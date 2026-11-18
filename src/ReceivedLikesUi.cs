using UnityEngine;

namespace BetterAstralParty;

// 받은 좋아요 표시 / Received Likes (user-chosen 2026-11-15: draft 다 with motion 다-1, artifacts/cc/like-count-drafts; the
// achievement-sticker look of draft 바 shipped first in 2026.11.2-dev.7 and read too small in a real match).
// A like the server confirmed reaches every human player's battle chat as a BattlePraiseMessage: the liker's own client adds
// it locally (BattleSettlementPanel.ChatPraise -> CommunicateLogic.RequestPlayerChatC2S) and the others get it through
// PlayerChatS2C -> CommunicateLogic.DealChatMsg. Both land in UIManager's ExpressionListWindow._messages, which keeps the
// newest messages up to a total UIWeight of 8. Each frame of the PvE settlement that list is compared with the previous
// frame's: a message new since then is counted once. An object still in the previous list is alive, so its address cannot
// have been reused. Messages already there when the settlement first shows count too (2026-11-16: players on a sped-up game
// liked before the local settlement showed, and those likes went missing): ExpressionListWindow.OnHide clears the list when
// the battle's windows close, so what it holds then is this match's. The game drops chat from players
// the viewer muted (GameLogicManager.lockExpressionPlayerID) before the list, so their likes are not seen. Nothing is sent.
// Each like is one stamp on the card's top-right, the row in pick order: its sender's character icon with a small pink like
// badge (draft 나 of artifacts/cc/like-count-drafts v10-faces, user-chosen 2026-11-17, replacing the pink thumb disc with
// the sender's pick label, 1P-4P, in a white pill under it). The icon is the sender's skin profile photo
// (RoomPlayer.standingPainting.ProfilePhoto, UT_Hero_ProfilePhoto_*: 220x220 with a white sticker outline and transparent
// margins, the figure about three quarters of it), the picture the battle chat bubble (UIExpression_Com_ChatItem.
// loader_Player) and the battle settings window's emoji block list (loader_Head) show for a player.
internal static class ReceivedLikesUi
{
    internal const string Feature = "ReceivedLikes";
    private static readonly string[] Players = { "com_Player1", "com_Player2", "com_Player3", "com_Player4" };
    // Settlement card units, the screen's at 1080p (the settlement draws 1:1): the card's right edge is its stripes' end
    // (36 + 418). Draft 나 right-aligns the stamps 14 inside it, 54 apart, the rightmost icon's centre at 418 as the discs'
    // was (32 into its slot). A stamp is 64 square, placed 4 under the card's visible top; the photo's 68 box (the figure
    // about 50) is centred at 32, 36 in it and the badge at 50, 13.
    private const float Right = 454f, RootW = 210f, RootH = 80f, Spacing = 54f, Inset = 32f, StampSize = 64f, StampY = 4f;
    private const float FaceX = 32f, FaceY = 36f, PhotoBox = 68f, BadgeX = 50f, BadgeY = 13f, BadgeDelay = 0.1f;
    // The badge: the shipped disc's look at radius 13 (rim 2.5, thumb 16, outline 1.3). The shipped disc itself (radius 24,
    // rim 3, thumb 30 with an outline of 2.2, 0.5 above the centre, measured against draft 다 on 2026.11.2-dev.8's in-game
    // capture) stays for a sender without a profile photo.
    private const float Radius = 24f, Rim = 3f, ThumbSize = 30f, ThumbY = -0.5f, ThumbOutline = 2.2f;
    private const float BadgeRadius = 13f, BadgeRim = 2.5f, BadgeThumb = 16f, BadgeThumbY = -0.3f, BadgeOutline = 1.3f;
    private static readonly Color Pink = new(1f, 42f / 255, 146f / 255, 1f);
    private static readonly LikeLedger Likes = new();
    private static readonly long[] Ids = new long[4];
    private static readonly string[] Photos = { "", "", "", "" };
    private static readonly Card?[] Cards = new Card?[4];
    // Muted senders' notes (2026-11-16, draft 나 of artifacts/cc/like-count-drafts v6): a capsule right-aligned like the stamps,
    // its bottom 10 above the name plate, and a description while the pointer is over it. They show on the second view only
    // (user-requested 2026-11-16, after 2026.11.2-dev.13 floated the note over the first view's full portrait): the card's
    // step controller at 2, which ShowTime_Data sets before it plays the card's "SJ Cut in", once that has finished. That
    // transition raises com_PlayerLabel from 1045 to 474 over 0.4 s; the plate's top border lies 7 under the label's y (481
    // card units on the user's capture of the second view: 237 above the screen top plus 244). The note rises out from behind
    // the plate 0.4 s later (entrance 나 of v7-entrance): it sits right above the portrait, so the plate covers its lower part.
    private const float PlateInset = 7f, NoteGap = 10f, NoteDelay = 0.4f;
    private static readonly MutedSender[] Mutes = new MutedSender[4];
    private static readonly Note?[] Notes = new Note?[4];
    private static NoteUi.Description? _tip;
    private static int _tipCard = -1;
    private static float _hoverAt;
    private static HashSet<IntPtr> _seen = new(), _now = new();
    private static IntPtr _settlement;
    private static int _players;
    private static float _scanAt;

    private sealed class Stamp
    {
        internal readonly RuntimeObject Body;
        internal readonly RuntimeObject? Badge;
        internal readonly float At;
        internal float From, To, MovedAt = float.NegativeInfinity;
        internal RuntimeObject? Photo; // The native loader until its picture is replaced by the shrunk copy.
        internal Stamp(RuntimeObject body, RuntimeObject? badge, RuntimeObject? photo, float x, float at) { Body = body; Badge = badge; Photo = photo; From = To = x; At = at; }
    }
    // Each sender photo shrunk once a settlement (null where shrinking failed and the loader's picture stays).
    private static readonly Dictionary<string, RuntimeObject?> Shrunk = new();
    private static readonly List<Texture2D> ShrunkTextures = new();

    private sealed class Note
    {
        internal readonly IntPtr Item;
        internal readonly NoteUi.Capsule Capsule;
        internal readonly MutedSender Kind;
        internal readonly float At;
        internal Note(IntPtr item, NoteUi.Capsule capsule, MutedSender kind, float at) { Item = item; Capsule = capsule; Kind = kind; At = at; }
    }

    private sealed class Card
    {
        internal readonly IntPtr Item;
        internal readonly RuntimeObject Root;
        internal readonly Dictionary<int, Stamp> Stamps = new();
        internal Card(IntPtr item, RuntimeObject root) { Item = item; Root = root; }
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        // The OFF path's cleanup is inside the breaker too: its failure stops only this feature, never the shared UI.
        try
        {
            if (!Plugin.ReceivedLikes.Value) { Clear(); return; }
            Update();
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update()
    {
        // Looked for four times a second until the settlement shows, every frame while it does.
        if (_settlement == IntPtr.Zero && Time.unscaledTime < _scanAt) return;
        _scanAt = Time.unscaledTime + 0.25f;
        var manager = GameUi.Root == null || PvpSafety.Suspended ? null : RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst");
        var panel = manager?.Get("currentPanel");
        var settlement = panel?.TypeName == "BattleSettlementPanel" ? panel.Field("_BattleSettlement") : null;
        if (settlement == null || settlement.Get<bool>("IsPVE") != true) { Clear(); return; }
        if (settlement.Pointer != _settlement)
        {
            Clear();
            _settlement = settlement.Pointer;
            ReadPlayers(settlement);
        }
        Poll(manager!.Field("_cachedExpressionListWindow")?.Field("_messages"));
        var showTime = panel!.Get("ui")?.Field("com_ShowTime");
        for (var card = 0; card < Players.Length; card++) Draw(card, showTime?.Field(Players[card]));
        for (var card = 0; card < Players.Length; card++) DrawNote(card, showTime?.Field(Players[card]));
        Hover();
    }

    // The cards' players in pick order (BattleSettlement.PlaySettlements, which RefreshAchieveInfo puts on com_Player1-4).
    private static void ReadPlayers(RuntimeObject settlement)
    {
        var list = settlement.Field("PlaySettlements");
        _players = Math.Min(Ids.Length, list?.Get<int>("Count") ?? 0);
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var muted = logic?.Field("lockExpressionPlayerID");
        var friend = logic?.Get("friend");
        var self = logic?.Get("account")?.Call("GetPlayerID")?.Value<long>() ?? 0;
        for (var i = 0; i < Ids.Length; i++)
        {
            var player = i < _players ? list!.Call("get_Item", i)?.Field("PlayerData")?.Field("player") : null;
            Ids[i] = player?.Get<long>("Id") ?? 0;
            Photos[i] = player?.Get("standingPainting")?.Get("ProfilePhoto")?.String() ?? "";
            Mutes[i] = Ids[i] == 0 ? MutedSender.None : MutedSenders.Kind(muted?.Call("Contains", Ids[i])?.Value<bool>() == true,
                Ids[i] == self, player!.Get<bool>("IsBot"), friend?.Call("IsBlack", Ids[i])?.Value<bool>() == true);
        }
    }

    private static int Pick(long id) => id == 0 ? -1 : Array.IndexOf(Ids, id, 0, _players);

    private static void Poll(RuntimeObject? messages)
    {
        (_seen, _now) = (_now, _seen);
        _now.Clear();
        var count = messages?.Get<int>("Count") ?? 0;
        if (count is < 0 or > 64) throw new InvalidOperationException("Battle chat history bound");
        for (var i = 0; i < count; i++)
        {
            var message = messages!.Call("get_Item", i);
            if (message == null) continue;
            _now.Add(message.Pointer);
            if (_seen.Contains(message.Pointer) || message.TypeName != "BattlePraiseMessage") continue;
            var sender = Pick(message.Field("Sender")?.Field("player")?.Get<long>("Id") ?? 0);
            var target = ReceivedLikesMotion.TryTarget(message.Field("SendMsg")?.String(), out var id) ? Pick(id) : -1;
            if (Likes.Add(sender, target, Time.unscaledTime))
                Plugin.Diagnostics.Write(FormattableString.Invariant($"receivedLikes.like from={sender + 1}P to={target + 1}P count={Likes.For(target).Count}"));
            else Plugin.Diagnostics.Write(FormattableString.Invariant($"receivedLikes.skip from={sender + 1}P to={target + 1}P"));
        }
    }

    private static void Draw(int index, RuntimeObject? item)
    {
        var senders = Likes.For(index);
        if (senders.Count == 0 || item == null || item.Get<bool>("isDisposed")) { Remove(index); return; }
        var card = Cards[index];
        if (card == null || card.Root.Get<bool>("isDisposed") || card.Item != item.Pointer)
        {
            Remove(index);
            Cards[index] = card = new Card(item.Pointer, Create(item));
            Plugin.Diagnostics.Write(FormattableString.Invariant(
                $"receivedLikes.show card={index + 1} item=({item.Get<float>("x"):0},{item.Get<float>("y"):0},{item.Get<float>("width"):0},{item.Get<float>("height"):0}) scale={item.Get<float>("scaleX"):0.##} top={Top(item):0}"));
        }
        var now = Time.unscaledTime;
        card.Root.Call("SetXY", Right - 14f - RootW, Top(item));
        for (var i = 0; i < senders.Count; i++)
        {
            var (pick, at) = senders[i];
            var x = ReceivedLikesMotion.StampX(i, senders.Count, RootW, Spacing, Inset);
            if (!card.Stamps.TryGetValue(pick, out var stamp))
                card.Stamps[pick] = stamp = NewStamp(card.Root, pick, x, at);
            else if (Math.Abs(stamp.To - x) > 0.01f)
            {
                stamp.From = ReceivedLikesMotion.Slide(stamp.From, stamp.To, now - stamp.MovedAt);
                stamp.To = x;
                stamp.MovedAt = now;
            }
            var cx = ReceivedLikesMotion.Slide(stamp.From, stamp.To, now - stamp.MovedAt);
            var slant = ReceivedLikesMotion.Slant(pick);
            // 다-1's motion: the stamp pops in its slot like the recommendation thumb, straightening from 12 degrees past its
            // slant; its like badge pops 0.1 s later, as the label did.
            Place(stamp.Body, cx - FaceX, StampY, now - stamp.At, slant + ReceivedLikesMotion.Lean(now - stamp.At));
            if (stamp.Badge != null) Pop(stamp.Badge, now - stamp.At - BadgeDelay);
            if (stamp.Photo != null) Sharpen(stamp, pick);
        }
    }

    private static void Place(RuntimeObject obj, float x, float y, float elapsed, float rotation)
    {
        if (!Pop(obj, elapsed)) return;
        obj.Call("SetXY", x, y);
        obj.Set("rotation", rotation);
    }

    private static bool Pop(RuntimeObject obj, float elapsed)
    {
        var visible = elapsed >= 0;
        if (obj.Get<bool>("visible") != visible) obj.Set("visible", visible);
        if (!visible) return false;
        var pop = AdviceText.RecommendationScale(elapsed);
        obj.Call("SetScale", pop, pop);
        return true;
    }

    // The card's visible top in its own units: the screen's top edge when the card reaches above it. Display coordinates start
    // at the rendered top-left, as its children's do; GObject conversions would measure from an anchored pivot.
    private static float Top(RuntimeObject item) =>
        Math.Max(0f, item.Get("displayObject")!.Result<Vector2, Vector2>("GlobalToLocal", Vector2.zero).y);

    private static RuntimeObject Create(RuntimeObject item)
    {
        var root = NativeUi.Component(item, RootW, RootH);
        // Right above the portrait, so the card's other parts and popups stay over it.
        var role = item.Field("com_Role");
        if (role != null) item.Call("SetChildIndex", root, item.Call("GetChildIndex", role)!.Value<int>() + 1);
        root.Set("touchable", false);
        return root;
    }

    private static Stamp NewStamp(RuntimeObject root, int pick, float x, float at)
    {
        var body = NativeUi.Component(root, StampSize, StampSize);
        body.Set("touchable", false);
        body.Set("visible", false);
        body.Call("SetPivot", 0.5f, 0.5f, false);
        if (Photos[pick].Length == 0)
        {
            // No profile photo to show: the shipped pink thumb disc, without the sender.
            Disc(body, FaceX, FaceY, Radius, Rim, ThumbSize, ThumbY, ThumbOutline);
            Plugin.Diagnostics.Write(FormattableString.Invariant($"receivedLikes.photo missing from={pick + 1}P"));
            return new Stamp(body, null, null, x, at);
        }
        // The game's TextureLoader, which UIManager.InstanceInit makes every package loader, loads the photo through
        // TextureManager.AsyncLoad as the chat bubble's loader does, and releases its texture when disposed.
        var photo = RuntimeObject.New(RuntimeObject.FindClass("UI", "TextureLoader"));
        photo.Set("touchable", false);
        body.Call("AddChild", photo);
        photo.Call("SetSize", PhotoBox, PhotoBox);
        photo.Call("SetXY", FaceX - PhotoBox / 2, FaceY - PhotoBox / 2);
        photo.Set("fill", 1); photo.Set("align", 1); photo.Set("verticalAlign", 1); // LoaderFillType.Scale, centred
        photo.Set("url", Photos[pick]);
        var badge = Disc(body, BadgeX, BadgeY, BadgeRadius, BadgeRim, BadgeThumb, BadgeThumbY, BadgeOutline);
        badge.Set("visible", false);
        return new Stamp(body, badge, photo, x, at);
    }

    // Once the loader has the photo, it gives way to a copy shrunk by area to twice the box (ShapeRaster.Density), which
    // bilinear filtering keeps smooth at its slant and pop: the GPU shrank the 220 texture to the 68 box without mipmaps, and
    // its sticker outline looked broken at FHD (user-reported 2026-11-17). The loader's picture stays if shrinking fails.
    private static void Sharpen(Stamp stamp, int pick)
    {
        var source = stamp.Photo!.Get("texture");
        if (source == null) return; // Still loading.
        var url = Photos[pick];
        if (!Shrunk.TryGetValue(url, out var texture))
        {
            try { texture = Shrink(source); }
            catch (Exception ex) { texture = null; Plugin.Diagnostics.Error("ReceivedLikes.photo", ex); }
            Shrunk[url] = texture;
        }
        if (texture != null)
        {
            var image = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GImage"));
            image.Set("texture", texture);
            image.Set("touchable", false);
            stamp.Body.Call("AddChildAt", image, stamp.Body.Call("GetChildIndex", stamp.Photo)!.Value<int>());
            image.Call("SetSize", PhotoBox, PhotoBox);
            image.Call("SetXY", FaceX - PhotoBox / 2, FaceY - PhotoBox / 2);
            GameUi.Dispose(stamp.Photo);
        }
        stamp.Photo = null;
    }

    // A GPU copy of the loader's texture read back, shrunk by PhotoScale and wrapped in an NTexture, as HomePortrait bakes.
    private static unsafe RuntimeObject Shrink(RuntimeObject source)
    {
        if (source.Field<bool>("rotated")) throw new NotSupportedException("rotated atlas sprite");
        int from = source.Get<int>("width"), to = (int)(PhotoBox * ShapeRaster.Density);
        if (from < to || from > 1024 || source.Get<int>("height") != from) throw new InvalidOperationException("photo size " + from);
        var native = new Texture(source.Get("nativeTexture")!.Pointer);
        var uv = source.Field<Rect>("uvRect");
        var target = RenderTexture.GetTemporary(from, from, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var copy = new Texture2D(from, from, TextureFormat.RGBA32, false);
        byte[] pixels;
        try
        {
            Graphics.Blit(native, target, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, from, from), 0, 0, false);
            var raw = copy.GetRawTextureData();
            pixels = RuntimeObject.Bytes(raw.Pointer);
            GC.KeepAlive(raw);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(copy);
        }
        var shrunk = PhotoScale.Downscale(pixels, from, to);
        var texture = new Texture2D(to, to, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        ShrunkTextures.Add(texture);
        fixed (byte* data = shrunk) texture.LoadRawTextureData((IntPtr)data, shrunk.Length);
        texture.Apply(false, true);
        // NTexture(Texture, Texture alphaTexture, float xScale, float yScale), as SmoothShape and HomePortrait build it.
        var ntexture = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "NTexture"), new RuntimeObject(texture.Pointer), null!, 1f, 1f);
        SmoothShape.Watch(ntexture); // Its materials face the same unused-asset sweep as the atlas's.
        return ntexture;
    }

    // A pink disc with a black rim and the native like thumb in white, outlined in black by sixteen dark copies around it.
    private static RuntimeObject Disc(RuntimeObject parent, float cx, float cy, float radius, float rim, float thumbSize, float thumbY, float outline)
    {
        var disc = NativeUi.Component(parent, radius * 2, radius * 2);
        disc.Set("touchable", false);
        disc.Call("SetPivot", 0.5f, 0.5f, false);
        disc.Call("SetXY", cx - radius, cy - radius);
        SmoothShape.Create(disc, 0f, 0f, radius * 2, radius * 2, Pink, radius, radius, radius, radius, rim, Color.black);
        for (var i = 0; i < 16; i++)
            Thumb(disc, radius + outline * MathF.Cos(i * MathF.PI / 8), radius + thumbY + outline * MathF.Sin(i * MathF.PI / 8), thumbSize, Color.black);
        Thumb(disc, radius, radius + thumbY, thumbSize, Color.white);
        return disc;
    }

    private static void Thumb(RuntimeObject parent, float x, float y, float size, Color color)
    {
        var thumb = NativeUi.Create("BattleSettlement", "点赞")!;
        var scale = size / thumb.Get<float>("width");
        thumb.Call("SetPivot", 0.5f, 0.5f, true);
        thumb.Call("SetScale", scale, scale);
        thumb.Call("SetXY", x, y);
        thumb.Set("color", color);
        thumb.Set("touchable", false);
        parent.Call("AddChild", thumb);
    }

    private static void DrawNote(int index, RuntimeObject? item)
    {
        var label = item?.Field("com_PlayerLabel");
        if (Mutes[index] == MutedSender.None || item == null || label == null || item.Get<bool>("isDisposed")
            || item.Field("step")?.Get<int>("selectedIndex") != 2 || item.Field("SJ_Cut_in")?.Get<bool>("playing") != false)
        { RemoveNote(index); return; }
        var plate = label.Get<float>("y") + PlateInset;
        var note = Notes[index];
        if (note == null || note.Capsule.Root.Get<bool>("isDisposed") || note.Item != item.Pointer || note.Kind != Mutes[index])
        {
            RemoveNote(index);
            var capsule = new NoteUi.Capsule(item, MutedSenders.Note, mutedIcon: true, leadingBadge: false, trailingBadge: true);
            // Right above the portrait, like the stamps: the plate and the card's other parts and popups stay over it.
            var role = item.Field("com_Role");
            if (role != null) item.Call("SetChildIndex", capsule.Root, item.Call("GetChildIndex", role)!.Value<int>() + 1);
            capsule.Root.Set("visible", false);
            capsule.TrailingBadge?.Call("SetPivot", 0.5f, 0.5f, false);
            Notes[index] = note = new Note(item.Pointer, capsule, Mutes[index], Time.unscaledTime + NoteDelay);
            Plugin.Diagnostics.Write(FormattableString.Invariant(
                $"receivedLikes.muted card={index + 1} kind={note.Kind} width={capsule.Width:0} label=({label.Get<float>("x"):0},{label.Get<float>("y"):0}) plate={plate:0}"));
        }
        var t = Time.unscaledTime - note.At;
        var root = note.Capsule.Root;
        if (root.Get<bool>("visible") != t >= 0) root.Set("visible", t >= 0);
        if (t < 0) return;
        var rest = plate - NoteGap - NoteUi.CapsuleHeight;
        root.Call("SetXY", Right - 14f - note.Capsule.Width, rest + (NoteGap + NoteUi.CapsuleHeight) * ReceivedLikesMotion.Rise(t));
        var pulse = ReceivedLikesMotion.Pulse(t);
        note.Capsule.TrailingBadge?.Call("SetScale", pulse, pulse);
    }

    // The description follows the pointer over a note: below it, right-aligned with it, its tail at the "?" badge.
    private static void Hover()
    {
        var root = GameUi.Root;
        var pointer = NativeUi.Pointer();
        var card = -1;
        for (var i = 0; i < Notes.Length && root != null; i++)
            if (Notes[i] is { } n && n.Capsule.Root.Get<bool>("visible") && NativeUi.Contains(n.Capsule.Root, pointer)) { card = i; break; }
        if (card < 0 || root == null) { if (_tip?.Root.Get<bool>("visible") == true) _tip.Root.Set("visible", false); _tipCard = -1; return; }
        var note = Notes[card]!;
        if (_tip == null || _tip.Root.Get<bool>("isDisposed") || _tipCard != card)
        {
            GameUi.Dispose(_tip?.Root);
            _tip = new NoteUi.Description(root, MutedSenders.Note, MutedSenders.Rows(note.Kind), mutedIcon: true);
            _tip.Root.Set("sortingOrder", 28000);
            _tipCard = card;
            _hoverAt = Time.unscaledTime;
        }
        var capsule = note.Capsule;
        var bottomRight = capsule.Root.Call("LocalToRoot", new Vector2(capsule.Width, NoteUi.CapsuleHeight), root)!.Value<Vector2>();
        var badge = capsule.Root.Call("LocalToRoot", new Vector2(capsule.BadgeX, 0f), root)!.Value<Vector2>();
        var x = Math.Clamp(bottomRight.x - NoteUi.Description.Width, 12f, Math.Max(12f, root.Get<float>("width") - NoteUi.Description.Width - 12f));
        _tip.Root.Call("SetXY", x, bottomRight.y + 2f);
        _tip.PointAt(badge.x - x);
        _tip.Root.Set("alpha", AdviceText.Reveal(Time.unscaledTime - _hoverAt - 0.15f));
        _tip.Root.Set("visible", true);
    }

    private static void RemoveNote(int index)
    {
        if (Notes[index] is { } note) GameUi.Dispose(note.Capsule.Root);
        Notes[index] = null;
        if (_tipCard == index) { GameUi.Dispose(_tip?.Root); _tip = null; _tipCard = -1; }
    }

    private static void Remove(int index)
    {
        if (Cards[index] is { } card) GameUi.Dispose(card.Root);
        Cards[index] = null;
    }

    internal static void Clear()
    {
        if (_settlement == IntPtr.Zero && Likes.Empty && Shrunk.Count == 0) return;
        for (var index = 0; index < Cards.Length; index++) { Remove(index); RemoveNote(index); Mutes[index] = MutedSender.None; Photos[index] = ""; }
        GameUi.Dispose(_tip?.Root);
        _tip = null;
        _tipCard = -1;
        // The stamps' images are gone with their cards; then the shrunk photos.
        foreach (var texture in Shrunk.Values) if (texture != null) SmoothShape.Unwatch(texture);
        Shrunk.Clear();
        foreach (var texture in ShrunkTextures) UnityEngine.Object.Destroy(texture);
        ShrunkTextures.Clear();
        Likes.Clear();
        _seen.Clear(); _now.Clear();
        _settlement = IntPtr.Zero;
        _players = 0;
    }
}
