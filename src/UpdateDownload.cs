using BetterAstralParty.Observability;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating;

internal enum UpdateDownloadStatus
{
    NotConfigured, Idle, Downloading, Ready, Cancelled, AuthenticationRequired,
    AccessUnavailable, RateLimited, Failed, TimedOut, TooLarge, VerificationFailed
}
internal sealed record UpdateDownloadResult(UpdateDownloadStatus Status, DownloadedUpdate? Update = null,
    DateTimeOffset? RetryAt = null, UpdateFailure? ValidationFailure = null);
internal sealed class UpdateDownloadRequest
{
    internal readonly UpdateContext Context;
    internal readonly long DescriptorAssetId, SignatureAssetId;
    internal UpdateDownloadRequest(UpdateContext context, long descriptorAssetId, long signatureAssetId)
    {
        if (context.Repository != UpdateDownloads.Repository && context.Repository != ReleaseFeedPolicy.BetaRepository || descriptorAssetId <= 0 || signatureAssetId <= 0
            || descriptorAssetId == signatureAssetId || descriptorAssetId == context.AssetId || signatureAssetId == context.AssetId)
            throw new ArgumentException("Invalid update download request");
        Context = context; DescriptorAssetId = descriptorAssetId; SignatureAssetId = signatureAssetId;
    }
    internal string Identity => string.Join("|", Context.Repository, Context.RepositoryId.ToString(CultureInfo.InvariantCulture), Context.Channel, Context.CurrentVersion,
        Context.ReleaseTag, Context.Platform, Context.HighestAppliedVersion ?? "", Context.HighestAppliedDescriptorSha256 ?? "", Context.ReleaseId.ToString(CultureInfo.InvariantCulture),
        Context.AssetId.ToString(CultureInfo.InvariantCulture), DescriptorAssetId.ToString(CultureInfo.InvariantCulture),
        SignatureAssetId.ToString(CultureInfo.InvariantCulture), Context.Transition?.Identity ?? "");
}
internal sealed class DownloadedUpdate
{
    private readonly byte[] _descriptor, _signature;
    internal readonly string KeyId;
    internal readonly VerifiedUpdatePayload Payload;
    internal DownloadedUpdate(object stamp, byte[] descriptor, string keyId, byte[] signature, VerifiedUpdatePayload payload)
    {
        if (!UpdateDownloads.ValidDownloadStamp(stamp)) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
        _descriptor = (byte[])descriptor.Clone(); _signature = (byte[])signature.Clone(); KeyId = keyId; Payload = payload;
    }
    // A future helper must reverify these bytes and a locked staged ZIP against its own trust roots/context.
    internal Stream OpenDescriptor() => new MemoryStream(_descriptor, 0, _descriptor.Length, false, false);
    internal Stream OpenSignature() => new MemoryStream(_signature, 0, _signature.Length, false, false);
}
internal sealed record ChannelTransitionProposal(VerifiedUpdateDescriptor Descriptor, int Generation, string FeedIdentity);

// Main-thread-owned coordinator. No filesystem, native UI, process or install operations.
// Start/Poll never wait for a network task; one worker owns a complete verified memory snapshot.
internal sealed class UpdateDownloads : IDisposable
{
    internal const string Repository = "TSM701/BetterAstralParty";
    internal const long RepositoryId = 1401226962; // Confirmed by the parent through the GitHub connector.
    internal const int MaxEnvelopeBytes = 1024;
    private static readonly object DownloadStamp = new();
    internal static bool ValidDownloadStamp(object stamp) => ReferenceEquals(stamp, DownloadStamp);
    private sealed record Pending(Task<UpdateDownloadResult> Task, CancellationTokenSource Cancellation,
        int Generation, string Identity, string RequestIdentity);
    private sealed class DownloadFailure : Exception
    {
        internal readonly UpdateDownloadStatus Status;
        internal readonly DateTimeOffset? RetryAt;
        internal DownloadFailure(UpdateDownloadStatus status, DateTimeOffset? retryAt = null)
            : base("Update download: " + status) { Status = status; RetryAt = retryAt; }
    }
    private sealed class Operation : IDisposable
    {
        internal readonly CancellationTokenSource Cancellation;
        internal CancellationToken Token => Cancellation.Token;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly TimeSpan _total, _idle;
        internal Operation(CancellationToken token, TimeSpan total, TimeSpan idle)
        { Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); _total = total; _idle = idle; }
        internal void Check()
        {
            Token.ThrowIfCancellationRequested();
            if (_elapsed.Elapsed >= _total) throw new DownloadFailure(UpdateDownloadStatus.TimedOut);
        }
        internal async Task<T> Wait<T>(Task<T> task, Action<T>? disposeLate = null)
        {
            try
            {
                Check(); var remaining = _total - _elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero) throw new DownloadFailure(UpdateDownloadStatus.TimedOut);
                return await task.WaitAsync(remaining < _idle ? remaining : _idle, Token).ConfigureAwait(false);
            }
            catch
            {
                SafeCancel(Cancellation);
                // A handler/stream may ignore cancellation. Observe faults and dispose late resources.
                _ = task.ContinueWith(t =>
                {
                    if (t.IsFaulted) { _ = t.Exception; }
                    else if (t.Status == TaskStatus.RanToCompletion && disposeLate != null)
                        try { disposeLate(t.Result); } catch { }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }
        }
        public void Dispose() { try { Cancellation.Dispose(); } catch { } }
    }
    private readonly HttpClient _client;
    private readonly UpdateTrust _trust;
    private readonly TimeSpan _totalTimeout, _idleTimeout;
    private IReleaseAuthentication? _authentication;
    private ReleaseFeed? _feed;
    private Pending? _pending;
    private string _identity = "", _requestIdentity = "";
    private int _generation;
    private bool _disposed;
    private DateTimeOffset _lastStarted = DateTimeOffset.MinValue, _retryAt = DateTimeOffset.MinValue;
    private long _serverRetryTicks;
    internal UpdateDownloadResult Result { get; private set; } = new(UpdateDownloadStatus.NotConfigured);
    internal int Revision { get; private set; }

    // Production clients always disable redirects, cookies, implicit credentials and decompression.
    // The injected handler is for controlled fixtures only; never inject a configured HttpClient.
    internal static UpdateDownloads Production(IReleaseAuthentication? authentication = null, ReleaseChannel channel = ReleaseChannel.Stable)
        => new(UpdateTrust.Production(), authentication, feed: ReleaseFeedPolicy.For(channel.ToString()));
    internal UpdateDownloads(UpdateTrust trust, IReleaseAuthentication? authentication, HttpMessageHandler? fixtureHandler = null,
        TimeSpan? totalTimeout = null, TimeSpan? idleTimeout = null, ReleaseFeed? feed = null)
    {
        _trust = trust; _feed = feed; _totalTimeout = totalTimeout ?? TimeSpan.FromMinutes(3); _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(15);
        if (_totalTimeout <= TimeSpan.Zero || _totalTimeout > TimeSpan.FromMinutes(3)
            || _idleTimeout <= TimeSpan.Zero || _idleTimeout > TimeSpan.FromSeconds(15)) throw new ArgumentException("Invalid download timeout");
        _client = ReleaseHttpClient.Create(fixtureHandler);
        Configure(authentication);
    }
    private static string Identity(IReleaseAuthentication? authentication, ReleaseFeed? feed)
    {
        if (feed is { RequiresAuthentication: false }) return feed.AnonymousIdentity;
        try { var value = authentication?.CacheIdentity ?? ""; return value.Length <= 256 ? value : ""; }
        catch { return ""; }
    }
    private bool Configured => !_disposed && _trust.Configured && _identity.Length != 0 && (_feed == null || _feed.Configured)
        && (_feed is { RequiresAuthentication: false } || _authentication != null);
    internal bool IsConfigured => Configured;
    internal ReleaseFeed? Feed => _feed;
    internal void SelectFeed(ReleaseChannel channel) { var feed = ReleaseFeedPolicy.For(channel.ToString()); if (_feed?.Identity == feed.Identity) return;
        _feed = feed; Configure(_authentication); }
    private UpdateDownloadResult Initial() => !Configured ? new(UpdateDownloadStatus.NotConfigured)
        : new(UpdateDownloadStatus.Idle, RetryAt: _retryAt == DateTimeOffset.MinValue ? null : _retryAt);
    private void Set(UpdateDownloadResult result) { if (Result != result) { Result = result; Revision++; } }
    private static void SafeCancel(CancellationTokenSource cancellation) { try { cancellation.Cancel(); } catch { } }
    private void Stop()
    {
        var pending = _pending; _pending = null; _generation++;
        if (pending == null) return;
        SafeCancel(pending.Cancellation); try { pending.Cancellation.Dispose(); } catch { }
    }
    internal void Configure(IReleaseAuthentication? authentication)
    {
        if (_disposed) return;
        Stop(); _authentication = authentication; _identity = Identity(authentication, _feed); _requestIdentity = ""; Set(Initial());
        // Preserve the shared cooldown and server backoff when an account/provider changes.
    }
    private void SyncIdentity()
    {
        var identity = Identity(_authentication, _feed);
        if (identity == _identity) return;
        Stop(); _identity = identity; _requestIdentity = ""; Set(Initial());
    }
    private void SyncBackoff()
    {
        var ticks = Interlocked.Read(ref _serverRetryTicks);
        if (ticks > _retryAt.UtcDateTime.Ticks) _retryAt = new DateTimeOffset(ticks, TimeSpan.Zero);
    }
    private void RememberBackoff(DateTimeOffset? retryAt)
    {
        if (!retryAt.HasValue) return;
        var ticks = retryAt.Value.UtcDateTime.Ticks; var previous = Interlocked.Read(ref _serverRetryTicks);
        while (ticks > previous)
        {
            var actual = Interlocked.CompareExchange(ref _serverRetryTicks, ticks, previous);
            if (actual == previous) break;
            previous = actual;
        }
    }
    internal void Start(UpdateDownloadRequest request, DateTimeOffset now, DiagnosticOperation diagnosticParent = default)
    {
        if (_disposed) return;
        SyncBackoff(); SyncIdentity(); var requestIdentity = request.Identity;
        if (_requestIdentity != requestIdentity)
        { Stop(); _requestIdentity = requestIdentity; Set(Initial()); }
        if (!Configured) { Set(new(UpdateDownloadStatus.NotConfigured)); return; }
        if (_feed != null && (request.Context.Repository != _feed.Repository || request.Context.RepositoryId != _feed.RepositoryId || request.Context.Channel != _feed.Channel))
        { Set(new(UpdateDownloadStatus.NotConfigured)); return; }
        if (_pending != null || Result.Status == UpdateDownloadStatus.Ready) return;
        if (now < _retryAt || now < _lastStarted.AddSeconds(60))
        { Set(new(UpdateDownloadStatus.RateLimited, RetryAt: _retryAt > _lastStarted.AddSeconds(60) ? _retryAt : _lastStarted.AddSeconds(60))); return; }
        var cancellation = new CancellationTokenSource(); var token = cancellation.Token;
        var authentication = _feed is { RequiresAuthentication: false } ? null : _authentication; var identity = _identity; var feed = _feed; _lastStarted = now;
        _pending = new(Task.Run(() => Read(request, authentication, feed, identity, now, token, diagnosticParent)), cancellation,
            _generation, identity, requestIdentity);
        Set(new(UpdateDownloadStatus.Downloading));
    }
    internal void Poll()
    {
        if (_disposed) return;
        SyncBackoff(); SyncIdentity(); var pending = _pending;
        if (pending == null || !pending.Task.IsCompleted) return;
        _pending = null; try { pending.Cancellation.Dispose(); } catch { }
        if (pending.Generation != _generation || pending.Identity != _identity || pending.RequestIdentity != _requestIdentity) return;
        UpdateDownloadResult result;
        try { result = pending.Task.GetAwaiter().GetResult(); }
        catch { result = new(UpdateDownloadStatus.Failed); }
        if (result.RetryAt is { } retryAt && retryAt > _retryAt) _retryAt = retryAt;
        Set(result);
    }
    internal void Cancel()
    {
        if (_pending == null) return;
        Stop(); Set(new(UpdateDownloadStatus.Cancelled));
    }
    // Invoked from an explicit channel-transition settings flow, never the popup or auto
    // download loop. Fetches only a signed manifest/envelope; it cannot stage or queue files.
    internal Task<ChannelTransitionProposal> Propose(UpdateDownloadRequest request, DateTimeOffset now, CancellationToken cancellation)
    {
        SyncBackoff(); SyncIdentity();
        var feed = _feed; var generation = _generation; var identity = _identity;
        if (!Configured || feed == null || request.Context.Repository != feed.Repository || request.Context.RepositoryId != feed.RepositoryId
            || request.Context.Channel != feed.Channel || _pending != null || now < _retryAt || now < _lastStarted.AddSeconds(60))
            throw new InvalidOperationException("Transition prerequisites unavailable");
        _lastStarted = now;
        var authentication = feed.RequiresAuthentication ? _authentication : null;
        return Task.Run(async () => {
            using var operation = new Operation(cancellation, _totalTimeout, _idleTimeout);
            var proof = await Asset(0, ReleaseFeedTransport.MaxRepositoryBytes, null, authentication, feed, identity, now, operation, true).ConfigureAwait(false);
            ReleaseFeedTransport.VerifyIdentity(proof, feed);
            var descriptor = await Asset(request.DescriptorAssetId, UpdateTrust.MaxDescriptorBytes, null, authentication, feed, identity, now, operation).ConfigureAwait(false);
            var envelope = Envelope(await Asset(request.SignatureAssetId, MaxEnvelopeBytes, null, authentication, feed, identity, now, operation).ConfigureAwait(false));
            var verified = _trust.VerifyProposal(descriptor, envelope.KeyId, envelope.Signature, request.Context, operation.Token);
            Account(authentication, feed, identity, operation);
            return new ChannelTransitionProposal(verified, generation, feed.Identity);
        }, cancellation);
    }
    internal bool CurrentProposal(ChannelTransitionProposal proposal) => !_disposed && Configured
        && proposal.Generation == _generation && proposal.FeedIdentity == _feed?.Identity;
    internal void Clear()
    {
        Stop(); _requestIdentity = ""; Set(Initial());
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(); _authentication = null; _identity = ""; _requestIdentity = "";
        Set(new(UpdateDownloadStatus.NotConfigured)); try { _client.Dispose(); } catch { }
    }
    private static void Account(IReleaseAuthentication? authentication, ReleaseFeed? feed, string identity, Operation operation)
    {
        operation.Check();
        if (identity.Length == 0 || Identity(authentication, feed) != identity) throw new DownloadFailure(UpdateDownloadStatus.Cancelled);
    }
    private static bool Cdn(Uri uri, ReleaseFeed? feed)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != "release-assets.githubusercontent.com"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.OriginalString.Length > 8192) return false;
        var prefix = "/github-production-release-asset/" + (feed?.RepositoryId ?? RepositoryId).ToString(CultureInfo.InvariantCulture) + "/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(uri.AbsolutePath.Substring(prefix.Length), "D", out _)) return false;
        // Reject encoded/normalized path aliases, alternate authority spellings and explicit ports.
        return uri.OriginalString == "https://release-assets.githubusercontent.com" + uri.AbsolutePath + uri.Query;
    }
    private static HttpRequestMessage Request(Uri uri, bool identityProof)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("BetterAstralParty-Updater/1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(identityProof ? "application/vnd.github+json" : "application/octet-stream"));
        return request;
    }
    private static void Authorize(HttpRequestMessage request, IReleaseAuthentication? authentication, ReleaseFeed? feed, string identity, Operation operation)
    {
        var endpoint = request.RequestUri!;
        using var probe = new HttpRequestMessage(HttpMethod.Get, endpoint);
        Account(authentication, feed, identity, operation);
        if (feed is { RequiresAuthentication: false }) return;
        bool allowed;
        try { allowed = authentication?.TryAuthorize(probe) == true; }
        catch { throw new DownloadFailure(UpdateDownloadStatus.AuthenticationRequired); }
        Account(authentication, feed, identity, operation);
        var header = probe.Headers.Authorization;
        if (!allowed || probe.Method != HttpMethod.Get || probe.RequestUri?.AbsoluteUri != endpoint.AbsoluteUri
            || header == null || header.Scheme != "Bearer" || string.IsNullOrWhiteSpace(header.Parameter) || header.Parameter.Length > 8192)
            throw new DownloadFailure(UpdateDownloadStatus.AuthenticationRequired);
        // Copy only the required bearer header. Provider cookies/arbitrary headers never enter the request.
        request.Headers.Authorization = header;
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
    }
    private static DateTimeOffset Backoff(HttpResponseMessage response, DateTimeOffset now)
    {
        var next = now.AddMinutes(1); var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta && delta > TimeSpan.Zero) next = now.Add(delta > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delta);
        else if (retry?.Date is { } date && date > now) next = date;
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            try { var reset = DateTimeOffset.FromUnixTimeSeconds(seconds); if (reset > next) next = reset; } catch (ArgumentOutOfRangeException) { }
        }
        return next > now.AddDays(1) ? now.AddDays(1) : next;
    }
    private static void Status(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.StatusCode == HttpStatusCode.OK) return;
        var code = (int)response.StatusCode;
        if (Limited(response))
            throw new DownloadFailure(UpdateDownloadStatus.RateLimited, Backoff(response, now));
        throw new DownloadFailure(code == 401 ? UpdateDownloadStatus.AuthenticationRequired
                : code == 403 || code == 404 ? UpdateDownloadStatus.AccessUnavailable : UpdateDownloadStatus.Failed);
    }
    private static bool Limited(HttpResponseMessage response) => (int)response.StatusCode == 429
        || (int)response.StatusCode == 403 && (response.Headers.RetryAfter != null
            || response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.Contains("0"));
    private void RememberLimit(HttpResponseMessage response, Uri destination, DateTimeOffset now)
    {
        if (response.RequestMessage?.RequestUri is { } actual && actual.AbsoluteUri != destination.AbsoluteUri) return;
        if (Limited(response)) RememberBackoff(Backoff(response, now));
    }
    private async Task<byte[]> Asset(long assetId, long limit, long? expected, IReleaseAuthentication? authentication,
        ReleaseFeed? feed, string identity, DateTimeOffset now, Operation operation, bool identityProof = false)
    {
        var endpoint = identityProof ? ReleaseFeedTransport.IdentityEndpoint(feed!)
            : new Uri("https://api.github.com/repos/" + (feed?.Repository ?? Repository) + "/releases/assets/" + assetId.ToString(CultureInfo.InvariantCulture));
        var destination = endpoint;
        for (var hop = 0; hop <= 1; hop++)
        {
            using var request = Request(destination, identityProof);
            Account(authentication, feed, identity, operation);
            if (hop == 0) Authorize(request, authentication, feed, identity, operation);
            var requestedDestination = destination;
            using var response = await operation.Wait(_client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operation.Token), r =>
            {
                // Server limits also survive a cancelled/retired account's late response.
                try { RememberLimit(r, requestedDestination, now); } finally { r.Dispose(); }
            }).ConfigureAwait(false);
            RememberLimit(response, requestedDestination, now);
            Account(authentication, feed, identity, operation);
            if (response.RequestMessage?.RequestUri is { } actual && actual.AbsoluteUri != destination.AbsoluteUri)
                throw new DownloadFailure(UpdateDownloadStatus.Failed);
            if (response.StatusCode == HttpStatusCode.Found)
            {
                var location = response.Headers.Location;
                if (identityProof || hop != 0 || location == null || !Cdn(location, feed)) throw new DownloadFailure(UpdateDownloadStatus.Failed);
                destination = location; continue;
            }
            Status(response, now);
            var content = response.Content;
            if (content.Headers.ContentEncoding.Count != 0) throw new DownloadFailure(UpdateDownloadStatus.Failed);
            var declared = content.Headers.ContentLength;
            if (declared is < 0 || declared > limit) throw new DownloadFailure(UpdateDownloadStatus.TooLarge);
            if (expected.HasValue && declared.HasValue && declared != expected) throw new DownloadFailure(UpdateDownloadStatus.VerificationFailed);
            using var stream = await operation.Wait(content.ReadAsStreamAsync(operation.Token), s => s.Dispose()).ConfigureAwait(false);
            using var output = new MemoryStream(); var buffer = new byte[32768]; long total = 0;
            while (true)
            {
                Account(authentication, feed, identity, operation);
                var length = await operation.Wait(stream.ReadAsync(buffer.AsMemory(), operation.Token).AsTask()).ConfigureAwait(false);
                Account(authentication, feed, identity, operation);
                if (length == 0) break;
                total += length;
                if (total > limit) throw new DownloadFailure(UpdateDownloadStatus.TooLarge);
                if (expected.HasValue && total > expected.Value) throw new DownloadFailure(UpdateDownloadStatus.VerificationFailed);
                output.Write(buffer, 0, length);
            }
            if (total == 0 || declared.HasValue && total != declared.Value || expected.HasValue && total != expected.Value)
                throw new DownloadFailure(UpdateDownloadStatus.VerificationFailed);
            return output.ToArray();
        }
        throw new DownloadFailure(UpdateDownloadStatus.Failed);
    }
    private static (string KeyId, byte[] Signature) Envelope(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxEnvelopeBytes) throw new UpdateValidationException(UpdateFailure.LimitExceeded);
        foreach (var b in bytes) if (b != 10 && (b < 32 || b > 126)) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
        var lines = Encoding.ASCII.GetString(bytes).Split('\n');
        if (lines.Length != 4 || lines[0] != "BetterAstralParty.UpdateSignature/v1" || lines[3] != ""
            || !lines[1].StartsWith("key-id=", StringComparison.Ordinal) || !lines[2].StartsWith("signature=", StringComparison.Ordinal))
            throw new UpdateValidationException(UpdateFailure.InvalidSignature);
        var keyId = lines[1].Substring(7); var text = lines[2].Substring(10);
        if (!Regex.IsMatch(keyId, @"\A[A-Za-z0-9_-]{1,40}\z")) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
        byte[] signature;
        try { signature = Convert.FromBase64String(text); }
        catch (FormatException) { throw new UpdateValidationException(UpdateFailure.InvalidSignature); }
        if (signature.Length == 0 || signature.Length > UpdateTrust.MaxSignatureBytes || Convert.ToBase64String(signature) != text)
            throw new UpdateValidationException(UpdateFailure.InvalidSignature);
        return (keyId, signature);
    }
    private async Task<UpdateDownloadResult> Read(UpdateDownloadRequest request, IReleaseAuthentication? authentication,
        ReleaseFeed? feed, string identity, DateTimeOffset now, CancellationToken cancellation, DiagnosticOperation diagnosticParent)
    {
        using var operation = new Operation(cancellation, _totalTimeout, _idleTimeout);
        var diagnosticOperation = DiagnosticOperation.New();
        DiagnosticHub.Relate(diagnosticParent, diagnosticOperation);
        var diagnosticPhase = DiagnosticPhase.AuthenticationInput;
        void Enter(DiagnosticPhase next) {
            DiagnosticHub.Stage(DiagnosticFeature.Download, diagnosticPhase, DiagnosticOutcome.Completed, diagnosticOperation, request.Context.ReleaseTag);
            diagnosticPhase = next; DiagnosticHub.Stage(DiagnosticFeature.Download, next, DiagnosticOutcome.Begin, diagnosticOperation, request.Context.ReleaseTag);
        }
        DiagnosticHub.Stage(DiagnosticFeature.Download, diagnosticPhase, DiagnosticOutcome.Begin, diagnosticOperation, request.Context.ReleaseTag);
        try
        {
            Account(authentication, feed, identity, operation);
            if (!_trust.Configured) { DiagnosticHub.Gate(DiagnosticFeature.Download, DiagnosticCode.NotConfigured); return new(UpdateDownloadStatus.NotConfigured); }
            Enter(DiagnosticPhase.ReleaseMetadata);
            if (feed != null) {
                var proof = await Asset(0, ReleaseFeedTransport.MaxRepositoryBytes, null, authentication, feed, identity, now, operation, identityProof: true).ConfigureAwait(false);
                try { ReleaseFeedTransport.VerifyIdentity(proof, feed); } catch { throw new DownloadFailure(UpdateDownloadStatus.VerificationFailed); }
            }
            Enter(DiagnosticPhase.DescriptorAsset);
            var descriptor = await Asset(request.DescriptorAssetId, UpdateTrust.MaxDescriptorBytes, null, authentication, feed, identity, now, operation).ConfigureAwait(false);
            Enter(DiagnosticPhase.SignatureAsset);
            var envelope = Envelope(await Asset(request.SignatureAssetId, MaxEnvelopeBytes, null, authentication, feed, identity, now, operation).ConfigureAwait(false));
            Enter(DiagnosticPhase.DescriptorVerification);
            var verified = _trust.VerifyDescriptor(descriptor, envelope.KeyId, envelope.Signature, request.Context, operation.Token);
            Account(authentication, feed, identity, operation);
            Enter(DiagnosticPhase.PackageAsset);
            var zip = await Asset(verified.AssetId, UpdateTrust.MaxZipBytes, verified.ZipBytes, authentication, feed, identity, now, operation).ConfigureAwait(false);
            Enter(DiagnosticPhase.PayloadVerification);
            var payload = UpdatePackage.VerifyPayload(zip, verified, operation.Token);
            Account(authentication, feed, identity, operation);
            DiagnosticHub.Stage(DiagnosticFeature.Download, diagnosticPhase, DiagnosticOutcome.Completed, diagnosticOperation, request.Context.ReleaseTag);
            return new(UpdateDownloadStatus.Ready, new DownloadedUpdate(DownloadStamp, descriptor, envelope.KeyId, envelope.Signature, payload));
        }
        catch (UpdateValidationException ex) { DiagnosticHub.Failure(DiagnosticFeature.Download, diagnosticPhase, DiagnosticHub.CodeForStatus(ex.Failure.ToString()), ex, validationCode: (int)ex.Failure, operation: diagnosticOperation, targetVersion: request.Context.ReleaseTag); SafeCancel(operation.Cancellation); return new(UpdateDownloadStatus.VerificationFailed, ValidationFailure: ex.Failure); }
        catch (DownloadFailure ex) { DiagnosticHub.Failure(DiagnosticFeature.Download, diagnosticPhase, DiagnosticHub.CodeForStatus(ex.Status.ToString()), ex, operation: diagnosticOperation, targetVersion: request.Context.ReleaseTag); RememberBackoff(ex.RetryAt); SafeCancel(operation.Cancellation); return new(ex.Status, RetryAt: ex.RetryAt); }
        catch (TimeoutException ex) { DiagnosticHub.Failure(DiagnosticFeature.Download, diagnosticPhase, DiagnosticCode.Timeout, ex, operation: diagnosticOperation, targetVersion: request.Context.ReleaseTag); SafeCancel(operation.Cancellation); return new(UpdateDownloadStatus.TimedOut); }
        catch (OperationCanceledException ex)
        {
            if (cancellation.IsCancellationRequested) DiagnosticHub.Stage(DiagnosticFeature.Download, diagnosticPhase, DiagnosticOutcome.Cancelled, diagnosticOperation, request.Context.ReleaseTag);
            else DiagnosticHub.Failure(DiagnosticFeature.Download, diagnosticPhase, DiagnosticCode.Unknown, ex, operation: diagnosticOperation, targetVersion: request.Context.ReleaseTag);
            // A transport abort is a failure unless the caller actually cancelled this operation.
            var status = cancellation.IsCancellationRequested ? UpdateDownloadStatus.Cancelled : UpdateDownloadStatus.Failed;
            SafeCancel(operation.Cancellation); return new(status);
        }
        catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Download, diagnosticPhase, DiagnosticCode.Unknown, ex, operation: diagnosticOperation, targetVersion: request.Context.ReleaseTag); SafeCancel(operation.Cancellation); return new(UpdateDownloadStatus.Failed); }
    }
}
