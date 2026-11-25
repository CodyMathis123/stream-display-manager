# Local development — Phase 1

## Requirements

- Windows 10/11, interactive desktop user.
- .NET 10 SDK (or the matching .NET 10 desktop runtime for a packaged release).
- Steam + SudoVDA for meaningful remote-play observations; not needed for the app to start.

## Commands

~~~powershell
dotnet restore StreamDisplayManager.slnx
dotnet build StreamDisplayManager.slnx -c Release
dotnet run --project tests/StreamDisplayManager.Core.SpecTests
dotnet run --project src/StreamDisplayManager.App
~~~

The app appears in the notification area with a generic icon. Right-click to capture a snapshot, open logs, or exit.

## Output

%LOCALAPPDATA%\StreamDisplayManager\logs\observer-YYYY-MM-DD-NN.jsonl

Events: lifecycle, system_snapshot, error. At most 5 files/day, 10 MiB/file, with 7-day cleanup at startup. A snapshot is saved on startup, on a change to observed state, on Windows display/device/power notifications, or when manually requested.

Data includes adapter names and IDs, physical display bounds, top-level window handles/positions/process names and whether Steam.exe is running. **Window titles and command lines are intentionally not collected.**

## Current limitations

- Read-only only: it does not move windows, change the primary display, change resolutions, or turn monitors off.
- SteamRunning means Steam.exe exists, *not* that streaming is active.
- SudoVDA candidate identification is a string heuristic and must be validated on the target machine.
- Screen.AllScreens and EnumDisplayDevices are diagnostics, not full QueryDisplayConfig topology/identity.
- Win32 enumeration excludes some invisible, tiny, minimized or protected windows.
- A desktop message notification may arrive before the topology stabilizes; periodic snapshots reconcile this.
- No session classification or game recognition is wired to the observer yet.
- The CI build must be verified before calling this executable validated.

## What to capture

1. Baseline on the physical ultrawide, with no remote stream.
2. Docked Steam Deck streaming Starfield at 3840x2160/60.
3. End stream while game remains open.
4. Handheld Steam Deck streaming at 1280x800/60.
5. Reconnect and restart Steam; optionally test sleep/resume.

Review/anonymize logs before sharing. In Phase 1.5 we'll add a version-aware Steam session signal parser and authoritative display identity.
