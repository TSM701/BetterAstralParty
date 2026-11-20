using UnityEngine;

namespace BetterAstralParty;

// User-authorized zoom-follow for native monster nameplates (2026-10-19). Native UIBattleInfoPanel.UpdateAttrInfoPosition
// runs in FairyGUI's LateUpdate, after the mod, and lifts every direct UICom_AttrInfo child of com_AttrInfos by fixed
// 200/120 px, so a zoomed-out monster's plate floats away from it. While the field zoom differs from the native distance,
// the native plates move into an owned layer inside com_AttrInfos, which the native pass skips, and are laid out here
// with the native UpdateOffset and UpdateGuideLine, overlapping plates stacked on their wanted places
// (PlayerIndicatorUi.StackRects, 2026-11-19, issue #19), and lifts scaled by the zoom. The dot height
// stays native: the hero indicator position slider does not apply here. Plates return to com_AttrInfos at native zoom, on
// feature OFF and on cleanup. Native Dispose removes a plate from whichever parent holds it.
internal static class FieldMonsterPlates
{
    private const float PlateLift = 200f, MarkerLift = 120f;
    private static readonly List<RuntimeObject> Adopted = new();
    private static readonly List<(RuntimeObject Ui, Vector2 Owner)> Placed = new();
    // Plates whose touchable or buff mark the mod turned off while the local player moves; Clear turns back only these.
    private static readonly HashSet<IntPtr> Untouchable = new(), BuffHidden = new();
    // Overlapping plates stacked on where they want to be, on rectangles read once; each placed plate's y before this pass
    // and the y it wants (its target's), index-aligned with Placed.
    private static readonly List<(float X, float Y, float Width, float Height)> Rects = new();
    private static readonly List<float> Before = new(), Wanted = new();
    private static RuntimeObject? _parent, _layer, _sentinel, _battleUi;
    private static IntPtr _plateClass, _sceneClass;
    private static float _scanAt;

    // What the last pass gave each plate (2026-11-18: zoomed out, about 23 µs a plate a frame, almost all of it native
    // calls). UpdateOffset only moves a plate toward its target, so while the owner, target, width and the plate's place
    // hold from one frame to the next it has settled and would write the same place again; UpdateGuideLine follows only
    // the marker point, the plate's place and offset and its line, so with those unchanged it would draw the same guide.
    // Both are then skipped; every 16th frame (staggered) and after an info switch each plate is laid out in full anyway.
    private sealed class PlateState
    {
        internal Vector3 Owner;
        internal Vector2 Target, Place;
        internal float Width;
        internal (float OwnerX, float OwnerY, float X, float Y, float OffsetX, float LineX, float LineWidth, float Lift) Guide;
        internal bool Placed, Guided;
    }
    private static readonly Dictionary<IntPtr, PlateState> States = new();
    private static readonly HashSet<IntPtr> Full = new();
    private static int _frame;

    // Visible adopted plates, for the hero layout's fixed obstacles.
    internal static IReadOnlyList<RuntimeObject> Plates => Adopted;

    internal static bool ZoomFollows(float scale) => float.IsFinite(scale) && Math.Abs(scale - 1f) > 0.01f;

    // Development builds only (2026-11-18): Ctrl+Shift+F6 lays the plates out here at the native zoom too, to compare the
    // frame against the native per-frame pass, which compares every pair of plates, on a crowded field.
    private static readonly bool DevelopmentBuild = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private static bool _forceAdopt;

    internal static void Tick(float scale)
    {
        if (DevelopmentBuild && Input.GetKeyDown(KeyCode.F6) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            _forceAdopt = !_forceAdopt;
            Plugin.Diagnostics.Write("fieldMonsterPlates.forceAdopt=" + (_forceAdopt ? "on" : "off"));
        }
        if (!(ZoomFollows(scale) || _forceAdopt) || PvpSafety.Suspended || GameUi.Root == null) { Clear(); return; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        if (logic?.Get("fight")?.Field("fightStatus")?.Value<bool>() == true && !FightPip.ShowsField) { Clear(); return; }
        var battleUi = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
        var parent = battleUi?.Field("com_AttrInfos");
        if (!GameUi.Visible(parent)) { Clear(); return; }
        if (_parent?.Pointer != parent!.Pointer || _layer == null || _layer.Get<bool>("isDisposed")) { Clear(); _parent = parent; _layer = NativeUi.Component(parent, 0, 0); }
        _battleUi = battleUi;
        if (_layer.Get<float>("width") != parent.Get<float>("width") || _layer.Get<float>("height") != parent.Get<float>("height"))
            _layer.Call("SetSize", parent.Get<float>("width"), parent.Get<float>("height"));
        if (Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 0.2f; Adopt(parent); }
        if (FieldIndicatorOpacity.Hidden) return;
        var lap = RuntimeObject.LapStart();
        Layout(scale);
        RuntimeObject.Lap("plates.monsters", ref lap);
    }

    private static void Adopt(RuntimeObject parent)
    {
        if (_plateClass == IntPtr.Zero) _plateClass = RuntimeObject.FindClass("UI", "UICom_AttrInfo");
        // A hidden owned plate stays a direct child: native SwitchMonsterInfo (long press) still reaches it,
        // and its ShowInfo state is mirrored onto the adopted plates.
        if (_sentinel == null || _sentinel.Get<bool>("isDisposed") || _sentinel.Get("parent")?.Pointer != parent.Pointer)
        {
            GameUi.Dispose(_sentinel);
            _sentinel = RuntimeObject.StaticCall(_plateClass, "CreateInstance") ?? throw new InvalidOperationException("Monster plate sentinel unavailable");
            _sentinel.SetField("_PlayerId", long.MinValue); // Owns no player: GetOwnerPos stays zero and native hides it.
            _sentinel.Set("visible", false);
            _sentinel.Set("touchable", false);
            parent.Call("AddChild", _sentinel);
        }
        Adopted.RemoveAll(ui => ui.Get<bool>("isDisposed") || ui.Get("parent")?.Pointer != _layer!.Pointer);
        foreach (var key in States.Keys.Where(key => !Adopted.Exists(ui => ui.Pointer == key)).ToList()) States.Remove(key);
        for (var i = parent.Get<int>("numChildren") - 1; i >= 0; i--)
        {
            var child = parent.Call("GetChildAt", i)!;
            if (child.TypeName != "UICom_AttrInfo" || child.Pointer == _sentinel.Pointer || child.Get("PlayerData") == null) continue;
            _layer!.Call("AddChild", child);
            Adopted.Add(child);
        }
    }

    private static void Layout(float scale)
    {
        // Character-sized indicators (user-decided 2026-11-20, readability traded for the characters' scale): the layer is drawn
        // at the zoom's scale and the plates are laid out inside it at native-zoom distances, so each plate, its side offset, the
        // stacking and the guide line shrink and grow with the characters around their own points.
        if (_layer!.Get<float>("scaleX") != scale) _layer.Call("SetScale", scale, scale);
        if (Adopted.Count == 0) return;
        if (_sceneClass == IntPtr.Zero) _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
        var cameraObject = RuntimeObject.StaticField(_sceneClass, "inst")?.Get("mainCamera");
        if (cameraObject == null) return;
        var camera = FieldZoomUi.CameraOf(cameraObject);
        var root = GameUi.Root!;
        float width = root.Get<float>("width"), height = root.Get<float>("height");
        var info = _sentinel == null ? 0 : Math.Max(0, ShowInfo(_sentinel));
        var self = _battleUi?.Field("_SelfPlayer")?.Field("CharacterInst");
        var stepping = self != null && self.Get<int>("canStep") > 0;
        // Value math through UnityMath, and the camera and screen read once a pass (2026-11-18).
        var enabled = camera.enabled;
        float screenHeight = Screen.height;
        // The layer's GlobalToLocal is affine: three calls a pass map every owner point.
        Vector2 Map(float x, float y) => _layer!.Result<Vector2, Vector2>("GlobalToLocal", UnityMath.V2(x, y));
        var origin = Map(0, 0); var alongX = Map(1000, 0); var alongY = Map(0, 1000);
        float xx = (alongX.x - origin.x) / 1000, xy = (alongX.y - origin.y) / 1000, yx = (alongY.x - origin.x) / 1000, yy = (alongY.y - origin.y) / 1000;
        _frame++;
        var index = 0;
        var step = PlayerIndicatorUi.LerpStep(Time.deltaTime);
        Placed.Clear(); Full.Clear(); Before.Clear(); Wanted.Clear();
        // Per plate and frame: no wrapper for a value result, no argument array (2026-11-13; zoomed out this layer
        // allocated about 12 KB a frame).
        foreach (var ui in Adopted)
        {
            if (ui.Get<bool>("isDisposed")) continue;
            var full = (_frame + index++ & 15) == 0;
            if (ShowInfo(ui) != info) { ui.Call("SwitchInfo", info == 1); full = true; }
            var owner = ui.Result<Vector3>("GetOwnerPos");
            var screen = UnityMath.IsZero(owner) || !enabled ? default : camera.WorldToScreenPoint(owner);
            float sx = screen.x, sy = screenHeight - screen.y;
            var local = screen.z > 0 ? UnityMath.V2(origin.x + xx * sx + yx * sy, origin.y + xy * sx + yy * sy) : default;
            var target = UnityMath.V2(local.x, local.y - PlateLift);
            var shown = screen.z > 0 && target.x > 0 && target.y > 0 && target.x < width / scale && target.y < height / scale;
            if (ui.Get<bool>("visible") != shown) ui.Set("visible", shown);
            // shortcut: native also fades a plate to 0.5 over a movement arrow; here every plate yields input while
            // the local player moves, like the hero plates. Mirror the arrow test if the fade is missed in game.
            var touchable = !shown || !stepping;
            if (ui.Get<bool>("touchable") != touchable)
            {
                ui.Set("touchable", touchable);
                if (touchable) Untouchable.Remove(ui.Pointer); else Untouchable.Add(ui.Pointer);
            }
            if (ui.Get<float>("alpha") != 1f) ui.Set("alpha", 1f);
            if (!States.TryGetValue(ui.Pointer, out var state)) States[ui.Pointer] = state = new PlateState();
            if (!shown) { state.Placed = state.Guided = false; continue; }
            var place = UnityMath.V2(ui.Get<float>("x"), ui.Get<float>("y"));
            var plateWidth = ui.Get<float>("width");
            if (full || !state.Placed || !Same(state.Owner, owner) || !Same(state.Target, target) || state.Width != plateWidth || !Same(state.Place, place))
                ui.Call("UpdateOffset", target);
            state.Owner = owner; state.Target = target; state.Width = plateWidth; state.Place = place; state.Placed = true;
            if (full) Full.Add(ui.Pointer);
            Placed.Add((ui, local)); Before.Add(place.y); Wanted.Add(target.y);
        }
        // Stacked on the wanted places, so a crowd whose owners stand still comes to rest and the settled skip above holds.
        Rects.Clear();
        for (var i = 0; i < Placed.Count; i++) Rects.Add(PlayerIndicatorUi.WantedRect(Placed[i].Ui, Wanted[i]));
        PlayerIndicatorUi.StackRects(Rects, null);
        for (var i = 0; i < Rects.Count; i++) PlayerIndicatorUi.Ease(Placed[i].Ui, Before[i], Rects[i].Y, step);
        foreach (var (ui, owner) in Placed)
        {
            var state = States[ui.Pointer];
            var line = ui.FieldPointer("graph_Line");
            var guide = (owner.x, owner.y, ui.Get<float>("x"), ui.Get<float>("y"), ui.Field<Vector2>("Offset").x,
                RuntimeObject.GetOf<float>(line, "x"), RuntimeObject.GetOf<float>(line, "width"), MarkerLift);
            if (Full.Contains(ui.Pointer) || !state.Guided || state.Guide != guide)
            {
                var mapped = _layer!.Result<Vector2, Vector2>("TransformPoint", owner, ui);
                var point = UnityMath.V2(mapped.x, mapped.y - MarkerLift);
                ui.Call("UpdateGuideLine", point);
                state.Guide = guide; state.Guided = true;
            }
            if (RuntimeObject.GetOf<bool>(ui.FieldPointer("graph_ShowBuff"), "visible") == stepping)
            {
                ui.Field("graph_ShowBuff")!.Set("visible", !stepping);
                if (stepping) BuffHidden.Add(ui.Pointer); else BuffHidden.Remove(ui.Pointer);
            }
        }
    }

    private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    private static bool Same(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;

    // The plate's ShowInfo controller index, or -1 without one; read through the pointer the plate holds.
    private static int ShowInfo(RuntimeObject plate)
    {
        var controller = plate.FieldPointer("ShowInfo");
        var index = controller == IntPtr.Zero ? -1 : RuntimeObject.GetOf<int>(controller, "selectedIndex");
        GC.KeepAlive(plate);
        return index;
    }

    internal static void Clear()
    {
        Exception? failure = null;
        var parentAlive = _parent != null && !_parent.Get<bool>("isDisposed");
        foreach (var ui in Adopted)
        {
            try
            {
                if (ui.Get<bool>("isDisposed")) continue;
                if (Untouchable.Contains(ui.Pointer)) ui.Set("touchable", true);
                if (BuffHidden.Contains(ui.Pointer)) ui.Field("graph_ShowBuff")?.Set("visible", true);
                if (parentAlive) _parent!.Call("AddChild", ui);
            }
            catch (Exception ex) { failure ??= ex; }
        }
        Adopted.Clear(); Placed.Clear(); Untouchable.Clear(); BuffHidden.Clear(); States.Clear(); Full.Clear();
        try { GameUi.Dispose(_sentinel); } catch (Exception ex) { failure ??= ex; }
        try { GameUi.Dispose(_layer); } catch (Exception ex) { failure ??= ex; }
        _sentinel = _layer = _parent = _battleUi = null;
        _scanAt = 0;
        if (failure != null) throw failure;
    }
}
