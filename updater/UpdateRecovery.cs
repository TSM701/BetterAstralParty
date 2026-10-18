#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace BetterAstralParty.Updating
{
    internal sealed class RecoveryEntry
    {
        internal string Work = "", Descriptor = "", Stage = "";
        internal bool Pending, Committed;
    }
    // Read-only bounded discovery. The transaction re-verifies every field before any recovery mutation.
    internal static class UpdateRecovery
    {
        private static bool Marker(WindowsFileFence fence, string path, string hash) {
            try { using (var file = fence.OpenFile(path)) {
                if (Encoding.ASCII.GetString(UpdateTicket.Read(file, 128)) != hash + "\n") throw Bad(); return true;
            } } catch (ApplySafetyException error) when (error.NativeError == 2) { return false; }
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
        internal static List<RecoveryEntry> Scan(WindowsFileFence fence) {
            // Retained terminal evidence keeps its original path/object identities. Only
            // unfinished preparation/recovery consumes the bounded active inventory.
            var result = new List<RecoveryEntry>(); var active = 0;
            foreach (var dir in UpdateWorkPath.Directories(fence, "txn")) {
                var work = dir; if (!UpdateWorkPath.Is(work, "txn")) throw Bad(); fence.AssertPrivateDirectory(work);
                byte[] bytes;
                try { using (var file = fence.OpenFile(work + "/plan")) bytes = UpdateTicket.Read(file, 16384); }
                catch (ApplySafetyException error) when (error.NativeError == 2) { if (++active > 128) throw Bad(); continue; }
                var lines = Encoding.ASCII.GetString(bytes).Split('\n');
                if (lines.Length != InstallReceipt.Owned.Length + 13 || lines[0] != UpdateTransaction.Header || lines[1] != "root=" + fence.RootIdentity.Text
                    || lines[2] != "work=" + work || lines[3] != "work-id=" + fence.DirectoryIdentity(work).Text || lines[8] != "count=" + (InstallReceipt.Owned.Length + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) || lines[lines.Length - 1] != "") throw Bad();
                if (!Regex.IsMatch(lines[11 + InstallReceipt.Owned.Length], @"\Asettings=(?:-\t-|[0-9A-F]{8}:[0-9A-F]{16}\t[0-9A-F]{64})\z")) throw Bad();
                if (!lines[4].StartsWith("stage=", StringComparison.Ordinal) || !UpdateWorkPath.Is(lines[4].Substring(6), "stage") || !Regex.IsMatch(lines[6], @"\Adescriptor=[0-9A-F]{64}\z")) throw Bad();
                var stage = lines[4].Substring(6); fence.AssertPrivateDirectory(stage); if (lines[5] != "stage-id=" + fence.DirectoryIdentity(stage).Text) throw Bad();
                var hash = UpdateTrust.Hash(bytes); var committed = Marker(fence, work + "/committed", hash); var rolled = Marker(fence, work + "/rolled-back", hash); var intent = Marker(fence, work + "/commit-intent", hash);
                if (committed && (!intent || rolled) || rolled && intent) throw Bad();
                if (!committed && !rolled && ++active > 128) throw Bad();
                result.Add(new RecoveryEntry { Work = work, Stage = stage, Descriptor = lines[6].Substring(11), Pending = !committed && !rolled, Committed = committed });
            }
            return result;
        }
        internal static RecoveryEntry? PendingWork(WindowsFileFence fence) { return UpdateRecoveryPolicy.Pending(Scan(fence)); }
        // Context for recovering a pending transaction: rebuilt from its own signed stage and the
        // original receipt, never from a saved preference or a later download.
        internal static UpdateContext RecoveryContext(WindowsFileFence fence, RecoveryEntry entry, UpdateTrust trust)
        {
            byte[] descriptor, signature, key;
            using (var file = fence.OpenFile(entry.Stage + "/descriptor.bin")) descriptor = UpdateTicket.Read(file, UpdateTrust.MaxDescriptorBytes);
            using (var file = fence.OpenFile(entry.Stage + "/signature.bin")) signature = UpdateTicket.Read(file, UpdateTrust.MaxSignatureBytes);
            using (var file = fence.OpenFile(entry.Stage + "/key-id.bin")) key = UpdateTicket.Read(file, 40);
            var fields = Encoding.ASCII.GetString(descriptor).Split('\n');
            long release, asset, repository;
            if (fields.Length < 14 || fields[0] != UpdateTrust.DescriptorHeader || !fields[1].StartsWith("repository=", StringComparison.Ordinal) || !fields[2].StartsWith("version=", StringComparison.Ordinal)
                || !fields[3].StartsWith("channel=", StringComparison.Ordinal) || !fields[5].StartsWith("release-id=", StringComparison.Ordinal) || !fields[6].StartsWith("asset-id=", StringComparison.Ordinal)
                || !fields[12].StartsWith("repository-id=", StringComparison.Ordinal)
                || !long.TryParse(fields[5].Substring(11), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out release)
                || !long.TryParse(fields[6].Substring(9), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out asset)
                || !long.TryParse(fields[12].Substring(14), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out repository)) throw Bad();
            var channel = fields[3].Substring(8);
            if (fields[11] == "purpose=" + ReleaseFeedPolicy.PublicPurpose && UpdateVersion.Parse(fields[2].Substring(8)).IsPrerelease) channel = "PreRelease";
            var proposal = new UpdateContext(fields[1].Substring(11), channel, "0.0.0", fields[2].Substring(8), release, asset, UpdateTrust.PlatformId, repositoryId: repository);
            var verified = trust.VerifyProposal(descriptor, Encoding.ASCII.GetString(key), signature, proposal);
            if (verified.DescriptorSha256 != entry.Descriptor) throw Bad();
            ChannelTransitionIntent? transition = null;
            try { using (var file = fence.OpenFile(entry.Stage + "/transition.bin")) transition = ChannelTransitionIntent.Parse(UpdateTicket.Read(file, 2048)); }
            catch (ApplySafetyException error) when (error.NativeError == 2) { }
            return transition == null ? proposal : new UpdateContext(proposal.Repository, proposal.Channel, transition.CurrentVersion, proposal.ReleaseTag, release, asset, UpdateTrust.PlatformId, repositoryId: repository, transition: transition);
        }
    }
}
