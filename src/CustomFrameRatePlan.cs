namespace BetterAstralParty;

// Custom Frame Rate's decisions without the game (self-test CustomFrameRateCheck): what a typed text means, and which number
// the frame rate gets.
internal static class CustomFrameRatePlan
{
    internal const int Min = 30, Max = 999;

    // The number BAP puts on the frame rate, or 0 when the game's own value stands (OFF, V-Sync on, out of range).
    internal static int Target(bool on, int value, int vSyncCount) => on && vSyncCount == 0 && value >= Min && value <= Max ? value : 0;

    // A typed number, or null for anything else (empty, signs, letters, out of range).
    internal static int? Parse(string? text) =>
        int.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
        && value >= Min && value <= Max ? value : null;
}
