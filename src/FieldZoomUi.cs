using UnityEngine;

namespace BetterAstralParty;

// Presentation only: native camera switching runs underneath an owned free-view override.
internal static class FieldZoomUi
{
    private static RuntimeObject? _scene, _body;
    private static Vector3 _original, _applied, _target;
    private static bool _owned;
    private static bool _fieldVisible;
    private static float _zoomOutLimit;
    private static readonly List<Renderer> Renderers = new();
    private static readonly List<FieldZoomGeometry.Point> Corners = new();
    private static IntPtr _nodes;
    private static int _nodeCount;
    private static float _scanAt;
    private static float _pendingWheel;
    private static IntPtr _wheelBrain, _wheelCamera, _wheelFrame;

    internal static void Tick()
    {
        try
        {
            if (!Compatibility.Allowed("FieldZoom")) { Clear(); return; }
            Update();
            var distance = _original.magnitude;
            FieldIndicatorOpacity.Tick(_fieldVisible && _owned && _zoomOutLimit > distance
                ? Math.Clamp((_applied.magnitude - distance) / (_zoomOutLimit - distance), 0, 1) : 0);
        }
        catch (Exception ex) { Compatibility.Block("FieldZoom", ex, Clear); }
    }

    private static void Update()
    {
        _fieldVisible = false;
        var root = GameUi.Root;
        if (!Plugin.FieldZoom.Value || PvpSafety.Suspended || root == null) { Clear(); return; }
        var scene = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Scene", "BattleSceneController"), "inst");
        if (scene?.Pointer != _scene?.Pointer) { Clear(); _scene = scene; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (!Alive(scene) || !VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)) { Clear(); return; }
        var brain = scene!.Get("cinemachineBrain");
        var free = scene.Field("freeObject");
        var status = free?.Field("status")?.Value<int>() ?? -1;
        if (!Alive(brain) || !Alive(free)) { Clear(); return; }
        var manager = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Camera", "CameraManager"), "_inst");
        var player = manager?.Call("GetCurPlayerCamera")?.Field("vCamera");
        var cameraObject = scene.Get("mainCamera");
        var held = FieldFreeCamera.Holding;
        // FightWindow opens before the battle platform; yield only when the field renderer stops.
        var blocked = status is not (0 or 1 or 2 or 3) || !Alive(cameraObject)
            || !new Camera(cameraObject!.Pointer).enabled || !new Camera(cameraObject.Pointer).gameObject.activeInHierarchy
            || logic?.Get("battle")?.Field<bool>("clientFinishReady") != true;
        var owned = FieldFreeCamera.Update(scene, brain!, free, player, blocked);
        FieldCameraReturnUi.Tick(FieldFreeCamera.Holding && !blocked);
        var wheel = Input.mouseScrollDelta.y;
        var wheelAllowed = wheel == 0 && _pendingWheel == 0 && Same(_target, _applied)
            || FieldInputAllowed(root) && !FieldCameraReturnUi.ConsumedInput
                && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2);
        if (!wheelAllowed) { _pendingWheel = 0; WheelState("input-owned"); }
        if (owned && !Compatibility.Allowed("FieldCameraReturn")) { Clear(); return; }
        if (held && !FieldFreeCamera.Holding) { _pendingWheel = 0; return; }
        if (blocked || FieldFreeCamera.Holding && !owned)
        {
            _pendingWheel = 0; WheelState(blocked ? "field-inactive" : "camera-yield");
            if (!FieldFreeCamera.Holding) Restore();
            _target = _applied; return;
        }
        var active = owned ? FieldFreeCamera.Camera : brain!.Get("ActiveVirtualCamera");
        if (!Alive(active) || active!.TypeName != "CinemachineVirtualCamera"
            || !owned && active.Pointer != scene.Field("freeCamera")?.Pointer && active.Pointer != player?.Pointer)
        { WheelState("camera-unsupported"); Clear(); return; }
        var body = active.Call("GetCinemachineComponent", 0); // Audited Cinemachine Stage.Body.
        if (!Alive(body) || body!.TypeName != "CinemachineTransposer" || !body.Get<bool>("IsValid")
            || body.Field("m_BindingMode")!.Value<int>() != 4 || active.Get("LookAt") != null
            || active.Call("GetCinemachineComponent", 1) != null) { WheelState("camera-body"); Clear(); return; }
        var follow = active.Get("Follow");
        if (!Alive(follow)) { _pendingWheel = 0; WheelState("camera-follow"); return; }
        var camera = new Camera(cameraObject!.Pointer);
        if (camera.orthographic || camera.usePhysicalProperties || camera.lensShift.x != 0 || camera.lensShift.y != 0)
        { _pendingWheel = 0; WheelState("camera-lens"); return; }
        var offset = body.Field<Vector3>("m_FollowOffset");
        if (!Finite(offset)) { _pendingWheel = 0; WheelState("camera-offset"); return; }
        _fieldVisible = true;
        if (!owned)
        {
            var frames = brain!.Field("mFrameStack");
            if (frames?.Get<int>("Count") != 1) { _pendingWheel = 0; WheelState("camera-stack"); Restore(); return; }
            var frame = frames.Call("get_Item", 0);
            var blend = frame?.Field("blend");
            if (_pendingWheel != 0 && (_wheelBrain != brain.Pointer || _wheelCamera != cameraObject.Pointer
                || _wheelFrame != frame?.Pointer)) _pendingWheel = 0;
            if (blend?.Field("CamB")?.Pointer != active.Pointer)
            { _pendingWheel = 0; WheelState("camera-stack"); Restore(); return; }
            if (brain!.Get<bool>("IsBlending") || !blend.Get<bool>("IsComplete"))
            {
                Restore();
                // Native blends use their own clock and can restart while the base rig changes.
                if (wheelAllowed && float.IsFinite(wheel) && wheel != 0)
                {
                    _pendingWheel = float.IsFinite(_pendingWheel + wheel) ? _pendingWheel + wheel : 0;
                    _wheelBrain = brain.Pointer; _wheelCamera = cameraObject.Pointer; _wheelFrame = frame!.Pointer;
                    WheelState("camera-transition");
                }
                return;
            }
        }
        if (_body?.Pointer != body.Pointer)
        {
            Restore(); _body = body; _original = _applied = _target = offset; _zoomOutLimit = 0;
        }
        else if (!Same(offset, _applied))
        {
            // A native/external change becomes the new baseline; never restore across it.
            _original = _applied = _target = offset; _owned = false; _zoomOutLimit = 0;
        }
        var inputAllowed = FieldInputAllowed(root, pan: true);
        var onUi = inputAllowed && Input.GetMouseButton(0)
            && RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_isTouchOnUI")!.Value<bool>();
        var indicator = onUi && Input.GetMouseButtonDown(0)
            && !FieldCameraReturnUi.ConsumedInput
            && PointerOnFieldIndicator(logic);
        FieldFreeCamera.Pan(free!, inputAllowed && !FieldCameraReturnUi.ConsumedInput, onUi, indicator && Input.GetMouseButtonDown(0));
        if (wheel == 0 && _pendingWheel == 0 && Same(_target, offset)) return;
        if (!wheelAllowed) { _target = offset; return; }
        var transform = camera.transform;
        var target = follow!.Get<Vector3>("position");
        // The rendered view basis includes Cinemachine orientation corrections; the rig transform need not match it.
        if (_pendingWheel != 0) WheelState("transition-resumed");
        wheel += _pendingWheel; _pendingWheel = 0;
        if (wheel != 0)
        {
            if (!FieldFreeCamera.Holding)
            {
                FieldCameraReturnUi.Tick(true);
                // Fit around the same rendered-pose anchor that Enter will create, not a damping native Follow.
                if (FieldCameraReturnUi.Visible) target = transform.position - offset;
            }
            // Fit the requested pose, not the still-damping rendered camera; retain every wheel tick.
            var position = target + _target;
            var right = transform.right; var up = transform.up; var forward = transform.forward;
            FieldZoomGeometry.Point Project(Vector3 v) => new(Vector3.Dot(v, right), Vector3.Dot(v, up), Vector3.Dot(v, forward));
            BoardCorners(position, right, up, forward, target.y);
            if (_target.sqrMagnitude != 0 && FieldZoomGeometry.TryStep(Corners, Project(_target), Project(target - position), camera.fieldOfView,
                camera.aspect, camera.nearClipPlane, camera.farClipPlane, _original.magnitude / _target.magnitude, wheel, out var scale, out var maximum))
            {
                _zoomOutLimit = maximum > 0 ? _target.magnitude * maximum : 0;
                var zoomTarget = _target * scale;
                if (!FieldFreeCamera.Holding)
                {
                    if (FieldCameraReturnUi.Visible)
                    {
                        FieldFreeCamera.Enter(scene, brain!, active, body, camera);
                        Restore();
                        active = FieldFreeCamera.Camera!;
                        body = active.Call("GetCinemachineComponent", 0)!;
                        _body = body; _original = _applied = offset = body.Field<Vector3>("m_FollowOffset");
                    }
                }
                _target = zoomTarget;
                WheelState("accepted");
            }
            else
            {
                WheelState("geometry-limit");
                if (Plugin.Diagnostics.IsRecording)
                {
                    var projected = Project(_target);
                    var span = Corners.Count == 0 ? new FieldZoomGeometry.Point() : new FieldZoomGeometry.Point(
                        Corners.Max(p => p.X) - Corners.Min(p => p.X), Corners.Max(p => p.Y) - Corners.Min(p => p.Y),
                        Corners.Max(p => p.Z) - Corners.Min(p => p.Z));
                    Plugin.Diagnostics.State("fieldZoom.geometry", FormattableString.Invariant(
                        $"wheel={wheel:F3}; held={FieldFreeCamera.Holding}; offset={projected.X:F4},{projected.Y:F4},{projected.Z:F4}; span={span.X:F4},{span.Y:F4},{span.Z:F4}; nativeDistance={_original.magnitude:F4}; targetDistance={_target.magnitude:F4}; fov={camera.fieldOfView:F4}; aspect={camera.aspect:F4}; near={camera.nearClipPlane:F4}; far={camera.farClipPlane:F4}"));
                }
            }
        }
        var dt = Time.unscaledDeltaTime;
        if (!float.IsFinite(dt) || dt <= 0) return;
        var next = Vector3.Lerp(offset, _target, 1 - MathF.Exp(-18 * Math.Min(dt, .1f)));
        if (!Finite(next)) return;
        if ((next - _target).sqrMagnitude < .000001f) next = _target;
        if (Same(next, offset)) return;
        _applied = next; _owned = true;
        body.SetField("m_FollowOffset", next);
    }

    private static void WheelState(string reason)
    {
        if (Input.mouseScrollDelta.y != 0 || _pendingWheel != 0) Plugin.Diagnostics.State("fieldZoom.wheel", reason);
    }

    private static bool FieldInputAllowed(RuntimeObject root, bool pan = false)
    {
        if (!Application.isFocused || ModUi.IsOpen || Input.touchCount != 0 || root.Get<bool>("modalWaiting")
            || RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "GObject"), "get_draggingObject") != null
            || GameUi.Find(root, "FightWindow", maxDepth: 1) != null
            || GameUi.Find(root, "SettingWindow", maxDepth: 1) != null
            || GameUi.Find(root, "SettingInBattleWindow", maxDepth: 1) != null
            || GameUi.Find(root, "SinglePlayerSettingInBattleWindow", maxDepth: 1) != null
            || GameUi.Find(root, "SettingListWindow", maxDepth: 1) != null
            || root.Get("focus")?.Get("asTextInput")?.Get<bool>("focused") == true) return false;
        // A stationary pointer does not own WASD; focused text input above does.
        if (pan && !Input.GetMouseButton(0)) return true;
        // Reserve actual native scrolling, not every field UI hit or window.
        foreach (var item in GameUi.PointerPath(refresh: true, hitTest: true))
        {
            var pane = item.Get("asCom")?.Get("scrollPane");
            if (pane != null && pane.Get<bool>("touchEffect") && (pan || pane.Get<bool>("mouseWheelEnabled"))) return false;
            var input = item.Get("asTextInput");
            if (input?.Get<bool>("focused") == true && (pan || input.Get<bool>("mouseWheelEnabled"))) return false;
        }
        return true;
    }

    private static bool PointerOnFieldIndicator(RuntimeObject? logic)
    {
        var ui = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
        var attributes = ui?.Field("com_AttrInfos");
        var players = ui?.Field("com_PlayerAttrInfos");
        var plate = false;
        foreach (var item in GameUi.PointerPath(refresh: true, hitTest: true))
        {
            if (item.Get<bool>("draggable")) return false;
            if (item.TypeName is "UICom_AttrInfo" or "UICom_PlayerAttrInfo")
            {
                if (!GameUi.Visible(item)) return false;
                plate = true;
            }
            if (plate && (item.Pointer == attributes?.Pointer || item.Pointer == players?.Pointer)
                && GameUi.Visible(item)) return true;
        }
        return false;
    }

    private static bool RefreshRenderers()
    {
        var manager = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Unit", "LandManager"), "_inst");
        var nodes = manager?.Field("NodeDict");
        var count = nodes?.Get<int>("Count") ?? 0;
        if (count == 0) { Renderers.Clear(); return false; }
        // shortcut: same-count renderer replacements refresh within one second of wheel input.
        if (_nodes != nodes!.Pointer || _nodeCount != count || Time.unscaledTime >= _scanAt)
        {
            Renderers.Clear(); _nodes = nodes.Pointer; _nodeCount = count; _scanAt = Time.unscaledTime + 1;
            var iterator = nodes.Get("Values")!.Call("GetEnumerator")!;
            try
            {
                while (iterator.Call("MoveNext")!.Value<bool>())
                {
                    var land = iterator.Get("Current");
                    if (Alive(land)) Renderers.AddRange(new Component(land!.Pointer).GetComponentsInChildren<Renderer>());
                }
            }
            finally { iterator.Call("Dispose"); }
        }
        return true;
    }

    private static void BoardCorners(Vector3 position, Vector3 right, Vector3 up, Vector3 forward, float anchorY)
    {
        Corners.Clear();
        if (!TryPanBounds(out var bounds)) return;
        var min = bounds.min; var max = bounds.max;
        min.y = Math.Min(min.y, anchorY); max.y = Math.Max(max.y, anchorY);
        for (var i = 0; i < 8; i++)
        {
            var point = new Vector3(i % 2 == 0 ? min.x : max.x, i / 2 % 2 == 0 ? min.y : max.y, i / 4 == 0 ? min.z : max.z) - position;
            Corners.Add(new(Vector3.Dot(point, right), Vector3.Dot(point, up), Vector3.Dot(point, forward)));
        }
    }

    internal static bool TryPanBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        if (!RefreshRenderers()) return false;
        var found = false;
        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (var renderer in Renderers)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            var b = renderer.bounds;
            var lo = b.min; var hi = b.max;
            if (!float.IsFinite(lo.x) || !float.IsFinite(lo.y) || !float.IsFinite(lo.z)
                || !float.IsFinite(hi.x) || !float.IsFinite(hi.y) || !float.IsFinite(hi.z)
                || lo.x > hi.x || lo.y > hi.y || lo.z > hi.z) continue;
            min = Vector3.Min(min, lo); max = Vector3.Max(max, hi); found = true;
        }
        if (found) bounds.SetMinMax(min, max);
        return found;
    }

    private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    private static bool Alive(RuntimeObject? obj) => obj != null
        && RuntimeObject.StaticCall(RuntimeObject.FindClass("UnityEngine", "Object"), "op_Implicit", obj)!.Value<bool>();

    private static void Restore()
    {
        if (_owned && Alive(_body) && Same(_body!.Field<Vector3>("m_FollowOffset"), _applied))
            _body.SetField("m_FollowOffset", _original);
        _body = null; _owned = false;
    }

    internal static void Clear()
    {
        Exception? failure = null;
        void Attempt(Action action) { try { action(); } catch (Exception ex) { failure ??= ex; } }
        Attempt(Restore); Attempt(FieldFreeCamera.Clear); Attempt(FieldCameraReturnUi.Clear); Attempt(FieldIndicatorOpacity.Clear);
        _fieldVisible = false; _zoomOutLimit = 0;
        _scene = null; Renderers.Clear(); Corners.Clear(); _nodes = IntPtr.Zero; _nodeCount = 0; _scanAt = 0;
        _pendingWheel = 0; _wheelBrain = _wheelCamera = _wheelFrame = IntPtr.Zero;
        if (failure != null) throw failure;
    }
}
