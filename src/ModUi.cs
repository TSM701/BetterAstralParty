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
    private static readonly List<(RuntimeObject View, RuntimeObject Title, RuntimeObject Band, bool General)> SectionHeaders = new();
    private static RuntimeObject? _modal, _sheet, _backdrop;
    private static RuntimeObject? _settingsView, _settingsContent, _settingsScroll;
    private static RuntimeObject? _scrollTrack, _scrollThumb;
    // Native slider from the game's Setting package, loaded on demand like the Hero package.
    // Native GSliders by setting, their gray "unavailable" switches, and the backing cards of top-level slider rows.
    private static readonly Dictionary<string, RuntimeObject> Sliders = new(), SliderBlocked = new();
    private static readonly List<(RuntimeObject[] Parts, string Action)> SliderBacks = new();
    // Mod pills drawn behind child switches (ring, pill), following each switch's visibility, alpha, OFF gray and hover.
    private static readonly List<(RuntimeObject Button, RuntimeObject Background, RuntimeObject[] Parts)> ChildPills = new();
    private static readonly Dictionary<IntPtr, (bool Visible, float Alpha, Color Fill)> ChildPillStates = new();
    private static readonly Color ChildOn = new(1f, 0.855f, 0.361f), ChildOff = new(0.827f, 0.816f, 0.776f), ChildRing = new(0.604f, 0.604f, 0.604f);
    private static IntPtr _dragSlider;
    private static float _dragX;
    private static double _dragStart;
    private static readonly List<(RuntimeObject[] Parts, string Parent)> Trays = new();
    private static (float Height, float Y, bool Visible)? _scrollMarker;
    // Category dots on the scroll track; hovering the track shows every category's bubble beside it, and the bubbles stay
    // while the pointer is anywhere across their span. A still press on a dot or bubble jumps there; a press that moves
    // on the track drags the thumb as usual.
    private static readonly List<(int Section, RuntimeObject Dot)> Ticks = new();
    private static readonly List<(int Section, float Y)> TickPositions = new();
    private static readonly List<CategoryBubble> Bubbles = new();
    private static (float View, float Content, bool General, bool Visible)? _tickLayout;
    private static int _tickCurrent = -1, _tickHot = -1, _pressTick = -1;
    private static float _pressY, _flashAt = -1, _bubblesHideAt = -1, _bubblesShownAt;
    private static bool _scrollPending, _pressMoved, _pressOnTrack, _bubblesShown;
    private static RuntimeObject? _flashBand, _trackHit;
    private static readonly Color TickDark = new(0.17f, 0.17f, 0.16f, 1f), TickCurrent = new(0.88f, 0.29f, 0.29f, 1f),
        TickRing = new(1f, 0.985f, 0.95f, 1f), BandFill = new(0.45f, 0.33f, 0.18f, 0.16f), BandFlash = new(1f, 0.76f, 0.05f, 0.75f),
        BubblePlain = new(1f, 0.985f, 0.95f, 1f), BubbleCurrent = new(1f, 0.906f, 0.627f, 1f), BubbleHot = new(1f, 0.76f, 0.05f, 1f);

    // A cream bubble whose tail reads as part of it: the body is a rounded rect with a flat side, the tail square sits
    // behind it centred on that side, and a borderless patch in the body colour opens the body's outline where they meet.
    // Right bubbles point left at the track; left bubbles (crowded runs alternate) point right. Fill applies to all parts.
    private sealed class CategoryBubble
    {
        private const float TailX = 8, Tail = 10, Radius = 7, Line = 3, Opening = 6, Slide = 10;
        internal readonly int Section;
        internal readonly RuntimeObject Root;
        private readonly RuntimeObject _tail, _body, _patch, _text;
        private Color _fill = new(-1, 0, 0, 0);
        private float _alpha, _slide, _y;
        private bool _left;
        private (float Alpha, float Slide, bool Shown)? _drawn;
        internal float Width { get; private set; }
        internal float X { get; private set; }

        internal CategoryBubble(RuntimeObject parent, int section)
        {
            Section = section;
            Root = NativeUi.Component(parent, 120, MenuLayout.BubbleHeight);
            // Opaque while shown: a bubble over the list takes the press, so neither a switch nor the list's own drag gets it.
            Root.Set("opaque", true); Root.Set("touchable", false); Root.Set("visible", false);
            _tail = Graph(Root, Tail, Tail, Line, -1);
            _tail.Call("SetPivot", 0.5f, 0.5f, true);
            _tail.Set("rotation", 45f);
            _body = Graph(Root, 100, MenuLayout.BubbleHeight, Line, Radius);
            _patch = Graph(Root, Line + 1, Opening, 0f, -1);
            // A wide single line measures the whole title; a narrow label would wrap and report only its first glyph.
            _text = NativeUi.Label(Root, "", 0, 0, 320, MenuLayout.BubbleHeight, 16, dark: true);
            _text.Set("singleLine", true); _text.Set("verticalAlign", 1);
        }

        private static RuntimeObject Graph(RuntimeObject parent, float width, float height, float line, float radius)
        {
            var graph = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            graph.Set("touchable", false);
            graph.Call("SetSize", width, height);
            if (radius < 0) graph.Get("shape")!.Call("DrawRect", line, Color.black, BubblePlain);
            else graph.Get("shape")!.Call("DrawRoundRect", line, Color.black, BubblePlain, radius, radius, radius, radius);
            parent.Call("AddChild", graph);
            return graph;
        }

        // Sets the title and side, then places the bubble's flat side 4 from the track (x is the track edge it faces).
        internal void Place(string text, float edge, float centreY, bool left)
        {
            _text.Set("text", text);
            Width = TailX + 24 + _text.Get<float>("textWidth");
            var body = Width - TailX;
            var joint = left ? body : TailX;
            _body.Call("SetSize", body, MenuLayout.BubbleHeight);
            _body.Call("SetXY", left ? 0f : TailX, 0f);
            _tail.Call("SetXY", joint, MenuLayout.BubbleHeight / 2);
            _patch.Call("SetXY", left ? joint - Line : joint - 1, (MenuLayout.BubbleHeight - Opening) / 2);
            _text.Call("SetXY", left ? 12f : TailX + 12, 0f);
            _text.Call("SetSize", body - 24, MenuLayout.BubbleHeight);
            Root.Call("SetSize", Width, MenuLayout.BubbleHeight);
            X = left ? edge - 4 - Width : edge + 4;
            _y = centreY - MenuLayout.BubbleHeight / 2;
            _left = left;
            _drawn = null;
        }

        internal void Hide() { _alpha = _slide = 0; _drawn = null; Root.Set("visible", false); Root.Set("touchable", false); }

        // Fades in while sliding out from the track after its row's stagger delay; fades out in place.
        internal void Animate(bool shown, float delay, float dt)
        {
            if (!shown) { _alpha = Math.Max(0, _alpha - dt / 0.14f); if (_alpha <= 0) _slide = 0; }
            else if (delay <= 0) { _alpha = Math.Min(1, _alpha + dt / 0.14f); _slide = Math.Min(1, _slide + dt / 0.18f); }
            if (_drawn == (_alpha, _slide, shown)) return;
            _drawn = (_alpha, _slide, shown);
            Root.Set("visible", _alpha > 0);
            Root.Set("touchable", shown && _alpha > 0);
            if (_alpha <= 0) return;
            Root.Set("alpha", _alpha);
            var rest = (1 - _slide) * (1 - _slide) * (1 - _slide);
            Root.Call("SetXY", X + (_left ? Slide : -Slide) * rest, _y);
        }

        internal void SetFill(Color fill)
        {
            if (_fill == fill) return;
            _fill = fill;
            foreach (var graph in new[] { _tail, _body, _patch }) graph.Set("color", fill);
        }
    }

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
    private static RuntimeObject? _helpViewport, _helpTag, _helpTagFill;
    private static float _helpOverflow, _helpLineHeight, _helpOffset;
    private static IntPtr _pressed, _hovered;
    private static float _openedAt, _changedAt = -10, _closingAt = -1;
    private static int _lastFrame = -1;
    private static bool _pvpCleaned;
    private static readonly Action[] PvpCleanup = {
        UpdateNotificationUi.Clear,
        GameUi.Stop, CardPreviewUi.Reset, BattleStatusUi.Clear, EncounterCounterUi.Clear, ShushuShieldUi.Clear,
        CharacterGuideUi.Clear, HandLayoutUi.Clear, CardTargetButtonsUi.Clear, FieldZoomUi.Clear, () => FieldBuffUi.Clear("pvp-safety"), PlayerTabUi.Clear,
        CharacterNameUi.RestoreAll, InputAttention.Stop, PingFocusGuard.Reset, AutoThanksUi.Clear, BattleMenuUi.Clear, FieldDiceUi.Clear,
        () => Plugin.Diagnostics.EndCombat(),
        () => { ClosePanel(true); GameUi.Dispose(_modal); _modal = null; }
    };
    private static string _lastError = "";
    private static string _savedText = "";
    private static int _compatibilityRevision = -1;
    private static (float Width, float Height, float Reveal)? _layout;
    // The panel opens from the home menu or from the in-battle menu's mod settings button.
    private static bool CanOpen => GameUi.HomeAvailable || BattleMenuUi.Showing;
    internal static void OpenFromBattleMenu()
    {
        if (IsOpen) return;
        _general = false; // Gameplay features first; update and sign-in rows act on the home screen.
        TogglePanel();
    }
    internal static bool IsOpen => Compatibility.Allowed("SettingsUi") && _modal != null && !_modal.Get<bool>("isDisposed") && _modal.Get<bool>("visible");
    // The general tab holds the update rows, which act on the home screen; opening on a new release also shows its details in the help box.
    internal static bool ShowsUpdates => IsOpen && _general && GameUi.HomeAvailable;

    internal static void Tick()
    {
        if (_lastFrame == Time.frameCount) return;
        _lastFrame = Time.frameCount;
        // Runs before every gate below, so a tripped settings breaker cannot leave saving switched off.
        if (_sliderSave != null && !Input.GetMouseButton(0)) FlushSliderSave();
        if (Time.realtimeSinceStartup >= StartupGuard.HealthySeconds) StartupGuard.Healthy();
        RuntimeObject.FlushLedger(due: true);
        try
        {
            Plugin.Updates.InstalledChannel(Plugin.Automatic.Snapshot.InstalledChannel);
            Plugin.Updates.Tick(DateTimeOffset.UtcNow, (Plugin.AutomaticChecks.Value || Plugin.Updates.TransitionCheckPending) && (!Compatibility.Allowed("CoreUi") || !Compatibility.Allowed("HomeMenu") || GameUi.HomeAvailable) && Plugin.Automatic.Status != AutomaticUpdateStatus.CheckingInstallation);
            Plugin.UpdateSession?.Poll();
            UpdateDiscoveryBridge.Apply(Plugin.Updates, Plugin.Automatic, Plugin.Version);
            Plugin.PollUpdatePreferences(); Plugin.Automatic.Tick(DateTimeOffset.UtcNow); Plugin.AdoptConfirmedChannel();
            Plugin.UpdateSession?.Poll();
            Plugin.ObserveUpdateDiagnostics();
        }
        catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.None, DiagnosticCode.Unknown, ex); try { Plugin.Updates.FrameFailed(); } catch { } } // Updater failure cannot stop the UI frame.
        // These observers do not depend on the shared FairyGUI controls.
        MatchFocus.Tick();
        var pauseGameplay = PvpSafety.Poll();
        if (Compatibility.Allowed("MuteUnfocused"))
        {
            try { if (PvpSafety.Suspended) FocusAudio.Restore(); else FocusAudio.Tick(); }
            catch (Exception ex) { Compatibility.Block("MuteUnfocused", ex, FocusAudio.Restore); }
        }
        if (!Compatibility.Allowed("CoreUi"))
        {
            try { FieldZoomUi.Clear(); }
            catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.CoreUi, DiagnosticPhase.None, DiagnosticCode.Unknown, ex); }
            DiagnosticHub.Gate(DiagnosticFeature.CoreUi, DiagnosticCode.CompatibilityBlocked);
            return;
        }
        if (Compatibility.Allowed(UpdateNotificationUi.Feature))
        {
            try { GameUi.RefreshReleaseNotification(); GameUi.RefreshUpdateProgress(); }
            catch (Exception ex) { Compatibility.Block(UpdateNotificationUi.Feature, ex, UpdateNotificationUi.Clear); }
        }
        try { UpdateNotificationUi.Observe(); }
        catch (Exception ex) { Compatibility.Block(UpdateNotificationUi.Feature, ex, UpdateNotificationUi.Clear); }
        try { ShushuShieldUi.PollLoads(); }
        catch (Exception ex) { Compatibility.Block(ShushuShieldUi.Feature, ex, ShushuShieldUi.ClearDisplay); }
        DiagnosticHub.Gate(DiagnosticFeature.CoreUi, pauseGameplay ? PvpSafety.Suspended ? DiagnosticCode.PvpExcluded : DiagnosticCode.StartupWaiting : !Compatibility.Allowed("CoreUi") ? DiagnosticCode.CompatibilityBlocked : DiagnosticCode.None);
        Plugin.Diagnostics.State("pvpSafety", pauseGameplay ? (PvpSafety.Suspended ? "suspended" : "initializing") : "active");
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
        // Spike attribution: managed collections and IL2CPP heap drops (a native GC) during and between ticks.
        var clrGcs = measure ? GC.CollectionCount(0) : 0;
        var nativeUsed = measure ? (long)Il2CppInterop.Runtime.IL2CPP.il2cpp_gc_get_used_size() : 0;
        _measureTicks = measure;
        Array.Clear(TickMarks);
        TickMarks[0] = started;
        long gameEnd = started, popupEnd = started, statusEnd = started, coreEnd = started;
        long fieldStart = 0, fieldEnd = 0;
        double handMs = 0;
        long handBytes = 0;
        try
        {
            Plugin.PollCatalogs(DateTime.UtcNow);
            TickSettingsBackup();
            ModText.Select(Plugin.Language.Value);
            GameUi.Tick(); Mark(1);
            if (measure) coreEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            BattleMenuUi.Tick(); Mark(2);
            AutoThanksUi.Tick(); Mark(3);
            CharacterGuideUi.Tick(); Mark(4);
            ModFont.Tick(); Mark(5);
            if (_languageRevision != ModText.Revision)
            {
                _languageRevision = ModText.Revision;
                Plugin.Logger.LogInfo($"[Mod language] mode={ModText.Mode}; UI={(ModText.Korean ? "ko" : "en")}");
                GameUi.RefreshLanguage();
                try { RefreshSettingsLanguage(); }
                catch (Exception ex) { FailSettings(ex); }
            }
            Mark(6);
            UpdateNotificationUi.Tick(); Mark(7);
            FieldZoomUi.Tick(); Mark(8);
            FieldDiceUi.Tick(); Mark(9);
            if (measure) gameEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            CardTargetButtonsUi.Tick(); Mark(10);
            CardPreviewUi.Tick(); Mark(11);
            var handStarted = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var handAllocated = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            HandLayoutUi.Tick(); Mark(12);
            if (measure)
            {
                handMs = (System.Diagnostics.Stopwatch.GetTimestamp() - handStarted) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                handBytes = GC.GetAllocatedBytesForCurrentThread() - handAllocated;
            }
            if (measure) popupEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            ShushuShieldUi.Tick(); Mark(13);
            BattleStatusUi.Tick(); Mark(14);
            EncounterCounterUi.Tick(); Mark(15);
            if (measure) fieldStart = System.Diagnostics.Stopwatch.GetTimestamp();
            FieldBuffUi.Tick(); PlayerTabUi.Tick(); Mark(16);
            if (measure) fieldEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            if (measure) statusEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            try { TickSettings(); }
            catch (Exception ex) { FailSettings(ex); }
            Mark(17);
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
                var nativeAfter = (long)Il2CppInterop.Runtime.IL2CPP.il2cpp_gc_get_used_size();
                Plugin.Diagnostics.TickSpikes(TickMarks, milliseconds, GC.CollectionCount(0) - clrGcs,
                    nativeAfter < nativeUsed, _nativeUsedAfterTick > 0 && nativeUsed < _nativeUsedAfterTick);
                _nativeUsedAfterTick = nativeAfter;
                Plugin.Diagnostics.Performance(ended / (double)System.Diagnostics.Stopwatch.Frequency,
                    (ended - started) * milliseconds, (gameEnd - started) * milliseconds,
                    Math.Max(0, popupEnd - gameEnd) * milliseconds, Math.Max(0, statusEnd - popupEnd) * milliseconds,
                    GC.GetAllocatedBytesForCurrentThread() - allocated, Time.unscaledDeltaTime * 1000d,
                    fieldEnd > 0 ? (fieldEnd - fieldStart) * milliseconds : 0,
                    FieldBuffUi.ActivePlateCount, FieldBuffUi.EffectCount, handMs, handBytes, (coreEnd - started) * milliseconds,
                    FieldBuffUi.MonsterPlateCount);
            }
        }
    }

    private static readonly long[] TickMarks = new long[DiagnosticLog.TickFeatures.Length + 1];
    private static bool _measureTicks;
    private static long _nativeUsedAfterTick;

    private static void Mark(int index)
    {
        if (_measureTicks) TickMarks[index] = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    internal static void FailUi(Exception ex) => Compatibility.Block("CoreUi", ex,
        UpdateNotificationUi.Clear,
        GameUi.Stop, CardPreviewUi.Reset, BattleStatusUi.Clear, EncounterCounterUi.Clear, ShushuShieldUi.Clear,
        CharacterGuideUi.Clear, HandLayoutUi.Clear, CardTargetButtonsUi.Clear, FieldZoomUi.Clear,
        () => FieldBuffUi.Clear("compatibility"), PlayerTabUi.Clear, CharacterNameUi.RestoreAll, BattleMenuUi.Clear, FieldDiceUi.Clear,
        () => { ClosePanel(true); GameUi.Dispose(_modal); _modal = null; });

    private static void FailSettings(Exception error) => Compatibility.Block("SettingsUi", error,
        () => ClosePanel(true), () => GameUi.SetSettingsModal(false),
        () => { if (_modal != null) _modal.Set("touchable", false); },
        () => { if (_modal != null) _modal.Set("visible", false); },
        () => { GameUi.Dispose(_modal); _modal = null; });

    private static void RefreshSettingsLanguage()
    {
        if (!Compatibility.Allowed("SettingsUi")) return;
        var reopen = IsOpen;
        var scrollPosition = reopen ? _settingsScroll!.Get<float>("scrollingPosY") : 0;
        ClosePanel(true);
        GameUi.Dispose(_modal); _modal = null;
        if (reopen)
        {
            TogglePanel();
            if (Compatibility.Allowed("SettingsUi")) _settingsScroll!.Call("SetPosY", scrollPosition, false);
        }
    }

    private static void TickSettings()
    {
        if (!Compatibility.Allowed("SettingsUi")) return;
        if (!CanOpen && IsOpen) ClosePanel(true);
        if (GameUi.HomeAvailable && !UpdateNotificationUi.ConsumedInput && Input.GetKeyDown(KeyCode.F8)) TogglePanel();
        if (!Compatibility.Allowed("SettingsUi") || !IsOpen) return;
        if (_updateRevision != Plugin.Updates.Revision || _automaticRevision != Plugin.Automatic.Revision || _sessionRevision != (Plugin.UpdateSession?.Revision ?? -1))
        {
            _updateRevision = Plugin.Updates.Revision; _automaticRevision = Plugin.Automatic.Revision;
            _sessionRevision = Plugin.UpdateSession?.Revision ?? -1;
            Refresh();
            if (_helpAction is "UpdateCheck" or "UpdateDownload" or "UpdateSwitch" or "UpdateGet" or "UpdateApply" or "UpdateCancel" or "UpdateAuth" or "UpdateSignOut")
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
        foreach (var action in MenuLayout.Sliders)
        {
            if (!Compatibility.Allowed(action)) continue;
            try { TickSlider(action); }
            catch (Exception ex) { Compatibility.Block(action, ex, () => DisposeSlider(action)); }
        }
        if (_slidersAdded) { _slidersAdded = false; Refresh(); }
        if (_closingAt < 0 && IsOpen && !UpdateNotificationUi.ConsumedInput) HandleInput();
        if ((_scrollDragging || _scrollPending) && !Input.GetMouseButton(0)) EndScrollPress(false);
        PaintChildPills();
        UpdateScrollMarker();
        AnimateHelp();
        var saved = Time.unscaledTime - _changedAt < 1.6f ? ModText.Text("✓ 저장됨") : $"BetterAstralParty {Plugin.Version}";
        if (_savedText != saved) { _savedText = saved; Labels["Saved"].Set("text", saved); }
    }

    private static BepInEx.Configuration.ConfigEntry<float> SliderEntry(string action) => action switch
    {
        "FieldMarker" => Plugin.FieldIndicatorHeight, "IndicatorFadeStrength" => Plugin.IndicatorFadeStrength,
        "FoldedHandOpacity" => Plugin.FoldedHandOpacity, "Scale" => Plugin.UiScale,
        "ZoomSensitivity" => Plugin.ZoomSensitivity, "ZoomOutLimit" => Plugin.ZoomOutLimit, "IndicatorHideDistance" => Plugin.IndicatorHideDistance, _ => Plugin.Opacity
    };

    // Stored settings in whole percent; the slider shows them from the range minimum.
    private static int SliderValue(string action) => (int)Math.Round(100 * SliderEntry(action).Value);

    // Default marker (user-chosen 2026-10-27, replacing a dark line that clashed with the pink bar): a translucent copy of
    // the native star grip (Setting_Com_Slider_grip, the star only; the pink tail belongs to the bar) at the default value,
    // under the real grip so they coincide there. The grip follows the bar end, so the copy keeps the grip's y and sits
    // where the grip would be at the default share. Call after the slider shows its value, so bar and grip agree.
    private const float DefaultMarkAlpha = 0.4f;
    private static void AddDefaultMark(RuntimeObject slider, string action, int min, int max)
    {
        var bar = slider.Field("_barObjectH"); var grip = slider.Field("_gripObject");
        if (bar == null || grip == null || max <= min) return;
        var ratio = Math.Clamp(((float)Math.Round(100 * (float)SliderEntry(action).DefaultValue) - min) / (max - min), 0f, 1f);
        var mark = NativeUi.Create("Setting", "Setting_Com_Slider_grip");
        if (mark == null) return;
        mark.Set("touchable", false);
        mark.Set("alpha", DefaultMarkAlpha);
        mark.Call("SetXY", grip.Get<float>("x") - bar.Get<float>("width") + ratio * slider.Field<float>("_barMaxWidth"), grip.Get<float>("y"));
        slider.Call("AddChildAt", mark, slider.Call("GetChildIndex", grip)!.Value<int>());
    }

    // A slider drag would rewrite the whole settings file at every 1 % step; the file is saved once when the drag ends.
    private static bool? _sliderSave;
    private static void DeferSliderSave()
    {
        if (_sliderSave != null) return;
        var config = Plugin.UiScale.ConfigFile;
        _sliderSave = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
    }

    private static void FlushSliderSave()
    {
        var config = Plugin.UiScale.ConfigFile;
        config.SaveOnConfigSet = _sliderSave ?? true;
        _sliderSave = null;
        try { config.Save(); }
        catch (Exception ex) { Plugin.Diagnostics.Error("SettingsSliderSave", ex); }
        // The drag's own changes may have settled before this save reached the file; back up the saved value too.
        SettingsBackup.MarkChanged(Environment.TickCount64);
    }

    private static void SetSliderValue(string action, int percent)
    {
        SliderEntry(action).Value = percent * 0.01f;
    }

    private static IntPtr _sliderPackage;
    private static bool _slidersAdded;
    private static IntPtr SettingPackage() =>
        RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "UIPackage"), "GetByName", "Setting")?.Pointer ?? IntPtr.Zero;

    private static void DisposeSlider(string action)
    {
        if (!Sliders.Remove(action, out var slider)) return;
        Controls.Remove(slider.Pointer);
        GameUi.Dispose(slider);
    }

    // The native GSlider owns its drag input; the setting is read back by polling, never through a delegate.
    private static void TickSlider(string action)
    {
        var (min, max) = MenuLayout.SliderRange(action);
        if (!Sliders.TryGetValue(action, out var slider) || slider.Get<bool>("isDisposed"))
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!NativeUi.PackageReady("Setting")) return;
            slider = NativeUi.Create("Setting", "Setting_Com_Slider")!;
            slider.Call("SetScale", MenuLayout.SliderScale, MenuLayout.SliderScale);
            slider.Call("SetSize", MenuLayout.SliderBarWidth(action) / MenuLayout.SliderScale, slider.Get<float>("height"));
            slider.Set("max", (double)(max - min));
            slider.SetField("changeOnClick", true); // Public field on GSlider, not a property.
            _settingsContent!.Call("AddChild", slider);
            slider.Call("SetXY", MenuLayout.SliderBarX(action), MenuLayout.SettingY(action) + MenuLayout.SliderOffsetY(action));
            // GSlider redraws only when value changes; a stored value equal to the prefab's would keep the prefab bar.
            // Then load the saved setting at once: the poll below reads the slider back into the setting, so a slider
            // still at -1 would save the minimum (1.5.3-dev.15 to dev.17 reset every slider that way).
            slider.Set("value", -1d);
            slider.Set("value", (double)(SliderValue(action) - min));
            AddDefaultMark(slider, action, min, max);
            Sliders[action] = slider;
            Controls[slider.Pointer] = (slider, action);
            _sliderPackage = SettingPackage();
            _slidersAdded = true;
            _sliderMs += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            if (++_sliderCount == MenuLayout.Sliders.Length)
                Plugin.Diagnostics.State("settingsSliders.ms", FormattableString.Invariant($"total={_sliderMs:F1}; count={_sliderCount}"));
            return; // Read user input back only from the next frame, after Refresh has applied visibility and state.
        }
        if (!slider.Get<bool>("visible")) { if (_dragSlider == slider.Pointer) _dragSlider = IntPtr.Zero; return; }
        DragSlider(slider, action, min, max - min);
        NudgeSlider(slider, max - min);
        var value = min + (int)Math.Round(Math.Clamp(slider.Get<double>("value"), 0, max - min)); // 1% steps
        if (value == SliderValue(action)) return;
        DeferSliderSave();
        SetSliderValue(action, value);
        Labels[action + "Value"].Set("text", MenuLayout.Percent(value));
        _changedAt = Time.unscaledTime;
    }

    // Native GSlider drags only its grip; every press on the slider is followed here instead, keeping the grip under
    // the pointer and snapping to the default within a few pixels of the star marker. The native press still runs
    // (a bar press jumps there first); the native grip drag is switched off while the press lasts. A wide range packs
    // about 2 % into a pixel, so single steps come from the arrow keys (NudgeSlider); one step per pixel was tried and
    // left the grip behind the pointer (2026-10-27).
    private const float DefaultSnapPixels = 3f;
    private static void DragSlider(RuntimeObject slider, string action, int min, int range)
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (slider.Get<bool>("touchable") && GameUi.PointerPath(hitTest: true).Any(item => item.Pointer == slider.Pointer))
            {
                // FairyGUI handles the press in its LateUpdate, after this; read the start value next frame.
                _dragSlider = slider.Pointer; _dragX = TouchX(); _dragStart = double.NaN;
            }
            return;
        }
        if (!Input.GetMouseButton(0)) _dragSlider = IntPtr.Zero;
        if (_dragSlider != slider.Pointer) return;
        slider.SetField("canDrag", false); // Every native grip press sets it again.
        if (double.IsNaN(_dragStart)) _dragStart = slider.Get<double>("value");
        var bar = slider.Field<float>("_barMaxWidth");
        var pixels = slider.Call("LocalToGlobal", new Vector2(bar, 0))!.Value<Vector2>().x - slider.Call("LocalToGlobal", Vector2.zero)!.Value<Vector2>().x;
        if (pixels <= 0) return;
        var step = range / pixels;
        var next = _dragStart + (TouchX() - _dragX) * step;
        var fallback = Math.Round(100 * (float)SliderEntry(action).DefaultValue) - min;
        next = Math.Clamp(Math.Abs(next - fallback) <= DefaultSnapPixels * step ? fallback : Math.Round(next), 0, range);
        if (next != slider.Get<double>("value")) slider.Set("value", next);
    }

    // Left/right arrows move the hovered slider one step, repeating while held.
    private static float _nudgeAt;
    private static void NudgeSlider(RuntimeObject slider, int range)
    {
        var left = Input.GetKey(KeyCode.LeftArrow);
        if (left == Input.GetKey(KeyCode.RightArrow) || _dragSlider != IntPtr.Zero || !slider.Get<bool>("touchable")) return;
        var pressed = Input.GetKeyDown(left ? KeyCode.LeftArrow : KeyCode.RightArrow);
        if (!pressed && Time.unscaledTime < _nudgeAt) return;
        if (GameUi.PointerPath(hitTest: true).All(item => item.Pointer != slider.Pointer)) return;
        _nudgeAt = Time.unscaledTime + (pressed ? 0.4f : 0.05f);
        var next = Math.Clamp(Math.Round(slider.Get<double>("value")) + (left ? -1 : 1), 0, range);
        if (next != slider.Get<double>("value")) slider.Set("value", next);
    }

    private static float TouchX() => RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!.Get<Vector2>("touchPosition").x;

    private static RuntimeObject Button(string action, string item, string title, float x, float y, float width)
    {
        var button = NativeUi.Create("Common", item) ?? throw new InvalidOperationException($"원본 버튼 없음: {item}");
        var scale = width / button.Get<float>("width");
        button.Call("SetScale", scale, scale);
        NativeUi.Position(button, x, y, scale);
        Title(button, title);
        // Scale the glyph size with the control, not down to an unreadable tiny +/-.
        if (width <= 80) Title(button, title, (int)MathF.Round(52 * 80 / width));
        button.SetField("changeStateOnClick", false);
        _sheet!.Call("AddChild", button);
        Controls[button.Pointer] = (button, action);
        return button;
    }

    private static void Create()
    {
        if (_modal != null && !_modal.Get<bool>("isDisposed")) return;
        Controls.Clear(); Labels.Clear(); ToggleBackgrounds.Clear(); SectionHeaders.Clear(); Trays.Clear(); ChildPills.Clear(); ChildPillStates.Clear(); Titles.Clear();
        _savedText = ""; _layout = null;
        TraceSettingsOpen(DiagnosticPhase.SettingsModal);
        _modal = NativeUi.Component(GameUi.Root!, 1920, 1080);
        _modal.Set("sortingOrder", 30000);
        _modal.Set("opaque", true);
        _modal.Set("visible", false);
        GameUi.AddRoundedRect(_modal, 1920, 1080, 0, new Color(0.015f, 0.01f, 0.03f, 0.7f));
        _backdrop = _modal.Call("GetChildAt", 0)!;
        _sheet = NativeUi.Component(_modal, MenuLayout.Width, MenuLayout.Height);
        TraceSettingsOpen(DiagnosticPhase.SettingsFrame);
        var frame = NativeUi.Create("Common", "Com_PopUpWindow_Bottom")
            ?? throw new InvalidOperationException("원본 설정 프레임 준비 대기");
        _sheet.Call("AddChild", frame);
        frame.Set("touchable", false);
        GrowFrame(frame, MenuLayout.Height);
        var heading = frame.Call("GetChild", "title")!;
        heading.Set("text", ModText.Text("모드 설정"));
        GameUi.StyleText(heading, 42);
        foreach (var name in new[] { "closeButton", "btn_Sure", "btn_Sure_Only", "btn_Cancel" })
            frame.Call("GetChild", name)?.Set("visible", false);

        TraceSettingsOpen(DiagnosticPhase.SettingsHelp);
        var helpArea = NativeUi.Component(_sheet, 360, MenuLayout.HelpHeight);
        helpArea.Call("SetXY", 45f, MenuLayout.HelpY);
        helpArea.Set("touchable", false);
        _ = new NativeUi.Surface(helpArea, 360, MenuLayout.HelpHeight, fixedOpacity: true);
        Labels["HelpTitle"] = NativeUi.Label(helpArea, "", 18, 14, 324, 40, 27);
        Labels["HelpTitle"].Set("singleLine", true);
        Labels["HelpTitle"].Set("autoSize", 3);
        // Location tag: a dark capsule with a map pin, left-aligned between the title and the body.
        _helpTag = NativeUi.Component(helpArea, MenuLayout.HelpTagMaxWidth, MenuLayout.HelpTagHeight);
        _helpTag.Call("SetXY", 18f, 54f);
        _helpTag.Set("touchable", false);
        _helpTagFill = Shape(_helpTag, 0, 0, MenuLayout.HelpTagMaxWidth, MenuLayout.HelpTagHeight, new Color(0.353f, 0.275f, 0.196f), 13f, 13f, 13f, 13f);
        Shape(_helpTag, 8, 20, 13, 4, new Color(0.114f, 0.71f, 0.573f), 2f, 2f, 2f, 2f); // Pin shadow.
        var drop = Shape(_helpTag, 8.5f, 3.5f, 12, 12, new Color(0.91f, 0.278f, 0.29f), 6f, 6f, 6f, 0f);
        drop.Call("SetPivot", 0.5f, 0.5f, false);
        drop.Set("rotation", 45f); // The square corner points down: a teardrop pin.
        Shape(_helpTag, 12.5f, 7.5f, 4, 4, Color.white, 2f, 2f, 2f, 2f);
        Labels["HelpTag"] = NativeUi.Label(_helpTag, "", 26, 0, MenuLayout.HelpTagMaxWidth - 36, MenuLayout.HelpTagHeight, 17);
        Labels["HelpTag"].Set("singleLine", true);
        Labels["HelpTag"].Set("autoSize", 3);
        GameUi.StyleText(Labels["HelpTag"], 17, align: 0);
        Labels["HelpTag"].Set("verticalAlign", 1);
        _helpViewport = NativeUi.Component(helpArea, 324, MenuLayout.HelpHeight - 96);
        _helpViewport.Call("SetXY", 18f, 80f);
        _helpViewport.Set("touchable", false);
        _helpViewport.Call("SetupOverflow", 1); // Native Hidden: clip only the scrolling body.
        Labels["HelpBody"] = NativeUi.Label(_helpViewport, "", 0, 0, 324, MenuLayout.HelpHeight - 96, 22);
        Labels["HelpBody"].Set("singleLine", false);
        Labels["HelpBody"].Set("UBBEnabled", false); // Verified native property; mod-owned GTextField, never rich text.
        Labels["HelpBody"].Set("autoSize", 2); // Fixed width, wrapped auto-height.
        GameUi.StyleText(Labels["HelpBody"], 22, align: 0);
        _helpAction = null;
        SetHelp("");
        TraceSettingsOpen(DiagnosticPhase.SettingsTabs);
        var features = Button("Features", "Button_ReturnRounded", ModText.Text("모드 기능"), 55, MenuLayout.TabY, 340);
        var generalY = MenuLayout.NextTabY(MenuLayout.TabY, features.Get<float>("height") * features.Get<float>("scaleY"));
        Button("General", "Button_ReturnRounded", ModText.Text("일반 설정"), 55, generalY, 340);
        // Reuse a private native scroll-list clone: wheel, clipping and hit testing
        // stay with FairyGUI. One content component preserves our existing two-tab layout.
        // Common is already required by this window; no dependency on opening native Settings first.
        TraceSettingsOpen(DiagnosticPhase.SettingsScrollTemplate);
        var scrollTemplate = NativeUi.Create("Common", "Com_BuffInfo")
            ?? throw new InvalidOperationException("Native settings scroll list unavailable");
        try
        {
            TraceSettingsOpen(DiagnosticPhase.SettingsScrollDetach);
            _settingsView = scrollTemplate.Call("GetChild", "list_Buff")!;
            _settingsView.Get("relations")!.Call("ClearAll");
            _sheet.Call("AddChild", _settingsView);
        }
        finally { TraceSettingsOpen(DiagnosticPhase.SettingsScrollDispose); GameUi.Dispose(scrollTemplate); }
        TraceSettingsOpen(DiagnosticPhase.SettingsScrollConfigure);
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
        _scrollTrack = new NativeUi.Surface(_sheet, MenuLayout.TrackWidth, MenuLayout.SettingsHeight,
            fixedOpacity: true, fill: new Color(0.75f, 0.75f, 0.75f)).Graph;
        _scrollThumb = new NativeUi.Surface(_sheet, MenuLayout.TrackWidth, 24,
            fixedOpacity: true, fill: new Color(1f, 0.8f, 0f)).Graph;
        _scrollTrack.Call("SetXY", MenuLayout.SettingsX + MenuLayout.TrackX, MenuLayout.SettingsY);
        // The marker overlaps the list's right edge: it takes the press, so the list never starts its own drag.
        _scrollTrack.Set("touchable", true);
        _scrollThumb.Set("touchable", true);
        _scrollMarker = null;
        Ticks.Clear(); TickPositions.Clear(); Bubbles.Clear(); _tickLayout = null; _tickCurrent = _tickHot = -1; _flashBand = null; _bubblesShown = false;
        for (var i = 0; i < MenuLayout.Sections.Length; i++)
        {
            // Dark dot with a cream ring: readable on the gray track and on the yellow thumb alike.
            var dot = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            dot.Set("touchable", false); dot.Set("visible", false);
            dot.Call("SetSize", 12f, 12f);
            dot.Call("SetPivot", 0.5f, 0.5f, true);
            // A fully rounded rect: this game's Shape has no 3-argument DrawEllipse.
            dot.Get("shape")!.Call("DrawRoundRect", 2f, TickRing, Color.white, 6f, 6f, 6f, 6f);
            dot.Set("color", TickDark);
            _sheet!.Call("AddChild", dot);
            Ticks.Add((i, dot));
        }
        for (var i = 0; i < MenuLayout.Sections.Length; i++) Bubbles.Add(new CategoryBubble(_sheet!, i));
        // Transparent press band over the track and its margins, so the native list never starts its own drag there.
        _trackHit = NativeUi.Component(_sheet!, MenuLayout.TrackWidth + 2 * MenuLayout.TrackPad, MenuLayout.SettingsHeight);
        _trackHit.Set("opaque", true); _trackHit.Set("touchable", true);
        _trackHit.Call("SetXY", MenuLayout.SettingsX + MenuLayout.TrackX - MenuLayout.TrackPad, MenuLayout.SettingsY);
        TraceSettingsOpen(DiagnosticPhase.SettingsSteppers);
        Sliders.Clear(); SliderBlocked.Clear(); SliderBacks.Clear(); _dragSlider = IntPtr.Zero;
        foreach (var action in MenuLayout.Sliders)
        {
            Labels[action] = NativeUi.Label(_sheet!, ModText.Text(MenuLayout.SliderLabel(action)), MenuLayout.SettingsX + MenuLayout.SliderLabelX(action), 0, 98, 35, 22);
            Labels[action].Set("autoSize", 3);
            Labels[action].Set("singleLine", true);
            // One line, shrinking instead of wrapping the % sign; "150%" is the widest value.
            Labels[action + "Value"] = NativeUi.Label(_sheet!, "", MenuLayout.SettingsX + MenuLayout.SliderValueX(action), 0, 58, 35, 22);
            Labels[action + "Value"].Set("singleLine", true);
            Labels[action + "Value"].Set("autoSize", 3);
            AddToggle(action, 440, MenuLayout.ToggleY(6)); // Shown only while the slider is unavailable.
            SliderBlocked[action] = Controls.Values.Last(control => control.Action == action).Button;
        }
        AddToggle("Language", 440, 125);
        AddToggle("Diagnostics", 440, 190);
        AddToggle("MuteUnfocused", 440, 255);
        AddToggle("InputAttention", 440, 320);
        AddToggle("MatchFocus", 440, MenuLayout.ToggleY(4));
        AddToggle("UpdateChannel", 440, MenuLayout.ToggleY(7));
        AddToggle("Update", 440, MenuLayout.ToggleY(8));
        AddToggle("UpdateDownload", 440, MenuLayout.ToggleY(9));
        AddToggle("AutoUpdate", 440, MenuLayout.ToggleY(10));
        AddToggle("UpdateAuth", 440, MenuLayout.ToggleY(11));
        AddToggle("UpdateSignOut", 440, MenuLayout.ToggleY(12));
        AddToggle("DiagnosticsOpen", 440, MenuLayout.ToggleY(13));
        AddToggle("DiagnosticsCollect", 440, MenuLayout.ToggleY(14));
        AddToggle("SuppressNotices", 440, MenuLayout.ToggleY(15));
        AddToggle("UpdateNotifications", 440, MenuLayout.ToggleY(16));
        AddToggle("SettingsAutoKeep", 440, MenuLayout.ToggleY(17));
        AddToggle("SettingsSave", 440, MenuLayout.ToggleY(18));
        AddToggle("SettingsLoad", 440, MenuLayout.ToggleY(19));
        AddToggle("SettingsFolder", 440, MenuLayout.ToggleY(20));
        AddToggle("SettingsReset", 440, MenuLayout.ToggleY(21));
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
        AddToggle("ShushuShieldRestrictions", 440, MenuLayout.ToggleY(10));
        AddToggle("AutoThanks", 440, MenuLayout.ToggleY(11));
        AddToggle("CharacterRoles", 440, MenuLayout.ToggleY(12));
        AddToggle("CharacterGuide", 440, MenuLayout.ToggleY(13));
        AddToggle("IndicatorFade", 440, MenuLayout.ToggleY(14));
        AddToggle("IndicatorHide", 440, MenuLayout.ToggleY(15));
        AddToggle("CounterSound", 440, MenuLayout.ToggleY(16));
        AddToggle("CounterAnimation", 440, MenuLayout.ToggleY(17));
        AddToggle("PingFocus", 440, MenuLayout.ToggleY(18));
        AddToggle("FinalDamage", 440, MenuLayout.ToggleY(19));
        AddToggle("DiceLock", 440, MenuLayout.ToggleY(20));
        AddToggle("EncounterAdvice", 440, MenuLayout.ToggleY(21));
        AddToggle("DefenseAdvice", 440, MenuLayout.ToggleY(22));
        AddToggle("CardAdvice", 440, MenuLayout.ToggleY(23));
        AddToggle("ShootingStarShow", 440, MenuLayout.ToggleY(24));
        AddToggle("KoCardPreview", 440, MenuLayout.ToggleY(25));
        AddToggle("KoSpectator", 440, MenuLayout.ToggleY(26));
        AddToggle("StatusHand", 440, MenuLayout.ToggleY(27));
        AddToggle("StatusEffects", 440, MenuLayout.ToggleY(28));
        AddToggle("CounterIcons", 440, MenuLayout.ToggleY(29));
        AddToggle("ShushuMaster", 440, MenuLayout.ToggleY(30));
        AddToggle("PlayerTab", 440, MenuLayout.ToggleY(31));
        AddToggle("PlayerTabHand", 440, MenuLayout.ToggleY(32));
        AddToggle("PlayerTabRole", 440, MenuLayout.ToggleY(33));
        TraceSettingsOpen(DiagnosticPhase.SettingsReparent);
        foreach (var (button, action) in Controls.Values)
            if (!MenuLayout.FixedControl(action)) MoveIntoSettings(button, MenuLayout.SettingY(action) + MenuLayout.ChildOffsetY(action));
        // Child switch pills: a gray ring and a black-outlined pill in the switch's place, behind it.
        // Made before slider backs and trays, which go further back, so a capsule never covers its switch's pill.
        foreach (var (button, action) in Controls.Values)
        {
            if (MenuLayout.Parent(action) == null || MenuLayout.IsSlider(action) || MenuLayout.FixedControl(action)) continue;
            float x = 10 + MenuLayout.Indent(action), y = MenuLayout.SettingY(action) + MenuLayout.ChildOffsetY(action);
            float w = MenuLayout.ToggleWidth(action), h = MenuLayout.ToggleHeightFor(action), r = h / 2;
            var parts = new[]
            {
                Shape(_settingsContent!, x - 3, y - 3, w + 6, h + 6, ChildRing, r + 3, r + 3, r + 3, r + 3),
                Shape(_settingsContent!, x, y, w, h, ChildOn, r, r, r, r, 3.5f, Color.black)
            };
            foreach (var part in parts.Reverse()) _settingsContent!.Call("SetChildIndex", part, 0);
            ChildPills.Add((button, ToggleBackgrounds[action], parts));
        }
        foreach (var section in MenuLayout.Sections)
            for (var i = 0; i < section.Actions.Length; i++)
            {
                var action = section.Actions[i];
                if (MenuLayout.IsSlider(action) || MenuLayout.SubParent(action) is not { } sub
                    || i > 0 && MenuLayout.SubParent(section.Actions[i - 1]) == sub) continue;
                var last = action;
                for (var j = i + 1; j < section.Actions.Length && MenuLayout.SubParent(section.Actions[j]) == sub; j++) last = section.Actions[j];
                var subTop = MenuLayout.SettingY(sub);
                var capsule = Shape(_settingsContent!, 56, subTop - 2, 388, MenuLayout.SettingY(last) - subTop + 46,
                    new Color(1f, 0.973f, 0.894f), 18f, 18f, 18f, 18f, 2f, new Color(0.886f, 0.827f, 0.682f));
                _settingsContent!.Call("SetChildIndex", capsule, 0);
                Trays.Add((new[] { capsule }, MenuLayout.Parent(sub)!)); // Shown and dimmed with the tray it sits in.
            }
        foreach (var action in MenuLayout.Sliders)
        {
            MoveIntoSettings(Labels[action], MenuLayout.SettingY(action) + MenuLayout.SliderLabelOffsetY(action));
            MoveIntoSettings(Labels[action + "Value"], MenuLayout.SettingY(action) + MenuLayout.SliderLabelOffsetY(action));
            // Every slider row sits on a switch-shaped pill so it reads like its sibling switches. A slider that tunes
            // a child switch gets a smaller pill, and one pale capsule wraps that switch and its slider together.
            // Shapes go to the back in creation order, so later ones land beneath earlier ones (and trays beneath all).
            var top = MenuLayout.SettingY(action);
            var pillLine = new Color(0.067f, 0.067f, 0.067f);
            var pill = MenuLayout.SliderPill(action);
            RuntimeObject[] parts;
            if (MenuLayout.SubParent(action) is { } sub)
            {
                var subTop = MenuLayout.SettingY(sub);
                parts = new[]
                {
                    Shape(_settingsContent!, pill.X, top - 2, pill.Width, 40, new Color(1f, 0.969f, 0.839f), 20f, 20f, 20f, 20f, 2f, pillLine),
                    Shape(_settingsContent!, 56, subTop - 2, 388, top - subTop + 46, new Color(1f, 0.973f, 0.894f), 18f, 18f, 18f, 18f, 2f, new Color(0.886f, 0.827f, 0.682f))
                };
            }
            else
            {
                var child = MenuLayout.Parent(action) != null;
                parts = new[]
                {
                    child ? Shape(_settingsContent!, pill.X, top + MenuLayout.ChildOffsetY(action), pill.Width, MenuLayout.ToggleHeightFor(action), new Color(1f, 0.941f, 0.702f), 23f, 23f, 23f, 23f, 2.5f, pillLine)
                        : Shape(_settingsContent!, pill.X, top, pill.Width, MenuLayout.ToggleHeight, new Color(1f, 0.941f, 0.702f), 30f, 30f, 30f, 30f, 3f, pillLine)
                };
            }
            foreach (var part in parts) _settingsContent!.Call("SetChildIndex", part, 0);
            SliderBacks.Add((parts, action));
        }
        // One pale tray per parent, tucked behind the parent switch, all behind the switches.
        foreach (var section in MenuLayout.Sections)
            for (var i = 0; i < section.Actions.Length; i++)
            {
                var parent = MenuLayout.Parent(section.Actions[i]);
                if (parent == null || i > 0 && MenuLayout.Parent(section.Actions[i - 1]) == parent) continue;
                var rows = 1;
                while (i + rows < section.Actions.Length && MenuLayout.Parent(section.Actions[i + rows]) == parent) rows++;
                var last = section.Actions[i + rows - 1];
                var top = MenuLayout.SettingY(section.Actions[i]);
                var parts = new[]
                {
                    Shape(_settingsContent!, MenuLayout.TrayX, top + MenuLayout.TrayTop, MenuLayout.TrayWidth, MenuLayout.TrayHeight(section.Actions[i], last),
                        new Color(0.945f, 0.902f, 0.796f), 0f, 0f, 16f, 16f, 2f, new Color(0.804f, 0.733f, 0.565f))
                };
                foreach (var part in parts) _settingsContent!.Call("SetChildIndex", part, 0);
                Trays.Add((parts, parent));
            }
        CreateSections();
        TraceSettingsOpen(DiagnosticPhase.SettingsFooter);
        Button("Close", "Button_ReturnRounded", ModText.Text("돌아가기"), 410, MenuLayout.FooterY, 235);
        Labels["Saved"] = NativeUi.Label(_sheet, "", 675, MenuLayout.FooterY + 7, 315, 30, 18);
        Labels["Saved"].Set("autoSize", 3);
        Labels["Saved"].Set("singleLine", true);
        TraceSettingsOpen(DiagnosticPhase.SettingsRefresh);
        Refresh();
        Plugin.Logger.LogInfo("[원본 모드 메뉴] 고정 크기 토글·호버 기능 안내 생성됨");
    }

    // Borderless mod-owned rounded shape (corner order: top-left, top-right, bottom-left, bottom-right).
    private static RuntimeObject Shape(RuntimeObject parent, float x, float y, float width, float height, Color fill,
        float topLeft, float topRight, float bottomLeft, float bottomRight, float line = 0f, Color? lineColor = null)
    {
        var graph = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
        graph.Set("touchable", false);
        parent.Call("AddChild", graph);
        graph.Call("SetSize", width, height);
        graph.Call("SetXY", x, y);
        graph.Get("shape")!.Call("DrawRoundRect", line, lineColor ?? Color.black, fill, topLeft, topRight, bottomLeft, bottomRight);
        return graph;
    }

    private static void PaintChildPills()
    {
        foreach (var (button, background, parts) in ChildPills)
        {
            var fill = background.Get<bool>("grayed") ? ChildOff : ChildOn;
            if (button.Pointer == _hovered && !Locked(Controls[button.Pointer].Action)) fill = Color.Lerp(fill, Color.white, 0.3f);
            var state = (button.Get<bool>("visible"), button.Get<float>("alpha"), fill);
            if (ChildPillStates.TryGetValue(button.Pointer, out var last) && last == state) continue;
            ChildPillStates[button.Pointer] = state;
            foreach (var part in parts) { part.Set("visible", state.Item1); part.Set("alpha", state.Item2); }
            parts[1].Set("color", fill);
        }
    }

    // Grows the native popup frame: SetSize lets its relations stretch the background; a full-height child the
    // relations left at the old bottom is stretched by hand.
    private static void GrowFrame(RuntimeObject frame, float height)
    {
        var native = frame.Get<float>("height");
        if (native >= height) return;
        var tall = new List<RuntimeObject>();
        for (var i = 0; i < frame.Get<int>("numChildren"); i++)
        {
            var item = frame.Call("GetChildAt", i)!;
            if (item.Get<float>("height") > native / 2 && item.Get<float>("y") + item.Get<float>("height") >= native - 2) tall.Add(item);
        }
        frame.Call("SetSize", frame.Get<float>("width"), height);
        foreach (var item in tall)
            if (item.Get<float>("y") + item.Get<float>("height") < height - 2)
                item.Call("SetSize", item.Get<float>("width"), item.Get<float>("height") + height - native);
    }

    private static void MoveIntoSettings(RuntimeObject item, float y)
    {
        var x = item.Get<float>("x") - MenuLayout.SettingsX;
        _settingsContent!.Call("AddChild", item);
        item.Call("SetXY", x, y);
    }

    private static void CreateSections()
    {
        for (var i = 0; i < MenuLayout.Sections.Length; i++)
        {
            var index = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TraceSettingsOpen(DiagnosticPhase.SettingsSections, index);
            var section = MenuLayout.Sections[i];
            var header = NativeUi.Component(_settingsContent!, 500, MenuLayout.SectionHeight);
            header.Call("SetXY", 0f, MenuLayout.SectionY(i));
            header.Set("touchable", false);
            TraceSettingsOpen(DiagnosticPhase.SettingsSections, index + ".label");
            TraceSettingsOpen(DiagnosticPhase.SettingsSections, index + ".separator");
            // A pale band across the row width marks the category; drawn once, it needs no text measurement.
            var band = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            band.Set("touchable", false);
            header.Call("AddChild", band);
            band.Call("SetSize", MenuLayout.SectionBandWidth, MenuLayout.SectionBandHeight);
            band.Call("SetXY", MenuLayout.SectionBandX, MenuLayout.SectionBandY);
            band.Get("shape")!.Call("DrawRoundRect", 0f, Color.black, new Color(0.45f, 0.33f, 0.18f, 0.16f), 8f, 8f, 8f, 8f); // Panel cream darkened by a translucent warm brown.
            var title = NativeUi.Label(header, ModText.Text(section.Title), MenuLayout.SectionBandX + 12f, MenuLayout.SectionBandY, MenuLayout.SectionBandWidth - 24f, MenuLayout.SectionBandHeight, MenuLayout.SectionTitleSize, dark: true);
            TraceSettingsOpen(DiagnosticPhase.SettingsSections, index + ".fit");
            title.Set("singleLine", true);
            title.Set("autoSize", 3);
            title.Set("touchable", false);
            TraceSettingsOpen(DiagnosticPhase.SettingsSections, index + ".style");
            GameUi.StyleText(title, MenuLayout.SectionTitleSize, align: 0);
            title.Set("verticalAlign", 1);
            SectionHeaders.Add((header, title, band, section.General));
        }
    }

    private static void UpdateScrollMarker()
    {
        var view = _settingsScroll!.Get<float>("viewHeight");
        var content = _settingsScroll.Get<float>("contentHeight");
        var (height, y) = MenuLayout.ScrollMarker(view, content, _settingsScroll.Get<float>("scrollingPosY"));
        var visible = content > view;
        UpdateTicks(view, content, visible, _settingsScroll.Get<float>("scrollingPosY"));
        if (_scrollMarker == (height, y, visible)) return;
        _scrollMarker = (height, y, visible);
        UpdateDropdownArrows();
        _scrollTrack!.Set("visible", visible);
        _scrollThumb!.Set("visible", visible);
        _scrollThumb.Call("SetSize", MenuLayout.TrackWidth, height);
        _scrollThumb.Call("SetXY", MenuLayout.SettingsX + MenuLayout.TrackX, MenuLayout.SettingsY + y);
        _trackHit?.Set("visible", visible);
    }

    private static void UpdateTicks(float view, float content, bool visible, float position)
    {
        if (_tickLayout != (view, content, _general, visible))
        {
            _tickLayout = (view, content, _general, visible);
            TickPositions.Clear();
            foreach (var (_, dot) in Ticks) dot.Set("visible", false);
            var shown = Ticks.Where(tick => MenuLayout.Sections[tick.Section].General == _general).ToList();
            var ys = MenuLayout.TickYs(shown.Select(tick => MenuLayout.JumpTarget(tick.Section, content, view)).ToArray(), content - view, view);
            var centre = MenuLayout.SettingsX + MenuLayout.TrackX + MenuLayout.TrackWidth / 2;
            for (var i = 0; i < shown.Count; i++)
            {
                var y = MenuLayout.SettingsY + ys[i];
                shown[i].Dot.Call("SetXY", centre, y);
                shown[i].Dot.Set("visible", visible);
                TickPositions.Add((shown[i].Section, y));
            }
            LayoutBubbles();
            _tickCurrent = _tickHot = -2;
        }
        var current = CurrentSection(position, content, view);
        if (current != _tickCurrent) { _tickCurrent = current; PaintTicks(); }
        if (_bubblesHideAt >= 0 && Time.unscaledTime >= _bubblesHideAt) ShowBubbles(false);
        for (var i = 0; i < TickPositions.Count; i++)
            Bubbles[TickPositions[i].Section].Animate(_bubblesShown, _bubblesShownAt + i * 0.018f - Time.unscaledTime, Time.unscaledDeltaTime);
        if (_flashBand != null)
        {
            // A jumped-to band glows briefly, then returns to its pale fill.
            var t = Math.Clamp((Time.unscaledTime - _flashAt) / 0.9f, 0f, 1f);
            _flashBand.Set("color", Color.Lerp(BandFlash, BandFill, t));
            if (t >= 1) _flashBand = null;
        }
    }

    // Each bubble sits at its own dot, right of the track; crowded runs alternate to the left (MenuLayout.BubbleSides).
    private static void LayoutBubbles()
    {
        var left = MenuLayout.BubbleSides(TickPositions.Select(tick => tick.Y).ToArray());
        var track = MenuLayout.SettingsX + MenuLayout.TrackX;
        foreach (var bubble in Bubbles) bubble.Hide();
        for (var i = 0; i < TickPositions.Count; i++)
        {
            var bubble = Bubbles[TickPositions[i].Section];
            bubble.Place(ModText.Text(MenuLayout.Sections[bubble.Section].Title), left[i] ? track : track + MenuLayout.TrackWidth, TickPositions[i].Y, left[i]);
        }
    }

    // Entering needs the track; once shown, the bubbles stay while the pointer is anywhere from the widest left bubble
    // to the widest right bubble, a bubble height above and below the track.
    private static bool InBubbleSpan(Vector2 local, float view)
    {
        float min = MenuLayout.SettingsX + MenuLayout.TrackX - MenuLayout.TrackPad, max = min + MenuLayout.TrackWidth + 2 * MenuLayout.TrackPad;
        foreach (var (section, _) in TickPositions) { min = Math.Min(min, Bubbles[section].X); max = Math.Max(max, Bubbles[section].X + Bubbles[section].Width); }
        return local.x >= min && local.x <= max
            && local.y >= MenuLayout.SettingsY - MenuLayout.BubbleHeight && local.y <= MenuLayout.SettingsY + view + MenuLayout.BubbleHeight;
    }

    private static void ShowBubbles(bool show)
    {
        _bubblesHideAt = -1;
        if (show == _bubblesShown) return;
        _bubblesShown = show;
        if (show) _bubblesShownAt = Time.unscaledTime;
        if (!show && _helpAction == "CategoryJump") SetHelp("");
        if (!show) { _tickHot = -1; PaintTicks(); }
    }

    private static void PaintTicks()
    {
        foreach (var (section, dot) in Ticks)
        {
            dot.Set("color", section == _tickCurrent ? TickCurrent : TickDark);
            var scale = section == _tickHot ? 1.4f : 1f;
            dot.Call("SetScale", scale, scale);
            Bubbles[section].SetFill(section == _tickHot ? BubbleHot : section == _tickCurrent ? BubbleCurrent : BubblePlain);
        }
    }

    private static int CurrentSection(float position, float content, float view)
    {
        var current = -1;
        foreach (var (section, _) in TickPositions)
            if (MenuLayout.JumpTarget(section, content, view) <= position + 1) current = section;
        return current < 0 && TickPositions.Count > 0 ? TickPositions[0].Section : current;
    }

    private static int TickAt(float y)
    {
        int best = -1; var distance = MenuLayout.TickHit;
        foreach (var (section, tickY) in TickPositions)
            if (Math.Abs(tickY - y) < distance) { distance = Math.Abs(tickY - y); best = section; }
        return best;
    }

    private static int BubbleAt(Vector2 point)
    {
        if (!_bubblesShown) return -1;
        foreach (var (section, _) in TickPositions)
            if (NativeUi.Contains(Bubbles[section].Root, point)) return section;
        return -1;
    }

    private static void JumpTo(int section)
    {
        var view = _settingsScroll!.Get<float>("viewHeight");
        _settingsScroll.Call("SetPosY", MenuLayout.JumpTarget(section, _settingsScroll.Get<float>("contentHeight"), view), true);
        _flashBand = SectionHeaders[section].Band; _flashAt = Time.unscaledTime;
    }

    // Ends a track press or drag at once; a still release on a dot or bubble jumps there.
    private static void EndScrollPress(bool jump)
    {
        if (jump && !_pressMoved && _pressTick >= 0) JumpTo(_pressTick);
        _scrollDragging = _scrollPending = false; _pressTick = -1;
    }

    private static void AddToggle(string action, float x, float y)
    {
        TraceSettingsOpen(DiagnosticPhase.SettingsControls, action);
        // A child switch is the native switch drawn at ChildScale: its rendered size is ToggleWidth x ToggleHeightFor.
        var child = MenuLayout.Parent(action) != null;
        var scale = child ? MenuLayout.ChildScale : 1f;
        var width = MenuLayout.ToggleWidth(action) / scale;
        var height = MenuLayout.ToggleHeightFor(action) / scale;
        x += MenuLayout.Indent(action);
        var toggle = Button(action, "Button_Switch_1", "", x, y, width);
        // Common-mode buttons never remain in the selected/down scaling state.
        // Modify only these new mod-owned instances, not the game's controls.
        toggle.Set("mode", 0);
        toggle.SetField("_downEffect", 1); // Native press tint, no size change.
        toggle.SetField("_downEffectValue", 0.88f);
        toggle.Call("SetPivot", 0f, 0f, true); // Tint-only controls retain top-left fixed-size layout.
        toggle.Call("SetScale", scale, scale);
        toggle.Call("SetSize", width, height);
        toggle.Call("SetXY", x, y);
        var background = toggle.Call("GetChild", "n3")
            ?? throw new InvalidOperationException("설정 버튼 배경 준비 대기");
        background.Call("SetSize", width, height);
        background.Set("touchable", false);
        ToggleBackgrounds[action] = background;
        if (child) background.Set("visible", false); // The mod pill behind the switch carries its look (ChildPills).
        var title = toggle.Call("GetChild", "title")!;
        title.Set("autoSize", 3); // Native Shrink: fit translations without expanding the button.
        var dropdown = MenuLayout.Options(action).Length > 2;
        title.Call("SetXY", 12f, 6f);
        title.Call("SetSize", width - (dropdown ? 64 : 24), height - 12);
        if (dropdown) Labels["Arrow." + action] = NativeUi.Label(toggle, "▼", width - 46, 12, 32, 36, 22, dark: true);
    }

    internal static void TogglePanel()
    {
        if (!Compatibility.Allowed("SettingsUi")) return;
        try { TogglePanelCore(); }
        catch (Exception ex) { FailSettings(ex); }
    }

    // Open cost per phase (ms), written as one settingsOpen.ms line while diagnostics are ON, to find what makes the
    // first open slow. Each phase's time runs until the next phase mark.
    private static readonly System.Diagnostics.Stopwatch OpenWatch = new();
    private static readonly Dictionary<DiagnosticPhase, double> OpenCosts = new();
    private static DiagnosticPhase _openPhase;
    private static double _sliderMs;
    private static int _sliderCount;

    private static void MarkOpenPhase(DiagnosticPhase stage)
    {
        if (!OpenWatch.IsRunning) return;
        OpenCosts[_openPhase] = OpenCosts.GetValueOrDefault(_openPhase) + OpenWatch.Elapsed.TotalMilliseconds;
        OpenWatch.Restart();
        _openPhase = stage;
        if (stage != DiagnosticPhase.SettingsReturn) return;
        OpenWatch.Stop();
        Plugin.Diagnostics.State("settingsOpen.ms", FormattableString.Invariant($"total={OpenCosts.Values.Sum():F1}; ")
            + string.Join("; ", OpenCosts.OrderByDescending(cost => cost.Value).Select(cost => FormattableString.Invariant($"{cost.Key}={cost.Value:F1}"))));
    }

    // Fixed mod-owned phases only; record BEFORE native work even when detailed diagnostics are OFF.
    private static void TraceSettingsOpen(DiagnosticPhase stage, string detail = "")
    {
        MarkOpenPhase(stage);
        DiagnosticHub.Stage(DiagnosticFeature.Settings, stage, DiagnosticOutcome.Observed);
        DiagnosticHub.FlushSoon();
        try { Plugin.Logger.LogInfo("[Mod settings] open stage=" + stage + (detail.Length == 0 ? "" : "; item=" + detail)); }
        catch { } // Diagnostic storage must not prevent opening settings.
    }

    private static void TogglePanelCore()
    {
        if (!CanOpen || GameUi.Root == null) return;
        TraceSettingsOpen(DiagnosticPhase.SettingsNoticeClose);
        UpdateNotificationUi.CloseForSettings();
        TraceSettingsOpen(DiagnosticPhase.SettingsOpenState);
        if (IsOpen) { ClosePanel(); return; }
        OpenCosts.Clear(); _openPhase = DiagnosticPhase.UiAction; OpenWatch.Restart(); _sliderMs = 0; _sliderCount = 0;
        DiagnosticHub.UiAction(DiagnosticUiAction.Open, DiagnosticOutcome.Begin, default);
        DiagnosticHub.FlushSoon();
        TraceSettingsOpen(DiagnosticPhase.UiAction);
        var updateNotice = Plugin.Updates.NotificationVersion != null;
        if (updateNotice && GameUi.HomeAvailable) _general = true;
        Create();
        // The game may unload the Setting package between scenes; rebuild the sliders only when that package is gone or
        // was reloaded since they were made, not on every open.
        if (SettingPackage() != _sliderPackage) foreach (var action in MenuLayout.Sliders) DisposeSlider(action);
        _closingAt = -1;
        _openedAt = Time.unscaledTime;
        TraceSettingsOpen(DiagnosticPhase.SettingsShow);
        _modal!.Set("visible", true);
        Plugin.Diagnostics.State("settingsPanel", "open");
        GameUi.SetSettingsModal(true);
        _pressed = _hovered = IntPtr.Zero;
        TraceSettingsOpen(DiagnosticPhase.SettingsScrollTop);
        _settingsScroll!.Call("ScrollTop", false);
        _scrollPosition = 0;
        _helpAction = null;
        TraceSettingsOpen(DiagnosticPhase.SettingsHelpContent);
        SetHelp(updateNotice ? "UpdateCheck" : "");
        TraceSettingsOpen(DiagnosticPhase.SettingsRefresh);
        Refresh();
        TraceSettingsOpen(DiagnosticPhase.SettingsLayout);
        Layout();
        if (updateNotice) Plugin.Updates.AcknowledgeNotification();
        TraceInput("open", IntPtr.Zero);
        TraceSettingsOpen(DiagnosticPhase.SettingsReturn);
        DiagnosticHub.UiAction(DiagnosticUiAction.Open, DiagnosticOutcome.Completed, default);
        DiagnosticHub.FlushSoon();
    }

    // Try Again from the update notice: open Update Settings and re-enable updates, exactly as its Update button does.
    internal static void RetryUpdateFromNotice()
    {
        OpenUpdateSettings();
        if (!Compatibility.Allowed("SettingsUi") || !IsOpen) return;
        if (Plugin.Automatic.CanReactivate) Plugin.Automatic.Reactivate(); else if (Plugin.Automatic.CanRecheck) Plugin.Automatic.Recheck();
        Refresh();
    }

    internal static void OpenUpdateSettings()
    {
        if (!Compatibility.Allowed("SettingsUi")) return;
        try
        {
            if (!GameUi.HomeAvailable || GameUi.Root == null) return;
            _general = true;
            if (!IsOpen) TogglePanel();
            if (!Compatibility.Allowed("SettingsUi")) return;
            _closingAt = -1;
            _settingsScroll!.Call("ScrollTop", false);
            Refresh();
            _helpAction = null;
            SetHelp("UpdateCheck");
            Plugin.Updates.AcknowledgeNotification();
        }
        catch (Exception ex) { FailSettings(ex); }
    }

    // A feature cleanup run from a settings action fails into that feature's breaker, not the settings panel's.
    private static void Isolated(string feature, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception ex) { Compatibility.Block(feature, ex); }
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
        var point = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!
            .Get<Vector2>("touchPosition");
        if (DragScrollMarker(point)) { _pressed = IntPtr.Zero; return; }
        var hit = GameUi.PointerPath().FirstOrDefault(item => Controls.ContainsKey(item.Pointer));
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
        if (current != IntPtr.Zero && current == _pressed && !hit!.Get<bool>("grayed") && !Locked(Controls[current].Action)) Apply(Controls[current].Action);
        _pressed = IntPtr.Zero;
    }

    private static bool _scrollDragging;
    private static float _scrollGrab;

    // The scroll marker is mod-drawn art; while it is held, move the native scroll pane to match.
    private static bool DragScrollMarker(Vector2 point)
    {
        if (!_scrollTrack!.Get<bool>("visible")) { EndScrollPress(false); ShowBubbles(false); return false; }
        var local = _sheet!.Call("GlobalToLocal", point)!.Value<Vector2>();
        var view = _settingsScroll!.Get<float>("viewHeight");
        var thumbY = _scrollThumb!.Get<float>("y");
        var thumbHeight = _scrollThumb.Get<float>("height");
        var onTrack = MenuLayout.OnScrollTrack(local.x, local.y, view);
        var bubble = onTrack ? -1 : BubbleAt(point);
        if (Input.GetMouseButtonDown(0) && (onTrack || bubble >= 0))
        {
            // A new press always stops a running jump so the list never keeps moving under the pointer.
            _settingsScroll.Call("SetPosY", _settingsScroll.Get<float>("scrollingPosY"), false);
            _pressY = local.y; _pressMoved = false; _pressOnTrack = onTrack;
            _pressTick = onTrack ? TickAt(local.y) : bubble;
            var onThumb = local.y >= thumbY && local.y <= thumbY + thumbHeight;
            // The thumb drags at once and keeps its offset; a dot or bubble waits to see whether the press moves;
            // the rest of the track centres the thumb there as before.
            if (onTrack && onThumb) { _scrollDragging = true; _scrollGrab = local.y - thumbY; }
            else if (_pressTick >= 0) _scrollPending = true;
            else { _scrollDragging = true; _scrollGrab = thumbHeight / 2; }
        }
        var pressing = _scrollDragging || _scrollPending;
        if (pressing && Math.Abs(local.y - _pressY) >= MenuLayout.TickSlop) _pressMoved = true;
        // A moving press on the track becomes a drag; one that started on a bubble just stops counting as a click.
        if (_scrollPending && _pressMoved && _pressOnTrack) { _scrollPending = false; _scrollDragging = true; _scrollGrab = thumbHeight / 2; }
        var hot = bubble >= 0 ? bubble : onTrack ? TickAt(local.y) : -1;
        if (onTrack || pressing) ShowBubbles(true);
        if (_bubblesShown && (onTrack || pressing || InBubbleSpan(local, view)))
        {
            _bubblesHideAt = -1;
            if (_tickHot != hot) { _tickHot = hot; PaintTicks(); }
            SetHelp("CategoryJump");
        }
        else if (_bubblesShown && _bubblesHideAt < 0) _bubblesHideAt = Time.unscaledTime + 0.15f;
        if (!pressing) return false;
        if (!Input.GetMouseButton(0)) { EndScrollPress(true); return true; }
        if (!_scrollDragging) return true;
        _settingsScroll.Call("SetPosY", MenuLayout.ScrollFromMarker(local.y - _scrollGrab, view,
            _settingsScroll.Get<float>("contentHeight"), thumbHeight), false);
        return true;
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

    private static float _loadArmedUntil, _resetArmedUntil;

    // The automatic copy follows the saved game-side file shortly after a burst of changes.
    private static void TickSettingsBackup()
    {
        if (!SettingsBackup.AutoDue(Environment.TickCount64) || !Plugin.SettingsAutoKeep.Value) return;
        try { SettingsBackup.Copy(Plugin.Settings.ConfigFilePath, Path.Combine(SettingsBackup.Folder, SettingsBackup.AutoName)); }
        catch (Exception ex) { Plugin.Logger.LogWarning("[설정 보관] 자동 보관 실패: " + ex.GetType().Name); }
    }

    // The single update button acts as whichever existing update action applies now.
    private static string UpdateRole() =>
        UpdateNotificationUi.CanConfirmTransition ? "UpdateSwitch"
        : Plugin.Automatic.CanApply ? "UpdateApply"
        : Plugin.Automatic.CanCancel ? "UpdateCancel"
        : Plugin.Automatic.CanDownload || Plugin.Automatic.CanReactivate || Plugin.Automatic.CanRecheck ? "UpdateGet"
        : Plugin.Automatic.Status is AutomaticUpdateStatus.Idle or AutomaticUpdateStatus.NotConfigured or AutomaticUpdateStatus.Cancelled ? "UpdateCheck" : "UpdateGet";

    private static string AutoUpdateMode() => MenuLayout.AutoUpdateMode(Plugin.AutomaticChecks.Value, Plugin.Automatic.AutoDownload, Plugin.Automatic.ApplyAfterExit);

    // Locked: a parent or the switch an option tunes is OFF. The option dims, ignores clicks and drags, and keeps its value.
    private static bool Locked(string action) => MenuLayout.Parent(action) is { } parent && !ToggleEnabled(parent)
        || MenuLayout.SubParent(action) is { } sub && !ToggleEnabled(sub);

    private static bool ToggleEnabled(string action) => action switch
    {
        "Enabled" => Plugin.Enabled.Value,
        "KoMinimum" => Plugin.KoMinimum.Value,
        "CardPopups" => Plugin.CardPopups.Value,
        "HandLayout" => Plugin.GroupHand.Value,
        "BattleStatus" => Plugin.BattleStatus.Value,
        "ShushuShield" => Plugin.ShushuShield.Value,
        "ShushuShieldRestrictions" => Plugin.ShushuShieldRestrictions.Value,
        "AutoThanks" => Plugin.AutoThanks.Value,
        "CharacterRoles" => Plugin.CharacterRoles.Value,
        "CharacterGuide" => Plugin.CharacterGuide.Value,
        "PlayerTab" => Plugin.PlayerTab.Value,
        "PlayerTabHand" => Plugin.PlayerTabHand.Value,
        "PlayerTabRole" => Plugin.PlayerTabRole.Value,
        "IndicatorFade" => Plugin.IndicatorFade.Value,
        "IndicatorHide" => Plugin.IndicatorHide.Value,
        "DiceLock" => Plugin.DiceLock.Value,
        "EncounterAdvice" => Plugin.EncounterAdvice.Value,
        "DefenseAdvice" => Plugin.DefenseAdvice.Value,
        "CardAdvice" => Plugin.CardAdvice.Value,
        "ShootingStarShow" => Plugin.ShootingStarShow.Value,
        "KoCardPreview" => Plugin.KoCardPreview.Value,
        "KoSpectator" => Plugin.KoSpectator.Value,
        "StatusHand" => Plugin.StatusHand.Value,
        "StatusEffects" => Plugin.StatusEffects.Value,
        "CounterIcons" => Plugin.CounterIcons.Value,
        "ShushuMaster" => Plugin.ShushuMaster.Value,
        "CounterSound" => Plugin.CounterSound.Value,
        "CounterAnimation" => Plugin.CounterAnimation.Value,
        "PingFocus" => Plugin.PingFocus.Value,
        "SuppressNotices" => Plugin.SuppressNotices.Value,
        "AutoUpdate" => AutoUpdateMode() != "Off",
        "Update" or "UpdateChannel" => true,
        "UpdateNotifications" => Plugin.UpdateNotifications.Value,
        "FinalDamage" => Plugin.FinalDamage.Value,
        "FieldBuffs" => Plugin.FieldBuffs.Value,
        "FieldZoom" => Plugin.FieldZoom.Value,
        "Names" => Plugin.UseRealNames.Value,
        "Diagnostics" => Plugin.DiagnosticLogging.Value,
        "MuteUnfocused" => Plugin.MuteUnfocused.Value,
        "SettingsAutoKeep" => Plugin.SettingsAutoKeep.Value,
        "MatchFocus" => Plugin.MatchFocus.Value,
        "InputAttention" => InputAttentionState.Normalize(Plugin.InputAttention.Value) != "Off",
        _ => Plugin.ShowDetails.Value
    };

    private static void SetHelp(string action, string? option = null)
    {
        if (action == "Update") action = UpdateRole();
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
            "AutoUpdate" => AutoUpdateMode(),
            _ => ""
        };
        body = option == null ? MenuLayout.StateHelp(action, ToggleEnabled(action), mode)
            : MenuLayout.OptionHelp(action, option);
        if (option != null) title = MenuLayout.OptionLabel(action, option);
        body = Compatibility.Reason(action) ?? body;
        Labels["HelpTitle"].Set("text", action is "UpdateAuth" or "UpdateSignOut" ? ModText.SessionTitle(action) : ModText.Text(title));
        var tag = MenuLayout.Subtitle(action);
        Labels["HelpTag"].Set("text", tag == null ? "" : ModText.Text(tag));
        _helpTag!.Set("visible", tag != null);
        if (tag != null)
        {
            // The capsule follows the rendered text width.
            var width = Math.Min(MenuLayout.HelpTagMaxWidth, 26 + Labels["HelpTag"].Get<float>("textWidth") + 12);
            _helpTagFill!.Call("SetSize", width, MenuLayout.HelpTagHeight);
            _helpTagFill.Get("shape")!.Call("DrawRoundRect", 0f, Color.black, new Color(0.353f, 0.275f, 0.196f), 13f, 13f, 13f, 13f);
        }
        Labels["HelpBody"].Set("text", action is "UpdateAuth" or "UpdateSignOut" ? ModText.SessionDetails(Plugin.UpdateSession?.Status ?? ReleaseSessionStatus.NotConfigured) : action == "UpdateCheck"
            ? ModText.UpdateDetails(Plugin.Updates.Result, Plugin.Version) : action is "UpdateGet" or "UpdateApply" or "UpdateCancel" or "UpdateSwitch"
                ? ModText.AutomaticDetails(Plugin.Automatic.Status, Plugin.Automatic.RecoveryStage, Plugin.Automatic.Fault, Plugin.Updates.Channel, Plugin.Automatic.TransitionWaiting) : ModText.Text(body));
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
            "UpdateChannel" => Plugin.UpdateChannel.Value,
            "AutoUpdate" => AutoUpdateMode(),
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
                // HandLayoutUi restores native layout itself when it sees the mode change, inside its own breaker.
                Plugin.HandExpandMode.Value = value == "Click" ? "Click" : "Hover";
                Plugin.GroupHand.Value = value != "Native";
                break;
            case "InputAttention": Isolated("InputAttention", InputAttention.Stop); Plugin.InputAttention.Value = value; break;
            case "UpdateChannel": Plugin.UpdateChannel.Value = value; Plugin.Updates.CheckTransition(DateTimeOffset.UtcNow); break;
            case "AutoUpdate":
                var (check, download, apply) = MenuLayout.AutoUpdateValues(value);
                Plugin.AutomaticChecks.Value = check; Plugin.AutoDownload.Value = download; Plugin.ApplyAfterExit.Value = apply;
                break;
        }
        _changedAt = Time.unscaledTime;
        Refresh();
        _helpAction = null;
        SetHelp(action);
        if (Plugin.Diagnostics.IsRecording) Plugin.Diagnostics.Write($"settingsDropdown applied; action={action}; value={value}");
    }

    private static void ApplyCore(string action)
    {
        if (!Compatibility.Allowed(action) || MenuLayout.IsSlider(action)) return; // Sliders apply their own value.
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
            case "UpdateCheck":
                Plugin.Updates.Check(DateTimeOffset.UtcNow);
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateDownload":
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Plugin.Updates.RepositoryPage.AbsoluteUri) { UseShellExecute = true }); }
                catch { Plugin.Updates.DownloadFailed(); }
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "Update": ApplyCore(UpdateRole()); return;
            case "UpdateSwitch":
                if (UpdateNotificationUi.CanConfirmTransition) { UpdateNotificationUi.BeginTransition(); ClosePanel(true); return; }
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateGet":
                if (Plugin.Automatic.CanReactivate) Plugin.Automatic.Reactivate(); else if (Plugin.Automatic.CanRecheck) Plugin.Automatic.Recheck(); else Plugin.Automatic.Download(DateTimeOffset.UtcNow);
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateCancel": Plugin.Automatic.Cancel(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateApply": Plugin.Automatic.ApplyOnce(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "SettingsAutoKeep": Plugin.SettingsAutoKeep.Value = !Plugin.SettingsAutoKeep.Value; break;
            case "SettingsSave":
                try { SettingsBackup.Copy(Plugin.Settings.ConfigFilePath, Path.Combine(SettingsBackup.Folder, SettingsBackup.SavedName)); }
                catch (Exception ex) { Plugin.Logger.LogWarning("[설정 보관] 저장 실패: " + ex.GetType().Name); }
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "SettingsLoad":
                // The first press only arms; a second press within a few seconds replaces the settings.
                if (Time.unscaledTime >= _loadArmedUntil) { _loadArmedUntil = Time.unscaledTime + 4f; Refresh(); return; }
                _loadArmedUntil = 0;
                try
                {
                    SettingsBackup.Copy(Path.Combine(SettingsBackup.Folder, SettingsBackup.SavedName), Plugin.Settings.ConfigFilePath);
                    Plugin.Settings.Reload();
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("[설정 보관] 불러오기 실패: " + ex.GetType().Name); }
                break;
            case "SettingsReset":
                // The first press only arms; a second press within a few seconds resets everything.
                if (Time.unscaledTime >= _resetArmedUntil) { _resetArmedUntil = Time.unscaledTime + 4f; Refresh(); return; }
                _resetArmedUntil = 0;
                Plugin.ResetAllSettings();
                Isolated("InputAttention", InputAttention.Stop); Isolated("FieldZoom", FieldIndicatorOpacity.Clear);
                break;
            case "SettingsFolder":
                try
                {
                    Directory.CreateDirectory(SettingsBackup.Folder);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsBackup.Folder) { UseShellExecute = true });
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("[설정 보관] 폴더 열기 실패: " + ex.GetType().Name); }
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "Enabled": Plugin.Enabled.Value = !Plugin.Enabled.Value; break;
            case "Details": Plugin.ShowDetails.Value = !Plugin.ShowDetails.Value; break;
            case "KoMinimum": Plugin.KoMinimum.Value = !Plugin.KoMinimum.Value; if (!Plugin.KoMinimum.Value) CardDiceUi.Clear(); break;
            case "Names": Plugin.UseRealNames.Value = !Plugin.UseRealNames.Value; break;
            case "CardPopups": Plugin.CardPopups.Value = !Plugin.CardPopups.Value; break;
            case "BattleStatus": Plugin.BattleStatus.Value = !Plugin.BattleStatus.Value; break;
            case "ShushuShield": Plugin.ShushuShield.Value = !Plugin.ShushuShield.Value; if (!Plugin.ShushuShield.Value) ShushuShieldUi.ClearDisplay(); break;
            case "ShushuShieldRestrictions": Plugin.ShushuShieldRestrictions.Value = !Plugin.ShushuShieldRestrictions.Value; if (!Plugin.ShushuShieldRestrictions.Value) ShushuShieldUi.ClearRestrictions(); break;
            case "AutoThanks": Plugin.AutoThanks.Value = !Plugin.AutoThanks.Value; break;
            case "CharacterRoles": Plugin.CharacterRoles.Value = !Plugin.CharacterRoles.Value; break;
            case "CharacterGuide": Plugin.CharacterGuide.Value = !Plugin.CharacterGuide.Value; break;
            case "PlayerTab": Plugin.PlayerTab.Value = !Plugin.PlayerTab.Value; break;
            case "PlayerTabHand": Plugin.PlayerTabHand.Value = !Plugin.PlayerTabHand.Value; break;
            case "PlayerTabRole": Plugin.PlayerTabRole.Value = !Plugin.PlayerTabRole.Value; break;
            case "IndicatorFade": Plugin.IndicatorFade.Value = !Plugin.IndicatorFade.Value; Isolated("FieldZoom", FieldIndicatorOpacity.Clear); break;
            case "IndicatorHide": Plugin.IndicatorHide.Value = !Plugin.IndicatorHide.Value; Isolated("FieldZoom", FieldIndicatorOpacity.Clear); break;
            case "DiceLock": Plugin.DiceLock.Value = !Plugin.DiceLock.Value; FieldDiceUi.Clear(); break;
            case "EncounterAdvice": Plugin.EncounterAdvice.Value = !Plugin.EncounterAdvice.Value; break;
            case "DefenseAdvice": Plugin.DefenseAdvice.Value = !Plugin.DefenseAdvice.Value; break;
            case "CardAdvice": Plugin.CardAdvice.Value = !Plugin.CardAdvice.Value; CardUi.Clear(keepDice: true); break;
            case "ShootingStarShow": Plugin.ShootingStarShow.Value = !Plugin.ShootingStarShow.Value; ShootingStarUi.Clear(); break;
            case "KoCardPreview": Plugin.KoCardPreview.Value = !Plugin.KoCardPreview.Value; CardDiceUi.Clear(); break;
            case "KoSpectator": Plugin.KoSpectator.Value = !Plugin.KoSpectator.Value; CardDiceUi.Clear(); break;
            case "StatusHand": Plugin.StatusHand.Value = !Plugin.StatusHand.Value; BattleStatusUi.Clear(); break;
            case "StatusEffects": Plugin.StatusEffects.Value = !Plugin.StatusEffects.Value; BattleStatusUi.Clear(); break;
            case "CounterIcons": Plugin.CounterIcons.Value = !Plugin.CounterIcons.Value; BattleStatusUi.Clear(); EncounterCounterUi.Clear(); break;
            case "ShushuMaster": Plugin.ShushuMaster.Value = !Plugin.ShushuMaster.Value; ShushuShieldUi.ClearDisplay(); ShushuShieldUi.ClearRestrictions(); break;
            case "CounterSound": Plugin.CounterSound.Value = !Plugin.CounterSound.Value; BattleStatusUi.Clear(); EncounterCounterUi.Clear(); break;
            case "CounterAnimation": Plugin.CounterAnimation.Value = !Plugin.CounterAnimation.Value; BattleStatusUi.Clear(); EncounterCounterUi.Clear(); break;
            case "PingFocus": Plugin.PingFocus.Value = !Plugin.PingFocus.Value; if (!Plugin.PingFocus.Value) PingFocusGuard.Reset(); break;
            case "SuppressNotices": Plugin.SuppressNotices.Value = !Plugin.SuppressNotices.Value; break;
            case "UpdateNotifications": Plugin.UpdateNotifications.Value = !Plugin.UpdateNotifications.Value; break;
            case "FinalDamage": Plugin.FinalDamage.Value = !Plugin.FinalDamage.Value; RemainingHpUi.ClearDisplay(); break;
            case "FieldBuffs": Plugin.FieldBuffs.Value = !Plugin.FieldBuffs.Value; break;
            case "FieldZoom": Plugin.FieldZoom.Value = !Plugin.FieldZoom.Value; break; // FieldZoomUi restores the camera itself once OFF.
            case "Diagnostics": Plugin.DiagnosticLogging.Value = !Plugin.DiagnosticLogging.Value; break;
            case "MuteUnfocused": Plugin.MuteUnfocused.Value = !Plugin.MuteUnfocused.Value; break;
            case "MatchFocus": Plugin.MatchFocus.Value = !Plugin.MatchFocus.Value; break;
        }
        _changedAt = Time.unscaledTime;
        Refresh();
        TraceInput("applied", _pressed);
        _helpAction = null;
        SetHelp(action);
    }

    // Working slider rows, or the same gray "unavailable" switch every blocked setting uses.
    // A slider failure blocks only its row, never the whole settings window.
    private static void RefreshSliders()
    {
        foreach (var action in MenuLayout.Sliders)
        {
            var tab = MenuLayout.GeneralControl(action) == _general;
            var working = tab && Compatibility.Allowed(action);
            var (min, _) = MenuLayout.SliderRange(action);
            // Dimmed and locked while a parent (or the switch it tunes) is OFF.
            var active = !Locked(action);
            if (Sliders.TryGetValue(action, out var slider) && !slider.Get<bool>("isDisposed"))
            {
                try
                {
                    slider.Set("visible", working);
                    if (working && Math.Abs(slider.Get<double>("value") - (SliderValue(action) - min)) >= 0.5) slider.Set("value", (double)(SliderValue(action) - min));
                    slider.Set("grayed", !active);
                    slider.Set("touchable", active);
                }
                catch (Exception ex) { Compatibility.Block(action, ex, () => DisposeSlider(action)); working = false; }
            }
            var blocked = SliderBlocked[action];
            blocked.Set("visible", tab && !working);
            ToggleBackgrounds[action].Set("grayed", true);
            Title(blocked, ModText.Text(MenuLayout.Help(action).Title) + ": " + ModText.Text("사용 불가"), 22);
            foreach (var key in new[] { action, action + "Value" })
            {
                Labels[key].Set("visible", working);
                Labels[key].Set("alpha", active ? 1f : 0.5f);
            }
            Labels[action + "Value"].Set("text", MenuLayout.Percent(SliderValue(action)));
        }
        foreach (var (parts, action) in SliderBacks)
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i].Set("visible", MenuLayout.GeneralControl(action) == _general);
                // The pill dims with its slider; the capsule around a child switch follows only the tray's parent.
                var parent = MenuLayout.Parent(action);
                var on = i == 0 ? (parent == null || ToggleEnabled(parent)) && (MenuLayout.SubParent(action) is not { } sub || ToggleEnabled(sub))
                    : parent == null || ToggleEnabled(parent);
                parts[i].Set("alpha", on ? 1f : 0.5f);
            }
    }

    // NativeUi.StyleTitle copies the native title format from a freshly created template button, which costs about
    // half a millisecond; Refresh runs on every open and every change for ~70 controls. Copy the format once per
    // control and size, then only swap the text (ModFont.Tick follows text changes for the font fallback).
    private static readonly Dictionary<IntPtr, (string Text, int Size)> Titles = new();
    private static void Title(RuntimeObject button, string text, int size = 26)
    {
        if (Titles.TryGetValue(button.Pointer, out var last))
        {
            // Already carries the native format: a size change only resizes (StyleTitle's own size step).
            if (last.Text != text) button.Set("title", text);
            if (last.Size != size) GameUi.StyleText(button.Call("GetChild", "title")!, size);
            Titles[button.Pointer] = (text, size);
            return;
        }
        NativeUi.StyleTitle(button, text, size);
        Titles[button.Pointer] = (text, size);
    }

    private static void Refresh()
    {
        foreach (var (button, action) in Controls.Values)
        {
            var general = MenuLayout.GeneralControl(action);
            button.Set("visible", MenuLayout.FixedControl(action) || general == _general);
            if (MenuLayout.IsSlider(action)) continue; // Sliders and their unavailable switches are refreshed below.
            var role = action == "Update" ? UpdateRole() : action;
            if (action == "Update") button.Set("grayed", false);
            if (action is "SettingsSave" or "SettingsLoad" or "SettingsFolder" or "SettingsReset")
            {
                var savedAt = SettingsBackup.SavedAt(SettingsBackup.Folder);
                var armed = action == "SettingsLoad" && Time.unscaledTime < _loadArmedUntil || action == "SettingsReset" && Time.unscaledTime < _resetArmedUntil;
                var missing = action == "SettingsLoad" && savedAt == null;
                button.Set("grayed", missing); ToggleBackgrounds[action].Set("grayed", missing);
                var state = action == "SettingsSave" && savedAt != null ? savedAt.Value.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    : missing ? ModText.Text("먼저 지금 보관을 누르세요") : armed ? ModText.Text("한 번 더 누르세요") : "";
                Title(button, ModText.Text(MenuLayout.Help(action).Title) + (state.Length == 0 ? "" : ": " + state), 22);
                continue;
            }
            if (action is "DiagnosticsOpen" or "DiagnosticsCollect")
            {
                var busy = action == "DiagnosticsCollect" && DiagnosticActions.Busy;
                button.Set("grayed", busy); ToggleBackgrounds[action].Set("grayed", busy);
                var state = action == "DiagnosticsCollect" ? DiagnosticActions.Status : "";
                Title(button, ModText.Text(MenuLayout.Help(action).Title) + (state.Length == 0 ? "" : ": " + state), 22);
                continue;
            }
            if (action is "UpdateAuth" or "UpdateSignOut")
            {
                var enabled = action == "UpdateAuth" ? Plugin.UpdateSession?.CanConnect == true : Plugin.UpdateSession?.CanSignOut == true;
                ToggleBackgrounds[action].Set("grayed", !enabled);
                Title(button, ModText.SessionTitle(action), 22);
                continue;
            }
            if (role is "UpdateChannel" or "UpdateCheck" or "UpdateDownload")
            {
                var installed = Plugin.Automatic.Snapshot is { RequiresTransition: true, InstalledChannel.Length: > 0 } snapshot ? snapshot.InstalledChannel : null;
                var state = role == "UpdateChannel" ? ModText.ChannelName(Plugin.Updates.Channel.ToString()) + (installed == null ? "" : ModText.InstalledChannelSuffix(installed))
                    : role == "UpdateCheck" ? ModText.CheckStatus(Plugin.Updates.Result) : "";
                ToggleBackgrounds[action].Set("grayed", false);
                Title(button, ModText.Text(MenuLayout.Help(role).Title) + (state.Length == 0 ? "" : ": " + state), 22);
                continue;
            }
            if (role == "UpdateSwitch")
            {
                var confirmable = UpdateNotificationUi.CanConfirmTransition;
                var target = confirmable && Plugin.Updates.Result.Release is { } candidate ? ModText.ChannelName(Plugin.Updates.Channel.ToString()) + " " + candidate.Version : null;
                button.Set("grayed", !confirmable); ToggleBackgrounds[action].Set("grayed", !confirmable);
                Title(button, ModText.Text(MenuLayout.Help(role).Title) + ": " + ModText.SwitchState(Plugin.Automatic.Snapshot.RequiresTransition, Plugin.Automatic.TransitionWaiting, target), 20);
                continue;
            }
            if (role == "UpdateApply")
            {
                ToggleBackgrounds[action].Set("grayed", false);
                Title(button, ModText.Text(MenuLayout.Help(role).Title) + ": " + ModText.AutomaticStatus(Plugin.Automatic.Status, Plugin.Automatic.TransitionWaiting), 20);
                continue;
            }
            if (role is "UpdateGet" or "UpdateCancel")
            {
                var available = role == "UpdateGet" ? Plugin.Automatic.CanDownload || Plugin.Automatic.CanReactivate || Plugin.Automatic.CanRecheck : Plugin.Automatic.CanCancel;
                button.Set("grayed", !available); ToggleBackgrounds[action].Set("grayed", !available);
                Title(button, (role == "UpdateGet" && Plugin.Automatic.CanReactivate ? ModText.Korean ? "다시 사용" : "Re-enable Updates" : role == "UpdateGet" && Plugin.Automatic.CanRecheck ? ModText.Korean ? "상태 다시 확인" : "Recheck Installation" : ModText.Text(MenuLayout.Help(role).Title)) + ": " + ModText.AutomaticStatus(Plugin.Automatic.Status, Plugin.Automatic.TransitionWaiting), 20);
                continue;
            }
            if (action == "Language")
            {
                ToggleBackgrounds[action].Set("grayed", false);
                Title(button, ModText.LanguageTitle, 26);
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
                if (action == "AutoUpdate") state = MenuLayout.OptionLabel(action, AutoUpdateMode());
                if (blocked) state = "사용 불가";
                // Gray only the yellow artwork: OFF remains fully clickable and hoverable.
                background.Set("grayed", blocked || !enabled);
                Title(button, ModText.Text(title) + ": " + ModText.Text(state), blocked || action is "InputAttention" or "HandLayout" or "AutoUpdate" || MenuLayout.Parent(action) != null ? 22 : 26);
                continue;
            }
        }
        // Dependent options dim and lock while their parent (or the switch they tune) is OFF (2026-10-23); values stay.
        foreach (var (button, action) in Controls.Values)
            if (MenuLayout.Parent(action) != null) button.Set("alpha", Locked(action) ? 0.5f : 1f);
        RefreshSliders();
        // Beta sign-in rows exist only for the Beta channel.
        var beta = Plugin.UpdateChannel.Value == "Beta";
        foreach (var (button, action) in Controls.Values)
            if (action is "UpdateAuth" or "UpdateSignOut") button.Set("visible", _general && beta);
        foreach (var (parts, parent) in Trays)
            foreach (var part in parts)
            {
                part.Set("visible", MenuLayout.GeneralControl(parent) == _general && (parent != "UpdateChannel" || beta));
                part.Set("alpha", ToggleEnabled(parent) ? 1f : 0.5f);
            }
        foreach (var section in SectionHeaders) section.View.Set("visible", section.General == _general);
        // Child pills follow their switches only in the next tick; sync them first, or the other tab's pills
        // (Player List Tab's are low in the feature list) stretch this tab's scroll height.
        PaintChildPills();
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
