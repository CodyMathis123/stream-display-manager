# Agent instructions

Stream Display Manager is a **small native Windows desktop utility**, not a streaming host. Optimize for trustworthy behavior, a low idle footprint, and a desktop that is always recoverable.

## Read before changing code

1. [docs/INDEX.md](docs/INDEX.md) — map of code, evidence, decisions, and unfinished work.
2. [docs/DECISIONS.md](docs/DECISIONS.md) — architectural choices and explicit non-goals.
3. [docs/NATIVE.md](docs/NATIVE.md) — display/session/recovery and MSI behavior.
4. [docs/STEAM_LOG_VALIDATION_2026-11-25.md](docs/STEAM_LOG_VALIDATION_2026-11-25.md) — observed Steam signatures; **not** an API contract.
5. [docs/RELEASING.md](docs/RELEASING.md) — production-only promotion, versioning and release checks.

## Hard constraints

- Keep C++20 Win32, MSVC x64 `/MT`, and WiX 5. No managed runtime, background service, web UI, external updater or unneeded library.
- **Never disable the last physical display on a hunch.** Require fresh Steam host start evidence, active and stable SudoVDA, and the current safety policy before `SetDisplayConfig`.
- Preserve the original recovery journal. Drift repairs must never overwrite it. Revert on stream end, loss of Steam evidence, SudoVDA disappearance, app shutdown, and startup recovery as designed.
- Do not persist the temporary virtual-only layout to the Windows display database. Restoring from the database is a fallback, not permission to save streaming topology there.
- A log parser must start at EOF and discard uncertain state on gap/rotation; `Steam.exe` or SudoVDA alone is not stream authorization.
- Do not commit raw Steam host logs, IPs, credentials, window titles, device IDs, or personal machine paths. Use synthetic/redacted fixtures. Diagnostic events should be minimal and reviewable.
- **Keep changes focused.** No speculative client profiles, game-window mover, custom virtual display, SensorDash integration, or automatic 4K forcing without evidence.
- Document any changed behavior/invariant in `docs/DECISIONS.md` or `docs/FINDINGS.md`. Update the relevant test and README/docs link when interfaces or behavior change.

## Repository boundaries

- `native/StreamDisplayManager.cpp`: tray/Win32 event loop and high-level orchestration only.
- `native/SteamObserver.{h,cpp}` and `native/SteamTail.h`: host evidence, bounded file tailing.
- `native/SessionPolicy.h`: deterministic, side-effect-free action decisions.
- `native/DisplayTopology.h`: Windows CCD mutation, recovery and journaling. **Safety-critical.**
- `native/AppRuntime.{h,cpp}`: settings, login startup, compact diagnostics, version/About UI.
- `tests/native_reliability.cpp`: pure policy and log lifecycle tests; never change actual hardware.
- `installer/`: per-user WiX 5 MSI, graceful close, install launch, startup registration.
- `.github/workflows/native-ci.yml`: authoritative Windows C++ build, tests, MSI lifecycle verification.
- `.github/workflows/release.yml`: manual promotion of successful **Production** MSI; never publish a PR artifact.

For common Windows CI/installer and display-safety verification, use the optional local [native-validation skill](.agents/skills/native-validation/SKILL.md). It is a checklist for developers and agents, not a runtime dependency.

## Validation expectations

- Changes to session or display policy require a deterministic regression test, plus hardware testing when real CCD behavior is affected. CI cannot emulate a genuine Steam/SudoVDA session.
- Changes to the installer must keep version parity and MSI stop/install/start/uninstall checks green. Silent installs intentionally do not auto-launch a tray.
- Keep the EXE small, and compare idle CPU/private bytes in CI before and after changes; do not claim GitHub runner memory measurements reflect the user's own desktop.
- Favor a small PR with a clear testable hypothesis. State what remains unverified by real hardware.
- Do not merge a failing or unverified refactor. Keep `Production` buildable.
