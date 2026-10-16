using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static class PackagingFinal
{
    internal static Dictionary<string, byte[]> Archive(byte[] bytes, int maximumFiles, long maximumBytes)
    {
        if (bytes.Length == 0 || bytes.LongLength > maximumBytes) throw new InvalidDataException("Release archive bound.");
        using var memory = new MemoryStream(bytes, false); using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal); var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        if (archive.Entries.Count == 0 || archive.Entries.Count > maximumFiles) throw new InvalidDataException("Release archive entry bound.");
        foreach (var entry in archive.Entries) {
            PackagingBoundary.PathName(entry.FullName);
            var kind = (entry.ExternalAttributes >> 16) & 0xf000;
            if (!aliases.Add(entry.FullName) || kind is not (0 or 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || entry.Length <= 0 || entry.Length > 64 * 1024 * 1024 || (total += entry.Length) > maximumBytes) throw new InvalidDataException("Duplicate, linked, or oversized release archive entry.");
            using var stream = entry.Open(); entries.Add(entry.FullName, PackagingWhole.ReadEntry(stream, entry.Length));
        }
        return entries;
    }
    internal static void ExactRows(Dictionary<string, byte[]> files, Dictionary<string, PackagingInput> rows)
    {
        PackagingExport.Closure(files.Keys, rows.Keys);
        foreach (var row in rows.Values) if (files[row.path].LongLength != row.bytes || PackagingBoundary.Digest(files[row.path]) != row.sha256) throw new InvalidDataException("Archive row differs: " + row.path);
    }
    internal static Dictionary<string, byte[]> VerifyTuple(PackagingReleaseIdentity identity, Dictionary<string, byte[]> payload, byte[] manifestBytes, byte[] updateZip, byte[] wholeZip, byte[] sourceZip, byte[] buildRecord, byte[] buildSource, JsonElement sourceExport, UpdateTrust trust)
    {
        identity.Validate(); var manifest = PackagingBoundary.Parse(manifestBytes);
        if (PackagingBoundary.Number(manifest, "schema") != 1 || PackagingBoundary.Text(manifest, "version") != identity.Version || PackagingBoundary.Text(manifest, "kind") != "private-ga-whole-candidate") throw new InvalidDataException("Current approved whole manifest required.");
        var rows = PackagingBoundary.Rows(manifest, 17); ExactRows(payload, rows);
        var buildIdentity = PackagingWhole.Identity(rows.Values, "private-ga-whole-candidate", identity.Version);
        if (buildIdentity != PackagingBoundary.Text(manifest, "buildIdentity")) throw new InvalidDataException("Whole content identity changed.");
        foreach (var required in PackagingWhole.Required.Concat(new[] { "helper-bootstrap/helper.descriptor", "helper-bootstrap/helper.signature", "update-proof/update.descriptor", "update-proof/update.signature" })) if (!payload.ContainsKey(required)) throw new InvalidDataException("Complete signed release input missing: " + required);
        var catalog = PackagingBoundary.Parse(payload["tools/approved-install-bundles.json"]);
        if (PackagingBoundary.Number(catalog, "schema") != 1 || catalog.GetProperty("bundles").GetArrayLength() != 1) throw new InvalidDataException("One approved native bundle required.");
        var approved = catalog.GetProperty("bundles")[0];
        if (PackagingBoundary.Text(approved, "version") != identity.Version || PackagingBoundary.Text(approved, "kind") != "private-ga-whole-candidate" || PackagingBoundary.Number(approved, "protocol") != 1 || PackagingBoundary.Text(approved, "buildIdentity") != buildIdentity) throw new InvalidDataException("Whole approval identity differs.");
        var descriptor = payload["update-proof/update.descriptor"]; var signature = payload["update-proof/update.signature"];
        var owned = GaPackageContract.OwnedPaths(); var ownedBytes = owned.Select(path => payload[PackagingWhole.OwnedSource(path)]).ToArray();
        var expected = GaPackageContract.PrepareUpdateDescriptor(identity.Version, identity.Channel, identity.ReleaseId, identity.AssetId, updateZip, ownedBytes.Select(bytes => bytes.LongLength).ToArray(), ownedBytes.Select(PackagingBoundary.Digest).ToArray());
        if (!expected.SequenceEqual(descriptor) || !GaPackageContract.PreviewUpdateZip(ownedBytes).SequenceEqual(updateZip)) throw new InvalidDataException("Canonical current-protocol descriptor/ZIP tuple differs.");
        var envelope = DetachedSignature.Parse(signature); var feed = identity.Feed;
        var verified = trust.VerifyProposal(descriptor, envelope.KeyId, envelope.Signature, new UpdateContext(feed.Repository, identity.Channel, "1.3.0-0", identity.Version, identity.ReleaseId, identity.AssetId, UpdateTrust.PlatformId, repositoryId: feed.RepositoryId));
        if (verified.Protocol != 4 || verified.SettingsSchema != 1 || verified.Files.Count != owned.Length) throw new InvalidDataException("Current protocol and complete owned payload required.");
        UpdatePackage.VerifyPayload(updateZip, verified);
        var helperImage = payload["helper-bootstrap/" + HelperDescriptor.Image]; var helperDescriptor = payload["helper-bootstrap/helper.descriptor"]; var helperSignature = payload["helper-bootstrap/helper.signature"];
        if (!GaPackageContract.PrepareHelper(identity.Version, helperImage).SequenceEqual(helperDescriptor) || HelperDescriptor.Verify(trust, helperDescriptor, DetachedSignature.Parse(helperSignature), helperImage, identity.Version).Protocol != 4) throw new InvalidDataException("Current signed helper tuple differs.");
        var whole = Archive(wholeZip, 1025, 256 * 1024 * 1024); var wholeExpected = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal) { ["package-manifest.json"] = manifestBytes };
        ExactRows(whole, wholeExpected.ToDictionary(row => row.Key, row => new PackagingInput(row.Key, row.Value.LongLength, PackagingBoundary.Digest(row.Value)), StringComparer.Ordinal));
        var sourceHash = PackagingBoundary.Digest(buildSource); var record = PackagingBoundary.Parse(buildRecord);
        if (PackagingBoundary.Number(record, "schema") != 1 || PackagingBoundary.Text(record, "configuration") != "Release" || PackagingBoundary.Text(record, "sourceManifestSha256") != sourceHash || PackagingBoundary.Flag(record, "installationPerformed") || PackagingBoundary.Flag(record, "releaseCreated")) throw new InvalidDataException("Full non-mutating source-bound build required.");
        PackagingExport.Checks(record.GetProperty("checks").EnumerateArray().Select(value => value.GetString() ?? throw new InvalidDataException("Build check text required.")).ToArray());
        var binaryMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["plugin/BetterAstralParty.dll"] = "artifacts/BetterAstralParty.dll", ["launcher/BetterAstralParty-Launcher.exe"] = "artifacts/BetterAstralParty-Launcher.exe", ["helper/BetterAstralParty-UpdateHelper.exe"] = "helper-bootstrap/BetterAstralParty-UpdateHelper.exe" };
        var binaryRows = PackagingBoundary.Rows(record, 3, 3); PackagingExport.Closure(binaryRows.Keys, binaryMap.Keys);
        foreach (var row in binaryRows.Values) if (row.sha256 != PackagingBoundary.Digest(payload[binaryMap[row.path]]) || row.bytes != payload[binaryMap[row.path]].LongLength) throw new InvalidDataException("Product build/whole package differs.");
        var compatibility = record.GetProperty("compatibility");
        if (PackagingBoundary.Text(compatibility, "path") != "compatibility/BetterAstralParty.compatibility.json" || compatibility.GetProperty("bytes").GetInt64() != payload["artifacts/BetterAstralParty.compatibility.json"].LongLength || PackagingBoundary.Text(compatibility, "sha256") != PackagingBoundary.Digest(payload["artifacts/BetterAstralParty.compatibility.json"])) throw new InvalidDataException("Reviewed compatibility input/package differs.");
        var source = Archive(sourceZip, 4098, 256 * 1024 * 1024);
        if (!source.TryGetValue("SOURCE-MANIFEST.json", out var sourceManifestBytes) || !source.TryGetValue("BUILD-RECORD.json", out var archivedRecord) || !archivedRecord.SequenceEqual(buildRecord)) throw new InvalidDataException("Source archive build provenance missing or changed.");
        var sourceManifest = PackagingBoundary.Parse(sourceManifestBytes); var sourceRows = PackagingBoundary.Rows(sourceManifest, 1, 4096);
        if (PackagingBoundary.Number(sourceManifest, "schema") != 1 || PackagingBoundary.Text(sourceManifest, "version") != identity.Version || PackagingBoundary.Text(sourceManifest, "buildSourceManifestSha256") != sourceHash) throw new InvalidDataException("Source archive manifest binding differs.");
        foreach (var row in sourceRows.Values) PackagingExport.SourceName(row.path);
        source.Remove("SOURCE-MANIFEST.json"); source.Remove("BUILD-RECORD.json"); ExactRows(source, sourceRows);
        if (!source.TryGetValue("src/Plugin.cs", out var pluginSource)) throw new InvalidDataException("Archived Plugin.cs required for build version provenance.");
        BuildVersion.Validate(pluginSource, record, identity.Version);
        var buildRows = PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new { files = PackagingBoundary.Parse(buildSource) })), 1, 4096);
        foreach (var row in buildRows.Values) if (!sourceRows.TryGetValue(row.path, out var exported) || exported != row) throw new InvalidDataException("Recorded build source omitted or changed in source ZIP.");
        var extras = new[] { "README.md", "LICENSE", "offline-nuget.config", "docs/BUILD.md", "docs/INSTALL.md", "Open-Logs.cmd", "Create-Diagnostics.cmd" }.Concat(payload.Keys.Where(path => path.StartsWith("THIRD-PARTY-NOTICES/", StringComparison.Ordinal) || path.StartsWith("THIRD-PARTY-SOURCE/", StringComparison.Ordinal)));
        PackagingExport.Closure(sourceRows.Keys, buildRows.Keys.Concat(extras).Distinct(StringComparer.Ordinal));
        if (PackagingBoundary.Number(sourceExport, "schema") != 1 || PackagingBoundary.Text(sourceExport, "version") != identity.Version || PackagingBoundary.Text(sourceExport, "sourceManifestSha256") != sourceHash || PackagingBoundary.Text(sourceExport, "packageManifestSha256") != PackagingBoundary.Digest(manifestBytes) || PackagingBoundary.Text(sourceExport, "zipSha256") != PackagingBoundary.Digest(sourceZip) || PackagingBoundary.Number(sourceExport, "sourceFiles") != sourceRows.Count || PackagingBoundary.Flag(sourceExport, "privateKeyAccessed") || PackagingBoundary.Flag(sourceExport, "installed") || PackagingBoundary.Flag(sourceExport, "published")) throw new InvalidDataException("Source export evidence differs.");
        return new Dictionary<string, byte[]>(StringComparer.Ordinal) {
            ["BetterAstralParty-" + identity.Version + ".zip"] = wholeZip, [identity.AssetName] = updateZip, ["BetterAstralParty-" + identity.Version + "-source.zip"] = sourceZip,
            [ReleaseFeedPolicy.UpdateManifestAsset] = descriptor, [ReleaseFeedPolicy.UpdateSignatureAsset] = signature, ["helper.descriptor"] = helperDescriptor, ["helper.signature"] = helperSignature, [HelperDescriptor.Image] = helperImage
        };
    }
    internal static string Checksums(Dictionary<string, byte[]> assets) => string.Concat(assets.OrderBy(row => row.Key, StringComparer.Ordinal).Select(row => PackagingBoundary.Digest(row.Value) + "  " + row.Key + "\n"));
    internal static void WriteAssets(PackagingBoundary boundary, string output, PackagingReleaseIdentity identity, Dictionary<string, byte[]> assets)
    {
        var names = new[] { "BetterAstralParty-" + identity.Version + ".zip", identity.AssetName, "BetterAstralParty-" + identity.Version + "-source.zip", ReleaseFeedPolicy.UpdateManifestAsset, ReleaseFeedPolicy.UpdateSignatureAsset, "helper.descriptor", "helper.signature", HelperDescriptor.Image };
        PackagingExport.Closure(assets.Keys, names); boundary.Files.Recheck(); boundary.Output(output);
        foreach (var asset in assets) boundary.Write(asset.Key, asset.Value);
        boundary.Write("SHA256SUMS", Encoding.ASCII.GetBytes(Checksums(assets))); boundary.Files.Recheck();
    }
    internal static object Run(string root, Dictionary<string, string> args)
    {
        using var boundary = new PackagingBoundary(root);
        var package = boundary.Stage(PackagingCommands.Required(args, "PackageDirectory")); var source = boundary.Stage(PackagingCommands.Required(args, "SourceDirectory")); var build = boundary.Relative(PackagingCommands.Required(args, "BuildDirectory")); var output = boundary.Stage(PackagingCommands.Required(args, "OutputDirectory"));
        foreach (var path in new[] { package, source, build }) PackagingBoundary.Separate(path, output); boundary.Fresh(output);
        var identity = new PackagingReleaseIdentity(PackagingCommands.Required(args, "Version"), PackagingCommands.Required(args, "Channel"), long.Parse(PackagingCommands.Required(args, "ReleaseId"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture), long.Parse(PackagingCommands.Required(args, "AssetId"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture)); identity.Validate();
        var state = boundary.Json(package + "/candidate-state.json", PackagingCommands.Required(args, "CandidateStateSha256"));
        if (PackagingBoundary.Number(state, "schema") != 1 || PackagingBoundary.Text(state, "version") != identity.Version || PackagingBoundary.Text(state, "kind") != "private-ga-whole-candidate" || !PackagingBoundary.Flag(state, "installableRelease") || !PackagingBoundary.Flag(state, "catalogRegistered") || !PackagingBoundary.Flag(state, "updateProofVerified") || !PackagingBoundary.Flag(state, "helperSignatureVerified") || PackagingBoundary.Text(state, "wholeZip") != "BetterAstralParty-" + identity.Version + ".zip") throw new InvalidDataException("Approved release package required.");
        var manifestBytes = boundary.Capture(package + "/payload/package-manifest.json", PackagingCommands.Required(args, "PackageManifestSha256")); var manifest = PackagingBoundary.Parse(manifestBytes); var rows = PackagingBoundary.Rows(manifest, 17); var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var row in rows.Values) payload.Add(row.path, boundary.Capture(package + "/payload/" + row.path, row.sha256, row.bytes));
        PackagingExport.Closure(boundary.Inventory(package + "/payload").Select(path => path[(package.Length + 9)..]), payload.Keys.Append("package-manifest.json"));
        if (PackagingBoundary.Text(state, "buildIdentity") != PackagingBoundary.Text(manifest, "buildIdentity") || PackagingBoundary.Text(state, "updateProofDescriptorSha256") != PackagingBoundary.Digest(payload["update-proof/update.descriptor"]) || PackagingBoundary.Text(state, "helperDescriptorSha256") != PackagingBoundary.Digest(payload["helper-bootstrap/helper.descriptor"]) || PackagingBoundary.Text(state, "helperImageSha256") != PackagingBoundary.Digest(payload["helper-bootstrap/" + HelperDescriptor.Image])) throw new InvalidDataException("Candidate state/proof/helper bindings differ.");
        var wholeZip = boundary.Capture(package + "/BetterAstralParty-" + identity.Version + ".zip", PackagingBoundary.Text(state, "wholeZipSha256")); var updateZip = boundary.Capture(package + '/' + identity.AssetName, PackagingBoundary.Text(state, "updateZipSha256"));
        var export = boundary.Json(source + "/source-export.json", PackagingCommands.Required(args, "SourceExportSha256")); var sourceZip = boundary.Capture(source + "/BetterAstralParty-" + identity.Version + "-source.zip", PackagingBoundary.Text(export, "zipSha256"));
        var buildRecord = boundary.Capture(build + "/build-result.json", PackagingCommands.Required(args, "BuildRecordSha256")); var buildSource = boundary.Capture(build + "/build-source.json", PackagingCommands.Required(args, "SourceManifestSha256"));
        var assets = VerifyTuple(identity, payload, manifestBytes, updateZip, wholeZip, sourceZip, buildRecord, buildSource, export, UpdateTrust.Production());
        // Retain independent production trust checks and metadata inspection at the operational boundary.
        GaPackageContract.VerifyWholeUpdate(identity.Version, assets[ReleaseFeedPolicy.UpdateManifestAsset], assets[ReleaseFeedPolicy.UpdateSignatureAsset], updateZip);
        GaPackageContract.VerifyHelper(identity.Version, assets["helper.descriptor"], assets["helper.signature"], assets[HelperDescriptor.Image]);
        foreach (var pair in new[] { ("Plugin", "artifacts/BetterAstralParty.dll"), ("Launcher", "artifacts/BetterAstralParty-Launcher.exe"), ("Helper", "helper-bootstrap/" + HelperDescriptor.Image) }) PackagingImage.Inspect(payload[pair.Item2], pair.Item1, identity.Version);
        var matrix = PackagingBoundary.Parse(payload["THIRD-PARTY-NOTICES/component-matrix.json"]); PackagingLicense.Verify(payload.ToDictionary(row => row.Key, row => new PackagingCaptured("", row.Value, PackagingBoundary.Digest(row.Value)), StringComparer.Ordinal), payload.Keys.Where(path => path.StartsWith(".deps/bepinex/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal), PackagingBoundary.Text(matrix, "upstreamArchiveSha256"));
        var recordedSource = PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new { files = PackagingBoundary.Parse(buildSource) })), 1, 4096);
        foreach (var row in recordedSource.Values) boundary.Capture(row.path, row.sha256, row.bytes);
        BuildVersion.ValidateBuild(boundary, build, PackagingBoundary.Parse(buildRecord), recordedSource, identity.Version);
        WriteAssets(boundary, output, identity, assets);
        return new { outputDirectory = output, version = identity.Version, files = assets.Count + 1, sourceManifestSha256 = PackagingBoundary.Digest(buildSource), packageManifestSha256 = PackagingBoundary.Digest(manifestBytes), privateKeyAccessed = false, signingCalls = 0, githubWrites = 0 };
    }
}
