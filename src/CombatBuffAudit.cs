namespace BetterAstralParty;

// Public (GetShowBuffs) buff IDs the combat model accounts for. Any other shown buff on either combatant is
// reported as an effect the calculation does not count, so new or unaudited content is never silently zero.
internal static class CombatBuffAudit
{
    // Handled by PublicCombatEffects: incoming adjustments, caps, immunity, marks, dice rules and Mind.
    internal static readonly HashSet<int> Modeled = new()
    {
        10006, 10012, 1071101, 1071102, 1101101, 1140101, 1140102, 1250101, 1250201, 1261101, 1291202,
        2000801, 4000301, 4000401, 5006001, 10141101, 10261101, 10331101, 10531201, 10551101, 10591101, 10671302,
    };

    // Description audit (2026-10-21) of the 159 shown buffs in the 2026-09-26 table snapshot: the original STRBuff
    // text changes only public ATK/DEF/max HP (already in the native totals), movement, coins, cards, healing or
    // damage outside the combat hit, combat avoidance, or a progress count. Counter availability (10008) is read
    // separately by the encounter advice. Buffs added by later game updates are not here until audited.
    // 2026-10-23 audit of the buffs new in the 2026-10-15 tables (ATK per stack, DEF/move, move, cost/move/cards):
    // 3081104, 5008702, 8000101, 10711102, 10711103, 10721101, 10741101; combat-audit replays agree (0 mismatches).
    internal static readonly HashSet<int> Neutral = new()
    {
        10004, 10005, 10007, 10008, 10011, 10013, 10014, 201002, 201102, 210102,
        1030101, 1030201, 1031101, 1031103, 1041101, 1041201, 1081201, 1085001, 1090101, 1091301,
        1120101, 1120102, 1120301, 1131201, 1131202, 1150101, 1151201, 1171101, 1201202, 1221202,
        1230101, 1230201, 1261201, 1261203, 1261204, 1270101, 1270201, 1280101, 1280201, 1280202,
        1281102, 1290101, 1290201, 2000601, 2001901, 2002501, 2100401, 2100501, 2101501, 3000201,
        3000601, 3001801, 3020201, 3021101, 3021402, 3031102, 3040201, 3040202, 3041101, 3041201,
        3050101, 3051201, 3051203, 3051302, 3061101, 3061201, 5002901, 5003501, 5003601, 5003701,
        5005401, 5006101, 5006301, 5006501, 5006701, 5006801, 5007101, 5007102, 5008301, 5008401,
        6010602, 6010603, 6015102, 6015202, 6015203, 6015204, 6015205, 6015702, 10081102, 10091202,
        10131101, 10141301, 10150101, 10170101, 10171101, 10181101, 10250101, 10271101, 10410101, 10441101,
        10470101, 10490101, 10570101, 10581101, 10640101, 10701102, 20271101,
        3081104, 5008702, 8000101, 10711102, 10711103, 10721101, 10741101,
    };

    internal static bool Counted(int id) => Modeled.Contains(id) || Neutral.Contains(id);
}
