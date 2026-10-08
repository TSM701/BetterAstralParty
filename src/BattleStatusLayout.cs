namespace BetterAstralParty;

internal static class BattleStatusLayout
{
    internal static int RowInputOrder(int step) => step is >= 3 and <= 6 ? 1 : 0;

    // Short, unscaled delay after DEFENSE appears, shared by local and spectator views.
    internal static bool CounterReady(bool visible, float now, ref float visibleSince)
    {
        if (!visible) { visibleSince = -1; return false; }
        if (visibleSince < 0) visibleSince = now;
        return now - visibleSince >= 0.75f;
    }

    internal const int PageSize = 6;
    internal const float CardWidth = 80, IconStep = 48, Height = 52;
    // Square buff art needs clearance from the capsule's curved ends, not just its rectangular bounds.
    internal const float EdgePadding = 20, IconSize = 44, IconInset = (IconStep - IconSize) / 2;
    internal const float Width = 2 * EdgePadding + CardWidth + PageSize * IconStep + 50;
    internal static float RowWidth(int visibleCount, bool paged) => 2 * EdgePadding + CardWidth + visibleCount * IconStep + (paged ? 50 : 0);
    internal static float IconX(int index, float width, bool attacker) =>
        attacker ? width - EdgePadding - CardWidth - (index + 1) * IconStep : EdgePadding + CardWidth + index * IconStep;
    // Centre lies just inside the header's top-right corner; most of the icon overlaps it.
    internal static (float X, float Y) CounterAnchor(float x, float y, float width, float size) =>
        (x + width - size * 0.65f, y - size * 0.35f);
    // Keep the attacker unchanged; align the defender to the native heart, not the padded HP background.
    internal static float HpEdgeX(bool attacker, float textX, float textWidth, float heartX) =>
        attacker ? textWidth : heartX - textX;
    internal static (float X, float Y, float Scale)? BelowHp(float hpX, float hpBottom, float width, float desiredScale,
        float areaWidth, float areaHeight, bool attacker)
    {
        if (!float.IsFinite(hpX + hpBottom + width + desiredScale + areaWidth + areaHeight)
            || width <= 0 || desiredScale <= 0 || hpBottom < 0) return null;
        var space = attacker ? hpX - 8 : areaWidth - hpX - 8;
        var scale = Math.Min(desiredScale, Math.Min(space / width, (areaHeight - hpBottom - 16) / Height));
        if (hpX < 8 || hpX > areaWidth - 8 || scale <= 0) return null;
        return (attacker ? hpX - width * scale : hpX, hpBottom + 8, scale);
    }
    // Camera viewport is bottom-up; FairyGUI root is top-down. Reject offscreen/behind-camera anchors.
    internal static (float X, float Y)? Project(float x, float y, float depth,
        float rectX, float rectY, float rectWidth, float rectHeight, float rootWidth, float rootHeight)
    {
        if (!float.IsFinite(x + y + depth + rectX + rectY + rectWidth + rectHeight + rootWidth + rootHeight)
            || depth <= 0 || x < 0 || x > 1 || y < 0 || y > 1
            || rectWidth <= 0 || rectHeight <= 0 || rootWidth <= 0 || rootHeight <= 0) return null;
        return ((rectX + x * rectWidth) * rootWidth, (1 - rectY - y * rectHeight) * rootHeight);
    }
    internal static bool Allowed(bool enabled, int map, int step) =>
        // Prepare from encounter through results; actual visibility follows each world-space HP label.
        enabled && VisibleCombatAdvisor.IsPve(map) && step is >= 1 and <= 6;
    internal static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    internal static int ClampPage(int page, int count) => Math.Clamp(page, 0, Pages(count) - 1);
    internal static string Counters(int progress, int rounds, bool property) =>
        ModText.Text((progress > 0 ? (property ? $"현재 수치 {progress}" : $"중첩·진행 {progress}") : "")
        + (rounds > 0 ? (progress > 0 ? " · " : "") + $"남은 턴 {rounds}" : ""));
}
