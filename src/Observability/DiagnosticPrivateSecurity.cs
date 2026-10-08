#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace BetterAstralParty.Observability
{
    // Apply only to a newly created staging directory. Never repair/change existing ACLs.
    internal static class DiagnosticPrivateSecurity
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint GetSecurityInfo(SafeFileHandle handle, int type, uint information, out IntPtr owner,
            out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorControl(IntPtr descriptor, out ushort control, out uint revision);
        [StructLayout(LayoutKind.Sequential)]
        private struct AclSize { internal uint Count, Used, Free; }
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetAclInformation(IntPtr acl, out AclSize size, uint length, int kind);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetAce(IntPtr acl, uint index, out IntPtr ace);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr text);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
        private static string CurrentSid()
        {
#if NETFRAMEWORK
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new DiagnosticStorageException(DiagnosticStorageFailure.Unsupported);
#else
            if (!OperatingSystem.IsWindows()) throw new DiagnosticStorageException(DiagnosticStorageFailure.Unsupported);
#endif
            using (var identity = WindowsIdentity.GetCurrent())
                return identity.User?.Value ?? throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState);
        }
        private static string Sid(IntPtr sid)
        {
            IntPtr text;
            if (sid == IntPtr.Zero || !ConvertSidToStringSidW(sid, out text)) throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState);
            try { return Marshal.PtrToStringUni(text) ?? throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState); }
            finally { LocalFree(text); }
        }
        internal sealed class Descriptor : IDisposable
        {
            internal readonly IntPtr Pointer;
            internal Descriptor(bool file = false)
            {
                var sid = CurrentSid(); uint length; IntPtr pointer;
                // Protected, owner=current user; only current user and SYSTEM inherit full access.
                // Files permit data I/O but deny FILE_WRITE_ATTRIBUTES, required by hardlink creation.
                // OWNER_RIGHTS grants READ_CONTROL only, removing the owner's implicit WRITE_DAC.
                var sddl = file ? "O:" + sid + "D:P(A;;FA;;;SY)(A;;0x0012008F;;;" + sid + ")(A;;RC;;;OW)"
                    : "O:" + sid + "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;" + sid + ")";
                if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out pointer, out length))
                    throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState, Marshal.GetLastWin32Error());
                Pointer = pointer;
            }
            public void Dispose() { LocalFree(Pointer); }
        }
        internal static void AssertDirectory(SafeFileHandle handle) { Assert(handle, true); }
        internal static void AssertFile(SafeFileHandle handle) { Assert(handle, false); }
        private static void Assert(SafeFileHandle handle, bool directory)
        {
            IntPtr owner, group, dacl, sacl, descriptor;
            var error = GetSecurityInfo(handle, 1, 1 | 4, out owner, out group, out dacl, out sacl, out descriptor);
            if (error != 0) throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState, (int)error);
            try
            {
                var sid = CurrentSid(); ushort control; uint revision; AclSize size;
                if (Sid(owner) != sid || dacl == IntPtr.Zero || !GetSecurityDescriptorControl(descriptor, out control, out revision)
                    || (control & 0x1000) == 0 || !GetAclInformation(dacl, out size, 12, 2) || size.Count != (directory ? 2 : 3))
                    throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState);
                var user = false; var system = false; var ownerRights = false;
                for (uint i = 0; i < size.Count; i++)
                {
                    IntPtr ace;
                    if (!GetAce(dacl, i, out ace) || Marshal.ReadByte(ace, 0) != 0 || Marshal.ReadByte(ace, 1) != (directory ? 3 : 0)
                        || (ushort)Marshal.ReadInt16(ace, 2) < 16) throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState);
                    var mask = (uint)Marshal.ReadInt32(ace, 4); var trustee = Sid(IntPtr.Add(ace, 8));
                    if (trustee == sid && !user && mask == (directory ? 0x001f01ffU : 0x0012008fU)) user = true;
                    else if (trustee == "S-1-5-18" && !system && mask == 0x001f01ffU) system = true;
                    else if (!directory && trustee == "S-1-3-4" && !ownerRights && mask == 0x00020000U) ownerRights = true;
                    else throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState);
                }
                if (!user || !system || !directory && !ownerRights) throw new DiagnosticStorageException(DiagnosticStorageFailure.InvalidState);
            }
            finally { LocalFree(descriptor); }
        }
    }
}
