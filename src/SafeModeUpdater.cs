using BetterAstralParty.Observability;
namespace BetterAstralParty;

// Safe mode has no game hook to drive frames, so one background thread owns the update objects.
// It runs the same check/download/apply sequence as ModUi.Tick and touches no Unity API.
internal static class SafeModeUpdater
{
    private static Thread? _thread;
    internal static void Start()
    {
        if (_thread != null) return;
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
            }
            catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.None, DiagnosticCode.Unknown, ex); try { Plugin.Updates.FrameFailed(); } catch { } }
            Thread.Sleep(500);
        }
    }
}
