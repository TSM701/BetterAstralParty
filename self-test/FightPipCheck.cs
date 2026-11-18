using BetterAstralParty;

// Fight PiP (FightPip, 2026-11-16/17): the first place lands on the drafted pixels at 1080p, a drag and a corner resize keep
// it on screen in the screen's aspect between a sixth and half of the width with the opposite corner fixed, the saved place
// reads back, it fades in and out quickly, the toolbar step-aside halves it, and bubbles fade only where
// they overlap.
static class FightPipCheck
{
    internal static void Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception("Fight PiP regression: " + name); }
        bool Near(float a, float b, float tolerance = .01f) => Math.Abs(a - b) < tolerance;
        const float W = 1918f, H = 1079f;

        var (x, y, w, h) = FightPipLayout.Rect(FightPipLayout.First, W, H);
        Check(Near(x, 1103f) && Near(y, 113f) && Near(w, 720f) && Near(h, 405.05f), "first place at the drafted pixels");
        var (x2, _, w2, h2) = FightPipLayout.Rect(FightPipLayout.First, 1920f, 1080f);
        Check(Near(w2 / h2, 1920f / 1080f, .001f) && x2 + w2 < 1920f - 80f, "keeps the screen's aspect, left of the toolbar");

        // Moving: follows the pointer, stops at the edges with its frame on screen.
        var moved = FightPipLayout.Rect(FightPipLayout.Moved(FightPipLayout.First, -400f, 200f, W, H), W, H);
        Check(Near(moved.X, 703f) && Near(moved.Y, 313f) && Near(moved.Width, 720f), "a drag moves it by the pointer's travel");
        var corner = FightPipLayout.Rect(FightPipLayout.Moved(FightPipLayout.First, 5000f, -5000f, W, H), W, H);
        Check(Near(corner.X + corner.Width, W - FightPipLayout.Side) && Near(corner.Y, FightPipLayout.Above), "stops at the top-right edge");
        var low = FightPipLayout.Rect(FightPipLayout.Moved(FightPipLayout.First, -5000f, 5000f, W, H), W, H);
        Check(Near(low.X, FightPipLayout.Side) && Near(low.Y + low.Height, H - FightPipLayout.Below), "stops at the bottom-left edge");

        // Resizing from a corner: the opposite corner stays and the aspect is kept.
        var start = FightPipLayout.First;
        var smaller = FightPipLayout.Rect(FightPipLayout.Resized(start, 2, 1103f + 300f, 113f + 200f, W, H), W, H);
        Check(Near(smaller.X + smaller.Width, 1823f) && Near(smaller.Y, 113f) && Near(smaller.Width, 420f) && Near(smaller.Width / smaller.Height, W / H, .001f),
            "the bottom-left corner pulled in keeps the top-right corner");
        var tiny = FightPipLayout.Rect(FightPipLayout.Resized(start, 3, 0f, 0f, W, H), W, H);
        Check(Near(tiny.Width, W * FightPipLayout.MinShare) && Near(tiny.X, 1103f) && Near(tiny.Y, 113f), "not smaller than a sixth, top-left kept");
        var big = FightPipLayout.Rect(FightPipLayout.Resized(start, 2, -5000f, 5000f, W, H), W, H);
        Check(Near(big.Width, W * FightPipLayout.MaxShare) && Near(big.X + big.Width, 1823f) && Near(big.Y, 113f), "not larger than half");
        var edge = FightPipLayout.Rect(FightPipLayout.Resized(start, 1, 5000f, 113f, W, H), W, H);
        Check(edge.X + edge.Width <= W - FightPipLayout.Side + .01f && Near(edge.X, 1103f) && Near(edge.Y + edge.Height, 113f + 405.05f),
            "stops where the dragged corner would leave the screen");
        var pulled = FightPipLayout.Rect(FightPipLayout.Resized(start, 3, 1103f + 1000f, 113f + 200f, W, H), W, H);
        Check(pulled.Width > 720f && Near(pulled.Width / pulled.Height, W / H, .001f), "the larger pull wins");

        // Saved place: reads back, anything else is the first place.
        var spot = new FightPipLayout.Spot(.1234f, .5f, .25f);
        Check(FightPipLayout.Parse(FightPipLayout.Format(spot)) is { } read && Near(read.X, .1234f, .0001f) && Near(read.Y, .5f) && Near(read.Share, .25f),
            "the saved place reads back");
        Check(FightPipLayout.Parse("") == null && FightPipLayout.Parse("1,2") == null && FightPipLayout.Parse("a,b,c") == null
            && FightPipLayout.Parse("NaN,0,0") == null && !FightPipLayout.Format(spot).Contains(' '), "a hand edit falls back to the first place");
        Check(FightPipLayout.Fit(new(-1f, 9f, 2f), W, H).Share == FightPipLayout.MaxShare, "a saved place out of range is pulled back on screen");

        // A quick fade in and out (가 of v4-fade, 2026-11-17): 0.15 s, eased at both ends, nothing before or after it.
        Check(Near(FightPipLayout.FadeSeconds, .15f) && FightPipLayout.Fade(-1f) == 0f && FightPipLayout.Fade(0f) == 0f
            && Near(FightPipLayout.Fade(FightPipLayout.FadeSeconds / 2), .5f) && FightPipLayout.Fade(FightPipLayout.FadeSeconds) == 1f
            && FightPipLayout.Fade(1f) == 1f, "a quick fade in and out");

        Check(FightPipLayout.Live(0f) && FightPipLayout.Live(.59f) && !FightPipLayout.Live(.61f) && !FightPipLayout.Live(.99f)
            && FightPipLayout.Live(1.2f), "live light 0.6 s on, 0.4 s off");
        Check(Near(FightPipLayout.Aside(0f), 1f) && Near(FightPipLayout.Aside(1f), .5f), "step-aside halves it");
        Check(Near(FightPipLayout.Step(0f, 1f, .1f, .25f), .4f) && Near(FightPipLayout.Step(.9f, 1f, .1f, .25f), 1f)
            && Near(FightPipLayout.Step(1f, 0f, .1f, .25f), .6f), "eased progress steps and stops at its target");

        var pip = (1094f, 87f, 738f, 457f);
        Check(FightPipLayout.Overlaps((1600f, 300f, 220f, 70f), pip), "a bubble over it fades");
        Check(!FightPipLayout.Overlaps((1600f, 600f, 220f, 70f), pip), "a bubble under it stays");
        Check(!FightPipLayout.Overlaps((1600f, 544f, 220f, 70f), pip), "touching edges do not overlap");
    }
}
