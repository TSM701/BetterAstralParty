namespace BetterAstralParty;

// Progress view of one update operation, shared by the home capsule and the update window.
// Stages: 1 check, 2 download, 3 verify, 4 apply, 5 done. The percentage follows the received bytes of the
// update ZIP during the download and fixed points for the short stages around it; it never moves backward.
internal sealed class UpdateProgress
{
    internal const int Steps = 5;
    internal static readonly UpdateProgress Shared = new();
    internal int Step { get; private set; }
    internal int Percent { get; private set; }
    internal long Read { get; private set; }
    internal long Total { get; private set; }
    internal string Version { get; private set; } = "";
    internal bool Transition { get; private set; }
    internal bool Stopped { get; private set; }
    // Set when stopped: a stable code such as BAP-UPD-DOWNLOAD-TIMEOUT and the reason key used for guidance.
    internal string Code { get; private set; } = "";
    internal string Reason { get; private set; } = "";
    internal AutomaticUpdateStatus Status { get; private set; }
    internal int Revision { get; private set; }
    internal bool Visible => Step > 0;
    internal bool Done => Step == Steps;

    internal static bool Running(AutomaticUpdateStatus status) => status is AutomaticUpdateStatus.Preparing or AutomaticUpdateStatus.Downloading
        or AutomaticUpdateStatus.Ready or AutomaticUpdateStatus.Queued or AutomaticUpdateStatus.RetryWaiting;

    // Download: 5..85 % by bytes; verification 88; ready 92; applying 95; done 100.
    internal static int DownloadPercent(long read, long total) => total <= 0 ? 5 : 5 + (int)(80 * Math.Clamp(read, 0, total) / total);

    internal void Observe(AutomaticUpdateStatus status, string target, bool transition, long read = 0, long total = 0, string code = "", string reason = "")
    {
        var before = (Step, Percent, Read, Total, Version, Transition, Stopped, Code, Reason, Status);
        Status = status;
        if (!Done)
        {
            if (Running(status))
            {
                if (Step == 0 || Stopped) { Step = 1; Percent = 0; Read = Total = 0; Stopped = false; Transition = false; Code = Reason = ""; }
                if (target.Length != 0) Version = target;
                Transition |= transition;
                // Preparing appears before the download (operation guard) and after it (verification); never step back.
                var (at, percent) = status switch
                {
                    AutomaticUpdateStatus.Downloading => (2, DownloadPercent(read, total)),
                    AutomaticUpdateStatus.Preparing => Step >= 2 ? (3, 88) : (1, 2),
                    AutomaticUpdateStatus.Ready => (4, 92),
                    AutomaticUpdateStatus.Queued => (4, 95),
                    _ => (Step, Percent)
                };
                Step = Math.Max(Step, at);
                Percent = Math.Max(Percent, percent);
                if (total > 0) { Read = Math.Max(Read, read); Total = total; }
            }
            else if (Step > 0 && status == AutomaticUpdateStatus.Committed) { Step = Steps; Percent = 100; Stopped = false; if (Total > 0) Read = Total; }
            else if (Step > 0 && status is AutomaticUpdateStatus.FaultDisabled or AutomaticUpdateStatus.FaultStorageFailed
                or AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.CancelFailed or AutomaticUpdateStatus.Failed)
            {
                if (!Stopped || code.Length != 0 && Code != code) { Code = code.Length != 0 ? code : "BAP-UPD-" + status.ToString().ToUpperInvariant(); Reason = reason; }
                Stopped = true;
            }
            else if (Step > 0 && !Stopped && status is AutomaticUpdateStatus.Cancelled or AutomaticUpdateStatus.Idle
                or AutomaticUpdateStatus.NotConfigured or AutomaticUpdateStatus.MissingAssets) { Step = 0; Percent = 0; Read = Total = 0; Version = ""; Transition = false; }
        }
        if (before != (Step, Percent, Read, Total, Version, Transition, Stopped, Code, Reason, Status)) Revision++;
    }

    // Stable code from the recorded failure phase and reason, e.g. BAP-UPD-DOWNLOAD-TIMEOUT.
    internal static string FaultCode(string phase, string reason) => "BAP-UPD-" + (phase switch
    {
        "Download" => "DOWNLOAD", "Verification" => "VERIFY", "Preparation" => "PREPARE", "Apply" => "APPLY", "Recovery" => "RECOVERY",
        _ => phase.ToUpperInvariant()
    }) + "-" + reason.ToUpperInvariant();
}
