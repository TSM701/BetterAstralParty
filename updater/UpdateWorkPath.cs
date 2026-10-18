#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Paths remain relative to the selected game fence; this is not a second game root.
    // Protocol 5 work lives only under the container; protocol-4 (bap-v4-*) evidence is never matched.
    internal static class UpdateWorkPath
    {
        internal const string Root = "BetterAstralParty.Update";
        internal const string Prefix = "bap-v5-";
        private static bool Kind(string kind) => kind == "stage" || kind == "txn";
        // Anchored regex for one contained work directory of the given kind.
        internal static string Pattern(string kind) => @"\ABetterAstralParty\.Update/" + Prefix + kind + @"-[0-9a-f]{32}\z";
        internal static bool Is(string relative, string kind) => Kind(kind) && Regex.IsMatch(relative, Pattern(kind));
        internal static bool IsStage(string relative) => Is(relative, "stage");
        internal static bool IsContained(string relative, string kind) => Is(relative, kind);
        internal static string Operation(string relative, string kind)
        {
            if (!Is(relative, kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return relative.Substring(relative.Length - 32);
        }
        internal static string At(string kind, string operation)
        {
            if (!Kind(kind) || !Regex.IsMatch(operation, @"\A[0-9a-f]{32}\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return Root + "/" + Prefix + kind + "-" + operation;
        }
        internal static string New(string kind) => At(kind, Guid.NewGuid().ToString("N"));
        internal static void RequireContained(string relative, string kind)
        {
            if (!Is(relative, kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
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
            if (!Is(relative, "stage") && !Is(relative, "txn")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            Container(fence, true); return fence.CreatePrivateDirectory(relative);
        }
        internal static IEnumerable<string> Directories(WindowsFileFence fence, string kind)
        {
            if (!Kind(kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            if (!Container(fence, false)) yield break;
            foreach (var directory in WindowsPath.Directories(fence.Full(Root), Prefix + kind + "-*"))
            {
                var relative = Root + "/" + WindowsPath.Leaf(directory);
                if (!Is(relative, kind)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                yield return relative;
            }
            fence.Recheck();
        }
    }
}
