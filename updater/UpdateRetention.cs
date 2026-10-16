#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Observability;
namespace BetterAstralParty.Updating {
    internal sealed class RetentionResult {internal int RemovedPackages, AlreadyClean, Preserved;}
    // Never removes transaction plans, backups, helper installation journals, receipts or user files.
    internal static class UpdateRetention {
        internal static bool CancelUnclaimed(WindowsFileFence fence, UpdateTicket ticket, UpdateTrust trust, UpdateLaunchGate.Lease? held=null, UpdateFault? unstarted=null) {
            UpdateWorkPath.RequireContained(ticket.Stage,"stage");
            WindowsFileFence.FileLease? operation=null;
            try {
                if(held!=null) held.Recheck(fence);
                else {
                    try { operation=fence.OpenFile("BetterAstralParty.update.lock",true); }
                    catch(ApplySafetyException error) when(error.NativeError==2) { operation=fence.OpenFile("BetterAstralParty.update.lock",true,true); }
                }
                using(operation) using(var file=fence.OpenFile(ticket.Stage+"/handoff.ticket",true)) {
                    ticket.Recheck(fence); var bytes=UpdateTicket.Read(file,UpdateTicket.MaxBytes);var hash=UpdateTrust.Hash(bytes);
                    if(hash!=UpdateTrust.Hash(ticket.Bytes())) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                    var active=UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath);
                    if(active!=null) {
                        if(unstarted==null) return false;
                        HelperRecoveryBinding.RequireOwner(fence,ticket,active);
                        if(UpdateTrust.Hash(active.Bytes())!=UpdateTrust.Hash(unstarted.Bytes())) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                    } else if(unstarted!=null) return false;
                    try { using(var cancelled=fence.OpenFile(ticket.Stage+"/helper.cancel"))
                        if(Encoding.ASCII.GetString(UpdateTicket.Read(cancelled,128))!=hash+"\n") throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                    } catch(ApplySafetyException error) when(error.NativeError==2) { }
                    var terminal=false;
                    try { using(var result=fence.OpenFile(ticket.Stage+"/helper.result")) {
                        terminal=Encoding.ASCII.GetString(UpdateTicket.Read(result,128))==hash+"\nCancelled\n";
                        if(!terminal) return false;
                    }
                    } catch(ApplySafetyException error) when(error.NativeError==2) { }
                    foreach(var name in new[]{"helper.claim","helper.attempt","helper.ready","helper.owner"}) {
                        try { using(var claimed=fence.OpenFile(ticket.Stage+"/"+name)) return false; }
                        catch(ApplySafetyException error) when(error.NativeError==2) { }
                    }
                    foreach(var entry in UpdateRecovery.Scan(fence)) if(entry.Pending) return false;
                    byte[] descriptor,signature,key;
                    using(var source=fence.OpenFile(ticket.Stage+"/descriptor.bin")) descriptor=UpdateTicket.Read(source,UpdateTrust.MaxDescriptorBytes);
                    using(var source=fence.OpenFile(ticket.Stage+"/signature.bin")) signature=UpdateTicket.Read(source,UpdateTrust.MaxSignatureBytes);
                    using(var source=fence.OpenFile(ticket.Stage+"/key-id.bin")) key=UpdateTicket.Read(source,40);
                    if(trust.VerifyDescriptor(descriptor,Encoding.ASCII.GetString(key),signature,ticket.Context).DescriptorSha256!=ticket.DescriptorHash)
                        throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                    file.Recheck();ticket.Recheck(fence);held?.Recheck(fence);
                    if(unstarted!=null) UpdateFaults.Complete(fence,unstarted);
                    if(!terminal) UpdateTicket.Write(fence,ticket.Stage+"/helper.result",Encoding.ASCII.GetBytes(hash+"\nCancelled\n"));
                    return true;
                }
            } catch(ApplySafetyException error) when(error.NativeError==32 || error.NativeError==80 || error.NativeError==183) {
                DiagnosticHub.Failure(DiagnosticFeature.Coordinator,DiagnosticPhase.Recovery,DiagnosticCode.StorageUnavailable,error,
                    operation:unstarted==null ? default : DiagnosticOperation.VerifiedGuid(unstarted.Operation),targetVersion:ticket.Context.ReleaseTag,nativeCode:error.NativeError,applyCode:(int)error.Failure);
                return false;
            }
        }
        internal static RetentionResult Clean(WindowsFileFence fence,UpdateTrust trust,Action<string>? boundary=null) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            WindowsFileFence.FileLease operation;
            try {operation=fence.OpenFile("BetterAstralParty.update.lock",true);} catch(ApplySafetyException error) when(error.NativeError==2) {operation=fence.OpenFile("BetterAstralParty.update.lock",true,true);}
            using(operation) {
                if(UpdateFaults.Read(fence)!=null) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var entries=UpdateRecovery.Scan(fence);foreach(var entry in entries) if(entry.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                // Read-only classification is streamed; each deletion still requires the
                // same ticket, terminal result, signature, package hash and native leases.
                // Retained historical evidence is not itself a recovery fault.
                var result=new RetentionResult();var dirs=UpdateWorkPath.Directories(fence, "stage");
                foreach(var dir in dirs) {
                    var stage=dir;bool used=false;foreach(var entry in entries) if(entry.Stage==stage) used=true;
                    if(!UpdateWorkPath.IsContained(stage,"stage")){result.Preserved++;continue;}
                    if(used) {result.Preserved++;continue;}
                    try { if(!Regex.IsMatch(stage,@"\A" + UpdateWorkPath.OptionalRootPattern + @"bap-v4-stage-[0-9a-f]{32}\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                        fence.AssertPrivateDirectory(stage);
                        using(var file=fence.OpenFile(stage+"/handoff.ticket")) {
                            var bytes=UpdateTicket.Read(file,2048);var ticket=UpdateTicket.Parse(bytes);ticket.Recheck(fence);if(ticket.Stage!=stage) throw new ApplySafetyException(ApplyFailure.InvalidState);
                            if(!UpdateRecovery.RetiredTicket(fence,ticket,bytes,entries,trust)) {result.Preserved++;continue;}
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
