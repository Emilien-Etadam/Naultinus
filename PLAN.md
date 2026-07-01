Données suffisantes. Voici le plan.

---

# Plan de mise à jour Palisades-Navigation — Instructions Cursor

## Contexte technique Zimbra OVH

Zimbra OVH expose : CalDAV sur `https://<serveur>/dav/<email>/Calendar` (calendriers) et `https://<serveur>/dav/<email>/Tasks` (tâches), IMAP standard sur le port 993 (SSL). Les credentials sont identiques pour les trois protocoles. Le plan unifie l'authentification autour d'un seul jeu de credentials Zimbra par compte configuré.

---

## Phase 0 — Résolution des bloquants

**0.1 — Résoudre le conflit Git dans `TaskPalisadeViewModel.cs`.** Le fichier contient un marqueur de merge non résolu (`=======`) avec deux implémentations concurrentes de la propriété `CalDAVPassword`. Conserver la version chiffrée (celle utilisant `CredentialEncryptor`), supprimer la version en clair et le marqueur.

**0.2 — ~~Sortir le DSN Sentry du code source.~~ Obsolète : Sentry a été entièrement retiré du projet.** Plus aucun package ni code de reporting d'erreurs ; `appsettings.example.json` et le crédit « Sentry » de la fenêtre À propos ont été supprimés. (Le DSN historiquement exposé n'est plus présent dans le dépôt.)

**0.3 — Vérifier la compilation.** Après 0.1 et 0.2, s'assurer que la solution compile sans erreur ni warning sur .NET 10.

### Modifications effectuées (Phase 0 et prérequis build)

- **Cible .NET :** projet en `net8.0-windows` (les packages WPF ne ciblent pas encore net10.0). Fichier `global.json` pour privilégier le SDK 8.0 sur Windows.
- **Référence COM supprimée :** la référence COM `IWshRuntimeLibrary` bloquait `dotnet build`. Remplacée par un lecteur .lnk en code : `Helpers/LnkReader.cs` (parsing binaire MS-SHLLINK) et `LnkShortcut.BuildFrom` utilise `LnkReader.GetTargetPath()`.
- **Sélection de couleurs :** le package PixiEditor.ColorPicker a été retiré. Utilisation de `System.Windows.Forms.ColorDialog` (API Windows standard) via `Helpers/ColorConversion.cs` (conversion Drawing.Color ↔ Media.Color), contrôle `View/ColorPickerButton.xaml` (carré couleur cliquable), et `EditTaskPalisade` appelle directement `ColorDialog`. Plus de dépendance tierce pour le color picker.
- **Packages mis à jour :** gong-wpf-dragdrop 4.0.0, MaterialDesignThemes 5.3.0, Microsoft.Xaml.Behaviors.Wpf 1.1.135. Microsoft.Extensions.Configuration 8.0.0, Ical.Net 4.3.0, System.Drawing.Common 8.0.0 conservés pour compatibilité net8. (Sentry ultérieurement retiré, cf. 0.2.)
- **Tests unitaires :** `CalDAVServiceTests.cs` (xUnit) exclu de la compilation du projet application (`<Compile Remove="Services\CalDAVServiceTests.cs" />`) en attendant la Phase 8.1 (projet Palisades.Tests).
- **Corrections diverses :** chaîne XML dans `CalDAVService.cs` (guillemets verbatim `""`), `using System` dans `App.xaml.cs`, `Descendants().FirstOrDefault()` au lieu de `Descendant` en LINQ to XML, `using Ical.Net` et `using System.Collections.Generic` où nécessaire, `nuget.config` avec source nuget.org explicite.
- **About.xaml :** mention de PixiEditor's ColorPicker retirée.
- **MaterialDesignThemes 5.x :** `MaterialDesignTheme.Defaults.xaml` remplacé par `MaterialDesign2.Defaults.xaml` dans App.xaml et toutes les vues (TaskPalisade, EditTaskPalisade, CreateTaskPalisadeDialog, TaskPalisadeSettingsDialog) pour corriger le plantage au démarrage (ressource introuvable).
- **Démarrage :** log de diagnostic dans `%TEMP%\Palisades_startup.log` et piège des exceptions non gérées pour faciliter le diagnostic si l’app se ferme silencieusement.

---

## Phase 1 — Refactoring de base (ViewModelBase + Modèle polymorphe)

**1.1 — Créer `ViewModelBase` dans `ViewModel/`.** Extraire de `PalisadeViewModel`, `FolderPortalViewModel` et `TaskPalisadeViewModel` tout le code dupliqué : propriétés communes (Identifier, Name, FenceX, FenceY, Width, Height, HeaderColor, BodyColor, TitleColor, LabelsColor), implémentation `INotifyPropertyChanged`, mécanisme `Save()`/`SaveAsync()` avec le thread background, commandes communes (NewPalisadeCommand, NewFolderPortalCommand, DeletePalisadeCommand, OpenAboutCommand). `ViewModelBase` devient abstraite avec une méthode `protected abstract void SerializeModel(StreamWriter writer)` que chaque sous-classe implémente pour gérer ses types XML spécifiques.

**1.2 — Refactorer les trois ViewModels.** `PalisadeViewModel`, `FolderPortalViewModel`, `TaskPalisadeViewModel` héritent de `ViewModelBase`. Ne conservent que leurs propriétés et commandes spécifiques. Supprimer tout code copié-collé.

**1.3 — Éclater `PalisadeModel`.** ✅ Fait. Hiérarchie : `PalisadeModelBase` (propriétés communes), `StandardPalisadeModel` (Shortcuts), `FolderPortalModel` (RootPath, CurrentPath), `TaskPalisadeModel` (CalDAV*). `[XmlInclude]` sur la base pour chaque sous-type. `PalisadeModel` hérite de `PalisadeModelBase` et conserve toutes les propriétés pour la rétrocompat. `LoadPalisades()` désérialise en `PalisadeModel`, puis conversion via `PalisadeModelMigration.ToConcreteModel()` vers le type concret.

**1.4 — Rétrocompatibilité.** ✅ Fait. Les anciens `state.xml` se désérialisent en `PalisadeModel`, puis sont convertis en `StandardPalisadeModel` / `FolderPortalModel` / `TaskPalisadeModel` selon `Type`. Mapper : `Model/PalisadeModelMigration.cs`. Les nouvelles créations utilisent les types concrets ; la sauvegarde sérialise le type concret (StandardPalisadeModel, FolderPortalModel, TaskPalisadeModel).

---

## Phase 2 — Correction des problèmes de concurrence

**2.1 — Protéger la sérialisation XML.** ✅ Fait. Un `lock` (_saveLock) entoure la sérialisation dans `SaveAsync()` pour les trois ViewModels et `ViewModelBase`, afin d'éviter lectures/écritures concurrentes du modèle.

**2.2 — Dispatcher pour les collections UI.** ✅ Fait. Dans `TaskPalisadeViewModel`, `LoadTasksAsync()` et `SyncWithCalDAVAsync()` mettent à jour `Tasks`, `SyncStatus`, `ErrorMessage`, `IsLoading`, `IsSyncing` via une méthode `Dispatch(action)` qui utilise `Dispatcher.Invoke` si appel hors thread UI. La copie de `Tasks` pour `SyncTasksAsync` est faite sur le thread UI via `InvokeAsync`.

**2.3 — Remplacer `Thread` + `Sleep(1000)` par `Timer`.** ✅ Fait. Chaque ViewModel (et `ViewModelBase`) utilise un `System.Threading.Timer` (période 1 s) au lieu d’un thread dédié avec boucle + `Thread.Sleep(1000)`.

**2.4 — Ne plus appeler `Save()` sur `SelectedShortcut` / `SelectedTask`.** ✅ Fait. Le setter de `SelectedShortcut` dans `PalisadeViewModel` ne appelle plus `Save()`. `SelectedTask` dans `TaskPalisadeViewModel` ne l’appelait déjà pas.

---

## Phase 3 — Remplacement de la couche réseau et sécurité

**3.1 — Remplacer `HttpWebRequest` par `HttpClient`.** ✅ Fait. `CalDAVService` utilise un `HttpClient` créé dans le constructeur avec `HttpClientHandler` (Credentials, PreAuthenticate). Requêtes PROPFIND, REPORT, PUT, DELETE via `HttpRequestMessage` et `SendAsync`.

**3.2 — Remplacer `CredentialEncryptor` par DPAPI.** ✅ Fait. `CredentialEncryptor` utilise `ProtectedData.Protect` / `Unprotect` (scope `CurrentUser`). API : `Encrypt(plainText)` et `Decrypt(cipherText)` sans clé utilisateur. Surcharges à deux paramètres conservées (obsoletes) pour compatibilité.

**3.3 — Valider HTTPS.** ✅ Fait. Dans le constructeur de `CalDAVService`, `EnsureHttps(url)` refuse toute URL non vide qui ne commence pas par `https://` (exception `InvalidOperationException`). Les URLs vides (palissade non configurée) sont acceptées.

**3.4 — Créer un modèle `ZimbraAccount`.** ✅ Fait. `Model/ZimbraAccount.cs` (Id, Server, Email, EncryptedPassword, CalDAVBaseUrl). `Services/ZimbraAccountStore.cs` : Load/Save vers `%LOCALAPPDATA%\Palisades\accounts.xml`, `GetById(Guid)`. `TaskPalisadeModel.ZimbraAccountId` (Guid?) : si défini, les credentials sont résolus depuis le store au chargement (`PalisadesManager.LoadPalisades`). Sinon, usage des champs CalDAVUrl/Username/Password (rétrocompat).

---

## Phase 4 — Compléter l'implémentation CalDAV (Task Palisade existante)

**4.1 — Implémenter le parsing de `GetTasksAsync`.** ✅ Fait. `ParseMultistatusCalendarData` parse la réponse multistatus (DAV:response, getetag, caldav:calendar-data), charge chaque blob avec `Calendar.Load`, mappe les `Todo` vers `CalDAVTask` (Summary, Description, Due, Status, Completed, Uid, CalDAVId depuis href, CalDAVEtag).

**4.2 — Corriger `CreateTaskAsync` et `UpdateTaskAsync`.** ✅ Fait. `CalDAVTask` a maintenant `Uid` (iCalendar) distinct de `CalDAVId` (fichier .ics). CreateTaskAsync utilise un Uid et nomme le fichier `{uid}.ics`, et remplit `task.Uid`. UpdateTaskAsync utilise `task.Uid` (avec fallback CalDAVId sans extension) pour le VTODO.

**4.3 — Résolution de conflits dans `SyncTasksAsync`.** ✅ Fait. Plus de suppression des tâches distantes. Sync retourne une liste fusionnée : créations des locales absentes du serveur, mises à jour si local plus récent, et toutes les tâches distantes inconnues localement sont ajoutées au résultat. Signature : `Task<List<CalDAVTask>> SyncTasksAsync(taskListId, localTasks)`. Le ViewModel remplace `Tasks` par la liste retournée.

**4.4 — URLs Zimbra OVH.** ✅ Fait. Commentaire dans `CalDAVService` et `CreateTaskPalisadeDialog` : pour Zimbra, TaskListId typiquement `"Tasks"`. Valeur par défaut du champ TaskListId dans le dialogue = "Tasks".

---

## Phase 5 — Nouvelle palisade : Calendrier CalDAV

**5.1 — Créer le modèle `CalendarPalisadeModel`.** ✅ Fait. Hérite de `PalisadeModelBase`. Propriétés : `ZimbraAccountId`, `CalDAVBaseUrl`, `CalDAVUsername`, `CalDAVPassword`, `CalendarIds`, `ViewMode` (enum Agenda/Day/Week), `DaysToShow`. `[XmlInclude(typeof(CalendarPalisadeModel))]` ajouté sur `PalisadeModelBase`.

**5.2 — Créer `CalendarEvent` dans `Model/`.** ✅ Fait. Propriétés : `Uid`, `Summary`, `Description`, `DtStart`, `DtEnd`, `Location`, `IsAllDay`, `CalendarName`, `Color`, `CalDAVHref`, `ETag`.

**5.3 — Créer `CalendarCalDAVService` dans `Services/`.** ✅ Fait. Constructeur `(caldavBaseUrl, username, password)`. `GetCalendarListAsync()` : PROPFIND pour découvrir les collections calendrier (resourcetype calendar). `GetEventsAsync(calendarIdOrHref, start, end)` : REPORT avec calendar-query VEVENT et time-range, parsing multistatus avec Ical.Net, mapping vers `CalendarEvent`. Classe `CalDAVCalendarInfo` (CalendarId, DisplayName, Href).

**5.4 — Créer `CalendarPalisadeViewModel`.** ✅ Fait. Hérite de `ViewModelBase`. Propriétés : `Events` (ObservableCollection), `ViewMode`, `SelectedDate`, `DaysToShow`, `ErrorMessage`, `IsLoading`. Commandes : `RefreshCommand`, `NewCalendarPalisadeCommand`, etc. Timer de rafraîchissement 5 min. `LoadEventsAsync()` agrège les événements de tous les `CalendarIds`, tri par `DtStart`. `SerializeModel` pour `CalendarPalisadeModel`.

**5.5 — Créer la vue `CalendarPalisade.xaml`.** ✅ Fait. Mode Agenda : header (titre, bouton Refresh), liste scrollable d’événements (Summary, DtStart → DtEnd), couleur par événement via `ColorToBrushConverter`. Menu contextuel header avec Edit, Refresh, Delete, New fence / Folder Portal / Task / Calendar, About.

**5.6 — Créer `CreateCalendarPalisadeDialog.xaml`.** ✅ Fait. Champs : titre, CalDAV URL, username, password, bouton "Load calendars" (appel à `GetCalendarListAsync`), ListBox multi-sélection des calendriers, Create/Cancel. Pas encore de dropdown compte Zimbra (prévu Phase 7).

**5.7 — Intégrer dans `PalisadesManager`.** ✅ Fait. `PalisadeType.CalendarPalisade` ajouté. `LoadPalisades()` désérialise avec `PalisadeModelBase` + tous les sous-types (dont `CalendarPalisadeModel`) ; branche pour `CalendarPalisadeModel` (credentials depuis ZimbraAccount ou CalDAV*). `CreateCalendarPalisade(caldavUrl, username, password, calendarIds, title, viewMode, daysToShow)`, `ShowCreateCalendarPalisadeDialog()`. `DeletePalisade` gère `CalendarPalisadeViewModel`.

**5.8 — Menus contextuels.** ✅ Fait. `NewCalendarPalisadeCommand` et `NewTaskPalisadeCommand` ajoutés dans `ViewModelBase`. Palisade.xaml et FolderPortal.xaml : entrées "New Task Palisade" et "New Calendar Palisade". TaskPalisade : context menu header avec "New Calendar Palisade". CalendarPalisade : menu complet avec toutes les créations.

---

## Phase 6 — Nouvelle palisade : Compteur / Afficheur de mails non lus

**6.1 — Ajouter la dépendance MailKit.** ✅ Fait. `PackageReference Include="MailKit" Version="4.9.0"` dans le `.csproj`.

**6.2 — Créer `MailPalisadeModel`.** ✅ Fait. Hérite de `PalisadeModelBase`. Propriétés : `ZimbraAccountId`, `ImapHost`, `ImapPort`, `ImapUsername`, `ImapPassword` (chiffré), `MonitoredFolders` (défaut `["INBOX"]`), `DisplayMode` (CountOnly / CountAndSubjects), `MaxSubjectsShown`, `PollIntervalMinutes`, `WebmailUrl`. `MailSummaryItem` (Sender, Subject, Date). `[XmlInclude(typeof(MailPalisadeModel))]` sur la base.

**6.3 — Créer `ImapMailService` dans `Services/`.** ✅ Fait. Constructeur `(host, port, username, password)`. `ConnectAsync()` (SSL port 993, `SecureSocketOptions.SslOnConnect`), `DisconnectAsync()`, `GetUnreadCountAsync(folderName)` (STATUS Unread), `GetFolderNamesAsync()` (INBOX + noms des sous-dossiers), `GetRecentUnreadSubjectsAsync(folderName, maxCount)` (Search NotSeen, Fetch Envelope + InternalDate, mapping vers `MailSummaryItem`). Pas d’IDLE pour l’instant (polling uniquement).

**6.4 — Créer `MailPalisadeViewModel`.** ✅ Fait. Hérite de `ViewModelBase`. Propriétés : `TotalUnreadCount`, `UnreadCountsDisplay`, `RecentSubjects`, `DisplayMode`, `IsConnected`, `ErrorMessage`, `IsLoading`. Commandes : `RefreshCommand`, `OpenWebmailCommand`, `NewMailPalisadeCommand`, etc. Timer de polling configurable. `EnsureConnectedAndRefreshAsync` / `RefreshAsync`, mise à jour UI via Dispatcher. Mot de passe déchiffré à la création du service.

**6.5 — Créer la vue `MailPalisade.xaml`.** ✅ Fait. Header : titre + badge (TotalUnreadCount) + bouton Refresh. Corps : message d’erreur ; indicateur de chargement ; mode CountOnly (gros chiffre + détail par dossier) ; mode CountAndSubjects (liste scrollable Summary / Sender / Date). MultiDataTrigger pour afficher selon IsLoading et DisplayMode. Menu contextuel avec toutes les créations + "New Mail Palisade".

**6.6 — Créer `CreateMailPalisadeDialog.xaml`.** ✅ Fait. Champs : titre, IMAP host, username, password, bouton "Test connection & load folders" (GetFolderNamesAsync), ListBox multi-sélection des dossiers, ComboBox mode (Count only / Count and subjects). Create/Cancel. Pas encore de dropdown compte Zimbra (Phase 7).

**6.7 — Intégrer dans `PalisadesManager`.** ✅ Fait. `PalisadeType.MailPalisade`. `LoadPalisades()` : branche pour `MailPalisadeModel`, création `MailPalisadeViewModel` + `MailPalisade`. `CreateMailPalisade(...)`, `ShowCreateMailPalisadeDialog()`. `DeletePalisade` gère `MailPalisadeViewModel`. `NewMailPalisadeCommand` dans `ViewModelBase` et dans les menus (Palisade, FolderPortal, TaskPalisade, CalendarPalisade).

---

## Phase 7 — Gestion centralisée des comptes Zimbra

**7.1 — Créer la vue `ManageAccountsDialog.xaml`.** ✅ Fait. Liste des comptes (ListBox avec ItemTemplate : Email, Server, LastTestStatus). Boutons Add, Edit, Test, Delete, Close. `EditZimbraAccountDialog` pour ajout/édition : Email, Server, CalDAV Base URL, IMAP Host, Password, bouton "Detect OVH". Accessible via "Manage Zimbra Accounts" dans le menu contextuel de chaque palisade.

**7.2 — Persistance des comptes.** ✅ Déjà en place (Phase 3.4). Fichier `accounts.xml` via `ZimbraAccountStore`. Mots de passe chiffrés DPAPI. `ZimbraAccount` : Id, Server, Email, EncryptedPassword, CalDAVBaseUrl, ImapHost, LastTestStatus.

**7.3 — Détection Zimbra OVH.** ✅ Fait. `ZimbraOvhDetection.SuggestFromEmail(email)` : retourne (ImapHost, CalDAVBaseUrl) avec conventions OVH (ssl0.ovh.net ou mail.domaine). Bouton "Detect OVH" dans `EditZimbraAccountDialog` pré-remplit les champs. Test connexion dans ManageAccountsDialog (CalDAV GetTaskLists + IMAP Connect/Disconnect), mise à jour `LastTestStatus`.

---

## Phase 8 — Déplacer les tests, CI, qualité

**8.1 — Créer `Palisades.Tests`.** ✅ Fait. Projet xUnit `Palisades.Tests` ajouté à la solution. `CalDAVServiceTests.cs` déplacé (tests CalDAV, CredentialEncryptor Encrypt/Decrypt round-trip, empty, invalid). `ZimbraOvhDetectionTests` pour SuggestFromEmail. Tests DPAPI skippés sur non-Windows.

**8.2 — Workflow GitHub Actions (`build.yml`).** ✅ Fait. Étape "Restore and run tests" : `dotnet test Palisades.Tests`. Publication des artefacts Release conservée.

**8.3 — UX.** ✅ Fait. Dans `ViewModelBase`, `TitleColor` et `LabelsColor` : cache `_titleColorBrush` / `_labelsColorBrush`, recréation uniquement si la couleur du modèle a changé.

---

## Phase 9 — Nouvelles commandes dans les menus contextuels

✅ Fait. Chaque palisade expose dans son menu contextuel header : création de tous les types (New Palisade, New Folder Portal, New Task Palisade, New Calendar Palisade, New Mail Palisade), séparateur, "Manage Zimbra Accounts", séparateur, Edit/Delete/About (ou Refresh selon le type). `ManageZimbraAccountsCommand` ajouté dans `ViewModelBase` et dans PalisadeViewModel, FolderPortalViewModel, TaskPalisadeViewModel. Entrées ajoutées dans Palisade.xaml, FolderPortal.xaml, TaskPalisade.xaml, CalendarPalisade.xaml, MailPalisade.xaml.

---



## Phase 10 — Tabs, Création par dessin, Snapshots de layout

---

### 10.1 — Création de palisades par clic-droit glissé sur le bureau

**10.1.1 — Fenêtre de capture plein-écran.** Créer `DesktopDrawingOverlay.xaml`, une fenêtre WPF transparente (`AllowsTransparency=true`, `WindowStyle=None`, `Background=Transparent`) couvrant l'intégralité de la zone de travail (tous les moniteurs). Cette fenêtre est permanente, invisible, positionnée entre le bureau et les palisades (même z-order que le sinker). Elle ne capture que les événements clic-droit. Les clics gauches et les autres interactions passent au travers (`IsHitTestVisible` conditionnel : activé uniquement quand un clic-droit est détecté et maintenu).

**10.1.2 — Dessin du rectangle de sélection.** Au clic-droit enfoncé + déplacement de souris, dessiner un `Rectangle` WPF en pointillés (stroke `DashArray="4,2"`, couleur blanche semi-transparente, fill avec une couleur d'accent à 15% d'opacité). Le point d'origine est la position du clic-droit initial. Le rectangle suit la souris en temps réel. Afficher les dimensions en pixels dans un petit label collé au coin inférieur droit du rectangle pendant le dessin. Seuil minimum : ignorer si la surface dessinée est inférieure à 100x80 pixels (clic-droit accidentel sans intention de créer).

**10.1.3 — Menu contextuel au relâchement.** Au relâchement du clic-droit, si le seuil minimum est atteint, afficher un `ContextMenu` WPF à la position du curseur avec les entrées suivantes : "Standard Palisade", "Folder Portal", "Task Palisade", "Calendar Palisade", "Mail Palisade". Chaque entrée appelle `PalisadesManager.CreatePalisade(type, x, y, width, height)` avec une surcharge qui accepte les coordonnées et dimensions du rectangle dessiné. Si le seuil n'est pas atteint, laisser passer l'événement comme un clic-droit normal du bureau Windows. Si l'utilisateur clique en dehors du menu sans choisir, annuler.

**10.1.4 — Surcharge de création positionnée dans `PalisadesManager`.** Ajouter à chaque méthode de création (`CreatePalisade`, `CreateFolderPortal`, `CreateTaskPalisade`, `CreateCalendarPalisade`, `CreateMailPalisade`) une surcharge acceptant `int x, int y, int width, int height`. Ces valeurs sont injectées dans le modèle avant la première sauvegarde. Les dialogues de configuration spécifiques au type (choix du dossier pour Folder Portal, credentials pour Task/Calendar/Mail) s'ouvrent immédiatement après la création, la palisade étant déjà visible à la bonne position et aux bonnes dimensions en arrière-plan.

**10.1.5 — Distinction avec le clic-droit simple.** Si l'utilisateur fait un clic-droit sans glisser (relâchement immédiat, distance < 5px), ne rien intercepter. Laisser le menu contextuel natif du bureau Windows apparaître normalement. La fenêtre overlay ne doit jamais interférer avec le comportement natif du bureau en dehors du geste explicite de dessin.

---

### 10.2 — Tabs (palisades groupées en onglets)

**10.2.1 — Modèle de données.** Ajouter dans `PalisadeModelBase` une propriété `string? GroupId` (nullable, null = palisade autonome). Ajouter une propriété `int TabOrder` (position de l'onglet dans le groupe, défaut 0). Ajouter un enum `TabStyle` dans `Model/` avec deux valeurs : `Flat`, `Rounded`. Ajouter dans la configuration globale de l'application (nouveau fichier `%LOCALAPPDATA%\Palisades\settings.xml`) une propriété `DefaultTabStyle` de type `TabStyle`.

**10.2.2 — `PalisadeGroup`.** Créer la classe `PalisadeGroup` dans le dossier racine du projet, à côté de `PalisadesManager`. Un `PalisadeGroup` contient une liste ordonnée de `ViewModelBase` (les ViewModels des palisades membres). Il expose les propriétés de la fenêtre conteneur : position (X, Y), dimensions (Width, Height), et le `GroupId`. La position et les dimensions du groupe sont celles de la première palisade ajoutée. Les palisades suivantes ajoutées au groupe adoptent les dimensions du groupe.

**10.2.3 — Fenêtre à onglets `TabbedPalisade.xaml`.** Créer une nouvelle fenêtre WPF qui remplace les fenêtres individuelles quand des palisades sont groupées. Structure : la fenêtre est sinkée au même z-order que les palisades normales. Le header est identique aux palisades existantes (drag pour déplacer, resize par les bords). Sous le header, un `TabControl` dont chaque `TabItem` contient le contenu visuel d'une palisade membre. Le header de chaque `TabItem` affiche le nom de la palisade et adopte la couleur de header de cette palisade. Le contenu de chaque `TabItem` est le `ContentPresenter` approprié selon le type : le corps de `Palisade.xaml` pour Standard, le corps de `FolderPortal.xaml` pour FolderPortal, etc. Extraire les corps (zone sous le header) des vues existantes en `UserControl` réutilisables : `StandardPalisadeContent.xaml`, `FolderPortalContent.xaml`, `TaskPalisadeContent.xaml`, `CalendarPalisadeContent.xaml`, `MailPalisadeContent.xaml`.

**10.2.4 — Styles d'onglets.** Deux styles dans les ressources de `TabbedPalisade.xaml`. `Flat` : barre d'onglets uniforme, onglet actif en texte blanc opaque, onglets inactifs en texte gris semi-transparent, pas de bordure arrondie. `Rounded` : onglets avec `CornerRadius="6,6,0,0"`, onglet actif avec fond légèrement plus sombre que le header, onglets inactifs grisés. Le style est lu depuis `settings.xml` et applicable via un `DynamicResource`.

**10.2.5 — Glisser une palisade sur une autre pour créer un groupe.** Détecter le drag d'une fenêtre palisade (mouvement de la fenêtre via le header). Pendant le drag, si la fenêtre survole une autre palisade (intersection des bounds > 50%), afficher un indicateur visuel sur la palisade cible : bordure en surbrillance avec le texte "Drop to add tab". Au relâchement sur la cible : fermer les deux fenêtres individuelles, générer un `GroupId` commun (nouveau GUID), affecter `TabOrder=0` à la cible et `TabOrder=1` à la source, créer une `TabbedPalisade` contenant les deux ViewModels, enregistrer dans `PalisadesManager`. Sauvegarder les deux modèles avec leur nouveau `GroupId`.

**10.2.6 — Ajouter un onglet à un groupe existant.** Même mécanisme que 10.2.5 : glisser une palisade autonome (ou un groupe entier) sur une `TabbedPalisade` existante. Les palisades du groupe source sont ajoutées comme onglets supplémentaires à la fin du `TabControl`. Les `TabOrder` sont recalculés séquentiellement.

**10.2.7 — Détacher un onglet.** Shift + glisser un onglet hors de la `TabbedPalisade`. Si le glisser dépasse 60px à l'extérieur des bounds de la fenêtre, afficher un indicateur "Drop to detach". Au relâchement : retirer le ViewModel du groupe, mettre son `GroupId` à null, créer une fenêtre palisade individuelle à la position du curseur, recalculer les `TabOrder` des onglets restants. Si le groupe ne contient plus qu'un seul onglet après détachement, dissoudre le groupe : remplacer la `TabbedPalisade` par une fenêtre palisade individuelle, mettre le `GroupId` à null.

**10.2.8 — Réordonner les onglets.** Shift + glisser un onglet latéralement dans la barre d'onglets (sans sortir des bounds) pour changer sa position. Le `TabOrder` est mis à jour. Le menu contextuel de chaque onglet (clic-droit sur l'en-tête d'onglet) propose aussi "Move Left" et "Move Right".

**10.2.9 — Menu contextuel d'onglet.** Clic-droit sur un header d'onglet : "Move Left", "Move Right", "Detach", "Close Tab" (supprime la palisade du groupe et la supprime définitivement après confirmation), "Edit" (ouvre le dialogue d'édition spécifique au type de la palisade de cet onglet).

**10.2.10 — Chargement des groupes dans `PalisadesManager.LoadPalisades()`.** ✅ Fait. Regroupement par `GroupId`, tri par `TabOrder`, création d’une `TabbedPalisade` par groupe, enregistrement de chaque membre dans `palisades` par identifiant. Suppression d’un onglet (DeletePalisade) : détachement du groupe, dissolution si un seul membre restant.

---

### 10.3 — Snapshots de layout

**10.3.1 — Modèle `LayoutSnapshot`.** ✅ Fait. `Model/LayoutSnapshot.cs` et `SnapshotEntry` dans le même fichier. Sérialisation XML.

**10.3.2 — Service `LayoutSnapshotService`.** ✅ Fait. `SaveSnapshot(name)` (avec GroupId/TabOrder par state.xml), `ListSnapshots()`, `RestoreSnapshot(id)` (CloseAllPalisades, vide saved, écrit entries, LoadPalisades, ApplyRescaleIfNeeded), `DeleteSnapshot(id)`, `RenameSnapshot`, `ExportSnapshot`, `ImportSnapshot`, `SaveAutoSnapshotAndPrune(3)`.

**10.3.3 — Recalcul de positions au restore.** ✅ Fait. `ApplyRescaleIfNeeded` + `PalisadesManager.ApplyRescale(oldW, oldH, newW, newH)` : scale et clamp (min 200×100, visible à l’écran).

**10.3.4 — Dialogue de sauvegarde `SaveSnapshotDialog.xaml`.** ✅ Fait. Nom pré-rempli "Layout - {date}", bouton Save.

**10.3.5 — Dialogue de gestion `ManageSnapshotsDialog.xaml`.** ✅ Fait. Liste (nom, date, résolution, nb palisades), Restore / Rename / Delete par ligne, Export… / Import… globaux. `RenameSnapshotInputDialog` pour renommer.

**10.3.6 — Intégration dans les menus contextuels.** ✅ Fait. Sous-menu "Layouts" (Save current layout…, 5 derniers snapshots, Manage layouts…) dans Palisade, FolderPortal, TaskPalisade, CalendarPalisade, MailPalisade. Commandes dans ViewModelBase et PalisadeViewModel.

**10.3.7 — Snapshot automatique.** ✅ Fait. Au exit : `SaveAutoSnapshotAndPrune(3)` (nom "Auto-save - {date}", garde les 3 derniers).

---

## Phase 10 — État actuel (implémenté)

**10.1 — Création par clic-droit glissé.** ✅ `DesktopDrawingOverlay` : rectangle de sélection au clic-droit + glisser (seuil 5 px, min 100×80), menu "Standard Palisade", "Folder Portal", "Task Palisade", "Calendar Palisade", "Mail Palisade" avec création positionnée. Surcharges `(x, y, width, height)` dans `PalisadesManager`.

**10.2 — Onglets (groupes).** ✅ `PalisadeModelBase` : `GroupId`, `TabOrder`. `TabStyle` (Flat, Rounded), `AppSettings` / `AppSettingsStore` (settings.xml). `PalisadeGroup`, `IPalisadeViewModel`. Fenêtre `TabbedPalisade.xaml` (header + TabControl, DataTemplates par type, styles Flat/Rounded). `LoadPalisades()` : regroupement par `GroupId`, création d’une `TabbedPalisade` par groupe. `DeletePalisade` : détachement d’onglet, dissolution si un seul membre. Drag-to-group, détacher onglet, réordonner, menu contextuel d’onglet : à compléter ultérieurement.

**10.3 — Snapshots de layout.** ✅ Modèle, service (Save, List, Restore, Delete, Rename, Export, Import, rescale), dialogues Save / Manage / Rename, sous-menu Layouts dans tous les menus contextuels, auto-save au exit (3 derniers).

---

## Ordre d'exécution recommandé pour Cursor

Exécuter les phases dans l'ordre numérique. Chaque phase est un ensemble de commits cohérent et testable. Phase 0 est un prérequis absolu. Phase 1 doit être terminée avant toute Phase 2+. Phase 3 doit être terminée avant Phase 4, 5, 6. Phases 5 et 6 sont parallélisables entre elles. Phase 7 peut être commencée dès que Phase 3.4 est en place. Phase 8 est continue (ajouter des tests au fur et à mesure).

---

## Récapitulatif des types de palisades en fin de plan

À l'issue de toutes les phases, l'application propose cinq types de palisades : **Standard** (raccourcis drag-drop, existant), **Folder Portal** (mini explorateur de fichiers, existant), **Task Palisade** (tâches CalDAV synchronisées avec Zimbra, existant mais à compléter), **Calendar Palisade** (affichage des calendriers CalDAV Zimbra en mode agenda/jour/semaine, nouveau), **Mail Palisade** (compteur et afficheur de mails non lus via IMAP Zimbra, nouveau). Les trois types Zimbra partagent un système de comptes centralisé avec credentials chiffrés DPAPI.