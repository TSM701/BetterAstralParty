using UnityEngine;

namespace BetterAstralParty;

// Public, already-synchronised data only. Never opens the player-info window (which can send an RPC).
internal static class BattleStatusUi
{
    private static readonly List<Row> Rows = new();
    private static IntPtr _logicClass, _battleClass, _helperClass, _commonClass, _parent;
    private static RuntimeObject? _fight, _ui, _tooltip, _tooltipItem, _counter;
    private static NativeUi.Surface? _tooltipBackground;
    private static float _scanAt, _hoverAt, _tipRefreshAt;
    private static float _rootWidth, _rootHeight, _uiWidth, _uiHeight;
    private static string _tipText = "";
    private static IntPtr _hovered;
    private static int _difficulty;
    private static Camera? _camera;
    private static RuntimeObject? _counterIcon, _counterParent, _counterHeader;
    private static IntPtr _counterDefender;
    private static bool _counterAvailable, _counterShown;
    private static float _counterVisibleSince = -1;
    private static (float X, float Y, float Size, float Alpha)? _counterPosition;

    private sealed class Row
    {
        internal readonly RuntimeObject Panel, Hand, Page;
        private readonly RuntimeObject _handArea;
        private readonly NativeUi.Surface _background;
        internal readonly List<RuntimeObject> Icons = new();
        internal RuntimeObject ActorUi;
        internal readonly bool Attacker;
        internal int PageIndex, Count;
        private IntPtr _player;
        private bool _hasData;
        internal bool NeedsData => !_hasData || _hpDisplay == null;
        internal bool Visible;
        private int _inputOrder;

        internal void UpdateInputOrder(int step)
        {
            // Ready-stage card containers intercept hits even outside their visible art.
            // Raise only our rows within FightWindow, never above separate modal windows.
            // Keep SHOWTIME above them until the native card phase starts.
            var order = BattleStatusLayout.RowInputOrder(step);
            if (_inputOrder == order) return;
            _inputOrder = order;
            Panel.Set("sortingOrder", order);
            if (order == 0)
            {
                var parent = Panel.Get("parent")!;
                parent.Call("SetChildIndex", Panel, parent.Call("GetChildIndex", parent.Field("com_Show")!)!.Value<int>());
            }
        }
        private RuntimeObject? _hp, _hpDisplay, _hpHeart;
        private Vector3 _hpEdge;
        private float _width;
        private string _handText = "", _pageText = "";
        private readonly (bool Property, int Id, int Progress, int Rounds)?[] _effects = new (bool, int, int, int)?[BattleStatusLayout.PageSize];
        private readonly bool[] _iconVisible = new bool[BattleStatusLayout.PageSize];
        private (float X, float Y, float Scale)? _position;

        internal Row(RuntimeObject parent, RuntimeObject actor, bool attacker)
        {
            ActorUi = actor.Field("_UI")!; Attacker = attacker;
            Panel = NativeUi.Component(parent, BattleStatusLayout.Width, BattleStatusLayout.Height);
            try
            {
                Panel.Set("opaque", true);
                Panel.Set("visible", false);
                // Prepare during the intro, but keep its full-screen movie above our private rows.
                parent.Call("SetChildIndex", Panel, parent.Call("GetChildIndex", parent.Field("com_Show")!)!.Value<int>());
                _background = new NativeUi.Surface(Panel, BattleStatusLayout.Width, BattleStatusLayout.Height);
                _handArea = NativeUi.Component(Panel, BattleStatusLayout.CardWidth, BattleStatusLayout.Height);
                _handArea.Set("opaque", true);
                _handArea.Set("tooltips", ModText.Text("공개 손패 장수"));
                NativeUi.Icon(_handArea, "Com_Icon_Card", 8, 12, 28);
                Hand = NativeUi.Label(_handArea, "", 37, 10, 37, 32, 23);
                Page = NativeUi.Label(Panel, "", 0, 16, 50, 24, 15);
                Page.Set("touchable", true);
                Page.Set("tooltips", ModText.Text("마우스 휠로 효과 페이지 넘기기"));
                for (var i = 0; i < BattleStatusLayout.PageSize; i++)
                {
                    var icon = NativeUi.Create("Common_Internal", "Button_Buff")
                        ?? throw new InvalidOperationException("공식 버프 아이콘 준비 대기");
                    icon.Set("opaque", true);
                    icon.Set("visible", false);
                    var iconScale = BattleStatusLayout.IconSize / Math.Max(icon.Get<float>("width"), icon.Get<float>("height"));
                    icon.Call("SetScale", iconScale, iconScale);
                    Panel.Call("AddChild", icon);
                    Icons.Add(icon);
                }
            }
            catch { GameUi.Dispose(Panel); throw; }
        }

        internal void Refresh()
        {
            _background.Refresh();
            var player = ActorUi.Field(Attacker ? "attackerData" : "defenderData");
            var container = player?.Get("buffContainer");
            _hasData = false;
            if (player == null || container == null) { SetVisible(false); return; }
            _hasData = true;
            if (_player != player.Pointer) { PageIndex = 0; _player = player.Pointer; Array.Clear(_effects, 0, _effects.Length); }
            var hp = ActorUi.Field(Attacker ? "com_Attack" : "com_Defend")?.Field("txt_Life");
            if (_hp?.Pointer != hp?.Pointer)
            {
                _hp = hp;
                _hpDisplay = hp?.Get("displayObject");
                // Fight_Com_Defenser/n0 is the native heart; n27 has extra background padding.
                _hpHeart = Attacker ? null : hp?.Get("parent")?.Call("GetChild", "n0");
            }
            if (hp != null)
            {
                var textX = hp.Get<float>("x");
                _hpEdge = new Vector3(BattleStatusLayout.HpEdgeX(Attacker, textX, hp.Get<float>("width"),
                    _hpHeart?.Get<float>("x") ?? textX), hp.Get<float>("height"), 0);
            }
            // Same public filter as the official detail window, including public property effects.
            var shown = container.Call("GetShowBuffs", player, false)!;
            var buffs = shown.Field("Item1")!;
            var properties = shown.Field("Item2")!;
            var propertyCount = properties.Get<int>("Count");
            Count = propertyCount + buffs.Get<int>("Count");
            PageIndex = BattleStatusLayout.ClampPage(PageIndex, Count);
            var paged = Count > BattleStatusLayout.PageSize;
            var visibleCount = Math.Min(BattleStatusLayout.PageSize, Count - PageIndex * BattleStatusLayout.PageSize);
            var width = BattleStatusLayout.RowWidth(visibleCount, paged);
            var resized = _width != width;
            if (resized)
            {
                _width = width;
                Panel.Call("SetSize", width, BattleStatusLayout.Height);
                _background.Resize(width, BattleStatusLayout.Height);
                _handArea.Call("SetXY", Attacker ? width - BattleStatusLayout.EdgePadding - BattleStatusLayout.CardWidth : BattleStatusLayout.EdgePadding, 0f);
                Page.Call("SetXY", Attacker ? BattleStatusLayout.EdgePadding : width - BattleStatusLayout.EdgePadding - 50, 16f);
            }
            var cards = player.Get("cardContainer");
            var hand = cards == null ? "—" : cards.Get<int>("CardCount").ToString();
            if (_handText != hand) { _handText = hand; Hand.Set("text", hand); }
            var page = paged ? $"{PageIndex + 1}/{BattleStatusLayout.Pages(Count)}↕" : "";
            if (_pageText != page) { _pageText = page; Page.Set("text", page); }
            Page.Set("visible", paged);
            for (var i = 0; i < Icons.Count; i++)
            {
                var index = PageIndex * BattleStatusLayout.PageSize + i;
                var icon = Icons[i];
                var visible = index < Count;
                if (_iconVisible[i] != visible) { _iconVisible[i] = visible; icon.Set("visible", visible); }
                if (!visible) { _effects[i] = null; continue; }
                if (resized || _effects[i] == null) NativeUi.Position(icon, BattleStatusLayout.IconX(i, width, Attacker) + BattleStatusLayout.IconInset,
                    (BattleStatusLayout.Height - BattleStatusLayout.IconSize) / 2,
                    BattleStatusLayout.IconSize / Math.Max(icon.Get<float>("width"), icon.Get<float>("height")));
                var property = index < propertyCount;
                var data = property ? properties.Call("get_Item", index)! : buffs.Call("get_Item", index - propertyCount)!;
                var progress = property ? data.Field("property")!.Get<int>("Value") : data.Get<int>("Progress");
                var rounds = property ? 0 : data.Get<int>("KeepRound");
                var id = property ? data.Field("buffId")!.Value<int>() : data.Get<int>("BuffId");
                var state = (property, id, progress, rounds);
                if (_effects[i] == state) continue;
                _effects[i] = state;
                if (property) icon.Call("RefreshProperty", data, false); // No reactive subscriptions.
                else icon.Call("RefreshBuff", data);
                NativeUi.OutlineTree(icon);
                icon.SetField("data", BattleStatusLayout.Counters(progress, rounds, property));
            }
        }

        private Vector2? HpAnchor()
        {
            if (!_hasData || _camera == null || !_camera.enabled || _hpDisplay == null || !GameUi.Visible(_hp)) return null;
            // HP is world-space FairyGUI. Use its display transform, but explicitly project through
            // PKMainCamera (LocalToRoot on this world UI can select the wrong render camera).
            return NativeUi.ProjectBattlePoint(_ui!, _hpDisplay, _hpEdge, _camera);
        }

        internal void Position()
        {
            var anchor = HpAnchor();
            if (anchor == null) { SetVisible(false); return; }
            var scale = Math.Min(Plugin.UiScale.Value, _uiWidth / 1100f);
            var position = BattleStatusLayout.BelowHp(anchor.Value.x, anchor.Value.y, _width, scale,
                _uiWidth, _uiHeight, Attacker);
            if (position == null) { SetVisible(false); return; }
            if (_position != position)
            {
                _position = position;
                Panel.Call("SetScale", position.Value.Scale, position.Value.Scale);
                Panel.Call("SetXY", position.Value.X, position.Value.Y);
            }
            SetVisible(true);
        }

        private void SetVisible(bool visible)
        {
            if (Visible == visible) return;
            Visible = visible;
            Panel.Set("visible", visible);
        }
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed("BattleStatus")) return;
        try
        {
            if (!Plugin.BattleStatus.Value || !Application.isFocused || ModUi.IsOpen) { Clear(); return; }
            if (GameUi.HomeAvailable) RuntimeObject.RequireClasses("BattleStatus", ("GameLogic", "GameLogicManager"), ("Core.Scene", "BattleSceneController"), ("UI", "UIHelper"), ("UI", "CommonUIManager"));
            if (GameUi.Root == null) { Clear(); return; }
            if (_logicClass == IntPtr.Zero || _battleClass == IntPtr.Zero)
            {
                _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
                _battleClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
                _helperClass = RuntimeObject.FindClass("UI", "UIHelper");
                _commonClass = RuntimeObject.FindClass("UI", "CommonUIManager");
                if (_logicClass == IntPtr.Zero || _battleClass == IntPtr.Zero) return;
            }
            var logic = RuntimeObject.StaticField(_logicClass, "_inst");
            if (logic?.Get("fight")?.Field("fightStatus")?.Value<bool>() != true) { Clear(); return; }
            var room = logic.Get("room")?.Get("curRoomInfo");
            var map = room?.Field("info")?.Get<int>("MapType") ?? 0;
            if (!VisibleCombatAdvisor.IsPve(map)) { Clear(); return; }
            var director = RuntimeObject.StaticField(_battleClass, "inst")?.Field("directorManager");
            // Spectators can see DEFENSE before their local FightWindow reaches a choice step.
            // Follow the world-space header every frame, independently of the slower buff scan.
            RefreshCounter(director?.Get("victim")?.Field("_UI"));
            PositionCounter();
            // Do not spend another polling interval waiting to construct/bind the first spectator rows.
            if (Rows.Count != 2 || Rows.Any(row => row.NeedsData) || Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.2f;
                Scan(room!, map, director);
            }
            if (!GameUi.Visible(_fight)) { ClearRows(); return; }
            _rootWidth = GameUi.Root.Get<float>("width"); _rootHeight = GameUi.Root.Get<float>("height");
            _uiWidth = _ui!.Get<float>("width"); _uiHeight = _ui.Get<float>("height");
            var step = _ui.Field("step")!.Get<int>("selectedIndex");
            foreach (var row in Rows) { row.UpdateInputOrder(step); row.Position(); }
            Hover();
        }
        catch (Exception ex)
        {
            Compatibility.Block("BattleStatus", ex, Clear, EncounterCounterUi.Clear);
        }
    }

    private static void Scan(RuntimeObject room, int map, RuntimeObject? director)
    {
        var fight = GameUi.Visible(_fight) ? _fight : GameUi.Find(GameUi.Root!, "FightWindow");
        var ui = fight?.Get("contentPane");
        var step = ui?.Field("step")?.Get<int>("selectedIndex") ?? 0;
        if (!BattleStatusLayout.Allowed(Plugin.BattleStatus.Value, map, step)) { ClearRows(); return; }
        var atk = director?.Get("attacker");
        var def = director?.Get("victim");
        var camera = director?.Field("PKCamera")?.Field("PKMainCamera");
        var atkUi = atk?.Field("_UI");
        var defUi = def?.Field("_UI");
        // Hidden actors already have public data during the intro. Each row reveals with its own HP.
        if (camera == null || atkUi == null || defUi == null
            || atkUi.Get<bool>("isDisposed") || defUi.Get<bool>("isDisposed")) { ClearRows(); return; }
        _difficulty = room.Get<int>("Difficulty");
        if (_parent != ui!.Pointer || Rows.Count != 2 || Rows.Any(r => r.Panel.Get<bool>("isDisposed")))
        {
            ClearRows();
            _parent = ui.Pointer;
            _ui = ui; _fight = fight;
            Plugin.Diagnostics.Write($"battle status create begin; step={step}");
            Rows.Add(new Row(ui, atk!, true));
            Rows.Add(new Row(ui, def!, false));
            Plugin.Diagnostics.Write("battle status create complete");
            Plugin.Logger.LogInfo("[전투 공개 상태] 양쪽 공개 손패 장수·효과 아이콘 연결됨 (추가 서버 요청 없음)");
        }
        if (_camera == null || _camera.Pointer != camera.Pointer) _camera = new Camera(camera.Pointer);
        Rows[0].ActorUi = atkUi;
        Rows[1].ActorUi = defUi;
        foreach (var row in Rows) row.Refresh();
    }

    private static void RefreshCounter(RuntimeObject? actorUi)
    {
        if (actorUi == null || actorUi.Get<bool>("isDisposed")) { ClearCounter(); return; }
        var parent = actorUi.Field("com_Defend");
        var defender = actorUi.Field("defenderData");
        var header = parent?.Call("GetChild", "n24");
        if (_counterParent?.Pointer != parent?.Pointer || _counterHeader?.Pointer != header?.Pointer
            || _counterDefender != (defender?.Pointer ?? IntPtr.Zero) || _counterIcon?.Get<bool>("isDisposed") == true)
        {
            ClearCounter();
            _counterParent = parent; _counterHeader = header;
            _counterDefender = defender?.Pointer ?? IntPtr.Zero;
        }
        // Public Counter also tracks heroes via BattleProperty.OnCanCounterChanged.
        _counterAvailable = defender?.Get("Property")?.Field("Counter")?.Get<bool>("Value") == true;
        // Prepare while the native header is still hidden; PositionCounter owns its reveal.
        if (!_counterAvailable || parent == null || header == null) return;
        if (_counterIcon == null)
            _counterIcon = NativeUi.CounterIndicator(parent!);
    }

    private static void PositionCounter()
    {
        if (_counterIcon == null || _counterIcon.Get<bool>("isDisposed")) return;
        // n24 is a GImage, not a container. Follow its own visibility/alpha every frame
        // while sharing its parent transform; do not reparent or alter the native header.
        var visible = _counterAvailable && GameUi.Visible(_counterHeader)
            && _counterHeader!.Get<float>("alpha") > 0;
        if (!BattleStatusLayout.CounterReady(visible, Time.unscaledTime, ref _counterVisibleSince))
        {
            if (_counterShown)
            {
                _counterIcon.Call("GetTransition", "MaxPoint")!.Call("Stop", false, false);
                _counterIcon.Set("visible", false);
            }
            _counterShown = false;
            return;
        }
        var header = _counterHeader!;
        var size = 72 * Plugin.UiScale.Value * Math.Min(Math.Abs(header.Get<float>("scaleX")), Math.Abs(header.Get<float>("scaleY")));
        var anchor = BattleStatusLayout.CounterAnchor(header.Get<float>("xMin"), header.Get<float>("yMin"),
            header.Get<float>("width") * header.Get<float>("scaleX"), size);
        var position = (anchor.X, anchor.Y, size, header.Get<float>("alpha"));
        if (_counterPosition != position)
        {
            _counterPosition = position;
            _counterIcon.Call("SetScale", size / 160, size / 160);
            _counterIcon.Call("SetXY", anchor.X, anchor.Y);
            _counterIcon.Set("alpha", position.Item4);
        }
        if (!_counterShown)
        {
            _counterIcon.Set("visible", true);
            NativeUi.Play(_counterIcon, "MaxPoint");
            _counterShown = true;
        }
    }

    private static void ClearCounter()
    {
        GameUi.Dispose(_counterIcon);
        _counterIcon = _counterParent = _counterHeader = null;
        _counterDefender = IntPtr.Zero;
        _counterAvailable = _counterShown = false;
        _counterVisibleSince = -1;
        _counterPosition = null;
    }

    private static void Hover()
    {
        var root = GameUi.Root!;
        RuntimeObject? icon = null;
        Row? hoveredRow = null;
        // Native masks/card containers can own touchTarget in any phase, including spectators.
        // Inspect our visible geometry without changing native hit testing or forwarding clicks.
        // Separate windows/popups still occlude the fight; never show a tooltip through them.
        var topWindow = root.Call("GetTopWindow");
        var blocked = (topWindow != null && topWindow.Pointer != _fight?.Pointer
                && (topWindow.Get<bool>("modal") || GameUi.PointerPath().Any(hit => hit.Pointer == topWindow.Pointer)))
            || root.Get<bool>("modalWaiting") || root.Get<bool>("hasAnyPopup");
        var step = _ui?.Field("step")?.Get<int>("selectedIndex") ?? 0;
        if (!blocked)
        {
            var point = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!
                .Get<Vector2>("touchPosition");
            foreach (var row in Rows)
            {
                if (!row.Visible || !GameUi.Visible(row.Panel) || !ContainsPoint(row.Panel, point)) continue;
                hoveredRow = row;
                icon = row.Icons.FirstOrDefault(item => item.Get<bool>("visible") && ContainsPoint(item, point));
                break;
            }
        }
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("battle.buffHover", $"step={step}; blocked={blocked}; row={hoveredRow != null}; icon={icon != null}");
        if (hoveredRow != null && Input.mouseScrollDelta.y != 0)
        {
            hoveredRow.PageIndex = BattleStatusLayout.ClampPage(hoveredRow.PageIndex + (Input.mouseScrollDelta.y < 0 ? 1 : -1), hoveredRow.Count);
            hoveredRow.Refresh();
            icon = null;
        }
        var pointer = icon?.Pointer ?? IntPtr.Zero;
        if (_hovered != pointer) { _hovered = pointer; _hoverAt = Time.unscaledTime; _tipText = ""; _tipRefreshAt = 0; }
        if (icon == null || Time.unscaledTime - _hoverAt < 0.15f)
        {
            _tooltip?.Set("visible", false);
            return;
        }
        EnsureTooltip();
        if (Time.unscaledTime >= _tipRefreshAt)
        {
            _tipRefreshAt = Time.unscaledTime + 0.2f;
            var config = icon.Field("BuffConfig");
            if (config == null) { _tooltip?.Set("visible", false); _tipRefreshAt = 0; return; }
            var title = Local(config, "NameId");
            var description = Local(config, "DescId");
            var counters = icon.Field("data")?.String() ?? "";
            var content = title + "\n" + description + "\n" + counters + "\n" + _difficulty;
            if (_tipText != content)
            {
                _tipText = content;
                _tooltipItem!.Field("loader_Buff")!.Set("url", config.Get("Icon")!.String());
                _tooltipItem.Field("txt_Title")!.Set("text", title);
                var desc = _tooltipItem.Field("txt_Desc")!;
                RuntimeObject.StaticCall(_commonClass, "RefreshHyperlinkDesc", description, desc, _difficulty);
                ModFont.Track(desc);
                var bottom = Math.Max(80, desc.Get<float>("y") + desc.Get<float>("textHeight") + 12);
                _counter!.Call("SetXY", 14f, bottom + 14);
                _counter.Set("text", counters);
                _tooltip!.Call("SetSize", 390f, bottom + 60);
                _tooltipBackground!.Resize(390f, bottom + 60);
            }
        }
        _tooltipBackground!.Refresh();
        var anchor = icon.Call("LocalToRoot", Vector2.zero, root)!.Value<Vector2>();
        var scale = Math.Min(1f, Math.Min((root.Get<float>("width") - 24) / 390f,
            (root.Get<float>("height") - 24) / _tooltip!.Get<float>("height")));
        var width = 390 * scale; var height = _tooltip.Get<float>("height") * scale;
        var reveal = AdviceText.Reveal(Time.unscaledTime - _hoverAt - 0.15f);
        _tooltip.Call("SetScale", scale, scale);
        _tooltip.Call("SetXY", Math.Clamp(anchor.x - width / 2, 12, Math.Max(12, root.Get<float>("width") - width - 12)),
            Math.Clamp(anchor.y - height - 12, 12, Math.Max(12, root.Get<float>("height") - height - 12)));
        _tooltip.Set("alpha", reveal);
        _tooltip.Set("visible", true);
    }

    private static bool ContainsPoint(RuntimeObject item, Vector2 point)
    {
        // Display coordinates start at the rendered top-left. GObject conversions instead
        // subtract the anchor pivot, shifting centered buff buttons by half their size.
        // Use the native stage point/transform, including parent scale and animation.
        var local = item.Get("displayObject")!.Call("GlobalToLocal", point)!.Value<Vector2>();
        return local.x >= 0 && local.y >= 0 && local.x < item.Get<float>("width") && local.y < item.Get<float>("height");
    }

    private static string Local(RuntimeObject config, string property)
    {
        var id = config.Get<int>(property);
        return id <= 0 ? "" : RuntimeObject.StaticCall(_helperClass, "GetLocal", id, 7)?.String() ?? "";
    }

    private static void EnsureTooltip()
    {
        if (_tooltip != null && !_tooltip.Get<bool>("isDisposed")) return;
        _tooltip = NativeUi.Component(GameUi.Root!, 390, 210);
        _tooltip.Set("sortingOrder", 28000);
        _tooltip.Set("touchable", false);
        _tooltipBackground = new NativeUi.Surface(_tooltip, 390, 210);
        _tooltipItem = NativeUi.Create("Common", "Com_BuffInfoItem")
            ?? throw new InvalidOperationException("공식 버프 설명 UI 준비 대기");
        _tooltip.Call("AddChild", _tooltipItem);
        _tooltipItem.Call("SetXY", 14f, 14f);
        _tooltipItem.Set("touchable", false);
        foreach (var field in new[] { "txt_Title", "txt_Desc" })
        {
            var label = _tooltipItem.Field(field)!;
            GameUi.StyleText(label, field == "txt_Title" ? 24 : 21, align: 0);
            NativeUi.InheritText(label, dark: false); // This clone sits on our cream tooltip.
            label.Set("autoSize", 2); // Native wrapping, grow height instead of shrinking Korean text.
        }
        _counter = NativeUi.Label(_tooltip, "", 14, 170, 362, 30, 19);
    }

    internal static void Clear()
    {
        ClearCounter();
        ClearRows();
    }

    private static void ClearRows()
    {
        foreach (var row in Rows) GameUi.Dispose(row.Panel);
        Rows.Clear();
        GameUi.Dispose(_tooltip);
        _tooltip = _tooltipItem = _counter = _ui = _fight = null;
        _tooltipBackground = null;
        _parent = _hovered = IntPtr.Zero; _tipText = "";
        _camera = null;
    }
}
