namespace BetterAstralParty;

// Read-only, before every feature tick. Do not cache live room/match state across frames.
internal static class PvpSafety
{
    private static IntPtr _logic;
    private static string? _error;
    internal static bool Suspended { get; private set; }

    internal static bool Poll()
    {
        try
        {
            if (_logic == IntPtr.Zero) _logic = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            var manager = _logic == IntPtr.Zero ? null : RuntimeObject.StaticField(_logic, "_inst");
            var match = manager?.Get("match")?.Field("matchData");
            var controller = manager?.Get("room")?.Field("roomController");
            // Dispose clears TeamId but leaves MatchMode/status stale. Do not gate on those alone.
            var team = (match?.Get<long>("TeamId") ?? 0) != 0;
            if (team && !VisibleCombatAdvisor.IsPve(match!.Get("MatchMode")!.Value<int>()))
                return Suspended = true;
            // ClearRoomInfo leaves a stale localRoom; state 0 means no active room.
            var roomActive = (controller?.Field("roomStateType")!.Value<int>() ?? 0) != 0;
            var map = roomActive ? controller!.Get("localRoom")?.Field("info")?.Get<int>("MapType") ?? 0 : 0;
            if (roomActive && !VisibleCombatAdvisor.IsPve(map)) return Suspended = true;
            // Cold startup is not PvP. Still pause gameplay ticks until both readers exist,
            // but keep one-shot startup notices/audio working. Missing data after suspension
            // retains that suspension until an authoritative non-PvP state is available.
            if (match == null || controller == null) return true;
            _error = null;
            return Suspended = false;
        }
        catch (Exception ex)
        {
            Suspended = true; // Unknown/broken binding never opens the feature gate.
            if (_error != ex.Message) { _error = ex.Message; Plugin.Diagnostics.Error("PvpSafety", ex); }
            return true;
        }
    }
}
