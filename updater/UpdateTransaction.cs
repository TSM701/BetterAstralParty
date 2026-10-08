#nullable enable
using System;
using BetterAstralParty.Observability;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    internal enum TransactionResult { Committed, RolledBack, Abandoned }
    // Transaction core only. No helper entrypoint/UI, credentials, installer execution or deletion.
    internal static class UpdateTransaction
    {
        internal const string HighWaterPath = "BetterAstralParty.update.version";
        private const string LockPath = "BetterAstralParty.update.lock";
        private sealed class Item
        {
            internal string Path = "", OldId = "-", OldHash = "-", NewId = "", NewHash = "";
            internal WindowsFileFence.FileLease? Target, Backup, New;
            internal int State; // 0 original; 1 original moved; 2 replacement installed.
        }
        private sealed class Plan
        {
            internal string Root = "", Work = "", WorkId = "", Stage = "", StageId = "", Descriptor = "", Original = "";
            internal int Protocol = 1;
            internal string SettingsId = "-", SettingsHash = "-";
            internal readonly List<Item> Items = new List<Item>();
        }
        private sealed class Leases : IDisposable
        {
            private readonly List<IDisposable> _all = new List<IDisposable>();
            internal T Own<T>(T lease) where T : IDisposable { _all.Add(lease); return lease; }
            public void Dispose() { for (var i = _all.Count - 1; i >= 0; i--) _all[i].Dispose(); _all.Clear(); }
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
        private static void Hit(Action<string>? hook, string boundary) {hook?.Invoke(boundary);}
        internal static void RecordBoundary(string boundary,DiagnosticOperation operation,string target) {
            switch(boundary) {
                case "before:prepared":DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Staging,DiagnosticOutcome.Begin,operation,target);break;
                case "after:prepared":DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Staging,DiagnosticOutcome.Completed,operation,target);break;
                case "before:commit-intent":DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Apply,DiagnosticOutcome.Begin,operation,target);break;
                case "after:committed":DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Apply,DiagnosticOutcome.Completed,operation,target);break;
                case "before:rolled-back":DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Rollback,DiagnosticOutcome.Begin,operation,target);break;
                case "after:rolled-back":DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Rollback,DiagnosticOutcome.Completed,operation,target);break;
            }
        }
        private static Action<string> Guarded(WindowsFileFence fence, Plan plan, Action<string>? hook,DiagnosticOperation operation,string target)
        {
            return label => {
                fence.AssertPrivateDirectory(plan.Work); fence.AssertPrivateDirectory(plan.Stage);
                RecordBoundary(label,operation,target);
                hook?.Invoke(label);
                fence.AssertPrivateDirectory(plan.Work); fence.AssertPrivateDirectory(plan.Stage);
            };
        }
        private static string Slot(string work, string prefix, int index) { return work + "/" + prefix + index.ToString("D2", CultureInfo.InvariantCulture); }
        private static string[] Targets()
        {
            var targets = new string[10]; Array.Copy(InstallReceipt.Owned, targets, 8); targets[8] = InstallReceipt.Path; targets[9] = HighWaterPath; return targets;
        }
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence fence, string path, bool mutable = true)
        {
            try { return fence.OpenFile(path, mutable); }
            catch (ApplySafetyException error) when (error.NativeError == 2) { return null; }
        }
        private static byte[] Read(WindowsFileFence.FileLease file, long limit)
        {
            file.Recheck(); var stream = file.Stream; stream.Position = 0;
            if (stream.Length < 1 || stream.Length > limit) throw Bad();
            var bytes = new byte[checked((int)stream.Length)]; var offset = 0;
            while (offset < bytes.Length) { var count = stream.Read(bytes, offset, bytes.Length - offset); if (count < 1) throw Bad(); offset += count; }
            file.Recheck(); return bytes;
        }
        private static WindowsFileFence.FileLease Write(WindowsFileFence fence, string path, byte[] bytes, Leases leases)
        {
            var file = leases.Own(fence.OpenFile(path, true, true)); file.Stream.Write(bytes, 0, bytes.Length); file.Flush(); return file;
        }
        private static WindowsFileFence.FileLease OperationLock(WindowsFileFence fence)
        {
            return Maybe(fence, LockPath) ?? fence.OpenFile(LockPath, true, true);
        }
        private static void Processes(WindowsFileFence fence)
        {
            WindowsProcessGuard.AssertNoRunning(new[] { fence.Full("AstralParty_INT.exe"), fence.Full("BetterAstralParty-Launcher.exe") });
        }
        private static string? Water(WindowsFileFence.FileLease? file, WindowsFileFence fence)
        {
            if (file == null) return null; var text = Encoding.ASCII.GetString(Read(file, 2048)); var lines = text.Split('\n');
            if (lines.Length != 5 || lines[0] != "BetterAstralParty.HighWater/v1" || lines[1] != "root=" + fence.RootIdentity.Text || !lines[2].StartsWith("version=", StringComparison.Ordinal)
                || !Regex.IsMatch(lines[3], @"\Adescriptor=[0-9A-F]{64}\z") || lines[4] != "") throw Bad();
            var version = lines[2].Substring(8); UpdateVersion.Parse(version); return version;
        }
        internal static string? ReadHighWater(WindowsFileFence fence)
        {
            string? hash; return ReadHighWater(fence, out hash);
        }
        internal static string? ReadHighWater(WindowsFileFence fence, out string? hash)
        {
            using (var file = Maybe(fence, HighWaterPath, false)) {
                var version = Water(file, fence); hash = file?.Hash(); return version;
            }
        }
        private static byte[] WaterBytes(WindowsFileFence fence, VerifiedUpdateDescriptor descriptor, WindowsFileFence.FileLease? original)
        {
            var previous = Water(original, fence);
            if (previous != null && UpdateVersion.Parse(descriptor.Version).CompareTo(UpdateVersion.Parse(previous)) <= 0) return Read(original!, 2048);
            return Encoding.ASCII.GetBytes("BetterAstralParty.HighWater/v1\nroot=" + fence.RootIdentity.Text + "\nversion=" + descriptor.Version + "\ndescriptor=" + descriptor.DescriptorSha256 + "\n");
        }
        private static UpdateContext Context(UpdateContext selection, string installed, string? highest, string? channelDescriptor = null)
        { return new UpdateContext(selection.Repository, selection.Channel, installed, selection.ReleaseTag, selection.ReleaseId, selection.AssetId, selection.Platform, highest, selection.RepositoryId, selection.Transition, channelDescriptor); }
        private static ReceiptChannelState Channels(WindowsFileFence fence, InstallReceipt receipt, string original, WindowsFileFence.FileLease? water, string receiptHash, UpdateContext selection)
        {
            if (receipt.Channels != null) {
                if (receipt.Channels.Root != fence.RootIdentity.Text || receipt.Channels.ActiveVersion != original) throw Bad(); return receipt.Channels;
            }
            // Audited legacy private installations had one global Beta floor. A first v2
            // Beta update migrates it without reducing it. Legacy -> Stable needs a whole bridge.
            if (selection.Channel != "Beta" || selection.Repository != ReleaseFeedPolicy.BetaRepository || selection.RepositoryId != ReleaseFeedPolicy.BetaRepositoryId)
                throw new ApplySafetyException(ApplyFailure.InstallationRequired);
            var highest = Water(water, fence); var version = highest != null && UpdateVersion.Parse(highest).CompareTo(UpdateVersion.Parse(original)) > 0 ? highest : original;
            var evidence = water == null ? receiptHash : Encoding.ASCII.GetString(Read(water, 2048)).Split('\n')[3].Substring(11);
            return new ReceiptChannelState(fence.RootIdentity.Text, "Beta", Guid.NewGuid().ToString("N"), 0, null, "-", ReleaseFeedPolicy.BetaRepositoryId, version, evidence);
        }
        private static WindowsFileFence.FileLease? Settings(WindowsFileFence fence, Plan plan, Leases leases, bool recovering)
        {
            if (plan.Protocol == 1) return null;
            WindowsFileFence.FileLease? file;
            try { file = Maybe(fence, ChannelTransitionIntent.SettingsPath, false); }
            catch (ApplySafetyException error) when (error.Failure == ApplyFailure.FileBusy && error.NativeError == 32) { throw new ApplySafetyException(ApplyFailure.ProcessBusy, 32); }
            if (file != null) leases.Own(file);
            if (file != null && file.Stream.Length > 1024 * 1024) throw new ApplySafetyException(ApplyFailure.InstallationRequired);
              if (recovering) {
                  // Configuration is outside the mutation set. After a helper exits,
                  // failure-OFF persistence or user changes must not strand owned-file
                  // recovery. Validate the original backup and retain the current file.
                  if (plan.SettingsId != "-") { var backup = leases.Own(fence.OpenFile(plan.Work + "/settings.backup"));
                      if (backup.Stream.Length > 1024 * 1024 || backup.Hash() != plan.SettingsHash) throw Bad(); }
              } else { plan.SettingsId = file?.Identity.Text ?? "-"; plan.SettingsHash = file?.Hash() ?? "-"; }
            return file;
        }
        private static byte[] Serialize(Plan plan)
        {
            var output = new StringBuilder("BetterAstralParty.Transaction/v" + plan.Protocol + "\nroot=" + plan.Root + "\nwork=" + plan.Work + "\nwork-id=" + plan.WorkId
                + "\nstage=" + plan.Stage + "\nstage-id=" + plan.StageId + "\ndescriptor=" + plan.Descriptor + "\noriginal=" + plan.Original + "\ncount=10\n");
            foreach (var item in plan.Items) output.Append("file=").Append(item.Path).Append('\t').Append(item.OldId).Append('\t').Append(item.OldHash).Append('\t').Append(item.NewId).Append('\t').Append(item.NewHash).Append('\n');
            if (plan.Protocol == 2) output.Append("settings=").Append(plan.SettingsId).Append('\t').Append(plan.SettingsHash).Append('\n');
            return Encoding.ASCII.GetBytes(output.ToString());
        }
        private static Plan Parse(byte[] bytes)
        {
            foreach (var value in bytes) if (value > 127 || value == 13) throw Bad(); var lines = Encoding.ASCII.GetString(bytes).Split('\n');
            var protocol = lines.Length == 20 && lines[0] == "BetterAstralParty.Transaction/v1" ? 1 : lines.Length == 21 && lines[0] == "BetterAstralParty.Transaction/v2" ? 2 : 0;
            if (protocol == 0 || lines[8] != "count=10" || lines[lines.Length - 1] != "") throw Bad();
            var keys = new[] { "root=", "work=", "work-id=", "stage=", "stage-id=", "descriptor=", "original=" }; var values = new string[7];
            for (var i = 0; i < keys.Length; i++) { if (!lines[i + 1].StartsWith(keys[i], StringComparison.Ordinal)) throw Bad(); values[i] = lines[i + 1].Substring(keys[i].Length); }
            var plan = new Plan { Protocol = protocol, Root = values[0], Work = values[1], WorkId = values[2], Stage = values[3], StageId = values[4], Descriptor = values[5], Original = values[6] };
            if (protocol == 2) {
                if (!lines[19].StartsWith("settings=", StringComparison.Ordinal)) throw Bad(); var cfg = lines[19].Substring(9).Split('\t');
                if (cfg.Length != 2 || (cfg[0] == "-" ? cfg[1] != "-" : !Regex.IsMatch(cfg[0], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(cfg[1], @"\A[0-9A-F]{64}\z"))) throw Bad();
                plan.SettingsId = cfg[0]; plan.SettingsHash = cfg[1];
            }
            if (!Regex.IsMatch(plan.Work, @"\Abap-txn-[0-9a-f]{32}\z") || !Regex.IsMatch(plan.Stage, @"\Abap-stage-[0-9a-f]{32}\z") || !Regex.IsMatch(plan.Descriptor, @"\A[0-9A-F]{64}\z")) throw Bad();
            UpdateVersion.Parse(plan.Original); var targets = Targets();
            for (var i = 0; i < targets.Length; i++) {
                if (!lines[9 + i].StartsWith("file=", StringComparison.Ordinal)) throw Bad(); var f = lines[9 + i].Substring(5).Split('\t');
                if (f.Length != 5 || f[0] != targets[i] || !Regex.IsMatch(f[3], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(f[4], @"\A[0-9A-F]{64}\z")) throw Bad();
                if (f[1] == "-" ? f[2] != "-" : !Regex.IsMatch(f[1], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(f[2], @"\A[0-9A-F]{64}\z")) throw Bad();
                plan.Items.Add(new Item { Path = f[0], OldId = f[1], OldHash = f[2], NewId = f[3], NewHash = f[4] });
            }
            if (!Equal(bytes, Serialize(plan))) throw Bad(); return plan;
        }
        private static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static bool Is(WindowsFileFence.FileLease? file, string id, string hash)
        { return file != null && file.Identity.Text == id && file.Hash() == hash; }
        private static void Classify(Item item)
        {
            if (Is(item.New, item.NewId, item.NewHash) && item.Backup == null && (item.OldId == "-" ? item.Target == null : Is(item.Target, item.OldId, item.OldHash))) item.State = 0;
            else if (item.OldId != "-" && item.Target == null && Is(item.Backup, item.OldId, item.OldHash) && Is(item.New, item.NewId, item.NewHash)) item.State = 1;
            else if (Is(item.Target, item.NewId, item.NewHash) && item.New == null && (item.OldId == "-" ? item.Backup == null : Is(item.Backup, item.OldId, item.OldHash))) item.State = 2;
            else throw Bad();
        }
        private static WindowsFileFence.FileLease Original(Item item)
        { return (item.State == 0 ? item.Target : item.Backup) ?? throw Bad(); }
        private static void Move(WindowsFileFence fence, WindowsFileFence.FileLease file, string path, string label, Action<string>? hook)
        { Processes(fence); Hit(hook, "before:" + label); file.RenameTo(fence, path); file.Flush(); Hit(hook, "after:" + label); }
        private static void Install(WindowsFileFence fence, Plan plan, int index, Action<string>? hook)
        {
            var item = plan.Items[index]; if (item.State == 2) return;
            if (item.State == 0 && item.Target != null) { Move(fence, item.Target, Slot(plan.Work, "old-", index), "backup-" + index, hook); item.Backup = item.Target; item.Target = null; item.State = 1; }
            var file = item.New ?? throw Bad(); Move(fence, file, item.Path, "install-" + index, hook); item.Target = file; item.New = null; item.State = 2;
        }
        private static void Rollback(WindowsFileFence fence, Plan plan, int index, Action<string>? hook)
        {
            var item = plan.Items[index]; if (item.State == 0) return;
            if (item.State == 2) { var file = item.Target ?? throw Bad(); Move(fence, file, Slot(plan.Work, "new-", index), "undo-new-" + index, hook); item.New = file; item.Target = null; item.State = item.OldId == "-" ? 0 : 1; }
            if (item.State == 1) { var file = item.Backup ?? throw Bad(); Move(fence, file, item.Path, "restore-old-" + index, hook); item.Target = file; item.Backup = null; item.State = 0; }
        }
        private static bool Marker(WindowsFileFence fence, string path, string planHash, Leases leases)
        {
            var file = Maybe(fence, path, false); if (file == null) return false; leases.Own(file);
            if (!Equal(Read(file, 128), Encoding.ASCII.GetBytes(planHash + "\n"))) throw Bad(); return true;
        }
        private static void Mark(WindowsFileFence fence, string path, string hash, Leases leases, string label, Action<string>? hook)
        { Hit(hook, "before:" + label); Write(fence, path, Encoding.ASCII.GetBytes(hash + "\n"), leases); Hit(hook, "after:" + label); }
        private static void AssertNoPending(WindowsFileFence fence)
        {
            // Enumerate only immediate, fixed-prefix work names; every open still uses the pinned fence.
            var directories = Directory.GetDirectories(fence.Root, "bap-txn-*", SearchOption.TopDirectoryOnly);
            if (directories.Length > 128) throw Bad();
            foreach (var directory in directories) using (var leases = new Leases()) {
                var work = System.IO.Path.GetFileName(directory);
                if (!Regex.IsMatch(work, @"\Abap-txn-[0-9a-f]{32}\z")) throw Bad(); fence.AssertPrivateDirectory(work);
                var journal = Maybe(fence, work + "/plan", false); if (journal == null) continue; leases.Own(journal);
                var bytes = Read(journal, 16384); var plan = Parse(bytes); var hash = UpdateTrust.Hash(bytes);
                if (plan.Root != fence.RootIdentity.Text || plan.Work != work || plan.WorkId != fence.DirectoryIdentity(work).Text) throw Bad();
                var committed = Marker(fence, work + "/committed", hash, leases); var rolled = Marker(fence, work + "/rolled-back", hash, leases);
                var intent = Marker(fence, work + "/commit-intent", hash, leases);
                if (committed ? !intent || rolled : !rolled || intent) throw Bad();
            }
        }
        internal static TransactionResult Apply(WindowsFileFence fence, byte[] descriptor, string keyId, byte[] signature, byte[] zip,
            UpdateTrust trust, UpdateContext selection, Action<string>? boundary = null,Action? beforeGameLease=null,string? attemptWork=null,DiagnosticOperation diagnosticOperation=default)
        {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            if (attemptWork != null && !Regex.IsMatch(attemptWork, @"\Abap-txn-[0-9a-f]{32}\z")) throw Bad();
            using (var leases = new Leases()) {
                leases.Own(OperationLock(fence)); AssertNoPending(fence);
                WindowsFileFence.FileLease gameImage;
                try { Processes(fence); beforeGameLease?.Invoke(); gameImage=leases.Own(fence.OpenFile("AstralParty_INT.exe")); }
                catch(ApplySafetyException error) when(error.Failure==ApplyFailure.FileBusy && error.NativeError==32) {throw new ApplySafetyException(ApplyFailure.ProcessBusy,32);}
                var plan = new Plan { Root = fence.RootIdentity.Text, Work = attemptWork ?? "bap-txn-" + Guid.NewGuid().ToString("N") }; var targets = Targets();
                foreach (var path in targets) { WindowsFileFence.FileLease? file;
                    try {file=Maybe(fence,path);} catch(ApplySafetyException error) when(path=="BetterAstralParty-Launcher.exe" && error.Failure==ApplyFailure.FileBusy && error.NativeError==32) {throw new ApplySafetyException(ApplyFailure.ProcessBusy,32);}
                    if (file != null) leases.Own(file);
                    plan.Items.Add(new Item { Path = path, Target = file, OldId = file?.Identity.Text ?? "-", OldHash = file?.Hash() ?? "-" }); }
                Processes(fence); var receiptFile = plan.Items[8].Target;
                // Recheck eligibility after the original game exits, before any private
                // transaction work or owned rename. Path/identity/hash errors still fault.
                if (receiptFile == null || receiptFile.Stream.Length > 16384) throw new ApplySafetyException(ApplyFailure.InstallationRequired);
                var receiptBytesBefore = Read(receiptFile, 16384); InstallReceipt receipt;
                try { receipt = InstallReceipt.Parse(receiptBytesBefore); }
                catch (ApplySafetyException error) when (error.Failure == ApplyFailure.InvalidState) { throw new ApplySafetyException(ApplyFailure.InstallationRequired); }
                for (var i = 0; i < 8; i++) if (plan.Items[i].Target != null && plan.Items[i].OldHash != receipt.Files[plan.Items[i].Path]) throw Bad();
                var plugin = plan.Items.Find(item => item.Path == UpdateTrust.PluginPath)!;
                var installedMetadata = InstalledPluginMetadata.Read(Read(plugin.Target ?? throw Bad(), UpdateTrust.MaxFileBytes)); plan.Original = installedMetadata.Version;
                var channels = selection.RepositoryId > 0 ? Channels(fence, receipt, plan.Original, plan.Items[9].Target, plan.Items[8].OldHash, selection) : null;
                var context = Context(selection, plan.Original, channels == null ? Water(plan.Items[9].Target, fence) : channels.Floor(selection), channels == null ? null : selection.Channel == "Stable" ? channels.StableDescriptor == "-" ? null : channels.StableDescriptor : channels.BetaDescriptor);
                var verified = trust.VerifyDescriptor(descriptor, keyId, signature, context); var payload = UpdatePackage.VerifyPayload(zip, verified);
                plan.Protocol = verified.Protocol;
                if (receipt.Channels != null && (installedMetadata.UpdateProtocol != 2 || installedMetadata.SettingsSchema != 1)) throw Bad();
                if (selection.Transition != null) selection.Transition.Require(fence, receipt, plan.Items[8].OldHash, plan.Items[9].Target?.Hash(), plan.Original, verified);
                var settings = Settings(fence, plan, leases, false);
                byte[] compatibility;
                using(var stream=payload.OpenRead()) using(var archive=new ZipArchive(stream,ZipArchiveMode.Read,false)) {
                    var entry=archive.GetEntry("BetterAstralParty.compatibility.json") ?? throw Bad();if(entry.Length>65536) throw new ApplySafetyException(ApplyFailure.CompatibilityRequired);
                    using(var input=entry.Open()) using(var output=new MemoryStream()) {input.CopyTo(output);compatibility=output.ToArray();}
                    var pluginEntry=archive.GetEntry(UpdateTrust.PluginPath) ?? throw Bad();
                    using(var input=pluginEntry.Open()) using(var output=new MemoryStream()) {
                        input.CopyTo(output);var target=InstalledPluginMetadata.Read(output.ToArray());
                        if(target.Version!=UpdateVersion.Parse(verified.Version).Tag || verified.Protocol==2 && (target.UpdateProtocol!=2 || target.SettingsSchema!=verified.SettingsSchema))
                            throw new ApplySafetyException(ApplyFailure.InstallationRequired);
                    }
                }
                var compatible=leases.Own(UpdateCompatibility.Require(fence,compatibility,gameImage));
                var nextChannels = channels?.Applied(fence.RootIdentity.Text, verified, selection.Transition?.Operation ?? Guid.NewGuid().ToString("N"), selection.Transition);
                var receiptBytes = receipt.Updated(verified, nextChannels); // All owned files and per-channel history share the receipt slot.
                plan.WorkId = fence.CreatePrivateDirectory(plan.Work).Text;
                if (settings != null) { settings.Stream.Position = 0; var backup = leases.Own(fence.OpenFile(plan.Work + "/settings.backup", true, true)); settings.Stream.CopyTo(backup.Stream); backup.Flush(); if (backup.Hash() != plan.SettingsHash) throw Bad(); }
                var staged = UpdateStaging.Create(fence, descriptor, keyId, signature, zip, trust, context);
                plan.Stage = staged.Directory; plan.StageId = staged.DirectoryIdentity.Text; plan.Descriptor = staged.DescriptorSha256;
                boundary = Guarded(fence, plan, boundary,diagnosticOperation,selection.ReleaseTag);
                using (var locked = UpdateStaging.VerifyLocked(fence, staged, trust, context)) {
                    using (var stream = locked.Payload.OpenRead()) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false)) {
                        for (var i = 0; i < 8; i++) {
                            var item = plan.Items[i]; var entry = archive.GetEntry(item.Path) ?? throw Bad(); var file = leases.Own(fence.OpenFile(Slot(plan.Work, "new-", i), true, true));
                            using (var input = entry.Open()) { var buffer = new byte[32768]; long total = 0; int count;
                                while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { total += count; if (total > UpdateTrust.MaxFileBytes) throw Bad(); file.Stream.Write(buffer, 0, count); } }
                            file.Flush(); item.New = file; item.NewId = file.Identity.Text; item.NewHash = file.Hash();
                            var signed = verified.Files.FindByPath(item.Path); if (item.NewHash != signed.Sha256 || file.Stream.Length != signed.Bytes) throw Bad();
                        }
                    }
                    var targetMetadata = InstalledPluginMetadata.Read(Read(plugin.New ?? throw Bad(), UpdateTrust.MaxFileBytes));
                    if (targetMetadata.Version != UpdateVersion.Parse(verified.Version).Tag || verified.Protocol == 2 && (targetMetadata.UpdateProtocol != 2 || targetMetadata.SettingsSchema != verified.SettingsSchema)) throw new ApplySafetyException(ApplyFailure.InstallationRequired);
                    for (var i = 8; i < 10; i++) { var file = Write(fence, Slot(plan.Work, "new-", i), i == 8 ? receiptBytes : WaterBytes(fence, verified, plan.Items[9].Target), leases);
                        var item = plan.Items[i]; item.New = file; item.NewId = file.Identity.Text; item.NewHash = file.Hash(); }
                    var planBytes = Serialize(plan); var hash = UpdateTrust.Hash(planBytes); Hit(boundary, "before:prepared"); Write(fence, plan.Work + "/plan", planBytes, leases); Hit(boundary, "after:prepared");
                    locked.Recheck(); fence.AssertPrivateDirectory(plan.Work); Processes(fence);
                    compatible.Recheck(); // Last read-only compatibility check before the first owned rename.
                    for (var i = 0; i < 9; i++) Install(fence, plan, i, boundary);
                    Mark(fence, plan.Work + "/commit-intent", hash, leases, "commit-intent", boundary);
                    Install(fence, plan, 9, boundary); // High-water is last; intent makes recovery finish forward.
                    Mark(fence, plan.Work + "/committed", hash, leases, "committed", boundary); return TransactionResult.Committed;
                }
            }
        }
        internal static TransactionResult Recover(WindowsFileFence fence, string work, UpdateTrust trust, UpdateContext selection, Action<string>? boundary = null,DiagnosticOperation diagnosticOperation=default)
        {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            if (!Regex.IsMatch(work, @"\Abap-txn-[0-9a-f]{32}\z")) throw Bad();
            using (var leases = new Leases()) {
                leases.Own(OperationLock(fence)); Processes(fence); leases.Own(fence.OpenFile("AstralParty_INT.exe")); fence.AssertPrivateDirectory(work);
                var journal = Maybe(fence, work + "/plan", false); if (journal == null) return TransactionResult.Abandoned; leases.Own(journal);
                var bytes = Read(journal, 16384); var plan = Parse(bytes); var hash = UpdateTrust.Hash(bytes);
                boundary = Guarded(fence, plan, boundary,diagnosticOperation,selection.ReleaseTag);
                if (plan.Work != work || plan.Root != fence.RootIdentity.Text || plan.WorkId != fence.DirectoryIdentity(work).Text) throw Bad();
                fence.AssertPrivateDirectory(plan.Stage); if (plan.StageId != fence.DirectoryIdentity(plan.Stage).Text) throw Bad();
                var evidence = new List<WindowsFileFence.FileLease>(); foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" }) evidence.Add(leases.Own(fence.OpenFile(plan.Stage + "/" + name)));
                for (var i = 0; i < 10; i++) {
                    var item = plan.Items[i]; item.Target = Maybe(fence, item.Path); item.Backup = Maybe(fence, Slot(work, "old-", i)); item.New = Maybe(fence, Slot(work, "new-", i));
                    foreach (var file in new[] { item.Target, item.Backup, item.New }) if (file != null) leases.Own(file); Classify(item);
                }
                Processes(fence); var plugin = plan.Items.Find(item => item.Path == UpdateTrust.PluginPath)!;
                if (InstalledPluginMetadata.Read(Read(Original(plugin), UpdateTrust.MaxFileBytes)).Version != plan.Original) throw Bad();
                var originalReceipt = InstallReceipt.Parse(Read(Original(plan.Items[8]), 16384));
                for (var i = 0; i < 8; i++) if (plan.Items[i].OldId != "-" && plan.Items[i].OldHash != originalReceipt.Files[plan.Items[i].Path]) throw Bad();
                var waterItem = plan.Items[9]; var oldWater = waterItem.OldId == "-" ? null : Original(waterItem);
                var channels = selection.RepositoryId > 0 ? Channels(fence, originalReceipt, plan.Original, oldWater, plan.Items[8].OldHash, selection) : null;
                var context = Context(selection, plan.Original, channels == null ? Water(oldWater, fence) : channels.Floor(selection), channels == null ? null : selection.Channel == "Stable" ? channels.StableDescriptor == "-" ? null : channels.StableDescriptor : channels.BetaDescriptor);
                var descriptor = trust.VerifyDescriptor(Read(evidence[0], UpdateTrust.MaxDescriptorBytes), Encoding.ASCII.GetString(Read(evidence[2], 40)), Read(evidence[1], UpdateTrust.MaxSignatureBytes), context);
                if (descriptor.DescriptorSha256 != plan.Descriptor || descriptor.Files.Count != 8) throw Bad();
                if (descriptor.Protocol != plan.Protocol) throw Bad(); Settings(fence, plan, leases, true);
                if (selection.Transition != null) selection.Transition.RequireRecovery(fence, originalReceipt, plan.Items[8].OldHash, oldWater?.Hash(), plan.Original, descriptor, plan.SettingsHash);
                UpdatePackage.VerifyPayload(Read(evidence[3], UpdateTrust.MaxZipBytes), descriptor);
                for (var i = 0; i < 8; i++) {
                    var item = plan.Items[i]; var signed = descriptor.Files.FindByPath(item.Path); var file = item.State == 2 ? item.Target : item.New;
                    if (item.NewHash != signed.Sha256 || file == null || file.Stream.Length != signed.Bytes) throw Bad();
                }
                var newReceiptFile = plan.Items[8].State == 2 ? plan.Items[8].Target : plan.Items[8].New;
                var expectedChannels = newReceiptFile == null ? null : InstallReceipt.Parse(Read(newReceiptFile, 16384)).Channels;
                if (channels != null) {
                    if (expectedChannels == null || selection.Transition != null && expectedChannels.Operation != selection.Transition.Operation) throw Bad();
                    expectedChannels = channels.Applied(fence.RootIdentity.Text, descriptor, expectedChannels.Operation, selection.Transition);
                }
                if (plan.Items[8].NewHash != UpdateTrust.Hash(originalReceipt.Updated(descriptor, expectedChannels)) || plan.Items[9].NewHash != UpdateTrust.Hash(WaterBytes(fence, descriptor, oldWater))) throw Bad();
                var newPlugin = plugin.State == 2 ? plugin.Target : plugin.New;
                var newMetadata = InstalledPluginMetadata.Read(Read(newPlugin ?? throw Bad(), UpdateTrust.MaxFileBytes));
                if (newMetadata.Version != UpdateVersion.Parse(descriptor.Version).Tag || descriptor.Protocol == 2 && (newMetadata.UpdateProtocol != 2 || newMetadata.SettingsSchema != descriptor.SettingsSchema)) throw Bad();
                var intent = Marker(fence, work + "/commit-intent", hash, leases); var committed = Marker(fence, work + "/committed", hash, leases); var rolled = Marker(fence, work + "/rolled-back", hash, leases);
                var states = new int[10]; for (var i = 0; i < 10; i++) states[i] = plan.Items[i].State;
                var direction = UpdateRecoveryPolicy.Direction(states, intent, committed, rolled);
                fence.AssertPrivateDirectory(work); fence.AssertPrivateDirectory(plan.Stage);
                if (direction == RecoveryDirection.CompleteTarget) {
                    Install(fence, plan, 9, boundary); VerifyResolved(fence, plan, true);
                    if (!committed) Mark(fence, work + "/committed", hash, leases, "committed", boundary);
                    return TransactionResult.Committed;
                }
                if (rolled) { VerifyResolved(fence, plan, false); return TransactionResult.RolledBack; }
                DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Rollback,DiagnosticOutcome.Begin,diagnosticOperation,selection.ReleaseTag);
                for (var i = 8; i >= 0; i--) Rollback(fence, plan, i, boundary);
                VerifyResolved(fence, plan, false);
                Mark(fence, work + "/rolled-back", hash, leases, "rolled-back", boundary); return TransactionResult.RolledBack;
            }
        }
        private static void VerifyResolved(WindowsFileFence fence, Plan plan, bool target)
        {
            foreach (var item in plan.Items) {
                if (item.State != (target ? 2 : 0)) throw Bad();
                var expectedId = target ? item.NewId : item.OldId;
                if (expectedId == "-") { if (item.Target != null) throw Bad();
                    using (var unexpected = Maybe(fence, item.Path, false)) if (unexpected != null) throw Bad(); }
                else { var file = item.Target ?? throw Bad(); file.Recheck();
                    if (file.Identity.Text != expectedId || file.Hash() != (target ? item.NewHash : item.OldHash)) throw Bad(); }
            }
        }
        private static UpdateFile FindByPath(this System.Collections.ObjectModel.ReadOnlyCollection<UpdateFile> files, string path)
        { foreach (var file in files) if (file.Path == path) return file; throw Bad(); }
    }
}
