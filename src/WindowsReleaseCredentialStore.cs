using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty;

internal interface IReleaseCredentialStore
{
    string? Load();
    void Save(string token);
    void Delete();
}

// One exact app/repository identity, current Windows user, local machine only.
// Never enumerate credentials, use Git's entries, or write secrets to mod files.
internal sealed class WindowsReleaseCredentialStore : IReleaseCredentialStore
{
    internal static string TargetName => "BetterAstralParty:GitHub:" + ReleaseFeedPolicy.Beta.Identity;
    private const uint Generic = 1, LocalMachine = 2;
    private const int NotFound = 1168;
    private readonly string _target;
    internal WindowsReleaseCredentialStore() { _target = TargetName; }
    // Isolated native regression checks may touch only their own random dummy entry.
    internal WindowsReleaseCredentialStore(string testTarget)
    {
        if (!Regex.IsMatch(testTarget, @"\ABetterAstralParty\.Test:Beta:[a-f0-9]{32}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid credential fixture target");
        _target = testTarget;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        internal uint Flags, Type;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Comment;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        internal uint BlobSize;
        internal IntPtr Blob;
        internal uint Persist, AttributeCount;
        internal IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr buffer);
    private static void Clear(IntPtr buffer, int bytes) { for (var i = 0; i < bytes; i++) Marshal.WriteByte(buffer, i, 0); }
    public string? Load()
    {
        if (!CredReadW(_target, Generic, 0, out var buffer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error);
        }
        var credential = default(Credential);
        try
        {
            credential = Marshal.PtrToStructure<Credential>(buffer);
            if (credential.Type != Generic || credential.Persist != LocalMachine || credential.AttributeCount != 0
                || !string.Equals(credential.TargetName, _target, StringComparison.OrdinalIgnoreCase)
                || credential.UserName != "TSM701" || credential.Blob == IntPtr.Zero
                || credential.BlobSize is 0 or > 512 || credential.BlobSize % 2 != 0)
                throw new InvalidOperationException("Invalid saved update credential");
            var token = Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2));
            if (!MemoryReleaseAuthentication.ValidToken(token)) throw new InvalidOperationException("Invalid saved update credential");
            return token;
        }
        finally
        {
            // Bound clearing even when malformed metadata is rejected; CredFree owns the allocation.
            if (credential.Blob != IntPtr.Zero && credential.BlobSize <= 2560) Clear(credential.Blob, (int)credential.BlobSize);
            CredFree(buffer);
        }
    }
    public void Save(string token)
    {
        if (!MemoryReleaseAuthentication.ValidToken(token)) throw new ArgumentException("Invalid update credential");
        var bytes = Encoding.Unicode.GetBytes(token);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new Credential { Type = Generic, TargetName = _target, UserName = "TSM701",
                Blob = buffer, BlobSize = (uint)bytes.Length, Persist = LocalMachine };
            if (!CredWriteW(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Clear(buffer, bytes.Length); Marshal.FreeHGlobal(buffer); Array.Clear(bytes, 0, bytes.Length); }
    }
    public void Delete()
    {
        if (CredDeleteW(_target, Generic, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != NotFound) throw new Win32Exception(error);
    }
}
