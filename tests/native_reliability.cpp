// Focused native tests; no SetDisplayConfig calls or display mutation.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <iostream>
#include "../native/SessionPolicy.h"
#include "../native/SteamTail.h"
#include "../native/Diagnostics.h"
#include "../native/CcdCandidate.h"
#include "../native/DisplayStrategy.h"
#include "../native/VirtualPrimaryCandidate.h"

#define CHECK(expr) do { if(!(expr)) { std::cerr << "FAIL " << __LINE__ << ": " #expr "\n"; return 1; } } while(0)

int PolicyTests() {
    using namespace sdm;
    SessionPolicy p;
    Evidence e{};
    e.enabled=true;e.steamRunning=true;e.streamActive=true;
    e.virtualPresent=true;e.physicalPresent=true;e.stable=true;
    e.session=1;e.now=1000;
    CHECK(p.Decide(e)==Action::Switch);
    CHECK(p.Decide(e)==Action::None); // exactly one initial switch
    e.hasJournal=true;e.physicalPresent=false;
    CHECK(p.Decide(e)==Action::None); // healthy virtual-only stream

    // Explicit stream end keeps the existing SudoVDA for 12s, not indefinitely.
    e.streamActive=false;e.endObserved=true;e.now=2000;
    CHECK(p.Decide(e)==Action::None);
    e.now=13999;
    CHECK(p.Decide(e)==Action::None);
    e.now=14000;
    CHECK(p.Decide(e)==Action::Restore);

    // Reconnect cancels end grace; don't deactivate the working virtual path.
    e.now=16000;e.endObserved=true;e.streamActive=false;
    CHECK(p.Decide(e)==Action::Restore); // grace already expired
    e.now=17000;e.endObserved=false;e.streamActive=true;e.session=2;
    CHECK(p.Decide(e)==Action::None);

    // Drift is repaired at most twice in a session with a 15s cooldown.
    e.physicalPresent=true;e.stable=true;e.needsRepair=true;
    CHECK(p.Decide(e)==Action::Repair);
    e.now=31999;
    CHECK(p.Decide(e)==Action::None);
    e.now=32000;
    CHECK(p.Decide(e)==Action::Repair);
    e.now=50000;
    CHECK(p.Decide(e)==Action::None);

    // Fresh session starts a new bounded drift budget.
    e.session=3;
    CHECK(p.Decide(e)==Action::Repair);
    e.physicalPresent=false;e.needsRepair=false;
    CHECK(p.Decide(e)==Action::None);

    // Safety: lost target/Steam/log evidence restores without waiting for grace.
    e.virtualPresent=false;
    CHECK(p.Decide(e)==Action::Restore);
    e.virtualPresent=true;e.steamRunning=false;
    CHECK(p.Decide(e)==Action::Restore);
    e.steamRunning=true;e.streamActive=false;e.endObserved=false;
    CHECK(p.Decide(e)==Action::Restore);
    e.streamActive=true;e.enabled=false;
    CHECK(p.Decide(e)==Action::Restore);

    // After a completed restore, new explicit stream start is necessary.
    e.hasJournal=false;e.enabled=true;e.physicalPresent=true;e.virtualPresent=true;e.needsRepair=false;
    e.streamActive=false;
    CHECK(p.Decide(e)==Action::None);
    e.streamActive=true;e.session=4;
    CHECK(p.Decide(e)==Action::Switch);

    // If Steam never logged a start, mere SudoVDA attachment is insufficient.
    SessionPolicy fresh;
    e.streamActive=false;
    CHECK(fresh.Decide(e)==Action::None);

    // Grace cancellation before expiry, not just when already expired.
    SessionPolicy quick;
    e.streamActive=true;e.session=5;e.hasJournal=true;e.physicalPresent=false;
    e.endObserved=false;e.now=1000;
    CHECK(quick.Decide(e)==Action::None);
    e.streamActive=false;e.endObserved=true;e.now=2000;
    CHECK(quick.Decide(e)==Action::None);
    e.streamActive=true;e.endObserved=false;e.now=3000;e.session=6;
    CHECK(quick.Decide(e)==Action::None);
    e.streamActive=false;e.endObserved=true;e.now=4000;
    CHECK(quick.Decide(e)==Action::None);
    e.now=15000;
    CHECK(quick.Decide(e)==Action::None);
    e.now=16000;
    CHECK(quick.Decide(e)==Action::Restore);
    return 0;
}
void Append(const std::filesystem::path& p,const std::string& str) {
    std::ofstream f(p,std::ios::binary|std::ios::app);f<<str;f.close();
}
void Replace(const std::filesystem::path& p,const std::string& str) {
    std::ofstream f(p,std::ios::binary|std::ios::trunc);f<<str;f.close();
}
int RestoreFallbackTests() {
    using namespace sdm;
    int saves=0, databases=0, checks=0;
    CHECK(RestoreWithFallback([&](){++saves;return true;},
        [&](){++databases;return true;}, [&](){++checks;return true;}));
    CHECK(saves==1 && databases==0 && checks==1);
    saves=databases=checks=0;
    CHECK(RestoreWithFallback([&](){++saves;return false;},
        [&](){++databases;return true;}, [&](){++checks;return true;}));
    CHECK(saves==1 && databases==1 && checks==1);
    saves=databases=checks=0;
    // A success return from SetDisplayConfig without a physical display is
    // not accepted; the saved-database fallback must still be attempted.
    CHECK(RestoreWithFallback([&](){++saves;return true;},
        [&](){++databases;return true;}, [&](){return ++checks==2;}));
    CHECK(saves==1 && databases==1 && checks==2);
    saves=databases=checks=0;
    CHECK(!RestoreWithFallback([&](){++saves;return false;},
        [&](){++databases;return false;}, [&](){++checks;return true;}));
    CHECK(saves==1 && databases==1 && checks==0);
    return 0;
}
int TailTests() {
    using namespace sdm;
    auto dir=std::filesystem::temp_directory_path()/
        (L"sdm-reliability-"+std::to_wstring(GetCurrentProcessId())+
         L"-"+std::to_wstring(GetTickCount64()));
    std::filesystem::create_directories(dir);
    auto path=dir/L"streaming_log.txt";
    auto old=dir/L"streaming_log.old";
    Replace(path,"[old] Streaming started to steamdeck at 127.0.0.1\n");
    SteamTail tail;
    std::vector<std::string> seen;
    auto poll=[&](){return tail.Poll(path,[&](const std::string& line){seen.push_back(line);});};
    CHECK(poll().status==TailStatus::Attached);
    CHECK(seen.empty()); // no replay after attach
    Append(path,"[new] Streaming started to steamdeck");
    CHECK(poll().status==TailStatus::Lines);
    CHECK(seen.empty()); // partial line deferred
    Append(path," at 127.0.0.1\n");
    CHECK(poll().status==TailStatus::Lines);
    CHECK(seen.size()==1);

    // Replacement with a LARGER file must be noticed via file ID, not length.
    CHECK(MoveFileExW(path.c_str(),old.c_str(),MOVEFILE_REPLACE_EXISTING)!=FALSE);
    Replace(path,"[stale] Encoding complete\n[stale] Streaming started to steamdeck at 127.0.0.1\n");
    CHECK(poll().status==TailStatus::Gap);
    CHECK(seen.size()==1);
    Append(path,"[fresh] Encoding complete\n");
    CHECK(poll().status==TailStatus::Lines);
    CHECK(seen.size()==2);
    CHECK(seen.back()=="[fresh] Encoding complete");

    // Truncation discards evidence and starts at EOF.
    Replace(path,"");
    CHECK(poll().status==TailStatus::Gap);
    Append(path,"[again] Encoding complete\n");
    CHECK(poll().status==TailStatus::Lines);
    CHECK(seen.size()==3);

    // Missing and reappearing file attaches anew at EOF.
    CHECK(DeleteFileW(path.c_str())!=FALSE);
    CHECK(poll().status==TailStatus::Unavailable);
    Replace(path,"[historical] Encoding complete\n");
    CHECK(poll().status==TailStatus::Attached);
    CHECK(seen.size()==3);
    Append(path,"[live] Encoding complete\n");
    CHECK(poll().status==TailStatus::Lines);
    CHECK(seen.size()==4);

    std::filesystem::remove_all(dir);
    return 0;
}

int DiagnosticTests() {
    namespace fs=std::filesystem;
    const auto dir=fs::temp_directory_path() /
        (L"sdm-diag-test-"+std::to_wstring(GetCurrentProcessId()));
    fs::remove_all(dir);
    fs::create_directories(dir);
    // Existing legacy logs are left untouched; new events use dated files.
    const auto legacy=dir/L"native-events.jsonl";
    {std::ofstream file(legacy);file<<"legacy history\n";}

    sdm::AppendDiagnostic(dir,"steam_signal","started",
        "\"runId\":\"test-run\",\"sessionId\":\"test-run-1\",\"session\":1");
    sdm::AppendDiagnostic(dir,"steam_signal","game-selected",
        "\"runId\":\"test-run\",\"sessionId\":\"test-run-1\",\"appId\":1716740");
    sdm::AppendDiagnostic(dir,"display_restore","pending-retry",
        "\"runId\":\"test-run\",\"sessionId\":\"test-run-1\"");
    auto files=std::vector<fs::path>{};
    for(const auto& x:fs::directory_iterator(dir))
        if(x.path().filename().wstring().find(L"native-events-")==0)
            files.push_back(x.path());
    CHECK(files.size()==1);
    std::ifstream log(files[0],std::ios::binary);
    std::string line;
    int count=0;
    bool sawWarning=false;
    while(std::getline(log,line)){
        ++count;
        CHECK(line.find("\"atUtc\":\"")!=std::string::npos);
        CHECK(line.find("\"sessionId\":\"test-run-1\"")!=std::string::npos);
        if(line.find("pending-retry")!=std::string::npos) {
            CHECK(line.find("\"severity\":\"error\"")!=std::string::npos);
            sawWarning=true;
        }
    }
    CHECK(count==3 && sawWarning);
    log.close(); // Windows cannot reliably remove a file held by ifstream.
    CHECK(fs::exists(legacy));

    // Retention removes expired dated segments and leaves legacy history
    // untouched. Use file time rather than relying on wall-clock timestamps.
    const auto expired=dir/L"native-events-2000-01-01.jsonl";
    {std::ofstream file(expired);file<<"old\n";}
    fs::last_write_time(expired,fs::file_time_type::clock::now()-std::chrono::hours(24*20));
    sdm::PruneDiagnostics(dir);
    CHECK(!fs::exists(expired));
    CHECK(fs::exists(files[0]));
    CHECK(fs::exists(legacy));

    // Simulate a full 2 MiB segment: the next event must move to another
    // dated file rather than grow the old file indefinitely.
    {
        std::ofstream out(files[0],std::ios::binary|std::ios::app);
        out<<std::string(2*1024*1024,'x');
    }
    auto previousSize=fs::file_size(files[0]);
    sdm::AppendDiagnostic(dir,"lifecycle","test-rotation",
        "\"runId\":\"test-run\"");
    CHECK(fs::file_size(files[0])==previousSize);
    int segments=0;
    for(const auto& x:fs::directory_iterator(dir))
        if(x.path().filename().wstring().find(L"native-events-")==0)
            ++segments;
    CHECK(segments==2);
    fs::remove_all(dir);
    return 0;
}


int CandidateTests() {
    using namespace sdm;
    Ccd before;
    before.paths.resize(2);
    before.modes.resize(5);
    const LUID physicalAdapter{11,1};
    const LUID virtualAdapter{22,2};
    auto& physical=before.paths[0];
    physical.flags=DISPLAYCONFIG_PATH_ACTIVE|DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE;
    physical.sourceInfo.adapterId=physicalAdapter;
    physical.sourceInfo.id=0;
    physical.sourceInfo.sourceModeInfoIdx=0;
    physical.targetInfo.adapterId=physicalAdapter;
    physical.targetInfo.id=0;
    physical.targetInfo.targetModeInfoIdx=1;
    physical.targetInfo.desktopModeInfoIdx=DISPLAYCONFIG_PATH_DESKTOP_IMAGE_IDX_INVALID;

    auto& virtualPath=before.paths[1];
    virtualPath.flags=DISPLAYCONFIG_PATH_ACTIVE|DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE;
    virtualPath.sourceInfo.adapterId=virtualAdapter;
    virtualPath.sourceInfo.id=1;
    virtualPath.sourceInfo.sourceModeInfoIdx=2;
    virtualPath.targetInfo.adapterId=virtualAdapter;
    virtualPath.targetInfo.id=4;
    virtualPath.targetInfo.targetModeInfoIdx=3;
    virtualPath.targetInfo.desktopModeInfoIdx=4;

    before.modes[0].infoType=DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE;
    before.modes[0].adapterId=physicalAdapter;
    before.modes[0].id=0;
    before.modes[0].sourceMode.width=3440;
    before.modes[0].sourceMode.height=1440;
    before.modes[0].sourceMode.position={0,0};

    before.modes[1].infoType=DISPLAYCONFIG_MODE_INFO_TYPE_TARGET;
    before.modes[1].adapterId=physicalAdapter;
    before.modes[1].id=0;

    before.modes[2].infoType=DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE;
    before.modes[2].adapterId=virtualAdapter;
    before.modes[2].id=1;
    before.modes[2].sourceMode.width=3840;
    before.modes[2].sourceMode.height=2160;
    before.modes[2].sourceMode.position={3440,120};

    before.modes[3].infoType=DISPLAYCONFIG_MODE_INFO_TYPE_TARGET;
    before.modes[3].adapterId=virtualAdapter;
    before.modes[3].id=4;

    before.modes[4].infoType=DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE;
    before.modes[4].adapterId=virtualAdapter;
    before.modes[4].id=4;

    Ccd only; CcdCandidateInfo info;
    CHECK(BuildIsolatedCandidate(before,1,false,only,info)==ERROR_SUCCESS);
    CHECK(info.virtualAware && !info.automaticModes);
    CHECK(info.sourceX==3440 && info.sourceY==120);
    CHECK(info.width==3840 && info.height==2160);
    CHECK(only.paths.size()==1 && only.modes.size()==2);
    CHECK(only.paths[0].sourceInfo.sourceModeInfoIdx==0);
    CHECK(only.paths[0].targetInfo.targetModeInfoIdx==1);
    CHECK(only.paths[0].targetInfo.desktopModeInfoIdx==DISPLAYCONFIG_PATH_DESKTOP_IMAGE_IDX_INVALID);
    CHECK(only.modes[0].sourceMode.position.x==0);
    CHECK(only.modes[0].sourceMode.position.y==0);
    CHECK(only.modes[0].sourceMode.width==3840);
    CHECK(only.modes[0].sourceMode.height==2160);
    CHECK(only.modes[1].infoType==DISPLAYCONFIG_MODE_INFO_TYPE_TARGET);
    // The journal input MUST NOT be modified by candidate construction.
    CHECK(before.modes[2].sourceMode.position.x==3440);
    CHECK(before.paths[1].sourceInfo.sourceModeInfoIdx==2);
    CHECK(before.paths[1].targetInfo.desktopModeInfoIdx==4);

    Ccd autoMode;CcdCandidateInfo autoInfo;
    CHECK(BuildIsolatedCandidate(before,1,true,autoMode,autoInfo)==ERROR_SUCCESS);
    CHECK(autoInfo.automaticModes && autoMode.paths.size()==1 && autoMode.modes.empty());
    CHECK(autoMode.paths[0].sourceInfo.sourceModeInfoIdx==
        DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID);
    CHECK(autoMode.paths[0].sourceInfo.cloneGroupId==
        DISPLAYCONFIG_PATH_CLONE_GROUP_INVALID);
    CHECK(autoMode.paths[0].targetInfo.targetModeInfoIdx==
        DISPLAYCONFIG_PATH_TARGET_MODE_IDX_INVALID);
    CHECK(autoMode.paths[0].targetInfo.desktopModeInfoIdx==
        DISPLAYCONFIG_PATH_DESKTOP_IMAGE_IDX_INVALID);

    // Malformed or ambiguous mode references must fail closed, not mutate
    // any real display or speculate about unreferenced mode entries.
    auto invalid=before;
    invalid.paths[1].sourceInfo.sourceModeInfoIdx=999;
    CHECK(BuildIsolatedCandidate(invalid,1,false,only,info)==ERROR_INVALID_DATA);
    invalid=before;
    invalid.modes[3].id=5; // target ID disagrees with selected path
    CHECK(BuildIsolatedCandidate(invalid,1,false,only,info)==ERROR_INVALID_DATA);
    CHECK(BuildIsolatedCandidate(before,2,false,only,info)==ERROR_INVALID_DATA);
    before.paths.resize(1);
    CHECK(BuildIsolatedCandidate(before,0,false,only,info)==ERROR_INVALID_DATA);
    return 0;
}


int StrategyTests() {
    using namespace sdm;
    CHECK(ParseStrategy("disabled")==DisplayStrategy::Disabled);
    CHECK(ParseStrategy("observe")==DisplayStrategy::Observe);
    CHECK(ParseStrategy("virtual-move")==DisplayStrategy::MoveToVirtual);
    CHECK(ParseStrategy("virtual-primary")==DisplayStrategy::VirtualPrimary);
    CHECK(ParseStrategy("virtual-only")==DisplayStrategy::VirtualOnly);
    CHECK(ParseStrategy("bad-value")==DisplayStrategy::Observe);
    CHECK(IsTopologyStrategy(DisplayStrategy::VirtualPrimary));
    CHECK(IsTopologyStrategy(DisplayStrategy::VirtualOnly));
    CHECK(!IsTopologyStrategy(DisplayStrategy::Disabled));
    CHECK(!IsTopologyStrategy(DisplayStrategy::Observe));
    CHECK(!IsTopologyStrategy(DisplayStrategy::MoveToVirtual));
    return 0;
}
int VirtualPrimaryTests() {
    using namespace sdm;
    Ccd before;before.paths.resize(2);before.modes.resize(4);
    const LUID physical{11,1},virtualAdapter{22,2};
    auto& physicalPath=before.paths[0];
    physicalPath.flags=DISPLAYCONFIG_PATH_ACTIVE|DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE;
    physicalPath.sourceInfo.adapterId=physical;
    physicalPath.sourceInfo.id=0;
    physicalPath.sourceInfo.sourceModeInfoIdx=0;
    physicalPath.targetInfo.adapterId=physical;
    physicalPath.targetInfo.id=0;
    physicalPath.targetInfo.targetModeInfoIdx=1;
    auto& virtualPath=before.paths[1];
    virtualPath.flags=DISPLAYCONFIG_PATH_ACTIVE|DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE;
    virtualPath.sourceInfo.adapterId=virtualAdapter;
    virtualPath.sourceInfo.id=1;
    virtualPath.sourceInfo.sourceModeInfoIdx=2;
    virtualPath.targetInfo.adapterId=virtualAdapter;
    virtualPath.targetInfo.id=1;
    virtualPath.targetInfo.targetModeInfoIdx=3;
    auto setSource=[&](size_t index,LUID adapter,UINT32 id,
                       LONG x,LONG y,UINT32 w,UINT32 h){
        auto& m=before.modes[index];
        m.infoType=DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE;
        m.adapterId=adapter;m.id=id;
        m.sourceMode.position={x,y};
        m.sourceMode.width=w;m.sourceMode.height=h;
    };
    setSource(0,physical,0,0,0,3440,1440);
    setSource(2,virtualAdapter,1,3440,0,3840,2160);
    before.modes[1].infoType=DISPLAYCONFIG_MODE_INFO_TYPE_TARGET;
    before.modes[1].adapterId=physical;before.modes[1].id=0;
    before.modes[3].infoType=DISPLAYCONFIG_MODE_INFO_TYPE_TARGET;
    before.modes[3].adapterId=virtualAdapter;before.modes[3].id=1;
    Ccd primary;VirtualPrimaryCandidateInfo info{};
    CHECK(BuildVirtualPrimaryCandidate(before,1,primary,info)==ERROR_SUCCESS);
    CHECK(primary.paths.size()==2 && primary.modes.size()==4);
    CHECK(primary.modes[2].sourceMode.position.x==0);
    CHECK(primary.modes[2].sourceMode.position.y==0);
    CHECK(primary.modes[0].sourceMode.position.x==3840);
    CHECK(primary.modes[0].sourceMode.position.y==0);
    CHECK(primary.modes[2].sourceMode.width==3840);
    CHECK(primary.modes[2].sourceMode.height==2160);
    CHECK(primary.paths[0].sourceInfo.sourceModeInfoIdx==0);
    CHECK(primary.paths[1].sourceInfo.sourceModeInfoIdx==2);
    CHECK(info.virtualSourceX==3440 && info.virtualSourceY==0);
    CHECK(info.physicalWidth==3440 && info.virtualWidth==3840);
    CHECK(before.modes[0].sourceMode.position.x==0);
    CHECK(before.modes[2].sourceMode.position.x==3440); // journal untouched

    Ccd invalid=before;
    invalid.paths[1].sourceInfo.sourceModeInfoIdx=999;
    CHECK(BuildVirtualPrimaryCandidate(invalid,1,primary,info)==ERROR_INVALID_DATA);
    invalid=before;
    invalid.modes[2].id=88;
    CHECK(BuildVirtualPrimaryCandidate(invalid,1,primary,info)==ERROR_INVALID_DATA);
    invalid=before;
    invalid.paths[1].sourceInfo.sourceModeInfoIdx=0;
    CHECK(BuildVirtualPrimaryCandidate(invalid,1,primary,info)==ERROR_INVALID_DATA);
    invalid=before;invalid.paths.resize(1);
    CHECK(BuildVirtualPrimaryCandidate(invalid,0,primary,info)==ERROR_INVALID_DATA);

    // Bounded repairs must be governed by the active mode's drift signal.
    SessionPolicy policy;
    Evidence evidence{};
    evidence.enabled=true;evidence.steamRunning=true;evidence.streamActive=true;
    evidence.virtualPresent=true;evidence.physicalPresent=true;
    evidence.stable=true;evidence.hasJournal=true;evidence.session=9;
    CHECK(policy.Decide(evidence)==Action::None); // both displays healthy
    evidence.needsRepair=true;
    CHECK(policy.Decide(evidence)==Action::Repair);
    evidence.needsRepair=false;
    CHECK(policy.Decide(evidence)==Action::None);
    return 0;
}

int main() {
    if(PolicyTests()!=0 || RestoreFallbackTests()!=0 || TailTests()!=0 || DiagnosticTests()!=0 || CandidateTests()!=0 ||
       StrategyTests()!=0 || VirtualPrimaryTests()!=0)return 1;
    std::cout<<"Native reliability, Steam tail, diagnostic rotation, CCD and strategy tests passed\n";
    return 0;
}
