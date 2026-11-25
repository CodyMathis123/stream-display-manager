#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <algorithm>
#include <vector>
#include "CcdCandidate.h"

namespace sdm {
// Pure Win32 CCD transformation. No system calls or persisted settings.
// Currently requires exactly two active paths: one physical, one virtual.
// Preserve both paths, target modes, and source dimensions; exchange desktop
// positions so the virtual source is at origin, physical source immediately
// right of it. Windows considers source (0,0) to be primary.
struct VirtualPrimaryCandidateInfo {
    LONG virtualSourceX=0,virtualSourceY=0;
    UINT32 virtualWidth=0,virtualHeight=0;
    UINT32 physicalWidth=0,physicalHeight=0;
    bool virtualAware=false;
};
inline bool SourceModeIndex(const Ccd& ccd,size_t pathIndex,size_t& index) {
    if(pathIndex>=ccd.paths.size()) return false;
    const auto& path=ccd.paths[pathIndex];
    const bool aware=(path.flags&DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE)!=0;
    const UINT32 raw=aware?path.sourceInfo.sourceModeInfoIdx:path.sourceInfo.modeInfoIdx;
    if(raw>=ccd.modes.size()) return false;
    const auto& mode=ccd.modes[raw];
    if(mode.infoType!=DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE ||
       mode.id!=path.sourceInfo.id ||
       !SameAdapter(mode.adapterId,path.sourceInfo.adapterId) ||
       mode.sourceMode.width==0 || mode.sourceMode.height==0)
        return false;
    index=raw;
    return true;
}
inline LONG BuildVirtualPrimaryCandidate(const Ccd& before,size_t virtualPath,
                                         Ccd& result,VirtualPrimaryCandidateInfo& info) {
    info={};result={};
    if(before.paths.size()!=2 || virtualPath>=2) return ERROR_INVALID_DATA;
    const size_t physicalPath=1-virtualPath;
    const auto& v=before.paths[virtualPath];
    const auto& p=before.paths[physicalPath];
    if(!(v.flags&DISPLAYCONFIG_PATH_ACTIVE) ||
       !(p.flags&DISPLAYCONFIG_PATH_ACTIVE)) return ERROR_INVALID_DATA;
    size_t vi=0,pi=0;
    if(!SourceModeIndex(before,virtualPath,vi) ||
       !SourceModeIndex(before,physicalPath,pi) || vi==pi)
        return ERROR_INVALID_DATA;
    info.virtualSourceX=before.modes[vi].sourceMode.position.x;
    info.virtualSourceY=before.modes[vi].sourceMode.position.y;
    info.virtualWidth=before.modes[vi].sourceMode.width;
    info.virtualHeight=before.modes[vi].sourceMode.height;
    info.physicalWidth=before.modes[pi].sourceMode.width;
    info.physicalHeight=before.modes[pi].sourceMode.height;
    info.virtualAware=(v.flags&DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE)!=0;
    if(info.virtualWidth>0x7fffffff) return ERROR_INVALID_DATA;
    result=before;
    result.modes[vi].sourceMode.position={0,0};
    result.modes[pi].sourceMode.position={
        static_cast<LONG>(info.virtualWidth),0};
    return ERROR_SUCCESS;
}
} // namespace sdm
