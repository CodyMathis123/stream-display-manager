#pragma once
#include <string>
#include <filesystem>
#include "SteamTail.h"

namespace sdm {
using EventSink = void(*)(const std::string& type,const std::string& reason,
                           const std::string& details);
std::filesystem::path SteamLogPath();
bool SteamRunning();
class SteamObserver {
public:
    bool streamActive=false;
    bool endObserved=false;
    long long session=0,appId=0;
    // Emits only sanitized events. A reattached log starts from EOF.
    void Poll(EventSink emit);
private:
    SteamTail tail_;
    std::filesystem::path steamlog_;
    bool missingLogReported_=false;
    void ParseSignal(const std::string& line,EventSink emit);
};
} // namespace sdm
