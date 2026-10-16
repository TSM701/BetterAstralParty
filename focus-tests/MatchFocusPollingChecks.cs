using BetterAstralParty;

internal static class MatchFocusPollingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var manager = new RuntimeObject("Manager");
        var network = new RuntimeObject("Network");
        var match = new RuntimeObject("Match");
        var data = new RuntimeObject("MatchData");
        var room = new RuntimeObject("Room");
        var controller = new RuntimeObject("Controller");
        var local = new RuntimeObject("LocalRoom");
        var info = new RuntimeObject("Info");
        manager.Values["match"] = match; manager.Values["room"] = room; manager.Values["connectRoomId"] = 0L;
        match.Values["matchData"] = data; room.Values["roomController"] = controller;
        controller.Values["localRoom"] = local; local.Values["info"] = info;
        network.Values["IsConnected"] = true; network.Values["_Reconnecting"] = false;
        data.Values["TeamId"] = 100L; data.Values["MatchStatus"] = 2;
        controller.Values["roomStateType"] = 0;
        info.Values["Id"] = 1000L; info.Values["IsMatchRoom"] = true; info.Values["MapType"] = 4;
        var window = new WindowFake();
        void Tick() => MatchFocus.Tick(window);
        void Queue() { window.Foreground = false; controller.Values["roomStateType"] = 0; Tick(); }
        void Found(long id, int map = 4, int state = 2, bool isMatch = true)
        {
            info.Values["Id"] = id; info.Values["MapType"] = map; info.Values["IsMatchRoom"] = isMatch;
            controller.Values["roomStateType"] = state; Tick();
        }
        foreach (var boundary in new[] { "off", "missing" })
        {
            window.AcceptTopmostRestore = false;
            Plugin.MatchFocus.Value = boundary != "off";
            var reads = RuntimeObject.Reads.Count; var cleanup = window.TopmostRestores;
            Tick();
            check(window.TopmostRestores == cleanup + 1 && RuntimeObject.Reads.Count == reads && window.Calls == 0,
                "owned topmost cleanup precedes disabled/missing-data guard: " + boundary);
            check(Compatibility.Allowed(MatchFocus.Feature), "refused topmost rollback is pending, not a new feature failure: " + boundary);
        }
        window.AcceptTopmostRestore = true; Plugin.MatchFocus.Value = true;
        // Missing late-loaded types/readers do not permanently trip the feature breaker.
        RuntimeObject.ClassesAvailable = false; Tick();
        check(Compatibility.Allowed(MatchFocus.Feature) && window.Calls == 0, "late classes wait safely");
        RuntimeObject.ClassesAvailable = true; Tick();
        RuntimeObject.Manager = manager; RuntimeObject.Network = network;
        Found(1000);
        check(window.Calls == 0, "startup in existing choosing match never focuses");
        Queue(); Found(1000);
        check(window.Calls == 0, "existing room identity stays consumed");
        Queue(); Found(1001);
        check(window.Activations == 1, "observed queue to fresh PvE choosing room activates");
        var calls = window.Calls;
        for (var repeat = 0; repeat < 100; repeat++) Tick();
        check(window.Calls == calls, "polling same match is silent without OS retries");
        Plugin.MatchFocus.Value = false; Queue(); Found(1002);
        Plugin.MatchFocus.Value = true; Queue(); Found(1002);
        check(window.Activations == 1, "OFF/ON cannot replay an observed disabled room");
        foreach (var map in new[] { -1, 0, 1, 2, 3, 5, 7, 8, 11, 13, int.MaxValue })
        {
            Queue(); Found(2000 + map, map);
            check(window.Activations == 1, "PvP/unknown map rejected: " + map);
        }
        foreach (var map in new[] { 4, 6, 9, 10, 12 })
        {
            Queue(); Found(3000 + map, map);
        }
        check(window.Activations == 6, "all existing verified PvE modes accepted");
        Queue(); Found(4000, isMatch: false); Queue(); Found(4001, state: 1); Found(4001);
        check(window.Activations == 6, "unobserved custom CHOICE and automatic WAIT never later replay as success");
        foreach (var state in new[] { 0, 1, 3, 4, 5, 10, 15, 20, 25, 30, -1 })
        {
            Queue(); Found(5000 + state, state: state);
        }
        check(window.Activations == 6, "only controller CHOICE accepted, not protocol enum values");
        data.Values["TeamId"] = 0L; Queue(); Found(6000);
        data.Values["TeamId"] = 100L; data.Values["MatchStatus"] = 1; Queue(); Found(6001);
        data.Values["MatchStatus"] = 2; Queue(); data.Values["MatchStatus"] = 1; Queue(); Found(6002);
        check(window.Activations == 6, "disposed/stale/cancelled queue cannot arm");
        data.Values["MatchStatus"] = 2;
        foreach (var reconnect in new[] { "disconnected", "reconnecting", "sync" })
        {
            Queue();
            if (reconnect == "disconnected") network.Values["IsConnected"] = false;
            if (reconnect == "reconnecting") network.Values["_Reconnecting"] = true;
            if (reconnect == "sync") manager.Values["connectRoomId"] = 7000L;
            Found(7000 + Array.IndexOf(new[] { "disconnected", "reconnecting", "sync" }, reconnect));
            network.Values["IsConnected"] = true; network.Values["_Reconnecting"] = false; manager.Values["connectRoomId"] = 0L;
            Tick();
        }
        check(window.Activations == 6, "connection/sync transitions consume instead of replaying matches");
        Queue(); RuntimeObject.Network = null; Tick(); RuntimeObject.Network = network; Found(8000);
        Queue(); controller.Values["localRoom"] = null; Found(8001); controller.Values["localRoom"] = local; Tick();
        check(window.Activations == 6, "missing readers invalidate pending queue rather than delayed focus");
        Queue(); Found(8002);
        check(window.Activations == 7, "recovers on next genuine queue after temporary missing readers");
        Queue(); data.Values["MatchStatus"] = 3; Queue(); Found(8100);
        check(window.Activations == 8, "Playing notify before room preserves already observed Matching transition");
        Queue(); Found(8101);
        check(window.Activations == 8, "Playing alone cannot arm a startup/reconnected session");
        data.Values["MatchStatus"] = 2;
        check(RuntimeObject.Reads.All(read => !read.Contains("Player", StringComparison.Ordinal) && !read.Contains("Name", StringComparison.Ordinal)), "no player/card/private data access");
        foreach (var boundary in new[] { "off", "reconnect", "missing", "pvp", "new-room" })
        {
            Queue(); var before = window.Activations;
            window.AcceptTopmostRestore = false;
            if (boundary == "off") Plugin.MatchFocus.Value = false;
            if (boundary == "reconnect") network.Values["_Reconnecting"] = true;
            if (boundary == "missing") RuntimeObject.Network = null;
            Found(8200 + Array.IndexOf(new[] { "off", "reconnect", "missing", "pvp", "new-room" }, boundary), boundary == "pvp" ? 1 : 4);
            check(window.Activations == before, "uncertain rollback blocks new focus at lifecycle boundary: " + boundary);
            window.AcceptTopmostRestore = true; Plugin.MatchFocus.Value = true;
            network.Values["_Reconnecting"] = false; RuntimeObject.Network = network;
            info.Values["MapType"] = 4; Tick();
            check(window.Activations == before, "rollback recovery cannot replay a queue armed before boundary: " + boundary);
        }
        // Exercise the real poller with no matchmaking object: every verified PvE manual lobby uses public WAIT -> CHOICE.
        manager.Values["match"] = null;
        foreach (var map in new[] { 4, 6, 9, 10, 12 })
        {
            window.Foreground = false;
            var before = window.Activations; var reads = RuntimeObject.Reads.Count; var nativeCalls = window.Calls;
            Found(8500 + map, map, state: 1, isMatch: false);
            check(window.Calls == nativeCalls, "manual lobby join WAIT is silent: " + map);
            Found(8500 + map, map, isMatch: false);
            check(window.Activations == before + 1 && Plugin.Diagnostics.States["matchFocus.result"] == "Activated",
                "production public/private PvE host start activates without matchData: " + map);
            check(RuntimeObject.Reads.Skip(reads).All(read => !read.StartsWith("Match", StringComparison.Ordinal) && read != "Manager.match"),
                "manual rooms do not depend on queue data: " + map);
            nativeCalls = window.Calls; Tick(); Tick();
            check(window.Calls == nativeCalls, "manual CHOICE poll consumes one episode: " + map);
        }
        var manualBefore = window.Activations;
        window.Foreground = false; Found(8600, state: 1, isMatch: false); Found(8600, isMatch: false);
        window.Foreground = false; Found(8600, state: 1, isMatch: false); Found(8600, isMatch: false);
        check(window.Activations == manualBefore + 2, "production same manual room permits a new host start after returning to lobby");
        foreach (var state in new[] { 2, 3, 4, 5, -1 })
        {
            manualBefore = window.Activations;
            Found(8700 + state, state: state, isMatch: false); Found(8700 + state, isMatch: false);
            check(window.Activations == manualBefore, "manual startup in non-WAIT state never invents host start: " + state);
        }
        foreach (var boundary in new[] { "off", "disconnect", "reconnect", "sync", "manager", "network", "controller", "info", "rollback", "pvp", "unknown", "ready", "identity", "classification" })
        {
            window.Foreground = false; manualBefore = window.Activations;
            var id = 8800 + Array.IndexOf(new[] { "off", "disconnect", "reconnect", "sync", "manager", "network", "controller", "info", "rollback", "pvp", "unknown", "ready", "identity", "classification" }, boundary);
            Found(id, state: 1, isMatch: false);
            if (boundary == "off") Plugin.MatchFocus.Value = false;
            if (boundary == "disconnect") network.Values["IsConnected"] = false;
            if (boundary == "reconnect") network.Values["_Reconnecting"] = true;
            if (boundary == "sync") manager.Values["connectRoomId"] = (long)id;
            if (boundary == "manager") RuntimeObject.Manager = null;
            if (boundary == "network") RuntimeObject.Network = null;
            if (boundary == "controller") room.Values["roomController"] = null;
            if (boundary == "info") local.Values["info"] = null;
            if (boundary == "rollback") window.AcceptTopmostRestore = false;
            Found(boundary == "identity" ? id + 100 : id, boundary == "pvp" ? 1 : boundary == "unknown" ? 0 : 4,
                state: boundary == "ready" ? 3 : 2, isMatch: boundary == "classification");
            window.AcceptTopmostRestore = true; Plugin.MatchFocus.Value = true;
            network.Values["IsConnected"] = true; network.Values["_Reconnecting"] = false; manager.Values["connectRoomId"] = 0L;
            RuntimeObject.Manager = manager; RuntimeObject.Network = network; room.Values["roomController"] = controller; local.Values["info"] = info;
            Found(id, isMatch: false);
            check(window.Activations == manualBefore, "production manual WAIT cannot survive lifecycle break: " + boundary);
            Found(id, state: 1, isMatch: false); Found(id, isMatch: false);
            check(window.Activations == manualBefore + 1, "production manual next genuine host start recovers: " + boundary);
        }
        check(RuntimeObject.Reads.All(read => !new[] { "Password", "Host", "Owner", "Player", "Bot", "Count", "Name" }.Any(word => read.Contains(word, StringComparison.Ordinal))),
            "manual focus reads no password/host/player/bot/count/identity fields");
        manager.Values["match"] = match;
        // Exercise the real production breaker. A changed native getter/type stops only this feature.
        Queue(); info.Values["MapType"] = 4L; info.Values["Id"] = 9000L; controller.Values["roomStateType"] = 2; Tick();
        check(!Compatibility.Allowed(MatchFocus.Feature) && Compatibility.Allowed("CoreUi") && Compatibility.Allowed("HandLayout"), "binding failure trips only focus breaker");
        var blockedCalls = window.Calls; info.Values["MapType"] = 4; Queue(); Found(9001);
        check(window.Calls == blockedCalls, "breaker prevents all later OS attempts");
        var restorationCalls = window.TopmostRestores;
        window.AcceptTopmostRestore = false; Plugin.MatchFocus.Value = false; RuntimeObject.Manager = null;
        Tick();
        check(window.TopmostRestores == restorationCalls + 1 && window.Calls == blockedCalls,
            "disabled feature breaker and missing manager cannot bypass owned rollback retry");
        window.AcceptTopmostRestore = true; Tick();
        check(window.TopmostRestores == restorationCalls + 2 && window.Calls == blockedCalls,
            "owned rollback recovery after breaker does not reactivate any consumed room");
        window.ThrowTopmostRestore = true; Tick();
        check(window.TopmostRestores == restorationCalls + 3 && window.Calls == blockedCalls,
            "rollback exception stays inside production feature catcher without activation");
    }
}
