#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace BetterAstralParty.Updating {
    internal sealed class RetentionResult {internal int RemovedPackages, AlreadyClean, Preserved;}
    // Never removes transaction plans, backups, helper installation journals, receipts or user files.
    internal static class UpdateRetention {
        internal static RetentionResult Clean(WindowsFileFence fence,UpdateTrust trust,Action<string>? boundary=null) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            WindowsFileFence.FileLease operation;
            try {operation=fence.OpenFile("BetterAstralParty.update.lock",true);} catch(ApplySafetyException error) when(error.NativeError==2) {operation=fence.OpenFile("BetterAstralParty.update.lock",true,true);}
            using(operation) {
                if(UpdateFaults.Read(fence)!=null) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var entries=UpdateRecovery.Scan(fence);foreach(var entry in entries) if(entry.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var result=new RetentionResult();var dirs=Directory.GetDirectories(fence.Root,"bap-stage-*",SearchOption.TopDirectoryOnly);
                if(dirs.Length>4096) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                foreach(var dir in dirs) {
                    var stage=Path.GetFileName(dir);bool used=false;foreach(var entry in entries) if(entry.Stage==stage) used=true;
                    if(used) {result.Preserved++;continue;}
                    try { if(!Regex.IsMatch(stage,@"\Abap-stage-[0-9a-f]{32}\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                        fence.AssertPrivateDirectory(stage);
                        using(var file=fence.OpenFile(stage+"/handoff.ticket")) {
                            var bytes=UpdateTicket.Read(file,2048);var ticket=UpdateTicket.Parse(bytes);ticket.Recheck(fence);if(ticket.Stage!=stage) throw new ApplySafetyException(ApplyFailure.InvalidState);
                            using(var completed=fence.OpenFile(stage+"/helper.result")) {
                                var lines=Encoding.ASCII.GetString(UpdateTicket.Read(completed,128)).Split('\n');
                                if(lines.Length!=3 || lines[0]!=UpdateTrust.Hash(bytes) || lines[2]!="" || (lines[1]!="Committed" && lines[1]!="Cancelled" && lines[1]!="FilesBusy" && lines[1]!="FaultDisabled" && lines[1]!="RecoveryRequired")) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                            }
                            byte[] descriptor,signature,key;
                            using(var d=fence.OpenFile(stage+"/descriptor.bin")) descriptor=UpdateTicket.Read(d,UpdateTrust.MaxDescriptorBytes);
                            using(var sig=fence.OpenFile(stage+"/signature.bin")) signature=UpdateTicket.Read(sig,UpdateTrust.MaxSignatureBytes);
                            using(var k=fence.OpenFile(stage+"/key-id.bin")) key=UpdateTicket.Read(k,40);
                            var verified=trust.VerifyDescriptor(descriptor,Encoding.ASCII.GetString(key),signature,ticket.Context);
                            if(verified.DescriptorSha256!=ticket.DescriptorHash) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                            var marker=Encoding.ASCII.GetBytes("BetterAstralParty.HandoffClean/v1\nroot="+fence.RootIdentity.Text+"\nstage="+stage+"\nstage-id="+fence.DirectoryIdentity(stage).Text+"\nticket="+UpdateTrust.Hash(bytes)+"\ndescriptor="+verified.DescriptorSha256+"\npackage="+verified.ZipSha256+"\nbytes="+verified.ZipBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\n");
                            var marked=false;
                            try {using(var proof=fence.OpenFile(stage+"/package.cleaned")) {
                                if(UpdateTrust.Hash(UpdateTicket.Read(proof,1024))!=UpdateTrust.Hash(marker)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);marked=true;
                            }} catch(ApplySafetyException error) when(error.NativeError==2) { }
                            try {using(var package=fence.OpenFile(stage+"/package.zip",true)) {
                                if(package.Stream.Length!=verified.ZipBytes || package.Hash()!=verified.ZipSha256) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                                if(!marked) UpdateTicket.Write(fence,stage+"/package.cleaned",marker);
                                boundary?.Invoke("before:delete");
                                package.RemoveTemporaryPackage();result.RemovedPackages++;
                                boundary?.Invoke("after:delete");
                            }} catch(ApplySafetyException error) when(error.NativeError==2) {if(marked) result.AlreadyClean++;else result.Preserved++;}
                        }
                    } catch(ApplySafetyException) {result.Preserved++;}
                    catch(UpdateValidationException) {result.Preserved++;}
                }
                return result;
            }
        }
    }
}
