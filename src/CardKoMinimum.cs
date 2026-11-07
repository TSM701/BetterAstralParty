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
    internal static KoPair? Calculate(PreRollCombat input, CardBonus? card, int fixedDie = 0, bool possible = false)
    {
        if (fixedDie is < 0 or > 6 || !CardAdvisor.Valid(input) || !input.Modifiers.DiceValid || fixedDie == 0 && input.Modifiers.AttackUnsupported) return null;
        KoPair? best = null;
        var bestArea = 0;
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
            if (defenseLimit == 0) continue;
            var area = Faces(input.Modifiers.AttackDice, attackDie, 6, fixedDie) * Faces(input.Modifiers.DefenseDice, 1, defenseLimit);
            if (area > bestArea) { best = new(attackDie, defenseLimit); bestArea = area; }
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
            if (attackLimit == 0) continue;
            var area = Faces(input.Modifiers.DefenseDice, defenseDie, 6) * Faces(input.Modifiers.AttackDice, 1, attackLimit, fixedDie);
            if (area > bestArea) { best = new(attackLimit, defenseDie); bestArea = area; }
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
            || attackDie is < 1 or > 6 || defenseDie is < 1 or > 6) return null;
        // A final-ATK boost (card 10010) applies after the die; its unobserved rounding widens the bounds.
        var low = CardAdvisor.Boost(input.AttackMin + (bonus.Attack ? bonus.Min : 0) + attackDie, bonus.FinalPercent).Floor;
        var high = CardAdvisor.Boost(input.AttackMax + (bonus.Attack ? bonus.Max : 0) + attackDie, bonus.FinalPercent).Ceiling;
        return (
            CombatAdvisor.HitDamage(Math.Max(1, low - input.DefenseMax - (bonus.Attack ? 0 : bonus.Max) - defenseDie), input.Modifiers, input.Hp),
            CombatAdvisor.HitDamage(Math.Max(1, high - input.DefenseMin - (bonus.Attack ? 0 : bonus.Min) - defenseDie), input.Modifiers, input.Hp));
    }

    internal static bool RevealedDie(int state, bool visible, int value) =>
        state == 1 && visible && value is >= 1 and <= 6;

    // The server choice writes the defender's raw die at showPoint 0 (RefreshPoint_Defender(0, val, dodge)); in the
    // logged fights it first became visible in Dice_Show at showPoint 2, before the totals (CardDiceUi.PollResolved).
    internal static bool RevealedDefenseDie(int state, bool visible, int value) =>
        state == 0 && visible && value is >= 1 and <= 6;

    // A card's rolled bonus as the native arithmetic panel shows it: "+N", or "...+N" summing the cards past the seventh.
    internal static int? CardValue(string? text)
    {
        if (text == null) return null;
        var body = text.StartsWith("...", StringComparison.Ordinal) ? text[3..] : text;
        // Fully qualified: the focused combat fixture compiles this file without its imports.
        return body.Length > 1 && body[0] == '+' && int.TryParse(body.AsSpan(1), System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value is >= -1000 and <= 1000 ? value : null;
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
