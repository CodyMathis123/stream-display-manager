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

## Phase 2C — Automatic DOOM window relocation experiment

- When the Steam session and SudoVDA target are both confirmed and stable, a DOOM window on the physical monitor triggers one automatic Win32 `SetWindowPos` move to the live virtual monitor's bounds.
- Enabled for **DOOM: The Dark Ages only** (App ID 3017860). Starfield and RDR2 remain read-only, and the observer never changes monitor topology or Steam settings.
- One attempt per stream session. A window already observed on SudoVDA is not subsequently forced back if the user moves it away.
- Before moving, revalidate HWND, PID, exact process name, live source and target displays and geometry. Preserve Z order and foreground activation.
- Log `placement_plan`, `move_attempt`, `move_result` (including Win32 error and before/after rectangles), `move_verification` at later observations, and `move_suppressed` / `move_skipped` reasons.
- This is an **experiment**, not a production-safe general mover. It has no crash-safe restore journal, Win32 movement may be ignored by DOOM's fullscreen renderer, and game-owned display selection may override the window coordinates. Test on a machine where the physical display remains accessible.

**Acceptance:** on a real DOOM Steam Deck session, observe a single attempt and determine from the logs whether the API was accepted and whether the same HWND reaches and stays on SudoVDA. Do not infer success solely from SetWindowPos returning true.
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
