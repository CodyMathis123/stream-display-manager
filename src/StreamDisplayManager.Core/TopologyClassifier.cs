namespace StreamDisplayManager.Core;

/// <summary>
/// Read-only reconciliation of active Windows desktop screens and display adapters.
/// Enumerated but unattached SudoVDA outputs are not active monitors.
/// </summary>
public static class TopologyClassifier
{
    public static TopologyAssessment Classify(
        IEnumerable<ObservedScreen> screens,
        IEnumerable<ObservedAdapter> adapters)
    {
        ObservedScreen[] desktop = screens
            .OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ObservedAdapter[] devices = adapters.ToArray();
        var adapterByName = devices
            .GroupBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var activeScreenNames = desktop
            .Select(x => x.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int physicalCount = 0;
        int virtualCount = 0;
        bool virtualIsPrimary = false;
        bool consistent = desktop.Length > 0 && desktop.Count(x => x.IsPrimary) == 1;
        var active = new List<ActiveScreen>();

        foreach (ObservedScreen screen in desktop)
        {
            if (!adapterByName.TryGetValue(screen.DeviceName, out ObservedAdapter? adapter)
                || !adapter.IsAttachedToDesktop)
            {
                // Screen and adapter enumeration can disagree during a topology change.
                consistent = false;
                active.Add(new ActiveScreen(screen.DeviceName, screen.IsPrimary,
                    screen.Width, screen.Height, "unknown"));
                continue;
            }

            string kind = adapter.IsSudoVdaCandidate ? "virtual-candidate" : "physical";
            if (adapter.IsSudoVdaCandidate)
            {
                virtualCount++;
                virtualIsPrimary |= screen.IsPrimary;
            }
            else
            {
                physicalCount++;
            }

            active.Add(new ActiveScreen(screen.DeviceName, screen.IsPrimary,
                screen.Width, screen.Height, kind));
        }

        if (devices.Any(adapter =>
            adapter.IsAttachedToDesktop && !activeScreenNames.Contains(adapter.DeviceName)))
        {
            consistent = false;
        }

        // Includes adapter-side activation state so transient adapter/screen mismatches
        // cannot accidentally be treated as an unchanged, stable topology.
        string signature = string.Join("|", active.Select(x =>
            $"{x.DeviceName}:{x.Width}x{x.Height}:{(x.IsPrimary ? 1 : 0)}:{x.Kind}"));
        string attachedAdapters = string.Join(",", devices
            .Where(x => x.IsAttachedToDesktop)
            .Select(x => x.DeviceName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        signature += "#attached=" + attachedAdapters;

        return new TopologyAssessment(
            consistent, physicalCount, virtualCount, virtualIsPrimary,
            active.ToArray(), signature);
    }
}

public sealed record ObservedScreen(
    string DeviceName, bool IsPrimary, int Width, int Height);

public sealed record ObservedAdapter(
    string DeviceName, bool IsSudoVdaCandidate, bool IsAttachedToDesktop);

public sealed record ActiveScreen(
    string DeviceName, bool IsPrimary, int Width, int Height, string Kind);

public sealed record TopologyAssessment(
    bool IsConsistent,
    int ActivePhysicalDisplays,
    int ActiveVirtualDisplays,
    bool VirtualIsPrimary,
    ActiveScreen[] Screens,
    string Signature);
