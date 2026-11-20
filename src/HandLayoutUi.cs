using UnityEngine;

namespace BetterAstralParty;

// Native layout tween destinations are redirected without cancelling their lifecycle callbacks.
internal static class HandLayoutUi
{
    private sealed class Entry
    {
        internal RuntimeObject Card = null!, Face = null!;
        internal int Guid, Id, Order, FrameOrder;
        internal HandGroup Group;
        internal Vector2 Position, Scale, Applied;
        internal Vector2 ZoomIn, AppliedZoom;
        internal Vector2 LastShow;
        internal Vector2 RenderedPosition, RenderedScale;
        internal bool Rendered;
        internal RuntimeObject? ScaleTween;
        internal bool ZoomOwned;
        internal bool? Batching;
        internal HandOutlineBinding? Outlines;
        internal float Rotation;
        internal bool Owned;
        internal bool HasLayout;
        internal Vector2 Center;
        internal bool CenterLocked;
        internal bool AlphaOwned;
        internal float OwnedAlpha; // The folded alpha this mod last wrote.
        internal OffsetKey? CornerKey; // The face and card values CornerOffset was read with.
        internal Vector2 CornerOffset;
        internal int CornerChecks;
    }
    // Everything the face corner's place inside its card follows from: the face's own transform and the card's anchor.
    private readonly record struct OffsetKey(float X, float Y, float Width, float Height, float ScaleX, float ScaleY, float Rotation,
        float PivotX, float PivotY, float CardWidth, float CardHeight, float CardPivotX, float CardPivotY);
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static readonly Dictionary<HandGroup, RuntimeObject> Captions = new();
    private static readonly Dictionary<HandGroup, RuntimeObject> PileTargets = new();
    private static readonly Dictionary<HandGroup, (Vector2 Position, float Width, float Height, string Text)> CaptionLayout = new();
    private static readonly HashSet<IntPtr> Live = new();
    private static readonly HashSet<Entry> Ready = new();
    // Reusable storage only: memberships/native tween handles are cleared after every tick.
    private static readonly List<Entry> FrameCards = new();
    private static readonly List<HandGroup> FrameGroups = new();
    private static readonly List<Entry>[] FramePiles = Enum.GetValues<HandGroup>().Select(_ => new List<Entry>()).ToArray();
    private static readonly float[] FrameAspects = new float[FramePiles.Length];
    private static readonly List<IntPtr> RemovedCards = new();
    private static readonly List<HandGroup> RemovedGroups = new();
    private static readonly Dictionary<IntPtr, List<RuntimeObject>> Motions = new();
    private static readonly Stack<List<RuntimeObject>> MotionPool = new();
    private static readonly string[] EffectFields = { "effectOutline", "effectOutline_Suggest_Bottom", "effectTempCard" };
    private static RuntimeObject? _ui, _container, _hotZone;
    private static Vector2 _hotPosition, _hotSize;
    private static float? _dropBottom; // From the last layout frame, as a share of the root height.
    private static float _scanAt, _diagnosticAt;
    private static IntPtr _tween, _tweenManager, _logic, _pool, _cardClass;
    private static HandGroup? _expanded;
    private static IntPtr _focused;
    private static bool _cardPress;
    private static HandGroup? _pressedPile;
    private static bool _outsidePress;
    private static Vector2 _pilePressAt;

    internal static void Tick()
    {
        // Do not register managed delegates with hot-loaded FairyGUI: 0.28.1's native crash
        // followed that new path, outside the managed exception/circuit-breaker boundary.
        if (!Compatibility.Allowed("GroupHand")) return;
        try { Update(); }
        catch (Exception ex) { Compatibility.Block("GroupHand", ex, Clear); }
        finally { ClearFrame(); }
    }

    private static void Update()
    {
        HandClickBinding.Clear(); // One input frame only; never retain snapshots through drag/use.
        var root = GameUi.Root;
        if (!Plugin.GroupHand.Value || root == null) { Clear(); return; }
        if (_tween == IntPtr.Zero) _tween = RuntimeObject.FindClass("FairyGUI", "GTween");
        if (_tweenManager == IntPtr.Zero) _tweenManager = RuntimeObject.FindClass("FairyGUI", "TweenManager");
        if (_cardClass == IntPtr.Zero) _cardClass = RuntimeObject.FindClass("UI", "UIHandCard_Button_Card");
        if (_pool == IntPtr.Zero) _pool = RuntimeObject.FindClass("GameLogic", "CardPool");
        var lap = RuntimeObject.LapStart();
        if (Time.unscaledTime >= _scanAt)
        {
            if (_logic == IntPtr.Zero) _logic = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            var manager = RuntimeObject.StaticField(_logic, "_inst");
            var room = manager?.Get("room")?.Get("curRoomInfo");
            // The fight starts at step 2. The window's opening (0) and the encounter screen (1, also with its UI hidden)
            // keep the field hand grouped (user-requested 2026-11-01), and so does a fight Fight PiP shows beside the field.
            var fightStep = GameUi.Find(root, "FightWindow")?.Get("contentPane")?.Field("step")?.Get<int>("selectedIndex") ?? 0;
            var eligible = VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)
                && manager?.Get("watch")?.Call("PlayerIsWatcher")?.Value<bool>() == false
                && (fightStep < 2 || FightPip.ShowsHand);
            var ui = eligible ? GameUi.Find(root, "UIHandCardPanel") : null;
            if (ui?.Pointer != _ui?.Pointer)
            {
                Clear(); _ui = ui;
                if (ui != null)
                {
                    _container = ui.Field("container_Card")!; _hotZone = ui.Field("hotZone")!;
                    _hotPosition = new(_hotZone.Get<float>("x"), _hotZone.Get<float>("y"));
                    _hotSize = new(_hotZone.Get<float>("width"), _hotZone.Get<float>("height"));
                }
            }
            _scanAt = Time.unscaledTime + .2f; // Clear on panel replacement must not erase this deadline.
        }
        if (_ui == null || !GameUi.Visible(_ui))
        {
            // Already detached: repeated Clear would reset _scanAt and search every frame.
            if (_ui != null) Clear();
            Report("no-field-hand"); return;
        }
        // ReadyFight hides by scale, not visible. Never counteract ancestor transforms.
        if (!Shown(_container!) || Tweening(_container!))
        {
            foreach (var caption in Captions.Values) caption.Set("visible", false);
            foreach (var target in PileTargets.Values) target.Set("visible", false);
            _pressedPile = null; _outsidePress = false;
            foreach (var entry in Entries.Values) RestoreZoom(entry);
            Report("parent-hidden-or-animating");
            return;
        }
        var width = root.Get<float>("width"); var height = root.Get<float>("height");
        var unit = Math.Min(width / 1920f, height / 1080f);
        // The destination stays current during a native drag, including a resize; the last layout frame sets its bottom.
        if (_dropBottom is { } dropBottom) UpdateDropZone(root, width, height, dropBottom * height);
        var dragging = GameUi.LiveDrag();
        if (CardPointerBusy(dragging))
        {
            foreach (var entry in Entries.Values) entry.Rendered = false;
            _pressedPile = null; _outsidePress = false;
            if (dragging != null && Entries.TryGetValue(dragging.Pointer, out var dragged)) RestoreZoom(dragged);
            else foreach (var item in GameUi.PointerPath(refresh: true))
                if (Entries.TryGetValue(item.Pointer, out var pressedCard)) SetFoldedOpacity(pressedCard, false);
            Report("card-pointer-busy"); return;
        }
        if (ModUi.IsOpen) { _pressedPile = null; _outsidePress = false; Report("settings-busy"); return; }
        // ForbidOperate locks input during normal actions/timeouts, not the visible hand layout.
        var cardsLocked = !_container!.Get<bool>("touchable");
        // A fight Fight PiP shows beside the field locks the cards (readyFight), not the piles: they still open to be looked
        // at (user-reported 2026-11-18), while the cards stay untouchable.
        var inputLocked = cardsLocked && !FightPip.ShowsHand;
        RuntimeObject.Lap("hand.gates", ref lap);
        var live = Live; live.Clear();
        var ready = Ready; ready.Clear();
        var motions = CardTweens();
        RuntimeObject.Lap("hand.tweens", ref lap);
        var hidden = 0; var distributing = 0; var foreign = 0; var pending = 0;
        var childCount = _container!.Get<int>("numChildren");
        for (var i = 0; i < childCount; i++)
        {
            var card = _container.Call("GetChildAt", i)!;
            if (card.TypeName != "UIHandCard_Button_Card" || card.Field<bool>("IsRelease")) continue;
            var data = card.Get("CardData");
            if (data == null) continue;
            live.Add(card.Pointer);
            var guid = data.Field<int>("Guid");
            if (Entries.TryGetValue(card.Pointer, out var old) && old.Guid != guid)
            {
                RestoreZoom(old);
                Entries.Remove(card.Pointer); // Recycled object is now owned by a new native lifecycle.
            }
            if (!Entries.TryGetValue(card.Pointer, out var entry))
            {
                var config = data.Get("Config")!;
                Entries[card.Pointer] = entry = new Entry { Card = card, Face = card.Field("com_Card")!, Guid = guid,
                    ZoomIn = card.Field<Vector2>("zoomInScale"),
                    Scale = new(card.Get<float>("scaleX"), card.Get<float>("scaleY")),
                    Id = config.Get<int>("Id"), Group = HandLayout.Classify(config.Get<int>("Id"), config.Get<int>("CardType")) };
            }
            var resting = card.Field<Vector2>("CustomPosition");
            var displayReset = DisplayZoomReset(entry, resting);
            if (!displayReset && (!entry.Owned || resting != entry.Applied))
            {
                entry.Owned = false; entry.Position = resting;
                entry.Rotation = card.Field<float>("CustomRotation");
                entry.Order = card.Field<int>("_CustomSortingOrder");
            }
            motions.TryGetValue(card.Pointer, out var active);
            var show = active?.Count > 0 ? card.Get<Vector2>("_ShowPosition") : entry.LastShow;
            var foreignMotion = active?.Any(t => !LayoutMotion(entry, t, resting, show)) == true;
            // The container ancestry was checked once above; only inspect each remaining branch.
            var shown = Shown(card, _container) && Shown(entry.Face, card);
            var waiting = Distributing(card);
            var usePending = UsePending(card, cardsLocked);
            if (!shown) hidden++;
            if (waiting) distributing++;
            if (foreignMotion) foreign++;
            if (usePending) { pending++; RestoreZoom(entry); }
            // A finished native move is recoverable even when its final point differs from CustomPosition.
            // Hidden/distributed/use-pending cards and unrelated animations still belong to the game.
            if (!HandLayout.CanResume(shown, waiting, usePending, foreignMotion)) { RestoreZoom(entry); continue; }
            if (displayReset)
            {
                // DisplayZoom directly writes Y/scale before its ZoomOutCard tween. Do not
                // turn that native teleport into a new grouped-layout animation baseline.
                card.Call("SetXY", entry.RenderedPosition.x, entry.RenderedPosition.y);
                card.Call("SetScale", entry.RenderedScale.x, entry.RenderedScale.y);
                foreach (var motion in active ?? Enumerable.Empty<RuntimeObject>())
                    if (motion.Field<int>("_propType") == 4)
                        Retarget(motion, entry.RenderedPosition, entry.Applied);
            }
            entry.LastShow = show;
            ready.Add(entry);
        }
        RuntimeObject.Lap("hand.cards", ref lap);
        RemovedCards.Clear();
        foreach (var key in Entries.Keys) if (!live.Contains(key)) RemovedCards.Add(key);
        foreach (var key in RemovedCards)
        { RestoreZoom(Entries[key]); Entries.Remove(key); }
        // Keep temporarily hovered/animated cards in their slots; neighbouring piles must not jump.
        var cards = FrameCards; cards.Clear();
        foreach (var entry in Entries.Values) { entry.FrameOrder = cards.Count; cards.Add(entry); }
        cards.Sort(static (a, b) => {
            var order = ((int)a.Group).CompareTo((int)b.Group);
            if (order == 0) order = a.Id.CompareTo(b.Id);
            if (order == 0) order = a.Order.CompareTo(b.Order);
            return order == 0 ? a.FrameOrder.CompareTo(b.FrameOrder) : order;
        });
        var groups = FrameGroups; groups.Clear();
        var piles = FramePiles;
        foreach (var pile in piles) pile.Clear();
        foreach (var entry in cards)
        {
            if (groups.Count == 0 || groups[^1] != entry.Group) groups.Add(entry.Group);
            piles[groups.Count - 1].Add(entry);
        }
        var aspects = FrameAspects;
        for (var column = 0; column < groups.Count; column++) aspects[column] = Aspect(piles[column][0]);
        RemovedGroups.Clear();
        foreach (var group in Captions.Keys) if (!groups.Contains(group)) RemovedGroups.Add(group);
        foreach (var group in RemovedGroups)
        { GameUi.Dispose(Captions[group]); Captions.Remove(group); CaptionLayout.Remove(group); }
        RemovedGroups.Clear();
        foreach (var group in PileTargets.Keys) if (!groups.Contains(group)) RemovedGroups.Add(group);
        foreach (var group in RemovedGroups)
        { GameUi.Dispose(PileTargets[group]); PileTargets.Remove(group); }
        RuntimeObject.Lap("hand.group", ref lap);
        var path = GameUi.PointerPath(refresh: true);
        var mouse = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!.Get<Vector2>("touchPosition");
        var pointer = root.Result<Vector2, Vector2>("GlobalToLocal", mouse);
        // Monster cancel can leave an empty CardWindow on stage; only its actual hit owns the click.
        var usingCard = (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0))
            && path.Any(item => item.TypeName == "CardWindow");
        PollPileClick(path, pointer, unit, inputLocked || pending > 0 || usingCard || FieldCameraReturnUi.ConsumedInput);
        // A native input lock folds the open row.
        if (inputLocked) { _expanded = null; _focused = IntPtr.Zero; }
        if (_expanded is { } missing && !groups.Contains(missing)) _expanded = null;
        RuntimeObject.Lap("hand.pointer", ref lap);
        if (_expanded == null || !Entries.TryGetValue(_focused, out var focused) || focused.Group != _expanded)
            _focused = IntPtr.Zero;
        var pileTop = height;
        for (var column = 0; column < groups.Count; column++)
        {
            var pile = piles[column];
            pileTop = Math.Min(pileTop, HandLayout.Place(width, height, groups.Count, column, pile.Count, 0, aspects[column]).Y);
        }
        HandLayout.Slot? open = null;
        for (var column = 0; column < groups.Count; column++)
        {
            var group = groups[column];
            var pile = piles[column];
            var aspect = aspects[column];
            if (_expanded == group) open = HandLayout.ClickRow(width, height, pile.Count, 0, aspect, pileTop);
            if (_expanded == group)
            {
                // The row stays open, not the last card enlarged after pointer exit.
                _focused = IntPtr.Zero;
                for (var row = 0; row < pile.Count; row++)
                {
                    var hit = HandLayout.ClickRow(width, height, pile.Count, row, aspect, pileTop);
                    if (ready.Contains(pile[row]) && HandLayout.InFocus(pointer.x, pointer.y, hit, 3 * unit))
                        _focused = pile[row].Card.Pointer;
                }
            }
            // Only the hovered card of the open row is enlarged; the others keep their base size.
            for (var row = 0; row < pile.Count; row++)
            {
                var slot = _expanded == group ? HandLayout.ClickRow(width, height, pile.Count, row, aspect, pileTop)
                    : HandLayout.Place(width, height, groups.Count, column, pile.Count, row, aspect);
                if (_expanded == group && _focused == pile[row].Card.Pointer) slot = HandLayout.Focus(slot);
                if (ready.Contains(pile[row]))
                {
                    SetFoldedOpacity(pile[row], _expanded != group);
                    Place(pile[row], slot, root, row, motions.GetValueOrDefault(pile[row].Card.Pointer));
                }
            }
            if (!AnyOwnedReady(pile, ready))
            {
                if (Captions.TryGetValue(group, out var hiddenCaption)) hiddenCaption.Set("visible", false);
                if (PileTargets.TryGetValue(group, out var hiddenTarget)) hiddenTarget.Set("visible", false);
                continue;
            }
            if (!Captions.TryGetValue(group, out var caption))
            {
                Captions[group] = caption = NativeUi.Label(_container, "", 0, 0, 190, 28, 20, dark: true);
                caption.Set("touchable", false);
            }
            var captionSlot = HandLayout.Place(width, height, groups.Count, column, pile.Count, 0, aspect);
            var pos = ToContainer(root, captionSlot.X, height - 37 * unit);
            var captionLayout = (pos, captionSlot.Width, 28 * unit, HandLayout.Label(group, ModText.Korean));
            if (!CaptionLayout.TryGetValue(group, out var previous) || previous != captionLayout)
            {
                caption.Call("SetXY", pos.x, pos.y); caption.Call("SetSize", captionSlot.Width, 28 * unit);
                caption.Set("text", captionLayout.Item4);
                CaptionLayout[group] = captionLayout;
            }
            if (!caption.Get<bool>("visible")) caption.Set("visible", true);
            UpdatePileTarget(group, captionSlot, root, height, unit, !inputLocked && pile.All(ready.Contains));
        }
        RuntimeObject.Lap("hand.place", ref lap);
        _dropBottom = HandLayout.DropBottom(pileTop, open, unit) / height;
        if (Plugin.Diagnostics.IsRecording && Time.unscaledTime >= _diagnosticAt)
            Report($"layout; tracked={cards.Count}; ready={ready.Count}; hidden={hidden}; distributing={distributing}; foreign={foreign}; inputLocked={cardsLocked}; viewOnly={cardsLocked && !inputLocked}; pending={pending}; motions={motions.Values.Sum(v => v.Count)}; expanded={_expanded}; nativeZoom={RuntimeObject.StaticField(_pool, "ShowCardStatus")!.Value<int>()}");
        if (ready.Count == 0) return;
        // Native ZoomCard runs only for world presses. Fresh hit testing avoids arming on
        // card/pile/target UI input; both grouped modes share this presentation correction.
        if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            && !FieldCameraReturnUi.ConsumedInput && root.Get("touchTarget") == null)
            HandClickBinding.Arm(RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!,
                ready.Select(e => e.Card));
    }

    private static void UpdateDropZone(RuntimeObject root, float width, float height, float bottom)
    {
        // Native drop checks and artwork use this same object; leave its visibility alone.
        var parent = _hotZone!.Get("parent")!;
        // From above the screen top down to the cards, 10-90 % wide (user-requested 2026-10-31 and 2026-11-15).
        // The native zone shows only while a card is dragged, so it never covers the turn bar otherwise.
        var start = ToLocal(parent, root, width * .1f, -height * HandLayout.DropOverhang);
        var dropEnd = ToLocal(parent, root, width * .9f, bottom);
        if (_hotZone.Get<float>("x") != start.x || _hotZone.Get<float>("y") != start.y)
            _hotZone.Call("SetXY", start.x, start.y);
        var dropSize = dropEnd - start;
        if (_hotZone.Get<float>("width") != dropSize.x || _hotZone.Get<float>("height") != dropSize.y)
            _hotZone.Call("SetSize", dropSize.x, dropSize.y);
    }

    private static void PollPileClick(IReadOnlyList<RuntimeObject> path, Vector2 pointer, float unit, bool locked)
    {
        HandGroup? hit = null;
        foreach (var pair in PileTargets)
            if (pair.Value.Get<bool>("visible") && path.Any(p => p.Pointer == pair.Value.Pointer)) { hit = pair.Key; break; }
        var overCard = path.Any(p => p.TypeName == "UIHandCard_Button_Card");
        if (Plugin.Diagnostics.IsRecording && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0)))
            Plugin.Diagnostics.Write($"hand.click phase={(Input.GetMouseButtonDown(0) ? "down" : "up")}; locked={locked}; focused={Application.isFocused}; pileHit={hit.HasValue}; overCard={overCard}; topType={path.FirstOrDefault()?.TypeName ?? "none"}");
        if (locked || !Application.isFocused) { _pressedPile = null; _outsidePress = false; return; }
        if (Input.GetMouseButtonDown(0))
        { _pressedPile = hit; _outsidePress = hit == null && !overCard; _pilePressAt = pointer; }
        if ((pointer - _pilePressAt).sqrMagnitude > 64 * unit * unit)
        { _pressedPile = null; _outsidePress = false; }
        if (Input.GetMouseButtonUp(0))
        {
            if (_pressedPile is { } pressed && hit == pressed && (pointer - _pilePressAt).sqrMagnitude <= 64 * unit * unit)
            { _expanded = _expanded == pressed ? null : pressed; _focused = IntPtr.Zero; }
            else if (_outsidePress && hit == null && !overCard)
            { _expanded = null; _focused = IntPtr.Zero; } // Presentation only; never cancel card use.
            _pressedPile = null; _outsidePress = false;
        }
        else if (!Input.GetMouseButton(0)) { _pressedPile = null; _outsidePress = false; }
    }

    private static void UpdatePileTarget(HandGroup group, HandLayout.Slot slot, RuntimeObject root, float height, float unit, bool enabled)
    {
        if (!PileTargets.TryGetValue(group, out var target))
        {
            // An owned, transparent hit surface consumes only folded-pile presses. Native
            // card handlers remain untouched and receive all expanded-card drag events.
            // It sits in the hand panel beside the card container, which a PiP fight leaves untouchable (see inputLocked).
            PileTargets[group] = target = NativeUi.Component(_ui!, 1, 1);
            target.Set("opaque", true); target.Set("touchable", true); target.Set("sortingOrder", 200);
        }
        // Once open only its caption is a toggle; the vacated card footprint must
        // not become an invisible wall over the field.
        var a = ToLocal(_ui!, root, slot.X, _expanded == group ? height - 37 * unit : slot.Y);
        var b = ToLocal(_ui!, root, slot.X + slot.Width, height - 8 * unit);
        target.Call("SetXY", a.x, a.y); target.Call("SetSize", b.x - a.x, b.y - a.y);
        target.Set("visible", enabled);
    }

    private static void SetFoldedOpacity(Entry e, bool folded)
    {
        if (e.Card.Get<bool>("isDisposed")) { e.AlphaOwned = false; return; }
        var alpha = e.Card.Get<float>("alpha");
        var target = Math.Clamp(Plugin.FoldedHandOpacity.Value, 0f, 1f);
        if (e.AlphaOwned && Math.Abs(alpha - e.OwnedAlpha) > .0001f) e.AlphaOwned = false;
        // Only resting opaque cards: never brighten, stop, or restart native fades.
        if (folded && !e.AlphaOwned && Math.Abs(alpha - 1f) < .0001f
            || folded && e.AlphaOwned && Math.Abs(e.OwnedAlpha - target) > .0001f)
        { e.Card.Set("alpha", target); e.AlphaOwned = true; e.OwnedAlpha = target; }
        else if (!folded && e.AlphaOwned)
        { e.Card.Set("alpha", 1f); e.AlphaOwned = false; }
    }

    private static bool CardPointerBusy(RuntimeObject? dragging)
    {
        bool HandCard(RuntimeObject? item) => item?.TypeName == "UIHandCard_Button_Card"
            && item.Get("parent")?.Pointer == _container?.Pointer;
        // Camera panning also holds LMB. Latch the press origin, not the object crossed later.
        if (!Input.GetMouseButton(0)) _cardPress = false;
        else if (Input.GetMouseButtonDown(0)) _cardPress = GameUi.PointerPath(refresh: true).Any(HandCard);
        return _cardPress || HandCard(dragging);
    }

    private static bool UsePending(RuntimeObject card, bool inputLocked)
    {
        // DisplayCard retains roll-out through use confirmation; ordinary deal/reflow retains only roll-over.
        return inputLocked && card.Get("onRollOut")!.Field("_bridge")!.Field<bool>("_isLocking");
    }

    private static bool Tweening(RuntimeObject item) => RuntimeObject.StaticCall(_tween, "IsTweening", item)!.Value<bool>();
    private static bool Shown(RuntimeObject item, RuntimeObject? checkedAncestor = null)
    {
        if (item.Get<bool>("isDisposed") || !item.Get<bool>("onStage")) return false;
        for (var node = item; node != null && node.Pointer != checkedAncestor?.Pointer; node = node.Get("parent"))
            if (!node.Get<bool>("internalVisible") || !node.Get<bool>("internalVisible2")
                || node.Get<float>("alpha") <= 0 || node.Get<float>("scaleX") <= 0 || node.Get<float>("scaleY") <= 0) return false;
        return true;
    }
    private static Vector2 ToLocal(RuntimeObject parent, RuntimeObject root, float x, float y) =>
        parent.Result<Vector2, Vector2>("RootToLocal", new Vector2(x, y), root);
    // RootToLocal into the card container is affine: a screen-space FairyGUI point goes through the stage transform, the
    // container's inverse transform and a fixed-direction plane projection. Three calls a frame map every slot, caption and
    // pile point (about 30 interpreted calls a frame with eight cards before, 2026-11-17 call log); a fourth, the far corner,
    // checks the map, and a frame whose corner misses it goes through the call for each point.
    private static int _mapFrame = -1;
    private static IntPtr _mapParent;
    private static bool _mapHolds;
    private static Vector2 _mapOrigin, _mapX, _mapY;
    private static Vector2 ToContainer(RuntimeObject root, float x, float y)
    {
        var parent = _container!;
        if (_mapFrame != Time.frameCount || _mapParent != parent.Pointer)
        {
            _mapFrame = Time.frameCount; _mapParent = parent.Pointer;
            var w = root.Get<float>("width"); var h = root.Get<float>("height");
            _mapHolds = false;
            if (w > 0 && h > 0)
            {
                _mapOrigin = ToLocal(parent, root, 0, 0);
                _mapX = (ToLocal(parent, root, w, 0) - _mapOrigin) / w;
                _mapY = (ToLocal(parent, root, 0, h) - _mapOrigin) / h;
                var miss = ToLocal(parent, root, w, h) - (_mapOrigin + _mapX * w + _mapY * h);
                _mapHolds = Math.Abs(miss.x) < .05f && Math.Abs(miss.y) < .05f;
            }
        }
        return _mapHolds ? _mapOrigin + _mapX * x + _mapY * y : ToLocal(parent, root, x, y);
    }
    // UIHandCard_Button_Card.IsDistribute() is `x == 0 && Math.Abs(y - Screen.height) == 0` (hand-layout checks its IL): read
    // through the plain x and y getters instead of an interpreted call per card each frame (2026-11-17 call log).
    private static bool Distributing(RuntimeObject card) =>
        card.Get<float>("x") == 0f && Math.Abs(card.Get<float>("y") - (float)Screen.height) == 0f;
    // The face corner in card space, card.GlobalToLocal(face.LocalToGlobal(corner)), depends only on the face's transform inside
    // its card and the card's anchor, not on where the card is: read again only when one of those values changes (two
    // interpreted calls per card each frame before). Every 64th frame per card it is read again and compared; a miss turns
    // the cache off for the session and is logged.
    private static bool _cornerCacheOff;
    private static Vector2 CornerOffset(Entry e, RuntimeObject card, Vector2 corner, float faceWidth, float faceHeight)
    {
        var face = e.Face;
        Vector2 Read() => card.Result<Vector2, Vector2>("GlobalToLocal", face.Result<Vector2, Vector2>("LocalToGlobal", corner));
        if (_cornerCacheOff || RuntimeObject.GetPointerOf(face.Pointer, "parent") != card.Pointer) return Read();
        var key = new OffsetKey(face.Get<float>("x"), face.Get<float>("y"), faceWidth, faceHeight, face.Get<float>("scaleX"),
            face.Get<float>("scaleY"), face.Get<float>("rotation"), face.Get<float>("pivotX"), face.Get<float>("pivotY"),
            card.Get<float>("width"), card.Get<float>("height"), card.Get<float>("pivotX"), card.Get<float>("pivotY"));
        if (e.CornerKey == key && (++e.CornerChecks & 63) != 0) return e.CornerOffset;
        var offset = Read();
        if (e.CornerKey == key && (Math.Abs(offset.x - e.CornerOffset.x) > .05f || Math.Abs(offset.y - e.CornerOffset.y) > .05f))
        {
            _cornerCacheOff = true;
            Plugin.Diagnostics.Write(FormattableString.Invariant($"hand.cornerCache off: cached=({e.CornerOffset.x:F2},{e.CornerOffset.y:F2}); read=({offset.x:F2},{offset.y:F2})"));
        }
        // A card scaled to zero maps to NaN; keep reading until the result is finite.
        e.CornerKey = float.IsFinite(offset.x) && float.IsFinite(offset.y) ? key : null;
        e.CornerOffset = offset;
        return offset;
    }
    private static bool Near(Vector2 a, Vector2 b) => HandLayout.Resting(a.x, a.y, b.x, b.y);
    // A loop, not a capturing lambda: this runs for every pile every frame.
    private static bool AnyOwnedReady(List<Entry> pile, HashSet<Entry> ready)
    {
        foreach (var e in pile) if (e.Owned && ready.Contains(e)) return true;
        return false;
    }
    private static float Aspect(Entry e)
    {
        var zoom = e.Card.Field<Vector2>("zoomOutScale");
        return e.Face.Get<float>("height") * zoom.y / (e.Face.Get<float>("width") * zoom.x);
    }

    private static void Place(Entry e, HandLayout.Slot slot, RuntimeObject root, int row, List<RuntimeObject>? motions)
    {
        SetCardBatching(e, false);
        var card = e.Card;
        var zoom = card.Field<Vector2>("zoomOutScale");
        // Native hover handlers remain installed, but cannot enlarge one card over its neighbours.
        e.AppliedZoom = zoom; e.ZoomOwned = true;
        if (card.Field<Vector2>("zoomInScale") != zoom) card.SetField("zoomInScale", zoom);
        if (e.Face.Get<float>("scaleX") != zoom.x || e.Face.Get<float>("scaleY") != zoom.y)
            e.Face.Call("SetScale", zoom.x, zoom.y);
        if ((e.Outlines ??= new HandOutlineBinding(card, EffectFields)).Apply()) FitEffects(e, false);
        var faceWidth = e.Face.Get<float>("width");
        var faceHeight = e.Face.Get<float>("height");
        var scale = slot.Width / (faceWidth * zoom.x);
        // Actual card artwork, hit area and native effects move together. Visibility stays native.
        var target = ToContainer(root, slot.X, slot.Y);
        var corner = e.Face.Field<bool>("_pivotAsAnchor")
            ? new Vector2(-faceWidth * e.Face.Get<float>("pivotX"),
                -faceHeight * e.Face.Get<float>("pivotY")) : Vector2.zero;
        // Every card every frame, folded or in the open row: value results without wrappers (2026-11-13).
        var offset = CornerOffset(e, card, corner, faceWidth, faceHeight);
        var pos = target - offset * scale;
        var actual = new Vector2(card.Get<float>("x"), card.Get<float>("y"));
        var centerOffset = offset + new Vector2(faceWidth * zoom.x, faceHeight * zoom.y) * .5f;
        var center = ToContainer(root, slot.X + slot.Width * .5f, slot.Y + slot.Height * .5f);
        e.CenterLocked = (e.CenterLocked && Near(e.Center, center))
            || Near(actual + centerOffset * card.Get<float>("scaleX"), center);
        e.Center = center;
        var moving = false;
        var rotating = false;
        var scaling = false;
        foreach (var motion in motions ?? Enumerable.Empty<RuntimeObject>())
        {
            switch (motion.Field<int>("_propType"))
            {
                case 4:
                    // A native hover starts at our slot but heads for its old bottom-screen show point.
                    // Do not bake that excursion into the new start and animate back (visible twitch).
                    if (HoldHover(e, motion, pos)) card.Call("SetXY", pos.x, pos.y);
                    else Retarget(motion, actual, pos);
                    moving = true; break;
                case 11: Retarget(motion, new(card.Get<float>("scaleX"), card.Get<float>("scaleY")), new(scale, scale)); scaling = true; break;
                case 12:
                    var end = motion.Get("endValue")!;
                    if (end.Field<float>("x") != 0)
                    {
                        motion.Get("startValue")!.SetField("x", HandLayout.RetargetStart(card.Get<float>("rotation"), 0, motion.Get<float>("normalizedTime")));
                        end.SetField("x", 0f);
                    }
                    rotating = true; break;
            }
        }
        if (!moving && !Near(actual, pos))
            card.Call("TweenMove", pos, .12f);
        if (!scaling && Math.Abs(card.Get<float>("scaleX") - scale) > .001f)
            e.ScaleTween = card.Call("TweenScale", new Vector2(scale, scale), .12f);
        if (!rotating && Math.Abs(card.Get<float>("rotation")) > .01f) card.Call("TweenRotate", 0f, .12f);
        // Native XY and scale tweens have different durations/easing. Pin the shared artwork/effect
        // centre to the cell during zoom, using this frame's scale, not a second independent path.
        if (e.CenterLocked)
        {
            var centered = center - centerOffset * card.Get<float>("scaleX");
            card.Call("SetXY", centered.x, centered.y);
        }
        e.Applied = pos; e.Owned = e.HasLayout = true;
        if (card.Field<Vector2>("CustomPosition") != pos) card.SetField("CustomPosition", pos);
        if (card.Field<float>("CustomRotation") != 0) card.SetField("CustomRotation", 0f);
        SetStackOrder(card, row, _expanded != null && _focused == card.Pointer);
        e.RenderedPosition = new(card.Get<float>("x"), card.Get<float>("y"));
        e.RenderedScale = new(card.Get<float>("scaleX"), card.Get<float>("scaleY"));
        e.Rendered = true;
    }

    private static bool DisplayZoomReset(Entry e, Vector2 resting)
    {
        if (!e.Rendered || !e.Owned) return false;
        // CustomPosition retains DisplayZoom's +/-100 even after the same-event visual
        // restore or a concurrent scale tween. Do not require the transient scale 1/1.6.
        return Math.Abs(resting.x - e.Applied.x) < .001f
            && Math.Abs(Math.Abs(resting.y - e.Applied.y) - 100f) < .001f;
    }

    private static void SetStackOrder(RuntimeObject card, int row, bool focused)
    {
        if (card.Field<int>("_CustomSortingOrder") != row) card.SetField("_CustomSortingOrder", row);
        // Native hover priority can outlive roll-out; only the current focus belongs in front.
        var order = focused ? 100 : row;
        if (card.Get<int>("sortingOrder") != order) card.Set("sortingOrder", order);
    }

    private static bool HoldHover(Entry e, RuntimeObject motion, Vector2 target)
    {
        if (!e.HasLayout || !Near(target, e.Applied)
            || !Near(motion.Get("startValue")!.Get<Vector2>("vec2"), target)) return false;
        motion.Get("startValue")!.Set("vec2", target);
        motion.Get("endValue")!.Set("vec2", target);
        return true;
    }

    private static void Retarget(RuntimeObject motion, Vector2 current, Vector2 target)
    {
        var end = motion.Get("endValue")!;
        if ((end.Get<Vector2>("vec2") - target).sqrMagnitude < .0001f) return;
        var t = motion.Get<float>("normalizedTime");
        motion.Get("startValue")!.Set("vec2", new Vector2(HandLayout.RetargetStart(current.x, target.x, t),
            HandLayout.RetargetStart(current.y, target.y, t)));
        end.Set("vec2", target);
    }

    private static bool LayoutMotion(Entry e, RuntimeObject motion, Vector2 resting, Vector2 show)
    {
        switch (motion.Field<int>("_propType"))
        {
            case 4:
                var end = motion.Get("endValue")!.Get<Vector2>("vec2");
                return Near(end, resting) || Near(end, show) || (e.HasLayout && (Near(end, e.Applied) || Near(end, e.LastShow)));
            case 12: return true;
            case 11: return e.ScaleTween?.Pointer == motion.Pointer;
            default: return false;
        }
    }

    // GetTween returns only the FIRST match; native hover and reflow can coexist on the same card.
    private static unsafe Dictionary<IntPtr, List<RuntimeObject>> CardTweens()
    {
        ClearMotions();
        var result = Motions;
        var array = RuntimeObject.StaticField(_tweenManager, "_activeTweens");
        if (array == null) return result;
        var count = Math.Min((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_array_length(array.Pointer),
            RuntimeObject.StaticField(_tweenManager, "_totalActiveTweens")!.Value<int>());
        // Element pointers straight from the native array (after its four-pointer header, as Il2CppInterop's
        // ArrayStartPointer): most active tweens belong to unrelated UI and are rejected by class before any wrapper.
        var elements = array.Pointer + 4 * IntPtr.Size;
        for (var i = 0; i < count; i++)
        {
            var tween = *(IntPtr*)(elements + i * IntPtr.Size);
            if (tween == IntPtr.Zero || RuntimeObject.FieldOf<bool>(tween, "_killed")) continue;
            var targetPointer = RuntimeObject.FieldPointerOf(tween, "_target");
            if (_cardClass == IntPtr.Zero || RuntimeObject.ClassOf(targetPointer) != _cardClass) continue;
            var motion = new RuntimeObject(tween);
            var target = new RuntimeObject(targetPointer);
            if (target.Get("parent")?.Pointer != _container?.Pointer) continue;
            if (!result.TryGetValue(target.Pointer, out var list))
                result[target.Pointer] = list = MotionPool.Count > 0 ? MotionPool.Pop() : new();
            list.Add(motion);
        }
        return result;
    }

    private static void ClearMotions()
    {
        foreach (var list in Motions.Values) { list.Clear(); MotionPool.Push(list); }
        Motions.Clear();
    }

    private static void ClearFrame()
    {
        FrameCards.Clear(); FrameGroups.Clear();
        foreach (var pile in FramePiles) pile.Clear();
        RemovedCards.Clear(); RemovedGroups.Clear();
        ClearMotions();
    }

    private static void Report(string state)
    {
        if (!Plugin.Diagnostics.IsRecording || Time.unscaledTime < _diagnosticAt) return;
        _diagnosticAt = Time.unscaledTime + .5f;
        Plugin.Diagnostics.State("hand.layout", state);
    }

    private static void Restore(Entry e, List<RuntimeObject>? motions)
    {
        RestoreZoom(e);
        var card = e.Card;
        if (!e.Owned || card.Get<bool>("isDisposed") || card.Field<bool>("IsRelease")
            || card.Get("CardData")?.Field<int>("Guid") != e.Guid
            || card.Field<Vector2>("CustomPosition") != e.Applied) return;
        var resting = !Tweening(card) && HandLayout.Resting(card.Get<float>("x"), card.Get<float>("y"), e.Applied.x, e.Applied.y);
        var restoringScale = false; var restoringRotation = false;
        foreach (var motion in motions ?? Enumerable.Empty<RuntimeObject>())
        {
            var prop = motion.Field<int>("_propType");
            if (prop == 4 && Near(motion.Get("endValue")!.Get<Vector2>("vec2"), e.Applied))
                Retarget(motion, new(card.Get<float>("x"), card.Get<float>("y")), e.Position);
            else if (prop == 11 && e.ScaleTween?.Pointer == motion.Pointer)
            {
                Retarget(motion, new(card.Get<float>("scaleX"), card.Get<float>("scaleY")), e.Scale);
                restoringScale = true;
            }
            else if (prop == 12 && motion.Get("endValue")!.Field<float>("x") == 0)
            {
                motion.Get("endValue")!.SetField("x", e.Rotation);
                restoringRotation = true;
            }
        }
        card.SetField("CustomPosition", e.Position); card.SetField("CustomRotation", e.Rotation);
        card.SetField("_CustomSortingOrder", e.Order);
        // Do not pull a hidden, fading or used card back on screen during cleanup.
        if (!resting)
        {
            if (Shown(card))
            {
                if (!restoringScale && Math.Abs(card.Get<float>("scaleX") - e.Scale.x) > .001f)
                    card.Call("TweenScale", e.Scale, .12f);
                if (!restoringRotation && Math.Abs(card.Get<float>("rotation") - e.Rotation) > .01f)
                    card.Call("TweenRotate", e.Rotation, .12f);
            }
            return;
        }
        card.Call("SetScale", e.Scale.x, e.Scale.y); card.Call("SetXY", e.Position.x, e.Position.y);
        card.Set("rotation", e.Rotation); card.Set("sortingOrder", e.Order);
    }

    private static void RestoreZoom(Entry e)
    {
        e.Rendered = false;
        SetFoldedOpacity(e, false);
        SetCardBatching(e, true);
        e.Outlines?.Restore(); e.Outlines = null;
        if (!e.ZoomOwned || e.Card.Get<bool>("isDisposed")) return;
        FitEffects(e, true);
        if (e.Card.Field<Vector2>("zoomInScale") == e.AppliedZoom)
            e.Card.SetField("zoomInScale", e.ZoomIn);
        if (e.Card.Get("CardData")?.Field<int>("Guid") == e.Guid
            && !e.Card.Field<bool>("IsRelease") && e.Card.Get<int>("sortingOrder") == 100)
            e.Face.Call("SetScale", e.ZoomIn.x, e.ZoomIn.y);
        e.ZoomOwned = false;
    }

    private static void SetCardBatching(Entry e, bool restore)
    {
        // Panel batching flattens cards and caches material order across XY/scale tweens.
        // A native per-card batching boundary keeps each card's text/artwork contiguous.
        if (restore)
        {
            if (e.Batching == false && !e.Card.Get<bool>("isDisposed")) e.Card.Set("fairyBatching", false);
            e.Batching = null;
        }
        else if (e.Batching == null)
        {
            e.Batching = e.Card.Get<bool>("fairyBatching");
            if (!e.Batching.Value) e.Card.Set("fairyBatching", true);
        }
    }

    private static void FitEffects(Entry e, bool restore)
    {
        // Original ZoomOutCard/ZoomInCard use 43/72. Outer card scale now enlarges artwork AND effects.
        var size = restore && e.Card.Get<int>("sortingOrder") == 100 ? 72f : 43f;
        foreach (var field in EffectFields)
            e.Card.Field(field)!.Get("displayObject")!.Set("scale", new Vector2(size, size));
    }

    internal static void Clear()
    {
        // Every step runs even when one fails, so one broken card cannot leave the others or the drop zone in the mod's
        // layout; a failure then stops the feature for the session instead of failing again each frame (2026-11-13 review).
        Exception? failed = null;
        void Step(Action step) { try { step(); } catch (Exception ex) { failed ??= ex; } }
        Step(HandClickBinding.Clear);
        Dictionary<IntPtr, List<RuntimeObject>>? motions = null;
        if (Entries.Count > 0) Step(() => motions = CardTweens());
        foreach (var e in Entries.Values) Step(() => Restore(e, motions?.GetValueOrDefault(e.Card.Pointer)));
        Entries.Clear();
        Step(ClearFrame);
        Live.Clear(); Ready.Clear();
        foreach (var caption in Captions.Values) Step(() => GameUi.Dispose(caption));
        Captions.Clear();
        foreach (var target in PileTargets.Values) Step(() => GameUi.Dispose(target));
        PileTargets.Clear(); _pressedPile = null; _outsidePress = false;
        CaptionLayout.Clear();
        _dropBottom = null; // The native zone stays until the next layout frame.
        Step(() =>
        {
            if (_hotZone == null || _hotZone.Get<bool>("isDisposed")) return;
            _hotZone.Call("SetXY", _hotPosition.x, _hotPosition.y);
            _hotZone.Call("SetSize", _hotSize.x, _hotSize.y);
        });
        if (failed != null) Compatibility.Block("GroupHand", failed);
        _ui = _container = _hotZone = null;
        _expanded = null;
        _focused = IntPtr.Zero;
        _cardPress = false;
        _scanAt = 0;
    }
}
