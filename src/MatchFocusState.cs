namespace BetterAstralParty;

internal enum MatchFocusResult { Ignored, Duplicate, AlreadyForeground, WindowUnavailable, RestoreFailed, Activated, TaskbarNotified, CapacityReached }

internal interface IMatchFocusWindow
{
    IntPtr FindGameWindow();
    bool IsGameWindow(IntPtr window);
    bool GameProcessIsForeground { get; }
    bool IsMinimized(IntPtr window);
    bool Restore(IntPtr window);
    bool Activate(IntPtr window);
    void Flash(IntPtr window);
}

// Event transport is deliberately separate. No game hook calls this yet.
// Keep identities across OFF/ON and reconnects, and bound session memory.
internal sealed class MatchFocusState
{
    private readonly HashSet<long> _seen = new();
    internal const int MaximumMatches = 4096;
    internal void ObserveExisting(long room) { if (room > 0 && _seen.Count < MaximumMatches) _seen.Add(room); }

    internal MatchFocusResult Success(bool enabled, long room, bool matchRoom, bool choosing, bool reconnecting, IMatchFocusWindow windows)
    {
        if (room <= 0 || !matchRoom || !choosing) return MatchFocusResult.Ignored;
        if (!enabled || reconnecting) { ObserveExisting(room); return MatchFocusResult.Ignored; }
        if (_seen.Contains(room)) return MatchFocusResult.Duplicate;
        if (_seen.Count >= MaximumMatches) return MatchFocusResult.CapacityReached;
        _seen.Add(room); // Consume even if unavailable/refused: one attempt for this match.
        if (windows.GameProcessIsForeground) return MatchFocusResult.AlreadyForeground;
        var window = windows.FindGameWindow();
        if (!windows.IsGameWindow(window)) return MatchFocusResult.WindowUnavailable;
        if (windows.IsMinimized(window))
        {
            if (!windows.IsGameWindow(window)) return MatchFocusResult.WindowUnavailable;
            if (!windows.Restore(window))
            {
                if (windows.GameProcessIsForeground) return MatchFocusResult.AlreadyForeground;
                if (windows.IsGameWindow(window)) windows.Flash(window);
                return MatchFocusResult.RestoreFailed;
            }
        }
        if (!windows.IsGameWindow(window)) return MatchFocusResult.WindowUnavailable;
        if (windows.GameProcessIsForeground) return MatchFocusResult.AlreadyForeground;
        if (windows.Activate(window)) return MatchFocusResult.Activated;
        if (!windows.IsGameWindow(window)) return MatchFocusResult.WindowUnavailable;
        if (windows.GameProcessIsForeground) return MatchFocusResult.AlreadyForeground;
        windows.Flash(window);
        return MatchFocusResult.TaskbarNotified;
    }
}
