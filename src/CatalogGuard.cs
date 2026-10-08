using System.Security.Cryptography;
using System.Text.Json;

namespace BetterAstralParty;

// Downloaded catalogs can change after the launcher check, during login.
internal sealed class CatalogGuard
{
    private readonly string _root;
    private readonly Dictionary<string, string> _expected = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _nextCheck;
    private Task? _pending;

    internal CatalogGuard(string manifest, string root)
    {
        _root = root;
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        foreach (var entry in json.RootElement.GetProperty("files").EnumerateArray())
        {
            if (entry.GetProperty("root").GetString() != "cache") continue;
            var name = entry.GetProperty("path").GetString()!;
            if (Path.GetFileName(name) != name || name.Contains('\\') || name.Contains(':'))
                throw new InvalidDataException("Invalid catalog path");
            _expected.Add(name, entry.GetProperty("sha256").GetString()!);
        }
        if (_expected.Count == 0) throw new InvalidDataException("Catalog baseline missing");
    }

    internal void Verify(DateTime now)
    {
        if (now < _nextCheck) return;
        var files = Directory.GetFiles(_root, "catalog*")
            .Where(path => Path.GetExtension(path) is ".json" or ".hash").ToArray();
        if (files.Length != _expected.Count) throw new InvalidDataException("Downloaded catalog inventory changed");
        foreach (var path in files)
        {
            if (!_expected.TryGetValue(Path.GetFileName(path), out var expected))
                throw new InvalidDataException("Unknown downloaded catalog");
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            if (!string.Equals(Convert.ToHexString(sha.ComputeHash(stream)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded catalog changed; review required");
        }
        // No AssetBundles scan or automatic baseline updates.
        _nextCheck = now.AddSeconds(10);
    }

    // Startup still uses synchronous Verify. Later full hashes use one worker;
    // observe failures on the UI thread so the existing circuit breaker owns cleanup.
    internal void Poll(DateTime now)
    {
        if (_pending != null)
        {
            if (!_pending.IsCompleted) return;
            _pending.GetAwaiter().GetResult();
            _pending = null;
        }
        if (now < _nextCheck) return;
        _pending = Task.Run(() => Verify(now));
    }
}
