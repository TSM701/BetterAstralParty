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
#if BAP_PROCESS_QUERY_FIXTURE
        internal static Func<SafeWaitHandle, string>? FixtureImageQuery = null;
        internal static Func<SafeWaitHandle, uint>? FixtureQueryFailureWait = null;
#endif
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
                get { return WaitForExit(0); }
            }
            internal bool WaitForExit(uint milliseconds)
            {
                if (milliseconds > 5000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
                var wait = WaitForSingleObject(_handle, milliseconds);
                if (wait == 0) return true;
                if (wait != 258) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, wait == uint.MaxValue ? Marshal.GetLastWin32Error() : 0);
                long creation, exit, kernel, user;
                if (!GetProcessTimes(_handle, out creation, out exit, out kernel, out user))
                    throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
                if (creation != Creation)
                    throw new ApplySafetyException(ApplyFailure.ProcessUncertain);
                return false;
            }
            public void Dispose() { _handle.Dispose(); }
        }
        internal static Captured Observe(uint pid, bool allowExited = false, uint metadataExitWaitMilliseconds = 0)
        {
            if (metadataExitWaitMilliseconds > 2000 || !allowExited && metadataExitWaitMilliseconds != 0) throw new ArgumentOutOfRangeException(nameof(metadataExitWaitMilliseconds));
            var handle = OpenProcess(0x00100000 | 0x1000, false, pid); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
            if (handle.IsInvalid)
            { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new ApplySafetyException(ApplyFailure.ProcessUncertain, error); }
            try
            {
                if (allowExited && WaitForSingleObject(handle, 0) == 0) return new Captured(handle, pid, 0, 0, "");
                long creation, exit, kernel, user; uint session;
                if (!GetProcessTimes(handle, out creation, out exit, out kernel, out user) || !ProcessIdToSessionId(pid, out session))
                    throw new ApplySafetyException(ApplyFailure.ProcessUncertain, Marshal.GetLastWin32Error());
#if BAP_PROCESS_QUERY_FIXTURE
                var image = FixtureImageQuery == null ? ImagePath(handle) : FixtureImageQuery(handle);
#else
                var image = ImagePath(handle);
#endif
                return new Captured(handle, pid, session, creation, image);
            }
            catch (Exception error) {
                // Only the held process object can prove an exit during metadata reads.
                var wait = error is ApplySafetyException safety && safety.NativeError == 5 ? metadataExitWaitMilliseconds : 0;
                if (allowExited && WaitForSingleObject(handle, wait) == 0) return new Captured(handle, pid, 0, 0, "");
                handle.Dispose(); throw;
            }
        }
        internal static Captured Capture(uint pid, string expectedImage, long? expectedCreation = null, uint? expectedSession = null)
        {
            var captured = Observe(pid);
            try {
                if (!string.Equals(captured.Image, WindowsPath.Absolute(expectedImage), StringComparison.OrdinalIgnoreCase)
                    || expectedCreation.HasValue && captured.Creation != expectedCreation.Value || expectedSession.HasValue && captured.Session != expectedSession.Value)
                    throw new ApplySafetyException(ApplyFailure.ProcessUncertain);
                return captured;
            } catch { captured.Dispose(); throw; }
        }
        internal static void AssertNoRunning(IEnumerable<string> exactImages) { AssertNoRunning(exactImages, 0); }
        // In-process apply: the calling game process is the only instance allowed to run.
        internal static void AssertNoOtherRunning(IEnumerable<string> exactImages) { AssertNoRunning(exactImages, GetCurrentProcessId()); }
        private static void AssertNoRunning(IEnumerable<string> exactImages, uint allowed)
        {
            var images = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var image in exactImages)
            {
                var full = WindowsPath.Absolute(image); var name = WindowsPath.Leaf(full);
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
                    if (entry.Pid != allowed && images.TryGetValue(entry.Name, out expected!))
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
                                var full = ImagePathOrExited(process);
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
        private static string? ImagePathOrExited(SafeWaitHandle process)
        {
            try {
#if BAP_PROCESS_QUERY_FIXTURE
                if (FixtureImageQuery != null) return FixtureImageQuery(process);
#endif
                return ImagePath(process);
            } catch (ApplySafetyException) {
                // Like Observe(allowExited), only this already-held process object can prove exit.
#if BAP_PROCESS_QUERY_FIXTURE
                var wait = FixtureQueryFailureWait == null ? WaitForSingleObject(process, 0) : FixtureQueryFailureWait(process);
#else
                var wait = WaitForSingleObject(process, 0);
#endif
                if (wait == 0) return null;
                throw;
            }
        }
        private static string ImagePath(SafeWaitHandle process)
        {
            for (var capacity = 512; capacity <= WindowsPath.MaximumLength + 5; capacity = Math.Min(capacity * 2, WindowsPath.MaximumLength + 5)) {
                var name = new StringBuilder(capacity); var size = (uint)capacity;
                if (QueryFullProcessImageNameW(process, 0, name, ref size)) return WindowsPath.FromIo(name.ToString());
                var error = Marshal.GetLastWin32Error();
                if (error != 122 || capacity == WindowsPath.MaximumLength + 5) throw new ApplySafetyException(ApplyFailure.ProcessUncertain, error);
            }
            throw new ApplySafetyException(ApplyFailure.ProcessUncertain);
        }
        internal sealed class LaunchLease : IDisposable
        {
            private readonly List<WindowsFileFence.FileLease> _files = new List<WindowsFileFence.FileLease>();
            private readonly List<string> _absent = new List<string>();
            private readonly WindowsFileFence _fence;
            private readonly string[] _images;
            internal LaunchLease(WindowsFileFence fence, IEnumerable<string> relativeImages, bool allowMissing = false)
            {
                _fence = fence;
                var images = new List<string>();
                try
                {
                    var relatives = new List<string>(relativeImages);
                    if (relatives.Count == 0 || relatives.Count > 4 || new HashSet<string>(relatives, StringComparer.OrdinalIgnoreCase).Count != relatives.Count)
                        throw new ApplySafetyException(ApplyFailure.InvalidState);
                    foreach (var relative in relatives) images.Add(fence.Full(relative));
                    _images = images.ToArray(); AssertNoRunning(_images);
                    foreach (var relative in relatives) {
                        try { _files.Add(fence.OpenExecutionBarrier(relative)); }
                        catch (ApplySafetyException error) when (allowMissing && (error.NativeError == 2 || error.NativeError == 3)) { fence.Recheck(); _absent.Add(relative); }
                    }
                    AssertNoRunning(_images); Recheck(); // Close the scan/open race after all executable leases.
                }
                catch { Dispose(); throw; }
            }
            internal void Recheck()
            {
                _fence.Recheck();
                foreach (var file in _files) file.Recheck();
                // Removal rechecks a missing original image; never create a placeholder game executable.
                foreach (var relative in _absent) {
                    try { using (var appeared = _fence.OpenExecutionBarrier(relative)) throw new ApplySafetyException(ApplyFailure.IdentityChanged); }
                    catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { }
                }
                AssertNoRunning(_images);
            }
            // Borrowed from this guard; only the guard owns disposal.
            internal WindowsFileFence.FileLease BorrowImage(string fullPath)
            {
                foreach (var file in _files)
                    if (string.Equals(file.Path, fullPath, StringComparison.OrdinalIgnoreCase)) { file.Recheck(); return file; }
                throw new ApplySafetyException(ApplyFailure.InvalidState);
            }
            internal string ImageHash(string fullPath) { return BorrowImage(fullPath).Hash(); }
            public void Dispose() { foreach (var file in _files) file.Dispose(); _files.Clear(); }
        }
        internal static LaunchLease Acquire(WindowsFileFence fence, IEnumerable<string> relativeImages, bool allowMissing = false)
        { return new LaunchLease(fence, relativeImages, allowMissing); }
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
