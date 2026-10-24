#nullable enable
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace BetterAstralParty.Updating
{
    internal enum FaultPhase { Download, Verification, Preparation, Apply, Recovery }
    internal enum FaultReason { Network, Timeout, Authentication, Access, RateLimited, Integrity, Preparation, Apply, Recovery, Interrupted, Storage, ManualUpgrade }
    internal sealed class UpdateFault
    {
        internal const string Header = "BetterAstralParty.Failure/v5";
        internal string Root = "", Operation = "", Stage = "-";
        internal uint GamePid, GameSession;
        internal long GameCreation;
        internal UpdateContext Context = null!;
        internal FaultPhase Phase;
        internal FaultReason Reason;
        internal bool PreferencesSaveFailed;
        internal byte[] Bytes() { return Encoding.ASCII.GetBytes(Header + "\nroot=" + Root + "\noperation=" + Operation + "\nstage=" + Stage
            + "\nphase=" + Phase + "\nreason=" + Reason + "\nrepository=" + Context.Repository + "\nchannel=" + Context.Channel
            + "\ncurrent=" + Context.CurrentVersion + "\ntag=" + Context.ReleaseTag + "\nrelease=" + Context.ReleaseId.ToString(CultureInfo.InvariantCulture)
            + "\nasset=" + Context.AssetId.ToString(CultureInfo.InvariantCulture) + "\ngame=" + GamePid.ToString(CultureInfo.InvariantCulture) + ":" + GameCreation.ToString(CultureInfo.InvariantCulture) + ":" + GameSession.ToString(CultureInfo.InvariantCulture)
            + "\nrepository-id=" + Context.RepositoryId.ToString(CultureInfo.InvariantCulture) + "\ntransition=" + (Context.Transition == null ? "-" : Convert.ToBase64String(Context.Transition.Bytes())) + "\n"); }
        internal static UpdateFault Parse(byte[] bytes) {
            foreach (var value in bytes) if (value > 127 || value == 13) throw Bad();
            var lines = Encoding.ASCII.GetString(bytes).Split('\n'); var keys = new[] { "root=", "operation=", "stage=", "phase=", "reason=", "repository=", "channel=", "current=", "tag=", "release=", "asset=", "game=", "repository-id=", "transition=" };
            if (lines.Length != keys.Length + 2 || lines[0] != Header || lines[lines.Length - 1] != "") throw Bad();
            var v = new string[keys.Length]; for (var i = 0; i < keys.Length; i++) { if (!lines[i + 1].StartsWith(keys[i], StringComparison.Ordinal)) throw Bad(); v[i] = lines[i + 1].Substring(keys[i].Length); }
            FaultPhase phase; FaultReason reason; long release, asset, repositoryId;
            if (!Regex.IsMatch(v[0], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1], @"\A[0-9a-f]{32}\z")
                || v[2] != "-" && !UpdateWorkPath.Is(v[2], "stage") || !Enum.TryParse(v[3], out phase) || !Enum.IsDefined(typeof(FaultPhase), phase)
                || !Enum.TryParse(v[4], out reason) || !Enum.IsDefined(typeof(FaultReason), reason) || v[5] != ReleaseFeedPolicy.StableRepository && v[5] != ReleaseFeedPolicy.BetaRepository
                || !long.TryParse(v[9], NumberStyles.None, CultureInfo.InvariantCulture, out release) || !long.TryParse(v[10], NumberStyles.None, CultureInfo.InvariantCulture, out asset)
                || !long.TryParse(v[12], NumberStyles.None, CultureInfo.InvariantCulture, out repositoryId) || repositoryId <= 0) throw Bad();
            ChannelTransitionIntent? transition = null;
            if (v[13] != "-") try { transition = ChannelTransitionIntent.Parse(Convert.FromBase64String(v[13])); } catch (FormatException) { throw Bad(); }
            var record = new UpdateFault { Root = v[0], Operation = v[1], Stage = v[2], Phase = phase, Reason = reason, Context = new UpdateContext(v[5], v[6], v[7], v[8], release, asset, UpdateTrust.PlatformId, repositoryId: repositoryId, transition: transition) };
            var game = v[11].Split(':');
            if (game.Length != 3 || !uint.TryParse(game[0], NumberStyles.None, CultureInfo.InvariantCulture, out record.GamePid) || !long.TryParse(game[1], NumberStyles.None, CultureInfo.InvariantCulture, out record.GameCreation)
                || !uint.TryParse(game[2], NumberStyles.None, CultureInfo.InvariantCulture, out record.GameSession)
                || (record.GamePid == 0 ? record.GameCreation != 0 || record.GameSession != 0 : record.GameCreation <= 0)) throw Bad();
            if (Encoding.ASCII.GetString(record.Bytes()) != Encoding.ASCII.GetString(bytes)) throw Bad(); return record;
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
    }
    // The plugin writes only these immutable mod-owned records, never the BepInEx configuration.
    internal static class UpdateFaults
    {
        internal const string FailurePath = "BetterAstralParty.update-v5.failure", OperationPath = "BetterAstralParty.update-v5.operation", SaveFailurePath = "BetterAstralParty.update-v5.preferences-failed";
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
            var record = new UpdateFault { Root = fence.RootIdentity.Text, Operation = Guid.NewGuid().ToString("N"), Context = context, Phase = phase, Reason = FaultReason.Interrupted };
            if (owner != null) { record.Stage = owner.Stage; record.GamePid = owner.GamePid; record.GameCreation = owner.GameCreation; record.GameSession = owner.GameSession; }
            UpdateFault.Parse(record.Bytes()); UpdateTicket.Write(fence, OperationPath, record.Bytes()); return record;
        }
        internal static bool Same(UpdateContext left, UpdateContext right) { return left.Repository == right.Repository && left.RepositoryId == right.RepositoryId && left.Channel == right.Channel && left.CurrentVersion == right.CurrentVersion && left.ReleaseTag == right.ReleaseTag && left.ReleaseId == right.ReleaseId && left.AssetId == right.AssetId && left.Transition?.Identity == right.Transition?.Identity; }
        internal static void Complete(WindowsFileFence fence, UpdateFault? expected) {
            if (expected == null) return;
            for (var attempt = 0; ; attempt++) try { using (var file = fence.OpenFile(OperationPath, true)) {
                var actual = UpdateFault.Parse(UpdateTicket.Read(file, UpdateTicket.MaxBytes)); if (actual.Root != fence.RootIdentity.Text || UpdateTrust.Hash(actual.Bytes()) != UpdateTrust.Hash(expected.Bytes())) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                file.RemoveUpdateRecord();
            } RemoveArchived(fence); return; } catch (ApplySafetyException error) when (error.NativeError == 2) { RemoveArchived(fence); return; }
            catch (ApplySafetyException error) when (error.NativeError == 32 && attempt < 20) { Thread.Sleep(10); }
        }
        // An interrupted operation whose owner process is gone becomes a durable OFF latch.
        internal static UpdateFault LatchInterrupted(WindowsFileFence fence, UpdateFault interrupted) {
            var existing = ReadFile(fence, FailurePath); if (existing != null) return existing;
            var record = new UpdateFault { Root = fence.RootIdentity.Text, Operation = interrupted.Operation, Context = interrupted.Context, Phase = FaultPhase.Recovery, Reason = FaultReason.Interrupted,
                Stage = interrupted.Stage, GamePid = interrupted.GamePid, GameCreation = interrupted.GameCreation, GameSession = interrupted.GameSession };
            UpdateFault.Parse(record.Bytes());
            try { UpdateTicket.Write(fence, FailurePath, record.Bytes()); }
            catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) { return ReadFile(fence, FailurePath) ?? throw new ApplySafetyException(ApplyFailure.RecoveryRequired); }
            return record;
        }
        internal static UpdateFault Fail(WindowsFileFence fence, UpdateContext context, FaultPhase phase, FaultReason reason, string? stage = null) {
            var existing = ReadFile(fence, FailurePath); if (existing != null) return existing;
            var record = new UpdateFault { Root = fence.RootIdentity.Text, Operation = Guid.NewGuid().ToString("N"), Context = context, Phase = phase, Reason = reason, Stage = stage ?? "-" };
            var active = ReadFile(fence, OperationPath);
            if (active != null && Same(active.Context, context)) { record.GamePid = active.GamePid; record.GameCreation = active.GameCreation; record.GameSession = active.GameSession; }
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
        // The owner is the game process that ran the in-process update.
        // An exited owner can linger while another program still holds its handle; its image path is then
        // unreadable (error 31), so check exit first. A reused PID with another image or start time is not the owner.
        internal static bool OwnerAlive(WindowsFileFence fence, UpdateFault guard) {
            if (guard.GamePid == 0) return false;
            try { using (var process = WindowsProcessGuard.Observe(guard.GamePid, allowExited: true))
                return !process.Exited && string.Equals(process.Image, WindowsPath.Absolute(fence.Full("AstralParty_INT.exe")), StringComparison.OrdinalIgnoreCase)
                    && process.Creation == guard.GameCreation && process.Session == guard.GameSession; }
            catch (ApplySafetyException error) when (error.NativeError == 87 || error.NativeError == 1168) { return false; }
        }
        internal static void Acknowledge(WindowsFileFence fence) {
            foreach (var entry in UpdateRecovery.Scan(fence)) if (entry.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            var fault = Read(fence); if (fault == null) return;
            var files = new List<WindowsFileFence.FileLease>();
            try {
                foreach (var name in new[] { OperationPath, SaveFailurePath, FailurePath }) try {
                    var file = fence.OpenFile(name, true); files.Add(file);
                    if (name == SaveFailurePath) { if (Encoding.ASCII.GetString(UpdateTicket.Read(file, 128)) != UpdateTrust.Hash(fault.Bytes()) + "\n") throw new ApplySafetyException(ApplyFailure.RecoveryRequired); }
                    else { var record = UpdateFault.Parse(UpdateTicket.Read(file, UpdateTicket.MaxBytes)); if (record.Root != fence.RootIdentity.Text || !Same(record.Context, fault.Context)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired); }
                } catch (ApplySafetyException error) when (error.NativeError == 2) { }
                // Validate all evidence first; the failure latch goes last. Nothing later reads acknowledged records.
                for (var i = 0; i < files.Count; i++) files[i].RemoveUpdateRecord();
            } finally { foreach (var file in files) file.Dispose(); }
            RemoveArchived(fence);
        }
        // Versions before 1.6.0 renamed finished records to bap-operation-done-*/bap-fault-ack-* in the game root.
        // Remove only names in that form whose content is this root's v5 record (or an acknowledged preferences
        // marker), by held handle. Best effort: a record that cannot be verified or removed stays and blocks nothing.
        private static readonly Regex ArchivedName = new Regex(@"\Abap-(?:operation-done|fault-ack)-[0-9a-f]{32}\z", RegexOptions.CultureInvariant);
        internal static List<string> Archived(string gameRoot) {
            var result = new List<string>();
            try { foreach (var file in Directory.GetFiles(WindowsPath.ForIo(WindowsPath.Absolute(gameRoot)), "bap-*")) {
                var leaf = WindowsPath.Leaf(WindowsPath.FromIo(file)); if (ArchivedName.IsMatch(leaf)) result.Add(leaf);
            } } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ApplySafetyException) { }
            return result;
        }
        internal static int RemoveArchived(WindowsFileFence fence) { return ArchivedRecords(fence, true); }
        // Read-only count of the records RemoveArchived would remove, so previews promise only what removal does.
        internal static int RemovableArchived(string gameRoot) {
            try { using (var fence = new WindowsFileFence(gameRoot)) return ArchivedRecords(fence, false); }
            catch (Exception error) when (error is ApplySafetyException || error is IOException || error is UnauthorizedAccessException) { return 0; }
        }
        private static int ArchivedRecords(WindowsFileFence fence, bool remove) {
            var count = 0;
            foreach (var name in Archived(fence.Root)) try { using (var file = fence.OpenFile(name, remove)) {
                var bytes = UpdateTicket.Read(file, UpdateTicket.MaxBytes);
                if (!Regex.IsMatch(Encoding.ASCII.GetString(bytes), @"\A[0-9A-F]{64}\n\z") && UpdateFault.Parse(bytes).Root != fence.RootIdentity.Text) continue;
                if (remove) file.RemoveUpdateRecord(); count++;
            } } catch (Exception error) when (error is ApplySafetyException || error is UpdateValidationException || error is ArgumentException || error is IOException || error is UnauthorizedAccessException) { }
            return count;
        }
        // A committed apply whose guard could not be removed leaves only that guard; the installed plugin then carries its target.
        internal static bool Committed(WindowsFileFence fence, UpdateFault guard) {
            if (guard.Phase != FaultPhase.Apply) return false;
            try { using (var plugin = fence.OpenFile(UpdateTrust.PluginPath))
                return InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes)).Version == UpdateVersion.Parse(guard.Context.ReleaseTag).Tag; }
            catch (ApplySafetyException) { return false; }
        }
        internal static int RemoveArchived(string gameRoot, Action<string> output) {
            var found = Archived(gameRoot).Count; if (found == 0) return 0;
            var removed = 0;
            try { using (var fence = new WindowsFileFence(gameRoot)) removed = RemoveArchived(fence); }
            catch (Exception error) when (error is ApplySafetyException || error is IOException || error is UnauthorizedAccessException) { }
            if (removed != 0) output("끝난 업데이트 기록 " + removed + "개를 정리했습니다. / Removed " + removed + " finished update records.");
            if (removed != found) output("확인하지 못한 업데이트 기록 " + (found - removed) + "개를 남겨 둡니다. / Kept " + (found - removed) + " unverified update records.");
            return removed;
        }
    }
}
