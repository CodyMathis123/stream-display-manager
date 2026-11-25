// Steam host evidence and file lifecycle. No display mutation or tray UI.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <string>
#include <filesystem>
#include "SteamObserver.h"

namespace sdm {
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
void SteamObserver::ParseSignal(const std::string& line,EventSink emit) {
    if(line.empty() || line[0]!='[') return;
    auto end=line.find("] ");
    if(end==std::string::npos)return;
    std::string message=line.substr(end+2);
    if(!message.empty() && message.back()=='\r')message.pop_back();
    const std::string start="Streaming started to ";
    if(message.rfind(start,0)==0 && message.find(" at ",start.size())!=std::string::npos){
        ++session;
        streamActive=true;endObserved=false;appId=0;
        emit("steam_signal","started","\"session\":"+std::to_string(session));
    }else if(message=="Encoding complete"){
        streamActive=false;endObserved=true;appId=0;
        emit("steam_signal","completed","\"session\":"+std::to_string(session));
    }else if(streamActive && message.rfind("Game: ",0)==0 && message.back()==')') {
        auto open=message.rfind('(');
        if(open==std::string::npos)return;
        try{
            const auto id=std::stoll(message.substr(open+1,message.size()-open-2));
            if(id>0){appId=id;emit("steam_signal","game-selected",
                "\"session\":"+std::to_string(session)+",\"appId\":"+std::to_string(id));}
        }catch(...){}
    }
}
void SteamObserver::Poll(EventSink emit) {
    if(steamlog_.empty() || !tail_.Attached())
        steamlog_=SteamLogPath(); // Steam may install/start after tray login.
    const bool wasAttached=tail_.Attached();
    auto batch=tail_.Poll(steamlog_,[&](const std::string& line){ParseSignal(line,emit);});
    if(batch.status==sdm::TailStatus::Unavailable) {
        // An absent source cannot authorize keeping the physical monitor off.
        streamActive=false;endObserved=false;appId=0;
        if(wasAttached) emit("steam_log_gap",batch.reason,"");
        else if(!missingLogReported_) emit("steam_log_source","unavailable","");
        missingLogReported_=true;
    } else if(batch.status==sdm::TailStatus::Attached) {
        // Always start from EOF after restart/reappearance, never old events.
        streamActive=false;endObserved=false;appId=0;
        missingLogReported_=false;
        emit("steam_log_source",batch.reason,"\"historyRead\":false");
    } else if(batch.status==sdm::TailStatus::Gap) {
        streamActive=false;endObserved=false;appId=0;
        missingLogReported_=false;
        emit("steam_log_gap",batch.reason,"");
    }
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

} // namespace sdm
