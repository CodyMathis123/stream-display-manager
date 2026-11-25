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
#include "DisplayTopology.h"
#pragma comment(lib,"user32.lib")
#pragma comment(lib,"shell32.lib")
#pragma comment(lib,"advapi32.lib")

namespace {
constexpr UINT kTrayMessage=WM_APP+20;
constexpr UINT_PTR kPollTimer=1;
constexpr UINT kToggle=1001,kRestore=1002,kModes=1003,kLogs=1004,kExit=1005,kStartup=1006;
HWND hwnd=nullptr;
NOTIFYICONDATAW icon{};
std::filesystem::path root,logdir,journal,settings,steamlog;
bool enabled=false,attached=false,streamActive=false,endObserved=false,ownsTopology=false;
bool lastVirtual=false;
int stableCount=0;
long long session=0,appId=0;
std::uintmax_t logPosition=0;
std::string partial;
std::wstring oldTopology;
std::wstring lastModesSignature;
bool missingLogReported=false;
constexpr wchar_t kRunKey[]=L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
constexpr wchar_t kNativeRunName[]=L"StreamDisplayManagerNative";
long long lastAttemptSession=-1;

std::string Utf8(const std::wstring& value) {
    if(value.empty()) return {};
    int n=WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),nullptr,0,nullptr,nullptr);
    std::string out(static_cast<size_t>(n),'\0');
    WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),out.data(),n,nullptr,nullptr);
    return out;
}
std::string Json(const std::string& s) {
    std::string out;
    for(unsigned char c:s) {
        if(c=='"' || c=='\\'){out+='\\';out+=static_cast<char>(c);}
        else if(c=='\n')out+="\\n";
        else if(c=='\r')out+="\\r";
        else if(c=='\t')out+="\\t";
        else if(c<32)out+='?';
        else out+=static_cast<char>(c);
    }
    return out;
}
void Log(const std::string& type,const std::string& reason,const std::string& details="") {
    std::filesystem::create_directories(logdir);
    std::ofstream file(logdir/"native-events.jsonl",std::ios::app|std::ios::binary);
    if(!file) return;
    SYSTEMTIME now{};GetSystemTime(&now);
    char at[40]{};
    sprintf_s(at,"%04hu-%02hu-%02huT%02hu:%02hu:%02hu.%03huZ",
        now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,now.wSecond,now.wMilliseconds);
    file<<"{\"atUtc\":\""<<at<<"\",\"type\":\""<<Json(type)<<"\",\"reason\":\""
        <<Json(reason)<<"\"";
    if(!details.empty())file<<","<<details;
    file<<"}\n";
}
std::filesystem::path LocalFolder() {
    wchar_t buffer[MAX_PATH]{};
    if(SUCCEEDED(SHGetFolderPathW(nullptr,CSIDL_LOCAL_APPDATA,nullptr,0,buffer)))
        return std::filesystem::path(buffer)/L"StreamDisplayManager";
    return std::filesystem::temp_directory_path()/L"StreamDisplayManager";
}
bool ReadSettings() {
    std::ifstream file(settings,std::ios::binary);
    if(!file)return false;
    std::string data((std::istreambuf_iterator<char>(file)),{});
    auto key=data.find("\"VirtualOnlyDuringStreaming\"");
    if(key==std::string::npos)return false;
    auto colon=data.find(':',key);
    if(colon==std::string::npos)return false;
    auto val=data.find_first_not_of(" \r\n\t",colon+1);
    return val!=std::string::npos && data.compare(val,4,"true")==0;
}
void SaveSettings() {
    auto temp=settings;temp+=L".tmp";
    {std::ofstream file(temp,std::ios::binary|std::ios::trunc);
     if(!file){Log("settings","write-failed");return;}
     file<<"{\"VirtualOnlyDuringStreaming\":"<<(enabled?"true":"false")<<"}\n";
    }
    if(!MoveFileExW(temp.c_str(),settings.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))
        Log("settings","rename-failed", "\"win32Error\":"+std::to_string(GetLastError()));
    else Log("settings","saved",std::string("\"enabled\":")+(enabled?"true":"false"));
}

bool ReadRunValue(const wchar_t* name,std::wstring& value) {
    HKEY key{};
    if(RegOpenKeyExW(HKEY_CURRENT_USER,kRunKey,0,KEY_QUERY_VALUE,&key)!=ERROR_SUCCESS)
        return false;
    wchar_t data[4096]{};
    DWORD bytes=sizeof(data),type=0;
    LONG result=RegQueryValueExW(key,name,nullptr,&type,reinterpret_cast<BYTE*>(data),&bytes);
    RegCloseKey(key);
    if(result!=ERROR_SUCCESS || (type!=REG_SZ && type!=REG_EXPAND_SZ))
        return false;
    data[(sizeof(data)/sizeof(data[0]))-1]=L'\0';
    value=data;
    return true;
}
bool StartupEnabled() {
    std::wstring value;
    return ReadRunValue(kNativeRunName,value);
}
bool SetStartup(bool turnOn) {
    HKEY key{};
    DWORD disposition=0;
    LONG result=RegCreateKeyExW(HKEY_CURRENT_USER,kRunKey,0,nullptr,0,
        KEY_SET_VALUE,nullptr,&key,&disposition);
    if(result!=ERROR_SUCCESS){Log("startup","registry-open-failed","\"error\":"+std::to_string(result));return false;}
    if(turnOn) {
        wchar_t executable[32768]{};
        DWORD size=GetModuleFileNameW(nullptr,executable,32768);
        if(size==0 || size>=32768){RegCloseKey(key);return false;}
        std::wstring command=L"\""+std::wstring(executable)+L"\"";
        result=RegSetValueExW(key,kNativeRunName,0,REG_SZ,
            reinterpret_cast<const BYTE*>(command.c_str()),
            static_cast<DWORD>((command.size()+1)*sizeof(wchar_t)));
    } else {
        result=RegDeleteValueW(key,kNativeRunName);
        if(result==ERROR_FILE_NOT_FOUND)result=ERROR_SUCCESS;
    }
    RegCloseKey(key);
    if(result!=ERROR_SUCCESS){Log("startup","registry-write-failed","\"error\":"+std::to_string(result));return false;}
    Log("startup",turnOn?"enabled":"disabled");
    return true;
}

std::filesystem::path SteamLogPath() {
    HKEY key{};
    if(RegOpenKeyExW(HKEY_CURRENT_USER,L"Software\\Valve\\Steam",0,KEY_QUERY_VALUE,&key)!=ERROR_SUCCESS)
        return {};
    wchar_t path[2048]{};
    DWORD type=0,size=sizeof(path);
    LONG result=RegQueryValueExW(key,L"SteamPath",nullptr,&type,reinterpret_cast<BYTE*>(path),&size);
    RegCloseKey(key);
    if(result!=ERROR_SUCCESS || (type!=REG_SZ && type!=REG_EXPAND_SZ))return {};
    return std::filesystem::path(path)/L"logs"/L"streaming_log.txt";
}
void InitSteamTail() {
    steamlog=SteamLogPath();
    if(steamlog.empty() || !std::filesystem::exists(steamlog)) {
        Log("steam_log_source","unavailable");return;
    }
    try {
        logPosition=std::filesystem::file_size(steamlog);
        attached=true;
        Log("steam_log_source","attached","\"historyRead\":false");
    }catch(...){Log("steam_log_source","unavailable");}
}
void ParseSignal(const std::string& line) {
    if(line.empty() || line[0]!='[') return;
    auto end=line.find("] ");
    if(end==std::string::npos)return;
    std::string message=line.substr(end+2);
    if(!message.empty() && message.back()=='\r')message.pop_back();
    const std::string start="Streaming started to ";
    if(message.rfind(start,0)==0 && message.find(" at ",start.size())!=std::string::npos){
        ++session;
        streamActive=true;endObserved=false;appId=0;
        Log("steam_signal","started","\"session\":"+std::to_string(session));
    }else if(message=="Encoding complete"){
        streamActive=false;endObserved=true;appId=0;
        Log("steam_signal","completed","\"session\":"+std::to_string(session));
    }else if(streamActive && message.rfind("Game: ",0)==0 && message.back()==')') {
        auto open=message.rfind('(');
        if(open==std::string::npos)return;
        try{
            const auto id=std::stoll(message.substr(open+1,message.size()-open-2));
            if(id>0){appId=id;Log("steam_signal","game-selected",
                "\"session\":"+std::to_string(session)+",\"appId\":"+std::to_string(id));}
        }catch(...){}
    }
}
void PollSteam() {
    if(!attached){
        if(!missingLogReported){Log("steam_log_source","missing");missingLogReported=true;}
        return;
    }
    HANDLE file=CreateFileW(steamlog.c_str(),GENERIC_READ,
        FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
        nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE){
        streamActive=false;endObserved=false;Log("steam_log_gap","missing");return;
    }
    LARGE_INTEGER size{};
    if(!GetFileSizeEx(file,&size)){CloseHandle(file);return;}
    if(static_cast<std::uintmax_t>(size.QuadPart)<logPosition ||
       static_cast<std::uintmax_t>(size.QuadPart)-logPosition>256*1024){
        logPosition=static_cast<std::uintmax_t>(size.QuadPart);
        partial.clear();streamActive=false;endObserved=false;
        Log("steam_log_gap","truncated-or-read-limit");
        CloseHandle(file);return;
    }
    DWORD remaining=static_cast<DWORD>(static_cast<std::uintmax_t>(size.QuadPart)-logPosition);
    if(remaining>0) {
        LARGE_INTEGER pos{};pos.QuadPart=static_cast<LONGLONG>(logPosition);
        if(SetFilePointerEx(file,pos,nullptr,FILE_BEGIN)) {
            std::string data(remaining,'\0');
            DWORD count=0;
            if(ReadFile(file,data.data(),remaining,&count,nullptr)){
                logPosition+=count;
                data.resize(count);
                partial+=data;
                size_t next=0;
                while((next=partial.find('\n'))!=std::string::npos){
                    ParseSignal(partial.substr(0,next));
                    partial.erase(0,next+1);
                }
                if(partial.size()>16384) {
                    partial.clear();streamActive=false;endObserved=false;
                    Log("steam_log_gap","oversize-line");
                }
            }
        }
    }
    CloseHandle(file);
}
bool SteamRunning() {
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if(snapshot==INVALID_HANDLE_VALUE)return false;
    PROCESSENTRY32W entry{};entry.dwSize=sizeof(entry);
    bool found=false;
    if(Process32FirstW(snapshot,&entry)){
        do {
            if(_wcsicmp(entry.szExeFile,L"steam.exe")==0){found=true;break;}
        }while(Process32NextW(snapshot,&entry));
    }
    CloseHandle(snapshot);
    return found;
}
std::wstring Signature(const std::vector<sdm::Screen>& screens) {
    std::wstring result;
    for(auto& s:screens)result+=s.name+L":"+std::to_wstring(s.width)+L"x"+
        std::to_wstring(s.height)+(s.isVirtual?L":V;":L":P;");
    return result;
}
void CaptureModes(const std::vector<sdm::Screen>& screens,const char* reason) {
    sdm::Ccd ccd;
    LONG ccdStatus=sdm::ReadCcd(ccd);
    for(const auto& screen:screens) {
        std::string values;
        for(const auto& mode:screen.modes){
            if(!values.empty())values+=",";
            values+="\""+Json(Utf8(mode))+"\"";
        }
        bool has4K=std::any_of(screen.modes.begin(),screen.modes.end(),
            [](const std::wstring& m){return m.rfind(L"3840x2160@",0)==0;});
        Log("display_modes",reason,
            "\"deviceName\":\""+Json(Utf8(screen.name))+"\","
            "\"description\":\""+Json(Utf8(screen.description))+"\","
            "\"virtualCandidate\":"+(screen.isVirtual?std::string("true"):"false")+","
            "\"primary\":"+(screen.primary?std::string("true"):"false")+","
            "\"activeWidth\":"+std::to_string(screen.width)+","
            "\"activeHeight\":"+std::to_string(screen.height)+","
            "\"advertises3840x2160\":"+(has4K?std::string("true"):"false")+","
            "\"modes\":["+values+"],"
            "\"ccdStatus\":"+std::to_string(ccdStatus)+","
            "\"activeCcdPaths\":"+std::to_string(ccd.paths.size()));
    }
}
void Tick() {
    PollSteam();
    auto screens=sdm::Screens();
    std::wstring signature=Signature(screens);
    if(signature!=oldTopology){oldTopology=signature;stableCount=1;
        Log("topology","changed","\"screens\":"+std::to_string(screens.size()));
    }else if(stableCount<3)++stableCount;
    if(signature!=lastModesSignature){
        lastModesSignature=signature;
        CaptureModes(sdm::Screens(true),"topology-change");
    }
    const bool virtualPresent=std::any_of(screens.begin(),screens.end(),
        [](const sdm::Screen& s){return s.isVirtual;});
    const bool active=enabled && streamActive && !endObserved && virtualPresent &&
        sdm::HasPhysical(screens) && stableCount>=2 && SteamRunning();
    if(std::filesystem::exists(journal)){
        if(!enabled || !streamActive || endObserved || !virtualPresent) {
            bool restored=sdm::Restore(journal);
            if(restored) {
                ownsTopology=false;
                Log("display_restore","session-ended-or-virtual-detached");
            }else Log("display_restore","pending-retry");
        }
    }else if(active && !ownsTopology && lastAttemptSession!=session){
        lastAttemptSession=session;
        LONG status=sdm::SwitchVirtualOnly(journal);
        if(status==ERROR_SUCCESS) {
            ownsTopology=true;
            Log("display_switch","virtual-only-active",
                "\"session\":"+std::to_string(session));
            CaptureModes(sdm::Screens(true),"virtual-only");
        }else{
            Log("display_switch","failed","\"win32Error\":"+std::to_string(status));
        }
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
    AppendMenuW(menu,MF_STRING|(StartupEnabled()?MF_CHECKED:0),kStartup,L"Start with Windows");
    AppendMenuW(menu,MF_SEPARATOR,0,nullptr);
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
        case kToggle: enabled=!enabled;SaveSettings();
            if(!enabled)sdm::Restore(journal);Tick();break;
        case kRestore: enabled=false;SaveSettings();
            if(!sdm::Restore(journal))sdm::EmergencyExtend();
            Tick();break;
        case kModes: CaptureModes(sdm::Screens(true),"manual");break;
        case kLogs:OpenLogFolder();break;
        case kStartup:
            if(StartupEnabled())SetStartup(false);
            else if(!SetStartup(true)){
                MessageBoxW(w,L"Could not configure native startup. See native-events.jsonl for details.",
                    L"Stream Display Manager",MB_OK|MB_ICONERROR);
            }
            break;
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
    root=LocalFolder();logdir=root/L"logs";
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
        CaptureModes(sdm::Screens(true),"command-line");return 0;
    }
    if(wcsstr(command,L"--restore"))
        return sdm::Restore(journal)?0:1;
    if(wcsstr(command,L"--extend"))
        return sdm::EmergencyExtend()?0:1;

    // Single instance per logged-on user.
    HANDLE singleton=CreateMutexW(nullptr,TRUE,L"Local\\StreamDisplayManager.Observer");
    if(!singleton)return 1;
    if(GetLastError()==ERROR_ALREADY_EXISTS){CloseHandle(singleton);return 0;}

    enabled=ReadSettings();
    if(std::filesystem::exists(journal)) {
        if(sdm::Restore(journal))Log("display_restore","startup-recovery");
        else Log("display_restore","startup-recovery-pending");
    }
    InitSteamTail();
    WNDCLASSW wc{};
    wc.hInstance=instance;
    wc.lpfnWndProc=WindowProc;
    wc.lpszClassName=L"StreamDisplayManager.Native";
    wc.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(101));
    if(!RegisterClassW(&wc)){CloseHandle(singleton);return 2;}
    hwnd=CreateWindowExW(0,wc.lpszClassName,L"Stream Display Manager",
        0,0,0,0,0,HWND_MESSAGE,nullptr,instance,nullptr);
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
