namespace BetterAstralParty;

// Read-only, before every feature tick. Do not cache live room/match state across frames.
internal static class PvpSafety
{
    private static IntPtr _logic;
    private static string? _error;
    internal static bool Suspended { get; private set; }
    // The reader itself failed (a renamed game member), so PvP cannot be told from PvE: gameplay stays off, while ModUi
    // keeps the home menu, its settings and the update notices, where no match runs, so a fixed version can still arrive.
    internal static bool Broken { get; private set; }
    // Why the last poll suspended (mode or room state and map numbers only), for the pvpSafety diagnostic state.
    internal static string Reason { get; private set; } = "";

    internal static bool Poll()
    {
        try
        {
            Broken = false;
            if (_logic == IntPtr.Zero) _logic = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            var manager = _logic == IntPtr.Zero ? null : RuntimeObject.StaticField(_logic, "_inst");
            var match = manager?.Get("match")?.Field("matchData");
            var controller = manager?.Get("room")?.Field("roomController");
            // Dispose clears TeamId but leaves MatchMode/status stale. Do not gate on those alone.
            var team = (match?.Get<long>("TeamId") ?? 0) != 0;
            var mode = team ? match!.Get("MatchMode")!.Value<int>() : 0;
            if (team && !VisibleCombatAdvisor.IsPve(mode)) { Reason = "team mode=" + mode; return Suspended = true; }
            // ClearRoomInfo leaves a stale localRoom; state 0 means no active room. RoomController.Dispose (ReplaySession.Clear
            // when a replay ends) nulls localRoom and keeps roomStateType, and only CreateRoom sets localRoom, before the
            // state: a state without a localRoom is a disposed room, not a match (2026-11-13: PvP stayed on at home after a replay).
            var state = controller?.Field("roomStateType")!.Value<int>() ?? 0;
            var room = state != 0 ? controller!.Get("localRoom") : null;
            var map = room?.Field("info")?.Get<int>("MapType") ?? 0;
            if (room != null && !VisibleCombatAdvisor.IsPve(map)) { Reason = "room state=" + state + " map=" + map; return Suspended = true; }
            // Cold startup is not PvP. Still pause gameplay ticks until both readers exist,
            // but keep one-shot startup notices/audio working. Missing data after suspension
            // retains that suspension until an authoritative non-PvP state is available.
            if (match == null || controller == null) return true;
            _error = null;
            return Suspended = false;
        }
        catch (Exception ex)
        {
            Suspended = Broken = true; // Unknown/broken binding never opens the feature gate.
            Reason = "broken";
            if (_error != ex.Message)
            {
                _error = ex.Message;
                Plugin.Diagnostics.Error("PvpSafety", ex);
                Plugin.Logger.LogWarning("[PvP 감지] 게임 정보를 읽지 못해 전투 기능을 끄고 홈 메뉴와 업데이트만 유지합니다: " + ex.Message);
            }
            return true;
        }
    }
}
