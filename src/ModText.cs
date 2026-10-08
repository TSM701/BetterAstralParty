using System.Text.RegularExpressions;

namespace BetterAstralParty;

// Only mod-owned presentation strings. Never translate player input or native descriptions.
internal static class ModText
{
    internal static string SessionTitle(string action) => Korean ? (action == "UpdateAuth" ? "업데이트 인증" : "업데이트 로그아웃") : (action == "UpdateAuth" ? "Connect Updates" : "Sign Out of Updates");
    internal static string SessionDetails(ReleaseSessionStatus status) => (Korean ? "비공개 업데이트 인증: " : "Private update session: ") + (Korean ? status switch {
        ReleaseSessionStatus.NotConfigured => "보안 승인과 공개 검증 키 설정이 필요합니다.",
        ReleaseSessionStatus.SignedOut => "로그아웃됨", ReleaseSessionStatus.AwaitingInput => "Windows 입력 창에서 PAT를 입력하세요.",
        ReleaseSessionStatus.Connected => "저장된 토큰 연결됨. 서버 접근 결과는 업데이트 확인에서 표시합니다.",
        ReleaseSessionStatus.Restoring => "저장된 인증을 불러오는 중입니다.",
        ReleaseSessionStatus.StorageFailed => "Windows 인증 저장소 작업에 실패했습니다. 인증 또는 로그아웃을 다시 시도하세요.",
        ReleaseSessionStatus.InvalidInput => "입력 형식을 확인하세요.", ReleaseSessionStatus.Cancelled => "입력 취소됨",
        ReleaseSessionStatus.Expired => "세션 만료됨", ReleaseSessionStatus.AuthenticationRequired => "다시 인증하세요.",
        ReleaseSessionStatus.AccessUnavailable => "저장소에 접근할 수 없습니다.", _ => "입력을 완료하지 못했습니다."
    } : status switch {
        ReleaseSessionStatus.NotConfigured => "Security approval and public verification pins are required.",
        ReleaseSessionStatus.SignedOut => "Signed out", ReleaseSessionStatus.AwaitingInput => "Enter the PAT in the Windows prompt.",
        ReleaseSessionStatus.Connected => "Saved token connected. Update Check shows the server access result.",
        ReleaseSessionStatus.Restoring => "Restoring saved authentication.",
        ReleaseSessionStatus.StorageFailed => "Windows credential storage failed. Retry Connect Updates or Sign Out.",
        ReleaseSessionStatus.InvalidInput => "Check the input format.", ReleaseSessionStatus.Cancelled => "Input cancelled",
        ReleaseSessionStatus.Expired => "Session expired", ReleaseSessionStatus.AuthenticationRequired => "Sign in again.",
        ReleaseSessionStatus.AccessUnavailable => "Repository access unavailable", _ => "Input could not be completed."
    }) + (Korean ? "\n현재 Windows 사용자의 자격 증명 관리자에 저장하며 게임 재실행 후에도 유지됩니다. 로그아웃하면 삭제합니다. 토큰이 만료·철회되면 재인증이 필요합니다." : "\nSaved in Windows Credential Manager for this user and restored after game restart. Sign Out deletes it. An expired or revoked token requires sign-in again.");
    internal static bool Korean { get; private set; } = true;
    internal static int Revision { get; private set; }
    private static bool _automaticKorean;
    internal static string Mode { get; private set; } = "Auto";
    internal static string Normalize(string? mode) => mode switch
    {
        "한국어" or "Korean" or "ko" => "한국어",
        "English" or "en" => "English",
        _ => "Auto"
    };
    internal static void Select(string? mode, string? nativeMenuTitle = null)
    {
        Mode = Normalize(mode);
        if (!string.IsNullOrWhiteSpace(nativeMenuTitle))
            _automaticKorean = nativeMenuTitle.Any(c => c is >= '\uAC00' and <= '\uD7A3');
        var korean = Mode == "한국어" || Mode == "Auto" && _automaticKorean;
        if (Korean == korean) return;
        Korean = korean;
        Revision++;
    }
    internal static string NextMode => Mode switch { "Auto" => "한국어", "한국어" => "English", _ => "Auto" };
    internal static string LanguageTitle => Text("언어") + ": " + (Mode == "Auto"
        ? Text("자동") + (Korean ? " (한국어)" : " (EN)") : Mode);
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["새 업데이트"] = "New Update",
        ["업데이트 설정"] = "Update Settings",
        ["닫기"] = "Close",
        ["현재 업데이트 상태"] = "Current Update Status",
        ["업데이트 채널"] = "Update Channel",
        ["자동 다운로드"] = "Auto Download",
        ["종료 후 적용"] = "Apply After Exit",
        ["업데이트 받기"] = "Get Update",
        ["업데이트 취소"] = "Cancel Update",
        ["업데이트 확인"] = "Check Updates",
        ["다운로드 페이지"] = "Download Page",
        ["안정판"] = "Stable",
        ["베타 포함"] = "Include Beta",
        ["인증 미설정"] = "Authentication not configured",
        ["미확인"] = "Not checked",
        ["확인 중"] = "Checking",
        ["확인 대기"] = "Check queued",
        ["최신 버전"] = "Up to date",
        ["새 버전 있음"] = "Update available",
        ["인증 필요"] = "Authentication required",
        ["접근할 수 없음"] = "Access unavailable",
        ["요청 제한"] = "Rate limited",
        ["확인 실패"] = "Check failed",
        ["이전 확인 결과"] = "Previous check result",
        ["일부만 확인됨"] = "Incomplete check",
        ["확인 취소됨"] = "Check cancelled",
        ["현재 버전"] = "Current version",
        ["새 버전"] = "New version",
        ["마지막 확인"] = "Last checked",
        ["다음 확인"] = "Next check",
        ["변경 사항"] = "Changes",
        ["변경 사항 없음"] = "No release notes",
        ["베타와 안정판의 새 버전을 확인합니다.\n클릭하면 안정판으로 전환합니다."] = "Checks beta and stable releases.\nClick to switch to stable only.",
        ["안정판의 새 버전을 확인합니다.\n클릭하면 베타 포함으로 전환합니다."] = "Checks stable releases.\nClick to include beta releases.",
        ["클릭: 안정판 ↔ 베타 포함\n파일을 자동으로 교체하지 않습니다."] = "Click: Stable ↔ Include Beta\nFiles are not replaced automatically.",
        ["새 버전과 변경 사항을 확인합니다.\n비공개 릴리스에는 인증이 필요합니다."] = "Checks versions and release notes.\nPrivate releases require authentication.",
        ["선택한 채널의 GitHub 릴리스 목록을 엽니다.\n다운로드와 설치는 직접 진행합니다."] = "Opens GitHub releases for the selected channel.\nDownload and install it yourself.",
        ["자동 선택은 게임 메뉴 언어에 맞춰\n모드 설명 언어를 설정합니다.\n게임 자체 언어는 변경하지 않습니다."] = "Auto follows the game menu language.\nOnly mod text is affected.",
        ["모드 설명을 한국어로 표시합니다.\n게임 자체 언어는 변경하지 않습니다."] = "Displays mod text in Korean.\nGame language stays unchanged.",
        ["모드 설명을 영어로 표시합니다.\n게임 자체 언어는 변경하지 않습니다."] = "Displays mod text in English.\nGame language stays unchanged.",
        ["캐릭터 이름을 본명으로 표시합니다.\n계정 닉네임은 변경하지 않습니다."] = "Displays character names.\nPlayer nicknames stay unchanged.",
        ["캐릭터 이름을 이명으로 표시합니다.\n계정 닉네임은 변경하지 않습니다."] = "Displays character titles.\nPlayer nicknames stay unchanged.",
        ["손패를 게임의 기본 배치로 표시합니다."] = "Uses the native hand layout.",
        ["묶음을 클릭하면 가로로 펼칩니다.\n다른 곳을 클릭하면 접습니다.\n접힌 카드는 반투명하게 표시합니다."] = "Click a pile to expand horizontally.\nClick outside to collapse.\nFolded cards are translucent.",
        ["묶음에 마우스를 올리면 위로 펼칩니다.\n마우스가 벗어나면 접습니다.\n접힌 카드는 반투명하게 표시합니다."] = "Hover a pile to expand upward.\nLeave the pile to collapse.\nFolded cards are translucent.",
        ["전투 중 활성 쉴드를 캐릭터에 표시합니다.\n쉴드가 있는 내가 방어자이면\n방어 버튼과 방어 카드를 잠급니다."] = "Shows active shields on combat actors.\nLocks Defend and defense cards\nwhen you are the shielded defender.",
        ["모드의 전투 쉴드 표시와\n방어 버튼·카드 잠금을 적용하지 않습니다."] = "Does not apply mod combat shields\nor Defend/defense-card locks.",
        ["공격·놓아주기, 방어·회피 및\n카드 사용 추천을 표시하지 않습니다."] = "Hides attack/let-go, defense/dodge\nand card recommendations.",
        ["추천에 마우스를 올려도\n피해·전투불능 확률을 표시하지 않습니다."] = "Hides damage and KO chances\nwhen hovering over advice.",
        ["KO·생존 최소 조건 인디케이터를\n표시하지 않습니다."] = "Hides the minimum KO/survival\ncondition indicator.",
        ["추천에 마우스를 올리면\n피해·전투불능 확률을 표시합니다."] = "Hover over advice to see\ndamage and KO chances.",
        ["KO 최소 조건 표시"] = "KO Conditions",
        ["공격·관전 시 KO 최소 조건,\n방어 시 생존 최소 조건을 표시합니다.\n가장 유리한 결과 기준이며\n성공을 보장하는 숫자는 아닙니다."] = "Shows KO conditions for attacks\nand spectating; survival for defense.\nUses best-case outcomes,\nnot a guarantee of success.",
        ["카드·칩 핑의 추가 상세 팝업을\n표시하지 않습니다.\n기본 핑 말풍선은 유지합니다."] = "Hides extra card/chip ping details.\nNative ping bubbles remain.",
        ["모드의 전투 정보와 반격 표시를\n표시하지 않습니다.\n슈슈 쉴드 설정은 별도로 적용합니다."] = "Hides mod battle info and counters.\nShushu Shield remains independent.",
        ["모드의 필드 이름·HP·버프 표시를\n표시하지 않습니다."] = "Hides mod field names, HP\nand public effect icons.",
        ["선택형 상세 진단 기록을 끕니다.\n기본 오류 기록은 유지합니다."] = "Disables optional detailed diagnostics.\nBasic error reporting remains.",
        ["다른 창으로 전환해도\n모드가 게임 소리를 끄지 않습니다."] = "The mod does not mute the game\nwhen switching to another window.",
        ["게임 창이 비활성 상태여도\n입력 대기 알림을 보내지 않습니다."] = "Does not send input-wait alerts\nwhile the game is unfocused.",
        ["게임 창이 비활성 상태에서\n내 입력을 기다리면 Windows로 알립니다.\nWindows 알림 설정을 따릅니다."] = "Sends Windows input-wait alerts\nwhile the game is unfocused.\nWindows notification settings apply.",
        ["게임 창이 비활성 상태에서\n내 입력을 기다리면 작업표시줄을 깜빡입니다."] = "Flashes the taskbar for input waits\nwhile the game is unfocused.",
        ["모드 설정"] = "Mod Settings",
        ["피해량"] = "Damage",
        ["BetterAstralParty · 모드 설정 (F8)"] = "BetterAstralParty · Mod Settings (F8)",
        ["✓ 저장됨"] = "Saved",
        ["돌아가기"] = "Back",
        ["표시 크기"] = "UI Scale",
        ["배경 불투명도"] = "Opacity",
        ["본명"] = "Name",
        ["이명"] = "Title",
        ["모드 기능"] = "Mod Features",
        ["일반 설정"] = "General",
        ["언어"] = "Language",
        ["자동"] = "Auto",
        ["전투 추천"] = "Battle Advice",
        ["호버 수치"] = "Hover Details",
        ["카드·칩 핑 팝업"] = "Card / Chip Pings",
        ["손패 기능별 정렬"] = "Grouped Hand",
        ["기본"] = "Native",
        ["호버 펼치기"] = "Hover",
        ["클릭 펼치기"] = "Click",
        ["기본 → 클릭 펼치기 → 호버 펼치기\n클릭 방식은 묶음을 눌러 가로로 펼칩니다.\n접힌 묶음은 반투명하게 표시합니다."] = "Native > Click > Hover\nClick a pile to open a horizontal row.\nFolded piles are translucent.",
        ["전투 정보 표시"] = "Battle Info",
        ["필드 버프 표시"] = "Field Indicators",
        ["캐릭터 이름"] = "Character Names",
        ["충돌 진단 로그"] = "Diagnostic Log",
        ["진단 폴더 열기"] = "Open Diagnostics Folder",
        ["진단 ZIP 만들기"] = "Create Diagnostics ZIP",
        ["진단 ZIP 만드는 중"] = "Creating ZIP",
        ["진단 ZIP 생성됨"] = "ZIP Created",
        ["진단 ZIP 생성 실패"] = "ZIP Failed",
        ["진단 폴더 열기 실패"] = "Could Not Open Folder",
        ["만든 진단 ZIP이 모이는 폴더를 엽니다.\nZIP 안에서 처리된 로그를 볼 수 있습니다."] = "Opens the diagnostics ZIP folder.\nOpen a ZIP to read filtered logs.",
        ["최근 로그의 허용 항목을 ZIP으로 요약합니다.\n카드·손패·채팅·전체 설정·덤프는 제외합니다.\n개인정보가 남을 수 있으니 내용을 확인한 뒤\n개발자에게 직접 첨부하세요."] = "Creates a ZIP of allowed log summaries.\nExcludes cards, hands, chat, dumps\nand full settings. Review for remaining\npersonal data before attaching.",
        ["창 비활성화 시 음소거"] = "Mute When Unfocused",
        ["입력 대기 알림"] = "Input Alert",
        ["작업표시줄"] = "Taskbar",
        ["Windows 알림"] = "Windows",
        ["게임에서 입력을 기다리고 있습니다."] = "The game is waiting for your input.",
        ["비활성 창에서 내 입력 대기를 알립니다.\n클릭: 작업표시줄 → Windows 알림 → OFF\nWindows 알림 설정에 따라 표시됩니다."] = "Alerts when unfocused and awaiting input.\nClick: Taskbar > Windows > OFF.\nWindows notification settings apply.",
        ["기능 안내"] = "Help",
        ["PvE에서 공격·놓아주기,\n방어·회피와 카드 사용을 추천합니다."] = "PvE advice for attacking,\nletting go, defending,\ndodging and playing cards.",
        ["추천에 마우스를 올리면\n피해·전투불능 확률을 봅니다."] = "Hover over advice to see\ndamage and KO chances.",
        ["카드·칩 핑 말풍선을 클릭하면\n원본 이미지와 설명을 봅니다."] = "Click a card or chip ping\nto see its image and details.",
        ["HP 아래 손패 장수·공개 효과와\n플레이어·몬스터 반격을 표시합니다.\n효과 설명은 아이콘 호버로 봅니다."] = "Shows public cards/effects and\nplayer/monster counters.\nHover effects for details.",
        ["슈슈 쉴드 표시"] = "Shushu Shield",
        ["전투 중 캐릭터에 쉴드 효과를 표시하고\n방어자일 때 방어 버튼·카드를 잠급니다.\nOFF: 표시와 잠금 모두 해제.\n전투 정보 표시와 별도 설정입니다."] = "Shows shields on combat actors;\nlocks Defend and defense cards\nwhile shielded. OFF restores all.\nIndependent of Battle Info.",
        ["필드 플레이어 이름·HP를 표시합니다.\n공개 버프가 있으면 아이콘도 표시하며\n클릭하면 효과 설명을 봅니다."] = "Shows player names and HP.\nClick public effect icons\nfor details.",
        ["ON: 본명 / OFF: 이명\n계정 닉네임은 바꾸지 않습니다."] = "ON: names / OFF: titles.\nPlayer nicknames stay unchanged.",
        ["오류·성능·공개 전투 계산을 기록합니다.\n계산 불일치는 별도 보존합니다.\n문제를 확인할 때만 켜 주세요."] = "Logs errors, performance\nand public combat calculations.\nKeeps mismatch reports.\nEnable only for diagnosis.",
        ["다른 창으로 전환하면 소리를 끕니다.\n게임으로 돌아오면 복원합니다.\n저장된 볼륨은 바꾸지 않습니다."] = "Mutes when focus is lost.\nRestores audio on return.\nSaved volume stays unchanged.",
        ["추천과 전투 상태 표시의\n크기를 조절합니다."] = "Scales advice and\nbattle status panels.",
        ["전투 패널 바탕만 조절합니다.\n0%에서도 글자·아이콘·테두리는\n유지됩니다."] = "Changes battle panel fill only.\nText, icons and borders\nremain visible at 0%.",
        ["설정을 닫고 돌아갑니다.\n변경 사항은 자동 저장됩니다."] = "Closes settings.\nChanges are saved automatically.",
        ["설정 버튼에 마우스를 올리면\n해당 기능을 안내합니다."] = "Hover over a setting\nfor a short description.",
        ["자동은 게임 메뉴의 언어를 따릅니다.\n클릭: 자동 → 한국어 → English\n게임 자체 언어는 바꾸지 않습니다."] = "Auto follows the game menu.\nClick: Auto > Korean > English.\nDoes not change game language.",
        ["기능별 표시 설정을 변경합니다."] = "Configure mod features.",
        ["언어·음소거·알림·진단을 설정합니다."] = "Configure language, audio,\nalerts and diagnostics.",
        ["공개 손패 장수"] = "Public hand size",
        ["마우스 휠로 효과 페이지 넘기기"] = "Scroll to browse effects",
        ["KO 최소 조건"] = "Minimum KO conditions",
        ["생존 최소 조건"] = "Minimum survival conditions",
        ["KO 불가"] = "KO impossible",
        ["확정 KO"] = "Guaranteed KO",
        ["공격자"] = "Attacker",
        ["방어자"] = "Defender",
        ["계산 정보 부족"] = "Insufficient data",
        ["카드 사용 후"] = "After card",
        ["현재 상태"] = "Current",
        ["정보 부족"] = "Unknown",
        ["불가"] = "None",
        ["추천"] = "Recommended",
        ["놓아주기"] = "Let Go",
        ["이번 전투 피해 없음"] = "No damage this battle",
        ["효과 없음 · 보존"] = "No benefit · Save",
        ["효과 있음"] = "Beneficial",
        ["조건부 이득"] = "Conditional benefit",
        ["더 싼 카드 우선"] = "Use cheaper card",
        ["판단 보류"] = "Unknown",
        ["사용 불가"] = "Unavailable",
        ["이득"] = "Benefit",
        ["조건부"] = "Conditional",
        ["보존"] = "Save",
        ["저비용 우선"] = "Cheaper first",
        ["방어자 기준 · "] = "Defender · ",
        ["방어 추천"] = "Defend advised",
        ["회피 추천"] = "Dodge advised",
        ["회피 불가"] = "Cannot dodge",
        ["방어"] = "Defend",
        ["회피"] = "Dodge",
        ["예상 피해"] = "Damage",
        ["평균 피해"] = "Mean damage",
        ["평균 체력 손실"] = "Mean HP loss",
        ["전투불능"] = "KO",
        ["추가 피해"] = "Extra damage",
        ["피해 감소"] = "Damage reduction",
        ["비용"] = "Cost",
        ["상대 KO"] = "Enemy KO",
        ["내 KO"] = "Self KO",
        ["현재 수치"] = "Value",
        ["중첩·진행"] = "Stacks/progress",
        ["남은 턴"] = "Turns left",
        ["게임 필드 변경 · 로그 확인"] = "Game fields changed; check log.",
        ["게임 함수 변경 · 로그 확인"] = "Game methods changed; check log.",
        ["게임 API 타입 변경 · 로그 확인"] = "Game API changed; check log.",
        ["필수 타입·UI 없음 · 로그 확인"] = "Required type/UI missing; check log.",
        ["호환성 오류 · 재시작 필요"] = "Compatibility error; restart required.",
    };
    private static readonly Regex Phrases = new(string.Join("|",
        English.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)), RegexOptions.CultureInvariant);
    internal static string Text(string text)
    {
        if (Korean || text.Length == 0) return text;
        if (English.TryGetValue(text, out var english)) return english;
        return Phrases.Replace(text, match => English[match.Value]);
    }

    internal static string AutomaticStatus(AutomaticUpdateStatus status) => Korean ? status switch
    {
        AutomaticUpdateStatus.NotConfigured => "업데이트 미설정", AutomaticUpdateStatus.Idle => "대기",
        AutomaticUpdateStatus.MissingAssets => "수동 다운로드 필요", AutomaticUpdateStatus.Downloading => "다운로드 중",
        AutomaticUpdateStatus.Preparing => "검증 중", AutomaticUpdateStatus.Ready => "준비됨", AutomaticUpdateStatus.Queued => "종료 후 적용 대기",
        AutomaticUpdateStatus.Cancelling => "취소 기록 중", AutomaticUpdateStatus.CancelFailed => "취소 기록 실패",
        AutomaticUpdateStatus.CheckingInstallation => "설치 상태 확인 중", AutomaticUpdateStatus.RecoveryRequired => "복구 필요",
        AutomaticUpdateStatus.FilesBusy => "업데이트 파일 사용 중", AutomaticUpdateStatus.AlreadyClaimed => "처리된 업데이트 요청",
        AutomaticUpdateStatus.Cancelled => "취소됨", AutomaticUpdateStatus.ManualUpgradeRequired => "수동 업그레이드 필요",
        AutomaticUpdateStatus.RetryWaiting => "재시도 대기", AutomaticUpdateStatus.FaultDisabled => "실패로 자동 OFF", AutomaticUpdateStatus.FaultStorageFailed => "실패 기록/설정 저장 확인 필요", AutomaticUpdateStatus.Reactivating => "다시 사용 준비 중",
        AutomaticUpdateStatus.TransitionRequired => "채널 전환 확인 필요", AutomaticUpdateStatus.Committed => "적용 완료", _ => "업데이트 실패"
    } : status switch
    {
        AutomaticUpdateStatus.NotConfigured => "Not Configured", AutomaticUpdateStatus.Idle => "Idle",
        AutomaticUpdateStatus.MissingAssets => "Manual Download Required", AutomaticUpdateStatus.Downloading => "Downloading",
        AutomaticUpdateStatus.Preparing => "Verifying", AutomaticUpdateStatus.Ready => "Ready", AutomaticUpdateStatus.Queued => "Waiting for Exit",
        AutomaticUpdateStatus.Cancelling => "Recording Cancellation", AutomaticUpdateStatus.CancelFailed => "Cancellation Record Failed",
        AutomaticUpdateStatus.CheckingInstallation => "Checking Installation", AutomaticUpdateStatus.RecoveryRequired => "Recovery Required",
        AutomaticUpdateStatus.FilesBusy => "Update Files In Use", AutomaticUpdateStatus.AlreadyClaimed => "Request Already Claimed",
        AutomaticUpdateStatus.Cancelled => "Cancelled", AutomaticUpdateStatus.ManualUpgradeRequired => "Manual Upgrade Required",
        AutomaticUpdateStatus.RetryWaiting => "Retry Waiting", AutomaticUpdateStatus.FaultDisabled => "Disabled After Failure", AutomaticUpdateStatus.FaultStorageFailed => "Failure/Settings Storage Needs Attention", AutomaticUpdateStatus.Reactivating => "Re-enabling Updates",
        AutomaticUpdateStatus.TransitionRequired => "Confirm Channel Transition", AutomaticUpdateStatus.Committed => "Applied", _ => "Update Failed"
    };
    internal static string AutomaticDetails(AutomaticUpdateStatus status, string? recoveryStage = null, Updating.UpdateFault? fault = null, ReleaseChannel? channel = null) => AutomaticStatus(status) + "\n" + (status == AutomaticUpdateStatus.NotConfigured
        ? channel == ReleaseChannel.Stable
            ? Korean ? "Stable 저장소 ID·서명·도우미 계약을 확인해야 합니다. Stable 조회는 로그인 없이 진행합니다." : "Verify the Stable repository identity, signing trust and helper contract. Stable checks are anonymous."
            : Korean ? "선택한 저장소·서명·도우미 계약을 확인하세요. 비공개 Beta는 업데이트 인증도 필요합니다." : "Verify the selected repository, signing trust and helper contract. Private Beta also needs update authentication."
        : status == AutomaticUpdateStatus.TransitionRequired ? Korean ? "대상 채널과 버전을 새로 확인하고 명시적으로 승인해야 합니다. 저장된 OFF 설정은 유지됩니다." : "Recheck the destination channel and version, then confirm explicitly. Saved OFF settings are preserved."
        : status == AutomaticUpdateStatus.RecoveryRequired ? Korean ? "미완료 업데이트를 확인했습니다. 게임과 모드 런처를 종료한 후 복구 안내에 따라 도우미의 --recover 명령을 실행하세요. 백업은 보존됩니다." : "An unfinished update needs recovery. Close the game and mod launcher, then follow the helper --recover guide. Backups are preserved."
        : status == AutomaticUpdateStatus.FaultStorageFailed ? Korean ? "실패 기록이나 설정을 저장하지 못했습니다. 자동 실행을 차단했습니다. 저장 위치와 복구 상태를 확인한 뒤 상태를 다시 확인하세요." : "Failure evidence or settings could not be saved. Automatic execution is blocked. Check storage and recovery, then recheck installation."
        : status == AutomaticUpdateStatus.CancelFailed ? Korean ? "취소 완료를 확인하지 못했습니다. 업데이트를 다시 실행하지 말고 복구 안내를 확인하세요." : "Cancellation completion could not be confirmed. Check recovery guidance before retrying."
        : status == AutomaticUpdateStatus.Cancelling ? Korean ? "도우미에 취소 신호를 보냈으며 디스크 기록을 확인 중입니다." : "The helper was signaled; cancellation persistence is being confirmed."
        : status == AutomaticUpdateStatus.ManualUpgradeRequired ? Korean ? "설치를 자동으로 변경할 수 없습니다. 수동 업그레이드가 필요합니다." : "This installation requires a manual upgrade."
        : Korean ? "자동 다운로드와 종료 후 적용은 각각 선택할 수 있습니다." : "Choose automatic download and apply after exit independently.")
        + (fault == null ? "" : "\n" + (Korean ? "자동 다운로드와 종료 후 적용을 OFF로 저장합니다. 원인을 해결하고 ‘다시 사용’을 선택하면 두 옵션을 ON으로 복원합니다.\n실패 단계/원인: " : "Both options are saved OFF. Resolve the cause, then select Re-enable Updates to restore both ON.\nFailure phase/reason: ") + FaultPhaseText(fault.Phase) + "/" + FaultReasonText(fault.Reason)
            + (fault.PreferencesSaveFailed ? Korean ? "\n설정 저장에 실패했습니다. 실패 표지가 자동 실행을 계속 차단합니다." : "\nSettings could not be saved. Persistent failure evidence still blocks automatic execution." : ""))
        + (status == AutomaticUpdateStatus.RetryWaiting ? Korean ? "\n최대 두 번 시도합니다. 대기 중에는 네트워크 요청을 보내지 않습니다." : "\nAt most two attempts. No network requests while waiting." : "")
        + (status == AutomaticUpdateStatus.RecoveryRequired && recoveryStage is { Length: > 0 } ? "\nBetterAstralParty-UpdateHelper.exe --recover \"" + (Korean ? "게임 INT 폴더" : "game INT folder") + "\" " + recoveryStage : "");

    private static string FaultPhaseText(Updating.FaultPhase phase) => Korean ? phase switch {
        Updating.FaultPhase.Download => "다운로드", Updating.FaultPhase.Verification => "서명/파일 검증", Updating.FaultPhase.Preparation => "준비", Updating.FaultPhase.Apply => "적용", _ => "복구"
    } : phase.ToString();
    private static string FaultReasonText(Updating.FaultReason reason) => Korean ? reason switch {
        Updating.FaultReason.Network => "네트워크 오류", Updating.FaultReason.Timeout => "시간 초과", Updating.FaultReason.Authentication => "인증 오류", Updating.FaultReason.Access => "접근 오류", Updating.FaultReason.RateLimited => "요청 제한",
        Updating.FaultReason.Integrity => "검증 실패", Updating.FaultReason.Preparation => "준비 실패", Updating.FaultReason.Apply => "적용 실패", Updating.FaultReason.Recovery => "복구 실패", Updating.FaultReason.Interrupted => "작업 중단",
        Updating.FaultReason.Storage => "저장 실패", _ => "수동 업그레이드 필요"
    } : reason.ToString();

    internal static string UpdateStatus(ReleaseUpdateStatus status) => Text(status switch
    {
        ReleaseUpdateStatus.NotConfigured => Korean ? "업데이트 미설정" : "Update Not Configured",
        ReleaseUpdateStatus.NotChecked => "미확인",
        ReleaseUpdateStatus.Checking => "확인 중",
        ReleaseUpdateStatus.RetryWaiting => "확인 대기",
        ReleaseUpdateStatus.UpToDate => "최신 버전",
        ReleaseUpdateStatus.Available => "새 버전 있음",
        ReleaseUpdateStatus.AuthenticationRequired => "인증 필요",
        ReleaseUpdateStatus.AccessUnavailable => "접근할 수 없음",
        ReleaseUpdateStatus.RateLimited => "요청 제한",
        ReleaseUpdateStatus.Stale => "이전 확인 결과",
        ReleaseUpdateStatus.Incomplete => "일부만 확인됨",
        ReleaseUpdateStatus.Cancelled => "확인 취소됨",
        _ => "확인 실패"
    });

    internal static string UpdateNotificationDetails(ReleaseUpdate release, string current,
        AutomaticUpdateStatus automatic, ReleaseUpdateStatus check, bool matchingTarget = true)
    {
        var status = !matchingTarget || automatic == AutomaticUpdateStatus.NotConfigured ? UpdateStatus(check) : AutomaticStatus(automatic);
        var guidance = !matchingTarget ? Korean ? "이 릴리스와 일치하는 작업 상태가 없습니다. 업데이트 설정에서 현재 작업을 확인하세요." : "No operation status matches this release. Check the current operation in Update Settings." : automatic switch
        {
            AutomaticUpdateStatus.Downloading or AutomaticUpdateStatus.Preparing or AutomaticUpdateStatus.RetryWaiting =>
                Korean ? "준비가 끝나면 종료 후 적용 여부에 따라 대기합니다." : "Preparation continues with your current apply-after-exit setting.",
            AutomaticUpdateStatus.TransitionRequired => Korean ? "채널 전환은 업데이트 설정에서 별도로 확인하고 승인해야 합니다." : "Review and confirm a channel transition separately in Update Settings.",
            AutomaticUpdateStatus.Ready => Korean ? "다운로드가 준비되었습니다. 업데이트 설정을 확인하세요." : "The download is ready. Check Update Settings.",
            AutomaticUpdateStatus.Queued => Korean ? "게임과 모드 런처의 종료를 기다립니다. 적용 후 게임은 직접 실행하세요." : "Waiting for the game and mod launcher to close. Start the game yourself after the update.",
            AutomaticUpdateStatus.Cancelled or AutomaticUpdateStatus.Cancelling => Korean ? "취소 상태는 업데이트 설정에서 확인할 수 있습니다." : "Check the cancellation status in Update Settings.",
            AutomaticUpdateStatus.FaultDisabled or AutomaticUpdateStatus.FaultStorageFailed or AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.CancelFailed or AutomaticUpdateStatus.Failed =>
                Korean ? "업데이트 설정에서 실패 또는 복구 상태를 확인하세요." : "Check Update Settings for failure or recovery status.",
            _ => Korean ? "업데이트 설정에서 자세한 내용을 확인하세요." : "Open Update Settings for details."
        };
        return Text("현재 버전") + ": " + current + "\n" + Text("새 버전") + ": " + release.Version
            + "\n\n" + Text("현재 업데이트 상태") + ": " + status
            + (check == ReleaseUpdateStatus.Available ? "" : "\n" + UpdateStatus(check)) + "\n\n" + guidance;
    }

    internal static string UpdateDetails(ReleaseUpdateResult result, string current)
    {
        var body = Text("현재 버전") + ": " + current + "\n" + UpdateStatus(result.Status);
        if (result.Failure is { } failure) body += " · " + UpdateStatus(failure);
        if (result.CheckedAt is { } time) body += "\n" + Text("마지막 확인") + ": " + time.UtcDateTime.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
        if (result.RetryAt is { } retryAt) body += "\n" + Text("다음 확인") + ": " + retryAt.UtcDateTime.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
        if (result.Release is not { } release) return body;
        body += "\n" + Text("새 버전") + ": " + release.Version;
        // Release text is displayed literally, never passed through translation/markup parsing.
        return body + "\n" + Text("변경 사항") + ":\n" + (release.Notes.Length == 0 ? Text("변경 사항 없음") : ReleaseUpdates.PlainText(release.Notes, 700));
    }
}
