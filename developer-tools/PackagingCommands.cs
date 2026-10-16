using System.Text.Json;

namespace BetterAstralParty.Development;

internal static class PackagingCommands
{
    internal static readonly string[] Names = ["package-ga", "prepare-local-install", "prepare-local-signed-inputs", "prepare-release-signed-inputs", "prepare-signing-inputs", "export-release-source", "sign-local-install", "sign-release", "packaging-self-check", "verify-staged-release", "finalize-release-assets"];
    internal static string Required(Dictionary<string, string> args, string name) => args.TryGetValue(name, out var value) && value.Length != 0 ? value : throw new ArgumentException("Required argument: " + name);
    private static Dictionary<string, string> Arguments(string[] args, params string[] permitted)
    {
        var names = permitted.ToDictionary(name => name.Replace("-", ""), StringComparer.OrdinalIgnoreCase); var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++) {
            var name = args[index].TrimStart('-').Replace("-", "");
            if (!args[index].StartsWith('-') || !names.TryGetValue(name, out var canonical) || ++index >= args.Length || !values.TryAdd(canonical, args[index])) throw new ArgumentException("Unknown, duplicate, or valueless packaging argument.");
        }
        return values;
    }
    internal static int Run(DevContext context, string command, string[] args)
    {
        object result = command switch {
            "package-ga" => PackagingWhole.Run(context.Root, Arguments(args, "InputDirectory", "InputManifestSha256", "OutputDirectory", "Scope", "ApprovalRecord", "ApprovalSha256")),
            "prepare-local-install" => PackagingPreparation.Run(context.Root, Arguments(args, "BuildDirectory", "OutputDirectory", "UpstreamArchive", "UpstreamSha256", "Scope")),
            "prepare-local-signed-inputs" => PackagingSignedInputs.Run(context.Root, Arguments(args, "StageDirectory", "InputManifestSha256", "PolicySha256", "ReviewSha256", "InputDirectory", "OutputDirectory"), true),
            "prepare-release-signed-inputs" => PackagingSignedInputs.Run(context.Root, Arguments(args, "StageDirectory", "InputManifestSha256", "PolicySha256", "ReviewSha256", "InputDirectory", "OutputDirectory"), false),
            "prepare-signing-inputs" => PackagingSigning.Prepare(context.Root, Arguments(args, "StageDirectory", "InputManifestSha256", "Scope", "Channel", "ReleaseId", "AssetId", "RemoteVerificationSha256")),
            "export-release-source" => PackagingExport.Run(context.Root, Arguments(args, "BuildDirectory", "PackageDirectory", "OutputDirectory")),
            "verify-staged-release" => PackagingRemote.Run(context.Root, Arguments(args, "StageDirectory", "LocalZipPath", "LocalZipSha256", "Version", "Channel", "ReleaseId", "AssetId", "TokenEnvironmentVariable")),
            "finalize-release-assets" => PackagingFinal.Run(context.Root, Arguments(args, "PackageDirectory", "SourceDirectory", "BuildDirectory", "OutputDirectory", "Version", "Channel", "ReleaseId", "AssetId", "CandidateStateSha256", "PackageManifestSha256", "SourceExportSha256", "BuildRecordSha256", "SourceManifestSha256")),
            "sign-local-install" => PackagingSigning.Run(context.Root, Arguments(args, "Mode", "PolicyPath", "PolicySha256", "ReviewSha256"), true),
            "sign-release" => PackagingSigning.Run(context.Root, Arguments(args, "Mode", "PolicyPath", "PolicySha256", "ReviewSha256"), false),
            "packaging-self-check" when args.Length == 0 => SelfCheck(),
            _ => throw new ArgumentException("Unknown packaging command: " + command)
        };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
    internal static object SelfCheck()
    {
        var passed = 0;
        void Reject(Action action) { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException or JsonException or KeyNotFoundException) { passed++; return; } throw new InvalidOperationException("Unsafe packaging input accepted."); }
        foreach (var selection in new[] { "", "unknown-check", "settings-scroll,settings-scroll", "settings-scroll," }) Reject(() => BuildCommands.SelectChecks(selection));
        if (!BuildCommands.SelectChecks(null).SequenceEqual(BuildCommands.Checks, StringComparer.Ordinal)
            || !BuildCommands.SelectChecks("native-steam,settings-scroll").SequenceEqual(new[] { "sdk-path-safety", "settings-scroll", "native-steam" }, StringComparer.Ordinal))
            throw new InvalidOperationException("Build check selection lost defaults, mandatory SDK guard or canonical order.");
        passed++;
        foreach (var path in new[] { "../source", "artifacts//file", "C:/target", "artifacts/NUL.txt", "artifacts/file.", "artifacts/ file", "artifacts\\file", "artifacts/a:b", "artifacts/a\0b" }) Reject(() => PackagingBoundary.PathName(path));
        foreach (var path in new[] { "artifacts/source/File.cs", ".deps/bepinex/dotnet/.version", "THIRD-PARTY-SOURCE/upstream.zip" }) { PackagingBoundary.PathName(path); passed++; }
        Reject(() => PackagingBoundary.Parse(System.Text.Encoding.UTF8.GetBytes("{\"files\":[],\"Files\":[]}")));
        Reject(() => PackagingBoundary.Parse(System.Text.Encoding.UTF8.GetBytes("{\"nested\":{\"path\":1,\"path\":2}}")));
        Reject(() => Arguments(["--InputDirectory", "a", "--input-directory", "b"], "InputDirectory"));
        Reject(() => Arguments(["--unknown", "a"], "InputDirectory"));
        Reject(() => Arguments(["--InputDirectory"], "InputDirectory"));
        var values = Arguments(["--input-directory", "a"], "InputDirectory"); if (values["InputDirectory"] != "a") throw new InvalidOperationException("Canonical argument mapping failed."); passed++;
        Reject(() => PackagingBoundary.Separate("artifacts/a", "ARTIFACTS/a/sub")); Reject(() => PackagingBoundary.Separate("artifacts/a/sub", "artifacts/a"));
        foreach (var hash in new[] { "", new string('a', 64), new string('A', 63) }) Reject(() => PackagingBoundary.Hash(hash));
        PackagingBoundary.Hash(new string('A', 64)); passed++;
        var rows = new[] { new PackagingInput("a", 1, new string('A', 64)), new PackagingInput("b", 2, new string('B', 64)) };
        if (PackagingWhole.Identity(rows, "kind", "1.3.0") != PackagingWhole.Identity(rows.Reverse(), "kind", "1.3.0")) throw new InvalidOperationException("Noncanonical bundle identity ordering."); passed++;
        Reject(() => PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new { files = new[] { new PackagingInput("A", 1, new string('A', 64)), new PackagingInput("a", 1, new string('A', 64)) } }))));
        return new { passed, operationalPrivateKeyAccessed = false, outputsCreated = false, powerShellExecuted = false };
    }
}
