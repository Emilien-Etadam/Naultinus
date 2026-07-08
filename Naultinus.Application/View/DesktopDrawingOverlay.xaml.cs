using System;
using System.Windows;
using Naultinus;
using Naultinus.Properties;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Naultinus.View
{
    public partial class DesktopDrawingOverlay : Window
    {
        private const int MinNaultinusWidth = 100;
        private const int MinNaultinusHeight = 80;
        private const double MinDragDistance = 5;

        private Point _startPoint;
        private bool _isDrawing;
        private bool _dragExceededThreshold;
        private ContextMenu? _creationMenu;

        public DesktopDrawingOverlay()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            PreviewMouseMove += OnPreviewMouseMove;
            PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SetFullScreenBounds();
            BuildContextMenu();
        }

        private void SetFullScreenBounds()
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void BuildContextMenu()
        {
            _creationMenu = new ContextMenu();

            void AddItem(string header, Action<int, int, int, int> create)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, __) =>
                {
                    var (x, y, w, h) = GetNormalizedRect();
                    create(x, y, w, h);
                    HideDrawing();
                };
                _creationMenu.Items.Add(item);
            }

            AddItem(Strings.DesktopShortcutNaultinus, (x, y, w, h) => NaultinusManager.CreateNaultinus(x, y, w, h));
            AddItem(Strings.DesktopBrowseNaultinus, (x, y, w, h) =>
            {
                var d = new CreateFolderPortalDialog();
                if (d.ShowDialog() == true)
                    NaultinusManager.CreateFolderPortal(d.SelectedPath, d.PortalTitle, x, y, w, h);
            });
            AddItem(Strings.DesktopTaskNaultinus, (x, y, w, h) =>
            {
                var d = new CreateTaskNaultinusDialog();
                if (d.ShowDialog() == true)
                    NaultinusManager.CreateTaskNaultinus(d.CalDAVUrl, d.Username, d.Password, d.SelectedTaskListIds, d.NaultinusTitle, x, y, w, h, zimbraAccountId: d.SelectedZimbraAccountId);
            });
            AddItem(Strings.DesktopCalendarNaultinus, (x, y, w, h) =>
            {
                var d = new CreateCalendarNaultinusDialog();
                if (d.ShowDialog() == true)
                    NaultinusManager.CreateCalendarNaultinus(d.CalDAVUrl, d.Username, d.Password, d.SelectedCalendarIds, d.NaultinusTitle, d.ViewMode, d.DaysToShow, x, y, w, h, zimbraAccountId: d.SelectedZimbraAccountId);
            });
            AddItem(Strings.DesktopMailNaultinus, (x, y, w, h) =>
            {
                var d = new CreateMailNaultinusDialog();
                if (d.ShowDialog() == true)
                    NaultinusManager.CreateMailNaultinus(d.ImapHost, d.ImapPort, d.Username, d.Password, d.SelectedFolders, d.NaultinusTitle, d.DisplayMode, d.PollIntervalMinutes, d.WebmailUrl, x, y, w, h, zimbraAccountId: d.SelectedZimbraAccountId);
            });
        }

        private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(DrawCanvas);
            _isDrawing = true;
            _dragExceededThreshold = false;
            SelectionRect.Visibility = Visibility.Collapsed;
            DimensionsLabel.Visibility = Visibility.Collapsed;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || e.RightButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(DrawCanvas);
            if (!_dragExceededThreshold)
            {
                var dist = Math.Sqrt(Math.Pow(pos.X - _startPoint.X, 2) + Math.Pow(pos.Y - _startPoint.Y, 2));
                if (dist < MinDragDistance) return;
                _dragExceededThreshold = true;
            }

            double x = Math.Min(_startPoint.X, pos.X);
            double y = Math.Min(_startPoint.Y, pos.Y);
            double w = Math.Abs(pos.X - _startPoint.X);
            double h = Math.Abs(pos.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = Math.Max(1, w);
            SelectionRect.Height = Math.Max(1, h);
            SelectionRect.Visibility = Visibility.Visible;

            DimensionsLabel.Text = $"{(int)w} × {(int)h}";
            Canvas.SetLeft(DimensionsLabel, x + w - 60);
            Canvas.SetTop(DimensionsLabel, y + h - 22);
            DimensionsLabel.Visibility = Visibility.Visible;
        }

        private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;

            if (!_dragExceededThreshold)
            {
                SelectionRect.Visibility = Visibility.Collapsed;
                DimensionsLabel.Visibility = Visibility.Collapsed;
                return;
            }

            var (_, _, w, h) = GetNormalizedRect();
            if (w >= MinNaultinusWidth && h >= MinNaultinusHeight && _creationMenu != null)
            {
                _creationMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                _creationMenu.IsOpen = true;
                _creationMenu.Closed += (_, __) => Dispatcher.BeginInvoke(() => HideDrawing(), DispatcherPriority.Background);
            }
            else
                HideDrawing();
        }

        private (int X, int Y, int W, int H) GetNormalizedRect()
        {
            double x = Canvas.GetLeft(SelectionRect);
            double y = Canvas.GetTop(SelectionRect);
            double w = SelectionRect.Width;
            double h = SelectionRect.Height;
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }
            int screenX = (int)(Left + x);
            int screenY = (int)(Top + y);
            return (screenX, screenY, (int)Math.Max(MinNaultinusWidth, w), (int)Math.Max(MinNaultinusHeight, h));
        }

        private void HideDrawing()
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            DimensionsLabel.Visibility = Visibility.Collapsed;
        }
    }
}
