using System.Security.Cryptography;
using System.Text.Json;

namespace BetterAstralParty;

// Downloaded catalogs can change after the launcher check, during login.
internal sealed class CatalogGuard
{
    private readonly string _root;
    private readonly Dictionary<string, string> _expected = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _pinnedSettings;
    private DateTime _nextCheck;
    private Task? _pending;
    internal bool Changed { get; private set; }

    internal CatalogGuard(string manifest, string root)
    {
        _root = root;
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        foreach (var entry in json.RootElement.GetProperty("files").EnumerateArray())
        {
            if (entry.GetProperty("root").GetString() == "game" && entry.GetProperty("path").GetString() == "AstralParty_INT_Data/StreamingAssets/aa/settings.json") _pinnedSettings = true;
            if (entry.GetProperty("root").GetString() != "cache") continue;
            var name = entry.GetProperty("path").GetString()!;
            if (Path.GetFileName(name) != name || name.Contains('\\') || name.Contains(':')
                || !name.StartsWith("catalog", StringComparison.OrdinalIgnoreCase)
                || !(name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Invalid catalog path");
            var hash = entry.GetProperty("sha256").GetString()!;
            if (hash == null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid catalog digest");
            _expected.Add(name, hash);
        }
        if (_expected.Count == 0 || _expected.Count > 512) throw new InvalidDataException("Catalog baseline bounds");
    }

    internal void Verify(DateTime now)
    {
        if (now < _nextCheck) return;
        var files = Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "catalog*")
            .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(path), ".hash", StringComparison.OrdinalIgnoreCase)).Take(513).ToArray() : Array.Empty<string>();
        var changed = (!_pinnedSettings && files.Length != _expected.Count)
            || !Updating.CatalogInventory.Matches(_expected.Keys.ToArray(), files.Select(path => Path.GetFileName(path)!).ToArray());
        foreach (var entry in _expected)
        {
            var path = Path.Combine(_root, entry.Key);
            if (!File.Exists(path)) { changed = true; continue; }
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            if (!string.Equals(Convert.ToHexString(sha.ComputeHash(stream)), entry.Value, StringComparison.OrdinalIgnoreCase))
                changed = true;
        }
        // Catalog drift is advisory; public API guards isolate actual feature incompatibilities.
        Changed = changed;
        _nextCheck = now.AddSeconds(10);
    }

    // Startup still uses synchronous Verify. Later full hashes use one worker;
    // observe failures on the UI thread, isolated from gameplay and other UI features.
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
