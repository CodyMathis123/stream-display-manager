# Native design and validation

Stream Display Manager is a small Windows x64 C++20 tray application. The only supported application source is in the native/ directory. It does not load .NET or WinForms.

## Implementation

Native code is separated into four responsibilities rather than one large tray source: `StreamDisplayManager.cpp` owns Windows message dispatch and the Tick orchestration; `SteamObserver.{h,cpp}` owns host log signals and process checks; `AppRuntime.{h,cpp}` owns local settings, Windows startup, JSONL and mode diagnostics, plus About/version information; `DisplayTopology.h` and the pure `SessionPolicy.h` own CCD recovery and decision policy respectively. No additional service, managed runtime or third-party runtime was introduced.


- Win32 Shell_NotifyIcon, **hidden top-level window** (not message-only, so MSI can send WM_CLOSE), and per-user singleton mutex. Tray About dialog shows the app version read from Windows VERSIONINFO in the running executable.
- Steam host log (streaming_log.txt) opened at EOF; explicit start/end markers, no historical replay.
- QueryDisplayConfig and SetDisplayConfig switch active Windows paths to the sole SudoVDA output when a live stream is confirmed. The switch is not persisted in the Windows display database.
- Saved CCD configuration is stored as a versioned binary journal before mutation. Normal session end and startup recovery restore the physical desktop; the journal is cleared when Steam/Windows already restored it.
- EnumDisplaySettingsExW enumerates every supported resolution and refresh rate on topology changes or demand. The modes log distinguishes active resolution from advertised modes and flags 3840x2160.
- HKCU Run entry starts the native tray at login; the tray menu can toggle startup. The MSI creates the entry initially.
- Per-user virtual-only preference stored under %LOCALAPPDATA%\StreamDisplayManager\settings.json.
- Diagnostics under %LOCALAPPDATA%\StreamDisplayManager\logs\native-events.jsonl.

## Build and installation

.github/workflows/native-ci.yml uses MSVC x64 C++20 /O2 /MT, resource compiler, and WiX 5. The workflow generates VERSIONINFO from the same 1.0.<run> build version used by the MSI and asserts Windows FileVersion and ProductVersion match the MSI's ProductVersion (the EXE uses a four-part version ending in .0). The only MSI is StreamDisplayManager-Setup-<version>-x64.msi. It installs the native app to a per-user directory, sets a normal Start Menu shortcut and an HKCU Run startup registration. No separate service or driver is installed.

Older managed and native preview installers have different identities. Uninstall them first before installing the single native MSI. No automatic compatibility migration is implemented or required.

## MSI tray lifecycle

Interactive MSI installation and upgrade now request a graceful shutdown of the
running Stream Display Manager tray before replacing the native EXE. The hidden
top-level Win32 message window handles WM_CLOSE through its normal WM_DESTROY
path, including display restoration and tray-icon removal. For older
message-only-window builds that cannot receive that message, the MSI terminates
the leftover process after a 15-second timeout. The newly launched instance
will check any existing display recovery journal before observing new streams.

After a successful **interactive** install or upgrade, WiX launches the newly
installed native executable with normal user privileges. Silent MSI operations
do not launch a tray, but the existing HKCU Run entry still starts it at the
next Windows sign-in. The MSI also closes the tray during uninstall; uninstall
does not relaunch it.

The Windows CI pipeline inspects close/launch MSI action scheduling and smoke
tests a real silent install and uninstall while a native tray is running. Its lifecycle smoke test also checks for the app's own `native-stopped` event to establish a graceful exit.

## Validation

- The initial native preview built successfully using MSVC and WiX.
- Live hardware streaming test confirmed SudoVDA-only at 1280x800 and physical monitor restoration to 3440x1440 after the stream.
- Native Windows CI measured a 9.43 MiB working set and 1.61 MiB private bytes at idle; actual desktop observation was approximately 2 MB in Task Manager. These are different metrics and environments.
- During handheld streaming, SudoVDA advertised 3840x2160@60 and @120 even while 1280x800 was active.
- Docked Steam Deck validation has not yet been performed.
- User reported interactive install and ordinary tray behavior working; actual fresh-login behavior and docked 4K remain to be verified if needed.

## Small reliability policy

The existing three-second polling cycle also checks for display drift. If Windows re-enables a physical monitor during a confirmed Steam stream, the native app may re-isolate SudoVDA after the topology stabilizes, with a 15-second cooldown and a maximum of two repair attempts per confirmed session. Repair uses the current active CCD paths and leaves the original pre-stream recovery journal unchanged.

An explicit Steam stream-end marker starts a 12-second reconnection window only while the virtual display remains attached. If a new confirmed stream starts, the pending restoration is canceled. If SudoVDA disappears, the Steam process exits, or log evidence becomes uncertain, restore begins immediately.

Steam log tracking checks file identity and position. When the file is absent at login, replaced, truncated or rotated, observation reopens at EOF and invalidates old session evidence. Historical lines cannot trigger a display switch.

Restoration tries the original CCD paths first, and falls back to Windows' last saved topology if the saved paths fail or do not reactivate a physical display. The recovery journal is retained until the physical desktop is confirmed. The streaming topology is never saved in Windows' display database.

Focused native tests cover these decisions and file-tail scenarios without changing any real display configuration. The timings are deliberately internal constants, not additional user-facing settings.

## Diagnostics and recovery

Right-click tray -> Capture display modes. Compare display_modes events before/after docking. Full GDI-advertised modes do not guarantee a particular game exposes those modes.

Manual commands:

- StreamDisplayManager.Native.exe --modes
- StreamDisplayManager.Native.exe --restore
- StreamDisplayManager.Native.exe --extend
- StreamDisplayManager.Native.exe --self-test

A reboot normally restores the last Windows-saved topology because the temporary switch does not persist it, but unexpected driver failures can require local recovery.

## Local diagnostics (no Windows Event Log integration)

Native diagnostics remain in per-user `%LOCALAPPDATA%\StreamDisplayManager\logs`.
New events use daily `native-events-YYYY-MM-DD.jsonl` filenames, with
`-001`, `-002`, etc. segments if a file reaches about 2 MiB.
Retention is bounded to roughly **14 days / 16 MiB**, applied lazily at
startup/first write of the day and periodically while logging. The original
pre-upgrade `native-events.jsonl` is deliberately left alone as legacy
history and is **not** deleted by retention or included in automated exports.

Each new JSONL record has `atUtc`, `type`, `reason`, `severity`
(`info`/`warning`/`error`) and optional details. Tray-generated events
include a per-process `runId` and events associated with a Steam stream
include `sessionId` as `runId-sessionNumber`. Unlike the old ordinal alone,
that ID remains distinct across tray restarts. A `game-selected` event carries
only the numeric Steam App ID, never the raw Steam streaming log line.

Right-click the tray and choose **Export diagnostics...** to save one readable
`.txt` file with the current version, summarized stream sessions and their
Steam App IDs, and the recent underlying JSONL events (last seven days, most
recent 8 MiB). A confirmation warns about game/display/device metadata before
a native Save dialog opens. The export does not include raw Steam host logs or
the user's Windows account settings. Review the exported file before sharing.

No Windows Event Log source, administrative registration, database, web
viewer, background service or game-specific log folders are introduced.
Log-writing failures are best effort and must never prevent display recovery.

## Secondary SudoVDA isolation: iterative CCD candidate (2026-11-25)

A verified error-87 report affected docked 4K sessions where the physical
3440×1440 display was primary and SudoVDA was already active at 3840×2160
as a secondary display. The older handheld tests had SudoVDA as primary.

For every newly confirmed stream, the utility now selects the sole already
active SudoVDA CCD source and constructs a compact **temporary** single-path
candidate. Its copied source mode retains the active width and height and is
rebased to desktop origin (0,0); referenced source and target modes are
remapped to a compact array, and desktop-image layout is recalculated.
The original CCD paths and modes are **never mutated**; they are saved in
the existing recovery journal only after candidate validation succeeds.

If the explicit active-mode candidate fails Windows `SDC_VALIDATE`, the
utility tests one mode-agnostic candidate and applies it only when that
variant also validates. The fallback uses Windows' best-mode selection and
**might not preserve the client's desired 4K resolution**; the resulting
active SudoVDA mode must be checked in diagnostics.

The tray emits one structured `display_switch` result per attempt with
`stage` (e.g. `validate-explicit`, `validate-auto`, `apply`, `verify`),
original virtual source position, width/height, mode-plan used, validation
error codes and whether a journal was written. No raw CCD identifiers are
exported. If apply fails or virtual-only verification fails, the existing
restoration path is invoked. Display changes are never saved to the Windows
display database. The switch remains attempted only once per Steam session
and repair cooldowns are unchanged.

**Real hardware validation required:** Confirm 4K secondary-to-virtual-only
switch, actual output mode and game selectable modes, reconnect, and physical
desktop recovery. Unit tests cover pure candidate generation only; Windows
CI cannot emulate genuine SudoVDA/Steam behavior.
