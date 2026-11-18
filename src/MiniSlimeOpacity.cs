using UnityEngine;

namespace BetterAstralParty;

// 미니 슬라임 불투명도 / Mini Slime Opacity (필드·카메라, 20-100 %, default 100 %; user-requested 2026-11-18, rules in
// .claude/rules/field.md). Lulu's Mini Slimes are land-buff summons (SummonInfoConfigure 11211 of character 112, born effect
// 11203 "Summon_112_Heal"): BattleLogic.summon (SummonLogic) keeps them in _landSummonDict, each LandBuffData's Summon holding
// the effect object (Core.Effect, from EffectManager's pool). The slime body (the "zhuti" particle renderers, material
// M_ShiLaiMu_001) draws with "Inutan/URP/Effect/ParticleAll Opaque", which writes no alpha, so below 100 % a body renderer gets
// a copy of its material on the effect's own transparent "Inutan/URP/Effect/ParticleAll" with straight alpha blending
// (__SrcBlend 5, __DstBlend 10, __ZWrite 0, as the effect's Alp materials have), and every renderer's copy scales _MainColor's
// alpha. The originals come back at 100 %, PvP, the breaker and core failure; a destroyed renderer's copy is destroyed. A
// pooled effect keeps its copy meanwhile, so a slime taken from the pool again shows the same opacity.
internal static class MiniSlimeOpacity
{
    internal const string Feature = "MiniSlimeOpacity";
    private const int SummonId = 11211;
    // The shaders' names carry a Chinese description after these (2026.11.2-dev.44 compared the whole names, found neither
    // and only scaled the alpha the opaque body ignores: no visible change, user-reported 2026-11-18).
    private const string Family = "Inutan/URP/Effect/ParticleAll";
    private const string OpaqueFamily = "Inutan/URP/Effect/ParticleAll Opaque";
    private const float Interval = .25f;
    private static readonly Dictionary<IntPtr, (Renderer Renderer, Material Original, Material Copy, float Alpha)> Faded = new();
    private static IntPtr _logicClass;
    private static float _next, _applied = 1f;
    private static bool _noShader;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { TickCore(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // Every renderer back on its own material; the copies destroyed.
    internal static void Clear()
    {
        foreach (var (renderer, original, copy, _) in Faded.Values)
        {
            try { if (renderer) renderer.sharedMaterial = original; } catch { /* A destroyed renderer has nothing to restore. */ }
            if (copy) UnityEngine.Object.Destroy(copy);
        }
        if (Faded.Count > 0) Plugin.Diagnostics.State("miniSlime", "restored " + Faded.Count);
        Faded.Clear();
        _applied = 1f;
    }

    private static void TickCore()
    {
        var now = Time.unscaledTime;
        if (now < _next) return;
        _next = now + Interval;
        var opacity = Math.Clamp(Plugin.MiniSlimeOpacity.Value, .2f, 1f);
        if (opacity >= .999f || PvpSafety.Suspended) { if (Faded.Count > 0) Clear(); return; }
        foreach (var key in Faded.Where(entry => !entry.Value.Renderer).Select(entry => entry.Key).ToList())
        {
            if (Faded[key].Copy) UnityEngine.Object.Destroy(Faded[key].Copy);
            Faded.Remove(key);
        }
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var summons = RuntimeObject.StaticField(_logicClass, "_inst")?.Get("battle")?.Field("summon")?.Field("_landSummonDict");
        if (summons != null)
        {
            var values = summons.Get("Values")!.Call("GetEnumerator")!;
            try
            {
                while (values.Call("MoveNext")!.Value<bool>())
                {
                    var summon = values.Get("Current")?.Get("Summon");
                    if (summon?.Field("trapConfig")?.Field<int>("id_") != SummonId) continue;
                    var effect = summon.Field("_effectObject");
                    if (effect != null && effect.Field<IntPtr>("m_CachedPtr") != IntPtr.Zero) Fade(new Component(effect.Pointer), opacity);
                }
            }
            finally { values.Call("Dispose"); }
        }
        if (_applied == opacity) return;
        _applied = opacity;
        foreach (var (_, _, copy, alpha) in Faded.Values) Alpha(copy, alpha, opacity);
    }

    private static void Fade(Component effect, float opacity)
    {
        var renderers = effect.GetComponentsInChildren<Renderer>(true);
        // The splashes' own transparent shader (Shader.Find needs the whole name, suffix included).
        Shader? transparent = null;
        foreach (var renderer in renderers)
            if (renderer.sharedMaterial is { } material && material.shader is { } shader && IsTransparent(shader.name)) { transparent = shader; break; }
        var before = Faded.Count;
        var bodies = 0;
        foreach (var renderer in renderers)
        {
            if (Faded.ContainsKey(renderer.Pointer)) continue;
            var original = renderer.sharedMaterial;
            if (original == null || !original.HasProperty("_MainColor")) continue;
            var name = original.shader?.name ?? "";
            var opaque = name.StartsWith(OpaqueFamily, StringComparison.Ordinal);
            if (!opaque && !IsTransparent(name)) continue;
            if (opaque && transparent == null)
            {
                if (!_noShader) { _noShader = true; Plugin.Diagnostics.State("miniSlime", "no transparent shader beside the opaque body"); }
                continue;
            }
            if (opaque) bodies++;
            var copy = new Material(original) { hideFlags = HideFlags.HideAndDontSave };
            if (opaque)
            {
                copy.shader = transparent;
                copy.SetFloat("__SrcBlend", 5f);  // UnityEngine.Rendering.BlendMode.SrcAlpha
                copy.SetFloat("__DstBlend", 10f); // OneMinusSrcAlpha
                copy.SetFloat("__ZWrite", 0f);
                copy.renderQueue = 3000;
            }
            var alpha = original.GetColor("_MainColor").a;
            Alpha(copy, alpha, opacity);
            renderer.sharedMaterial = copy;
            Faded[renderer.Pointer] = (renderer, original, copy, alpha);
        }
        if (before == 0 && Faded.Count > 0)
            Plugin.Diagnostics.State("miniSlime", FormattableString.Invariant($"faded to {opacity:P0}; renderers={Faded.Count}; bodies={bodies}"));
    }

    private static bool IsTransparent(string name) =>
        (name == Family || name.StartsWith(Family + " ", StringComparison.Ordinal)) && !name.StartsWith(OpaqueFamily, StringComparison.Ordinal);

    private static void Alpha(Material copy, float alpha, float opacity)
    {
        if (!copy) return;
        var color = copy.GetColor("_MainColor");
        color.a = alpha * opacity;
        copy.SetColor("_MainColor", color);
    }
}
