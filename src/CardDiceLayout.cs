namespace BetterAstralParty;

internal static class CardDiceLayout
{
    internal const float ResolvedHold = 2f;
    // The last row (UnknownRowY) names shown effects the calculation does not count.
    internal const float ColumnWidth = 132, Diameter = 100, Width = ColumnWidth * 2, Height = 258, UnknownRowY = 228;
    internal static float Row(int index) => index * ColumnWidth + (ColumnWidth - Diameter) / 2;

    internal static string Title(bool survival = false) => survival ? "생존 주사위 조건" : "KO 주사위 조건";

    internal static string Status(KoPair? pair, (long Min, long Max)? damage, int hp, bool survival = false)
    {
        if (damage is { } bounds)
        {
            if (bounds.Max < hp) return "KO 불가";
            if (bounds.Min >= hp) return "확정 KO";
        }
        if (pair == null) return "계산 정보 부족";
        if (pair.Value.DefenseDie != 0) return "";
        if (damage == null) return "계산 정보 부족";
        // Possible but no roll guarantees it (only a lucky card roll reaches it): not impossible, not a threshold.
        return "보장 조건 없음";
    }

    internal static (int Die, string Symbol, string Empty) Content(KoPair? pair, int fixedDie, int row, bool survival = false, bool unconditional = false, DiceFaces defenseDice = default, int fixedDefense = 0)
    {
        if (row == 0 && fixedDie is >= 1 and <= 6) return (fixedDie, "=", "");
        if (row == 1 && fixedDefense is >= 1 and <= 6) return (fixedDefense, "=", "");
        if (unconditional) return (0, "", "—");
        if (pair == null) return (0, "", "?");
        if (pair.Value.DefenseDie == 0) return (0, "", "—");
        // A bound covering every achievable defense face is not an extra condition.
        if (row == 1 && pair.Value.DefenseDie == (survival ? defenseDice.Min : defenseDice.Max)) return (0, "", "—");
        return (row == 0 ? pair.Value.AttackDie : pair.Value.DefenseDie, (row == 0) != survival ? "≥" : "≤", "");
    }
    internal static float Alpha(float now, float shownAt, float hideAt) =>
        AdviceText.Reveal(now - shownAt, 0.2f) *
        (hideAt < 0 ? 1 : 1 - AdviceText.Reveal(now - hideAt, 0.25f));

    internal static (float X, float Y)? Place(float screenWidth, float screenHeight,
        float width, float height)
    {
        const float gap = 16;
        if (width <= 0 || height <= 0 || width + gap * 2 > screenWidth
            || height + gap * 2 > screenHeight) return null;
        return ((screenWidth - width) / 2, (screenHeight - height) / 2);
    }
}
