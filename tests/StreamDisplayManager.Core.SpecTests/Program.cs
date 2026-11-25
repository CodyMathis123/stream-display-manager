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

Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed");
return failures == 0 ? 0 : 1;
