# Releasing Stream Display Manager

The release process intentionally separates **build/test** from **publish**. The project has no automatic updater and does not publish releases from pull requests.

## Normal flow

1. Merge a tested PR into `Production`.
2. Wait for the `Windows native build, tests and MSI` workflow on **that exact Production commit** to succeed. It compiles native C++ with MSVC, runs deterministic tests, validates EXE VERSIONINFO against MSI ProductVersion, checks MSI actions, and exercises a silent install/uninstall with a live tray.
3. From GitHub Actions, select **Publish verified Windows MSI release** and **Run workflow** with branch **Production**. This is a deliberate manual publish gate.
4. The workflow looks for a **successful Production push CI run whose head SHA exactly matches the checked-out commit**, downloads its existing `StreamDisplayManager-MSI` artifact, checks its versioned filename, computes SHA-256, and creates tag `v1.0.<build>` and a GitHub Release with the MSI plus `SHA256SUMS`. It does **not** rebuild or accept PR-only artifacts.
5. Open the GitHub Releases page and verify the tag, attached MSI, SHA-256 and release notes. Test a normal user-facing interactive upgrade before recommending a release broadly.

If Production changed since CI, the manual workflow fails closed. If a release/tag already exists, it refuses to overwrite the asset. A fresh Production commit/build is needed to produce a new version. GitHub artifact retention is currently 14 days, so publish while the verified artifact exists.

## Versioning

CI uses `1.0.<GitHub Actions run number>` for MSI ProductVersion and `1.0.<run>.0` for the EXE's four-component Windows file/product versions. An About dialog reads the version from the installed EXE resource. Windows Installer compares first three version components.

## Safety

- Only `Production` is a release source; avoid enabling a release trigger on PR events.
- Ensure release workflow token has Actions read and Contents write permissions; GitHub repository policies may require approval for publication.
- The application has no signing identity today. A Windows SmartScreen warning is possible; signing is a separate conscious cost/operations decision.
- The MSI installs under LocalAppData and does not install SudoVDA or modify Steam itself.
- Quiet installs don't launch UI processes; interactive installs do. Login auto-start uses HKCU Run.
- The release workflow creates a public/private GitHub Release according to repository visibility. Review notes/assets before broader sharing.
