using Il2CppInterop.Runtime;
using UnityEngine;
using NativeDelegate = Il2CppSystem.Delegate;

namespace BetterAstralParty;

// All callbacks target existing GAME methods. No managed callback, injected type or render hook.
internal sealed class HandOutlineBinding
{
    private readonly List<(RuntimeObject Graph, Vector2 Scale)> _graphs = new();
    private readonly List<(RuntimeObject Bridge, IntPtr Applied)> _bridges = new();
    private NativeDelegate? _callback;

    internal HandOutlineBinding(RuntimeObject card, string[] fields)
    {
        var klass = RuntimeObject.FindClass("FairyGUI", "EventCallback0");
        if (klass == IntPtr.Zero) throw new TypeLoadException("FairyGUI.EventCallback0");
        var type = new Il2CppSystem.Type(IL2CPP.il2cpp_type_get_object(IL2CPP.il2cpp_class_get_type(klass)));
        foreach (var field in fields)
        {
            var graph = card.Field(field)!;
            _graphs.Add((graph, new(graph.Get<float>("scaleX"), graph.Get<float>("scaleY"))));
            var handler = NativeDelegate.CreateDelegate(type, new Il2CppSystem.Object(graph.Pointer), "HandleScaleChanged");
            _callback = NativeDelegate.Combine(_callback, handler);
        }
        foreach (var name in new[] { "onRollOver", "onRollOut" })
            _bridges.Add((card.Get(name)!.Field("_bridge")!, IntPtr.Zero));
    }

    internal bool Apply()
    {
        var changed = false;
        for (var i = 0; i < _bridges.Count; i++)
        {
            var (bridge, applied) = _bridges[i];
            var current = bridge.Field("_callback0");
            if (current?.Pointer == applied) continue;
            // Same remove/combine order as EventBridge.Add(EventCallback0). Preserve native
            // handlers/locks; RendererCard.Set can replace the chain, so reattach only if changed.
            var chain = current == null ? null : new NativeDelegate(current.Pointer);
            chain = NativeDelegate.Combine(NativeDelegate.Remove(chain, _callback), _callback);
            bridge.SetField("_callback0", new RuntimeObject(chain.Pointer));
            _bridges[i] = (bridge, chain.Pointer);
            changed = true;
        }
        // Native hover changes display scales, not these graph fields. Only initialize or rebind.
        if (changed)
            foreach (var (graph, _) in _graphs)
            {
                graph.SetField("_scaleX", 43f); graph.SetField("_scaleY", 43f);
            }
        return changed;
    }

    internal void Restore()
    {
        foreach (var (bridge, _) in _bridges)
        {
            var current = bridge.Field("_callback0");
            if (current == null) continue;
            var chain = NativeDelegate.Remove(new NativeDelegate(current.Pointer), _callback);
            bridge.SetField("_callback0", chain == null ? null : new RuntimeObject(chain.Pointer));
        }
        foreach (var (graph, scale) in _graphs)
        {
            if (graph.Get<bool>("isDisposed")) continue;
            if (graph.Get<float>("scaleX") == 43f) graph.SetField("_scaleX", scale.x);
            if (graph.Get<float>("scaleY") == 43f) graph.SetField("_scaleY", scale.y);
        }
    }
}
