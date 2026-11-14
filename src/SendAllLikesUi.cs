using UnityEngine;

namespace BetterAstralParty;

// 좋아요 일괄 전송 / Send All Likes (user-chosen 2026-11-13, draft 3 in artifacts/cc/all-like-drafts): a pink sticker with the
// native thumb above the PvE settlement screen's confirm button. Only the player's press acts: it presses each other player's
// native like button (UIBattleSettlement_Com_Item.btn_Praise) through its own click listener, one every 0.15 s. That handler
// retains (locks) its listener, plays its cut-in, marks the button done and sends the like through
// BattleSettlementPanel.TryPraise, so the mod sends nothing itself and presses each card at most once a settlement.
// Self and bots are done from the start (UIBattleSettlement_Com_Item.Refresh); the sticker turns the native gray when
// nothing is left to like.
internal static class SendAllLikesUi
{
    internal const string Feature = "SendAllLikes";
    private static readonly bool Preview = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    // The settlement screen draws its 1920x1080 design 1:1 at 1080p (measured on 1.9.2-dev.2, where the confirm button sat at its
    // package position 1640, 772), so draft 3's capture pixels are screen units: the disc centre 194 right of and 70 above it.
    private const float OffsetX = 194f, OffsetY = -70f, Interval = 0.15f;
    private const float Width = 140f, CentreX = 70f, CentreY = 74f, Radius = 60f;
    private static readonly Color Pink = new(1f, 42f / 255, 146f / 255, 1f), Gray = new(111f / 255, 111f / 255, 111f / 255, 1f);
    private static readonly string[] Players = { "com_Player1", "com_Player2", "com_Player3", "com_Player4" };
    private static readonly (float X, float Y)[] Outline = { (3f, 0f), (-3f, 0f), (0f, 3f), (0f, -3f), (2.1f, 2.1f), (-2.1f, 2.1f), (2.1f, -2.1f), (-2.1f, -2.1f) };
    // Cards pressed in the settlement _pressedFor; never pressed again. Kept while the settlement panel stays current, through
    // hidden buttons, feature OFF and a breaker (2026-11-13 review), and emptied once the panel leaves, since a later
    // settlement can be allocated at the same address.
    private static readonly HashSet<IntPtr> Pressed = new();
    private static IntPtr _pressedFor;
    // The chat bubbles (UIManager's ExpressionListWindow, a window above the settlement panel) stack up the right side and
    // covered the sticker and took its presses in the first real match (2026-11-13). While the sticker shows, their list's
    // right anchor (list_Plaform, width 0, items right-aligned, the avatar ending 7 inside it) sits this far left of the disc
    // centre, clear of the disc at its bounce peak (60 x 1.06 x 1.15, about 73). The game sets that x once, in OnInit. Only a
    // referee message (MessageType 11) draws its wider band (n4) 135 past the anchor and still reaches the disc.
    private const float ChatGap = 70f;
    private static RuntimeObject? _root, _disc, _chat;
    private static IntPtr _settlement, _parent;
    private static float _chatNative, _chatOwned;
    private static bool _likable, _down, _sending, _blockedNoted;
    private static float _scanAt, _shownAt, _bounceAt = -10, _nextAt, _hover = 1;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        // The OFF path's cleanup is inside the breaker too: its failure stops only this feature, never the shared UI.
        try
        {
            if (!Plugin.SendAllLikes.Value) { Clear(); return; }
            Update();
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update()
    {
        // Looked for four times a second until the settlement shows, every frame while it does.
        if (_settlement == IntPtr.Zero && Time.unscaledTime < _scanAt) return;
        _scanAt = Time.unscaledTime + 0.25f;
        var panel = GameUi.Root == null || PvpSafety.Suspended ? null
            : RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst")?.Get("currentPanel");
        var settlement = panel?.TypeName == "BattleSettlementPanel" ? panel.Field("_BattleSettlement") : null;
        if (settlement == null) { Pressed.Clear(); _pressedFor = IntPtr.Zero; }
        var logic = settlement == null ? null : RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        // Replays cannot send; Auto Thanks reads the same switch. Development builds preview the sticker, gray and inert, on a
        // replay's settlement, which the replay window's round jumps reach in a minute (user-approved 2026-11-13).
        var canSend = settlement?.Get<bool>("IsPVE") != true ? null : logic?.Get("replay")?.Get("Session")?.Get<bool>("CanSendC2S");
        if (canSend == null || canSend == false && !Preview) { Clear(); return; }
        var preview = canSend == false;
        if (settlement!.Pointer != _settlement) { Clear(); _settlement = settlement.Pointer; }
        if (settlement.Pointer != _pressedFor) { Pressed.Clear(); _pressedFor = settlement.Pointer; }
        var showTime = panel!.Get("ui")?.Field("com_ShowTime");
        var next = showTime?.Field("btn_Next");
        RuntimeObject? first = null, card = null, click = null;
        var open = 0;
        var shown = false;
        if (GameUi.Visible(next))
            foreach (var name in Players)
            {
                var item = showTime!.Field(name);
                if (item == null) continue;
                first ??= item;
                // The native like buttons show only for players in the battle scene, never for spectators.
                if (!GameUi.Visible(item.Field("btn_Praise"))) continue;
                shown = true;
                if (preview || Pressable(item, item.Field("btn_Praise")!) is not { } listener) continue;
                open++;
                if (click == null) { card = item; click = listener; }
            }
        if (first == null || !shown && !preview) { Hide(); return; }
        if (_root == null || _root.Get<bool>("isDisposed") || _parent != showTime!.Pointer) Create(showTime!, first, preview);
        _likable = open > 0;
        if (_sending && Time.unscaledTime >= _nextAt)
        {
            _sending = false;
            if (click != null)
            {
                // Marked before the call: a press is never repeated, whatever the native handler does.
                Pressed.Add(card!.Pointer);
                click.Call("Call");
                _sending = open > 1;
                _nextAt = Time.unscaledTime + Interval;
                Plugin.Diagnostics.Write(FormattableString.Invariant($"sendAllLikes.press left={open - 1}"));
            }
        }
        Layout(next!);
        HandleInput();
    }

    // The card's native listener while its like button can still be pressed: not done, not locked, and still the settlement
    // panel's own handler for this card (the closure BattleSettlementPanel.RefreshPlayerItem sets).
    private static RuntimeObject? Pressable(RuntimeObject item, RuntimeObject button)
    {
        if (Pressed.Contains(item.Pointer) || button.Field("isFinish")?.Get<int>("selectedIndex") != 0) return null;
        var click = button.Get("onClick");
        var bridge = click?.Field("_bridge");
        if (bridge?.Field("owner") == null || bridge.Field<bool>("_isLocking") || click!.Get<bool>("isEmpty") || click.Get<bool>("isDispatching")) return null;
        var handler = bridge.Field("_callback0");
        if (handler?.Call("GetInvocationList")?.Get<int>("Length") != 1 || bridge.Field("_callback1") != null || bridge.Field("_captureCallback") != null) return null;
        return handler.Get("Target")?.Field("com_Player")?.Pointer == item.Pointer ? click : null;
    }

    private static void Create(RuntimeObject showTime, RuntimeObject item, bool preview)
    {
        GameUi.Dispose(_root);
        _root = NativeUi.Component(showTime, Width, Width);
        _parent = showTime.Pointer;
        _root.Set("opaque", true); // The hit area; hover and presses are read from the hit path, no listener is added.
        _root.Call("SetPivot", CentreX / Width, CentreY / Width, true);
        _disc = SmoothShape.Create(_root, CentreX - Radius, CentreY - Radius, Radius * 2, Radius * 2, Pink, Radius, Radius, Radius, Radius, 6f, Color.black);
        foreach (var (x, y) in Outline) Thumb(x, y, Color.black);
        Thumb(0f, 0f, Color.white);
        var all = NativeUi.Label(_root, "ALL", 0f, 0f, 80f, 36f, 24);
        // The coin badge's own ALL label lends its font and outline.
        var badge = item.Field("com_BattleData")?.Field("com_PVE")?.Call("GetChild", "n118");
        if (badge?.Get("asTextField") != null) NativeUi.CopyTextFormat(all, badge);
        all.Set("align", 1); all.Set("verticalAlign", 1);
        all.Call("SetPivot", 0.5f, 0.5f, true);
        all.Call("SetXY", CentreX - 34f, CentreY - 44f);
        all.Set("rotation", -14f);
        _shownAt = Time.unscaledTime; _hover = 1;
        var next = showTime.Field("btn_Next")!;
        Plugin.Diagnostics.Write(FormattableString.Invariant(
            $"sendAllLikes.show preview={preview} next=({next.Get<float>("x"):0},{next.Get<float>("y"):0},{next.Get<float>("width"):0},{next.Get<float>("height"):0}) screen=({showTime.Get<float>("width"):0},{showTime.Get<float>("height"):0})"));
    }

    // Draft 3's thumb is the native one at 1.25 times its size, outlined in black by eight dark copies around it.
    private static void Thumb(float dx, float dy, Color color)
    {
        var thumb = NativeUi.Create("BattleSettlement", "点赞")!;
        var scale = 54f / thumb.Get<float>("width");
        thumb.Call("SetPivot", 0.5f, 0.5f, true);
        thumb.Call("SetScale", scale, scale);
        thumb.Call("SetXY", CentreX + 1f + dx, CentreY + 4f + dy);
        thumb.Set("color", color);
        thumb.Set("touchable", false);
        _root!.Call("AddChild", thumb);
    }

    // Pops in like the recommendation thumb, lifts slightly under the pointer while it can act and bounces once when pressed.
    private static void Layout(RuntimeObject next)
    {
        var now = Time.unscaledTime;
        var elapsed = now - _shownAt;
        var bounce = now - _bounceAt;
        _hover = Mathf.MoveTowards(_hover, _likable && Over() ? 1.06f : 1f, Time.unscaledDeltaTime * 0.6f);
        var pop = AdviceText.RecommendationScale(elapsed) * _hover * (bounce < 0.45f ? 1 + 0.15f * Mathf.Sin(bounce / 0.45f * Mathf.PI) : 1);
        _root!.Call("SetScale", pop, pop);
        _root.Set("rotation", elapsed < 0.5f ? 12f * (1 - elapsed / 0.5f) : 0f);
        _root.Call("SetXY", next.Get<float>("x") + OffsetX, next.Get<float>("y") + OffsetY);
        SmoothShape.Fill(_disc!, _likable ? Pink : Gray);
        if (!_root.Get<bool>("visible")) _root.Set("visible", true);
        MoveChat();
    }

    // Moves the chat bubble list left of the disc. A value the game wrote since is its new own position.
    private static void MoveChat()
    {
        var found = false;
        if (_chat == null || _chat.Get<bool>("isDisposed"))
        {
            _chat = RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst")?.Field("_cachedExpressionListWindow")
                ?.Get("contentPane")?.Field("list_Plaform");
            if (_chat == null) return;
            _chatNative = _chatOwned = _chat.Get<float>("x");
            found = true;
        }
        var x = _chat.Get<float>("x");
        if (Math.Abs(x - _chatOwned) > 0.01f) _chatNative = x;
        if (_chat.Get("parent") is not { } parent) return;
        // The disc centre is the root's local origin (see InDisc).
        var centre = parent.Result<Vector2, Vector2>("GlobalToLocal", _root!.Result<Vector2, Vector2>("LocalToGlobal", Vector2.zero));
        _chatOwned = Math.Min(_chatNative, centre.x - ChatGap);
        if (Math.Abs(x - _chatOwned) > 0.01f) _chat.Set("x", _chatOwned);
        if (found) Plugin.Diagnostics.Write(FormattableString.Invariant($"sendAllLikes.chat native={_chatNative:0} moved={_chatOwned:0}"));
    }

    private static void RestoreChat()
    {
        if (_chat == null) return;
        // The first line comes while the confirm button still slides in; this one names where the list settled.
        Plugin.Diagnostics.Write(FormattableString.Invariant($"sendAllLikes.chat restore native={_chatNative:0} moved={_chatOwned:0}"));
        if (!_chat.Get<bool>("isDisposed") && Math.Abs(_chat.Get<float>("x") - _chatOwned) <= 0.01f) _chat.Set("x", _chatNative);
        _chat = null;
    }

    private static bool Over(bool hitTest = false)
    {
        var hit = false;
        foreach (var item in GameUi.PointerPath(hitTest: hitTest))
            if (item.Pointer == _root!.Pointer) { hit = true; break; }
        return hit && InDisc();
    }

    // The root's pivot is the disc centre and serves as its anchor, so FairyGUI's LocalToGlobal and GlobalToLocal measure from
    // it, not from the top-left corner its children use. Until 2026.11.0-dev.2 both used (CentreX, CentreY) and were 100 off to the
    // lower right: presses on the disc missed it and the chat bubbles moved 5 left instead of past the disc.
    private static bool InDisc()
    {
        var (x, y) = GameUi.PointerIn(_root!);
        return x * x + y * y <= Radius * Radius;
    }

    // A press counts when it starts and ends on the disc. A press inside the disc that something drawn over it took is logged
    // once a settlement with the types it hit, to name what covers the sticker.
    private static void HandleInput()
    {
        if (ModUi.IsOpen) { _down = false; return; }
        if (Input.GetMouseButtonDown(0))
        {
            _down = Over(hitTest: true);
            if (!_down && !_blockedNoted && InDisc())
            {
                _blockedNoted = true;
                Plugin.Diagnostics.Write("sendAllLikes.blocked hit=" + string.Join("<", GameUi.PointerPath().Take(4).Select(item => item.TypeName)));
            }
            return;
        }
        if (!_down) return;
        if (Input.GetMouseButtonUp(0))
        {
            _down = false;
            if (!_likable || _sending || !Over(hitTest: true)) return;
            _sending = true; _nextAt = _bounceAt = Time.unscaledTime;
            Plugin.Diagnostics.Write("sendAllLikes.start");
        }
        else if (!Input.GetMouseButton(0)) _down = false;
    }

    // The screen or its buttons are away for a moment: hidden, and a running send waits instead of ending.
    private static void Hide()
    {
        if (_root != null && !_root.Get<bool>("isDisposed") && _root.Get<bool>("visible")) _root.Set("visible", false);
        _down = false;
        RestoreChat();
    }

    internal static void Clear()
    {
        RestoreChat();
        GameUi.Dispose(_root);
        _root = _disc = null;
        _settlement = _parent = IntPtr.Zero;
        _likable = _down = _sending = _blockedNoted = false;
        _bounceAt = -10; _hover = 1;
    }
}
