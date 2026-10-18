using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static readonly string[] MaintenanceNames = ["maintenance-frontends", "maintenance-paths", "long-paths", "maintenance-preservation", "maintenance-history", "history-binding", "native-maintenance-integration", "native-reinstall-generation", "failure-context", "install-log", "install-bundle-missing", "install-reader-origin", "install-recovery-diagnostics", "install-safety", "install-transaction", "install-transaction-long-ps7", "install-transaction-long-ps5", "install-transaction-root1100-ps7", "install-transaction-root1100-ps5", "install-transaction-target1100-ps7", "install-transaction-target1100-ps5", "steam-session", "steam-shortcut", "steam-observation", "find-game", "uninstall-steam-boundary", "uninstall", "uninstall-updated-receipt", "maintenance-package-integration", "whole-channel-transition"];
    private static string CompileFrameworkOnly(DevContext context, string stage, string entry, string[] sourcePaths, Action<string> pin, string target = "exe", string defines = "NETFRAMEWORK;BAP_SAFETY_FIXTURE", string? output = null)
    {
        if (sourcePaths.Any(path => Path.GetFileName(path) == "PublicReleaseEligibility.cs") && !sourcePaths.Any(path => Path.GetFileName(path) == "MaintenanceJsonReader.cs"))
            sourcePaths = sourcePaths.Append("tools/MaintenanceJsonReader.cs").ToArray();
        Directory.CreateDirectory(stage); var image = output ?? Path.Combine(stage, entry + (target is "exe" or "winexe" ? ".exe" : ".dll")); Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        Check(!File.Exists(image), "Framework fixture output is create-new");
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
        var flags = new List<string> { Compiler(context), "/nologo", "/noconfig", "/utf8output", "/nostdlib+", "/langversion:latest", "/nullable:enable", "/deterministic+", "/platform:x64", "/target:" + target, "/out:" + image };
        if (defines.Length != 0) flags.Add("/define:" + defines);
        if (target is "exe" or "winexe") flags.Add("/main:" + entry);
        foreach (var name in new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Security.dll", "System.Web.Extensions.dll", "System.Net.Http.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll", "System.Windows.Forms.dll", "System.Drawing.dll" }) flags.Add("/reference:" + Path.Combine(framework, name));
        foreach (var relative in sourcePaths) { var path = Path.IsPathRooted(relative) ? relative : Path.Combine(context.Root, relative); pin(path); flags.Add(path); }
        Ensure(context.Run(Dotnet(context), flags, env: EnvironmentFor(stage)), "Inert Framework fixture compile"); return image;
    }
    private static void RunMaintenance(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        switch (name) {
            case "maintenance-frontends": MaintenanceFrontends(context, stage, pin); break;
            case "native-maintenance-integration": NativeMaintenanceIntegration(context, stage, pin); break;
            case "history-binding": MaintenanceHistoryBinding(context, stage, pin); break;
            case "maintenance-history": MaintenanceHistory(context, stage, pin); break;
            case "native-reinstall-generation": NativeReinstallMatrix(context,stage,scratch,pin);break;
            case "whole-channel-transition": WholeNativeHistory(context,name,stage,pin);break;
            case "maintenance-paths": case "long-paths": MaintenancePaths(context, stage, pin); break;
            case "install-log": case "install-recovery-diagnostics": NativeDiagnostics(context, Path.Combine(stage,"native-maintenance-diagnostics-fixture"), pin); break;
            case "failure-context": NativeDiagnostics(context,Path.Combine(stage,"native-maintenance-diagnostics-fixture"),pin,true);break;
            case "install-bundle-missing": CommonMaintenanceScenario(context,stage,scratch,pin,"bundle-missing");break;
            case "install-reader-origin": CommonMaintenanceScenario(context,stage,scratch,pin,"reader-origin");break;
            case "install-safety": CommonMaintenanceScenario(context,stage,scratch,pin,"safety");break;
            case "install-transaction": CommonMaintenanceScenario(context,stage,scratch,pin,"transaction-standard");break;
            case "install-transaction-long-ps7": case "install-transaction-long-ps5": case "install-transaction-root1100-ps7": case "install-transaction-root1100-ps5": case "install-transaction-target1100-ps7": case "install-transaction-target1100-ps5": CommonMaintenanceScenario(context,stage,scratch,pin,name[8..]);break;
            case "maintenance-preservation": CommonMaintenanceScenario(context,stage,scratch,pin,"preservation");UninstallMaintenanceScenario(context,stage,scratch+"-uninstall",pin);break;
            case "uninstall": case "uninstall-updated-receipt": UninstallMaintenanceScenario(context,stage,scratch,pin);break;
            case "uninstall-steam-boundary": UninstallMaintenanceScenario(context,stage,scratch,pin);SteamMaintenanceScenario(context,stage,pin,"session");break;
            case "steam-session": SteamMaintenanceScenario(context,stage,pin,"session");break;
            case "steam-shortcut": SteamMaintenanceScenario(context,stage,pin,"shortcut");break;
            case "steam-observation": SteamMaintenanceScenario(context,stage,pin,"observation");break;
            case "find-game": SteamMaintenanceScenario(context,stage,pin,"discovery");break;
            default: throw new InvalidOperationException("Maintenance port not yet complete; no PASS recorded: " + name);
        }
    }
    private static void CommonMaintenanceScenario(DevContext context,string stage,string scratch,Action<string> pin,string mode)
    {
        var source=new[]{"tools/NativeMaintenance.cs","tools/NativeMaintenanceFiles.cs","tools/NativeMaintenanceBundle.cs","tools/NativeMaintenanceEntry.cs","tools/MaintenanceJsonReader.cs","focus-tests/native-maintenance-common/Program.cs","focus-tests/native-maintenance-common/BoundaryCases.cs","focus-tests/native-maintenance-common/Facades.cs","focus-tests/native-maintenance-common/Globals.cs"}.Concat(UpdateSources(context)).ToArray();
        var image=CompileFrameworkOnly(context,Path.Combine(stage,"native-common"),"NativeCommonFixture",source,pin);var arguments=new List<string>{scratch,Path.Combine(context.Root,"artifacts"),mode};
        if(mode=="reader-origin")arguments.Add(CompileFrameworkOnly(context,Path.Combine(stage,"metadata"),"Metadata",["focus-tests/native-reinstall-generation/MetadataPlugin.cs"],pin,target:"library",defines:""));
        Ensure(context.Run(image,arguments,cwd:stage,env:new Dictionary<string,string?>{["LOCALAPPDATA"]=Path.Combine(stage,"inert-profile")}),"actual native maintenance semantic matrix "+mode);
        context.WriteJsonCreateNew(Path.Combine(stage,"native-semantic-"+mode+".json"),new{passed=true,actualFileFenceAndOrdinaryRecovery=true,packageAuthenticationAndSteamFacades=true,operationalSigning=false,retired=mode=="reader-origin"?"PowerShell Mono.Cecil type-cache/import and interpreter comparisons; native byte-only parser is used":mode.StartsWith("transaction-",StringComparison.Ordinal)?"PowerShell 5/7 interpreter comparison; each named case executes the equivalent native path geometry":""});
    }
    private static void UninstallMaintenanceScenario(DevContext context,string stage,string scratch,Action<string> pin)
    {
        var assembly=BuildProject(context,"focus-tests/native-uninstall/NativeUninstallFixture.csproj",Path.Combine(stage,"native-uninstall"),pin);
        Ensure(context.Run(Dotnet(context),[assembly,scratch,Path.Combine(context.Root,"artifacts")],env:EnvironmentFor(stage)),"actual native uninstall owned-byte and recovery matrix");
    }
    private static void SteamMaintenanceScenario(DevContext context,string stage,Action<string> pin,string mode)
    {
        var fixture=Path.Combine(stage,"native-steam-"+mode);Directory.CreateDirectory(fixture);
        Framework(context,Path.Combine(fixture,"compile"),"FixtureEntry",["tools/NativeSteamMaintenance.cs","updater/WindowsPath.cs","updater/WindowsFileFence.cs","updater/WindowsPrivateSecurity.cs","focus-tests/native-steam/FixtureGuard.cs","focus-tests/native-steam/FixtureEntry.cs","focus-tests/native-steam/DiscoveryCases.cs"],[fixture,mode],pin);
    }
    private static void MaintenanceFrontends(DevContext context, string stage, Action<string> pin)
    {
        var stub = CompileFrameworkOnly(context, Path.Combine(stage, "stub"), "NativeFrontendStub", ["focus-tests/native-maintenance-common/FrontendStub.cs"], pin, defines: "NETFRAMEWORK");
        var sources = new Dictionary<string, byte[]>();
        foreach (var entry in new[] { "Install.cmd", "Uninstall.cmd", "Force-Uninstall.cmd" }) { var path = Path.Combine(context.Root, entry); pin(path); sources.Add(entry, File.ReadAllBytes(path)); }
        var cwd = Path.Combine(stage, "unrelated-working-directory"); Directory.CreateDirectory(cwd); var number = 0;
        foreach (var label in new[] { "plain", "가 spaces", "brackets [1] (2)", "amp&ersand", "bang!folder", "percent%folder" }) foreach (var development in new[] { false, true }) foreach (var entry in sources.Keys) foreach (var delayed in new[] { "off", "on" }) {
            var previews = label == "plain" && delayed == "off" ? new[] { "", "-WhatIf", "--WhatIf", "-CheckOnly", "--CheckOnly" } : [""];
            foreach (var preview in previews) foreach (var status in preview.Length == 0 && label == "plain" && delayed == "off" ? new[] { 0, 7, -1 } : preview.Length != 0 ? new[] { 0, 7 } : [0]) {
                var root = Path.Combine(stage, "frontend-" + ++number, label); Directory.CreateDirectory(Path.Combine(root, "artifacts")); File.WriteAllBytes(Path.Combine(root, entry), sources[entry]); File.Copy(stub, Path.Combine(root, "artifacts/BetterAstralParty-Setup.exe"), false);
                if (!development) File.WriteAllText(Path.Combine(root, "package-manifest.json"), "{}");
                var resultPath = Path.Combine(root, "inert-result.json"); var game = Path.Combine(cwd, "selected game [1] (2)"); var relative = label == "bang!folder" && delayed == "on"; var invocation = relative ? entry : Path.Combine(root, entry); var caller = relative ? root : cwd;
                var environment = new Dictionary<string, string?> { ["LOCALAPPDATA"] = Path.Combine(root, "local-data"), ["TEMP"] = Path.Combine(root, "temp-data"), ["BAP_ENTRY_RESULT"] = resultPath, ["BAP_FIXTURE_EXIT"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture), ["BAP_EXIT"] = "parent-value" };
                var code = RunFrontendCommand("\"\"" + invocation + "\" -GameRoot \"" + game + "\" " + preview + " <nul\"", delayed, caller, environment);
                Check(code == status && File.Exists(resultPath), "CMD exit code and inert dispatch: " + number);
                var result = PackagingBoundary.Parse(File.ReadAllBytes(resultPath)); var operation = entry == "Install.cmd" ? "install" : entry == "Uninstall.cmd" ? "uninstall" : "force-uninstall";
                Check(PackagingBoundary.Text(result, "executableRoot") == Path.Combine(root, "artifacts") && PackagingBoundary.Text(result, "callerRoot") == caller && PackagingBoundary.Text(result, "gameRoot") == game && PackagingBoundary.Text(result, "operation") == operation && PackagingBoundary.Flag(result, "development") == development && !PackagingBoundary.Flag(result, "actualInstallerExecuted"), "CMD exact path/arguments/branch: " + number);
                if (preview.Length != 0) Check(!Directory.Exists(environment["LOCALAPPDATA"]!) && !Directory.Exists(environment["TEMP"]!) && result.GetProperty("args").EnumerateArray().Any(value => value.GetString() == preview), "CMD preview creates no bootstrap files: " + number);
            }
        }
        foreach (var development in new[] { false, true }) foreach (var entry in sources.Keys) foreach (var preview in new[] { "", "-WhatIf" }) {
            var root = Path.Combine(stage, "missing-exe-" + ++number); Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, entry), sources[entry]); if (!development) File.WriteAllText(Path.Combine(root, "package-manifest.json"), "{}");
            var local = Path.Combine(root, "local-data"); var temporary = Path.Combine(root, "temp-data");
            var code = RunFrontendCommand("\"\"" + Path.Combine(root, entry) + "\" " + preview + " <nul\"", "off", cwd, new Dictionary<string, string?> { ["LOCALAPPDATA"] = local, ["TEMP"] = temporary });
            Check(code != 0 && (preview.Length == 0 || !Directory.Exists(local) && !Directory.Exists(temporary)), "Missing setup fails without preview writes: " + number);
        }
        context.WriteJsonCreateNew(Path.Combine(stage, "frontend-matrix.json"), new { cases = number, actualInstallerExecuted = false, actualGameOrSteamTouched = false, parentDelayedExpansionTested = true });
    }
    private static int RunFrontendCommand(string command, string delayed, string cwd, IReadOnlyDictionary<string, string?> environment)
    {
        // CMD has its own /s /c grammar; ArgumentList would escape its quotes as C argv.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) {
            Arguments = "/d /s /v:" + delayed + " /c " + command, WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var pair in environment) { if (pair.Value == null) start.Environment.Remove(pair.Key); else start.Environment[pair.Key] = pair.Value; }
        using var process = Process.Start(start) ?? throw new IOException("Inert frontend command did not start.");
        process.StandardInput.Close(); var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new TimeoutException("Inert frontend command timed out."); }
        Console.Write(output.GetAwaiter().GetResult()); Console.Error.Write(errors.GetAwaiter().GetResult()); return process.ExitCode;
    }
    private static void MaintenancePaths(DevContext context, string stage, Action<string> pin)
    {
        var boundary = Path.Combine(stage, "FixtureBoundary.cs"); File.WriteAllText(boundary, "internal static class FixtureBoundary { internal const string Root = @\"" + stage.Replace("\"", "\"\"") + "\"; }", new UTF8Encoding(false));
        Framework(context, Path.Combine(stage, "framework"), "FixtureEntry", new[] { "focus-tests/long-paths/FixtureEntry.cs", boundary }.Concat(UpdateSources(context)).ToArray(), [], pin, fixtureDefine: false);
        var assembly=BuildProject(context,"focus-tests/long-paths/LongPathFixture.csproj",Path.Combine(stage,"net8"),pin,["-p:FixtureBoundarySource="+boundary]);
        Ensure(context.Run(Dotnet(context),[assembly],env:EnvironmentFor(stage)),"actual NET8 and Framework NTFS longpath parity");
    }
}
