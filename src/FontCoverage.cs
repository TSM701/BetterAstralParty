namespace BetterAstralParty;

internal static class FontCoverage
{
    internal static bool NeedsFallback(string text, System.Func<char, bool> hasCharacter)
    {
        foreach (var c in text)
            if (!char.IsWhiteSpace(c) && !char.IsControl(c) && c != '\uFFFC' && !hasCharacter(c)) return true;
        return false;
    }
}
