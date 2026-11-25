# Stream Display Manager — Implementation Specification

Version: 0.1 design baseline (2026-11-25). Target: Windows 10/11, .NET 10, interactive user session.

## 1. Problem statement

Steam Remote Play on Windows can create a SudoVDA virtual monitor matching the client. In a multi-monitor host, applications sometimes launch on or return to an unintended display, and virtual monitor identities/topology can change across connections. The utility should detect the *actual session* and *actual window placement*, correct recognized game windows when safe, and restore its own mutations on disconnect.

Primary scenario: Windows PC with 3440x1440 physical ultrawide; Steam Deck LCD connected locally or docked to 4K60 TV; Steam Remote Play via gigabit LAN.

## 2. Product requirements

R1. Run automatically at the user's interactive login; never require a Windows service.
R2. Enumerate active physical and virtual desktop display paths and their monitor identity, mode, bounds, primary status, and lifetime.
R3. Detect Steam running, likely stream start/end, and a confirmed streaming session. Steam process presence alone is **not** confirmation.
R4. Observe creation, destruction, movement and state of visible top-level windows. Correlate a game window to its own process. Match game identity through authoritative evidence (Steam launch context / configured executable / Steam app metadata), not foreground window title.
R5. When stream confirmed, prefer Steam's chosen virtual display. If the recognized game is already there, do nothing. Otherwise safely move it, respecting DPI and monitor work areas.
R6. Leave physical displays enabled while streaming by default.
R7. Record the placement before a successful automated move and restore only owned, still-valid windows on disconnect.
R8. Recover from rapid connect/disconnect, Steam crashes, driver removal, sleep/resume, app restart and missing display without putting desktop windows off-screen.
R9. Keep durable, bounded logs and provide diagnostics suitable for evaluating why a move was or was not performed.
R10. Support dry-run mode and deterministic tests with synthetic observation sequences.
R11. Never assume a stream is docked merely because resolution is 4K; client capability may suggest a profile, but device identity requires corroboration.

## 3. Non-goals (initial releases)

- Reimplementing SudoVDA, display virtualization, streaming codecs, Steam Remote Play or Moonlight.
- Manipulating non-game applications, arbitrary dialogs, elevated windows, or secure desktops.
- Forcing exclusive fullscreen games to move; game-native settings/borderless may be needed.
- Changing resolution, refresh rate, HDR or primary monitor if Steam has already configured them correctly.
- Automatically disconnecting or disabling physical monitors.
- Remote input/control service, network listener or privileged background service.

## 4. Component boundaries

### Host.App (Windows desktop/tray)
Owns process lifetime, per-user startup, user consent, status, notification, manual capture, and emergency restore UI. Runs in the interactive session with message loop. No Session 0 service.

### Observer.Windows (read-only adapters)
- DisplayWatcher: WM_DISPLAYCHANGE / device events prompt a fresh QueryDisplayConfig snapshot; APIs are also polled periodically for missed transitions.
- DisplayIdentity: joins active path/source/target identifiers with DisplayConfigGetDeviceInfo and adapter/device properties; captures EDID when available.
- WindowWatcher: EnumWindows initial census plus out-of-context SetWinEventHook for lifecycle and location events. Callbacks remain lightweight; all data is reconciled by the observer.
- LifecycleWatcher: WM_POWERBROADCAST, session notifications, app startup/resume. Reconcile on every transition.
- SteamObserver: observes Steam host process plus log activity and/or supported session signals. All log signatures are version-sensitive and must be validated experimentally; an unrecognized format is "unknown", never "inactive".
The first code increment may use a smaller WinForms Screen.AllScreens and EnumWindows census. It is *not* the final authoritative topology detector.

### Core (platform-independent)
A pure engine accepts immutable snapshots and returns a decision plan with explanations. Inputs: previous state, display topology, stream signals with confidence/source, game ownership/identity, window geometry, previous moves, and settings. Outputs: no-op, probe, move, restore, warn, or hold. No Win32 calls or direct side effects.

### Executor.Windows (Phase 2+)
Executes only approved move/restore actions, with validation before and after each action. Uses Win32 SetWindowPlacement/SetWindowPos/MonitorFromWindow and foreground-safe behavior. Exclusive fullscreen is not forcibly manipulated. Phase 3 may add optional SetDisplayConfig functionality, separately consented to.

### Journal & diagnostics
Persist before-action state and the result of each mutation atomically. Maintain a crash-safe session journal and bounded JSONL diagnostics. Never log window titles by default, user input, Steam credentials, or full command lines.

## 5. Identity and signal confidence

Display identity uses *session-local* path/target IDs plus canonical adapter/device identifiers. DEVICE_NAME or Windows "Display 1/2" is diagnostic only, not a durable identity key. If SudoVDA is recreated, match adapter/device properties and session correlation rather than a previously observed path index.

Stream confidence:
- **Unknown:** only Steam process or only ambiguous monitor evidence.
- **Candidate:** Steam process is running and a plausible SudoVDA path is active.
- **Confirmed:** a validated Steam session start/active signal plus a matched SudoVDA path; or equivalent explicit user-confirmed session information.
- **Ended:** positive end/disconnect evidence AND display no longer present (or verified timeout/reconciliation); absence of a log entry alone must not trigger restoration.

Do not automatically move windows until Confirmed. If confirmation cannot be established, remain read-only and surface the reason.

## 6. Session state machine

States: Desktop, Candidate, Preparing, Streaming, Restoring, RecoveryRequired.

Desktop → Candidate on credible but incomplete stream evidence.
Candidate → Preparing on confirmed stream and stable virtual display.
Preparing → Streaming after target display/window validation and any approved placement.
Streaming → Streaming on new game windows or docking resolution change; recalculate desired state.
Streaming → Restoring on verified session end or target removal.
Restoring → Desktop on verified cleanup; otherwise RecoveryRequired and instruct user.
Any state → RecoveryRequired on unresolvable display identity, failed action verification or journal inconsistency.
Candidate → Desktop if evidence expires without confirmation.

Debounce topology-change events (recommended initial 300–1000 ms stable window), recheck immediately before moving, and rate-limit retries. Repeated observations must produce no repeated move.

## 7. Desired display/window placement policy

1. Determine currently confirmed streaming target. If absent or ambiguous, NO-OP.
2. Identify *the game* using Steam launch/session metadata or explicit per-game rule; never any foreground process.
3. Resolve main top-level game window; ensure visible, non-child, safe geometry, not protected/elevated, non-minimized or intentionally minimized as appropriate.
4. If already entirely on target monitor, NO-OP.
5. Snapshot HWND, PID plus process creation time, target/current monitor identities, WINDOWPLACEMENT and relevant DPI/bounds before mutation.
6. Prefer borderless/windowed movement. If exclusive fullscreen or graphics API fights placement, do not repeatedly force it; provide user guidance or per-game policy.
7. Move using appropriate API after checking state; verify window ended on intended monitor. If not, attempt one safe rollback then stop and record failure.
8. On verified session end, restore only windows this app moved, that still exist and map to the same process instance. If the original physical monitor is unavailable, do not move off-screen; defer and warn.
9. If user manually moved the window after our operation, do not override that choice without an explicit lock-to-stream policy.

Default: keep all physical displays enabled; do not change primary display outside what Steam already does.

## 8. Profiles and configuration

Profiles:
- Desktop: physical 3440x1440, no remote session.
- Deck docked: expected virtual 3840x2160 at 60 Hz.
- Deck handheld: expected virtual 1280x800 at 60 Hz.
- Generic: use actual client virtual mode.

A profile may be chosen automatically only when evidence is clear. Actual virtual mode is authoritative; a size match is insufficient device identification.

Initial settings:
- physicalDisplayPolicy = KeepEnabled
- wrongDisplayPolicy = AutoMoveRecognizedGame
- onSessionEnd = RestoreOwnedWindows
- standalone = true
- dryRun = true in Phase 1, opt-in mutations in Phase 2
- restoreOnShutdown = true, when safe and synchronous
- logWindowTitles = false

Store per-user settings at %LOCALAPPDATA%/StreamDisplayManager/settings.json with schema versioning and safe defaults. Keep machine-specific EDID/device identifiers out of source control.

## 9. Safety and recovery invariants

S1. No execution without session confirmation, resolved target and recognized game identity.
S2. Phase 1 never calls display/window mutation APIs.
S3. Never disable the last physical monitor.
S4. No unrelated windows are moved.
S5. Every move has an owned, persisted original placement BEFORE acting.
S6. Restoration is conditional on process/window identity, current display availability, and user intent.
S7. On failed action, prefer leaving physical displays functioning over pursuing the stream layout.
S8. App crash or restart must not strand game windows off-screen. Recovery plan re-reads current topology before applying any saved placement.
S9. No endless move/retry loops, no UAC prompt or forced elevation.
S10. Unknown/conflicting evidence produces observation and notification, not mutation.

## 10. Observability schema (v1)

Time-stamped structured events (JSONL):
- system_snapshot: physical/virtual displays, modes, bounds, primary, adapter metadata, top-level game-candidate windows and process IDs; no window titles.
- steam_signal: source, parsed version/signature, confidence, timestamp; no credential paths.
- session_transition: previous, next, reason, evidence IDs.
- planned_action: correlation ID, window identity, intended target, dryRun, policy reason.
- applied_action: result, verification result, rollback, duration and errors.
- restore_action: previous identity check, result, reason.
- health: last complete snapshot, observer failures, event backlog and journal state.

IDs are per-session, not durable Windows display-number indexes. Logs rotate daily with retention and maximum size limits.

## 11. Validation matrix

Test A: start normal desktop, Steam running but no stream; no moves.
Test B: connect docked Deck, virtual path appears at 4K60; eventual confirmed session, game appears on physical monitor; move only game.
Test C: Steam already launches game on virtual monitor; no-op.
Test D: Steam Deck handheld 1280x800; distinguish client profile without hardcoded Windows display number.
Test E: disconnect while game remains open; restore owned game window only.
Test F: user moves game manually; do not override on disconnect without explicit rule.
Test G: virtual monitor disappears mid-move; rollback or safe fail.
Test H: sleep/resume or app restart; reconcile journal, do not restore off-screen.
Test I: game is exclusive fullscreen/elevated/protected; warn and skip.
Test J: concurrent Steam session, changing resolution, fast reconnect; no repeated moves and no unsafe restore.
Test K: Windows device path changes between sessions; no stale identity use.
Test L: diagnostic logs do not contain window titles, credentials or full command lines.

## 12. Completion criteria

Phase 1: observer works without admin rights, logs desktop and streaming snapshots, never changes display/window state, and captures evidence from one docked and one handheld session.
Phase 2: recognized borderless game window is moved within 2 seconds of confirmed stable session and restored after verified end, with complete ownership and rollback tests.
Phase 3: optional primary/topology policies require explicit opt-in, emergency restore, and interruption tests before release.

The Phase 2 2-second goal is an engineering target, not a current measured capability.

## References

See REFERENCES.md for vendor API and Steam announcement links.
