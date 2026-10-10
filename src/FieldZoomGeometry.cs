namespace BetterAstralParty;

internal static class FieldZoomGeometry
{
    internal readonly record struct Point(double X, double Y, double Z);

    // A scaled WorldSpace follow offset moves the camera by (scale - 1) * offset.
    internal static bool TryStep(IReadOnlyList<Point> corners, Point offset, Point follow,
        float fieldOfView, float aspect, float near, float far, float nativeScale, float wheel, out float scale, out float maximum)
    {
        scale = 1; maximum = 0;
        if (corners.Count == 0 || !Finite(offset) || !Finite(follow) || offset.Z >= 0
            || !float.IsFinite(fieldOfView) || fieldOfView <= 0 || fieldOfView >= 180
            || !float.IsFinite(aspect) || aspect <= 0 || !float.IsFinite(near) || near <= 0
            || !float.IsFinite(far) || far <= near || !float.IsFinite(nativeScale) || nativeScale <= 0
            || !float.IsFinite(wheel) || wheel == 0) return false;
        var vertical = Math.Tan(fieldOfView * Math.PI / 360);
        var horizontal = vertical * Math.Min(aspect, 16d / 9);
        var min = new Point(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new Point(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var p in corners)
        {
            if (!Finite(p)) return false;
            min = new(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
        // Cover the field from any in-bounds pan anchor, without changing the limit after a pan.
        var centre = new Point((min.X + max.X) / 2 + offset.X,
            (min.Y + max.Y) / 2 + offset.Y, (min.Z + max.Z) / 2 + offset.Z);
        double fitLow = -1, fitHigh = double.PositiveInfinity;
        double clipLow = -1, clipHigh = double.PositiveInfinity;
        var frustumFits = true;
        var clipFits = true;
        foreach (var p in corners)
        {
            var q = new Point(2 * (p.X - centre.X) + offset.X,
                2 * (p.Y - centre.Y) + offset.Y, 2 * (p.Z - centre.Z) + offset.Z);
            frustumFits &= Limit(horizontal * offset.Z - offset.X, horizontal * q.Z - q.X, ref fitLow, ref fitHigh);
            frustumFits &= Limit(horizontal * offset.Z + offset.X, horizontal * q.Z + q.X, ref fitLow, ref fitHigh);
            frustumFits &= Limit(vertical * offset.Z - offset.Y, vertical * q.Z - q.Y, ref fitLow, ref fitHigh);
            frustumFits &= Limit(vertical * offset.Z + offset.Y, vertical * q.Z + q.Y, ref fitLow, ref fitHigh);
            clipFits &= Limit(offset.Z, p.Z - near, ref clipLow, ref clipHigh);
            clipFits &= Limit(-offset.Z, far - p.Z, ref clipLow, ref clipHigh);
        }
        double nearLow = -1, nearHigh = double.PositiveInfinity;
        if (!Limit(offset.Z, follow.Z - near, ref nearLow, ref nearHigh)
            || !Limit(-offset.Z, far - follow.Z, ref nearLow, ref nearHigh)
            || nearLow > 0 || nearHigh < 0) return false;
        // shortcut: half the native distance is the close-up calibration; verify actor clearance in game.
        var minimum = CeilingFloat(Math.Max(nativeScale * .5d, nearLow + 1));
        var ceiling = CeilingFloat(Math.Max(1, fitLow + 1));
        if (minimum - 1d < nearLow) minimum = MathF.BitIncrement(minimum);
        if (ceiling - 1d < fitLow) ceiling = MathF.BitIncrement(ceiling);
        // Validate the float stop even for a small step, because indicators use this bound too.
        var high = Math.Min(fitHigh, Math.Min(clipHigh, nearHigh));
        var wholeFits = frustumFits && clipFits && float.IsFinite(ceiling) && minimum <= ceiling
            && ceiling - 1d >= clipLow && ceiling - 1d <= high;
        if (wholeFits && !Fits(corners, centre, offset, horizontal, vertical, near, far, ceiling))
        {
            // Exact projection can require the next float even after rounding the interval up.
            var next = MathF.BitIncrement(ceiling);
            wholeFits = next - 1d <= high && Fits(corners, centre, offset, horizontal, vertical, near, far, next);
            if (wholeFits) ceiling = next;
        }
        if (wholeFits) maximum = ceiling;
        else
        {
            var clipped = (float)Math.Min(ceiling, high + 1);
            if (clipped - 1d > high) clipped = MathF.BitDecrement(clipped);
            // Indicators normalize against the verified safe stop, not a claim of whole-field coverage.
            var safeStop = frustumFits && clipFits && float.IsFinite(clipped) && minimum <= clipped
                && Clips(corners, offset, near, far, clipped);
            if (safeStop) maximum = clipped;
            if (wheel > 0) ceiling = 1;
            else
            {
                if (!safeStop) return false;
                ceiling = clipped;
            }
        }
        if (!float.IsFinite(minimum) || minimum <= 0 || minimum > ceiling) return false;
        var requested = Math.Pow(.9, wheel);
        scale = (float)Math.Clamp(requested, minimum, ceiling);
        if (scale - 1d < nearLow) scale = minimum;
        if (scale - 1d > nearHigh || wheel > 0 && scale >= 1 || wheel < 0 && scale <= 1) return false;
        var depth = follow.Z - (scale - 1d) * offset.Z;
        if (depth < near || depth > far) return false;
        return true;
    }

    private static float CeilingFloat(double value)
    {
        var result = (float)value;
        return result < value ? MathF.BitIncrement(result) : result;
    }

    private static bool Limit(double a, double b, ref double low, ref double high)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b)) return false;
        if (a > 0) high = Math.Min(high, b / a);
        else if (a < 0) low = Math.Max(low, b / a);
        else if (b < 0) return false;
        return low <= high;
    }

    private static bool Fits(IReadOnlyList<Point> corners, Point centre, Point d, double h, double v, double near, double far, float scale)
    {
        var t = scale - 1d;
        foreach (var p in corners)
        {
            var x = 2 * (p.X - centre.X) + d.X - t * d.X;
            var y = 2 * (p.Y - centre.Y) + d.Y - t * d.Y;
            var z = 2 * (p.Z - centre.Z) + d.Z - t * d.Z;
            var depth = p.Z - t * d.Z;
            if (Math.Abs(x) > h * z || Math.Abs(y) > v * z || depth < near || depth > far) return false;
        }
        return true;
    }

    private static bool Clips(IReadOnlyList<Point> corners, Point d, double near, double far, float scale)
    {
        var t = scale - 1d;
        foreach (var p in corners)
        {
            var depth = p.Z - t * d.Z;
            if (!double.IsFinite(depth) || depth < near || depth > far) return false;
        }
        return true;
    }

    private static bool Finite(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
