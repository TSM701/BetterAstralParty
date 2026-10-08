using BetterAstralParty.Observability;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using BetterAstralParty.Updating;
namespace BetterAstralParty;

internal sealed class UpdateHandoff(string root, UpdateTrust trust, string helperSha256 = "") : IUpdateHandoff
{
    private const string HelperImage = "BetterAstralParty-UpdateHelper.exe";
    private string _helperHash = helperSha256;
    public bool Configured => trust.Configured && Volatile.Read(ref _helperHash).Length == 64;
    internal Task Initialize() {
        if (!trust.Configured || Volatile.Read(ref _helperHash).Length == 64) return Task.CompletedTask;
        using var fence = new WindowsFileFence(root);
        Volatile.Write(ref _helperHash, HelperDelivery.Fingerprint(fence, trust, minimumProtocol: 2) ?? "");
        return Task.CompletedTask;
    }
    private sealed record Prepared(string Root, UpdateTicket Ticket, UpdateCancelSignal Signal) : IPreparedUpdate { public string Stage => Ticket.Stage; }
    private static AutomaticUpdateStatus ExitStatus(int code) => code switch {
        0 => AutomaticUpdateStatus.Committed, 3 => AutomaticUpdateStatus.Cancelled,
        4 => AutomaticUpdateStatus.ManualUpgradeRequired, 7 => AutomaticUpdateStatus.FilesBusy,
        8 => AutomaticUpdateStatus.AlreadyClaimed, 9 => AutomaticUpdateStatus.RecoveryRequired,
        12 => AutomaticUpdateStatus.FaultDisabled, _ => AutomaticUpdateStatus.Failed
    };
    private sealed class Job(Process process, UpdateCancelSignal signal, string operation, string target) : IUpdateHelperJob
    {
        public DiagnosticOperation DiagnosticContextOperation => DiagnosticOperation.VerifiedGuid(operation);
        public string? DiagnosticTargetVersion => target;
        public AutomaticUpdateStatus? Poll() { try { return !process.HasExited ? null : ExitStatus(process.ExitCode); } catch { throw new AutomaticUpdateException(AutomaticUpdateStatus.RecoveryRequired); } }
        public void Dispose() { process.Dispose(); signal.Dispose(); }
    }
    private static byte[] Bytes(Stream stream) { using (stream) using (var output = new MemoryStream()) { stream.CopyTo(output); return output.ToArray(); } }
    public Task<object> Prepare(DownloadedUpdate update, UpdateContext context, CancellationToken token)
    {
        if (!Configured) throw new AutomaticUpdateException(AutomaticUpdateStatus.NotConfigured); token.ThrowIfCancellationRequested();
        using var fence = new WindowsFileFence(root);
        try {
        using (var receipt = fence.OpenFile(InstallReceipt.Path))
            if (receipt.Stream.Length > 16384 || UpdateEligibility.Receipt(UpdateTicket.Read(receipt, 16384)) != InstallEligibility.Supported) throw new AutomaticUpdateException(AutomaticUpdateStatus.ManualUpgradeRequired);
        } catch (ApplySafetyException error) when(error.NativeError==2) { throw new AutomaticUpdateException(AutomaticUpdateStatus.ManualUpgradeRequired); }
        if (context.Transition != null) {
            try {
                using var file = fence.OpenFile(InstallReceipt.Path); var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384));
                using var plugin = fence.OpenFile(UpdateTrust.PluginPath);
                if (plugin.Hash() != receipt.Files[UpdateTrust.PluginPath]) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var descriptor = trust.VerifyDescriptor(Bytes(update.OpenDescriptor()), update.KeyId, Bytes(update.OpenSignature()), context, token);
                context.Transition.Require(fence, receipt, file.Hash(), ChannelTransitionIntent.WaterHashAt(fence), InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes)).Version, descriptor);
            } catch (ApplySafetyException error) when (error.Failure == ApplyFailure.InstallationRequired) { throw new AutomaticUpdateException(AutomaticUpdateStatus.Cancelled); }
            catch (ApplySafetyException error) when (error.Failure == ApplyFailure.FileBusy && error.NativeError == 32) { throw new AutomaticUpdateException(AutomaticUpdateStatus.FilesBusy); }
        }
        using var process = Process.GetCurrentProcess();
        using var game = WindowsProcessGuard.Capture((uint)process.Id, fence.Full("AstralParty_INT.exe"), process.StartTime.ToFileTimeUtc(), WindowsProcessGuard.CurrentSession);
        var staged = UpdateStaging.Create(fence, Bytes(update.OpenDescriptor()), update.KeyId, Bytes(update.OpenSignature()), Bytes(update.Payload.OpenRead()), trust, context, token);
        var ticket = new UpdateTicket { RootId = fence.RootIdentity.Text, Stage = staged.Directory, StageId = staged.DirectoryIdentity.Text, DescriptorHash = staged.DescriptorSha256,
            Context = context, GamePid = game.Pid, GameCreation = game.Creation, GameSession = game.Session };
        var launcherPid = Environment.GetEnvironmentVariable("BAP_UPDATER_LAUNCHER_PID");
        if (!string.IsNullOrEmpty(launcherPid)) {
            if (!uint.TryParse(launcherPid, NumberStyles.None, CultureInfo.InvariantCulture, out ticket.LauncherPid)
                || !long.TryParse(Environment.GetEnvironmentVariable("BAP_UPDATER_LAUNCHER_CREATION"), NumberStyles.None, CultureInfo.InvariantCulture, out ticket.LauncherCreation)
                || !uint.TryParse(Environment.GetEnvironmentVariable("BAP_UPDATER_LAUNCHER_SESSION"), NumberStyles.None, CultureInfo.InvariantCulture, out ticket.LauncherSession)) throw new AutomaticUpdateException(AutomaticUpdateStatus.Failed);
            using var launcher = WindowsProcessGuard.Capture(ticket.LauncherPid, fence.Full("BetterAstralParty-Launcher.exe"), ticket.LauncherCreation, ticket.LauncherSession);
        }
        token.ThrowIfCancellationRequested(); UpdateTicket.Write(fence, ticket.Stage + "/handoff.ticket", ticket.Bytes());
        if (context.Transition != null) {
            try { UpdateTicket.Write(fence, "bap-transition-" + context.Transition.Operation,
                Encoding.ASCII.GetBytes(context.Transition.Identity + "\nstage=" + ticket.Stage + "\n")); }
            catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) { throw new AutomaticUpdateException(AutomaticUpdateStatus.Cancelled); }
        }
        return Task.FromResult<object>(new Prepared(root, ticket, UpdateCancelSignal.Create(UpdateTrust.Hash(ticket.Bytes()))));
    }
    public async Task<object> Queue(object prepared, CancellationToken token)
    {
        var item = (Prepared)prepared; if (!Configured) throw new AutomaticUpdateException(AutomaticUpdateStatus.NotConfigured);
        using var fence = new WindowsFileFence(item.Root); item.Ticket.Recheck(fence);
        using var image = fence.OpenLaunchImage(HelperImage); if (image.Hash() != Volatile.Read(ref _helperHash)) throw new AutomaticUpdateException(AutomaticUpdateStatus.Failed);
        using var registration = token.Register(item.Signal.Set); token.ThrowIfCancellationRequested();
        var guard = UpdateFaults.Begin(fence, item.Ticket.Context, FaultPhase.Apply, item.Ticket);
        if (token.IsCancellationRequested) { UpdateFaults.Complete(fence, guard); token.ThrowIfCancellationRequested(); }
        var info = new ProcessStartInfo { FileName = fence.Full(HelperImage), WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--apply"); info.ArgumentList.Add(root); info.ArgumentList.Add(item.Ticket.Stage);
        var process = Process.Start(info) ?? throw new AutomaticUpdateException(AutomaticUpdateStatus.Failed);
        if (token.IsCancellationRequested) { process.Dispose(); await Cancel(item); token.ThrowIfCancellationRequested(); }
        try {
            var due = DateTime.UtcNow.AddSeconds(15);
            while (true) {
                token.ThrowIfCancellationRequested();
                try { using var ready = fence.OpenFile(item.Ticket.Stage + "/helper.ready");
                    if (Encoding.ASCII.GetString(UpdateTicket.Read(ready, 128)) != UpdateTrust.Hash(item.Ticket.Bytes()) + "\n") throw new AutomaticUpdateException(AutomaticUpdateStatus.Failed);
                    break;
                } catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 32) { }
                // A successful apply cannot precede the authenticated ready handshake.
                if (process.HasExited) { var status = ExitStatus(process.ExitCode); throw new AutomaticUpdateException(status == AutomaticUpdateStatus.Committed ? AutomaticUpdateStatus.Failed : status); }
                if (DateTime.UtcNow > due) throw new AutomaticUpdateException(AutomaticUpdateStatus.Failed);
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            return new Job(process, item.Signal, guard.Operation, item.Ticket.Context.ReleaseTag);
        } catch (OperationCanceledException) { item.Signal.Set(); process.Dispose(); throw; }
        catch (AutomaticUpdateException error) when (error.Status is AutomaticUpdateStatus.ManualUpgradeRequired or AutomaticUpdateStatus.FilesBusy or AutomaticUpdateStatus.AlreadyClaimed or AutomaticUpdateStatus.Cancelled) {
            // The exited helper rejected a prerequisite. It already retired its guard;
            // this must never set the failure event or rewrite saved preferences.
            item.Signal.Set(); item.Signal.Dispose(); process.Dispose(); throw;
        }
        catch { item.Signal.SetFailure(); process.Dispose(); throw; }
    }
    public void SignalCancel(object prepared) => ((Prepared)prepared).Signal.Set();
    public void SignalFailure(object prepared) => ((Prepared)prepared).Signal.SetFailure();
    public Task<string?> Inspect() { using var fence = new WindowsFileFence(root); return Task.FromResult(UpdateRecovery.PendingTicket(fence)); }
    public Task Cancel(object prepared)
    {
        var item = (Prepared)prepared; item.Signal.Set();
        try { using var fence = new WindowsFileFence(item.Root); item.Ticket.Recheck(fence); UpdateTicket.Write(fence, item.Ticket.Stage + "/helper.cancel", Encoding.ASCII.GetBytes(UpdateTrust.Hash(item.Ticket.Bytes()) + "\n")); }
        catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) { }
        item.Signal.Dispose(); return Task.CompletedTask;
    }
}
