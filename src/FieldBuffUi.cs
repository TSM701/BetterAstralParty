using UnityEngine;

namespace BetterAstralParty;

// Mod-owned hero clones of the native monster nameplate. They live in an owned layer inside com_AttrInfos,
// which native per-frame layout skips (it only walks direct UICom_AttrInfo children), so the mod positions them
// with the native UpdateOffset/UpdateGuideLine and scales the native pixel lifts by the field zoom.
internal static class FieldBuffUi
{
    private static readonly Dictionary<IntPtr, Plate> Plates = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static readonly List<IntPtr> Stale = new();
    private static readonly Dictionary<IntPtr, CounterBadge> Counters = new();
    private static readonly HashSet<IntPtr> CounterSeen = new(), CounterOwners = new();
    private static readonly string[] CounterParents = { "com_AttrInfos", "com_PlayerAttrInfos" };
    private static IntPtr _logicClass, _plateClass, _sceneClass, _parent;
    private static RuntimeObject? _layer, _battleUi;
    // Native UIBattleInfoPanel.UpdateAttrInfoPosition lifts (com_AttrInfos units at the native camera distance).
    private const float PlateLift = 200f, MarkerLift = 120f;
    private static readonly List<(Plate Plate, Vector2 Owner)> Placed = new();
    private static float _scanAt;
    private static IntPtr _pressed;
    private static Vector2 _pressAt;
    private static bool _cancelled;
    private static RuntimeObject? _popupList, _popupScroll;
    private static bool _popupTouchable, _popupWheel;
    internal static int ActivePlateCount => Plates.Count;
    internal static int EffectCount { get; private set; }

    private sealed class CounterBadge(RuntimeObject icon, IntPtr owner)
    {
        internal readonly RuntimeObject Icon = icon;
        internal readonly IntPtr Owner = owner;
        internal bool? Shown;
        internal (float X, float Y)? Position;
    }

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
            if (GameUi.Root == null) { Clear("root-unavailable"); return; }
            if (FieldIndicatorOpacity.Hidden) { _pressed = IntPtr.Zero; return; }
            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.2f;
                Scan();
            }
            Layout();
            // Keep passive plates alive when screenshots/other windows take focus.
            Plugin.Diagnostics.State("field.focus", Application.isFocused.ToString());
            if (!Application.isFocused) { _pressed = IntPtr.Zero; return; }
            // Same click-to-open public buff description as the monster plate.
            if (Plates.Count == 0) { _pressed = IntPtr.Zero; return; }
            if (_pressed == IntPtr.Zero && !Input.GetMouseButtonDown(0)) return;
            RuntimeObject? hit = null;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0))
                foreach (var item in GameUi.PointerPath())
                    if (Plates.TryGetValue(item.Pointer, out _)) { hit = item; break; }
            var mouse = Input.mousePosition;
            var pointer = new Vector2(mouse.x, mouse.y);
            if (Input.GetMouseButtonDown(0))
            { _pressed = hit?.Pointer ?? IntPtr.Zero; _pressAt = pointer; _cancelled = false; }
            if (_pressed == IntPtr.Zero) return;
            var sensitivity = RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "UIConfig"), "clickDragSensitivity")!.Value<int>();
            // Returning to the same icon after a drag must not become a click.
            if ((pointer - _pressAt).sqrMagnitude > sensitivity * (float)sensitivity) _cancelled = true;
            if (Input.GetMouseButtonUp(0))
            {
                if (!_cancelled && hit != null && hit.Pointer == _pressed && GameUi.Visible(hit.Field("graph_ShowBuff")))
                    hit.Call("OpenBuffInfo", (object?)null);
                _pressed = IntPtr.Zero;
            }
            else if (!Input.GetMouseButton(0)) _pressed = IntPtr.Zero;
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
        if (_layer == null || _layer.Get<bool>("isDisposed")) _layer = NativeUi.Component(parent, 0, 0);
        // Same space as com_AttrInfos; size only keeps hit testing of the owned plates unambiguous.
        if (_layer.Get<float>("width") != parent.Get<float>("width") || _layer.Get<float>("height") != parent.Get<float>("height"))
            _layer.Call("SetSize", parent.Get<float>("width"), parent.Get<float>("height"));
        _battleUi = battleUi;
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
                plate = new Plate(player, _layer!);
                Plates.Add(plate.Ui.Pointer, plate);
                Plugin.Diagnostics.Write($"field.slot{i} created");
            }
            Seen.Add(plate.Ui.Pointer);
            if (!plate.NameFontApplied)
                plate.NameFontApplied = FieldNameFont.Apply(plate.Ui.Field("txt_Name")!, battleUi);
            plate.Refresh(player, buffs, properties);
            EffectCount += plate.EffectCount;
            plate.Diagnose(player, _layer!, i);
        }
        Stale.Clear();
        foreach (var key in Plates.Keys) if (!Seen.Contains(key)) Stale.Add(key);
        foreach (var key in Stale)
        {
            GameUi.Dispose(Plates[key].Ui);
            Plates.Remove(key);
            Plugin.Diagnostics.Write("field removed: player-not-seen");
        }
        RefreshCounters(battleUi!);
    }

    // Mirrors the native per-frame plate pass for the owned layer: owner projection, side offset and lerp,
    // pairwise vertical separation, marker line and step-phase input. Monster plates stay native.
    private static readonly List<(float X, float Y, float Width, float Height)> Obstacles = new();

    private static void Layout()
    {
        var layer = _layer;
        if (Plates.Count == 0 || layer == null || layer.Get<bool>("isDisposed") || _battleUi == null) return;
        if (_sceneClass == IntPtr.Zero) _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
        var cameraObject = RuntimeObject.StaticField(_sceneClass, "inst")?.Get("mainCamera");
        if (cameraObject == null) return;
        var camera = new Camera(cameraObject.Pointer);
        var root = GameUi.Root!;
        float width = root.Get<float>("width"), height = root.Get<float>("height");
        var scale = FieldZoomUi.IndicatorScale;
        var marker = MarkerLift * scale * Math.Clamp(Plugin.FieldIndicatorHeight.Value, 0f, 1f);
        Placed.Clear();
        foreach (var plate in Plates.Values)
        {
            var ui = plate.Ui;
            var owner = ui.Call("GetOwnerPos")!.Value<Vector3>();
            var screen = owner == Vector3.zero || !camera.enabled ? default : camera.WorldToScreenPoint(owner);
            var local = screen.z > 0 ? layer.Call("GlobalToLocal", new Vector2(screen.x, Screen.height - screen.y))!.Value<Vector2>() : default;
            var target = local - Vector2.up * (PlateLift * scale);
            var shown = screen.z > 0 && target.x >= 0 && target.y >= 0 && target.x <= width && target.y <= height;
            if (ui.Get<bool>("visible") != shown) ui.Set("visible", shown);
            if (!shown) continue;
            ui.Call("UpdateOffset", target);
            Placed.Add((plate, local));
        }
        Separate();
        var self = _battleUi.Field("_SelfPlayer")?.Field("CharacterInst");
        var stepping = self != null && self.Get<int>("canStep") > 0;
        foreach (var (plate, owner) in Placed)
        {
            var point = layer.Call("TransformPoint", owner - Vector2.up * marker, plate.Ui)!.Value<Vector2>();
            plate.Ui.Call("UpdateGuideLine", point);
            var buffs = plate.Ui.Field("graph_ShowBuff")!;
            if (buffs.Get<bool>("visible") == stepping) buffs.Set("visible", !stepping);
            // Native fades plates over movement arrows; hero plates instead yield all input while moving.
            if (plate.Ui.Get<bool>("touchable") == stepping) plate.Ui.Set("touchable", !stepping);
        }
    }

    // Native pair rule: overlapping plates split the vertical overlap. Native monster plates are fixed obstacles.
    private static void Separate()
    {
        // Native monster plates never move during this pass; read their rectangles once instead of once per hero plate.
        Obstacles.Clear();
        var monsters = _battleUi!.Field("com_AttrInfos");
        for (int c = 0, count = monsters?.Get<int>("numChildren") ?? 0; c < count; c++)
        {
            var other = monsters!.Call("GetChildAt", c)!;
            if (other.TypeName == "UICom_AttrInfo" && other.Get<bool>("visible"))
                Obstacles.Add((other.Get<float>("x"), other.Get<float>("y"), other.Get<float>("width"), other.Get<float>("height")));
        }
        // Monster plates laid out by the zoom-follow layer this frame, in the same com_AttrInfos space.
        foreach (var other in FieldMonsterPlates.Plates)
            if (!other.Get<bool>("isDisposed") && other.Get<bool>("visible"))
                Obstacles.Add((other.Get<float>("x"), other.Get<float>("y"), other.Get<float>("width"), other.Get<float>("height")));
        for (var i = 0; i < Placed.Count; i++)
        {
            var a = Placed[i].Plate.Ui;
            for (var j = i + 1; j < Placed.Count; j++) Split(a, Placed[j].Plate.Ui);
            foreach (var other in Obstacles) Split(a, other);
        }
    }

    private static void Split(RuntimeObject a, RuntimeObject b)
    {
        float ay = a.Get<float>("y"), by = b.Get<float>("y");
        var overlapX = (a.Get<float>("width") + b.Get<float>("width")) * 0.5f - Math.Abs(a.Get<float>("x") - b.Get<float>("x"));
        var overlapY = (a.Get<float>("height") + b.Get<float>("height")) * 0.5f - Math.Abs(ay - by);
        if (overlapX <= 0 || overlapY <= 0) return;
        var shift = overlapY * 0.5f;
        var up = ay + 0.1f < by;
        a.Set("y", up ? ay - shift : ay + shift);
        b.Set("y", up ? by + shift : by - shift);
    }

    private static void Split(RuntimeObject a, (float X, float Y, float Width, float Height) fixedPlate)
    {
        var ay = a.Get<float>("y");
        var overlapX = (a.Get<float>("width") + fixedPlate.Width) * 0.5f - Math.Abs(a.Get<float>("x") - fixedPlate.X);
        var overlapY = (a.Get<float>("height") + fixedPlate.Height) * 0.5f - Math.Abs(ay - fixedPlate.Y);
        if (overlapX <= 0 || overlapY <= 0) return;
        a.Set("y", ay + 0.1f < fixedPlate.Y ? ay - overlapY : ay + overlapY);
    }

    private static void RefreshCounters(RuntimeObject battleUi)
    {
        CounterSeen.Clear(); CounterOwners.Clear();
        // Prefer the hero HP/buff clone; do not draw the same owner's badge again on its native state plate.
        foreach (var plate in Plates.Values) RefreshCounter(plate.Ui);
        foreach (var name in CounterParents)
        {
            var parent = battleUi.Field(name);
            if (!GameUi.Visible(parent)) continue;
            for (var i = 0; i < parent!.Get<int>("numChildren"); i++)
            {
                var ui = parent.Call("GetChildAt", i)!;
                if (!Plates.ContainsKey(ui.Pointer) && ui.TypeName is "UICom_AttrInfo" or "UICom_PlayerAttrInfo")
                    RefreshCounter(ui);
            }
        }
        foreach (var ui in FieldMonsterPlates.Plates) RefreshCounter(ui);
        Stale.Clear();
        foreach (var key in Counters.Keys) if (!CounterSeen.Contains(key)) Stale.Add(key);
        foreach (var key in Stale) { GameUi.Dispose(Counters[key].Icon); Counters.Remove(key); }
    }

    private static void RefreshCounter(RuntimeObject ui)
    {
        if (ui.Get<bool>("isDisposed") || !ui.Get<bool>("onStage")) return;
        var player = ui.Get("PlayerData"); // Local, already-synchronised owner lookup; no player-info window/RPC.
        if (player == null) return;
        CounterSeen.Add(ui.Pointer);
        Counters.TryGetValue(ui.Pointer, out var badge);
        if (badge != null && (badge.Owner != player.Pointer || badge.Icon.Get<bool>("isDisposed")))
        { GameUi.Dispose(badge.Icon); Counters.Remove(ui.Pointer); badge = null; }
        var shown = GameUi.Visible(ui) && player.Get("Property")?.Field("Counter")?.Get<bool>("Value") == true
            && CounterOwners.Add(player.Pointer);
        if (badge == null && !shown) return;
        var attr = ui.TypeName == "UICom_AttrInfo";
        var anchor = ui.Field(attr ? "txt_Name" : "loader_State")!;
        var x = attr ? ui.Field("graph_Guide")!.Get<float>("xMin") + 8 : anchor.Get<float>("xMin") + anchor.Get<float>("width") + 6;
        var y = anchor.Get<float>("yMin") + (anchor.Get<float>("height") - 36) / 2;
        if (!float.IsFinite(x + y)) shown = false;
        if (badge == null)
        {
            if (!shown) return;
            var icon = NativeUi.Icon(ui, "Com_Icon_Counter", x, y, 36)
                ?? throw new InvalidOperationException("필드 반격 아이콘 준비 대기");
            badge = new CounterBadge(icon, player.Pointer); Counters.Add(ui.Pointer, badge);
            badge.Position = (x, y);
        }
        if (shown && badge.Position != (x, y))
        {
            NativeUi.Position(badge.Icon, x, y, 36 / Math.Max(badge.Icon.Get<float>("width"), badge.Icon.Get<float>("height")));
            badge.Position = (x, y);
        }
        if (badge.Shown != shown) { badge.Icon.Set("visible", shown); badge.Shown = shown; }
    }

    internal static void Clear(string reason)
    {
        RestoreBuffPopup();
        foreach (var badge in Counters.Values) GameUi.Dispose(badge.Icon);
        Counters.Clear(); CounterSeen.Clear(); CounterOwners.Clear();
        Plugin.Diagnostics.State("field.gate", reason);
        if (Plates.Count > 0) Plugin.Diagnostics.Write($"field clear: {reason}; count={Plates.Count}");
        foreach (var plate in Plates.Values) GameUi.Dispose(plate.Ui);
        Plates.Clear(); Seen.Clear(); Stale.Clear(); Placed.Clear();
        GameUi.Dispose(_layer);
        _layer = _battleUi = null;
        EffectCount = 0;
        _parent = _pressed = IntPtr.Zero;
        _cancelled = false;
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
        // Native list already contains every public effect and owns its authored layout.
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
