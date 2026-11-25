---
name: native-validation
description: Validate Stream Display Manager C++ Win32, Steam session, display recovery and WiX MSI changes using deterministic tests and CI evidence.
---

# Native Windows validation workflow

Use when changing Steam observation, display policy, Win32 tray events, MSI packaging or release workflows. See [AGENTS.md](../../../AGENTS.md) and [docs/INDEX.md](../../../docs/INDEX.md) before starting.

1. **Classify risk.** Pure policy/file parsing is testable on CI; `SetDisplayConfig`, full-screen game behavior and live taskbar/installer interactions need real Windows hardware confirmation. A CI green check never proves display restoration on a real host.
2. **Protect the desktop.** Before CCD changes, enumerate active physical/SudoVDA paths, preserve the original recovery journal, and verify the manual `--restore` and `--extend` paths still exist. Don't persist a virtual-only layout.
3. **Add bounded tests.** Expand `tests/native_reliability.cpp` for stream gap/reconnect/replay rules, cooldowns and fallback decisions; do not mutate active displays in tests.
4. **Run the Windows workflow.** `.github/workflows/native-ci.yml` uses MSVC C++20 `/MT`, the Win32 resource compiler, WiX 5, native tests, version parity and a silent MSI install/uninstall while a tray is running. The same job records idle working set/private bytes.
5. **Read failures rather than guessing.** For MSI failures, inspect the verbose `msiexec` lifecycle log, especially `Return value 3`, and check the named action's placement between `InstallInitialize` and `InstallFinalize`. For RC failures, check generated `VersionInfo.h` include path. For Steam tests, confirm EOF-on-attach and gap invalidation.
6. **State what is verified.** Cite the successful Actions run and versioned MSI, and separately note any pending live host/Deck tests. Update `docs/FINDINGS.md` or `docs/DECISIONS.md` if the result affects the current design.
7. **Release only deliberately.** Publish with the `Publish verified Windows MSI release` workflow on `Production` after a successful Production CI run for the *exact* commit. Never promote a PR build.

This workflow is a development skill, **not** a tool or automation to be installed on the user's PC.
