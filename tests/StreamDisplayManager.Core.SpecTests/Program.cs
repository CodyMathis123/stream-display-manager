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

Console.WriteLine($"{checks.Length + topologyChecks.Length - failures}/{checks.Length + topologyChecks.Length} total checks passed");
return failures == 0 ? 0 : 1;
