using System.Text.Json;
using System.Globalization;
using SemVersion = SemanticVersioning.Version;
using BetterAstralParty;
using BetterAstralParty.Updating;
using Updating = BetterAstralParty.Updating;
namespace Frozen112;
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
internal static class ReleaseUpdates {
internal const int MaxBytes = global::BetterAstralParty.ReleaseUpdates.MaxBytes;
internal const int MaxNotes = global::BetterAstralParty.ReleaseUpdates.MaxNotes;
internal static bool TryVersion(string tag, out SemVersion version) => global::BetterAstralParty.ReleaseUpdates.TryVersion(tag, out version);
internal static bool TryReleasePage(string value, string repository, out Uri? uri, string? tag = null) => global::BetterAstralParty.ReleaseUpdates.TryReleasePage(value, repository, out uri, tag);
internal static string PlainText(string value, int limit) => global::BetterAstralParty.ReleaseUpdates.PlainText(value, limit);
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
}
