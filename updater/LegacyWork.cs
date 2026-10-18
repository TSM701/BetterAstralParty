#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
namespace BetterAstralParty.Updating
{
    // Finished 1.3.x (protocol 4) update work under BetterAstralParty.Update: a flat bap-v4-* folder whose
    // plan is closed by a committed, rolled-back or cancelled marker. Unfinished or unexpected folders stay.
    internal static class LegacyWork
    {
        private static readonly Regex Name = new Regex(@"\Abap-v4-[a-z]{1,16}-[0-9a-f]{32}\z", RegexOptions.CultureInvariant);
        private static readonly string[] Terminal = { "committed", "rolled-back", "cancelled" };
        internal static List<string> Finished(string gameRoot)
        {
            var result = new List<string>();
            var container = WindowsPath.Absolute(gameRoot + "\\" + UpdateWorkPath.Root);
            if (!Directory.Exists(WindowsPath.ForIo(container))) return result;
            foreach (var directory in WindowsPath.Directories(container, "bap-v4-*")) {
                var leaf = WindowsPath.Leaf(directory); var io = WindowsPath.ForIo(directory);
                if (!Name.IsMatch(leaf) || Directory.GetDirectories(io).Length != 0 || !File.Exists(Path.Combine(io, "plan"))) continue;
                if (Array.Exists(Terminal, marker => File.Exists(Path.Combine(io, marker)))) result.Add(UpdateWorkPath.Root + "/" + leaf);
            }
            return result;
        }
        // Files go by held handle, then the folder only when empty and still the same object. A folder that
        // cannot be removed is reported and kept; this cleanup never blocks the maintenance it follows.
        internal static int Remove(string gameRoot, Action<string> output)
        {
            var removed = 0;
            foreach (var relative in Finished(gameRoot)) {
                try {
                    using (var fence = new WindowsFileFence(gameRoot)) {
                        var identity = fence.DirectoryIdentity(relative).Text;
                        foreach (var file in Directory.GetFiles(WindowsPath.ForIo(fence.Full(relative)))) {
                            var name = relative + "/" + WindowsPath.Leaf(WindowsPath.FromIo(file));
                            using (var held = fence.OpenFile(name, true)) held.RemoveOrdinaryRecoveryTarget(name);
                        }
                        fence.RemoveEmptyRecoveryDirectory(relative, identity);
                    }
                    removed++;
                }
                catch (Exception error) when (error is ApplySafetyException || error is IOException || error is UnauthorizedAccessException) {
                    output("1.3.x 작업 기록을 남겨 둡니다 / Kept 1.3.x work record: " + relative + " reason=" + error.GetType().Name);
                }
            }
            if (removed != 0) output("끝난 1.3.x 업데이트 작업 기록 " + removed + "개를 정리했습니다. / Removed " + removed + " finished 1.3.x update work records.");
            return removed;
        }
    }
}
