namespace BetterAstralParty;

// Copies of the mod settings file kept in the per-user folder, so removal, reinstallation or a fresh game folder
// keep them. The game-side file is replaced only when it is missing at start-up or on an explicit, confirmed load.
internal static class SettingsBackup
{
    internal const string AutoName = "auto.cfg", SavedName = "saved.cfg";
    internal const long AutoDelayMs = 1000; // A dragged slider settles before the automatic copy is written.
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Settings");
    private static long _changedAt = -1;

    // Start-up restore: prefer the automatic copy (latest), fall back to the saved one.
    internal static bool RestoreIfMissing(string config, string folder)
    {
        if (File.Exists(config)) return false;
        var auto = Path.Combine(folder, AutoName); var saved = Path.Combine(folder, SavedName);
        var source = !File.Exists(auto) ? saved : !File.Exists(saved) ? auto
            : File.GetLastWriteTimeUtc(saved) > File.GetLastWriteTimeUtc(auto) ? saved : auto;
        if (!File.Exists(source)) return false;
        Copy(source, config);
        return true;
    }

    // Write a temporary sibling first so a failed copy never leaves a half-written target.
    internal static void Copy(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        File.Copy(source, temp, true);
        File.Move(temp, target, true);
    }

    internal static DateTime? SavedAt(string folder)
    {
        var path = Path.Combine(folder, SavedName);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    internal static void MarkChanged(long nowMs) => _changedAt = nowMs;

    // True once per burst of changes, AutoDelayMs after the last one.
    internal static bool AutoDue(long nowMs)
    {
        if (_changedAt < 0 || nowMs - _changedAt < AutoDelayMs) return false;
        _changedAt = -1;
        return true;
    }
}
