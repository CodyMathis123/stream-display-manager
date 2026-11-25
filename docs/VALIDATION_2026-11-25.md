# Undocked Deck LCD — first hardware trace

**Observed:** 2026-11-25. **Source:** local read-only observer JSONL supplied during initial testing. Only sanitized, aggregate observations are recorded in this document; no raw logs, local user paths, HWNDs or PIDs are committed.

## Summary

- Baseline: one active physical monitor, 3440×1440, primary; Steam was running.
- SudoVDA exposes ten installed/unattached outputs in Windows adapter enumeration. None were active during the desktop baseline.
- On Remote Play start, **SudoVDA output DISPLAY6** attached and was visible as **1280×800 primary**, while the 3440×1440 physical monitor remained attached as a secondary.
- **Starfield** occupied the active 1280×800 virtual monitor; Steam positioned it correctly without an application-side move.
- When streaming stopped, the virtual monitor disappeared and the physical ultrawide was primary again.
- The game window had ceased being enumerated **before the virtual display disconnected**. This does **not** validate game-window restoration after disconnect; that remains future work.

## Timeline (UTC; seconds rounded)

| Time | Event | Observed topology |
| --- | --- | --- |
| 14:05:17 | Observer starts | Physical primary (3440×1440) |
| 14:05:26.729 | WM_DEVICECHANGE | SudoVDA DISPLAY6 marked attached before `Screen.AllScreens` recognizes it |
| 14:05:26.832–14:05:26.968 | Rapid device/display notifications | Adapter state alternates between 1 and 5; screen enumeration lags |
| 14:05:29.510 | Stable stream scene | DISPLAY6 1280×800 primary, physical DISPLAY1 secondary; Starfield 1280×800 on DISPLAY6 |
| 14:06:29.626 | Game absent in enumeration | Virtual display still active |
| 14:06:31.785–14:06:31.898 | Rapid detach notifications | Screen and adapter enumerations disagree |
| 14:06:32.638 | Desktop restored | Physical DISPLAY1 3440×1440 primary; SudoVDA unattached |

## Design consequences

1. **Installed driver != active virtual display.** An adapter can be enumerated with no active desktop source. Require both adapter attached state and actual `Screen.AllScreens` membership for the Phase 1 candidate; transition to authoritative `QueryDisplayConfig` in Phase 1.5.
2. **Windows APIs can disagree during a topology transition.** A monitor can temporarily appear in adapter enumeration before `Screen.AllScreens` recognizes it, and appear in screen enumeration after adapter detach. These are transient observations; never authorize mutations from them.
3. **Transient window-to-monitor attribution may be wrong.** During display changes, `Screen.FromHandle` sometimes reports DISPLAY6 for windows whose coordinates still match the physical display, before `Screen.AllScreens` recognizes DISPLAY6. Require stable reconciled topology before trusting it for relocation decisions.
4. **Steam already handles the happy path.** The policy engine should be idempotent and move a game only if it remains misplaced after the display state stabilizes.
5. **Steam session confirmation is not implemented.** SudoVDA activation plus Steam process presence corroborates this test, but does not establish a reliable general-purpose start/end signal. Stay read-only until log/session evidence is validated.
6. **Auto-restore needs a separate test.** A test with the game left running at disconnect is still needed.

## Follow-up

- Replay these five topology states in deterministic classifier tests.
- Log a `topology_stable` event only after a consistent snapshot persists long enough to be trusted (initial 750ms).
- Evaluate real Steam session signals before enabling game-window movement.
- Repeat with game left running and, later, docked 4K60.
