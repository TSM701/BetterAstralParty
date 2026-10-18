using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal sealed record PackagingReleaseIdentity(string Version, string Channel, long ReleaseId, long AssetId)
{
    internal ReleaseFeed Feed => ReleaseFeedPolicy.For(Channel);
    internal string AssetName => "BetterAstralParty-" + Version + "-update.zip";
    internal void Validate()
    {
        var parsed = UpdateVersion.Parse(Version);
        if (parsed.Tag != Version || parsed.IsLocalPrerelease || parsed.CompareTo(UpdateVersion.Parse(ReleaseFeedPolicy.Baseline + "-0")) < 0 || !ReleaseFeedPolicy.IsChannel(Channel) || Channel == "Stable" && parsed.IsPrerelease || ReleaseId <= 0 || AssetId <= 0 || !Feed.Configured)
            throw new InvalidDataException("Current-generation exact remote release identity required.");
    }
}

internal static class PackagingRemote
{
    private static long Number(JsonElement value, string name) => value.GetProperty(name).GetInt64();
    private static string Text(JsonElement value, string name) => PackagingBoundary.Text(value, name);
    internal static object Run(string root, Dictionary<string, string> args)
    {
        using var boundary = new PackagingBoundary(root);
        var stage = boundary.Stage(PackagingCommands.Required(args, "StageDirectory"));
        var zipPath = boundary.Relative(PackagingCommands.Required(args, "LocalZipPath")); PackagingBoundary.Separate(zipPath, stage);
        var zip = boundary.Capture(zipPath, PackagingCommands.Required(args, "LocalZipSha256"));
        var identity = new PackagingReleaseIdentity(PackagingCommands.Required(args, "Version"), PackagingCommands.Required(args, "Channel"), long.Parse(PackagingCommands.Required(args, "ReleaseId"), NumberStyles.None, CultureInfo.InvariantCulture), long.Parse(PackagingCommands.Required(args, "AssetId"), NumberStyles.None, CultureInfo.InvariantCulture));
        identity.Validate(); if (zip.Length == 0 || zip.LongLength > UpdateTrust.MaxZipBytes) throw new InvalidDataException("Update ZIP bound.");
        boundary.Fresh(stage);
        string? token = null;
        if (args.TryGetValue("TokenEnvironmentVariable", out var variable)) {
            if (!Regex.IsMatch(variable, "\\A[A-Za-z_][A-Za-z0-9_]{0,127}\\z")) throw new ArgumentException("Explicit process token environment name required.");
            token = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsWhiteSpace)) throw new InvalidDataException("Read-only GitHub credential unavailable or invalid.");
        }
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var evidence = Verify(client, identity, zip.LongLength, PackagingBoundary.Digest(zip), token).GetAwaiter().GetResult();
        boundary.Files.Recheck(); boundary.Output(stage); var bytes = PackagingBoundary.Bytes(evidence); boundary.Write("staged-asset-verification.json", bytes); boundary.Files.Recheck();
        return new { evidencePath = stage + "/staged-asset-verification.json", evidenceSha256 = PackagingBoundary.Digest(bytes), githubWrites = 0, privateKeyAccessed = false };
    }
    internal static async Task<object> Verify(HttpClient client, PackagingReleaseIdentity identity, long zipBytes, string zipHash, string? token = null)
    {
        identity.Validate(); PackagingBoundary.Hash(zipHash); if (zipBytes <= 0 || zipBytes > UpdateTrust.MaxZipBytes) throw new InvalidDataException("Update ZIP bound.");
        if (identity.Feed.RequiresAuthentication && string.IsNullOrEmpty(token)) throw new InvalidDataException("Private repository authentication required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var prefix = "https://api.github.com/repos/" + identity.Feed.Repository;
        bool? observedPrerelease = null;
        async Task<JsonElement> Json(string url) => PackagingBoundary.Parse(await Get(client, new Uri(url), token, 4 * 1024 * 1024, timeout.Token));
        void Repository(JsonElement repository) {
            if (Number(repository, "id") != identity.Feed.RepositoryId || Text(repository, "full_name") != identity.Feed.Repository || repository.GetProperty("private").GetBoolean() != identity.Feed.RequiresAuthentication || Text(repository, "visibility") != (identity.Feed.RequiresAuthentication ? "private" : "public")) throw new InvalidDataException("GitHub repository identity or visibility changed.");
        }
        JsonElement Release(JsonElement release) {
            var prerelease = release.GetProperty("prerelease").GetBoolean();
            if (Number(release, "id") != identity.ReleaseId || Text(release, "tag_name") != identity.Version || !identity.Feed.Accepts(prerelease) || observedPrerelease.HasValue && observedPrerelease.Value != prerelease) throw new InvalidDataException("GitHub release identity, tag, or channel changed.");
            observedPrerelease = prerelease;
            // Draft releases are valid staging targets; publication is a separately authorized operation.
            _ = release.GetProperty("draft").GetBoolean();
            var rows = release.GetProperty("assets").EnumerateArray().ToArray(); if (rows.Length is 0 or > 100) throw new InvalidDataException("Bounded staged asset inventory required.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var ids = new HashSet<long>();
            foreach (var row in rows) if (!names.Add(Text(row, "name")) || Number(row, "id") <= 0 || !ids.Add(Number(row, "id"))) throw new InvalidDataException("Duplicate staged asset identity.");
            var matches = rows.Where(row => Text(row, "name") == identity.AssetName).ToArray(); if (matches.Length != 1) throw new InvalidDataException("Exact staged update asset missing."); return matches[0];
        }
        void Asset(JsonElement asset) {
            if (Number(asset, "id") != identity.AssetId || Text(asset, "name") != identity.AssetName || Text(asset, "state") != "uploaded" || Number(asset, "size") != zipBytes || Text(asset, "url") != prefix + "/releases/assets/" + identity.AssetId.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("GitHub asset identity, state, size, or origin changed.");
            if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind != JsonValueKind.Null && (digest.ValueKind != JsonValueKind.String || !string.Equals(digest.GetString(), "sha256:" + zipHash, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("GitHub asset digest differs from local ZIP.");
        }
        Repository(await Json(prefix)); var listed = Release(await Json(prefix + "/releases/" + identity.ReleaseId.ToString(CultureInfo.InvariantCulture))); Asset(listed);
        var assetUrl = prefix + "/releases/assets/" + identity.AssetId.ToString(CultureInfo.InvariantCulture); var direct = await Json(assetUrl); Asset(direct);
        // Verify bytes even when server digest is absent; never forward credentials through a download redirect.
        var binary = await Get(client, new Uri(assetUrl), token, UpdateTrust.MaxZipBytes, timeout.Token, binary: true);
        if (binary.LongLength != zipBytes || PackagingBoundary.Digest(binary) != zipHash) throw new InvalidDataException("Downloaded staged asset differs from local ZIP.");
        Repository(await Json(prefix)); Asset(Release(await Json(prefix + "/releases/" + identity.ReleaseId.ToString(CultureInfo.InvariantCulture)))); Asset(await Json(assetUrl));
        return new { schema = 1, repository = identity.Feed.Repository, repositoryId = identity.Feed.RepositoryId, channel = identity.Channel, version = identity.Version, releaseId = identity.ReleaseId, assetId = identity.AssetId, assetName = identity.AssetName, zipSha256 = zipHash, zipBytes, assetState = "uploaded", repositoryVisibility = identity.Feed.RequiresAuthentication ? "private" : "public", prerelease = observedPrerelease ?? throw new InvalidDataException("Verified release flag missing.") };
    }
    private static async Task<byte[]> Get(HttpClient client, Uri initial, string? token, long maximum, CancellationToken cancel, bool binary = false)
    {
        var uri = initial;
        for (var redirects = 0; redirects <= 3; redirects++) {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !(uri.Host == "api.github.com" || binary && uri.Host == "release-assets.githubusercontent.com")) throw new InvalidDataException("Untrusted GitHub response origin.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("BetterAstralParty-Developer/1.3"); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/octet-stream" : "application/vnd.github+json")); request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (uri.Host == "api.github.com" && token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect) {
                if (!binary || response.Headers.Location == null || redirects == 3) throw new InvalidDataException("Unexpected GitHub redirect.");
                uri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                if (uri.Host != "release-assets.githubusercontent.com") throw new InvalidDataException("Untrusted GitHub download redirect."); continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("GitHub read failed with HTTP " + (int)response.StatusCode + ".");
            if (response.Content.Headers.ContentLength is long length && (length < 0 || length > maximum)) throw new InvalidDataException("GitHub response bound.");
            using var stream = await response.Content.ReadAsStreamAsync(cancel); using var output = new MemoryStream(); var buffer = new byte[65536];
            while (true) { var count = await stream.ReadAsync(buffer, cancel); if (count == 0) break; if (output.Length + count > maximum) throw new InvalidDataException("GitHub response bound."); output.Write(buffer, 0, count); }
            return output.ToArray();
        }
        throw new InvalidDataException("GitHub redirect limit.");
    }
}
