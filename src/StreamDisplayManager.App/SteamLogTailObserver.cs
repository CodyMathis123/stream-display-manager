using System.Text;
using Microsoft.Win32;
using StreamDisplayManager.Core;

namespace StreamDisplayManager.App;

/// <summary>
/// Read-only incremental reader for Steam's host streaming log.
/// It begins at EOF: historical sessions must never confirm a current session.
/// Supports shared open files, rotation/truncation, partial lines and bounded IO.
/// </summary>
internal sealed class SteamLogTailObserver
{
    private const int MaxBytesPerPoll = 256 * 1024;
    private readonly string _path;
    private DateTime _creationUtc;
    private long _position;
    private bool _initialized;
    private string _pending = "";

    private SteamLogTailObserver(string path) => _path = path;

    public static SteamLogTailObserver? TryCreate()
    {
        string? installPath = null;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            installPath = key?.GetValue("SteamPath") as string;
        }
        catch
        {
            // Registry is an optional discovery hint.
        }

        if (string.IsNullOrWhiteSpace(installPath))
            return null;

        string path = Path.Combine(installPath, "logs", "streaming_log.txt");
        if (!File.Exists(path))
            return null;

        var observer = new SteamLogTailObserver(path);
        observer.InitializeAtEnd();
        return observer;
    }

    private void InitializeAtEnd()
    {
        using FileStream stream = OpenShared();
        _position = stream.Length;
        _creationUtc = File.GetCreationTimeUtc(_path);
        _initialized = true;
    }

    public SteamLogBatch Poll()
    {
        if (!File.Exists(_path))
            return new SteamLogBatch([], true, "missing");

        using FileStream stream = OpenShared();
        DateTime creationUtc = File.GetCreationTimeUtc(_path);
        if (!_initialized)
        {
            _position = stream.Length;
            _creationUtc = creationUtc;
            _initialized = true;
            return new SteamLogBatch([], false, null);
        }

        bool changedFile = creationUtc != _creationUtc || stream.Length < _position;
        if (changedFile)
        {
            _position = 0;
            _pending = "";
            _creationUtc = creationUtc;
        }

        long unread = stream.Length - _position;
        if (unread == 0)
            return new SteamLogBatch([], changedFile, changedFile ? "rotated" : null);

        if (unread > MaxBytesPerPoll)
        {
            // Do not interpret the tail of a missing sequence as a complete session.
            _position = stream.Length;
            _pending = "";
            return new SteamLogBatch([], true, "read-limit");
        }

        byte[] bytes = new byte[checked((int)unread)];
        stream.Position = _position;
        stream.ReadExactly(bytes);
        _position = stream.Position;

        string data = _pending + Encoding.UTF8.GetString(bytes);
        string[] parts = data.Split('\n');
        _pending = parts[^1];

        // An oversized unterminated line is suspect. Discard rather than retain
        // arbitrary raw log text in memory indefinitely.
        if (_pending.Length > 16_384)
        {
            _pending = "";
            return new SteamLogBatch([], true, "oversize-line");
        }

        var events = new List<SteamSignal>();
        foreach (string line in parts.AsSpan(0, parts.Length - 1))
        {
            SteamSignal? signal = SteamSignalParser.Parse(line.TrimStart('\uFEFF'));
            if (signal is not null)
                events.Add(signal);
        }

        return new SteamLogBatch(events, changedFile,
            changedFile ? "rotated" : null);
    }

    private FileStream OpenShared() =>
        new(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
}

internal sealed record SteamLogBatch(
    IReadOnlyList<SteamSignal> Signals, bool HasGap, string? GapReason);
