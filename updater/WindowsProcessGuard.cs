#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BetterAstralParty.Updating
{
    // Queries/waits only. No terminate, shell, network or gameplay operations.
    internal static class WindowsProcessGuard
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            internal uint Size, Usage, Pid;
            internal UIntPtr DefaultHeap;
            internal uint ModuleId, Threads, ParentPid;
            internal int BasePriority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeWaitHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(SafeWaitHandle process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageNameW(SafeWaitHandle process, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeWaitHandle CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32FirstW(SafeWaitHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32NextW(SafeWaitHandle snapshot, ref ProcessEntry entry);
        internal sealed class Captured : IDisposable
        {
            private readonly SafeWaitHandle _handle;
            internal readonly uint Pid, Session;
            internal readonly long Creation;
            internal readonly string Image;
            internal Captured(SafeWaitHandle handle, uint pid, uint session, long creation, string image)
            { _handle = handle; Pid = pid; Session = session; Creation = creation; Image = image; }
            internal bool Exited
            {
                get
                {
                    var wait = WaitForSingleObject(_handle, 0);
                    if (wait == 0) return true;
                    if (wait != 258) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                    long creation, exit, kernel, user;
                    if (!GetProcessTimes(_handle, out creation, out exit, out kernel, out user) || creation != Creation)
                        throw new ApplySafetyException(ApplyFailure.ProcessUncertain);
                    return false;
                }
            }
            public void Dispose() { _handle.Dispose(); }
        }
        internal static Captured Capture(uint pid, string expectedImage, long? expectedCreation = null, uint? expectedSession = null)
        {
            var handle = OpenProcess(0x00100000 | 0x1000, false, pid); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
            if (handle.IsInvalid)
            { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new ApplySafetyException(ApplyFailure.ProcessUncertain, error); }
            try
            {
                long creation, exit, kernel, user; uint session;
                if (!GetProcessTimes(handle, out creation, out exit, out kernel, out user) || !ProcessIdToSessionId(pid, out session))
                    throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                var name = new StringBuilder(512); var size = (uint)name.Capacity;
                if (!QueryFullProcessImageNameW(handle, 0, name, ref size)) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                var image = Path.GetFullPath(name.ToString());
                if (!string.Equals(image, Path.GetFullPath(expectedImage), StringComparison.OrdinalIgnoreCase)
                    || expectedCreation.HasValue && creation != expectedCreation.Value || expectedSession.HasValue && session != expectedSession.Value)
                    throw new ApplySafetyException(ApplyFailure.ProcessUncertain);
                return new Captured(handle, pid, session, creation, image);
            }
            catch { handle.Dispose(); throw; }
        }
        internal static void AssertNoRunning(IEnumerable<string> exactImages)
        {
            var images = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var image in exactImages)
            {
                var full = Path.GetFullPath(image); var name = Path.GetFileName(full);
                List<string> paths;
                if (!images.TryGetValue(name, out paths!)) images.Add(name, paths = new List<string>());
                paths.Add(full);
            }
            using (var snapshot = CreateToolhelp32Snapshot(2, 0))
            {
                if (snapshot.IsInvalid) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)), Name = "" };
                var more = Process32FirstW(snapshot, ref entry);
                if (!more && Marshal.GetLastWin32Error() != 18) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                while (more)
                {
                    List<string> expected;
                    if (images.TryGetValue(entry.Name, out expected!))
                    {
                        // Resolve matching names only. An unresolved matching process is unsafe.
                        using (var process = OpenProcess(0x00100000 | 0x1000, false, entry.Pid))
                        {
                            if (process.IsInvalid)
                            {
                                var error = Marshal.GetLastWin32Error();
                                if (error != 87) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, error);
                            }
                            else if (WaitForSingleObject(process, 0) == 258)
                            {
                                var name = new StringBuilder(512); var size = (uint)name.Capacity;
                                if (!QueryFullProcessImageNameW(process, 0, name, ref size))
                                    throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                                var full = Path.GetFullPath(name.ToString());
                                foreach (var path in expected) if (string.Equals(full, path, StringComparison.OrdinalIgnoreCase))
                                    throw new ApplySafetyException(ApplyFailure.ProcessBusy);
                            }
                            else
                            {
                                var wait = WaitForSingleObject(process, 0);
                                if (wait != 0) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                            }
                        }
                    }
                    more = Process32NextW(snapshot, ref entry);
                }
                if (Marshal.GetLastWin32Error() != 18) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
            }
        }
        internal sealed class LaunchLease : IDisposable
        {
            private readonly List<WindowsFileFence.FileLease> _files = new List<WindowsFileFence.FileLease>();
            private readonly string[] _images;
            internal LaunchLease(WindowsFileFence fence, IEnumerable<string> relativeImages)
            {
                var images = new List<string>();
                try
                {
                    var relatives = new List<string>(relativeImages);
                    if (relatives.Count == 0 || relatives.Count > 4 || new HashSet<string>(relatives, StringComparer.OrdinalIgnoreCase).Count != relatives.Count)
                        throw new ApplySafetyException(ApplyFailure.InvalidState);
                    foreach (var relative in relatives) images.Add(fence.Full(relative));
                    _images = images.ToArray(); AssertNoRunning(_images);
                    foreach (var relative in relatives) _files.Add(fence.OpenFile(relative));
                    AssertNoRunning(_images); Recheck(); // Close the scan/open race after all executable leases.
                }
                catch { Dispose(); throw; }
            }
            internal void Recheck()
            {
                foreach (var file in _files) file.Recheck();
                AssertNoRunning(_images);
            }
            internal string ImageHash(string fullPath)
            {
                foreach (var file in _files)
                    if (string.Equals(file.Path, fullPath, StringComparison.OrdinalIgnoreCase)) return file.Hash();
                throw new ApplySafetyException(ApplyFailure.InvalidState);
            }
            public void Dispose() { foreach (var file in _files) file.Dispose(); _files.Clear(); }
        }
        internal static LaunchLease Acquire(WindowsFileFence fence, IEnumerable<string> relativeImages)
        { return new LaunchLease(fence, relativeImages); }
        internal static uint CurrentSession
        {
            get
            {
                uint session;
                if (!ProcessIdToSessionId(GetCurrentProcessId(), out session)) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                return session;
            }
        }
    }
}
