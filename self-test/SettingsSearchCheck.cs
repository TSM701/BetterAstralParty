using BetterAstralParty;

// Settings search (2026-11-02): matching across names, tags and descriptions, the match-box sentence and its cut, and the
// search list's arrangement (both tabs in one list, grown rows, a head's match lines and its tray).
internal static class SettingsSearchCheck
{
    internal static void Run()
    {
        static void Check(bool valid, string message)
        {
            if (!valid) throw new Exception("Settings search: " + message);
        }
        var words = SettingsSearch.Words("  줌감도  KO ");
        Check(words.SequenceEqual(new[] { "줌감도", "ko" }), "words split on spaces, lower-cased");
        Check(SettingsSearch.Has("줌 감도", "줌감도") && SettingsSearch.Has("KO 주사위", "ko"), "spaces and case do not count");
        Check(SettingsSearch.Ranges("줌 감도와 줌", new[] { "줌감도", "줌" }).SequenceEqual(new[] { (0, 4), (6, 7) }), "ranges map back over spaces and merge");

        string[] Name(string text) => new[] { text };
        Check(SettingsSearch.Match(new[] { "반격" }, Name("반격 아이콘"), "인카운트·전투 반격 아이콘", Name("반격 가능 여부를 보여줍니다.")) is { Boxed: false },
            "a name holding every word needs no box");
        Check(SettingsSearch.Match(new[] { "hp" }, Name("전투 상태"), "전투 HP 아래", Name("손패 장수를 보여줍니다.")) is { Tag: true, Sentence: null },
            "a tag match shows the tag alone");
        var hit = SettingsSearch.Match(new[] { "아이콘" }, Name("전투 상태"), "전투 HP 아래",
            Name("양쪽의 손패 장수·공개 효과와\n반격 가능 여부를 보여줍니다.\n아이콘에 마우스를 올리면 설명이 나옵니다."));
        Check(hit is { Tag: false, Sentence: "아이콘에 마우스를 올리면 설명이 나옵니다." }, "the sentence holding the word, across line breaks");
        Check(SettingsSearch.Match(new[] { "전투", "알림음" }, Name("소리"), "인카운트·전투 반격 아이콘", Name("반격 아이콘이 나타날 때\n알림음을 냅니다.")) is { Tag: true, Sentence: not null },
            "words may come from different fields");
        Check(SettingsSearch.Match(new[] { "전투", "없는말" }, Name("소리"), "인카운트·전투 반격 아이콘", Name("알림음을 냅니다.")) == null, "every word is needed");

        float Width(string text) => text.Length * 10f;
        var sentence = "줌·자유 시점에서도 굴린 주사위를 기본 시점과 같은 위치·크기로 보여줍니다.";
        var cut = SettingsSearch.Fit(sentence, new[] { "기본" }, 200, Width);
        Check(cut.Contains("기본", StringComparison.Ordinal) && Width(cut) <= 200 && cut.StartsWith("…", StringComparison.Ordinal)
            && sentence.Contains(cut.Trim('…'), StringComparison.Ordinal), "a long sentence keeps its match, cut between words: " + cut);
        Check(SettingsSearch.Fit("짧은 문장", new[] { "문장" }, 200, Width) == "짧은 문장", "a sentence that fits stays whole");

        // Both tabs in one list: the General tab's sections follow the feature tabs' instead of restarting at the top.
        var all = MenuLayout.Sections.Select((section, index) => (index, section.Actions)).ToArray();
        var list = MenuLayout.Arrange(all, true);
        var general = Array.FindIndex(MenuLayout.Sections, section => section.General);
        Check(list.Bands[general] > list.Bands[general - 1] && MenuLayout.Default.Bands[general] == 5, "one list for a search, each tab from the top by default");
        Check(all.SelectMany(section => section.Actions).All(action => MenuLayout.SettingY(action) == MenuLayout.Default.Y(action)
            && MenuLayout.RowHeight(action) == MenuLayout.Default.Pitches[action]), "the default arrangement is the settings list");

        // A found counter sound: the counter switch with both chips and one match line, under its master with a grown box.
        var battle = Array.FindIndex(MenuLayout.Sections, section => section.Actions.Contains("CounterSound"));
        var kept = new[] { "BattleStatus", "CounterIcons", "CounterAnimation", "CounterSound" };
        var grow = new Dictionary<string, float> { ["BattleStatus"] = 26 };
        var notes = new Dictionary<string, float> { ["CounterIcons"] = 28 };
        var found = MenuLayout.Arrange(new[] { (battle, kept) }, true, grow, notes);
        Check(found.Y("BattleStatus") == 5 + MenuLayout.SectionHeight && found.Y("CounterIcons") == found.Y("BattleStatus") + 48 + 26 + MenuLayout.TrayPad,
            "a grown top row pushes its options down");
        Check(MenuLayout.HeadHeight(found, "CounterIcons") == MenuLayout.ChildHeight + MenuLayout.LinesTop + MenuLayout.LineHeight + MenuLayout.LinesBottom + 28,
            "a head's match lines lengthen its row");
        Check(MenuLayout.TrayY(found, "BattleStatus") == found.Y("BattleStatus") + (48 + 26) / 2
            && MenuLayout.TrayY(found, "BattleStatus") + MenuLayout.TrayHeight(found, "BattleStatus", "CounterSound")
                == found.Y("CounterIcons") + MenuLayout.HeadHeight(found, "CounterIcons") + MenuLayout.TrayPad,
            "the tray runs from the grown parent's middle to under the last option");
        Check(MenuLayout.TrayY(MenuLayout.Default, "BattleStatus") == MenuLayout.SettingY("StatusHand") + MenuLayout.TrayTop
            && MenuLayout.TrayHeight(MenuLayout.Default, "BattleStatus", "CounterSound") == MenuLayout.TrayHeight("StatusHand", "CounterSound"),
            "the default tray is the settings list's tray");
        Check(MenuLayout.FixedControl("Search") && MenuLayout.FixedControl("SearchClear") && MenuLayout.Help("Search").Title == "설정 검색", "search controls stay outside the list");
        Console.WriteLine("Settings search: matching, sentence cut and search arrangement passed.");
    }
}
