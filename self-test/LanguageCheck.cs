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
        Check(ModText.Text(MenuLayout.Help("MuteUnfocused").Title + ": ON") == "Mute in Background: ON", "Mute label and separator in English");
        Check(ModText.Text(MenuLayout.Help("Enabled").Title + ": OFF") == "Action Advice: OFF", "Toggle separator in English");
        ModText.Select("한국어");
        Check(ModText.Text(MenuLayout.Help("MuteUnfocused").Title + ": ON") == "백그라운드 음소거: ON", "Mute label and separator in Korean");
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
            && ModText.Text("확정 KO") == "Guaranteed KO", "Condition status in English");
        ModText.Select("한국어");
        Check(ModText.Text("KO 최소 조건") == "KO 최소 조건"
            && ModText.Text("KO 불가") == "KO 불가", "Condition status in Korean");
        ModText.Select("English");
        Check(ModText.Text("계산 정보 부족") == "Insufficient data", "Unknown data label");
        foreach (var action in new[] { "", "Enabled", "Details", "KoMinimum", "CardPopups", "BattleStatus", "ShushuShield", "FieldBuffs",
            "Names", "Diagnostics", "MuteUnfocused", "FieldZoom", "MatchFocus", "Scale", "Opacity", "IndicatorFadeStrength", "FoldedHandOpacity", "Close", "Language", "General", "Features",
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
            ("MatchFocus", "매칭 시 게임 창 띄우기", "Bring Game Forward on Match"),
            ("FieldZoom", "휠 줌·자유 시점", "Wheel Zoom and Free View"),
            ("FieldBuffs", "플레이어 인디케이터", "Player Indicators") })
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
        Check(MenuLayout.StateHelp("MatchFocus", true, "").Contains("PvE"), "Focus help retains the PvE scope");
        Check(MenuLayout.StateHelp("FieldZoom", true, "").Contains("휠")
            && MenuLayout.StateHelp("FieldZoom", true, "").Contains("자유 시점"), "Camera help covers zoom and free movement");
        Check(MenuLayout.StateHelp("FieldBuffs", true, "").Contains("HP")
            && MenuLayout.StateHelp("FieldBuffs", true, "").Contains("공개 효과")
            && MenuLayout.StateHelp("FieldBuffs", true, "").Contains("클릭"), "Player indicator help retains public information and detail access");
        Check(CardAdvisor.Parse("ATK +1~6.\n(Only for attacker)", 2) == new CardBonus(true, 1, 6, 2), "English ATK");
        Check(CardAdvisor.Parse("DEF +2\n(Only for defender)", 1) == new CardBonus(false, 2, 2, 1), "English DEF");
        foreach (var text in new[] { "ATK +2 (Only for defender)", "ATK +2 and draw a card", "DEF +9~2", "ATK +999" })
            Check(CardAdvisor.Parse(text, 1) == null, "Unsafe English card: " + text);
        ModText.Select("한국어"); // Existing calculation/presentation regression tests retain their baseline.
        var booleanActions = new[] { "Enabled", "Details", "KoMinimum", "CardPopups", "BattleStatus", "ShushuShield", "ShushuShieldRestrictions",
            "FieldBuffs", "FieldZoom", "MatchFocus", "Names", "Diagnostics", "MuteUnfocused", "AutoThanks", "CharacterRoles", "CharacterGuide",
            "IndicatorFade", "IndicatorHide", "DiceLock", "CounterSound", "CounterAnimation", "PingFocus", "FinalDamage", "SuppressNotices", "UpdateNotifications" };
        var padding = new[] { "이 기능을 끕니다", "다른 설정", "별도", "independent", "other settings" };
        foreach (var action in booleanActions)
        {
            Check(MenuLayout.Options(action).Length == 0, "Boolean preference remains a toggle: " + action);
            foreach (var language in new[] { "한국어", "English" })
            {
                ModText.Select(language);
                var title = ModText.Text(MenuLayout.Help(action).Title);
                var bodies = new[] { false, true }.Select(enabled => ModText.Text(MenuLayout.StateHelp(action, enabled, ""))).ToArray();
                Check(!string.IsNullOrWhiteSpace(title) && bodies.All(body => !string.IsNullOrWhiteSpace(body))
                    && bodies[0] != bodies[1], "Nonempty distinct current-state help: " + language + "/" + action);
                foreach (var body in bodies)
                    Check(!padding.Any(phrase => body.Contains(phrase, StringComparison.OrdinalIgnoreCase)), "No generic state-help padding: " + language + "/" + action);
                Check(language == "한국어" ? bodies.All(body => body.Any(c => c is >= '가' and <= '힣'))
                    : !title.Any(c => c is >= '가' and <= '힣') && bodies.All(body => !body.Any(c => c is >= '가' and <= '힣')),
                    "Boolean title and state help translated: " + language + "/" + action);
            }
        }
        var sections = MenuLayout.Sections;
        Check(sections.Any(section => section.General) && sections.Any(section => !section.General)
            && sections.Select(section => section.Title).Distinct().Count() == sections.Length, "Both settings tabs have distinct category titles");
        var categoryActions = sections.SelectMany(section => section.Actions).ToArray();
        Check(sections.Single(section => section.Title == "손패·카드").Actions.SequenceEqual(new[] { "HandLayout", "FoldedHandOpacity", "CardPopups" })
            && sections.Single(section => section.Title == "캐릭터").Actions.SequenceEqual(new[] { "Names", "CharacterRoles", "CharacterGuide" })
            && !categoryActions.Contains("CardTargetButtons"), "Cards and characters are separate; target button priority is always applied");
        Check(MenuLayout.SectionBandX + MenuLayout.SectionBandWidth <= 490 && MenuLayout.SectionBandHeight < MenuLayout.SectionHeight, "Category band stays inside the switch column and header row");
        foreach (var action in categoryActions)
        {
            if (MenuLayout.Parent(action) is { } parent)
                Check(categoryActions.Contains(parent) && MenuLayout.Parent(parent) == null && MenuLayout.ToggleWidth(action) + MenuLayout.Indent(action) + MenuLayout.ChildRightInset == 480, "Dependent option sits indented under a top-level parent: " + action);
            if (MenuLayout.Subtitle(action) is not { } subtitle) continue;
            ModText.Select("English");
            var english = ModText.Text(subtitle);
            Check(english != subtitle && !english.Any(c => c is >= '가' and <= '힣'), "Subtitle translated: " + action);
            ModText.Select("한국어");
        }
        Check(categoryActions.Distinct().Count() == categoryActions.Length && booleanActions.All(categoryActions.Contains), "Every boolean preference belongs to exactly one category");
        foreach (var section in sections)
        foreach (var language in new[] { "한국어", "English" })
        {
            ModText.Select(language);
            var title = ModText.Text(section.Title);
            Check(!string.IsNullOrWhiteSpace(title) && (language == "한국어" ? title == section.Title
                : title != section.Title && !title.Any(c => c is >= '가' and <= '힣')), "Category title translation: " + language + "/" + section.Title);
        }
        ModText.Select("English");
        foreach (var action in new[] { "Language", "InputAttention", "HandLayout", "UpdateChannel" })
        {
            var modes = action == "Language" ? new[] { "Auto", "한국어", "English" }
                : action == "InputAttention" ? new[] { "Off", "Windows", "Taskbar" }
                : action == "UpdateChannel" ? new[] { "Stable", "PreRelease", "Beta" } : new[] { "Native", "Click", "Hover" };
            var bodies = modes.Select(mode => MenuLayout.OptionHelp(action, mode)).ToArray();
            Check(bodies.Distinct().Count() == modes.Length, "Separate descriptions for every mode: " + action);
            foreach (var body in bodies)
                Check(!string.IsNullOrWhiteSpace(body) && !ModText.Text(body).Any(c => c is >= '가' and <= '힣')
                    && !padding.Any(phrase => ModText.Text(body).Contains(phrase, StringComparison.OrdinalIgnoreCase)), "Specific translated mode help: " + action);
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
        Check(MenuLayout.OptionHelp("InputAttention", "Off").Contains("알리지 않습니다"), "Off alert preview");
        Check(!MenuLayout.StateHelp("Names", true, "").Contains("이명")
            && !MenuLayout.StateHelp("Names", false, "").Contains("본명"), "Names help must not describe the other state");
        Check(!MenuLayout.Help("Details").Body.Contains("KO·생존")
            && MenuLayout.StateHelp("KoMinimum", true, "").Contains("KO")
            && MenuLayout.StateHelp("KoMinimum", true, "").Contains("생존")
            && MenuLayout.Options("KoMinimum").Length == 0, "Separate binary indicator help");
        foreach (var mode in MenuLayout.Options("AutoUpdate"))
        {
            var (check, download, apply) = MenuLayout.AutoUpdateValues(mode);
            Check(MenuLayout.AutoUpdateMode(check, download, apply) == mode, "Automatic update step round-trips: " + mode);
        }
        Check(MenuLayout.AutoUpdateMode(false, true, true) == "Custom" && MenuLayout.OptionLabel("AutoUpdate", "Custom") == "사용자 지정", "Saved combinations outside the steps stay visible as Custom");
        Check(MenuLayout.ScrollFromMarker(MenuLayout.SettingsY, 390, 1000, 100) == 0 && MenuLayout.ScrollFromMarker(MenuLayout.SettingsY + 290, 390, 1000, 100) == 610            && MenuLayout.ScrollFromMarker(MenuLayout.SettingsY + 999, 390, 1000, 100) == 610 && MenuLayout.ScrollFromMarker(MenuLayout.SettingsY + 50, 390, 300, 100) == 0, "Dragged scroll marker maps to the clamped list position");
        var backupRoot = Path.Combine(Path.GetTempPath(), "bap-settings-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(backupRoot, "keep"); var config = Path.Combine(backupRoot, "game", "mod.cfg");
            Check(!SettingsBackup.RestoreIfMissing(config, folder) && !File.Exists(config), "Nothing to restore leaves the game folder untouched");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, SettingsBackup.SavedName), "saved");
            Check(SettingsBackup.RestoreIfMissing(config, folder) && File.ReadAllText(config) == "saved", "Saved copy restores a missing settings file");
            File.WriteAllText(Path.Combine(folder, SettingsBackup.AutoName), "auto");
            File.WriteAllText(config, "current");
            Check(!SettingsBackup.RestoreIfMissing(config, folder) && File.ReadAllText(config) == "current", "An existing settings file is never overwritten");
            File.Delete(config);
            Check(SettingsBackup.RestoreIfMissing(config, folder) && File.ReadAllText(config) == "auto", "The newest automatic copy wins over the saved one");
            SettingsBackup.Copy(config, Path.Combine(folder, SettingsBackup.SavedName));
            Check(File.ReadAllText(Path.Combine(folder, SettingsBackup.SavedName)) == "auto" && !File.Exists(Path.Combine(folder, SettingsBackup.SavedName) + ".tmp"), "Manual save replaces the saved copy without leftovers");
            SettingsBackup.MarkChanged(1000);
            Check(!SettingsBackup.AutoDue(1500) && SettingsBackup.AutoDue(2000) && !SettingsBackup.AutoDue(3000), "Automatic copy waits for changes to settle and writes once");
        }
        finally { try { Directory.Delete(backupRoot, true); } catch { } }
        Check(MenuLayout.Parent("UpdateAuth") == "UpdateChannel" && MenuLayout.Sections[^1].Actions[^1] == "UpdateSignOut", "Beta sign-in rows sit last under the channel");
        ModText.Select("한국어");
        Console.WriteLine("Korean/English auto/manual modes, UI coverage, tabs and English card parsing passed.");
    }
}
