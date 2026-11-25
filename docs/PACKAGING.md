# Installer, icon, CI and release plan

This project mirrors proven patterns from [SensorDash](https://github.com/CodyMathis123/SensorDash): .NET 10 single-file self-contained publish, Windows GitHub Actions, WiX 5.0.2, a deterministic PNG-compressed ICO generator, and a Windows Installer metadata check.

## Package boundaries

- Installs a **per-user** tray application under the user's LocalAppData StreamDisplayManager folder.
- No Windows service, UAC requirement for ordinary use, driver installation, firewall opening, network listener or background privileged process.
- Steam and SudoVDA are separate prerequisites; installing or uninstalling this MSI must not touch either.
- Start Menu shortcut, branded executable icon, branded tray icon, HKCU Run entry to start at **next user login**.
- MSI upgrade/uninstall sends a polite close to the tray app and does not forcibly terminate Steam or game processes.
- Existing logs and settings remain in LocalAppData and should not be deleted by uninstall.

## Reproducible icon

The **single source of truth** is scripts/Generate-StreamDisplayManagerIcon.ps1, inspired by SensorDash's generator. It generates a multi-resolution ICO (16/24/32/48/64/256 px) of a dark background with an overlapping physical monitor and cyan streaming display. The app embeds it as its Windows executable icon, and the MSI references the same asset for the Start Menu shortcut. No externally downloaded image or fonts.

## Development on Windows

PowerShell:

~~~powershell
dotnet build StreamDisplayManager.slnx -c Release
dotnet run --project tests/StreamDisplayManager.Core.SpecTests -c Release
dotnet publish src/StreamDisplayManager.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=0.1.0 -o dist
dotnet build installer/StreamDisplayManager.wixproj -c Release -p:StreamDisplayManagerVersion=0.1.0
./scripts/Test-Msi.ps1 -MsiPath installer-output/StreamDisplayManager-Setup-0.1.0-x64.msi -ExpectedVersion 0.1.0
~~~

## CI workflow

On each feature branch, pull request, and Production push, Windows CI:
1. Restores/builds the .NET solution and runs all deterministic core checks.
2. Assigns a three-part MSI version 0.1.<GitHub workflow run number> (newer workflow runs replace older builds).
3. Publishes a self-contained x64 single-file Windows app.
4. Builds a WiX 5 per-user installer.
5. Opens the actual MSI database and validates ProductName, ProductVersion, UpgradeCode, install scope, and packaged app filename.
6. Uploads a directly downloadable MSI Actions artifact without an outer ZIP.

The test installer is **not digitally signed** and not yet published as a public GitHub Release. A formal release gate that promotes only validated Production artifacts can be added after an installer smoke test.

## Manual MSI acceptance matrix

- [ ] Clean per-user install without elevation or unrequested driver changes.
- [ ] EXE, Start Menu, and tray show the same branded icon.
- [ ] Launching shortcut twice still runs one observer instance.
- [ ] Login auto-start launches exactly one tray instance.
- [ ] Stream Deck connects/disconnects, observer shows correct session state.
- [ ] MSI upgrade while tray is open gracefully shuts down and replaces EXE.
- [ ] Settings and logs persist through upgrades and uninstall.
- [ ] Downgrade is rejected and leaves the newer app installed.
- [ ] Uninstall clears Start Menu shortcut, executable, and user-login Run entry.
- [ ] No SudoVDA driver or Steam installation is touched.

If you have an unsigned MSI from Actions, Windows SmartScreen may warn. Do not bypass organizational security policy. Run validation before treating it as a stable release.
