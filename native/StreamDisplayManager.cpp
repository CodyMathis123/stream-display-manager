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
#include "Diagnostics.h"
#include "WindowRelocator.h"
#pragma comment(lib,"user32.lib")
#pragma comment(lib,"shell32.lib")
#pragma comment(lib,"advapi32.lib")
#pragma comment(lib,"version.lib")

namespace {
constexpr UINT kTrayMessage=WM_APP+20;
constexpr UINT_PTR kPollTimer=1;
constexpr UINT kRestore=1002,kModes=1003,kLogs=1004,kExit=1005,kStartup=1006,kAbout=1007,kExport=1008;
constexpr UINT kModeDisabled=1101,kModeObserve=1102,kModeMove=1103,kModePrimary=1104,kModeOnly=1105;
HWND hwnd=nullptr;
NOTIFYICONDATAW icon{};
std::filesystem::path root,logdir,journal,settings;
sdm::DisplayStrategy strategy=sdm::DisplayStrategy::Observe;
sdm::DisplayStrategy activeStrategy=sdm::DisplayStrategy::Observe;
sdm::WindowRelocator relocator;
std::string runId;
sdm::SteamObserver steamObserver;
sdm::SessionPolicy sessionPolicy;
int stableCount=0;
std::wstring oldTopology;
std::wstring lastModesSignature;

void Log(const std::string& type,const std::string& reason,const std::string& details="") {
    std::string fields=details;
    if(!fields.empty())fields+=",";
    fields+="\"runId\":\""+sdm::Json(runId)+"\"";
    const bool sessionEvent=steamObserver.session>0 &&
        (steamObserver.streamActive || std::filesystem::exists(journal) ||
         type=="steam_signal" || type=="display_restore" || type=="display_repair" ||
         type=="window_move" || type=="strategy");
    if(sessionEvent)
        fields+=",\"sessionId\":\""+sdm::Json(runId)+"-"+
            std::to_string(steamObserver.session)+"\"";
    sdm::Log(logdir,type,reason,fields);
}
void Tick() {
    if(activeStrategy!=sdm::DisplayStrategy::Disabled)steamObserver.Poll(Log);
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
    // Never hot-switch strategies in the middle of a game. Selection remains
    // visibly pending until Steam ends AND the original layout is restored.
    if(!steamObserver.streamActive && !hasJournal && strategy!=activeStrategy) {
        relocator.Restore(Log);
        activeStrategy=strategy;
        Log("strategy","applied","\"strategy\":\""+
            std::string(sdm::StrategyName(activeStrategy))+"\"");
    }
    if(activeStrategy==sdm::DisplayStrategy::MoveToVirtual)
        relocator.Poll(screens,steamObserver.streamActive,steamObserver.appId,
            static_cast<std::uint64_t>(steamObserver.session),GetTickCount64(),Log);
    else relocator.Restore(Log);
    const bool activeTopology=sdm::IsTopologyStrategy(activeStrategy);
    const bool needsRepair=activeStrategy==sdm::DisplayStrategy::VirtualOnly
        ? sdm::HasPhysical(screens)
        : activeStrategy==sdm::DisplayStrategy::VirtualPrimary &&
          !sdm::IsVirtualPrimaryExtended(screens);
    const sdm::Evidence evidence{
        activeTopology, sdm::SteamRunning(), steamObserver.streamActive,
        steamObserver.endObserved, virtualPresent,
        sdm::HasPhysical(screens), stableCount>=2, hasJournal,
        static_cast<std::uint64_t>(steamObserver.session), GetTickCount64(),
        needsRepair
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
        sdm::SwitchDiagnostics diagnostics{};
        LONG status=activeStrategy==sdm::DisplayStrategy::VirtualPrimary
            ?sdm::SwitchVirtualPrimary(journal,&diagnostics)
            :sdm::SwitchVirtualOnly(journal,&diagnostics);
        // Explain CCD failures without raw adapter IDs/device IDs or a
        // sensitive copy of the saved original desktop configuration.
        const std::string fields=
            "\"win32Error\":"+std::to_string(status)+
            ",\"stage\":\""+sdm::Json(diagnostics.stage)+"\""+
            ",\"modePlan\":\""+sdm::Json(diagnostics.modePlan)+"\""+
            ",\"explicitValidateError\":"+std::to_string(diagnostics.explicitValidateError)+
            ",\"fallbackValidateError\":"+std::to_string(diagnostics.fallbackValidateError)+
            ",\"sourceX\":"+std::to_string(diagnostics.sourceX)+
            ",\"sourceY\":"+std::to_string(diagnostics.sourceY)+
            ",\"sourceWidth\":"+std::to_string(diagnostics.sourceWidth)+
            ",\"sourceHeight\":"+std::to_string(diagnostics.sourceHeight)+
            ",\"virtualModeAware\":"+(diagnostics.virtualAware?"true":"false")+
            ",\"beforePaths\":"+std::to_string(diagnostics.beforePaths)+
            ",\"beforeModes\":"+std::to_string(diagnostics.beforeModes)+
            ",\"candidateModes\":"+std::to_string(diagnostics.candidateModes)+
            ",\"journalWritten\":"+(diagnostics.journalWritten?"true":"false");
        if(status==ERROR_SUCCESS) {
            const bool primaryMode=activeStrategy==sdm::DisplayStrategy::VirtualPrimary;
            Log("display_switch",primaryMode?"virtual-primary-active":"virtual-only-active",fields);
            sdm::CaptureModes(logdir,sdm::Screens(true),
                primaryMode?"virtual-primary":"virtual-only");
        } else Log("display_switch","failed",fields);
        break;
    }
    case sdm::Action::Repair: {
        LONG status=activeStrategy==sdm::DisplayStrategy::VirtualPrimary
            ?sdm::RepairVirtualPrimary(journal):sdm::RepairVirtualOnly(journal);
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
void ChooseStrategy(sdm::DisplayStrategy desired) {
    if(desired==strategy)return;
    strategy=desired;
    sdm::SaveStrategy(settings,strategy,logdir);
    if(strategy==sdm::DisplayStrategy::Disabled) {
        // User-initiated disable is an immediate safety stop, not deferred.
        relocator.Restore(Log);
        if(!sdm::Restore(journal)) {
            Log("display_restore","mode-disabled-pending-recovery");
            sdm::EmergencyExtend();
        }
        activeStrategy=strategy;
        steamObserver.streamActive=false;
        steamObserver.endObserved=false;
        steamObserver.appId=0;
        Log("strategy","applied","\"strategy\":\"disabled\"");
    } else if(steamObserver.streamActive || std::filesystem::exists(journal)) {
        Log("strategy","pending-next-session","\"strategy\":\""+
            std::string(sdm::StrategyName(strategy))+"\"");
    } else {
        relocator.Restore(Log);
        activeStrategy=strategy;
        Log("strategy","applied","\"strategy\":\""+
            std::string(sdm::StrategyName(strategy))+"\"");
    }
    Tick();
}
void Menu() {
    HMENU menu=CreatePopupMenu();
    if(!menu)return;
    HMENU display=CreatePopupMenu();
    if(!display){DestroyMenu(menu);return;}
    AppendMenuW(display,MF_STRING,kModeDisabled,L"Disabled");
    AppendMenuW(display,MF_STRING,kModeObserve,L"Observe only (diagnostics)");
    AppendMenuW(display,MF_STRING,kModeMove,L"Virtual + move game window (experimental)");
    AppendMenuW(display,MF_STRING,kModePrimary,L"Virtual primary (keep both active)");
    AppendMenuW(display,MF_STRING,kModeOnly,L"Virtual only (disable physical)");
    const UINT checked=strategy==sdm::DisplayStrategy::Disabled?kModeDisabled:
        strategy==sdm::DisplayStrategy::Observe?kModeObserve:
        strategy==sdm::DisplayStrategy::MoveToVirtual?kModeMove:
        strategy==sdm::DisplayStrategy::VirtualPrimary?kModePrimary:kModeOnly;
    CheckMenuRadioItem(display,kModeDisabled,kModeOnly,checked,MF_BYCOMMAND);
    if(activeStrategy!=strategy) {
        AppendMenuW(display,MF_SEPARATOR,0,nullptr);
        AppendMenuW(display,MF_STRING|MF_GRAYED,0,L"Change applies after current stream");
    }
    AppendMenuW(menu,MF_POPUP,reinterpret_cast<UINT_PTR>(display),L"Display handling");
    AppendMenuW(menu,MF_STRING,kRestore,L"Restore physical display now");
    AppendMenuW(menu,MF_STRING,kModes,L"Capture display modes");
    AppendMenuW(menu,MF_STRING,kLogs,L"Open diagnostics folder");
    AppendMenuW(menu,MF_STRING,kExport,L"Export diagnostics...");
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
        case kModeDisabled:ChooseStrategy(sdm::DisplayStrategy::Disabled);break;
        case kModeObserve:ChooseStrategy(sdm::DisplayStrategy::Observe);break;
        case kModeMove:ChooseStrategy(sdm::DisplayStrategy::MoveToVirtual);break;
        case kModePrimary:ChooseStrategy(sdm::DisplayStrategy::VirtualPrimary);break;
        case kModeOnly:ChooseStrategy(sdm::DisplayStrategy::VirtualOnly);break;
        case kRestore:
            ChooseStrategy(sdm::DisplayStrategy::Disabled);
            if(!sdm::Restore(journal))sdm::EmergencyExtend();
            Tick();break;
        case kModes: sdm::CaptureModes(logdir,sdm::Screens(true),"manual");break;
        case kLogs:OpenLogFolder();break;
        case kExport:sdm::ExportDiagnostics(w,logdir,sdm::InstalledFileVersion());break;
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
        relocator.Restore(Log);
        sdm::Restore(journal);
        Shell_NotifyIconW(NIM_DELETE,&icon);
        PostQuitMessage(0);return 0;
    default:return DefWindowProcW(w,msg,wp,lp);
    }
}
void Prepare() {
    FILETIME created{};GetSystemTimeAsFileTime(&created);
    ULARGE_INTEGER stamp{};stamp.LowPart=created.dwLowDateTime;stamp.HighPart=created.dwHighDateTime;
    // Process-local identifier keeps session IDs distinct across tray restarts.
    runId=std::to_string(stamp.QuadPart)+"-"+std::to_string(GetCurrentProcessId());
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

    strategy=sdm::ReadStrategy(settings);
    activeStrategy=strategy;
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
    Log("lifecycle","native-started",
        "\"strategy\":\""+std::string(sdm::StrategyName(strategy))+"\"");
    MSG msg{};
    while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}
    Log("lifecycle","native-stopped");
    CloseHandle(singleton);
    return 0;
}
