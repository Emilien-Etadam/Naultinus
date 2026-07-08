<p align="center">
  <img src="logonault.png" alt="Naultinus" width="180"/>
</p>

<h1 align="center">Naultinus</h1>

<p align="center"><em>De petites fenêtres qui s'accrochent au bureau — comme le gecko vert dont l'app tient son nom.</em></p>

<p align="center">
  <a href="https://github.com/Emilien-Etadam/Naultinus/blob/main/LICENSE">
    <img alt="Licence" src="https://img.shields.io/github/license/Emilien-Etadam/Naultinus?style=for-the-badge"/>
  </a>
  <a href="https://github.com/Emilien-Etadam/Naultinus/releases">
    <img alt="Version" src="https://img.shields.io/github/v/release/Emilien-Etadam/Naultinus?label=Version&style=for-the-badge"/>
  </a>
  <a href="https://github.com/Emilien-Etadam/Naultinus/releases">
    <img alt="Téléchargements" src="https://img.shields.io/github/downloads/Emilien-Etadam/Naultinus/total?style=for-the-badge"/>
  </a>
</p>

## Introduction

Naultinus organise le bureau avec de petites fenêtres toujours en arrière-plan (« naultinus ») : raccourcis, mini-explorateur de fichiers, tâches ou calendriers CalDAV, compteur de courriels non lus. Les naultinus restent derrière les autres fenêtres ; on peut les regrouper par onglets et enregistrer ou restaurer des dispositions.

> Le nom vient de *Naultinus*, le genre des geckos verts de Nouvelle-Zélande — discrets, accrochés à leur support, exactement comme ces fenêtres sur le bureau.

## Installation

Téléchargez le dernier installateur sur la page [Releases](https://github.com/Emilien-Etadam/Naultinus/releases), installez Naultinus puis lancez l’application.

## Compiler depuis les sources

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download) (voir `global.json` pour la version exacte du SDK). Cible : `net10.0-windows10.0.17763.0` (Windows 10 version 1809 ou ultérieure).

```bash
git clone https://github.com/Emilien-Etadam/Naultinus.git
cd Naultinus
dotnet restore Naultinus.Application/Naultinus.Application.csproj
dotnet build Naultinus.Application/Naultinus.Application.csproj -c Release
dotnet test Naultinus.Tests/Naultinus.Tests.csproj -c Release
```

La solution contient aussi un projet d’installateur Visual Studio historique (`.vdproj`) ; les commandes ci-dessus ciblent les projets compatibles avec le CLI .NET.

L’exécutable se trouve sous `Naultinus.Application\bin\Release\net10.0-windows10.0.17763.0\Naultinus.exe`.

Les erreurs non gérées, les échecs de chiffrement DPAPI et certains problèmes de snapshots sont consignés dans `%TEMP%\Naultinus_startup.log`. Les traces verbeuses de démarrage ne sont ajoutées qu’en build Debug.

## Fonctionnalités

- **Naultinus raccourcis** : glisser-déposer de raccourcis ; réordonnancement ; nom, couleurs d’en-tête / corps et du texte.
- **Naultinus navigation** : mini-explorateur pour un dossier (fil d’Ariane, ouverture avec l’application par défaut).
- **Naultinus tâches** : liste CalDAV (ex. Zimbra), synchronisation, ajout / édition / complétion / suppression.
- **Naultinus calendrier** : calendriers CalDAV, vue agenda, plusieurs calendriers.
- **Naultinus courriel** : nombre de non lus IMAP et liste optionnelle des sujets (ex. Zimbra), interrogation configurable.
- **Création au tracé** : clic droit + glisser sur le bureau pour dessiner un rectangle, puis choix du type de naultinus.
- **Onglets** : regroupement dans une fenêtre à onglets ; chargement / enregistrement des groupes.
- **Dispositions** : enregistrer l’ensemble des naultinus sous un nom, restaurer plus tard (avec remise à l’échelle si la résolution change). Jusqu’à 5 dispositions récentes dans le menu contextuel ; dialogue de gestion (renommer, supprimer, exporter, importer). Sauvegarde automatique à la sortie (3 dernières dispositions).
- **Zimbra / OVH** : gestion centralisée des comptes (CalDAV + IMAP), identifiants chiffrés (DPAPI), détection automatique optionnelle à partir du courriel.

## Utilisation

- **Raccourcis** : glisser-déposer dans une naultinus raccourcis.
- **Nouvelle naultinus** : menu complet via clic droit sur l’en-tête, ou clic droit + glisser sur le bureau pour en dessiner une nouvelle.
- **Dispositions** : clic droit sur l’en-tête → Dispositions → Enregistrer la disposition actuelle… ou Gérer les dispositions….

## Technique

.NET 10, WPF et Windows Forms. Les erreurs non gérées et plusieurs échecs (ex. chiffrement DPAPI, lecture de snapshot) sont consignés dans `%TEMP%\Naultinus_startup.log`. GongSolutions.WPF.DragDrop pour le glisser-déposer ; MailKit et Ical.Net pour IMAP / iCalendar. Inspiré par [NoFences de Twometer](https://github.com/Twometer/NoFences) et [Fences de Stardock](https://www.stardock.com/products/fences/).
