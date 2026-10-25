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
    private static IntPtr _sceneClass, _roadClass;
    private static float _scanAt;

    // Adopted character state plates, for the counter badges.
    internal static IReadOnlyList<RuntimeObject> Players => StatePlates.Adopted;

    internal static void Tick(float scale)
    {
        if (!FieldMonsterPlates.ZoomFollows(scale) || PvpSafety.Suspended || GameUi.Root == null) { Clear(); return; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        if (logic?.Get("fight")?.Field("fightStatus")?.Value<bool>() == true) { Clear(); return; }
        var battleUi = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
        var scan = Time.unscaledTime >= _scanAt;
        if (scan) _scanAt = Time.unscaledTime + 0.2f;
        if (_sceneClass == IntPtr.Zero) _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
        var cameraObject = RuntimeObject.StaticField(_sceneClass, "inst")?.Get("mainCamera");
        foreach (var group in new[] { StatePlates, UpgradeTips })
        {
            var parent = battleUi?.Field(group.Container);
            if (!GameUi.Visible(parent)) { Release(group); continue; }
            if (group.Parent?.Pointer != parent!.Pointer || group.Layer == null || group.Layer.Get<bool>("isDisposed"))
            { Release(group); group.Parent = parent; group.Layer = NativeUi.Component(parent, 0, 0); }
            if (group.Layer.Get<float>("width") != parent.Get<float>("width") || group.Layer.Get<float>("height") != parent.Get<float>("height"))
                group.Layer.Call("SetSize", parent.Get<float>("width"), parent.Get<float>("height"));
            if (scan) Adopt(group);
            if (group == UpgradeTips) DropRemovedTips();
            // Hide When Far hides only the state plates; upgrade tips stay visible and keep following their character.
            if (cameraObject != null && (group == UpgradeTips || !FieldIndicatorOpacity.Hidden)) Layout(group, new Camera(cameraObject.Pointer), scale);
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
                if (live.Call("get_Item", i)?.Pointer == tip.Pointer) return false;
            GameUi.Dispose(tip);
            return true;
        });
    }

    private static void Layout(Group group, Camera camera, float scale)
    {
        foreach (var ui in group.Adopted)
        {
            if (ui.Get<bool>("isDisposed")) continue;
            var owner = Owner(ui);
            var screen = owner is not { } point || point == Vector3.zero || !camera.enabled ? default : camera.WorldToScreenPoint(point);
            var shown = screen.z > 0;
            if (ui.Get<bool>("visible") != shown) ui.Set("visible", shown);
            if (!shown) continue;
            var local = group.Layer!.Call("GlobalToLocal", new Vector2(screen.x, Screen.height - screen.y))!.Value<Vector2>();
            ui.Call("SetXY", local.x, local.y + ContentLift(ui) * (1 - scale));
        }
    }

    // Native owners: a state plate's character (GetOwnerPos, zero when hidden or dead), an upgrade tip's Target transform.
    private static Vector3? Owner(RuntimeObject ui) => ui.TypeName == "UICom_PlayerAttrInfo"
        ? ui.Call("GetOwnerPos")!.Value<Vector3>()
        : ui.Field("Target") is { } target ? target.Get<Vector3>("position") : null;

    // Pixels between the plate's anchor (the character's screen point) and the bottom of its visible content above it.
    internal static float ContentLift(RuntimeObject ui)
    {
        var anchor = ui.Field("_pivotAsAnchor")!.Value<bool>() ? ui.Get<float>("pivotY") * ui.Get<float>("height") : 0;
        var bottom = float.NegativeInfinity;
        for (int i = 0, count = ui.Get<int>("numChildren"); i < count; i++)
        {
            var child = ui.Call("GetChildAt", i)!;
            if (child.Get<bool>("visible")) bottom = Math.Max(bottom, child.Get<float>("yMin") + child.Get<float>("height"));
        }
        return float.IsFinite(bottom) ? Math.Max(0, anchor - bottom) : 0;
    }

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
