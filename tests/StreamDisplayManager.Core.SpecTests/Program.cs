using StreamDisplayManager.Core;

var checks = new (string Name, SessionDecision Actual, SessionPhase Expected, bool MayMove)[]
{
    ("Steam alone cannot move", SessionReducer.Evaluate(
        SessionPhase.Desktop, new SessionEvidence(true, false, false, TargetDisplayStable: true), false),
        SessionPhase.Desktop, false),

    ("Uncorroborated virtual target cannot move", SessionReducer.Evaluate(
        SessionPhase.Desktop, new SessionEvidence(true, true, false), false),
        SessionPhase.Candidate, false),

    ("Stable but unconfirmed stream cannot move", SessionReducer.Evaluate(
        SessionPhase.Candidate, new SessionEvidence(true, true, false, TargetDisplayStable: true), false),
        SessionPhase.Candidate, false),

    ("Confirmed unstable target must wait", SessionReducer.Evaluate(
        SessionPhase.Candidate, new SessionEvidence(true, true, true, TargetDisplayStable: false), false),
        SessionPhase.Preparing, false),

    ("Confirmed stable target may plan", SessionReducer.Evaluate(
        SessionPhase.Preparing, new SessionEvidence(true, true, true, TargetDisplayStable: true), false),
        SessionPhase.Streaming, true),

    ("Disconnected target restores owned moves", SessionReducer.Evaluate(
        SessionPhase.Streaming, new SessionEvidence(true, false, false, TargetDisplayStable: true), true),
        SessionPhase.Restoring, false),

    ("Disconnected target with no owned moves is desktop", SessionReducer.Evaluate(
        SessionPhase.Streaming, new SessionEvidence(true, false, false, TargetDisplayStable: true), false),
        SessionPhase.Desktop, false),

    ("Recovery required is sticky", SessionReducer.Evaluate(
        SessionPhase.RecoveryRequired, new SessionEvidence(true, true, true, TargetDisplayStable: true), true),
        SessionPhase.RecoveryRequired, false)
};

int failures = 0;
foreach (var check in checks)
{
    bool passed = check.Actual.NextPhase == check.Expected &&
        check.Actual.MayPlanWindowMoves == check.MayMove;

    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {check.Name}");
    if (!passed)
        failures++;
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} session checks passed");

var desktopScreen = new ObservedScreen(@"\\.\DISPLAY1", true, 3440, 1440);
var desktopAdapter = new ObservedAdapter(@"\\.\DISPLAY1", false, true);
var virtualAdapter = new ObservedAdapter(@"\\.\DISPLAY6", true, false);
var activeVirtualAdapter = virtualAdapter with { IsAttachedToDesktop = true };
var deckScreen = new ObservedScreen(@"\\.\DISPLAY6", true, 1280, 800);
var secondaryDesktop = desktopScreen with { IsPrimary = false };
var topologyChecks = new (string Name, TopologyAssessment Result,
    bool ExpectedConsistent, int ExpectedVirtual, bool ExpectedVirtualPrimary)[]
{
    ("Installed SudoVDA is not an active screen",
        TopologyClassifier.Classify(new[] { desktopScreen },
            new[] { desktopAdapter, virtualAdapter }),
        true, 0, false),
    ("Attached SudoVDA before screen enumeration is transient",
        TopologyClassifier.Classify(new[] { desktopScreen },
            new[] { desktopAdapter, activeVirtualAdapter }),
        false, 0, false),
    ("Deck virtual primary is a consistent topology",
        TopologyClassifier.Classify(new[] { secondaryDesktop, deckScreen },
            new[] { desktopAdapter, activeVirtualAdapter }),
        true, 1, true),
    ("Stale desktop screen after driver detach is transient",
        TopologyClassifier.Classify(new[] { secondaryDesktop, deckScreen },
            new[] { desktopAdapter, virtualAdapter }),
        false, 0, false),
    ("Physical monitor is primary after disconnect",
        TopologyClassifier.Classify(new[] { desktopScreen },
            new[] { desktopAdapter, virtualAdapter }),
        true, 0, false)
};

foreach (var check in topologyChecks)
{
    bool passed = check.Result.IsConsistent == check.ExpectedConsistent &&
        check.Result.ActiveVirtualDisplays == check.ExpectedVirtual &&
        check.Result.VirtualIsPrimary == check.ExpectedVirtualPrimary;

    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {check.Name}");
    if (!passed)
        failures++;
}

Console.WriteLine($"{checks.Length + topologyChecks.Length - failures}/{checks.Length + topologyChecks.Length} initial checks passed");

// This sequence matches the observed undocked Deck lifecycle:
// ultrawide desktop -> transient attach -> virtual primary -> transient detach ->
// desktop -> reconnect to the *same* DISPLAY6 signature -> stream exit.
int stabilityChecks = 0;
void VerifyStability(string name, bool condition)
{
    stabilityChecks++;
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition)
        failures++;
}

var stabilizer = new TopologyStabilizer(TimeSpan.FromMilliseconds(750));
var start = new DateTimeOffset(2026, 11, 25, 14, 10, 27, TimeSpan.Zero);

var desktop = TopologyClassifier.Classify(
    new[] { desktopScreen }, new[] { desktopAdapter, virtualAdapter });
var virtualPrimary = TopologyClassifier.Classify(
    new[] { secondaryDesktop, deckScreen },
    new[] { desktopAdapter, activeVirtualAdapter });
var attaching = TopologyClassifier.Classify(
    new[] { desktopScreen }, new[] { desktopAdapter, activeVirtualAdapter });
var detaching = TopologyClassifier.Classify(
    new[] { secondaryDesktop, deckScreen }, new[] { desktopAdapter, virtualAdapter });

VerifyStability("Startup topology waits for debounce",
    stabilizer.Observe(desktop, start) is null);
var firstDesktop = stabilizer.Observe(desktop, start.AddSeconds(1));
VerifyStability("Desktop stabilizes without changing snapshot contents",
    firstDesktop?.Sequence == 1 && stabilizer.IsStable(desktop));
VerifyStability("Installed SudoVDA never authorizes a virtual state",
    firstDesktop?.Topology.ActiveVirtualDisplays == 0);

VerifyStability("Attaching driver creates no stable event",
    stabilizer.Observe(attaching, start.AddSeconds(2)) is null &&
    !stabilizer.IsStable(attaching));
VerifyStability("First virtual observation is pending",
    stabilizer.Observe(virtualPrimary, start.AddSeconds(2.2)) is null);
var firstVirtual = stabilizer.Observe(virtualPrimary, start.AddSeconds(3.1));
VerifyStability("First virtual topology stabilizes",
    firstVirtual?.Sequence == 2 && firstVirtual.Topology.VirtualIsPrimary);

VerifyStability("Transient detach cannot publish a display state",
    stabilizer.Observe(detaching, start.AddSeconds(30)) is null &&
    !stabilizer.IsStable(detaching));
VerifyStability("Desktop after disconnect initially pending",
    stabilizer.Observe(desktop, start.AddSeconds(30.2)) is null);
var recoveredDesktop = stabilizer.Observe(desktop, start.AddSeconds(31.2));
VerifyStability("Desktop restored while game may remain running",
    recoveredDesktop?.Sequence == 3 &&
    recoveredDesktop.PreviousSignature == virtualPrimary.Signature);

VerifyStability("Reconnect with identical virtual display is pending",
    stabilizer.Observe(virtualPrimary, start.AddSeconds(38)) is null);
var reconnected = stabilizer.Observe(virtualPrimary, start.AddSeconds(39));
VerifyStability("Reconnect publishes another virtual transition",
    reconnected?.Sequence == 4 &&
    reconnected.Topology.Signature == firstVirtual?.Topology.Signature);
VerifyStability("Unchanged virtual observations produce no duplicate events",
    stabilizer.Observe(virtualPrimary, start.AddSeconds(42)) is null);

var recoveredAgain = stabilizer.Observe(desktop, start.AddSeconds(50));
var finalDesktop = stabilizer.Observe(desktop, start.AddSeconds(51));
VerifyStability("Final disconnect publishes physical desktop",
    recoveredAgain is null && finalDesktop?.Sequence == 5);

Console.WriteLine($"{checks.Length + topologyChecks.Length + stabilityChecks - failures}/{checks.Length + topologyChecks.Length + stabilityChecks} total checks passed");
// Sanitized Steam host-log lines shaped like the user's two real files.
// These tests guard against interpreting game/capture events as stream events.
int steamChecks = 0;
void VerifySteam(string name, bool passed)
{
    steamChecks++;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
    if (!passed)
        failures++;
}

// Regression from the MSI-observer trace: a display change after a
// confirmed Desktop must not reopen the old disconnect.
void VerifyStableDesktop(string name, SessionDecision actual, SessionPhase expected)
{
    bool pass = actual.NextPhase == expected && !actual.MayPlanWindowMoves;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures++;
}
VerifyStableDesktop("Old end marker does not re-enter Disconnecting",
    SessionReducer.Evaluate(SessionPhase.Desktop,
        new SessionEvidence(true, false, false, SessionEndConfirmed: true, TargetDisplayStable: false),
        false),
    SessionPhase.Desktop);
VerifyStableDesktop("New virtual candidate cannot reuse old stream end",
    SessionReducer.Evaluate(SessionPhase.Desktop,
        new SessionEvidence(true, true, false, SessionEndConfirmed: true, TargetDisplayStable: false),
        false),
    SessionPhase.Candidate);

// The host's Encoding complete marker precedes virtual-display removal.
void VerifyDisconnect(string name, SessionDecision decision, SessionPhase expected)
{
    bool pass = decision.NextPhase == expected && !decision.MayPlanWindowMoves;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures++;
}
VerifyDisconnect("Steam complete while SudoVDA present",
    SessionReducer.Evaluate(SessionPhase.Streaming,
        new SessionEvidence(true, true, false, SessionEndConfirmed: true, TargetDisplayStable: true), false),
    SessionPhase.Disconnecting);
VerifyDisconnect("Unstable detach is not yet Desktop",
    SessionReducer.Evaluate(SessionPhase.Disconnecting,
        new SessionEvidence(true, false, false, SessionEndConfirmed: true, TargetDisplayStable: false), false),
    SessionPhase.Disconnecting);
VerifyDisconnect("Stable physical topology completes disconnect",
    SessionReducer.Evaluate(SessionPhase.Disconnecting,
        new SessionEvidence(true, false, false, SessionEndConfirmed: true, TargetDisplayStable: true), false),
    SessionPhase.Desktop);
VerifyDisconnect("Owned-window restore delayed until physical desktop is stable",
    SessionReducer.Evaluate(SessionPhase.Disconnecting,
        new SessionEvidence(true, false, false, SessionEndConfirmed: true, TargetDisplayStable: true), true),
    SessionPhase.Restoring);

const string prefix = "[2026-11-25 09:10:39][208376.813953] ";
var startSignal = SteamSignalParser.Parse(
    prefix + "Streaming started to steamdeck at 192.0.2.10:55568, audio channels = 2, MTU = 1468");
VerifySteam("Explicit host streaming start is recognized",
    startSignal?.Kind == SteamSignalKind.Started &&
    startSignal.ClientKind == "steamdeck" && startSignal.AppId is null);

var gameSignal = SteamSignalParser.Parse(
    prefix + "Game: Starfield (1716740)");
VerifySteam("Steam app id is extracted without storing game name",
    gameSignal?.Kind == SteamSignalKind.GameSelected &&
    gameSignal.AppId == 1716740 &&
    gameSignal.ClientKind is null);

var endSignal = SteamSignalParser.Parse(
    prefix + "Encoding complete");
VerifySteam("Encoding completion is recognized",
    endSignal?.Kind == SteamSignalKind.Completed);

VerifySteam("Listener initialization is not a connected session",
    SteamSignalParser.Parse(prefix + "Streaming initialized and listening on port 27031") is null);
VerifySteam("Desktop video switch is not a disconnected session",
    SteamSignalParser.Parse(prefix + ">>> Stopped desktop stream") is null);
VerifySteam("Game recording stop is not a stream end",
    SteamSignalParser.Parse(prefix + "Game Recording - game stopped [gameid=1716740]") is null);
VerifySteam("Lost video capture is not a stream end",
    SteamSignalParser.Parse(prefix + "Lost Video Stream") is null);
VerifySteam("Arbitrary log text containing start words is rejected",
    SteamSignalParser.Parse(prefix + "CLIENT: Streaming started to steamdeck at fake") is null);

var steamState = new SteamEvidenceState();
VerifySteam("Observer starts unknown even if old log contains sessions",
    !steamState.IsActive && !steamState.EndObserved && steamState.SessionNumber == 0);
if (startSignal is not null)
    steamState.Observe(startSignal);
if (gameSignal is not null)
    steamState.Observe(gameSignal);
VerifySteam("Start then game attribution creates an active session",
    steamState.IsActive && !steamState.EndObserved &&
    steamState.AppId == 1716740 && steamState.SessionNumber == 1);
if (endSignal is not null)
    steamState.Observe(endSignal);
VerifySteam("Disconnect ends the session without implying game exit",
    !steamState.IsActive && steamState.EndObserved && steamState.AppId is null);
if (startSignal is not null)
    steamState.Observe(startSignal);
VerifySteam("Reconnect creates another distinct active session",
    steamState.IsActive && !steamState.EndObserved && steamState.SessionNumber == 2);
steamState.ResetAfterDetachedVirtualDisplay();
VerifySteam("Detached physical desktop invalidates stale starts",
    !steamState.IsActive && !steamState.EndObserved && steamState.SessionNumber == 2);
VerifySteam("Session reducer requires corroborating display evidence",
    !SessionReducer.Evaluate(SessionPhase.Candidate,
        new SessionEvidence(true, false, steamState.IsActive), false).MayPlanWindowMoves);

// Phase 2A/B: dry-run-only placement decisions. The observed Starfield
// Steam App ID/process rule is diagnostic; a future executor needs stronger
// process-instance ownership and fullscreen/foreground validation.
int placementChecks = 0;
void VerifyPlacement(string name, PlacementStatus actual, PlacementStatus expected)
{
    placementChecks++;
    bool pass = actual == expected;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures++;
}

var virtualScreen = new PlacementScreen(@"\\.\DISPLAY6", 0, 0, 1280, 800,
    IsVirtualCandidate: true, IsPhysical: false);
var physicalScreen = new PlacementScreen(@"\\.\DISPLAY1", -3440, 0, 3440, 1440,
    IsVirtualCandidate: false, IsPhysical: true);
var deckWindow = new PlacementWindow("ABC1", 1500, "Starfield",
    virtualScreen.DeviceName, 0, 0, 1280, 800, false);
var desktopWindow = deckWindow with
{
    DeviceName = physicalScreen.DeviceName, X = -3440, Y = 0,
    Width = 3440, Height = 1440
};

PlacementObservation PlacementInput(
    PlacementWindow[] windows, bool confirmed = true, bool stable = true,
    long? appId = 1716740, PlacementScreen[]? screens = null) =>
    new(confirmed, stable, appId,
        screens ?? new[] { physicalScreen, virtualScreen }, windows);

VerifyPlacement("No stream: no placement evaluation",
    PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow }, confirmed: false)).Status,
    PlacementStatus.Hold);
VerifyPlacement("Unstable topology: no placement evaluation",
    PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow }, stable: false)).Status,
    PlacementStatus.Hold);
VerifyPlacement("Steam already placed Starfield on virtual display",
    PlacementPlanner.Evaluate(PlacementInput(new[] { deckWindow })).Status,
    PlacementStatus.AlreadyOnTarget);
VerifyPlacement("Physical-monitor Starfield is a dry-run candidate",
    PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow })).Status,
    PlacementStatus.WouldMove);
VerifyPlacement("Unknown Steam App ID cannot claim a game process",
    PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow }, appId: 1234567)).Status,
    PlacementStatus.UnrecognizedGame);
VerifyPlacement("No game window: hold",
    PlacementPlanner.Evaluate(PlacementInput([])).Status,
    PlacementStatus.GameWindowNotFound);
VerifyPlacement("Ambiguous game windows: hold",
    PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow, deckWindow })).Status,
    PlacementStatus.AmbiguousGameWindow);
VerifyPlacement("Minimized game window: hold",
    PlacementPlanner.Evaluate(PlacementInput(new[] {
        desktopWindow with { IsMinimized = true } })).Status,
    PlacementStatus.UnsafeWindow);
VerifyPlacement("Small dialog is not a game main window",
    PlacementPlanner.Evaluate(PlacementInput(new[] {
        desktopWindow with { Width = 320, Height = 240 } })).Status,
    PlacementStatus.UnsafeWindow);
VerifyPlacement("Window attribution conflict: hold",
    PlacementPlanner.Evaluate(PlacementInput(new[] {
        desktopWindow with { DeviceName = virtualScreen.DeviceName } })).Status,
    PlacementStatus.UnknownWindowLocation);
VerifyPlacement("Overlapping monitors cannot give a unique window source",
    PlacementPlanner.Evaluate(PlacementInput(new[] { deckWindow },
        screens: new[] { physicalScreen with { X = 0 }, virtualScreen })).Status,
    PlacementStatus.UnknownWindowLocation);
VerifyPlacement("Ambiguous virtual targets: hold",
    PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow },
        screens: new[] { physicalScreen, virtualScreen,
            virtualScreen with { DeviceName = @"\\.\DISPLAY7" } })).Status,
    PlacementStatus.AmbiguousTarget);

// DOOM and RDR2 diagnostic replay from Nov 25 host snapshots.
var doomPhysicalScreen = physicalScreen with { X = -2560, Width = 2560 };
var doomPhysicalWindow = new PlacementWindow("1F0B62", 22301, "DOOMTheDarkAges",
    doomPhysicalScreen.DeviceName, -2560, 0, 2560, 1440, false);
var doomVirtualWindow = doomPhysicalWindow with {
    DeviceName = virtualScreen.DeviceName, X = 0, Y = 0, Width = 1280, Height = 800 };
VerifyPlacement("DOOM physical window would move",
    PlacementPlanner.Evaluate(PlacementInput(new[] { doomPhysicalWindow },
        appId: 3017860, screens: new[] { doomPhysicalScreen, virtualScreen })).Status,
    PlacementStatus.WouldMove);
VerifyPlacement("DOOM game-selected monitor is virtual",
    PlacementPlanner.Evaluate(PlacementInput(new[] { doomVirtualWindow },
        appId: 3017860)).Status, PlacementStatus.AlreadyOnTarget);
VerifyPlacement("Wrong process never matches DOOM",
    PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow },
        appId: 3017860)).Status, PlacementStatus.GameWindowNotFound);
var rdr2Window = deckWindow with { ProcessName = "RDR2", WindowHandle = "6009C4" };
var rockstarLauncher = desktopWindow with { ProcessName = "SocialClubHelper", WindowHandle = "80A56" };
VerifyPlacement("RDR2 actual window on SudoVDA",
    PlacementPlanner.Evaluate(PlacementInput(new[] { rdr2Window, rockstarLauncher },
        appId: 1174180)).Status, PlacementStatus.AlreadyOnTarget);
VerifyPlacement("Rockstar launcher is not RDR2",
    PlacementPlanner.Evaluate(PlacementInput(new[] { rockstarLauncher },
        appId: 1174180)).Status, PlacementStatus.GameWindowNotFound);
var candidatePlan = PlacementPlanner.Evaluate(PlacementInput(new[] { desktopWindow }));
var noOpPlan = PlacementPlanner.Evaluate(PlacementInput(new[] { deckWindow }));
var placementStabilizer = new PlacementPlanStabilizer(TimeSpan.FromMilliseconds(750));
DateTimeOffset tick = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);
placementChecks++;
bool initialWait = placementStabilizer.Observe(candidatePlan, tick) is null;
Console.WriteLine($"{(initialWait ? "PASS" : "FAIL")} Plan waits for repeat observation");
if (!initialWait) failures++;
placementChecks++;
bool emittedOnce =
    placementStabilizer.Observe(candidatePlan, tick.AddSeconds(1))?.Status == PlacementStatus.WouldMove &&
    placementStabilizer.Observe(candidatePlan, tick.AddSeconds(2)) is null;
Console.WriteLine($"{(emittedOnce ? "PASS" : "FAIL")} Stable dry-run plan emits once");
if (!emittedOnce) failures++;
placementChecks++;
bool changedPlanPending =
    placementStabilizer.Observe(noOpPlan, tick.AddSeconds(3)) is null &&
    placementStabilizer.Observe(noOpPlan, tick.AddSeconds(4))?.Status == PlacementStatus.AlreadyOnTarget;
Console.WriteLine($"{(changedPlanPending ? "PASS" : "FAIL")} Correct placement supersedes stale plan");
if (!changedPlanPending) failures++;
placementStabilizer.Reset();
placementChecks++;
bool reconnectResets = placementStabilizer.Observe(candidatePlan, tick.AddSeconds(5)) is null;
Console.WriteLine($"{(reconnectResets ? "PASS" : "FAIL")} Reconnect resets placement candidate");
if (!reconnectResets) failures++;

Console.WriteLine($"{checks.Length + topologyChecks.Length + stabilityChecks + steamChecks + 6 + placementChecks - failures}/{checks.Length + topologyChecks.Length + stabilityChecks + steamChecks + 6 + placementChecks} total checks passed");

return failures == 0 ? 0 : 1;
