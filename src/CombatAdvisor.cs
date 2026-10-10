namespace BetterAstralParty;

// Zero is the ordinary 1..6 set. Bits represent achievable faces, not hidden rolls.
public readonly record struct DiceFaces(byte Mask = 0)
{
    private uint Bits => Mask == 0 ? 126u : Mask;
    public bool Valid => (Mask & ~126) == 0;
    public int Min => System.Numerics.BitOperations.TrailingZeroCount(Bits);
    public int Max => System.Numerics.BitOperations.Log2(Bits);
    public int Count => System.Numerics.BitOperations.PopCount(Bits);
    public bool Contains(int die) => die is >= 1 and <= 6 && (Bits & (1u << die)) != 0;
    internal static DiceFaces Range(int min, int max) => min is >= 1 and <= 6 && max >= min && max <= 6
        ? new((byte)(((1 << (max + 1)) - 1) & ~((1 << min) - 1))) : new(128);
}

// A value snapshot shared by every advisor and cache key. Native ATK/DEF buffs
// are already resolved in the displayed stats, never added here a second time.
public readonly record struct HitModifiers(int Bonus = 0, int Cap = 0, bool Immune = false)
{
    // Recommendation policy only; do not infer shield identity from reduction magnitude.
    public bool ShushuShield { get; init; }
    public int MinimumHp { get; init; }
    public int OnHitBonus { get; init; }
    public DiceFaces AttackDice { get; init; }
    public DiceFaces DefenseDice { get; init; }
    public DiceFaces DodgeDice { get; init; }
    public bool DiceUnsupported { get; init; }
    public bool AttackDistributionUnknown { get; init; }
    public bool DefenseDistributionUnknown { get; init; }
    public bool Valid => Bonus is >= -10000 and <= 100 && Cap is >= 0 and <= 10000
        && MinimumHp is >= 0 and <= 10000 && OnHitBonus is >= 0 and <= 100;
    public bool DiceValid => !DiceUnsupported && AttackDice.Valid && DefenseDice.Valid && DodgeDice.Valid;
    public bool PreRollDiceKnown => DiceValid && !AttackDistributionUnknown && !DefenseDistributionUnknown;
}

public readonly record struct CombatInput(
    int Hp,
    int FinalAttack,
    int AttackerDie,
    int BaseDefense,
    int DefenseBonusMin = 0,
    int DefenseBonusMax = 0)
{
    public HitModifiers Modifiers { get; init; }
}

public readonly record struct ChoiceStats(
    double ExpectedDamage,
    double KnockoutChance,
    double NoDamageChance);

public readonly record struct CombatAdvice(
    ChoiceStats Defend,
    ChoiceStats Dodge,
    RecommendedAction Recommendation,
    string ReasonKo);

public enum RecommendedAction
{
    Defend,
    Dodge
}

public static class CombatAdvisor
{
    // Runtime trace: final totals are visible during choice (5); result (6) hides them.
    internal static bool IsFinalHpPhase(int step) => step == 5;

    // Dice_Show publishes showResult 2 on a failed dodge, then RefreshPoint_Defender
    // resets changeState to defense and displays zero. The icon alone loses the choice.
    internal static bool IsDodge(int changeState, int showResult) =>
        changeState == 1 || showResult is 1 or 2;

    // Inputs are the final numbers already displayed by the native result UI.
    internal static int RemainingHp(int hp, int attack, int defense, bool dodge,
        int attackDie, HitModifiers modifiers)
        => Math.Max(0, hp - FinalDamage(hp, attack, defense, dodge, attackDie, modifiers));

    internal static int FinalDamage(int hp, int attack, int defense, bool dodge,
        int attackDie, HitModifiers modifiers)
    {
        if (hp is < 0 or > 10000 || attack is < 0 or > 10000 || defense is < 0 or > 10000
            || !modifiers.Valid
            || dodge && (attackDie is < 1 or > 6 || defense is > 6))
            throw new ArgumentOutOfRangeException(nameof(hp));
        // Zero here is the native failed-dodge total, not an achievable pre-roll die.
        var escaped = dodge && (defense > attackDie || defense == 6 && attackDie == 6);
        var damage = escaped ? 0 : HitDamage(dodge ? attack : Math.Max(1, attack - defense), modifiers, hp);
        return (int)damage;
    }

    public static CombatAdvice Calculate(CombatInput input) => Calculate(input, true);

    internal static CombatAdvice Calculate(CombatInput input, bool includeReason)
    {
        Validate(input);

        var defend = EnumerateDefend(input);
        var dodge = EnumerateDodge(input);
        var recommendation = Choose(defend, dodge);
        var action = recommendation == RecommendedAction.Defend ? "방어" : "회피";
        var reason = !includeReason ? "" : Math.Abs(defend.KnockoutChance - dodge.KnockoutChance) > 0.000001
            ? $"{action}: 전투불능 확률 우선"
            : Math.Abs(defend.ExpectedDamage - dodge.ExpectedDamage) > 0.000001
                ? $"{action}: 같은 전투불능 확률, 체력 손실 우선" : "동률";

        return new CombatAdvice(defend, dodge, recommendation, reason);
    }

    private static ChoiceStats EnumerateDefend(CombatInput input)
    {
        var outcomes = 0;
        double totalDamage = 0;
        var knockouts = 0;
        var noDamage = 0;

        for (long bonus = input.DefenseBonusMin; bonus <= input.DefenseBonusMax; bonus++)
        for (var die = 1; die <= 6; die++)
        {
            if (!input.Modifiers.DefenseDice.Contains(die)) continue;
            var damage = HitDamage(Math.Max(1L, (long)input.FinalAttack - input.BaseDefense - bonus - die), input.Modifiers, input.Hp);
            // All advisors compare HP actually lost, never overkill damage.
            totalDamage += Math.Min(input.Hp, damage);
            knockouts += damage >= input.Hp ? 1 : 0;
            noDamage += damage == 0 ? 1 : 0;
            outcomes++;
        }

        return new ChoiceStats(
            (double)totalDamage / outcomes,
            (double)knockouts / outcomes,
            (double)noDamage / outcomes);
    }

    private static ChoiceStats EnumerateDodge(CombatInput input)
    {
        var successes = 0;

        for (var die = 1; die <= 6; die++)
        {
            if (!input.Modifiers.DodgeDice.Contains(die)) continue;
            successes += die > input.AttackerDie || die == 6 && input.AttackerDie == 6 ? 1 : 0;
        }

        var count = input.Modifiers.DodgeDice.Count;
        var failures = count - successes;
        var damage = HitDamage(input.FinalAttack, input.Modifiers, input.Hp);
        return new ChoiceStats(
            (double)failures * Math.Min(input.Hp, damage) / count,
            damage >= input.Hp ? (double)failures / count : 0,
            damage == 0 ? 1 : (double)successes / count);
    }

    // Successful dodges never enter this resolver. Zero-damage events are not hits.
    // ponytail: single-hit model. Additive modifiers precede the cap; verify mixed
    // shield/increase ordering in solo play before treating it as an exact game rule.
    internal static long HitDamage(long baseDamage, HitModifiers modifiers, int hp)
    {
        if (baseDamage <= 0 || modifiers.Immune) return 0;
        var damage = Math.Max(0, baseDamage + modifiers.Bonus);
        if (damage > 0) damage += modifiers.OnHitBonus;
        damage = Math.Min(damage, modifiers.Cap > 0 ? modifiers.Cap : long.MaxValue);
        return modifiers.MinimumHp > 0 ? Math.Min(damage, Math.Max(0, hp - modifiers.MinimumHp)) : damage;
    }

    private static RecommendedAction Choose(ChoiceStats defend, ChoiceStats dodge) =>
        CompareChoices(defend, dodge) <= 0 ? RecommendedAction.Defend : RecommendedAction.Dodge;

    // Negative: defend is better for the defender. Positive: dodge. Zero: equal value.
    internal static int CompareChoices(ChoiceStats defend, ChoiceStats dodge)
    {
        const double epsilon = 0.000_001;
        if (Math.Abs(defend.KnockoutChance - dodge.KnockoutChance) > epsilon)
            return defend.KnockoutChance.CompareTo(dodge.KnockoutChance);

        return Math.Abs(defend.ExpectedDamage - dodge.ExpectedDamage) <= epsilon
            ? 0 : defend.ExpectedDamage.CompareTo(dodge.ExpectedDamage);
    }

    // Callers select the endpoint favorable to the local side, not an average of
    // unknown scenarios. Tiny nonzero odds must not become guaranteed 0/100%.
    internal static string Probability(double chance)
    {
        const double epsilon = 1e-9;
        var percent = chance * 100;
        if (percent > epsilon && percent < 0.01) return "<0.01";
        if (percent < 100 - epsilon && percent > 99.99) return ">99.99";
        return Math.Round(percent, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Validate(CombatInput input)
    {
        if (input.Hp <= 0) throw new ArgumentOutOfRangeException(nameof(input), "HP는 1 이상이어야 합니다.");
        if (input.FinalAttack < 0) throw new ArgumentOutOfRangeException(nameof(input), "최종 공격력은 음수일 수 없습니다.");
        if (!input.Modifiers.Valid || !input.Modifiers.DiceValid || input.Modifiers.DefenseDistributionUnknown)
            throw new ArgumentOutOfRangeException(nameof(input), "Invalid hit modifiers");
        if (input.AttackerDie is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(input), "공격 주사위는 1~6이어야 합니다.");
        if (input.DefenseBonusMin > input.DefenseBonusMax || (long)input.DefenseBonusMax - input.DefenseBonusMin > 100)
            throw new ArgumentOutOfRangeException(nameof(input), "방어 보정 범위가 올바르지 않습니다.");
    }
}
