#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    internal static class NativeMaintenance
    {
        internal sealed class Entry
        {
            public string Target { get; set; } = "";
            public bool Existed { get; set; }
            public string Backup { get; set; } = "";
            public string? Hash { get; set; }
        }
        internal sealed class Transaction
        {
            internal string JournalRoot = "", JournalPath = "", JournalHash = "", BackupDir = "";
            internal readonly List<Entry> Entries = new List<Entry>();
        }
        internal sealed class InstallLock : IDisposable
        {
            private readonly WindowsFileFence _fence;
            internal readonly WindowsFileFence.FileLease File;
            internal readonly string Root;
            internal InstallLock(string root)
            {
                Root = root; _fence = new WindowsFileFence(root);
                try {
                    try { File = _fence.OpenFile("pending.json.lock", true); }
                    catch (ApplySafetyException error) when (error.NativeError == 2) { File = _fence.OpenFile("pending.json.lock", true, true); }
                } catch { _fence.Dispose(); throw; }
            }
            public void Dispose() { File.Dispose(); _fence.Dispose(); }
        }
        internal static string JournalRoot(string gameRoot)
        {
#if BAP_SAFETY_FIXTURE
            var local = WindowsPath.Parent(WindowsPath.Absolute(gameRoot));
#else
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#endif
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Local application-data directory unavailable.");
            var key = UpdateTrust.Hash(Encoding.UTF8.GetBytes(WindowsPath.Absolute(gameRoot).TrimEnd('\\').ToLowerInvariant()));
            return WindowsPath.Absolute(local.TrimEnd('\\') + "\\BetterAstralParty\\Backups\\v5\\" + key);
        }
        // 1.3.x (protocol 4) journals; an unfinished one blocks takeover instead of being replayed.
        internal static string LegacyJournalPath(string gameRoot)
        {
            var root = JournalRoot(gameRoot); return WindowsPath.Parent(WindowsPath.Parent(root)) + "\\v4\\" + WindowsPath.Leaf(root) + "\\pending.json";
        }
        internal static string JournalPath(string gameRoot) { return JournalRoot(gameRoot) + "\\pending.json"; }
        internal static bool HasPending(NativeMaintenanceBundle bundle)
        {
            var root = JournalRoot(bundle.GameRoot); if (!Directory.Exists(WindowsPath.ForIo(root))) return false;
            using (var fence = new WindowsFileFence(root)) return Maybe(fence, "pending.json") != null;
        }
        private static string? Maybe(WindowsFileFence fence, string relative)
        {
            try { using (var file = fence.OpenFile(relative)) return file.Hash(); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
        }
        internal static string Relative(string root, string absolute)
        {
            root = WindowsPath.Absolute(root).TrimEnd('\\'); absolute = WindowsPath.Absolute(absolute);
            if (!absolute.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Target outside selected root.");
            return absolute.Substring(root.Length + 1).Replace('\\', '/');
        }
        internal static void AssertTarget(string gameRoot, string target)
        {
            using (var fence = new WindowsFileFence(gameRoot)) Maybe(fence, Relative(gameRoot, target));
        }
        internal static InstallLock AcquireInstallLock(NativeMaintenanceBundle bundle)
        {
            var root = JournalRoot(bundle.GameRoot); SetupHost.ValidateMaintenancePaths(bundle.GameRoot, root);
#if BAP_SAFETY_FIXTURE
            var local = WindowsPath.Parent(bundle.GameRoot);
#else
            var local = WindowsPath.Absolute(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
#endif
            using (var parent = new WindowsFileFence(local)) parent.EnsureDirectory(Relative(local, root));
            bundle.AssertRoot(); return new InstallLock(root);
        }
        internal static string[] RecoveryTargets(NativeMaintenanceBundle bundle)
        {
            var names = new List<string>(bundle.Expected.Keys);
            names.AddRange(new[] { "BepInEx/config/BepInEx.cfg", "doorstop_config.ini", InstallReceipt.Path,
                UpdateTransaction.HighWaterPath, OrdinaryInstallRecovery.LegacyLauncher, OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyHelperReceipt, "BepInEx/config/kr.betterastralparty.mod.cfg",
                "BepInEx/BetterAstralParty-Diagnostics/current.log", "BepInEx/BetterAstralParty-Diagnostics/previous.log",
                "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log", "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log",
                "BetterAstralParty-Compatibility.log", "BetterAstralParty-LaunchError.log" });
            // Previously journaled loader documents remain recoverable; new plans never consume them.
            names.AddRange(bundle.Hashes.Keys.Where(n => n.StartsWith(".deps/bepinex/", StringComparison.Ordinal)).Select(n => n.Substring(14)));
            return names.Distinct(StringComparer.OrdinalIgnoreCase).Select(n => WindowsPath.Absolute(bundle.GameRoot + "\\" + n.Replace('/', '\\'))).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        internal static void TestWritable(string[] targets)
        {
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets) {
                var parent = WindowsPath.Parent(WindowsPath.Absolute(target));
                while (!Directory.Exists(WindowsPath.ForIo(parent))) parent = WindowsPath.Parent(parent);
                using (var fence = new WindowsFileFence(parent)) {
                    try { using (fence.OpenFile(Relative(parent, target), true)) { } }
                    catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { }
                }
                parents.Add(parent);
            }
            foreach (var parent in parents) using (var fence = new WindowsFileFence(parent)) {
                var name = Guid.NewGuid().ToString("N") + ".bap-write-test";
                using (var probe = fence.OpenFile(name, true, true)) { probe.Stream.WriteByte(0); probe.Flush(); probe.RemoveOrdinaryRecoveryTarget(name); }
            }
        }
        internal static Transaction StartTransaction(NativeMaintenanceBundle bundle, string[] targets, long installBytes)
        {
            var ordered = targets.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            if (ordered.Length < 1 || ordered.Length > 300 || installBytes < 0) throw new InvalidDataException("Recovery target/size bounds exceeded.");
            foreach (var target in ordered) AssertTarget(bundle.GameRoot, target);
            TestWritable(ordered); bundle.AssertRoot();
            var txn = new Transaction { JournalRoot = JournalRoot(bundle.GameRoot), JournalPath = JournalPath(bundle.GameRoot) };
            var id = Guid.NewGuid().ToString("N"); txn.BackupDir = txn.JournalRoot + "\\" + id;
            using (var backup = new WindowsFileFence(txn.JournalRoot)) using (var game = new WindowsFileFence(bundle.GameRoot)) {
                if (Maybe(backup, "pending.json") != null) throw new InvalidDataException("Unfinished installation needs recovery; retain the journal.");
                var held = new List<WindowsFileFence.FileLease>();
                try {
                    long backupBytes = 0;
                    foreach (var target in ordered) {
                        WindowsFileFence.FileLease? file = null;
                        try { file = game.OpenFile(Relative(bundle.GameRoot, target)); held.Add(file); backupBytes = checked(backupBytes + file.Stream.Length); }
                        catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { }
                        txn.Entries.Add(new Entry { Target = target, Existed = file != null, Backup = txn.BackupDir + "\\" + txn.Entries.Count.ToString(CultureInfo.InvariantCulture) + ".bin", Hash = file?.Hash() });
                    }
                    var budgets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in new[] { Tuple.Create(bundle.GameRoot, installBytes), Tuple.Create(txn.JournalRoot, backupBytes) }) {
                        var volume = item.Item1.Substring(0, 3); long old;
                        budgets[volume] = checked((budgets.TryGetValue(volume, out old) ? old : 16L * 1024 * 1024) + item.Item2);
                    }
                    foreach (var budget in budgets) if (new DriveInfo(budget.Key).AvailableFreeSpace < budget.Value) throw new IOException("Not enough free space for installation and rollback backup.");
                    var bytes = MaintenanceJson.Write(new { GameRoot = bundle.GameRoot, Entries = txn.Entries, BackupDir = txn.BackupDir });
                    if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Recovery journal exceeds supported size.");
                    backup.EnsureDirectory(id); var at = 0;
                    foreach (var entry in txn.Entries) {
                        if (entry.Existed) {
                            var source = held[at++]; source.Recheck(); source.Stream.Position = 0;
                            using (var copy = backup.OpenFile(Relative(txn.JournalRoot, entry.Backup), true, true)) { source.Stream.CopyTo(copy.Stream); copy.Flush(); if (copy.Hash() != entry.Hash || source.Hash() != entry.Hash) throw new IOException("File changed during backup."); }
                        } else if (Maybe(game, Relative(bundle.GameRoot, entry.Target)) != null) throw new IOException("File appeared during backup.");
                    }
                    using (var journal = backup.OpenFile(id + "/pending.json", true, true)) {
                        journal.Stream.Write(bytes, 0, bytes.Length); journal.Flush(); txn.JournalHash = journal.Hash(); journal.RenameTo(backup, "pending.json");
                    }
                    return txn;
                } finally { foreach (var file in held) file.Dispose(); }
            }
        }
        internal static Transaction ReadTransaction(NativeMaintenanceBundle bundle, string[] targets, string? expectedHash = null)
        {
            var txn = new Transaction { JournalRoot = JournalRoot(bundle.GameRoot), JournalPath = JournalPath(bundle.GameRoot) };
            using (var backup = new WindowsFileFence(txn.JournalRoot)) using (var journal = backup.OpenFile("pending.json")) {
                txn.JournalHash = journal.Hash(); if (expectedHash != null && txn.JournalHash != expectedHash) throw new InvalidDataException("Recovery journal changed after confirmation.");
                var state = MaintenanceJson.Read(UpdateTicket.Read(journal, 1024 * 1024));
                if (state.Count != 3 || MaintenanceJson.Text(state, "GameRoot") != bundle.GameRoot) throw new InvalidDataException("Invalid recovery journal root.");
                txn.BackupDir = MaintenanceJson.Text(state, "BackupDir");
                var id = WindowsPath.Leaf(txn.BackupDir);
                if (!Regex.IsMatch(id, @"\A[0-9a-f]{32}\z") || txn.BackupDir != txn.JournalRoot + "\\" + id) throw new InvalidDataException("Invalid recovery backup scope.");
                var entries = MaintenanceJson.List(state, "Entries"); if (entries.Count < 1 || entries.Count > 300) throw new InvalidDataException("Invalid recovery entry count.");
                var allowed = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in entries) {
                    var row = MaintenanceJson.Map(raw); object? exists, hash;
                    if (row.Count != 4 || !row.TryGetValue("Existed", out exists) || !(exists is bool) || !row.TryGetValue("Hash", out hash)) throw new InvalidDataException("Invalid recovery row.");
                    var entry = new Entry { Target = MaintenanceJson.Text(row, "Target"), Backup = MaintenanceJson.Text(row, "Backup"), Existed = (bool)exists, Hash = hash as string };
                    if (WindowsPath.Absolute(entry.Target) != entry.Target || !allowed.Contains(entry.Target) || !seen.Add(entry.Target) || entry.Backup != txn.BackupDir + "\\" + txn.Entries.Count.ToString(CultureInfo.InvariantCulture) + ".bin") throw new InvalidDataException("Invalid recovery entry scope.");
                    AssertTarget(bundle.GameRoot, entry.Target);
                    var observed = Maybe(backup, Relative(txn.JournalRoot, entry.Backup));
                    if (entry.Existed ? entry.Hash == null || !MaintenanceJson.Hash(entry.Hash) || observed != entry.Hash : hash != null || observed != null) throw new InvalidDataException("Recovery backup missing/damaged.");
                    txn.Entries.Add(entry);
                }
                journal.Recheck(); return txn;
            }
        }
        internal static bool RecoverPending(NativeMaintenanceBundle bundle, Func<string, string?> consent, Action<string> output, bool whatIf = false)
        {
            if (!HasPending(bundle)) return true;
            var targets = RecoveryTargets(bundle); var txn = ReadTransaction(bundle, targets);
            if (whatIf) { output("WhatIf: verified pending maintenance requires recovery: " + txn.JournalPath); return false; }
            output("미완료 설치·제거 기록이 있습니다. / Unfinished installation/removal found: " + txn.JournalPath);
            if (consent("저장된 작업 전 파일을 복원할까요? 이후 변경 내용은 덮어씁니다. / Restore saved pre-maintenance files? Later edits will be replaced. [y/N]") != "y") throw new InvalidOperationException("Recovery cancelled; retain journal and backups.");
            NativeSteamMaintenance.WaitGameClosed(bundle.GameRoot, consent, output);
            using (SetupHost.GuardOrdinaryRecovery(bundle.GameRoot)) {
                bundle.AssertRoot(); SetupHost.RestoreOrdinaryInstall(bundle.GameRoot, txn.JournalRoot, txn.JournalHash, targets, "", false, bundle.RootIdentity);
            }
            return true;
        }
        internal static void CompleteTransaction(NativeMaintenanceBundle bundle, Transaction txn, string[] targets)
        { bundle.AssertRoot(); SetupHost.RestoreOrdinaryInstall(bundle.GameRoot, txn.JournalRoot, txn.JournalHash, targets, "", true, bundle.RootIdentity); }
        internal static void RestoreTransaction(NativeMaintenanceBundle bundle, Transaction txn, string[] targets, Dictionary<string, string?> expectedCurrent)
        { bundle.AssertRoot(); SetupHost.RestoreOrdinaryInstall(bundle.GameRoot, txn.JournalRoot, txn.JournalHash, targets, Encoding.UTF8.GetString(MaintenanceJson.Write(expectedCurrent)), false, bundle.RootIdentity); }
    }
}
