using System.IO.Compression;
using System.Text;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static class PackagingPreparation
{
    internal static object Run(string root, Dictionary<string, string> args)
    {
        using var boundary = new PackagingBoundary(root); var build = boundary.Relative(PackagingCommands.Required(args, "BuildDirectory")); var output = boundary.Stage(PackagingCommands.Required(args, "OutputDirectory")); var upstream = boundary.Relative(PackagingCommands.Required(args, "UpstreamArchive"), false);
        PackagingBoundary.Separate(build, output); var scope = args.GetValueOrDefault("Scope", "LocalInstall"); if (scope is not ("LocalInstall" or "Release")) throw new ArgumentException("Unknown preparation scope.");
        var pointer = boundary.Json("artifacts/development-maintenance.json"); var package = PackagingBoundary.Text(pointer, "packagePath"); PackagingBoundary.PathName(package);
        if (!package.StartsWith("artifacts/", StringComparison.Ordinal)) throw new InvalidDataException("Approved package must be project-local.");
        PackagingBoundary.Separate(package, output);
        var baseManifestHash = PackagingBoundary.Text(pointer, "manifestSha256"); var manifest = boundary.Json(package + "/package-manifest.json", baseManifestHash); var baseRows = PackagingBoundary.Rows(manifest, 17);
        var record = boundary.Json(build + "/build-result.json"); var sourceBytes = boundary.Capture(build + "/build-source.json"); var sourceHash = PackagingBoundary.Digest(sourceBytes);
        if (PackagingBoundary.Number(record, "schema") != 1 || PackagingBoundary.Text(record, "configuration") != "Release" || PackagingBoundary.Text(record, "sourceManifestSha256") != sourceHash || PackagingBoundary.Flag(record, "installationPerformed") || PackagingBoundary.Flag(record, "releaseCreated")) throw new InvalidDataException("Full source-bound Release build required.");
        var source = PackagingBoundary.Parse(sourceBytes); var sourceRows = PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new { files = source })), 1, 4096);
        foreach (var row in sourceRows.Values) boundary.Capture(row.path, row.sha256, row.bytes);
        var buildRows = PackagingBoundary.Rows(record, 3, 3); var replacements = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["artifacts/BetterAstralParty.dll"] = "plugin/BetterAstralParty.dll", ["artifacts/BetterAstralParty-Launcher.exe"] = "launcher/BetterAstralParty-Launcher.exe", ["helper-bootstrap/BetterAstralParty-UpdateHelper.exe"] = "helper/BetterAstralParty-UpdateHelper.exe"
        };
        if (!buildRows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(replacements.Values)) throw new InvalidDataException("Exact plugin/launcher/helper build outputs required.");
        foreach (var row in buildRows.Values) boundary.Capture(build + '/' + row.path, row.sha256, row.bytes);
        var compatibility = record.GetProperty("compatibility"); var compatibilityPath = PackagingBoundary.Text(compatibility, "path");
        if (compatibilityPath != "compatibility/BetterAstralParty.compatibility.json") throw new InvalidDataException("Exact source-bound compatibility output required.");
        var compatibilityBytes = boundary.Capture(build + '/' + compatibilityPath, PackagingBoundary.Text(compatibility, "sha256"), compatibility.GetProperty("bytes").GetInt64());
        var version = PackagingImage.PluginVersion(boundary.Capture(build + "/plugin/BetterAstralParty.dll")); var parsed = UpdateVersion.Parse(version);
        BuildVersion.ValidateBuild(boundary, build, record, sourceRows, version);
        if (parsed.CompareTo(UpdateVersion.Parse("1.3.0-0")) < 0) throw new InvalidDataException("Fresh 1.3.0 baseline required.");
        if (scope == "LocalInstall" && (!parsed.IsLocalPrerelease || parsed.CompareTo(UpdateVersion.Parse(PackagingBoundary.Text(pointer, "version"))) <= 0)) throw new InvalidDataException("Distinct increasing local prerelease required.");
        if (scope == "Release" && parsed.IsLocalPrerelease) throw new InvalidDataException("Local prereleases cannot be remote release inputs.");
        var upstreamHash = PackagingCommands.Required(args, "UpstreamSha256"); var archiveBytes = boundary.Capture(upstream, upstreamHash);
        byte[] loaderConfiguration;
        using (var memory = new MemoryStream(archiveBytes, false)) using (var archive = new ZipArchive(memory, ZipArchiveMode.Read)) {
            var entries = archive.Entries.Where(entry => entry.FullName == "doorstop_config.ini").ToArray();
            if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > 65536) throw new InvalidDataException("Unique bounded upstream loader configuration required.");
            using var stream = entries[0].Open(); loaderConfiguration = PackagingWhole.ReadEntry(stream, entries[0].Length);
        }
        var inputs = new Dictionary<string, byte[]>(StringComparer.Ordinal); var names = new HashSet<string>(PackagingWhole.Required, StringComparer.Ordinal);
        foreach (var name in baseRows.Keys) if (name.StartsWith(".deps/bepinex/", StringComparison.Ordinal) || name.StartsWith("THIRD-PARTY-NOTICES/", StringComparison.Ordinal) || name.StartsWith("THIRD-PARTY-SOURCE/", StringComparison.Ordinal)) names.Add(name);
        foreach (var path in GaPackageContract.OwnedPaths().Select(PackagingWhole.OwnedSource)) names.Add(path);
        foreach (var name in names.Order(StringComparer.Ordinal)) {
            byte[] bytes;
            if (name == ".deps/bepinex/doorstop_config.ini") bytes = loaderConfiguration;
            else if (name == "tools/approved-install-bundles.json") bytes = PackagingBoundary.Bytes(new { schema = 1, bundles = Array.Empty<object>() });
            else if (name == "artifacts/BetterAstralParty.compatibility.json") bytes = compatibilityBytes;
            else if (replacements.TryGetValue(name, out var replacement)) bytes = boundary.Capture(build + '/' + replacement);
            else if (name.StartsWith(".deps/bepinex/", StringComparison.Ordinal) || name.StartsWith("THIRD-PARTY-SOURCE/", StringComparison.Ordinal) || name.StartsWith("THIRD-PARTY-NOTICES/", StringComparison.Ordinal) && name is not ("THIRD-PARTY-NOTICES/component-matrix.json" or "THIRD-PARTY-NOTICES/SOURCES.md")) {
                if (!baseRows.TryGetValue(name, out var row)) throw new InvalidDataException("Approved vendor asset missing: " + name);
                bytes = boundary.Capture(package + '/' + name, row.sha256, row.bytes);
            } else {
                if (!sourceRows.TryGetValue(name, out var row)) throw new InvalidDataException("Package-owned source absent from immutable build: " + name);
                bytes = boundary.Capture(name, row.sha256, row.bytes);
                if (scope == "LocalInstall" && name == "README.md") bytes = Encoding.UTF8.GetBytes("Local development installation: " + version + ". Not published to GitHub.\n\n" + Encoding.UTF8.GetString(bytes));
            }
            inputs.Add(name, bytes);
        }
        boundary.Output(output); var rows = new List<PackagingInput>();
        foreach (var input in inputs) { boundary.Write("files/" + input.Key, input.Value); rows.Add(new(input.Key, input.Value.LongLength, PackagingBoundary.Digest(input.Value))); }
        boundary.Write("upstream-loader.zip", archiveBytes);
        var spec = PackagingBoundary.Bytes(new { schema = 1, version, upstreamArchiveSha256 = upstreamHash, productSourceManifestSha256 = sourceHash, basePackageManifestSha256 = baseManifestHash, files = rows.OrderBy(row => row.path, StringComparer.Ordinal).ToArray() });
        boundary.Write("input-manifest.json", spec); boundary.Write("build-source.json", sourceBytes);
        boundary.Write("preparation.json", PackagingBoundary.Bytes(new { scope, version, inputManifestSha256 = PackagingBoundary.Digest(spec), buildRecordSha256 = boundary.Files.SourceHash(build + "/build-result.json"), buildSourceManifestSha256 = sourceHash, basePackageManifestSha256 = baseManifestHash, installed = false, published = false }));
        boundary.Files.Recheck(); return new { version, inputDirectory = Path.Combine(root, output), inputManifestSha256 = PackagingBoundary.Digest(spec), installed = false, published = false };
    }
}
