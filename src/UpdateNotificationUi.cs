using UnityEngine;

namespace BetterAstralParty;

// Owned native clones, driven by the existing managed EventSystem.Update path.
// Home keyboard/game controls remain native; leaving Home closes this notice.
internal static class UpdateNotificationUi
{
    internal const string Feature = "UpdateNotification";
    private static readonly UpdateNotificationState State = new();
    private static readonly UpdateNotificationInput InputState = new();
    private static readonly Dictionary<IntPtr, (RuntimeObject Button, UpdateNoticeAction Action)> Controls = new();
    private static RuntimeObject? _panel, _frame, _body;
    private static UpdateNotificationTarget? _target;
    private static IntPtr _parent, _hovered;
    private static int _openedFrame, _consumedFrame = -1;
    private static int _contextGeneration;
    private static int _languageRevision = -1, _automaticRevision = -1, _releaseRevision = -1;
    private static float _openedAt;
    internal static bool ConsumedInput => _consumedFrame == Time.frameCount;

    internal static void Observe() => State.Observe(Plugin.Updates.Result, Plugin.Updates.Channel, Plugin.Updates.Generation);

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) { Clear(); return; }
        try { TickCore(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void TickCore()
    {
        var home = GameUi.NotificationHome();
        if (!home.Eligible) { Clear(); return; }
        var root = GameUi.Root!;
        if (_panel != null && (_panel.Get<bool>("isDisposed") || _parent != root.Pointer
            || _contextGeneration != State.ContextGeneration
            || _target == null || State.Active?.Key != _target.Key && State.Next?.Key != _target.Key))
            Clear();
        if (_panel == null)
        {
            // Never take the click/key which just opened or left another native screen.
            if (!Application.isFocused || UnityEngine.Input.GetMouseButtonDown(0)
                || UnityEngine.Input.GetMouseButtonUp(0) || UnityEngine.Input.anyKeyDown) return;
            if (State.Next is not { } target) return;
            Create(root, target);
        }
        Layout(root);
        if (_target == null) return;
        if (State.Active == null && GameUi.Visible(_panel) && _panel!.Get<float>("alpha") > 0)
            State.Shown(_target.Key);
        if (_languageRevision != ModText.Revision || _automaticRevision != Plugin.Automatic.Revision
            || _releaseRevision != Plugin.Updates.Revision) Refresh();
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
        _body = NativeUi.Label(_panel, "", 110, 110, 840, 300, 30);
        _body.Set("touchable", false);
        _body.Set("UBBEnabled", false);
        _body.Set("singleLine", false);
        _body.Set("autoSize", 3);
        AddButton(UpdateNoticeAction.Close, "Button_ReturnRounded", 168);
        AddButton(UpdateNoticeAction.Settings, "Button_ConfirmRounded", 610);
        _languageRevision = _automaticRevision = _releaseRevision = -1;
        Refresh();
        _panel.Set("visible", true);
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
        _frame!.Call("GetChild", "title")!.Set("text", ModText.Text("새 업데이트"));
        _body!.Set("text", ModText.UpdateNotificationDetails(_target!.Release, Plugin.Version,
            Plugin.Automatic.Status, Plugin.Updates.Result.Status, Plugin.Automatic.Snapshot.Matches(_target.Release)));
        ModFont.Track(_body);
        foreach (var (button, action) in Controls.Values)
        {
            if (button.Pointer == _frame.Call("GetChild", "closeButton")!.Pointer) continue;
            NativeUi.StyleTitle(button, ModText.Text(action == UpdateNoticeAction.Settings ? "업데이트 설정" : "닫기"));
        }
    }

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
        Clear();
        if (dispatched == UpdateNoticeAction.Settings) ModUi.OpenUpdateSettings();
    }

    internal static void CloseForSettings() { _consumedFrame = Time.frameCount; Clear(); }

    internal static void Clear()
    {
        State.Close(); InputState.Reset();
        var panel = _panel;
        _frame = _body = null; _target = null;
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
