# External implementation references

Official documentation and related work, reviewed 2026-11-25:

- Steam April 2026 client release notes, Remote Play SudoVDA support: https://store.steampowered.com/news/posts/?enddate=1777419598&feed=steam_community_announcements
- Steam Remote Play support: https://help.steampowered.com/en/faqs/view/0689-74B8-92AC-10F2
- Windows QueryDisplayConfig: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
- Windows DisplayConfigGetDeviceInfo: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo
- Windows SetDisplayConfig: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig
- Windows WM_DISPLAYCHANGE: https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange
- Windows SetWinEventHook: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
- Windows SetWindowPlacement: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement
- Third-party prototype (PowerShell, requires inspection before reuse): https://github.com/XanderSC/sudovda-monitorcontroller

**Note:** Steam's log format and monitor lifetimes are not contractual APIs. Treat third-party reports as hypotheses until validated with instrumented local sessions.
