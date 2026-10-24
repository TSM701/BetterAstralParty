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
        internal ApplySafetyException(ApplyFailure failure, int error, Exception inner) : base("Update safety: " + failure, inner)
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
            if (!WindowsPath.SafeComponent(component))
                throw new ApplySafetyException(ApplyFailure.UnsafePath);
        }
        private static string Absolute(string path)
        {
            try { return WindowsPath.Absolute(path); }
            catch (ArgumentException) { throw new ApplySafetyException(ApplyFailure.UnsafePath); }
        }
        internal static void ValidatePlannedPath(string path) {
            try {Absolute(path);}
            catch(ApplySafetyException error) {
                error.Data["BapRecoveryReason"]=path!=null && path.Length>WindowsPath.MaximumLength?"MaintenancePathTooLong":"MaintenancePathInvalid";
                throw;
            }
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
            var handle = CreateFileW(WindowsPath.ForIo(path), access, share, IntPtr.Zero, disposition, flags | OpenReparse, IntPtr.Zero);
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
            var name = new StringBuilder(512);
            for (;;) {
                var length = GetFinalPathNameByHandleW(handle, name, (uint)name.Capacity, 0);
                if (length == 0 || length > WindowsPath.MaximumLength + (length >= name.Capacity ? 5 : 4)) throw new ApplySafetyException(ApplyFailure.IdentityChanged, Marshal.GetLastWin32Error());
                if (length < name.Capacity) break;
                name = new StringBuilder(checked((int)length + 1));
            }
            var path = name.ToString();
            if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return Absolute(path.Substring(4));
        }
        // A packaged host can merge directory views while redirecting only their child files.
        internal sealed class OrdinaryJournal : IDisposable
        {
            private readonly List<Pin> _namespacePins=new List<Pin>();
            private readonly SafeFileHandle _handle;
            internal readonly WindowsFileIdentity Identity;
            internal readonly string PhysicalPath;
            internal OrdinaryJournal(string logicalRoot)
            {
                var root=Absolute(logicalRoot);var current=root.Substring(0,3);
                try {
                    PinNamespace(current);
                    foreach(var component in root.Substring(3).Split('\\')) {current=Path.Combine(current,component);PinNamespace(current);}
                    _handle=Open(Path.Combine(root,"pending.json"),0,7,3,0);
                    try {Identity=WindowsFileFence.Identity(ReadInfo(_handle,false));PhysicalPath=Final(_handle);Recheck(PhysicalPath);}
                    catch {_handle.Dispose();throw;}
                } catch {foreach(var pin in _namespacePins)pin.Dispose();throw;}
            }
            private void PinNamespace(string path)
            {
                var handle=Open(path,0x80|0x20000,1,3,BackupSemantics);
                try {_namespacePins.Add(new Pin(Final(handle),handle,WindowsFileFence.Identity(ReadInfo(handle,true))));}
                catch {handle.Dispose();throw;}
            }
            internal void Recheck(string path)
            {
                foreach(var pin in _namespacePins)
                    if(!pin.Identity.Same(WindowsFileFence.Identity(ReadInfo(pin.Handle,true))) || Final(pin.Handle)!=pin.Path)
                        throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                if(!Identity.Same(WindowsFileFence.Identity(ReadInfo(_handle,false))) || Final(_handle)!=path)
                    throw new ApplySafetyException(ApplyFailure.IdentityChanged);
            }
            public void Dispose() {_handle.Dispose();foreach(var pin in _namespacePins)pin.Dispose();}
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
                : OpenRelative(_pins[WindowsPath.Parent(path)!], WindowsPath.Leaf(path), 0x80 | 0x20000, 1, false, true, false);
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
                    var parent = _pins[WindowsPath.Parent(current)!];
                    using (OpenRelative(parent, WindowsPath.Leaf(current), 0x80, 1, true, true, false)) { }
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
                var handle = OpenRelative(parent, WindowsPath.Leaf(path), 0x80 | 0x20000, 1, true, true, false, security.Pointer);
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
        internal void RemoveEmptyMaintenanceDirectory(string relative)
        {
            if (relative != "BepInEx/plugins/BetterAstralParty" && relative != "BepInEx/BetterAstralParty-Diagnostics") throw new ApplySafetyException(ApplyFailure.UnsafePath);
            var path = Full(relative); var parent = Parent(path); Recheck();
            using (var handle = OpenRelative(parent, WindowsPath.Leaf(path), Delete | 0x80, 0, false, true, false)) {
                var identity = Identity(ReadInfo(handle, true));
                if (identity.Volume != RootIdentity.Volume || !string.Equals(Final(handle), path, StringComparison.OrdinalIgnoreCase)) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                var buffer = Marshal.AllocHGlobal(1);
                try { Marshal.WriteByte(buffer, 1); IoStatus io; var status = NtSetInformationFile(handle, out io, buffer, 1, 13);
                    if (status != 0) throw new ApplySafetyException(ApplyFailure.FileBusy, (int)RtlNtStatusToDosError(status));
                    Recheck();
                } finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        // Delete only the same empty object reopened beneath its held parent.
        internal void RemoveEmptyRecoveryDirectory(string relative, string expectedIdentity)
        {
            var path = Full(relative); var parent = Parent(path);
            AddPin(path); var pin = _pins[path];
            if (pin.Identity.Text != expectedIdentity) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
            foreach (var child in _pins.Keys)
                if (child.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)) throw new ApplySafetyException(ApplyFailure.FileBusy);
            Recheck(); _pins.Remove(path); pin.Dispose();
            // Release only our own pin; the held parent makes the reopen and deletion identity-bound.
            _beforeOperation?.Invoke("remove-empty-open");
            using (var handle = OpenRelative(parent, WindowsPath.Leaf(path), Delete | 0x80, 0, false, true, false)) {
                var identity = Identity(ReadInfo(handle, true));
                if (identity.Text != expectedIdentity || identity.Volume != RootIdentity.Volume || !string.Equals(Final(handle), path, StringComparison.OrdinalIgnoreCase))
                    throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                Recheck(); var buffer = Marshal.AllocHGlobal(1);
                try { Marshal.WriteByte(buffer, 1); IoStatus io; var status = NtSetInformationFile(handle, out io, buffer, 1, 13);
                    if (status != 0) throw new ApplySafetyException(ApplyFailure.FileBusy, (int)RtlNtStatusToDosError(status));
                    Recheck();
                } finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        private static void RenameHeld(SafeFileHandle handle, Pin parent, string leaf)
        {
            Component(leaf); var name = Encoding.Unicode.GetBytes(leaf);
            var buffer = Marshal.AllocHGlobal(20 + name.Length); var referenced = false;
            try {
                for (var i = 0; i < 20; i++) Marshal.WriteByte(buffer, i, 0);
                parent.Handle.DangerousAddRef(ref referenced);
                Marshal.WriteIntPtr(buffer, 8, parent.Handle.DangerousGetHandle());
                Marshal.WriteInt32(buffer, 16, name.Length); Marshal.Copy(name, 0, IntPtr.Add(buffer, 20), name.Length);
                // ReplaceIfExists=false; never overwrite another file or directory.
                IoStatus io; var status = NtSetInformationFile(handle, out io, buffer, (uint)(20 + name.Length), 10);
                if (status != 0) throw new ApplySafetyException(ApplyFailure.FileBusy, (int)RtlNtStatusToDosError(status));
            } finally { if (referenced) parent.Handle.DangerousRelease(); Marshal.FreeHGlobal(buffer); }
        }
        private Pin Parent(string path)
        {
            var parent = WindowsPath.Parent(path)!;
            if (!parent.StartsWith(Root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parent, Root, StringComparison.OrdinalIgnoreCase)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            PinPath(parent); Recheck(); return _pins[parent];
        }
        // Nested proof readers may coexist; writers and deletion remain blocked.
        internal FileLease OpenFile(string relative, bool mutable = false, bool create = false)
        { return OpenFileCore(relative, mutable, create, !mutable && !create ? 1u : 0u); }
        // Maintenance must block a new image section, not only write/delete replacement.
        internal FileLease OpenExecutionBarrier(string relative)
        { return OpenFileCore(relative, false, false, 0); }
        // Verified helper image only: allow execution/read while denying write/delete replacement.
        internal FileLease OpenLaunchImage(string relative)
        { return OpenFileCore(relative, false, false, 1); }
        // Dedicated diagnostic writer: allow bounded collector reads, deny foreign writes/deletes.
        internal FileLease OpenDiagnosticFile(string relative,bool create=false)
        { return OpenFileCore(relative,true,create,1); }
        // In-process update only: the running plugin image may be renamed by handle, never written or deleted.
        internal FileLease OpenLoadedImage(string relative)
        { return OpenFileCore(relative, false, false, 1 | 4, true); }
        private FileLease OpenFileCore(string relative, bool mutable, bool create, uint share, bool renameable = false)
        {
            var path = Full(relative); var parent = Parent(path);
            var access = Read | (mutable ? Write | Delete : 0) | (renameable ? Delete : 0);
            _beforeOperation?.Invoke("open");
            var handle = OpenRelative(parent, WindowsPath.Leaf(path), access, share, create, false, mutable);
            try
            {
                var info = ReadInfo(handle, false); var identity = Identity(info);
                if (identity.Volume != RootIdentity.Volume || !string.Equals(Final(handle), path, StringComparison.OrdinalIgnoreCase))
                    throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                if (mutable && (info.Attributes & 1) != 0) throw new ApplySafetyException(ApplyFailure.FileBusy);
                Recheck(); // Detect a directory reparse mutation before exposing any data stream.
                return new FileLease(this, path, handle, identity, mutable, renameable);
            }
            catch { handle.Dispose(); throw; }
        }
        internal sealed class FileLease : IDisposable
        {
            private WindowsFileFence _fence;
            private readonly SafeFileHandle _handle;
            private readonly bool _mutable, _renameable;
            private readonly FileStream _stream;
            internal string Path { get; private set; }
            internal readonly WindowsFileIdentity Identity;
            internal Stream Stream { get { return _stream; } }
            internal FileLease(WindowsFileFence fence, string path, SafeFileHandle handle, WindowsFileIdentity identity, bool mutable, bool renameable = false)
            { _fence = fence; Path = path; _handle = handle; Identity = identity; _mutable = mutable; _renameable = renameable; _stream = new FileStream(handle, mutable ? FileAccess.ReadWrite : FileAccess.Read); }
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
            internal void Flush() { if (_mutable) _stream.Flush(true); Recheck(); }
            internal void RenameTo(WindowsFileFence destinationFence, string relative)
            {
                if (!_mutable && !_renameable) throw new ApplySafetyException(ApplyFailure.InvalidState);
                var destination = destinationFence.Full(relative); var parent = destinationFence.Parent(destination);
                Recheck(); destinationFence.Recheck();
                if (Identity.Volume != parent.Identity.Volume) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                destinationFence._beforeOperation?.Invoke("rename");
                RenameHeld(_handle, parent, WindowsPath.Leaf(destination));
                Path = destination; _fence = destinationFence;
                if (!Identity.Same(WindowsFileFence.Identity(ReadInfo(_handle, false))) || !string.Equals(Final(_handle), Path, StringComparison.OrdinalIgnoreCase))
                    throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                destinationFence.Recheck();
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
            internal void RemoveChannelAdoption() {
                if (!_mutable || Path != _fence.Full("BetterAstralParty.channel-adopt")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                Recheck(); var buffer=Marshal.AllocHGlobal(1);
                try { Marshal.WriteByte(buffer,1);IoStatus io;var status=NtSetInformationFile(_handle,out io,buffer,1,13);
                    if(status!=0)throw new ApplySafetyException(ApplyFailure.FileBusy,(int)RtlNtStatusToDosError(status));_fence.Recheck();
                } finally {Marshal.FreeHGlobal(buffer);}
            }
            // Update fault/operation records directly in the game root, live or archived by earlier versions.
            internal void RemoveUpdateRecord() {
                if (!_mutable || !Regex.IsMatch(Path.Substring(_fence.Root.Length + 1), @"\A(?:BetterAstralParty\.update-v5\.(?:operation|failure|preferences-failed)|bap-(?:operation-done|fault-ack)-[0-9a-f]{32})\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                Recheck(); var buffer=Marshal.AllocHGlobal(1);
                try { Marshal.WriteByte(buffer,1);IoStatus io;var status=NtSetInformationFile(_handle,out io,buffer,1,13);
                    if(status!=0)throw new ApplySafetyException(ApplyFailure.FileBusy,(int)RtlNtStatusToDosError(status));_fence.Recheck();
                } finally {Marshal.FreeHGlobal(buffer);}
            }
            internal void RemoveTemporaryPackage() {
                if (!_mutable || !Regex.IsMatch(Path.Substring(_fence.Root.Length + 1).Replace('\\','/'), @"\ABetterAstralParty\.Update/bap-v5-stage-[0-9a-f]{32}/package\.zip\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
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
