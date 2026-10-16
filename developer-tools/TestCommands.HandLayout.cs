using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void HandLayoutChecks(DevContext context, string stage, Action<string> pin)
    {
        HandScan(context, stage, pin); HandOutline(context, stage, pin); HandPointer(context, stage, pin); HandSubmit(context, stage, pin);
        using var native = Native(context);
        var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        FieldDefinition? FindField(string owner, string name) { for (var type = types[owner]; type != null; type = type.BaseType?.Resolve()) { var field = type.Fields.FirstOrDefault(row => row.Name == name); if (field != null) return field; } return null; }
        foreach (var (type, names) in new[] { ("UI.UIHandCardPanel", "container_Card,hotZone"), ("UI.UIHandCard_Button_Card", "CustomPosition,CustomRotation,_CustomSortingOrder,zoomInScale,zoomOutScale,IsRelease,com_Card,effectOutline,effectOutline_Suggest_Bottom,effectTempCard"),
            ("GameLogic.CardPool", "ShowCardStatus"), ("FairyGUI.TweenManager", "_activeTweens,_totalActiveTweens"), ("FairyGUI.GTweener", "_killed,_propType"), ("FairyGUI.EventListener", "_bridge"), ("FairyGUI.EventBridge", "_isLocking"), ("GameLogic.HandCardData", "Guid"), ("GameLogic.GameLogicManager", "_inst") })
            foreach (var name in names.Split(',')) Check(FindField(type, name) != null, "Inherited native hand field: " + type + "." + name);
        foreach (var (type, name) in new[] { ("GameLogic.WatchLogic", "PlayerIsWatcher"), ("GameLogic.HandCardData", "get_Config"), ("UI.UIHandCard_Button_Card", "get_CardData"), ("FairyGUI.GObject", "get_draggingObject"), ("FairyGUI.GObject", "get_onRollOut"), ("FairyGUI.GObject", "RootToLocal"), ("FairyGUI.GComponent", "set_opaque") })
            Check(types[type].Methods.Any(method => method.Name == name), "Native hand binding: " + type + "." + name);
        foreach (var (type, name, expected) in new[] { ("UI.UIHandCard_Button_Card", "CustomPosition", "UnityEngine.Vector2"), ("UI.UIHandCard_Button_Card", "CustomRotation", "System.Single"), ("UI.UIHandCard_Button_Card", "_CustomSortingOrder", "System.Int32"),
            ("UI.UIHandCard_Button_Card", "zoomInScale", "UnityEngine.Vector2"), ("UI.UIHandCard_Button_Card", "zoomOutScale", "UnityEngine.Vector2"), ("UI.UIHandCard_Button_Card", "IsRelease", "System.Boolean"), ("GameLogic.HandCardData", "Guid", "System.Int32"),
            ("FairyGUI.GObject", "_pivotAsAnchor", "System.Boolean"), ("FairyGUI.GTweener", "_killed", "System.Boolean"), ("FairyGUI.GTweener", "_propType", "System.Int32"), ("FairyGUI.TweenValue", "x", "System.Single"), ("FairyGUI.EventBridge", "_isLocking", "System.Boolean") }) {
            var field = FindField(type, name); Check(field != null && !field.IsStatic, "Instance native value field: " + type + "." + name);
            var actual = field!.FieldType.FullName;
            if (actual != expected) { var valueType = field.FieldType.Resolve(); if (valueType.IsEnum) actual = valueType.Fields.Single(row => row.Name == "value__").FieldType.FullName; }
            Check(actual == expected, "Exact native value/enum size: " + type + "." + name);
        }
        foreach (var (type, name) in new[] { ("FairyGUI.GTweener", "get_endValue"), ("FairyGUI.TweenValue", "set_vec2"), ("FairyGUI.GObject", "TweenMove"), ("FairyGUI.GObject", "TweenScale"), ("FairyGUI.GTweener", "get_startValue"), ("FairyGUI.GTweener", "get_normalizedTime"), ("FairyGUI.GTweener", "get_target"), ("UI.UIHandCard_Button_Card", "get__ShowPosition") }) Check(types[type].Methods.Any(method => method.Name == name), "Native animation binding: " + type + "." + name);
        var query = types["FairyGUI.GTween"].Methods.Where(method => method.Name == "IsTweening" && method.Parameters.Count == 1).ToArray(); Check(query.Length == 1 && query[0].Parameters[0].ParameterType.FullName == "System.Object", "Exact native tween query overload");
        foreach (var (name, pattern) in new[] { ("PlayTransition", "Retain"), ("TranslateCard", "OnComplete"), ("Release", "set_visible"), ("IsDistribute", "get_height") }) Pattern(Operands(NativeMethod(native, "UI.UIHandCard_Button_Card", name)), pattern, "Native lifecycle: " + name);
        Pattern(Operands(NativeAsyncMoveNext(native, "UI.HandCardPanel", "DragEndEvent")), "UIHandCardPanel::hotZone", "Native drop target path");
        foreach (var (type, name, pattern) in new[] { ("UI.BasePanel`1", "Show", "set_fairyBatching"), ("FairyGUI.GComponent", "set_fairyBatching", "set_fairyBatching"), ("FairyGUI.Container", "UpdateBatchingFlags", "InvalidateBatchingState"),
            ("FairyGUI.Container", "DoFairyBatching", "get_material"), ("FairyGUI.Container", "CollectChildren", "DoFairyBatching"), ("FairyGUI.Container", "SetRenderingOrder", "SetRenderingOrder") }) Pattern(Operands(NativeMethod(native, type, name)), pattern, "Native batching: " + type + "." + name);
        foreach (var name in new[] { "SetPosition", "SetScale" }) NoPattern(Operands(NativeMethod(native, "FairyGUI.DisplayObject", name)), "InvalidateBatchingState", "Native transform invalidation unchanged: " + name);
        var source = ReadSource(context, "src/HandLayoutUi.cs"); var readOnly = source.Replace("card.Get(\"onRollOut\")!.Field(\"_bridge\")!.Field<bool>(\"_isLocking\")", "nativeUsePending", StringComparison.Ordinal);
        foreach (var forbidden in new[] { "RequestUse", "ShowUseCardWin", "SetDragEvent", "onRollOver", "onRollOut", "\"Retain\"", "\"Release\"", "SetField(\"_CardData\"", "SetField(\"_EnableUse\"", "\"Kill\"", "ReadyAt", ".Face.Set(\"alpha\"", "card.Set(\"visible\"", "card.Set(\"touchable\"", "Strip" }) Check(!readOnly.Contains(forbidden, StringComparison.Ordinal), "Hand changes no gameplay/input: " + forbidden);
        Contains(source, new[] {
            "PlayerIsWatcher",
            "FightWindow",
            "get_draggingObject",
            "Input.GetMouseButton(0)",
            "\"IsDistribute\"",
            "\"IsTweening\"",
            "HandLayout.Resting",
            "\"ShowCardStatus\"",
            "_hotPosition",
            "_hotSize",
            "node.Get<float>(\"scaleX\") <= 0",
            "HandLayout.CanResume(shown, waiting, usePending, foreignMotion)",
            "HandLayout.RetargetStart",
            "\"_activeTweens\"",
            "HandLayout.InColumn",
            "var inputLocked = !_container!.Get<bool>(\"touchable\");",
            "if (hoverLocked) { _expanded = null;",
            "if (CardPointerBusy(dragging))",
            "var hoverLocked = inputLocked || (!clickMode && Input.GetMouseButton(0));",
            "HoldHover(e, motion, pos)",
            "HandLayout.Focus(slot)",
            "FitEffects(e, true)",
            "GameUi.PointerPath(refresh: true)",
            "_focused = hovered.Pointer;",
            "HandLayout.InFocus",
            "if (e.CenterLocked)",
            "card.SetField(\"zoomInScale\", zoom)",
            "RestoreZoom(old)",
            "RestoreZoom(Entries[key])",
            "RestoreZoom(e)",
        }, "Hand layout safety");
        Check(!source.Contains("StaticCall(_tween, \"GetTween\"", StringComparison.Ordinal) && !source.Contains("\"ShowCardStatus\")!.Value<int>() != 0", StringComparison.Ordinal), "No first-tween-only or persistent zoom gate");
        Check(!source.Contains("_drawAt", StringComparison.Ordinal), "Hand render correction not frame-throttled");
        Check(!source.Contains("Input.GetMouseButton(0) || ModUi.IsOpen", StringComparison.Ordinal) && !source.Contains("if (dragging != null)", StringComparison.Ordinal), "Unrelated mouse/drag does not stop layout");
        foreach (var forbidden in new[] { "DelegateSupport", "Il2CppSystem.Action", "add_beforeUpdate", "remove_beforeUpdate", "ClassInjector" }) Check(!source.Contains(forbidden, StringComparison.Ordinal), "No native crash binding: " + forbidden);
        Contains(MethodFragment(source, "internal static void Tick"), new[] { "try { Update(); }", "Compatibility.Block(\"HandLayout\", ex, Clear)" }, "Managed driver/circuit breaker");
        var fragments = new Dictionary<string, string> {
            ["RETARGET"] = MethodFragment(source, "private static void Retarget"), ["CLASSIFY"] = MethodFragment(source, "private static bool LayoutMotion"), ["PENDING"] = MethodFragment(source, "private static bool UsePending"),
            ["HOLD"] = MethodFragment(source, "private static bool HoldHover"), ["DISPLAYRESET"] = MethodFragment(source, "private static bool DisplayZoomReset"), ["EFFECTS"] = MethodFragment(source, "private static void FitEffects"),
            ["POINTER"] = MethodFragment(source, "private static bool CardPointerBusy"), ["SHOWN"] = MethodFragment(source, "private static bool Shown"), ["ORDER"] = MethodFragment(source, "private static void SetStackOrder"), ["BATCH"] = MethodFragment(source, "private static void SetCardBatching"),
            ["OPACITY"] = MethodFragment(source, "private static void SetFoldedOpacity"), ["CLICK"] = MethodFragment(source, "private static void PollPileClick"), ["CLEARFRAME"] = MethodFragment(source, "private static void ClearFrame"), ["CLEARMOTIONS"] = MethodFragment(source, "private static void ClearMotions"),
            ["FOCUS_BLOCK"] = Fragment(source, @"(?s)            if \(_expanded == group\)\r?\n            \{.*?\r?\n            \}", "Expanded focus block"), ["GROUPING"] = Fragment(source, @"(?s)        var cards = FrameCards;.*?aspects\[column\] = Aspect\(piles\[column\]\[0\]\);", "Per-frame card grouping"),
            ["LAYOUT"] = WithoutNamespace(ReadSource(context, "src/HandLayout.cs")), ["NS"] = "MigratedHandMotion"
        };
        Contains(source, new[] { "finally { ClearFrame(); }", "SetCardBatching(e, false)", "SetCardBatching(e, true)", "SetStackOrder(card, row, _expanded != null && _focused == card.Pointer)" }, "Frame scratch/batching/order restore");
        Contains(fragments["FOCUS_BLOCK"], new[] { "if (clickMode) _focused = IntPtr.Zero;" }, "Click focus exit reset");
        var harness = Template(context, "focus-tests/csharp-migration/hand-layout.template.cs", pin); foreach (var row in fragments) harness = harness.Replace("__" + row.Key + "__", row.Value, StringComparison.Ordinal);
        RunEmbedded(context, stage, "hand-layout-motion", harness, "MigratedHandMotion.MotionCheck.Run()", pin);
        NoPattern(source, "HandTargetUi|_targetCeiling|BattlePreMonsterWindow|BattleSelectMonsterWindow", "Grouped hand leaves native target windows unchanged"); NoPattern(fragments["LAYOUT"], "TargetPanel", "Hand layout adds no target panel");
    }
}
