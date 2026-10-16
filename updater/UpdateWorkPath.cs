#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Paths remain relative to the selected game fence; this is not a second game root.
    internal static class UpdateWorkPath
    {
        internal const string Root = "BetterAstralParty.Update";
        internal const string OptionalRootPattern = @"(?:BetterAstralParty\.Update/)?";
        private static bool Kind(string kind) => Regex.IsMatch(kind, @"\A(?:stage|txn|helper|helperauto|helperbridge|wholeintent|wholehist)\z");
        internal static bool Is(string relative, string kind) => Kind(kind) && Regex.IsMatch(relative, @"\A" + OptionalRootPattern + "bap-v4-" + kind + @"-[0-9a-f]{32}\z");
        internal static bool IsStage(string relative) => Is(relative, "stage");
        internal static bool IsContained(string relative, string kind) => relative.StartsWith(Root + "/", StringComparison.Ordinal) && Is(relative, kind);
        internal static string Operation(string relative, string kind)
        {
            if (!Is(relative, kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return relative.Substring(relative.Length - 32);
        }
        internal static string At(string kind, string operation)
        {
            if (!Kind(kind) || !Regex.IsMatch(operation, @"\A[0-9a-f]{32}\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return Root + "/bap-v4-" + kind + "-" + operation;
        }
        internal static string New(string kind) => At(kind, Guid.NewGuid().ToString("N"));
        internal static string Transition(string operation)
        {
            if (!Regex.IsMatch(operation, @"\A[0-9a-f]{32}\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return Root + "/bap-v4-transition-" + operation;
        }
        internal static string TransitionFor(string stage, string operation) => IsContained(stage, "stage") ? Transition(operation)
            : IsStage(stage) && Regex.IsMatch(operation, @"\A[0-9a-f]{32}\z") ? "bap-v4-transition-" + operation : throw new ApplySafetyException(ApplyFailure.UnsafePath);
        internal static string WholeHistory(string relative)
        {
            var operation = Operation(relative, "wholeintent");
            return IsContained(relative, "wholeintent") ? At("wholehist", operation) : "bap-v4-wholehist-" + operation;
        }
        internal static string LogicalIntent(string relative)
        {
            var kind = Is(relative, "wholeintent") ? "wholeintent" : "wholehist";
            var operation = Operation(relative, kind);
            return IsContained(relative, kind) ? At("wholeintent", operation) : "bap-v4-wholeintent-" + operation;
        }
        internal static void RequireContained(string relative, string kind)
        {
            if (IsContained(relative, kind)) return;
            if (!Is(relative, kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            var error = new ApplySafetyException(ApplyFailure.RecoveryRequired);
            error.Data["BapRecoveryReason"] = "LegacyFlatWorkPending";
            throw error;
        }
        private static bool Container(WindowsFileFence fence, bool create)
        {
            try { fence.DirectoryIdentity(Root); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3)
            {
                if (!create) return false;
                try { fence.CreatePrivateDirectory(Root); }
                catch (ApplySafetyException raced) when (raced.NativeError == 80 || raced.NativeError == 183) { fence.DirectoryIdentity(Root); }
            }
            fence.AssertPrivateDirectory(Root); fence.Recheck(); return true;
        }
        internal static WindowsFileIdentity Create(WindowsFileFence fence, string relative)
        {
            var valid = false;
            foreach (var kind in new[] { "stage", "txn", "helper", "helperauto", "helperbridge", "wholeintent", "wholehist" }) valid |= IsContained(relative, kind);
            if (!valid) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            Container(fence, true); return fence.CreatePrivateDirectory(relative);
        }
        internal static IEnumerable<string> Directories(WindowsFileFence fence, string kind)
        {
            if (!Kind(kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            // Read retained flat evidence in place. No caller may mutate a flat pending operation.
            foreach (var directory in WindowsPath.Directories(fence.Root, "bap-v4-" + kind + "-*"))
            {
                var relative = WindowsPath.Leaf(directory);
                if (!Is(relative, kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                yield return relative;
            }
            if (!Container(fence, false)) yield break;
            foreach (var directory in WindowsPath.Directories(fence.Full(Root), "bap-v4-" + kind + "-*"))
            {
                var relative = Root + "/" + WindowsPath.Leaf(directory);
                if (!IsContained(relative, kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                yield return relative;
            }
            fence.Recheck();
        }
    }
}
