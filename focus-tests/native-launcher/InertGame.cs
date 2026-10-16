using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

internal static class InertGame
{
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        if (!root.StartsWith(FixtureBoundary.Root, StringComparison.OrdinalIgnoreCase)) return 90;
        var text = new StringBuilder("Args=" + string.Join("|", args) + "\n");
        foreach (var name in new[] { "SteamAppId", "SteamGameId", "SteamOverlayGameId", "BAP_COMPATIBILITY_CHECKED", "BAP_UPDATER_LAUNCHER_PID", "BAP_UPDATER_LAUNCHER_CREATION", "BAP_UPDATER_LAUNCHER_SESSION", "PSModulePath" })
            text.Append(name).Append('=').Append(Environment.GetEnvironmentVariable(name) ?? "<absent>").Append('\n');
        File.WriteAllText(Path.Combine(root, "game-ready"), text.ToString());
        if (File.Exists(Path.Combine(root, "hold"))) {
            var due = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(root, "release"))) { if (DateTime.UtcNow >= due) return 88; Thread.Sleep(20); }
        }
        return File.Exists(Path.Combine(root, "game-exit-code")) ? int.Parse(File.ReadAllText(Path.Combine(root, "game-exit-code"))) : 0;
    }
}

