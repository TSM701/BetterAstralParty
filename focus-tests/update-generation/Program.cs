using System.Text.Json;
using BetterAstralParty;
using BetterAstralParty.Updating;
using Legacy = Frozen112.ReleaseUpdates;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Main()
    {
        var currentChecks = UpdateGenerationCheck.Run();
        foreach (var version in new[] { "1.3.1", "1.4.0", "2.0.0" }) {
            var body = UpdateGenerationCheck.Release(version, UpdateGenerationCheck.Assets(version));
            foreach (var old in new[] { "1.0.0", "1.1.0", "1.1.1", "1.1.2" }) {
                var legacy = Legacy.Select(Legacy.Parse(body, ReleaseFeedPolicy.StableRepository), old, ReleaseChannel.Stable);
                Check(legacy?.Version == version, "frozen 1.1.2 client still sees the latest stable notification");
                Check(legacy!.ReleaseId == 0 && legacy.ZipAssetId == 0 && legacy.DescriptorAssetId == 0 && legacy.SignatureAssetId == 0
                    && legacy.DownloadRequest(old, ReleaseChannel.Stable) == null, "frozen legacy asset parser cannot create a future automatic request");
            }
            var current = ReleaseUpdates.Select(ReleaseUpdates.Parse(body, ReleaseFeedPolicy.StableRepository), "1.3.0", ReleaseChannel.Stable);
            Check(current?.DownloadRequest("1.3.0", ReleaseChannel.Stable)?.Context.ReleaseTag == version, "new baseline accepts each future stable target");
        }
        var manual = Legacy.Select(Legacy.Parse(UpdateGenerationCheck.Release("1.3.0", "[]"), ReleaseFeedPolicy.StableRepository), "1.1.2", ReleaseChannel.Stable);
        Check(manual?.Version == "1.3.0" && manual.DownloadRequest("1.1.2", ReleaseChannel.Stable) == null, "frozen client sees the fresh-install baseline without an automatic request");
        var oldTuple = UpdateGenerationCheck.Assets("1.3.1")
            .Replace(ReleaseFeedPolicy.UpdateManifestAsset, "BetterAstralParty.update.manifest")
            .Replace(ReleaseFeedPolicy.UpdateSignatureAsset, "BetterAstralParty.update.signature");
        var oldRelease = Legacy.Parse(UpdateGenerationCheck.Release("1.3.1", oldTuple), ReleaseFeedPolicy.StableRepository).Single();
        Check(oldRelease.DownloadRequest("1.1.2", ReleaseChannel.Stable) != null, "positive legacy control exposes why old asset names must never be published for future releases");
        var newRelease = ReleaseUpdates.Parse(UpdateGenerationCheck.Release("1.3.1", oldTuple), ReleaseFeedPolicy.StableRepository).Single();
        Check(newRelease.DownloadRequest("1.3.0", ReleaseChannel.Stable) == null, "new generation has no fallback to the legacy tuple");
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = true, CurrentChecks = currentChecks, FrozenLegacyChecks = _checks,
            Scope = "Exact SHA-bound 1.1.2 Parse/Assets/Select/DownloadRequest source and current linked discovery; memory-only JSON, no HTTP/helper/game/install/key." }));
    }
}
