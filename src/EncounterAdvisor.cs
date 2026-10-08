namespace BetterAstralParty;

internal readonly record struct EncounterInput(int MapType, int Hp, int Attack, int Defense,
    int EnemyHp, int EnemyAttack, int EnemyDefense, bool Counter)
{
    public HitModifiers Modifiers { get; init; }
    public HitModifiers EnemyModifiers { get; init; }
}

internal readonly record struct EncounterAdvice(bool? Attack, double EnemyKoMin, double EnemyKoMax,
    double SelfKoMin, double SelfKoMax)
{
    internal string Quick => $"상대 KO {CombatAdvisor.ProbabilityRange(EnemyKoMin, EnemyKoMax)}%\n내 KO {CombatAdvisor.ProbabilityRange(SelfKoMin, SelfKoMax)}%";
}

internal static class EncounterAdvisor
{
    internal static EncounterAdvice? Calculate(EncounterInput input)
    {
        if (!input.Modifiers.Valid || !input.EnemyModifiers.Valid || !input.Modifiers.PreRollDiceKnown || !input.EnemyModifiers.PreRollDiceKnown
            || !CardAdvisor.Valid(new(input.MapType, input.EnemyHp, input.Attack, input.Attack,
                input.EnemyDefense, input.EnemyDefense))
            || !CardAdvisor.Valid(new(input.MapType, input.Hp, input.EnemyAttack, input.EnemyAttack,
                input.Defense, input.Defense))) return null;

        double enemyMin = 1, enemyMax = 0, selfMin = 1, selfMax = 0;
        bool? consensus = null;
        var first = true;
        var disagrees = false;
        var outgoingOptions = new[] {
            CardAdvisor.BeforeRoll(input.EnemyHp, input.Attack, input.EnemyDefense, false, input.EnemyModifiers),
            CardAdvisor.BeforeRoll(input.EnemyHp, input.Attack, input.EnemyDefense, true, input.EnemyModifiers) };
        var incomingOptions = input.Counter ? new[] {
            CardAdvisor.BeforeRoll(input.Hp, input.EnemyAttack, input.Defense, false, input.Modifiers),
            CardAdvisor.BeforeRoll(input.Hp, input.EnemyAttack, input.Defense, true, input.Modifiers) } : new ChoiceStats[2];
        // ponytail: one basic exchange, not a reward/mission planner; add public special-rule
        // handlers when supported. No guessed hand cards, dice seeds or future random results.
        for (var enemyDodge = 0; enemyDodge < 2; enemyDodge++)
        for (var selfDodge = 0; selfDodge < 2; selfDodge++)
        {
            var outgoing = outgoingOptions[enemyDodge];
            var incoming = incomingOptions[selfDodge];
            // A knocked-out defender cannot make the return attack.
            var survives = 1 - outgoing.KnockoutChance;
            var selfKo = incoming.KnockoutChance * survives;
            var selfDamage = incoming.ExpectedDamage * survives;
            enemyMin = Math.Min(enemyMin, outgoing.KnockoutChance);
            enemyMax = Math.Max(enemyMax, outgoing.KnockoutChance);
            selfMin = Math.Min(selfMin, selfKo);
            selfMax = Math.Max(selfMax, selfKo);
            const double epsilon = 0.000001;
            var koGain = outgoing.KnockoutChance - selfKo;
            var damageGain = outgoing.ExpectedDamage - selfDamage;
            bool? attack = Math.Abs(koGain) > epsilon ? koGain > 0
                : Math.Abs(damageGain) > epsilon ? damageGain > 0 : null;
            if (first) { consensus = attack; first = false; }
            else if (consensus != attack) disagrees = true;
        }
        return new(disagrees ? null : consensus, enemyMin, enemyMax, selfMin, selfMax);
    }
}
