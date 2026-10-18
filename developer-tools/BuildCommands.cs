using System.Security.Cryptography;
using System.Text;

namespace BetterAstralParty.Development;

internal static class BuildCommands
{
    public static string[] Checks => new[] {
        "sdk-path-safety", "source-mode", "settings-input", "settings-scroll", "shared-loader",
        "install-bundle-missing", "install-reader-origin", "update-runtime-isolation", "long-paths", "field-zoom", "field-free-camera", "field-camera-return",
        "recovery-diagnostics", "catalog-compatibility",
        "install-recovery-diagnostics", "diagnostics-bundle", "failure-context", "steam-session", "steam-shortcut", "steam-observation",
        "uninstall-steam-boundary", "install-log", "maintenance-history", "release-updates-and-readiness", "maintenance-preservation", "uninstall-updated-receipt", "install-safety", "install-transaction",
        "install-transaction-long-ps7", "install-transaction-long-ps5", "install-transaction-root1100-ps7", "install-transaction-root1100-ps5",
        "install-transaction-target1100-ps7", "install-transaction-target1100-ps5", "find-game", "maintenance-frontends", "maintenance-paths",
        "channel-transition", "update-notification-ui", "history-binding", "staging", "export-release-source",
        "native-compatibility", "native-steam", "native-install", "native-uninstall", "native-reinstall-generation",
        "native-maintenance-common", "native-maintenance-integration", "native-maintenance-diagnostics"
    }.Concat(TestCommands.Supported).Distinct(StringComparer.Ordinal).ToArray();

    internal static int Run(DevContext context, string[] args)
    {
        var options = Options(args);
        var checks = SelectChecks(options.GetValueOrDefault("checks"));
        var versionOverride = options.TryGetValue("version", out var requestedVersion) ? BuildVersion.Canonical(requestedVersion!) : null;
        if (options.TryGetValue("native-assembly", out var nativeAssembly)) context = context.WithNativeAssembly(nativeAssembly!);
        var output = context.FreshArtifactOutput(options.TryGetValue("output-directory", out var path) ? path!
            : Path.Combine(context.Root, "artifacts", "development-build-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        var game = Path.GetFullPath(options.TryGetValue("game-root", out var gamePath) ? gamePath!
            : Path.Combine(Directory.GetParent(context.Root)!.FullName, "8vJXnINT"));
        var bepinex = Path.GetFullPath(options.TryGetValue("bepinex-root", out var loader) ? loader! : Path.Combine(context.Root, ".deps", "bepinex"));
        var configuration = options.ContainsKey("release") ? "Release" : "Debug";
        if (!options.TryGetValue("compatibility-path", out var compatibilityPath)) throw new ArgumentException("Explicit reviewed --compatibility-path input is required.");
        Directory.CreateDirectory(output);
        var leases = new List<FileStream>();
        var before = DevContext.PersistentPaths();
        Exception? failure = null;
        try {
            var nativeLease = context.OpenNativeAssemblyInput(); leases.Add(nativeLease);
            var nativeHash = Convert.ToHexString(SHA256.HashData(nativeLease)); nativeLease.Position = 0;
            var nativeInput = new { path = Path.GetRelativePath(context.Root, context.NativeAssemblyPath).Replace('\\', '/'), bytes = nativeLease.Length, sha256 = nativeHash, explicitOverride = context.NativeAssemblyExplicit };
            var records = new List<object>();
            var sourcePaths = Sources(context.Root).ToArray();
            foreach (var relative in sourcePaths) {
                var source = Path.Combine(context.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                var lease = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read); leases.Add(lease);
                records.Add(new { path = relative, bytes = lease.Length, sha256 = Convert.ToHexString(SHA256.HashData(lease)) });
            }
            context.WriteJsonCreateNew(Path.Combine(output, "build-source.json"), records);
            var pluginSource = File.ReadAllBytes(Path.Combine(context.Root, "src", "Plugin.cs"));
            PackagingInput? generatedPlugin = null;
            if (versionOverride != null) {
                var bytes = BuildVersion.Transform(pluginSource, versionOverride);
                var generatedPath = Path.Combine(output, BuildVersion.GeneratedPath);
                Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
                using (var generated = new FileStream(generatedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { generated.Write(bytes); generated.Flush(true); }
                leases.Add(File.Open(generatedPath, FileMode.Open, FileAccess.Read, FileShare.Read));
                generatedPlugin = new(BuildVersion.GeneratedPath, bytes.LongLength, context.Hash(generatedPath));
            }
            var compatibilityLease = File.Open(Path.GetFullPath(compatibilityPath!), FileMode.Open, FileAccess.Read, FileShare.Read); leases.Add(compatibilityLease);
            if (compatibilityLease.Length == 0 || compatibilityLease.Length > 1024 * 1024) throw new InvalidDataException("Compatibility input bounds.");
            var compatibilityHash = Convert.ToHexString(SHA256.HashData(compatibilityLease)); compatibilityLease.Position = 0;
            var compatibilityRelative = "compatibility/BetterAstralParty.compatibility.json";
            Directory.CreateDirectory(Path.Combine(output, "compatibility"));
            using (var copy = new FileStream(Path.Combine(output, compatibilityRelative), FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                compatibilityLease.CopyTo(copy); copy.Flush(true);
            }
            var environment = new Dictionary<string, string?> {
                ["DOTNET_CLI_HOME"] = Path.Combine(output, "dotnet-home"), ["NUGET_PACKAGES"] = Path.Combine(output, "packages"),
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false"
            };
            Check(context, "sdk-path-safety", output, game, bepinex);
            var pluginProperties = new List<string> { "-p:GameRoot=" + game, "-p:BepInExRoot=" + bepinex, "-p:CheckEolTargetFramework=false" };
            if (generatedPlugin != null) pluginProperties.Add("-p:BapVersionOverrideSource=" + Path.Combine(output, BuildVersion.GeneratedPath).Replace("%", "%25").Replace(";", "%3B").Replace(",", "%2C"));
            BuildProject(context, Path.Combine(context.Root, "src", "BetterAstralParty.csproj"), Path.Combine(output, "plugin"),
                Path.Combine(output, "plugin-obj"), configuration, environment, pluginProperties);
            CompileNative(context, "Setup", Path.Combine(output, "setup"), versionOverride);
            foreach (var check in checks.Where(value => value != "sdk-path-safety" && value != "release-updates-and-readiness"))
                Check(context, check, output, game, bepinex);
            var selfTests = new List<string>();
            if (checks.Contains("release-updates-and-readiness", StringComparer.Ordinal)) selfTests.Add("--release-updates-only");
            if (checks.Contains("settings-input", StringComparer.Ordinal) || checks.Contains("settings-scroll", StringComparer.Ordinal)) selfTests.Add("--settings-only");
            if (selfTests.Count != 0) {
                BuildProject(context, Path.Combine(context.Root, "self-test", "BetterAstralParty.SelfTest.csproj"), Path.Combine(output, "selftest"),
                    Path.Combine(output, "selftest-obj"), "Release", environment, Array.Empty<string>());
                foreach (var selfTest in selfTests)
                    Step(context, Path.Combine(context.DotnetRoot, "dotnet.exe"), new[] { Path.Combine(output, "selftest", "BetterAstralParty.SelfTest.dll"), selfTest }, environment);
            }
            DevContext.RequireInventory(sourcePaths, Sources(context.Root));
            if (context.Hash(context.NativeAssemblyPath) != nativeHash) throw new IOException("Native metadata input changed; build approval refused.");
            if (!String.Equals(before, DevContext.PersistentPaths(), StringComparison.Ordinal)) throw new InvalidOperationException("Persistent PATH changed; build approval refused.");
            var files = new[] { "plugin/BetterAstralParty.dll", "setup/BetterAstralParty-Setup.exe" }
                .Select(relative => new { path = relative, bytes = new FileInfo(Path.Combine(output, relative)).Length, sha256 = context.Hash(Path.Combine(output, relative)) }).ToArray();
            var productVersion = PackagingImage.PluginVersion(File.ReadAllBytes(Path.Combine(output, "plugin", "BetterAstralParty.dll")));
            BuildVersion.Validate(pluginSource, System.Text.Json.JsonSerializer.SerializeToElement(new { versionOverride, generatedPlugin }), productVersion,
                generatedPlugin == null ? null : File.ReadAllBytes(Path.Combine(output, BuildVersion.GeneratedPath)));
            BuildVersion.ValidateNative(File.ReadAllBytes(Path.Combine(output, "setup", "BetterAstralParty-Setup.exe")), SetupVersion(context.Root));
            context.WriteJsonCreateNew(Path.Combine(output, "build-result.json"), new {
                schema = 1, configuration, sourceManifestSha256 = context.Hash(Path.Combine(output, "build-source.json")), checks,
                validationScope = options.ContainsKey("checks") ? "targeted" : "full", selfTests, versionOverride, generatedPlugin,
                files, compatibility = new { path = compatibilityRelative, bytes = compatibilityLease.Length, sha256 = compatibilityHash }, nativeInput,
                installationPerformed = false, releaseCreated = false
            });
            Console.WriteLine("Development build and C# checks completed: " + output);
            return 0;
        } catch (Exception error) { failure = error; throw; }
        finally {
            foreach (var lease in leases) lease.Dispose();
            try {
                if (!String.Equals(before, DevContext.PersistentPaths(), StringComparison.Ordinal))
                    throw new InvalidOperationException("Persistent PATH changed. Stop and inspect it; the build did not overwrite environment values.");
            } catch (Exception error) {
                if (failure == null) throw;
                Console.Error.WriteLine("Build PATH verification also failed: " + error.Message);
            }
        }
    }

    internal static string[] SelectChecks(string? selection)
    {
        if (selection == null) return Checks;
        var names = selection.Split(',');
        if (names.Length == 0 || names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(name => !Checks.Contains(name, StringComparer.Ordinal)))
            throw new ArgumentException("Unique known comma-separated build checks required.");
        return Checks.Where(name => name == "sdk-path-safety" || names.Contains(name, StringComparer.Ordinal)).ToArray();
    }

    private static Dictionary<string, string?> Options(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++) {
            var name = args[index].TrimStart('-');
            if (name.Equals("Release", StringComparison.OrdinalIgnoreCase)) name = "release";
            else if (name.Equals("OutputDirectory", StringComparison.OrdinalIgnoreCase)) name = "output-directory";
            else if (name.Equals("GameRoot", StringComparison.OrdinalIgnoreCase)) name = "game-root";
            else if (name.Equals("BepInExRoot", StringComparison.OrdinalIgnoreCase)) name = "bepinex-root";
            else if (name.Equals("CompatibilityPath", StringComparison.OrdinalIgnoreCase)) name = "compatibility-path";
            else if (name.Equals("NativeAssembly", StringComparison.OrdinalIgnoreCase)) name = "native-assembly";
            if (name != "release" && name != "output-directory" && name != "game-root" && name != "bepinex-root" && name != "compatibility-path" && name != "native-assembly" && name != "checks" && name != "version") throw new ArgumentException("Unknown build option: " + args[index]);
            string? value = null;
            if (name != "release") { if (++index >= args.Length) throw new ArgumentException("Missing build option value: " + name); value = args[index]; }
            if (!result.TryAdd(name, value)) throw new ArgumentException("Duplicate build option: " + name);
        }
        return result;
    }

    internal static int RunNative(DevContext context, string role, string[] args)
    {
        var options = Options(args);
        if (role != "Setup") throw new ArgumentException("Unknown native build role.");
        if (!options.TryGetValue("output-directory", out var path) || options.Keys.Any(name => name is not ("output-directory" or "version"))) throw new ArgumentException("Isolated native compilation requires --output-directory and optional --version.");
        var versionOverride = options.TryGetValue("version", out var version) ? BuildVersion.Canonical(version!) : null;
        var output = context.FreshArtifactOutput(path!);
        CompileNative(context, role, output, versionOverride);
        Console.WriteLine("Native " + role + " compiled without build-result approval: " + output);
        return 0;
    }

    // Codex (AGENTS.md) and Claude Code (CLAUDE.md, CLAUDE.local.md) instructions stay development-only.
    internal static bool IsAgentInstructions(string relative) =>
        Path.GetFileName(relative) is var name && (name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase) || name.Equals("CLAUDE.local.md", StringComparison.OrdinalIgnoreCase));

    internal static bool IsPublicSourcePath(string relative)
    {
        return !relative.Split('/').Any(part => part.Equals("review-candidates", StringComparison.OrdinalIgnoreCase))
            && !IsAgentInstructions(relative)
            && !relative.Equals("tools/RecoverGaLocal20261008.cs", StringComparison.OrdinalIgnoreCase)
            && !relative.Equals("tools/SignGaRelease.cs", StringComparison.OrdinalIgnoreCase)
            && !System.Text.RegularExpressions.Regex.IsMatch(relative, @"\Atools/SignRc[0-9][^/]*\.cs\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    internal static IEnumerable<string> Sources(string root)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        IEnumerable<string> Files(string directory) {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked build source directory rejected.");
            foreach (var file in Directory.EnumerateFiles(directory)) {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked build source file rejected.");
                yield return file;
            }
            foreach (var child in Directory.EnumerateDirectories(directory)) {
                if (new[] { "bin", "obj", "review-candidates" }.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
                foreach (var file in Files(child)) yield return file;
            }
        }
        foreach (var folder in new[] { "src", "updater", "diagnostics", "developer-tools", "tools", "focus-tests", "diagnostics-tests", "updater-tests", "updater-safety-tests", "self-test", "docs", "vendor" }) {
            var directory = Path.Combine(root, folder);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Files(directory)) {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!IsPublicSourcePath(relative)) continue;
                if (new[] { ".cs", ".csproj", ".config", ".props", ".json", ".cmd", ".md", ".txt", ".xml", ".yml" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    || Path.GetFileName(file).Equals("LICENSE", StringComparison.OrdinalIgnoreCase)) paths.Add(relative);
            }
        }
        foreach (var name in new[] { "NuGet.Config", "Directory.Build.props", "global.json", "Install.cmd", "Uninstall.cmd", "Force-Uninstall.cmd", "Create-Diagnostics.cmd", "Open-Logs.cmd", "Developer.cmd", "README.md", "CHANGELOG.md", "LICENSE", "THIRD-PARTY-NOTICES/SOURCES.md", "THIRD-PARTY-NOTICES/component-matrix.json" })
            if (File.Exists(Path.Combine(root, name))) paths.Add(name);
        return paths;
    }

    private static void Step(DevContext context, string executable, IEnumerable<string> args, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (context.Run(executable, args, env: environment) != 0) throw new InvalidOperationException("Build process failed: " + Path.GetFileName(executable));
    }

    private static void Check(DevContext context, string name, string output, string game, string bepinex)
    {
        var args = new List<string> { name, "--output-directory", Path.Combine(output, "checks", name), "--game-root", game, "--bepinex-root", bepinex };
        if (context.NativeAssemblyExplicit) args.AddRange(new[] { "--native-assembly", context.NativeAssemblyPath });
        if (name == "native-compatibility") args.AddRange(new[] { "--compatibility-path", Path.Combine(output, "compatibility", "BetterAstralParty.compatibility.json") });
        if (File.Exists(Path.Combine(output, "plugin", "BetterAstralParty.dll"))) args.AddRange(new[] { "--plugin-path", Path.Combine(output, "plugin", "BetterAstralParty.dll") });
        if (File.Exists(Path.Combine(output, "setup", "BetterAstralParty-Setup.exe"))) args.AddRange(new[] { "--inspector-path", Path.Combine(output, "setup", "BetterAstralParty-Setup.exe") });
        if (TestCommands.Run(context, args.ToArray()) != 0) throw new InvalidOperationException("Build check failed: " + name);
    }

    private static void BuildProject(DevContext context, string project, string destination, string intermediate, string configuration,
        IReadOnlyDictionary<string, string?> environment, IEnumerable<string> extra)
    {
        var properties = new[] { "-p:BaseIntermediateOutputPath=" + intermediate + Path.DirectorySeparatorChar,
            "-p:MSBuildProjectExtensionsPath=" + intermediate + Path.DirectorySeparatorChar, "-p:UseSharedCompilation=false", "-p:NuGetAudit=false" }.Concat(extra).ToArray();
        var dotnet = Path.Combine(context.DotnetRoot, "dotnet.exe");
        Step(context, dotnet, new[] { "restore", project, "--configfile", Path.Combine(context.Root, "NuGet.Config") }.Concat(properties), environment);
        Step(context, dotnet, new[] { "build", project, "-c", configuration, "--no-restore", "-o", destination }.Concat(properties), environment);
    }

    private static void CompileNative(DevContext context, string role, string output, string? versionOverride = null)
    {
        if (role != "Setup") throw new ArgumentException("Only the setup host is a native build role.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Fresh native build output is required.");
        Directory.CreateDirectory(output);
        var compiler = Path.Combine(context.DotnetRoot, "sdk", "8.0.425", "Roslyn", "bincore", "csc.dll");
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
        var arguments = new List<string> { compiler, "/nologo", "/noconfig", "/utf8output", "/nostdlib+", "/langversion:latest", "/nullable:enable", "/define:NETFRAMEWORK", "/warnaserror+",
            "/deterministic+", "/pathmap:" + context.Root + "=/src/BetterAstralParty", "/platform:x64", "/target:winexe",
            "/main:SetupHost", "/out:" + Path.Combine(output, "BetterAstralParty-Setup.exe") };
        // No System.Net.Http or WinForms: the setup host has no network or UI-framework code.
        foreach (var name in new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Security.dll", "System.Web.Extensions.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll" }) {
            var reference = Path.Combine(framework, name); if (!File.Exists(reference)) throw new FileNotFoundException("Framework reference missing.", reference);
            arguments.Add("/reference:" + reference);
        }
        arguments.Add(Identity(context.Root, output, role, versionOverride));
        arguments.Add(Path.Combine(context.Root, "tools", "SetupHost.cs"));
        arguments.Add(Path.Combine(context.Root, "tools", "GameCompatibility.cs"));
        arguments.Add(Path.Combine(context.Root, "tools", "MaintenanceJsonReader.cs"));
        arguments.AddRange(Directory.EnumerateFiles(Path.Combine(context.Root, "tools"), "Native*.cs").OrderBy(path => path, StringComparer.Ordinal));
        var usings = Path.Combine(output, "SetupGlobalUsings.cs");
        File.WriteAllText(usings, "global using System;\nglobal using System.IO;\nglobal using System.Collections.Generic;\nglobal using System.Threading;\nglobal using System.Threading.Tasks;\n", new UTF8Encoding(false));
        arguments.Add(usings);
        var collector = Path.Combine(output, "SetupDiagnosticBundle.cs");
        File.WriteAllText(collector, "#nullable disable\n" + File.ReadAllText(Path.Combine(context.Root, "diagnostics", "DiagnosticBundle.cs")), new UTF8Encoding(false));
        arguments.Add(collector);
        arguments.AddRange(Directory.EnumerateFiles(Path.Combine(context.Root, "updater"), "*.cs")
            .Where(path => Path.GetFileName(path) != "PublicReleaseEligibility.cs").OrderBy(path => path, StringComparer.Ordinal));
        arguments.AddRange(Directory.EnumerateFiles(Path.Combine(context.Root, "src", "Observability"), "*.cs").OrderBy(path => path, StringComparer.Ordinal));
        arguments.Add(Path.Combine(context.Root, "diagnostics", "MinimalDiagnosticProjection.cs"));
        Step(context, Path.Combine(context.DotnetRoot, "dotnet.exe"), arguments);
    }

    // shortcut: nothing fails if setup bytes change while SetupVersion stays the same; add a pinned-hash check if that drift bites.
    internal static string SetupVersion(string root)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(root, "tools", "SetupHost.cs")), "(?m)^    public const string SetupVersion = \"([^\"\\r\\n]+)\";\\r?$");
        if (matches.Count != 1) throw new InvalidDataException("Exactly one setup component version is required.");
        return BuildVersion.Canonical(matches[0].Groups[1].Value);
    }

    internal static string Identity(string root, string output, string role, string? versionOverride = null)
    {
        var tag = role == "Setup" ? SetupVersion(root)
            : versionOverride == null ? BuildVersion.SourceVersion(File.ReadAllBytes(Path.Combine(root, "src", "Plugin.cs"))) : BuildVersion.Canonical(versionOverride);
        var version = BetterAstralParty.Updating.UpdateVersion.Parse(tag);
        var fileVersion = version.Major + "." + version.Minor + "." + version.Patch + ".0";
        var text = "using System.Reflection;\n[assembly: System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.8\")]\n[assembly: AssemblyProduct(\"BetterAstralParty\")]\n"
            + "[assembly: AssemblyTitle(\"" + "BetterAstralParty setup host" + "\")]\n"
            + "[assembly: AssemblyVersion(\"" + fileVersion + "\")]\n[assembly: AssemblyFileVersion(\"" + fileVersion + "\")]\n[assembly: AssemblyInformationalVersion(\"" + tag + "\")]\n";
        var path = Path.Combine(output, "ExecutableIdentity.cs"); File.WriteAllText(path, text, new UTF8Encoding(false)); return path;
    }
}
