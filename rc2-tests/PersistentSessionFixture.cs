using System.Net;
using System.Text;
using BetterAstralParty;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Observability;
using BetterAstralParty.Updating;

// Inert credentials and injected transports only. Never open the production vault target.
internal static class PersistentSessionFixture
{
    private const string Inert = "github_pat_INERT_TEST_ONLY_DO_NOT_USE_00000";
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static async Task Until(Action poll, Func<bool> done, string name)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < end) { poll(); await Task.Delay(1); }
        poll(); Check(done(), name);
    }
    private sealed class Store : IReleaseCredentialStore
    {
        internal string? Value;
        internal int Loads, Saves, Deletes;
        internal bool FailLoad, FailSave, FailDelete;
        internal readonly TaskCompletionSource<bool> LoadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim? LoadGate;
        public string? Load()
        {
            Interlocked.Increment(ref Loads); var value = Value; LoadStarted.TrySetResult(true);
            LoadGate?.Wait();
            if (FailLoad) throw new IOException("Synthetic credential load failure");
            return value;
        }
        public void Save(string token)
        {
            Interlocked.Increment(ref Saves);
            if (FailSave) throw new IOException("Synthetic credential save failure");
            Value = token;
        }
        public void Delete()
        {
            Interlocked.Increment(ref Deletes);
            if (FailDelete) throw new IOException("Synthetic credential delete failure");
            Value = null;
        }
    }
    private sealed class Input : IReleaseCredentialInput
    {
        internal int Calls;
        internal bool Korean;
        internal CancellationToken Cancellation;
        internal Task<ReleaseCredential?> Result = Task.FromResult<ReleaseCredential?>(new(Inert));
        public Task<ReleaseCredential?> Read(bool korean, CancellationToken cancellation)
        { Interlocked.Increment(ref Calls); Korean = korean; Cancellation = cancellation; return Result; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls, BetaCalls, StableCalls;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal bool FailNetwork, RateLimited;
        internal string? ReleaseVersion;
        internal TaskCompletionSource<bool>? DelayNext;
        internal readonly TaskCompletionSource<bool> RequestDelayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);
            var uri = request.RequestUri!;
            Check(request.Method == HttpMethod.Get && uri.Scheme == "https" && uri.Host == "api.github.com", "Fixture receives only read-only GitHub API requests");
            var feed = uri.AbsolutePath == "/repos/" + ReleaseFeedPolicy.BetaRepository || uri.AbsolutePath == "/repos/" + ReleaseFeedPolicy.BetaRepository + "/releases"
                ? ReleaseFeedPolicy.Beta : ReleaseFeedPolicy.Stable;
            var identity = uri.AbsolutePath == "/repos/" + feed.Repository;
            Check(identity && uri.Query.Length == 0 || uri.AbsolutePath == "/repos/" + feed.Repository + "/releases" && uri.Query == "?per_page=100", "Request stays on the exact selected repository identity/list endpoint");
            if (feed.RequiresAuthentication) {
                Interlocked.Increment(ref BetaCalls);
                Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == Inert, "Beta receives only its inert fixture credential");
            } else {
                Interlocked.Increment(ref StableCalls);
                Check(request.Headers.Authorization == null, "Every Stable request remains anonymous");
            }
            if (FailNetwork) throw new HttpRequestException("Synthetic network failure");
            var releases = ReleaseVersion == null ? "[]" : System.Text.Json.JsonSerializer.Serialize(new[] { new {
                tag_name = ReleaseVersion, draft = false, prerelease = feed.RequiresAuthentication,
                name = "Inert fixture release", body = "Fixture only", html_url = "https://github.com/" + feed.Repository + "/releases/tag/" + ReleaseVersion } });
            var body = identity ? "{\"id\":" + feed.RepositoryId + ",\"full_name\":\"" + feed.Repository + "\",\"private\":" + (feed.RequiresAuthentication ? "true" : "false") + "}" : releases;
            var response = new HttpResponseMessage(Status) { RequestMessage = request, Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
            if (RateLimited) { response.Headers.Add("X-RateLimit-Remaining", "0"); response.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            var delay = DelayNext; DelayNext = null;
            if (delay != null) { RequestDelayed.TrySetResult(true); await delay.Task.ConfigureAwait(false); } // Deliberately ignore cancellation to exercise retired generations.
            return response;
        }
    }
    private sealed class Harness : IDisposable
    {
        internal readonly Handler Handler = new();
        internal readonly HttpClient Client;
        internal readonly ReleaseUpdates Updates;
        internal readonly AutomaticUpdates Automatic;
        internal readonly PrivateReleaseSession Session;
        internal IReleaseAuthentication? Authentication;
        internal bool Allowed;
        internal TimeSpan Elapsed;
        internal Harness(Store? store, bool allowed = true)
        {
            Allowed = allowed; Client = new(Handler);
            Updates = new("1.0.0-rc.5", ReleaseFeedPolicy.Beta, Client);
            // Exercise production authentication configuration with an owned, inert transport.
            typeof(ReleaseUpdates).GetField("_ownedClient", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(Updates, Client);
            Automatic = new(new UpdateDownloads(new UpdateTrust(Array.Empty<TrustedUpdateKey>()), null, new Handler(), feed: ReleaseFeedPolicy.Beta));
            Session = new(Updates, Automatic, () => Allowed, () => Elapsed, auth => {
                if (auth != null && store != null) Check(store.Value == Inert, "Persistence precedes authentication publication");
                Authentication = auth; Updates.ConfigureAuthentication(auth);
            }, store);
        }
        internal Task Ready() => Until(Session.Poll, () => Session.Status != ReleaseSessionStatus.Restoring, "One-shot restore completes");
        internal async Task Connect(Input input, bool korean = false)
        { Session.Connect(input, korean); await Until(Session.Poll, () => Session.Status != ReleaseSessionStatus.AwaitingInput, "Input completes"); }
        internal async Task CheckRemote()
        { Updates.Check(DateTimeOffset.UtcNow); await Until(Updates.Poll, () => Updates.Result.Status != ReleaseUpdateStatus.Checking, "Fake remote check completes"); Session.Poll(); }
        internal void SelectChannel(ReleaseChannel channel)
        { Updates.SetChannel(channel); Automatic.SetChannel(channel); Automatic.Context(null, Updates.Generation); }
        public void Dispose() { Session.Dispose(); Automatic.Dispose(); Updates.Dispose(); Client.Dispose(); }
    }
    private static async Task RestartAndLifetime()
    {
        var store = new Store { Value = Inert };
        using (var first = new Harness(store))
        {
            Check(first.Session.Status == ReleaseSessionStatus.Restoring && !first.Session.CanConnect && first.Session.CanSignOut, "Restore blocks duplicate connection and permits explicit logout");
            await first.Ready();
            Check(first.Session.Status == ReleaseSessionStatus.Connected && store.Loads == 1 && store.Saves == 0, "Saved credential restores without another save or input");
            first.Elapsed = TimeSpan.FromDays(7); first.Session.Poll();
            Check(first.Session.Status == ReleaseSessionStatus.Connected && first.Authentication?.CacheIdentity.Length == 32, "Coordinator has no artificial eight-hour expiration");
            await first.CheckRemote();
            Check(first.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "Restored authentication works through the current identity-checked fake transport");
        }
        Check(store.Value == Inert && store.Deletes == 0, "Dispose forgets memory but preserves the saved credential");
        using (var second = new Harness(store))
        {
            await second.Ready(); var auth = second.Authentication;
            Check(second.Session.Status == ReleaseSessionStatus.Connected && store.Loads == 2, "A new session restores after restart");
            second.Session.ProcessExiting(); second.Session.Poll();
            Check(store.Value == Inert && store.Deletes == 0 && auth?.CacheIdentity == "", "Process exit forgets memory but keeps storage");
        }
        using (var third = new Harness(store))
        {
            await third.Ready(); third.Session.SignOut();
            Check(third.Session.Status == ReleaseSessionStatus.SignedOut && third.Authentication == null && store.Value == null && store.Deletes == 1, "Explicit logout deletes saved credentials and retires both pipelines");
            for (var i = 0; i < 20; i++) third.Session.Poll();
            Check(store.Loads == 3, "Logout does not trigger an automatic restore loop");
        }
        using (var fourth = new Harness(store)) { await fourth.Ready(); Check(fourth.Session.Status == ReleaseSessionStatus.SignedOut, "Restart after logout remains signed out"); }
        var elapsed = TimeSpan.Zero;
        using var limited = new MemoryReleaseAuthentication(Inert, TimeSpan.FromMinutes(1), () => elapsed);
        elapsed = TimeSpan.FromMinutes(1); Check(limited.CacheIdentity == "", "Explicit fixture TTL remains supported");
    }
    private static async Task InputAndStorageFailures()
    {
        foreach (var korean in new[] { false, true })
        {
            var store = new Store(); using var h = new Harness(store); await h.Ready();
            var input = new Input(); await h.Connect(input, korean);
            Check(h.Session.Status == ReleaseSessionStatus.Connected && store.Value == Inert && store.Saves == 1 && input.Calls == 1 && input.Korean == korean, "Successful input saves before connection and preserves the chosen prompt language");
        }
        foreach (var failLoad in new[] { false, true })
        {
            var store = new Store { FailLoad = failLoad, FailSave = !failLoad }; using var h = new Harness(store); await h.Ready();
            if (!failLoad) await h.Connect(new Input());
            Check(h.Session.Status == ReleaseSessionStatus.StorageFailed && h.Authentication == null && store.Value == null, "Load/save failure never reports a persistent connection");
            for (var i = 0; i < 20; i++) h.Session.Poll();
            Check(store.Loads == 1, "Storage failure is not retried every frame");
        }
        var failedDelete = new Store { Value = Inert, FailDelete = true };
        using (var h = new Harness(failedDelete))
        {
            await h.Ready(); var auth = h.Authentication; h.Session.SignOut();
            Check(h.Session.Status == ReleaseSessionStatus.StorageFailed && h.Authentication == null && auth?.CacheIdentity == "" && failedDelete.Value == Inert, "Failed delete clearly reports failure while retiring memory");
            failedDelete.FailDelete = false; h.Session.SignOut();
            Check(h.Session.Status == ReleaseSessionStatus.SignedOut && failedDelete.Value == null && failedDelete.Deletes == 2, "Explicit logout can retry a failed storage deletion");
        }
        foreach (var value in new string?[] { null, "not-a-PAT" })
        {
            var store = new Store { Value = value }; using var h = new Harness(store); await h.Ready();
            Check(h.Session.Status == (value == null ? ReleaseSessionStatus.SignedOut : ReleaseSessionStatus.InvalidInput) && h.Authentication == null, "Missing/invalid saved input cannot publish authentication");
        }
        var blocked = new Store { Value = Inert }; using var denied = new Harness(blocked, allowed: false);
        denied.Session.Poll(); Check(blocked.Loads == 0 && denied.Session.Status == ReleaseSessionStatus.NotConfigured, "Disabled policy never reads the store");
    }
    private static async Task LateWorkers()
    {
        using var gate = new ManualResetEventSlim(false);
        var store = new Store { Value = Inert, LoadGate = gate };
        using (var h = new Harness(store))
        {
            try
            {
                await Until(() => { }, () => store.LoadStarted.Task.IsCompleted, "Restore entered its controlled store");
                var ignored = new Input(); h.Session.Connect(ignored, false); Check(ignored.Calls == 0, "Clicking Connect during restore never opens a second input");
                h.Session.SignOut(); gate.Set();
                for (var i = 0; i < 20; i++) { await Task.Delay(1); h.Session.Poll(); }
                Check(h.Session.Status == ReleaseSessionStatus.SignedOut && h.Authentication == null && store.Value == null && store.Deletes == 1 && store.Saves == 0, "Late restore cannot undo explicit logout");
            }
            finally { gate.Set(); }
        }
        foreach (var processExit in new[] { false, true })
        {
            var inputStore = new Store(); using var h = new Harness(inputStore); await h.Ready();
            var pending = new TaskCompletionSource<ReleaseCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var input = new Input { Result = pending.Task }; h.Session.Connect(input, false);
            await Until(() => { }, () => input.Calls == 1, "Controlled input started");
            if (processExit) h.Session.ProcessExiting(); else h.Session.SignOut();
            var credential = new ReleaseCredential(Inert); pending.SetResult(credential);
            await Until(h.Session.Poll, () => credential.IsEmpty, "Retired late credential is disposed");
            Check(input.Cancellation.IsCancellationRequested && h.Authentication == null && h.Session.Status == ReleaseSessionStatus.SignedOut && inputStore.Saves == 0, "Late input cannot save or republish after logout/process exit");
        }
        using var cancelled = new Harness(new Store()); await cancelled.Ready();
        await cancelled.Connect(new Input { Result = Task.FromResult<ReleaseCredential?>(null) });
        Check(cancelled.Session.Status == ReleaseSessionStatus.Cancelled && cancelled.Authentication == null, "Cancelled input remains disconnected");
    }
    private static async Task RemoteFailures()
    {
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.OK })
        {
            var store = new Store { Value = Inert }; using var h = new Harness(store); await h.Ready();
            h.Handler.Status = code; h.Handler.FailNetwork = code == HttpStatusCode.OK; await h.CheckRemote();
            Check(store.Deletes == (code == HttpStatusCode.Unauthorized ? 1 : 0) && (store.Value == null) == (code == HttpStatusCode.Unauthorized), "Only a real 401 discards saved authentication");
            if (code == HttpStatusCode.Unauthorized) Check(h.Session.Status == ReleaseSessionStatus.AuthenticationRequired && h.Authentication == null, "401 retires authentication and requests sign-in");
            else if (code is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) Check(h.Session.Status == ReleaseSessionStatus.AccessUnavailable, "403/404 access failure is distinct from invalid authentication");
            else Check(h.Session.Status == ReleaseSessionStatus.Connected, "Rate limiting/network failure preserves connection and saved token");
            var calls = h.Handler.Calls; for (var i = 0; i < 20; i++) h.Session.Poll();
            Check(store.Loads == 1 && h.Handler.Calls == calls, "Failure polling never reloads credentials or retries the network");
        }
        var limitedStore = new Store { Value = Inert }; using (var limited = new Harness(limitedStore))
        {
            await limited.Ready(); limited.Handler.Status = HttpStatusCode.Forbidden; limited.Handler.RateLimited = true; await limited.CheckRemote();
            Check(limited.Updates.Result.Status == ReleaseUpdateStatus.RateLimited && limited.Session.Status == ReleaseSessionStatus.Connected && limitedStore.Value == Inert && limitedStore.Deletes == 0, "A rate-limit 403 preserves both saved and memory authentication");
        }
        var failedDelete = new Store { Value = Inert, FailDelete = true }; using var failed = new Harness(failedDelete); await failed.Ready();
        failed.Handler.Status = HttpStatusCode.Unauthorized; await failed.CheckRemote();
        Check(failed.Session.Status == ReleaseSessionStatus.StorageFailed && failed.Authentication == null && failedDelete.Value == Inert, "401 delete failure is visible and does not keep memory authorization");
    }
    private static async Task RefreshAfterContextChange()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(20000);
        using var h = new Harness(new Store()); await h.Ready();
        h.Updates.Tick(now, true);
        Check(h.Handler.Calls == 0, "Signed-out Beta never starts a discovery request");
        await h.Connect(new Input());
        h.Updates.Tick(now, true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "First login automatically refreshes discovery");
        Check(h.Handler.Calls == 2 && h.Updates.Result.CheckedAt == now, "Login checks repository identity and releases once");
        h.Updates.Tick(now.AddSeconds(1), false); h.Updates.Tick(now.AddSeconds(2), true);
        Check(h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate && h.Updates.Result.CheckedAt == now && h.Handler.Calls == 2, "A plain home reentry keeps the fresh cache instead of queuing another request");

        h.Session.SignOut(); h.Updates.Tick(now.AddSeconds(3), true);
        Check(h.Updates.Result.Status == ReleaseUpdateStatus.NotConfigured && h.Handler.Calls == 2, "Logout clears discovery without network access");
        await h.Connect(new Input()); h.Updates.Tick(now.AddSeconds(4), true);
        Check(h.Updates.Result.Status == ReleaseUpdateStatus.RetryWaiting && h.Updates.Result.RetryAt == now.AddSeconds(60), "Relogin exposes its scheduled refresh instead of staying NotChecked");
        for (var i = 0; i < 20; i++) h.Updates.Tick(now.AddSeconds(59), true);
        Check(h.Handler.Calls == 2, "Repeated frames cannot bypass discovery cooldown");
        h.Updates.Tick(now.AddSeconds(60), true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "Relogin refresh automatically runs at its deadline");

        h.SelectChannel(ReleaseChannel.Stable); h.Updates.Tick(now.AddSeconds(61), true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "First Stable visit refreshes immediately despite the recent Beta check");
        Check(h.Handler.Calls == 6 && h.Handler.StableCalls == 2 && h.Updates.Result.CheckedAt == now.AddSeconds(61) && h.Session.Status == ReleaseSessionStatus.Connected, "Stable identity/list requests are anonymous while Beta authentication remains connected");
        h.SelectChannel(ReleaseChannel.Beta); h.Updates.Tick(now.AddSeconds(62), true);
        Check(h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate && h.Updates.Result.CheckedAt == now.AddSeconds(60) && h.Handler.Calls == 6 && h.Handler.BetaCalls == 4, "Quick return restores the verified Beta result without another request or a fictional checked timestamp");

        h.Handler.FailNetwork = true; h.Updates.Check(now.AddSeconds(240));
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.Stale, "Controlled discovery failure settles");
        h.Handler.FailNetwork = false; var calls = h.Handler.Calls;
        h.Updates.Check(now.AddSeconds(241));
        Check(h.Updates.Result.RetryAt == now.AddSeconds(300), "A manual refresh during cooldown is queued with its deadline");
        h.Updates.Tick(now.AddSeconds(299), true);
        Check(h.Handler.Calls == calls, "Queued manual refresh respects the shared cooldown");
        h.Updates.Tick(now.AddSeconds(300), true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "Queued manual refresh is not dropped or suppressed by old cache freshness");
        Check(h.Handler.Calls == calls + 2 && h.Updates.Result.CheckedAt == now.AddSeconds(300), "Queued manual refresh receives a new result");

        h.Handler.Status = HttpStatusCode.TooManyRequests; h.Updates.Check(now.AddSeconds(360));
        await Until(h.Updates.Poll, () => h.Updates.Result.Failure == ReleaseUpdateStatus.RateLimited, "Controlled server backoff settles");
        var retryAt = h.Updates.Result.RetryAt!.Value; calls = h.Handler.Calls;
        h.Handler.Status = HttpStatusCode.OK;
        h.SelectChannel(ReleaseChannel.Stable); h.SelectChannel(ReleaseChannel.Beta);
        h.Updates.Tick(now.AddSeconds(361), true);
        Check((h.Updates.Result.Status == ReleaseUpdateStatus.RateLimited || h.Updates.Result.Failure == ReleaseUpdateStatus.RateLimited) && h.Updates.Result.RetryAt == retryAt, "Channel changes preserve visible server backoff");
        h.Updates.Tick(retryAt.AddTicks(-1), true);
        Check(h.Handler.Calls == calls, "A context refresh never bypasses server retry restrictions");
        h.Updates.Tick(retryAt, true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "Context refresh resumes after server backoff expires");
    }
    private static async Task StableLoginAndLogout()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(20000);
        var store = new Store(); using var h = new Harness(store); await h.Ready(); h.SelectChannel(ReleaseChannel.Stable);
        h.Updates.Tick(now, true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "Signed-out Stable checks anonymously");
        Check(h.Handler.StableCalls == 2 && h.Handler.BetaCalls == 0, "Stable identity/list requests do not require Beta sign-in");
        var stable = h.Updates.Result; var generation = h.Updates.Generation;
        await h.Connect(new Input()); h.Updates.Tick(now.AddSeconds(1), true);
        Check(ReferenceEquals(h.Updates.Result, stable) && h.Updates.Generation == generation && h.Handler.Calls == 2, "Beta login preserves the active Stable result, generation and freshness cache");
        h.Updates.Check(now.AddSeconds(60));
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "Stable checks after Beta login");
        Check(h.Session.Status == ReleaseSessionStatus.Connected && h.Handler.StableCalls == 4 && h.Handler.BetaCalls == 0, "Beta login cannot add authorization to Stable requests");
        stable = h.Updates.Result; generation = h.Updates.Generation;
        var auth = h.Authentication; h.Session.SignOut(); h.Updates.Tick(now.AddSeconds(61), true);
        Check(ReferenceEquals(h.Updates.Result, stable) && h.Updates.Generation == generation && h.Handler.Calls == 4, "Beta logout preserves the active Stable result, generation and freshness cache");
        h.Updates.Check(now.AddSeconds(120));
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "Stable checks after Beta logout");
        Check(auth?.CacheIdentity == "" && store.Value == null && h.Handler.StableCalls == 6 && h.Handler.BetaCalls == 0, "Logout retires Beta credentials without disabling anonymous Stable");
        h.SelectChannel(ReleaseChannel.Beta); h.Updates.Tick(now.AddSeconds(121), true);
        Check(h.Updates.Result.Status == ReleaseUpdateStatus.NotConfigured && h.Handler.BetaCalls == 0, "Returning to signed-out Beta cannot make authenticated requests");
    }
    private static async Task ChannelCacheGenerationAndPrivacy()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(20000);
        using var h = new Harness(new Store { Value = Inert }); await h.Ready();
        h.Handler.ReleaseVersion = "2.0.0";
        h.Updates.Tick(now, true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.Available, "Beta release metadata is verified before caching");
        var betaGeneration = h.Updates.Generation;
        h.Updates.AcknowledgeNotification();
        h.SelectChannel(ReleaseChannel.Stable); h.Updates.Tick(now.AddSeconds(1), true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.Available, "First Stable check is immediate and independent of the Beta cooldown");
        h.Updates.AcknowledgeNotification();
        Check(h.Handler.Calls == 4 && h.Handler.BetaCalls == 2 && h.Handler.StableCalls == 2, "Two new channels use exactly one identity/list scan each");
        for (var i = 0; i < 40; i++) {
            var channel = i % 2 == 0 ? ReleaseChannel.Beta : ReleaseChannel.Stable;
            h.SelectChannel(channel); h.Updates.Tick(now.AddSeconds(2), true);
            var release = h.Updates.Result.Release;
            Check(h.Updates.Result.Status == ReleaseUpdateStatus.Available && release is { } found && found.Generation == h.Updates.Generation
                && h.Updates.Generation > betaGeneration && found.FeedChannel == channel.ToString()
                && found.RepositoryId == ReleaseFeedPolicy.For(channel.ToString()).RepositoryId,
                "Rapid channel cache restoration rebinds the verified release to the current feed and generation");
            Check(h.Updates.Result.CheckedAt == (channel == ReleaseChannel.Beta ? now : now.AddSeconds(1))
                && h.Updates.NotificationVersion == null && h.Handler.Calls == 4,
                "Rapid toggles preserve real checked timestamps and acknowledged notifications without extra requests");
        }
        var stable = h.Updates.Result; var stableGeneration = h.Updates.Generation;
        h.Session.SignOut(); h.Updates.Tick(now.AddSeconds(3), true);
        Check(ReferenceEquals(stable, h.Updates.Result) && h.Updates.Generation == stableGeneration && h.Handler.Calls == 4,
            "Logout preserves the visible anonymous Stable cache while invalidating hidden Beta metadata");
        h.SelectChannel(ReleaseChannel.Beta); h.Updates.Tick(now.AddSeconds(4), true);
        Check(h.Updates.Result.Status == ReleaseUpdateStatus.NotConfigured && h.Updates.Result.Release == null && h.Handler.Calls == 4,
            "Signed-out Beta cannot restore its previously cached private release");
        await h.Connect(new Input()); h.Updates.Tick(now.AddSeconds(5), true);
        Check(h.Updates.Result.Status == ReleaseUpdateStatus.RetryWaiting && h.Updates.Result.Release == null
            && h.Updates.Result.RetryAt == now.AddSeconds(60) && h.Handler.Calls == 4,
            "A new Beta session cannot reuse old private metadata or bypass its channel cooldown");
        h.Updates.Tick(now.AddSeconds(60), true);
        await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.Available, "New Beta session re-verifies metadata at its channel deadline");
        Check(h.Handler.Calls == 6 && h.Handler.BetaCalls == 4 && h.Updates.Result.Release?.Generation == h.Updates.Generation,
            "Post-logout Beta cache belongs only to the newly verified session and generation");
    }
    private static async Task DelayedChannelResults()
    {
        foreach (var logout in new[] { false, true }) {
            var now = DateTimeOffset.UnixEpoch.AddDays(20000);
            var store = new Store { Value = Inert }; using var h = new Harness(store); await h.Ready();
            var delay = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); h.Handler.DelayNext = delay;
            try {
                h.Updates.Check(now);
                await Until(() => { }, () => h.Handler.RequestDelayed.Task.IsCompleted, "Beta identity request entered its controlled delay");
                var pending = typeof(ReleaseUpdates).GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.Updates)!;
                var retired = (Task)pending.GetType().GetProperty("Task")!.GetValue(pending)!;
                var generation = h.Updates.Generation;
                if (logout) h.Session.SignOut();
                h.SelectChannel(ReleaseChannel.Stable); h.Updates.Tick(now.AddSeconds(1), true);
                await Until(h.Updates.Poll, () => h.Updates.Result.Status == ReleaseUpdateStatus.UpToDate, "New Stable generation completes while old Beta HTTP is delayed");
                var stable = h.Updates.Result;
                Check(h.Updates.Generation > generation && h.Handler.BetaCalls == 1 && h.Handler.StableCalls == 2, "Channel change retires the old generation and uses anonymous Stable HTTP");
                delay.TrySetResult(true);
                await Until(h.Updates.Poll, () => retired.IsCompleted, "Detached Beta worker completes after cancellation was ignored"); h.Session.Poll();
                Check(ReferenceEquals(h.Updates.Result, stable) && h.Updates.Feed?.Channel == "Stable" && stable.CheckedAt == now.AddSeconds(1), "Late Beta result cannot replace the immediately checked Stable result or timestamp");
                Check(h.Session.Status == (logout ? ReleaseSessionStatus.SignedOut : ReleaseSessionStatus.Connected) && (store.Value == null) == logout, "Late Beta result cannot undo logout or change the retained session");
            } finally { delay.TrySetResult(true); }
        }
    }
    private static void BoundariesAndNative(bool native)
    {
        foreach (var state in new[] { DiagnosticUpdateState.Restoring, DiagnosticUpdateState.StorageFailed })
        {
            var stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            var line = SafeDiagnosticJson.Encode(new SafeDiagnosticEvent { Kind = DiagnosticKind.UpdateStatus, Feature = DiagnosticFeature.Authentication,
                UpdateState = state, Utc = DateTimeOffset.UtcNow, EnqueuedStamp = stamp }, new DiagnosticIdentity("1.0.0-rc.5", new string('A', 64), "6.0.36"), 1, stamp);
            Check(line.Contains("\"update_state\":\"" + state + "\"", StringComparison.Ordinal), "Authentication diagnostics retain the exact new state rather than Unknown");
            Check(OwnedDiagnosticRecord.TryProject(line, out var ownedLine), "Owned strict diagnostic projection accepts the new authentication state");
            Check(MinimalDiagnosticProjection.TryProject(line, out var collectedLine), "Collector strict diagnostic projection accepts the new authentication state");
            Check(!line.Contains(Inert, StringComparison.Ordinal) && !ownedLine.Contains(Inert, StringComparison.Ordinal) && !collectedLine.Contains(Inert, StringComparison.Ordinal), "Authentication diagnostics contain no inert credential");
        }
        using var auth = new MemoryReleaseAuthentication(Inert);
        foreach (var url in new[] { "https://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100", "https://api.github.com/user", "https://release-assets.githubusercontent.com/file", "http://api.github.com/repos/TSM701/BetterAstralPartyBeta" })
        { using var request = new HttpRequestMessage(HttpMethod.Get, url); Check(!auth.TryAuthorize(request) && request.Headers.Authorization == null, "Persistent memory provider cannot send its token to Stable or unrelated endpoints"); }
        using var stableRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/TSM701/BetterAstralParty/releases?per_page=100");
        Check(ReleaseFeedTransport.Authorize(stableRequest, auth, new ReleaseFeed(ReleaseFeedPolicy.StableRepository, 7000007, "Stable")) && stableRequest.Headers.Authorization == null, "Stable feed remains anonymous even with restored Beta authentication");
        Check(WindowsReleaseCredentialStore.TargetName == "BetterAstralParty:GitHub:" + ReleaseFeedPolicy.Beta.Identity, "Production target is scoped to the exact app and Beta repository identity; never opened by this fixture");
        foreach (var target in new[] { "", WindowsReleaseCredentialStore.TargetName, "BetterAstralParty.Test:Beta:" + new string('A', 32), "BetterAstralParty.Test:Beta:" + new string('a', 32) + "\n" })
        { var rejected = false; try { _ = new WindowsReleaseCredentialStore(target); } catch (ArgumentException) { rejected = true; } Check(rejected, "Native test constructor rejects noncanonical or production targets"); }
        if (!native) return;
        var targetName = "BetterAstralParty.Test:Beta:" + Guid.NewGuid().ToString("N");
        var store = new WindowsReleaseCredentialStore(targetName);
        try
        {
            Check(store.Load() == null, "Fresh random native test credential does not exist");
            store.Save(Inert); Check(store.Load() == Inert, "Native vault round-trips only the inert random test entry");
            var rejected = false; try { store.Save("invalid"); } catch (ArgumentException) { rejected = true; }
            Check(rejected && store.Load() == Inert, "Invalid native save cannot replace the existing test credential");
        }
        finally { store.Delete(); Check(store.Load() == null, "Native dummy credential is deleted and cleanup verified"); }
        store.Delete(); Check(store.Load() == null, "Deleting an absent native dummy credential is idempotent");
        Console.WriteLine("Native fixture credential cleanup: verified; production target never opened.");
    }
    private static async Task<int> Main(string[] args)
    {
        Check(args.Length == 0 || args.Length == 1 && args[0] == "--native", "Only the explicit native fixture flag is supported");
        await RestartAndLifetime(); await InputAndStorageFailures(); await LateWorkers(); await RemoteFailures(); await RefreshAfterContextChange(); await StableLoginAndLogout(); await ChannelCacheGenerationAndPrivacy(); await DelayedChannelResults(); BoundariesAndNative(args.Length == 1);
        Console.WriteLine("Persistent-session assertions: " + _checks + " PASS. Inert credentials, injected HTTP only; no real user credential, network, game or installation accessed.");
        return 0;
    }
}
