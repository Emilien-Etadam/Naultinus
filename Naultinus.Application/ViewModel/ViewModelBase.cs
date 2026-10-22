using System.Collections.ObjectModel;
using System.Linq;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using Naultinus.View;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using Naultinus.Serialization;
using Naultinus.Properties;

namespace Naultinus.ViewModel
{
    public abstract class ViewModelBase : INotifyPropertyChanged, INaultinusViewModel, IDisposable
    {
        /// <summary>Sérialiseur partagé pour garantir un format XML compatible avec LoadNaultinus (root/namespace cohérents).</summary>
        public static readonly XmlSerializer SharedSerializer = NaultinusXmlSerialization.NaultinusModelSerializer;

        protected readonly NaultinusModelBase Model;
        protected volatile bool ShouldSave;
        private readonly object _saveLock = new object();
        private readonly System.Threading.Timer? _saveTimer;

        /// <summary>Délai après le dernier <see cref="Save"/> avant écriture disque (regroupe les redimensionnements / déplacements rapides).</summary>
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(800);

        protected ViewModelBase(NaultinusModelBase model)
        {
            Model = model;
            _saveTimer = new System.Threading.Timer(_ => FlushSave(), null, Timeout.Infinite, Timeout.Infinite);
            EditCommand = new RelayCommand(() => NaultinusManager.OpenEditDialog(this));
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
        public NaultinusModelBase ModelBase => Model;

        /// <summary>
        /// Quand vrai, les pinceaux de fond corps utilisent une alpha 255 (vue shell / HwndHost).
        /// </summary>
        private bool _forceOpaqueBody;

        public bool ForceOpaqueBody
        {
            get => _forceOpaqueBody;
            set
            {
                if (_forceOpaqueBody == value) return;
                _forceOpaqueBody = value;
                NotifyAppearanceBrushesChanged();
            }
        }

        public string HeaderColor
        {
            get => Model.HeaderColor ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (Model.HeaderColor == next) return;
                Model.HeaderColor = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomHeaderColor));
                NotifyTintPropertiesChanged();
                Save();
            }
        }

        public string BodyColor
        {
            get => Model.BodyColor ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (Model.BodyColor == next) return;
                Model.BodyColor = next;
                // Premier choix de couleur : teinte semi-transparente comme Fences.
                if (!string.IsNullOrEmpty(next)
                    && NaultinusAppearanceColors.ClampOpacityPercent(Model.BodyOpacity) >= 100)
                {
                    Model.BodyOpacity = NaultinusAppearanceColors.PreferredTintOpacityPercent;
                    OnPropertyChanged(nameof(BodyOpacity));
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomBodyColor));
                NotifyTintPropertiesChanged();
                Save();
            }
        }

        public string TitleColor
        {
            get => Model.TitleColor ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (Model.TitleColor == next) return;
                Model.TitleColor = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomTitleColor));
                OnPropertyChanged(nameof(TitleForegroundBrush));
                Save();
            }
        }

        public string LabelsColor
        {
            get => Model.LabelsColor ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (Model.LabelsColor == next) return;
                Model.LabelsColor = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomLabelsColor));
                OnPropertyChanged(nameof(LabelsForegroundBrush));
                Save();
            }
        }

        public int BodyOpacity
        {
            get => NaultinusAppearanceColors.ClampOpacityPercent(Model.BodyOpacity);
            set
            {
                int next = NaultinusAppearanceColors.ClampOpacityPercent(value);
                if (Model.BodyOpacity == next) return;
                Model.BodyOpacity = next;
                OnPropertyChanged();
                NotifyTintPropertiesChanged();
                Save();
            }
        }

        public bool HasCustomHeaderColor => NaultinusAppearanceColors.TryParseRgb(HeaderColor, out _);
        public bool HasCustomBodyColor => NaultinusAppearanceColors.TryParseRgb(BodyColor, out _);
        public bool HasCustomTitleColor => NaultinusAppearanceColors.TryParseRgb(TitleColor, out _);
        public bool HasCustomLabelsColor => NaultinusAppearanceColors.TryParseRgb(LabelsColor, out _);

        /// <summary>Fond teinté (couleur choisie ou noir si seule l'opacité est baissée).</summary>
        public bool UsesBodyTint => HasCustomBodyColor || BodyOpacity < 100;

        /// <summary>
        /// Demande un vrai fond transparent (opacité &lt; 100 %). Incompatible avec l'hôte shell :
        /// la fenêtre doit alors rester en grille WPF + <c>AllowsTransparency</c>.
        /// </summary>
        public bool WantsTranslucentBody => BodyOpacity < 100;

        /// <summary>
        /// Couleur de teinte opaque. L'intensité passe par <see cref="BodyTintOpacity"/>
        /// (Opacity UIElement), fiable sur fenêtre layered — pas l'alpha du brush.
        /// </summary>
        public Brush BodyTintBrush
        {
            get
            {
                string tintHex = HasCustomBodyColor
                    ? BodyColor
                    : NaultinusAppearanceColors.DefaultTintRgbHex;
                return NaultinusAppearanceColors.TryCreateOpaqueBrush(tintHex)
                    ?? Brushes.Black;
            }
        }

        /// <summary>0.0–1.0 pour <c>UIElement.Opacity</c> de la couche de teinte.</summary>
        public double BodyTintOpacity =>
            UsesBodyTint ? NaultinusAppearanceColors.PercentToOpacity(BodyOpacity) : 0.0;

        public Brush HeaderTintBrush
        {
            get
            {
                if (HasCustomHeaderColor)
                {
                    return NaultinusAppearanceColors.TryCreateOpaqueBrush(HeaderColor)
                        ?? NaultinusAppearanceColors.ThemeBrush("NaultinusWindowBrush", SystemColors.WindowBrush);
                }

                return BodyTintBrush;
            }
        }

        /// <summary>
        /// Fond interactif de l'en-tête (hors couche de teinte).
        /// Avec teinte : brush A=1 (pas Transparent) pour garder DragMove sur fenêtre layered.
        /// </summary>
        public Brush HeaderChromeBrush =>
            UsesBodyTint
                ? NaultinusAppearanceColors.HitTestableClear
                : NaultinusAppearanceColors.ThemeBrush("NaultinusWindowBrush", SystemColors.WindowBrush);

        public Brush HeaderBackgroundBrush =>
            UsesBodyTint ? HeaderTintBrush : HeaderChromeBrush;

        public Brush BodyBackgroundBrush =>
            UsesBodyTint
                ? BodyTintBrush
                : NaultinusAppearanceColors.ThemeBrush("NaultinusControlBrush", SystemColors.ControlBrush);

        public Brush BodyOverlayBrush => Brushes.Transparent;

        public Brush TitleForegroundBrush =>
            NaultinusAppearanceColors.TryCreateBrush(TitleColor, opacityPercent: 100, forceOpaque: true)
            ?? NaultinusAppearanceColors.ThemeBrush("NaultinusTextBrush", SystemColors.WindowTextBrush);

        public Brush LabelsForegroundBrush =>
            NaultinusAppearanceColors.TryCreateBrush(LabelsColor, opacityPercent: 100, forceOpaque: true)
            ?? NaultinusAppearanceColors.ThemeBrush("NaultinusSubtleBrush", SystemColors.GrayTextBrush);

        public void ResetAppearance()
        {
            Model.HeaderColor = string.Empty;
            Model.BodyColor = string.Empty;
            Model.TitleColor = string.Empty;
            Model.LabelsColor = string.Empty;
            Model.BodyOpacity = NaultinusAppearanceColors.DefaultBodyOpacityPercent;
            OnPropertyChanged(nameof(HeaderColor));
            OnPropertyChanged(nameof(BodyColor));
            OnPropertyChanged(nameof(TitleColor));
            OnPropertyChanged(nameof(LabelsColor));
            OnPropertyChanged(nameof(BodyOpacity));
            OnPropertyChanged(nameof(HasCustomHeaderColor));
            OnPropertyChanged(nameof(HasCustomBodyColor));
            OnPropertyChanged(nameof(HasCustomTitleColor));
            OnPropertyChanged(nameof(HasCustomLabelsColor));
            NotifyTintPropertiesChanged();
            Save();
        }

        private void NotifyTintPropertiesChanged()
        {
            OnPropertyChanged(nameof(UsesBodyTint));
            OnPropertyChanged(nameof(WantsTranslucentBody));
            OnPropertyChanged(nameof(BodyTintBrush));
            OnPropertyChanged(nameof(BodyTintOpacity));
            OnPropertyChanged(nameof(HeaderTintBrush));
            OnPropertyChanged(nameof(HeaderChromeBrush));
            OnPropertyChanged(nameof(HeaderBackgroundBrush));
            OnPropertyChanged(nameof(BodyBackgroundBrush));
            OnPropertyChanged(nameof(BodyOverlayBrush));
            OnPropertyChanged(nameof(TitleForegroundBrush));
            OnPropertyChanged(nameof(LabelsForegroundBrush));
        }

        private void NotifyAppearanceBrushesChanged() => NotifyTintPropertiesChanged();

        #endregion

        #region Méthodes communes

        public void Save()
        {
            ShouldSave = true;
            _saveTimer?.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
        }

        public void Delete()
        {
            string saveDirectory = AppPaths.GetNaultinusDirectory(Identifier);
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
                        string saveDirectory = AppPaths.GetNaultinusDirectory(Identifier);
                        NaultinusStateFile.Write(saveDirectory, Model);
                        ShouldSave = false;
                    }
                    catch (Exception ex)
                    {
                        NaultinusDiagnostics.Log("ViewModelBase", "Sauvegarde impossible pour la naultinus " + Identifier, ex);
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

        public ICommand NewNaultinusCommand { get; } = new RelayCommand(() => NaultinusManager.CreateNaultinus());

        public ICommand NewFolderPortalCommand { get; } = new RelayCommand(() => NaultinusManager.ShowCreateFolderPortalDialog());

        public ICommand NewTaskNaultinusCommand { get; } = new RelayCommand(() => NaultinusManager.ShowCreateTaskNaultinusDialog());

        public ICommand NewCalendarNaultinusCommand { get; } = new RelayCommand(() => NaultinusManager.ShowCreateCalendarNaultinusDialog());

        public ICommand NewMailNaultinusCommand { get; } = new RelayCommand(() => NaultinusManager.ShowCreateMailNaultinusDialog());

        public ICommand ManageZimbraAccountsCommand { get; } = new RelayCommand(() =>
        {
            var dialog = new ManageAccountsDialog();
            dialog.ShowDialog();
        });

        public ICommand DeleteNaultinusCommand { get; } = new RelayCommand<string>(identifier => NaultinusManager.DeleteNaultinus(identifier));
        public ICommand HideNaultinusCommand { get; } = new RelayCommand<string>(identifier => NaultinusManager.HideNaultinus(identifier));

        public ICommand OpenAboutCommand { get; } = new RelayCommand(() =>
        {
            var about = new About
            {
                DataContext = new AboutViewModel()
            };
            NaultinusManager.SetOwnerSafe(about, Application.Current.MainWindow);
            about.ShowDialog();
        });

        public ICommand CheckForUpdatesCommand { get; } = new RelayCommand(() => _ = App.CheckForUpdatesAsync(announceIfNone: true));

        public ICommand SaveSnapshotCommand { get; } = new RelayCommand(() =>
        {
            var dialog = new SaveSnapshotDialog();
            NaultinusManager.SetOwnerSafe(dialog, Application.Current.MainWindow);
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SnapshotName))
                LayoutSnapshotService.SaveSnapshot(dialog.SnapshotName.Trim());
        });

        public ICommand ManageSnapshotsCommand { get; } = new RelayCommand(() =>
        {
            var dialog = new ManageSnapshotsDialog();
            NaultinusManager.SetOwnerSafe(dialog, Application.Current.MainWindow);
            dialog.ShowDialog();
        });

        public ICommand RestoreSnapshotCommand { get; } = new RelayCommand<string>(id =>
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!UserDialogs.ConfirmYesNo(Strings.RestoreLayoutConfirm, Strings.RestoreLayoutTitle))
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
            GC.SuppressFinalize(this);
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
