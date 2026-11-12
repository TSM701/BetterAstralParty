using UnityEngine;

namespace BetterAstralParty;

// Development builds only (user-requested 2026-11-10): does the real server's fight data hold the rolled card bonuses before
// both sides have chosen? The native card window reads only the ranges from it, and the tutorial's copy of the server writes
// the rolled values at card use, which cannot say what the real server sends. While diagnostics record in PvE (not replays)
// this logs the numbers whenever they change and shows nothing. Reads only, never IDs or names; runs after the PvP gate.
// ServerFight stays the only reader for any view, and only after isEnd. When a fight ends it also logs both actors' buffs
// without an icon (fightData.hidden), which no view reads, for hits the damage model misses (user-requested 2026-11-11, after
// a Berserk defender took 1 where five similar hits took 2).
internal static class FightDataProbe
{
    private const string Feature = "FightDataProbe";
    private const int MaxLines = 400;
    private static readonly bool Enabled = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private static IntPtr _logicClass, _sceneClass;
    private static float _pollAt;
    private static int _lines;
    private static string? _last;

    internal static void Tick()
    {
        if (!Enabled || _lines >= MaxLines || !Plugin.Diagnostics.IsRecording || !Compatibility.Allowed(Feature) || Time.unscaledTime < _pollAt) return;
        _pollAt = Time.unscaledTime + .1f;
        try { Poll(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex); }
    }

    private static void Poll()
    {
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (room == null || !VisibleCombatAdvisor.IsPve(room.Field("info")?.Get<int>("MapType") ?? 0)
            || logic!.Get("replay")?.Get("Session")?.Get<bool>("IsReplay") == true) return;
        var data = logic.Get("fight")?.Field("battleFightData");
        var attacker = data?.Field("attackerInfo");
        var defender = data?.Field("defenderInfo");
        if (attacker == null || defender == null) return;
        var line = $"fightData end={data!.Field("isEnd")!.Value<bool>()}; back={data.Field("fightBack")!.Value<bool>()}; attacker {Role(attacker)}; defender {Role(defender)}";
        if (line == _last) return;
        _last = line;
        Plugin.Diagnostics.Write(++_lines == MaxLines ? line + "; cap reached" : line);
        if (data.Field("isEnd")!.Value<bool>() && _lines < MaxLines) WriteHidden(attacker, defender);
    }

    private static void WriteHidden(RuntimeObject attackerInfo, RuntimeObject defenderInfo)
    {
        if (_sceneClass == IntPtr.Zero) _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
        var director = RuntimeObject.StaticField(_sceneClass, "inst")?.Field("directorManager");
        // Only the shown fight's players, as ServerFight checks.
        if (director == null || attackerInfo.Get<long>("PlayerId") != director.Field("attackerId")!.Value<long>()
            || defenderInfo.Get<long>("PlayerId") != director.Field("defendeId")!.Value<long>()) return;
        var line = $"fightData.hidden attacker={Hidden(director.Field("_attackerData"))}; defender={Hidden(director.Field("_defenderData"))}";
        Plugin.Diagnostics.Write(++_lines == MaxLines ? line + "; cap reached" : line);
    }

    // BuffContainer._buffDict holds every buff; GetShowBuffs returns the same objects that have an icon.
    private static string Hidden(RuntimeObject? player)
    {
        var container = player?.Get("buffContainer");
        var all = container?.Field("_buffDict");
        if (container == null || all == null) return "unreadable";
        var shown = new HashSet<IntPtr>();
        var list = container.Call("GetShowBuffs", player, false)?.Field("Item1");
        for (int i = 0, count = list?.Get<int>("Count") ?? 0; i < count; i++) shown.Add(list!.Call("get_Item", i)!.Pointer);
        var hidden = new List<string>();
        var iterator = all.Get("Values")!.Call("GetEnumerator")!;
        while (iterator.Call("MoveNext")!.Value<bool>())
        {
            var buff = iterator.Get("Current");
            if (buff == null || shown.Contains(buff.Pointer)) continue;
            hidden.Add($"{buff.Get<int>("BuffId")}:{buff.Get<int>("Progress")}:{buff.Get<int>("KeepRound")}:{buff.Get<int>("DelayRound")}");
        }
        return hidden.Count == 0 ? "none" : string.Join(",", hidden);
    }

    private static string Role(RuntimeObject role)
    {
        var rolled = role.Get("CardCombatBonus");
        var bonus = new List<int>();
        for (int i = 0, count = rolled?.Get<int>("Count") ?? 0; i < count; i++) bonus.Add(rolled!.Call("get_Item", i)!.Get<int>("CardBonus"));
        return $"atk={role.Get<int>("Atk")}; def={role.Get<int>("Def")}; point={role.Get<int>("Point")}; atkRange={role.Get<int>("MinAtk")}~{role.Get<int>("MaxAtk")}; "
            + $"defRange={role.Get<int>("MinDef")}~{role.Get<int>("MaxDef")}; init={role.Get<int>("InitAtk")}/{role.Get<int>("InitDef")}; "
            + $"used={role.Get("UseCards")?.Get<int>("Count") ?? 0}; bonus={string.Join("+", bonus)}; dodge={role.Get<bool>("Dodge")}; incHp={role.Get<int>("IncHp")}";
    }
}
