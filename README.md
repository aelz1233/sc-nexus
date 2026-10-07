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

## Interface

**Contract earnings are optional.** Settings → General → “Include contract earnings in analytics” (also available in Journal). Turn it off to exclude contracts from total and hourly income while keeping saved records. Automatic reward detection is not guaranteed for every contract; verify catalog amounts before saving. Wallet balance is unaffected.

The interface uses one compact flight-operations layout in both themes: persistent navigation, a clear active-ship and available-to-buy budget header, one next action on the dashboard, and one restrained accent colour. Routes keep a fixed profit-first table with investment, profit, and confirmed Pyro/NQA risk; columns cannot be rearranged or resorted. The selected-route inspector stays focused on actionable details; on narrow windows it moves below the table. The optional click-through overlay shows only the ship, location, next action, and an explicit risk warning.

Contract payouts can be looked up by the definition ID recorded in Game.log using SC Wiki. A catalog reward is an estimate for the displayed game version, never a confirmed payment. Missing rewards remain unknown; a returned range is not converted into a single amount. The actual payout must be confirmed before it enters analytics. Acceptance and completion timestamps fill duration when both are available.

<p align="center">
  <img src="docs/screenshots/dashboard.png" width="94%" alt="English flight-operations dashboard — dark theme">
</p>
<p align="center">
  <img src="docs/screenshots/routes-dark-en.png" width="94%" alt="English route planner — dark theme">
</p>
<p align="center">
  <img src="docs/screenshots/routes-light-en.png" width="48%" alt="English route planner — light theme">
  <img src="docs/screenshots/settings-en.png" width="48%" alt="English settings and optional contract earnings">
</p>
<p align="center">
  <img src="docs/screenshots/overlay.png" width="36%" alt="English compact in-game overlay — dark theme">
</p>
<p align="center"><em>English interface: dashboard, dark and light routes, settings, and the compact overlay. Screenshots are rendered from the application using illustrative data, not live market quotes.</em></p>

## Features

| Area | What SC Nexus does |
| --- | --- |
| **Dashboard and player state** | Shows the immediate next action, active ship and capacity, starting location, and available trading budget without mixing it with a ship price or unconfirmed telemetry. |
| **Trade routes** | Uses UEX market quotes, cargo capacity, budget, and safety rules to find direct routes, chains, and multi-stop cargo collection plans. Pyro is explicitly marked as dangerous. |
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

## Language and updates

The installer offers **English / Русский**. On first launch, confirm your language in the bilingual welcome screen. The suggested language comes from the installer, or from Windows for portable installs (English unless Windows uses Russian). Existing profiles keep their saved language across updates. Change it in **Settings → Language**.

Use **Settings → Check for updates** to check GitHub. Confirm the update to download and verify the installer, back up your data, install in the background and restart. Public releases require no GitHub token. The update check stops after 30 seconds if GitHub does not respond.

## OCR and ship detection

**Balance:** enable OCR in Settings and keep the mobiGlas wallet bar (near **Home**) visible with the game in the foreground. Nexus enlarges this region and requires two matching recent readings before changing its balance. Ambiguous results are skipped. Transfer notifications and prices are not balances. A previously saved Nexus balance is kept until a reliable new reading or a manual correction is available.

**Missions:** the dashboard and overlay show active statuses confirmed during the current game session. Older entries remain in Journal. Known log identifiers use readable labels; hover for the original identifier. Game logs do not guarantee a complete list of contracts or their official titles.

OCR is an optional fallback when a value cannot be reliably obtained from `Game.log`, local game data, or APIs. It captures only the visible Star Citizen window, sends the image to the Windows OCR engine locally, and releases the image immediately after recognition. No image is uploaded or kept by SC Nexus.

In **Settings → Data and automation**, choose an OCR interval of **5 / 10 / 20 / 30 seconds** (default: **5**), automatic reading of visible ASOP rows, and list scrolling for button scans.

To scan your fleet:

1. Open Star Citizen and keep its window visible.
2. Open the interactive **ASOP terminal** and clear its search/filter.
3. In SC Nexus open **Settings → Overlay** and select **Scan ASOP**. Switch to Star Citizen within three seconds; the game must be the foreground window.
4. Read the result below the button. Recognized ship models and categories are added to your fleet.

With list scrolling enabled, the **Scan ASOP** button reads the visible rows, scrolls to the top, then down through the list (up to 60 scroll steps / 100 seconds). Clear ASOP search filters first and keep the cursor still during the scan. **Esc**, switching away from the game, or losing the terminal stops scanning. Already recognized entries are kept; interrupted scans may be incomplete. An unchanged list is a stopping heuristic, not proof that every ship was found.

**Locked / Заблокировано** entries are excluded. Rows whose status cannot be read are also skipped. Claim, destroyed and delivery entries are included when recognized. Models are deduplicated; an unlocked copy is kept even when another copy of that model is locked. Categories use UEX metadata with name-based rules as a fallback. Existing ship notes are preserved; scanning does not delete existing fleet entries.

English and Russian Windows OCR are used together when installed. For Russian terminal statuses, install Russian OCR in Windows language settings. The button works independently of periodic OCR. No retrieve, claim or purchase buttons are pressed. The overlay stays click-through and never intercepts game input. OCR can fail on a hidden/minimized window, unreadable text or unsupported languages.

### Overlay controls and checklists

- Assign a shortcut in **Settings → Game overlay**. No shortcut is enabled by default. Nexus uses a Windows hotkey plus a read-only check of the assigned chord while Star Citizen is active; it does not install keyboard hooks in the game. Use borderless/windowed mode and run both applications with the same Windows privileges if the shortcut only works outside the game.
- Configure compact or expanded view, visibility, and positioning in **Settings → Overlay**. The in-game panel contains no controls, so every click passes through to the game.
- The compact view keeps only ship, location, next action, and explicit risk. The expanded view adds the next route stop when one is available.
- Component shopping plans remain in the loadout workflow. Install purchased parts through Vehicle Loadout in the game.
- Shopping plans offer lowest price, fewer flights, and a balanced option. Stop order is a proximity heuristic, not a guaranteed fastest route or a live stock reservation.

## Data sources

| Source | Used for | Notes |
| --- | --- | --- |
| [UEX Corp](https://uexcorp.space/) | Commodity prices, terminal availability, vehicle catalog, and component shop offers | Community-supplied economy data; confirm live terminal values before buying. |
| [Star Citizen Wiki API](https://api.star-citizen.wiki/developers) | Ship ports, supported component types, item stats, and game-version metadata | Used by the loadout catalog. |
| Local Star Citizen files | Build, environment, logs, session hints, and supported game events | Read-only. No files are modified. |
| GitHub Releases | Application update metadata and verified release assets | A token is only needed if this repository becomes private. |

## Privacy and game safety

SC Nexus reads game files and background data without modifying them. The explicitly requested ASOP scan is the only game UI automation: it moves the pointer over recognized list text and sends mouse-wheel scrolling. It does **not** use DLL injection, memory reading, process hooks, packet interception, or modifications to Star Citizen files.

OCR is disabled by default. When enabled, it only processes the visible Star Citizen window and releases the captured image after recognition. Application data is stored locally under `%LOCALAPPDATA%\SCNexus`.

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

### Contract earnings
Game.log payout notifications near completed contracts are recorded in Journal. Use Settings → Data and automation to include or exclude them from analytics. Missing payouts can be confirmed manually; offer OCR only prefills the amount for verification. The wallet is updated separately by OCR. Hourly income needs known activity duration.

