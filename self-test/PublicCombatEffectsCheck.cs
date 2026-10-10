using BetterAstralParty;

// Execute the production public-effect reader against the native tuple shape.
namespace BetterAstralParty
{
    internal sealed class RuntimeObject
    {
        internal readonly Dictionary<string, RuntimeObject> Members = new();
        internal object? Scalar;
        internal static RuntimeObject Node(params (string, RuntimeObject)[] entries)
        {
            var node = new RuntimeObject();
            foreach (var (key, value) in entries) node.Members.Add(key, value);
            return node;
        }
        internal static RuntimeObject Number(object value) => new() { Scalar = value };
        internal static RuntimeObject List(params RuntimeObject[] values) => new() { Scalar = values };
        internal static readonly Dictionary<string, RuntimeObject> Tables = new();
        internal RuntimeObject? Get(string key) => Members.GetValueOrDefault(key);
        internal RuntimeObject? Field(string key) => Get(key);
        internal T Get<T>(string key) => key == "Count" ? (T)(object)((RuntimeObject[])Scalar!).Length : Get(key)!.Value<T>();
        internal T Value<T>() => (T)Scalar!;
        internal RuntimeObject? Call(string key, params object[] args) => Scalar is Dictionary<int, RuntimeObject> table
            ? key == "ContainsKey" ? Number(table.ContainsKey((int)args[0])) : table[(int)args[0]]
            : key == "get_Item" ? ((RuntimeObject[])Scalar!)[(int)args[0]] : Get(key);
        internal static IntPtr FindClass(string ns, string name) => IntPtr.Zero;
        internal static RuntimeObject? StaticCall(IntPtr type, string name) => Tables.GetValueOrDefault(name);
    }
}

internal static class PublicCombatEffectsCheck
{
    internal static void Run()
    {
        RuntimeObject Property(int id, int count) => RuntimeObject.Node(
            ("buffId", RuntimeObject.Number(id)),
            ("property", RuntimeObject.Node(("Value", RuntimeObject.Number(count)))));
        HitModifiers? Read(RuntimeObject[] properties) => PublicCombatEffects.Read(
            RuntimeObject.Node(("buffContainer", RuntimeObject.Node(("GetShowBuffs", RuntimeObject.Node(
                ("Item1", RuntimeObject.List()), ("Item2", RuntimeObject.List(properties))))))), "test", null);
        void Check(bool condition) { if (!condition) throw new Exception("Public mark regression"); }
        Check(Read(Array.Empty<RuntimeObject>()) == new HitModifiers());
        var mark = Read(new[] { Property(10006, 1) });
        Check(mark == new HitModifiers(1));
        Check(Read(new[] { Property(10006, 3), Property(10006, 3), Property(10004, 7) }) == new HitModifiers(1));
        Check(Read(new[] { Property(10006, 0) }) == new HitModifiers());
        // Sanitized nonlethal log observations: current QA/local and previous local.
        foreach (var row in new[] { (12,16,7,2,2), (121,28,9,3,101), (94,39,8,2,62),
            (62,41,10,4,30), (19,13,11,3,16), (23,18,7,2,11) })
            Check(CombatAdvisor.RemainingHp(row.Item1, row.Item2, row.Item3, false, 0,
                Read(new[] { Property(10006, row.Item4) })!.Value) == row.Item5);
        Check(Read(new[] { Property(10006, -1) }) == null);
        Check(Read(new[] { Property(10006, 101) }) == null);
        Check(CombatAdvisor.FinalDamage(10, 10, 6, false, 0, mark!.Value) == 5);
        Check(CombatAdvisor.FinalDamage(10, 10, 6, true, 3, mark.Value) == 0);
        Check(CombatAdvisor.Calculate(new CombatInput(2, 6, 4, 4) { Modifiers = mark.Value })
            .Recommendation == RecommendedAction.Dodge);
        Check(VisibleCombatAdvisor.Calculate(new VisibleCombat(9, 2, 2, 2, 4, 4, 4, true)
            { Modifiers = mark.Value })?.Recommendation == RecommendedAction.Dodge);
        Check(CardAdvisor.BeforeRoll(2, 2, 4, false, mark.Value).KnockoutChance == 1);
        Check(EncounterAdvisor.Calculate(new EncounterInput(9, 10, 2, 4, 2, 2, 4, false)
            { EnemyModifiers = mark.Value })!.Value.EnemyKoMax == 1);
        CheckBuffs();
        CheckDice();
        Console.WriteLine("Public mark reader: tuple Item2, stacks, deduplication, bounds and all advisor paths passed (native substitute).");
    }

    private static void CheckDice()
    {
        RuntimeObject Numbers(params int[] values) => RuntimeObject.List(values.Select(value => RuntimeObject.Number(value)).ToArray());
        RuntimeObject Buff(int id, int stacks = 1, int delay = 0) => RuntimeObject.Node(
            ("BuffId", RuntimeObject.Number(id)), ("Progress", RuntimeObject.Number(stacks)),
            ("DelayRound", RuntimeObject.Number(delay)), ("KeepRound", RuntimeObject.Number(1)));
        RuntimeObject Actor(int[] passives, params RuntimeObject[] buffs) => RuntimeObject.Node(
            ("player", RuntimeObject.Node(("GetBattlePassiveSkillId", Numbers(passives)))),
            ("buffContainer", RuntimeObject.Node(("GetShowBuffs", RuntimeObject.Node(
                ("Item1", RuntimeObject.List(buffs)), ("Item2", RuntimeObject.List()))))));
        void Check(bool value, string message) { if (!value) throw new Exception("Public dice: " + message); }
        var configs = new Dictionary<int, RuntimeObject>();
        void Catalog(int id, int buff, params int[] values) => configs[id] = RuntimeObject.Node(
            ("Params", Numbers(values)), ("BuffId", Numbers(buff)));
        RuntimeObject.Tables["get_Skill"] = RuntimeObject.Node(("InfoDict", new RuntimeObject { Scalar = configs }));
        try
        {
            Catalog(104011, 10401101, 4,4,-2,21014,400,21015,300,21016,300);
            Catalog(106311, 10631101, 6,1,-1,1);
            Catalog(12501, 1250101, 3);
            var ordinary = Actor(Array.Empty<int>());
            var fixedDodge = PublicCombatEffects.Read(Actor(new[] {104011}), "dice", ordinary)!.Value;
            Check(fixedDodge.DodgeDice == DiceFaces.Range(4,4), "selected public passive fixes dodge, not defense");
            var advice = CombatAdvisor.Calculate(new(10,6,3,0) { Modifiers = fixedDodge });
            Check(advice.Recommendation == RecommendedAction.Dodge && advice.Dodge.ExpectedDamage == 0, "fixed-4 reproduction corrected");
            Check(VisibleCombatAdvisor.Calculate(new(9,10,3,3,3,0,0,true) { Modifiers = fixedDodge })?.Recommendation == RecommendedAction.Dodge, "choice UI shares fixed dodge");
            var source = Actor(new[] {106311});
            var fixedAttack = PublicCombatEffects.Read(ordinary, "dice", source)!.Value;
            Check(fixedAttack.AttackDice == DiceFaces.Range(6,6), "no Mind fixes attack to live face");
            var ranged = PublicCombatEffects.Read(Actor(Array.Empty<int>(), Buff(10012)), "dice", source)!.Value;
            Check(ranged.AttackDice == DiceFaces.Range(3,6), "active public Mind allows 3..6");
            Check(PublicCombatEffects.Read(Actor(Array.Empty<int>(), Buff(10012,0), Buff(10012,1,1)), "dice", source)!.Value.AttackDice == fixedAttack.AttackDice, "zero/delayed Mind does not activate");
            var capped = PublicCombatEffects.Read(Actor(Array.Empty<int>(), Buff(1250101)), "dice", ordinary)!.Value;
            Check(capped.DefenseDice == DiceFaces.Range(1,3) && capped.DodgeDice == default, "vulnerability caps defense, not dodge");
            Check(capped.DefenseDistributionUnknown && ranged.AttackDistributionUnknown, "support does not invent uniform weights");
            Check(CardAdvisor.Calculate(new(9,10,5,5,2,2) { Modifiers = ranged }, new(true,1,1,1)) == null, "unknown attack weights do not fabricate card percentages");
            var bounded = VisibleCombatAdvisor.Calculate(new(9,10,3,3,3,0,0,true) { Modifiers = capped })!.Value;
            Check(bounded.DefendQuick.Contains("3~5") && bounded.Recommendation == RecommendedAction.Dodge, "unknown defense weights bound damage and preserve single robust recommendation");
            Check(CardKoMinimum.Calculate(new(9,10,5,5,2,2) { Modifiers = ranged }, null, possible:true) != null, "known support keeps KO thresholds despite unknown weights");
            Check(PublicCombatEffects.Read(ordinary, "dice", Actor(Array.Empty<int>(), Buff(1250101)))!.Value.AttackDice == DiceFaces.Range(1,3), "source attack cap");
            var unknown = PublicCombatEffects.Read(ordinary, "dice", Actor(new[] {106311}, Buff(1250101)))!.Value;
            Check(unknown.DiceUnsupported && unknown.Valid && !unknown.DiceValid, "unverified cap/passive precedence not silently combined");
            Check(PublicCombatEffects.Read(Actor(Array.Empty<int>(), Buff(1250201)), "dice", ordinary)!.Value.DiceUnsupported, "zero-roll conditional rule not treated as ordinary");
            Check(CombatAdvisor.FinalDamage(10,7,4,false,0,unknown) == 3, "observed final totals remain calculable without dice distribution");
            Check(CardAdvisor.Calculate(new(9,10,5,5,2,2) { Modifiers = unknown }, new(true,1,1,1)) == null, "unsupported card calculation withheld");
            Check(EncounterAdvisor.Calculate(new(9,10,5,2,10,5,2,false) { EnemyModifiers = unknown }) == null, "unsupported encounter cannot throw");
            foreach (var faces in new[] {default(DiceFaces), DiceFaces.Range(3,6), DiceFaces.Range(6,6), DiceFaces.Range(1,3)})
            foreach (var defense in new[] {default(DiceFaces), DiceFaces.Range(1,3)})
            foreach (var hp in new[] {1,3,10})
            {
                var input = new PreRollCombat(9,hp,4,4,2,2) { Modifiers = new() { AttackDice=faces, DefenseDice=defense } };
                var pair = CardKoMinimum.Calculate(input,null,possible:true)!.Value;
                var survival = CardKoMinimum.Survival(input,null,possible:true)!.Value;
                Check(pair.DefenseDie == 0 || faces.Contains(pair.AttackDie) && defense.Contains(pair.DefenseDie), "KO pair achievable");
                Check(survival.DefenseDie == 0 || faces.Contains(survival.AttackDie) && defense.Contains(survival.DefenseDie), "survival pair achievable");
                var values = new List<long>();
                for (var a=1;a<=6;a++) for(var d=1;d<=6;d++)
                    if(faces.Contains(a) && defense.Contains(d)) values.Add(CardKoMinimum.DamageRange(input,null,a,d)!.Value.Min);
                Check(CardKoMinimum.OutcomeRange(input,null) == (values.Min(),values.Max()), "all-outcome bounds use allowed faces");
            }
            Catalog(104011,10401101,4,5,-2,21014,400,21015,300,21016,300);
            Check(PublicCombatEffects.Read(Actor(new[] {104011}), "dice", ordinary)!.Value.DodgeDice == DiceFaces.Range(5,5), "live dodge parameter, no cached face");
            Catalog(104011,999,4,4,-2,21014,400,21015,300,21016,300);
            Check(PublicCombatEffects.Read(Actor(new[] {104011}), "dice", ordinary)!.Value.DiceUnsupported, "changed schema association unsupported");
            Console.WriteLine("Public dice: fixed dodge/attack, Mind, caps, allowed KO/survival faces, unknown precedence and final-result independence passed (native substitute).");
        }
        finally { RuntimeObject.Tables.Clear(); }
    }

    private static void CheckBuffs()
    {
        RuntimeObject Numbers(params int[] values) => RuntimeObject.List(values.Select(value => RuntimeObject.Number(value)).ToArray());
        RuntimeObject Buff(int id, int stacks = 0, int delay = 0) => RuntimeObject.Node(
            ("BuffId", RuntimeObject.Number(id)), ("Progress", RuntimeObject.Number(stacks)),
            ("KeepRound", RuntimeObject.Number(1)), ("DelayRound", RuntimeObject.Number(delay)));
        HitModifiers? Read(params RuntimeObject[] buffs) => PublicCombatEffects.Read(RuntimeObject.Node(
            ("buffContainer", RuntimeObject.Node(("GetShowBuffs", RuntimeObject.Node(
                ("Item1", RuntimeObject.List(buffs)), ("Item2", RuntimeObject.List())))))), "test.buff", null);
        HitModifiers? ReadFrom(int? sourceId, params RuntimeObject[] buffs) => PublicCombatEffects.Read(RuntimeObject.Node(
            ("buffContainer", RuntimeObject.Node(("GetShowBuffs", RuntimeObject.Node(
                ("Item1", RuntimeObject.List(buffs)), ("Item2", RuntimeObject.List())))))), "test.source",
            sourceId == null ? null : RuntimeObject.Node(("player", RuntimeObject.Node(
                ("characterConfig", RuntimeObject.Node(("Id", RuntimeObject.Number(sourceId.Value))))))));
        void Check(bool condition, string message) { if (!condition) throw new Exception("Public effects: " + message); }
        void Catalog(string kind, int id, int buff, params int[] numbers)
        {
            if (!RuntimeObject.Tables.TryGetValue("get_" + kind, out var catalog))
                RuntimeObject.Tables["get_" + kind] = catalog = RuntimeObject.Node(
                    ("InfoDict", new RuntimeObject { Scalar = new Dictionary<int, RuntimeObject>() }));
            var map = (Dictionary<int, RuntimeObject>)catalog.Get("InfoDict")!.Scalar!;
            map[id] = RuntimeObject.Node(("Params", Numbers(numbers)),
                (kind is "Skill" or "Relic" ? "BuffId" : "BuffIds",
                    kind == "Relic" ? RuntimeObject.Number(buff) : Numbers(buff)));
        }
        try
        {
            // Fixtures checked against current original INT configuration and STRBuff.
            Catalog("Skill", 10712, 1071101, -99);
            Catalog("Skill", 102611, 10261101, -2, 2, 20);
            Catalog("Skill", 105312, 10531201, 6, 1, -1);
            Catalog("Skill", 11011, 1101101, 2, 1);
            Catalog("Destiny", 40003, 4000301, -2);
            Catalog("Destiny", 40004, 4000401, 2);
            Catalog("Relic", 50060, 5006001, -2, 2);
            Catalog("Card", 20008, 2000801, 10, 1, 3, 1);
            Catalog("Skill", 106713, 10671302, 8, 4, 2, 2, 1, 2, 220, 3, 10, 5, -20, 0, -2, -1, 5);
            var skills = (Dictionary<int, RuntimeObject>)RuntimeObject.Tables["get_Skill"].Get("InfoDict")!.Scalar!;
            Catalog("Skill", 12912, 1291202, 3, 12904, 12912, 12922, 1, 0);
            skills[12912].Members["BuffId"] = Numbers(1291201, 1291202);
            var sykes = RuntimeObject.Node(("player", RuntimeObject.Node(
                ("GetBattlePassiveSkillId", Numbers(12912)),
                ("characterConfig", RuntimeObject.Node(("Id", RuntimeObject.Number(129)))))));
            foreach (var oldStacks in new[] { 0, 3 })
            {
                var property = RuntimeObject.Node(("buffId", RuntimeObject.Number(10006)),
                    ("property", RuntimeObject.Node(("Value", RuntimeObject.Number(2)))));
                var target = RuntimeObject.Node(("buffContainer", RuntimeObject.Node(("GetShowBuffs", RuntimeObject.Node(
                    ("Item1", RuntimeObject.List(oldStacks == 0 ? Array.Empty<RuntimeObject>() : new[] { Buff(1291202, oldStacks) })),
                    ("Item2", RuntimeObject.List(property)))))));
                var effect = PublicCombatEffects.Read(target, "replay", sykes)!.Value;
                Check(effect.Bonus == oldStacks + 1 && effect.OnHitBonus == 1, "separate existing and triggered erosion");
                var hp = oldStacks == 0 ? 17 : 105;
                Check(CombatAdvisor.RemainingHp(hp, 7, 5, false, 3, effect) == (oldStacks == 0 ? 13 : 98), "retained mismatches 120/113 replay");
                Check(CombatAdvisor.FinalDamage(hp, 7, 6, true, 3, effect) == 0, "dodge never triggers new erosion");
                Check(CombatAdvisor.HitDamage(2, effect with { Immune = true }, hp) == 0, "immune never triggers erosion");
                Check(CombatAdvisor.HitDamage(2, effect with { Bonus = -99 }, hp) == 0, "zero damage never triggers erosion");
                Check(CombatAdvisor.HitDamage(2, effect with { Cap = 1 }, hp) == 1, "trigger respects damage cap");
                var input = new PreRollCombat(4, hp, 4, 4, 4, 4) { Modifiers = effect };
                Check(CardKoMinimum.DamageRange(input, null, 3, 1) == (3L + oldStacks + 1, 3L + oldStacks + 1), "threshold shares hit modifier");
            }
            skills[106713].Members["BuffId"] = Numbers(10671301, 10671302, 10671303);
            var culprit = Read(Buff(10671302))!.Value;
            Check(culprit == new HitModifiers(2), "culprit is +2 even with zero Progress");
            foreach (var row in new[] { (10,11,8,5), (11,12,10,7), (12,16,22,9) })
                Check(CombatAdvisor.RemainingHp(row.Item1, row.Item2, row.Item3, false, 0, culprit) == row.Item4,
                    "culprit QA/local replay");
            Check(Read(Buff(10671302, 0, 1)) == new HitModifiers(), "delayed culprit excluded");
            skills[106713].Members["Params"] = Numbers(8,4,2,2,1,4,220,3,10,5,-20,0,-2,-1,5);
            Check(Read(Buff(10671302)) == new HitModifiers(4), "culprit reads live magnitude");
            Catalog("Skill", 100312, 10031201, 1);
            RuntimeObject Target(int type) => RuntimeObject.Node(("characterType", RuntimeObject.Number(type)),
                ("buffContainer", RuntimeObject.Node(("GetShowBuffs", RuntimeObject.Node(
                    ("Item1", RuntimeObject.List()), ("Item2", RuntimeObject.List()))))));
            var source = RuntimeObject.Node(("player", RuntimeObject.Node(("GetBattlePassiveSkillId", Numbers(100311,100312)))));
            var spared = PublicCombatEffects.Read(Target(1), "test.spare", source)!.Value;
            Check(spared.MinimumHp == 1, "native source passive preserves hero HP");
            Check(PublicCombatEffects.Read(Target(2), "test.spare", source)!.Value.MinimumHp == 0, "does not spare monsters");
            // Conditional replay: old logs lack source IDs, so these are compatible with
            // 100312, not proof it caused the five observed HP=1 results.
            foreach (var row in new[] { (4,9,0), (5,9,4), (5,9,3), (7,9,2), (9,13,2) })
                Check(CombatAdvisor.RemainingHp(row.Item1, row.Item2, row.Item3, false, 0, spared) == 1, "spared conditional replay");
            Check(CombatAdvisor.FinalDamage(1, 99, 0, false, 0, spared) == 0, "no healing at HP floor");
            Check(CombatAdvisor.Calculate(new(4,99,6,0) { Modifiers = spared }).Dodge.KnockoutChance == 0, "failed dodge respects floor");
            Check(CardAdvisor.BeforeRoll(4,99,0,false,spared).KnockoutChance == 0, "card path respects floor");
            Check(CardKoMinimum.Calculate(new(9,4,99,99,0,0) { Modifiers = spared }, null) == new KoPair(0,0), "KO threshold respects floor");
            Check(CardKoMinimum.Survival(new(9,4,99,99,0,0) { Modifiers = spared }, null) == new KoPair(6,1), "survival respects floor");
            Check(EncounterAdvisor.Calculate(new(9,4,9,0,10,99,99,true) { Modifiers = spared })!.Value.SelfKoMax == 0, "encounter respects floor");
            foreach (var hp in new[] { 1, 4, 12 })
            foreach (var bonus in new[] { -99, 0, 2 })
            for (var a = 1; a <= 6; a++)
            for (var d = 1; d <= 6; d++)
            {
                var effect = spared with { Bonus = bonus };
                var expected = Math.Min(hp - 1, Math.Max(0, Math.Max(1, 8 + a - 2 - d) + bonus));
                Check(CombatAdvisor.FinalDamage(hp, 8+a, 2+d, false, a, effect) == expected, "floor all defended dice");
                var escaped = d > a || a == 6 && d == 6;
                expected = escaped ? 0 : Math.Min(hp - 1, Math.Max(0, 8+a+bonus));
                Check(CombatAdvisor.FinalDamage(hp, 8+a, d, true, a, effect) == expected, "floor all dodge dice");
            }
            Console.WriteLine("QA/local replay: 9 confirmed outcomes, 5 conditional HP-floor outcomes, 648 floor/dodge checks passed.");
            foreach (var stacks in new[] { 0, 1, 2, 6 })
            {
                Check(ReadFrom(129, Buff(1291202, stacks)) == new HitModifiers(stacks), "Sykes erosion uses existing stacks");
                Check(ReadFrom(101, Buff(1291202, stacks)) == new HitModifiers(), "unrelated hero excluded");
                Check(ReadFrom(1001, Buff(1291202, stacks)) == new HitModifiers(), "unrelated monster excluded");
                Check(ReadFrom(129, Buff(1291202, stacks, 1)) == new HitModifiers(), "delayed erosion excluded");
            }
            Check(Read(Buff(1291202, 2)) == null, "unknown source must not silently ignore erosion");
            Check(Read(Buff(1291202, 0)) == new HitModifiers(), "zero stacks need no source");
            Check(ReadFrom(0, Buff(1291202, 2)) == null, "invalid source rejected");
            Check(ReadFrom(129, Buff(1291202, -1)) == null, "invalid erosion stacks rejected");
            Check(ReadFrom(129, Buff(1291202, 2), Buff(1291202, 2)) == new HitModifiers(2), "erosion deduplicated");
            Check(ReadFrom(129, Buff(1291202, 2), Buff(5006001, 1)) == new HitModifiers(), "erosion and reduction combine");
            Check(ReadFrom(129, Buff(1291202, 2), Buff(10591101)) == new HitModifiers(2, 1), "erosion respects cap");
            var erosion = ReadFrom(129, Buff(1291202, 2))!.Value;
            Check(CombatAdvisor.FinalDamage(9, 7, 3, false, 4, erosion) == 6, "two stacks add two damage");
            Check(CombatAdvisor.FinalDamage(9, 7, 6, true, 4, erosion) == 0, "successful dodge remains zero");
            var erosionInput = new PreRollCombat(9, 9, 4, 4, 0, 0) { Modifiers = erosion };
            Check(CardKoMinimum.Calculate(erosionInput, null) == new KoPair(4, 1), "erosion reaches KO thresholds");
            Check(CardKoMinimum.Survival(erosionInput, null, 6) == new KoPair(6, 4), "erosion reaches survival thresholds");
            Check(Read(Buff(1071101)) == new HitModifiers(-99) { ShushuShield = true }, "PvE Ren shield identity and reduction");
            Check(Read(Buff(1071101, 0, 1)) == new HitModifiers(), "delayed shield is inactive");
            Check(Read(Buff(1071101), Buff(4000401)) == new HitModifiers(-97) { ShushuShield = true }, "shield identity survives combined adjustments");
            foreach (var stacks in new[] { 0, 1, 3, 6 })
            {
                Check(Read(Buff(10261101, stacks)) == new HitModifiers(-2 * stacks), "existing scales only");
                Check(Read(Buff(10531201, stacks)) == new HitModifiers(-stacks), "ash stacks");
                Check(Read(Buff(5006001, stacks)) == new HitModifiers(-2 * stacks), "relic scalar association");
            }
            Check(Read(Buff(1101101)) == new HitModifiers(1), "incoming, not healing amount");
            Check(Read(Buff(4000301), Buff(4000401)) == new HitModifiers(), "fortune modifiers cancel");
            Check(Read(Buff(2000801), Buff(10531201, 3), Buff(10591101)) == new HitModifiers(-2, 1), "mixed snapshot");
            Check(Read(Buff(10331101), Buff(4000401)) == new HitModifiers(2, Immune: true), "explicit immunity");
            Check(Read(Buff(10531201, 3, 1), Buff(10331101, 0, 1)) == new HitModifiers(), "delayed inactive effects");
            Check(Read(Buff(10531201, 3), Buff(10531201, 3)) == new HitModifiers(-3), "deduplicate IDs");
            Check(Read(Buff(10531201, -1)) == null, "invalid stacks");
            Catalog("Skill", 105312, 10531201, 6, 1, -4);
            Check(Read(Buff(10531201, 3)) == new HitModifiers(-12), "read live numbers, no cached magnitude");
            Catalog("Skill", 105312, 10531201, 6, 1);
            Check(Read(Buff(10531201, 3)) == null, "changed schema rejected");
            Catalog("Skill", 105312, 999, 6, 1, -1);
            Check(Read(Buff(10531201, 3)) == null, "wrong buff association rejected");
            Catalog("Relic", 50060, 999, -2, 2);
            Check(Read(Buff(5006001, 3)) == null, "wrong relic association rejected");
            RuntimeObject.Tables.Clear();
            Check(Read(Buff(1071101)) == null, "missing catalog rejected");
            Console.WriteLine("Original-config effect reader: stacks, immunity, expiry, live values and schema guards passed.");
        }
        finally { RuntimeObject.Tables.Clear(); }
    }
}
