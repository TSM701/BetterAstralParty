using System.Diagnostics;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Observability;

namespace BetterAstralParty;

// Owns managed status only. The worker never touches native UI or configuration.
internal sealed class DiagnosticActionController
{
    private int _busy, _status;
    private readonly Func<Func<BundleResult>> _prepare;
    private readonly Action _openHub;
    private readonly Action<BundleResult> _openBundle;
    internal DiagnosticActionController(Func<Func<BundleResult>> prepare, Action openHub, Action<BundleResult> openBundle)
    { _prepare = prepare; _openHub = openHub; _openBundle = openBundle; }
    internal bool Busy => Volatile.Read(ref _busy) != 0;
    internal int StatusCode => Volatile.Read(ref _status);
    internal Task Pending { get; private set; } = Task.CompletedTask;
    internal void OpenFolder()
    {
        try { _openHub(); if (!Busy) Volatile.Write(ref _status, 0); }
        catch { Volatile.Write(ref _status, 4); }
    }
    internal void CreateBundle()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        Volatile.Write(ref _status, 1);
        Func<BundleResult> collect;
        try { collect = _prepare(); }
        catch { Volatile.Write(ref _status, 3); Volatile.Write(ref _busy, 0); return; }
        Pending = Task.Run(() =>
        {
            try
            {
                var result = collect();
                Volatile.Write(ref _status, 2);
                try { _openBundle(result); }
                catch { Volatile.Write(ref _status, 4); }
            }
            catch { Volatile.Write(ref _status, 3); }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }
}

internal static class DiagnosticActions
{
    private static DiagnosticActionController _controller = new(Prepare, OpenHub, result => result.OpenFolder(Open));
    internal static bool Busy => _controller.Busy;
    internal static string Status => ModText.Text(_controller.StatusCode switch
    {
        1 => "진단 ZIP 만드는 중", 2 => "진단 ZIP 생성됨", 3 => "진단 ZIP 생성 실패",
        4 => "진단 폴더 열기 실패", _ => ""
    });
    private static string Primary => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Diagnostics");
    private static string Fallback => Path.Combine(Path.GetTempPath(), "BetterAstralParty", "Diagnostics");
    internal static void OpenFolder() => _controller.OpenFolder();
    internal static void CreateBundle() => _controller.CreateBundle();
    private static Func<BundleResult> Prepare()
    {
        // Flush the known mod-owned writer, without changing recording preferences.
        var game = BepInEx.Paths.GameRootPath;
        var minimal=Plugin.MinimalDiagnostics;
        return () => {
            Plugin.Diagnostics.Flush();
            minimal?.FlushBoundary(50);
            return DiagnosticBundle.Collect(game,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Logs"),
            Path.Combine(Path.GetTempPath(), "BetterAstralParty"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "feimo", "AstralParty_INT"),
            Primary, Fallback, "", Plugin.Version, CaptureHealth(minimal));
        };
    }
    private static LiveDiagnosticHealth CaptureHealth(MinimalEventLog? owner)
    {
        // Memory reads only on the collector worker, after the bounded flush request.
        return new LiveDiagnosticHealth(owner != null, owner?.Detailed ?? false, owner?.Recording ?? false,
            owner?.StorageFailed ?? false, owner?.Dropped ?? 0, owner?.ModVersion ?? Plugin.Version,
            owner?.BuildId ?? "unknown", owner?.ProcessSession ?? "", DateTime.UtcNow);
    }
    private static void OpenHub() { using var lease = DiagnosticBundle.AcquireHub(Primary, Fallback); lease.Open(Open); }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
#if BAP_DIAGNOSTICS_FIXTURE
    // Compiled only by the inert fixture; production has no replaceable controller entry point.
    internal static void UseFixture(DiagnosticActionController controller) => _controller = controller;
#endif
}
