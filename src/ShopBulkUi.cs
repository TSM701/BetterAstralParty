using UnityEngine;

namespace BetterAstralParty;

// 상점 일괄 구매 / Shop Bulk Purchase. Design: draft D of artifacts/cc/shop-bulk-drafts v6 (user-chosen 2026-11-13, replacing
// the five-chip row of 1.9.2-dev.3/dev.4, whose small chips and corner pick boxes read poorly in game), placed as draft I1 of v7
// (user-chosen the same day): two buttons as wide as a goods card on the last two card columns of the Star Coin shop on the
// shopping screen (ProductRecommendationPanel), in the panel's own space under the cards. 무료 일괄 수령 claims the free goods
// at once, 일괄 구매 opens a cream basket card listing every buyable goods with a checkbox; each is gray while it has nothing
// to act on. Presets (무료, 배지, 50% 이상 할인, 전부) are independent toggles that add or remove their own goods, a row ticks
// itself, and only the card's 구매 spends Star Coins; the purchase then runs inside the card (each row's state, a bar, 중지; draft K
// of v8) and the card returns to its pick state, the result shown in its footer for a moment. The card pops in and fades out (draft MB). Each purchase is the native buy button's own request, StoreLogic.RequestMallBuy(2, goodsId, 1, 0, 0)
// (PurchaseWindow.OnBuyGoodsByToken for shop tab 2), sent one at a time after the previous reply; any failure stops the rest
// and nothing is retried. The game's reply handler refreshes the shelf itself (refreshCurShelf).
internal static class ShopBulkUi
{
    internal const string Feature = "ShopBulkBuy";
    private const int StarShopTab = 2, StarCoin = 1, BadgeType = 17;
    private const int RewardTitle = 1008; // RewardWindow's common reward title (BagLogic's TryShowAsync(0, 1008), OnMohuClick)
    // The panel draws its 1920 x 1080 design 1:1 at 1080p; offsets are from the panel art n21 (星币商店, at 1148, 141). Its
    // right part (from the step at x 1395) reaches y 697, so the cards (columns at 1212, 1372, 1532, 1692, 136 wide, bottom
    // 647) leave room for a 36-high row at 656 on the last two columns.
    private const float RowDy = 515f, ShelfW = 136f, ShelfH = 36f, FreeDx = 384f, BulkDx = 544f, ButtonH = 44f;
    // The basket card is centred on the goods grid (1520, 430).
    private const float CardW = 630f, CardCx = 372f, CardCy = 289f, RowsTop = 116f, FooterH = 84f;
    private static readonly Color Yellow = new(1f, 201f / 255, 15f / 255, 1f), Green = new(122f / 255, 214f / 255, 54f / 255, 1f);
    private static readonly Color Gray = new(190f / 255, 186f / 255, 178f / 255, 1f), Rim = new(112f / 255, 112f / 255, 112f / 255, 1f);
    private static readonly Color Cream = new(1f, 251f / 255, 241f / 255, 1f), Tan = new(200f / 255, 186f / 255, 150f / 255, 1f);
    private static readonly Color Ink = new(34f / 255, 30f / 255, 26f / 255, 1f), Muted = new(140f / 255, 132f / 255, 112f / 255, 1f);
    private static readonly Color Red = new(220f / 255, 50f / 255, 50f / 255, 1f), Picked = new(1f, 244f / 255, 196f / 255, 1f);
    private static readonly Color Track = new(58f / 255, 0f, 120f / 255, 1f), ThumbBack = new(236f / 255, 226f / 255, 1f, 1f);
    private static readonly Color Dim = new(20f / 255, 0f, 40f / 255, 0.5f), Shadow = new(40f / 255, 0f, 80f / 255, 1f);
    private static readonly Color HoverFill = new(1f, 249f / 255, 228f / 255, 1f), HoverLine = new(230f / 255, 214f / 255, 160f / 255, 1f);
    private static readonly Color Flat = new(238f / 255, 234f / 255, 224f / 255, 1f), FlatText = new(178f / 255, 170f / 255, 150f / 255, 1f);
    private static readonly Color FlatLine = new(200f / 255, 196f / 255, 188f / 255, 1f);
    private static readonly Color Rule = new(186f / 255, 168f / 255, 124f / 255, 1f); // the card's dividers (Tan read too faint)
    private const float CheckIdle = 0.22f; // the open box's check: black at this alpha
    // Draft MB (user-chosen 2026-11-13, artifacts/cc/shop-bulk-drafts v8): the game's window fade, shorter, with the card
    // growing from 92 % with a slight overshoot; closing fades it to 96 %.
    private const float OpenTime = 0.22f, CloseTime = 0.16f;

    private sealed class Goods
    {
        internal int Id, Price, Original; internal bool Badge; internal string Name = "", Icon = "";
        internal bool Free => Price == 0;
        internal bool Half => Price > 0 && Price * 2 <= Original;
    }

    private enum CardMode { Pick, Buying }

    private sealed class Batch
    {
        internal readonly Queue<(int Id, int Price)> Items = new();
        internal readonly HashSet<int> Ids = new(), DoneIds = new();
        internal int Count, Done, Spent, Current = -1, CurrentPrice;
        internal bool Free, FromCard, Stop, Bad; internal RuntimeObject? Reply; internal string Result = "";
    }

    // The screen's own button look (its 갱신 and 방 생성): a gray rim, a thick black outline, a flat fill and the shop title's
    // face. Scaled about its centre for hover and press feedback.
    private sealed class Sticker
    {
        internal readonly RuntimeObject Root, Body, Label;
        internal Sticker(RuntimeObject parent, float w, float h, int size)
        {
            Root = NativeUi.Component(parent, w + 6f, h + 6f);
            Root.Set("opaque", true); // the hit area; presses are read from the hit path, no listener is added
            Root.Call("SetPivot", 0.5f, 0.5f, false);
            var r = h / 2;
            SmoothShape.Create(Root, 0f, 0f, w + 6f, h + 6f, Rim, r + 3f, r + 3f, r + 3f, r + 3f);
            Body = SmoothShape.Create(Root, 3f, 3f, w, h, Yellow, r, r, r, r, 4f, Color.black);
            Label = NativeUi.Label(Root, "", 3f, 3f, w, h, size);
            Face(Label);
            Label.Set("align", 1); Label.Set("verticalAlign", 1); Label.Set("singleLine", true);
        }
        internal void Show(string text, Color fill)
        {
            SmoothShape.Fill(Body, fill);
            if (Label.Get("text")?.String() != text) Label.Set("text", text);
            if (!Root.Get<bool>("visible")) Root.Set("visible", true);
        }
        internal bool Hit(IntPtr pointer) => Root.Pointer == pointer && Root.Get<bool>("visible");
    }

    // Draft I1: a button as wide as a goods card on a card column, with the card's corners and drop shadow, so it reads as
    // part of the shelf. The label is the shop title's face, shrunk to fit.
    private sealed class ShelfButton
    {
        internal readonly RuntimeObject Root, Body, Label;
        internal ShelfButton(RuntimeObject parent)
        {
            Root = NativeUi.Component(parent, ShelfW + 3f, ShelfH + 4f);
            Root.Set("opaque", true); // the hit area; presses are read from the hit path, no listener is added
            Root.Call("SetPivot", 0.5f, 0.5f, false);
            SmoothShape.Create(Root, 3f, 4f, ShelfW, ShelfH, Shadow, 10f, 10f, 10f, 10f);
            Body = SmoothShape.Create(Root, 0f, 0f, ShelfW, ShelfH, Yellow, 10f, 10f, 10f, 10f, 3f, Color.black);
            Label = NativeUi.Label(Root, "", 5f, 1f, ShelfW - 10f, ShelfH, 18);
            Face(Label);
            Label.Set("align", 1); Label.Set("verticalAlign", 1); Label.Set("singleLine", true); Label.Set("autoSize", 3);
        }
        internal void Show(string text, Color fill)
        {
            SmoothShape.Fill(Body, fill);
            if (Label.Get("text")?.String() != text) Label.Set("text", text);
            if (!Root.Get<bool>("visible")) Root.Set("visible", true);
        }
        internal bool Hit(IntPtr pointer) => Root.Pointer == pointer;
    }

    // A preset capsule on the basket card, an independent toggle (user-decided 2026-11-13, replacing cumulative tiers that
    // replaced the ticks): pressed, it ticks its own goods, or unticks them when all already are; it shows on (yellow) while
    // every goods of it is ticked. No count: a preset with nothing to tick is flat and borderless.
    private sealed class Preset
    {
        internal readonly RuntimeObject Root, Shape, Label; internal readonly int Tier;
        internal Preset(RuntimeObject parent, int tier, string text, float x, float y)
        {
            Tier = tier;
            Root = NativeUi.Component(parent, 200f, 34f);
            Root.Set("opaque", true);
            Label = Text(Root, text, 0f, 1f, 200f, 34f, 16, Ink);
            var w = Label.Get<float>("textWidth") + 30f;
            Root.Call("SetSize", w, 34f); Root.Call("SetXY", x, y); Root.Call("SetPivot", 0.5f, 0.5f, false);
            Label.Call("SetSize", w, 34f); Label.Set("align", 1); Label.Set("verticalAlign", 1);
            Shape = SmoothShape.Create(Root, 0f, 0f, w, 34f, Cream, 17f, 17f, 17f, 17f, 2.5f, Tan);
            Root.Call("SetChildIndex", Shape, 0);
        }
    }

    private sealed class Row
    {
        internal int Id; internal RuntimeObject Root = null!, Back = null!, Box = null!, Disc = null!, Check = null!, Spinner = null!;
        internal readonly List<RuntimeObject> Parts = new(); // faded while a purchase leaves this goods out
    }

    private static readonly List<Goods> Shelf = new();
    private static readonly HashSet<int> Chosen = new();
    private static readonly List<Preset> Presets = new();
    private static readonly List<Row> Rows = new();
    private static RuntimeObject? _ui, _layer, _face;
    private static RuntimeObject? _dim, _card, _close, _sumCount, _sumCoin, _sumAmount, _held, _heldCoin, _cardBar, _cardBarFill;
    private static Sticker? _buy, _cancel;
    private static ShelfButton? _free, _bulk;
    private static Batch? _batch, _cardBatch;
    private static CardMode _mode;
    private static int _coins;
    private static string _cardIds = "";
    private static IntPtr _down;
    private static float _scanAt, _readAt, _resultUntil, _mergeUntil, _openedAt, _closingAt = -1f, _shownAlpha = -1f, _shownScale = -1f, _shownDim = -1f;
    private static bool _mergeOff;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        // The OFF path's cleanup is inside the breaker too: its failure stops only this feature, never the shared UI.
        try
        {
            if (!Plugin.ShopBulkBuy.Value) { Clear(); return; }
            Update();
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update()
    {
        // Looked for four times a second until the shop shows, every frame while it does.
        if (_ui == null && Time.unscaledTime < _scanAt) return;
        _scanAt = Time.unscaledTime + 0.25f;
        var root = GameUi.Root;
        var panel = root == null || PvpSafety.Suspended ? null
            : RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst")?.Get("currentPanel");
        var ui = panel?.TypeName == "ProductRecommendationPanel" ? panel.Get("ui") : null;
        var list = ui?.Field("com_Goods")?.Field("list_Goods");
        var art = ui?.Call("GetChild", "n21");
        if (ui == null || art == null || list == null) { Clear(); return; }
        if (_ui?.Pointer != ui.Pointer || _layer == null || _layer.Get<bool>("isDisposed")) { Clear(); Create(ui); }
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst")!;
        // The list away while the shop panel stays (2026-11-13 review): hidden, and a running purchase only collects its reply
        // and waits, so it goes on when the list returns. Leaving the panel ends it.
        var shown = GameUi.Visible(list);
        if (_layer!.Get<bool>("visible") != shown) _layer.Set("visible", shown);
        if (!shown) { Run(logic, true); _down = IntPtr.Zero; CloseCard(); return; }
        if (Time.unscaledTime >= _readAt) Read(panel!, logic);
        // A native window on top (the purchase window or the guide) or the mod settings pause everything here. It ends a pick;
        // a purchase keeps its card and only waits. The game's reward window, which each purchase opens or adds to
        // (BagLogic.OnBagItemChangeS2CServerCallBack -> RewardWindow.ShowReward), neither pauses a purchase nor closes the card,
        // and while our purchases run its queued rewards join the one on screen (user-requested 2026-11-13: every item showed its
        // own window, and the next purchase waited until it was closed). Presses still wait under it.
        // The window holding the keys, not GRoot's top window: the SystemTipsWindow overlay stays on GRoot for the session once a
        // tip has shown, and paused every purchase after it (2026-11-14, see GameUi.FocusWindow).
        var top = GameUi.FocusWindow();
        var blocked = ModUi.IsOpen || top != null || root!.Get<bool>("hasModalWindow") || root.Get<bool>("modalWaiting");
        var reward = top?.TypeName == "RewardWindow" && !ModUi.IsOpen && !root!.Get<bool>("modalWaiting");
        if (reward && Time.unscaledTime < _mergeUntil) MergeRewards(top!);
        var halted = blocked && !reward;
        Run(logic, halted);
        if (halted && _mode == CardMode.Pick) CloseCard();
        Layout(art, blocked);
        if (blocked) _down = IntPtr.Zero;
        else HandleInput();
    }

    // The shelf the panel shows (its own GoodsDatas), only goods still buyable with Star Coins from shop tab 2.
    private static void Read(RuntimeObject panel, RuntimeObject logic)
    {
        _readAt = Time.unscaledTime + 0.2f;
        Shelf.Clear();
        var datas = panel.Field("GoodsDatas");
        var count = datas?.Get<int>("Count") ?? 0;
        for (var i = 0; i < count; i++)
        {
            var g = datas!.Call("get_Item", i);
            if (g == null || g.Field<int>("shopTabType") != StarShopTab || g.Field<int>("currencyID") != StarCoin) continue;
            if (g.Result<bool>("SellOut") || g.Result<bool>("IsOwn") || g.Result<int>("RemainingNum") <= 0) continue;
            var item = g.Field("itemConfig");
            Shelf.Add(new Goods
            {
                Id = g.Field<int>("goodsId"), Price = g.Get<int>("SalePrice"), Original = g.Result<int>("GetOriginalPrice"),
                Badge = item?.Get<int>("ItemType") == BadgeType, Name = g.Call("GetName")?.String() ?? "",
                Icon = item?.Get("ShowIcon")?.String() ?? "" // the goods cell's own loader_Goods url (UIProductRecommendation_Button_GoodsItem.InitData)
            });
        }
        Chosen.RemoveWhere(id => Shelf.All(g => g.Id != id));
        _coins = logic.Get("bag")?.Result<int, int>("GetItemCount", StarCoin) ?? 0;
    }

    // A preset's goods: 1 free, 2 badges, 3 50 % off or more, 4 everything. Free first, then badges, then by price.
    private static List<Goods> Tier(int tier) => Order(Shelf.Where(g => tier switch
    {
        1 => g.Free,
        2 => g.Badge,
        3 => g.Half,
        _ => true
    }));

    private static List<Goods> Order(IEnumerable<Goods> goods) => goods.OrderBy(g => g.Price > 0).ThenBy(g => !g.Badge).ThenBy(g => g.Price).ToList();

    private static void Start(List<Goods> goods, bool fromCard)
    {
        var batch = new Batch { Count = goods.Count, Free = goods.All(g => g.Free), FromCard = fromCard };
        foreach (var g in goods) { batch.Items.Enqueue((g.Id, g.Price)); batch.Ids.Add(g.Id); }
        _batch = batch;
        _mergeUntil = float.MaxValue; // until a moment after it ends: the last reward may come after the last reply
        if (fromCard) { _cardBatch = batch; _mode = CardMode.Buying; } // the ticks stay: those not bought are still ticked after
        Plugin.Diagnostics.Write(FormattableString.Invariant($"shopBulk.start count={goods.Count}; total={goods.Sum(g => g.Price)}; card={fromCard}"));
    }

    // One request at a time; a reply that failed or timed out stops the rest, and nothing is sent twice. 중지 stops before
    // the next request, never in the middle of one.
    private static void Run(RuntimeObject logic, bool blocked)
    {
        var batch = _batch;
        if (batch == null) return;
        if (batch.Reply != null)
        {
            if (!batch.Reply.Field<bool>("isCompleted")) return;
            var failed = batch.Reply.Field<bool>("isTimeOut") || batch.Reply.Field<int>("errId") != 0;
            Plugin.Diagnostics.Write(FormattableString.Invariant($"shopBulk.reply err={batch.Reply.Field<int>("errId")}; timeout={batch.Reply.Field<bool>("isTimeOut")}"));
            batch.Reply = null;
            _readAt = 0;
            if (failed) { batch.Current = -1; Finish(batch, "failed"); return; }
            batch.Done++; batch.Spent += batch.CurrentPrice; batch.DoneIds.Add(batch.Current); batch.Current = -1;
        }
        if (batch.Stop) { Finish(batch, "stopped"); return; }
        if (blocked) return;
        while (batch.Items.Count > 0)
        {
            var (id, price) = batch.Items.Dequeue();
            var goods = Shelf.FirstOrDefault(g => g.Id == id);
            // Gone or repriced since the press (a refresh or a manual buy): skipped, never bought at another price.
            if (goods == null || goods.Price != price) { batch.Count--; batch.Ids.Remove(id); continue; }
            if (_coins < price) { Finish(batch, "failed"); return; }
            batch.Reply = logic.Get("store")!.Call("RequestMallBuy", StarShopTab, id, 1, 0, 0)
                ?? throw new InvalidOperationException("StoreLogic.RequestMallBuy returned no result");
            batch.Current = id; batch.CurrentPrice = price;
            _coins -= price;
            Plugin.Diagnostics.Write(FormattableString.Invariant($"shopBulk.buy goods={id}; price={price}"));
            return;
        }
        Finish(batch, "done");
    }

    private static void Finish(Batch batch, string outcome)
    {
        _batch = null;
        _mergeUntil = Time.unscaledTime + 3f;
        batch.Bad = outcome == "failed";
        // Only a failure keeps a line, in the card's footer (user-decided 2026-11-14: "n개 받음" and "n개 구매 완료" lines are gone,
        // and the shelf buttons never give way to a progress or result line).
        batch.Result = batch.Bad ? Words($"구매가 멈췄습니다 {batch.Done}/{batch.Count}", $"Purchase stopped {batch.Done}/{batch.Count}") : "";
        if (batch.FromCard && _card != null && _cardBatch == batch)
        {
            // Back to the pick state (user-decided 2026-11-13: dev.8's result screen had to be closed and the card reopened to
            // buy more). The goods bought leave the list with the shelf refresh and those not bought stay ticked.
            _mode = CardMode.Pick;
            Chosen.ExceptWith(batch.DoneIds);
            _resultUntil = batch.Bad ? Time.unscaledTime + 4f : 0f;
        }
        Plugin.Diagnostics.Write(FormattableString.Invariant($"shopBulk.done outcome={outcome}; bought={batch.Done}; count={batch.Count}; spent={batch.Spent}"));
    }

    // The reward window keeps each purchase's items as one list in rewardsDataDic[0].showQueue and shows the next one per press
    // on it (ShowCommonReward). The lists queued behind the one on screen are folded into it, which ShowCommonReward then
    // redraws, so a bulk purchase ends on one window with every item. A failure stops only the folding for the session.
    private static void MergeRewards(RuntimeObject window)
    {
        if (_mergeOff || !window.Get<bool>("isShowing")) return;
        try
        {
            var data = window.Field("rewardsDataDic")?.Call("get_Item", 0); // type 0, the common rewards
            var queue = data?.Field("showQueue");
            var shown = data?.Field("itemList");
            var queued = queue?.Get<int>("Count") ?? 0;
            if (queued == 0 || shown == null || shown.Get<int>("Count") == 0) return;
            var merged = queue!.Call("Dequeue") ?? throw new InvalidOperationException("RewardsData.showQueue.Dequeue returned nothing");
            merged.Call("InsertRange", 0, shown); // what is on screen first, then each later purchase's items
            for (var i = 1; i < queued; i++) merged.Call("AddRange", queue.Call("Dequeue"));
            queue.Call("Enqueue", merged);
            window.Call("ShowCommonReward", RewardTitle);
            Plugin.Diagnostics.Write(FormattableString.Invariant($"shopBulk.rewardMerge lists={queued + 1}; items={merged.Get<int>("Count")}"));
        }
        catch (Exception ex) { _mergeOff = true; Plugin.Diagnostics.Error("ShopBulk.rewardMerge", ex); }
    }

    private static string Words(string korean, string english) => ModText.Korean ? korean : english;

    private static void Create(RuntimeObject ui)
    {
        _ui = ui;
        _face = ui.Field("com_Goods")?.Field("txt_Title"); // "스타 코인 상점"
        _layer = NativeUi.Component(ui, 1920f, 1080f);
        _free = new ShelfButton(_layer);
        _bulk = new ShelfButton(_layer);
        Plugin.Diagnostics.Write("shopBulk.show");
    }

    // The settings list's text face without the template's outline, dilation or shadow (user-reported 2026-11-13: the
    // light template's thick outline merged with white text at 15 into a blur).
    private static RuntimeObject Text(RuntimeObject parent, string text, float x, float y, float width, float height, int size, Color color)
    {
        var label = NativeUi.Label(parent, text, x, y, width, height, size);
        ModUi.Plain(label, color);
        return label;
    }

    // The shop title's face for the buttons and the card title (the template face read thin and small in game, 1.9.2-dev.5),
    // without its letter spacing: the title's negative spacing suits its 40-size text and made letters overlap at button
    // sizes (dev.7, 2026-11-13).
    private static void Face(RuntimeObject label)
    {
        if (_face == null || _face.Get<bool>("isDisposed")) return;
        NativeUi.CopyTextFormat(label, _face);
        var format = label.Get("textFormat")!;
        format.SetField("letterSpacing", 0);
        label.Set("textFormat", format);
    }

    private static RuntimeObject Coin(RuntimeObject parent, float size)
    {
        var coin = NativeUi.Create("ProductRecommendation", "星币") ?? throw new TypeLoadException("ProductRecommendation/星币 unavailable");
        var scale = size / Math.Max(coin.Get<float>("width"), 1f);
        coin.Call("SetScale", scale, scale);
        coin.Set("touchable", false);
        parent.Call("AddChild", coin);
        return coin;
    }

    // Hover lifts a live button a little and a press pushes it in, as the screen's own buttons do (scaled about its centre).
    private static void Feel(RuntimeObject root, IntPtr hover, bool live)
    {
        var target = !live ? 1f : _down == root.Pointer && Input.GetMouseButton(0) ? 0.95f : hover == root.Pointer ? 1.04f : 1f;
        var now = root.Get<float>("scaleX");
        if (Math.Abs(now - target) < 0.0005f) return;
        var next = Mathf.MoveTowards(now, target, Time.unscaledDeltaTime * 1.5f);
        root.Call("SetScale", next, next);
    }

    private static void Show(RuntimeObject item, bool visible)
    {
        if (item.Get<bool>("visible") != visible) item.Set("visible", visible);
    }

    private static void Fade(RuntimeObject item, float alpha)
    {
        if (Math.Abs(item.Get<float>("alpha") - alpha) > 0.001f) item.Set("alpha", alpha);
    }

    private static void Layout(RuntimeObject art, bool blocked)
    {
        var ax = art.Get<float>("x"); var ay = art.Get<float>("y");
        var y0 = ay + RowDy;
        var hover = blocked ? IntPtr.Zero : Hover();
        // The two buttons always stay (user-decided 2026-11-14); while a claim or purchase runs they only stop answering. A button
        // with nothing to act on is gray, without a count (user-decided 2026-11-13).
        _free!.Root.Call("SetXY", ax + FreeDx, y0);
        _bulk!.Root.Call("SetXY", ax + BulkDx, y0);
        var free = Shelf.Any(g => g.Free);
        _free.Show(Words("무료 일괄 수령", "Claim All Free"), free ? Green : Gray);
        _bulk.Show(Words("일괄 구매", "Bulk Buy"), Shelf.Count > 0 ? Yellow : Gray);
        Feel(_free.Root, hover, free && _batch == null && _card == null);
        Feel(_bulk.Root, hover, Shelf.Count > 0 && _batch == null && _card == null);
        if (_card == null) return;
        // Draft MB: the open pop, or the closing fade that disposes the card when it ends.
        var now = Time.unscaledTime;
        if (_closingAt >= 0f)
        {
            var u = (now - _closingAt) / CloseTime;
            if (u >= 1f) { CloseCard(); return; }
            Motion(1f - u, 1f - 0.04f * u, 1f - u);
        }
        else
        {
            var t = Math.Min(1f, (now - _openedAt) / OpenTime);
            Motion(Math.Min(1f, 1.6f * t), 0.92f + 0.08f * EaseOutBack(t), 1f - (1f - t) * (1f - t) * (1f - t));
        }
        // While picking, the card follows the shelf: a goods that left (or arrived) rebuilds it, keeping the ticks of those
        // that stay. A purchase keeps the rows it started with.
        if (_mode == CardMode.Pick && _closingAt < 0f && Ids() != _cardIds) { OpenCard(false); if (_card == null) return; }
        var h = _card.Get<float>("height");
        _card.Call("SetXY", ax + CardCx - CardW / 2, Math.Max(ay + 8f, ay + CardCy - h / 2));
        var pick = _mode == CardMode.Pick;
        var batch = _cardBatch;
        foreach (var row in Rows)
        {
            if (pick)
            {
                var on = Chosen.Contains(row.Id);
                var over = hover == row.Root.Pointer;
                Show(row.Back, on || over);
                SmoothShape.Restyle(row.Back, on ? Picked : HoverFill, 2f, on ? Picked : HoverLine);
                Show(row.Box, true); Show(row.Disc, false); Show(row.Spinner, false);
                SmoothShape.Restyle(row.Box, on ? Yellow : Color.white, 3f, Color.black);
                Show(row.Check, true); Fade(row.Check, on ? 1f : CheckIdle);
                foreach (var part in row.Parts) Fade(part, 1f);
                continue;
            }
            // Buying: the goods of the purchase show their state at the checkbox's place; the rest are faded.
            var mine = batch != null && batch.Ids.Contains(row.Id);
            var done = mine && batch!.DoneIds.Contains(row.Id);
            var current = mine && batch!.Current == row.Id;
            Show(row.Back, false);
            foreach (var part in row.Parts) Fade(part, mine ? 1f : 0.4f);
            Show(row.Disc, done);
            Show(row.Spinner, current);
            if (current) row.Spinner.Set("rotation", now * 300f % 360f);
            Show(row.Box, mine && !done && !current);
            if (mine && !done) SmoothShape.Restyle(row.Box, Flat, 2f, FlatLine);
            Show(row.Check, done); Fade(row.Check, 1f);
        }
        foreach (var preset in Presets)
        {
            var goods = Tier(preset.Tier);
            var usable = goods.Count > 0;
            var active = pick && usable && goods.All(g => Chosen.Contains(g.Id));
            var over = pick && usable && hover == preset.Root.Pointer;
            SmoothShape.Restyle(preset.Shape, !usable ? Flat : active ? Yellow : Cream, 2.5f, !usable ? Flat : active ? Color.black : over ? HoverLine : Tan);
            preset.Label.Set("color", usable ? Ink : FlatText);
            Feel(preset.Root, hover, pick && usable);
        }
        Show(_close!, _mode != CardMode.Buying);
        Feel(_close!, hover, _mode != CardMode.Buying);
        var footY = _sumCount!.Get<float>("y");
        if (pick)
        {
            var picked = Shelf.Where(g => Chosen.Contains(g.Id)).ToList();
            var total = picked.Sum(g => g.Price);
            var shortOf = total > _coins;
            var result = now < _resultUntil ? batch : null; // the purchase just ended: its result for a moment
            if (result != null)
            {
                _sumCount.Set("text", result.Result);
                _sumCount.Set("color", result.Bad ? Red : Ink);
                if (result.Spent > 0) Sum(result.Spent, Ink, footY);
                else { Show(_sumCoin!, false); Show(_sumAmount!, false); }
                Held(Words($"보유 {_coins}", $"Have {_coins}"), Muted);
            }
            else
            {
                _sumCount.Set("text", Words($"{picked.Count}개 선택", $"{picked.Count} picked"));
                _sumCount.Set("color", Ink);
                Sum(total, shortOf ? Red : Ink, footY);
                Held(shortOf ? Words($"보유 {_coins} · 스타 코인이 부족합니다", $"Have {_coins} · not enough Star Coins")
                    : picked.Count > 0 ? Words($"보유 {_coins} → {_coins - total}", $"Have {_coins} → {_coins - total}") : Words($"보유 {_coins}", $"Have {_coins}"),
                    shortOf ? Red : Muted);
            }
            Show(_cardBar!, false); Show(_cardBarFill!, false);
            _cancel!.Show(Words("취소", "Cancel"), Cream);
            _buy!.Show(Words("구매", "Buy"), picked.Count > 0 && !shortOf ? Yellow : Gray);
            Feel(_cancel.Root, hover, true);
            Feel(_buy.Root, hover, picked.Count > 0 && !shortOf);
        }
        else
        {
            var b = batch!;
            _sumCount.Set("text", b.Free ? Words($"받는 중 {b.Done}/{b.Count}", $"Claiming {b.Done}/{b.Count}") : Words($"구매 중 {b.Done}/{b.Count}", $"Buying {b.Done}/{b.Count}"));
            _sumCount.Set("color", Ink);
            Show(_sumCoin!, false); Show(_sumAmount!, false); Show(_held!, false); Show(_heldCoin!, false);
            Show(_cardBar!, true);
            var bw = _cardBar!.Get<float>("width");
            var fw = b.Count == 0 ? 0f : bw * b.Done / b.Count;
            Show(_cardBarFill!, fw > 14f); _cardBarFill!.Call("SetSize", Math.Max(fw, 14f), 14f);
            SmoothShape.Fill(_cardBarFill, b.Free ? Green : Yellow);
            Show(_cancel!.Root, false);
            _buy!.Show(b.Stop ? Words("중지 중", "Stopping") : Words("중지", "Stop"), Cream);
            Feel(_buy.Root, hover, !b.Stop);
        }
    }

    // "N 사용" with the coin after the footer's lead text.
    private static void Sum(int total, Color color, float y)
    {
        var cx = 26f + _sumCount!.Get<float>("textWidth") + 10f;
        Show(_sumCoin!, true); _sumCoin!.Call("SetXY", cx, y + 7f); // centred on the 36-high line
        Show(_sumAmount!, true); _sumAmount!.Call("SetXY", cx + 28f, y);
        _sumAmount.Set("text", Words($"{total} 사용", total.ToString()));
        _sumAmount.Set("color", color);
    }

    // The balance line under it, led by the Star Coin icon (user-requested 2026-11-13).
    private static void Held(string text, Color color)
    {
        Show(_heldCoin!, true); Show(_held!, true);
        _held!.Set("text", text); _held.Set("color", color);
    }

    private static void Motion(float alpha, float scale, float dim)
    {
        if (Math.Abs(alpha - _shownAlpha) > 0.001f) { _card!.Set("alpha", alpha); _shownAlpha = alpha; }
        if (Math.Abs(scale - _shownScale) > 0.0005f) { _card!.Call("SetScale", scale, scale); _shownScale = scale; }
        if (Math.Abs(dim - _shownDim) > 0.001f) { _dim!.Set("alpha", dim); _shownDim = dim; }
    }

    private static float EaseOutBack(float t)
    {
        const float s = 1.4f;
        t -= 1f;
        return t * t * ((s + 1f) * t + s) + 1f;
    }

    private static string Ids() => string.Join(",", Shelf.Select(g => g.Id));

    // Draft D's basket card over a dimmed screen: presets, one row per buyable goods, the total and 취소 / 구매.
    private static void OpenCard(bool fresh)
    {
        CloseCard(keepChoice: !fresh);
        if (Shelf.Count == 0) return;
        if (fresh) Chosen.Clear(); // nothing ticked at first: one toggle press picks a group
        var goodsList = Order(Shelf);
        var n = goodsList.Count;
        var rowH = n <= 6 ? 62f : Mathf.Floor(372f / n);
        var footerTop = RowsTop + n * rowH + 8f;
        var h = footerTop + FooterH;
        _dim = NativeUi.Component(_layer!, 1920f, 1080f);
        _dim.Set("opaque", true); // a press here closes the card; the shop behind it takes no input while it is open
        var shade = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
        shade.Call("SetSize", 1920f, 1080f);
        shade.Get("shape")!.Call("DrawRect", 0f, Dim, Dim);
        shade.Set("touchable", false);
        _dim.Call("AddChild", shade);
        _card = NativeUi.Component(_layer!, CardW, h);
        _card.Set("opaque", true);
        _card.Call("SetPivot", 0.5f, 0.5f, false);
        SmoothShape.Create(_card, 6f, 8f, CardW, h, Shadow, 22f, 22f, 22f, 22f);
        SmoothShape.Create(_card, 0f, 0f, CardW, h, Cream, 22f, 22f, 22f, 22f, 4f, Color.black);
        var title = NativeUi.Label(_card, "", 26f, 12f, 320f, 46f, 30);
        Face(title);
        title.Set("text", Words("일괄 구매", "Bulk Buy")); title.Set("align", 0); title.Set("verticalAlign", 1);
        _close = NativeUi.Component(_card, 36f, 36f);
        _close.Set("opaque", true); _close.Call("SetXY", CardW - 54f, 16f); _close.Call("SetPivot", 0.5f, 0.5f, false);
        SmoothShape.Create(_close, 0f, 0f, 36f, 36f, Color.white, 18f, 18f, 18f, 18f, 3f, Color.black);
        foreach (var angle in new[] { 45f, -45f })
        {
            var bar = SmoothShape.Create(_close, 0f, 0f, 20f, 3.6f, Ink, 1.8f, 1.8f, 1.8f, 1.8f);
            bar.Call("SetPivot", 0.5f, 0.5f, true); bar.Call("SetXY", 18f, 18f); bar.Set("rotation", angle);
        }
        var px = 26f;
        // "50% 이상 할인" (user-renamed 2026-11-13 from 반값 이하) matches the rows' N% OFF tags.
        foreach (var (tier, ko, en) in new[] { (1, "무료", "Free"), (2, "배지", "Badges"), (3, "50% 이상 할인", "50%+ Off"), (4, "전부", "All") })
        {
            var preset = new Preset(_card, tier, Words(ko, en), px, 66f);
            Presets.Add(preset);
            px += preset.Root.Get<float>("width") + 10f;
        }
        // Dividers under the presets and above the footer, in every state (user-requested 2026-11-13).
        SmoothShape.Create(_card, 20f, RowsTop - 9f, CardW - 40f, 2f, Rule, 1f, 1f, 1f, 1f);
        var size = rowH >= 56f ? 21 : 18;
        for (var i = 0; i < n; i++) Rows.Add(NewRow(goodsList[i], RowsTop + i * rowH, rowH, size));
        SmoothShape.Create(_card, 20f, footerTop, CardW - 40f, 2f, Rule, 1f, 1f, 1f, 1f);
        _sumCount = Text(_card, "", 26f, footerTop + 10f, 300f, 36f, 21, Ink);
        _sumCount.Set("align", 0); _sumCount.Set("verticalAlign", 1);
        _sumCoin = Coin(_card, 22f);
        _sumAmount = Text(_card, "", 0f, footerTop + 10f, 160f, 36f, 21, Ink);
        _sumAmount.Set("align", 0); _sumAmount.Set("verticalAlign", 1);
        _heldCoin = Coin(_card, 16f); _heldCoin.Call("SetXY", 26f, footerTop + 48f);
        _held = Text(_card, "", 46f, footerTop + 44f, 270f, 24f, 14, Muted);
        _held.Set("align", 0); _held.Set("verticalAlign", 1); _held.Set("singleLine", true); _held.Set("autoSize", 3);
        _cardBar = SmoothShape.Create(_card, 26f, footerTop + 50f, CardW - 240f, 14f, Track, 7f, 7f, 7f, 7f, 2f, Color.black);
        _cardBarFill = SmoothShape.Create(_card, 26f, footerTop + 50f, 14f, 14f, Yellow, 7f, 7f, 7f, 7f, 2f, Color.black);
        _cancel = new Sticker(_card, 122f, ButtonH, 23);
        _cancel.Root.Call("SetXY", CardW - 318f - 3f, footerTop + 20f - 3f);
        _buy = new Sticker(_card, 160f, ButtonH, 24);
        _buy.Root.Call("SetXY", CardW - 182f - 3f, footerTop + 20f - 3f);
        _cardIds = Ids();
        _mode = CardMode.Pick;
        // A fresh card pops in; a rebuild for a changed shelf stays where it was.
        _openedAt = fresh ? Time.unscaledTime : Time.unscaledTime - OpenTime;
        _shownAlpha = _shownScale = _shownDim = -1f;
        if (fresh)
        {
            Motion(0f, 0.92f, 0f);
            Plugin.Diagnostics.Write(FormattableString.Invariant($"shopBulk.open goods={n}; ticked={Chosen.Count}"));
        }
    }

    private static Row NewRow(Goods g, float y, float rowH, int size)
    {
        var card = _card!;
        var row = new Row { Id = g.Id };
        row.Root = NativeUi.Component(card, CardW - 32f, rowH - 4f);
        row.Root.Set("opaque", true); row.Root.Call("SetXY", 16f, y - 2f); // the whole row ticks its goods
        row.Back = SmoothShape.Create(card, 16f, y - 2f, CardW - 32f, rowH - 4f, Picked, 14f, 14f, 14f, 14f, 2f, Picked);
        var cy = y + rowH / 2 - 4f;
        var t = rowH - 14f;
        row.Parts.Add(SmoothShape.Create(card, 26f, cy - t / 2, t, t, ThumbBack, 8f, 8f, 8f, 8f, 2.5f, Color.black));
        if (g.Icon.Length > 0)
        {
            // The goods cell's own icon url (its loader_Goods), filling the thumbnail. The game's TextureLoader, which
            // UIManager.InstanceInit makes every package loader, also loads non-package urls (TextureManager), as HomePortrait's.
            var icon = RuntimeObject.New(RuntimeObject.FindClass("UI", "TextureLoader"));
            icon.Set("touchable", false);
            card.Call("AddChild", icon);
            icon.Call("SetSize", t - 4f, t - 4f); icon.Call("SetXY", 28f, cy - t / 2 + 2f);
            icon.Set("fill", 1); icon.Set("align", 1); icon.Set("verticalAlign", 1); // LoaderFillType.Scale, centred
            icon.Set("url", g.Icon);
            row.Parts.Add(icon);
        }
        var nameX = 26f + t + 16f;
        var name = Text(card, "", nameX, cy - 18f, 300f, 36f, size, Ink);
        name.Set("text", g.Name); // the game's own item name, never through ModText
        name.Set("align", 0); name.Set("verticalAlign", 1); name.Set("singleLine", true);
        row.Parts.Add(name);
        var off = g.Original > g.Price && g.Price > 0 ? (int)Math.Round((g.Original - g.Price) * 100.0 / g.Original) : 0;
        if (off > 0)
        {
            var tag = Text(card, $"{off}% OFF", 0f, 0f, 120f, 20f, 12, Ink);
            var tw = tag.Get<float>("textWidth") + 14f;
            var tx = nameX + name.Get<float>("textWidth") + 10f;
            row.Parts.Add(SmoothShape.Create(card, tx, cy - 10f, tw, 20f, Green, 10f, 10f, 10f, 10f, 2f, Color.black));
            card.Call("SetChildIndex", tag, card.Get<int>("numChildren") - 1);
            tag.Call("SetXY", tx, cy - 10f); tag.Call("SetSize", tw, 20f); tag.Set("align", 1); tag.Set("verticalAlign", 1);
            row.Parts.Add(tag);
        }
        var right = CardW - 82f;
        var price = Text(card, "", right - 140f, cy - 18f, 140f, 36f, size, Ink);
        price.Set("text", g.Free ? Words("무료", "Free") : g.Price.ToString()); price.Set("align", 2); price.Set("verticalAlign", 1);
        row.Parts.Add(price);
        if (!g.Free)
        {
            var coinX = right - price.Get<float>("textWidth") - 28f;
            var coin = Coin(card, 22f); coin.Call("SetXY", coinX, cy - 11f);
            row.Parts.Add(coin);
            if (g.Original > g.Price)
            {
                var old = Text(card, g.Original.ToString(), coinX - 6f - 80f, cy - 12f, 80f, 24f, 14, Muted);
                old.Set("align", 2); old.Set("verticalAlign", 1);
                row.Parts.Add(old);
            }
        }
        row.Box = SmoothShape.Create(card, CardW - 59f, cy - 15f, 30f, 30f, Color.white, 8f, 8f, 8f, 8f, 3f, Color.black);
        row.Disc = SmoothShape.Create(card, CardW - 59f, cy - 15f, 30f, 30f, Green, 15f, 15f, 15f, 15f, 3f, Color.black);
        row.Disc.Set("visible", false);
        row.Check = NativeUi.Icon(card, "打勾（大）", CardW - 54f, cy - 10f, 20f) ?? throw new TypeLoadException("Common/打勾（大） unavailable");
        row.Check.Set("color", Color.black); row.Check.Set("alpha", CheckIdle);
        // The purchase in progress: eight dots of falling shade, turning.
        row.Spinner = NativeUi.Component(card, 30f, 30f);
        row.Spinner.Set("touchable", false); row.Spinner.Call("SetXY", CardW - 59f, cy - 15f); row.Spinner.Call("SetPivot", 0.5f, 0.5f, false);
        for (var k = 0; k < 8; k++)
        {
            var angle = k * Mathf.PI / 4f;
            var shade = (60f + k * 22f) / 255f;
            SmoothShape.Create(row.Spinner, 15f + Mathf.Cos(angle) * 10f - 3f, 15f + Mathf.Sin(angle) * 10f - 3f, 6f, 6f,
                new Color(shade, shade, shade, 1f), 3f, 3f, 3f, 3f);
        }
        row.Spinner.Set("visible", false);
        return row;
    }

    // Draft MB's close: the card fades out over CloseTime, then CloseCard runs from Layout. Presses wait meanwhile.
    private static void BeginClose()
    {
        if (_closingAt < 0f) _closingAt = Time.unscaledTime;
        _down = IntPtr.Zero;
    }

    private static void CloseCard(bool keepChoice = false)
    {
        GameUi.Dispose(_card); GameUi.Dispose(_dim);
        _card = _dim = _close = _sumCount = _sumCoin = _sumAmount = _held = _heldCoin = _cardBar = _cardBarFill = null;
        _buy = _cancel = null;
        Presets.Clear(); Rows.Clear(); _cardIds = "";
        _closingAt = -1f;
        if (keepChoice) return;
        Chosen.Clear();
        _mode = CardMode.Pick;
        _cardBatch = null;
    }

    // What a press at this pointer would act on: the first of our targets on its hit path.
    private static IntPtr Match(IReadOnlyList<RuntimeObject> path)
    {
        foreach (var item in path)
        {
            var p = item.Pointer;
            if (_card != null)
            {
                if (_closingAt >= 0f) return IntPtr.Zero;
                if (_buy!.Hit(p) || _cancel!.Hit(p) || p == _close?.Pointer && _mode != CardMode.Buying || p == _dim?.Pointer || p == _card.Pointer) return p;
                if (Presets.Any(x => x.Root.Pointer == p) || Rows.Any(x => x.Root.Pointer == p)) return p;
            }
            else if (_free!.Hit(p) || _bulk!.Hit(p)) return p;
        }
        return IntPtr.Zero;
    }

    private static IntPtr Hover() => Match(GameUi.PointerPath());

    // Presses are read from the hit path (no listener on native objects): a press counts when it starts and ends on one item.
    private static void HandleInput()
    {
        var hit = GameUi.PointerPath(hitTest: Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0));
        if (Input.GetMouseButtonDown(0)) { _down = Match(hit); return; }
        if (!Input.GetMouseButtonUp(0)) return;
        var up = Match(hit);
        var pressed = _down; _down = IntPtr.Zero;
        if (up == IntPtr.Zero || up != pressed) return;
        if (_card == null)
        {
            if (_batch != null) return;
            if (_free!.Hit(up)) { var free = Tier(1); if (free.Count > 0) Start(free, false); } // free goods: at once, no card
            else if (_bulk!.Hit(up) && Shelf.Count > 0) OpenCard(true);
            return;
        }
        if (_mode == CardMode.Buying)
        {
            if (_buy!.Hit(up) && _cardBatch is { } running && !running.Stop)
            {
                running.Stop = true;
                Plugin.Diagnostics.Write(FormattableString.Invariant($"shopBulk.stop done={running.Done}; count={running.Count}"));
            }
            return;
        }
        _resultUntil = 0f; // any press on the card ends the result line
        if (up == _dim?.Pointer || up == _close?.Pointer || _cancel!.Hit(up)) { BeginClose(); return; }
        if (_buy!.Hit(up))
        {
            var goods = Order(Shelf.Where(g => Chosen.Contains(g.Id)));
            if (goods.Count == 0 || goods.Sum(g => g.Price) > _coins) return;
            Start(goods, true);
            return;
        }
        foreach (var preset in Presets)
            if (preset.Root.Pointer == up)
            {
                var goods = Tier(preset.Tier);
                if (goods.Count == 0) return;
                var on = goods.All(g => Chosen.Contains(g.Id));
                foreach (var g in goods) { if (on) Chosen.Remove(g.Id); else Chosen.Add(g.Id); } // its own goods only
                return;
            }
        foreach (var row in Rows)
            if (row.Root.Pointer == up) { if (!Chosen.Remove(row.Id)) Chosen.Add(row.Id); return; }
    }

    internal static void Clear()
    {
        CloseCard();
        GameUi.Dispose(_layer);
        _ui = _layer = null;
        _free = _bulk = null; _face = null;
        _batch = null; _down = IntPtr.Zero;
        Shelf.Clear();
    }
}
