# Observations, findings and open hypotheses

Always distinguish **observed** behavior from a proposal or theory. Source: local user tests, native logs, CI or cited external documentation. Review raw logs for sensitive content before sharing or committing.

## Verified on the test desktop (2026-11-25)

- The Windows host has one physical 3440×1440 monitor, Steam Remote Play and SudoVDA. A native test recorded SudoVDA-only topology at 1280×800 and successful restoration of the physical 3440×1440 desktop after the stream.
- SudoVDA's Windows mode enumeration already advertised 3840×2160 at 60 Hz and 120 Hz even in a handheld 1280×800 session. **Advertised** does not mean a game or the docked client actually selects 4K.
- Game-window relocation experiments in the retired managed branch placed DOOM/RDR2 physically on SudoVDA, but did **not** reliably change the monitor exposed by the game's own settings. This is the main reason for native virtual-only CCD switching rather than carrying a window mover forward.
- Some RDR2 focus-related black-stream reports correlated with Steam's capture path; switching its renderer to Vulkan improved the observed stream. This is Steam/game behavior, not a proven Stream Display Manager bug.
- User observed roughly 2 MB private memory for the native tray. Windows CI measured ~1.6 MiB private bytes and around 9–10 MiB working set in separate runner tests. These are different metrics and environments.

## Verified by CI

- MSVC builds and native reliability tests pass without real display mutations.
- MSI contains the versioned native EXE and HKCU startup registration. Silent install/uninstall with running tray has been tested; the app's own lifecycle event confirmed graceful shutdown. The MSI installs under the user profile.
- Interactive installer launch is configured and basic local behavior was reported working; CI's silent install test cannot exercise an actual taskbar/UI launch.
- Executable Windows VERSIONINFO matches the MSI build number.

## Open observations and questions

- **Docked 4K60:** untested. Follow [issue #2](https://github.com/CodyMathis123/stream-display-manager/issues/2) rather than implementing automatic mode forcing blindly.
- **Identity heuristics:** SudoVDA identification uses driver strings and ID hints. This is adequate on tested hardware but is not a formally stable target identity; see [issue #3](https://github.com/CodyMathis123/stream-display-manager/issues/3).
- **Actual non-admin install and signed distribution:** optional follow-up in [issue #6](https://github.com/CodyMathis123/stream-display-manager/issues/6).

## Adding a finding

Record the precise test scenario, local-versus-CI source, expected versus observed behavior, relevant commit/build, logs stripped of machine/private identifiers, and the resulting action (if any). Do not copy raw Steam log lines containing client addresses, window titles or local filesystem paths.
