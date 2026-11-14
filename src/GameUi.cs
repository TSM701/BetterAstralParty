using System.Globalization;
using UnityEngine;

namespace BetterAstralParty;

internal static class GameUi
{
    private static IntPtr _rootClass, _textClass, _logicClass, _battleClass;
    private static RuntimeObject? _root, _home, _menu, _pressed, _fight;
    private static Vector3 _pressedAt;
    private static AdviceBadge? _defBadge, _dodgeBadge;
    private static RuntimeObject? _settingsHome;
    private static bool _settingsHomeTouchable;
    private static RuntimeObject? _menuAnchor, _menuPlate;
    private static float _menuOffsetX;
    // Development builds only (user-requested 2026-11-14): Ctrl+Shift+F11 on the home steps the progress capsule through
    // downloading, done and stopped, then off, from a private UpdateProgress; no update state changes and its clicks do nothing.
    private static readonly bool DevelopmentBuild = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private static UpdateProgress? _previewProgress;
    private static int _previewStep;
    private static int _menuLanguageRevision = -1;
    private static (IntPtr Menu, int Language, string? Version)? _releaseNotice;
    // Progress capsule hung under the Mod Settings button while an update runs; it moves and fades with the button.
    private static RuntimeObject? _progressCap, _progressLabel, _progressFill, _progressDoor, _progressShadow, _progressReason, _progressCode;
    private static RuntimeObject? _progressIconLoad, _progressIconDone, _progressIconStop;
    private static float _progressTrackWidth;
    private static bool _progressStoppedLayout, _progressHover;
    private static (IntPtr Menu, int Progress, int Language)? _progressShown;
    // The new-version toast left of the button (UpdateNotificationUi.Toast): it slides in, runs a timer bar and, like the
    // progress capsule, is a click target; its own x closes it.
    private static RuntimeObject? _menuButton, _toastView, _toastClose, _toastTimer;
    private static string? _toastKey;
    private static bool _toastHover;
    private const float ToastWidth = 470f, ToastHeight = 78f;
    internal static bool ToastAvailable => Compatibility.Allowed("HomeMenu") && Visible(_menu);
    internal static bool ToastShown => Visible(_toastView) && _toastView!.Get<float>("alpha") > 0;
    internal static bool ToastHovered => _toastHover;
    private static (float X, float Y, float Alpha, bool Visible, bool Touchable)? _menuState;
    private static RuntimeObject? _defButton, _dodgeButton;
    private static ChoiceVisual? _observerSummary;
    private static IntPtr _observerParent;
    private static bool _adviceShowing;
    private static string _lastError = "", _lastAdvice = "";
    private static float _nextScan;
    private static VisibleCombat? _calculationInput;
    private static VisibleAdvice? _calculationResult;
    private static EncounterInput? _encounterInput;
    private static EncounterAdvice? _encounterResult;
    private static readonly List<RuntimeObject> HitPath = new();
    private static int _hitFrame = -1;
    internal static bool HomeAvailable { get; private set; }
    internal static RuntimeObject? Root => _root;

    // Read current native state at display time; HomeAvailable is a sampled discovery hint.
    internal static UpdateNotificationHome NotificationHome()
    {
        if (!HomeAvailable || !Visible(_home) || !Visible(_menuAnchor) || _root == null)
            return default;
        RuntimeObject.RequireClasses(UpdateNotificationUi.Feature, ("UI", "UIManager"), ("GameLogic", "GameLogicManager"));
        var manager = RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst");
        var panel = manager?.Get("currentPanel");
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var match = logic?.Get("match")?.Field("matchData");
        var room = logic?.Get("room")?.Field("roomController");
        if (manager == null || panel == null || logic == null || match == null || room == null)
            return default;
        var currentHome = panel.TypeName == "HomePanel" && panel.Get("ui")?.Pointer == _home!.Pointer;
        var controlsReady = _home!.Get<bool>("touchable") && _home.Get<float>("alpha") > 0
            && _home.Get<float>("scaleX") > 0 && _home.Get<float>("scaleY") > 0
            && !_home.Field("Cut_in")!.Get<bool>("playing") && _menuAnchor!.Get<bool>("touchable")
            && _menuAnchor.Get<float>("alpha") > 0 && _menuAnchor.Get<float>("scaleX") > 0
            && _menuAnchor.Get<float>("scaleY") > 0;
        var blocked = ModUi.IsOpen || WindowsReleaseCredentialInput.PromptInProgress
            || Plugin.UpdateSession?.Status == ReleaseSessionStatus.AwaitingInput
            || FocusWindow() != null || _root.Get<bool>("hasModalWindow")
            || _root.Get<bool>("modalWaiting") || _root.Get<bool>("hasAnyPopup");
        // Verified native NONE=0; a disposed team has TeamId=0 with stale MatchStatus.
        return new(true, Visible(_home), currentHome, controlsReady, blocked, match.Get<long>("TeamId"),
            room.Field("roomStateType")!.Value<int>(), logic.Get<long>("connectRoomId"));
    }

    // The window that has the keys: the top of UIManager.propUpWindows. BaseWindow.OnShown pushes every window there and hands it
    // the key events, and OnHide pops it; of the game's windows only the LoadingTipWindow and SystemTipsWindow overlays override
    // OnShown without that. GRoot.GetTopWindow counted SystemTipsWindow, which the game never hides once a tip, notice or invite
    // has shown, so after a matching tip every home Esc (and the shop's bulk buttons) stayed blocked for the session
    // (reproduced 2026-11-14 on 2026.11.0-dev.4: `by=window=SystemTipsWindow`). MatchInfoWindow, the match team's floating widget,
    // keeps the keys while the team lasts but answers none (no OnKeyDown of its own), so it covers nothing either.
    internal static RuntimeObject? FocusWindow()
    {
        var windows = RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst")?.Field("propUpWindows");
        if (windows == null || windows.Get<int>("Count") == 0) return null;
        var top = windows.Call("Peek");
        return Visible(top) && top!.TypeName != "MatchInfoWindow" ? top : null;
    }

    // For diagnostics: what made the home count as covered, by type name only.
    internal static string HomeBlockers()
    {
        if (_root == null) return "";
        var parts = new List<string>();
        if (ModUi.IsOpen) parts.Add("settings");
        if (WindowsReleaseCredentialInput.PromptInProgress || Plugin.UpdateSession?.Status == ReleaseSessionStatus.AwaitingInput) parts.Add("signIn");
        if (FocusWindow() is { } focus) parts.Add(FormattableString.Invariant($"window={focus.TypeName}(modal={focus.Get<bool>("modal")})"));
        if (_root.Get<bool>("hasModalWindow")) parts.Add("modal");
        if (_root.Get<bool>("modalWaiting")) parts.Add("modalWait");
        if (_root.Get<bool>("hasAnyPopup")) parts.Add("popup");
        return string.Join(", ", parts);
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed("CoreUi")) return;
        var gameplay = !_homeOnly;
        // Do not keep last frame's scene objects alive when no feature needs hit testing.
        if (_hitFrame != Time.frameCount) HitPath.Clear();
        try
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.2f;
                if (_rootClass == IntPtr.Zero)
                {
                    _rootClass = RuntimeObject.FindClass("FairyGUI", "GRoot");
                    if (_rootClass == IntPtr.Zero) return;
                    _textClass = RuntimeObject.FindClass("FairyGUI", "GTextField");
                    _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
                    _battleClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
                }
                _root = RuntimeObject.StaticField(_rootClass, "_inst");
                if (_root == null) return;
                RuntimeObject.RequireClasses("CoreUi", ("FairyGUI", "GTextField"), ("FairyGUI", "GComponent"), ("FairyGUI", "GGraph"));
                try { UpdateHome(); }
                catch (Exception ex) { FailHomeMenu(ex); }
                if (gameplay && Compatibility.Allowed("Enabled"))
                {
                    try { UpdateFight(); }
                    catch (Exception ex) { Compatibility.Block("Enabled", ex, ClearAdvice); }
                }
            }
            if (gameplay && _root != null) CharacterNameUi.Tick(_root);
            if (Compatibility.Allowed("HomeMenu"))
            {
                try { FollowHomeMenu(); HandleMenuInput(); }
                catch (Exception ex) { FailHomeMenu(ex); }
            }
            if (gameplay && Compatibility.Allowed("Enabled"))
            {
                try
                {
                    UpdateHover();
                    _defBadge?.Animate();
                    _dodgeBadge?.Animate();
                    _observerSummary?.Tick();
                    CardDiceUi.PollResolved();
                    CardUi.Animate();
                    RemainingHpUi.Animate();
                }
                catch (Exception ex) { Compatibility.Block("Enabled", ex, ClearAdvice); }
                if (Compatibility.Allowed("ShootingStar"))
                {
                    try { ShootingStarUi.Tick(); }
                    catch (Exception ex) { Compatibility.Block("ShootingStar", ex, ShootingStarUi.Clear); }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("GameUi", ex);
            ModUi.FailUi(ex);
            HomeAvailable = false;
            try { HideAdvice(); Dispose(_menu); } catch { /* A disposed scene may already have removed its UI. */ }
            _menu = null;
            if (_lastError != ex.Message)
            {
                _lastError = ex.Message;
                Plugin.Logger.LogWarning($"[게임 UI 연결] {ex}");
            }
        }
    }

    // The home menu alone (root, home detection, the Mod Settings button and its input) while gameplay features stay off
    // because the PvP reader is broken (PvpSafety.Broken): the home screen runs no match.
    internal static void TickHome()
    {
        _homeOnly = true;
        try { Tick(); }
        finally { _homeOnly = false; }
    }
    private static bool _homeOnly;

    // The native drag in progress, or null when there is none or FairyGUI left a stale one behind (see DragState).
    internal static RuntimeObject? LiveDrag()
    {
        var drag = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "GObject"), "get_draggingObject");
        var held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || Input.touchCount != 0;
        return DragState.Live(drag?.Pointer ?? IntPtr.Zero, held) ? drag : null;
    }

    internal static bool Visible(RuntimeObject? obj)
    {
        if (obj == null || obj.Get<bool>("isDisposed") || !obj.Get<bool>("onStage")) return false;
        // Ancestors through the pointers the object holds while on stage, not a wrapper each (many calls a frame, 2026-11-13).
        for (var node = obj.Pointer; node != IntPtr.Zero; node = RuntimeObject.GetPointerOf(node, "parent"))
            if (!RuntimeObject.GetOf<bool>(node, "internalVisible") || !RuntimeObject.GetOf<bool>(node, "internalVisible2")) return false;
        GC.KeepAlive(obj);
        return true;
    }

    private static int _childrenFrame = -1;
    private static IntPtr _childrenParent;
    private static bool _childrenVisible;
    private static readonly Dictionary<string, RuntimeObject> VisibleChildren = new(StringComparer.Ordinal);

    internal static RuntimeObject? Find(RuntimeObject node, string type, int depth = 0, int maxDepth = 4)
    {
        // Several features look up root windows every frame; walk the root's direct children once per frame.
        // shortcut: a child shown later in the same frame is seen next frame, upgrade if a same-frame caller needs it.
        if (depth == 0 && maxDepth == 1) return VisibleChild(node, type);
        // Default-depth window searches from the root share one walk for a short window instead of each feature
        // walking the whole tree on its own 0.2 s timer; aligned walks made 20-40 ms frames.
        if (depth == 0 && maxDepth == DeepDepth && node.Pointer == _root?.Pointer) return DeepWindow(node, type);
        // Ancestors were already checked by the preceding recursive call.
        if (depth == 0 ? !Visible(node) : node.Get<bool>("isDisposed") || !node.Get<bool>("onStage")
            || !node.Get<bool>("internalVisible") || !node.Get<bool>("internalVisible2")) return null;
        if (node.TypeName == type) return node;
        // Window/panel searches cannot succeed inside a field plate or buff icon.
        // Avoid walking every nickname, stat and effect whenever a window is absent.
        if (node.TypeName is "UICom_AttrInfo" or "UIButton_Buff") return null;
        if (depth >= maxDepth || node.Get("asCom") == null) return null;
        var count = node.Get<int>("numChildren");
        for (var i = 0; i < count; i++)
        {
            var child = node.Call("GetChildAt", i);
            if (child == null) continue;
            var found = Find(child, type, depth + 1, maxDepth);
            if (found != null) return found;
        }
        return null;
    }

    private const int DeepDepth = 4;
    private const float DeepLifetime = 0.1f;
    private static float _deepUntil = -1;
    private static IntPtr _deepRoot;
    private static readonly Dictionary<string, RuntimeObject> DeepWindows = new(StringComparer.Ordinal);

    // shortcut: a window shown less than DeepLifetime ago can be missed until the next walk; callers poll at 0.2 s.
    private static RuntimeObject? DeepWindow(RuntimeObject root, string type)
    {
        var stale = _deepRoot != root.Pointer || Time.unscaledTime >= _deepUntil;
        if (!stale && DeepWindows.TryGetValue(type, out var cached) && !Visible(cached)) stale = true;
        if (stale)
        {
            _deepRoot = root.Pointer; _deepUntil = Time.unscaledTime + DeepLifetime; DeepWindows.Clear();
            if (Visible(root)) CollectWindows(root, 0);
        }
        return DeepWindows.TryGetValue(type, out var found) ? found : null;
    }

    // Same order, visibility rules and pruning as Find, recording the first match of every type.
    private static void CollectWindows(RuntimeObject node, int depth)
    {
        if (depth > 0 && (node.Get<bool>("isDisposed") || !node.Get<bool>("onStage")
            || !node.Get<bool>("internalVisible") || !node.Get<bool>("internalVisible2"))) return;
        var type = node.TypeName;
        DeepWindows.TryAdd(type, node);
        if (type is "UICom_AttrInfo" or "UIButton_Buff") return;
        if (depth >= DeepDepth || node.Get("asCom") == null) return;
        for (int i = 0, count = node.Get<int>("numChildren"); i < count; i++)
        {
            var child = node.Call("GetChildAt", i);
            if (child != null) CollectWindows(child, depth + 1);
        }
    }

    private static RuntimeObject? VisibleChild(RuntimeObject parent, string type)
    {
        if (_childrenFrame != Time.frameCount || _childrenParent != parent.Pointer)
        {
            _childrenFrame = Time.frameCount; _childrenParent = parent.Pointer; VisibleChildren.Clear();
            _childrenVisible = Visible(parent);
            if (_childrenVisible && parent.Get("asCom") != null)
                for (int i = 0, count = parent.Get<int>("numChildren"); i < count; i++)
                {
                    var child = parent.Call("GetChildAt", i);
                    if (child == null || child.Get<bool>("isDisposed") || !child.Get<bool>("onStage")
                        || !child.Get<bool>("internalVisible") || !child.Get<bool>("internalVisible2")) continue;
                    VisibleChildren.TryAdd(child.TypeName, child);
                }
        }
        if (!_childrenVisible) return null;
        return parent.TypeName == type ? parent : VisibleChildren.TryGetValue(type, out var found) ? found : null;
    }

    internal static IReadOnlyList<RuntimeObject> PointerPath(bool refresh = false, bool hitTest = false)
    {
        if (!refresh && !hitTest && _hitFrame == Time.frameCount) return HitPath;
        _hitFrame = Time.frameCount;
        HitPath.Clear();
        RuntimeObject? hit;
        if (_root != null && (hitTest || refresh && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0)
            || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))))
        {
            // Stage.touchTarget is cached for the frame, even after owned hit surfaces change.
            var stage = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!;
            var mouse = Input.mousePosition;
            try
            {
                var display = stage.Call("HitTest", new Vector2(mouse.x, Screen.height - mouse.y), true);
                hit = _root.Call("DisplayObjectToGObject", display);
            }
            finally { RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "HitTestContext"), "ClearRaycastHitCache"); }
        }
        else hit = _root?.Get("touchTarget");
        for (; hit != null && HitPath.Count < 64; hit = hit.Get("parent"))
            HitPath.Add(hit);
        return HitPath;
    }

    private static void UpdateHome()
    {
        HomeAvailable = false;
        var home = Visible(_home) ? _home : Find(_root!, "UIHomePanel");
        HomeAvailable = home != null;
        if (HomeAvailable) StartupGuard.HomeReached();
        Plugin.Diagnostics.State("home", HomeAvailable ? "visible" : "hidden");
        if (!Compatibility.Allowed("HomeMenu"))
        {
            if (_home?.Pointer != home?.Pointer) _menuAnchor = null;
            _home = home;
            return;
        }
        if (home == null)
        {
            if (_menu != null && !_menu.Get<bool>("isDisposed")) _menu.Set("visible", false);
            _menuState = null;
            return;
        }
        // Read one known native menu label; never scan player names or translated mod labels.
        ModText.Select(Plugin.Language.Value, home.Field("btn_Activity")?.Call("GetChild", "n8")?.Get("text")?.String());
        if (_home?.Pointer != home.Pointer || _menu == null || _menu.Get<bool>("isDisposed"))
        {
            Dispose(_menu);
            _releaseNotice = null;
            Plugin.Diagnostics.Write("home menu create begin");
            _home = home;
            _menuLanguageRevision = ModText.Revision;
            var template = _menuAnchor = home.Field("btn_Dictionary")!;
            var activity = home.Field("btn_Activity")!;
            _menu = NativeUi.Component(home, activity.Get<float>("width"), activity.Get<float>("height"));
            _menu.Set("visible", false);
            _menu.Set("tooltips", ModText.Text("BetterAstralParty · 모드 설정 (F8)"));
            var nativeButton = NativeUi.Create("Home", "Home_Button_Activity")
                ?? throw new InvalidOperationException("원본 메뉴 버튼 준비 대기");
            _menu.Call("AddChild", nativeButton);
            _menuButton = nativeButton;
            // Keep the native button controller, hover/down gears and sound.
            // No HomePanel event/shop callbacks are attached to this private instance.
            nativeButton.Field("redPoint")!.Set("selectedIndex", 0);
            var driver = nativeButton.Call("GetChild", "n11")!;
            driver.Set("visible", false);
            // Preserve the original image's pivot/extent: its GearLook supplies the actual
            // hover/down/rollout rotation (including the game's custom easing curve).
            var art = NativeUi.Component(nativeButton, driver.Get<float>("width"), driver.Get<float>("height"));
            art.Set("touchable", false);
            art.Call("SetPivot", driver.Get<float>("pivotX"), driver.Get<float>("pivotY"), driver.Get<bool>("pivotAsAnchor"));
            art.Call("SetXY", driver.Get<float>("x"), driver.Get<float>("y"));
            // This gear belongs to our private button clone, never to the live Activity button.
            // Retarget it directly: native timing/curve/press/reversal all run on the artwork,
            // with no polling, duplicate tweens or one-frame transform lag.
            driver.Call("GetGear", 3)!.SetField("_owner", art);
            nativeButton.Call("SetChildIndex", art, 0);
            // Native menu composition: rounded colour plate, drop shadow, oversized portrait,
            // and the original outlined title. No separate label pill or miniature card.
            AddRoundedRect(art, 347, 170, 0, new Color(0f, 0f, 0f, 0.10f));
            art.Call("GetChildAt", 0)!.Call("SetXY", 4f, 8f);
            AddRoundedRect(art, 343, 166, 0, new Color(0f, 0f, 0f, 0.24f));
            art.Call("GetChildAt", 1)!.Call("SetXY", 6f, 10f);
            AddRoundedRect(art, 343, 166, 0, HomePortrait.Plate());
            art.Call("GetChildAt", 2)!.Call("SetXY", 2f, 6f);
            // The portrait inherits the plate's hover transform; its clip is baked once the art loads.
            HomePortrait.Build(art);
            // The click area turns with the drawn plate (user-requested 2026-11-14, after a click on the hovered button's
            // tilted lower edge missed it): the plate takes hits through the tilting artwork, as the native button's image
            // does, and the button's own rectangle keeps the hover steady while the plate tilts away from it.
            _menuPlate = art.Call("GetChildAt", 2)!;
            _menuPlate.Set("touchable", true);
            art.Set("touchable", true);
            nativeButton.Set("opaque", true);
            var title = nativeButton.Call("GetChild", "n8")!;
            title.Set("text", ModText.Text("모드 설정"));
            title.Set("autoSize", 3);
            title.Set("singleLine", true);
            NativeUi.OutlineText(title);
            // Keep the native title's font, size and spacing.
            NativeUi.Position(nativeButton, 0f, 0f, 1f);
            var scale = Math.Min(activity.Get<float>("scaleX"), activity.Get<float>("scaleY"));
            _menu.Call("SetScale", scale, scale);
            // The earlier art reached 17px left of the hit box plus 16px clear; the spacing stays.
            _menuOffsetX = _menu.Get<float>("width") * scale + 33f * scale;
            _menuState = null;
            Plugin.Logger.LogInfo("[게임 UI 연결] 메인메뉴에 모드 설정 버튼 추가됨");
            Plugin.Diagnostics.Write("home menu create complete");
        }
    }

    // Only the mod-owned Activity clone: its own native red point (the redPoint controller the home sets on live buttons)
    // marks a new version until the settings show it, and the tooltip names it; no callbacks.
    internal static void RefreshReleaseNotification()
    {
        if (!Compatibility.Allowed("HomeMenu")) return;
        if (_menu == null || _menu.Get<bool>("isDisposed")) return;
        RefreshUpdateToast();
        var version = Plugin.Updates.NotificationVersion ?? UpdateNotificationUi.PreviewMark;
        var state = (_menu.Pointer, ModText.Revision, version);
        if (_releaseNotice == state) return;
        _menuButton!.Field("redPoint")!.Set("selectedIndex", version == null ? 0 : 1);
        var tooltip = ModText.Text("BetterAstralParty · 모드 설정 (F8)");
        if (version != null) tooltip += "\n" + ModText.Text("새 버전") + ": " + version;
        _menu.Set("tooltips", tooltip);
        _releaseNotice = state;
    }

    // The pointer in a component's local coordinates (FairyGUI's touch position).
    internal static (float X, float Y) PointerIn(RuntimeObject target)
    {
        var point = target.Call("GlobalToLocal", RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!
            .Get<Vector2>("touchPosition"))!.Value<Vector2>();
        return (point.x, point.y);
    }

    internal static void RefreshUpdateProgress()
    {
        var snapshot = Plugin.Automatic.Snapshot;
        var fault = Plugin.Automatic.Fault;
        var download = Plugin.Automatic.DownloadProgress;
        UpdateProgress.Shared.Observe(Plugin.Automatic.Status, snapshot.TargetVersion, snapshot.IsTransition, download.Read, download.Total,
            fault == null ? "" : UpdateProgress.FaultCode(fault.Phase.ToString(), fault.Reason.ToString()), fault?.Reason.ToString().ToLowerInvariant() ?? UpdateProgress.StatusReason(Plugin.Automatic.Status));
        if (!Compatibility.Allowed("HomeMenu") || _menu == null || _menu.Get<bool>("isDisposed")) return;
        var progress = _previewProgress ?? UpdateProgress.Shared;
        var shown = (_menu.Pointer, progress.Revision, ModText.Revision);
        if (_progressShown == shown) return;
        _progressShown = shown;
        // A shown or hidden capsule moves a toast stacked beyond it (PlaceAttachments).
        if (!progress.Visible) { _progressCap?.Set("visible", false); PlaceAttachments(); return; }
        // A stopped update needs a second row for the reason and code, so its capsule is rebuilt taller.
        if (_progressCap == null || _progressCap.Get<bool>("isDisposed") || _progressStoppedLayout != progress.Stopped)
        {
            if (_progressCap != null && !_progressCap.Get<bool>("isDisposed")) Dispose(_progressCap);
            CreateProgressCapsule(progress.Stopped);
        }
        _progressCap!.Set("visible", true);
        PlaceAttachments();
        var accent = progress.Stopped ? new Color(0.88f, 0.29f, 0.29f) : progress.Done ? new Color(0.49f, 0.78f, 0.42f) : new Color(1f, 0.76f, 0.05f);
        _progressLabel!.Set("text", ModText.UpdateProgressCapsule(progress.Version, progress.Status, progress.Step, progress.Stopped, progress.Done));
        _progressFill!.Call("SetSize", Math.Max(6f, _progressTrackWidth * progress.Percent / 100f), 6f);
        SmoothShape.Fill(_progressFill, accent);
        SmoothShape.Fill(_progressDoor!, accent);
        SmoothShape.Fill(_progressShadow!, new Color(accent.r * 0.62f, accent.g * 0.62f, accent.b * 0.62f));
        _progressIconDone!.Set("visible", progress.Done && !progress.Stopped);
        _progressIconStop!.Set("visible", progress.Stopped);
        _progressIconLoad!.Set("visible", !progress.Done && !progress.Stopped);
        ModFont.Track(_progressLabel);
        if (_progressReason != null)
        {
            _progressReason.Set("text", ModText.UpdateStopReason(progress.Reason));
            _progressCode!.Set("text", progress.Code);
            ModFont.Track(_progressReason); ModFont.Track(_progressCode);
        }
    }

    private static RuntimeObject ProgressShape(RuntimeObject parent, float x, float y, float width, float height, Color fill,
        float topLeft, float topRight, float bottomLeft, float bottomRight, float line = 0f)
        => SmoothShape.Create(parent, x, y, width, height, fill, topLeft, topRight, bottomLeft, bottomRight, line);

    // Cream capsule as wide as the button (draft B with the round state button, user-chosen 2026-11-14 in
    // artifacts/cc/progress-capsule-drafts, after "2026.11.0 적용 완료" was cut at the old 42% title box): the title over the
    // whole width before the door, a thin bar under it that keeps one place and length whatever the title, and a coloured door
    // with a raised white disc carrying the state (download arrow, check, exclamation). The whole capsule is the click target.
    private static void CreateProgressCapsule(bool stopped)
    {
        var width = _menu!.Get<float>("width"); const float head = 54f, door = 50f;
        // Stopped: the title and bar, then the reason, then the error code on its own full-width line.
        var height = stopped ? head + 26f + 26f : head;
        _progressStoppedLayout = stopped; _progressHover = false;
        _progressCap = NativeUi.Component(_menu, width, height);
        _progressCap.Call("SetXY", 0f, _menu.Get<float>("height") + 8f);
        _progressCap.Call("SetPivot", 0.5f, 0.5f, false);
        _progressCap.Set("opaque", true);
        ProgressShape(_progressCap, 0, 0, width, height, stopped ? new Color(1f, 0.914f, 0.902f) : new Color(1f, 0.985f, 0.95f), 22, 22, 22, 22, 3f);
        _progressDoor = ProgressShape(_progressCap, width - door, 0, door, height, new Color(1f, 0.76f, 0.05f), 0, 22, 0, 22, 3f);
        // The round button: a darker disc 3 below as its shadow, the white disc with a black rim, and the state icon on it.
        float cx = width - door / 2f + 1f, cy = height / 2f - 1f; const float r = 15f;
        _progressShadow = ProgressShape(_progressCap, cx - r, cy - r + 3f, r * 2, r * 2, new Color(0.62f, 0.47f, 0.03f), r, r, r, r);
        ProgressShape(_progressCap, cx - r, cy - r, r * 2, r * 2, Color.white, r, r, r, r, 3f);
        _progressIconLoad = ProgressIcon(cx, cy, (0f, -8f, 0f, 3.6f), (-5f, -0.7f, 0f, 4.3f), (5f, -0.7f, 0f, 4.3f), (-7.2f, 8.6f, 7.2f, 8.6f));
        _progressIconDone = ProgressIcon(cx, cy, (-7.2f, 0f, -2.2f, 5f), (-2.2f, 5f, 7.2f, -5.8f));
        _progressIconStop = ProgressIcon(cx, cy, (0f, -8f, 0f, 2.2f));
        ProgressShape(_progressIconStop, cx - 2.2f, cy + 5f, 4.4f, 4.4f, new Color(0.067f, 0.067f, 0.067f), 2.2f, 2.2f, 2.2f, 2.2f);
        var inner = width - door - 10f - 14f;
        _progressLabel = NativeUi.Label(_progressCap, "", 14f, 4f, inner, 34f, 22);
        _progressLabel.Set("touchable", false);
        StyleText(_progressLabel, 22, align: 0);
        _progressLabel.Set("autoSize", 3); // Shrink only a title longer than the whole row, e.g. a long beta version.
        _progressTrackWidth = inner - 4f;
        ProgressShape(_progressCap, 14f, 35f, inner, 10f, new Color(0.9f, 0.875f, 0.8f), 5, 5, 5, 5, 2f);
        _progressFill = ProgressShape(_progressCap, 16f, 37f, 6f, 6f, new Color(1f, 0.76f, 0.05f), 3, 3, 3, 3);
        _progressReason = _progressCode = null;
        if (!stopped) return;
        var rowWidth = width - door - 14f - 8f;
        _progressReason = NativeUi.Label(_progressCap, "", 14f, head - 4f, rowWidth, 26f, 16, dark: true);
        _progressCode = NativeUi.Label(_progressCap, "", 14f, head + 22f, rowWidth, 24f, 15, dark: true);
        foreach (var label in new[] { _progressReason, _progressCode })
        {
            label.Set("touchable", false);
            StyleText(label, label == _progressCode ? 15 : 16, align: 0);
            label.Set("autoSize", 3); // Shrink to fit instead of cutting off a long reason or code.
        }
    }

    // One state icon on the round button: rounded strokes in a component of their own, shown only for its state.
    private static RuntimeObject ProgressIcon(float cx, float cy, params (float X0, float Y0, float X1, float Y1)[] strokes)
    {
        var icon = NativeUi.Component(_progressCap!, _progressCap!.Get<float>("width"), _progressCap.Get<float>("height"));
        icon.Set("touchable", false);
        const float thick = 3.6f;
        foreach (var (x0, y0, x1, y1) in strokes)
        {
            float dx = x1 - x0, dy = y1 - y0, length = (float)Math.Sqrt(dx * dx + dy * dy) + thick;
            var bar = ProgressShape(icon, 0, 0, length, thick, new Color(0.067f, 0.067f, 0.067f), thick / 2, thick / 2, thick / 2, thick / 2);
            bar.Call("SetPivot", 0.5f, 0.5f, true);
            bar.Call("SetXY", cx + (x0 + x1) / 2f, cy + (y0 + y1) / 2f);
            bar.Set("rotation", (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI));
        }
        return icon;
    }

    // Follows UpdateNotificationUi.Toast: built per release (a language change rebuilds the whole menu), sliding in over
    // 0.25 s and fading out over its last 0.25 s.
    private static void RefreshUpdateToast()
    {
        var toast = UpdateNotificationUi.Toast;
        if (toast == null)
        {
            if (_toastView != null && !_toastView.Get<bool>("isDisposed")) Dispose(_toastView);
            _toastView = _toastClose = _toastTimer = null; _toastKey = null; _toastHover = false;
            return;
        }
        if (_toastView == null || _toastView.Get<bool>("isDisposed") || _toastKey != toast.Key)
        {
            if (_toastView != null && !_toastView.Get<bool>("isDisposed")) Dispose(_toastView);
            CreateUpdateToast(toast.Release.Version);
            _toastKey = toast.Key; _toastHover = false;
            PlaceAttachments();
        }
        var reveal = AdviceText.Reveal(Time.unscaledTime - UpdateNotificationUi.ToastShownAt, 0.25f);
        var left = UpdateNotificationUi.ToastRemaining;
        _toastView!.Call("SetXY", _toastAt.X, _toastAt.Y - (1 - reveal) * 24f);
        _toastView.Set("alpha", reveal * Math.Min(1f, left * UpdateNotificationUi.ToastSeconds / 0.25f));
        _toastTimer!.Call("SetSize", Math.Max(4f, (ToastWidth - 44f) * left), 4f);
    }

    // Draft 3: a cream card with the capsule's outline, a yellow disc with an up arrow, the version, a hint, a dark close
    // disc and a timer bar along the bottom.
    private static void CreateUpdateToast(string version)
    {
        var ink = new Color(0.067f, 0.067f, 0.067f);
        _toastView = NativeUi.Component(_menu!, ToastWidth, ToastHeight);
        _toastView.Call("SetPivot", 0.5f, 0.5f, false);
        _toastView.Set("opaque", true);
        ProgressShape(_toastView, 0, 0, ToastWidth, ToastHeight, new Color(1f, 0.985f, 0.95f), 20, 20, 20, 20, 3f);
        ProgressShape(_toastView, 14, 13, 52, 52, new Color(1f, 0.76f, 0.05f), 26, 26, 26, 26, 3f);
        ProgressShape(_toastView, 37.75f, 28, 4.5f, 23, ink, 2.25f, 2.25f, 2.25f, 2.25f);
        foreach (var side in new[] { -1f, 1f })
        {
            // Each head is 14 long at 45 degrees, its upper end on the arrow's tip.
            var head = ProgressShape(_toastView, 0, 0, 14, 4.5f, ink, 2.25f, 2.25f, 2.25f, 2.25f);
            head.Call("SetPivot", 0.5f, 0.5f, true);
            head.Call("SetXY", 40f + side * 4.2f, 31.5f);
            head.Set("rotation", side * 45f);
        }
        // The settings list's text: the default template's face without its outline is dark (the dark template's is white).
        var title = NativeUi.Label(_toastView, "BetterAstralParty " + version, 80, 8, ToastWidth - 80 - 52, 36, 22);
        var hint = NativeUi.Label(_toastView, "새 버전이 나왔습니다 · 눌러서 변경 내역 보기", 80, 40, ToastWidth - 80 - 24, 24, 15);
        foreach (var (label, size, color) in new[] { (title, 22, (Color?)null), (hint, 15, new Color(0.478f, 0.431f, 0.361f)) })
        {
            StyleText(label, size, align: 0);
            ModUi.Plain(label, color);
            label.Set("singleLine", true);
            label.Set("autoSize", 3); // Shrink a long version or English hint instead of cutting it off.
            label.Set("verticalAlign", 1);
        }
        _toastClose = NativeUi.Component(_toastView, 24, 24);
        _toastClose.Call("SetXY", ToastWidth - 40, 14f);
        _toastClose.Set("opaque", true);
        ProgressShape(_toastClose, 0, 0, 24, 24, new Color(0.275f, 0.243f, 0.204f), 12, 12, 12, 12);
        foreach (var angle in new[] { 45f, -45f })
        {
            var bar = ProgressShape(_toastClose, 0, 0, 12, 2.4f, Color.white, 1.2f, 1.2f, 1.2f, 1.2f);
            bar.Call("SetPivot", 0.5f, 0.5f, true);
            bar.Call("SetXY", 12f, 12f);
            bar.Set("rotation", angle);
        }
        ProgressShape(_toastView, 22, ToastHeight - 10, ToastWidth - 44, 4, new Color(0.914f, 0.875f, 0.769f), 2, 2, 2, 2);
        _toastTimer = ProgressShape(_toastView, 22, ToastHeight - 10, ToastWidth - 44, 4, new Color(1f, 0.76f, 0.05f), 2, 2, 2, 2);
    }

    internal static void RefreshLanguage()
    {
        // UpdateHome may already have created the menu in the new language this
        // frame. Do not destroy/reload its portrait and controls a second time.
        try { if (_menuLanguageRevision != ModText.Revision) ClearHomeMenu(); }
        catch (Exception ex) { FailHomeMenu(ex); }
        try { ClearAdviceDisplay(); }
        catch (Exception ex) { Compatibility.Block("Enabled", ex, ClearAdvice); }
        try { CardPreviewUi.Reset(); }
        catch (Exception ex) { Compatibility.Block("CardPopups", ex, CardPreviewUi.Reset); }
        try { BattleStatusUi.Clear(); }
        catch (Exception ex) { Compatibility.Block("BattleStatus", ex, BattleStatusUi.Clear); }
        try { FieldBuffUi.Clear("language"); }
        catch (Exception ex) { Compatibility.Block("FieldBuffs", ex, () => FieldBuffUi.Clear("compatibility")); }
    }

    private static void FailHomeMenu(Exception error) => Compatibility.Block("HomeMenu", error, ClearHomeMenu);

    private static void ClearHomeMenu()
    {
        _menuState = null; _pressed = null;
        HomeMenuPlacement.Clear();
        if (_menu == null) return;
        var menu = _menu;
        try { menu.Set("touchable", false); }
        finally
        {
            try { menu.Set("visible", false); }
            finally { Dispose(menu); _menu = null; }
        }
    }

    internal static void AddRoundedRect(RuntimeObject parent, float width, float height, float inset, Color color) =>
        SmoothShape.Create(parent, inset, inset, width, height, color, 12f, 12f, 12f, 12f);

    private static void FollowHomeMenu()
    {
        if (_menu == null || _menu.Get<bool>("isDisposed")) return;
        HomePortrait.Tick();
        var visible = Visible(_menuAnchor);
        if (!visible)
        {
            if (HomeMenuPlacement.Active) HomeMenuPlacement.Clear();
            if (_menuState != null) _menu.Set("visible", false);
            _menuState = null; _pressed = null;
            return;
        }
        var anchor = _menuAnchor!;
        // Sample native Cut in/Hide/BGset motion every frame, including reverse/re-entry.
        // Move only our wrapper so its child's own hover/press transform is untouched.
        // xMin/yMin handle both pivot modes; native centre pivots are NOT necessarily anchors.
        float x = anchor.Get<float>("xMin") - _menuOffsetX, y = anchor.Get<float>("yMin");
        // The game's background-adjust mode also moves the button (issue #18); there it stays opaque and draggable.
        var placing = HomeMenuPlacement.Tick(_home!, _menu, _menuButton!, x, y);
        var (dx, dy) = HomeMenuPlacement.Offset;
        var state = (X: x + dx, Y: y + dy, Alpha: placing ? 1f : anchor.Get<float>("alpha"), Visible: visible,
            Touchable: placing || anchor.Get<bool>("touchable"));
        if (_menuState == state) return;
        _menuState = state;
        _menu.Call("SetXY", state.X, state.Y);
        _menu.Set("alpha", state.Alpha);
        _menu.Set("visible", state.Visible);
        _menu.Set("touchable", state.Touchable);
        PlaceAttachments();
    }

    // The capsule and the toast follow the button's zone wherever it was moved (issue #18, user-chosen 2026-11-14: 3 x 3 zones,
    // v10 in artifacts/cc/home-button-drafts). The home above the bottom menu bar is cut into thirds both ways, and the zone of
    // the button's centre decides: top and middle rows put the capsule under the button, the bottom row above its hat; the left
    // column puts the toast right of the button, the right column left of it, the centre column centred on the button beyond
    // the capsule. Both stay Edge inside that area. A zone changes only once the centre is ZoneSlack past its line, so a button
    // resting on a line does not flip them back and forth.
    private static void PlaceAttachments()
    {
        if (_menu == null || _home == null || _menu.Get<bool>("isDisposed")) return;
        float scale = _menu.Get<float>("scaleX"), x = _menu.Get<float>("x"), y = _menu.Get<float>("y");
        float width = _menu.Get<float>("width"), height = _menu.Get<float>("height");
        float areaWidth = _home.Get<float>("width"), areaHeight = _home.Get<float>("height") - BottomBar;
        _zone = (Zone(y + height * scale / 2, areaHeight, _zone.Row), Zone(x + width * scale / 2, areaWidth, _zone.Column));
        var above = _zone.Row == 2;
        // Local units under the button; the clamps keep each attachment inside the area in home units.
        float LocalX(float local, float size) => (Math.Clamp(x + local * scale, Edge, Math.Max(Edge, areaWidth - Edge - size * scale)) - x) / scale;
        float LocalY(float local, float size) => (Math.Clamp(y + local * scale, Edge, Math.Max(Edge, areaHeight - Edge - size * scale)) - y) / scale;
        var cap = _progressCap != null && !_progressCap.Get<bool>("isDisposed") ? _progressCap : null;
        float capHeight = cap?.Get<float>("height") ?? 0f, capY = above ? -HatHeight - 8f - capHeight : height + 8f;
        if (cap != null) { capY = LocalY(capY, capHeight); cap.Call("SetXY", 0f, capY); }
        var stacked = cap != null && cap.Get<bool>("visible");
        float toastX, toastY;
        if (_zone.Column == 1)
        {
            toastX = width / 2 - ToastWidth / 2;
            toastY = above ? (stacked ? capY : -HatHeight) - 8f - ToastHeight : (stacked ? capY + capHeight : height) + 8f;
        }
        else { toastX = _zone.Column == 0 ? width + 24f : -ToastWidth - 24f; toastY = -12f; }
        _toastAt = (LocalX(toastX, ToastWidth), LocalY(toastY, ToastHeight));
    }

    private static int Zone(float centre, float size, int previous)
    {
        var zone = Math.Clamp((int)(centre / (size / 3f)), 0, 2);
        if (previous is >= 0 and <= 2 && zone != previous
            && centre >= previous * size / 3f - ZoneSlack && centre < (previous + 1) * size / 3f + ZoneSlack) return previous;
        return zone;
    }

    // The portrait's hat rises this far above the plate (HomePortrait's visible rectangle starts 43 above it).
    private const float HatHeight = 43f;
    // The bottom menu bar (its own BottomMenu package, not readable here) covers the home's lowest 122 units: its top sat at
    // y 958 of the 1920 x 1080 home in the user's capture of 2026-11-14 (capture-24 in artifacts/cc/home-button-drafts).
    private const float BottomBar = 122f, ZoneSlack = 20f, Edge = 8f;
    private static (int Row, int Column) _zone = (-1, -1);
    private static (float X, float Y) _toastAt = (-ToastWidth - 24f, -12f);

    private static bool PreviewKey() => DevelopmentBuild && Input.GetKeyDown(KeyCode.F11)
        && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

    // Downloading at 45 %, then done, then stopped on the network (BAP-UPD-FAILED), then the real state again.
    private static void PreviewProgress()
    {
        _previewStep = (_previewStep + 1) % 4;
        _previewProgress = null;
        if (_previewStep > 0)
        {
            var preview = new UpdateProgress();
            preview.Observe(AutomaticUpdateStatus.Downloading, Plugin.Version, false, 45, 100);
            if (_previewStep == 2) preview.Observe(AutomaticUpdateStatus.Committed, "", false);
            if (_previewStep == 3) preview.Observe(AutomaticUpdateStatus.Failed, "", false, 0, 0, "", UpdateProgress.StatusReason(AutomaticUpdateStatus.Failed));
            _previewProgress = preview;
        }
        _progressShown = null;
        Plugin.Diagnostics.Write("homeMenu.capsulePreview step=" + _previewStep.ToString(CultureInfo.InvariantCulture));
    }

    internal static void SetSettingsModal(bool visible)
    {
        if (!visible)
        {
            if (_settingsHome != null && !_settingsHome.Get<bool>("isDisposed"))
                _settingsHome.Set("touchable", _settingsHomeTouchable);
            _settingsHome = null;
            return;
        }
        // Block the home subtree while our settings are open, then restore it.
        if (_settingsHome == null && _home != null && !_home.Get<bool>("isDisposed"))
        {
            _settingsHome = _home;
            _settingsHomeTouchable = _home.Get<bool>("touchable");
            _home.Set("touchable", false);
        }
    }

    private static void HandleMenuInput()
    {
        if (!HomeAvailable || !Visible(_menu) || _root == null || !_menu!.Get<bool>("touchable")
            || _menu.Get<float>("alpha") <= 0) { _pressed = null; return; }
        if (HomeMenuPlacement.Active) { _pressed = null; HomeMenuPlacement.Press(_home!, _menu, _menuPlate!); return; }
        if (PreviewKey()) PreviewProgress();
        // Hover feedback like native buttons: a slight lift. The pointer path is read only while the capsule or the toast
        // shows; the toast and its close disc are children of the menu, so they are hit-tested before it.
        var capsule = _progressCap != null && Visible(_progressCap);
        var toast = _toastView != null && Visible(_toastView);
        if (capsule || toast)
        {
            var under = PointerPath().ToList();
            var over = capsule && under.Any(item => item.Pointer == _progressCap!.Pointer);
            if (capsule && over != _progressHover) { _progressHover = over; var lift = over ? 1.03f : 1f; _progressCap!.Call("SetScale", lift, lift); }
            var onToast = toast && under.Any(item => item.Pointer == _toastView!.Pointer);
            if (onToast != _toastHover && toast) { var lift = onToast ? 1.02f : 1f; _toastView!.Call("SetScale", lift, lift); }
            _toastHover = onToast;
        }
        else _toastHover = false;
        // The native click rule (FairyGUI TouchInfo.ClickTest): the press picks the target, and the release clicks it unless the
        // pointer strayed past 50 pixels on an axis. A second hit test at the release missed the button's edge, because its
        // native down effect shrinks it to 80 % while it is held (Home_Button_Activity, downEffect 2, 0.8; 2026-11-14).
        if (Input.GetMouseButtonDown(0))
        {
            var path = PointerPath().ToList();
            _pressed = (capsule ? path.FirstOrDefault(item => item.Pointer == _progressCap!.Pointer) : null)
                ?? (toast ? path.FirstOrDefault(item => item.Pointer == _toastClose!.Pointer) ?? path.FirstOrDefault(item => item.Pointer == _toastView!.Pointer) : null)
                ?? (path.Any(item => item.Pointer == _menu.Pointer) && (HomeMenuPlacement.OnPlate(_menuPlate!) || HomePortrait.OnHat()) ? _menu : null);
            _pressedAt = Input.mousePosition;
        }
        if (!Input.GetMouseButtonUp(0)) return;
        var hit = _pressed;
        _pressed = null;
        var moved = Input.mousePosition - _pressedAt;
        if (hit == null || Math.Abs(moved.x) > 50f || Math.Abs(moved.y) > 50f) return;
        if (hit.Pointer == _progressCap?.Pointer) { if (_previewProgress == null) UpdateNotificationUi.ShowProgress(); }
        else if (hit.Pointer == _toastClose?.Pointer) UpdateNotificationUi.CloseToast();
        else if (hit.Pointer == _toastView?.Pointer) UpdateNotificationUi.OpenToast();
        else ModUi.TogglePanel();
    }

    private static bool _fightEnded;

    private static void UpdateFight()
    {
        HideAdvice(keepDice: true);
        if (!Plugin.Enabled.Value && !Plugin.KoMinimum.Value && !Plugin.FinalDamage.Value) { Plugin.Diagnostics.EndCombat(); CardUi.Clear(); RemainingHpUi.Clear(); return; }
        if (HomeAvailable) RuntimeObject.RequireClasses("Enabled", ("GameLogic", "GameLogicManager"), ("Core.Scene", "BattleSceneController"));
        var fight = Visible(_fight) ? _fight : Find(_root!, "FightWindow");
        if (fight == null) ReleaseWatch.Poll(RuntimeObject.StaticField(_logicClass, "_inst"), -1);
        if (fight == null) { _fightEnded = false; Plugin.Diagnostics.EndCombat(); Plugin.Diagnostics.State("fightStep", "hidden"); _lastAdvice = ""; CardUi.Clear(); RemainingHpUi.ResetCapture(); return; }
        _fight = fight;
        var ui = fight.Get("contentPane");
        // A window can be on stage before its asynchronously loaded content is attached.
        if (ui == null || ui.Get<bool>("isDisposed"))
        {
            CardUi.Clear(); RemainingHpUi.ResetCapture();
            Plugin.Diagnostics.State("fightReady", "waiting-content");
            return;
        }
        if (ui.TypeName != "UIFightWindow") { RemainingHpUi.Clear(); return; }
        var stepController = ui.Field("step");
        if (stepController == null)
        {
            CardUi.Clear(); RemainingHpUi.ResetCapture();
            Plugin.Diagnostics.State("fightReady", "waiting-step");
            return;
        }
        var step = stepController.Get<int>("selectedIndex");
        ReleaseWatch.Poll(RuntimeObject.StaticField(_logicClass, "_inst"), step);
        Plugin.Diagnostics.State("fightReady", "ready");
        Plugin.Diagnostics.State("fightStep", step.ToString(CultureInfo.InvariantCulture));
        // Spectated fights skip the card step (3) and go 2 -> 4: their step 2 is "both sides are playing cards" (as long
        // as the players take), so spectators get the dice target from step 2. A local fighter's step 2 is the short intro.
        // A finished fight also passes through step 2 (6 -> 2) before closing; that step 2 is not a card phase.
        if (step == 6) _fightEnded = true; else if (step != 2) _fightEnded = false;
        var spectating = ui.Field("playerState")!.Get<int>("selectedIndex") == 2;
        var spectatorCards = spectating && (step == 4 || step == 2 && !_fightEnded);
        if (step != 3) CardUi.Clear(keepDice: step is 4 or 5 or 6 || spectatorCards);
        if (step is not (1 or 3 or 5 or 6) && !spectatorCards) { CardDiceUi.Hide(); RemainingHpUi.ResetCapture(); return; }
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        var mapType = room?.Field("info")?.Get<int>("MapType") ?? 0;
        if (!VisibleCombatAdvisor.IsPve(mapType)) { Plugin.Diagnostics.EndCombat(); CardUi.Clear(); RemainingHpUi.Clear(); return; }
        if (Plugin.Diagnostics.IsRecording)
        {
            // Diagnostic failure must not disable the advisor or damage view.
            try
            {
                var source = fight.Get("attackerData");
                var target = fight.Get("defenderData");
                if (source != null && target != null && Plugin.Diagnostics.CombatPhase(fight.Pointer,
                    source.Pointer, target.Pointer, step, mapType, ui.Field("playerState")!.Get<int>("selectedIndex")))
                {
                    foreach (var actor in new[] { ("attacker", source), ("defender", target) })
                    {
                        var player = actor.Item2.Field("player")!;
                        Plugin.Diagnostics.State("combat.actor." + actor.Item1,
                            $"heroId={player.Get("Hero")!.Get<int>("HeroId")}; type={actor.Item2.Get<int>("characterType")}");
                    }
                    // The normal reader captures the defender. Capture the other public
                    // side at phase boundaries only, without inspecting any hand cards.
                    PublicCombatEffects.Read(source, "attacker", target);
                    PublicCombatEffects.Read(target, "phaseDefender", source);
                }
            }
            catch (Exception ex) { Plugin.Diagnostics.Error("combatSnapshot", ex); }
        }
        if (step == 1) { RemainingHpUi.ResetCapture(); if (Plugin.Enabled.Value) UpdateEncounter(fight, ui, mapType); return; }
        var director = RuntimeObject.StaticField(_battleClass, "inst")?.Field("directorManager");
        var atk = director?.Get("attacker")?.Field("_UI");
        var def = director?.Get("victim")?.Field("_UI");
        if (!Visible(atk) || !Visible(def)) return;
        // Until ActiveBattlePlatform binds this fight, the director's actors still show the previous fight's HP and stats
        // (2026-10-24: a spectated fight flashed "KO 불가" from them for 0.2 s).
        if (director!.Field("_attackerData")?.Pointer != fight.Get("attackerData")?.Pointer
            || director.Field("_defenderData")?.Pointer != fight.Get("defenderData")?.Pointer) return;
        var attackerUi = atk!.Field("com_Attack")!;
        var defenderUi = def!.Field("com_Defend")!;
        if (!int.TryParse(defenderUi.Field("txt_Life")!.Get("text")!.String(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var hp)) return;
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("combat.inputs", $"hp={hp}; attackMin={atk.Field("minATK")!.Value<int>()}; "
                + $"attackMax={atk.Field("maxATK")!.Value<int>()}; defenseMin={def!.Field("minDEF")!.Value<int>()}; "
                + $"defenseMax={def.Field("maxDEF")!.Value<int>()}");
        RemainingHpUi.Update(fight, ui, attackerUi, defenderUi, step, hp, step == 5 ? ServerFight.Decided(director) : null);
        if (Plugin.Diagnostics.IsRecording)
        {
            string PublicNumber(RuntimeObject? label) => Visible(label)
                && int.TryParse(label!.Get("text")!.String(), out var value) ? value.ToString(CultureInfo.InvariantCulture) : "hidden";
            Plugin.Diagnostics.State("combatVisible", $"step={step}; defender_hp={hp}; "
                + $"attacker_hp={PublicNumber(attackerUi.Field("txt_Life"))}; "
                + $"attack_point={PublicNumber(attackerUi.Field("com_Point")?.Field("txt_Point"))}; "
                + $"defense_point={PublicNumber(defenderUi.Field("com_Point")?.Field("txt_Point"))}");
        }
        if (step == 6) { CardDiceUi.Hide(); return; }
        var incomingBonus = PublicCombatEffects.Read(fight.Get("defenderData"), "defender", fight.Get("attackerData"));
        if (incomingBonus == null) { CardDiceUi.Clear(); return; }
        var defenderPlayer = fight.Get("defenderData")?.Get<int>("characterType") == 1;
        // Who may see the rolled card values and a played Powerful Attack is CardKoMinimum.SeesRolled / SeesBoost (self-tested).
        var viewer = ui.Field("playerState")!.Get<int>("selectedIndex");
        var rolled = CardKoMinimum.SeesRolled(viewer, step) ? ServerFight.Rolled(director) : null;
        if (rolled is { } seen && Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("koMinimum.rolled", $"role={viewer}; step={step}; attack={seen.Attack?.ToString() ?? "bounds"}; defense={seen.Defense?.ToString() ?? "bounds"}");
        var boost = CardKoMinimum.SeesBoost(viewer) ? ServerFight.Boost(director) : default;
        if (boost.Count > 0 && Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("koMinimum.boost", $"role={viewer}; step={step}; percent={boost.Percent}; count={boost.Count}");
        if (step is 3 or 4 || spectatorCards)
        {
            var counter = fight.Get("defenderData")?.Get("Property")?.Field("Counter");
            int.TryParse(attackerUi.Field("txt_Life")!.Get("text")!.String(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var attackerHp);
            CardUi.Update(fight, ui, CardKoMinimum.WithRolled(new(mapType, hp, atk.Field("minATK")!.Value<int>(), atk.Field("maxATK")!.Value<int>(),
                def.Field("minDEF")!.Value<int>(), def.Field("maxDEF")!.Value<int>())
                { Modifiers = incomingBonus.Value, Boost = boost,
                    CounterAvailable = counter?.Get<bool>("Value"), AttackerHp = attackerHp, DefenderPlayer = defenderPlayer }, rolled),
                attackerUi.Field("com_Point")!);
            return;
        }
        if (step == 5 && ui.Field("playerState")!.Get<int>("selectedIndex") is var role && (role is 0 or 1 || role == 2 && Plugin.KoSpectator.Value)
            && Plugin.KoMinimum.Value)
            CardDiceUi.BindResolved(ui, atk, def!, new(mapType, hp, atk.Field("minATK")!.Value<int>(),
                atk.Field("maxATK")!.Value<int>(), def!.Field("minDEF")!.Value<int>(), def.Field("maxDEF")!.Value<int>())
                { Modifiers = incomingBonus.Value, Boost = boost, DefenderPlayer = defenderPlayer }, rolled);
        if (!Plugin.Enabled.Value || !Plugin.DefenseAdvice.Value) return;
        var point = attackerUi.Field("com_Point")!.Field("txt_Point")!;
        if (!Visible(point) || !int.TryParse(point.Get("text")!.String(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var die)) return;
        // The same label later contains ATK + die. A total of 1..6 is not a roll.
        if (!CardKoMinimum.RevealedDie(attackerUi.Field("showPoint")!.Get<int>("selectedIndex"),
                Visible(point), die, incomingBonus.Value.AttackDice.ZeroOnly)) return;
        var input = new VisibleCombat(mapType, hp, atk.Field("minATK")!.Value<int>(), atk.Field("maxATK")!.Value<int>(),
            die, def.Field("minDEF")!.Value<int>(), def.Field("maxDEF")!.Value<int>(), !fight.Field("dodgeNoAvailable")!.Value<bool>())
            { Modifiers = incomingBonus.Value, Boost = boost };
        if (_calculationInput != input)
        {
            _calculationInput = input;
            _calculationResult = VisibleCombatAdvisor.Calculate(input);
        }
        var advice = _calculationResult;
        if (advice == null) return;
        var defend = ui.Field("btn_Defend")!;
        var dodge = ui.Field("btn_Dodge")!;
        var localChoice = Visible(defend) && Visible(dodge) && (defend.Get<bool>("touchable")
            || (ShushuShieldUi.IsBlocking(defend) && dodge.Get<bool>("touchable")));
        // Do not switch a local defender to spectator advice after they already chose.
        if (!localChoice && ui.Field("playerState")!.Get<int>("selectedIndex") == 1) return;
        if (!localChoice)
        {
            if (_observerSummary == null || _observerSummary.Root.Get<bool>("isDisposed")
                || _observerParent != ui.Pointer)
            {
                _observerSummary?.Dispose();
                _observerSummary = new ChoiceVisual(ui);
                _observerParent = ui.Pointer;
            }
            var scale = Plugin.UiScale.Value;
            _observerSummary.Show(advice.Value, scale, (ui.Get<float>("width") - 360 * scale) / 2,
                ui.Get<float>("height") - 220 * scale);
            return;
        }
        EnsureBadges(defend, dodge);
        // Clamp the badge scale to the existing gap; never move the actual buttons.
        var badgeScale = Math.Min(Plugin.UiScale.Value,
            Math.Max(180f, Math.Abs(dodge.Get<float>("x") - defend.Get<float>("x")) - 12f) / 240f);
        _defBadge!.Show(advice.Value.DefendQuick, advice.Value.Recommendation == RecommendedAction.Defend,
            badgeScale, -76f * badgeScale - 70f, visualOnly: true);
        _dodgeBadge!.Show(advice.Value.DodgeQuick, advice.Value.Recommendation == RecommendedAction.Dodge,
            badgeScale, -76f * badgeScale - 70f, visualOnly: true);
        _adviceShowing = true;
        if (_lastAdvice != input.ToString())
        {
            _lastAdvice = input.ToString();
            Plugin.Logger.LogInfo($"[전투 추천 표시] {_lastAdvice} / {advice.Value.Defend.Replace('\n', ' ')} / {advice.Value.Dodge.Replace('\n', ' ')}");
        }
    }

    private static void EnsureBadges(RuntimeObject first, RuntimeObject second, float width = 240, int fontSize = 23)
    {
        if (_defButton?.Pointer == first.Pointer && _dodgeButton?.Pointer == second.Pointer
            && _defBadge != null && !_defBadge.Panel.Get<bool>("isDisposed")
            && _dodgeBadge != null && !_dodgeBadge.Panel.Get<bool>("isDisposed")) return;
        _defBadge?.Dispose();
        _dodgeBadge?.Dispose();
        _defButton = first; _dodgeButton = second;
        _defBadge = new AdviceBadge(first, width, fontSize: fontSize);
        _dodgeBadge = new AdviceBadge(second, width, fontSize: fontSize);
    }

    private static void UpdateEncounter(RuntimeObject fight, RuntimeObject ui, int mapType)
    {
        var launch = ui.Field("com_LaunchPK");
        if (!Visible(launch) || ui.Field("playerState")!.Get<int>("selectedIndex") != 0) return;
        var attack = launch!.Field("btn_PK");
        var leave = launch.Field("btn_Leave");
        // Forced encounters, spectators and hidden/disabled buttons are not choices.
        if (!Visible(attack) || !Visible(leave) || !attack!.Get<bool>("touchable")
            || !leave!.Get<bool>("touchable") || attack.Get<bool>("grayed") || leave.Get<bool>("grayed")) return;
        var enemy = fight.Get("defenderData");
        if (enemy?.Get<int>("characterType") != 2 || !Visible(launch.Field("txt_HP"))) return;
        var enemyBonus = Plugin.ShootingStarShow.Value || Plugin.EncounterAdvice.Value
            ? PublicCombatEffects.Read(enemy, "encounter.enemy", fight.Get("attackerData")) : null;
        var release = OfferRelease(fight, launch, leave, enemy, enemyBonus);
        if (!Plugin.EncounterAdvice.Value) return;
        var player = fight.Get("attackerData")?.Get("Property");
        var enemyProperty = enemy.Get("Property");
        if (player == null || enemyProperty == null) return;
        // Enemy numbers are the exact public labels used by the official encounter screen.
        int? Stat(string name) => int.TryParse(launch.Field(name)!.Get("text")!.String(),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        var hp = Stat("txt_HP"); var atk = Stat("txt_ATK"); var def = Stat("txt_DEF");
        if (hp == null || atk == null || def == null) return;
        var counter = enemyProperty.Field("Counter");
        if (counter == null) return;
        var selfBonus = PublicCombatEffects.Read(fight.Get("attackerData"), "encounter.self", enemy);
        if (selfBonus == null || enemyBonus == null) return;
        var input = new EncounterInput(mapType, player.Field("HP")!.Get<int>("Value"),
            player.Field("ATK")!.Get<int>("Value"), player.Field("DEF")!.Get<int>("Value"),
            hp.Value, atk.Value, def.Value, counter.Get<bool>("Value"))
            { Modifiers = selfBonus.Value, EnemyModifiers = enemyBonus.Value };
        if (_encounterInput != input)
        {
            _encounterInput = input;
            _encounterResult = EncounterAdvisor.Calculate(input);
        }
        if (_encounterResult is not { } advice) return;
        EnsureBadges(attack, leave, width: 280, fontSize: 21);
        var scale = Math.Min(Plugin.UiScale.Value,
            Math.Max(180f, Math.Abs(attack.Get<float>("x") - leave.Get<float>("x")) - 12f) / 280f);
        _defBadge!.Show(advice.Quick, advice.Attack == true, scale, -86f * scale, visualOnly: true, guide: false);
        _dodgeBadge!.Show(release is { } offered ? $"몬스터 피해 {offered.Damage}\n스타라이트 +{offered.Stacks}" : string.Empty,
            advice.Attack == false, scale, -86f * scale,
            visualOnly: true, guide: false);
        _adviceShowing = true;
    }

    // #14 Shooting Star: what releasing this monster yields, shown on the Release button. Its own breaker clears only
    // the star, so a failure here never turns off the encounter advice.
    private static ReleaseGain? OfferRelease(RuntimeObject fight, RuntimeObject launch, RuntimeObject leave, RuntimeObject enemy, HitModifiers? enemyBonus)
    {
        if (!Plugin.ShootingStarShow.Value || !Compatibility.Allowed("ShootingStar")) return null;
        try
        {
            if (ShootingStar.Read(fight.Get("attackerData"), enemy, enemyBonus) is not { } gain) return null;
            ShootingStarUi.Show(leave, gain, Plugin.UiScale.Value);
            if (Plugin.Diagnostics.IsRecording)
            {
                Plugin.Diagnostics.State("shootingStar.offer", $"monsterHP={launch.Field("txt_HP")!.Get("text")!.String()}; "
                    + $"monsterType={enemy.Field("player")?.Get("Hero")?.Get<int>("MonsterType")}; damage={gain.Damage}; stacks=+{gain.Stacks}; {ShootingStar.Evidence}");
                ReleaseWatch.Offer(enemy, ShootingStar.Evidence);
            }
            return gain;
        }
        catch (Exception ex) { Compatibility.Block("ShootingStar", ex, ShootingStarUi.Clear); return null; }
    }

    private static void UpdateHover()
    {
        if (!_adviceShowing || _root == null) return;
        var hit = PointerPath().FirstOrDefault(item => item.Pointer == _defButton?.Pointer || item.Pointer == _dodgeButton?.Pointer);
        _defBadge?.Hover(hit != null && hit.Pointer == _defButton?.Pointer);
        _dodgeBadge?.Hover(hit != null && hit.Pointer == _dodgeButton?.Pointer);
    }

    internal static RuntimeObject NewLabel(RuntimeObject parent, string text, int size,
        int align = 1, bool rich = false, bool dark = false)
    {
        var label = RuntimeObject.New(rich ? RuntimeObject.FindClass("FairyGUI", "GRichTextField") : _textClass);
        label.Set("touchable", false);
        label.Set("autoSize", 0);
        label.Set("text", ModText.Text(text));
        StyleText(label, size, align);
        NativeUi.InheritText(label, dark);
        parent.Call("AddChild", label);
        return label;
    }

    // Mod-owned labels only. Preserve native typography; size/alignment belong to the layout.
    internal static void StyleText(RuntimeObject label, int size, int align = 1)
    {
        var format = label.Get("textFormat")!;
        format.SetField("size", size);
        format.SetField("align", align);
        label.Set("textFormat", format);
        NativeUi.OutlineText(label);
    }

    private static void HideAdvice(bool keepDice = false)
    {
        CardUi.Hide(keepDice);
        _observerSummary?.Hide();
        _adviceShowing = false;
        _defBadge?.Hide();
        _dodgeBadge?.Hide();
    }

    private static void ClearAdvice()
    {
        ClearAdviceDisplay(); RemainingHpUi.Clear();
    }

    // A language switch only redraws text: the current hit's final damage stays and is relabelled in place.
    private static void ClearAdviceDisplay()
    {
        HideAdvice(); CardUi.Clear(); RemainingHpUi.RefreshLanguage(); ShootingStarUi.Clear();
        _defBadge?.Dispose(); _defBadge = null;
        _dodgeBadge?.Dispose(); _dodgeBadge = null;
        _observerSummary?.Dispose(); _observerSummary = null;
    }

    internal static void Stop()
    {
        try { UpdateNotificationUi.Clear(); }
        catch (Exception ex) { Plugin.Diagnostics.Error("UpdateNotification.cleanup", ex); }
        HomeAvailable = false;
        SetSettingsModal(false);
        Dispose(_menu); _menu = null;
        ClearAdvice();
    }

    internal static void Dispose(RuntimeObject? obj)
    {
        if (obj != null && !obj.Get<bool>("isDisposed")) obj.Call("Dispose");
    }
}
