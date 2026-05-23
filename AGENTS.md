# Palisades — Agent Instructions

## Cursor Cloud specific instructions

### Overview

Palisades is a Windows-only WPF/.NET 10 desktop application. The codebase cross-compiles from Linux via `EnableWindowsTargeting`, but **running the app and tests requires Windows** (`Microsoft.WindowsDesktop.App` runtime).

### Build commands

```bash
dotnet restore Palisades.sln -p:EnableWindowsTargeting=true
dotnet build Palisades.sln -p:EnableWindowsTargeting=true
```

The `-p:EnableWindowsTargeting=true` flag is **required on Linux** for both restore and build. The main application csproj already sets this property, but the test project (`Palisades.Tests`) does not, so the CLI property is needed for the solution-level commands.

### Tests

```bash
dotnet test Palisades.Tests/Palisades.Tests.csproj -p:EnableWindowsTargeting=true
```

**Important:** Tests cannot run on Linux because the test host requires `Microsoft.WindowsDesktop.App` (WPF runtime), which is only available on Windows. The CI workflow (`.github/workflows/build.yml`) runs tests on `windows-latest`. On Linux, tests will abort with "Framework: 'Microsoft.WindowsDesktop.App' … No frameworks were found."

### Lint / static analysis

StyleCop.Analyzers is included as a build-time dependency. Analyzer warnings are emitted during `dotnet build`. There is no separate lint command — the build output IS the lint output. Expect ~1750 analyzer warnings (mostly CA1707 naming conventions in tests and CA5369 in serialization code); these are pre-existing.

### Running the application

The application is a WPF desktop app and **cannot run on Linux**. On Windows: `dotnet run --project Palisades.Application/Palisades.Application.csproj`.

### Key gotchas

- The `Palisades.Installer` project (`.vdproj`) is an Inno Setup/VS installer project and is ignored by `dotnet build`; it only builds in Visual Studio or MSBuild on Windows.
- DPAPI-based credential encryption tests are skipped on non-Windows platforms.
- No Docker, no databases, no backend services are required. The app connects to user-configured external CalDAV/IMAP servers.
