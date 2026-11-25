# Stream Display Manager

A small Windows user-session companion for reliable game streaming across physical and virtual displays.

**Status:** architecture established; Phase 1 read-only observer in progress. This repository does not yet move windows or change display topology.

## Intended experience

- Gaming at the desk: physical 3440x1440 ultrawide stays primary.
- Docked Steam Deck: Steam Remote Play uses a 3840x2160 60 Hz SudoVDA virtual display.
- Handheld Steam Deck: Steam Remote Play uses a 1280x800 virtual display.
- When a game opens on the wrong monitor, move that game's top-level window automatically when safe.
- When the stream ends, restore only game windows that this application moved.
- Keep the physical display enabled during streaming.
- Fail safely when Steam, Windows, the virtual driver, or a game behaves unexpectedly.

Steam owns virtual display creation and streaming; this app orchestrates, observes and, in later phases, manages window placement.

## Architecture

- [Implementation specification](docs/IMPLEMENTATION_SPEC.md) – behavior, contracts, architecture and safety invariants.
- [Architecture decisions](docs/DECISIONS.md) – agreed defaults and rationale.
- [Delivery plan](docs/DELIVERY_PLAN.md) – phased scope and acceptance criteria.
- [Windows and Steam references](docs/REFERENCES.md).

## Windows installer

A branded, self-contained Windows x64 application is packaged as a **per-user WiX 5 MSI** from CI. It installs a Start Menu shortcut and registers user-login startup; it never installs SudoVDA, a service, or kernel drivers. The MSI and icon are generated on Windows CI; see [packaging and smoke tests](docs/PACKAGING.md). Developer MSI builds are unsigned and should be installed only after reviewing CI and smoke-test results.

## Phase 1 observer

The first implementation targets Windows 10/11 with .NET 10 and runs inside the logged-on interactive user session, rather than as a Windows service. It captures display and game-window observations to local JSONL logs. It does not alter monitor configurations.

When the Phase 1 app is available, use a Windows machine with .NET 10 SDK:

~~~powershell
dotnet build StreamDisplayManager.slnx -c Release
dotnet run --project src/StreamDisplayManager.App
~~~

Logs will be written beneath %LOCALAPPDATA%/StreamDisplayManager/logs. Do not upload diagnostic logs publicly without reviewing process names, device identifiers and desktop metadata.

## Contributing

Work on feature branches and use pull requests into Production. Keep unsafe display topology changes behind explicit user opt-in, dry-run verification, and a documented recovery procedure.
