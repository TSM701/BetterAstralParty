#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using BetterAstralParty.Updating;
#if NETFRAMEWORK
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
#else
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v8.0")]
#endif

internal static class FixtureEntry
{
    private static int _checks;
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("inert verified long-path payload");
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string alias, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint size, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    private static void Check(bool valid, string name)
    {
        if (!valid) throw new Exception("Long-path fixture: " + name);
        ++_checks; Console.WriteLine("CHECK " + name);
    }
    private static void RejectPath(string path)
    {
        try { WindowsPath.Absolute(path); }
        catch (ArgumentException) { Check(true, "unsafe logical path rejected"); return; }
        throw new Exception("Unsafe path accepted.");
    }
    private static void Reject(Action action, string name, ApplyFailure failure)
    {
        try { action(); }
        catch (ApplySafetyException error) { Check(error.Failure == failure, name + " [" + error.Failure + "]"); return; }
        throw new Exception("Unsafe fence operation accepted: " + name);
    }
    private static void DenyIo(Action action, string name)
    {
        try { action(); }
        catch (IOException) { Check(true, name); return; }
        catch (UnauthorizedAccessException) { Check(true, name); return; }
        throw new Exception("Held object was replaced: " + name);
    }
    private static string AtLength(string prefix, int length)
    {
        if (prefix != WindowsPath.Absolute(prefix)) throw new ArgumentException("Canonical fixture prefix required.", nameof(prefix));
        if (length < prefix.Length) throw new ArgumentOutOfRangeException(nameof(length));
        var path = prefix;
        if (length - path.Length == 1)
        {
            var leaf = path.Length == 3 ? "p" : WindowsPath.Leaf(path) + "p";
            if (!WindowsPath.SafeComponent(leaf)) throw new ArgumentException("One-character padding cannot extend this fixture leaf safely.", nameof(length));
            path += "p";
        }
        while (path.Length < length)
        {
            var remaining = length - path.Length;
            var separator = path.Length == 3 ? "" : "\\";
            var count = Math.Min(181, remaining - separator.Length);
            if (remaining - count - separator.Length == 1) --count;
            path += separator + new string('p', count);
        }
        if (path.Length != length) throw new Exception("Fixture padding did not reach the exact requested length.");
        if (path.Length != 3) foreach (var component in path.Substring(3).Split('\\'))
            if (!WindowsPath.SafeComponent(component)) throw new Exception("Unsafe fixture padding component.");
        return path;
    }
    private static void Padding()
    {
        foreach (var prefix in new[] { @"C:\", @"C:\fixture", @"C:\" + new string('x', 254), @"C:\" + new string('x', 255) })
        foreach (var remaining in new[] { 1, 2, 181, 182, 183, 364, 365, 366 })
        {
            if (remaining == 1 && prefix.Length != 3 && WindowsPath.Leaf(prefix).Length == 255)
            {
                var rejected = false;
                try { AtLength(prefix, prefix.Length + remaining); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected, "one-character padding refuses a saturated component without changing its prefix");
                continue;
            }
            var path = AtLength(prefix, prefix.Length + remaining); var safe = true;
            foreach (var component in path.Substring(3).Split('\\')) safe &= WindowsPath.SafeComponent(component);
            Check(path.Length == prefix.Length + remaining && path.StartsWith(prefix, StringComparison.Ordinal)
                && WindowsPath.Absolute(path) == path && safe, "exact safe padding remainder " + remaining + " prefix length " + prefix.Length);
        }
        Check(AtLength(@"C:\fixture", @"C:\fixture".Length) == @"C:\fixture", "zero padding preserves the canonical prefix");
        var shorter = false;
        try { AtLength(@"C:\fixture", @"C:\fixture".Length - 1); }
        catch (ArgumentOutOfRangeException) { shorter = true; }
        Check(shorter, "padding never silently accepts a target shorter than its prefix");
    }
    private static string Scoped(string path)
    {
        var ordinary = WindowsPath.Absolute(path);
        if (!ordinary.StartsWith(FixtureBoundary.Root + "\\", StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture escaped its fresh artifact root.");
        return ordinary;
    }
    private static string NewRoot(string name, int length)
    {
        var path = Scoped(AtLength(FixtureBoundary.Root + "\\" + name, length));
        if (Directory.Exists(WindowsPath.ForIo(path))) throw new Exception("Fresh fixture root required.");
        Directory.CreateDirectory(WindowsPath.ForIo(path)); return path;
    }
    private static void Logical()
    {
        foreach (var length in new[] { 240, 260, 512, 1000, WindowsPath.MaximumLength })
        {
            var path = AtLength(@"C:\fixture", length);
            Check(path.Length == length && WindowsPath.Absolute(path) == path && WindowsPath.ForIo(path) == @"\\?\" + path && WindowsPath.FromIo(WindowsPath.ForIo(path)) == path && WindowsPath.ForProcess(path) == (length < 260 ? path : @"\\?\" + path), "ordinary identity round-trip length " + length);
        }
        foreach (var size in new[] { 181, 254, 255 })
        {
            var component = new string('한', size); var path = @"D:\fixture\" + component;
            Check(WindowsPath.SafeComponent(component) && WindowsPath.Absolute(path) == path && WindowsPath.Leaf(path) == component && WindowsPath.Parent(path) == @"D:\fixture", "Unicode component length " + size);
        }
        Check(WindowsPath.Absolute(@"c:\") == @"c:\" && WindowsPath.Absolute(@"C:\valid\") == @"C:\valid" && WindowsPath.Absolute(@"C:\valid\\") == @"C:\valid", "drive root and safe trailing separator canonicalization");
        foreach (var path in new[] {
            @"C:\fixture\..\escape", @"C:\fixture\.\escape", @"\\server\share\fixture", @"\\?\C:\fixture", @"\\.\C:\fixture",
            @"C:\fixture\file.txt:secret", @"C:\fixture\\child", @"C:\\fixture", @"C:/fixture", "C:fixture",
            @"C:\fixture\CON", @"C:\fixture\NUL.txt", @"C:\fixture\PRN", @"C:\fixture\AUX", @"C:\fixture\CONIN$", @"C:\fixture\CONOUT$",
            @"C:\fixture\COM1", @"C:\fixture\COM9.txt", @"C:\fixture\LPT1", @"C:\fixture\LPT9.txt", @"C:\fixture\COM¹.txt", @"C:\fixture\LPT²", @"C:\fixture\COM³",
            @"C:\fixture\trailing.", @"C:\fixture\trailing ", @"C:\fixture\wild*", @"C:\fixture\wild?", @"C:\fixture\quote" + '"', @"C:\fixture\pipe|", "C:\\fixture\\control\u0001",
            @"C:\fixture\" + new string('x', 256), AtLength(@"C:\fixture", WindowsPath.MaximumLength + 1) }) RejectPath(path);
        foreach (var device in new[] { @"\\?\C:\fixture", @"\\.\C:\fixture", @"\\server\share" })
        {
            try { WindowsPath.ForIo(device); }
            catch (ArgumentException) { Check(true, "caller device namespace not adopted"); continue; }
            throw new Exception("Untrusted I/O namespace accepted.");
        }
        RejectPath(null!);
    }
    private static void Files(string name, int length)
    {
        var root = NewRoot(name, length);
        var setting = root + "\\settings.keep"; var unrelated = root + "\\other-mod.keep";
        File.WriteAllText(WindowsPath.ForIo(setting), "user setting"); File.WriteAllText(WindowsPath.ForIo(unrelated), "unrelated mod");
        using (var fence = new WindowsFileFence(root))
        {
            Check(fence.Root == root && !fence.Root.StartsWith(@"\\?\", StringComparison.Ordinal) && fence.RootIdentity.FileId != 0, "ordinary root identity length " + length);
            foreach (var bad in new[] { "../escape", "a/../escape", "a\\b", "/absolute", "C:/absolute", "file:ads", "COM¹.txt", "a.", "a ", "a//b", new string('x', 256) })
                Reject(() => fence.Full(bad), "unsafe relative path", ApplyFailure.UnsafePath);
            var component = new string('d', 181); var leaf = new string('f', 251) + ".bin";
            fence.EnsureDirectory(component + "/nested");
            using (var file = fence.OpenFile(component + "/nested/" + leaf, true, true))
            {
                file.Stream.Write(Payload, 0, Payload.Length); file.Flush();
                Check(file.Path.Length > root.Length + 400 && !file.Path.StartsWith(@"\\?\", StringComparison.Ordinal) && file.Hash() == UpdateTrust.Hash(Payload), "long file create, flush and held hash");
                var identity = file.Identity.Text;
                Reject(() => { using (fence.OpenFile(component + "/nested/" + leaf)) { } }, "exclusive lease denies second reader", ApplyFailure.FileBusy);
                DenyIo(() => File.WriteAllText(WindowsPath.ForIo(Scoped(file.Path)), "foreign write"), "exclusive file lease denies replacement write");
                DenyIo(() => File.Move(WindowsPath.ForIo(Scoped(file.Path)), WindowsPath.ForIo(Scoped(WindowsPath.Parent(file.Path) + "\\foreign-renamed.bin"))), "exclusive file lease denies foreign rename");
                var original = file.Path;
                file.RenameTo(fence, component + "/nested/renamed.bin");
                Check(file.Identity.Text == identity && file.Hash() == UpdateTrust.Hash(Payload) && !File.Exists(WindowsPath.ForIo(original)), "held rename preserves bytes and identity");
                using (var occupied = fence.OpenFile(component + "/nested/occupied.bin", true, true))
                {
                    occupied.Stream.WriteByte(44); occupied.Flush();
                    Reject(() => file.RenameTo(fence, component + "/nested/occupied.bin"), "occupied rename never overwrites", ApplyFailure.FileBusy);
                    Check(occupied.Hash() == UpdateTrust.Hash(new byte[] { 44 }) && file.Hash() == UpdateTrust.Hash(Payload), "collision preserves both files");
                }
            }
            using (var read = fence.OpenFile(component + "/nested/renamed.bin"))
            using (var second = fence.OpenLaunchImage(component + "/nested/renamed.bin"))
                Check(read.Hash() == second.Hash() && read.Hash() == UpdateTrust.Hash(Payload), "verified read leases coexist");
            DenyIo(() => Directory.Move(WindowsPath.ForIo(Scoped(root)), WindowsPath.ForIo(Scoped(root + "-moved"))), "root replacement blocked by held fence");
            var ancestor = WindowsPath.Parent(root);
            if (ancestor.StartsWith(FixtureBoundary.Root + "\\", StringComparison.OrdinalIgnoreCase))
                DenyIo(() => Directory.Move(WindowsPath.ForIo(Scoped(ancestor)), WindowsPath.ForIo(Scoped(ancestor + "-moved"))), "ancestor replacement blocked by held fence");
            var privateId = fence.CreatePrivateDirectory("history"); fence.AssertPrivateDirectory("history");
            fence.Recheck();
            var directories = WindowsPath.Directories(root, "*"); var count = 0;
            foreach (var directory in directories) { Check(!directory.StartsWith(@"\\?\", StringComparison.Ordinal), "directory enumeration returns ordinary identity"); ++count; }
            Check(count == 2, "long directory enumeration complete");
            var files = Directory.GetFiles(WindowsPath.ForIo(root + "\\" + component + "\\nested"), "*.bin");
            Check(files.Length == 2 && !WindowsPath.FromIo(files[0]).StartsWith(@"\\?\", StringComparison.Ordinal), "known safe long file enumeration returns ordinary identity");
        }
        Check(File.ReadAllText(WindowsPath.ForIo(setting)) == "user setting" && File.ReadAllText(WindowsPath.ForIo(unrelated)) == "unrelated mod", "settings and unrelated files preserved");
    }
    // Same inert junction setup used by existing apply-boundary fixtures; no shell or privilege changes.
    private static void Junction(string path, string target)
    {
        Scoped(path); Scoped(target); Directory.CreateDirectory(WindowsPath.ForIo(path));
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target); var print = Encoding.Unicode.GetBytes(target);
        var data = new byte[16 + substitute.Length + print.Length + 4];
        BitConverter.GetBytes(0xA0000003U).CopyTo(data, 0); BitConverter.GetBytes(checked((ushort)(data.Length - 8))).CopyTo(data, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(data, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(data, 12); BitConverter.GetBytes(checked((ushort)print.Length)).CopyTo(data, 14);
        substitute.CopyTo(data, 16); print.CopyTo(data, 18 + substitute.Length);
        using (var handle = CreateFileW(WindowsPath.ForIo(path), 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
        {
            uint returned;
            if (handle.IsInvalid || !DeviceIoControl(handle, 0x900A4, data, (uint)data.Length, IntPtr.Zero, 0, out returned, IntPtr.Zero))
                throw new IOException("Inert junction setup failed: " + Marshal.GetLastWin32Error());
        }
    }
    private static void Links(string name)
    {
        var root = NewRoot(name, 512); var target = NewRoot(name + "-target", 600);
        var original = root + "\\original.bin"; var alias = root + "\\alias.bin";
        File.WriteAllBytes(WindowsPath.ForIo(original), Payload);
        Check(CreateHardLinkW(WindowsPath.ForIo(alias), WindowsPath.ForIo(original), IntPtr.Zero), "long fixture hardlink created");
        using (var fence = new WindowsFileFence(root))
            Reject(() => { using (fence.OpenFile("original.bin")) { } }, "hard-linked file refused", ApplyFailure.LinkedFile);
        var linked = root + "\\linked"; Junction(linked, target);
        Reject(() => { using (new WindowsFileFence(linked)) { } }, "junction root refused", ApplyFailure.LinkedFile);
        using (var fence = new WindowsFileFence(root))
        {
            Reject(() => fence.EnsureDirectory("linked/new"), "junction ancestor refused before creation", ApplyFailure.LinkedFile);
            Reject(() => { using (fence.OpenFile("linked/escape.bin", true, true)) { } }, "junction child file refused before creation", ApplyFailure.LinkedFile);
        }
        Check(!Directory.Exists(WindowsPath.ForIo(target + "\\new")) && !File.Exists(WindowsPath.ForIo(target + "\\escape.bin")), "junction destination remains untouched");
        Check(File.ReadAllBytes(WindowsPath.ForIo(original)).Length == Payload.Length && File.ReadAllBytes(WindowsPath.ForIo(alias)).Length == Payload.Length, "rejected hardlinks preserved");
    }
    private static void WorkPaths(string runtime)
    {
        var root = NewRoot(runtime + "-work-namespace", 240);
        using (var fence = new WindowsFileFence(root))
        {
            var rootId = fence.RootIdentity.Text;
            foreach (var kind in new[] { "stage", "txn" }) {
                var relative = UpdateWorkPath.New(kind); var identity = UpdateWorkPath.Create(fence, relative);
                Check(UpdateWorkPath.IsContained(relative, kind) && fence.DirectoryIdentity(relative).Same(identity), "new " + kind + " is private contained game-relative work");
                Check(UpdateWorkPath.Directories(fence, kind).Contains(relative), "new " + kind + " is found under selected game fence");
            }
            Check(fence.RootIdentity.Text == rootId && Directory.GetDirectories(WindowsPath.ForIo(root)).Length == 1, "all new work shares one child namespace without changing game-root identity");
            DenyIo(() => Directory.Move(WindowsPath.ForIo(fence.Full(UpdateWorkPath.Root)), WindowsPath.ForIo(root + "-replaced")), "held work parent identity cannot be replaced");
            var flat = "bap-v4-stage-" + new string('a', 32); fence.CreatePrivateDirectory(flat);
            var legacy = UpdateWorkPath.Root + "/bap-v4-stage-" + new string('c', 32); fence.CreatePrivateDirectory(legacy);
            Check(!UpdateWorkPath.Directories(fence, "stage").Contains(flat) && !UpdateWorkPath.Directories(fence, "stage").Contains(legacy), "protocol-4 work is never adopted as v5 work");
            Reject(() => UpdateWorkPath.RequireContained(flat, "stage"), "flat protocol-4 work cannot be mutated as v5 work", ApplyFailure.UnsafePath);
            var unknown = UpdateWorkPath.Root + "/unrelated-user-directory"; fence.CreatePrivateDirectory(unknown);
            Check(!UpdateWorkPath.Directories(fence, "stage").Contains(unknown), "unknown container descendants are not adopted as update work");
            Reject(() => UpdateWorkPath.Create(fence, flat), "new work cannot be written to the historical flat namespace", ApplyFailure.UnsafePath);
            Reject(() => UpdateWorkPath.Create(fence, UpdateWorkPath.Root + "/../bap-v4-stage-" + new string('b', 32)), "work path traversal rejected", ApplyFailure.UnsafePath);
            fence.Recheck();
        }
        var linked = NewRoot(runtime + "-work-linked", 240); var foreign = NewRoot(runtime + "-work-foreign", 240);
        Junction(linked + "\\" + UpdateWorkPath.Root, foreign);
        using (var fence = new WindowsFileFence(linked)) {
            Reject(() => UpdateWorkPath.Create(fence, UpdateWorkPath.New("stage")), "linked namespace ancestor rejects creation", ApplyFailure.LinkedFile);
            Reject(() => UpdateWorkPath.Directories(fence, "stage").ToArray(), "linked namespace ancestor rejects discovery", ApplyFailure.LinkedFile);
        }
        Check(Directory.GetFileSystemEntries(WindowsPath.ForIo(foreign)).Length == 0, "namespace link refusals preserve unrelated target contents");
    }
    public static int Main()
    {
        try
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8) throw new Exception("Windows x64 fixture required.");
            var runtime = typeof(object).Assembly.GetName().Name == "mscorlib" ? "framework" : "net8";
            Padding();
            Logical();
            foreach (var length in new[] { 240, 260, 512, 1000 }) Files(runtime + "-files-" + length, length);
            Links(runtime + "-links");
            WorkPaths(runtime);
            Console.WriteLine("RESULT=" + _checks);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
