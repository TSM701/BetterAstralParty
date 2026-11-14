using UnityEngine;

namespace BetterAstralParty;

// Development builds only (user-requested 2026-11-14): Nancy Lu's (character 120) PvE potential, STRPVENurturance 120010,
// gives "1 battle card" when her active skill targets something more than six panels away, and spares the after-fight
// discard of one battle card while her hand holds 5 or fewer. The server does both, and a skill fight sometimes had no
// attack card, so while the local player is Nancy and diagnostics record in PvE (not replays) this logs:
// - `nancy.hand` (whenever it changes): her hand by kind, from each card's EffectType as the native IsContainBattleCard reads it;
// - `nancy.skill` when her active-skill countdown rises, i.e. she used the skill;
// - `nancy.fight` as each of her fights starts: her role, both lands, and whether the game's own LandManager.CheckDistance
//   reaches the opponent within 2 and within 6 panels.
// Counts and land numbers only, never card IDs or names; reads only; runs after the PvP gate.
internal static class NancyProbe
{
    private const string Feature = "NancyProbe";
    private const int MaxLines = 300, Nancy = 120;
    private static readonly bool Enabled = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private static IntPtr _logicClass, _landClass;
    private static float _pollAt;
    private static int _lines, _cooldown = -1;
    private static string? _hand;
    private static bool _fighting;

    internal static void Tick()
    {
        if (!Enabled || _lines >= MaxLines || !Plugin.Diagnostics.IsRecording || !Compatibility.Allowed(Feature) || Time.unscaledTime < _pollAt) return;
        _pollAt = Time.unscaledTime + .25f;
        try { Poll(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex); }
    }

    private static void Poll()
    {
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        var battle = logic?.Get("battle");
        var self = room == null || battle == null || !VisibleCombatAdvisor.IsPve(room.Field("info")?.Get<int>("MapType") ?? 0)
            || logic!.Get("replay")?.Get("Session")?.Get<bool>("IsReplay") == true ? null : battle.Call("GetSelfPlayerData");
        if (self == null || self.Field("player")?.Field("characterConfig")?.Field("Id")?.Value<int>() != Nancy) { Reset(); return; }
        var round = room!.Get<int>("Round");
        if (Hand(self) is { } hand && hand != _hand) { _hand = hand; Write($"nancy.hand {hand}; round={round}"); }
        // The countdown (0 = ready) rises when the skill is used; a new round lowers it.
        var cooldown = self.Get("Property")?.Field("activeSkillCD")?.Get<int>("Value") ?? -1;
        if (_cooldown >= 0 && cooldown > _cooldown) Write($"nancy.skill used; cooldown={cooldown}; {_hand}; round={round}");
        _cooldown = cooldown;
        var fight = logic!.Get("fight");
        var fighting = fight?.Field<bool>("fightStatus") == true;
        if (fighting && !_fighting && self.Field("player")?.Get<long>("Id") is { } id)
        {
            var data = fight!.Field("battleFightData");
            var attacker = data?.Field("attackerInfo")?.Get<long>("PlayerId"); var defender = data?.Field("defenderInfo")?.Get<long>("PlayerId");
            var role = attacker == id ? "attacker" : defender == id ? "defender" : null;
            if (role != null)
            {
                var opponent = battle!.Call("GetPlayerDataById", role == "attacker" ? defender ?? 0 : attacker ?? 0);
                Write($"nancy.fight role={role}; opponentType={opponent?.Get("characterType")?.Value<int>()}; {Distance(self, opponent)}; {_hand}; round={round}");
            }
        }
        _fighting = fighting;
    }

    private static string? Hand(RuntimeObject self)
    {
        var cards = self.Get("cardContainer")?.Field("HandCards");
        if (cards == null) return null;
        int attack = 0, defense = 0, other = 0, count = cards.Get<int>("Count");
        for (var i = 0; i < count; i++)
        {
            var type = cards.Call("get_Item", i)?.Get("Config")?.Get<int>("EffectType") ?? -1;
            if (type == 1) attack++; else if (type == 2) defense++; else other++;
        }
        return $"attack={attack}; defense={defense}; effect={other}; cards={count}";
    }

    private static string Distance(RuntimeObject self, RuntimeObject? opponent)
    {
        static int? Land(RuntimeObject? actor) => actor?.Field("CharacterInst")?.Get("standLand")?.Get<int>("Id");
        if (Land(self) is not { } from || Land(opponent) is not { } to) return "lands=unread";
        if (_landClass == IntPtr.Zero) _landClass = RuntimeObject.FindClass("Core.Unit", "LandManager");
        var lands = RuntimeObject.StaticField(_landClass, "_inst");
        bool? Within(int panels) => from == to ? true : lands?.Call("CheckDistance", from, to, panels, 0)?.Value<bool>();
        return $"land={from}; opponentLand={to}; within2={Within(2)}; within6={Within(6)}";
    }

    private static void Reset()
    {
        _hand = null; _cooldown = -1; _fighting = false;
    }

    private static void Write(string line)
    {
        if (_lines >= MaxLines) return;
        Plugin.Diagnostics.Write(++_lines == MaxLines ? line + "; cap reached" : line);
    }
}
