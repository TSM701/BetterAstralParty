using System.Text.RegularExpressions;

namespace BetterAstralParty;

// Only mod-owned presentation strings. Never translate player input or native descriptions.
internal static class ModText
{
    internal static string ChannelName(string channel) => channel switch { "Stable" => Text("릴리즈"), "PreRelease" => Text("프리 릴리즈"), "Beta" => Text("비공개 베타"), _ => "—" };
    // Switch Channel row state: the target when confirmable, otherwise why there is nothing to confirm.
    internal static string SwitchState(bool required, bool waiting, string? target) => target != null ? target
        : !required ? Korean ? "필요 없음" : "Not Needed" : waiting ? Korean ? "새 버전 대기" : "Waiting for a Release" : Korean ? "확인 중" : "Checking";
    internal static string InstalledChannelSuffix(string channel) => " (" + (Korean ? "설치: " : "Installed: ") + ChannelName(channel) + ")";
    internal static string ChannelTransitionTitle => Korean ? "채널 전환 확인" : "Confirm Channel Switch";
    internal static string ChannelTransitionConfirm => Korean ? "전환 시작" : "Start Switch";
    internal static string ChannelTransitionExit => Korean ? "종료 및 전환" : "Exit and Switch";
    internal static string ChannelTransitionUnavailable => Korean ? "채널 전환 확인을 멈췄습니다." : "The channel switch check stopped.";
    internal static string ChannelTransitionReason(string reason, int seconds = 0) => Korean ? reason switch
    {
        "retry" => $"방금 확인을 시도했습니다. {seconds}초 뒤 다시 시도하세요.",
        "changed" => "확인하는 동안 업데이트 정보가 바뀌었습니다. 다시 시도하세요.",
        "verify" => "서명된 업데이트 정보를 확인하지 못했습니다.",
        "install" => "설치 상태가 전환 조건과 맞지 않습니다. 원인은 진단 로그에 기록됩니다.",
        _ => "GitHub에 연결하지 못했습니다. 잠시 후 다시 시도하세요."
    } : reason switch
    {
        "retry" => $"A check just ran. Try again in {seconds} seconds.",
        "changed" => "Update information changed during the check. Try again.",
        "verify" => "The signed update information could not be verified.",
        "install" => "The installation does not meet the switch requirements. The reason is recorded in diagnostics.",
        _ => "Could not reach GitHub. Try again shortly."
    };
    // Shown after approval: the switch downloads and applies in this game session, so the game must stay open.
    internal static string ChannelTransitionProgress(string version, AutomaticUpdateStatus status) => Korean
        ? $"{version} 전환 중: {AutomaticStatus(status)}\n\n받기와 적용이 끝날 때까지 게임을 끄지 마세요. 끝나면 '종료 및 전환' 버튼이 켜집니다."
        : $"Switching to {version}: {AutomaticStatus(status)}\n\nKeep the game open until the download and apply finish. Exit and Switch turns on when they are done.";
    internal static string ChannelTransitionReady(string version) => Korean
        ? $"{version} 전환 준비가 끝났습니다.\n\n'종료 및 전환'을 누르면 게임이 종료되고, 다음 실행부터 새 버전을 사용합니다."
        : $"{version} is ready.\n\nExit and Switch closes the game; the new version is used from the next launch.";
    internal static string UpdateProgressTitle => Korean ? "업데이트 진행" : "Update Progress";
    internal static string UpdateProgressExit => Korean ? "게임 종료" : "Exit Game";
    internal static string UpdateProgressBody(string version, AutomaticUpdateStatus status) => Korean
        ? $"{version} 업데이트 중: {AutomaticStatus(status)}\n\n받기와 적용이 끝날 때까지 게임을 끄지 마세요. 끝나면 '게임 종료' 버튼이 켜집니다."
        : $"Updating to {version}: {AutomaticStatus(status)}\n\nKeep the game open until the download and apply finish. Exit Game turns on when they are done.";
    internal static string UpdateProgressReady(string version) => Korean
        ? $"{version} 업데이트 준비가 끝났습니다.\n\n'게임 종료'를 누르면 게임이 종료되고, 다음 실행부터 새 버전을 사용합니다."
        : $"{version} is ready.\n\nExit Game closes the game; the new version is used from the next launch.";
    internal static string UpdateProgressStopped(AutomaticUpdateStatus status) => Korean
        ? $"업데이트를 끝내지 못했습니다: {AutomaticStatus(status)}\n\n업데이트 설정에서 다시 시도하세요. 지금 버전은 그대로 사용할 수 있습니다."
        : $"The update did not finish: {AutomaticStatus(status)}\n\nTry again in Update Settings. The current version keeps working.";
    // Home capsule under the Mod Settings button while an update runs.
    internal static string UpdateProgressCapsule(string version, AutomaticUpdateStatus status, int step, bool stopped, bool done) => $"{version} " + (Korean
        ? done ? "적용 완료" : stopped ? "오류" : step == 1 ? "확인 중" : status == AutomaticUpdateStatus.Downloading ? "받는 중" : AutomaticStatus(status)
        : done ? "Ready" : stopped ? "Error" : step == 1 ? "Checking" : AutomaticStatus(status));
    internal static string UpdatePercent(int percent, long read, long total) =>
        total > 0 ? $"{percent}% · {(read + 1023) / 1024} / {(total + 1023) / 1024}KB" : $"{percent}%";
    internal static string UpdateRetry => Korean ? "다시 시도" : "Try Again";
    // Short reason (capsule) and what to do (window) for a stopped update, keyed by the recorded failure reason.
    internal static string UpdateStopReason(string key) => Korean ? key switch
    {
        "network" => "GitHub에 연결하지 못했습니다", "timeout" => "연결 시간이 초과됐습니다", "ratelimited" => "GitHub 요청 한도에 걸렸습니다",
        "integrity" => "받은 파일의 서명이 맞지 않습니다", "storage" => "게임 폴더에 쓰지 못했습니다", "preparation" => "적용 준비에 실패했습니다",
        "apply" => "파일 교체에 실패해 되돌렸습니다", "interrupted" => "적용 중에 중단됐습니다", "recovery" => "업데이트 복구가 필요합니다",
        "authentication" or "access" => "비공개 베타 인증이 필요합니다", "manualupgrade" => "수동 다운로드가 필요합니다", _ => "업데이트를 끝내지 못했습니다"
    } : key switch
    {
        "network" => "Could not reach GitHub", "timeout" => "The connection timed out", "ratelimited" => "GitHub's request limit was reached",
        "integrity" => "The downloaded file's signature did not match", "storage" => "Could not write to the game folder", "preparation" => "Preparing the update failed",
        "apply" => "Replacing files failed and was rolled back", "interrupted" => "Applying was interrupted", "recovery" => "Update recovery is required",
        "authentication" or "access" => "Private Beta sign-in is required", "manualupgrade" => "A manual download is required", _ => "The update did not finish"
    };
    internal static string UpdateStopAdvice(string key) => Korean ? key switch
    {
        "network" => "인터넷 연결을 확인하고 다시 시도하세요.", "timeout" => "연결을 확인하고 잠시 뒤 다시 시도하세요.", "ratelimited" => "잠시 뒤 다시 시도하세요.",
        "integrity" => "적용하지 않았습니다. 다시 시도하고, 계속되면 진단 ZIP을 보내 주세요.", "storage" => "게임 폴더 권한이나 백신 차단을 확인한 뒤 다시 시도하세요.",
        "apply" => "원래 버전 그대로입니다. 다시 시도하세요.", "interrupted" => "게임을 다시 실행하면 정리됩니다. 계속되면 Install.cmd를 실행하세요.",
        "recovery" => "게임을 닫고 Install.cmd를 다시 실행하세요.", "authentication" or "access" => "업데이트 설정에서 베타 인증을 다시 하세요.",
        "manualupgrade" => "다운로드 페이지에서 설치 ZIP을 받아 Install.cmd로 설치하세요.", _ => "다시 시도하고, 계속되면 진단 ZIP을 보내 주세요."
    } : key switch
    {
        "network" => "Check your internet connection and try again.", "timeout" => "Check the connection and try again shortly.", "ratelimited" => "Try again shortly.",
        "integrity" => "Nothing was applied. Try again; if it repeats, send a diagnostics ZIP.", "storage" => "Check game folder permissions or antivirus blocking, then try again.",
        "apply" => "The current version is unchanged. Try again.", "interrupted" => "Restarting the game cleans it up. If it repeats, run Install.cmd.",
        "recovery" => "Close the game and run Install.cmd again.", "authentication" or "access" => "Sign in to the Beta again in Update Settings.",
        "manualupgrade" => "Download the installation ZIP from the download page and install it with Install.cmd.", _ => "Try again; if it repeats, send a diagnostics ZIP."
    };
    internal static string UpdateStopDetails(string version, int percent, string key, string code) => Korean
        ? $"{version} 업데이트 중 오류가 발생했습니다 ({percent}%).\n\n이유: {UpdateStopReason(key)}\n할 일: {UpdateStopAdvice(key)}\n오류 코드: {code}\n\n지금 버전은 그대로 사용할 수 있습니다. 문의할 때는 오류 코드와 진단 ZIP을 함께 보내 주세요."
        : $"The {version} update hit an error at {percent}%.\n\nReason: {UpdateStopReason(key)}\nWhat to do: {UpdateStopAdvice(key)}\nError code: {code}\n\nThe current version keeps working. When asking for help, include the error code and a diagnostics ZIP.";
    internal static string ChannelTransitionStopped(AutomaticUpdateStatus status) => Korean
        ? $"전환을 끝내지 못했습니다: {AutomaticStatus(status)}\n\n업데이트 설정에서 다시 시도하세요. 지금 버전은 그대로 사용할 수 있습니다."
        : $"The switch did not finish: {AutomaticStatus(status)}\n\nTry again in Update Settings. The current version keeps working.";
    internal static string ChannelTransitionDetails(string source, string current, string target, string kind, string version, string repository) => Korean
        ? $"전환할 채널: {ChannelName(target)}\n현재: {ChannelName(source)} {current}\n대상: {ChannelName(kind)} {version}\n저장소: {repository}\n\n선택한 채널의 서명된 최신 대상을 확인했습니다. '전환 시작'을 누르면 게임 안에서 바로 받아 적용합니다. 끝날 때까지 게임을 끄지 마세요. 설정과 저장된 자동 업데이트 선택은 바꾸지 않습니다."
        : $"Switch to: {ChannelName(target)}\nInstalled: {ChannelName(source)} {current}\nTarget: {ChannelName(kind)} {version}\nRepository: {repository}\n\nThe signed latest target from the selected channel has been checked. Start Switch downloads and applies it right away in this game session; keep the game open until it finishes. Settings and saved automatic update choices are kept.";
    internal static string SessionTitle(string action) => Korean ? (action == "UpdateAuth" ? "업데이트 인증" : "업데이트 로그아웃") : (action == "UpdateAuth" ? "Connect Updates" : "Sign Out of Updates");
    internal static string SessionDetails(ReleaseSessionStatus status) => (Korean ? "비공개 베타 인증: " : "Private Beta access: ") + (Korean ? status switch {
        ReleaseSessionStatus.NotConfigured => "업데이트 인증이 준비되지 않았습니다.",
        ReleaseSessionStatus.SignedOut => "로그아웃됨", ReleaseSessionStatus.AwaitingInput => "Windows 입력 창에서 PAT를 입력하세요.",
        ReleaseSessionStatus.Connected => "토큰이 연결되었습니다.",
        ReleaseSessionStatus.Restoring => "저장된 인증을 불러오는 중입니다.",
        ReleaseSessionStatus.StorageFailed => "Windows 인증 저장소 작업에 실패했습니다. 인증 또는 로그아웃을 다시 시도하세요.",
        ReleaseSessionStatus.InvalidInput => "입력 형식을 확인하세요.", ReleaseSessionStatus.Cancelled => "입력 취소됨",
        ReleaseSessionStatus.Expired => "세션 만료됨", ReleaseSessionStatus.AuthenticationRequired => "다시 인증하세요.",
        ReleaseSessionStatus.AccessUnavailable => "저장소에 접근할 수 없습니다.", _ => "입력을 완료하지 못했습니다."
    } : status switch {
        ReleaseSessionStatus.NotConfigured => "Update authentication is not configured.",
        ReleaseSessionStatus.SignedOut => "Signed out", ReleaseSessionStatus.AwaitingInput => "Enter the PAT in the Windows prompt.",
        ReleaseSessionStatus.Connected => "Token connected.",
        ReleaseSessionStatus.Restoring => "Restoring saved authentication.",
        ReleaseSessionStatus.StorageFailed => "Windows credential storage failed. Retry Connect Updates or Sign Out.",
        ReleaseSessionStatus.InvalidInput => "Check the input format.", ReleaseSessionStatus.Cancelled => "Input cancelled",
        ReleaseSessionStatus.Expired => "Session expired", ReleaseSessionStatus.AuthenticationRequired => "Sign in again.",
        ReleaseSessionStatus.AccessUnavailable => "Repository access unavailable", _ => "Input could not be completed."
    }) + (Korean ? "\n토큰은 Windows 자격 증명 관리자에 저장됩니다. 로그아웃하면 삭제되며, 만료·철회 시 재인증이 필요합니다." : "\nTokens are saved in Windows Credential Manager. Sign Out deletes them. Expired or revoked tokens require sign-in again.");
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
        ["자동 적용"] = "Apply Automatically",
        ["업데이트 받기"] = "Get Update",
        ["채널 전환"] = "Switch Channel",
        ["업데이트 취소"] = "Cancel Update",
        ["업데이트 확인"] = "Check Updates",
        ["다운로드 페이지"] = "Download Page",
        ["릴리즈"] = "Release",
        ["프리 릴리즈"] = "Pre-release",
        ["비공개 베타"] = "Private Beta",
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
        ["정식 릴리즈만 받습니다."] = "Receives full releases only.",
        ["프리 릴리즈와\n더 새로운 정식 릴리즈를 받습니다."] = "Receives pre-releases and\nnewer full releases.",
        ["비공개 베타 버전을 받습니다.\n토큰 인증이 필요합니다."] = "Receives private beta versions.\nRequires token authentication.",
        ["선택한 채널의 최신 버전을 확인합니다.\n설치된 채널과 다르면 채널 전환에서 승인합니다."] = "Checks the selected channel's latest version.\nIf it differs from the installed channel, approve it in Switch Channel.",
        ["선택한 채널의 대상 버전을 확인하고\n설치된 채널을 전환합니다."] = "Reviews the selected channel's target version\nand switches the installed channel.",
        ["선택한 채널의 새 버전을 확인합니다."] = "Checks for updates on the selected channel.",
        ["선택한 채널의 GitHub 릴리스 목록을 엽니다."] = "Opens GitHub releases for the selected channel.",
        ["모드 안내 언어를 게임 메뉴에 맞춥니다."] = "Uses the game menu language for mod text.",
        ["모드 안내를 한국어로 표시합니다."] = "Displays mod text in Korean.",
        ["모드 안내를 영어로 표시합니다."] = "Displays mod text in English.",
        ["본명으로 표시합니다."] = "Shows real names.",
        ["이명으로 표시합니다."] = "Shows titles.",
        ["게임 기본 배치로 보여줍니다."] = "Uses the native hand layout.",
        ["클릭한 묶음을 가로로 펼치고\n다른 곳을 클릭하면 접습니다."] = "Clicked groups expand sideways\nand fold when you click elsewhere.",
        ["마우스를 올린 묶음을 위로 펼치고\n벗어나면 접습니다."] = "Hovered groups expand upward\nand fold when the mouse leaves.",
        ["슈슈 쉴드가 있는 전투 캐릭터에\n쉴드 효과를 그립니다."] = "Draws the shield effect on combat\ncharacters with a Shushu shield.",
        ["쉴드 효과를 그리지 않습니다."] = "Draws no shield effect.",
        ["내가 쉴드를 가진 방어자이면\n방어 버튼과 방어 카드를 잠급니다."] = "Locks Defend and defense cards\nwhen you defend with a shield.",
        ["방어 버튼과 카드를 잠그지 않습니다."] = "Leaves Defend and cards unlocked.",
        ["자동으로 보내지 않습니다."] = "Sends nothing automatically.",
        ["역할을 보여주지 않습니다."] = "Shows no roles.",
        ["설명을 보여주지 않습니다."] = "Shows no descriptions.",
        ["줌 아웃해도 그대로 보여줍니다."] = "Keeps indicators opaque\nwhen zoomed out.",
        ["거리와 상관없이 보여줍니다."] = "Shows indicators at any distance.",
        ["알림음을 내지 않습니다."] = "Plays no chime.",
        ["반격 아이콘을 멈춘 채 보여줍니다."] = "Shows counter icons without motion.",
        ["핑 선택 모드를 그대로 둡니다."] = "Leaves ping mode as it is.",
        ["피해 숫자를 보여주지 않습니다."] = "Shows no damage number.",
        ["공지가 자동으로 열립니다."] = "Login notices open automatically.",
        ["업데이트 확인을 눌렀을 때만 찾습니다."] = "Looks only when you press Check.",
        ["팝업을 띄우지 않습니다."] = "Shows no pop-up.",
        ["자동으로 받지 않습니다."] = "Downloads nothing automatically.",
        ["자동으로 적용하지 않습니다."] = "Applies nothing automatically.",
        ["슈슈 쉴드 방어 잠금"] = "Shushu Shield Defend Lock",
        ["감사합니다 자동 전송"] = "Auto Thanks",
        ["역할 표시"] = "Role Labels",
        ["준비 화면 스킬 설명"] = "Skill Info in Preparation",
        ["줌 아웃 시 반투명"] = "Fade When Zoomed Out",
        ["멀면 숨기기"] = "Hide When Far",
        ["주사위 화면 고정"] = "Pin Dice to Screen",
        ["줌·자유 시점에서도 굴린 주사위를\n기본 시점과 같은 위치·크기로 보여줍니다."] = "Shows thrown dice at the default\nview's position and size while zoomed.",
        ["게임 기본처럼 필드 위에 두어\n줌에 따라 크기가 바뀝니다."] = "Leaves dice on the field as in the\nbase game, so zoom changes their size.",
        ["반격 아이콘 소리"] = "Counter Icon Sound",
        ["반격 아이콘 애니메이션"] = "Counter Icon Animation",
        ["Alt+Tab 핑 모드 해제"] = "Clear Ping Mode on Alt+Tab",
        ["최종 피해"] = "Final Damage",
        ["시작 공지 끄기"] = "Skip Startup Notice",
        ["자동 업데이트 확인"] = "Automatic Update Checks",
        ["업데이트 알림 팝업"] = "Update Notification Popups",
        ["게임 기본 감사합니다 추천을\n추천마다 한 번 보냅니다."] = "Sends each native Thanks\nsuggestion once.",
        ["게임 내부 분류로\n캐릭터 역할을 보여줍니다."] = "Shows character roles from\nthe game's own classification.",
        ["준비 화면 초상화에 마우스를 올리면\n스킬·재사용 대기시간·잠재력이 나옵니다."] = "Hovering a preparation portrait shows\nskills, cooldowns and potentials.",
        ["줌 아웃할수록 플레이어·몬스터\n인디케이터를 반투명하게 합니다."] = "Fades player and monster\nindicators as you zoom out.",
        ["멀리 줌 아웃하면 숨기고\n다시 확대하면 보여줍니다."] = "Hides indicators far out and\nshows them again closer in.",
        ["반격 아이콘이 나타날 때\n알림음을 냅니다."] = "Plays a chime when\na counter icon appears.",
        ["인카운트·전투 반격 아이콘을\n움직여 보여줍니다."] = "Animates encounter and\ncombat counter icons.",
        ["Alt+Tab 후 남은 핑 선택 모드를\n핑을 보내지 않고 끕니다."] = "Ends ping mode left after Alt+Tab\nwithout sending a ping.",
        ["공개된 최종 수치로\n계산한 피해를 보여줍니다."] = "Shows damage calculated\nfrom public final values.",
        ["공지가 자동으로 열리지 않습니다."] = "Login notices do not open automatically.",
        ["따로 누르지 않아도\n새 버전을 찾습니다."] = "Looks for new versions\nwithout pressing Check.",
        ["새 버전과 변경 내역을\n팝업으로 알려줍니다."] = "Shows new versions and\nrelease notes in a pop-up.",
        ["새 버전을 자동으로 받습니다.\n서명이 확인된 버전만 받습니다."] = "Downloads new versions automatically,\nonly when their signature checks out.",
        ["받은 업데이트를 바로 적용하고\n다음 실행부터 새 버전을 사용합니다."] = "Applies downloaded updates and\nruns the new version from the next launch.",
        ["선택한 채널의 검증된 업데이트를 받습니다.\n비공개 베타만 토큰 인증이 필요합니다."] = "Downloads verified updates for the selected channel.\nOnly Private Beta needs a token.",
        ["다운로드 또는 적용 대기를 취소합니다."] = "Cancels a download or a pending apply.",
        ["추천 아이콘을 표시하지 않습니다."] = "Shows no advice icons.",
        ["마우스를 올려도\n수치를 보여주지 않습니다."] = "Shows no numbers on hover.",
        ["주사위 조건을 보여주지 않습니다."] = "Shows no dice targets.",
        ["예상 피해와\n전투불능 확률을 보여줍니다."] = "Shows expected damage\nand KO chances.",
        ["KO 주사위 조건"] = "KO Dice Target",
        ["KO·생존에 필요한 최소 주사위를\n가장 유리한 경우 기준으로 보여줍니다."] = "Shows the lowest dice needed to\nKO or survive, based on the best case.",
        ["클릭해도 열지 않습니다."] = "Clicking opens nothing.",
        ["추가 정보를 보여주지 않습니다."] = "Shows no extra information.",
        ["인디케이터를 보여주지 않습니다."] = "Shows no indicators.",
        ["기록하지 않습니다."] = "Records nothing.",
        ["소리를 그대로 둡니다."] = "Leaves sound unchanged.",
        ["알리지 않습니다."] = "Sends no alert.",
        ["내 입력을 기다리면\nWindows 알림을 보냅니다."] = "Sends a Windows notification\nwhen your input is needed.",
        ["내 입력을 기다리면\n작업 표시줄 아이콘을 깜빡입니다."] = "Flashes the taskbar icon\nwhen your input is needed.",
        ["모드 설정"] = "Mod Settings",
        ["피해량"] = "Damage",
        ["BetterAstralParty · 모드 설정 (F8)"] = "BetterAstralParty · Mod Settings (F8)",
        ["✓ 저장됨"] = "Saved",
        ["돌아가기"] = "Back",
        ["표시 크기"] = "UI Scale",
        ["배경 불투명도"] = "Panel Opacity",
        ["본명"] = "Name",
        ["이명"] = "Title",
        ["모드 기능"] = "Mod Features",
        ["일반 설정"] = "General",
        ["언어"] = "Language",
        ["자동"] = "Auto",
        ["전투 추천"] = "Battle Advice",
        ["행동 추천"] = "Action Advice",
        ["전투 표시"] = "Battle Display",
        ["필드·카메라"] = "Field / Camera",
        ["카드"] = "Cards",
        ["손패·카드"] = "Hand and Cards",
        ["자동 업데이트"] = "Automatic Updates",
        ["반투명 정도"] = "Fade Strength",
        ["줌 감도"] = "Zoom Sensitivity",
        ["숨김 거리"] = "Hide Distance",
        ["휠 한 칸에 확대·축소되는 양입니다.\n100%가 기본입니다."] = "How far one wheel tick zooms.\n100% is the default.",
        ["인디케이터가 숨는 줌 아웃 거리입니다.\n값이 작을수록 더 일찍 숨습니다."] = "Zoom-out distance at which indicators hide.\nLower values hide them sooner.",
        ["접힌 카드 불투명도"] = "Folded Card Opacity",
        ["불투명도"] = "Opacity",
        ["줌 아웃했을 때 인디케이터가 흐려지는 정도입니다.\n0%는 흐려지지 않고 100%가 기본입니다."] = "How much indicators fade when zoomed out.\n0% keeps them opaque; 100% is the default.",
        ["묶여 접힌 카드의 불투명도입니다.\n100%는 원래대로, 0%는 보이지 않습니다."] = "Opacity of folded grouped cards.\n100% is unchanged; 0% hides them.",
        ["설정 보관"] = "Settings Backup",
        ["모든 설정 초기화"] = "Reset All Settings",
        ["모든 설정을 처음 설치한 상태로 되돌립니다.\n한 번 더 눌러야 적용됩니다."] = "Returns every setting to its first-install value.\nPress twice to apply.",
        ["자동 보관"] = "Keep Automatically",
        ["지금 보관"] = "Save Now",
        ["보관본 불러오기"] = "Load Saved Settings",
        ["보관 폴더 열기"] = "Open Backup Folder",
        ["한 번 더 누르세요"] = "Press Again",
        ["먼저 지금 보관을 누르세요"] = "Use Save Now First",
        ["설정을 자동으로 보관하지 않습니다."] = "Does not keep settings automatically.",
        ["설정을 바꿀 때마다 보관합니다.\n게임 쪽 설정 파일이 없을 때만 되살립니다."] = "Keeps a copy whenever settings change.\nRestores it only when the game has no settings file.",
        ["지금 설정을 보관본으로 남깁니다.\n자동 보관은 이 보관본을 덮어쓰지 않습니다."] = "Saves the current settings as a backup.\nAutomatic keeping never overwrites it.",
        ["남겨 둔 보관본으로 지금 설정을 바꿉니다.\n한 번 더 눌러야 적용됩니다."] = "Replaces the current settings with the saved backup.\nPress twice to apply.",
        ["보관 파일이 있는 폴더를 엽니다.\n다른 PC로 옮길 때 쓰세요."] = "Opens the folder with the backup files.\nUse it to move them to another PC.",
        ["업데이트 적용"] = "Apply Update",
        ["받아 둔 업데이트를 적용합니다.\n다음 실행부터 새 버전을 사용합니다."] = "Applies the downloaded update.\nThe new version runs from the next launch.",
        ["확인만"] = "Check Only",
        ["자동 받기"] = "Download Automatically",
        ["사용자 지정"] = "Custom",
        ["끄기"] = "Off",
        ["지금 할 수 있는 업데이트 작업을 실행합니다."] = "Runs the update action available now.",
        ["새 버전을 자동으로 확인하거나 받지 않습니다."] = "Does not check for or download new versions automatically.",
        ["새 버전을 주기적으로 확인합니다.\n받기와 적용은 자동으로 하지 않습니다."] = "Checks for new versions regularly.\nDoes not download or apply them automatically.",
        ["새 버전을 확인하고 자동으로 받습니다.\n적용은 업데이트 버튼으로 합니다."] = "Checks for and downloads new versions.\nApply them with the Update button.",
        ["새 버전을 확인하고 받아서 바로 적용합니다.\n다음 실행부터 새 버전을 사용합니다."] = "Checks, downloads and applies new versions.\nThe new version runs from the next launch.",
        ["단계에 없는 조합으로 저장되어 있습니다.\n단계를 고르면 그 단계로 바뀝니다."] = "Saved as a combination outside these steps.\nChoosing a step switches to it.",
        ["전투·인카운트 선택 버튼"] = "Combat and encounter choice buttons",
        ["추천 아이콘에 마우스"] = "Hovering an advice icon",
        ["전투 화면 가운데"] = "Center of the combat screen",
        ["전투 결과의 HP 옆"] = "Next to HP in combat results",
        ["전투 HP 아래"] = "Below combat HP",
        ["전투 캐릭터"] = "Combat characters",
        ["내 방어·회피 선택"] = "Your Defend/Dodge choice",
        ["인카운트·전투 반격 아이콘"] = "Encounter and combat counter icons",
        ["필드 캐릭터 위"] = "Above field characters",
        ["필드"] = "Field",
        ["필드 인디케이터"] = "Field indicators",
        ["필드 주사위"] = "Field dice",
        ["내 필드 손패"] = "Your field hand",
        ["카드·칩 핑 말풍선"] = "Card and chip ping bubbles",
        ["캐릭터 이름이 나오는 곳"] = "Wherever character names appear",
        ["준비 화면 초상화"] = "Preparation portraits",
        ["준비 화면 초상화에 마우스"] = "Hovering a preparation portrait",
        ["PvE 퀵 채팅"] = "PvE quick chat",
        ["핑 선택 모드"] = "Ping selection mode",
        ["모드 글자 전체"] = "All mod text",
        ["모드 전투 패널"] = "Mod combat panels",
        ["다른 창을 쓰는 동안"] = "While another window is active",
        ["게임 창이 뒤에 있을 때"] = "While the game is in the background",
        ["PvE 매칭·방 시작"] = "PvE match or room start",
        ["로그인 직후"] = "Right after login",
        ["모드 업데이트"] = "Mod updates",
        ["내 PC"] = "This PC",
        ["캐릭터"] = "Characters",
        ["편의 기능"] = "Convenience",
        ["화면·언어"] = "Display / Language",
        ["알림·소리"] = "Alerts / Audio",
        ["업데이트"] = "Updates",
        ["비공개 베타 인증"] = "Private Beta Access",
        ["진단"] = "Diagnostics",
        ["추천 상세 수치"] = "Advice Details",
        ["핑 상세 보기"] = "Ping Details",
        ["손패 묶기"] = "Group Hand",
        ["기본"] = "Native",
        ["호버 펼치기"] = "Hover",
        ["클릭 펼치기"] = "Click",
        ["손패를 기능별로 모아\n클릭이나 호버로 펼칩니다."] = "Groups cards by function\nfor click or hover expansion.",
        ["전투 상태"] = "Combat Status",
        ["플레이어 인디케이터"] = "Player Indicators",
        ["휠 줌·자유 시점"] = "Wheel Zoom and Free View",
        ["인디케이터 높이"] = "Indicator Height",
        ["위치 조정"] = "Position",
        ["점을 캐릭터 발밑(0%)부터\n원래 위치(100%) 사이로 옮깁니다."] = "Moves the marker between the\ncharacter's base (0%) and\nits original height (100%).",
        ["매칭 시 게임 창 띄우기"] = "Bring Game Forward on Match",
        ["휠 줌과 자유 시점을 쓰지 않습니다."] = "Uses no wheel zoom or free view.",
        ["창을 가져오지 않습니다."] = "Leaves the window where it is.",
        ["휠로 확대·축소하고 드래그로 시점을 옮깁니다.\n턴 바의 '자유 시점 카메라 종료'로\n기본 시점에 돌아갑니다."] = "Zoom with the wheel and drag to move.\nExit Free Camera on the turn bar\nreturns to the default view.",
        ["자유 시점 카메라 종료"] = "Exit Free Camera",
        ["PvE 매칭 성사나 방 시작 때\n게임 창을 한 번 앞으로 가져옵니다."] = "Brings the game forward once\nwhen a PvE match or room starts.",
        ["이름 표기"] = "Name Style",
        ["진단 로그"] = "Diagnostic Log",
        ["진단 폴더 열기"] = "Open Diagnostics Folder",
        ["진단 ZIP 만들기"] = "Create Diagnostics ZIP",
        ["진단 ZIP 만드는 중"] = "Creating ZIP",
        ["진단 ZIP 생성됨"] = "ZIP Created",
        ["진단 ZIP 생성 실패"] = "ZIP Failed",
        ["진단 폴더 열기 실패"] = "Could Not Open Folder",
        ["진단 ZIP이 저장된 폴더를 엽니다."] = "Opens the folder containing diagnostics ZIPs.",
        ["진단 로그를 ZIP으로 모읍니다.\n공유 전 개인정보를 확인하세요."] = "Collects diagnostic logs into a ZIP.\nReview personal information before sharing.",
        ["백그라운드 음소거"] = "Mute in Background",
        ["입력 대기 알림"] = "Input Alert",
        ["작업표시줄"] = "Taskbar",
        ["Windows 알림"] = "Windows",
        ["게임에서 입력을 기다리고 있습니다."] = "The game is waiting for your input.",
        ["게임 창이 비활성 상태일 때\n내 입력 대기를 알립니다."] = "Alerts when the unfocused game\nis waiting for your input.",
        ["기능 안내"] = "Help",
        ["공격·놓아주기, 방어·회피와\n쓸 만한 카드를 추천합니다."] = "Recommends Attack/Let Go,\nDefend/Dodge and useful cards.",
        ["추천에 마우스를 올리면\n피해·전투불능 확률을 봅니다."] = "Hover over advice to see\ndamage and KO chances.",
        ["카드·칩 핑 말풍선을 클릭하면\n원본 이미지와 설명이 열립니다."] = "Clicking a card or chip ping\nopens its image and description.",
        ["양쪽의 손패 장수·공개 효과와\n반격 가능 여부를 보여줍니다.\n아이콘에 마우스를 올리면 설명이 나옵니다."] = "Shows both sides' hand counts,\npublic effects and counter status.\nHover an icon for its description.",
        ["슈슈 쉴드 효과"] = "Shushu Shield Effect",
        ["플레이어 이름·HP와 공개 효과를 보여줍니다.\n효과 아이콘을 클릭하면 설명이 열립니다."] = "Shows player names, HP and public effects.\nClick an effect icon for details.",
        ["본명 또는 이명으로 표시합니다."] = "Displays character names or titles.",
        ["공개 전투 계산까지 자세히 기록합니다.\n내 PC에만 저장됩니다."] = "Records details down to public\ncombat calculations, on this PC only.",
        ["게임 소리를 끄고\n돌아오면 다시 켭니다."] = "Mutes the game and\nrestores sound when you return.",
        ["75%부터 150%까지 조절합니다."] = "Adjusts from 75% to 150%.",
        ["0%에서도 글자·아이콘·테두리는\n그대로 보입니다."] = "Text, icons and borders\nstay visible even at 0%.",
        ["설정을 닫고 돌아갑니다.\n변경 사항은 자동 저장됩니다."] = "Closes settings.\nChanges are saved automatically.",
        ["설정 버튼에 마우스를 올리면\n해당 기능을 안내합니다."] = "Hover over a setting\nfor a short description.",
        ["모드 안내 언어를 선택합니다.\n자동은 게임 메뉴 언어를 따릅니다."] = "Selects the language for mod text.\nAuto follows the game menu language.",
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

    internal static string AutomaticStatus(AutomaticUpdateStatus status, bool transitionWaiting) => transitionWaiting && status == AutomaticUpdateStatus.TransitionRequired
        ? Korean ? "채널 전환 대기" : "Channel Switch Waiting" : AutomaticStatus(status);
    internal static string AutomaticStatus(AutomaticUpdateStatus status) => Korean ? status switch
    {
        AutomaticUpdateStatus.NotConfigured => "업데이트 미설정", AutomaticUpdateStatus.Idle => "대기",
        AutomaticUpdateStatus.MissingAssets => "수동 다운로드 필요", AutomaticUpdateStatus.Downloading => "다운로드 중",
        AutomaticUpdateStatus.Preparing => "검증 중", AutomaticUpdateStatus.Ready => "준비됨", AutomaticUpdateStatus.Queued => "적용 중",
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
        AutomaticUpdateStatus.Preparing => "Verifying", AutomaticUpdateStatus.Ready => "Ready", AutomaticUpdateStatus.Queued => "Applying",
        AutomaticUpdateStatus.Cancelling => "Recording Cancellation", AutomaticUpdateStatus.CancelFailed => "Cancellation Record Failed",
        AutomaticUpdateStatus.CheckingInstallation => "Checking Installation", AutomaticUpdateStatus.RecoveryRequired => "Recovery Required",
        AutomaticUpdateStatus.FilesBusy => "Update Files In Use", AutomaticUpdateStatus.AlreadyClaimed => "Request Already Claimed",
        AutomaticUpdateStatus.Cancelled => "Cancelled", AutomaticUpdateStatus.ManualUpgradeRequired => "Manual Upgrade Required",
        AutomaticUpdateStatus.RetryWaiting => "Retry Waiting", AutomaticUpdateStatus.FaultDisabled => "Disabled After Failure", AutomaticUpdateStatus.FaultStorageFailed => "Failure/Settings Storage Needs Attention", AutomaticUpdateStatus.Reactivating => "Re-enabling Updates",
        AutomaticUpdateStatus.TransitionRequired => "Confirm Channel Transition", AutomaticUpdateStatus.Committed => "Applied", _ => "Update Failed"
    };
    internal static string AutomaticDetails(AutomaticUpdateStatus status, string? recoveryStage = null, Updating.UpdateFault? fault = null, ReleaseChannel? channel = null, bool transitionWaiting = false) => AutomaticStatus(status, transitionWaiting) + "\n" + (transitionWaiting && status == AutomaticUpdateStatus.TransitionRequired
        ? Korean ? "선택한 채널에는 설치된 버전보다 새 버전이 아직 없습니다. 새 버전이 나오면 이곳에서 전환을 확인할 수 있습니다." : "The selected channel has no version newer than the installed one yet. When one is released, you can confirm the switch here."
        : status == AutomaticUpdateStatus.NotConfigured
        ? channel is ReleaseChannel.Stable or ReleaseChannel.PreRelease
            ? Korean ? "업데이트 설정과 설치 상태를 확인하세요. 공개 채널은 토큰이 필요 없습니다." : "Check update settings and installation. Public channels need no token."
            : Korean ? "업데이트 설정과 설치 상태를 확인하세요. 비공개 베타는 토큰 인증이 필요합니다." : "Check update settings and installation. Private Beta requires a token."
        : status == AutomaticUpdateStatus.TransitionRequired ? Korean ? "대상 채널과 버전을 확인한 뒤 전환을 승인하세요. 업데이트 채널을 설치된 채널로 되돌리면 그 채널의 업데이트를 계속 받습니다." : "Review the target channel and version, then confirm the switch. Select the installed channel again to keep receiving its updates."
        : status == AutomaticUpdateStatus.RecoveryRequired ? Korean ? "미완료 업데이트를 확인했습니다. 게임을 다시 시작하면 자동으로 복구합니다. 계속 이 상태이면 게임을 닫고 Install.cmd를 다시 실행하세요. 백업은 보존됩니다." : "An unfinished update needs recovery. Restarting the game recovers it automatically. If this stays, close the game and run Install.cmd again. Backups are preserved."
        : status == AutomaticUpdateStatus.FaultStorageFailed ? Korean ? "실패 기록이나 설정을 저장하지 못했습니다. 자동 실행을 차단했습니다. 저장 위치와 복구 상태를 확인한 뒤 상태를 다시 확인하세요." : "Failure evidence or settings could not be saved. Automatic execution is blocked. Check storage and recovery, then recheck installation."
        : status == AutomaticUpdateStatus.CancelFailed ? Korean ? "취소 완료를 확인하지 못했습니다. 업데이트를 다시 실행하지 말고 복구 안내를 확인하세요." : "Cancellation completion could not be confirmed. Check recovery guidance before retrying."
        : status == AutomaticUpdateStatus.Cancelling ? Korean ? "취소를 기록하고 있습니다." : "Recording the cancellation."
        : status == AutomaticUpdateStatus.ManualUpgradeRequired ? Korean ? "설치를 자동으로 변경할 수 없습니다. 수동 업그레이드가 필요합니다." : "This installation requires a manual upgrade."
        : Korean ? "다운로드·적용 상태를 표시합니다." : "Shows download and apply status.")
        + (fault == null ? "" : "\n" + (Korean ? "자동 다운로드와 자동 적용을 OFF로 저장합니다. 원인을 해결하고 ‘다시 사용’을 선택하면 두 옵션을 ON으로 복원합니다.\n실패 단계/원인: " : "Both options are saved OFF. Resolve the cause, then select Re-enable Updates to restore both ON.\nFailure phase/reason: ") + FaultPhaseText(fault.Phase) + "/" + FaultReasonText(fault.Reason)
            + (fault.PreferencesSaveFailed ? Korean ? "\n설정 저장에 실패했습니다. 실패 표지가 자동 실행을 계속 차단합니다." : "\nSettings could not be saved. Persistent failure evidence still blocks automatic execution." : ""))
        + (status == AutomaticUpdateStatus.RetryWaiting ? Korean ? "\n최대 두 번 시도합니다. 대기 중에는 네트워크 요청을 보내지 않습니다." : "\nAt most two attempts. No network requests while waiting." : "")
        + (status == AutomaticUpdateStatus.Committed ? Korean ? "\n게임을 다시 시작하면 새 버전을 사용합니다." : "\nRestart the game to use the new version." : "");

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
                Korean ? "준비가 끝나면 자동 적용 설정에 따라 바로 적용합니다." : "When ready, it applies right away if automatic apply is on.",
            AutomaticUpdateStatus.TransitionRequired => Korean ? "채널 전환은 업데이트 설정에서 별도로 확인하고 승인해야 합니다." : "Review and confirm a channel transition separately in Update Settings.",
            AutomaticUpdateStatus.Ready => Korean ? "다운로드가 준비되었습니다. 업데이트 설정을 확인하세요." : "The download is ready. Check Update Settings.",
            AutomaticUpdateStatus.Queued => Korean ? "업데이트를 적용하고 있습니다. 다음 실행부터 새 버전을 사용합니다." : "Applying the update. The new version runs from the next launch.",
            AutomaticUpdateStatus.Cancelled or AutomaticUpdateStatus.Cancelling => Korean ? "취소 상태는 업데이트 설정에서 확인할 수 있습니다." : "Check the cancellation status in Update Settings.",
            AutomaticUpdateStatus.FaultDisabled or AutomaticUpdateStatus.FaultStorageFailed or AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.CancelFailed or AutomaticUpdateStatus.Failed =>
                Korean ? "업데이트 설정에서 실패 또는 복구 상태를 확인하세요." : "Check Update Settings for failure or recovery status.",
            _ => Korean ? "업데이트 설정에서 자세한 내용을 확인하세요." : "Open Update Settings for details."
        };
        // Display release text literally, never through translation or markup parsing.
        return Text("현재 버전") + ": " + current + "\n" + Text("새 버전") + ": " + release.Version
            + "\n\n" + Text("현재 업데이트 상태") + ": " + status
            + (check == ReleaseUpdateStatus.Available ? "" : "\n" + UpdateStatus(check)) + "\n\n" + guidance
            + "\n\n" + Text("변경 사항") + ":\n"
            + (release.Notes.Length == 0 ? Text("변경 사항 없음") : ReleaseUpdates.PlainText(release.Notes, ReleaseUpdates.MaxNotes));
    }

    // A lower or equal release on another channel is a switch target, not a new version.
    internal static string CheckStatus(ReleaseUpdateResult result) => result.Status == ReleaseUpdateStatus.Available && result.Release is { TransitionCandidate: true } target
        ? (Korean ? "전환 대상 " : "Switch target ") + target.Version : UpdateStatus(result.Status);
    // The player's own clock, minutes only: checks run hours apart.
    internal static string LocalTime(DateTimeOffset time) => time.ToLocalTime().ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    internal static string UpdateDetails(ReleaseUpdateResult result, string current)
    {
        var body = Text("현재 버전") + ": " + current + "\n" + CheckStatus(result);
        if (result.Failure is { } failure) body += " · " + UpdateStatus(failure);
        if (result.CheckedAt is { } time) body += "\n" + Text("마지막 확인") + ": " + LocalTime(time);
        if (result.RetryAt is { } retryAt) body += "\n" + Text("다음 확인") + ": " + LocalTime(retryAt);
        if (result.Release is not { } release) return body;
        body += "\n" + Text("새 버전") + ": " + release.Version;
        return body;
    }
}
