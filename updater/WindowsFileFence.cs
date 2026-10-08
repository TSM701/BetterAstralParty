#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace BetterAstralParty.Updating
{
    internal enum ApplyFailure { UnsafePath, IdentityChanged, LinkedFile, FileBusy, ProcessBusy, ProcessUncertain, InvalidState, RecoveryRequired, Unsupported, CompatibilityRequired, InstallationRequired }
    internal sealed class ApplySafetyException : Exception
    {
        internal readonly ApplyFailure Failure;
        internal readonly int NativeError;
        internal ApplySafetyException(ApplyFailure failure, int error = 0) : base("Update safety: " + failure)
        { Failure = failure; NativeError = error; }
    }
    internal sealed class WindowsFileIdentity
    {
        internal readonly uint Volume;
        internal readonly ulong FileId;
        internal WindowsFileIdentity(uint volume, ulong id) { Volume = volume; FileId = id; }
        internal bool Same(WindowsFileIdentity other) { return Volume == other.Volume && FileId == other.FileId; }
        internal string Text { get { return Volume.ToString("X8", CultureInfo.InvariantCulture) + ":" + FileId.ToString("X16", CultureInfo.InvariantCulture); } }
    }
    // Local NTFS paths only. Keep every ancestor open without write/delete sharing.
    // Caller separately enforces the exact owned-file allowlist before applying a payload.
    internal sealed class WindowsFileFence : IDisposable
    {
        internal const uint Read = 0x80000000, Write = 0x40000000, Delete = 0x00010000;
        private const uint OpenReparse = 0x00200000, BackupSemantics = 0x02000000, WriteThrough = 0x80000000;
        [StructLayout(LayoutKind.Sequential)]
        private struct Info
        {
            internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out uint information, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder name, uint size, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformationByHandleW(SafeFileHandle handle, StringBuilder? volumeName, uint volumeSize,
            out uint serial, out uint maxName, out uint flags, StringBuilder filesystem, uint filesystemSize);
        [StructLayout(LayoutKind.Sequential)]
        private struct IoStatus { internal IntPtr Status; internal UIntPtr Information; }
        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeName { internal ushort Length, MaximumLength; internal IntPtr Buffer; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        { internal uint Length; internal IntPtr RootDirectory, ObjectName; internal uint Attributes; internal IntPtr SecurityDescriptor, SecurityQuality; }
        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(out IntPtr handle, uint access, ref ObjectAttributes attributes, out IoStatus status,
            IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatus status, IntPtr data, uint size, int kind);
        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);
        private sealed class Pin : IDisposable
        {
            internal readonly string Path;
            internal readonly SafeFileHandle Handle;
            internal readonly WindowsFileIdentity Identity;
            internal Pin(string path, SafeFileHandle handle, WindowsFileIdentity identity) { Path = path; Handle = handle; Identity = identity; }
            public void Dispose() { Handle.Dispose(); }
        }
        private readonly Dictionary<string, Pin> _pins = new Dictionary<string, Pin>(StringComparer.OrdinalIgnoreCase);
        internal readonly string Root;
        internal readonly WindowsFileIdentity RootIdentity;
        private readonly Action<string>? _beforeOperation;
        private bool _disposed;
        internal WindowsFileFence(string root, Action<string>? beforeOperation = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8) throw new ApplySafetyException(ApplyFailure.Unsupported);
            Root = Absolute(root);
            _beforeOperation = beforeOperation;
            try
            {
                PinPath(Root); RootIdentity = _pins[Root].Identity;
                var format = new StringBuilder(32);
                uint serial, maxName, flags;
                if (!GetVolumeInformationByHandleW(_pins[Root].Handle, null, 0, out serial, out maxName, out flags, format, (uint)format.Capacity)
                    || format.ToString() != "NTFS") throw new ApplySafetyException(ApplyFailure.Unsupported);
            }
            catch { Dispose(); throw; }
        }
        private static void Component(string component)
        {
            if (component.Length == 0 || component.Length > 180 || component == "." || component == ".."
                || component.EndsWith(".", StringComparison.Ordinal) || component.EndsWith(" ", StringComparison.Ordinal)
                || component.IndexOfAny(new[] { ':', '*', '?', '<', '>', '|', '"', '/', '\\' }) >= 0)
                throw new ApplySafetyException(ApplyFailure.UnsafePath);
            foreach (var c in component) if (c < 32) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            if (Regex.IsMatch(component, @"\A(CONIN\$|CONOUT\$|CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new ApplySafetyException(ApplyFailure.UnsafePath);
        }
        private static string Absolute(string path)
        {
            if (path == null || path.Length > 240 || !Regex.IsMatch(path, @"\A[A-Za-z]:\\") || path.IndexOf(':', 2) >= 0)
                throw new ApplySafetyException(ApplyFailure.UnsafePath);
            var trimmed = path.Length == 3 ? path : path.TrimEnd('\\');
            if (!string.Equals(Path.GetFullPath(trimmed), trimmed, StringComparison.OrdinalIgnoreCase)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            if (trimmed.Length > 3) foreach (var part in trimmed.Substring(3).Split('\\')) Component(part);
            return trimmed;
        }
        internal string Full(string relative)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WindowsFileFence));
            if (relative == null || relative.Length == 0 || relative.IndexOf('\\') >= 0) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            foreach (var part in relative.Split('/')) Component(part);
            return Absolute(Root.TrimEnd('\\') + "\\" + relative.Replace('/', '\\'));
        }
        private static SafeFileHandle Open(string path, uint access, uint share, uint disposition, uint flags)
        {
            var handle = CreateFileW(path, access, share, IntPtr.Zero, disposition, flags | OpenReparse, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error(); handle.Dispose();
                throw new ApplySafetyException(error == 32 || error == 33 || error == 5 ? ApplyFailure.FileBusy : ApplyFailure.UnsafePath, error);
            }
            return handle;
        }
        private static SafeFileHandle OpenRelative(Pin parent, string leaf, uint access, uint share, bool create, bool directory, bool writeThrough,
            IntPtr securityDescriptor = default(IntPtr))
        {
            Component(leaf);
            var characters = Marshal.StringToHGlobalUni(leaf);
            var namePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeName))); var referenced = false;
            try
            {
                parent.Handle.DangerousAddRef(ref referenced);
                var name = new UnicodeName { Length = (ushort)(leaf.Length * 2), MaximumLength = (ushort)((leaf.Length + 1) * 2), Buffer = characters };
                Marshal.StructureToPtr(name, namePointer, false);
                var attributes = new ObjectAttributes { Length = (uint)Marshal.SizeOf(typeof(ObjectAttributes)), RootDirectory = parent.Handle.DangerousGetHandle(),
                    ObjectName = namePointer, Attributes = 0x40 | 0x1000, SecurityDescriptor = securityDescriptor }; // CASE_INSENSITIVE | DONT_REPARSE; no inherited handle.
                IntPtr raw; IoStatus io;
                var options = 0x20U | 0x00200000U | (directory ? 1U : 0x40U) | (writeThrough ? 2U : 0U);
                var status = NtCreateFile(out raw, access | 0x00100000, ref attributes, out io, IntPtr.Zero, 0x80, share,
                    create ? 2U : 1U, options, IntPtr.Zero, 0); // FILE_CREATE / FILE_OPEN; never supersede/overwrite.
                if (status != 0)
                {
                    if (raw != IntPtr.Zero && raw != new IntPtr(-1)) new SafeFileHandle(raw, true).Dispose();
                    var error = (int)RtlNtStatusToDosError(status);
                    throw new ApplySafetyException(error == 4395 || status == unchecked((int)0xc000050b) ? ApplyFailure.LinkedFile
                        : error == 32 || error == 33 || error == 5 ? ApplyFailure.FileBusy : ApplyFailure.UnsafePath, error);
                }
                return new SafeFileHandle(raw, true);
            }
            finally
            {
                if (referenced) parent.Handle.DangerousRelease();
                Marshal.FreeHGlobal(namePointer); Marshal.FreeHGlobal(characters);
            }
        }
        private static Info ReadInfo(SafeFileHandle handle, bool directory)
        {
            Info info;
            if (!GetFileInformationByHandle(handle, out info)) throw new ApplySafetyException(ApplyFailure.IdentityChanged, Marshal.GetLastWin32Error());
            if ((info.Attributes & 0x400) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
            if (((info.Attributes & 0x10) != 0) != directory) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            if (!directory && info.Links != 1) throw new ApplySafetyException(ApplyFailure.LinkedFile);
            if (directory)
            {
                uint caseFlags;
                if (!GetFileInformationByHandleEx(handle, 23, out caseFlags, 4)) // FileCaseSensitiveInfo
                    throw new ApplySafetyException(ApplyFailure.Unsupported, Marshal.GetLastWin32Error());
                if (caseFlags != 0) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            }
            return info;
        }
        private static WindowsFileIdentity Identity(Info info) { return new WindowsFileIdentity(info.Volume, (ulong)info.IndexHigh << 32 | info.IndexLow); }
        private static string Final(SafeFileHandle handle)
        {
            var name = new StringBuilder(512); var length = GetFinalPathNameByHandleW(handle, name, (uint)name.Capacity, 0);
            if (length == 0 || length >= name.Capacity) throw new ApplySafetyException(ApplyFailure.IdentityChanged, Marshal.GetLastWin32Error());
            var path = name.ToString();
            if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return Absolute(path.Substring(4));
        }
        private void PinPath(string path)
        {
            var drive = path.Substring(0, 3); AddPin(drive);
            var current = drive;
            if (path.Length <= 3) return;
            foreach (var component in path.Substring(3).Split('\\')) { current = Path.Combine(current, component); AddPin(current); }
        }
        private void AddPin(string path)
        {
            if (_pins.ContainsKey(path)) return;
            var handle = path.Length == 3 ? Open(path, 0x80 | 0x20000, 1, 3, BackupSemantics)
                : OpenRelative(_pins[Path.GetDirectoryName(path)!], Path.GetFileName(path), 0x80 | 0x20000, 1, false, true, false);
            try
            {
                var info = ReadInfo(handle, true);
                if (!string.Equals(Final(handle), path, StringComparison.OrdinalIgnoreCase)) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                _pins.Add(path, new Pin(path, handle, Identity(info)));
            }
            catch { handle.Dispose(); throw; }
        }
        internal void Recheck()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WindowsFileFence));
            foreach (var pin in _pins.Values)
                if (!pin.Identity.Same(Identity(ReadInfo(pin.Handle, true))) || !string.Equals(Final(pin.Handle), pin.Path, StringComparison.OrdinalIgnoreCase))
                    throw new ApplySafetyException(ApplyFailure.IdentityChanged);
        }
        internal void EnsureDirectory(string relative)
        {
            Full(relative); Recheck();
            // Parents are pinned first; existing final directories are pinned without following reparse points.
            var parts = relative.Split('/'); var current = Root;
            foreach (var part in parts)
            {
                current = Path.Combine(current, part);
                try { AddPin(current); }
                catch (ApplySafetyException error)
                {
                    // OPEN_REPARSE_POINT recognizes existing/broken links before any creation.
                    if (error.NativeError != 2 && error.NativeError != 3) throw;
                    var parent = _pins[Path.GetDirectoryName(current)!];
                    using (OpenRelative(parent, Path.GetFileName(current), 0x80, 1, true, true, false)) { }
                    AddPin(current);
                }
            }
            Recheck();
        }
        internal WindowsFileIdentity CreatePrivateDirectory(string relative)
        {
            var path = Full(relative); var parent = Parent(path);
            using (var security = new WindowsPrivateSecurity.Descriptor())
            {
                var handle = OpenRelative(parent, Path.GetFileName(path), 0x80 | 0x20000, 1, true, true, false, security.Pointer);
                try
                {
                    var info = ReadInfo(handle, true); var identity = Identity(info);
                    if (identity.Volume != RootIdentity.Volume || !string.Equals(Final(handle), path, StringComparison.OrdinalIgnoreCase))
                        throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                    WindowsPrivateSecurity.AssertDirectory(handle); Recheck();
                    _pins.Add(path, new Pin(path, handle, identity)); return identity;
                }
                catch { handle.Dispose(); throw; }
            }
        }
        internal void AssertPrivateDirectory(string relative)
        {
            var path = Full(relative); Parent(path); AddPin(path); Recheck();
            WindowsPrivateSecurity.AssertDirectory(_pins[path].Handle);
        }
        internal WindowsFileIdentity DirectoryIdentity(string relative)
        {
            var path = Full(relative); Parent(path); AddPin(path); Recheck(); return _pins[path].Identity;
        }
        private Pin Parent(string path)
        {
            var parent = Path.GetDirectoryName(path)!;
            if (!parent.StartsWith(Root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parent, Root, StringComparison.OrdinalIgnoreCase)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            PinPath(parent); Recheck(); return _pins[parent];
        }
        internal FileLease OpenFile(string relative, bool mutable = false, bool create = false)
        { return OpenFileCore(relative, mutable, create, 0); }
        // Verified helper image only: allow execution/read while denying write/delete replacement.
        internal FileLease OpenLaunchImage(string relative)
        { return OpenFileCore(relative, false, false, 1); }
        // Dedicated diagnostic writer: allow bounded collector reads, deny foreign writes/deletes.
        internal FileLease OpenDiagnosticFile(string relative,bool create=false)
        { return OpenFileCore(relative,true,create,1); }
        private FileLease OpenFileCore(string relative, bool mutable, bool create, uint share)
        {
            var path = Full(relative); var parent = Parent(path);
            var access = Read | (mutable ? Write | Delete : 0);
            _beforeOperation?.Invoke("open");
            var handle = OpenRelative(parent, Path.GetFileName(path), access, share, create, false, mutable);
            try
            {
                var info = ReadInfo(handle, false); var identity = Identity(info);
                if (identity.Volume != RootIdentity.Volume || !string.Equals(Final(handle), path, StringComparison.OrdinalIgnoreCase))
                    throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                if (mutable && (info.Attributes & 1) != 0) throw new ApplySafetyException(ApplyFailure.FileBusy);
                Recheck(); // Detect a directory reparse mutation before exposing any data stream.
                return new FileLease(this, path, handle, identity, mutable);
            }
            catch { handle.Dispose(); throw; }
        }
        internal sealed class FileLease : IDisposable
        {
            private WindowsFileFence _fence;
            private readonly SafeFileHandle _handle;
            private readonly bool _mutable;
            private readonly FileStream _stream;
            internal string Path { get; private set; }
            internal readonly WindowsFileIdentity Identity;
            internal Stream Stream { get { return _stream; } }
            internal FileLease(WindowsFileFence fence, string path, SafeFileHandle handle, WindowsFileIdentity identity, bool mutable)
            { _fence = fence; Path = path; _handle = handle; Identity = identity; _mutable = mutable; _stream = new FileStream(handle, mutable ? FileAccess.ReadWrite : FileAccess.Read); }
            internal void Recheck()
            {
                _fence.Recheck();
                if (!Identity.Same(WindowsFileFence.Identity(ReadInfo(_handle, false))) || !string.Equals(Final(_handle), Path, StringComparison.OrdinalIgnoreCase))
                    throw new ApplySafetyException(ApplyFailure.IdentityChanged);
            }
            internal string Hash()
            {
                Recheck(); _stream.Position = 0;
                using (var hash = SHA256.Create()) { var digest = UpdateTrust.Hex(hash.ComputeHash(_stream)); _stream.Position = 0; Recheck(); return digest; }
            }
            internal void Flush() { _stream.Flush(true); Recheck(); }
            internal void RenameTo(WindowsFileFence destinationFence, string relative)
            {
                if (!_mutable) throw new ApplySafetyException(ApplyFailure.InvalidState);
                var destination = destinationFence.Full(relative); var parent = destinationFence.Parent(destination);
                Recheck(); destinationFence.Recheck();
                if (Identity.Volume != parent.Identity.Volume) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                var leaf = System.IO.Path.GetFileName(destination); Component(leaf);
                var name = Encoding.Unicode.GetBytes(leaf);
                var buffer = Marshal.AllocHGlobal(20 + name.Length); var referenced = false;
                try
                {
                    for (var i = 0; i < 20; i++) Marshal.WriteByte(buffer, i, 0);
                    parent.Handle.DangerousAddRef(ref referenced);
                    Marshal.WriteIntPtr(buffer, 8, parent.Handle.DangerousGetHandle());
                    Marshal.WriteInt32(buffer, 16, name.Length); Marshal.Copy(name, 0, IntPtr.Add(buffer, 20), name.Length);
                    destinationFence._beforeOperation?.Invoke("rename");
                    // ReplaceIfExists=false. Another file occupying the name causes a safe stop.
                    IoStatus io;
                    var status = NtSetInformationFile(_handle, out io, buffer, (uint)(20 + name.Length), 10); // FileRenameInformation
                    if (status != 0)
                        throw new ApplySafetyException(ApplyFailure.FileBusy, (int)RtlNtStatusToDosError(status));
                    Path = destination;
                    _fence = destinationFence;
                    if (!Identity.Same(WindowsFileFence.Identity(ReadInfo(_handle, false))) || !string.Equals(Final(_handle), Path, StringComparison.OrdinalIgnoreCase))
                        throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                    destinationFence.Recheck();
                }
                finally { if (referenced) parent.Handle.DangerousRelease(); Marshal.FreeHGlobal(buffer); }
            }
            internal void RemoveDiagnosticPrevious() {
                if (!_mutable || !Regex.IsMatch(Path.Substring(_fence.Root.Length + 1).Replace('\\','/'), @"\ABepInEx/BetterAstralParty-Diagnostics/minimal-v1/(?:(?:helper|installer)/)?events-previous\.jsonl\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                Recheck(); var buffer=Marshal.AllocHGlobal(1);
                try { Marshal.WriteByte(buffer,1);IoStatus io;var status=NtSetInformationFile(_handle,out io,buffer,1,13);
                    if(status!=0)throw new ApplySafetyException(ApplyFailure.FileBusy,(int)RtlNtStatusToDosError(status));_fence.Recheck();
                } finally {Marshal.FreeHGlobal(buffer);}
            }
            internal void RemoveOrdinaryRecoveryTarget(string relative) {
                // Called only with an entry from the approved install recovery allowlist.
                // Delete this held object; never resolve a target pathname again.
                if (!_mutable || Path != _fence.Full(relative)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                Recheck(); var buffer=Marshal.AllocHGlobal(1);
                try {
                    Marshal.WriteByte(buffer,1);IoStatus io;var status=NtSetInformationFile(_handle,out io,buffer,1,13);
                    if(status!=0)throw new ApplySafetyException(ApplyFailure.FileBusy,(int)RtlNtStatusToDosError(status));
                    _fence.Recheck();
                } finally {Marshal.FreeHGlobal(buffer);}
            }
            internal void RemoveTemporaryPackage() {
                if (!_mutable || !Regex.IsMatch(Path.Substring(_fence.Root.Length + 1).Replace('\\','/'), @"\Abap-stage-[0-9a-f]{32}/package\.zip\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                Recheck();
                var buffer=Marshal.AllocHGlobal(1);
                try {
                    Marshal.WriteByte(buffer,1); IoStatus io;
                    var status=NtSetInformationFile(_handle,out io,buffer,1,13); // FILE_DISPOSITION_INFORMATION has a one-byte BOOLEAN.
                    if(status!=0) throw new ApplySafetyException(ApplyFailure.FileBusy,(int)RtlNtStatusToDosError(status));
                    _fence.Recheck();
                } finally {Marshal.FreeHGlobal(buffer);}
            }
            public void Dispose() { _stream.Dispose(); }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pin in _pins.Values) pin.Dispose();
            _pins.Clear();
        }
    }
}
