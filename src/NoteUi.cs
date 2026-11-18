using UnityEngine;

namespace BetterAstralParty;

// Mod notes (user-chosen 2026-11-16, draft 나 of artifacts/cc/like-count-drafts v6, recoloured the same day to draft 가 of v9):
// a light grey capsule with darker "/" stripes, outlined in black because it sits on art, with dark text and a light grey "?"
// badge, and a cream description panel with a striped light grey header band, light grey numbered badges, a soft shadow and a
// tail pointing at the badge. The user found the first charcoal band out of place on 2026.11.2-dev.13 and chose light grey
// for every band, capsule and badge. Received Likes' note on a muted sender's card, the Combat Status uncounted-effect tag
// and markers, the KO row's uncounted-effects help and the observer panel's 선택 tag share it.
internal static class NoteUi
{
    internal static readonly Color Grey = new(234f / 255, 231f / 255, 223f / 255, 1f), Stripe = new(222f / 255, 218f / 255, 209f / 255, 1f);
    internal static readonly Color Cream = new(253f / 255, 253f / 255, 241f / 255, 1f);
    internal static readonly Color Ink = new(45f / 255, 45f / 255, 45f / 255, 1f), Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color Clear = new(0f, 0f, 0f, 0f);
    // The stripes: about 8.5 units apart (the settlement's dark bars'), the darker band 45 % of that.
    private const float StripeLength = 8.5f, StripeFill = 0.45f;
    internal const float CapsuleHeight = 34f, BadgeRadius = 11f;
    // A label in the template face centred in a box sits a little low: the line box keeps room for descenders. Centred labels
    // are lifted by this share of their size. Measured on 2026.11.2-dev.13 in game (2026-11-16): with 0.21 (taken from the like
    // pills, another face) the badges' marks at 14 in 22 and the capsule's text at 16 in 34 sat 2 px above the middle, while
    // the header bands' titles at 18 in 42 sat in it, so those keep 0.21 (BandTitleLift).
    internal const float CentreLift = 0.07f, BandTitleLift = 0.21f;

    // A striped light grey shape: the fill, the stripes under a stencil mask whose stepped edge lies maskInset inside, and on
    // top a second copy of the black ring that hides that edge (as the player list tab hides its mask edge). With no ring,
    // whatever is drawn over the shape's edge must hide it.
    internal static RuntimeObject Striped(RuntimeObject parent, float x, float y, float width, float height,
        float topLeft, float topRight, float bottomLeft, float bottomRight, float ring, float maskInset)
    {
        var box = NativeUi.Component(parent, width, height);
        box.Set("touchable", false);
        box.Call("SetXY", x, y);
        SmoothShape.Create(box, 0f, 0f, width, height, Grey, topLeft, topRight, bottomLeft, bottomRight, ring, Color.black);
        var clip = NativeUi.Component(box, width, height);
        clip.Set("touchable", false);
        var mask = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
        mask.Set("touchable", false);
        clip.Call("AddChild", mask);
        mask.Call("SetXY", maskInset, maskInset);
        mask.Call("SetSize", width - 2 * maskInset, height - 2 * maskInset);
        float Inner(float radius) => Math.Max(0f, radius - maskInset);
        mask.Get("shape")!.Call("DrawRoundRect", 0f, Color.white, Color.white, Inner(topLeft), Inner(topRight), Inner(bottomLeft), Inner(bottomRight));
        clip.Set("mask", mask.Get("displayObject")!);
        var size = (int)MathF.Round(height * ShapeRaster.Density);
        var count = Math.Max(1, (int)MathF.Round(height / StripeLength));
        for (var at = 0f; at < width; at += height)
        {
            // Square tiles as tall as the shape: the stripe period divides them, so they continue across the seams.
            var tile = SmoothShape.MaskImage($"stripes.{size}.{count}", size, () => ShapeRaster.Stripes(size, count, StripeFill));
            if (tile == null) break; // Smooth shapes failed: plain grey.
            tile.Set("touchable", false);
            tile.Set("color", Stripe);
            tile.Call("SetSize", height, height);
            tile.Call("SetXY", at, 0f);
            clip.Call("AddChild", tile);
        }
        if (ring > 0) SmoothShape.Create(box, 0f, 0f, width, height, Clear, topLeft, topRight, bottomLeft, bottomRight, ring, Color.black);
        return box;
    }

    // A light grey disc with a black rim and a dark mark: "?" for help or for uncounted effects, or a row number.
    internal static RuntimeObject Badge(RuntimeObject parent, float centreX, float centreY, float radius, string mark = "?")
    {
        var badge = NativeUi.Component(parent, radius * 2, radius * 2);
        badge.Set("touchable", false);
        badge.Call("SetXY", centreX - radius, centreY - radius);
        SmoothShape.Create(badge, 0f, 0f, radius * 2, radius * 2, Grey, radius, radius, radius, radius, 2f, Color.black);
        var size = (int)MathF.Round(radius * 1.3f);
        Text(badge, mark, 0f, -CentreLift * size, radius * 2, radius * 2, size, Ink, 1);
        return badge;
    }

    // A label in the game's own face (NativeUi's template) in one colour without an outline.
    internal static RuntimeObject Text(RuntimeObject parent, string text, float x, float y, float width, float height, int size, Color color, int align = 0)
    {
        var label = NativeUi.Label(parent, text, x, y, width, height, size);
        var format = label.Get("textFormat")!;
        format.SetField("color", color);
        format.SetField("outline", 0f);
        label.Set("textFormat", format);
        label.Set("align", align);
        label.Set("verticalAlign", 1);
        return label;
    }

    // The muted-expression icon of the battle settings window (Common_Internal/屏蔽表情: a white bubble, a red no sign).
    internal static RuntimeObject? MutedIcon(RuntimeObject parent, float centreX, float centreY, float size)
    {
        var icon = NativeUi.Create("Common_Internal", "屏蔽表情");
        if (icon == null) return null;
        icon.Set("touchable", false);
        parent.Call("AddChild", icon);
        icon.Call("SetSize", size, size);
        icon.Call("SetXY", centreX - size / 2, centreY - size / 2);
        return icon;
    }

    // The capsule: [leading icon or badge] text [trailing "?"], CapsuleHeight tall, its width following the text, with the
    // settlement's soft shadow. Returns the component and where the trailing badge's centre is (or the width without one).
    internal sealed class Capsule
    {
        internal readonly RuntimeObject Root;
        internal readonly float Width, BadgeX;
        internal readonly RuntimeObject? TrailingBadge;
        internal Capsule(RuntimeObject parent, string text, bool mutedIcon, bool leadingBadge, bool trailingBadge)
        {
            const float h = CapsuleHeight, ring = 2.5f, lead = 46f, pad = 12f, gap = 10f;
            Root = NativeUi.Component(parent, 10f, h + 3f);
            Root.Set("touchable", false);
            var label = Text(Root, text, 0f, 0f, 400f, h, 16, Ink);
            var textWidth = label.Get<float>("textWidth");
            var left = mutedIcon || leadingBadge ? lead : pad + 4f;
            Width = left + textWidth + gap + (trailingBadge ? 2 * BadgeRadius + 6f : pad - gap + 4f);
            Root.Call("SetSize", Width, h + 3f);
            var shadow = SmoothShape.Create(Root, 0f, 3f, Width, h, Shadow, h / 2, h / 2, h / 2, h / 2);
            var body = Striped(Root, 0f, 0f, Width, h, h / 2, h / 2, h / 2, h / 2, ring, ring / 2);
            // The label first measured the text; it moves above the body now.
            Root.Call("SetChildIndex", shadow, 0);
            Root.Call("SetChildIndex", body, 1);
            label.Call("SetXY", left, 0.5f - CentreLift * 16);
            label.Call("SetSize", textWidth + 8f, h);
            Root.Call("SetChildIndex", label, Root.Get<int>("numChildren") - 1);
            if (mutedIcon) MutedIcon(Root, lead / 2 + 2f, h / 2, 31f);
            else if (leadingBadge) Badge(Root, lead / 2 + 1f, h / 2, BadgeRadius);
            BadgeX = trailingBadge ? Width - 6f - BadgeRadius : Width;
            if (trailingBadge) TrailingBadge = Badge(Root, BadgeX, h / 2, BadgeRadius);
        }
    }

    // The description panel: a tail Tail tall above (or below) a cream body with a striped light grey header band holding the
    // icon and title, then numbered rows. Width is fixed; the height follows the wrapped rows.
    internal sealed class Description
    {
        internal const float Width = 384f, Tail = 9f, Band = 42f, Pad = 16f, RowGap = 12f, Ring = 3f, Radius = 14f, TextLeft = 34f;
        internal readonly RuntimeObject Root;
        internal readonly float Height;
        private readonly RuntimeObject _tail;
        internal Description(RuntimeObject parent, string title, IReadOnlyList<string> rows, bool mutedIcon)
        {
            Root = NativeUi.Component(parent, Width, 100f);
            Root.Set("touchable", false);
            // Rows first, to measure them: wrapped at a fixed width, growing in height.
            var labels = new List<RuntimeObject>();
            foreach (var row in rows)
            {
                var label = Text(Root, row, Pad + TextLeft, 0f, Width - 2 * Pad - TextLeft, 22f, 15, Ink);
                label.Set("verticalAlign", 0);
                label.Set("autoSize", 2);
                labels.Add(label);
            }
            var y = Tail + Band + Pad;
            var tops = new List<float>();
            foreach (var label in labels)
            {
                tops.Add(y);
                y += Math.Max(22f, label.Get<float>("textHeight") + 4f) + RowGap;
            }
            Height = y - RowGap + Pad;
            Root.Call("SetSize", Width, Height + 4f);
            // Back to front: shadow, tail, body, header band, divider, ring, title, rows.
            SmoothShape.Create(Root, 0f, Tail + 4f, Width, Height - Tail, Shadow, Radius, Radius, Radius, Radius);
            _tail = NativeUi.Component(Root, 13f, 13f);
            _tail.Set("touchable", false);
            _tail.Call("SetPivot", 0.5f, 0.5f, true);
            _tail.Set("rotation", 45f);
            SmoothShape.Create(_tail, 0f, 0f, 13f, 13f, Grey, 2f, 2f, 2f, 2f, Ring, Color.black);
            // The body is one outlined shape, so its fill stays under its ring; the band then covers the ring's top and a ring
            // copy goes back over it. The band's own edge lies half the ring inside, under that copy.
            SmoothShape.Create(Root, 0f, Tail, Width, Height - Tail, Cream, Radius, Radius, Radius, Radius, Ring, Color.black);
            Striped(Root, Ring / 2, Tail + Ring / 2, Width - Ring, Band - Ring / 2, Radius - Ring / 2, Radius - Ring / 2, 0f, 0f, 0f, Ring / 2);
            SmoothShape.Create(Root, Ring, Tail + Band, Width - 2 * Ring, 2.5f, Color.black, 0f, 0f, 0f, 0f);
            SmoothShape.Create(Root, 0f, Tail, Width, Height - Tail, Clear, Radius, Radius, Radius, Radius, Ring, Color.black);
            if (mutedIcon) MutedIcon(Root, Pad + 14f, Tail + Band / 2 + 1f, 31f);
            Text(Root, title, Pad + (mutedIcon ? TextLeft : 0f), Tail + 1f - BandTitleLift * 18, Width - 2 * Pad - TextLeft, Band, 18, Ink);
            for (var i = 0; i < labels.Count; i++)
            {
                Badge(Root, Pad + BadgeRadius, tops[i] + BadgeRadius, BadgeRadius, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                labels[i].Call("SetXY", Pad + TextLeft, tops[i]);
                Root.Call("SetChildIndex", labels[i], Root.Get<int>("numChildren") - 1);
            }
            PointAt(Width - 40f);
        }

        // The tail's tip at x (panel units), kept clear of the rounded corners.
        internal void PointAt(float x) => _tail.Call("SetXY", Math.Clamp(x, Radius + 10f, Width - Radius - 10f), Tail + 1.5f);
    }
}
