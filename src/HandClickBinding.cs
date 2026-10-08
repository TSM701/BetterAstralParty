using Il2CppInterop.Runtime;
using NativeDelegate = Il2CppSystem.Delegate;

namespace BetterAstralParty;

// Stage processes world clicks in LateUpdate, AFTER our Update driver. Use native
// presentation callbacks, as HandOutlineBinding does, not an injected managed delegate.
internal static class HandClickBinding
{
    private static RuntimeObject? _bridge;
    private static NativeDelegate? _callback;

    internal static void Arm(RuntimeObject stage, IEnumerable<RuntimeObject> cards)
    {
        Clear();
        var klass = RuntimeObject.FindClass("FairyGUI", "EventCallback0");
        if (klass == IntPtr.Zero) throw new TypeLoadException("FairyGUI.EventCallback0");
        var type = new Il2CppSystem.Type(IL2CPP.il2cpp_type_get_object(IL2CPP.il2cpp_class_get_type(klass)));
        var controller = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "Controller"));
        foreach (var card in cards)
            foreach (var name in new[] { "GearSize", "GearXY" })
            {
                // Standalone, mod-owned gears: never overwrite the card's native gear array.
                // Setting a controller initializes the default from the current presentation.
                // No tween config => Apply restores immediately without killing native tweens.
                var gear = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", name), card);
                gear.Set("controller", controller);
                var handler = NativeDelegate.CreateDelegate(type, new Il2CppSystem.Object(gear.Pointer), "Apply");
                _callback = NativeDelegate.Combine(_callback, handler);
            }
        if (_callback == null) return;
        _bridge = stage.Get("onTouchBegin")!.Field("_bridge")!;
        var current = _bridge.Field("_callback0");
        // CallInternal invokes callback1 (native ZoomCard) before callback0. Preserve both.
        var chain = NativeDelegate.Combine(current == null ? null : new NativeDelegate(current.Pointer), _callback);
        _bridge.SetField("_callback0", new RuntimeObject(chain.Pointer));
    }

    internal static void Clear()
    {
        var current = _bridge?.Field("_callback0");
        if (current != null && _callback != null)
        {
            var chain = NativeDelegate.Remove(new NativeDelegate(current.Pointer), _callback);
            _bridge!.SetField("_callback0", chain == null ? null : new RuntimeObject(chain.Pointer));
        }
        _bridge = null; _callback = null;
    }
}
