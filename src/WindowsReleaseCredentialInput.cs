using System.Runtime.InteropServices;
using System.Text;
using BetterAstralParty.Updating;

namespace BetterAstralParty;

// Masked-input adapter. The coordinator saves accepted input in its scoped Windows vault entry.
// Requires security approval AND reviewed production trust before even displaying the dialog.
internal sealed class WindowsReleaseCredentialInput : IReleaseCredentialInput
{
    internal const uint Flags = 1; // CREDUIWIN_GENERIC; never CREDUIWIN_CHECKBOX or persistence flags.
    private static int _promptActive;
    private static int _promptRevision;
    internal static bool PromptInProgress => Volatile.Read(ref _promptActive) != 0;
    internal static int PromptRevision => Volatile.Read(ref _promptRevision);
    internal static bool TryEnterPrompt()
    {
        if (Interlocked.CompareExchange(ref _promptActive, 1, 0) != 0) return false;
        Interlocked.Increment(ref _promptRevision); return true;
    }
    internal static void LeavePrompt()
    {
        if (Interlocked.Exchange(ref _promptActive, 0) != 0) Interlocked.Increment(ref _promptRevision);
    }
    internal static bool ValidatePrompt(uint status, bool save, IntPtr output, uint outputBytes)
    {
        if (status == 1223) return false; // ERROR_CANCELLED only.
        if (status != 0 || save || output == IntPtr.Zero || outputBytes == 0 || outputBytes > 65536)
            throw new InvalidOperationException("Private native input failed");
        return true;
    }
    internal static void ValidateUnpacked(bool success, string user, string domain)
    {
        if (!success) throw new InvalidOperationException("Private native input failed");
        if (domain.Length != 0 || !string.Equals(user, "TSM701", StringComparison.OrdinalIgnoreCase)) throw new ReleaseInputRejectedException();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Info
    {
        internal int Size;
        internal IntPtr Parent;
        internal string Message, Caption;
        internal IntPtr Banner;
    }
    [DllImport("credui.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CredUIPromptForWindowsCredentialsW(ref Info info, uint error, ref uint package,
        IntPtr input, uint inputBytes, out IntPtr output, out uint outputBytes, [MarshalAs(UnmanagedType.Bool)] ref bool save, uint flags);
    [DllImport("credui.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredUnPackAuthenticationBufferW(uint flags, IntPtr buffer, uint bytes,
        StringBuilder user, ref uint userChars, StringBuilder domain, ref uint domainChars, StringBuilder password, ref uint passwordChars);
    public Task<ReleaseCredential?> Read(bool korean, CancellationToken cancellation) => Task.Run(() => Prompt(korean, cancellation));
    private static ReleaseCredential? Prompt(bool korean, CancellationToken cancellation)
    {
        if (!PrivateUpdateApproval.CredentialInputEnabled || !UpdateTrust.Production().Configured || !OperatingSystem.IsWindows()) return null;
        cancellation.ThrowIfCancellationRequested();
        var info = new Info { Size = Marshal.SizeOf<Info>(), Caption = "BetterAstralParty / " + ReleaseFeedPolicy.BetaRepository,
            Message = korean ? "사용자 이름: TSM701\n암호: 이 저장소의 읽기 전용 PAT\nWindows 자격 증명 관리자에 저장하고 로그아웃하면 삭제합니다. GitHub 암호를 입력하지 마세요."
                : "User name: TSM701\nPassword: this repository's read-only PAT\nSaved in Windows Credential Manager until Sign Out. Do not enter your GitHub password." };
        uint package = 0, outputBytes = 0; var save = false; var output = IntPtr.Zero;
        var user = new StringBuilder(257); var domain = new StringBuilder(257); var password = new StringBuilder(257);
        if (!TryEnterPrompt()) return null;
        try
        {
            var status = CredUIPromptForWindowsCredentialsW(ref info, 0, ref package, IntPtr.Zero, 0, out output, out outputBytes, ref save, Flags);
            if (!ValidatePrompt(status, save, output, outputBytes)) return null;
            cancellation.ThrowIfCancellationRequested();
            uint userChars = 257, domainChars = 257, passwordChars = 257;
            var unpacked = CredUnPackAuthenticationBufferW(0, output, outputBytes, user, ref userChars, domain, ref domainChars, password, ref passwordChars);
            ValidateUnpacked(unpacked, user.ToString(), domain.ToString());
            cancellation.ThrowIfCancellationRequested();
            return new ReleaseCredential(password.ToString());
        }
        finally
        {
            // Clear accessible native/managed buffers; immutable managed token copies cannot be guaranteed erased.
            for (var i = 0; i < password.Length; i++) password[i] = '\0'; password.Clear(); user.Clear(); domain.Clear();
            if (output != IntPtr.Zero)
            {
                try { for (uint i = 0; i < outputBytes; i++) Marshal.WriteByte(output, checked((int)i), 0); }
                finally { Marshal.FreeCoTaskMem(output); }
            }
            LeavePrompt();
        }
    }
}
