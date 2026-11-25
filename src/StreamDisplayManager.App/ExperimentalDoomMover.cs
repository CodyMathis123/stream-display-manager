using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using StreamDisplayManager.Core;

namespace StreamDisplayManager.App;

/// <summary>
/// One-shot DOOM experiment: move and resize its existing top-level HWND to the
/// live SudoVDA bounds. Does not alter displays, force focus, change fullscreen
/// graphics settings or retry after a rejected Win32 call.
/// </summary>
internal static class ExperimentalDoomMover
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint PositionFlags = SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder;
    private const int GwlStyle = -16;

    public static ExperimentalMoveResult Attempt(PlacementPlan plan, DesktopSnapshot snapshot, JsonLog log)
    {
        var outcome = new ExperimentalMoveResult
        {
            WindowHandle = plan.WindowHandle ?? "",
            SourceDisplay = plan.SourceDisplay ?? "",
            TargetDisplay = plan.TargetDisplay ?? "",
            Status = "preflight-rejected"
        };

        ExperimentalMoveResult Reject(string reason)
        {
            var rejected = outcome with { Status = reason };
            log.Write("move_skipped", "doom-auto-preflight", rejected);
            return rejected;
        }

        // Revalidate live HWND and process just before touching Win32.
        if (plan.AppId != ExperimentalAutoMovePolicy.EnabledAppId ||
            plan.Status != PlacementStatus.WouldMove ||
            string.IsNullOrWhiteSpace(plan.WindowHandle) ||
            string.IsNullOrWhiteSpace(plan.SourceDisplay) ||
            string.IsNullOrWhiteSpace(plan.TargetDisplay))
            return Reject("Invalid experimental move plan.");

        WindowObservation[] candidates = snapshot.Windows.Where(w =>
            string.Equals(w.WindowHandle, plan.WindowHandle, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(w.ProcessName, "DOOMTheDarkAges", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length != 1 || candidates[0].ProcessId == 0 ||
            candidates[0].IsMinimized)
            return Reject("Expected exactly one live, nonminimized DOOM window.");

        WindowObservation observed = candidates[0];
        outcome = outcome with { ProcessId = observed.ProcessId };
        if (!long.TryParse(plan.WindowHandle, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out long numericHandle))
            return Reject("Unparseable HWND.");

        IntPtr hwnd = new(numericHandle);
        if (!IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
            return Reject("HWND is no longer visible and usable.");

        GetWindowThreadProcessId(hwnd, out uint livePid);
        if (livePid != observed.ProcessId)
            return Reject("HWND process identity changed since the snapshot.");

        DateTime? processStartUtc;
        try
        {
            using Process process = Process.GetProcessById(checked((int)livePid));
            if (!string.Equals(process.ProcessName, "DOOMTheDarkAges",
                    StringComparison.OrdinalIgnoreCase))
                return Reject("Live process is not DOOMTheDarkAges.");
            processStartUtc = process.StartTime.ToUniversalTime();
        }
        catch (Exception ex)
        {
            return Reject("Cannot verify game process: " + ex.GetType().Name);
        }

        Screen[] screens = Screen.AllScreens;
        Screen[] targets = screens.Where(s =>
            string.Equals(s.DeviceName, plan.TargetDisplay, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Screen[] sources = screens.Where(s =>
            string.Equals(s.DeviceName, plan.SourceDisplay, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (targets.Length != 1 || sources.Length != 1 ||
            string.Equals(plan.SourceDisplay, plan.TargetDisplay, StringComparison.OrdinalIgnoreCase))
            return Reject("Active display identity changed since the plan.");

        ScreenObservation[] observedTarget = snapshot.Screens.Where(s =>
            string.Equals(s.DeviceName, plan.TargetDisplay, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (observedTarget.Length != 1 ||
            targets[0].Bounds != new Rectangle(observedTarget[0].X, observedTarget[0].Y,
                observedTarget[0].Width, observedTarget[0].Height))
            return Reject("Virtual display geometry changed since the snapshot.");

        if (!string.Equals(Screen.FromHandle(hwnd).DeviceName, plan.SourceDisplay,
                StringComparison.OrdinalIgnoreCase))
            return Reject("Game is no longer on the expected source display.");

        if (!GetWindowRect(hwnd, out NativeRect nativeBefore))
            return Reject("GetWindowRect failed before moving.");

        Rectangle before = ToRectangle(nativeBefore);
        if (Math.Abs((long)before.X - observed.X) > 48 ||
            Math.Abs((long)before.Y - observed.Y) > 48 ||
            Math.Abs((long)before.Width - observed.Width) > 48 ||
            Math.Abs((long)before.Height - observed.Height) > 48)
            return Reject("Game geometry changed before the Win32 attempt.");

        Rectangle target = targets[0].Bounds;
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();

        log.Write("move_attempt", "doom-auto-setwindowpos", new
        {
            plan.AppId, plan.WindowHandle,
            processId = livePid,
            processStartUtc,
            sourceDisplay = plan.SourceDisplay,
            targetDisplay = plan.TargetDisplay,
            before,
            requested = target,
            windowStyle = $"0x{style:X}",
            flags = $"0x{PositionFlags:X}",
            method = "SetWindowPos",
            attemptLimit = "one-per-stream-session"
        });

        Stopwatch stopwatch = Stopwatch.StartNew();
        bool accepted = SetWindowPos(hwnd, IntPtr.Zero,
            target.X, target.Y, target.Width, target.Height, PositionFlags);
        int win32Error = accepted ? 0 : Marshal.GetLastWin32Error();
        stopwatch.Stop();

        Rectangle? after = GetWindowRect(hwnd, out NativeRect nativeAfter)
            ? ToRectangle(nativeAfter)
            : null;
        string? actualDisplay = IsWindow(hwnd) ? Screen.FromHandle(hwnd).DeviceName : null;

        var result = outcome with
        {
            Attempted = true,
            ApiSucceeded = accepted,
            Status = accepted ? "win32-call-accepted" : "win32-call-rejected",
            Before = before,
            Requested = target,
            After = after,
            AfterDisplay = actualDisplay,
            Win32Error = win32Error,
            DurationMs = stopwatch.ElapsedMilliseconds
        };
        log.Write("move_result", "doom-auto-setwindowpos", result);
        return result;
    }

    private static Rectangle ToRectangle(NativeRect r) =>
        new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

internal sealed record ExperimentalMoveResult
{
    public string WindowHandle { get; init; } = "";
    public uint ProcessId { get; init; }
    public string SourceDisplay { get; init; } = "";
    public string TargetDisplay { get; init; } = "";
    public bool Attempted { get; init; }
    public bool ApiSucceeded { get; init; }
    public string Status { get; init; } = "";
    public Rectangle? Before { get; init; }
    public Rectangle? Requested { get; init; }
    public Rectangle? After { get; init; }
    public string? AfterDisplay { get; init; }
    public int Win32Error { get; init; }
    public long DurationMs { get; init; }
}
