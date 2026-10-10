namespace BetterAstralParty;

internal static class FieldIndicatorOpacity
{
    private static readonly AlphaOwner Monsters = new(), Players = new();
    internal static bool Hidden { get; private set; }

    internal static void Tick(float zoomOut)
    {
        var root = GameUi.Root;
        if (!float.IsFinite(zoomOut) || zoomOut <= 0 || PvpSafety.Suspended || root == null) { Clear(); return; }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var field = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
        if (!GameUi.Visible(field)) { Clear(); return; }
        var ancestor = field;
        while (ancestor != null && ancestor.Pointer != root.Pointer) ancestor = ancestor.Get("parent");
        if (ancestor == null) { Clear(); return; }
        zoomOut = Math.Min(1f, zoomOut);
        Hidden = Hidden ? zoomOut > .50f : zoomOut >= .60f;
        var alpha = .6f * (1f - zoomOut * .5f);
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
