using System.Text;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static readonly string[] DiagnosticsNames = ["diagnostics-bundle", "diagnostics-zip", "diagnostics-structured", "diagnostics-long-path", "sdk-first-use"];
    private static void RunDiagnostics(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        if (name == "sdk-first-use") { SdkFirstUse(context, stage, pin); return; }
        var assembly = BuildProject(context, "focus-tests/diagnostics-structured/DiagnosticsStructuredFixture.csproj", stage, pin);
        Ensure(context.Run(Dotnet(context), [assembly, scratch, name=="diagnostics-bundle"?"diagnostics-zip":name], env: EnvironmentFor(stage)), "Actual scoped diagnostics ZIP fixture: " + name);
        if (name == "diagnostics-structured") {
            var health = BuildProject(context, "diagnostics-tests/RuntimeHealthFixture.csproj", Path.Combine(stage, "typed-health-host"), pin);
            Ensure(context.Run(Dotnet(context), [health, Path.Combine(stage, "typed-channel-health")], env: EnvironmentFor(stage), timeoutMilliseconds: 60000, logPath: Path.Combine(stage, "typed-channel-health.log")), "Actual typed writer, fenced store and bounded channel/settings diagnostics ZIP");
        }
    }
    private static void SdkFirstUse(DevContext context, string stage, Action<string> pin)
    {
        var before = DevContext.PersistentPaths(); var processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
        var sdks = Directory.EnumerateDirectories(Path.Combine(context.DotnetRoot, "sdk")).Select(path => (Path: path, Version: Version.Parse(Path.GetFileName(path))))
            .Where(row => row.Version.Major is 6 or 8).GroupBy(row => row.Version.Major).Select(group => group.OrderByDescending(row => row.Version).First()).OrderBy(row => row.Version).ToArray();
        Check(sdks.Length == 2, "Both locally installed SDK generations are required; no downloads");
        var project = Path.Combine(context.Root, "focus-tests/sdk-path-safety/SdkPathSafetyFixture.csproj"); pin(project); pin(Path.Combine(context.Root, "NuGet.Config"));
        var attempts = 0;
        foreach (var sdk in sdks) foreach (var attempt in new[] { 1, 2 }) {
            var root = Path.Combine(stage, "sdk-" + sdk.Version + "-" + attempt); Directory.CreateDirectory(root); var home = Path.Combine(root, "home");
            File.WriteAllText(Path.Combine(root, "global.json"), System.Text.Json.JsonSerializer.Serialize(new { sdk = new { version = sdk.Version.ToString(), rollForward = "disable" } }), new UTF8Encoding(false));
            var obj = Path.Combine(root, "obj") + Path.DirectorySeparatorChar;
            var env = new Dictionary<string, string?> { ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false", ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1", ["DOTNET_CLI_HOME"] = home, ["NUGET_PACKAGES"] = Path.Combine(home, "packages") };
            Ensure(context.Run(Dotnet(context), ["restore", project, "--configfile", Path.Combine(context.Root, "NuGet.Config"), "-p:BaseIntermediateOutputPath=" + obj, "-p:MSBuildProjectExtensionsPath=" + obj, "-p:NuGetAudit=false", "-p:CheckEolTargetFramework=false"], cwd: root, env: env), "Offline SDK first-use " + sdk.Version);
            Check(DevContext.PersistentPaths() == before && Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) == processPath, "Persistent and parent PATH unchanged after SDK " + sdk.Version);
            Check(!File.Exists(Path.Combine(home, ".dotnet", sdk.Version + ".toolpath.sentinel")), "SDK global-tool PATH registration did not run"); attempts++;
        }
        context.WriteJsonCreateNew(Path.Combine(stage, "sdk-first-use.json"), new { passed = true, attempts, generations = sdks.Select(row => row.Version.Major).ToArray(), persistentPathUnchanged = DevContext.PersistentPaths() == before, processPathUnchanged = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) == processPath, userOrMachineEnvironmentWritten = false, noNetworkOrInstallation = true });
    }
}
