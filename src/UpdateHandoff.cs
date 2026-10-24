using BetterAstralParty.Observability;
using BetterAstralParty.Updating;
namespace BetterAstralParty;

// Protocol 5: no helper process. The verified package is applied from this running game process;
// the loaded plugin image is renamed aside, so the new version takes effect at the next launch.
internal sealed class UpdateHandoff(string root, UpdateTrust trust) : IUpdateHandoff
{
    public bool Configured => trust.Configured;
    internal Task Initialize() => Task.CompletedTask;
    private sealed record Prepared(byte[] Descriptor, string KeyId, byte[] Signature, byte[] Zip, UpdateContext Context) : IPreparedUpdate { public string Stage => "-"; }
    private sealed class Job(AutomaticUpdateStatus status, string operation, string target) : IUpdateHelperJob
    {
        public DiagnosticOperation DiagnosticContextOperation => DiagnosticOperation.VerifiedGuid(operation);
        public string? DiagnosticTargetVersion => target;
        public AutomaticUpdateStatus? Poll() => status;
        public void Dispose() { }
    }
    private static byte[] Bytes(Stream stream) { using (stream) using (var output = new MemoryStream()) { stream.CopyTo(output); return output.ToArray(); } }
    public Task<object> Prepare(DownloadedUpdate update, UpdateContext context, CancellationToken token)
    {
        if (!Configured) throw new AutomaticUpdateException(AutomaticUpdateStatus.NotConfigured); token.ThrowIfCancellationRequested();
        using var fence = new WindowsFileFence(root);
        try {
            using var receipt = fence.OpenFile(InstallReceipt.Path);
            if (receipt.Stream.Length > 16384 || UpdateEligibility.Receipt(UpdateTicket.Read(receipt, 16384)) != InstallEligibility.Supported) throw new AutomaticUpdateException(AutomaticUpdateStatus.ManualUpgradeRequired);
        } catch (ApplySafetyException error) when (error.NativeError == 2) { throw new AutomaticUpdateException(AutomaticUpdateStatus.ManualUpgradeRequired); }
        var descriptor = Bytes(update.OpenDescriptor()); var signature = Bytes(update.OpenSignature()); var zip = Bytes(update.Payload.OpenRead());
        // Apply re-verifies everything against the installed receipt; this rejects a bad package early.
        var verified = trust.VerifyDescriptor(descriptor, update.KeyId, signature, context, token);
        UpdatePackage.VerifyPayload(zip, verified, token);
        if (context.Transition != null) {
            try {
                using var file = fence.OpenFile(InstallReceipt.Path); var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384));
                using var plugin = fence.OpenFile(UpdateTrust.PluginPath);
                if (plugin.Hash() != receipt.Files[UpdateTrust.PluginPath]) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                context.Transition.Require(fence, receipt, file.Hash(), ChannelTransitionIntent.WaterHashAt(fence), InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes)).Version, verified);
            } catch (ApplySafetyException error) when (error.Failure == ApplyFailure.InstallationRequired) { throw new AutomaticUpdateException(AutomaticUpdateStatus.Cancelled); }
            catch (ApplySafetyException error) when (error.Failure == ApplyFailure.FileBusy && error.NativeError == 32) { throw new AutomaticUpdateException(AutomaticUpdateStatus.FilesBusy); }
        }
        return Task.FromResult<object>(new Prepared(descriptor, update.KeyId, signature, zip, context));
    }
    public Task<object> Queue(object prepared, CancellationToken token)
    {
        var item = (Prepared)prepared; if (!Configured) throw new AutomaticUpdateException(AutomaticUpdateStatus.NotConfigured);
        token.ThrowIfCancellationRequested();
        using var fence = new WindowsFileFence(root);
        UpdateTicket owner;
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        using (var game = WindowsProcessGuard.Capture((uint)process.Id, fence.Full("AstralParty_INT.exe"), process.StartTime.ToFileTimeUtc(), WindowsProcessGuard.CurrentSession))
            owner = new UpdateTicket { GamePid = game.Pid, GameCreation = game.Creation, GameSession = game.Session };
        var guard = UpdateFaults.Begin(fence, item.Context, FaultPhase.Apply, owner);
        var operation = DiagnosticOperation.VerifiedGuid(guard.Operation);
        try {
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.Apply, DiagnosticOutcome.Begin, operation, item.Context.ReleaseTag);
            if (UpdateTransaction.Apply(fence, item.Descriptor, item.KeyId, item.Signature, item.Zip, trust, item.Context, diagnosticOperation: operation, inProcess: true) != TransactionResult.Committed)
                throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            // The update is installed; a guard file still busy after retries must not turn that into a failure.
            try { UpdateFaults.Complete(fence, guard); }
            catch (Exception error) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Apply, error is ApplySafetyException { NativeError: 32 } ? DiagnosticCode.FilesBusy : DiagnosticCode.StorageUnavailable, error, operation: operation, targetVersion: item.Context.ReleaseTag,
                nativeCode: error is ApplySafetyException safety ? safety.NativeError : 0, applyCode: error is ApplySafetyException apply ? (int)apply.Failure : 0); }
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.Apply, DiagnosticOutcome.Completed, operation, item.Context.ReleaseTag, DiagnosticCode.CommitCompleted);
            DiagnosticHub.FlushSoon();
            return Task.FromResult<object>(new Job(AutomaticUpdateStatus.Committed, guard.Operation, item.Context.ReleaseTag));
        } catch (ApplySafetyException error) when ((error.Failure is ApplyFailure.CompatibilityRequired or ApplyFailure.InstallationRequired) && NoPending(fence)) {
            UpdateFaults.Complete(fence, guard); throw new AutomaticUpdateException(AutomaticUpdateStatus.ManualUpgradeRequired);
        } catch (ApplySafetyException error) when ((error.Failure is ApplyFailure.ProcessBusy or ApplyFailure.FileBusy) && NoPending(fence)) {
            UpdateFaults.Complete(fence, guard); throw new AutomaticUpdateException(AutomaticUpdateStatus.FilesBusy);
        } catch (Exception error) when (error is not AutomaticUpdateException) {
            DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Apply, DiagnosticCode.Unknown, error, operation: operation, targetVersion: item.Context.ReleaseTag,
                nativeCode: error is ApplySafetyException safety ? safety.NativeError : 0, applyCode: error is ApplySafetyException apply ? (int)apply.Failure : 0);
            // Roll back the exact unfinished attempt here; a failed rollback stays blocked for startup recovery.
            try {
                var pending = UpdateRecovery.PendingWork(fence);
                if (pending != null) UpdateTransaction.Recover(fence, pending.Work, trust, UpdateRecovery.RecoveryContext(fence, pending, trust), diagnosticOperation: operation, inProcess: true);
                UpdateFaults.Fail(fence, item.Context, FaultPhase.Apply, FaultReason.Apply);
                UpdateFaults.Complete(fence, guard);
            } catch (Exception recovery) {
                DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Recovery, DiagnosticCode.Unknown, recovery, operation: operation, targetVersion: item.Context.ReleaseTag);
                DiagnosticHub.FlushSoon();
                throw new AutomaticUpdateException(AutomaticUpdateStatus.RecoveryRequired);
            }
            DiagnosticHub.FlushSoon();
            throw new AutomaticUpdateException(AutomaticUpdateStatus.FaultDisabled);
        }
    }
    private static bool NoPending(WindowsFileFence fence) { try { return UpdateRecovery.PendingWork(fence) == null; } catch { return false; } }
    public void SignalCancel(object prepared) { }
    public void SignalFailure(object prepared) { }
    public Task<string?> Inspect() { using var fence = new WindowsFileFence(root); return Task.FromResult(UpdateRecovery.PendingWork(fence)?.Work); }
    public Task Cancel(object prepared) => Task.CompletedTask;
}
