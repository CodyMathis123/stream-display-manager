namespace StreamDisplayManager.Core;

/// <summary>
/// Steam's host-side streaming_log.txt is an undocumented diagnostic log.
/// Interpret only signatures observed in the 2026-11-25 host log. Never infer a
/// live stream from "Streaming initialized", video capture switches, Steam.exe,
/// or "Game Recording - game stopped".
/// </summary>
public static class SteamSignalParser
{
    public static SteamSignal? Parse(string line)
    {
        int headerEnd = line.IndexOf("] ", StringComparison.Ordinal);
        if (headerEnd < 0 || !line.StartsWith("[", StringComparison.Ordinal))
            return null;

        string message = line[(headerEnd + 2)..].TrimEnd('\r');
        const string start = "Streaming started to ";
        if (message.StartsWith(start, StringComparison.Ordinal))
        {
            string endpoint = message[start.Length..];
            int separator = endpoint.IndexOf(" at ", StringComparison.Ordinal);
            if (separator <= 0)
                return null;

            // Never retain the client's address, port or arbitrary device name.
            string clientKind = endpoint[..separator].Equals(
                "steamdeck", StringComparison.OrdinalIgnoreCase)
                ? "steamdeck" : "other";
            return new SteamSignal(SteamSignalKind.Started, null, clientKind);
        }

        if (message.Equals("Encoding complete", StringComparison.Ordinal))
            return new SteamSignal(SteamSignalKind.Completed);

        if (message.StartsWith("Game: ", StringComparison.Ordinal) &&
            message.EndsWith(')'))
        {
            int idStart = message.LastIndexOf('(');
            if (idStart > 6 &&
                long.TryParse(message.AsSpan(idStart + 1, message.Length - idStart - 2),
                    out long appId) &&
                appId > 0)
                return new SteamSignal(SteamSignalKind.GameSelected, appId);
        }

        return null;
    }
}

public enum SteamSignalKind { Started, GameSelected, Completed }

/// <summary>Contains no raw Steam log lines, client IPs, ports or game titles.</summary>
public sealed record SteamSignal(
    SteamSignalKind Kind, long? AppId = null, string? ClientKind = null);

/// <summary>
/// Tracks *observed* starts/ends only; beginning to watch mid-stream is unknown.
/// A truncated or unrecognized log resets any prior confirmation.
/// </summary>
public sealed class SteamEvidenceState
{
    public bool IsActive { get; private set; }
    public bool EndObserved { get; private set; }
    public long? AppId { get; private set; }
    public long SessionNumber { get; private set; }
    public string? ClientKind { get; private set; }

    public void Observe(SteamSignal signal)
    {
        switch (signal.Kind)
        {
            case SteamSignalKind.Started:
                IsActive = true;
                EndObserved = false;
                AppId = null;
                ClientKind = signal.ClientKind;
                SessionNumber++;
                break;
            case SteamSignalKind.GameSelected when IsActive:
                AppId = signal.AppId;
                break;
            case SteamSignalKind.Completed:
                IsActive = false;
                EndObserved = true;
                AppId = null;
                ClientKind = null;
                break;
        }
    }

    public void Invalidate()
    {
        IsActive = false;
        EndObserved = false;
        AppId = null;
        ClientKind = null;
    }

    // After a stable return to physical desktop we cannot safely retain a
    // previous start marker if a new virtual display later appears.
    public void ResetAfterDetachedVirtualDisplay()
    {
        if (IsActive)
            Invalidate();
    }
}
