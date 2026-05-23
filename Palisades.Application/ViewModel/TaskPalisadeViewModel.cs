using Palisades.Helpers;
using Palisades.Properties;
using Palisades.Model;
using Palisades.Services;
using Palisades.View;
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

namespace Palisades.ViewModel
{
    public class TaskPalisadeViewModel : ViewModelBase
    {
        private readonly TaskPalisadeModel _model;
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
                catch { return string.Empty; }
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

        public TaskPalisadeViewModel() : this(new TaskPalisadeModel { Name = Strings.TaskDefaultName, Width = 600, Height = 400 }, new CalDAVService(new CalDAVClient("https://localhost/", "", "")))
        { }

        public TaskPalisadeViewModel(TaskPalisadeModel model, ICalDAVService caldavService) : base(model)
        {
            _model = model;
            _caldavService = caldavService;

            Tasks.CollectionChanged += Tasks_CollectionChanged;
            _visibleTasksView.Filter += (_, e) =>
            {
                if (e.Item is CalDAVTask t)
                    e.Accepted = !IsTaskHiddenInUi(t);
                else
                    e.Accepted = true;
            };

            SelectTabCommand = new RelayCommand<TaskTabItem>(tab => { if (tab != null) SelectedTaskTab = tab; });
            ForceSyncCommand = new RelayCommand(async () => await SyncWithCalDAVAsync());
            AddTaskCommand = new RelayCommand(() =>
            {
                var newTask = new CalDAVTask(Strings.TaskNewTaskName)
                {
                    Description = Strings.TaskNewTaskDescription,
                    DueDate = DateTime.Today.AddDays(1)
                };
                if (HasMultipleLists && SelectedTaskTab != null)
                {
                    SelectedTaskTab.Tasks.Add(newTask);
                }
                else
                {
                    Tasks.Add(newTask);
                }
                SelectedTask = newTask;
            });
            HideTaskCommand = new RelayCommand<CalDAVTask>(task =>
            {
                var t = task ?? SelectedTask;
                if (t == null) return;
                RegisterTaskHiddenKeys(t);
                if (SelectedTask == t) SelectedTask = null;
                RefreshVisibleTasksFilter();
                OnPropertyChanged(nameof(HasNoTasks));
            });
            ToggleTaskCompletedCommand = new RelayCommand<CalDAVTask>(async task =>
            {
                var t = task ?? SelectedTask;
                if (t == null) return;
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
            SaveTaskCommand = new RelayCommand<CalDAVTask>(async task =>
            {
                var t = task ?? SelectedTask;
                if (t == null) return;
                if (!string.IsNullOrEmpty(t.CalDAVId))
                    return;
                var listId = GetListIdForTask(t);
                try
                {
                    t.LastModified = DateTime.Now;
                    var createdTask = await _caldavService.CreateTaskAsync(listId, t);
                    t.CalDAVId = createdTask.CalDAVId;
                    t.CalDAVEtag = createdTask.CalDAVEtag;
                    if (!string.IsNullOrEmpty(createdTask.Uid))
                        t.Uid = createdTask.Uid;
                    SyncStatus = Strings.TaskSavedSuccess;
                }
                catch (Exception ex)
                {
                    ErrorMessage = string.Format(CultureInfo.CurrentCulture, Strings.TaskSaveFailedFormat, ex.Message);
                }
            });

            _visibleTasksView.Source = Tasks;
            RefreshVisibleTasksFilter();

            var hasListIds = _model.TaskListIds != null && _model.TaskListIds.Count > 0;
            var hasLegacyId = !string.IsNullOrEmpty(_model.TaskListId);
            if (!string.IsNullOrEmpty(_model.CalDAVUrl) && (hasListIds || hasLegacyId))
            {
                _ = LoadTasksAsync();
            }

            StartSyncTimer();
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

        private string GetDisplayNameForListId(string href)
        {
            if (string.IsNullOrEmpty(href)) return Strings.TaskListDefaultName;
            var idx = href.TrimEnd('/').LastIndexOf('/');
            return idx >= 0 ? href.Substring(idx + 1).TrimEnd('/') : href;
        }

        public async Task LoadTasksAsync()
        {
            var listIds = GetListIds().ToList();
            if (listIds.Count == 0 || string.IsNullOrEmpty(CalDAVUrl))
            {
                Dispatch(() => { ErrorMessage = Strings.CaldavIncomplete; });
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
            Save();
            if (!_isSyncing)
            {
                SyncStatus = Strings.SyncLocalChanges;
            }
        }

        private void StartSyncTimer()
        {
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
                if (listIds.Count == 0 || string.IsNullOrEmpty(CalDAVUrl))
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

        public ICommand ShowSettingsCommand { get; } = new RelayCommand<TaskPalisadeViewModel>(viewModel =>
        {
            var settings = new TaskPalisadeSettingsDialog { DataContext = viewModel };
            try { settings.Owner = PalisadesManager.GetWindow(viewModel.Identifier); } catch { }
            settings.ShowDialog();
        });

        public ICommand SelectTabCommand { get; }
        public ICommand ForceSyncCommand { get; }
        public ICommand AddTaskCommand { get; }
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
        }
    }
}
