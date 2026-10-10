namespace BetterAstralParty;

internal enum HandGroup { Attack, Defense, Disrupt, Support, Move, Resource, Other }

// Presentation only. Native CardType is authoritative for battle cards; effect IDs are
// audited against original Card/STRCard because EffectType is NOT a functional taxonomy.
internal static class HandLayout
{
    internal static int Mode(bool enabled, string expand) => !enabled ? 0 : expand == "Click" ? 2 : 1;
    internal static int NextMode(bool enabled, string expand) => (Mode(enabled, expand) + 2) % 3;
    internal static string ModeLabel(int mode) => mode == 0 ? "기본" : mode == 2 ? "클릭 펼치기" : "호버 펼치기";
    internal const float FoldedOpacity = .75f;
    internal const float GuideRightRotation = 90f;
    internal static string DropHint(bool korean) => korean ? "카드를 이곳에\n놓으세요" : "Drop card\nhere";
    internal static Slot ClickRow(float width, float height, int count, int index, float aspect, float pileTop)
    {
        var unit = Math.Min(width / 1920f, height / 1080f);
        var gap = 10 * unit;
        var cell = Math.Min(190 * unit * 1.12f, (width * .5f - gap * (count - 1)) / count);
        var w = Math.Min(cell / 1.12f, height * .28f / (aspect * 1.12f));
        var h = w * aspect;
        var start = (width - (cell * count + gap * (count - 1))) / 2;
        // Keep the enlarged card and its outline just above the highest folded pile.
        return new(start + index * (cell + gap) + (cell - w) / 2, pileTop - 16 * unit - h * 1.06f, w, h, cell + gap);
    }

    internal static HandGroup Classify(int id, int cardType) => cardType switch
    {
        1 => HandGroup.Attack,
        2 => HandGroup.Defense,
        _ => id switch
        {
            20003 or 20015 or 20017 => HandGroup.Defense,
            20001 or 20004 or 20007 or 20009 or 20010 or 20012 or 20013 or 20018 or 20019
                or 20021 or 20025 or 20028 or 20029 or 20030 or 20031 or 20033
                or 21001 or 21007 or 21009 or 21010 or 21012 or 21013 or 21018 or 21019 => HandGroup.Disrupt,
            20002 or 20006 or 20008 or 20014 or 20020 or 20027 or 21004 or 21005 or 21006
                or 21014 or 21015 or 21021 or 21024 => HandGroup.Support,
            20005 or 20011 or 20024 or 20026 or 20032 or 21003 or 21008 or 21020 => HandGroup.Move,
            20022 or 20023 or 21002 or 21016 => HandGroup.Resource,
            // Choice/mixed exceptional/passive cards and future IDs remain visible, never guessed.
            _ => HandGroup.Other
        }
    };

    internal static string Label(HandGroup group, bool korean) => (group, korean) switch
    {
        (HandGroup.Attack, true) => "공격", (HandGroup.Attack, false) => "Attack",
        (HandGroup.Defense, true) => "방어", (HandGroup.Defense, false) => "Defense",
        (HandGroup.Disrupt, true) => "피해·방해", (HandGroup.Disrupt, false) => "Disruption",
        (HandGroup.Support, true) => "회복·지원", (HandGroup.Support, false) => "Support",
        (HandGroup.Move, true) => "이동", (HandGroup.Move, false) => "Movement",
        (HandGroup.Resource, true) => "카드·자원", (HandGroup.Resource, false) => "Resources",
        (_, true) => "기타", _ => "Other"
    };

    internal static bool Resting(float x, float y, float restX, float restY) =>
        (x - restX) * (x - restX) + (y - restY) * (y - restY) <= 1f;
    // Preserve the current point when changing an in-flight tween's endpoint.
    internal static float RetargetStart(float current, float end, float progress) =>
        progress < 1f ? (current - end * progress) / (1f - progress) : current;
    internal static bool CanResume(bool shown, bool distributing, bool inputBusy, bool foreignMotion) =>
        shown && !distributing && !inputBusy && !foreignMotion;
    internal readonly record struct Slot(float X, float Y, float Width, float Height, float Step);
    internal static bool InColumn(float x, float y, Slot top, float bottom, float margin) =>
        x >= top.X - margin && x <= top.X + top.Width + margin && y >= top.Y - margin && y <= bottom;
    internal static Slot Focus(Slot slot) => slot with
    {
        X = slot.X - slot.Width * .06f, Y = slot.Y - slot.Height * .06f,
        Width = slot.Width * 1.12f, Height = slot.Height * 1.12f
    };
    internal static bool InFocus(float x, float y, Slot slot, float border)
    {
        var hit = Focus(slot);
        return InColumn(x, y, hit, hit.Y + hit.Height + border, border);
    }
    internal static Slot Place(float width, float height, int columns, int column, int rows, int row, float aspect = 1.5f, bool expanded = false)
    {
        var unit = Math.Min(width / 1920f, height / 1080f);
        var gap = 10 * unit;
        var bottomInset = 44 * unit; // Caption begins at height - 37: keep 7px, including the outline.
        // Symmetric centre region: standing portrait at left, native skill/move controls at right.
        var available = width * 0.50f;
        var cardWidth = Math.Min(190 * unit, (available - gap * (columns - 1)) / columns);
        var start = (width - (cardWidth * columns + gap * (columns - 1))) / 2;
        var columnX = start + column * (cardWidth + gap);
        var columnWidth = cardWidth;
        var cardHeight = cardWidth * aspect;
        var step = Math.Min(32 * unit, height * .10f / Math.Max(1, rows - 1));
        if (expanded)
        {
            // Entire cards, not partially exposed strips. Fit the whole column uniformly.
            var rowGap = 8 * unit;
            // Reserve each cell's zoom footprint; focusing never moves neighbours or overlaps them.
            cardHeight = Math.Min(cardHeight, (height * .82f - bottomInset - rowGap * (rows - 1)) / rows) / 1.12f;
            cardWidth = cardHeight / aspect;
            step = cardHeight * 1.12f + rowGap;
        }
        var top = height - bottomInset - cardHeight * (expanded ? 1.06f : 1f) - step * (rows - 1);
        return new(columnX + (columnWidth - cardWidth) / 2, top + row * step, cardWidth, cardHeight, step);
    }
}
