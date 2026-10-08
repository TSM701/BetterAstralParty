using System.Runtime.InteropServices;
using System.Text;

namespace BetterAstralParty;

// Inactive scaffold: no production caller/event registration/settings integration yet.
internal sealed class WindowsMatchFocus : IMatchFocusWindow
{
    public bool GameProcessIsForeground => OwnProcess(GetForegroundWindow());
    private static bool OwnProcess(IntPtr window) => window != IntPtr.Zero && IsWindow(window)
        && GetWindowThreadProcessId(window, out var process) != 0 && process == (uint)Environment.ProcessId;

    public bool IsGameWindow(IntPtr window)
    {
        if (!OwnProcess(window) || !IsWindowVisible(window) || GetAncestor(window, 2) != window) return false;
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
    public bool Activate(IntPtr window) => IsGameWindow(window) && SetForegroundWindow(window) && GetForegroundWindow() == window;
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
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
}
