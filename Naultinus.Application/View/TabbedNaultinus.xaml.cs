using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Naultinus.Model;
using Naultinus.ViewModel;
using Naultinus;
using Naultinus.Properties;
using Naultinus.Services;

namespace Naultinus.View
{
    public partial class TabbedNaultinus : Window
    {
        private readonly NaultinusGroup _group;
        private Point _tabDragOrigin;
        private bool _tabDragPending;

        public TabbedNaultinus(NaultinusGroup group)
        {
            InitializeComponent();
            _group = group;
            DataContext = group;

            ApplyTabStyle();
            LocationChanged += (_, _) => SyncBounds();
            SizeChanged += (_, _) => SyncBounds();
            Loaded += (_, _) => SyncBounds();
            Activated += (_, _) =>
            {
                foreach (var member in _group.Members)
                {
                    if (member is CalendarNaultinusViewModel calendar)
                        calendar.OnWindowActivated();
                }
            };

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Delete && e.Key != Key.Back)
                    return;
                if (_group.SelectedMember is NaultinusViewModel vm)
                {
                    vm.DeleteShortcut();
                    e.Handled = true;
                }
            };

            Show();
        }

        private void ApplyTabStyle()
        {
            var settings = AppSettingsStore.Load();
            var styleKey = settings.DefaultTabStyle == TabStyle.Rounded ? "RoundedTabHeaderButtonStyle" : "FlatTabHeaderButtonStyle";
            if (Resources[styleKey] is System.Windows.Style style)
                Resources["CurrentTabHeaderButtonStyle"] = style;
        }

        private void SyncBounds()
        {
            if (_group.Members.Count == 0) return;
            _group.SetBounds((int)Left, (int)Top, (int)Width, (int)Height);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); }
            catch (System.InvalidOperationException) { /* le bouton gauche n'est plus enfoncé : sans effet */ }
        }

        // Le Button d'un onglet marque MouseLeftButtonDown comme géré : le Grid ne reçoit plus l'événement
        // et DragMove ne démarre pas. Dès le deuxième onglet, les libellés (surtout Parcourir) couvrent
        // la barre ; la poignée de 12 px ne suffit plus. Un écart au-delà du seuil système lance le
        // déplacement, un clic court laisse la commande de sélection s'exécuter.
        private void Header_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _tabDragPending = false;
            if (e.ChangedButton != MouseButton.Left)
                return;
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not Button button)
                return;
            if (button.DataContext is not INaultinusViewModel)
                return;

            _tabDragOrigin = e.GetPosition(this);
            _tabDragPending = true;
        }

        private void Header_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_tabDragPending || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point position = e.GetPosition(this);
            double dx = position.X - _tabDragOrigin.X;
            double dy = position.Y - _tabDragOrigin.Y;
            if (System.Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
                && System.Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _tabDragPending = false;
            // Sans ce relâchement, le MouseUp synthétique de DragMove déclenche la sélection d'onglet.
            Mouse.Captured?.ReleaseMouseCapture();
            try { DragMove(); }
            catch (System.InvalidOperationException) { /* bouton gauche relâché pendant le seuil : sans effet */ }
            e.Handled = true;
        }

        private void Header_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _tabDragPending = false;
        }

        private void TitleBarMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (Header.ContextMenu is ContextMenu cm)
            {
                cm.PlacementTarget = sender as UIElement;
                cm.Placement = PlacementMode.Bottom;
                cm.IsOpen = true;
            }
            e.Handled = true;
        }

        private void AddFolderPortalTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement anchor)
                NaultinusManager.RequestAddTab(this, anchor);
            e.Handled = true;
        }

        private void LayoutsSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (DataContext is NaultinusGroup g && g.Members.Count > 0 && g.Members[0] is ViewModelBase vb)
                vb.RefreshRecentSnapshots();
        }

        private void TabStrip_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Right)
                return;
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not Button btn)
                return;
            if (btn.DataContext is not INaultinusViewModel vm)
                return;

            var menu = new ContextMenu();
            var rename = new MenuItem { Header = Strings.TabRenameMenu };
            rename.Click += (_, _) => RenameTab(vm);
            menu.Items.Add(rename);

            var left = new MenuItem { Header = Strings.TabMoveLeft };
            left.Click += (_, _) => MoveTab(vm, -1);
            left.IsEnabled = _group.Members.IndexOf(vm) > 0;
            menu.Items.Add(left);

            var right = new MenuItem { Header = Strings.TabMoveRight };
            right.Click += (_, _) => MoveTab(vm, 1);
            right.IsEnabled = _group.Members.IndexOf(vm) < _group.Members.Count - 1;
            menu.Items.Add(right);

            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match)
                    return match;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void RenameTab(INaultinusViewModel vm)
        {
            var dlg = new RenameSnapshotInputDialog
            {
                Owner = this,
                Title = Strings.RenameTabTitle,
                PromptLabel = Strings.NewTabNamePrompt,
                CurrentName = vm.Name,
            };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.NewName))
                return;
            vm.Name = dlg.NewName.Trim();
        }

        private void MoveTab(INaultinusViewModel vm, int delta)
        {
            if (!_group.TryMoveMember(vm, delta))
                return;
            _group.SelectedMember = vm;
        }
    }
}
