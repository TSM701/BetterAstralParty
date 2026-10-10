using BetterAstralParty.Observability;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SemVersion = SemanticVersioning.Version;
using BetterAstralParty.Updating;

namespace BetterAstralParty;

// The private authentication implementation is deliberately absent. Never reuse connector credentials.
internal interface IReleaseAuthentication
{
    // Nonsecret account/session identity from memory only; never block or refresh credentials here.
    string CacheIdentity { get; }
    bool TryAuthorize(HttpRequestMessage request);
}

internal enum ReleaseChannel { Stable, Beta }
internal enum ReleaseUpdateStatus
{
    NotConfigured, NotChecked, Checking, UpToDate, Available, AuthenticationRequired,
    AccessUnavailable, RateLimited, Failed, Stale, Incomplete, Cancelled, RetryWaiting
}
internal sealed record ReleaseUpdate(string Version, string Title, string Notes, Uri Page)
{
    internal bool Beta { get; init; }
    internal long ReleaseId { get; init; }
    internal long ZipAssetId { get; init; }
    internal long DescriptorAssetId { get; init; }
    internal long SignatureAssetId { get; init; }
    internal string Repository { get; init; } = "";
    internal long RepositoryId { get; init; }
    internal string FeedChannel { get; init; } = "";
    internal int Generation { get; init; }
    internal bool TransitionCandidate { get; init; }
    internal Updating.UpdateDownloadRequest? DownloadRequest(string current, ReleaseChannel channel, ChannelTransitionIntent? transition = null)
    {
        if (ReleaseId <= 0 || ZipAssetId <= 0 || DescriptorAssetId <= 0 || SignatureAssetId <= 0) return null;
        if (channel == ReleaseChannel.Stable && Beta) return null;
        if (TransitionCandidate && transition == null) return null;
        // Beta selection includes stable releases; verification follows the selected release's channel.
        var repository = Repository.Length == 0 ? "TSM701/BetterAstralParty" : Repository;
        var actualChannel = FeedChannel.Length == 0 ? Beta ? "Beta" : "Stable" : FeedChannel;
        if (FeedChannel.Length != 0 && actualChannel != channel.ToString()) return null;
        return new(new Updating.UpdateContext(repository, actualChannel, current, Version, ReleaseId, ZipAssetId, Updating.UpdateTrust.PlatformId, repositoryId: RepositoryId, transition: transition), DescriptorAssetId, SignatureAssetId);
    }
    internal Updating.UpdateDownloadRequest? ProposalRequest(string current)
        => FeedChannel is "Stable" or "Beta" && RepositoryId > 0 && ReleaseId > 0 && ZipAssetId > 0 && DescriptorAssetId > 0 && SignatureAssetId > 0
        ? new(new UpdateContext(Repository, FeedChannel, current, Version, ReleaseId, ZipAssetId, UpdateTrust.PlatformId, repositoryId: RepositoryId), DescriptorAssetId, SignatureAssetId) : null;
}
internal sealed record ReleaseUpdateResult(ReleaseUpdateStatus Status, ReleaseUpdate? Release = null,
    DateTimeOffset? CheckedAt = null, ReleaseUpdateStatus? Failure = null, DateTimeOffset? RetryAt = null);

// Main-thread-owned state; workers return immutable results and private page snapshots only.
internal sealed class ReleaseUpdates : IDisposable
{
    internal const int MaxBytes = 1024 * 1024, MaxPages = 4, MaxNotes = 4096;
    private sealed record Page(byte[] Body, string? ETag, Uri? Next);
    private sealed record Scan(ReleaseUpdateResult Result, Dictionary<string, Page>? Pages = null);
    private sealed record Pending(Task<Scan> Task, CancellationTokenSource Cancellation, int Generation, string Identity);
    private sealed class CheckFailure : Exception
    {
        internal readonly ReleaseUpdateStatus Status;
        internal readonly DateTimeOffset? RetryAt;
        internal CheckFailure(ReleaseUpdateStatus status, DateTimeOffset? retryAt = null)
        { Status = status; RetryAt = retryAt; }
    }
    private readonly string _current;
    private string _repository;
    private Uri _endpoint;
    private ReleaseFeed? _feed;
    private readonly TimeSpan _timeout;
    private HttpClient? _client;
    private HttpClient? _ownedClient;
    private IReleaseAuthentication? _authentication;
    private string _identity = "";
    private Dictionary<string, Page> _pages = new(StringComparer.Ordinal);
    private Pending? _pending;
    private ReleaseUpdateResult? _lastSuccess;
    private readonly Dictionary<ReleaseChannel, DateTimeOffset> _lastStarted = new();
    private readonly Dictionary<ReleaseChannel, (string Identity, Scan Scan)> _channelCache = new();
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private long _serverRetryTicks;
    private int _generation;
    private bool _disposed;
    private bool _menuWasAvailable, _autoDue = true;
    private bool _transitionScan;
    private bool _channelRefresh;
    private readonly HashSet<string> _notifiedVersions = new(StringComparer.Ordinal);
    internal ReleaseChannel Channel { get; private set; }
    internal ReleaseUpdateResult Result { get; private set; } = new(ReleaseUpdateStatus.NotConfigured);
    internal string? NotificationVersion { get; private set; }
    internal int Revision { get; private set; }
    internal int Generation => _generation;
    internal string FeedIdentity => _feed?.Identity ?? _repository;
    internal ReleaseFeed? Feed => _feed;

    internal ReleaseUpdates(string current, string repository, HttpClient? client = null,
        IReleaseAuthentication? authentication = null, TimeSpan? timeout = null)
    {
        if (!TryVersion(current, out _)) throw new ArgumentException("Invalid current version", nameof(current));
        if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}\z"))
            throw new ArgumentException("Invalid repository", nameof(repository));
        _current = current; _repository = repository;
        _endpoint = new Uri("https://api.github.com/repos/" + repository + "/releases?per_page=100");
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        Configure(client, authentication);
    }

    internal ReleaseUpdates(string current, ReleaseFeed feed, HttpClient? client = null, IReleaseAuthentication? authentication = null, TimeSpan? timeout = null)
        : this(current, feed.Repository, client, null, timeout)
    {
        _feed = feed; Channel = feed.Channel == "Beta" ? ReleaseChannel.Beta : ReleaseChannel.Stable; Configure(client, authentication);
    }
    // Stable is anonymous; Beta uses only the memory provider on a verified private identity.
    internal static ReleaseUpdates Production(string current, string repository, IReleaseAuthentication? authentication = null)
        => Production(current, repository == ReleaseFeedPolicy.StableRepository ? ReleaseChannel.Stable
            : repository == ReleaseFeedPolicy.BetaRepository ? ReleaseChannel.Beta : throw new ArgumentException("Unknown production repository"), authentication);
    internal static ReleaseUpdates Production(string current, ReleaseChannel channel = ReleaseChannel.Stable, IReleaseAuthentication? authentication = null)
    {
        var client = ReleaseHttpClient.Create();
        try
        {
            var updates = new ReleaseUpdates(current, ReleaseFeedPolicy.For(channel.ToString()), client, authentication);
            updates._ownedClient = client;
            return updates;
        }
        catch { client.Dispose(); throw; }
    }
    internal void ConfigureAuthentication(IReleaseAuthentication? authentication)
    {
        if (_disposed) return;
        if (_ownedClient == null) throw new InvalidOperationException("A production transport is required");
        _channelCache.Remove(ReleaseChannel.Beta); // Signing out while Stable is selected must retire hidden Beta metadata too.
        if (_feed is { RequiresAuthentication: false }) { _authentication = authentication; return; }
        Configure(_ownedClient, authentication);
    }
    // Injected clients are controlled fixtures only. A production instance cannot replace its transport.
    internal void Configure(HttpClient? client, IReleaseAuthentication? authentication)
    {
        if (_disposed) return;
        if (_ownedClient != null && !ReferenceEquals(client, _ownedClient))
            throw new InvalidOperationException("The production transport cannot be replaced");
        Clear(); _channelCache.Clear(); _client = client; _authentication = authentication;
        var previousIdentity = _identity;
        _identity = AuthIdentity;
        if (previousIdentity != _identity) _notifiedVersions.Clear();
        Set(ReadyState());
    }
    private bool Configured => !_disposed && _client != null && _identity.Length != 0 && (_feed == null || _feed.Configured)
        && (_feed is { RequiresAuthentication: false } || _authentication != null);
    private ReleaseUpdateResult ReadyState() => !Configured ? new(ReleaseUpdateStatus.NotConfigured)
        : _retryAt != DateTimeOffset.MinValue ? new(ReleaseUpdateStatus.RateLimited, RetryAt: _retryAt)
        : new(ReleaseUpdateStatus.NotChecked);
    private string AuthIdentity
    {
        get { if (_feed is { RequiresAuthentication: false }) return _feed.AnonymousIdentity;
            try { var value = _authentication?.CacheIdentity ?? ""; return value.Length <= 256 ? value : ""; } catch { return ""; } }
    }
    internal void SetChannel(ReleaseChannel channel)
    {
        if (_disposed) return;
        if (Channel == channel) return;
        Channel = channel; Clear();
        if (_feed != null) { _feed = ReleaseFeedPolicy.For(channel.ToString()); _repository = _feed.Repository;
            _endpoint = new Uri("https://api.github.com/repos/" + _repository + "/releases?per_page=100"); _identity = AuthIdentity; }
        _channelRefresh = true;
        if (_channelCache.TryGetValue(channel, out var cached) && cached.Identity == _identity)
        {
            _pages = new(cached.Scan.Pages!, StringComparer.Ordinal);
            _lastSuccess = cached.Scan.Result with { Release = cached.Scan.Result.Release is { } release ? release with { Generation = _generation } : null };
        }
        SyncBackoff();
        Set(_retryAt != DateTimeOffset.MinValue ? ReadyState() : _lastSuccess ?? ReadyState());
    }
    internal void CheckTransition(DateTimeOffset now)
    {
        if (_feed is not { Configured: true }) return;
          Clear(); Set(ReadyState()); _transitionScan = true; Check(now);
    }
    private void SyncIdentity()
    {
        var identity = AuthIdentity;
        if (identity == _identity) return;
        Clear(); _channelCache.Clear(); _identity = identity; _notifiedVersions.Clear();
        Set(ReadyState());
    }
    private void Clear()
    {
        StopPending(); _pages = new(StringComparer.Ordinal); _lastSuccess = null;
        NotificationVersion = null; _autoDue = true; _transitionScan = _channelRefresh = false;
        // Per-channel cooldown survives account/provider changes; server/IP backoff remains shared.
    }
    private void StopPending()
    {
        var pending = _pending; _pending = null; // Detach before cancellation can call back.
        _generation++;
        if (pending == null) return;
        try { pending.Cancellation.Cancel(); } catch { }
        try { pending.Cancellation.Dispose(); } catch { }
    }
    private void Set(ReleaseUpdateResult result)
    {
        if (Result == result) return;
        Result = result; Revision++;
    }
    internal void Cancel()
    {
        if (_pending == null) return;
        StopPending(); _channelCache.Remove(Channel); Set(Failure(ReleaseUpdateStatus.Cancelled));
    }
    internal void Tick(DateTimeOffset now, bool menuAvailable)
    {
        Poll();
        if (menuAvailable && !_menuWasAvailable) _autoDue = true;
        _menuWasAvailable = menuAvailable;
        if (!menuAvailable || !_autoDue || !Configured || _pending != null) return;
        // A queued manual check must not be lost to the six-hour freshness cache.
        var deferred = Result.RetryAt != null;
        _autoDue = false;
        Check(now, manual: deferred || _channelRefresh); // Channel refresh must not be swallowed by home freshness.
    }
    internal void AcknowledgeNotification()
    {
        if (NotificationVersion == null) return;
        NotificationVersion = null; Revision++;
    }
    internal void FrameFailed()
    {
        StopPending(); _channelCache.Remove(Channel); _autoDue = false; NotificationVersion = null;
        Set(Failure(ReleaseUpdateStatus.Failed));
    }
    internal void Check(DateTimeOffset now, bool manual = true)
    {
        if (_disposed) return;
        SyncIdentity();
        SyncBackoff();
        if (!Configured) { Set(new(ReleaseUpdateStatus.NotConfigured)); return; }
        if (_pending != null) return;
        if (!manual && _lastSuccess?.CheckedAt is { } checkedAt && now < checkedAt.AddHours(6)) return;
        var cooldown = _lastStarted.GetValueOrDefault(Channel, DateTimeOffset.MinValue).AddSeconds(60);
        var due = _retryAt > cooldown ? _retryAt : cooldown;
        if (now < due)
        {
            _autoDue = true;
            Set(_channelRefresh && now >= _retryAt && _lastSuccess != null ? _lastSuccess
                : Failure(now < _retryAt ? ReleaseUpdateStatus.RateLimited : ReleaseUpdateStatus.RetryWaiting, due));
            return;
        }
        var cancellation = new CancellationTokenSource(_timeout);
        var token = cancellation.Token;
        var pages = new Dictionary<string, Page>(_pages, StringComparer.Ordinal);
        var client = _client!; var authentication = _feed is { RequiresAuthentication: false } ? null : _authentication;
        var feed = _feed; var repository = _repository; var endpoint = _endpoint; var generation = _generation; var transitionScan = _transitionScan;
        var channel = Channel; var identity = _identity;
        _channelCache.Remove(Channel); // An interrupted newer scan must not resurrect an older success as current.
        _lastStarted[Channel] = now; _autoDue = false; _transitionScan = _channelRefresh = false;
        _pending = new(Task.Run(() => ReadAsync(client, authentication, pages, channel, repository, endpoint, feed, generation, transitionScan, now, token)),
            cancellation, _generation, identity);
        Set(new(ReleaseUpdateStatus.Checking));
    }
    internal void Poll()
    {
        if (_disposed) return;
        SyncBackoff();
        SyncIdentity();
        var pending = _pending;
        if (pending == null || !pending.Task.IsCompleted) return;
        _pending = null;
        Scan scan;
        try { scan = pending.Task.GetAwaiter().GetResult(); } // Already completed; never waits for I/O.
        catch { FrameFailed(); return; }
        finally { try { pending.Cancellation.Dispose(); } catch { } }
        if (pending.Generation != _generation || pending.Identity != _identity) return;
        var result = scan.Result;
        if (scan.Pages != null)
        {
            _pages = scan.Pages; _lastSuccess = result; _retryAt = DateTimeOffset.MinValue; SyncBackoff(); Set(result);
            if (result.Release?.TransitionCandidate != true) _channelCache[Channel] = (_identity, scan);
            else _channelCache.Remove(Channel);
            if (result.Release is { } release)
            {
                var versionKey = (release.Version.StartsWith('v') ? release.Version[1..] : release.Version).Split('+')[0];
                if (_notifiedVersions.Add(versionKey) || NotificationVersion != null) NotificationVersion = release.Version;
            }
            else NotificationVersion = null;
        }
        else
        {
            _channelCache.Remove(Channel);
            if (result.Status is ReleaseUpdateStatus.NotConfigured or ReleaseUpdateStatus.AuthenticationRequired or ReleaseUpdateStatus.AccessUnavailable)
            { _pages.Clear(); _lastSuccess = null; NotificationVersion = null; }
            _retryAt = result.RetryAt ?? DateTimeOffset.MinValue; SyncBackoff();
            Set(Failure(result.Status, result.RetryAt));
        }
    }
    private ReleaseUpdateResult Failure(ReleaseUpdateStatus status, DateTimeOffset? retryAt = null) =>
        _lastSuccess == null ? new(status, RetryAt: retryAt)
            : new(ReleaseUpdateStatus.Stale, _lastSuccess.Release, _lastSuccess.CheckedAt, status, retryAt);

    private void SyncBackoff()
    {
        var ticks = Interlocked.Read(ref _serverRetryTicks);
        if (ticks > _retryAt.UtcDateTime.Ticks) _retryAt = new DateTimeOffset(ticks, TimeSpan.Zero);
    }
    private void RememberBackoff(DateTimeOffset? retryAt)
    {
        if (retryAt == null) return;
        var ticks = retryAt.Value.UtcDateTime.Ticks;
        var previous = Interlocked.Read(ref _serverRetryTicks);
        while (ticks > previous)
        {
            var actual = Interlocked.CompareExchange(ref _serverRetryTicks, ticks, previous);
            if (actual == previous) break;
            previous = actual;
        }
    }

    private async Task<Scan> ReadAsync(HttpClient client, IReleaseAuthentication? authentication,
        Dictionary<string, Page> cached, ReleaseChannel channel, string repository, Uri endpoint, ReleaseFeed? feed, int generation, bool transitionScan, DateTimeOffset now, CancellationToken token)
    {
        var diagnosticOperation = DiagnosticOperation.New();
        DiagnosticHub.Stage(DiagnosticFeature.ReleaseCheck, DiagnosticPhase.ReleaseMetadata, DiagnosticOutcome.Begin, diagnosticOperation);
        try
        {
            var collected = new List<ReleaseUpdate>();
            var pages = new Dictionary<string, Page>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (feed != null) {
                var proof = await ReadPageAsync(client, authentication, ReleaseFeedTransport.IdentityEndpoint(feed), null,
                    ReleaseFeedTransport.MaxRepositoryBytes, endpoint, feed, now, token).ConfigureAwait(false);
                try { ReleaseFeedTransport.VerifyIdentity(proof.Page.Body, feed); }
                catch { throw new CheckFailure(ReleaseUpdateStatus.AccessUnavailable); }
            }
            Uri? next = endpoint;
            var bytes = 0;
            while (next != null)
            {
                token.ThrowIfCancellationRequested();
                if (pages.Count >= MaxPages || !visited.Add(next.AbsoluteUri)) throw new CheckFailure(ReleaseUpdateStatus.Incomplete);
                cached.TryGetValue(next.AbsoluteUri, out var previous);
                var page = await ReadPageAsync(client, authentication, next, previous, MaxBytes - bytes, endpoint, feed, now, token).ConfigureAwait(false);
                bytes += page.Page.Body.Length; // Cached pages still count toward the bounded JSON work.
                if (bytes > MaxBytes) throw new CheckFailure(ReleaseUpdateStatus.Failed);
                collected.AddRange(Parse(page.Page.Body, repository).Select(release => feed == null ? release : release with
                    { Repository = feed.Repository, RepositoryId = feed.RepositoryId, FeedChannel = feed.Channel, Generation = generation }));
                pages.Add(next.AbsoluteUri, page.Page);
                next = page.Next;
            }
            token.ThrowIfCancellationRequested();
            var release = transitionScan ? Select(collected, "0.0.0", channel) : Select(collected, _current, channel);
            if (release != null && transitionScan && TryVersion(release.Version, out var target) && TryVersion(_current, out var current) && target.CompareTo(current) <= 0)
                release = release with { TransitionCandidate = true };
            DiagnosticHub.Stage(DiagnosticFeature.ReleaseCheck, DiagnosticPhase.ReleaseMetadata, DiagnosticOutcome.Completed, diagnosticOperation);
            return new(new(release == null ? ReleaseUpdateStatus.UpToDate : ReleaseUpdateStatus.Available, release, now), pages);
        }
        catch (CheckFailure failure) { RememberBackoff(failure.RetryAt); DiagnosticHub.Failure(DiagnosticFeature.ReleaseCheck, DiagnosticPhase.ReleaseMetadata, DiagnosticHub.CodeForStatus(failure.Status.ToString()), failure, operation: diagnosticOperation); return new(new(failure.Status, RetryAt: failure.RetryAt)); }
        catch (OperationCanceledException ex) {
            if (token.IsCancellationRequested) DiagnosticHub.Stage(DiagnosticFeature.ReleaseCheck, DiagnosticPhase.ReleaseMetadata, DiagnosticOutcome.Cancelled, diagnosticOperation);
            else DiagnosticHub.Failure(DiagnosticFeature.ReleaseCheck, DiagnosticPhase.ReleaseMetadata, DiagnosticCode.Unknown, ex, operation: diagnosticOperation);
            return new(new(ReleaseUpdateStatus.Failed));
        }
        catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.ReleaseCheck, DiagnosticPhase.ReleaseMetadata, DiagnosticCode.Unknown, ex, operation: diagnosticOperation); return new(new(ReleaseUpdateStatus.Failed)); } // Do not expose server bodies/credentials in errors.
    }
    private async Task<(Page Page, Uri? Next)> ReadPageAsync(HttpClient client, IReleaseAuthentication? authentication,
        Uri uri, Page? cached, int remaining, Uri endpoint, ReleaseFeed? feed, DateTimeOffset now, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("BetterAstralParty-UpdateCheck/" + _current);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            if (attempt == 0 && !string.IsNullOrEmpty(cached?.ETag)) request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
            if (!ReleaseFeedTransport.Authorize(request, authentication, feed)) throw new CheckFailure(ReleaseUpdateStatus.NotConfigured);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } actual && actual != uri)
                throw new CheckFailure(ReleaseUpdateStatus.Failed);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (attempt == 0 && cached?.ETag != null)
                {
                    var refreshed = cached with
                    {
                        ETag = response.Headers.ETag?.ToString() ?? cached.ETag,
                        Next = response.Headers.Contains("Link") ? NextPage(response, endpoint) : cached.Next
                    };
                    return (refreshed, refreshed.Next);
                }
                if (attempt == 0) continue; // A 304 without an exact cached representation cannot mean up to date.
                throw new CheckFailure(ReleaseUpdateStatus.Failed);
            }
            var limited = response.StatusCode == HttpStatusCode.TooManyRequests
                || response.StatusCode == HttpStatusCode.Forbidden &&
                    (response.Headers.RetryAfter != null || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues) && remainingValues.Contains("0"));
            if (limited) throw new CheckFailure(ReleaseUpdateStatus.RateLimited, RetryAt(response, now));
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new CheckFailure(ReleaseUpdateStatus.AuthenticationRequired);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) throw new CheckFailure(ReleaseUpdateStatus.AccessUnavailable);
            if (response.StatusCode != HttpStatusCode.OK) throw new CheckFailure(ReleaseUpdateStatus.Failed);
            if (response.Content.Headers.ContentLength > remaining) throw new CheckFailure(ReleaseUpdateStatus.Failed);
            using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                if (body.Length + count > remaining) throw new CheckFailure(ReleaseUpdateStatus.Failed);
                body.Write(buffer, 0, count);
            }
            var next = uri.AbsolutePath == endpoint.AbsolutePath ? NextPage(response, endpoint) : null;
            return (new(body.ToArray(), response.Headers.ETag?.ToString(), next), next);
        }
        throw new CheckFailure(ReleaseUpdateStatus.Failed);
    }
    private static Uri? NextPage(HttpResponseMessage response, Uri endpoint)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        var next = Regex.Matches(string.Join(",", values), "<([^<>]+)>\\s*;\\s*rel=\"next\"");
        if (next.Count == 0) return null;
        if (next.Count != 1 || !Uri.TryCreate(next[0].Groups[1].Value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "api.github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0 || uri.AbsolutePath != endpoint.AbsolutePath || uri.Query.Length > 100)
            throw new CheckFailure(ReleaseUpdateStatus.Incomplete);
        var query = uri.Query.TrimStart('?').Split('&');
        if (query.Length != 2 || query.Distinct().Count() != 2
            || !query.Contains("per_page=100") || !query.Any(value => Regex.IsMatch(value, @"\Apage=[1-9][0-9]{0,5}\z")))
            throw new CheckFailure(ReleaseUpdateStatus.Incomplete);
        return uri;
    }
    private static DateTimeOffset RetryAt(HttpResponseMessage response, DateTimeOffset now)
    {
        var retry = response.Headers.RetryAfter?.Date;
        if (response.Headers.RetryAfter?.Delta is { } delta) retry = now.AddSeconds(Math.Clamp(delta.TotalSeconds, 1, 86400));
        if (retry == null && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset)
            && long.TryParse(reset.FirstOrDefault(), out var unix) && unix is >= 0 and <= 253402300799)
            retry = DateTimeOffset.FromUnixTimeSeconds(unix);
        return retry is { } time && time > now ? time : now.AddSeconds(60);
    }

    internal static bool TryVersion(string tag, out SemVersion version)
    {
        version = null!;
        if (tag.Length is 0 or > 128) return false;
        // The existing library also strips v itself; enforce exactly one prefix and no whitespace here.
        var value = tag.StartsWith('v') ? tag[1..] : tag;
        return value.Length != 0 && value[0] is >= '0' and <= '9'
            && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '.' or '-' or '+')
            && SemVersion.TryParse(value, false, out version);
    }
    internal static ReleaseUpdate[] Parse(byte[] body, string repository)
    {
        if (body.Length > MaxBytes) throw new InvalidDataException("Release response too large");
        using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 100)
            throw new InvalidDataException("Invalid release list");
        var releases = new List<ReleaseUpdate>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !item.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            string Text(string key) => item.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString()! : "";
            var tag = Text("tag_name");
            if (!TryVersion(tag, out var version) || !TryReleasePage(Text("html_url"), repository, out var page, tag)) continue;
            // Keep release eligibility flags out of untrusted display strings.
            var beta = prerelease.ValueKind == JsonValueKind.True || version.IsPreRelease || version.Major == 0;
            var assets = Assets(item, tag);
            releases.Add(new(tag, PlainText(Text("name"), 160), PlainText(Text("body"), MaxNotes), page!)
            { Beta = beta, ReleaseId = assets[0], ZipAssetId = assets[1], DescriptorAssetId = assets[2], SignatureAssetId = assets[3] });
        }
        return releases.ToArray();
    }
    private static long[] Assets(JsonElement item, string tag)
    {
        var result = new long[4];
        if (!item.TryGetProperty("id", out var release) || release.ValueKind != JsonValueKind.Number || !release.TryGetInt64(out var releaseId) || releaseId <= 0
            || !item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 16) return result;
        var assetVersion = tag.StartsWith('v') ? tag[1..] : tag;
        var names = new[] { "BetterAstralParty-" + assetVersion + "-update.zip", "BetterAstralParty.update.manifest", "BetterAstralParty.update.signature" };
        var ids = new HashSet<long>();
        foreach (var asset in assets.EnumerateArray()) {
            if (asset.ValueKind != JsonValueKind.Object || !asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            var index = Array.IndexOf(names, name.GetString()); if (index < 0) continue;
            if (!asset.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var value) || value <= 0 || result[index + 1] != 0 || !ids.Add(value)) return new long[4];
            result[index + 1] = value;
        }
        if (result.Skip(1).Any(value => value == 0)) return new long[4]; result[0] = releaseId; return result;
    }
    internal static ReleaseUpdate? Select(IEnumerable<ReleaseUpdate> releases, string current, ReleaseChannel channel)
    {
        if (!TryVersion(current, out var installed)) throw new ArgumentException("Invalid current version");
        ReleaseUpdate? selected = null; SemVersion? highest = null;
        foreach (var release in releases)
        {
            if (!TryVersion(release.Version, out var version) || version.CompareTo(installed) <= 0
                || channel == ReleaseChannel.Stable && (release.Beta || version.Major == 0 || version.IsPreRelease)) continue;
            if (highest != null && version.CompareTo(highest) <= 0) continue;
            highest = version; selected = release;
        }
        return selected;
    }
    internal static string PlainText(string value, int limit)
    {
        var text = new StringBuilder();
        foreach (var c in value)
        {
            if (text.Length >= limit) break;
            if (c == '\r' || char.IsControl(c) && c is not ('\n' or '\t') || c is >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069') continue;
            text.Append(c);
        }
        if (text.Length != 0 && char.IsHighSurrogate(text[^1])) text.Length--;
        return text.ToString();
    }
    internal static bool TryReleasePage(string value, string repository, out Uri? uri, string? tag = null)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" || parsed.Host != "github.com"
            || !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0) return false;
        var path = Uri.UnescapeDataString(parsed.AbsolutePath);
        var prefix = "/" + repository + "/releases/tag/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var releaseTag = path[prefix.Length..];
        if (!TryVersion(releaseTag, out _) || tag != null && releaseTag != tag) return false;
        uri = parsed; return true;
    }
    internal Uri RepositoryPage => new("https://github.com/" + ReleaseFeedPolicy.For(Channel.ToString()).Repository + "/releases");
    internal bool TryDownloadPage(out Uri? page)
    {
        SyncIdentity(); page = null;
        return (Result.Status is ReleaseUpdateStatus.Available or ReleaseUpdateStatus.Stale) && Result.Release is { } release
            && TryReleasePage(release.Page.AbsoluteUri, _repository, out page, release.Version);
    }
    internal void DownloadFailed() => Set(Failure(ReleaseUpdateStatus.Failed));
    public void Dispose()
    {
        if (_disposed) return;
        Clear(); _channelCache.Clear(); _notifiedVersions.Clear(); _disposed = true;
        _authentication = null; _identity = ""; _client = null;
        _ownedClient?.Dispose(); _ownedClient = null;
        Set(new(ReleaseUpdateStatus.NotConfigured));
    }
}
