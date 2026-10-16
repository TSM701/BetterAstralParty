namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void UpdateGenerationFixture(DevContext context, string stage, Action<string> pin)
    {
        var legacy = Path.Combine(context.Root, "focus-tests/csharp-migration/Frozen112Discovery.template.cs"); pin(legacy);
        const string origin = "7410EE0BBE2374EE8E5F1B335B42C5C23C94A080654CC843061B87D679795E86";
        const string expected = "C0EBEC94D5B4DAB002BBF91C6639FA61DEF78FB715D732EFD61B58160D821433";
        var reference = File.ReadAllText(legacy).Replace("\r\n", "\n", StringComparison.Ordinal);
        Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(reference))) == expected, "Exact SHA-derived immutable legacy discovery slice");
        var source = UpdateGenerated(stage, "Frozen112.cs", reference);
        var assembly = BuildProject(context, "focus-tests/update-generation/UpdateGenerationFixture.csproj", stage, pin, new[] { "-p:LegacyReferencePath=" + source });
        Ensure(context.Run(Dotnet(context), new[] { assembly }, cwd: stage, env: EnvironmentFor(stage), timeoutMilliseconds: 60000, logPath: Path.Combine(stage, "generation.log")), "Historical isolation and current generation fixture");
        context.WriteJsonCreateNew(Path.Combine(stage, "update-generation-parity.json"), new { passed = true, frozenOriginSha256 = origin, frozenSliceSha256 = expected, referenceSha256 = context.Hash(source), currentProtocol = 4,
            oldTuplePositiveControlRetained = true, priorV3TupleRejected = true, networkUsed = false, operationalKeyUsed = false, legacyInstallMigrationImplemented = false });
    }
}
