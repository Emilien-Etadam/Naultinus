# Palisades

<p align="center">
  <a href="https://github.com/Emilien-Etadam/Palisades-Navigation/blob/main/LICENSE">
    <img alt="Licence" src="https://img.shields.io/github/license/Emilien-Etadam/Palisades-Navigation?style=for-the-badge"/>
  </a>
  <a href="https://github.com/Emilien-Etadam/Palisades-Navigation/releases">
    <img alt="Version" src="https://img.shields.io/github/v/release/Emilien-Etadam/Palisades-Navigation?label=Version&style=for-the-badge"/>
  </a>
  <a href="https://github.com/Emilien-Etadam/Palisades-Navigation/releases">
    <img alt="Téléchargements" src="https://img.shields.io/github/downloads/Emilien-Etadam/Palisades-Navigation/total?style=for-the-badge"/>
  </a>
</p>

## Introduction

Palisades organise le bureau avec de petites fenêtres toujours en arrière-plan (« palisades ») : raccourcis, mini-explorateur de fichiers, tâches ou calendriers CalDAV, compteur de courriels non lus. Les palisades restent derrière les autres fenêtres ; on peut les regrouper par onglets et enregistrer ou restaurer des dispositions.

## Installation

Téléchargez le dernier installateur sur la page [Releases](https://github.com/Emilien-Etadam/Palisades-Navigation/releases), installez Palisades puis lancez l’application.

## Compiler depuis les sources

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download) (voir `global.json` pour la version exacte du SDK). Cible : `net10.0-windows10.0.17763.0` (Windows 10 version 1809 ou ultérieure).

```bash
git clone https://github.com/Emilien-Etadam/Palisades-Navigation.git
cd Palisades-Navigation
dotnet restore Palisades.Application/Palisades.Application.csproj
dotnet build Palisades.Application/Palisades.Application.csproj -c Release
dotnet test Palisades.Tests/Palisades.Tests.csproj -c Release
```

La solution contient aussi un projet d’installateur Visual Studio historique (`.vdproj`) ; les commandes ci-dessus ciblent les projets compatibles avec le CLI .NET.

L’exécutable se trouve sous `Palisades.Application\bin\Release\net10.0-windows10.0.17763.0\Palisades.exe`.

Les erreurs non gérées, les échecs de chiffrement DPAPI et certains problèmes de snapshots sont consignés dans `%TEMP%\Palisades_startup.log`. Les traces verbeuses de démarrage ne sont ajoutées qu’en build Debug.

## Fonctionnalités

- **Palisade raccourcis** : glisser-déposer de raccourcis ; réordonnancement ; nom, couleurs d’en-tête / corps et du texte.
- **Palisade navigation** : mini-explorateur pour un dossier (fil d’Ariane, ouverture avec l’application par défaut).
- **Palisade tâches** : liste CalDAV (ex. Zimbra), synchronisation, ajout / édition / complétion / suppression.
- **Palisade calendrier** : calendriers CalDAV, vue agenda, plusieurs calendriers.
- **Palisade courriel** : nombre de non lus IMAP et liste optionnelle des sujets (ex. Zimbra), interrogation configurable.
- **Création au tracé** : clic droit + glisser sur le bureau pour dessiner un rectangle, puis choix du type de palisade.
- **Onglets** : regroupement dans une fenêtre à onglets ; chargement / enregistrement des groupes.
- **Dispositions** : enregistrer l’ensemble des palisades sous un nom, restaurer plus tard (avec remise à l’échelle si la résolution change). Jusqu’à 5 dispositions récentes dans le menu contextuel ; dialogue de gestion (renommer, supprimer, exporter, importer). Sauvegarde automatique à la sortie (3 dernières dispositions).
- **Zimbra / OVH** : gestion centralisée des comptes (CalDAV + IMAP), identifiants chiffrés (DPAPI), détection automatique optionnelle à partir du courriel.

## Utilisation

- **Raccourcis** : glisser-déposer dans une palisade raccourcis.
- **Nouvelle palisade** : menu complet via clic droit sur l’en-tête, ou clic droit + glisser sur le bureau pour en dessiner une nouvelle.
- **Dispositions** : clic droit sur l’en-tête → Dispositions → Enregistrer la disposition actuelle… ou Gérer les dispositions….

## Technique

.NET 10, WPF et Windows Forms. Les erreurs non gérées et plusieurs échecs (ex. chiffrement DPAPI, lecture de snapshot) sont consignés dans `%TEMP%\Palisades_startup.log`. GongSolutions.WPF.DragDrop pour le glisser-déposer ; MailKit et Ical.Net pour IMAP / iCalendar. Inspiré par [NoFences de Twometer](https://github.com/Twometer/NoFences) et [Fences de Stardock](https://www.stardock.com/products/fences/).
