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
        // The assist vote's NPC title, relic target and tip (2026-11-16, from CharacterHandle.GetCharacterNickName's callers).
        "UIAssistVote_Com_NPCInfo" => "txt_Title",
        "UIAssistVote_Com_RelicInfo" => "txt_Target",
        "UIAssistVoteWindow" => "txt_Tip2",
        _ => null
    };

    internal static string LabelProperty(string type) => type switch
    {
        "UIAccountInfo_Com_Main" => "data", // GTextField.templateVars["data"]
        "UIAssistVote_Com_RelicInfo" or "UIAssistVoteWindow" => "target", // GTextField.templateVars["target"]
        "UIActivityStoreSeason_Button_Hero" => "title",
        _ => "text"
    };

    // Template variables the game fills with an epithet alone (AccountInfoWindow.RefreshStatisticalData, AssistVoteWindow).
    internal static bool IsVar(string property) => property is "data" or "target";

    // Slots whose sentence the game builds around an epithet (2026-11-16, user-reported for the achievements): the
    // achievement description (string.Format of its DescId with GetCharacterNickName) and the chat emoji popup's pack title.
    internal static string? SentenceField(string type) => type switch
    {
        "UITask_Com_AchieveLabel" => "txt_taskDesc",
        "UIChat_Com_ExpressionPopup" => "txt_CurrentTitle",
        _ => null
    };

    // Epithets replaced inside sentences: at least three letters, with Hangul and not part of the real name, so collaboration
    // short names (질, 한나, 셰리, 도로시: the real name holds them) and an untranslated epithet never touch ordinary words.
    internal static Dictionary<string, string> SentenceMap(IReadOnlyDictionary<string, string> names) =>
        names.Where(n => n.Key.Length >= 3 && n.Key.Any(c => c is >= '가' and <= '힣') && !n.Value.Contains(n.Key, StringComparison.Ordinal))
            .ToDictionary(n => n.Key, n => n.Value, StringComparer.Ordinal);

    // Every epithet in the text becomes its real name, the longest match first. A text that already holds that real name keeps
    // its epithet ("소심한 수녀 - 알라나", the tutorial's "간판 소녀 미미"), and a particle right after it follows the real name's
    // final consonant (초텐짱을 -> 최고귀염뽀짝천사를). The text comes back unchanged when nothing applies.
    internal static string Sentence(string text, IReadOnlyDictionary<string, string> map)
    {
        if (text.Length == 0 || map.Count == 0) return text;
        System.Text.StringBuilder? result = null;
        var copied = 0;
        for (var i = 0; i < text.Length; i++)
        {
            string? epithet = null;
            foreach (var key in map.Keys)
                if (key[0] == text[i] && key.Length > (epithet?.Length ?? 0) && string.CompareOrdinal(text, i, key, 0, key.Length) == 0
                    && !text.Contains(map[key], StringComparison.Ordinal)) epithet = key;
            if (epithet == null) continue;
            result ??= new System.Text.StringBuilder(text.Length + 16);
            result.Append(text, copied, i - copied).Append(map[epithet]);
            i += epithet.Length;
            copied = i + Particle(text, i, epithet, map[epithet], result);
            i = copied - 1;
        }
        return result == null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    // Writes the particle at text[at] for the real name when it was the epithet's own form; returns how many characters of
    // text it consumed (0: nothing to change). Only Hangul endings decide, and only a particle that ends its word counts
    // (로봇 or 이에요 after an epithet stay).
    private static int Particle(string text, int at, string epithet, string name, System.Text.StringBuilder result)
    {
        bool? Final(string s) => s.Length > 0 && s[^1] is >= '가' and <= '힣' ? (s[^1] - '가') % 28 != 0 : null;
        bool Rieul(string s) => s.Length > 0 && s[^1] is >= '가' and <= '힣' && (s[^1] - '가') % 28 == 8;
        if (at >= text.Length || Final(epithet) is not { } before || Final(name) is not { } after || before == after && Rieul(epithet) == Rieul(name)) return 0;
        var length = string.CompareOrdinal(text, at, "으로", 0, 2) == 0 ? 2 : 1;
        if (text.Length > at + length && char.IsLetterOrDigit(text[at + length])) return 0;
        static bool TakesEuro(bool final, bool rieul) => final && !rieul;
        var particle = text[at];
        string? swap = null;
        if (length == 2) { if (TakesEuro(before, Rieul(epithet)) && !TakesEuro(after, Rieul(name))) swap = "로"; }
        else if (particle == '로') { if (!TakesEuro(before, Rieul(epithet)) && TakesEuro(after, Rieul(name))) swap = "으로"; }
        else
        {
            // The epithet's own form of a pair, then the real name's.
            foreach (var (final, plain) in new[] { ('을', '를'), ('은', '는'), ('과', '와'), ('이', '가') })
                if (particle == (before ? final : plain)) { swap = (after ? final : plain).ToString(); break; }
            if (swap != null && swap[0] == particle) swap = null;
        }
        if (swap == null) return 0;
        result.Append(swap);
        return length;
    }

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
