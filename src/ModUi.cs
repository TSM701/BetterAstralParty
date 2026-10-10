using BetterAstralParty.Observability;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterAstralParty;

// Native FairyGUI controls: their original hover/down gears work without Unity UI patches.
internal static class ModUi
{
    private static readonly Dictionary<IntPtr, (RuntimeObject Button, string Action)> Controls = new();
    private static readonly Dictionary<string, RuntimeObject> Labels = new();
    private static readonly Dictionary<string, RuntimeObject> ToggleBackgrounds = new();
    private static RuntimeObject? _modal, _sheet, _backdrop;
    private static RuntimeObject? _settingsView, _settingsContent, _settingsScroll;
    private static RuntimeObject? _scrollTrack, _scrollThumb;
    private static (float Height, float Y, bool Visible)? _scrollMarker;
    private static float _scrollPosition;
    private static string? _helpAction;
    private static string? _helpOption;
    private static RuntimeObject? _dropdown;
    private static string? _dropdownAction;
    private static readonly Dictionary<IntPtr, (RuntimeObject Button, string Value)> Choices = new();
    private static bool _general;
    private static int _languageRevision = -1;
    private static int _updateRevision = -1, _automaticRevision = -1, _sessionRevision = -1;
    private static (bool Busy, string Status)? _diagnosticState;
    private static float _helpAt, _helpAlpha = 1;
    private static int _helpFontRevision = -1;
    private static RuntimeObject? _helpViewport;
    private static float _helpOverflow, _helpLineHeight, _helpOffset;
    private static IntPtr _pressed, _hovered;
    private static float _openedAt, _changedAt = -10, _closingAt = -1;
    private static int _lastFrame = -1;
    private static bool _pvpCleaned;
    private static readonly Action[] PvpCleanup = {
        UpdateNotificationUi.Clear,
        GameUi.Stop, CardPreviewUi.Reset, BattleStatusUi.Clear, EncounterCounterUi.Clear, ShushuShieldUi.Clear,
        CharacterGuideUi.Clear, HandLayoutUi.Clear, CardTargetButtonsUi.Clear, FieldZoomUi.Clear, () => FieldBuffUi.Clear("pvp-safety"),
        CharacterNameUi.RestoreAll, InputAttention.Stop, PingFocusGuard.Reset, AutoThanksUi.Clear,
        () => Plugin.Diagnostics.EndCombat(),
        () => { ClosePanel(true); GameUi.Dispose(_modal); _modal = null; }
    };
    private static string _lastError = "";
    private static string _savedText = "";
    private static int _compatibilityRevision = -1;
    private static (float Width, float Height, float Reveal)? _layout;
    internal static bool IsOpen => _modal != null && !_modal.Get<bool>("isDisposed") && _modal.Get<bool>("visible");

    internal static void Tick()
    {
        if (_lastFrame == Time.frameCount) return;
        _lastFrame = Time.frameCount;
        try
        {
            Plugin.Updates.Tick(DateTimeOffset.UtcNow, GameUi.HomeAvailable);
            Plugin.UpdateSession?.Poll();
            UpdateDiscoveryBridge.Apply(Plugin.Updates, Plugin.Automatic, Plugin.Version);
            Plugin.PollUpdatePreferences(); Plugin.Automatic.Tick(DateTimeOffset.UtcNow);
            Plugin.UpdateSession?.Poll();
            GameUi.RefreshReleaseNotification();
            Plugin.ObserveUpdateDiagnostics();
        }
        catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.None, DiagnosticCode.Unknown, ex); try { Plugin.Updates.FrameFailed(); } catch { } } // Updater failure cannot stop the UI frame.
        try { UpdateNotificationUi.Observe(); }
        catch (Exception ex) { Compatibility.Block(UpdateNotificationUi.Feature, ex, UpdateNotificationUi.Clear); }
        try { ShushuShieldUi.PollLoads(); }
        catch (Exception ex) { Compatibility.Block(ShushuShieldUi.Feature, ex, ShushuShieldUi.Clear); }
        // Match success precedes the field gate; the observer independently requires verified PvE.
        MatchFocus.Tick();
        var pauseGameplay = PvpSafety.Poll();
        DiagnosticHub.Gate(DiagnosticFeature.CoreUi, pauseGameplay ? PvpSafety.Suspended ? DiagnosticCode.PvpExcluded : DiagnosticCode.StartupWaiting : !Compatibility.Allowed("CoreUi") ? DiagnosticCode.CompatibilityBlocked : DiagnosticCode.None);
        Plugin.Diagnostics.State("pvpSafety", pauseGameplay ? (PvpSafety.Suspended ? "suspended" : "initializing") : "active");
        // Audio needs no match state; keep it available during startup, but restore it in PvP.
        if (Compatibility.Allowed("MuteUnfocused"))
        {
            try { if (PvpSafety.Suspended) FocusAudio.Restore(); else FocusAudio.Tick(); }
            catch (Exception ex) { Compatibility.Block("MuteUnfocused", ex, FocusAudio.Restore); }
        }
        if (pauseGameplay)
        {
            if (!_pvpCleaned)
            {
                _pvpCleaned = true;
                foreach (var cleanup in PvpCleanup)
                    try { cleanup(); }
                    catch (Exception ex) { _pvpCleaned = false; Plugin.Diagnostics.Error("PvpSafety.cleanup", ex); }
            }
            return;
        }
        _pvpCleaned = false;
        try { PingFocusGuard.Tick(); }
        catch (Exception ex) { Compatibility.Block("PingFocus", ex); }
        try { InputAttention.Tick(); }
        catch (Exception ex) { Compatibility.Block("InputAttention", ex, InputAttention.Stop); }
        var measure = Plugin.DiagnosticLogging.Value && Plugin.Diagnostics.IsRecording;
        var started = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var allocated = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
        long gameEnd = started, popupEnd = started, statusEnd = started;
        long fieldStart = 0, fieldEnd = 0;
        double handMs = 0;
        long handBytes = 0;
        try
        {
            if (!Compatibility.Allowed("CoreUi")) { FieldZoomUi.Tick(); DiagnosticHub.Gate(DiagnosticFeature.CoreUi, DiagnosticCode.CompatibilityBlocked); return; }
            Plugin.Catalogs?.Poll(DateTime.UtcNow);
            ModText.Select(Plugin.Language.Value);
            GameUi.Tick();
            AutoThanksUi.Tick();
            CharacterGuideUi.Tick();
            ModFont.Tick();
            if (_languageRevision != ModText.Revision)
            {
                _languageRevision = ModText.Revision;
                Plugin.Logger.LogInfo($"[Mod language] mode={ModText.Mode}; UI={(ModText.Korean ? "ko" : "en")}");
                var reopen = IsOpen;
                var scrollPosition = reopen ? _settingsScroll!.Get<float>("scrollingPosY") : 0;
                ClosePanel(true);
                GameUi.Dispose(_modal); _modal = null;
                GameUi.RefreshLanguage();
                if (reopen)
                {
                    TogglePanel();
                    _settingsScroll!.Call("SetPosY", scrollPosition, false);
                }
            }
            UpdateNotificationUi.Tick();
            FieldZoomUi.Tick();
            if (measure) gameEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            CardTargetButtonsUi.Tick();
            CardPreviewUi.Tick();
            var handStarted = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var handAllocated = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            HandLayoutUi.Tick();
            if (measure)
            {
                handMs = (System.Diagnostics.Stopwatch.GetTimestamp() - handStarted) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                handBytes = GC.GetAllocatedBytesForCurrentThread() - handAllocated;
            }
            if (measure) popupEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            ShushuShieldUi.Tick();
            BattleStatusUi.Tick();
            EncounterCounterUi.Tick();
            if (measure) fieldStart = System.Diagnostics.Stopwatch.GetTimestamp();
            FieldBuffUi.Tick();
            if (measure) fieldEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            if (measure) statusEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!GameUi.HomeAvailable && IsOpen) ClosePanel(true);
            if (GameUi.HomeAvailable && !UpdateNotificationUi.ConsumedInput && Input.GetKeyDown(KeyCode.F8)) TogglePanel();
            if (!IsOpen) return;
            if (_updateRevision != Plugin.Updates.Revision || _automaticRevision != Plugin.Automatic.Revision || _sessionRevision != (Plugin.UpdateSession?.Revision ?? -1))
            {
                _updateRevision = Plugin.Updates.Revision; _automaticRevision = Plugin.Automatic.Revision;
                _sessionRevision = Plugin.UpdateSession?.Revision ?? -1;
                Refresh();
                if (_helpAction is "UpdateCheck" or "UpdateDownload" or "AutoDownload" or "AfterExit" or "UpdateGet" or "UpdateCancel" or "UpdateAuth" or "UpdateSignOut")
                { var action = _helpAction; _helpAction = null; SetHelp(action); }
            }
            if (_compatibilityRevision != Compatibility.Revision)
            {
                _compatibilityRevision = Compatibility.Revision;
                var help = _helpAction ?? ""; _helpAction = null;
                Refresh(); SetHelp(help);
            }
            var diagnosticState = (DiagnosticActions.Busy, DiagnosticActions.Status);
            if (_diagnosticState != diagnosticState)
            {
                _diagnosticState = diagnosticState;
                Refresh();
                if (_helpAction is "DiagnosticsOpen" or "DiagnosticsCollect")
                { var action = _helpAction; _helpAction = null; SetHelp(action); }
            }
            if (!UpdateNotificationUi.ConsumedInput && Input.GetKeyDown(KeyCode.Escape))
            {
                if (_dropdown != null) CloseDropdown(); else ClosePanel();
            }
            Layout();
            if (_closingAt < 0 && IsOpen && !UpdateNotificationUi.ConsumedInput) HandleInput();
            UpdateScrollMarker();
            AnimateHelp();
            var saved = Time.unscaledTime - _changedAt < 1.6f ? ModText.Text("✓ 저장됨") : $"BetterAstralParty {Plugin.Version}";
            if (_savedText != saved) { _savedText = saved; Labels["Saved"].Set("text", saved); }
        }
        catch (Exception ex)
        {
            DiagnosticHub.Failure(DiagnosticFeature.CoreUi, DiagnosticPhase.None, DiagnosticCode.Unknown, ex);
            FailUi(ex);
            Plugin.Diagnostics.Error("ModUi", ex);
            try { ClosePanel(true); } catch { GameUi.SetSettingsModal(false); }
            if (_lastError != ex.Message) Plugin.Logger.LogWarning($"[원본 모드 메뉴] {ex}");
            _lastError = ex.Message;
        }
        finally
        {
            if (measure)
            {
                var ended = System.Diagnostics.Stopwatch.GetTimestamp();
                var milliseconds = 1000d / System.Diagnostics.Stopwatch.Frequency;
                Plugin.Diagnostics.Performance(ended / (double)System.Diagnostics.Stopwatch.Frequency,
                    (ended - started) * milliseconds, (gameEnd - started) * milliseconds,
                    Math.Max(0, popupEnd - gameEnd) * milliseconds, Math.Max(0, statusEnd - popupEnd) * milliseconds,
                    GC.GetAllocatedBytesForCurrentThread() - allocated, Time.unscaledDeltaTime * 1000d,
                    fieldEnd > 0 ? (fieldEnd - fieldStart) * milliseconds : 0,
                    FieldBuffUi.ActivePlateCount, FieldBuffUi.EffectCount, handMs, handBytes);
            }
        }
    }

    internal static void FailUi(Exception ex) => Compatibility.Block("CoreUi", ex,
        UpdateNotificationUi.Clear,
        GameUi.Stop, CardPreviewUi.Reset, BattleStatusUi.Clear, EncounterCounterUi.Clear, ShushuShieldUi.Clear,
        CharacterGuideUi.Clear, HandLayoutUi.Clear, CardTargetButtonsUi.Clear, FieldZoomUi.Clear,
        () => FieldBuffUi.Clear("compatibility"), CharacterNameUi.RestoreAll,
        () => { ClosePanel(true); GameUi.Dispose(_modal); _modal = null; });

    private static RuntimeObject Button(string action, string item, string title, float x, float y, float width)
    {
        var button = NativeUi.Create("Common", item) ?? throw new InvalidOperationException($"원본 버튼 없음: {item}");
        var scale = width / button.Get<float>("width");
        button.Call("SetScale", scale, scale);
        NativeUi.Position(button, x, y, scale);
        NativeUi.StyleTitle(button, title);
        // Scale the glyph size with the control, not down to an unreadable tiny +/-.
        if (width <= 80) NativeUi.StyleTitle(button, title, (int)MathF.Round(52 * 80 / width));
        button.SetField("changeStateOnClick", false);
        _sheet!.Call("AddChild", button);
        Controls[button.Pointer] = (button, action);
        return button;
    }

    private static void Create()
    {
        if (_modal != null && !_modal.Get<bool>("isDisposed")) return;
        Controls.Clear(); Labels.Clear(); ToggleBackgrounds.Clear();
        _savedText = ""; _layout = null;
        _modal = NativeUi.Component(GameUi.Root!, 1920, 1080);
        _modal.Set("sortingOrder", 30000);
        _modal.Set("opaque", true);
        _modal.Set("visible", false);
        GameUi.AddRoundedRect(_modal, 1920, 1080, 0, new Color(0.015f, 0.01f, 0.03f, 0.7f));
        _backdrop = _modal.Call("GetChildAt", 0)!;
        _sheet = NativeUi.Component(_modal, MenuLayout.Width, MenuLayout.Height);
        var frame = NativeUi.Create("Common", "Com_PopUpWindow_Bottom")
            ?? throw new InvalidOperationException("원본 설정 프레임 준비 대기");
        _sheet.Call("AddChild", frame);
        frame.Set("touchable", false);
        var heading = frame.Call("GetChild", "title")!;
        heading.Set("text", ModText.Text("모드 설정"));
        GameUi.StyleText(heading, 42);
        foreach (var name in new[] { "closeButton", "btn_Sure", "btn_Sure_Only", "btn_Cancel" })
            frame.Call("GetChild", name)?.Set("visible", false);

        var helpArea = NativeUi.Component(_sheet, 360, 200);
        helpArea.Call("SetXY", 45f, 125f);
        helpArea.Set("touchable", false);
        _ = new NativeUi.Surface(helpArea, 360, 200, fixedOpacity: true);
        Labels["HelpTitle"] = NativeUi.Label(helpArea, "", 18, 22, 324, 44, 27);
        Labels["HelpTitle"].Set("singleLine", true);
        Labels["HelpTitle"].Set("autoSize", 3);
        _helpViewport = NativeUi.Component(helpArea, 324, 116);
        _helpViewport.Call("SetXY", 18f, 68f);
        _helpViewport.Set("touchable", false);
        _helpViewport.Call("SetupOverflow", 1); // Native Hidden: clip only the scrolling body.
        Labels["HelpBody"] = NativeUi.Label(_helpViewport, "", 0, 0, 324, 116, 22);
        Labels["HelpBody"].Set("singleLine", false);
        Labels["HelpBody"].Set("UBBEnabled", false); // Verified native property; mod-owned GTextField, never rich text.
        Labels["HelpBody"].Set("autoSize", 2); // Fixed width, wrapped auto-height.
        GameUi.StyleText(Labels["HelpBody"], 22, align: 0);
        _helpAction = null;
        SetHelp("");
        var features = Button("Features", "Button_ReturnRounded", ModText.Text("모드 기능"), 55, 330, 340);
        var generalY = MenuLayout.NextTabY(330, features.Get<float>("height") * features.Get<float>("scaleY"));
        Button("General", "Button_ReturnRounded", ModText.Text("일반 설정"), 55, generalY, 340);
        // Reuse a private native scroll-list clone: wheel, clipping and hit testing
        // stay with FairyGUI. One content component preserves our existing two-tab layout.
        // Common is already required by this window; no dependency on opening native Settings first.
        var scrollTemplate = NativeUi.Create("Common", "Com_BuffInfo")
            ?? throw new InvalidOperationException("Native settings scroll list unavailable");
        try
        {
            _settingsView = scrollTemplate.Call("GetChild", "list_Buff")!;
            _settingsView.Get("relations")!.Call("ClearAll");
            _sheet.Call("AddChild", _settingsView);
        }
        finally { GameUi.Dispose(scrollTemplate); }
        _settingsView.Call("RemoveChildren", 0, -1, true);
        _settingsView.Call("SetXY", MenuLayout.SettingsX, MenuLayout.SettingsY);
        _settingsView.Call("SetSize", MenuLayout.SettingsWidth, MenuLayout.SettingsHeight);
        _settingsView.Set("autoResizeItem", false);
        _settingsView.Set("layout", 0); // Single column containing our free-layout component.
        _settingsView.SetField("selectionMode", 3); // None: nested setting buttons own selection.
        // The single list item is the ENTIRE settings body, not the clicked button.
        _settingsView.SetField("scrollItemToViewOnClick", false);
        _settingsView.Set("touchable", true);
        _settingsView.Set("opaque", true);
        _settingsScroll = _settingsView.Get("scrollPane")!;
        _settingsScroll.Set("mouseWheelEnabled", true);
        _settingsScroll.Set("bouncebackEffect", false);
        _settingsScroll.Set("scrollStep", 48f);
        _settingsContent = NativeUi.Component(_settingsView, 500, MenuLayout.SettingsHeight);
        // Position-only indicator; native wheel/pan input remains on the settings list.
        _scrollTrack = new NativeUi.Surface(_sheet, 12, MenuLayout.SettingsHeight,
            fixedOpacity: true, fill: new Color(0.75f, 0.75f, 0.75f)).Graph;
        _scrollThumb = new NativeUi.Surface(_sheet, 12, 24,
            fixedOpacity: true, fill: new Color(1f, 0.8f, 0f)).Graph;
        _scrollTrack.Call("SetXY", MenuLayout.SettingsX + 505, MenuLayout.SettingsY);
        _scrollMarker = null;
        AddStepper("Scale", ModText.Text("표시 크기"), MenuLayout.ToggleY(5));
        AddStepper("Opacity", ModText.Text("배경 불투명도"), MenuLayout.ToggleY(6));
        AddToggle("Language", 440, 125);
        AddToggle("Diagnostics", 440, 190);
        AddToggle("MuteUnfocused", 440, 255);
        AddToggle("InputAttention", 440, 320);
        AddToggle("MatchFocus", 440, MenuLayout.ToggleY(4));
        AddToggle("UpdateChannel", 440, MenuLayout.ToggleY(7));
        AddToggle("UpdateCheck", 440, MenuLayout.ToggleY(8));
        AddToggle("UpdateDownload", 440, MenuLayout.ToggleY(9));
        AddToggle("AutoDownload", 440, MenuLayout.ToggleY(10));
        AddToggle("AfterExit", 440, MenuLayout.ToggleY(11));
        AddToggle("UpdateGet", 440, MenuLayout.ToggleY(12));
        AddToggle("UpdateCancel", 440, MenuLayout.ToggleY(13));
        AddToggle("UpdateAuth", 440, MenuLayout.ToggleY(14));
        AddToggle("UpdateSignOut", 440, MenuLayout.ToggleY(15));
        AddToggle("DiagnosticsOpen", 440, MenuLayout.ToggleY(16));
        AddToggle("DiagnosticsCollect", 440, MenuLayout.ToggleY(17));
        AddToggle("Enabled", 440, MenuLayout.ToggleY(0));
        AddToggle("Details", 440, MenuLayout.ToggleY(1));
        AddToggle("KoMinimum", 440, MenuLayout.ToggleY(2));
        AddToggle("CardPopups", 440, MenuLayout.ToggleY(3));
        AddToggle("BattleStatus", 440, MenuLayout.ToggleY(4));
        AddToggle("ShushuShield", 440, MenuLayout.ToggleY(5));
        AddToggle("FieldBuffs", 440, MenuLayout.ToggleY(6));
        AddToggle("Names", 440, MenuLayout.ToggleY(7));
        AddToggle("HandLayout", 440, MenuLayout.ToggleY(8));
        AddToggle("FieldZoom", 440, MenuLayout.ToggleY(9));
        foreach (var (button, action) in Controls.Values)
            if (!MenuLayout.FixedControl(action)) MoveIntoSettings(button);
        foreach (var key in new[] { "Scale", "ScaleValue", "Opacity", "OpacityValue" })
            MoveIntoSettings(Labels[key]);
        Button("Close", "Button_ReturnRounded", ModText.Text("돌아가기"), 410, MenuLayout.FooterY, 235);
        Labels["Saved"] = NativeUi.Label(_sheet, "", 675, MenuLayout.FooterY + 7, 315, 30, 18);
        Labels["Saved"].Set("autoSize", 3);
        Labels["Saved"].Set("singleLine", true);
        Refresh();
        Plugin.Logger.LogInfo("[원본 모드 메뉴] 고정 크기 토글·호버 기능 안내 생성됨");
    }

    private static void MoveIntoSettings(RuntimeObject item)
    {
        var x = item.Get<float>("x") - MenuLayout.SettingsX;
        var y = item.Get<float>("y") - MenuLayout.SettingsY;
        _settingsContent!.Call("AddChild", item);
        item.Call("SetXY", x, y);
    }

    private static void UpdateScrollMarker()
    {
        var view = _settingsScroll!.Get<float>("viewHeight");
        var content = _settingsScroll.Get<float>("contentHeight");
        var (height, y) = MenuLayout.ScrollMarker(view, content, _settingsScroll.Get<float>("scrollingPosY"));
        var visible = content > view;
        if (_scrollMarker == (height, y, visible)) return;
        _scrollMarker = (height, y, visible);
        UpdateDropdownArrows();
        _scrollTrack!.Set("visible", visible);
        _scrollThumb!.Set("visible", visible);
        _scrollThumb.Call("SetSize", 12f, height);
        _scrollThumb.Call("SetXY", MenuLayout.SettingsX + 505, MenuLayout.SettingsY + y);
    }

    private static void AddToggle(string action, float x, float y)
    {
        var width = MenuLayout.ToggleWidth(action);
        var toggle = Button(action, "Button_Switch_1", "", x, y, width);
        // Common-mode buttons never remain in the selected/down scaling state.
        // Modify only these new mod-owned instances, not the game's controls.
        toggle.Set("mode", 0);
        toggle.SetField("_downEffect", 1); // Native press tint, no size change.
        toggle.SetField("_downEffectValue", 0.88f);
        toggle.Call("SetPivot", 0f, 0f, true); // Tint-only controls retain top-left fixed-size layout.
        toggle.Call("SetScale", 1f, 1f);
        toggle.Call("SetSize", width, MenuLayout.ToggleHeight);
        toggle.Call("SetXY", x, y);
        var background = toggle.Call("GetChild", "n3")
            ?? throw new InvalidOperationException("설정 버튼 배경 준비 대기");
        background.Call("SetSize", width, MenuLayout.ToggleHeight);
        background.Set("touchable", false);
        ToggleBackgrounds[action] = background;
        var title = toggle.Call("GetChild", "title")!;
        title.Set("autoSize", 3); // Native Shrink: fit translations without expanding the button.
        title.Call("SetXY", 12f, 6f);
        var dropdown = MenuLayout.Options(action).Length > 2;
        title.Call("SetSize", width - (dropdown ? 64 : 24), MenuLayout.ToggleHeight - 12);
        if (dropdown) Labels["Arrow." + action] = NativeUi.Label(toggle, "▼", width - 46, 12, 32, 36, 22, dark: true);
    }

    private static void AddStepper(string action, string title, float y)
    {
        Labels[action] = NativeUi.Label(_sheet!, title + ":", 450, y + 7, 175, 35, 24);
        // Use ASCII hyphen-minus; the native TMP font does not display U+2212 here.
        Button(action + "Minus", "Button_Confirm_SmallRounded", "-", 635, y + 9, 56);
        Labels[action + "Value"] = NativeUi.Label(_sheet!, "", 693, y + 7, 76, 35, 24);
        Button(action + "Plus", "Button_Confirm_SmallRounded", "+", 785, y + 9, 56);
    }

    internal static void TogglePanel()
    {
        if (!GameUi.HomeAvailable || GameUi.Root == null) return;
        UpdateNotificationUi.CloseForSettings();
        if (IsOpen) { ClosePanel(); return; }
        var updateNotice = Plugin.Updates.NotificationVersion != null;
        if (updateNotice) _general = true;
        try { Create(); }
        catch { GameUi.Dispose(_modal); _modal = null; throw; }
        _closingAt = -1;
        _openedAt = Time.unscaledTime;
        _modal!.Set("visible", true);
        Plugin.Diagnostics.State("settingsPanel", "open");
        GameUi.SetSettingsModal(true);
        _pressed = _hovered = IntPtr.Zero;
        _settingsScroll!.Call("ScrollTop", false);
        _scrollPosition = 0;
        _helpAction = null;
        SetHelp(updateNotice ? "UpdateCheck" : "");
        Refresh();
        Layout();
        if (updateNotice) Plugin.Updates.AcknowledgeNotification();
        TraceInput("open", IntPtr.Zero);
    }

    internal static void OpenUpdateSettings()
    {
        if (!GameUi.HomeAvailable || GameUi.Root == null) return;
        _general = true;
        if (!IsOpen) TogglePanel();
        _closingAt = -1;
        _settingsScroll!.Call("ScrollTop", false);
        Refresh();
        _helpAction = null;
        SetHelp("UpdateCheck");
        Plugin.Updates.AcknowledgeNotification();
    }

    private static void ClosePanel(bool immediately = false)
    {
        CloseDropdown();
        if (!immediately && IsOpen && _closingAt < 0) { _closingAt = Time.unscaledTime; return; }
        if (!immediately) return;
        if (_modal != null && !_modal.Get<bool>("isDisposed")) _modal.Set("visible", false);
        _closingAt = -1;
        _pressed = _hovered = IntPtr.Zero;
        GameUi.SetSettingsModal(false);
        Plugin.Diagnostics.State("settingsPanel", "closed");
    }

    private static void Layout()
    {
        var width = GameUi.Root!.Get<float>("width");
        var height = GameUi.Root.Get<float>("height");
        var scale = MenuLayout.FitScale(width, height);
        var eased = _closingAt < 0 ? AdviceText.Reveal(Time.unscaledTime - _openedAt, 0.22f)
            : 1 - AdviceText.Reveal(Time.unscaledTime - _closingAt);
        if (_closingAt >= 0 && eased <= 0) { ClosePanel(true); return; }
        if (_layout == (width, height, eased)) return;
        _layout = (width, height, eased);
        _modal!.Call("SetSize", width, height);
        _backdrop!.Call("SetSize", width, height);
        _sheet!.Call("SetScale", scale, scale);
        _sheet.Call("SetXY", (width - MenuLayout.Width * scale) / 2,
            (height - MenuLayout.Height * scale) / 2 + (1 - eased) * 24f);
        _modal.Set("alpha", eased);
        if (_closingAt >= 0 && eased <= 0) ClosePanel(true);
    }

    private static void HandleInput()
    {
        if (_dropdown != null) { HandleDropdown(); return; }
        var hit = GameUi.PointerPath().FirstOrDefault(item => Controls.ContainsKey(item.Pointer));
        var point = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!
            .Get<Vector2>("touchPosition");
        var local = _settingsView!.Call("GlobalToLocal", point)!.Value<Vector2>();
        var position = _settingsScroll!.Get<float>("scrollingPosY");
        var scrolling = position != _scrollPosition || _settingsScroll.Get<bool>("isDragged");
        _scrollPosition = position;
        if (scrolling) { _pressed = IntPtr.Zero; hit = null; }
        if (hit != null && !MenuLayout.FixedControl(Controls[hit.Pointer].Action)
            && !MenuLayout.InSettings(local.x, local.y)) hit = null;
        var current = hit?.Pointer ?? IntPtr.Zero;
        if (current != _hovered)
        {
            if (Controls.TryGetValue(_hovered, out var old)) NativeUi.Play(old.Button, "Switchout");
            if (hit != null) NativeUi.Play(hit, "Switchin");
            _hovered = current;
            SetHelp(hit == null ? "" : Controls[current].Action);
        }
        if (Input.GetMouseButtonDown(0)) { _pressed = current; TraceInput("down", current); }
        if (!Input.GetMouseButtonUp(0)) return;
        TraceInput("up", current);
        if (current != IntPtr.Zero && current == _pressed && !hit!.Get<bool>("grayed")) Apply(Controls[current].Action);
        _pressed = IntPtr.Zero;
    }

    // Opt-in, settings-panel-only diagnostics. Never record game labels or account data.
    private static void TraceInput(string phase, IntPtr hit)
    {
        if (!Plugin.Diagnostics.IsRecording) return;
        try
        {
            string Action(IntPtr pointer) => Controls.TryGetValue(pointer, out var control) ? control.Action : "none";
            var mouse = Input.mousePosition;
            Plugin.Diagnostics.Write(FormattableString.Invariant($"settingsInput phase={phase}; frame={Time.frameCount}; mouseBottomLeft={mouse.x:F1},{mouse.y:F1}; screen={Screen.width},{Screen.height}; hit={Action(hit)}; pressed={Action(_pressed)}"));
            // Detailed input trace reads no extra native controls, captions or bounds.

        }
        catch (Exception ex) { Plugin.Diagnostics.Error("SettingsInputTrace", ex); }
    }

    private static bool ToggleEnabled(string action) => action switch
    {
        "Enabled" => Plugin.Enabled.Value,
        "KoMinimum" => Plugin.KoMinimum.Value,
        "CardPopups" => Plugin.CardPopups.Value,
        "HandLayout" => Plugin.GroupHand.Value,
        "BattleStatus" => Plugin.BattleStatus.Value,
        "ShushuShield" => Plugin.ShushuShield.Value,
        "FieldBuffs" => Plugin.FieldBuffs.Value,
        "FieldZoom" => Plugin.FieldZoom.Value,
        "Names" => Plugin.UseRealNames.Value,
        "Diagnostics" => Plugin.DiagnosticLogging.Value,
        "MuteUnfocused" => Plugin.MuteUnfocused.Value,
        "MatchFocus" => Plugin.MatchFocus.Value,
        "InputAttention" => InputAttentionState.Normalize(Plugin.InputAttention.Value) != "Off",
        "AutoDownload" => Plugin.Automatic.AutoDownload,
        "AfterExit" => Plugin.Automatic.ApplyAfterExit,
        _ => Plugin.ShowDetails.Value
    };

    private static void SetHelp(string action, string? option = null)
    {
        if (_helpAction == action && _helpOption == option) return;
        _helpAction = action;
        _helpOption = option;
        var (title, body) = MenuLayout.Help(action);
        var mode = action switch
        {
            "Language" => ModText.Mode,
            "HandLayout" => Plugin.HandExpandMode.Value,
            "InputAttention" => InputAttentionState.Normalize(Plugin.InputAttention.Value),
            "UpdateChannel" => Plugin.UpdateChannel.Value,
            _ => ""
        };
        body = option == null ? MenuLayout.StateHelp(action, ToggleEnabled(action), mode)
            : MenuLayout.OptionHelp(action, option);
        if (option != null) title = MenuLayout.OptionLabel(action, option);
        body = Compatibility.Reason(action) ?? body;
        Labels["HelpTitle"].Set("text", action is "UpdateAuth" or "UpdateSignOut" ? ModText.SessionTitle(action) : ModText.Text(title));
        Labels["HelpBody"].Set("text", action is "UpdateAuth" or "UpdateSignOut" ? ModText.SessionDetails(Plugin.UpdateSession?.Status ?? ReleaseSessionStatus.NotConfigured) : action == "UpdateCheck"
            ? ModText.UpdateDetails(Plugin.Updates.Result, Plugin.Version) : action is "AutoDownload" or "AfterExit" or "UpdateGet" or "UpdateCancel"
                ? ModText.AutomaticDetails(Plugin.Automatic.Status, Plugin.Automatic.RecoveryStage, Plugin.Automatic.Fault, Plugin.Updates.Channel) : ModText.Text(body));
        var label = Labels["HelpBody"];
        ModFont.Track(label); // Resolve fallback before measuring scrolling bounds.
        // textHeight forces native line layout once, rather than measuring every frame.
        _helpOverflow = Math.Max(0, label.Get<float>("textHeight") - _helpViewport!.Get<float>("height"));
        var format = label.Get("textFormat")!;
        _helpLineHeight = Math.Max(1, format.Field("size")!.Value<int>() + format.Field("lineSpacing")!.Value<int>());
        _helpOffset = 0;
        label.Set("y", 0f);
        _helpAt = Time.unscaledTime;
        _helpAlpha = 0;
        Labels["HelpBody"].Set("alpha", 0f);
    }

    private static void AnimateHelp()
    {
        if (_helpFontRevision != ModFont.Revision)
        {
            _helpFontRevision = ModFont.Revision;
            var label = Labels["HelpBody"];
            _helpOverflow = Math.Max(0, label.Get<float>("textHeight") - _helpViewport!.Get<float>("height"));
            var format = label.Get("textFormat")!;
            _helpLineHeight = Math.Max(1, format.Field("size")!.Value<int>() + format.Field("lineSpacing")!.Value<int>());
        }
        var elapsed = Time.unscaledTime - _helpAt;
        if (_helpAlpha < 1)
        {
            _helpAlpha = AdviceText.Reveal(elapsed, 0.14f);
            Labels["HelpBody"].Set("alpha", _helpAlpha);
        }
        var offset = MenuLayout.HelpScrollOffset(elapsed, _helpOverflow, _helpLineHeight);
        if (_helpOffset == offset) return;
        _helpOffset = offset;
        Labels["HelpBody"].Set("y", -offset);
    }

    private static void Apply(string action)
    {
        var operation = DiagnosticOperation.New();
        var safeAction = SafeEnum.Parse<DiagnosticUiAction>(action);
        DiagnosticHub.UiAction(safeAction, DiagnosticOutcome.Begin, operation);
        DiagnosticHub.FlushSoon();
        if (Plugin.MinimalDiagnostics?.Detailed == true) _ = Plugin.MinimalDiagnostics.FlushBoundary(15);
        Plugin.Diagnostics.Write("settingsAction begin=" + action);
        try
        {
            ApplyCore(action);
            DiagnosticHub.UiAction(safeAction, DiagnosticOutcome.Completed, operation);
            Plugin.Diagnostics.Write("settingsAction complete=" + action);
        }
        catch (Exception ex)
        {
            DiagnosticHub.UiAction(safeAction, DiagnosticOutcome.Failed, operation);
            DiagnosticHub.Failure(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, DiagnosticCode.Unknown, ex, operation: operation);
            Plugin.Diagnostics.Error("settingsAction." + action, ex);
            throw;
        }
    }

    private static void CloseDropdown()
    {
        if (_dropdown == null) return;
        var action = _dropdownAction;
        if (Plugin.Diagnostics.IsRecording) Plugin.Diagnostics.Write("settingsDropdown close begin; action=" + action);
        GameUi.Dispose(_dropdown);
        _dropdown = null; _dropdownAction = null;
        Choices.Clear();
        _pressed = _hovered = IntPtr.Zero;
        if (action != null) SetHelp(action);
        if (Plugin.Diagnostics.IsRecording) Plugin.Diagnostics.Write("settingsDropdown close complete; action=" + action);
    }

    private static Vector2 DropdownPosition(RuntimeObject button, float height)
    {
        var origin = button.Call("LocalToGlobal", Vector2.zero)!.Value<Vector2>();
        var point = _sheet!.Call("GlobalToLocal", origin)!.Value<Vector2>();
        var edge = button.Call("LocalToGlobal", new Vector2(0, button.Get<float>("height")))!.Value<Vector2>();
        var bottom = _sheet.Call("GlobalToLocal", edge)!.Value<Vector2>().y;
        var footer = Controls.Values.First(c => c.Action == "Close").Button.Get<float>("y");
        return new Vector2(point.x, MenuLayout.DropdownY(point.y, bottom - point.y, height, footer));
    }

    private static void UpdateDropdownArrows()
    {
        foreach (var (button, action) in Controls.Values)
        {
            if (!Labels.TryGetValue("Arrow." + action, out var arrow) || !button.Get<bool>("visible")) continue;
            var height = MenuLayout.DropdownHeight(action);
            var position = DropdownPosition(button, height);
            var origin = _sheet!.Call("GlobalToLocal", button.Call("LocalToGlobal", Vector2.zero)!.Value<Vector2>())!.Value<Vector2>();
            arrow.Set("text", position.y < origin.y ? "▲" : "▼");
        }
    }

    private static void OpenDropdown(string action)
    {
        if (Plugin.Diagnostics.IsRecording) Plugin.Diagnostics.Write("settingsDropdown open begin; action=" + action);
        CloseDropdown();
        var button = Controls.Values.First(c => c.Action == action).Button;
        var options = MenuLayout.Options(action);
        var height = MenuLayout.DropdownHeight(action);
        var point = DropdownPosition(button, height);
        _dropdown = NativeUi.Component(_sheet!, 480, height);
        _dropdown.Set("sortingOrder", 100);
        _dropdown.Set("opaque", true);
        _dropdown.Call("SetXY", point.x, point.y);
        _ = new NativeUi.Surface(_dropdown, 480, height, fixedOpacity: true);
        _dropdownAction = action;
        var selected = action switch
        {
            "Language" => ModText.Mode,
            "HandLayout" => !Plugin.GroupHand.Value ? "Native" : Plugin.HandExpandMode.Value,
            _ => InputAttentionState.Normalize(Plugin.InputAttention.Value)
        };
        for (var i = 0; i < options.Length; i++)
        {
            var item = NativeUi.Create("Common", "Button_Switch_1")!;
            _dropdown.Call("AddChild", item);
            item.Set("mode", 0);
            item.SetField("changeStateOnClick", false);
            item.SetField("_downEffect", 1);
            item.SetField("_downEffectValue", 0.88f);
            item.Call("SetPivot", 0f, 0f, true);
            item.Call("SetScale", 1f, 1f);
            item.Call("SetSize", 468f, 52f);
            item.Call("SetXY", 6f, 6f + i * 56f);
            var background = item.Call("GetChild", "n3")!;
            background.Call("SetSize", 468f, 52f);
            background.Set("touchable", false);
            background.Set("grayed", options[i] != selected);
            NativeUi.StyleTitle(item, ModText.Text(MenuLayout.OptionLabel(action, options[i])), 24);
            var title = item.Call("GetChild", "title")!;
            title.Call("SetXY", 12f, 6f);
            title.Call("SetSize", 444f, 40f);
            Choices[item.Pointer] = (item, options[i]);
        }
        _pressed = _hovered = IntPtr.Zero;
        if (Plugin.Diagnostics.IsRecording) Plugin.Diagnostics.Write("settingsDropdown open complete; action=" + action);
    }

    private static void HandleDropdown()
    {
        if (!Compatibility.Allowed(_dropdownAction!)) { CloseDropdown(); return; }
        if (_settingsScroll!.Get<bool>("isDragged")
            || _settingsScroll.Get<float>("scrollingPosY") != _scrollPosition)
        { CloseDropdown(); return; }
        var path = GameUi.PointerPath().ToArray();
        var hit = path.FirstOrDefault(item => Choices.ContainsKey(item.Pointer));
        var current = hit?.Pointer ?? IntPtr.Zero;
        if (current != _hovered)
        {
            if (Choices.TryGetValue(_hovered, out var old)) NativeUi.Play(old.Button, "Switchout");
            if (hit != null) NativeUi.Play(hit, "Switchin");
            _hovered = current;
            SetHelp(_dropdownAction!, hit == null ? null : Choices[current].Value);
        }
        if (Input.GetMouseButtonDown(0))
        {
            _pressed = current;
            if (!path.Any(item => item.Pointer == _dropdown!.Pointer)) CloseDropdown();
            return; // Never pass the dismissing click to an underlying setting.
        }
        if (!Input.GetMouseButtonUp(0)) return;
        if (current == IntPtr.Zero || current != _pressed) { _pressed = IntPtr.Zero; return; }
        var action = _dropdownAction!;
        var value = Choices[current].Value;
        if (Plugin.Diagnostics.IsRecording) Plugin.Diagnostics.Write($"settingsDropdown select; action={action}; value={value}");
        CloseDropdown();
        switch (action)
        {
            case "Language": Plugin.Language.Value = value; ModText.Select(value); break;
            case "HandLayout":
                HandLayoutUi.Clear();
                Plugin.HandExpandMode.Value = value == "Click" ? "Click" : "Hover";
                Plugin.GroupHand.Value = value != "Native";
                break;
            case "InputAttention": InputAttention.Stop(); Plugin.InputAttention.Value = value; break;
        }
        _changedAt = Time.unscaledTime;
        Refresh();
        _helpAction = null;
        SetHelp(action);
        if (Plugin.Diagnostics.IsRecording) Plugin.Diagnostics.Write($"settingsDropdown applied; action={action}; value={value}");
    }

    private static void ApplyCore(string action)
    {
        if (!Compatibility.Allowed(action)) return;
        if (MenuLayout.Options(action).Length > 2) { OpenDropdown(action); return; }
        switch (action)
        {
            case "DiagnosticsOpen": DiagnosticActions.OpenFolder(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "DiagnosticsCollect": DiagnosticActions.CreateBundle(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "Close": ClosePanel(); return;
            case "Features": _general = false; _settingsScroll!.Call("ScrollTop", false); Refresh(); return;
            case "General": _general = true; _settingsScroll!.Call("ScrollTop", false); Refresh(); return;
            case "UpdateAuth": Plugin.UpdateSession?.Connect(new WindowsReleaseCredentialInput(), ModText.Korean); Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateSignOut": Plugin.UpdateSession?.SignOut(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateChannel": Plugin.UpdateChannel.Value = Plugin.UpdateChannel.Value == "Beta" ? "Stable" : "Beta"; break;
            case "UpdateCheck":
                Plugin.Updates.Check(DateTimeOffset.UtcNow);
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateDownload":
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Plugin.Updates.RepositoryPage.AbsoluteUri) { UseShellExecute = true }); }
                catch { Plugin.Updates.DownloadFailed(); }
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "AutoDownload": Plugin.AutoDownload.Value = !Plugin.AutoDownload.Value; break;
            case "AfterExit": Plugin.ApplyAfterExit.Value = !Plugin.ApplyAfterExit.Value; break;
            case "UpdateGet": if (Plugin.Automatic.CanReactivate) Plugin.Automatic.Reactivate(); else if (Plugin.Automatic.CanRecheck) Plugin.Automatic.Recheck(); else Plugin.Automatic.Download(DateTimeOffset.UtcNow); Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateCancel": Plugin.Automatic.Cancel(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "Enabled": Plugin.Enabled.Value = !Plugin.Enabled.Value; break;
            case "Details": Plugin.ShowDetails.Value = !Plugin.ShowDetails.Value; break;
            case "KoMinimum": Plugin.KoMinimum.Value = !Plugin.KoMinimum.Value; if (!Plugin.KoMinimum.Value) CardDiceUi.Clear(); break;
            case "Names": Plugin.UseRealNames.Value = !Plugin.UseRealNames.Value; break;
            case "CardPopups": Plugin.CardPopups.Value = !Plugin.CardPopups.Value; break;
            case "BattleStatus": Plugin.BattleStatus.Value = !Plugin.BattleStatus.Value; break;
            case "ShushuShield": Plugin.ShushuShield.Value = !Plugin.ShushuShield.Value; if (!Plugin.ShushuShield.Value) ShushuShieldUi.Clear(); break;
            case "FieldBuffs": Plugin.FieldBuffs.Value = !Plugin.FieldBuffs.Value; break;
            case "FieldZoom": Plugin.FieldZoom.Value = !Plugin.FieldZoom.Value; if (!Plugin.FieldZoom.Value) FieldZoomUi.Clear(); break;
            case "Diagnostics": Plugin.DiagnosticLogging.Value = !Plugin.DiagnosticLogging.Value; break;
            case "MuteUnfocused": Plugin.MuteUnfocused.Value = !Plugin.MuteUnfocused.Value; break;
            case "MatchFocus": Plugin.MatchFocus.Value = !Plugin.MatchFocus.Value; break;
            case "ScaleMinus": Plugin.UiScale.Value = Math.Max(0.75f, Plugin.UiScale.Value - 0.05f); break;
            case "ScalePlus": Plugin.UiScale.Value = Math.Min(1.5f, Plugin.UiScale.Value + 0.05f); break;
            case "OpacityMinus": Plugin.Opacity.Value = Math.Max(0f, MathF.Round(Plugin.Opacity.Value - 0.05f, 2)); break;
            case "OpacityPlus": Plugin.Opacity.Value = Math.Min(1f, MathF.Round(Plugin.Opacity.Value + 0.05f, 2)); break;
        }
        _changedAt = Time.unscaledTime;
        Refresh();
        TraceInput("applied", _pressed);
        _helpAction = null;
        SetHelp(action);
    }

    private static void Refresh()
    {
        foreach (var (button, action) in Controls.Values)
        {
            var general = MenuLayout.GeneralControl(action);
            button.Set("visible", MenuLayout.FixedControl(action) || general == _general);
            if (action is "DiagnosticsOpen" or "DiagnosticsCollect")
            {
                var busy = action == "DiagnosticsCollect" && DiagnosticActions.Busy;
                button.Set("grayed", busy); ToggleBackgrounds[action].Set("grayed", busy);
                var state = action == "DiagnosticsCollect" ? DiagnosticActions.Status : "";
                NativeUi.StyleTitle(button, ModText.Text(MenuLayout.Help(action).Title) + (state.Length == 0 ? "" : ": " + state), 22);
                continue;
            }
            if (action is "UpdateAuth" or "UpdateSignOut")
            {
                var enabled = action == "UpdateAuth" ? Plugin.UpdateSession?.CanConnect == true : Plugin.UpdateSession?.CanSignOut == true;
                ToggleBackgrounds[action].Set("grayed", !enabled);
                NativeUi.StyleTitle(button, ModText.SessionTitle(action), 22);
                continue;
            }
            if (action is "UpdateChannel" or "UpdateCheck" or "UpdateDownload")
            {
                var available = action == "UpdateDownload" || Plugin.Updates.TryDownloadPage(out _);
                var state = action == "UpdateChannel" ? ModText.Text(Plugin.Updates.Channel == ReleaseChannel.Beta ? "베타 포함" : "안정판")
                    : action == "UpdateCheck" ? ModText.UpdateStatus(Plugin.Updates.Result.Status) : "";
                ToggleBackgrounds[action].Set("grayed", action != "UpdateChannel" && !available);
                NativeUi.StyleTitle(button, ModText.Text(MenuLayout.Help(action).Title) + (state.Length == 0 ? "" : ": " + state), 22);
                continue;
            }
            if (action is "UpdateGet" or "UpdateCancel")
            {
                var available = action == "UpdateGet" ? Plugin.Automatic.CanDownload || Plugin.Automatic.CanReactivate || Plugin.Automatic.CanRecheck : Plugin.Automatic.CanCancel;
                button.Set("grayed", !available); ToggleBackgrounds[action].Set("grayed", !available);
                NativeUi.StyleTitle(button, (action == "UpdateGet" && Plugin.Automatic.CanReactivate ? ModText.Korean ? "다시 사용" : "Re-enable Updates" : action == "UpdateGet" && Plugin.Automatic.CanRecheck ? ModText.Korean ? "상태 다시 확인" : "Recheck Installation" : ModText.Text(MenuLayout.Help(action).Title)) + ": " + ModText.AutomaticStatus(Plugin.Automatic.Status), 20);
                continue;
            }
            if (action == "Language")
            {
                ToggleBackgrounds[action].Set("grayed", false);
                NativeUi.StyleTitle(button, ModText.LanguageTitle, 26);
                continue;
            }
            if (ToggleBackgrounds.TryGetValue(action, out var background))
            {
                var enabled = ToggleEnabled(action);
                var blocked = !Compatibility.Allowed(action);
                var title = MenuLayout.Help(action).Title;
                var state = action == "Names" ? (enabled ? "본명" : "이명") : (enabled ? "ON" : "OFF");
                if (action == "InputAttention") state = InputAttentionState.Label(Plugin.InputAttention.Value);
                if (action == "HandLayout") state = HandLayout.ModeLabel(HandLayout.Mode(Plugin.GroupHand.Value, Plugin.HandExpandMode.Value));
                if (blocked) state = "사용 불가";
                // Gray only the yellow artwork: OFF remains fully clickable and hoverable.
                background.Set("grayed", blocked || !enabled);
                NativeUi.StyleTitle(button, ModText.Text(title) + ": " + ModText.Text(state), blocked || action is "InputAttention" or "HandLayout" ? 22 : 26);
                continue;
            }
            button.Set("grayed", action switch
            {
                "ScaleMinus" => Plugin.UiScale.Value <= 0.751f, "ScalePlus" => Plugin.UiScale.Value >= 1.499f,
                "OpacityMinus" => Plugin.Opacity.Value <= 0.001f, "OpacityPlus" => Plugin.Opacity.Value >= 0.999f,
                _ => false
            });
        }
        foreach (var key in new[] { "Scale", "ScaleValue", "Opacity", "OpacityValue" })
            Labels[key].Set("visible", _general);
        Labels["ScaleValue"].Set("text", $"{Plugin.UiScale.Value:P0}");
        Labels["OpacityValue"].Set("text", $"{Plugin.Opacity.Value:P0}");
        var bottom = 0f;
        for (var i = 0; i < _settingsContent!.Get<int>("numChildren"); i++)
        {
            var child = _settingsContent.Call("GetChildAt", i)!;
            if (!child.Get<bool>("visible")) continue;
            var size = new Vector2(child.Get<float>("width"), child.Get<float>("height"));
            var corner = child.Field("_pivotAsAnchor")!.Value<bool>()
                ? new Vector2(size.x * (1 - child.Get<float>("pivotX")), size.y * (1 - child.Get<float>("pivotY"))) : size;
            var global = child.Call("LocalToGlobal", corner)!.Value<Vector2>();
            bottom = Math.Max(bottom, _settingsContent.Call("GlobalToLocal", global)!.Value<Vector2>().y);
        }
        _settingsContent.Set("height", MenuLayout.ContentHeight(bottom));
        _settingsView!.Call("EnsureBoundsCorrect");
        UpdateDropdownArrows();
    }
}

[HarmonyPatch(typeof(EventSystem), "Update")]
internal static class EventSystemUpdatePatch
{
    [HarmonyPrepare]
    private static bool Prepare() => Plugin.RequireHook(typeof(EventSystem), "Update", typeof(void));
    [HarmonyPostfix]
    private static void Postfix() { DiagnosticHub.ObserveHook(DiagnosticHook.CoreUi); ModUi.Tick(); }
}
