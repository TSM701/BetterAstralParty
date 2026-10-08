using UnityEngine;

namespace BetterAstralParty;

// Mod-owned monster nameplates. Native code owns all positioning and render timing.
internal static class FieldBuffUi
{
    private static readonly Dictionary<IntPtr, Plate> Plates = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static readonly List<IntPtr> Stale = new();
    private static IntPtr _logicClass, _plateClass, _parent;
    private static float _scanAt;
    private static IntPtr _pressed;
    private static RuntimeObject? _popupList, _popupScroll;
    private static bool _popupTouchable, _popupWheel;
    internal static int ActivePlateCount => Plates.Count;
    internal static int EffectCount { get; private set; }

    private sealed class Plate
    {
        internal readonly RuntimeObject Ui, List;
        internal readonly IntPtr Player;
        private readonly List<(bool Property, int Id, int Progress, int Rounds)> _effects = new();
        private (int Hp, int Max, int Atk, int Def, int Move)? _stats;
        private string _diagnosticState = "";
        private float _diagnoseAt;
        internal bool NameFontApplied;
        internal int EffectCount => _effects.Count;

        internal void Diagnose(RuntimeObject player, RuntimeObject parent, int slot)
        {
            if (!Plugin.Diagnostics.IsRecording || Time.unscaledTime < _diagnoseAt) return;
            _diagnoseAt = Time.unscaledTime + 1f;
            try
            {
                var character = player.Field("CharacterInst");
                var animator = character?.Field("characterAnimator");
                var state = $"visible={Ui.Get<bool>("visible")}; onStage={Ui.Get<bool>("onStage")}; attached={Ui.Get("parent")?.Pointer == parent.Pointer}; ownerMatches={Ui.Get("PlayerData")?.Pointer == player.Pointer}; ownerZero={Ui.Call("GetOwnerPos")!.Value<Vector3>() == Vector3.zero}; animatorHidden={animator?.Call("IsHide")?.Value<bool>()}; hp={_stats?.Hp}; effects={_effects.Count}";
                if (state == _diagnosticState) return;
                _diagnosticState = state;
                Plugin.Diagnostics.Write(FormattableString.Invariant($"field.slot{slot} {state}; x={Ui.Get<float>("x"):F0}; y={Ui.Get<float>("y"):F0}; alpha={Ui.Get<float>("alpha"):F2}"));
            }
            catch (Exception ex) { Plugin.Diagnostics.Error("FieldBuff.inspect", ex); }
        }

        internal Plate(RuntimeObject player, RuntimeObject parent)
        {
            Player = player.Pointer;
            Ui = RuntimeObject.StaticCall(_plateClass, "CreateInstance")
                ?? throw new InvalidOperationException("필드 상태 UI 준비 대기");
            try
            {
                Ui.Set("visible", false);
                var roomPlayer = player.Field("player")!;
                Ui.SetField("_PlayerId", roomPlayer.Get<long>("Id"));
                // Native display names preserve the game's incognito/friend-name rules.
                var nickname = roomPlayer.Call("GetNick", false)!.String();
                var nameLabel = Ui.Field("txt_Name")!;
                nameLabel.Set("text", nickname);
                Ui.Field("txt_Number")!.Set("text", ""); // Monster serial number does not apply to heroes.
                Ui.Call("SwitchInfo", false);
                // PvE hero-only clones: keep buff artwork and monster plates unchanged.
                var allyColor = new Color(0.35f, 0.8f, 1f);
                foreach (var field in new[] { "txt_Name", "txt_HP", "graph_Line", "graph_Guide", "graph_Sign" })
                    Ui.Field(field)!.Set("color", allyColor);
                List = Ui.Field("list_Buff")!;
                List.Set("numItems", 0); // No placeholder icons when the player has no public effects.
                // Do not call ShowAttrInfo/RendererBuff: they subscribe native pooled icons
                // to property callbacks without detaching them when the icon is disposed.
                parent.Call("AddChild", Ui);
            }
            catch { GameUi.Dispose(Ui); throw; }
        }

        internal void Refresh(RuntimeObject player, RuntimeObject buffs, RuntimeObject properties)
        {
            var attr = player.Get("Property")!;
            var stats = (attr.Field("HP")!.Get<int>("Value"), attr.Field("maxHP")!.Value<int>(),
                attr.Field("ATK")!.Get<int>("Value"), attr.Field("DEF")!.Get<int>("Value"), attr.Get<int>("ExtraMovePoint"));
            if (_stats != stats)
            {
                _stats = stats;
                Ui.Call("UpdateHP", stats.Item1, stats.Item2);
                Ui.Call("UpdateATK", stats.Item3);
                Ui.Call("UpdateDEF", stats.Item4);
                Ui.Call("UpdateExtraMovePoint", stats.Item5);
            }
            var propertyCount = properties.Get<int>("Count");
            var count = propertyCount + buffs.Get<int>("Count");
            var changed = _effects.Count != count;
            if (changed)
            {
                List.Set("numItems", count);
                _effects.Clear();
                for (var i = 0; i < count; i++) _effects.Add(default);
            }
            for (var i = 0; i < count; i++)
            {
                var property = i < propertyCount;
                var data = property ? properties.Call("get_Item", i)! : buffs.Call("get_Item", i - propertyCount)!;
                var id = property ? data.Field("buffId")!.Value<int>() : data.Get<int>("BuffId");
                var progress = property ? data.Field("property")!.Get<int>("Value") : data.Get<int>("Progress");
                var rounds = property ? 0 : data.Get<int>("KeepRound");
                var state = (property, id, progress, rounds);
                if (_effects[i] == state) continue;
                var icon = List.Call("GetChildAt", i)!;
                icon.Field("showframe")!.Set("selectedIndex", 1);
                if (property) icon.Call("RefreshProperty", data, false);
                else icon.Call("RefreshBuff", data);
                _effects[i] = state;
                changed = true;
            }
            if (changed) List.Call("ResizeToFit");
        }
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed("FieldBuffs")) return;
        try
        {
            if (!Plugin.FieldBuffs.Value) { Clear("setting-off"); return; }
            if (GameUi.HomeAvailable) RuntimeObject.RequireClasses("FieldBuffs", ("GameLogic", "GameLogicManager"), ("UI", "UICom_AttrInfo"));
            if (ModUi.IsOpen) { Clear("settings-open"); return; }
            if (GameUi.Root == null) return;
            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.2f;
                Scan();
            }
            // Keep passive plates alive when screenshots/other windows take focus.
            Plugin.Diagnostics.State("field.focus", Application.isFocused.ToString());
            if (!Application.isFocused) { _pressed = IntPtr.Zero; return; }
            // Same click-to-open public buff description as the monster plate.
            if (Plates.Count == 0 || (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonUp(0))) return;
            RuntimeObject? hit = null;
            foreach (var item in GameUi.PointerPath())
                if (Plates.TryGetValue(item.Pointer, out _)) { hit = item; break; }
            if (Input.GetMouseButtonDown(0)) _pressed = hit?.Pointer ?? IntPtr.Zero;
            if (Input.GetMouseButtonUp(0))
            {
                if (hit != null && hit.Pointer == _pressed && GameUi.Visible(hit.Field("graph_ShowBuff")))
                    hit.Call("OpenBuffInfo", (object?)null);
                _pressed = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Compatibility.Block("FieldBuffs", ex, () => Clear("exception"));
        }
    }

    private static void Scan()
    {
        if (_logicClass == IntPtr.Zero || _plateClass == IntPtr.Zero)
        {
            _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            _plateClass = RuntimeObject.FindClass("UI", "UICom_AttrInfo");
            if (_logicClass == IntPtr.Zero || _plateClass == IntPtr.Zero) return;
        }
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (!VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)) { Clear("not-pve"); return; }
        if (logic?.Get("fight")?.Field("fightStatus")?.Value<bool>() == true) { Clear("fight-active"); return; }
        UpdateBuffPopup();
        var battle = logic?.Get("battle");
        var info = battle?.Field("battleInfo");
        var battleUi = info?.Get("ui");
        var parent = battleUi?.Field("com_AttrInfos");
        var players = battle?.Field("PlayerDatas");
        if (!GameUi.Visible(parent)) { Clear("parent-hidden"); return; }
        if (players == null) { Clear("players-unavailable"); return; }
        if (_parent != parent!.Pointer) { Clear("parent-changed"); _parent = parent.Pointer; }
        Plugin.Diagnostics.State("field.gate", "scan");
        Seen.Clear();
        EffectCount = 0;
        var diagnose = Plugin.Diagnostics.IsRecording;
        var playerCount = players.Get<int>("Count");
        for (var i = 0; i < playerCount; i++)
        {
            var player = players.Call("get_Item", i)!;
            if (player.Get<int>("characterType") != 1) continue;
            if (player.Field("CharacterInst") == null) { if (diagnose) Plugin.Diagnostics.State($"field.slot{i}.scan", "character-unavailable"); continue; }
            var shown = player.Get("buffContainer")?.Call("GetShowBuffs", player, false);
            if (shown == null) { if (diagnose) Plugin.Diagnostics.State($"field.slot{i}.scan", "effects-unavailable"); continue; }
            if (diagnose) Plugin.Diagnostics.State($"field.slot{i}.scan", "ready");
            var buffs = shown.Field("Item1")!;
            var properties = shown.Field("Item2")!;
            Plate? plate = null;
            foreach (var existing in Plates.Values)
                if (existing.Player == player.Pointer) { plate = existing; break; }
            if (plate == null || plate.Ui.Get<bool>("isDisposed"))
            {
                plate = new Plate(player, parent);
                Plates.Add(plate.Ui.Pointer, plate);
                Plugin.Diagnostics.Write($"field.slot{i} created");
            }
            Seen.Add(plate.Ui.Pointer);
            if (!plate.NameFontApplied)
                plate.NameFontApplied = FieldNameFont.Apply(plate.Ui.Field("txt_Name")!, battleUi);
            plate.Refresh(player, buffs, properties);
            EffectCount += plate.EffectCount;
            plate.Diagnose(player, parent, i);
        }
        Stale.Clear();
        foreach (var key in Plates.Keys) if (!Seen.Contains(key)) Stale.Add(key);
        foreach (var key in Stale)
        {
            GameUi.Dispose(Plates[key].Ui);
            Plates.Remove(key);
            Plugin.Diagnostics.Write("field removed: player-not-seen");
        }
    }

    internal static void Clear(string reason)
    {
        RestoreBuffPopup();
        Plugin.Diagnostics.State("field.gate", reason);
        if (Plates.Count > 0) Plugin.Diagnostics.Write($"field clear: {reason}; count={Plates.Count}");
        foreach (var plate in Plates.Values) GameUi.Dispose(plate.Ui);
        Plates.Clear(); Seen.Clear(); Stale.Clear();
        EffectCount = 0;
        _parent = _pressed = IntPtr.Zero;
    }

    private static void UpdateBuffPopup()
    {
        var window = GameUi.Find(GameUi.Root!, "BattlePlayerInfoWindow");
        var panel = window?.Get("contentPane")?.Field("com_Monster");
        var list = GameUi.Visible(panel) ? panel!.Field("list_Buff") : null;
        if (list != null && list.Pointer == _popupList?.Pointer && _popupScroll != null && Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("field.buffScroll", $"items={list.Get<int>("numItems")}; touchable={list.Get<bool>("touchable")}; wheel={_popupScroll.Get<bool>("mouseWheelEnabled")}; position={_popupScroll.Get<float>("posY"):F0}; content={_popupScroll.Get<float>("contentHeight"):F0}; viewport={_popupScroll.Get<float>("viewHeight"):F0}");
        if (list?.Pointer == _popupList?.Pointer) return;
        RestoreBuffPopup();
        if (list == null) return;
        var scroll = list.Get("scrollPane");
        if (scroll == null) return;
        _popupList = list; _popupScroll = scroll;
        _popupTouchable = list.Get<bool>("touchable");
        _popupWheel = scroll.Get<bool>("mouseWheelEnabled");
        // Native list already contains every public effect and a four-item scroll viewport.
        // Its authored touchable=false prevented the existing ScrollPane from receiving the wheel.
        list.Set("touchable", true);
        scroll.Set("mouseWheelEnabled", true);
        Plugin.Diagnostics.Write("field buff popup: native scrolling enabled");
    }

    private static void RestoreBuffPopup()
    {
        if (_popupList != null && !_popupList.Get<bool>("isDisposed"))
        {
            _popupList.Set("touchable", _popupTouchable);
            _popupScroll!.Set("mouseWheelEnabled", _popupWheel);
        }
        _popupList = _popupScroll = null;
    }
}
