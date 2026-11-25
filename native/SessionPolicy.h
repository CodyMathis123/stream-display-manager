#pragma once
#include <cstdint>
#include <optional>

namespace sdm {

// Pure decisions: all clocks are monotonic milliseconds (GetTickCount64).
// A missing SudoVDA or lost Steam evidence restores immediately; a confirmed
// normal stream end gets a short reconnect window while SudoVDA still exists.
enum class Action { None, Switch, Restore, Repair };

// The database fallback is attempted only when the saved topology could not
// be applied AND verified. Never call it after successful physical recovery.
template<class ApplySaved, class ApplyDatabase, class CheckPhysical>
bool RestoreWithFallback(ApplySaved&& saved, ApplyDatabase&& database,
                         CheckPhysical&& physical) {
    if(saved() && physical()) return true;
    return database() && physical();
}
struct Evidence {
    bool enabled=false;
    bool steamRunning=false;
    bool streamActive=false;
    bool endObserved=false;
    bool virtualPresent=false;
    bool physicalPresent=false;
    bool stable=false;
    bool hasJournal=false;
    std::uint64_t session=0;
    std::uint64_t now=0;
    bool needsRepair=false; // supplied by the active strategy, not assumed from physical presence
};
class SessionPolicy {
public:
    static constexpr std::uint64_t ReconnectMs=12000;
    static constexpr std::uint64_t RepairCooldownMs=15000;
    static constexpr unsigned MaxRepairsPerSession=2;

    Action Decide(const Evidence& e) {
        if (!e.hasJournal) {
            pendingEnd_.reset();
            repairs_=0;
            if (e.enabled && e.steamRunning && e.streamActive && !e.endObserved &&
                e.virtualPresent && e.physicalPresent && e.stable &&
                attemptedSession_!=e.session) {
                attemptedSession_=e.session;
                return Action::Switch;
            }
            return Action::None;
        }
        if (!e.enabled || !e.steamRunning || !e.virtualPresent)
            return Action::Restore;

        if (e.endObserved && !e.streamActive) {
            if (!pendingEnd_) pendingEnd_=e.now;
            return e.now-*pendingEnd_>=ReconnectMs ? Action::Restore : Action::None;
        }
        // Uncertain session evidence (log disappeared/rotated) must not keep
        // the physical display disabled; grace is only for an explicit end.
        if (!e.streamActive) return Action::Restore;
        pendingEnd_.reset();

        if (repairSession_!=e.session) {
            repairSession_=e.session;
            repairs_=0;
            lastRepair_.reset();
        }
        if (e.needsRepair && e.stable && repairs_<MaxRepairsPerSession &&
            (!lastRepair_ || e.now-*lastRepair_>=RepairCooldownMs)) {
            lastRepair_=e.now;
            ++repairs_;
            return Action::Repair;
        }
        return Action::None;
    }

    void OnRecovery() {
        pendingEnd_.reset();
        repairs_=0;
    }
private:
    std::uint64_t attemptedSession_=UINT64_MAX;
    std::uint64_t repairSession_=UINT64_MAX;
    unsigned repairs_=0;
    std::optional<std::uint64_t> pendingEnd_;
    std::optional<std::uint64_t> lastRepair_;
};

} // namespace sdm
