# Phase 1: Undocked Steam Deck LCD baseline

**Scope:** 1280×800 / 60 Hz Steam Remote Play from a Windows gaming PC to an undocked LCD Steam Deck. This is the first hardware validation. A TV, Dockcase or wired Ethernet on the Deck is **not** required.

**Safety:** This project's observer is read-only. Keep the physical monitor connected and enabled. Do not run scripts that disable primary/physical displays. Steam/SudoVDA own virtual display creation.

## 0. Prerequisites

- Check out branch `feature/architecture-and-observer-phase1` (PR #1).
- Windows 10/11 with .NET 10 SDK on the host; same local network as Steam Deck.
- Steam Remote Play enabled on both devices, with the same Steam account or a paired authorized client.
- Steam updated to a build with SudoVDA virtual display support (stable since April 28, 2026).
- No separate Windows software or SudoVDA driver needs to be installed on the Steam Deck itself.

### Install the virtual display driver on Windows

The upstream source repository [SudoMaker/SudoVDA](https://github.com/SudoMaker/SudoVDA) currently has no downloadable binary releases. An established packaged installation path is [Apollo releases](https://github.com/ClassicOldSong/Apollo/releases), which bundles the SudoVDA display driver with a Windows installer. Choose the SudoVDA driver component during install. If using *only Steam* as the streaming server, disable Apollo's autostart/service to avoid a second host running, but **do not uninstall Apollo before checking whether doing so also removes SudoVDA**.

This adds a third-party Windows display driver and may require administrative approval/certificate trust. Use releases from the publisher, inspect Windows prompts, and do not run unknown downloaded driver scripts. A separate supported driver-only installation method may be added later.

To inspect whether the driver is registered:

~~~powershell
Get-PnpDevice -Class Display |
  Where-Object FriendlyName -Match 'Sudo|Virtual' |
  Format-Table Status, FriendlyName, InstanceId -AutoSize
~~~

Absence from this view means verification is still needed, not necessarily that the stream is impossible.

### Steam settings on host

Open **Steam → Settings → Remote Play → Advanced Host Options**:
- Select `SudoVDA` as **Primary display while streaming**.
- Prefer options that match the streaming client's resolution/refresh rate.
- Keep hardware encoding enabled if supported by the graphics driver.
- Keep physical ultrawide monitor enabled.

On the Deck, ensure Remote Play is enabled and choose **Stream** from the desktop game's Steam library entry (the launcher may be available from the Play/Stream selector). The first handshake/pairing may require local confirmation.

## 1. Start read-only observer on the Windows PC

From the repo root:

~~~powershell
dotnet build StreamDisplayManager.slnx -c Release
dotnet run --project src/StreamDisplayManager.App
~~~

A notification-area icon should appear. Right-click for **Capture snapshot**, **Open diagnostics folder**, or **Exit**.

Logs reside under:

`%LOCALAPPDATA%\StreamDisplayManager\logs\observer-YYYY-MM-DD-NN.jsonl`

Data includes process names, PIDs, display adapter IDs, display bounds, and top-level window geometry; no window titles or command-line arguments. Review logs before sharing.

## 2. Capture a baseline without streaming

1. Leave the physical 3440×1440 monitor connected and keep Steam open.
2. Start the observer and choose **Capture snapshot** from the tray.
3. Note current physical display names/geometry in the log.
4. Verify Steam running is logged as an observation only—not an active stream.

## 3. First undocked Steam Deck session

1. Keep the observer running on the Windows host.
2. On the undocked LCD Deck, stream a desktop-installed game using Steam Remote Play (Starfield is useful later for window-placement testing, but any game is fine initially).
3. Verify the actual Windows virtual display mode, expected around **1280×800 @ 60 Hz**, and whether the physical monitor remains enabled. Do not assume the mode; record it.
4. While connected, right-click the observer tray icon and choose **Capture snapshot**.
5. Exit the stream while leaving the game open if practical.
6. Capture another snapshot and check whether the virtual display disappears and how the game window relocates.
7. Repeat once to assess whether monitor identity/device numbering changes.

For first-pass diagnostics, capture **Steam idle → stream start → game launch → stream end**. Collect timings of any display notifications and note whether windows changed position unexpectedly.

## 4. What successful Phase 1 looks like

- [ ] Steam Remote Play works to Deck LCD (Wi-Fi is fine for this first test).
- [ ] Windows recognizes SudoVDA when a stream starts.
- [ ] Read-only observer shows a virtual-display candidate and changed screen bounds.
- [ ] Logs identify game process/window and which display contains it.
- [ ] Physical ultrawide remains enabled and usable.
- [ ] Ending the stream does not strand windows off-screen.
- [ ] Logs have no sensitive titles or command lines.

The observer **does not yet parse Steam's active-session log, confirm a stream, move windows, or restore window placements**. Phase 1.5 will use these observations to implement trustworthy session detection.

## 5. Next increment

Use anonymized trace observations to implement authoritative `QueryDisplayConfig` identity and explicit Steam session confirmation. Then add the safe window-moving policy in Phase 2. The docked 4K60 path becomes a second test matrix case after the undocked baseline is stable.
