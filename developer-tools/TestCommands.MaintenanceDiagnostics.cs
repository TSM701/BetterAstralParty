using System.Text;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void LaunchFailureDiagnostics(DevContext context,string stage,Action<string> pin)
    {
        Directory.CreateDirectory(stage);
        var path=Path.Combine(context.Root,"tools/SteamLauncher.cs");pin(path);var source=File.ReadAllText(path);var start=source.IndexOf("    internal static string LaunchFailure(",StringComparison.Ordinal);if(start<0)throw new InvalidDataException("Exact native launch diagnostic boundary required.");var methods=source[start..];
        var scoped=methods.Replace("Environment.GetFolderPath(","LaunchFixtureFolders.GetFolderPath(",StringComparison.Ordinal).Replace("Path.GetTempPath()","LaunchFixtureFolders.GetTempPath()",StringComparison.Ordinal);
        Check(scoped.Replace("LaunchFixtureFolders.GetFolderPath(","Environment.GetFolderPath(",StringComparison.Ordinal).Replace("LaunchFixtureFolders.GetTempPath()","Path.GetTempPath()",StringComparison.Ordinal)==methods,"native launch failure substitution changes only fixture folder broker");
        var derived=Path.Combine(stage,"SteamLaunchDiagnostics.derived.cs");File.WriteAllText(derived,"using System;using System.IO;using BetterAstralParty.Updating;internal static class SteamLauncher {\n"+scoped,new UTF8Encoding(false));
        var collectorPath=Path.Combine(context.Root,"diagnostics/DiagnosticBundle.cs");pin(collectorPath);var collector=Path.Combine(stage,"DiagnosticBundle.derived.cs");File.WriteAllText(collector,"#nullable disable\n"+File.ReadAllText(collectorPath),new UTF8Encoding(false));
        Framework(context,Path.Combine(stage,"compiled"),"NativeLaunchFailureFixture",new[]{derived,collector,"diagnostics/MinimalDiagnosticProjection.cs","focus-tests/native-launch-failure/FixtureEntry.cs"}.Concat(UpdateSources(context)).ToArray(),[Path.Combine(stage,"native-launch-failure-fixture")],pin);
        context.WriteJsonCreateNew(Path.Combine(stage,"launch-failure-port.json"),new{passed=true,literalSourceSha256=context.Hash(path),derivedSourceSha256=context.Hash(derived),actualNativeProducerToZip=true,retired=new[]{"Legacy PowerShell entry catch/interpreter comparisons; native C# route is the supported path"}});
    }
}
