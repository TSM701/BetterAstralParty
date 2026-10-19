using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static bool FieldChecks(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        if (!new[] { "field-zoom", "field-free-camera", "field-camera-return", "field-indicator-opacity", "field-indicator-input", "field-counter", "field-buffs" }.Contains(name, StringComparer.Ordinal)) return false;
        pin(context.NativeAssemblyPath); pin(Path.Combine(context.Root, ".deps/bepinex/BepInEx/core/Mono.Cecil.dll"));
        if (name is "field-zoom" or "field-free-camera") foreach (var path in new[] { ".research/extracted/Cinemachine.dll.dll", ".research/extracted/mscorlib.dll.dll", ".research/extracted/UnityEngine.CoreModule.dll.dll" }) pin(Path.Combine(context.Root, path));
        switch (name) {
            case "field-zoom": FieldZoom(context, stage, pin); break;
            case "field-free-camera": FieldFreeCamera(context, stage, options, pin); break;
            case "field-camera-return": FieldCameraReturn(context, stage, pin); break;
            case "field-indicator-opacity": FieldOpacity(context, stage, pin); break;
            case "field-indicator-input": FieldInput(context, stage, pin); break;
            case "field-counter": FieldCounter(context, stage, pin); break;
            case "field-buffs": FieldBuffs(context, stage, options, pin); break;
            default: return false;
        }
        return true;
    }
    private static void FieldZoom(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        using var cine = AssemblyDefinition.ReadAssembly(Path.Combine(context.Root, ".research/extracted/Cinemachine.dll.dll"));
        using var core = AssemblyDefinition.ReadAssembly(Path.Combine(context.Root, ".research/extracted/mscorlib.dll.dll"));
        // Field dice: pooled controllers, root placed once by ThrowDice, animation only on the Container child.
        var dicePool = native.MainModule.GetType("Core.DiceManager").Fields.SingleOrDefault(field => field.Name == "_diceControllerPool");
        Check(dicePool?.FieldType.FullName == "Core.DiceControllerPool" && native.MainModule.GetType("Core.DiceControllerPool").BaseType.FullName == "Core.BasePool`1<DiceController>"
            && native.MainModule.GetType("Core.BasePool`1").Fields.Any(field => field.Name == "Cache" && field.FieldType.FullName.StartsWith("System.Collections.Generic.Dictionary`2<T,System.Boolean>", StringComparison.Ordinal)), "Native dice controller pool cache");
        var throwDice = native.MainModule.GetType("Core.DiceManager").NestedTypes.Single(nested => nested.Name.StartsWith("<ThrowDice>", StringComparison.Ordinal)).Methods.Single(method => method.Name == "MoveNext");
        Check(throwDice.Body.Instructions.Count(instruction => instruction.Operand is MethodReference reference && reference.Name == "ReadyDiceData") >= 2
            && throwDice.Body.Instructions.Any(instruction => instruction.Operand is float value && value == 48f), "Native dice root placed above the player by ThrowDice");
        Check(Operands(NativeMethod(native, "DiceController", "Awake")).Contains("DiceController::Container", StringComparison.Ordinal)
            && Operands(NativeMethod(native, "DiceController", "Awake")).Contains("Animator", StringComparison.Ordinal), "Native dice animator lives on the Container child, not the moved root");
        Contains(ReadSource(context, "src/FieldDiceUi.cs"), new[] { "\"_diceControllerPool\"", "\"Cache\"", "FieldZoomUi.DiceViewDelta" }, "Dice follow the camera displacement from the native default view");
        foreach (var (type, name) in new[] { ("Core.Scene.BattleSceneController", "freeCamera"), ("Core.Scene.BattleSceneController", "freeObject"), ("Core.Unit.LandManager", "NodeDict"), ("Core.Camera.CharacterCamera", "vCamera"), ("Core.FreeCameraObject", "status") })
            Check(native.MainModule.GetType(type).Fields.Single(field => field.Name == name).IsPublic, "Public camera field: " + type + "." + name);
        var follow = cine.MainModule.GetType("Cinemachine.CinemachineTransposer").Fields.Single(field => field.Name == "m_FollowOffset");
        Check(follow.IsPublic && follow.FieldType.FullName == "UnityEngine.Vector3", "Native follow offset Vector3");
        ExactField(cine, "Cinemachine.CinemachineBlend", "CamB", "Cinemachine.ICinemachineCamera");
        Check(!cine.MainModule.GetType("Cinemachine.CinemachineBrain/BrainFrame").IsValueType
            && cine.MainModule.GetType("Cinemachine.CinemachineBrain").Fields.Single(field => field.Name == "mFrameStack").FieldType.FullName == "System.Collections.Generic.List`1<Cinemachine.CinemachineBrain/BrainFrame>", "BrainFrame reference identity and frame stack");
        foreach (var name in new[] { "Duration", "TimeInBlend" }) ExactField(cine, "Cinemachine.CinemachineBlend", name, "System.Single");
        foreach (var (type, name, value) in new[] { ("Cinemachine.CinemachineTransposer/BindingMode", "WorldSpace", 4), ("Cinemachine.CinemachineCore/Stage", "Body", 0), ("Cinemachine.CinemachineCore/Stage", "Aim", 1) })
            Check((int)cine.MainModule.GetType(type).Fields.Single(field => field.Name == name).Constant == value, "Native camera enum: " + type + "." + name);
        var gate = native.MainModule.GetType("FairyGUI.Stage").Methods.Single(method => method.Name == "get_isTouchOnUI"); Check(gate.IsStatic && gate.ReturnType.FullName == "System.Boolean", "Native UI wheel gate");
        foreach (var (type, name, result) in new[] { ("FairyGUI.GRoot", "get_focus", "FairyGUI.GObject"), ("FairyGUI.GObject", "get_asCom", "FairyGUI.GComponent"),
            ("FairyGUI.GComponent", "get_scrollPane", "FairyGUI.ScrollPane"), ("FairyGUI.ScrollPane", "get_touchEffect", "System.Boolean"), ("FairyGUI.ScrollPane", "get_mouseWheelEnabled", "System.Boolean"),
            ("FairyGUI.GObject", "get_asTextInput", "FairyGUI.GTextInput"), ("FairyGUI.GObject", "get_focused", "System.Boolean"), ("FairyGUI.GTextInput", "get_mouseWheelEnabled", "System.Boolean") }) ExactMethod(native, type, name, result);
        var wheel = NativeMethod(native, "FairyGUI.ScrollPane", "__mouseWheel").Body.Instructions;
        Check(((FieldReference)wheel[1].Operand).Name == "_touchEffect" && wheel[2].OpCode.Name.StartsWith("brtrue", StringComparison.Ordinal) && wheel[3].OpCode.Name == "ret"
            && ((FieldReference)wheel[5].Operand).Name == "_mouseWheelEnabled" && wheel[6].OpCode.Name.StartsWith("brtrue", StringComparison.Ordinal) && wheel[7].OpCode.Name == "ret", "Native wheel ownership needs touch and wheel together");
        foreach (var name in new[] { "com_AttrInfos", "com_PlayerAttrInfos" }) ExactField(native, "UI.UIBattleInfoPanel", name, "FairyGUI.GComponent");
        foreach (var type in new[] { "Core.Camera.CameraManager", "Core.Unit.LandManager", "UI.UIManager" }) Check(native.MainModule.GetType(type).BaseType.GetElementType().FullName == "Tools.SimpleSingletonProvider`1"
            && native.MainModule.GetType("Tools.SimpleSingletonProvider`1").Fields.Any(field => field.Name == "_inst" && field.IsStatic), "Native singleton storage: " + type);
        foreach (var name in new[] { "GetCurPlayerCamera", "GetHostStatus" }) Check(NativeMethod(native, "Core.Camera.CameraManager", name).IsPublic, "Public camera reader: " + name);
        Check(native.MainModule.GetType("UI.UIManager").BaseType.FullName == "Tools.SimpleSingletonProvider`1<UI.UIManager>", "Native UI manager singleton");
        ExactField(native, "UI.UIManager", "propUpWindows", "System.Collections.Generic.Stack`1<UI.BaseWindow>");
        foreach (var name in new[] { "ExpressionWindow", "ExpressionListWindow", "OperateTimeWindow", "TipsWindow" }) Check(native.MainModule.GetType("UI." + name).BaseType.FullName == "UI.BaseWindow", "Native passive window: " + name);
        var tips = native.MainModule.GetType("UI.TipsWindow"); Check(!tips.Methods.Any(method => method.Name == "OnShown"), "Passive tips lifetime has no OnShown override");
        Check(tips.NestedTypes.Where(type => type.Name.StartsWith("<TryShowAsync>", StringComparison.Ordinal)).SelectMany(type => type.Methods.Where(method => method.Name == "MoveNext")).SelectMany(Calls).Contains("System.Void FairyGUI.Window::Show()", StringComparer.Ordinal), "Passive tips show native container");
        foreach (var name in new[] { "HideTopTip", "HideThinkingTip" }) {
            var calls = Calls(NativeMethod(native, "UI.TipsWindow", name));
            Check(calls.Contains("System.Void FairyGUI.GObject::set_visible(System.Boolean)", StringComparer.Ordinal) && !calls.Any(call => Regex.IsMatch(call, @"::Hide\(|::HideImmediately\(")), "Passive tips only hide children: " + name);
        }
        foreach (var (type, name, result) in new[] { ("System.Collections.Generic.Stack`1", "get_Count", "System.Int32"), ("System.Collections.Generic.Stack`1", "GetEnumerator", "System.Collections.Generic.Stack`1/Enumerator<T>"),
            ("System.Collections.Generic.Stack`1/Enumerator", "MoveNext", "System.Boolean"), ("System.Collections.Generic.Stack`1/Enumerator", "get_Current", "T"), ("System.Collections.Generic.Stack`1/Enumerator", "Dispose", "System.Void") }) ExactMethod(core, type, name, result);
        var coordinator = ReadSource(context, "src/ModUi.cs");
        var blocked = Regex.Match(coordinator, "(?s)if\\s*\\(\\s*!\\s*Compatibility\\.Allowed\\(\"CoreUi\"\\)\\s*\\)\\s*\\{(?<body>.*?)\\n        \\}");
        var cleanup = Regex.Match(blocked.Groups["body"].Value, @"\btry\s*\{\s*FieldZoomUi\.Clear\(\)\s*;\s*\}\s*catch\s*\(Exception ex\)");
        var returning = Regex.Match(blocked.Groups["body"].Value, @"\breturn\s*;"); Check(blocked.Success && cleanup.Success && returning.Success && cleanup.Index <= returning.Index, "Blocked CoreUi retries cleanup before return");
        var runtime = ReadSource(context, "src/RuntimeObject.cs"); var gameUi = ReadSource(context, "src/GameUi.cs");
        var source = WithoutNamespace(ReadSource(context, "src/FieldZoomUi.cs")).Replace("using UnityEngine;", "", StringComparison.Ordinal);
        NoPattern(source, @"Set\(|ActiveCamera|EnableFreeCamera|GetAxis|Send|Dispatch|Register|\.position\s*=|\.rotation\s*=|fieldOfView\s*=", "Zoom changes no native movement/FOV/input/callbacks");
        Check(Regex.Matches(source, "SetField\\(\"([^\"]+)\"").All(match => match.Groups[1].Value == "m_FollowOffset"), "Only follow offset field written");
        var harness = Template(context, "focus-tests/field-zoom/RuntimeFixture.template.cs", pin)
            .Replace("__GEOMETRY__", WithoutNamespace(ReadSource(context, "src/FieldZoomGeometry.cs")), StringComparison.Ordinal)
            .Replace("__SOURCE__", source, StringComparison.Ordinal)
            .Replace("__POINTER_PATH__", MethodFragment(gameUi, "internal static IReadOnlyList<RuntimeObject> PointerPath"), StringComparison.Ordinal)
            .Replace("__VALUE_CHECK__", MethodFragment(runtime, "private static void CheckValue<T>"), StringComparison.Ordinal)
            .Replace("__FIELD_GET__", MethodFragment(runtime, "internal T Field<T>"), StringComparison.Ordinal)
            .Replace("__FIELD_SET__", MethodFragment(runtime, "internal void SetField<T>"), StringComparison.Ordinal);
        RunEmbedded(context, stage, "field-zoom", harness, "FieldZoomTest.Check.Run()", pin);
    }
}
