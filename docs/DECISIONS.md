# Architecture Decision Records

## ADR-001 — Standalone Windows application
**Status:** accepted, 2026-11-25

Implement a separate .NET/C# user-session application and repository, not part of the SensorDash Windows service.

**Reason:** interactive desktop windows, user notifications, session notifications and monitor state belong to the logged-on user's session. Windows services run in Session 0 and should not manipulate an interactive desktop.

**Consequences:** an app started at login (tray optional), independently installable; telemetry integrations with SensorDash or HomeAwayFromHome can be added later but are nonessential.

## ADR-002 — Physical monitor remains enabled
**Status:** accepted, 2026-11-25

During a Steam streaming session, retain the physical display(s). Do not disconnect, disable, turn off, or change the physical monitor unless the user explicitly enables a future feature.

**Reason:** remote PC is in the basement; disabling its only display risks a black-screen recovery problem. Steam and SudoVDA already provide the virtual monitor.

## ADR-003 — Automatically move recognized games
**Status:** accepted, 2026-11-25

When session evidence is sufficient and a recognized game window is on the wrong display, move it to the intended virtual display without an additional prompt, provided the operation is supported and safe.

**Constraints:** do not relocate arbitrary application windows, launchers, system windows, elevated windows, dialogs, or unrecognized processes. Do not fight Steam's own positioning. Exclusive fullscreen or protected windows may be advisory-only.

## ADR-004 — Restore only windows we moved
**Status:** accepted, 2026-11-25

Capture placement immediately before the first successful move of each window; on the end of streaming restore only windows moved by this application and only if the HWND still belongs to the same process and restoration remains safe.

**Reason:** Windows and the user may independently change other windows. Never revert unrelated user work.

## ADR-005 — Steam owns virtual display lifetime
**Status:** accepted, 2026-11-25

Rely on Steam's Windows SudoVDA integration to create and remove the streaming display. Do not install, replace, or implement a display driver.

**Consequences:** detect state via Windows display APIs and corroborate with Steam signals. Avoid undocumented Steam interfaces as a sole source of truth. SudoVDA identifiers may change across sessions.

## ADR-006 — Observe → plan → act
**Status:** accepted, 2026-11-25

Separate read-only observation, pure deterministic policy evaluation, and native Windows mutations. Phase 1 is fully read-only. Future actions require idempotency, ownership tracking, journaled rollback, and a dry-run setting.

## ADR-007 — No privilege escalation
**Status:** proposed default

Run unprivileged as the interactive user. If a game is elevated or protected, report unsupported movement rather than elevating the whole utility. No kernel driver or administrator requirement for routine operation.
