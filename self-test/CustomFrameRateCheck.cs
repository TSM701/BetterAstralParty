using BetterAstralParty;

// Custom Frame Rate (2026-11-19): a typed number counts only as plain digits from 30 to 999, and the frame rate is BAP's only
// while the switch is on, V-Sync is off and the saved number is in range.
static class CustomFrameRateCheck
{
    internal static void Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception("Custom Frame Rate regression: " + name); }

        Check(CustomFrameRatePlan.Parse("240") == 240 && CustomFrameRatePlan.Parse(" 30 ") == 30 && CustomFrameRatePlan.Parse("999") == 999
            && CustomFrameRatePlan.Parse("0240") == 240, "plain digits in range parse");
        foreach (var text in new[] { null, "", " ", "29", "1000", "-60", "+60", "6O", "60.5", "1,000", "６０" })
            Check(CustomFrameRatePlan.Parse(text) is null, "rejects " + (text ?? "null"));

        Check(CustomFrameRatePlan.Target(true, 240, 0) == 240, "on with V-Sync off applies");
        Check(CustomFrameRatePlan.Target(false, 240, 0) == 0, "off leaves the game's value");
        Check(CustomFrameRatePlan.Target(true, 240, 1) == 0 && CustomFrameRatePlan.Target(true, 240, 2) == 0, "V-Sync leaves the game's value");
        Check(CustomFrameRatePlan.Target(true, 29, 0) == 0 && CustomFrameRatePlan.Target(true, 1000, 0) == 0, "out-of-range saved value is ignored");
    }
}
