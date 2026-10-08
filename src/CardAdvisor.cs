using System.Text.RegularExpressions;

namespace BetterAstralParty;

public readonly record struct CardBonus(bool Attack, int Min, int Max, int Cost)
{
    public bool PreventCounter { get; init; }
}
public readonly record struct PreRollCombat(int MapType, int Hp, int AttackMin, int AttackMax,
    int DefenseMin, int DefenseMax)
{
    public HitModifiers Modifiers { get; init; }
    public bool? CounterAvailable { get; init; }
    public int AttackerHp { get; init; }
}
public readonly record struct CardImpact(string Title, string Details, double DamageGainMin,
    double DamageGainMax, double KoGainMin, double KoGainMax)
{
    // Lower bound is zero: do not invent the server's counter-trigger probability.
    public double PreventedSelfKoMax { get; init; }
    public double PreventedSelfDamageMax { get; init; }
}

public static class CardAdvisor
{
    public static CardBonus? FromConfig(int id, int effectType, int cardType, int[] parameters,
        int buffCount, int cardCount, int cost)
    {
        if (effectType is not (1 or 2) || cardType != effectType || buffCount != 0 || cardCount != 0
            || parameters.Length is < 2 or > 3 || cost is < 0 or > 20) return null;
        // Native TutorialLogic.CalFightCardValue: first two values are additive min/max.
        // EffectType does NOT describe extra parameters. Card 10008's native catalogue
        // defines the third parameter as counter prevention; 10009/10010 use other meanings.
        // Keep this verified semantic mapping; values themselves always come from live data.
        var counter = parameters.Length == 3 && id == 10008 && effectType == 1 && parameters[2] is 0 or 1;
        if (parameters.Length == 3 && !counter) return null;
        var min = parameters[0]; var max = parameters[1];
        return min >= 0 && max >= min && max <= 100
            ? new CardBonus(effectType == 1, min, max, cost) { PreventCounter = counter && parameters[2] == 1 } : null;
    }

    // Only fully matched Korean/English additive cards. Unknown effects fail closed.
    public static CardBonus? Parse(string description, int cost)
    {
        if (description.Length > 1000 || cost is < 0 or > 20) return null;
        var plain = Regex.Replace(description,
            @"</?(?:color|size|b|i|u)(?:=[^>]+)?>|\[/?(?:color|size|b|i|u)(?:=[^\]]+)?\]", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var match = Regex.Match(plain.Trim(),
            @"\A(?:전투\s*중\s*(?<role>공격자|방어자)\s*사용\s*가능\s*[.。]?\s*)?(?<stat>공격력|방어력)\s*\+\s*(?<min>[0-9]{1,3})(?:\s*[~～〜–-]\s*(?<max>[0-9]{1,3}))?\s*[.。]?\z");
        var english = !match.Success;
        if (english)
            match = Regex.Match(plain.Trim(),
                @"\A(?<stat>ATK|DEF)\s*\+\s*(?<min>[0-9]{1,3})(?:\s*[~～〜–-]\s*(?<max>[0-9]{1,3}))?\s*[.]?\s*(?:\(Only\s+for\s+(?<role>attacker|defender)\))?\s*[.]?\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var attack = english ? match.Groups["stat"].Value.Equals("ATK", StringComparison.OrdinalIgnoreCase)
            : match.Groups["stat"].Value == "공격력";
        var role = match.Groups["role"].Value;
        if (role.Length > 0 && (english ? role.Equals("attacker", StringComparison.OrdinalIgnoreCase) : role == "공격자") != attack) return null;
        var min = int.Parse(match.Groups["min"].Value);
        var max = match.Groups["max"].Success ? int.Parse(match.Groups["max"].Value) : min;
        return min >= 0 && max >= min && max <= 100 ? new(attack, min, max, cost) : null;
    }

    public static bool Valid(PreRollCombat input) => VisibleCombatAdvisor.IsPve(input.MapType)
        && input.AttackerHp is >= 0 and <= 10000
        && input.Modifiers.Valid
        && input.Hp is > 0 and <= 10000 && input.AttackMin >= 0 && input.AttackMax <= 1000
        && input.AttackMax >= input.AttackMin && input.AttackMax - input.AttackMin <= 100
        && input.DefenseMin >= -1000 && input.DefenseMax <= 1000
        && input.DefenseMax >= input.DefenseMin && input.DefenseMax - input.DefenseMin <= 100;

    public static bool Dominates(CardBonus cheaper, CardBonus candidate) =>
        cheaper.Attack == candidate.Attack && cheaper.Cost < candidate.Cost && cheaper.Min >= candidate.Max
        && (!candidate.PreventCounter || cheaper.PreventCounter);

    public static CardImpact? Calculate(PreRollCombat input, CardBonus card)
    {
        if (!Valid(input) || !input.Modifiers.PreRollDiceKnown || card.Min < 0 || card.Max < card.Min || card.Max > 100 || card.Cost is < 0 or > 20)
            return null;
        if (card.PreventCounter && !card.Attack) return null;
        // ponytail: bounded exhaustive stat ranges; add a closed-form solver if wider ranges are needed.
        if ((long)(input.AttackMax - input.AttackMin + 1) * (input.DefenseMax - input.DefenseMin + 1)
            * (card.Max - card.Min + 1) > 20000) return null;

        double damageMin = double.PositiveInfinity, damageMax = double.NegativeInfinity;
        double koMin = double.PositiveInfinity, koMax = double.NegativeInfinity;
        double afterKoMin = 1, afterKoMax = 0;
        var cache = new Dictionary<(int Attack, int Defense, bool Dodge), ChoiceStats>();
        ChoiceStats Stats(int attack, int defense, bool dodge)
        {
            var key = (attack, defense, dodge);
            if (!cache.TryGetValue(key, out var stats))
            {
                stats = BeforeRoll(input.Hp, attack, defense, dodge, input.Modifiers);
                cache[key] = stats;
            }
            return stats;
        }
        for (var attack = input.AttackMin; attack <= input.AttackMax; attack++)
        for (var defense = input.DefenseMin; defense <= input.DefenseMax; defense++)
        // Dodge availability at the later choice phase is not yet public. Bound both scenarios.
        for (var scenario = 0; scenario < 2; scenario++)
        {
            var dodge = scenario != 0;
            var before = Stats(attack, defense, dodge);
            for (var bonus = card.Min; bonus <= card.Max; bonus++)
            {
                var after = Stats(attack + (card.Attack ? bonus : 0), defense + (card.Attack ? 0 : bonus), dodge);
                var sign = card.Attack ? 1 : -1;
                var damage = sign * (after.ExpectedDamage - before.ExpectedDamage);
                var ko = sign * (after.KnockoutChance - before.KnockoutChance);
                damageMin = Math.Min(damageMin, damage); damageMax = Math.Max(damageMax, damage);
                koMin = Math.Min(koMin, ko); koMax = Math.Max(koMax, ko);
                afterKoMin = Math.Min(afterKoMin, after.KnockoutChance);
                afterKoMax = Math.Max(afterKoMax, after.KnockoutChance);
            }
        }
        const double epsilon = 0.000001;
        // Bound the incremental protection after the boosted attack. A dead defender cannot
        // counter; an unavailable counter has zero value. Unknown trigger rules span 0..survival.
        var counterMax = card.PreventCounter && input.CounterAvailable != false ? 1 - afterKoMin : 0;
        if (counterMax < epsilon) counterMax = 0;
        var title = koMax <= epsilon && damageMax <= epsilon && counterMax <= epsilon ? "효과 없음 · 보존*"
            : koMin > epsilon || koMin >= -epsilon && damageMin > epsilon ? "효과 있음*" : "조건부 이득*";
        // Keep paired gains for card evaluation, but show only the resulting KO chance.
        var details = card.Attack
            ? $"{CombatAdvisor.ProbabilityRange(afterKoMin, afterKoMax)}%" : "";
        return new CardImpact(title, details, damageMin, damageMax, koMin, koMax)
        {
            PreventedSelfKoMax = counterMax,
            PreventedSelfDamageMax = counterMax * input.AttackerHp
        };
    }

    // Pure enumeration: no random generator or game state is accessed.
    internal static ChoiceStats BeforeRoll(int hp, int attack, int defense, bool dodgeAvailable, HitModifiers modifiers = default)
    {
        if (!modifiers.PreRollDiceKnown) throw new ArgumentOutOfRangeException(nameof(modifiers), "Unknown pre-roll distribution");
        double damage = 0, ko = 0, noDamage = 0;
        for (var die = 1; die <= 6; die++)
        {
            if (!modifiers.AttackDice.Contains(die)) continue;
            var choices = CombatAdvisor.Calculate(new(hp, attack + die, die, defense)
                { Modifiers = modifiers }, false);
            var useDodge = dodgeAvailable && choices.Recommendation == RecommendedAction.Dodge;
            var choice = useDodge ? choices.Dodge : choices.Defend;
            ko += choice.KnockoutChance;
            damage += choice.ExpectedDamage;
            noDamage += choice.NoDamageChance;
        }
        var count = modifiers.AttackDice.Count;
        return new(damage / count, ko / count, noDamage / count);
    }
}
