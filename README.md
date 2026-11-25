# Stream Display Manager

A small Windows user-session companion for reliable game streaming across physical and virtual displays.

**Status:** Phase 2C experimental: automatic game-window placement during confirmed Steam/SudoVDA streaming. Works with recognized games and other installed Steam games whose running executables can be matched to their Steam installation folders. Never changes display topology.

## Intended experience

- Gaming at the desk: physical 3440x1440 ultrawide stays primary.
- Docked Steam Deck: Steam Remote Play uses a 3840x2160 60 Hz SudoVDA virtual display.
- Handheld Steam Deck: Steam Remote Play uses a 1280x800 virtual display.
- When a game opens on the wrong monitor, move that game's top-level window automatically when safe.
- When the stream ends, restore only game windows that this application moved.
- Keep the physical display enabled during streaming.
- Fail safely when Steam, Windows, the virtual driver, or a game behaves unexpectedly.

Steam owns virtual display creation and streaming. The app observes Windows and Steam, identifies the streamed game's window, and experimentally corrects its placement on SudoVDA when necessary.

## Architecture

- [Implementation specification](docs/IMPLEMENTATION_SPEC.md) – behavior, contracts, architecture and safety invariants.
- [Architecture decisions](docs/DECISIONS.md) – agreed defaults and rationale.
- [Delivery plan](docs/DELIVERY_PLAN.md) – phased scope and acceptance criteria.
- [Windows and Steam references](docs/REFERENCES.md).

## Windows installer

A branded, self-contained Windows x64 application is packaged as a **per-user WiX 5 MSI** from CI. It installs a Start Menu shortcut and registers user-login startup; it never installs SudoVDA, a service, or kernel drivers. The MSI and icon are generated on Windows CI; see [packaging and smoke tests](docs/PACKAGING.md). Developer MSI builds are unsigned and should be installed only after reviewing CI and smoke-test results.

## Experimental automatic Steam game placement

- Requires a confirmed Steam Remote Play session, stable SudoVDA topology, and an identifiable game window on the physical monitor.
- Identification is general: known App ID-to-process rules for DOOM, Starfield and RDR2, plus executable-path matching against the active Steam App ID's installed game directory (including additional Steam library folders). When identification fails or windows are ambiguous, the app records the reason and does not move unrelated windows.
- The same SetWindowPos relocation runs for all eligible games. One initial correction, plus **one follow-up** if the first was seen on SudoVDA (immediately after a successful native API call or in delayed verification) and the **same game process** returns to physical. Games may recreate the HWND at launch; a changed handle with the same PID is eligible, but a changed PID is not. No further attempts in that stream. No tray action.
- Log events: game_identity; placement_plan with PID/process and identity source; move_attempt with attempt number, source/target current resolutions and window style; move_result with API/error and immediate geometry; move_immediate_confirmation; delayed move_verification including same-PID replacement windows; move_followup with old/new HWND; move_owned_confirmation; move_skipped; move_suppressed.
- **Critical limitation:** changing Windows window coordinates does not change the monitor a game selected internally or its resolution options. DOOM may display on SudoVDA while its settings still say Dell and cap resolution at Dell's maximum (3440×1440). The game's own selected display may still need changing to offer 4K; SudoVDA also needs the requested mode configured.
- Multiple candidate game windows are not guessed; some exclusive fullscreen games may ignore movement, and there is no crash-safe restore journal yet. Keep the host physical display accessible. This is a feature-branch experiment, not a stable release.

## Phase 1 observer

The first implementation targets Windows 10/11 with .NET 10 and runs inside the logged-on interactive user session, rather than as a Windows service. It captures display and game-window observations to local JSONL logs. It does not alter monitor configurations. Game-window relocation with SetWindowPos is the only mutation in this feature-branch build.

When the Phase 1 app is available, use a Windows machine with .NET 10 SDK:

~~~powershell
dotnet build StreamDisplayManager.slnx -c Release
dotnet run --project src/StreamDisplayManager.App
~~~

Logs will be written beneath %LOCALAPPDATA%/StreamDisplayManager/logs. Do not upload diagnostic logs publicly without reviewing process names, device identifiers and desktop metadata.

## Contributing

Work on feature branches and use pull requests into Production. Do not change display topology by default. Keep game identification and no-guessing preflight checks for window movement, and log all native outcomes.
