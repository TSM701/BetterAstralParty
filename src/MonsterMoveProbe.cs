using UnityEngine;

namespace BetterAstralParty;

// Development builds only (user-requested 2026-11-05): learn how monsters move before any reach preview is built on it.
// Monsters roll move dice on their own turn; the server's ThrowDiceS2C sets Character.canStep (ResetStep(MovePoint)) before
// the dice show, and MoveS2C walks the path. While diagnostics record in PvE (not replays), this writes the board graph once
// per match and, per monster move: `monsterMove.dice` when the held step count rises while idle (start land, native
// next-land choices, public move modifier and shown effects, where heroes and monsters stand), `monsterMove.start` when a
// move begins without such a rise, and `monsterMove.end` with every land passed (steps then held) once it has been still
// for 2.5 s outside a fight. Polls every 0.1 s, so a land crossed faster can be missing from the path (the graph fills it).
// Reads only, never IDs or names; runs after the PvP gate.
internal static class MonsterMoveProbe
{
    private const string Feature = "MonsterMoveProbe";
    private const int MaxLines = 800;
    private static readonly bool Enabled = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private static readonly Dictionary<IntPtr, Unit> Units = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static readonly List<IntPtr> Stale = new();
    private static IntPtr _logicClass, _landClass, _room;
    private static float _pollAt;
    private static int _lines, _sequence;
    private static bool _graphWritten;

    private sealed class Unit
    {
        internal int Number, Land = -1, Step;
        internal bool Moving, Rose, Fought;
        internal float ChangedAt;
        internal readonly List<string> Path = new();
    }

    internal static void Tick()
    {
        if (!Enabled || !Plugin.Diagnostics.IsRecording || !Compatibility.Allowed(Feature) || Time.unscaledTime < _pollAt) return;
        _pollAt = Time.unscaledTime + .1f;
        try { Poll(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex); }
    }

    private static void Poll()
    {
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        var battle = logic?.Get("battle");
        var players = battle?.Field("PlayerDatas");
        if (room == null || players == null || !VisibleCombatAdvisor.IsPve(room.Field("info")?.Get<int>("MapType") ?? 0)
            || logic!.Get("replay")?.Get("Session")?.Get<bool>("IsReplay") == true) { Reset(IntPtr.Zero); return; }
        if (room.Pointer != _room) Reset(room.Pointer);
        if (!_graphWritten) _graphWritten = WriteGraph(room);
        var fighting = logic.Get("fight")?.Field("fightStatus")?.Value<bool>() == true;
        var now = Time.unscaledTime;
        Seen.Clear();
        for (int i = 0, count = players.Get<int>("Count"); i < count; i++)
        {
            var player = players.Call("get_Item", i);
            if (player == null || player.Get<int>("characterType") != 2) continue;
            var character = player.Field("CharacterInst");
            var land = character?.Get("standLand");
            if (character == null || land == null) continue;
            Seen.Add(character.Pointer);
            if (!Units.TryGetValue(character.Pointer, out var unit)) Units.Add(character.Pointer, unit = new Unit { Number = ++_sequence });
            int id = land.Get<int>("Id"), step = character.Get<int>("canStep");
            if (unit.Land < 0) { unit.Land = id; unit.Step = step; continue; }
            if (!unit.Moving && step > unit.Step)
            {
                unit.Rose = true;
                Write(Snapshot("dice", unit, room, battle!, players, player, character, land, step));
            }
            if (id != unit.Land)
            {
                if (!unit.Moving)
                {
                    if (!unit.Rose) Write($"monsterMove.start m{unit.Number}; hero={Hero(player)}; round={room.Get<int>("Round")}; ownTurn={OwnTurn(battle!, player)}; from={unit.Land}; step={unit.Step}; noRise");
                    unit.Moving = true; unit.Fought = false; unit.Path.Clear();
                }
                unit.Path.Add(id + "(" + step + ")");
                unit.Land = id; unit.ChangedAt = now;
            }
            if (unit.Moving && fighting) { unit.Fought = true; unit.ChangedAt = now; }
            if (unit.Moving && now - unit.ChangedAt > 2.5f)
            {
                Write($"monsterMove.end m{unit.Number}; path={string.Join(",", unit.Path)}; stepLeft={step}; fought={unit.Fought}; heroes={Lands(players, 1)}");
                unit.Moving = unit.Rose = false;
            }
            unit.Step = step;
        }
        Stale.Clear();
        foreach (var key in Units.Keys) if (!Seen.Contains(key)) Stale.Add(key);
        foreach (var key in Stale)
        {
            var unit = Units[key];
            if (unit.Moving) Write($"monsterMove.end m{unit.Number}; path={string.Join(",", unit.Path)}; removed; fought={unit.Fought}");
            Units.Remove(key);
        }
    }

    private static string Snapshot(string kind, Unit unit, RuntimeObject room, RuntimeObject battle, RuntimeObject players,
        RuntimeObject player, RuntimeObject character, RuntimeObject land, int step)
    {
        var from = character.Get<int>("fromLandId");
        var choices = new List<int>();
        var list = land.Call("CanSelectedLandId", from);
        for (int i = 0, count = list?.Get<int>("Count") ?? 0; i < count; i++) choices.Add(list!.Call("get_Item", i)!.Value<int>());
        var effects = new List<string>();
        var shown = player.Get("buffContainer")?.Call("GetShowBuffs", player, false);
        var buffs = shown?.Field("Item1"); var properties = shown?.Field("Item2");
        for (int i = 0, count = properties?.Get<int>("Count") ?? 0; i < count; i++)
        {
            var data = properties!.Call("get_Item", i)!;
            effects.Add(data.Field("buffId")!.Value<int>() + "x" + data.Field("property")!.Get<int>("Value"));
        }
        for (int i = 0, count = buffs?.Get<int>("Count") ?? 0; i < count; i++)
        {
            var data = buffs!.Call("get_Item", i)!;
            effects.Add(data.Get<int>("BuffId") + "x" + data.Get<int>("Progress") + "r" + data.Get<int>("KeepRound"));
        }
        return $"monsterMove.{kind} m{unit.Number}; hero={Hero(player)}; round={room.Get<int>("Round")}; ownTurn={OwnTurn(battle, player)}; land={unit.Land}; from={from}; choices={string.Join("/", choices)}; step={step}; extra={player.Get("Property")?.Get<int>("ExtraMovePoint")}; effects={string.Join(",", effects)}; heroes={Lands(players, 1)}; monsters={Lands(players, 2)}";
    }

    private static int Hero(RuntimeObject player) => player.Field("player")?.Get("Hero")?.Get<int>("HeroId") ?? 0;

    private static bool OwnTurn(RuntimeObject battle, RuntimeObject player) =>
        player.Field("player") is { } owner && battle.Get<long>("curPlayerId") == owner.Get<long>("Id");

    private static string Lands(RuntimeObject players, int type)
    {
        var lands = new List<int>();
        for (int i = 0, count = players.Get<int>("Count"); i < count; i++)
            if (players.Call("get_Item", i) is { } player && player.Get<int>("characterType") == type
                && player.Field("CharacterInst")?.Get("standLand") is { } land) lands.Add(land.Get<int>("Id"));
        return string.Join("/", lands);
    }

    // Every land once per match: id:type:adjacent ids, 25 lands a line.
    private static bool WriteGraph(RuntimeObject room)
    {
        if (_landClass == IntPtr.Zero) _landClass = RuntimeObject.FindClass("Core.Unit", "LandManager");
        var nodes = RuntimeObject.StaticField(_landClass, "_inst")?.Field("NodeDict");
        var count = nodes?.Get<int>("Count") ?? 0;
        if (count == 0) return false;
        var entries = new List<string>(count);
        var iterator = nodes!.Get("Values")!.Call("GetEnumerator")!;
        try
        {
            while (iterator.Call("MoveNext")!.Value<bool>())
            {
                var land = iterator.Get("Current");
                if (land == null) continue;
                var adjacent = land.Get("AdjacencyLandIds");
                var ids = new List<int>();
                for (int i = 0, size = adjacent?.Get<int>("Count") ?? 0; i < size; i++) ids.Add(adjacent!.Call("get_Item", i)!.Value<int>());
                entries.Add(land.Get<int>("Id") + ":" + land.Get<int>("LandType") + ":" + string.Join("/", ids));
            }
        }
        finally { iterator.Call("Dispose"); }
        Write($"monsterMove.map map={room.Get<int>("MapId")}; difficulty={room.Get<int>("MapDifficultyId")}; lands={entries.Count}");
        for (var i = 0; i < entries.Count; i += 25)
            Write($"monsterMove.graph part={i / 25 + 1}; {string.Join(" ", entries.Skip(i).Take(25))}");
        return true;
    }

    private static void Reset(IntPtr room)
    {
        if (_room == room) return;
        Units.Clear();
        _room = room; _graphWritten = false;
    }

    private static void Write(string line)
    {
        if (_lines >= MaxLines) return;
        Plugin.Diagnostics.Write(++_lines == MaxLines ? line + "; cap reached" : line);
    }
}
