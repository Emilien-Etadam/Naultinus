# Folder Portal Naultinus

A new naultinus type for Naultinus that acts as a mini file explorer for a chosen folder, displayed directly on the desktop. Inspired by Stardock Fences' Folder Portals feature.

## What It Does

A Folder Portal naultinus displays the contents of a target folder (files and subfolders) as items inside the naultinus. You can navigate into subfolders by double-clicking, go back to the parent folder, and open files with their default application.

### Features

- **Folder browsing**: Displays files and subfolders with their system icons
- **Navigation**: Double-click a folder to navigate into it; double-click a file to open it
- **Back button**: Navigate to the parent folder (only shown when not at the root)
- **Home button**: Jump back to the root folder in one click
- **Breadcrumb**: Shows the current path relative to the root (e.g., `Root > Subfolder > Sub-subfolder`)
- **Fixed title**: A user-defined title that always stays visible regardless of navigation
- **Persistence**: Remembers the last visited folder across application restarts
- **Context menu**: Refresh contents, open current folder in Windows Explorer, edit portal settings
- **Customization**: Same color customization as standard naultinus (header, body, title, labels colors)

## How to Create a Folder Portal

1. **Right-click** on the header of any existing naultinus (standard or folder portal)
2. Select **"New Folder Portal"** from the context menu
3. In the dialog that appears:
   - Enter a **title** for the portal (e.g., "My Projects")
   - Click **"Browse..."** to select the root folder
   - Click **"Create"**
4. The new Folder Portal appears on the desktop showing the contents of the selected folder

## How to Use

- **Double-click a folder** to navigate into it
- **Double-click a file** to open it with the default application
- Click the **back arrow** (left arrow in the header) to go up one folder level
- Click the **home icon** to return to the root folder
- **Right-click** the header for more options:
  - **Edit**: Change the portal title and colors
  - **Refresh**: Reload the current folder contents
  - **Open in Explorer**: Open the current folder in Windows File Explorer
  - **Delete**: Remove this Folder Portal

## How to Configure / Edit

1. **Right-click** the Folder Portal header
2. Select **"Edit"**
3. You can change:
   - The portal **name/title**
   - **Header color**, **body color**, **title color**, **labels color**
4. Changes are saved automatically

## Technical Details

### Architecture

The Folder Portal follows the same MVVM pattern as the existing standard naultinus:

| Layer | File | Purpose |
|-------|------|---------|
| Model | `Model/NaultinusModel.cs` | Extended with `Type`, `RootPath`, `CurrentPath` properties |
| Model | `Model/NaultinusType.cs` | Enum: `Standard`, `FolderPortal` |
| Model | `Model/FolderPortalItem.cs` | Represents a file/folder entry in the portal |
| ViewModel | `ViewModel/FolderPortalViewModel.cs` | Navigation logic, folder loading, commands |
| View | `View/FolderPortal.xaml` | WPF window with header, breadcrumb, item grid |
| View | `View/CreateFolderPortalDialog.xaml` | Dialog for creating a new Folder Portal |
| View | `View/EditFolderPortal.xaml` | Dialog for editing portal properties |

### Persistence

Folder Portals use the same XML persistence as standard naultinus:
- Stored in `%LOCALAPPDATA%\Naultinus\saved\{GUID}\state.xml`
- The `Type` property in `NaultinusModel` distinguishes between `Standard` and `FolderPortal`
- `RootPath` and `CurrentPath` are persisted so the last browsed location is restored on restart
- Backward compatible: existing standard naultinus configs default to `Type=Standard` with empty paths

## Current Limitations / TODOs

- **No file system watcher**: The portal does not auto-refresh when files are added/removed externally. Use the "Refresh" context menu option to reload.
- **No drag-and-drop**: You cannot drag files out of or into the Folder Portal (unlike standard naultinus which support shortcut drag-drop).
- **No rename/delete**: File management operations (rename, delete, copy) are not implemented. Use "Open in Explorer" for file management.
- **No search/filter**: No text filter or search box to filter displayed items.
- **No custom sorting**: Items are always sorted alphabetically (folders first, then files).
- **Root folder change**: To change the root folder, you need to delete the portal and create a new one.
- **Icon caching**: Icons are cached per-file based on path hash. If a file's icon changes, you may need to clear the icons folder manually.
