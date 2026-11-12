using UnityEngine;

namespace BetterAstralParty;

// Game bug of the 2026-11-12 update (workaround user-requested that day): ChooseRoundCardWindow.ChooseRoundCard, which picks round
// cards and the Plant Building Shop's cards, now sends CardLogic.RequestBuildingCardS2C, and unlike the RequestRoundCard it
// replaced, that never cancels the window's input timer (OperationTimer.ActionDownTime, keyed by the action Sn). The timer
// outlives the choice: when it runs out the game marks the player AFK (punishment time limits, a time-over log, GiveUpGame
// at the AFK limit), while it stays no new timer under that Sn can start, and its auto choice fires into the next action
// the same window shows. Once the window has closed or moved on, this cancels the very timer object that window started,
// as RequestRoundCard did, and nothing else: a timer the game cancelled itself, or a newer one under the same Sn, is left
// alone, so a fix in the game turns this into a no-op. PvE only, never in replays; it sends nothing.
internal static class CardChoiceTimer
{
    internal const string Feature = "CardChoiceTimer";
    private static long _sn;
    // Held while watched, so its address cannot be reused by another object before the comparison.
    private static RuntimeObject? _timer;
    private static float _nextPoll;
    private static int _ended;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature) || Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.1f;
        var logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var timerClass = RuntimeObject.FindClass("GameLogic", "OperationTimer");
        if (logicClass == IntPtr.Zero || timerClass == IntPtr.Zero) { Clear(); return; }
        var logic = RuntimeObject.StaticField(logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        var replay = logic?.Get("replay")?.Get("Session")?.Get<bool>("IsReplay") == true;
        if (room == null || replay || room.Call("IsPVE")?.Value<bool>() != true || GameUi.Root is not { } root) { Clear(); return; }
        var window = GameUi.Find(root, "ChooseRoundCardWindow", maxDepth: 1);
        var open = window?.Field<long>("_sn") ?? 0;
        if (_sn != 0 && _sn != open)
        {
            var registered = RuntimeObject.StaticCall(timerClass, "GetOperateTimer", _sn);
            var due = InputAttentionState.StaleChoiceTimer(_sn, _timer?.Pointer ?? IntPtr.Zero, open, registered?.Pointer ?? IntPtr.Zero);
            if (due != 0) RuntimeObject.StaticCall(timerClass, "CancelOperatTimer", due);
            // "gone" is the game's own cancel or completion (an auto choice); "replaced" a newer timer under the same Sn.
            Plugin.Diagnostics.State("cardChoiceTimer", $"{++_ended} {(due != 0 ? "cancelled" : _timer == null ? "unseen" : registered == null ? "gone" : "replaced")}");
            _sn = 0; _timer = null;
        }
        // The window registers its timer right after showing; keep looking until it is there.
        if (open != 0 && (_sn != open || _timer == null)) { _sn = open; _timer = RuntimeObject.StaticCall(timerClass, "GetOperateTimer", open); }
    }

    internal static void Clear() { _sn = 0; _timer = null; }
}
