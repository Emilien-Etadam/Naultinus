# Intégration CalDAV

Naultinus synchronise les **tâches** (VTODO) et les **calendriers** (VEVENT) via CalDAV. Les comptes Zimbra/OVH peuvent être centralisés dans **Gérer les comptes Zimbra** (CalDAV + IMAP, identifiants chiffrés DPAPI).

## Prérequis

1. Serveur CalDAV compatible (Zimbra, Nextcloud, Baïkal, Radicale…)
2. URL HTTPS, identifiant et mot de passe
3. Pour les tâches : au moins une collection supportant `VTODO`
4. Pour le calendrier : au moins une collection supportant `VEVENT`

### Zimbra / OVH

- CalDAV tâches : `https://<serveur>/dav/<email>/Tasks`
- CalDAV calendriers : `https://<serveur>/dav/<email>/Calendar`
- IMAP : port 993 (SSL), même identifiant

## Création d'une naultinus CalDAV

**Tâches** : menu → **Nouvelle naultinus tâches** → URL, identifiant, mot de passe → **Charger les listes de tâches** → sélectionner une ou plusieurs listes.

**Calendrier** : menu → **Nouvelle naultinus calendrier** → même principe avec les calendriers.

Les comptes enregistrés dans **Gérer les comptes Zimbra** préremplissent URL et identifiant ; le mot de passe est stocké chiffré (DPAPI Windows).

## Fonctionnalités

- Synchronisation périodique et manuelle (bouton actualiser)
- Tâches : ajout, édition, complétion, suppression
- Calendrier : vue agenda, événements multi-calendriers
- HTTPS obligatoire pour toute URL CalDAV saisie manuellement

## Sécurité

- Mots de passe chiffrés avec **DPAPI** (`CredentialEncryptor`) — liés au profil Windows courant
- Toujours utiliser HTTPS
- Fichier des comptes : `%LOCALAPPDATA%\Naultinus\accounts.xml`

## Dépannage

| Symptôme | Pistes |
|----------|--------|
| Échec de connexion | URL, pare-feu, identifiants |
| Aucune liste VTODO | Collection Tasks absente ou URL incorrecte |
| Aucun calendrier | Collection Calendar absente |
| Sync échouée | Réseau, serveur indisponible, conflit côté serveur |

Traces : `%TEMP%\Naultinus_startup.log` (erreurs globales) ; statut affiché dans la naultinus concernée.

## Code

| Fichier | Rôle |
|---------|------|
| `Services/CalDAVClient.cs` | PROPFIND, REPORT, découverte |
| `Services/CalDAVService.cs` | Tâches VTODO |
| `Services/CalendarCalDAVService.cs` | Événements VEVENT |
| `ViewModel/TaskNaultinusViewModel.cs` | Sync tâches |
| `ViewModel/CalendarNaultinusViewModel.cs` | Affichage calendrier |
| `Helpers/CredentialEncryptor.cs` | DPAPI |

Dépendance iCalendar : **Ical.Net**.

Tests : `Naultinus.Tests/Services/CalDAVServiceTests.cs` (certains tests réseau tolèrent l'échec DNS hors ligne).
