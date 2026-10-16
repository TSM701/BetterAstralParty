using BepInEx.Configuration;
using BetterAstralParty;
using BetterAstralParty.Updating;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
internal static partial class HelperFixture {
    private sealed class MemorySafety : IUpdateSafety {
        internal UpdateFault? Record, Active;
        internal bool Download = true, Apply = true, FailWrite, SaveThrows, BeginThrows, Corrupt, Busy, Recovery;
        internal int Begun, Writes, Saves, Completed, Ack;
        public Task<UpdateSafetySnapshot> Probe() => Corrupt ? Task.FromException<UpdateSafetySnapshot>(new IOException("corrupt")) : Task.FromResult(new UpdateSafetySnapshot(Record, Busy, Recovery));
        public Task<UpdateFault> Begin(UpdateContext context) {
            Begun++; if (BeginThrows) throw new IOException("guard write"); Active = new UpdateFault { Context = context, Operation = Guid.NewGuid().ToString("N"), Phase = FaultPhase.Download, Reason = FaultReason.Interrupted }; return Task.FromResult(Active);
        }
        public Task Complete(UpdateFault? record) { if (record != null) { Active = null; Completed++; } return Task.CompletedTask; }
        public Task<UpdateFault> Fail(UpdateContext context, FaultPhase phase, FaultReason reason, string? stage) {
            Writes++; if (FailWrite) throw new IOException("fault write"); Record ??= new UpdateFault { Context = context, Operation = Guid.NewGuid().ToString("N"), Phase = phase, Reason = reason, Stage = stage ?? "-" }; return Task.FromResult(Record);
        }
        public Task SavePreferences(bool download, bool apply) { Saves++; if (SaveThrows) throw new IOException("config"); Download = download; Apply = apply; return Task.CompletedTask; }
        public Task SaveFailed(UpdateFault fault) { fault.PreferencesSaveFailed = true; return Task.CompletedTask; }
        public Task Acknowledge() { if (Recovery || Busy || Corrupt) throw new IOException("unsafe"); Record = Active = null; Ack++; return Task.CompletedTask; }
    }
    private sealed class FaultHandler(HttpStatusCode status, bool timeout = false, bool badSignature = false, Package? package = null) : HttpMessageHandler {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Calls++; if (timeout) throw new TimeoutException("fixture timeout");
            var data = badSignature ? new byte[] { 0 } : Array.Empty<byte>();
            if (badSignature && request.RequestUri!.AbsolutePath.EndsWith("/457")) data = package!.Descriptor;
            var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(data) };
            if (status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new(System.TimeSpan.FromSeconds(61));
            return Task.FromResult(response);
        }
    }
    private static async Task Pump(AutomaticUpdates model, Func<bool> done, DateTimeOffset? clock = null) {
        for (var i = 0; i < 600 && !done(); i++) { model.Tick(clock ?? Now); await Task.Delay(5); }
        Check(done(), "policy condition; status=" + model.Status);
    }
    private static async Task PolicyIntegration(Package package, string baseRoot, byte[] inert) {
        var auth = new Auth();
        foreach (var label in new[] { "network", "timeout", "rate", "integrity" }) {
            var safety = new MemorySafety(); var handler = new FaultHandler(label == "rate" ? HttpStatusCode.TooManyRequests : label == "integrity" ? HttpStatusCode.OK : HttpStatusCode.InternalServerError, label == "timeout", label == "integrity", package);
            using var model = new AutomaticUpdates(new UpdateDownloads(package.Trust, auth, handler), new Handoff(), safety);
            model.Configure(auth); model.Context(Request);
            await Pump(model, () => model.Status is AutomaticUpdateStatus.RetryWaiting or AutomaticUpdateStatus.FaultDisabled);
            if (label != "integrity") {
                Check(handler.Calls == 1 && safety.Record == null && model.AutoDownload && model.ApplyAfterExit, "first transient failure retries without OFF " + label);
                for (var i = 0; i < 20; i++) model.Tick(Now.AddSeconds(30));
                Check(handler.Calls == 1, "cooldown no requests");
                await Pump(model, () => model.Status == AutomaticUpdateStatus.FaultDisabled, Now.AddSeconds(120));
                Check(handler.Calls == 2, "finite two attempts " + label);
            }
            await Pump(model, () => safety.Saves > 0 && model.Fault != null);
            Check(!model.AutoDownload && !model.ApplyAfterExit && !safety.Download && !safety.Apply && !model.CanDownload, "terminal failure persisted both OFF " + label);
            Check(safety.Active == null && safety.Writes == 1, "record before guard retirement " + label);
            model.Preferences(true, true); model.Context(new(new UpdateContext(Context.Repository, "Beta", Context.CurrentVersion, Context.ReleaseTag, 123,456,Context.Platform),457,458)); auth.Id += "x"; model.Tick(Now.AddHours(1));
            Check(!model.CanDownload && !model.AutoDownload, "settings/channel/account cannot bypass latch");
        }
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }) {
            var safety = new MemorySafety(); var handler = new FaultHandler(status); using var model = new AutomaticUpdates(new UpdateDownloads(package.Trust,auth,handler),new Handoff(),safety);
            model.Configure(auth); model.Context(Request); await Pump(model,()=>handler.Calls > 0 && model.Status == AutomaticUpdateStatus.NotConfigured);
            await Pump(model,()=>safety.Active == null);
            Check(safety.Record == null && model.AutoDownload && model.ApplyAfterExit && handler.Calls == 1,"auth/access prerequisite no fault loop");
        }
        foreach (var point in new[] { "prepare", "queue", "poll" }) {
            var safety = new MemorySafety(); var handoff = new Handoff();
            if(point=="prepare") handoff.Delay=new(TaskCreationOptions.RunContinuationsAsynchronously);
            if(point=="queue") handoff.QueueDelay=new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var model=new AutomaticUpdates(new UpdateDownloads(package.Trust,auth,new Handler(package)),handoff,safety);
            model.Configure(auth); model.Context(Request);
            if(point=="prepare") { await Pump(model,()=>handoff.Prepares>0); handoff.Delay!.SetException(new IOException("prepare")); }
            if(point=="queue") { await Pump(model,()=>handoff.Queues>0); handoff.QueueDelay!.SetException(new IOException("queue")); }
            if(point=="poll") { await Pump(model,()=>model.Status==AutomaticUpdateStatus.Queued); handoff.Current.PollFault=true; }
            await Pump(model,()=>model.Fault!=null && safety.Saves>0);
            Check(!safety.Download && !safety.Apply && !model.CanDownload,"terminal "+point+" both OFF");
        }
        foreach(var point in new[]{"guard","fault","settings","corrupt","busy"}) {
            var safety=new MemorySafety { BeginThrows=point=="guard", FailWrite=point=="fault", SaveThrows=point=="settings", Corrupt=point=="corrupt", Busy=point=="busy" };
            var handler=new FaultHandler(HttpStatusCode.OK,false,true,package);
            using var model=new AutomaticUpdates(new UpdateDownloads(package.Trust,auth,handler),new Handoff(),safety);
            model.Configure(auth);model.Context(Request);
            await Pump(model,()=>model.Status is AutomaticUpdateStatus.FaultStorageFailed or AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.FilesBusy);
            if(point=="guard" || point=="corrupt" || point=="busy") Check(handler.Calls==0,"unsafe guard/probe blocks network "+point);
            if(point=="fault") Check(safety.Active!=null && safety.Record==null && !model.CanReactivate,"fault write failure retains operation guard");
            if(point=="settings") { await Pump(model,()=>model.Fault?.PreferencesSaveFailed==true); Check(model.Fault!=null && !model.AutoDownload,"save failure latch remains"); }
            if(point=="busy") Check(model.AutoDownload && model.ApplyAfterExit && safety.Record==null && safety.Saves==0,"normal owner wait not OFF/fault");
            Check(!model.CanDownload,"safe storage/probe block "+point);
        }
        var persisted=new MemorySafety {Record=new UpdateFault {Context=Context,Phase=FaultPhase.Apply,Reason=FaultReason.Apply}};
        using(var restarted=new AutomaticUpdates(new UpdateDownloads(package.Trust,auth,new Handler(package)),new Handoff(),persisted)) {
            restarted.Preferences(true,true);restarted.Configure(auth);restarted.Context(Request);
            await Pump(restarted,()=>restarted.CanReactivate);
            Check(!restarted.AutoDownload && !persisted.Download,"restart saved ON overridden by fault");
            persisted.Recovery=true; restarted.Reactivate(); await Pump(restarted,()=>restarted.Status==AutomaticUpdateStatus.RecoveryRequired);
            Check(persisted.Ack==0 && persisted.Record!=null,"pending recovery cannot reactivate");
            persisted.Recovery=false; restarted.Recheck(); await Pump(restarted,()=>restarted.CanReactivate); persisted.SaveThrows=true; restarted.Reactivate(); await Pump(restarted,()=>restarted.Status==AutomaticUpdateStatus.FaultStorageFailed);
            Check(persisted.Ack==0 && persisted.Record!=null && !restarted.AutoDownload,"failed ON save never archives fault");
            persisted.SaveThrows=false;restarted.Reactivate();await Pump(restarted,()=>persisted.Ack==1 && restarted.Status==AutomaticUpdateStatus.Idle);
            Check(restarted.AutoDownload && restarted.ApplyAfterExit && persisted.Download && persisted.Apply,"explicit reactivation saves ON then archives latch");
            Check(!restarted.CanCancel,"reactivation requires next explicit download/new release");
        }
        var off=new MemorySafety {Download=false,Apply=false};var offHandler=new Handler(package);
        using(var savedOff=new AutomaticUpdates(new UpdateDownloads(package.Trust,auth,offHandler),new Handoff(),off)) {
            savedOff.Preferences(false,false);savedOff.Configure(auth);savedOff.Context(Request);await Pump(savedOff,()=>savedOff.Status==AutomaticUpdateStatus.Idle);
            Check(!savedOff.AutoDownload && !savedOff.ApplyAfterExit && offHandler.Calls==0 && off.Begun==0,"stored OFF survives configured restart without requests");
        }
        foreach(var phase in Enum.GetValues<FaultPhase>()) {
            var state=new MemorySafety {Record=new UpdateFault {Context=Context,Phase=phase,Reason=FaultReason.Interrupted}};
            using var restored=new AutomaticUpdates(UpdateDownloads.Production(),new Handoff(),state);restored.Configure(null);
            await Pump(restored,()=>restored.Fault!=null && !state.Download && !state.Apply);
            Check(!restored.CanDownload && !restored.AutoDownload && !restored.ApplyAfterExit,"restart terminal phase OFF "+phase);
        }
        var fileRoot=Path.Combine(baseRoot,"p-file");Directory.CreateDirectory(fileRoot);File.WriteAllBytes(Path.Combine(fileRoot,"AstralParty_INT.exe"),inert);
        var process=Start(Path.Combine(fileRoot,"AstralParty_INT.exe"),"100");var owner=new UpdateTicket {Stage="-",GamePid=(uint)process.Id,GameCreation=process.StartTime.ToFileTimeUtc(),GameSession=(uint)process.SessionId};
        var cfgPath=Path.Combine(fileRoot,"preferences.cfg");var cfg=new ConfigFile(cfgPath,true);var d=cfg.Bind("Update","Download",true);var a=cfg.Bind("Update","Apply",true);
        cfg.SaveOnConfigSet=false;
        var fileSafety=new FileUpdateSafety(fileRoot,(dl,ap)=>{d.Value=dl;a.Value=ap;cfg.Save();return Task.CompletedTask;},()=>owner);
        var guard=await Task.Run(()=>fileSafety.Begin(Context)); Check((await Task.Run(fileSafety.Probe)).Busy,"exact living original owner waits");
        await process.WaitForExitAsync();process.Dispose();
        Check((await Task.Run(fileSafety.Probe)).Fault?.Reason==FaultReason.Interrupted,"dead original owner classified interrupted");
        using(var restarted=new AutomaticUpdates(UpdateDownloads.Production(channel: ReleaseChannel.Beta),new Handoff(),fileSafety)) {
            restarted.Configure(null);await Pump(restarted,()=>restarted.Fault!=null && !d.Value && !a.Value);
            var reload=new ConfigFile(cfgPath,false);Check(!reload.Bind("Update","Download",true).Value && !reload.Bind("Update","Apply",true).Value,"real config restart both OFF");
            cfg.SaveOnConfigSet=false;d.Value=a.Value=true;cfg.Save();restarted.Preferences(true,true);
            await Pump(restarted,()=>!d.Value && !a.Value);
            Check(!restarted.AutoDownload,"late config writer forced OFF by failure latch");
            await Pump(restarted,()=>restarted.CanReactivate);restarted.Reactivate();await Pump(restarted,()=>restarted.Status==AutomaticUpdateStatus.NotConfigured && restarted.AutoDownload);
            using var verified = new WindowsFileFence(fileRoot); Check(UpdateFaults.Read(verified)==null,"explicit fault acknowledgement no leftover guard");
        }
        Console.WriteLine("PASS: integrated OFF policy, finite retries, prerequisites, storage faults, settings race, exact owner liveness, restart and explicit reactivation.");
    }
}

