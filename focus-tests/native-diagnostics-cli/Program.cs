using BetterAstralParty.Installation;

internal static class Program
{
    private static int _passed;
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Diagnostics parser assertion failed."); _passed++; }
    private static void Reject(params string[] args)
    {
        try { NativeDiagnosticEntry.Parse(args); }
        catch (ArgumentException) { _passed++; return; }
        throw new InvalidOperationException("Invalid diagnostics arguments accepted.");
    }
    private static int Main()
    {
        foreach (var action in new[] { "collect", "open", "menu" }) {
            var options = NativeDiagnosticEntry.Parse(new[] { "--diagnostics", action });
            Check(options.Action == action && options.Language == "ko" && options.GameRoot == "" && !options.NoOpen);
        }
        var parsed = NativeDiagnosticEntry.Parse(new[] { "--diagnostics", "collect", "--game-root", @"C:\Synthetic game\8vJXnINT", "--language", "en", "--no-open" });
        Check(parsed.GameRoot == @"C:\Synthetic game\8vJXnINT" && parsed.Language == "en" && parsed.NoOpen);
        Reject(); Reject("--diagnostics"); Reject("--install", "collect"); Reject("--diagnostics", "unknown");
        Reject("--diagnostics", "collect", "--unknown"); Reject("--diagnostics", "collect", "--game-root");
        Reject("--diagnostics", "collect", "--language", "tr");
        Reject("--diagnostics", "collect", "--language", "ko", "--language", "en");
        Reject("--diagnostics", "collect", "--no-open", "--no-open");
        Reject("--diagnostics", "collect", "--game-root", new string('a', BetterAstralParty.Updating.WindowsPath.MaximumLength + 1));
        Reject(Enumerable.Repeat("--diagnostics", 10).ToArray());
        Check(BetterAstralParty.Diagnostics.DiagnosticBundle.Calls == 0);
        Console.WriteLine("PASS: " + _passed + " native diagnostics parser checks; no collection, process launch, or real user logs.");
        return 0;
    }
}

namespace BetterAstralParty.Installation
{
    internal static class NativeSteamMaintenance
    {
        internal static string ResolveSteamRoot() => "";
        internal static List<string> FindGameDirectories(string steamRoot, string packageRoot) => new List<string>();
    }
    internal static class NativeMaintenanceEntry
    {
        internal static void ConsoleReady() => throw new InvalidOperationException("Runtime collection is outside this parser fixture.");
        internal static string? Consent(string prompt) => throw new InvalidOperationException(prompt);
    }
}
namespace BetterAstralParty.Updating
{
    internal static class WindowsPath
    {
        internal const int MaximumLength = 32767;
        internal static string Absolute(string path) => throw new InvalidOperationException(path);
        internal static string ForIo(string path) => throw new InvalidOperationException(path);
        internal static string FromIo(string path) => throw new InvalidOperationException(path);
        internal static string Parent(string path) => throw new InvalidOperationException(path);
    }
}
namespace BetterAstralParty.Diagnostics
{
    internal sealed class Hub : IDisposable
    {
        internal string Path => "unused";
        internal void Open(Action<string> action) => throw new InvalidOperationException("No Explorer launch in parser fixture.");
        public void Dispose() { }
    }
    internal sealed class BundleResult
    {
        internal string ArchivePath => "unused";
        internal int Included => 0;
        internal int Omitted => 0;
        internal void OpenFolder(Action<string> action) => throw new InvalidOperationException("No Explorer launch in parser fixture.");
    }
    internal static class DiagnosticBundle
    {
        internal static int Calls { get; private set; }
        internal static Hub AcquireHub(string primary, string fallback) { Calls++; throw new InvalidOperationException("No collection in parser fixture."); }
        internal static BundleResult Collect(params object?[] paths) { Calls++; throw new InvalidOperationException("No real logs in parser fixture."); }
    }
    internal sealed class DiagnosticSources { internal static DiagnosticSources Production() => new DiagnosticSources(); }
}
