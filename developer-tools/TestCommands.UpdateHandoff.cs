using System.Text;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void HandoffStartFailureCore(DevContext context, string stage, Action<string> pin)
    {
        var metadata = Path.Combine(context.Root, "focus-tests/handoff-start-failure/Metadata.cs");
        var inputs = Path.Combine(stage, "inputs"); Directory.CreateDirectory(inputs);
        var old = UpdateFrameworkCompile(context, inputs, "old.dll", new[] { metadata }, pin, defines: "NETFRAMEWORK,BAP_METADATA_OLD");
        var target = UpdateFrameworkCompile(context, inputs, "target.dll", new[] { metadata }, pin, defines: "NETFRAMEWORK");
        var helper = UpdateFrameworkCompile(context, inputs, "fixture-helper.exe", UpdateFixtureSources(context, true, true), pin,
            "BetterAstralParty.Updating.UpdateHelperProgram", "NETFRAMEWORK,BAP_FIXTURE_HELPER");
        var hostStage = Path.Combine(stage, "fixture-host"); Directory.CreateDirectory(hostStage);
        var assembly = BuildProject(context, "updater-safety-tests/BetterAstralParty.HelperTests.csproj", hostStage, pin, new[] { "-p:UseAppHost=true" });
        var host = Path.Combine(Path.GetDirectoryName(assembly)!, Path.GetFileNameWithoutExtension(assembly) + ".exe");
        Check(File.Exists(host), "Inert apphost exists");
        var boundary = Path.Combine(Path.GetTempPath(), "bap-v4-helper-fixture-" + Guid.NewGuid().ToString("N"));
        Check(!Directory.Exists(boundary), "Fresh inert helper root"); Directory.CreateDirectory(boundary);
        UpdateGenerated(boundary, ".bap-v4-helper-fixture-root", "BetterAstralParty.HelperFixtureRoot/v1\n");
        var environment = new Dictionary<string, string?>(EnvironmentFor(stage)) {
            ["BAP_HELPER_FIXTURE_ROOT"] = boundary, ["BAP_UPDATER_LAUNCHER_PID"] = null,
            ["LOCALAPPDATA"] = Path.Combine(stage, "inert-profile"), ["APPDATA"] = Path.Combine(stage, "inert-roaming")
        };
        var cases = new List<object>();
        foreach (var mode in new[] { "ready-cancel", "queued-cancel", "prestart-cancel", "invalid-image", "injected-start", "cleanup-busy" }) {
            var root = Path.Combine(boundary, "c" + cases.Count);
            Ensure(context.Run(Dotnet(context), new[] { assembly, "--handoff-setup", root, old, target, host, helper, mode }, env: environment,
                timeoutMilliseconds: 60000, logPath: Path.Combine(stage, mode + ".setup.log")), "Inert handoff setup: " + mode);
            foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(assembly)!).Where(path => Path.GetExtension(path) is ".dll" or ".json")) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            var log = Path.Combine(stage, mode + ".log");
            Ensure(context.Run(Path.Combine(root, "AstralParty_INT.exe"), new[] { "--handoff-boundary", root, mode }, cwd: root, env: environment,
                timeoutMilliseconds: 60000, logPath: log), "Actual inert handoff boundary: " + mode);
            cases.Add(new { mode, status = "Passed", logSha256 = context.Hash(log) });
        }
        context.WriteJsonCreateNew(Path.Combine(stage, "handoff-start-parity.json"), new {
            schema = 1, status = "Passed", cases, fixtureBoundary = boundary,
            sourceIsolation = "Held immutable source leases plus fresh compiler/MSBuild outputs; no shared bin/obj writes",
            retired = "LegacyCore comparison is historical unsupported-protocol evidence; all six supported boundary modes retained",
            liveNetworkUsed = false, operationalKeyUsed = false, gameSteamOrUserInstallationUsed = false
        });
    }
}
