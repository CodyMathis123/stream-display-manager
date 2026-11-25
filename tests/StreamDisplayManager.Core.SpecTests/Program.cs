using StreamDisplayManager.Core;

var checks = new (string Name, SessionDecision Actual, SessionPhase Expected, bool MayMove)[]
{
    ("Steam alone cannot move", SessionReducer.Evaluate(
        SessionPhase.Desktop, new SessionEvidence(true, false, false), false),
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
        SessionPhase.Streaming, new SessionEvidence(true, false, false), true),
        SessionPhase.Restoring, false),

    ("Disconnected target with no owned moves is desktop", SessionReducer.Evaluate(
        SessionPhase.Streaming, new SessionEvidence(true, false, false), false),
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
return failures == 0 ? 0 : 1;
