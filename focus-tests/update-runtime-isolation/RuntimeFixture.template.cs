using System;
using System.Collections.Generic;
namespace __NAMESPACE__ {
public static class Probe {
    public static readonly List<string> Calls = new List<string>();
    public static readonly HashSet<string> Blocked = new HashSet<string>();
    public static bool Core = true, Home = true, Pause, Suspended, AllowAudio = true;
    public static bool ThrowBackend, ThrowAutomatic, ThrowFrameFailed, ThrowRefresh, ThrowNotice, ThrowShield, ThrowCleanup, ThrowOwnedCleanup;
    public static bool ThrowCatalogWarning, ThrowCatalogStage, ThrowCatalogWrite, ThrowCatalogFailure, ThrowCatalogError;
    public static bool Checking, Discovery;
    public static int HomeReads, NativeCalls, OwnedCleanupAttempts;
    public static void Visit(string name, bool native = false) {
        Calls.Add(name);
        if (native) { NativeCalls++; if (!Core) throw new InvalidOperationException("Blocked native call: " + name); }
    }
    public static void Reset() {
        Calls.Clear(); Blocked.Clear(); Core = Home = AllowAudio = true; Pause = Suspended = false;
        ThrowBackend = ThrowAutomatic = ThrowFrameFailed = ThrowRefresh = ThrowNotice = ThrowShield = ThrowCleanup = ThrowOwnedCleanup = false;
        ThrowCatalogWarning = ThrowCatalogStage = ThrowCatalogWrite = ThrowCatalogFailure = ThrowCatalogError = false;
        Checking = Discovery = false; HomeReads = NativeCalls = OwnedCleanupAttempts = 0; Time.frameCount = 1; ModUi.Reset(); Plugin.ResetCatalogObserver(); Plugin.AutomaticChecks.Value = true; Plugin.Updates.TransitionCheckPending = false;
    }
    public static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    public static int Count(string name) { return Calls.FindAll(x => x == name).Count; }
}
static class Time { internal static int frameCount; internal static float realtimeSinceStartup = 0f; }
static class StartupGuard { internal const float HealthySeconds = 90f; internal static int HealthyCalls; internal static void Healthy() { HealthyCalls++; } }
enum AutomaticUpdateStatus { Idle, CheckingInstallation }
enum DiagnosticFeature { Coordinator, CoreUi, Loader }
enum DiagnosticPhase { None, Catalog }
enum DiagnosticOutcome { Partial }
enum DiagnosticCode { Unknown, CompatibilityBlocked, PvpExcluded, StartupWaiting, None }
static class DiagnosticHub {
    internal static void Failure(DiagnosticFeature feature, DiagnosticPhase phase, DiagnosticCode code, Exception ex) {
        Probe.Visit("diagnostic.failure." + feature);
        if (feature == DiagnosticFeature.Loader && Probe.ThrowCatalogFailure) throw new Exception("inert catalog failure writer failure");
    }
    internal static void Stage(DiagnosticFeature feature, DiagnosticPhase phase, DiagnosticOutcome outcome) {
        Probe.Require(feature == DiagnosticFeature.Loader && phase == DiagnosticPhase.Catalog && outcome == DiagnosticOutcome.Partial, "Catalog stage remains advisory");
        Probe.Visit("catalog.stage"); if (Probe.ThrowCatalogStage) throw new Exception("inert catalog stage writer failure");
    }
    internal static void Gate(DiagnosticFeature feature, DiagnosticCode code) { Probe.Visit("diagnostic.gate." + code); }
}
static class Compatibility {
    internal static bool Allowed(string feature) { return !Probe.Blocked.Contains(feature) && (feature == "CoreUi" ? Probe.Core : feature == "MuteUnfocused" ? Probe.AllowAudio : true); }
    internal static void Block(string feature, Exception ex, Action cleanup = null) { Probe.Visit("block." + feature); Probe.Blocked.Add(feature); if (cleanup != null) cleanup(); }
}
sealed class ReleaseUpdates {
    internal bool TransitionCheckPending;
    internal void InstalledChannel(string channel) { Probe.Visit("updates.installed"); }
    internal void Tick(DateTimeOffset now, bool discover) { Probe.Visit("updates.tick"); Probe.Discovery = discover; if (Probe.ThrowBackend) throw new Exception("inert backend failure"); }
    internal void FrameFailed() { Probe.Visit("updates.failed"); if (Probe.ThrowFrameFailed) throw new Exception("inert failure handler failure"); }
}
sealed class Snapshot { internal string InstalledChannel = "Stable"; }
sealed class AutomaticUpdates {
    internal Snapshot Snapshot = new Snapshot();
    internal AutomaticUpdateStatus Status { get { return Probe.Checking ? AutomaticUpdateStatus.CheckingInstallation : AutomaticUpdateStatus.Idle; } }
    internal void Tick(DateTimeOffset now) { Probe.Visit("automatic.tick"); if (Probe.ThrowAutomatic) throw new Exception("inert automatic failure"); }
}
sealed class Session { internal void Poll() { Probe.Visit("session.poll"); } }
sealed class DiagnosticLog {
    internal void State(string key, string value) { Probe.Visit("diagnostics.state." + value); }
    internal void Error(string key, Exception ex) {
        Probe.Visit("diagnostics.error");
        if (key == "catalog") { Probe.Visit("catalog.error"); if (Probe.ThrowCatalogError) throw new Exception("inert catalog error writer failure"); }
    }
    internal void Write(string value) { Probe.Visit("catalog.write"); if (Probe.ThrowCatalogWrite) throw new Exception("inert catalog log writer failure"); }
}
sealed class CatalogLogger {
    internal void LogWarning(string value) { Probe.Visit("catalog.warning"); if (Probe.ThrowCatalogWarning) throw new Exception("inert catalog warning writer failure"); }
}
sealed class CatalogGuard {
    readonly int _owner = System.Threading.Thread.CurrentThread.ManagedThreadId;
    internal bool Change, ThrowPoll, ThrowChanged;
    internal int Polls, Reads;
    internal bool Changed { get {
        Probe.Require(_owner == System.Threading.Thread.CurrentThread.ManagedThreadId, "Catalog observation stays on the UI caller thread");
        Reads++; Probe.Visit("catalog.changed"); if (ThrowChanged) throw new Exception("inert catalog result failure"); return Change;
    } }
    internal void Poll(DateTime now) {
        Probe.Require(_owner == System.Threading.Thread.CurrentThread.ManagedThreadId, "Catalog polling stays on the UI caller thread");
        Polls++; Probe.Visit("catalog.poll"); if (ThrowPoll) throw new Exception("inert catalog polling failure");
    }
}
sealed class BoolPreference { internal bool Value = true; }
static class Plugin {
    internal const string Version = "inert";
    internal static readonly ReleaseUpdates Updates = new ReleaseUpdates();
    internal static readonly AutomaticUpdates Automatic = new AutomaticUpdates();
    internal static readonly BoolPreference AutomaticChecks = new BoolPreference();
    internal static readonly Session UpdateSession = new Session();
    internal static readonly DiagnosticLog Diagnostics = new DiagnosticLog();
    internal static readonly CatalogLogger Logger = new CatalogLogger();
    internal static CatalogGuard? Catalogs { get; set; }
    private static bool _catalogWarningShown;
    internal static void ResetCatalogObserver() { Catalogs = null; _catalogWarningShown = false; }
    internal static void PollUpdatePreferences() { Probe.Visit("preferences.poll"); }
    internal static void AdoptConfirmedChannel() { }
    internal static void ObserveUpdateDiagnostics() { Probe.Visit("updates.diagnostics"); }
__POLL_CATALOGS__
}
static class UpdateDiscoveryBridge { internal static void Apply(ReleaseUpdates updates, AutomaticUpdates automatic, string version) { Probe.Visit("discovery.apply"); } }
static class GameUi {
    internal static bool HomeAvailable { get { Probe.HomeReads++; Probe.Visit("native.home", true); return Probe.Home; } }
    internal static void RefreshReleaseNotification() { Probe.Visit("native.refresh", true); if (Probe.ThrowRefresh) throw new Exception("inert native notification failure"); }
    internal static void RefreshUpdateProgress() { }
}
static class UpdateNotificationUi {
    internal const string Feature = "UpdateNotification";
    internal static void Observe() { Probe.Visit("native.notice", true); if (Probe.ThrowNotice) throw new Exception("inert notice failure"); }
    internal static void Clear() { Probe.Visit("notice.clear", true); }
}
static class ShushuShieldUi {
    internal const string Feature = "ShushuShield";
    internal static void PollLoads() { Probe.Visit("native.shield", true); if (Probe.ThrowShield) throw new Exception("inert shield failure"); }
    internal static void Clear() { Probe.Visit("shield.clear", true); }
    internal static void ClearDisplay() { Probe.Visit("shield.clear", true); }
}
static class MatchFocus { internal static void Tick() { Probe.Visit("native.matchFocus"); } }
static class PvpSafety {
    internal static bool Suspended { get { return Probe.Suspended; } }
    internal static bool Poll() { Probe.Visit("native.pvp"); return Probe.Pause; }
}
static class FocusAudio {
    internal static void Restore() { Probe.Visit("native.audioRestore"); }
    internal static void Tick() { Probe.Visit("native.audio"); }
}
static class PingFocusGuard { internal static void Tick() { Probe.Visit("native.ping", true); } }
static class InputAttention {
    internal static void Tick() { Probe.Visit("native.attention", true); }
    internal static void Stop() { Probe.Visit("native.attentionStop", true); }
}
static class FieldZoomUi {
    internal static void Clear() {
        Probe.OwnedCleanupAttempts++; Probe.Visit("owned.cameraClear");
        if (Probe.ThrowOwnedCleanup) throw new Exception("inert owned camera cleanup failure");
    }
}
static class ModUi {
    static int _lastFrame = -1;
    static bool _pvpCleaned;
    static readonly Action[] PvpCleanup = { () => { Probe.Visit("native.cleanup", true); if (Probe.ThrowCleanup) throw new Exception("inert cleanup failure"); } };
    internal static void Reset() { _lastFrame = -1; _pvpCleaned = false; }
    static void FailUi(Exception ex) { Probe.Visit("ui.failed"); Probe.Core = false; }
__PREFIX__
        try {
__CATALOG_TICK__
            Probe.Visit("healthy.tail", true);
        } catch (Exception ex) { FailUi(ex); }
    }
}
public static class Runner {
    public static int Run() {
        int cases = 0;
        Probe.Reset(); Probe.Core = false; ModUi.Tick();
        Probe.Require(Probe.Discovery && Probe.Count("updates.tick") == 1 && Probe.Count("automatic.tick") == 1 && Probe.NativeCalls == 0 && Probe.HomeReads == 0 && Probe.OwnedCleanupAttempts == 1 && Probe.Calls.IndexOf("automatic.tick") < Probe.Calls.IndexOf("owned.cameraClear"), "CoreUi-blocked frame must run both backends then owned camera cleanup without general native reads"); cases++;
        Probe.Require(Probe.Count("native.matchFocus") == 1 && Probe.Count("native.pvp") == 1 && Probe.Count("native.audio") == 1
            && Probe.Calls.IndexOf("native.audio") < Probe.Calls.IndexOf("owned.cameraClear"), "Shared UI failure must not suppress independent focus restoration or audio"); cases++;
        Probe.Reset(); Probe.Core = false; Probe.Pause = Probe.Suspended = true; ModUi.Tick(); Time.frameCount++; ModUi.Tick();
        Probe.Require(Probe.Count("native.matchFocus") == 2 && Probe.Count("native.pvp") == 2 && Probe.Count("native.audioRestore") == 2
            && Probe.Count("native.audio") == 0 && Probe.NativeCalls == 0 && Probe.HomeReads == 0, "Blocked shared UI retains fresh PvP audio restoration without accessing failed UI bindings"); cases++;
        Probe.Reset(); Probe.Core = false; Probe.Checking = true; ModUi.Tick();
        Probe.Require(!Probe.Discovery && Probe.Count("automatic.tick") == 1 && Probe.NativeCalls == 0, "Checking installation must suppress blocked-frame discovery without suppressing backend maintenance"); cases++;
        Probe.Reset(); ModUi.Tick();
        Probe.Require(Probe.Discovery && Probe.Count("healthy.tail") == 1 && Probe.Calls.IndexOf("automatic.tick") < Probe.Calls.IndexOf("native.refresh"), "Healthy frame retains UI after backend"); cases++;
        Probe.Reset(); Probe.Home = false; ModUi.Tick();
        Probe.Require(!Probe.Discovery && Probe.HomeReads == 1 && Probe.Count("automatic.tick") == 1, "Healthy non-home frame retains discovery eligibility"); cases++;
        Probe.Reset(); Probe.Home = false; Probe.Blocked.Add("HomeMenu"); ModUi.Tick(); Time.frameCount++; ModUi.Tick();
        Probe.Require(Probe.Discovery && Probe.HomeReads == 0 && Probe.Core && Probe.Count("automatic.tick") == 2 && Probe.Count("healthy.tail") == 2,
            "An unavailable optional home menu cannot suppress discovery or unrelated UI while the shared root remains healthy"); cases++;
        Probe.Reset(); Probe.Blocked.Add("HomeMenu"); Probe.Checking = true; ModUi.Tick();
        Probe.Require(!Probe.Discovery && Probe.Core && Probe.Count("automatic.tick") == 1 && Probe.Count("healthy.tail") == 1,
            "Optional-menu fallback discovery still respects an installation check already in progress"); cases++;
        Probe.Reset(); Probe.Checking = true; ModUi.Tick();
        Probe.Require(!Probe.Discovery && Probe.Count("automatic.tick") == 1, "Healthy installation check suppresses discovery only"); cases++;
        Probe.Reset(); Probe.ThrowBackend = true; ModUi.Tick();
        Probe.Require(Probe.Count("updates.failed") == 1 && Probe.Count("healthy.tail") == 1, "Release backend exception must not stop native UI frame"); cases++;
        Probe.Reset(); Probe.ThrowAutomatic = true; ModUi.Tick();
        Probe.Require(Probe.Count("updates.failed") == 1 && Probe.Count("healthy.tail") == 1, "Automatic backend exception must not stop native UI frame"); cases++;
        Probe.Reset(); Probe.ThrowBackend = Probe.ThrowFrameFailed = true; ModUi.Tick();
        Probe.Require(Probe.Count("healthy.tail") == 1, "Updater failure reporting exception must not stop native UI frame"); cases++;
        Probe.Reset(); Probe.ThrowRefresh = true; ModUi.Tick(); Probe.ThrowRefresh = false; Time.frameCount++; ModUi.Tick();
        Probe.Require(Probe.Core && Probe.Count("ui.failed") == 0 && Probe.Count("block.UpdateNotification") == 1 && Probe.Count("native.refresh") == 1
            && Probe.Count("updates.tick") == 2 && Probe.Count("automatic.tick") == 2 && Probe.Count("healthy.tail") == 2
            && Probe.Count("native.matchFocus") == 2 && Probe.Count("native.audio") == 2 && Probe.Discovery,
            "Optional notification decoration failure disables only its own feature; other UI, updates, focus and audio continue without retrying failed bindings"); cases++;
        Probe.Reset(); Probe.Pause = Probe.Suspended = true; ModUi.Tick(); Time.frameCount++; ModUi.Tick();
        Probe.Require(Probe.Count("automatic.tick") == 2 && Probe.Count("native.cleanup") == 1 && Probe.Count("native.audioRestore") == 2 && Probe.Count("native.ping") == 0 && Probe.Count("healthy.tail") == 0, "PvP still suspends gameplay, restores audio and cleans once while backend remains independent"); cases++;
        Probe.Reset(); Probe.Pause = true; ModUi.Tick();
        Probe.Require(Probe.Count("diagnostics.state.initializing") == 1 && Probe.Count("native.cleanup") == 1 && Probe.Count("healthy.tail") == 0, "Startup pause retains its existing gameplay gate"); cases++;
        Probe.Reset(); Probe.Pause = Probe.ThrowCleanup = true; ModUi.Tick(); Time.frameCount++; ModUi.Tick();
        Probe.Require(Probe.Count("native.cleanup") == 2 && Probe.Count("diagnostics.error") == 2 && Probe.Count("healthy.tail") == 0, "Failed PvP cleanup retries on a later frame without admitting gameplay"); cases++;
        Probe.Reset(); Probe.ThrowNotice = true; ModUi.Tick();
        Probe.Require(Probe.Core && Probe.Count("block.UpdateNotification") == 1 && Probe.Count("healthy.tail") == 1, "Notice failure remains feature-local"); cases++;
        Probe.Reset(); Probe.ThrowShield = true; ModUi.Tick();
        Probe.Require(Probe.Core && Probe.Count("block.ShushuShield") == 1 && Probe.Count("healthy.tail") == 1, "Shield failure remains feature-local"); cases++;
        Probe.Reset(); ModUi.Tick(); ModUi.Tick();
        Probe.Require(Probe.Count("updates.tick") == 1 && Probe.Count("automatic.tick") == 1 && Probe.Count("healthy.tail") == 1, "Repeated engine callbacks in one frame remain deduplicated"); cases++;
        Probe.Reset(); Probe.Core = false; Probe.ThrowBackend = true; ModUi.Tick();
        Probe.Require(Probe.Count("updates.failed") == 1 && Probe.NativeCalls == 0 && Probe.OwnedCleanupAttempts == 1, "Blocked CoreUi plus updater failure must still run owned cleanup without general native calls"); cases++;
        Probe.Reset(); Probe.Core = false; Probe.ThrowOwnedCleanup = true; ModUi.Tick(); Time.frameCount++; ModUi.Tick();
        Probe.Require(!Probe.Core && Probe.Count("updates.tick") == 2 && Probe.Count("automatic.tick") == 2 && Probe.OwnedCleanupAttempts == 2 && Probe.Count("diagnostic.failure.CoreUi") == 2 && Probe.NativeCalls == 0 && Probe.HomeReads == 0, "Owned cleanup failure must be contained, diagnosed and retried next frame without general native reads"); cases++;
        Probe.Reset(); Probe.Core = false; Probe.ThrowOwnedCleanup = true; ModUi.Tick(); Probe.ThrowOwnedCleanup = false; Time.frameCount++; ModUi.Tick();
        Probe.Require(Probe.Count("automatic.tick") == 2 && Probe.OwnedCleanupAttempts == 2 && Probe.Count("diagnostic.failure.CoreUi") == 1 && Probe.NativeCalls == 0 && Probe.HomeReads == 0, "Later successful owned cleanup must not suppress continuing backend frames"); cases++;
        Probe.Reset(); Probe.Core = false; ModUi.Tick(); ModUi.Tick();
        Probe.Require(Probe.Count("updates.tick") == 1 && Probe.Count("automatic.tick") == 1 && Probe.OwnedCleanupAttempts == 1 && Probe.NativeCalls == 0, "Owned cleanup remains deduplicated with the engine frame"); cases++;
        Probe.Reset(); ModUi.Tick();
        Probe.Require(Probe.OwnedCleanupAttempts == 0 && Probe.Count("healthy.tail") == 1, "Healthy frame must not use compatibility-blocked owned cleanup"); cases++;
        foreach (var core in new[] { false, true }) {
            Probe.Reset(); Probe.Core = core; Plugin.AutomaticChecks.Value = false; ModUi.Tick();
            Probe.Require(!Probe.Discovery && Probe.Count("updates.tick") == 1 && Probe.Count("automatic.tick") == 1
                && Probe.Count("session.poll") == 2 && Probe.Count("healthy.tail") == (core ? 1 : 0),
                "Automatic discovery OFF must preserve manual/backend processing and the independent native UI boundary"); cases++;
            Plugin.AutomaticChecks.Value = true; Time.frameCount++; ModUi.Tick();
            Probe.Require(Probe.Discovery && Probe.Count("updates.tick") == 2 && Probe.Count("automatic.tick") == 2,
                "Re-enabling discovery must recover on the next eligible frame without resetting backend state"); cases++;
            Plugin.AutomaticChecks.Value = false; Plugin.Updates.TransitionCheckPending = true; Time.frameCount++; ModUi.Tick();
            Probe.Require(Probe.Discovery && Probe.Count("updates.tick") == 3 && Probe.Count("automatic.tick") == 3,
                "Explicit channel selection can finish its deferred check while periodic discovery is OFF"); cases++;
            Plugin.Updates.TransitionCheckPending = false; Time.frameCount++; ModUi.Tick();
            Probe.Require(!Probe.Discovery && Probe.Count("automatic.tick") == 4,
                "Completing the explicit check does not re-enable periodic discovery"); cases++;
        }
        return cases;
    }
}
public static class CatalogRunner {
    public static int Cases;
    static void Frames(int count) { for (int i = 0; i < count; i++) { ModUi.Tick(); Time.frameCount++; } }
    static void Continuing(int count) {
        Probe.Require(Probe.Core && Probe.Count("ui.failed") == 0 && Probe.Count("updates.tick") == count && Probe.Count("automatic.tick") == count
            && Probe.Count("native.refresh") == count && Probe.Count("native.notice") == count && Probe.Count("healthy.tail") == count,
            "Catalog observation must not disable CoreUi, updates, native notice or the continuing UI frame");
    }
    static void Disabled(CatalogGuard observer) {
        Probe.Require(Plugin.Catalogs == null && observer.Polls == 1 && Probe.Count("diagnostic.failure.Loader") == 1
            && Probe.Count("catalog.error") == 1 && Probe.Count("block.CoreUi") == 0,
            "Only the failed catalog observer is retired and each independent failure reporter is attempted once");
        Continuing(2);
    }
    public static int Run() {
        Cases = 0;
        Probe.Reset(); Frames(2); Continuing(2);
        Probe.Require(Probe.Count("catalog.poll") == 0 && Probe.Count("catalog.warning") == 0, "No observer leaves a healthy frame untouched"); Cases++;

        Probe.Reset(); var observer = new CatalogGuard(); Plugin.Catalogs = observer; Frames(3); Continuing(3);
        Probe.Require(Plugin.Catalogs == observer && observer.Polls == 3 && observer.Reads == 3 && Probe.Count("catalog.warning") == 0,
            "An unchanged catalog keeps observing without warning or a feature breaker"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { Change = true }; Plugin.Catalogs = observer; Frames(3); Continuing(3);
        Probe.Require(Plugin.Catalogs == observer && observer.Polls == 3 && Probe.Count("catalog.warning") == 1 && Probe.Count("catalog.stage") == 1 && Probe.Count("catalog.write") == 1,
            "Changed catalogs warn once while polling and all existing UI continue"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { Change = true }; Plugin.Catalogs = observer; Frames(1); observer.Change = false; Frames(1); observer.Change = true; Frames(2); Continuing(4);
        Probe.Require(observer.Polls == 4 && Probe.Count("catalog.warning") == 1 && Probe.Count("catalog.stage") == 1 && Probe.Count("catalog.write") == 1,
            "Transient recovery does not reset the session warning latch"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { ThrowPoll = true }; Plugin.Catalogs = observer; Frames(2); Disabled(observer);
        Probe.Require(observer.Reads == 0 && Probe.Count("catalog.warning") == 1, "Polling failure stops only its observer and is not retried next frame"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { ThrowChanged = true }; Plugin.Catalogs = observer; Frames(2); Disabled(observer);
        Probe.Require(observer.Reads == 1 && Probe.Count("catalog.warning") == 1, "A result-read failure has the same local observer boundary"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { ThrowPoll = true }; Plugin.Catalogs = observer;
        Probe.ThrowCatalogFailure = Probe.ThrowCatalogError = Probe.ThrowCatalogWarning = true; Frames(2); Disabled(observer);
        Probe.Require(Probe.Count("catalog.warning") == 1, "All three failing diagnostic sinks remain independent and cannot escape into CoreUi"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { Change = true }; Plugin.Catalogs = observer; Probe.ThrowCatalogWarning = true; Frames(2); Disabled(observer);
        Probe.Require(Probe.Count("catalog.warning") == 2 && Probe.Count("catalog.stage") == 0 && Probe.Count("catalog.write") == 0,
            "A failing first advisory warning retires observation only, including a failing fallback warning"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { Change = true }; Plugin.Catalogs = observer; Probe.ThrowCatalogStage = true; Frames(2); Disabled(observer);
        Probe.Require(Probe.Count("catalog.warning") == 2 && Probe.Count("catalog.stage") == 1 && Probe.Count("catalog.write") == 0,
            "An advisory stage diagnostic failure does not reach the UI breaker"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { Change = true }; Plugin.Catalogs = observer; Probe.ThrowCatalogWrite = true; Frames(2); Disabled(observer);
        Probe.Require(Probe.Count("catalog.warning") == 2 && Probe.Count("catalog.stage") == 1 && Probe.Count("catalog.write") == 1,
            "An advisory log failure does not reach the UI breaker"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { Change = true }; Plugin.Catalogs = observer; Probe.Pause = Probe.Suspended = true; Frames(2);
        Probe.Require(observer.Polls == 0 && Plugin.Catalogs == observer && Probe.Core && Probe.Count("automatic.tick") == 2 && Probe.Count("healthy.tail") == 0,
            "Catalog observation does not bypass the existing PvP gameplay pause"); Cases++;

        Probe.Reset(); observer = new CatalogGuard { ThrowPoll = true }; Plugin.Catalogs = observer; Probe.Core = false; Frames(2);
        Probe.Require(observer.Polls == 0 && Plugin.Catalogs == observer && Probe.NativeCalls == 0 && Probe.Count("automatic.tick") == 2 && Probe.OwnedCleanupAttempts == 2,
            "A blocked CoreUi still runs independent backends without polling gameplay observers or native UI"); Cases++;
        Probe.Reset(); return Cases;
    }
}
}
