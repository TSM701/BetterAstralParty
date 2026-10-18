using System.Text;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static string UpdateGenerated(string directory, string name, string source)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = new UTF8Encoding(false).GetBytes(source); stream.Write(bytes);
        return path;
    }

    private static string UpdateFrameworkCompile(DevContext context, string stage, string name, IEnumerable<string> sources,
        Action<string> pin, string? entry = null, string defines = "NETFRAMEWORK,BAP_SAFETY_FIXTURE,BAP_LAUNCHER_RECOVERY_FIXTURE", string? helperIdentity = null)
    {
        var output = Path.Combine(stage, name); Directory.CreateDirectory(stage);
        Check(!File.Exists(output), "Fresh framework fixture output: " + name);
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
        var flags = new List<string> { Compiler(context), "/nologo", "/noconfig", "/utf8output", "/nostdlib+", "/langversion:latest", "/nullable:enable", "/warnaserror+",
            "/deterministic+", "/platform:x64", "/target:" + (entry == null ? "library" : "exe"), "/out:" + output,
            "/pathmap:" + context.Root + "=/src/BetterAstralParty" };
        if (defines.Length != 0) flags.Add("/define:" + defines);
        if (entry != null) flags.Add("/main:" + entry);
        foreach (var reference in new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Security.dll", "System.Windows.Forms.dll", "System.Drawing.dll", "System.Web.Extensions.dll", "System.Net.Http.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll" }) {
            var path = Path.Combine(framework, reference); pin(path); flags.Add("/reference:" + path);
        }
        var inputs = sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (inputs.Any(path => Path.GetFileName(path) == "PublicReleaseEligibility.cs") && !inputs.Any(path => Path.GetFileName(path) == "MaintenanceJsonReader.cs"))
            inputs.Add(Path.Combine(context.Root, "tools/MaintenanceJsonReader.cs"));
        if (entry == "BetterAstralParty.Updating.UpdateHelperProgram") {
            var identity = helperIdentity == null ? UpdateGenerated(stage, name + ".framework.cs",
                "[assembly: System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.8\")]\n") : Path.GetFullPath(helperIdentity);
            Check(!inputs.Contains(identity, StringComparer.OrdinalIgnoreCase), "Helper metadata has one explicit source owner: " + name);
            inputs.Add(identity);
        } else Check(helperIdentity == null, "Helper identity cannot be supplied to another fixture entry point");
        foreach (var source in inputs) { var path = Path.GetFullPath(source); pin(path); flags.Add(path); }
        Ensure(context.Run(Dotnet(context), flags, env: EnvironmentFor(stage), timeoutMilliseconds: 60000,
            logPath: Path.Combine(stage, name + ".compile.log")), "Framework fixture compilation: " + name);
        if (entry == "BetterAstralParty.Updating.UpdateHelperProgram") {
            using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
            var attributes = assembly.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute").ToArray();
            Check(attributes.Length == 1 && attributes[0].ConstructorArguments.Count == 1
                && attributes[0].ConstructorArguments[0].Value is string target && target == ".NETFramework,Version=v4.8",
                "Compiled helper declares exactly one Framework 4.8 runtime contract: " + name);
        }
        return output;
    }

    private static string[] UpdateFixtureSources(DevContext context, bool program, bool productionKeys) =>
        Directory.EnumerateFiles(Path.Combine(context.Root, "updater"), "*.cs")
            .Where(path => (program || Path.GetFileName(path) != "UpdateHelperProgram.cs") && (productionKeys || Path.GetFileName(path) != "ProductionUpdateKeys.cs"))
            .Concat(Directory.EnumerateFiles(Path.Combine(context.Root, "src/Observability"), "*.cs"))
            .Append(Path.Combine(context.Root, "diagnostics/MinimalDiagnosticProjection.cs")).OrderBy(path => path, StringComparer.Ordinal).ToArray();
}
