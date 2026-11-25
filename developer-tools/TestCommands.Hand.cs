using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static bool HandChecks(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        if (!new[] { "hand-layout", "hand-scan", "hand-outline", "hand-pointer-input", "hand-submit", "card-target-buttons" }.Contains(name, StringComparer.Ordinal)) return false;
        pin(context.NativeAssemblyPath); pin(Path.Combine(context.Root, ".deps/bepinex/BepInEx/core/Mono.Cecil.dll"));
        switch (name) {
            case "hand-layout": HandLayoutChecks(context, stage, pin); break;
            case "hand-scan": HandScan(context, stage, pin); break;
            case "hand-outline": HandOutline(context, stage, pin); break;
            case "hand-pointer-input": HandPointer(context, stage, pin); break;
            case "hand-submit": HandSubmit(context, stage, pin); break;
            case "card-target-buttons": CardTargets(context, stage, pin); break;
            default: return false;
        }
        return true;
    }
    private static void HandScan(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/HandLayoutUi.cs");
        var prefix = Fragment(source, @"(?s)    private static void Update\(\).*?(?=        // ReadyFight)", "Hand scan prefix");
        var cleanup = Fragment(source, @"(?s)        _ui = _container = _hotZone = null;.*?(?=\r?\n    \})", "Hand clear state");
        var harness = Template(context, "focus-tests/csharp-migration/hand-scan.template.cs", pin).Replace("__PREFIX__", prefix, StringComparison.Ordinal).Replace("__CLEAR__", cleanup, StringComparison.Ordinal);
        RunEmbedded(context, stage, "hand-scan", harness, "HandScanTest.Check.Run()", pin);
    }
    private static void HandOutline(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        var scale = NativeMethod(native, "FairyGUI.GObject", "HandleScaleChanged"); Check(scale.Parameters.Count == 0, "Native scale callback has no arguments"); Pattern(Operands(scale), "_scaleX.*_scaleY.*DisplayObject::SetScale", "Scale callback native rendering");
        Pattern(Operands(NativeMethod(native, "FairyGUI.EventBridge", "CallInternal")), "_isLocking.*EventCallback1::Invoke.*EventCallback0::Invoke", "Native callback order/lock");
        Pattern(Operands(NativeMethod(native, "UI.UIHandCard_Button_Card", "RendererCard")), @"get_onRollOver.*EventListener::Set\(FairyGUI.EventCallback0\).*get_onRollOut.*EventListener::Set\(FairyGUI.EventCallback0\)", "Native hover callback zero-argument types");
        foreach (var (type, name, pattern) in new[] { ("FairyGUI.StageEngine", "LateUpdate", "Stage::InternalUpdate"), ("FairyGUI.Stage", "InternalUpdate", "HandleEvents.*beforeUpdate.*DisplayObject::Update"),
            ("FairyGUI.Stage", "get_touchTarget", "GetHitTarget"), ("UI.HandCardPanel", "AddEvent", "get_onTouchBegin.*ZoomCard.*EventCallback1"), ("UI.HandCardPanel", "ZoomCard", "get_touchTarget.*LookCardByZoom"),
            ("FairyGUI.GearBase", "set_controller", "Init"), ("FairyGUI.GearSize", "Init", "get_scaleX.*get_scaleY.*_default"), ("FairyGUI.GearSize", "Apply", "_tweenConfig.*SetSize.*SetScale"),
            ("FairyGUI.GearXY", "Init", "get_x.*get_y.*_default"), ("FairyGUI.GearXY", "Apply", "_tweenConfig.*SetXY") }) Pattern(Operands(NativeMethod(native, type, name)), pattern, "World press presentation: " + type + "." + name);
        var source = ReadSource(context, "src/HandOutlineBinding.cs"); var click = ReadSource(context, "src/HandClickBinding.cs");
        foreach (var forbidden in new[] { "DelegateSupport", "ClassInjector", "System.Action", "beforeUpdate", "SetField(\"_isLocking\"", "\"Retain\"", "\"Release\"" }) Check(!source.Contains(forbidden, StringComparison.Ordinal) && !click.Contains(forbidden, StringComparison.Ordinal), "No unsafe presentation binding: " + forbidden);
        const string ns = "MigratedOutlineCheck";
        source = source.Replace("namespace BetterAstralParty;", "namespace Check {", StringComparison.Ordinal) + "\n}";
        source += "\nnamespace Check {\n" + Regex.Replace(WithoutNamespace(click), @"(?m)^using .*;\r?\n", "") + "\n}";
        var harness = Template(context, "focus-tests/csharp-migration/hand-outline.template.cs", pin).Replace("using System;", "", StringComparison.Ordinal).Replace("using System.Collections.Generic;", "", StringComparison.Ordinal).Replace("#nullable enable", "", StringComparison.Ordinal);
        var combined = "#nullable enable\nusing System; using System.Collections.Generic;\n" + (source + "\n" + harness).Replace("Check.", ns + ".", StringComparison.Ordinal).Replace("namespace Check {", "namespace " + ns + " {", StringComparison.Ordinal);
        RunEmbedded(context, stage, "hand-outline", combined, ns + ".Regression.Run()", pin);
    }
    private static void HandPointer(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        foreach (var (owner, name, parameters, result, isStatic) in new[] { ("FairyGUI.Stage", "HitTest", "UnityEngine.Vector2,System.Boolean", "FairyGUI.DisplayObject", false), ("FairyGUI.GRoot", "DisplayObjectToGObject", "FairyGUI.DisplayObject", "FairyGUI.GObject", false), ("FairyGUI.HitTestContext", "ClearRaycastHitCache", "", "System.Void", true) }) {
            MethodDefinition[] found = Array.Empty<MethodDefinition>();
            for (var type = native.MainModule.GetTypes().Single(row => row.FullName == owner); type != null; type = type.BaseType?.Resolve()) {
                found = type.Methods.Where(method => method.Name == name && string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName)) == parameters).ToArray(); if (found.Length != 0) break;
            }
            Check(found.Length == 1 && found[0].ReturnType.FullName == result && found[0].IsStatic == isStatic, "Native pointer ABI: " + owner + "." + name);
        }
        foreach (var (type, name, first, second) in new[] { ("GameLogic.Card", "CardAction", "UI.CardWindow::ShowCard", "UI.CardWindow::UpdateConfig"), ("UI.CardWindow", "ShowCard", "FairyGUI.Window::Show", "UI.UICardWindow::showContent"),
            ("UI.HandCardPanel", "ResetCardList", "UI.HandCardPanel::UpdateCardList", "UI.HandCardPanel::RefreshUsableCards"),
            ("UI.HandCardPanel", "RefreshUsableCards", "UI.UIHandCardPanel::container_Card", "FairyGUI.GObject::set_touchable"), ("FairyGUI.GObject", "__touchEnd", "FairyGUI.GObject::set_draggingObject", "FairyGUI.EventDispatcher::DispatchEvent") }) Contains(Operands(name is "CardAction" or "ShowCard" ? NativeAsyncMoveNext(native, type, name) : NativeMethod(native, type, name, name == "ResetCardList" ? 1 : null)), new[] { first, second }, "Native cancel lifecycle");
        var targetSelection = NativeAsyncMoveNext(native, "UI.BattleSelectMonsterWindow", "ShowCardVailMonsterTarget", 4);
        var cancels = targetSelection.Body.Instructions.Where(instruction => instruction.OpCode.Name is "ldftn" or "ldvirtftn").Select(instruction => instruction.Operand).OfType<MethodReference>().Select(method => method.Resolve())
            .Where(method => method.HasBody && method.DeclaringType.DeclaringType?.FullName == "UI.BattleSelectMonsterWindow" && Operands(method).Contains("GameLogic.CardSignal::resetCardList", StringComparison.Ordinal) && Operands(method).Contains("FairyGUI.Window::Hide", StringComparison.Ordinal)).Distinct().ToArray();
        Check(cancels.Length == 1, "Exact native monster-selection cancel callback originates in the source async method");
        Contains(Operands(cancels[0]), new[] { "GameLogic.CardSignal::resetCardList", "FairyGUI.Window::Hide" }, "Native cancel lifecycle");
        NoPattern(Operands(cancels[0]), "UI.CardWindow::|FairyGUI.Window::HideImmediately", "Monster cancel retains empty CardWindow");
        var game = ReadSource(context, "src/GameUi.cs"); var preview = ReadSource(context, "src/PingDetailsUi.cs"); var hand = ReadSource(context, "src/HandLayoutUi.cs");
        var interaction = Fragment(hand, @"(?s)        // Monster cancel can leave an empty CardWindow on stage.*?(?=        if \(_expanded is \{ \} missing)", "Grouped hand interaction branch");
        Pattern(interaction, @"PollPileClick\(path, pointer, unit, pending > 0 \|\| usingCard \|\| FieldCameraReturnUi.ConsumedInput\)", "Native use ownership retained in grouped input");
        const string ns = "MigratedHandPointer";
        var harness = Template(context, "focus-tests/csharp-migration/hand-pointer-input.template.cs", pin).Replace("__NS__", ns, StringComparison.Ordinal)
            .Replace("__POINTER__", MethodFragment(game, "internal static IReadOnlyList<RuntimeObject> PointerPath"), StringComparison.Ordinal)
            .Replace("__TARGETS__", MethodFragment(preview, "private static void UpdatePingTargets"), StringComparison.Ordinal).Replace("__UPDATE__", MethodFragment(preview, "private static void Update"), StringComparison.Ordinal)
            .Replace("__CLICK__", MethodFragment(hand, "private static void PollPileClick"), StringComparison.Ordinal).Replace("__LAYOUT__", WithoutNamespace(ReadSource(context, "src/PingDetailsLayout.cs")), StringComparison.Ordinal)
            .Replace("__HAND_LAYOUT__", WithoutNamespace(ReadSource(context, "src/HandLayout.cs")), StringComparison.Ordinal).Replace("__INTERACTION__", interaction, StringComparison.Ordinal);
        RunEmbedded(context, stage, "hand-pointer-input", harness, ns + ".Check.Run()", pin);
        var legacy = harness.Replace("path.Any(item => item.TypeName == \"CardWindow\")", "GameUi.Find(root, \"CardWindow\", maxDepth: 1) != null", StringComparison.Ordinal); Check(legacy != harness, "Exact legacy CardWindow mutant bound");
        RunEmbedded(context, stage, "hand-pointer-legacy-mutant", legacy.Replace(ns, ns + "Legacy", StringComparison.Ordinal), ns + "Legacy.Check.Run()", pin, expectedFailure: "retained empty CardWindow must not disable");
        // Before 2026.11.4-dev.4 a native container lock blocked pile clicks (user-reported twice, 2026-11-18 and 2026-11-21).
        var lockGate = harness.Replace("PollPileClick(path, pointer, unit, pending > 0", "PollPileClick(path, pointer, unit, inputLocked || pending > 0", StringComparison.Ordinal); Check(lockGate != harness, "Exact lock-gate mutant bound");
        RunEmbedded(context, stage, "hand-pointer-lock-mutant", lockGate.Replace(ns, ns + "Lock", StringComparison.Ordinal), ns + "Lock.Check.Run()", pin, expectedFailure: "under a native container lock");
    }
    private static void HandSubmit(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        Contains(Operands(NativeAsyncMoveNext(native, "UI.HandCardPanel", "DragEndEvent")), new[] { "UIHandCardPanel::hotZone", "GObject::LocalToGlobal", "GObject::get_actualWidth", "GObject::get_actualHeight" }, "Native card drop bounds");
        var source = ReadSource(context, "src/HandLayoutUi.cs"); var methods = new Dictionary<string, string>();
        foreach (var name in new[] { "UpdateDropZone" }) methods[name] = MethodFragment(source, "private static void " + name);
        var toLocal = Fragment(source, @"(?s)    private static Vector2 ToLocal\(.*?;", "Native root-to-local drop path"); var update = MethodFragment(source, "private static void Update");
        var zone = update.IndexOf("if (_dropBottom is { } dropBottom) UpdateDropZone(root, width, height, dropBottom * height);", StringComparison.Ordinal);
        Check(zone >= 0 && zone < update.IndexOf("if (CardPointerBusy(dragging))", StringComparison.Ordinal)
            && update.Contains("_dropBottom = HandLayout.DropBottom(pileTop, open, unit) / height;", StringComparison.Ordinal), "Drop zone follows a resize through a drag; its bottom follows the resting cards");
        Contains(source, new[] { "if (!Plugin.GroupHand.Value || root == null) { Clear(); return; }", "PlayerIsWatcher", "FightWindow", "VisibleCombatAdvisor.IsPve", "_hotPosition", "_hotSize" }, "Submit cleanup/gates");
        NoPattern(methods["UpdateDropZone"], @"\.Set\(", "Drop geometry retains native state/controller");
        const string ns = "MigratedHandSubmit";
        var harness = Template(context, "focus-tests/csharp-migration/hand-submit.template.cs", pin).Replace("__NS__", ns, StringComparison.Ordinal).Replace("__LAYOUT__", WithoutNamespace(ReadSource(context, "src/HandLayout.cs")), StringComparison.Ordinal).Replace("__TOLOCAL__", toLocal, StringComparison.Ordinal);
        foreach (var row in methods) harness = harness.Replace("__" + row.Key + "__", row.Value, StringComparison.Ordinal);
        RunEmbedded(context, stage, "hand-submit", harness, ns + ".SubmitCheck.Run()", pin);
    }
    private static void CardTargets(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        foreach (var (type, parent) in new[] { ("UI.CardWindow", "UI.BaseWindow"), ("UI.BaseWindow", "FairyGUI.Window") }) {
            var row = native.MainModule.GetType(type); Check(row.BaseType.FullName == parent && !row.Methods.Any(method => method.Name is "Show" or "ShowOn"), "Native window hierarchy/parenting: " + type);
        }
        foreach (var (type, name, pattern) in new[] { ("FairyGUI.Window", "Show", "FairyGUI.GRoot::ShowWindow"), ("FairyGUI.Window", "ShowOn", "FairyGUI.GRoot::ShowWindow"), ("FairyGUI.GRoot", "ShowWindow", "FairyGUI.GComponent::AddChild"),
            ("FairyGUI.GObject", "set_sortingOrder", "ChildSortingOrderChanged"), ("FairyGUI.GComponent", "ChildSortingOrderChanged", "SetChildIndex"), ("UI.HandCardPanel", "Show", "UI.BasePanel.*::Show"), ("UI.BasePanel`1", "Show", "FairyGUI.GComponent::AddChild"), ("UI.BasePanel`1", "Show", "set_sortingOrder") }) Pattern(Operands(NativeMethod(native, type, name)), pattern, "Native target order: " + type + "." + name);
        foreach (var name in new[] { "type", "btn_Cancel", "btn_Sure", "com_ShowCard" }) Check(native.MainModule.GetType("UI.UICardWin_Com_UseCard").Fields.Any(field => field.Name == name), "Native target field: " + name);
        // Target steps raised above the hand: each native entry selects its exact com_UseCard.type index.
        foreach (var (method, index) in new[] { ("RefreshCardInfo_SelectPlayer", "ldc.i4.1"), ("RefreshPointList", "ldc.i4.3"), ("InitSelectLandStatus", "ldc.i4.4") })
            Pattern(Il(NativeMethod(native, "UI.CardWindow", method)), @"UICardWin_Com_UseCard::type\n[^\n]*: " + index + @"\n[^\n]*Controller::set_selectedIndex", "Native target step type: " + method + " " + index);
        var source = WithoutNamespace(ReadSource(context, "src/CardTargetButtonsUi.cs")); NoPattern(source, "Set\\(\"[xy]\"|SetXY|SetScale|AddChild", "Target geometry/hierarchy stays native");
        Check(!source.Contains("Plugin.CardTargetButtons", StringComparison.Ordinal)
            && !ReadSource(context, "src/Plugin.cs").Contains("CardTargetButtons", StringComparison.Ordinal), "Target button priority has no saved preference gate");
        var driver = ReadSource(context, "src/ModUi.cs"); var target = driver.IndexOf("CardTargetButtonsUi.Tick();", StringComparison.Ordinal);
        Check(target > driver.IndexOf("PvpSafety.Poll()", StringComparison.Ordinal) && target < driver.IndexOf("PingDetailsUi.Tick();", StringComparison.Ordinal) && target < driver.IndexOf("HandLayoutUi.Tick();", StringComparison.Ordinal), "PvE target ordering precedes pointer consumers");
        // Another player's card result lets clicks through its empty area (CardResultClicks, 2026-11-17).
        foreach (var name in new[] { "com_CardResult", "com_UseCard", "com_ChooseCard" })
            Check(native.MainModule.GetType("UI.UICardWindow").Fields.Any(field => field.Name == name), "Native card window part: " + name);
        Check(driver.Contains("CardTargetButtonsUi.Tick(); CardResultClicks.Tick();", StringComparison.Ordinal)
            && driver.Split("CardTargetButtonsUi.Clear, CardResultClicks.Clear,").Length == 3, "Card result clicks tick with the target buttons and clear with PvP and the core breaker");
        RunEmbedded(context, stage, "card-result-clicks", Template(context, "focus-tests/csharp-migration/card-result-clicks.template.cs", pin)
            .Replace("__SOURCE__", WithoutNamespace(ReadSource(context, "src/CardResultClicks.cs")), StringComparison.Ordinal), "CardResultClicksTest.Check.Run()", pin);
        RunEmbedded(context, stage, "card-target-buttons", Template(context, "focus-tests/csharp-migration/card-target-buttons.template.cs", pin).Replace("__SOURCE__", source, StringComparison.Ordinal), "TargetButtonsTest.Check.Run()", pin);
    }
}
