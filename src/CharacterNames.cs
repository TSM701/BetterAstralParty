namespace BetterAstralParty;

internal static class CharacterNames
{
    internal static bool SkipSubtree(string type) => type is "UICom_AttrInfo" or "UIButton_Buff";

    // Only standalone hero-name slots verified in the game's UI renderers.
    // Do not replace arbitrary text: chat, player nicknames and biographies are not labels.
    internal static string? LabelField(string type) => type switch
    {
        "UIHero_Button_Hero" or "UIAccountInfo_Button_Hero" => "txt_chrname",
        "UIRoomHero_Com_CharacterName" or "UITutorial_Com_CharacterName" => "txt_Title",
        "UIAccount_Button_FightData" => "txt_HeroName",
        "UIAccountInfo_Button_RankInfo" => "txt_HeroNick",
        "UIAccountInfo_Com_Main" => "txt_Hero", // Favorite character: template variable, not the whole sentence.
        "UIActivityComeback_Hero_Button" => "hero_title",
        "UIActivityStoreSeason_Button_Hero" => "", // GButton.title
        _ => null
    };

    internal static string LabelProperty(string type) => type switch
    {
        "UIAccountInfo_Com_Main" => "data", // GTextField.templateVars["data"]
        "UIActivityStoreSeason_Button_Hero" => "title",
        _ => "text"
    };

    internal static Dictionary<string, string> BuildMap(IEnumerable<(string Epithet, string Name)> names) =>
        names.Where(n => !string.IsNullOrWhiteSpace(n.Epithet))
            .GroupBy(n => n.Epithet, StringComparer.Ordinal)
            // Ambiguous or untranslated entries stay untouched rather than guessing.
            .Where(g => g.Select(n => n.Name).Distinct(StringComparer.Ordinal).Count() == 1)
            .Where(g => !string.IsNullOrWhiteSpace(g.First().Name) && g.Key != g.First().Name)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

    internal static string Restore(string current, string original, string applied) =>
        current == applied ? original : current;
}
