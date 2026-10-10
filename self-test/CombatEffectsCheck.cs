using BetterAstralParty;

internal static class CombatEffectsCheck
{
    internal static void Run()
    {
        PublicCombatEffectsCheck.Run();
        CombatSimulationCheck.Run();
        CombatLogReplayCheck.Run();
        CheckThresholdRanges();
        void Require(bool condition, string name)
        {
            if (!condition) throw new Exception("Combat effects: " + name);
        }
        var basic = new CombatInput(2, 6, 4, 4);
        Require(!CombatAdvisor.IsDodge(0, 0) && CombatAdvisor.IsDodge(1, 0)
            && CombatAdvisor.IsDodge(0, 1) && CombatAdvisor.IsDodge(0, 2), "public dodge choice survives native icon reset");
        Require(CombatAdvisor.FinalDamage(10, 2, 0, true, 1, default) == 2,
            "logged failed dodge uses full attack, not a defense die");
        Require(CombatAdvisor.FinalDamage(10, 0, 0, true, 1, new(1)) == 0,
            "zero attack on failed dodge must not invent minimum damage or trigger bonuses");
        var berserk = basic with { Modifiers = new(1) };
        var before = CombatAdvisor.Calculate(basic);
        var after = CombatAdvisor.Calculate(berserk);
        Require(before.Recommendation == RecommendedAction.Defend && before.Defend.KnockoutChance == 0, "baseline defend");
        Require(after.Recommendation == RecommendedAction.Dodge && after.Defend.KnockoutChance == 1, "Berserk flips choice");
        Require(Math.Abs(after.Dodge.KnockoutChance - 4d / 6) < 1e-9, "dodge success ignores modifier");
        Require(CombatAdvisor.HitDamage(0, new(1), 10) == 0 && CombatAdvisor.HitDamage(1, new(1), 10) == 2, "modifier after minimum, no hit no bonus");
        var visible = new VisibleCombat(9, 2, 2, 2, 4, 4, 4, true);
        Require(VisibleCombatAdvisor.Calculate(visible)?.Recommendation == RecommendedAction.Defend, "visible baseline");
        Require(VisibleCombatAdvisor.Calculate(visible with { Modifiers = new(1) })?.Recommendation == RecommendedAction.Dodge, "visible propagation");
        Require(VisibleCombatAdvisor.Calculate(visible with { Modifiers = new(1), DodgeAvailable = false })?.Recommendation == RecommendedAction.Defend, "unavailable dodge");
        foreach (var available in new[] { false, true })
        foreach (var reduction in new[] { -99, 0 })
        foreach (var attack in new[] { 2, 100 })
        {
            var normal = visible with { AttackMin = attack, AttackMax = attack + 1,
                DodgeAvailable = available, Modifiers = new(reduction) };
            var forced = normal with { Modifiers = normal.Modifiers with { ShushuShield = true } };
            var advice = VisibleCombatAdvisor.Calculate(forced)!.Value;
            var baseline = VisibleCombatAdvisor.Calculate(normal)!.Value;
            Require(advice.Recommendation == RecommendedAction.Dodge
                && advice.Summary == "방어자 기준 · 회피 추천"
                && !advice.Defend.Contains("추천") && advice.Dodge.Contains("회피 추천"), "Shushu always recommends dodge");
            Require(advice.DefendQuick == baseline.DefendQuick && advice.DodgeQuick == baseline.DodgeQuick,
                "recommendation override preserves probabilities and availability facts");
            Require(normal != forced, "shield identity invalidates advice cache");
        }
        Require(visible != visible with { Modifiers = new(1) }, "visible cache invalidation");
        var cardInput = new PreRollCombat(9, 2, 2, 2, 4, 4);
        Require(cardInput != cardInput with { Modifiers = new(1) }, "card cache invalidation");
        Require(CardAdvisor.BeforeRoll(2, 2, 4, false, new(1)).KnockoutChance == 1, "pre-roll shared resolver");
        Require(CardAdvisor.Calculate(cardInput with { Modifiers = new(1) }, new(true, 1, 3, 1)) != null, "card effect context accepted");
        var encounter = new EncounterInput(9, 10, 2, 4, 2, 2, 4, false);
        Require(EncounterAdvisor.Calculate(encounter with { EnemyModifiers = new(1) })!.Value.EnemyKoMax == 1, "outgoing encounter modifier");
        Require(EncounterAdvisor.Calculate(encounter with { Modifiers = new(1) }) == EncounterAdvisor.Calculate(encounter), "no incoming hit without counter");
        Require(VisibleCombatAdvisor.Calculate(visible with { Modifiers = new(-10001) }) == null, "bounded reduction");
        Require(!CardAdvisor.Valid(cardInput with { Modifiers = new(101) }), "bounded modifier");
        Require(CombatAdvisor.HitDamage(1, new(-99), 10) == 0, "shield removes minimum hit");
        Require(CombatAdvisor.HitDamage(100, new(-99), 10) == 1, "shield is not immunity");
        Require(CombatAdvisor.HitDamage(99, new(-98), 10) == 1, "shield plus Berserk additive model");
        Require(CombatAdvisor.HitDamage(3, new(-5), 10) == 0, "reduction cannot heal");
        Require(CombatAdvisor.HitDamage(100, new(1, 1), 10) == 1, "cap after increase");
        Require(CombatAdvisor.HitDamage(1, new(-99, 1), 10) == 0, "cap cannot create damage");
        var shield = CombatAdvisor.Calculate(basic with { Modifiers = new(-99) });
        Require(shield.Defend.NoDamageChance == 1 && shield.Defend.KnockoutChance == 0, "defense shield statistics");
        Require(shield.Dodge.NoDamageChance == 1 && shield.Recommendation == RecommendedAction.Defend, "shield tie prefers defense");
        Require(CardAdvisor.BeforeRoll(2, 100, 0, false, new(0, 1)).KnockoutChance == 0, "pre-roll cap propagated");
        Require(VisibleCombatAdvisor.Calculate(visible with { Modifiers = new(-99) }) != null, "visible reduction accepted");
        Require(visible != visible with { Modifiers = new(Cap: 1) } && cardInput != cardInput with { Modifiers = new(Cap: 1) }, "cap invalidates caches");
        Require(EncounterAdvisor.Calculate(encounter with { EnemyModifiers = new(Cap: 1) })!.Value.EnemyKoMax == 0, "encounter cap propagated");
        Require(!CardAdvisor.Valid(cardInput with { Modifiers = new(Cap: -1) }), "negative cap rejected");
        Require(CombatAdvisor.Calculate(basic with { Modifiers = new(Cap: 1) }).Defend.ExpectedDamage == 1, "cap preserves positive hit");
        Console.WriteLine("Combat effects: 29 checks passed");
        Require(CombatAdvisor.RemainingHp(9, 7, 3, false, 0, new(0, 0)) == 5, "final HP uses final totals once");
        Require(CombatAdvisor.RemainingHp(9, 7, 3, false, 0, new(-99, 0)) == 9, "final HP shield");
        Require(CombatAdvisor.RemainingHp(9, 7, 3, false, 0, new(1, 0)) == 4, "final HP Berserk");
        Require(CombatAdvisor.RemainingHp(9, 7, 3, false, 0, new(0, 1)) == 8, "final HP cap");
        Require(CombatAdvisor.RemainingHp(4, 7, 3, false, 0, new(0, 0)) == 0, "exact lethal");
        Require(CombatAdvisor.RemainingHp(2, 7, 3, false, 0, new(0, 0)) == 0, "overkill clamp");
        Require(CombatAdvisor.RemainingHp(9, 2, 5, false, 0, new(0, 0)) == 8, "defense minimum");
        Require(CombatAdvisor.RemainingHp(9, 12, 6, true, 6, new(1, 0)) == 9, "six dodge tie");
        Require(CombatAdvisor.RemainingHp(9, 7, 3, true, 4, new(1, 0)) == 1, "failed dodge ignores defense");
        Require(CombatAdvisor.RemainingHp(9, 7, 5, true, 4, new(1, 0)) == 9, "successful dodge ignores modifiers");
        Console.WriteLine("Remaining HP: 10 final-number checks passed");
        // Recorded 0.4.79 runtime: totals 8/5, HP10 at step5; HP7 and hidden points at step6.
        Require(CombatAdvisor.IsFinalHpPhase(5) && !CombatAdvisor.IsFinalHpPhase(6), "actual final-display phase");
        Require(CombatAdvisor.RemainingHp(10, 8, 5, false, 4, new(0, 0)) == 7, "recorded battle one");
        Require(CombatAdvisor.RemainingHp(9, 9, 1, false, 6, new(0, 0)) == 1, "recorded battle two");
        Require(CombatAdvisor.RemainingHp(10, 3, 8, false, 3, new(0, 0)) == 9, "recorded battle three");
        Console.WriteLine("Remaining HP: recorded phase and 3 observed HP outcomes passed");
        // Preserved 2026-09-26 0.13.1 log: HP outcomes agree; exact overkill is unknown.
        Require(CombatAdvisor.FinalDamage(10, 13, 2, false, 3, new(1)) == 12
            && CombatAdvisor.RemainingHp(10, 13, 2, false, 3, new(1)) == 0, "21:05:01 recorded KO");
        Require(CombatAdvisor.FinalDamage(9, 16, 3, false, 6, default) == 13
            && CombatAdvisor.RemainingHp(9, 16, 3, false, 6, default) == 0, "21:05:20 recorded KO");
        for (var face = 1; face <= 6; face++)
        {
            Require(CardKoMinimum.RevealedDie(1, true, face), "visible roll accepted");
            Require(!CardKoMinimum.RevealedDie(2, true, face), "small final total cannot become roll");
            Require(!CardKoMinimum.RevealedDie(1, false, face), "hidden roll rejected");
        }
        Require(CombatAdvisor.FinalDamage(2, 7, 3, false, 0, new(0, 0)) == 4, "damage preserves overkill");
        Require(CombatAdvisor.FinalDamage(9, 7, 3, false, 0, new(-99, 0)) == 0, "damage shield");
        Require(CombatAdvisor.FinalDamage(9, 7, 3, false, 0, new(1, 1)) == 1, "damage capped Berserk");
        Require(CombatAdvisor.FinalDamage(9, 7, 5, true, 4, new(1, 0)) == 0, "damage successful dodge");
        Require(AdviceText.DamageAlpha(0) == 0 && AdviceText.DamageAlpha(0.06f) is > 0 and < 1
            && AdviceText.DamageAlpha(0.12f) == 1, "fast damage entrance fade");
        Require(AdviceText.DamageAlpha(0, true) == 1 && AdviceText.DamageAlpha(AdviceText.DamageLifetime, true) == 0,
            "lethal starts fully visible and retains exit fade");
        Require(AdviceText.DamageLifetime == 3f && AdviceText.DamageAlpha(0.5f) == 1 && AdviceText.DamageAlpha(2.5f) == 1, "three second total lifetime");
        Require(AdviceText.DamageAlpha(2.75f) is > 0 and < 1 && AdviceText.DamageAlpha(3f) == 0, "damage exit fade");
        Require(AdviceText.RecommendationScale(0) < 1 && AdviceText.RecommendationScale(0.5f) == 1, "native style entrance scale");
        Console.WriteLine("Damage indicator: 9 calculation and animation checks passed");
        Require(!AdviceText.FinalDamageReady(false, false, true, true, false), "raw dice pulses cannot preview damage");
        Require(!AdviceText.FinalDamageReady(true, false, true, false, false), "both final updates required");
        Require(AdviceText.FinalDamageReady(true, false, true, true, false), "preview before arithmetic exit and hit");
        Require(AdviceText.FinalDamageReady(true, true, false, false, false), "late sample fallback");
        Require(AdviceText.FinalDamageReady(false, false, false, false, true), "successful dodge skips arithmetic");
        Console.WriteLine("Damage preview: 5 early-final-number readiness checks passed");
    }

    private static void CheckThresholdRanges()
    {
        void Check(bool ok, string reason) { if (!ok) throw new Exception("KO threshold bounds: " + reason); }
        var cases = 0;
        foreach (var hp in new[] { 1, 2, 6, 10 })
        foreach (var attack in new[] { 0, 4, 10 })
        foreach (var defense in new[] { 0, 4 })
        foreach (var die in Enumerable.Range(1, 6))
        foreach (var survival in new[] { false, true })
        foreach (var effect in new HitModifiers[] { default, new(-2), new(1), new(Cap: 1),
            new(Immune: true), new() { MinimumHp = 1 }, new() { OnHitBonus = 1 } })
        foreach (var card in new CardBonus?[] { null, new(true, 1, 3, 1), new(false, 1, 3, 1) })
        {
            var input = new PreRollCombat(9, hp, attack, attack + 2, defense, defense + 2) { Modifiers = effect };
            var thresholds = new List<int>();
            // Enumerate actual card/stat values, independently of the bounds solver.
            for (var a = attack; a <= attack + 2; a++)
            for (var d = defense; d <= defense + 2; d++)
            for (var bonus = card?.Min ?? 0; bonus <= (card?.Max ?? 0); bonus++)
            {
                var threshold = survival ? 7 : 0;
                for (var face = 1; face <= 6; face++)
                {
                    var damage = CombatAdvisor.FinalDamage(hp, a + die + (card is { Attack: true } ? bonus : 0),
                        d + face + (card is { Attack: false } ? bonus : 0), false, die, effect);
                    if (survival && damage < hp) threshold = Math.Min(threshold, face);
                    if (!survival && damage >= hp) threshold = face;
                }
                thresholds.Add(threshold);
            }
            var best = survival ? thresholds.Min() : thresholds.Max();
            var pair = survival ? CardKoMinimum.Survival(input, card, die, possible: true)
                : CardKoMinimum.Calculate(input, card, die, possible: true);
            Check(pair == new KoPair(die, best == 7 ? 0 : best),
                "best achievable single threshold: public stats/cards, HP equality, immunity, cap and floor");
            var display = CardDiceLayout.Content(pair, die, 1, survival);
            Check(!display.Empty.Contains('~') && display.Die is >= 0 and <= 6, "single numeric glyph, no endpoint clamping");
            cases++;
        }
        // Best-case presentation deliberately selects the achievable endpoint, not a guarantee.
        var logged = new PreRollCombat(9, 10, 4, 9, 1, 1);
        foreach (var input in new[] { logged, logged with { Hp = 1 }, logged with { Hp = 5, DefenseMax = 5 } })
        foreach (var survival in new[] { false, true })
        {
            var candidates = Enumerable.Range(1, 6).Select(die => survival
                ? CardKoMinimum.Survival(input, null, die, true)!.Value
                : CardKoMinimum.Calculate(input, null, die, true)!.Value).Where(p => p.DefenseDie > 0);
            var expected = survival ? candidates.OrderBy(p => p.DefenseDie).ThenByDescending(p => p.AttackDie).FirstOrDefault()
                : candidates.OrderBy(p => p.AttackDie).FirstOrDefault();
            var actual = survival ? CardKoMinimum.Survival(input, null, possible: true)
                : CardKoMinimum.Calculate(input, null, possible: true);
            Check(actual == expected, "pre-roll selects ideal pair; revealed roll remains fixed");
        }
        Check(CardKoMinimum.Calculate(logged, null, 6, true) == new KoPair(6, 4), "logged unresolved range picks best case");
        foreach (var faces in new[] { default(DiceFaces), DiceFaces.Range(3, 6), DiceFaces.Range(2, 4), DiceFaces.Range(4, 4) })
        foreach (var survival in new[] { false, true })
        {
            var boundary = survival ? faces.Min : faces.Max;
            var pair = new KoPair(4, boundary);
            Check(CardDiceLayout.Content(pair, 4, 1, survival, false, faces) == (0, "", "—"), "vacuous defender bound follows achievable faces");
            Check(CardDiceLayout.Content(pair, 4, 0, survival, false, faces) == (4, "=", ""), "vacuous defense does not erase revealed attack");
            if (faces.Count > 1)
                Check(CardDiceLayout.Content(new(4, survival ? faces.Max : faces.Min), 4, 1, survival, false, faces).Die > 0,
                    "real endpoint-only defense conditions stay numeric");
        }
        var mixed = new PreRollCombat(9, 5, 0, 10, 0, 0);
        var mixedPair = CardKoMinimum.Calculate(mixed, null, possible: true);
        Check(mixedPair == new KoPair(1, 6) && CardDiceLayout.Status(mixedPair, CardKoMinimum.OutcomeRange(mixed, null), mixed.Hp) == "",
            "unconditional defense must not promote best-case KO to guaranteed KO");
        foreach (var survival in new[] { false, true })
        {
            Check(CardDiceLayout.Status(null, (10, 12), 10, survival) == "확정 KO"
                && CardDiceLayout.Status(null, (0, 9), 10, survival) == "KO 불가",
                "final arithmetic known without pre-roll support in either role");
            Check(CardDiceLayout.Status(null, null, 10, survival) == "계산 정보 부족"
                && CardDiceLayout.Status(null, (0, 10), 10, survival) == "계산 정보 부족"
                && CardDiceLayout.Content(null, 0, 1, survival) == (0, "", "?"),
                "unsupported unknown/mixed outcomes stay unknown");
            foreach (var fixedDie in new[] { 0, 1, 6 })
            {
                Check(CardDiceLayout.Content(null, fixedDie, 1, survival, true) == (0, "", "—"),
                    "known final condition-free defense does not need a pre-roll pair");
                Check(CardDiceLayout.Content(null, fixedDie, 0, survival, true)
                    == (fixedDie > 0 ? (fixedDie, "=", "") : (0, "", "—")),
                    "known final preserves revealed attack equality before hiding conditions");
                Check(CardDiceLayout.Content(null, fixedDie, 1, survival) == (0, "", "?"),
                    "visible attack alone cannot resolve unknown defender condition");
            }
        }
        Check(CardKoMinimum.Survival(new(9, 2, 6, 6, 0, 0), null, 1, true) == new KoPair(1, 6), "six really survives");
        Check(CardKoMinimum.Survival(new(9, 1, 6, 6, 0, 0), null, 1, true) == new KoPair(1, 0), "HP1 dies even at six");
        Check(CardKoMinimum.Calculate(logged with { MapType = 0 }, null, 6, true) == null
            && CardKoMinimum.Calculate(logged, null, 7, true) == null, "invalid public inputs");
        // Two newest stored 0.30.14 results. KO proves HP loss, not exact overkill.
        foreach (var sample in new[] { (Hp: 10, Atk: 15, Def: 2, Die: 6, Observed: 0), (Hp: 6, Atk: 6, Def: 6, Die: 1, Observed: 5) })
        {
            var damage = CombatAdvisor.FinalDamage(sample.Hp, sample.Atk, sample.Def, false, sample.Die, default);
            Check(Math.Max(0, sample.Hp - damage) == sample.Observed, "logged final HP");
            foreach (var survival in new[] { false, true })
                Check(CardDiceLayout.Status(new(sample.Die, 1), (damage, damage), sample.Hp, survival)
                    == (sample.Observed == 0 ? "확정 KO" : "KO 불가"), "resolved outcome replaces stale threshold for every role");
        }
        Console.WriteLine($"KO threshold bounds: {cases} cases, endpoint formatting and two 0.30.14 log replays passed");
    }
}
