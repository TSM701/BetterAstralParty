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
        var server = new Server(battle, 7, 1);
        // First sight asks once and keeps the client count until the answer.
        Assert(Asked(server, 42, 0) == 0 && Requests == 1, "first sight asks once");
        for (var i = 0; i < 50; i++) Assert(Asked(server, 42, 0) == 0, "a pending request shows the client count");
        Assert(Requests == 1, "nothing moved, nothing asked");
        // The answer shows the stored SkillCD; the window's own requests refresh the same field.
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 0) == 2 && Requests == 1, "the answer shows SkillCD");
        data.Data["SkillCD"] = 1;
        Assert(Asked(server, 42, 0) == 1, "the latest stored answer shows");
        // A round start asks again and keeps the last answer until the reply.
        server = server with { Round = 2 };
        Assert(Asked(server, 42, 0) == 1 && Requests == 2, "a round start asks");
        server = server with { Round = 3 };
        Assert(Asked(server, 42, 0) == 1 && Requests == 2, "no second request while one is pending");
        // A failed reply keeps the last answer and is recorded; the round that moved meanwhile asks once.
        Replies[^1].Data["isCompleted"] = true; Replies[^1].Data["errId"] = 5;
        Assert(Asked(server, 42, 0) == 1 && Requests == 3 && Plugin.Diagnostics.States.Count == 1, "a failed reply is kept and recorded");
        // A skill use moves the client count: ask again.
        Replies[^1].Data["isCompleted"] = true;
        Assert(Asked(server, 42, 3) == 1 && Requests == 4, "a skill use asks");
        // Each player asks separately; a timed-out first request keeps the client count and is not retried.
        Assert(Asked(server, 43, 4) == 4 && Requests == 5, "a second player asks separately");
        Replies[^1].Data["isCompleted"] = true; Replies[^1].Data["isTimeOut"] = true;
        Assert(Asked(server, 43, 4) == 4 && Requests == 5 && Plugin.Diagnostics.States.Count == 2, "a timeout keeps the client count");
        Console.WriteLine("PASS: other players' turns ask the server once per round start or count change, one request at a time, and show its stored answer (production fragments, native substitute).");
    }
}
}
