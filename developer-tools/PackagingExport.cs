using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

internal static class PackagingExport
{
    internal static void Checks(string[] checks)
    {
        if (!checks.SequenceEqual(BuildCommands.Checks, StringComparer.Ordinal) || checks.Length == 0 || checks.Length > 1024 || checks.Any(check => check.Length > 128 || !Regex.IsMatch(check, "\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z")) || checks.Distinct(StringComparer.Ordinal).Count() != checks.Length) throw new InvalidDataException("Full source-bound C# build check inventory required.");
    }
    internal static void SourceName(string name)
    {
        PackagingBoundary.PathName(name); if (!BuildCommands.IsPublicSourcePath(name)) throw new InvalidDataException("Inactive private/historical source rejected: " + name); var extension = Path.GetExtension(name).ToLowerInvariant();
        if (Regex.IsMatch(name, "(^|/)(artifacts|releases|\\.deps|\\.dotnet|\\.codegraph|bin|obj|helper-bootstrap)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || extension is ".exe" or ".dll" or ".pdb" or ".cfg" or ".log" or ".dmp" or ".pfx" or ".p12" or ".pem" or ".key" or ".ps1" || Path.GetFileName(name) is "AGENTS.md" or ".env" or "development-maintenance.json") throw new InvalidDataException("Private/binary/PowerShell source rejected: " + name);
    }
    internal static void Closure(IEnumerable<string> rows, IEnumerable<string> expected)
    {
        var actual = rows.ToArray(); if (actual.Length != actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected)) throw new InvalidDataException("Exact source/package-extra union required.");
    }
    internal static object Run(string root, Dictionary<string, string> args)
    {
        using var boundary = new PackagingBoundary(root); var build = boundary.Relative(PackagingCommands.Required(args, "BuildDirectory")); var package = boundary.Relative(PackagingCommands.Required(args, "PackageDirectory")); var output = boundary.Stage(PackagingCommands.Required(args, "OutputDirectory"));
        PackagingBoundary.Separate(build, output); PackagingBoundary.Separate(package, output);
        boundary.Fresh(output);
        var recordBytes = boundary.Capture(build + "/build-result.json"); var record = PackagingBoundary.Parse(recordBytes); var sourceBytes = boundary.Capture(build + "/build-source.json"); var sourceHash = PackagingBoundary.Digest(sourceBytes);
        if (PackagingBoundary.Number(record, "schema") != 1 || PackagingBoundary.Text(record, "configuration") != "Release" || PackagingBoundary.Text(record, "sourceManifestSha256") != sourceHash || PackagingBoundary.Flag(record, "installationPerformed") || PackagingBoundary.Flag(record, "releaseCreated")) throw new InvalidDataException("Full source-bound Release build required.");
        var source = PackagingBoundary.Parse(sourceBytes); var sourceRows = PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new { files = source })), 1, 4096);
        var checks = record.GetProperty("checks").EnumerateArray().Select(value => value.GetString() ?? throw new InvalidDataException("Build check text required.")).ToArray();
        Checks(checks);
        foreach (var required in new[] { "developer-tools/BuildCommands.cs", "developer-tools/Program.cs", "developer-tools/DevContext.cs" }) if (!sourceRows.ContainsKey(required)) throw new InvalidDataException("Pinned C# build generator missing: " + required);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal); var rows = new Dictionary<string, PackagingInput>(StringComparer.Ordinal);
        void Add(string name, string input, string hash, long size) {
            SourceName(name); if (!sources.TryAdd(name, input)) throw new InvalidDataException("Duplicate source rejected: " + name);
            boundary.Capture(input, hash, size); rows.Add(name, new(name, size, hash));
        }
        foreach (var row in sourceRows.Values) Add(row.path, row.path, row.sha256, row.bytes);
        var manifestBytes = boundary.Capture(package + "/payload/package-manifest.json"); var manifest = PackagingBoundary.Parse(manifestBytes);
        if (PackagingBoundary.Number(manifest, "schema") != 1 || PackagingBoundary.Text(manifest, "kind") != "private-ga-whole-candidate") throw new InvalidDataException("Complete GA package required.");
        var packageRows = PackagingBoundary.Rows(manifest, 17); var captured = new Dictionary<string, PackagingCaptured>(StringComparer.Ordinal);
        foreach (var row in packageRows.Values) { var path = package + "/payload/" + row.path; captured.Add(row.path, new(path, boundary.Capture(path, row.sha256, row.bytes), row.sha256)); }
        var inventory = boundary.Inventory(package + "/payload", 1025);
        if (inventory.Length != captured.Count + 1 || !inventory.Select(path => path[(package.Length + 9)..]).ToHashSet(StringComparer.Ordinal).SetEquals(captured.Keys.Append("package-manifest.json"))) throw new InvalidDataException("Unlisted package file rejected.");
        var buildRows = PackagingBoundary.Rows(record, 3, 3); var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["plugin/BetterAstralParty.dll"] = "artifacts/BetterAstralParty.dll", ["launcher/BetterAstralParty-Launcher.exe"] = "artifacts/BetterAstralParty-Launcher.exe", ["helper/BetterAstralParty-UpdateHelper.exe"] = "helper-bootstrap/BetterAstralParty-UpdateHelper.exe" };
        if (!buildRows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(map.Keys)) throw new InvalidDataException("Exact product build closure required.");
        foreach (var row in buildRows.Values) { boundary.Capture(build + '/' + row.path, row.sha256, row.bytes); if (captured[map[row.path]].Hash != row.sha256) throw new InvalidDataException("Build/package binary mismatch."); }
        var version = PackagingBoundary.CanonicalVersion(PackagingBoundary.Text(manifest, "version")); if (PackagingImage.PluginVersion(captured["artifacts/BetterAstralParty.dll"].Bytes) != version) throw new InvalidDataException("Build/package version mismatch.");
        BuildVersion.ValidateBuild(boundary, build, record, sourceRows, version);
        var matrix = PackagingBoundary.Parse(captured["THIRD-PARTY-NOTICES/component-matrix.json"].Bytes); var loader = captured.Keys.Where(path => path.StartsWith(".deps/bepinex/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal); var license = PackagingLicense.Verify(captured, loader, PackagingBoundary.Text(matrix, "upstreamArchiveSha256"));
        var extras = new[] { "README.md", "LICENSE", "offline-nuget.config", "docs/BUILD.md", "docs/INSTALL.md", "Open-Logs.cmd", "Create-Diagnostics.cmd" }.Concat(captured.Keys.Where(path => path.StartsWith("THIRD-PARTY-NOTICES/", StringComparison.Ordinal) || path.StartsWith("THIRD-PARTY-SOURCE/", StringComparison.Ordinal))).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in extras) {
            if (sources.ContainsKey(name)) continue; var input = captured.TryGetValue(name, out var packed) ? packed.Source : name; var bytes = boundary.Capture(input);
            if (File.Exists(Path.Combine(root, name)) && PackagingBoundary.Digest(boundary.Capture(name)) != PackagingBoundary.Digest(bytes)) throw new InvalidDataException("Package/current document differs: " + name);
            Add(name, input, PackagingBoundary.Digest(bytes), bytes.LongLength);
        }
        var union = sourceRows.Keys.Concat(extras).ToHashSet(StringComparer.Ordinal);
        Closure(sources.Keys, union);
        PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new { files = rows.Values.ToArray() })), 1, 4096);
        boundary.Output(output); foreach (var entry in sources) boundary.Copy(entry.Value, "source/" + entry.Key);
        boundary.Write("source/SOURCE-MANIFEST.json", PackagingBoundary.Bytes(new { schema = 1, version, buildSourceManifestSha256 = sourceHash, files = rows.Values.OrderBy(row => row.path, StringComparer.Ordinal).ToArray() })); boundary.Write("source/BUILD-RECORD.json", recordBytes);
        var nameZip = "BetterAstralParty-" + version + "-source.zip"; var zipHash = boundary.Files.Archive(nameZip, "source", true); boundary.Write(nameZip + ".sha256", Encoding.ASCII.GetBytes(zipHash + "  " + nameZip + '\n'));
        var result = new { schema = 1, version, sourceManifestSha256 = sourceHash, packageManifestSha256 = PackagingBoundary.Digest(manifestBytes), sourceFiles = sources.Count, zip = Path.Combine(root, output, nameZip), zipSha256 = zipHash, licenseClosure = license, privateKeyAccessed = false, installed = false, published = false };
        boundary.Write("source-export.json", PackagingBoundary.Bytes(result)); boundary.Files.Recheck(); return result;
    }
}
