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
        if (previous == SessionPhase.RecoveryRequired)
            return new SessionDecision(SessionPhase.RecoveryRequired, false,
                "RecoveryRequired must be explicitly reconciled.");

        // Steam completes the stream before SudoVDA finishes detaching.
        // Don't initiate window restoration or advertise Desktop until the
        // real physical desktop is stable.
        if (evidence.SessionEndConfirmed)
        {
            if (evidence.VirtualDisplayPresent || !evidence.TargetDisplayStable)
                return new SessionDecision(SessionPhase.Disconnecting, false,
                    "Steam completed; waiting for physical desktop to stabilize.");

            return hasOwnedMoves
                ? new SessionDecision(SessionPhase.Restoring, false,
                    "Confirmed end; stable physical desktop; restore owned windows.")
                : new SessionDecision(SessionPhase.Desktop, false,
                    "Confirmed end; physical desktop restored.");
        }

        if (!evidence.VirtualDisplayPresent)
        {
            if ((previous == SessionPhase.Streaming ||
                 previous == SessionPhase.Disconnecting) &&
                !evidence.TargetDisplayStable)
                return new SessionDecision(SessionPhase.Disconnecting, false,
                    "Virtual target disappeared; waiting for physical desktop to stabilize.");

            return hasOwnedMoves
                ? new SessionDecision(SessionPhase.Restoring, false,
                    "Virtual target removed; owned windows require validated restoration.")
                : new SessionDecision(SessionPhase.Desktop, false,
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
    Disconnecting,
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
