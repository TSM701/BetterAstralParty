using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BetterAstralParty;

// Development builds only (user-requested 2026-11-19, after 유령 골목 ran at about 230 fps with BAP's own tick at 0.73 ms of a
// 4.3 ms frame), while diagnostics record.
// Always on: every 10 s `performance_frame` gives the frame time spread (p50, p95, p99, max), the CPU main thread, render
// thread and GPU medians when the build has frame timing stats on (FrameTimingManager.IsFeatureEnabled), and what was on
// screen: the map, the zoom scale range, the most monster plates, and the share of frames in a fight and with the PiP small.
// Frame times go into fixed 0.05 ms bins, so no list grows or is sorted.
// On demand: Ctrl+Shift+F3 on the field runs a bottleneck test of seven steps, each changing one thing against the same still
// view and putting it back afterwards: base, render scale 50 %, shadows off (URP shadowDistance 0), post-processing off on
// the field camera, MSAA 1x, BAP's tick paused (ModUi.Tick skipped), base again. Each step settles 2 s and logs
// `frameProbe.step` from 10 sampled seconds. A fight, the mod settings or the game losing focus pauses the step (its change
// goes back, its samples wait) and it goes on 2 s after that ends (user-requested 2026-11-19, as fights ended the test); a
// second press, a scene change or PvP stops it. Every changed value goes back on each step change, pause and at the end.
// While a step measures, V-Sync and the frame rate limit are off (user-requested 2026-11-20: under a 320 custom cap every step
// read 3.12 ms, so no change could show); the game's values come back on a pause and at the end, and Custom Frame Rate leaves
// the frame rate alone meanwhile (HoldsFrameRate). A capsule at the top of the screen shows the step, the seconds left or why
// it waits, and then how it ended for 4 s (user-requested 2026-11-20).
internal static class FrameProbe
{
    private static readonly bool DevelopmentBuild = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private const float SettleSeconds = 2f, WindowSeconds = 10f;
    private static readonly string[] Steps = { "base", "scale50", "noShadows", "noPost", "msaa1", "modPaused", "base2" };
    private static readonly Times Frames = new(), Main = new(), Render = new(), Gpu = new();
    private static readonly Times WindowFrames = new(), WindowMain = new(), WindowRender = new(), WindowGpu = new();
    private static int _step = -1, _modTicks, _windowFight, _windowPip, _monstersMax, _frameIndex, _pauses;
    private static long _modTimestamps;
    private static float _settleUntil, _sampled, _windowAt = -1f, _zoomMin = float.MaxValue, _zoomMax;
    // Why the running step waits, or null while it measures.
    private static string? _paused;
    // This frame counts toward the step (not paused, settled), so BAP's tick time joins it.
    private static bool _sampling;
    private static bool _inFight;
    private static IntPtr _scene, _sceneClass, _logicClass;
    private static RuntimeObject? _asset, _cameraData;
    private static (float Scale, float Shadows, int Msaa, bool Post) _kept;
    // The game's V-Sync and frame rate while a measuring step has them off.
    private static (int VSync, int Target)? _cap;
    private static Il2CppStructArray<FrameTiming>? _timing;
    private static bool? _timingOn;
    // The always-on window's reading of this frame, which a running test reuses (one capture a frame).
    private static (float Main, float Render, float Gpu)? _lastTiming;

    // Frame times in 0.05 ms bins up to 100 ms, the rest in the last bin; percentiles from the running counts.
    private sealed class Times
    {
        private const float Bin = 0.05f;
        private readonly int[] _bins = new int[2001];
        internal int Count { get; private set; }
        internal double Sum { get; private set; }
        internal float Max { get; private set; }
        internal void Add(float ms)
        {
            if (!(ms >= 0)) return;
            _bins[Math.Min(_bins.Length - 1, (int)(ms / Bin))]++;
            Count++; Sum += ms; if (ms > Max) Max = ms;
        }
        internal float Percentile(float share)
        {
            var target = (int)Math.Ceiling(share * Count);
            for (int i = 0, seen = 0; i < _bins.Length; i++)
                if ((seen += _bins[i]) >= target && target > 0) return (i + 0.5f) * Bin;
            return 0f;
        }
        internal void Reset() { Array.Clear(_bins); Count = 0; Sum = 0; Max = 0; }
    }

    internal static bool Running => _step >= 0 && _sampling;
    internal static bool PausesMod => _step >= 0 && _paused == null && Steps[_step] == "modPaused";
    internal static bool HoldsFrameRate => _cap != null;

    // BAP's own tick time while a test runs (ModUi.Tick around TickCore).
    internal static void ModTime(long started)
    {
        _modTimestamps += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        _modTicks++;
    }

    internal static void Tick()
    {
        if (!DevelopmentBuild) return;
        try
        {
            Observe();
            TickCore();
        }
        catch (Exception ex)
        {
            try { Restore(); } catch { /* Restore logs its own failure. */ }
            try { Uncap(false); } catch { _cap = null; }
            try { Indicator.Hide(); } catch { /* A disposed capsule has nothing left to hide. */ }
            _step = -1;
            _paused = null;
            Plugin.Diagnostics.State("frameProbe", "failed " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // The always-on window.
    private static void Observe()
    {
        if (!Plugin.Diagnostics.IsRecording) { _windowAt = -1f; return; }
        var now = Time.unscaledTime;
        if (_windowAt < 0) { ResetWindow(now); return; }
        var ms = Time.unscaledDeltaTime * 1000f;
        WindowFrames.Add(ms);
        var timing = _lastTiming = ReadTiming();
        if (timing is { } t) { WindowMain.Add(t.Main); WindowRender.Add(t.Render); WindowGpu.Add(t.Gpu); }
        var zoom = FieldZoomUi.IndicatorScale;
        if (zoom < _zoomMin) _zoomMin = zoom;
        if (zoom > _zoomMax) _zoomMax = zoom;
        _monstersMax = Math.Max(_monstersMax, PlayerIndicatorUi.MonsterPlateCount);
        if (FightPip.Picture != null) _windowPip++;
        // A fight's state is read every 30th frame; the frames between count as that reading.
        if (_frameIndex++ % 30 == 0) _inFight = FightActive();
        if (_inFight) _windowFight++;
        if (now - _windowAt < WindowSeconds) return;
        var frames = WindowFrames.Count;
        if (frames > 0)
        {
            var line = FormattableString.Invariant(
                $"frames={frames}; fps={frames / (WindowFrames.Sum / 1000):F0}; frame_p50={WindowFrames.Percentile(.5f):F2}; frame_p95={WindowFrames.Percentile(.95f):F2}; frame_p99={WindowFrames.Percentile(.99f):F2}; frame_max={WindowFrames.Max:F1}");
            if (WindowMain.Count > 0)
                line += FormattableString.Invariant($"; main_p50={WindowMain.Percentile(.5f):F2}; render_p50={WindowRender.Percentile(.5f):F2}; gpu_p50={WindowGpu.Percentile(.5f):F2}");
            line += FormattableString.Invariant(
                $"; timing={(_timingOn == true ? "on" : "off")}; map={MapId()}; zoom={_zoomMin:F2}-{_zoomMax:F2}; monsters_max={_monstersMax}; fight_share={(float)_windowFight / frames:F2}; pip_share={(float)_windowPip / frames:F2}");
            Plugin.Diagnostics.Write("performance_frame " + line);
        }
        ResetWindow(now);
    }

    private static void ResetWindow(float now)
    {
        _windowAt = now;
        WindowFrames.Reset(); WindowMain.Reset(); WindowRender.Reset(); WindowGpu.Reset();
        _windowFight = _windowPip = _monstersMax = 0;
        _zoomMin = float.MaxValue; _zoomMax = 0;
    }

    private static (float Main, float Render, float Gpu)? ReadTiming()
    {
        _timingOn ??= FrameTimingManager.IsFeatureEnabled();
        if (_timingOn != true) return null;
        FrameTimingManager.CaptureFrameTimings();
        _timing ??= new Il2CppStructArray<FrameTiming>(1);
        if (FrameTimingManager.GetLatestTimings(1, _timing) != 1) return null;
        var timing = _timing[0];
        return ((float)timing.cpuMainThreadFrameTime, (float)timing.cpuRenderThreadFrameTime, (float)timing.gpuFrameTime);
    }

    private static RuntimeObject? Logic()
    {
        // The game's logic loads after start-up; until then there is no fight or map to read.
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        return _logicClass == IntPtr.Zero ? null : RuntimeObject.StaticField(_logicClass, "_inst");
    }

    private static bool FightActive() => Logic()?.Get("fight")?.Field<bool>("fightStatus") == true;
    private static int MapId() => Logic()?.Get("room")?.Get("curRoomInfo")?.Get<int>("MapId") ?? 0;

    private static void TickCore()
    {
        var key = Input.GetKeyDown(KeyCode.F3) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        var now = Time.unscaledTime;
        Indicator.Tick(now);
        if (_step < 0) { if (key) Start(now); return; }
        if (key) { Stop("cancelled"); return; }
        if (Ended() is { } reason) { Stop(reason); return; }
        if (Hold() is { } hold)
        {
            if (_paused == null) Pause(hold);
            _sampling = false;
            Indicator.Running(_step, 0, _paused);
            return;
        }
        if (_paused != null)
        {
            _paused = null;
            Change();
            _settleUntil = now + SettleSeconds;
        }
        Uncap(true);
        _sampling = now >= _settleUntil;
        if (_sampling)
        {
            Sample();
            if ((_sampled += Time.unscaledDeltaTime) >= WindowSeconds) { Next(now); return; }
        }
        Indicator.Running(_step, (int)Math.Ceiling(Math.Max(0f, _settleUntil - now) + WindowSeconds - _sampled), null);
    }

    // V-Sync and the frame rate limit off while a step measures, put back as the game had them; the game may write them on a
    // screen change, so a measuring frame puts its own back.
    private static void Uncap(bool on)
    {
        if (on)
        {
            _cap ??= (QualitySettings.vSyncCount, Application.targetFrameRate);
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
            return;
        }
        if (_cap is not { } kept) return;
        _cap = null;
        QualitySettings.vSyncCount = kept.VSync;
        Application.targetFrameRate = kept.Target;
    }

    private static void Pause(string reason)
    {
        Restore();
        Uncap(false);
        _paused = reason;
        _pauses++;
        Plugin.Diagnostics.State("frameProbe", "paused: " + reason + " at " + Steps[_step]);
    }

    private static void Start(float now)
    {
        // The results are log lines only.
        if (!Plugin.Diagnostics.IsRecording) { Indicator.Finish("no diagnostics", now); return; }
        if (_sceneClass == IntPtr.Zero) _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
        var scene = _sceneClass == IntPtr.Zero ? null : RuntimeObject.StaticField(_sceneClass, "inst");
        var camera = scene?.Get("mainCamera");
        if (scene == null || camera == null || Ended() is { } blocked)
        {
            var why = scene == null || camera == null ? "no field" : Ended()!;
            Plugin.Diagnostics.State("frameProbe", "not started: " + why);
            Indicator.Finish(why, now);
            return;
        }
        _scene = scene.Pointer;
        _asset = RuntimeObject.StaticCall(RuntimeObject.FindClass("UnityEngine.Rendering", "GraphicsSettings"), "get_currentRenderPipeline");
        _cameraData = RuntimeObject.StaticCall(RuntimeObject.FindClass("UnityEngine.Rendering.Universal", "CameraExtensions"),
            "GetUniversalAdditionalCameraData", camera);
        if (_asset == null || _cameraData == null)
        {
            Plugin.Diagnostics.State("frameProbe", "not started: no URP asset or camera data");
            Indicator.Finish("no field", now);
            return;
        }
        _kept = (_asset.Get<float>("renderScale"), _asset.Get<float>("shadowDistance"), _asset.Get<int>("msaaSampleCount"),
            _cameraData.Get<bool>("renderPostProcessing"));
        _timingOn ??= FrameTimingManager.IsFeatureEnabled();
        Plugin.Diagnostics.State("frameProbe", FormattableString.Invariant(
            $"start frameTiming={_timingOn}; map={MapId()}; renderScale={_kept.Scale:F2}; shadowDistance={_kept.Shadows:F1}; msaa={_kept.Msaa}; post={_kept.Post}; vSync={QualitySettings.vSyncCount}; targetFps={Application.targetFrameRate}; screen={Screen.width}x{Screen.height}"));
        // Pressed during a fight or the like, the test starts waiting and the first change comes once that ends.
        _paused = Hold();
        Begin(0, now);
        if (_paused != null) Plugin.Diagnostics.State("frameProbe", "paused: " + _paused + " at " + Steps[0]);
        Indicator.Running(0, (int)(SettleSeconds + WindowSeconds), _paused);
    }

    // What ends the test: the match is over or turned out PvP.
    private static string? Ended()
    {
        if (PvpSafety.Suspended) return "pvp";
        if (_sceneClass != IntPtr.Zero && _step >= 0 && RuntimeObject.StaticField(_sceneClass, "inst")?.Pointer != _scene) return "scene changed";
        return null;
    }

    // What the test waits out: another view in front of the field, or a window in front of the game.
    private static string? Hold()
    {
        if (!Application.isFocused) return "unfocused";
        if (ModUi.IsOpen) return "settings";
        // The game's settings window can change the frame rate, which the measuring step holds off.
        if (GameUi.Root is { } root && GameUi.Find(root, "SettingWindow", maxDepth: 1) != null) return "game settings";
        if (FightActive()) return "fight";
        return null;
    }

    private static void Sample()
    {
        Frames.Add(Time.unscaledDeltaTime * 1000f);
        if ((Plugin.Diagnostics.IsRecording ? _lastTiming : ReadTiming()) is { } t) { Main.Add(t.Main); Render.Add(t.Render); Gpu.Add(t.Gpu); }
    }

    private static void Next(float now)
    {
        Report();
        Restore();
        if (_step + 1 >= Steps.Length) { Stop("done"); return; }
        Begin(_step + 1, now);
    }

    private static void Begin(int step, float now)
    {
        _step = step; _settleUntil = now + SettleSeconds; _sampled = 0; _pauses = 0; _sampling = false;
        Frames.Reset(); Main.Reset(); Render.Reset(); Gpu.Reset();
        _modTimestamps = 0; _modTicks = 0;
        if (_paused == null) Change();
    }

    // The running step's one change.
    private static void Change()
    {
        switch (Steps[_step])
        {
            case "scale50": _asset!.Set("renderScale", _kept.Scale * 0.5f); break;
            case "noShadows": _asset!.Set("shadowDistance", 0f); break;
            case "noPost": _cameraData!.Set("renderPostProcessing", false); break;
            case "msaa1": _asset!.Set("msaaSampleCount", 1); break;
        }
    }

    private static void Report()
    {
        if (Frames.Count == 0) return;
        var mod = _modTicks == 0 ? 0 : _modTimestamps * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _modTicks;
        Plugin.Diagnostics.State("frameProbe.step", FormattableString.Invariant(
            $"{Steps[_step]}: fps={Frames.Count / (Frames.Sum / 1000):F0}; frame_p50={Frames.Percentile(.5f):F2}; frame_p95={Frames.Percentile(.95f):F2}; mod_ms={mod:F3}; paused={_pauses}")
            + (Main.Count == 0 ? "" : FormattableString.Invariant(
                $"; main_p50={Main.Percentile(.5f):F2}; render_p50={Render.Percentile(.5f):F2}; gpu_p50={Gpu.Percentile(.5f):F2}")));
    }

    private static void Stop(string reason)
    {
        Restore();
        Uncap(false);
        Plugin.Diagnostics.State("frameProbe", (reason == "done" ? "done" : "stopped: " + reason) + " at " + (_step >= 0 ? Steps[_step] : "-"));
        _step = -1;
        _paused = null;
        Indicator.Finish(reason, Time.unscaledTime);
    }

    // A cream capsule at the top of the screen, made only for a test: the step, the seconds left or why it waits, and how the
    // test ended for 4 s. Its text changes about once a second, so a measuring step pays almost nothing for it.
    private static class Indicator
    {
        // Below the round bar and its location pin (to about 76 at 1080p), which drew over the capsule at 14 (dev.11).
        private const float Width = 640f, Height = 44f, Top = 92f, EndSeconds = 4f;
        private static RuntimeObject? _box, _label;
        private static int _shown = -2, _seconds = -1, _language = -1;
        private static string? _pause;
        private static float _hideAt = -1f;

        internal static void Running(int step, int seconds, string? paused)
        {
            _hideAt = -1f;
            if (step == _shown && seconds == _seconds && paused == _pause && _language == ModText.Revision && Alive()) return;
            _shown = step; _seconds = seconds; _pause = paused; _language = ModText.Revision;
            Show(ModText.FrameTestStatus(step + 1, Steps.Length, Steps[step], seconds, paused));
        }

        internal static void Finish(string reason, float now)
        {
            _shown = -2;
            _hideAt = now + EndSeconds;
            Show(ModText.FrameTestEnd(reason));
        }

        internal static void Tick(float now)
        {
            if (_hideAt >= 0f && now >= _hideAt) Hide();
        }

        internal static void Hide()
        {
            GameUi.Dispose(_box);
            _box = _label = null;
            _shown = -2;
            _hideAt = -1f;
        }

        private static bool Alive() => _box != null && !_box.Get<bool>("isDisposed");

        private static void Show(string text)
        {
            var root = GameUi.Root;
            if (root == null) return;
            if (!Alive())
            {
                var box = _box = NativeUi.Component(root, Width, Height);
                box.Set("touchable", false);
                _ = new NativeUi.Surface(box, Width, Height, fixedOpacity: true);
                var label = _label = NativeUi.Label(box, "", 0, 0, Width, Height, 22, dark: true);
                label.Set("verticalAlign", 1);
            }
            var shown = _box!;
            shown.Call("SetXY", (root.Get<float>("width") - Width) / 2f, Top);
            // Windows the game opens later go above it; the next text change brings it back on top.
            var last = root.Get<int>("numChildren") - 1;
            if (root.Call("GetChildIndex", shown)!.Value<int>() != last) root.Call("SetChildIndex", shown, last);
            _label!.Set("text", text);
        }
    }

    // The step's one change goes back; a disposed asset or camera has nothing to restore.
    private static void Restore()
    {
        if (_step < 0) return;
        try
        {
            switch (Steps[_step])
            {
                case "scale50": _asset?.Set("renderScale", _kept.Scale); break;
                case "noShadows": _asset?.Set("shadowDistance", _kept.Shadows); break;
                case "noPost": _cameraData?.Set("renderPostProcessing", _kept.Post); break;
                case "msaa1": _asset?.Set("msaaSampleCount", _kept.Msaa); break;
            }
        }
        catch (Exception ex) { Plugin.Diagnostics.State("frameProbe", "restore failed " + ex.GetType().Name); }
    }
}
