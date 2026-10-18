using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void NativeDiagnostics(DevContext context, string stage, Action<string> pin, bool failureContext = false)
    {
        Directory.CreateDirectory(stage);
        var path = Path.Combine(context.Root, "tools/NativeMaintenanceEntry.cs"); pin(path);
        var entry = File.ReadAllText(path);
        var start = entry.IndexOf("        internal static string? Failure(Exception error)", StringComparison.Ordinal);
        var save = entry.IndexOf("        private static string? SaveLog(string output)", StringComparison.Ordinal);
        if (start < 0 || save < start) throw new InvalidDataException("Exact native failure producer boundaries required.");
        var body = entry[start..];
        var derived = body.Replace("Environment.GetFolderPath(", "FixtureFolders.GetFolderPath(", StringComparison.Ordinal)
            .Replace("Path.GetTempPath()", "FixtureFolders.GetTempPath()", StringComparison.Ordinal);
        Check(derived.Replace("FixtureFolders.GetFolderPath(", "Environment.GetFolderPath(", StringComparison.Ordinal)
            .Replace("FixtureFolders.GetTempPath()", "Path.GetTempPath()", StringComparison.Ordinal) == body, "Folder broker is the only producer substitution");
        var prefix = "#nullable enable\nusing System; using System.Collections.Generic; using System.Globalization; using System.IO; using System.Text; using System.Text.RegularExpressions; using BetterAstralParty.Updating;\nnamespace BetterAstralParty.Installation { internal static class NativeMaintenanceEntry {\nprivate static string _run=\"\", _version=\"unknown\", _package=\"\", _game=\"\", _operation=\"Maintenance\";\nprivate static bool _preview=false;\nprivate static readonly List<string> Observations=new List<string>();\n";
        var generated = Path.Combine(stage, "NativeMaintenanceFailure.derived.cs"); File.WriteAllText(generated, prefix + derived, new UTF8Encoding(false));
        var launcherPath = Path.Combine(context.Root, "tools/SetupHost.cs"); pin(launcherPath);
        var mapper = Regex.Matches(File.ReadAllText(launcherPath), @"(?ms)^    public static void RecordInstallerFailure\(string phase,Exception error\) \{.*?^    \}");
        Check(mapper.Count == 1, "Exactly one literal installer failure mapper");
        var trustPath = Path.Combine(context.Root, "updater/UpdateTrust.cs"); pin(trustPath); var trust = File.ReadAllText(trustPath);
        var typeStart = trust.IndexOf("    internal enum UpdateFailure {", StringComparison.Ordinal);
        var typeEnd = typeStart < 0 ? -1 : trust.IndexOf("    internal sealed class UpdateContext", typeStart, StringComparison.Ordinal);
        Check(typeStart >= 0 && typeEnd > typeStart, "Literal update failure enum and exception boundaries");
        var mapperSource = Path.Combine(stage, "SetupHostFailure.derived.cs");
        File.WriteAllText(mapperSource, "using System; using BetterAstralParty.Updating;\nnamespace BetterAstralParty.Updating {\n" + trust[typeStart..typeEnd] + "}\ninternal static class SetupHost {\n" + mapper[0].Value + "\n}\n", new UTF8Encoding(false));
        var fixtureRoot = Path.Combine(stage, "native-maintenance-diagnostics-fixture");
        Framework(context, stage, "NativeMaintenanceDiagnosticsFixture", new[] {
            "tools/NativeSteamMaintenance.cs", "updater/WindowsPath.cs", "updater/WindowsFileFence.cs", "updater/WindowsPrivateSecurity.cs", "updater/UpdateWorkPath.cs",
            "focus-tests/native-steam/FixtureGuard.cs", "diagnostics/DiagnosticBundle.cs", "diagnostics/MinimalDiagnosticProjection.cs",
            "focus-tests/native-maintenance-diagnostics/FixtureEntry.cs", "focus-tests/native-maintenance-diagnostics/FailureContextCases.cs", generated, mapperSource
        }.Concat(Directory.EnumerateFiles(Path.Combine(context.Root, "src/Observability"), "*.cs").OrderBy(value => value, StringComparer.Ordinal)).ToArray(), failureContext ? new[] { fixtureRoot, Path.Combine(context.Root, "artifacts"), "failure-context" } : new[] { fixtureRoot, Path.Combine(context.Root, "artifacts") }, pin);
        context.WriteJsonCreateNew(Path.Combine(stage, "producer-source-binding.json"), new {
            literalSourceSha256 = context.Hash(path), derivedSourceSha256 = context.Hash(generated), folderBrokerOnly = true, collectorUnmodified = true,
            mapperLiteralSourceSha256 = context.Hash(launcherPath), mapperDerivedSourceSha256 = context.Hash(mapperSource), exceptionTypesLiteralSourceSha256 = context.Hash(trustPath), mapperVerbatim = true, observabilityUnmodified = true
        });
    }

    private static void CardScan(DevContext context, string stage, Action<string> pin)
    {
        var source = File.ReadAllText(Path.Combine(context.Root, "src/CardUi.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var changes = new[] {
            ("internal static class CardUi", "internal static class ReferenceCardUi"),
            ("            var values = length is >= 2 and <= 3 ? new int[length] : Array.Empty<int>();\n            for (var index = 0; index < values.Length; index++)\n                values[index] = parameters.Call(\"get_Item\", index)!.Value<int>();",
             "            var values = length is >= 2 and <= 3 ? Enumerable.Range(0, length)\n                .Select(index => parameters.Call(\"get_Item\", index)!.Value<int>()).ToArray() : Array.Empty<int>();"),
            ("        var hovered = default((RuntimeObject Card, RuntimeObject Face, CardBonus? Bonus, bool Usable));\n        foreach (var candidate in candidates)\n            if (candidate.Card.Pointer == hit?.Pointer) { hovered = candidate; break; }",
             "        var hovered = candidates.FirstOrDefault(item => item.Card.Pointer == hit?.Pointer);"),
            ("            var cheaper = false;\n            if (item.Bonus is { } current)\n                foreach (var other in candidates)\n                    if (other.Usable && other.Bonus is { } otherBonus && CardAdvisor.Dominates(otherBonus, current))\n                    { cheaper = true; break; }",
             "            var cheaper = item.Bonus is { } current && candidates.Any(other => other.Usable\n                && other.Bonus is { } otherBonus && CardAdvisor.Dominates(otherBonus, current));")
        };
        foreach (var (from, to) in changes) {
            Check(Regex.Matches(source, Regex.Escape(from)).Count == 1, "Exact card performance reference replacement"); source = source.Replace(from, to, StringComparison.Ordinal);
        }
        var reference = Path.Combine(stage, "ReferenceCardUi.cs"); File.WriteAllText(reference, source, new UTF8Encoding(false)); pin(reference);
        var assembly = BuildProject(context, "focus-tests/card-scan-performance/CardScanFixture.csproj", stage, pin, new[] { "-p:ReferencePath=" + reference });
        Ensure(context.Run(Dotnet(context), new[] { assembly }, env: EnvironmentFor(stage)), "Card scan performance fixture");
    }

    private static void SdkPathSafety(DevContext context, string stage, Action<string> pin)
    {
        DevContext.RequireInventory(new[] { "a.cs", "b.cs" }, new[] { "a.cs", "b.cs" });
        Check(true, "Unchanged source inventory accepted");
        foreach (var changed in new[] { new[] { "a.cs" }, new[] { "a.cs", "b.cs", "new.cs" }, new[] { "b.cs", "a.cs" }, new[] { "a.cs", "replaced.cs" } }) {
            var rejected = false;
            try { DevContext.RequireInventory(new[] { "a.cs", "b.cs" }, changed); } catch (IOException) { rejected = true; }
            Check(rejected, "Missing, added, reordered or replaced source blocks approval");
        }
        var firstUse = Path.Combine(stage, "first-use"); Directory.CreateDirectory(firstUse);
        SdkFirstUse(context, firstUse, pin);
        var original = Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", EnvironmentVariableTarget.Process);
        var processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
        var persisted = DevContext.PersistentPaths();
        var checks = 0;
        try {
            foreach (var initial in new string?[] { null, "true", "false", "caller-value" }) {
                Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", initial, EnvironmentVariableTarget.Process);
                foreach (var fail in new[] { false, true }) {
                    var observedFailure = false;
                    try { context.GuardSdkEnvironment(() => { Check(Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH") == "false", "SDK guard set before callback"); checks++; if (fail) throw new FixtureFailure(); }); }
                    catch (FixtureFailure) { observedFailure = true; }
                    Check(observedFailure == fail && Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH") == initial, "Caller SDK setting restored on success/failure"); checks++;
                }
            }
            foreach (var executable in new[] { "powershell.exe", "pwsh.exe" }) {
                var rejected = false; try { context.Run(executable, Array.Empty<string>()); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Interpreter rejected before starting a child"); checks++;
            }
            Check(DevContext.PersistentPaths() == persisted && Environment.GetEnvironmentVariable("PATH") == processPath, "Persistent and process PATH preserved"); checks++;
            context.WriteJsonCreateNew(Path.Combine(stage, "sdk-path-safety.json"), new { passed = true, checks, realSdkFirstUseExecuted = true, persistentPathChanged = false });
        } finally { Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", original, EnvironmentVariableTarget.Process); }
    }
    private sealed class FixtureFailure : Exception { }
}
