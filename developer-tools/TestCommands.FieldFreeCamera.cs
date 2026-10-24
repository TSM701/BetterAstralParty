using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    // A FairyGUI drag left behind after its object missed the touch end must stop gating input once the button is up.
    private const string DragStateCheck = """
internal static class DragStateCheck
{
    private static void Check(bool ok, string name) { if (!ok) throw new System.Exception(name); System.Console.WriteLine("PASS: " + name); }
    internal static void Run()
    {
        System.IntPtr card = (System.IntPtr)7, other = (System.IntPtr)9;
        Check(DragState.Live(card, true), "a drag under a held button is live");
        Check(!DragState.Live(card, false), "a drag still set after release is stale");
        Check(!DragState.Live(card, true), "a later press does not revive the stale drag");
        Check(DragState.Live(other, true), "a new native drag replaces the stale one");
        Check(!DragState.Live(System.IntPtr.Zero, true) && DragState.Live(card, true), "a cleared drag forgets the stale object");
    }
}
""";

    private static void FieldFreeCamera(DevContext context, string stage, Dictionary<string, string> options, Action<string> pin)
    {
        RunGeneralFixture(context, Path.Combine(stage, "drag-state"), GeneralWithoutNamespace(ReadSource(context, "src/DragState.cs")) + "\n" + DragStateCheck, "DragStateCheck.Run()", pin);
        using var native = Native(context);
        using var cine = AssemblyDefinition.ReadAssembly(Path.Combine(context.Root, ".research/extracted/Cinemachine.dll.dll"));
        var game = options.TryGetValue("game-root", out var suppliedGame) ? Path.GetFullPath(suppliedGame) : Path.Combine(Directory.GetParent(context.Root)!.FullName, "8vJXnINT");
        var interopRoot = options.TryGetValue("interop-root", out var suppliedInterop) ? Path.GetFullPath(suppliedInterop) : Path.Combine(game, "BepInEx", "interop");
        var interopPath = Path.Combine(interopRoot, "Cinemachine.dll"); pin(interopPath);
        using var interop = AssemblyDefinition.ReadAssembly(interopPath);
        using var unity = AssemblyDefinition.ReadAssembly(Path.Combine(context.Root, ".research/extracted/UnityEngine.CoreModule.dll.dll"));
        foreach (var assembly in new[] { cine, interop }) {
            var set = ExactMethod(assembly, "Cinemachine.CinemachineBrain", "SetCameraOverride", "System.Int32", "System.Int32", "Cinemachine.ICinemachineCamera", "Cinemachine.ICinemachineCamera", "System.Single", "System.Single");
            var release = ExactMethod(assembly, "Cinemachine.CinemachineBrain", "ReleaseCameraOverride", "System.Void", "System.Int32");
            ExactMethod(assembly, "Cinemachine.CinemachineVirtualCamera", "GetComponentOwner", "UnityEngine.Transform");
            ExactMethod(assembly, "Cinemachine.CinemachineVirtualCamera", "InvalidateComponentPipeline", "System.Void");
            ExactMethod(assembly, "Cinemachine.CinemachineVirtualCamera", "ForceCameraPosition", "System.Void", "UnityEngine.Vector3", "UnityEngine.Quaternion");
            ExactField(assembly, "Cinemachine.LensSettings", "Dutch", "System.Single");
            if (assembly == interop) foreach (var method in new[] { set, release }) Check(method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "il2cpp_runtime_invoke"), "Camera interop uses supported invoke bridge");
        }
        foreach (var (type, name, result, visible) in new[] { ("Cinemachine.CinemachineBrain", "mFrameStack", "System.Collections.Generic.List`1<Cinemachine.CinemachineBrain/BrainFrame>", false),
            ("Cinemachine.CinemachineBrain/BrainFrame", "id", "System.Int32", true), ("Cinemachine.CinemachineBrain/BrainFrame", "blend", "Cinemachine.CinemachineBlend", true),
            ("Cinemachine.CinemachineBlend", "CamB", "Cinemachine.ICinemachineCamera", true), ("Cinemachine.CinemachineVirtualCamera", "m_Lens", "Cinemachine.LensSettings", true),
            ("Cinemachine.CinemachineTransposer", "m_FollowOffset", "UnityEngine.Vector3", true) }) ExactField(cine, type, name, result, visible);
        foreach (var name in new[] { "m_XDamping", "m_YDamping", "m_ZDamping" }) ExactField(cine, "Cinemachine.CinemachineTransposer", name, "System.Single");
        foreach (var (type, name, result, visible, isStatic) in new[] {
            ("Core.Scene.BattleSceneController", "freeCamera", "Cinemachine.CinemachineVirtualCamera", true, false), ("Core.Scene.BattleSceneController", "freeObject", "Core.FreeCameraObject", true, false),
            ("Core.FreeCameraObject", "status", "Core.FreeCameraStatus", true, false), ("Core.FreeCameraObject", "CameraSensitivity", "System.Single", false, false), ("Core.FreeCameraObject", "CameraSpeed", "System.Single", false, false),
            ("Core.GameSettings", "MouseControl", "System.Boolean", true, true), ("Core.GameSettings", "KeyControl", "System.Boolean", true, true), ("GameLogic.BattleLogic", "clientFinishReady", "System.Boolean", true, false),
            ("GameLogic.FightLogic", "fightStatus", "System.Boolean", true, false), ("GameLogic.BattlePlayerData", "CharacterInst", "Core.Unit.Character", true, false), ("Core.Unit.Character", "vCamera", "Core.Camera.CharacterCamera", true, false),
            ("Core.Camera.CharacterCamera", "vCamera", "Cinemachine.CinemachineVirtualCamera", true, false) }) ExactField(native, type, name, result, visible, isStatic);
        ExactMethod(native, "GameLogic.BattleLogic", "GetCurrentPlayer", "GameLogic.BattlePlayerData");
        ExactMethod(native, "Core.Camera.CameraManager", "CancelFreeStatus", "System.Void");
        ExactMethod(native, "Core.Camera.CameraManager", "SwitchCamera", "Cysharp.Threading.Tasks.UniTask`1<System.Boolean>", "Core.Camera.CharacterCamera");
        Pattern(Body(ExactMethod(native, "Core.FreeCameraObject", "GetHostStatus", "System.Boolean")), @"ldfld Core.FreeCameraStatus Core.FreeCameraObject::status[\s\S]*ldc.i4.2\nIL_0009: beq.s IL_0014[\s\S]*ldc.i4.3\nIL_000d: ceq\nIL_000f: ldc.i4.0\nIL_0010: ceq\nIL_0012: br.s IL_0015\nIL_0014: ldc.i4.0\nIL_0015: ldc.i4.0\nIL_0016: ceq\nIL_0018: ret", "Host reader protects queued return during System/MapSignal");
        ExactMethod(unity, "UnityEngine.GameObject", "AddComponent", "UnityEngine.Component", "System.Type");
        var writes = ExactMethod(native, "FairyGUI.Stage", "CancelClick", "System.Void", "System.Int32").Body.Instructions.Where(instruction => instruction.OpCode.Name == "stfld").Select(instruction => ((FieldReference)instruction.Operand).FullName).ToArray();
        Check(writes.Length == 1 && writes[0] == "System.Boolean FairyGUI.TouchInfo::clickCancelled", "CancelClick writes only matching touch click flag");
        ExactMethod(native, "FairyGUI.GObject", "get_draggable", "System.Boolean"); ExactField(native, "FairyGUI.UIConfig", "clickDragSensitivity", "System.Int32", true, true);
        var screen = native.MainModule.GetType("FairyGUI.Stage").Methods.Where(method => method.Name == "get_touchScreen" && method.IsPublic && method.IsStatic && method.ReturnType.FullName == "System.Boolean" && method.Parameters.Count == 0).ToArray(); Check(screen.Length == 1, "Desktop input mode static reader");
        Pattern(Body(NativeMethod(native, "FairyGUI.Stage", "ResetInputState")), @"ldc.i4.0\nIL_[0-9a-f]+: ldelem.ref\nIL_[0-9a-f]+: ldc.i4.0\nIL_[0-9a-f]+: stfld System.Int32 FairyGUI.TouchInfo::touchId", "Desktop touch ID is zero");
        Check(ExactMethod(native, "FairyGUI.Stage", "get_touchPosition", "UnityEngine.Vector2").Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "UpdateTouchPosition"), "Gesture samples freshly updated touch position");
        Check(native.MainModule.GetType("FairyGUI.GObject").Methods.Count(method => method.Name == "get_draggingObject" && method.IsPublic && method.IsStatic && !method.HasGenericParameters && method.ReturnType.FullName == "FairyGUI.GObject" && method.Parameters.Count == 0) == 1, "Static native drag ownership reader");
        foreach (var (name, value) in new[] { ("None", 0), ("Player", 1), ("System", 2), ("MapSignal", 3) }) Check((int)native.MainModule.GetType("Core.FreeCameraStatus").Fields.Single(field => field.Name == name).Constant == value, "Native free-camera host state: " + name);
        foreach (var (type, method, call) in new[] { ("Core.ActionEffectShow", "PlayPlayerShow", "LimitCamera"), ("Core.ActionEffectShow", "LimitCamera", "HostFreeCamera"), ("Core.Camera.CameraManager", "HostFreeCamera", "SetHostCamera") })
            Check(NativeMethod(native, type, method).Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == call), "FIELD host-control chain: " + method + "→" + call);
        // Free view ends only on clicks whose native handler moves the camera.
        Check(native.MainModule.GetType("FairyGUI.GObject").Fields.Any(field => field.Name == "name" && field.IsPublic && field.FieldType.FullName == "System.String")
            && !native.MainModule.GetType("FairyGUI.GObject").Properties.Any(property => property.Name == "name"), "GObject.name is a public field, read with Field");
        Contains(ReadSource(context, "src/FieldFreeCamera.cs"), new[] { "item.Field(\"name\")" }, "Player head detection reads the native name field");
        var listen = NativeMethod(native, "UI.UIBattleInfo_Button_PlayerInfo", "AddListener").Body.Instructions.Select(instruction => instruction.Operand).OfType<MemberReference>().Select(reference => reference.Name).ToList();
        Check(listen.IndexOf("btn_Head") >= 0 && listen.IndexOf("SwitchCamera") == listen.IndexOf("btn_Head") + 2 && listen.IndexOf("btn_Label") >= 0 && listen.IndexOf("ShowBattlePlayerInfo") == listen.IndexOf("btn_Label") + 2, "Only the player head moves the camera; the label opens details");
        foreach (var (type, method, call) in new[] { ("UI.UIBattleInfo_Button_PlayerInfo", "SwitchCamera", "MoveCameraToPlayer"), ("UI.UIBattleInfo_Com_Player", "OnClickPlayer", "ControlFreeCamera"), ("UI.BattleInfoPanel", "OnClickMiniMap", "ControlFreeCamera") })
            Check(NativeMethod(native, type, method).Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == call), "Focus click handler moves the camera: " + type + "." + method + "→" + call);
        Check(native.MainModule.GetType("UI.BattleMonsterWindow").NestedTypes.SelectMany(nested => nested.Methods).Any(method => method.HasBody && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "SwitchCamera"))
            && native.MainModule.GetType("UI.UIButton_MonsterItem") != null && native.MainModule.GetType("UI.UIBattleInfo_Com_Minimap") != null, "Monster target item click switches the camera; focus UI types exist");
        Contains(ReadSource(context, "src/FieldFreeCamera.cs"), new[] { "{ PlayerInfo, \"UIButton_MonsterItem\", \"UIBattleInfo_Com_Minimap\" }", "PlayerInfo = \"UIBattleInfo_Button_PlayerInfo\", PlayerHead = \"btn_Head\"" }, "Free view releases only for audited focus UI");
        var host = NativeMethod(native, "Core.FreeCameraObject", "SetHostCamera").Body.Instructions;
        Check(host.Any(instruction => instruction.OpCode.Name == "ldc.i4.2") && host.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "UpdateStatus"), "FIELD effects set System host status");
        var manual = NativeMethod(cine, "Cinemachine.CinemachineBrain", "ManualUpdate").Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().Select(reference => reference.Name).ToArray();
        Check(Array.IndexOf(manual, "UpdateFrame0") >= 0 && Array.IndexOf(manual, "UpdateFrame0") < Array.IndexOf(manual, "ComputeCurrentBlend") && Array.IndexOf(manual, "ComputeCurrentBlend") < Array.IndexOf(manual, "ProcessActiveCamera"), "Native base frame advances before blend and output");
        var switching = NativeAsyncMoveNext(native, "Core.Camera.CameraManager", "SwitchCamera", 1);
        var waits = switching.Body.Instructions.Where(instruction => instruction.OpCode.Name is "ldftn" or "ldvirtftn").Select(instruction => instruction.Operand).OfType<MethodReference>()
            .Where(method => method.Name.StartsWith("<SwitchCamera>b__", StringComparison.Ordinal) && method.Parameters.Count == 0 && method.ReturnType.FullName == "System.Boolean")
            .Select(method => method.Resolve()).Where(method => method.DeclaringType.DeclaringType?.FullName == "Core.Camera.CameraManager" && method.HasBody
                && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call && call.FullName == "System.Boolean Cinemachine.CinemachineBrain::get_IsBlending()"))
            .Distinct().ToArray();
        Check(waits.Length == 1, "Native camera wait predicate is the exact callback created by SwitchCamera"); var wait = waits[0];
        Pattern(Body(wait), @"op_Inequality.*\nIL_0024: brfalse.s IL_0036[\s\S]*get_IsBlending\(\)\nIL_0035: ret\nIL_0036: ldc.i4.0\nIL_0037: ret", "Native wait AND predicate");
        var source = WithoutNamespace(ReadSource(context, "src/FieldFreeCamera.cs")).Replace("using Cinemachine;", "", StringComparison.Ordinal).Replace("using Il2CppInterop.Runtime;", "", StringComparison.Ordinal).Replace("using UnityEngine;", "", StringComparison.Ordinal);
        NoPattern(source, @"Harmony|Detour|Marshal\.|GetFunctionPointer|il2cpp_|\.SetField\(|\.Set\(|\b(Send|Dispatch|Register)\s*\(", "Free camera changes no gameplay/status/settings or hooks");
        NoPattern(source, @"(?m)^\s*(?!_camera\.Priority\s*=)[A-Za-z_][\w.]*\.Priority\s*=", "Only owned camera sets priority");
        var caller = ReadSource(context, "src/FieldZoomUi.cs");
        foreach (var pattern in new[] {
            "!Plugin\\.FieldZoom\\.Value\\s*\\|\\|\\s*PvpSafety\\.Suspended\\s*\\|\\|\\s*root\\s*==\\s*null",
            "!Alive\\(scene\\)\\s*\\|\\|\\s*!VisibleCombatAdvisor\\.IsPve",
            "var blocked\\s*=\\s*status is not \\(0 or 1 or 2 or 3\\)\\s*\\|\\|\\s*!Alive\\(cameraObject\\)",
            "!new Camera\\(cameraObject!\\.Pointer\\)\\.enabled",
            "!new Camera\\(cameraObject\\.Pointer\\)\\.gameObject\\.activeInHierarchy",
            "logic\\?\\.Get\\(\"battle\"\\)\\?\\.Field<bool>\\(\"clientFinishReady\"\\)\\s*!=\\s*true",
            "frames\\?\\.Get<int>\\(\"Count\"\\)\\s*!=\\s*1",
            "blend\\?\\.Field\\(\"CamB\"\\)\\?\\.Pointer\\s*!=\\s*active\\.Pointer",
            "brain!\\.Get<bool>\\(\"IsBlending\"\\)\\s*\\|\\|\\s*!blend\\.Get<bool>\\(\"IsComplete\"\\)",
            "GameUi\\.Find\\(root,\\s*\"FightWindow\",\\s*maxDepth:\\s*1\\)\\s*is\\s*\\{\\s*\\}\\s*fight\\s*&&\\s*!EncounterHidden\\(fight\\)",
            "root\\.Get\\(\"touchTarget\"\\)\\?\\.Get<bool>\\(\"touchable\"\\)\\s*==\\s*true",
            "FieldFreeCamera\\.Update\\(scene,\\s*brain!,\\s*free,\\s*player,\\s*blocked\\)",
            "var inputAllowed\\s*=\\s*FieldInputAllowed\\(root,\\s*pan:\\s*true\\)",
            "!Application\\.isFocused\\s*\\|\\|\\s*ModUi\\.IsOpen\\s*\\|\\|\\s*Input\\.touchCount\\s*!=\\s*0\\s*\\|\\|\\s*root\\.Get<bool>\\(\"modalWaiting\"\\)",
            "GameUi\\.LiveDrag\\(\\)\\s*!=\\s*null",
            "root\\.Get\\(\"focus\"\\)\\?\\.Get\\(\"asTextInput\"\\)\\?\\.Get<bool>\\(\"focused\"\\)\\s*==\\s*true",
            "pane\\.Get<bool>\\(\"touchEffect\"\\)\\s*&&\\s*\\(pan\\s*\\|\\|\\s*pane\\.Get<bool>\\(\"mouseWheelEnabled\"\\)\\)",
            "\"get_isTouchOnUI\"",
            "FieldFreeCamera\\.Pan\\(free!,\\s*inputAllowed\\s*&&\\s*!FieldCameraReturnUi\\.ConsumedInput,\\s*onUi,\\s*indicator\\s*&&\\s*Input\\.GetMouseButtonDown\\(0\\)\\)",
            "if\\s*\\(held\\s*&&\\s*!FieldFreeCamera\\.Holding\\)\\s*\\{\\s*_pendingWheel\\s*=\\s*0;\\s*return;",
            "Attempt\\(FieldFreeCamera\\.Clear\\)",
            "!float\\.IsFinite\\(lo\\.x\\)",
            "!float\\.IsFinite\\(hi\\.z\\)",
            "lo\\.x\\s*>\\s*hi\\.x\\s*\\|\\|\\s*lo\\.y\\s*>\\s*hi\\.y\\s*\\|\\|\\s*lo\\.z\\s*>\\s*hi\\.z",
        }) Pattern(caller, pattern, "Free-camera caller: " + pattern);
        NoPattern(caller, "HasInputWindow|var hosted|root\\.Get<bool>\\(\"hasModalWindow\"\\)|root\\.Get<bool>\\(\"hasAnyPopup\"\\)", "Permitted field UI adds no second Pan-only lock");
        NoPattern(Regex.Match(caller, @"var blocked\s*=[\s\S]*?;").Value, @"FightWindow|fightStatus|!held\s*&&\s*hosted", "Preparation UI retains field camera");
        Pattern(source, "if\\s*\\(Input\\.GetMouseButtonDown\\(0\\)\\)\\s*\\{\\s*_dragging\\s*=\\s*_indicatorPress\\s*=\\s*false;\\s*if\\s*\\(!onUi\\s*\\|\\|\\s*fieldIndicator", "Fresh indicator/world gestures are distinct");
        Pattern(source, "stage\\.Call\\(\"CancelClick\",\\s*0\\)", "Native moved-click cancellation"); Pattern(source, @"moved\.sqrMagnitude\s*>\s*threshold\s*\*\s*\(float\)threshold", "Native click-drag threshold");
        Pattern(source, "free\\?\\.Call\\(\"GetHostStatus\"\\)\\?\\.Value<bool>\\(\\)\\s*!=\\s*false", "Queued return waits for host-input release");
        var harness = Template(context, "focus-tests/free-camera/FieldFreeCameraFixture.cs", pin).Replace("__PRODUCTION_SOURCE__", source, StringComparison.Ordinal);
        RunEmbedded(context, stage, "field-free-camera", harness, "FreeCameraTest.Check.Run()", pin, noWarn: "8602,8604");
    }
}
