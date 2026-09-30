using Naultinus.Helpers;
using Naultinus.Properties;
using Naultinus.Model;
using Naultinus.Services;
using Naultinus.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Naultinus.ViewModel
{
    public class TaskNaultinusViewModel : ViewModelBase
    {
        private readonly TaskNaultinusModel _model;
        private readonly ICalDAVService _caldavService;
        private CalDAVTask? _selectedTask;
        private string _errorMessage = string.Empty;
        private bool _isSyncing;
        private bool _isLoading;
        private string _syncStatus = Strings.SyncReady;
        private Timer? _syncTimer;
        private int _syncInProgress;
        private bool _disposed;
        private readonly CollectionViewSource _visibleTasksView = new();
        private readonly bool _sharedAccountConfigured;
        private bool _suppressTaskPersistence;
        private string _quickAddTitle = string.Empty;
        private readonly HashSet<CalDAVTask> _uploadsInFlight = new();

        public string CalDAVUrl
        {
            get { return _model.CalDAVUrl ?? string.Empty; }
            set { _model.CalDAVUrl = value; OnPropertyChanged(); Save(); }
        }

        public string CalDAVUsername
        {
            get { return _model.CalDAVUsername ?? string.Empty; }
            set { _model.CalDAVUsername = value; OnPropertyChanged(); Save(); }
        }

        public string CalDAVPassword
        {
            get
            {
                if (string.IsNullOrEmpty(_model.CalDAVPassword))
                    return string.Empty;
                try { return CredentialEncryptor.Decrypt(_model.CalDAVPassword); }
                catch (Exception ex) { NaultinusDiagnostics.LogDebug("TaskNaultinus.CalDAVPassword", ex); return string.Empty; }
            }
            set
            {
                _model.CalDAVPassword = string.IsNullOrEmpty(value) ? string.Empty : CredentialEncryptor.Encrypt(value);
                OnPropertyChanged();
                Save();
            }
        }

        public string TaskListId
        {
            get { return _model.TaskListId ?? string.Empty; }
            set { _model.TaskListId = value; OnPropertyChanged(); Save(); }
        }

        public int SyncIntervalMinutes
        {
            get { return _model.SyncIntervalMinutes > 0 ? _model.SyncIntervalMinutes : 5; }
            set { _model.SyncIntervalMinutes = value; OnPropertyChanged(); Save(); }
        }

        public bool EnableLogging
        {
            get { return _model.EnableLogging; }
            set { _model.EnableLogging = value; OnPropertyChanged(); Save(); }
        }

        public bool ShowCompletedTasks
        {
            get { return _model.ShowCompletedTasks; }
            set { _model.ShowCompletedTasks = value; OnPropertyChanged(); Save(); }
        }

        /// <summary>
        /// Applique les réglages du dialogue. Un intervalle hors plage est refusé : rien n'est écrit.
        /// Si l'intervalle change, le minuteur de synchro est réarmé avec la nouvelle période.
        /// </summary>
        public bool TryApplySettings(int syncIntervalMinutes, bool enableLogging, bool showCompletedTasks)
        {
            int previousInterval = _model.SyncIntervalMinutes;
            if (!TaskNaultinusSettings.TryApply(_model, syncIntervalMinutes, enableLogging, showCompletedTasks))
                return false;

            OnPropertyChanged(nameof(SyncIntervalMinutes));
            OnPropertyChanged(nameof(EnableLogging));
            OnPropertyChanged(nameof(ShowCompletedTasks));
            Save();
            if (previousInterval != _model.SyncIntervalMinutes)
                StartSyncTimer();
            return true;
        }

        /// <summary>Synchro seulement si le compte partagé existe et qu'une liste distante est choisie.</summary>
        public bool IsRemote => LocalPlannerStore.UsesRemoteTasks(_model, _sharedAccountConfigured);

        public bool IsLocalMode => !IsRemote;

        public string RemoveTaskToolTip => IsLocalMode ? Strings.TooltipDelete : Strings.TooltipHideTask;

        public ObservableCollection<CalDAVTask> Tasks { get; set; } = new ObservableCollection<CalDAVTask>();

        public ObservableCollection<CalDAVTask> ActiveTasks =>
            HasMultipleLists && SelectedTaskTab != null ? SelectedTaskTab.Tasks : Tasks;

        public ICollectionView FilteredActiveTasks => _visibleTasksView.View;

        public ObservableCollection<TaskTabItem> TaskTabs { get; } = new ObservableCollection<TaskTabItem>();
        public bool HasMultipleLists => TaskTabs.Count > 1;

        public bool HasNoTasks
        {
            get
            {
                if (IsLoading) return false;
                foreach (var t in ActiveTasks)
                {
                    if (!IsTaskHiddenInUi(t))
                        return false;
                }
                return true;
            }
        }

        private TaskTabItem? _selectedTaskTab;
        public TaskTabItem? SelectedTaskTab
        {
            get => _selectedTaskTab;
            set
            {
                _selectedTaskTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveTasks));
                RefreshVisibleTasksFilter();
                OnPropertyChanged(nameof(HasNoTasks));
            }
        }

        public CalDAVTask? SelectedTask
        {
            get => _selectedTask;
            set { _selectedTask = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public bool IsSyncing
        {
            get => _isSyncing;
            set { _isSyncing = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoTasks)); }
        }

        public string SyncStatus
        {
            get => _syncStatus;
            set { _syncStatus = value; OnPropertyChanged(); }
        }

        /// <summary>Titre en cours de saisie dans le champ en bas de la liste. Entrée le confirme.</summary>
        public string QuickAddTitle
        {
            get => _quickAddTitle;
            set
            {
                var next = value ?? string.Empty;
                if (_quickAddTitle == next)
                    return;
                _quickAddTitle = next;
                OnPropertyChanged();
            }
        }

        /// <summary>Le bouton « + Tâche » demande le focus du champ, sans créer de tâche.</summary>
        public event EventHandler? QuickAddFocusRequested;

        public TaskNaultinusViewModel() : this(new TaskNaultinusModel { Name = Strings.TaskDefaultName, Width = 600, Height = 400 }, new CalDAVService(new CalDAVClient("https://localhost/", "", "")))
        { }

        public TaskNaultinusViewModel(TaskNaultinusModel model, ICalDAVService caldavService)
            : this(model, caldavService, SharedCalDavAccount.IsConfigured(), startBackgroundWork: true)
        {
        }

        /// <summary>
        /// Constructeur de test : le compte partagé et le chargement réseau sont injectés,
        /// pour exercer la création rapide sans CalDAV réel.
        /// </summary>
        internal TaskNaultinusViewModel(TaskNaultinusModel model, ICalDAVService caldavService, bool sharedAccountConfigured, bool startBackgroundWork) : base(model)
        {
            _model = model;
            _caldavService = caldavService;
            _sharedAccountConfigured = sharedAccountConfigured;

            Tasks.CollectionChanged += Tasks_CollectionChanged;
            _visibleTasksView.Filter += (_, e) =>
            {
                if (e.Item is CalDAVTask t)
                    e.Accepted = !IsTaskHiddenInUi(t);
                else
                    e.Accepted = true;
            };

            SelectTabCommand = new RelayCommand<TaskTabItem>(tab => { if (tab != null) SelectedTaskTab = tab; });
            ForceSyncCommand = new AsyncRelayCommand(() => SyncWithCalDAVAsync());
            AddTaskCommand = new RelayCommand(RequestQuickAddFocus);
            ConfirmQuickAddCommand = new AsyncRelayCommand(ConfirmQuickAddAsync);
            EditTaskCommand = new RelayCommand<CalDAVTask>(task => EditTask(task ?? SelectedTask));
            HideTaskCommand = new RelayCommand<CalDAVTask>(task =>
            {
                var t = task ?? SelectedTask;
                if (t == null) return;
                if (IsLocalMode)
                {
                    DeleteLocalTask(t);
                    return;
                }

                RegisterTaskHiddenKeys(t);
                if (SelectedTask == t) SelectedTask = null;
                RefreshVisibleTasksFilter();
                OnPropertyChanged(nameof(HasNoTasks));
            });
            ToggleTaskCompletedCommand = new AsyncRelayCommand<CalDAVTask>(async task =>
            {
                var t = task ?? SelectedTask;
                if (t == null) return;
                if (IsLocalMode)
                {
                    t.Completed = !t.Completed;
                    t.CompletedDate = t.Completed ? DateTime.Now : null;
                    t.LastModified = DateTime.Now;
                    if (!LocalPlannerStore.TryUpdateTask(_model, LocalPlannerStore.ToStoredTask(t), out var localError))
                        ErrorMessage = LocalPlannerStore.Describe(localError);
                    else
                        Save();
                    return;
                }

                var listId = GetListIdForTask(t);
                t.Completed = !t.Completed;
                t.CompletedDate = t.Completed ? DateTime.Now : null;
                t.LastModified = DateTime.Now;
                try
                {
                    if (!string.IsNullOrEmpty(t.CalDAVId))
                        await _caldavService.UpdateTaskAsync(listId, t);
                }
                catch (Exception ex)
                {
                    ErrorMessage = string.Format(CultureInfo.CurrentCulture, Strings.TaskUpdateFailedFormat, ex.Message);
                    t.Completed = !t.Completed;
                    t.CompletedDate = t.Completed ? DateTime.Now : null;
                }
            });
            SaveTaskCommand = new AsyncRelayCommand<CalDAVTask>(async task =>
            {
                var t = task ?? SelectedTask;
                if (t == null)
                    return;
                await UploadNewTaskAsync(t);
            });

            _visibleTasksView.Source = Tasks;
            RefreshVisibleTasksFilter();

            if (IsRemote)
            {
                if (startBackgroundWork)
                {
                    _ = LoadTasksAsync();
                    StartSyncTimer();
                }
            }
            else
            {
                ReloadLocalTasks();
                if (HasRemoteListConfigured)
                    ErrorMessage = Strings.SharedCalDavMissing;
            }
        }

        private bool HasRemoteListConfigured =>
            (_model.TaskListIds != null && _model.TaskListIds.Any(id => !string.IsNullOrWhiteSpace(id)))
            || !string.IsNullOrWhiteSpace(_model.TaskListId);

        private void ReloadLocalTasks()
        {
            _suppressTaskPersistence = true;
            try
            {
                Tasks.Clear();
                if (_model.LocalTasks == null)
                    return;
                foreach (var stored in _model.LocalTasks)
                    Tasks.Add(LocalPlannerStore.ToUiTask(stored));
            }
            finally
            {
                _suppressTaskPersistence = false;
            }

            RefreshVisibleTasksFilter();
            OnPropertyChanged(nameof(HasNoTasks));
        }

        private void RequestQuickAddFocus()
        {
            QuickAddFocusRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Crée une tâche dont le titre est le texte saisi. Une saisie vide ou blanche ne fait rien.
        /// Le mode local l'enregistre dans le state.xml. Une liste CalDAV l'envoie dans la foulée
        /// (CreateTaskAsync). Si cet envoi échoue, la ligne reste et le bouton 💾 permet de réessayer.
        /// </summary>
        private async Task ConfirmQuickAddAsync()
        {
            if (string.IsNullOrWhiteSpace(_quickAddTitle))
                return;

            if (!LocalPlannerStore.TryBuildTask(_quickAddTitle, string.Empty, null, null, default, false, null, out var built, out var error) || built == null)
            {
                ErrorMessage = LocalPlannerStore.Describe(error);
                return;
            }

            if (IsLocalMode)
            {
                if (!LocalPlannerStore.TryAddTask(_model, built, out error))
                {
                    ErrorMessage = LocalPlannerStore.Describe(error);
                    return;
                }

                QuickAddTitle = string.Empty;
                ErrorMessage = string.Empty;
                Save();
                ReloadLocalTasks();
                SelectedTask = Tasks.FirstOrDefault(t => t.Id == built.Id);
                return;
            }

            var created = new CalDAVTask(built.Title);
            if (HasMultipleLists && SelectedTaskTab != null)
                SelectedTaskTab.Tasks.Add(created);
            else
                Tasks.Add(created);

            QuickAddTitle = string.Empty;
            ErrorMessage = string.Empty;
            SelectedTask = created;
            await UploadNewTaskAsync(created);
        }

        /// <summary>
        /// Envoie une tâche encore sans identifiant serveur. Le bouton 💾 et Entrée partagent ce chemin.
        /// Un second appel pendant l'envoi ne crée pas une autre tâche. L'échec laisse CalDAVId vide.
        /// </summary>
        private async Task UploadNewTaskAsync(CalDAVTask task)
        {
            if (IsLocalMode || !string.IsNullOrEmpty(task.CalDAVId))
                return;
            if (!_uploadsInFlight.Add(task))
                return;

            try
            {
                var listId = GetListIdForTask(task);
                task.LastModified = DateTime.Now;
                var createdTask = await _caldavService.CreateTaskAsync(listId, task);
                task.CalDAVId = createdTask.CalDAVId;
                task.CalDAVEtag = createdTask.CalDAVEtag;
                if (!string.IsNullOrEmpty(createdTask.Uid))
                    task.Uid = createdTask.Uid;
                ErrorMessage = string.Empty;
                SyncStatus = Strings.TaskSavedSuccess;
            }
            catch (Exception ex)
            {
                ErrorMessage = string.Format(CultureInfo.CurrentCulture, Strings.TaskSaveFailedFormat, ex.Message);
            }
            finally
            {
                _uploadsInFlight.Remove(task);
            }
        }

        /// <summary>
        /// Ouvre le même dialogue pour une tâche locale ou CalDAV. Annuler ne change rien.
        /// Confirmer enregistre en local, ou met à jour cette tâche sur le serveur.
        /// </summary>
        private void EditTask(CalDAVTask? task)
        {
            if (task == null)
                return;
            var dialog = new EditLocalTaskDialog(task);
            try { dialog.Owner = NaultinusManager.GetWindow(Identifier); }
            catch (KeyNotFoundException) { /* fenêtre non enregistrée : dialogue sans owner */ }
            if (dialog.ShowDialog() != true || dialog.Result == null)
                return;
            _ = CommitTaskEditAsync(task, dialog.Result);
        }

        /// <summary>
        /// Applique une édition déjà confirmée. Le dialogue n'est pas rouvert ici :
        /// une annulation ne doit pas appeler cette méthode.
        /// </summary>
        internal Task CommitTaskEditAsync(CalDAVTask task, StoredLocalTask edited)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(edited);
            if (IsLocalMode)
            {
                ApplyLocalTaskEdit(task, edited);
                return Task.CompletedTask;
            }

            return ApplyRemoteTaskEditAsync(task, edited);
        }

        private void ApplyLocalTaskEdit(CalDAVTask task, StoredLocalTask edited)
        {
            if (!LocalPlannerStore.TryUpdateTask(_model, edited, out var error))
            {
                ErrorMessage = LocalPlannerStore.Describe(error);
                return;
            }

            var updated = LocalPlannerStore.ToUiTask(edited);
            var index = Tasks.IndexOf(task);
            if (index >= 0)
                Tasks[index] = updated;
            if (SelectedTask == task)
                SelectedTask = updated;
            Save();
            RefreshVisibleTasksFilter();
        }

        /// <summary>
        /// Met à jour cette tâche sur le serveur (UpdateTaskAsync), sans la recréer.
        /// Une tâche pas encore envoyée (CalDAVId vide) garde seulement les champs saisis :
        /// le bouton 💾 reste le chemin de création.
        /// </summary>
        private async Task ApplyRemoteTaskEditAsync(CalDAVTask task, StoredLocalTask edited)
        {
            var previousTitle = task.Title;
            var previousDescription = task.Description;
            var previousDue = task.DueDate;
            var previousModified = task.LastModified;

            task.Title = edited.Title ?? string.Empty;
            task.Description = edited.Description ?? string.Empty;
            task.DueDate = edited.DueDate;
            task.LastModified = edited.LastModified == default ? DateTime.Now : edited.LastModified;

            if (string.IsNullOrEmpty(task.CalDAVId))
                return;

            try
            {
                await _caldavService.UpdateTaskAsync(GetListIdForTask(task), task);
                ErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                task.Title = previousTitle;
                task.Description = previousDescription;
                task.DueDate = previousDue;
                task.LastModified = previousModified;
                ErrorMessage = string.Format(CultureInfo.CurrentCulture, Strings.TaskUpdateFailedFormat, ex.Message);
            }
        }

        private void DeleteLocalTask(CalDAVTask task)
        {
            if (!LocalPlannerStore.TryRemoveTask(_model, task.Id, out var error))
            {
                ErrorMessage = LocalPlannerStore.Describe(error);
                return;
            }

            if (SelectedTask == task)
                SelectedTask = null;
            Save();
            Tasks.Remove(task);
            RefreshVisibleTasksFilter();
            OnPropertyChanged(nameof(HasNoTasks));
        }

        private static IEnumerable<string> GetTaskHideKeys(CalDAVTask t)
        {
            yield return "id:" + t.Id;
            if (!string.IsNullOrEmpty(t.CalDAVId))
                yield return "caldav:" + t.CalDAVId;
            if (!string.IsNullOrEmpty(t.Uid))
                yield return "uid:" + t.Uid;
        }

        private bool IsTaskHiddenInUi(CalDAVTask t)
        {
            if (_model.HiddenTaskKeys == null || _model.HiddenTaskKeys.Count == 0)
                return false;
            foreach (var key in GetTaskHideKeys(t))
            {
                if (_model.HiddenTaskKeys.Contains(key))
                    return true;
            }
            return false;
        }

        private void RegisterTaskHiddenKeys(CalDAVTask t)
        {
            _model.HiddenTaskKeys ??= new List<string>();
            foreach (var key in GetTaskHideKeys(t))
            {
                if (!_model.HiddenTaskKeys.Contains(key))
                    _model.HiddenTaskKeys.Add(key);
            }
            Save();
        }

        private void RefreshVisibleTasksFilter()
        {
            var src = HasMultipleLists && SelectedTaskTab != null ? (object)SelectedTaskTab.Tasks : Tasks;
            if (!ReferenceEquals(_visibleTasksView.Source, src))
                _visibleTasksView.Source = src;
            _visibleTasksView.View?.Refresh();
            OnPropertyChanged(nameof(FilteredActiveTasks));
        }

        private string GetListIdForTask(CalDAVTask task)
        {
            if (TaskTabs.Count > 1)
            {
                foreach (var tab in TaskTabs)
                    if (tab.Tasks.Contains(task))
                        return tab.ListId;
            }
            return TaskListId;
        }

        private IEnumerable<string> GetListIds()
        {
            if (_model.TaskListIds != null && _model.TaskListIds.Count > 0)
                return _model.TaskListIds;
            if (!string.IsNullOrEmpty(_model.TaskListId))
                return new[] { _model.TaskListId };
            return Array.Empty<string>();
        }

        private static string GetDisplayNameForListId(string href)
        {
            if (string.IsNullOrEmpty(href)) return Strings.TaskListDefaultName;
            var idx = href.TrimEnd('/').LastIndexOf('/');
            return idx >= 0 ? href.Substring(idx + 1).TrimEnd('/') : href;
        }

        public async Task LoadTasksAsync()
        {
            var listIds = GetListIds().ToList();
            if (!IsRemote || listIds.Count == 0)
            {
                Dispatch(() => { ErrorMessage = Strings.SharedCalDavMissing; });
                return;
            }

            try
            {
                Dispatch(() => { IsLoading = true; ErrorMessage = string.Empty; SyncStatus = Strings.SyncLoadingTasks; });

                if (listIds.Count > 1)
                {
                    Dispatch(() => TaskTabs.Clear());
                    foreach (var listId in listIds)
                    {
                        var tasks = await _caldavService.GetTasksAsync(listId);
                        var tab = new TaskTabItem
                        {
                            ListId = listId,
                            DisplayName = GetDisplayNameForListId(listId)
                        };
                        foreach (var t in tasks)
                            tab.Tasks.Add(t);
                        Dispatch(() =>
                        {
                            TaskTabs.Add(tab);
                            OnPropertyChanged(nameof(HasMultipleLists));
                            if (SelectedTaskTab == null && TaskTabs.Count > 0)
                                SelectedTaskTab = TaskTabs[0];
                        });
                    }
                    Dispatch(() =>
                    {
                        SyncStatus = Strings.SyncTasksLoaded;
                        OnPropertyChanged(nameof(ActiveTasks));
                        RefreshVisibleTasksFilter();
                        OnPropertyChanged(nameof(HasNoTasks));
                    });
                }
                else
                {
                    var singleId = listIds[0];
                    var tasks = await _caldavService.GetTasksAsync(singleId);
                    Dispatch(() =>
                    {
                        Tasks.Clear();
                        foreach (var task in tasks)
                            Tasks.Add(task);
                        SyncStatus = Strings.SyncTasksLoaded;
                        OnPropertyChanged(nameof(ActiveTasks));
                        RefreshVisibleTasksFilter();
                        OnPropertyChanged(nameof(HasNoTasks));
                    });
                }
            }
            catch (Exception ex)
            {
                var msg = string.Format(CultureInfo.CurrentCulture, Strings.SyncLoadFailedFormat, ex.Message);
                Dispatch(() =>
                {
                    ErrorMessage = msg;
                    SyncStatus = Strings.SyncLoadError;
                    OnPropertyChanged(nameof(ActiveTasks));
                    RefreshVisibleTasksFilter();
                    OnPropertyChanged(nameof(HasNoTasks));
                });
            }
            finally
            {
                Dispatch(() => { IsLoading = false; OnPropertyChanged(nameof(HasNoTasks)); });
            }
        }

        private void Tasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressTaskPersistence)
                return;
            Save();
            if (!_isSyncing)
            {
                SyncStatus = Strings.SyncLocalChanges;
            }

            OnPropertyChanged(nameof(HasNoTasks));
        }

        private void StartSyncTimer()
        {
            _syncTimer?.Dispose();
            var syncInterval = TimeSpan.FromMinutes(SyncIntervalMinutes);
            _syncTimer = new Timer(async _ =>
            {
                if (_disposed)
                    return;

                await SyncWithCalDAVAsync();
            }, null, syncInterval, syncInterval);
        }

        public async Task SyncWithCalDAVAsync()
        {
            if (_disposed || Interlocked.Exchange(ref _syncInProgress, 1) == 1)
                return;

            try
            {
                var listIds = GetListIds().ToList();
                if (!IsRemote || listIds.Count == 0)
                    return;

                Dispatch(() => { IsSyncing = true; SyncStatus = Strings.SyncWithCalDav; ErrorMessage = string.Empty; });

                if (TaskTabs.Count > 1)
                {
                    foreach (var tab in TaskTabs)
                    {
                        var snapshot = await Application.Current.Dispatcher.InvokeAsync(() => new List<CalDAVTask>(tab.Tasks)).Task;
                        var merged = await _caldavService.SyncTasksAsync(tab.ListId, snapshot);
                        Dispatch(() =>
                        {
                            tab.Tasks.Clear();
                            foreach (var t in merged)
                                tab.Tasks.Add(t);
                        });
                    }
                    Dispatch(() =>
                    {
                        SyncStatus = string.Format(CultureInfo.CurrentCulture, Strings.SyncCompletedFormat, DateTime.Now.ToShortTimeString());
                        RefreshVisibleTasksFilter();
                        OnPropertyChanged(nameof(HasNoTasks));
                    });
                }
                else
                {
                    var tasksSnapshot = await Application.Current.Dispatcher.InvokeAsync(() => new List<CalDAVTask>(Tasks)).Task;
                    var merged = await _caldavService.SyncTasksAsync(TaskListId, tasksSnapshot);
                    Dispatch(() =>
                    {
                        Tasks.Clear();
                        foreach (var t in merged)
                            Tasks.Add(t);
                        SyncStatus = string.Format(CultureInfo.CurrentCulture, Strings.SyncCompletedFormat, DateTime.Now.ToShortTimeString());
                        RefreshVisibleTasksFilter();
                        OnPropertyChanged(nameof(HasNoTasks));
                    });
                }
            }
            catch (Exception ex)
            {
                var msg = string.Format(CultureInfo.CurrentCulture, Strings.SyncFailedFormat, ex.Message);
                Dispatch(() => { ErrorMessage = msg; SyncStatus = Strings.SyncError; });
            }
            finally
            {
                Dispatch(() => { IsSyncing = false; });
                Interlocked.Exchange(ref _syncInProgress, 0);
            }
        }

        public ICommand ShowSettingsCommand { get; } = new RelayCommand<TaskNaultinusViewModel>(viewModel =>
        {
            var settings = new TaskNaultinusSettingsDialog(viewModel);
            try { settings.Owner = NaultinusManager.GetWindow(viewModel.Identifier); }
            catch (KeyNotFoundException) { /* fenêtre non enregistrée : dialogue sans owner */ }
            settings.ShowDialog();
        });

        public ICommand SelectTabCommand { get; }
        public ICommand ForceSyncCommand { get; }
        public ICommand AddTaskCommand { get; }
        public ICommand ConfirmQuickAddCommand { get; }
        public ICommand EditTaskCommand { get; }
        public ICommand HideTaskCommand { get; }
        public ICommand ToggleTaskCompletedCommand { get; }
        public ICommand SaveTaskCommand { get; }

        public override void Dispose()
        {
            _disposed = true;
            _syncTimer?.Dispose();
            _syncTimer = null;
            (_caldavService as IDisposable)?.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
