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
            var result = new List<RecoveryEntry>(); var dirs = Directory.GetDirectories(fence.Root, "bap-txn-*", SearchOption.TopDirectoryOnly);
            if (dirs.Length > 128) throw Bad();
            foreach (var dir in dirs) {
                var work = Path.GetFileName(dir); if (!Regex.IsMatch(work, @"\Abap-txn-[0-9a-f]{32}\z")) throw Bad(); fence.AssertPrivateDirectory(work);
                byte[] bytes;
                try { using (var file = fence.OpenFile(work + "/plan")) bytes = UpdateTicket.Read(file, 16384); }
                catch (ApplySafetyException error) when (error.NativeError == 2) { continue; }
                var lines = Encoding.ASCII.GetString(bytes).Split('\n');
                var v2 = lines.Length == 21 && lines[0] == "BetterAstralParty.Transaction/v2";
                if (!v2 && (lines.Length != 20 || lines[0] != "BetterAstralParty.Transaction/v1") || lines[1] != "root=" + fence.RootIdentity.Text
                    || lines[2] != "work=" + work || lines[3] != "work-id=" + fence.DirectoryIdentity(work).Text || lines[8] != "count=10" || lines[lines.Length - 1] != "") throw Bad();
                if (v2 && !Regex.IsMatch(lines[19], @"\Asettings=(?:-\t-|[0-9A-F]{8}:[0-9A-F]{16}\t[0-9A-F]{64})\z")) throw Bad();
                if (!Regex.IsMatch(lines[4], @"\Astage=bap-stage-[0-9a-f]{32}\z") || !Regex.IsMatch(lines[6], @"\Adescriptor=[0-9A-F]{64}\z")) throw Bad();
                var stage = lines[4].Substring(6); fence.AssertPrivateDirectory(stage); if (lines[5] != "stage-id=" + fence.DirectoryIdentity(stage).Text) throw Bad();
                var hash = UpdateTrust.Hash(bytes); var committed = Marker(fence, work + "/committed", hash); var rolled = Marker(fence, work + "/rolled-back", hash); var intent = Marker(fence, work + "/commit-intent", hash);
                if (committed && (!intent || rolled) || rolled && intent) throw Bad();
                result.Add(new RecoveryEntry { Work = work, Stage = stage, Descriptor = lines[6].Substring(11), Pending = !committed && !rolled, Committed=committed });
            }
            return result;
        }
        internal static string? PendingTicket(WindowsFileFence fence, UpdateFault? operation = null, UpdateFault? failure = null) {
            RecoveryEntry? selected = null;
            foreach (var entry in Scan(fence)) if (entry.Pending) { if (selected != null) throw Bad(); selected = entry; }
            if (selected == null) return null;
            // Apply creates a separate payload stage. Link the HANDOFF to its exact current
            // transaction WORK via helper.attempt, rather than treating plan.Stage as a ticket.
            string? current = null;
            var dirs = Directory.GetDirectories(fence.Root, "bap-stage-*", SearchOption.TopDirectoryOnly); if (dirs.Length > 256) throw Bad();
            foreach (var dir in dirs) {
                var stage = Path.GetFileName(dir); if (!Regex.IsMatch(stage, @"\Abap-stage-[0-9a-f]{32}\z")) throw Bad();
                try { using (var file = fence.OpenFile(stage + "/handoff.ticket")) {
                    var bytes = UpdateTicket.Read(file, UpdateTicket.MaxBytes); var ticket = UpdateTicket.Parse(bytes); ticket.Recheck(fence);
                    if (ticket.Stage != stage) throw Bad(); if (ticket.DescriptorHash != selected.Descriptor) continue;
                    var work = HelperRecoveryBinding.ReadAttempt(fence, ticket, UpdateTrust.Hash(bytes));
                    if (work != null ? work != selected.Work : ticket.Context.RepositoryId > 0 || operation?.Stage != stage && failure?.Stage != stage) continue;
                    if (operation != null) HelperRecoveryBinding.RequireOwner(fence, ticket, operation);
                    if (failure != null) HelperRecoveryBinding.RequireLatch(fence, ticket, failure);
                    if (current != null) throw Bad(); current = stage;
                } } catch (ApplySafetyException error) when (error.NativeError == 2) { }
            }
            return current ?? throw Bad();
        }
    }
}
