public static class PresentationCheck
{
    private static int _checks;
    private static void Check(bool value, string reason)
    {
        _checks++;
        if (!value) throw new Exception("Probability UI: " + reason);
    }
    private static void Equal(double expected, double actual, string reason) =>
        Check(Math.Abs(expected - actual) < 1e-9, reason);
    private static string KoLine(string quick) => quick.Split('\n').Last();

    public static void Run()
    {
        foreach (var (chance, expected) in new[] { (0d, "0"), (1d, "100"), (20d / 36, "55.56"),
            (1d / 36, "2.78"), (5d / 6, "83.33"), (0.00001, "<0.01"), (0.99999, ">99.99") })
            Check(CombatAdvisor.Probability(chance) == expected, "one value with safe rounding");

        // The reported HP1 range is real: normal defense always loses its minimum 1 HP,
        // but 16/36 pre-roll dodge outcomes survive (including 6 against 6).
        Equal(1, CardAdvisor.BeforeRoll(1, 0, 0, false).KnockoutChance, "HP1 ordinary defense");
        Equal(20d / 36, CardAdvisor.BeforeRoll(1, 0, 0, true).KnockoutChance, "HP1 dodge remains possible");
        Check(CardAdvisor.Calculate(new(9, 1, 0, 0, 0, 0), new(true, 0, 0, 0))!.Value.Details == "100%",
            "attack card chooses favorable endpoint, not average or range");
        var noEffect = CardAdvisor.Calculate(new(9, 5, 2, 5, 0, 3), new(true, 0, 0, 0))!.Value;
        Check(noEffect.Details == "58.33%" && noEffect.KoGainMin == 0 && noEffect.KoGainMax == 0,
            "zero-gain card preserves internal paired gains while showing favorable total KO");
        var immune = new HitModifiers(Immune: true);
        var reduced = new HitModifiers(-99);
        var spared = new HitModifiers { MinimumHp = 1 };
        foreach (var effect in new[] { immune, reduced, spared })
            Check(CardAdvisor.Calculate(new PreRollCombat(9, 1, 0, 0, 0, 0) { Modifiers = effect },
                new(true, 1, 1, 1))!.Value.Details == "0%", "HP1 is not forced to 100 through public protection");
        Check(VisibleCombatAdvisor.Calculate(new(9, 1, 0, 0, 3, 0, 0, true))!.Value.DodgeQuick
            == "예상 피해 0.5\n전투불능 50%", "revealed-roll HP1 dodge risk remains calculated");

        // Favorable display must not change the existing worst-case defense policy.
        var uncertain = VisibleCombatAdvisor.Calculate(new(4, 2, 1, 10, 3, 0, 10, true))!.Value;
        Check(uncertain.Recommendation == RecommendedAction.Dodge, "conservative recommendation remains dodge");
        Check(KoLine(uncertain.DefendQuick) == "전투불능 0%", "own defense shows minimum risk");
        Check(uncertain.DefendQuick.Split('\n')[0].Contains('~'), "damage range retained");
        Check(KoLine(uncertain.DodgeQuick) == "전투불능 50%", "own dodge shows minimum risk");

        foreach (var hp in new[] { 1, 2, 5 })
        foreach (var attack in new[] { 0, 2, 5 })
        foreach (var defense in new[] { 0, 3 })
        foreach (var die in new[] { 1, 3, 6 })
        foreach (var available in new[] { false, true })
        {
            var input = new VisibleCombat(9, hp, attack, attack + 2, die, defense, defense + 2, available);
            var advice = VisibleCombatAdvisor.Calculate(input)!.Value;
            var outcomes = (from a in Enumerable.Range(attack, 3)
                from d in Enumerable.Range(defense, 3)
                select CombatAdvisor.Calculate(new(hp, a + die, die, d))).ToArray();
            Check(KoLine(advice.DefendQuick) == "전투불능 " + CombatAdvisor.Probability(outcomes.Min(x => x.Defend.KnockoutChance)) + "%",
                "all visible defense endpoints");
            Check(available ? KoLine(advice.DodgeQuick)
                == "전투불능 " + CombatAdvisor.Probability(outcomes.Min(x => x.Dodge.KnockoutChance)) + "%" : advice.DodgeQuick == "회피 불가",
                "all visible dodge endpoints and availability");
            var allDefend = outcomes.All(x => CombatAdvisor.CompareChoices(x.Defend, x.Dodge) <= 0);
            var allDodge = outcomes.All(x => CombatAdvisor.CompareChoices(x.Defend, x.Dodge) >= 0);
            var recommendation = !available || allDefend ? RecommendedAction.Defend : allDodge ? RecommendedAction.Dodge
                : CombatAdvisor.CompareChoices(new(outcomes.Max(x => x.Defend.ExpectedDamage), outcomes.Max(x => x.Defend.KnockoutChance), 0),
                    new(outcomes.Max(x => x.Dodge.ExpectedDamage), outcomes.Max(x => x.Dodge.KnockoutChance), 0)) <= 0
                    ? RecommendedAction.Defend : RecommendedAction.Dodge;
            Check(advice.Recommendation == recommendation, "worst-case ordering retained for every ranged input");
        }

        foreach (var hp in new[] { 1, 5, 10 })
        foreach (var attack in new[] { 0, 3 })
        foreach (var defense in new[] { 0, 3 })
        foreach (var effect in new[] { default(HitModifiers), immune, reduced })
        {
            var input = new PreRollCombat(9, hp, attack, attack + 1, defense, defense + 1) { Modifiers = effect };
            var card = new CardBonus(true, 1, 3, 1);
            var result = CardAdvisor.Calculate(input, card)!.Value;
            var maximum = (from a in Enumerable.Range(attack, 2)
                from d in Enumerable.Range(defense, 2)
                from bonus in Enumerable.Range(1, 3)
                from dodge in new[] { false, true }
                select CardAdvisor.BeforeRoll(hp, a + bonus, d, dodge, effect).KnockoutChance).Max();
            Check(result.Details == CombatAdvisor.Probability(maximum) + "%", "card hover maximum across existing public bounds");
            Check(!result.Details.Contains('~') && !result.Details.Contains('\n'), "one card probability only");
            Check(CardAdvisor.Calculate(input, card with { Attack = false })!.Value.Details == "", "defense card long text remains absent");
            var encounter = EncounterAdvisor.Calculate(new EncounterInput(9, hp, attack, defense, hp, attack, defense, true)
                { Modifiers = effect, EnemyModifiers = effect })!.Value;
            Check(encounter.Quick == "상대 KO " + CombatAdvisor.Probability(encounter.EnemyKoMax)
                + "%\n내 KO " + CombatAdvisor.Probability(encounter.SelfKoMin) + "%", "enemy maximum / own minimum encounter display");
        }

        var unknown = new HitModifiers { AttackDistributionUnknown = true };
        Check(CardAdvisor.Calculate(new PreRollCombat(9, 1, 0, 0, 0, 0) { Modifiers = unknown }, new(true, 1, 1, 1)) == null,
            "unknown pre-roll distribution unsupported");
        Check(EncounterAdvisor.Calculate(new EncounterInput(9, 1, 0, 0, 1, 0, 0, false) { Modifiers = unknown }) == null,
            "unknown encounter distribution unsupported");
        Check(VisibleCombatAdvisor.Calculate(new VisibleCombat(9, 1, 0, 0, 1, 0, 0, true)
            { Modifiers = new HitModifiers { DiceUnsupported = true } }) == null, "unsupported visible dice rejected");
        Check(CardAdvisor.Calculate(new(0, 1, 0, 0, 0, 0), new(true, 1, 1, 1)) == null, "PvP/unknown map stays excluded");

        foreach (var language in new[] { "한국어", "English" })
        {
            ModText.Select(language);
            Check(ModText.Text("전투불능 55.56%") == (language == "한국어" ? "전투불능 55.56%" : "KO 55.56%"), "existing label localization");
            Check(ModText.Text("상대 KO 100%\n내 KO 0%") == (language == "한국어" ? "상대 KO 100%\n내 KO 0%" : "Enemy KO 100%\nSelf KO 0%"), "encounter localization");
            var styled = AdviceText.Quick(uncertain.DefendQuick);
            Check(styled.Contains("1~2") && !styled.Contains("최대") && !styled.Contains("최소") && !styled.Contains("내지는"),
                "shared hover/spectator styling retains damage only ranges, no extra wording");
            var tiny = AdviceText.Quick("전투불능 " + CombatAdvisor.Probability(0.00001) + "%");
            Check(System.Xml.Linq.XElement.Parse("<root>" + tiny + "</root>").Value.Contains("<0.01%"),
                "tiny odds escape safely in rich text");
        }
        Console.WriteLine($"PASS: production probability presentation {_checks} assertions; numeric bounds and conservative advice preserved.");
    }
}
