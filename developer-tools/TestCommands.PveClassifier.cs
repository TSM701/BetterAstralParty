using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void PveClassifier(DevContext context, string stage, Action<string> pin)
    {
        // IsPve keeps each mode's answer for five seconds (2026-11-17); the classification itself is ReadPve, tested here as IsPve.
        var runtimeSource = ReadSource(context, "src/RuntimeObject.cs");
        Check(MethodFragment(runtimeSource, "internal static bool IsPve").Contains("var pve = ReadPve(mode);", StringComparison.Ordinal), "IsPve remembers ReadPve's answer per mode");
        var runtime = MethodFragment(runtimeSource, "private static bool ReadPve").Replace("private static bool ReadPve", "internal static bool IsPve", StringComparison.Ordinal);
        var visible = ReadSource(context, "src/VisibleCombat.cs");
        var bridge = Derive(visible, @"(?m)^    internal static Func<int, bool>\? PveClassifier \{ get; set; \}\r?\n    public static bool IsPve\(int mapType\) => [^\r\n]+;", "Literal production PvE classifier bridge");
        var load = MethodFragment(ReadSource(context, "src/Plugin.cs"), "private void LoadCore");
        const string binding = "        VisibleCombatAdvisor.PveClassifier = RuntimeObject.IsPve;";
        Check(Regex.IsMatch(load, @"\A    private void LoadCore\(\)\s*\{\s*" + Regex.Escape(binding.Trim())), "Native classifier is bound before bootstrap gates, configuration or any feature use");
        var fixture = Template(context, "focus-tests/pve-classifier/Fixture.template.cs", pin)
            .Replace("__RUNTIME_CLASSIFIER__", runtime, StringComparison.Ordinal)
            .Replace("__PURE_BRIDGE__", bridge, StringComparison.Ordinal)
            .Replace("__BINDING__", binding, StringComparison.Ordinal);
        RunEmbedded(context, stage, "pve-classifier", fixture, "PveClassifierFixture.Checks.Run()", pin, warnAsError: true);
    }
}
