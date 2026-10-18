#if !NETFRAMEWORK
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BetterAstralParty;
using BetterAstralParty.Updating;

internal static class DownloadFixture
{
    private static int _checks;
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 7, 0, 0, TimeSpan.Zero);
    private static readonly UpdateDownloadRequest Request = new(new UpdateContext(UpdateDownloads.Repository, "Stable",
        "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId), 457, 458);
    private static readonly Uri Cdn = new("https://release-assets.githubusercontent.com/github-production-release-asset/1401226962/12345678-abcd-1234-abcd-123456789abc?fixture=signed-placeholder");
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception("Download fixture: " + message); }
    private sealed class Auth : IReleaseAuthentication
    {
        internal string Value = "fixture-account-a";
        internal bool ThrowIdentity, Refuse, ThrowAuthorize, ChangeDestination, ChangeIdentity;
        internal int Calls;
        public string CacheIdentity => ThrowIdentity ? throw new InvalidOperationException("fixture") : Value;
        public bool TryAuthorize(HttpRequestMessage request)
        {
            Interlocked.Increment(ref Calls);
            if (ThrowAuthorize) throw new InvalidOperationException("fixture");
            if (ChangeDestination) request.RequestUri = new Uri("https://example.invalid/");
            if (ChangeIdentity) Value = "fixture-account-b";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-placeholder-only");
            request.Headers.Add("Cookie", "fixture-cookie"); request.Headers.Add("X-Provider-Private", "fixture-private");
            return !Refuse;
        }
    }
    private sealed record Sent(string Url, string? Authorization, bool Cookie, bool ProviderHeader, bool ApiHeader, int ThreadId);
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<Sent> Requests = new();
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply = (_, _) => throw new InvalidOperationException("fixture unconfigured");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            lock (Requests) Requests.Add(new(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(),
                request.Headers.Contains("Cookie"), request.Headers.Contains("X-Provider-Private"), request.Headers.Contains("X-GitHub-Api-Version"), Environment.CurrentManagedThreadId));
            return Reply(request, cancellation);
        }
        internal int Count { get { lock (Requests) return Requests.Count; } }
        internal Sent[] Snapshot() { lock (Requests) return Requests.ToArray(); }
    }
    private sealed class Response : HttpResponseMessage
    {
        internal volatile bool WasDisposed;
        internal Response(HttpStatusCode code) : base(code) { }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly RSA _signing = RSA.Create(3072);
        internal readonly byte[] Zip, Descriptor, Signature, Envelope;
        internal readonly UpdateTrust Trust;
        internal Fixture()
        {
            var payload = new byte[2048]; new Random(88).NextBytes(payload);
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var path in InstallReceipt.Owned) {
                    var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
                    entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var target = entry.Open(); target.Write(payload);
                }
            }
            Zip = output.ToArray();
            Descriptor = Encoding.ASCII.GetBytes("BetterAstralParty.UpdateDescriptor/v5\nrepository=" + UpdateDownloads.Repository
                + "\nversion=1.4.0\nchannel=Stable\nplatform=" + UpdateTrust.PlatformId
                + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-1.4.0-update.zip\nzip-bytes=" + Zip.Length
                + "\nzip-sha256=" + UpdateTrust.Hash(Zip) + "\nfile-count=4\npurpose=release/Stable\nrepository-id=" + ReleaseFeedPolicy.StableRepositoryId + "\nsettings-schema=1\n"
                + string.Concat(InstallReceipt.Owned.Select(path => "file=" + path + "\t" + payload.Length + "\t" + UpdateTrust.Hash(payload) + "\n")));
            Signature = _signing.SignData(Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            Envelope = Encoding.ASCII.GetBytes("BetterAstralParty.UpdateSignature/v1\nkey-id=download-fixture\nsignature=" + Convert.ToBase64String(Signature) + "\n");
            Trust = new(new[] { new TrustedUpdateKey("download-fixture", _signing.ExportParameters(false)) });
        }
        internal HttpResponseMessage Valid(HttpRequestMessage request)
        {
            if (PublicRepositoryRequest(request)) return PublicRepositoryIdentity();
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases/123", StringComparison.Ordinal)) return Body(Metadata(false));
            return Body(path.EndsWith("/457", StringComparison.Ordinal) ? Descriptor : path.EndsWith("/458", StringComparison.Ordinal) ? Envelope : Zip);
        }
        internal Handler Handler() => new() { Reply = (r, _) => Task.FromResult(Valid(r)) };
        internal byte[] SignEnvelope(byte[] descriptor, string keyId = "download-fixture") => Encoding.ASCII.GetBytes(
            "BetterAstralParty.UpdateSignature/v1\nkey-id=" + keyId + "\nsignature=" + Convert.ToBase64String(_signing.SignData(descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) + "\n");
        public void Dispose() => _signing.Dispose();
    }
    private static HttpResponseMessage Body(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static bool PublicRepositoryRequest(HttpRequestMessage request)
        => request.RequestUri == ReleaseFeedTransport.IdentityEndpoint(ReleaseFeedPolicy.PreRelease);
    private static HttpResponseMessage PublicRepositoryIdentity() => Body(Encoding.UTF8.GetBytes(
        "{\"id\":" + ReleaseFeedPolicy.StableRepositoryId + ",\"full_name\":\"" + ReleaseFeedPolicy.StableRepository + "\",\"private\":false}"));
    private static byte[] Metadata(bool prerelease) => Encoding.UTF8.GetBytes("{\"id\":123,\"draft\":false,\"prerelease\":" + (prerelease ? "true" : "false")
        + ",\"tag_name\":\"1.4.0\",\"html_url\":\"https://github.com/" + ReleaseFeedPolicy.StableRepository + "/releases/tag/1.4.0\",\"assets\":[{\"id\":456,\"name\":\"BetterAstralParty-1.4.0-update.zip\"},"
        + "{\"id\":457,\"name\":\"" + ReleaseFeedPolicy.UpdateManifestAsset + "\"},{\"id\":458,\"name\":\"" + ReleaseFeedPolicy.UpdateSignatureAsset + "\"}]}");
    private static HttpResponseMessage Redirect(Uri uri) { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = uri; return response; }
    private static TaskCompletionSource<HttpResponseMessage> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Until(Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition()) { if (elapsed.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Fixture wait: " + message); await Task.Delay(2); }
    }
    private static async Task<UpdateDownloadResult> Complete(UpdateDownloads client)
    {
        await Until(() => { client.Poll(); return client.Result.Status != UpdateDownloadStatus.Downloading; }, "download completion");
        return client.Result;
    }
    private static async Task Expect(Fixture fixture, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply,
        UpdateDownloadStatus status, string message, UpdateDownloadRequest? request = null, TimeSpan? total = null, TimeSpan? idle = null)
    {
        var handler = new Handler { Reply = reply };
        using var client = new UpdateDownloads(fixture.Trust, new Auth(), handler, total, idle);
        client.Start(request ?? Request, Now);
        var result = await Complete(client);
        Check(result.Status == status && result.Update == null, message + " [" + result.Status + "]");
    }
    private sealed class Content : HttpContent
    {
        private readonly Stream _stream;
        internal Content(Stream stream, long? length = null) { _stream = stream; if (length.HasValue) Headers.ContentLength = length; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_stream);
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellation) => Task.FromResult(_stream);
        protected override void Dispose(bool disposing) { if (disposing) _stream.Dispose(); base.Dispose(disposing); }
    }
    private sealed class HungStream : Stream
    {
        internal readonly TaskCompletionSource<int> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile bool Started, WasDisposed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default) { Started = true; return new(Gate.Task); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    private static async Task Configuration(Fixture fixture)
    {
        foreach (var auth in new Auth?[] { null, new() { Value = "" }, new() { ThrowIdentity = true }, new() { Value = new string('a', 257) } })
        {
            var handler = fixture.Handler(); using var client = new UpdateDownloads(fixture.Trust, auth, handler);
            client.Start(Request, Now); client.Poll(); Check(client.Result.Status == UpdateDownloadStatus.NotConfigured && handler.Count == 0, "unconfigured auth sends no HTTP");
        }
        var noRoot = fixture.Handler(); using (var client = new UpdateDownloads(new UpdateTrust(Array.Empty<TrustedUpdateKey>()), new Auth(), noRoot))
        { client.Start(Request, Now); client.Poll(); Check(client.Result.Status == UpdateDownloadStatus.NotConfigured && noRoot.Count == 0, "empty fixture trust root sends no HTTP"); }
        foreach (var auth in new[] { new Auth { Refuse = true }, new Auth { ThrowAuthorize = true }, new Auth { ChangeDestination = true } })
        {
            var handler = fixture.Handler(); using var client = new UpdateDownloads(fixture.Trust, auth, handler);
            client.Start(Request, Now); Check((await Complete(client)).Status == UpdateDownloadStatus.AuthenticationRequired && handler.Count == 0, "authorization refusal/mutation sends no HTTP");
        }
        var identityMutation = fixture.Handler(); using (var client = new UpdateDownloads(fixture.Trust, new Auth { ChangeIdentity = true }, identityMutation))
        { client.Start(Request, Now); await Complete(client); Check(identityMutation.Count == 0 && client.Result.Update == null, "authorization identity change sends no HTTP"); }
        foreach (var pair in new[] { (457L, 457L), (456L, 458L), (457L, 456L), (0L, 458L) })
        { try { _ = new UpdateDownloadRequest(Request.Context, pair.Item1, pair.Item2); throw new Exception("Bad asset IDs accepted"); } catch (ArgumentException) { Check(true, "distinct positive asset IDs"); } }
    }
    private static async Task ValidAndRedirects(Fixture fixture)
    {
        var handler = fixture.Handler(); using (var client = new UpdateDownloads(fixture.Trust, new Auth(), handler))
        {
            client.Start(Request, Now); Check(client.Result.Status == UpdateDownloadStatus.Downloading, "start returns immediately");
            var result = await Complete(client); Check(result.Status == UpdateDownloadStatus.Ready && result.Update != null && handler.Count == 3, "three direct assets validated");
            using var read = result.Update!.Payload.OpenRead(); using var copy = new MemoryStream(); read.CopyTo(copy);
            Check(UpdateTrust.Hash(copy.ToArray()) == UpdateTrust.Hash(fixture.Zip) && !read.CanWrite, "ready bytes exact and read-only");
            var memory = (MemoryStream)read;
            Check(!memory.TryGetBuffer(out _), "payload array not exposed");
            try { memory.GetBuffer(); throw new Exception("Buffer exposed"); } catch (UnauthorizedAccessException) { Check(true, "GetBuffer denied"); }
            try { read.WriteByte(0); throw new Exception("Write accepted"); } catch (NotSupportedException) { Check(true, "write denied"); }
            using var descriptor = result.Update.OpenDescriptor(); using var signature = result.Update.OpenSignature();
            Check(!descriptor.CanWrite && !signature.CanWrite && !((MemoryStream)descriptor).TryGetBuffer(out _) && !((MemoryStream)signature).TryGetBuffer(out _), "raw signed bytes private");
            var verified = fixture.Trust.VerifyDescriptor(fixture.Descriptor, "download-fixture", fixture.Signature, Request.Context);
            var caller = (byte[])fixture.Zip.Clone(); var payload = UpdatePackage.VerifyPayload(caller, verified); caller[0] ^= 1;
            using var privateRead = payload.OpenRead(); Check(privateRead.ReadByte() == fixture.Zip[0], "caller mutation cannot change verified snapshot");
            try { _ = new VerifiedUpdatePayload(new object(), fixture.Zip, payload.Package); throw new Exception("Invalid stamp accepted"); }
            catch (UpdateValidationException) { Check(true, "unverified payload stamp denied"); }
            try { _ = new DownloadedUpdate(new object(), fixture.Descriptor, "download-fixture", fixture.Signature, payload); throw new Exception("Invalid stamp accepted"); }
            catch (UpdateValidationException) { Check(true, "unverified download stamp denied"); }
            client.Start(Request, Now.AddHours(1)); Check(handler.Count == 3 && ReferenceEquals(client.Result.Update, result.Update), "ready cache avoids duplicate download");
            foreach (var sent in handler.Snapshot()) Check(sent.Authorization == "Bearer fixture-placeholder-only" && sent.ApiHeader && !sent.Cookie && !sent.ProviderHeader && sent.Url.StartsWith("https://api.github.com/repos/TSM701/BetterAstralParty/releases/assets/", StringComparison.Ordinal), "API auth limited/filtered");
            client.Configure(null); Check(client.Result.Status == UpdateDownloadStatus.NotConfigured && client.Result.Update == null, "logout clears ready artifact");
        }
        var redirected = new Handler { Reply = (r, _) => Task.FromResult(r.RequestUri!.Host == "api.github.com" ? Redirect(Cdn) : Body(
            // All three requests redirect to the fixture CDN. Dispatch by request order.
            Array.Empty<byte>())) };
        redirected.Reply = (r, _) => Task.FromResult(r.RequestUri!.Host == "api.github.com" ? Redirect(Cdn)
            : Body(redirected.Count == 2 ? fixture.Descriptor : redirected.Count == 4 ? fixture.Envelope : fixture.Zip));
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), redirected))
        {
            client.Start(Request, Now); Check((await Complete(client)).Status == UpdateDownloadStatus.Ready && redirected.Count == 6, "one CDN redirect per asset accepted");
            foreach (var sent in redirected.Snapshot())
                Check(sent.Url.StartsWith("https://api.github.com", StringComparison.Ordinal) ? sent.Authorization != null && sent.ApiHeader && !sent.Cookie
                    : sent.Authorization == null && !sent.ApiHeader && !sent.Cookie && !sent.ProviderHeader, "external CDN receives no API auth/headers");
        }
        foreach (var target in new[] {
            "http://release-assets.githubusercontent.com" + Cdn.AbsolutePath, "https://example.invalid" + Cdn.AbsolutePath,
            "https://release-assets.githubusercontent.com.evil.invalid" + Cdn.AbsolutePath,
            "https://user@release-assets.githubusercontent.com" + Cdn.AbsolutePath, "https://release-assets.githubusercontent.com:444" + Cdn.AbsolutePath,
            "https://release-assets.githubusercontent.com:443" + Cdn.AbsolutePath, Cdn.AbsoluteUri + "#fragment",
            "https://release-assets.githubusercontent.com/github-production-release-asset/1401226963/12345678-abcd-1234-abcd-123456789abc",
            "https://release-assets.githubusercontent.com/github-production-release-asset/1401226962/../1401226962/12345678-abcd-1234-abcd-123456789abc",
            "https://release-assets.githubusercontent.com/github-production-release-asset/1401226962/%31%32%33%34%35%36%37%38-abcd-1234-abcd-123456789abc",
            "https://release-assets.githubusercontent.com" + Cdn.AbsolutePath + "/file.zip", "https://api.github.com/repos/TSM701/BetterAstralParty/releases/assets/456",
            "/relative", Cdn.AbsoluteUri + new string('a', 8192) })
        {
            var bad = new Handler { Reply = (_, _) => Task.FromResult(Redirect(new Uri(target, UriKind.RelativeOrAbsolute))) };
            using var client = new UpdateDownloads(fixture.Trust, new Auth(), bad);
            client.Start(Request, Now); Check((await Complete(client)).Status == UpdateDownloadStatus.Failed && bad.Count == 1, "unsafe redirect blocked before destination request");
        }
        var again = new Handler { Reply = (_, _) => Task.FromResult(Redirect(Cdn)) };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), again))
        { client.Start(Request, Now); Check((await Complete(client)).Status == UpdateDownloadStatus.Failed && again.Count == 2, "second redirect rejected"); }
        await Expect(fixture, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/"), Content = new ByteArrayContent(fixture.Descriptor) }), UpdateDownloadStatus.Failed, "unexpected automatic redirect detected");
        var betaDescriptor = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(fixture.Descriptor)
            .Replace("version=1.4.0\n", "version=1.4.0-rc.1\n").Replace("channel=Stable", "channel=Beta")
            .Replace("repository=" + ReleaseFeedPolicy.StableRepository + "\n", "repository=" + ReleaseFeedPolicy.BetaRepository + "\n")
            .Replace("repository-id=" + ReleaseFeedPolicy.StableRepositoryId, "repository-id=" + ReleaseFeedPolicy.BetaRepositoryId).Replace("purpose=release/Stable", "purpose=release/Beta")
            .Replace("BetterAstralParty-1.4.0-update.zip", "BetterAstralParty-1.4.0-rc.1-update.zip"));
        var betaEnvelope = fixture.SignEnvelope(betaDescriptor);
        var betaRequest = new UpdateDownloadRequest(new UpdateContext(ReleaseFeedPolicy.BetaRepository, "Beta", "1.4.0-dev", "1.4.0-rc.1", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.BetaRepositoryId), 457, 458);
        var betaHandler = new Handler { Reply = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(betaDescriptor)
            : r.RequestUri.AbsolutePath.EndsWith("/458", StringComparison.Ordinal) ? Body(betaEnvelope) : Body(fixture.Zip)) };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), betaHandler))
        { client.Start(betaRequest, Now); var beta = await Complete(client); Check(beta.Status == UpdateDownloadStatus.Ready && beta.Update!.Payload.Package.Descriptor.Version == "1.4.0-rc.1", "Beta RC signed download accepted"); }
        var stableRequest = new UpdateDownloadRequest(new UpdateContext(UpdateDownloads.Repository, "Stable", "1.4.0-dev", "1.4.0-rc.1", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId), 457, 458);
        await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(betaDescriptor) : Body(betaEnvelope)), UpdateDownloadStatus.VerificationFailed, "Stable rejects Beta signed descriptor", stableRequest);
        var preDescriptor = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(fixture.Descriptor).Replace("channel=Stable\n", "channel=PreRelease\n").Replace("purpose=release/Stable\n", "purpose=release/PreRelease\n"));
        var preEnvelope = fixture.SignEnvelope(preDescriptor);
        var preRequest = new UpdateDownloadRequest(new UpdateContext(ReleaseFeedPolicy.StableRepository, "PreRelease", "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId), 457, 458);
        using var preHandler = new Handler { Reply = (r, _) => Task.FromResult(PublicRepositoryRequest(r) ? PublicRepositoryIdentity() : r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(preDescriptor)
            : r.RequestUri.AbsolutePath.EndsWith("/458", StringComparison.Ordinal) ? Body(preEnvelope) : r.RequestUri.AbsolutePath.EndsWith("/releases/123", StringComparison.Ordinal) ? Body(Metadata(true)) : Body(fixture.Zip)) };
        var neverPrivate = new Auth { ThrowIdentity = true, ThrowAuthorize = true };
        using (var client = new UpdateDownloads(fixture.Trust, neverPrivate, preHandler, feed: ReleaseFeedPolicy.PreRelease)) {
            client.Start(preRequest, Now); var pre = await Complete(client);
            Check(pre.Status == UpdateDownloadStatus.Ready && pre.Update!.Payload.Package.Descriptor.Channel == "PreRelease", "public prerelease signed download retains its exact independent channel [" + pre.Status + "]");
            var preRequests = preHandler.Snapshot();
            var publicEndpoint = ReleaseFeedTransport.IdentityEndpoint(ReleaseFeedPolicy.PreRelease).AbsoluteUri;
            Check(neverPrivate.Calls == 0 && preHandler.Count == 6 && preRequests.All(sent => sent.Authorization == null && !sent.Cookie && !sent.ProviderHeader)
                && preRequests.Select(sent => sent.Url).SequenceEqual(new[] { publicEndpoint, publicEndpoint + "/releases/assets/457", publicEndpoint + "/releases/assets/458", publicEndpoint + "/releases/assets/456", publicEndpoint, publicEndpoint + "/releases/123" }), "public prerelease identity, descriptor, signature and ZIP are followed by fresh identity/classification without private credentials");
        }
        await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(preDescriptor) : Body(preEnvelope)), UpdateDownloadStatus.VerificationFailed, "same public repository cannot promote a signed PreRelease descriptor to Stable", Request);
        using var wrongPreHandler = fixture.Handler();
        using (var client = new UpdateDownloads(fixture.Trust, neverPrivate, wrongPreHandler, feed: ReleaseFeedPolicy.PreRelease)) {
            client.Start(preRequest, Now); var wrongPre = await Complete(client);
            var publicEndpoint = ReleaseFeedTransport.IdentityEndpoint(ReleaseFeedPolicy.PreRelease).AbsoluteUri;
            Check(wrongPre.Status == UpdateDownloadStatus.VerificationFailed && wrongPreHandler.Snapshot().Select(sent => sent.Url)
                .SequenceEqual(new[] { publicEndpoint, publicEndpoint + "/releases/assets/457", publicEndpoint + "/releases/assets/458" }), "after accepted public identity, signed Stable descriptor cannot be relabeled PreRelease and ZIP is never requested");
            Check(neverPrivate.Calls == 0 && wrongPreHandler.Snapshot().All(sent => sent.Authorization == null && !sent.Cookie && !sent.ProviderHeader), "wrong-channel rejection also remains anonymous");
        }
    }
    private static async Task PublicClassifications(Fixture fixture)
    {
        var descriptor = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(fixture.Descriptor).Replace("purpose=release/Stable\n", "purpose=" + ReleaseFeedPolicy.PublicPurpose + "\n"));
        var envelope = fixture.SignEnvelope(descriptor);
        foreach (var channel in new[] { "Stable", "PreRelease" }) foreach (var prerelease in new[] { false, true }) {
            var feed = ReleaseFeedPolicy.For(channel); var context = new UpdateContext(feed.Repository, channel, "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId, repositoryId: feed.RepositoryId);
            var request = new UpdateDownloadRequest(context, 457, 458); var privateSpy = new Auth { ThrowIdentity = true, ThrowAuthorize = true };
            using var handler = new Handler { Reply = (r, _) => Task.FromResult(PublicRepositoryRequest(r) ? PublicRepositoryIdentity()
                : r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(descriptor)
                : r.RequestUri.AbsolutePath.EndsWith("/458", StringComparison.Ordinal) ? Body(envelope)
                : r.RequestUri.AbsolutePath.EndsWith("/releases/123", StringComparison.Ordinal) ? Body(Metadata(prerelease)) : Body(fixture.Zip)) };
            using var client = new UpdateDownloads(fixture.Trust, privateSpy, handler, feed: feed); client.Start(request, Now); var result = await Complete(client);
            var accepted = prerelease == (channel == "PreRelease");
            Check(result.Status == (accepted ? UpdateDownloadStatus.Ready : UpdateDownloadStatus.Cancelled), "same exact public signature follows live classification " + channel + " " + prerelease);
            Check(privateSpy.Calls == 0 && handler.Count == 6 && handler.Snapshot().All(sent => sent.Authorization == null), "live public classification is bounded and anonymous");
            if (accepted) {
                using var raw = result.Update!.OpenDescriptor(); using var copy = new MemoryStream(); raw.CopyTo(copy);
                Check(copy.ToArray().SequenceEqual(descriptor), "both public classifications preserve exact signed descriptor bytes");
                var verified = result.Update.Payload.Package.Descriptor;
                var text = Encoding.UTF8.GetString(Metadata(prerelease));
                foreach (var invalid in new[] { text.Replace("\"id\":123", "\"id\":124"), text.Replace("\"id\":456", "\"id\":999"), text.Replace("\"id\":457", "\"id\":459"),
                    text.Replace("\"id\":123", "\"id\":123,\"id\":123"), text.Replace("\"id\":123", "\"id\":123.0"), text.Replace("1.4.0", "1.4.1"), text.Replace("\"draft\":false", "\"draft\":null") }) {
                    try { PublicReleaseEligibility.Release(Encoding.UTF8.GetBytes(invalid), context, verified, 457, 458); throw new Exception("Ambiguous/mismatched public metadata accepted"); }
                    catch (Exception error) when (error is InvalidDataException or UpdateValidationException or System.Text.Json.JsonException) { Check(true, "release ID, tag, ZIP/metadata assets and strict unique keys are revalidated"); }
                }
                Check(!PublicReleaseEligibility.Release(Encoding.UTF8.GetBytes(text.Replace("\"draft\":false", "\"draft\":true")), context, verified, 457, 458), "draft public release is never eligible");
            }
        }
        foreach (var failure in new[] { "network", "redirect", "malformed" }) {
            using var handler = new Handler { Reply = (r, _) => {
                if (!r.RequestUri!.AbsolutePath.EndsWith("/releases/123", StringComparison.Ordinal)) return Task.FromResult(fixture.Valid(r));
                if (failure == "network") throw new HttpRequestException("inert public classification unavailable");
                return Task.FromResult(failure == "redirect" ? Redirect(new Uri("https://untrusted.invalid/metadata")) : Body(Encoding.UTF8.GetBytes("{}")));
            } };
            using var client = new UpdateDownloads(fixture.Trust, null, handler, feed: ReleaseFeedPolicy.Stable); client.Start(Request, Now);
            Check((await Complete(client)).Status == UpdateDownloadStatus.Cancelled && client.Result.Update == null, "unavailable live public classification is transient cancellation " + failure);
        }
    }
    private static async Task Failures(Fixture fixture)
    {
        foreach (var code in new[] { 401, 403, 404, 429, 500, 301, 307, 308 })
            await Expect(fixture, (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)), code == 401 ? UpdateDownloadStatus.AuthenticationRequired
                : code == 403 || code == 404 ? UpdateDownloadStatus.AccessUnavailable : code == 429 ? UpdateDownloadStatus.RateLimited : UpdateDownloadStatus.Failed, "HTTP " + code);
        await Expect(fixture, (_, _) => throw new HttpRequestException("fixture offline, private URL omitted"), UpdateDownloadStatus.Failed, "offline failure sanitized");
        await Expect(fixture, (_, _) => Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true)), UpdateDownloadStatus.Failed, "transport abort is not a user cancellation");
        await Expect(fixture, (_, _) => Task.FromResult(Body(new byte[UpdateTrust.MaxDescriptorBytes + 1])), UpdateDownloadStatus.TooLarge, "descriptor declared budget");
        await Expect(fixture, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(new MemoryStream(new byte[UpdateTrust.MaxDescriptorBytes + 1])) }), UpdateDownloadStatus.TooLarge, "descriptor actual budget without content length");
        await Expect(fixture, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(new MemoryStream(fixture.Descriptor), fixture.Descriptor.Length + 1) }), UpdateDownloadStatus.VerificationFailed, "truncated declared body");
        await Expect(fixture, (_, _) => { var r = Body(fixture.Descriptor); r.Content.Headers.ContentEncoding.Add("gzip"); return Task.FromResult(r); }, UpdateDownloadStatus.Failed, "encoded body refused");
        await Expect(fixture, (_, _) => Task.FromResult(Body(Array.Empty<byte>())), UpdateDownloadStatus.VerificationFailed, "empty descriptor");
        await Expect(fixture, (r, _) =>
        {
            if (!r.RequestUri!.AbsolutePath.EndsWith("/456", StringComparison.Ordinal)) return Task.FromResult(fixture.Valid(r));
            var result = Body(fixture.Zip); result.Content.Headers.ContentLength = UpdateTrust.MaxZipBytes + 1;
            return Task.FromResult(result);
        }, UpdateDownloadStatus.TooLarge, "ZIP 64 MiB budget before reading");
        foreach (var envelope in new[] { Encoding.ASCII.GetBytes("invalid"), Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(fixture.Envelope).Replace("\n", "\r\n")),
            Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(fixture.Envelope).Replace("download-fixture", "untrusted-key")),
            Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(fixture.Envelope).Replace("signature=", "signature= ")), new byte[UpdateDownloads.MaxEnvelopeBytes + 1] })
            await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/458", StringComparison.Ordinal) ? Body(envelope) : fixture.Valid(r)), envelope.Length > UpdateDownloads.MaxEnvelopeBytes ? UpdateDownloadStatus.TooLarge : UpdateDownloadStatus.VerificationFailed, "signature envelope rejected");
        var tampered = (byte[])fixture.Descriptor.Clone(); tampered[4] ^= 1;
        await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(tampered) : fixture.Valid(r)), UpdateDownloadStatus.VerificationFailed, "tampered descriptor stops before ZIP");
        var wrong = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(fixture.Descriptor).Replace("asset-id=456", "asset-id=459"));
        var wrongEnvelope = fixture.SignEnvelope(wrong);
        await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(wrong) : Body(wrongEnvelope)), UpdateDownloadStatus.VerificationFailed, "signed mismatched asset target");
        var downgrade = new UpdateDownloadRequest(new UpdateContext(UpdateDownloads.Repository, "Stable", "1.4.1", "1.4.0", 123, 456, UpdateTrust.PlatformId), 457, 458);
        await Expect(fixture, (r, _) => Task.FromResult(fixture.Valid(r)), UpdateDownloadStatus.VerificationFailed, "download context downgrade", downgrade);
        var highWater = new UpdateDownloadRequest(new UpdateContext(UpdateDownloads.Repository, "Stable", "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId, "1.4.0"), 457, 458);
        await Expect(fixture, (r, _) => Task.FromResult(fixture.Valid(r)), UpdateDownloadStatus.VerificationFailed, "highest-applied replay refused", highWater);
        var signatureBlocked = new Handler { Reply = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/457", StringComparison.Ordinal) ? Body(tampered) : fixture.Valid(r)) };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), signatureBlocked))
        { client.Start(Request, Now); await Complete(client); Check(signatureBlocked.Count == 2 && client.Result.ValidationFailure == UpdateFailure.InvalidSignature, "untrusted descriptor never fetches ZIP"); }
        await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/456", StringComparison.Ordinal) ? Body(fixture.Zip[..^1]) : fixture.Valid(r)), UpdateDownloadStatus.VerificationFailed, "ZIP declared length mismatch");
        var badZip = (byte[])fixture.Zip.Clone(); badZip[100] ^= 1;
        await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/456", StringComparison.Ordinal) ? Body(badZip) : fixture.Valid(r)), UpdateDownloadStatus.VerificationFailed, "ZIP hash mismatch");
        var extraZip = fixture.Zip.Concat(new byte[] { 0 }).ToArray();
        await Expect(fixture, (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/456", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(new MemoryStream(extraZip)) } : fixture.Valid(r)), UpdateDownloadStatus.VerificationFailed, "ZIP actual length mismatch without header");
        await Expect(fixture, (_, _) => Gate().Task, UpdateDownloadStatus.TimedOut, "header idle timeout", idle: TimeSpan.FromMilliseconds(45));
        var hung = new HungStream();
        await Expect(fixture, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(hung) }), UpdateDownloadStatus.TimedOut, "body idle timeout", idle: TimeSpan.FromMilliseconds(45));
        Check(hung.WasDisposed, "timed out body disposed"); hung.Gate.TrySetException(new IOException("fixture late fault"));
        await Expect(fixture, async (r, _) => { await Task.Delay(35); return fixture.Valid(r); }, UpdateDownloadStatus.TimedOut, "operation total deadline", total: TimeSpan.FromMilliseconds(80), idle: TimeSpan.FromMilliseconds(75));
    }
    private static async Task Coordination(Fixture fixture)
    {
        var gate = Gate(); var handler = new Handler { Reply = (_, _) => gate.Task }; var auth = new Auth();
        using (var client = new UpdateDownloads(fixture.Trust, auth, handler))
        {
            client.Start(Request, Now); await Until(() => handler.Count == 1, "blocked header");
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++) { client.Start(Request, Now.AddHours(1)); client.Poll(); }
            Check(stopwatch.Elapsed < TimeSpan.FromSeconds(1) && handler.Count == 1 && client.Result.Status == UpdateDownloadStatus.Downloading, "1000 Start/Poll calls nonblocking/singleflight");
            client.Cancel(); Check(client.Result.Status == UpdateDownloadStatus.Cancelled && client.Result.Update == null, "cancel detaches worker");
            client.Start(Request, Now.AddSeconds(1)); Check(client.Result.Status == UpdateDownloadStatus.RateLimited && handler.Count == 1, "cancel retains cooldown");
            var late = new Response(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Descriptor) }; gate.SetResult(late);
            await Until(() => late.WasDisposed, "late response disposal"); client.Poll(); Check(client.Result.Update == null, "late response cannot resurrect artifact");
        }
        var cancelSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var badCallback = new Handler { Reply = (_, ct) => { ct.Register(() => { cancelSignal.TrySetResult(true); throw new InvalidOperationException("fixture cancellation callback"); }); return Gate().Task; } };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), badCallback))
        { client.Start(Request, Now); await Until(() => badCallback.Count == 1, "callback request"); await Task.Delay(10); client.Cancel(); Check(await cancelSignal.Task && client.Result.Status == UpdateDownloadStatus.Cancelled, "throwing cancellation callback contained"); }
        var oldGate = Gate(); var identityHandler = new Handler { Reply = (_, _) => oldGate.Task }; var identityAuth = new Auth();
        using (var client = new UpdateDownloads(fixture.Trust, identityAuth, identityHandler))
        {
            client.Start(Request, Now); await Until(() => identityHandler.Count == 1, "identity request"); identityAuth.Value = "fixture-account-b"; client.Poll();
            Check(client.Result.Status == UpdateDownloadStatus.Idle && client.Result.Update == null, "identity change clears active private context");
            client.Start(Request, Now.AddSeconds(1)); Check(client.Result.Status == UpdateDownloadStatus.RateLimited && identityHandler.Count == 1, "account switch cannot bypass cooldown");
            var late = new Response(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Descriptor) }; oldGate.SetResult(late);
            await Until(() => late.WasDisposed, "identity late response"); client.Poll(); Check(client.Result.Update == null, "old account response ignored");
        }
        var oldRateGate = Gate(); var oldRateHandler = new Handler { Reply = (_, _) => oldRateGate.Task }; var oldRateAuth = new Auth();
        using (var client = new UpdateDownloads(fixture.Trust, oldRateAuth, oldRateHandler))
        {
            client.Start(Request, Now); await Until(() => oldRateHandler.Count == 1, "retired rate request");
            oldRateAuth.Value = "fixture-account-b"; client.Poll();
            var lateRate = new Response(HttpStatusCode.TooManyRequests); lateRate.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(4)); oldRateGate.SetResult(lateRate);
            await Until(() => lateRate.WasDisposed, "retired rate disposal"); client.Start(Request, Now.AddMinutes(2));
            Check(oldRateHandler.Count == 1 && client.Result.Status == UpdateDownloadStatus.RateLimited && client.Result.RetryAt == Now.AddMinutes(4), "late old-account server backoff retained");
        }
        var body = new HungStream(); var bodyHandler = new Handler { Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(body) }) };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), bodyHandler))
        {
            client.Start(Request, Now); await Until(() => body.Started, "cancel body read"); client.Cancel();
            await Until(() => body.WasDisposed, "cancel body disposal"); Check(client.Result.Status == UpdateDownloadStatus.Cancelled && client.Result.Update == null, "body cancellation disposes stream/no artifact"); body.Gate.TrySetResult(0);
        }
        var contextGate = Gate(); var contextHandler = new Handler { Reply = (_, _) => contextGate.Task };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), contextHandler))
        {
            client.Start(Request, Now); await Until(() => contextHandler.Count == 1, "context blocked request");
            var changed = new UpdateDownloadRequest(new UpdateContext(UpdateDownloads.Repository, "Stable", "1.4.0-dev", "1.4.1", 124, 459, UpdateTrust.PlatformId), 460, 461);
            client.Start(changed, Now.AddSeconds(1)); Check(client.Result.Status == UpdateDownloadStatus.RateLimited && contextHandler.Count == 1, "active release replacement clears worker/retains cooldown");
            var late = new Response(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Descriptor) }; contextGate.SetResult(late);
            await Until(() => late.WasDisposed, "context late disposal"); client.Poll(); Check(client.Result.Update == null, "old release result ignored");
        }
        var readyHandler = fixture.Handler(); using (var client = new UpdateDownloads(fixture.Trust, new Auth(), readyHandler))
        {
            client.Start(Request, Now); await Complete(client);
            var other = new UpdateDownloadRequest(new UpdateContext(UpdateDownloads.Repository, "Beta", "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId), 457, 458);
            client.Start(other, Now.AddSeconds(1)); Check(client.Result.Status == UpdateDownloadStatus.RateLimited && client.Result.Update == null && readyHandler.Count == 3, "channel change clears ready/cooldown retained");
            client.Start(other, Now.AddSeconds(61)); Check((await Complete(client)).Status == UpdateDownloadStatus.VerificationFailed && client.Result.Update == null, "failed new context never returns old ready binary");
        }
        var rate = new Handler { Reply = (_, _) => { var r = new HttpResponseMessage(HttpStatusCode.Forbidden); r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(3)); return Task.FromResult(r); } };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), rate))
        {
            client.Start(Request, Now); var result = await Complete(client); Check(result.Status == UpdateDownloadStatus.RateLimited && result.RetryAt == Now.AddMinutes(3), "403 RetryAfter recognized");
            client.Configure(new Auth { Value = "fixture-account-b" }); client.Start(Request, Now.AddMinutes(2)); Check(rate.Count == 1 && client.Result.RetryAt == Now.AddMinutes(3), "server backoff shared across providers");
            client.Start(Request, Now.AddMinutes(4)); await Complete(client); Check(rate.Count == 2, "retry permitted after server backoff");
        }
        var rateReset = new Handler { Reply = (_, _) => { var r = new HttpResponseMessage(HttpStatusCode.Forbidden); r.Headers.Add("X-RateLimit-Remaining", "0"); r.Headers.Add("X-RateLimit-Reset", Now.AddMinutes(5).ToUnixTimeSeconds().ToString()); return Task.FromResult(r); } };
        using (var client = new UpdateDownloads(fixture.Trust, new Auth(), rateReset))
        { client.Start(Request, Now); var result = await Complete(client); Check(result.Status == UpdateDownloadStatus.RateLimited && result.RetryAt == Now.AddMinutes(5), "primary rate reset recognized"); }
        var disposeGate = Gate(); var disposed = new Handler { Reply = (_, _) => disposeGate.Task }; var disposable = new UpdateDownloads(fixture.Trust, new Auth(), disposed);
        disposable.Start(Request, Now); await Until(() => disposed.Count == 1, "dispose request"); disposable.Dispose(); disposable.Dispose(); disposable.Poll(); disposable.Start(Request, Now.AddHours(1));
        Check(disposable.Result.Status == UpdateDownloadStatus.NotConfigured && disposed.Count == 1 && disposable.Result.Update == null, "dispose idempotent and blocks new request");
        var disposedResponse = new Response(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Descriptor) }; disposeGate.SetResult(disposedResponse);
        await Until(() => disposedResponse.WasDisposed, "disposed late response cleanup");
    }
    internal static async Task Run()
    {
        using var fixture = new Fixture();
        await Configuration(fixture); await ValidAndRedirects(fixture); await PublicClassifications(fixture); await Failures(fixture); await Coordination(fixture);
        Console.WriteLine("PASS: update download " + _checks + " assertions; fake HTTPS handlers only; credential isolation/limits/immutable bytes/cancellation/context/cache; no network, file staging, install or process changes.");
    }
}
#endif
