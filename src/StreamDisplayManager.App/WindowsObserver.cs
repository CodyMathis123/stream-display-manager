using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StreamDisplayManager.App;

/// <summary>
/// Phase 1 approximate desktop census. QueryDisplayConfig path identity and
/// WinEventHook are explicitly deferred until hardware traces validate them.
/// </summary>
internal static class WindowsObserver
{
    public static DesktopSnapshot Read()
    {
        var screens = Screen.AllScreens
            .Select(screen => new ScreenObservation(
                screen.DeviceName,
                screen.Primary,
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.WorkingArea.X,
                screen.WorkingArea.Y,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height))
            .OrderBy(s => s.DeviceName, StringComparer.Ordinal)
            .ToArray();

        var adapters = ReadAdapters();
        var windows = ReadWindows();

        var processes = Process.GetProcessesByName("steam");
        bool steamRunning = processes.Length > 0;
        foreach (Process process in processes)
            process.Dispose();

        return new DesktopSnapshot(screens, adapters, windows, steamRunning);
    }

    private static DisplayAdapterObservation[] ReadAdapters()
    {
        var adapters = new List<DisplayAdapterObservation>();
        for (uint index = 0; index < 32; index++)
        {
            DISPLAY_DEVICE device = new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
                break;

            string deviceName = device.DeviceName ?? "";
            string description = device.DeviceString ?? "";
            string deviceId = device.DeviceID ?? "";
            bool candidate =
                description.Contains("Sudo", StringComparison.OrdinalIgnoreCase) ||
                deviceId.Contains("SUDOVDA", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("Virtual Display Adapter", StringComparison.OrdinalIgnoreCase);

            adapters.Add(new DisplayAdapterObservation(
                deviceName, description, deviceId,
                device.StateFlags, candidate,
                (device.StateFlags & 0x00000001u) != 0));
        }

        return adapters.OrderBy(a => a.DeviceName, StringComparer.Ordinal).ToArray();
    }

    private static WindowObservation[] ReadWindows()
    {
        var windows = new List<WindowObservation>();
        var processCache = new Dictionary<uint, string>();

        EnumWindows((window, _) =>
        {
            if (windows.Count >= 500)
                return false;

            if (!IsWindowVisible(window) || !GetWindowRect(window, out RECT rect))
                return true;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 200 || height < 150)
                return true;

            GetWindowThreadProcessId(window, out uint pid);
            if (pid == 0)
                return true;

            if (!processCache.TryGetValue(pid, out string? processName))
            {
                try
                {
                    using Process process = Process.GetProcessById(checked((int)pid));
                    processName = process.ProcessName;
                }
                catch
                {
                    processName = "<unavailable>";
                }

                processCache[pid] = processName;
            }

            string displayName;
            try
            {
                displayName = Screen.FromHandle(window).DeviceName;
            }
            catch
            {
                displayName = "<unknown>";
            }

            windows.Add(new WindowObservation(
                window.ToInt64().ToString("X"),
                pid,
                processName,
                displayName,
                rect.Left, rect.Top, width, height,
                IsIconic(window)));

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(w => w.ProcessId)
            .ThenBy(w => w.WindowHandle, StringComparer.Ordinal)
            .ToArray();
    }

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}

internal sealed record DesktopSnapshot(
    ScreenObservation[] Screens,
    DisplayAdapterObservation[] Adapters,
    WindowObservation[] Windows,
    bool SteamRunning);

internal sealed record ScreenObservation(
    string DeviceName,
    bool Primary,
    int X, int Y, int Width, int Height,
    int WorkX, int WorkY, int WorkWidth, int WorkHeight);

internal sealed record DisplayAdapterObservation(
    string DeviceName, string Description, string DeviceId,
    uint StateFlags, bool IsSudoVdaCandidate, bool IsAttachedToDesktop);

internal sealed record WindowObservation(
    string WindowHandle, uint ProcessId, string ProcessName, string DeviceName,
    int X, int Y, int Width, int Height, bool IsMinimized);
