using BetterAstralParty.Observability;
namespace BetterAstralParty;

// Safe mode has no game hook to drive frames, so one background thread owns the update objects.
// It runs the same check/download/apply sequence as ModUi.Tick and touches no Unity API.
internal static class SafeModeUpdater
{
    private static Thread? _thread;
    private static string _notice = "";
    internal static void Start()
    {
        if (_thread != null) return;
        // The first screen never changes in safe mode; ask again after a failure, within the anonymous hourly quota.
        Plugin.Updates.RepeatEvery = TimeSpan.FromMinutes(10);
        _thread = new Thread(Run) { IsBackground = true, Name = "BAP safe-mode updates" };
        _thread.Start();
    }
    private static void Run()
    {
        var reported = AutomaticUpdateStatus.NotConfigured;
        while (true)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                Plugin.Updates.InstalledChannel(Plugin.Automatic.Snapshot.InstalledChannel);
                Plugin.Updates.Tick(now, Plugin.Automatic.Status != AutomaticUpdateStatus.CheckingInstallation);
                Plugin.UpdateSession?.Poll();
                UpdateDiscoveryBridge.Apply(Plugin.Updates, Plugin.Automatic, Plugin.Version);
                Plugin.PollUpdatePreferences(); Plugin.Automatic.Tick(now);
                var status = Plugin.Automatic.Status;
                if (status != reported)
                {
                    reported = status; StartupGuard.Stage("SafeUpdate" + status, "Observed");
                    if (status == AutomaticUpdateStatus.Committed) Plugin.Logger.LogWarning("안전 모드에서 업데이트를 적용했습니다. 게임을 다시 시작하세요. / Update applied in safe mode; restart the game.");
                }
                Notify();
            }
            catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.None, DiagnosticCode.Unknown, ex); try { Plugin.Updates.FrameFailed(); } catch { } }
            Thread.Sleep(500);
        }
    }
    // A message box is the only way safe mode can reach the player; a failure here never stops updates.
    private static void Notify()
    {
        try
        {
            var automatic = Plugin.Automatic; var result = Plugin.Updates.Result;
            var release = result.Status == ReleaseUpdateStatus.Available && result.Release is { TransitionCandidate: false } found ? found : null;
            var target = automatic.Snapshot.TargetVersion is { Length: > 0 } running ? running : release?.Version ?? "";
            var unattended = automatic.AutoDownload && automatic.ApplyAfterExit && automatic.Fault == null && automatic.Status is not (AutomaticUpdateStatus.FaultDisabled
                or AutomaticUpdateStatus.FaultStorageFailed or AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.ManualUpgradeRequired or AutomaticUpdateStatus.TransitionRequired);
            if (SafeModeNotice.Message(automatic.Status, target, release != null, unattended) is not { } shown || shown.Key == _notice) return;
            _notice = shown.Key;
            StartupGuard.Stage("SafeModeNotice", shown.Key.StartsWith("committed:", StringComparison.Ordinal) ? "Committed" : "Blocked");
            SafeModeNotice.Tell(shown.Text);
        }
        catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.None, DiagnosticCode.Unknown, ex); }
    }
}
