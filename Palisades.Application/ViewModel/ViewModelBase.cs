using System.Collections.ObjectModel;
using System.Linq;
using Palisades.Helpers;
using Palisades.Model;
using Palisades.Services;
using Palisades.View;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Xml.Serialization;
using Palisades.Serialization;
using Palisades.Properties;

namespace Palisades.ViewModel
{
    public abstract class ViewModelBase : INotifyPropertyChanged, IPalisadeViewModel, IDisposable
    {
        /// <summary>Sérialiseur partagé pour garantir un format XML compatible avec LoadPalisades (root/namespace cohérents).</summary>
        public static readonly XmlSerializer SharedSerializer = PalisadeXmlSerialization.PalisadeModelSerializer;

        protected readonly PalisadeModelBase Model;
        protected volatile bool ShouldSave;
        private readonly object _saveLock = new object();
        private readonly System.Threading.Timer? _saveTimer;

        /// <summary>Délai après le dernier <see cref="Save"/> avant écriture disque (regroupe les redimensionnements / déplacements rapides).</summary>
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(800);

        protected ViewModelBase(PalisadeModelBase model)
        {
            Model = model;
            _saveTimer = new System.Threading.Timer(_ => FlushSave(), null, Timeout.Infinite, Timeout.Infinite);
            EditCommand = new RelayCommand(() => PalisadesManager.OpenEditDialog(this));
        }

        #region Propriétés communes

        public string Identifier
        {
            get => Model.Identifier;
            set { Model.Identifier = value; OnPropertyChanged(); Save(); }
        }

        public string Name
        {
            get => Model.Name;
            set { Model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(TabBarLabel)); Save(); }
        }

        public virtual string TabBarLabel => Name;

        public int FenceX
        {
            get => Model.FenceX;
            set { Model.FenceX = value; OnPropertyChanged(); Save(); }
        }

        public int FenceY
        {
            get => Model.FenceY;
            set { Model.FenceY = value; OnPropertyChanged(); Save(); }
        }

        public int Width
        {
            get => Model.Width;
            set { Model.Width = value; OnPropertyChanged(); Save(); }
        }

        public int Height
        {
            get => Model.Height;
            set { Model.Height = value; OnPropertyChanged(); Save(); }
        }

        public string? GroupId { get => Model.GroupId; set { Model.GroupId = value; OnPropertyChanged(); Save(); } }
        public int TabOrder { get => Model.TabOrder; set { Model.TabOrder = value; OnPropertyChanged(); Save(); } }
        public PalisadeModelBase ModelBase => Model;

        #endregion

        #region Méthodes communes

        public void Save()
        {
            ShouldSave = true;
            _saveTimer?.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
        }

        public void Delete()
        {
            string saveDirectory = PDirectory.GetPalisadeDirectory(Identifier);
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
        }

        /// <summary>Écrit sur disque si <see cref="ShouldSave"/> ; appelé une fois après période d’inactivité (pas de boucle périodique).</summary>
        /// <remarks>
        /// Le timer appelle depuis le pool de threads : la sérialisation lit le modèle sur le thread UI via
        /// <see cref="Dispatcher.Invoke"/> pour éviter une course avec les mutations WPF (déplacement, redimensionnement).
        /// </remarks>
        private void FlushSave()
        {
            if (!ShouldSave) return;
            void WriteState()
            {
                if (!ShouldSave) return;
                lock (_saveLock)
                {
                    if (!ShouldSave) return;
                    try
                    {
                        string saveDirectory = PDirectory.GetPalisadeDirectory(Identifier);
                        PDirectory.WriteAtomicText(Path.Combine(saveDirectory, "state.xml"), writer => SharedSerializer.Serialize(writer, Model));
                        ShouldSave = false;
                    }
                    catch (Exception ex)
                    {
                        PalisadeDiagnostics.Log("ViewModelBase", "Sauvegarde impossible pour la palisade " + Identifier, ex);
                        // Conserver ShouldSave=true pour qu'un prochain Save() retente l'écriture.
                    }
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(WriteState);
            else
                WriteState();
        }

        #endregion

        #region Commandes communes

        public ICommand EditCommand { get; }

        public ICommand NewPalisadeCommand { get; } = new RelayCommand(() => PalisadesManager.CreatePalisade());

        public ICommand NewFolderPortalCommand { get; } = new RelayCommand(() => PalisadesManager.ShowCreateFolderPortalDialog());

        public ICommand NewTaskPalisadeCommand { get; } = new RelayCommand(() => PalisadesManager.ShowCreateTaskPalisadeDialog());

        public ICommand NewCalendarPalisadeCommand { get; } = new RelayCommand(() => PalisadesManager.ShowCreateCalendarPalisadeDialog());

        public ICommand NewMailPalisadeCommand { get; } = new RelayCommand(() => PalisadesManager.ShowCreateMailPalisadeDialog());

        public ICommand ManageZimbraAccountsCommand { get; } = new RelayCommand(() =>
        {
            var dialog = new ManageAccountsDialog();
            dialog.ShowDialog();
        });

        public ICommand DeletePalisadeCommand { get; } = new RelayCommand<string>(identifier => PalisadesManager.DeletePalisade(identifier));

        public ICommand OpenAboutCommand { get; } = new RelayCommand(() =>
        {
            var about = new About
            {
                DataContext = new AboutViewModel()
            };
            PalisadesManager.SetOwnerSafe(about, Application.Current.MainWindow);
            about.ShowDialog();
        });

        public ICommand SaveSnapshotCommand { get; } = new RelayCommand(() =>
        {
            var dialog = new SaveSnapshotDialog();
            PalisadesManager.SetOwnerSafe(dialog, Application.Current.MainWindow);
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SnapshotName))
                LayoutSnapshotService.SaveSnapshot(dialog.SnapshotName.Trim());
        });

        public ICommand ManageSnapshotsCommand { get; } = new RelayCommand(() =>
        {
            var dialog = new ManageSnapshotsDialog();
            PalisadesManager.SetOwnerSafe(dialog, Application.Current.MainWindow);
            dialog.ShowDialog();
        });

        public ICommand RestoreSnapshotCommand { get; } = new RelayCommand<string>(id =>
        {
            if (string.IsNullOrEmpty(id)) return;
            if (System.Windows.MessageBox.Show(Strings.RestoreLayoutConfirm, Strings.RestoreLayoutTitle,
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
                return;
            LayoutSnapshotService.RestoreSnapshot(id);
        });

        public ObservableCollection<LayoutSnapshot> RecentSnapshots { get; } = new();

        public void RefreshRecentSnapshots()
        {
            RecentSnapshots.Clear();
            foreach (var s in LayoutSnapshotService.ListSnapshots().Take(5))
                RecentSnapshots.Add(s);
        }

        #endregion

        public virtual void Dispose()
        {
            _saveTimer?.Dispose();
            if (ShouldSave) FlushSave();
        }

        protected static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) { action(); return; }
            if (dispatcher.CheckAccess()) action();
            else dispatcher.Invoke(action);
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
