using System.Net;
using System.Text;
using BetterAstralParty;
using BetterAstralParty.Updating;

internal static class ReleaseSessionCheck
{
    private static int _checks;
    private const string Inert = "github_pat_INERT_TEST_ONLY_DO_NOT_USE_00000";
    private static void Check(bool value, string name) { Interlocked.Increment(ref _checks); if (!value) throw new Exception("Session fixture: " + name); }
    private sealed class Input : IReleaseCredentialInput
    {
        internal int Calls;
        internal Task<ReleaseCredential?> Value = Task.FromResult<ReleaseCredential?>(new ReleaseCredential(Inert));
        public Task<ReleaseCredential?> Read(bool korean, CancellationToken token) { Interlocked.Increment(ref Calls); return Value; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls; internal HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            Check(request.Headers.Authorization?.Parameter == Inert, "only inert fixture bearer in fake handler");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("[]")) });
        }
    }
    private sealed class Handoff : IUpdateHandoff
    {
        public bool Configured => true;
        public Task<object> Prepare(DownloadedUpdate update, UpdateContext context, CancellationToken token) => throw new Exception("Fixture must not reach prepare");
        public Task<object> Queue(object prepared, CancellationToken token) => throw new Exception("Fixture must not reach queue");
        public void SignalCancel(object prepared) { }
        public Task Cancel(object prepared) => Task.CompletedTask;
        public Task<string?> Inspect() => Task.FromResult<string?>(null);
    }
    private static async Task Until(Action poll, Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < end) { poll(); await Task.Delay(1); }
        poll(); Check(done(), "worker completes within fixture bound");
    }
    internal static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        MonotonicLifetimeChecks();
        var now = DateTimeOffset.UtcNow;
        var elapsed = TimeSpan.Zero;
        Check(!PrivateUpdateApproval.CredentialInputEnabled && !UpdateTrust.Production().Configured && ProductionUpdateKeys.Read().Length == 0, "production approval and pins remain absent");
        Check(MemoryReleaseAuthentication.ValidToken(Inert), "inert fixture lexical format");
        foreach (var bad in new[] { "", "ghp_INERT_TEST_ONLY_DO_NOT_USE_00000", Inert + "\n", " " + Inert, Inert + ":", new string('a', 257) })
            Check(!MemoryReleaseAuthentication.ValidToken(bad), "unsafe input rejected without echo");
        using (var auth = new MemoryReleaseAuthentication(Inert, TimeSpan.FromMinutes(1), () => elapsed))
        {
            var id = auth.CacheIdentity;
            Check(id.Length == 32 && !id.Contains(Inert) && auth.ToString() == "MemoryReleaseAuthentication", "opaque identity and safe stringification");
            foreach (var url in new[] { "https://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100", "https://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100&page=2", "https://api.github.com/repos/TSM701/BetterAstralParty/releases?&per_page=100", "https://api.github.com/repos/TSM701/BetterAstralParty/releases/assets/902" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                var allowed = !url.Contains("?&");
                Check(auth.TryAuthorize(request) == allowed && (request.Headers.Authorization != null) == allowed, "exact allowed URI/query boundary");
            }
            foreach (var url in new[] {
                "http://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100", "https://api.github.com:443/repos/TSM701/BetterAstralParty/releases?per_page=100",
                "https://api.github.com/repos/OTHER/BetterAstralParty/releases?per_page=100", "https://api.github.com/user", "https://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100&page=0",
                "https://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100&page=2&x=1", "https://api.github.com/repos/TSM701/BetterAstralParty/releases/assets/0",
                "https://api.github.com/repos/TSM701/BetterAstralParty/releases/assets/902?x=1", "https://release-assets.githubusercontent.com/file", "https://api.github.com/repos/TSM701/BetterAstralParty/releases/assets/%39%30%32"
            }) { using var request = new HttpRequestMessage(HttpMethod.Get, url); Check(!auth.TryAuthorize(request) && request.Headers.Authorization == null, "bearer cannot leave exact repo API"); }
            using var post = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100");
            Check(!auth.TryAuthorize(post), "write verbs refused");
            elapsed += TimeSpan.FromMinutes(2); Check(auth.CacheIdentity == "" && !auth.TryAuthorize(post), "expired session loses identity/bearer");
        }
        using (var credential = new ReleaseCredential(Inert)) { Check(credential.ToString() == "ReleaseCredential" && credential.Take() == Inert && credential.Take() == null, "credential consumed once without ToString leak"); }
        using (var productionUpdates = ReleaseUpdates.Production("1.0.0-dev", "TSM701/BetterAstralParty"))
        using (var automatic = new AutomaticUpdates(UpdateDownloads.Production()))
        using (var session = PrivateReleaseSession.Production(productionUpdates, automatic))
        {
            var input = new Input(); session.Connect(input, false); session.Poll();
            Check(input.Calls == 0 && !session.CanConnect && session.Status == ReleaseSessionStatus.NotConfigured, "unapproved product never invokes input");
            Check(await new WindowsReleaseCredentialInput().Read(false, CancellationToken.None) == null, "Windows native prompt gate checked before PInvoke");
            Check(WindowsReleaseCredentialInput.Flags == 1, "no save checkbox or persistence flag");
        }
        using var handler = new Handler(); using var client = new HttpClient(handler);
        using var updates = new ReleaseUpdates("1.0.0-dev", "TSM701/BetterAstralParty", client);
        using var downloads = new AutomaticUpdates(UpdateDownloads.Production());
        var allowedPolicy = true;
        using var coordinator = new PrivateReleaseSession(updates, downloads, () => allowedPolicy, () => elapsed, auth => updates.Configure(client, auth));
        var completed = new Input(); coordinator.Connect(completed, false);
        await Until(coordinator.Poll, () => coordinator.Status != ReleaseSessionStatus.AwaitingInput);
        Check(coordinator.Status == ReleaseSessionStatus.Connected && completed.Calls == 1, "input connects session");
        updates.Check(now); await Until(updates.Poll, () => updates.Result.Status != ReleaseUpdateStatus.Checking);
        Check(handler.Calls == 1 && updates.Result.Status == ReleaseUpdateStatus.UpToDate, "fake private API uses memory provider");
        handler.Status = HttpStatusCode.Unauthorized;
        updates.Check(now.AddMinutes(2)); await Until(updates.Poll, () => updates.Result.Status != ReleaseUpdateStatus.Checking);
        coordinator.Poll(); Check(coordinator.Status == ReleaseSessionStatus.AuthenticationRequired && updates.Result.Status == ReleaseUpdateStatus.NotConfigured && !downloads.CanDownload, "401 retires both network pipelines");
        coordinator.Connect(new Input(), false); await Until(coordinator.Poll, () => coordinator.Status != ReleaseSessionStatus.AwaitingInput);
        handler.Status = HttpStatusCode.NotFound; updates.Check(now.AddMinutes(4)); await Until(updates.Poll, () => updates.Result.Status != ReleaseUpdateStatus.Checking);
        coordinator.Poll(); Check(coordinator.Status == ReleaseSessionStatus.AccessUnavailable && !updates.TryDownloadPage(out _), "404 access loss discards account metadata");
        var late = new TaskCompletionSource<ReleaseCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new Input { Value = late.Task }; coordinator.Connect(waiting, false);
        await Until(() => {}, () => waiting.Calls == 1);
        Check(WindowsReleaseCredentialInput.TryEnterPrompt(), "substitute prompt acquires lifecycle without PInvoke");
        try {
            coordinator.Poll(); coordinator.SignOut(); var signedOutRevision = coordinator.Revision;
            Check(!coordinator.CanConnect && coordinator.Status == ReleaseSessionStatus.SignedOut, "logout waits for retired substitute prompt to close");
            WindowsReleaseCredentialInput.LeavePrompt(); coordinator.Poll();
            Check(coordinator.CanConnect && coordinator.Revision > signedOutRevision, "prompt close raises main-thread revision for connection colour refresh");
        } finally { WindowsReleaseCredentialInput.LeavePrompt(); }
        var retired = new ReleaseCredential(Inert); late.SetResult(retired);
        await Until(() => {}, () => retired.IsEmpty);
        coordinator.Poll(); Check(coordinator.Status == ReleaseSessionStatus.SignedOut && updates.Result.Status == ReleaseUpdateStatus.NotConfigured, "late input cannot revive signed-out session");
        coordinator.Connect(new Input { Value = Task.FromResult<ReleaseCredential?>(new ReleaseCredential("INVALID_INERT_INPUT")) }, false);
        await Until(coordinator.Poll, () => coordinator.Status != ReleaseSessionStatus.AwaitingInput);
        Check(coordinator.Status == ReleaseSessionStatus.InvalidInput && updates.Result.Status == ReleaseUpdateStatus.NotConfigured, "invalid input never configures transport");
        foreach (var item in new[] {
            (Task.FromResult<ReleaseCredential?>(null), ReleaseSessionStatus.Cancelled),
            (Task.FromException<ReleaseCredential?>(new InvalidOperationException("INERT_ERROR_DO_NOT_ECHO")), ReleaseSessionStatus.InputFailed),
            (Task.FromException<ReleaseCredential?>(new ReleaseInputRejectedException()), ReleaseSessionStatus.InvalidInput),
            (Task.FromCanceled<ReleaseCredential?>(new CancellationToken(true)), ReleaseSessionStatus.Cancelled)
        }) {
            coordinator.Connect(new Input { Value = item.Item1 }, false);
            await Until(coordinator.Poll, () => coordinator.Status != ReleaseSessionStatus.AwaitingInput);
            Check(coordinator.Status == item.Item2 && updates.Result.Status == ReleaseUpdateStatus.NotConfigured, "input cancellation/rejection/API error classified before transport");
            Check(!ModText.SessionDetails(coordinator.Status).Contains("INERT_ERROR"), "input failure detail never echoes worker exception");
        }
        coordinator.Connect(new Input(), false); await Until(coordinator.Poll, () => coordinator.Status != ReleaseSessionStatus.AwaitingInput);
        elapsed += TimeSpan.FromHours(9); coordinator.Poll(); Check(coordinator.Status == ReleaseSessionStatus.Connected, "accepted session has no artificial eight-hour expiry");
        coordinator.Connect(new Input(), false); await Until(coordinator.Poll, () => coordinator.Status != ReleaseSessionStatus.AwaitingInput);
        allowedPolicy = false; coordinator.Poll(); Check(coordinator.Status == ReleaseSessionStatus.NotConfigured && updates.Result.Status == ReleaseUpdateStatus.NotConfigured, "policy revocation clears live session");
        var publicOnly = new System.Security.Cryptography.RSAParameters { Modulus = Enumerable.Repeat((byte)0xA5, 384).ToArray(), Exponent = new byte[] {1,0,1} };
        var spki = ProductionUpdateKeys.Spki(publicOnly);
        Check(spki.Length == 422 && spki[0] == 48, "public-only canonical 3072 SPKI encoding");
        ProductionUpdateKeys.Review("inert-public-only", Convert.ToBase64String(publicOnly.Modulus!), UpdateTrust.Hash(spki));
        try { ProductionUpdateKeys.Review("inert-public-only", Convert.ToBase64String(publicOnly.Modulus!), new string('0',64)); Check(false,"wrong pin accepted"); } catch(ArgumentException) { Check(true,"wrong public fingerprint rejected"); }
        Check(!UpdateTrust.Production().Configured, "reviewed fixture public bytes are never enrolled in production");
        foreach (var language in new[] { "English", "한국어" })
        {
            ModText.Select(language);
            foreach (var status in Enum.GetValues<ReleaseSessionStatus>())
                Check(ModText.SessionDetails(status).Length > 0 && (language != "English" || !ModText.SessionDetails(status).Any(c => c is >= '\uac00' and <= '\ud7a3')), "localized session status");
            Check(MenuLayout.GeneralControl("UpdateAuth") && MenuLayout.GeneralControl("UpdateSignOut") && ModText.SessionTitle("UpdateAuth").Length > 0, "native scrolling general settings host");
        }
        ModText.Select("Auto");
        foreach (var failure in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            using var metadataHandler = new Handler(); using var metadataClient = new HttpClient(metadataHandler);
            using var metadata = new ReleaseUpdates("1.0.0-dev", "TSM701/BetterAstralParty", metadataClient);
            using var assetHandler = new Handler { Status = failure };
            var fixtureTrust = new UpdateTrust(new[] { new TrustedUpdateKey("inert-public-only", publicOnly) });
            using var automatic = new AutomaticUpdates(new UpdateDownloads(fixtureTrust, null, assetHandler), new Handoff());
            using var session = new PrivateReleaseSession(metadata, automatic, () => true, () => elapsed, auth => metadata.Configure(metadataClient, auth));
            session.Connect(new Input(), false); await Until(session.Poll, () => session.Status != ReleaseSessionStatus.AwaitingInput);
            automatic.Context(new UpdateDownloadRequest(new UpdateContext("TSM701/BetterAstralParty","Beta","1.0.0-dev","1.0.0-rc.1",901,902,UpdateTrust.PlatformId),903,904));
            await Until(() => automatic.Tick(now), () => automatic.AuthenticationLost);
            session.Poll(); Check(assetHandler.Calls == 1 && !automatic.CanDownload && metadata.Result.Status == ReleaseUpdateStatus.NotConfigured,
                "download auth/access failure retires metadata/download/handoff together");
            Check(session.Status == (failure == HttpStatusCode.Unauthorized ? ReleaseSessionStatus.AuthenticationRequired : ReleaseSessionStatus.AccessUnavailable), "download failure wording classified");
        }
        using (var exitHandler = new Handler()) using (var exitClient = new HttpClient(exitHandler))
        using (var exitUpdates = new ReleaseUpdates("1.0.0-dev", "TSM701/BetterAstralParty", exitClient))
        using (var exitAutomatic = new AutomaticUpdates(UpdateDownloads.Production()))
        {
            var configureCalls = 0; IReleaseAuthentication? active = null;
            using var exitSession = new PrivateReleaseSession(exitUpdates, exitAutomatic, () => true, () => elapsed,
                auth => { configureCalls++; active = auth; exitUpdates.Configure(exitClient, auth); });
            exitSession.Connect(new Input(), false); await Until(exitSession.Poll, () => exitSession.Status != ReleaseSessionStatus.AwaitingInput);
            var callsBeforeExit = configureCalls; exitSession.ProcessExiting();
            Check(active?.CacheIdentity == "" && configureCalls == callsBeforeExit && !exitSession.CanConnect,
                "natural exit forgets token without reconfiguring/cancelling queued offline apply");
        }
        using (var raceHandler = new Handler()) using (var raceClient = new HttpClient(raceHandler))
        using (var raceUpdates = new ReleaseUpdates("1.0.0-dev", "TSM701/BetterAstralParty", raceClient))
        using (var raceAutomatic = new AutomaticUpdates(UpdateDownloads.Production()))
        using (var published = new ManualResetEventSlim()) using (var allowPublication = new ManualResetEventSlim())
        {
            IReleaseAuthentication? active = null; var publications = 0;
            using var raceSession = new PrivateReleaseSession(raceUpdates,raceAutomatic,()=>true,null,auth=>{
                if(auth!=null){published.Set();if(!allowPublication.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("INERT_PUBLICATION_GATE");Interlocked.Increment(ref publications);active=auth;}
                raceUpdates.Configure(raceClient,auth);
            });
            var input = new Input(); raceSession.Connect(input,false);await Until(()=>{},()=>input.Calls==1);
            var poll=Task.Run(raceSession.Poll);
            Check(await Task.Run(()=>published.Wait(TimeSpan.FromSeconds(5))),"publication race reaches deterministic gate without native input");
            var exit=Task.Run(raceSession.ProcessExiting);allowPublication.Set();await Task.WhenAll(poll,exit).WaitAsync(TimeSpan.FromSeconds(5));
            Check(publications==1&&active?.CacheIdentity==""&&!raceSession.CanConnect,"concurrent exit serializes publication then retires token");
            raceSession.Connect(new Input(),false);raceSession.Poll();
            Check(publications==1&&raceSession.Status==ReleaseSessionStatus.SignedOut,"no authentication can publish after irreversible exit");
        }
        using(var shortSession=new MemoryReleaseAuthentication(Inert,TimeSpan.FromMilliseconds(5))){await Task.Delay(30);Check(shortSession.CacheIdentity=="","default production lifetime clock uses elapsed Stopwatch");}
        Console.WriteLine($"PASS: release session {_checks} assertions; inert credentials/fake HTTP/public-only parameters. No real credentials, private keys, native prompt, installation or publication.");
    }
    private static void MonotonicLifetimeChecks()
    {
        Check(!WindowsReleaseCredentialInput.ValidatePrompt(1223, false, IntPtr.Zero, 0), "only ERROR_CANCELLED returns normal cancellation");
        Check(WindowsReleaseCredentialInput.ValidatePrompt(0, false, new IntPtr(1), 1), "successful native result classification only; pointer never dereferenced");
        foreach (var item in new[] {(5U,false,new IntPtr(1),1U),(0U,true,new IntPtr(1),1U),(0U,false,IntPtr.Zero,1U),(0U,false,new IntPtr(1),0U),(0U,false,new IntPtr(1),65537U)}) {
            try { WindowsReleaseCredentialInput.ValidatePrompt(item.Item1,item.Item2,item.Item3,item.Item4); Check(false,"native failure classified as success"); }
            catch(InvalidOperationException) { Check(true,"native API/save/buffer failure distinguished from cancellation"); }
        }
        WindowsReleaseCredentialInput.ValidateUnpacked(true,"tsm701","");
        Check(true,"username matching accepts canonical account case without input echo");
        try { WindowsReleaseCredentialInput.ValidateUnpacked(false,"",""); Check(false,"unpack failure accepted"); } catch(InvalidOperationException) { Check(true,"unpack failure distinguished from cancellation"); }
        foreach(var item in new[] {("OTHER", ""),("TSM701", "DOMAIN")}) {
            try { WindowsReleaseCredentialInput.ValidateUnpacked(true,item.Item1,item.Item2); Check(false,"identity mismatch accepted"); }
            catch(ReleaseInputRejectedException) { Check(true,"identity mismatch is invalid input"); }
        }
        foreach (var jump in new[] { TimeSpan.Zero, TimeSpan.FromHours(-3), TimeSpan.FromHours(12) })
        {
            var wall = DateTimeOffset.UtcNow; var origin = wall; var elapsed = TimeSpan.Zero;
            using var auth = new MemoryReleaseAuthentication(Inert, TimeSpan.FromHours(8), () => elapsed);
            wall += jump; elapsed = TimeSpan.FromHours(8) - TimeSpan.FromTicks(1);
            using var before = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/TSM701/BetterAstralPartyBeta/releases?per_page=100");
            Check(auth.TryAuthorize(before) && auth.CacheIdentity.Length == 32, "normal/backward/forward wall time leaves pre-limit elapsed session valid");
            Check(wall - origin == jump, "fixture applies independent wall adjustment");
            elapsed = TimeSpan.FromHours(8);
            using var at = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/TSM701/BetterAstralPartyBeta/releases?per_page=100");
            Check(!auth.TryAuthorize(at) && at.Headers.Authorization == null && auth.CacheIdentity == "", "exact eight elapsed hours refuses fresh bearer regardless of wall adjustment");
            elapsed = TimeSpan.Zero;
            using var after = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/TSM701/BetterAstralPartyBeta/releases/assets/902");
            Check(!auth.TryAuthorize(after), "retired elapsed session cannot revive when injected clock regresses");
        }
        foreach (var invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromHours(8) + TimeSpan.FromTicks(1) })
        {
            try { using var _ = new MemoryReleaseAuthentication(Inert, invalid); Check(false, "invalid elapsed limit accepted"); }
            catch (ArgumentException) { Check(true, "zero/negative/over-eight-hour lifetime refused"); }
        }
        var stamp = TimeSpan.FromHours(1);
        using var regressed = new MemoryReleaseAuthentication(Inert, TimeSpan.FromHours(8), () => stamp);
        stamp -= TimeSpan.FromTicks(1);
        Check(regressed.CacheIdentity == "", "invalid negative elapsed age fails closed");
    }
}
