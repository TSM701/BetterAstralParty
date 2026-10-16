#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating {
    // Explicit approved-whole installation only. Ordinary HelperDelivery never replaces a helper.
    // An authenticated durable intent precedes owned8/receipt replacement. Helper commit follows that transaction.
    // Old objects are archived by handle rename, keeping their identities and signed provenance.
    internal static class WholeInstallBridge {
        private static ApplySafetyException Bad(string? reason=null) {
            var error=new ApplySafetyException(ApplyFailure.RecoveryRequired);
            if(reason!=null)error.Data["BapRecoveryReason"]=reason;
            return error;
        }
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence f, string p, bool mutable = false) {
            try { return f.OpenFile(p, mutable); } catch (ApplySafetyException e) when(e.NativeError == 2) { return null; }
        }
        private static void Write(WindowsFileFence f, string p, byte[] b) {
            using(var l=f.OpenFile(p,true,true)) { l.Stream.Write(b,0,b.Length);l.Flush(); }
        }
        private static string Slot(WindowsFileFence.FileLease? f) { return f == null ? "-\t-" : f.Identity.Text+"\t"+f.Hash(); }
        private static bool IsSlot(string s) { return s=="-\t-" || Regex.IsMatch(s,@"\A[0-9A-F]{8}:[0-9A-F]{16}\t[0-9A-F]{64}\z"); }
        private static byte[] Ascii(string s) { return Encoding.ASCII.GetBytes(s); }
        private static string[] Lines(byte[] b, int count, string header) {
            foreach(var c in b) if(c>127 || c==13) throw Bad();
            var l=Encoding.ASCII.GetString(b).Split('\n'); if(l.Length!=count || l[0]!=header || l[count-1]!="") throw Bad();return l;
        }
        internal static VerifiedUpdateDescriptor VerifyUpdateProof(byte[] bytes,byte[] signature) {
            var envelope=DetachedSignature.Parse(signature);
            var l=Encoding.ASCII.GetString(bytes).Split('\n');
            var local=l.Length>0 && l[0]=="BetterAstralParty.LocalInstallDescriptor/v1";
            if(l.Length<15 || !local && l[0]!="BetterAstralParty.UpdateDescriptor/v4" || !l[1].StartsWith("repository=",StringComparison.Ordinal) || !l[2].StartsWith("version=",StringComparison.Ordinal) || !l[3].StartsWith("channel=",StringComparison.Ordinal) || !l[5].StartsWith("release-id=",StringComparison.Ordinal) || !l[6].StartsWith("asset-id=",StringComparison.Ordinal) || !l[12].StartsWith("repository-id=",StringComparison.Ordinal)) throw Bad();
            long release,asset,id;
            if(!long.TryParse(l[5].Substring(11),NumberStyles.None,CultureInfo.InvariantCulture,out release) || !long.TryParse(l[6].Substring(9),NumberStyles.None,CultureInfo.InvariantCulture,out asset) || !long.TryParse(l[12].Substring(14),NumberStyles.None,CultureInfo.InvariantCulture,out id)) throw Bad();
            if(local && (release!=0 || asset!=0)) throw Bad();
            var channel=l[3].Substring(8);
            if(!local && l[11]=="purpose="+ReleaseFeedPolicy.PublicPurpose && UpdateVersion.Parse(l[2].Substring(8)).IsPrerelease)channel="PreRelease";
            var context=local ? UpdateContext.LocalInstall(l[1].Substring(11),channel,l[2].Substring(8),id)
                : new UpdateContext(l[1].Substring(11),channel,"0.0.0",l[2].Substring(8),release,asset,UpdateTrust.PlatformId,repositoryId:id);
            var trust=UpdateTrust.Production();
            return local ? trust.VerifyLocalInstall(bytes,envelope.KeyId,envelope.Signature,context)
                : trust.VerifyProposal(bytes,envelope.KeyId,envelope.Signature,context);
        }
        private sealed class Inputs : IDisposable {
            internal readonly WindowsFileFence Source;
            internal readonly VerifiedUpdateDescriptor Update;
            internal readonly HelperDescriptor Helper;
            internal readonly byte[] HelperBytes, HelperDescriptorBytes, HelperSignature, UpdateDescriptorBytes, UpdateSignatureBytes;
            internal readonly string ManifestHash;
            internal byte[]? PriorDescriptor, PriorSignature;
            private readonly List<IDisposable> _leases=new List<IDisposable>();
            private byte[] Read(string p, long limit) { var f=Source.OpenLaunchImage(p);_leases.Add(f);return UpdateTicket.Read(f,limit); }
            internal bool PriorProof(string digest) {
                if(!Digest(digest))throw Bad("PriorProofDigestInvalid");
                try {PriorDescriptor=Read("update-proof/prior/"+digest+".descriptor",UpdateTrust.MaxDescriptorBytes);}
                catch(ApplySafetyException e) when(e.NativeError==2 || e.NativeError==3) {return false;}
                PriorSignature=Read("update-proof/prior/"+digest+".signature",1024);
                if(UpdateTrust.Hash(PriorDescriptor)!=digest)throw Bad("PriorProofDigestMismatch");
                return true;
            }
            internal Inputs(string root,string package,string version,bool verifyPayload=true) {
                Source=new WindowsFileFence(package);
                try {
                    var actual=WindowsPath.Absolute(root).TrimEnd('\\');
                    if(actual.Equals(Source.Root,StringComparison.OrdinalIgnoreCase) || actual.StartsWith(Source.Root+"\\",StringComparison.OrdinalIgnoreCase) || Source.Root.StartsWith(actual+"\\",StringComparison.OrdinalIgnoreCase)) throw Bad();
                    ManifestHash=UpdateTrust.Hash(Read("package-manifest.json",1024*1024));
                    UpdateDescriptorBytes=Read("update-proof/update.descriptor",UpdateTrust.MaxDescriptorBytes);
                    UpdateSignatureBytes=Read("update-proof/update.signature",1024);
                    Update=VerifyUpdateProof(UpdateDescriptorBytes,UpdateSignatureBytes);
                    if(Update.Version!=version || Update.Protocol!=4 || Update.Files.Count!=InstallReceipt.Owned.Length) throw Bad();
                    if(!verifyPayload) {Helper=null!;HelperBytes=HelperDescriptorBytes=HelperSignature=Array.Empty<byte>();return;}
                    foreach(var file in Update.Files) {
                        var name=file.Path==UpdateTrust.PluginPath ? "artifacts/BetterAstralParty.dll" : file.Path=="BetterAstralParty-Launcher.exe" || file.Path=="BetterAstralParty.compatibility.json" ? "artifacts/"+file.Path : "tools/"+file.Path;
                        var payload=Read(name,UpdateTrust.MaxFileBytes);
                        if(payload.LongLength!=file.Bytes || UpdateTrust.Hash(payload)!=file.Sha256) throw Bad();
                        if(file.Path==UpdateTrust.PluginPath) {
                            var metadata=InstalledPluginMetadata.Read(payload);
                            if(metadata.Version!=version || metadata.UpdateProtocol!=4 || metadata.SettingsSchema!=1)throw Bad();
                        }
                    }
                    HelperDescriptorBytes=Read("helper-bootstrap/helper.descriptor",UpdateTrust.MaxDescriptorBytes);
                    HelperSignature=Read("helper-bootstrap/helper.signature",1024);
                    HelperBytes=Read("helper-bootstrap/"+HelperDescriptor.Image,UpdateTrust.MaxFileBytes);
                    Helper=HelperDescriptor.Verify(UpdateTrust.Production(),HelperDescriptorBytes,DetachedSignature.Parse(HelperSignature),HelperBytes,version);
                    if(Helper.Protocol<4) throw Bad();
                } catch {Dispose();throw;}
            }
            public void Dispose() {for(var i=_leases.Count-1;i>=0;i--)_leases[i].Dispose();_leases.Clear();Source.Dispose();}
        }
        internal static void ValidateInputs(string root,string package,string version) { using(var input=new Inputs(root,package,version)) { } }
        internal static void ValidateMaintenancePaths(string root,string journalRoot) {
            WindowsFileFence.ValidatePlannedPath(root);WindowsFileFence.ValidatePlannedPath(journalRoot);
            var id=new string('a',32);
            foreach(var path in InstallReceipt.Owned)WindowsFileFence.ValidatePlannedPath(Path.Combine(root,path.Replace('/','\\')));
            foreach(var path in new[]{InstallReceipt.Path,HelperDescriptor.Image,HelperDelivery.Receipt,UpdateTransaction.HighWaterPath,UpdateFaults.OperationPath,UpdateFaults.FailurePath})
                WindowsFileFence.ValidatePlannedPath(Path.Combine(root,path));
            foreach(var prefix in new[]{"bap-v4-wholeintent-","bap-v4-wholehist-"})
                foreach(var leaf in new[]{"old-plugin","old-receipt","target-receipt","signed-update.descriptor","signed-update.signature","plan","committed","cancelled"})
                    WindowsFileFence.ValidatePlannedPath(Path.Combine(root,UpdateWorkPath.Root,prefix+id,leaf));
            foreach(var prefix in new[]{"bap-v4-helperbridge-","bap-v4-helper-"})
                foreach(var leaf in new[]{"descriptor","signature","image","receipt","plan","committed","rolled-back"})
                    WindowsFileFence.ValidatePlannedPath(Path.Combine(root,UpdateWorkPath.Root,prefix+id,leaf));
            foreach(var path in new[]{"pending.json","pending.json.lock",id+"/pending.json",id+"/completed.json",id+"/restored.json",id+"/299.bin"})
                WindowsFileFence.ValidatePlannedPath(Path.Combine(journalRoot,path.Replace('/','\\')));
        }
        private static bool MatchesInstalledProof(WindowsFileFence f,InstallReceipt receipt,VerifiedUpdateDescriptor proof) {
            var state=receipt.Channels;
            if(state==null || state.Root!=f.RootIdentity.Text || state.ActiveVersion!=UpdateVersion.Parse(proof.Version).Tag ||
                !proof.AllowsChannel(state.ActiveChannel) || state.ActiveRepositoryId!=proof.RepositoryId ||
                proof.Protocol!=4 || proof.SettingsSchema!=1 || proof.Files.Count!=InstallReceipt.Owned.Length ||
                state.DescriptorFor(state.ActiveChannel)!=proof.DescriptorSha256)return false;
            foreach(var item in proof.Files)if(!receipt.Files.TryGetValue(item.Path,out var hash) || hash!=item.Sha256)return false;
            return true;
        }
        private static byte[] ProofFile(WindowsFileFence f,string path,long limit,Dictionary<string,string> pins) {
            using(var file=f.OpenFile(path)) {
                var bytes=UpdateTicket.Read(file,limit);pins.Add(path,file.Hash());return bytes;
            }
        }
        // A receipt's claimed version never grants ownership without an exact signed owned8 proof.
        private static VerifiedUpdateDescriptor SignedInstalledProof(WindowsFileFence f,byte[] receiptBytes,Inputs? input,out Dictionary<string,string> pins) {
            var receipt=InstallReceipt.Parse(receiptBytes);pins=new Dictionary<string,string>(StringComparer.Ordinal);
            if(input!=null && MatchesInstalledProof(f,receipt,input.Update)){input.PriorDescriptor=input.PriorSignature=null;return input.Update;}
            var state=receipt.Channels??throw Bad("SignedInstalledProofRequired");
            var digest=state.DescriptorFor(state.ActiveChannel);
            var receiptHash=UpdateTrust.Hash(receiptBytes);VerifiedUpdateDescriptor? selected=null;
            foreach(var entry in UpdateRecovery.Scan(f)) {
                if(entry.Pending || !entry.Committed || entry.Descriptor!=digest)continue;
                var candidatePins=new Dictionary<string,string>(StringComparer.Ordinal);
                var plan=ProofFile(f,entry.Work+"/plan",16384,candidatePins);
                var lines=Encoding.ASCII.GetString(plan).Split('\n');var matches=0;
                foreach(var line in lines)if(line.StartsWith("file="+InstallReceipt.Path+"\t",StringComparison.Ordinal)) {
                    var row=line.Split('\t');
                    if(row.Length!=5 || !Regex.IsMatch(row[3],@"\A[A-F0-9]{8}:[A-F0-9]{16}\z") || row[4]!=receiptHash)throw Bad("HistoricalReceiptMismatch");
                    matches++;
                }
                if(matches!=1)throw Bad("HistoricalReceiptMismatch");
                foreach(var marker in new[]{"commit-intent","committed"})
                    if(Encoding.ASCII.GetString(ProofFile(f,entry.Work+"/"+marker,128,candidatePins))!=UpdateTrust.Hash(plan)+"\n")throw Bad("HistoricalCommitMismatch");
                var bytes=ProofFile(f,entry.Stage+"/descriptor.bin",UpdateTrust.MaxDescriptorBytes,candidatePins);
                var signature=ProofFile(f,entry.Stage+"/signature.bin",UpdateTrust.MaxSignatureBytes,candidatePins);
                var key=ProofFile(f,entry.Stage+"/key-id.bin",40,candidatePins);
                foreach(var b in key)if(b>127)throw Bad("HistoricalSigningKeyInvalid");
                var keyId=Encoding.ASCII.GetString(key);
                if(!Regex.IsMatch(keyId,@"\A[A-Za-z0-9_-]{1,40}\z"))throw Bad("HistoricalSigningKeyInvalid");
                var envelope=new DetachedSignature(keyId,signature).Bytes();
                var verified=VerifyUpdateProof(bytes,envelope);
                if(!MatchesInstalledProof(f,receipt,verified))throw Bad("HistoricalOwnedProofMismatch");
                if(selected==null){selected=verified;pins=candidatePins;}
                else if(selected.DescriptorSha256!=verified.DescriptorSha256)throw Bad("HistoricalProofAmbiguous");
            }
            // New whole installations retain the already verified proof with their durable intent.
            foreach(var work in IntentDirectories(f)) {
                f.AssertPrivateDirectory(work);
                using(var descriptor=Maybe(f,work+"/signed-update.descriptor"))using(var signature=Maybe(f,work+"/signed-update.signature")) {
                    if(descriptor==null && signature==null)continue;
                    if(descriptor==null || signature==null)throw Bad("WholeProofIncomplete");
                    var candidatePins=new Dictionary<string,string>(StringComparer.Ordinal);
                    var plan=ProofFile(f,work+"/plan",262144,candidatePins);var intent=WholeIntent.Parse(plan);
                    if(intent.Root!=f.RootIdentity.Text || intent.Work!=LogicalIntent(work) || intent.WorkId!=f.DirectoryIdentity(work).Text)throw Bad("WholeProofRootMismatch");
                    using(var committed=Maybe(f,work+"/committed")) {
                        if(committed==null)continue;
                        if(Encoding.ASCII.GetString(UpdateTicket.Read(committed,128))!=UpdateTrust.Hash(plan)+"\n")throw Bad("WholeProofCommitMismatch");
                        candidatePins.Add(work+"/committed",committed.Hash());
                    }
                    var bytes=UpdateTicket.Read(descriptor,UpdateTrust.MaxDescriptorBytes);
                    var envelope=UpdateTicket.Read(signature,1024);
                    var verified=VerifyUpdateProof(bytes,envelope);
                    if(!MatchesInstalledProof(f,receipt,verified))continue;
                    var target=ProofFile(f,work+"/target-receipt",16384,candidatePins);
                    if(UpdateTrust.Hash(target)!=intent.Receipt)throw Bad("WholeProofReceiptMismatch");
                    if(intent.Receipt!=receiptHash)continue; // A valid older installation generation is not this receipt.
                    candidatePins.Add(work+"/signed-update.descriptor",descriptor.Hash());
                    candidatePins.Add(work+"/signed-update.signature",signature.Hash());
                    // Multiple deliveries of the exact same signed owned8 proof are harmless.
                    if(selected!=null && selected.DescriptorSha256!=verified.DescriptorSha256)throw Bad("HistoricalProofAmbiguous");
                    if(selected==null){selected=verified;pins=candidatePins;}
                }
            }
            if(selected==null && input!=null && input.PriorProof(digest)) {
                var verified=VerifyUpdateProof(input.PriorDescriptor!,input.PriorSignature!);
                if(!MatchesInstalledProof(f,receipt,verified))throw Bad("PriorInstalledProofMismatch");
                return verified;
            }
            return selected??throw Bad("SignedInstalledProofRequired");
        }
        internal static string RemovedInstallEvidence(string root,string package,string version,byte[] receiptBytes) {
            using(var input=new Inputs(root,package,version))using(var f=new WindowsFileFence(root)) {
                UpdateLaunchGate.Check(f);UpdateTransaction.ReadHighWater(f);
                using var history=UpdateTransaction.ValidateHistory(f,InstallReceipt.Parse(receiptBytes));
                var proof=SignedInstalledProof(f,receiptBytes,input,out var pins);
                var names=new List<string>(pins.Keys);names.Sort(StringComparer.Ordinal);
                var text="BetterAstralParty.RemovedInstallEvidence/v1\nroot="+f.RootIdentity.Text+"\nversion="+UpdateVersion.Parse(proof.Version).Tag+
                    "\ndescriptor="+proof.DescriptorSha256+"\nupdate-protocol=4\nsettings-schema=1\n";
                foreach(var name in names)text+="proof="+name+"="+pins[name]+"\n";
                return text;
            }
        }
        internal static void ValidatePartialRemoval(string root) {ValidatePartialRemovalCore(root,null);}
        internal static void ValidatePartialRemovalWithPackage(string root,string package,string version) {
            using(var input=new Inputs(root,package,version,false))ValidatePartialRemovalCore(root,input);
        }
        private static void ValidatePartialRemovalCore(string root,Inputs? input) {
            using(var fence=new WindowsFileFence(root))using(var file=fence.OpenFile(InstallReceipt.Path)) {
                UpdateLaunchGate.Check(fence);UpdateTransaction.ReadHighWater(fence);
                using var history=UpdateTransaction.ValidateHistory(fence,InstallReceipt.Parse(UpdateTicket.Read(file,16384)));
                var bytes=UpdateTicket.Read(file,16384);var receipt=InstallReceipt.Parse(bytes);
                foreach(var name in InstallReceipt.Owned)using(var owned=Maybe(fence,name))if(owned!=null && owned.Hash()!=receipt.Files[name])throw Bad("OwnedFileChanged");
                using(var plugin=Maybe(fence,UpdateTrust.PluginPath)) {
                    if(plugin==null)SignedInstalledProof(fence,bytes,input,out _);
                    else {
                        var current=InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes));
                        var state=receipt.Channels;
                        if(state==null || state.Root!=fence.RootIdentity.Text || state.ActiveVersion!=current.Version || state.ActiveRepositoryId!=ReleaseFeedPolicy.For(state.ActiveChannel).RepositoryId || current.UpdateProtocol!=4 || current.SettingsSchema!=1)throw Bad();
                    }
                }
                using(var helper=Maybe(fence,HelperDescriptor.Image))using(var helperReceipt=Maybe(fence,HelperDelivery.Receipt))
                    if((helper!=null || helperReceipt!=null) && HelperDelivery.Fingerprint(fence,UpdateTrust.Production())==null)throw Bad();
            }
        }
        private sealed class Observation {
            internal string Nonce="00000000000000000000000000000000",Root="",Plugin="-",Receipt="-",Image="-\t-",HelperReceipt="-\t-",Water="-\t-",Update="",Helper="",Mode="";
            internal string Text() {return "BetterAstralParty.WholeObservation/v1\nroot="+Root+"\nplugin="+Plugin+"\nreceipt="+Receipt+"\nimage="+Image+"\nhelper-receipt="+HelperReceipt+"\nwater="+Water+"\nupdate="+Update+"\nhelper="+Helper+"\nmode="+Mode+"\nnonce="+Nonce+"\n";}
            internal static Observation Parse(string text) {
                if(text.Length>2048) throw Bad();var l=Lines(Ascii(text),12,"BetterAstralParty.WholeObservation/v1");
                var keys=new[]{"root=","plugin=","receipt=","image=","helper-receipt=","water=","update=","helper=","mode=","nonce="};var v=new string[10];
                for(var i=0;i<keys.Length;i++){if(!l[i+1].StartsWith(keys[i],StringComparison.Ordinal))throw Bad();v[i]=l[i+1].Substring(keys[i].Length);}
                if(!Regex.IsMatch(v[0],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !DigestOrAbsent(v[1]) || !DigestOrAbsent(v[2]) || !IsSlot(v[3]) || !IsSlot(v[4]) || !IsSlot(v[5]) || !Digest(v[6]) || !Digest(v[7]) || !Regex.IsMatch(v[8],@"\A(?:Fresh|HelperUpgrade|Current|Recovery)\z") || !Regex.IsMatch(v[9],@"\A[0-9a-f]{32}\z"))throw Bad();
                var o=new Observation {Root=v[0],Plugin=v[1],Receipt=v[2],Image=v[3],HelperReceipt=v[4],Water=v[5],Update=v[6],Helper=v[7],Mode=v[8],Nonce=v[9]};if(o.Text()!=text)throw Bad();return o;
            }
        }
        private static bool Digest(string h) {return Regex.IsMatch(h,@"\A[0-9A-F]{64}\z");}
        private static bool DigestOrAbsent(string h) {return h=="-" || Digest(h);}
        private static void MatchInputs(WindowsFileFence f,Inputs input,Observation o) {if(o.Root!=f.RootIdentity.Text || o.Update!=input.Update.DescriptorSha256 || o.Helper!=input.Helper.DescriptorHash)throw Bad();}
        private static void OtherState(WindowsFileFence f,List<MigrationPlan> pending,string? ordinaryJournalHash=null) {
            UpdateLaunchGate.AssertNoReinstall(f);
            if(AutomaticHelperUpgrade.Pending(f))throw Bad("HelperUpgradePending");
            UpdateFaults.Read(f); // Retain valid failure latch; malformed evidence is a blocker.
            if(UpdateFaults.ReadFile(f,UpdateFaults.OperationPath)!=null)throw Bad("UpdateOperationPending");
            foreach(var e in UpdateRecovery.Scan(f))if(e.Pending)throw Bad("UpdateTransactionPending"); // Old signed helper must first finish old journals.
            if(HelperDelivery.PendingInstall(f)) {
                var ordinaryWork=ordinaryJournalHash==null?null:OrdinaryInstallRecovery.ValidateHelperRepair(f,ordinaryJournalHash);
                if(pending.Count!=1 && ordinaryWork==null)throw Bad("HelperInstallPending");
                foreach(var d in new List<string>(UpdateWorkPath.Directories(f, "helper")).ToArray()) {
                    var work=d;f.AssertPrivateDirectory(work);
                    using(var plan=Maybe(f,work+"/plan"))using(var committed=Maybe(f,work+"/committed"))
                        if(plan!=null && committed==null && work!=ordinaryWork && (pending.Count!=1 || work!=pending[0].HelperWork))throw Bad("HelperInstallConflict");
                }
            }
        }
        private static InstallReceipt TargetReceipt(WindowsFileFence f,Inputs input,byte[] bytes) {
            var receipt=InstallReceipt.Parse(bytes);
            if(receipt.Channels==null || receipt.Channels.Root!=f.RootIdentity.Text || receipt.Channels.ActiveChannel!=input.Update.Channel || receipt.Channels.ActiveVersion!=input.Update.Version || receipt.Channels.DescriptorFor(input.Update.Channel)!=input.Update.DescriptorSha256)throw Bad();
            foreach(var entry in input.Update.Files)if(receipt.Files[entry.Path]!=entry.Sha256)throw Bad();
            return receipt;
        }
        private static bool MatchesTarget(WindowsFileFence f,Inputs input,string receiptHash,WindowsProcessGuard.LaunchLease? processes=null) {
            using(var file=Maybe(f,InstallReceipt.Path)) {
                if(file==null || file.Hash()!=receiptHash)return false;TargetReceipt(f,input,UpdateTicket.Read(file,16384));
                foreach(var entry in input.Update.Files) {
                    if(processes!=null && entry.Path=="BetterAstralParty-Launcher.exe") {if(processes.ImageHash(f.Full(entry.Path))!=entry.Sha256)return false;}
                    else using(var payload=Maybe(f,entry.Path))if(payload==null || payload.Hash()!=entry.Sha256)return false;
                }
                return true;
            }
        }
        private static void Target(WindowsFileFence f,Inputs input,string receiptHash,WindowsProcessGuard.LaunchLease? processes=null) {
            if(!MatchesTarget(f,input,receiptHash,processes))throw Bad();
        }
        internal static void ValidateWholeChannel(ReceiptChannelState? state,string root,string currentVersion,int currentProtocol,int currentSettings,VerifiedUpdateDescriptor incoming,string? high) {
            if(state==null)throw Bad();
            if(state.Root!=root || state.ActiveVersion!=currentVersion || state.ActiveRepositoryId!=ReleaseFeedPolicy.For(state.ActiveChannel).RepositoryId)throw Bad();
            // The chosen, signed whole package may switch channels only by advancing the installed version.
            var feed=ReleaseFeedPolicy.For(incoming.Channel);
            if(currentProtocol!=4 || currentSettings!=1 || incoming.Protocol!=4 || incoming.SettingsSchema!=1 || incoming.Files.Count!=InstallReceipt.Owned.Length
                || incoming.Repository!=feed.Repository || incoming.RepositoryId!=feed.RepositoryId
                || !(incoming.ReleaseId>0 && incoming.AssetId>0 || incoming.ReleaseId==0 && incoming.AssetId==0))throw Bad();
            if(state.ActiveChannel==incoming.Channel)return;
            var next=UpdateVersion.Parse(incoming.Version);
            if(next.CompareTo(UpdateVersion.Parse(currentVersion))<=0 || high!=null && next.CompareTo(UpdateVersion.Parse(high))<=0)
                throw new UpdateValidationException(UpdateFailure.NotNewer);
        }
        private static string Observe(string root,string package,string version,string? review=null) {
            using(var input=new Inputs(root,package,version))using(var f=new WindowsFileFence(root)) {
                var pending=Scan(f);OtherState(f,pending);if(ScanIntents(f).Count!=0)throw Bad();
                var o=new Observation {Root=f.RootIdentity.Text,Update=input.Update.DescriptorSha256,Helper=input.Helper.DescriptorHash};
                var high=UpdateTransaction.ReadHighWater(f);
                using(var water=Maybe(f,UpdateTransaction.HighWaterPath))o.Water=Slot(water);
                if(high!=null && UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse(high))<0)throw new UpdateValidationException(UpdateFailure.NotNewer);
                using(var receiptFile=Maybe(f,InstallReceipt.Path)) {
                    o.Receipt=receiptFile?.Hash()??"-";
                    if(receiptFile!=null) {
                        var receipt=InstallReceipt.Parse(UpdateTicket.Read(receiptFile,16384));
                        using var history=UpdateTransaction.ValidateHistory(f,receipt);
                        using(var plugin=Maybe(f,UpdateTrust.PluginPath)) {
                            var signed=plugin==null || review!=null ? SignedInstalledProof(f,UpdateTicket.Read(receiptFile,16384),input,out _) : null;
                            if(review!=null)ValidateReview(f,input,review,receipt, signed??throw Bad("SignedInstalledProofRequired"),CaptureSnapshot(f,receiptFile,plugin));
                            o.Plugin=plugin?.Hash()??"-";
                            if(plugin!=null && o.Plugin!=receipt.Files[UpdateTrust.PluginPath] && review==null)throw Bad("OwnedFileChanged");
                            foreach(var name in InstallReceipt.Owned)if(name!=UpdateTrust.PluginPath)using(var owned=Maybe(f,name))
                                if(owned!=null && owned.Hash()!=receipt.Files[name] && review==null)throw Bad("OwnedFileChanged");
                            var current=plugin!=null && review==null?InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes)):null;
                            var currentVersion=current?.Version??UpdateVersion.Parse(signed!.Version).Tag;
                            var protocol=current?.UpdateProtocol??signed!.Protocol;var schema=current?.SettingsSchema??signed!.SettingsSchema;
                            var cmp=UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse(currentVersion));
                            if(cmp<0)throw new UpdateValidationException(UpdateFailure.NotNewer);
                            if(cmp==0)foreach(var entry in input.Update.Files)if(receipt.Files[entry.Path]!=entry.Sha256)throw new UpdateValidationException(UpdateFailure.NotNewer);
                            ValidateWholeChannel(receipt.Channels,o.Root,currentVersion,protocol,schema,input.Update,high);
                        }
                    } else {
                        foreach(var name in InstallReceipt.Owned)using(var file=Maybe(f,name))if(file!=null)throw Bad("InstallReceiptMissing");
                        if(high!=null)throw Bad("RemovalProofRequired");
                    }
                }
                if(pending.Count!=0) {
                    if(pending.Count!=1)throw Bad();var p=pending[0];CheckPlan(f,input,p);Target(f,input,p.Receipt);
                    o.Mode="Recovery";o.Image=p.OldImage;o.HelperReceipt=p.OldReceipt;return o.Text();
                }
                using(var image=Maybe(f,HelperDescriptor.Image))o.Image=Slot(image);
                using(var receipt=Maybe(f,HelperDelivery.Receipt))o.HelperReceipt=Slot(receipt);
                if(o.Image=="-\t-" && o.HelperReceipt=="-\t-"){if(o.Receipt!="-")throw Bad("HelperEvidenceMissing");o.Mode="Fresh";}
                else {
                    var actual=HelperDelivery.Fingerprint(f,UpdateTrust.Production());if(actual==null || o.Receipt=="-")throw Bad();
                    using(var image=f.OpenLaunchImage(HelperDescriptor.Image)) {
                        var bytes=UpdateTicket.Read(image,UpdateTrust.MaxFileBytes);var matched=false;
                        foreach(var dir in new List<string>(UpdateWorkPath.Directories(f, "helper")).ToArray()) {
                            var work=dir;
                            using(var plan=f.OpenFile(work+"/plan")) {
                                var lines=Encoding.ASCII.GetString(UpdateTicket.Read(plan,2048)).Split('\n');
                                if(lines.Length!=8 || lines[5]!="image-id="+image.Identity.Text)continue;
                            }
                            using(var descriptor=f.OpenFile(work+"/descriptor"))using(var signature=f.OpenFile(work+"/signature")) {
                                var prior=HelperDescriptor.Verify(UpdateTrust.Production(),UpdateTicket.Read(descriptor,UpdateTrust.MaxDescriptorBytes),DetachedSignature.Parse(UpdateTicket.Read(signature,1024)),bytes);
                                if(UpdateVersion.Parse(prior.Version).CompareTo(UpdateVersion.Parse(version))>0)throw new UpdateValidationException(UpdateFailure.NotNewer);
                                matched=true;break;
                            }
                        }
                        if(!matched)throw Bad();
                    }
                    o.Mode=actual==input.Helper.Sha256 && HelperDelivery.Fingerprint(f,UpdateTrust.Production(),3)==actual ? "Current":"HelperUpgrade";
                }
                return o.Text();
            }
        }
        private sealed class PreparedObservation {internal string Text="",Receipt="",Snapshot="";internal string? Review;}
        private static readonly Dictionary<string,PreparedObservation> ApprovedObservations=new Dictionary<string,PreparedObservation>(StringComparer.Ordinal);
        internal static string Validate(string root,string package,string version) {
            var o=Observation.Parse(Observe(root,package,version));o.Nonce=Guid.NewGuid().ToString("N");
            lock(ApprovedObservations) {if(ApprovedObservations.Count>=32)throw Bad();ApprovedObservations.Add(o.Nonce,new PreparedObservation {Text=o.Text()});}
            return o.Text();
        }
        internal static string ValidateReviewed(string root,string package,string version,string review) {
            var o=Observation.Parse(Observe(root,package,version,review));o.Nonce=Guid.NewGuid().ToString("N");
            using(var f=new WindowsFileFence(root)) {
                var snapshot=CaptureSnapshot(f);
                lock(ApprovedObservations) {
                    if(ApprovedObservations.Count>=32)throw Bad();
                    ApprovedObservations.Add(o.Nonce,new PreparedObservation{Text=o.Text(),Review=review,Snapshot=snapshot});
                }
            }
            return o.Text();
        }
        internal static string VerifiedInstalledState(string root,string package,string version) {
            using(var input=new Inputs(root,package,version))using(var f=new WindowsFileFence(root))using(var receipt=f.OpenFile(InstallReceipt.Path)) {
                UpdateLaunchGate.Check(f);UpdateTransaction.ReadHighWater(f);
                var proof=SignedInstalledProof(f,UpdateTicket.Read(receipt,16384),input,out _);
                return "BetterAstralParty.VerifiedInstalledState/v1\nroot="+f.RootIdentity.Text+"\nversion="+UpdateVersion.Parse(proof.Version).Tag+
                    "\nupdate-protocol="+proof.Protocol+"\nsettings-schema="+proof.SettingsSchema+"\n";
            }
        }
        private static string[] SnapshotPaths() {
            var names=new List<string>(InstallReceipt.Owned);names.Add(InstallReceipt.Path);names.Add(UpdateTransaction.HighWaterPath);names.Sort(StringComparer.Ordinal);return names.ToArray();
        }
        private static string CaptureSnapshot(WindowsFileFence f,WindowsFileFence.FileLease? receipt=null,WindowsFileFence.FileLease? plugin=null) {
            var text="BetterAstralParty.OwnedSnapshot/v1\nroot="+f.RootIdentity.Text+"\n";
            foreach(var name in SnapshotPaths()) {
                // Reuse exclusive leases already held by observation instead of reopening them.
                var held=name==InstallReceipt.Path?receipt:name==UpdateTrust.PluginPath?plugin:null;
                if(held!=null)text+="file="+name+"\t"+Slot(held)+"\n";
                else using(var file=Maybe(f,name))text+="file="+name+"\t"+Slot(file)+"\n";
            }
            return text;
        }
        private static Dictionary<string,string> ParseSnapshot(string text,string root) {
            var names=SnapshotPaths();var l=Lines(Ascii(text),names.Length+3,"BetterAstralParty.OwnedSnapshot/v1");
            if(l[1]!="root="+root)throw Bad("ReviewSnapshotRootMismatch");
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            for(var i=0;i<names.Length;i++) {
                var prefix="file="+names[i]+"\t";
                if(!l[i+2].StartsWith(prefix,StringComparison.Ordinal))throw Bad("ReviewSnapshotPathInvalid");
                var slot=l[i+2].Substring(prefix.Length);if(!IsSlot(slot))throw Bad("ReviewSnapshotSlotInvalid");result.Add(names[i],slot);
            }
            return result;
        }
        private static void ValidateReview(WindowsFileFence f,Inputs input,string review,InstallReceipt prior,VerifiedUpdateDescriptor signed,string snapshot) {
            if(review.Length<2 || Encoding.UTF8.GetByteCount(review)>16384)throw Bad("ReviewPlanLimit");
            var plan=new InstallReceipt.Json(Encoding.UTF8.GetBytes(review),16384,32,8192).Parse() as Dictionary<string,object?>;
            if(plan==null || plan.Count!=9 || !plan.TryGetValue("schema",out var schema) || !Equals(schema,1))throw Bad("ReviewPlanShape");
            string Value(string name) {
                if(!plan.TryGetValue(name,out var v) || !(v is string s))throw Bad("ReviewPlanShape");return s;
            }
            if(Value("purpose")!="manual-recovery" || Value("packageManifestSha256")!=input.ManifestHash || Value("gameRoot")!=f.Root ||
                Value("gameRootIdentity")!=f.RootIdentity.Text || Value("toVersion")!=input.Update.Version ||
                Value("fromVersion")!=UpdateVersion.Parse(signed.Version).Tag || !MatchesInstalledProof(f,prior,signed))throw Bad("ReviewPlanBindingMismatch");
            var slots=ParseSnapshot(snapshot,f.RootIdentity.Text);
            var receiptSlot=slots[InstallReceipt.Path].Split('\t');
            if(receiptSlot.Length!=2 || receiptSlot[1]!=Value("receiptSha256"))throw Bad("ReviewReceiptMismatch");
            if(!plan.TryGetValue("observedFiles",out var observedValue) || !(observedValue is Dictionary<string,object?> observed) || observed.Count!=slots.Count)throw Bad("ReviewObservedSetInvalid");
            foreach(var pair in slots) {
                var parts=pair.Value.Split('\t');var expected=parts[1]=="-"?null:parts[1];
                if(!observed.TryGetValue(pair.Key,out var hash) || !Equals(hash,expected))throw Bad("ReviewObservedHashMismatch");
            }
            var high=UpdateTransaction.ReadHighWater(f);var next=UpdateVersion.Parse(input.Update.Version);
            if(next.CompareTo(UpdateVersion.Parse(signed.Version))<0 || high!=null && next.CompareTo(UpdateVersion.Parse(high))<0)throw new UpdateValidationException(UpdateFailure.NotNewer);
            if(next.CompareTo(UpdateVersion.Parse(signed.Version))==0 && input.Update.DescriptorSha256!=signed.DescriptorSha256)throw new UpdateValidationException(UpdateFailure.NotNewer);
        }
        private static void PriorProofBytes(WindowsFileFence f,Inputs input,byte[] receipt,out byte[] descriptor,out byte[] signature) {
            var verified=SignedInstalledProof(f,receipt,input,out var pins);
            if(pins.Count==0){descriptor=input.PriorDescriptor??input.UpdateDescriptorBytes;signature=input.PriorSignature??input.UpdateSignatureBytes;return;}
            string? d=null,s=null,k=null;
            foreach(var path in pins.Keys) {
                if(path.EndsWith("/descriptor.bin",StringComparison.Ordinal) || path.EndsWith("/signed-update.descriptor",StringComparison.Ordinal))d=path;
                else if(path.EndsWith("/signature.bin",StringComparison.Ordinal) || path.EndsWith("/signed-update.signature",StringComparison.Ordinal))s=path;
                else if(path.EndsWith("/key-id.bin",StringComparison.Ordinal))k=path;
            }
            if(d==null || s==null)throw Bad("ReviewPriorProofMissing");
            byte[] ReadPin(string path,long limit) {
                using(var file=f.OpenFile(path)){if(file.Hash()!=pins[path])throw Bad("ReviewPriorProofChanged");return UpdateTicket.Read(file,limit);}
            }
            descriptor=ReadPin(d,UpdateTrust.MaxDescriptorBytes);
            signature=k==null?ReadPin(s,1024):new DetachedSignature(Encoding.ASCII.GetString(ReadPin(k,40)),ReadPin(s,UpdateTrust.MaxSignatureBytes)).Bytes();
            if(VerifyUpdateProof(descriptor,signature).DescriptorSha256!=verified.DescriptorSha256)throw Bad("ReviewPriorProofChanged");
        }
        private static PreparedObservation Approved(Observation o) {
            lock(ApprovedObservations) {PreparedObservation value;if(!ApprovedObservations.TryGetValue(o.Nonce,out value!) || value.Text!=o.Text())throw Bad();return value;}
        }
        private static string FindHash(VerifiedUpdateDescriptor d,string name){foreach(var file in d.Files)if(file.Path==name)return file.Sha256;throw Bad();}
        internal static string PrepareReceipt(string root,string package,string version,string observation,string loaderBefore) {
            var o=Observation.Parse(observation);
            var approved=Approved(o);var observed=Observation.Parse(Observe(root,package,version,approved.Review));observed.Nonce=o.Nonce;
            if(observed.Text()!=observation)throw Bad();
            using(var input=new Inputs(root,package,version))using(var f=new WindowsFileFence(root)) {
                MatchInputs(f,input,o);InstallReceipt old;
                using(var file=Maybe(f,InstallReceipt.Path)) {
                    if(file!=null) {
                        old=InstallReceipt.Parse(UpdateTicket.Read(file,16384));
                        using(var plugin=Maybe(f,UpdateTrust.PluginPath)) {
                            var current=plugin!=null && approved.Review==null?InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes)):null;
                            var signed=current==null?SignedInstalledProof(f,UpdateTicket.Read(file,16384),input,out _):null;
                            ValidateWholeChannel(old.Channels,o.Root,current?.Version??UpdateVersion.Parse(signed!.Version).Tag,current?.UpdateProtocol??signed!.Protocol,current?.SettingsSchema??signed!.SettingsSchema,input.Update,UpdateTransaction.ReadHighWater(f));
                        }
                    }
                    else {
                        if(loaderBefore!="true" && loaderBefore!="false" && loaderBefore!="null")throw Bad();
                        old=InstallReceipt.Parse(InstallReceipt.Create(o.Root,input.Update,loaderBefore=="null"?(bool?)null:loaderBefore=="true"));
                    }
                }
                var target=InstallReceipt.Parse(old.Updated(input.Update));
                var result=target.InitializeChannel(o.Root,input.Update);
                lock(ApprovedObservations)approved.Receipt=UpdateTrust.Hash(result);return Encoding.ASCII.GetString(result);
            }
        }
        internal sealed class MigrationPlan {
            internal string Root="",Work="",WorkId="",HelperWork="",HelperWorkId="",Update="",Helper="",Receipt="",Water="",OldImage="",OldReceipt="",NewImage="",NewReceipt="";
            internal string Text() {return "BetterAstralParty.WholeHelperMigration/v1\nroot="+Root+"\nwork="+Work+"\nwork-id="+WorkId+"\nhelper-work="+HelperWork+"\nhelper-work-id="+HelperWorkId+"\nupdate="+Update+"\nhelper="+Helper+"\nreceipt="+Receipt+"\nwater="+Water+"\nold-image="+OldImage+"\nold-receipt="+OldReceipt+"\nnew-image="+NewImage+"\nnew-receipt="+NewReceipt+"\n";}
            internal static MigrationPlan Parse(byte[] bytes) {
                var l=Lines(bytes,15,"BetterAstralParty.WholeHelperMigration/v1");var keys=new[]{"root=","work=","work-id=","helper-work=","helper-work-id=","update=","helper=","receipt=","water=","old-image=","old-receipt=","new-image=","new-receipt="};var v=new string[13];
                for(var i=0;i<keys.Length;i++){if(!l[i+1].StartsWith(keys[i],StringComparison.Ordinal))throw Bad();v[i]=l[i+1].Substring(keys[i].Length);}
                if(!Regex.IsMatch(v[0],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1],@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helperbridge-[0-9a-f]{32}\z") || !Regex.IsMatch(v[3],@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helper-[0-9a-f]{32}\z") || !Regex.IsMatch(v[2],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[4],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Digest(v[5]) || !Digest(v[6]) || !Digest(v[7]) || !IsSlot(v[8]) || !IsSlot(v[9]) || !IsSlot(v[10]) || !IsSlot(v[11]) || v[11]=="-\t-" || !IsSlot(v[12]) || v[12]=="-\t-")throw Bad();
                var p=new MigrationPlan {Root=v[0],Work=v[1],WorkId=v[2],HelperWork=v[3],HelperWorkId=v[4],Update=v[5],Helper=v[6],Receipt=v[7],Water=v[8],OldImage=v[9],OldReceipt=v[10],NewImage=v[11],NewReceipt=v[12]};if(UpdateTrust.Hash(Ascii(p.Text()))!=UpdateTrust.Hash(bytes))throw Bad();return p;
            }
        }
        private static List<MigrationPlan> Scan(WindowsFileFence f) {
            var result=new List<MigrationPlan>();var dirs=UpdateWorkPath.Directories(f, "helperbridge");
            foreach(var d in dirs) {
                var work=d;if(!Regex.IsMatch(work,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helperbridge-[0-9a-f]{32}\z"))throw Bad();f.AssertPrivateDirectory(work);
                using(var file=Maybe(f,work+"/plan")) {
                    if(file==null){UpdateWorkPath.RequireContained(work,"helperbridge");throw Bad();}var bytes=UpdateTicket.Read(file,4096);var p=MigrationPlan.Parse(bytes);if(p.Root!=f.RootIdentity.Text || p.Work!=work || p.WorkId!=f.DirectoryIdentity(work).Text)throw Bad();
                    using(var marker=Maybe(f,work+"/committed")) {if(marker==null){UpdateWorkPath.RequireContained(work,"helperbridge");if(result.Count>=64)throw Bad();result.Add(p);}else if(Encoding.ASCII.GetString(UpdateTicket.Read(marker,128))!=UpdateTrust.Hash(bytes)+"\n")throw Bad();}
                }
            }return result;
        }
        internal static bool Pending(WindowsFileFence f) {return Scan(f).Count!=0 || ScanIntents(f).Count!=0;}
        private static void CheckPlan(WindowsFileFence f,Inputs input,MigrationPlan p) {
            if(p.Root!=f.RootIdentity.Text || p.WorkId!=f.DirectoryIdentity(p.Work).Text || p.HelperWorkId!=f.DirectoryIdentity(p.HelperWork).Text || p.Update!=input.Update.DescriptorSha256 || p.Helper!=input.Helper.DescriptorHash)throw Bad();
            f.AssertPrivateDirectory(p.Work);f.AssertPrivateDirectory(p.HelperWork);
            using(var water=Maybe(f,UpdateTransaction.HighWaterPath))if(Slot(water)!=p.Water)throw Bad();
            using(var desc=f.OpenFile(p.HelperWork+"/descriptor"))using(var sig=f.OpenFile(p.HelperWork+"/signature"))if(desc.Hash()!=input.Helper.DescriptorHash || sig.Hash()!=UpdateTrust.Hash(input.HelperSignature))throw Bad();
            using(var plan=f.OpenFile(p.HelperWork+"/plan")) {
                var l=Lines(UpdateTicket.Read(plan,2048),8,"BetterAstralParty.HelperInstall/v1");
                if(l[1]!="root="+p.Root || l[2]!="work="+p.HelperWork || l[3]!="work-id="+p.HelperWorkId || l[4]!="descriptor="+p.Helper || l[5]!="image-id="+p.NewImage.Split('\t')[0] || l[6]!="receipt-id="+p.NewReceipt.Split('\t')[0] || p.NewImage.Split('\t')[1]!=input.Helper.Sha256 || p.NewReceipt.Split('\t')[1]!=UpdateTrust.Hash(HelperDelivery.WholeReceiptBytes(f,input.Helper,p.NewImage.Split('\t')[0])))throw Bad();
            }
        }
        // Validate all original and new slots before any rename; targets may be old or exact new IDs.
        private static void CheckItem(WindowsFileFence f,MigrationPlan p,string target,string old,string next,string staged,string archive) {
            using(var a=Maybe(f,target))using(var b=Maybe(f,p.Work+"/"+archive))using(var n=Maybe(f,p.HelperWork+"/"+staged)) {
                var t=Slot(a);var saved=Slot(b);var incoming=Slot(n);
                if(old=="-\t-") {if(b!=null || a!=null && t!=next)throw Bad();}
                else {
                    if(saved!="-\t-" && saved!=old || t!=old && saved!=old || t==old && b!=null || t!=old && t!=next && a!=null)throw Bad();
                }
                if(t==next ? n!=null : incoming!=next)throw Bad();
            }
        }
        private static void Archive(WindowsFileFence f,MigrationPlan p,string target,string old,string next,string archive,Action<string>? boundary) {
            using(var existing=Maybe(f,target,true)) {
                if(existing==null || Slot(existing)==next)return;
                if(old=="-\t-" || Slot(existing)!=old)throw Bad();
                boundary?.Invoke("before:"+archive);existing.RenameTo(f,p.Work+"/"+archive);existing.Flush();boundary?.Invoke("after:"+archive);
            }
        }
        private static void Finish(WindowsFileFence f,Inputs input,MigrationPlan p,Action<string>? boundary,WindowsProcessGuard.LaunchLease processes) {
            CheckPlan(f,input,p);Target(f,input,p.Receipt,processes);
            CheckItem(f,p,HelperDescriptor.Image,p.OldImage,p.NewImage,"image","old-image");
            CheckItem(f,p,HelperDelivery.Receipt,p.OldReceipt,p.NewReceipt,"receipt","old-receipt");
            if(p.OldImage!="-\t-") {
                string imagePath,receiptPath;
                using(var old=Maybe(f,HelperDescriptor.Image))imagePath=Slot(old)==p.OldImage?HelperDescriptor.Image:p.Work+"/old-image";
                using(var old=Maybe(f,HelperDelivery.Receipt))receiptPath=Slot(old)==p.OldReceipt?HelperDelivery.Receipt:p.Work+"/old-receipt";
                if(p.OldReceipt=="-\t-" || HelperDelivery.ArchivedFingerprint(f,UpdateTrust.Production(),imagePath,receiptPath)!=p.OldImage.Split('\t')[1])throw Bad();
            }
            Archive(f,p,HelperDescriptor.Image,p.OldImage,p.NewImage,"old-image",boundary);
            Archive(f,p,HelperDelivery.Receipt,p.OldReceipt,p.NewReceipt,"old-receipt",boundary);
            HelperDelivery.FinishWholeMigration(f,p.HelperWork,UpdateTrust.Production(),boundary);
            CheckItem(f,p,HelperDescriptor.Image,p.OldImage,p.NewImage,"image","old-image");
            CheckItem(f,p,HelperDelivery.Receipt,p.OldReceipt,p.NewReceipt,"receipt","old-receipt");
            if(HelperDelivery.Fingerprint(f,UpdateTrust.Production(),3)!=input.Helper.Sha256)throw Bad();
            Target(f,input,p.Receipt,processes);CheckPlan(f,input,p);
            boundary?.Invoke("before:bridge-committed");Write(f,p.Work+"/committed",Ascii(UpdateTrust.Hash(Ascii(p.Text()))+"\n"));boundary?.Invoke("after:bridge-committed");
        }
        // Intent files belong to a native private directory and bind exact root/object identities.
        // They grant no permission to adopt an unrelated helper or repair partial owned files.
        private sealed class WholeIntent {
            internal string Root="",Work="",WorkId="",Observation="",Receipt="",Plugin="-\t-",OldReceipt="-\t-",Snapshot="",Review="",PriorDescriptor="",PriorSignature="";
            internal string Text(){
                var text=(Snapshot==""?"BetterAstralParty.WholeInstallIntent/v1":"BetterAstralParty.WholeInstallIntent/v2")+"\nroot="+Root+"\nwork="+Work+"\nwork-id="+WorkId+"\nobservation="+Convert.ToBase64String(Ascii(Observation))+"\ntarget-receipt="+Receipt+"\nold-plugin="+Plugin+"\nold-receipt="+OldReceipt+"\n";
                return Snapshot==""?text:text+"snapshot="+Convert.ToBase64String(Ascii(Snapshot))+"\nreview="+Convert.ToBase64String(Encoding.UTF8.GetBytes(Review))+"\nprior-descriptor="+PriorDescriptor+"\nprior-signature="+PriorSignature+"\n";
            }
            internal static WholeIntent Parse(byte[] bytes){
                var v2=Encoding.ASCII.GetString(bytes).StartsWith("BetterAstralParty.WholeInstallIntent/v2\n",StringComparison.Ordinal);
                var l=Lines(bytes,v2?13:9,v2?"BetterAstralParty.WholeInstallIntent/v2":"BetterAstralParty.WholeInstallIntent/v1");var keys=new[]{"root=","work=","work-id=","observation=","target-receipt=","old-plugin=","old-receipt="};var v=new string[7];
                for(var i=0;i<keys.Length;i++){if(!l[i+1].StartsWith(keys[i],StringComparison.Ordinal))throw Bad();v[i]=l[i+1].Substring(keys[i].Length);}
                if(!Regex.IsMatch(v[0],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1],@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-wholeintent-[0-9a-f]{32}\z") || !Regex.IsMatch(v[2],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Digest(v[4]) || !IsSlot(v[5]) || !IsSlot(v[6]))throw Bad();
                string observed;try{observed=Encoding.ASCII.GetString(Convert.FromBase64String(v[3]));}catch(FormatException){throw Bad();}
                var o=WholeInstallBridge.Observation.Parse(observed);
                if(o.Root!=v[0] || o.Nonce!="00000000000000000000000000000000" || o.Mode!="Fresh" && o.Mode!="HelperUpgrade" && !(v2 && o.Mode=="Current"))throw Bad();
                var intent=new WholeIntent {Root=v[0],Work=v[1],WorkId=v[2],Observation=observed,Receipt=v[4],Plugin=v[5],OldReceipt=v[6]};
                if(v2) {
                    var extra=new[]{"snapshot=","review=","prior-descriptor=","prior-signature="};
                    for(var i=0;i<extra.Length;i++)if(!l[i+8].StartsWith(extra[i],StringComparison.Ordinal))throw Bad("ReviewIntentShape");
                    try {
                        intent.Snapshot=Encoding.ASCII.GetString(Convert.FromBase64String(l[8].Substring(extra[0].Length)));
                        intent.Review=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(l[9].Substring(extra[1].Length)));
                        intent.PriorDescriptor=l[10].Substring(extra[2].Length);intent.PriorSignature=l[11].Substring(extra[3].Length);
                        if(Convert.FromBase64String(intent.PriorDescriptor).Length>UpdateTrust.MaxDescriptorBytes || Convert.FromBase64String(intent.PriorSignature).Length>1024 || intent.Review.Length>16384)throw Bad("ReviewIntentLimit");
                    }catch(FormatException){throw Bad("ReviewIntentShape");}
                    ParseSnapshot(intent.Snapshot,intent.Root);
                    if(intent.OldReceipt=="-\t-")throw Bad("ReviewOriginalReceiptRequired");
                }
                if(UpdateTrust.Hash(Ascii(intent.Text()))!=UpdateTrust.Hash(bytes))throw Bad();return intent;
            }
        }
        private static IEnumerable<string> IntentDirectories(WindowsFileFence f) {
            foreach(var kind in new[]{"wholeintent","wholehist"})
                foreach(var directory in UpdateWorkPath.Directories(f, kind)) {
                    var work=directory;LogicalIntent(work);yield return work;
                }
        }
        private static string LogicalIntent(string work) {
            if(!Regex.IsMatch(work,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-(?:wholeintent|wholehist)-[0-9a-f]{32}\z"))throw Bad("WholeProofPathInvalid");
            return UpdateWorkPath.LogicalIntent(work);
        }
        private static List<WholeIntent> ScanIntents(WindowsFileFence f){
            var result=new List<WholeIntent>();
            foreach(var work in IntentDirectories(f)){
                f.AssertPrivateDirectory(work);
                using(var file=Maybe(f,work+"/plan")){
                    if(file==null){UpdateWorkPath.RequireContained(LogicalIntent(work),"wholeintent");throw Bad();}
                    var bytes=UpdateTicket.Read(file,262144);var intent=WholeIntent.Parse(bytes);
                    if(intent.Root!=f.RootIdentity.Text || intent.Work!=LogicalIntent(work) || intent.WorkId!=f.DirectoryIdentity(work).Text || work!=intent.Work && intent.Snapshot=="")throw Bad();
                    using(var committed=Maybe(f,work+"/committed"))using(var cancelled=Maybe(f,work+"/cancelled")){
                        if(committed!=null && cancelled!=null)throw Bad();var marker=committed??cancelled;
                        if(marker==null){UpdateWorkPath.RequireContained(work,"wholeintent");if(work!=intent.Work || result.Count>=64)throw Bad();result.Add(intent);}else if(Encoding.ASCII.GetString(UpdateTicket.Read(marker,128))!=UpdateTrust.Hash(bytes)+"\n")throw Bad();
                    }
                }
            }return result;
        }
        private static void CheckIntent(WindowsFileFence f,Inputs input,WholeIntent intent){
            var o=Observation.Parse(intent.Observation);MatchInputs(f,input,o);f.AssertPrivateDirectory(intent.Work);
            if(intent.WorkId!=f.DirectoryIdentity(intent.Work).Text)throw Bad();
            using(var target=f.OpenFile(intent.Work+"/target-receipt")){if(target.Hash()!=intent.Receipt)throw Bad();TargetReceipt(f,input,UpdateTicket.Read(target,16384));}
            using(var plugin=Maybe(f,intent.Work+"/old-plugin"))using(var receipt=Maybe(f,intent.Work+"/old-receipt")){
                if(Slot(plugin)!=intent.Plugin || Slot(receipt)!=intent.OldReceipt || (plugin?.Hash()??"-")!=o.Plugin || (receipt?.Hash()??"-")!=o.Receipt)throw Bad();
                if(receipt==null){if(plugin!=null || o.Plugin!="-" || o.Mode!="Fresh")throw Bad();}
                else {
                    var priorBytes=UpdateTicket.Read(receipt,16384);var prior=InstallReceipt.Parse(priorBytes);
                    if(intent.Snapshot!="" && intent.PriorDescriptor!="") {
                        var signed=VerifyUpdateProof(Convert.FromBase64String(intent.PriorDescriptor),Convert.FromBase64String(intent.PriorSignature));
                        if(!MatchesInstalledProof(f,prior,signed))throw Bad("ReviewPriorProofMismatch");
                        if(UpdateVersion.Parse(signed.Version).CompareTo(UpdateVersion.Parse(input.Update.Version))>0)throw new UpdateValidationException(UpdateFailure.NotNewer);
                        var slots=ParseSnapshot(intent.Snapshot,f.RootIdentity.Text);
                        if((plugin?.Hash()??"-")!=slots[UpdateTrust.PluginPath].Split('\t')[1] || receipt.Hash()!=slots[InstallReceipt.Path].Split('\t')[1])throw Bad("ReviewOriginalSnapshotMismatch");
                        if(intent.Review!="")ValidateReview(f,input,intent.Review,prior,signed,intent.Snapshot);
                        else foreach(var pair in slots)if(pair.Key!=InstallReceipt.Path && pair.Key!=UpdateTransaction.HighWaterPath && pair.Value!="-\t-" && pair.Value.Split('\t')[1]!=prior.Files[pair.Key])throw Bad("UnreviewedChangedOwnedFile");
                        ValidateWholeChannel(prior.Channels,o.Root,UpdateVersion.Parse(signed.Version).Tag,signed.Protocol,signed.SettingsSchema,input.Update,UpdateTransaction.ReadHighWater(f));
                    } else {
                    if(plugin==null)throw Bad();
                    if(prior.Files[UpdateTrust.PluginPath]!=o.Plugin)throw Bad();
                    var metadata=InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes));
                    if(UpdateVersion.Parse(metadata.Version).CompareTo(UpdateVersion.Parse(input.Update.Version))>0)throw Bad();
                    ValidateWholeChannel(prior.Channels,o.Root,metadata.Version,metadata.UpdateProtocol,metadata.SettingsSchema,input.Update,UpdateTransaction.ReadHighWater(f));
                    if(intent.Snapshot!="") {
                        if(o.Mode!="Current" || intent.Review!="" || intent.PriorDescriptor!="" || intent.PriorSignature!="")throw Bad("UnverifiedReviewIntent");
                        var slots=ParseSnapshot(intent.Snapshot,f.RootIdentity.Text);
                        if(plugin.Hash()!=slots[UpdateTrust.PluginPath].Split('\t')[1] || receipt.Hash()!=slots[InstallReceipt.Path].Split('\t')[1])throw Bad("ReviewOriginalSnapshotMismatch");
                        foreach(var name in InstallReceipt.Owned)if(slots[name]!="-\t-" && slots[name].Split('\t')[1]!=prior.Files[name])throw Bad("UnreviewedChangedOwnedFile");
                    }
                    }
                }
            }
            if(o.Mode=="Fresh" && (o.Image!="-\t-" || o.HelperReceipt!="-\t-"))throw Bad();
            if(o.Mode=="HelperUpgrade" && (o.Image=="-\t-" || o.HelperReceipt=="-\t-"))throw Bad();
            using(var water=Maybe(f,UpdateTransaction.HighWaterPath))if(Slot(water)!=o.Water)throw Bad();
        }
        private static void Original(WindowsFileFence f,WholeIntent intent,bool restored=false){
            var o=Observation.Parse(intent.Observation);
            using(var image=Maybe(f,HelperDescriptor.Image))using(var helper=Maybe(f,HelperDelivery.Receipt))using(var receipt=Maybe(f,InstallReceipt.Path)){
                if(Slot(image)!=o.Image || Slot(helper)!=o.HelperReceipt || (receipt?.Hash()??"-")!=o.Receipt)throw Bad();
                if(receipt==null){foreach(var name in InstallReceipt.Owned)using(var file=Maybe(f,name))if(file!=null)throw Bad();}
                else if(intent.Snapshot!="") {
                    var slots=ParseSnapshot(intent.Snapshot,f.RootIdentity.Text);
                    foreach(var pair in slots) {
                        if(pair.Key==InstallReceipt.Path) {
                            if(restored ? receipt.Hash()!=pair.Value.Split('\t')[1] : Slot(receipt)!=pair.Value)throw Bad("ReviewOriginObjectChanged");
                            continue;
                        }
                        using(var file=Maybe(f,pair.Key)) {
                            if(restored){if((file?.Hash()??"-")!=pair.Value.Split('\t')[1])throw Bad("ReviewOriginChanged");}
                            else if(Slot(file)!=pair.Value)throw Bad("ReviewOriginObjectChanged");
                        }
                    }
                } else {var prior=InstallReceipt.Parse(UpdateTicket.Read(receipt,16384));foreach(var name in InstallReceipt.Owned)using(var file=Maybe(f,name))if(file==null ? name==UpdateTrust.PluginPath : file.Hash()!=prior.Files[name])throw Bad();}
            }
        }
        private static void ArchiveIntent(WindowsFileFence f,WholeIntent intent,Action<string>? boundary=null) {
            if(intent.Snapshot=="" || !UpdateWorkPath.IsContained(intent.Work,"wholeintent"))return;
            using(var target=f.OpenFile(intent.Work+"/target-receipt")) {
                if(target.Hash()!=intent.Receipt)throw Bad("WholeProofReceiptMismatch");
                using(var d=f.OpenFile(intent.Work+"/signed-update.descriptor"))using(var s=f.OpenFile(intent.Work+"/signed-update.signature")) {
                    var proof=VerifyUpdateProof(UpdateTicket.Read(d,UpdateTrust.MaxDescriptorBytes),UpdateTicket.Read(s,1024));
                    if(!MatchesInstalledProof(f,InstallReceipt.Parse(UpdateTicket.Read(target,16384)),proof))throw Bad("HistoricalOwnedProofMismatch");
                }
            }
            boundary?.Invoke("before:whole-history-archive");
            f.ArchiveDirectory(intent.Work,UpdateWorkPath.WholeHistory(intent.Work),intent.WorkId);
            boundary?.Invoke("after:whole-history-archive");
        }
        private static void ArchiveCompletedIntents(WindowsFileFence f,Action<string>? boundary=null) {
            ScanIntents(f);
            foreach(var directory in new List<string>(UpdateWorkPath.Directories(f, "wholeintent")).ToArray()) {
                var work=directory;WholeIntent intent;bool terminal;
                using(var plan=f.OpenFile(work+"/plan"))intent=WholeIntent.Parse(UpdateTicket.Read(plan,262144));
                using(var committed=Maybe(f,work+"/committed"))using(var cancelled=Maybe(f,work+"/cancelled"))terminal=committed!=null || cancelled!=null;
                if(terminal)ArchiveIntent(f,intent,boundary);
            }
        }
        private static void RetireIntent(WindowsFileFence f,WholeIntent intent,bool committed,Action<string>? boundary=null){
            Write(f,intent.Work+(committed?"/committed":"/cancelled"),Ascii(UpdateTrust.Hash(Ascii(intent.Text()))+"\n"));
            ArchiveIntent(f,intent,boundary);
        }
        private sealed class Maintenance : IDisposable {
            internal readonly WindowsFileFence Fence;private WindowsFileFence.FileLease? _lock;
            internal Maintenance(string root){
                Fence=new WindowsFileFence(root);try{_lock=Maybe(Fence,"BetterAstralParty.update.lock",true)??Fence.OpenFile("BetterAstralParty.update.lock",true,true);}catch{Dispose();throw;}
            }
            public void Dispose(){_lock?.Dispose();_lock=null;Fence.Dispose();}
        }
        internal static IDisposable GuardRecovery(string root,string package,string version,string? ordinaryJournalHash=null){
            var lease=new Maintenance(root);try{using(var input=new Inputs(root,package,version)){
                var pending=Scan(lease.Fence);OtherState(lease.Fence,pending,ordinaryJournalHash);
                // Ordinary journals finish before helper migration begins. A bridge plan here
                // is conflicting recovery evidence and must not be rolled back by this API.
                if(pending.Count!=0)throw Bad();
                var intents=ScanIntents(lease.Fence);if(intents.Count>1)throw Bad();foreach(var intent in intents)CheckIntent(lease.Fence,input,intent);
                // Does not require partial owned8 to match a receipt. Only the authenticated
                // ordinary journal restore may repair it, after explicit confirmation.
                WindowsProcessGuard.AssertNoRunning(new[]{lease.Fence.Full("AstralParty_INT.exe"),lease.Fence.Full("BetterAstralParty-Launcher.exe"),lease.Fence.Full(HelperDescriptor.Image)});
            }return lease;}catch{lease.Dispose();throw;}
        }
        internal static IDisposable BeginInstall(string root,string package,string version,string observation,string receiptText,Action<string>? boundary=null){
            var o=Observation.Parse(observation);var approved=Approved(o);var receiptBytes=Ascii(receiptText);
            if(approved.Receipt!=UpdateTrust.Hash(receiptBytes))throw Bad();
            var lease=new Maintenance(root);try{using(var input=new Inputs(root,package,version)){
                var f=lease.Fence;MatchInputs(f,input,o);TargetReceipt(f,input,receiptBytes);
                var actual=Observation.Parse(Observe(root,package,version,approved.Review));actual.Nonce=o.Nonce;if(actual.Text()!=o.Text())throw Bad();
                ArchiveCompletedIntents(f,boundary);
                if(approved.Review!=null && CaptureSnapshot(f)!=approved.Snapshot)throw Bad("ReviewOriginObjectChanged");
                WindowsProcessGuard.AssertNoRunning(new[]{f.Full("AstralParty_INT.exe"),f.Full("BetterAstralParty-Launcher.exe"),f.Full(HelperDescriptor.Image)});
                if(o.Mode=="Recovery")return lease;
                var durable=Observation.Parse(o.Text());durable.Nonce="00000000000000000000000000000000";
                var intent=new WholeIntent {Root=o.Root,Work=UpdateWorkPath.New("wholeintent"),Observation=durable.Text(),Receipt=approved.Receipt};
                intent.WorkId=UpdateWorkPath.Create(f, intent.Work).Text;
                using(var oldPlugin=Maybe(f,UpdateTrust.PluginPath))if(oldPlugin!=null)Write(f,intent.Work+"/old-plugin",UpdateTicket.Read(oldPlugin,UpdateTrust.MaxFileBytes));
                using(var oldReceipt=Maybe(f,InstallReceipt.Path))if(oldReceipt!=null)Write(f,intent.Work+"/old-receipt",UpdateTicket.Read(oldReceipt,16384));
                using(var oldPlugin=Maybe(f,intent.Work+"/old-plugin"))intent.Plugin=Slot(oldPlugin);
                using(var oldReceipt=Maybe(f,intent.Work+"/old-receipt"))intent.OldReceipt=Slot(oldReceipt);
                if(intent.OldReceipt!="-\t-" && (o.Plugin=="-" || approved.Review!=null || o.Mode=="Current")) {
                    intent.Snapshot=CaptureSnapshot(f);intent.Review=approved.Review??"";
                    if(o.Plugin=="-" || approved.Review!=null)using(var receipt=f.OpenFile(intent.Work+"/old-receipt")) {
                        PriorProofBytes(f,input,UpdateTicket.Read(receipt,16384),out var descriptor,out var signature);
                        intent.PriorDescriptor=Convert.ToBase64String(descriptor);intent.PriorSignature=Convert.ToBase64String(signature);
                    }
                }
                Write(f,intent.Work+"/target-receipt",receiptBytes);
                Write(f,intent.Work+"/signed-update.descriptor",input.UpdateDescriptorBytes);
                Write(f,intent.Work+"/signed-update.signature",input.UpdateSignatureBytes);
                Write(f,intent.Work+"/plan",Ascii(intent.Text()));
                CheckIntent(f,input,intent);Original(f,intent);boundary?.Invoke("whole-intent-durable");
            }return lease;}catch{lease.Dispose();throw;}
        }
        private static MigrationPlan CreateMigration(WindowsFileFence f,Inputs input,Observation o,string receiptHash,Action<string>? boundary){
            using(var oldImage=Maybe(f,HelperDescriptor.Image))using(var oldReceipt=Maybe(f,HelperDelivery.Receipt))if(Slot(oldImage)!=o.Image || Slot(oldReceipt)!=o.HelperReceipt)throw Bad();
            var p=new MigrationPlan {Root=o.Root,Work=UpdateWorkPath.New("helperbridge"),HelperWork=UpdateWorkPath.New("helper"),Update=o.Update,Helper=o.Helper,Receipt=receiptHash,Water=o.Water,OldImage=o.Image,OldReceipt=o.HelperReceipt};
            p.WorkId=UpdateWorkPath.Create(f, p.Work).Text;p.HelperWorkId=UpdateWorkPath.Create(f, p.HelperWork).Text;
            Write(f,p.HelperWork+"/descriptor",input.HelperDescriptorBytes);Write(f,p.HelperWork+"/signature",input.HelperSignature);Write(f,p.HelperWork+"/image",input.HelperBytes);
            using(var image=f.OpenFile(p.HelperWork+"/image"))p.NewImage=Slot(image);
            Write(f,p.HelperWork+"/receipt",HelperDelivery.WholeReceiptBytes(f,input.Helper,p.NewImage.Split('\t')[0]));
            using(var receipt=f.OpenFile(p.HelperWork+"/receipt"))p.NewReceipt=Slot(receipt);
            var plan=Ascii("BetterAstralParty.HelperInstall/v1\nroot="+p.Root+"\nwork="+p.HelperWork+"\nwork-id="+p.HelperWorkId+"\ndescriptor="+p.Helper+"\nimage-id="+p.NewImage.Split('\t')[0]+"\nreceipt-id="+p.NewReceipt.Split('\t')[0]+"\n");
            Write(f,p.HelperWork+"/plan",plan);Write(f,p.Work+"/plan",Ascii(p.Text()));boundary?.Invoke("bridge-prepared");return p;
        }
        private static void MatchIntentPlan(WholeIntent intent,MigrationPlan p){
            var o=Observation.Parse(intent.Observation);if(p.Root!=o.Root || p.Update!=o.Update || p.Helper!=o.Helper || p.Receipt!=intent.Receipt || p.Water!=o.Water || p.OldImage!=o.Image || p.OldReceipt!=o.HelperReceipt)throw Bad();
        }
        private static bool CompletedIntent(WindowsFileFence f,Inputs input,WholeIntent intent,WindowsProcessGuard.LaunchLease processes){
            foreach(var d in new List<string>(UpdateWorkPath.Directories(f, "helperbridge")).ToArray()){
                var work=d;using(var marker=Maybe(f,work+"/committed"))if(marker!=null)using(var file=f.OpenFile(work+"/plan")){
                    var p=MigrationPlan.Parse(UpdateTicket.Read(file,4096));var o=Observation.Parse(intent.Observation);
                    if(p.Receipt!=intent.Receipt || p.Update!=o.Update || p.Helper!=o.Helper || p.OldImage!=o.Image || p.OldReceipt!=o.HelperReceipt)continue;
                    MatchIntentPlan(intent,p);CheckPlan(f,input,p);Target(f,input,p.Receipt,processes);
                    CheckItem(f,p,HelperDescriptor.Image,p.OldImage,p.NewImage,"image","old-image");CheckItem(f,p,HelperDelivery.Receipt,p.OldReceipt,p.NewReceipt,"receipt","old-receipt");
                    if(HelperDelivery.Fingerprint(f,UpdateTrust.Production(),3)!=input.Helper.Sha256)throw Bad();return true;
                }
            }return false;
        }
        internal static void Recover(string root,string package,string version,Action<string>? boundary=null) {
            using(var input=new Inputs(root,package,version))using(var lease=new Maintenance(root)) {
                var f=lease.Fence;ArchiveCompletedIntents(f,boundary);var pending=Scan(f);OtherState(f,pending);var intents=ScanIntents(f);if(pending.Count>1 || intents.Count>1)throw Bad();
                var intent=intents.Count==1?intents[0]:null;if(intent!=null)CheckIntent(f,input,intent);
                if(pending.Count==0 && intent==null)return;
                if(pending.Count==0 && intent!=null){
                    string receipt;using(var file=Maybe(f,InstallReceipt.Path))receipt=file?.Hash()??"-";
                    if(receipt!=intent.Receipt || !MatchesTarget(f,input,intent.Receipt)){
                        Original(f,intent,true);
                        WindowsProcessGuard.AssertNoRunning(new[]{f.Full("AstralParty_INT.exe"),f.Full("BetterAstralParty-Launcher.exe"),f.Full(HelperDescriptor.Image)});
                        RetireIntent(f,intent,false,boundary);boundary?.Invoke("whole-intent-restored");return;
                    }
                }
                using(var processes=WindowsProcessGuard.Acquire(f,new[]{"AstralParty_INT.exe","BetterAstralParty-Launcher.exe"})) {
                    WindowsProcessGuard.AssertNoRunning(new[]{f.Full(HelperDescriptor.Image)});
                    if(pending.Count==1){if(intent!=null)MatchIntentPlan(intent,pending[0]);Finish(f,input,pending[0],boundary,processes);}
                    else if(intent!=null && Observation.Parse(intent.Observation).Mode=="Current") {
                        Target(f,input,intent.Receipt,processes);
                        if(HelperDelivery.Fingerprint(f,UpdateTrust.Production(),3)!=input.Helper.Sha256)throw Bad("ReviewHelperChanged");
                    } else if(intent!=null && !CompletedIntent(f,input,intent,processes)){
                        Target(f,input,intent.Receipt,processes);var o=Observation.Parse(intent.Observation);
                        var plan=CreateMigration(f,input,o,intent.Receipt,boundary);Finish(f,input,plan,boundary,processes);
                    }
                    if(intent!=null)RetireIntent(f,intent,true,boundary);
                }
            }
        }
        internal static void Install(string root,string package,string version,string observation,string receiptHash,Action<string>? boundary=null) {
            var o=Observation.Parse(observation);var approved=Approved(o);if(approved.Receipt!=receiptHash)throw Bad();
            using(var input=new Inputs(root,package,version))using(var lease=new Maintenance(root)) {
                var f=lease.Fence;MatchInputs(f,input,o);var pending=Scan(f);OtherState(f,pending);Target(f,input,receiptHash);
                using(var processes=WindowsProcessGuard.Acquire(f,new[]{"AstralParty_INT.exe","BetterAstralParty-Launcher.exe"})) {
                    WindowsProcessGuard.AssertNoRunning(new[]{f.Full(HelperDescriptor.Image)});
                    var intents=ScanIntents(f);if(intents.Count>1 || pending.Count>1)throw Bad();
                    if(o.Mode=="Current" || o.Mode=="Recovery"){
                        if(pending.Count!=0 || intents.Count>1 || HelperDelivery.Fingerprint(f,UpdateTrust.Production(),3)!=input.Helper.Sha256)throw Bad();
                        if(intents.Count==1) {
                            var current=intents[0];CheckIntent(f,input,current);
                            if(current.Snapshot=="" || current.Receipt!=receiptHash || Observation.Parse(current.Observation).Mode!="Current")throw Bad();
                            RetireIntent(f,current,true,boundary);
                        }
                        return;
                    }
                    if(intents.Count!=1)throw Bad();var intent=intents[0];CheckIntent(f,input,intent);
                    var durable=Observation.Parse(o.Text());durable.Nonce="00000000000000000000000000000000";
                    if(intent.Observation!=durable.Text() || intent.Receipt!=receiptHash)throw Bad();
                    var plan=pending.Count==1?pending[0]:CreateMigration(f,input,o,receiptHash,boundary);
                    MatchIntentPlan(intent,plan);Finish(f,input,plan,boundary,processes);RetireIntent(f,intent,true,boundary);
                }
            }
        }
    }
}
