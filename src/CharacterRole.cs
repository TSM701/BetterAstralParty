namespace BetterAstralParty;

internal static class CharacterRole
{
    // CharacterTagType, not character IDs. Unknown future tags stay unlabelled.
    internal static string Label(int tag, bool korean) => (tag, korean) switch
    {
        (2, true) => "딜러", (2, false) => "Attack",
        (4, true) => "서포터", (4, false) => "Support",
        (3, true) => "카드 특화", (3, false) => "Card",
        (5, true) => "탱커", (5, false) => "Tank",
        (1, true) => "범용", (1, false) => "General",
        _ => ""
    };
}
