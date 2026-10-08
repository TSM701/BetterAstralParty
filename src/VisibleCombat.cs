namespace BetterAstralParty;

public readonly record struct VisibleCombat(int MapType, int Hp, int AttackMin, int AttackMax,
    int AttackDie, int DefenseMin, int DefenseMax, bool DodgeAvailable)
{
    public HitModifiers Modifiers { get; init; }
}

public readonly record struct VisibleAdvice(string Defend, string Dodge)
{
    public string DefendQuick { get; init; }
    public string DodgeQuick { get; init; }
    public RecommendedAction Recommendation { get; init; }
    public string Summary { get; init; }
}

public static class VisibleCombatAdvisor
{
    public static bool IsPve(int mapType) => mapType is 4 or 6 or 9 or 10 or 12;

    public static VisibleAdvice? Calculate(VisibleCombat input)
    {
        if (!IsPve(input.MapType) || input.Hp is <= 0 or > 10000 || input.AttackDie is < 1 or > 6
            || !input.Modifiers.Valid || !input.Modifiers.DiceValid
            || input.AttackMin < 0 || input.AttackMax < input.AttackMin || input.AttackMax > 1000
            || input.DefenseMin < -1000 || input.DefenseMax < input.DefenseMin || input.DefenseMax > 1000
            || input.AttackMax - input.AttackMin > 100 || input.DefenseMax - input.DefenseMin > 100)
            return null;

        var allDefend = true;
        var allDodge = true;
        double defLow = double.PositiveInfinity, defHigh = 0, dodgeLow = double.PositiveInfinity, dodgeHigh = 0;
        double defKoLow = 1, defKoHigh = 0, dodgeKoLow = 1, dodgeKoHigh = 0;
        // Bounds, not an invented probability distribution for random card bonuses.
        for (var atk = input.AttackMin; atk <= input.AttackMax; atk++)
        for (var def = input.DefenseMin; def <= input.DefenseMax; def++)
        // A face restriction does not prove equal weights. Bound every allowed defense
        // outcome instead of inventing a distribution; the publicly revealed attack is fixed.
        for (var face = input.Modifiers.DefenseDistributionUnknown ? input.Modifiers.DefenseDice.Min : 0;
             face <= (input.Modifiers.DefenseDistributionUnknown ? input.Modifiers.DefenseDice.Max : 0); face++)
        {
            if (face > 0 && !input.Modifiers.DefenseDice.Contains(face)) continue;
            var modifiers = face == 0 ? input.Modifiers : input.Modifiers with
                { DefenseDice = DiceFaces.Range(face, face), DefenseDistributionUnknown = false };
            var result = CombatAdvisor.Calculate(new(input.Hp, atk + input.AttackDie, input.AttackDie, def)
                { Modifiers = modifiers }, false);
            var comparison = CombatAdvisor.CompareChoices(result.Defend, result.Dodge);
            allDefend &= comparison <= 0;
            allDodge &= comparison >= 0;
            defLow = Math.Min(defLow, result.Defend.ExpectedDamage);
            defHigh = Math.Max(defHigh, result.Defend.ExpectedDamage);
            dodgeLow = Math.Min(dodgeLow, result.Dodge.ExpectedDamage);
            dodgeHigh = Math.Max(dodgeHigh, result.Dodge.ExpectedDamage);
            defKoLow = Math.Min(defKoLow, result.Defend.KnockoutChance);
            defKoHigh = Math.Max(defKoHigh, result.Defend.KnockoutChance);
            dodgeKoLow = Math.Min(dodgeKoLow, result.Dodge.KnockoutChance);
            dodgeKoHigh = Math.Max(dodgeKoHigh, result.Dodge.KnockoutChance);
        }
        // Preserve unanimous choices. When bounds disagree, minimise worst-case KO,
        // then worst-case HP loss; equal value resolves to defend, without inventing odds.
        // Explicit user policy: active Shushu shield always recommends dodge,
        // including native dodge-unavailable states. Probabilities remain unchanged.
        var recommendation = input.Modifiers.ShushuShield ? RecommendedAction.Dodge
            : !input.DodgeAvailable || allDefend ? RecommendedAction.Defend
            : allDodge ? RecommendedAction.Dodge
            : CombatAdvisor.CompareChoices(new(defHigh, defKoHigh, 0), new(dodgeHigh, dodgeKoHigh, 0)) <= 0
                ? RecommendedAction.Defend : RecommendedAction.Dodge;
        var defTitle = recommendation != RecommendedAction.Defend ? "방어*"
            : !input.DodgeAvailable ? "★ 방어 추천 · 회피 불가" : "★ 방어 추천*";
        var dodgeTitle = recommendation == RecommendedAction.Dodge ? "★ 회피 추천*"
            : !input.DodgeAvailable ? "회피 불가" : "회피*";
        return new(defTitle + $"\n평균 피해 {Range(defLow, defHigh)}",
            dodgeTitle + (input.DodgeAvailable ? $"\n평균 피해 {Range(dodgeLow, dodgeHigh)}" : ""))
        {
            DefendQuick = $"예상 피해 {Range(defLow, defHigh)}\n전투불능 {CombatAdvisor.ProbabilityRange(defKoLow, defKoHigh)}%",
            DodgeQuick = input.DodgeAvailable
                ? $"예상 피해 {Range(dodgeLow, dodgeHigh)}\n전투불능 {CombatAdvisor.ProbabilityRange(dodgeKoLow, dodgeKoHigh)}%" : "회피 불가",
            Recommendation = recommendation,
            Summary = "방어자 기준 · " + (!input.DodgeAvailable && !input.Modifiers.ShushuShield ? "회피 불가"
                : recommendation == RecommendedAction.Defend ? "방어 추천" : "회피 추천")
        };
    }

    private static string Range(double min, double max) => Math.Abs(min - max) < 0.000001
        ? $"{min:0.#}" : $"{min:0.#}~{max:0.#}";
}
