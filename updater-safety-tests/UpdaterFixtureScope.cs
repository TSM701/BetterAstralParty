using System;
using System.IO;

internal static class UpdaterFixtureScope
{
    internal static string Require(string path)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("BAP_UPDATER_TEST_ROOT") ?? throw new InvalidOperationException("Inert updater fixture boundary missing."));
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0
            || File.ReadAllText(Path.Combine(root, ".bap-v4-updater-fixture-root")) != "BetterAstralParty.UpdaterFixtureRoot/v1\n")
            throw new InvalidOperationException("Exact inert updater fixture marker required.");
        var target = Path.GetFullPath(path);
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only an inert updater fixture descendant is allowed.");
        return target;
    }
}
