namespace BetterAstralParty;

internal static class FieldIndicatorOpacity
{
    private static readonly AlphaOwner Monsters = new(), Players = new();
    // Hidden: under Hide When Far, from the hide distance, the name/HP plates (com_AttrInfos: native monster plates and the
    // hero clones) and the state plates (com_PlayerAttrInfos) are off.
    internal static bool Hidden { get; private set; }

    // Indicator Display (2026-11-20, Hide When Far became one of its choices): Native or HideFar. The capsule indicators were
    // its other choices until they were pulled for a remake the same day (branch archive/capsule-indicators).
    internal static void Tick(float zoomOut)
    {
        var root = GameUi.Root;
        var hide = MenuLayout.UsesDistance(Plugin.IndicatorDisplay.Value);
        if (!Plugin.IndicatorFade.Value && !hide || !float.IsFinite(zoomOut) || zoomOut <= 0 || PvpSafety.Suspended || root == null) { Clear(); return; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var field = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
        if (!GameUi.Visible(field)) { Clear(); return; }
        var ancestor = field;
        while (ancestor != null && ancestor.Pointer != root.Pointer) ancestor = ancestor.Get("parent");
        if (ancestor == null) { Clear(); return; }
        zoomOut = Math.Min(1f, zoomOut);
        // One point both ways (user-requested 2026-11-10): the zoom eases one way and stops, so the boundary cannot flicker.
        // Hide distance 0-100 %; at 0 % any zoom-out hides and the native view always shows them.
        var hideAt = Math.Clamp(Plugin.IndicatorHideDistance.Value, 0f, 1f);
        Hidden = hide && zoomOut >= hideAt;
        // The fade (user-decided 2026-11-20): opaque up to the Start Distance, then straight down to the slider's opacity
        // (1 - 0.7 x the stored strength: 30 % at 1, 0 % at 100/70), reached at the hide distance under Hide When Far, so the
        // plates fade into hiding, or at the farthest zoom-out under Native. The original curve dropped to 1 - 0.4 x strength
        // at its start, which read as a jump once the start moved off the first zoom-out, and under Hide When Far never
        // reached the slider's value before the plates hid.
        var start = Math.Clamp(Plugin.IndicatorFadeDistance.Value, 0f, 1f);
        var end = hide ? hideAt : 1f;
        var along = zoomOut < start ? 0f : end <= start ? 1f : Math.Min(1f, (zoomOut - start) / (end - start));
        var lowest = Math.Max(0f, 1f - .7f * Math.Clamp(Plugin.IndicatorFadeStrength.Value, 0f, 100f / 70f));
        var alpha = Plugin.IndicatorFade.Value ? 1f - (1f - lowest) * along : 1f;
        Monsters.Tick(field!.Field("com_AttrInfos"), alpha, Hidden);
        Players.Tick(field.Field("com_PlayerAttrInfos"), alpha, Hidden);
    }

    internal static void Clear()
    {
        Hidden = false;
        try { Monsters.Clear(); }
        finally { Players.Clear(); }
    }

    private sealed class AlphaOwner
    {
        private RuntimeObject? _node;
        private RuntimeObject? _hiddenDisplay;
        private float _original, _applied;
        private float? _attemptedAlpha;

        internal void Tick(RuntimeObject? node, float factor, bool hidden)
        {
            if (node?.Get<bool>("isDisposed") == true) node = null;
            if (node?.Pointer != _node?.Pointer) Clear();
            if (node == null) return;
            var alpha = node.Get<float>("alpha");
            if (!float.IsFinite(alpha)) { Clear(); return; }
            if (_node == null || alpha != _applied && alpha != _attemptedAlpha)
            {
                _original = _applied = alpha;
                _attemptedAlpha = null;
            }
            _node = node;
            var applied = _original * factor;
            if (alpha != applied)
            {
                // Keep the last successful value until the potentially side-effecting call returns.
                _attemptedAlpha = applied;
                node.Set("alpha", applied);
            }
            _applied = applied;
            _attemptedAlpha = null;
            if (!hidden) { RestoreVisibility(); return; }
            var display = node.Get("displayObject")
                ?? throw new InvalidOperationException("Field indicator display unavailable");
            if (display.Pointer != _hiddenDisplay?.Pointer) RestoreVisibility();
            // Preserve native visibility/gears; suppress only rendering and native hit testing.
            if (display.Get<bool>("visible"))
            {
                _hiddenDisplay = display;
                display.Set("visible", false);
            }
        }

        internal void Clear()
        {
            // Retain ownership if native restoration throws so cleanup can retry.
            try
            {
                if (_node != null && !_node.Get<bool>("isDisposed"))
                {
                    var alpha = _node.Get<float>("alpha");
                    if (alpha != _original && (alpha == _applied || alpha == _attemptedAlpha))
                        _node.Set("alpha", _original);
                }
            }
            finally { RestoreVisibility(); }
            _node = null;
            _attemptedAlpha = null;
        }

        private void RestoreVisibility()
        {
            if (_hiddenDisplay != null && !_hiddenDisplay.Get<bool>("isDisposed")
                && _node != null && !_node.Get<bool>("isDisposed") && !_hiddenDisplay.Get<bool>("visible"))
                _hiddenDisplay.Set("visible", _node.Get<bool>("internalVisible2"));
            _hiddenDisplay = null;
        }
    }
}
