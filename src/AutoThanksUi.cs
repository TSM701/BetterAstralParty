using UnityEngine;

namespace BetterAstralParty;

// Confirm the native recommendation, not a fabricated message or a replacement sender.
internal static class AutoThanksUi
{
    private const string Feature = "AutoThanks";
    private static (IntPtr Window, IntPtr Cts, int Id)? _episode;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            var root = GameUi.Root;
            if (root == null || GameUi.HomeAvailable || PvpSafety.Suspended) { Clear(); return; }
            var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
            var roomLogic = logic?.Get("room");
            var room = roomLogic?.Get("curRoomInfo");
            var roomState = roomLogic?.Field("roomController")?.Field("roomStateType")?.Value<int>();
            if (roomState != 4) { if (roomState != null) Clear(); return; }
            var window = GameUi.Find(root, "ExpressionWindow", maxDepth: 1);
            var button = window?.Field("_btn_QuickReply");
            if (!GameUi.Visible(button) || !button!.Get<bool>("touchable") || button.Get<bool>("grayed")) return;
            var cts = window!.Field("quickReplyCts");
            if (cts == null || cts.Field<bool>("_dispose") || cts.Field("cts") == null
                || cts.Get<bool>("IsCancellationRequested")) return;
            var episode = (window.Pointer, cts.Pointer, cts.Field<int>("id"));
            if (_episode == episode) return;
            // Temporary focus/UI locks must not reset the recommendation's once-only latch.
            // Native recommendations may coexist with other popups/modals; their own button/CTS/listener owns availability.
            if (!Application.isFocused || ModUi.IsOpen || Input.touchCount > 0
                || root.Get<bool>("modalWaiting")
                || RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "GObject"), "get_draggingObject") != null) return;
            for (var i = 0; i < 3; i++)
                if (Input.GetMouseButton(i) || Input.GetMouseButtonDown(i) || Input.GetMouseButtonUp(i)) return;
            if (room?.Call("IsPVE")?.Value<bool>() != true || room.Call("GetSelfInfo") == null
                || logic?.Get("watch")?.Call("PlayerIsWatcher")?.Value<bool>() != false
                || logic.Get("replay")?.Get("Session")?.Get<bool>("CanSendC2S") != true) return;
            var battle = logic.Get("battle");
            if (battle?.Field<bool>("clientFinishReady") != true) return;
            var player = battle.Call("GetSelfPlayerData")?.Field("player");
            var localId = logic.Get("account")?.Call("GetPlayerID");
            if (player == null || localId == null || localId.Value<long>() == 0 || localId.Value<long>() != player.Get<long>("Id")) return;
            if (window.Field("_showQuickReply") == null || !window.Get<bool>("isShowing")) return;
            var click = button.Get("onClick");
            var bridge = click?.Field("_bridge");
            if (bridge?.Field("owner") == null || bridge.Field<bool>("_isLocking")
                || click!.Get<bool>("isEmpty") || click.Get<bool>("isDispatching")) return;
            var handler = bridge.Field("_callback0");
            if (handler?.Call("GetInvocationList")?.Get<int>("Length") != 1
                || bridge.Field("_callback1") != null || bridge.Field("_captureCallback") != null) return;
            var callback = handler.Get("Target");
            if (callback?.Field("<>4__this")?.Pointer != window.Pointer) return;
            // shortcut: native gratitude IDs are 50000..50002; re-audit if its recommendation producer changes.
            if (callback.Field<int>("chatId") is not (50000 or 50001 or 50002)) return;
            // Native Call's bool is not a send acknowledgement; never retry an uncertain dispatch.
            _episode = episode;
            click.Call("Call");
            Plugin.Diagnostics.State("autoThanks", "native-recommendation-confirmed");
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex); }
    }

    internal static void Clear() => _episode = null;
}
