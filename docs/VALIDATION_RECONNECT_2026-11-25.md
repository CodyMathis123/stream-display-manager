# Undocked Steam Deck — reconnect trace (second capture)

Observed November 25, 2026. Source: observer JSONL supplied by the user during an undocked Steam Deck LCD test. Sanitized conclusions only; the repository does not store the raw log, Windows username, PIDs or window handles.

## Important scope note

The uploaded file is an appended daily log containing earlier tests too. This report concerns the **final observer process** that started at **14:10:27 UTC**. Earlier entries include separate Starfield and Cyberpunk sessions; they are not part of this reconnect experiment.

## Timeline (UTC)

| Time | Observation |
| --- | --- |
| 14:10:27 | Observer starts; single physical DISPLAY1 3440×1440 primary |
| 14:10:38–14:10:39 | SudoVDA DISPLAY6 attaches; adapter and desktop screen enumerations momentarily disagree |
| 14:10:42 | Starfield is visible at 1280×800 on virtual DISPLAY6, which is primary; physical display remains attached |
| 14:11:19 | SudoVDA begins detaching; same Starfield window is observed on physical DISPLAY1, briefly changing from 1280×800 to 3440×1440 |
| 14:11:21 | Only the physical 3440×1440 display is active and primary; Starfield still running |
| 14:11:27–14:11:28 | SudoVDA reattaches to DISPLAY6 and Starfield returns to virtual 1280×800, preserving its process and HWND across sessions |
| 14:11:39 | Starfield window disappears from enumeration (consistent with user exiting game) |
| 14:11:41–14:11:42 | SudoVDA detaches; physical display primary again |

## Findings

1. Steam/Windows correctly switched the **same running game window** from virtual DISPLAY6 to physical DISPLAY1 on disconnect, and back to DISPLAY6 on reconnect. Our application never moved the window.
2. Physical monitor remained enabled even when virtual DISPLAY6 was primary.
3. Display and window observations are **not atomic**: a virtual screen can temporarily remain in \`Screen.AllScreens\` while its adapter reports detached. \`Screen.FromHandle\` can temporarily label a window with a display that isn't currently in \`Screen.AllScreens\`.
4. Only one \`topology_stable\` event was logged for the reconnect experiment, despite physical and virtual transitions. **Bug identified:** the observer returned early on unchanged 3-second poll snapshots, preventing debounce completion; it also compared only the last published signature without accounting for re-entry after a prior stable desktop. Fixed by separating display-topology reconciliation from changed-snapshot logging and tracking transitions in a pure \`TopologyStabilizer\` class.
5. The \`topology_stable\` event **only** proves consistent Windows monitor topology; it does *not* confirm Steam streaming. Do not authorize window movement without corroborating an active Steam Remote Play session and a recognized game identity.
6. This trace validates the happy path, not deliberate wrong-monitor placement or app-owned restoration. The automatic mover remains disabled.

## Implementation/test consequences

- Evaluate topology on **every** observer poll, including when the full snapshot is unchanged.
- Debounce inconsistent or changed signatures, and publish a new stable event on each *distinct transition*, including virtual → desktop → same virtual.
- On reconnect, preserve game-process identity but do not assume it persists across process restarts.
- Never move a game window in the middle of display activation; Steam may already be fixing its position.
- Later: obtain independently verifiable Steam streaming start/end evidence and explicit user/game action policies.
