// Win32/registry/settings/diagnostics helpers only. No session policy or tray state.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <winver.h>
#include <algorithm>
#include <fstream>
#include <vector>
#include <cstdint>
#include <cstdio>
#include "AppRuntime.h"
#pragma comment(lib,"advapi32.lib")
#pragma comment(lib,"version.lib")

namespace sdm {
namespace {
constexpr wchar_t kRunKey[]=L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
constexpr wchar_t kNativeRunName[]=L"StreamDisplayManagerNative";
}
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
void Log(const std::filesystem::path& logdir,const std::string& type,const std::string& reason,const std::string& details) {
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
bool ReadSettings(const std::filesystem::path& settings) {
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
void SaveSettings(const std::filesystem::path& settings,bool enabled,const std::filesystem::path& logdir) {
    auto temp=settings;temp+=L".tmp";
    {std::ofstream file(temp,std::ios::binary|std::ios::trunc);
     if(!file){Log(logdir,"settings","write-failed");return;}
     file<<"{\"VirtualOnlyDuringStreaming\":"<<(enabled?"true":"false")<<"}\n";
    }
    if(!MoveFileExW(temp.c_str(),settings.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))
        Log(logdir,"settings","rename-failed", "\"win32Error\":"+std::to_string(GetLastError()));
    else Log(logdir,"settings","saved",std::string("\"enabled\":")+(enabled?"true":"false"));
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
bool SetStartup(bool turnOn,const std::filesystem::path& logdir) {
    HKEY key{};
    DWORD disposition=0;
    LONG result=RegCreateKeyExW(HKEY_CURRENT_USER,kRunKey,0,nullptr,0,
        KEY_SET_VALUE,nullptr,&key,&disposition);
    if(result!=ERROR_SUCCESS){Log(logdir,"startup","registry-open-failed","\"error\":"+std::to_string(result));return false;}
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
    if(result!=ERROR_SUCCESS){Log(logdir,"startup","registry-write-failed","\"error\":"+std::to_string(result));return false;}
    Log(logdir,"startup",turnOn?"enabled":"disabled");
    return true;
}


std::wstring Signature(const std::vector<sdm::Screen>& screens) {
    std::wstring result;
    for(auto& s:screens)result+=s.name+L":"+std::to_wstring(s.width)+L"x"+
        std::to_wstring(s.height)+(s.isVirtual?L":V;":L":P;");
    return result;
}
void CaptureModes(const std::filesystem::path& logdir,const std::vector<sdm::Screen>& screens,const char* reason) {
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
        Log(logdir,"display_modes",reason,
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

std::wstring InstalledFileVersion() {
    wchar_t path[32768]{};
    DWORD length=GetModuleFileNameW(nullptr,path,32768);
    if(!length || length>=32768) return L"Unknown";
    DWORD ignored=0;
    DWORD size=GetFileVersionInfoSizeW(path,&ignored);
    if(!size) return L"Unknown";
    std::vector<BYTE> info(size);
    if(!GetFileVersionInfoW(path,0,size,info.data())) return L"Unknown";
    VS_FIXEDFILEINFO* fixed=nullptr;
    UINT bytes=0;
    if(!VerQueryValueW(info.data(),L"\\",reinterpret_cast<LPVOID*>(&fixed),&bytes) ||
       !fixed || bytes<sizeof(VS_FIXEDFILEINFO) || fixed->dwSignature!=0xFEEF04BD)
        return L"Unknown";
    std::wstring result=std::to_wstring(HIWORD(fixed->dwFileVersionMS))+L"."+
        std::to_wstring(LOWORD(fixed->dwFileVersionMS))+L"."+
        std::to_wstring(HIWORD(fixed->dwFileVersionLS));
    WORD revision=LOWORD(fixed->dwFileVersionLS);
    if(revision) result+=L"."+std::to_wstring(revision);
    return result;
}
void ShowAbout(HWND owner) {
    const std::wstring message=
        L"Stream Display Manager\n"
        L"Version "+InstalledFileVersion()+L"\n\n"
        L"A lightweight native Windows companion for Steam Remote Play.\n"
        L"Uses SudoVDA during streaming and restores the physical desktop afterward.\n\n"
        L"https://github.com/CodyMathis123/stream-display-manager";
    MessageBoxW(owner,message.c_str(),L"About Stream Display Manager",
                MB_OK|MB_ICONINFORMATION);
}


} // namespace sdm
