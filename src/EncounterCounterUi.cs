using UnityEngine;

namespace BetterAstralParty;

internal static class EncounterCounterUi
{
    private static RuntimeObject? _fight, _anchor, _effect;
    private static IntPtr _logicClass, _defender;
    private static float _scanAt;

    internal static void Tick()
    {
        if (!Compatibility.Allowed("BattleStatus")) return;
        try
        {
            if (!Plugin.BattleStatus.Value || ModUi.IsOpen || !Application.isFocused) { Clear(); return; }
            if (GameUi.Root == null) return;
            if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            if (_logicClass == IntPtr.Zero) return;
            var room = RuntimeObject.StaticField(_logicClass, "_inst")?.Get("room")?.Get("curRoomInfo");
            if (!VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)) { Clear(); return; }
            if (!GameUi.Visible(_fight) && Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.2f;
                _fight = GameUi.Find(GameUi.Root, "FightWindow");
            }
            var ui = GameUi.Visible(_fight) ? _fight!.Get("contentPane") : null;
            var anchor = ui?.Field("com_LaunchPK")?.Field("com_Counter");
            var defender = _fight?.Get<bool>("isDisposed") == false ? _fight.Get("defenderData") : null;
            if (ui?.Field("step")?.Get<int>("selectedIndex") != 1 || !GameUi.Visible(anchor)
                || defender?.Get<int>("characterType") != 2) { Clear(); return; }
            if (defender.Get("Property")?.Field("Counter")?.Get<bool>("Value") != true) { Clear(); return; }
            if (_anchor?.Pointer == anchor!.Pointer && _defender == defender.Pointer
                && _effect?.Get<bool>("isDisposed") == false) return;
            Clear();
            // Overlay a private effect at the official icon, inheriting its transform and visibility.
            // Do not animate/hide the original icon or depend on the recommendation toggle.
            _effect = NativeUi.CounterIndicator(anchor!);
            if (_effect == null) return;
            var size = Math.Min(anchor!.Get<float>("width"), anchor.Get<float>("height"));
            _effect.Call("SetScale", size / 160f, size / 160f);
            _effect.Call("SetXY", (anchor.Get<float>("width") - size) / 2,
                (anchor.Get<float>("height") - size) / 2);
            _effect.Set("visible", true);
            _anchor = anchor; _defender = defender.Pointer;
            NativeUi.Play(_effect, "MaxPoint");
        }
        catch (Exception ex)
        {
            Compatibility.Block("BattleStatus", ex, Clear, BattleStatusUi.Clear);
        }
    }

    internal static void Clear()
    {
        GameUi.Dispose(_effect);
        _effect = _anchor = null;
        _defender = IntPtr.Zero;
    }
}
