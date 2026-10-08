#nullable enable
using System;
using System.Globalization;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace BetterAstralParty.Updating
{
    internal enum FaultPhase { Download, Verification, Preparation, Apply, Recovery }
    internal enum FaultReason { Network, Timeout, Authentication, Access, RateLimited, Integrity, Preparation, Apply, Recovery, Interrupted, Storage, ManualUpgrade }
    internal sealed class UpdateFault
    {
        internal string Root = "", Operation = "", Stage = "-";
        internal int Schema = 2;
        internal uint GamePid, GameSession, LauncherPid, LauncherSession;
        internal long GameCreation, LauncherCreation;
        internal UpdateContext Context = null!;
        internal FaultPhase Phase;
        internal FaultReason Reason;
        internal bool PreferencesSaveFailed;
        internal byte[] Bytes() { return Encoding.ASCII.GetBytes("BetterAstralParty.Failure/v" + Schema + "\nroot=" + Root + "\noperation=" + Operation + "\nstage=" + Stage
            + "\nphase=" + Phase + "\nreason=" + Reason + "\nrepository=" + Context.Repository + "\nchannel=" + Context.Channel
            + "\ncurrent=" + Context.CurrentVersion + "\ntag=" + Context.ReleaseTag + "\nrelease=" + Context.ReleaseId.ToString(CultureInfo.InvariantCulture)
            + "\nasset=" + Context.AssetId.ToString(CultureInfo.InvariantCulture) + "\n" + (Schema == 1 ? "" : "game=" + ProcessText(GamePid, GameCreation, GameSession) + "\nlauncher=" + ProcessText(LauncherPid, LauncherCreation, LauncherSession) + "\n")
            + (Schema == 3 ? "repository-id=" + Context.RepositoryId.ToString(CultureInfo.InvariantCulture) + "\ntransition=" + (Context.Transition == null ? "-" : Convert.ToBase64String(Context.Transition.Bytes())) + "\n" : "")); }
        private static string ProcessText(uint pid, long creation, uint session) { return pid.ToString(CultureInfo.InvariantCulture) + ":" + creation.ToString(CultureInfo.InvariantCulture) + ":" + session.ToString(CultureInfo.InvariantCulture); }
        private static void ProcessFields(string value, out uint pid, out long creation, out uint session) {
            var fields = value.Split(':'); pid = 0; creation = 0; session = 0;
            if (fields.Length != 3 || !uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
                || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out creation) || !uint.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out session)
                || pid == 0 && (creation != 0 || session != 0) || pid > 0 && creation <= 0) throw Bad();
        }
        internal static UpdateFault Parse(byte[] bytes) {
            foreach (var value in bytes) if (value > 127 || value == 13) throw Bad();
            var lines = Encoding.ASCII.GetString(bytes).Split('\n'); var keys = new[] { "root=", "operation=", "stage=", "phase=", "reason=", "repository=", "channel=", "current=", "tag=", "release=", "asset=" };
            var schema = lines.Length == 13 && lines[0] == "BetterAstralParty.Failure/v1" ? 1 : lines.Length == 15 && lines[0] == "BetterAstralParty.Failure/v2" ? 2
                : lines.Length == 17 && lines[0] == "BetterAstralParty.Failure/v3" ? 3 : 0;
            if (schema == 0 || lines[lines.Length-1] != "") throw Bad();
            var v = new string[keys.Length]; for (var i = 0; i < keys.Length; i++) { if (!lines[i+1].StartsWith(keys[i], StringComparison.Ordinal)) throw Bad(); v[i] = lines[i+1].Substring(keys[i].Length); }
            FaultPhase phase; FaultReason reason; long release, asset;
            if (!Regex.IsMatch(v[0], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1], @"\A[0-9a-f]{32}\z")
                || v[2] != "-" && !Regex.IsMatch(v[2], @"\Abap-stage-[0-9a-f]{32}\z") || !Enum.TryParse(v[3], out phase) || !Enum.IsDefined(typeof(FaultPhase), phase)
                || !Enum.TryParse(v[4], out reason) || !Enum.IsDefined(typeof(FaultReason), reason) || v[5] != ReleaseFeedPolicy.StableRepository && v[5] != ReleaseFeedPolicy.BetaRepository
                || !long.TryParse(v[9], NumberStyles.None, CultureInfo.InvariantCulture, out release) || !long.TryParse(v[10], NumberStyles.None, CultureInfo.InvariantCulture, out asset)) throw Bad();
            long repositoryId = 0; ChannelTransitionIntent? transition = null;
            if (schema == 3) {
                if (!lines[14].StartsWith("repository-id=", StringComparison.Ordinal) || !long.TryParse(lines[14].Substring(14), NumberStyles.None, CultureInfo.InvariantCulture, out repositoryId) || repositoryId <= 0
                    || !lines[15].StartsWith("transition=", StringComparison.Ordinal)) throw Bad();
                var encoded = lines[15].Substring(11); if (encoded != "-") try { transition = ChannelTransitionIntent.Parse(Convert.FromBase64String(encoded)); } catch (FormatException) { throw Bad(); }
            }
            var record = new UpdateFault { Schema = schema, Root = v[0], Operation = v[1], Stage = v[2], Phase = phase, Reason = reason, Context = new UpdateContext(v[5], v[6], v[7], v[8], release, asset, UpdateTrust.PlatformId, repositoryId: repositoryId, transition: transition) };
            if (schema >= 2) {
                if (!lines[12].StartsWith("game=", StringComparison.Ordinal) || !lines[13].StartsWith("launcher=", StringComparison.Ordinal)) throw Bad();
                ProcessFields(lines[12].Substring(5), out record.GamePid, out record.GameCreation, out record.GameSession);
                ProcessFields(lines[13].Substring(9), out record.LauncherPid, out record.LauncherCreation, out record.LauncherSession);
            }
            if (Encoding.ASCII.GetString(record.Bytes()) != Encoding.ASCII.GetString(bytes)) throw Bad(); return record;
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
    }
    // Helper writes only these immutable mod-owned records, never the BepInEx configuration.
    internal static class UpdateFaults
    {
        internal const string FailurePath = "BetterAstralParty.update.failure", OperationPath = "BetterAstralParty.update.operation", SaveFailurePath = "BetterAstralParty.update.preferences-failed";
        internal static UpdateFault? ReadFile(WindowsFileFence fence, string path) {
            for (var attempt = 0; ; attempt++) try { using (var file = fence.OpenFile(path)) {
                var value = UpdateFault.Parse(UpdateTicket.Read(file, UpdateTicket.MaxBytes)); if (value.Root != fence.RootIdentity.Text) throw new ApplySafetyException(ApplyFailure.RecoveryRequired); return value;
            } } catch (ApplySafetyException error) when (error.NativeError == 2) { return null; }
            catch (ApplySafetyException error) when (error.NativeError == 32 && attempt < 20) { Thread.Sleep(10); }
        }
        internal static UpdateFault? Read(WindowsFileFence fence) {
            var failure = ReadFile(fence, FailurePath) ?? ReadFile(fence, OperationPath);
            if (failure == null) return null;
            try { using (var file = fence.OpenFile(SaveFailurePath)) {
                if (Encoding.ASCII.GetString(UpdateTicket.Read(file, 128)) != UpdateTrust.Hash(failure.Bytes()) + "\n") throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                failure.PreferencesSaveFailed = true;
            } } catch (ApplySafetyException error) when (error.NativeError == 2) { }
            return failure;
        }
        internal static UpdateFault Begin(WindowsFileFence fence, UpdateContext context, FaultPhase phase, UpdateTicket? owner = null) {
            if (Read(fence) != null) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            var record = new UpdateFault { Schema = context.RepositoryId > 0 ? 3 : 2, Root = fence.RootIdentity.Text, Operation = Guid.NewGuid().ToString("N"), Context = context, Phase = phase, Reason = FaultReason.Interrupted };
            if (owner != null) {
                record.Stage = owner.Stage; record.GamePid = owner.GamePid; record.GameCreation = owner.GameCreation; record.GameSession = owner.GameSession;
                record.LauncherPid = owner.LauncherPid; record.LauncherCreation = owner.LauncherCreation; record.LauncherSession = owner.LauncherSession;
            }
            UpdateFault.Parse(record.Bytes()); UpdateTicket.Write(fence, OperationPath, record.Bytes()); return record;
        }
        internal static bool Same(UpdateContext left, UpdateContext right) { return left.Repository == right.Repository && left.RepositoryId == right.RepositoryId && left.Channel == right.Channel && left.CurrentVersion == right.CurrentVersion && left.ReleaseTag == right.ReleaseTag && left.ReleaseId == right.ReleaseId && left.AssetId == right.AssetId && left.Transition?.Identity == right.Transition?.Identity; }
        internal static void Complete(WindowsFileFence fence, UpdateFault? expected, UpdateContext? helperContext = null) {
            if (expected == null) { if (helperContext != null) throw new ApplySafetyException(ApplyFailure.RecoveryRequired); return; }
            try { using (var file = fence.OpenFile(OperationPath, true)) {
                var actual = UpdateFault.Parse(UpdateTicket.Read(file, UpdateTicket.MaxBytes)); if (actual.Root != fence.RootIdentity.Text || UpdateTrust.Hash(actual.Bytes()) != UpdateTrust.Hash(expected.Bytes()) || helperContext != null && !Same(actual.Context, helperContext)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                file.RenameTo(fence, "bap-operation-done-" + actual.Operation); file.Flush();
            } } catch (ApplySafetyException error) when (error.NativeError == 2) { }
        }
        internal static UpdateFault LatchRecovery(WindowsFileFence fence, UpdateTicket ticket, UpdateFault? expected) {
            var active = ReadFile(fence, OperationPath);
            if (expected == null && active != null || expected != null && (active == null || UpdateTrust.Hash(active.Bytes()) != UpdateTrust.Hash(expected.Bytes())))
                throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            if (active != null) HelperRecoveryBinding.RequireOwner(fence, ticket, active);
            var existing = ReadFile(fence, FailurePath);
            if (existing != null) { HelperRecoveryBinding.RequireLatch(fence, ticket, existing); return existing; }
            var record = new UpdateFault { Schema = ticket.Context.RepositoryId > 0 ? 3 : 2, Root = fence.RootIdentity.Text,
                Operation = active?.Operation ?? Guid.NewGuid().ToString("N"), Context = ticket.Context, Phase = FaultPhase.Recovery, Reason = FaultReason.Interrupted,
                Stage = ticket.Stage, GamePid = ticket.GamePid, GameCreation = ticket.GameCreation, GameSession = ticket.GameSession,
                LauncherPid = ticket.LauncherPid, LauncherCreation = ticket.LauncherCreation, LauncherSession = ticket.LauncherSession };
            UpdateFault.Parse(record.Bytes());
            try { UpdateTicket.Write(fence, FailurePath, record.Bytes()); }
            catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) {
                existing = ReadFile(fence, FailurePath) ?? throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                HelperRecoveryBinding.RequireLatch(fence, ticket, existing); return existing;
            }
            return record;
        }
        internal static UpdateFault Fail(WindowsFileFence fence, UpdateContext context, FaultPhase phase, FaultReason reason, string? stage = null) {
            var existing = ReadFile(fence, FailurePath); if (existing != null) return existing;
            var record = new UpdateFault { Schema = context.RepositoryId > 0 ? 3 : 2, Root = fence.RootIdentity.Text, Operation = Guid.NewGuid().ToString("N"), Context = context, Phase = phase, Reason = reason, Stage = stage ?? "-" };
            var active = ReadFile(fence, OperationPath);
            if (active != null && Same(active.Context, context)) {
                record.GamePid = active.GamePid; record.GameCreation = active.GameCreation; record.GameSession = active.GameSession;
                record.LauncherPid = active.LauncherPid; record.LauncherCreation = active.LauncherCreation; record.LauncherSession = active.LauncherSession;
            }
            // Validate before creating a record; an uncertain record is never adopted/overwritten.
            UpdateFault.Parse(record.Bytes());
            try { UpdateTicket.Write(fence, FailurePath, record.Bytes()); }
            catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) { return ReadFile(fence, FailurePath) ?? throw new ApplySafetyException(ApplyFailure.RecoveryRequired); }
            return record;
        }
        internal static void SaveFailed(WindowsFileFence fence, UpdateFault record) {
            try { UpdateTicket.Write(fence, SaveFailurePath, Encoding.ASCII.GetBytes(UpdateTrust.Hash(record.Bytes()) + "\n")); }
            catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) { }
        }

        internal static bool OwnerAlive(WindowsFileFence fence, UpdateFault guard) {
            if (guard.Schema < 2 || guard.GamePid == 0) throw new ApplySafetyException(ApplyFailure.ProcessUncertain);
            bool Alive(uint pid, long creation, uint session, string image) {
                if (pid == 0) return false;
                try { using (var process = WindowsProcessGuard.Capture(pid, fence.Full(image), creation, session)) return !process.Exited; }
                catch (ApplySafetyException error) when (error.NativeError == 87 || error.NativeError == 1168) { return false; }
            }
            if (Alive(guard.GamePid, guard.GameCreation, guard.GameSession, "AstralParty_INT.exe")) return true;
            if (Alive(guard.LauncherPid, guard.LauncherCreation, guard.LauncherSession, "BetterAstralParty-Launcher.exe")) return true;
            if (guard.Stage == "-") return false;
            try { using (var file = fence.OpenFile(guard.Stage + "/helper.owner")) {
                var bytes = UpdateTicket.Read(file, 512); var lines = Encoding.ASCII.GetString(bytes).Split('\n');
                if (lines.Length != 5 || lines[0] != "BetterAstralParty.HelperOwner/v1" || lines[1] != UpdateTrust.Hash(guard.Bytes()) || lines[4] != "") throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                uint pid, session; long creation;
                if (!uint.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out pid) || pid == 0) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var fields = lines[3].Split(':');
                if (fields.Length != 2 || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out creation) || !uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out session)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                return Alive(pid, creation, session, "BetterAstralParty-UpdateHelper.exe");
            } } catch (ApplySafetyException error) when (error.NativeError == 2) { return false; }
        }
        internal static void HelperOwner(WindowsFileFence fence, UpdateFault guard) {
            using (var process = Process.GetCurrentProcess()) UpdateTicket.Write(fence, guard.Stage + "/helper.owner", Encoding.ASCII.GetBytes("BetterAstralParty.HelperOwner/v1\n" + UpdateTrust.Hash(guard.Bytes()) + "\n" + process.Id.ToString(CultureInfo.InvariantCulture) + "\n" + process.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture) + ":" + process.SessionId.ToString(CultureInfo.InvariantCulture) + "\n"));
        }
        internal static void Acknowledge(WindowsFileFence fence) {
            foreach (var entry in UpdateRecovery.Scan(fence)) if (entry.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            var fault = Read(fence); if (fault == null) return;
            // A live helper retains this ticket lease; reactivation cannot overlap its exit/recovery.
            if (fault.Stage != "-") using (var ticket = fence.OpenFile(fault.Stage + "/handoff.ticket")) {
                var value = UpdateTicket.Parse(UpdateTicket.Read(ticket, UpdateTicket.MaxBytes)); value.Recheck(fence);
                if (!Same(value.Context, fault.Context)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            }
            var files = new List<WindowsFileFence.FileLease>(); var names = new List<string>();
            try {
                foreach (var name in new[] { OperationPath, SaveFailurePath, FailurePath }) try {
                    var file = fence.OpenFile(name, true); files.Add(file); names.Add(name);
                    if (name == SaveFailurePath) { if (Encoding.ASCII.GetString(UpdateTicket.Read(file, 128)) != UpdateTrust.Hash(fault.Bytes()) + "\n") throw new ApplySafetyException(ApplyFailure.RecoveryRequired); }
                    else { var record = UpdateFault.Parse(UpdateTicket.Read(file, UpdateTicket.MaxBytes)); if (record.Root != fence.RootIdentity.Text || !Same(record.Context, fault.Context)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired); }
                } catch (ApplySafetyException error) when (error.NativeError == 2) { }
                // Validate all evidence first. Failure latch is archived last; no delete or overwrite.
                for (var i = 0; i < files.Count; i++) { files[i].RenameTo(fence, "bap-fault-ack-" + Guid.NewGuid().ToString("N")); files[i].Flush(); }
            } finally { foreach (var file in files) file.Dispose(); }
        }
    }
}
