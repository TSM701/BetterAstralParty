using UnityEngine;

namespace BetterAstralParty;

// Experimental Old Version Replays (2026-11-01). The account record screen grays its replay play button only when the
// record's version differs from the running game (AccountInfoWindow.RefreshRecordOperation, BattleShortRecord
// .IsValidVersion), and the button's click handler refuses only while the button is grayed. With the option ON the mod
// clears that gray, so the native handler plays the replay through the current client; the record, the save button and
// the replay path stay native.
// Safety while ON (2026-11-01, after the match played right after an old-version replay froze its animations and ESC):
// TweenManager.Update has no try, so one tween callback that throws stops every later tween for the session (cards held
// at their deal pose, fades at zero, windows never finishing their hide, so focus and ESC never return). FairyGUI's own
// switches (GTween/Timers .catchCallbackExceptions, false natively) log and drop that callback instead. A replay whose
// settlement preview failed leaves the replay state on at home (AccountInfoWindow.RequestPlayReplay drops the failed
// result), and the next live match would hide Move and refuse fights; the mod ends that state at an idle home.
internal static class ReplayUi
{
    internal const string Feature = "OldReplays";
    private static RuntimeObject? _fight, _button;
    private static string? _replayId; // The record whose gray the mod cleared.
    private static (bool Tween, bool Timers)? _catch; // The native switch values while the mod holds them ON.
    private static float _checkAt, _staleSince = -1;
    private static bool _replay;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { Update(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update()
    {
        if (PvpSafety.Suspended || !Plugin.OldReplays.Value) { Clear(); return; }
        CatchCallbacks(true);
        SettleReplay();
        var root = GameUi.Root;
        var fight = root == null ? null
            : GameUi.Find(root, "AccountInfoWindow", maxDepth: 1)?.Get("contentPane")?.Field("com_Main")?.Field("com_Fight");
        var button = fight?.Field("btn_ReplayPlay");
        if (button == null) { Release(); return; }
        if (!button.Get<bool>("visible") || !button.Get<bool>("grayed")) return;
        button.Set("grayed", false);
        _fight = fight; _button = button;
        _replayId = fight!.Field("txt_ReplayId")?.Get("text")?.String();
        Plugin.Diagnostics.State("oldReplays", "play button enabled");
    }

    private static void CatchCallbacks(bool on)
    {
        if (on == (_catch != null)) return;
        var tween = RuntimeObject.FindClass("FairyGUI", "GTween");
        var timers = RuntimeObject.FindClass("FairyGUI", "Timers");
        const string name = "catchCallbackExceptions";
        if (on)
        {
            _catch = (RuntimeObject.StaticField(tween, name)!.Value<bool>(), RuntimeObject.StaticField(timers, name)!.Value<bool>());
            RuntimeObject.SetStaticField(tween, name, true);
            RuntimeObject.SetStaticField(timers, name, true);
        }
        else
        {
            var (tweenValue, timersValue) = _catch!.Value;
            _catch = null;
            RuntimeObject.SetStaticField(tween, name, tweenValue);
            RuntimeObject.SetStaticField(timers, name, timersValue);
        }
        Plugin.Diagnostics.State("oldReplays.catch", on ? "on" : "native");
    }

    // A replay state still on at an idle home (home panel current, nothing on top, no team, room state 0 and no room
    // connection, so no live room exists to lose) for 2 seconds is left over. It ends the way ReplayLogic.ExitReplay does,
    // without its DelaySignalManager.CancelAllTask, which would cancel unrelated delayed work game-wide.
    private static void SettleReplay()
    {
        if (Time.unscaledTime < _checkAt) return;
        _checkAt = Time.unscaledTime + .5f;
        var session = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst")?.Get("replay")?.Get("Session");
        var replay = session?.Get<bool>("IsReplay") == true;
        if (replay != _replay)
        {
            _replay = replay;
            Plugin.Diagnostics.State("oldReplays.session", replay ? "replay state=" + State(session!) : "none");
        }
        if (session == null || !replay || !GameUi.NotificationHome().Eligible) { _staleSince = -1; return; }
        if (_staleSince < 0) { _staleSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _staleSince < 2f) return;
        _staleSince = -1;
        var state = State(session);
        session.Get("CurrentPlayer")?.Call("Stop");
        session.Call("Clear");
        Plugin.Diagnostics.State("oldReplays.session", "left over at home; cleared state=" + state);
    }

    // ReplayState for the log only; its width is checked before any read, and an unexpected one must not stop the feature.
    private static string State(RuntimeObject session)
    {
        try { return session.Get<int>("State").ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch (InvalidCastException) { return "unknown"; }
    }

    // Grays the button again only while it still shows the same record: the native refresh sets it for every other one.
    private static void Release()
    {
        var (fight, button, id) = (_fight, _button, _replayId);
        _fight = _button = null; _replayId = null;
        if (button == null || button.Get<bool>("isDisposed") || id == null) return;
        if (fight!.Field("txt_ReplayId")?.Get("text")?.String() == id) button.Set("grayed", true);
    }

    internal static void Clear()
    {
        _staleSince = -1; _replay = false;
        try { Release(); }
        finally { CatchCallbacks(false); }
    }
}
