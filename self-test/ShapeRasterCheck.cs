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
        Console.WriteLine("Smooth shape raster: fill, outline, ring mask, atlas blit, antialiased corners, row order, per-corner radii and clip coverage passed.");
    }
}
