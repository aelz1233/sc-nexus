<p align="center">
  <img src="SCNexus/Assets/sc-nexus.png" width="88" alt="SC Nexus logo">
</p>

<h1 align="center">SC Nexus</h1>

<p align="center">
  <strong>Your Star Citizen companion — ships, economy, analytics and game data in one place.</strong>
</p>

<p align="center">
  <a href="https://github.com/aelz1233/sc-nexus/releases/latest"><img src="https://img.shields.io/github/v/release/aelz1233/sc-nexus?display_name=tag&sort=semver&color=50bfae" alt="Latest release"></a>
  <a href="https://github.com/aelz1233/sc-nexus/actions/workflows/windows-build.yml"><img src="https://img.shields.io/github/actions/workflow/status/aelz1233/sc-nexus/windows-build.yml?branch=main&label=build" alt="Windows build"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4" alt=".NET 10">
</p>

<p align="center">
  <a href="#features">Features</a> ·
  <a href="#install">Install</a> ·
  <a href="#data-sources">Data sources</a> ·
  <a href="#roadmap">Roadmap</a> ·
  <a href="README_RU.md">Русский</a>
</p>

SC Nexus is a Windows companion application for **Star Citizen**. It collects available game data without modifying the game process, combines it with market and component data, and keeps trading, fleet, sessions, and game events in one local workspace.

The guiding principle is simple: use automatic data first and ask the player for manual input only when a reliable source is unavailable.

> There are no application screenshots committed to this repository yet. The project logo above is the only current image asset; screenshots will be added only after they are captured from the released application.

## Features

| Area | What SC Nexus does |
| --- | --- |
| **Dashboard and player state** | Shows the current or last session, detected ship and location, active mission, game build, server details, notifications, and personal trading metrics. |
| **Trade routes** | Uses UEX market quotes, stock, demand, cargo capacity, budget, safety rules, and system filters to find direct routes, chains, and multi-stop cargo collection plans. Pyro is explicitly marked as dangerous. |
| **Fleet and loadouts** | Maintains a personal fleet from the UEX vehicle catalog or reliable detections. The loadout planner reads compatible ports and component data, compares builds, and creates purchase checklists. |
| **Game data collection** | Reads new `Game.log` lines incrementally, checks local game files in read-only mode, tracks sessions, movement, missions, deaths, trade events, and supported values. |
| **Overlay and tray** | Provides an optional read-only overlay, configurable hotkey, system-tray controls, and a notification center. It does not inject into Star Citizen or intercept its input. |
| **Local history and safety** | Stores settings, flights, and collected history locally in SQLite; creates rotating backups and can restore the previous app/database state after a failed update. |

## Install

Download the latest release from [GitHub Releases](https://github.com/aelz1233/sc-nexus/releases/latest).

- `SCNexus-Setup-…-win-x64.exe` — installer with a selectable installation folder.
- `SCNexus-…-win-x64.zip` — portable single-file build.

The published builds are self-contained. Existing settings, fleet, balance, history, and caches live outside the installation folder in `%LOCALAPPDATA%\SCNexus`, so they survive upgrades and reinstallations.

### Requirements

| Requirement | Details |
| --- | --- |
| Operating system | Windows 10 version 1809 or later, or Windows 11 |
| Architecture | x64-compatible CPU |
| Game integration | A local LIVE, PTU, or EPTU installation is detected automatically when available |
| Network | Needed only for UEX, Star Citizen Wiki, GitHub updates, and optional data refreshes |

## Run from source

The repository targets .NET 10 and WPF.

```powershell
dotnet run --project SCNexus/SCNexus.csproj
```

Run the automated test suite with:

```powershell
dotnet test SCNexus.slnx -c Release
```

<details>
<summary><strong>Build a self-contained Windows executable</strong></summary>

```powershell
dotnet publish SCNexus/SCNexus.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The GitHub Actions workflow runs tests, builds this executable, builds the Inno Setup installer, verifies install/upgrade/uninstall behavior, and publishes release assets.
</details>

## Automatic data collection

Every collected value carries its source, timestamp, and confidence. Providers are evaluated by priority:

1. `Game.log` and `logbackups`, following only new lines after the initial tail;
2. local Star Citizen installation files, read-only;
3. UEX and Star Citizen Wiki APIs;
4. optional OCR of the foreground Star Citizen window;
5. previously saved Nexus history;
6. manual values when no automatic source is available.

The app detects LIVE, PTU, and EPTU folders on first launch. Manual selection is available when automatic discovery cannot find the game.

## Data sources

| Source | Used for | Notes |
| --- | --- | --- |
| [UEX Corp](https://uexcorp.space/) | Commodity prices, terminal availability, vehicle catalog, and component shop offers | Community-supplied economy data; confirm live terminal values before buying. |
| [Star Citizen Wiki API](https://api.star-citizen.wiki/developers) | Ship ports, supported component types, item stats, and game-version metadata | Used by the loadout catalog. |
| Local Star Citizen files | Build, environment, logs, session hints, and supported game events | Read-only. No files are modified. |
| GitHub Releases | Application update metadata and verified release assets | A token is only needed if this repository becomes private. |

## Privacy and game safety

SC Nexus is an external, read-only helper. It does **not** use DLL injection, memory reading, process hooks, packet interception, or modifications to Star Citizen files.

OCR is disabled by default. When enabled, it only processes the foreground Star Citizen window and releases the captured image after recognition. Application data is stored locally under `%LOCALAPPDATA%\SCNexus`.

## Project structure

```text
SC-Nexus/
├── SCNexus/                 WPF application
│   ├── Assets/              Application logo and icons
│   ├── Controls/            Reusable UI controls and loadout view
│   ├── Data/                Entity Framework SQLite context
│   ├── Models/              Game, trade, fleet, and collected-data models
│   ├── Services/            Providers, parsing, routes, updates, and persistence
│   └── ViewModels/          Application state and UI commands
├── SCNexus.Tests/           xUnit regression and smoke tests
├── installer/               Inno Setup installer definition
├── scripts/                 Release and installer helpers
├── docs/                    Versioned release notes
├── .github/                 CI workflow and collaboration templates
└── VERSION                  Release version used by CI
```

## Development status

SC Nexus is actively developed as a personal companion application. The current release includes a production Windows installer and automated CI checks, while Star Citizen log coverage and provider quality continue to evolve with game builds.

## Roadmap

- ✅ Windows WPF companion, local SQLite storage, installer, portable build, and GitHub release automation
- ✅ Fleet management, trade recommendations, chains, cargo collection, loadout planning, overlay, tray controls, and bilingual UI
- ✅ Provider-based automatic data collection with `Game.log`, local game files, UEX, Star Citizen Wiki, OCR, and local-history fallbacks
- 🚧 Broader parsing coverage for new or changed Star Citizen log events
- 🚧 More resilient provider diagnostics and data-quality reporting across game patches
- 📋 Additional data providers using the existing `IDataProvider` extension point
- 📋 Official interface screenshots captured from released builds

## Known limitations

- Public local game data does not reliably expose a complete account fleet, current installed loadout, personal inventory, blueprints, exact wallet balance, or every mission, death, and location event.
- The application stores and displays a value only when a supported provider, log event, or enabled OCR result can supply it. It does not invent missing game data.
- UEX availability, stock, demand, and prices can change in game. Verify a terminal before making a large purchase.
- The overlay remains external and read-only, so it cannot access information unavailable to the configured providers.

## Contributing

Bug reports, improvement ideas, and focused pull requests are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), use the supplied issue templates, and keep changes small enough to test independently.

## License

No open-source license has been selected for this repository yet. Until one is added, the project remains under the copyright of its owner and contributions should be discussed in an issue before substantial work begins.

## Acknowledgements

SC Nexus is an unofficial fan-made companion. Star Citizen and related marks belong to Cloud Imperium Games. UEX and Star Citizen Wiki are independent community data sources.
