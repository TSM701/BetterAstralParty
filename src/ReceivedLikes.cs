using System.Globalization;

namespace BetterAstralParty;

// 받은 좋아요 표시 / Received Likes: the likes one settlement has seen, and the stamps' motion (draft 다-1,
// artifacts/cc/like-count-drafts/anim). Cards and senders are pick positions 0-3: the settlement sorts its cards by slot.
internal sealed class LikeLedger
{
    private readonly List<(int Sender, int Target, float At)> _likes = new();

    // One like per sender and card, as the native like button allows; self-likes and players not on the settlement drop out.
    internal bool Add(int sender, int target, float at)
    {
        if (sender is < 0 or > 3 || target is < 0 or > 3 || sender == target) return false;
        foreach (var like in _likes) if (like.Sender == sender && like.Target == target) return false;
        _likes.Add((sender, target, at));
        return true;
    }

    // A card's senders in pick order with the time each like arrived.
    internal List<(int Sender, float At)> For(int target)
    {
        var result = new List<(int Sender, float At)>();
        foreach (var like in _likes) if (like.Target == target) result.Add((like.Sender, like.At));
        result.Sort((a, b) => a.Sender.CompareTo(b.Sender));
        return result;
    }

    internal bool Empty => _likes.Count == 0;
    internal void Clear() => _likes.Clear();
}

// The senders' profile photos (220 square) shrunk for the stamps by area, so the 68-unit icon keeps a smooth sticker outline
// at FHD (user-reported 2026-11-17: the GPU's bilinear shrink of the whole texture, without mipmaps, looked broken). Colour
// is averaged premultiplied, so the transparent texels around the figure do not darken its white outline.
internal static class PhotoScale
{
    // A square RGBA image, from x from pixels to to x to (to <= from); row order is kept.
    internal static byte[] Downscale(byte[] source, int from, int to)
    {
        if (to <= 0 || to > from || source.Length != from * from * 4) throw new ArgumentException("photo size");
        // Premultiplied floats (colour times alpha / 255, alpha 0-255), shrunk across each row, then down each column.
        var image = new float[from * from * 4];
        for (var i = 0; i < from * from; i++)
        {
            var alpha = source[i * 4 + 3];
            for (var c = 0; c < 3; c++) image[i * 4 + c] = source[i * 4 + c] * alpha / 255f;
            image[i * 4 + 3] = alpha;
        }
        var across = Shrink(image, from, from, to, true);
        var both = Shrink(across, from, to, to, false);
        var result = new byte[to * to * 4];
        for (var i = 0; i < to * to; i++)
        {
            var alpha = both[i * 4 + 3];
            for (var c = 0; c < 3; c++) result[i * 4 + c] = alpha > 0f ? (byte)Math.Clamp(MathF.Round(both[i * 4 + c] * 255f / alpha), 0f, 255f) : (byte)0;
            result[i * 4 + 3] = (byte)Math.Clamp(MathF.Round(alpha), 0f, 255f);
        }
        return result;
    }

    // Rows x columns texels shrunk to `to` along the rows (horizontal) or the columns, each output the overlap-weighted
    // average of the texels its span covers.
    private static float[] Shrink(float[] source, int rows, int columns, int to, bool horizontal)
    {
        var length = (float)(horizontal ? columns : rows) / to;
        var outRows = horizontal ? rows : to;
        var outColumns = horizontal ? to : columns;
        var result = new float[outRows * outColumns * 4];
        for (var line = 0; line < (horizontal ? rows : columns); line++)
            for (var o = 0; o < to; o++)
            {
                float start = o * length, end = start + length;
                var at = (horizontal ? line * outColumns + o : o * outColumns + line) * 4;
                for (var s = (int)start; s < end && s < (horizontal ? columns : rows); s++)
                {
                    var weight = (Math.Min(end, s + 1) - Math.Max(start, s)) / length;
                    if (weight <= 0f) continue;
                    var i = (horizontal ? line * columns + s : s * columns + line) * 4;
                    for (var c = 0; c < 4; c++) result[at + c] += source[i + c] * weight;
                }
            }
        return result;
    }
}

internal static class ReceivedLikesMotion
{
    // BattlePraiseMessage.ReadyMsg writes SendMsg as "{MsgType}, {target player id}".
    internal static bool TryTarget(string? message, out long target)
    {
        target = 0;
        var parts = message?.Split(',');
        return parts is { Length: 2 } && long.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out target) && target > 0;
    }

    private static float Ease(float x) => 1 - (1 - x) * (1 - x);
    private static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;

    // Each pick's stamp keeps its own slant, in FairyGUI degrees (clockwise): the draft's Pillow angles (-8, 6, -4, 8) turn
    // counter-clockwise, so their signs flip here. 2026.11.2-dev.8 used them unflipped and slanted every stamp the other way.
    internal static float Slant(int pick) => pick switch { 0 => 8f, 1 => -6f, 2 => 4f, _ => -8f };

    // A new stamp straightens from 12 degrees counter-clockwise of its slant over 0.5 s while it pops, as the draft's did.
    internal static float Lean(float t) => t is >= 0 and < 0.5f ? -12f * (1 - t / 0.5f) : 0f;

    // Stamps already shown slide to their new place over 0.2 s.
    internal static float Slide(float from, float to, float t) => from + (to - from) * Ease(Clamp01(t / 0.2f));

    // Centre of stamp `index` of `count`, the row right-aligned: `spacing` apart, the last one `inset` left of the right edge.
    internal static float StampX(int index, int count, float right, float spacing, float inset) => right - spacing * (count - index) + inset;

    // Muted note entrance 나 (user-chosen 2026-11-16, artifacts/cc/like-count-drafts v7-entrance): the note rises out from
    // behind the name plate over 0.35 s, easing out with a small overshoot (ease-out-back 1.2). The share of its travel still
    // ahead: 1 before it starts, 0 once it rests, below 0 while it overshoots.
    internal static float Rise(float t)
    {
        if (t <= 0) return 1f;
        if (t >= 0.35f) return 0f;
        var x = t / 0.35f - 1;
        return -x * x * (2.2f * x + 1.2f);
    }

    // Then its "?" badge swells once, 1 -> 1.25 -> 1 between 0.75 and 1.05 s, hinting the description under the pointer.
    internal static float Pulse(float t) => t is > 0.75f and < 1.05f ? 1 + 0.25f * MathF.Sin(MathF.PI * (t - 0.75f) / 0.3f) : 1f;
}

// A sender whose likes the stamps cannot show (2026-11-16): the game drops chat, like notices included, from players muted in
// this match (GameLogicManager.lockExpressionPlayerID). The battle settings window's emoji block toggles that list and every
// game start clears it, and BattleLogic.BattleStart then mutes again everyone in the friend list's block list
// (FriendLogic.IsBlack), so a friend-list block lasts every match while a battle-window block lasts this one.
internal enum MutedSender { None, Match, Friend }

internal static class MutedSenders
{
    // The local player cannot mute themself, and bots send no likes, so neither gets a note.
    internal static MutedSender Kind(bool muted, bool self, bool bot, bool friendBlocked) =>
        !muted || self || bot ? MutedSender.None : friendBlocked ? MutedSender.Friend : MutedSender.Match;

    internal const string Note = "보낸 좋아요 미표시";

    // The description's rows: how the player came to be muted, why their likes cannot be counted, and how long it lasts.
    internal static string[] Rows(MutedSender kind) => new[]
    {
        kind == MutedSender.Friend ? "친구 목록에서 차단한 플레이어라 경기마다 이모티콘이 자동으로 차단됩니다." : "이번 경기에서 이 플레이어의 이모티콘을 차단했습니다.",
        "좋아요 알림은 채팅 메시지로 오는데, 게임이 차단한 플레이어의 메시지를 받자마자 버려서 도장으로 셀 수 없습니다.",
        kind == MutedSender.Friend ? "다른 플레이어가 보낸 좋아요는 그대로 표시되고, 친구 목록에서 차단을 풀기 전까지 계속 적용됩니다."
            : "다른 플레이어가 보낸 좋아요는 그대로 표시되고, 이 차단은 이번 경기에만 적용됩니다.",
    };
}
