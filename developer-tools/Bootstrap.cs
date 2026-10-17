using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

// Compiled by Windows Framework csc before the first SDK process starts.
internal static class DeveloperBootstrap
{
    private const int SdkPreparationTimeoutMilliseconds = 900000;
    private const int DeveloperHostTimeoutMilliseconds = 3600000;
    private const int ChildCleanupTimeoutMilliseconds = 30000;
    private const string WorkerMode = "--job-worker";
    private const string JobPrefix = @"Local\BetterAstralParty.Developer-";
    private const string StartPrefix = "BAP-DEVELOPER-START:";

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        internal BasicLimit Basic;
        internal IoCounters Io;
        internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        internal long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        internal uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimit information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, out BasicAccounting information, uint size, IntPtr returnedLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenJobObjectW(uint access, bool inheritHandle, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, SafeFileHandle job, out bool member);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static string Quote(string value)
    {
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("Invalid process argument.");
        var text = new StringBuilder("\""); var slashes = 0;
        foreach (var character in value) {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes); slashes = 0;
            text.Append(character);
        }
        text.Append('\\', slashes * 2); return text.Append('"').ToString();
    }
    private static string Paths()
    {
        var text = new StringBuilder();
        foreach (var machine in new[] { false, true }) {
            var hive = machine ? Registry.LocalMachine : Registry.CurrentUser;
            using (var key = hive.OpenSubKey(machine ? @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment" : "Environment", false)) {
                var exists = key != null && Array.Exists(key.GetValueNames(), name => name.Equals("Path", StringComparison.OrdinalIgnoreCase));
                text.Append(hive.Name).Append('\0').Append(exists).Append('\0');
                if (exists) text.Append(key.GetValueKind("Path")).Append('\0').Append(key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                text.Append('\0');
            }
        }
        return text.ToString();
    }
    private static ProcessStartInfo ChildStart(string executable, IEnumerable<string> args, string root, string stage)
    {
        var command = new StringBuilder(); foreach (var argument in args) command.Append(Quote(argument)).Append(' ');
        var start = new ProcessStartInfo(executable, command.ToString()) { UseShellExecute = false, WorkingDirectory = root, CreateNoWindow = true, RedirectStandardInput = true };
        start.EnvironmentVariables["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        start.EnvironmentVariables["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        start.EnvironmentVariables["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.EnvironmentVariables["DOTNET_CLI_HOME"] = Path.Combine(stage, "dotnet-home");
        start.EnvironmentVariables["NUGET_PACKAGES"] = Path.Combine(stage, "packages");
        return start;
    }
    private static void Signal(ManualResetEvent done)
    {
        try { done.Set(); } catch (ObjectDisposedException) { }
    }
    private static int Remaining(Stopwatch clock)
    {
        return Math.Max(0, ChildCleanupTimeoutMilliseconds - (int)Math.Min(clock.ElapsedMilliseconds, ChildCleanupTimeoutMilliseconds));
    }
    private static void Cleanup(SafeFileHandle job, Process process, bool started, bool assigned, ManualResetEvent output, ManualResetEvent error)
    {
        var clock = Stopwatch.StartNew(); Exception failure = null;
        if (assigned) {
            if (!TerminateJobObject(job, 1)) failure = new Win32Exception(Marshal.GetLastWin32Error(), "Could not terminate the owned developer job.");
        } else if (started) {
            // An unreleased worker cannot start a child; only its held process can need direct cleanup.
            try { process.StandardInput.Close(); if (!process.WaitForExit(0)) process.Kill(); }
            catch (Exception problem) { failure = problem; }
        }
        try {
            if (started && !process.WaitForExit(Remaining(clock))) throw new TimeoutException("Developer worker cleanup timed out.");
            BasicAccounting accounting;
            while (true) {
                if (!QueryInformationJobObject(job, 1, out accounting, (uint)Marshal.SizeOf(typeof(BasicAccounting)), IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not verify owned developer job cleanup.");
                if (accounting.ActiveProcesses == 0) break;
                if (Remaining(clock) == 0) throw new TimeoutException("Developer descendant cleanup timed out.");
                Thread.Sleep(Math.Min(25, Remaining(clock)));
            }
            if (started && !WaitHandle.WaitAll(new WaitHandle[] { output, error }, Remaining(clock))) throw new TimeoutException("Developer output cleanup timed out.");
        } catch (Exception problem) { if (failure == null) failure = problem; }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static int Run(string executable, IEnumerable<string> args, string root, string stage, int timeoutMilliseconds)
    {
        if (timeoutMilliseconds < 1 || timeoutMilliseconds > DeveloperHostTimeoutMilliseconds) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
        var name = JobPrefix + Guid.NewGuid().ToString("N"); var nonce = Guid.NewGuid().ToString("N");
        using (var job = CreateJobObjectW(IntPtr.Zero, name)) {
            var createError = Marshal.GetLastWin32Error();
            if (job.IsInvalid || createError == 183) throw new Win32Exception(createError, "A fresh developer job is required.");
            var limits = new ExtendedLimit(); limits.Basic.Flags = 0x2000; // Kill only this job's tree when its owner closes.
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimit))))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set owned developer job limits.");
            var invoke = new List<string> { WorkerMode, name, nonce, executable, root, stage }; invoke.AddRange(args);
            var start = ChildStart(typeof(DeveloperBootstrap).Assembly.Location, invoke, root, stage);
            start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8;
            using (var output = new ManualResetEvent(false))
            using (var error = new ManualResetEvent(false))
            using (var process = new Process { StartInfo = start }) {
                var started = false; var assigned = false; Exception failure = null; Exception outputFailure = null; var result = 1;
                process.OutputDataReceived += (sender, value) => {
                    if (value.Data == null) { Signal(output); return; }
                    try { Console.Out.WriteLine(value.Data); } catch (Exception problem) { Interlocked.CompareExchange(ref outputFailure, problem, null); }
                };
                process.ErrorDataReceived += (sender, value) => {
                    if (value.Data == null) { Signal(error); return; }
                    try { Console.Error.WriteLine(value.Data); } catch (Exception problem) { Interlocked.CompareExchange(ref outputFailure, problem, null); }
                };
                try {
                    if (!process.Start()) throw new IOException("Could not start the developer worker.");
                    started = true; process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if (!AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not contain the developer worker; SDK execution refused.");
                    assigned = true;
                    process.StandardInput.WriteLine(StartPrefix + nonce); process.StandardInput.Flush(); process.StandardInput.Close();
                    if (!process.WaitForExit(timeoutMilliseconds)) throw new TimeoutException("Developer bootstrap child timed out.");
                    result = process.ExitCode;
                } catch (Exception problem) { failure = problem; }
                finally {
                    try { Cleanup(job, process, started, assigned, output, error); }
                    catch (Exception problem) {
                        Console.Error.WriteLine("Developer bootstrap cleanup failed: " + problem.Message);
                        if (failure == null) failure = problem;
                    }
                }
                if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
                if (outputFailure != null) throw new IOException("Developer output could not be recorded.", outputFailure);
                return result;
            }
        }
    }
    private static int Worker(string[] args)
    {
        string before = null, processPath = null, previous = null; Exception failure = null; var result = 1;
        try {
            Console.OutputEncoding = new UTF8Encoding(false);
            Guid parsed;
            if (args.Length < 6 || args[0] != WorkerMode || !args[1].StartsWith(JobPrefix, StringComparison.Ordinal)
                || !Guid.TryParseExact(args[1].Substring(JobPrefix.Length), "N", out parsed) || !Guid.TryParseExact(args[2], "N", out parsed))
                throw new ArgumentException("Invalid developer worker handshake.");
            var expected = StartPrefix + args[2]; var input = new StringBuilder();
            // Read bytes, not characters: under code page 65001 (GitHub Actions cmd) the owner's stdin writer
            // prepends a UTF-8 BOM, which a non-UTF-8 worker decoder would merge into the first release character.
            var stdin = Console.OpenStandardInput(); var position = 0; var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            while (true) {
                var value = stdin.ReadByte();
                if (value < 0) throw new IOException("Developer worker was not released by its owner.");
                if (position < 3 && input.Length == 0 && value == bom[position]) { position++; continue; }
                if (position > 0 && position < 3 && input.Length == 0) throw new IOException("Invalid developer worker release.");
                position = 3;
                if (value == '\n') break;
                if (value > 0x7F || input.Length >= expected.Length + 1) throw new IOException("Invalid developer worker release.");
                input.Append((char)value);
            }
            if (input.ToString() != expected && input.ToString() != expected + "\r") throw new IOException("Invalid developer worker release.");
            // Close the query handle before SDK start so only the supervisor owns kill-on-close lifetime.
            using (var job = OpenJobObjectW(0x0004, false, args[1])) {
                bool member;
                if (job.IsInvalid || !IsProcessInJob(GetCurrentProcess(), job, out member) || !member)
                    throw new IOException("Developer worker is not in its owner's job; SDK execution refused.");
            }
            before = Paths(); processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
            previous = Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "false", EnvironmentVariableTarget.Process);
            var invoke = new List<string>(); for (var index = 6; index < args.Length; index++) invoke.Add(args[index]);
            using (var process = new Process { StartInfo = ChildStart(args[3], invoke, args[4], args[5]) }) {
                if (!process.Start()) throw new IOException("Could not start the existing SDK.");
                process.StandardInput.Close();
                // The supervisor's job and deadline cover this SDK process and every descendant.
                process.WaitForExit(); result = process.ExitCode;
            }
        } catch (Exception problem) { failure = problem; Console.Error.WriteLine("Developer worker failed: " + problem); }
        finally {
            if (before != null) {
                Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", previous, EnvironmentVariableTarget.Process);
                try {
                    if (!String.Equals(before, Paths(), StringComparison.Ordinal) || !String.Equals(processPath, Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process), StringComparison.Ordinal))
                        throw new InvalidOperationException("PATH changed; stop and inspect it. It was not automatically overwritten.");
                } catch (Exception problem) { Console.Error.WriteLine("Developer worker PATH check failed: " + problem.Message); if (failure == null) result = 1; }
            }
        }
        return result;
    }
    private static string[] Sources(string root)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var folder in new[] { "developer-tools", "updater", "src/Observability" })
            foreach (var path in Directory.GetFiles(Path.Combine(root, folder), "*.cs")) paths.Add(path);
        foreach (var relative in new[] { "developer-tools/BetterAstralParty.Developer.csproj", "tools/DevelopmentPackageFiles.cs",
            "tools/GaPackageContract.cs", "tools/SignLocalInstall.cs", "tools/SignRelease.cs", "diagnostics/MinimalDiagnosticProjection.cs",
            ".deps/bepinex/BepInEx/core/Mono.Cecil.dll", "NuGet.Config", "Directory.Build.props", "global.json" })
            paths.Add(Path.GetFullPath(Path.Combine(root, relative)));
        return new List<string>(paths).ToArray();
    }
    private static int Main(string[] args)
    {
        if (args.Length != 0 && args[0] == WorkerMode) return Worker(args);
        string before = null, processPath = null, previous = null; Exception failure = null; var result = 1;
        var sources = new List<FileStream>();
        try {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length < 1) throw new ArgumentException("Canonical project root is required.");
            var root = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
            var project = Path.Combine(root, "developer-tools", "BetterAstralParty.Developer.csproj");
            var dotnet = Path.Combine(root, ".dotnet", "dotnet.exe");
            if (!File.Exists(project) || !File.Exists(Path.Combine(root, "src", "Plugin.cs")) || !File.Exists(dotnet))
                throw new FileNotFoundException("Developer source and the existing project SDK are required; nothing is downloaded.");
            var stage = Path.Combine(root, "artifacts", "developer-host-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(stage) || File.Exists(stage)) throw new IOException("Fresh developer output is required.");
            var artifacts = Path.Combine(root, "artifacts");
            if ((Directory.Exists(artifacts) && (File.GetAttributes(artifacts) & FileAttributes.ReparsePoint) != 0)
                || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked developer output refused.");
            before = Paths(); processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
            previous = Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "false", EnvironmentVariableTarget.Process);
            Directory.CreateDirectory(stage);
            var sourcePaths = Sources(root);
            foreach (var path in sourcePaths) sources.Add(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var obj = Path.Combine(stage, "obj") + Path.DirectorySeparatorChar;
            var properties = new[] { "-p:BaseIntermediateOutputPath=" + obj, "-p:MSBuildProjectExtensionsPath=" + obj, "-p:UseSharedCompilation=false", "-p:NuGetAudit=false" };
            var restore = new List<string> { "restore", project, "--configfile", Path.Combine(root, "NuGet.Config") }; restore.AddRange(properties);
            var exit = Run(dotnet, restore, root, stage, SdkPreparationTimeoutMilliseconds); if (exit != 0) return exit;
            var host = Path.Combine(stage, "host");
            var build = new List<string> { "build", project, "--no-restore", "-c", "Release", "-o", host }; build.AddRange(properties);
            exit = Run(dotnet, build, root, stage, SdkPreparationTimeoutMilliseconds); if (exit != 0) return exit;
            if (String.Join("\n", sourcePaths) != String.Join("\n", Sources(root))) throw new IOException("Developer compile inputs changed; host execution is refused.");
            if (!String.Equals(before, Paths(), StringComparison.Ordinal)) throw new InvalidOperationException("SDK changed persistent PATH; host execution is refused.");
            var invoke = new List<string> { Path.Combine(host, "BetterAstralParty.Developer.dll"), "--project-root", root };
            for (var index = 1; index < args.Length; index++) invoke.Add(args[index]);
            result = Run(dotnet, invoke, root, stage, DeveloperHostTimeoutMilliseconds);
        } catch (Exception error) { failure = error; Console.Error.WriteLine("Developer bootstrap failed: " + error); }
        finally {
            foreach (var source in sources) source.Dispose();
            if (before != null) {
                Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", previous, EnvironmentVariableTarget.Process);
                try {
                    if (!String.Equals(before, Paths(), StringComparison.Ordinal) || !String.Equals(processPath, Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process), StringComparison.Ordinal))
                        throw new InvalidOperationException("PATH changed; stop and inspect it. It was not automatically overwritten.");
                } catch (Exception error) {
                    Console.Error.WriteLine("Developer bootstrap PATH check failed: " + error.Message);
                    if (failure == null) result = 1;
                }
            }
        }
        return result;
    }
}
