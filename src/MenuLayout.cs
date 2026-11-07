namespace BetterAstralParty;

internal static class MenuLayout
{
    internal const float Width = 1060;
    // The settings window grows the native Common/Com_PopUpWindow_Bottom (620 high) so the list shows more rows;
    // the update notice keeps the native frame height. The search row (2026-11-02) sits above the list, so the window
    // grew by its 50 and the list keeps its 530.
    internal const float Height = 810;
    internal const float SettingsX = 430, SettingsY = 170, SettingsWidth = 530, SettingsHeight = 530;
    internal const float SearchY = 120, SearchHeight = 42;
    internal const float FooterY = 710;
    // Left column: help box, then the two tab buttons, grown with the window in proportion (help box +25, the gap to the
    // tabs +3, between the tabs +5, the rest under them).
    internal const float HelpY = 125, HelpHeight = 355, TabY = 498;
    internal const float SectionHeight = 36;
    // Category band spanning the switch column.
    internal const float SectionBandX = 10, SectionBandY = 2, SectionBandWidth = 480, SectionBandHeight = 26;
    internal const int SectionTitleSize = 16;
    // Categories whose features may break with game changes carry a BETA chip on their band.
    internal static bool Experimental(int section) => Sections[section].Title == "실험 기능";
    internal const float HelpTagMaxWidth = 324, HelpTagHeight = 26;
    internal static readonly (string Title, bool General, string[] Actions)[] Sections =
    {
        ("전투 추천", false, new[] { "Enabled", "EncounterAdvice", "DefenseAdvice", "CardAdvice", "Details", "ShootingStarShow", "KoMinimum", "KoCardPreview", "KoSpectator", "FinalDamage" }),
        ("전투 표시", false, new[] { "BattleStatus", "StatusHand", "StatusEffects", "CounterIcons", "CounterAnimation", "CounterSound", "ShushuMaster", "ShushuShield", "ShushuShieldRestrictions" }),
        ("필드·카메라", false, new[] { "FieldBuffs", "FieldMarker", "WalkDirection", "FieldZoom", "ZoomSensitivity", "ZoomOutLimit", "FreeCameraSpeed", "IndicatorFade", "IndicatorFadeStrength", "IndicatorHide", "IndicatorHideDistance", "DiceLock" }),
        ("손패·카드", false, new[] { "HandLayout", "FoldedHandOpacity", "CardPopups" }),
        ("캐릭터", false, new[] { "Names", "CharacterRoles", "CharacterGuide", "PlayerTab", "PlayerTabHand", "PlayerTabRole", "PlayerTabSkill" }),
        ("편의 기능", false, new[] { "AutoThanks", "PingFocus" }),
        ("실험 기능", false, new[] { "OldReplays" }),
        ("화면·언어", true, new[] { "Language", "Scale", "Opacity" }),
        ("알림·소리", true, new[] { "MuteUnfocused", "InputAttention", "MatchFocus", "SuppressNotices" }),
        ("진단", true, new[] { "Diagnostics", "DiagnosticsOpen", "DiagnosticsCollect" }),
        ("설정 보관", true, new[] { "SettingsAutoKeep", "SettingsSave", "SettingsLoad", "SettingsFolder", "SettingsReset" }),
        // Last on its tab: the Beta sign-in rows under the channel show only for the Beta channel, leaving no gap.
        ("업데이트", true, new[] { "Update", "AutoUpdate", "UpdateNotifications", "UpdateDownload", "UpdateChannel", "UpdateAuth", "UpdateSignOut" })
    };
    internal static float SectionY(int index) => Default.Bands[index];
    // Scroll position that puts a category band at the top of the list, clamped to the scrollable range.
    internal static float JumpTarget(int section, float content, float view) => JumpTarget(Default, section, content, view);
    internal static float JumpTarget(Arrangement arrangement, int section, float content, float view) =>
        Math.Clamp(arrangement.Bands[section] - 5, 0, Math.Max(0, content - view));

    // Where each shown row and category band sits, in settings-content y. The default holds every setting, each tab's
    // list starting at the top; a search keeps its rows from both tabs in one list, and a row whose name does not hold
    // the query grows by a match box (Grow), a head by match lines under its options (Notes).
    internal sealed class Arrangement
    {
        internal readonly Dictionary<string, float> Rows = new(), Pitches = new(), Grow = new(), Notes = new();
        internal readonly Dictionary<int, float> Bands = new();
        internal float Y(string action) => Rows[action];
        internal float Extra(string action) => Grow.GetValueOrDefault(action);
        internal float Note(string head) => Notes.GetValueOrDefault(head);
    }

    private static Arrangement? _default;
    internal static Arrangement Default => _default ??= Arrange(Sections.Select((section, index) => (index, section.Actions)));

    // Sections in the given order, each tab's starting at the top, or all in one list (a search).
    internal static Arrangement Arrange(IEnumerable<(int Section, string[] Actions)> shown, bool oneList = false,
        IReadOnlyDictionary<string, float>? grow = null, IReadOnlyDictionary<string, float>? notes = null)
    {
        var arrangement = new Arrangement();
        foreach (var (key, value) in grow ?? new Dictionary<string, float>()) arrangement.Grow[key] = value;
        foreach (var (key, value) in notes ?? new Dictionary<string, float>()) arrangement.Notes[key] = value;
        var tops = new Dictionary<bool, float> { [false] = 5f, [true] = 5f };
        foreach (var (section, actions) in shown.Where(section => section.Actions.Length > 0))
        {
            var tab = !oneList && Sections[section].General;
            var y = tops[tab];
            arrangement.Bands[section] = y;
            y += SectionHeight;
            for (var i = 0; i < actions.Length; i++)
            {
                var pitch = Pitch(actions, i, arrangement);
                arrangement.Rows[actions[i]] = y; arrangement.Pitches[actions[i]] = pitch;
                y += pitch;
            }
            tops[tab] = y + 4;
        }
        return arrangement;
    }
    // Category dots on the scroll track (from its top): proportional to each jump target, pushed at least
    // minGap apart, then pulled back inside the track from the bottom.
    internal static float[] TickYs(float[] targets, float maxScroll, float view, float minGap = 16)
    {
        var ys = targets.Select(t => (maxScroll <= 0 ? 0 : Math.Clamp(t / maxScroll, 0, 1)) * (view - 12) + 6).ToArray();
        for (var i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + minGap);
        for (var i = ys.Length - 1; i >= 0; i--) ys[i] = Math.Min(ys[i], i == ys.Length - 1 ? view - 6 : ys[i + 1] - minGap);
        return ys;
    }
    // Each bubble sits at its own dot. Inside a run of dots closer than BubbleGap the sides alternate right/left
    // (true = left), so same-side neighbours are at least two dot gaps (2 x 16) apart and never overlap.
    internal static bool[] BubbleSides(float[] dots)
    {
        var left = new bool[dots.Length];
        for (int i = 1, run = 0; i < dots.Length; i++) { run = dots[i] - dots[i - 1] < BubbleGap ? run + 1 : 0; left[i] = run % 2 == 1; }
        return left;
    }
    // A press that moves this far on the track becomes a drag instead of a category jump.
    internal const float TickSlop = 5, TickHit = 13, BubbleGap = 31, BubbleHeight = 28;
    internal static float SettingY(string action)
    {
        var row = action switch
        {
            _ when action.EndsWith("Value", StringComparison.Ordinal) && IsSlider(action[..^5]) => action[..^5],
            _ => action
        };
        return Default.Rows.TryGetValue(row, out var y) ? y : throw new ArgumentException("Settings category missing: " + action);
    }
    internal static (float Height, float Y) ScrollMarker(float viewHeight, float contentHeight, float position)
    {
        if (contentHeight <= viewHeight) return (viewHeight, 0);
        var height = Math.Clamp(viewHeight * viewHeight / contentHeight, 24, viewHeight);
        return (height, Math.Clamp(position / (contentHeight - viewHeight), 0, 1) * (viewHeight - height));
    }
    // The marker sits at SettingsX + 505 (12 wide) and itself takes the press, so the band matches it exactly.
    // The track is 18 wide at SettingsX + 505; presses and hovers reach TrackPad further on each side.
    internal const float TrackX = 505, TrackWidth = 18, TrackPad = 8;
    internal static bool OnScrollTrack(float x, float y, float viewHeight) =>
        x >= SettingsX + TrackX - TrackPad && x <= SettingsX + TrackX + TrackWidth + TrackPad && y >= SettingsY && y <= SettingsY + viewHeight;
    internal static float ScrollFromMarker(float thumbTop, float viewHeight, float contentHeight, float thumbHeight, float trackTop = SettingsY) =>
        contentHeight <= viewHeight ? 0 : Math.Clamp((thumbTop - trackTop) / Math.Max(1f, viewHeight - thumbHeight), 0f, 1f) * (contentHeight - viewHeight);
    internal static bool FixedControl(string action) => action is "Close" or "Features" or "General" or "Search" or "SearchClear";
    internal static bool InSettings(float x, float y) => x >= 0 && y >= 0 && x < SettingsWidth && y < SettingsHeight;
    internal static float ContentHeight(float bottom) => Math.Max(SettingsHeight, bottom + 5);

    internal static float ToggleY(int row) => 125 + 65 * row;
    internal const float ToggleHeight = 60;
    // Return buttons keep their native aspect ratio; they are taller than switch toggles.
    internal static float NextTabY(float top, float renderedHeight) => top + renderedHeight + 21f;
    internal static float HelpScrollOffset(float elapsed, float overflow, float lineHeight)
    {
        if (overflow <= 0 || lineHeight <= 0 || elapsed <= 0) return 0;
        var speed = lineHeight / 2f; // One line every two seconds, independent of UI scale.
        var travel = overflow / speed;
        var phase = elapsed % (1.5f + travel * 2 + 2f);
        return phase <= 1.5f + travel
            ? Math.Clamp((phase - 1.5f) * speed, 0, overflow)
            : overflow - Math.Clamp((phase - 1.5f - travel - 2f) * speed, 0, overflow);
    }
    // Options that only act while another option is ON sit in a tray under it, joined by a guide line, and dim while it is OFF.
    internal static string? Parent(string action) => action switch
    {
        "Details" or "EncounterAdvice" or "DefenseAdvice" or "CardAdvice" or "ShootingStarShow" => "Enabled",
        "KoCardPreview" or "KoSpectator" => "KoMinimum",
        "StatusHand" or "StatusEffects" or "CounterIcons" or "CounterAnimation" or "CounterSound" => "BattleStatus",
        "ShushuShield" or "ShushuShieldRestrictions" => "ShushuMaster",
        "FieldMarker" => "FieldBuffs",
        "ZoomSensitivity" or "ZoomOutLimit" or "FreeCameraSpeed" or "IndicatorFade" or "IndicatorFadeStrength" or "IndicatorHide" or "IndicatorHideDistance" or "DiceLock" => "FieldZoom",
        "FoldedHandOpacity" => "HandLayout",
        "PlayerTabHand" or "PlayerTabRole" or "PlayerTabSkill" => "PlayerTab",
        "UpdateAuth" or "UpdateSignOut" => "UpdateChannel",
        _ => null
    };
    // A slider or switch tuning a child switch sits in a smaller, darker capsule under that switch inside the parent tray.
    internal static string? SubParent(string action) => action switch
    {
        "IndicatorFadeStrength" => "IndicatorFade",
        "IndicatorHideDistance" => "IndicatorHide",
        "CounterAnimation" or "CounterSound" => "CounterIcons", // Both only shape the counter icon (2026-10-23).
        _ => null
    };
    // Settings rows (draft B, 2026-11-01): one grid. Top rows span the column at 48 high; their options sit 40 high in a
    // tray inset 8 + 14. Two visible levels only (hierarchy draft 3, 2026-11-01): a child's own options sit on lines
    // inside its row under a divider, a slider on a whole line and switches as chips, two to a line, 192 wide each.
    internal const float RowX = 10, RowWidth = 480, TopHeight = 48, ChildHeight = 40, ChildX = 32, ChildWidth = 436;
    internal const float RowGap = 8, TrayPad = 10, LineHeight = 38, LinesTop = 6, LinesBottom = 2;
    internal const float ChipX = ChildX + 30, ChipHeight = 30, ChipGap = 8, ChipWidth = (ChildWidth - 30 - 14 - ChipGap) / 2;
    private static readonly HashSet<string> Heads = Sections.SelectMany(section => section.Actions).Select(SubParent).OfType<string>().ToHashSet();
    // A child switch whose own options sit on lines inside its row.
    internal static bool Head(string action) => Heads.Contains(action);
    private static bool Chip(string action) => SubParent(action) != null && !IsSlider(action);
    // A chip's column: switches under one head fill two columns left to right; a slider line restarts at column 0.
    internal static int ChipColumn(string action)
    {
        var (actions, index) = Locate(action);
        var column = 0;
        for (var i = index - 1; i >= 0 && Chip(actions[i]) && SubParent(actions[i]) == SubParent(action); i--) column++;
        return column % 2;
    }
    // Left edge (settings-content x) and width of a row; a sub slider's line starts 12 in so its label sits under the chips.
    internal static (float X, float Width) RowBox(string action) =>
        Parent(action) == null ? (RowX, RowWidth)
        : Chip(action) ? (ChipX + ChipColumn(action) * (ChipWidth + ChipGap), ChipWidth)
        : SubParent(action) != null ? (ChildX + 12, ChildWidth - 12)
        : (ChildX, ChildWidth);
    internal static float ToggleWidth(string action) => RowBox(action).Width;
    internal static float Indent(string action) => RowBox(action).X - RowX;
    // The update row is taller: its state and last check time sit under its name (draft 2+3, 2026-11-01).
    internal static float ToggleHeightFor(string action) => action == "Update" ? UpdateHeight : Parent(action) == null ? TopHeight
        : Chip(action) ? ChipHeight : SubParent(action) != null ? LineHeight : ChildHeight;
    internal const float UpdateHeight = 60;
    // Chips sit centred on their line.
    internal static float ChildOffsetY(string action) => Chip(action) ? (LineHeight - ChipHeight) / 2 : 0;
    // Name text starts 18 inside a row (12 inside a chip); switch tracks sit 14 inside a row's right edge (6 in a chip).
    internal static float TextX(string action) => Chip(action) ? 12 : 18;
    internal static int TextSize(string action) => Parent(action) == null ? 21 : SubParent(action) != null ? 16 : 18;
    internal static (float Width, float Height) TrackSize(string action) => Parent(action) == null ? (64, 32) : Chip(action) ? (42, 21) : (54, 27);
    internal static float TrackInset(string action) => Chip(action) ? 6 : 14;
    internal static float RowRadius(string action) => Chip(action) ? ChipHeight / 2 : 13;
    // Pitch from a row's slot to the next one. A top row with options leaves the tray's padding under it; the last row
    // of a tray adds the padding and the gap. A head leaves its divider gap; each of its lines is one LineHeight, a chip
    // in column 0 shares its line with a following column-1 chip, and the last line adds the row's bottom padding.
    internal static float RowHeight(string action) => Default.Pitches[action];
    // Neighbours are the shown rows (a search keeps a head with all its options); a grown row adds its match box, and a
    // head's last option line adds the head's match lines.
    private static float Pitch(string[] actions, int index, Arrangement arrangement)
    {
        var action = actions[index];
        var next = index + 1 < actions.Length ? actions[index + 1] : null;
        if (Parent(action) == null) return ToggleHeightFor(action) + arrangement.Extra(action) + (next != null && Parent(next) == action ? TrayPad : RowGap);
        if (Head(action)) return ChildHeight + arrangement.Extra(action) + LinesTop;
        var owner = SubParent(action);
        var sameRow = next != null && owner != null && SubParent(next) == owner;
        var height = owner == null ? ChildHeight + arrangement.Extra(action)
            : sameRow && Chip(action) && Chip(next!) && ChipColumn(action) == 0 ? 0 : LineHeight;
        if (sameRow) return height;
        if (owner != null) height += LinesBottom + arrangement.Note(owner);
        return height + (next != null && Parent(next) == Parent(action) ? RowGap : TrayPad + RowGap);
    }
    private static (string[] Actions, int Index) Locate(string action)
    {
        foreach (var section in Sections)
            if (Array.IndexOf(section.Actions, action) is var index and >= 0) return (section.Actions, index);
        throw new ArgumentException("Settings category missing: " + action);
    }
    // Pale tray behind a parent's options, from the parent's middle (its round ends hide the tray's square corners) to
    // TrayPad under the last option.
    internal const float TrayX = RowX + 8, TrayWidth = RowWidth - 16, TrayTop = -(TrayPad + TopHeight / 2);
    internal static float TrayHeight(string first, string last) => SettingY(last) + RowHeight(last) - RowGap - SettingY(first) - TrayTop;
    // The same for a parent and its last shown option, starting at the parent's middle however tall it grew.
    internal static float TrayY(Arrangement arrangement, string parent) => arrangement.Y(parent) + (TopHeight + arrangement.Extra(parent)) / 2;
    internal static float TrayHeight(Arrangement arrangement, string parent, string last) =>
        arrangement.Y(last) + arrangement.Pitches[last] - RowGap - TrayY(arrangement, parent);
    // A head's row reaches LinesBottom (and its match lines) under its last option line.
    internal static float HeadHeight(string head) => HeadHeight(Default, head);
    internal static float HeadHeight(Arrangement arrangement, string head)
    {
        var (actions, index) = Locate(head);
        var last = head;
        for (var i = index + 1; i < actions.Length && SubParent(actions[i]) == head; i++) last = actions[i];
        return last == head ? ChildHeight + arrangement.Extra(head)
            : arrangement.Y(last) + LineHeight + LinesBottom + arrangement.Note(head) - arrangement.Y(head);
    }
    // Short "where / what" tag shown above the hover help body.
    internal static string? Subtitle(string action) => action switch
    {
        "Enabled" => "전투·인카운트 선택 버튼",
        "Details" => "추천 아이콘에 마우스",
        "EncounterAdvice" => "인카운트 선택 버튼",
        "DefenseAdvice" => "방어·회피 버튼",
        "CardAdvice" => "전투 손패 카드",
        "ShootingStarShow" => "인카운트 놓아주기 버튼",
        "KoCardPreview" => "전투 손패 카드",
        "KoSpectator" => "다른 사람 전투",
        "StatusHand" => "전투 HP 아래",
        "StatusEffects" => "전투 HP 아래",
        "CounterIcons" => "인카운트·전투 반격 아이콘",
        "ShushuMaster" => "전투 캐릭터",
        "KoMinimum" => "전투 화면 가운데",
        "FinalDamage" => "전투 결과의 HP 옆",
        "BattleStatus" => "전투 HP 아래",
        "ShushuShield" => "전투 캐릭터",
        "ShushuShieldRestrictions" => "내 방어·회피 선택",
        "CounterAnimation" => "인카운트·전투 반격 아이콘",
        "CounterSound" => "인카운트·전투 반격 아이콘",
        "FieldBuffs" => "필드 캐릭터 위",
        "FieldMarker" => "필드 캐릭터 위",
        "WalkDirection" => "필드 캐릭터 발밑",
        "FieldZoom" => "필드",
        "IndicatorFade" => "필드 인디케이터",
        "IndicatorHide" => "필드 인디케이터",
        "DiceLock" => "필드 주사위",
        "HandLayout" => "내 필드 손패",
        "CardPopups" => "카드·칩 핑 말풍선",
        "Names" => "캐릭터 이름이 나오는 곳",
        "CharacterRoles" => "준비 화면 초상화",
        "CharacterGuide" => "준비 화면 초상화에 마우스",
        "PlayerTab" or "PlayerTabHand" or "PlayerTabRole" or "PlayerTabSkill" => "필드 플레이어 목록",
        "AutoThanks" => "PvE 퀵 채팅",
        "PingFocus" => "핑 선택 모드",
        "OldReplays" => "전적 리플레이",
        "Language" => "모드 글자 전체",
        "Scale" => "모드 전투 패널",
        "IndicatorFadeStrength" or "IndicatorHideDistance" => "필드 인디케이터",
        "ZoomSensitivity" or "ZoomOutLimit" or "FreeCameraSpeed" => "필드",
        "FoldedHandOpacity" => "내 필드 손패",
        "Opacity" => "모드 전투 패널",
        "MuteUnfocused" => "다른 창을 쓰는 동안",
        "InputAttention" => "게임 창이 뒤에 있을 때",
        "MatchFocus" => "PvE 매칭·방 시작",
        "SuppressNotices" => "로그인 직후",
        "UpdateChannel" => "모드 업데이트",
        "Update" or "AutoUpdate" => "모드 업데이트",
        "SettingsAutoKeep" => "내 PC",
        "UpdateNotifications" => "홈 화면",
        "Diagnostics" => "내 PC",
        _ => null
    };
    // Native Setting_Com_Slider is 1010x68 (bar 997x54, grip 100x100 at y -16); placed in settings-content units.
    // A slider row is a switch row with its name at the left, the native bar after the 98-wide label (as on the draft,
    // 128 in) and the value right-aligned 18 inside the row; bar and label are centred on the row.
    internal static float SliderCenterY(string action) => ToggleHeightFor(action) / 2;
    internal static float SliderOffsetY(string action) => SliderCenterY(action) - 8;
    internal static float SliderLabelOffsetY(string action) => SliderCenterY(action) - 17.5f;
    internal static float SliderLabelX(string action) => RowBox(action).X + TextX(action);
    internal static float SliderBarX(string action) => RowBox(action).X + 128;
    internal static float SliderValueX(string action) => RowBox(action).X + RowBox(action).Width - 18 - SliderValueWidth;
    internal const float SliderValueWidth = 58;
    // Every slider uses the top-level scale, so bar thickness and grip size match; narrower rows get a shorter bar
    // (the native GSlider recomputes its bar from its width). The grip overhangs the bar end, so leave room before the value.
    internal const float SliderScale = 250f / 1010f;
    internal static float SliderBarWidth(string action) => RowBox(action).Width - 214;
    internal static readonly string[] Sliders = { "FieldMarker", "ZoomSensitivity", "ZoomOutLimit", "FreeCameraSpeed", "IndicatorFadeStrength", "IndicatorHideDistance", "FoldedHandOpacity", "Scale", "Opacity" };
    internal static bool IsSlider(string action) => Array.IndexOf(Sliders, action) >= 0;
    internal static (int Min, int Max) SliderRange(string action) => action switch
    {
        "Scale" => (75, 150), "ZoomSensitivity" or "FreeCameraSpeed" => (10, 300), "ZoomOutLimit" => (0, 100), "IndicatorHideDistance" => (0, 100), _ => (0, 100)
    };
    internal static string SliderLabel(string action) => action switch
    {
        "FieldMarker" => "위치 조정", "IndicatorFadeStrength" => "반투명 정도", "FoldedHandOpacity" => "불투명도",
        "ZoomSensitivity" => "줌 감도", "ZoomOutLimit" => "최대 줌 아웃", "FreeCameraSpeed" => "WASD 속도", "IndicatorHideDistance" => "숨김 거리",
        "Scale" => "표시 크기", _ => "배경 불투명도"
    };
    // Culture P0 can insert a space ("50 %") that wraps the sign; always "50%".
    internal static string Percent(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    internal static string[] Options(string action) => action switch
    {
        "Language" => new[] { "Auto", "한국어", "English" },
        "HandLayout" => new[] { "Native", "Click", "Hover" },
        "WalkDirection" => new[] { "Off", "Arrows", "Circles" },
        "InputAttention" => new[] { "Taskbar", "Windows", "Off" },
        "UpdateChannel" => new[] { "Stable", "PreRelease", "Beta" },
        "AutoUpdate" => new[] { "Off", "Check", "Download", "Apply" },
        _ => Array.Empty<string>()
    };
    // Move Direction Highlight's saved value; anything else (a hand edit, an unknown value) is the default, Arrows.
    internal static string WalkDirectionMode(string? value) => value is "Off" or "Circles" ? value : "Arrows";
    internal static string OptionLabel(string action, string value) => action switch
    {
        "Language" => value == "Auto" ? "자동" : value,
        "HandLayout" => value switch { "Click" => "클릭 펼치기", "Hover" => "호버 펼치기", _ => "기본" },
        "WalkDirection" => value switch { "Off" => "끄기", "Circles" => "원과 화살표", _ => "화살표만" },
        "InputAttention" => InputAttentionState.Label(value),
        "UpdateChannel" => value switch { "PreRelease" => "프리 릴리즈", "Beta" => "비공개 베타", _ => "릴리즈" },
        "AutoUpdate" => value switch { "Off" => "끄기", "Check" => "확인만", "Download" => "자동 받기", "Apply" => "자동 적용", _ => "사용자 지정" },
        _ => value
    };
    // Automatic update steps over the three saved switches; other saved combinations show as Custom until changed.
    internal static string AutoUpdateMode(bool check, bool download, bool apply) => (check, download, apply) switch
    {
        (false, false, false) => "Off", (true, false, false) => "Check", (true, true, false) => "Download", (true, true, true) => "Apply", _ => "Custom"
    };
    internal static (bool Check, bool Download, bool Apply) AutoUpdateValues(string mode) => mode switch
    {
        "Off" => (false, false, false), "Check" => (true, false, false), "Download" => (true, true, false), _ => (true, true, true)
    };
    // A white card with 6 of padding around 44-high choices, 4 apart (dropdown draft 1, 2026-11-01).
    internal const float ChoiceHeight = 44, ChoicePitch = 48;
    internal static float DropdownHeight(string action) => Options(action).Length * ChoicePitch + 8;
    internal static float DropdownY(float top, float buttonHeight, float height, float bottom = FooterY) =>
        top + buttonHeight + height + 4 <= bottom ? top + buttonHeight + 4 : Math.Max(8, top - height - 4);
    internal static string OptionHelp(string action, string value) =>
        StateHelp(action, value is not ("Native" or "Off"), value);
    internal static bool GeneralControl(string action) => action is "Language" or "Diagnostics" or "DiagnosticsOpen" or "DiagnosticsCollect" or "MuteUnfocused" or "InputAttention" or "MatchFocus"
        or "UpdateAuth" or "UpdateSignOut"
        or "Update" or "AutoUpdate" or "UpdateChannel" or "UpdateSwitch" or "UpdateCheck" or "UpdateDownload" or "UpdateGet" or "UpdateCancel"
        or "SuppressNotices" or "UpdateNotifications" or "SettingsAutoKeep" or "SettingsSave" or "SettingsLoad" or "SettingsFolder" or "SettingsReset"
        or "Scale" or "Opacity";

    internal static string StateHelp(string action, bool enabled, string mode) => action switch
    {
        "Language" => mode switch { "한국어" => "모드 안내를 한국어로 표시합니다.", "English" => "모드 안내를 영어로 표시합니다.", _ => "모드 안내 언어를 게임 메뉴에 맞춥니다." },
        "Names" => enabled ? "본명으로 표시합니다." : "이명으로 표시합니다.",
        "HandLayout" => !enabled ? "게임 기본 배치로 보여줍니다." : mode == "Click" ? "클릭한 묶음을 가로로 펼치고\n다른 곳을 클릭하면 접습니다." : "마우스를 올린 묶음을 위로 펼치고\n벗어나면 접습니다.",
        "WalkDirection" => mode switch
        {
            "Off" => "게임 기본 표시만 보여줍니다.",
            "Circles" => "발밑 원과 이동 방향 화살표를\n캐릭터보다 위에 그립니다.\n캐릭터 뒤쪽 원은 점선입니다.",
            _ => "발밑 원의 이동 방향 화살표를\n캐릭터보다 위에 그립니다."
        },
        "ShushuShield" when !enabled => "쉴드 효과를 그리지 않습니다.",
        "ShushuShieldRestrictions" when !enabled => "방어 버튼과 카드를 잠그지 않습니다.",
        "AutoThanks" when !enabled => "자동으로 보내지 않습니다.",
        "CharacterRoles" when !enabled => "역할을 보여주지 않습니다.",
        "CharacterGuide" when !enabled => "설명을 보여주지 않습니다.",
        "PlayerTab" when !enabled => "탭을 보여주지 않습니다.",
        "PlayerTabHand" when !enabled => "손패 장수를 보여주지 않습니다.",
        "PlayerTabRole" when !enabled => "역할을 보여주지 않습니다.",
        "PlayerTabSkill" when !enabled => "스킬 쿨타임을 보여주지 않습니다.",
        "IndicatorFade" when !enabled => "줌 아웃해도 그대로 보여줍니다.",
        "IndicatorHide" when !enabled => "거리와 상관없이 보여줍니다.",
        "DiceLock" when !enabled => "게임 기본처럼 필드 위에 두어\n줌에 따라 크기가 바뀝니다.",
        "CounterSound" when !enabled => "알림음을 내지 않습니다.",
        "CounterAnimation" when !enabled => "반격 아이콘을 멈춘 채 보여줍니다.",
        "PingFocus" when !enabled => "핑 선택 모드를 그대로 둡니다.",
        "OldReplays" when !enabled => "버전이 다른 리플레이는\n게임 기본처럼 재생하지 않습니다.",
        "FinalDamage" when !enabled => "피해 숫자를 보여주지 않습니다.",
        "SuppressNotices" when !enabled => "공지가 자동으로 열립니다.",
        "UpdateNotifications" when !enabled => "알림을 띄우지 않고\n모드 설정 버튼에만 표시합니다.",
        "InputAttention" => mode switch { "Off" => "알리지 않습니다.", "Windows" => "내 입력을 기다리면\nWindows 알림을 보냅니다.", _ => "내 입력을 기다리면\n작업 표시줄 아이콘을 깜빡입니다." },
        "AutoUpdate" => mode switch
        {
            "Off" => "새 버전을 자동으로 확인하거나 받지 않습니다.",
            "Check" => "새 버전을 주기적으로 확인합니다.\n받기와 적용은 자동으로 하지 않습니다.",
            "Download" => "새 버전을 확인하고 자동으로 받습니다.\n적용은 업데이트 버튼으로 합니다.",
            "Apply" => "새 버전을 확인하고 받아서 바로 적용합니다.\n다음 실행부터 새 버전을 사용합니다.",
            _ => "단계에 없는 조합으로 저장되어 있습니다.\n단계를 고르면 그 단계로 바뀝니다."
        },
        "UpdateChannel" => mode switch { "PreRelease" => "프리 릴리즈와\n더 새로운 정식 릴리즈를 받습니다.", "Beta" => "비공개 베타 버전을 받습니다.\n토큰 인증이 필요합니다.", _ => "정식 릴리즈만 받습니다." },
        "Enabled" when !enabled => "추천 아이콘을 표시하지 않습니다.",
        "Details" when !enabled => "마우스를 올려도\n수치를 보여주지 않습니다.",
        "EncounterAdvice" when !enabled => "조우 화면에서 추천하지 않습니다.",
        "DefenseAdvice" when !enabled => "방어·회피를 추천하지 않습니다.",
        "CardAdvice" when !enabled => "카드를 추천하지 않습니다.",
        "ShootingStarShow" when !enabled => "놓아주기 버튼에 표시하지 않습니다.",
        "KoCardPreview" when !enabled => "카드에 마우스를 올려도\n지금 조건을 그대로 보여줍니다.",
        "KoSpectator" when !enabled => "내 전투에서만 보여줍니다.",
        "StatusHand" when !enabled => "손패 장수를 보여주지 않습니다.",
        "StatusEffects" when !enabled => "효과 아이콘을 보여주지 않습니다.",
        "CounterIcons" when !enabled => "반격 아이콘을 보여주지 않습니다.",
        "ShushuMaster" when !enabled => "슈슈 쉴드 효과와 방어 잠금을\n모두 쓰지 않습니다.",
        "KoMinimum" when !enabled => "주사위 조건을 보여주지 않습니다.",
        "CardPopups" when !enabled => "클릭해도 열지 않습니다.",
        "BattleStatus" when !enabled => "추가 정보를 보여주지 않습니다.",
        "FieldBuffs" when !enabled => "인디케이터를 보여주지 않습니다.",
        "FieldZoom" when !enabled => "휠 줌과 자유 시점을 쓰지 않습니다.",
        "MatchFocus" when !enabled => "창을 가져오지 않습니다.",
        "Diagnostics" when !enabled => "기록하지 않습니다.",
        "SettingsAutoKeep" when !enabled => "설정을 자동으로 보관하지 않습니다.",
        "MuteUnfocused" when !enabled => "소리를 그대로 둡니다.",
        _ => Help(action).Body
    };

    internal static (string Title, string Body) Help(string action) => action switch
    {
        "Language" => ("언어", "모드 안내 언어를 선택합니다.\n자동은 게임 메뉴 언어를 따릅니다."),
        "Features" => ("모드 기능", "기능별 표시 설정을 변경합니다."),
        "General" => ("일반 설정", "언어·음소거·알림·진단을 설정합니다."),
        "Enabled" => ("행동 추천", "공격·놓아주기, 방어·회피와\n쓸 만한 카드를 추천합니다."),
        "Details" => ("추천 상세 수치", "예상 피해와\n전투불능 확률을 보여줍니다."),
        "KoMinimum" => ("KO 주사위 조건", "KO·생존이 확정되는 주사위 조건을\n보여줍니다."),
        "CardPopups" => ("핑 상세 보기", "카드·칩 핑 말풍선을 클릭하면\n원본 이미지와 설명이 열립니다."),
        "HandLayout" => ("손패 묶기", "손패를 기능별로 모아\n클릭이나 호버로 펼칩니다."),
        "BattleStatus" => ("전투 상태", "양쪽의 손패 장수·공개 효과와\n반격 가능 여부를 보여줍니다.\n아이콘에 마우스를 올리면 설명이 나옵니다."),
        "ShushuShield" => ("슈슈 쉴드 효과", "슈슈 쉴드가 있는 전투 캐릭터에\n쉴드 효과를 그립니다."),
        "ShushuShieldRestrictions" => ("슈슈 쉴드 방어 잠금", "내가 쉴드를 가진 방어자이면\n방어 버튼과 방어 카드를 잠급니다."),
        "AutoThanks" => ("감사합니다 자동 전송", "게임 기본 감사합니다 추천을\n추천마다 한 번 보냅니다."),
        "CharacterRoles" => ("역할 표시", "게임 내부 분류로\n캐릭터 역할을 보여줍니다."),
        "PlayerTab" => ("플레이어 목록 탭", "필드 플레이어 목록 바 아래에\n손패 장수·역할·스킬 쿨타임을 보여줍니다."),
        "PlayerTabHand" => ("손패 장수", "플레이어마다 공개 손패 장수를\n보여줍니다."),
        "PlayerTabRole" => ("역할", "게임 내부 분류로\n캐릭터 역할을 보여줍니다."),
        "PlayerTabSkill" => ("스킬 쿨타임", "액티브 스킬의 남은 턴/전체 턴을\n보여줍니다."),
        "CharacterGuide" => ("준비 화면 스킬 설명", "준비 화면 초상화에 마우스를 올리면\n스킬·재사용 대기시간·잠재력이 나옵니다."),
        "IndicatorFade" => ("줌 아웃 시 반투명", "줌 아웃할수록 플레이어·몬스터\n인디케이터를 반투명하게 합니다."),
        "IndicatorHide" => ("멀면 숨기기", "멀리 줌 아웃하면 숨기고\n다시 확대하면 보여줍니다."),
        "DiceLock" => ("주사위 화면 고정", "줌·자유 시점에서도 굴린 주사위를\n기본 시점과 같은 위치·크기로 보여줍니다."),
        "CounterSound" => ("소리", "반격 아이콘이 나타날 때\n알림음을 냅니다."),
        "CounterAnimation" => ("애니메이션", "인카운트·전투 반격 아이콘을\n움직여 보여줍니다."),
        "OldReplays" => ("구버전 리플레이 재생", "버전이 다른 리플레이도 재생합니다.\n지금 게임과 규칙이 달라 도중에\n멈추거나 화면이 틀릴 수 있습니다."),
        "PingFocus" => ("Alt+Tab 핑 모드 해제", "Alt+Tab 후 남은 핑 선택 모드를\n핑을 보내지 않고 끕니다."),
        "FinalDamage" => ("최종 피해", "공개된 최종 수치로\n계산한 피해를 보여줍니다."),
        "SuppressNotices" => ("시작 공지 끄기", "공지가 자동으로 열리지 않습니다."),
        "UpdateNotifications" => ("업데이트 알림 팝업", "새 버전이 나오면 홈 화면 위쪽에\n잠깐 알림을 띄웁니다.\n누르면 변경 내역이 열립니다."),
        "FieldBuffs" => ("플레이어 인디케이터", "플레이어 이름·HP와 공개 효과를 보여줍니다.\n효과 아이콘을 클릭하면 설명이 열립니다."),
        "FieldMarker" => ("인디케이터 높이", "점을 캐릭터 발밑(0%)부터\n원래 위치(100%) 사이로 옮깁니다."),
        "WalkDirection" => ("이동 방향 강조", "플레이어·몬스터 발밑의 이동 방향을\n가려지지 않게 그립니다."),
        "FieldZoom" => ("휠 줌·자유 시점", "휠로 확대·축소하고 드래그로 시점을 옮깁니다.\n턴 바의 '자유 시점 카메라 종료'로\n기본 시점에 돌아갑니다."),
        "MatchFocus" => ("매칭 시 게임 창 띄우기", "PvE 매칭 성사나 방 시작 때\n게임 창을 한 번 앞으로 가져옵니다."),
        "Names" => ("이름 표기", "본명 또는 이명으로 표시합니다."),
        "Diagnostics" => ("진단 로그", "공개 전투 계산까지 자세히 기록합니다.\n내 PC에만 저장됩니다."),
        "SettingsAutoKeep" => ("자동 보관", "설정을 바꿀 때마다 보관합니다.\n게임 쪽 설정 파일이 없을 때만 되살립니다."),
        "SettingsSave" => ("지금 보관", "지금 설정을 보관본으로 남깁니다.\n자동 보관은 이 보관본을 덮어쓰지 않습니다."),
        "SettingsLoad" => ("보관본 불러오기", "남겨 둔 보관본으로 지금 설정을 바꿉니다.\n한 번 더 눌러야 적용됩니다."),
        "SettingsReset" => ("모든 설정 초기화", "모든 설정을 처음 설치한 상태로 되돌립니다.\n한 번 더 눌러야 적용됩니다."),
        "SettingsFolder" => ("보관 폴더 열기", "보관 파일이 있는 폴더를 엽니다.\n다른 PC로 옮길 때 쓰세요."),
        "DiagnosticsOpen" => ("진단 폴더 열기", "진단 ZIP이 저장된 폴더를 엽니다."),
        "DiagnosticsCollect" => ("진단 ZIP 만들기", "진단 로그를 ZIP으로 모읍니다."),
        "MuteUnfocused" => ("백그라운드 음소거", "게임 소리를 끄고\n돌아오면 다시 켭니다."),
        "InputAttention" => ("입력 대기 알림", "게임 창이 비활성 상태일 때\n내 입력 대기를 알립니다."),
        "UpdateChannel" => ("업데이트 채널", "선택한 채널의 최신 버전을 확인합니다.\n설치된 채널과 다르면 채널 전환에서 승인합니다."),
        "UpdateSwitch" => ("채널 전환", "선택한 채널의 대상 버전을 확인하고\n설치된 채널을 전환합니다."),
        "Update" => ("업데이트", "지금 할 수 있는 업데이트 작업을 실행합니다."),
        "AutoUpdate" => ("자동 업데이트", "새 버전을 확인하고 받아서 바로 적용합니다.\n다음 실행부터 새 버전을 사용합니다."),
        "UpdateCheck" => ("업데이트 확인", "선택한 채널의 새 버전을 확인합니다."),
        "UpdateDownload" => ("다운로드 페이지", "선택한 채널의 GitHub 릴리스 목록을 엽니다."),
        "UpdateGet" => ("업데이트 받기", "선택한 채널의 검증된 업데이트를 받습니다.\n비공개 베타만 토큰 인증이 필요합니다."),
        "UpdateApply" => ("업데이트 적용", "받아 둔 업데이트를 적용합니다.\n다음 실행부터 새 버전을 사용합니다."),
        "UpdateCancel" => ("업데이트 취소", "다운로드 또는 적용 대기를 취소합니다."),
        "ZoomSensitivity" => ("줌 감도", "휠 한 칸에 확대·축소되는 양입니다."),
        "ZoomOutLimit" => ("최대 줌 아웃", "휠로 멀어질 수 있는 최대 거리입니다.\n100%는 필드 전체가 보이는 거리입니다."),
        "FreeCameraSpeed" => ("WASD 속도", "WASD로 자유 시점을 움직이는 속도입니다."),
        "IndicatorHideDistance" => ("숨김 거리", "인디케이터가 숨는 줌 아웃 거리입니다.\n값이 작을수록 더 일찍 숨습니다."),
        "IndicatorFadeStrength" => ("반투명 정도", "줌 아웃했을 때 인디케이터가 흐려지는 정도입니다.\n0%는 흐려지지 않습니다."),
        "FoldedHandOpacity" => ("접힌 카드 불투명도", "묶여 접힌 카드의 불투명도입니다.\n100%는 원래대로, 0%는 보이지 않습니다."),
        "Scale" => ("표시 크기", "75%부터 150%까지 조절합니다."),
        "Opacity" => ("배경 불투명도", "0%에서도 글자·아이콘·테두리는\n그대로 보입니다."),
        "Close" => ("돌아가기", "설정을 닫고 돌아갑니다.\n변경 사항은 자동 저장됩니다."),
        "EncounterAdvice" => ("인카운트 추천", "조우 화면에서 공격·놓아주기를\n추천합니다."),
        "DefenseAdvice" => ("방어·회피 추천", "방어를 추천하고, 방어해도 반드시\n쓰러질 때는 회피를 추천합니다."),
        "CardAdvice" => ("카드 추천", "쓸 만한 카드 위에\n효과를 표시합니다."),
        "ShootingStarShow" => ("슈팅 스타 표시", "놓아줄 때 몬스터에게 주는 피해와\n얻는 스타라이트를 보여줍니다."),
        "KoCardPreview" => ("카드 호버 조건", "카드에 마우스를 올리면\n카드 사용 후 조건으로 보여줍니다."),
        "KoSpectator" => ("관전 전투 표시", "다른 사람 전투에서도\n조건을 보여줍니다."),
        "StatusHand" => ("손패 장수", "양쪽의 공개 손패 장수를\n보여줍니다."),
        "StatusEffects" => ("공개 효과 아이콘", "양쪽의 공개 효과를 아이콘으로 보여줍니다.\n아이콘에 마우스를 올리면 설명이 나옵니다."),
        "CounterIcons" => ("반격 아이콘", "인카운트·전투에서 반격 가능 여부를\n아이콘으로 보여줍니다."),
        "ShushuMaster" => ("슈슈 쉴드", "슈슈 쉴드 효과와 방어 잠금을\n사용합니다."),
        "CategoryJump" => ("카테고리 이동", "스크롤바에 마우스를 올리면 카테고리 이름이 모두 보이고,\n점이나 이름을 누르면 그 카테고리로 이동합니다."),
        "Search" => ("설정 검색", "이름·설명·위치 태그에서 찾습니다.\n이름 밖에서 찾은 설정은\n이름 아래에 그 이유가 나옵니다."),
        "SearchClear" => ("검색 지우기", "검색어를 지우고\n전체 목록으로 돌아갑니다."),
        _ => ("기능 안내", "설정 버튼에 마우스를 올리면\n해당 기능을 안내합니다.")
    };

    internal static float FitScale(float width, float height, float windowHeight = Height) =>
        Math.Max(0.1f, Math.Min(1f, Math.Min((width - 48f) / Width, (height - 48f) / windowHeight)));
}
