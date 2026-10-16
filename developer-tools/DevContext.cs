using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace BetterAstralParty.Development;

internal sealed class DevContext
{
    private static readonly object SdkEnvironmentLock = new();
    internal string Root { get; }
    internal string DotnetRoot { get; }
    internal string NativeAssemblyPath { get; }
    internal bool NativeAssemblyExplicit { get; }
    private readonly Action<string>? _nativeInputObserver;

    internal DevContext(string root, string? dotnetRoot = null, string? nativeAssembly = null, Action<string>? nativeInputObserver = null)
    {
        Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(Root, "src", "BetterAstralParty.csproj")) || !Directory.Exists(Path.Combine(Root, "updater")))
            throw new InvalidDataException("The canonical BetterAstralParty project is required.");
        DotnetRoot = Path.GetFullPath(dotnetRoot ?? Path.Combine(Root, ".dotnet"));
        if (nativeAssembly != null && string.IsNullOrWhiteSpace(nativeAssembly)) throw new ArgumentException("An explicit native assembly path cannot be blank.");
        NativeAssemblyExplicit = nativeAssembly != null;
        NativeAssemblyPath = Path.GetFullPath(nativeAssembly ?? Path.Combine(Root, ".research", "extracted", "AstralParty.Runtime.dll.dll"));
        _nativeInputObserver = nativeInputObserver;
    }

    internal DevContext WithNativeAssembly(string path) => new(Root, DotnetRoot, path, _nativeInputObserver);
    internal DevContext WithNativeInputObserver(Action<string> observer) => new(Root, DotnetRoot, NativeAssemblyExplicit ? NativeAssemblyPath : null, observer);
    internal void ObserveNativeAssembly() => _nativeInputObserver?.Invoke(NativeAssemblyPath);

    internal FileStream OpenNativeAssemblyInput()
    {
        var file = File.Open(NativeAssemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try {
            if (file.Length == 0 || file.Length > 512L * 1024 * 1024 || (File.GetAttributes(NativeAssemblyPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A bounded regular native metadata assembly is required.");
            return file;
        } catch { file.Dispose(); throw; }
    }

    internal int Run(string exe, IEnumerable<string> args, string? cwd = null, IReadOnlyDictionary<string, string?>? env = null,
        int timeoutMilliseconds = 900000, string? logPath = null, bool closeInput = true)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        if (name.Equals("powershell", StringComparison.OrdinalIgnoreCase) || name.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Developer tools do not execute PowerShell.");
        var arguments = args.ToArray();
        if (arguments.Any(argument => argument.IndexOf('\0') >= 0)) throw new ArgumentException("NUL is not a process argument.");
        if (timeoutMilliseconds < 1 || timeoutMilliseconds > 3600000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        var logFile = logPath == null ? null : FreshArtifactOutput(logPath);
        var result = -1;
        void Execute()
        {
            var start = new ProcessStartInfo(exe) {
                WorkingDirectory = cwd ?? Root, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = closeInput, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            if (env != null) foreach (var pair in env) {
                if (pair.Value == null) start.Environment.Remove(pair.Key);
                else start.Environment[pair.Key] = pair.Value;
            }
            if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) {
                start.Environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
                start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            }
            if (logFile != null) Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            using var log = logFile == null ? null : new StreamWriter(new FileStream(logFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            var logLock = new object(); var logBytes = 0L; var truncated = false; Exception? logFailure = null;
            void Line(string? value, bool error)
            {
                if (value == null) return;
                if (error) Console.Error.WriteLine(value); else Console.Out.WriteLine(value);
                if (log == null) return;
                lock (logLock) {
                    if (logFailure != null) return;
                    var bytes = Encoding.UTF8.GetByteCount(value) + 1;
                    try {
                        if (logBytes + bytes <= 16 * 1024 * 1024) { log.WriteLine(value); logBytes += bytes; }
                        else if (!truncated) { log.WriteLine("[Developer process log truncated at 16 MiB]"); truncated = true; }
                    } catch (Exception writeError) { logFailure = writeError; }
                }
            }
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, value) => Line(value.Data, false);
            process.ErrorDataReceived += (_, value) => Line(value.Data, true);
            if (!process.Start()) throw new IOException("Could not start " + Path.GetFileName(exe));
            if (closeInput) process.StandardInput.Close();
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMilliseconds)) {
                process.Kill(entireProcessTree: true); process.WaitForExit();
                throw new TimeoutException("Developer child process timed out: " + Path.GetFileName(exe));
            }
            process.WaitForExit(); result = process.ExitCode;
            if (logFailure != null) throw new IOException("Developer process log could not be completed.", logFailure);
        }
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) GuardSdkEnvironment(Execute);
        else Execute();
        return result;
    }

    internal static string PersistentPaths()
    {
        var rows = new List<object>();
        foreach (var pair in new[] { (Registry.CurrentUser, "Environment"), (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment") }) {
            using var key = pair.Item1.OpenSubKey(pair.Item2, false);
            var exists = key != null && key.GetValueNames().Contains("Path", StringComparer.OrdinalIgnoreCase);
            rows.Add(new { hive = pair.Item1.Name, exists,
                kind = exists ? key!.GetValueKind("Path").ToString() : null,
                value = exists ? key!.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null });
        }
        return JsonSerializer.Serialize(rows);
    }

    internal void GuardSdkEnvironment(Action action)
    {
        lock (SdkEnvironmentLock) {
            var before = PersistentPaths();
            var processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
            var previous = Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", EnvironmentVariableTarget.Process);
            Exception? failure = null;
            try {
                Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "false", EnvironmentVariableTarget.Process);
                action();
            } catch (Exception error) { failure = error; throw; }
            finally {
                Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", previous, EnvironmentVariableTarget.Process);
                try {
                    if (!String.Equals(before, PersistentPaths(), StringComparison.Ordinal)
                        || !String.Equals(processPath, Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process), StringComparison.Ordinal))
                        throw new InvalidOperationException("PATH changed during SDK execution. Stop and inspect it; no environment values were overwritten.");
                } catch (Exception error) {
                    if (failure == null) throw;
                    Console.Error.WriteLine("SDK PATH verification also failed: " + error.Message);
                }
            }
        }
    }

    internal static void RequireInventory(IEnumerable<string> before, IEnumerable<string> after)
    {
        if (!before.SequenceEqual(after, StringComparer.Ordinal)) throw new IOException("Source inventory changed; approval refused.");
    }

    internal string Hash(string fullpath)
    {
        using var file = File.Open(fullpath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    internal string FreshArtifactOutput(string path)
    {
        var full = Path.GetFullPath(path);
        var artifacts = Path.Combine(Root, "artifacts");
        if (!full.StartsWith(artifacts + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(full) || Directory.Exists(full))
            throw new InvalidOperationException("Fresh output under canonical artifacts is required.");
        for (var parent = Path.GetDirectoryName(full); parent != null; parent = Path.GetDirectoryName(parent)) {
            if (File.Exists(parent)) throw new IOException("An output ancestor is a file.");
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked output ancestor refused.");
            if (parent.Equals(Root, StringComparison.OrdinalIgnoreCase)) return full;
        }
        throw new InvalidOperationException("Artifact output escaped the project.");
    }

    internal void WriteJsonCreateNew(string path, object value)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        file.Write(bytes); file.Flush(true);
    }
}
