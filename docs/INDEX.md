# Repository index

This is the navigation page for maintainers and coding agents. Keep it current when adding/removing code. Prefer focused, linked files to a broad unstructured knowledge dump.

## Source of truth

| Topic | Entry point | Notes |
| --- | --- | --- |
| Native tray & orchestration | [StreamDisplayManager.cpp](../native/StreamDisplayManager.cpp) | Windows message loop, commands and 3-second tick |
| Steam host evidence | [SteamObserver.cpp](../native/SteamObserver.cpp), [SteamTail.h](../native/SteamTail.h) | Log events; EOF/gap/rotation behavior |
| Session decisions | [SessionPolicy.h](../native/SessionPolicy.h) | Pure actions for switch, restore, drift repair and reconnect |
| Display switching & restore | [DisplayTopology.h](../native/DisplayTopology.h) | Windows CCD APIs; recovery journal; safety-critical |
| Local runtime/diagnostics | [AppRuntime.cpp](../native/AppRuntime.cpp) | About/version, settings, startup, mode capture, JSONL |
| Windows installer | [StreamDisplayManager.wxs](../installer/StreamDisplayManager.wxs) | Close running app; launch post-install; login startup |
| Native CI | [native-ci.yml](../.github/workflows/native-ci.yml) | Tests, MSI lifecycle and EXE metadata; Windows runner |
| Release | [release.yml](../.github/workflows/release.yml) | Manual verified Production MSI promotion |
| Regression tests | [native_reliability.cpp](../tests/native_reliability.cpp) | Steam log and recovery decision tests, no real display mutation |

## Reusable agent workflow

- [Native validation skill](../.agents/skills/native-validation/SKILL.md) — focused checklist for Steam/recovery, EXE resource and MSI lifecycle changes; no extra installed tools

## Engineering reference

- [AGENTS.md](../AGENTS.md) — hard safety invariants, default coding rules, verification expectations
- [NATIVE.md](NATIVE.md) — design, recovery commands, operational details
- [DECISIONS.md](DECISIONS.md) — deliberate choices, constraints, non-goals, tradeoffs
- [FINDINGS.md](FINDINGS.md) — actual host observations versus unverified hypotheses
- [STEAM_LOG_VALIDATION_2026-11-25.md](STEAM_LOG_VALIDATION_2026-11-25.md) — historical evidence for observed Steam signatures
- [REFERENCES.md](REFERENCES.md) — official API references and related projects
- [RELEASING.md](RELEASING.md) — version/build/release checklist

## Work queues

GitHub issues are the canonical unfinished-work list. Focused current items:
- [#2 — Docked 4K60 validation](https://github.com/CodyMathis123/stream-display-manager/issues/2)
- [#3 — Conditional CCD identity hardening](https://github.com/CodyMathis123/stream-display-manager/issues/3)
- [#6 — Release/distribution hygiene](https://github.com/CodyMathis123/stream-display-manager/issues/6)

Historical managed WinForms and experimental game-window relocation work was retired. Do not treat old PR/issue comments as current design instructions.
