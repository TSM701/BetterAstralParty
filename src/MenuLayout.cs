namespace BetterAstralParty;

internal static class MenuLayout
{
    internal const float Width = 1060;
    internal const float Height = 620; // Common/Com_PopUpWindow_Bottom's actual native size.
    internal const float SettingsX = 430, SettingsY = 120, SettingsWidth = 530, SettingsHeight = 390;
    internal const float FooterY = 520;
    internal const float SectionHeight = 36;
    internal const float SectionLineHeight = 2;
    internal static readonly (string Title, bool General, string[] Actions)[] Sections =
    {
        ("전투 추천", false, new[] { "Enabled", "Details", "KoMinimum", "FinalDamage" }),
        ("전투 표시", false, new[] { "BattleStatus", "ShushuShield", "ShushuShieldRestrictions", "CounterAnimation", "CounterSound" }),
        ("필드·카메라", false, new[] { "FieldBuffs", "FieldZoom", "IndicatorFade", "IndicatorHide" }),
        ("카드", false, new[] { "HandLayout", "CardPopups" }),
        ("캐릭터", false, new[] { "Names", "CharacterRoles", "CharacterGuide" }),
        ("편의 기능", false, new[] { "AutoThanks", "PingFocus" }),
        ("화면·언어", true, new[] { "Language", "Scale", "Opacity" }),
        ("알림·소리", true, new[] { "MuteUnfocused", "InputAttention", "MatchFocus", "SuppressNotices" }),
        ("업데이트", true, new[] { "UpdateChannel", "UpdateSwitch", "AutomaticChecks", "UpdateNotifications", "UpdateCheck", "AutoDownload", "AfterExit", "UpdateGet", "UpdateCancel", "UpdateDownload" }),
        ("비공개 베타 인증", true, new[] { "UpdateAuth", "UpdateSignOut" }),
        ("진단", true, new[] { "Diagnostics", "DiagnosticsOpen", "DiagnosticsCollect" })
    };
    internal static float SectionY(int index)
    {
        var y = 5f;
        for (var i = 0; i < index; i++)
            if (Sections[i].General == Sections[index].General)
                y += SectionHeight + 65 * Sections[i].Actions.Length + 12;
        return y;
    }
    internal static (float X, float Width) SectionLine(float titleX, float titleWidth, float textWidth, float right)
    {
        if (!float.IsFinite(textWidth) || textWidth <= 0) return (right, 0);
        var x = Math.Min(right, titleX + Math.Min(titleWidth, textWidth) + 12f);
        return (x, Math.Max(0, right - x));
    }
    internal static float SettingY(string action)
    {
        var row = action switch
        {
            "ScaleMinus" or "ScalePlus" or "ScaleValue" => "Scale",
            "OpacityMinus" or "OpacityPlus" or "OpacityValue" => "Opacity",
            _ => action
        };
        for (var i = 0; i < Sections.Length; i++)
        {
            var position = Array.IndexOf(Sections[i].Actions, row);
            if (position >= 0) return SectionY(i) + SectionHeight + 65 * position;
        }
        throw new ArgumentException("Settings category missing: " + action);
    }
    internal static (float Height, float Y) ScrollMarker(float viewHeight, float contentHeight, float position)
    {
        if (contentHeight <= viewHeight) return (viewHeight, 0);
        var height = Math.Clamp(viewHeight * viewHeight / contentHeight, 24, viewHeight);
        return (height, Math.Clamp(position / (contentHeight - viewHeight), 0, 1) * (viewHeight - height));
    }
    internal static bool FixedControl(string action) => action is "Close" or "Features" or "General";
    internal static bool InSettings(float x, float y) => x >= 0 && y >= 0 && x < SettingsWidth && y < SettingsHeight;
    internal static float ContentHeight(float bottom) => Math.Max(SettingsHeight, bottom + 5);

    internal static float ToggleY(int row) => 125 + 65 * row;
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
    internal static float ToggleWidth(string action) => 480;
    internal static string[] Options(string action) => action switch
    {
        "Language" => new[] { "Auto", "한국어", "English" },
        "HandLayout" => new[] { "Native", "Click", "Hover" },
        "InputAttention" => new[] { "Taskbar", "Windows", "Off" },
        "UpdateChannel" => new[] { "Stable", "PreRelease", "Beta" },
        _ => Array.Empty<string>()
    };
    internal static string OptionLabel(string action, string value) => action switch
    {
        "Language" => value == "Auto" ? "자동" : value,
        "HandLayout" => value switch { "Click" => "클릭 펼치기", "Hover" => "호버 펼치기", _ => "기본" },
        "InputAttention" => InputAttentionState.Label(value),
        "UpdateChannel" => value switch { "PreRelease" => "프리 릴리즈", "Beta" => "비공개 베타", _ => "릴리즈" },
        _ => value
    };
    internal static float DropdownHeight(string action) => Options(action).Length * 56f + 12;
    internal static float DropdownY(float top, float buttonHeight, float height, float bottom = FooterY) =>
        top + buttonHeight + height + 4 <= bottom ? top + buttonHeight + 4 : Math.Max(8, top - height - 4);
    internal static string OptionHelp(string action, string value) =>
        StateHelp(action, value is not ("Native" or "Off"), value);
    internal static bool GeneralControl(string action) => action is "Language" or "Diagnostics" or "DiagnosticsOpen" or "DiagnosticsCollect" or "MuteUnfocused" or "InputAttention" or "MatchFocus"
        or "UpdateAuth" or "UpdateSignOut"
        or "UpdateChannel" or "UpdateSwitch" or "UpdateCheck" or "UpdateDownload" or "AutoDownload" or "AfterExit" or "UpdateGet" or "UpdateCancel"
        or "SuppressNotices" or "AutomaticChecks" or "UpdateNotifications"
        or "ScaleMinus" or "ScalePlus" or "OpacityMinus" or "OpacityPlus";

    internal static string StateHelp(string action, bool enabled, string mode) => action switch
    {
        "Language" => mode switch { "한국어" => "모드 안내를 한국어로 표시합니다.", "English" => "모드 안내를 영어로 표시합니다.", _ => "모드 안내 언어를 게임 메뉴에 맞춥니다." },
        "Names" => enabled ? "캐릭터 이름을 본명으로 표시합니다." : "캐릭터 이름을 이명으로 표시합니다.",
        "HandLayout" => !enabled ? "손패를 게임의 기본 배치로 표시합니다." : mode == "Click" ? "묶음을 클릭하면 가로로 펼칩니다.\n다른 곳을 클릭하면 접습니다." : "묶음에 마우스를 올리면 위로 펼칩니다.\n마우스가 벗어나면 접습니다.",
        "ShushuShield" when !enabled => "전투 중 쉴드 효과를 표시하지 않습니다.",
        "ShushuShieldRestrictions" when !enabled => "방어 버튼·카드에 잠금을 추가하지 않습니다.",
        "AutoThanks" when !enabled => "감사합니다 추천을 자동 전송하지 않습니다.",
        "CharacterRoles" when !enabled => "캐릭터 역할을 추가 표시하지 않습니다.",
        "CharacterGuide" when !enabled => "준비 초상화에 스킬·잠재력\n설명을 추가하지 않습니다.",
        "IndicatorFade" when !enabled => "줌 아웃해도 인디케이터를\n반투명하게 하지 않습니다.",
        "IndicatorHide" when !enabled => "줌 거리에 따라 인디케이터를 숨기지 않습니다.",
        "CounterSound" when !enabled => "반격 아이콘의 알림음을 재생하지 않습니다.",
        "CounterAnimation" when !enabled => "반격 아이콘을 애니메이션 없이 표시합니다.",
        "PingFocus" when !enabled => "Alt+Tab 후 핑 선택 모드를\n자동 해제하지 않습니다.",
        "FinalDamage" when !enabled => "최종 피해 숫자를 표시하지 않습니다.",
        "SuppressNotices" when !enabled => "로그인 공지의 자동 열기를 사용합니다.",
        "AutomaticChecks" when !enabled => "새 버전을 주기적으로 확인하지 않습니다.",
        "UpdateNotifications" when !enabled => "새 버전 알림 팝업을 표시하지 않습니다.",
        "AutoDownload" when !enabled => "새 버전을 자동으로 다운로드하지 않습니다.",
        "AfterExit" when !enabled => "검증한 업데이트를 자동으로 적용하지 않습니다.",
        "InputAttention" => mode switch { "Off" => "게임 창이 비활성 상태일 때\n입력 대기 알림을 보내지 않습니다.", "Windows" => "게임 창이 비활성 상태에서\n내 입력을 기다리면 Windows로 알립니다.", _ => "게임 창이 비활성 상태에서\n내 입력을 기다리면 작업표시줄을 깜빡입니다." },
        "UpdateChannel" => mode switch { "PreRelease" => "공개 프리 릴리즈의 최신 버전을 확인합니다.", "Beta" => "비공개 베타의 최신 버전을 확인합니다.\n토큰 인증이 필요합니다.", _ => "공개 릴리즈의 최신 버전을 확인합니다." },
        "Enabled" when !enabled => "공격·놓아주기, 방어·회피 및\n카드 사용 추천을 표시하지 않습니다.",
        "Details" when !enabled => "추천에 마우스를 올려도\n피해·전투불능 확률을 표시하지 않습니다.",
        "KoMinimum" when !enabled => "KO·생존 최소 조건 인디케이터를\n표시하지 않습니다.",
        "CardPopups" when !enabled => "카드·칩 핑을 클릭해도\n추가 설명 팝업을 열지 않습니다.",
        "BattleStatus" when !enabled => "전투 손패 장수·효과·반격을\n추가 표시하지 않습니다.",
        "FieldBuffs" when !enabled => "플레이어 필드 인디케이터를\n표시하지 않습니다.",
        "FieldZoom" when !enabled => "필드 휠 줌과 자유 시점 이동을 사용하지 않습니다.",
        "MatchFocus" when !enabled => "PvE 매칭 성사·로비 방 시작에도\n게임 창으로 전환하지 않습니다.",
        "Diagnostics" when !enabled => "오류·성능·공개 전투의\n상세 진단 로그를 기록하지 않습니다.",
        "MuteUnfocused" when !enabled => "다른 창으로 전환해도\n모드가 게임 소리를 끄지 않습니다.",
        _ => Help(action).Body
    };

    internal static (string Title, string Body) Help(string action) => action switch
    {
        "Language" => ("언어", "모드 안내 언어를 선택합니다.\n자동은 게임 메뉴 언어를 따릅니다."),
        "Features" => ("모드 기능", "기능별 표시 설정을 변경합니다."),
        "General" => ("일반 설정", "언어·음소거·알림·진단을 설정합니다."),
        "Enabled" => ("전투 추천", "PvE에서 공격·놓아주기,\n방어·회피와 카드 사용을 추천합니다."),
        "Details" => ("호버 수치", "추천에 마우스를 올리면\n피해·전투불능 확률을 표시합니다."),
        "KoMinimum" => ("KO 최소 조건 표시", "가장 유리한 경우의 KO·생존에\n필요한 최소 주사위를 표시합니다."),
        "CardPopups" => ("카드·칩 핑 팝업", "카드·칩 핑 말풍선을 클릭하면\n원본 이미지와 설명을 봅니다."),
        "HandLayout" => ("손패 기능별 정렬", "손패를 기능별로 모아\n클릭이나 호버로 펼칩니다."),
        "BattleStatus" => ("전투 정보 표시", "HP 아래 손패 장수·공개 효과와\n플레이어·몬스터 반격을 표시합니다.\n효과 설명은 아이콘 호버로 봅니다."),
        "ShushuShield" => ("슈슈 쉴드 표시", "전투 중 캐릭터에 쉴드 효과를 표시합니다."),
        "ShushuShieldRestrictions" => ("슈슈 쉴드 방어 제한", "쉴드가 있는 내가 방어자이면\n방어 버튼·방어 카드를 제한합니다."),
        "AutoThanks" => ("자동 감사합니다", "게임 기본 감사합니다 추천을\n추천 한 건당 한 번 자동 전송합니다."),
        "CharacterRoles" => ("캐릭터 역할 표시", "게임 내부 분류로 캐릭터 역할을 표시합니다."),
        "CharacterGuide" => ("캐릭터 준비 설명", "준비 초상화에 마우스를 올리면\n스킬·잠재력 설명을 표시합니다."),
        "IndicatorFade" => ("줌 아웃 인디케이터 반투명", "줌 아웃 시 플레이어·몬스터\n인디케이터를 반투명하게 합니다."),
        "IndicatorHide" => ("원거리 인디케이터 숨김", "멀리 줌 아웃하면 인디케이터를 숨기고\n가까이 확대하면 다시 표시합니다."),
        "CounterSound" => ("반격 표시 소리", "인카운트·전투 반격 아이콘에\n알림음을 재생합니다."),
        "CounterAnimation" => ("반격 표시 애니메이션", "인카운트·전투 반격 아이콘에\n애니메이션을 재생합니다."),
        "PingFocus" => ("창 전환 후 핑 모드 해제", "Alt+Tab 후 남은 핑 선택 모드를\n핑을 보내지 않고 해제합니다."),
        "FinalDamage" => ("최종 피해 표시", "전투의 최종 수치로 계산한\n피해 숫자를 표시합니다."),
        "SuppressNotices" => ("시작 공지 자동 열기 방지", "로그인 공지가 자동으로 열리지 않게 합니다."),
        "AutomaticChecks" => ("자동 업데이트 확인", "주기적으로 새 버전을 확인합니다."),
        "UpdateNotifications" => ("업데이트 알림 팝업", "새 버전 알림과 변경 내역을 표시합니다."),
        "FieldBuffs" => ("플레이어 필드 인디케이터", "필드의 플레이어 이름·HP와\n공개 효과 아이콘을 표시합니다.\n클릭하면 효과 설명을 봅니다."),
        "FieldZoom" => ("필드 휠 줌", "휠로 확대·축소하고 자유 시점으로 이동합니다.\n‘자유 시점 카메라 종료’로 기본 시점에 돌아갑니다."),
        "MatchFocus" => ("매칭 성사 시 창 포커스", "PvE 매칭 성사·로비 방 시작 시\n게임 창을 앞으로 가져옵니다."),
        "Names" => ("캐릭터 이름", "본명 또는 이명으로 표시합니다."),
        "Diagnostics" => ("충돌 진단 로그", "오류·성능·공개 전투 계산의\n상세 로그를 기록합니다."),
        "DiagnosticsOpen" => ("진단 폴더 열기", "진단 ZIP이 저장된 폴더를 엽니다."),
        "DiagnosticsCollect" => ("진단 ZIP 만들기", "진단 로그를 ZIP으로 모읍니다.\n공유 전 개인정보를 확인하세요."),
        "MuteUnfocused" => ("창 비활성화 시 음소거", "다른 창으로 전환하면 소리를 끄고\n게임으로 돌아오면 복원합니다."),
        "InputAttention" => ("입력 대기 알림", "게임 창이 비활성 상태일 때\n내 입력 대기를 알립니다."),
        "UpdateChannel" => ("업데이트 채널", "선택한 채널의 최신 버전을 확인합니다.\n설치된 채널과 다르면 채널 전환에서 승인합니다."),
        "UpdateSwitch" => ("채널 전환", "선택한 채널의 대상 버전을 확인하고\n설치된 채널을 전환합니다."),
        "UpdateCheck" => ("업데이트 확인", "선택한 채널의 새 버전을 확인합니다."),
        "UpdateDownload" => ("다운로드 페이지", "선택한 채널의 GitHub 릴리스 목록을 엽니다."),
        "AutoDownload" => ("자동 다운로드", "검증된 새 버전을 자동으로 받습니다."),
        "AfterExit" => ("자동 적용", "검증한 업데이트를 바로 적용하고\n다음 실행부터 새 버전을 사용합니다."),
        "UpdateGet" => ("업데이트 받기", "선택한 채널의 검증된 업데이트를 받습니다.\n비공개 베타만 토큰 인증이 필요합니다."),
        "UpdateCancel" => ("업데이트 취소", "다운로드 또는 적용 대기를 취소합니다."),
        "ScaleMinus" or "ScalePlus" => ("표시 크기", "추천과 전투 상태 표시의\n크기를 조절합니다."),
        "OpacityMinus" or "OpacityPlus" => ("배경 불투명도", "전투 패널 바탕만 조절합니다.\n0%에서도 글자·아이콘·테두리는\n유지됩니다."),
        "Close" => ("돌아가기", "설정을 닫고 돌아갑니다.\n변경 사항은 자동 저장됩니다."),
        _ => ("기능 안내", "설정 버튼에 마우스를 올리면\n해당 기능을 안내합니다.")
    };

    internal static float FitScale(float width, float height) =>
        Math.Max(0.1f, Math.Min(1f, Math.Min((width - 48f) / Width, (height - 48f) / Height)));
}
