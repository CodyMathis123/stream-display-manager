# Native design and validation

Stream Display Manager is a small Windows x64 C++20 tray application. The only supported application source is in the native/ directory. It does not load .NET or WinForms.

## Implementation

- Win32 Shell_NotifyIcon, message-only window, per-user singleton mutex.
- Steam host log (streaming_log.txt) opened at EOF; explicit start/end markers, no historical replay.
- QueryDisplayConfig and SetDisplayConfig switch active Windows paths to the sole SudoVDA output when a live stream is confirmed. The switch is not persisted in the Windows display database.
- Saved CCD configuration is stored as a versioned binary journal before mutation. Normal session end and startup recovery restore the physical desktop; the journal is cleared when Steam/Windows already restored it.
- EnumDisplaySettingsExW enumerates every supported resolution and refresh rate on topology changes or demand. The modes log distinguishes active resolution from advertised modes and flags 3840x2160.
- HKCU Run entry starts the native tray at login; the tray menu can toggle startup. The MSI creates the entry initially.
- Per-user virtual-only preference stored under %LOCALAPPDATA%\StreamDisplayManager\settings.json.
- Diagnostics under %LOCALAPPDATA%\StreamDisplayManager\logs\native-events.jsonl.

## Build and installation

.github/workflows/native-ci.yml uses MSVC x64 C++20 /O2 /MT, resource compiler, and WiX 5. The only MSI is StreamDisplayManager-Setup-<version>-x64.msi. It installs the native app to a per-user directory, sets a normal Start Menu shortcut and an HKCU Run startup registration. No separate service or driver is installed.

Older managed and native preview installers have different identities. Uninstall them first before installing the single native MSI. No automatic compatibility migration is implemented or required.

## Validation

- The initial native preview built successfully using MSVC and WiX.
- Live hardware streaming test confirmed SudoVDA-only at 1280x800 and physical monitor restoration to 3440x1440 after the stream.
- Native Windows CI measured a 9.43 MiB working set and 1.61 MiB private bytes at idle; actual desktop observation was approximately 2 MB in Task Manager. These are different metrics and environments.
- During handheld streaming, SudoVDA advertised 3840x2160@60 and @120 even while 1280x800 was active.
- Docked Steam Deck validation has not yet been performed.
- Test the canonical installer startup/uninstall behavior on the host before a stable release.

## Diagnostics and recovery

Right-click tray -> Capture display modes. Compare display_modes events before/after docking. Full GDI-advertised modes do not guarantee a particular game exposes those modes.

Manual commands:

- StreamDisplayManager.Native.exe --modes
- StreamDisplayManager.Native.exe --restore
- StreamDisplayManager.Native.exe --extend
- StreamDisplayManager.Native.exe --self-test

A reboot normally restores the last Windows-saved topology because the temporary switch does not persist it, but unexpected driver failures can require local recovery.
