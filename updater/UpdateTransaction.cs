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
        internal const string HighWaterPath = "BetterAstralParty.update-v5.version";
        internal const string Header = "BetterAstralParty.Transaction/v5";
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
            internal int Protocol = 4;
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
            var count = InstallReceipt.Owned.Length; var targets = new string[count + 2]; Array.Copy(InstallReceipt.Owned, targets, count); targets[count] = InstallReceipt.Path; targets[count + 1] = HighWaterPath; return targets;
        }
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence fence, string path, bool mutable = true)
        {
            try { return fence.OpenFile(path, mutable); }
            catch (ApplySafetyException error) when (error.NativeError == 2) { return null; }
        }
        private static WindowsFileFence.FileLease? Loaded(WindowsFileFence fence, string slot, string path)
        {
            if (!_inProcess || path != UpdateTrust.PluginPath) return Maybe(fence, slot);
            try { return fence.OpenLoadedImage(slot); }
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
        [ThreadStatic] private static bool _inProcess;
        private static void Processes(WindowsFileFence fence)
        {
            if (_inProcess) WindowsProcessGuard.AssertNoOtherRunning(new[] { fence.Full("AstralParty_INT.exe") });
            else WindowsProcessGuard.AssertNoRunning(new[] { fence.Full("AstralParty_INT.exe") });
        }
        // The running game executable cannot take an execution barrier from inside its own process.
        private static WindowsFileFence.FileLease GameImage(WindowsFileFence fence)
        {
            return _inProcess ? fence.OpenLaunchImage("AstralParty_INT.exe") : fence.OpenExecutionBarrier("AstralParty_INT.exe");
        }
        // The loaded plugin image can only be renamed while this process maps it.
        private static WindowsFileFence.FileLease? Owned(WindowsFileFence fence, string path)
        {
            if (!_inProcess || path != UpdateTrust.PluginPath) return Maybe(fence, path);
            try { return fence.OpenLoadedImage(path); }
            catch (ApplySafetyException error) when (error.NativeError == 2) { return null; }
        }
        private static string? Water(WindowsFileFence.FileLease? file, WindowsFileFence fence)
        {
            if (file == null) return null; return ParseWater(Read(file, 2048),fence).Version;
        }
        private sealed class HistoryRecord {
            internal readonly string Version,Descriptor;
            internal HistoryRecord(string version,string descriptor) { Version=version;Descriptor=descriptor; }
        }
        private static ApplySafetyException HistoryBad(string reason,DiagnosticCode code=DiagnosticCode.Integrity) {
            var error=new ApplySafetyException(ApplyFailure.RecoveryRequired);error.Data["BapRecoveryReason"]=reason;
            DiagnosticHub.Failure(DiagnosticFeature.Helper,DiagnosticPhase.DescriptorVerification,code,error,applyCode:(int)error.Failure);
            return error;
        }
        private static HistoryRecord ParseWater(byte[] bytes,WindowsFileFence fence) {
            foreach(var value in bytes)if(value>127 || value==13)throw HistoryBad("HighWaterInvalid");
            var lines=Encoding.ASCII.GetString(bytes).Split('\n');
            if(lines.Length==5 && lines[1]!="root="+fence.RootIdentity.Text)throw HistoryBad("HighWaterRootMismatch",DiagnosticCode.WrongTarget);
            if (lines.Length != 5 || lines[0] != "BetterAstralParty.HighWater/v1" || lines[1] != "root=" + fence.RootIdentity.Text || !lines[2].StartsWith("version=", StringComparison.Ordinal)
                || !Regex.IsMatch(lines[3], @"\Adescriptor=[0-9A-F]{64}\z") || lines[4] != "") throw HistoryBad("HighWaterInvalid");
            try {return new HistoryRecord(UpdateVersion.Parse(lines[2].Substring(8)).Tag,lines[3].Substring(11));}
            catch(UpdateValidationException){throw HistoryBad("HighWaterInvalid");}
        }
        // Keep exact historical proof handles until the caller finishes its guarded operation.
        internal static IDisposable ValidateHistory(WindowsFileFence fence,InstallReceipt receipt,UpdateTrust? trust=null) {
            var held=new Leases();try {
                var water=Maybe(fence,HighWaterPath,false);if(water!=null)held.Own(water);
                ValidateHistory(fence,receipt.Channels,water,trust??UpdateTrust.Production(),held,new HashSet<string>(StringComparer.Ordinal));
                return held;
            }catch {held.Dispose();throw;}
        }
        private static bool HistoryRow(ReceiptChannelState state,string channel,HistoryRecord water,bool exact) {
            var version=state.VersionFor(channel);
            var id=state.RepositoryIdFor(channel);
            var digest=state.DescriptorFor(channel);
            if(version==null)return false;
            if(id!=ReleaseFeedPolicy.For(channel).RepositoryId || id<=0)throw HistoryBad("HighWaterRepositoryMismatch",DiagnosticCode.WrongTarget);
            var comparison=UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse(water.Version));
            return exact?comparison==0 && digest==water.Descriptor:comparison>0;
        }
        private static void ValidateHistory(WindowsFileFence fence,ReceiptChannelState? state,WindowsFileFence.FileLease? water,UpdateTrust trust,Leases held,HashSet<string> seen) {
            try {ValidateHistoryCore(fence,state,water,trust,held,seen);}
            catch(ApplySafetyException error) {
                if(error.Data["BapRecoveryReason"]==null) {
                    error.Data["BapRecoveryReason"]=error.NativeError==2?"HighWaterProofMissing":error.Failure==ApplyFailure.FileBusy?"HighWaterProofBusy":"HighWaterProofChanged";
                    DiagnosticHub.Failure(DiagnosticFeature.Helper,DiagnosticPhase.DescriptorVerification,
                        error.NativeError==2?DiagnosticCode.Prerequisite:error.Failure==ApplyFailure.FileBusy?DiagnosticCode.FilesBusy:DiagnosticCode.Integrity,
                        error,error.NativeError,applyCode:(int)error.Failure);
                }
                throw;
            }
        }
        private static void ValidateHistoryCore(WindowsFileFence fence,ReceiptChannelState? state,WindowsFileFence.FileLease? water,UpdateTrust trust,Leases held,HashSet<string> seen) {
            if(state==null || state.Root!=fence.RootIdentity.Text)throw HistoryBad("HighWaterRootMismatch",DiagnosticCode.WrongTarget);
            if(water==null)return; // Absence is an existing optional state, not invented history.
            var record=ParseWater(Read(water,2048),fence);
            if(HistoryRow(state,"Stable",record,true) || HistoryRow(state,"PreRelease",record,true) || HistoryRow(state,"Beta",record,true))return;
            if(!HistoryRow(state,"Stable",record,false) && !HistoryRow(state,"PreRelease",record,false) && !HistoryRow(state,"Beta",record,false))throw HistoryBad("HighWaterUnbound");
            if(seen.Count>=128 || !seen.Add(water.Identity.Text))throw HistoryBad("HighWaterProofAmbiguous");
            var waterHash=water.Hash();RecoveryEntry? selected=null;Plan? selectedPlan=null;
            foreach(var entry in UpdateRecovery.Scan(fence)) {
                if(entry.Pending || !entry.Committed)continue;
                byte[] planBytes;Plan plan;
                using(var candidate=fence.OpenFile(entry.Work+"/plan",false)){planBytes=Read(candidate,16384);plan=Parse(planBytes);}
                var slot=plan.Items[InstallReceipt.Owned.Length + 1];if(slot.NewId!=water.Identity.Text || slot.NewHash!=waterHash)continue;
                if(selected!=null)throw HistoryBad("HighWaterProofAmbiguous");
                var planFile=held.Own(fence.OpenFile(entry.Work+"/plan",false));if(planFile.Hash()!=UpdateTrust.Hash(planBytes))throw HistoryBad("HighWaterProofChanged");
                if(plan.Root!=fence.RootIdentity.Text || plan.Work!=entry.Work || plan.Stage!=entry.Stage || plan.WorkId!=fence.DirectoryIdentity(entry.Work).Text || plan.StageId!=fence.DirectoryIdentity(entry.Stage).Text)
                    throw HistoryBad("HighWaterProofChanged");
                foreach(var marker in new[]{"commit-intent","committed"}) {
                    var markerFile=held.Own(fence.OpenFile(entry.Work+"/"+marker,false));
                    if(Encoding.ASCII.GetString(Read(markerFile,128))!=UpdateTrust.Hash(planBytes)+"\n")throw HistoryBad("HighWaterProofChanged");
                }
                selected=entry;selectedPlan=plan;
            }
            if(selected==null || selectedPlan==null)throw HistoryBad("HighWaterProofMissing",DiagnosticCode.Prerequisite);
            var descriptorFile=held.Own(fence.OpenFile(selected.Stage+"/descriptor.bin",false));var descriptor=Read(descriptorFile,UpdateTrust.MaxDescriptorBytes);
            var signature=Read(held.Own(fence.OpenFile(selected.Stage+"/signature.bin",false)),UpdateTrust.MaxSignatureBytes);
            var keyBytes=Read(held.Own(fence.OpenFile(selected.Stage+"/key-id.bin",false)),40);
            foreach(var value in keyBytes)if(value>127)throw HistoryBad("HighWaterProofChanged");
            var key=Encoding.ASCII.GetString(keyBytes);if(!Regex.IsMatch(key,@"\A[A-Za-z0-9_-]{1,40}\z"))throw HistoryBad("HighWaterProofChanged");
            var fields=Encoding.ASCII.GetString(descriptor).Split('\n');long release,asset,repository;
            if(fields.Length<14 || fields[0]!=UpdateTrust.DescriptorHeader || !fields[1].StartsWith("repository=",StringComparison.Ordinal) || !fields[2].StartsWith("version=",StringComparison.Ordinal) || !fields[3].StartsWith("channel=",StringComparison.Ordinal)
                || !fields[5].StartsWith("release-id=",StringComparison.Ordinal) || !fields[6].StartsWith("asset-id=",StringComparison.Ordinal) || !fields[12].StartsWith("repository-id=",StringComparison.Ordinal)
                || !long.TryParse(fields[5].Substring(11),NumberStyles.None,CultureInfo.InvariantCulture,out release) || !long.TryParse(fields[6].Substring(9),NumberStyles.None,CultureInfo.InvariantCulture,out asset) || !long.TryParse(fields[12].Substring(14),NumberStyles.None,CultureInfo.InvariantCulture,out repository))throw HistoryBad("HighWaterProofChanged");
            var historyChannel=fields[3].Substring(8);
            if(fields[11]=="purpose="+ReleaseFeedPolicy.PublicPurpose && UpdateVersion.Parse(fields[2].Substring(8)).IsPrerelease)historyChannel="PreRelease";
            var context=new UpdateContext(fields[1].Substring(11),historyChannel,"0.0.0",fields[2].Substring(8),release,asset,UpdateTrust.PlatformId,repositoryId:repository);
            VerifiedUpdateDescriptor verified;
            try {verified=trust.VerifyProposal(descriptor,key,signature,context);}
            catch(UpdateValidationException){throw HistoryBad("HighWaterProofChanged");}
            if(verified.DescriptorSha256!=selectedPlan.Descriptor || !ReleaseFeedPolicy.Matches(context))throw HistoryBad("HighWaterProofChanged");
            // A channel transition may copy a higher other-channel watermark into a new slot.
            if(verified.DescriptorSha256==record.Descriptor) {
                if(UpdateVersion.Parse(verified.Version).Tag!=record.Version ||
                    !HistoryRow(state,verified.Channel,record,false) && !(verified.PublicRelease &&
                        (HistoryRow(state,"Stable",record,false) || HistoryRow(state,"PreRelease",record,false))))throw HistoryBad("HighWaterUnbound");
            }else {
                var oldReceiptItem=selectedPlan.Items[InstallReceipt.Owned.Length];var oldReceiptFile=held.Own(fence.OpenFile(Slot(selected.Work,"old-",InstallReceipt.Owned.Length),false));
                if(oldReceiptFile.Identity.Text!=oldReceiptItem.OldId || oldReceiptFile.Hash()!=oldReceiptItem.OldHash)throw HistoryBad("HighWaterProofChanged");
                var oldReceipt=InstallReceipt.Parse(Read(oldReceiptFile,16384));
                if(oldReceipt.Channels==null || oldReceipt.Channels.ActiveVersion!=selectedPlan.Original)throw HistoryBad("HighWaterProofChanged");
                var oldWaterItem=selectedPlan.Items[InstallReceipt.Owned.Length + 1];WindowsFileFence.FileLease? oldWater=null;
                if(oldWaterItem.OldId!="-") {
                    oldWater=held.Own(fence.OpenFile(Slot(selected.Work,"old-",InstallReceipt.Owned.Length + 1),false));
                    if(oldWater.Identity.Text!=oldWaterItem.OldId || oldWater.Hash()!=oldWaterItem.OldHash)throw HistoryBad("HighWaterProofChanged");
                }
                if(UpdateTrust.Hash(WaterBytes(fence,verified,oldWater))!=waterHash)throw HistoryBad("HighWaterProofChanged");
                ValidateHistory(fence,oldReceipt.Channels,oldWater,trust,held,seen);
            }
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
        { return new UpdateContext(selection.Repository, selection.Channel, installed, selection.ReleaseTag, selection.ReleaseId, selection.AssetId, selection.Platform, highest, selection.RepositoryId, selection.Transition, channelDescriptor == "-" ? null : channelDescriptor); }
        private static ReceiptChannelState Channels(WindowsFileFence fence, InstallReceipt receipt, string original, WindowsFileFence.FileLease? water, UpdateTrust trust,Leases leases)
        {
            if (receipt.Channels == null || receipt.Channels.Root != fence.RootIdentity.Text || receipt.Channels.ActiveVersion != original) throw Bad();
            ValidateHistory(fence,receipt.Channels,water,trust,leases,new HashSet<string>(StringComparer.Ordinal));
            return receipt.Channels;
        }
        private static WindowsFileFence.FileLease? Settings(WindowsFileFence fence, Plan plan, Leases leases, bool recovering)
        {
            WindowsFileFence.FileLease? file;
            try { file = Maybe(fence, ChannelTransitionIntent.SettingsPath, false); }
            catch (ApplySafetyException error) when (error.NativeError == 3) { fence.Recheck(); file = null; }
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
            var output = new StringBuilder(Header + "\nroot=" + plan.Root + "\nwork=" + plan.Work + "\nwork-id=" + plan.WorkId
                + "\nstage=" + plan.Stage + "\nstage-id=" + plan.StageId + "\ndescriptor=" + plan.Descriptor + "\noriginal=" + plan.Original + "\ncount=" + plan.Items.Count.ToString(CultureInfo.InvariantCulture) + "\n");
            foreach (var item in plan.Items) output.Append("file=").Append(item.Path).Append('\t').Append(item.OldId).Append('\t').Append(item.OldHash).Append('\t').Append(item.NewId).Append('\t').Append(item.NewHash).Append('\n');
            if (plan.Protocol == UpdateTrust.Protocol) output.Append("settings=").Append(plan.SettingsId).Append('\t').Append(plan.SettingsHash).Append('\n');
            return Encoding.ASCII.GetBytes(output.ToString());
        }
        private static Plan Parse(byte[] bytes)
        {
            foreach (var value in bytes) if (value > 127 || value == 13) throw Bad(); var lines = Encoding.ASCII.GetString(bytes).Split('\n');
            var protocol = lines.Length == Targets().Length + 11 && lines[0] == Header ? UpdateTrust.Protocol : 0;
            if (protocol == 0 || lines[8] != "count=" + Targets().Length.ToString(CultureInfo.InvariantCulture) || lines[lines.Length - 1] != "") throw Bad();
            var keys = new[] { "root=", "work=", "work-id=", "stage=", "stage-id=", "descriptor=", "original=" }; var values = new string[7];
            for (var i = 0; i < keys.Length; i++) { if (!lines[i + 1].StartsWith(keys[i], StringComparison.Ordinal)) throw Bad(); values[i] = lines[i + 1].Substring(keys[i].Length); }
            var plan = new Plan { Protocol = protocol, Root = values[0], Work = values[1], WorkId = values[2], Stage = values[3], StageId = values[4], Descriptor = values[5], Original = values[6] };
            if (protocol == UpdateTrust.Protocol) {
                if (!lines[9 + Targets().Length].StartsWith("settings=", StringComparison.Ordinal)) throw Bad(); var cfg = lines[9 + Targets().Length].Substring(9).Split('\t');
                if (cfg.Length != 2 || (cfg[0] == "-" ? cfg[1] != "-" : !Regex.IsMatch(cfg[0], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(cfg[1], @"\A[0-9A-F]{64}\z"))) throw Bad();
                plan.SettingsId = cfg[0]; plan.SettingsHash = cfg[1];
            }
            if (!UpdateWorkPath.Is(plan.Work, "txn") || !UpdateWorkPath.Is(plan.Stage, "stage") || !Regex.IsMatch(plan.Descriptor, @"\A[0-9A-F]{64}\z")) throw Bad();
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
            var directories = new List<string>(UpdateWorkPath.Directories(fence, "txn")).ToArray();
            if (directories.Length > 128) throw Bad();
            foreach (var directory in directories) using (var leases = new Leases()) {
                var work = directory;
                if (!UpdateWorkPath.Is(work, "txn")) throw Bad(); fence.AssertPrivateDirectory(work);
                var journal = Maybe(fence, work + "/plan", false); if (journal == null) { UpdateWorkPath.RequireContained(work,"txn"); continue; } leases.Own(journal);
                var bytes = Read(journal, 16384); var plan = Parse(bytes); var hash = UpdateTrust.Hash(bytes);
                if (plan.Root != fence.RootIdentity.Text || plan.Work != work || plan.WorkId != fence.DirectoryIdentity(work).Text) throw Bad();
                var committed = Marker(fence, work + "/committed", hash, leases); var rolled = Marker(fence, work + "/rolled-back", hash, leases);
                var intent = Marker(fence, work + "/commit-intent", hash, leases);
                if (committed ? !intent || rolled : !rolled || intent) { UpdateWorkPath.RequireContained(work,"txn"); throw Bad(); }
            }
        }
        internal static TransactionResult Apply(WindowsFileFence fence, byte[] descriptor, string keyId, byte[] signature, byte[] zip,
            UpdateTrust trust, UpdateContext selection, Action<string>? boundary = null,Action? beforeGameLease=null,string? attemptWork=null,DiagnosticOperation diagnosticOperation=default,bool inProcess=false)
        {
            var previous = _inProcess; _inProcess = inProcess;
            try { return ApplyCore(fence, descriptor, keyId, signature, zip, trust, selection, boundary, beforeGameLease, attemptWork, diagnosticOperation); }
            finally { _inProcess = previous; }
        }
        private static TransactionResult ApplyCore(WindowsFileFence fence, byte[] descriptor, string keyId, byte[] signature, byte[] zip,
            UpdateTrust trust, UpdateContext selection, Action<string>? boundary, Action? beforeGameLease, string? attemptWork, DiagnosticOperation diagnosticOperation)
        {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            if (attemptWork != null && !UpdateWorkPath.Is(attemptWork, "txn")) throw Bad();
            if (attemptWork != null) UpdateWorkPath.RequireContained(attemptWork,"txn");
            using (var leases = new Leases()) {
                leases.Own(OperationLock(fence)); AssertNoPending(fence);
                WindowsFileFence.FileLease gameImage;
                try { Processes(fence); beforeGameLease?.Invoke(); gameImage=leases.Own(GameImage(fence)); }
                catch(ApplySafetyException error) when(error.Failure==ApplyFailure.FileBusy && error.NativeError==32) {throw new ApplySafetyException(ApplyFailure.ProcessBusy,32);}
                var plan = new Plan { Root = fence.RootIdentity.Text, Work = attemptWork ?? UpdateWorkPath.New("txn") }; var targets = Targets();
                foreach (var path in targets) { WindowsFileFence.FileLease? file;
                    try {file=Owned(fence,path);} catch(ApplySafetyException error) when(path==UpdateTrust.PluginPath && error.Failure==ApplyFailure.FileBusy && error.NativeError==32) {throw new ApplySafetyException(ApplyFailure.ProcessBusy,32);}
                    if (file != null) leases.Own(file);
                    plan.Items.Add(new Item { Path = path, Target = file, OldId = file?.Identity.Text ?? "-", OldHash = file?.Hash() ?? "-" }); }
                Processes(fence); var receiptFile = plan.Items[InstallReceipt.Owned.Length].Target;
                // Recheck eligibility after the original game exits, before any private
                // transaction work or owned rename. Path/identity/hash errors still fault.
                if (receiptFile == null || receiptFile.Stream.Length > 16384) throw new ApplySafetyException(ApplyFailure.InstallationRequired);
                var receiptBytesBefore = Read(receiptFile, 16384); InstallReceipt receipt;
                try { receipt = InstallReceipt.Parse(receiptBytesBefore); }
                catch (ApplySafetyException error) when (error.Failure == ApplyFailure.InvalidState) { throw new ApplySafetyException(ApplyFailure.InstallationRequired); }
                for (var i = 0; i < InstallReceipt.Owned.Length; i++) if (plan.Items[i].Target != null && plan.Items[i].OldHash != receipt.Files[plan.Items[i].Path]) throw Bad();
                var plugin = plan.Items.Find(item => item.Path == UpdateTrust.PluginPath)!;
                var installedMetadata = InstalledPluginMetadata.Read(Read(plugin.Target ?? throw Bad(), UpdateTrust.MaxFileBytes)); plan.Original = installedMetadata.Version;
                var channels = selection.RepositoryId > 0 ? Channels(fence, receipt, plan.Original, plan.Items[InstallReceipt.Owned.Length + 1].Target, trust,leases) : null;
                if(channels!=null && channels.ActiveChannel!=selection.Channel && selection.Transition==null) {
                    var error=new UpdateValidationException(UpdateFailure.WrongTarget);
                    DiagnosticHub.Failure(DiagnosticFeature.Helper,DiagnosticPhase.DescriptorVerification,DiagnosticCode.Prerequisite,error,validationCode:(int)error.Failure,operation:diagnosticOperation,targetVersion:selection.ReleaseTag);throw error;
                }
                var context = Context(selection, plan.Original, channels == null ? Water(plan.Items[InstallReceipt.Owned.Length + 1].Target, fence) : channels.Floor(selection), channels?.DescriptorFor(selection.Channel));
                var verified = trust.VerifyDescriptor(descriptor, keyId, signature, context); var payload = UpdatePackage.VerifyPayload(zip, verified);
                plan.Protocol = verified.Protocol;
                if (receipt.Channels != null && (installedMetadata.UpdateProtocol != UpdateTrust.Protocol || installedMetadata.SettingsSchema != 1)) throw Bad();
                if (selection.Transition != null) selection.Transition.Require(fence, receipt, plan.Items[InstallReceipt.Owned.Length].OldHash, plan.Items[InstallReceipt.Owned.Length + 1].Target?.Hash(), plan.Original, verified);
                var settings = Settings(fence, plan, leases, false);
                byte[] compatibility;
                using(var stream=payload.OpenRead()) using(var archive=new ZipArchive(stream,ZipArchiveMode.Read,false)) {
                    var entry=archive.GetEntry("BetterAstralParty.compatibility.json") ?? throw Bad();
                    compatibility=Array.Empty<byte>();
                    if(entry.Length<=65536) using(var input=entry.Open()) using(var output=new MemoryStream()) {input.CopyTo(output);compatibility=output.ToArray();}
                    var pluginEntry=archive.GetEntry(UpdateTrust.PluginPath) ?? throw Bad();
                    using(var input=pluginEntry.Open()) using(var output=new MemoryStream()) {
                        input.CopyTo(output);var target=InstalledPluginMetadata.Read(output.ToArray());
                        if(target.Version!=UpdateVersion.Parse(verified.Version).Tag || target.UpdateProtocol!=UpdateTrust.Protocol || target.SettingsSchema!=verified.SettingsSchema)
                            throw new ApplySafetyException(ApplyFailure.InstallationRequired);
                    }
                }
                var compatible=leases.Own(UpdateCompatibility.Require(fence,compatibility,gameImage));
                var nextChannels = channels?.Applied(fence.RootIdentity.Text, verified, selection.Transition?.Operation ?? Guid.NewGuid().ToString("N"), selection.Transition);
                var receiptBytes = receipt.Updated(verified, nextChannels); // All owned files and per-channel history share the receipt slot.
                plan.WorkId = UpdateWorkPath.Create(fence, plan.Work).Text;
                if (settings != null) { settings.Stream.Position = 0; var backup = leases.Own(fence.OpenFile(plan.Work + "/settings.backup", true, true)); settings.Stream.CopyTo(backup.Stream); backup.Flush(); if (backup.Hash() != plan.SettingsHash) throw Bad(); }
                var staged = UpdateStaging.Create(fence, descriptor, keyId, signature, zip, trust, context);
                plan.Stage = staged.Directory; plan.StageId = staged.DirectoryIdentity.Text; plan.Descriptor = staged.DescriptorSha256;
                // Recovery after a crash rebuilds an interrupted channel switch from its own stage.
                if (selection.Transition != null) UpdateTicket.Write(fence, staged.Directory + "/transition.bin", selection.Transition.Bytes());
                boundary = Guarded(fence, plan, boundary,diagnosticOperation,selection.ReleaseTag);
                using (var locked = UpdateStaging.VerifyLocked(fence, staged, trust, context)) {
                    using (var stream = locked.Payload.OpenRead()) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false)) {
                        for (var i = 0; i < InstallReceipt.Owned.Length; i++) {
                            var item = plan.Items[i]; var entry = archive.GetEntry(item.Path) ?? throw Bad(); var file = leases.Own(fence.OpenFile(Slot(plan.Work, "new-", i), true, true));
                            using (var input = entry.Open()) { var buffer = new byte[32768]; long total = 0; int count;
                                while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { total += count; if (total > UpdateTrust.MaxFileBytes) throw Bad(); file.Stream.Write(buffer, 0, count); } }
                            file.Flush(); item.New = file; item.NewId = file.Identity.Text; item.NewHash = file.Hash();
                            var signed = verified.Files.FindByPath(item.Path); if (item.NewHash != signed.Sha256 || file.Stream.Length != signed.Bytes) throw Bad();
                        }
                    }
                    var targetMetadata = InstalledPluginMetadata.Read(Read(plugin.New ?? throw Bad(), UpdateTrust.MaxFileBytes));
                    if (targetMetadata.Version != UpdateVersion.Parse(verified.Version).Tag || targetMetadata.UpdateProtocol != UpdateTrust.Protocol || targetMetadata.SettingsSchema != verified.SettingsSchema) throw new ApplySafetyException(ApplyFailure.InstallationRequired);
                    for (var i = InstallReceipt.Owned.Length; i < InstallReceipt.Owned.Length + 2; i++) { var file = Write(fence, Slot(plan.Work, "new-", i), i == InstallReceipt.Owned.Length ? receiptBytes : WaterBytes(fence, verified, plan.Items[InstallReceipt.Owned.Length + 1].Target), leases);
                        var item = plan.Items[i]; item.New = file; item.NewId = file.Identity.Text; item.NewHash = file.Hash(); }
                    var planBytes = Serialize(plan); var hash = UpdateTrust.Hash(planBytes); Hit(boundary, "before:prepared"); Write(fence, plan.Work + "/plan", planBytes, leases); Hit(boundary, "after:prepared");
                    locked.Recheck(); fence.AssertPrivateDirectory(plan.Work); Processes(fence);
                    compatible.Recheck(); // Last read-only compatibility check before the first owned rename.
                    for (var i = 0; i < InstallReceipt.Owned.Length + 1; i++) Install(fence, plan, i, boundary);
                    Mark(fence, plan.Work + "/commit-intent", hash, leases, "commit-intent", boundary);
                    Install(fence, plan, InstallReceipt.Owned.Length + 1, boundary); // High-water is last; intent makes recovery finish forward.
                    Mark(fence, plan.Work + "/committed", hash, leases, "committed", boundary); return TransactionResult.Committed;
                }
            }
        }
        internal static TransactionResult Recover(WindowsFileFence fence, string work, UpdateTrust trust, UpdateContext selection, Action<string>? boundary = null,DiagnosticOperation diagnosticOperation=default,bool inProcess=false)
        {
            var previous = _inProcess; _inProcess = inProcess;
            try { return RecoverCore(fence, work, trust, selection, boundary, diagnosticOperation); }
            finally { _inProcess = previous; }
        }
        private static TransactionResult RecoverCore(WindowsFileFence fence, string work, UpdateTrust trust, UpdateContext selection, Action<string>? boundary, DiagnosticOperation diagnosticOperation)
        {
            UpdateWorkPath.RequireContained(work,"txn");
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            if (!UpdateWorkPath.Is(work, "txn")) throw Bad();
            using (var leases = new Leases()) {
                leases.Own(OperationLock(fence)); Processes(fence); leases.Own(GameImage(fence)); fence.AssertPrivateDirectory(work);
                var journal = Maybe(fence, work + "/plan", false); if (journal == null) return TransactionResult.Abandoned; leases.Own(journal);
                var bytes = Read(journal, 16384); var plan = Parse(bytes); var hash = UpdateTrust.Hash(bytes);
                boundary = Guarded(fence, plan, boundary,diagnosticOperation,selection.ReleaseTag);
                if (plan.Work != work || plan.Root != fence.RootIdentity.Text || plan.WorkId != fence.DirectoryIdentity(work).Text) throw Bad();
                fence.AssertPrivateDirectory(plan.Stage); if (plan.StageId != fence.DirectoryIdentity(plan.Stage).Text) throw Bad();
                var evidence = new List<WindowsFileFence.FileLease>(); foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" }) evidence.Add(leases.Own(fence.OpenFile(plan.Stage + "/" + name)));
                for (var i = 0; i < InstallReceipt.Owned.Length + 2; i++) {
                    var item = plan.Items[i]; item.Target = Owned(fence, item.Path); item.Backup = Loaded(fence, Slot(work, "old-", i), item.Path); item.New = Loaded(fence, Slot(work, "new-", i), item.Path);
                    foreach (var file in new[] { item.Target, item.Backup, item.New }) if (file != null) leases.Own(file); Classify(item);
                }
                Processes(fence); var plugin = plan.Items.Find(item => item.Path == UpdateTrust.PluginPath)!;
                if (InstalledPluginMetadata.Read(Read(Original(plugin), UpdateTrust.MaxFileBytes)).Version != plan.Original) throw Bad();
                var originalReceipt = InstallReceipt.Parse(Read(Original(plan.Items[InstallReceipt.Owned.Length]), 16384));
                for (var i = 0; i < InstallReceipt.Owned.Length; i++) if (plan.Items[i].OldId != "-" && plan.Items[i].OldHash != originalReceipt.Files[plan.Items[i].Path]) throw Bad();
                var waterItem = plan.Items[InstallReceipt.Owned.Length + 1]; var oldWater = waterItem.OldId == "-" ? null : Original(waterItem);
                var channels = selection.RepositoryId > 0 ? Channels(fence, originalReceipt, plan.Original, oldWater, trust,leases) : null;
                var context = Context(selection, plan.Original, channels == null ? Water(oldWater, fence) : channels.Floor(selection), channels?.DescriptorFor(selection.Channel));
                var descriptor = trust.VerifyDescriptor(Read(evidence[0], UpdateTrust.MaxDescriptorBytes), Encoding.ASCII.GetString(Read(evidence[2], 40)), Read(evidence[1], UpdateTrust.MaxSignatureBytes), context);
                if (descriptor.DescriptorSha256 != plan.Descriptor || descriptor.Files.Count != InstallReceipt.Owned.Length) throw Bad();
                if (descriptor.Protocol != plan.Protocol) throw Bad(); Settings(fence, plan, leases, true);
                if (selection.Transition != null) {
                    byte[]? originalSettings = null;
                    if (plan.SettingsId != "-") using (var backup = fence.OpenFile(plan.Work + "/settings.backup")) originalSettings = Read(backup, 1024 * 1024);
                    selection.Transition.RequireRecovery(fence, originalReceipt, plan.Items[InstallReceipt.Owned.Length].OldHash, oldWater?.Hash(), plan.Original, descriptor, plan.SettingsHash, originalSettings);
                }
                UpdatePackage.VerifyPayload(Read(evidence[3], UpdateTrust.MaxZipBytes), descriptor);
                for (var i = 0; i < InstallReceipt.Owned.Length; i++) {
                    var item = plan.Items[i]; var signed = descriptor.Files.FindByPath(item.Path); var file = item.State == 2 ? item.Target : item.New;
                    if (item.NewHash != signed.Sha256 || file == null || file.Stream.Length != signed.Bytes) throw Bad();
                }
                var newReceiptFile = plan.Items[InstallReceipt.Owned.Length].State == 2 ? plan.Items[InstallReceipt.Owned.Length].Target : plan.Items[InstallReceipt.Owned.Length].New;
                var expectedChannels = newReceiptFile == null ? null : InstallReceipt.Parse(Read(newReceiptFile, 16384)).Channels;
                if (channels != null) {
                    if (expectedChannels == null || selection.Transition != null && expectedChannels.Operation != selection.Transition.Operation) throw Bad();
                    expectedChannels = channels.Applied(fence.RootIdentity.Text, descriptor, expectedChannels.Operation, selection.Transition);
                }
                if (plan.Items[InstallReceipt.Owned.Length].NewHash != UpdateTrust.Hash(originalReceipt.Updated(descriptor, expectedChannels)) || plan.Items[InstallReceipt.Owned.Length + 1].NewHash != UpdateTrust.Hash(WaterBytes(fence, descriptor, oldWater))) throw Bad();
                var newPlugin = plugin.State == 2 ? plugin.Target : plugin.New;
                var newMetadata = InstalledPluginMetadata.Read(Read(newPlugin ?? throw Bad(), UpdateTrust.MaxFileBytes));
                if (newMetadata.Version != UpdateVersion.Parse(descriptor.Version).Tag || newMetadata.UpdateProtocol != UpdateTrust.Protocol || newMetadata.SettingsSchema != descriptor.SettingsSchema) throw Bad();
                var intent = Marker(fence, work + "/commit-intent", hash, leases); var committed = Marker(fence, work + "/committed", hash, leases); var rolled = Marker(fence, work + "/rolled-back", hash, leases);
                var states = new int[InstallReceipt.Owned.Length + 2]; for (var i = 0; i < InstallReceipt.Owned.Length + 2; i++) states[i] = plan.Items[i].State;
                var direction = UpdateRecoveryPolicy.Direction(states, intent, committed, rolled);
                fence.AssertPrivateDirectory(work); fence.AssertPrivateDirectory(plan.Stage);
                if (direction == RecoveryDirection.CompleteTarget) {
                    Install(fence, plan, InstallReceipt.Owned.Length + 1, boundary); VerifyResolved(fence, plan, true);
                    if (!committed) Mark(fence, work + "/committed", hash, leases, "committed", boundary);
                    return TransactionResult.Committed;
                }
                if (rolled) { VerifyResolved(fence, plan, false); return TransactionResult.RolledBack; }
                DiagnosticHub.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Rollback,DiagnosticOutcome.Begin,diagnosticOperation,selection.ReleaseTag);
                for (var i = InstallReceipt.Owned.Length; i >= 0; i--) Rollback(fence, plan, i, boundary);
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
