using System.Text.Json;

namespace StreamDisplayManager.App;

/// <summary>
/// Bounded local JSONL diagnostics: max 5 files/day x 10 MiB, 7-day retention.
/// No window titles, arguments, keystrokes or network traffic are collected.
/// </summary>
internal sealed class JsonLog
{
    private const int MaxSegmentsPerDay = 5;
    private const long MaxSegmentBytes = 10L * 1024 * 1024;
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public JsonLog(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        DeleteExpiredFiles();
    }

    public void Write(string type, string reason, object? payload)
    {
        try
        {
            string day = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string line = JsonSerializer.Serialize(new
            {
                version = 1,
                atUtc = DateTimeOffset.UtcNow,
                type,
                reason,
                payload
            }, _jsonOptions) + Environment.NewLine;

            for (int segment = 0; segment < MaxSegmentsPerDay; segment++)
            {
                string path = Path.Combine(_directory,
                    $"observer-{day}-{segment:D2}.jsonl");

                long existingBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (existingBytes + System.Text.Encoding.UTF8.GetByteCount(line) > MaxSegmentBytes)
                    continue;

                File.AppendAllText(path, line);
                return;
            }

            // Hard cap reached; don't grow indefinitely.
        }
        catch
        {
            // Diagnostic IO errors must not affect desktop stability.
        }
    }

    private void DeleteExpiredFiles()
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (string path in Directory.EnumerateFiles(_directory, "observer-*.jsonl"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                    File.Delete(path);
            }
            catch
            {
                // Best-effort retention only.
            }
        }
    }
}
