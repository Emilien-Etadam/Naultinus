# Naultinus — Agent Instructions

## Cursor Cloud specific instructions

### Overview

Naultinus is a Windows-only WPF/.NET 10 desktop application. The codebase cross-compiles from Linux via `EnableWindowsTargeting`, but **running the app and tests requires Windows** (`Microsoft.WindowsDesktop.App` runtime).

### Build commands

```bash
dotnet restore Naultinus.sln -p:EnableWindowsTargeting=true
dotnet build Naultinus.sln -p:EnableWindowsTargeting=true
```

The `-p:EnableWindowsTargeting=true` flag is **required on Linux** for both restore and build. The main application csproj already sets this property, but the test project (`Naultinus.Tests`) does not, so the CLI property is needed for the solution-level commands.

### Local toolchain (WSL)

Le SDK épinglé par `global.json` (10.0.112, `rollForward: latestFeature`) est installé **sans sudo**
dans `~/.dotnet`, joignable par `dotnet` grâce à un lien `~/.local/bin/dotnet` ; `DOTNET_ROOT` est
exporté depuis `~/.bashrc`. Installation : `bash dotnet-install.sh --version 10.0.112 --install-dir ~/.dotnet`.
Vérifier avec `dotnet --version`.

Ne pas utiliser `/mnt/c/Program Files/dotnet/dotnet.exe` : l'installation Windows est un SDK 8 et ne
sait pas compiler ce projet (`global.json` la refuse). En revanche, les versions Windows du runtime
et du SDK sont ce qu'il faut **sur le poste** pour exécuter l'application : le build Linux ne fait
que compiler.

### Tests

```bash
dotnet test Naultinus.Tests/Naultinus.Tests.csproj -p:EnableWindowsTargeting=true
```

**Important:** Tests cannot run on Linux because the test host requires `Microsoft.WindowsDesktop.App` (WPF runtime), which is only available on Windows. The CI workflow (`.github/workflows/build.yml`) runs tests on `windows-latest`. On Linux, tests will abort with "Framework: 'Microsoft.WindowsDesktop.App' … No frameworks were found."

### Lint / static analysis

StyleCop.Analyzers is included as a build-time dependency. Analyzer warnings are emitted during `dotnet build`. There is no separate lint command — the build output IS the lint output. Expect ~1750 analyzer warnings (mostly CA1707 naming conventions in tests and CA5369 in serialization code); these are pre-existing.

### Running the application

The application is a WPF desktop app and **cannot run on Linux**. On Windows: `dotnet run --project Naultinus.Application/Naultinus.Application.csproj`.

### Key gotchas

- L’installateur officiel est Inno Setup (`installer/naultinus.iss`), construit par le workflow Release. L’ancien projet Visual Studio Deployment (`.vdproj`) a été retiré.
- DPAPI-based credential encryption tests are skipped on non-Windows platforms.
- No Docker, no databases, no backend services are required. The app connects to user-configured external CalDAV/IMAP servers.
- The .NET 10 SDK required by `global.json` may be absent from the WSL environment (only an older SDK is installed on the Windows side). If `dotnet` is unavailable or reports "A compatible .NET SDK was not found", do not guess at compilation: the change is verified by CI and on the contributor's Windows machine.

## Propriété intellectuelle

Naultinus est inspiré d'un organiseur de bureau propriétaire dont la licence interdit la décompilation. Pour ce produit : **aucun code copié ni adapté**, et ni ses icônes, ni ses textes d'interface, ni ses identifiants internes ne sont versés dans ce dépôt public. Les fonctions inspirées sont spécifiées par **observation du comportement** (voir `docs/PORTAL_EXPLORER_PARITY.md`) et implémentées uniquement avec les **API documentées par Microsoft**.
