#pragma once
// Pure CCD candidate construction. Never calls SetDisplayConfig, writes a
// recovery journal, or changes the desktop. Unit tests use synthetic CCD data.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <vector>
#include <cstdint>

namespace sdm {
struct Ccd {
    std::vector<DISPLAYCONFIG_PATH_INFO> paths;
    std::vector<DISPLAYCONFIG_MODE_INFO> modes;
};

struct CcdCandidateInfo {
    bool virtualAware=false;
    bool automaticModes=false;
    LONG sourceX=0, sourceY=0;
    UINT32 width=0, height=0;
    UINT32 inputPaths=0, inputModes=0;
    UINT32 outputModes=0;
};

inline bool SameAdapter(const LUID& a,const LUID& b) {
    return a.HighPart==b.HighPart && a.LowPart==b.LowPart;
}

// Build a compact, coherent path and mode set for ONE already-active target.
// The caller has separately identified the sole virtual adapter by Windows
// device name and must have verified a physical desktop is also active.
// Windows treats the source at (0,0) as primary. Rebase the retained source,
// not the saved pre-stream topology, when a secondary target becomes sole.
// A fallback variant lets Windows choose the source/target modes if validating
// the explicit, current 4K modes fails. This may change output resolution.
inline LONG BuildIsolatedCandidate(const Ccd& before,size_t targetIndex,
                                   bool automaticModes,Ccd& out,
                                   CcdCandidateInfo& info) {
    info={};
    out={};
    info.automaticModes=automaticModes;
    info.inputPaths=static_cast<UINT32>(before.paths.size());
    info.inputModes=static_cast<UINT32>(before.modes.size());
    if(before.paths.size()<2 || targetIndex>=before.paths.size())
        return ERROR_INVALID_DATA;
    const auto& path=before.paths[targetIndex];
    if(!(path.flags&DISPLAYCONFIG_PATH_ACTIVE))return ERROR_INVALID_DATA;
    info.virtualAware=(path.flags&DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE)!=0;
    out.paths.push_back(path);
    auto& only=out.paths.front();

    if(automaticModes) {
        if(info.virtualAware) {
            only.sourceInfo.sourceModeInfoIdx=DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID;
            only.sourceInfo.cloneGroupId=DISPLAYCONFIG_PATH_CLONE_GROUP_INVALID;
            only.targetInfo.targetModeInfoIdx=DISPLAYCONFIG_PATH_TARGET_MODE_IDX_INVALID;
            only.targetInfo.desktopModeInfoIdx=DISPLAYCONFIG_PATH_DESKTOP_IMAGE_IDX_INVALID;
        } else {
            only.sourceInfo.modeInfoIdx=DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            only.targetInfo.modeInfoIdx=DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        }
        return ERROR_SUCCESS;
    }

    const UINT32 sourceIndex=info.virtualAware
        ?only.sourceInfo.sourceModeInfoIdx:only.sourceInfo.modeInfoIdx;
    if(sourceIndex>=before.modes.size())return ERROR_INVALID_DATA;
    const auto& original=before.modes[sourceIndex];
    if(original.infoType!=DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE ||
       original.id!=only.sourceInfo.id ||
       !SameAdapter(original.adapterId,only.sourceInfo.adapterId) ||
       !original.sourceMode.width || !original.sourceMode.height)
        return ERROR_INVALID_DATA;
    info.sourceX=original.sourceMode.position.x;
    info.sourceY=original.sourceMode.position.y;
    info.width=original.sourceMode.width;
    info.height=original.sourceMode.height;

    out.modes.push_back(original);
    out.modes.front().sourceMode.position.x=0;
    out.modes.front().sourceMode.position.y=0;
    if(info.virtualAware) {
        only.sourceInfo.sourceModeInfoIdx=0;
        // Recompute desktop image placement after source relocation. This is
        // required when the original desktop image described an extended view.
        only.targetInfo.desktopModeInfoIdx=DISPLAYCONFIG_PATH_DESKTOP_IMAGE_IDX_INVALID;
    } else only.sourceInfo.modeInfoIdx=0;

    const UINT32 targetModeIndex=info.virtualAware
        ?only.targetInfo.targetModeInfoIdx:only.targetInfo.modeInfoIdx;
    const UINT32 invalidIndex=info.virtualAware
        ?DISPLAYCONFIG_PATH_TARGET_MODE_IDX_INVALID:DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
    if(targetModeIndex!=invalidIndex) {
        if(targetModeIndex>=before.modes.size())return ERROR_INVALID_DATA;
        const auto& target=before.modes[targetModeIndex];
        if(target.infoType!=DISPLAYCONFIG_MODE_INFO_TYPE_TARGET ||
           target.id!=only.targetInfo.id ||
           !SameAdapter(target.adapterId,only.targetInfo.adapterId))
            return ERROR_INVALID_DATA;
        out.modes.push_back(target);
        if(info.virtualAware)only.targetInfo.targetModeInfoIdx=1;
        else only.targetInfo.modeInfoIdx=1;
    }
    info.outputModes=static_cast<UINT32>(out.modes.size());
    return ERROR_SUCCESS;
}
} // namespace sdm
