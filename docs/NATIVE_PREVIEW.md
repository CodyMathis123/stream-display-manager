# Native Win32 migration (preview)

This branch follows the SensorDash tray build pattern: MSVC x64, C++20, statically linked CRT (/MT), a Win32 message-only window, Shell_NotifyIcon, and WiX 5 packaging. The native executable loads no CLR or WinForms.

## Artifacts

The **Native C++ preview (MSVC)** GitHub Action builds a standalone StreamDisplayManager.Native.exe and a separate StreamDisplayManager-NativePreview MSI. The preview installer uses %LOCALAPPDATA%\\StreamDisplayManagerNativePreview; it does not replace the managed application or register login startup.

## Running safely

Both executables use the same Local\\StreamDisplayManager.Observer mutex. Exit the managed tray app before launching the native preview. If the native preview immediately exits, check whether the managed app is still running. Verify that the physical display is restored before switching implementations.

The tray menu provides the virtual-only setting, manual restoration, mode capture, diagnostics folder and Exit. The native app reads the existing %LOCALAPPDATA%\\StreamDisplayManager\\settings.json preference.

Standalone command-line arguments:

- --modes: capture and log the Windows mode census without switching displays
- --restore: recover the native snapshot
- --extend: request Windows extended topology
- --self-test: run a non-mutating smoke check

Native restoration uses %LOCALAPPDATA%\\StreamDisplayManager\\display-native-restore.bin. It does **not** overwrite or deserialize the managed build's separate JSON journal. Stream detection begins at Steam streaming_log.txt EOF, never from historical sessions.

## Display modes

The native logger writes %LOCALAPPDATA%\\StreamDisplayManager\\logs\\native-events.jsonl. Each topology change and manual capture writes display_modes events: display device name, driver description, whether SudoVDA is recognized, primary status, current width/height, all GDI-advertised width/height/refresh combinations from EnumDisplaySettingsExW, and a boolean advertises3840x2160. It also queries active Windows CCD path count and result code.

Compare the captured virtual display modes with the Steam Deck handheld and when docked to a 4K display. The dock does not guarantee that Steam will request a 4K mode. Advertised Windows modes are distinct from what the game actually offers.

## Migration gates

1. Native MSVC compilation, self-test and WiX preview packaging pass in CI.
2. Verify native stream start/end, monitor restoration, and crash/restart handling on the real PC.
3. Compare idle working set, private bytes and CPU between managed and native processes.
4. Port remaining managed window-placement fallback if retaining it is desirable.
5. Only then change the production MSI to ship the native executable.

**Parity limitation:** this native preview implements the virtual-only strategy and mode diagnostics, not yet the managed application's optional game-window correction or full game identity diagnostics. Production installer remains unchanged while the native version is tested.
