using UnityEngine;

namespace BetterAstralParty;

// Presentation only: native camera switching runs underneath an owned free-view override.
internal static class FieldZoomUi
{
    private static RuntimeObject? _scene, _body;
    private static Vector3 _original, _applied, _target;
    private static bool _owned;
    private static bool _fieldVisible, _switching;
    private static float _zoomOutLimit;
    private static readonly List<Renderer> Renderers = new();
    private static readonly List<FieldZoomGeometry.Point> Corners = new();
    private static IntPtr _nodes;
    private static int _nodeCount;
    private static float _scanAt;
    private static float _pendingWheel;
    // On-screen size of field characters relative to the native camera distance (perspective follow offset).
    // How far the rendered camera sits from where the native view would put it: the same native camera without our
    // zoom, or (free view) the current player's native camera at its own follow offset. Zero at the native view.
    internal static Vector3 DiceViewDelta => _fieldVisible ? _viewDelta : default;
    private static Vector3 _viewDelta;
    // Which native view DiceViewDelta was measured from this frame, for the dice diagnostics.
    internal static string DiceViewSource { get; private set; } = "hidden";
    internal static float IndicatorScale => _owned && _fieldVisible && UnityMath.Magnitude(_applied) > 0
        ? Math.Clamp(UnityMath.Magnitude(_original) / UnityMath.Magnitude(_applied), 0.2f, 5f) : 1f;
    private static IntPtr _wheelBrain, _wheelCamera, _wheelFrame;
    // WASD moves the native free camera, and the owned view's pan, by FreeCameraObject.CameraSpeed per second; the game
    // writes it only in its constructor. The WASD Speed slider scales it while this feature runs, and Clear puts it back.
    private static RuntimeObject? _speedFree;
    private static float _speedBase, _speedSet;
    private static Camera? _camera;
    private static bool _cleared;

    internal static void Tick()
    {
        try
        {
            if (!Compatibility.Allowed("FieldZoom")) { Clear(); return; }
            // Laps per part (development builds; 2026-11-20): camera, monster plates, state plates, opacity.
            var lap = RuntimeObject.LapStart();
            Update(); RuntimeObject.Lap("zoom.update", ref lap);
            FieldMonsterPlates.Tick(IndicatorScale); RuntimeObject.Lap("zoom.monsters", ref lap);
            FieldStatePlates.Tick(IndicatorScale); RuntimeObject.Lap("zoom.states", ref lap);
            var distance = UnityMath.Magnitude(_original);
            FieldIndicatorOpacity.Tick(_fieldVisible && _owned && _zoomOutLimit > distance
                ? Math.Clamp((UnityMath.Magnitude(_applied) - distance) / (_zoomOutLimit - distance), 0, 1) : 0);
            RuntimeObject.Lap("zoom.opacity", ref lap);
        }
        catch (Exception ex) { Compatibility.Block("FieldZoom", ex, Clear); }
    }

    private static void Update()
    {
        _fieldVisible = false; _viewDelta = default; DiceViewSource = "hidden";
        var root = GameUi.Root;
        if (!Plugin.FieldZoom.Value || PvpSafety.Suspended || root == null) { Clear(); return; }
        var scene = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Scene", "BattleSceneController"), "inst");
        // Outside a battle scene (home, loading) nothing below applies: undo once, not on every frame.
        if (scene == null) { if (!_cleared) Clear(); return; }
        _cleared = false;
        if (scene.Pointer != _scene?.Pointer) { Clear(); _cleared = false; _scene = scene; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (!Alive(scene) || !VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)) { Clear(); return; }
        var brain = scene!.Get("cinemachineBrain");
        var free = scene.Field("freeObject");
        var status = free?.Field("status")?.Value<int>() ?? -1;
        if (!Alive(brain) || !Alive(free)) { Clear(); return; }
        ScaleSpeed(free!);
        var manager = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Camera", "CameraManager"), "_inst");
        var player = manager?.Call("GetCurPlayerCamera")?.Field("vCamera");
        var cameraObject = scene.Get("mainCamera");
        // A board switch (Magic Academy's phase change) runs on the native camera: the zoom yields while it plays, and
        // the free view ends with it, since the board it looked at is gone. The board is read again afterwards.
        var lands = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Unit", "LandManager"), "_inst")?.Field("NodeDict");
        var switching = SubMapSwitch.Running(lands?.Pointer ?? IntPtr.Zero);
        if (switching != _switching)
        {
            _switching = switching; _scanAt = 0;
            Plugin.Diagnostics.State("fieldZoom.mapSwitch", switching ? "running; freeView=" + FieldFreeCamera.Holding : "done");
        }
        if (switching) FieldFreeCamera.Clear();
        var held = FieldFreeCamera.Holding;
        // FightWindow opens before the battle platform; yield only when the field renderer stops.
        var blocked = switching || status is not (0 or 1 or 2 or 3) || !Alive(cameraObject)
            || !CameraOf(cameraObject!).enabled || !CameraOf(cameraObject!).gameObject.activeInHierarchy
            || logic?.Get("battle")?.Field<bool>("clientFinishReady") != true;
        var freeLap = RuntimeObject.LapStart();
        var owned = FieldFreeCamera.Update(scene, brain!, free, player, blocked);
        RuntimeObject.Lap("zoom.free", ref freeLap);
        // While a PIP holds the field with the free view (FightPip.HoldsField) the return button shows only for a view the
        // player moved or zoomed (FieldFreeCamera.Moved); the hold alone is no free view of theirs.
        FieldCameraReturnUi.Tick(FieldFreeCamera.Holding && !blocked && (!FightPip.HoldsField || FieldFreeCamera.Moved));
        RuntimeObject.Lap("zoom.return", ref freeLap);
        var wheel = Input.mouseScrollDelta.y;
        var wheelAllowed = wheel == 0 && _pendingWheel == 0 && Same(_target, _applied)
            || FieldInputAllowed(root) && !FieldCameraReturnUi.ConsumedInput
                && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2);
        if (!wheelAllowed) { _pendingWheel = 0; WheelState("input-owned"); }
        if (owned && !Compatibility.Allowed("FieldCameraReturn")) { Reset(); return; }
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
        { WheelState("camera-unsupported"); Reset(); return; }
        var body = active.Call("GetCinemachineComponent", 0); // Audited Cinemachine Stage.Body.
        if (!Alive(body) || body!.TypeName != "CinemachineTransposer" || !body.Get<bool>("IsValid")
            || body.Field("m_BindingMode")!.Value<int>() != 4 || active.Get("LookAt") != null
            || active.Call("GetCinemachineComponent", 1) != null) { WheelState("camera-body"); Reset(); return; }
        var follow = active.Get("Follow");
        if (!Alive(follow)) { _pendingWheel = 0; WheelState("camera-follow"); return; }
        var camera = CameraOf(cameraObject!);
        if (camera.orthographic || camera.usePhysicalProperties || camera.lensShift is var shift && (shift.x != 0 || shift.y != 0))
        { _pendingWheel = 0; WheelState("camera-lens"); return; }
        var offset = body.Field<Vector3>("m_FollowOffset");
        if (!Finite(offset)) { _pendingWheel = 0; WheelState("camera-offset"); return; }
        _fieldVisible = true;
        // Each view is read this frame; a cached baseline from another native camera (free/skill camera) shifted the dice.
        // GetCurPlayerCamera is null while no character camera has priority (native free or system camera); a null view
        // left the dice unpinned. The turn player's own character camera is then the native default view. Not the native
        // base frame: after a native drag that is the free camera, which the same drag moves, so the dice stopped
        // following the pan (2026-10-25).
        Vector3? nativeView;
        if (owned)
        {
            nativeView = NativeView(player); DiceViewSource = "player";
            if (nativeView == null)
            {
                nativeView = NativeView(logic?.Get("battle")?.Call("GetCurrentPlayer")?.Field("CharacterInst")?.Field("vCamera")?.Field("vCamera"));
                DiceViewSource = nativeView != null ? "turn" : "none";
            }
        }
        else
        {
            nativeView = _owned && _body?.Pointer == body.Pointer && !Same(_applied, _original) ? follow!.Get<Vector3>("position") + _original : null;
            DiceViewSource = nativeView != null ? "native-zoom" : "native";
        }
        _viewDelta = nativeView is { } view ? UnityMath.Sub(camera.transform.position, view) : default;
        if (!owned)
        {
            var frames = brain!.Field("mFrameStack");
            if (frames?.Get<int>("Count") != 1) { _pendingWheel = 0; WheelState("camera-stack"); Restore(); return; }
            var frame = frames.Call("get_Item", 0);
            var blend = frame?.Field("blend");
            if (_pendingWheel != 0 && (_wheelBrain != brain.Pointer || _wheelCamera != cameraObject!.Pointer
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
                    _wheelBrain = brain.Pointer; _wheelCamera = cameraObject!.Pointer; _wheelFrame = frame!.Pointer;
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
        // Same rule as the native drag (FreeCameraObject.HandleControlCamera): only a touchable UI target owns the press.
        var onUi = inputAllowed && Input.GetMouseButton(0)
            && RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_isTouchOnUI")!.Value<bool>()
            && root.Get("touchTarget")?.Get<bool>("touchable") == true;
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
                camera.aspect, camera.nearClipPlane, camera.farClipPlane, _original.magnitude / _target.magnitude, wheel * Math.Clamp(Plugin.ZoomSensitivity.Value, .1f, 3f), out var scale, out var maximum))
            {
                // Max Zoom-Out keeps a share of the travel to the field-fit stop; indicators normalize against it too.
                _zoomOutLimit = FieldZoomGeometry.CappedLimit(_original.magnitude, maximum > 0 ? _target.magnitude * maximum : 0, Plugin.ZoomOutLimit.Value);
                var zoomTarget = _target * scale;
                if (scale > 1 && _zoomOutLimit > 0 && zoomTarget.magnitude > _zoomOutLimit)
                    zoomTarget = _target * (_zoomOutLimit / _target.magnitude);
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
                if (FieldFreeCamera.Holding) FieldFreeCamera.Touched();
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

    // A native camera's pose (follow target + its own follow offset), or null when unreadable.
    private static Vector3? NativeView(RuntimeObject? camera)
    {
        if (camera?.TypeName != "CinemachineVirtualCamera") return null;
        var follow = camera.Get("Follow");
        var body = camera.Call("GetCinemachineComponent", 0);
        if (!Alive(follow) || !Alive(body) || body!.TypeName != "CinemachineTransposer") return null;
        var offset = body.Field<Vector3>("m_FollowOffset");
        return Finite(offset) ? follow!.Get<Vector3>("position") + offset : null;
    }

    private static void WheelState(string reason)
    {
        if (Input.mouseScrollDelta.y != 0 || _pendingWheel != 0) Plugin.Diagnostics.State("fieldZoom.wheel", reason);
    }

    private static bool FieldInputAllowed(RuntimeObject root, bool pan = false)
    {
        if (!Application.isFocused || ModUi.IsOpen || Input.touchCount != 0 || root.Get<bool>("modalWaiting")
            || GameUi.LiveDrag() != null
            || GameUi.Find(root, "FightWindow", maxDepth: 1) is { } fight && !EncounterHidden(fight) && !FightPip.FieldOpen(pointer: Input.GetMouseButton(0))
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

    // The encounter's own hide-UI state (native txt_HideTip shown) hands the field view back: the wheel zooms, and
    // once the zoom holds the owned view the drag pans it, as the native drag does there before any zoom.
    private static bool EncounterHidden(RuntimeObject fight) =>
        GameUi.Visible(fight.Get("contentPane")?.Field("com_LaunchPK")?.Field("txt_HideTip"));

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
    // UnityEngine.Object's own liveness test (op_Implicit) for scene objects, read without a call or a boxed result:
    // the engine zeroes a destroyed object's cached native pointer.
    private static bool Alive(RuntimeObject? obj) => obj != null && obj.Field<IntPtr>("m_CachedPtr") != IntPtr.Zero;

    // One interop wrapper for the render camera while it stays the same object, instead of new ones every frame; the plate
    // layers read the same BattleSceneController.mainCamera.
    // `is not null`: Unity's != on a UnityEngine.Object is a native call, and the pointer test decides here.
    internal static Camera CameraOf(RuntimeObject cameraObject) =>
        _camera is not null && _camera.Pointer == cameraObject.Pointer ? _camera : _camera = new Camera(cameraObject.Pointer);

    private static void Restore()
    {
        if (_owned && Alive(_body) && Same(_body!.Field<Vector3>("m_FollowOffset"), _applied))
            _body.SetField("m_FollowOffset", _original);
        _body = null; _owned = false;
    }

    private static void ScaleSpeed(RuntimeObject free)
    {
        var current = free.Field<float>("CameraSpeed");
        if (free.Pointer != _speedFree?.Pointer || current != _speedSet)
        {
            if (!float.IsFinite(current) || current <= 0) return;
            _speedFree = free; _speedBase = current;
        }
        _speedSet = _speedBase * Math.Clamp(Plugin.FreeCameraSpeed.Value, .1f, 3f);
        if (current != _speedSet) free.SetField("CameraSpeed", _speedSet);
    }

    private static void RestoreSpeed()
    {
        if (_speedSet != _speedBase && Alive(_speedFree) && _speedFree!.Field<float>("CameraSpeed") == _speedSet)
            _speedFree.SetField("CameraSpeed", _speedBase);
        _speedFree = null;
    }

    // Fight PiP (2026-11-17): while a PIP shows the field, the owned free view holds the field camera. Without its override
    // the field camera's CinemachineBrain follows the fight's own cameras once the battle platform opens (BattleSceneCamera:
    // PK layer 6, priority 51, over the character cameras' 10; the game keeps the field camera off in fights, so its own flow
    // never shows that), and the fight was drawn full size behind the PIP (user-reported on 2026.11.2-dev.23). Entered from
    // the native view as a wheel tick enters it. Returns "entered", or the check that kept it out, for the diagnostics.
    // `enter` false (a PIP that starts full screen, user-chosen 2026-11-18): the checks pass and the rendered pose is read
    // into `later` ("ready"), touching nothing, and HoldLater enters from it once the PIP first shrinks.
    internal static string HoldForFight(out FightHold? later, bool enter = true)
    {
        later = null;
        if (FieldFreeCamera.Holding) return "held";
        if (!Compatibility.Allowed("FieldZoom") || !Compatibility.Allowed("FieldCameraReturn") || !Plugin.FieldZoom.Value || PvpSafety.Suspended) return "off";
        var scene = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Scene", "BattleSceneController"), "inst");
        if (!Alive(scene) || scene!.Pointer != _scene?.Pointer) return "scene";
        var brain = scene.Get("cinemachineBrain");
        var cameraObject = scene.Get("mainCamera");
        if (!Alive(brain) || !Alive(cameraObject)) return "camera";
        var player = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Camera", "CameraManager"), "_inst")?.Call("GetCurPlayerCamera")?.Field("vCamera");
        var active = brain!.Get("ActiveVirtualCamera");
        if (!Alive(active) || active!.TypeName != "CinemachineVirtualCamera"
            || active.Pointer != scene.Field("freeCamera")?.Pointer && active.Pointer != player?.Pointer)
            return "camera-unsupported:" + (active?.Get("Name")?.String() ?? active?.TypeName ?? "none");
        var body = active.Call("GetCinemachineComponent", 0); // Audited Cinemachine Stage.Body.
        if (!Alive(body) || body!.TypeName != "CinemachineTransposer" || !body.Get<bool>("IsValid")
            || body.Field("m_BindingMode")!.Value<int>() != 4 || active.Get("LookAt") != null
            || active.Call("GetCinemachineComponent", 1) != null) return "camera-body";
        if (!Alive(active.Get("Follow"))) return "camera-follow";
        var camera = CameraOf(cameraObject!);
        if (camera.orthographic || camera.usePhysicalProperties || camera.lensShift.x != 0 || camera.lensShift.y != 0) return "camera-lens";
        if (brain.Field("mFrameStack")?.Get<int>("Count") != 1) return "camera-stack";
        var transform = camera.transform;
        later = new FightHold(scene, brain, active, body, transform.position, transform.rotation);
        return enter ? HoldLater(later) : "ready";
    }

    // What HoldForFight read as the fight window opened. The field does not change during a fight (server pushes wait
    // behind its presentation), but the field camera's brain follows the fight's cameras while the camera is off, so the
    // free view starts from this pose and the camera it followed then, not from the brain's current ones.
    internal sealed record FightHold(RuntimeObject Scene, RuntimeObject Brain, RuntimeObject Active, RuntimeObject Body, Vector3 Position, Quaternion Rotation);

    internal static string HoldLater(FightHold? hold)
    {
        if (FieldFreeCamera.Holding) return "held";
        if (hold == null) return "none";
        if (!Compatibility.Allowed("FieldZoom") || !Compatibility.Allowed("FieldCameraReturn") || !Plugin.FieldZoom.Value || PvpSafety.Suspended) return "off";
        if (!Alive(hold.Scene) || hold.Scene.Pointer != _scene?.Pointer) return "scene";
        if (!Alive(hold.Brain) || !Alive(hold.Active) || !Alive(hold.Body)) return "camera";
        if (!Alive(hold.Active.Get("Follow"))) return "camera-follow";
        if (hold.Brain.Field("mFrameStack")?.Get<int>("Count") != 1) return "camera-stack";
        FieldFreeCamera.Enter(hold.Scene, hold.Brain, hold.Active, hold.Body, hold.Position, hold.Rotation, player: false);
        Restore();
        var held = FieldFreeCamera.Camera!.Call("GetCinemachineComponent", 0)!;
        _body = held; _original = _applied = _target = held.Field<Vector3>("m_FollowOffset");
        return "entered";
    }

    // The return button while a Fight PiP holds the field: the held view moves to the turn player's own camera at its own
    // distance, as the native return would show it (CameraManager.SwitchCamera to that character), and the hold stays.
    internal static bool ReturnInHold() => MoveHold(RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst")
        ?.Get("battle")?.Call("GetCurrentPlayer")?.Field("CharacterInst")?.Field("vCamera")?.Field("vCamera"));

    // A focus click while a Fight PiP holds the field (a chat ping's show-location button, a player head, a monster item,
    // the minimap): its native handler moved the game's field camera under the hold, CameraManager.ControlFreeCamera to
    // the free camera (character cameras at priority 0) or SwitchCamera to a character's (16, free camera closed). The held
    // view moves there at that camera's own distance, as ending the free view shows it outside a hold. `player` is
    // GetCurPlayerCamera's virtual camera this tick. Returns the view followed, for the diagnostics.
    internal static string FollowInHold(RuntimeObject scene, RuntimeObject? player)
    {
        if (Alive(player)) return MoveHold(player) ? "player" : "player-unsupported";
        var free = scene.Field("freeCamera");
        if (!Alive(free) || free!.Get<int>("Priority") <= 0 || free.Get("gameObject")?.Get<bool>("activeInHierarchy") != true) return "none";
        return MoveHold(free) ? "free" : "free-unsupported";
    }

    private static bool MoveHold(RuntimeObject? camera)
    {
        var follow = camera?.Get("Follow");
        var native = camera?.Call("GetCinemachineComponent", 0); // Audited Cinemachine Stage.Body.
        var held = FieldFreeCamera.Camera?.Call("GetCinemachineComponent", 0);
        if (!Alive(camera) || !Alive(follow) || !Alive(native) || native!.TypeName != "CinemachineTransposer" || !Alive(held)) return false;
        var offset = native.Field<Vector3>("m_FollowOffset");
        if (!Finite(offset)) return false;
        Restore();
        held!.SetField("m_FollowOffset", offset);
        _body = held; _original = _applied = _target = offset; _zoomOutLimit = 0;
        FieldFreeCamera.MoveAnchor(follow!.Get<Vector3>("position"));
        return true;
    }

    // Feature off, PvP, scene exit and failures also put the WASD speed back; the zoom's own camera resets keep it.
    internal static void Clear()
    {
        Exception? failure = null;
        try { RestoreSpeed(); } catch (Exception ex) { failure = ex; }
        Reset();
        if (failure != null) throw failure;
    }

    private static void Reset()
    {
        Exception? failure = null;
        void Attempt(Action action) { try { action(); } catch (Exception ex) { failure ??= ex; } }
        Attempt(Restore); Attempt(FieldFreeCamera.Clear); Attempt(FieldCameraReturnUi.Clear); Attempt(FieldIndicatorOpacity.Clear); Attempt(FieldMonsterPlates.Clear); Attempt(FieldStatePlates.Clear);
        _fieldVisible = _switching = false; _zoomOutLimit = 0;
        _scene = null; _camera = null; Renderers.Clear(); Corners.Clear(); _nodes = IntPtr.Zero; _nodeCount = 0; _scanAt = 0;
        _pendingWheel = 0; _wheelBrain = _wheelCamera = _wheelFrame = IntPtr.Zero;
        if (failure != null) throw failure;
        _cleared = true;
    }
}
