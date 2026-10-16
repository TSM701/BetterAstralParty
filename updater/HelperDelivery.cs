#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Observability;
namespace BetterAstralParty.Updating {
    internal sealed class DetachedSignature {
        internal readonly string KeyId; internal readonly byte[] Signature;
        internal DetachedSignature(string key, byte[] signature) { KeyId=key; Signature=(byte[])signature.Clone(); }
        internal byte[] Bytes() { return Encoding.ASCII.GetBytes("BetterAstralParty.UpdateSignature/v1\nkey-id="+KeyId+"\nsignature="+Convert.ToBase64String(Signature)+"\n"); }
        internal static DetachedSignature Parse(byte[] bytes) {
            if(bytes.Length==0 || bytes.Length>1024) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            foreach(var value in bytes) if(value>127 || value==13) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            var lines=Encoding.ASCII.GetString(bytes).Split('\n');
            if(lines.Length!=4 || lines[0]!="BetterAstralParty.UpdateSignature/v1" || !lines[1].StartsWith("key-id=",StringComparison.Ordinal) || !lines[2].StartsWith("signature=",StringComparison.Ordinal) || lines[3]!="") throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            var key=lines[1].Substring(7); if(!Regex.IsMatch(key,@"\A[A-Za-z0-9_-]{1,40}\z")) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            try { var result=new DetachedSignature(key,Convert.FromBase64String(lines[2].Substring(10)));
                if(result.Signature.Length==0 || result.Signature.Length>UpdateTrust.MaxSignatureBytes || Encoding.ASCII.GetString(result.Bytes())!=Encoding.ASCII.GetString(bytes)) throw new FormatException();
                return result;
            } catch(FormatException) { throw new UpdateValidationException(UpdateFailure.InvalidSignature); }
        }
    }
    internal sealed class HelperDescriptor {
        internal const string Image="BetterAstralParty-UpdateHelper.exe";
        internal readonly string Version, Sha256, DescriptorHash, KeyId; internal readonly long Size;
        internal readonly int Protocol;
        private HelperDescriptor(string version,long size,string hash,string descriptor,string key,int protocol) {Version=version;Size=size;Sha256=hash;DescriptorHash=descriptor;KeyId=key;Protocol=protocol;}
        internal static byte[] Prepare(string version,byte[] image,int protocol=4) {
            if(protocol!=4) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            var parsed=UpdateVersion.Parse(version); if(parsed.Tag!=version || image.Length==0 || image.Length>UpdateTrust.MaxFileBytes) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            Executable(image);
            return Encoding.ASCII.GetBytes("BetterAstralParty.HelperDelivery/v"+protocol+"\nrepository=TSM701/BetterAstralParty\nversion="+version+"\nplatform="+UpdateTrust.PlatformId+"\nimage="+Image+"\nbytes="+image.Length.ToString(CultureInfo.InvariantCulture)+"\nsha256="+UpdateTrust.Hash(image)+"\n"+(protocol==4?"update-protocol=4\n":""));
        }
        internal static void Executable(byte[] image) {
            if(image.Length<512 || image[0]!=77 || image[1]!=90) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
            var offset=BitConverter.ToInt32(image,60);
            if(offset<64 || offset>image.Length-24 || image[offset]!=80 || image[offset+1]!=69 || image[offset+2]!=0 || image[offset+3]!=0 || BitConverter.ToUInt16(image,offset+4)!=0x8664 || (BitConverter.ToUInt16(image,offset+22)&0x2000)!=0) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
        }
        internal static HelperDescriptor Verify(UpdateTrust trust,byte[] descriptor,DetachedSignature envelope,byte[] image,string? expectedVersion=null) {
            var bytes=trust.VerifyDetached(descriptor,envelope.KeyId,envelope.Signature);
            var lines=Encoding.ASCII.GetString(bytes).Split('\n');
            var protocol=lines.Length==9 && lines[0]=="BetterAstralParty.HelperDelivery/v4" && lines[7]=="update-protocol=4" && lines[8]==""?4:0;
            if(protocol==0 || lines[1]!="repository=TSM701/BetterAstralParty" || !lines[2].StartsWith("version=",StringComparison.Ordinal) || lines[3]!="platform="+UpdateTrust.PlatformId || lines[4]!="image="+Image || !lines[5].StartsWith("bytes=",StringComparison.Ordinal) || !lines[6].StartsWith("sha256=",StringComparison.Ordinal)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            var version=lines[2].Substring(8); long size;
            if(UpdateVersion.Parse(version).Tag!=version || expectedVersion!=null && version!=expectedVersion || !long.TryParse(lines[5].Substring(6),NumberStyles.None,CultureInfo.InvariantCulture,out size) || size<=0 || size>UpdateTrust.MaxFileBytes || size!=image.Length || lines[6].Substring(7)!=UpdateTrust.Hash(image)) throw new UpdateValidationException(UpdateFailure.WrongTarget);
            var canonical=Prepare(version,image,protocol); if(UpdateTrust.Hash(bytes)!=UpdateTrust.Hash(canonical)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            return new HelperDescriptor(version,size,UpdateTrust.Hash(image),UpdateTrust.Hash(bytes),envelope.KeyId,protocol);
        }
    }
    internal enum HelperInstallResult { Installed, AlreadyInstalled, ManualUpgradeRequired, RecoveryRequired }
    // First delivery only: never replace/adopt an unreceipted existing helper or expand eight-file ownership.
    internal static class HelperDelivery {
        internal const string Receipt="BetterAstralParty.helper.receipt";
        private static ApplySafetyException Bad() {return new ApplySafetyException(ApplyFailure.RecoveryRequired);}
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence fence,string path,bool mutable=false) {
            try {return fence.OpenFile(path,mutable);} catch(ApplySafetyException error) when(error.NativeError==2) {return null;}
        }
        private static WindowsFileFence.FileLease? MaybeImage(WindowsFileFence fence) {
            try {return fence.OpenLaunchImage(HelperDescriptor.Image);} catch(ApplySafetyException error) when(error.NativeError==2) {return null;}
        }
        private static WindowsFileFence.FileLease? MaybeLaunch(WindowsFileFence fence,string path) {
            try {return fence.OpenLaunchImage(path);} catch(ApplySafetyException error) when(error.NativeError==2) {return null;}
        }
        private static WindowsFileFence.FileLease Lock(WindowsFileFence fence) {
            try {return fence.OpenFile("BetterAstralParty.update.lock",true);} catch(ApplySafetyException error) when(error.NativeError==2) {return fence.OpenFile("BetterAstralParty.update.lock",true,true);}
        }
        internal static void Write(WindowsFileFence fence,string path,byte[] bytes) {
            // Publish only flushed whole files; retain interrupted temporary evidence, never overwrite.
            if(!Regex.IsMatch(path,@"\ABetterAstralParty\.Update/bap-v4-helper(?:auto)?-[0-9a-f]{32}/[a-z-]+\z"))throw Bad();
            var pending=path+"-pending-"+Guid.NewGuid().ToString("N");
            using(var file=fence.OpenFile(pending,true,true)){file.Stream.Write(bytes,0,bytes.Length);file.Flush();file.RenameTo(fence,path);file.Flush();}
        }
        private static byte[] ReceiptBytes(WindowsFileFence fence,HelperDescriptor descriptor,string imageId) { return Encoding.ASCII.GetBytes("BetterAstralParty.HelperReceipt/v1\nroot="+fence.RootIdentity.Text+"\nimage-id="+imageId+"\nversion="+descriptor.Version+"\nsha256="+descriptor.Sha256+"\ndescriptor="+descriptor.DescriptorHash+"\nkey-id="+descriptor.KeyId+"\n"); }
        internal static bool Eligible(WindowsFileFence fence,WindowsProcessGuard.LaunchLease? processes=null) {
            try { using(var file=fence.OpenFile(InstallReceipt.Path)) {
                var receipt=InstallReceipt.Parse(UpdateTicket.Read(file,16384));
                foreach(var owned in InstallReceipt.Owned) {
                    if(processes!=null && owned=="BetterAstralParty-Launcher.exe") {if(processes.ImageHash(fence.Full(owned))!=receipt.Files[owned]) return false;}
                    else using(var payload=fence.OpenFile(owned)) if(payload.Hash()!=receipt.Files[owned]) return false;
                }
                return true;
            } } catch(ApplySafetyException error) when(error.NativeError==2 || error.Failure==ApplyFailure.InvalidState) {return false;}
        }
        internal static string? Fingerprint(WindowsFileFence fence,UpdateTrust trust,int minimumProtocol=4) {
            if(PendingInstall(fence)) throw Bad();
            return FingerprintAt(fence,trust,HelperDescriptor.Image,Receipt,minimumProtocol);
        }
        internal static string? ArchivedFingerprint(WindowsFileFence fence,UpdateTrust trust,string imagePath,string receiptPath) {
            foreach(var path in new[]{imagePath,receiptPath})
                if(path!=HelperDescriptor.Image && path!=Receipt && !Regex.IsMatch(path,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helper(?:bridge|auto)-[0-9a-f]{32}/old-(?:image|receipt)\z"))throw Bad();
            return FingerprintAt(fence,trust,imagePath,receiptPath,3);
        }
        private static string? FingerprintAt(WindowsFileFence fence,UpdateTrust trust,string imagePath,string receiptPath,int minimumProtocol) {
            var descriptor=AuthenticatedAt(fence,trust,imagePath,receiptPath);
            return descriptor!=null && descriptor.Protocol>=minimumProtocol?descriptor.Sha256:null;
        }
        internal static HelperDescriptor? AuthenticatedAt(WindowsFileFence fence,UpdateTrust trust,string imagePath,string receiptPath) {
            if(!trust.Configured) return null;
            using(var image=MaybeLaunch(fence,imagePath)) using(var receipt=Maybe(fence,receiptPath)) {
                if(image==null || receipt==null) return null;
                return AuthenticatedPair(fence,trust,image,receipt);
            }
        }
        private static HelperDescriptor AuthenticatedPair(WindowsFileFence fence,UpdateTrust trust,WindowsFileFence.FileLease image,WindowsFileFence.FileLease receipt) {
                // Exact live object IDs + signed descriptor/receipt still select the
                // provenance; completed historical journal count is not a trust limit.
                var actual=UpdateTicket.Read(receipt,1024);var dirs=UpdateWorkPath.Directories(fence, "helper");
                foreach(var dir in dirs) {
                    var work=dir;if(!Regex.IsMatch(work,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helper-[0-9a-f]{32}\z")) throw Bad();
                    fence.AssertPrivateDirectory(work);
                    try {
                        byte[] plan,descriptor,signature;
                        using(var file=fence.OpenFile(work+"/plan")) plan=UpdateTicket.Read(file,2048);
                        using(var file=fence.OpenFile(work+"/descriptor")) descriptor=UpdateTicket.Read(file,UpdateTrust.MaxDescriptorBytes);
                        using(var file=fence.OpenFile(work+"/signature")) signature=UpdateTicket.Read(file,1024);
                        var lines=Encoding.ASCII.GetString(plan).Split('\n');
                        if(lines.Length!=8 || lines[0]!="BetterAstralParty.HelperInstall/v1" || lines[1]!="root="+fence.RootIdentity.Text || lines[2]!="work="+work || lines[3]!="work-id="+fence.DirectoryIdentity(work).Text || lines[4]!="descriptor="+UpdateTrust.Hash(descriptor) || lines[5]!="image-id="+image.Identity.Text || lines[6]!="receipt-id="+receipt.Identity.Text || lines[7]!="") continue;
                        using(var committed=fence.OpenFile(work+"/committed")) if(Encoding.ASCII.GetString(UpdateTicket.Read(committed,128))!=UpdateTrust.Hash(plan)+"\n") throw Bad();
                        // Retained old helper journals bind different original object IDs. Verify only
                        // the exact plan for the current image/receipt, without erasing old evidence.
                        var verified=HelperDescriptor.Verify(trust,descriptor,DetachedSignature.Parse(signature),UpdateTicket.Read(image,UpdateTrust.MaxFileBytes));
                        if(UpdateTrust.Hash(actual)==UpdateTrust.Hash(ReceiptBytes(fence,verified,image.Identity.Text))) return verified;
                    } catch(ApplySafetyException error) when(error.NativeError==2) { }
                }
                throw Bad();
        }
        // Backup copies have new file IDs. Authenticate their original signed delivery
        // without weakening the live helper's object-identity checks in FingerprintAt.
        internal static string RemovedEvidence(WindowsFileFence fence,UpdateTrust trust,byte[] image,byte[] receipt) {
            return RemovedProof(fence,trust,image,receipt).Evidence;
        }
        private sealed class CopyProof {
            internal string Evidence="";internal byte[] Descriptor=Array.Empty<byte>(),Signature=Array.Empty<byte>();
            internal HelperDescriptor Verified=null!;
        }
        private static CopyProof RemovedProof(WindowsFileFence fence,UpdateTrust trust,byte[] image,byte[] receipt,string? expectedEvidence=null,string? exclude=null) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            if(receipt.Length==0 || receipt.Length>1024) throw Bad();
            foreach(var value in receipt) if(value>127 || value==13) throw Bad();
            var actual=Encoding.ASCII.GetString(receipt).Split('\n');
            if(actual.Length!=8 || actual[0]!="BetterAstralParty.HelperReceipt/v1" || actual[1]!="root="+fence.RootIdentity.Text || !Regex.IsMatch(actual[2],@"\Aimage-id=[0-9A-F]{8}:[0-9A-F]{16}\z") || actual[7]!="") throw Bad();
            var dirs=new List<string>(UpdateWorkPath.Directories(fence, "helper"));dirs.Sort(StringComparer.Ordinal);
            CopyProof? selected=null;string? descriptorHash=null;
            foreach(var dir in dirs) {
                var work=dir;if(!Regex.IsMatch(work,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helper-[0-9a-f]{32}\z")) throw Bad();
                if(work==exclude)continue;
                fence.AssertPrivateDirectory(work);
                using(var plan=Maybe(fence,work+"/plan")) {
                    if(plan==null) continue;
                    var bytes=UpdateTicket.Read(plan,2048);var lines=Encoding.ASCII.GetString(bytes).Split('\n');
                    if(lines.Length!=8 || lines[0]!="BetterAstralParty.HelperInstall/v1" || lines[1]!=actual[1] || lines[2]!="work="+work || lines[3]!="work-id="+fence.DirectoryIdentity(work).Text || lines[4]!=actual[5] || lines[5]!=actual[2] || !Regex.IsMatch(lines[6],@"\Areceipt-id=[0-9A-F]{8}:[0-9A-F]{16}\z") || lines[7]!="") continue;
                    using(var descriptor=fence.OpenFile(work+"/descriptor")) using(var signature=fence.OpenFile(work+"/signature")) using(var committed=fence.OpenFile(work+"/committed")) {
                        if(Encoding.ASCII.GetString(UpdateTicket.Read(committed,128))!=UpdateTrust.Hash(bytes)+"\n") throw Bad();
                        var desc=UpdateTicket.Read(descriptor,UpdateTrust.MaxDescriptorBytes);var sig=UpdateTicket.Read(signature,1024);
                        var verified=HelperDescriptor.Verify(trust,desc,DetachedSignature.Parse(sig),image);
                        if(verified.Protocol<4 || lines[4]!="descriptor="+verified.DescriptorHash || UpdateTrust.Hash(receipt)!=UpdateTrust.Hash(ReceiptBytes(fence,verified,actual[2].Substring(9))) || descriptorHash!=null && descriptorHash!=verified.DescriptorHash) throw Bad();
                        descriptorHash=verified.DescriptorHash;
                        var evidence="BetterAstralParty.RemovedHelperEvidence/v1\n"+work+"/plan="+plan.Hash()+"\n"+work+"/descriptor="+descriptor.Hash()+"\n"+work+"/signature="+signature.Hash()+"\n"+work+"/committed="+committed.Hash()+"\n";
                        if(selected==null && (expectedEvidence==null || UpdateTrust.Hash(Encoding.ASCII.GetBytes(evidence))==expectedEvidence))
                            selected=new CopyProof {Evidence=evidence,Descriptor=desc,Signature=sig,Verified=verified};
                    }
                }
            }
            return selected??throw Bad();
        }
        internal sealed class OrdinaryBinding {
            internal string JournalRoot="",JournalRootId="",JournalId="",JournalHash="",ImageHash="",ReceiptHash="";
            internal string Text(WindowsFileFence f) {return "root="+f.RootIdentity.Text+"\njournal-root="+JournalRoot+"\njournal-root-id="+JournalRootId+"\njournal-id="+JournalId+"\njournal-sha="+JournalHash+"\nbackup-image="+ImageHash+"\nbackup-receipt="+ReceiptHash+"\n";}
        }
        private sealed class OrdinaryRepair {
            internal string Work="",WorkId="",Binding="",Prior="",ImageId="",ReceiptId="";
            internal string Text() {return "BetterAstralParty.OrdinaryHelperRepair/v1\n"+Binding+"work="+Work+"\nwork-id="+WorkId+"\nprior-proof="+Prior+"\nimage-id="+ImageId+"\nreceipt-id="+ReceiptId+"\n";}
            internal static OrdinaryRepair Parse(byte[] bytes) {
                foreach(var b in bytes)if(b>127 || b==13)throw RepairBad("OrdinaryHelperRepairMalformed");
                var l=Encoding.ASCII.GetString(bytes).Split('\n');
                var keys=new[]{"root=","journal-root=","journal-root-id=","journal-id=","journal-sha=","backup-image=","backup-receipt=","work=","work-id=","prior-proof=","image-id=","receipt-id="};
                if(l.Length!=14 || l[0]!="BetterAstralParty.OrdinaryHelperRepair/v1" || l[13]!="")throw RepairBad("OrdinaryHelperRepairMalformed");
                for(var i=0;i<keys.Length;i++) {
                    if(!l[i+1].StartsWith(keys[i],StringComparison.Ordinal))throw RepairBad("OrdinaryHelperRepairMalformed");
                    var v=l[i+1].Substring(keys[i].Length);var pattern=i==7?@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helper-[0-9a-f]{32}\z":i==1 || i==4 || i==5 || i==6 || i==9?@"\A[0-9A-F]{64}\z":@"\A[0-9A-F]{8}:[0-9A-F]{16}\z";
                    if(!Regex.IsMatch(v,pattern))throw RepairBad("OrdinaryHelperRepairMalformed");
                }
                var p=new OrdinaryRepair {Binding=string.Join("\n",l,1,7)+"\n",Work=l[8].Substring(5),WorkId=l[9].Substring(8),Prior=l[10].Substring(12),ImageId=l[11].Substring(9),ReceiptId=l[12].Substring(11)};
                if(UpdateTrust.Hash(bytes)!=UpdateTrust.Hash(Encoding.ASCII.GetBytes(p.Text())))throw RepairBad("OrdinaryHelperRepairMalformed");return p;
            }
        }
        private static ApplySafetyException RepairBad(string reason) {var e=Bad();e.Data["BapRecoveryReason"]=reason;return e;}
        private static string RepairPlan(WindowsFileFence f,OrdinaryRepair p,HelperDescriptor d) {return "BetterAstralParty.HelperInstall/v1\nroot="+f.RootIdentity.Text+"\nwork="+p.Work+"\nwork-id="+p.WorkId+"\ndescriptor="+d.DescriptorHash+"\nimage-id="+p.ImageId+"\nreceipt-id="+p.ReceiptId+"\n";}
        private static void CheckRepairFiles(WindowsFileFence f,OrdinaryRepair p,CopyProof proof) {
            var plan=Encoding.ASCII.GetBytes(RepairPlan(f,p,proof.Verified));
            var expected=new Dictionary<string,byte[]>(StringComparer.Ordinal) {{"ordinary-repair",Encoding.ASCII.GetBytes(p.Text())},{"descriptor",proof.Descriptor},{"signature",proof.Signature},{"plan",plan},{"committed",Encoding.ASCII.GetBytes(UpdateTrust.Hash(plan)+"\n")}};
            f.AssertPrivateDirectory(p.Work);
            foreach(var path in Directory.EnumerateFileSystemEntries(WindowsPath.ForIo(f.Full(p.Work)),"*",SearchOption.TopDirectoryOnly)) {
                var leaf=Path.GetFileName(path);var canonical=leaf;
                var pending=Regex.Match(leaf,@"\A([a-z-]+)-pending-[0-9a-f]{32}\z");if(pending.Success)canonical=pending.Groups[1].Value;
                byte[] bytes;if(!expected.TryGetValue(canonical,out bytes!))throw RepairBad("OrdinaryHelperRepairProofChanged");
                using(var file=f.OpenFile(p.Work+"/"+leaf))if(file.Stream.Length!=bytes.Length || file.Hash()!=UpdateTrust.Hash(bytes))throw RepairBad("OrdinaryHelperRepairProofChanged");
            }
        }
        private static void EnsureProof(WindowsFileFence f,string path,byte[] bytes,Action<string>? boundary) {
            using(var file=Maybe(f,path)) {if(file!=null) {if(file.Hash()!=UpdateTrust.Hash(bytes))throw RepairBad("OrdinaryHelperRepairProofChanged");return;}}
            boundary?.Invoke("before:ordinary-helper-"+Path.GetFileName(path));
            var slash=path.LastIndexOf('/');var work=path.Substring(0,slash);var leaf=path.Substring(slash+1);var candidates=new List<string>();
            f.AssertPrivateDirectory(work);
            foreach(var entry in Directory.EnumerateFiles(WindowsPath.ForIo(f.Full(work)),leaf+"-pending-*",SearchOption.TopDirectoryOnly)) {
                var name=Path.GetFileName(entry);if(!Regex.IsMatch(name,@"\A"+Regex.Escape(leaf)+@"-pending-[0-9a-f]{32}\z"))throw RepairBad("OrdinaryHelperRepairProofChanged");
                using(var file=f.OpenFile(work+"/"+name))if(file.Stream.Length!=bytes.Length || file.Hash()!=UpdateTrust.Hash(bytes))throw RepairBad("OrdinaryHelperRepairProofChanged");
                candidates.Add(name);
            }
            candidates.Sort(StringComparer.Ordinal);
            if(candidates.Count==0)Write(f,path,bytes);
            else using(var file=f.OpenFile(work+"/"+candidates[0],true)) {
                if(file.Stream.Length!=bytes.Length || file.Hash()!=UpdateTrust.Hash(bytes))throw RepairBad("OrdinaryHelperRepairProofChanged");
                file.RenameTo(f,path);file.Flush();
            }
            boundary?.Invoke("after:ordinary-helper-"+Path.GetFileName(path));
        }
        private static OrdinaryRepair? FindOrdinaryRepair(WindowsFileFence f,OrdinaryBinding binding,byte[] originalImage,byte[] originalReceipt,UpdateTrust trust,WindowsFileFence.FileLease? image,WindowsFileFence.FileLease? receipt) {
            OrdinaryRepair? selected=null;
            foreach(var dir in UpdateWorkPath.Directories(f, "helper")) {
                var work=dir;f.AssertPrivateDirectory(work);
                using(var marker=Maybe(f,work+"/ordinary-repair")) {
                    if(marker==null)continue;var p=OrdinaryRepair.Parse(UpdateTicket.Read(marker,4096));
                    if(p.Work!=work || p.WorkId!=f.DirectoryIdentity(work).Text || !p.Binding.StartsWith("root="+f.RootIdentity.Text+"\n",StringComparison.Ordinal))throw RepairBad("OrdinaryHelperRepairMalformed");
                    if(p.Binding!=binding.Text(f)) {
                        using(var committed=Maybe(f,work+"/committed"))if(committed==null)throw RepairBad("OrdinaryHelperRepairJournalChanged");
                        continue;
                    }
                    if(selected!=null || image==null || receipt==null || image.Identity.Text!=p.ImageId || receipt.Identity.Text!=p.ReceiptId)throw RepairBad("OrdinaryHelperRepairObjectChanged");
                    RemovedProof(f,trust,originalImage,originalReceipt,p.Prior,p.Work);selected=p;
                }
            }
            return selected;
        }
        // Only the exact ordinary journal may finish this helper provenance repair.
        // The original signed copies and every prior delivery record remain unchanged.
        internal static string? ValidateOrdinaryRepair(WindowsFileFence f,OrdinaryBinding binding,byte[] originalImage,byte[] originalReceipt,UpdateTrust trust,WindowsFileFence.FileLease? image,WindowsFileFence.FileLease? receipt) {
            if(UpdateTrust.Hash(originalImage)!=binding.ImageHash || UpdateTrust.Hash(originalReceipt)!=binding.ReceiptHash)throw RepairBad("OrdinaryHelperRepairProofChanged");
            if(PendingInstall(f))foreach(var dir in UpdateWorkPath.Directories(f, "helper")) {
                var work=dir;
                using(var plan=Maybe(f,work+"/plan"))using(var committed=Maybe(f,work+"/committed")) {
                    if(plan==null || committed!=null)continue;
                    using(var marker=Maybe(f,work+"/ordinary-repair")) {
                        if(marker==null)throw RepairBad("OrdinaryHelperRepairProofChanged");
                        var pendingRepair=OrdinaryRepair.Parse(UpdateTicket.Read(marker,4096));if(pendingRepair.Binding!=binding.Text(f))throw RepairBad("OrdinaryHelperRepairJournalChanged");
                    }
                }
            }
            var p=FindOrdinaryRepair(f,binding,originalImage,originalReceipt,trust,image,receipt);if(p==null)return null;
            var proof=RemovedProof(f,trust,originalImage,originalReceipt,p.Prior,p.Work);
            using(var plan=Maybe(f,p.Work+"/plan"))using(var committed=Maybe(f,p.Work+"/committed")) {
                if(plan==null) {if(committed!=null)throw RepairBad("OrdinaryHelperRepairProofChanged");}
                else {
                    if(plan.Hash()!=UpdateTrust.Hash(Encoding.ASCII.GetBytes(RepairPlan(f,p,proof.Verified))))throw RepairBad("OrdinaryHelperRepairProofChanged");
                    using(var desc=f.OpenFile(p.Work+"/descriptor"))using(var sig=f.OpenFile(p.Work+"/signature")) {
                        if(desc.Hash()!=UpdateTrust.Hash(proof.Descriptor) || sig.Hash()!=UpdateTrust.Hash(proof.Signature))throw RepairBad("OrdinaryHelperRepairProofChanged");
                    }
                    if(committed!=null && Encoding.ASCII.GetString(UpdateTicket.Read(committed,128))!=plan.Hash()+"\n")throw RepairBad("OrdinaryHelperRepairProofChanged");
                }
            }
            return p.Work;
        }
        internal static string OrdinaryReceiptHash(WindowsFileFence f,byte[] originalImage,byte[] originalReceipt,string imageId,UpdateTrust trust,string? repairWork=null) {
            try {return UpdateTrust.Hash(ReceiptBytes(f,RemovedProof(f,trust,originalImage,originalReceipt,exclude:repairWork).Verified,imageId));}
            catch(Exception e) {if(!e.Data.Contains("BapRecoveryReason"))e.Data["BapRecoveryReason"]="OrdinaryHelperRepairProofChanged";throw;}
        }
        internal static string RestoreOrdinary(WindowsFileFence f,OrdinaryBinding binding,byte[] originalImage,byte[] originalReceipt,WindowsFileFence.FileLease image,WindowsFileFence.FileLease receipt,UpdateTrust trust,Action<string>? boundary) {
            var operation=default(DiagnosticOperation);
            try {
                if(image.Hash()!=binding.ImageHash || UpdateTrust.Hash(originalImage)!=binding.ImageHash || UpdateTrust.Hash(originalReceipt)!=binding.ReceiptHash)throw RepairBad("OrdinaryHelperRepairProofChanged");
                var p=FindOrdinaryRepair(f,binding,originalImage,originalReceipt,trust,image,receipt);
                var proof=RemovedProof(f,trust,originalImage,originalReceipt,p?.Prior,p?.Work);
                var canonical=ReceiptBytes(f,proof.Verified,image.Identity.Text);var canonicalHash=UpdateTrust.Hash(canonical);
                if(p!=null && !UpdateWorkPath.IsContained(p.Work,"helper")) {
                    if(receipt.Hash()==canonicalHash && AuthenticatedPair(f,trust,image,receipt).DescriptorHash==proof.Verified.DescriptorHash)return canonicalHash;
                    UpdateWorkPath.RequireContained(p.Work,"helper");
                }
                if(p==null && receipt.Hash()==canonicalHash) {
                    try {if(AuthenticatedPair(f,trust,image,receipt).DescriptorHash==proof.Verified.DescriptorHash)return canonicalHash;}
                    catch(ApplySafetyException e)when(e.Failure==ApplyFailure.RecoveryRequired) { }
                }
                if(p==null) {
                    var prior=UpdateTrust.Hash(Encoding.ASCII.GetBytes(proof.Evidence));
                    var tuple=binding.Text(f)+"prior-proof="+prior+"\nimage-id="+image.Identity.Text+"\nreceipt-id="+receipt.Identity.Text+"\n";
                    var work=UpdateWorkPath.At("helper",UpdateTrust.Hash(Encoding.ASCII.GetBytes(tuple)).Substring(0,32).ToLowerInvariant());
                    operation=DiagnosticOperation.VerifiedGuid(UpdateWorkPath.Operation(work,"helper"));
                    string workId;try {workId=f.DirectoryIdentity(work).Text;f.AssertPrivateDirectory(work);}
                    catch(ApplySafetyException e)when(e.NativeError==2 || e.NativeError==3){workId=UpdateWorkPath.Create(f, work).Text;}
                    p=new OrdinaryRepair {Work=work,WorkId=workId,Binding=binding.Text(f),Prior=prior,ImageId=image.Identity.Text,ReceiptId=receipt.Identity.Text};
                }
                operation=DiagnosticOperation.VerifiedGuid(UpdateWorkPath.Operation(p.Work,"helper"));
                DiagnosticHub.Stage(DiagnosticFeature.Installer,DiagnosticPhase.Recovery,DiagnosticOutcome.Begin,operation,proof.Verified.Version);
                CheckRepairFiles(f,p,proof);
                EnsureProof(f,p.Work+"/ordinary-repair",Encoding.ASCII.GetBytes(p.Text()),boundary);
                EnsureProof(f,p.Work+"/descriptor",proof.Descriptor,boundary);EnsureProof(f,p.Work+"/signature",proof.Signature,boundary);
                var plan=Encoding.ASCII.GetBytes(RepairPlan(f,p,proof.Verified));EnsureProof(f,p.Work+"/plan",plan,boundary);
                if(receipt.Hash()!=canonicalHash) {
                    boundary?.Invoke("before:ordinary-helper-receipt");receipt.Recheck();receipt.Stream.Position=0;
                    receipt.Stream.Write(canonical,0,canonical.Length);receipt.Stream.SetLength(canonical.Length);receipt.Flush();boundary?.Invoke("after:ordinary-helper-receipt");
                }
                if(image.Hash()!=binding.ImageHash || receipt.Hash()!=canonicalHash)throw RepairBad("OrdinaryHelperRepairObjectChanged");
                EnsureProof(f,p.Work+"/committed",Encoding.ASCII.GetBytes(UpdateTrust.Hash(plan)+"\n"),boundary);
                if(AuthenticatedPair(f,trust,image,receipt).DescriptorHash!=proof.Verified.DescriptorHash)throw RepairBad("OrdinaryHelperRepairProofChanged");
                DiagnosticHub.Stage(DiagnosticFeature.Installer,DiagnosticPhase.Recovery,DiagnosticOutcome.Completed,operation,proof.Verified.Version,DiagnosticCode.Recovered);return canonicalHash;
            } catch(Exception e) {
                if(!e.Data.Contains("BapRecoveryReason"))e.Data["BapRecoveryReason"]="OrdinaryHelperRepairFailed";
                var safety=e as ApplySafetyException;var validation=e as UpdateValidationException;
                DiagnosticHub.Failure(DiagnosticFeature.Installer,DiagnosticPhase.Recovery,safety?.Failure==ApplyFailure.FileBusy?DiagnosticCode.FilesBusy:DiagnosticCode.RollbackIncomplete,e,safety?.NativeError??0,validation!=null?(int)validation.Failure:0,operation,safety!=null?(int)safety.Failure:0);DiagnosticHub.FlushSoon();throw;
            }
        }
        internal static bool PendingInstall(WindowsFileFence fence) {
            var active=0;var pending=false;
            var dirs=UpdateWorkPath.Directories(fence, "helper");
            foreach(var dir in dirs) {
                var work=dir;if(!Regex.IsMatch(work,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helper-[0-9a-f]{32}\z")) throw Bad();fence.AssertPrivateDirectory(work);
                using(var plan=Maybe(fence,work+"/plan"))using(var committed=Maybe(fence,work+"/committed")) {
                    if(plan==null){UpdateWorkPath.RequireContained(work,"helper");if(committed!=null || ++active>64)throw Bad();continue;}
                    var bytes=UpdateTicket.Read(plan,2048);foreach(var value in bytes)if(value>127 || value==13)throw Bad();
                    var lines=Encoding.ASCII.GetString(bytes).Split('\n');
                    if(lines.Length!=8 || lines[0]!="BetterAstralParty.HelperInstall/v1" || lines[1]!="root="+fence.RootIdentity.Text
                        || lines[2]!="work="+work || lines[3]!="work-id="+fence.DirectoryIdentity(work).Text
                        || !Regex.IsMatch(lines[4],@"\Adescriptor=[0-9A-F]{64}\z") || !Regex.IsMatch(lines[5],@"\Aimage-id=[0-9A-F]{8}:[0-9A-F]{16}\z")
                        || !Regex.IsMatch(lines[6],@"\Areceipt-id=[0-9A-F]{8}:[0-9A-F]{16}\z") || lines[7]!="")throw Bad();
                    if(committed==null){UpdateWorkPath.RequireContained(work,"helper");if(++active>64)throw Bad();pending=true;}
                    else if(Encoding.ASCII.GetString(UpdateTicket.Read(committed,128))!=UpdateTrust.Hash(bytes)+"\n")throw Bad();
                }
            }
            return pending;
        }
        internal static HelperInstallResult Install(WindowsFileFence fence,byte[] descriptor,byte[] envelopeBytes,byte[] image,UpdateTrust trust,Action<string>? boundary=null) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            var envelope=DetachedSignature.Parse(envelopeBytes);var verified=HelperDescriptor.Verify(trust,descriptor,envelope,image);
            using(var operation=Lock(fence)) using(var processes=WindowsProcessGuard.Acquire(fence,new[]{"AstralParty_INT.exe","BetterAstralParty-Launcher.exe"})) {
                UpdateLaunchGate.AssertNoReinstall(fence);
                WindowsProcessGuard.AssertNoRunning(new[]{fence.Full(HelperDescriptor.Image)});
                if(!Eligible(fence,processes)) return HelperInstallResult.ManualUpgradeRequired;
                if(UpdateFaults.Read(fence)!=null) return HelperInstallResult.RecoveryRequired;
                foreach(var item in UpdateRecovery.Scan(fence)) if(item.Pending) return HelperInstallResult.RecoveryRequired;
                using(var plugin=fence.OpenFile(UpdateTrust.PluginPath)) if(UpdateVersion.Parse(verified.Version).CompareTo(UpdateVersion.Parse(InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes)).Version))<0) throw new UpdateValidationException(UpdateFailure.NotNewer);
                if(PendingInstall(fence) || WholeInstallBridge.Pending(fence)) return HelperInstallResult.RecoveryRequired;
                var alreadyInstalled=false;
                using(var existing=Maybe(fence,HelperDescriptor.Image)) using(var receipt=Maybe(fence,Receipt)) {
                    if(existing!=null || receipt!=null) {
                        if(existing!=null && receipt!=null && existing.Hash()==verified.Sha256 && UpdateTrust.Hash(UpdateTicket.Read(receipt,1024))==UpdateTrust.Hash(ReceiptBytes(fence,verified,existing.Identity.Text))) alreadyInstalled=true;
                        else return HelperInstallResult.ManualUpgradeRequired;
                    }
                }
                if(alreadyInstalled) return Fingerprint(fence,trust)==verified.Sha256 ? HelperInstallResult.AlreadyInstalled : HelperInstallResult.ManualUpgradeRequired;
                var work=UpdateWorkPath.New("helper");var workId=UpdateWorkPath.Create(fence, work);
                Write(fence,work+"/descriptor",descriptor);Write(fence,work+"/signature",envelopeBytes);Write(fence,work+"/image",image);
                string imageId;using(var source=fence.OpenFile(work+"/image")) imageId=source.Identity.Text;
                Write(fence,work+"/receipt",ReceiptBytes(fence,verified,imageId));
                string receiptId;using(var source=fence.OpenFile(work+"/receipt")) receiptId=source.Identity.Text;
                var plan=Encoding.ASCII.GetBytes("BetterAstralParty.HelperInstall/v1\nroot="+fence.RootIdentity.Text+"\nwork="+work+"\nwork-id="+workId.Text+"\ndescriptor="+verified.DescriptorHash+"\nimage-id="+imageId+"\nreceipt-id="+receiptId+"\n");
                Write(fence,work+"/plan",plan); boundary?.Invoke("prepared");
                Finish(fence,work,trust,boundary);return HelperInstallResult.Installed;
            }
        }
        internal static HelperInstallResult Recover(WindowsFileFence fence,string work,UpdateTrust trust,Action<string>? boundary=null) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            using(var operation=Lock(fence)) using(var processes=WindowsProcessGuard.Acquire(fence,new[]{"AstralParty_INT.exe","BetterAstralParty-Launcher.exe"})) {
                UpdateLaunchGate.AssertNoReinstall(fence);
                WindowsProcessGuard.AssertNoRunning(new[]{fence.Full(HelperDescriptor.Image)});
                if(!Eligible(fence,processes)) return HelperInstallResult.ManualUpgradeRequired;
                if(WholeInstallBridge.Pending(fence)) return HelperInstallResult.RecoveryRequired;
                Finish(fence,work,trust,boundary);return HelperInstallResult.Installed;
            }
        }
        internal static byte[] WholeReceiptBytes(WindowsFileFence fence,HelperDescriptor descriptor,string imageId) {return ReceiptBytes(fence,descriptor,imageId);}
        internal static void FinishWholeMigration(WindowsFileFence fence,string work,UpdateTrust trust,Action<string>? boundary) {Finish(fence,work,trust,boundary);}
        private static void Finish(WindowsFileFence fence,string work,UpdateTrust trust,Action<string>? boundary) {
            UpdateWorkPath.RequireContained(work,"helper");
            if(!Regex.IsMatch(work,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-helper-[0-9a-f]{32}\z")) throw Bad();
            fence.AssertPrivateDirectory(work);byte[] plan,desc,sig;
            using(var file=fence.OpenFile(work+"/plan")) plan=UpdateTicket.Read(file,2048);
            var lines=Encoding.ASCII.GetString(plan).Split('\n');
            if(lines.Length!=8 || lines[0]!="BetterAstralParty.HelperInstall/v1" || lines[1]!="root="+fence.RootIdentity.Text || lines[2]!="work="+work || lines[3]!="work-id="+fence.DirectoryIdentity(work).Text || !Regex.IsMatch(lines[4],@"\Adescriptor=[0-9A-F]{64}\z") || !Regex.IsMatch(lines[5],@"\Aimage-id=[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(lines[6],@"\Areceipt-id=[0-9A-F]{8}:[0-9A-F]{16}\z") || lines[7]!="") throw Bad();
            using(var file=fence.OpenFile(work+"/descriptor")) desc=UpdateTicket.Read(file,UpdateTrust.MaxDescriptorBytes);
            using(var file=fence.OpenFile(work+"/signature")) sig=UpdateTicket.Read(file,1024);
            using(var target=Maybe(fence,HelperDescriptor.Image,true)) using(var source=Maybe(fence,work+"/image",true)) {
                var image=target??source??throw Bad();if(target!=null && source!=null || image.Identity.Text!=lines[5].Substring(9)) throw Bad();
                var verified=HelperDescriptor.Verify(trust,desc,DetachedSignature.Parse(sig),UpdateTicket.Read(image,UpdateTrust.MaxFileBytes));
                if(lines[4]!="descriptor="+verified.DescriptorHash) throw Bad();
                if(target==null) { boundary?.Invoke("before:image");image.RenameTo(fence,HelperDescriptor.Image);image.Flush();boundary?.Invoke("after:image"); }
                using(var installed=Maybe(fence,Receipt,true)) using(var staged=Maybe(fence,work+"/receipt",true)) {
                    var receipt=installed??staged??throw Bad();if(installed!=null && staged!=null || receipt.Identity.Text!=lines[6].Substring(11) || UpdateTrust.Hash(UpdateTicket.Read(receipt,1024))!=UpdateTrust.Hash(ReceiptBytes(fence,verified,image.Identity.Text))) throw Bad();
                    if(installed==null) {boundary?.Invoke("before:receipt");receipt.RenameTo(fence,Receipt);receipt.Flush();boundary?.Invoke("after:receipt");}
                }
            }
            using(var marker=Maybe(fence,work+"/committed")) {if(marker!=null) {if(Encoding.ASCII.GetString(UpdateTicket.Read(marker,128))!=UpdateTrust.Hash(plan)+"\n") throw Bad();return;}}
            boundary?.Invoke("before:committed");Write(fence,work+"/committed",Encoding.ASCII.GetBytes(UpdateTrust.Hash(plan)+"\n"));boundary?.Invoke("after:committed");
        }
    }
}
