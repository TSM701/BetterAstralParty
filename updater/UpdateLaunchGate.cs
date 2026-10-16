#nullable enable
using System;
namespace BetterAstralParty.Updating
{
    internal static class UpdateLaunchGate
    {
        internal static void AssertNoReinstall(WindowsFileFence fence) {
            try { using(var pending=fence.OpenFile("BetterAstralParty.reinstall-v4.pending")) throw new ApplySafetyException(ApplyFailure.RecoveryRequired); }
            catch(ApplySafetyException error) when(error.NativeError==2) { }
        }
        internal static void Check(WindowsFileFence fence) {
            Check(fence,false);
        }
        internal static void CheckHelperRecovery(WindowsFileFence fence) {
            Check(fence,true);
        }
        private static void Check(WindowsFileFence fence,bool helperRecovery) {
            AssertNoReinstall(fence);
            UpdateFaults.Read(fence); // Corrupt fault/guard evidence is unsafe; valid retired failures remain visible in-game.
            var helperPending=AutomaticHelperUpgrade.Pending(fence);var deliveryPending=HelperDelivery.PendingInstall(fence);
            if(UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath)!=null || !helperRecovery && (helperPending || deliveryPending) || WholeInstallBridge.Pending(fence)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            foreach(var entry in UpdateRecovery.Scan(fence)) if(entry.Pending) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
        }
        internal sealed class Lease : IDisposable {
            private readonly WindowsFileFence _fence;
            private WindowsFileFence.FileLease? _lock;
            internal Lease(string root,Action<WindowsFileFence>? beforeCheck=null) {
                _fence=new WindowsFileFence(root);
                try {
                    try {_lock=_fence.OpenFile("BetterAstralParty.update.lock",true);}
                    catch(ApplySafetyException error) when(error.NativeError==2) {_lock=_fence.OpenFile("BetterAstralParty.update.lock",true,true);}
                    beforeCheck?.Invoke(_fence);
                    Check(_fence);
                } catch {Dispose();throw;}
            }
            public void Dispose() {_lock?.Dispose();_lock=null;_fence.Dispose();}
            internal void Recheck(WindowsFileFence fence) {
                if(_lock==null)throw new ApplySafetyException(ApplyFailure.InvalidState);
                _lock.Recheck();
                if(!_fence.RootIdentity.Same(fence.RootIdentity))throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                Check(fence);
            }
        }
    }
}
