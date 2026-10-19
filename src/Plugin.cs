using BetterAstralParty.Observability;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace BetterAstralParty;

[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "kr.betterastralparty.mod";
    public const string Name = "BetterAstralParty";
    public const string Version = "1.4.1-rc.1.local.27";
    public const int InstallBundleProtocol = 1;
    public const string UpdateProtocol = "5";
    public const string SettingsSchema = "1";

    internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
    internal static ConfigEntry<bool> ShowDetails { get; private set; } = null!;
    internal static ConfigEntry<bool> KoMinimum { get; private set; } = null!;
    internal static ConfigEntry<bool> UseRealNames { get; private set; } = null!;
    internal static ConfigEntry<bool> CardPopups { get; private set; } = null!;
    internal static ConfigEntry<bool> GroupHand { get; private set; } = null!;
    internal static ConfigEntry<string> HandExpandMode { get; private set; } = null!;
    internal static ConfigEntry<bool> BattleStatus { get; private set; } = null!;
    internal static ConfigEntry<bool> ShushuShield { get; private set; } = null!;
    internal static ConfigEntry<bool> ShushuShieldRestrictions { get; private set; } = null!;
    internal static ConfigEntry<bool> AutoThanks { get; private set; } = null!;
    internal static ConfigEntry<bool> CharacterGuide { get; private set; } = null!;
    internal static ConfigEntry<bool> CharacterRoles { get; private set; } = null!;
    internal static ConfigEntry<bool> IndicatorFade { get; private set; } = null!;
    internal static ConfigEntry<bool> IndicatorHide { get; private set; } = null!;
    internal static ConfigEntry<bool> CounterSound { get; private set; } = null!;
    internal static ConfigEntry<bool> CounterAnimation { get; private set; } = null!;
    internal static ConfigEntry<bool> SuppressNotices { get; private set; } = null!;
    internal static ConfigEntry<bool> PingFocus { get; private set; } = null!;
    internal static ConfigEntry<bool> UpdateNotifications { get; private set; } = null!;
    internal static ConfigEntry<bool> AutomaticChecks { get; private set; } = null!;
    internal static ConfigEntry<bool> FinalDamage { get; private set; } = null!;
    internal static ConfigEntry<bool> FieldBuffs { get; private set; } = null!;
    internal static ConfigEntry<bool> FieldZoom { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticLogging { get; private set; } = null!;
    internal static ConfigEntry<bool> MuteUnfocused { get; private set; } = null!;
    internal static ConfigEntry<bool> SettingsAutoKeep { get; private set; } = null!;
    internal static ConfigEntry<float> IndicatorFadeStrength { get; private set; } = null!;
    internal static ConfigEntry<float> FoldedHandOpacity { get; private set; } = null!;
    internal static ConfigEntry<float> ZoomSensitivity { get; private set; } = null!;
    internal static ConfigEntry<float> IndicatorHideDistance { get; private set; } = null!;
    internal static BepInEx.Configuration.ConfigFile Settings { get; private set; } = null!;

    // Every setting back to its first-install value. A few defaults were derived at bind time from older
    // settings for upgrades; recompute those from the reset values, as a fresh installation would.
    internal static void ResetAllSettings()
    {
        foreach (var entry in Settings.Values) entry.BoxedValue = entry.DefaultValue;
        FinalDamage.Value = Enabled.Value;
        KoMinimum.Value = Enabled.Value && ShowDetails.Value;
        ShushuShield.Value = BattleStatus.Value;
        ShushuShieldRestrictions.Value = ShushuShield.Value;
    }
    internal static ConfigEntry<bool> MatchFocus { get; private set; } = null!;
    internal static ConfigEntry<string> InputAttention { get; private set; } = null!;
    internal static DiagnosticLog Diagnostics { get; private set; } = null!;
    internal static ConfigEntry<float> UiScale { get; private set; } = null!;
    internal static ConfigEntry<float> Opacity { get; private set; } = null!;
    internal static ConfigEntry<float> FieldIndicatorHeight { get; private set; } = null!;
    internal static ConfigEntry<string> Language { get; private set; } = null!;
    internal static ConfigEntry<string> UpdateChannel { get; private set; } = null!;
    internal static ConfigEntry<bool> AutoDownload { get; private set; } = null!;
    internal static ConfigEntry<bool> ApplyAfterExit { get; private set; } = null!;
    private static UpdatePreferencesBridge? _updatePreferences;
    internal static void PollUpdatePreferences() => _updatePreferences?.Poll(Automatic);
    private static bool _channelAdoptionChecked;
    // Once per session after installation inspection: a switch confirmed in the emergency window becomes the saved channel.
    internal static void AdoptConfirmedChannel()
    {
        var snapshot = Automatic.Snapshot;
        if (_channelAdoptionChecked || Automatic.Status == AutomaticUpdateStatus.CheckingInstallation || snapshot.InstalledChannel.Length == 0 || snapshot.InstalledVersion.Length == 0) return;
        _channelAdoptionChecked = true;
        try {
            using var fence = new Updating.WindowsFileFence(System.IO.Path.GetDirectoryName(Paths.BepInExRootPath)!);
            if (!Updating.ChannelTransitionIntent.ReadAdoption(fence, out var channel, out var version)) return;
            var applied = channel.Length != 0 && channel == snapshot.InstalledChannel && version == snapshot.InstalledVersion;
            // Unresolved update work may still apply the switch; keep the marker until it settles.
            if (!applied && Automatic.Status is AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.FaultDisabled or AutomaticUpdateStatus.FaultStorageFailed) return;
            if (applied && UpdateChannel.Value != channel) UpdateChannel.Value = channel;
            Updating.ChannelTransitionIntent.RemoveAdoption(fence);
            Logger.LogInfo(applied ? "[Update channel] Saved the channel confirmed in the emergency update window: " + channel : "[Update channel] Discarded an emergency channel choice that was not applied.");
        } catch (Exception ex) { Logger.LogWarning("[Update channel] Emergency channel choice could not be applied: " + ex.GetType().Name); }
    }
    internal static AutomaticUpdates Automatic { get; private set; } = new(Updating.UpdateDownloads.Production());
    internal static ReleaseUpdates Updates { get; } = ReleaseUpdates.Production(Version, ReleaseChannel.Stable);
    internal static PrivateReleaseSession? UpdateSession { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Logger { get; private set; } = null!;
    internal static CatalogGuard? Catalogs { get; private set; }
    private static bool _catalogWarningShown;

    internal static MinimalEventLog? MinimalDiagnostics { get; private set; }
    private DiagnosticPhase _diagnosticLoadPhase;

    public override void Load()
    {
        // Steam forwards arguments, not the launcher's environment; both are process-local.
        // Mod.cmd normally asks Steam to start the owned app without arguments and leaves a short-lived request instead.
        var launchRequested = StartupGuard.ConsumeLaunchRequest();
        if (!launchRequested && Environment.GetEnvironmentVariable("BAP_COMPATIBILITY_CHECKED") != "1"
            && !Environment.GetCommandLineArgs().Contains("--bap-compatibility-checked", StringComparer.Ordinal))
        {
            StartupGuard.RecordLaunchDecision(false, "none");
            Log.LogInfo("BAP OFF for this launch (" + StartupGuard.LaunchRequestReason + "); shared loader and other mods are unchanged.");
            return;
        }
        var route = launchRequested ? "mod-request"
            : Environment.GetCommandLineArgs().Contains("--bap-compatibility-checked", StringComparer.Ordinal) ? "arguments" : "environment";
        StartupGuard.RecordLaunchDecision(true, route);
        Log.LogInfo("BAP ON for this launch (" + route + ").");
        StartupGuard.Begin(Version, Path.GetDirectoryName(Paths.BepInExRootPath)!);
        try
        {
            MinimalDiagnostics = DiagnosticOwner.Start(
                Path.GetDirectoryName(Paths.BepInExRootPath)!, false,
                Version, typeof(Plugin).Module.ModuleVersionId.ToString("N"), () => Diagnostics?.Flush());
            if (!DiagnosticHub.Bind(MinimalDiagnostics)) { MinimalDiagnostics.Dispose(); MinimalDiagnostics = null; }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => MinimalDiagnostics?.Stop(DiagnosticEndReason.ProcessExitCallback);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                DiagnosticHub.Failure(DiagnosticFeature.Loader, DiagnosticPhase.None, args.IsTerminating ? DiagnosticCode.UnhandledTerminating : DiagnosticCode.UnhandledNonTerminating, args.ExceptionObject as Exception);
                DiagnosticHub.FlushSoon();
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
                DiagnosticHub.Failure(DiagnosticFeature.Loader, DiagnosticPhase.None, DiagnosticCode.UnobservedManaged, args.Exception);
        }
        catch { Log.LogWarning("Minimal local diagnostic setup unavailable."); }
        try { LoadCore(); }
        catch (Exception ex)
        {
            DiagnosticHub.Failure(DiagnosticFeature.Loader, _diagnosticLoadPhase, DiagnosticCode.Unknown, ex);
            DiagnosticHub.FlushSoon();
            throw; // Preserve the loader's existing failure handling.
        }
    }

    private void LoadStage(DiagnosticPhase phase)
    {
        _diagnosticLoadPhase = phase;
        DiagnosticHub.Stage(DiagnosticFeature.Loader, phase, DiagnosticOutcome.Begin);
    }

    private void LoadCore()
    {
        VisibleCombatAdvisor.PveClassifier = RuntimeObject.IsPve;
        LoadStage(DiagnosticPhase.BootstrapGate);
        Logger = Log;
        if (!UpdateStartup.Recover(Path.GetDirectoryName(Paths.BepInExRootPath)!, Version, Updating.UpdateTrust.Production())) { StartupGuard.Stage("BootstrapGate", "Failed"); return; }
        DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.BootstrapGate, DiagnosticOutcome.Completed);
        StartupGuard.Stage("BootstrapGate");
        LoadStage(DiagnosticPhase.Configuration);
        Settings = Config;
        try
        {
            // Only a missing game-side file is restored, so existing settings are never overwritten.
            if (SettingsBackup.RestoreIfMissing(Config.ConfigFilePath, SettingsBackup.Folder))
            {
                Config.Reload();
                Log.LogInfo("[설정 보관] 게임 쪽 설정 파일이 없어 보관본을 복원함");
            }
        }
        catch (Exception ex) { Log.LogWarning("[설정 보관] 복원 실패: " + ex.GetType().Name); }
        Enabled = Config.Bind("일반", "활성화", true, "[설정 창: 행동 추천 / Settings: Action Advice] PvE 공격·놓아주기, 방어·회피와 카드 사용을 추천합니다. / PvE advice for attacking, letting go, defending, dodging and playing cards.");
        FinalDamage = Config.Bind("화면", "최종 피해 표시", Enabled.Value, "[설정 창: 최종 피해 / Settings: Final Damage] 전투의 최종 수치로 계산한 피해를 표시합니다. / Shows damage calculated from final combat values.");
        ShowDetails = Config.Bind("전투 추천", "상세 확률 표시", true, "[설정 창: 추천 상세 수치 / Settings: Advice Details] 추천 호버에 피해·전투불능 확률을 표시합니다. / Shows damage and KO chances when hovering over advice.");
        // Preserve the previously effective indicator state on the first upgrade only.
        KoMinimum = Config.Bind("화면", "KO 최소 조건 표시", Enabled.Value && ShowDetails.Value,
            "[설정 창: KO 주사위 조건 / Settings: KO Dice Target] 가장 유리한 경우의 KO·생존 최소 주사위를 표시합니다. / Shows minimum KO/survival dice in the best-case scenario.");
        UseRealNames = Config.Bind("화면", "캐릭터 본명 표시", true, "[설정 창: 이름 표기 / Settings: Name Style] 캐릭터를 본명으로 표시합니다. OFF는 이명입니다. / Shows characters' real names; OFF shows titles.");
        CardPopups = Config.Bind("화면", "카드 확대 팝업", true, "[설정 창: 핑 상세 보기 / Settings: Ping Details] 카드·칩 핑을 클릭하면 이미지와 설명을 엽니다. / Click a card or chip ping to open its image and details.");
        GroupHand = Config.Bind("화면", "손패 기능별 정렬", false, "[설정 창: 손패 묶기 / Settings: Group Hand] 필드 손패를 기능별로 모읍니다. OFF는 기본 배치입니다. / Groups field cards by function; OFF uses the native layout.");
        HandExpandMode = Config.Bind("화면", "손패 펼치기 방식", "Hover", "Hover는 세로로, Click은 가로로 펼칩니다. / Hover expands vertically; Click expands horizontally.");
        BattleStatus = Config.Bind("화면", "전투 공개 상태", true, "[설정 창: 전투 상태 / Settings: Combat Status] PvE HP 아래 공개 손패 장수·효과와 플레이어·몬스터의 반격 가능 여부를 표시합니다. / Public hand counts, effects and player/monster counter indicators in PvE.");
        ShushuShield = Config.Bind("화면", "슈슈 쉴드 표시", BattleStatus.Value, "[설정 창: 슈슈 쉴드 효과 / Settings: Shushu Shield Effect] 전투 중 캐릭터에 슈슈 쉴드 효과를 표시합니다. / Shows Shushu shield effects on combat actors.");
        ShushuShieldRestrictions = Config.Bind("화면", "슈슈 쉴드 방어 제한", ShushuShield.Value, "[설정 창: 슈슈 쉴드 방어 잠금 / Settings: Shushu Shield Defend Lock] 쉴드가 있는 내가 방어자이면 방어 버튼·방어 카드를 잠급니다. / Locks Defend and defense cards when you are the shielded defender.");
        AutoThanks = Config.Bind("편의", "자동 감사합니다", true, "[설정 창: 감사합니다 자동 전송 / Settings: Auto Thanks] 게임 기본 감사합니다 추천을 추천마다 한 번 전송합니다. / Confirm each native Thanks recommendation at most once.");
        CharacterGuide = Config.Bind("화면", "캐릭터 준비 설명", true, "[설정 창: 준비 화면 스킬 설명 / Settings: Skill Info in Preparation] 준비 화면의 초상화 호버에 공개 스킬·잠재력 설명을 표시합니다. / Public character skill and potential descriptions on preparation portraits.");
        CharacterRoles = Config.Bind("화면", "캐릭터 역할 표시", true, "[설정 창: 역할 표시 / Settings: Role Labels] 게임 내부 분류로 캐릭터 역할을 표시합니다. / Shows characters' internal role classifications.");
        IndicatorFade = Config.Bind("화면", "줌 아웃 인디케이터 반투명", true, "[설정 창: 줌 아웃 시 반투명 / Settings: Fade When Zoomed Out] 필드 줌 아웃 시 플레이어·몬스터 인디케이터를 반투명하게 합니다. / Fade field player/monster indicators when zoomed out.");
        IndicatorHide = Config.Bind("화면", "원거리 인디케이터 숨김", true, "[설정 창: 멀면 숨기기 / Settings: Hide When Far] 멀리 줌 아웃하면 필드 인디케이터를 숨기고 확대하면 다시 표시합니다. / Hides far-zoom field indicators and restores them when zooming in.");
        CounterSound = Config.Bind("화면", "반격 표시 소리", true, "[설정 창: 반격 아이콘 소리 / Settings: Counter Icon Sound] 인카운트·전투 반격 아이콘에 알림음을 재생합니다. / Plays sounds for encounter and combat counter icons.");
        CounterAnimation = Config.Bind("화면", "반격 표시 애니메이션", true, "[설정 창: 반격 아이콘 애니메이션 / Settings: Counter Icon Animation] 반격 아이콘을 애니메이션으로 표시합니다. OFF는 정적인 아이콘입니다. / Animates counter icons; OFF shows static icons.");
        SuppressNotices = Config.Bind("편의", "시작 공지 자동 열기 방지", true, "[설정 창: 시작 공지 끄기 / Settings: Skip Startup Notice] 로그인 공지가 자동으로 열리지 않게 합니다. / Prevents login notices from opening automatically.");
        PingFocus = Config.Bind("편의", "창 전환 후 핑 모드 해제", true, "[설정 창: Alt+Tab 핑 모드 해제 / Settings: Clear Ping Mode on Alt+Tab] Alt+Tab 후 남은 핑 선택 모드를 전송 없이 해제합니다. / Cancel lingering ping mode after task switching without sending a ping.");
        UpdateNotifications = Config.Bind("업데이트", "업데이트 알림 팝업", true, "새 버전 알림과 변경 내역을 표시합니다. / Shows new-version alerts and release notes.");
        AutomaticChecks = Config.Bind("업데이트", "자동 업데이트 확인", true, "주기적으로 새 버전을 확인합니다. / Periodically checks for new versions.");
        FieldBuffs = Config.Bind("화면", "필드 버프 표시", true, "[설정 창: 플레이어 인디케이터 / Settings: Player Indicators] 필드 플레이어의 이름·HP·공개 효과를 표시합니다. 아이콘을 클릭하면 효과 설명을 엽니다. / Shows field player names, HP and public effects. Click an icon for effect details.");
        FieldZoom = Config.Bind("화면", "필드 휠 줌", true,
            "[설정 창: 휠 줌·자유 시점 / Settings: Wheel Zoom and Free View] 필드 휠 줌과 자유 시점 이동을 사용합니다. ‘자유 시점 카메라 종료’로 기본 시점에 돌아갑니다. / Enables field wheel zoom and free-camera movement. Exit Free Camera returns to the default view.");
        // 1 keeps the native marker height (face level at the native camera distance); 0 is the character's base.
        FieldIndicatorHeight = Config.Bind("화면", "필드 인디케이터 점 높이", 1f, new ConfigDescription("[설정 창: 인디케이터 높이 / Settings: Indicator Height] 필드 플레이어 인디케이터 점의 높이입니다. 100%는 원래 위치입니다. / Height of the field player indicator marker; 100% is the original position.", new AcceptableValueRange<float>(0f, 1f)));
        UiScale = Config.Bind("화면", "UI 배율", 1.0f, new ConfigDescription("추천·전투 상태 표시의 크기를 조절합니다. / Scales advice and battle status panels.", new AcceptableValueRange<float>(0.75f, 1.5f)));
        // New fill-only setting starts at 100%; do not carry over the old whole-panel opacity.
        Opacity = Config.Bind("화면", "배경 불투명도", 1f, new ConfigDescription("전투 패널 바탕의 불투명도를 0~100%로 조절합니다. / Sets battle panel background opacity from 0% to 100%.", new AcceptableValueRange<float>(0f, 1f)));
        Language = Config.Bind("일반", "언어", "Auto", "모드 안내 언어를 선택합니다. Auto는 게임 메뉴 언어를 따릅니다. / Selects the mod text language. Auto follows the game menu language.");
        ModText.Select(Language.Value);
        UpdateChannel = Config.Bind("일반", "업데이트 채널", "Stable",
            "Stable: 릴리즈, PreRelease: 프리 릴리즈, Beta: 비공개 베타. 선택한 채널의 최신 버전을 확인합니다. 비공개 베타는 토큰이 필요합니다. / Checks the latest Release, Pre-release or Private Beta. Private Beta requires a token.");
        AutoDownload = Config.Bind("업데이트", "자동 다운로드", true, "검증한 새 버전을 자동으로 다운로드합니다. / Automatically downloads verified new versions.");
        // Same key keeps each player's saved choice; protocol 5 applies in game and takes effect at the next launch.
        ApplyAfterExit = Config.Bind("업데이트", "종료 후 적용", true, "다운로드한 업데이트를 바로 적용하고 다음 실행부터 사용합니다. / Applies downloaded updates now; they take effect at the next launch.");
        DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.Configuration, DiagnosticOutcome.Completed);
        LoadStage(DiagnosticPhase.UpdateReadiness);
        UpdateSession?.Dispose(); UpdateSession = null;
        Automatic.Dispose();
        var updateRoot = Path.GetDirectoryName(Paths.BepInExRootPath)!;
        _updatePreferences = new UpdatePreferencesBridge(Config, AutoDownload, ApplyAfterExit);
        var updateHandoff = new UpdateHandoff(updateRoot, Updating.UpdateTrust.Production());
        Automatic = new(Updating.UpdateDownloads.Production(), updateHandoff, new FileUpdateSafety(updateRoot, _updatePreferences.Save, readiness: updateHandoff.Initialize));
        Automatic.Preferences(AutoDownload.Value, ApplyAfterExit.Value);
        Automatic.Configure(null);
        UpdateSession = PrivateReleaseSession.Production(Updates, Automatic);
        void SelectUpdateChannel()
        {
            var channel = UpdateChannel.Value switch { "Beta" => ReleaseChannel.Beta, "PreRelease" => ReleaseChannel.PreRelease, _ => ReleaseChannel.Stable };
            Updates.SetChannel(channel); Automatic.SetChannel(channel); Automatic.Context(null, Updates.Generation);
        }
        SelectUpdateChannel();
        UpdateChannel.SettingChanged += (_, _) => SelectUpdateChannel();
        DiagnosticLogging = Config.Bind("진단", "충돌 진단 로그", false,
            "[설정 창: 진단 로그 / Settings: Diagnostic Log] 오류·성능·공개 전투 계산의 상세 로그를 기록합니다. / Records detailed error, performance and public combat calculation logs.");

        DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.UpdateReadiness, DiagnosticOutcome.Completed);
        StartupGuard.Stage("Updates");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StartupGuard.SessionEnd();
        if (StartupGuard.SafeMode)
        {
            // Repeated failed starts: no game hooks or UI, only update check/download/apply.
            Log.LogWarning($"안전 모드: 시작이 {StartupGuard.Failures}번 연속 실패해 모드 기능을 끄고 업데이트만 확인합니다. / Safe mode after {StartupGuard.Failures} failed starts: features off, updates only.");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { UpdateSession?.ProcessExiting(); Updates.Dispose(); };
            SafeModeUpdater.Start();
            return;
        }
        // Keep the saved key so upgrades preserve each user's existing choice.
        IndicatorFadeStrength = Config.Bind("화면", "줌 아웃 반투명 정도", 1f, new ConfigDescription(
            "[설정 창: 반투명 정도 / Settings: Fade Strength] 줌 아웃 시 인디케이터가 흐려지는 정도입니다. 100%가 기본입니다. / How much field indicators fade when zoomed out; 100% is the default.",
            new AcceptableValueRange<float>(0f, 1f)));
        FoldedHandOpacity = Config.Bind("화면", "접힌 손패 불투명도", 0.75f, new ConfigDescription(
            "[설정 창: 접힌 카드 불투명도 / Settings: Folded Card Opacity] 묶여 접힌 카드의 불투명도입니다. / Opacity of folded grouped cards.",
            new AcceptableValueRange<float>(0f, 1f)));
        ZoomSensitivity = Config.Bind("화면", "휠 줌 감도", 1f, new ConfigDescription(
            "[설정 창: 줌 감도 / Settings: Zoom Sensitivity] 휠 한 칸에 확대·축소되는 양입니다. 100%가 기본입니다. / How far one wheel tick zooms; 100% is the default.",
            new AcceptableValueRange<float>(0.5f, 2f)));
        IndicatorHideDistance = Config.Bind("화면", "인디케이터 숨김 거리", 0.6f, new ConfigDescription(
            "[설정 창: 숨김 거리 / Settings: Hide Distance] 멀면 숨기기에서 인디케이터가 숨는 줌 아웃 거리입니다. / Zoom-out distance at which Hide When Far hides indicators.",
            new AcceptableValueRange<float>(0.3f, 0.9f)));
        SettingsAutoKeep = Config.Bind("일반", "설정 자동 보관", true,
            "[설정 창: 자동 보관 / Settings: Keep Automatically] 설정을 바꿀 때마다 사용자 폴더에 보관합니다. / Keeps a copy in the user folder whenever settings change.");
        MuteUnfocused = Config.Bind("환경", "비활성 창 음소거", true,
            "[설정 창: 백그라운드 음소거 / Settings: Mute in Background] 다른 창으로 전환하면 소리를 끄고 게임으로 돌아오면 복원합니다. / Mutes when focus is lost and restores audio on return.");
        MatchFocus = Config.Bind("환경", "매칭 성사 시 창 포커스", true,
            "[설정 창: 매칭 시 게임 창 띄우기 / Settings: Bring Game Forward on Match] PvE 매칭 성사·로비 방 시작 시 게임 창을 앞으로 가져옵니다. / Brings the game forward on a PvE match or when a lobby game starts.");
        InputAttention = Config.Bind("환경", "입력 대기 알림", "Taskbar",
            "게임 창이 비활성 상태일 때 입력 대기를 알립니다. Taskbar: 작업표시줄, Windows: 알림, Off: 끔. / Input-wait alerts while unfocused: Taskbar, Windows or Off.");
        // Same key: migrate 0.18's serialized true/false without losing an OFF preference.
        InputAttention.Value = InputAttentionState.Normalize(InputAttention.Value);
        // Per-user folder: removal or reinstallation of the mod keeps the evidence. Legacy game-folder copies are still collected.
        Diagnostics = new DiagnosticLog(Path.Combine(StartupGuard.Directory, "Logs"),
            message => Log.LogWarning(message));
        var crashScanStarted = false;
        void UpdateDiagnostics()
        {
            Diagnostics.SetEnabled(DiagnosticLogging.Value,
                $"mod={Version}; runtime={Environment.Version}; loader=ON; executable={Path.GetFileName(Environment.ProcessPath)}");
            MinimalDiagnostics?.SetDetailed(DiagnosticLogging.Value);
            RecordSettings();
            if (Diagnostics.IsRecording && !crashScanStarted)
            {
                crashScanStarted = true;
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        Diagnostics.Write("nativeCrash scan begin; historical reports from last 7 days; not a live crash handler");
                        foreach (var line in NativeCrashReport.Read(
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
                            Path.GetDirectoryName(Paths.BepInExRootPath)!, DateTime.UtcNow.AddDays(-7)))
                            Diagnostics.Write(line);
                        Diagnostics.Write("nativeCrash scan complete");
                    }
                    catch (Exception ex) { Diagnostics.Error("nativeCrashScan", ex); }
                });
            }
        }
        Config.SettingChanged += (_, _) => UpdateDiagnostics();
        Config.SettingChanged += (_, _) => SettingsBackup.MarkChanged(Environment.TickCount64);
        UpdateDiagnostics();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Diagnostics.SetEnabled(false, "");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { UpdateSession?.ProcessExiting(); Updates.Dispose(); };
        // The verified engine driver keeps updates alive without touching incompatible game UI.
        LoadStage(DiagnosticPhase.PatchRegistration);
        PatchFeature("CoreUi", typeof(EventSystemUpdatePatch));
        try
        {
            LoadStage(DiagnosticPhase.Catalog);
            var gameRoot = Path.GetDirectoryName(Paths.BepInExRootPath)!;
            Catalogs = new CatalogGuard(Path.Combine(gameRoot, "BetterAstralParty.compatibility.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "feimo", "AstralParty_INT", "com.unity.addressables"));
            Catalogs.Verify(DateTime.UtcNow);
            PollCatalogs(DateTime.UtcNow);
            DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.Catalog, Catalogs == null ? DiagnosticOutcome.Unavailable : Catalogs.Changed ? DiagnosticOutcome.Partial : DiagnosticOutcome.Completed);
        }
        catch (Exception ex)
        {
            Catalogs = null;
            try { DiagnosticHub.Failure(DiagnosticFeature.Loader, DiagnosticPhase.Catalog, DiagnosticCode.Unknown, ex); } catch { }
            try { Diagnostics.Error("catalog", ex); } catch { }
            try { Log.LogWarning($"카탈로그 관찰 불가 · 기능별 검증 유지 / Catalog observation unavailable; feature guards remain active: {ex}"); } catch { }
        }
        LoadStage(DiagnosticPhase.PatchRegistration);
        Diagnostics.Write("patches begin");
        PatchFeature("Notices", typeof(StartupNoticePatch), typeof(LauncherNoticePatch));
        Diagnostics.Write("patches complete");
        DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.PatchRegistration, Compatibility.Allowed("CoreUi") && Compatibility.Allowed("Notices") ? DiagnosticOutcome.Completed : DiagnosticOutcome.Partial);
        StartupGuard.Stage("Patches", Compatibility.Allowed("CoreUi") && Compatibility.Allowed("Notices") ? "Completed" : "Partial");
        Log.LogInfo($"{Name} {Version} 로드됨 — Korean/English, INT/PvE");
    }

    internal static void PollCatalogs(DateTime now)
    {
        if (Catalogs == null) return;
        try
        {
            Catalogs.Poll(now);
            if (!Catalogs.Changed || _catalogWarningShown) return;
            _catalogWarningShown = true;
            Logger.LogWarning("게임 카탈로그 변경 감지 · 실행 유지 / Game catalog changed; continuing with per-feature API guards.");
            DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.Catalog, DiagnosticOutcome.Partial);
            Diagnostics.Write("catalog changed; advisory only; native feature guards remain active");
        }
        catch (Exception ex)
        {
            Catalogs = null;
            try { DiagnosticHub.Failure(DiagnosticFeature.Loader, DiagnosticPhase.Catalog, DiagnosticCode.Unknown, ex); } catch { }
            try { Diagnostics.Error("catalog", ex); } catch { }
            try { Logger.LogWarning($"카탈로그 관찰 중단 · 실행 유지 / Catalog observation stopped; other features remain active: {ex}"); } catch { }
        }
    }

    private static void PatchFeature(string feature, params Type[] patches)
    {
        var harmony = new Harmony(Guid + "." + feature);
        DiagnosticHook Hook(Type patch) => patch == typeof(EventSystemUpdatePatch) ? DiagnosticHook.CoreUi
            : patch == typeof(StartupNoticePatch) ? DiagnosticHook.StartupNotice : DiagnosticHook.LauncherNotice;
        try
        {
            foreach (var patch in patches) harmony.CreateClassProcessor(patch).Patch();
            foreach (var patch in patches) DiagnosticHub.Registered(Hook(patch), true);
        }
        catch (Exception ex)
        {
            DiagnosticHub.Failure(feature == "CoreUi" ? DiagnosticFeature.CoreUi : DiagnosticFeature.Notices,
                DiagnosticPhase.PatchRegistration, DiagnosticCode.CompatibilityBlocked, ex);
            foreach (var patch in patches) DiagnosticHub.Registered(Hook(patch), false);
            Compatibility.Block(feature, ex, () => harmony.UnpatchSelf());
        }
    }

    internal static bool RequireHook(Type type, string method, Type result, bool isStatic = false)
    {
        var target = AccessTools.DeclaredMethod(type, method, Type.EmptyTypes);
        if (target == null || target.ReturnType != result || target.IsStatic != isStatic)
            throw new MissingMethodException(type.FullName, method + "() -> " + result.Name);
        return true;
    }

    private static void RecordSettings()
    {
        bool[] enabled = { Enabled.Value, ShowDetails.Value, KoMinimum.Value, UseRealNames.Value, CardPopups.Value,
            GroupHand.Value, BattleStatus.Value, ShushuShield.Value, FieldBuffs.Value, MuteUnfocused.Value,
            DiagnosticLogging.Value, AutoDownload.Value, ApplyAfterExit.Value,
            InputAttentionState.Normalize(InputAttention.Value) == "Taskbar",
            InputAttentionState.Normalize(InputAttention.Value) == "Windows" };
        uint flags = 0; for (var i = 0; i < enabled.Length; i++) if (enabled[i]) flags |= 1u << i;
        var language = Language.Value == "한국어" ? DiagnosticLanguage.Korean : Language.Value == "English" ? DiagnosticLanguage.English
            : Language.Value == "Auto" ? DiagnosticLanguage.Auto : DiagnosticLanguage.Unknown;
        var channel = UpdateChannel.Value switch { "Stable" => DiagnosticChannel.Stable, "PreRelease" => DiagnosticChannel.PreRelease, "Beta" => DiagnosticChannel.Beta, _ => DiagnosticChannel.Unknown };
        var hand = HandExpandMode.Value == "Click" ? DiagnosticHandMode.Click : HandExpandMode.Value == "Hover" ? DiagnosticHandMode.Hover : DiagnosticHandMode.Unknown;
        MinimalDiagnostics?.Settings(new DiagnosticSettings(flags, language, channel, hand,
            (int)Math.Round(UiScale.Value * 100), (int)Math.Round(Opacity.Value * 100)));
         Diagnostics.State("settings",
        FormattableString.Invariant($"advice={Enabled.Value}; details={ShowDetails.Value}; koMinimum={KoMinimum.Value}; names={UseRealNames.Value}; cardPopups={CardPopups.Value}; battleStatus={BattleStatus.Value}; shushuShield={ShushuShield.Value}; fieldBuffs={FieldBuffs.Value}; fieldZoom={FieldZoom.Value}; matchFocus={MatchFocus.Value}; muteUnfocused={MuteUnfocused.Value}; inputAttention={InputAttention.Value}; scale={UiScale.Value:F2}; opacity={Opacity.Value:F2}"));
        Diagnostics.State("settings.features",
            FormattableString.Invariant($"autoThanks={AutoThanks.Value}; characterGuide={CharacterGuide.Value}; characterRoles={CharacterRoles.Value}; indicatorFade={IndicatorFade.Value}; indicatorHide={IndicatorHide.Value}; counterSound={CounterSound.Value}; counterAnimation={CounterAnimation.Value}; suppressNotices={SuppressNotices.Value}; pingFocus={PingFocus.Value}; updateNotifications={UpdateNotifications.Value}; automaticChecks={AutomaticChecks.Value}; shushuShieldRestrictions={ShushuShieldRestrictions.Value}; finalDamage={FinalDamage.Value}"));
    }

    private static ReleaseUpdateResult? _diagnosticRelease;
    private static ReleaseSessionStatus? _diagnosticAuthentication;
    internal static void ObserveUpdateDiagnostics()
    {
        // Enum names are allowlisted by SafeEnum.Parse; never pass request/auth cache identities.
        if (DiagnosticHub.Sink == null) return;
        var result = Updates.Result; var target = result.Release?.Version;
        if (!ReferenceEquals(_diagnosticRelease, result))
        { _diagnosticRelease = result; DiagnosticHub.Status(DiagnosticFeature.ReleaseCheck, SafeEnum.Parse<DiagnosticUpdateState>(result.Status.ToString()), target); }
        if (UpdateSession != null && _diagnosticAuthentication != UpdateSession.Status)
        { _diagnosticAuthentication = UpdateSession.Status; DiagnosticHub.Status(DiagnosticFeature.Authentication, SafeEnum.Parse<DiagnosticUpdateState>(UpdateSession.Status.ToString())); }
    }

}

// Only the login auto-open gate; manual NOTICE actions bypass this method.
[HarmonyPatch(typeof(UGUIController), nameof(UGUIController.IsNeedOpenNoticeWindow))]
internal static class StartupNoticePatch
{
    [HarmonyPrepare]
    private static bool Prepare() => Plugin.RequireHook(typeof(UGUIController), nameof(UGUIController.IsNeedOpenNoticeWindow), typeof(bool), true);
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        DiagnosticHub.ObserveHook(DiagnosticHook.StartupNotice);
        if (PvpSafety.Suspended || !Plugin.SuppressNotices.Value) return true;
        __result = false;
        return false;
    }
}

// The launcher opens notices before LoginSceneController runs. Keep its NOTICE button
// activation, but skip the automatic OpenNotice async invocation (before any rendering).
[HarmonyPatch(typeof(UGUILoginController), nameof(UGUILoginController.TryShowNoticeWin))]
internal static class LauncherNoticePatch
{
    [HarmonyPrepare]
    private static bool Prepare() => Plugin.RequireHook(typeof(UGUILoginController), nameof(UGUILoginController.TryShowNoticeWin), typeof(void));
    [HarmonyPrefix]
    private static bool Prefix(UGUILoginController __instance)
    {
        DiagnosticHub.ObserveHook(DiagnosticHook.LauncherNotice);
        if (PvpSafety.Suspended || !Plugin.SuppressNotices.Value || !Compatibility.Allowed("Notices")) return true;
        try
        {
            var button = __instance.btn_Notices;
            if (button != null) button.gameObject.SetActive(true);
            return false;
        }
        catch (Exception ex)
        {
            Compatibility.Block("Notices", ex, () => new Harmony(Plugin.Guid + ".Notices").UnpatchSelf());
            return true; // Leave the original notice behavior intact on failure.
        }
    }
}
