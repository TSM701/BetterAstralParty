using UnityEngine;

namespace BetterAstralParty;

// Same defender-benefit comparison as local buttons; never predicts their actual input.
internal sealed class ChoiceVisual
{
    internal readonly RuntimeObject Root;
    private readonly RuntimeObject _status, _left, _right;
    private readonly NativeUi.Surface _surface;
    private readonly RuntimeObject? _arrow;
    private readonly AdviceBadge _leftAdvice, _rightAdvice;
    private RecommendedAction? _last;
    private float _lastShown = -10;
    private bool _hovered;

    internal ChoiceVisual(RuntimeObject parent)
    {
        Root = NativeUi.Component(parent, 360, 186);
        Root.Set("opaque", true);
        _surface = new NativeUi.Surface(Root, 360, 186);
        var left = _left = NativeUi.Component(Root, 116, 116);
        left.Set("opaque", true);
        left.Call("SetXY", 18f, 26f);
        var right = _right = NativeUi.Component(Root, 116, 116);
        right.Set("opaque", true);
        right.Call("SetXY", 226f, 26f);
        NativeUi.Icon(left, "Com_Icon_Def", 18, 0, 80);
        NativeUi.Icon(right, "Com_Icon_Mov", 18, 0, 80);
        NativeUi.Label(left, "방어", 0, 80, 116, 30, 23);
        NativeUi.Label(right, "회피", 0, 80, 116, 30, 23);
        _arrow = NativeUi.Arrow(Root, 180, 70, 52, true);
        _status = NativeUi.Label(Root, "", 0, 0, 360, 27, 17);
        _leftAdvice = new AdviceBadge(left);
        _rightAdvice = new AdviceBadge(right);
    }

    internal void Show(VisibleAdvice advice, float scale, float x, float y)
    {
        var fresh = Time.unscaledTime - _lastShown > 0.35f;
        _lastShown = Time.unscaledTime;
        Root.Set("visible", true);
        _surface.Refresh();
        Root.Call("SetXY", x, y);
        Root.Call("SetScale", scale, scale);
        _status.Set("text", ModText.Text(advice.Summary));
        const float hoverScale = 0.8f;
        _leftAdvice.Show(advice.DefendQuick, advice.Recommendation == RecommendedAction.Defend, hoverScale,
            AdviceText.HoverAbovePanel(_left.Get<float>("y"), _leftAdvice.Panel.Get<float>("height"), hoverScale), visualOnly: true, guide: false);
        _rightAdvice.Show(advice.DodgeQuick, advice.Recommendation == RecommendedAction.Dodge, hoverScale,
            AdviceText.HoverAbovePanel(_right.Get<float>("y"), _rightAdvice.Panel.Get<float>("height"), hoverScale), visualOnly: true, guide: false);
        if (_arrow != null)
        {
            _arrow.Set("rotation", advice.Recommendation == RecommendedAction.Defend ? 180f : 0f);
            if (fresh || _last != advice.Recommendation) NativeUi.Play(_arrow, "Loop", 2);
        }
        _last = advice.Recommendation;
        Tick();
    }

    internal void Tick()
    {
        if (Root.Get<bool>("isDisposed") || !Root.Get<bool>("visible")) return;
        var path = GameUi.PointerPath();
        var over = path.Any(item => item.Pointer == Root.Pointer);
        _leftAdvice.Hover(path.Any(item => item.Pointer == _left.Pointer));
        _rightAdvice.Hover(path.Any(item => item.Pointer == _right.Pointer));
        if (over && !_hovered && _arrow != null && _last != null) NativeUi.Play(_arrow, "Loop");
        _hovered = over;
        _leftAdvice.Animate();
        _rightAdvice.Animate();
    }

    internal void Hide() { if (!Root.Get<bool>("isDisposed")) Root.Set("visible", false); }
    internal void Dispose() { _leftAdvice.Dispose(); _rightAdvice.Dispose(); GameUi.Dispose(Root); }
}
