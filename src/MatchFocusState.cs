namespace BetterAstralParty;

internal enum MatchFocusResult { Ignored, Duplicate, AlreadyForeground, WindowUnavailable, RestoreFailed, Activated, TaskbarNotified, CapacityReached, Pending, RaiseFailed }

internal interface IMatchFocusWindow
{
    IntPtr FindGameWindow();
    bool IsGameWindow(IntPtr window);
    bool GameProcessIsForeground { get; }
    bool IsMinimized(IntPtr window);
    bool Restore(IntPtr window);
    bool IsForeground(IntPtr window);
    bool Activate(IntPtr window);
    bool RestoreTopmost();
    bool Raise(IntPtr window);
    void Flash(IntPtr window);
}

// Keep identities across OFF/ON and reconnects, and bound session memory.
internal sealed class MatchFocusState
{
    private readonly HashSet<long> _seen = new();
    private bool _matching, _pendingMatchRoom;
    private IntPtr _window;
    private long _waitingRoom, _pendingRoom, _started, _lastAttempt, _lastRaise;
    private int _attempts, _raiseAttempts;
    internal const long ActivationTimeout = 1000, RetryInterval = 250;
    internal const int MaximumMatches = 4096;
    internal void ObserveExisting(long room) { if (room > 0 && _seen.Count < MaximumMatches) _seen.Add(room); }
    internal void Unavailable() { _matching = false; _waitingRoom = 0; ClearPending(); }
    private void ClearPending() { _window = IntPtr.Zero; _pendingRoom = 0; _pendingMatchRoom = false; _attempts = _raiseAttempts = 0; }

    internal MatchFocusResult Poll(bool enabled, long room, bool matchRoom, bool choosing, bool pve,
        int queueStatus, bool reconnecting, IMatchFocusWindow windows, long now = 0, bool waiting = false)
    {
        if (_pendingRoom != 0)
        {
            if (!enabled || room != _pendingRoom || !pve || matchRoom != _pendingMatchRoom || !choosing || reconnecting)
                Unavailable();
            else return Complete(windows, now);
        }
        if (reconnecting) { Unavailable(); ObserveExisting(room); return MatchFocusResult.Ignored; }
        if (room <= 0)
        {
            _waitingRoom = 0;
            if (!enabled || queueStatus is not (2 or 3)) _matching = false;
            else if (queueStatus == 2) _matching = true;
            // Playing may precede the success room, but cannot arm an existing session itself.
            return MatchFocusResult.Ignored;
        }
        var privateStart = enabled && pve && !matchRoom && choosing && !waiting && _waitingRoom == room;
        _waitingRoom = enabled && pve && !matchRoom && waiting && !choosing ? room : 0;
        var expected = _matching;
        _matching = false;
        if (privateStart)
        {
            _seen.Remove(room); // An observed lobby start is a new episode even when the room ID is reused.
            return Begin(enabled, room, matchRoom, reconnecting, windows, now);
        }
        if (!expected || !pve || !matchRoom || !choosing)
        {
            ObserveExisting(room);
            return MatchFocusResult.Ignored;
        }
        return Success(enabled, room, matchRoom, choosing, reconnecting, windows, now);
    }

    internal MatchFocusResult Success(bool enabled, long room, bool matchRoom, bool choosing, bool reconnecting, IMatchFocusWindow windows, long now = 0)
    {
        if (room <= 0 || !matchRoom || !choosing) return MatchFocusResult.Ignored;
        return Begin(enabled, room, matchRoom, reconnecting, windows, now);
    }

    private MatchFocusResult Begin(bool enabled, long room, bool matchRoom, bool reconnecting, IMatchFocusWindow windows, long now)
    {
        if (!enabled || reconnecting) { ObserveExisting(room); return MatchFocusResult.Ignored; }
        if (_seen.Contains(room)) return MatchFocusResult.Duplicate;
        if (_seen.Count >= MaximumMatches) return MatchFocusResult.CapacityReached;
        _seen.Add(room); // Consume even if unavailable/refused: one bounded activation episode.
        var processForeground = windows.GameProcessIsForeground;
        var window = windows.FindGameWindow();
        if (processForeground && !windows.IsForeground(window)) return MatchFocusResult.AlreadyForeground;
        if (!windows.IsGameWindow(window)) return MatchFocusResult.WindowUnavailable;
        if (windows.IsMinimized(window))
        {
            if (!windows.IsGameWindow(window)) return MatchFocusResult.WindowUnavailable;
            if (!windows.Restore(window))
            {
                if (windows.GameProcessIsForeground && !windows.IsForeground(window)) return MatchFocusResult.AlreadyForeground;
                if (windows.IsGameWindow(window)) windows.Flash(window);
                return MatchFocusResult.RestoreFailed;
            }
        }
        _window = window; _pendingRoom = room; _pendingMatchRoom = matchRoom; _started = now; _lastAttempt = _lastRaise = now; _attempts = _raiseAttempts = 0;
        return Complete(windows, now);
    }

    private MatchFocusResult Complete(IMatchFocusWindow windows, long now)
    {
        if (!windows.IsGameWindow(_window)) { ClearPending(); return MatchFocusResult.WindowUnavailable; }
        var foreground = windows.IsForeground(_window);
        if (!foreground && windows.GameProcessIsForeground) { ClearPending(); return MatchFocusResult.AlreadyForeground; }
        var minimized = windows.IsMinimized(_window);
        if (foreground && !minimized) return CompleteRaise(windows, now);
        if (now - _started >= ActivationTimeout || now < _started)
        {
            windows.Flash(_window); ClearPending();
            return minimized ? MatchFocusResult.RestoreFailed : MatchFocusResult.TaskbarNotified;
        }
        if (!minimized && (_attempts == 0 || _attempts < 2 && now - _lastAttempt >= RetryInterval))
        {
            _attempts++; _lastAttempt = now;
            _ = windows.Activate(_window);
            if (!windows.IsGameWindow(_window)) { ClearPending(); return MatchFocusResult.WindowUnavailable; }
            if (windows.IsForeground(_window))
                return windows.IsMinimized(_window) ? MatchFocusResult.Pending : CompleteRaise(windows, now);
            if (windows.GameProcessIsForeground) { ClearPending(); return MatchFocusResult.AlreadyForeground; }
        }
        return MatchFocusResult.Pending;
    }

    private MatchFocusResult CompleteRaise(IMatchFocusWindow windows, long now)
    {
        if (now - _started >= ActivationTimeout || now < _started)
        { ClearPending(); return MatchFocusResult.RaiseFailed; }
        if (_raiseAttempts == 0 || _raiseAttempts < 2 && now - _lastRaise >= RetryInterval)
        {
            _raiseAttempts++; _lastRaise = now;
            var accepted = windows.Raise(_window);
            if (!windows.IsGameWindow(_window)) { ClearPending(); return MatchFocusResult.WindowUnavailable; }
            if (accepted && windows.IsForeground(_window) && !windows.IsMinimized(_window))
            { ClearPending(); return MatchFocusResult.Activated; }
        }
        return MatchFocusResult.Pending;
    }
}
