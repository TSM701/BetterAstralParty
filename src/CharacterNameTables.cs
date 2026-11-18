using System.Diagnostics;

namespace BetterAstralParty;

// Epithets inside the game's other text tables (user-requested 2026-11-16, after the achievements kept "그림자 닌자(으)로 50회
// 플레이하기"): item and emoji names, the battle pass, gacha pools, banners, activities, collaboration and shop names and a
// skill or buff text name characters by epithet (2026-11-12 data: about 600 strings in 12 STR tables). With real names on,
// the language in use of every STR table but STRCharacter, whose epithets are the subtitles character details show beside
// the name, has its epithets replaced in memory by CharacterNames.Sentence, a slice of entries a frame, and put back when
// names turn off or the language or tables change. UIHelper.GetLocal reads the field of GameSettings.languageType (English
// 10, Japanese 22, Korean 23, Simplified 40, Traditional 41; the Korean patch writes its Korean into English); the first
// entry is checked against the entry's own GetLocal, and any other language is left alone.
internal static class CharacterNameTables
{
    // StaticConfigure's STR tables (get_STR<name>), STRCharacter left out.
    private static readonly string[] Tables =
    {
        "Achieve", "Acquisition", "Activity", "Banner", "BattlePass", "BeginTips", "Bot", "Buff", "Build", "Campaign", "Card",
        "Chat", "Chest", "ChoosingTimeLimit", "Collaboration", "Day7GiftPackage", "Destiny", "Dialog", "DiceActivity", "Divination",
        "Dynamic", "Event", "ExchangeStore", "Friend", "Gacha", "GameMode", "GUI", "Guild", "Item", "Land", "LuckyStarBattle", "Map",
        "MapEvent", "Match", "Message", "Mission", "Monster", "MonthlyCard", "Mutator", "Perform", "Player", "ProductRecommendation",
        "PVEMission", "PVENurturance", "RechargeStore", "RechargeStoreAds", "Relic", "Server", "Settings", "SignIn", "SinglePlayer",
        "Skill", "Skin", "SkinSell", "Spectate", "Story", "Summon", "Survey", "Task", "Tutorial", "Voice", "Way", "Welfare",
    };
    private static readonly List<(RuntimeObject Entry, string Property, string Original, string Applied)> Patched = new();
    private static IReadOnlyDictionary<string, string>? _map;
    private static string? _property;
    private static RuntimeObject? _items;
    private static int _table = int.MaxValue;
    private static bool _checked;
    private static IntPtr _configClass;

    private static bool Done => _table >= Tables.Length;

    // Starts over with a new name map; RestoreAll has already put the previous strings back.
    internal static void Begin(IReadOnlyDictionary<string, string> sentences, int language)
    {
        _map = sentences;
        _items = null;
        _checked = false;
        _property = language switch { 10 => "English", 22 => "Japanese", 23 => "Korean", 40 => "Simplified", 41 => "Traditional", _ => null };
        _table = _property == null || sentences.Count == 0 ? Tables.Length : 0;
    }

    // Entries until about a millisecond has passed.
    internal static void Slice()
    {
        if (Done || _map == null || _property == null) return;
        if (_configClass == IntPtr.Zero) _configClass = RuntimeObject.FindClass("", "StaticConfigure");
        var started = Stopwatch.GetTimestamp();
        while (!Done && (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency < 0.001)
        {
            if (_items == null)
            {
                // A table this game version lacks is passed over instead of stopping the names.
                try { _items = RuntimeObject.StaticCall(_configClass, "get_STR" + Tables[_table])?.Get("LocalDict")?.Call("System.Collections.IDictionary.GetEnumerator"); }
                catch (MissingMethodException) { _items = null; }
                if (_items == null) { _table++; continue; }
            }
            for (var n = 0; n < 32; n++)
            {
                if (!_items.Call("MoveNext")!.Value<bool>()) { _items = null; _table++; break; }
                var entry = _items.Get("Value");
                var text = entry?.Get(_property)?.String();
                if (string.IsNullOrEmpty(text)) continue;
                if (!_checked)
                {
                    _checked = true;
                    if (entry!.Call("GetLocal")?.String() != text)
                    {
                        Plugin.Logger.LogWarning($"[캐릭터 이름] 게임 문자열 언어 칸 불일치: {_property}; 문자열 표는 바꾸지 않음");
                        _table = Tables.Length;
                        return;
                    }
                }
                var applied = CharacterNames.Sentence(text, _map);
                if (ReferenceEquals(applied, text)) continue;
                entry!.Set(_property, applied);
                Patched.Add((entry, _property, text, applied));
            }
        }
        if (Done) Plugin.Logger.LogInfo($"[캐릭터 이름] 게임 문자열 {Patched.Count}개 안의 이명을 본명으로 바꿈");
    }

    // Puts back every string still showing what was written; a string the game replaced since stays.
    internal static void Restore()
    {
        foreach (var (entry, property, original, applied) in Patched)
            if (entry.Get(property)?.String() == applied) entry.Set(property, original);
        Patched.Clear();
        _items = null;
        _table = int.MaxValue;
    }
}
