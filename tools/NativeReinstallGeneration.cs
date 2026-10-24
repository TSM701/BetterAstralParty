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
    internal static class NativeReinstallGeneration
    {
        internal sealed class Plan
        {
            internal string Version = "", BackupId = "", JournalHash = "", WaterHash = "", RootIdentity = "", WaterIdentity = "";
            internal string GameRoot = "", PackageRoot = "", IncomingVersion = "", ManifestHash = "", BackupRoot = "", BackupRootIdentity = "";
            internal string HelperProof = "", CoreProof = "";
            internal readonly Dictionary<string, string> BackupPins = new Dictionary<string, string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, string> GamePins = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        private sealed class Entry
        {
            internal string Relative = "", Backup = "";
            internal string? Hash;
            internal bool Existed;
        }
        private static readonly string[] Exclusive = {
            "BepInEx/config/kr.betterastralparty.mod.cfg", "BepInEx/BetterAstralParty-Diagnostics/current.log",
            "BepInEx/BetterAstralParty-Diagnostics/previous.log", "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log",
            "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log", "BetterAstralParty-Compatibility.log", "BetterAstralParty-LaunchError.log", ChannelTransitionIntent.AdoptionPath
        };
        private static ApplySafetyException Bad(string reason)
        {
            var error = new ApplySafetyException(ApplyFailure.RecoveryRequired);
            error.Data["BapRecoveryReason"] = reason; return error;
        }
        private static bool Digest(string text) { return Regex.IsMatch(text, @"\A[A-F0-9]{64}\z"); }
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence fence, string relative)
        {
            try { return fence.OpenFile(relative); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
        }
        private static void Absent(WindowsFileFence game)
        {
            foreach (var name in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path, OrdinaryInstallRecovery.LegacyLauncher, OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyHelperReceipt }))
            {
                var path = game.Full(name);
                if (File.Exists(WindowsPath.ForIo(path)) || Directory.Exists(WindowsPath.ForIo(path))) throw Bad("ReinstallOwnedFilesRemain");
                using (Maybe(game, name)) { }
            }
            foreach (var name in new[] { "BetterAstralParty.reinstall-v5.pending", UpdateFaults.OperationPath, "BetterAstralParty.update-v5.failed" })
            {
                using (var file = Maybe(game, name)) if (file != null) throw Bad("ReinstallOperationPending");
                if (Directory.Exists(WindowsPath.ForIo(game.Full(name)))) throw Bad("ReinstallOperationPending");
            }
        }
        private static string Text(Dictionary<string, object?> value, string name)
        { object? item; if (!value.TryGetValue(name, out item) || !(item is string text)) throw Bad("RemovalJournalInvalid"); return text; }
        private static void UniqueKeys(object? value)
        {
            if (value is Dictionary<string, object?> obj)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in obj) { if (!keys.Add(pair.Key)) throw Bad("RemovalJournalDuplicateKey"); UniqueKeys(pair.Value); }
            }
            else if (value is List<object?> array) foreach (var item in array) UniqueKeys(item);
        }
        private static Dictionary<string, object?> Json(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) bytes = bytes.Skip(3).ToArray();
            var value = new InstallReceipt.Json(bytes, 1024 * 1024, 300, WindowsPath.MaximumLength).Parse();
            UniqueKeys(value); return value as Dictionary<string, object?> ?? throw Bad("RemovalJournalInvalid");
        }
        private static byte[] Pin(WindowsFileFence fence, string relative, long limit, Dictionary<string, string> pins)
        {
            using (var file = fence.OpenFile(relative))
            {
                var bytes = UpdateTicket.Read(file, limit); var hash = file.Hash();
                string? prior; if (pins.TryGetValue(relative, out prior) && prior != hash) throw Bad("RemovalEvidenceChanged");
                pins[relative] = hash; return bytes;
            }
        }
        private static Dictionary<string, Entry> Journal(WindowsFileFence game, WindowsFileFence backups, Plan plan)
        {
            var relative = plan.BackupId + "/completed.json";
            var bytes = Pin(backups, relative, 1024 * 1024, plan.BackupPins); plan.JournalHash = plan.BackupPins[relative];
            var state = Json(bytes); var backupDir = backups.Full(plan.BackupId);
            object? entries;
            if (state.Count != 3 || !string.Equals(Text(state, "GameRoot"), game.Root, StringComparison.OrdinalIgnoreCase) || Text(state, "BackupDir") != backupDir
                || !state.TryGetValue("Entries", out entries) || !(entries is List<object?> rows) || rows.Count < 1 || rows.Count > 300)
                throw Bad("RemovalJournalInvalid");
            backups.DirectoryIdentity(plan.BackupId);
            var allowed = new HashSet<string>(InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path, "doorstop_config.ini" }).Concat(Exclusive), StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i] as Dictionary<string, object?>; object? existed, hash;
                if (row == null || row.Count != 4 || !row.TryGetValue("Existed", out existed) || !(existed is bool) || !row.TryGetValue("Hash", out hash))
                    throw Bad("RemovalJournalEntryInvalid");
                var target = Text(row, "Target"); var prefix = game.Root + "\\";
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw Bad("RemovalJournalEntryInvalid");
                var name = target.Substring(prefix.Length).Replace('\\', '/'); var backup = plan.BackupId + "/" + i.ToString(CultureInfo.InvariantCulture) + ".bin";
                if (game.Full(name) != target || !allowed.Contains(name) || result.ContainsKey(name) || Text(row, "Backup") != backups.Full(backup))
                    throw Bad("RemovalJournalEntryInvalid");
                var entry = new Entry { Relative = name, Backup = backup, Existed = (bool)existed };
                if (entry.Existed)
                {
                    if (!(hash is string digest) || !Digest(digest)) throw Bad("RemovalBackupInvalid");
                    using (var file = backups.OpenFile(backup)) if (file.Hash() != digest) throw Bad("RemovalBackupChanged");
                    entry.Hash = digest; plan.BackupPins.Add(backup, digest);
                }
                else
                {
                    using (var file = Maybe(backups, backup)) if (file != null) throw Bad("RemovalBackupUnexpected");
                    if (hash != null || !InstallReceipt.Owned.Contains(name, StringComparer.Ordinal)) throw Bad("RemovalBackupMissingCore");
                }
                result.Add(name, entry);
            }
            foreach (var name in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path }))
                if (!result.ContainsKey(name)) throw Bad("RemovalBackupIncomplete");
            return result;
        }
        private static string Latest(WindowsFileFence backups)
        {
            string? selected = null; DateTime timestamp = DateTime.MinValue;
            foreach (var directory in WindowsPath.Directories(backups.Root, "*"))
            {
                var id = Path.GetFileName(directory);
                if (!Regex.IsMatch(id, @"\A[0-9a-f]{32}\z")) continue;
                using (var file = Maybe(backups, id + "/completed.json"))
                {
                    if (file == null) continue;
                    var time = File.GetLastWriteTimeUtc(WindowsPath.ForIo(file.Path)); file.Recheck();
                    if (selected == null || time > timestamp) { selected = id; timestamp = time; }
                }
            }
            return selected ?? throw Bad("RemovalBackupRequired");
        }
        private static bool History(ReceiptChannelState state, string root, string version, string descriptor, string pluginVersion, VerifiedUpdateDescriptor? historical)
        {
            if (state.Root != root || state.ActiveVersion != pluginVersion) throw Bad("RemovalActiveVersionMismatch");
            var matched = false;
            foreach (var channel in new[] { "Stable", "PreRelease", "Beta" })
            {
                var rowVersion = state.VersionFor(channel);
                var rowHash = state.DescriptorFor(channel);
                var rowId = state.RepositoryIdFor(channel);
                if (rowVersion == null) continue;
                if (rowId != ReleaseFeedPolicy.For(channel).RepositoryId) throw Bad("RemovalRepositoryMismatch");
                if (rowVersion == version && rowHash == descriptor) matched = true;
            }
            if (matched) return true;
            if (historical == null) return false;
            if (historical.Version != version || historical.DescriptorSha256 != descriptor)
                throw Bad("RemovalHistoricalMismatch");
            var retained = false;
            foreach (var channel in new[] { "Stable", "PreRelease", "Beta" }) {
                if (!historical.AllowsChannel(channel)) continue;
                var retainedVersion = state.VersionFor(channel);
                if (retainedVersion == null) continue;
                if (historical.RepositoryId != state.RepositoryIdFor(channel)) throw Bad("RemovalHistoricalMismatch");
                retained = true;
                if (UpdateVersion.Parse(retainedVersion).CompareTo(UpdateVersion.Parse(version)) > 0) return true;
            }
            if (!retained) throw Bad("RemovalHistoricalMismatch");
            return false;
        }
        private static VerifiedUpdateDescriptor RetainedHistory(WindowsFileFence game, Plan plan, string digest)
        {
            VerifiedUpdateDescriptor? selected = null;
            foreach (var entry in UpdateRecovery.Scan(game))
            {
                if (entry.Pending || !entry.Committed || entry.Descriptor != digest) continue;
                var pins = new Dictionary<string, string>(StringComparer.Ordinal);
                var bytes = Pin(game, entry.Work + "/plan", 16384, pins);
                var rows = Encoding.ASCII.GetString(bytes).Split('\n').Where(line => line.StartsWith("file=" + UpdateTransaction.HighWaterPath + "\t", StringComparison.Ordinal)).ToArray();
                if (rows.Length != 1) throw Bad("RemovalHistoricalSlotInvalid");
                var slot = rows[0].Split('\t'); if (slot.Length != 5) throw Bad("RemovalHistoricalSlotInvalid");
                if (slot[3] != plan.WaterIdentity || slot[4] != plan.WaterHash) continue;
                if (selected != null) throw Bad("RemovalHistoricalAmbiguous");
                foreach (var marker in new[] { "committed", "commit-intent" })
                    if (Encoding.ASCII.GetString(Pin(game, entry.Work + "/" + marker, 128, pins)) != UpdateTrust.Hash(bytes) + "\n") throw Bad("RemovalHistoricalCommitChanged");
                var descriptor = Pin(game, entry.Stage + "/descriptor.bin", UpdateTrust.MaxDescriptorBytes, pins);
                var signature = Pin(game, entry.Stage + "/signature.bin", UpdateTrust.MaxSignatureBytes, pins);
                var key = Pin(game, entry.Stage + "/key-id.bin", 40, pins); var keyId = Encoding.ASCII.GetString(key);
                if (UpdateTrust.Hash(descriptor) != digest || key.Any(value => value > 127) || !Regex.IsMatch(keyId, @"\A[A-Za-z0-9_-]{1,40}\z")) throw Bad("RemovalHistoricalDescriptorInvalid");
                selected = InstallProof.VerifyUpdateProof(descriptor, new DetachedSignature(keyId, signature).Bytes());
                foreach (var pair in pins)
                {
                    string? previous; if (plan.GamePins.TryGetValue(pair.Key, out previous) && previous != pair.Value) throw Bad("RemovalEvidenceConflict");
                    plan.GamePins[pair.Key] = pair.Value;
                }
            }
            return selected ?? throw Bad("RemovalHistoricalProofRequired");
        }
        internal static Plan? Review(string gameRoot, string packageRoot, string version, string manifestHash, string backupRoot)
        {
            gameRoot = WindowsPath.Absolute(gameRoot); packageRoot = WindowsPath.Absolute(packageRoot); backupRoot = WindowsPath.Absolute(backupRoot);
            if (UpdateVersion.Parse(version).Tag != version || !Digest(manifestHash)) throw Bad("ReinstallInputInvalid");
            using (var game = new WindowsFileFence(gameRoot))
            {
                UpdateLaunchGate.AssertNoReinstall(game);
                string? waterHash; var highest = UpdateTransaction.ReadHighWater(game, out waterHash);
                using (var receipt = Maybe(game, InstallReceipt.Path)) if (highest == null || receipt != null) return null;
                Absent(game); UpdateLaunchGate.Check(game);
                var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrEmpty(local) || backupRoot != WindowsPath.Absolute(Path.Combine(local, "BetterAstralParty", "Backups", "v5", UpdateTrust.Hash(Encoding.UTF8.GetBytes(game.Root.ToLowerInvariant())))))
                    throw Bad("RemovalBackupRootMismatch");
                using (var package = new WindowsFileFence(packageRoot))
                using (var manifest = package.OpenLaunchImage("package-manifest.json"))
                using (var backups = new WindowsFileFence(backupRoot))
                using (var water = game.OpenFile(UpdateTransaction.HighWaterPath))
                {
                    if (manifest.Hash() != manifestHash || water.Hash() != waterHash) throw Bad("ReinstallInputChanged");
                    InstallProof.ValidateInputs(gameRoot, packageRoot, version);
                    var plan = new Plan { GameRoot = gameRoot, PackageRoot = packageRoot, IncomingVersion = version, ManifestHash = manifestHash, BackupRoot = backupRoot,
                        RootIdentity = game.RootIdentity.Text, BackupRootIdentity = backups.RootIdentity.Text, Version = highest, WaterHash = waterHash!, WaterIdentity = water.Identity.Text, BackupId = Latest(backups) };
                    var entries = Journal(game, backups, plan);
                    var receiptBytes = Pin(backups, entries[InstallReceipt.Path].Backup, 16384, plan.BackupPins);
                    var receipt = InstallReceipt.Parse(receiptBytes);
                    foreach (var pair in receipt.Files) if (entries[pair.Key].Existed && entries[pair.Key].Hash != pair.Value) throw Bad("RemovalReceiptMismatch");
                    string pluginVersion;
                    if (entries[UpdateTrust.PluginPath].Existed)
                    {
                        var metadata = InstalledPluginMetadata.ReadMaintenance(Pin(backups, entries[UpdateTrust.PluginPath].Backup, UpdateTrust.MaxFileBytes, plan.BackupPins));
                        if (metadata.UpdateProtocol != UpdateTrust.Protocol || metadata.SettingsSchema != 1) throw Bad("RemovalPluginUnsupported");
                        pluginVersion = metadata.Version;
                    }
                    else throw Bad("RemovalPluginBackupRequired");
                    var channel = receipt.Channels ?? throw Bad("RemovalChannelMissing");
                    var waterLines = Encoding.ASCII.GetString(UpdateTicket.Read(water, 2048)).Split('\n');
                    var digest = waterLines[3].Substring(11);
                    if (!History(channel, plan.RootIdentity, highest, digest, pluginVersion, null))
                    {
                        var historical = RetainedHistory(game, plan, digest);
                        if (!History(channel, plan.RootIdentity, highest, digest, pluginVersion, historical)) throw Bad("RemovalHistoryMismatch");
                    }
                    return plan;
                }
            }
        }
        private static bool Same(Dictionary<string, string> first, Dictionary<string, string> second)
        { return first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var hash) && hash == pair.Value); }
#if BAP_SAFETY_FIXTURE
        internal static Action<string, string>? FixtureBoundary = null;
#endif
        private static void Boundary(string name, string archive)
        {
#if BAP_SAFETY_FIXTURE
            FixtureBoundary?.Invoke(name, archive);
#endif
        }
        internal static void Retire(Plan plan, string gameRoot, string packageRoot, string version, string manifestHash, string backupRoot, WindowsFileFence.FileLease heldBackupLock)
        {
            using (var maintenance = new UpdateLaunchGate.Lease(gameRoot))
            using (var game = new WindowsFileFence(gameRoot))
            using (var backups = new WindowsFileFence(backupRoot))
            using (var processes = WindowsProcessGuard.Acquire(game, new[] { "AstralParty_INT.exe" }))
            {
                heldBackupLock.Recheck();
                if (!heldBackupLock.Stream.CanWrite || heldBackupLock.Path != backups.Full("pending.json.lock")) throw Bad("ReinstallBackupLockMismatch");
                if (game.RootIdentity.Text != plan.RootIdentity || backups.RootIdentity.Text != plan.BackupRootIdentity) throw Bad("ReinstallRootChanged");
                WindowsProcessGuard.AssertNoRunning(new[] { game.Full("AstralParty_INT.exe"), game.Full(OrdinaryInstallRecovery.LegacyLauncher), game.Full(OrdinaryInstallRecovery.LegacyHelper) });
                foreach (var relative in plan.GamePins.Keys) game.DirectoryIdentity(relative.Substring(0, relative.LastIndexOf('/')));
                Boundary("proof-parents-pinned", "");
                var current = Review(gameRoot, packageRoot, version, manifestHash, backupRoot);
                if (current == null || current.GameRoot != plan.GameRoot || current.PackageRoot != plan.PackageRoot || current.IncomingVersion != plan.IncomingVersion
                    || current.ManifestHash != plan.ManifestHash || current.BackupRoot != plan.BackupRoot || current.RootIdentity != plan.RootIdentity
                    || current.BackupRootIdentity != plan.BackupRootIdentity || current.Version != plan.Version || current.BackupId != plan.BackupId
                    || current.JournalHash != plan.JournalHash || current.WaterHash != plan.WaterHash || current.WaterIdentity != plan.WaterIdentity
                    || current.HelperProof != plan.HelperProof || current.CoreProof != plan.CoreProof || !Same(current.BackupPins, plan.BackupPins) || !Same(current.GamePins, plan.GamePins))
                    throw Bad("ReinstallPlanChanged");
                var held = new List<WindowsFileFence.FileLease>();
                try
                {
                    foreach (var pair in plan.BackupPins)
                    {
                        var file = backups.OpenFile(pair.Key); held.Add(file); if (file.Hash() != pair.Value) throw Bad("RemovalEvidenceChanged");
                    }
                    foreach (var pair in plan.GamePins)
                    {
                        var file = game.OpenFile(pair.Key); held.Add(file); if (file.Hash() != pair.Value) throw Bad("RemovalEvidenceChanged");
                    }
                    using (var water = game.OpenFile(UpdateTransaction.HighWaterPath, true))
                    {
                        if (water.Identity.Text != plan.WaterIdentity || water.Hash() != plan.WaterHash) throw Bad("ReinstallWaterChanged");
                        maintenance.Recheck(game); processes.Recheck(); heldBackupLock.Recheck(); Absent(game);
                        var nonce = Guid.NewGuid().ToString("N"); var archive = UpdateTransaction.HighWaterPath + ".uninstalled-" + nonce;
                        var record = new Dictionary<string, object?> { { "schema", 1 }, { "purpose", "clean-reinstall-generation" }, { "root", plan.RootIdentity },
                            { "fromVersion", plan.Version }, { "toVersion", version }, { "packageManifestSha256", manifestHash }, { "backupId", plan.BackupId },
                            { "proofSha256", plan.JournalHash }, { "waterSha256", plan.WaterHash }, { "archive", archive } };
                        // Values were validated above; this journal carries no executable input or secrets.
                        var text = "{" + string.Join(",", record.Select(pair => "\"" + pair.Key + "\":" + (pair.Value is int ? "1" : "\"" + (string)pair.Value! + "\""))) + "}";
                        var bytes = new UTF8Encoding(false).GetBytes(text);
                        using (var intent = game.OpenFile("BetterAstralParty.reinstall-v5.pending", true, true))
                        {
                            intent.Stream.Write(bytes, 0, bytes.Length); intent.Flush(); Boundary("intent-durable", archive);
                            water.RenameTo(game, archive); water.Flush(); Boundary("water-archived", archive);
                            if (water.Hash() != plan.WaterHash) throw Bad("ReinstallArchiveChanged");
                            intent.RenameTo(game, "BetterAstralParty.reinstall-v5.completed-" + nonce); intent.Flush(); Boundary("generation-completed", archive);
                        }
                    }
                }
                finally { for (var i = held.Count - 1; i >= 0; i--) held[i].Dispose(); }
            }
        }
    }
}
