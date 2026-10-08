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
        private const string OriginalDevImage = "816BBDE3EE27DD306F7266D4D9CCCC0B143D9A7FA34B1FDF7B46C73E8651BE31";
        private const string OriginalDevPlugin = "283D554024FE664B8FB67BACD7777C3BDF5C0CB777332D731BCDD8BA98ABFDA1";
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
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
            if(l.Length<15 || !local && l[0]!="BetterAstralParty.UpdateDescriptor/v2" || !l[1].StartsWith("repository=",StringComparison.Ordinal) || !l[2].StartsWith("version=",StringComparison.Ordinal) || !l[3].StartsWith("channel=",StringComparison.Ordinal) || !l[5].StartsWith("release-id=",StringComparison.Ordinal) || !l[6].StartsWith("asset-id=",StringComparison.Ordinal) || !l[12].StartsWith("repository-id=",StringComparison.Ordinal)) throw Bad();
            long release,asset,id;
            if(!long.TryParse(l[5].Substring(11),NumberStyles.None,CultureInfo.InvariantCulture,out release) || !long.TryParse(l[6].Substring(9),NumberStyles.None,CultureInfo.InvariantCulture,out asset) || !long.TryParse(l[12].Substring(14),NumberStyles.None,CultureInfo.InvariantCulture,out id)) throw Bad();
            if(local && (release!=0 || asset!=0)) throw Bad();
            var context=local ? UpdateContext.LocalInstall(l[1].Substring(11),l[3].Substring(8),l[2].Substring(8),id)
                : new UpdateContext(l[1].Substring(11),l[3].Substring(8),"0.0.0",l[2].Substring(8),release,asset,UpdateTrust.PlatformId,repositoryId:id);
            var trust=UpdateTrust.Production();
            return local ? trust.VerifyLocalInstall(bytes,envelope.KeyId,envelope.Signature,context)
                : trust.VerifyProposal(bytes,envelope.KeyId,envelope.Signature,context);
        }
        private sealed class Inputs : IDisposable {
            internal readonly WindowsFileFence Source;
            internal readonly VerifiedUpdateDescriptor Update;
            internal readonly HelperDescriptor Helper;
            internal readonly byte[] HelperBytes, HelperDescriptorBytes, HelperSignature;
            private readonly List<IDisposable> _leases=new List<IDisposable>();
            private byte[] Read(string p, long limit) { var f=Source.OpenLaunchImage(p);_leases.Add(f);return UpdateTicket.Read(f,limit); }
            internal Inputs(string root,string package,string version) {
                Source=new WindowsFileFence(package);
                try {
                    var actual=Path.GetFullPath(root).TrimEnd('\\');
                    if(actual.Equals(Source.Root,StringComparison.OrdinalIgnoreCase) || actual.StartsWith(Source.Root+"\\",StringComparison.OrdinalIgnoreCase) || Source.Root.StartsWith(actual+"\\",StringComparison.OrdinalIgnoreCase)) throw Bad();
                    var bytes=Read("update-proof/update.descriptor",UpdateTrust.MaxDescriptorBytes);
                    Update=VerifyUpdateProof(bytes,Read("update-proof/update.signature",1024));
                    if(Update.Version!=version || Update.Protocol!=2 || Update.Files.Count!=8) throw Bad();
                    foreach(var file in Update.Files) {
                        var name=file.Path==UpdateTrust.PluginPath ? "artifacts/BetterAstralParty.dll" : file.Path=="BetterAstralParty-Launcher.exe" || file.Path=="BetterAstralParty.compatibility.json" ? "artifacts/"+file.Path : "tools/"+file.Path;
                        var payload=Read(name,UpdateTrust.MaxFileBytes);
                        if(payload.LongLength!=file.Bytes || UpdateTrust.Hash(payload)!=file.Sha256) throw Bad();
                        if(file.Path==UpdateTrust.PluginPath) {
                            var metadata=InstalledPluginMetadata.Read(payload);
                            if(metadata.Version!=version || metadata.UpdateProtocol!=2 || metadata.SettingsSchema!=1)throw Bad();
                        }
                    }
                    HelperDescriptorBytes=Read("helper-bootstrap/helper.descriptor",UpdateTrust.MaxDescriptorBytes);
                    HelperSignature=Read("helper-bootstrap/helper.signature",1024);
                    HelperBytes=Read("helper-bootstrap/"+HelperDescriptor.Image,UpdateTrust.MaxFileBytes);
                    Helper=HelperDescriptor.Verify(UpdateTrust.Production(),HelperDescriptorBytes,DetachedSignature.Parse(HelperSignature),HelperBytes,version);
                    if(Helper.Protocol<2) throw Bad();
                } catch {Dispose();throw;}
            }
            public void Dispose() {for(var i=_leases.Count-1;i>=0;i--)_leases[i].Dispose();_leases.Clear();Source.Dispose();}
        }
        internal static void ValidateInputs(string root,string package,string version) { using(var input=new Inputs(root,package,version)) { } }
        private sealed class Observation {
            internal string Nonce="00000000000000000000000000000000",Root="",Plugin="-",Receipt="-",Image="-\t-",HelperReceipt="-\t-",Water="-\t-",Update="",Helper="",Mode="";
            internal string Text() {return "BetterAstralParty.WholeObservation/v1\nroot="+Root+"\nplugin="+Plugin+"\nreceipt="+Receipt+"\nimage="+Image+"\nhelper-receipt="+HelperReceipt+"\nwater="+Water+"\nupdate="+Update+"\nhelper="+Helper+"\nmode="+Mode+"\nnonce="+Nonce+"\n";}
            internal static Observation Parse(string text) {
                if(text.Length>2048) throw Bad();var l=Lines(Ascii(text),12,"BetterAstralParty.WholeObservation/v1");
                var keys=new[]{"root=","plugin=","receipt=","image=","helper-receipt=","water=","update=","helper=","mode=","nonce="};var v=new string[10];
                for(var i=0;i<keys.Length;i++){if(!l[i+1].StartsWith(keys[i],StringComparison.Ordinal))throw Bad();v[i]=l[i+1].Substring(keys[i].Length);}
                if(!Regex.IsMatch(v[0],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !DigestOrAbsent(v[1]) || !DigestOrAbsent(v[2]) || !IsSlot(v[3]) || !IsSlot(v[4]) || !IsSlot(v[5]) || !Digest(v[6]) || !Digest(v[7]) || !Regex.IsMatch(v[8],@"\A(?:Fresh|OriginalDev|SignedLegacy|Current|Recovery)\z") || !Regex.IsMatch(v[9],@"\A[0-9a-f]{32}\z"))throw Bad();
                var o=new Observation {Root=v[0],Plugin=v[1],Receipt=v[2],Image=v[3],HelperReceipt=v[4],Water=v[5],Update=v[6],Helper=v[7],Mode=v[8],Nonce=v[9]};if(o.Text()!=text)throw Bad();return o;
            }
        }
        private static bool Digest(string h) {return Regex.IsMatch(h,@"\A[0-9A-F]{64}\z");}
        private static bool DigestOrAbsent(string h) {return h=="-" || Digest(h);}
        private static void MatchInputs(WindowsFileFence f,Inputs input,Observation o) {if(o.Root!=f.RootIdentity.Text || o.Update!=input.Update.DescriptorSha256 || o.Helper!=input.Helper.DescriptorHash)throw Bad();}
        private static void OtherState(WindowsFileFence f,List<MigrationPlan> pending) {
            UpdateLaunchGate.AssertNoReinstall(f);
            UpdateFaults.Read(f); // Retain valid failure latch; malformed evidence is a blocker.
            if(UpdateFaults.ReadFile(f,UpdateFaults.OperationPath)!=null)throw Bad();
            foreach(var e in UpdateRecovery.Scan(f))if(e.Pending)throw Bad(); // Old signed helper must first finish old journals.
            if(HelperDelivery.PendingInstall(f)) {
                if(pending.Count!=1)throw Bad();
                foreach(var d in Directory.GetDirectories(f.Root,"bap-helper-*",SearchOption.TopDirectoryOnly)) {
                    var work=Path.GetFileName(d);f.AssertPrivateDirectory(work);
                    using(var plan=Maybe(f,work+"/plan"))using(var committed=Maybe(f,work+"/committed"))
                        if(plan!=null && committed==null && work!=pending[0].HelperWork)throw Bad();
                }
            }
        }
        private static InstallReceipt TargetReceipt(WindowsFileFence f,Inputs input,byte[] bytes) {
            var receipt=InstallReceipt.Parse(bytes);
            if(receipt.Channels==null || receipt.Channels.Root!=f.RootIdentity.Text || receipt.Channels.ActiveChannel!=input.Update.Channel || receipt.Channels.ActiveVersion!=input.Update.Version || (input.Update.Channel=="Beta"?receipt.Channels.BetaDescriptor:receipt.Channels.StableDescriptor)!=input.Update.DescriptorSha256)throw Bad();
            foreach(var entry in input.Update.Files)if(receipt.Files[entry.Path]!=entry.Sha256)throw Bad();
            return receipt;
        }
        private static void Target(WindowsFileFence f,Inputs input,string receiptHash,WindowsProcessGuard.LaunchLease? processes=null) {
            using(var file=f.OpenFile(InstallReceipt.Path)) {
                if(file.Hash()!=receiptHash)throw Bad();TargetReceipt(f,input,UpdateTicket.Read(file,16384));
                foreach(var entry in input.Update.Files) {
                    if(processes!=null && entry.Path=="BetterAstralParty-Launcher.exe") {if(processes.ImageHash(f.Full(entry.Path))!=entry.Sha256)throw Bad();}
                    else using(var payload=f.OpenFile(entry.Path))if(payload.Hash()!=entry.Sha256)throw Bad();
                }
            }
        }
        internal static void ValidateWholeChannel(ReceiptChannelState? state,string root,string currentVersion,int currentProtocol,int currentSettings,VerifiedUpdateDescriptor incoming,string? high) {
            if(state==null) {if(incoming.Channel!="Beta")throw Bad();return;}
            if(state.Root!=root || state.ActiveVersion!=currentVersion || state.ActiveRepositoryId!=ReleaseFeedPolicy.For(state.ActiveChannel).RepositoryId)throw Bad();
            if(state.ActiveChannel==incoming.Channel)return;
            // Only the first, strictly newer public GA whole package may adopt a known Beta installation.
            if(state.ActiveChannel!="Beta" || incoming.Channel!="Stable" || state.BetaRepositoryId!=ReleaseFeedPolicy.BetaRepositoryId
                || state.StableRepositoryId!=0 || state.StableVersion!=null || state.StableDescriptor!="-"
                || currentProtocol!=2 || currentSettings!=1 || incoming.Protocol!=2 || incoming.SettingsSchema!=1 || incoming.Files.Count!=8
                || incoming.Repository!=ReleaseFeedPolicy.StableRepository || incoming.RepositoryId!=ReleaseFeedPolicy.StableRepositoryId
                || incoming.ReleaseId<=0 || incoming.AssetId<=0 || UpdateVersion.Parse(incoming.Version).IsPrerelease)throw Bad();
            var next=UpdateVersion.Parse(incoming.Version);
            if(next.CompareTo(UpdateVersion.Parse(currentVersion))<=0 || high!=null && next.CompareTo(UpdateVersion.Parse(high))<=0)
                throw new UpdateValidationException(UpdateFailure.NotNewer);
        }
        private static string Observe(string root,string package,string version) {
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
                        foreach(var name in InstallReceipt.Owned)using(var file=f.OpenFile(name))if(file.Hash()!=receipt.Files[name])throw Bad();
                        using(var plugin=f.OpenFile(UpdateTrust.PluginPath)) {
                            o.Plugin=plugin.Hash();var current=InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes));var cmp=UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse(current.Version));
                            if(cmp<0)throw new UpdateValidationException(UpdateFailure.NotNewer);
                            if(cmp==0)foreach(var entry in input.Update.Files)if(receipt.Files[entry.Path]!=entry.Sha256)throw new UpdateValidationException(UpdateFailure.NotNewer);
                            ValidateWholeChannel(receipt.Channels,o.Root,current.Version,current.UpdateProtocol,current.SettingsSchema,input.Update,high);
                        }
                    } else {
                        foreach(var name in InstallReceipt.Owned)using(var file=Maybe(f,name))if(file!=null)throw Bad();
                        if(high!=null)throw Bad();
                    }
                }
                if(pending.Count!=0) {
                    if(pending.Count!=1)throw Bad();var p=pending[0];CheckPlan(f,input,p);Target(f,input,p.Receipt);
                    o.Mode="Recovery";o.Image=p.OldImage;o.HelperReceipt=p.OldReceipt;return o.Text();
                }
                using(var image=Maybe(f,HelperDescriptor.Image))o.Image=Slot(image);
                using(var receipt=Maybe(f,HelperDelivery.Receipt))o.HelperReceipt=Slot(receipt);
                if(o.Image=="-\t-" && o.HelperReceipt=="-\t-")o.Mode="Fresh";
                else if(o.Image.EndsWith("\t"+OriginalDevImage,StringComparison.Ordinal) && o.Plugin==OriginalDevPlugin && o.Receipt!="-" && o.HelperReceipt=="-\t-" && Directory.GetDirectories(f.Root,"bap-helper-*",SearchOption.TopDirectoryOnly).Length==0)o.Mode="OriginalDev";
                else {
                    var actual=HelperDelivery.Fingerprint(f,UpdateTrust.Production());if(actual==null || o.Receipt=="-")throw Bad();
                    using(var image=f.OpenLaunchImage(HelperDescriptor.Image)) {
                        var bytes=UpdateTicket.Read(image,UpdateTrust.MaxFileBytes);var matched=false;
                        foreach(var dir in Directory.GetDirectories(f.Root,"bap-helper-*",SearchOption.TopDirectoryOnly)) {
                            var work=Path.GetFileName(dir);
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
                    o.Mode=actual==input.Helper.Sha256 && HelperDelivery.Fingerprint(f,UpdateTrust.Production(),2)==actual ? "Current":"SignedLegacy";
                }
                return o.Text();
            }
        }
        private sealed class PreparedObservation {internal string Text="",Receipt="";}
        private static readonly Dictionary<string,PreparedObservation> ApprovedObservations=new Dictionary<string,PreparedObservation>(StringComparer.Ordinal);
        internal static string Validate(string root,string package,string version) {
            var o=Observation.Parse(Observe(root,package,version));o.Nonce=Guid.NewGuid().ToString("N");
            lock(ApprovedObservations) {if(ApprovedObservations.Count>=32)throw Bad();ApprovedObservations.Add(o.Nonce,new PreparedObservation {Text=o.Text()});}
            return o.Text();
        }
        private static PreparedObservation Approved(Observation o) {
            lock(ApprovedObservations) {PreparedObservation value;if(!ApprovedObservations.TryGetValue(o.Nonce,out value!) || value.Text!=o.Text())throw Bad();return value;}
        }
        private static string FindHash(VerifiedUpdateDescriptor d,string name){foreach(var file in d.Files)if(file.Path==name)return file.Sha256;throw Bad();}
        internal static string PrepareReceipt(string root,string package,string version,string observation,string loaderBefore) {
            var o=Observation.Parse(observation);
            var approved=Approved(o);var observed=Observation.Parse(Observe(root,package,version));observed.Nonce=o.Nonce;
            if(observed.Text()!=observation)throw Bad();
            using(var input=new Inputs(root,package,version))using(var f=new WindowsFileFence(root)) {
                MatchInputs(f,input,o);InstallReceipt old;
                using(var file=Maybe(f,InstallReceipt.Path)) {
                    if(file!=null) {
                        old=InstallReceipt.Parse(UpdateTicket.Read(file,16384));
                        using(var plugin=f.OpenFile(UpdateTrust.PluginPath)) {
                            var current=InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes));
                            ValidateWholeChannel(old.Channels,o.Root,current.Version,current.UpdateProtocol,current.SettingsSchema,input.Update,UpdateTransaction.ReadHighWater(f));
                        }
                    }
                    else {
                        if(loaderBefore!="true" && loaderBefore!="false" && loaderBefore!="null")throw Bad();
                        var json=new StringBuilder("{\"schema\":1,\"owner\":\""+InstalledPluginMetadata.Owner+"\",\"loaderBefore\":"+loaderBefore+",\"files\":[");
                        for(var i=0;i<InstallReceipt.Owned.Length;i++){if(i!=0)json.Append(',');var name=InstallReceipt.Owned[i];json.Append("{\"path\":\"").Append(name).Append("\",\"sha256\":\"").Append(FindHash(input.Update,name)).Append("\"}");}old=InstallReceipt.Parse(Ascii(json.Append("]}").ToString()));
                    }
                }
                string? descriptor=null;var high=UpdateTransaction.ReadHighWater(f);
                if(high!=null)using(var water=f.OpenFile(UpdateTransaction.HighWaterPath))descriptor=Encoding.ASCII.GetString(UpdateTicket.Read(water,2048)).Split('\n')[3].Substring(11);
                var target=InstallReceipt.Parse(old.Updated(input.Update));
                var result=target.InitializeChannel(o.Root,input.Update,high,descriptor);
                lock(ApprovedObservations)approved.Receipt=UpdateTrust.Hash(result);return Encoding.ASCII.GetString(result);
            }
        }
        internal sealed class MigrationPlan {
            internal string Root="",Work="",WorkId="",HelperWork="",HelperWorkId="",Update="",Helper="",Receipt="",Water="",OldImage="",OldReceipt="",NewImage="",NewReceipt="";
            internal string Text() {return "BetterAstralParty.WholeHelperMigration/v1\nroot="+Root+"\nwork="+Work+"\nwork-id="+WorkId+"\nhelper-work="+HelperWork+"\nhelper-work-id="+HelperWorkId+"\nupdate="+Update+"\nhelper="+Helper+"\nreceipt="+Receipt+"\nwater="+Water+"\nold-image="+OldImage+"\nold-receipt="+OldReceipt+"\nnew-image="+NewImage+"\nnew-receipt="+NewReceipt+"\n";}
            internal static MigrationPlan Parse(byte[] bytes) {
                var l=Lines(bytes,15,"BetterAstralParty.WholeHelperMigration/v1");var keys=new[]{"root=","work=","work-id=","helper-work=","helper-work-id=","update=","helper=","receipt=","water=","old-image=","old-receipt=","new-image=","new-receipt="};var v=new string[13];
                for(var i=0;i<keys.Length;i++){if(!l[i+1].StartsWith(keys[i],StringComparison.Ordinal))throw Bad();v[i]=l[i+1].Substring(keys[i].Length);}
                if(!Regex.IsMatch(v[0],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1],@"\Abap-helperbridge-[0-9a-f]{32}\z") || !Regex.IsMatch(v[3],@"\Abap-helper-[0-9a-f]{32}\z") || !Regex.IsMatch(v[2],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[4],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Digest(v[5]) || !Digest(v[6]) || !Digest(v[7]) || !IsSlot(v[8]) || !IsSlot(v[9]) || !IsSlot(v[10]) || !IsSlot(v[11]) || v[11]=="-\t-" || !IsSlot(v[12]) || v[12]=="-\t-")throw Bad();
                var p=new MigrationPlan {Root=v[0],Work=v[1],WorkId=v[2],HelperWork=v[3],HelperWorkId=v[4],Update=v[5],Helper=v[6],Receipt=v[7],Water=v[8],OldImage=v[9],OldReceipt=v[10],NewImage=v[11],NewReceipt=v[12]};if(UpdateTrust.Hash(Ascii(p.Text()))!=UpdateTrust.Hash(bytes))throw Bad();return p;
            }
        }
        private static List<MigrationPlan> Scan(WindowsFileFence f) {
            var result=new List<MigrationPlan>();var dirs=Directory.GetDirectories(f.Root,"bap-helperbridge-*",SearchOption.TopDirectoryOnly);if(dirs.Length>64)throw Bad();
            foreach(var d in dirs) {
                var work=Path.GetFileName(d);if(!Regex.IsMatch(work,@"\Abap-helperbridge-[0-9a-f]{32}\z"))throw Bad();f.AssertPrivateDirectory(work);
                using(var file=Maybe(f,work+"/plan")) {
                    if(file==null)throw Bad();var bytes=UpdateTicket.Read(file,4096);var p=MigrationPlan.Parse(bytes);if(p.Root!=f.RootIdentity.Text || p.Work!=work || p.WorkId!=f.DirectoryIdentity(work).Text)throw Bad();
                    using(var marker=Maybe(f,work+"/committed")) {if(marker==null)result.Add(p);else if(Encoding.ASCII.GetString(UpdateTicket.Read(marker,128))!=UpdateTrust.Hash(bytes)+"\n")throw Bad();}
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
                if(p.OldReceipt=="-\t-") {if(p.OldImage.Split('\t')[1]!=OriginalDevImage)throw Bad();}
                else if(HelperDelivery.ArchivedFingerprint(f,UpdateTrust.Production(),imagePath,receiptPath)!=p.OldImage.Split('\t')[1])throw Bad();
            }
            Archive(f,p,HelperDescriptor.Image,p.OldImage,p.NewImage,"old-image",boundary);
            Archive(f,p,HelperDelivery.Receipt,p.OldReceipt,p.NewReceipt,"old-receipt",boundary);
            HelperDelivery.FinishWholeMigration(f,p.HelperWork,UpdateTrust.Production(),boundary);
            CheckItem(f,p,HelperDescriptor.Image,p.OldImage,p.NewImage,"image","old-image");
            CheckItem(f,p,HelperDelivery.Receipt,p.OldReceipt,p.NewReceipt,"receipt","old-receipt");
            if(HelperDelivery.Fingerprint(f,UpdateTrust.Production(),2)!=input.Helper.Sha256)throw Bad();
            Target(f,input,p.Receipt,processes);CheckPlan(f,input,p);
            boundary?.Invoke("before:bridge-committed");Write(f,p.Work+"/committed",Ascii(UpdateTrust.Hash(Ascii(p.Text()))+"\n"));boundary?.Invoke("after:bridge-committed");
        }
        // Intent files belong to a native private directory and bind exact root/object identities.
        // They grant no permission to adopt an unrelated helper or repair partial owned files.
        private sealed class WholeIntent {
            internal string Root="",Work="",WorkId="",Observation="",Receipt="",Plugin="-\t-",OldReceipt="-\t-";
            internal string Text(){return "BetterAstralParty.WholeInstallIntent/v1\nroot="+Root+"\nwork="+Work+"\nwork-id="+WorkId+"\nobservation="+Convert.ToBase64String(Ascii(Observation))+"\ntarget-receipt="+Receipt+"\nold-plugin="+Plugin+"\nold-receipt="+OldReceipt+"\n";}
            internal static WholeIntent Parse(byte[] bytes){
                var l=Lines(bytes,9,"BetterAstralParty.WholeInstallIntent/v1");var keys=new[]{"root=","work=","work-id=","observation=","target-receipt=","old-plugin=","old-receipt="};var v=new string[7];
                for(var i=0;i<keys.Length;i++){if(!l[i+1].StartsWith(keys[i],StringComparison.Ordinal))throw Bad();v[i]=l[i+1].Substring(keys[i].Length);}
                if(!Regex.IsMatch(v[0],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1],@"\Abap-wholeintent-[0-9a-f]{32}\z") || !Regex.IsMatch(v[2],@"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Digest(v[4]) || !IsSlot(v[5]) || !IsSlot(v[6]))throw Bad();
                string observed;try{observed=Encoding.ASCII.GetString(Convert.FromBase64String(v[3]));}catch(FormatException){throw Bad();}
                var o=WholeInstallBridge.Observation.Parse(observed);
                if(o.Root!=v[0] || o.Nonce!="00000000000000000000000000000000" || o.Mode!="Fresh" && o.Mode!="OriginalDev" && o.Mode!="SignedLegacy")throw Bad();
                var intent=new WholeIntent {Root=v[0],Work=v[1],WorkId=v[2],Observation=observed,Receipt=v[4],Plugin=v[5],OldReceipt=v[6]};
                if(UpdateTrust.Hash(Ascii(intent.Text()))!=UpdateTrust.Hash(bytes))throw Bad();return intent;
            }
        }
        private static List<WholeIntent> ScanIntents(WindowsFileFence f){
            var result=new List<WholeIntent>();var dirs=Directory.GetDirectories(f.Root,"bap-wholeintent-*",SearchOption.TopDirectoryOnly);if(dirs.Length>64)throw Bad();
            foreach(var directory in dirs){
                var work=Path.GetFileName(directory);if(!Regex.IsMatch(work,@"\Abap-wholeintent-[0-9a-f]{32}\z"))throw Bad();f.AssertPrivateDirectory(work);
                using(var file=f.OpenFile(work+"/plan")){
                    var bytes=UpdateTicket.Read(file,8192);var intent=WholeIntent.Parse(bytes);
                    if(intent.Root!=f.RootIdentity.Text || intent.Work!=work || intent.WorkId!=f.DirectoryIdentity(work).Text)throw Bad();
                    using(var committed=Maybe(f,work+"/committed"))using(var cancelled=Maybe(f,work+"/cancelled")){
                        if(committed!=null && cancelled!=null)throw Bad();var marker=committed??cancelled;
                        if(marker==null)result.Add(intent);else if(Encoding.ASCII.GetString(UpdateTicket.Read(marker,128))!=UpdateTrust.Hash(bytes)+"\n")throw Bad();
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
                    if(plugin==null)throw Bad();var prior=InstallReceipt.Parse(UpdateTicket.Read(receipt,16384));
                    if(prior.Files[UpdateTrust.PluginPath]!=o.Plugin)throw Bad();
                    var metadata=InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes));
                    if(UpdateVersion.Parse(metadata.Version).CompareTo(UpdateVersion.Parse(input.Update.Version))>0)throw Bad();
                    ValidateWholeChannel(prior.Channels,o.Root,metadata.Version,metadata.UpdateProtocol,metadata.SettingsSchema,input.Update,UpdateTransaction.ReadHighWater(f));
                }
            }
            if(o.Mode=="OriginalDev" && (o.Plugin!=OriginalDevPlugin || !o.Image.EndsWith("\t"+OriginalDevImage,StringComparison.Ordinal) || o.HelperReceipt!="-\t-" || o.Receipt=="-"))throw Bad();
            if(o.Mode=="Fresh" && (o.Image!="-\t-" || o.HelperReceipt!="-\t-"))throw Bad();
            if(o.Mode=="SignedLegacy" && (o.Image=="-\t-" || o.HelperReceipt=="-\t-"))throw Bad();
            using(var water=Maybe(f,UpdateTransaction.HighWaterPath))if(Slot(water)!=o.Water)throw Bad();
        }
        private static void Original(WindowsFileFence f,WholeIntent intent){
            var o=Observation.Parse(intent.Observation);
            using(var image=Maybe(f,HelperDescriptor.Image))using(var helper=Maybe(f,HelperDelivery.Receipt))using(var receipt=Maybe(f,InstallReceipt.Path)){
                if(Slot(image)!=o.Image || Slot(helper)!=o.HelperReceipt || (receipt?.Hash()??"-")!=o.Receipt)throw Bad();
                if(receipt==null){foreach(var name in InstallReceipt.Owned)using(var file=Maybe(f,name))if(file!=null)throw Bad();}
                else {var prior=InstallReceipt.Parse(UpdateTicket.Read(receipt,16384));foreach(var name in InstallReceipt.Owned)using(var file=f.OpenFile(name))if(file.Hash()!=prior.Files[name])throw Bad();}
            }
        }
        private static void RetireIntent(WindowsFileFence f,WholeIntent intent,bool committed){
            Write(f,intent.Work+(committed?"/committed":"/cancelled"),Ascii(UpdateTrust.Hash(Ascii(intent.Text()))+"\n"));
        }
        private sealed class Maintenance : IDisposable {
            internal readonly WindowsFileFence Fence;private WindowsFileFence.FileLease? _lock;
            internal Maintenance(string root){
                Fence=new WindowsFileFence(root);try{_lock=Maybe(Fence,"BetterAstralParty.update.lock",true)??Fence.OpenFile("BetterAstralParty.update.lock",true,true);}catch{Dispose();throw;}
            }
            public void Dispose(){_lock?.Dispose();_lock=null;Fence.Dispose();}
        }
        internal static IDisposable GuardRecovery(string root,string package,string version){
            var lease=new Maintenance(root);try{using(var input=new Inputs(root,package,version)){
                var pending=Scan(lease.Fence);OtherState(lease.Fence,pending);
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
                var actual=Observation.Parse(Observe(root,package,version));actual.Nonce=o.Nonce;if(actual.Text()!=o.Text())throw Bad();
                WindowsProcessGuard.AssertNoRunning(new[]{f.Full("AstralParty_INT.exe"),f.Full("BetterAstralParty-Launcher.exe"),f.Full(HelperDescriptor.Image)});
                if(o.Mode=="Current" || o.Mode=="Recovery")return lease;
                var durable=Observation.Parse(o.Text());durable.Nonce="00000000000000000000000000000000";
                var intent=new WholeIntent {Root=o.Root,Work="bap-wholeintent-"+Guid.NewGuid().ToString("N"),Observation=durable.Text(),Receipt=approved.Receipt};
                intent.WorkId=f.CreatePrivateDirectory(intent.Work).Text;
                using(var oldPlugin=Maybe(f,UpdateTrust.PluginPath))if(oldPlugin!=null)Write(f,intent.Work+"/old-plugin",UpdateTicket.Read(oldPlugin,UpdateTrust.MaxFileBytes));
                using(var oldReceipt=Maybe(f,InstallReceipt.Path))if(oldReceipt!=null)Write(f,intent.Work+"/old-receipt",UpdateTicket.Read(oldReceipt,16384));
                using(var oldPlugin=Maybe(f,intent.Work+"/old-plugin"))intent.Plugin=Slot(oldPlugin);
                using(var oldReceipt=Maybe(f,intent.Work+"/old-receipt"))intent.OldReceipt=Slot(oldReceipt);
                Write(f,intent.Work+"/target-receipt",receiptBytes);Write(f,intent.Work+"/plan",Ascii(intent.Text()));
                CheckIntent(f,input,intent);Original(f,intent);boundary?.Invoke("whole-intent-durable");
            }return lease;}catch{lease.Dispose();throw;}
        }
        private static MigrationPlan CreateMigration(WindowsFileFence f,Inputs input,Observation o,string receiptHash,Action<string>? boundary){
            using(var oldImage=Maybe(f,HelperDescriptor.Image))using(var oldReceipt=Maybe(f,HelperDelivery.Receipt))if(Slot(oldImage)!=o.Image || Slot(oldReceipt)!=o.HelperReceipt)throw Bad();
            var p=new MigrationPlan {Root=o.Root,Work="bap-helperbridge-"+Guid.NewGuid().ToString("N"),HelperWork="bap-helper-"+Guid.NewGuid().ToString("N"),Update=o.Update,Helper=o.Helper,Receipt=receiptHash,Water=o.Water,OldImage=o.Image,OldReceipt=o.HelperReceipt};
            p.WorkId=f.CreatePrivateDirectory(p.Work).Text;p.HelperWorkId=f.CreatePrivateDirectory(p.HelperWork).Text;
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
            foreach(var d in Directory.GetDirectories(f.Root,"bap-helperbridge-*",SearchOption.TopDirectoryOnly)){
                var work=Path.GetFileName(d);using(var marker=Maybe(f,work+"/committed"))if(marker!=null)using(var file=f.OpenFile(work+"/plan")){
                    var p=MigrationPlan.Parse(UpdateTicket.Read(file,4096));var o=Observation.Parse(intent.Observation);
                    if(p.Receipt!=intent.Receipt || p.Update!=o.Update || p.Helper!=o.Helper || p.OldImage!=o.Image || p.OldReceipt!=o.HelperReceipt)continue;
                    MatchIntentPlan(intent,p);CheckPlan(f,input,p);Target(f,input,p.Receipt,processes);
                    CheckItem(f,p,HelperDescriptor.Image,p.OldImage,p.NewImage,"image","old-image");CheckItem(f,p,HelperDelivery.Receipt,p.OldReceipt,p.NewReceipt,"receipt","old-receipt");
                    if(HelperDelivery.Fingerprint(f,UpdateTrust.Production(),2)!=input.Helper.Sha256)throw Bad();return true;
                }
            }return false;
        }
        internal static void Recover(string root,string package,string version,Action<string>? boundary=null) {
            using(var input=new Inputs(root,package,version))using(var lease=new Maintenance(root)) {
                var f=lease.Fence;var pending=Scan(f);OtherState(f,pending);var intents=ScanIntents(f);if(pending.Count>1 || intents.Count>1)throw Bad();
                var intent=intents.Count==1?intents[0]:null;if(intent!=null)CheckIntent(f,input,intent);
                if(pending.Count==0 && intent==null)return;
                if(pending.Count==0 && intent!=null){
                    string receipt;using(var file=Maybe(f,InstallReceipt.Path))receipt=file?.Hash()??"-";
                    if(receipt!=intent.Receipt){
                        Original(f,intent);
                        WindowsProcessGuard.AssertNoRunning(new[]{f.Full("AstralParty_INT.exe"),f.Full("BetterAstralParty-Launcher.exe"),f.Full(HelperDescriptor.Image)});
                        RetireIntent(f,intent,false);boundary?.Invoke("whole-intent-restored");return;
                    }
                }
                using(var processes=WindowsProcessGuard.Acquire(f,new[]{"AstralParty_INT.exe","BetterAstralParty-Launcher.exe"})) {
                    WindowsProcessGuard.AssertNoRunning(new[]{f.Full(HelperDescriptor.Image)});
                    if(pending.Count==1){if(intent!=null)MatchIntentPlan(intent,pending[0]);Finish(f,input,pending[0],boundary,processes);}
                    else if(intent!=null && !CompletedIntent(f,input,intent,processes)){
                        Target(f,input,intent.Receipt,processes);var o=Observation.Parse(intent.Observation);
                        var plan=CreateMigration(f,input,o,intent.Receipt,boundary);Finish(f,input,plan,boundary,processes);
                    }
                    if(intent!=null)RetireIntent(f,intent,true);
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
                        if(pending.Count!=0 || intents.Count!=0 || HelperDelivery.Fingerprint(f,UpdateTrust.Production(),2)!=input.Helper.Sha256)throw Bad();return;
                    }
                    if(intents.Count!=1)throw Bad();var intent=intents[0];CheckIntent(f,input,intent);
                    var durable=Observation.Parse(o.Text());durable.Nonce="00000000000000000000000000000000";
                    if(intent.Observation!=durable.Text() || intent.Receipt!=receiptHash)throw Bad();
                    var plan=pending.Count==1?pending[0]:CreateMigration(f,input,o,receiptHash,boundary);
                    MatchIntentPlan(intent,plan);Finish(f,input,plan,boundary,processes);RetireIntent(f,intent,true);
                }
            }
        }
    }
}
