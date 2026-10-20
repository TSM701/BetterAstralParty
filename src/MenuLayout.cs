namespace BetterAstralParty;

internal static class MenuLayout
{
    internal const float Width = 1060;
    internal const float Height = 620; // Common/Com_PopUpWindow_Bottom's actual native size.
    internal const float SettingsX = 430, SettingsY = 120, SettingsWidth = 530, SettingsHeight = 390;
    internal const float FooterY = 520;
    internal const float SectionHeight = 36;
    // Category band spanning the switch column.
    internal const float SectionBandX = 10, SectionBandY = 3, SectionBandWidth = 480, SectionBandHeight = 30;
    internal const int SectionTitleSize = 19;
    internal const float HelpTagMaxWidth = 324, HelpTagHeight = 26;
    internal static readonly (string Title, bool General, string[] Actions)[] Sections =
    {
        ("전투 추천", false, new[] { "Enabled", "Details", "KoMinimum", "FinalDamage" }),
        ("전투 표시", false, new[] { "BattleStatus", "ShushuShield", "ShushuShieldRestrictions", "CounterAnimation", "CounterSound" }),
        ("필드·카메라", false, new[] { "FieldBuffs", "FieldMarker", "FieldZoom", "ZoomSensitivity", "IndicatorFade", "IndicatorFadeStrength", "IndicatorHide", "IndicatorHideDistance", "DiceLock" }),
        ("손패·카드", false, new[] { "HandLayout", "FoldedHandOpacity", "CardPopups" }),
        ("캐릭터", false, new[] { "Names", "CharacterRoles", "CharacterGuide" }),
        ("편의 기능", false, new[] { "AutoThanks", "PingFocus" }),
        ("화면·언어", true, new[] { "Language", "Scale", "Opacity" }),
        ("알림·소리", true, new[] { "MuteUnfocused", "InputAttention", "MatchFocus", "SuppressNotices" }),
        ("진단", true, new[] { "Diagnostics", "DiagnosticsOpen", "DiagnosticsCollect" }),
        ("설정 보관", true, new[] { "SettingsAutoKeep", "SettingsSave", "SettingsLoad", "SettingsFolder", "SettingsReset" }),
        // Last on its tab: the Beta sign-in rows under the channel show only for the Beta channel, leaving no gap.
        ("업데이트", true, new[] { "Update", "AutoUpdate", "UpdateNotifications", "UpdateDownload", "UpdateChannel", "UpdateAuth", "UpdateSignOut" })
    };
    internal static float SectionY(int index)
    {
        var y = 5f;
        for (var i = 0; i < index; i++)
            if (Sections[i].General == Sections[index].General)
                y += SectionHeight + Sections[i].Actions.Sum(RowHeight) + 12;
        return y;
    }
    internal static float SettingY(string action)
    {
        var row = action switch
        {
            _ when action.EndsWith("Value", StringComparison.Ordinal) && IsSlider(action[..^5]) => action[..^5],
            _ => action
        };
        for (var i = 0; i < Sections.Length; i++)
        {
            var position = Array.IndexOf(Sections[i].Actions, row);
            if (position >= 0) return SectionY(i) + SectionHeight + Sections[i].Actions.Take(position).Sum(RowHeight);
        }
        throw new ArgumentException("Settings category missing: " + action);
    }
    internal static (float Height, float Y) ScrollMarker(float viewHeight, float contentHeight, float position)
    {
        if (contentHeight <= viewHeight) return (viewHeight, 0);
        var height = Math.Clamp(viewHeight * viewHeight / contentHeight, 24, viewHeight);
        return (height, Math.Clamp(position / (contentHeight - viewHeight), 0, 1) * (viewHeight - height));
    }
    // The marker sits at SettingsX + 505 (12 wide) and itself takes the press, so the band matches it exactly.
    internal static bool OnScrollTrack(float x, float y, float viewHeight) =>
        x >= SettingsX + 505 && x <= SettingsX + 517 && y >= SettingsY && y <= SettingsY + viewHeight;
    internal static float ScrollFromMarker(float thumbTop, float viewHeight, float contentHeight, float thumbHeight) =>
        contentHeight <= viewHeight ? 0 : Math.Clamp((thumbTop - SettingsY) / Math.Max(1f, viewHeight - thumbHeight), 0f, 1f) * (contentHeight - viewHeight);
    internal static bool FixedControl(string action) => action is "Close" or "Features" or "General";
    internal static bool InSettings(float x, float y) => x >= 0 && y >= 0 && x < SettingsWidth && y < SettingsHeight;
    internal static float ContentHeight(float bottom) => Math.Max(SettingsHeight, bottom + 5);

    internal static float ToggleY(int row) => 125 + 65 * row;
    // A slider tuning a child switch hangs right under it inside one capsule, so its row is shorter.
    internal static float RowHeight(string action) => SubParent(action) != null ? 52 : 65;
    internal const float ToggleHeight = 60;
    // Return buttons keep their native aspect ratio; they are taller than switch toggles.
    internal static float NextTabY(float top, float renderedHeight) => top + renderedHeight + 16f;
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
    internal static float ToggleWidth(string action) => Parent(action) == null ? 480 : 480 - Indent(action) - ChildRightInset;
    // Options that only act while another option is ON sit in a tray under it, joined by a guide line, and dim while it is OFF.
    internal static string? Parent(string action) => action switch
    {
        "Details" => "Enabled",
        "FieldMarker" => "FieldBuffs",
        "ZoomSensitivity" or "IndicatorFade" or "IndicatorFadeStrength" or "IndicatorHide" or "IndicatorHideDistance" or "DiceLock" => "FieldZoom",
        "FoldedHandOpacity" => "HandLayout",
        "UpdateAuth" or "UpdateSignOut" => "UpdateChannel",
        _ => null
    };
    internal static float Indent(string action) => Parent(action) == null ? 0 : 16; // Children sit centred in the tray.
    // A slider tuning a child switch sits in a smaller, darker tray under that switch inside the parent tray.
    internal static string? SubParent(string action) => action switch
    {
        "IndicatorFadeStrength" => "IndicatorFade",
        "IndicatorHideDistance" => "IndicatorHide",
        _ => null
    };
    internal const float ChildRightInset = 16;
    // Child switches are shorter so a tray row still fits the uniform 65-unit pitch.
    internal static float ToggleHeightFor(string action) => Parent(action) == null ? ToggleHeight : 54;
    internal static float ChildOffsetY(string action) => Parent(action) == null ? 0 : 1; // Equal 6-unit gaps in the tray.
    // Pale tray tucked behind the parent switch (its top is hidden), rounded only at the bottom.
    // As wide as the parent switch; the top reaches its middle so the square corners stay hidden behind its round ends.
    internal const float TrayX = 10f, TrayWidth = 480f, TrayTop = -35f;
    // From behind the parent's middle to 6 units below the last row's content (a capsule ends 46 below its slider row).
    internal static float TrayHeight(string first, string last) =>
        SettingY(last) - SettingY(first) - TrayTop + (SubParent(last) != null ? 46 : 55) + 6;
    // Short "where / what" tag shown above the hover help body.
    internal static string? Subtitle(string action) => action switch
    {
        "Enabled" => "전투·인카운트 선택 버튼",
        "Details" => "추천 아이콘에 마우스",
        "KoMinimum" => "전투 화면 가운데",
        "FinalDamage" => "전투 결과의 HP 옆",
        "BattleStatus" => "전투 HP 아래",
        "ShushuShield" => "전투 캐릭터",
        "ShushuShieldRestrictions" => "내 방어·회피 선택",
        "CounterAnimation" => "인카운트·전투 반격 아이콘",
        "CounterSound" => "인카운트·전투 반격 아이콘",
        "FieldBuffs" => "필드 캐릭터 위",
        "FieldMarker" => "필드 캐릭터 위",
        "FieldZoom" => "필드",
        "IndicatorFade" => "필드 인디케이터",
        "IndicatorHide" => "필드 인디케이터",
        "DiceLock" => "필드 주사위",
        "HandLayout" => "내 필드 손패",
        "CardPopups" => "카드·칩 핑 말풍선",
        "Names" => "캐릭터 이름이 나오는 곳",
        "CharacterRoles" => "준비 화면 초상화",
        "CharacterGuide" => "준비 화면 초상화에 마우스",
        "AutoThanks" => "PvE 퀵 채팅",
        "PingFocus" => "핑 선택 모드",
        "Language" => "모드 글자 전체",
        "Scale" => "모드 전투 패널",
        "IndicatorFadeStrength" or "IndicatorHideDistance" => "필드 인디케이터",
        "ZoomSensitivity" => "필드",
        "FoldedHandOpacity" => "내 필드 손패",
        "Opacity" => "모드 전투 패널",
        "MuteUnfocused" => "다른 창을 쓰는 동안",
        "InputAttention" => "게임 창이 뒤에 있을 때",
        "MatchFocus" => "PvE 매칭·방 시작",
        "SuppressNotices" => "로그인 직후",
        "UpdateChannel" => "모드 업데이트",
        "Update" or "AutoUpdate" => "모드 업데이트",
        "SettingsAutoKeep" => "내 PC",
        "UpdateNotifications" => "모드 업데이트",
        "Diagnostics" => "내 PC",
        _ => null
    };
    // Native Setting_Com_Slider is 1010x68 (bar 997x54, grip 100x100 at y -16); placed in settings-content units.
    // Each slider row: short label, slider, value. Rows under a parent sit in its tray; the others get a card.
    // Offsets centre the bar and label on the row; every slider row sits on a switch-shaped pill.
    // Vertical centre of each slider row's pill; bar and label are centred on it.
    internal static float SliderCenterY(string action) => SubParent(action) != null ? 18 : Parent(action) != null ? 28 : 30;
    internal static float SliderOffsetY(string action) => SliderCenterY(action) - 8;
    internal static float SliderLabelOffsetY(string action) => SliderCenterY(action) - 17.5f;
    // The pill under a slider row: top-level rows span the column, child rows match child switches, and a slider
    // tuning a child switch is a smaller pill inside that switch's capsule. All are centred on the column.
    internal static (float X, float Width) SliderPill(string action) =>
        SubParent(action) != null ? (40, 420) : Parent(action) != null ? (26, 448) : (10, 480);
    // Label at the pill's left, value at its right, and the native bar (1010 wide) scaled to fill between them.
    internal static float SliderLabelX(string action) => SliderPill(action).X + 22;
    internal static float SliderBarX(string action) => SliderPill(action).X + 124;
    internal static float SliderValueX(string action) => SliderPill(action).X + SliderPill(action).Width - 80;
    // The grip overhangs the bar end, so leave room before the value.
    internal static float SliderScale(string action) => (SliderPill(action).Width - 230) / 1010f;
    internal static readonly string[] Sliders = { "FieldMarker", "ZoomSensitivity", "IndicatorFadeStrength", "IndicatorHideDistance", "FoldedHandOpacity", "Scale", "Opacity" };
    internal static bool IsSlider(string action) => Array.IndexOf(Sliders, action) >= 0;
    internal static (int Min, int Max) SliderRange(string action) => action switch
    {
        "Scale" => (75, 150), "ZoomSensitivity" => (50, 200), "IndicatorHideDistance" => (30, 90), _ => (0, 100)
    };
    internal static string SliderLabel(string action) => action switch
    {
        "FieldMarker" => "위치 조정", "IndicatorFadeStrength" => "반투명 정도", "FoldedHandOpacity" => "불투명도",
        "ZoomSensitivity" => "줌 감도", "IndicatorHideDistance" => "숨김 거리",
        "Scale" => "표시 크기", _ => "배경 불투명도"
    };
    // Culture P0 can insert a space ("50 %") that wraps the sign; always "50%".
    internal static string Percent(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    internal static string[] Options(string action) => action switch
    {
        "Language" => new[] { "Auto", "한국어", "English" },
        "HandLayout" => new[] { "Native", "Click", "Hover" },
        "InputAttention" => new[] { "Taskbar", "Windows", "Off" },
        "UpdateChannel" => new[] { "Stable", "PreRelease", "Beta" },
        "AutoUpdate" => new[] { "Off", "Check", "Download", "Apply" },
        _ => Array.Empty<string>()
    };
    internal static string OptionLabel(string action, string value) => action switch
    {
        "Language" => value == "Auto" ? "자동" : value,
        "HandLayout" => value switch { "Click" => "클릭 펼치기", "Hover" => "호버 펼치기", _ => "기본" },
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
    internal static float DropdownHeight(string action) => Options(action).Length * 56f + 12;
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
        "ShushuShield" when !enabled => "쉴드 효과를 그리지 않습니다.",
        "ShushuShieldRestrictions" when !enabled => "방어 버튼과 카드를 잠그지 않습니다.",
        "AutoThanks" when !enabled => "자동으로 보내지 않습니다.",
        "CharacterRoles" when !enabled => "역할을 보여주지 않습니다.",
        "CharacterGuide" when !enabled => "설명을 보여주지 않습니다.",
        "IndicatorFade" when !enabled => "줌 아웃해도 그대로 보여줍니다.",
        "IndicatorHide" when !enabled => "거리와 상관없이 보여줍니다.",
        "DiceLock" when !enabled => "게임 기본처럼 필드 위에 두어\n줌에 따라 크기가 바뀝니다.",
        "CounterSound" when !enabled => "알림음을 내지 않습니다.",
        "CounterAnimation" when !enabled => "반격 아이콘을 멈춘 채 보여줍니다.",
        "PingFocus" when !enabled => "핑 선택 모드를 그대로 둡니다.",
        "FinalDamage" when !enabled => "피해 숫자를 보여주지 않습니다.",
        "SuppressNotices" when !enabled => "공지가 자동으로 열립니다.",
        "UpdateNotifications" when !enabled => "팝업을 띄우지 않습니다.",
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
        "KoMinimum" => ("KO 주사위 조건", "KO·생존에 필요한 최소 주사위를\n가장 유리한 경우 기준으로 보여줍니다."),
        "CardPopups" => ("핑 상세 보기", "카드·칩 핑 말풍선을 클릭하면\n원본 이미지와 설명이 열립니다."),
        "HandLayout" => ("손패 묶기", "손패를 기능별로 모아\n클릭이나 호버로 펼칩니다."),
        "BattleStatus" => ("전투 상태", "양쪽의 손패 장수·공개 효과와\n반격 가능 여부를 보여줍니다.\n아이콘에 마우스를 올리면 설명이 나옵니다."),
        "ShushuShield" => ("슈슈 쉴드 효과", "슈슈 쉴드가 있는 전투 캐릭터에\n쉴드 효과를 그립니다."),
        "ShushuShieldRestrictions" => ("슈슈 쉴드 방어 잠금", "내가 쉴드를 가진 방어자이면\n방어 버튼과 방어 카드를 잠급니다."),
        "AutoThanks" => ("감사합니다 자동 전송", "게임 기본 감사합니다 추천을\n추천마다 한 번 보냅니다."),
        "CharacterRoles" => ("역할 표시", "게임 내부 분류로\n캐릭터 역할을 보여줍니다."),
        "CharacterGuide" => ("준비 화면 스킬 설명", "준비 화면 초상화에 마우스를 올리면\n스킬·재사용 대기시간·잠재력이 나옵니다."),
        "IndicatorFade" => ("줌 아웃 시 반투명", "줌 아웃할수록 플레이어·몬스터\n인디케이터를 반투명하게 합니다."),
        "IndicatorHide" => ("멀면 숨기기", "멀리 줌 아웃하면 숨기고\n다시 확대하면 보여줍니다."),
        "DiceLock" => ("주사위 화면 고정", "줌·자유 시점에서도 굴린 주사위를\n기본 시점과 같은 위치·크기로 보여줍니다."),
        "CounterSound" => ("반격 아이콘 소리", "반격 아이콘이 나타날 때\n알림음을 냅니다."),
        "CounterAnimation" => ("반격 아이콘 애니메이션", "인카운트·전투 반격 아이콘을\n움직여 보여줍니다."),
        "PingFocus" => ("Alt+Tab 핑 모드 해제", "Alt+Tab 후 남은 핑 선택 모드를\n핑을 보내지 않고 끕니다."),
        "FinalDamage" => ("최종 피해", "공개된 최종 수치로\n계산한 피해를 보여줍니다."),
        "SuppressNotices" => ("시작 공지 끄기", "공지가 자동으로 열리지 않습니다."),
        "UpdateNotifications" => ("업데이트 알림 팝업", "새 버전과 변경 내역을\n팝업으로 알려줍니다."),
        "FieldBuffs" => ("플레이어 인디케이터", "플레이어 이름·HP와 공개 효과를 보여줍니다.\n효과 아이콘을 클릭하면 설명이 열립니다."),
        "FieldMarker" => ("인디케이터 높이", "점을 캐릭터 발밑(0%)부터\n원래 위치(100%) 사이로 옮깁니다."),
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
        "DiagnosticsCollect" => ("진단 ZIP 만들기", "진단 로그를 ZIP으로 모읍니다.\n공유 전 개인정보를 확인하세요."),
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
        "ZoomSensitivity" => ("줌 감도", "휠 한 칸에 확대·축소되는 양입니다.\n100%가 기본입니다."),
        "IndicatorHideDistance" => ("숨김 거리", "인디케이터가 숨는 줌 아웃 거리입니다.\n값이 작을수록 더 일찍 숨습니다."),
        "IndicatorFadeStrength" => ("반투명 정도", "줌 아웃했을 때 인디케이터가 흐려지는 정도입니다.\n0%는 흐려지지 않고 100%가 기본입니다."),
        "FoldedHandOpacity" => ("접힌 카드 불투명도", "묶여 접힌 카드의 불투명도입니다.\n100%는 원래대로, 0%는 보이지 않습니다."),
        "Scale" => ("표시 크기", "75%부터 150%까지 조절합니다."),
        "Opacity" => ("배경 불투명도", "0%에서도 글자·아이콘·테두리는\n그대로 보입니다."),
        "Close" => ("돌아가기", "설정을 닫고 돌아갑니다.\n변경 사항은 자동 저장됩니다."),
        _ => ("기능 안내", "설정 버튼에 마우스를 올리면\n해당 기능을 안내합니다.")
    };

    internal static float FitScale(float width, float height) =>
        Math.Max(0.1f, Math.Min(1f, Math.Min((width - 48f) / Width, (height - 48f) / Height)));
}
