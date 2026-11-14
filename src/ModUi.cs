using BetterAstralParty.Observability;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterAstralParty;

// Native FairyGUI controls: their original hover/down gears work without Unity UI patches.
internal static partial class ModUi
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
    // Transparent hover areas over whole slider rows, by setting.
    private static readonly Dictionary<string, RuntimeObject> SliderAreas = new();
    // Settings rows (approved draft A, 2026-11-01): each native switch keeps its input and title (dark, left-aligned), its
    // own artwork hidden; mod shapes behind it draw the row (white top rows with a state bar, cream option rows) and, for
    // ON/OFF settings, a switch track whose knob slides. Other rows show their state as text at the right.
    private sealed class SettingRow
    {
        internal RuntimeObject Button = null!, Background = null!, Value = null!;
        internal RuntimeObject? Fill, Divider, Track, Knob;
        internal UpdateParts? Update;
        internal Color Base;
        internal bool Top;
        internal (bool Switch, bool Capsule, bool Line)? Layout; // null until the first refresh lays out the row.
        internal float KnobAt = -1, KnobX, KnobTravel;
        internal (bool Visible, float Alpha, bool On, bool Hot, bool Switch, float Knob)? Painted;
        internal bool Switch => Layout?.Switch == true;
    }
    private static readonly Dictionary<IntPtr, SettingRow> SettingRows = new();
    // Draft B: a top row's fill shows its state (pale yellow ON, warm gray OFF); option rows stay cream.
    private static readonly Color RowOn = new(1f, 0.788f, 0.059f), ChildOn = new(1f, 0.855f, 0.361f), ChildOff = new(0.827f, 0.816f, 0.776f),
        RowCream = new(1f, 0.973f, 0.89f), ChipLine = new(0.839f, 0.776f, 0.627f), Divider = new(0.886f, 0.839f, 0.729f),
        TopOn = new(1f, 0.941f, 0.698f), TopOff = new(0.925f, 0.91f, 0.871f), ChoiceHover = new(1f, 0.973f, 0.871f), LineText = new(0.431f, 0.408f, 0.361f);
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
    private static (float View, float Content, bool General, bool Visible, int Search)? _tickLayout;
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

        // A negative radius is a plain rectangle.
        private static RuntimeObject Graph(RuntimeObject parent, float width, float height, float line, float radius)
        {
            var r = Math.Max(radius, 0f);
            return SmoothShape.Create(parent, 0f, 0f, width, height, BubblePlain, r, r, r, r, line);
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
            foreach (var graph in new[] { _tail, _body, _patch }) SmoothShape.Fill(graph, fill);
        }
    }

    private static float _scrollPosition;
    private static string? _helpAction;
    private static string? _helpOption;
    private static RuntimeObject? _dropdown;
    private static string? _dropdownAction;
    private static readonly Dictionary<IntPtr, (RuntimeObject Button, string Value, RuntimeObject Fill, bool Chosen)> Choices = new();
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
        CharacterGuideUi.Clear, HandLayoutUi.Clear, CardTargetButtonsUi.Clear, FieldZoomUi.Clear, () => FieldBuffUi.Clear("pvp-safety"), WalkDirectionUi.Clear, PlayerTabUi.Clear,
        CharacterNameUi.RestoreAll, InputAttention.Stop, PingFocusGuard.Reset, AutoThanksUi.Clear, SendAllLikesUi.Clear, ShopBulkUi.Clear, BattleMenuUi.Clear, FieldDiceUi.Clear, ReplayUi.Clear,
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
    internal static bool ShowsUpdates => IsOpen && _general && !Searching && GameUi.HomeAvailable;

    // Every exit, PvP and early gates included, hands the stall watch back to the game.
    internal static void Tick()
    {
        StallWatch.Focus(Application.isFocused);
        StallWatch.Enter(StallWatch.ModStart);
        try { TickCore(); }
        finally { StallWatch.Enter(-1); }
    }

    private static void TickCore()
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
        DiagnosticHub.Gate(DiagnosticFeature.CoreUi, pauseGameplay ? PvpSafety.Broken ? DiagnosticCode.NativeCallFailed : PvpSafety.Suspended ? DiagnosticCode.PvpExcluded : DiagnosticCode.StartupWaiting : !Compatibility.Allowed("CoreUi") ? DiagnosticCode.CompatibilityBlocked : DiagnosticCode.None);
        // The state keeps the collector's fixed values; why it suspended is its own line (team mode, room state and map, broken).
        Plugin.Diagnostics.State("pvpSafety", pauseGameplay ? (PvpSafety.Suspended ? "suspended" : "initializing") : "active");
        if (pauseGameplay && PvpSafety.Suspended) Plugin.Diagnostics.State("pvpSafety.reason", PvpSafety.Reason);
        if (pauseGameplay)
        {
            if (!_pvpCleaned)
            {
                _pvpCleaned = true;
                foreach (var cleanup in PvpCleanup)
                    try { cleanup(); }
                    catch (Exception ex) { _pvpCleaned = false; Plugin.Diagnostics.Error("PvpSafety.cleanup", ex); }
            }
            if (PvpSafety.Broken) TickHomeOnly();
            return;
        }
        _pvpCleaned = false;
        try { PingFocusGuard.Tick(); }
        catch (Exception ex) { Compatibility.Block("PingFocus", ex); }
        try { InputAttention.Tick(); }
        catch (Exception ex) { Compatibility.Block("InputAttention", ex, InputAttention.Stop); }
        try { CardChoiceTimer.Tick(); }
        catch (Exception ex) { Compatibility.Block(CardChoiceTimer.Feature, ex, CardChoiceTimer.Clear); }
        try { HomeEscQuit.Tick(); }
        catch (Exception ex) { Compatibility.Block(HomeEscQuit.Feature, ex); }
        var measure = Plugin.DiagnosticLogging.Value && Plugin.Diagnostics.IsRecording;
        var started = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var allocated = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
        // Spike attribution: managed collections and IL2CPP heap drops (a native GC) during and between ticks.
        var clrGcs = measure ? GC.CollectionCount(0) : 0;
        var nativeUsed = measure ? (long)Il2CppInterop.Runtime.IL2CPP.il2cpp_gc_get_used_size() : 0;
        _measureTicks = measure;
        Array.Clear(TickMarks); Array.Clear(TickBytes);
        TickMarks[0] = started; TickBytes[0] = allocated;
        StallWatch.Enter(0);
        long gameEnd = started, popupEnd = started, statusEnd = started, coreEnd = started;
        long fieldStart = 0, fieldEnd = 0;
        double handMs = 0;
        long handBytes = 0;
        try
        {
            Plugin.PollCatalogs(DateTime.UtcNow);
            TickSettingsBackup();
            ModText.Select(Plugin.Language.Value);
            SmoothShape.Tick();
            GameUi.Tick(); Mark(1);
            if (measure) coreEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            BattleMenuUi.Tick(); Mark(2);
            AutoThanksUi.Tick(); SendAllLikesUi.Tick(); ShopBulkUi.Tick(); Mark(3);
            CharacterGuideUi.Tick(); ReplayUi.Tick(); Mark(4);
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
            FieldBuffUi.Tick(); WalkDirectionUi.Tick(); PlayerTabUi.Tick(); MonsterMoveProbe.Tick(); FightDataProbe.Tick(); NancyProbe.Tick(); Mark(16);
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
                Plugin.Diagnostics.TickSpikes(TickMarks, TickBytes, milliseconds, GC.CollectionCount(0) - clrGcs,
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
    // Managed bytes allocated by the main thread at each mark: which feature's garbage sets off the CLR collections.
    private static readonly long[] TickBytes = new long[DiagnosticLog.TickFeatures.Length + 1];
    private static bool _measureTicks;
    private static long _nativeUsedAfterTick;

    private static void Mark(int index)
    {
        StallWatch.Enter(index < DiagnosticLog.TickFeatures.Length ? index : -1);
        if (!_measureTicks) return;
        TickMarks[index] = System.Diagnostics.Stopwatch.GetTimestamp();
        TickBytes[index] = GC.GetAllocatedBytesForCurrentThread();
    }

    // PvpSafety.Broken: gameplay stays off, but on the home screen, where no match runs, the Mod Settings button, the settings
    // window and the update notices keep working, so a fixed version can still be found and installed.
    private static void TickHomeOnly()
    {
        try { GameUi.TickHome(); }
        catch (Exception ex) { FailUi(ex); return; }
        try { UpdateNotificationUi.Tick(); }
        catch (Exception ex) { Compatibility.Block(UpdateNotificationUi.Feature, ex, UpdateNotificationUi.Clear); }
        try { TickSettings(); }
        catch (Exception ex) { FailSettings(ex); }
    }

    internal static void FailUi(Exception ex) => Compatibility.Block("CoreUi", ex,
        UpdateNotificationUi.Clear,
        GameUi.Stop, CardPreviewUi.Reset, BattleStatusUi.Clear, EncounterCounterUi.Clear, ShushuShieldUi.Clear,
        CharacterGuideUi.Clear, HandLayoutUi.Clear, CardTargetButtonsUi.Clear, FieldZoomUi.Clear,
        () => FieldBuffUi.Clear("compatibility"), WalkDirectionUi.Clear, PlayerTabUi.Clear, CharacterNameUi.RestoreAll, BattleMenuUi.Clear, SendAllLikesUi.Clear, ShopBulkUi.Clear, FieldDiceUi.Clear, ReplayUi.Clear,
        // As in PvpCleanup: the input-attention tray icon and the latches of features the blocked frame no longer ticks.
        InputAttention.Stop, AutoThanksUi.Clear, PingFocusGuard.Reset,
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

    private static readonly bool DevelopmentBuild = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);

    private static void TickSettings()
    {
        if (!Compatibility.Allowed("SettingsUi")) return;
        if (!CanOpen && IsOpen) ClosePanel(true);
        if (GameUi.HomeAvailable && !UpdateNotificationUi.ConsumedInput && Input.GetKeyDown(KeyCode.F8)) TogglePanel();
        if (!Compatibility.Allowed("SettingsUi") || !IsOpen) return;
        // Development builds only: Ctrl+Shift+F10 shows the hovered setting as unavailable, and again brings it back.
        if (DevelopmentBuild && Input.GetKeyDown(KeyCode.F10) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            && Controls.TryGetValue(_hovered, out var previewed) && !MenuLayout.FixedControl(previewed.Action))
            Compatibility.TogglePreview(previewed.Action);
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
        TickSearch();
        if (!UpdateNotificationUi.ConsumedInput && Input.GetKeyDown(KeyCode.Escape))
        {
            // Esc first closes a list, then clears a search, then closes the window.
            if (_dropdown != null) CloseDropdown(); else if (_query.Length > 0) SetQuery(""); else ClosePanel();
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
        PaintRows();
        UpdateScrollMarker();
        AnimateHelp();
        var saved = Time.unscaledTime - _changedAt < 1.6f ? ModText.Text("✓ 저장됨") : $"BetterAstralParty v{Plugin.Version}";
        if (_savedText != saved) { _savedText = saved; Labels["Saved"].Set("text", saved); }
    }

    private static BepInEx.Configuration.ConfigEntry<float> SliderEntry(string action) => action switch
    {
        "FieldMarker" => Plugin.FieldIndicatorHeight, "IndicatorFadeStrength" => Plugin.IndicatorFadeStrength,
        "FoldedHandOpacity" => Plugin.FoldedHandOpacity, "Scale" => Plugin.UiScale, "FreeCameraSpeed" => Plugin.FreeCameraSpeed,
        "ZoomSensitivity" => Plugin.ZoomSensitivity, "ZoomOutLimit" => Plugin.ZoomOutLimit, "IndicatorHideDistance" => Plugin.IndicatorHideDistance, _ => Plugin.Opacity
    };

    // Stored settings in whole percent; the slider shows them from the range minimum.
    private static int SliderValue(string action) => MenuLayout.SliderPercent(action, SliderEntry(action).Value);

    // Default marker (user-chosen 2026-10-27, replacing a dark line that clashed with the pink bar): a translucent copy of
    // the native star grip (Setting_Com_Slider_grip, the star only; the pink tail belongs to the bar) at the default value,
    // under the real grip so they coincide there. The grip follows the bar end, so the copy keeps the grip's y and sits
    // where the grip would be at the default share. Call after the slider shows its value, so bar and grip agree.
    private const float DefaultMarkAlpha = 0.4f;
    private static void AddDefaultMark(RuntimeObject slider, string action, int min, int max)
    {
        var bar = slider.Field("_barObjectH"); var grip = slider.Field("_gripObject");
        if (bar == null || grip == null || max <= min) return;
        var ratio = Math.Clamp(((float)MenuLayout.SliderPercent(action, (float)SliderEntry(action).DefaultValue) - min) / (max - min), 0f, 1f);
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
        SliderEntry(action).Value = MenuLayout.SliderStored(action, percent);
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
            Place(slider, action);
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
        var fallback = MenuLayout.SliderPercent(action, (float)SliderEntry(action).DefaultValue) - min;
        next = Math.Clamp(Math.Abs(next - fallback) <= DefaultSnapPixels * step ? fallback : Math.Round(next), 0, range);
        if (next != slider.Get<double>("value")) slider.Set("value", next);
    }

    // Left/right arrows move the hovered slider one step, repeating while held.
    private static float _nudgeAt;
    private static void NudgeSlider(RuntimeObject slider, int range)
    {
        var left = Input.GetKey(KeyCode.LeftArrow);
        if (left == Input.GetKey(KeyCode.RightArrow) || _dragSlider != IntPtr.Zero || !slider.Get<bool>("touchable") || Typing()) return;
        var pressed = Input.GetKeyDown(left ? KeyCode.LeftArrow : KeyCode.RightArrow);
        if (!pressed && Time.unscaledTime < _nudgeAt) return;
        var area = SliderAreas.GetValueOrDefault(Controls[slider.Pointer].Action)?.Pointer ?? IntPtr.Zero;
        if (GameUi.PointerPath(hitTest: true).All(item => item.Pointer != slider.Pointer && item.Pointer != area)) return;
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
        Controls.Clear(); Labels.Clear(); ToggleBackgrounds.Clear(); SectionHeaders.Clear(); Trays.Clear(); SettingRows.Clear(); Titles.Clear();
        ResetSearchState();
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
        // The template's black text drew a thick white outline on the dark capsule; plain white text, like the BETA chip.
        Plain(Labels["HelpTag"], Color.white);
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
        CreateSearch();
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
            var dot = SmoothShape.Create(_sheet!, 0f, 0f, 12f, 12f, TickDark, 6f, 6f, 6f, 6f, 2f, TickRing);
            dot.Set("visible", false);
            dot.Call("SetPivot", 0.5f, 0.5f, true);
            Ticks.Add((i, dot));
        }
        for (var i = 0; i < MenuLayout.Sections.Length; i++) Bubbles.Add(new CategoryBubble(_sheet!, i));
        // Transparent press band over the track and its margins, so the native list never starts its own drag there.
        _trackHit = NativeUi.Component(_sheet!, MenuLayout.TrackWidth + 2 * MenuLayout.TrackPad, MenuLayout.SettingsHeight);
        _trackHit.Set("opaque", true); _trackHit.Set("touchable", true);
        _trackHit.Call("SetXY", MenuLayout.SettingsX + MenuLayout.TrackX - MenuLayout.TrackPad, MenuLayout.SettingsY);
        TraceSettingsOpen(DiagnosticPhase.SettingsSteppers);
        Sliders.Clear(); SliderBlocked.Clear(); SliderBacks.Clear(); SliderAreas.Clear(); _dragSlider = IntPtr.Zero;
        foreach (var action in MenuLayout.Sliders)
        {
            var size = MenuLayout.TextSize(action);
            Labels[action] = NativeUi.Label(_sheet!, ModText.Text(MenuLayout.SliderLabel(action)), MenuLayout.SettingsX + MenuLayout.SliderLabelX(action), 0, 98, 35, size);
            Labels[action].Set("autoSize", 3);
            Labels[action].Set("singleLine", true);
            GameUi.StyleText(Labels[action], size, align: 0);
            Plain(Labels[action]);
            Labels[action].Set("verticalAlign", 1);
            // One line, shrinking instead of wrapping the % sign; "150%" is the widest value.
            Labels[action + "Value"] = NativeUi.Label(_sheet!, "", MenuLayout.SettingsX + MenuLayout.SliderValueX(action), 0, MenuLayout.SliderValueWidth, 35, size);
            Labels[action + "Value"].Set("singleLine", true);
            Labels[action + "Value"].Set("autoSize", 3);
            GameUi.StyleText(Labels[action + "Value"], size, align: 2);
            Plain(Labels[action + "Value"]);
            Labels[action + "Value"].Set("verticalAlign", 1);
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
        AddToggle("PlayerTabSkill", 440, MenuLayout.ToggleY(34));
        AddToggle("OldReplays", 440, MenuLayout.ToggleY(35));
        AddToggle("WalkDirection", 440, MenuLayout.ToggleY(36));
        AddToggle("SendAllLikes", 440, MenuLayout.ToggleY(37));
        AddToggle("ShopBulkBuy", 440, MenuLayout.ToggleY(38));
        TraceSettingsOpen(DiagnosticPhase.SettingsReparent);
        foreach (var (button, action) in Controls.Values)
            if (!MenuLayout.FixedControl(action)) MoveIntoSettings(button, MenuLayout.SettingY(action) + MenuLayout.ChildOffsetY(action));
        // Row shapes behind each switch: background, and a track and knob for ON/OFF settings. A head's row reaches down
        // around its option lines, with a divider under its own line; heads go last, so their fill lands behind their chips.
        // Made before slider rows and trays, which go further back.
        foreach (var row in SettingRows.Values.OrderBy(row => MenuLayout.Head(Controls[row.Button.Pointer].Action)))
        {
            var action = Controls[row.Button.Pointer].Action;
            if (MenuLayout.IsSlider(action)) continue; // A slider's unavailable switch sits on its slider row.
            var (x, w) = MenuLayout.RowBox(action);
            float y = MenuLayout.SettingY(action) + MenuLayout.ChildOffsetY(action), h = MenuLayout.ToggleHeightFor(action), r = MenuLayout.RowRadius(action);
            var top = MenuLayout.Parent(action) == null;
            var chip = MenuLayout.SubParent(action) != null;
            var line = top ? 3f : chip ? 1.5f : 2.5f;
            row.Top = top; row.Base = top ? TopOn : chip ? Color.white : RowCream;
            var (tw, th) = MenuLayout.TrackSize(action);
            float tx = x + w - MenuLayout.TrackInset(action) - tw, ty = y + (h - th) / 2, inset = th <= 24 ? 3.5f : 4f, knob = th - 2 * inset, edge = chip ? 2f : 2.5f;
            var head = MenuLayout.Head(action);
            row.Fill = Shape(_settingsContent!, x, y, w, head ? MenuLayout.HeadHeight(action) : h, row.Base, r, r, r, r, line, chip ? ChipLine : Color.black);
            if (head) row.Divider = Shape(_settingsContent!, x + 14, y + h - 0.75f, w - 28, 1.5f, Divider, 0f, 0f, 0f, 0f);
            row.Track = Shape(_settingsContent!, tx, ty, tw, th, ChildOn, th / 2, th / 2, th / 2, th / 2, edge, Color.black);
            row.Knob = Shape(_settingsContent!, tx + inset, ty + inset, knob, knob, Color.white, knob / 2, knob / 2, knob / 2, knob / 2, edge, Color.black);
            row.KnobX = tx + inset; row.KnobTravel = tw - th;
            foreach (var part in new[] { row.Knob, row.Track, row.Divider, row.Fill }) if (part != null) _settingsContent!.Call("SetChildIndex", part, 0);
        }
        foreach (var action in MenuLayout.Sliders)
        {
            MoveIntoSettings(Labels[action], MenuLayout.SettingY(action) + MenuLayout.SliderLabelOffsetY(action));
            MoveIntoSettings(Labels[action + "Value"], MenuLayout.SettingY(action) + MenuLayout.SliderLabelOffsetY(action));
            // Every slider row is drawn like its sibling switch rows; a slider tuning a child switch is a line inside that
            // switch's row and draws nothing of its own. Shapes go to the back in creation order (trays beneath all).
            var (x, w) = MenuLayout.RowBox(action);
            var top = MenuLayout.Parent(action) == null;
            var r = MenuLayout.RowRadius(action);
            var parts = MenuLayout.SubParent(action) != null ? Array.Empty<RuntimeObject>()
                : new[] { Shape(_settingsContent!, x, MenuLayout.SettingY(action), w, MenuLayout.ToggleHeightFor(action), top ? TopOn : RowCream, r, r, r, r, top ? 3f : 2.5f, Color.black) };
            foreach (var part in parts) _settingsContent!.Call("SetChildIndex", part, 0);
            SliderBacks.Add((parts, action));
            // The whole row (a sub slider: its line) shows the help and takes the arrow keys, not only the bar (2026-11-01).
            // Behind everything: the bar, labels and the unavailable switch stay on top.
            var area = NativeUi.Component(_settingsContent!, w, MenuLayout.ToggleHeightFor(action));
            area.Call("SetXY", x, MenuLayout.SettingY(action));
            area.Set("opaque", true);
            _settingsContent!.Call("SetChildIndex", area, 0);
            Controls[area.Pointer] = (area, action);
            SliderAreas[action] = area;
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
                        new Color(0.945f, 0.902f, 0.796f), 0f, 0f, 12f, 12f, 1.5f, new Color(0.804f, 0.733f, 0.565f))
                };
                foreach (var part in parts) _settingsContent!.Call("SetChildIndex", part, 0);
                Trays.Add((parts, parent));
            }
        CreateSections();
        PlaceRows();
        TraceSettingsOpen(DiagnosticPhase.SettingsFooter);
        Button("Close", "Button_ReturnRounded", ModText.Text("돌아가기"), 410, MenuLayout.FooterY, 235);
        Labels["Saved"] = NativeUi.Label(_sheet, "", 675, MenuLayout.FooterY + 7, 315, 30, 18);
        Labels["Saved"].Set("autoSize", 3);
        Labels["Saved"].Set("singleLine", true);
        TraceSettingsOpen(DiagnosticPhase.SettingsRefresh);
        Refresh();
        Plugin.Logger.LogInfo("[원본 모드 메뉴] 고정 크기 토글·호버 기능 안내 생성됨");
    }

    // Mod-owned rounded shape, antialiased (SmoothShape; corner order: top-left, top-right, bottom-left, bottom-right).
    private static RuntimeObject Shape(RuntimeObject parent, float x, float y, float width, float height, Color fill,
        float topLeft, float topRight, float bottomLeft, float bottomRight, float line = 0f, Color? lineColor = null)
        => SmoothShape.Create(parent, x, y, width, height, fill, topLeft, topRight, bottomLeft, bottomRight, line, lineColor);

    // Rows follow their switch's visibility, alpha (dimmed while locked), OFF gray and hover; the knob slides in 0.12 s.
    private static void PaintRows()
    {
        foreach (var row in SettingRows.Values)
        {
            if (row.Fill == null || row.Layout == null) continue;
            var action = Controls[row.Button.Pointer].Action;
            var on = !row.Background.Get<bool>("grayed");
            var target = on ? 1f : 0f;
            var knob = row.KnobAt < 0 ? target : Mathf.MoveTowards(row.KnobAt, target, Time.unscaledDeltaTime / 0.12f);
            var state = (row.Button.Get<bool>("visible"), row.Button.Get<float>("alpha"), on, row.Button.Pointer == _hovered && !Locked(action), row.Switch, knob);
            if (row.Painted == state) continue;
            row.Painted = state; row.KnobAt = knob;
            foreach (var part in new[] { row.Fill, row.Divider, row.Track, row.Knob })
            {
                if (part == null) continue;
                part.Set("visible", state.Item1 && (state.Item5 || part != row.Track && part != row.Knob));
                part.Set("alpha", state.Item2);
            }
            var fill = row.Top ? on ? TopOn : TopOff : row.Base;
            SmoothShape.Fill(row.Fill, state.Item4 ? Color.Lerp(fill, RowOn, 0.15f) : fill);
            SmoothShape.Fill(row.Track!, on ? row.Top ? RowOn : ChildOn : ChildOff);
            row.Knob!.Set("x", row.KnobX + knob * row.KnobTravel);
        }
    }

    // Grows the native popup frame: SetSize lets its relations stretch the background; a full-height child the
    // relations left at the old bottom is stretched by hand.
    internal static void GrowFrame(RuntimeObject frame, float height)
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
            // Panel cream darkened by a translucent warm brown.
            var band = SmoothShape.Create(header, MenuLayout.SectionBandX, MenuLayout.SectionBandY, MenuLayout.SectionBandWidth, MenuLayout.SectionBandHeight, BandFill, 8f, 8f, 8f, 8f);
            if (MenuLayout.Experimental(i))
            {
                // BETA chip at the band's right end: dark brown capsule, cream text. Plain text: the template's outline
                // swallowed the small cream letters (1.7.0-dev.1).
                var chip = Shape(header, MenuLayout.SectionBandX + MenuLayout.SectionBandWidth - 58, MenuLayout.SectionBandY + 3, 50, 20, new Color(0.388f, 0.22f, 0.024f), 10f, 10f, 10f, 10f);
                var beta = NativeUi.Label(header, "BETA", chip.Get<float>("x"), chip.Get<float>("y"), 50, 20, 14);
                Plain(beta, new Color(0.98f, 0.933f, 0.855f));
                beta.Set("verticalAlign", 1);
            }
            var title = NativeUi.Label(header, ModText.Text(section.Title), MenuLayout.SectionBandX + 12f, MenuLayout.SectionBandY, MenuLayout.SectionBandWidth - 24f, MenuLayout.SectionBandHeight, MenuLayout.SectionTitleSize);
            TraceSettingsOpen(DiagnosticPhase.SettingsSections, index + ".fit");
            title.Set("singleLine", true);
            title.Set("autoSize", 3);
            title.Set("touchable", false);
            TraceSettingsOpen(DiagnosticPhase.SettingsSections, index + ".style");
            GameUi.StyleText(title, MenuLayout.SectionTitleSize, align: 0);
            Plain(title);
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
        if (_tickLayout != (view, content, _general, visible, _searchRevision))
        {
            _tickLayout = (view, content, _general, visible, _searchRevision);
            TickPositions.Clear();
            foreach (var (_, dot) in Ticks) dot.Set("visible", false);
            var shown = Ticks.Where(tick => SectionShown(tick.Section)).ToList();
            var ys = MenuLayout.TickYs(shown.Select(tick => MenuLayout.JumpTarget(_arrangement, tick.Section, content, view)).ToArray(), content - view, view);
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
            SmoothShape.Fill(_flashBand, Color.Lerp(BandFlash, BandFill, t));
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
            SmoothShape.Fill(dot, section == _tickCurrent ? TickCurrent : TickDark);
            var scale = section == _tickHot ? 1.4f : 1f;
            dot.Call("SetScale", scale, scale);
            Bubbles[section].SetFill(section == _tickHot ? BubbleHot : section == _tickCurrent ? BubbleCurrent : BubblePlain);
        }
    }

    private static int CurrentSection(float position, float content, float view)
    {
        var current = -1;
        foreach (var (section, _) in TickPositions)
            if (MenuLayout.JumpTarget(_arrangement, section, content, view) <= position + 1) current = section;
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

    // The update rows' category (General tab), for a new release and the update notice's Update Settings (2026-11-03).
    private static void JumpToUpdates() => JumpTo(Array.FindIndex(MenuLayout.Sections, section => section.Actions.Contains("AutoUpdate")));

    private static void JumpTo(int section)
    {
        var view = _settingsScroll!.Get<float>("viewHeight");
        _settingsScroll.Call("SetPosY", MenuLayout.JumpTarget(_arrangement, section, _settingsScroll.Get<float>("contentHeight"), view), true);
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
        var width = MenuLayout.ToggleWidth(action);
        var height = MenuLayout.ToggleHeightFor(action);
        x += MenuLayout.Indent(action);
        var toggle = Button(action, "Button_Switch_1", "", x, y, width);
        // Common-mode buttons never remain in the selected/down scaling state.
        // Modify only these new mod-owned instances, not the game's controls.
        toggle.Set("mode", 0);
        toggle.SetField("_downEffect", 1); // Native press tint, no size change.
        toggle.SetField("_downEffectValue", 0.88f);
        toggle.Call("SetPivot", 0f, 0f, true); // Tint-only controls retain top-left fixed-size layout.
        toggle.Call("SetScale", 1f, 1f);
        toggle.Call("SetSize", width, height);
        toggle.Call("SetXY", x, y);
        var background = toggle.Call("GetChild", "n3")
            ?? throw new InvalidOperationException("설정 버튼 배경 준비 대기");
        background.Call("SetSize", width, height);
        background.Set("touchable", false);
        background.Set("visible", false); // The row shapes behind the switch carry its look; its gray still marks OFF.
        ToggleBackgrounds[action] = background;
        toggle.Call("GetChild", "title")!.Set("autoSize", 3); // Native Shrink: fit translations without expanding the button.
        var value = NativeUi.Label(toggle, "", 0, 0, 0, height, MenuLayout.TextSize(action));
        value.Set("singleLine", true);
        value.Set("autoSize", 3);
        GameUi.StyleText(value, MenuLayout.TextSize(action), align: 2);
        Plain(value);
        value.Set("verticalAlign", 1);
        var row = SettingRows[toggle.Pointer] = new SettingRow { Button = toggle, Background = background, Value = value };
        Titles.Remove(toggle.Pointer); // Button() styled a centred title; the row restyles it on its first refresh.
        if (MenuLayout.Options(action).Length > 2) Labels["Arrow." + action] = Chevron(toggle, width - 38, height / 2);
        if (action == "Update") AddUpdateParts(row, width, height);
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
        // Read before CloseForSettings clears a development preview's red point.
        var updateNotice = Plugin.Updates.NotificationVersion != null || UpdateNotificationUi.PreviewMark != null;
        TraceSettingsOpen(DiagnosticPhase.SettingsNoticeClose);
        UpdateNotificationUi.CloseForSettings();
        TraceSettingsOpen(DiagnosticPhase.SettingsOpenState);
        if (IsOpen) { ClosePanel(); return; }
        OpenCosts.Clear(); _openPhase = DiagnosticPhase.UiAction; OpenWatch.Restart(); _sliderMs = 0; _sliderCount = 0;
        DiagnosticHub.UiAction(DiagnosticUiAction.Open, DiagnosticOutcome.Begin, default);
        DiagnosticHub.FlushSoon();
        TraceSettingsOpen(DiagnosticPhase.UiAction);
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
        EndSearch();
        Refresh();
        TraceSettingsOpen(DiagnosticPhase.SettingsLayout);
        Layout();
        // The first open after the red point lit scrolls to the update rows, as a category dot would.
        if (updateNotice && _general) JumpToUpdates();
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
        // A download stopped for this session (network) has no latch to lift; it simply downloads again.
        if (Plugin.Automatic.CanReactivate) Plugin.Automatic.Reactivate(); else if (Plugin.Automatic.CanRecheck) Plugin.Automatic.Recheck(); else Plugin.Automatic.Download(DateTimeOffset.UtcNow);
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
            EndSearch();
            JumpToUpdates();
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
        // A channel switch waiting for a release, or still being checked, keeps the switch row (draft 2, 2026-11-01).
        : Plugin.Automatic.Status == AutomaticUpdateStatus.TransitionRequired ? "UpdateSwitch"
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
        "SendAllLikes" => Plugin.SendAllLikes.Value,
        "ShopBulkBuy" => Plugin.ShopBulkBuy.Value,
        "CharacterRoles" => Plugin.CharacterRoles.Value,
        "CharacterGuide" => Plugin.CharacterGuide.Value,
        "PlayerTab" => Plugin.PlayerTab.Value,
        "PlayerTabHand" => Plugin.PlayerTabHand.Value,
        "PlayerTabRole" => Plugin.PlayerTabRole.Value,
        "PlayerTabSkill" => Plugin.PlayerTabSkill.Value,
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
        "OldReplays" => Plugin.OldReplays.Value,
        "SuppressNotices" => Plugin.SuppressNotices.Value,
        "AutoUpdate" => AutoUpdateMode() != "Off",
        "Update" or "UpdateChannel" => true,
        "UpdateNotifications" => Plugin.UpdateNotifications.Value,
        "FinalDamage" => Plugin.FinalDamage.Value,
        "FieldBuffs" => Plugin.FieldBuffs.Value,
        "WalkDirection" => MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value) != "Off",
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
            "WalkDirection" => MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value),
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
            arrow.Set("rotation", position.y < origin.y ? 180f : 0f);
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
        _dropdown = NativeUi.Component(_sheet!, MenuLayout.RowWidth, height);
        _dropdown.Set("sortingOrder", 100);
        _dropdown.Set("opaque", true);
        _dropdown.Call("SetXY", point.x, point.y);
        Shape(_dropdown, 0, 0, MenuLayout.RowWidth, height, Color.white, 13f, 13f, 13f, 13f, 3f, Color.black);
        _dropdownAction = action;
        var selected = action switch
        {
            "Language" => ModText.Mode,
            "HandLayout" => !Plugin.GroupHand.Value ? "Native" : Plugin.HandExpandMode.Value,
            "WalkDirection" => MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value),
            "UpdateChannel" => Plugin.UpdateChannel.Value,
            "AutoUpdate" => AutoUpdateMode(),
            _ => InputAttentionState.Normalize(Plugin.InputAttention.Value)
        };
        const float itemWidth = MenuLayout.RowWidth - 12, itemHeight = MenuLayout.ChoiceHeight;
        for (var i = 0; i < options.Length; i++)
        {
            var chosen = options[i] == selected;
            var y = 6f + i * MenuLayout.ChoicePitch;
            var fill = Shape(_dropdown, 6, y, itemWidth, itemHeight, chosen ? TopOn : Color.white, 10f, 10f, 10f, 10f);
            var item = NativeUi.Create("Common", "Button_Switch_1")!;
            _dropdown.Call("AddChild", item);
            item.Set("mode", 0);
            item.SetField("changeStateOnClick", false);
            item.SetField("_downEffect", 1);
            item.SetField("_downEffectValue", 0.88f);
            item.Call("SetPivot", 0f, 0f, true);
            item.Call("SetScale", 1f, 1f);
            item.Call("SetSize", itemWidth, itemHeight);
            item.Call("SetXY", 6f, y);
            var background = item.Call("GetChild", "n3")!;
            background.Set("touchable", false);
            background.Set("visible", false); // The card and its fills carry the look.
            NativeUi.StyleTitle(item, ModText.Text(MenuLayout.OptionLabel(action, options[i])), 19);
            var title = item.Call("GetChild", "title")!;
            GameUi.StyleText(title, 19, align: 0);
            NativeUi.InheritText(title, dark: false);
            Plain(title);
            title.Set("verticalAlign", 1);
            title.Call("SetXY", 16f, 0f);
            title.Call("SetSize", itemWidth - 64, itemHeight);
            // On the card, not in the button: the button's press tint recolours its children and left the art's own pink.
            if (chosen) NativeUi.Icon(_dropdown, "打勾（大）", 6 + itemWidth - 42, y + (itemHeight - 18) / 2, 26)?.Set("color", Color.black);
            Choices[item.Pointer] = (item, options[i], fill, chosen);
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
            if (Choices.TryGetValue(_hovered, out var old)) SmoothShape.Fill(old.Fill, old.Chosen ? TopOn : Color.white);
            if (hit != null && Choices[current] is var now) SmoothShape.Fill(now.Fill, now.Chosen ? Color.Lerp(TopOn, RowOn, 0.15f) : ChoiceHover);
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
            case "WalkDirection": Plugin.WalkDirection.Value = value; break; // WalkDirectionUi follows the mode on its next frame.
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
            case "Features": _general = false; EndSearch(); _settingsScroll!.Call("ScrollTop", false); Refresh(); return;
            case "General": _general = true; EndSearch(); _settingsScroll!.Call("ScrollTop", false); Refresh(); return;
            case "Search": return; // The field takes the click itself.
            case "SearchClear": SetQuery(""); return;
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
            case "SendAllLikes": Plugin.SendAllLikes.Value = !Plugin.SendAllLikes.Value; break;
            case "ShopBulkBuy": Plugin.ShopBulkBuy.Value = !Plugin.ShopBulkBuy.Value; break;
            case "CharacterRoles": Plugin.CharacterRoles.Value = !Plugin.CharacterRoles.Value; break;
            case "CharacterGuide": Plugin.CharacterGuide.Value = !Plugin.CharacterGuide.Value; break;
            case "PlayerTab": Plugin.PlayerTab.Value = !Plugin.PlayerTab.Value; break;
            case "PlayerTabHand": Plugin.PlayerTabHand.Value = !Plugin.PlayerTabHand.Value; break;
            case "PlayerTabRole": Plugin.PlayerTabRole.Value = !Plugin.PlayerTabRole.Value; break;
            case "PlayerTabSkill": Plugin.PlayerTabSkill.Value = !Plugin.PlayerTabSkill.Value; break;
            case "OldReplays": Plugin.OldReplays.Value = !Plugin.OldReplays.Value; break;
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
            var tab = Shown(action);
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
            RowText(blocked, action, ModText.Text(MenuLayout.Help(action).Title), ModText.Text("사용 불가"));
            foreach (var key in new[] { action, action + "Value" })
            {
                Labels[key].Set("visible", working);
                Labels[key].Set("alpha", active ? 1f : 0.5f);
            }
            Labels[action + "Value"].Set("text", MenuLayout.Percent(SliderValue(action)));
        }
        foreach (var (parts, action) in SliderBacks)
            foreach (var part in parts)
            {
                part.Set("visible", Shown(action));
                part.Set("alpha", Locked(action) ? 0.5f : 1f); // The row dims with its slider.
            }
    }

    // NativeUi.StyleTitle copies the native title format from a freshly created template button, which costs about
    // half a millisecond; Refresh runs on every open and every change for ~70 controls. Copy the format once per
    // control and size, then only swap the text (ModFont.Tick follows text changes for the font fallback).
    // A settings row: its name at the left; an ON/OFF setting (state null) shows the switch track, any other its state
    // as text at the right, before a dropdown's arrow. The update row can instead show pills on a second line and a
    // capsule button at the right.
    private static void RowText(RuntimeObject button, string action, string title, string? state, Pills? pills = null, CapsuleMode? capsule = null)
    {
        var row = SettingRows[button.Pointer];
        Title(button, title, MenuLayout.TextSize(action));
        var layout = (state == null && capsule == null, capsule != null, pills != null);
        if (row.Layout != layout)
        {
            row.Layout = layout;
            float width = button.Get<float>("width"), height = button.Get<float>("height"), text = MenuLayout.TextX(action);
            var right = Labels.ContainsKey("Arrow." + action) ? 46f : 14f;
            var value = layout.Item1 || layout.Item2 ? 0 : (width - text - right) * 0.5f;
            var reserve = layout.Item1 ? MenuLayout.TrackSize(action).Width + MenuLayout.TrackInset(action) + 8 : layout.Item2 ? CapsuleWidth + 20 : right + value + 8;
            var label = button.Call("GetChild", "title")!;
            label.Call("SetXY", text, pills != null ? 5f : 0f);
            label.Call("SetSize", width - text - reserve, pills != null ? 32f : height);
            row.Value.Call("SetXY", width - right - value, 0f);
            row.Value.Call("SetSize", value, height);
            row.Update?.Strip.Set("visible", pills != null);
            row.Update?.Capsule.Set("visible", layout.Item2);
        }
        if (row.Value.Get("text")?.String() != (state ?? "")) row.Value.Set("text", state ?? "");
        if (row.Update != null) ShowUpdateParts(row.Update, pills, capsule);
    }

    // Settings list text as drawn in the drafts: the dark template's face without its outline, shadow or dilation.
    internal static void Plain(RuntimeObject label, Color? color = null)
    {
        var format = label.Get("textFormat")!;
        format.SetField("outline", 0f);
        format.SetField("faceDilate", 0f);
        format.SetField("shadowOffset", Vector2.zero);
        if (color is { } fill) format.SetField("color", fill);
        label.Set("textFormat", format);
    }

    // The update row (channel switch draft 2, user-chosen 2026-11-01): pills on its second line (a check's result and time,
    // or the installed release, an arrow and the switch target, outlined only while it is still awaited) and a capsule
    // button at the right: Check Again with the native re-roll arrows, Switch with two mod arrows (yellow), or an inert
    // gray Waiting (the native stopwatch) or Checking (the re-roll arrows).
    private enum CapsuleMode { Check, Switch, Wait, Busy }
    private readonly record struct Pills(string First, string? Second, bool Arrow = false, bool Pending = false);
    private sealed class UpdateParts
    {
        internal readonly (RuntimeObject Fill, RuntimeObject Text)[] Pills = new (RuntimeObject, RuntimeObject)[2];
        internal RuntimeObject Strip = null!, Arrow = null!, Capsule = null!, CapsuleFill = null!, CapsuleText = null!, Swap = null!;
        internal RuntimeObject? Refresh, Clock;
        internal Color Dark;
        internal (Pills?, CapsuleMode?, bool)? Shown;
    }
    private const float CapsuleWidth = 112, PillHeight = 20;
    private static readonly Color PillLine = new(0.47f, 0.455f, 0.416f), PillWaitLine = new(0.6f, 0.58f, 0.53f);

    private static void AddUpdateParts(SettingRow row, float width, float height)
    {
        var button = row.Button;
        var parts = row.Update = new UpdateParts();
        var left = MenuLayout.TextX("Update");
        parts.Strip = NativeUi.Component(button, width - left - CapsuleWidth - 20, PillHeight);
        parts.Strip.Set("touchable", false);
        parts.Strip.Call("SetXY", left, 34f);
        parts.Strip.Call("SetPivot", 0f, 0.5f, false); // Shrinks toward its left edge and middle (RowText).
        for (var i = 0; i < 2; i++)
        {
            var fill = Shape(parts.Strip, 0, 0, 40, PillHeight, Color.white, 10f, 10f, 10f, 10f, 1.5f, PillLine);
            var text = NativeUi.Label(parts.Strip, "", 0, 0, 24, PillHeight, 14);
            text.Set("singleLine", true);
            GameUi.StyleText(text, 14, align: 0);
            Plain(text);
            text.Set("verticalAlign", 1);
            parts.Pills[i] = (fill, text);
        }
        parts.Dark = parts.Pills[0].Text.Get("textFormat")!.Field<Color>("color");
        parts.Arrow = Arrow(parts.Strip, 12, false, LineText);
        parts.Capsule = NativeUi.Component(button, CapsuleWidth, 32);
        parts.Capsule.Set("touchable", false);
        parts.Capsule.Call("SetXY", width - 12 - CapsuleWidth, (height - 32) / 2);
        parts.CapsuleFill = Shape(parts.Capsule, 0, 0, CapsuleWidth, 32, RowOn, 16f, 16f, 16f, 16f, 2.5f, Color.black);
        parts.Refresh = NativeUi.Icon(parts.Capsule, "重新随机", 11, 7, 19);
        parts.Clock = NativeUi.Icon(parts.Capsule, "时间", 10, 6, 20);
        parts.Swap = NativeUi.Component(parts.Capsule, 18, 17);
        parts.Swap.Set("touchable", false);
        parts.Swap.Call("SetXY", 11f, 8f);
        Arrow(parts.Swap, 18, false, Color.black);
        Arrow(parts.Swap, 18, true, Color.black).Call("SetXY", 0f, 7f);
        parts.CapsuleText = NativeUi.Label(parts.Capsule, "", 34, 0, CapsuleWidth - 42, 32, 16);
        parts.CapsuleText.Set("singleLine", true);
        parts.CapsuleText.Set("autoSize", 3);
        GameUi.StyleText(parts.CapsuleText, 16, align: 0);
        Plain(parts.CapsuleText);
        parts.CapsuleText.Set("verticalAlign", 1);
    }

    private static void ShowUpdateParts(UpdateParts parts, Pills? pills, CapsuleMode? mode)
    {
        var shown = (pills, mode, ModText.Korean);
        if (parts.Shown == shown) return;
        parts.Shown = shown;
        if (pills is { } p)
        {
            var x = 0f;
            for (var i = 0; i < 2; i++)
            {
                var (fill, text) = parts.Pills[i];
                var label = i == 0 ? p.First : p.Second;
                fill.Set("visible", label != null);
                text.Set("visible", label != null);
                if (label == null) continue;
                if (i == 1 && p.Arrow) { parts.Arrow.Call("SetXY", x + 1, PillHeight / 2 - 5); x += 18; }
                var pending = i == 1 && p.Pending;
                SmoothShape.Restyle(fill, pending ? new Color(1f, 1f, 1f, 0f) : Color.white, 1.5f, pending ? PillWaitLine : PillLine);
                Plain(text, pending ? LineText : parts.Dark);
                text.Set("text", label);
                var width = text.Get<float>("textWidth") + 16;
                text.Call("SetSize", width - 16, PillHeight);
                text.Call("SetXY", x + 8, 0f);
                fill.Call("SetSize", width, PillHeight);
                fill.Call("SetXY", x, 0f);
                x += width + 6;
            }
            parts.Arrow.Set("visible", p.Arrow && p.Second != null);
            // Date versions in the channel switch role ("비공개 베타 2026.11.1-beta.2 → 릴리즈 2026.11.1", about 355 wide at 14)
            // can outgrow the strip before the capsule: the pills shrink to fit, as long labels shrink elsewhere.
            var room = parts.Strip.Get<float>("width");
            var scale = x - 6 > room ? room / (x - 6) : 1f;
            if (Math.Abs(parts.Strip.Get<float>("scaleX") - scale) > 0.001f) parts.Strip.Call("SetScale", scale, scale);
        }
        if (mode is { } m)
        {
            var active = m is CapsuleMode.Check or CapsuleMode.Switch;
            var ink = active ? Color.black : LineText;
            SmoothShape.Restyle(parts.CapsuleFill, active ? RowOn : ChildOff, 2.5f, active ? Color.black : PillWaitLine);
            parts.Refresh?.Set("visible", m is CapsuleMode.Check or CapsuleMode.Busy || m == CapsuleMode.Wait && parts.Clock == null);
            parts.Refresh?.Set("color", ink);
            parts.Clock?.Set("visible", m == CapsuleMode.Wait);
            parts.Clock?.Set("color", ink);
            parts.Swap.Set("visible", m == CapsuleMode.Switch);
            Plain(parts.CapsuleText, active ? parts.Dark : LineText);
            parts.CapsuleText.Set("text", ModText.CapsuleLabel(m == CapsuleMode.Check, m == CapsuleMode.Switch, m == CapsuleMode.Wait));
        }
    }

    // A small arrow of mod bars, 10 high: a shaft and a two-bar head, pointing right or left.
    private static RuntimeObject Arrow(RuntimeObject parent, float length, bool left, Color color)
    {
        var arrow = NativeUi.Component(parent, length, 10);
        arrow.Set("touchable", false);
        Shape(arrow, 0, 3.8f, length, 2.4f, color, 1.2f, 1.2f, 1.2f, 1.2f);
        var tip = left ? 0f : length;
        foreach (var up in new[] { true, false })
        {
            // Each head bar is 7 long at 40 degrees, its outer end on the tip.
            var bar = Shape(arrow, tip + (left ? 2.68f : -2.68f) - 3.5f, 5f + (up ? -2.25f : 2.25f) - 1.2f, 7, 2.4f, color, 1.2f, 1.2f, 1.2f, 1.2f);
            bar.Call("SetPivot", 0.5f, 0.5f, false);
            bar.Set("rotation", up ^ left ? 40f : -40f);
        }
        return arrow;
    }

    // Dropdown arrow: two short black bars in a V, turned over while the list opens upward.
    private static RuntimeObject Chevron(RuntimeObject parent, float x, float centerY)
    {
        var chevron = NativeUi.Component(parent, 16, 9);
        chevron.Set("touchable", false);
        chevron.Call("SetPivot", 0.5f, 0.5f, false);
        chevron.Call("SetXY", x, centerY - 4.5f);
        foreach (var (mid, angle) in new[] { (4f, 40f), (12f, -40f) })
        {
            var bar = Shape(chevron, mid - 5.5f, 4.5f - 1.6f, 11, 3.2f, Color.black, 1.6f, 1.6f, 1.6f, 1.6f);
            bar.Call("SetPivot", 0.5f, 0.5f, false);
            bar.Set("rotation", angle);
        }
        return chevron;
    }

    private static readonly Dictionary<IntPtr, (string Text, int Size)> Titles = new();
    private static void Title(RuntimeObject button, string text, int size = 26)
    {
        if (Titles.TryGetValue(button.Pointer, out var last))
        {
            // Already carries the native format: a size change only resizes (StyleTitle's own size step).
            if (last.Text != text) button.Set("title", text);
            if (last.Size != size) GameUi.StyleText(button.Call("GetChild", "title")!, size, SettingRows.ContainsKey(button.Pointer) ? 0 : 1);
            Titles[button.Pointer] = (text, size);
            return;
        }
        NativeUi.StyleTitle(button, text, size);
        if (SettingRows.ContainsKey(button.Pointer))
        {
            // Settings rows: the dark text of the slider labels, at the left (draft A).
            var label = button.Call("GetChild", "title")!;
            GameUi.StyleText(label, size, align: 0);
            NativeUi.InheritText(label, dark: false);
            Plain(label);
            label.Set("verticalAlign", 1);
        }
        Titles[button.Pointer] = (text, size);
    }

    private static void Refresh()
    {
        foreach (var (button, action) in Controls.Values)
        {
            button.Set("visible", action == "SearchClear" ? _query.Length > 0 : MenuLayout.FixedControl(action) || Shown(action));
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
                RowText(button, action, ModText.Text(MenuLayout.Help(action).Title), state);
                continue;
            }
            if (action is "DiagnosticsOpen" or "DiagnosticsCollect")
            {
                var busy = action == "DiagnosticsCollect" && DiagnosticActions.Busy;
                button.Set("grayed", busy); ToggleBackgrounds[action].Set("grayed", busy);
                RowText(button, action, ModText.Text(MenuLayout.Help(action).Title), action == "DiagnosticsCollect" ? DiagnosticActions.Status : "");
                continue;
            }
            if (action is "UpdateAuth" or "UpdateSignOut")
            {
                var enabled = action == "UpdateAuth" ? Plugin.UpdateSession?.CanConnect == true : Plugin.UpdateSession?.CanSignOut == true;
                ToggleBackgrounds[action].Set("grayed", !enabled);
                RowText(button, action, ModText.SessionTitle(action), "");
                continue;
            }
            if (role is "UpdateChannel" or "UpdateCheck" or "UpdateDownload")
            {
                var installed = Plugin.Automatic.Snapshot is { RequiresTransition: true, InstalledChannel.Length: > 0 } snapshot ? snapshot.InstalledChannel : null;
                var state = role == "UpdateChannel" ? ModText.ChannelName(Plugin.Updates.Channel.ToString()) + (installed == null ? "" : ModText.InstalledChannelSuffix(installed)) : "";
                ToggleBackgrounds[action].Set("grayed", false);
                if (role == "UpdateCheck")
                {
                    var (first, second) = ModText.CheckPills(Plugin.Updates.Result, DateTimeOffset.Now);
                    RowText(button, action, ModText.Text(MenuLayout.Help(role).Title), "", new Pills(first, second), CapsuleMode.Check);
                }
                else RowText(button, action, ModText.Text(MenuLayout.Help(role).Title), state);
                continue;
            }
            if (role == "UpdateSwitch")
            {
                // Installed release -> target; while no target is offered yet the row waits (no release) or checks.
                var confirmable = UpdateNotificationUi.CanConfirmTransition;
                var waiting = Plugin.Automatic.TransitionWaiting;
                var snapshot = Plugin.Automatic.Snapshot;
                var target = confirmable && Plugin.Updates.Result.Release is { } candidate ? ModText.ChannelName(Plugin.Updates.Channel.ToString()) + " " + candidate.Version : null;
                button.Set("grayed", false); ToggleBackgrounds[action].Set("grayed", false);
                RowText(button, action, ModText.Text(MenuLayout.Help(role).Title), "",
                    new Pills(ModText.InstalledPill(snapshot.InstalledChannel, snapshot.InstalledVersion.Length > 0 ? snapshot.InstalledVersion : Plugin.Version),
                        ModText.SwitchState(snapshot.RequiresTransition, waiting, target), Arrow: true, Pending: target == null),
                    confirmable ? CapsuleMode.Switch : waiting ? CapsuleMode.Wait : CapsuleMode.Busy);
                continue;
            }
            if (role == "UpdateApply")
            {
                ToggleBackgrounds[action].Set("grayed", false);
                RowText(button, action, ModText.Text(MenuLayout.Help(role).Title), ModText.AutomaticStatus(Plugin.Automatic.Status, Plugin.Automatic.TransitionWaiting));
                continue;
            }
            if (role is "UpdateGet" or "UpdateCancel")
            {
                var available = role == "UpdateGet" ? Plugin.Automatic.CanDownload || Plugin.Automatic.CanReactivate || Plugin.Automatic.CanRecheck : Plugin.Automatic.CanCancel;
                button.Set("grayed", !available); ToggleBackgrounds[action].Set("grayed", !available);
                RowText(button, action, role == "UpdateGet" && Plugin.Automatic.CanReactivate ? ModText.Korean ? "다시 사용" : "Re-enable Updates" : role == "UpdateGet" && Plugin.Automatic.CanRecheck ? ModText.Korean ? "상태 다시 확인" : "Recheck Installation" : ModText.Text(MenuLayout.Help(role).Title),
                    ModText.AutomaticStatus(Plugin.Automatic.Status, Plugin.Automatic.TransitionWaiting));
                continue;
            }
            if (action == "Language")
            {
                ToggleBackgrounds[action].Set("grayed", false);
                RowText(button, action, ModText.Text("언어"), ModText.LanguageState);
                continue;
            }
            if (ToggleBackgrounds.TryGetValue(action, out var background))
            {
                var enabled = ToggleEnabled(action);
                var blocked = !Compatibility.Allowed(action);
                var title = MenuLayout.Help(action).Title;
                var state = action == "Names" ? (enabled ? "본명" : "이명") : null;
                if (action == "InputAttention") state = InputAttentionState.Label(Plugin.InputAttention.Value);
                if (action == "HandLayout") state = HandLayout.ModeLabel(HandLayout.Mode(Plugin.GroupHand.Value, Plugin.HandExpandMode.Value));
                if (action == "WalkDirection") state = MenuLayout.OptionLabel(action, MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value));
                if (action == "AutoUpdate") state = MenuLayout.OptionLabel(action, AutoUpdateMode());
                if (blocked) state = "사용 불가";
                // Gray only the switch and bar: OFF remains fully clickable and hoverable.
                background.Set("grayed", blocked || !enabled);
                RowText(button, action, ModText.Text(title), state == null ? null : ModText.Text(state));
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
            if (action is "UpdateAuth" or "UpdateSignOut") button.Set("visible", Shown(action) && beta);
        foreach (var (parts, parent) in Trays)
            foreach (var part in parts)
            {
                part.Set("visible", TrayShown(parent) && (parent != "UpdateChannel" || beta));
                part.Set("alpha", ToggleEnabled(parent) ? 1f : 0.5f);
            }
        for (var i = 0; i < SectionHeaders.Count; i++) SectionHeaders[i].View.Set("visible", SectionShown(i));
        // Child pills follow their switches only in the next tick; sync them first, or the other tab's pills
        // (Player List Tab's are low in the feature list) stretch this tab's scroll height.
        PaintRows();
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
