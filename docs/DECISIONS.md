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

## D-008 — Temporary CCD isolation from a secondary 4K source (2026-11-25)

**Decision:** Use a pure candidate transformation to rebase a uniquely
identified active SudoVDA path from a secondary desktop position to (0,0).
Rebuild only referenced source/target mode entries, validate before changing
displays and preserve the original 2-path journal for recovery. If validating
the explicit active modes fails, a single validated Windows-selected-mode
fallback is allowed; the chosen plan and failure stage are logged for the
next iteration. No retry loops, persistence, services, automatic resolution
forcing, or game-specific overrides.

**Reason:** Reproducible `SetDisplayConfig` error 87 occurred on three
4K sessions with physical-display primary while handheld SudoVDA-primary
sessions previously worked. **Status:** candidate ready for real 4K host
validation; the precise Windows-invalid parameter has not yet been confirmed.

## D-009 — Tray-only strategy selector and non-destructive A/B modes (2026-11-25)

**Decision:** Retain a lightweight native tray submenu instead of a settings
window. Offer Disabled, Observe only, Virtual + move (opt-in experimental),
Virtual primary with both displays active, and the already-supported
Virtual only. Choose exactly one mode and persist it; preserve the old
Boolean preference for existing installs. Defer live selection changes to
the next stream, except Disabled, which attempts immediate restoration.

**Reason:** DOOM: The Dark Ages dropped from ~60 host FPS during startup to
2–4 host FPS at the main menu while virtual-only isolation was active, even
though Windows' 4K mode stayed valid. A monitor hot-removal sensitivity is
plausible, but unconfirmed. Virtual-primary preserves both displays; window
move tests a no-topology-change alternative. Neither approach assumes it
fixes game performance.

**Tradeoffs:** Virtual-primary currently requires precisely one physical
and one virtual CCD path, with validation and saved-layout rollback. Window
relocation filters only the foreground \`steamapps\\common\` executable,
does not inject or manipulate renderer internals, and is best-effort for
fullscreen games. Both new strategies require hardware testing. The
Production default remains unchanged until reviewed.
