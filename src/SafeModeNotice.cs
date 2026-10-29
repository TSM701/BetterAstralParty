using System.Runtime.InteropServices;
namespace BetterAstralParty;

// Safe mode has no game UI, and Windows Do Not Disturb drops notification-area balloons over a fullscreen game
// (observed 2026-10-29), so safe mode speaks through message boxes, and only when the player has to act.
// The mod language is unknown before the home menu, so each message carries Korean and English, like the safe-mode log lines.
internal static class SafeModeNotice
{
    // Asked after a failed start, before the game shows its first screen: Yes starts safe mode, No tries normally again.
    internal static string Choice(bool previousSafe) => (previousSafe
            ? "지난번에는 안전 모드로 실행했습니다.\r\n이번에도 안전 모드로 실행할까요?"
            : "지난번 실행에서 BetterAstralParty가 홈 화면까지 가지 못했습니다.\r\n안전 모드로 실행할까요?")
        + "\r\n\r\n예: 모드 기능을 끄고 업데이트만 확인합니다.\r\n아니요: 평소처럼 다시 시도합니다.\r\n\r\n"
        + (previousSafe ? "The last start ran in safe mode.\r\nStart in safe mode again?" : "BetterAstralParty did not reach the home screen last time.\r\nStart in safe mode?")
        + "\r\n\r\nYes: mod features stay off and only updates run.\r\nNo: try again as usual.";

    // true: safe mode, false: another normal try, null: no answer. The game waits while the question is open.
    // BepInEx loads plugins when the first scene activates, so the (often fullscreen) game window may already exist.
    // An owned window always stays above its owner, but a hidden or minimized owner hides it too; then it stands alone.
    internal static bool? Ask(bool previousSafe, IntPtr window)
    {
        var owner = window != IntPtr.Zero && IsWindowVisible(window) && !IsIconic(window) ? window : IntPtr.Zero;
        StartupGuard.Stage("SafeModeQuestion", owner != IntPtr.Zero ? "OverGame" : window != IntPtr.Zero ? "GameHidden" : "NoGameWindow");
        // MB_YESNO | MB_ICONWARNING | MB_SETFOREGROUND | MB_TOPMOST; IDYES = 6, IDNO = 7.
        return MessageBoxW(owner, Choice(previousSafe), "BetterAstralParty", 0x4 | 0x30 | 0x10000 | 0x40000) switch { 6 => true, 7 => false, _ => null };
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);

    // The box for the current update state, keyed so each one shows once; null says nothing new. Safe mode itself, a
    // download and its session-only stop stay silent: the player chose safe mode, and the next start downloads again.
    // automatic: a found update can download and apply by itself (no latch, both automatic settings ON).
    internal static (string Key, string Text)? Message(AutomaticUpdateStatus status, string target, bool available, bool automatic)
    {
        if (target.Length == 0) return null;
        if (status == AutomaticUpdateStatus.Committed)
            return ("committed:" + target, $"안전 모드에서 {target} 업데이트를 적용했습니다.\r\n게임을 다시 시작하면 새 버전으로 실행됩니다.\r\n\r\n"
                + $"Safe mode applied the {target} update.\r\nRestart the game to use it.");
        if (available && !automatic)
            return ("blocked:" + target, $"{target} 업데이트가 있지만 안전 모드에서 자동으로 적용할 수 없습니다.\r\n다운로드 페이지에서 설치 ZIP을 받아 Install.cmd로 설치하세요.\r\n\r\n"
                + $"The {target} update is available, but safe mode cannot apply it automatically.\r\nDownload the installation ZIP and run Install.cmd.");
        return null;
    }

    // Its own thread keeps updates running while the box is open. No window owns it, so the game stays usable,
    // and topmost keeps it above the fullscreen game. MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND | MB_TOPMOST.
    internal static void Tell(string text) => new Thread(() => { try { MessageBoxW(IntPtr.Zero, text, "BetterAstralParty", 0x40 | 0x10000 | 0x40000); } catch { } })
        { IsBackground = true, Name = "BAP safe-mode notice" }.Start();
}
