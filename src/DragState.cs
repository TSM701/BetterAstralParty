namespace BetterAstralParty;

// FairyGUI keeps GObject.draggingObject when the dragged object misses its touch end, e.g. a hand card used on a
// monster starts a fight mid-drag. A drag seen while no button is held is stale until FairyGUI clears or replaces it,
// so it must not gate hand layout or field input.
// shortcut: a new drag of the very object left stale reads as stale until it ends; the hand's own press latch covers cards.
internal static class DragState
{
    private static IntPtr _stale;

    internal static bool Live(IntPtr drag, bool held)
    {
        if (drag == IntPtr.Zero) { _stale = IntPtr.Zero; return false; }
        if (!held) _stale = drag;
        return drag != _stale;
    }
}
