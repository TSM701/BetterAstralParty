using System.Security.Cryptography;
using System.Text.Json;

namespace BetterAstralParty;

internal static class CatalogCheck
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "BAP-Catalog-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "catalog_test.hash");
        var manifest = Path.Combine(root, "baseline.json");
        File.WriteAllText(path, "baseline");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            files = new[] { new { root = "cache", path = "catalog_test.hash", sha256 = hash } }
        }));
        var guard = new CatalogGuard(manifest, root);
        var now = DateTime.UtcNow;
        guard.Verify(now);
        if (guard.Changed) throw new Exception("Unchanged catalog reported as changed");
        File.WriteAllText(path, "changed!");
        guard.Verify(now.AddSeconds(1)); // Bounded polling.
        if (guard.Changed) throw new Exception("Catalog polling interval ignored");
        guard.Verify(now.AddSeconds(11));
        if (!guard.Changed) throw new Exception("Changed catalog was not observed");
        File.WriteAllText(path, "baseline");
        // Both writes are 8 bytes and can land on one file-time tick under load; the guard's 10 s polling never sees
        // edits that close in the game, so give the restore its own timestamp.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));
        guard.Verify(now.AddSeconds(22));
        if (guard.Changed) throw new Exception("Restored catalog remained changed");
        File.WriteAllText(Path.Combine(root, "catalog_new.hash"), "new");
        guard.Verify(now.AddSeconds(33));
        if (!guard.Changed) throw new Exception("New catalog was not observed");
        var background = new CatalogGuard(manifest, root);
        // A content hotfix is observed without becoming a runtime failure.
        background.Poll(now);
        var observed = SpinWait.SpinUntil(() =>
        {
            background.Poll(now);
            return background.Changed;
        }, TimeSpan.FromSeconds(5));
        if (!observed) throw new Exception("Background catalog drift was not observed");
        File.Delete(path);
        guard.Verify(now.AddSeconds(44));
        if (!guard.Changed) throw new Exception("Removed historical catalog was not observed");
        new CatalogGuard(manifest, Path.Combine(root, "not-downloaded-yet")).Verify(now);
        var badManifest = Path.Combine(root, "malformed.json");
        File.WriteAllText(badManifest, JsonSerializer.Serialize(new { files = new[] { new { root = "cache", path = "../escape.hash", sha256 = hash } } }));
        try { new CatalogGuard(badManifest, root); throw new Exception("Unsafe manifest path accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("Catalog observation: matching, drift, restore, missing cache, background polling and unsafe-path rejection passed; hotfix drift does not block features.");
    }
}
