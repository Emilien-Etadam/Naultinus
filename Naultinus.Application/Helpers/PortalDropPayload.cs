using System;
using System.Windows;
using GongSolutions.Wpf.DragDrop;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>Effets et chemins extraits d'un dépôt Gong / DataObject (portail).</summary>
    internal static class PortalDropPayload
    {
        internal static void ApplyFileDropEffects(IDropInfo dropInfo)
        {
            bool isCopy = PortalDragDropKeys.IsCopy(dropInfo.KeyStates);
            dropInfo.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
        }

        internal static bool LooksLikeFileDrop(object? data)
        {
            if (data is DataObject dataObject && dataObject.GetDataPresent(DataFormats.FileDrop))
                return true;
            if (data is IDataObject iDataObject && iDataObject.GetDataPresent(DataFormats.FileDrop))
                return true;
            return data is FolderPortalItem;
        }

        internal static bool TryGetPaths(object? data, out string[] files)
        {
            files = Array.Empty<string>();
            if (data is DataObject dataObject && dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                files = dataObject.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
                return files.Length > 0;
            }

            if (data is IDataObject iDataObject && iDataObject.GetDataPresent(DataFormats.FileDrop))
            {
                files = iDataObject.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
                return files.Length > 0;
            }

            if (data is FolderPortalItem item && !string.IsNullOrEmpty(item.FullPath))
            {
                files = new[] { item.FullPath };
                return true;
            }

            return false;
        }

        internal static string? TargetDirectory(IDropInfo dropInfo)
            => dropInfo.TargetItem is FolderPortalItem folder && folder.IsDirectory
                ? folder.FullPath
                : null;
    }
}
