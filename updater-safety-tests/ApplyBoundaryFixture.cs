#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using BetterAstralParty.Updating;

internal static class ApplyBoundaryFixture
{
    private static int _checks;
    private static readonly string[] Images = { "AstralParty_INT.exe", "BetterAstralParty-Launcher.exe" };
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize,
        IntPtr output, uint outputSize, out uint bytes, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception("Boundary fixture: " + message); }
    private static void Reject(Action action, string message, ApplyFailure? expected = null)
    {
        try { action(); }
        catch (ApplySafetyException error) { Check(!expected.HasValue || error.Failure == expected.Value, message + " [" + error.Failure + "]"); return; }
        throw new Exception("Boundary fixture accepted: " + message);
    }
    private static Process Start(string path, int milliseconds)
    {
        return Process.Start(new ProcessStartInfo {
            FileName = path, Arguments = milliseconds.ToString(), WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        })!;
    }
    private static void Junction(string path, string target)
    {
        Directory.CreateDirectory(path);
        SetJunction(path, target);
    }
    private static string Tree(string baseDirectory, string name, string inert)
    {
        var root = Path.Combine(baseDirectory, name); Directory.CreateDirectory(root);
        foreach (var image in Images) File.Copy(inert, Path.Combine(root, image));
        Directory.CreateDirectory(Path.Combine(root, "BepInEx", "plugins", "BetterAstralParty"));
        File.WriteAllBytes(Path.Combine(root, "BepInEx", "plugins", "BetterAstralParty", "BetterAstralParty.dll"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(root, "settings.keep"), "user setting");
        File.WriteAllText(Path.Combine(root, "other-mod.keep"), "unrelated mod");
        return root;
    }
    private static void Files(string root)
    {
        using (var fence = new WindowsFileFence(root))
        {
            fence.Recheck(); Check(fence.RootIdentity.FileId != 0, "root handle identity");
            foreach (var path in new[] { "../escape", "a/../escape", "a\\b", "/absolute", "C:/absolute", "file:ads", "CON", "NUL.txt", "COM1.txt", "LPT2", "CONIN$", "CONOUT$", "COM¹.txt", "LPT²", "a.", "a ", "a//b", "a/*", "a/\0" })
                Reject(() => fence.Full(path), "unsafe relative path", ApplyFailure.UnsafePath);
            fence.EnsureDirectory("stage/new");
            using (var staged = fence.OpenFile("stage/new/0000.bin", mutable: true, create: true))
            {
                staged.Stream.Write(new byte[] { 7, 8, 9 }, 0, 3); staged.Flush();
                Check(staged.Hash() == UpdateTrust.Hash(new byte[] { 7, 8, 9 }), "staged bytes hashed through lease");
                Reject(() => { using (fence.OpenFile("stage/new/0000.bin")) { } }, "duplicate exclusive file open", ApplyFailure.FileBusy);
                try { File.WriteAllText(staged.Path, "race"); throw new Exception("Concurrent write accepted"); } catch (IOException) { Check(true, "concurrent file write denied"); }
                try { File.Move(staged.Path, staged.Path + ".moved"); throw new Exception("Concurrent rename accepted"); } catch (IOException) { Check(true, "concurrent file rename denied"); }
                var oldIdentity = staged.Identity.Text;
                staged.RenameTo(fence, "stage/new/0001.bin");
                Check(staged.Identity.Text == oldIdentity && staged.Hash() == UpdateTrust.Hash(new byte[] { 7, 8, 9 }), "handle rename preserves verified bytes/identity");
                using (var occupied = fence.OpenFile("stage/new/occupied.bin", mutable: true, create: true))
                {
                    occupied.Stream.WriteByte(44); occupied.Flush();
                    Reject(() => staged.RenameTo(fence, "stage/new/occupied.bin"), "occupied name never overwritten", ApplyFailure.FileBusy);
                    Check(occupied.Hash() == UpdateTrust.Hash(new byte[] { 44 }) && staged.Hash() == UpdateTrust.Hash(new byte[] { 7, 8, 9 }), "rename failure preserves both files");
                }
            }
            using (var target = fence.OpenFile("BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll", mutable: true))
            {
                var parent = Path.Combine(root, "BepInEx", "plugins", "BetterAstralParty");
                try { Directory.Move(parent, parent + "-moved"); throw new Exception("Pinned directory rename accepted"); }
                catch (IOException) { Check(true, "ancestor rename blocked while lease held"); }
                target.Recheck(); Check(target.Hash() == UpdateTrust.Hash(new byte[] { 1, 2, 3 }), "unchanged owned file exact");
            }
            Reject(() => { using (new WindowsFileFence(root + "\\..")) { } }, "normalized root alias rejected", ApplyFailure.UnsafePath);
            Reject(() => { using (new WindowsFileFence(@"\\localhost\C$\Windows")) { } }, "UNC root rejected", ApplyFailure.UnsafePath);
        }
        Check(File.ReadAllText(Path.Combine(root, "settings.keep")) == "user setting" && File.ReadAllText(Path.Combine(root, "other-mod.keep")) == "unrelated mod", "settings/unrelated files untouched");
    }
    private static void Links(string baseDirectory, string root)
    {
        var existing = Path.Combine(root, "BepInEx", "plugins", "BetterAstralParty", "BetterAstralParty.dll");
        var alias = Path.Combine(root, "hard-alias.dll");
        Check(CreateHardLinkW(alias, existing, IntPtr.Zero), "fixture hard link created");
        using (var fence = new WindowsFileFence(root))
            Reject(() => { using (fence.OpenFile("BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll", true)) { } }, "hard link target refused", ApplyFailure.LinkedFile);
        var outside = Path.Combine(baseDirectory, "outside"); Directory.CreateDirectory(outside);
        var linked = Path.Combine(root, "linked"); Junction(linked, outside);
        using (var fence = new WindowsFileFence(root))
            Reject(() => fence.EnsureDirectory("linked/new"), "junction ancestor refused", ApplyFailure.LinkedFile);
        Check(!Directory.Exists(Path.Combine(outside, "new")), "junction outside tree unchanged");
        Reject(() => { using (new WindowsFileFence(linked)) { } }, "junction root refused", ApplyFailure.LinkedFile);
        var missing = Path.Combine(baseDirectory, "missing-outside"); var broken = Path.Combine(root, "broken");
        Directory.CreateDirectory(missing); Junction(broken, missing); Directory.Move(missing, missing + "-moved");
        using (var fence = new WindowsFileFence(root)) Reject(() => fence.EnsureDirectory("broken/new"), "broken junction refused before creation", ApplyFailure.LinkedFile);
        Check(!Directory.Exists(missing), "broken junction outside path never created");
    }
    private static void SetJunction(string directory, string target)
    {
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target); var printable = Encoding.Unicode.GetBytes(target);
        var buffer = new byte[16 + substitute.Length + printable.Length + 4];
        Array.Copy(BitConverter.GetBytes(0xa0000003U), 0, buffer, 0, 4);
        Array.Copy(BitConverter.GetBytes((ushort)(buffer.Length - 8)), 0, buffer, 4, 2);
        Array.Copy(BitConverter.GetBytes((ushort)substitute.Length), 0, buffer, 10, 2);
        Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length + 2)), 0, buffer, 12, 2);
        Array.Copy(BitConverter.GetBytes((ushort)printable.Length), 0, buffer, 14, 2);
        Array.Copy(substitute, 0, buffer, 16, substitute.Length); Array.Copy(printable, 0, buffer, 18 + substitute.Length, printable.Length);
        using (var handle = CreateFileW(directory, 0x40000000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero))
        {
            uint bytes;
            if (handle.IsInvalid || !DeviceIoControl(handle, 0x900a4, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out bytes, IntPtr.Zero))
                throw new Exception("Fixture in-place junction mutation failed: " + Marshal.GetLastWin32Error());
        }
    }
    private static void Races(string baseDirectory, string inert)
    {
        var root = Tree(baseDirectory, "open-race", inert); var outside = Path.Combine(baseDirectory, "race-outside"); Directory.CreateDirectory(outside);
        var armed = false;
        using (var fence = new WindowsFileFence(root, operation => { if (armed && operation == "open") { armed = false; SetJunction(Path.Combine(root, "empty"), outside); } }))
        {
            fence.EnsureDirectory("empty"); armed = true;
            Reject(() => { using (fence.OpenFile("empty/escape.bin", true, true)) { } }, "parent changed after validation before file create");
            Check(!File.Exists(Path.Combine(outside, "escape.bin")), "handle-relative create never reaches junction outside target");
        }
        var renameRoot = Tree(baseDirectory, "rename-race", inert); var renameOutside = Path.Combine(baseDirectory, "rename-outside"); Directory.CreateDirectory(renameOutside);
        var renameArmed = false;
        using (var fence = new WindowsFileFence(renameRoot, operation => { if (renameArmed && operation == "rename") { renameArmed = false; SetJunction(Path.Combine(renameRoot, "empty"), renameOutside); } }))
        {
            fence.EnsureDirectory("empty");
            using (var staged = fence.OpenFile("staged.bin", true, true))
            {
                staged.Stream.WriteByte(9); staged.Flush(); renameArmed = true;
                Reject(() => staged.RenameTo(fence, "empty/escape.bin"), "parent changed after validation before rename");
                Check(!File.Exists(Path.Combine(renameOutside, "escape.bin")), "handle-relative rename never reaches junction outside target");
            }
        }
    }
    private static void Transfer(string baseDirectory, string inert)
    {
        var sourceRoot = Tree(baseDirectory, "transfer-source", inert); var targetRoot = Tree(baseDirectory, "transfer-target", inert);
        using (var source = new WindowsFileFence(sourceRoot))
        using (var target = new WindowsFileFence(targetRoot))
        using (var bytes = source.OpenFile("transfer.bin", true, true))
        {
            bytes.Stream.WriteByte(88); bytes.Flush(); var identity = bytes.Identity.Text;
            bytes.RenameTo(target, "transfer.bin");
            Check(bytes.Identity.Text == identity && bytes.Hash() == UpdateTrust.Hash(new byte[] { 88 }), "cross-fence same-volume transfer binds exact bytes/identity");
            source.Dispose(); Check(bytes.Hash() == UpdateTrust.Hash(new byte[] { 88 }), "transferred lease uses destination fence");
        }
    }
    private static void Processes(string root, string other)
    {
        var game = Path.Combine(root, Images[0]); var otherGame = Path.Combine(other, Images[0]); var gameHash = UpdateTrust.Hash(File.ReadAllBytes(game));
        using (var child = Process.Start(new ProcessStartInfo { FileName = game, Arguments = "--wait-for-release", WorkingDirectory = root,
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true })!)
        using (var captured = WindowsProcessGuard.Capture((uint)child.Id, game, expectedSession: WindowsProcessGuard.CurrentSession))
        {
            try {
                Check(!captured.WaitForExit(1), "bounded held wait observes the unreleased inert process alive");
                bool rejected = false;
                try { captured.WaitForExit(5001); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "held exit wait rejects a timeout above its finite bound");
                child.StandardInput.WriteLine("release"); child.StandardInput.Flush();
                Check(captured.WaitForExit(5000) && captured.Exited, "bounded wait confirms natural exit on the original retained handle");
            } finally {
                if (!child.HasExited) { child.StandardInput.WriteLine("release"); child.StandardInput.Flush(); }
                child.StandardInput.Close(); Check(child.WaitForExit(5000) && child.ExitCode == 0, "bounded-wait inert child exited and was reaped");
            }
        }
        using (var running = Start(game, 700))
        {
            using (var captured = WindowsProcessGuard.Capture((uint)running.Id, game, expectedSession: WindowsProcessGuard.CurrentSession))
            {
                Check(!captured.Exited && captured.Creation > 0, "capture exact live process handle/time/session");
                Reject(() => { using (WindowsProcessGuard.Capture((uint)running.Id, game, captured.Creation + 1)) { } }, "PID reused/mismatched start time refused", ApplyFailure.ProcessUncertain);
                Reject(() => { using (WindowsProcessGuard.Capture((uint)running.Id, otherGame)) { } }, "same-name different image refused", ApplyFailure.ProcessUncertain);
                Reject(() => { using (WindowsProcessGuard.Capture((uint)running.Id, game, expectedSession: captured.Session + 1)) { } }, "wrong Windows session refused", ApplyFailure.ProcessUncertain);
                using (var fence = new WindowsFileFence(root))
                    Reject(() => { using (WindowsProcessGuard.Acquire(fence, Images)) { } }, "live exact game blocks apply", ApplyFailure.ProcessBusy);
                Check(running.WaitForExit(5000) && captured.Exited && running.ExitCode == 0, "captured original exits naturally");
            }
        }
        using (var unrelated = Start(otherGame, 1000))
        using (var fence = new WindowsFileFence(root))
        using (var guard = WindowsProcessGuard.Acquire(fence, Images))
        {
            guard.Recheck(); Check(!unrelated.HasExited, "same-name other install does not block exact guard");
            var borrowed = guard.BorrowImage(game);
            Check(ReferenceEquals(borrowed, guard.BorrowImage(game)) && borrowed.Hash() == gameHash, "exact held image is borrowed without reopening or transferring ownership");
            Reject(() => guard.BorrowImage(otherGame), "another installation cannot borrow the held image", ApplyFailure.InvalidState);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try { using (var started = Start(game, 0)) { started.WaitForExit(5000); } throw new Exception("EXE lease did not block CreateProcess"); }
                catch (System.ComponentModel.Win32Exception) { Check(true, "game restart blocked by OS executable lease"); }
            }
            try { using (var started = Start(Path.Combine(root, Images[1]), 0)) { started.WaitForExit(5000); } throw new Exception("Launcher restart accepted"); }
            catch (System.ComponentModel.Win32Exception) { Check(true, "launcher restart blocked by OS lease"); }
            guard.Recheck();
        }
        using (var restarted = Start(game, 0)) Check(restarted.WaitForExit(5000) && restarted.ExitCode == 0, "launch works again after lease release");
        using (var launcher = Start(Path.Combine(root, Images[1]), 500))
        using (var fence = new WindowsFileFence(root))
        { Reject(() => { using (WindowsProcessGuard.Acquire(fence, Images)) { } }, "owned launcher blocks apply", ApplyFailure.ProcessBusy); launcher.WaitForExit(5000); }
        var original=File.ReadAllBytes(game);File.Delete(game);
        try {
            using(var fence=new WindowsFileFence(root)) {
                Reject(()=>{using(WindowsProcessGuard.Acquire(fence,new[]{Images[0]})){}},"default update guard still rejects a missing required image");
                using(var removal=WindowsProcessGuard.Acquire(fence,new[]{Images[0]},allowMissing:true)) {
                    removal.Recheck();Check(!File.Exists(game),"removal guard never creates a placeholder game image");
                    File.WriteAllBytes(game,original);
                    Reject(()=>removal.Recheck(),"restored game executable stops missing-image removal",ApplyFailure.IdentityChanged);
                }
            }
        } finally {if(!File.Exists(game))File.WriteAllBytes(game,original);}
    }
    private static void QueryExitRaces(string baseDirectory, string inert)
    {
        foreach (var mode in new[] { "exited", "live", "wait-failed" })
        {
            var image = Path.Combine(baseDirectory, "bap-query-" + Guid.NewGuid().ToString("N") + ".exe"); File.Copy(inert, image);
            using (var child = Process.Start(new ProcessStartInfo { FileName = image, Arguments = "--wait-for-release", WorkingDirectory = baseDirectory,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true })!)
            {
                var priorQuery = WindowsProcessGuard.FixtureImageQuery; var priorWait = WindowsProcessGuard.FixtureQueryFailureWait;
                var queryFailure = new ApplySafetyException(ApplyFailure.ProcessUncertain, 5); SafeWaitHandle? queried = null; var queries = 0; var waits = 0;
                try
                {
                    WindowsProcessGuard.FixtureImageQuery = handle => {
                        queried = handle; queries++;
                        Check(WaitForSingleObject(handle, 0) == 258, "query fault begins with the exact held process still alive: " + mode);
                        if (mode == "exited") {
                            child.StandardInput.WriteLine("release"); child.StandardInput.Flush();
                            Check(WaitForSingleObject(handle, 5000) == 0, "controlled inert child exits naturally before its image query fails");
                        }
                        throw queryFailure;
                    };
                    WindowsProcessGuard.FixtureQueryFailureWait = handle => {
                        waits++; Check(ReferenceEquals(handle, queried), "query failure checks the same held handle without PID reopening: " + mode);
                        var actual = WaitForSingleObject(handle, 0);
                        Check(actual == (mode == "exited" ? 0U : 258U), "held exit status is deterministic: " + mode);
                        return mode == "wait-failed" ? uint.MaxValue : actual;
                    };
                    ApplySafetyException? observed = null;
                    try { WindowsProcessGuard.AssertNoRunning(new[] { image }); } catch (ApplySafetyException error) { observed = error; }
                    Check(queries == 1 && waits == 1, "query fault and held-exit check each run once without retries: " + mode);
                    Check(mode == "exited" ? observed == null : observed != null && ReferenceEquals(observed, queryFailure) && observed.NativeError == 5,
                        "only confirmed exit is ignored; live and WAIT_FAILED preserve the original query error: " + mode);
                }
                finally
                {
                    WindowsProcessGuard.FixtureImageQuery = priorQuery; WindowsProcessGuard.FixtureQueryFailureWait = priorWait;
                    if (!child.HasExited) { child.StandardInput.WriteLine("release"); child.StandardInput.Flush(); }
                    child.StandardInput.Close(); Check(child.WaitForExit(5000) && child.ExitCode == 0, "controlled query-race child exited and was reaped: " + mode);
                }
            }
        }
    }
    private static void ObserveQueryExitRaces(string baseDirectory, string inert)
    {
        foreach (var mode in new[] { "delayed-exit", "live", "default", "strict", "other-error" }) {
            var image = Path.Combine(baseDirectory, "bap-observe-" + Guid.NewGuid().ToString("N") + ".exe"); File.Copy(inert, image);
            using (var child = Process.Start(new ProcessStartInfo { FileName = image, Arguments = "--wait-for-release", WorkingDirectory = baseDirectory,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true })!) {
                var priorQuery = WindowsProcessGuard.FixtureImageQuery;
                var queryFailure = new ApplySafetyException(ApplyFailure.ProcessUncertain, mode == "other-error" ? 6 : 5);
                System.Threading.Tasks.Task? release = null; var queries = 0;
                try {
                    WindowsProcessGuard.FixtureImageQuery = handle => {
                        queries++; Check(WaitForSingleObject(handle, 0) == 258, "observe query fails while the original held process is alive: " + mode);
                        if (mode == "delayed-exit") release = System.Threading.Tasks.Task.Run(() => {
                            System.Threading.Thread.Sleep(50); child.StandardInput.WriteLine("release"); child.StandardInput.Flush();
                        });
                        throw queryFailure;
                    };
                    ApplySafetyException? observed = null; var timer = Stopwatch.StartNew();
                    try {
                        using (var captured = WindowsProcessGuard.Observe((uint)child.Id, mode != "strict", mode == "strict" || mode == "default" ? 0U : 2000U))
                            Check(mode == "delayed-exit" && captured.Exited, "only a confirmed exit on the original handle resolves failed capture");
                    } catch (ApplySafetyException error) { observed = error; }
                    timer.Stop(); Check(queries == 1, "observe does not retry its failed metadata query: " + mode);
                    Check(mode == "delayed-exit" ? observed == null : ReferenceEquals(observed, queryFailure) && !child.HasExited,
                        "live, strict and other-error capture failures preserve the original error: " + mode);
                    if (mode == "default" || mode == "strict" || mode == "other-error") Check(timer.ElapsedMilliseconds < 1500, "ordinary scans and unrelated errors do not wait two seconds: " + mode);
                    bool rejected = false;
                    try { WindowsProcessGuard.Observe((uint)child.Id, true, 2001); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Check(rejected, "capture wait has a finite two-second bound");
                    rejected = false;
                    try { WindowsProcessGuard.Observe((uint)child.Id, false, 1); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Check(rejected, "strict capture cannot enable an exit fallback");
                } finally {
                    WindowsProcessGuard.FixtureImageQuery = priorQuery; release?.GetAwaiter().GetResult();
                    if (!child.HasExited) { child.StandardInput.WriteLine("release"); child.StandardInput.Flush(); }
                    child.StandardInput.Close(); Check(child.WaitForExit(5000) && child.ExitCode == 0, "observe-race inert child exited naturally and was reaped: " + mode);
                }
            }
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new Exception("Fixture needs output base and inert executable");
            var baseDirectory = UpdaterFixtureScope.Require(args[0]); var inert = Path.GetFullPath(args[1]);
            if (!baseDirectory.EndsWith("\\fixtures", StringComparison.OrdinalIgnoreCase) || Directory.Exists(baseDirectory))
                throw new Exception("Only a fresh isolated fixture tree is allowed");
            Directory.CreateDirectory(baseDirectory);
            var root = Tree(baseDirectory, "install", inert); var other = Tree(baseDirectory, "other-install", inert);
            Files(root); QueryExitRaces(baseDirectory, inert); ObserveQueryExitRaces(baseDirectory, inert); Processes(root, other); Links(baseDirectory, root); Races(baseDirectory, inert); Transfer(baseDirectory, inert);
            Console.WriteLine("PASS: apply boundary " + _checks + " assertions; real Windows NTFS handles/rename/EXE restart guard, inert processes only; no production installation or force-close.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (error is ApplySafetyException safety) Console.Error.WriteLine("Fixture native error: " + safety.NativeError);
            return 1;
        }
    }
}
