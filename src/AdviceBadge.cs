using UnityEngine;

namespace BetterAstralParty;

// Mod-owned children only: never tween, recolor, or intercept the game's button.
internal sealed class AdviceBadge
{
    internal readonly RuntimeObject Panel;
    private readonly RuntimeObject _text, _parent;
    private readonly NativeUi.Surface _surface;
    private RuntimeObject? _thumb, _fallback, _guide;
    private float _shownAt, _lastShown = -10, _thumbWidth, _thumbHeight, _scale = 1;
    private bool _recommended;
    private bool _active, _visualOnly, _hovered;
    private float _hoverAt, _panelY;
    private static bool _assetWarning;
    private string? _lastText;
    private (bool Visible, float Alpha, float Y)? _panelDrawn;
    private (bool Visible, float Scale, float X, float Y, float Rotation)? _thumbDrawn;

    internal AdviceBadge(RuntimeObject parent, float width = 240, float height = 76, int fontSize = 23)
    {
        _parent = parent;
        Panel = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GComponent"));
        Panel.Set("touchable", false);
        Panel.Call("SetSize", width, height);
        _surface = new NativeUi.Surface(Panel, width, height);
        _text = GameUi.NewLabel(Panel, "", fontSize, rich: true);
        var padding = Math.Min(fontSize * 0.25f, height * 0.125f);
        _text.Call("SetXY", padding, padding);
        _text.Call("SetSize", width - 2 * padding, height - 2 * padding);
        parent.Call("AddChild", Panel);
    }

    internal void Show(string text, bool recommended, float scale, float y, bool visualOnly = false, bool guide = true)
    {
        var fresh = Time.unscaledTime - _lastShown > 0.35f;
        var newRecommendation = recommended && (!_recommended || fresh);
        if (newRecommendation) _shownAt = Time.unscaledTime;
        if (fresh) _hovered = false;
        _lastShown = Time.unscaledTime;
        _recommended = recommended;
        _active = true;
        _visualOnly = visualOnly;
        _panelY = y;
        _scale = scale;
        Panel.Call("SetScale", scale, scale);
        Panel.Call("SetXY", (_parent.Get<float>("width") - Panel.Get<float>("width") * scale) / 2, y);
        _surface.Refresh();
        if (_lastText != text) { _lastText = text; _text.Set("text", AdviceText.Quick(text)); }
        Panel.Set("visible", !visualOnly);
        _panelDrawn = null;
        if (recommended && _thumb == null) TryCreateThumb();
        if (recommended && visualOnly && guide && _guide == null)
        {
            _guide = NativeUi.Arrow(_parent, _parent.Get<float>("width") / 2, -28f, 46, false);
            _guide?.Set("rotation", 90f);
        }
        if (_guide != null)
        {
            _guide.Set("visible", recommended && visualOnly && guide);
            if (newRecommendation) NativeUi.Play(_guide, "Loop", 2);
        }
        Animate();
    }

    internal void Hover(bool over)
    {
        if (over && !_hovered)
        {
            _hoverAt = Time.unscaledTime;
            if (_guide != null && _recommended) NativeUi.Play(_guide, "Loop");
        }
        _hovered = over;
    }

    private void TryCreateThumb()
    {
        try
        {
            var packageClass = RuntimeObject.FindClass("FairyGUI", "UIPackage");
            // Relic_Button_Item.txt_Recommend uses Common_Internal/r0lyc7.
            // Reference the installed asset; no copied game artwork is distributed.
            _thumb = RuntimeObject.StaticCall(packageClass, "CreateObjectFromURL", "ui://1ov1i0v9r0lyc7");
            if (_thumb == null) return;
            var ratio = 64f / Math.Max(_thumb.Get<float>("width"), _thumb.Get<float>("height"));
            _thumbWidth = _thumb.Get<float>("width") * ratio;
            _thumbHeight = _thumb.Get<float>("height") * ratio;
            _thumb.Call("SetSize", _thumbWidth, _thumbHeight);
            _thumb.Call("SetPivot", 0.5f, 0.5f, true);
            _thumb.Set("touchable", false);
            _parent.Call("AddChild", _thumb);
            Plugin.Logger.LogInfo("[추천 따봉] 칩 추천 원본 이미지 연결됨");
        }
        catch (Exception ex)
        {
            GameUi.Dispose(_thumb);
            _thumb = null;
            if (!_assetWarning) Plugin.Logger.LogWarning($"[추천 따봉] 텍스트로 대체: {ex.Message}");
            _assetWarning = true;
        }
    }

    internal void Animate()
    {
        if (Panel.Get<bool>("isDisposed")) return;
        if (_visualOnly)
        {
            var show = _active && _hovered && !string.IsNullOrEmpty(_lastText) && Plugin.ShowDetails.Value;
            var progress = AdviceText.Reveal(Time.unscaledTime - _hoverAt);
            var drawn = (show, show ? progress : 0f, _panelY + (show ? (1 - progress) * AdviceText.HoverSlide : 0f));
            if (_panelDrawn != drawn)
            {
                _panelDrawn = drawn;
                Panel.Set("visible", show);
                if (show) { Panel.Set("alpha", drawn.Item2); Panel.Set("y", drawn.Item3); }
            }
        }
        if (_thumb == null)
        {
            // Asset updates must not hide the recommendation completely.
            if (_recommended && _active)
            {
                _fallback ??= GameUi.NewLabel(_parent, "추천", 23, dark: true);
                _fallback.Call("SetSize", 80f, 34f);
                _fallback.Call("SetXY", _parent.Get<float>("width") - 40f, 8f);
                _fallback.Set("visible", true);
            }
            else if (_fallback != null) _fallback.Set("visible", false);
            return;
        }
        _fallback?.Set("visible", false);
        var visible = _recommended && _active;
        var elapsed = Time.unscaledTime - _shownAt;
        var pop = AdviceText.RecommendationScale(elapsed);
        var thumb = (visible, _scale * pop, _parent.Get<float>("width") - 8f * _scale,
            28f * _scale, elapsed < 0.5f ? -12f * (1 - elapsed / 0.5f) : 0f);
        if (_thumbDrawn == thumb) return;
        _thumbDrawn = thumb;
        _thumb.Set("visible", visible);
        if (!visible) return;
        _thumb.Call("SetScale", thumb.Item2, thumb.Item2);
        _thumb.Call("SetXY", thumb.Item3, thumb.Item4);
        _thumb.Set("rotation", thumb.Item5);
    }

    internal void Hide()
    {
        _active = false;
        _panelDrawn = null; _thumbDrawn = null;
        if (!Panel.Get<bool>("isDisposed")) Panel.Set("visible", false);
        if (_thumb != null && !_thumb.Get<bool>("isDisposed")) _thumb.Set("visible", false);
        if (_fallback != null && !_fallback.Get<bool>("isDisposed")) _fallback.Set("visible", false);
        if (_guide != null && !_guide.Get<bool>("isDisposed")) _guide.Set("visible", false);
    }

    internal void Dispose()
    {
        GameUi.Dispose(_thumb); GameUi.Dispose(_fallback); GameUi.Dispose(_guide); GameUi.Dispose(Panel);
    }
}
