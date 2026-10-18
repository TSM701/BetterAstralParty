using System.Text;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void UpdatePrimitives(DevContext context, string stage, Action<string> pin)
    {
        var net8 = Path.Combine(stage, "net8"); Directory.CreateDirectory(net8);
        var assembly = BuildProject(context, "updater-tests/BetterAstralParty.UpdateTrustTests.csproj", net8, pin);
        Ensure(context.Run(Dotnet(context), new[] { assembly }, cwd: net8, env: EnvironmentFor(net8), timeoutMilliseconds: 120000,
            logPath: Path.Combine(stage, "net8-primitives.log")), "Net8 RSA-PSS/ZIP/SemVer/fake HTTP fixture");
        var framework = Path.Combine(stage, "framework"); Directory.CreateDirectory(framework);
        var image = UpdateFrameworkCompile(context, framework, "UpdateTrustFixture.exe", UpdateFixtureSources(context, false, true).Append(Path.Combine(context.Root, "updater-tests/Program.cs")), pin, "TrustFixture", "NETFRAMEWORK");
        Ensure(context.Run(image, Array.Empty<string>(), cwd: framework, timeoutMilliseconds: 120000, logPath: Path.Combine(stage, "framework-primitives.log")), "Actual Framework RSA-PSS/ZIP/SemVer fixture");
        context.WriteJsonCreateNew(Path.Combine(stage, "updater-primitives-parity.json"), new { passed = true, net8AndFrameworkExecuted = true, malformedProtocol123Rejected = true, privateKeysPersisted = false, operationalKeyUsed = false, actualNetworkUsed = false });
    }

    private static void UpdateSafetyFixture(DevContext context, string stage, string name, Dictionary<string, string> options, Action<string> pin)
    {
        var inputs = Path.Combine(stage, "inputs"); Directory.CreateDirectory(inputs);
        var inert = UpdateFrameworkCompile(context, inputs, "inert-process.exe", new[] { Path.Combine(context.Root, "updater-safety-tests/InertProcess.cs") }, pin, "InertProcess", "NETFRAMEWORK");
        var metadata = Path.Combine(context.Root, "focus-tests/handoff-start-failure/Metadata.cs");
        var old = UpdateFrameworkCompile(context, inputs, "old.dll", new[] { metadata }, pin, defines: "NETFRAMEWORK,BAP_METADATA_OLD");
        var target = UpdateFrameworkCompile(context, inputs, "target.dll", new[] { metadata }, pin, defines: "NETFRAMEWORK");
        var project = name switch {
            "updater-transaction" => "updater-safety-tests/BetterAstralParty.TransactionTests.csproj",
            "updater-apply-boundary" => "updater-safety-tests/BetterAstralParty.ApplyBoundaryTests.csproj",
            "staging" => "updater-safety-tests/BetterAstralParty.StagingTests.csproj",
            _ => throw new ArgumentException("Unknown inert updater fixture: " + name)
        };
        var host = Path.Combine(stage, "host"); Directory.CreateDirectory(host);
        var assembly = BuildProject(context, project, host, pin);
        var boundary = Path.Combine(stage, "inert-boundary"); Directory.CreateDirectory(boundary);
        UpdateGenerated(boundary, ".bap-v4-updater-fixture-root", "BetterAstralParty.UpdaterFixtureRoot/v1\n");
        var root = Path.Combine(boundary, "fixtures");
        var environment = new Dictionary<string, string?>(EnvironmentFor(stage)) {
            ["BAP_UPDATER_TEST_ROOT"] = boundary, ["LOCALAPPDATA"] = Path.Combine(stage, "inert-profile"), ["APPDATA"] = Path.Combine(stage, "inert-roaming")
        };
        var arguments = new List<string> { assembly, root };
        if (name == "updater-apply-boundary") arguments.Add(inert);
        else if (name == "updater-transaction") {
            if (!options.TryGetValue("plugin-path", out var probe)) throw new ArgumentException("Exact compiled --plugin-path is required for the metadata probe.");
            pin(Path.GetFullPath(probe)); arguments.AddRange(new[] { old, target, inert, Path.GetFullPath(probe) });
        }
        Ensure(context.Run(Dotnet(context), arguments, cwd: context.Root, env: environment, timeoutMilliseconds: 300000, logPath: Path.Combine(stage, "fixture-run.log")), "Full inert updater fixture: " + name);
        context.WriteJsonCreateNew(Path.Combine(stage, "updater-safety-parity.json"), new { passed = true, name, root, protocol = 5, ownedNativeFiles = 4, networkUsed = false, operationalKeyUsed = false, liveGameSteamOrUserInstallationUsed = false });
    }
}
