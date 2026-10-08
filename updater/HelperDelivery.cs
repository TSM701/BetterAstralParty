#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        internal static byte[] Prepare(string version,byte[] image,int protocol=1) {
            if(protocol!=1 && protocol!=2) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            var parsed=UpdateVersion.Parse(version); if(parsed.Tag!=version || image.Length==0 || image.Length>UpdateTrust.MaxFileBytes) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            Executable(image);
            return Encoding.ASCII.GetBytes("BetterAstralParty.HelperDelivery/v"+protocol+"\nrepository=TSM701/BetterAstralParty\nversion="+version+"\nplatform="+UpdateTrust.PlatformId+"\nimage="+Image+"\nbytes="+image.Length.ToString(CultureInfo.InvariantCulture)+"\nsha256="+UpdateTrust.Hash(image)+"\n"+(protocol==2?"update-protocol=2\n":""));
        }
        internal static void Executable(byte[] image) {
            if(image.Length<512 || image[0]!=77 || image[1]!=90) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
            var offset=BitConverter.ToInt32(image,60);
            if(offset<64 || offset>image.Length-24 || image[offset]!=80 || image[offset+1]!=69 || image[offset+2]!=0 || image[offset+3]!=0 || BitConverter.ToUInt16(image,offset+4)!=0x8664 || (BitConverter.ToUInt16(image,offset+22)&0x2000)!=0) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
        }
        internal static HelperDescriptor Verify(UpdateTrust trust,byte[] descriptor,DetachedSignature envelope,byte[] image,string? expectedVersion=null) {
            var bytes=trust.VerifyDetached(descriptor,envelope.KeyId,envelope.Signature);
            var lines=Encoding.ASCII.GetString(bytes).Split('\n');
            var protocol=lines.Length==8 && lines[0]=="BetterAstralParty.HelperDelivery/v1" && lines[7]==""?1:lines.Length==9 && lines[0]=="BetterAstralParty.HelperDelivery/v2" && lines[7]=="update-protocol=2" && lines[8]==""?2:0;
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
        private static void Write(WindowsFileFence fence,string path,byte[] bytes) { using(var file=fence.OpenFile(path,true,true)) {file.Stream.Write(bytes,0,bytes.Length);file.Flush();} }
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
        internal static string? Fingerprint(WindowsFileFence fence,UpdateTrust trust,int minimumProtocol=1) {
            if(PendingInstall(fence)) throw Bad();
            return FingerprintAt(fence,trust,HelperDescriptor.Image,Receipt,minimumProtocol);
        }
        internal static string? ArchivedFingerprint(WindowsFileFence fence,UpdateTrust trust,string imagePath,string receiptPath) {
            foreach(var path in new[]{imagePath,receiptPath})
                if(path!=HelperDescriptor.Image && path!=Receipt && !Regex.IsMatch(path,@"\Abap-helperbridge-[0-9a-f]{32}/old-(?:image|receipt)\z"))throw Bad();
            return FingerprintAt(fence,trust,imagePath,receiptPath,1);
        }
        private static string? FingerprintAt(WindowsFileFence fence,UpdateTrust trust,string imagePath,string receiptPath,int minimumProtocol) {
            if(!trust.Configured) return null;
            using(var image=MaybeLaunch(fence,imagePath)) using(var receipt=Maybe(fence,receiptPath)) {
                if(image==null || receipt==null) return null;
                var actual=UpdateTicket.Read(receipt,1024);var dirs=Directory.GetDirectories(fence.Root,"bap-helper-*",SearchOption.TopDirectoryOnly);
                if(dirs.Length>64) throw Bad();
                foreach(var dir in dirs) {
                    var work=System.IO.Path.GetFileName(dir);if(!Regex.IsMatch(work,@"\Abap-helper-[0-9a-f]{32}\z")) throw Bad();
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
                        if(UpdateTrust.Hash(actual)==UpdateTrust.Hash(ReceiptBytes(fence,verified,image.Identity.Text))) return verified.Protocol>=minimumProtocol?verified.Sha256:null;
                    } catch(ApplySafetyException error) when(error.NativeError==2) { }
                }
                throw Bad();
            }
        }
        // Backup copies have new file IDs. Authenticate their original signed delivery
        // without weakening the live helper's object-identity checks in FingerprintAt.
        internal static string RemovedEvidence(WindowsFileFence fence,UpdateTrust trust,byte[] image,byte[] receipt) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            if(receipt.Length==0 || receipt.Length>1024) throw Bad();
            foreach(var value in receipt) if(value>127 || value==13) throw Bad();
            var actual=Encoding.ASCII.GetString(receipt).Split('\n');
            if(actual.Length!=8 || actual[0]!="BetterAstralParty.HelperReceipt/v1" || actual[1]!="root="+fence.RootIdentity.Text || !Regex.IsMatch(actual[2],@"\Aimage-id=[0-9A-F]{8}:[0-9A-F]{16}\z") || actual[7]!="") throw Bad();
            var dirs=Directory.GetDirectories(fence.Root,"bap-helper-*",SearchOption.TopDirectoryOnly);
            if(dirs.Length>64) throw Bad();
            string? evidence=null;
            foreach(var dir in dirs) {
                var work=Path.GetFileName(dir);if(!Regex.IsMatch(work,@"\Abap-helper-[0-9a-f]{32}\z")) throw Bad();
                fence.AssertPrivateDirectory(work);
                using(var plan=Maybe(fence,work+"/plan")) {
                    if(plan==null) continue;
                    var bytes=UpdateTicket.Read(plan,2048);var lines=Encoding.ASCII.GetString(bytes).Split('\n');
                    if(lines.Length!=8 || lines[0]!="BetterAstralParty.HelperInstall/v1" || lines[1]!=actual[1] || lines[2]!="work="+work || lines[3]!="work-id="+fence.DirectoryIdentity(work).Text || lines[4]!=actual[5] || lines[5]!=actual[2] || !Regex.IsMatch(lines[6],@"\Areceipt-id=[0-9A-F]{8}:[0-9A-F]{16}\z") || lines[7]!="") continue;
                    using(var descriptor=fence.OpenFile(work+"/descriptor")) using(var signature=fence.OpenFile(work+"/signature")) using(var committed=fence.OpenFile(work+"/committed")) {
                        if(Encoding.ASCII.GetString(UpdateTicket.Read(committed,128))!=UpdateTrust.Hash(bytes)+"\n") throw Bad();
                        var verified=HelperDescriptor.Verify(trust,UpdateTicket.Read(descriptor,UpdateTrust.MaxDescriptorBytes),DetachedSignature.Parse(UpdateTicket.Read(signature,1024)),image);
                        if(verified.Protocol<2 || lines[4]!="descriptor="+verified.DescriptorHash || UpdateTrust.Hash(receipt)!=UpdateTrust.Hash(ReceiptBytes(fence,verified,actual[2].Substring(9))) || evidence!=null) throw Bad();
                        evidence="BetterAstralParty.RemovedHelperEvidence/v1\n"+work+"/plan="+plan.Hash()+"\n"+work+"/descriptor="+descriptor.Hash()+"\n"+work+"/signature="+signature.Hash()+"\n"+work+"/committed="+committed.Hash()+"\n";
                    }
                }
            }
            return evidence??throw Bad();
        }
        internal static bool PendingInstall(WindowsFileFence fence) {
            var dirs=Directory.GetDirectories(fence.Root,"bap-helper-*",SearchOption.TopDirectoryOnly);if(dirs.Length>64) throw Bad();
            foreach(var dir in dirs) {
                var work=System.IO.Path.GetFileName(dir);if(!Regex.IsMatch(work,@"\Abap-helper-[0-9a-f]{32}\z")) throw Bad();fence.AssertPrivateDirectory(work);
                using(var plan=Maybe(fence,work+"/plan")) if(plan!=null) using(var committed=Maybe(fence,work+"/committed")) {
                    if(committed==null) return true;
                    if(Encoding.ASCII.GetString(UpdateTicket.Read(committed,128))!=UpdateTrust.Hash(UpdateTicket.Read(plan,2048))+"\n") throw Bad();
                }
            }
            return false;
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
                var work="bap-helper-"+Guid.NewGuid().ToString("N");var workId=fence.CreatePrivateDirectory(work);
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
            if(!Regex.IsMatch(work,@"\Abap-helper-[0-9a-f]{32}\z")) throw Bad();
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
