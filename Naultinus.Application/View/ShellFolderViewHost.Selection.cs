using Naultinus.Helpers;
using Naultinus.Helpers.Native;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Naultinus.View
{
    public partial class ShellFolderViewHost : HwndHost
    {
        #region Sélection

        private void TryRestoreSelectionAfterRefresh()
        {
            int index = _restoreSelectionIndex;
            _restoreSelectionIndex = -1;
            if (index < 0 || _browser is not IExplorerBrowser browser)
                return;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                    folderView.SelectItem(index, ShellBrowserNative.SvsiKeyboardMove);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryRestoreSelectionAfterRefresh", ex);
            }
        }

        /// <summary>
        /// Index de l'élément marqué par la sélection (-1 si la vue n'est pas joignable). Sert à
        /// juger ce que coûte un rafraîchissement par renavigation.
        /// </summary>
        private int SelectionMarkedItem()
        {
            if (_browser is not IExplorerBrowser browser)
                return -2;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetSelectionMarkedItem(out int index);
                    return index;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.SelectionMarkedItem", ex);
            }

            return -2;
        }

        /// <summary>Index de l'élément qui porte le focus dans la vue (-2 si la vue n'est pas joignable).</summary>
        private int FocusedItem()
        {
            if (_browser is not IExplorerBrowser browser)
                return -2;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetFocusedItem(out int index);
                    return index;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.FocusedItem", ex);
            }

            return -2;
        }

        #endregion
    }
}
