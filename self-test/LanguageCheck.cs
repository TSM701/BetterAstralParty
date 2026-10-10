using BetterAstralParty;

internal static class LanguageCheck
{
    internal static void Run()
    {
        static void Check(bool valid, string message)
        {
            if (!valid) throw new Exception(message);
        }
        ModText.Select("Auto", "Event");
        Check(ModText.Text(MenuLayout.Help("MuteUnfocused").Title + ": ON") == "Mute When Unfocused: ON", "Mute label and separator in English");
        Check(ModText.Text(MenuLayout.Help("Enabled").Title + ": OFF") == "Battle Advice: OFF", "Toggle separator in English");
        ModText.Select("한국어");
        Check(ModText.Text(MenuLayout.Help("MuteUnfocused").Title + ": ON") == "창 비활성화 시 음소거: ON", "Mute label and separator in Korean");
        ModText.Select("Auto", "Event");
        Check(MenuLayout.HelpScrollOffset(10, 0, 26) == 0, "Short help must stay still");
        Check(MenuLayout.HelpScrollOffset(1.5f, 52, 26) == 0, "Help initial reading pause");
        Check(MenuLayout.HelpScrollOffset(3.5f, 52, 26) == 26, "Help moves one line in two seconds");
        Check(MenuLayout.HelpScrollOffset(7, 52, 26) == 52, "Help bottom reading pause");
        Check(MenuLayout.HelpScrollOffset(7.5f, 52, 26) == 52, "Help starts return without jumping");
        Check(MenuLayout.HelpScrollOffset(9.5f, 52, 26) == 26, "Help returns at the same speed");
        Check(MenuLayout.HelpScrollOffset(11.5f, 52, 26) == 0, "Help finishes return at loop boundary");
        Check(MenuLayout.HelpScrollOffset(12.5f, 52, 26) == 0, "Help top reading pause repeats");
        for (var t = 0f; t < 24f; t += 0.01f)
        {
            var current = MenuLayout.HelpScrollOffset(t, 52, 26);
            var next = MenuLayout.HelpScrollOffset(t + 0.01f, 52, 26);
            Check(current is >= 0 and <= 52 && Math.Abs(next - current) < 0.14f,
                "Help remains bounded and continuous at reversals and loop boundary");
        }
        Check(MenuLayout.HelpScrollOffset(0, 52, 26) == 0, "New hover reset");
        Check(MenuLayout.HelpScrollOffset(3.5f, 104, 52) == 52, "Help speed follows font size");
        Check(!ModText.Korean && ModText.Text("모드 설정") == "Mod Settings", "Unpatched Auto");
        ModText.Select("Auto", "이벤트");
        Check(ModText.Korean, "Patched Auto");
        ModText.Select("English", "이벤트");
        Check(!ModText.Korean, "Manual English overrides patch");
        ModText.Select("한국어", "Event");
        Check(ModText.Korean, "Manual Korean without patch");
        ModText.Select("invalid", "活动");
        Check(ModText.Mode == "Auto" && !ModText.Korean, "Unknown modes/locales safely use English");
        ModText.Select("Auto", "이벤트");
        ModText.Select("Auto");
        Check(ModText.Korean, "Missing sample must not flip auto language");
        ModText.Select("English");
        Check(ModText.Text("KO 최소 조건") == "Minimum KO conditions", "KO title");
        Check(ModText.Text("생존 최소 조건") == "Minimum survival conditions", "Defender survival title");
        Check(ModText.Text("≥") == "≥" && ModText.Text("≤") == "≤", "Language-neutral KO comparisons");
        Check(ModText.Text("KO 불가") == "KO impossible"
            && ModText.Text("확정 KO") == "Guaranteed KO"
            && ModText.Text(MenuLayout.Help("KoMinimum").Body).Contains("best-case"), "Condition status and best-case policy in English");
        ModText.Select("한국어");
        Check(ModText.Text("KO 최소 조건") == "KO 최소 조건"
            && ModText.Text("KO 불가") == "KO 불가", "Condition status in Korean");
        ModText.Select("English");
        Check(ModText.Text("계산 정보 부족") == "Insufficient data", "Unknown data label");
        foreach (var action in new[] { "", "Enabled", "Details", "KoMinimum", "CardPopups", "BattleStatus", "ShushuShield", "FieldBuffs",
            "Names", "Diagnostics", "MuteUnfocused", "FieldZoom", "MatchFocus", "ScaleMinus", "OpacityPlus", "Close", "Language", "General", "Features",
            "UpdateChannel", "UpdateCheck", "UpdateDownload" })
        {
            var (title, body) = MenuLayout.Help(action);
            var localized = ModText.Text(title + "\n" + body);
            Check(!localized.Any(c => c is >= '가' and <= '힣'), "Untranslated help: " + action);
        }
        Check(AdviceText.Quick("예상 피해 1~6\n전투불능 20%").Contains("Damage"), "Quick advice localization");
        Check(!AdviceText.Format("방어 추천\n평균 피해 1~6", 24).Contains("방어"), "Rich advice localization");
        Check(BattleStatusLayout.Counters(2, 3, false).Contains("Turns left"), "Effect counters localization");
        Check(MenuLayout.GeneralControl("Language") && !MenuLayout.GeneralControl("FieldBuffs"), "Tab membership");
        Check(MenuLayout.GeneralControl("MatchFocus") && !MenuLayout.GeneralControl("FieldZoom")
            && !MenuLayout.FixedControl("MatchFocus") && !MenuLayout.FixedControl("FieldZoom"), "Focus is General; wheel zoom is Features");
        foreach (var (action, korean, english) in new[] {
            ("MatchFocus", "매칭 성사 시 창 포커스", "Focus on Match Found"),
            ("FieldZoom", "필드 휠 줌", "Field Wheel Zoom"),
            ("FieldBuffs", "플레이어 필드 인디케이터", "Player Field Indicator") })
        {
            Check(MenuLayout.Options(action).Length == 0, "New preference remains binary: " + action);
            foreach (var enabled in new[] { false, true })
            {
                var state = enabled ? "ON" : "OFF";
                var help = MenuLayout.StateHelp(action, enabled, "");
                ModText.Select("한국어");
                Check(ModText.Text(MenuLayout.Help(action).Title + ": " + state) == korean + ": " + state,
                    "New setting Korean title/state: " + action);
                Check(ModText.Text(help) == help && help.Any(c => c is >= '가' and <= '힣'), "New setting Korean current-state help: " + action);
                ModText.Select("English");
                Check(ModText.Text(MenuLayout.Help(action).Title + ": " + state) == english + ": " + state,
                    "New setting English title/state: " + action);
                Check(ModText.Text(help) != help && !ModText.Text(help).Any(c => c is >= '가' and <= '힣'),
                    "New setting English current-state help: " + action);
            }
        }
        Check(ModText.Text(MenuLayout.StateHelp("MatchFocus", true, "")).Contains("PvE match")
            && ModText.Text(MenuLayout.StateHelp("MatchFocus", true, "")).Contains("taskbar")
            && ModText.Text(MenuLayout.StateHelp("MatchFocus", false, "")).Contains("Does not focus"), "Focus help reflects PvE-only ON and inactive OFF");
        Check(ModText.Text(MenuLayout.StateHelp("FieldZoom", true, "")).Contains("whole field")
            && ModText.Text(MenuLayout.StateHelp("FieldZoom", false, "")).Contains("Does not apply"), "Zoom help reflects whole-field ON and inactive OFF");
        Check(ModText.Text(MenuLayout.StateHelp("FieldBuffs", true, "")).Contains("player names, HP")
            && ModText.Text(MenuLayout.StateHelp("FieldBuffs", true, "")).Contains("public effect icons")
            && ModText.Text(MenuLayout.StateHelp("FieldBuffs", true, "")).Contains("Click for effect details")
            && ModText.Text(MenuLayout.StateHelp("FieldBuffs", false, "")) == "Hides the player field indicator.", "Player field help describes only the current state");
        Check(CardAdvisor.Parse("ATK +1~6.\n(Only for attacker)", 2) == new CardBonus(true, 1, 6, 2), "English ATK");
        Check(CardAdvisor.Parse("DEF +2\n(Only for defender)", 1) == new CardBonus(false, 2, 2, 1), "English DEF");
        foreach (var text in new[] { "ATK +2 (Only for defender)", "ATK +2 and draw a card", "DEF +9~2", "ATK +999" })
            Check(CardAdvisor.Parse(text, 1) == null, "Unsafe English card: " + text);
        ModText.Select("한국어"); // Existing calculation/presentation regression tests retain their baseline.
        foreach (var action in new[] { "Enabled", "Details", "KoMinimum", "CardPopups", "BattleStatus", "ShushuShield",
            "FieldBuffs", "FieldZoom", "MatchFocus", "Names", "Diagnostics", "MuteUnfocused", "HandLayout" })
        {
            var on = MenuLayout.StateHelp(action, true, "Hover");
            var off = MenuLayout.StateHelp(action, false, "Hover");
            Check(on != off, "Help must describe current state: " + action);
            foreach (var body in new[] { on, off })
            {
                ModText.Select("English");
                Check(!ModText.Text(body).Any(c => c is >= '가' and <= '힣'), "State help translation: " + action);
            }
        }
        foreach (var action in new[] { "Language", "InputAttention", "HandLayout" })
        {
            var modes = action == "Language" ? new[] { "Auto", "한국어", "English" }
                : action == "InputAttention" ? new[] { "Off", "Windows", "Taskbar" } : new[] { "Click", "Hover" };
            var bodies = modes.Select(mode => MenuLayout.StateHelp(action, true, mode)).ToArray();
            Check(bodies.Distinct().Count() == modes.Length, "Separate descriptions for every mode: " + action);
            foreach (var body in bodies)
                Check(!ModText.Text(body).Any(c => c is >= '가' and <= '힣'), "Mode help translation: " + action);
        }
        foreach (var action in new[] { "Language", "HandLayout", "InputAttention" })
        {
            var options = MenuLayout.Options(action);
            Check(options.Length == 3 && options.Distinct().Count() == 3, "Three explicit choices: " + action);
            foreach (var option in options)
                Check(action == "Language" && option == "한국어"
                    || !ModText.Text(MenuLayout.OptionLabel(action, option)).Any(c => c is >= '가' and <= '힣'), "Choice translation");
        }
        Check(MenuLayout.Options("Names").Length == 0 && MenuLayout.Options("Enabled").Length == 0, "Binary settings remain toggles");
        Check(MenuLayout.Options("HandLayout").SequenceEqual(new[] { "Native", "Click", "Hover" }), "Hand dropdown order");
        Check(MenuLayout.DropdownY(125, 60, 180) == 189, "Dropdown below");
        Check(MenuLayout.DropdownY(460, 60, 180) == 276, "Dropdown above footer");
        Check(MenuLayout.DropdownY(220, 60, 180, 450) == 36, "Dropdown follows actual footer boundary");
        foreach (var action in new[] { "Language", "HandLayout", "InputAttention" })
        foreach (var option in MenuLayout.Options(action))
        {
            Check(MenuLayout.OptionHelp(action, option) == MenuLayout.StateHelp(action,
                option is not ("Native" or "Off"), option), "Preview is selected-value help: " + option);
            Check(!ModText.Text(MenuLayout.OptionHelp(action, option)).Any(c => c is >= '가' and <= '힣'),
                "Preview description localization");
        }
        Check(MenuLayout.OptionHelp("HandLayout", "Native").Contains("기본 배치"), "Native preview ignores active grouped mode");
        Check(MenuLayout.OptionHelp("InputAttention", "Off").Contains("보내지 않습니다"), "Off alert preview");
        Check(!MenuLayout.StateHelp("Names", true, "").Contains("이명")
            && !MenuLayout.StateHelp("Names", false, "").Contains("본명"), "Names help must not describe the other state");
        Check(!MenuLayout.Help("Details").Body.Contains("KO·생존")
            && MenuLayout.StateHelp("KoMinimum", true, "").Contains("생존 최소 조건")
            && MenuLayout.Options("KoMinimum").Length == 0, "Separate binary indicator help");
        ModText.Select("한국어");
        Console.WriteLine("Korean/English auto/manual modes, UI coverage, tabs and English card parsing passed.");
    }
}
