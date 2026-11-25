# Architecture decisions

Decisions describe present intent, not immutable truth. Amend them when real evidence changes the choice; link the observed failure and proposed test.

## D-001 — Native-only, minimal-resident utility (2026-11-25)

**Decision:** Win32 C++20 with MSVC statically linked runtime and a notification-area UI. No .NET/WinForms runtime, service or new streaming protocol. **Why:** our previous managed preview was disproportionate for a simple always-on display companion; the native implementation worked in live tests with about 2 MB of observed private memory on the host. **Tradeoff:** lower-level Windows API handling and platform-specific tests. **Status:** accepted.

## D-002 — Steam host log plus active SudoVDA evidence (2026-11-25)

**Decision:** Only fresh host log start evidence combined with a stable active virtual output permits switching. A Steam process alone and an attached SudoVDA alone are insufficient. Log gaps, rotation or ambiguous evidence invalidate authorization. **Why:** Steam's host log is informal and can change, and display switching affects the user's ability to control their desktop. **Tradeoff:** a missed early stream may not switch until a fresh connection. **Status:** accepted; revisit only with validated new Steam behavior.

## D-003 — Virtual-only CCD topology, never persistent (2026-11-25)

**Decision:** Preserve the pre-stream CCD layout in a versioned binary recovery journal, then make SudoVDA the sole active path without saving the streaming configuration to Windows' database. Restore original CCD paths on termination, or fall back to Windows' previously saved topology if necessary. A repair must not overwrite the original journal. **Why:** placing a game window on a virtual monitor did not reliably change game-selected GPU/display modes; isolating the virtual display did. **Tradeoff:** real hardware testing and a robust fallback are essential. **Status:** accepted.

## D-004 — Bounded recovery rather than complex session manager (2026-11-25)

**Decision:** Use the existing three-second polling cycle, up to two display-drift repairs per session with 15-second cooldown, and a 12-second reconnect grace only following an explicit end while SudoVDA is still present. Loss of virtual target, Steam process or trustworthy log evidence restores immediately. **Why:** covers practical reconnects without adding services, timers, background workers or configuration. **Status:** accepted.

## D-005 — Versioned MSI and explicit verified Production releases (2026-11-25)

**Decision:** One WiX 5 per-user MSI for native executable, Windows login autostart, and interactive post-install launch. MSI gracefully closes running tray before file changes. Both EXE and MSI derive version from the same successful CI run. A *manual* release action only promotes an already successful Production CI artifact for the exact commit; release assets do not come from PR builds. **Why:** predictable shipping and easy provenance without updater infrastructure. **Status:** accepted.

## D-006 — Mechanical module separation, no feature expansion (2026-11-25)

**Decision:** Keep Windows tray/window lifecycle and high-level Tick orchestration together; move independent Steam evidence and local runtime/diagnostics into explicit modules. Keep pure SessionPolicy and SteamTail separately testable. **Why:** prevents a single C++ file from becoming the repository's only entry point while keeping code browsing and build configuration simple. **Tradeoff:** a few more C++ translation units. **Status:** proposed in PR #9.

## Explicit non-goals

No custom game renderer hooks, universal HWND relocation, per-device profiles, custom SudoVDA driver, automatic 4K forcing, service-based remote control, or deep integration with unrelated PC telemetry. Revisit these only for reproduced, user-visible failures and a small defensible fix.

## D-007 — Per-user session diagnostics, not Windows Event Log (2026-11-25)

**Decision:** Keep structured user-local JSONL as the sole diagnostic event
source, rotate and prune bounded daily files, add process/session correlation
and severity, and provide a user-initiated, single-file readable export from
the tray. **Why:** this is a per-user native utility; Windows event-source
registration would add installer/privilege complexity, and per-game log files
would fragment a Steam streaming timeline. **Tradeoff:** use a simple
text/session summary instead of a live viewer, and keep only a bounded recent
history. Preserve the legacy single JSONL file without automatic deletion.
**Status:** proposed in the diagnostics PR. **Non-goals:** Windows Event Log,
SQLite, logging service, game-specific files and automatic uploads.
