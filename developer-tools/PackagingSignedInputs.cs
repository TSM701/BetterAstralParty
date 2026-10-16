using BetterAstralParty.GaSigning;
using BetterAstralParty.Packaging;

namespace BetterAstralParty.Development;

internal static class PackagingSignedInputs
{
    internal static object Run(string root, Dictionary<string, string> args, bool local)
    {
        using var boundary = new PackagingBoundary(root); var stage = boundary.Stage(PackagingCommands.Required(args, "StageDirectory"));
        var input = args.TryGetValue("InputDirectory", out var inputArgument) ? boundary.Stage(inputArgument) : stage + "/inputs";
        var output = args.TryGetValue("OutputDirectory", out var outputArgument) ? boundary.Stage(outputArgument) : stage + "/signed-inputs";
        foreach (var path in new[] { input, stage + "/signatures", stage + "/signing-inputs" }) PackagingBoundary.Separate(path, output);
        if (stage.Equals(output, StringComparison.OrdinalIgnoreCase) || stage.StartsWith(output + '/', StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Separate fresh signed-input output required.");
        var manifestHash = PackagingCommands.Required(args, "InputManifestSha256"); var policyHash = PackagingCommands.Required(args, "PolicySha256"); var reviewHash = PackagingCommands.Required(args, "ReviewSha256");
        var spec = boundary.Json(input + "/input-manifest.json", manifestHash); var policy = boundary.Json(stage + "/signing-policy.json", policyHash); var review = boundary.Json(stage + "/signing-review.json", reviewHash);
        var purpose = local ? "sign-local-install-once" : "sign-release-once"; var reviewPurpose = local ? "approve-local-install-signing" : "approve-release-signing";
        var version = PackagingBoundary.Text(spec, "version");
        if (PackagingBoundary.Text(policy, "purpose") != purpose || PackagingBoundary.Text(policy, "version") != version || PackagingBoundary.Text(spec, "productSourceManifestSha256") != PackagingBoundary.Text(policy, "sourceManifestSha256") || PackagingBoundary.Text(policy, "output") != stage + "/signatures" || PackagingBoundary.Text(review, "policySha256") != policyHash || PackagingBoundary.Text(review, "purpose") != reviewPurpose || PackagingBoundary.Text(review, "status") != "approved") throw new InvalidDataException("Exact signing review required.");
        if (local) SignLocalInstall.Validate(root, stage + "/signing-policy.json", policyHash, reviewHash); else SignRelease.Validate(root, stage + "/signing-policy.json", policyHash, reviewHash);
        var evidence = boundary.Json(stage + "/signatures/signing-evidence.json");
        if (!PackagingBoundary.Flag(evidence, "signingComplete") || PackagingBoundary.Number(evidence, "nativeSignCalls") != 2 || PackagingBoundary.Text(evidence, "policySha256") != policyHash || PackagingBoundary.Text(evidence, "reviewSha256") != reviewHash || PackagingBoundary.Text(evidence, "helperDescriptorSha256") != PackagingBoundary.Text(policy, "helperDescriptorSha256") || PackagingBoundary.Text(evidence, "updateDescriptorSha256") != PackagingBoundary.Text(policy, "updateDescriptorSha256")) throw new InvalidDataException("Completed bounded signing evidence required.");
        var captured = PackagingWhole.Inputs(boundary, input, spec);
        var signed = new[] {
            (Path: "helper-bootstrap/helper.signature", Source: stage + "/signatures/helper.signature", Hash: PackagingBoundary.Text(evidence, "helperSignatureSha256")),
            (Path: "update-proof/update.descriptor", Source: stage + "/signing-inputs/update.descriptor.unsigned", Hash: PackagingBoundary.Text(policy, "updateDescriptorSha256")),
            (Path: "update-proof/update.signature", Source: stage + "/signatures/update.signature", Hash: PackagingBoundary.Text(evidence, "updateSignatureSha256"))
        };
        foreach (var row in signed) {
            if (captured.ContainsKey(row.Path)) throw new InvalidDataException("Signature/proof already included.");
            captured.Add(row.Path, new PackagingCaptured(row.Source, boundary.Capture(row.Source, row.Hash), row.Hash));
        }
        var helperImage = captured["helper-bootstrap/BetterAstralParty-UpdateHelper.exe"].Bytes;
        if (PackagingBoundary.Digest(helperImage) != PackagingBoundary.Text(policy, "helperImageSha256")) throw new InvalidDataException("Helper image changed.");
        var helper = GaPackageContract.PrepareHelper(version, helperImage);
        if (PackagingBoundary.Digest(helper) != PackagingBoundary.Text(policy, "helperDescriptorSha256")) throw new InvalidDataException("Helper descriptor changed.");
        GaPackageContract.VerifyHelper(version, helper, captured[signed[0].Path].Bytes, helperImage);
        var zip = boundary.Capture(stage + "/signing-inputs/payload.zip", PackagingBoundary.Text(policy, "zipSha256"));
        if (PackagingBoundary.Digest(PackagingWhole.Zip(captured)) != PackagingBoundary.Digest(zip)) throw new InvalidDataException("Actual signed update ZIP differs from prepared files.");
        var proofHash = GaPackageContract.VerifyWholeUpdate(version, captured[signed[1].Path].Bytes, captured[signed[2].Path].Bytes, zip);
        var upstream = input + "/upstream-loader.zip"; boundary.Capture(upstream, PackagingBoundary.Text(spec, "upstreamArchiveSha256"));
        var manifest = PackagingBoundary.Bytes(new { schema = 1, version, upstreamArchiveSha256 = PackagingBoundary.Text(spec, "upstreamArchiveSha256"), baseInputManifestSha256 = manifestHash, productSourceManifestSha256 = PackagingBoundary.Text(spec, "productSourceManifestSha256"), signedInputReviewSha256 = reviewHash, files = captured.Select(entry => new PackagingInput(entry.Key, entry.Value.Bytes.LongLength, entry.Value.Hash)).OrderBy(row => row.path, StringComparer.Ordinal).ToArray() });
        boundary.Output(output); foreach (var entry in captured) boundary.Copy(entry.Value.Source, "files/" + entry.Key);
        boundary.Copy(upstream, "upstream-loader.zip"); boundary.Write("input-manifest.json", manifest); boundary.Files.Recheck();
        return new { inputDirectory = Path.Combine(root, output), inputManifestSha256 = PackagingBoundary.Digest(manifest), files = captured.Count, helperVerified = true, updateDescriptorSha256 = proofHash, signingCalls = 0, catalogApproved = false };
    }
}
