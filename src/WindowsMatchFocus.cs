using System.Runtime.InteropServices;
using System.Text;

namespace BetterAstralParty;

// Only the current game's top-level Unity window; a scoped topmost pulse, never a foreground-lock workaround.
internal sealed class WindowsMatchFocus : IMatchFocusWindow
{
    private IntPtr _restoreWindow;
    public bool GameProcessIsForeground => OwnProcess(GetForegroundWindow());
    private static bool OwnProcess(IntPtr window) => window != IntPtr.Zero && IsWindow(window)
        && GetWindowThreadProcessId(window, out var process) != 0 && process == (uint)Environment.ProcessId;

    public bool IsGameWindow(IntPtr window) => IsOwnedGameWindow(window) && IsWindowVisible(window);
    private static bool IsOwnedGameWindow(IntPtr window)
    {
        if (!OwnProcess(window) || GetAncestor(window, 2) != window) return false;
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) != 0 && name.ToString() == "UnityWndClass";
    }
    public IntPtr FindGameWindow()
    {
        var found = IntPtr.Zero;
        var count = 0;
        var complete = EnumWindows((window, _) => { if (IsGameWindow(window)) { found = window; count++; } return true; }, IntPtr.Zero);
        return complete && count == 1 ? found : IntPtr.Zero;
    }
    public bool IsMinimized(IntPtr window) => IsIconic(window);
    public bool Restore(IntPtr window) => IsGameWindow(window) && ShowWindowAsync(window, 9); // SW_RESTORE, no waiting.
    public bool IsForeground(IntPtr window) => IsGameWindow(window) && GetForegroundWindow() == window;
    public bool Activate(IntPtr window)
    {
        if (!RestoreTopmost() || !IsGameWindow(window) || IsMinimized(window)
            || GameProcessIsForeground && !IsForeground(window) || !TryTopmost(window, out var topmost)) return false;
        var pulse = !topmost && CanPulseTopmost(window);
        if (!IsGameWindow(window) || IsMinimized(window) || GameProcessIsForeground && !IsForeground(window)
            || !TryTopmost(window, out topmost)) return false;
        if (topmost || !pulse)
        {
            _ = SetForegroundWindow(window);
            return IsForeground(window); // Preserve existing root/owned-popup topmost settings.
        }
        _restoreWindow = window; // Reserve rollback before a possibly partial native operation.
        var activated = false;
        var restored = false;
        try
        {
            // Synchronous requests keep the promotion and finally-restoration in this call's scope.
            if (SetWindowPos(window, (IntPtr)(-1), 0, 0, 0, 0, 0x213)
                && IsGameWindow(window) && !IsMinimized(window)
                && (!GameProcessIsForeground || IsForeground(window)))
            {
                _ = SetForegroundWindow(window);
                activated = IsForeground(window);
            }
        }
        finally { restored = RestoreTopmost(); }
        return activated && restored;
    }
    private static bool TryTopmost(IntPtr window, out bool topmost)
    {
        Marshal.SetLastPInvokeError(0);
        var style = GetWindowLongW(window, -20); // GWL_EXSTYLE is a 32-bit style, not a pointer.
        topmost = (style & 8) != 0; // WS_EX_TOPMOST
        return style != 0 || Marshal.GetLastPInvokeError() == 0;
    }
    private static bool CanPulseTopmost(IntPtr window)
    {
        if (GetAncestor(window, 3) != window) return false;
        var safe = true;
        var complete = EnumWindows((owned, _) =>
        {
            // NOTOPMOST also demotes owned popups; do not undo their pre-existing native state.
            if (owned != window && GetAncestor(owned, 3) == window
                && (!TryTopmost(owned, out var topmost) || topmost)) safe = false;
            return true;
        }, IntPtr.Zero);
        return complete && safe;
    }
    public bool RestoreTopmost()
    {
        var window = _restoreWindow;
        if (window == IntPtr.Zero) return true;
        // Hidden windows still need restoration; dead or reassigned handles must never be touched.
        if (!IsOwnedGameWindow(window)) { _restoreWindow = IntPtr.Zero; return true; }
        if (!TryTopmost(window, out var topmost)) return false;
        if (topmost)
        {
            _ = SetWindowPos(window, (IntPtr)(-2), 0, 0, 0, 0, 0x213);
            if (!IsOwnedGameWindow(window)) { _restoreWindow = IntPtr.Zero; return true; }
            if (!TryTopmost(window, out topmost) || topmost) return false;
        }
        _restoreWindow = IntPtr.Zero;
        return true;
    }
    public bool Raise(IntPtr window)
    {
        if (!RestoreTopmost() || !IsForeground(window) || IsMinimized(window)) return false;
        // Separate Z-order from activation; HWND_TOP preserves the existing topmost style.
        var accepted = SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x4000 | 0x200 | 0x10 | 0x2 | 0x1);
        return accepted && IsForeground(window) && !IsMinimized(window);
    }
    public void Flash(IntPtr window)
    {
        if (!IsGameWindow(window) || GameProcessIsForeground) return;
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window, Flags = 2, Count = 3 };
        _ = FlashWindowEx(ref info); // Return value is the previous active state, not success.
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo { internal uint Size; internal IntPtr Window; internal uint Flags, Count, Timeout; }
    private delegate bool EnumerateWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
}
