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
    public const string Version = "1.1.1";
    public const int InstallBundleProtocol = 1;
    public const string UpdateProtocol = "2";
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
    internal static ConfigEntry<bool> FieldBuffs { get; private set; } = null!;
    internal static ConfigEntry<bool> FieldZoom { get; private set; } = null!;
    internal static ConfigEntry<bool> DiagnosticLogging { get; private set; } = null!;
    internal static ConfigEntry<bool> MuteUnfocused { get; private set; } = null!;
    internal static ConfigEntry<bool> MatchFocus { get; private set; } = null!;
    internal static ConfigEntry<string> InputAttention { get; private set; } = null!;
    internal static DiagnosticLog Diagnostics { get; private set; } = null!;
    internal static ConfigEntry<float> UiScale { get; private set; } = null!;
    internal static ConfigEntry<float> Opacity { get; private set; } = null!;
    internal static ConfigEntry<string> Language { get; private set; } = null!;
    internal static ConfigEntry<string> UpdateChannel { get; private set; } = null!;
    internal static ConfigEntry<bool> AutoDownload { get; private set; } = null!;
    internal static ConfigEntry<bool> ApplyAfterExit { get; private set; } = null!;
    private static UpdatePreferencesBridge? _updatePreferences;
    internal static void PollUpdatePreferences() => _updatePreferences?.Poll(Automatic);
    internal static AutomaticUpdates Automatic { get; private set; } = new(Updating.UpdateDownloads.Production());
    internal static ReleaseUpdates Updates { get; } = ReleaseUpdates.Production(Version, ReleaseChannel.Stable);
    internal static PrivateReleaseSession? UpdateSession { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Logger { get; private set; } = null!;
    internal static CatalogGuard? Catalogs { get; private set; }

    internal static MinimalEventLog? MinimalDiagnostics { get; private set; }
    private DiagnosticPhase _diagnosticLoadPhase;

    public override void Load()
    {
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
        LoadStage(DiagnosticPhase.BootstrapGate);
        try {using var launchFence = new Updating.WindowsFileFence(Path.GetDirectoryName(Paths.BepInExRootPath)!); Updating.UpdateLaunchGate.Check(launchFence);}
        catch (Exception ex) {DiagnosticHub.Failure(DiagnosticFeature.Loader, DiagnosticPhase.BootstrapGate, DiagnosticCode.Prerequisite, ex);Log.LogError("업데이트 복구가 필요해 모드 로드를 중단합니다. 게임과 모드 런처를 닫고 복구 안내를 확인하세요. / Mod load blocked: update recovery required. Close the game and mod launcher and follow the recovery guide.");return;}
        DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.BootstrapGate, DiagnosticOutcome.Completed);
        LoadStage(DiagnosticPhase.Configuration);
        Enabled = Config.Bind("일반", "활성화", true, "PvE 전투 추천: 공격·놓아주기, 방어·회피, 카드 판단을 표시합니다. 공개 수치와 기본 규칙을 사용하며 특수 효과·추가 카드·임무 보상은 계산하지 않습니다.");
        ShowDetails = Config.Bind("전투 추천", "상세 확률 표시", true, "추천 호버의 피해·전투불능 확률을 표시합니다. KO·생존 최소 조건은 별도 설정입니다. / Hover damage and KO probabilities; minimum conditions have their own setting.");
        // Preserve the previously effective indicator state on the first upgrade only.
        KoMinimum = Config.Bind("화면", "KO 최소 조건 표시", Enabled.Value && ShowDetails.Value,
            "PvE KO·생존 최소 조건과 카드 호버 후 조건을 표시합니다. 전투 추천·호버 수치와 독립적입니다. / PvE KO/survival conditions and card previews, independent of Battle Advice and Hover Details.");
        UseRealNames = Config.Bind("화면", "캐릭터 본명 표시", true, "캐릭터 선택·목록의 이명을 게임 번역에 등록된 본명으로 표시합니다. 끄면 이명으로 복원합니다. 계정 닉네임과 프로필의 본명·이명 설명은 변경하지 않습니다.");
        CardPopups = Config.Bind("화면", "카드 확대 팝업", true, "PvE 카드·칩 핑 말풍선 클릭 팝업을 켭니다. 일반 카드·칩 호버는 변경하지 않습니다. 전투 추천과 독립적으로 설정하며, 끄면 팝업과 추가 클릭 영역을 제거합니다.");
        GroupHand = Config.Bind("화면", "손패 기능별 정렬", false, "기본은 원본 배치입니다. 켜면 PvE 필드 손패를 기능별로 모읍니다. / Native layout by default. Enable to group your PvE field hand by function.");
        HandExpandMode = Config.Bind("화면", "손패 펼치기 방식", "Hover", "Hover: 호버 세로 펼치기, Click: 클릭 가로 펼치기. 접힌 묶음은 75% 불투명도입니다. / Hover: vertical expansion; Click: horizontal expansion. Folded piles use 75% opacity.");
        BattleStatus = Config.Bind("화면", "전투 공개 상태", true, "PvE HP 아래 공개 손패 장수·효과와 플레이어·몬스터의 반격 가능 여부를 표시합니다. / Public hand counts, effects and player/monster counter indicators in PvE.");
        ShushuShield = Config.Bind("화면", "슈슈 쉴드 표시", BattleStatus.Value, "캐릭터의 슈슈 쉴드 효과 및 로컬 방어 버튼·방어 카드 잠금. OFF 시 모두 해제합니다. 전투 정보 표시와 독립적입니다. / Shushu shield effects on characters and local Defend/defense-card locks. OFF restores all; independent of Battle Info.");
        FieldBuffs = Config.Bind("화면", "필드 버프 표시", true, "PvE 필드 플레이어의 이름·체력을 항상 표시하고 공개 버프가 있으면 아이콘을 함께 표시합니다. 클릭하면 원본 효과 설명을 엽니다. 전투 정보 표시와 독립적이며 추가 서버 요청은 없습니다.");
        FieldZoom = Config.Bind("화면", "필드 휠 줌", true,
            "PvE 필드에서 휠로 확대·축소하고 자유 시점을 유지합니다. 자유 시점 카메라 종료를 누르면 기본 배율과 캐릭터 추적으로 돌아갑니다. / Wheel zoom keeps a free view on PvE fields. Exit Free Camera restores native zoom and follow. Default ON.");
        UiScale = Config.Bind("화면", "UI 배율", 1.0f, new ConfigDescription("추천 패널 크기입니다.", new AcceptableValueRange<float>(0.75f, 1.5f)));
        // New fill-only setting starts at 100%; do not carry over the old whole-panel opacity.
        Opacity = Config.Bind("화면", "배경 불투명도", 1f, new ConfigDescription("전투 패널 바탕만 0~100%로 조절합니다. 글자·아이콘·테두리 및 설정창·암막에는 적용하지 않습니다.", new AcceptableValueRange<float>(0f, 1f)));
        Language = Config.Bind("일반", "언어", "Auto", "Mod language: Auto, 한국어, English. Auto follows the native menu text.");
        ModText.Select(Language.Value);
        UpdateChannel = Config.Bind("일반", "업데이트 채널", "Stable",
            "Stable: 안정판, Beta: 베타 포함. 비공개 릴리스는 업데이트 인증이 필요합니다. 자동 다운로드·종료 후 적용 설정에 따라 검증된 업데이트를 내려받고 게임과 모드 런처 종료 후 모드 소유 파일을 교체합니다. / Stable or Beta. Private releases require Connect Updates. Auto Download and Apply After Exit control verified downloads and replacement of mod-owned files after the game and mod launcher exit.");
        AutoDownload = Config.Bind("업데이트", "자동 다운로드", true, "기본 ON. 확인된 저장소·서명·도우미 계약이 필요하며 Beta는 업데이트 인증도 필요합니다. / Default ON. Requires verified repository, signing trust and helper contract. Beta also requires update authentication.");
        ApplyAfterExit = Config.Bind("업데이트", "종료 후 적용", true, "기본 ON. 검증한 업데이트를 게임과 런처가 종료된 뒤 적용합니다. / Default ON. Apply a verified update after the game and launcher exit.");
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
            var channel = UpdateChannel.Value == "Beta" ? ReleaseChannel.Beta : ReleaseChannel.Stable;
            Updates.SetChannel(channel); Automatic.SetChannel(channel); Automatic.Context(null, Updates.Generation);
        }
        SelectUpdateChannel();
        UpdateChannel.SettingChanged += (_, _) => SelectUpdateChannel();
        DiagnosticLogging = Config.Bind("진단", "충돌 진단 로그", false,
            "모드 오류·성능·공개 전투 입력/결과를 로컬 순환 로그로 기록하며 HP 불일치 최근 2건을 별도 보존합니다. 기본 OFF·자동 업로드 없음. 계정·채팅·손패·미공개 주사위는 기록하지 않습니다. / Local rotating error, performance and public combat logs; retains two HP mismatch reports. Default OFF, no uploads or private game/account data.");

        DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.UpdateReadiness, DiagnosticOutcome.Completed);
        Logger = Log;
        // An already-running Steam client cannot inherit the launcher's environment.
        // The launcher passes this one-shot flag only after the same full compatibility check.
        if (Environment.GetEnvironmentVariable("BAP_COMPATIBILITY_CHECKED") != "1"
            && !Environment.GetCommandLineArgs().Contains("--bap-compatibility-checked", StringComparer.Ordinal))
        {
            DiagnosticHub.Gate(DiagnosticFeature.Loader, DiagnosticCode.CompatibilityBlocked);
            Log.LogError("호환성 검사 없이 실행되어 모드를 중단합니다. BetterAstralParty-Mod.cmd를 사용하세요.");
            return;
        }
        // Keep the saved key so upgrades preserve each user's existing choice.
        MuteUnfocused = Config.Bind("환경", "비활성 창 음소거", true,
            "창 비활성화 시 음소거: 게임 창이 포커스를 잃으면 소리를 끄고 돌아오면 복원합니다. 저장된 게임 볼륨은 변경하지 않습니다. 기본 ON.");
        MatchFocus = Config.Bind("환경", "매칭 성사 시 창 포커스", true,
            "PvE 매칭 성사 또는 로비 방 시작 시 잠깐 최상위로 올려 게임 창으로 전환한 뒤 기존 창 상태로 복원합니다. Windows가 전환을 허용하지 않으면 작업표시줄로 알립니다. / On a PvE match or lobby game start, briefly raise the game topmost, request focus, then restore its previous window state; flash the taskbar if Windows declines. Default ON.");
        InputAttention = Config.Bind("환경", "입력 대기 알림", "Taskbar",
            "비활성 창의 PvE 입력 대기 알림: Taskbar(작업표시줄), Windows(Windows 알림), Off. / Unfocused PvE input alerts: Taskbar, Windows or Off. Default Taskbar.");
        // Same key: migrate 0.18's serialized true/false without losing an OFF preference.
        InputAttention.Value = InputAttentionState.Normalize(InputAttention.Value);
        Diagnostics = new DiagnosticLog(Path.Combine(Paths.BepInExRootPath, "BetterAstralParty-Diagnostics"),
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
        UpdateDiagnostics();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Diagnostics.SetEnabled(false, "");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { UpdateSession?.ProcessExiting(); Updates.Dispose(); };
        try
        {
            LoadStage(DiagnosticPhase.Catalog);
            var gameRoot = Path.GetDirectoryName(Paths.BepInExRootPath)!;
            Catalogs = new CatalogGuard(Path.Combine(gameRoot, "BetterAstralParty.compatibility.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "feimo", "AstralParty_INT", "com.unity.addressables"));
            Catalogs.Verify(DateTime.UtcNow);
            DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.Catalog, DiagnosticOutcome.Completed);
            LoadStage(DiagnosticPhase.PatchRegistration);
            Diagnostics.Write("patches begin");
            // A missing notice hook must not prevent the independent UI driver from loading.
            PatchFeature("CoreUi", typeof(EventSystemUpdatePatch));
            PatchFeature("Notices", typeof(StartupNoticePatch), typeof(LauncherNoticePatch));
            Diagnostics.Write("patches complete");
            DiagnosticHub.Stage(DiagnosticFeature.Loader, DiagnosticPhase.PatchRegistration, Compatibility.Allowed("CoreUi") && Compatibility.Allowed("Notices") ? DiagnosticOutcome.Completed : DiagnosticOutcome.Partial);
        }
        catch (Exception ex)
        {
            DiagnosticHub.Failure(DiagnosticFeature.Loader, _diagnosticLoadPhase, DiagnosticCode.CompatibilityBlocked, ex);
            Diagnostics.Error("patches", ex);
            Compatibility.Block("CoreUi", ex);
            Log.LogError($"UI 패치 실패: {ex}");
        }
        Log.LogInfo($"{Name} {Version} 로드됨 — Korean/English, INT/PvE");
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
        var channel = UpdateChannel.Value == "Stable" ? DiagnosticChannel.Stable : UpdateChannel.Value == "Beta" ? DiagnosticChannel.Beta : DiagnosticChannel.Unknown;
        var hand = HandExpandMode.Value == "Click" ? DiagnosticHandMode.Click : HandExpandMode.Value == "Hover" ? DiagnosticHandMode.Hover : DiagnosticHandMode.Unknown;
        MinimalDiagnostics?.Settings(new DiagnosticSettings(flags, language, channel, hand,
            (int)Math.Round(UiScale.Value * 100), (int)Math.Round(Opacity.Value * 100)));
         Diagnostics.State("settings",
        FormattableString.Invariant($"advice={Enabled.Value}; details={ShowDetails.Value}; koMinimum={KoMinimum.Value}; names={UseRealNames.Value}; cardPopups={CardPopups.Value}; battleStatus={BattleStatus.Value}; shushuShield={ShushuShield.Value}; fieldBuffs={FieldBuffs.Value}; fieldZoom={FieldZoom.Value}; matchFocus={MatchFocus.Value}; muteUnfocused={MuteUnfocused.Value}; inputAttention={InputAttention.Value}; scale={UiScale.Value:F2}; opacity={Opacity.Value:F2}"));
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
        if (PvpSafety.Suspended) return true;
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
        if (PvpSafety.Suspended || !Compatibility.Allowed("Notices")) return true;
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
