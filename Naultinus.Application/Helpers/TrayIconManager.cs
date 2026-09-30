using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Naultinus.Properties;
using Naultinus.View;
using Naultinus.ViewModel;

namespace Naultinus.Helpers
{
    internal sealed class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private Icon? _ownedIcon;

        public TrayIconManager()
        {
            _notifyIcon = new NotifyIcon();

            _ownedIcon = ApplicationIconLoader.TryCreateOwnedIcon();
            if (_ownedIcon == null)
            {
                NaultinusDiagnostics.Log(
                    "TrayIcon",
                    "Aucun handle d'icône utilisable ; la zone de notification restera vide.");
            }
            else
            {
                _notifyIcon.Icon = _ownedIcon;
            }

            _notifyIcon.Text = Strings.AppNameNaultinus;
            _notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add(CreateItem(Strings.MenuNewShortcutNaultinus, () => NaultinusManager.CreateNaultinus()));
            menu.Items.Add(CreateItem(Strings.MenuNewBrowseNaultinus, () => NaultinusManager.ShowCreateFolderPortalDialog()));
            menu.Items.Add(CreateItem(Strings.MenuNewTaskNaultinus, () => NaultinusManager.ShowCreateTaskNaultinusDialog()));
            menu.Items.Add(CreateItem(Strings.MenuNewCalendarNaultinus, () => NaultinusManager.ShowCreateCalendarNaultinusDialog()));
            menu.Items.Add(CreateItem(Strings.MenuNewMailNaultinus, () => NaultinusManager.ShowCreateMailNaultinusDialog()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateItem(Strings.MenuManageZimbraAccounts, () => new ManageAccountsDialog().ShowDialog()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateItem(Strings.MenuAbout, () =>
            {
                var about = new About { DataContext = new AboutViewModel() };
                about.ShowDialog();
            }));
            menu.Items.Add(CreateItem(Strings.MenuCheckForUpdates, () => _ = App.CheckForUpdatesAsync(announceIfNone: true)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateItem(Strings.MenuQuit, () =>
            {
                NaultinusManager.CloseAllNaultinus();
                System.Windows.Application.Current.Shutdown();
            }));

            _notifyIcon.ContextMenuStrip = menu;

            _notifyIcon.DoubleClick += (_, _) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var w in NaultinusManager.naultinus.Values)
                    {
                        try
                        {
                            w.Show();
                            w.Activate();
                        }
                        catch (Exception ex) { NaultinusDiagnostics.LogDebug("TrayIcon: activation de la fenêtre", ex); }
                    }
                });
            };
        }

        private static ToolStripMenuItem CreateItem(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => action());
            };
            return item;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Icon = null;
            _notifyIcon.Dispose();
            _ownedIcon?.Dispose();
            _ownedIcon = null;
        }
    }
}
