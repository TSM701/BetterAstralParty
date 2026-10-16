using BepInEx.Configuration;
using BetterAstralParty;
using BetterAstralParty.Updating;
using System.Diagnostics;
using System.Text;
internal static partial class HelperFixture
{
    private sealed class DelayedAck(IUpdateSafety actual) : IUpdateSafety {
        internal readonly TaskCompletionSource Ready=new(TaskCreationOptions.RunContinuationsAsynchronously),Allow=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<UpdateSafetySnapshot> Probe()=>actual.Probe(); public Task<UpdateFault> Begin(UpdateContext c)=>actual.Begin(c); public Task Complete(UpdateFault? o)=>actual.Complete(o);
        public Task<UpdateFault> Fail(UpdateContext c,FaultPhase p,FaultReason r,string? s)=>actual.Fail(c,p,r,s);public Task SavePreferences(bool d,bool a)=>actual.SavePreferences(d,a);public Task SaveFailed(UpdateFault f)=>actual.SaveFailed(f);
        public async Task Acknowledge(){Ready.TrySetResult();await Allow.Task;await actual.Acknowledge();}
    }
    private static async Task BridgePump(UpdatePreferencesBridge bridge,AutomaticUpdates model,Func<bool> done) {
        for(var i=0;i<600&&!done();i++){bridge.Poll(model);model.Tick(Now);await Task.Delay(5);}Check(done(),"production ConfigEntry bridge condition "+model.Status);
    }
    private static async Task ReviewIntegration(string baseRoot,Package package,byte[] inert,string helper) {
        foreach(var external in new[]{false,true}) {
            var root=Install(baseRoot,"r-events-"+external,package,inert);using(var fence=new WindowsFileFence(root)) UpdateFaults.Fail(fence,Context,FaultPhase.Apply,FaultReason.Apply);
            var path=Path.Combine(root,"preferences.cfg");var config=new ConfigFile(path,true);var download=config.Bind("Update","Download",true);var apply=config.Bind("Update","Apply",true);
            using var bridge=new UpdatePreferencesBridge(config,download,apply);var delayed=new DelayedAck(new FileUpdateSafety(root,bridge.Save));
            using var model=new AutomaticUpdates(UpdateDownloads.Production(channel: ReleaseChannel.Beta),new Handoff(),delayed);model.Configure(null);
            await BridgePump(bridge,model,()=>model.CanReactivate&&!download.Value&&!apply.Value);
            download.Value=apply.Value=true;await BridgePump(bridge,model,()=>!download.Value&&!apply.Value);Check(!model.AutoDownload,"external ON writer repaired through real SettingChanged events");
            await BridgePump(bridge,model,()=>model.CanReactivate);model.Reactivate();await delayed.Ready.Task;
            for(var i=0;i<10;i++){bridge.Poll(model);model.Tick(Now);}Check(download.Value&&apply.Value&&model.Status==AutomaticUpdateStatus.Reactivating,"internal ON events never schedule OFF during acknowledgement");
            if(external){download.Value=false;apply.Value=false;bridge.Poll(model);Check(model.Status==AutomaticUpdateStatus.Reactivating,"external OFF event deferred while activation pending");}
            delayed.Allow.TrySetResult();await BridgePump(bridge,model,()=>model.Status==AutomaticUpdateStatus.NotConfigured);
            for(var i=0;i<30;i++){bridge.Poll(model);model.Tick(Now);await Task.Delay(2);}
            Check(model.AutoDownload==!external&&model.ApplyAfterExit==!external&&download.Value==!external&&apply.Value==!external,"activation/late external writer runtime and disk agree");
            var reload=new ConfigFile(path,false);Check(reload.Bind("Update","Download",true).Value==!external&&reload.Bind("Update","Apply",true).Value==!external,"restart confirms persisted production event result");
            using var verified=new WindowsFileFence(root);Check(UpdateFaults.Read(verified)==null,"activation events never recreate fault after acknowledgement");
        }
        var vroot=Install(baseRoot,"r-vtag",package,inert);var vcontext=new UpdateContext(Context.Repository,Context.Channel,Context.CurrentVersion,"v1.3.0",123,456,Context.Platform,repositoryId:Context.RepositoryId);
        var vdescriptor=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(package.Descriptor).Replace("\nversion=1.3.0\n","\nversion=v1.3.0\n"));
        using(var fence=new WindowsFileFence(vroot)) {
            Check(UpdateTransaction.Apply(fence,vdescriptor,"helper-fixture",package.Sign(vdescriptor),package.Zip,package.Trust,vcontext)==TransactionResult.Committed,"v-prefixed release actually applies normalized DLL metadata");
            Check(UpdateTransaction.Recover(fence,UpdateRecovery.Scan(fence).Single().Work,package.Trust,vcontext)==TransactionResult.Committed,"v-prefixed release core recovery metadata binding");
        }
        foreach(var boundary in new[]{"before:commit-intent","after:commit-intent"}) {
            var root=Install(baseRoot,"r-retry-"+(boundary.StartsWith("before")?"b":"a"),package,inert);string oldWork,stage;
            using(var fence=new WindowsFileFence(root)) {
                try {UpdateTransaction.Apply(fence,package.Descriptor,"helper-fixture",package.Signature,package.Zip,package.Trust,Context,p=>{if(p=="before:commit-intent")throw new Interruption();});}catch(Interruption){}
                oldWork=UpdateRecovery.Scan(fence).Single().Work;Check(UpdateTransaction.Recover(fence,oldWork,package.Trust,Context)==TransactionResult.RolledBack,"first same-descriptor attempt rolled back");
                var request=UpdateStaging.Create(fence,package.Descriptor,"helper-fixture",package.Signature,package.Zip,package.Trust,Context);stage=request.Directory;
                using var current=Start(Path.Combine(root,"AstralParty_INT.exe"),"250");
                var ticket=new UpdateTicket{RootId=fence.RootIdentity.Text,Stage=stage,StageId=request.DirectoryIdentity.Text,DescriptorHash=request.DescriptorSha256,Context=Context,GamePid=(uint)current.Id,GameCreation=current.StartTime.ToFileTimeUtc(),GameSession=(uint)current.SessionId};UpdateTicket.Write(fence,stage+"/handoff.ticket",ticket.Bytes());
                await current.WaitForExitAsync();
                UpdateFaults.Begin(fence,Context,FaultPhase.Apply,ticket);
                var work=UpdateWorkPath.New("txn");
                UpdateTicket.Write(fence,stage+"/helper.attempt",HelperRecoveryBinding.Attempt(UpdateTrust.Hash(ticket.Bytes()),work));
                try {UpdateTransaction.Apply(fence,package.Descriptor,"helper-fixture",package.Signature,package.Zip,package.Trust,Context,p=>{if(p==boundary)throw new Interruption();},attemptWork:work);}catch(Interruption){}
                Refused(()=>UpdateLaunchGate.Check(fence),"pending transaction blocks mod load/launcher gate");
            }
            var retained=Directory.GetFiles(Path.Combine(root,oldWork)).ToDictionary(p=>p,p=>UpdateTrust.Hash(File.ReadAllBytes(p)));
            for(var repeat=0;repeat<2;repeat++) {
                using var process=Start(helper,"--recover \""+root+"\" "+stage+" \""+Path.Combine(baseRoot,"fixture-public-key.bin")+"\"");await process.WaitForExitAsync();var error=await process.StandardError.ReadToEndAsync();Check(process.ExitCode==0,"CLI pending-only recovery skips historical completed journal "+boundary+" "+error);
            }
            foreach(var file in retained)Check(UpdateTrust.Hash(File.ReadAllBytes(file.Key))==file.Value,"historical completed journal/backups preserved after repeated CLI recovery");
            using(var fence=new WindowsFileFence(root)){UpdateLaunchGate.Check(fence);Check(UpdateRecovery.Scan(fence).All(p=>!p.Pending),"completed retained journals allow clean mod launch");}
        }
        foreach(var changed in new[]{"GameAssembly.dll","winhttp.dll","BepInEx/core/BepInEx.Core.dll","AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle","fixture-cache/catalog_fixture.hash","fixture-cache/catalog_new.json"}) {
            var root=Install(baseRoot,"r-c"+Array.IndexOf(new[]{"GameAssembly.dll","winhttp.dll","BepInEx/core/BepInEx.Core.dll","AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle","fixture-cache/catalog_fixture.hash","fixture-cache/catalog_new.json"},changed),package,inert);
            File.WriteAllText(Path.Combine(root,changed),"changed compatibility fixture");
            using(var fence=new WindowsFileFence(root)) {
                Check(UpdateTransaction.Apply(fence,package.Descriptor,"helper-fixture",package.Signature,package.Zip,package.Trust,Context)==TransactionResult.Committed,"Signed owned update does not require foreign game/loader/catalog approval: "+changed);
            }
            Check(File.ReadAllText(Path.Combine(root,changed))=="changed compatibility fixture","mod update never restores original game/cache/loader content "+changed);
        }
        var missing=Install(baseRoot,"r-missing",package,inert);File.Delete(Path.Combine(missing,InstallReceipt.Path));
        using(var downloads=new UpdateDownloads(package.Trust,new Auth(),new Handler(package))) {
            downloads.Start(Request,Now);for(var i=0;i<300&&downloads.Result.Update==null;i++){downloads.Poll();await Task.Delay(5);}Check(downloads.Result.Update!=null,"valid verified memory payload for missing-receipt fixture");
            try {await new UpdateHandoff(missing,package.Trust,new string('A',64)).Prepare(downloads.Result.Update!,Context,CancellationToken.None);throw new Exception("missing receipt accepted");}
            catch(AutomaticUpdateException error){Check(error.Status==AutomaticUpdateStatus.ManualUpgradeRequired,"actual handoff missing receipt prerequisite before process capture");}
        }
        using(var fence=new WindowsFileFence(missing))Check(UpdateFaults.Read(fence)==null&&!WorkDirectories(missing, "stage").Any(),"missing receipt handoff creates no fault/stage");
        var corrupt=Install(baseRoot,"r-launch-corrupt",package,inert);using(var fence=new WindowsFileFence(corrupt)){UpdateFaults.Fail(fence,Context,FaultPhase.Apply,FaultReason.Apply);UpdateLaunchGate.Check(fence);Check(true,"retired valid failure permits in-game OFF/re-enable UI");}
        File.WriteAllText(Path.Combine(corrupt,UpdateFaults.FailurePath),"corrupt");using(var fence=new WindowsFileFence(corrupt)) Refused(()=>UpdateLaunchGate.Check(fence),"corrupt recovery evidence blocks mod load");
        Console.WriteLine("PASS: review regressions: real ConfigEntry events/poll/Tick/ack, retained same-descriptor retry recovery, launch gate, signed game/loader/catalog preflight and missing receipt.");
    }
}
