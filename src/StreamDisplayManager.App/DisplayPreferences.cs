using System.Text.Json;

namespace StreamDisplayManager.App;

/// <summary>Per-user preferences stored outside the MSI installation directory.</summary>
internal sealed class DisplayPreferences
{
    private readonly string _path;
    private readonly JsonLog _log;

    public DisplayPreferences(JsonLog log)
    {
        _log = log;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamDisplayManager");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public bool LoadVirtualOnly()
    {
        try
        {
            if (!File.Exists(_path))
                return false;
            Settings? settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path));
            return settings?.VirtualOnlyDuringStreaming ?? false;
        }
        catch (Exception ex)
        {
            _log.Write("settings", "read-failed-default-window-placement",
                new { error = ex.GetType().Name, ex.Message });
            return false;
        }
    }

    public void SaveVirtualOnly(bool enabled)
    {
        try
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Settings(enabled)));
            File.Move(temp, _path, overwrite: true);
            _log.Write("settings", "saved", new { virtualOnlyDuringStreaming = enabled });
        }
        catch (Exception ex)
        {
            _log.Write("settings", "write-failed",
                new { error = ex.GetType().Name, ex.Message });
        }
    }

    private sealed record Settings(bool VirtualOnlyDuringStreaming);
}
