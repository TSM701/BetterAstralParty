using UnityEngine;

namespace BetterAstralParty;

// Native Alt toggles immediately. Cancel that mode on task switching; never send a ping.
internal static class PingFocusGuard
{
    private static bool _cancel;
    internal static void Reset() => _cancel = false;

    internal static void Tick()
    {
        if (!Compatibility.Allowed("PingFocus")) return;
        try
        {
            var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!Application.isFocused || (alt && Input.GetKey(KeyCode.Tab))) _cancel = true;
            if (!_cancel) return;
            var sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
            if (sceneClass != IntPtr.Zero)
            {
                var manager = RuntimeObject.StaticField(sceneClass, "inst")?.Field("MarkManager");
                if (manager != null && manager.Get<bool>("IsActive"))
                {
                    // MarkExit dispatches SelectedCurrentTarget, which sends card/relic pings.
                    // ExitCurrentHover resets visuals/timers but does NOT clear the target.
                    var detection = manager.Field("_detectionSystem")
                        ?? throw new InvalidOperationException("Mark detection unavailable; refusing to confirm a target");
                    detection.Call("ExitCurrentHover");
                    detection.SetField("_currentTarget", (RuntimeObject?)null);
                    manager.Get("MarkInput")?.Call("ExitMark");
                }
            }
            // Retain observed focus loss until the first neutral frame after refocusing.
            // No new Unity callback hooks: this game's IL2CPP bridge crashes on the focus hook.
            if (Application.isFocused && !alt && !Input.GetKey(KeyCode.Tab)) _cancel = false;
        }
        catch (Exception ex) { Compatibility.Block("PingFocus", ex); }
    }
}
