public static class EncounterRecommendationCheck
{
    private static int _checks;
    private static EncounterInput? _disagreement;
    private static void Check(bool value, string reason)
    {
        _checks++;
        if (!value) throw new Exception("Encounter recommendation: " + reason);
    }

    private static (double Ko, double Damage)[] Pairs(EncounterInput input) =>
        (from enemyDodge in new[] { false, true }
         from selfDodge in new[] { false, true }
         let outgoing = CardAdvisor.BeforeRoll(input.EnemyHp, input.Attack, input.EnemyDefense, enemyDodge, input.EnemyModifiers)
         let incoming = input.Counter ? CardAdvisor.BeforeRoll(input.Hp, input.EnemyAttack, input.Defense, selfDodge, input.Modifiers) : default
         let survives = 1 - outgoing.KnockoutChance
         select (outgoing.KnockoutChance - incoming.KnockoutChance * survives,
             outgoing.ExpectedDamage - incoming.ExpectedDamage * survives)).ToArray();

    public static void Policy()
    {
        var effects = new[] { default(HitModifiers), new HitModifiers(Immune: true), new HitModifiers(-99),
            new HitModifiers { MinimumHp = 1 }, new HitModifiers { AttackDice = DiceFaces.Range(3, 6), DefenseDice = DiceFaces.Range(1, 3) } };
        var formerlyAmbiguous = false;
        foreach (var myHp in new[] { 1, 3, 10 })
        foreach (var enemyHp in new[] { 1, 4, 10 })
        foreach (var myAttack in new[] { 0, 3, 10 })
        foreach (var enemyAttack in new[] { 0, 3, 10 })
        foreach (var counter in new[] { false, true })
        foreach (var effect in effects)
        {
            var input = new EncounterInput(9, myHp, myAttack, 2, enemyHp, enemyAttack, 1, counter)
                { Modifiers = effect, EnemyModifiers = effect };
            var result = EncounterAdvisor.Calculate(input)!.Value;
            var pairs = Pairs(input);
            var best = pairs.OrderByDescending(pair => Math.Round(pair.Ko, 9))
                .ThenByDescending(pair => Math.Round(pair.Damage, 9)).First();
            var expected = Math.Round(best.Ko, 9) > 0 || Math.Round(best.Ko, 9) == 0 && Math.Round(best.Damage, 9) > 0;
            Check(result.Attack == expected, "single favorable paired recommendation");
            Check(result.Attack.HasValue && (result.Attack == true) != (result.Attack == false), "exactly one supported recommendation");
            Check(result == EncounterAdvisor.Calculate(input), "deterministic snapshot");
            Check(result.SelfKoMin >= 0 && result.SelfKoMax <= 1 && result.SelfKoMin <= result.SelfKoMax
                && result.EnemyKoMin >= 0 && result.EnemyKoMax <= 1 && result.EnemyKoMin <= result.EnemyKoMax, "public probability bounds preserved");
            Check(result.Quick == "상대 KO " + CombatAdvisor.Probability(result.EnemyKoMax)
                + "%\n내 KO " + CombatAdvisor.Probability(result.SelfKoMin) + "%", "existing favorable display unchanged");
            var oldChoices = pairs.Select(pair => Math.Abs(pair.Ko) > 1e-6 ? (bool?)(pair.Ko > 0)
                : Math.Abs(pair.Damage) > 1e-6 ? pair.Damage > 0 : null).ToArray();
            formerlyAmbiguous |= oldChoices.Any(choice => !choice.HasValue) || oldChoices.Distinct().Count() > 1;
            if (_disagreement == null && oldChoices.Contains(true) && oldChoices.Contains(false)) {
                _disagreement = input;
                Console.WriteLine($"REPRO: old paired choices disagree, HP/ATK/DEF {myHp}/{myAttack}/2 vs {enemyHp}/{enemyAttack}/1, counter={counter}; new single recommendation={(expected ? "attack" : "release")}.");
            }
        }
        Check(formerlyAmbiguous, "old consensus/no recommendation condition actually exercised");
        Check(_disagreement.HasValue, "old attack/release disagreement separately exercised, not only exact ties");
        var baseline = new EncounterInput(9, 10, 3, 2, 9, 0, 0, false);
        Check(EncounterAdvisor.Calculate(baseline)!.Value.Attack == true, "harmless encounter attack");
        Check(EncounterAdvisor.Calculate(new(9, 1, 0, 0, 100, 20, 20, true))!.Value.Attack == false, "unfavorable return attack release");
        Check(EncounterAdvisor.Calculate(baseline with { Counter = true, Modifiers = new(Immune: true), EnemyModifiers = new(Immune: true) })!.Value.Attack == false,
            "exact tie releases");
        foreach (var map in new[] { 0, 1, 2, 3, 5, 7, 8, 11, 99 })
            Check(EncounterAdvisor.Calculate(baseline with { MapType = map }) == null, "PvP/unknown maps rejected");
        foreach (var invalid in new[] { baseline with { Hp = 0 }, baseline with { EnemyHp = 0 }, baseline with { Attack = -1 },
            baseline with { EnemyAttack = 1001 }, baseline with { Defense = -1001 }, baseline with { EnemyDefense = 1001 },
            baseline with { Modifiers = new() { DiceUnsupported = true } }, baseline with { EnemyModifiers = new() { AttackDistributionUnknown = true } },
            baseline with { Modifiers = new() { DefenseDistributionUnknown = true } }, baseline with { EnemyModifiers = new(-10001) } })
            Check(EncounterAdvisor.Calculate(invalid) == null, "unsupported public data rejected");
        Console.WriteLine($"PASS: favorable paired encounter policy {_checks} assertions, no hidden information or native execution.");
    }

    public static void Badges()
    {
        var attack = new EncounterInput(9, 10, 3, 2, 9, 0, 0, false);
        var release = new EncounterInput(9, 1, 0, 0, 100, 20, 20, true);
        var tie = attack with { Counter = true, Modifiers = new(Immune: true), EnemyModifiers = new(Immune: true) };
        foreach (var input in new[] { attack, release, tie, _disagreement ?? new EncounterInput(9, 3, 0, 2, 4, 3, 1, true) })
        foreach (var missingAsset in new[] { false, true })
        {
            GameUi.Reset(); RuntimeObject.MissingAsset = missingAsset;
            var scene = Scene(input);
            var oldAttack = scene.Attack.Snapshot(); var oldLeave = scene.Leave.Snapshot();
            GameUi.RunEncounter(scene.Fight, scene.Ui, input.MapType);
            var recommendation = EncounterAdvisor.Calculate(input)!.Value.Attack!.Value;
            Check(scene.Attack.Recommendations == (recommendation ? 1 : 0)
                && scene.Leave.Recommendations == (recommendation ? 0 : 1), "actual encounter/badge path shows one native thumb or fallback");
            Check(GameUi.AttackPanel?.Get<bool>("visible") == false && GameUi.LeavePanel?.Get<bool>("visible") == false, "no unsolicited hover panels");
            Check(oldAttack == scene.Attack.Snapshot() && oldLeave == scene.Leave.Snapshot(), "native button appearance/input untouched");
            Time.unscaledTime += .2f;
            GameUi.RunEncounter(scene.Fight, scene.Ui, input.MapType);
            Check(scene.Attack.Recommendations + scene.Leave.Recommendations == 1, "cached snapshot does not duplicate thumbs");
            GameUi.HideBadges();
            Check(scene.Attack.Recommendations + scene.Leave.Recommendations == 0, "native phase exit hides owned recommendation");
            GameUi.RunEncounter(scene.Fight, scene.Ui, input.MapType);
            Check(scene.Attack.Recommendations + scene.Leave.Recommendations == 1, "same native button later encounter recovers");
        }
        foreach (var gate in new[] { "spectator", "forced", "disabled", "grayed", "hidden", "enemy-player", "hidden-hp", "malformed-stat", "missing-property", "missing-counter", "unsupported-effect", "PvP" })
        {
            GameUi.Reset(); RuntimeObject.MissingAsset = false;
            var scene = Scene(attack);
            var map = 9;
            switch (gate) {
                case "spectator": scene.Ui.Field("playerState")!.Members["selectedIndex"] = 2; break;
                case "forced": scene.Launch.Members["internalVisible"] = false; break;
                case "disabled": scene.Attack.Members["touchable"] = false; break;
                case "grayed": scene.Leave.Members["grayed"] = true; break;
                case "hidden": scene.Attack.Members["internalVisible2"] = false; break;
                case "enemy-player": scene.Fight.Get("defenderData")!.Members["characterType"] = 1; break;
                case "hidden-hp": scene.Launch.Field("txt_HP")!.Members["onStage"] = false; break;
                case "malformed-stat": scene.Launch.Field("txt_ATK")!.Members["text"] = RuntimeObject.Text("unknown"); break;
                case "missing-property": scene.Fight.Get("attackerData")!.Members.Remove("Property"); break;
                case "missing-counter": scene.Fight.Get("defenderData")!.Get("Property")!.Members.Remove("Counter"); break;
                case "unsupported-effect": scene.Fight.Get("defenderData")!.Modifiers = null; break;
                case "PvP": map = 0; break;
            }
            GameUi.RunEncounter(scene.Fight, scene.Ui, map);
            Check(scene.Attack.Recommendations + scene.Leave.Recommendations == 0, "native/public eligibility gate retained: " + gate);
        }
        // A Shooting Star failure trips only its own breaker; the encounter recommendation still shows.
        GameUi.Reset(); RuntimeObject.MissingAsset = false; ShootingStar.Failure = new InvalidOperationException("inert relic read failure");
        var failing = Scene(attack); GameUi.RunEncounter(failing.Fight, failing.Ui, attack.MapType);
        Check(failing.Attack.Recommendations == 1 && Compatibility.Blocked.SetEquals(new[] { "ShootingStar" }) && ShootingStarUi.Cleared == 1,
            "a Shooting Star failure blocks only the star and keeps the encounter advice");
        ShootingStar.Failure = null; Compatibility.Blocked.Clear();
        Console.WriteLine($"PASS: production UpdateEncounter/EnsureBadges/AdviceBadge native substitutes; {_checks} cumulative assertions. Real asset rendering unexecuted.");
    }

    private static (RuntimeObject Fight, RuntimeObject Ui, RuntimeObject Launch, RuntimeObject Attack, RuntimeObject Leave) Scene(EncounterInput input)
    {
        var attack = RuntimeObject.Button(100); var leave = RuntimeObject.Button(450);
        var launch = RuntimeObject.Node(("btn_PK", attack), ("btn_Leave", leave),
            ("txt_HP", RuntimeObject.Node(("text", RuntimeObject.Text(input.EnemyHp.ToString(CultureInfo.InvariantCulture))))),
            ("txt_ATK", RuntimeObject.Node(("text", RuntimeObject.Text(input.EnemyAttack.ToString(CultureInfo.InvariantCulture))))),
            ("txt_DEF", RuntimeObject.Node(("text", RuntimeObject.Text(input.EnemyDefense.ToString(CultureInfo.InvariantCulture))))));
        var ui = RuntimeObject.Node(("com_LaunchPK", launch), ("playerState", RuntimeObject.Node(("selectedIndex", 0))));
        RuntimeObject Actor(int hp, int atk, int def, bool counter, HitModifiers modifiers, int type) {
            var actor = RuntimeObject.Node(("Property", RuntimeObject.Node(("HP", RuntimeObject.Node(("Value", hp))),
                ("ATK", RuntimeObject.Node(("Value", atk))), ("DEF", RuntimeObject.Node(("Value", def))),
                ("Counter", RuntimeObject.Node(("Value", counter))))), ("characterType", type));
            actor.Modifiers = modifiers; return actor;
        }
        var fight = RuntimeObject.Node(("attackerData", Actor(input.Hp, input.Attack, input.Defense, false, input.Modifiers, 1)),
            ("defenderData", Actor(input.EnemyHp, input.EnemyAttack, input.EnemyDefense, input.Counter, input.EnemyModifiers, 2)));
        return (fight, ui, launch, attack, leave);
    }
}

internal sealed class RuntimeObject
{
    private static long _next;
    internal static bool MissingAsset;
    internal readonly IntPtr Pointer = new(++_next);
    internal string Kind = "node";
    internal readonly Dictionary<string, object?> Members = new() { ["width"] = 200f, ["height"] = 64f, ["x"] = 0f,
        ["y"] = 0f, ["alpha"] = 1f, ["touchable"] = true, ["grayed"] = false, ["visible"] = true,
        ["onStage"] = true, ["internalVisible"] = true, ["internalVisible2"] = true, ["isDisposed"] = false };
    internal readonly List<RuntimeObject> Children = new();
    internal HitModifiers? Modifiers = default(HitModifiers);
    internal int Recommendations => Children.Count(child => child.Get<bool>("visible") && !child.Get<bool>("isDisposed")
        && (child.Kind == "thumb" || child.Kind == "label" && child.Members.GetValueOrDefault("caption") is "추천"));
    internal static RuntimeObject Node(params (string Key, object? Value)[] members) { var node = new RuntimeObject(); foreach (var (key, value) in members) node.Members[key] = value; return node; }
    internal static RuntimeObject Text(string text) => Node(("value", text));
    internal static RuntimeObject Button(float x) { var result = Node(("x", x)); result.Kind = "button"; return result; }
    internal string Snapshot() => string.Join("|", new[] { "x", "y", "alpha", "touchable", "grayed", "visible", "width", "height" }.Select(key => Members[key]));
    internal RuntimeObject? Get(string name) => Members.TryGetValue(name, out var value) ? value as RuntimeObject : null;
    internal RuntimeObject? Field(string name) => Get(name);
    internal T Get<T>(string name) => Members.TryGetValue(name, out var value) && value is T typed ? typed : throw new Exception("Unexpected getter " + name);
    internal string String() => Get<string>("value");
    internal void Set(string name, object value) { if (Kind == "button") throw new Exception("Native button modified"); if (name is not ("text" or "touchable" or "visible" or "alpha" or "y" or "rotation")) throw new Exception("Unexpected setter " + name); Members[name] = value; }
    internal static string FindClass(string package, string name) => package == "FairyGUI" && name is "GComponent" or "UIPackage" ? name : throw new Exception("Unexpected class");
    internal static RuntimeObject New(string type) => type == "GComponent" ? new() { Kind = "panel" } : throw new Exception("Unexpected allocation");
    internal static RuntimeObject? StaticCall(string type, string name, string url) => type == "UIPackage" && name == "CreateObjectFromURL" && url == "ui://1ov1i0v9r0lyc7"
        ? MissingAsset ? null : new() { Kind = "thumb" } : throw new Exception("Unexpected asset call");
    internal void Call(string name, params object[] args) {
        if (name == "AddChild" && args.Length == 1 && args[0] is RuntimeObject child) { Children.Add(child); child.Members["parent"] = this; return; }
        if (Kind == "button") throw new Exception("Native button mutated");
        if (name is "SetSize" or "SetXY" or "SetScale" && args.Length == 2 && args.All(value => value is float)) {
            var keys = name == "SetSize" ? new[] { "width", "height" } : name == "SetXY" ? new[] { "x", "y" } : new[] { "scaleX", "scaleY" };
            Members[keys[0]] = args[0]; Members[keys[1]] = args[1]; return;
        }
        if (name == "SetPivot" && args.Length == 3 && args[0] is float && args[1] is float && args[2] is bool) return;
        throw new Exception("Unexpected native method " + name);
    }
}

internal static class ModText { internal static string Text(string text) => text; }
internal static class PublicCombatEffects {
    internal static HitModifiers? Read(RuntimeObject? actor, string key, RuntimeObject? source) => actor?.Modifiers;
}
internal static class Plugin {
    internal sealed class Setting<T>(T value) { internal T Value = value; }
    internal static readonly Setting<float> UiScale = new(1);
    internal static readonly Setting<bool> ShowDetails = new(true), EncounterAdvice = new(true), ShootingStarShow = new(true);
    internal static readonly SilentLogger Logger = new();
    internal sealed class SilentLogger { internal void LogInfo(string value) { } internal void LogWarning(string value) { } }
    internal static readonly SilentDiagnostics Diagnostics = new();
    internal sealed class SilentDiagnostics { internal bool IsRecording => false; internal void State(string key, string value) { } }
}
// No Shooting Star relic in these fixtures: the release badge text stays empty as before.
internal readonly record struct ReleaseGain(int DamageMin, int DamageMax, int Stacks) { internal string Damage => DamageMin.ToString(); }
internal static class ShootingStar {
    internal static Exception? Failure;
    internal static ReleaseGain? Read(RuntimeObject? self, RuntimeObject? monster, HitModifiers? incoming) { if (Failure != null) throw Failure; return null; }
    internal static string Evidence => "";
}
internal static class ReleaseWatch { internal static void Offer(RuntimeObject monster, string evidence) { } }
internal static class ShootingStarUi { internal static int Cleared; internal static void Show(RuntimeObject button, ReleaseGain gain, float scale) { } internal static void Clear() { Cleared++; } }
internal static class Compatibility {
    internal static readonly HashSet<string> Blocked = new();
    internal static bool Allowed(string feature) => !Blocked.Contains(feature);
    internal static void Block(string feature, Exception error, params Action[] cleanup) { if (Blocked.Add(feature)) foreach (var action in cleanup) action(); }
}
internal static class NativeUi {
    internal sealed class Surface(RuntimeObject panel, float width, float height) { internal void Refresh() { } }
    internal static RuntimeObject? Arrow(RuntimeObject parent, float x, float y, int size, bool direction) => throw new Exception("Encounter guide intentionally disabled");
    internal static void Play(RuntimeObject node, string transition, int repeat = 1) => throw new Exception("Encounter guide intentionally disabled");
}
internal static class GameUi {
    private static RuntimeObject? _defButton, _dodgeButton;
    private static AdviceBadge? _defBadge, _dodgeBadge;
    private static EncounterInput? _encounterInput;
    private static EncounterAdvice? _encounterResult;
    private static bool _adviceShowing;
    internal static RuntimeObject? AttackPanel => _defBadge?.Panel;
    internal static RuntimeObject? LeavePanel => _dodgeBadge?.Panel;
    internal static void Reset() { _defBadge?.Dispose(); _dodgeBadge?.Dispose(); _defBadge = _dodgeBadge = null; _defButton = _dodgeButton = null; _encounterInput = null; _encounterResult = null; _adviceShowing = false; Time.unscaledTime += 1; }
    internal static void HideBadges() { _defBadge?.Hide(); _dodgeBadge?.Hide(); _adviceShowing = false; }
    internal static void RunEncounter(RuntimeObject fight, RuntimeObject ui, int map) { UpdateEncounter(fight, ui, map); if (_adviceShowing) { _defBadge?.Animate(); _dodgeBadge?.Animate(); } }
    internal static bool Visible(RuntimeObject? node) { if (node == null || node.Get<bool>("isDisposed") || !node.Get<bool>("onStage")) return false; for (var current = node; current != null; current = current.Get("parent")) if (!current.Get<bool>("internalVisible") || !current.Get<bool>("internalVisible2")) return false; return true; }
    internal static RuntimeObject NewLabel(RuntimeObject parent, string text, int size, int align = 1, bool rich = false, bool dark = false) { var label = RuntimeObject.Node(("caption", text)); label.Kind = "label"; label.Members["touchable"] = false; parent.Call("AddChild", label); return label; }
    internal static void Dispose(RuntimeObject? node) { if (node != null) node.Members["isDisposed"] = true; }
__ENCOUNTER_UI__
}
