using System.Reflection;
using BetterAstralParty.GaSigning;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static class PackagingSigning
{
    internal static string[] CompiledPaths(string root)
    {
        var paths = Directory.EnumerateFiles(Path.Combine(root, "developer-tools"), "*.cs", SearchOption.TopDirectoryOnly).Select(path => "developer-tools/" + Path.GetFileName(path))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "updater"), "*.cs", SearchOption.TopDirectoryOnly).Select(path => "updater/" + Path.GetFileName(path)))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src/Observability"), "*.cs", SearchOption.TopDirectoryOnly).Select(path => "src/Observability/" + Path.GetFileName(path)))
            .Concat(new[] { "tools/DevelopmentPackageFiles.cs", "tools/GaPackageContract.cs", "tools/SignLocalInstall.cs", "tools/SignRelease.cs", "diagnostics/MinimalDiagnosticProjection.cs" });
        return paths.Order(StringComparer.Ordinal).ToArray();
    }
    internal static string[] ToolPaths(string root)
    {
        var image = Assembly.GetExecutingAssembly().Location; var relative = Path.GetRelativePath(root, image).Replace('\\', '/'); PackagingBoundary.PathName(relative);
        if (!relative.StartsWith("artifacts/", StringComparison.Ordinal)) throw new InvalidDataException("Signing requires the exact project-local precompiled developer host.");
        var stem = relative.EndsWith(".dll", StringComparison.Ordinal) ? relative[..^4] : throw new InvalidDataException("Managed precompiled host required.");
        var paths = new List<string> { relative, stem + ".deps.json", stem + ".runtimeconfig.json", "developer-tools/BetterAstralParty.Developer.csproj" };
        if (File.Exists(Path.Combine(root, stem + ".exe"))) paths.Add(stem + ".exe");
        var cecil = Path.GetRelativePath(root, typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location).Replace('\\', '/'); PackagingBoundary.PathName(cecil);
        if (!cecil.StartsWith(relative[..(relative.LastIndexOf('/') + 1)], StringComparison.Ordinal) || PackagingBoundary.Digest(File.ReadAllBytes(Path.Combine(root, cecil))) != "769A59793D4B8885BBBFBC5AEE8F57A0D4E34D275C56C60C03994309B87F67E9") throw new InvalidDataException("Exact pinned metadata dependency required beside the precompiled host.");
        paths.Add(cecil);
        return paths.Order(StringComparer.Ordinal).ToArray();
    }
    internal static object Run(string root, Dictionary<string, string> args, bool local)
    {
        var mode = args.GetValueOrDefault("Mode", "Validate");
        if (mode == "SelfCheck") return PackagingBoundary.Parse(System.Text.Encoding.UTF8.GetBytes(local ? SignLocalInstall.SelfCheck() : SignRelease.SelfCheck()));
        var policy = PackagingCommands.Required(args, "PolicyPath"); PackagingBoundary.PathName(policy); var policyHash = PackagingCommands.Required(args, "PolicySha256"); PackagingBoundary.Hash(policyHash); args.TryGetValue("ReviewSha256", out var reviewHash);
        var result = mode switch {
            "Validate" => local ? SignLocalInstall.Validate(root, policy, policyHash, reviewHash) : SignRelease.Validate(root, policy, policyHash, reviewHash),
            "Execute" when reviewHash != null => local ? SignLocalInstall.Execute(root, policy, policyHash, reviewHash) : SignRelease.Execute(root, policy, policyHash, reviewHash),
            _ => throw new ArgumentException("Signing supports SelfCheck, Validate, or explicitly reviewed Execute.")
        };
        return PackagingBoundary.Parse(System.Text.Encoding.UTF8.GetBytes(result));
    }
    internal static object Prepare(string root, Dictionary<string, string> args)
    {
        using var boundary = new PackagingBoundary(root); var stage = boundary.Stage(PackagingCommands.Required(args, "StageDirectory")); var input = stage + "/inputs";
        var inputHash = PackagingCommands.Required(args, "InputManifestSha256"); var spec = boundary.Json(input + "/input-manifest.json", inputHash); var captured = PackagingWhole.Inputs(boundary, input, spec);
        var version = PackagingBoundary.CanonicalVersion(PackagingBoundary.Text(spec, "version")); var channel = args.GetValueOrDefault("Channel", "Beta"); var local = args.GetValueOrDefault("Scope", "LocalInstall") == "LocalInstall";
        if (args.GetValueOrDefault("Scope", "LocalInstall") is not ("Release" or "LocalInstall")) throw new ArgumentException("Unknown signing scope.");
        var parsed = UpdateVersion.Parse(version);
        if (!ReleaseFeedPolicy.IsChannel(channel) || parsed.CompareTo(UpdateVersion.Parse(ReleaseFeedPolicy.Baseline + "-0")) < 0 || local != parsed.IsLocalPrerelease) throw new InvalidDataException("Exact new-baseline local/remote version required.");
        var zip = PackagingWhole.Zip(captured); var owned = GaPackageContract.OwnedPaths(); var sizes = owned.Select(path => captured[PackagingWhole.OwnedSource(path)].Bytes.LongLength).ToArray(); var hashes = owned.Select(path => captured[PackagingWhole.OwnedSource(path)].Hash).ToArray();
        var releaseId = local ? 0 : long.Parse(PackagingCommands.Required(args, "ReleaseId"), System.Globalization.CultureInfo.InvariantCulture); var assetId = local ? 0 : long.Parse(PackagingCommands.Required(args, "AssetId"), System.Globalization.CultureInfo.InvariantCulture);
        if (local && (args.ContainsKey("ReleaseId") || args.ContainsKey("AssetId"))) throw new InvalidDataException("Local proofs cannot have remote identity.");
        var descriptor = local ? GaPackageContract.PrepareLocalInstallDescriptor(version, channel, zip, sizes, hashes) : GaPackageContract.PrepareUpdateDescriptor(version, channel, releaseId, assetId, zip, sizes, hashes);
        var sourceHash = PackagingBoundary.Text(spec, "productSourceManifestSha256"); boundary.Capture(input + "/build-source.json", sourceHash);
        var compiled = CompiledPaths(root).Select(path => { var bytes = boundary.Capture(path); return new { path, sha256 = PackagingBoundary.Digest(bytes) }; }).ToArray();
        var tools = ToolPaths(root).Select(path => { var bytes = boundary.Capture(path); return new { path, sha256 = PackagingBoundary.Digest(bytes) }; }).ToArray();
        var output = stage + "/signatures"; var reservation = "artifacts/automatic-update-development/signing-" + version + ".reserved.json";
        var policy = new Dictionary<string, object?> {
            ["schema"] = 1, ["purpose"] = local ? "sign-local-install-once" : "sign-release-once", ["version"] = version, ["channel"] = channel,
            ["keyName"] = "BetterAstralParty.PrivateTrial.20261002", ["keyId"] = "bap-trial-20261002", ["maximumNativeSignCalls"] = 1, ["rootIdentity"] = boundary.Files.RootIdentity,
            ["output"] = output, ["reservation"] = reservation, ["sourceManifestSha256"] = sourceHash, ["updateDescriptorSha256"] = PackagingBoundary.Digest(descriptor), ["zipSha256"] = PackagingBoundary.Digest(zip), ["compiledFiles"] = compiled, ["toolFiles"] = tools
        };
        if (!local) {
            var remoteHash = PackagingCommands.Required(args, "RemoteVerificationSha256"); boundary.Capture(stage + "/staged-asset-verification.json", remoteHash); var feed = ReleaseFeedPolicy.For(channel);
            policy["repository"] = feed.Repository; policy["repositoryId"] = feed.RepositoryId; policy["releaseId"] = releaseId; policy["assetId"] = assetId; policy["remoteVerificationSha256"] = remoteHash;
        }
        // Preparation never reserves signing, creates a key, or manufactures an approval.
        var policyBytes = PackagingBoundary.Bytes(policy); var policyHash = PackagingBoundary.Digest(policyBytes);
        if (File.Exists(Path.Combine(root, stage, "signing-policy.json")) || Directory.Exists(Path.Combine(root, stage, "signing-policy.json"))) throw new IOException("Existing signing policy retained; fresh stage required.");
        boundary.Output(stage + "/signing-inputs"); boundary.Write("payload.zip", zip); boundary.Write("update.descriptor.unsigned", descriptor); boundary.Files.Recheck();
        using var fence = new WindowsFileFence(root); using var policyFile = fence.OpenFile(stage + "/signing-policy.json", true, true); policyFile.Stream.Write(policyBytes); policyFile.Flush(); policyFile.Recheck();
        if (policyFile.Hash() != policyHash) throw new InvalidDataException("Policy write mismatch.");
        return new { version, scope = local ? "LocalInstall" : "Release", policyPath = stage + "/signing-policy.json", policySha256 = policyHash, keyOpened = false, nativeSignCalls = 0, reviewRequired = true, providerConfirmationPreserved = true };
    }
}
