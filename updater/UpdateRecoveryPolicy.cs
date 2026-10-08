#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    internal enum RecoveryDirection { RestoreOriginal, CompleteTarget }
    internal enum RecoveryOutcome { NoChanges, OriginalRestored, TargetCompleted }
    // Pure selection/state rules. They do not authenticate journals or grant file writes.
    // Recover must verify signatures, root/file identities and hashes before using them.
    internal static class UpdateRecoveryPolicy
    {
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
        internal static RecoveryDirection Direction(int[] states, bool intent, bool committed, bool rolled)
        {
            if (states.Length != 10 || committed && !intent || rolled && intent) throw Bad();
            foreach (var state in states) if (state < 0 || state > 2) throw Bad();
            if (intent) {
                for (var i = 0; i < 9; i++) if (states[i] != 2) throw Bad();
                if (committed && states[9] != 2) throw Bad();
                return RecoveryDirection.CompleteTarget;
            }
            if (states[9] != 0) throw Bad();
            if (rolled) foreach (var state in states) if (state != 0) throw Bad();
            return RecoveryDirection.RestoreOriginal;
        }
        internal static RecoveryEntry? Select(List<RecoveryEntry> entries, string descriptor, string? work, bool legacy)
        {
            RecoveryEntry? selected = null;
            if (work != null && !Regex.IsMatch(work, @"\Abap-txn-[0-9a-f]{32}\z")) throw Bad();
            foreach (var entry in entries) {
                if (work != null && entry.Work == work || work == null && legacy && entry.Pending && entry.Descriptor == descriptor) {
                    if (selected != null || entry.Descriptor != descriptor) throw Bad(); selected = entry;
                }
            }
            foreach (var entry in entries) if (entry.Pending && !ReferenceEquals(entry, selected)) throw Bad();
            // Completed history cannot stand in for the current attempt. A production
            // ticket with pending work requires its durable exact attempt binding.
            return selected;
        }
        internal static RecoveryOutcome Outcome(TransactionResult result)
        { if (result == TransactionResult.Committed) return RecoveryOutcome.TargetCompleted;
            if (result == TransactionResult.RolledBack) return RecoveryOutcome.OriginalRestored;
            if (result == TransactionResult.Abandoned) return RecoveryOutcome.NoChanges; throw Bad(); }
    }
    // Ticket-bound diagnostics only. Neither record authorizes a rollback/downgrade.
    internal static class HelperRecoveryBinding
    {
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
        internal static void RequireOwner(WindowsFileFence fence, UpdateTicket ticket, UpdateFault owner) {
            if (owner.Root != fence.RootIdentity.Text || owner.Root != ticket.RootId || owner.Stage != ticket.Stage || !UpdateFaults.Same(owner.Context, ticket.Context)
                || owner.GamePid != ticket.GamePid || owner.GameCreation != ticket.GameCreation || owner.GameSession != ticket.GameSession
                || owner.LauncherPid != ticket.LauncherPid || owner.LauncherCreation != ticket.LauncherCreation || owner.LauncherSession != ticket.LauncherSession) throw Bad();
        }
        internal static void RequireLatch(WindowsFileFence fence, UpdateTicket ticket, UpdateFault fault) {
            if (fault.Root != fence.RootIdentity.Text || fault.Stage != ticket.Stage || !UpdateFaults.Same(fault.Context, ticket.Context)) throw Bad();
            // UI failure recording without an active owner may have no actor tuple. It remains
            // a latch only; an operation guard always requires the complete exact tuple.
            if (fault.GamePid != 0) RequireOwner(fence, ticket, fault);
        }
        internal static byte[] Attempt(string ticketHash, string work)
        {
            if (!Regex.IsMatch(ticketHash, @"\A[0-9A-F]{64}\z") || !Regex.IsMatch(work, @"\Abap-txn-[0-9a-f]{32}\z")) throw Bad();
            return Encoding.ASCII.GetBytes("BetterAstralParty.HelperAttempt/v1\nticket=" + ticketHash + "\nwork=" + work + "\n");
        }
        internal static string ParseAttempt(byte[] bytes, string ticketHash)
        {
            var lines = Encoding.ASCII.GetString(bytes).Split('\n');
            if (bytes.Length > 256 || lines.Length != 4 || lines[0] != "BetterAstralParty.HelperAttempt/v1" || lines[1] != "ticket=" + ticketHash || !lines[2].StartsWith("work=", StringComparison.Ordinal) || lines[3] != "") throw Bad();
            var work = lines[2].Substring(5); if (UpdateTrust.Hash(Attempt(ticketHash, work)) != UpdateTrust.Hash(bytes)) throw Bad(); return work;
        }
        internal static string? ReadAttempt(WindowsFileFence fence, UpdateTicket ticket, string ticketHash)
        {
            try { using (var file = fence.OpenFile(ticket.Stage + "/helper.attempt")) return ParseAttempt(UpdateTicket.Read(file, 256), ticketHash); }
            catch (ApplySafetyException error) when (error.NativeError == 2) { return null; }
        }
        internal static void RequireNoUntrackedWork(WindowsFileFence fence, string? work)
        {
            if (work == null) return;
            if (!Regex.IsMatch(work, @"\Abap-txn-[0-9a-f]{32}\z")) throw Bad();
            try { fence.DirectoryIdentity(work); }
            catch (ApplySafetyException error) when (error.NativeError == 2) { return; }
            // A missing journal in an existing attempt directory is uncertain. Retain
            // the guard/backups rather than certifying no changes from absence alone.
            throw Bad();
        }
        internal static byte[] Resolution(string ticketHash, string? work, RecoveryOutcome outcome)
        {
            if (!Enum.IsDefined(typeof(RecoveryOutcome), outcome)) throw Bad();
            var attempt = work == null ? "-" : work;
            if (!Regex.IsMatch(ticketHash, @"\A[0-9A-F]{64}\z") || work != null && !Regex.IsMatch(work, @"\Abap-txn-[0-9a-f]{32}\z") || work == null && outcome != RecoveryOutcome.NoChanges) throw Bad();
            return Encoding.ASCII.GetBytes("BetterAstralParty.HelperRecovery/v1\nticket=" + ticketHash + "\nwork=" + attempt + "\noutcome=" + outcome + "\n");
        }
        internal static RecoveryOutcome ParseResolution(byte[] bytes, string ticketHash, string? work)
        {
            var lines = Encoding.ASCII.GetString(bytes).Split('\n'); RecoveryOutcome outcome;
            if (bytes.Length > 320 || lines.Length != 5 || !lines[3].StartsWith("outcome=", StringComparison.Ordinal) || !Enum.TryParse(lines[3].Substring(8), out outcome)) throw Bad();
            if (UpdateTrust.Hash(Resolution(ticketHash, work, outcome)) != UpdateTrust.Hash(bytes)) throw Bad(); return outcome;
        }
        internal static void WriteResolution(WindowsFileFence fence, UpdateTicket ticket, string hash, string? work, RecoveryOutcome outcome)
        {
            var bytes = Resolution(hash, work, outcome);
            try { UpdateTicket.Write(fence, ticket.Stage + "/helper.recovery", bytes); }
            catch (ApplySafetyException error) when (error.NativeError == 80 || error.NativeError == 183) {
                using (var file = fence.OpenFile(ticket.Stage + "/helper.recovery")) if (UpdateTrust.Hash(UpdateTicket.Read(file, 320)) != UpdateTrust.Hash(bytes)) throw Bad();
            }
        }
    }
}
