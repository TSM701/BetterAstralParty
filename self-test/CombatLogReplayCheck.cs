using BetterAstralParty;

internal static class CombatLogReplayCheck
{
    internal static void Run()
    {
        // 2026-10-02 session ed7103a231be: scalar public inputs/observed HP only.
        // Combats 3/96 are failed dodges: native Dice_Show publishes a zero total
        // and resets the icon. The old diagnostic's dodge=False was not the choice.
        var samples = new (int Hp, int Attack, int Defense, int Die, bool Dodge, int Bonus, int Observed, bool MonsterDefeat)[] {
            (12, 6, 4, 5, false, 0, 10, false), // 1
            (11, 9, 3, 6, false, 0, 5, false), // 2
            (10, 2, 0, 1, true, 0, 8, false), // 3
            (23, 4, 5, 1, false, 0, 22, false), // 4
            (9, 8, 7, 3, false, 0, 8, false), // 5
            (3, 6, 4, 5, false, 0, 1, false), // 6
            (1, 3, 2, 2, false, 0, 0, false), // 7
            (10, 7, 3, 5, false, 0, 6, false), // 8
            (5, 4, 5, 2, false, 0, 4, false), // 9
            (9, 7, 4, 6, false, 0, 6, false), // 10
            (16, 10, 7, 3, false, 0, 13, false), // 11
            (10, 1, 5, 1, true, 0, 10, false), // 12
            (6, 5, 6, 4, false, 0, 5, false), // 13
            (10, 8, 17, 1, false, 0, 9, false), // 14
            (10, 1, 2, 1, true, 0, 10, false), // 15
            (9, 8, 4, 3, false, 0, 5, false), // 16
            (10, 9, 8, 4, false, 0, 9, false), // 17
            (5, 10, 6, 6, false, 0, 1, false), // 18
            (12, 9, 5, 4, false, 0, 8, false), // 19
            (1, 5, 5, 1, false, 0, 0, false), // 20
            (4, 12, 4, 5, false, 0, 0, false), // 22
            (8, 11, 3, 4, false, 0, 1, true), // 24
            (10, 8, 7, 2, false, 0, 9, false), // 25
            (9, 9, 11, 4, false, 0, 8, false), // 26
            (9, 8, 7, 3, false, 0, 8, false), // 27
            (21, 2, 6, 1, false, 0, 20, false), // 28
            (8, 4, 3, 3, false, 0, 7, false), // 29
            (20, 5, 5, 4, false, 0, 19, false), // 32
            (7, 4, 6, 3, false, 0, 6, false), // 33
            (3, 7, 5, 6, false, 0, 1, false), // 35
            (21, 3, 5, 1, false, 0, 20, false), // 38
            (1, 4, 5, 3, false, 0, 0, false), // 40
            (11, 9, 7, 5, false, 0, 9, false), // 41
            (8, 7, 5, 3, false, 0, 6, false), // 42
            (9, 1, 3, 1, true, 0, 9, false), // 43
            (6, 6, 3, 1, false, 0, 3, false), // 44
            (9, 8, 3, 3, false, 0, 4, false), // 45
            (6, 3, 5, 2, false, 0, 5, false), // 47
            (20, 3, 6, 2, false, 0, 19, false), // 48
            (13, 15, 3, 6, false, 0, 1, false), // 51
            (9, 10, 10, 4, false, 0, 8, false), // 52
            (19, 25, 4, 2, false, 0, 0, false), // 53
            (1, 10, 5, 2, false, 0, 0, false), // 54
            (22, 12, 6, 3, false, 0, 16, false), // 55
            (11, 9, 2, 3, false, 0, 4, false), // 56
            (8, 10, 7, 4, false, 0, 5, false), // 58
            (5, 8, 10, 2, false, 0, 4, false), // 59
            (4, 7, 4, 2, false, 0, 1, false), // 60
            (18, 11, 8, 6, false, 1, 14, false), // 63
            (8, 6, 9, 1, false, 0, 7, false), // 64
            (19, 7, 3, 4, false, 0, 15, false), // 65
            (16, 5, 4, 2, false, 0, 15, false), // 66
            (7, 10, 9, 4, false, 0, 6, false), // 67
            (18, 8, 5, 3, false, 0, 15, false), // 68
            (6, 10, 11, 4, false, 0, 5, false), // 69
            (7, 9, 12, 5, false, 0, 6, false), // 72
            (6, 9, 10, 3, false, 0, 5, false), // 73
            (5, 9, 14, 3, false, 0, 4, false), // 74
            (21, 4, 6, 3, false, 0, 20, false), // 76
            (15, 6, 7, 5, false, 0, 14, false), // 77
            (14, 15, 5, 2, false, 0, 4, false), // 79
            (7, 8, 6, 3, false, 0, 5, false), // 80
            (5, 9, 8, 4, false, 0, 4, false), // 81
            (4, 11, 9, 3, false, 0, 2, false), // 82
            (5, 11, 7, 5, false, 0, 1, false), // 83
            (2, 10, 13, 4, false, 0, 1, false), // 84
            (4, 3, 6, 3, true, 0, 4, false), // 85
            (4, 17, 7, 5, false, 0, 0, false), // 86
            (15, 32, 5, 1, false, 0, 0, false), // 87
            (6, 16, 8, 4, false, 0, 0, false), // 88
            (14, 17, 8, 3, false, 0, 5, false), // 90
            (1, 15, 5, 3, false, 0, 0, false), // 91
            (16, 15, 4, 1, false, 0, 5, false), // 92
            (4, 11, 10, 4, false, 0, 3, false), // 93
            (6, 15, 6, 3, false, 0, 0, false), // 94
            (5, 2, 4, 2, true, 0, 5, false), // 95
            (5, 11, 0, 3, true, 0, 1, true), // 96
            (23, 22, 7, 3, false, 0, 8, false), // 98
            (8, 18, 6, 1, false, 0, 0, false), // 99
            (7, 22, 6, 5, false, 0, 0, false), // 100
            (3, 19, 4, 1, false, 1, 0, false), // 101
            (10, 23, 8, 5, false, 0, 0, false), // 102
            (152, 31, 10, 3, false, 0, 131, false), // 103
            (3, 1, 6, 1, true, 0, 3, false), // 104
            (122, 32, 9, 6, false, 0, 99, false), // 105
            (23, 32, 5, 2, false, 0, 0, false), // 108
            (23, 31, 7, 1, false, 1, 0, false), // 109
            (70, 51, 9, 4, false, 0, 28, false), // 110
            (6, 3, 4, 3, true, 1, 6, false), // 111
            (9, 10, 10, 5, false, 0, 8, false), // 112
            (14, 13, 12, 5, false, 0, 13, false), // 113
        };
        var censored = 0;
        foreach (var s in samples)
        {
            var damage = CombatAdvisor.FinalDamage(s.Hp, s.Attack, s.Defense, s.Dodge, s.Die, new(s.Bonus));
            var remaining = Math.Max(0, s.Hp - damage);
            var displayed = s.MonsterDefeat ? Math.Max(1, remaining) : remaining;
            if (displayed != s.Observed) throw new Exception("Recorded combat HP: " + s);
            if (s.Observed == 0 || s.MonsterDefeat) censored++;
            else if (damage != s.Hp - s.Observed) throw new Exception("Recorded nonlethal damage: " + s);
            if (!s.Dodge && CardDiceLayout.Status(new(s.Die, 1), (damage, damage), s.Hp, false)
                != (remaining == 0 ? "확정 KO" : "KO 불가"))
                throw new Exception("Resolved KO must follow arithmetic, not native defeat HP1: " + s);
        }
        if (samples.Length != 91 || censored != 19) throw new Exception("Combat replay coverage changed");
        Console.WriteLine("2026-10-02 combat replays: 72 exact HP-loss checks + 19 lethal/display-censored checks passed; overkill unverified.");
        // 2026-10-10 local.16: current/previous logs, public final labels and observed HP only.
        foreach (var s in new[] {
            (Hp:3, Attack:12, Defense:3, Die:2, Dodge:false, Bonus:0, Observed:0),
            (Hp:7, Attack:18, Defense:6, Die:1, Dodge:false, Bonus:0, Observed:0),
            (Hp:7, Attack:1, Defense:4, Die:1, Dodge:true, Bonus:2, Observed:7),
            (Hp:66, Attack:45, Defense:5, Die:6, Dodge:false, Bonus:1, Observed:25),
            (Hp:25, Attack:22, Defense:8, Die:3, Dodge:false, Bonus:1, Observed:10),
            (Hp:11, Attack:15, Defense:8, Die:4, Dodge:false, Bonus:0, Observed:4),
            (Hp:10, Attack:33, Defense:8, Die:4, Dodge:false, Bonus:1, Observed:0) })
        {
            var damage = CombatAdvisor.FinalDamage(s.Hp, s.Attack, s.Defense, s.Dodge, s.Die, new(s.Bonus));
            if (Math.Max(0, s.Hp - damage) != s.Observed) throw new Exception("2026-10-10 public combat HP: " + s);
            if (!s.Dodge)
            {
                var status = CardDiceLayout.Status(new(s.Die, 1), (damage, damage), s.Hp);
                if (status != (s.Observed == 0 ? "확정 KO" : "KO 불가")
                    || CardDiceLayout.Content(new(s.Die, 1), s.Die, 1, unconditional:true) != (0, "", "—"))
                    throw new Exception("2026-10-10 final KO must replace defender conditions: " + s);
            }
        }
        Console.WriteLine("2026-10-10 combat replays: 7 public HP/result checks passed; lethal overkill unverified.");
    }
}
