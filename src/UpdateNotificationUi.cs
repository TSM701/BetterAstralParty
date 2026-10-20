using UnityEngine;
using BetterAstralParty.Updating;
using BetterAstralParty.Observability;

namespace BetterAstralParty;

// Owned native clones, driven by the existing managed EventSystem.Update path.
// Home keyboard/game controls remain native; leaving Home closes this notice.
internal static class UpdateNotificationUi
{
    internal const string Feature = "UpdateNotification";
    private static readonly UpdateNotificationState State = new();
    private static readonly UpdateNotificationInput InputState = new();
    private static readonly Dictionary<IntPtr, (RuntimeObject Button, UpdateNoticeAction Action)> Controls = new();
    private static RuntimeObject? _panel, _frame, _view, _content, _body, _scrollTrack, _scrollThumb;
    private static (float Height, float Y, bool Visible)? _scrollMarker;
    private static UpdateNotificationTarget? _target;
    private static IntPtr _parent, _hovered;
    private static int _openedFrame, _consumedFrame = -1;
    private static int _contextGeneration;
    private static int _languageRevision = -1, _fontRevision = -1, _automaticRevision = -1, _releaseRevision = -1;
    private static float _openedAt;
    private sealed record Transition(ReleaseUpdate Release, ChannelTransitionProposal Proposal, ChannelTransitionReview Review, DiagnosticOperation Operation);
    private sealed record PendingTransition(Task<Transition> Task, CancellationTokenSource Cancellation, ReleaseUpdate Release, DiagnosticOperation Operation);
    private static PendingTransition? _pendingTransition;
    private static Transition? _transition;
    private static ReleaseUpdate? _failedTransition;
    private static string _failedReason = "";
    // An approved switch keeps its window open: it downloads and applies in this session, and only a
    // committed switch enables Exit and Switch.
    private sealed record Switching(string Version, DiagnosticOperation Operation, bool Transition = true) { internal bool Committed; }
    // One continuous progress bar with its percentage above the buttons, shared with the home progress capsule.
    private static RuntimeObject? _barTrack, _barFill, _barText;
    private const float BarX = 110, BarY = 426, BarWidth = 560, BarHeight = 18;
    private static int _progressRevision = -1;
    private static Switching? _switching;
    internal static bool ConsumedInput => _consumedFrame == Time.frameCount;
    internal static bool CanConfirmTransition => _pendingTransition == null && _transition == null && Plugin.Automatic.CanReviewTransition
        && Plugin.Updates.Result.Release is { } release && Plugin.Updates.CurrentRelease(release) && release.ProposalRequest(Plugin.Automatic.Snapshot.InstalledVersion) != null;

    internal static void BeginTransition()
    {
        if (!CanConfirmTransition) {
            DiagnosticHub.Stage(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, DiagnosticOutcome.Unavailable,
                DiagnosticOperation.New(), Plugin.Updates.Result.Release?.Version, DiagnosticCode.Prerequisite);
            DiagnosticHub.FlushSoon(); return;
        }
        var release = Plugin.Updates.Result.Release!;
        var request = release.ProposalRequest(Plugin.Automatic.Snapshot.InstalledVersion)!;
        Clear();
        var operation = DiagnosticOperation.New();
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        try {
            var proposal = Plugin.Automatic.ProposeTransition(request, DateTimeOffset.UtcNow, cancellation.Token);
            var root = Path.GetDirectoryName(BepInEx.Paths.BepInExRootPath)!;
            var task = Task.Run(async () => {
                var verified = await proposal.ConfigureAwait(false); token.ThrowIfCancellationRequested();
                using var fence = new WindowsFileFence(root);
                return new Transition(release, verified, ChannelTransitionIntent.Review(fence, verified.Descriptor), operation);
            }, token);
            _pendingTransition = new(task, cancellation, release, operation);
            DiagnosticHub.Stage(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, DiagnosticOutcome.Begin, operation, release.Version);
        } catch (Exception error) {
            cancellation.Dispose(); TransitionFailure(error, operation, release);
        }
    }

    // A newer scan still running or finding the same release keeps the review; only a different target retires it.
    private static bool TargetHolds(ReleaseUpdate release) => Plugin.Updates.Result.Status == ReleaseUpdateStatus.Checking || Plugin.Updates.SameRelease(release);
    // The home progress capsule reopens the running update here, even after its window was closed.
    internal static void ShowProgress()
    {
        var progress = UpdateProgress.Shared;
        if (!progress.Visible || _switching != null && _panel != null) return;
        var release = Plugin.Updates.Result.Release;
        if (release == null || !GameUi.NotificationHome().Eligible) { ModUi.OpenUpdateSettings(); return; }
        Clear(false);
        _switching = new(progress.Version, DiagnosticOperation.New(), progress.Transition) { Committed = progress.Done };
        Create(GameUi.Root!, new UpdateNotificationTarget(release, "progress"));
    }

    private static bool CurrentTransition(Transition transition) => TargetHolds(transition.Release)
        && Plugin.Automatic.CurrentTransitionProposal(transition.Proposal)
        && transition.Review.Target.DescriptorSha256 == transition.Proposal.Descriptor.DescriptorSha256;
    private static void TransitionFailure(Exception error, DiagnosticOperation operation, ReleaseUpdate release)
    {
        var safety = error as ApplySafetyException; var validation = error as UpdateValidationException;
        var code = validation?.Failure == UpdateFailure.InvalidSignature ? DiagnosticCode.InvalidSignature
            : validation?.Failure == UpdateFailure.WrongTarget ? DiagnosticCode.WrongTarget
            : validation != null ? DiagnosticCode.Integrity : DiagnosticCode.Prerequisite;
        _failedTransition = release;
        var retryAt = error.Data["BapRetryAt"] as DateTimeOffset?;
        var reason = retryAt != null ? "retry" : validation != null ? "verify" : safety != null ? "install"
            : error is InvalidOperationException ? "changed" : "network";
        _failedReason = ModText.ChannelTransitionReason(reason, retryAt is { } at ? Math.Max(1, (int)Math.Ceiling((at - DateTimeOffset.UtcNow).TotalSeconds)) : 0);
        Plugin.Diagnostics.Write("updateTransition stopped=" + reason + "; code=" + code);
        DiagnosticHub.Failure(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, code, error,
            safety?.NativeError ?? 0, validation != null ? (int)validation.Failure : 0, operation, safety != null ? (int)safety.Failure : 0, release.Version);
        DiagnosticHub.FlushSoon();
    }
    private static void PollTransition()
    {
        var pending = _pendingTransition;
        if (pending == null) return;
        if (!TargetHolds(pending.Release)) {
            Clear(false); TransitionFailure(new InvalidOperationException("Channel transition target changed during review"), pending.Operation, pending.Release); return;
        }
        if (!pending.Task.IsCompleted) return;
        _pendingTransition = null; pending.Cancellation.Dispose();
        try {
            var transition = pending.Task.GetAwaiter().GetResult();
            if (!CurrentTransition(transition)) throw new InvalidOperationException("Channel transition review became stale");
            _transition = transition;
        } catch (Exception error) { TransitionFailure(error, pending.Operation, pending.Release); }
    }

    internal static void Observe() => State.Observe(Plugin.Updates.Result, Plugin.Updates.Channel, Plugin.Updates.Generation);

    internal static void Tick()
    {
        try { if (!Compatibility.Allowed(Feature)) { Clear(); return; } TickCore(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void TickCore()
    {
        PollTransition();
        if (!Plugin.UpdateNotifications.Value && _pendingTransition == null && _transition == null && _failedTransition == null && _switching == null) { Clear(); return; }
        var home = GameUi.NotificationHome();
        if (!home.Eligible) { Clear(); return; }
        if (_pendingTransition != null) return; // Ordinary notices must not replace an in-flight explicit review.
        var root = GameUi.Root!;
        if (_panel != null && (_panel.Get<bool>("isDisposed") || _parent != root.Pointer
            || _transition == null && _failedTransition == null && _switching == null && (_contextGeneration != State.ContextGeneration
            || _target == null || State.Active?.Key != _target.Key && State.Next?.Key != _target.Key)))
            Clear();
        if (_transition != null && !CurrentTransition(_transition)) {
            var stale = _transition;
            Clear(false); TransitionFailure(new InvalidOperationException("Channel transition review became stale"), stale.Operation, stale.Release);
        }
        if (_panel == null)
        {
            // Never take the click/key which just opened or left another native screen.
            if (!Application.isFocused || UnityEngine.Input.GetMouseButtonDown(0)
                || UnityEngine.Input.GetMouseButtonUp(0) || UnityEngine.Input.anyKeyDown) return;
            if (_transition == null && _failedTransition == null && State.Next == null) return;
            var target = _transition != null ? new UpdateNotificationTarget(_transition.Release, "transition")
                : _failedTransition != null ? new UpdateNotificationTarget(_failedTransition, "transition-failed") : State.Next!;
            Create(root, target);
        }
        Layout(root);
        if (_target == null) return;
        if (_transition == null && _failedTransition == null && _switching == null && State.Active == null && GameUi.Visible(_panel) && _panel!.Get<float>("alpha") > 0)
            State.Shown(_target.Key);
        if (_languageRevision != ModText.Revision || _fontRevision != ModFont.Revision || _automaticRevision != Plugin.Automatic.Revision
            || _releaseRevision != Plugin.Updates.Revision || _progressRevision != UpdateProgress.Shared.Revision) Refresh();
        UpdateScrollMarker();
        if (!Application.isFocused) { InputState.Reset(); return; }
        HandleInput();
    }

    private static void Create(RuntimeObject root, UpdateNotificationTarget target)
    {
        _target = target; _parent = root.Pointer;
        _contextGeneration = State.ContextGeneration;
        _openedFrame = Time.frameCount; _openedAt = Time.unscaledTime;
        _panel = NativeUi.Component(root, MenuLayout.Width, MenuLayout.Height);
        _panel.Set("visible", false);
        _panel.Set("sortingOrder", 29900); // Below the existing mod settings.
        _panel.Set("opaque", true); // Only our frame bounds, no global dim/input layer.
        _frame = NativeUi.Create("Common", "Com_PopUpWindow_Bottom")
            ?? throw new InvalidOperationException("Native update notice frame unavailable");
        _panel.Call("AddChild", _frame);
        foreach (var name in new[] { "btn_Sure", "btn_Sure_Only", "btn_Cancel" })
            _frame.Call("GetChild", name)!.Set("visible", false);
        var heading = _frame.Call("GetChild", "title")!;
        heading.Set("touchable", false);
        GameUi.StyleText(heading, 42);
        ModFont.Track(heading);
        var close = _frame.Call("GetChild", "closeButton")!;
        Controls.Add(close.Pointer, (close, UpdateNoticeAction.Close));
        var scrollTemplate = NativeUi.Create("Common", "Com_BuffInfo")
            ?? throw new InvalidOperationException("Native update notice scroll list unavailable");
        try
        {
            _view = scrollTemplate.Call("GetChild", "list_Buff")!;
            _view.Get("relations")!.Call("ClearAll");
            _panel.Call("AddChild", _view);
        }
        finally { GameUi.Dispose(scrollTemplate); }
        _view.Call("RemoveChildren", 0, -1, true);
        _view.Call("SetXY", 110f, 110f);
        _view.Call("SetSize", 840f, 300f);
        _view.Set("autoResizeItem", false);
        _view.Set("layout", 0);
        _view.SetField("selectionMode", 3);
        _view.SetField("scrollItemToViewOnClick", false);
        _view.Set("touchable", true);
        _view.Set("opaque", true);
        var scroll = _view.Get("scrollPane")!;
        scroll.Set("mouseWheelEnabled", true);
        scroll.Set("bouncebackEffect", false);
        scroll.Set("scrollStep", 48f);
        var contentWidth = scroll.Get<float>("viewWidth");
        if (!float.IsFinite(contentWidth) || contentWidth <= 0)
            throw new InvalidOperationException("Native update notice scroll width unavailable");
        _content = NativeUi.Component(_view, contentWidth, 300);
        _body = NativeUi.Label(_content, "", 0, 0, contentWidth, 300, 30);
        _body.Set("touchable", false);
        _body.Set("UBBEnabled", false);
        _body.Set("singleLine", false);
        _body.Set("autoSize", 2);
        GameUi.StyleText(_body, 30, align: 0);
        // This native list hides its scrollbar; reuse the settings position indicator.
        _scrollTrack = new NativeUi.Surface(_panel, 12, 300, fixedOpacity: true,
            fill: new Color(0.75f, 0.75f, 0.75f)).Graph;
        _scrollThumb = new NativeUi.Surface(_panel, 12, 24, fixedOpacity: true,
            fill: new Color(1f, 0.8f, 0f)).Graph;
        _scrollTrack.Call("SetXY", 958f, 110f);
        _scrollMarker = null;
        CreateBar();
        AddButton(UpdateNoticeAction.Close, "Button_ReturnRounded", 168);
        AddButton(_switching != null ? UpdateNoticeAction.ExitGame : _transition != null ? UpdateNoticeAction.ConfirmTransition : UpdateNoticeAction.Settings, "Button_ConfirmRounded", 610);
        _languageRevision = _fontRevision = _automaticRevision = _releaseRevision = -1;
        Refresh();
        scroll.Call("ScrollTop", false);
        _panel.Set("visible", true);
    }

    // The progress bar between the text and the buttons: a pale track, a coloured fill and the percentage.
    private static void CreateBar()
    {
        _barTrack = new NativeUi.Surface(_panel!, BarWidth, BarHeight, fixedOpacity: true, fill: new Color(0.9f, 0.875f, 0.8f)).Graph;
        _barTrack.Call("SetXY", BarX, BarY);
        _barFill = new NativeUi.Surface(_panel!, BarHeight - 4, BarHeight - 4, fixedOpacity: true, fill: new Color(1f, 0.76f, 0.05f)).Graph;
        _barFill.Call("SetXY", BarX + 2, BarY + 2);
        _barText = NativeUi.Label(_panel!, "", BarX + BarWidth + 14, BarY - 9, 950 - (BarX + BarWidth + 14), 36, 22);
        _barText.Set("touchable", false);
        GameUi.StyleText(_barText, 22, align: 0);
    }

    private static void AddButton(UpdateNoticeAction action, string item, float x)
    {
        var button = NativeUi.Create("Common", item)
            ?? throw new InvalidOperationException("Native update notice button unavailable");
        var scale = 298 / button.Get<float>("width");
        NativeUi.Position(button, x, 484, scale);
        button.SetField("changeStateOnClick", false);
        _panel!.Call("AddChild", button);
        Controls.Add(button.Pointer, (button, action));
    }

    private static void Refresh()
    {
        _languageRevision = ModText.Revision;
        _automaticRevision = Plugin.Automatic.Revision;
        _releaseRevision = Plugin.Updates.Revision;
        _progressRevision = UpdateProgress.Shared.Revision;
        var transitionView = _transition != null || _failedTransition != null || _switching is { Transition: true };
        _frame!.Call("GetChild", "title")!.Set("text", transitionView ? ModText.ChannelTransitionTitle
            : _switching != null ? ModText.UpdateProgressTitle : ModText.Text("새 업데이트"));
        var review = _transition?.Review;
        var status = Plugin.Automatic.Status;
        var progress = UpdateProgress.Shared;
        if (_switching != null && (status == AutomaticUpdateStatus.Committed || progress.Done)) _switching.Committed = true;
        var committed = _switching?.Committed == true;
        _body!.Set("text", _failedTransition != null ? ModText.ChannelTransitionUnavailable + "\n\n" + _failedReason
            : _switching != null && progress.Stopped && !committed ? ModText.UpdateStopDetails(_switching.Version, progress.Percent, progress.Reason, progress.Code)
            : _switching is { Transition: true } ? committed ? ModText.ChannelTransitionReady(_switching.Version)
                : SwitchRunning(status) ? ModText.ChannelTransitionProgress(_switching.Version, status) : ModText.ChannelTransitionStopped(status)
            : _switching != null ? committed ? ModText.UpdateProgressReady(_switching.Version)
                : SwitchRunning(status) ? ModText.UpdateProgressBody(_switching.Version, status) : ModText.UpdateProgressStopped(status)
            : review != null ? ModText.ChannelTransitionDetails(review.SourceChannel, review.CurrentVersion, review.Target.Channel,
                // The Pre-release channel also offers a newer full Release; name the target by its own kind.
                _transition!.Release.Beta ? "Beta" : _transition.Release.GitHubPrerelease ? "PreRelease" : "Stable", review.Target.Version, review.Target.Repository)
            : ModText.UpdateNotificationDetails(_target!.Release, Plugin.Version,
                Plugin.Automatic.Status, Plugin.Updates.Result.Status, Plugin.Automatic.Snapshot.Matches(_target.Release)));
        ModFont.Track(_body);
        _content!.Call("SetSize", _body.Get<float>("width"), Math.Max(300f, _body.Get<float>("textHeight")));
        _view!.Call("EnsureBoundsCorrect");
        var retry = _switching != null && progress.Stopped && !committed;
        foreach (var key in Controls.Keys.ToList())
            if (Controls[key].Action is UpdateNoticeAction.ExitGame or UpdateNoticeAction.Retry)
                Controls[key] = (Controls[key].Button, retry ? UpdateNoticeAction.Retry : UpdateNoticeAction.ExitGame);
        foreach (var (button, action) in Controls.Values)
        {
            if (button.Pointer == _frame.Call("GetChild", "closeButton")!.Pointer) continue;
            NativeUi.StyleTitle(button, action == UpdateNoticeAction.ConfirmTransition ? ModText.ChannelTransitionConfirm
                : action == UpdateNoticeAction.Retry ? ModText.UpdateRetry
                : action == UpdateNoticeAction.ExitGame ? _switching is { Transition: false } ? ModText.UpdateProgressExit : ModText.ChannelTransitionExit
                : ModText.Text(action == UpdateNoticeAction.Settings ? "업데이트 설정" : "닫기"));
            // Exit and Switch stays gray and inert until the switch is committed.
            if (action == UpdateNoticeAction.ExitGame) button.Set("grayed", !committed);
            if (action == UpdateNoticeAction.Retry) button.Set("grayed", false);
        }
        RefreshBar();
        _fontRevision = ModFont.Revision;
    }

    // Yellow while running, green when done, red when stopped; hidden while nothing runs.
    private static void RefreshBar()
    {
        var progress = UpdateProgress.Shared;
        foreach (var part in new[] { _barTrack, _barFill, _barText }) part?.Set("visible", progress.Visible);
        if (!progress.Visible || _barFill == null || _barText == null) return;
        _barFill.Call("SetSize", Math.Max(BarHeight - 4, (BarWidth - 4) * progress.Percent / 100f), BarHeight - 4);
        _barFill.Set("color", progress.Stopped ? new Color(0.88f, 0.29f, 0.29f) : progress.Done ? new Color(0.49f, 0.78f, 0.42f) : new Color(1f, 0.76f, 0.05f));
        _barText.Set("text", ModText.UpdatePercent(progress.Percent, progress.Read, progress.Total));
        ModFont.Track(_barText);
    }

    private static bool SwitchRunning(AutomaticUpdateStatus status) => status is AutomaticUpdateStatus.Preparing or AutomaticUpdateStatus.Downloading
        or AutomaticUpdateStatus.Ready or AutomaticUpdateStatus.Queued or AutomaticUpdateStatus.RetryWaiting;

    private static void Layout(RuntimeObject root)
    {
        var width = root.Get<float>("width"); var height = root.Get<float>("height");
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new InvalidOperationException("Update notice root size unavailable");
        var scale = MenuLayout.FitScale(width, height);
        _panel!.Call("SetScale", scale, scale);
        var reveal = AdviceText.Reveal(Time.unscaledTime - _openedAt, 0.22f);
        _panel.Call("SetXY", (width - MenuLayout.Width * scale) / 2,
            (height - MenuLayout.Height * scale) / 2 + (1 - reveal) * 24);
        _panel.Set("alpha", reveal);
    }

    private static void UpdateScrollMarker()
    {
        var scroll = _view!.Get("scrollPane")!;
        var view = scroll.Get<float>("viewHeight");
        var content = scroll.Get<float>("contentHeight");
        var (height, y) = MenuLayout.ScrollMarker(view, content, scroll.Get<float>("scrollingPosY"));
        var visible = content > view;
        if (_scrollMarker == (height, y, visible)) return;
        _scrollMarker = (height, y, visible);
        _scrollTrack!.Set("visible", visible);
        _scrollThumb!.Set("visible", visible);
        _scrollThumb.Call("SetSize", 12f, height);
        _scrollThumb.Call("SetXY", 958f, 110f + y);
    }

    private static void HandleInput()
    {
        var hit = GameUi.PointerPath().FirstOrDefault(item => Controls.ContainsKey(item.Pointer));
        var pointer = hit?.Pointer ?? IntPtr.Zero;
        if (_hovered != pointer)
        {
            if (Controls.TryGetValue(_hovered, out var old)) NativeUi.Play(old.Button, "Switchout");
            if (hit != null) NativeUi.Play(hit, "Switchin");
            _hovered = pointer;
        }
        var action = hit != null && GameUi.Visible(hit) && hit.Get<bool>("touchable") && !hit.Get<bool>("grayed")
            ? Controls[pointer].Action : UpdateNoticeAction.None;
        var dispatched = InputState.Step(Time.frameCount, _openedFrame, action,
            UnityEngine.Input.GetMouseButtonDown(0), UnityEngine.Input.GetMouseButtonUp(0),
            UnityEngine.Input.GetKeyDown(KeyCode.Escape), UnityEngine.Input.GetKeyDown(KeyCode.Return),
            UnityEngine.Input.GetKeyDown(KeyCode.F8), pointer);
        if (dispatched == UpdateNoticeAction.None) return;
        _consumedFrame = Time.frameCount;
        if (dispatched == UpdateNoticeAction.ConfirmTransition && _transition is { } transition) {
            try {
                if (!CurrentTransition(transition)) throw new InvalidOperationException("Channel transition review became stale");
                var intent = ChannelTransitionIntent.Approve(transition.Review);
                var request = transition.Release.DownloadRequest(transition.Review.CurrentVersion, Plugin.Updates.Channel, intent);
                if (request == null || !Plugin.Automatic.ConfirmTransition(request, Plugin.Updates.Generation, DateTimeOffset.UtcNow, true))
                    throw new InvalidOperationException("Channel transition could not be started");
                DiagnosticHub.Stage(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, DiagnosticOutcome.Completed, transition.Operation, transition.Release.Version);
                DiagnosticHub.FlushSoon();
                _transition = null; _switching = new(transition.Release.Version, transition.Operation);
                if (Controls.TryGetValue(pointer, out var confirm)) Controls[pointer] = (confirm.Button, UpdateNoticeAction.ExitGame);
                Refresh();
            } catch (Exception error) { Clear(false); TransitionFailure(error, transition.Operation, transition.Release); }
            return;
        }
        if (dispatched == UpdateNoticeAction.Retry) {
            // The notice stays presentation-only: Update Settings performs the retry, as its Update button would.
            Clear(); ModUi.RetryUpdateFromNotice();
            return;
        }
        if (dispatched == UpdateNoticeAction.ExitGame) {
            if (_switching is not { Committed: true }) return;
            DiagnosticHub.Stage(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, DiagnosticOutcome.Completed, _switching.Operation, _switching.Version);
            DiagnosticHub.FlushSoon(); Plugin.Diagnostics.Write("updateTransition exit=" + _switching.Version);
            Clear(false);
            Application.Quit(); // The game's normal exit; the switched version runs from the next launch.
            return;
        }
        Clear();
        if (dispatched == UpdateNoticeAction.Settings) ModUi.OpenUpdateSettings();
    }

    internal static void CloseForSettings() { _consumedFrame = Time.frameCount; Clear(); }

    internal static void Clear() => Clear(true);
    private static void Clear(bool cancelled)
    {
        var pending = _pendingTransition; var transition = _transition;
        _pendingTransition = null; _transition = null; _failedTransition = null; _switching = null; _failedReason = "";
        if (pending != null) {
            try { pending.Cancellation.Cancel(); } catch { }
            pending.Cancellation.Dispose();
            _ = pending.Task.ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
        }
        if (cancelled && (pending != null || transition != null)) {
            DiagnosticHub.Stage(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, DiagnosticOutcome.Cancelled,
                pending?.Operation ?? transition!.Operation, pending?.Release.Version ?? transition!.Release.Version);
            DiagnosticHub.FlushSoon();
        }
        State.Close(); InputState.Reset();
        var panel = _panel;
        _frame = _view = _content = _body = _scrollTrack = _scrollThumb = _barTrack = _barFill = _barText = null; _target = null; _progressRevision = -1;
        _scrollMarker = null;
        _parent = _hovered = IntPtr.Zero; Controls.Clear();
        if (panel == null) return;
        Exception? failure = null;
        void Attempt(Action action) { try { action(); } catch (Exception ex) { failure ??= ex; } }
        var disposed = false;
        Attempt(() => disposed = panel.Get<bool>("isDisposed"));
        if (!disposed)
        {
            // Each operation is independent: a failed getter must not prevent
            // input removal, hiding or a later cleanup retry of this owned clone.
            Attempt(() => panel.Set("touchable", false));
            Attempt(() => panel.Set("visible", false));
            Attempt(() => GameUi.Dispose(panel));
            Attempt(() => disposed = panel.Get<bool>("isDisposed"));
        }
        if (disposed) _panel = null; // Retain ownership until disposal is confirmed.
        if (failure != null) Compatibility.Block(Feature, failure);

    }
}
