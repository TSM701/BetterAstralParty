using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static class PackagingWhole
{
    internal static readonly string[] Required = ["Install.cmd", "Uninstall.cmd", "Force-Uninstall.cmd", "Open-Logs.cmd", "Create-Diagnostics.cmd", "README.md", "LICENSE", "artifacts/BetterAstralParty.dll", "artifacts/BetterAstralParty-Launcher.exe", "artifacts/BetterAstralParty.compatibility.json", "helper-bootstrap/BetterAstralParty-UpdateHelper.exe", "tools/approved-install-bundles.json", "tools/BetterAstralParty-Mod.cmd", "tools/AstralParty-Vanilla.cmd", ".deps/bepinex/BepInEx/core/Mono.Cecil.dll", ".deps/bepinex/doorstop_config.ini", ".deps/bepinex/winhttp.dll"];
    internal static string OwnedSource(string path) => path switch {
        UpdateTrust.PluginPath => "artifacts/BetterAstralParty.dll",
        "BetterAstralParty-Launcher.exe" => "artifacts/BetterAstralParty-Launcher.exe",
        "BetterAstralParty.compatibility.json" => "artifacts/BetterAstralParty.compatibility.json",
        _ => "tools/" + path
    };
    internal static string Identity(IEnumerable<PackagingInput> rows, string kind, string version)
    {
        var text = new StringBuilder("BetterAstralParty.InstallBundle/v1\nversion=").Append(version).Append("\nkind=").Append(kind).Append("\nprotocol=1\n");
        foreach (var row in rows.Where(row => row.path != "tools/approved-install-bundles.json").OrderBy(row => row.path, StringComparer.Ordinal)) text.Append(row.path).Append('=').Append(row.sha256).Append('\n');
        return PackagingBoundary.Digest(Encoding.UTF8.GetBytes(text.ToString()));
    }
    internal static byte[] Zip(Dictionary<string, PackagingCaptured> captured) => GaPackageContract.PreviewUpdateZip(GaPackageContract.OwnedPaths().Select(path => captured[OwnedSource(path)].Bytes).ToArray());
    internal static Dictionary<string, PackagingCaptured> Inputs(PackagingBoundary boundary, string input, JsonElement spec)
    {
        if (PackagingBoundary.Number(spec, "schema") != 1) throw new InvalidDataException("Invalid input inventory schema.");
        var rows = PackagingBoundary.Rows(spec, 17); var captured = new Dictionary<string, PackagingCaptured>(StringComparer.Ordinal);
        foreach (var row in rows.Values) {
            var extension = Path.GetExtension(row.path).ToLowerInvariant();
            if (extension is ".ps1" or ".cs" or ".pdb" or ".log" or ".bak" or ".dmp" or ".ttf" or ".otf" or ".cfg" || Path.GetFileName(row.path) == "AGENTS.md" || Path.GetFileName(row.path).StartsWith("test-", StringComparison.OrdinalIgnoreCase) || row.path.StartsWith("assets/steam/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Developer/private/PowerShell input rejected: " + row.path);
            var source = input + "/files/" + row.path; captured.Add(row.path, new PackagingCaptured(source, boundary.Capture(source, row.sha256, row.bytes), row.sha256));
        }
        var inventory = boundary.Inventory(input + "/files", 1024);
        if (inventory.Length != captured.Count || !inventory.Select(path => path[(input.Length + 7)..]).ToHashSet(StringComparer.Ordinal).SetEquals(captured.Keys)) throw new InvalidDataException("Unlisted input files rejected.");
        foreach (var path in Required) if (!captured.ContainsKey(path)) throw new InvalidDataException("Required native package input missing: " + path);
        foreach (var path in GaPackageContract.OwnedPaths().Select(OwnedSource)) if (!captured.ContainsKey(path)) throw new InvalidDataException("Required update-owned input missing: " + path);
        if (captured[".deps/bepinex/BepInEx/core/Mono.Cecil.dll"].Hash != "769A59793D4B8885BBBFBC5AEE8F57A0D4E34D275C56C60C03994309B87F67E9") throw new InvalidDataException("Metadata reader pin changed.");
        var catalog = PackagingBoundary.Parse(captured["tools/approved-install-bundles.json"].Bytes);
        if (PackagingBoundary.Number(catalog, "schema") != 1 || catalog.GetProperty("bundles").GetArrayLength() != 0) throw new InvalidDataException("Input catalog must remain empty; exact external approval required.");
        if (captured.ContainsKey("package-manifest.json") || captured.ContainsKey("helper-bootstrap/helper.descriptor") || captured.Keys.Any(path => path.StartsWith("update-proof/prior/", StringComparison.Ordinal))) throw new InvalidDataException("Derived/historical compatibility inputs forbidden in the fresh generation.");
        return captured;
    }
    internal static HashSet<string> Loader(PackagingBoundary boundary, string input, JsonElement spec, Dictionary<string, PackagingCaptured> captured)
    {
        var hash = PackagingBoundary.Text(spec, "upstreamArchiveSha256"); var upstream = boundary.Capture(input + "/upstream-loader.zip", hash);
        using var memory = new MemoryStream(upstream, false); using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries) {
            var name = entry.FullName.Replace('\\', '/'); if (!Regex.IsMatch(name, "\\A(winhttp\\.dll|doorstop_config\\.ini|\\.doorstop_version|changelog\\.txt|BepInEx/core/[^/]+\\.(dll|config)|dotnet/[^/]+\\.(dll|json)|dotnet/\\.version)\\z")) continue;
            PackagingBoundary.PathName(name); var key = ".deps/bepinex/" + name;
            if (!seen.Add(key) || !captured.TryGetValue(key, out var inputRow) || entry.Length <= 0 || entry.Length > 64 * 1024 * 1024 || (total += entry.Length) > 128 * 1024 * 1024) throw new InvalidDataException("Invalid clean loader closure.");
            using var stream = entry.Open(); var bytes = ReadEntry(stream, entry.Length);
            if (PackagingBoundary.Digest(bytes) != inputRow.Hash) throw new InvalidDataException("Clean loader byte mismatch: " + name);
        }
        if (captured.Keys.Count(path => path.StartsWith(".deps/bepinex/", StringComparison.Ordinal)) != seen.Count) throw new InvalidDataException("Unlisted loader input rejected.");
        var compatibility = PackagingBoundary.Parse(captured["artifacts/BetterAstralParty.compatibility.json"].Bytes);
        foreach (var row in compatibility.GetProperty("files").EnumerateArray()) {
            var name = PackagingBoundary.Text(row, "path");
            if (PackagingBoundary.Text(row, "root") == "game" && (name == "winhttp.dll" || Regex.IsMatch(name, "\\ABepInEx/core/[^/]+\\.dll\\z"))) {
                if (!captured.TryGetValue(".deps/bepinex/" + name, out var file) || file.Hash != PackagingBoundary.Text(row, "sha256").ToUpperInvariant()) throw new InvalidDataException("Compatibility/loader mismatch.");
            }
        }
        return new HashSet<string>(seen, StringComparer.Ordinal);
    }
    internal static byte[] ReadEntry(Stream stream, long size)
    {
        if (size <= 0 || size > 64 * 1024 * 1024) throw new InvalidDataException("Archive entry bound.");
        var bytes = new byte[checked((int)size)]; var position = 0;
        while (position < bytes.Length) { var count = stream.Read(bytes, position, bytes.Length - position); if (count == 0) throw new InvalidDataException("Incomplete archive entry."); position += count; }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Archive entry length changed."); return bytes;
    }
    internal static object Run(string root, Dictionary<string, string> args)
    {
        using var boundary = new PackagingBoundary(root); var input = boundary.Stage(PackagingCommands.Required(args, "InputDirectory")); var output = boundary.Stage(PackagingCommands.Required(args, "OutputDirectory")); PackagingBoundary.Separate(input, output);
        var manifestHash = PackagingCommands.Required(args, "InputManifestSha256"); var spec = boundary.Json(input + "/input-manifest.json", manifestHash);
        var version = PackagingBoundary.CanonicalVersion(PackagingBoundary.Text(spec, "version"));
        if (UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse("1.3.0-0")) < 0) throw new InvalidDataException("Fresh 1.3.0 baseline required.");
        var scope = args.GetValueOrDefault("Scope", "Release"); if (scope is not ("Release" or "LocalInstall")) throw new ArgumentException("Unknown package scope.");
        var kind = scope == "LocalInstall" ? "local-development-whole-candidate" : "private-ga-whole-candidate";
        var captured = Inputs(boundary, input, spec); var loader = Loader(boundary, input, spec, captured); var license = PackagingLicense.Verify(captured, loader, PackagingBoundary.Text(spec, "upstreamArchiveSha256"));
        var metadata = new[] { PackagingImage.Inspect(captured["artifacts/BetterAstralParty.dll"].Bytes, "Plugin", version), PackagingImage.Inspect(captured["artifacts/BetterAstralParty-Launcher.exe"].Bytes, "Launcher", version), PackagingImage.Inspect(captured["helper-bootstrap/BetterAstralParty-UpdateHelper.exe"].Bytes, "Helper", version) };
        var helperImage = captured["helper-bootstrap/BetterAstralParty-UpdateHelper.exe"].Bytes; var helperDescriptor = GaPackageContract.PrepareHelper(version, helperImage);
        var rows = captured.Select(entry => new PackagingInput(entry.Key, entry.Value.Bytes.LongLength, entry.Value.Hash)).ToDictionary(row => row.path, StringComparer.Ordinal);
        rows.Add("helper-bootstrap/helper.descriptor", new("helper-bootstrap/helper.descriptor", helperDescriptor.Length, PackagingBoundary.Digest(helperDescriptor)));
        var helperSigned = captured.TryGetValue("helper-bootstrap/helper.signature", out var helperSignature);
        if (helperSigned) GaPackageContract.VerifyHelper(version, helperDescriptor, helperSignature!.Bytes, helperImage);
        var zip = Zip(captured); var previewHash = PackagingBoundary.Digest(zip);
        var hasDescriptor = captured.TryGetValue("update-proof/update.descriptor", out var descriptor); var hasSignature = captured.TryGetValue("update-proof/update.signature", out var signature);
        if (hasDescriptor != hasSignature) throw new InvalidDataException("Both update descriptor/signature required.");
        string? proofHash = null;
        if (hasDescriptor) {
            var local = Encoding.ASCII.GetString(descriptor!.Bytes).StartsWith("BetterAstralParty.LocalInstallDescriptor/v1\n", StringComparison.Ordinal);
            if (local != (scope == "LocalInstall")) throw new InvalidDataException("Local and remote proofs must remain separate.");
            proofHash = GaPackageContract.VerifyWholeUpdate(version, descriptor.Bytes, signature!.Bytes, zip);
        }
        var build = Identity(rows.Values, kind, version);
        var review = PackagingBoundary.Bytes(new { schema = 1, version, kind, protocol = 1, buildIdentity = build, files = rows.Values.Where(row => row.path != "tools/approved-install-bundles.json").OrderBy(row => row.path, StringComparer.Ordinal).ToArray() }); var reviewHash = PackagingBoundary.Digest(review);
        var approved = false;
        if (args.ContainsKey("ApprovalRecord") || args.ContainsKey("ApprovalSha256")) {
            if (!helperSigned || proofHash == null) throw new InvalidDataException("Signed helper/update proof required before approval.");
            var approval = boundary.Json(boundary.Stage(PackagingCommands.Required(args, "ApprovalRecord")), PackagingCommands.Required(args, "ApprovalSha256"));
            if (PackagingBoundary.Number(approval, "schema") != 1 || PackagingBoundary.Text(approval, "purpose") != "approve-ga-whole-bundle" || PackagingBoundary.Text(approval, "status") != "approved" || PackagingBoundary.Text(approval, "version") != version || PackagingBoundary.Text(approval, "kind") != kind || PackagingBoundary.Number(approval, "protocol") != 1 || PackagingBoundary.Text(approval, "buildIdentity") != build || PackagingBoundary.Text(approval, "reviewInventorySha256") != reviewHash || PackagingBoundary.Text(approval, "reviewReference").Length == 0) throw new InvalidDataException("External approval binding mismatch.");
            approved = true;
        }
        var catalog = PackagingBoundary.Bytes(new { schema = 1, bundles = approved ? new[] { new { buildIdentity = build, version, kind, protocol = 1 } } : [] });
        rows["tools/approved-install-bundles.json"] = new("tools/approved-install-bundles.json", catalog.Length, PackagingBoundary.Digest(catalog));
        if (Identity(rows.Values, kind, version) != build) throw new InvalidDataException("Catalog changed content identity.");
        boundary.Output(output);
        foreach (var entry in captured) if (entry.Key != "tools/approved-install-bundles.json") boundary.Copy(entry.Value.Source, "payload/" + entry.Key);
        boundary.Write("payload/tools/approved-install-bundles.json", catalog); boundary.Write("payload/helper-bootstrap/helper.descriptor", helperDescriptor);
        var packageManifest = PackagingBoundary.Bytes(new { schema = 1, version, kind, buildIdentity = build, files = rows.Values.OrderBy(row => row.path, StringComparer.Ordinal).ToArray() }); boundary.Write("payload/package-manifest.json", packageManifest);
        foreach (var owned in GaPackageContract.OwnedPaths()) boundary.Copy(captured[OwnedSource(owned)].Source, "update-payload/" + owned);
        var wholeName = "BetterAstralParty-" + version + (approved ? "" : "-unsigned-candidate") + ".zip"; var updateName = "BetterAstralParty-" + version + "-update.zip";
        var wholeHash = boundary.Files.Archive(wholeName, "payload", true); var updateHash = boundary.Files.Archive(updateName, "update-payload", true);
        if (updateHash != previewHash) throw new InvalidDataException("Deterministic ZIP differs from verified preview.");
        foreach (var pair in new[] { (wholeName, wholeHash), (updateName, updateHash) }) boundary.Write(pair.Item1 + ".sha256", Encoding.ASCII.GetBytes(pair.Item2 + "  " + pair.Item1 + '\n'));
        boundary.Write("review-inventory.json", review);
        boundary.Write("catalog-proposal.json", PackagingBoundary.Bytes(new { schema = 1, purpose = "approve-ga-whole-bundle", status = "pending", version, kind, protocol = 1, buildIdentity = build, reviewInventorySha256 = reviewHash, candidateManifestSha256 = PackagingBoundary.Digest(packageManifest), helperSigned, exactApprovalReady = helperSigned && proofHash != null, updateProofVerified = proofHash != null, reviewReference = (string?)null }));
        var state = new { schema = 1, version, kind, sourceInputManifestSha256 = manifestHash, buildIdentity = build, catalogRegistered = approved, licenseClosure = license, installableRelease = approved && helperSigned && proofHash != null, updateProofVerified = proofHash != null, updateProofDescriptorSha256 = proofHash, helperSignatureVerified = helperSigned, helperDescriptorSha256 = PackagingBoundary.Digest(helperDescriptor), helperImageSha256 = captured["helper-bootstrap/BetterAstralParty-UpdateHelper.exe"].Hash, publicKeyIds = GaPackageContract.PublicKeyIds(), operationalTrustConfigured = GaPackageContract.PublicKeyIds().Length != 0, runtimeAuthenticationConnected = (bool?)null, runtimeHelperInstalled = false, privateCredentialInputCompiled = JsonSerializer.SerializeToElement(metadata[0]).GetProperty("credentialInputCompiled").GetBoolean(), credentialsAccessed = false, operationalPrivateKeyAccessed = false, updateDescriptorReady = proofHash != null, updateReleaseAndAssetIds = (object?)null, updateInventory = GaPackageContract.OwnedPaths().Select(path => { var file = captured[OwnedSource(path)]; return new PackagingInput(path, file.Bytes.LongLength, file.Hash); }).ToArray(), wholeZip = wholeName, wholeZipSha256 = wholeHash, updateZipSha256 = updateHash, cleanLoaderSourceSha256 = PackagingBoundary.Text(spec, "upstreamArchiveSha256"), inputPinned = true, outputRootIdentity = boundary.OutputIdentity, metadata };
        boundary.Write("candidate-state.json", PackagingBoundary.Bytes(state)); boundary.Files.Recheck(); return state;
    }
}
