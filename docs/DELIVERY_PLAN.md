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
- Only the observed Starfield diagnostic rule (Steam App ID `1716740`, process name `Starfield`) is available initially. This is **not proof** of process ownership sufficient to authorize actual window mutations.
- Refuse to guess when Steam is unconfirmed, virtual targets are multiple, game windows are ambiguous/minimized/small, or window center and monitor attribution disagree.
- Assess a game already on the virtual display as `AlreadyOnTarget`; assess a candidate on the physical monitor as `WouldMove`, with `dryRun=true` and `executorEnabled=false`.
- Debounce the assessment over multiple observations before logging `placement_plan`. Reset after a stream ends.
- Window moving, restoration, process-start identity checks, QueryDisplayConfig target identity and fullscreen policy are **still not implemented**.

**Acceptance:** all pure tests and CI pass, and real games produce diagnostic no-op or dry-run results without moving any windows. A future release must not equate a dry-run candidate with permission to move.

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
