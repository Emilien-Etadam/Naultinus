# Intégration CalDAV avec Naultinus

## Configuration requise

Pour utiliser la fonctionnalité de Task Naultinus avec synchronisation CalDAV, vous avez besoin :

1. **Un serveur CalDAV compatible** (Nextcloud, Baïkal, Radicale, etc.)
2. **Les informations de connexion** : URL du serveur, nom d'utilisateur, mot de passe
3. **L'ID de la liste de tâches** que vous souhaitez synchroniser

## Configuration d'un serveur CalDAV

### Nextcloud
1. Installez Nextcloud sur votre serveur
2. Activez l'application "Tasks" ou "Calendar"
3. L'URL CalDAV sera généralement : `https://votre-domaine.com/remote.php/dav/calendars/nom-utilisateur/`
4. Créez une liste de tâches dans l'interface web
5. L'ID de la liste de tâches est généralement le nom de la liste

### Baïkal
1. Installez Baïkal sur votre serveur
2. Configurez un utilisateur et un calendrier
3. L'URL CalDAV sera : `https://votre-domaine.com/baikal/cal.php/calendars/nom-utilisateur/`

## Utilisation dans Naultinus

### Création d'une Task Naultinus
1. Cliquez droit sur le bureau et sélectionnez "Nouvelle Task Naultinus"
2. Entrez les informations suivantes :
   - **Titre** : Nom de votre Naultinus de tâches
   - **URL du serveur CalDAV** : URL complète de votre serveur CalDAV
   - **Nom d'utilisateur** : Votre nom d'utilisateur CalDAV
   - **Mot de passe** : Votre mot de passe CalDAV (sera chiffré localement)
   - **ID de la liste de tâches** : ID ou nom de votre liste de tâches

### Fonctionnalités disponibles
- **Synchronisation automatique** : Toutes les 5 minutes
- **Synchronisation manuelle** : Cliquez sur le bouton 🔄
- **Ajout de tâches** : Cliquez sur le bouton ➕
- **Marquage comme complétée** : Cochez la case à côté de la tâche
- **Suppression de tâches** : Cliquez sur le bouton 🗑
- **Édition des tâches** : Cliquez sur le bouton 📝

### Personnalisation
Vous pouvez personnaliser :
- La couleur de l'en-tête
- La couleur du corps
- Le titre de la Naultinus
- La fréquence de synchronisation (dans les paramètres avancés)

## Sécurité

### Chiffrement des informations d'identification
- Les mots de passe sont chiffrés localement utilisant AES-256
- La clé de chiffrement est dérivée du nom d'utilisateur
- Les données chiffrées sont stockées dans le fichier de configuration XML

### Recommandations de sécurité
1. Utilisez toujours HTTPS pour les connexions CalDAV
2. Utilisez des mots de passe forts pour vos comptes CalDAV
3. Considérez l'utilisation de l'authentification à deux facteurs si supportée
4. Sauvegardez régulièrement vos données CalDAV

## Dépannage

### Problèmes courants

**"Failed to connect to CalDAV server"**
- Vérifiez que l'URL du serveur est correcte
- Assurez-vous que le serveur est accessible depuis votre réseau
- Vérifiez vos informations d'identification

**"No task lists found"**
- Assurez-vous que vous avez créé au moins une liste de tâches sur le serveur
- Vérifiez que vous avez les permissions appropriées
- L'URL pourrait nécessiter un chemin spécifique pour les tâches

**"Sync failed"**
- Vérifiez votre connexion internet
- Assurez-vous que le serveur CalDAV est en ligne
- Les conflits de synchronisation peuvent parfois se produire

### Journalisation
Les erreurs de synchronisation sont affichées dans l'interface utilisateur de la Task Naultinus. Pour des informations plus détaillées, vous pouvez activer la journalisation dans les paramètres avancés.

## Développement

### Structure du code
- `CalDAVService.cs` : Gère la communication avec le serveur CalDAV
- `TaskNaultinusViewModel.cs` : Gère la logique métier et la synchronisation
- `CredentialEncryptor.cs` : Gère le chiffrement des informations d'identification

### Dépendances
- Ical.Net : Pour la manipulation des données iCalendar (RFC 5545)
- Communication CalDAV : Requêtes HTTP directes (PROPFIND, REPORT, PUT, DELETE)

### Tests
Des tests unitaires sont disponibles dans `CalDAVServiceTests.cs`. Pour exécuter les tests avec un serveur CalDAV réel, vous devrez configurer un serveur de test et mettre à jour les informations d'identification dans les tests.

## Exemples de configuration

### Nextcloud
```
URL du serveur : https://cloud.example.com/remote.php/dav/calendars/username/
Nom d'utilisateur : username
Mot de passe : votre-mot-de-passe
ID de la liste : personal
```

### Baïkal
```
URL du serveur : https://cal.example.com/baikal/cal.php/calendars/username/
Nom d'utilisateur : username
Mot de passe : votre-mot-de-passe
ID de la liste : work-tasks
```

### Radicale
```
URL du serveur : https://radicale.example.com/username/calendar/
Nom d'utilisateur : username
Mot de passe : votre-mot-de-passe
ID de la liste : tasks
```