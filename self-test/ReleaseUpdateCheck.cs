using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BetterAstralParty;

internal static class ReleaseUpdateCheck
{
    private const string Repository = "TSM701/BetterAstralParty";
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(20000);
    private static int _checks;
    private static void Check(bool valid, string message)
    {
        Interlocked.Increment(ref _checks);
        if (!valid) throw new Exception("Release update check: " + message);
    }
    private sealed class Authentication : IReleaseAuthentication
    {
        internal string Identity = "test-account-A";
        internal bool Allow = true, ThrowIdentity;
        internal int IdentityReads, Authorizations;
        public string CacheIdentity
        {
            get { IdentityReads++; return ThrowIdentity ? throw new InvalidOperationException("test") : Identity; }
        }
        public bool TryAuthorize(HttpRequestMessage request)
        {
            Authorizations++;
            if (!Allow) return false;
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "inert-fixture-not-a-credential");
            return true;
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<(string Url, string? ETag, int Thread)> Requests = new();
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply = (_, _) => Task.FromResult(Response("[]"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Enqueue((request.RequestUri!.AbsoluteUri,
                request.Headers.TryGetValues("If-None-Match", out var tags) ? tags.Single() : null, Environment.CurrentManagedThreadId));
            Check(request.Headers.Authorization?.Parameter == "inert-fixture-not-a-credential", "mock requests carry only the fixed inert fixture marker");
            return Reply(request, token);
        }
    }
    private static object Item(string tag, bool draft = false, bool prerelease = false, string notes = "Changes",
        string? url = null) => new { tag_name = tag, draft, prerelease, name = "Release " + tag, body = notes,
            html_url = url ?? "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag) };
    private static byte[] Json(params object[] items) => JsonSerializer.SerializeToUtf8Bytes(items);
    private static HttpResponseMessage Response(string body, HttpStatusCode code = HttpStatusCode.OK,
        string? etag = null, string? next = null)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (etag != null) response.Headers.TryAddWithoutValidation("ETag", etag);
        if (next != null) response.Headers.TryAddWithoutValidation("Link", "<" + next + ">; rel=\"next\"");
        return response;
    }
    private static HttpResponseMessage Releases(params object[] items) => Response(Encoding.UTF8.GetString(Json(items)));
    private static async Task Complete(ReleaseUpdates updates)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (updates.Result.Status == ReleaseUpdateStatus.Checking && DateTime.UtcNow < deadline)
        { updates.Poll(); await Task.Delay(1); }
        updates.Poll();
        Check(updates.Result.Status != ReleaseUpdateStatus.Checking, "mock operation completed");
    }
    internal static void Run()
    {
        FeedIdentityCheck.Run();
        HttpTransportCheck.Run();
        VersionsAndText();
        AsyncChecks().GetAwaiter().GetResult();
        ModText.Select("한국어");
        Console.WriteLine($"Release updates: {_checks} production-code assertions passed (mock HTTP only; no credentials, live GitHub or game UI).");
    }
    private static void VersionsAndText()
    {
        var releases = ReleaseUpdates.Parse(Json(Item("1.9.0"), Item("v1.10.0"), Item("2.0.0-rc.2", prerelease: true),
            Item("2.0.0-rc.10", prerelease: true), Item("9.0.0", draft: true), Item("8.0.0", prerelease: true),
            Item("banana"), Item("0.99.0")), Repository);
        Check(ReleaseUpdates.Select(releases, "1.0.0", ReleaseChannel.Stable)?.Version == "v1.10.0", "numeric stable ordering and flags");
        Check(ReleaseUpdates.Select(releases.Where(r => r.Version != "8.0.0"), "1.0.0", ReleaseChannel.Beta)?.Version == "2.0.0-rc.10", "numeric prerelease ordering");
        Check(releases.Single(r => r.Version == "0.99.0").Beta, "historical 0.x is beta regardless of GitHub flag");
        Check(ReleaseUpdates.Select(releases, "10.0.0", ReleaseChannel.Beta) == null, "no downgrade");
        Check(ReleaseUpdates.Select(ReleaseUpdates.Parse(Json(Item("1.2.3+new")), Repository), "1.2.3+old", ReleaseChannel.Stable) == null, "build metadata ignored");
        Check(ReleaseUpdates.Select(ReleaseUpdates.Parse(Json(Item("1.1.0")), Repository), "1.1.0-dev", ReleaseChannel.Stable)?.Version == "1.1.0", "stable newer than development prerelease");
        foreach (var tag in new[] { "1.0", "01.2.3", "1.2.3-rc.01", "v", "vv1.2.3", "1.2.3 ", "1.2.3/evil", "9999999999999999999999.0.0" })
            Check(!ReleaseUpdates.TryVersion(tag, out _), "invalid SemVer: " + tag);
        foreach (var tag in new[] { "1.2.3", "v1.2.3", "1.2.3-rc.10", "1.2.3+build.1", "1.1.0-dev" })
            Check(ReleaseUpdates.TryVersion(tag, out _), "valid SemVer: " + tag);
        foreach (var url in new[] {
            "http://github.com/TSM701/BetterAstralParty/releases/tag/1.2.3",
            "https://github.com.evil.test/TSM701/BetterAstralParty/releases/tag/1.2.3",
            "https://user@github.com/TSM701/BetterAstralParty/releases/tag/1.2.3",
            "https://github.com/other/BetterAstralParty/releases/tag/1.2.3",
            "https://github.com/TSM701/Other/releases/tag/1.2.3",
            "https://github.com/TSM701/BetterAstralParty/releases/tag/1.2.3?token=test",
            "https://github.com/TSM701/BetterAstralParty/releases/tag/1.2.3#link",
            "https://github.com/TSM701/BetterAstralParty/releases/tag/1.2.3%2Fevil",
            "https://github.com:444/TSM701/BetterAstralParty/releases/tag/1.2.3",
            "javascript:alert(1)", "file:///C:/test", "data:text/html,test" })
        {
            Check(!ReleaseUpdates.TryReleasePage(url, Repository, out _), "unsafe page URL rejected");
            Check(ReleaseUpdates.Parse(Json(Item("1.2.3", url: url)), Repository).Length == 0, "unsafe release omitted");
        }
        Check(ReleaseUpdates.Parse(Json(Item("1.2.3", url: "https://github.com/TSM701/BetterAstralParty/releases/tag/9.0.0")), Repository).Length == 0, "page tag must match response tag");
        Check(ReleaseUpdates.TryReleasePage("https://github.com/TSM701/BetterAstralParty/releases/tag/v1.2.3%2Bbuild", Repository, out _), "escaped legitimate tag");
        var malicious = "<a href='javascript:alert(1)'>literal</a>[url=evil]plain[/url]\n한글\u0000\u202e";
        var text = ReleaseUpdates.Parse(Json(Item("2.0.0", notes: malicious)), Repository).Single();
        Check(text.Notes.Contains("<a") && text.Notes.Contains("[url="), "markup remains literal, not executed or converted into actions");
        Check(!text.Notes.Contains('\0') && !text.Notes.Contains('\u202e'), "unsafe control characters removed");
        Check(ReleaseUpdates.PlainText(new string('x', 5000), ReleaseUpdates.MaxNotes).Length == ReleaseUpdates.MaxNotes, "bounded notes");
        Check(ReleaseUpdates.PlainText("x😀", 2) == "x", "truncation does not split surrogate pair");
        foreach (var language in new[] { "English", "한국어" })
        {
            ModText.Select(language);
            foreach (var status in Enum.GetValues<ReleaseUpdateStatus>())
            {
                var result = new ReleaseUpdateResult(status);
                var shown = ModText.UpdateDetails(result, "1.1.0-dev");
                Check(shown.Contains("1.1.0-dev") && ModText.UpdateStatus(status).Length > 0, "every UI status has current version and wording");
                Check(language != "English" || !shown.Any(c => c is >= '\uac00' and <= '\ud7a3'), "English status translated");
                Check(status != ReleaseUpdateStatus.Failed || !shown.Contains(ModText.UpdateStatus(ReleaseUpdateStatus.UpToDate)), "failure is not up to date");
            }
            var stale = ModText.UpdateDetails(new(ReleaseUpdateStatus.Stale, text, Now, ReleaseUpdateStatus.RateLimited), "1.1.0-dev");
            Check(stale.Contains(ModText.UpdateStatus(ReleaseUpdateStatus.Stale)) && stale.Contains(ModText.UpdateStatus(ReleaseUpdateStatus.RateLimited)), "stale state and failure both visible");
            Check(stale.Contains("1.1.0-dev") && stale.Contains(text.Version) && stale.Contains(Now.UtcDateTime.ToString("u", System.Globalization.CultureInfo.InvariantCulture)), "hover retains current/new versions and checked time");
            Check(!stale.Contains("<a") && !stale.Contains("한글") && !stale.Contains(ModText.Text("변경 사항")), "release notes removed from update hover");
            var popup = ModText.UpdateNotificationDetails(text, "1.1.0-dev", AutomaticUpdateStatus.Queued, ReleaseUpdateStatus.Stale);
            Check(popup.Contains("<a") && popup.Contains("한글") && popup.Contains(ModText.AutomaticStatus(AutomaticUpdateStatus.Queued)), "popup preserves literal release notes alongside current status");
            foreach (var action in new[] { "UpdateChannel", "UpdateCheck", "UpdateDownload" })
                Check(MenuLayout.GeneralControl(action) && !MenuLayout.FixedControl(action), "update controls stay in scrolling general settings");
            foreach (var mode in new[] { "Stable", "Beta" })
                Check(language != "English" || !ModText.Text(MenuLayout.StateHelp("UpdateChannel", true, mode)).Any(c => c is >= '\uac00' and <= '\ud7a3'), "channel help translated");
        }
        var bottom = MenuLayout.ToggleY(8) + MenuLayout.ToggleHeight - MenuLayout.SettingsY;
        var height = MenuLayout.ContentHeight(bottom);
        Check(bottom - (height - MenuLayout.SettingsHeight) <= MenuLayout.SettingsHeight, "last update row is reachable above fixed footer");
        foreach (var invalid in new[] { "{}", "null", "[", "[1]" })
        {
            if (invalid == "[1]") { Check(ReleaseUpdates.Parse(Encoding.UTF8.GetBytes(invalid), Repository).Length == 0, "non-object skipped"); continue; }
            try { ReleaseUpdates.Parse(Encoding.UTF8.GetBytes(invalid), Repository); Check(false, "invalid JSON accepted"); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { Check(true, "invalid response rejected"); }
        }
    }
    private static async Task AsyncChecks()
    {
        await RepositoryPages();
        var handler = new Handler(); using var client = new HttpClient(handler);
        var auth = new Authentication();
        using (var noAuth = new ReleaseUpdates("1.0.0", Repository, client))
        {
            for (var i = 0; i < 20; i++) { noAuth.Check(Now.AddHours(i)); noAuth.Poll(); }
            Check(noAuth.Result.Status == ReleaseUpdateStatus.NotConfigured && handler.Requests.IsEmpty, "unconfigured authentication sends zero HTTP requests");
        }
        using (var denied = new ReleaseUpdates("1.0.0", Repository, client, new Authentication { Allow = false }))
        {
            denied.Check(Now); await Complete(denied);
            Check(denied.Result.Status == ReleaseUpdateStatus.NotConfigured && handler.Requests.IsEmpty, "provider refusal sends zero HTTP requests");
        }
        using (var broken = new ReleaseUpdates("1.0.0", Repository, client, new Authentication { ThrowIdentity = true }))
        {
            broken.Check(Now); broken.Poll();
            Check(broken.Result.Status == ReleaseUpdateStatus.NotConfigured && handler.Requests.IsEmpty, "provider identity failure isolated");
        }
        using (var oversized = new ReleaseUpdates("1.0.0", Repository, client, new Authentication { Identity = new string('x', 257) }))
        {
            oversized.Check(Now); oversized.Poll();
            Check(oversized.Result.Status == ReleaseUpdateStatus.NotConfigured && handler.Requests.IsEmpty,
                "oversized session identity cannot enable a private request");
        }
        handler.Reply = (request, _) =>
        {
            var second = request.RequestUri!.Query.Contains("page=2");
            if (request.Headers.Contains("If-None-Match")) return Task.FromResult(Response("", HttpStatusCode.NotModified));
            return Task.FromResult(Response(Encoding.UTF8.GetString(Json(Item(second ? "2.0.0" : "1.1.0"))),
                etag: second ? "\"page-2\"" : "\"page-1\"",
                next: second ? null : "https://api.github.com/repos/" + Repository + "/releases?per_page=100&page=2"));
        };
        using (var updates = new ReleaseUpdates("1.0.0", Repository, client, auth))
        {
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.Available && updates.Result.Release?.Version == "2.0.0", "all release pages scanned");
            Check(updates.TryDownloadPage(out var page) && page!.Host == "github.com", "validated download page available");
            var count = handler.Requests.Count;
            updates.Check(Now.AddMinutes(10), manual: false); updates.Poll();
            Check(handler.Requests.Count == count, "six-hour cache freshness");
            updates.Check(Now.AddMinutes(11)); await Complete(updates);
            Check(handler.Requests.Count == count + 2 && updates.Result.Release?.Version == "2.0.0", "304 preserves cached pagination when Link is absent");
            Check(handler.Requests.Skip(count).Select(x => x.ETag).SequenceEqual(new[] { "\"page-1\"", "\"page-2\"" }), "ETags belong to exact pages");
            count = handler.Requests.Count;
            updates.Check(Now.AddMinutes(11).AddSeconds(30)); updates.Poll();
            Check(handler.Requests.Count == count, "manual cooldown");
            updates.SetChannel(ReleaseChannel.Beta);
            Check(updates.Result.Status == ReleaseUpdateStatus.NotChecked && !updates.TryDownloadPage(out _), "channel invalidates displayed result");
            updates.Check(Now.AddMinutes(12)); await Complete(updates);
            Check(handler.Requests.Skip(count).All(x => x.ETag == null), "channel invalidates cache context");
            count = handler.Requests.Count;
            auth.Identity = "test-account-B"; updates.Poll();
            Check(updates.Result.Status == ReleaseUpdateStatus.NotChecked && !updates.TryDownloadPage(out _), "account change removes private display");
            updates.Check(Now.AddMinutes(13)); await Complete(updates);
            Check(handler.Requests.Skip(count).All(x => x.ETag == null), "account change removes private cached ETags");
            auth.Identity = new string('x', 257); updates.Poll();
            Check(updates.Result.Status == ReleaseUpdateStatus.NotConfigured && updates.Result.Release == null && updates.NotificationVersion == null,
                "invalid session clears private display and notification");
            auth.Identity = "test-account-C"; updates.Poll();
            count = handler.Requests.Count;
            updates.Check(Now.AddMinutes(14)); await Complete(updates);
            Check(handler.Requests.Skip(count).All(x => x.ETag == null), "recovered session cannot reuse previous private ETags");
            auth.Identity = "";
            updates.Check(Now.AddMinutes(15));
            Check(updates.Result.Status == ReleaseUpdateStatus.NotConfigured && updates.Result.Release == null, "signout clears cached private release");
        }
        await RefreshedPagination();
        await ResponseFailures();
        await CancellationAndTimeout();
        await ThrottleAcrossContexts();
        await ImmediateChannelRefresh();
        await CancelledChannelBackoff();
        await CancellationCallbackFailures();
        await CompletedWorkerFailures();
        await AutomaticChecks();
    }
    private static async Task RepositoryPages()
    {
        void Page(ReleaseUpdates updates, string repository, Authentication? auth = null, Handler? handler = null)
        {
            var result = updates.Result; var revision = updates.Revision; var generation = updates.Generation;
            var identityReads = auth?.IdentityReads; var authorizations = auth?.Authorizations;
            var requests = handler?.Requests.Count;
            Check(updates.RepositoryPage.AbsoluteUri == "https://github.com/" + repository + "/releases",
                "repository page uses the selected official channel, not fetched metadata");
            Check(ReferenceEquals(updates.Result, result) && updates.Revision == revision && updates.Generation == generation,
                "repository page does not change update state or generation");
            Check(auth?.IdentityReads == identityReads && auth?.Authorizations == authorizations && handler?.Requests.Count == requests,
                "repository page does not read authentication or call transport");
        }
        using (var noClient = new ReleaseUpdates("1.0.0", "OTHER/Untrusted"))
        {
            Check(noClient.Result.Status == ReleaseUpdateStatus.NotConfigured, "repository page fixture has no client or authentication");
            foreach (var channel in new[] { ReleaseChannel.Stable, ReleaseChannel.Beta, ReleaseChannel.Beta, ReleaseChannel.Stable, ReleaseChannel.Beta, ReleaseChannel.Stable })
            {
                noClient.SetChannel(channel);
                Page(noClient, channel == ReleaseChannel.Stable ? Repository : "TSM701/BetterAstralPartyBeta");
            }
        }
        using (var signedOutHandler = new Handler())
        using (var signedOutClient = new HttpClient(signedOutHandler))
        using (var signedOut = new ReleaseUpdates("1.0.0", BetterAstralParty.Updating.ReleaseFeedPolicy.Beta, signedOutClient))
        {
            Check(signedOut.Result.Status == ReleaseUpdateStatus.NotConfigured, "signed-out Beta remains unconfigured for updates");
            Page(signedOut, "TSM701/BetterAstralPartyBeta", handler: signedOutHandler);
            Check(signedOutHandler.Requests.IsEmpty, "signed-out Beta repository page needs no API request");
        }
        foreach (var status in new[] { ReleaseUpdateStatus.Available, ReleaseUpdateStatus.UpToDate, ReleaseUpdateStatus.Failed })
        {
            using var handler = new Handler(); using var client = new HttpClient(handler);
            var auth = new Authentication();
            handler.Reply = (_, _) => Task.FromResult(status == ReleaseUpdateStatus.Failed
                ? Response("", HttpStatusCode.InternalServerError)
                : status == ReleaseUpdateStatus.Available ? Releases(Item("2.0.0")) : Releases());
            using var updates = new ReleaseUpdates("1.0.0", Repository, client, auth);
            Page(updates, Repository, auth, handler);
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == status, "repository page fixture reaches " + status);
            auth.ThrowIdentity = true;
            Page(updates, Repository, auth, handler);
        }
    }
    private static async Task RefreshedPagination()
    {
        var round = 0;
        var handler = new Handler();
        handler.Reply = (request, _) =>
        {
            var first = !request.RequestUri!.Query.Contains("&page=");
            if (first && round == 0)
                return Task.FromResult(Response(Encoding.UTF8.GetString(Json(Item("1.1.0"))), etag: "\"first\"",
                    next: "https://api.github.com/repos/" + Repository + "/releases?per_page=100&page=2"));
            if (first)
                return Task.FromResult(Response("", HttpStatusCode.NotModified, etag: "\"refreshed\"",
                    next: round == 1 ? "https://api.github.com/repos/" + Repository + "/releases?per_page=100&page=3" : null));
            if (round == 2) return Task.FromResult(Response("", HttpStatusCode.NotModified));
            return Task.FromResult(Response(Encoding.UTF8.GetString(Json(Item(round == 0 ? "2.0.0" : "3.0.0"))), etag: "\"next\""));
        };
        using var client = new HttpClient(handler);
        using var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication());
        for (round = 0; round < 3; round++)
        {
            updates.Check(Now.AddMinutes(round * 2)); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.Available && updates.Result.Release?.Version == (round == 0 ? "2.0.0" : "3.0.0"),
                "304 updated pagination survives later responses without Link");
        }
        var final = handler.Requests.Skip(4).ToArray();
        Check(final.Length == 2 && final[0].ETag == "\"refreshed\"" && final[1].Url.EndsWith("&page=3") && final[1].ETag == "\"next\"",
            "304 refreshed ETag and Next belong to the retained page snapshot");
    }
    private static async Task ResponseFailures()
    {
        foreach (var (code, expected) in new[] {
            (HttpStatusCode.Unauthorized, ReleaseUpdateStatus.AuthenticationRequired),
            (HttpStatusCode.Forbidden, ReleaseUpdateStatus.AccessUnavailable),
            (HttpStatusCode.NotFound, ReleaseUpdateStatus.AccessUnavailable),
            (HttpStatusCode.TooManyRequests, ReleaseUpdateStatus.RateLimited),
            (HttpStatusCode.InternalServerError, ReleaseUpdateStatus.Failed) })
        {
            var handler = new Handler { Reply = (_, _) => Task.FromResult(Response("private error body", code)) };
            using var client = new HttpClient(handler); using var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication());
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == expected && updates.Result.Release == null, "status classification: " + code);
        }
        foreach (var body in new[] { "{", "{}", new string('x', ReleaseUpdates.MaxBytes + 1) })
        {
            var handler = new Handler { Reply = (_, _) => Task.FromResult(Response(body)) };
            using var client = new HttpClient(handler); using var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication());
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.Failed, "malformed/oversized response fails");
        }
        foreach (var next in new[] {
            "https://evil.test/releases?per_page=100&page=2",
            "https://api.github.com/repos/other/repo/releases?per_page=100&page=2",
            "https://api.github.com/repos/" + Repository + "/releases?token=test&page=2",
            "https://api.github.com/repos/" + Repository + "/releases?per_page=100&page=1" })
        {
            var handler = new Handler { Reply = (_, _) => Task.FromResult(Response("[]", next: next)) };
            using var client = new HttpClient(handler); using var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication());
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.Incomplete && handler.Requests.Count <= 2, "unsafe/cyclic pagination is bounded");
        }
        var paging = new Handler(); var pageNumber = 0;
        paging.Reply = (_, _) => Task.FromResult(Response("[]", next: "https://api.github.com/repos/" + Repository + "/releases?per_page=100&page=" + Interlocked.Increment(ref pageNumber)));
        using (var client = new HttpClient(paging))
        using (var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication()))
        {
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.Incomplete && paging.Requests.Count == ReleaseUpdates.MaxPages, "page budget never claims up to date");
        }
        var handler304 = new Handler { Reply = (_, _) => Task.FromResult(Response("", HttpStatusCode.NotModified)) };
        using (var client = new HttpClient(handler304))
        using (var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication()))
        {
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.Failed && handler304.Requests.Count == 2, "304 without cached body gets one bounded unconditional retry");
        }
        var staleHandler = new Handler { Reply = (_, _) => Task.FromResult(Releases(Item("2.0.0"))) };
        using (var client = new HttpClient(staleHandler))
        using (var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication()))
        {
            updates.Check(Now); await Complete(updates);
            staleHandler.Reply = (_, _) => throw new HttpRequestException("offline/TLS mock");
            updates.Check(Now.AddMinutes(2)); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.Stale && updates.Result.CheckedAt == Now
                && updates.Result.Release?.Version == "2.0.0" && updates.Result.Failure == ReleaseUpdateStatus.Failed, "network failure retains timestamped stale result");
            staleHandler.Reply = (_, _) => Task.FromResult(Response("", HttpStatusCode.Unauthorized));
            updates.Check(Now.AddMinutes(4)); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.AuthenticationRequired && !updates.TryDownloadPage(out _), "auth failure removes prior private release");
        }
        var rate = new Handler();
        rate.Reply = (_, _) =>
        {
            var response = Response("", HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(4));
            return Task.FromResult(response);
        };
        using (var client = new HttpClient(rate))
        using (var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication()))
        {
            updates.Check(Now); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.RateLimited && updates.Result.RetryAt == Now.AddMinutes(4), "403 rate headers distinguished from access failure");
            updates.Check(Now.AddMinutes(2)); updates.Poll(); Check(rate.Requests.Count == 1, "Retry-After suppresses manual repeat");
            rate.Reply = (_, _) => Task.FromResult(Response("[]"));
            updates.Check(Now.AddMinutes(5)); await Complete(updates);
            Check(updates.Result.Status == ReleaseUpdateStatus.UpToDate && rate.Requests.Count == 2, "rate limit expires");
        }
    }
    private static async Task CancellationAndTimeout()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler { Reply = async (_, _) => { entered.TrySetResult(); await gate.Task; return Releases(Item("9.0.0")); } };
        using var client = new HttpClient(handler); var auth = new Authentication();
        using var updates = new ReleaseUpdates("1.0.0", Repository, client, auth);
        updates.Check(Now); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) { updates.Check(Now); updates.Poll(); }
        Check(watch.Elapsed < TimeSpan.FromSeconds(1) && handler.Requests.Count == 1, "Check/Poll never waits on pending I/O and is single-flight");
        updates.Cancel();
        Check(updates.Result.Status == ReleaseUpdateStatus.Cancelled, "explicit cancellation state");
        auth.Identity = "test-account-B"; updates.Poll();
        handler.Reply = (_, _) => Task.FromResult(Releases(Item("2.0.0")));
        updates.Check(Now.AddMinutes(2)); await Complete(updates);
        gate.SetResult(); await Task.Delay(20); updates.Poll();
        Check(updates.Result.Release?.Version == "2.0.0", "late cancelled/old-account completion cannot overwrite new result");
        updates.SetChannel(ReleaseChannel.Beta);
        Check(updates.Result.Release == null, "channel change clears old completion");
        foreach (var change in new[] { "channel", "provider", "signout", "dispose" })
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var raceHandler = new Handler { Reply = async (_, _) => { started.TrySetResult(); await finish.Task; return Releases(Item("9.0.0")); } };
            using var raceClient = new HttpClient(raceHandler); var raceAuth = new Authentication();
            using var race = new ReleaseUpdates("1.0.0", Repository, raceClient, raceAuth);
            race.Check(Now); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (change)
            {
                case "channel": race.SetChannel(ReleaseChannel.Beta); break;
                case "provider": race.Configure(raceClient, new Authentication { Identity = "test-account-C" }); break;
                case "signout": raceAuth.Identity = ""; race.Poll(); break;
                case "dispose": race.Dispose(); break;
            }
            raceHandler.Reply = (_, _) => Task.FromResult(Releases(Item("2.0.0")));
            race.Check(Now.AddMinutes(2)); await Complete(race);
            var result = race.Result;
            finish.SetResult(); await Task.Delay(20); race.Poll();
            Check(race.Result == result && race.Result.Release?.Version != "9.0.0", "late completion after " + change + " discarded");
            Check(change is not ("signout" or "dispose") || raceHandler.Requests.Count == 1, "no request after " + change);
        }
        using var timeoutHandler = new Handler { Reply = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Response("[]"); } };
        using var timeoutClient = new HttpClient(timeoutHandler);
        using var timeout = new ReleaseUpdates("1.0.0", Repository, timeoutClient, new Authentication(), TimeSpan.FromMilliseconds(150));
        timeout.Check(Now); await Complete(timeout);
        Check(timeout.Result.Status == ReleaseUpdateStatus.Failed, "request-header timeout handled");
        using var bodyHandler = new Handler { Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream()) }) };
        using var bodyClient = new HttpClient(bodyHandler);
        using var bodyTimeout = new ReleaseUpdates("1.0.0", Repository, bodyClient, new Authentication(), TimeSpan.FromMilliseconds(150));
        bodyTimeout.Check(Now); await Complete(bodyTimeout);
        Check(bodyTimeout.Result.Status == ReleaseUpdateStatus.Failed, "total timeout includes response-body reading");
        var count = handler.Requests.Count;
        updates.Dispose(); updates.Check(Now.AddHours(8)); updates.Poll();
        Check(handler.Requests.Count == count && updates.Result.Release == null, "dispose cancels and clears private state");
    }
    private static async Task ThrottleAcrossContexts()
    {
        foreach (var change in new[] { "channel", "account", "provider" })
        foreach (var limited in new[] { false, true })
        {
            var handler = new Handler();
            handler.Reply = (_, _) =>
            {
                var response = Response("[]", limited ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
                if (limited) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(4));
                return Task.FromResult(response);
            };
            using var client = new HttpClient(handler); var auth = new Authentication();
            using var updates = new ReleaseUpdates("1.0.0", Repository, client, auth);
            updates.Check(Now); await Complete(updates);
            switch (change)
            {
                case "channel": updates.SetChannel(ReleaseChannel.Beta); updates.SetChannel(ReleaseChannel.Stable); break;
                case "account": auth.Identity = "test-account-B"; updates.Poll(); break;
                case "provider": updates.Configure(client, new Authentication()); break;
            }
            Check(!limited || updates.Result.Status == ReleaseUpdateStatus.RateLimited && updates.Result.RetryAt == Now.AddMinutes(4), "backoff remains visible after " + change);
            updates.Check(Now.AddSeconds(1)); updates.Tick(Now.AddSeconds(1), true);
            Check(handler.Requests.Count == 1, "channel/account/provider cannot bypass " + (limited ? "Retry-After" : "cooldown"));
            updates.Tick(limited ? Now.AddMinutes(3) : Now.AddSeconds(59), true);
            Check(handler.Requests.Count == 1, "automatic check also respects retained limit");
            updates.Tick(limited ? Now.AddMinutes(4) : Now.AddSeconds(60), true); await Complete(updates);
            Check(handler.Requests.Count == 2, "automatic pending context check starts when retained limit expires");
        }
    }
    private static async Task ImmediateChannelRefresh()
    {
        var handler = new Handler { Reply = (_, _) => Task.FromResult(Response(
            Encoding.UTF8.GetString(Json(Item("1.1.0"), Item("2.0.0-rc.1", prerelease: true))), etag: "\"verified-page\"")) };
        using var client = new HttpClient(handler); var auth = new Authentication();
        using var updates = new ReleaseUpdates("1.0.0", Repository, client, auth);
        updates.Tick(Now, true); await Complete(updates);
        var checkedAt = updates.Result.CheckedAt;
        updates.AcknowledgeNotification();
        updates.SetChannel(ReleaseChannel.Beta); updates.Tick(Now.AddSeconds(1), true); await Complete(updates);
        Check(handler.Requests.Count == 2 && updates.Result.Release?.Version == "2.0.0-rc.1", "first other channel checks immediately without the previous channel cooldown");
        updates.AcknowledgeNotification();
        updates.SetChannel(ReleaseChannel.Stable);
        Check(updates.Result.Release?.Version == "1.1.0" && updates.Result.Release.Generation == updates.Generation
            && updates.Result.CheckedAt == checkedAt, "returning channel restores its verified result with current generation and original check time");
        for (var i = 0; i < 100; i++)
        {
            updates.SetChannel(i % 2 == 0 ? ReleaseChannel.Beta : ReleaseChannel.Stable);
            updates.Tick(Now.AddSeconds(2), true);
            Check(updates.Result.Status == ReleaseUpdateStatus.Available && updates.NotificationVersion == null,
                "rapid channel return stays usable without replaying acknowledged notifications");
        }
        Check(handler.Requests.Count == 2, "100 rapid channel toggles reuse two verified scans rather than flooding HTTP");
        updates.Tick(Now.AddSeconds(59), true);
        Check(handler.Requests.Count == 2 && updates.Result.CheckedAt == checkedAt, "channel result remains cached before its own 60-second boundary");
        updates.Tick(Now.AddSeconds(60), true); await Complete(updates);
        Check(handler.Requests.Count == 3 && updates.Result.CheckedAt == Now.AddSeconds(60), "queued channel refresh is not swallowed by six-hour home freshness");
        Check(handler.Requests.Last().ETag == "\"verified-page\"", "channel roundtrip preserves the exact verified page ETag for the next refresh");
        updates.SetChannel(ReleaseChannel.Beta); updates.Tick(Now.AddSeconds(61), true); await Complete(updates);
        Check(handler.Requests.Count == 4, "other channel refresh has its own 60-second boundary");
        updates.SetChannel(ReleaseChannel.Stable); auth.Identity = "test-account-B"; updates.Poll();
        updates.SetChannel(ReleaseChannel.Beta); updates.Tick(Now.AddSeconds(62), true);
        Check(updates.Result.Release == null && updates.Result.Status == ReleaseUpdateStatus.RetryWaiting && handler.Requests.Count == 4,
            "account replacement cannot revive a hidden channel result or bypass its cooldown");
        updates.Tick(Now.AddSeconds(121), true); await Complete(updates);
        Check(handler.Requests.Count == 5 && handler.Requests.Last().ETag == null, "new account checks without old private ETags after retained cooldown");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Reply = async (_, _) => { entered.TrySetResult(); await finish.Task; return Releases(Item("9.0.0")); };
        try
        {
            updates.Check(Now.AddSeconds(181)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            updates.SetChannel(ReleaseChannel.Stable); updates.SetChannel(ReleaseChannel.Beta);
            Check(updates.Result.Release == null, "an interrupted newer scan cannot revive the channel's earlier success as current");
        }
        finally { finish.TrySetResult(); }

        foreach (var failure in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
        {
            using var failedHandler = new Handler { Reply = (_, _) => Task.FromResult(Releases(Item("2.0.0-rc.1", prerelease: true))) };
            using var failedClient = new HttpClient(failedHandler);
            using var failed = new ReleaseUpdates("1.0.0", Repository, failedClient, new Authentication());
            failed.SetChannel(ReleaseChannel.Beta); failed.Tick(Now, true); await Complete(failed);
            failedHandler.Reply = (_, _) => Task.FromResult(Response("[]", failure));
            failed.Check(Now.AddSeconds(60)); await Complete(failed);
            failed.SetChannel(ReleaseChannel.Stable); failed.SetChannel(ReleaseChannel.Beta);
            Check(failed.Result.Release == null, "failed/auth-rejected channel cannot resurrect a previous successful snapshot: " + failure);
        }
    }
    private static async Task CancelledChannelBackoff()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler { Reply = async (_, _) =>
        {
            entered.TrySetResult(); await finish.Task;
            var response = Response("[]", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(4));
            returned.TrySetResult(); return response;
        } };
        using var client = new HttpClient(handler);
        using var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication());
        updates.Tick(Now, true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        updates.SetChannel(ReleaseChannel.Beta); finish.TrySetResult();
        await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The deliberately cancellation-ignoring old transport must publish its server backoff first.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            updates.Poll();
            if ((DateTimeOffset)typeof(ReleaseUpdates).GetField("_retryAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(updates)! == Now.AddMinutes(4)) break;
            await Task.Delay(1);
        }
        updates.Tick(Now.AddSeconds(1), true);
        Check(handler.Requests.Count == 1 && updates.Result.Status == ReleaseUpdateStatus.RateLimited && updates.Result.RetryAt == Now.AddMinutes(4),
            "late server backoff from a cancelled channel remains global before the new channel starts");
        handler.Reply = (_, _) => Task.FromResult(Response("[]"));
        updates.SetChannel(ReleaseChannel.Stable); updates.SetChannel(ReleaseChannel.Beta);
        updates.Tick(Now.AddMinutes(3), true);
        Check(handler.Requests.Count == 1 && updates.Result.Status == ReleaseUpdateStatus.RateLimited, "channel switching cannot hide server Retry-After");
        updates.Tick(Now.AddMinutes(4), true); await Complete(updates);
        Check(handler.Requests.Count == 2 && updates.Result.Status == ReleaseUpdateStatus.UpToDate, "global server wait expires normally");
    }
    private static async Task CancellationCallbackFailures()
    {
        foreach (var change in new[] { "cancel", "channel", "account", "provider", "dispose" })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler { Reply = async (_, token) =>
            {
                using var registration = token.Register(() => throw new InvalidOperationException("mock cancellation callback"));
                entered.TrySetResult(); await gate.Task; return Releases(Item("9.0.0"));
            } };
            using var client = new HttpClient(handler); var auth = new Authentication();
            using var updates = new ReleaseUpdates("1.0.0", Repository, client, auth);
            updates.Check(Now); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (change)
            {
                case "cancel": updates.Cancel(); break;
                case "channel": updates.SetChannel(ReleaseChannel.Beta); break;
                case "account": auth.Identity = "test-account-B"; updates.Poll(); break;
                case "provider": updates.Configure(client, new Authentication()); break;
                case "dispose": updates.Dispose(); break;
            }
            Check(updates.Result.Status != ReleaseUpdateStatus.Checking, "throwing cancellation callback isolated during " + change);
            handler.Reply = (_, _) => Task.FromResult(Releases(Item("2.0.0")));
            updates.Check(Now.AddMinutes(2)); await Complete(updates);
            var result = updates.Result;
            gate.TrySetResult(); await Task.Delay(20); updates.Poll();
            Check(updates.Result == result && updates.Result.Release?.Version != "9.0.0", "detached cancellation result cannot return after " + change);
            Check(change == "dispose" || updates.Result.Release?.Version == "2.0.0", "next check succeeds after throwing callback");
        }
    }
    private static async Task CompletedWorkerFailures()
    {
        foreach (var priorSuccess in new[] { false, true })
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler { Reply = (_, _) => Task.FromResult(Releases(Item("2.0.0"))) };
            using var client = new HttpClient(handler);
            using var updates = new ReleaseUpdates("1.0.0", Repository, client, new Authentication());
            if (priorSuccess) { updates.Check(Now); await Complete(updates); }
            handler.Reply = async (_, _) => { await gate.Task; return Releases(Item("9.0.0")); };
            updates.Check(Now.AddMinutes(2));
            // Inject an unexpectedly faulted worker without adding a production test hook.
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var pending = typeof(ReleaseUpdates).GetField("_pending", flags)!.GetValue(updates)!;
            var taskProperty = pending.GetType().GetProperty("Task")!;
            var scanType = taskProperty.PropertyType.GetGenericArguments()[0];
            var fromException = typeof(Task).GetMethods().Single(m => m.Name == "FromException" && m.IsGenericMethodDefinition).MakeGenericMethod(scanType);
            taskProperty.SetValue(pending, fromException.Invoke(null, new object[] { new InvalidOperationException("mock unexpected worker fault") }));
            updates.Poll();
            Check(updates.Result.Status == (priorSuccess ? ReleaseUpdateStatus.Stale : ReleaseUpdateStatus.Failed), "completed worker fault becomes failure without escaping Poll");
            Check(!priorSuccess || updates.Result.CheckedAt == Now && updates.Result.Failure == ReleaseUpdateStatus.Failed, "worker fault preserves original stale timestamp");
            Check(updates.NotificationVersion == null, "worker fault clears unread marker");
            gate.TrySetResult(); await Task.Delay(20); updates.Poll();
            Check(updates.Result.Release?.Version != "9.0.0", "late replaced worker cannot overwrite failure");
            updates.FrameFailed();
            Check(updates.Result.Status != ReleaseUpdateStatus.Checking, "last frame failure boundary leaves a settled state");
        }
    }
    private static async Task AutomaticChecks()
    {
        var handler = new Handler(); using var client = new HttpClient(handler);
        using (var noAuth = new ReleaseUpdates("1.0.0", Repository, client))
        {
            for (var i = 0; i < 1000; i++) noAuth.Tick(Now.AddSeconds(i), i % 2 == 0);
            Check(handler.Requests.IsEmpty && noAuth.NotificationVersion == null && noAuth.Result.Status == ReleaseUpdateStatus.NotConfigured,
                "unconfigured repeated menu entries send HTTP zero and create no notice");
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Reply = async (_, _) => { entered.TrySetResult(); await gate.Task; return Releases(Item("2.0.0")); };
        var auth = new Authentication(); using var updates = new ReleaseUpdates("1.0.0", Repository, client, auth);
        updates.Tick(Now, false);
        Check(handler.Requests.IsEmpty && updates.Result.Status == ReleaseUpdateStatus.NotChecked, "automatic check waits for home readiness");
        updates.Tick(Now, true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) updates.Tick(Now, true);
        Check(watch.Elapsed < TimeSpan.FromSeconds(1) && handler.Requests.Count == 1, "automatic entry trigger is nonblocking and single-flight");
        gate.TrySetResult(); await Complete(updates);
        Check(updates.Result.Status == ReleaseUpdateStatus.Available && updates.NotificationVersion == "2.0.0", "Available creates automatic home notice");
        updates.AcknowledgeNotification();
        Check(updates.NotificationVersion == null && updates.Result.Release?.Version == "2.0.0", "reading notice clears marker but keeps release details");
        handler.Reply = (_, _) => Task.FromResult(Releases(Item("v2.0.0+rebuild")));
        updates.Check(Now.AddMinutes(2)); await Complete(updates);
        Check(updates.NotificationVersion == null, "leading v/build metadata cannot repeat acknowledged notice");
        updates.Tick(Now.AddMinutes(10), false); updates.Tick(Now.AddMinutes(10), true);
        Check(handler.Requests.Count == 2, "menu reentry reuses six-hour freshness");
        updates.SetChannel(ReleaseChannel.Beta); updates.Tick(Now.AddMinutes(11), true); await Complete(updates);
        Check(updates.NotificationVersion == null && handler.Requests.Count == 3, "new channel context auto-checks without repeating same version");
        handler.Reply = (_, _) => Task.FromResult(Releases(Item("3.0.0")));
        updates.Tick(Now.AddHours(7), false); updates.Tick(Now.AddHours(7), true); await Complete(updates);
        Check(updates.NotificationVersion == "3.0.0" && handler.Requests.Count == 4, "later home reentry detects newer release after freshness expires");
        var count = handler.Requests.Count;
        auth.Identity = ""; updates.Tick(Now.AddHours(7).AddSeconds(1), true);
        Check(updates.Result.Status == ReleaseUpdateStatus.NotConfigured && updates.NotificationVersion == null && handler.Requests.Count == count, "signout clears private notice without HTTP");
        auth.Identity = "test-account-B";
        updates.Tick(Now.AddHours(7).AddSeconds(2), true);
        Check(handler.Requests.Count == count, "account switch waits for shared cooldown");
        updates.Tick(Now.AddHours(7).AddMinutes(1), true); await Complete(updates);
        Check(updates.NotificationVersion == "3.0.0" && handler.Requests.Count == count + 1, "new account context checks automatically and owns its notice");
        handler.Reply = (_, _) => Task.FromResult(Response("[]"));
        updates.Check(Now.AddHours(7).AddMinutes(3)); await Complete(updates);
        Check(updates.Result.Status == ReleaseUpdateStatus.UpToDate && updates.NotificationVersion == null, "release removal/up-to-date result clears unread marker");
        updates.Dispose(); count = handler.Requests.Count;
        updates.Tick(Now.AddHours(20), true);
        Check(handler.Requests.Count == count && updates.NotificationVersion == null, "disposed automatic trigger stays inactive");
        foreach (var language in new[] { "한국어", "English" })
        {
            ModText.Select(language);
            var label = ModText.Text("모드 설정") + " · " + ModText.Text("새 버전");
            Check(language != "English" || !label.Any(c => c is >= '\uac00' and <= '\ud7a3'), "automatic home title localized");
        }
    }
    private sealed class WaitingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, token); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
