# Delivery plan

## Phase 1 — Read-only diagnostics (first deliverable)

Build a minimal .NET Windows tray/observer application:
- Enumerate Windows displays, active virtual candidates and top-level windows.
- Record changes and process/window-to-monitor association with bounded, privacy-aware logs.
- Report Steam process presence without equating it to an active stream.
- Start with read-only snapshots; layer QueryDisplayConfig and experimental stream confirmation next.
- Build on GitHub Actions Windows runner.
- Validate locally with: normal desktop, docked Steam Deck 4K60, handheld Deck 1280x800, reconnect, sleep/resume.

**Acceptance:** it starts at user login or manually, produces useful JSONL observation, and performs zero mutations. No claim of confirmed sessions until verified stream signatures exist.

## Phase 1.5 — Durable session recognition

- Identify SudoVDA using Windows display device metadata rather than display number.
- Observe Steam Remote Play host's session-specific signals (validate against real logs).
- Implement debounced stream confidence (Unknown/Candidate/Confirmed/Ended).
- Add deterministic traces/fixtures from anonymized observations and replay test harness.
- Provide a visible tray state: desktop, candidate, confirmed, ambiguous.

**Acceptance:** classify sample session start/stop without false-positive move authorization.

## Phase 2 — Game-window placement

- Implement Steam/game process mapping and explicit per-game overrides.
- Add WinEventHook and game-window lifecycle discovery.
- Implement journaled per-window ownership and safe borderless relocation.
- Restore only owned windows on verified disconnect; respect user-initiated changes.
- Add dry-run, opt-in action mode, single-action rollback, and emergency restore.
- Tests for malformed snapshots, disconnected targets and exclusive fullscreen skip.

**Acceptance:** automatic move and restore in Starfield and at least one other borderless game, no unrelated windows affected.

## Phase 2A/B — Pure placement assessment and dry-run (implemented)

- A pure `PlacementPlanner` accepts stable, active display rectangles, Steam session App ID and observed top-level game windows.
- Diagnostic mappings for Starfield (1716740 → Starfield), RDR2 (1174180 → RDR2), and DOOM: The Dark Ages (3017860 → DOOMTheDarkAges); these process-name rules are provisional, not durable ownership proof.
- Refuse to guess when Steam is unconfirmed, virtual targets are multiple, game windows are ambiguous/minimized/small, or window center and monitor attribution disagree.
- Assess a game already on the virtual display as `AlreadyOnTarget`; assess a candidate on the physical monitor as `WouldMove`, with `dryRun=true` and `executorEnabled=false`.
- Debounce the assessment over multiple observations before logging `placement_plan`. Reset after a stream ends.
- Window moving, restoration, process-start identity checks, QueryDisplayConfig target identity and fullscreen policy are **still not implemented**.

**Acceptance:** all pure tests and CI pass, and real games produce diagnostic no-op or dry-run results without moving any windows. A future release must not equate a dry-run candidate with permission to move.

## Phase 2C — Automatic Steam game window relocation experiment

- Requires a confirmed Steam Remote Play session, a stable, single SudoVDA target, an active physical display, and a confidently identified game window.
- Games with observed process mappings (Starfield, RDR2, DOOM) are eligible; others are eligible if the actual window PID's executable path resides inside the active Steam App ID's installed game directory according to Steam library folders and appmanifest metadata.
- A pure planner refuses unknown/ambiguous windows; the generalized native mover revalidates the HWND, PID, live process name, source monitor, target monitor and geometry before calling SetWindowPos.
- One initial automatic correction, plus one follow-up when the same game process returns to physical, even if it recreates its top-level HWND (observed with RDR2). The first move must have been observed on SudoVDA either immediately after a successful SetWindowPos call or in delayed observations. A different PID never inherits the remaining attempt. No third attempt or unbounded enforcement. A window previously seen on virtual may still receive a correction if it later moves to physical, per the user's requested policy.
- Logs game_identity (status, PID set, inaccessible process count), placement_plan (app/PID/process, identity evidence), move_attempt (source/target active resolutions, style, requested geometry, attempt number), move_result (Win32 return/error, after geometry/monitor), move_immediate_confirmation, delayed move_verification (including same-PID HWND replacements), move_followup with previous/new handle, move_owned_confirmation, move_skipped, and move_suppressed.
- Win32 window relocation cannot itself change a game's selected graphics output or available resolution modes. DOOM displayed on SudoVDA but internally still selected the Dell physical monitor, limiting its graphics menu to the Dell's 3440×1440 mode until the display was changed in-game. QueryDisplayConfig/game-specific display selection are distinct future investigations.
- This remains experimental; game-native fullscreen behavior can ignore or later override window moves. Automatic disconnect restoration and stable monitor path identity remain unimplemented. Never alter physical monitor topology by default.

**Acceptance:** known games and game-directory-identified Steam games use one shared policy; unknown/ambiguous games are logged but untouched; demonstrate verified SudoVDA placement and reproducible no-more-than-two-attempt behavior in real sessions.
## Phase 3 — Profiles and optional topology changes

- Support docked/handheld/other profiles using actual streamed mode.
- Preserve Steam's primary display selection by default.
- Optional physical topology or monitor disabling *only* behind explicit opt-in with robust recovery, never enabled by default.
- Recovery from crashed driver, Steam restart, Windows login and monitor removal.

**Acceptance:** unattended remote session does not strand Windows on an inaccessible display.

## Phase 4 — Productization

- Tray status and explanations, preferences UI, MSI or MSIX packaging, auto-start option.
- Optional SensorDash/HomeAwayFromHome read-only telemetry integration.
- Stable log schema, troubleshooting export with privacy redaction.
- Release pipeline, signed artifacts if signing infrastructure is available.

## First validation checklist

1. Install/configure SudoVDA and select it in Steam Remote Play advanced host options.
2. Run the observer on host and record a desktop-only baseline.
3. Connect the wired docked Steam Deck; start 4K60 Remote Play, launch Starfield, then disconnect.
4. Repeat in handheld 1280x800 mode.
5. Confirm which display fields/Steam log messages actually change.
6. Only then enable session-confirmed window movement in Phase 2.

No auto-move behavior should be enabled until the above observations have been evaluated.
