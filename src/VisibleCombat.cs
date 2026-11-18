namespace BetterAstralParty;

public readonly record struct VisibleCombat(int MapType, int Hp, int AttackMin, int AttackMax,
    int AttackDie, int DefenseMin, int DefenseMax, bool DodgeAvailable)
{
    public HitModifiers Modifiers { get; init; }
    // The attacker's used Powerful Attack cards, for viewers who know them (ServerFight.Boost).
    public FinalBoost Boost { get; init; }
}

public readonly record struct VisibleAdvice(string Defend, string Dodge)
{
    public string DefendQuick { get; init; }
    public string DodgeQuick { get; init; }
    public RecommendedAction Recommendation { get; init; }
    // The observer panel's figures (ChoiceVisual): damage ranges and KO chances without their labels. The dodge pair is
    // null while the native dodge is unavailable.
    public string DefendDamage { get; init; }
    public string DefendKnockout { get; init; }
    public string? DodgeDamage { get; init; }
    public string? DodgeKnockout { get; init; }
}

public static class VisibleCombatAdvisor
{
    internal static Func<int, bool>? PveClassifier { get; set; }
    public static bool IsPve(int mapType) => PveClassifier?.Invoke(mapType) ?? (mapType is 4 or 6 or 9 or 10 or 12);

    public static VisibleAdvice? Calculate(VisibleCombat input)
    {
        if (!IsPve(input.MapType) || input.Hp is <= 0 or > 10000 || input.AttackDie is < 0 or > 6
            || input.AttackDie == 0 && !input.Modifiers.AttackDice.ZeroOnly
            || !input.Modifiers.Valid || !input.Modifiers.DiceValid || !input.Boost.Valid
            || input.AttackMin < 0 || input.AttackMax < input.AttackMin || input.AttackMax > 1000
            || input.DefenseMin < -1000 || input.DefenseMax < input.DefenseMin || input.DefenseMax > 1000
            || input.AttackMax - input.AttackMin > 100 || input.DefenseMax - input.DefenseMin > 100)
            return null;

        var allDodge = true;
        double defLow = double.PositiveInfinity, defHigh = 0, dodgeLow = double.PositiveInfinity, dodgeHigh = 0;
        double defKoLow = 1, dodgeKoLow = 1;
        // Bounds, not an invented probability distribution for random card bonuses.
        for (var atk = input.AttackMin; atk <= input.AttackMax; atk++)
        // A final-ATK boost raises ATK + die; both candidate rules bound it.
        for (var rule = 0; rule < (input.Boost.Count > 0 ? 2 : 1); rule++)
        for (var def = input.DefenseMin; def <= input.DefenseMax; def++)
        // A face restriction does not prove equal weights. Bound every allowed defense
        // outcome instead of inventing a distribution; the publicly revealed attack is fixed.
        for (var face = input.Modifiers.DefenseDistributionUnknown ? input.Modifiers.DefenseDice.Min : 0;
             face <= (input.Modifiers.DefenseDistributionUnknown ? input.Modifiers.DefenseDice.Max : 0); face++)
        {
            if (face > 0 && !input.Modifiers.DefenseDice.Contains(face)) continue;
            var modifiers = face == 0 ? input.Modifiers : input.Modifiers with
                { DefenseDice = DiceFaces.Range(face, face), DefenseDistributionUnknown = false };
            var boosted = input.Boost.Apply(atk + input.AttackDie);
            var combat = new CombatInput(input.Hp, (int)(rule == 1 ? boosted.High : boosted.Low), input.AttackDie, def)
                { Modifiers = modifiers };
            var result = CombatAdvisor.Calculate(combat, false);
            var failedDodge = CombatAdvisor.FailedDodgeDamage(combat);
            allDodge &= result.Recommendation == RecommendedAction.Dodge;
            defLow = Math.Min(defLow, result.Defend.ExpectedDamage);
            defHigh = Math.Max(defHigh, result.Defend.ExpectedDamage);
            dodgeLow = Math.Min(dodgeLow, failedDodge);
            dodgeHigh = Math.Max(dodgeHigh, failedDodge);
            defKoLow = Math.Min(defKoLow, result.Defend.KnockoutChance);
            dodgeKoLow = Math.Min(dodgeKoLow, result.Dodge.KnockoutChance);
        }
        // Dodge only when CombatAdvisor's rule picks it in every public outcome. An active Shushu shield
        // recommends dodge even where the native dodge is unavailable. Probabilities remain unchanged.
        var recommendation = input.Modifiers.ShushuShield || input.DodgeAvailable && allDodge
            ? RecommendedAction.Dodge : RecommendedAction.Defend;
        var defTitle = recommendation != RecommendedAction.Defend ? "방어*"
            : !input.DodgeAvailable ? "★ 방어 추천 · 회피 불가" : "★ 방어 추천*";
        var dodgeTitle = recommendation == RecommendedAction.Dodge ? "★ 회피 추천*"
            : !input.DodgeAvailable ? "회피 불가" : "회피*";
        return new(defTitle + $"\n평균 피해 {Range(defLow, defHigh)}",
            dodgeTitle + (input.DodgeAvailable ? $"\n예상 피해 {Range(dodgeLow, dodgeHigh)}" : ""))
        {
            DefendQuick = $"예상 피해 {Range(defLow, defHigh)}\n전투불능 {CombatAdvisor.Probability(defKoLow)}%",
            DodgeQuick = input.DodgeAvailable
                ? $"예상 피해 {Range(dodgeLow, dodgeHigh)}\n전투불능 {CombatAdvisor.Probability(dodgeKoLow)}%" : "회피 불가",
            Recommendation = recommendation,
            DefendDamage = Range(defLow, defHigh),
            DefendKnockout = CombatAdvisor.Probability(defKoLow) + "%",
            DodgeDamage = input.DodgeAvailable ? Range(dodgeLow, dodgeHigh) : null,
            DodgeKnockout = input.DodgeAvailable ? CombatAdvisor.Probability(dodgeKoLow) + "%" : null
        };
    }

    private static string Range(double min, double max) => Math.Abs(min - max) < 0.000001
        ? FormattableString.Invariant($"{min:0.#}") : FormattableString.Invariant($"{min:0.#}~{max:0.#}");
}
