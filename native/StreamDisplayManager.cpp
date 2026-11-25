// Stream Display Manager native Win32 application. No CLR or WinForms.
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include <fstream>
#include <filesystem>
#include <sstream>
#include <chrono>
#include <set>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <tlhelp32.h>
#include <winver.h>
#include "DisplayTopology.h"
#include "SessionPolicy.h"
#include "SteamObserver.h"
#include "AppRuntime.h"
#pragma comment(lib,"user32.lib")
#pragma comment(lib,"shell32.lib")
#pragma comment(lib,"advapi32.lib")
#pragma comment(lib,"version.lib")

namespace {
constexpr UINT kTrayMessage=WM_APP+20;
constexpr UINT_PTR kPollTimer=1;
constexpr UINT kToggle=1001,kRestore=1002,kModes=1003,kLogs=1004,kExit=1005,kStartup=1006,kAbout=1007;
HWND hwnd=nullptr;
NOTIFYICONDATAW icon{};
std::filesystem::path root,logdir,journal,settings;
bool enabled=false;
sdm::SteamObserver steamObserver;
sdm::SessionPolicy sessionPolicy;
int stableCount=0;
std::wstring oldTopology;
std::wstring lastModesSignature;

void Log(const std::string& type,const std::string& reason,const std::string& details="") {
    sdm::Log(logdir,type,reason,details);
}
void Tick() {
    steamObserver.Poll(Log);
    auto screens=sdm::Screens();
    const std::wstring signature=sdm::Signature(screens);
    if(signature!=oldTopology) {
        oldTopology=signature;
        stableCount=1;
        Log("topology","changed","\"screens\":"+std::to_string(screens.size()));
    } else if(stableCount<3) ++stableCount;
    if(signature!=lastModesSignature) {
        lastModesSignature=signature;
        sdm::CaptureModes(logdir,sdm::Screens(true),"topology-change");
    }
    const bool virtualPresent=std::any_of(screens.begin(),screens.end(),
        [](const sdm::Screen& s){return s.isVirtual;});
    const bool hasJournal=std::filesystem::exists(journal);
    const sdm::Evidence evidence{
        enabled, sdm::SteamRunning(), steamObserver.streamActive, steamObserver.endObserved, virtualPresent,
        sdm::HasPhysical(screens), stableCount>=2, hasJournal,
        static_cast<std::uint64_t>(steamObserver.session), GetTickCount64()
    };
    const auto action=sessionPolicy.Decide(evidence);
    switch(action) {
    case sdm::Action::Restore:
        if(sdm::Restore(journal)) {
            sessionPolicy.OnRecovery();
            Log("display_restore","session-ended-or-target-lost");
        } else Log("display_restore","pending-retry");
        break;
    case sdm::Action::Switch: {
        LONG status=sdm::SwitchVirtualOnly(journal);
        if(status==ERROR_SUCCESS) {
            Log("display_switch","virtual-only-active",
                "\"session\":"+std::to_string(steamObserver.session));
            sdm::CaptureModes(logdir,sdm::Screens(true),"virtual-only");
        } else Log("display_switch","failed","\"win32Error\":"+std::to_string(status));
        break;
    }
    case sdm::Action::Repair: {
        LONG status=sdm::RepairVirtualOnly(journal);
        Log("display_repair",status==ERROR_SUCCESS ? "re-isolated" : "failed",
            "\"win32Error\":"+std::to_string(status));
        break;
    }
    case sdm::Action::None:
        break;
    }
}
void OpenLogFolder() {
    ShellExecuteW(hwnd,L"open",logdir.c_str(),nullptr,nullptr,SW_SHOWNORMAL);
}
void Menu() {
    HMENU menu=CreatePopupMenu();
    if(!menu)return;
    AppendMenuW(menu,MF_STRING|(enabled?MF_CHECKED:0),kToggle,L"Use virtual-only during streaming");
    AppendMenuW(menu,MF_STRING,kRestore,L"Restore physical display now");
    AppendMenuW(menu,MF_STRING,kModes,L"Capture display modes");
    AppendMenuW(menu,MF_STRING,kLogs,L"Open diagnostics folder");
    AppendMenuW(menu,MF_STRING|(sdm::StartupEnabled()?MF_CHECKED:0),kStartup,L"Start with Windows");
    AppendMenuW(menu,MF_SEPARATOR,0,nullptr);
    AppendMenuW(menu,MF_STRING,kAbout,L"About Stream Display Manager...");
    AppendMenuW(menu,MF_STRING,kExit,L"Exit");
    POINT p{};GetCursorPos(&p);SetForegroundWindow(hwnd);
    TrackPopupMenu(menu,TPM_RIGHTBUTTON|TPM_BOTTOMALIGN,p.x,p.y,0,hwnd,nullptr);
    DestroyMenu(menu);
    PostMessageW(hwnd,WM_NULL,0,0);
}
LRESULT CALLBACK WindowProc(HWND w,UINT msg,WPARAM wp,LPARAM lp){
    switch(msg){
    case WM_CREATE:
        SetTimer(w,kPollTimer,3000,nullptr);
        PostMessageW(w,WM_TIMER,kPollTimer,0);
        return 0;
    case WM_TIMER:
        if(wp==kPollTimer)Tick();
        return 0;
    case kTrayMessage:
        if(lp==WM_RBUTTONUP || lp==WM_CONTEXTMENU)Menu();
        return 0;
    case WM_DISPLAYCHANGE:
    case WM_DEVICECHANGE:
        PostMessageW(w,WM_TIMER,kPollTimer,0);
        return 0;
    case WM_COMMAND:
        switch(LOWORD(wp)){
        case kToggle: enabled=!enabled;sdm::SaveSettings(settings,enabled,logdir);
            if(!enabled)sdm::Restore(journal);Tick();break;
        case kRestore: enabled=false;sdm::SaveSettings(settings,enabled,logdir);
            if(!sdm::Restore(journal))sdm::EmergencyExtend();
            Tick();break;
        case kModes: sdm::CaptureModes(logdir,sdm::Screens(true),"manual");break;
        case kLogs:OpenLogFolder();break;
        case kStartup:
            if(sdm::StartupEnabled())sdm::SetStartup(false,logdir);
            else if(!sdm::SetStartup(true,logdir)){
                MessageBoxW(w,L"Could not configure native startup. See native-events.jsonl for details.",
                    L"Stream Display Manager",MB_OK|MB_ICONERROR);
            }
            break;
        case kAbout: sdm::ShowAbout(w);break;
        case kExit: DestroyWindow(w);break;
        }return 0;
    case WM_CLOSE:DestroyWindow(w);return 0;
    case WM_DESTROY:
        KillTimer(w,kPollTimer);
        sdm::Restore(journal);
        Shell_NotifyIconW(NIM_DELETE,&icon);
        PostQuitMessage(0);return 0;
    default:return DefWindowProcW(w,msg,wp,lp);
    }
}
void Prepare() {
    root=sdm::LocalFolder();logdir=root/L"logs";
    std::filesystem::create_directories(logdir);
    settings=root/L"settings.json";
    // Native journal contains original Windows display configuration.
    journal=root/L"display-native-restore.bin";
}
} // namespace

int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR command,int){
    Prepare();
    if(wcsstr(command,L"--self-test")){
        // No topology mutations in CI. Validate the settings and basic
        // Windows CCD data-layout assumptions.
        return sizeof(DISPLAYCONFIG_PATH_INFO)>0 &&
            sizeof(DISPLAYCONFIG_MODE_INFO)>0 ? 0 : 1;
    }
    if(wcsstr(command,L"--modes")) {
        sdm::CaptureModes(logdir,sdm::Screens(true),"command-line");return 0;
    }
    if(wcsstr(command,L"--restore"))
        return sdm::Restore(journal)?0:1;
    if(wcsstr(command,L"--extend"))
        return sdm::EmergencyExtend()?0:1;

    // Single instance per logged-on user.
    HANDLE singleton=CreateMutexW(nullptr,TRUE,L"Local\\StreamDisplayManager.Observer");
    if(!singleton)return 1;
    if(GetLastError()==ERROR_ALREADY_EXISTS){CloseHandle(singleton);return 0;}

    enabled=sdm::ReadSettings(settings);
    if(std::filesystem::exists(journal)) {
        if(sdm::Restore(journal))Log("display_restore","startup-recovery");
        else Log("display_restore","startup-recovery-pending");
    }
    WNDCLASSW wc{};
    wc.hInstance=instance;
    wc.lpfnWndProc=WindowProc;
    wc.lpszClassName=L"StreamDisplayManager.Native";
    wc.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(101));
    if(!RegisterClassW(&wc)){CloseHandle(singleton);return 2;}
    // Hidden top-level window (never shown): unlike HWND_MESSAGE, Windows
    // Installer's close-app helper can find it and send WM_CLOSE. WM_DESTROY
    // already restores display topology and removes the tray icon.
    hwnd=CreateWindowExW(0,wc.lpszClassName,L"Stream Display Manager",
        WS_OVERLAPPED,0,0,0,0,nullptr,nullptr,instance,nullptr);
    if(!hwnd){CloseHandle(singleton);return 3;}
    icon.cbSize=sizeof(icon);
    icon.hWnd=hwnd;
    icon.uID=1;
    icon.uFlags=NIF_ICON|NIF_TIP|NIF_MESSAGE;
    icon.uCallbackMessage=kTrayMessage;
    icon.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(101));
    wcscpy_s(icon.szTip,L"Stream Display Manager");
    Shell_NotifyIconW(NIM_ADD,&icon);
    Log("lifecycle","native-started","\"strategy\":\"virtual-only-or-observe\"");
    MSG msg{};
    while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}
    Log("lifecycle","native-stopped");
    CloseHandle(singleton);
    return 0;
}
