using BetterAstralParty;

// SmoothShape's texture (2026-11-01): opaque inside, transparent outside, a one-pixel antialiased edge, the outline inside
// the bounds as in FairyGUI's DrawRoundRect, rows bottom-up, and per-corner radii.
static class ShapeRasterCheck
{
    internal static void Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception("Smooth shape raster regression: " + name); }
        var black = (0f, 0f, 0f, 1f);
        var cream = (1f, 0.985f, 0.95f, 1f);
        // A row: radius 13, 3-unit black outline, cream fill.
        var margin = ShapeRaster.Margin(13, 13, 13, 13, 3);
        var size = ShapeRaster.Size(margin);
        Check(margin == 13 * ShapeRaster.Density + 1 && size == 2 * margin + 2, "margin holds the corner plus the ramp, centre is two pixels");
        var pixels = ShapeRaster.Pixels(margin, 13, 13, 13, 13, 3, cream, black);
        Check(pixels.Length == size * size * 4, "RGBA32");
        // x, y from the top-left; Unity rows are bottom-up.
        (byte R, byte G, byte B, byte A) At(byte[] data, int x, int y, int width)
        {
            var i = ((width - 1 - y) * width + x) * 4;
            return (data[i], data[i + 1], data[i + 2], data[i + 3]);
        }
        var centre = At(pixels, size / 2, size / 2, size);
        Check(centre.A == 255 && centre.R == 255 && centre.B == 242, "centre is the opaque fill");
        Check(At(pixels, 0, 0, size).A == 0, "corner outside the arc is transparent");
        var edge = At(pixels, 0, size / 2, size);
        Check(edge.A == 255 && edge.R == 0, "straight edge is the opaque outline up to the bounds");
        // Outline band: 3 units = 6 pixels inside the edge, then the fill.
        Check(At(pixels, 5, size / 2, size).R == 0 && At(pixels, 6, size / 2, size).R == 255, "outline runs inside the bounds");
        var ramp = 0;
        for (var x = 0; x < margin; x++) { var a = At(pixels, x, x, size).A; if (a is > 0 and < 255) ramp++; }
        Check(ramp is >= 1 and <= 2, "the corner arc has a one-pixel antialiased ramp");
        // A transparent outside pixel keeps the outline colour, so bilinear filtering cannot brighten or darken the edge.
        Check(At(pixels, 0, 0, size).R == 0, "transparent edge pixels keep the edge colour");
        // Per-corner radii (tray: square top, round bottom) land on the right rows.
        var tray = ShapeRaster.Margin(0, 0, 12, 12, 1.5f);
        var trayPixels = ShapeRaster.Pixels(tray, 0, 0, 12, 12, 1.5f, cream, black);
        var traySize = ShapeRaster.Size(tray);
        Check(At(trayPixels, 0, 0, traySize).A == 255 && At(trayPixels, 0, traySize - 1, traySize).A == 0, "square top corners, round bottom corners");
        // No outline: the whole shape is the fill colour, white for a tinted shape.
        var plain = ShapeRaster.Pixels(ShapeRaster.Margin(8, 8, 8, 8, 0), 8, 8, 8, 8, 0, (1f, 1f, 1f, 1f), black);
        var plainSize = ShapeRaster.Size(ShapeRaster.Margin(8, 8, 8, 8, 0));
        Check(At(plain, plainSize / 2, plainSize / 2, plainSize) == (255, 255, 255, 255) && At(plain, 0, plainSize / 2, plainSize) == (255, 255, 255, 255), "plain shapes are pure fill");
        // A translucent fill under an opaque outline keeps the outline opaque.
        var glass = ShapeRaster.Pixels(margin, 13, 13, 13, 13, 3, (1f, 1f, 1f, 0.5f), black);
        Check(At(glass, 0, size / 2, size).A == 255 && At(glass, size / 2, size / 2, size).A == 128, "panel opacity changes only the fill");
        // SmoothShape's white masks: a ring is transparent inside and opaque on its band, a fill opaque throughout; both stay white.
        var ring = ShapeRaster.Pixels(margin, 13, 13, 13, 13, 3, (1f, 1f, 1f, 0f), (1f, 1f, 1f, 1f));
        Check(At(ring, size / 2, size / 2, size) == (255, 255, 255, 0) && At(ring, 0, size / 2, size) == (255, 255, 255, 255), "ring mask: clear centre, opaque band, white");
        // Atlas blit: the mask lands with its top-left at (x, y) from the page top, and its edge texels extrude into the padding.
        var tiny = new byte[3 * 3 * 4];
        for (var i = 0; i < 9; i++) { tiny[i * 4] = (byte)(i + 1); tiny[i * 4 + 3] = 255; } // Bottom-up rows: 1..3 bottom, 7..9 top.
        var page = new byte[12 * 12 * 4];
        ShapeRaster.Blit(tiny, 3, page, 12, 5, 4, 2);
        Check(At(page, 5, 4, 12).R == 7 && At(page, 7, 6, 12).R == 3 && At(page, 6, 5, 12).R == 5, "mask lands top-left first, rows in order");
        Check(At(page, 3, 2, 12).R == 7 && At(page, 9, 8, 12).R == 3 && At(page, 4, 5, 12).R == 4, "edge texels extrude into the padding");
        Check(At(page, 2, 2, 12).A == 0 && At(page, 10, 5, 12).A == 0 && At(page, 5, 9, 12).A == 0, "nothing outside the padding is touched");
        // The home portrait clip: a rounded plate and half-planes, antialiased the same way.
        Check(ShapeRaster.RoundRect(100, 50, 2, 6, 343, 166, 12) == 1f && ShapeRaster.RoundRect(3, 7, 2, 6, 343, 166, 12) == 0f
            && ShapeRaster.RoundRect(100, 6, 2, 6, 343, 166, 12) == 0.5f, "rounded clip: inside, outside the corner arc, half on the edge");
        Check(ShapeRaster.HalfPlane(20, -50, 12, -16, 12, -100) == 1f && ShapeRaster.HalfPlane(5, -50, 12, -16, 12, -100) == 0f,
            "an upward line keeps its right side");
        Check(ShapeRaster.HalfPlane(40, -20, 80, 20, 12, -16) == 1f && ShapeRaster.HalfPlane(20, 5, 80, 20, 12, -16) == 0f,
            "a line running up-left keeps what lies above it");
        // Move direction masks (2026-11-06): a notched arrowhead pointing right, widened for its outline bands, and a ground
        // ring whose front (lower) half is solid and whose back (upper) half is dashed, the dashes meeting the solid ends.
        var head = ShapeRaster.Arrowhead(64, 40, 0);
        var rim = ShapeRaster.Arrowhead(64, 40, 7.2f);
        Check(head.Length == 64 * 64 * 4 && At(head, 32, 32, 64) == (255, 255, 255, 255) && At(head, 20, 32, 64).A == 0 && At(head, 55, 32, 64).A == 0,
            "arrowhead: white inside, open notch, nothing past the tip");
        Check(At(rim, 20, 32, 64).A == 255 && At(rim, 58, 32, 64).A == 255 && At(rim, 0, 0, 64).A == 0 && At(rim, 5, 32, 64).A == 0,
            "widened arrowhead fills the notch and passes the tip, inside its mask");
        Check(At(head, 30, 20, 64) == At(head, 30, 43, 64) && At(rim, 12, 8, 64) == At(rim, 12, 55, 64), "arrowhead is symmetric about its axis");
        var groundRing = ShapeRaster.Ring(144, 64, 10.88f, 7.36f, 8);
        var frontOnly = ShapeRaster.Ring(144, 64, 7.36f, 0, 8);
        Check(At(groundRing, 72, 136, 144).A == 255 && At(groundRing, 72, 72, 144).A == 0 && At(groundRing, 136, 72, 144).A == 255 && At(groundRing, 8, 72, 144).A == 255,
            "ring: solid front, clear centre, solid to both ends of the front half");
        Check(At(groundRing, 135, 65, 144).A == 255 && At(groundRing, 132, 52, 144).A == 0 && At(groundRing, 8, 65, 144).A == 255 && At(groundRing, 11, 52, 144).A == 0,
            "ring: back half dashed from both ends, mirror alike");
        Check(At(frontOnly, 72, 136, 144).A == 255 && At(frontOnly, 135, 65, 144).A == 0, "ring without a back band leaves the back half clear");
        // Ground fit: turn * scale * spin rebuilds the axes it was given, squashed, sheared or turned; a plain turn stays one.
        foreach (var (ax, ay, bx, by) in new[] { (1f, 0f, 0f, .55f), (.8f, .35f, -.2f, .5f), (0f, -.55f, 1f, 0f), (-.6f, .2f, -.15f, -.5f) })
        {
            var (turn, sx, sy, spin) = ShapeRaster.Ground(ax, ay, bx, by);
            (float X, float Y) Map(float x, float y)
            {
                float c = MathF.Cos(spin * MathF.PI / 180), s = MathF.Sin(spin * MathF.PI / 180), u = (c * x - s * y) * sx, v = (s * x + c * y) * sy;
                c = MathF.Cos(turn * MathF.PI / 180); s = MathF.Sin(turn * MathF.PI / 180);
                return (c * u - s * v, s * u + c * v);
            }
            var (mx, my) = Map(1, 0);
            var (nx, ny) = Map(0, 1);
            Check(sy > 0 && Math.Abs(mx - ax) + Math.Abs(my - ay) + Math.Abs(nx - bx) + Math.Abs(ny - by) < 1e-4f, $"ground fit rebuilds ({ax}, {ay}), ({bx}, {by})");
        }
        var turned = ShapeRaster.Ground(MathF.Cos(.5f), MathF.Sin(.5f), -MathF.Sin(.5f), MathF.Cos(.5f));
        Check(Math.Abs(turned.ScaleX - 1) + Math.Abs(turned.ScaleY - 1) < 1e-5f && Math.Abs(turned.Turn + turned.Spin - .5f * 180 / MathF.PI) < 1e-3f, "ground fit of a plain turn");
        Check(ShapeRaster.Ground(1, 0, 0, -.5f).ScaleY <= 0, "a mirrored view has no ground fit");
        Console.WriteLine("Smooth shape raster: fill, outline, ring mask, atlas blit, antialiased corners, row order, per-corner radii, clip coverage and move direction masks passed.");
    }
}
