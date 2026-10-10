namespace BetterAstralParty;

// Poll public pre-match state, independently of gameplay/UI gates. Never register hot-loaded delegates.
internal static class MatchFocus
{
    internal const string Feature = "MatchFocus";
    private static readonly MatchFocusState State = new();
    private static readonly WindowsMatchFocus Windows = new();
    private static IntPtr _logic, _network;

    internal static void Tick() => Tick(Windows);

    internal static void Tick(IMatchFocusWindow windows)
    {
        try
        {
            var restored = windows.RestoreTopmost();
            Plugin.Diagnostics.State("matchFocus.restore", restored ? "ready" : "pending");
            if (!restored) { State.Unavailable(); return; }
            if (!Compatibility.Allowed(Feature)) return;
            if (_logic == IntPtr.Zero) _logic = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            if (_network == IntPtr.Zero) _network = RuntimeObject.FindClass("Core.Net", "NetManager");
            var manager = _logic == IntPtr.Zero ? null : RuntimeObject.StaticField(_logic, "_inst");
            var network = _network == IntPtr.Zero ? null : RuntimeObject.StaticField(_network, "_inst");
            var controller = manager?.Get("room")?.Field("roomController");
            if (manager == null || network == null || controller == null)
            {
                State.Unavailable();
                return;
            }
            var reconnecting = !network.Get<bool>("IsConnected") || network.Field<bool>("_Reconnecting")
                || manager.Get<long>("connectRoomId") != 0;
            // Native NONE=0, WAIT=1, CHOICE=2; queue Matching=2/Playing=3 require a live TeamId.
            var roomState = controller.Field<int>("roomStateType");
            if (roomState == 0)
            {
                var match = manager.Get("match")?.Field("matchData");
                if (match == null) { State.Unavailable(); return; }
                var queueStatus = match.Get<long>("TeamId") > 0 ? match.Get<int>("MatchStatus") : 0;
                var result = State.Poll(Plugin.MatchFocus.Value, 0, false, false, false, queueStatus, reconnecting, windows, Environment.TickCount64);
                Plugin.Diagnostics.State("matchFocus.result", result.ToString());
                return;
            }
            var info = controller.Get("localRoom")?.Field("info");
            if (info == null) { State.Unavailable(); return; }
            // Read the model directly: RoomInfo.MapType's getter writes a default for zero.
            // shortcut: starts without an observed queue/lobby frame are skipped; revisit with a runtime-safe event bridge.
            var outcome = State.Poll(Plugin.MatchFocus.Value, info.Get<long>("Id"), info.Get<bool>("IsMatchRoom"),
                roomState == 2, VisibleCombatAdvisor.IsPve(info.Get<int>("MapType")), 0, reconnecting, windows, Environment.TickCount64, roomState == 1);
            Plugin.Diagnostics.State("matchFocus.result", outcome.ToString());
        }
        catch (Exception error)
        {
            State.Unavailable();
            Compatibility.Block(Feature, error);
        }
    }
}
