#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Clean-reinstall records earlier versions left in the game root move to UpdateWorkPath.History: a pair whose generation
    // record names this root and whose archive still holds the recorded bytes (or is already gone), never over an existing
    // file. Anything else stays, and the move never blocks the install or removal it follows.
    internal static class ReinstallHistory
    {
        // move false only counts what a move would take, for CheckOnly and WhatIf.
        internal static int Move(string gameRoot, Action<string>? output, bool move = true)
        {
            var moved = 0;
            try
            {
                using (var fence = new WindowsFileFence(gameRoot))
                    foreach (var path in Directory.EnumerateFiles(WindowsPath.ForIo(fence.Root), UpdateWorkPath.ReinstallRecordPrefix + "*", SearchOption.TopDirectoryOnly))
                    {
                        var name = WindowsPath.Leaf(WindowsPath.FromIo(path));
                        if (!Regex.IsMatch(name, @"\A" + Regex.Escape(UpdateWorkPath.ReinstallRecordPrefix) + @"[0-9a-f]{32}\z")) continue;
                        try { if (Pair(fence, name, name.Substring(UpdateWorkPath.ReinstallRecordPrefix.Length), move)) moved++; }
                        catch (Exception error) when (error is ApplySafetyException || error is IOException || error is UnauthorizedAccessException || error is System.Text.DecoderFallbackException) { }
                    }
            }
            catch (Exception error) when (error is ApplySafetyException || error is IOException || error is UnauthorizedAccessException) { }
            if (move && moved != 0) output?.Invoke("이전 재설치 기록 " + moved + "개를 BetterAstralParty.Update\\history로 옮겼습니다. / Moved " + moved + " earlier reinstall records to BetterAstralParty.Update\\history.");
            return moved;
        }
        private static bool Pair(WindowsFileFence fence, string name, string nonce, bool move)
        {
            var archive = UpdateTransaction.HighWaterPath + ".uninstalled-" + nonce;
            if (File.Exists(WindowsPath.ForIo(fence.Full(UpdateWorkPath.History + "/" + name))) || File.Exists(WindowsPath.ForIo(fence.Full(UpdateWorkPath.History + "/" + archive)))) return false;
            using (var record = fence.OpenFile(name, move))
            {
                var value = new InstallReceipt.Json(UpdateTicket.Read(record, 4096)).Parse() as Dictionary<string, object?>;
                string Field(string key) => value != null && value.TryGetValue(key, out var item) && item is string text ? text : "";
                if (Field("purpose") != "clean-reinstall-generation" || Field("root") != fence.RootIdentity.Text || Field("archive") != archive) return false;
                WindowsFileFence.FileLease? water = null;
                try { water = fence.OpenFile(archive, move); }
                catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { }
                using (water)
                {
                    if (water != null && water.Hash() != Field("waterSha256")) return false;
                    if (!move) return true;
                    UpdateWorkPath.EnsureHistory(fence);
                    water?.RenameTo(fence, UpdateWorkPath.History + "/" + archive);
                    record.RenameTo(fence, UpdateWorkPath.History + "/" + name);
                }
            }
            return true;
        }
    }
}
