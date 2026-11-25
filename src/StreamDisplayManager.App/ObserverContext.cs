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
    private readonly SteamEvidenceState _steamEvidence = new();
    private readonly SteamLogTailObserver? _steamLog;
    private SessionPhase _sessionPhase = SessionPhase.Desktop;
    private string? _lastSteamLogGap;
    private string? _previousSerializedSnapshot;
    private readonly TopologyStabilizer _topologyStabilizer =
        new(TimeSpan.FromMilliseconds(750));
    private bool _closed;

    public ObserverContext()
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamDisplayManager", "logs");

        _log = new JsonLog(logDirectory);

        // Start at EOF: historical Steam sessions are never evidence of a live stream.
        try
        {
            _steamLog = SteamLogTailObserver.TryCreate();
            _log.Write("steam_log_source", _steamLog is null ? "unavailable" : "attached",
                new { observer = "streaming_log.txt", historyRead = false });
        }
        catch (Exception ex)
        {
            _log.Write("error", "steam-log-initialization", new
            {
                error = ex.GetType().Name
            });
        }

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
            ReadSteamEvidence();
            DesktopSnapshot snapshot = WindowsObserver.Read();
            string serialized = JsonSerializer.Serialize(snapshot);
            bool changed = !string.Equals(_previousSerializedSnapshot, serialized, StringComparison.Ordinal);

            // Continue observing topology even if every screen/window field is unchanged.
            // Otherwise the 3-second polling interval can never confirm a stable topology.
            if (changed || reason != "poll")
            {
                _previousSerializedSnapshot = serialized;
                _log.Write("system_snapshot", reason, snapshot);
            }

            // Installed virtual outputs may be enumerated even when none are attached.
            // Do not imply an active streaming display just because the driver exists.
            var topology = TopologyClassifier.Classify(
                snapshot.Screens.Select(screen =>
                    new ObservedScreen(screen.DeviceName, screen.Primary, screen.Width, screen.Height)),
                snapshot.Adapters.Select(adapter =>
                    new ObservedAdapter(adapter.DeviceName, adapter.IsSudoVdaCandidate,
                        adapter.IsAttachedToDesktop)));

            // A device notification can precede Windows updating its desktop screen list.
            // Do not label a topology stable until the SAME consistent state persists.
            StableTopologyTransition? transition =
                _topologyStabilizer.Observe(topology, DateTimeOffset.UtcNow);

            if (transition is not null)
            {
                _log.Write("topology_stable", "reconciled", new
                {
                    transition.Sequence,
                    transition.PreviousSignature,
                    topology.IsConsistent,
                    topology.ActivePhysicalDisplays,
                    topology.ActiveVirtualDisplays,
                    topology.VirtualIsPrimary,
                    topology.Screens,
                    topology.Signature
                });
            }

            bool stable = _topologyStabilizer.IsStable(topology);
            bool virtualActive = topology.IsConsistent && topology.ActiveVirtualDisplays > 0;

            // Correlate explicit host-log starts with a stable SudoVDA desktop
            // before reporting Streaming. This remains strictly observational.
            SessionDecision decision = SessionReducer.Evaluate(
                _sessionPhase,
                new SessionEvidence(
                    snapshot.SteamRunning,
                    virtualActive,
                    _steamEvidence.IsActive,
                    _steamEvidence.EndObserved,
                    stable),
                hasOwnedMoves: false);

            if (decision.NextPhase != _sessionPhase)
            {
                _log.Write("session_transition", "steam-display-correlation", new
                {
                    previous = _sessionPhase.ToString(),
                    next = decision.NextPhase.ToString(),
                    decision.Reason,
                    decision.MayPlanWindowMoves,
                    steamStartObserved = _steamEvidence.IsActive,
                    steamEndObserved = _steamEvidence.EndObserved,
                    _steamEvidence.SessionNumber,
                    _steamEvidence.AppId,
                    topologyStable = stable,
                    virtualActive
                });
                _sessionPhase = decision.NextPhase;
            }

            // Stale start markers must not authorize a later virtual display
            // appearing after we observed the physical desktop return.
            if (_sessionPhase == SessionPhase.Desktop && stable && !virtualActive)
                _steamEvidence.ResetAfterDetachedVirtualDisplay();

            _tray.Text = !stable
                ? "Stream Display Manager: topology changing"
                : _sessionPhase == SessionPhase.Streaming
                    ? "Stream Display Manager: stream confirmed (read only)"
                    : virtualActive
                        ? "Stream Display Manager: virtual display candidate"
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

    private void ReadSteamEvidence()
    {
        if (_steamLog is null)
            return;

        try
        {
            SteamLogBatch batch = _steamLog.Poll();
            if (batch.HasGap)
            {
                _steamEvidence.Invalidate();
                if (_lastSteamLogGap != batch.GapReason)
                {
                    _log.Write("steam_log_gap", batch.GapReason ?? "unknown", new
                    {
                        message = "Session confidence reset; old log history is not replayed."
                    });
                }
                _lastSteamLogGap = batch.GapReason;
            }
            else
            {
                _lastSteamLogGap = null;
            }

            foreach (SteamSignal signal in batch.Signals)
            {
                _steamEvidence.Observe(signal);
                _log.Write("steam_signal", signal.Kind.ToString(), new
                {
                    _steamEvidence.SessionNumber,
                    signal.AppId,
                    signal.ClientKind,
                    _steamEvidence.IsActive,
                    _steamEvidence.EndObserved
                });
            }
        }
        catch (Exception ex)
        {
            _steamEvidence.Invalidate();
            _log.Write("error", "steam-log-read-failed", new
            {
                error = ex.GetType().Name
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
