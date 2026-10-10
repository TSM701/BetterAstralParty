using Cinemachine;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BetterAstralParty;

// An owned Cinemachine override holds the free view while native field cameras switch.
internal static class FieldFreeCamera
{
    private static GameObject? _anchor, _cameraObject;
    private static CinemachineVirtualCamera? _camera;
    private static CinemachineBrain? _brain;
    private static RuntimeObject? _scene;
    private static int _override = -1;
    private static bool _returnPending, _dragging, _indicatorPress, _acquireUncertain;
    private static Vector2 _pointer, _pressAt;
    internal static bool Holding => _camera != null;
    internal static RuntimeObject? Camera => _camera == null ? null : new RuntimeObject(_camera.Pointer);

    internal static void Enter(RuntimeObject scene, RuntimeObject brain, RuntimeObject active, RuntimeObject body, UnityEngine.Camera output)
    {
        if (Holding) return;
        _scene = scene; _brain = new CinemachineBrain(brain.Pointer);
        try
        {
            _anchor = new GameObject("BetterAstralParty.FreeCameraAnchor");
            _cameraObject = new GameObject("BetterAstralParty.FreeCamera");
            _cameraObject.SetActive(false);
            _camera = _cameraObject.AddComponent(Il2CppType.Of<CinemachineVirtualCamera>()).Cast<CinemachineVirtualCamera>();
            var source = new CinemachineVirtualCamera(active.Pointer);
            var lens = source.m_Lens;
            lens.Dutch = 0; // The rendered rotation below already includes the native lens roll.
            _camera.m_Lens = lens;
            _camera.Priority = int.MinValue;
            _camera.m_StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
            _camera.Follow = _anchor.transform;
            var transposer = _camera.GetComponentOwner().gameObject.AddComponent(Il2CppType.Of<CinemachineTransposer>()).Cast<CinemachineTransposer>();
            var nativeBody = new CinemachineTransposer(body.Pointer);
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
            transposer.m_FollowOffset = nativeBody.m_FollowOffset;
            transposer.m_XDamping = nativeBody.m_XDamping;
            transposer.m_YDamping = nativeBody.m_YDamping;
            transposer.m_ZDamping = nativeBody.m_ZDamping;
            // Start at the rendered pose, even while the native Follow target is still damping.
            _anchor.transform.position = output.transform.position - transposer.m_FollowOffset;
            _camera.transform.SetPositionAndRotation(output.transform.position, output.transform.rotation);
            _camera.InvalidateComponentPipeline();
            _cameraObject.SetActive(true);
            _camera.ForceCameraPosition(output.transform.position, output.transform.rotation);
            Acquire();
        }
        catch { Clear(); throw; }
    }

    internal static bool Update(RuntimeObject scene, RuntimeObject brain, RuntimeObject? free, RuntimeObject? player, bool blocked)
    {
        if (!Holding) return false;
        if (_scene?.Pointer != scene.Pointer || !_anchor || !_camera || !_brain || _brain!.Pointer != brain.Pointer)
        { Clear(); return false; }
        var stack = _brain!.mFrameStack;
        var count = stack?.Count ?? 0;
        if (count < 1 || count > 64) { Suspend(); return false; }
        var native = stack![0].blend?.CamB;
        var nativePointer = native?.Pointer ?? IntPtr.Zero;
        // Field character/card cameras may change while the owned view stays fixed.
        var supported = nativePointer != IntPtr.Zero
            && new RuntimeObject(nativePointer).TypeName == "CinemachineVirtualCamera"
            && new CinemachineVirtualCamera(nativePointer);
        // The top owned override masks IsBlending; inspect the native base frame instead.
        if (blocked || !supported || count != (_override >= 0 ? 2 : 1)
            || _override >= 0 && (stack[1].id != _override || stack[1].blend?.CamB?.Pointer != _camera!.Pointer))
        { Suspend(); return false; }
        if (_returnPending)
        {
            Suspend();
            if (free?.Call("GetHostStatus")?.Value<bool>() != false
                || stack[0].blend is { IsComplete: false }) return false;
            var manager = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Camera", "CameraManager"), "_inst");
            var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
            var character = logic?.Get("battle")?.Call("GetCurrentPlayer")?.Field("CharacterInst")?.Field("vCamera");
            if (manager == null || character == null) return false;
            FieldZoomUi.Clear();
            manager.Call("CancelFreeStatus");
            manager.Call("SwitchCamera", character);
            return false;
        }
        if (_override < 0)
        {
            if (stack[0].blend is { IsComplete: false }) return false;
            Acquire();
        }
        return true;
    }

    private static void Acquire()
    {
        // shortcut: native camera waits see the complete override and may finish early; validate effect timing in game.
        _cameraObject!.SetActive(true);
        _acquireUncertain = true;
        _override = _brain!.SetCameraOverride(-1, null, _camera!.Cast<ICinemachineCamera>(), 1f, -1f);
        if (_override < 0) throw new InvalidOperationException("Owned field camera override unavailable");
        _acquireUncertain = false;
    }

    internal static void Pan(RuntimeObject free, bool allowed, bool onUi, bool fieldIndicator = false)
    {
        if (!allowed || _override < 0 || !_anchor || !Application.isFocused || Input.touchCount != 0
            || Input.GetMouseButton(1) || Input.GetMouseButton(2))
        { _dragging = _indicatorPress = false; return; }
        var settings = RuntimeObject.FindClass("Core", "GameSettings");
        var delta = new Vector3();
        var transform = new Component(free.Pointer).transform;
        if (RuntimeObject.StaticField(settings, "MouseControl")!.Value<bool>() && Input.GetMouseButton(0))
        {
            var stageClass = RuntimeObject.FindClass("FairyGUI", "Stage");
            var stage = RuntimeObject.StaticCall(stageClass, "get_inst")!;
            var point = stage.Get<Vector2>("touchPosition");
            // UI blocks a new gesture, not a world-origin gesture already captured.
            if (Input.GetMouseButtonDown(0))
            {
                _dragging = _indicatorPress = false;
                if (!onUi || fieldIndicator
                    && RuntimeObject.StaticCall(stageClass, "get_touchScreen")?.Value<bool>() == false)
                { _pointer = _pressAt = point; _dragging = true; _indicatorPress = onUi; }
            }
            if (_dragging)
            {
                var distance = point - _pointer;
                if (_indicatorPress)
                {
                    var moved = point - _pressAt;
                    var threshold = RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "UIConfig"), "clickDragSensitivity")!.Value<int>();
                    if (threshold >= 0 && float.IsFinite(moved.x) && float.IsFinite(moved.y)
                        && moved.sqrMagnitude > threshold * (float)threshold)
                    {
                        // Desktop touch ID is 0; cancel the native buff click before moving its camera.
                        stage.Call("CancelClick", 0);
                        _indicatorPress = false;
                    }
                    else distance = new Vector2();
                }
                var sensitivity = free.Field<float>("CameraSensitivity");
                if (float.IsFinite(sensitivity) && sensitivity > 0)
                    delta += (transform.forward * distance.y - transform.right * distance.x) * sensitivity;
            }
            _pointer = point;
        }
        else _dragging = _indicatorPress = false;
        if (!onUi && RuntimeObject.StaticField(settings, "KeyControl")!.Value<bool>())
        {
            var speed = free.Field<float>("CameraSpeed");
            if (float.IsFinite(speed) && speed > 0 && float.IsFinite(Time.deltaTime) && Time.deltaTime > 0)
                delta += (transform.forward * Input.GetAxis("Vertical") + transform.right * Input.GetAxis("Horizontal")) * speed * Math.Min(Time.deltaTime, .1f);
        }
        delta.y = 0;
        if (!float.IsFinite(delta.x) || !float.IsFinite(delta.z) || delta.sqrMagnitude == 0
            || !FieldZoomUi.TryPanBounds(out var bounds)) return;
        var position = _anchor!.transform.position + delta;
        position.x = Math.Clamp(position.x, bounds.min.x, bounds.max.x);
        position.z = Math.Clamp(position.z, bounds.min.z, bounds.max.z);
        _anchor.transform.position = position;
    }

    internal static void Return() { _returnPending = true; Suspend(); }

    private static void Suspend()
    {
        _dragging = _indicatorPress = false;
        // SetCameraOverride can insert its frame before throwing; recover only our camera's ID.
        if (_acquireUncertain && _brain && _camera)
        {
            var stack = _brain!.mFrameStack;
            if (stack == null || stack.Count < 1 || stack.Count > 64)
                throw new InvalidOperationException("Owned field camera override cleanup unavailable");
            var recoveredId = -1;
            for (var i = 1; i < stack.Count; i++)
                if (stack[i].blend?.CamB?.Pointer == _camera!.Pointer)
                {
                    if (recoveredId >= 0 || stack[i].id < 0)
                        throw new InvalidOperationException("Ambiguous owned field camera override");
                    recoveredId = stack[i].id;
                }
            _override = recoveredId;
            _acquireUncertain = false;
        }
        if (_override >= 0 && _brain) _brain!.ReleaseCameraOverride(_override);
        _override = -1;
        if (_cameraObject) _cameraObject!.SetActive(false);
    }

    internal static void Clear()
    {
        // Retain the ID/resources for cleanup retry if releasing the native override fails.
        Suspend();
        if (_cameraObject) UnityEngine.Object.Destroy(_cameraObject);
        if (_anchor) UnityEngine.Object.Destroy(_anchor);
        _cameraObject = _anchor = null; _camera = null; _brain = null; _scene = null;
        _returnPending = _dragging = _indicatorPress = false;
    }
}
