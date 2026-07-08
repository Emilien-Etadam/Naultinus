# Naultinus navigation

Type de naultinus qui affiche le contenu d'un dossier choisi, comme un mini-explorateur sur le bureau. Inspiré des Folder Portals de Stardock Fences.

## Fonctionnalités

- Navigation dans les sous-dossiers (double-clic) et ouverture des fichiers avec l'application par défaut
- Boutons retour et accueil (racine)
- Fil d'Ariane relatif à la racine
- Titre fixe défini par l'utilisateur
- Persistance du dernier dossier visité (`CurrentPath` dans `state.xml`)
- Actualisation automatique via `FileSystemWatcher` (debounce 500 ms)
- Menu contextuel : actualiser, ouvrir dans l'Explorateur, coller depuis le presse-papiers, modifier, supprimer
- Personnalisation des couleurs (en-tête, corps, titre, libellés)

## Création

1. Clic droit sur l'en-tête d'une naultinus existante → **Nouvelle naultinus navigation**
2. Ou : icône systray → **Nouvelle naultinus navigation**
3. Ou : clic droit + glisser sur le bureau pour dessiner un rectangle, puis choisir le type

Dans le dialogue : saisir un titre, parcourir le dossier racine, **Créer**.

## Limites connues

- Pas de glisser-déposer de fichiers (contrairement aux naultinus raccourcis)
- Pas de renommage/suppression de fichiers dans la naultinus (utiliser l'Explorateur)
- Pas de filtre/recherche
- Tri alphabétique fixe (dossiers puis fichiers)
- Pour changer le dossier racine : supprimer et recréer la naultinus
- Icônes mises en cache dans `saved/{id}/icons/` ; en cas d'icône obsolète, vider ce dossier

## Architecture

| Couche | Fichier | Rôle |
|--------|---------|------|
| Modèle | `Model/FolderPortalModel.cs` | `RootPath`, `CurrentPath` |
| Modèle | `Model/NaultinusType.cs` | `FolderPortal = 1` |
| ViewModel | `ViewModel/FolderPortalViewModel.cs` | Navigation, watcher, commandes |
| Vue | `View/FolderPortal.xaml` | Fenêtre WPF |
| Vue | `View/CreateFolderPortalDialog.xaml` | Dialogue de création |

## Persistance

- `%LOCALAPPDATA%\Naultinus\saved\{GUID}\state.xml`
- Namespace XML `io.stouder` (ne pas modifier)
- Rétrocompat : anciens `state.xml` sans `Type` explicite → `Standard`
