#nullable enable
using System;
using System.Collections.Generic;
namespace PlayerTabSkillTest {
sealed class RuntimeObject {
    public readonly Dictionary<string, object> Data = new();
    public Func<string, object?[], RuntimeObject?>? OnCall;
    public T Field<T>(string name) => (T)Data[name];
    public RuntimeObject? Call(string name, params object?[] args) => OnCall!(name, args);
}
sealed class DiagnosticsStub { public bool IsRecording = true; public readonly List<string> States = new(); public void State(string key, string value) => States.Add(key + ": " + value); }
static class Plugin { public static readonly DiagnosticsStub Diagnostics = new(); }
public static class Check {
__ASK__
__ASKED__
    static int Requests;
    static readonly List<RuntimeObject> Replies = new();
    static void Assert(bool ok, string what) { if (!ok) throw new Exception("Player tab skill regression: " + what); }
    public static void Run() {
        var data = new RuntimeObject { Data = { ["SkillCD"] = 2 } };
        var battle = new RuntimeObject();
        battle.OnCall = (name, args) => {
            Assert(args.Length == 1 && args[0] is long, "player IDs travel as Int64");
            if (name == "RequestGetHeroInfoC2S") {
                Requests++;
                var reply = new RuntimeObject { Data = { ["isCompleted"] = false, ["isTimeOut"] = false, ["errId"] = 0 } };
                Replies.Add(reply); return reply;
            }
            if (name == "GetPlayerDataById") return (long)args[0]! == 42L ? data : null;
            throw new Exception("Unexpected native call " + name);
        };
        var server = new Server(battle, 7, 1, true, 0, true);
        // First sight asks once and keeps the client count until the answer.
        Assert(Asked(server, 42, 0, 0, 0f) == 0 && Requests == 1, "first sight asks once");
        for (var i = 0; i < 50; i++) Assert(Asked(server, 42, 0, 0, 0f) == 0, "a pending request shows the client count");
        Assert(Requests == 1, "nothing moved, nothing asked");
        // The answer shows the stored SkillCD; the window's own requests refresh the same field.
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 0, 0, 0f) == 2 && Requests == 1, "the answer shows SkillCD");
        data.Data["SkillCD"] = 1;
        Assert(Asked(server, 42, 0, 0, 0f) == 1, "the latest stored answer shows");
        // A round start asks again and keeps the last answer until the reply.
        server = server with { Round = 2 };
        Assert(Asked(server, 42, 0, 0, 0f) == 1 && Requests == 2, "a round start asks");
        server = server with { Round = 3 };
        Assert(Asked(server, 42, 0, 0, 0f) == 1 && Requests == 2, "no second request while one is pending");
        // A failed reply keeps the last answer and is recorded; the round that moved meanwhile asks once.
        Replies[^1].Data["isCompleted"] = true; Replies[^1].Data["errId"] = 5;
        Assert(Asked(server, 42, 0, 0, 0f) == 1 && Requests == 3 && Plugin.Diagnostics.States.Count == 1, "a failed reply is kept and recorded");
        // A skill use moves the client count outside a round start: the count shows at once (CardLogic.TriggerSkill sets it
        // from the server's map) and asks again; the answer to that request replaces it.
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3, 0, 0f) == 3 && Requests == 4, "a skill use shows its count at once and asks");
        // Each player asks separately; a timed-out first request keeps the client count and is not retried.
        Assert(Asked(server, 43, 4, 0, 0f) == 4 && Requests == 5, "a second player asks separately");
        Replies[^1].Data["isCompleted"] = true; Replies[^1].Data["isTimeOut"] = true;
        Assert(Asked(server, 43, 4, 0, 0f) == 4 && Requests == 5 && Plugin.Diagnostics.States.Count == 2, "a timeout keeps the client count");
        // A card play moves the hand count (운명의 인도: cooldown -1 reaches only that player's client): ask now, once
        // more FollowUp seconds later, then nothing until something moves again.
        Replies[3].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3, 5, 0f) == 1 && Requests == 6, "a card play moves the hand count and asks");
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3, 5, 2.9f) == 1 && Requests == 6, "no follow-up before its time");
        Assert(Asked(server, 42, 3, 5, 3.1f) == 1 && Requests == 7, "one follow-up after a card play");
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3, 5, 60f) == 1 && Requests == 7, "nothing more without another move");
        // During a fight or once the game is ending (room not RUNNING) nothing goes out: the server answered such a request
        // with error 11001, which the game showed (2026-11-16). A move seen meanwhile asks once the room is open again.
        server = server with { Open = false, Running = false };
        Assert(Asked(server, 42, 3, 4, 61f) == 1 && Requests == 7, "no request during a fight or once the game is ending");
        Assert(Asked(server, 42, 3, 4, 70f) == 1 && Requests == 7, "no request during a fight or once the game is ending, later either");
        server = server with { Open = true, Running = true };
        Assert(Asked(server, 42, 3, 4, 71f) == 1 && Requests == 8, "the move seen while closed asks once open");
        // Anything else on the table (a land, coins, a buff) may have lowered this player's cooldown too: ask, at most every
        // Gap seconds, and not once the turns are 0 (user-requested 2026-11-16).
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3, 4, 72f) == 1 && Requests == 8, "nothing changed on the table");
        server = server with { Table = 1 };
        Assert(Asked(server, 42, 3, 4, 72.5f) == 1 && Requests == 8, "a table change waits for the gap");
        Assert(Asked(server, 42, 3, 4, 73.1f) == 1 && Requests == 9, "a table change asks after the gap");
        Replies[^1].Data["isCompleted"] = true;
        server = server with { Table = 2 };
        data.Data["SkillCD"] = 0;
        Assert(Asked(server, 42, 3, 4, 80f) == 0 && Requests == 9, "no table ask once the turns are 0");
        // The room is open for requests only Settle seconds after it was last closed (a fight, a push waiting) or the table
        // last changed: the server's last pushes as a game ends can come seconds apart (11001 on dev.21, 2026-11-17).
        Assert(Quiet(true, false, 100f), "a room open long enough is quiet");
        Assert(!Quiet(false, false, 101f) && !Quiet(true, false, 101f + Settle - .1f) && Quiet(true, false, 101f + Settle), "quiet only Settle seconds after it was closed");
        Assert(!Quiet(true, true, 110f) && !Quiet(true, false, 110f + Settle - .1f) && Quiet(true, false, 110f + Settle), "a table change restarts the wait");
        // A failed reply names how long after its request it was seen.
        data.Data["SkillCD"] = 1;
        server = server with { Table = 3 };
        Assert(Asked(server, 42, 3, 4, 90f) == 1 && Requests == 10, "a table change asks while the turns are not 0");
        Replies[^1].Data["isCompleted"] = true; Replies[^1].Data["errId"] = 11001;
        var states = Plugin.Diagnostics.States.Count;
        Assert(Asked(server, 42, 3, 4, 90.5f) == 1 && Requests == 10 && Plugin.Diagnostics.States.Count == states + 1
            && Plugin.Diagnostics.States[^1].Contains("err=11001; timeout=False; sentMs=500"), "a failed reply records how long after its request it was seen");
        // A round start asks as soon as the room runs outside a fight, quiet or not (the server ends a game instead of starting
        // a round), and the count copied from the local player's map with it is not shown (user-reported 2026-11-17: the tab
        // lagged the player info window while the bots played).
        data.Data["SkillCD"] = 2;
        server = server with { Round = 4, Open = false, Running = true };
        Assert(Asked(server, 42, 1, 4, 95f) == 2 && Requests == 11, "a round start asks at once, the copied count not shown");
        Replies[^1].Data["isCompleted"] = true;
        server = server with { Round = 5, Running = false };
        Assert(Asked(server, 42, 1, 4, 96f) == 2 && Requests == 11, "no round request during a fight");
        server = server with { Running = true };
        Assert(Asked(server, 42, 1, 4, 97f) == 2 && Requests == 12, "the round seen during a fight asks once the room runs");
        // A skill use after that request: its count shows until an answer asked for after it comes.
        Assert(Asked(server, 42, 3, 4, 98f) == 3 && Requests == 12, "a skill use shows its count while a request is pending");
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3, 4, 98.5f) == 3, "an answer asked for before the skill use does not replace its count");
        server = server with { Open = true };
        Assert(Asked(server, 42, 3, 4, 99f) == 3 && Requests == 13, "the skill use asks once the room is open");
        data.Data["SkillCD"] = 3;
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3, 4, 99.5f) == 3, "the answer asked for after the skill use replaces its count");
        data.Data["SkillCD"] = 2;
        Assert(Asked(server, 42, 3, 4, 100f) == 2, "the stored answer shows again");
        Console.WriteLine("PASS: other players' turns ask the server once per round start, count, hand or table change (and one follow-up), one request at a time and at most every gap for table changes, only into a quiet running room outside fights (a round start into any running room), show a skill use's count at once, and show its stored answer (production fragments, native substitute).");
    }
}
}
