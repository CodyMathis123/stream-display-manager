using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using StreamDisplayManager.Core;

namespace StreamDisplayManager.App;

/// <summary>Steam/SudoVDA observer with identified-game automatic placement experiments.</summary>
internal sealed class ObserverContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private readonly System.Windows.Forms.Timer _scanTimer;
    private readonly MessageWindow _messageWindow;
    private readonly JsonLog _log;
    private readonly SteamEvidenceState _steamEvidence = new();
    private readonly SteamLogTailObserver? _steamLog;
    private SessionPhase _sessionPhase = SessionPhase.Desktop;
    private bool _hadConfirmedVirtualSession;
    private string? _lastSteamLogGap;
    private string? _previousSerializedSnapshot;
    private readonly TopologyStabilizer _topologyStabilizer =
        new(TimeSpan.FromMilliseconds(750));
    private readonly PlacementPlanStabilizer _placementStabilizer =
        new(TimeSpan.FromMilliseconds(750));
    private readonly AutomaticPlacementPolicy _autoMovePolicy = new();
    private readonly SteamGameProcessLocator _gameProcessLocator = new();
    private string? _lastGameIdentitySignature;
    private GameWindowMoveResult? _pendingMoveVerification;
    private DateTimeOffset _lastMoveAttemptUtc;
    private int _verificationCount;
    private bool _captureBusy;
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

        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (Icon)SystemIcons.Application.Clone();
        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Stream Display Manager (Steam game auto-placement)",
            ContextMenuStrip = menu,
            Visible = true
        };

        _messageWindow = new MessageWindow(reason => Capture(reason), () => ExitThread());

        _scanTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _scanTimer.Tick += (_, _) => Capture("poll");
        _scanTimer.Start();

        _log.Write("lifecycle", "started", new
        {
            mode = "steam-game-auto-experimental",
            gameWindowIdentification = "known-process-rule-or-steam-install-executable",
            maxAttemptsPerSession = AutomaticPlacementPolicy.MaxAttemptsPerSession,
            schema = 1,
            logDirectory
        });

        Capture("startup");
    }

    private void Capture(string reason)
    {
        if (_closed || _captureBusy)
            return;

        _captureBusy = true;
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

            // Do not clear a fresh Steam start merely because it arrives before
            // SudoVDA attaches. Clear a stale start only after a previously
            // confirmed stream has returned to a stable physical desktop.
            if (_sessionPhase == SessionPhase.Streaming)
                _hadConfirmedVirtualSession = true;

            if (_sessionPhase == SessionPhase.Desktop && stable && !virtualActive &&
                _hadConfirmedVirtualSession)
            {
                _steamEvidence.ResetAfterDetachedVirtualDisplay();
                _hadConfirmedVirtualSession = false;
            }

            VerifyPendingMove(snapshot);

            // Game-agnostic phase 2C: Steam App ID + stable SudoVDA topology +
            // a known game process rule or verified Steam install executable.
            // No blind foreground-window or unrelated desktop window moves.
            if (_sessionPhase == SessionPhase.Streaming && stable &&
                _steamEvidence.IsActive && snapshot.SteamRunning)
            {
                SteamGameIdentityScan gameIdentity = _gameProcessLocator.Inspect(
                    _steamEvidence.AppId, snapshot);
                string identitySignature = string.Join("|",
                    _steamEvidence.AppId, gameIdentity.Status,
                    string.Join(",", gameIdentity.ProcessIds.OrderBy(p => p)),
                    gameIdentity.AccessFailures);
                if (!string.Equals(_lastGameIdentitySignature, identitySignature,
                        StringComparison.Ordinal))
                {
                    _lastGameIdentitySignature = identitySignature;
                    _log.Write("game_identity", "steam-app-install-correlation", new
                    {
                        _steamEvidence.AppId,
                        gameIdentity.Status,
                        gameIdentity.ProcessIds,
                        gameIdentity.AccessFailures,
                        note = "Executable paths and game-selected monitor settings are not logged."
                    });
                }

                var planningInput = new PlacementObservation(
                    SessionConfirmed: true,
                    TopologyStable: true,
                    SteamAppId: _steamEvidence.AppId,
                    Screens: snapshot.Screens.Select(screen =>
                    {
                        ActiveScreen? active = topology.Screens.FirstOrDefault(t =>
                            string.Equals(t.DeviceName, screen.DeviceName,
                                StringComparison.OrdinalIgnoreCase));
                        return new PlacementScreen(
                            screen.DeviceName, screen.X, screen.Y, screen.Width, screen.Height,
                            active?.Kind == "virtual-candidate", active?.Kind == "physical");
                    }).ToArray(),
                    Windows: snapshot.Windows.Select(window =>
                        new PlacementWindow(window.WindowHandle, window.ProcessId,
                            window.ProcessName, window.DeviceName,
                            window.X, window.Y, window.Width, window.Height,
                            window.IsMinimized)).ToArray(),
                    SteamInstalledGameProcessIds: gameIdentity.ProcessIds);

                PlacementPlan plan = PlacementPlanner.Evaluate(planningInput);
                PlacementPlan? stablePlan = _placementStabilizer.Observe(
                    plan, DateTimeOffset.UtcNow);
                if (stablePlan is not null)
                {
                    AutoMoveDecision autoDecision = _autoMovePolicy.Observe(
                        stablePlan, _steamEvidence.SessionNumber);
                    bool experimentalExecutor = stablePlan.AppId is not null &&
                        stablePlan.WindowProcessId is not null &&
                        stablePlan.IdentityEvidence != "none";
                    _log.Write("placement_plan",
                        experimentalExecutor ? "steam-game-auto-experimental" : "dry-run", new
                    {
                        status = stablePlan.Status.ToString(),
                        stablePlan.Reason,
                        stablePlan.AppId,
                        stablePlan.WindowHandle,
                        stablePlan.SourceDisplay,
                        stablePlan.TargetDisplay,
                        stablePlan.WindowProcessId,
                        stablePlan.WindowProcessName,
                        stablePlan.IdentityEvidence,
                        plannerIsDryRun = stablePlan.IsDryRun,
                        _steamEvidence.SessionNumber,
                        identityConfidence = stablePlan.IdentityEvidence,
                        executorEnabled = experimentalExecutor,
                        autoMoveReason = autoDecision.Reason,
                        autoDecision.ShouldAttempt,
                        autoDecision.AttemptNumber,
                        autoDecision.PreviousWindowHandle
                    });

                    if (autoDecision.ShouldAttempt)
                    {
                        if (autoDecision.AttemptNumber == 2)
                        {
                            _log.Write("move_followup", "same-game-process-window-correction", new
                            {
                                stablePlan.AppId,
                                stablePlan.WindowProcessId,
                                autoDecision.PreviousWindowHandle,
                                newWindowHandle = stablePlan.WindowHandle,
                                autoDecision.Reason,
                                _steamEvidence.SessionNumber
                            });
                        }

                        GameWindowMoveResult result =
                            GameWindowMover.Attempt(
                                stablePlan, snapshot, _log, autoDecision.AttemptNumber);
                        if (result.Attempted)
                        {
                            bool immediateOnTarget =
                                result.ApiSucceeded &&
                                result.After is Rectangle afterRect &&
                                result.Requested is Rectangle targetRect &&
                                targetRect.Contains(
                                    afterRect.X + afterRect.Width / 2,
                                    afterRect.Y + afterRect.Height / 2) &&
                                string.Equals(result.AfterDisplay,
                                    result.TargetDisplay, StringComparison.OrdinalIgnoreCase);

                            if (autoDecision.AttemptNumber == 1)
                            {
                                bool recognized = _autoMovePolicy.ConfirmImmediateMove(
                                    result.WindowHandle, result.ProcessId,
                                    result.AppId ?? 0L, _steamEvidence.SessionNumber,
                                    result.AttemptNumber, immediateOnTarget);
                                _log.Write("move_immediate_confirmation", "steam-game-post-win32", new
                                {
                                    result.AppId,
                                    result.ProcessId,
                                    result.WindowHandle,
                                    result.AttemptNumber,
                                    result.ApiSucceeded,
                                    immediateOnTarget,
                                    policyAccepted = recognized,
                                    method = "Win32 result and post-call window-center observation"
                                });
                            }

                            _pendingMoveVerification = result;
                            _lastMoveAttemptUtc = DateTimeOffset.UtcNow;
                            _verificationCount = 0;
                        }
                    }
                    else if (autoDecision.Suppressed)
                    {
                        _log.Write("move_suppressed", "steam-game-auto-policy", new
                        {
                            stablePlan.AppId,
                            stablePlan.WindowHandle,
                            autoDecision.Reason,
                            _steamEvidence.SessionNumber
                        });
                    }
                }
            }
            else
            {
                // A new stream must not inherit a candidate from an earlier
                // session, even when Steam reuses the same game window.
                _placementStabilizer.Reset();
                _lastGameIdentitySignature = null;
            }

            _tray.Text = !stable
                ? "Stream Display Manager: topology changing"
                : _sessionPhase == SessionPhase.Streaming
                    ? "Stream Display Manager: stream confirmed (game auto)"
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
        finally
        {
            _captureBusy = false;
        }
    }

    private void VerifyPendingMove(DesktopSnapshot snapshot)
    {
        if (_pendingMoveVerification is not { Attempted: true } attempt)
            return;

        TimeSpan elapsed = DateTimeOffset.UtcNow - _lastMoveAttemptUtc;
        bool sessionEnded = _sessionPhase != SessionPhase.Streaming;
        if (!sessionEnded &&
            elapsed < TimeSpan.FromSeconds(_verificationCount == 0 ? 3 : 10))
            return;

        WindowObservation? window = snapshot.Windows.FirstOrDefault(w =>
            string.Equals(w.WindowHandle, attempt.WindowHandle,
                StringComparison.OrdinalIgnoreCase) &&
            w.ProcessId == attempt.ProcessId);

        bool onVirtual = window is not null &&
            string.Equals(window.DeviceName, attempt.TargetDisplay,
                StringComparison.OrdinalIgnoreCase);
        bool onSource = window is not null &&
            string.Equals(window.DeviceName, attempt.SourceDisplay,
                StringComparison.OrdinalIgnoreCase);

        _log.Write("move_verification", "steam-game-post-win32", new
        {
            attempt.WindowHandle,
            attempt.ProcessId,
            attempt.AppId,
            attempt.ApiSucceeded,
            attempt.AttemptNumber,
            attempt.TargetDisplay,
            elapsedMs = (long)elapsed.TotalMilliseconds,
            checkNumber = _verificationCount + 1,
            result = window is null ? "window-not-observed" :
                onVirtual ? "on-virtual-target" :
                onSource ? "remained-or-returned-to-physical" : "other-display",
            samePidReplacementWindows = snapshot.Windows
                .Where(w => window is null && w.ProcessId == attempt.ProcessId)
                .Select(w => new
                {
                    w.WindowHandle, w.DeviceName,
                    w.X, w.Y, w.Width, w.Height
                }).ToArray(),
            actualDisplay = window?.DeviceName,
            actualRect = window is null ? null :
                new { window.X, window.Y, window.Width, window.Height },
            streamActive = !sessionEnded
        });

        _verificationCount++;
        if (_verificationCount >= 2 && !sessionEnded && onVirtual &&
            attempt.ApiSucceeded)
        {
            bool confirmed = _autoMovePolicy.ConfirmOwnedMove(
                attempt.WindowHandle, attempt.ProcessId, attempt.AppId ?? 0L,
                _steamEvidence.SessionNumber, attempt.AttemptNumber);
            _log.Write("move_owned_confirmation", "steam-game-verified-on-target", new
            {
                attempt.AttemptNumber,
                attempt.WindowHandle,
                attempt.ProcessId,
                confirmed,
                result = confirmed ? "owned-move-verified" : "stale-move-not-confirmed"
            });
        }
        if (_verificationCount >= 2 || sessionEnded || window is null)
            _pendingMoveVerification = null;
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

        _log.Write("lifecycle", "stopped", new { mode = "steam-game-auto-experimental" });
        _closed = true;

        _scanTimer.Stop();
        _scanTimer.Dispose();
        _messageWindow.DestroyHandle();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _appIcon.Dispose();

        base.ExitThreadCore();
    }

    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action<string> _notify;
        private readonly Action _close;

        public MessageWindow(Action<string> notify, Action close)
        {
            _notify = notify;
            _close = close;
            CreateHandle(new CreateParams { Caption = "StreamDisplayManagerObserver" });
        }

        protected override void WndProc(ref Message m)
        {
            // Signals initiate a fresh census, not a trusted state transition.
            switch (m.Msg)
            {
                case 0x0010: // WM_CLOSE (graceful MSI upgrade/uninstall)
                    _close();
                    return;
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
