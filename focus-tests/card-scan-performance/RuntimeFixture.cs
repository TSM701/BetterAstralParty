using System.Diagnostics;
using System.Text.Json;

namespace UnityEngine { internal static class Time { } }

namespace BetterAstralParty
{
    internal readonly record struct NativeRead(IntPtr Pointer, string Kind, string Member, int Index = -1);
    internal sealed class RuntimeObject
    {
        private static int _next;
        internal readonly IntPtr Pointer = (IntPtr)(++_next);
        internal readonly string TypeName;
        internal readonly Dictionary<string, RuntimeObject> References = new();
        internal readonly Dictionary<string, object> Values = new();
        internal readonly List<RuntimeObject> Items = new();
        internal bool Visible = true, Disposed, Native = true;
        internal RuntimeObject(string type) => TypeName = type;
        private void Read(string kind, string member, int index = -1)
        { if (Native && Check.Capture) Check.Reads.Add(new(Pointer, kind, member, index)); }
        internal RuntimeObject? Field(string name) { Read("field", name); return References.GetValueOrDefault(name); }
        internal RuntimeObject? Get(string name) { Read("get", name); return References.GetValueOrDefault(name); }
        internal T Get<T>(string name)
        {
            Read("get-value", name);
            if (name == "Count") return (T)(object)Items.Count;
            if (name == "isDisposed") return (T)(object)Disposed;
            return Values.TryGetValue(name, out var value) && value is T typed
                ? typed : throw new InvalidOperationException("Unexpected typed getter: " + TypeName + "." + name);
        }
        internal T Value<T>()
        {
            Read("value", typeof(T).Name);
            return Values.TryGetValue("value", out var value) && value is T typed
                ? typed : throw new InvalidOperationException("Unexpected boxed value: " + TypeName);
        }
        internal RuntimeObject Call(string name, params object[] arguments)
        {
            if (name != "get_Item" || arguments is not [int index]) throw new InvalidOperationException("Unexpected native method");
            Read("call", name, index); return Items[index];
        }
        internal static RuntimeObject Box(object value) { var result = new RuntimeObject("Box"); result.Values["value"] = value; return result; }
    }
    internal sealed class Setting<T>(T value) { internal T Value = value; }
    internal static class Plugin
    {
        internal static readonly Setting<bool> Enabled = new(true), KoMinimum = new(true);
        internal static readonly Setting<float> UiScale = new(1);
        internal static Diagnostics? MinimalDiagnostics;
    }
    internal sealed class Diagnostics
    {
        internal bool Detailed => true;
        internal void CardCoverage(int supported, int unsupported)
        { if (Check.Capture) Check.Effects.Add($"coverage:{supported}:{unsupported}"); }
    }
    internal static class GameUi
    {
        internal static readonly List<RuntimeObject> Hit = new();
        internal static bool Visible(RuntimeObject? node) => node != null && node.Visible && !node.Disposed;
        internal static IReadOnlyList<RuntimeObject> PointerPath() => Hit;
    }
    internal static class AdviceText { internal static string CardTitle(string title) => title; }
    internal sealed class AdviceBadge
    {
        internal readonly RuntimeObject Panel = new("OwnedPanel") { Native = false };
        private readonly RuntimeObject _face;
        internal AdviceBadge(RuntimeObject face, int width, int height, int fontSize)
        { _face = face; if (Check.Capture) Check.Effects.Add($"create:{face.Pointer}:{width}:{height}:{fontSize}"); }
        internal void Show(string title, bool recommended, float scale, float offset)
        { if (Check.Capture) Check.Effects.Add($"show:{_face.Pointer}:{title}:{recommended}:{scale}:{offset}"); }
        internal void Hide() { if (Check.Capture) Check.Effects.Add($"hide:{_face.Pointer}"); }
        internal void Dispose() { Panel.Disposed = true; if (Check.Capture) Check.Effects.Add($"dispose:{_face.Pointer}"); }
    }
    internal static class CardDiceUi
    {
        internal static void Show(RuntimeObject ui, RuntimeObject point, PreRollCombat input, CardBonus? card, bool preview, bool unsupported)
        { if (Check.Capture) Check.Effects.Add($"dice:{input}:{card}:{preview}:{unsupported}"); }
        internal static void Hide() { if (Check.Capture) Check.Effects.Add("dice-hide"); }
        internal static void Clear() { if (Check.Capture) Check.Effects.Add("dice-clear"); }
        internal static void Animate() { if (Check.Capture) Check.Effects.Add("dice-animate"); }
    }
    internal sealed class World
    {
        internal readonly RuntimeObject Fight = new("Fight"), Ui = new("Ui"), Point = new("Point"), Ready = new("Ready");
        internal readonly RuntimeObject Cards = new("Cards"), Role = new("Role");
        internal PreRollCombat Input = new(9, 5, 3, 3, 1, 1);
        internal World(int count, int role, int variant)
        {
            Ui.References["playerState"] = Role; Role.Values["selectedIndex"] = role;
            var container = new RuntimeObject("CardContainer"); Ui.References["com_Card"] = container;
            container.References["btn_FinishPkCard"] = Ready; Ready.Values["touchable"] = true;
            Fight.References["cardItemList"] = Cards;
            for (var i = 0; i < count; i++)
            {
                var card = new RuntimeObject("UIHandCard_Button_Card"); Cards.Items.Add(card);
                card.References["IsRelease"] = RuntimeObject.Box(false); card.References["_EnableUse"] = RuntimeObject.Box(true);
                var face = new RuntimeObject("UICom_Card"); card.References["com_Card"] = face; face.Values["opened"] = true;
                var icon = new RuntimeObject("Icon"); face.References["com_CardIcon"] = icon;
                var cost = new RuntimeObject("Cost"); icon.References["Cost"] = cost; cost.Values["selectedIndex"] = i % 3;
                var config = new RuntimeObject("Config"); card.References["_config"] = config;
                config.Values["Id"] = 10008; config.Values["EffectType"] = role == 1 ? 2 : 1; config.Values["CardType"] = role == 1 ? 2 : 1;
                var parameters = new RuntimeObject("Params"); config.References["Params"] = parameters;
                parameters.Items.Add(RuntimeObject.Box(1 + i % 3)); parameters.Items.Add(RuntimeObject.Box(1 + i % 3));
                var buffs = new RuntimeObject("BuffIds"); config.References["BuffIds"] = buffs;
                config.References["CardIds"] = new RuntimeObject("CardIds");
                switch ((i + variant) % 12)
                {
                    case 1: card.Visible = false; break;
                    case 2: card.References["IsRelease"] = RuntimeObject.Box(true); break;
                    case 3: face.Visible = false; break;
                    case 4: face.Values["opened"] = false; break;
                    case 5: parameters.Items.Clear(); break;
                    case 6: parameters.Items.Add(RuntimeObject.Box(1)); break;
                    case 7: parameters.Items.Add(RuntimeObject.Box(2)); parameters.Items.Add(RuntimeObject.Box(3)); break;
                    case 8: buffs.Items.Add(RuntimeObject.Box(1)); break;
                    case 9: card.References["_EnableUse"] = RuntimeObject.Box(false); break;
                    case 10: config.Values["CardType"] = role == 1 ? 1 : 2; break;
                    case 11: cost.Values["selectedIndex"] = 21; break;
                }
            }
            GameUi.Hit.Clear(); if (count != 0) GameUi.Hit.Add(Cards.Items[count / 2]);
        }
    }
    internal static class Check
    {
        internal static bool Capture;
        internal static readonly List<NativeRead> Reads = new();
        internal static readonly List<string> Effects = new();
        private static int _cases, _assertions;
        private static void Require(bool condition, string message)
        { _assertions++; if (!condition) throw new InvalidOperationException(message); }
        private static (NativeRead[] Reads, string[] Effects) Observe(World world, bool legacy, Action? change = null)
        {
            Capture = false; CardUi.Clear(); ReferenceCardUi.Clear(); Reads.Clear(); Effects.Clear(); Capture = true;
            void Update() { if (legacy) ReferenceCardUi.Update(world.Fight, world.Ui, world.Input, world.Point); else CardUi.Update(world.Fight, world.Ui, world.Input, world.Point); }
            Update(); Update(); // Same snapshot must retain result/badge caching.
            if (change != null) { change(); Update(); }
            Capture = false; return (Reads.ToArray(), Effects.ToArray());
        }
        private static void Equivalent(World world)
        {
            var before = Observe(world, true); var after = Observe(world, false);
            Require(before.Reads.SequenceEqual(after.Reads), "Native read count/order drift in case " + _cases);
            Require(before.Effects.SequenceEqual(after.Effects), "Badge/preview/diagnostic output drift in case " + _cases); _cases++;
        }
        private static void Failure(World world)
        {
            (NativeRead[] Reads, string[] Effects, string Error) ObserveError(bool legacy)
            {
                Capture = false; CardUi.Clear(); ReferenceCardUi.Clear(); Reads.Clear(); Effects.Clear(); Capture = true;
                try
                {
                    if (legacy) ReferenceCardUi.Update(world.Fight, world.Ui, world.Input, world.Point);
                    else CardUi.Update(world.Fight, world.Ui, world.Input, world.Point);
                }
                catch (InvalidOperationException error)
                { Capture = false; return (Reads.ToArray(), Effects.ToArray(), error.Message); }
                throw new InvalidOperationException("Expected substitute ABI failure was not propagated");
            }
            var before = ObserveError(true); var after = ObserveError(false);
            Require(before.Reads.SequenceEqual(after.Reads), "Reads before native substitute failure changed");
            Require(before.Effects.SequenceEqual(after.Effects) && before.Error == after.Error, "Failure propagation changed"); _cases++;
        }
        private static void Cases()
        {
            Plugin.MinimalDiagnostics = new();
            for (var count = 0; count <= 12; count++)
            for (var role = 0; role <= 3; role++)
            for (var variant = 0; variant < 12; variant++)
            { Plugin.Enabled.Value = true; Plugin.KoMinimum.Value = true; Equivalent(new(count, role, variant)); }
            foreach (var advice in new[] { false, true })
            foreach (var dice in new[] { false, true })
            foreach (var ready in new[] { false, true })
            {
                Plugin.Enabled.Value = advice; Plugin.KoMinimum.Value = dice;
                var world = new World(11, 0, 0); world.Ready.Visible = ready; Equivalent(world);
            }
            Plugin.Enabled.Value = Plugin.KoMinimum.Value = true;
            var invalid = new World(11, 0, 0); invalid.Input = invalid.Input with { MapType = 0 }; Equivalent(invalid);
            var unsupported = new World(11, 0, 0); unsupported.Input = unsupported.Input with { Modifiers = new HitModifiers { AttackDistributionUnknown = true } }; Equivalent(unsupported);
            foreach (var role in new[] { 0, 1 })
            foreach (var length in new[] { 0, 1, 2, 3, 4 })
            {
                var world = new World(11, role, 0);
                for (var index = 0; index < world.Cards.Items.Count; index++)
                {
                    var card = world.Cards.Items[index]; card.Visible = true;
                    card.References["IsRelease"] = RuntimeObject.Box(false);
                    card.References["_EnableUse"] = RuntimeObject.Box(index != 9);
                    var face = card.References["com_Card"]; face.Visible = true; face.Values["opened"] = true;
                    face.References["com_CardIcon"].References["Cost"].Values["selectedIndex"] = index % 3;
                    var config = card.References["_config"]; config.Values["CardType"] = role == 1 ? 2 : 1;
                    config.References["BuffIds"].Items.Clear(); var parameters = config.References["Params"].Items; parameters.Clear();
                    for (var parameter = 0; parameter < length; parameter++) parameters.Add(RuntimeObject.Box(parameter < 2 ? 1 + index % 3 : index % 2));
                }
                Equivalent(world); // Equal/different costs, both counter-prevention values and usable dominance.
            }
            foreach (var index in new[] { 0, 1 })
            {
                var world = new World(1, 0, 0);
                world.Cards.Items[0].References["_config"].References["Params"].Items[index] = RuntimeObject.Box("invalid integer ABI"); Failure(world);
            }
            var duplicate = new World(3, 0, 0);
            duplicate.Cards.Items[1].Visible = true; duplicate.Cards.Items[2].References["IsRelease"] = RuntimeObject.Box(false);
            duplicate.Cards.Items[2].References["com_Card"] = duplicate.Cards.Items[0].References["com_Card"];
            duplicate.Cards.Items[2] = duplicate.Cards.Items[0]; GameUi.Hit.Clear(); GameUi.Hit.Add(duplicate.Cards.Items[0]); Equivalent(duplicate);
            foreach (var transition in new[] { "stats", "hand", "disposed", "role" })
            {
                var world = new World(11, 0, 0); var input = world.Input;
                var first = world.Cards.Items[0];
                Action change = transition switch {
                    "stats" => () => world.Input = world.Input with { Hp = 1, DefenseMax = 2 },
                    "hand" => () => world.Cards.Items.RemoveAt(0),
                    "disposed" => () => world.Cards.Items[0].Disposed = true,
                    _ => () => world.Role.Values["selectedIndex"] = 2 };
                var before = Observe(world, true, change);
                world.Input = input; world.Role.Values["selectedIndex"] = 0; world.Cards.Items[0].Disposed = false;
                if (transition == "hand") world.Cards.Items.Insert(0, first);
                var after = Observe(world, false, change);
                Require(before.Reads.SequenceEqual(after.Reads), "Lifecycle read drift: " + transition);
                Require(before.Effects.SequenceEqual(after.Effects), "Lifecycle output drift: " + transition); _cases++;
            }
            Plugin.MinimalDiagnostics = null;
        }
        private static (long Bytes, double Ms) Measure(World world, bool legacy)
        {
            Capture = false; CardUi.Clear(); ReferenceCardUi.Clear();
            void Update() { if (legacy) ReferenceCardUi.Update(world.Fight, world.Ui, world.Input, world.Point); else CardUi.Update(world.Fight, world.Ui, world.Input, world.Point); }
            for (var i = 0; i < 2000; i++) Update();
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 10000; i++) Update();
            return (GC.GetAllocatedBytesForCurrentThread() - bytes, (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
        }
        private static void Main()
        {
            Cases(); var world = new World(11, 0, 0);
            var before = Measure(world, true); var after = Measure(world, false);
            Require(after.Bytes < before.Bytes, "Warm scan allocation must be lower than LINQ reference");
            var result = new { Status = "PASS", Cases = _cases, Assertions = _assertions, Scans = 10000,
                BeforeBytes = before.Bytes, AfterBytes = after.Bytes, SavedBytes = before.Bytes - after.Bytes,
                BeforeMs = before.Ms, AfterMs = after.Ms,
                Scope = "Actual linked CardUi with strict managed UI substitutes and legacy-LINQ reference; not an in-game FPS benchmark" };
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
