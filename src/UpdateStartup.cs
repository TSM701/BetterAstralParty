using BetterAstralParty.Observability;
using BetterAstralParty.Updating;
namespace BetterAstralParty;

// Runs before any feature: finishes or rolls back an update transaction a previous game session
// left unfinished. Without a helper process this is the only in-game recovery point.
internal static class UpdateStartup
{
    // False when this session must not run features (recovery needed or the running image is stale).
    internal static bool Recover(string root, string runningVersion, UpdateTrust trust)
    {
        try
        {
            using var fence = new WindowsFileFence(root);
            UpdateLaunchGate.AssertNoReinstall(fence);
            var operation = UpdateFaults.ReadFile(fence, UpdateFaults.OperationPath);
            if (operation != null && UpdateFaults.OwnerAlive(fence, operation)) { Plugin.Logger.LogWarning("다른 게임 창이 업데이트 중이라 이 창에서는 모드를 켜지 않습니다. / Another game instance is updating; BAP stays off here."); return false; }
            var pending = UpdateRecovery.PendingWork(fence);
            if (pending != null)
            {
                DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.Recovery, DiagnosticOutcome.Begin);
                var context = UpdateRecovery.RecoveryContext(fence, pending, trust);
                var result = UpdateTransaction.Recover(fence, pending.Work, trust, context, inProcess: true);
                DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.Recovery, DiagnosticOutcome.Completed, code: result == TransactionResult.Committed ? DiagnosticCode.CommitCompleted : DiagnosticCode.RollbackCompleted);
                // Automatic updates stay OFF until the player re-enables them after an interrupted update.
                if (operation != null) UpdateFaults.LatchInterrupted(fence, operation); else UpdateFaults.Fail(fence, context, FaultPhase.Recovery, FaultReason.Interrupted, pending.Stage);
                Plugin.Logger.LogWarning("중단된 업데이트를 " + (result == TransactionResult.Committed ? "마무리" : "되돌림") + "했습니다. / Interrupted update " + (result == TransactionResult.Committed ? "completed." : "rolled back."));
            }
            // Without a transaction only an interrupted apply latches; a download cut off before any file changed is just cleared.
            else if (operation != null) UpdateFaults.SettleGuard(fence, operation);
            if (operation != null) UpdateFaults.Complete(fence, operation);
            UpdateLaunchGate.Check(fence);
            // Downloaded ZIPs of finished updates are no longer needed; plans, backups and descriptors stay.
            try { UpdateRetention.Clean(fence, trust); }
            catch (Exception error) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Recovery, DiagnosticCode.Unknown, error); }
            // Rollback restores the previous plugin file while this process still runs the newer image.
            using var plugin = fence.OpenFile(UpdateTrust.PluginPath);
            var installed = InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes)).Version;
            if (installed != runningVersion && pending != null)
            {
                Plugin.Logger.LogWarning("업데이트 복구가 끝났습니다. 게임을 다시 시작하세요. / Update recovery finished; restart the game.");
                return false;
            }
            return true;
        }
        catch (Exception error)
        {
            DiagnosticHub.Failure(DiagnosticFeature.Loader, DiagnosticPhase.BootstrapGate, DiagnosticCode.Prerequisite, error,
                nativeCode: error is ApplySafetyException safety ? safety.NativeError : 0, applyCode: error is ApplySafetyException apply ? (int)apply.Failure : 0);
            DiagnosticHub.FlushSoon();
            Plugin.Logger.LogError("업데이트 복구가 필요해 모드 기능을 켜지 않습니다. 게임을 닫고 Install.cmd를 다시 실행하세요. / Update recovery required; BAP stays off. Close the game and run Install.cmd again.");
            return false;
        }
    }
}
