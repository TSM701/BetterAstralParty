using System.Globalization;
using System.Text;
namespace BetterAstralParty;

// Durable start-to-home stage ladder and crash-loop safe mode.
// A start that neither reached the home menu, ran HealthySeconds of frames, nor exited normally counts as a failed start.
// The frame-time marker keeps a renamed home panel (game hotfix) from pushing a working mod into safe mode.
// Two failed starts in a row switch to safe mode: no game hooks, only update check/download/apply,
// so a fixed release can still arrive. A new BAP version or game build retries normal mode.
// The collector reads these files by fixed name and keeps only fixed tokens.
internal static class StartupGuard
{
    internal const int FailuresForSafeMode = 2, SafeRunsBeforeRetry = 3;
    internal const float HealthySeconds = 90f;
    private const int MaxLadderBytes = 64 * 1024;
    private static readonly object Gate = new();
    private static string _directory = "", _session = "", _version = "";
    private static bool _homeReached, _enabled;
    internal static bool SafeMode { get; private set; }
    internal static int Failures { get; private set; }

    internal static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Runtime");

    // BetterAstralParty-Mod.cmd writes this empty file right before asking Steam to start app 2622000, so the
    // Steam overlay and checkout bind to the real game. A later Steam-client launch must stay vanilla:
    // the request is deleted on every load and honoured only while fresh and only when the delete succeeded.
    internal const string LaunchRequestName = "mod-launch.request";
    internal static readonly TimeSpan LaunchRequestLifetime = TimeSpan.FromMinutes(3);
    internal static bool LaunchRequestFresh(TimeSpan age) => age >= TimeSpan.FromSeconds(-5) && age <= LaunchRequestLifetime;
    internal static string LaunchRequestReason { get; private set; } = "request-absent";
    internal static bool ConsumeLaunchRequest()
    {
        try
        {
            var path = Path.Combine(Directory, LaunchRequestName);
            var info = new FileInfo(path);
            if (!info.Exists) { LaunchRequestReason = "request-absent"; return false; }
            var age = DateTime.UtcNow - info.LastWriteTimeUtc;
            LaunchRequestReason = age < TimeSpan.FromSeconds(-5) ? "request-future" : age > LaunchRequestLifetime ? "request-stale" : "request-fresh";
            File.Delete(path);
            return LaunchRequestFresh(age);
        }
        catch { LaunchRequestReason = "request-unreadable"; return false; }
    }

    // Why BAP was ON or OFF for the latest launch, in fixed tokens the collector keeps. Never throws.
    internal static void RecordLaunchDecision(bool active, string route)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var text = "time=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\nactive=" + (active ? "true" : "false")
                + "\nroute=" + route + "\nrequest=" + LaunchRequestReason + "\n";
            var path = Path.Combine(Directory, "launch-decision.state"); var temporary = path + ".tmp";
            File.WriteAllText(temporary, text, Encoding.ASCII); File.Move(temporary, path, true);
        }
        catch { }
    }

    // Called first in Load; never throws. Storage failure leaves normal mode on.
    internal static void Begin(string version, string gameRoot)
    {
        try
        {
            _directory = Directory; _version = version; _session = Guid.NewGuid().ToString("N");
            System.IO.Directory.CreateDirectory(_directory);
            var current = Path.Combine(_directory, "stages-current.log");
            var unfinished = false;
            if (File.Exists(current))
            {
                var text = Ladder(current);
                unfinished = text != null && !text.Contains(" stage=HomeMenu ", StringComparison.Ordinal) && !text.Contains(" stage=Healthy ", StringComparison.Ordinal) && !text.Contains(" stage=SessionEnd ", StringComparison.Ordinal);
                File.Copy(current, Path.Combine(_directory, "stages-previous.log"), true);
                File.Delete(current);
            }
            var state = ReadState();
            var game = GameIdentity(gameRoot);
            var sameBuild = state.TryGetValue("version", out var v) && v == version && state.TryGetValue("game", out var g) && g == game;
            var failures = sameBuild && int.TryParse(state.GetValueOrDefault("failures"), NumberStyles.None, CultureInfo.InvariantCulture, out var f) ? f : 0;
            var safeRuns = sameBuild && int.TryParse(state.GetValueOrDefault("safe-runs"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;
            failures = unfinished && sameBuild ? Math.Min(failures + 1, 99) : unfinished ? 1 : sameBuild ? failures : 0;
            SafeMode = failures >= FailuresForSafeMode && safeRuns < SafeRunsBeforeRetry;
            if (failures >= FailuresForSafeMode && !SafeMode) { failures = FailuresForSafeMode - 1; safeRuns = 0; } // One more normal attempt after several safe runs.
            if (SafeMode) safeRuns++;
            Failures = failures;
            WriteState(failures, SafeMode ? "safe" : "normal", version, game, safeRuns);
            _enabled = true;
            Stage("PluginLoad", "Begin");
            if (SafeMode) Stage("SafeMode", "Begin", failures);
        }
        catch { _enabled = false; SafeMode = false; }
    }
    internal static void Stage(string stage, string outcome = "Completed", int count = -1)
    {
        if (!_enabled) return;
        lock (Gate)
        {
            try
            {
                var path = Path.Combine(_directory, "stages-current.log");
                var info = new FileInfo(path); if (info.Exists && info.Length > MaxLadderBytes) return;
                var line = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) + " session=" + _session + " version=" + _version
                    + " stage=" + stage + " outcome=" + outcome + (count >= 0 ? " count=" + count.ToString(CultureInfo.InvariantCulture) : "") + "\n";
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
                var bytes = Encoding.ASCII.GetBytes(line); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            catch { }
        }
    }
    // The first home menu, or HealthySeconds of mod frames, clears the failure count for this build.
    internal static void HomeReached() => MarkHealthy("HomeMenu");
    internal static void Healthy() => MarkHealthy("Healthy");
    private static void MarkHealthy(string stage)
    {
        if (!_enabled || _homeReached) return;
        _homeReached = true; Stage(stage);
        try { var state = ReadState(); WriteState(0, "normal", _version, state.GetValueOrDefault("game") ?? "unknown", 0); Failures = 0; } catch { }
    }
    internal static void SessionEnd() => Stage("SessionEnd");

    private static string? Ladder(string path)
    {
        var info = new FileInfo(path); if (info.Length > MaxLadderBytes * 2) return null;
        return File.ReadAllText(path, Encoding.ASCII);
    }
    private static Dictionary<string, string> ReadState()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(_directory, "safe-mode.state");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024) return result;
        foreach (var line in File.ReadAllText(path, Encoding.ASCII).Split('\n'))
        {
            var at = line.IndexOf('='); if (at > 0) result[line[..at]] = line[(at + 1)..];
        }
        return result;
    }
    private static void WriteState(int failures, string mode, string version, string game, int safeRuns)
    {
        var text = "failures=" + failures.ToString(CultureInfo.InvariantCulture) + "\nmode=" + mode + "\nversion=" + version + "\ngame=" + game
            + "\nsafe-runs=" + safeRuns.ToString(CultureInfo.InvariantCulture) + "\n";
        var path = Path.Combine(_directory, "safe-mode.state"); var temporary = path + ".tmp";
        File.WriteAllText(temporary, text, Encoding.ASCII); File.Move(temporary, path, true);
    }
    // Build identity from the size and write time of the game executable and GameAssembly.dll, never their contents.
    // Game hotfixes usually replace GameAssembly.dll and data while the player executable stays the same.
    private static string GameIdentity(string gameRoot)
    {
        try
        {
            var identity = "";
            foreach (var name in new[] { "AstralParty_INT.exe", "GameAssembly.dll" })
            {
                var info = new FileInfo(Path.Combine(gameRoot, name));
                identity += (identity.Length == 0 ? "" : "-") + (info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) + "-" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0-0");
            }
            return identity;
        }
        catch { return "unknown"; }
    }
}
