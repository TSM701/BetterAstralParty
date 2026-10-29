global using System;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

internal static class NativeBootstrapFixture
{
    private const string Marker = "BetterAstralParty.BootstrapLifetimeFixture/v1\n";
    private static int _checks;
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static string Image => Assembly.GetExecutingAssembly().Location;
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); _checks++; }
    private static object? Invoke(string name, params object[] args)
    {
        try { return typeof(DeveloperBootstrap).GetMethod(name, PrivateStatic)!.Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    private static string Quote(string value) => (string)Invoke("Quote", value)!;
    private static string PersistentPaths() => (string)Invoke("Paths")!;
    private static string Boundary(string path)
    {
        var current = Path.GetFullPath(path);
        for (var depth = 0; depth < 6; depth++) {
            Check((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Synthetic fixture ancestors are regular directories");
            var marker = Path.Combine(current, ".bap-bootstrap-fixture");
            if (File.Exists(marker)) {
                Check(File.ReadAllText(marker) == Marker && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0, "Exact inert bootstrap boundary marker");
                return current;
            }
            current = Path.GetDirectoryName(current) ?? throw new InvalidOperationException("Synthetic fixture marker missing.");
        }
        throw new InvalidOperationException("Synthetic fixture boundary missing.");
    }
    private static void InertImage(string image, string root)
    {
        var boundary = Boundary(root); var target = Path.GetFullPath(image);
        Check(target.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && (File.GetAttributes(target) & FileAttributes.ReparsePoint) == 0, "Only exact descendant inert executables may run");
        using var hash = SHA256.Create();
        Check(hash.ComputeHash(File.ReadAllBytes(target)).SequenceEqual(hash.ComputeHash(File.ReadAllBytes(Image))), "Executable bytes match this inert fixture");
    }
    private static void NewText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var bytes = new UTF8Encoding(false).GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
    private static bool Until(Func<bool> condition, int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        do { if (condition()) return true; Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds);
        return condition();
    }
    private static void AliveMarker(string path)
    {
        using var current = Process.GetCurrentProcess();
        NewText(path, current.Id + "\t" + current.StartTime.ToUniversalTime().ToFileTimeUtc());
    }
    private static Process Observed(string path)
    {
        var fields = File.ReadAllText(path).Split('\t'); var process = Process.GetProcessById(int.Parse(fields[0]));
        _ = process.Handle;
        Check(process.StartTime.ToUniversalTime().ToFileTimeUtc() == long.Parse(fields[1])
            && string.Equals(process.MainModule!.FileName, Image, StringComparison.OrdinalIgnoreCase), "Observed handle binds the exact inert executable and creation time");
        return process;
    }
    private static int Inert(string[] args)
    {
        Check(args.Length == 3, "Exact inert role arguments"); var root = Path.GetFullPath(args[1]); InertImage(Image, root);
        if (args[0] == "--inert-marker") {
            Check(Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH") == "false", "SDK guard is present inside the inert child");
            NewText(Path.Combine(root, args[2]), "started"); return 37;
        }
        if (args[0] == "--inert-leaf") { AliveMarker(Path.Combine(root, "leaf.ready")); Thread.Sleep(8000); return 0; }
        Check(args[0] == "--inert-tree" && args[2] is "normal" or "timeout", "Known inert tree role only");
        using var leaf = Process.Start(new ProcessStartInfo(Image, Quote("--inert-leaf") + " " + Quote(root) + " " + Quote("leaf")) { UseShellExecute = false, CreateNoWindow = true })!;
        AliveMarker(Path.Combine(root, "parent.ready"));
        if (args[2] == "normal") Check(Until(() => File.Exists(Path.Combine(root, "release-parent")), 5000), "Parent release is bounded");
        else Thread.Sleep(8000);
        return 37;
    }
    private static int SdkSubstitute(string[] args)
    {
        var root = Directory.GetParent(Path.GetDirectoryName(Image)!)!.FullName; Boundary(root); InertImage(Image, root);
        Check(args.Length > 0 && (args[0] is "restore" or "build" || Path.GetFileName(args[0]) == "BetterAstralParty.Developer.dll"), "Only the three inert SDK phases are accepted");
        Check(Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH") == "false", "Every bootstrap SDK phase disables global tool PATH writes");
        File.AppendAllText(Path.Combine(root, "sdk-observed"), (args[0] is "restore" or "build" ? args[0] : "host") + "\n");
        return File.Exists(Path.Combine(root, "sdk-fail")) ? 23 : 0;
    }
    private static void Policies()
    {
        foreach (var row in new[] { ("SdkPreparationTimeoutMilliseconds", 900000), ("DeveloperHostTimeoutMilliseconds", 3600000), ("ChildCleanupTimeoutMilliseconds", 30000) })
            Check((int)typeof(DeveloperBootstrap).GetField(row.Item1, PrivateStatic)!.GetRawConstantValue()! == row.Item2, "Separate bounded timeout policy: " + row.Item1);
    }
    private sealed class Result(int code, Exception? error) { internal readonly int Code = code; internal readonly Exception? Error = error; }
    private static void Trees(string boundary, string mode)
    {
        var root = Path.Combine(boundary, "tree-" + mode); Directory.CreateDirectory(root);
        var started = Stopwatch.StartNew();
        var run = Task.Run(() => {
            try { return new Result((int)Invoke("Run", Image, new[] { "--inert-tree", root, mode }, root, root, mode == "timeout" ? 4000 : 10000)!, null); }
            catch (Exception error) { return new Result(-1, error); }
        });
        Check(Until(() => File.Exists(Path.Combine(root, "parent.ready")) && File.Exists(Path.Combine(root, "leaf.ready")), 3000), "Both owned generations started before the test deadline");
        using var parent = Observed(Path.Combine(root, "parent.ready")); using var leaf = Observed(Path.Combine(root, "leaf.ready"));
        if (mode == "normal") NewText(Path.Combine(root, "release-parent"), "release");
        Check(run.Wait(12000), "Bootstrap Run returns boundedly: " + mode);
        Check(mode == "normal" ? run.Result.Code == 37 && run.Result.Error == null : run.Result.Error is TimeoutException,
            "Normal exit code or original timeout remains observable: " + mode);
        Check(parent.WaitForExit(1000) && leaf.WaitForExit(1000) && started.ElapsedMilliseconds < 7000, "Job cleanup ends held parent and grandchild before natural inert exit: " + mode);
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { internal long ProcessTime, JobTime; internal uint Flags; internal UIntPtr Minimum, Maximum; internal uint Active; internal UIntPtr Affinity; internal uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounts { internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { internal BasicLimits Basic; internal IoCounts Io; internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static void Handshake(string boundary, string mode)
    {
        var root = Path.Combine(boundary, "handshake-" + mode); Directory.CreateDirectory(root);
        var nonce = Guid.NewGuid().ToString("N"); var jobName = @"Local\BetterAstralParty.Developer-" + nonce;
        var job = CreateJobObjectW(IntPtr.Zero, jobName); Check(job != IntPtr.Zero, "Fresh fixture-only Job created");
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        Process? worker = null;
        try {
            Check(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))), "Fixture containment uses kill-on-close, never PID termination");
            var arguments = new[] { "--job-worker", jobName, nonce, Image, root, root, "--inert-marker", root, "sdk.started" };
            var start = new ProcessStartInfo(Image, string.Join(" ", arguments.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            worker = Process.Start(start)!; var stdout = worker.StandardOutput.ReadToEndAsync(); var stderr = worker.StandardError.ReadToEndAsync();
            if (mode != "unassigned") Check(AssignProcessToJobObject(job, worker.Handle), "Worker is assigned before any start authorization");
            Thread.Sleep(150);
            Check(!worker.HasExited && !File.Exists(Path.Combine(root, "sdk.started")), "No SDK starts while handshake is withheld");
            if (mode != "eof") worker.StandardInput.WriteLine("BAP-DEVELOPER-START:" + (mode == "wrong" ? new string('0', 32) : nonce));
            worker.StandardInput.Close();
            Check(worker.WaitForExit(5000), "Worker handshake completes or rejects boundedly");
            Check(mode == "valid" ? worker.ExitCode == 37 && File.Exists(Path.Combine(root, "sdk.started")) : worker.ExitCode != 0 && !File.Exists(Path.Combine(root, "sdk.started")),
                "Exact nonce and Job assignment are required before SDK start: " + mode + " " + stderr.Result);
            _ = stdout.Result;
        } finally {
            if (worker != null) worker.StandardInput.Close();
            CloseHandle(job);
            if (worker != null) { worker.WaitForExit(5000); worker.Dispose(); }
        }
    }
    private static void MainRestoration(string boundary)
    {
        var original = Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"); var path = Environment.GetEnvironmentVariable("PATH"); var persistent = PersistentPaths();
        string Synthetic(string name, bool fail)
        {
            var root = Path.Combine(boundary, name); Directory.CreateDirectory(root);
            foreach (var folder in new[] { "developer-tools", "updater", "src/Observability", ".dotnet" }) Directory.CreateDirectory(Path.Combine(root, folder));
            NewText(Path.Combine(root, "developer-tools/BetterAstralParty.Developer.csproj"), "inert project only"); NewText(Path.Combine(root, "src/Plugin.cs"), "inert source only");
            foreach (var source in (string[])Invoke("Sources", root)!) if (!File.Exists(source)) NewText(source, "inert compile input only");
            File.Copy(Image, Path.Combine(root, ".dotnet/dotnet.exe")); if (fail) NewText(Path.Combine(root, "sdk-fail"), "fail first SDK phase");
            return root;
        }
        int Hosts(string root) => Directory.GetDirectories(Path.Combine(root, "artifacts"), "developer-host-*").Length;
        try {
            foreach (var initial in new string?[] { null, "true", "false", "caller-value" }) foreach (var fail in new[] { false, true }) {
                var root = Synthetic("main-" + _checks, fail);
                Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", initial);
                var code = (int)Invoke("Main", new object[] { new[] { root, "test", "inert-only" } })!;
                Check(code == (fail ? 23 : 0), "Actual bootstrap Main preserves inert SDK success/failure status");
                Check(File.ReadAllLines(Path.Combine(root, "sdk-observed")).SequenceEqual(fail ? new[] { "restore" } : new[] { "restore", "build", "host" }), "Only the expected inert SDK phases executed");
                Check(Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH") == initial && Environment.GetEnvironmentVariable("PATH") == path && PersistentPaths() == persistent,
                    "Actual bootstrap Main restores caller setting and leaves process/persistent PATH unchanged on success/failure");
                Check(Hosts(root) == (fail ? 1 : 0), "A successful run removes its developer host; a failed run keeps it");
            }
            var pinned = Synthetic("main-pinned", false);
            Check((int)Invoke("Main", new object[] { new[] { pinned, "prepare-signing-inputs" } })! == 0 && Hosts(pinned) == 1,
                "The host prepare-signing-inputs pins for the signing steps stays after success");
        } finally { Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", original); }
    }
    private static int Main(string[] args)
    {
        try {
            if (args.Length > 0 && args[0] == "--job-worker") {
                Check(args.Length >= 7, "Exact bootstrap worker tuple"); InertImage(args[3], args[4]);
                return (int)Invoke("Main", new object[] { args })!;
            }
            if (Path.GetFileName(Image).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return SdkSubstitute(args);
            if (args.Length > 0 && args[0].StartsWith("--inert-", StringComparison.Ordinal)) return Inert(args);
            Check(args.Length == 1, "One explicit synthetic fixture root"); var root = Path.GetFullPath(args[0]);
            Check(root == Path.GetDirectoryName(Image) && Boundary(root) == root, "Fixture executable and marker share the direct artifact root");
            Policies(); foreach (var mode in new[] { "valid", "eof", "wrong", "unassigned" }) Handshake(root, mode);
            Trees(root, "normal"); Trees(root, "timeout"); MainRestoration(root);
            Console.WriteLine("PASS: actual Framework bootstrap lifecycle " + _checks + " checks; inert descendants and SDK substitute only; no PID kill, signing or installation."); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
