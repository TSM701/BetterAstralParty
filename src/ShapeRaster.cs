namespace BetterAstralParty;

// Antialiased rounded rectangles for SmoothShape (2026-11-01). One small nine-slice mask per shape: its margins hold the
// corners and the outline, its centre is two pixels that the image stretches; SmoothShape packs the masks into an atlas. The outline runs inside the bounds,
// as in FairyGUI's DrawRoundRect. Each pixel's coverage comes from the signed distance to the edge with a one-pixel ramp.
// Pure arithmetic, so the self-test runs it.
internal static class ShapeRaster
{
    internal const int Density = 2; // Texture pixels per UI unit; the image's textureScale maps them back.

    // Margin in texture pixels: the largest corner or the outline, plus one pixel for the ramp.
    internal static int Margin(float topLeft, float topRight, float bottomLeft, float bottomRight, float line) =>
        (int)MathF.Ceiling(Math.Max(Math.Max(Math.Max(topLeft, topRight), Math.Max(bottomLeft, bottomRight)), line) * Density) + 1;

    internal static int Size(int margin) => 2 * margin + 2;

    // RGBA32 pixels, rows bottom-up as Unity stores them; straight (not premultiplied) colours in 0..1. A transparent pixel
    // keeps the edge colour, so bilinear filtering never darkens the edge.
    internal static byte[] Pixels(int margin, float topLeft, float topRight, float bottomLeft, float bottomRight, float line,
        (float R, float G, float B, float A) fill, (float R, float G, float B, float A) stroke)
    {
        var size = Size(margin);
        var pixels = new byte[size * size * 4];
        var half = size / 2f;
        var width = line * Density;
        var radii = new[] { topLeft * Density, topRight * Density, bottomLeft * Density, bottomRight * Density };
        var edge = line > 0 ? stroke : fill;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // Distance from the pixel centre to the edge (negative inside), y measured down from the top.
                float qx = x + 0.5f - half, qy = y + 0.5f - half;
                var radius = Math.Min(radii[(qy < 0 ? 0 : 2) + (qx < 0 ? 0 : 1)], half);
                float dx = Math.Abs(qx) - (half - radius), dy = Math.Abs(qy) - (half - radius);
                var distance = MathF.Sqrt(Math.Max(dx, 0) * Math.Max(dx, 0) + Math.Max(dy, 0) * Math.Max(dy, 0)) + Math.Min(Math.Max(dx, dy), 0) - radius;
                float outer = Coverage(distance), inner = line > 0 ? Coverage(distance + width) : outer;
                float strokeAlpha = (outer - inner) * stroke.A, fillAlpha = inner * fill.A, alpha = strokeAlpha + fillAlpha;
                var i = ((size - 1 - y) * size + x) * 4;
                pixels[i] = Byte(alpha > 0 ? (strokeAlpha * stroke.R + fillAlpha * fill.R) / alpha : edge.R);
                pixels[i + 1] = Byte(alpha > 0 ? (strokeAlpha * stroke.G + fillAlpha * fill.G) / alpha : edge.G);
                pixels[i + 2] = Byte(alpha > 0 ? (strokeAlpha * stroke.B + fillAlpha * fill.B) / alpha : edge.B);
                pixels[i + 3] = Byte(alpha);
            }
        return pixels;
    }

    // Copies a mask (rows bottom-up) into an atlas page (rows bottom-up) with its top-left at (x, y) counted from the page's
    // top, extruding the mask's edge texels into the padding around it as Clamp would, so filtering at a sprite's edge reads
    // the sprite itself.
    internal static void Blit(byte[] mask, int size, byte[] page, int pageSize, int x, int y, int padding)
    {
        for (var row = -padding; row < size + padding; row++)
            for (var column = -padding; column < size + padding; column++)
            {
                var from = ((size - 1 - Math.Clamp(row, 0, size - 1)) * size + Math.Clamp(column, 0, size - 1)) * 4;
                var to = ((pageSize - 1 - (y + row)) * pageSize + x + column) * 4;
                for (var i = 0; i < 4; i++) page[to + i] = mask[from + i];
            }
    }

    // Coverage of a point (UI units, y down) by a rounded rectangle, with the same one-pixel ramp at Density.
    internal static float RoundRect(float x, float y, float left, float top, float width, float height, float radius)
    {
        float halfX = width / 2, halfY = height / 2;
        float dx = Math.Abs(x - left - halfX) - (halfX - radius), dy = Math.Abs(y - top - halfY) - (halfY - radius);
        var distance = MathF.Sqrt(Math.Max(dx, 0) * Math.Max(dx, 0) + Math.Max(dy, 0) * Math.Max(dy, 0)) + Math.Min(Math.Max(dx, dy), 0) - radius;
        return Coverage(distance * Density);
    }

    // Coverage by the half-plane on the right of the line from a to b (y down: an upward line keeps what lies right of it).
    internal static float HalfPlane(float x, float y, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        var distance = -(dx * (y - ay) - dy * (x - ax)) / MathF.Sqrt(dx * dx + dy * dy);
        return Coverage(distance * Density);
    }

    private static float Coverage(float distance) => Math.Clamp(0.5f - distance, 0f, 1f);
    private static byte Byte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
