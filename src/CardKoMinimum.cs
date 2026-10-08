namespace BetterAstralParty;

internal readonly record struct KoPair(int AttackDie, int DefenseDie);

internal static class CardKoMinimum
{
    // Zero defense = no guaranteed KO pair in the supported public bounds.
    // null = unsupported data. fixedDie is a visibly revealed attack roll, never a total.
    internal static KoPair? Calculate(PreRollCombat input, CardBonus? card, int fixedDie = 0, bool possible = false)
    {
        if (fixedDie is < 0 or > 6 || !CardAdvisor.Valid(input) || !input.Modifiers.DiceValid) return null;
        for (var attackDie = fixedDie == 0 ? 1 : fixedDie; attackDie <= (fixedDie == 0 ? 6 : fixedDie); attackDie++)
        {
            if (fixedDie == 0 && !input.Modifiers.AttackDice.Contains(attackDie)) continue;
            var defenseLimit = 0;
            for (var defenseDie = 1; defenseDie <= 6; defenseDie++)
            {
                if (!input.Modifiers.DefenseDice.Contains(defenseDie)) continue;
                // Worst public ATK/card gain and strongest DEF: no assumed distributions.
                var damage = DamageRange(input, card, attackDie, defenseDie);
                if (damage == null) return null;
                if ((possible ? damage.Value.Max : damage.Value.Min) >= input.Hp) defenseLimit = defenseDie;
            }
            if (defenseLimit > 0) return new(attackDie, defenseLimit);
        }
        return new(fixedDie, 0);
    }

    // Mirror KO: minimize the defender's required die, then maximize the allowed attack die.
    // Survival must hold even at the largest public damage; equality with HP is KO.
    internal static KoPair? Survival(PreRollCombat input, CardBonus? card, int fixedDie = 0, bool possible = false)
    {
        if (fixedDie is < 0 or > 6 || !CardAdvisor.Valid(input) || !input.Modifiers.DiceValid) return null;
        for (var defenseDie = 1; defenseDie <= 6; defenseDie++)
        {
            if (!input.Modifiers.DefenseDice.Contains(defenseDie)) continue;
            var attackLimit = 0;
            for (var attackDie = fixedDie == 0 ? 1 : fixedDie; attackDie <= (fixedDie == 0 ? 6 : fixedDie); attackDie++)
            {
                if (fixedDie == 0 && !input.Modifiers.AttackDice.Contains(attackDie)) continue;
                var damage = DamageRange(input, card, attackDie, defenseDie);
                if (damage == null) return null;
                if ((possible ? damage.Value.Min : damage.Value.Max) < input.Hp) attackLimit = attackDie;
            }
            if (attackLimit > 0) return new(attackLimit, defenseDie);
        }
        return new(fixedDie, 0);
    }

    // Same monotone damage rules as KO thresholds, including caps, immunity and card ranges.
    // These are damage bounds for the displayed dice, not a probability or an observed roll.
    internal static (long Min, long Max)? DamageRange(PreRollCombat input, CardBonus? card,
        int attackDie, int defenseDie)
    {
        var bonus = card ?? new CardBonus(true, 0, 0, 0);
        if (!CardAdvisor.Valid(input) || !input.Modifiers.DiceValid || bonus.Min < 0 || bonus.Max < bonus.Min || bonus.Max > 100
            || attackDie is < 1 or > 6 || defenseDie is < 1 or > 6) return null;
        return (
            CombatAdvisor.HitDamage(Math.Max(1, input.AttackMin + (bonus.Attack ? bonus.Min : 0)
                + attackDie - input.DefenseMax - (bonus.Attack ? 0 : bonus.Max) - defenseDie), input.Modifiers, input.Hp),
            CombatAdvisor.HitDamage(Math.Max(1, input.AttackMax + (bonus.Attack ? bonus.Max : 0)
                + attackDie - input.DefenseMin - (bonus.Attack ? 0 : bonus.Min) - defenseDie), input.Modifiers, input.Hp));
    }

    internal static bool RevealedDie(int state, bool visible, int value) =>
        state == 1 && visible && value is >= 1 and <= 6;

    // Extremes across every still-possible die and public stat/card range.
    internal static (long Min, long Max)? OutcomeRange(PreRollCombat input, CardBonus? card, int fixedDie = 0)
    {
        if (fixedDie is < 0 or > 6 || !CardAdvisor.Valid(input) || !input.Modifiers.DiceValid) return null;
        var low = DamageRange(input, card, fixedDie == 0 ? input.Modifiers.AttackDice.Min : fixedDie, input.Modifiers.DefenseDice.Max);
        var high = DamageRange(input, card, fixedDie == 0 ? input.Modifiers.AttackDice.Max : fixedDie, input.Modifiers.DefenseDice.Min);
        return low is { } min && high is { } max ? (min.Min, max.Max) : null;
    }
}
