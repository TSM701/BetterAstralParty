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
    // Only clicks on the native UI whose click handler moves the camera end the free view: the player icon's head
    // (btn_Head -> SwitchCamera -> MoveCameraToPlayer; the whole button only for watchers, OnClickPlayer -> ControlFreeCamera),
    // a monster target item (UIButton_MonsterItem -> BattleMonsterWindow.SwitchCamera) and the minimap (OnClickMiniMap).
    // The player label (btn_Label -> ShowBattlePlayerInfo) only opens details. Turn, move and skill camera switches never do.
    // A teammate's land/monster ping in the chat list has a show-location button (btn_ShowSignal -> BattleMessage.
    // ShowAddressInfo -> CameraManager.ControlFreeCamera), which the free view used to hide (issue #16, 2026-10-26).
    private const string PlayerInfo = "UIBattleInfo_Button_PlayerInfo", PlayerHead = "btn_Head", ShowSignal = "btn_ShowSignal";
    private static readonly string[] FocusTypes = { PlayerInfo, "UIButton_MonsterItem", "UIBattleInfo_Com_Minimap", ShowSignal };
    private static string? _focusTarget, _follow;
    private static Vector2 _focusPress;
    private static int _focusDrag = -1, _followFrame;
    // pathTypes runs from the hit object up to the root; the player head graph is listed by its name.
    internal static string? FocusTarget(IEnumerable<string> pathTypes, bool watcher = false)
    {
        var head = false;
        foreach (var type in pathTypes)
        {
            if (type == PlayerHead) head = true;
            else if (Array.IndexOf(FocusTypes, type) >= 0) return type != PlayerInfo || head || watcher ? type : null;
        }
        return null;
    }
    // Native click rule: FairyGUI TouchInfo.Move cancels a click once the press strays past 50 pixels on an axis, and a
    // draggable target's drag start cancels it at clickDragSensitivity (dragThreshold; negative when nothing is draggable).
    // Using clickDragSensitivity (2) for every target missed monster clicks that moved a few pixels (2026-10-25).
    internal static bool ClickGesture(Vector2 press, Vector2 point, int dragThreshold)
    {
        var dx = Math.Abs(point.x - press.x); var dy = Math.Abs(point.y - press.y);
        return dx <= 50 && dy <= 50 && (dragThreshold < 0 || dx < dragThreshold && dy < dragThreshold);
    }
    internal static bool Holding => _camera != null;
    // The player's own free view: entered by a wheel tick, or panned or zoomed since a Fight PiP's hold entered it or since
    // a return inside that hold. FieldZoomUi shows the return button during a hold only then.
    internal static bool Moved { get; private set; }
    // CharacterMove.Move turns its character camera's damping to 0 while the character walks and back to 0.3 when it stops
    // (CharacterCamera.SetMoveDamping); a free view entered from a walking camera kept 0 and moved without easing until it
    // ended (user-reported 2026-11-18: smooth camera moves sometimes turned linear). The resting value replaces that 0.
    internal const float RestingDamping = .3f;
    internal static RuntimeObject? Camera => _camera == null ? null : new RuntimeObject(_camera.Pointer);

    internal static void Enter(RuntimeObject scene, RuntimeObject brain, RuntimeObject active, RuntimeObject body, UnityEngine.Camera output) =>
        Enter(scene, brain, active, body, output.transform.position, output.transform.rotation);

    // `position`, `rotation`: the rendered pose to start at. A Fight PiP that starts full screen reads it as the fight window
    // opens and enters only once it shrinks, when the field camera's brain may follow the fight's cameras (FieldZoomUi.FightHold).
    internal static void Enter(RuntimeObject scene, RuntimeObject brain, RuntimeObject active, RuntimeObject body, Vector3 position, Quaternion rotation,
        bool player = true)
    {
        if (Holding) return;
        Moved = player;
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
            var walking = nativeBody.m_XDamping == 0 && nativeBody.m_YDamping == 0 && nativeBody.m_ZDamping == 0;
            transposer.m_XDamping = walking ? RestingDamping : nativeBody.m_XDamping;
            transposer.m_YDamping = walking ? RestingDamping : nativeBody.m_YDamping;
            transposer.m_ZDamping = walking ? RestingDamping : nativeBody.m_ZDamping;
            if (walking) Plugin.Diagnostics.Write("fieldFreeCamera entered from an undamped camera; damping=" + RestingDamping);
            // Start at the rendered pose, even while the native Follow target is still damping.
            _anchor.transform.position = position - transposer.m_FollowOffset;
            _camera.transform.SetPositionAndRotation(position, rotation);
            _camera.InvalidateComponentPipeline();
            _cameraObject.SetActive(true);
            _camera.ForceCameraPosition(position, rotation);
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
        // A focus click inside a Fight PiP's hold: its native handler ran in FairyGUI's LateUpdate, after the click's tick.
        if (_follow != null && Time.frameCount != _followFrame)
        {
            var followed = _follow; _follow = null;
            if (FightPip.HoldsField)
                Plugin.Diagnostics.Write("fieldFreeCamera followed native focus in hold; target=" + followed + "; view=" + FieldZoomUi.FollowInHold(scene, player));
        }
        // A focus click ends the free view even while the owned override is suspended (a native camera switch or
        // blend underneath, another frame on top); otherwise the free view came back over the native focus once the
        // override was acquired again (2026-10-24: skill monster targets needed a third click).
        if (Input.GetMouseButtonDown(0))
        {
            _focusPress = Input.mousePosition;
            var types = new List<string>();
            var draggable = false;
            foreach (var item in GameUi.PointerPath(hitTest: true))
            {
                var name = item.Field("name")?.String(); // GObject.name is a field, not a property.
                types.Add(name is PlayerHead or ShowSignal ? name : item.TypeName);
                draggable |= item.Get<bool>("draggable");
            }
            _focusTarget = FocusTarget(types, types.Contains(PlayerInfo) && RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst")
                ?.Get("watch")?.Call("PlayerIsWatcher")?.Value<bool>() == true);
            _focusDrag = draggable ? RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "UIConfig"), "clickDragSensitivity")!.Value<int>() : -1;
        }
        // A press that strayed once stays cancelled, as the native click does.
        else if (_focusTarget != null && !ClickGesture(_focusPress, Input.mousePosition, _focusDrag)) _focusTarget = null;
        else if (_focusTarget != null && Input.GetMouseButtonUp(0))
        {
            var target = _focusTarget; _focusTarget = null;
            // A Fight PiP keeps the free view while it shows the field (FightPip.HoldsField): the held view follows the camera
            // the click moved underneath instead, and a return pressed earlier in the hold is called off (user-reported
            // 2026-11-23: a chat ping's show-location button did nothing during a PIP fight).
            if (FightPip.HoldsField) { _follow = target; _followFrame = Time.frameCount; _returnPending = false; }
            else if (!_returnPending)
            {
                // Same as the exit button, but leave the camera to the native click handler instead of returning home.
                Plugin.Diagnostics.Write("fieldFreeCamera released for native focus; target=" + target + "; suspended=" + (_override < 0 ? "true" : "false")
                    + "; nativeBlending=" + (count is >= 1 and <= 64 && stack![0].blend is { IsComplete: false } ? "true" : "false"));
                FieldZoomUi.Clear();
                return false;
            }
        }
        if (count < 1 || count > 64) { Suspend(); return false; }
        var native = stack![0].blend?.CamB;
        var nativePointer = native?.Pointer ?? IntPtr.Zero;
        // Field character/card cameras may change while the owned view stays fixed.
        var supported = nativePointer != IntPtr.Zero
            && new RuntimeObject(nativePointer).TypeName == "CinemachineVirtualCamera"
            && new CinemachineVirtualCamera(nativePointer);
        // The top owned override masks IsBlending; inspect the native base frame instead. A Fight PiP keeps the override
        // while the field camera is off for a full-screen fight, so the field comes back held.
        if (blocked && !FightPip.HoldsField || !supported || count != (_override >= 0 ? 2 : 1)
            || _override >= 0 && (stack[1].id != _override || stack[1].blend?.CamB?.Pointer != _camera!.Pointer))
        { Suspend(); return false; }
        // A return pressed while a Fight PiP held the field ends the free view once the hold does.
        if (_returnPending && !FightPip.HoldsField)
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
            // The native free camera reads GameSettings.GetAxis since 2026-11-12: the player's rebindable movement keys.
            float Axis(string axis) => RuntimeObject.StaticCall(settings, "GetAxis", axis)!.Value<float>();
            if (float.IsFinite(speed) && speed > 0 && float.IsFinite(Time.deltaTime) && Time.deltaTime > 0)
                delta += (transform.forward * Axis("Vertical") + transform.right * Axis("Horizontal")) * speed * Math.Min(Time.deltaTime, .1f);
        }
        delta.y = 0;
        if (!float.IsFinite(delta.x) || !float.IsFinite(delta.z) || delta.sqrMagnitude == 0
            || !FieldZoomUi.TryPanBounds(out var bounds)) return;
        var position = _anchor!.transform.position + delta;
        position.x = Math.Clamp(position.x, bounds.min.x, bounds.max.x);
        position.z = Math.Clamp(position.z, bounds.min.z, bounds.max.z);
        _anchor.transform.position = position;
        Moved = true; _returnPending = false;
    }

    // The player zoomed the free view (FieldZoomUi): it is theirs again, and a return pressed in a hold is called off.
    internal static void Touched() { Moved = true; _returnPending = false; }

    // A return inside a Fight PiP's hold: the view follows the turn player's camera from here (FieldZoomUi.ReturnInHold).
    internal static void MoveAnchor(Vector3 position)
    {
        if (_anchor) _anchor!.transform.position = position;
        Moved = false;
    }

    // While a Fight PiP holds the field the override stays (without it the field camera's brain follows the fight's cameras):
    // the view goes back to the turn player's camera now and the free view ends with the hold (user-reported 2026-11-18:
    // the return button vanished during PIP fights).
    internal static void Return()
    {
        _returnPending = true;
        if (FightPip.HoldsField) { FieldZoomUi.ReturnInHold(); return; }
        Suspend();
    }

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
        _returnPending = _dragging = _indicatorPress = Moved = false;
        _focusTarget = _follow = null;
    }
}
