#nullable enable
using System;
using System.Text;
namespace BetterAstralParty.Updating {
    internal sealed class RetentionResult {internal int RemovedPackages, AlreadyClean, Preserved;}
    // Never removes transaction plans, backups, signed descriptors, receipts or user files.
    // Only the package ZIP of a finished transaction stage is removed, after its signed hash matches.
    internal static class UpdateRetention {
        internal static RetentionResult Clean(WindowsFileFence fence,UpdateTrust trust,Action<string>? boundary=null) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            WindowsFileFence.FileLease operation;
            try {operation=fence.OpenFile("BetterAstralParty.update.lock",true);} catch(ApplySafetyException error) when(error.NativeError==2) {operation=fence.OpenFile("BetterAstralParty.update.lock",true,true);}
            using(operation) {
                if(UpdateFaults.Read(fence)!=null) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var entries=UpdateRecovery.Scan(fence);foreach(var entry in entries) if(entry.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                var result=new RetentionResult();
                foreach(var entry in entries) {
                    try {
                        fence.AssertPrivateDirectory(entry.Stage);
                        var context=UpdateRecovery.RecoveryContext(fence,entry,trust);
                        byte[] descriptor,signature,key;
                        using(var d=fence.OpenFile(entry.Stage+"/descriptor.bin")) descriptor=UpdateTicket.Read(d,UpdateTrust.MaxDescriptorBytes);
                        using(var s=fence.OpenFile(entry.Stage+"/signature.bin")) signature=UpdateTicket.Read(s,UpdateTrust.MaxSignatureBytes);
                        using(var k=fence.OpenFile(entry.Stage+"/key-id.bin")) key=UpdateTicket.Read(k,40);
                        var verified=trust.VerifyProposal(descriptor,Encoding.ASCII.GetString(key),signature,context);
                        if(verified.DescriptorSha256!=entry.Descriptor) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                        using(var package=fence.OpenFile(entry.Stage+"/package.zip",true)) {
                            if(package.Stream.Length!=verified.ZipBytes || package.Hash()!=verified.ZipSha256) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                            boundary?.Invoke("before:delete");
                            package.RemoveTemporaryPackage();result.RemovedPackages++;
                            boundary?.Invoke("after:delete");
                        }
                    } catch(ApplySafetyException error) when(error.NativeError==2) {result.AlreadyClean++;}
                    catch(ApplySafetyException) {result.Preserved++;}
                    catch(UpdateValidationException) {result.Preserved++;}
                }
                return result;
            }
        }
    }
}
