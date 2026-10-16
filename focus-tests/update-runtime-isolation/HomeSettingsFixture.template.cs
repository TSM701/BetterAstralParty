using System;
using System.Collections.Generic;
namespace BapHomeSettings {
static class Probe {
    internal static readonly List<string> Calls = new List<string>();
    internal static bool Home = true, ThrowHome, ThrowFollow, ThrowInput, ThrowRoot, ThrowCreate, ThrowRefresh;
    internal static bool ThrowClose, ThrowRestore, ThrowTouch, ThrowVisible, ThrowDispose, ThrowWarning, ThrowError;
    internal static bool NativeModal;
    internal static bool ThrowNoticeClear;
    internal static string? ThrowLanguage;
    internal static void Visit(string name) { Calls.Add(name); }
    internal static int Count(string name) => Calls.FindAll(value => value == name).Count;
    internal static void LanguageStep(string feature) { Visit("language." + feature); if (ThrowLanguage == feature) throw new MissingMethodException("Language cleanup: " + feature); }
    internal static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    internal static void Reset() {
        Calls.Clear(); Compatibility.Reset(); Time.frameCount = 1; Time.unscaledTime = 0;
        Home = true; ThrowHome = ThrowFollow = ThrowInput = ThrowRoot = ThrowCreate = ThrowRefresh = ThrowNoticeClear = false; ThrowLanguage = null;
        ThrowClose = ThrowRestore = ThrowTouch = ThrowVisible = ThrowDispose = ThrowWarning = ThrowError = NativeModal = false;
        GameUi.Reset(); ModUi.Reset();
    }
}
static class ModText { internal static int Revision => 0; internal static string Text(string value) => value; }
static class Time { internal static int frameCount; internal static float unscaledTime; }
sealed class RuntimeObject {
    internal bool Visible = true, Touchable = true, Disposed;
    internal IntPtr Pointer => (IntPtr)1;
    internal T Get<T>(string name) => (T)(object)(name == "isDisposed" ? Disposed : Visible);
    internal void Set(string name, bool value) {
        Probe.Visit("modal." + name);
        if (name == "touchable") { if (Probe.ThrowTouch) throw new MissingMethodException("touchable"); Touchable = value; }
        else { if (Probe.ThrowVisible) throw new MissingMethodException("visible"); Visible = value; }
    }
    internal void Call(string name, bool value) { Probe.Visit("scroll." + name); }
    internal static IntPtr FindClass(string ns, string name) => (IntPtr)1;
    internal static RuntimeObject StaticField(IntPtr type, string name) => new RuntimeObject();
    internal static void RequireClasses(string feature, params (string, string)[] classes) { if (Probe.ThrowRoot) throw new TypeLoadException("Shared root API unavailable"); }
}
sealed class Log {
    internal void LogWarning(string message) { Probe.Visit("diagnostic.warning"); if (Probe.ThrowWarning) throw new Exception("Warning sink unavailable"); }
    internal void Error(string feature, Exception error) { Probe.Visit("diagnostic." + feature); if (Probe.ThrowError) throw new Exception("Detailed sink unavailable"); }
}
sealed class Updates { internal void AcknowledgeNotification() { Probe.Visit("updates.acknowledge"); } }
static class Plugin {
    internal static readonly Log Logger = new Log(), Diagnostics = new Log();
    internal static readonly Updates Updates = new Updates();
}
__COMPATIBILITY__
sealed class Badge { internal void Animate() { Probe.Visit("advice.animate"); } internal void Tick() { } }
static class CharacterNameUi { internal static void Tick(RuntimeObject root) { Probe.Visit("character.tick"); } }
static class CardDiceUi { internal static void PollResolved() { Probe.Visit("dice.tick"); } }
static class CardUi { internal static void Animate() { Probe.Visit("cards.tick"); } }
static class RemainingHpUi { internal static void PollFinalPoints() { } internal static void Animate() { } }
static class CardPreviewUi { internal static void Reset() { Probe.LanguageStep("CardPopups"); } }
static class BattleStatusUi { internal static void Clear() { Probe.LanguageStep("BattleStatus"); } }
static class FieldBuffUi { internal static void Clear(string reason) { Probe.LanguageStep("FieldBuffs"); } }
static class UpdateNotificationUi {
    internal const string Feature = "UpdateNotification";
    static void Clear() { Probe.Visit("notice.clear"); if (Probe.ThrowNoticeClear) throw new Exception("Notice cleanup unavailable"); }
    static void TickCore() { Probe.Visit("notice.tick"); }
__NOTICE_TICK__
}
static class GameUi {
    internal static IntPtr _rootClass, _textClass, _logicClass, _battleClass;
    internal static RuntimeObject? _root, _menu, _pressed;
    internal static object? _menuState;
    internal static readonly Badge _defBadge = new Badge(), _dodgeBadge = new Badge(), _observerSummary = new Badge();
    internal static float _nextScan;
    internal static int _hitFrame;
    internal static int _menuLanguageRevision = 0;
    internal static string _lastError = "";
    internal static readonly List<int> HitPath = new List<int>();
    internal static bool HomeAvailable { get; private set; }
    internal static RuntimeObject? Root => _root;
    internal static void Reset() { _rootClass = _textClass = _logicClass = _battleClass = IntPtr.Zero; _root = _menu = new RuntimeObject(); _pressed = null; _menuState = null; _nextScan = 0; _hitFrame = -1; _lastError = ""; HomeAvailable = true; }
    internal static void SetSettingsModal(bool value) { Probe.Visit("settings.restore"); if (Probe.ThrowRestore) throw new Exception("Modal restore unavailable"); Probe.NativeModal = value; }
    internal static void Dispose(RuntimeObject? value) { if (value == null) return; Probe.Visit("modal.dispose"); if (Probe.ThrowDispose) throw new Exception("Dispose unavailable"); value.Disposed = true; }
    static void UpdateHome() { HomeAvailable = Probe.Home; Probe.Visit("home.observe"); if (!Compatibility.Allowed("HomeMenu")) return; Probe.Visit("home.bind"); if (Probe.ThrowHome) throw new MissingFieldException("Home_Button_Activity"); }
    static void FollowHomeMenu() { Probe.Visit("home.follow"); if (Probe.ThrowFollow) throw new MissingFieldException("menu anchor"); }
    static void HandleMenuInput() { Probe.Visit("home.input"); if (Probe.ThrowInput) throw new MissingMethodException("menu hit-test"); }
    static void UpdateFight() { Probe.Visit("fight.tick"); }
    static void UpdateHover() { Probe.Visit("hover.tick"); }
    static void ClearAdvice() { Probe.Visit("advice.clear"); Probe.LanguageStep("Enabled"); }
    static void HideAdvice() { }
__FAIL_HOME__
__CLEAR_HOME__
__REFRESH_LANGUAGE__
__GAME_TICK__
}
static class ModUi {
    internal static RuntimeObject? _modal;
    internal static readonly RuntimeObject _settingsScroll = new RuntimeObject();
    internal static bool _general;
    internal static float _closingAt;
    internal static string? _helpAction;
__IS_OPEN__
    internal static void Reset() { _modal = null; _general = false; _closingAt = -1; _helpAction = null; }
    internal static void FailUi(Exception error) { Probe.Visit("core.failed"); Compatibility.Block("CoreUi", error); }
    static void ClosePanel(bool immediately) { Probe.Visit("settings.close"); if (Probe.ThrowClose) throw new Exception("Close unavailable"); GameUi.SetSettingsModal(false); }
    static void TogglePanelCore() { Probe.Visit("settings.create"); _modal = new RuntimeObject(); if (Probe.ThrowCreate) throw new MissingMethodException("Settings frame"); Probe.NativeModal = true; }
    static void Refresh() { Probe.Visit("settings.refresh"); if (Probe.ThrowRefresh) throw new MissingMethodException("Settings toggle"); }
    static void SetHelp(string action) { Probe.Visit("settings.help"); }
__FAIL_SETTINGS__
__TOGGLE_PANEL__
__OPEN_UPDATE_SETTINGS__
}
public static class Runner {
    static void Frames(int count) { for (int i = 0; i < count; i++) { GameUi.Tick(); Time.frameCount++; Time.unscaledTime += .25f; } }
    static void Continuing(int frames) { Probe.Check(Compatibility.Allowed("CoreUi") && Probe.Count("core.failed") == 0 && Probe.Count("fight.tick") == frames && Probe.Count("cards.tick") == frames && Probe.Count("character.tick") == frames, "Optional UI failure must preserve unrelated scene UI"); }
    public static int Run() {
        int cases = 0;
        Probe.Reset(); Frames(2); Continuing(2); Probe.Check(Probe.Count("home.bind") == 2 && Probe.Count("home.follow") == 2 && Probe.Count("home.input") == 2, "Healthy home UI unchanged"); cases++;
        Probe.Reset(); Probe.ThrowHome = true; Frames(2); Continuing(2);
        Probe.Check(!Compatibility.Allowed("HomeMenu") && GameUi.HomeAvailable && Probe.Count("home.observe") == 2 && Probe.Count("home.bind") == 1 && Probe.Count("home.follow") == 0 && Probe.Count("diagnostic.Compatibility.HomeMenu") == 1, "Optional clone fault is session-local, diagnosed, and retains native availability observation"); cases++;
        Probe.Reset(); Probe.ThrowFollow = true; Frames(2); Continuing(2); Probe.Check(Probe.Count("home.follow") == 1 && Probe.Count("home.input") == 0 && !Compatibility.Allowed("HomeMenu"), "Anchor fault retires only its menu"); cases++;
        Probe.Reset(); Probe.ThrowInput = true; Frames(2); Continuing(2); Probe.Check(Probe.Count("home.input") == 1 && !Compatibility.Allowed("HomeMenu"), "Hit-test fault retires only its menu"); cases++;
        Probe.Reset(); Probe.ThrowRoot = true; Frames(2); Probe.Check(!Compatibility.Allowed("CoreUi") && Probe.Count("core.failed") == 1 && Probe.Count("fight.tick") == 0, "Actual shared root/type failure keeps the shared safety boundary"); cases++;
        Probe.Reset(); Probe.ThrowCreate = true; ModUi.TogglePanel(); ModUi.TogglePanel(); Frames(2); Continuing(2);
        Probe.Check(!Compatibility.Allowed("SettingsUi") && !ModUi.IsOpen && !Probe.NativeModal && Probe.Count("settings.create") == 1 && Probe.Count("diagnostic.Compatibility.SettingsUi") == 1 && Compatibility.Allowed("HomeMenu"), "Settings creation failure does not poison the native menu or gameplay"); cases++;
        Probe.Reset(); ModUi.TogglePanel(); Probe.ThrowRefresh = true; ModUi.OpenUpdateSettings(); ModUi.OpenUpdateSettings(); Frames(2); Continuing(2);
        Probe.Check(!Compatibility.Allowed("SettingsUi") && !ModUi.IsOpen && !Probe.NativeModal && Probe.Count("settings.refresh") == 1 && Probe.Count("updates.acknowledge") == 0, "Settings refresh failure is local and does not acknowledge an unavailable update screen"); cases++;
        Probe.Reset(); Probe.ThrowCreate = true; ModUi.OpenUpdateSettings(); Frames(1); Continuing(1);
        Probe.Check(!Compatibility.Allowed("SettingsUi") && Probe.Count("scroll.ScrollTop") == 0 && Probe.Count("settings.refresh") == 0, "Failed panel creation stops only the remaining optional panel calls"); cases++;
        Probe.Reset(); Probe.ThrowCreate = Probe.ThrowClose = Probe.ThrowRestore = Probe.ThrowTouch = Probe.ThrowVisible = Probe.ThrowDispose = true; ModUi.TogglePanel(); Frames(1); Continuing(1);
        Probe.Check(!Compatibility.Allowed("SettingsUi") && !ModUi.IsOpen && Probe.Count("settings.close") == 1 && Probe.Count("settings.restore") == 1 && Probe.Count("modal.touchable") == 1 && Probe.Count("modal.visible") == 1 && Probe.Count("modal.dispose") == 1, "Every owned cleanup is attempted independently; stale settings state cannot cascade"); cases++;
        Probe.Reset(); Probe.ThrowHome = Probe.ThrowWarning = Probe.ThrowError = true; Frames(2); Continuing(2);
        Probe.Check(!Compatibility.Allowed("HomeMenu") && Probe.Count("diagnostic.Compatibility.HomeMenu") == 1 && Probe.Count("modal.dispose") == 1, "Failing diagnostics cannot prevent local home cleanup or reach CoreUi"); cases++;
        Probe.Reset(); Probe.ThrowCreate = Probe.ThrowWarning = Probe.ThrowError = true; ModUi.TogglePanel(); Frames(2); Continuing(2);
        Probe.Check(!Compatibility.Allowed("SettingsUi") && !ModUi.IsOpen && !Probe.NativeModal && Probe.Count("diagnostic.Compatibility.SettingsUi") == 1, "Failing diagnostics cannot prevent local settings cleanup or reach CoreUi"); cases++;
        foreach (var feature in new[] { "Enabled", "CardPopups", "BattleStatus", "FieldBuffs" }) {
            Probe.Reset(); Probe.ThrowLanguage = feature; GameUi.RefreshLanguage(); Frames(2);
            Probe.Check(Compatibility.Allowed("CoreUi") && !Compatibility.Allowed(feature) && Compatibility.Allowed("HomeMenu")
                && Probe.Count("core.failed") == 0 && Probe.Count("character.tick") == 2 && Probe.Count("home.follow") == 2
                && Probe.Count("diagnostic.Compatibility." + feature) == 1, "Language cleanup fault remains with its owning feature: " + feature);
            foreach (var cleanup in new[] { "Enabled", "CardPopups", "BattleStatus", "FieldBuffs" })
                Probe.Check(Probe.Count("language." + cleanup) == (cleanup == feature ? 2 : 1), "Other language cleanups still run after a failed one");
            cases++;
        }
        Probe.Reset(); Compatibility.Block(UpdateNotificationUi.Feature, new Exception("Previously unavailable notice")); Probe.ThrowNoticeClear = true; UpdateNotificationUi.Tick(); Frames(2); Continuing(2);
        Probe.Check(!Compatibility.Allowed(UpdateNotificationUi.Feature) && Probe.Count("notice.clear") == 1 && Probe.Count("notice.tick") == 0,
            "A cleanup failure on an already-disabled notice must not escape into the shared UI driver"); cases++;
        Probe.Reset(); return cases;
    }
}
}
