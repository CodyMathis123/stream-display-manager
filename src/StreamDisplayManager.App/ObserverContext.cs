using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using StreamDisplayManager.Core;

namespace StreamDisplayManager.App;

/// <summary>Read-only Phase 1 diagnostic tray app. Does not mutate windows or displays.</summary>
internal sealed class ObserverContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _scanTimer;
    private readonly MessageWindow _messageWindow;
    private readonly JsonLog _log;
    private string? _previousSerializedSnapshot;
    private string? _pendingTopologySignature;
    private DateTimeOffset _pendingTopologySince;
    private string? _lastStableTopologySignature;
    private bool _closed;

    public ObserverContext()
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamDisplayManager", "logs");

        _log = new JsonLog(logDirectory);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture snapshot", null, (_, _) => Capture("manual"));
        menu.Items.Add("Open diagnostics folder", null, (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = logDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _log.Write("error", "open-log-directory", new { error = ex.GetType().Name, ex.Message });
            }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Stream Display Manager (observe only)",
            ContextMenuStrip = menu,
            Visible = true
        };

        _messageWindow = new MessageWindow(reason => Capture(reason));

        _scanTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _scanTimer.Tick += (_, _) => Capture("poll");
        _scanTimer.Start();

        _log.Write("lifecycle", "started", new
        {
            mode = "read-only",
            schema = 1,
            logDirectory
        });

        Capture("startup");
    }

    private void Capture(string reason)
    {
        if (_closed)
            return;

        try
        {
            DesktopSnapshot snapshot = WindowsObserver.Read();
            string serialized = JsonSerializer.Serialize(snapshot);
            bool changed = !string.Equals(_previousSerializedSnapshot, serialized, StringComparison.Ordinal);

            if (!changed && reason == "poll")
                return;

            _previousSerializedSnapshot = serialized;
            _log.Write("system_snapshot", reason, snapshot);

            // Installed virtual outputs may be enumerated even when none are attached.
            // Do not imply an active streaming display just because the driver exists.
            var topology = TopologyClassifier.Classify(
                snapshot.Screens.Select(screen =>
                    new ObservedScreen(screen.DeviceName, screen.Primary, screen.Width, screen.Height)),
                snapshot.Adapters.Select(adapter =>
                    new ObservedAdapter(adapter.DeviceName, adapter.IsSudoVdaCandidate,
                        adapter.IsAttachedToDesktop)));

            // A device notification can precede Windows updating its desktop screen list.
            // Changes need to be consistent and stable before we trust the topology.
            var now = DateTimeOffset.UtcNow;
            if (_pendingTopologySignature != topology.Signature)
            {
                _pendingTopologySignature = topology.Signature;
                _pendingTopologySince = now;
            }

            if (topology.IsConsistent &&
                now - _pendingTopologySince >= TimeSpan.FromMilliseconds(750) &&
                _lastStableTopologySignature != topology.Signature)
            {
                _lastStableTopologySignature = topology.Signature;
                _log.Write("topology_stable", "reconciled", new
                {
                    topology.IsConsistent,
                    topology.ActivePhysicalDisplays,
                    topology.ActiveVirtualDisplays,
                    topology.VirtualIsPrimary,
                    topology.Screens,
                    topology.Signature
                });
            }

            _tray.Text = !topology.IsConsistent
                ? "Stream Display Manager: topology changing"
                : topology.ActiveVirtualDisplays > 0
                    ? "Stream Display Manager: active virtual display"
                    : "Stream Display Manager: desktop observer";
        }
        catch (Exception ex)
        {
            _log.Write("error", "snapshot-failed", new
            {
                error = ex.GetType().Name,
                ex.Message
            });
        }
    }

    protected override void ExitThreadCore()
    {
        if (_closed)
            return;

        _log.Write("lifecycle", "stopped", new { mode = "read-only" });
        _closed = true;

        _scanTimer.Stop();
        _scanTimer.Dispose();
        _messageWindow.DestroyHandle();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();

        base.ExitThreadCore();
    }

    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action<string> _notify;

        public MessageWindow(Action<string> notify)
        {
            _notify = notify;
            CreateHandle(new CreateParams { Caption = "StreamDisplayManagerObserver" });
        }

        protected override void WndProc(ref Message m)
        {
            // Signals initiate a fresh census, not a trusted state transition.
            switch (m.Msg)
            {
                case 0x007E: // WM_DISPLAYCHANGE
                    _notify("WM_DISPLAYCHANGE");
                    break;
                case 0x0219: // WM_DEVICECHANGE
                    _notify("WM_DEVICECHANGE");
                    break;
                case 0x0218: // WM_POWERBROADCAST
                    _notify("WM_POWERBROADCAST");
                    break;
            }

            base.WndProc(ref m);
        }
    }
}
