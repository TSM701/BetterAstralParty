namespace BetterAstralParty;

// Session-only presentation state. No settings, IO or updater commands.
internal sealed record UpdateNotificationTarget(ReleaseUpdate Release, string Key);
internal readonly record struct UpdateNotificationHome(bool Known, bool HomeVisible, bool CurrentHome,
    bool ControlsReady, bool Blocked, long TeamId, int RoomState, long ConnectRoomId)
{
    internal bool Eligible => Known && HomeVisible && CurrentHome && ControlsReady && !Blocked
        && TeamId == 0 && RoomState == 0 && ConnectRoomId == 0;
}

internal sealed class UpdateNotificationState
{
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private ReleaseChannel? _channel;
    private UpdateNotificationTarget? _pending;
    private bool _fresh;
    internal int ContextGeneration { get; private set; }
    internal UpdateNotificationTarget? Active { get; private set; }
    internal UpdateNotificationTarget? Next => Active == null && _fresh ? _pending : null;

    internal void Observe(ReleaseUpdateResult result, ReleaseChannel channel, int generation = -1)
    {
        if (_channel != channel) { ClearContext(); ContextGeneration++; _channel = channel; }
        if (result.Status is ReleaseUpdateStatus.NotConfigured or ReleaseUpdateStatus.NotChecked
            or ReleaseUpdateStatus.AuthenticationRequired or ReleaseUpdateStatus.AccessUnavailable
            or ReleaseUpdateStatus.UpToDate
            || result.Failure is ReleaseUpdateStatus.AuthenticationRequired or ReleaseUpdateStatus.AccessUnavailable)
        { ClearContext(); return; }
        // Configure may retain backoff but clears private release data across accounts.
        if (result.Release == null && result.Status != ReleaseUpdateStatus.Checking)
        { ClearContext(); return; }
        _fresh = result.Status == ReleaseUpdateStatus.Available;
        if (!_fresh) return;
        if (result.Release is not { } release) { ClearContext(); return; }
        if (release.TransitionCandidate || generation >= 0 && release.Generation != generation
            || !ReleaseUpdates.TryVersion(release.Version, out _)) { ClearContext(); return; }
        var key = (release.Version.StartsWith('v') ? release.Version[1..] : release.Version).Split('+')[0];
        _pending = _shown.Contains(key) ? null : new(release, key);
    }

    internal bool Shown(string key)
    {
        if (Next is not { } target || target.Key != key || !_shown.Add(key)) return false;
        Active = target; _pending = null; return true;
    }
    internal void Close() => Active = null;
    private void ClearContext()
    {
        if (Active != null || _pending != null) ContextGeneration++;
        Active = _pending = null; _fresh = false;
    }
}

internal enum UpdateNoticeAction { None, Close, Settings }

// A press must start and finish on the same owned control. One dispatch per frame.
internal sealed class UpdateNotificationInput
{
    private UpdateNoticeAction _pressed;
    private IntPtr _pressedPointer;
    private int _lastFrame = -1;
    internal void Reset() { _pressed = UpdateNoticeAction.None; _pressedPointer = IntPtr.Zero; }
    internal UpdateNoticeAction Step(int frame, int openedFrame, UpdateNoticeAction hit,
        bool down, bool up, bool escape, bool enter, bool settingsKey, IntPtr control)
    {
        if (_lastFrame == frame) return UpdateNoticeAction.None;
        _lastFrame = frame;
        if (frame <= openedFrame) { Reset(); return UpdateNoticeAction.None; }
        if (escape || enter || settingsKey)
        { Reset(); return settingsKey ? UpdateNoticeAction.Settings : UpdateNoticeAction.Close; }
        if (down) { _pressed = hit; _pressedPointer = control; }
        if (!up) return UpdateNoticeAction.None;
        var action = hit != UpdateNoticeAction.None && hit == _pressed && control != IntPtr.Zero
            && control == _pressedPointer ? hit : UpdateNoticeAction.None;
        Reset(); return action;
    }
}
