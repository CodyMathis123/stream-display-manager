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

        if (observation.SteamAppId is not long appId || appId <= 0)
            return new(PlacementStatus.UnrecognizedGame,
                "No Steam App ID was confirmed for the session.",
                observation.SteamAppId, TargetDisplay: target.DeviceName);

        // Known games are matched by their observed executable name. For any
        // other installed Steam game, the app may supply live PIDs whose
        // executable is physically inside that App ID's Steam install folder.
        // Never guess using only window size or current desktop foreground.
        bool hasRule = ObservedGameRules.TryGetValue(appId, out string? expectedProcessName);
        bool hasInstallEvidence = observation.SteamInstalledGameProcessIds is { Count: > 0 };
        if (!hasRule && !hasInstallEvidence)
            return new(PlacementStatus.UnrecognizedGame,
                "No game process identity from a known rule or Steam installation.",
                appId, TargetDisplay: target.DeviceName);

        string evidence = hasRule ? "observed-appid-process-rule" : "steam-app-install-path";
        PlacementWindow[] matches = observation.Windows
            .Where(w => hasRule
                ? string.Equals(w.ProcessName, expectedProcessName,
                    StringComparison.OrdinalIgnoreCase)
                : observation.SteamInstalledGameProcessIds!.Contains(w.ProcessId))
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
        PlacementPlan WithIdentity(PlacementPlan decision) => decision with
        {
            WindowProcessId = window.ProcessId,
            WindowProcessName = window.ProcessName,
            IdentityEvidence = evidence
        };
        if (window.IsMinimized || window.Width < 640 || window.Height < 360 ||
            string.IsNullOrWhiteSpace(window.WindowHandle) || window.ProcessId == 0)
        {
            return WithIdentity(new(PlacementStatus.UnsafeWindow,
                "Candidate window is minimized, too small or missing identity.",
                appId, TargetDisplay: target.DeviceName));
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
            return WithIdentity(new(PlacementStatus.UnknownWindowLocation,
                "Game geometry and monitor attribution do not agree.",
                appId, WindowHandle: window.WindowHandle, TargetDisplay: target.DeviceName));
        }

        PlacementScreen current = containing[0];
        if (string.Equals(current.DeviceName, target.DeviceName,
            StringComparison.OrdinalIgnoreCase))
        {
            return WithIdentity(new(PlacementStatus.AlreadyOnTarget,
                "The game window is already on SudoVDA.",
                appId, window.WindowHandle, current.DeviceName, target.DeviceName));
        }

        if (!current.IsPhysical)
            return WithIdentity(new(PlacementStatus.UnknownWindowLocation,
                "Game window is not on a trusted physical source monitor.",
                appId, window.WindowHandle, current.DeviceName, target.DeviceName));

        return WithIdentity(new(PlacementStatus.WouldMove,
            "Game window is on the physical monitor while SudoVDA is active.",
            appId, window.WindowHandle, current.DeviceName, target.DeviceName));
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
    IReadOnlyList<PlacementScreen> Screens, IReadOnlyList<PlacementWindow> Windows,
    IReadOnlyCollection<uint>? SteamInstalledGameProcessIds = null);

public sealed record PlacementPlan(
    PlacementStatus Status, string Reason, long? AppId = null,
    string? WindowHandle = null, string? SourceDisplay = null,
    string? TargetDisplay = null)
{
    // The planner never invokes Win32. Only app-side code may execute a plan.
    public bool IsDryRun => true;
    public uint? WindowProcessId { get; init; }
    public string? WindowProcessName { get; init; }
    public string IdentityEvidence { get; init; } = "none";
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


// Generic auto-placement policy for confidently identified Steam game windows.
// One initial correction and one evidence-backed follow-up if a game resets
// its output or replaces its top-level HWND (observed with DOOM and RDR2).
// A successful native call plus immediate observed target location is valid
// evidence even if the game replaces that HWND before delayed verification.
public sealed class AutomaticPlacementPolicy
{
    public const int MaxAttemptsPerSession = 2;
    private long _session = -1;
    private long _appId = -1;
    private int _attempts;
    private string? _ownedWindow;
    private uint? _ownedPid;
    private bool _firstMoveVerified;
    private bool _firstMoveImmediatelyOnTarget;

    public AutoMoveDecision Observe(PlacementPlan plan, long sessionNumber)
    {
        if (sessionNumber <= 0 || plan.AppId is not long appId || appId <= 0)
            return new(false, "No confirmed Steam game/session.");

        if (_session != sessionNumber || _appId != appId)
        {
            _session = sessionNumber;
            _appId = appId;
            _attempts = 0;
            _ownedWindow = null;
            _ownedPid = null;
            _firstMoveVerified = false;
            _firstMoveImmediatelyOnTarget = false;
        }

        if (plan.Status == PlacementStatus.AlreadyOnTarget)
            return new(false, "Game is already on SudoVDA.");

        if (plan.Status != PlacementStatus.WouldMove ||
            string.IsNullOrWhiteSpace(plan.WindowHandle) ||
            plan.WindowProcessId is not uint pid || pid == 0 ||
            string.IsNullOrWhiteSpace(plan.WindowProcessName) ||
            plan.IdentityEvidence == "none")
            return new(false, "Game placement or process identity is not eligible.");

        if (_attempts == 0)
        {
            _attempts = 1;
            _ownedWindow = plan.WindowHandle;
            _ownedPid = pid;
            return new(true, "Game first observed on a physical monitor.", false, 1);
        }

        if (_attempts >= MaxAttemptsPerSession)
            return new(false, "Two corrections used for this Steam session.", true);

        if (_ownedPid != pid)
            return new(false, "Game PID changed; no retry on a different process.", true);

        if (!_firstMoveVerified && !_firstMoveImmediatelyOnTarget)
            return new(false, "First move never reached SudoVDA; no follow-up.", true);

        string previousHandle = _ownedWindow!;
        bool windowRecreated = !string.Equals(
            previousHandle, plan.WindowHandle, StringComparison.OrdinalIgnoreCase);
        _attempts = 2;
        _ownedWindow = plan.WindowHandle;
        return new(true, windowRecreated
            ? "Same game PID created a new HWND on physical; follow-up correction."
            : "Previously moved game returned to physical; follow-up correction.",
            false, 2, windowRecreated ? previousHandle : null);
    }

    // Validates immediate post-SetWindowPos geometry for the first attempt.
    // This enables a single follow-up if RDR2 replaces the original window
    // before any delayed snapshot can re-observe that HWND.
    public bool ConfirmImmediateMove(
        string windowHandle, uint processId, long appId, long sessionNumber,
        int attemptNumber, bool onTarget)
    {
        if (!onTarget || sessionNumber != _session || appId != _appId ||
            attemptNumber != 1 || _attempts != 1 || processId != _ownedPid ||
            !string.Equals(_ownedWindow, windowHandle, StringComparison.OrdinalIgnoreCase))
            return false;

        _firstMoveImmediatelyOnTarget = true;
        return true;
    }

    public bool ConfirmOwnedMove(
        string windowHandle, uint processId, long appId, long sessionNumber, int attemptNumber)
    {
        if (sessionNumber != _session || appId != _appId ||
            attemptNumber < 1 || attemptNumber != _attempts ||
            processId != _ownedPid ||
            !string.Equals(_ownedWindow, windowHandle, StringComparison.OrdinalIgnoreCase))
            return false;

        if (attemptNumber == 1)
            _firstMoveVerified = true;
        return true;
    }
}

public sealed record AutoMoveDecision(
    bool ShouldAttempt, string Reason, bool Suppressed = false,
    int AttemptNumber = 0, string? PreviousWindowHandle = null);
