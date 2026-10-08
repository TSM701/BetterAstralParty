#nullable enable
using System;
using BetterAstralParty.Observability;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
namespace BetterAstralParty.Updating {
    internal enum HelperResult { Committed, Cancelled, ManualUpgradeRequired, FilesBusy, AlreadyClaimed, RecoveryRequired, Recovered, Abandoned, FaultDisabled }
    internal static class UpdateHelperService {
        private static bool Cancelled(WindowsFileFence fence, UpdateTicket ticket, string hash) {
            try { using (var file = fence.OpenFile(ticket.Stage + "/helper.cancel")) {
                if (Encoding.ASCII.GetString(UpdateTicket.Read(file, 128)) != hash + "\n") throw new ApplySafetyException(ApplyFailure.InvalidState); return true;
            } } catch (ApplySafetyException error) when (error.NativeError == 2) { return false; }
            catch (ApplySafetyException error) when (error.NativeError == 32) { return true; }
        }
        private static HelperResult Stop(WindowsFileFence fence, UpdateTicket ticket, string hash, UpdateCancelSignal signal, UpdateFault guard) {
            DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.WaitForExit,signal.Failure?DiagnosticOutcome.Failed:DiagnosticOutcome.Cancelled,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
            if (signal.Failure) {
                // Synchronous failure signal survives parent exit. Save latch before retiring guard.
                try { UpdateFaults.Fail(fence, ticket.Context, FaultPhase.Preparation, FaultReason.Interrupted, ticket.Stage); UpdateFaults.Complete(fence, guard); }
                catch { return Finish(fence,ticket,hash,HelperResult.RecoveryRequired,guard); }
                return Finish(fence,ticket,hash,HelperResult.FaultDisabled,guard);
            }
            UpdateFaults.Complete(fence, guard);
            return Finish(fence,ticket,hash,HelperResult.Cancelled,guard);
        }
        private static HelperResult Failed(WindowsFileFence fence, UpdateTicket ticket, string hash, UpdateFault guard, FaultPhase phase, UpdateTrust trust, string? attemptWork) {
#if BAP_FIXTURE_HELPER
            // Fault injection runs only after Apply has released its exclusive plan leases.
            if (Environment.GetEnvironmentVariable("BAP_FIXTURE_CORRUPT_PLAN") == "1") foreach (var entry in UpdateRecovery.Scan(fence))
                using (var plan = fence.OpenFile(entry.Work + "/plan", true)) { plan.Stream.Position=0; plan.Stream.WriteByte(88); plan.Flush(); }
            if (Environment.GetEnvironmentVariable("BAP_FIXTURE_FAULT_STORAGE") == "1") Directory.CreateDirectory(fence.Full(UpdateFaults.FailurePath));
#endif
            var recorded = false;
            try { var fault = UpdateFaults.Fail(fence, ticket.Context, phase, phase == FaultPhase.Apply ? FaultReason.Apply : phase == FaultPhase.Verification ? FaultReason.Integrity : FaultReason.Preparation, ticket.Stage);
                recorded = fault.Root == fence.RootIdentity.Text && UpdateFaults.Same(fault.Context, ticket.Context); } catch (Exception error) {RecoveryFailed(error,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag,DiagnosticCode.StorageUnavailable);}
            // Recover only this ticket's durable exact attempt, never a retained attempt
            // with the same descriptor. Both restored/finished states retain failure OFF.
            // Unknown/corrupt journals or unsuccessful recovery remain blocked.
            try {
                DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Recovery,DiagnosticOutcome.Begin,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                var entry = UpdateRecoveryPolicy.Select(UpdateRecovery.Scan(fence), ticket.DescriptorHash, attemptWork, false);
                if (entry == null) HelperRecoveryBinding.RequireNoUntrackedWork(fence, attemptWork);
                var outcome = entry == null ? RecoveryOutcome.NoChanges : UpdateRecoveryPolicy.Outcome(UpdateTransaction.Recover(fence, entry.Work, trust, ticket.Context,diagnosticOperation:DiagnosticOperation.VerifiedGuid(guard.Operation)));
                foreach (var remaining in UpdateRecovery.Scan(fence)) if (remaining.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Recovery,outcome==RecoveryOutcome.NoChanges?DiagnosticOutcome.NotObserved:DiagnosticOutcome.Completed,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                if (recorded) { HelperRecoveryBinding.WriteResolution(fence, ticket, hash, attemptWork ?? entry?.Work, outcome);
                    UpdateFaults.Complete(fence, guard); return Finish(fence,ticket,hash,HelperResult.FaultDisabled,guard); }
            } catch (Exception error) { RecoveryFailed(error,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag); }
            return Finish(fence,ticket,hash,HelperResult.RecoveryRequired,guard);
        }
        internal static HelperResult Run(string root, string stage, UpdateTrust trust, TimeSpan? maximumWait = null) {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            using (var fence = new WindowsFileFence(root)) using (var ticketFile = fence.OpenFile(stage + "/handoff.ticket")) {
                var ticketBytes = UpdateTicket.Read(ticketFile, UpdateTicket.MaxBytes); var ticket = UpdateTicket.Parse(ticketBytes); var hash = UpdateTrust.Hash(ticketBytes);
                if (ticket.Stage != stage || ticket.Context.RepositoryId > 0 && !ReleaseFeedPolicy.Matches(ticket.Context)
                    || ticket.Context.RepositoryId == 0 && ticket.Context.Repository != "TSM701/BetterAstralParty") throw new ApplySafetyException(ApplyFailure.InvalidState);
                ticket.Recheck(fence);
                ticket.Context.Transition?.RequireStage(fence, ticket);
                if (ticket.Context.RepositoryId == 0 && !trust.AllowsLegacyContracts) return HelperResult.ManualUpgradeRequired;
                if (UpdateFaults.ReadFile(fence, UpdateFaults.FailurePath) != null) return HelperResult.FaultDisabled;
                try { using(var result=fence.OpenFile(stage+"/helper.result")) {
                    var lines=Encoding.ASCII.GetString(UpdateTicket.Read(result,128)).Split('\n');
                    if(lines.Length!=3 || lines[0]!=hash || lines[2]!="") throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                    if(lines[1]=="Committed" || lines[1]=="Cancelled" || lines[1]=="FilesBusy" || lines[1]=="FaultDisabled") return HelperResult.AlreadyClaimed;
                    if(lines[1]=="ManualUpgradeRequired") return HelperResult.ManualUpgradeRequired;
                    if(lines[1]=="RecoveryRequired") return HelperResult.RecoveryRequired;
                    throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                }} catch(ApplySafetyException error) when(error.NativeError==2) { }
                var guard = UpdateFaults.ReadFile(fence, UpdateFaults.OperationPath);
                if (guard != null && (guard.Stage != stage || !UpdateFaults.Same(guard.Context, ticket.Context) || guard.GamePid != ticket.GamePid || guard.GameCreation != ticket.GameCreation || guard.GameSession != ticket.GameSession || guard.LauncherPid != ticket.LauncherPid || guard.LauncherCreation != ticket.LauncherCreation || guard.LauncherSession != ticket.LauncherSession)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                guard ??= UpdateFaults.Begin(fence, ticket.Context, FaultPhase.Apply, ticket);
                var phase = FaultPhase.Preparation;
                var diagnosticPhase = DiagnosticPhase.Staging;
                string? attemptWork = null;
                var files = new List<WindowsFileFence.FileLease>();
                try {
                    try {
                    using (var receipt = fence.OpenFile(InstallReceipt.Path)) if (receipt.Stream.Length > 16384 || UpdateEligibility.Receipt(UpdateTicket.Read(receipt, 16384)) != InstallEligibility.Supported) { UpdateFaults.Complete(fence, guard); return Finish(fence,ticket,hash,HelperResult.ManualUpgradeRequired,guard); }
                    } catch(ApplySafetyException error) when(error.NativeError==2) {UpdateFaults.Complete(fence,guard);return Finish(fence,ticket,hash,HelperResult.ManualUpgradeRequired,guard);}
                    using (var signal = UpdateCancelSignal.Open(hash)) {
                        foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" }) files.Add(fence.OpenFile(stage + "/" + name));
                        var descriptorBytes = UpdateTicket.Read(files[0], UpdateTrust.MaxDescriptorBytes); var signature = UpdateTicket.Read(files[1], UpdateTrust.MaxSignatureBytes); var key = Encoding.ASCII.GetString(UpdateTicket.Read(files[2], 40)); var zip = UpdateTicket.Read(files[3], UpdateTrust.MaxZipBytes);
                        phase = FaultPhase.Verification;
                        diagnosticPhase = DiagnosticPhase.DescriptorVerification;
                        DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.DescriptorVerification,DiagnosticOutcome.Begin,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                        var descriptor = trust.VerifyDescriptor(descriptorBytes, key, signature, ticket.Context);
                        if (descriptor.DescriptorSha256 != ticket.DescriptorHash) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                        DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.DescriptorVerification,DiagnosticOutcome.Completed,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                        diagnosticPhase = DiagnosticPhase.PayloadVerification;
                        DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.PayloadVerification,DiagnosticOutcome.Begin,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                        UpdatePackage.VerifyPayload(zip, descriptor);
                        DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.PayloadVerification,DiagnosticOutcome.Completed,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                        phase = FaultPhase.Preparation;
                        diagnosticPhase = DiagnosticPhase.HelperQueue;
                        try { UpdateTicket.Write(fence, stage + "/helper.claim", Encoding.ASCII.GetBytes(hash + "\n")); }
                        catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) { UpdateFaults.Complete(fence, guard); return HelperResult.AlreadyClaimed; }
                        diagnosticPhase = DiagnosticPhase.WaitForExit;
                        using (var game = WindowsProcessGuard.Capture(ticket.GamePid, fence.Full("AstralParty_INT.exe"), ticket.GameCreation, ticket.GameSession))
                        using (var launcher = ticket.LauncherPid == 0 ? null : WindowsProcessGuard.Capture(ticket.LauncherPid, fence.Full("BetterAstralParty-Launcher.exe"), ticket.LauncherCreation, ticket.LauncherSession)) {
                            diagnosticPhase = DiagnosticPhase.HelperQueue;
                            UpdateFaults.HelperOwner(fence, guard);
                            UpdateTicket.Write(fence, stage + "/helper.ready", Encoding.ASCII.GetBytes(hash + "\n"));
                            diagnosticPhase = DiagnosticPhase.WaitForExit;
                            DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.WaitForExit,DiagnosticOutcome.Begin,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                            var start = DateTime.UtcNow; var limit = maximumWait ?? TimeSpan.FromHours(4);
                            if (limit <= TimeSpan.Zero || limit > TimeSpan.FromHours(4)) throw new ApplySafetyException(ApplyFailure.InvalidState);
                            while (!game.Exited || launcher != null && !launcher.Exited) {
                                ticket.Recheck(fence);
                                if (signal.Cancelled || Cancelled(fence, ticket, hash)) return Stop(fence, ticket, hash, signal, guard);
                                if (DateTime.UtcNow - start > limit) { UpdateFaults.Complete(fence, guard); DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.WaitForExit,DiagnosticOutcome.Unavailable,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag); return Finish(fence,ticket,hash,HelperResult.FilesBusy,guard); }
                                Thread.Sleep(100);
                            }
                            game.Dispose(); launcher?.Dispose();
                            var leaseDue = DateTime.UtcNow.Add(limit); // A new game instance is a wait, never a failure/retry loop.
                            while (true) {
                                if (signal.Cancelled || Cancelled(fence, ticket, hash)) return Stop(fence, ticket, hash, signal, guard);
                                try { using (WindowsProcessGuard.Acquire(fence, new[] { "AstralParty_INT.exe", "BetterAstralParty-Launcher.exe" })) { } break; }
                                catch (ApplySafetyException error) when (error.Failure == ApplyFailure.FileBusy || error.Failure == ApplyFailure.ProcessBusy) {
                                    if (DateTime.UtcNow >= leaseDue) { UpdateFaults.Complete(fence, guard); DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.WaitForExit,DiagnosticOutcome.Unavailable,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag); return Finish(fence,ticket,hash,HelperResult.FilesBusy,guard); }
                                    Thread.Sleep(100);
                                }
                            }
                            ticket.Recheck(fence);
                            if (signal.Cancelled || Cancelled(fence, ticket, hash)) return Stop(fence, ticket, hash, signal, guard);
                            DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.WaitForExit,DiagnosticOutcome.Completed,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                            phase = FaultPhase.Apply;
                            diagnosticPhase = DiagnosticPhase.Apply;
                            DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Apply,DiagnosticOutcome.Begin,DiagnosticOperation.VerifiedGuid(guard.Operation),ticket.Context.ReleaseTag);
                            var work = "bap-txn-" + Guid.NewGuid().ToString("N");
                            UpdateTicket.Write(fence, stage + "/helper.attempt", HelperRecoveryBinding.Attempt(hash, work));
                            attemptWork = work; // Durable before the transaction can create its plan or rename files.
                            var result = UpdateTransaction.Apply(fence, descriptorBytes, key, signature, zip, trust, ticket.Context
#if BAP_FIXTURE_HELPER
                                , boundary: point => { var fail = Environment.GetEnvironmentVariable("BAP_FIXTURE_APPLY_BOUNDARY"); if (point == fail) {
                                    throw new IOException("fixture apply interruption"); } }, beforeGameLease: () => { if(Environment.GetEnvironmentVariable("BAP_FIXTURE_GAME_LEASE_RACE")=="1") throw new ApplySafetyException(ApplyFailure.FileBusy,32); }
#endif
                                , attemptWork: work,diagnosticOperation:DiagnosticOperation.VerifiedGuid(guard.Operation)
                            );
                            if (result != TransactionResult.Committed) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                            UpdateFaults.Complete(fence, guard);
                            return Finish(fence,ticket,hash,HelperResult.Committed,guard);
                        }
                    }
                } catch (ApplySafetyException error) when ((error.Failure == ApplyFailure.CompatibilityRequired || error.Failure == ApplyFailure.InstallationRequired) && UpdateRecovery.Scan(fence).TrueForAll(entry => !entry.Pending)) {
                    UpdateFaults.Complete(fence,guard);return Finish(fence,ticket,hash,HelperResult.ManualUpgradeRequired,guard);
                } catch (ApplySafetyException error) when (error.Failure == ApplyFailure.ProcessBusy && UpdateRecovery.Scan(fence).TrueForAll(entry => !entry.Pending)) {
                    UpdateFaults.Complete(fence,guard); return Finish(fence,ticket,hash,HelperResult.FilesBusy,guard);
                } catch(Exception error) {
                    DiagnosticHub.Failure(DiagnosticFeature.Helper,diagnosticPhase,DiagnosticCode.Unknown,error,
                        nativeCode:error is ApplySafetyException safety?safety.NativeError:0,validationCode:error is UpdateValidationException validation?(int)validation.Failure:0,
                        operation:DiagnosticOperation.VerifiedGuid(guard.Operation),applyCode:error is ApplySafetyException apply?(int)apply.Failure:0,targetVersion:ticket.Context.ReleaseTag);
                    return Failed(fence, ticket, hash, guard, phase, trust, attemptWork);
                }
                finally { foreach (var file in files) file.Dispose(); }
            }
        }
        internal static HelperResult Recover(string root, string stage, UpdateTrust trust) {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            using (var fence = new WindowsFileFence(root)) using (var file = fence.OpenFile(stage + "/handoff.ticket")) {
                var ticket = UpdateTicket.Parse(UpdateTicket.Read(file, UpdateTicket.MaxBytes)); ticket.Recheck(fence);
                ticket.Context.Transition?.RequireStage(fence, ticket);
                if (ticket.Context.RepositoryId == 0 && !trust.AllowsLegacyContracts) throw new ApplySafetyException(ApplyFailure.InstallationRequired);
                if (ticket.Stage != stage) throw new ApplySafetyException(ApplyFailure.InvalidState);
                var owner=UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath);
                if(owner!=null) HelperRecoveryBinding.RequireOwner(fence,ticket,owner);
                WindowsProcessGuard.AssertNoRunning(new[]{fence.Full("AstralParty_INT.exe"),fence.Full("BetterAstralParty-Launcher.exe")});
                if(owner!=null && UpdateFaults.OwnerAlive(fence,owner)) throw new ApplySafetyException(ApplyFailure.ProcessBusy);
                byte[] descriptor,signature,key;
                using(var source=fence.OpenFile(stage+"/descriptor.bin")) descriptor=UpdateTicket.Read(source,UpdateTrust.MaxDescriptorBytes);
                using(var source=fence.OpenFile(stage+"/signature.bin")) signature=UpdateTicket.Read(source,UpdateTrust.MaxSignatureBytes);
                using(var source=fence.OpenFile(stage+"/key-id.bin")) key=UpdateTicket.Read(source,40);
                if(trust.VerifyDescriptor(descriptor,Encoding.ASCII.GetString(key),signature,ticket.Context).DescriptorSha256!=ticket.DescriptorHash) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var hash = UpdateTrust.Hash(ticket.Bytes()); var attemptWork = HelperRecoveryBinding.ReadAttempt(fence, ticket, hash);
                var entry = UpdateRecoveryPolicy.Select(UpdateRecovery.Scan(fence), ticket.DescriptorHash, attemptWork, ticket.Context.RepositoryId == 0 && trust.AllowsLegacyContracts);
                if (entry == null) HelperRecoveryBinding.RequireNoUntrackedWork(fence, attemptWork);
                // A direct interrupted recovery must establish effective durable OFF before
                // mutation/guard retirement. Completed-history inspection creates no new latch.
                if (owner != null || entry?.Pending == true) UpdateFaults.LatchRecovery(fence,ticket,owner);
                var diagnosticOwner=owner;
                if(diagnosticOwner==null){try {var latch=UpdateFaults.ReadFile(fence,UpdateFaults.FailurePath);if(latch!=null){HelperRecoveryBinding.RequireLatch(fence,ticket,latch);diagnosticOwner=latch;}}catch {}}
                var diagnosticOperation=DiagnosticOperation.VerifiedGuid(diagnosticOwner?.Operation);
                DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Recovery,DiagnosticOutcome.Begin,diagnosticOperation,ticket.Context.ReleaseTag);
                try {
                var wasPending=entry?.Pending == true;
                var outcome = entry == null ? RecoveryOutcome.NoChanges : UpdateRecoveryPolicy.Outcome(UpdateTransaction.Recover(fence, entry.Work, trust, ticket.Context,diagnosticOperation:diagnosticOperation));
                foreach (var remaining in UpdateRecovery.Scan(fence)) if (remaining.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                HelperRecoveryBinding.WriteResolution(fence, ticket, hash, attemptWork ?? entry?.Work, outcome);
                UpdateFaults.Complete(fence, owner); // Exact guard only; durable latch awaits explicit in-game reactivation.
                var result=outcome == RecoveryOutcome.NoChanges ? HelperResult.Abandoned : outcome == RecoveryOutcome.TargetCompleted ? HelperResult.Committed : HelperResult.Recovered;
                RecordResult(ticket,diagnosticOwner,result,DiagnosticPhase.Recovery,wasPending && outcome==RecoveryOutcome.TargetCompleted?DiagnosticCode.CommitCompleted:wasPending && outcome==RecoveryOutcome.OriginalRestored?DiagnosticCode.RollbackCompleted:DiagnosticCode.None);
                return result;
                } catch(Exception error) {RecoveryFailed(error,diagnosticOperation,ticket.Context.ReleaseTag);throw;}
            }
        }
        internal static void RecordResult(UpdateTicket ticket,UpdateFault? owner,HelperResult result,DiagnosticPhase phase,DiagnosticCode code=DiagnosticCode.None) {
            var operation=DiagnosticOperation.VerifiedGuid(owner?.Operation);
            var outcome=result==HelperResult.Committed || result==HelperResult.Recovered?DiagnosticOutcome.Completed:result==HelperResult.Cancelled?DiagnosticOutcome.Cancelled:result==HelperResult.FilesBusy || result==HelperResult.ManualUpgradeRequired || result==HelperResult.AlreadyClaimed?DiagnosticOutcome.Unavailable:result==HelperResult.Abandoned?DiagnosticOutcome.NotObserved:DiagnosticOutcome.Failed;
            DiagnosticHub.Stage(DiagnosticFeature.Helper,phase,outcome,operation,ticket.Context.ReleaseTag,code);
            DiagnosticHub.FlushSoon();
            DiagnosticHub.Status(DiagnosticFeature.Helper,SafeEnum.Parse<DiagnosticUpdateState>(result.ToString()),ticket.Context.ReleaseTag,operation);
        }
        private static void RecoveryFailed(Exception error,DiagnosticOperation operation,string target,DiagnosticCode code=DiagnosticCode.Unknown) {
            var safety=error as ApplySafetyException;var validation=error as UpdateValidationException;
            DiagnosticHub.Failure(DiagnosticFeature.Helper,DiagnosticPhase.Recovery,code,error,safety?.NativeError??0,validation!=null?(int)validation.Failure:0,operation,safety!=null?(int)safety.Failure:0,target);
            DiagnosticHub.FlushSoon();
        }
        private static HelperResult Finish(WindowsFileFence fence, UpdateTicket ticket, string hash, HelperResult result,UpdateFault owner) {
            UpdateTicket.Write(fence, ticket.Stage + "/helper.result", Encoding.ASCII.GetBytes(hash + "\n" + result + "\n"));
            RecordResult(ticket,owner,result,DiagnosticPhase.Apply,result==HelperResult.Committed?DiagnosticCode.CommitCompleted:DiagnosticCode.None);return result;
        }
    }
}

