using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static byte[] DistributionZip(IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        using var output = new MemoryStream(); using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var file in files) { var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); stream.Write(file.Value); } return output.ToArray();
    }
    private static void DistributionFinal(DevContext context, string parent)
    {
        var stage = DistributionStage(context, parent); using var key = RSA.Create(3072); const string keyId = "packaging-final-fixture"; var trust = new UpdateTrust([new TrustedUpdateKey(keyId, key.ExportParameters(false))]);
        byte[] Envelope(byte[] descriptor) => Encoding.ASCII.GetBytes("BetterAstralParty.UpdateSignature/v1\nkey-id=" + keyId + "\nsignature=" + Convert.ToBase64String(key.SignData(descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) + "\n");
        foreach (var channel in new[] { "Stable", "PreRelease", "Beta" }) {
            var identity = new PackagingReleaseIdentity(channel == "Stable" ? "1.4.0" : "1.4.0-rc.9", channel, 123, 456);
            var payload = PackagingWhole.Required.ToDictionary(path => path, path => Encoding.UTF8.GetBytes("inert fixture " + path), StringComparer.Ordinal);
            var owned = GaPackageContract.OwnedPaths().Select(path => payload[PackagingWhole.OwnedSource(path)]).ToArray(); var update = GaPackageContract.PreviewUpdateZip(owned);
            var descriptor = GaPackageContract.PrepareUpdateDescriptor(identity.Version, channel, identity.ReleaseId, identity.AssetId, update, owned.Select(bytes => bytes.LongLength).ToArray(), owned.Select(PackagingBoundary.Digest).ToArray());
            payload["update-proof/update.descriptor"] = descriptor; payload["update-proof/update.signature"] = Envelope(descriptor);
            var preliminary = payload.Select(row => new PackagingInput(row.Key, row.Value.LongLength, PackagingBoundary.Digest(row.Value))).ToArray(); var buildIdentity = PackagingWhole.Identity(preliminary, "private-ga-whole-candidate", identity.Version);
            payload["tools/approved-install-bundles.json"] = PackagingBoundary.Bytes(new { schema = 1, bundles = new[] { new { version = identity.Version, kind = "private-ga-whole-candidate", protocol = 1, buildIdentity } } });
            byte[] Manifest(Dictionary<string, byte[]> files) => PackagingBoundary.Bytes(new { schema = 1, version = identity.Version, kind = "private-ga-whole-candidate", buildIdentity = PackagingWhole.Identity(files.Select(row => new PackagingInput(row.Key, row.Value.LongLength, PackagingBoundary.Digest(row.Value))), "private-ga-whole-candidate", identity.Version), files = files.Select(row => new PackagingInput(row.Key, row.Value.LongLength, PackagingBoundary.Digest(row.Value))).OrderBy(row => row.path, StringComparer.Ordinal).ToArray() });
            var manifest = Manifest(payload); var wholeRows = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal) { ["package-manifest.json"] = manifest }; var whole = DistributionZip(wholeRows);
            var sourceFiles = new[] { "developer-tools/Fixture.cs", "README.md", "LICENSE", "offline-nuget.config", "docs/BUILD.md", "docs/INSTALL.md", "Open-Logs.cmd", "Create-Diagnostics.cmd" }.ToDictionary(path => path, path => Encoding.ASCII.GetBytes("inert exported source " + path), StringComparer.Ordinal);
            sourceFiles.Add("src/Plugin.cs", Encoding.ASCII.GetBytes("    public const string Version = \"1.4.0-rc.3.local.12\";\n"));
            var sourceRows = sourceFiles.Select(row => new PackagingInput(row.Key, row.Value.LongLength, PackagingBoundary.Digest(row.Value))).OrderBy(row => row.path, StringComparer.Ordinal).ToArray(); var buildSource = PackagingBoundary.Bytes(sourceRows); var sourceHash = PackagingBoundary.Digest(buildSource);
            var map = new Dictionary<string, string> { ["plugin/BetterAstralParty.dll"] = "artifacts/BetterAstralParty.dll", ["setup/BetterAstralParty-Setup.exe"] = "artifacts/BetterAstralParty-Setup.exe" };
            var generated = BuildVersion.Transform(sourceFiles["src/Plugin.cs"], identity.Version);
            var record = PackagingBoundary.Bytes(new { schema = 1, configuration = "Release", sourceManifestSha256 = sourceHash, checks = BuildCommands.Checks,
                versionOverride = identity.Version, generatedPlugin = new PackagingInput(BuildVersion.GeneratedPath, generated.LongLength, PackagingBoundary.Digest(generated)),
                files = map.Select(row => new PackagingInput(row.Key, payload[row.Value].LongLength, PackagingBoundary.Digest(payload[row.Value]))).ToArray(), compatibility = new PackagingInput("compatibility/BetterAstralParty.compatibility.json", payload["artifacts/BetterAstralParty.compatibility.json"].LongLength, PackagingBoundary.Digest(payload["artifacts/BetterAstralParty.compatibility.json"])), installationPerformed = false, releaseCreated = false });
            var sourceManifest = PackagingBoundary.Bytes(new { schema = 1, version = identity.Version, buildSourceManifestSha256 = sourceHash, files = sourceRows });
            var sourceZipFiles = new Dictionary<string, byte[]>(sourceFiles, StringComparer.Ordinal) { ["SOURCE-MANIFEST.json"] = sourceManifest, ["BUILD-RECORD.json"] = record }; var source = DistributionZip(sourceZipFiles);
            JsonElement Export(byte[] archive, byte[] packageManifest) => PackagingBoundary.Parse(PackagingBoundary.Bytes(new { schema = 1, version = identity.Version, sourceManifestSha256 = sourceHash, packageManifestSha256 = PackagingBoundary.Digest(packageManifest), zipSha256 = PackagingBoundary.Digest(archive), sourceFiles = sourceRows.Length, privateKeyAccessed = false, installed = false, published = false }));
            Dictionary<string, byte[]> Verify(Dictionary<string, byte[]>? candidate = null, byte[]? candidateManifest = null, byte[]? candidateUpdate = null, byte[]? candidateWhole = null, byte[]? candidateSource = null, byte[]? candidateRecord = null, byte[]? candidateBuildSource = null, JsonElement? candidateExport = null, PackagingReleaseIdentity? candidateIdentity = null, UpdateTrust? candidateTrust = null) => PackagingFinal.VerifyTuple(candidateIdentity ?? identity, candidate ?? payload, candidateManifest ?? manifest, candidateUpdate ?? update, candidateWhole ?? whole, candidateSource ?? source, candidateRecord ?? record, candidateBuildSource ?? buildSource, candidateExport ?? Export(source, manifest), candidateTrust ?? trust);
            var assets = Verify(); Check(assets.Count == 5 && assets[identity.AssetName].SequenceEqual(update) && assets[ReleaseFeedPolicy.UpdateManifestAsset].SequenceEqual(descriptor), "actual current-protocol finalizer verifies synthetic signed whole/source/update closure: " + channel);
            var output = Path.GetRelativePath(context.Root, Path.Combine(stage, channel)).Replace('\\', '/'); using (var boundary = new PackagingBoundary(context.Root)) PackagingFinal.WriteAssets(boundary, output, identity, assets);
            var files = Directory.GetFiles(Path.Combine(context.Root, output)); Check(files.Length == 6 && File.ReadAllText(Path.Combine(context.Root, output, "SHA256SUMS")) == PackagingFinal.Checksums(assets), "flat finalizer writes exact five assets and deterministic aggregate SHA256SUMS: " + channel);
            var before = context.Hash(Path.Combine(context.Root, output, "SHA256SUMS")); using (var boundary = new PackagingBoundary(context.Root)) RefuseDistribution(() => PackagingFinal.WriteAssets(boundary, output, identity, assets), "final asset directory is create-new"); Check(context.Hash(Path.Combine(context.Root, output, "SHA256SUMS")) == before, "existing finalized assets remain unchanged");
            RefuseDistribution(() => Verify(candidateTrust: new UpdateTrust([])), "unconfigured production-equivalent trust refuses finalization");
            RefuseDistribution(() => Verify(candidateIdentity: identity with { AssetId = 999 }), "server-assigned asset identity cannot be substituted");
            if(channel=="Stable") {
                var reclassified=Verify(candidateIdentity:identity with{Channel="PreRelease"});
                Check(reclassified.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(assets.Keys)&&reclassified.All(row=>row.Value.SequenceEqual(assets[row.Key])),"GitHub public promotion/demotion preserves every whole/update/source/signature asset byte");
            } else if(channel=="PreRelease") RefuseDistribution(()=>Verify(candidateIdentity:identity with{Channel="Stable"}),"semantic RC version cannot be promoted to Stable without a GA tag");
            RefuseDistribution(() => Verify(candidateUpdate: update.Concat(new byte[] { 0 }).ToArray()), "changed update ZIP refused");
            var missing = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal); missing.Remove("update-proof/update.signature"); RefuseDistribution(() => Verify(candidate: missing), "missing signed update evidence refused");
            var changed = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal) { ["update-proof/update.signature"] = Encoding.ASCII.GetBytes("invalid") }; RefuseDistribution(() => Verify(candidate: changed), "changed proof cannot match manifest");
            void RejectRebound(Dictionary<string, byte[]> candidate, string reason) {
                var candidateIdentity = PackagingWhole.Identity(candidate.Select(row => new PackagingInput(row.Key, row.Value.LongLength, PackagingBoundary.Digest(row.Value))), "private-ga-whole-candidate", identity.Version);
                candidate["tools/approved-install-bundles.json"] = PackagingBoundary.Bytes(new { schema = 1, bundles = new[] { new { version = identity.Version, kind = "private-ga-whole-candidate", protocol = 1, buildIdentity = candidateIdentity } } });
                var candidateManifest = Manifest(candidate); var archived = new Dictionary<string, byte[]>(candidate, StringComparer.Ordinal) { ["package-manifest.json"] = candidateManifest };
                RefuseDistribution(() => Verify(candidate: candidate, candidateManifest: candidateManifest, candidateWhole: DistributionZip(archived), candidateExport: Export(source, candidateManifest)), reason);
            }
            var oldProtocol = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal); oldProtocol["update-proof/update.descriptor"] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(descriptor).Replace("UpdateDescriptor/v5", "UpdateDescriptor/v4", StringComparison.Ordinal)); oldProtocol["update-proof/update.signature"] = Envelope(oldProtocol["update-proof/update.descriptor"]); RejectRebound(oldProtocol, "properly signed previous protocol still refused by native finalizer");
            var wrongSignature = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal); var signatureBytes = key.SignData(descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); signatureBytes[0] ^= 1; wrongSignature["update-proof/update.signature"] = Encoding.ASCII.GetBytes("BetterAstralParty.UpdateSignature/v1\nkey-id=" + keyId + "\nsignature=" + Convert.ToBase64String(signatureBytes) + "\n"); RejectRebound(wrongSignature, "cryptographic signature mismatch refused after manifest/hash rebinding");
            var extraWhole = new Dictionary<string, byte[]>(wholeRows, StringComparer.Ordinal) { ["private.key"] = Encoding.ASCII.GetBytes("inert forbidden") }; RefuseDistribution(() => Verify(candidateWhole: DistributionZip(extraWhole)), "unlisted whole ZIP file refused");
            RefuseDistribution(() => Verify(candidateWhole: DistributionZip(wholeRows.Concat(new[] { wholeRows.First() }))), "duplicate whole ZIP entry refused");
            RefuseDistribution(() => Verify(candidateWhole: DistributionZip(wholeRows.Concat(new[] { new KeyValuePair<string, byte[]>("ARTIFACTS/BetterAstralParty.dll", Encoding.ASCII.GetBytes("inert alias")) }))), "case-alias whole ZIP entry refused");
            var extraSource = new Dictionary<string, byte[]>(sourceZipFiles, StringComparer.Ordinal) { ["unlisted.cs"] = Encoding.ASCII.GetBytes("inert extra") }; var extraArchive = DistributionZip(extraSource); RefuseDistribution(() => Verify(candidateSource: extraArchive, candidateExport: Export(extraArchive, manifest)), "unlisted source ZIP file refused even with matching export digest");
            var changedSource = new Dictionary<string, byte[]>(sourceZipFiles, StringComparer.Ordinal) { ["README.md"] = Encoding.ASCII.GetBytes("changed") }; var changedArchive = DistributionZip(changedSource); RefuseDistribution(() => Verify(candidateSource: changedArchive, candidateExport: Export(changedArchive, manifest)), "source row hash mismatch refused");
            RefuseDistribution(() => Verify(candidateBuildSource: buildSource.Concat(new byte[] { 32 }).ToArray()), "source manifest hash binding required");
            RefuseDistribution(() => Verify(candidateRecord: Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(record).Replace("Release", "Debug", StringComparison.Ordinal))), "partial/debug build cannot authorize release");
            var wrongExport = PackagingBoundary.Parse(PackagingBoundary.Bytes(new { schema = 1, version = identity.Version, sourceManifestSha256 = new string('A', 64), packageManifestSha256 = PackagingBoundary.Digest(manifest), zipSha256 = PackagingBoundary.Digest(source), sourceFiles = sourceRows.Length, privateKeyAccessed = false, installed = false, published = false })); RefuseDistribution(() => Verify(candidateExport: wrongExport), "export source hash binding required");
            var invalidNames = new Dictionary<string, byte[]>(assets, StringComparer.Ordinal) { ["extra.log"] = Encoding.ASCII.GetBytes("inert") }; using (var boundary = new PackagingBoundary(context.Root)) RefuseDistribution(() => PackagingFinal.WriteAssets(boundary, output + "-extra", identity, invalidNames), "final flat asset extras rejected before writes"); Check(!Directory.Exists(Path.Combine(context.Root, output + "-extra")), "rejected flat asset closure creates nothing");
        }
        context.WriteJsonCreateNew(Path.Combine(parent, "release-finalization-matrix.json"), new { actualCryptographicTupleAndArchiveValidator = true, actualCreateNewFlatWriter = true, syntheticBuildRecord = true, fixtureKeyGeneratedInMemory = true, operationalPrivateKeyAccessed = false, productMetadataExecution = false, githubWrites = 0 });
    }
}
