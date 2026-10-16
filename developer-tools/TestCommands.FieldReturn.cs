using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void FieldCameraReturn(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        var map = ExactField(native, "UI.UIBattleInfoPanel", "com_MapInfo", "UI.UIBattleInfo_Com_MapInfo");
        var anchor = ExactField(native, "UI.UIBattleInfo_Com_MapInfo", "btn_ShowMap", "FairyGUI.GButton");
        var assigned = NativeMethod(native, "UI.UIBattleInfo_Com_MapInfo", "ConstructFromXML").Body.Instructions.Where(instruction => instruction.OpCode.Name == "stfld" && instruction.Operand is FieldReference field && field.FullName == anchor.FullName).ToArray();
        Check(assigned.Length == 1 && ((MethodReference)assigned[0].Previous.Previous.Operand).Name == "GetChildAt" && assigned[0].Previous.Previous.Previous.OpCode.Name == "ldc.i4.1", "Map pin is native child one");
        var handler = NativeMethod(native, "UI.BattleInfoPanel", "Minimap_AddEvent").Body.Instructions;
        Check(handler.Any(instruction => instruction.Operand is FieldReference field && field.FullName == anchor.FullName)
            && handler.Any(instruction => instruction.OpCode.Name == "ldftn" && instruction.Operand is MethodReference method && method.Name == "SwitchMap"), "Pin handler provenance");
        ExactMethod(native, "FairyGUI.GObject", "LocalToRoot", "UnityEngine.Vector2", "UnityEngine.Vector2", "FairyGUI.GRoot");
        ExactField(native, "FairyGUI.UIConfig", "clickDragSensitivity", "System.Int32", true, true);
        var dragging = native.MainModule.GetType("FairyGUI.GObject").Methods.Single(method => method.Name == "get_draggingObject"); Check(dragging.IsPublic && dragging.IsStatic && dragging.Parameters.Count == 0, "Native dragging reader");
        var state = native.MainModule.GetType("FairyGUI.GButton").Fields.Single(field => field.Name == "changeStateOnClick"); Check(state.IsPublic && state.FieldType.FullName == "System.Boolean", "Button state Boolean");
        foreach (var (name, constant) in new[] { ("Shrink", 3), ("None", 0) }) { var field = native.MainModule.GetType("FairyGUI.AutoSizeType").Fields.Single(row => row.Name == name); Check(field.HasConstant && (int)field.Constant == constant, "Native text enum: " + name); }
        foreach (var name in new[] { "get_textWidth", "get_textHeight" }) {
            ExactMethod(native, "FairyGUI.GTextField", name, "System.Single");
            Check(NativeMethod(native, "FairyGUI.TextField", name).Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "BuildLines"), "Text metric builds fresh unshrunk lines: " + name);
        }
        var xMin = ExactMethod(native, "FairyGUI.GObject", "get_xMin", "System.Single"); Check(!xMin.Body.Instructions.Any(instruction => instruction.Operand is FieldReference field && field.Name.Contains("scale", StringComparison.OrdinalIgnoreCase)), "Resting xMin is pivot-only");
        ExactMethod(native, "FairyGUI.GObject", "SetSize", "System.Void", "System.Single", "System.Single");
        ExactMethod(native, "FairyGUI.GObject", "get_relations", "FairyGUI.Relations");
        ExactMethod(native, "FairyGUI.Relations", "Add", "System.Void", "FairyGUI.GObject", "FairyGUI.RelationType");
        var size = native.MainModule.GetType("FairyGUI.RelationType").Fields.Single(field => field.Name == "Size"); Check(size.HasConstant && (int)size.Constant == 24, "Native parent Size relation");
        ExactMethod(native, "FairyGUI.GComponent", "GetChild", "FairyGUI.GObject", "System.String");
        foreach (var name in new[] { "get_pivotX", "get_pivotY" }) ExactMethod(native, "FairyGUI.GObject", name, "System.Single");
        foreach (var (name, result) in new[] { ("_downScaled", "System.Boolean"), ("_downEffectValue", "System.Single") }) {
            var field = native.MainModule.GetType("FairyGUI.GButton").Fields.Single(row => row.Name == name); Check(!field.IsStatic && field.FieldType.FullName == result, "Native pressed-scale field: " + name);
        }
        var button = NativeMethod(native, "FairyGUI.GButton", "SetState").Body.Instructions;
        foreach (var opcode in new[] { "mul", "div" }) Check(button.Any(instruction => instruction.OpCode.Name == opcode && instruction.Previous.Operand is FieldReference field && field.Name == "_downEffectValue"), "Native pressed-scale operation: " + opcode);
        ExactMethod(native, "FairyGUI.GObject", "get_sortingOrder", "System.Int32");
        Check(ExactMethod(native, "FairyGUI.GObject", "set_sortingOrder", "System.Void", "System.Int32").Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "ChildSortingOrderChanged"), "Sorting property notifies parent");
        foreach (var name in new[] { "AddChildAt", "ChildSortingOrderChanged" }) {
            var body = NativeMethod(native, "FairyGUI.GComponent", name).Body.Instructions;
            Check(body.Any(instruction => instruction.Operand is MethodReference method && method.Name == "GetInsertPosForSortingChild")
                && body.Any(instruction => instruction.Operand is FieldReference field && field.Name == "_sortingChildCount"), "Native sorted-tail insertion: " + name);
        }
        var comparisons = NativeMethod(native, "FairyGUI.GComponent", "GetInsertPosForSortingChild").Body.Instructions.Where(instruction => instruction.OpCode.FlowControl == FlowControl.Cond_Branch && instruction.Previous.Operand is MethodReference method && method.Name == "get_sortingOrder").ToArray();
        Check(comparisons.Length == 1 && comparisons[0].OpCode.Name.StartsWith("blt", StringComparison.Ordinal), "Equal-order insertion is stable strict-less-than");
        var sortedGuard = NativeMethod(native, "FairyGUI.GComponent", "SetChildIndex").Body.Instructions.Where(instruction => instruction.Operand is MethodReference method && method.Name == "get_sortingOrder").ToArray();
        Check(sortedGuard.Length == 1 && sortedGuard[0].Next.OpCode.Name.StartsWith("brfalse", StringComparison.Ordinal) && sortedGuard[0].Next.Next.OpCode.Name == "ret", "Manual child moves preserve sorted tail");
        var ascent = native.MainModule.GetType("FairyGUI.ChildrenRenderOrder").Fields.Single(field => field.Name == "Ascent");
        Check(ascent.HasConstant && (int)ascent.Constant == 0 && Calls(NativeMethod(native, "FairyGUI.GComponent", "BuildNativeDisplayList")).Contains("FairyGUI.DisplayObject FairyGUI.Container::AddChild(FairyGUI.DisplayObject)", StringComparer.Ordinal), "Ascending native display list");
        var hitStart = NativeMethod(native, "FairyGUI.Container", "HitTest_Container").Body.Instructions.Where(instruction => instruction.Operand is MethodReference method && method.Name == "get_Count" && instruction.Previous.Operand is FieldReference field && field.Name == "_children").ToArray();
        Check(hitStart.Length == 1 && hitStart[0].Next.OpCode.Name == "ldc.i4.1" && hitStart[0].Next.Next.OpCode.Name == "sub", "Hit testing begins at last rendered child");
        ExactMethod(native, "FairyGUI.Window", "get_modal", "System.Boolean");
        var top = NativeMethod(native, "FairyGUI.GRoot", "GetTopWindow"); var topNames = top.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().Select(method => method.Name).ToArray();
        Check(top.IsPublic && top.Parameters.Count == 0 && top.ReturnType.FullName == "FairyGUI.Window" && topNames.Contains("GetChildAt", StringComparer.Ordinal) && !topNames.Contains("get_modal", StringComparer.Ordinal) && !topNames.Contains("get_visible", StringComparer.Ordinal)
            && top.Body.Instructions.Any(instruction => instruction.OpCode.Name == "isinst" && instruction.Operand is TypeReference type && type.FullName == "FairyGUI.Window"), "Top window means any root Window child");
        Check(native.MainModule.GetType("UI.BaseWindow").BaseType.FullName == "FairyGUI.Window" && native.MainModule.GetType("UI.BattleInfoPanel").BaseType.FullName == "UI.BasePanel`1<UI.UIBattleInfoPanel>" && native.MainModule.GetType("UI.UIBattleInfoPanel").BaseType.FullName == "FairyGUI.GComponent", "Native field HUD inheritance");
        foreach (var name in new[] { "UI.ExpressionWindow", "UI.ExpressionListWindow", "UI.OperateTimeWindow" }) {
            var type = native.MainModule.GetType(name);
            Check(type.BaseType.FullName == "UI.BaseWindow" && type.NestedTypes.Where(row => row.Name.StartsWith("<TryShowAsync>", StringComparison.Ordinal)).SelectMany(row => row.Methods.Where(method => method.Name == "MoveNext")).SelectMany(Calls).Contains("System.Void FairyGUI.Window::Show()", StringComparer.Ordinal)
                && !type.Methods.Where(method => method.HasBody).SelectMany(Calls).Contains("System.Void FairyGUI.Window::set_modal(System.Boolean)", StringComparer.Ordinal), "Ordinary HUD nonmodal show: " + name);
        }
        var readyCalls = native.MainModule.GetType("GameLogic.BattleLogic").NestedTypes.Where(type => type.Name.StartsWith("<ReadyBattleUI>", StringComparison.Ordinal)).SelectMany(type => type.Methods.Where(method => method.Name == "MoveNext")).SelectMany(Calls).ToArray();
        foreach (var name in new[] { "UI.ExpressionWindow", "UI.ExpressionListWindow" }) Check(readyCalls.Contains("Cysharp.Threading.Tasks.UniTask " + name + "::TryShowAsync()", StringComparer.Ordinal), "Battle entry shows HUD: " + name);
        foreach (var name in new[] { "UpdateOperationProgress", "UpdateShowProgress" }) Check(Calls(NativeMethod(native, "UI.OperateTimeWindow", name)).Contains("Cysharp.Threading.Tasks.UniTask UI.OperateTimeWindow::TryShowAsync()", StringComparer.Ordinal), "Field countdown show: " + name);
        var raycast = NativeMethod(native, "Core.FreeCameraObject", "RaycastLand"); var rayCalls = Calls(raycast);
        Check(rayCalls[0] == "System.Boolean FairyGUI.Stage::get_isTouchOnUI()" && rayCalls[1] == "System.Boolean Core.FreeCameraObject::TouchOnAttrUI()", "Native world-click UI gate order");
        var guard = raycast.Body.Instructions;
        Check(guard[1].OpCode.Name.StartsWith("brfalse", StringComparison.Ordinal) && guard[4].OpCode.Name.StartsWith("brtrue", StringComparison.Ordinal) && ((Instruction)guard[1].Operand).Offset == ((Instruction)guard[4].Operand).Offset && guard[5].OpCode.Name == "ldnull" && guard[6].OpCode.Name == "ret", "UI hits reject world-land raycasts");
        Check(NativeMethod(native, "UI.BattleInfoPanel", "AttrContainerIsAncestorOf").Body.Instructions.Any(instruction => instruction.Operand is FieldReference field && field.Name == "com_AttrInfos"), "Native attribute UI exception");
        var production = WithoutNamespace(ReadSource(context, "src/FieldCameraReturnUi.cs")).Replace("using UnityEngine;", "", StringComparison.Ordinal);
        NoPattern(production, @"Delegate|\.onClick|onTouchBegin|Register|AddListener|Dispatch\(|Send\(", "Return uses managed polling without native/network event writes"); NoPattern(production, "1920|1080", "Return follows native pin geometry, not fixed reference resolution");
        Contains(ReadSource(context, "src/ModText.cs"), new[] { "[\"자유 시점 카메라 종료\"] = \"Exit Free Camera\"" }, "Exit camera KR/EN mapping");
        var style = ReadSource(context, "src/NativeUi.cs"); Contains(style, new[] { "label.Set(\"autoSize\", 3);", "label.Set(\"singleLine\", true);" }, "Owned native title shrink");
        var position = Fragment(style, @"(?ms)    internal static void Position\(RuntimeObject obj, float x, float y, float restingScale\)\s*=>.*?;", "NativeUi.Position required argument contract");
        var template = Template(context, "focus-tests/field-camera-return/RuntimeFixture.cs", pin);
        var asset = Path.Combine(context.Root, ".research/ui-assets/Common_fui.bytes"); pin(asset);
        RunEmbedded(context, stage, "field-camera-return", template.Replace("__PRODUCTION__", production, StringComparison.Ordinal).Replace("__NATIVE_POSITION__", position, StringComparison.Ordinal), "FieldCameraReturnTest.NativeReturnAsset.Load(System.IO.File.ReadAllBytes(" + Literal(asset) + ")); FieldCameraReturnTest.Check.Run()", pin, true);
        foreach (var (name, before, after) in new[] { ("Gap", "Math.Max(top.x, bottom.x) + anchorHeight / 2", "Math.Max(top.x, bottom.x) + anchorHeight / 6"),
            ("Size", "anchorHeight * 1.25f / buttonHeight", "anchorHeight / buttonHeight"), ("Centre", "var y = Math.Min(top.y, bottom.y);", "var y = (top.y + bottom.y - buttonHeight * scale) / 2;") }) {
            Check(production.Contains(before, StringComparison.Ordinal), "Exact negative-control binding: " + name);
            var ns = "FieldCameraReturnLegacy" + name;
            var mutant = template.Replace("FieldCameraReturnTest", ns, StringComparison.Ordinal).Replace("UnityEngine", ns + ".UnityEngine", StringComparison.Ordinal)
                .Replace("__PRODUCTION__", production.Replace(before, after, StringComparison.Ordinal), StringComparison.Ordinal).Replace("__NATIVE_POSITION__", position, StringComparison.Ordinal);
            RunEmbedded(context, stage, "field-return-mutant-" + name, mutant, ns + ".NativeReturnAsset.Load(System.IO.File.ReadAllBytes(" + Literal(asset) + ")); " + ns + ".Check.Run()", pin, true, expectedFailure: "Return UI regression at fixture line");
        }
    }
}
