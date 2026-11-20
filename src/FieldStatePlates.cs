using UnityEngine;

namespace BetterAstralParty;

// Zoom-follow for the native state plates that stand on each character (UICom_PlayerAttrInfo: state icon, the Modify
// timer and other counters, attribute changes, and the mod's counter badge) and for upgrade tips (2026-10-23). Native
// UIBattleInfoPanel puts each at its character's screen point every LateUpdate and draws the content at fixed pixel
// heights above it, so a zoomed-out character leaves them floating high above. While the field zoom differs from the
// native distance they move into an owned layer inside their container, which the native pass skips, and sit at the
// same point lowered so the content keeps its height in proportion to the zoom. They return at native zoom, on feature
// OFF, PvP and cleanup. Native Dispose removes a plate from whichever parent holds it.
internal static class FieldStatePlates
{
    private sealed class Group(string container, string type)
    {
        internal readonly string Container = container, Type = type;
        internal readonly List<RuntimeObject> Adopted = new();
        internal RuntimeObject? Parent, Layer;
    }
    private static readonly Group StatePlates = new("com_PlayerAttrInfos", "UICom_PlayerAttrInfo"),
        UpgradeTips = new("com_UpgradeTips", "UIBattleInfo_Com_UpgradeTips");
    private static readonly Group[] Groups = { StatePlates, UpgradeTips };
    private static IntPtr _sceneClass, _roadClass;
    private static float _scanAt;

    // Adopted character state plates, for the counter badges.
    internal static IReadOnlyList<RuntimeObject> Players => StatePlates.Adopted;

    internal static void Tick(float scale)
    {
        if (!FieldMonsterPlates.ZoomFollows(scale) || PvpSafety.Suspended || GameUi.Root == null) { Clear(); return; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        if (logic?.Get("fight")?.Field("fightStatus")?.Value<bool>() == true && !FightPip.ShowsField) { Clear(); return; }
        var battleUi = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
        var now = Time.unscaledTime;
        var scan = now >= _scanAt;
        if (scan) _scanAt = now + 0.2f;
        if (_sceneClass == IntPtr.Zero) _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
        var cameraObject = RuntimeObject.StaticField(_sceneClass, "inst")?.Get("mainCamera");
        foreach (var group in Groups)
        {
            var parent = battleUi?.Field(group.Container);
            if (!GameUi.Visible(parent)) { Release(group); continue; }
            if (group.Parent?.Pointer != parent!.Pointer || group.Layer == null || group.Layer.Get<bool>("isDisposed"))
            { Release(group); group.Parent = parent; group.Layer = NativeUi.Component(parent, 0, 0); }
            if (group.Layer.Get<float>("width") != parent.Get<float>("width") || group.Layer.Get<float>("height") != parent.Get<float>("height"))
                group.Layer.Call("SetSize", parent.Get<float>("width"), parent.Get<float>("height"));
            if (scan) Adopt(group);
            if (group == UpgradeTips) DropRemovedTips();
            // The far modes hide only the state plates; upgrade tips stay visible and keep following their character.
            if (cameraObject != null && (group == UpgradeTips || !FieldIndicatorOpacity.Hidden)) Layout(group, FieldZoomUi.CameraOf(cameraObject), scale);
        }
    }

    private static void Adopt(Group group)
    {
        group.Adopted.RemoveAll(ui => ui.Get<bool>("isDisposed") || ui.Get("parent")?.Pointer != group.Layer!.Pointer);
        for (var i = group.Parent!.Get<int>("numChildren") - 1; i >= 0; i--)
        {
            var child = group.Parent.Call("GetChildAt", i)!;
            if (child.TypeName != group.Type) continue;
            group.Layer!.Call("AddChild", child);
            group.Adopted.Add(child);
        }
    }

    // Native RoadLineManager.DestroyRoad removes tips with com_UpgradeTips.RemoveChild(tip, true), which misses a tip held
    // in the owned layer and left it on screen. A tip its list no longer holds is one the native removed: dispose it.
    private static void DropRemovedTips()
    {
        if (UpgradeTips.Adopted.Count == 0) return;
        if (_roadClass == IntPtr.Zero) _roadClass = RuntimeObject.FindClass("Core", "RoadLineManager");
        if (RuntimeObject.StaticField(_roadClass, "_inst")?.Field("UpgradeTips") is not { } live) return;
        var count = live.Get<int>("Count");
        UpgradeTips.Adopted.RemoveAll(tip =>
        {
            for (var i = 0; i < count; i++)
                if (live.CallPointer("get_Item", i) == tip.Pointer) return false;
            GameUi.Dispose(tip);
            return true;
        });
    }

    // Per plate and frame: no wrapper for a value result or a child, no argument array (2026-11-13; zoomed out the plate
    // layers allocated about 12 KB a frame).
    // Value math through UnityMath, and the camera and screen read once a pass (2026-11-18).
    private static void Layout(Group group, Camera camera, float scale)
    {
        if (group.Adopted.Count == 0) return;
        // Character-sized (user-decided 2026-11-20): each plate's anchor (pivot) is its character's point, so the layer drawn at
        // the zoom's scale shrinks and grows the content around it, in place of lowering it by its lift times (1 - scale).
        if (group.Layer!.Get<float>("scaleX") != scale) group.Layer.Call("SetScale", scale, scale);
        var enabled = camera.enabled;
        float screenHeight = Screen.height;
        foreach (var ui in group.Adopted)
        {
            if (ui.Get<bool>("isDisposed")) continue;
            var owner = Owner(ui);
            var screen = owner is not { } point || UnityMath.IsZero(point) || !enabled ? default : camera.WorldToScreenPoint(point);
            var shown = screen.z > 0;
            if (ui.Get<bool>("visible") != shown) ui.Set("visible", shown);
            if (!shown) continue;
            var local = group.Layer!.Result<Vector2, Vector2>("GlobalToLocal", UnityMath.V2(screen.x, screenHeight - screen.y));
            ui.Call("SetXY", local.x, local.y);
        }
    }

    // Native owners: a state plate's character (GetOwnerPos, zero when hidden or dead), an upgrade tip's Target transform.
    private static Vector3? Owner(RuntimeObject ui) => ui.TypeName == "UICom_PlayerAttrInfo"
        ? ui.Result<Vector3>("GetOwnerPos")
        : ui.FieldPointer("Target") is var target && target != IntPtr.Zero ? RuntimeObject.GetOf<Vector3>(target, "position") : null;

    private static void Release(Group group)
    {
        Exception? failure = null;
        var parentAlive = group.Parent != null && !group.Parent.Get<bool>("isDisposed");
        foreach (var ui in group.Adopted)
        {
            try { if (parentAlive && !ui.Get<bool>("isDisposed")) group.Parent!.Call("AddChild", ui); }
            catch (Exception ex) { failure ??= ex; }
        }
        group.Adopted.Clear();
        try { GameUi.Dispose(group.Layer); } catch (Exception ex) { failure ??= ex; }
        group.Layer = group.Parent = null;
        if (failure != null) throw failure;
    }

    internal static void Clear()
    {
        _scanAt = 0;
        try { Release(StatePlates); }
        finally { Release(UpgradeTips); }
    }
}
