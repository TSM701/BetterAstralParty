#nullable enable
using System;
using System.Collections.Generic;

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
            if (states.Length != InstallReceipt.Owned.Length + 2 || committed && !intent || rolled && intent) throw Bad();
            foreach (var state in states) if (state < 0 || state > 2) throw Bad();
            if (intent) {
                for (var i = 0; i < states.Length - 1; i++) if (states[i] != 2) throw Bad();
                if (committed && states[states.Length - 1] != 2) throw Bad();
                return RecoveryDirection.CompleteTarget;
            }
            if (states[states.Length - 1] != 0) throw Bad();
            if (rolled) foreach (var state in states) if (state != 0) throw Bad();
            return RecoveryDirection.RestoreOriginal;
        }
        // At most one unfinished transaction may exist; anything else needs manual recovery.
        internal static RecoveryEntry? Pending(List<RecoveryEntry> entries)
        {
            RecoveryEntry? selected = null;
            foreach (var entry in entries) if (entry.Pending) { if (selected != null) throw Bad(); selected = entry; }
            return selected;
        }
        internal static RecoveryOutcome Outcome(TransactionResult result)
        { if (result == TransactionResult.Committed) return RecoveryOutcome.TargetCompleted;
            if (result == TransactionResult.RolledBack) return RecoveryOutcome.OriginalRestored;
            if (result == TransactionResult.Abandoned) return RecoveryOutcome.NoChanges; throw Bad(); }
    }
}
