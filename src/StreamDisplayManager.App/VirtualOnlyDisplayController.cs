using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace StreamDisplayManager.App;

/// <summary>
/// Switches Windows CCD active paths, not game window coordinates. Never calls
/// SDC_SAVE_TO_DATABASE: an unclean reboot can revert the temporary topology.
/// </summary>
internal sealed class VirtualOnlyDisplayController
{
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint QdcVirtualModeAware = 0x00000010;
    private const uint SdcApply = 0x00000080;
    private const uint SdcUseSupplied = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint SdcVirtualModeAware = 0x00008000;
    private const uint SdcTopologyExtend = 0x00000004;
    private const uint PathActive = 0x00000001;
    private const uint GetSourceName = 1;
    private const int ErrorInsufficientBuffer = 122;
    private readonly string _journalPath;
    private readonly JsonLog _log;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private bool _switched;

    public VirtualOnlyDisplayController(JsonLog log)
    {
        _log = log;
        string dir = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "StreamDisplayManager");
        Directory.CreateDirectory(dir);
        _journalPath = Path.Combine(dir, "display-restore.json");
    }

    public bool IsActive => _switched;
    public bool HasJournal => File.Exists(_journalPath);

    public bool Switch(DesktopSnapshot snapshot, string reason)
    {
        if (_switched || HasJournal || DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromSeconds(30))
            return false;
        _lastAttempt = DateTimeOffset.UtcNow;

        // Screen -> CCD source mapping must agree with the observer. Never guess
        // which monitor to disable from an adapter index or "primary" flag.
        var virtualNames = snapshot.Adapters.Where(a => a.IsSudoVdaCandidate &&
            a.IsAttachedToDesktop).Select(a => a.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var virtualScreens = snapshot.Screens.Where(s => virtualNames.Contains(s.DeviceName)).ToArray();
        if (virtualScreens.Length != 1 || snapshot.Screens.Length < 2 ||
            snapshot.Screens.Any(s => s.Width <= 0 || s.Height <= 0))
        {
            _log.Write("display_switch", "preflight-rejected", new { reason, detail = "Need one SudoVDA and a physical screen." });
            return false;
        }

        try
        {
            Configuration before = Read();
            var virtualPaths = before.Paths.Where(p =>
                string.Equals(SourceName(p.Source), virtualScreens[0].DeviceName,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (virtualPaths.Length != 1 || before.Paths.Count(p => (p.Flags & PathActive) != 0) < 2)
                throw new InvalidOperationException("CCD source paths do not uniquely identify SudoVDA.");

            var intended = new Configuration(virtualPaths, before.Modes);
            uint flags = SdcUseSupplied | SdcVirtualModeAware | SdcAllowChanges;
            Check(SetDisplayConfig((uint)intended.Paths.Length, intended.Paths,
                (uint)intended.Modes.Length, intended.Modes, flags | SdcValidate), "Validate virtual-only topology");

            // A persisted journal is deliberately written BEFORE any mutation.
            // If the process terminates, restart or --restore can use it.
            string temporary = _journalPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(before, new JsonSerializerOptions { IncludeFields = true }));
            File.Move(temporary, _journalPath, overwrite: true);
            try
            {
                Check(SetDisplayConfig((uint)intended.Paths.Length, intended.Paths,
                    (uint)intended.Modes.Length, intended.Modes, flags | SdcApply), "Apply virtual-only topology");
                Configuration after = Read();
                if (after.Paths.Length != 1 ||
                    !string.Equals(SourceName(after.Paths[0].Source), virtualScreens[0].DeviceName,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Virtual-only topology was not confirmed.");
                _switched = true;
                _log.Write("display_switch", "virtual-only-active", new {
                    reason, virtualDisplay = virtualScreens[0].DeviceName,
                    previousActivePaths = before.Paths.Length,
                    journal = _journalPath
                });
                return true;
            }
            catch
            {
                Restore("switch-verification-failed");
                throw;
            }
        }
        catch (Exception ex)
        {
            _log.Write("display_switch", "failed", new { reason, error = ex.GetType().Name, ex.Message });
            return false;
        }
    }

    public bool Restore(string reason)
    {
        if (!HasJournal)
        {
            _switched = false;
            return true;
        }
        try
        {
            var before = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(_journalPath),
                new JsonSerializerOptions { IncludeFields = true })
                ?? throw new InvalidOperationException("Invalid restore journal.");
            Check(SetDisplayConfig((uint)before.Paths.Length, before.Paths,
                (uint)before.Modes.Length, before.Modes,
                SdcUseSupplied | SdcApply | SdcAllowChanges | SdcVirtualModeAware),
                "Restore original topology");
            // Require original source devices to be active again before removing recovery data.
            Configuration after = Read();
            string[] expected = before.Paths.Select(p => SourceName(p.Source))
                .OrderBy(x => x).ToArray();
            string[] actual = after.Paths.Select(p => SourceName(p.Source))
                .OrderBy(x => x).ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Original display paths not all restored.");
            File.Delete(_journalPath);
            _switched = false;
            _log.Write("display_restore", "restored", new { reason, activePaths = after.Paths.Length });
            return true;
        }
        catch (Exception ex)
        {
            _log.Write("display_restore", "failed", new { reason, error = ex.GetType().Name, ex.Message });
            return false;
        }
    }

    public bool RecoverOnStartup()
    {
        if (!HasJournal)
            return true;
        // If reboot already restored physical displays, do not reapply stale CCD
        // mode indices; discard the journal only if a physical desktop exists.
        try
        {
            var desktop = WindowsObserver.Read();
            bool physicalActive = desktop.Screens.Any(s =>
                !desktop.Adapters.Any(a => a.IsSudoVdaCandidate &&
                    string.Equals(a.DeviceName, s.DeviceName, StringComparison.OrdinalIgnoreCase)));
            if (physicalActive)
            {
                File.Delete(_journalPath);
                _switched = false;
                _log.Write("display_restore", "physical-display-already-active", new { });
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Write("display_restore", "recovery-preflight-failed", new { ex.Message });
        }
        return Restore("startup-journal");
    }

    public bool EmergencyExtend()
    {
        // A last-resort manual recovery path when saved mode indices are stale.
        // It intentionally does NOT delete the journal on failure.
        int result = SetDisplayConfig(0, null, 0, null, SdcTopologyExtend | SdcApply);
        _log.Write("display_restore", "emergency-extend", new { result });
        return result == 0;
    }

    private static Configuration Read()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint flags = QdcOnlyActivePaths | QdcVirtualModeAware;
            Check(GetDisplayConfigBufferSizes(flags, out uint pc, out uint mc), "Get CCD buffer sizes");
            var paths = new DisplayPath[pc];
            var modes = Enumerable.Range(0, (int)mc).Select(_ => new DisplayMode { Data = new byte[48] }).ToArray();
            int result = QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (result == ErrorInsufficientBuffer) continue;
            Check(result, "Query active CCD paths");
            return new Configuration(paths.Take((int)pc).ToArray(), modes.Take((int)mc).ToArray());
        }
        throw new Win32Exception(ErrorInsufficientBuffer, "Display configuration changed during read.");
    }

    private static string SourceName(DisplaySource source)
    {
        var name = new SourceDeviceName {
            Header = new DeviceInfoHeader { Type = GetSourceName,
                Size = (uint)Marshal.SizeOf<SourceDeviceName>(),
                AdapterId = source.AdapterId, Id = source.Id },
            ViewGdiDeviceName = ""
        };
        Check(DisplayConfigGetDeviceInfo(ref name), "Get CCD source name");
        return name.ViewGdiDeviceName.TrimEnd('\0');
    }

    private static void Check(int result, string operation)
    {
        if (result != 0) throw new Win32Exception(result, operation);
    }

    internal sealed record Configuration(DisplayPath[] Paths, DisplayMode[] Modes);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplaySource {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayTarget {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayPath {
        public DisplaySource Source;
        public DisplayTarget Target;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayMode {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] Data;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount,
        [Out] DisplayPath[] paths, ref uint modeCount, [Out] DisplayMode[] modes, IntPtr currentTopology);
    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint pathCount, [In] DisplayPath[]? paths,
        uint modeCount, [In] DisplayMode[]? modes, uint flags);
    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName name);
}
