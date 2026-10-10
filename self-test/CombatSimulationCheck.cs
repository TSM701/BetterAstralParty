using BetterAstralParty;

// Offline model verification, not evidence of undocumented server rule ordering.
internal static class CombatSimulationCheck
{
    internal static void Run()
    {
        var rolls = 0;
        var scenarios = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("Combat simulator: " + name); }
        void Equal(double expected, double actual, string name) => Check(Math.Abs(expected - actual) < 1e-9, name);
        // Deliberately independent of the production resolver and recommendation helper.
        int Damage(int hp, int attack, int defense, int attackDie, int defenseDie, bool dodge, HitModifiers effect)
        {
            if (effect.Immune || dodge && (defenseDie > attackDie || defenseDie == 6 && attackDie == 6)) return 0;
            var raw = dodge ? attack + attackDie : Math.Max(1, attack + attackDie - defense - defenseDie);
            var adjusted = Math.Max(0, raw + effect.Bonus);
            if (adjusted > 0) adjusted += effect.OnHitBonus;
            if (effect.Cap > 0) adjusted = Math.Min(adjusted, effect.Cap);
            return effect.MinimumHp > 0 ? Math.Min(adjusted, Math.Max(0, hp - effect.MinimumHp)) : adjusted;
        }
        ChoiceStats Stats(int hp, int attack, int defense, int attackDie, bool dodge, HitModifiers effect)
        {
            var damage = 0; var ko = 0; var zero = 0;
            for (var die = 1; die <= 6; die++)
            {
                var hit = Damage(hp, attack, defense, attackDie, die, dodge, effect);
                damage += Math.Min(hp, hit); ko += hit >= hp ? 1 : 0; zero += hit == 0 ? 1 : 0;
            }
            return new(damage / 6d, ko / 6d, zero / 6d);
        }
        ChoiceStats Before(int hp, int attack, int defense, bool dodge, HitModifiers effect)
        {
            double damage = 0, ko = 0, zero = 0;
            for (var die = 1; die <= 6; die++)
            {
                var d = Stats(hp, attack, defense, die, false, effect);
                var e = Stats(hp, attack, defense, die, true, effect);
                var chooseDodge = dodge && (e.KnockoutChance < d.KnockoutChance - 1e-9
                    || Math.Abs(e.KnockoutChance - d.KnockoutChance) < 1e-9 && e.ExpectedDamage < d.ExpectedDamage - 1e-9);
                var choice = chooseDodge ? e : d;
                damage += choice.ExpectedDamage; ko += choice.KnockoutChance; zero += choice.NoDamageChance;
            }
            return new(damage / 6, ko / 6, zero / 6);
        }
        void Compare(ChoiceStats expected, ChoiceStats actual)
        {
            Equal(expected.ExpectedDamage, actual.ExpectedDamage, "mean HP loss");
            Equal(expected.KnockoutChance, actual.KnockoutChance, "KO risk");
            Equal(expected.NoDamageChance, actual.NoDamageChance, "zero damage chance");
        }

        foreach (var hp in new[] { 1, 2, 5, 10 })
        foreach (var attack in new[] { 0, 1, 4, 8, 20, 100 })
        foreach (var defense in new[] { 0, 1, 5, 20 })
        foreach (var effect in (from adjustment in new[] { -99, -6, -2, 0, 1, 3 }
            from cap in new[] { 0, 1, 5 } from immune in new[] { false, true }
            select new HitModifiers(adjustment, cap, immune)).Concat(new HitModifiers[] {
                new(-2) { OnHitBonus = 1 }, new(1, 1) { OnHitBonus = 2 },
                new(Immune: true) { OnHitBonus = 2, MinimumHp = 1 },
                new(-99) { OnHitBonus = 2, MinimumHp = 1 },
                new() { MinimumHp = 1 }, new() { MinimumHp = 3 },
                new(1) { MinimumHp = 1, OnHitBonus = 2 },
                new(-2, 5) { MinimumHp = 3, OnHitBonus = 1 } }))
        {
            scenarios++;
            var firstAttack = 0; var defenseLimit = 0;
            for (var atkDie = 1; atkDie <= 6; atkDie++)
            {
                var advice = CombatAdvisor.Calculate(new(hp, attack + atkDie, atkDie, defense) { Modifiers = effect });
                Compare(Stats(hp, attack, defense, atkDie, false, effect), advice.Defend);
                Compare(Stats(hp, attack, defense, atkDie, true, effect), advice.Dodge);
                var limit = 0;
                for (var defDie = 1; defDie <= 6; defDie++)
                {
                    var hit = Damage(hp, attack, defense, atkDie, defDie, false, effect);
                    var range = CardKoMinimum.DamageRange(
                        new(9, hp, attack, attack, defense, defense) { Modifiers = effect }, null, atkDie, defDie);
                    Check(range == (hit, hit), "numeric dice damage matches independent simulation");
                    Equal(hit, CombatAdvisor.FinalDamage(hp, attack + atkDie, defense + defDie, false, atkDie, effect), "final defended hit");
                    Equal(Damage(hp, attack, defense, atkDie, defDie, true, effect),
                        CombatAdvisor.FinalDamage(hp, attack + atkDie, defDie, true, atkDie, effect), "final dodged hit");
                    if (hit >= hp) limit = defDie;
                    rolls++;
                }
                if (firstAttack == 0 && limit > 0) { firstAttack = atkDie; defenseLimit = limit; }
                var fixedInput = new PreRollCombat(9, hp, attack, attack, defense, defense) { Modifiers = effect };
                Check(CardKoMinimum.Calculate(fixedInput, null, atkDie) == new KoPair(atkDie, limit), "revealed KO pair");
            }
            var input = new PreRollCombat(9, hp, attack, attack, defense, defense) { Modifiers = effect };
            foreach (var fixedDie in Enumerable.Range(0, 7))
            foreach (var survivalCard in new CardBonus?[] { null, new(true, 1, 3, 1), new(false, 1, 3, 1) })
            {
                var ranged = input with { AttackMax = attack + 2, DefenseMax = defense + 2 };
                var safe = new List<KoPair>();
                var lethal = new List<KoPair>();
                for (var a = 1; a <= 6; a++)
                for (var d = 1; d <= 6; d++)
                {
                    if (fixedDie != 0 && a != fixedDie) continue;
                    // Independent worst public endpoints: high ATK, low DEF and lowest defense-card gain.
                    var worst = Damage(hp, attack + 2 + (survivalCard is { Attack: true } ? survivalCard.Value.Max : 0),
                        defense + (survivalCard is { Attack: false } ? survivalCard.Value.Min : 0), a, d, false, effect);
                    if (worst < hp) safe.Add(new(a, d));
                    // KO requires even the lowest ATK/highest DEF/card bounds to be lethal.
                    var least = Damage(hp, attack + (survivalCard is { Attack: true } ? survivalCard.Value.Min : 0),
                        defense + 2 + (survivalCard is { Attack: false } ? survivalCard.Value.Max : 0), a, d, false, effect);
                    if (least >= hp) lethal.Add(new(a, d));
                }
                var expected = safe.Count == 0 ? new KoPair(fixedDie, 0)
                    : safe.OrderBy(p => p.DefenseDie).ThenByDescending(p => p.AttackDie).First();
                Check(CardKoMinimum.Survival(ranged, survivalCard, fixedDie) == expected,
                    "survival minimum across ranges/cards/revealed rolls and HP equality");
                var expectedKo = lethal.Count == 0 ? new KoPair(fixedDie, 0)
                    : lethal.OrderBy(p => p.AttackDie).ThenByDescending(p => p.DefenseDie).First();
                Check(CardKoMinimum.Calculate(ranged, survivalCard, fixedDie) == expectedKo,
                    "KO minimum across all revealed rolls, stat/card bounds and effects");
            }
            foreach (var attackCard in new[] { false, true })
            {
                var ranged = input with { AttackMax = attack + 2, DefenseMax = defense + 2 };
                var hits = new List<int>();
                for (var a = attack; a <= attack + 2; a++)
                for (var d = defense; d <= defense + 2; d++)
                for (var bonus = 1; bonus <= 3; bonus++)
                    hits.Add(Damage(hp, a + (attackCard ? bonus : 0), d + (attackCard ? 0 : bonus), 6, 1, false, effect));
                Check(CardKoMinimum.DamageRange(ranged, new(attackCard, 1, 3, 1), 6, 1)
                    == (hits.Min(), hits.Max()), "displayed damage range matches all public stat/card outcomes");
            }
            Check(CardKoMinimum.Calculate(input, null) == new KoPair(firstAttack, defenseLimit), "pre-roll KO pair");
            foreach (var fixedDie in new[] { 0, 1, 6 })
            {
                var expectedAttack = fixedDie; var expectedDefense = 0;
                for (var a = fixedDie == 0 ? 1 : fixedDie; a <= (fixedDie == 0 ? 6 : fixedDie); a++)
                {
                    var limit = 0;
                    for (var d = 1; d <= 6; d++)
                        if (Damage(hp, attack, defense + 6, a, d, false, effect) >= hp) limit = d;
                    if (limit == 0) continue;
                    expectedAttack = a; expectedDefense = limit; break;
                }
                Check(CardKoMinimum.Calculate(input, new(false, 1, 6, 2), fixedDie)
                    == new KoPair(expectedAttack, expectedDefense), "defense-card preview uses strongest DEF");
            }
            foreach (var dodge in new[] { false, true })
                Compare(Before(hp, attack, defense, dodge, effect), CardAdvisor.BeforeRoll(hp, attack, defense, dodge, effect));

            // Same modifier snapshot must survive the card/advisor boundary.
            var gains = new[] { false, true }.Select(dodge => (
                Before: Before(hp, attack, defense, dodge, effect),
                After: Before(hp, attack + 2, defense, dodge, effect))).ToArray();
            var card = CardAdvisor.Calculate(input, new(true, 2, 2, 1))!.Value;
            Equal(gains.Min(g => g.After.ExpectedDamage - g.Before.ExpectedDamage), card.DamageGainMin, "card lower gain");
            Equal(gains.Max(g => g.After.ExpectedDamage - g.Before.ExpectedDamage), card.DamageGainMax, "card upper gain");
            Equal(gains.Min(g => g.After.KnockoutChance - g.Before.KnockoutChance), card.KoGainMin, "card lower KO gain");
            Equal(gains.Max(g => g.After.KnockoutChance - g.Before.KnockoutChance), card.KoGainMax, "card upper KO gain");
            var encounter = EncounterAdvisor.Calculate(new EncounterInput(9, 10, attack, 0, hp, 0, defense, false)
                { EnemyModifiers = effect })!.Value;
            var ordinary = Before(hp, attack, defense, false, effect);
            var evading = Before(hp, attack, defense, true, effect);
            Equal(Math.Min(ordinary.KnockoutChance, evading.KnockoutChance), encounter.EnemyKoMin, "encounter lower KO");
            Equal(Math.Max(ordinary.KnockoutChance, evading.KnockoutChance), encounter.EnemyKoMax, "encounter upper KO");
        }
        var protectedInput = new VisibleCombat(9, 1, 1, 10, 6, 0, 3, true)
            { Modifiers = new(100, Immune: true) };
        Check(VisibleCombatAdvisor.Calculate(protectedInput)?.DefendQuick.Contains("전투불능 0%") == true, "visible immunity propagation");
        Check(protectedInput != protectedInput with { Modifiers = default }, "effect-only cache invalidation");
        var immuneCounter = EncounterAdvisor.Calculate(new EncounterInput(9, 1, 0, 0, 1000, 100, 100, true)
            { Modifiers = new(Immune: true) })!.Value;
        Equal(0, immuneCounter.SelfKoMax, "counter-side immunity");
        Console.WriteLine($"Offline combat simulator: {scenarios} effect scenarios / {rolls} dice pairs; all advisor paths agree with independent enumeration.");
    }
}
