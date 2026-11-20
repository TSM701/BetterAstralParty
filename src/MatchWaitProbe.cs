using UnityEngine;

namespace BetterAstralParty;

// Matchmaking wait record (user-requested 2026-11-18, to learn what the native wait display shows before changing it).
// The server sends an estimate with the Matching state (RefreshMatchTeamStateNotify) and on each GetMatchEstimatedWaitTime
// answer; MatchInfoWindow counts it down from the server second it arrived (_estimatedWaitTimeEnd = now + estimate, set by
// StartMatch and OnEstimatedWaitTimeChange) and at zero asks again every second (_nextEstimatedWaitTimeRefresh = now + 1);
// its timer stops at 300 s. While diagnostics record, each queue writes `matchWait.start` (map, mode, difficulty, party
// size), `matchWait.estimate` for each answer seen (a change of _estimatedWaitTimeEnd; at most 25 a queue) and
// `matchWait.end`: matched or how it ended, the wait, answer and request counts, the first, last, lowest and highest
// estimate and, for a match, the room's map, players, bots and its own WaitTime field. Reads only, never IDs or names;
// runs after the PvP gate, so PvP queues are never read.
internal static class MatchWaitProbe
{
    private const string Feature = "MatchWaitProbe";
    private const int MaxLines = 300, MaxEstimateLines = 25;
    private const float RoomWait = 5f; // A Playing state may arrive before MatchSuccessS2C creates the room.
    private static IntPtr _logicClass, _uiClass;
    private static float _pollAt;
    private static int _lines;
    private static Queue? _queue;

    private sealed class Queue
    {
        internal float Started, EndedAt = -1;
        internal int EstimateEnd = int.MinValue, NextRefresh, Estimates, Requests, Logged;
        internal int First = -1, Last = -1, Min = int.MaxValue, Max = int.MinValue, EndStatus;
    }

    internal static void Tick()
    {
        if (!Plugin.Diagnostics.IsRecording) { _queue = null; return; }
        if (!Compatibility.Allowed(Feature) || Time.unscaledTime < _pollAt) return;
        _pollAt = Time.unscaledTime + .25f;
        try { Poll(); }
        catch (Exception ex) { _queue = null; Compatibility.Block(Feature, ex); }
    }

    private static void Poll()
    {
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var match = logic?.Get("match")?.Field("matchData");
        if (match == null) { _queue = null; return; }
        var status = match.Get<long>("TeamId") != 0 ? match.Get<int>("MatchStatus") : 0;
        var now = Time.realtimeSinceStartup;
        if (_queue == null)
        {
            if (status != 2) return;
            _queue = new Queue { Started = now, NextRefresh = Window()?.Field<int>("_nextEstimatedWaitTimeRefresh") ?? 0 };
            Write($"matchWait.start map={match.Get<int>("MatchMapId")}; mode={match.Get("MatchMode")!.Value<int>()}; difficulty={match.Get<int>("MatchDifficulty")}; party={match.Get("TeamPlayers")?.Get<int>("Count") ?? 0}");
        }
        var queue = _queue;
        var waited = (int)Math.Round(now - queue.Started);
        // MatchSuccessS2C creates the room (CreateRoom, then the room state) whatever the queue status says by then.
        var controller = logic!.Get("room")?.Field("roomController");
        var room = controller != null && controller.Field<int>("roomStateType") != 0 ? controller.Get("localRoom")?.Field("info") : null;
        if (status == 2 && room == null && queue.EndedAt < 0)
        {
            var window = Window();
            if (window == null) return;
            var end = window.Field<int>("_estimatedWaitTimeEnd");
            if (end != 0 && end != queue.EstimateEnd)
            {
                queue.EstimateEnd = end;
                var value = match.Get<int>("EstimatedWaitTime");
                queue.Estimates++;
                if (queue.First < 0) queue.First = value;
                queue.Last = value; queue.Min = Math.Min(queue.Min, value); queue.Max = Math.Max(queue.Max, value);
                if (queue.Logged < MaxEstimateLines) { queue.Logged++; Write($"matchWait.estimate t={waited}s; value={value}s; requests={queue.Requests}"); }
            }
            var next = window.Field<int>("_nextEstimatedWaitTimeRefresh");
            if (next != 0 && next != queue.NextRefresh) queue.Requests++;
            queue.NextRefresh = next;
            return;
        }
        if (queue.EndedAt < 0) { queue.EndedAt = now; queue.EndStatus = status; }
        if (room == null && queue.EndStatus == 3 && now - queue.EndedAt < RoomWait) return;
        var outcome = room != null ? "matched" : queue.EndStatus switch { 1 => "cancelled", 0 => "left", _ => "status " + queue.EndStatus };
        var line = $"matchWait.end {outcome}; waited={(int)Math.Round(queue.EndedAt - queue.Started)}s; estimates={queue.Estimates}; requests={queue.Requests}";
        if (queue.Estimates > 0) line += $"; first={queue.First}s; last={queue.Last}s; min={queue.Min}s; max={queue.Max}s";
        if (room != null)
        {
            var players = room.Get("Players");
            int count = players?.Get<int>("Count") ?? 0, bots = 0;
            for (var i = 0; i < count; i++) if (players!.Call("get_Item", i)?.Get<bool>("IsBot") == true) bots++;
            line += $"; room map={room.Get<int>("MapId")}; players={count}; bots={bots}; roomWait={room.Get<long>("WaitTime")}";
        }
        Write(line);
        _queue = null;
    }

    private static RuntimeObject? Window()
    {
        if (_uiClass == IntPtr.Zero) _uiClass = RuntimeObject.FindClass("UI", "UIManager");
        return RuntimeObject.StaticField(_uiClass, "_inst")?.Field("_cachedMatchInfoWindow");
    }

    private static void Write(string line)
    {
        if (_lines >= MaxLines) return;
        Plugin.Diagnostics.Write(++_lines == MaxLines ? line + "; cap reached" : line);
    }
}
