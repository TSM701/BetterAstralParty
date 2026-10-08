namespace BetterAstralParty;

internal static class FieldNameText
{
    internal static bool HasHan(string name)
    {
        foreach (var rune in name.EnumerateRunes())
            if (rune.Value is 0x3007 or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
                or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x2FA1F or >= 0x30000 and <= 0x3347F)
                return true;
        return false;
    }
}
