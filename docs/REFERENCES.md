# External implementation references

Official documentation and related work, reviewed 2026-11-25:

- Steam April 2026 client release notes, Remote Play SudoVDA support: https://store.steampowered.com/news/posts/?enddate=1777419598&feed=steam_community_announcements
- Steam Remote Play support: https://help.steampowered.com/en/faqs/view/0689-74B8-92AC-10F2
- Windows QueryDisplayConfig: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
- Windows DisplayConfigGetDeviceInfo: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo
- Windows SetDisplayConfig: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig
- Windows WM_DISPLAYCHANGE: https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange
- Windows EXE VERSIONINFO: https://learn.microsoft.com/en-us/windows/win32/menurc/versioninfo-resource
- WiX CloseApplication: https://docs.firegiant.com/wix/schema/util/closeapplication/
- WiX native application launch / ShellExec: https://docs.firegiant.com/wix/tools/wixext/util/

### Comparative implementations (reference, not dependencies)

- SpoutRemotePlayHost: https://github.com/justjoseorg/SpoutRemotePlayHost — session grace, re-isolation and restore concepts; do not copy its whole streaming host architecture.
- Apollo: https://github.com/ClassicOldSong/Apollo — virtual display profiles, not required in this minimal Steam companion.
- SudoVDA Monitor Controller: https://github.com/XanderSC/sudovda-monitorcontroller — older PowerShell counterpart to the native virtual-only approach.

### Historical-only window-moving prototypes

- Windows SetWinEventHook: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
- Windows SetWindowPlacement: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement

The native product intentionally does not move HWNDs. These references document retired experiments, not a plan to reintroduce game-window placement.

**Note:** Steam's log format and monitor lifetimes are not contractual APIs. Treat third-party reports as hypotheses until validated with instrumented local sessions.
