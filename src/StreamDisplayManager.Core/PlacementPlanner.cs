namespace StreamDisplayManager.Core;

/// <summary>
/// Read-only Phase 2 placement assessment. This is NOT an authorization to
/// move a real HWND. DeviceName is a session-local diagnostic label only.
/// </summary>
public static class PlacementPlanner
{
    // Observed on this host: Steam App ID and exact process name.
    // These are diagnostic rules, not an authoritative PID/launch ownership
    // proof. An eventual executor must require stronger identity validation.
    private static readonly Dictionary<long, string> ObservedGameRules = new()
    {
        [1716740] = "Starfield",
        [1174180] = "RDR2",
        [3017860] = "DOOMTheDarkAges"
    };

    public static PlacementPlan Evaluate(PlacementObservation observation)
    {
        if (!observation.SessionConfirmed || !observation.TopologyStable)
            return new(PlacementStatus.Hold, "Session or desktop topology not confirmed.");

        PlacementScreen[] targets = observation.Screens.Where(x => x.IsVirtualCandidate).ToArray();
        if (targets.Length != 1 || observation.Screens.Count(x => x.IsPhysical) < 1)
            return new(PlacementStatus.AmbiguousTarget,
                "Need exactly one active virtual target and at least one physical monitor.",
                observation.SteamAppId);

        PlacementScreen target = targets[0];
        if (string.IsNullOrWhiteSpace(target.DeviceName) || target.Width <= 0 || target.Height <= 0)
            return new(PlacementStatus.AmbiguousTarget, "Target geometry is invalid.",
                observation.SteamAppId);

        if (observation.SteamAppId is not long appId ||
            !ObservedGameRules.TryGetValue(appId, out string? expectedProcessName))
        {
            return new(PlacementStatus.UnrecognizedGame,
                "No verified diagnostic process rule for the Steam App ID.",
                observation.SteamAppId, TargetDisplay: target.DeviceName);
        }

        PlacementWindow[] matches = observation.Windows
            .Where(w => string.Equals(w.ProcessName, expectedProcessName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return new(PlacementStatus.GameWindowNotFound,
                "No top-level window from the expected game process was observed.",
                appId, TargetDisplay: target.DeviceName);

        if (matches.Length != 1)
            return new(PlacementStatus.AmbiguousGameWindow,
                "Multiple matching windows; cannot infer the game main window.",
                appId, TargetDisplay: target.DeviceName);

        PlacementWindow window = matches[0];
        if (window.IsMinimized || window.Width < 640 || window.Height < 360 ||
            string.IsNullOrWhiteSpace(window.WindowHandle) || window.ProcessId == 0)
        {
            return new(PlacementStatus.UnsafeWindow,
                "Candidate window is minimized, too small or missing identity.",
                appId, TargetDisplay: target.DeviceName);
        }

        // Screen.FromHandle may temporarily identify the *nearest* screen.
        // Require the game's center to be within one and only one live monitor
        // AND agree with the Windows observer's monitor attribution.
        long cx = (long)window.X + window.Width / 2;
        long cy = (long)window.Y + window.Height / 2;
        PlacementScreen[] containing = observation.Screens
            .Where(s => s.Width > 0 && s.Height > 0 &&
                cx >= s.X && cx < (long)s.X + s.Width &&
                cy >= s.Y && cy < (long)s.Y + s.Height)
            .ToArray();

        if (containing.Length != 1 ||
            !string.Equals(containing[0].DeviceName, window.DeviceName,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(PlacementStatus.UnknownWindowLocation,
                "Game geometry and monitor attribution do not agree.",
                appId, WindowHandle: window.WindowHandle, TargetDisplay: target.DeviceName);
        }

        PlacementScreen current = containing[0];
        if (string.Equals(current.DeviceName, target.DeviceName,
            StringComparison.OrdinalIgnoreCase))
        {
            return new(PlacementStatus.AlreadyOnTarget,
                "Steam already placed the observed game window on SudoVDA.",
                appId, window.WindowHandle, current.DeviceName, target.DeviceName);
        }

        if (!current.IsPhysical)
            return new(PlacementStatus.UnknownWindowLocation,
                "Game window is not on a trusted physical source monitor.",
                appId, window.WindowHandle, current.DeviceName, target.DeviceName);

        return new(PlacementStatus.WouldMove,
            "Eligible-looking game window is on the physical monitor.",
            appId, window.WindowHandle, current.DeviceName, target.DeviceName);
    }
}

public enum PlacementStatus
{
    Hold,
    AmbiguousTarget,
    UnrecognizedGame,
    GameWindowNotFound,
    AmbiguousGameWindow,
    UnsafeWindow,
    UnknownWindowLocation,
    AlreadyOnTarget,
    WouldMove
}

public sealed record PlacementScreen(
    string DeviceName, int X, int Y, int Width, int Height,
    bool IsVirtualCandidate, bool IsPhysical);

public sealed record PlacementWindow(
    string WindowHandle, uint ProcessId, string ProcessName,
    string DeviceName, int X, int Y, int Width, int Height, bool IsMinimized);

public sealed record PlacementObservation(
    bool SessionConfirmed, bool TopologyStable, long? SteamAppId,
    IReadOnlyList<PlacementScreen> Screens, IReadOnlyList<PlacementWindow> Windows);

public sealed record PlacementPlan(
    PlacementStatus Status, string Reason, long? AppId = null,
    string? WindowHandle = null, string? SourceDisplay = null,
    string? TargetDisplay = null)
{
    public bool IsDryRun => true;
}

/// <summary>
/// Two distinct observations at least 750ms apart are required to publish
/// a placement plan. This suppresses Windows attribution during topology churn.
/// All plans are informational and have no executor.
/// </summary>
public sealed class PlacementPlanStabilizer(TimeSpan minimumDuration)
{
    private string? _candidate;
    private DateTimeOffset _since;
    private string? _published;

    public PlacementPlan? Observe(PlacementPlan plan, DateTimeOffset observedAtUtc)
    {
        string identity = string.Join("|", plan.Status, plan.AppId, plan.WindowHandle,
            plan.SourceDisplay, plan.TargetDisplay);

        if (_candidate != identity)
        {
            _candidate = identity;
            _since = observedAtUtc;
        }

        if (observedAtUtc - _since < minimumDuration || _published == identity)
            return null;

        _published = identity;
        return plan;
    }

    public void Reset()
    {
        _candidate = null;
        _published = null;
    }
}


// DOOM experimental automation gate. DOOM (3017860) only; one attempt per
// streaming session, and never fight a window already seen on virtual display.
public sealed class ExperimentalAutoMovePolicy
{
    public const long EnabledAppId = 3017860;
    private long _session = -1;
    private bool _attempted;
    private readonly HashSet<string> _previouslyOnTarget = new(StringComparer.OrdinalIgnoreCase);

    public AutoMoveDecision Observe(PlacementPlan plan, long sessionNumber)
    {
        if (sessionNumber <= 0 || plan.AppId != EnabledAppId ||
            string.IsNullOrWhiteSpace(plan.WindowHandle))
            return new(false, "Not the enabled game or missing identity.");

        if (_session != sessionNumber)
        {
            _session = sessionNumber;
            _attempted = false;
            _previouslyOnTarget.Clear();
        }

        if (plan.Status == PlacementStatus.AlreadyOnTarget)
        {
            _previouslyOnTarget.Add(plan.WindowHandle);
            return new(false, "Previously on streaming target; respect subsequent user moves.");
        }

        if (plan.Status != PlacementStatus.WouldMove)
            return new(false, "Planner did not find a misplaced window.");

        if (_previouslyOnTarget.Contains(plan.WindowHandle))
            return new(false, "User may have moved the window; do not fight that.", true);

        if (_attempted)
            return new(false, "One attempted move per session; no retries.", true);

        _attempted = true; // Mark before native API call.
        return new(true, "DOOM first observed on the physical monitor.");
    }
}

public sealed record AutoMoveDecision(bool ShouldAttempt, string Reason, bool Suppressed = false);
