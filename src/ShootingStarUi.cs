using UnityEngine;

namespace BetterAstralParty;

// Starlight icon with a "+1 | DMG N" capsule (the Starlight gain first, under its icon) at the Release button's top-left, popping in like the recommendation
// thumb (same native relic cut-in curve, mirrored rotation) and twinkling once when the button is hovered.
// Top-left, not bottom-left: the button sits near the screen bottom, which cut the capsule off (2026-10-23).
internal static class ShootingStarUi
{
    private const float Star = 54;
    private static RuntimeObject? _root, _button, _icon, _damage, _stacks;
    private static NativeUi.Surface? _damageFill, _stacksFill;
    private static ReleaseGain? _shown;
    private static int _language = -1;
    private static float _shownAt, _twinkleAt = -10, _offeredAt, _scale = 1;
    private static bool _hovered;
    // A Starlight buff without its icon (a game change) skips the star for the session instead of failing.
    private static bool _iconMissing;

    // Called from the encounter scan (every 0.2 s); Tick animates every frame.
    internal static void Show(RuntimeObject button, ReleaseGain gain, float scale)
    {
        if (_iconMissing) return;
        _offeredAt = Time.unscaledTime; _scale = scale;
        if (_root == null || _root.Get<bool>("isDisposed") || _button?.Pointer != button.Pointer) Create(button);
        if (_root == null) return;
        if (_shown != gain || _language != ModText.Revision)
        {
            if (_shown == null) _shownAt = Time.unscaledTime;
            _shown = gain; _language = ModText.Revision;
            _damage!.Set("text", ModText.Text("피해") + " " + gain.Damage);
            _stacks!.Set("text", "+" + gain.Stacks);
            var damageWidth = 16 + _damage.Get<float>("textWidth");
            var stacksWidth = 14 + _stacks.Get<float>("textWidth");
            _stacksFill!.Resize(stacksWidth, 26f); _stacks.Call("SetSize", stacksWidth, 26f);
            _damageFill!.Graph.Call("SetXY", stacksWidth - 3, 0f); _damageFill.Resize(damageWidth, 26f);
            _damage.Call("SetXY", stacksWidth - 3, 0f); _damage.Call("SetSize", damageWidth, 26f);
        }
        _root!.Set("visible", true);
        Animate();
    }

    private static void Animate()
    {
        var button = _button!; var scale = _scale;
        var hovered = GameUi.PointerPath().Any(item => item.Pointer == button.Pointer);
        if (hovered && !_hovered) _twinkleAt = Time.unscaledTime;
        _hovered = hovered;
        var elapsed = Time.unscaledTime - _shownAt;
        var twinkle = Time.unscaledTime - _twinkleAt;
        var pop = AdviceText.RecommendationScale(elapsed) * (twinkle < 0.45f ? 1 + 0.15f * Mathf.Sin(twinkle / 0.45f * Mathf.PI) : 1);
        var rotation = elapsed < 0.5f ? 12f * (1 - elapsed / 0.5f) : twinkle < 0.45f ? -8f * Mathf.Sin(twinkle / 0.45f * Mathf.PI) : 0f;
        _root!.Call("SetScale", scale * pop, scale * pop);
        _root.Set("rotation", rotation);
        _root.Call("SetXY", -6f * scale, 20f * scale);
    }

    private static void Create(RuntimeObject button)
    {
        Clear();
        _button = button;
        _root = NativeUi.Component(button, Star, Star);
        _root.Set("touchable", false);
        _root.Call("SetPivot", 0.5f, 0.5f, true);
        _icon = UnknownEffectsRow.BuffIcon(_root, Star);
        if (!UnknownEffectsRow.SetIcon(_icon, ShootingStar.Starlight, 0, 0)) { _iconMissing = true; Plugin.Diagnostics.State("shootingStar.icon", "missing"); Clear(); return; }
        var capsule = NativeUi.Component(_root, 120, 26);
        capsule.Set("touchable", false);
        capsule.Call("SetXY", 8f, Star - 8);
        _damageFill = new NativeUi.Surface(capsule, 60, 26, fixedOpacity: true, fill: new Color(0.85f, 0.23f, 0.23f, 1f));
        _damage = NativeUi.Label(capsule, "", 0, 0, 60, 26, 15);
        _stacksFill = new NativeUi.Surface(capsule, 30, 26, fixedOpacity: true, fill: NativeUi.DarkFill);
        _stacks = NativeUi.Label(capsule, "", 0, 0, 30, 26, 15);
        foreach (var label in new[] { _damage, _stacks }) { label.Set("align", 1); label.Set("verticalAlign", 1); }
        _shown = null; _language = -1;
    }

    // Hides the star once the encounter scan (every 0.2 s) stops offering it; animates it every frame meanwhile.
    internal static void Tick()
    {
        if (_root == null) return;
        if (Time.unscaledTime - _offeredAt > 0.35f || _root.Get<bool>("isDisposed")) { Clear(); return; }
        Animate();
    }

    internal static void Clear()
    {
        GameUi.Dispose(_root);
        _root = _button = _icon = _damage = _stacks = null; _damageFill = _stacksFill = null; _shown = null; _hovered = false;
    }
}
