#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <string>
#include <vector>
#include <cwctype>
#include "DisplayTopology.h"

namespace sdm {
// Opt-in, best-effort relocation of the *foreground Steam game* window.
// Never enumerate/move unrelated desktop windows, never inject/hooks game
// code, and never change monitor primary/topology in this strategy.
// Game executables installed outside steamapps/common will not be moved.
class WindowRelocator {
public:
    template<class Emit>
    void Poll(const std::vector<Screen>& screens,bool active,long long appId,
              std::uint64_t session,std::uint64_t now,Emit emit) {
        if(!active) {Restore(emit);return;}
        if(session!=session_) {
            Restore(emit);
            session_=session;attempts_=0;lastAttempt_=0;reported_=false;
        }
        if(!appId || moved_)return;
        if(attempts_>=12 || (lastAttempt_ && now-lastAttempt_<3000))return;
        ++attempts_;lastAttempt_=now;
        if(!HasPhysical(screens) ||
           std::count_if(screens.begin(),screens.end(),
             [](const Screen& s){return s.isVirtual;})!=1)return;
        const auto target=std::find_if(screens.begin(),screens.end(),
            [](const Screen& s){return s.isVirtual;});
        HWND window=GetForegroundWindow();
        if(!EligibleGameWindow(window))return;
        DWORD pid=0;
        GetWindowThreadProcessId(window,&pid);
        const HMONITOR current=MonitorFromWindow(window,MONITOR_DEFAULTTONULL);
        if(!current)return;
        MONITORINFOEXW currentInfo{};currentInfo.cbSize=sizeof(currentInfo);
        if(!GetMonitorInfoW(current,&currentInfo))return;
        if(_wcsicmp(currentInfo.szDevice,target->name.c_str())==0){
            if(!reported_){emit("window_move","already-on-virtual",
                "\"appId\":"+std::to_string(appId));reported_=true;}
            return;
        }
        DEVMODEW mode{};mode.dmSize=sizeof(mode);
        if(!EnumDisplaySettingsExW(target->name.c_str(),
              ENUM_CURRENT_SETTINGS,&mode,0))return;
        RECT before{};
        if(!GetWindowRect(window,&before))return;
        const LONG oldWidth=before.right-before.left;
        const LONG oldHeight=before.bottom-before.top;
        if(oldWidth<640 || oldHeight<480 || !mode.dmPelsWidth || !mode.dmPelsHeight)
            return;
        const RECT source=currentInfo.rcMonitor;
        const bool coversMonitor=std::abs(oldWidth-(source.right-source.left))<12 &&
                                 std::abs(oldHeight-(source.bottom-source.top))<12;
        const LONG width=coversMonitor?static_cast<LONG>(mode.dmPelsWidth):
            std::min(oldWidth,static_cast<LONG>(mode.dmPelsWidth));
        const LONG height=coversMonitor?static_cast<LONG>(mode.dmPelsHeight):
            std::min(oldHeight,static_cast<LONG>(mode.dmPelsHeight));
        const LONG x=mode.dmPosition.x+(coversMonitor?0:
            std::max<LONG>(0,(static_cast<LONG>(mode.dmPelsWidth)-width)/2));
        const LONG y=mode.dmPosition.y+(coversMonitor?0:
            std::max<LONG>(0,(static_cast<LONG>(mode.dmPelsHeight)-height)/2));
        if(!SetWindowPos(window,nullptr,x,y,width,height,
            SWP_NOACTIVATE|SWP_NOZORDER|SWP_NOOWNERZORDER)) {
            emit("window_move","set-window-pos-failed",
                "\"win32Error\":"+std::to_string(GetLastError()));
            return;
        }
        // Move only a single foreground Steam game window per session.
        window_=window;pid_=pid;original_=before;moved_=true;
        emit("window_move","moved-to-virtual",
            "\"appId\":"+std::to_string(appId)+
            ",\"width\":"+std::to_string(width)+
            ",\"height\":"+std::to_string(height)+
            ",\"resized\":"+(coversMonitor?"true":"false"));
    }
    template<class Emit>
    void Restore(Emit emit) {
        if(moved_ && IsWindow(window_)) {
            DWORD currentPid=0;GetWindowThreadProcessId(window_,&currentPid);
            if(currentPid==pid_) {
                RECT now{};
                if(GetWindowRect(window_,&now)) {
                    // Only restore if the user/game has not relocated the window
                    // again. Do not overwrite a later manual window placement.
                    const LONG oldWidth=original_.right-original_.left;
                    const LONG oldHeight=original_.bottom-original_.top;
                    if(SetWindowPos(window_,nullptr,original_.left,original_.top,
                         oldWidth,oldHeight,SWP_NOACTIVATE|SWP_NOZORDER|
                         SWP_NOOWNERZORDER))
                        emit("window_move","restored-original-window");
                    else emit("window_move","restore-failed",
                            "\"win32Error\":"+std::to_string(GetLastError()));
                }
            }
        }
        moved_=false;window_=nullptr;pid_=0;
    }
private:
    static bool EligibleGameWindow(HWND window) {
        if(!window || !IsWindow(window) || !IsWindowVisible(window) ||
           IsIconic(window) || GetAncestor(window,GA_ROOT)!=window ||
           GetWindow(window,GW_OWNER)!=nullptr)return false;
        RECT rect{};
        if(!GetWindowRect(window,&rect) ||
           rect.right-rect.left<640 || rect.bottom-rect.top<480)return false;
        DWORD pid=0;
        GetWindowThreadProcessId(window,&pid);
        if(!pid || pid==GetCurrentProcessId())return false;
        HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);
        if(!process)return false;
        wchar_t path[32768]{};DWORD length=32768;
        const bool ok=QueryFullProcessImageNameW(process,0,path,&length)!=FALSE;
        CloseHandle(process);
        if(!ok || length==0)return false;
        std::wstring lower(path,length);
        std::transform(lower.begin(),lower.end(),lower.begin(),towlower);
        return lower.find(L"\\steamapps\\common\\")!=std::wstring::npos;
    }
    std::uint64_t session_=UINT64_MAX,lastAttempt_=0;
    unsigned attempts_=0;
    HWND window_=nullptr;
    DWORD pid_=0;
    RECT original_{};
    bool moved_=false,reported_=false;
};
} // namespace sdm
