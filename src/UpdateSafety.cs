using System.Diagnostics;
using BetterAstralParty.Updating;
namespace BetterAstralParty;

internal sealed record UpdateSafetySnapshot(UpdateFault? Fault = null, bool Busy = false, bool RecoveryRequired = false, string? Stage = null,
    ReceiptChannelState? Channels = null, string? InstalledVersion = null);
internal interface IUpdateSafety
{
    Task<UpdateSafetySnapshot> Probe();
    Task<UpdateFault> Begin(UpdateContext context);
    Task Complete(UpdateFault? operation);
    Task<UpdateFault> Fail(UpdateContext context, FaultPhase phase, FaultReason reason, string? stage);
    Task SavePreferences(bool download, bool apply);
    Task SaveFailed(UpdateFault fault);
    Task Acknowledge();
}
// All filesystem methods are called on workers. Helper never edits BepInEx configuration.
internal sealed class FileUpdateSafety(string root, Func<bool, bool, Task> save, Func<UpdateTicket>? owner = null, Func<Task>? readiness = null) : IUpdateSafety
{
    private readonly SemaphoreSlim _preferences = new(1, 1);
    public async Task<UpdateSafetySnapshot> Probe()
    {
        try {
            using var fence = new WindowsFileFence(root);
            var fault = UpdateFaults.ReadFile(fence, UpdateFaults.FailurePath);
            var operation = UpdateFaults.ReadFile(fence, UpdateFaults.OperationPath);
            if (fault == null && operation != null && UpdateFaults.OwnerAlive(fence, operation)) return new UpdateSafetySnapshot(Busy: true, Stage: operation.Stage == "-" ? null : operation.Stage);
            fault = UpdateFaults.Read(fence); // Includes interrupted guard and save-failure evidence.
            // Startup recovery normally finishes a pending transaction before this probe runs.
            var pending = UpdateRecovery.PendingWork(fence); var stage = pending?.Stage;
            if (pending != null && fault == null) fault = UpdateFaults.Fail(fence, UpdateRecovery.RecoveryContext(fence, pending, UpdateTrust.Production()), FaultPhase.Recovery, FaultReason.Recovery, pending.Stage);
            ReceiptChannelState? channels = null; string? installed = null;
            if (fault == null && stage == null) {
                try { using var file = fence.OpenFile(InstallReceipt.Path); var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384));
                    using var plugin = fence.OpenFile(UpdateTrust.PluginPath); var metadata = InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes));
                    if (plugin.Hash() != receipt.Files[UpdateTrust.PluginPath]) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                    channels = receipt.Channels; installed = metadata.Version;
                    if (channels != null && (channels.Root != fence.RootIdentity.Text || channels.ActiveVersion != installed || metadata.UpdateProtocol != UpdateTrust.Protocol || metadata.SettingsSchema != 1))
                        throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                } catch (ApplySafetyException error) when (error.NativeError == 2) { }
                if (readiness != null) await readiness().ConfigureAwait(false);
            }
            return new UpdateSafetySnapshot(fault, RecoveryRequired: stage != null, Stage: stage ?? (fault?.Stage == "-" ? null : fault?.Stage), Channels: channels, InstalledVersion: installed);
        } catch (ApplySafetyException error) when (error.Failure == ApplyFailure.FileBusy && error.NativeError is 32 or 33) {
            // A temporary read/handshake lock must not persistently disable update preferences.
            return new UpdateSafetySnapshot(Busy: true);
        }
    }
    public Task<UpdateFault> Begin(UpdateContext context)
    {
        using var fence = new WindowsFileFence(root); UpdateTicket actor;
        if (owner != null) actor = owner();
        else {
            using var process = Process.GetCurrentProcess();
            using var verified = WindowsProcessGuard.Capture((uint)process.Id, fence.Full("AstralParty_INT.exe"), process.StartTime.ToFileTimeUtc(), WindowsProcessGuard.CurrentSession);
            actor = new UpdateTicket { Stage = "-", GamePid = verified.Pid, GameCreation = verified.Creation, GameSession = verified.Session };
        }
        return Task.FromResult(UpdateFaults.Begin(fence, context, FaultPhase.Download, actor));
    }
    public Task Complete(UpdateFault? operation) { using var fence = new WindowsFileFence(root); UpdateFaults.Complete(fence, operation); return Task.CompletedTask; }
    public Task<UpdateFault> Fail(UpdateContext context, FaultPhase phase, FaultReason reason, string? stage)
    { using var fence = new WindowsFileFence(root); return Task.FromResult(UpdateFaults.Fail(fence, context, phase, reason, stage)); }
    public async Task SavePreferences(bool download, bool apply)
    { await _preferences.WaitAsync().ConfigureAwait(false); try { await save(download, apply).ConfigureAwait(false); } finally { _preferences.Release(); } }
    public Task SaveFailed(UpdateFault fault) { using var fence = new WindowsFileFence(root); UpdateFaults.SaveFailed(fence, fault); return Task.CompletedTask; }
    public Task Acknowledge() { using var fence = new WindowsFileFence(root); UpdateFaults.Acknowledge(fence); return Task.CompletedTask; }
}
