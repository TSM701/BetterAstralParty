namespace BetterAstralParty;

// 인디케이터 상세 정보 항상 표시 / Always Show Indicator Details (user-requested 2026-11-20, default ON). The game shows a plate's ATK and DEF only
// while the field is held down: BattleInfoPanel.OnLongPressGestureBegin/End call UIBattleInfoPanel.SwitchMonsterInfo, which
// calls SwitchInfo on each direct UICom_AttrInfo child of com_AttrInfos (the ShowInfo controller, index 1 shown), and keeps
// no state of its own. ON keeps every native monster plate switched on, again after the gesture's end switches them off;
// the zoom-follow plates (FieldMonsterPlates) and the hero plates (PlayerIndicatorUi), which the native switch never
// reaches, follow Shown. OFF leaves the gesture to the game, and Shown then mirrors it through a hidden direct child of
// com_AttrInfos that the native switch reaches, so the hero plates follow the long press as the monster plates do.
internal static class IndicatorDetails
{
    internal const string Feature = "IndicatorDetails";
    private static RuntimeObject? _sentinel, _parent;
    private static IntPtr _logicClass, _plateClass;
    // Whether the last tick held the native plates switched on; turning OFF or clearing then gives them the gesture's state.
    private static bool _forced;

    // Whether plates show ATK and DEF this frame: always while ON, otherwise while the field is held down.
    internal static bool Shown { get; private set; }

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            if (PvpSafety.Suspended || GameUi.Root == null) { Clear(); return; }
            if (_logicClass == IntPtr.Zero)
            {
                _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
                _plateClass = RuntimeObject.FindClass("UI", "UICom_AttrInfo");
                if (_logicClass == IntPtr.Zero || _plateClass == IntPtr.Zero) { _logicClass = IntPtr.Zero; return; }
            }
            var logic = RuntimeObject.StaticField(_logicClass, "_inst");
            if (!VisibleCombatAdvisor.IsPve(logic?.Get("room")?.Get("curRoomInfo")?.Field("info")?.Get<int>("MapType") ?? 0)) { Clear(); return; }
            var parent = logic!.Get("battle")?.Field("battleInfo")?.Get("ui")?.Field("com_AttrInfos");
            if (parent == null || parent.Get<bool>("isDisposed")) { Clear(); return; }
            if (_parent?.Pointer != parent.Pointer || _sentinel == null || _sentinel.Get<bool>("isDisposed") || _sentinel.Get("parent")?.Pointer != parent.Pointer)
            {
                Clear();
                _sentinel = RuntimeObject.StaticCall(_plateClass, "CreateInstance") ?? throw new InvalidOperationException("Stat info sentinel unavailable");
                _sentinel.SetField("_PlayerId", long.MinValue); // Owns no player: GetOwnerPos stays zero and native hides it.
                _sentinel.Set("visible", false);
                _sentinel.Set("touchable", false);
                parent.Call("AddChild", _sentinel);
                _parent = parent;
            }
            var held = ShowInfo(_sentinel) == 1;
            var always = Plugin.IndicatorDetails.Value;
            Shown = always || held;
            if (always || _forced) Switch(parent, Shown);
            _forced = always;
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // The ShowInfo controller's index of a plate, or -1 without one; read through the pointer the plate holds.
    internal static int ShowInfo(RuntimeObject plate)
    {
        var controller = plate.FieldPointer("ShowInfo");
        var index = controller == IntPtr.Zero ? -1 : RuntimeObject.GetOf<int>(controller, "selectedIndex");
        GC.KeepAlive(plate);
        return index;
    }

    // Every plate under com_AttrInfos to `shown` (the sentinel keeps the gesture's state). The zoom layer's plates, a child
    // component here, follow Shown on their own.
    private static void Switch(RuntimeObject parent, bool shown)
    {
        for (int i = 0, count = parent.Get<int>("numChildren"); i < count; i++)
        {
            var child = parent.Call("GetChildAt", i);
            if (child == null || child.Pointer == _sentinel?.Pointer || child.TypeName != "UICom_AttrInfo") continue;
            if (ShowInfo(child) != (shown ? 1 : 0)) child.Call("SwitchInfo", shown);
        }
    }

    internal static void Clear()
    {
        Shown = false;
        try { if (_forced && _parent != null && !_parent.Get<bool>("isDisposed")) Switch(_parent, false); }
        finally
        {
            _forced = false;
            GameUi.Dispose(_sentinel);
            _sentinel = _parent = null;
        }
    }
}
