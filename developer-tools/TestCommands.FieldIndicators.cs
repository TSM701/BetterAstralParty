using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void FieldInput(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context); ExactField(native, "FairyGUI.UIConfig", "clickDragSensitivity", "System.Int32", true, true);
        var production = WithoutNamespace(ReadSource(context, "src/FieldBuffUi.cs")).Replace("using UnityEngine;", "", StringComparison.Ordinal);
        RunEmbedded(context, stage, "field-indicator-input", Template(context, "focus-tests/field-indicator/RuntimeFixture.cs", pin).Replace("__PRODUCTION__", production, StringComparison.Ordinal), "FieldIndicatorInputTest.Check.Run()", pin, true);
    }
    private static void FieldOpacity(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        foreach (var (type, name, result) in new[] { ("FairyGUI.GObject", "get_alpha", "System.Single"), ("FairyGUI.GObject", "get_isDisposed", "System.Boolean"), ("FairyGUI.GObject", "get_parent", "FairyGUI.GComponent"), ("FairyGUI.GObject", "get_displayObject", "FairyGUI.DisplayObject"),
            ("FairyGUI.DisplayObject", "get_visible", "System.Boolean"), ("FairyGUI.DisplayObject", "get_isDisposed", "System.Boolean"), ("GameLogic.GameLogicManager", "get_battle", "GameLogic.BattleLogic") }) ExactMethod(native, type, name, result);
        ExactMethod(native, "FairyGUI.GObject", "set_alpha", "System.Void", "System.Single"); ExactMethod(native, "FairyGUI.DisplayObject", "set_visible", "System.Void", "System.Boolean");
        var visible = native.MainModule.GetType("FairyGUI.GObject").Methods.Where(method => method.Name == "get_internalVisible2" && method.IsAssembly && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType.FullName == "System.Boolean").ToArray();
        Check(visible.Length == 1, "Internal group-aware visibility ABI"); Pattern(Body(visible[0]), @"::get_group[\s\S]*::get_internalVisible2", "Group-aware internal visibility propagation");
        foreach (var (type, name, pattern) in new[] { ("FairyGUI.GObject", "HandleAlphaChanged", @"::get_displayObject[\s\S]*::set_alpha"), ("FairyGUI.GObject", "HandleVisibleChanged", @"::get_internalVisible2[\s\S]*::set_visible"),
            ("FairyGUI.Container", "Update", @"::get_alpha[\s\S]*mul[\s\S]*UpdateContext::alpha"), ("FairyGUI.DisplayObject", "Update", @"UpdateContext::alpha[\s\S]*DisplayObject::_alpha[\s\S]*mul[\s\S]*NGraphics::Update"),
            ("FairyGUI.DisplayObject", "InternalHitTest", @"DisplayObject::_visible[\s\S]*brfalse[\s\S]*::HitTest"), ("FairyGUI.Container", "Update", @"::get_visible[\s\S]*brfalse[\s\S]*::Update") }) {
            var methods = native.MainModule.GetType(type).Methods.Where(method => method.Name == name && method.Parameters.Count <= 1).ToArray(); Check(methods.Length == 1, "Presentation IL method: " + type + "." + name); Pattern(Body(methods[0]), pattern, "Native alpha/visibility/hit propagation: " + name);
        }
        Check(native.MainModule.GetType("GameLogic.GameLogicManager").BaseType.FullName == "Tools.SimpleSingletonProvider`1<GameLogic.GameLogicManager>" && native.MainModule.GetType("Tools.SimpleSingletonProvider`1").Fields.Count(field => field.Name == "_inst" && field.IsStatic) == 1, "Native game singleton storage");
        ExactField(native, "GameLogic.BattleLogic", "battleInfo", "UI.BattleInfoPanel");
        var ui = native.MainModule.GetType("UI.BasePanel`1").Methods.Where(method => method.Name == "get_ui" && !method.IsStatic && method.IsFamily && method.Parameters.Count == 0 && method.ReturnType.FullName == "T").ToArray();
        Check(native.MainModule.GetType("UI.BattleInfoPanel").BaseType.FullName == "UI.BasePanel`1<UI.UIBattleInfoPanel>" && ui.Length == 1, "Inherited battle UI getter");
        foreach (var name in new[] { "com_AttrInfos", "com_PlayerAttrInfos" }) ExactField(native, "UI.UIBattleInfoPanel", name, "FairyGUI.GComponent");
        var source = WithoutNamespace(ReadSource(context, "src/FieldIndicatorOpacity.cs")); var template = Template(context, "focus-tests/csharp-migration/field-indicator-opacity.template.cs", pin);
        RunEmbedded(context, stage, "field-indicator-opacity", template.Replace("__SOURCE__", source, StringComparison.Ordinal), "BetterAstralParty.OpacityCheck.Run()", pin, true);
        foreach (var (name, before, after) in new[] { ("AlphaGuardMutation", "(alpha == _applied || alpha == _attemptedAlpha)", "true"), ("VisibilityRestoreMutation", "_node.Get<bool>(\"internalVisible2\")", "true"),
            ("HideMutation", "display.Set(\"visible\", false)", "display.Set(\"visible\", true)"), ("PrematureAppliedMutation", "var applied = _original * factor;", "var applied = _original * factor; _applied = applied;") }) {
            var mutated = source.Replace(before, after, StringComparison.Ordinal); Check(mutated != source, "Opacity mutation bound: " + name);
            RunEmbedded(context, stage, name, template.Replace("__SOURCE__", mutated, StringComparison.Ordinal).Replace("namespace BetterAstralParty {", "namespace " + name + " {", StringComparison.Ordinal), name + ".OpacityCheck.Run()", pin, true, expectedFailure: "Opacity assertion");
        }
    }
    private static void FieldCounter(DevContext context, string stage, Action<string> pin)
    {
        var runtime = context.NativeAssemblyPath; pin(runtime);
        if (!context.NativeAssemblyExplicit) Check(context.Hash(runtime) == "DDEFD944846C612CC8935BCEF33170BAA8F3EE3974CBF2EC0F8F001678CED341", "Exact audited historical native runtime SHA-256");
        using var native = Native(context);
        ExactField(native, "Core.Unit.BattleProperty", "Counter", "Tools.ReadOnlyReactiveProperty`1<System.Boolean>");
        ExactMethod(native, "Tools.ReadOnlyReactiveProperty`1", "get_Value", "T"); ExactMethod(native, "GameLogic.BattlePlayerData", "get_Property", "Core.Unit.BattleProperty");
        foreach (var type in new[] { "UI.UICom_AttrInfo", "UI.UICom_PlayerAttrInfo" }) {
            var calls = Calls(ExactMethod(native, type, "get_PlayerData", "GameLogic.BattlePlayerData")); Check(calls.Contains("GameLogic.BattlePlayerData GameLogic.BattleLogic::GetPlayerDataById(System.Int64)", StringComparer.Ordinal) && !calls.Any(call => Regex.IsMatch(call, "Request|Send|RPC|TryShow")), "Counter owner reads only local cache: " + type);
        }
        foreach (var (type, name, result) in new[] { ("UI.UICom_AttrInfo", "txt_Name", "FairyGUI.GTextField"), ("UI.UICom_AttrInfo", "graph_Guide", "FairyGUI.GGraph"), ("UI.UICom_PlayerAttrInfo", "loader_State", "FairyGUI.GLoader") }) ExactField(native, type, name, result);
        foreach (var name in new[] { "get_xMin", "get_yMin", "get_width", "get_height" }) ExactMethod(native, "FairyGUI.GObject", name, "System.Single");
        foreach (var name in new[] { "get_isDisposed", "get_onStage" }) ExactMethod(native, "FairyGUI.GObject", name, "System.Boolean");
        ExactMethod(native, "FairyGUI.GComponent", "get_numChildren", "System.Int32");
        ExactMethod(native, "FairyGUI.GComponent", "GetChildAt", "FairyGUI.GObject", "System.Int32");
        foreach (var name in new[] { "com_AttrInfos", "com_PlayerAttrInfos" }) {
            var parents = native.MainModule.GetTypes().SelectMany(type => type.Fields).Where(field => field.Name == name).ToArray(); Check(parents.Length == 1 && parents[0].IsPublic && parents[0].FieldType.FullName == "FairyGUI.GComponent", "Exact live container: " + name);
        }
        Check(Calls(NativeMethod(native, "Core.Unit.BattleProperty", ".ctor")).Contains("System.Boolean party.model.Hero::get_CanCounter()", StringComparer.Ordinal)
            && Calls(NativeMethod(native, "Core.Unit.BattleProperty", "OnCanCounterChanged")).Contains("System.Boolean party.protocol.HeroCounterChangeS2C::get_CanCounter()", StringComparer.Ordinal), "Counter is native synced constructor/change data");
        var source = ReadSource(context, "src/FieldBuffUi.cs"); var start = source.IndexOf("private static void RefreshCounters", StringComparison.Ordinal); var end = source.IndexOf("internal static void Clear", StringComparison.Ordinal); Check(start >= 0 && end > start, "Exact counter renderer fragment");
        var fragment = source[start..end]; Contains(fragment, new[] { "Field(\"Counter\")?.Get<bool>(\"Value\")", "NativeUi.Icon(ui, \"Com_Icon_Counter\"" }, "Static private counter art and synced getter");
        NoPattern(fragment, "CounterIndicator|Play\\(|RefreshBuff|SetField|OnCanCounterChanged|Request|Subscribe|AddListener|ShowAttrInfo|GetPlayerDataById\"", "Counter has no animation/callback/RPC/buff-ID guess");
        Contains(source, new[] { "RefreshCounters(battleUi!);", "foreach (var badge in Counters.Values) GameUi.Dispose(badge.Icon);" }, "Counter scan/cleanup lifecycle");
        var production = WithoutNamespace(source).Replace("using UnityEngine;", "", StringComparison.Ordinal);
        RunEmbedded(context, stage, "field-counter", Template(context, "focus-tests/field-indicator/CounterFixture.cs", pin).Replace("__PRODUCTION__", production, StringComparison.Ordinal), "FieldCounterTest.Check.Run()", pin, true);
    }
}
