using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StreamDisplayManager.App;

/// <summary>
/// Resolves a Steam App ID to its installed game directory. Only top-level
/// windows owned by an executable within that directory are considered for
/// unrecognized games. Never move a process merely because it has focus.
/// Install paths and command lines are never written to diagnostic logs.
/// </summary>
internal sealed class SteamGameProcessLocator
{
    private static readonly HashSet<string> ExcludedExecutableNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "gameoverlayui", "SocialClubHelper",
        "RockstarLauncher", "Launcher", "CrashReporter", "CrashHandler",
        "UnityCrashHandler64", "EADesktop", "EABackgroundService",
        "EpicGamesLauncher", "UbisoftConnect", "unins000", "dxsetup"
    };
    private long? _cachedAppId;
    private string? _cachedInstallFolder;
    private DateTimeOffset _lastResolution;

    public SteamGameIdentityScan Inspect(long? steamAppId, DesktopSnapshot snapshot)
    {
        if (steamAppId is not long appId || appId <= 0)
            return new([], "no-appid", 0);

        if (_cachedAppId != appId ||
            DateTimeOffset.UtcNow - _lastResolution > TimeSpan.FromMinutes(2))
        {
            _cachedAppId = appId;
            _cachedInstallFolder = FindSteamGameFolder(appId);
            _lastResolution = DateTimeOffset.UtcNow;
        }

        if (string.IsNullOrWhiteSpace(_cachedInstallFolder))
            return new([], "steam-install-not-resolved", 0);

        string prefix = Path.TrimEndingDirectorySeparator(_cachedInstallFolder)
            + Path.DirectorySeparatorChar;
        var matches = new List<uint>();
        int accessFailures = 0;

        foreach (uint pid in snapshot.Windows.Select(w => w.ProcessId)
            .Where(p => p != 0).Distinct())
        {
            try
            {
                using Process process = Process.GetProcessById(checked((int)pid));
                if (ExcludedExecutableNames.Contains(process.ProcessName))
                    continue;

                string? exe = process.MainModule?.FileName;
                if (exe is not null && Path.GetFullPath(exe)
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    matches.Add(pid);
            }
            catch
            {
                // A protected or rapidly exiting process cannot establish
                // game identity. Do not fall back to a guess.
                accessFailures++;
            }
        }

        return new(matches, matches.Count > 0
            ? "steam-install-executable-match" : "no-matching-game-window",
            accessFailures);
    }

    private static string? FindSteamGameFolder(long appId)
    {
        foreach (string libraryRoot in FindLibraryRoots())
        {
            try
            {
                string manifestPath = Path.Combine(libraryRoot, "steamapps",
                    $"appmanifest_{appId}.acf");
                if (!File.Exists(manifestPath))
                    continue;

                string data = File.ReadAllText(manifestPath);
                Match match = Regex.Match(data,
                    "\"installdir\"\\s*\"(?<dir>[^\"]+)\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
                if (!match.Success)
                    continue;

                string common = Path.GetFullPath(Path.Combine(
                    libraryRoot, "steamapps", "common")) + Path.DirectorySeparatorChar;
                string game = Path.GetFullPath(Path.Combine(common,
                    UnescapeVdfPath(match.Groups["dir"].Value)));
                if (Directory.Exists(game) &&
                    (game + Path.DirectorySeparatorChar).StartsWith(
                        common, StringComparison.OrdinalIgnoreCase))
                    return game;
            }
            catch
            {
                // Missing/malformed library is not grounds to move arbitrary windows.
            }
        }

        return null;
    }

    private static IEnumerable<string> FindLibraryRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string? registryPath = Registry.CurrentUser
                .OpenSubKey(@"Software\Valve\Steam")?
                .GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(registryPath))
                roots.Add(Path.GetFullPath(registryPath));
        }
        catch { }

        try
        {
            foreach (Process steam in Process.GetProcessesByName("steam"))
            {
                using (steam)
                {
                    string? exe = steam.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(exe))
                    {
                        string? parent = Path.GetDirectoryName(exe);
                        if (parent is not null)
                            roots.Add(Path.GetFullPath(parent));
                    }
                }
            }
        }
        catch { }

        string? conventional = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(conventional))
            roots.Add(Path.Combine(conventional, "Steam"));

        foreach (string steamRoot in roots.ToArray())
        {
            string libraries = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraries))
                continue;

            try
            {
                string content = File.ReadAllText(libraries);
                foreach (Match match in Regex.Matches(content,
                    "\"path\"\\s*\"(?<path>[^\"]+)\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)))
                {
                    string raw = UnescapeVdfPath(match.Groups["path"].Value);
                    if (Path.IsPathFullyQualified(raw))
                        roots.Add(Path.GetFullPath(raw));
                }
            }
            catch { }
        }

        return roots;
    }

    private static string UnescapeVdfPath(string path) =>
        path.Replace(@"\\", @"\");
}

internal sealed record SteamGameIdentityScan(
    IReadOnlyCollection<uint> ProcessIds, string Status, int AccessFailures);
