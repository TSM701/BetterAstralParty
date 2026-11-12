namespace BetterAstralParty;

// The decided fight (user-decided 2026-11-08). Once both sides have chosen, the server's BattleS2C sets isEnd and starts
// BattleShowDirector.PlayBattleResult, and FightLogic.battleFightData then holds the dice (Point), the totals (Atk, Def),
// the dodge and the defender's HP change (IncHp: the game shows it as the hit value, and VictimDead is HP + IncHp <= 0).
// Never read before isEnd: until then the fight is still being chosen. The one exception is Rolled below.
internal readonly record struct ServerFight(int AttackDie, int DefenseDie, int Attack, int Defense, bool Dodge, int HpChange)
{
    private const int PowerfulAttack = 10010;
    // Session breaker: a failed read leaves the views on their pre-choice bounds; advice and dice keep working.
    private static bool _failed;

    internal static ServerFight? Decided(RuntimeObject? director)
    {
        if (_failed || director == null) return null;
        try
        {
            if (Shown(director) is not { } fight || !fight.Data.Field("isEnd")!.Value<bool>()) return null;
            return new(fight.Attacker.Get<int>("Point"), fight.Defender.Get<int>("Point"), fight.Attacker.Get<int>("Atk"),
                fight.Defender.Get<int>("Def"), fight.Defender.Get<bool>("Dodge"), fight.Defender.Get<int>("IncHp"));
        }
        catch (Exception ex) { Fail(ex); return null; }
    }

    // Rolled card values (user-decided 2026-11-11): the server sends every rolled card bonus the moment the card is used
    // (a non-end BattleS2C), before either die. Atk then holds the attacker's value plus its thrown die (Point), and Def the
    // defender's value plus its die once the fight ends; in the logged match of 2026-11-10 that value equalled the final
    // total minus the die in all 21 attacks and 20 defenses. Callers use it only for a viewer with nothing left to choose.
    // Powerful Attack (card 10010) boosts ATK + die once the die is thrown (2026-11-11: Atk 24 with Point 0, then 41 with
    // Point 3), so a boosted attacker's value is the one whose boosted total is Atk, or none while the rule leaves several.
    internal static (int? Attack, int? Defense)? Rolled(RuntimeObject? director)
    {
        if (_failed || director == null) return null;
        try
        {
            if (Shown(director) is not { } fight) return null;
            var atk = fight.Attacker.Get<int>("Atk");
            var point = fight.Attacker.Get<int>("Point");
            var boost = AttackerBoost(fight.Attacker);
            return (boost.Count == 0 || point == 0 ? atk - point : boost.Before(atk) - point,
                fight.Defender.Get<int>("Def") - fight.Defender.Get<int>("Point"));
        }
        catch (Exception ex) { Fail(ex); return null; }
    }

    // The shown fight's attacker's Powerful Attack cards. CardCombatBonus lists every used attack or defense card with its
    // rolled value (TutorialLogic.CalFightCardValue; UseCards holds hand-card GUIDs, not card IDs). The cards show publicly
    // only in the result show (Dice_Show), so callers pass this only to the attacker itself and to spectators.
    internal static FinalBoost Boost(RuntimeObject? director)
    {
        if (_failed || director == null) return default;
        try { return Shown(director) is { } fight ? AttackerBoost(fight.Attacker) : default; }
        catch (Exception ex) { Fail(ex); return default; }
    }

    private static FinalBoost AttackerBoost(RuntimeObject attacker)
    {
        var bonus = attacker.Get("CardCombatBonus");
        var count = 0;
        for (int i = 0, total = bonus?.Get<int>("Count") ?? 0; i < total; i++)
            if (bonus!.Call("get_Item", i)!.Get<int>("CardId") == PowerfulAttack) count++;
        // The live card's percent (Params [ATK min, max, final percent]); an unreadable one leaves the boost invalid.
        return count == 0 ? default : new(PublicCombatEffects.Parameter("Card", PowerfulAttack, 2, 3, 0, 0) ?? 0, count);
    }

    // ActiveBattlePlatform binds the shown fight's players; a counterattack is its own battle with its own data.
    private static (RuntimeObject Data, RuntimeObject Attacker, RuntimeObject Defender)? Shown(RuntimeObject director)
    {
        var data = director.Get("fightData");
        var attacker = data?.Field("attackerInfo");
        var defender = data?.Field("defenderInfo");
        if (data == null || attacker == null || defender == null
            || attacker.Get<long>("PlayerId") != director.Field("attackerId")!.Value<long>()
            || defender.Get<long>("PlayerId") != director.Field("defendeId")!.Value<long>()) return null;
        return (data, attacker, defender);
    }

    private static void Fail(Exception ex)
    {
        _failed = true;
        Plugin.Diagnostics.Error("ServerFight", ex);
    }
}
