using UnityEngine;

namespace BetterAstralParty;

// User-authorized zoom-follow for native monster nameplates (2026-10-19). Native UIBattleInfoPanel.UpdateAttrInfoPosition
// runs in FairyGUI's LateUpdate, after the mod, and lifts every direct UICom_AttrInfo child of com_AttrInfos by fixed
// 200/120 px, so a zoomed-out monster's plate floats away from it. While the field zoom differs from the native distance,
// the native plates move into an owned layer inside com_AttrInfos, which the native pass skips, and are laid out here
// with the same native steps (UpdateOffset, pairwise split, UpdateGuideLine) and lifts scaled by the zoom. The dot height
// stays native: the hero indicator position slider does not apply here. Plates return to com_AttrInfos at native zoom, on
// feature OFF and on cleanup. Native Dispose removes a plate from whichever parent holds it.
internal static class FieldMonsterPlates
{
    private const float PlateLift = 200f, MarkerLift = 120f;
    private static readonly List<RuntimeObject> Adopted = new();
    private static readonly List<(RuntimeObject Ui, Vector2 Owner)> Placed = new();
    private static RuntimeObject? _parent, _layer, _sentinel, _battleUi;
    private static IntPtr _plateClass, _sceneClass;
    private static float _scanAt;

    // Visible adopted plates, for the hero layout's fixed obstacles.
    internal static IReadOnlyList<RuntimeObject> Plates => Adopted;

    internal static bool ZoomFollows(float scale) => float.IsFinite(scale) && Math.Abs(scale - 1f) > 0.01f;

    internal static void Tick(float scale)
    {
        if (!ZoomFollows(scale) || PvpSafety.Suspended || GameUi.Root == null) { Clear(); return; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        if (logic?.Get("fight")?.Field("fightStatus")?.Value<bool>() == true) { Clear(); return; }
        var battleUi = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
        var parent = battleUi?.Field("com_AttrInfos");
        if (!GameUi.Visible(parent)) { Clear(); return; }
        if (_parent?.Pointer != parent!.Pointer || _layer == null || _layer.Get<bool>("isDisposed")) { Clear(); _parent = parent; _layer = NativeUi.Component(parent, 0, 0); }
        _battleUi = battleUi;
        if (_layer.Get<float>("width") != parent.Get<float>("width") || _layer.Get<float>("height") != parent.Get<float>("height"))
            _layer.Call("SetSize", parent.Get<float>("width"), parent.Get<float>("height"));
        if (Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 0.2f; Adopt(parent); }
        if (!FieldIndicatorOpacity.Hidden) Layout(scale);
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
        if (Adopted.Count == 0) return;
        if (_sceneClass == IntPtr.Zero) _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
        var cameraObject = RuntimeObject.StaticField(_sceneClass, "inst")?.Get("mainCamera");
        if (cameraObject == null) return;
        var camera = new Camera(cameraObject.Pointer);
        var root = GameUi.Root!;
        float width = root.Get<float>("width"), height = root.Get<float>("height");
        var info = _sentinel?.Field("ShowInfo")?.Get<int>("selectedIndex") ?? 0;
        var self = _battleUi?.Field("_SelfPlayer")?.Field("CharacterInst");
        var stepping = self != null && self.Get<int>("canStep") > 0;
        Placed.Clear();
        foreach (var ui in Adopted)
        {
            if (ui.Get<bool>("isDisposed")) continue;
            if (ui.Field("ShowInfo")?.Get<int>("selectedIndex") != info) ui.Call("SwitchInfo", info == 1);
            var owner = ui.Call("GetOwnerPos")!.Value<Vector3>();
            var screen = owner == Vector3.zero || !camera.enabled ? default : camera.WorldToScreenPoint(owner);
            var local = screen.z > 0 ? _layer!.Call("GlobalToLocal", new Vector2(screen.x, Screen.height - screen.y))!.Value<Vector2>() : default;
            var target = local - Vector2.up * (PlateLift * scale);
            var shown = screen.z > 0 && target.x > 0 && target.y > 0 && target.x < width && target.y < height;
            if (ui.Get<bool>("visible") != shown) ui.Set("visible", shown);
            // shortcut: native also fades a plate to 0.5 over a movement arrow; here every plate yields input while
            // the local player moves, like the hero plates. Mirror the arrow test if the fade is missed in game.
            var touchable = !shown || !stepping;
            if (ui.Get<bool>("touchable") != touchable) ui.Set("touchable", touchable);
            if (ui.Get<float>("alpha") != 1f) ui.Set("alpha", 1f);
            if (!shown) continue;
            ui.Call("UpdateOffset", target);
            Placed.Add((ui, local));
        }
        for (var i = 0; i < Placed.Count; i++)
            for (var j = i + 1; j < Placed.Count; j++) Split(Placed[i].Ui, Placed[j].Ui);
        foreach (var (ui, owner) in Placed)
        {
            var point = _layer!.Call("TransformPoint", owner, ui)!.Value<Vector2>() - Vector2.up * (MarkerLift * scale);
            ui.Call("UpdateGuideLine", point);
            var buffs = ui.Field("graph_ShowBuff")!;
            if (buffs.Get<bool>("visible") == stepping) buffs.Set("visible", !stepping);
        }
    }

    // Native pair rule of UpdateAttrInfoPosition: overlapping plates split the vertical overlap.
    internal static void Split(RuntimeObject a, RuntimeObject b)
    {
        float ay = a.Get<float>("y"), by = b.Get<float>("y");
        var overlapX = (a.Get<float>("width") + b.Get<float>("width")) * 0.5f - Math.Abs(a.Get<float>("x") - b.Get<float>("x"));
        var overlapY = (a.Get<float>("height") + b.Get<float>("height")) * 0.5f - Math.Abs(ay - by);
        if (overlapX <= 0 || overlapY <= 0) return;
        var shift = overlapY * 0.5f;
        var up = ay + 0.1f < by;
        a.Set("y", up ? ay - shift : ay + shift);
        b.Set("y", up ? by + shift : by - shift);
    }

    internal static void Clear()
    {
        Exception? failure = null;
        var parentAlive = _parent != null && !_parent.Get<bool>("isDisposed");
        foreach (var ui in Adopted)
        {
            try { if (parentAlive && !ui.Get<bool>("isDisposed")) _parent!.Call("AddChild", ui); }
            catch (Exception ex) { failure ??= ex; }
        }
        Adopted.Clear(); Placed.Clear();
        try { GameUi.Dispose(_sentinel); } catch (Exception ex) { failure ??= ex; }
        try { GameUi.Dispose(_layer); } catch (Exception ex) { failure ??= ex; }
        _sentinel = _layer = _parent = _battleUi = null;
        _scanAt = 0;
        if (failure != null) throw failure;
    }
}
