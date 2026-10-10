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
        File.WriteAllText(path, "changed!");
        guard.Verify(now.AddSeconds(1)); // Bounded polling.
        try { guard.Verify(now.AddSeconds(11)); throw new Exception("Changed catalog accepted"); }
        catch (InvalidDataException) { }
        File.WriteAllText(path, "baseline");
        guard.Verify(now.AddSeconds(12));
        File.WriteAllText(Path.Combine(root, "catalog_new.hash"), "new");
        try { guard.Verify(now.AddSeconds(23)); throw new Exception("New catalog accepted"); }
        catch (InvalidDataException) { }
        var background = new CatalogGuard(manifest, root);
        // Poll must schedule I/O, not throw a worker failure synchronously.
        background.Poll(now);
        var observed = SpinWait.SpinUntil(() =>
        {
            try { background.Poll(now); return false; }
            catch (InvalidDataException) { return true; }
        }, TimeSpan.FromSeconds(5));
        if (!observed) throw new Exception("Background catalog failure was not delivered to the UI caller");
        Console.WriteLine("다운로드 카탈로그 기준 일치·실행 후 변경·신규 버전 감지·검사 주기 검증 통과");
        Console.WriteLine("카탈로그 백그라운드 검사·메인 스레드 오류 전달 검증 통과");
    }
}
