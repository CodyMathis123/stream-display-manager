# Stream Display Manager

A small Windows user-session companion for reliable game streaming across physical and virtual displays.

**Status:** Experimental feature branch: automatic game-window placement plus an **opt-in virtual-only topology strategy** for confirmed Steam/SudoVDA streams. The new strategy changes Windows active display paths; it is not enabled by default until tested on the host.

## Intended experience

- Gaming at the desk: physical 3440x1440 ultrawide stays primary.
- Docked Steam Deck: Steam Remote Play uses a 3840x2160 60 Hz SudoVDA virtual display.
- Handheld Steam Deck: Steam Remote Play uses a 1280x800 virtual display.
- When a game opens on the wrong monitor, move that game's top-level window automatically when safe.
- When the stream ends, restore only game windows that this application moved.
- Choose whether to retain the physical display during streaming or temporarily make SudoVDA the sole active display.
- Fail safely when Steam, Windows, the virtual driver, or a game behaves unexpectedly.

Steam owns virtual display creation and streaming. The app observes Windows and Steam, identifies the streamed game's window, and experimentally corrects its placement on SudoVDA when necessary.

## Architecture

- [Implementation specification](docs/IMPLEMENTATION_SPEC.md) – behavior, contracts, architecture and safety invariants.
- [Architecture decisions](docs/DECISIONS.md) – agreed defaults and rationale.
- [Delivery plan](docs/DELIVERY_PLAN.md) – phased scope and acceptance criteria.
- [Windows and Steam references](docs/REFERENCES.md).

## Windows installer

A branded, self-contained Windows x64 application is packaged as a **per-user WiX 5 MSI** from CI. It installs a Start Menu shortcut and registers user-login startup; it never installs SudoVDA, a service, or kernel drivers. The MSI and icon are generated on Windows CI; see [packaging and smoke tests](docs/PACKAGING.md). Developer MSI builds are unsigned and should be installed only after reviewing CI and smoke-test results.

## Experimental virtual-only display strategy

**Try it:** Install the feature-branch MSI, open the tray menu, and check **Use virtual-only during streaming**. The selection is saved under `%LOCALAPPDATA%\\StreamDisplayManager\\settings.json` and is reused on restart and normal per-user MSI upgrades. The existing window-placement strategy stays enabled when this is unchecked. Start a **new** Steam Remote Play session (preferably before launching RDR2) and check whether RDR2 now enumerates SudoVDA and exposes the intended modes. No physical monitor is disabled before Steam streaming and a stable SudoVDA target are confirmed.

- The app snapshots the active Windows CCD display paths/modes into `%LOCALAPPDATA%\\StreamDisplayManager\\display-restore.json` before switching, validates the new configuration and verifies that SudoVDA is the sole active output.
- On explicit stream end, lost SudoVDA, strategy change or normal app exit, it attempts to restore the saved configuration. If Steam already detached SudoVDA and Windows brought back the physical desktop, the app recognizes that recovery and clears the stale journal instead of repeating an invalid CCD restore. Startup attempts journal recovery if the physical display remains disconnected.
- Changes are deliberately **not** saved to the Windows display persistence database. A reboot should normally restore the desktop configuration Windows last saved, but test this on your hardware; reboot is not a substitute for manual recovery.
- Manual recovery: choose **Restore physical display now** in the tray menu, or run `StreamDisplayManager.App.exe --restore` in the installed application directory from the **same user session**. If the saved topology is no longer usable, `StreamDisplayManager.App.exe --extend` requests Windows' extend topology as a last-resort manual fallback.
- A crash or forced process termination cannot execute the normal restore handler. The journal survives and will be examined at next startup, but no separate watchdog is installed. Before enabling, ensure you can reboot the machine or access it remotely if needed.
- This is an *experimental* CCD path change. Windows may reject a topology if the virtual adapter or driver cannot support it; rejection is logged and the existing configuration should remain active. If validation fails after mutation, a restore is attempted.
- SudoVDA still needs to expose a 3840x2160 mode for RDR2 to offer 4K. Sole-display topology is not itself a way to create unsupported modes.

## Experimental automatic Steam game placement

- Requires a confirmed Steam Remote Play session, stable SudoVDA topology, and an identifiable game window on the physical monitor.
- Identification is general: known App ID-to-process rules for DOOM, Starfield and RDR2, plus executable-path matching against the active Steam App ID's installed game directory (including additional Steam library folders). When identification fails or windows are ambiguous, the app records the reason and does not move unrelated windows.
- The same SetWindowPos relocation runs for all eligible games. One initial correction, plus **one follow-up** if the first was seen on SudoVDA (immediately after a successful native API call or in delayed verification) and the **same game process** returns to physical. Games may recreate the HWND at launch; a changed handle with the same PID is eligible, but a changed PID is not. No further attempts in that stream. No tray action.
- Log events: game_identity; placement_plan with PID/process and identity source; move_attempt with attempt number, source/target current resolutions and window style; move_result with API/error and immediate geometry; move_immediate_confirmation; delayed move_verification including same-PID replacement windows; move_followup with old/new HWND; move_owned_confirmation; move_skipped; move_suppressed.
- **Critical limitation:** changing Windows window coordinates does not change the monitor a game selected internally or its resolution options. DOOM may display on SudoVDA while its settings still say Dell and cap resolution at Dell's maximum (3440×1440). The game's own selected display may still need changing to offer 4K; SudoVDA also needs the requested mode configured.
- Multiple candidate game windows are not guessed; some exclusive fullscreen games may ignore movement, and there is no crash-safe restore journal yet. Keep the host physical display accessible. This is a feature-branch experiment, not a stable release.

## Phase 1 observer

The app targets Windows 10/11 with .NET 10 and runs in the logged-on interactive user session, not as a Windows service. It records display/window observations in local JSONL logs. Window relocation is the default; virtual-only active-path switching is optional.

When the Phase 1 app is available, use a Windows machine with .NET 10 SDK:

~~~powershell
dotnet build StreamDisplayManager.slnx -c Release
dotnet run --project src/StreamDisplayManager.App
~~~

Logs will be written beneath %LOCALAPPDATA%/StreamDisplayManager/logs. Do not upload diagnostic logs publicly without reviewing process names, device identifiers and desktop metadata.

## Contributing

Work on feature branches and use pull requests into Production. Keep topology changes opt-in until host validation. Keep game identification and no-guessing preflight checks for window movement, and log all native outcomes.
