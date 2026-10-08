namespace BetterAstralParty;

internal static class MenuLayout
{
    internal const float Width = 1060;
    internal const float Height = 620; // Common/Com_PopUpWindow_Bottom's actual native size.
    internal const float SettingsX = 430, SettingsY = 120, SettingsWidth = 530, SettingsHeight = 390;
    internal const float FooterY = 520;
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
        _ => Array.Empty<string>()
    };
    internal static string OptionLabel(string action, string value) => action switch
    {
        "Language" => value == "Auto" ? "자동" : value,
        "HandLayout" => value switch { "Click" => "클릭 펼치기", "Hover" => "호버 펼치기", _ => "기본" },
        "InputAttention" => InputAttentionState.Label(value),
        _ => value
    };
    internal static float DropdownHeight(string action) => Options(action).Length * 56f + 12;
    internal static float DropdownY(float top, float buttonHeight, float height, float bottom = FooterY) =>
        top + buttonHeight + height + 4 <= bottom ? top + buttonHeight + 4 : Math.Max(8, top - height - 4);
    internal static string OptionHelp(string action, string value) =>
        StateHelp(action, value is not ("Native" or "Off"), value);
    internal static bool GeneralControl(string action) => action is "Language" or "Diagnostics" or "DiagnosticsOpen" or "DiagnosticsCollect" or "MuteUnfocused" or "InputAttention"
        or "UpdateAuth" or "UpdateSignOut"
        or "UpdateChannel" or "UpdateCheck" or "UpdateDownload" or "AutoDownload" or "AfterExit" or "UpdateGet" or "UpdateCancel"
        or "ScaleMinus" or "ScalePlus" or "OpacityMinus" or "OpacityPlus";

    internal static string StateHelp(string action, bool enabled, string mode) => action switch
    {
        "Language" => mode switch { "한국어" => "모드 설명을 한국어로 표시합니다.\n게임 자체 언어는 변경하지 않습니다.", "English" => "모드 설명을 영어로 표시합니다.\n게임 자체 언어는 변경하지 않습니다.", _ => "자동 선택은 게임 메뉴 언어에 맞춰\n모드 설명 언어를 설정합니다.\n게임 자체 언어는 변경하지 않습니다." },
        "Names" => enabled ? "캐릭터 이름을 본명으로 표시합니다.\n계정 닉네임은 변경하지 않습니다." : "캐릭터 이름을 이명으로 표시합니다.\n계정 닉네임은 변경하지 않습니다.",
        "HandLayout" => !enabled ? "손패를 게임의 기본 배치로 표시합니다." : mode == "Click" ? "묶음을 클릭하면 가로로 펼칩니다.\n다른 곳을 클릭하면 접습니다.\n접힌 카드는 반투명하게 표시합니다." : "묶음에 마우스를 올리면 위로 펼칩니다.\n마우스가 벗어나면 접습니다.\n접힌 카드는 반투명하게 표시합니다.",
        "ShushuShield" => enabled ? "전투 중 활성 쉴드를 캐릭터에 표시합니다.\n쉴드가 있는 내가 방어자이면\n방어 버튼과 방어 카드를 잠급니다." : "모드의 전투 쉴드 표시와\n방어 버튼·카드 잠금을 적용하지 않습니다.",
        "InputAttention" => mode switch { "Off" => "게임 창이 비활성 상태여도\n입력 대기 알림을 보내지 않습니다.", "Windows" => "게임 창이 비활성 상태에서\n내 입력을 기다리면 Windows로 알립니다.\nWindows 알림 설정을 따릅니다.", _ => "게임 창이 비활성 상태에서\n내 입력을 기다리면 작업표시줄을 깜빡입니다." },
        "UpdateChannel" => mode == "Beta" ? "베타와 안정판의 새 버전을 확인합니다.\n클릭하면 안정판으로 전환합니다." : "안정판의 새 버전을 확인합니다.\n클릭하면 베타 포함으로 전환합니다.",
        "Enabled" when !enabled => "공격·놓아주기, 방어·회피 및\n카드 사용 추천을 표시하지 않습니다.",
        "Details" when !enabled => "추천에 마우스를 올려도\n피해·전투불능 확률을 표시하지 않습니다.",
        "KoMinimum" when !enabled => "KO·생존 최소 조건 인디케이터를\n표시하지 않습니다.",
        "CardPopups" when !enabled => "카드·칩 핑의 추가 상세 팝업을\n표시하지 않습니다.\n기본 핑 말풍선은 유지합니다.",
        "BattleStatus" when !enabled => "모드의 전투 정보와 반격 표시를\n표시하지 않습니다.\n슈슈 쉴드 설정은 별도로 적용합니다.",
        "FieldBuffs" when !enabled => "모드의 필드 이름·HP·버프 표시를\n표시하지 않습니다.",
        "Diagnostics" when !enabled => "선택형 상세 진단 기록을 끕니다.\n기본 오류 기록은 유지합니다.",
        "MuteUnfocused" when !enabled => "다른 창으로 전환해도\n모드가 게임 소리를 끄지 않습니다.",
        _ => Help(action).Body
    };

    internal static (string Title, string Body) Help(string action) => action switch
    {
        "Language" => ("언어", "자동은 게임 메뉴의 언어를 따릅니다.\n클릭: 자동 → 한국어 → English\n게임 자체 언어는 바꾸지 않습니다."),
        "Features" => ("모드 기능", "기능별 표시 설정을 변경합니다."),
        "General" => ("일반 설정", "언어·음소거·알림·진단을 설정합니다."),
        "Enabled" => ("전투 추천", "PvE에서 공격·놓아주기,\n방어·회피와 카드 사용을 추천합니다."),
        "Details" => ("호버 수치", "추천에 마우스를 올리면\n피해·전투불능 확률을 표시합니다."),
        "KoMinimum" => ("KO 최소 조건 표시", "공격·관전 시 KO 최소 조건,\n방어 시 생존 최소 조건을 표시합니다.\n가장 유리한 결과 기준이며\n성공을 보장하는 숫자는 아닙니다."),
        "CardPopups" => ("카드·칩 핑 팝업", "카드·칩 핑 말풍선을 클릭하면\n원본 이미지와 설명을 봅니다."),
        "HandLayout" => ("손패 기능별 정렬", "기본 → 클릭 펼치기 → 호버 펼치기\n클릭 방식은 묶음을 눌러 가로로 펼칩니다.\n접힌 묶음은 반투명하게 표시합니다."),
        "BattleStatus" => ("전투 정보 표시", "HP 아래 손패 장수·공개 효과와\n플레이어·몬스터 반격을 표시합니다.\n효과 설명은 아이콘 호버로 봅니다."),
        "ShushuShield" => ("슈슈 쉴드 표시", "전투 중 캐릭터에 쉴드 효과를 표시하고\n방어자일 때 방어 버튼·카드를 잠급니다.\nOFF: 표시와 잠금 모두 해제.\n전투 정보 표시와 별도 설정입니다."),
        "FieldBuffs" => ("필드 버프 표시", "필드 플레이어 이름·HP를 표시합니다.\n공개 버프가 있으면 아이콘도 표시하며\n클릭하면 효과 설명을 봅니다."),
        "Names" => ("캐릭터 이름", "ON: 본명 / OFF: 이명\n계정 닉네임은 바꾸지 않습니다."),
        "Diagnostics" => ("충돌 진단 로그", "오류·성능·공개 전투 계산을 기록합니다.\n계산 불일치는 별도 보존합니다.\n문제를 확인할 때만 켜 주세요."),
        "DiagnosticsOpen" => ("진단 폴더 열기", "만든 진단 ZIP이 모이는 폴더를 엽니다.\nZIP 안에서 처리된 로그를 볼 수 있습니다."),
        "DiagnosticsCollect" => ("진단 ZIP 만들기", "최근 로그의 허용 항목을 ZIP으로 요약합니다.\n카드·손패·채팅·전체 설정·덤프는 제외합니다.\n개인정보가 남을 수 있으니 내용을 확인한 뒤\n개발자에게 직접 첨부하세요."),
        "MuteUnfocused" => ("창 비활성화 시 음소거", "다른 창으로 전환하면 소리를 끕니다.\n게임으로 돌아오면 복원합니다.\n저장된 볼륨은 바꾸지 않습니다."),
        "InputAttention" => ("입력 대기 알림", "비활성 창에서 내 입력 대기를 알립니다.\n클릭: 작업표시줄 → Windows 알림 → OFF\nWindows 알림 설정에 따라 표시됩니다."),
        "UpdateChannel" => ("업데이트 채널", "클릭: 안정판 ↔ 베타 포함\n파일을 자동으로 교체하지 않습니다."),
        "UpdateCheck" => ("업데이트 확인", "새 버전과 변경 사항을 확인합니다.\n비공개 릴리스에는 인증이 필요합니다."),
        "UpdateDownload" => ("다운로드 페이지", "확인한 새 버전의 GitHub 페이지를 엽니다.\n다운로드와 설치는 직접 진행합니다."),
        "AutoDownload" => ("자동 다운로드", "기본 OFF. 검증할 새 버전을 자동으로 받습니다."),
        "AfterExit" => ("종료 후 적용", "기본 OFF. 게임과 런처 종료 후 검증한 업데이트를 적용합니다."),
        "UpdateGet" => ("업데이트 받기", "서명 키와 비공개 인증 설정이 필요합니다."),
        "UpdateCancel" => ("업데이트 취소", "다운로드 또는 종료 후 적용 대기를 취소합니다."),
        "ScaleMinus" or "ScalePlus" => ("표시 크기", "추천과 전투 상태 표시의\n크기를 조절합니다."),
        "OpacityMinus" or "OpacityPlus" => ("배경 불투명도", "전투 패널 바탕만 조절합니다.\n0%에서도 글자·아이콘·테두리는\n유지됩니다."),
        "Close" => ("돌아가기", "설정을 닫고 돌아갑니다.\n변경 사항은 자동 저장됩니다."),
        _ => ("기능 안내", "설정 버튼에 마우스를 올리면\n해당 기능을 안내합니다.")
    };

    internal static float FitScale(float width, float height) =>
        Math.Max(0.1f, Math.Min(1f, Math.Min((width - 48f) / Width, (height - 48f) / Height)));
}
