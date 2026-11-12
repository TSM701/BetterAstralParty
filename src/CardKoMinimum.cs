namespace BetterAstralParty;

internal readonly record struct KoPair(int AttackDie, int DefenseDie);

internal static class CardKoMinimum
{
    // A pair is a rectangle of dice: KO is attack >= M with defense <= N, survival attack <= M with defense >= N. Damage
    // only grows with the attack die and falls with the defense die, so each row's limit gives the largest rectangle
    // from that row. The view shows the rectangle holding the most face combinations (user-chosen 2026-11-06, draft A:
    // the lowest attack die covered 28 of 40 logged certain KOs, the widest pair 36), the easier row on a tie. A
    // revealed attack die leaves one attack face, so the pair after the reveal is the same as before this rule.

    // Zero defense = no guaranteed KO pair in the supported public bounds.
    // null = unsupported data. fixedDie is a visibly revealed attack roll, never a total.
    // A side whose dice are only 0 (Moses's 약점) has no condition of its own: its row reports ZeroRow so a pair's 0 keeps
    // meaning "no pair", and CardDiceLayout shows that row as = 0.
    internal const int ZeroRow = 6;
    internal static KoPair? Calculate(PreRollCombat input, CardBonus? card, int fixedDie = 0, bool possible = false)
    {
        if (fixedDie is < 0 or > 6 || !CardAdvisor.Valid(input) || !input.Modifiers.DiceValid || fixedDie == 0 && input.Modifiers.AttackUnsupported) return null;
        KoPair? best = null;
        var bestArea = 0;
        for (var attackDie = fixedDie == 0 ? 0 : fixedDie; attackDie <= (fixedDie == 0 ? 6 : fixedDie); attackDie++)
        {
            if (fixedDie == 0 && !input.Modifiers.AttackDice.Contains(attackDie)) continue;
            var defenseLimit = -1;
            for (var defenseDie = 0; defenseDie <= 6; defenseDie++)
            {
                if (!input.Modifiers.DefenseDice.Contains(defenseDie)) continue;
                // Worst public ATK/card gain and strongest DEF: no assumed distributions.
                var damage = DamageRange(input, card, attackDie, defenseDie);
                if (damage == null) return null;
                if ((possible ? damage.Value.Max : damage.Value.Min) >= input.Hp) defenseLimit = defenseDie;
            }
            if (defenseLimit < 0) continue;
            var area = Faces(input.Modifiers.AttackDice, attackDie, 6, fixedDie) * Faces(input.Modifiers.DefenseDice, 0, defenseLimit);
            if (area > bestArea) { best = new(attackDie, input.Modifiers.DefenseDice.ZeroOnly ? ZeroRow : defenseLimit); bestArea = area; }
        }
        return best ?? new(fixedDie, 0);
    }

    // Mirror KO: the defender's required die and the highest attack die it allows, widest rectangle first.
    // Survival must hold even at the largest public damage; equality with HP is KO.
    internal static KoPair? Survival(PreRollCombat input, CardBonus? card, int fixedDie = 0, bool possible = false)
    {
        if (fixedDie is < 0 or > 6 || !CardAdvisor.Valid(input) || !input.Modifiers.DiceValid || fixedDie == 0 && input.Modifiers.AttackUnsupported) return null;
        KoPair? best = null;
        var bestArea = 0;
        for (var defenseDie = 0; defenseDie <= 6; defenseDie++)
        {
            if (!input.Modifiers.DefenseDice.Contains(defenseDie)) continue;
            var attackLimit = -1;
            for (var attackDie = fixedDie == 0 ? 0 : fixedDie; attackDie <= (fixedDie == 0 ? 6 : fixedDie); attackDie++)
            {
                if (fixedDie == 0 && !input.Modifiers.AttackDice.Contains(attackDie)) continue;
                var damage = DamageRange(input, card, attackDie, defenseDie);
                if (damage == null) return null;
                if ((possible ? damage.Value.Min : damage.Value.Max) < input.Hp) attackLimit = attackDie;
            }
            if (attackLimit < 0) continue;
            var area = Faces(input.Modifiers.DefenseDice, defenseDie, 6) * Faces(input.Modifiers.AttackDice, 0, attackLimit, fixedDie);
            if (area > bestArea) { best = new(attackLimit, input.Modifiers.DefenseDice.ZeroOnly ? ZeroRow : defenseDie); bestArea = area; }
        }
        return best ?? new(fixedDie, 0);
    }

    // Allowed faces from low to high; a revealed attack die is its one face.
    private static int Faces(DiceFaces dice, int low, int high, int fixedDie = 0)
    {
        if (fixedDie != 0) return 1;
        var count = 0;
        for (var face = low; face <= high; face++) if (dice.Contains(face)) count++;
        return count;
    }

    // Same monotone damage rules as KO thresholds, including caps, immunity and card ranges.
    // These are damage bounds for the displayed dice, not a probability or an observed roll.
    internal static (long Min, long Max)? DamageRange(PreRollCombat input, CardBonus? card,
        int attackDie, int defenseDie)
    {
        var bonus = card ?? new CardBonus(true, 0, 0, 0);
        if (!CardAdvisor.Valid(input) || !input.Modifiers.DiceValid || bonus.Min < 0 || bonus.Max < bonus.Min || bonus.Max > 100
            || attackDie is < 0 or > 6 || defenseDie is < 0 or > 6
            || attackDie == 0 && !input.Modifiers.AttackDice.Contains(0) || defenseDie == 0 && !input.Modifiers.DefenseDice.Contains(0)) return null;
        // A final-ATK boost (card 10010, played or previewed) applies after the die.
        var boost = input.Boost.Plus(bonus.FinalPercent);
        var low = boost.Apply(input.AttackMin + (bonus.Attack ? bonus.Min : 0) + attackDie).Low;
        var high = boost.Apply(input.AttackMax + (bonus.Attack ? bonus.Max : 0) + attackDie).High;
        return (
            CombatAdvisor.HitDamage(Math.Max(1, low - input.DefenseMax - (bonus.Attack ? 0 : bonus.Max) - defenseDie), input.Modifiers, input.Hp),
            CombatAdvisor.HitDamage(Math.Max(1, high - input.DefenseMin - (bonus.Attack ? 0 : bonus.Min) - defenseDie), input.Modifiers, input.Hp));
    }

    // zero: the attacker's dice are only 0 (Moses's 약점), so its shown 0 is the roll, not an unrevealed label.
    internal static bool RevealedDie(int state, bool visible, int value, bool zero = false) =>
        state == 1 && visible && (value is >= 1 and <= 6 || zero && value == 0);

    // Rolled card values (ServerFight.Rolled) replace a side's public range when they lie inside it; a value outside means
    // an effect the server counted differently, so that side keeps its range.
    internal static PreRollCombat WithRolled(PreRollCombat input, (int? Attack, int? Defense)? rolled)
    {
        if (rolled is not { } r) return input;
        if (r.Attack is { } a && a >= input.AttackMin && a <= input.AttackMax) input = input with { AttackMin = a, AttackMax = a };
        if (r.Defense is { } d && d >= input.DefenseMin && d <= input.DefenseMax) input = input with { DefenseMin = d, DefenseMax = d };
        return input;
    }

    // Extremes across every still-possible die and public stat/card range.
    internal static (long Min, long Max)? OutcomeRange(PreRollCombat input, CardBonus? card, int fixedDie = 0)
    {
        if (fixedDie is < 0 or > 6 || !CardAdvisor.Valid(input) || !input.Modifiers.DiceValid || fixedDie == 0 && input.Modifiers.AttackUnsupported) return null;
        var low = DamageRange(input, card, fixedDie == 0 ? input.Modifiers.AttackDice.Min : fixedDie, input.Modifiers.DefenseDice.Max);
        var high = DamageRange(input, card, fixedDie == 0 ? input.Modifiers.AttackDice.Max : fixedDie, input.Modifiers.DefenseDice.Min);
        return low is { } min && high is { } max ? (min.Min, max.Max) : null;
    }
}
