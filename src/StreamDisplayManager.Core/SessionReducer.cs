namespace StreamDisplayManager.Core;

/// <summary>
/// A pure, side-effect-free interpretation of session evidence.
/// A candidate session never authorizes moving windows.
/// </summary>
public static class SessionReducer
{
    public static SessionDecision Evaluate(
        SessionPhase previous,
        SessionEvidence evidence,
        bool hasOwnedMoves)
    {
        if (evidence.SessionEndConfirmed || !evidence.VirtualDisplayPresent)
        {
            if (hasOwnedMoves)
            {
                return new SessionDecision(
                    SessionPhase.Restoring, false,
                    evidence.SessionEndConfirmed
                        ? "Confirmed end; owned windows require safe restoration."
                        : "Virtual target disappeared; owned windows require safe restoration.");
            }

            return new SessionDecision(
                SessionPhase.Desktop, false,
                "No active virtual streaming target and no owned moves.");
        }

        if (!evidence.SteamRunning)
        {
            return new SessionDecision(
                SessionPhase.Candidate, false,
                "Virtual display exists, but Steam is not running.");
        }

        if (!evidence.SessionSignalConfirmed)
        {
            return new SessionDecision(
                SessionPhase.Candidate, false,
                "Steam and virtual display observed; stream not confirmed.");
        }

        if (!evidence.TargetDisplayStable)
        {
            return new SessionDecision(
                SessionPhase.Preparing, false,
                "Streaming confirmed, awaiting stable target display.");
        }

        if (previous is SessionPhase.RecoveryRequired)
        {
            return new SessionDecision(
                SessionPhase.RecoveryRequired, false,
                "RecoveryRequired must be explicitly reconciled.");
        }

        return new SessionDecision(
            SessionPhase.Streaming, true,
            "Steam session confirmed; stable virtual target available.");
    }
}

public enum SessionPhase
{
    Desktop,
    Candidate,
    Preparing,
    Streaming,
    Restoring,
    RecoveryRequired
}

public sealed record SessionEvidence(
    bool SteamRunning,
    bool VirtualDisplayPresent,
    bool SessionSignalConfirmed,
    bool SessionEndConfirmed = false,
    bool TargetDisplayStable = false);

public sealed record SessionDecision(
    SessionPhase NextPhase,
    bool MayPlanWindowMoves,
    string Reason);
