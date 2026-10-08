using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using BetterAstralParty.Updating;
namespace BetterAstralParty;
internal static class ReleaseFeedTransport
{
    internal const int MaxRepositoryBytes = 32768;
    internal static Uri IdentityEndpoint(ReleaseFeed feed) => new("https://api.github.com/repos/" + feed.Repository);
    internal static bool Authorize(HttpRequestMessage request, IReleaseAuthentication? authentication, ReleaseFeed? feed)
    {
        if (feed is { RequiresAuthentication: false }) return request.Headers.Authorization == null;
        if (authentication == null) return false;
        var destination = request.RequestUri;
        using var probe = new HttpRequestMessage(HttpMethod.Get, destination);
        bool ok; try { ok = authentication.TryAuthorize(probe); } catch { return false; }
        var header = probe.Headers.Authorization;
        if (!ok || probe.Method != HttpMethod.Get || probe.RequestUri != destination || header?.Scheme != "Bearer"
            || string.IsNullOrWhiteSpace(header.Parameter) || header.Parameter.Length > 8192) return false;
        request.Headers.Authorization = header; return true;
    }
    internal static void VerifyIdentity(byte[] bytes, ReleaseFeed feed)
    {
        if (!feed.Configured || bytes.Length is 0 or > MaxRepositoryBytes) throw new InvalidDataException("Repository identity unavailable");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement; var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Repository identity mismatch");
        foreach (var property in root.EnumerateObject()) if (!seen.Add(property.Name)) throw new InvalidDataException("Ambiguous repository identity");
        if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var actualId) || actualId != feed.RepositoryId
            || !root.TryGetProperty("full_name", out var name) || name.ValueKind != JsonValueKind.String || !string.Equals(name.GetString(),feed.Repository,StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("private", out var visibility) || visibility.ValueKind != (feed.RequiresAuthentication ? JsonValueKind.True : JsonValueKind.False))
            throw new InvalidDataException("Repository identity mismatch");
    }
}
