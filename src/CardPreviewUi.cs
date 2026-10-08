using UnityEngine;

namespace BetterAstralParty;

// Read-only card/chip inspection. Never calls a selection action, chat sender or use-card window.
internal static class CardPreviewUi
{
    private static readonly Dictionary<string, int> Pings = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> RelicPings = new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr, RuntimeObject> PingTargets = new();
    private static readonly HashSet<IntPtr> LiveTargets = new();
    private static readonly List<IntPtr> StaleTargets = new();
    private static RuntimeObject? _popup, _card, _pingWindow;
    private static RuntimeObject? _relicLoad;
    private static bool _relicBound;
    private static (int Id, bool Relic, Vector2 Anchor)? _pending;
    private static IntPtr _helperClass, _commonClass, _logicClass, _configClass, _messageClass, _relicMessageClass, _settingsClass;
    private static IntPtr _room, _table, _relicTable, _pressed;
    private static NativeUi.Surface? _relicBackground;
    private static string _pressedText = "";
    private static int _language = -1, _shownId;
    private static bool _allowed, _pinned, _pressedPopup, _shownRelic;
    private static float _scanAt, _shownAt, _closingAt = -1, _closeAlpha = 1;
    private static Vector2 _anchor;
    private static (float X, float Y, float Scale, float Alpha)? _drawn;

    internal static void Tick()
    {
        if (!Compatibility.Allowed("CardPopups")) return;
        try
        {
            // Finish/observe the native load even if its click was cancelled or the setting is OFF.
            if (_relicLoad != null && _relicLoad.Get<bool>("IsCompleted"))
            {
                var load = _relicLoad;
                _relicLoad = null;
                if (load.Call("GetResult") == null) throw new InvalidOperationException("Relic UI package load failed");
                Plugin.Logger.LogInfo("[핑 확대] 칩 UI 로딩 완료");
            }
            Update();
        }
        catch (Exception ex)
        {
            // A changed game UI must not break the existing combat recommendations.
            Compatibility.Block("CardPopups", ex, Reset);
        }
    }

    private static void Update()
    {
        if (!Plugin.CardPopups.Value) { Reset(); return; }
        if (GameUi.HomeAvailable) RuntimeObject.RequireClasses("CardPopups", ("GameLogic", "GameLogicManager"), ("UI", "UIHelper"), ("UI", "CommonUIManager"));
        var root = GameUi.Root;
        if (root == null) return;
        var now = Time.unscaledTime;
        var scan = now >= _scanAt;
        if (scan)
        {
            _scanAt = now + 0.2f;
            if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            if (_logicClass == IntPtr.Zero) return;
            var room = RuntimeObject.StaticField(_logicClass, "_inst")?.Get("room")?.Get("curRoomInfo");
            var roomPointer = room?.Pointer ?? IntPtr.Zero;
            if (_room != roomPointer) { Reset(); _room = roomPointer; }
            _allowed = VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0);
        }
        if (!_allowed || ModUi.IsOpen || !Application.isFocused) { Reset(); return; }
        if (scan) UpdatePingTargets(root);

        RuntimeObject? ping = null;
        var insidePopup = false;
        var mouseDown = Input.GetMouseButtonDown(0);
        var mouseUp = Input.GetMouseButtonUp(0);
        if (mouseDown || mouseUp)
        {
            foreach (var current in GameUi.PointerPath())
            {
                if (current.Pointer == _popup?.Pointer) { insidePopup = true; break; }
                if (current.TypeName == "UIExpression_Com_ChatItem") ping = current;
            }
        }
        var pingText = ping?.Field("title")?.Get("text")?.String() ?? "";
        if (mouseDown)
        {
            _pressed = ping?.Pointer ?? IntPtr.Zero;
            _pressedText = pingText;
            _pressedPopup = insidePopup;
            if (!insidePopup && ping == null) BeginClose();
        }
        if (mouseUp)
        {
            if (ping != null && _pressed == ping.Pointer && _pressedText == pingText && LoadIndex())
            {
                var (relic, id) = CardPreviewLayout.Resolve(Pings, RelicPings, pingText);
                if (id > 0)
                {
                    if ((_pinned && _shownId == id && _shownRelic == relic)
                        || (_pending is { } waiting && waiting.Id == id && waiting.Relic == relic)) BeginClose();
                    else
                    {
                        BeginClose();
                        var mouse = Input.mousePosition;
                        _pending = (id, relic, new(mouse.x / Math.Max(1, Screen.width), 1 - mouse.y / Math.Max(1, Screen.height)));
                        Plugin.Logger.LogInfo($"[핑 확대] 클릭 / {(relic ? "칩" : "카드")} / 공개 ID={id}");
                    }
                }
            }
            else if (insidePopup && _pressedPopup) BeginClose();
            _pressed = IntPtr.Zero;
            _pressedPopup = false;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) BeginClose();
        if (_pending is { } request && (!request.Relic || RelicUiReady()))
        {
            _pending = null;
            Show(root, request.Id, request.Relic, request.Anchor);
        }
        Animate(root);
    }

    private static bool RelicUiReady()
    {
        if (_relicLoad != null) return false;
        var packageClass = RuntimeObject.FindClass("FairyGUI", "UIPackage");
        if (RuntimeObject.StaticCall(packageClass, "GetByName", "Relic") == null)
        {
            // Same package loader as BaseWindow.Load; no window, selection handlers or injected delegates.
            _relicLoad = RuntimeObject.StaticCall(packageClass, "AddPackageAsync", "Relic", "")!.Call("GetAwaiter")
                ?? throw new InvalidOperationException("Relic UI load awaiter unavailable");
            Plugin.Logger.LogInfo("[핑 확대] 칩 UI 로딩 시작 / 클릭 유지");
            return false;
        }
        if (!_relicBound)
        {
            // Native CreateInstance normally registers these extensions before creating the card.
            RuntimeObject.StaticCall(RuntimeObject.FindClass("UI", "UIRelicWindow"), "BindAll");
            _relicBound = true;
        }
        return true;
    }

    private static void UpdatePingTargets(RuntimeObject root)
    {
        var window = GameUi.Visible(_pingWindow) ? _pingWindow : GameUi.Find(root, "ExpressionListWindow");
        _pingWindow = window;
        var list = window?.Get("contentPane")?.Field("list_Plaform");
        var live = LiveTargets;
        live.Clear();
        if (list != null && LoadIndex())
        {
            var count = list.Get<int>("numChildren");
            for (var i = 0; i < count; i++)
            {
                var item = list.Call("GetChildAt", i)!;
                if (item.TypeName != "UIExpression_Com_ChatItem" || !GameUi.Visible(item)) continue;
                var title = item.Field("title")!;
                if (CardPreviewLayout.Resolve(Pings, RelicPings, title.Get("text")?.String() ?? "").Id <= 0) continue;
                live.Add(item.Pointer);
                if (!PingTargets.TryGetValue(item.Pointer, out var target) || target.Get<bool>("isDisposed"))
                {
                    // Native text/backgrounds may be non-hit-testable. Add only our own transparent target.
                    target = NativeUi.Component(item, 1, 1);
                    target.Set("opaque", true);
                    PingTargets[item.Pointer] = target;
                }
                target.Call("SetXY", title.Get<float>("x") - 8, title.Get<float>("y") - 4);
                target.Call("SetSize", title.Get<float>("width") + 16, title.Get<float>("height") + 8);
            }
        }
        StaleTargets.Clear();
        foreach (var key in PingTargets.Keys) if (!live.Contains(key)) StaleTargets.Add(key);
        foreach (var key in StaleTargets)
        {
            GameUi.Dispose(PingTargets[key]);
            PingTargets.Remove(key);
        }
    }

    private static bool LoadIndex()
    {
        if (_configClass == IntPtr.Zero)
        {
            _configClass = RuntimeObject.FindClass("", "StaticConfigure");
            _helperClass = RuntimeObject.FindClass("UI", "UIHelper");
            _commonClass = RuntimeObject.FindClass("UI", "CommonUIManager");
            _messageClass = RuntimeObject.FindClass("GameLogic", "BattleCardMessage");
            _relicMessageClass = RuntimeObject.FindClass("GameLogic", "BattleRelicMessage");
            _settingsClass = RuntimeObject.FindClass("Core", "GameSettings");
        }
        var table = RuntimeObject.StaticCall(_configClass, "get_Card")?.Get("InfoDict");
        if (table == null || table.Get<int>("Count") == 0) return false;
        var relicTable = RuntimeObject.StaticCall(_configClass, "get_Relic")?.Get("InfoDict");
        var language = RuntimeObject.StaticField(_settingsClass, "languageType")!.Value<int>();
        if (_table == table.Pointer && _relicTable == (relicTable?.Pointer ?? IntPtr.Zero) && _language == language) return true;
        Index(Pings, table, _messageClass, "GetCardMsg");
        RelicPings.Clear();
        if (relicTable != null) Index(RelicPings, relicTable, _relicMessageClass, "GetRelicMsg");
        _table = table.Pointer; _relicTable = relicTable?.Pointer ?? IntPtr.Zero; _language = language;
        Plugin.Logger.LogInfo($"[핑 확대] 카드 {Pings.Count}개·칩 {RelicPings.Count}개 연결됨");
        return true;
    }

    private static void Index(Dictionary<string, int> index, RuntimeObject table, IntPtr messageClass, string method)
    {
        index.Clear();
        var entries = table.Call("System.Collections.IDictionary.GetEnumerator")!;
        while (entries.Call("MoveNext")!.Value<bool>())
        {
            var config = entries.Get("Value")!;
            var id = config.Get<int>("Id");
            // The game's own formatter handles the installed Korean patch and colour tags.
            var ping = RuntimeObject.StaticCall(messageClass, method, id)?.String() ?? "";
            CardPreviewLayout.AddUnique(index, ping, id);
        }
    }

    private static string Local(RuntimeObject config, string field, int stringType = 9)
    {
        var id = config.Get<int>(field);
        return id <= 0 ? "" : RuntimeObject.StaticCall(_helperClass, "GetLocal", id, stringType)?.String() ?? "";
    }

    private static void Show(RuntimeObject root, int id, bool relic, Vector2 anchor)
    {
        var config = RuntimeObject.StaticCall(_helperClass, relic ? "GetRelicInfoConfigure" : "GetCardConfigure", id);
        if (config == null) return;
        if (_popup == null || _popup.Get<bool>("isDisposed") || _shownRelic != relic)
        {
            GameUi.Dispose(_popup);
            _relicBackground = null;
            _popup = NativeUi.Component(root, CardPreviewLayout.Width, CardPreviewLayout.Height);
            _popup.Set("sortingOrder", 29000);
            _popup.Set("visible", false);
            _popup.Set("opaque", true);
            _card = NativeUi.Create(relic ? "Relic" : "Common", relic ? "Relic_Button_Item" : "Com_Card")
                ?? throw new InvalidOperationException("Required ping UI resource unavailable");
            if (relic)
            {
                _popup.Call("SetSize", _card.Get<float>("width"), _card.Get<float>("height"));
                _relicBackground = new NativeUi.Surface(_popup, _card.Get<float>("width"), _card.Get<float>("height"),
                    // Relic/RelicWindow: black n4 (102/255) under n2 (204/255), combined alpha 0.88.
                    fill: new Color(0f, 0f, 0f, 0.88f));
            }
            _popup.Call("AddChild", _card);
            NativeUi.Position(_card, 0f, 0f, 1f);
            _card.Set("touchable", false);
        }
        // Base public card art: no alternate-art ownership lookup or other player's hand access.
        if (relic) RenderRelic(config);
        else RuntimeObject.StaticCall(_commonClass, "RendererCard", _card, config.Call("GetCardView", 0),
            Local(config, "NameID"), Local(config, "DescId"), Local(config, "CardNumb"), Local(config, "CommentId"),
            config.Get<int>("Cost"), config.Get<int>("CardType"), config.Get<int>("CardTargetType"), true, false);
        NativeUi.OutlineTree(_card!);
        _popup.Set("touchable", true);
        _pinned = true; _shownId = id; _shownRelic = relic; _shownAt = Time.unscaledTime; _closingAt = -1;
        _drawn = null;
        _anchor = anchor;
        _popup.Set("visible", true);
        // Optional native description reveal; wrapper fade/slide covers packages without this transition.
        NativeUi.Play(_card!, relic ? "cutIn" : "ShowDescState");
        Plugin.Logger.LogInfo($"[핑 확대] {(relic ? "칩" : "카드")} / 공개 ID={id}");
    }

    private static void RenderRelic(RuntimeObject config)
    {
        var title = _card!.Field("txt_tltle")!;
        var description = _card.Field("txt_Desc")!;
        title.Set("text", Local(config, "NameID", 46));
        description.Set("text", Local(config, "DescID", 46));
        NativeUi.InheritText(title, dark: true);
        NativeUi.InheritText(description, dark: true); // Native light text; retain inline emphasis colours.
        _card.Field("loader_Icon")!.Set("url", config.Get("Icon")!.String());
        _card.Field("loader_Frame")!.Field("qualityType")!.Set("selectedIndex", config.Get<int>("RelicQualityType"));
        _card.Field("status")!.Set("selectedIndex", 0);
        _card.Field("RecommendType")!.Set("selectedIndex", 0);
        for (var i = 0; i < _card.Get<int>("numChildren"); i++)
        {
            var child = _card.Call("GetChildAt", i)!;
            if (child.Field("name")!.String() is not ("loader_Frame" or "loader_Icon" or "txt_tltle" or "txt_Desc"))
                child.Set("visible", false); // No recommendation stamps, selection UI or looping star effects.
        }
        // Only public artwork/text; never call the choice window renderer, which binds selection actions.
        description.Set("autoSize", 2);
        var height = Math.Max(_card.Get<float>("height"), description.Get<float>("y") + description.Get<float>("textHeight") + 24);
        _popup!.Call("SetSize", _card.Get<float>("width"), height);
        _relicBackground!.Resize(_card.Get<float>("width"), height);
    }

    private static void BeginClose()
    {
        _pending = null;
        if (_popup == null || _popup.Get<bool>("isDisposed") || !_popup.Get<bool>("visible") || _closingAt >= 0) return;
        _closingAt = Time.unscaledTime;
        _closeAlpha = _popup.Get<float>("alpha");
        _pinned = false;
        _popup.Set("touchable", false);
    }

    private static void Animate(RuntimeObject root)
    {
        if (_popup == null || _popup.Get<bool>("isDisposed") || !_popup.Get<bool>("visible")) return;
        var width = root.Get<float>("width"); var height = root.Get<float>("height");
        var layout = CardPreviewLayout.Place(width, height, _anchor.x * width, _anchor.y * height, Plugin.UiScale.Value,
            _popup.Get<float>("width"), _popup.Get<float>("height"));
        _relicBackground?.Refresh();
        var reveal = _closingAt < 0 ? AdviceText.Reveal(Time.unscaledTime - _shownAt, 0.18f)
            : _closeAlpha * (1 - AdviceText.Reveal(Time.unscaledTime - _closingAt, 0.12f));
        var drawn = (layout.X, layout.Y + (1 - reveal) * 10 * layout.Scale, layout.Scale, reveal);
        if (_drawn != drawn)
        {
            _drawn = drawn;
            _popup.Call("SetScale", layout.Scale, layout.Scale);
            _popup.Call("SetXY", drawn.Item1, drawn.Item2);
            _popup.Set("alpha", reveal);
        }
        if (_closingAt >= 0 && reveal <= 0) _popup.Set("visible", false);
    }

    internal static void Reset()
    {
        _pending = null; // The shared native package load may finish; it must not reopen a cancelled click.
        foreach (var target in PingTargets.Values) GameUi.Dispose(target);
        PingTargets.Clear();
        LiveTargets.Clear(); StaleTargets.Clear();
        GameUi.Dispose(_popup);
        _popup = _card = _pingWindow = null;
        _relicBackground = null;
        _pinned = _pressedPopup = false; _shownId = 0; _closingAt = -1;
        _pressed = IntPtr.Zero; _pressedText = "";
    }
}
