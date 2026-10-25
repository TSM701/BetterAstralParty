using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

// Replays the public combat inputs logged in game (diagnostic logging ON) through the current damage model and
// groups the outcome by shown buff, so unaudited effects and wrong rules surface from real fights. Read-only.
internal static class CombatAudit
{
    internal sealed class Hit
    {
        internal string Combat = "";
        internal int Before, Attack, Defense, Die, Logged, Observed = -1;
        internal bool Dodge, MonsterDefeat;
        internal HitModifiers Modifiers;
        internal readonly SortedSet<int> Buffs = new();
        internal int Recomputed => CombatAdvisor.FinalDamage(Before, Attack, Defense, Dodge, Die, Modifiers);
        internal int ExpectedDisplay => MonsterDefeat ? Math.Max(1, Math.Max(0, Before - Recomputed)) : Math.Max(0, Before - Recomputed);
        // Zero HP (or the native defeat HP 1) hides overkill; only lethality is comparable there.
        internal bool Exact => Observed > 0 && !(MonsterDefeat && Observed == 1);
        internal bool Matches => ExpectedDisplay == Observed;
    }

    private static readonly Regex Line = new(@"\| combat=([^;]+); (.*)$");
    private static readonly Regex Prediction = new(@"^prediction before=(\d+); atk=(\d+); def=(\d+); attackDie=(\d+); dodge=(True|False); damage=(-?\d+); modifiers=(.*)$");
    private static readonly Regex Result = new(@"^damage\.result before=\d+; predictedDamage=-?\d+; expectedHP=\d+; observedHP=(\d+); expectedDisplayHP=\d+; monsterDefeatPresentation=(True|False);");
    private static readonly Regex Effects = new(@"^combatEffects\.(?!resolver\.|source\.|schema\.)[\w.]+=(.*)$");

    // A Shooting Star release (ReleaseWatch): its logged public inputs and what the server did to the monster's HP.
    internal sealed class Release
    {
        internal int Before, After = -1, Purple, Gold, Starlight, Incoming, Cap, Stacks, Raises;
        internal int? ChangeHp;
        internal bool Boss;
        // HP losses in the order seen (ReleaseWatch since 2026-10-25); two held relics show two when they land apart.
        internal List<int> Hits = new();
        // Exact damage when the monster survived, or when a KO's native change is deeper than its HP (overkill kept).
        internal int? Damage => After > 0 ? Before - After : After == 0 && ChangeHp is { } change && -change > Before ? -change : null;
        internal bool Lethal => After == 0;
    }

    // Candidate release rules. The first is the current model (ShootingStar.Calculate); the others are what it replaced
    // or might still be wrong about. Each returns the (min, max) damage it predicts.
    internal static readonly (string Name, Func<Release, (int Min, int Max)> Rule)[] ReleaseRules =
    {
        ("current: held Starlight, all incoming modifiers, cap as range, two relics as one hit each", Apart),
        ("this release's own Starlight counted, no incoming modifiers", r => Clamp(Raw(r, true), 0)),
        ("no incoming modifiers", r => Clamp(Raw(r, false), 0)),
        ("incoming increases only", r => Clamp(Raw(r, false) + r.Raises, r.Cap)),
        ("cap always applies", r => { var (min, _) = Clamp(Raw(r, false) + r.Incoming, r.Cap); return (min, min); }),
        ("two relics as one hit", Together),
    };

    private static int GoldRaw(Release r, bool ownStack) => r.Gold + (r.Gold > 0 && r.Boss ? r.Starlight + (ownStack ? r.Stacks : 0) : 0);
    private static int Raw(Release r, bool ownStack) => r.Purple + GoldRaw(r, ownStack);
    private static (int Min, int Max) Together(Release r) => Clamp(Raw(r, false) + r.Incoming, r.Cap);
    // Both relics held: each its own hit, the incoming modifiers applied to each.
    private static (int Min, int Max) Apart(Release r)
    {
        if (r.Purple == 0 || r.Gold == 0) return Together(r);
        var (purpleMin, purpleMax) = Clamp(r.Purple + r.Incoming, r.Cap); var (goldMin, goldMax) = Clamp(GoldRaw(r, false) + r.Incoming, r.Cap);
        return (purpleMin + goldMin, purpleMax + goldMax);
    }
    private static (int Min, int Max) Clamp(int damage, int cap) { damage = Math.Max(0, damage); return (cap > 0 ? Math.Min(damage, cap) : damage, damage); }

    // A rule fits a release when its range holds the exact damage, or, for a KO without one, reaches the monster's HP.
    internal static bool Fits(Release r, (int Min, int Max) p) => r.Damage is { } exact ? p.Min <= exact && exact <= p.Max : r.Lethal && p.Max >= r.Before;

    // Per relic: what the current rule predicts for each of the two hits, purple first (matched in either order).
    internal static ((int Min, int Max) Purple, (int Min, int Max) Gold) SplitHits(Release r) => (Clamp(r.Purple + r.Incoming, r.Cap), Clamp(GoldRaw(r, false) + r.Incoming, r.Cap));
    internal static bool HitsFit(Release r)
    {
        if (r.Hits.Count != 2) return false;
        var (purple, gold) = SplitHits(r);
        bool In(int lost, (int Min, int Max) p) => p.Min <= lost && lost <= p.Max;
        return In(-r.Hits[0], purple) && In(-r.Hits[1], gold) || In(-r.Hits[0], gold) && In(-r.Hits[1], purple);
    }

    private static readonly Regex ReleaseLine = new(@"shootingStar\.result=release=\d+; before=(\d+); after=(\w+); (?:changeHP=(-?\d+); )?(?:hits=(-?\d+(?:,-?\d+)*); )?(?:removed=True; )?purple=(\d+); gold=(\d+); boss=(True|False); starlight=(-?\d+); incoming=(-?\d+); cap=(\d+); stacks=(\d+);");
    private static readonly Regex EncounterEnemy = new(@"combatEffects\.encounter\.enemy=(.*)$");
    private static readonly Regex ChangeHp = new(@"changeHP=(-?\d+)");

    internal static List<Release> ParseReleases(IEnumerable<string> lines)
    {
        var releases = new List<Release>(); var raises = 0;
        foreach (var line in lines)
        {
            var enemy = EncounterEnemy.Match(line);
            if (enemy.Success)
            {
                // The latest encounter snapshot before a release is that monster's: split its resolved additions.
                raises = Regex.IsMatch(enemy.Groups[1].Value, @"property:10006:[1-9]") ? 1 : 0; // Mark: +1 while marked
                foreach (Match add in Regex.Matches(enemy.Groups[1].Value, @"resolved:\d+:add=(-?\d+)"))
                    raises += Math.Max(0, int.Parse(add.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }
            var m = ReleaseLine.Match(line);
            if (!m.Success || !int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var after)) continue;
            int At(int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
            releases.Add(new Release
            {
                Before = At(1), After = after, ChangeHp = m.Groups[3].Success ? At(3) : null, Purple = At(5), Gold = At(6), Boss = m.Groups[7].Value == "True",
                Starlight = At(8), Incoming = At(9), Cap = At(10), Stacks = At(11), Raises = raises,
                Hits = m.Groups[4].Success ? m.Groups[4].Value.Split(',').Select(hit => int.Parse(hit, CultureInfo.InvariantCulture)).ToList() : new()
            });
        }
        return releases;
    }

    internal static string ReleaseReport(List<Release> releases)
    {
        var output = new StringBuilder().AppendLine().AppendLine($"Shooting Star releases: {releases.Count} with a result ({releases.Count(r => r.Damage != null)} exact, {releases.Count(r => r.Lethal && r.Damage == null)} KO without overkill).");
        if (releases.Count == 0) return output.ToString();
        foreach (var (name, rule) in ReleaseRules)
            output.AppendLine($"  {releases.Count(r => Fits(r, rule(r)))} fit / {releases.Count(r => !Fits(r, rule(r)))} miss  {name}");
        // Which alternatives the evidence has not separated from the current rule yet.
        var current = ReleaseRules[0].Rule;
        foreach (var (name, rule) in ReleaseRules.Skip(1))
            if (!releases.Any(r => Fits(r, current(r)) != Fits(r, rule(r))))
                output.AppendLine($"  Not separated yet from the current rule: {name}");
        var split = releases.Where(r => r.Purple > 0 && r.Gold > 0 && r.Hits.Count == 2).ToList();
        if (split.Count > 0) output.AppendLine($"  Two relics seen as two hits: {split.Count(HitsFit)} fit / {split.Count(r => !HitsFit(r))} miss  each relic its own hit");
        return output.ToString();
    }

    // Lethal hits whose native ChangeHP was logged: does it keep the overkill (equals the predicted damage) or stop at the HP?
    internal static string ChangeHpReport(IEnumerable<string> lines)
    {
        int overkill = 0, clamped = 0, other = 0;
        foreach (var raw in lines)
        {
            var line = Line.Match(raw);
            if (!line.Success) continue;
            var result = Regex.Match(line.Groups[2].Value, @"^damage\.result before=(\d+); predictedDamage=(-?\d+); expectedHP=\d+; observedHP=(\d+);");
            var change = ChangeHp.Match(line.Groups[2].Value);
            if (!result.Success || !change.Success || result.Groups[3].Value != "0") continue;
            int before = int.Parse(result.Groups[1].Value, CultureInfo.InvariantCulture), predicted = int.Parse(result.Groups[2].Value, CultureInfo.InvariantCulture);
            var lost = -int.Parse(change.Groups[1].Value, CultureInfo.InvariantCulture);
            if (lost == predicted && predicted > before) overkill++; else if (lost == before) clamped++; else other++;
        }
        return $"{Environment.NewLine}KO hits with native changeHP: {overkill} kept the predicted overkill, {clamped} stopped at the HP, {other} other.{Environment.NewLine}";
    }

    private static int Field(string text, string name) => int.Parse(Regex.Match(text, @"\b" + name + @" = (-?\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

    internal static List<Hit> Parse(IEnumerable<string> lines)
    {
        var hits = new List<Hit>(); var buffs = new Dictionary<string, SortedSet<int>>(); var open = new Dictionary<string, Hit>();
        foreach (var raw in lines)
        {
            var line = Line.Match(raw);
            if (!line.Success) continue;
            var combat = line.Groups[1].Value; var message = line.Groups[2].Value;
            if (!buffs.TryGetValue(combat, out var seen)) buffs[combat] = seen = new SortedSet<int>();
            var effects = Effects.Match(message);
            if (effects.Success)
            {
                // Entries: "id:progress:rounds:delay=N", "property:id:value", "resolved:..." or a plain id list.
                foreach (var entry in effects.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var buff = Regex.Match(entry, @"^(?:property:)?(\d+)(?::|$)");
                    if (buff.Success && !entry.Contains("delay=", StringComparison.Ordinal) || buff.Success && entry.EndsWith("delay=0", StringComparison.Ordinal))
                        seen.Add(int.Parse(buff.Groups[1].Value, CultureInfo.InvariantCulture));
                }
                continue;
            }
            var prediction = Prediction.Match(message);
            if (prediction.Success)
            {
                int At(int group) => int.Parse(prediction.Groups[group].Value, CultureInfo.InvariantCulture);
                var modifiers = prediction.Groups[7].Value;
                open[combat] = new Hit
                {
                    Combat = combat, Before = At(1), Attack = At(2), Defense = At(3), Die = At(4), Dodge = prediction.Groups[5].Value == "True", Logged = At(6),
                    Modifiers = new HitModifiers(Field(modifiers, "Bonus"), Field(modifiers, "Cap"), modifiers.Contains("Immune = True", StringComparison.Ordinal))
                        { MinimumHp = Field(modifiers, "MinimumHp"), OnHitBonus = Field(modifiers, "OnHitBonus") }
                };
                continue;
            }
            var result = Result.Match(message);
            if (result.Success && open.Remove(combat, out var hit))
            {
                hit.Observed = int.Parse(result.Groups[1].Value, CultureInfo.InvariantCulture);
                hit.MonsterDefeat = result.Groups[2].Value == "True";
                hit.Buffs.UnionWith(seen);
                hits.Add(hit);
            }
        }
        return hits;
    }

    internal static string Report(List<Hit> hits, int files)
    {
        var output = new StringBuilder();
        var exact = hits.Count(hit => hit.Exact); var wrong = hits.Where(hit => !hit.Matches).ToList();
        output.AppendLine($"Combat audit: {hits.Count} logged hit(s) in {files} log file(s); {exact} exact HP, {hits.Count - exact} lethal; {wrong.Count} mismatch(es) with the current model.");
        output.AppendLine("Inputs are the public numbers logged in game; an exact match checks HP loss, a lethal one only that the hit was lethal.");
        if (wrong.Count > 0) output.AppendLine().AppendLine("Mismatches:");
        foreach (var hit in wrong)
            output.AppendLine($"  {hit.Combat}: HP {hit.Before}, ATK {hit.Attack}, DEF {hit.Defense}, die {hit.Die}{(hit.Dodge ? " dodge" : "")}, "
                + $"expected HP {hit.ExpectedDisplay}, observed {hit.Observed}; buffs {(hit.Buffs.Count == 0 ? "none" : string.Join(",", hit.Buffs))}");
        var changed = hits.Count(hit => hit.Recomputed != hit.Logged);
        if (changed > 0) output.AppendLine().AppendLine($"{changed} hit(s) compute differently now than when logged (model changed since).");
        output.AppendLine().AppendLine("Shown buffs (exact matches / lethal matches / mismatches):");
        foreach (var id in hits.SelectMany(hit => hit.Buffs).Distinct().Order())
        {
            var with = hits.Where(hit => hit.Buffs.Contains(id)).ToList();
            var status = CombatBuffAudit.Modeled.Contains(id) ? "modeled" : CombatBuffAudit.Neutral.Contains(id) ? "neutral" : "not counted";
            output.AppendLine($"  {id}: {with.Count(hit => hit.Exact && hit.Matches)} / {with.Count(hit => !hit.Exact && hit.Matches)} / {with.Count(hit => !hit.Matches)}  {status}");
        }
        var candidates = hits.SelectMany(hit => hit.Buffs).Distinct().Where(id => !CombatBuffAudit.Counted(id)
            && hits.Where(hit => hit.Buffs.Contains(id)).All(hit => hit.Matches) && hits.Count(hit => hit.Buffs.Contains(id) && hit.Exact) >= 3).Order().ToList();
        output.AppendLine().AppendLine(candidates.Count == 0 ? "No uncounted buff has three or more exact matches without a mismatch yet."
            : "Uncounted buffs with three or more exact matches and no mismatch (review their descriptions before adding to the neutral list): " + string.Join(", ", candidates));
        return output.ToString();
    }

    internal static int Run(string[] args)
    {
        var logs = new List<string>(); string? file = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length || args[i] is not ("--log" or "--output")) throw new ArgumentException("Usage: combat-audit [--log <file>]... [--output <file>]");
            if (args[i] == "--log") logs.Add(Path.GetFullPath(args[++i])); else file = Path.GetFullPath(args[++i]);
        }
        if (logs.Count == 0) logs.AddRange(NativeCoverage.RuntimeLogs());
        var lines = logs.SelectMany(path => File.ReadLines(path, Encoding.UTF8)).ToList();
        var hits = Parse(lines);
        var releases = ParseReleases(lines);
        var report = Report(hits, logs.Count) + ChangeHpReport(lines) + ReleaseReport(releases);
        Console.Write(report);
        if (file != null) File.WriteAllText(file, report, new UTF8Encoding(false));
        return hits.Any(hit => !hit.Matches) || releases.Any(r => !Fits(r, ReleaseRules[0].Rule(r))) ? 2 : 0;
    }

    // Synthetic logs only; runs as the combat-audit build check.
    internal static void SelfCheck(Action<bool, string> check)
    {
        const string modifiers = "HitModifiers { Bonus = 0, Cap = 0, Immune = False, ShushuShield = False, MinimumHp = 0, OnHitBonus = 0, AttackDice = DiceFaces { Mask = 0 } }; order=x";
        var lines = new[]
        {
            "t | combat=s-1; combatEffects.defender=10007:0:0:delay=0,resolved:10007:add=0,5007002:1:0:delay=2",
            "t | combat=s-1; prediction before=10; atk=8; def=3; attackDie=4; dodge=False; damage=5; modifiers=" + modifiers,
            "t | combat=s-1; damage.result before=10; predictedDamage=5; expectedHP=5; observedHP=5; expectedDisplayHP=5; monsterDefeatPresentation=False; hpMismatch=False",
            "t | combat=s-2; combatEffects.unknown.defender=5007002",
            "t | combat=s-2; combatEffects.resolver.defender=additive-before-cap; adjustment=9",
            "t | combat=s-2; prediction before=10; atk=8; def=3; attackDie=4; dodge=False; damage=5; modifiers=" + modifiers,
            "t | combat=s-2; damage.result before=10; predictedDamage=5; expectedHP=5; observedHP=6; expectedDisplayHP=5; monsterDefeatPresentation=False; hpMismatch=True",
            "t | combat=s-3; prediction before=3; atk=9; def=1; attackDie=2; dodge=False; damage=8; modifiers=" + modifiers.Replace("Cap = 0", "Cap = 1"),
            "t | combat=s-3; damage.result before=3; predictedDamage=8; expectedHP=0; observedHP=2; expectedDisplayHP=0; monsterDefeatPresentation=False; hpMismatch=True",
        };
        var hits = Parse(lines);
        check(hits.Count == 3 && hits[0].Buffs.SetEquals(new[] { 10007 }) && hits[1].Buffs.SetEquals(new[] { 5007002 }), "Combat audit reads shown buffs per hit, skipping delayed ones");
        check(hits[0].Matches && hits[0].Exact && !hits[1].Matches, "Combat audit replays logged inputs through the current model");
        check(hits[2].Modifiers.Cap == 1 && hits[2].Recomputed == 1 && hits[2].Matches && hits[2].Recomputed != hits[2].Logged, "Combat audit recomputes with the logged modifiers, not the logged damage");
        var report = Report(hits, 1);
        check(report.Contains("5007002: 0 / 0 / 1  not counted", StringComparison.Ordinal) && report.Contains("10007: 1 / 0 / 0  neutral", StringComparison.Ordinal)
            && report.Contains("1 mismatch(es)", StringComparison.Ordinal), "Combat audit groups outcomes by buff and audit status");
        var releaseLines = new[]
        {
            // The 2026-10-23 observation: gold vs a boss, 15 Starlight held, Weakness Mark +1, 83 -> 65.
            "t | combat=e-1; combatEffects.encounter.enemy=10711102:3:0:delay=0,resolved:10711102:add=0,1140102:0:1:delay=0,resolved:1140102:add=1",
            "t | shootingStar.result=release=1; before=83; after=65; changeHP=-18; purple=0; gold=2; boss=True; starlight=15; incoming=1; cap=0; stacks=1; damage=18",
            // A KO whose native change keeps the overkill, and one that stops at the HP.
            "t | shootingStar.result=release=2; before=1; after=0; changeHP=-2; purple=0; gold=2; boss=False; starlight=0; incoming=0; cap=0; stacks=1; damage=2",
            "t | shootingStar.result=release=3; before=1; after=0; changeHP=-1; purple=1; gold=0; boss=False; starlight=0; incoming=0; cap=0; stacks=1; damage=1",
            // Both relics against a +1 monster: one hit would be 4, one hit each 5.
            "t | shootingStar.result=release=4; before=10; after=5; changeHP=-5; purple=1; gold=2; boss=False; starlight=0; incoming=1; cap=0; stacks=2; damage=5",
        };
        var releases = ParseReleases(releaseLines);
        check(releases.Count == 4 && releases[0].Damage == 18 && releases[0].Raises == 1 && releases[1].Damage == 2 && releases[2].Damage == null && releases[2].Lethal,
            "Release audit reads exact damage, overkill kept by changeHP and lethal-only results");
        check(ReleaseRules.Select(rule => Fits(releases[0], rule.Rule(releases[0]))).SequenceEqual(new[] { true, true, false, true, true, true }),
            "Release rules: 18 fits the current rule and own-Starlight, not the no-modifier rule");
        check(Fits(releases[3], ReleaseRules[0].Rule(releases[3])) && !Fits(releases[3], Together(releases[3])),
            "Release rules: two relics hitting once each fit the current rule and separate the one-hit rule");
        var releaseReport = ReleaseReport(releases.Take(3).ToList()); // the single-relic evidence alone
        check(releaseReport.Contains("Not separated yet from the current rule: this release's own Starlight counted", StringComparison.Ordinal)
            && !releaseReport.Contains("separated yet from the current rule: no incoming", StringComparison.Ordinal), "Release audit names the rules the evidence has not separated");
        // 2026-10-25 format: both hits of two relics against a boss (purple 1, then gold 2 + 12 held Starlight).
        var split = ParseReleases(new[] { "t | shootingStar.result=release=8; before=59; after=44; changeHP=-15; hits=-1,-14; purple=1; gold=2; boss=True; starlight=12; incoming=0; cap=0; stacks=2; damage=15",
            "t | shootingStar.result=release=9; before=4; after=removed; purple=0; gold=2; boss=False; starlight=0; incoming=0; cap=0; stacks=1; damage=2" });
        check(split.Count == 1 && split[0].Damage == 15 && split[0].Hits.SequenceEqual(new[] { -1, -14 }) && HitsFit(split[0]) && Fits(split[0], ReleaseRules[0].Rule(split[0]))
            && ReleaseReport(split).Contains("Two relics seen as two hits: 1 fit / 0 miss", StringComparison.Ordinal), "Release audit checks each relic's hit");
        check(ChangeHpReport(new[] { "t | combat=k-1; damage.result before=3; predictedDamage=8; expectedHP=0; observedHP=0; expectedDisplayHP=0; monsterDefeatPresentation=False; hpMismatch=False; modifiers=x; changeHP=-8" })
            .Contains("1 kept the predicted overkill", StringComparison.Ordinal), "KO changeHP report");
    }
}
