#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <vector>
#include <string>
#include <set>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <cstdint>
#include <cwchar>
#include <cwctype>
#include "SessionPolicy.h"
#include "CcdCandidate.h"
#include "VirtualPrimaryCandidate.h"

namespace sdm {
inline LONG ReadCcd(Ccd& result) {
    for (int attempt=0; attempt<5; ++attempt) {
        UINT32 pc=0, mc=0;
        LONG err=GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE,&pc,&mc);
        if(err) return err;
        Ccd next;
        next.paths.resize(pc);
        next.modes.resize(mc);
        err=QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE,&pc,next.paths.data(),
                               &mc,next.modes.data(),nullptr);
        if(err==ERROR_INSUFFICIENT_BUFFER) continue;
        if(err) return err;
        next.paths.resize(pc); next.modes.resize(mc);
        result=std::move(next);
        return ERROR_SUCCESS;
    }
    return ERROR_INSUFFICIENT_BUFFER;
}
inline std::wstring SourceName(const DISPLAYCONFIG_PATH_SOURCE_INFO& source) {
    DISPLAYCONFIG_SOURCE_DEVICE_NAME info{};
    info.header.type=DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
    info.header.size=sizeof(info);
    info.header.adapterId=source.adapterId;
    info.header.id=source.id;
    return DisplayConfigGetDeviceInfo(&info.header)==ERROR_SUCCESS
        ? std::wstring(info.viewGdiDeviceName) : L"";
}
struct Screen {
    std::wstring name, description;
    bool isVirtual=false, primary=false;
    LONG width=0, height=0;
    std::set<std::wstring> modes;
};
inline std::vector<Screen> Screens(bool enumerateModes=false) {
    std::vector<Screen> screens;
    for(DWORD i=0;i<32;++i) {
        DISPLAY_DEVICEW device{sizeof(DISPLAY_DEVICEW)};
        if(!EnumDisplayDevicesW(nullptr,i,&device,0)) break;
        if(!(device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP)) continue;
        DEVMODEW current{}; current.dmSize=sizeof(current);
        if(!EnumDisplaySettingsExW(device.DeviceName,ENUM_CURRENT_SETTINGS,&current,0)) continue;
        Screen s;
        s.name=device.DeviceName; s.description=device.DeviceString;
        std::wstring lower=s.description;
        std::transform(lower.begin(),lower.end(),lower.begin(),towlower);
        std::wstring id=device.DeviceID; std::transform(id.begin(),id.end(),id.begin(),towlower);
        s.isVirtual=lower.find(L"sudo")!=std::wstring::npos ||
                    lower.find(L"virtual display adapter")!=std::wstring::npos ||
                    id.find(L"sudovda")!=std::wstring::npos;
        s.primary=(device.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE)!=0;
        s.width=static_cast<LONG>(current.dmPelsWidth);
        s.height=static_cast<LONG>(current.dmPelsHeight);
        // Advertised GDI display modes, not a claim about the game's renderer.
        for(DWORD mode=0;enumerateModes && mode<2048;++mode) {
            DEVMODEW m{}; m.dmSize=sizeof(m);
            if(!EnumDisplaySettingsExW(device.DeviceName,mode,&m,0)) break;
            if(m.dmPelsWidth==0 || m.dmPelsHeight==0) continue;
            s.modes.insert(std::to_wstring(m.dmPelsWidth)+L"x"+
                           std::to_wstring(m.dmPelsHeight)+L"@"+
                           std::to_wstring(m.dmDisplayFrequency));
        }
        screens.push_back(std::move(s));
    }
    return screens;
}
inline bool HasPhysical(const std::vector<Screen>& screens) {
    return std::any_of(screens.begin(),screens.end(),[](const Screen& s){return !s.isVirtual;});
}
inline bool VirtualOnly(const std::vector<Screen>& screens) {
    return screens.size()==1 && screens.front().isVirtual;
}
inline bool IsVirtualPrimaryExtended(const std::vector<Screen>& screens) {
    return screens.size()==2 && HasPhysical(screens) &&
        std::any_of(screens.begin(),screens.end(),
           [](const Screen& s){return s.isVirtual && s.primary;});
}


// Raw CCD snapshot with exact native struct sizes, CRC, and a version marker.
// Never write persistent Windows display settings. A reboot should normally
// revert an unsaved topology change. Journal is only for recovery while logged in.
struct JournalHeader {
    uint32_t magic=0x314D4453; // SDM1
    uint32_t version=1;
    uint32_t pathSize=sizeof(DISPLAYCONFIG_PATH_INFO);
    uint32_t modeSize=sizeof(DISPLAYCONFIG_MODE_INFO);
    uint32_t paths=0, modes=0, checksum=0;
};
inline uint32_t Hash(const void* bytes,size_t count,uint32_t seed=2166136261u) {
    auto p=static_cast<const unsigned char*>(bytes);
    for(size_t i=0;i<count;++i) {seed^=p[i];seed*=16777619u;}
    return seed;
}
inline bool WriteJournal(const std::filesystem::path& path,const Ccd& ccd) {
    if(ccd.paths.empty() || ccd.paths.size()>32 || ccd.modes.size()>128) return false;
    JournalHeader h;
    h.paths=static_cast<uint32_t>(ccd.paths.size());
    h.modes=static_cast<uint32_t>(ccd.modes.size());
    h.checksum=Hash(ccd.paths.data(),ccd.paths.size()*sizeof(DISPLAYCONFIG_PATH_INFO));
    h.checksum=Hash(ccd.modes.data(),ccd.modes.size()*sizeof(DISPLAYCONFIG_MODE_INFO),h.checksum);
    auto temp=path;temp+=L".tmp";
    std::ofstream out(temp,std::ios::binary|std::ios::trunc);
    if(!out) return false;
    out.write(reinterpret_cast<const char*>(&h),sizeof(h));
    out.write(reinterpret_cast<const char*>(ccd.paths.data()),ccd.paths.size()*sizeof(DISPLAYCONFIG_PATH_INFO));
    out.write(reinterpret_cast<const char*>(ccd.modes.data()),ccd.modes.size()*sizeof(DISPLAYCONFIG_MODE_INFO));
    out.flush();
    if(!out.good()) return false;
    out.close();
    return MoveFileExW(temp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)!=FALSE;
}
inline bool ReadJournal(const std::filesystem::path& path,Ccd& ccd) {
    std::ifstream in(path,std::ios::binary);
    if(!in) return false;
    JournalHeader h{};
    in.read(reinterpret_cast<char*>(&h),sizeof(h));
    if(!in || h.magic!=0x314D4453 || h.version!=1 ||
       h.pathSize!=sizeof(DISPLAYCONFIG_PATH_INFO) ||
       h.modeSize!=sizeof(DISPLAYCONFIG_MODE_INFO) ||
       h.paths<2 || h.paths>32 || h.modes>128) return false;
    Ccd next;
    next.paths.resize(h.paths);next.modes.resize(h.modes);
    in.read(reinterpret_cast<char*>(next.paths.data()),h.paths*sizeof(DISPLAYCONFIG_PATH_INFO));
    in.read(reinterpret_cast<char*>(next.modes.data()),h.modes*sizeof(DISPLAYCONFIG_MODE_INFO));
    if(!in || in.peek()!=EOF) return false;
    uint32_t checksum=Hash(next.paths.data(),next.paths.size()*sizeof(DISPLAYCONFIG_PATH_INFO));
    checksum=Hash(next.modes.data(),next.modes.size()*sizeof(DISPLAYCONFIG_MODE_INFO),checksum);
    if(checksum!=h.checksum) return false;
    ccd=std::move(next);
    return true;
}
inline bool Restore(const std::filesystem::path& journal) {
    if(!std::filesystem::exists(journal)) return true;
    auto screens=Screens();
    if(HasPhysical(screens) && std::none_of(screens.begin(),screens.end(),
                     [](const Screen& s){return s.isVirtual;})) {
        // Steam can remove the virtual output first, causing the original
        // CCD target ID to become unavailable. Physical desktop is recovered.
        return DeleteFileW(journal.c_str())!=FALSE;
    }
    Ccd saved;
    const bool hasSaved=ReadJournal(journal,saved);
    bool recovered=RestoreWithFallback(
        [&]() {
            if(!hasSaved) return false;
            return SetDisplayConfig(static_cast<UINT32>(saved.paths.size()),saved.paths.data(),
                static_cast<UINT32>(saved.modes.size()),saved.modes.data(),
                SDC_APPLY|SDC_USE_SUPPLIED_DISPLAY_CONFIG|SDC_ALLOW_CHANGES|
                SDC_VIRTUAL_MODE_AWARE)==ERROR_SUCCESS;
        },
        [&]() {
            // Apply the persisted Windows configuration; never save streaming
            // topology into the display database.
            return SetDisplayConfig(0,nullptr,0,nullptr,
                SDC_APPLY|SDC_USE_DATABASE_CURRENT)==ERROR_SUCCESS;
        },
        [&]() { return HasPhysical(Screens()); });
    if(!recovered) return false; // Preserve the journal for another attempt.
    return DeleteFileW(journal.c_str())!=FALSE;
}
// Diagnostics describe which CCD step failed, without serializing adapter
// LUIDs, monitor identifiers, or the raw Windows display journal.
struct SwitchDiagnostics {
    const char* stage="precondition";
    const char* modePlan="explicit";
    LONG explicitValidateError=ERROR_SUCCESS;
    LONG fallbackValidateError=ERROR_SUCCESS;
    LONG sourceX=0,sourceY=0;
    UINT32 sourceWidth=0,sourceHeight=0;
    UINT32 beforePaths=0,beforeModes=0,candidateModes=0;
    bool virtualAware=false;
    bool journalWritten=false;
};

// Construct and validate before making ANY display mutations. The explicit
// candidate preserves current source dimensions (notably docked 4K), moves
// the source to (0,0), and remaps referenced mode indexes. If Windows rejects
// it, try a single Windows-selected-modes candidate in SDC_VALIDATE only;
// applying that fallback is still gated by journal + verification/rollback.
inline LONG ValidatedVirtualCandidate(const Ccd& before,
                                      const std::wstring& virtualName,
                                      Ccd& only,SwitchDiagnostics& diag) {
    diag.beforePaths=static_cast<UINT32>(before.paths.size());
    diag.beforeModes=static_cast<UINT32>(before.modes.size());
    size_t found=before.paths.size();
    for(size_t i=0;i<before.paths.size();++i) {
        if(SourceName(before.paths[i].sourceInfo)!=virtualName)continue;
        if(found!=before.paths.size()) {
            diag.stage="ambiguous-ccd-source";
            return ERROR_INVALID_DATA;
        }
        found=i;
    }
    if(found==before.paths.size() || before.paths.size()<2) {
        diag.stage="missing-ccd-source";
        return ERROR_INVALID_DATA;
    }
    CcdCandidateInfo info{};
    diag.stage="build-explicit";
    LONG result=BuildIsolatedCandidate(before,found,false,only,info);
    if(result)return result;
    diag.sourceX=info.sourceX;
    diag.sourceY=info.sourceY;
    diag.sourceWidth=info.width;
    diag.sourceHeight=info.height;
    diag.virtualAware=info.virtualAware;
    diag.candidateModes=info.outputModes;

    constexpr UINT32 flags=SDC_USE_SUPPLIED_DISPLAY_CONFIG|
        SDC_ALLOW_CHANGES|SDC_VIRTUAL_MODE_AWARE;
    diag.stage="validate-explicit";
    result=SetDisplayConfig(static_cast<UINT32>(only.paths.size()),only.paths.data(),
        static_cast<UINT32>(only.modes.size()),only.modes.data(),flags|SDC_VALIDATE);
    diag.explicitValidateError=result;
    if(!result)return ERROR_SUCCESS;

    // The explicit modes were rejected. Ask Windows to select viable modes
    // only after a separate validation; retain the failed error for diagnosis.
    Ccd relaxed;
    CcdCandidateInfo relaxedInfo{};
    diag.stage="build-auto";
    result=BuildIsolatedCandidate(before,found,true,relaxed,relaxedInfo);
    if(result)return result;
    diag.stage="validate-auto";
    result=SetDisplayConfig(static_cast<UINT32>(relaxed.paths.size()),
        relaxed.paths.data(),0,nullptr,flags|SDC_VALIDATE);
    diag.fallbackValidateError=result;
    if(result)return result;
    diag.modePlan="automatic";
    diag.candidateModes=0;
    only=std::move(relaxed);
    return ERROR_SUCCESS;
}

// Re-isolation must never replace the pre-stream journal with a drifted
// two-display snapshot. It only changes ACTIVE paths for the current stream.
inline LONG RepairVirtualOnly(const std::filesystem::path& journal) {
    if(!std::filesystem::exists(journal))return ERROR_FILE_NOT_FOUND;
    auto screens=Screens();
    if(!HasPhysical(screens) ||
       std::count_if(screens.begin(),screens.end(),
           [](const Screen& s){return s.isVirtual;})!=1)return ERROR_INVALID_DATA;
    const auto it=std::find_if(screens.begin(),screens.end(),
        [](const Screen& s){return s.isVirtual;});
    Ccd current;
    LONG result=ReadCcd(current);
    if(result)return result;
    Ccd only;
    SwitchDiagnostics diag;
    result=ValidatedVirtualCandidate(current,it->name,only,diag);
    if(result)return result;
    constexpr UINT32 flags=SDC_USE_SUPPLIED_DISPLAY_CONFIG|
        SDC_ALLOW_CHANGES|SDC_VIRTUAL_MODE_AWARE;
    result=SetDisplayConfig(static_cast<UINT32>(only.paths.size()),only.paths.data(),
        static_cast<UINT32>(only.modes.size()),
        only.modes.empty()?nullptr:only.modes.data(),flags|SDC_APPLY);
    return result?result:(VirtualOnly(Screens())?ERROR_SUCCESS:ERROR_INVALID_STATE);
}
inline LONG SwitchVirtualOnly(const std::filesystem::path& journal,
                              SwitchDiagnostics* provided=nullptr) {
    SwitchDiagnostics local{};
    auto& diag=provided?*provided:local;
    diag={};
    if(std::filesystem::exists(journal)){
        diag.stage="journal-already-present";
        return ERROR_ALREADY_EXISTS;
    }
    auto screens=Screens();
    if(!HasPhysical(screens) ||
       std::count_if(screens.begin(),screens.end(),
           [](const Screen& s){return s.isVirtual;})!=1) {
        diag.stage="precondition";
        return ERROR_INVALID_DATA;
    }
    const auto it=std::find_if(screens.begin(),screens.end(),
        [](const Screen& s){return s.isVirtual;});
    Ccd before;
    diag.stage="read-ccd";
    LONG result=ReadCcd(before);
    if(result)return result;
    Ccd only;
    result=ValidatedVirtualCandidate(before,it->name,only,diag);
    if(result)return result;
    diag.stage="write-journal";
    if(!WriteJournal(journal,before))return ERROR_WRITE_FAULT;
    diag.journalWritten=true;
    constexpr UINT32 flags=SDC_USE_SUPPLIED_DISPLAY_CONFIG|
        SDC_ALLOW_CHANGES|SDC_VIRTUAL_MODE_AWARE;
    diag.stage="apply";
    result=SetDisplayConfig(static_cast<UINT32>(only.paths.size()),only.paths.data(),
        static_cast<UINT32>(only.modes.size()),
        only.modes.empty()?nullptr:only.modes.data(),flags|SDC_APPLY);
    if(result){
        Restore(journal);  // Never leave an unverified virtual-only desktop.
        return result;
    }
    diag.stage="verify";
    if(!VirtualOnly(Screens())){
        Restore(journal);
        return ERROR_INVALID_STATE;
    }
    diag.stage="verified";
    return ERROR_SUCCESS;
}
// The virtual-primary mode leaves BOTH Windows outputs attached, so games
// can initialize without monitor hot-removal. Only the source origins change.
// The original entire CCD snapshot is journaled and restored at stream end.
inline LONG ValidateVirtualPrimary(const Ccd& before,const std::wstring& virtualName,
                                    Ccd& candidate,SwitchDiagnostics& diag) {
    diag.modePlan="extended-primary";
    diag.beforePaths=static_cast<UINT32>(before.paths.size());
    diag.beforeModes=static_cast<UINT32>(before.modes.size());
    diag.candidateModes=diag.beforeModes;
    if(before.paths.size()!=2){
        diag.stage="expected-two-paths";
        return ERROR_INVALID_DATA;
    }
    size_t vi=before.paths.size();
    for(size_t i=0;i<before.paths.size();++i) {
        if(SourceName(before.paths[i].sourceInfo)!=virtualName)continue;
        if(vi<before.paths.size()){
            diag.stage="ambiguous-source";return ERROR_INVALID_DATA;
        }
        vi=i;
    }
    if(vi==before.paths.size()){
        diag.stage="missing-virtual-path";return ERROR_INVALID_DATA;
    }
    VirtualPrimaryCandidateInfo info{};
    diag.stage="build-extended-primary";
    LONG status=BuildVirtualPrimaryCandidate(before,vi,candidate,info);
    if(status)return status;
    diag.sourceX=info.virtualSourceX;
    diag.sourceY=info.virtualSourceY;
    diag.sourceWidth=info.virtualWidth;
    diag.sourceHeight=info.virtualHeight;
    diag.virtualAware=info.virtualAware;
    constexpr UINT32 flags=SDC_USE_SUPPLIED_DISPLAY_CONFIG|
        SDC_ALLOW_CHANGES|SDC_VIRTUAL_MODE_AWARE;
    diag.stage="validate-extended-primary";
    status=SetDisplayConfig(static_cast<UINT32>(candidate.paths.size()),candidate.paths.data(),
        static_cast<UINT32>(candidate.modes.size()),candidate.modes.data(),flags|SDC_VALIDATE);
    diag.explicitValidateError=status;
    return status;
}
inline LONG SwitchVirtualPrimary(const std::filesystem::path& journal,
                                 SwitchDiagnostics* provided=nullptr) {
    SwitchDiagnostics local{};
    auto& diag=provided?*provided:local;
    diag={};diag.modePlan="extended-primary";
    if(std::filesystem::exists(journal)){
        diag.stage="journal-already-present";
        return ERROR_ALREADY_EXISTS;
    }
    const auto screens=Screens();
    if(screens.size()!=2 || !HasPhysical(screens) ||
       std::count_if(screens.begin(),screens.end(),
          [](const Screen& s){return s.isVirtual;})!=1){
        diag.stage="expected-two-displays";return ERROR_INVALID_DATA;
    }
    const auto it=std::find_if(screens.begin(),screens.end(),
        [](const Screen& s){return s.isVirtual;});
    Ccd before;diag.stage="read-ccd";
    LONG status=ReadCcd(before);
    if(status)return status;
    Ccd candidate;
    status=ValidateVirtualPrimary(before,it->name,candidate,diag);
    if(status)return status;
    diag.stage="write-journal";
    if(!WriteJournal(journal,before))return ERROR_WRITE_FAULT;
    diag.journalWritten=true;
    constexpr UINT32 flags=SDC_USE_SUPPLIED_DISPLAY_CONFIG|
        SDC_ALLOW_CHANGES|SDC_VIRTUAL_MODE_AWARE;
    diag.stage="apply-extended-primary";
    status=SetDisplayConfig(static_cast<UINT32>(candidate.paths.size()),candidate.paths.data(),
        static_cast<UINT32>(candidate.modes.size()),candidate.modes.data(),flags|SDC_APPLY);
    if(status){Restore(journal);return status;}
    diag.stage="verify-extended-primary";
    if(!IsVirtualPrimaryExtended(Screens())){
        Restore(journal);return ERROR_INVALID_STATE;
    }
    diag.stage="verified";
    return ERROR_SUCCESS;
}
inline LONG RepairVirtualPrimary(const std::filesystem::path& journal){
    if(!std::filesystem::exists(journal))return ERROR_FILE_NOT_FOUND;
    const auto screens=Screens();
    if(screens.size()!=2 || !HasPhysical(screens))return ERROR_INVALID_DATA;
    const auto it=std::find_if(screens.begin(),screens.end(),
        [](const Screen& s){return s.isVirtual;});
    if(it==screens.end())return ERROR_INVALID_DATA;
    Ccd current;
    LONG status=ReadCcd(current);
    if(status)return status;
    Ccd candidate;SwitchDiagnostics diag;
    status=ValidateVirtualPrimary(current,it->name,candidate,diag);
    if(status)return status;
    constexpr UINT32 flags=SDC_USE_SUPPLIED_DISPLAY_CONFIG|
        SDC_ALLOW_CHANGES|SDC_VIRTUAL_MODE_AWARE;
    status=SetDisplayConfig(static_cast<UINT32>(candidate.paths.size()),candidate.paths.data(),
        static_cast<UINT32>(candidate.modes.size()),candidate.modes.data(),flags|SDC_APPLY);
    return status?status:
        (IsVirtualPrimaryExtended(Screens())?ERROR_SUCCESS:ERROR_INVALID_STATE);
}

inline bool EmergencyExtend() {
    return SetDisplayConfig(0,nullptr,0,nullptr,
        SDC_TOPOLOGY_EXTEND|SDC_APPLY)==ERROR_SUCCESS;
}
} // namespace sdm
