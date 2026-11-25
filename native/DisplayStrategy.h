#pragma once
#include <string>
#include <string_view>

namespace sdm {
// Exactly one streaming strategy is active. Disabled avoids Steam observation
// entirely; Observe continues diagnostics but never mutates the desktop.
enum class DisplayStrategy {
    Disabled,
    Observe,
    MoveToVirtual,
    VirtualPrimary,
    VirtualOnly
};
inline const char* StrategyName(DisplayStrategy strategy) {
    switch(strategy) {
    case DisplayStrategy::Disabled:return "disabled";
    case DisplayStrategy::Observe:return "observe";
    case DisplayStrategy::MoveToVirtual:return "virtual-move";
    case DisplayStrategy::VirtualPrimary:return "virtual-primary";
    case DisplayStrategy::VirtualOnly:return "virtual-only";
    }
    return "disabled";
}
inline bool IsTopologyStrategy(DisplayStrategy strategy) {
    return strategy==DisplayStrategy::VirtualPrimary ||
           strategy==DisplayStrategy::VirtualOnly;
}
inline DisplayStrategy ParseStrategy(std::string_view value,
                                    DisplayStrategy fallback=DisplayStrategy::Observe) {
    if(value=="disabled")return DisplayStrategy::Disabled;
    if(value=="observe")return DisplayStrategy::Observe;
    if(value=="virtual-move")return DisplayStrategy::MoveToVirtual;
    if(value=="virtual-primary")return DisplayStrategy::VirtualPrimary;
    if(value=="virtual-only")return DisplayStrategy::VirtualOnly;
    return fallback;
}
} // namespace sdm
