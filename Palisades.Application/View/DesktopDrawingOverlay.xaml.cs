using System;
using System.Windows;
using Palisades;
using Palisades.Properties;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Palisades.View
{
    public partial class DesktopDrawingOverlay : Window
    {
        private const int MinPalisadeWidth = 100;
        private const int MinPalisadeHeight = 80;
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

            AddItem(Strings.DesktopShortcutPalisade, (x, y, w, h) => PalisadesManager.CreatePalisade(x, y, w, h));
            AddItem(Strings.DesktopBrowsePalisade, (x, y, w, h) =>
            {
                var d = new CreateFolderPortalDialog();
                if (d.ShowDialog() == true)
                    PalisadesManager.CreateFolderPortal(d.SelectedPath, d.PortalTitle, x, y, w, h);
            });
            AddItem(Strings.DesktopTaskPalisade, (x, y, w, h) =>
            {
                var d = new CreateTaskPalisadeDialog();
                if (d.ShowDialog() == true)
                    PalisadesManager.CreateTaskPalisade(d.CalDAVUrl, d.Username, d.Password, d.SelectedTaskListIds, d.PalisadeTitle, x, y, w, h);
            });
            AddItem(Strings.DesktopCalendarPalisade, (x, y, w, h) =>
            {
                var d = new CreateCalendarPalisadeDialog();
                if (d.ShowDialog() == true)
                    PalisadesManager.CreateCalendarPalisade(d.CalDAVUrl, d.Username, d.Password, d.SelectedCalendarIds, d.PalisadeTitle, d.ViewMode, d.DaysToShow, x, y, w, h);
            });
            AddItem(Strings.DesktopMailPalisade, (x, y, w, h) =>
            {
                var d = new CreateMailPalisadeDialog();
                if (d.ShowDialog() == true)
                    PalisadesManager.CreateMailPalisade(d.ImapHost, d.ImapPort, d.Username, d.Password, d.SelectedFolders, d.PalisadeTitle, d.DisplayMode, d.PollIntervalMinutes, d.WebmailUrl, x, y, w, h);
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
            if (w >= MinPalisadeWidth && h >= MinPalisadeHeight && _creationMenu != null)
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
            return (screenX, screenY, (int)Math.Max(MinPalisadeWidth, w), (int)Math.Max(MinPalisadeHeight, h));
        }

        private void HideDrawing()
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            DimensionsLabel.Visibility = Visibility.Collapsed;
        }
    }
}
