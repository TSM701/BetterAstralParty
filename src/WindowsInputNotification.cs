using System.Runtime.InteropServices;

namespace BetterAstralParty;

// Native notification-area balloon; no shortcut/registry registration or game window hooks.
internal static class WindowsInputNotification
{
    private static NotifyIconData _data;
    private static bool _added;
    private static readonly Guid Identity = Guid.NewGuid();

    static WindowsInputNotification()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Hide(); } catch { /* process is exiting */ } };
    }

    internal static void Show(IntPtr window, string message)
    {
        Hide();
        if (_added) throw new InvalidOperationException("Previous input notification could not be removed.");
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window,
            Flags = 0x20 | 0x2 | 0x4, // NIF_GUID | NIF_ICON | NIF_TIP
            Identity = Identity,
            Icon = LoadIcon(IntPtr.Zero, (IntPtr)32512), // shared IDI_APPLICATION; do not destroy
            Tip = "BetterAstralParty", Info = "", Title = ""
        };
        if (!Shell_NotifyIcon(0, ref _data)) throw new InvalidOperationException("Input notification icon could not be added.");
        _added = true;
        _data.Flags = 0x20 | 0x10 | 0x40; // GUID | INFO | REALTIME: never queue stale input waits
        _data.Info = message;
        _data.Title = "Astral Party · BetterAstralParty";
        _data.InfoFlags = 0x1 | 0x10 | 0x80; // INFO | NOSOUND | RESPECT_QUIET_TIME
        if (!Shell_NotifyIcon(1, ref _data))
        {
            Hide();
            throw new InvalidOperationException("Input notification could not be sent.");
        }
    }

    internal static void Hide()
    {
        if (!_added) return;
        // Retain ownership on failure so a later tick can retry removal.
        if (Shell_NotifyIcon(2, ref _data)) _added = false;
    }

    // NOTIFYICONDATAW, current x64 ABI (976 bytes). Unicode buffers include their terminators.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Identity;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint action, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
}
