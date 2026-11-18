using System.Globalization;

namespace BetterAstralParty;

// 전투 PIP / Fight PiP (code name FightPip), geometry and motion (pure, checked by self-test FightPipCheck). Its first
// place is 가 of artifacts/cc/cctv-fight/v1 (user-chosen 2026-11-16) measured on a 1918 x 1079 capture: the picture 720 x 405
// at 1103, 113. Since 2026-11-17 (user-chosen) the player moves it by dragging and sizes it from any corner, the screen's
// aspect kept, and it remembers that place in the settings file; a thin frame (a black ring of 3, draft 가 of v3-quiet)
// replaced the cream bezel. The motion is that of anim/flow and v2-panels/ga2.
internal static class FightPipLayout
{
    internal const float Scale = 720f / 1918f, LeftShare = 1103f / 1918f, TopShare = 113f / 1079f;
    internal const float Ring = 3f, Pad = Ring, OuterRadius = 10f, InnerRadius = 7f;
    // Entrance and exit: a quick fade and nothing else (가 of artifacts/cc/cctv-fight/v4-fade, user-chosen 2026-11-17,
    // replacing the pop out of the top-right corner, the monitor-like turn-on and turn-off with its letterbox and the drop).
    internal const float FadeSeconds = .15f, GrowSeconds = .45f, AsideSeconds = .25f, BubbleAlpha = .3f;
    // Size between a sixth and half of the screen's width. The frame stays on screen: the ring beside it, the names capsule
    // (26 high) half above it and the "전체 화면" capsule (30 high) half below it. A press moving more than DragStart moves it.
    internal const float MinShare = 1f / 6f, MaxShare = .5f, Side = Ring + 1f, Above = Ring + 13f + 2f, Below = Ring + 15f + 2f;
    internal const float CornerSize = 22f, DragStart = 6f;
    // The 불투명도 slider stops at 20 %, so the window never vanishes while it still takes clicks.
    internal const float LeastOpacity = .2f;

    // Where the PIP rests: its picture's top-left as shares of the root's width and height, and its width as a share of the
    // root's width (its height follows the screen's aspect, so the fight keeps its proportions).
    internal readonly record struct Spot(float X, float Y, float Share);
    internal static Spot First => new(LeftShare, TopShare, Scale);

    internal static (float X, float Y, float Width, float Height) Rect(Spot spot, float rootWidth, float rootHeight) =>
        (spot.X * rootWidth, spot.Y * rootHeight, spot.Share * rootWidth, spot.Share * rootHeight);

    // Kept on screen with its frame, its size between MinShare and MaxShare.
    internal static Spot Fit(Spot spot, float rootWidth, float rootHeight)
    {
        var share = Math.Clamp(spot.Share, MinShare, MaxShare);
        float width = share * rootWidth, height = share * rootHeight;
        var x = Math.Clamp(spot.X * rootWidth, Side, Math.Max(Side, rootWidth - Side - width));
        var y = Math.Clamp(spot.Y * rootHeight, Above, Math.Max(Above, rootHeight - Below - height));
        return new(x / rootWidth, y / rootHeight, share);
    }

    internal static Spot Moved(Spot start, float dx, float dy, float rootWidth, float rootHeight) =>
        Fit(start with { X = start.X + dx / rootWidth, Y = start.Y + dy / rootHeight }, rootWidth, rootHeight);

    // Corner 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right is dragged to the pointer: the opposite corner stays, the
    // aspect is kept (the larger of the two pulls wins), and the size stops where the dragged corner would leave the screen.
    internal static Spot Resized(Spot start, int corner, float px, float py, float rootWidth, float rootHeight)
    {
        var (x, y, width, height) = Rect(start, rootWidth, rootHeight);
        bool left = corner is 0 or 2, top = corner is 0 or 1;
        float ax = left ? x + width : x, ay = top ? y + height : y, aspect = rootWidth / rootHeight;
        var wanted = Math.Max(left ? ax - px : px - ax, (top ? ay - py : py - ay) * aspect);
        var room = Math.Min(left ? ax - Side : rootWidth - Side - ax, (top ? ay - Above : rootHeight - Below - ay) * aspect);
        float least = MinShare * rootWidth, most = Math.Max(least, Math.Min(MaxShare * rootWidth, room));
        var newWidth = Math.Clamp(wanted, least, most);
        var newHeight = newWidth / aspect;
        return Fit(new((left ? ax - newWidth : ax) / rootWidth, (top ? ay - newHeight : ay) / rootHeight, newWidth / rootWidth),
            rootWidth, rootHeight);
    }

    // Saved as "x,y,share" in invariant digits; anything else (empty, a hand edit) is the first place.
    internal static string Format(Spot spot) =>
        string.Create(CultureInfo.InvariantCulture, $"{spot.X:0.####},{spot.Y:0.####},{spot.Share:0.####}");

    internal static Spot? Parse(string? text)
    {
        var parts = text?.Split(',');
        if (parts is not { Length: 3 }) return null;
        var values = new float[3];
        for (var i = 0; i < 3; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i])) return null;
        return new(values[0], values[1], values[2]);
    }

    internal static float Clamp(float u) => u < 0f ? 0f : u > 1f ? 1f : u;
    internal static float InOut(float u) { u = Clamp(u); return u < .5f ? 4f * u * u * u : 1f - MathF.Pow(-2f * u + 2f, 3f) / 2f; }
    internal static float Lerp(float a, float b, float u) => a + (b - a) * u;

    // The fade's progress, 0 to 1 over FadeSeconds, eased at both ends.
    internal static float Fade(float t) { var u = Clamp(t / FadeSeconds); return u * u * (3f - 2f * u); }

    // The live light blinks like a recording light, about once a second: on 0.6 s, off 0.4 s (steady on dev.19-21, blinking
    // again at the user's request, 2026-11-17).
    internal static bool Live(float t) => t - MathF.Floor(t) < .6f;

    // Under an open toolbar panel it shrinks to half towards its bottom-left corner (draft 가-2).
    internal static float Aside(float u) => Lerp(1f, .5f, InOut(u));

    // A step towards a target that takes `seconds` for the whole way, for eased progress values kept between frames.
    internal static float Step(float value, float target, float dt, float seconds)
    {
        var limit = seconds <= 0f ? 1f : dt / seconds;
        var delta = target - value;
        return value + (delta > limit ? limit : delta < -limit ? -limit : delta);
    }

    internal static bool Overlaps((float X, float Y, float Width, float Height) a, (float X, float Y, float Width, float Height) b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
