using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Naultinus.View;
using Naultinus.ViewModel;

namespace Naultinus.Helpers
{
    internal sealed class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;

        public TrayIconManager()
        {
            _notifyIcon = new NotifyIcon();

            Icon? icon = null;
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Ressources", "icon.ico");
            try
            {
                if (File.Exists(iconPath))
                    icon = new Icon(iconPath);
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("TrayIcon: chargement de l'icône", ex); }

            if (icon == null)
                icon = SystemIcons.Application;

            _notifyIcon.Icon = icon;
            _notifyIcon.Text = "Naultinus";
            _notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add(CreateItem("New shortcut naultinus", () => NaultinusManager.CreateNaultinus()));
            menu.Items.Add(CreateItem("New browse naultinus", () => NaultinusManager.ShowCreateFolderPortalDialog()));
            menu.Items.Add(CreateItem("New task naultinus", () => NaultinusManager.ShowCreateTaskNaultinusDialog()));
            menu.Items.Add(CreateItem("New calendar naultinus", () => NaultinusManager.ShowCreateCalendarNaultinusDialog()));
            menu.Items.Add(CreateItem("New mail naultinus", () => NaultinusManager.ShowCreateMailNaultinusDialog()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateItem("Manage Zimbra Accounts", () => new ManageAccountsDialog().ShowDialog()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateItem("About", () =>
            {
                var about = new About { DataContext = new AboutViewModel() };
                about.ShowDialog();
            }));
            menu.Items.Add(CreateItem(Properties.Strings.MenuCheckForUpdates, () => _ = App.CheckForUpdatesAsync(announceIfNone: true)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateItem("Quit", () =>
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
            _notifyIcon.Dispose();
        }
    }
}
