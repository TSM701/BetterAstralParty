using System.Text.RegularExpressions;

namespace BetterAstralParty;

public readonly record struct CardBonus(bool Attack, int Min, int Max, int Cost)
{
    public bool PreventCounter { get; init; }
    // Card 10010: this fight's final ATK (after the die) +N % (FinalBoost).
    public int FinalPercent { get; init; }
    // Card 10013: the defender's DEF -N for this hit (an attack card's debuff on its target).
    public int TargetDefense { get; init; }
}
public readonly record struct PreRollCombat(int MapType, int Hp, int AttackMin, int AttackMax,
    int DefenseMin, int DefenseMax)
{
    public HitModifiers Modifiers { get; init; }
    // Boosts the attacker already used, for viewers who know them (ServerFight.Boost); a previewed card adds its own.
    public FinalBoost Boost { get; init; }
    public bool? CounterAvailable { get; init; }
    public int AttackerHp { get; init; }
    // A player defender: spectators then see that player's own survival view.
    public bool DefenderPlayer { get; init; }
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
        // Card 10013 (Mamushi's 龙之咆哮, what 10012 becomes in her dragon form): Params [ATK min, ATK max, counter
        // prevention, buff 1261203 on the target, its move -9, its DEF -3], STRBuff 1261203 "DEF -3, move -9". Played on
        // 2026-11-14: ATK 42 -> 45 and the monster's DEF 4 -> 1, and the server's totals agreed (47 = 45 + die 2, 4 = 1 + die 3).
        if (id == 10013 && effectType == 1 && cardType == 1 && buffCount == 0 && cardCount == 0 && parameters.Length == 6
            && parameters[2] is 0 or 1 && parameters[3] == 1261203 && parameters[5] is >= -100 and <= 0 && cost is >= 0 and <= 20)
            return parameters[0] >= 0 && parameters[1] >= parameters[0] && parameters[1] <= 100
                ? new CardBonus(true, parameters[0], parameters[1], cost) { PreventCounter = parameters[2] == 1, TargetDefense = -parameters[5] } : null;
        if (effectType is not (1 or 2) || cardType != effectType || buffCount != 0 || cardCount != 0
            || parameters.Length is < 2 or > 3 || cost is < 0 or > 20) return null;
        // Native TutorialLogic.CalFightCardValue: first two values are additive min/max.
        // EffectType does NOT describe extra parameters. Card 10008's native catalogue
        // defines the third parameter as counter prevention; 10009 grants a later card.
        // 10010's third parameter is the final-ATK percent of its description ("final ATK +50% for this battle").
        // Keep this verified semantic mapping; values themselves always come from live data.
        var counter = parameters.Length == 3 && id == 10008 && effectType == 1 && parameters[2] is 0 or 1;
        var grantsCard = parameters.Length == 3 && id == 10009 && effectType == 1 && parameters[2] == 10010;
        var finalBoost = parameters.Length == 3 && id == 10010 && effectType == 1 && parameters[2] is > 0 and <= 100;
        if (parameters.Length == 3 && !counter && !grantsCard && !finalBoost) return null;
        var min = parameters[0]; var max = parameters[1];
        return min >= 0 && max >= min && max <= 100
            ? new CardBonus(effectType == 1, min, max, cost) { PreventCounter = counter && parameters[2] == 1, FinalPercent = finalBoost ? parameters[2] : 0 } : null;
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
        && input.Modifiers.Valid && input.Boost.Valid
        && input.Hp is > 0 and <= 10000 && input.AttackMin >= 0 && input.AttackMax <= 1000
        && input.AttackMax >= input.AttackMin && input.AttackMax - input.AttackMin <= 100
        && input.DefenseMin >= -1000 && input.DefenseMax <= 1000
        && input.DefenseMax >= input.DefenseMin && input.DefenseMax - input.DefenseMin <= 100;

    public static bool Dominates(CardBonus cheaper, CardBonus candidate) =>
        cheaper.Attack == candidate.Attack && cheaper.Cost < candidate.Cost && cheaper.Min >= candidate.Max
        && (!candidate.PreventCounter || cheaper.PreventCounter) && cheaper.FinalPercent >= candidate.FinalPercent
        && cheaper.TargetDefense >= candidate.TargetDefense;

    // The defender's DEF after an attack card's debuff. Whether the server stops it at 0 is not observed: clamp gives
    // the stopped value (never above the DEF before), and both rules bound every outcome.
    internal static int LoweredDefense(int defense, int lowered, bool clamp) =>
        clamp ? Math.Max(defense - lowered, Math.Min(defense, 0)) : defense - lowered;

    public static CardImpact? Calculate(PreRollCombat input, CardBonus card)
    {
        if (!Valid(input) || !input.Modifiers.PreRollDiceKnown || card.Min < 0 || card.Max < card.Min || card.Max > 100 || card.Cost is < 0 or > 20
            || card.TargetDefense is < 0 or > 100)
            return null;
        if ((card.PreventCounter || card.TargetDefense > 0) && !card.Attack) return null;
        // ponytail: bounded exhaustive stat ranges; add a closed-form solver if wider ranges are needed.
        if ((long)(input.AttackMax - input.AttackMin + 1) * (input.DefenseMax - input.DefenseMin + 1)
            * (card.Max - card.Min + 1) > 20000) return null;

        double damageMin = double.PositiveInfinity, damageMax = double.NegativeInfinity;
        double koMin = double.PositiveInfinity, koMax = double.NegativeInfinity;
        double afterKoMin = 1, afterKoMax = 0;
        var cache = new Dictionary<(int Attack, int Defense, bool Dodge, FinalBoost Boost, bool High), ChoiceStats>();
        ChoiceStats Stats(int attack, int defense, bool dodge, FinalBoost boost, bool high)
        {
            var key = (attack, defense, dodge, boost, high);
            if (!cache.TryGetValue(key, out var stats))
            {
                stats = BeforeRoll(input.Hp, attack, defense, dodge, input.Modifiers, boost, high);
                cache[key] = stats;
            }
            return stats;
        }
        var boosted = input.Boost.Plus(card.FinalPercent);
        for (var attack = input.AttackMin; attack <= input.AttackMax; attack++)
        for (var defense = input.DefenseMin; defense <= input.DefenseMax; defense++)
        // Dodge availability at the later choice phase is not yet public. Bound both scenarios.
        for (var scenario = 0; scenario < 2; scenario++)
        // Both candidate rules of a final-ATK boost bound the result, each applied before and after the card alike.
        for (var rule = 0; rule < (boosted.Count > 0 ? 2 : 1); rule++)
        // A DEF debuff on the target bounds both the stopped-at-0 and the unstopped rule.
        for (var clamp = 0; clamp < (card.TargetDefense > 0 ? 2 : 1); clamp++)
        {
            var dodge = scenario != 0;
            var before = Stats(attack, defense, dodge, input.Boost, rule == 1);
            var lowered = LoweredDefense(defense, card.TargetDefense, clamp == 1);
            for (var bonus = card.Min; bonus <= card.Max; bonus++)
            {
                var after = Stats(attack + (card.Attack ? bonus : 0), card.Attack ? lowered : defense + bonus, dodge, boosted, rule == 1);
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
            ? $"{CombatAdvisor.Probability(afterKoMax)}%" : "";
        return new CardImpact(title, details, damageMin, damageMax, koMin, koMax)
        {
            PreventedSelfKoMax = counterMax,
            PreventedSelfDamageMax = counterMax * input.AttackerHp
        };
    }

    // Pure enumeration: no random generator or game state is accessed.
    internal static ChoiceStats BeforeRoll(int hp, int attack, int defense, bool dodgeAvailable, HitModifiers modifiers = default,
        FinalBoost boost = default, bool high = false)
    {
        if (!modifiers.PreRollDiceKnown) throw new ArgumentOutOfRangeException(nameof(modifiers), "Unknown pre-roll distribution");
        double damage = 0, ko = 0, noDamage = 0;
        for (var die = 0; die <= 6; die++)
        {
            if (!modifiers.AttackDice.Contains(die)) continue;
            var boosted = boost.Apply(attack + die);
            var choices = CombatAdvisor.Calculate(new(hp, (int)(high ? boosted.High : boosted.Low), die, defense)
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
