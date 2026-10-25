using UnityEngine;

namespace BetterAstralParty;

// Keeps the thrown field dice where they appear in the native default view. Native DiceManager.ThrowDice places each
// DiceController root at the player position + (0, 48, 0) (two dice: ±10 on x/z) and animates only its Container
// child, so moving the root by the camera's displacement from the native default view (zoom and free-view pan)
// keeps the whole roll at the same screen pose.
// Only the root position is touched; native placement wins whenever it moves the root, and it is restored on exit.
internal static class FieldDiceUi
{
    private const string Feature = "FieldDice";
    // The wrapper keeps the pooled controller alive while tracked; a raw pointer could outlive a scene unload.
    private sealed class Tracked { internal RuntimeObject Controller = null!; internal Vector3 Native, Applied; }
    private static readonly Dictionary<IntPtr, Tracked> Dice = new();
    private static readonly List<IntPtr> Gone = new();
    private static IntPtr _managerClass;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { Update(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update()
    {
        // Pin Dice to Screen OFF returns tracked dice to their native field position and stops tracking.
        var enabled = Plugin.FieldZoom.Value && Plugin.DiceLock.Value && !PvpSafety.Suspended;
        var delta = enabled ? FieldZoomUi.DiceViewDelta : Vector3.zero;
        // In the free view a roll is tracked even while the delta reads zero, so the diagnostics see it.
        var watch = delta != Vector3.zero || enabled && FieldFreeCamera.Holding;
        if (!watch && Dice.Count == 0) return;
        // Picks up a new throw on its first frame; the pool holds only a few controllers.
        if (watch) Scan();
        Gone.Clear();
        foreach (var (pointer, dice) in Dice)
        {
            var component = new Component(dice.Controller.Pointer);
            if (!component) { Gone.Add(pointer); continue; }
            var transform = component.transform;
            if (!component.gameObject.activeInHierarchy) { Release(transform, dice); Gone.Add(pointer); continue; }
            var position = transform.position;
            // A new throw or a native move replaces our base; never fight native placement.
            if ((position - dice.Applied).sqrMagnitude > 0.0001f) dice.Native = position;
            dice.Applied = dice.Native + delta;
            if ((position - dice.Applied).sqrMagnitude > 0.0001f) transform.position = dice.Applied;
        }
        foreach (var pointer in Gone) Dice.Remove(pointer);
        // Bounded (logged on change): which native view pinned the tracked dice, so an unpinned roll names its cause.
        if (Dice.Count > 0) Plugin.Diagnostics.State("fieldDice", "tracked=" + Dice.Count + "; pinned=" + (delta != Vector3.zero) + "; view=" + FieldZoomUi.DiceViewSource);
    }

    // Pooled controllers: DiceManager._diceControllerPool.Cache holds every created DiceController.
    private static void Scan()
    {
        if (_managerClass == IntPtr.Zero) _managerClass = RuntimeObject.FindClass("Core", "DiceManager");
        var cache = RuntimeObject.StaticField(_managerClass, "_inst")?.Field("_diceControllerPool")?.Field("Cache");
        if (cache == null) return;
        var keys = cache.Get("Keys")!.Call("GetEnumerator")!;
        try
        {
            while (keys.Call("MoveNext")!.Value<bool>())
            {
                var controller = keys.Get("Current");
                if (controller == null || Dice.ContainsKey(controller.Pointer)) continue;
                var component = new Component(controller.Pointer);
                if (!component || !component.gameObject.activeInHierarchy) continue;
                var position = component.transform.position;
                Dice[controller.Pointer] = new Tracked { Controller = controller, Native = position, Applied = position };
            }
        }
        finally { keys.Call("Dispose"); }
    }

    private static void Release(Transform transform, Tracked dice)
    {
        if ((transform.position - dice.Applied).sqrMagnitude <= 0.0001f) transform.position = dice.Native;
    }

    internal static void Clear()
    {
        foreach (var dice in Dice.Values)
            try { var component = new Component(dice.Controller.Pointer); if (component) Release(component.transform, dice); }
            catch { } // A destroyed scene object needs no restore.
        Dice.Clear();
    }
}
