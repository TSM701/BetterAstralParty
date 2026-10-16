using System.Net;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private sealed class DistributionHttp(Func<HttpRequestMessage, int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        internal readonly List<string> Requests = []; internal int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Synthetic distribution must never mutate GitHub.");
            Requests.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(reply(request, ++Count));
        }
    }
    private static void DistributionRemote(DevContext context, string stage)
    {
        var zip = Encoding.ASCII.GetBytes("inert staged ZIP for transport verification only"); var hash = PackagingBoundary.Digest(zip);
        foreach (var channel in new[] { "Stable", "PreRelease", "Beta" }) foreach (var mode in new[] { "valid", "no-digest", "redirect", "anonymous", "regular-beta" }) {
            if (mode == "anonymous" && channel == "Beta" || mode == "regular-beta" && channel != "Beta") continue;
            var identity = new PackagingReleaseIdentity(channel == "Stable" ? "1.3.0" : "1.3.0-rc.9", channel, 123, 456);
            using var handler = RemoteHandler(identity, zip, mode); using var client = new HttpClient(handler);
            var evidence = PackagingBoundary.Parse(PackagingBoundary.Bytes(PackagingRemote.Verify(client, identity, zip.LongLength, hash, mode == "anonymous" ? null : "FAKE_READ_ONLY_FIXTURE_TOKEN").GetAwaiter().GetResult()));
            Check(PackagingBoundary.Number(evidence, "schema") == 1 && PackagingBoundary.Text(evidence, "channel") == channel && PackagingBoundary.Text(evidence, "zipSha256") == hash && evidence.GetProperty("repositoryId").GetInt64() == identity.Feed.RepositoryId && handler.Count >= 7, "actual bounded read-only remote verifier emits exact signing-consumer evidence: " + channel + "/" + mode);
            Check(!Encoding.UTF8.GetString(PackagingBoundary.Bytes(evidence)).Contains("FAKE_READ_ONLY_FIXTURE_TOKEN", StringComparison.Ordinal), "remote evidence never exports credentials");
            Check(PackagingBoundary.Text(evidence,"repositoryVisibility")== (identity.Feed.RequiresAuthentication?"private":"public")&&evidence.GetProperty("prerelease").GetBoolean()==(channel!="Stable"&&mode!="regular-beta"),"remote evidence retains observed GitHub flag and visibility: "+channel+"/"+mode);
        }
        foreach (var channel in new[]{"Stable","PreRelease","Beta"}) foreach (var mode in new[] { "repository-id", "repository-name", "visibility", "release-id", "tag", "prerelease", "changed-prerelease", "asset-id", "asset-name", "asset-state", "asset-size", "asset-origin", "digest", "duplicate-name", "duplicate-id", "binary", "changed", "untrusted-redirect", "json-redirect", "http-error", "oversized-json", "duplicate-json" }) {
            if(channel=="Beta"&&mode=="prerelease")continue;
            var identity = new PackagingReleaseIdentity(channel=="Stable"?"1.3.0":"1.3.0-rc.9", channel, 123, 456); using var handler = RemoteHandler(identity, zip, mode); using var client = new HttpClient(handler);
            RefuseDistribution(() => PackagingRemote.Verify(client, identity, zip.LongLength, hash, "FAKE_READ_ONLY_FIXTURE_TOKEN").GetAwaiter().GetResult(), "remote provenance/content rejection: " +channel+"/"+mode);
        }
        var betaIdentity=new PackagingReleaseIdentity("1.3.0-rc.9","Beta",123,456);using(var handler=RemoteHandler(betaIdentity,zip,"valid"))using(var client=new HttpClient(handler)){RefuseDistribution(()=>PackagingRemote.Verify(client,betaIdentity,zip.LongLength,hash).GetAwaiter().GetResult(),"private beta requires explicit credential before transport");Check(handler.Count==0,"missing private credential sends no request");}
        new PackagingReleaseIdentity("1.3.0","PreRelease",1,2).Validate();Check(true,"public pre-release classification follows GitHub flag rather than requiring a semver suffix");
        foreach (var identity in new[] { new PackagingReleaseIdentity("1.2.1", "Stable", 1, 2), new PackagingReleaseIdentity("1.3.0-rc.1", "Stable", 1, 2), new PackagingReleaseIdentity("1.3.0-rc.1.local.1", "Beta", 1, 2), new PackagingReleaseIdentity("1.3.0", "LocalInstall", 1, 2), new PackagingReleaseIdentity("1.3.0", "Stable", 0, 2) }) RefuseDistribution(identity.Validate, "unsupported/local or missing-server remote identity");
        var artifact = DistributionStage(context, stage); var local = Path.Combine(artifact, "inert-update.zip"); File.WriteAllBytes(local, zip); var retained = Path.Combine(artifact, "retained"); Directory.CreateDirectory(retained); File.WriteAllText(Path.Combine(retained, "keep.txt"), "retained");
        var arguments = new Dictionary<string, string> { ["StageDirectory"] = retained, ["LocalZipPath"] = local, ["LocalZipSha256"] = hash, ["Version"] = "1.3.0", ["Channel"] = "Stable", ["ReleaseId"] = "123", ["AssetId"] = "456" };
        RefuseDistribution(() => PackagingRemote.Run(context.Root, arguments), "existing remote evidence stage refused before credential or network access"); Check(File.ReadAllText(Path.Combine(retained, "keep.txt")) == "retained", "prior remote evidence retained");
        arguments["StageDirectory"] = Path.Combine(artifact, "fresh"); arguments["LocalZipSha256"] = new string('A', 64); RefuseDistribution(() => PackagingRemote.Run(context.Root, arguments), "changed local ZIP refused before network access"); Check(!Directory.Exists(arguments["StageDirectory"]), "rejected remote input creates no output");
        context.WriteJsonCreateNew(Path.Combine(stage, "remote-verification-matrix.json"), new { actualVerifier = true, syntheticHttpOnly = true, liveCredentialRead = false, liveNetworkUsed = false, githubWrites = 0 });
    }
    private static DistributionHttp RemoteHandler(PackagingReleaseIdentity identity, byte[] zip, string mode)
    {
        var prefix = "https://api.github.com/repos/" + identity.Feed.Repository;
        Dictionary<string, object?> Asset(int call) => new() { ["id"] = mode == "asset-id" ? 999 : identity.AssetId, ["name"] = mode == "asset-name" ? "unrelated.zip" : identity.AssetName, ["state"] = mode == "asset-state" ? "new" : "uploaded", ["size"] = mode == "asset-size" ? zip.Length + 1 : zip.Length, ["url"] = mode == "asset-origin" ? "https://attacker.invalid/asset" : prefix + "/releases/assets/" + identity.AssetId, ["digest"] = mode == "no-digest" ? null : "sha256:" + (mode == "digest" || mode == "changed" && call > 4 ? new string('A', 64) : PackagingBoundary.Digest(zip).ToLowerInvariant()) };
        return new DistributionHttp((request, call) => {
            if (request.RequestUri!.Host == "release-assets.githubusercontent.com") {
                Check(request.Headers.Authorization == null, "download redirect strips authorization"); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
            }
            Check(request.RequestUri.Host == "api.github.com" && request.Headers.Authorization?.Parameter == (mode=="anonymous"?null:"FAKE_READ_ONLY_FIXTURE_TOKEN"), "public anonymous access and optional credential stay confined to pinned GitHub API origin");
            if (mode == "http-error") return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("FAKE_SECRET_RESPONSE_BODY") };
            if (mode == "json-redirect") { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri("https://attacker.invalid/credential"); return response; }
            if (request.Headers.Accept.Any(value => value.MediaType == "application/octet-stream")) {
                if (mode is "redirect" or "untrusted-redirect") { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(mode == "redirect" ? "https://release-assets.githubusercontent.com/inert" : "https://attacker.invalid/credential"); return response; }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(mode == "binary" ? Encoding.ASCII.GetBytes("different") : zip) };
            }
            object body;
            if (request.RequestUri.AbsoluteUri == prefix) {var hidden=mode=="visibility"?!identity.Feed.RequiresAuthentication:identity.Feed.RequiresAuthentication;body = new { id = mode == "repository-id" ? 999 : identity.Feed.RepositoryId, full_name = mode == "repository-name" ? "another/repo" : identity.Feed.Repository, @private = hidden, visibility = hidden ? "private" : "public" };}
            else if (request.RequestUri.AbsoluteUri == prefix + "/releases/" + identity.ReleaseId) {
                var asset = Asset(call); var rows = new List<Dictionary<string, object?>> { asset };
                if (mode == "duplicate-name") rows.Add(new(asset) { ["id"] = 789 }); if (mode == "duplicate-id") rows.Add(new(asset) { ["name"] = "other.zip" });
                var prerelease=identity.Channel!="Stable"&&mode!="regular-beta";if(mode=="prerelease"||mode=="changed-prerelease"&&call>4)prerelease=!prerelease;
                body = new { id = mode == "release-id" ? 999 : identity.ReleaseId, tag_name = mode == "tag" ? "1.3.1" : identity.Version, prerelease, draft = true, assets = rows };
            } else body = Asset(call);
            var bytes = mode == "oversized-json" ? new byte[4 * 1024 * 1024 + 1] : mode == "duplicate-json" ? Encoding.UTF8.GetBytes("{\"id\":1,\"id\":2}") : PackagingBoundary.Bytes(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
    }
}
