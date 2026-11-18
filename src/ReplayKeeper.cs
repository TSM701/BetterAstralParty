using System.Threading.Tasks;
using UnityEngine;

namespace BetterAstralParty;

// 리플레이 저장 한도 해제 / Remove Replay Save Limit (편의 기능, Keep Replays until 2026.11.2-dev.44; default ON; user-requested 2026-11-18, rules in .claude/rules/replays.md). The game
// saves a replay by downloading it into persistentDataPath/Temp/Replay/<id>/<id> (ReplayLogic.TryLoadReplayFile with
// needSave) and refuses once that folder holds StaticGlobalData.MAX_BATTLE_RECORDS replays (10 in the Global table), a check
// only the client makes. While ON:
// - the limit in the table's cache (StaticGlobalData._cachedIntDict, read only by that check) is int.MaxValue, and the table's
//   own value comes back on OFF, PvP and the breaker (InitData rebuilds the cache from the table, so it is checked again
//   every pass);
// - every saved replay is copied to %LOCALAPPDATA%\BetterAstralParty\Replays, since the login screen's 캐시 정리 deletes the
//   whole Temp folder (its confirmation names only the local settings), and the next run puts copies back
//   (ReplayKeeperPlan says which). The file work runs on a worker thread, one pass every 5 seconds.
internal static class ReplayKeeper
{
    internal const string Feature = "ReplaySaveLimit"; // The settings row's action, so a tripped breaker grays it.
    private const string LimitKey = "MAX_BATTLE_RECORDS";
    private const float Interval = 5f;
    // A replay younger than this may still be downloading, or be one played without saving (read, then deleted).
    private static readonly TimeSpan Settled = TimeSpan.FromSeconds(5);
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Replays");
    private static string StatePath => Path.Combine(Folder, "known.txt");
    private static IntPtr _globalClass, _logicClass;
    private static int? _native;
    private static bool _noKey;
    private static float _next;
    private static Task<PassResult>? _pass;
    // The game's replays at the previous pass of this run; null until the first pass ends.
    private static HashSet<string>? _seen;
    private static HashSet<string>? _written;

    private sealed record PassResult(HashSet<string> Present, string? Report);

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { TickCore(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // OFF, PvP, breaker: the table's limit back. Copies and the state file stay; a pass already running finishes.
    internal static void Clear()
    {
        _next = 0;
        Limit(false);
    }

    private static void TickCore()
    {
        if (_pass is { IsCompleted: true } done)
        {
            _pass = null;
            if (done.IsFaulted) Plugin.Diagnostics.State("replayKeeper", "failed: " + done.Exception?.GetBaseException().GetType().Name);
            else
            {
                _seen = done.Result.Present;
                if (done.Result.Report != null) Plugin.Diagnostics.State("replayKeeper", done.Result.Report);
            }
        }
        var now = Time.unscaledTime;
        if (now < _next) return;
        _next = now + Interval;
        if (!Plugin.ReplaySaveLimit.Value) { Limit(false); return; }
        Limit(true);
        if (_pass != null) return;
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var root = RuntimeObject.StaticField(_logicClass, "_inst")?.Get("replay")?.Field("_replayRoot")?.String();
        if (string.IsNullOrEmpty(root)) return;
        var seen = _seen;
        _pass = Task.Run(() => Pass(root!, seen));
    }

    // The save limit the game reads (StaticGlobalData.FindValue_Int), raised or put back.
    private static void Limit(bool raise)
    {
        if (!raise && _native == null) return;
        if (_globalClass == IntPtr.Zero) _globalClass = RuntimeObject.FindClass("", "StaticGlobalData");
        var cache = RuntimeObject.StaticField(_globalClass, "_cachedIntDict");
        if (cache == null || cache.Call("ContainsKey", LimitKey)?.Value<bool>() != true)
        {
            if (raise && !_noKey) { _noKey = true; Plugin.Diagnostics.State("replayKeeper.limit", "no " + LimitKey); }
            return;
        }
        var value = cache.Call("get_Item", LimitKey)!.Value<int>();
        if (raise && value != int.MaxValue)
        {
            _native = value;
            cache.Call("set_Item", LimitKey, int.MaxValue);
            Plugin.Diagnostics.State("replayKeeper.limit", FormattableString.Invariant($"raised from {value}"));
        }
        else if (!raise && value == int.MaxValue)
        {
            cache.Call("set_Item", LimitKey, _native!.Value);
            Plugin.Diagnostics.State("replayKeeper.limit", FormattableString.Invariant($"back to {_native}"));
            _native = null;
        }
        else if (!raise) _native = null;
    }

    // Worker thread: files only, no game objects. Counts are logged, never replay ids.
    private static PassResult Pass(string root, HashSet<string>? seen)
    {
        Directory.CreateDirectory(Folder);
        var present = Ids(root);
        var copies = Ids(Folder);
        var known = seen == null ? ReadKnown() : new HashSet<string>(StringComparer.Ordinal);
        var plan = ReplayKeeperPlan.Decide(present, copies, known, seen);
        int restored = 0, copied = 0, dropped = 0, failed = 0;
        foreach (var id in plan.Restore)
            if (Try(() => CopyFolder(Path.Combine(Folder, id), root, id))) { restored++; present.Add(id); }
            else failed++;
        var cutoff = DateTime.UtcNow - Settled;
        foreach (var id in present)
        {
            var file = new FileInfo(Path.Combine(root, id, id));
            var copy = new FileInfo(Path.Combine(Folder, id, id));
            if (!file.Exists || file.LastWriteTimeUtc > cutoff) continue;
            if (copy.Exists && copy.Length == file.Length && copy.LastWriteTimeUtc == file.LastWriteTimeUtc) continue;
            if (Try(() => CopyFolder(Path.Combine(root, id), Folder, id))) copied++; else failed++;
        }
        foreach (var id in plan.DropCopy)
            if (Try(() => Directory.Delete(Path.Combine(Folder, id), true))) dropped++; else failed++;
        if (_written == null || !_written.SetEquals(plan.Known)) Try(() => WriteKnown(plan.Known));
        var parts = new List<string>();
        if (seen == null) parts.Add(FormattableString.Invariant($"saved={present.Count}; copies={copies.Count}"));
        if (copied > 0) parts.Add("copied=" + copied);
        if (restored > 0) parts.Add("restored=" + restored);
        if (dropped > 0) parts.Add("copy removed with its replay=" + dropped);
        if (plan.KeptAtOnce > 0) parts.Add("removed at once, copies kept=" + plan.KeptAtOnce);
        if (failed > 0) parts.Add("failed=" + failed);
        return new PassResult(present, parts.Count == 0 ? null : string.Join("; ", parts));
    }

    // The game's own test for a saved replay (ReplayLogic.ListAllReplayIds): a folder holding a file of its own name.
    private static HashSet<string> Ids(string folder)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder)) return ids;
        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            var id = Path.GetFileName(directory);
            if (File.Exists(Path.Combine(directory, id))) ids.Add(id);
        }
        return ids;
    }

    // The folder's files go to <id>.tmp beside the target first, which the game does not list (no file of that name in it),
    // then take its place.
    private static void CopyFolder(string source, string targetRoot, string id)
    {
        var target = Path.Combine(targetRoot, id);
        var staging = target + ".tmp";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.Move(staging, target);
    }

    private static HashSet<string> ReadKnown()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        try { if (File.Exists(StatePath)) foreach (var line in File.ReadAllLines(StatePath)) if (line.Trim() is { Length: > 0 } id) known.Add(id); }
        catch (IOException) { }
        _written = new HashSet<string>(known, StringComparer.Ordinal);
        return known;
    }

    private static void WriteKnown(ISet<string> known)
    {
        var staging = StatePath + ".tmp";
        File.WriteAllLines(staging, known.OrderBy(id => id, StringComparer.Ordinal));
        File.Move(staging, StatePath, true);
        _written = new HashSet<string>(known, StringComparer.Ordinal);
    }

    private static bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
