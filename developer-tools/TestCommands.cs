using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static string[] Supported => new[] {
        "sdk-path-safety", "source-mode", "settings-input", "settings-scroll",
        "catalog-compatibility", "native-compatibility",
        "native-steam", "native-install", "native-uninstall", "native-maintenance-common",
        "native-maintenance-diagnostics", "recovery-diagnostics",
        "diagnostics-runtime", "staging", "channel-transition", "release-updates-and-readiness", "self-test-body",
        "match-focus", "card-scan-performance", "native-diagnostics-cli", "compatibility-snapshot", "native-coverage", "combat-audit", "diagnostic-schema"
    }.Concat(new[] { "field-zoom", "field-free-camera", "field-camera-return", "field-indicator-opacity", "field-indicator-input", "field-counter", "field-buffs",
        "hand-layout", "hand-scan", "hand-outline", "hand-pointer-input", "hand-submit", "card-target-buttons" })
        .Concat(GeneralSupported).Concat(MaintenanceNames.Where(name => name != "maintenance-package-integration"))
        .Concat(DiagnosticsNames).Concat(DistributionNames)
        .Concat(new[] { "shared-loader", "update-runtime-isolation", "update-notification-ui",
            "updater-primitives", "updater-transaction", "updater-apply-boundary", "update-generation" })
        .Distinct(StringComparer.Ordinal).ToArray();

    internal static int Run(DevContext context, string[] args)
    {
        if (args.Length == 0 || args[0] == "--list") { foreach (var available in Supported) Console.WriteLine(available); return 0; }
        var name = args[0];
        if (!Supported.Contains(name, StringComparer.Ordinal)) throw new InvalidOperationException("Check is not migrated to C#: " + name);
        var options = Options(args.Skip(1).ToArray());
        if (options.TryGetValue("native-assembly", out var nativeAssembly)) context = context.WithNativeAssembly(nativeAssembly);
        var stage = FreshOutput(context, options.TryGetValue("output-directory", out var requested) ? requested : null, "ct-" + name);
        var scratch = Path.Combine(context.Root, "artifacts", "ct-" + Guid.NewGuid().ToString("N")[..12]);
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        var leases = new List<FileStream>();
        object? nativeInput = null;
        void Pin(string path) {
            path = Path.GetFullPath(path);
            if (pins.ContainsKey(path)) return;
            var lease = path.Equals(context.NativeAssemblyPath, StringComparison.OrdinalIgnoreCase) ? context.OpenNativeAssemblyInput() : File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); leases.Add(lease);
            pins[path] = Convert.ToHexString(SHA256.HashData(lease)); lease.Position = 0;
            if (path.Equals(context.NativeAssemblyPath, StringComparison.OrdinalIgnoreCase))
                nativeInput = new { path, bytes = lease.Length, sha256 = pins[path], explicitOverride = context.NativeAssemblyExplicit };
        }
        context = context.WithNativeInputObserver(Pin);
        var folders = new[] { "developer-tools", "src", "updater", "diagnostics", "tools", "focus-tests", "diagnostics-tests", "updater-tests", "updater-safety-tests", "self-test" };
        string[] Inventory() => folders.SelectMany(folder => SourceFiles(Path.Combine(context.Root, folder))).Order(StringComparer.Ordinal).ToArray();
        var sourceInventory = Inventory();
        foreach (var path in sourceInventory) Pin(path);
        var passed = false;
        try {
            if (context.NativeAssemblyExplicit || File.Exists(context.NativeAssemblyPath)) {
                Pin(context.NativeAssemblyPath);
            }
            switch (name) {
                case "sdk-path-safety": SdkPathSafety(context, stage, Pin); break;
                case "source-mode": SourceMode(context, stage, Pin); break;
                case "compatibility-snapshot": {
                    var report = JsonSerializer.SerializeToElement(CompatibilitySnapshot.SelfCheck(context, stage)); Console.WriteLine(report.GetRawText());
                    Framework(context, Path.Combine(stage, "framework"), "NativeSnapshotFixture", new[] { "tools/GameCompatibility.cs", "focus-tests/compatibility-snapshot/NativeFixture.cs" }.Concat(UpdateSources(context)).ToArray(), new[] { report.GetProperty("stage").GetString()! }, Pin);
                    break;
                }
                case "settings-input": SettingsInput(context); break;
                case "settings-scroll": SettingsScroll(context); break;
                case "diagnostic-schema": DiagnosticSchema(context); break;
                case "combat-audit": CombatAudit.SelfCheck(Check); break;
                case "native-coverage":
                    NativeCoverage.SelfCheck(Check);
                    Contains(ReadSource(context, "src/RuntimeObject.cs"), new[] { "Native method missing: ", "Native field missing: ", "a field of that name exists, read it with Field",
                        "a property of that name exists, read it with Get", "Note(\"method \" + ClassName(klass)", "Note(\"field \" + ClassName(klass)" }, "Native lookup failures name the class and record the ledger");
                    break;
                case "native-install": Framework(context, stage, "FixtureEntry", new[] {
                    "tools/NativeInstall.cs", "tools/NativeMaintenanceFiles.cs", "updater/WindowsPath.cs", "updater/UpdateVersion.cs",
                    "focus-tests/native-install/FixtureDependencies.cs", "focus-tests/native-install/FixtureEntry.cs"
                }, new[] { Path.Combine(context.Root, "artifacts", "native-install-" + Guid.NewGuid().ToString("N")) }, Pin, fixtureDefine: false); break;
                case "native-steam": Framework(context, stage, "FixtureEntry", new[] {
                    "tools/NativeSteamMaintenance.cs", "updater/WindowsPath.cs", "updater/WindowsFileFence.cs", "updater/WindowsPrivateSecurity.cs",
                    "focus-tests/native-steam/FixtureGuard.cs", "focus-tests/native-steam/FixtureEntry.cs", "focus-tests/native-steam/DiscoveryCases.cs"
                }, new[] { Path.Combine(context.Root, "artifacts", "native-steam-" + Guid.NewGuid().ToString("N")) }, Pin); break;
                case "native-maintenance-common": Framework(context, stage, "NativeCommonFixture", new[] {
                    "tools/NativeMaintenance.cs", "tools/NativeMaintenanceBundle.cs", "tools/NativeMaintenanceEntry.cs", "tools/NativeMaintenanceFiles.cs", "tools/MaintenanceJsonReader.cs",
                    "focus-tests/native-maintenance-common/Program.cs", "focus-tests/native-maintenance-common/BoundaryCases.cs", "focus-tests/native-maintenance-common/Facades.cs", "focus-tests/native-maintenance-common/Globals.cs"
                }.Concat(UpdateSources(context)).ToArray(), new[] { scratch, Path.Combine(context.Root, "artifacts") }, Pin); break;
                case "native-maintenance-diagnostics": NativeDiagnostics(context, stage, Pin); break;
                case "card-scan-performance": CardScan(context, stage, Pin); break;
                default:
                    if (name == "staging") { if (!UpdateChecks(context, name, stage, scratch, options, Pin)) throw new InvalidOperationException("Staging validation is not migrated."); break; }
                    if (MaintenanceNames.Contains(name, StringComparer.Ordinal)) { RunMaintenance(context, name, stage, scratch, options, Pin); break; }
                    if (DiagnosticsNames.Contains(name, StringComparer.Ordinal)) { RunDiagnostics(context, name, stage, scratch, options, Pin); break; }
                    if (DistributionNames.Contains(name, StringComparer.Ordinal)) { RunDistribution(context, name, stage, scratch, options, Pin); break; }
                    if (!FieldChecks(context, name, stage, scratch, options, Pin)
                        && !HandChecks(context, name, stage, scratch, options, Pin)
                        && !TryGeneral(context, name, stage, scratch, options, Pin)
                        && !UpdateChecks(context, name, stage, scratch, options, Pin))
                        ProjectFixture(context, name, stage, scratch, options, Pin);
                    break;
            }
            DevContext.RequireInventory(sourceInventory, Inventory());
            foreach (var pair in pins) if (context.Hash(pair.Key) != pair.Value) throw new IOException("Tested source changed: " + pair.Key);
            context.WriteJsonCreateNew(Path.Combine(stage, "csharp-check.json"), new {
                schema = 1, name, passed = true, utc = DateTime.UtcNow, sources = pins, nativeInput,
                powerShellExecuted = false, realInstallationPerformed = false, publicationPerformed = false,
                historicalPowerShellParityExecuted = false
            });
            passed = true;
            return 0;
        } catch (Exception error) {
            context.WriteJsonCreateNew(Path.Combine(stage, "csharp-check-failed.json"), new { schema = 1, name, passed = false, error = error.ToString(), sources = pins, nativeInput });
            throw;
        } finally {
            foreach (var lease in leases) lease.Dispose();
            // A passing check's scratch is spent; a failing check keeps it for inspection.
            if (passed) DeleteScratch(scratch);
        }
    }

    internal static void DeleteScratch(string scratch)
    {
        try {
            if (Directory.Exists(scratch) && (File.GetAttributes(scratch) & FileAttributes.ReparsePoint) == 0) DeleteTree(new DirectoryInfo(scratch));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            Console.Error.WriteLine("Check scratch kept: " + scratch + " (" + error.Message + ")");
        }
    }

    // Not Directory.Delete(recursive): it unmounts junctions with DeleteVolumeMountPoint, which is denied without elevation.
    // A link is removed as a link and never followed; read-only flags are cleared only on real entries.
    private static void DeleteTree(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos()) {
            var linked = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            if (!linked && (entry.Attributes & FileAttributes.ReadOnly) != 0) entry.Attributes &= ~FileAttributes.ReadOnly;
            if (!linked && entry is DirectoryInfo child) DeleteTree(child);
            else entry.Delete();
        }
        directory.Delete();
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2) {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length || !result.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Unique --name value options required.");
        }
        return result;
    }
    private static string FreshOutput(DevContext context, string? requested, string prefix)
    {
        var artifacts = Path.Combine(context.Root, "artifacts");
        var output = Path.GetFullPath(requested ?? Path.Combine(artifacts, prefix + "-" + Guid.NewGuid().ToString("N")[..12]));
        if (!output.StartsWith(artifacts + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output) || File.Exists(output))
            throw new IOException("Fresh project-artifact output required.");
        for (var parent = Path.GetDirectoryName(output); parent != null && parent.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked fixture output rejected.");
        Directory.CreateDirectory(output); return output;
    }
    private static IEnumerable<string> SourceFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.EnumerateFiles(root)) if (Path.GetExtension(path) is ".cs" or ".csproj") {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked source file rejected.");
            yield return path;
        }
        foreach (var directory in Directory.EnumerateDirectories(root)) {
            if (Path.GetFileName(directory) is "bin" or "obj" or "review-candidates") continue;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked source directory rejected.");
            foreach (var path in SourceFiles(directory)) yield return path;
        }
    }
    private static string[] UpdateSources(DevContext context) => new[] { "updater", "src/Observability" }
        .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(context.Root, folder), "*.cs"))
        .Where(path => Path.GetFileName(path) != "UpdateHelperProgram.cs").Select(path => Path.GetRelativePath(context.Root, path)).ToArray();
    private static void Ensure(int exit, string stage) { if (exit != 0) throw new InvalidOperationException(stage + " failed with exit " + exit); }
    private static string Dotnet(DevContext context) => Path.Combine(context.DotnetRoot, "dotnet.exe");
    private static Dictionary<string, string?> EnvironmentFor(string stage) => new() {
        ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false", ["DOTNET_CLI_HOME"] = Path.Combine(stage, "dotnet-home"),
        ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1", ["DOTNET_CLI_UI_LANGUAGE"] = "en"
    };
    private static string BuildProject(DevContext context, string relative, string stage, Action<string> pin, IEnumerable<string>? properties = null)
    {
        var project = Path.Combine(context.Root, relative); pin(project);
        var sourceDirectory = Path.GetDirectoryName(project)!;
        var projectSources = SourceFiles(sourceDirectory).Order(StringComparer.Ordinal).ToArray();
        foreach (var source in projectSources) pin(source);
        var obj = Path.Combine(stage, "obj") + Path.DirectorySeparatorChar;
        var common = new[] { "-p:BaseIntermediateOutputPath=" + obj, "-p:MSBuildProjectExtensionsPath=" + obj,
            "-p:UseSharedCompilation=false", "-p:NuGetAudit=false", "-p:CheckEolTargetFramework=false" }.Concat(properties ?? Array.Empty<string>()).ToArray();
        Ensure(context.Run(Dotnet(context), new[] { "restore", project, "--configfile", Path.Combine(context.Root, "NuGet.Config") }.Concat(common), env: EnvironmentFor(stage)), "Fixture restore");
        var bin = Path.Combine(stage, "bin");
        Ensure(context.Run(Dotnet(context), new[] { "build", project, "--no-restore", "-c", "Release", "-o", bin }.Concat(common), env: EnvironmentFor(stage)), "Fixture build");
        DevContext.RequireInventory(projectSources, SourceFiles(sourceDirectory).Order(StringComparer.Ordinal));
        var assembly = XDocument.Load(project).Descendants("AssemblyName").FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(project);
        return Path.Combine(bin, assembly + ".dll");
    }
    private static void ProjectFixture(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        var recipe = name switch {
            "native-diagnostics-cli" => ("focus-tests/native-diagnostics-cli/NativeDiagnosticsCliFixture.csproj", Array.Empty<string>()),
            "catalog-compatibility" => ("focus-tests/catalog-compatibility/CatalogCompatibilityFixture.csproj", new[] { scratch, Path.Combine(context.Root, "artifacts") }),
            "native-compatibility" => ("focus-tests/native-compatibility/NativeCompatibilityFixture.csproj", new[] { scratch, Path.Combine(context.Root, "artifacts") }),
            "native-uninstall" => ("focus-tests/native-uninstall/NativeUninstallFixture.csproj", new[] { scratch, Path.Combine(context.Root, "artifacts") }),
            "recovery-diagnostics" => ("diagnostics-tests/BetterAstralParty.Diagnostics.RecoveryFixture.csproj", new[] { scratch }),
            "diagnostics-runtime" => ("diagnostics-tests/BetterAstralParty.Diagnostics.Fixture.csproj", Array.Empty<string>()),
            "staging" => ("updater-safety-tests/BetterAstralParty.StagingTests.csproj", new[] { scratch }),
            "channel-transition" => ("focus-tests/channel-transition/ChannelTransitionTests.csproj", new[] { stage }),
            "release-updates-and-readiness" => ("self-test/BetterAstralParty.SelfTest.csproj", new[] { "--release-updates-only" }),
            // The whole self-test body (session, focus audio, hand layout, notification ABI, diagnostics rotation and more).
            "self-test-body" => ("self-test/BetterAstralParty.SelfTest.csproj", Array.Empty<string>()),
            "match-focus" => ("focus-tests/BetterAstralParty.MatchFocusTests.csproj", new[] { context.NativeAssemblyPath }),
            _ => throw new InvalidOperationException("No C# recipe: " + name)
        };
        if (name == "match-focus") pin(recipe.Item2.Single());
        var props = options.TryGetValue("bepinex-root", out var loader) ? new[] { "-p:BepInExRoot=" + Path.GetFullPath(loader) } : Array.Empty<string>();
        var assembly = BuildProject(context, recipe.Item1, stage, pin, props);
        Ensure(context.Run(Dotnet(context), new[] { assembly }.Concat(recipe.Item2), env: EnvironmentFor(stage)), "Fixture runtime");
        if (name == "native-compatibility") Framework(context, Path.Combine(stage, "framework"), "FrameworkCompatibilityParity", new[] { "focus-tests/native-compatibility/FrameworkParity.cs", "tools/GameCompatibility.cs" }.Concat(UpdateSources(context)).ToArray(), recipe.Item2, pin);
        if (name == "native-compatibility" && options.TryGetValue("compatibility-path", out var manifest)) {
            if (!options.TryGetValue("game-root", out var game)) throw new ArgumentException("An explicit game root is required for observed compatibility.");
            manifest = Path.GetFullPath(manifest); game = Path.GetFullPath(game); pin(manifest);
            var image = Framework(context, Path.Combine(stage, "observed-game"), "ObservedGameProbe", new[] { "focus-tests/native-compatibility/ObservedGameProbe.cs", "tools/GameCompatibility.cs" }.Concat(UpdateSources(context)).ToArray(),
                new[] { game, manifest, options.TryGetValue("bepinex-root", out var bepinex) ? Path.GetFullPath(bepinex) : Path.Combine(context.Root, ".deps", "bepinex") }, pin, fixtureDefine: false);
            context.WriteJsonCreateNew(Path.Combine(stage, "observed-game-compatibility.json"), new { passed = true, gameRoot = game, manifestSha256 = context.Hash(manifest), inspectorSha256 = context.Hash(image), readOnly = true, runtimeExecutionVerified = false });
        }
    }
    private static string Compiler(DevContext context) => Directory.EnumerateDirectories(Path.Combine(context.DotnetRoot, "sdk"))
        .Where(path => File.Exists(Path.Combine(path, "Roslyn", "bincore", "csc.dll")))
        .OrderByDescending(path => Version.Parse(Path.GetFileName(path))).Select(path => Path.Combine(path, "Roslyn", "bincore", "csc.dll")).First();
    private static string Framework(DevContext context, string stage, string entry, string[] sourcePaths, string[] arguments, Action<string> pin, bool fixtureDefine = true)
    {
        if (sourcePaths.Any(path => Path.GetFileName(path) == "PublicReleaseEligibility.cs") && !sourcePaths.Any(path => Path.GetFileName(path) == "MaintenanceJsonReader.cs"))
            sourcePaths = sourcePaths.Append("tools/MaintenanceJsonReader.cs").ToArray();
        Directory.CreateDirectory(stage);
        var image = Path.Combine(stage, entry + ".exe");
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
        var flags = new List<string> { Compiler(context), "/nologo", "/noconfig", "/utf8output", "/nostdlib+", "/langversion:latest", "/nullable:enable",
            "/deterministic+", "/platform:x64", "/target:exe", "/main:" + entry, "/out:" + image,
            "/define:" + (fixtureDefine ? "NETFRAMEWORK,BAP_SAFETY_FIXTURE" : "NETFRAMEWORK"), "/nowarn:8600,8601,8602,8603,8604,8618,8625" };
        foreach (var name in new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Security.dll", "System.Web.Extensions.dll", "System.Net.Http.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll" }) flags.Add("/reference:" + Path.Combine(framework, name));
        foreach (var relative in sourcePaths) { var path = Path.IsPathRooted(relative) ? relative : Path.Combine(context.Root, relative); pin(path); flags.Add(path); }
        Ensure(context.Run(Dotnet(context), flags, env: EnvironmentFor(stage)), ".NET Framework fixture compilation");
        Ensure(context.Run(image, arguments, cwd: stage, env: new Dictionary<string, string?> { ["LOCALAPPDATA"] = Path.Combine(stage, "inert-profile") }), ".NET Framework fixture runtime");
        return image;
    }
}
