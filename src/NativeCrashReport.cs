namespace BetterAstralParty;

// Read existing Windows evidence only. No exception hooks, registry changes or dump contents.
internal static class NativeCrashReport
{
    internal static IEnumerable<string> Read(string reports, string dumps, string game, DateTime since)
    {
        var found = false;
        if (Directory.Exists(reports))
        foreach (var directory in Directory.EnumerateDirectories(reports, "AppCrash_AstralParty_INT.*")
                     .OrderByDescending(Directory.GetLastWriteTimeUtc).Take(32))
        {
            var path = Path.Combine(directory, "Report.wer");
            var line = ReadReport(path, game, since);
            if (line == null) continue;
            found = true;
            yield return line;
        }
        if (!found) yield return "nativeCrash no matching recent WER report; absence does not rule out a crash";
        if (Directory.Exists(dumps))
        foreach (var path in Directory.EnumerateFiles(dumps, "AstralParty_INT.exe.*.dmp")
                     .Where(path => File.GetLastWriteTimeUtc(path) >= since).OrderByDescending(File.GetLastWriteTimeUtc).Take(8))
            yield return $"nativeCrash dumpAvailable={Path.GetFileName(path)}; modifiedUtc={File.GetLastWriteTimeUtc(path):O}; bytes={new FileInfo(path).Length}; contentsNotRead=True";
    }

    private static string? ReadReport(string path, string game, DateTime since)
    {
        try
        {
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < since || new FileInfo(path).Length > 256 * 1024) return null;
            var fields = File.ReadLines(path).Where(line => line.Contains('='))
                .Select(line => line.Split('=', 2)).GroupBy(parts => parts[0])
                .ToDictionary(group => group.Key, group => group.First()[1]);
            if (!fields.TryGetValue("AppPath", out var app) ||
                !string.Equals(app, Path.Combine(game, "AstralParty_INT.exe"), StringComparison.OrdinalIgnoreCase)) return null;
            string Value(string key) => fields.TryGetValue(key, out var value) ? value.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(value.Length, 100)] : "unknown";
            var eventTime = "unknown";
            if (fields.TryGetValue("EventTime", out var timestamp) && long.TryParse(timestamp, out var ticks)
                && ticks is >= 0 and <= 2650467743999999999)
            {
                var time = DateTime.FromFileTimeUtc(ticks);
                if (time < since) return null; // Copying an old WER must not make an old crash recent.
                eventTime = time.ToString("O");
            }
            return $"nativeCrash historical=True; eventUtc={eventTime}; reportUpdatedUtc={File.GetLastWriteTimeUtc(path):O}; module={Value("Sig[3].Value")}; exception={Value("Sig[6].Value")}; offset={Value("Sig[7].Value")}; report={Path.GetFileName(Path.GetDirectoryName(path))}";
        }
        // WER can rotate or lock individual files while scanning. Do not lose every other report.
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
