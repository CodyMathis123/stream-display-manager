# Stream Display Manager

A small Windows user-session companion for reliable game streaming across physical and virtual displays.

**Status:** Phase 2C experimental: automatic one-shot window relocation for DOOM: The Dark Ages during a confirmed Steam/SudoVDA stream. All other games remain read-only. It never changes physical/virtual display topology.

## Intended experience

- Gaming at the desk: physical 3440x1440 ultrawide stays primary.
- Docked Steam Deck: Steam Remote Play uses a 3840x2160 60 Hz SudoVDA virtual display.
- Handheld Steam Deck: Steam Remote Play uses a 1280x800 virtual display.
- When a game opens on the wrong monitor, move that game's top-level window automatically when safe.
- When the stream ends, restore only game windows that this application moved.
- Keep the physical display enabled during streaming.
- Fail safely when Steam, Windows, the virtual driver, or a game behaves unexpectedly.

Steam owns virtual display creation and streaming; the app observes Steam/Windows and now experimentally attempts to move only DOOM's game window if it first opens on the physical monitor.

## Architecture

- [Implementation specification](docs/IMPLEMENTATION_SPEC.md) – behavior, contracts, architecture and safety invariants.
- [Architecture decisions](docs/DECISIONS.md) – agreed defaults and rationale.
- [Delivery plan](docs/DELIVERY_PLAN.md) – phased scope and acceptance criteria.
- [Windows and Steam references](docs/REFERENCES.md).

## Windows installer

A branded, self-contained Windows x64 application is packaged as a **per-user WiX 5 MSI** from CI. It installs a Start Menu shortcut and registers user-login startup; it never installs SudoVDA, a service, or kernel drivers. The MSI and icon are generated on Windows CI; see [packaging and smoke tests](docs/PACKAGING.md). Developer MSI builds are unsigned and should be installed only after reviewing CI and smoke-test results.

## Experimental DOOM auto-move (enabled by default in feature MSI)

- A confirmed Steam Deck streaming session, stable SudoVDA display, and matching `DOOMTheDarkAges` window on the physical display cause an initial `SetWindowPos` move to the virtual display. If two later observations verify that move, and the same HWND returns to the physical display (as DOOM did after its intro cutscenes), **one follow-up correction** is attempted.
- No tray action required. **At most two moves per Steam session**, no third attempt, and no follow-up unless the first was verified. If DOOM is ever observed on the physical monitor during a confirmed streaming session, it is eligible for correction regardless of earlier placement. This prototype deliberately does **not** distinguish a manual relocation from DOOM resetting its display; the two-attempt limit still applies. Starfield and RDR2 remain diagnostics-only.
- Detailed events: `placement_plan`, `move_attempt` (numbered 1 or 2), `move_result`, delayed `move_verification`, and `move_owned_confirmation`. These distinguish a successful native move from DOOM subsequently resetting its display at the menu.
- **Prototype limitation:** an exclusive-fullscreen game may ignore Win32 placement. A crash-safe restoration journal is not implemented. Keep access to the host's physical monitor and use DOOM's display menu if needed. This build is not a stable release.

## Phase 1 observer

The first implementation targets Windows 10/11 with .NET 10 and runs inside the logged-on interactive user session, rather than as a Windows service. It captures display and game-window observations to local JSONL logs. It does not alter monitor configurations. DOOM's experimental game-window move is the only mutation in this feature-branch build.

When the Phase 1 app is available, use a Windows machine with .NET 10 SDK:

~~~powershell
dotnet build StreamDisplayManager.slnx -c Release
dotnet run --project src/StreamDisplayManager.App
~~~

Logs will be written beneath %LOCALAPPDATA%/StreamDisplayManager/logs. Do not upload diagnostic logs publicly without reviewing process names, device identifiers and desktop metadata.

## Contributing

Work on feature branches and use pull requests into Production. Do not change display topology by default. Experiment with window placement only on the designated test game and log all native outcomes.
