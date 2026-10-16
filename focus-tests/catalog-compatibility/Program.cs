using System.Reflection;
using System.Text;
using System.Text.Json;
using BetterAstralParty;
using BetterAstralParty.Updating;

internal static class Program
{
    private sealed record Case(string Name, string[] Expected, string[] Actual, bool Allowed);
    private sealed record PolicyResult(string Name, string[] Expected, string[] Actual, bool Allowed, bool CsResult);
    private static readonly string[] Current = { "catalog_3.2.0.hash", "catalog_3.2.0.json" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static int _checks, _nativeChecks, _runtimeChecks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); _checks++; }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (ApplySafetyException error) when (error.Failure == ApplyFailure.CompatibilityRequired) { Check(true, name); return; }
        catch (InvalidDataException) { Check(true, name); return; }
        throw new Exception("Accepted: " + name);
    }
    private static string[] Add(params string[] extra) => Current.Concat(extra).ToArray();
    private static List<Case> Cases()
    {
        var cases = new List<Case>
        {
            new("exact", Current, Current, true),
            new("order-independent", Current.Reverse().ToArray(), Current, true),
            new("older-pair", Current, Add("catalog_3.1.0.json", "catalog_3.1.0.hash"), true),
            new("older-orphan", Current, Add("catalog_2.9.9.hash"), true),
            new("zero-version", Current, Add("catalog_0.0.0.json"), true),
            new("older-lexically-greater", new[] { "catalog_10.0.0.json", "catalog_10.0.0.hash" }, new[] { "catalog_10.0.0.hash", "catalog_10.0.0.json", "catalog_9.9.9.json" }, true),
            new("missing-json", Current, new[] { Current[0], "catalog_3.1.0.json" }, false),
            new("missing-all", Current, Array.Empty<string>(), false),
            new("newer-minor", Current, Add("catalog_3.3.0.json"), false),
            new("newer-major", Current, Add("catalog_4.0.0.hash"), false),
            new("newer-patch", Current, Add("catalog_3.2.1.hash"), false),
            new("unknown", Current, Add("catalog_test.json"), false),
            new("same-version-alias", Current, Add("catalog_3.2.0.JSON"), false),
            new("uppercase-prefix", Current, Add("Catalog_3.1.0.json"), false),
            new("uppercase-extension", Current, Add("catalog_3.1.0.HASH"), false),
            new("two-components", Current, Add("catalog_3.1.json"), false),
            new("four-components", Current, Add("catalog_3.1.0.0.json"), false),
            new("prerelease", Current, Add("catalog_3.1.0-rc.1.json"), false),
            new("leading-zero", Current, Add("catalog_03.1.0.json"), false),
            new("negative", Current, Add("catalog_-1.0.0.hash"), false),
            new("overflow", Current, Add("catalog_2147483648.0.0.json"), false),
            new("ten-digits", Current, Add("catalog_1000000000.0.0.json"), false),
            new("non-ascii-digit", Current, Add("catalog_３.1.0.json"), false),
            new("nested", Current, Add("old/catalog_3.1.0.json"), false),
            new("trailing-text", Current, Add("catalog_3.1.0.json.backup"), false),
            new("duplicate-expected", new[] { Current[0], Current[0] }, Current, false),
            new("duplicate-actual", Current, Add(Current[0]), false),
            new("legacy-exact", new[] { "catalog_fixture.hash", "catalog_fixture.json" }, new[] { "catalog_fixture.json", "catalog_fixture.hash" }, true),
            new("legacy-extra", new[] { "catalog_fixture.hash", "catalog_fixture.json" }, new[] { "catalog_fixture.json", "catalog_fixture.hash", "catalog_1.0.0.hash" }, false),
            new("multiple-exact", Add("catalog_2.0.0.hash", "catalog_2.0.0.json"), Add("catalog_2.0.0.json", "catalog_2.0.0.hash"), true),
            new("multiple-extra", Add("catalog_2.0.0.hash", "catalog_2.0.0.json"), Add("catalog_2.0.0.hash", "catalog_2.0.0.json", "catalog_1.0.0.hash"), false),
            new("mixed-version-pair", new[] { Current[0], "catalog_3.1.0.json" }, new[] { Current[0], "catalog_3.1.0.json", "catalog_2.0.0.hash" }, false),
            new("single-exact", new[] { Current[0] }, new[] { Current[0] }, true),
            new("single-extra", new[] { Current[0] }, Current, false),
            new("empty-exact", Array.Empty<string>(), Array.Empty<string>(), true),
            new("empty-extra", Array.Empty<string>(), new[] { "catalog_0.0.0.hash" }, false),
            new("expected-limit", Enumerable.Range(0, 128).Select(i => "catalog_fixture" + i + ".hash").ToArray(), Enumerable.Range(0, 128).Select(i => "catalog_fixture" + i + ".hash").Reverse().ToArray(), true),
            new("expected-over-limit", Enumerable.Range(0, 129).Select(i => "catalog_fixture" + i + ".hash").ToArray(), Enumerable.Range(0, 129).Select(i => "catalog_fixture" + i + ".hash").ToArray(), false),
            new("actual-limit", Current, Current.Concat(Enumerable.Range(0, 510).Select(i => "catalog_1.0." + i + ".json")).ToArray(), true),
            new("actual-over-limit", Current, Current.Concat(Enumerable.Range(0, 511).Select(i => "catalog_1.0." + i + ".json")).ToArray(), false)
        };
        return cases;
    }
    private static string Hash(byte[] bytes) => UpdateTrust.Hash(bytes);
    private static void JsonFile(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
    private static Dictionary<string, byte[]> Files(string[] names)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in new[] { "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", "BepInEx/core/BepInEx.Core.dll", "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle" })
            files.Add(path, Encoding.ASCII.GetBytes("inert compatibility fixture: " + path));
        files.Add("AstralParty_INT_Data/StreamingAssets/aa/settings.json", Encoding.ASCII.GetBytes("{\"m_CatalogLocations\":[{\"m_Keys\":[\"AddressablesMainContentCatalogRemoteHash\"],\"m_InternalId\":\"{App.WebServerConfig.Path}/catalog_3.2.0.hash\"},{\"m_Keys\":[\"AddressablesMainContentCatalogCacheHash\"],\"m_InternalId\":\"{UnityEngine.Application.persistentDataPath}/com.unity.addressables/catalog_3.2.0.hash\"},{\"m_Keys\":[\"AddressablesMainContentCatalog\"],\"m_InternalId\":\"{UnityEngine.AddressableAssets.Addressables.RuntimePath}/catalog.bundle\",\"m_Dependencies\":[\"AddressablesMainContentCatalogRemoteHash\",\"AddressablesMainContentCatalogCacheHash\"]}]}"));
        var resource = new byte[32]; Encoding.ASCII.GetBytes("UnityFS\0").CopyTo(resource, 0);
        files.Add("AstralParty_INT_Data/data.unity3d", resource);
        foreach (var name in names) files.Add("fixture-cache/" + name, Encoding.ASCII.GetBytes("inert catalog fixture: " + name));
        return files;
    }
    private sealed class Seed
    {
        internal readonly string Root, Cache, ManifestPath;
        internal readonly byte[] Manifest;
        internal readonly Dictionary<string, string> Original;
        internal Seed(string scratch, string name, string[]? expected = null, string[]? extra = null, string? wrongDigest = null, string? missing = null, bool settingsAnchor = true)
        {
            expected ??= Current;
            Root = Path.Combine(scratch, name); Cache = Path.Combine(Root, "fixture-cache"); ManifestPath = Path.Combine(Root, "baseline.json");
            Directory.CreateDirectory(Cache);
            var files = Files(expected);
            foreach (var file in files)
            {
                var path = Path.Combine(Root, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (file.Key != missing) File.WriteAllBytes(path, file.Value);
            }
            foreach (var catalog in extra ?? Array.Empty<string>()) File.WriteAllText(Path.Combine(Cache, catalog), "unused historical fixture bytes");
            var entries = files.Where(file => settingsAnchor || file.Key != "AstralParty_INT_Data/StreamingAssets/aa/settings.json").Select(file => new { root = file.Key.StartsWith("fixture-cache/", StringComparison.Ordinal) ? "cache" : "game", path = file.Key.StartsWith("fixture-cache/", StringComparison.Ordinal) ? file.Key.Substring(14) : file.Key, kind = "fixture", sha256 = file.Key == wrongDigest ? "not-a-digest" : Hash(file.Value) }).ToArray();
            Manifest = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, cacheCatalogs = expected, files = entries });
            File.WriteAllBytes(ManifestPath, Manifest);
            Original = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(Root, path).Replace('\\', '/'), path => Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
        }
        internal void Unchanged()
        {
            foreach (var file in Original) Check(File.Exists(Path.Combine(Root, file.Key)) && Hash(File.ReadAllBytes(Path.Combine(Root, file.Key))) == file.Value, "original preserved: " + file.Key);
        }
        internal UpdateCompatibility Require(WindowsFileFence fence, WindowsFileFence.FileLease image) => UpdateCompatibility.Require(fence, Manifest, image);
    }
    private static void Runtime(string scratch)
    {
        var start = _checks; var now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var good = new Seed(scratch, "runtime-older", extra: new[] { "catalog_3.1.0.json", "catalog_3.1.0.hash" });
        var guard = new CatalogGuard(good.ManifestPath, good.Cache); guard.Verify(now); Check(true, "runtime current hashes plus older accepted");
        good.Unchanged();
        var json = Path.Combine(good.Cache, "catalog_3.2.0.json"); var original = File.ReadAllBytes(json);
        // A pre-expiry call must retain the existing ten-second schedule; it must not rescan early.
        File.WriteAllText(json, "changed current catalog"); guard.Verify(now.AddSeconds(9)); Check(true, "runtime retained interval");
        guard.Verify(now.AddSeconds(10)); Check(guard.Changed, "runtime changed current hash is advisory after interval"); File.WriteAllBytes(json, original);
        guard.Verify(now.AddSeconds(10));
        var pendingField = typeof(CatalogGuard).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!;
        guard.Poll(now.AddSeconds(19)); Check(pendingField.GetValue(guard) == null, "poll skips before interval");
        File.WriteAllText(Path.Combine(good.Cache, "catalog_3.3.0.json"), "newer catalog fixture");
        guard.Poll(now.AddSeconds(20)); var task = (Task?)pendingField.GetValue(guard); Check(task != null, "poll starts one worker");
        task!.GetAwaiter().GetResult(); guard.Poll(now.AddSeconds(20)); Check(guard.Changed, "poll observes advisory drift without throwing");
        good.Unchanged();
        foreach (var test in new[] { ("newer", new[] { "catalog_3.2.1.hash" }), ("unknown", new[] { "catalog_other.json" }), ("leading-zero", new[] { "catalog_03.1.0.hash" }), ("uppercase", new[] { "catalog_3.3.0.HASH" }) })
        {
            var seed = new Seed(scratch, "runtime-" + test.Item1, extra: test.Item2);
            var observer = new CatalogGuard(seed.ManifestPath, seed.Cache); observer.Verify(now); Check(observer.Changed, "runtime " + test.Item1 + " is advisory"); seed.Unchanged();
        }
        foreach (var missing in Current)
        {
            var seed = new Seed(scratch, "runtime-missing-" + Path.GetExtension(missing)[1..], missing: "fixture-cache/" + missing);
            var observer = new CatalogGuard(seed.ManifestPath, seed.Cache); observer.Verify(now); Check(observer.Changed, "runtime missing expected is advisory"); seed.Unchanged();
        }
        var malformed = new Seed(scratch, "runtime-malformed-digest", wrongDigest: "fixture-cache/catalog_3.2.0.json");
        Reject(() => new CatalogGuard(malformed.ManifestPath, malformed.Cache).Verify(now), "runtime malformed digest"); malformed.Unchanged();
        foreach (var test in new[] { ("no-settings-exact", Current, Array.Empty<string>(), true), ("no-settings-older", Current, new[] { "catalog_3.1.0.hash" }, false), ("legacy-exact", new[] { "catalog_fixture.hash", "catalog_fixture.json" }, Array.Empty<string>(), true), ("legacy-extra", new[] { "catalog_fixture.hash", "catalog_fixture.json" }, new[] { "catalog_1.0.0.hash" }, false) })
        {
            var seed = new Seed(scratch, "runtime-" + test.Item1, expected: test.Item2, extra: test.Item3, settingsAnchor: false);
            var observer = new CatalogGuard(seed.ManifestPath, seed.Cache); observer.Verify(now);
            Check(observer.Changed == !test.Item4, "runtime " + test.Item1 + " reports drift without blocking");
            seed.Unchanged();
        }
        var bounded = new Seed(scratch, "runtime-limit", extra: Enumerable.Range(0, 510).Select(i => "catalog_1.0." + i + ".hash").ToArray());
        new CatalogGuard(bounded.ManifestPath, bounded.Cache).Verify(now); Check(true, "runtime 512 catalogs accepted");
        File.WriteAllText(Path.Combine(bounded.Cache, "catalog_1.1.0.hash"), "limit fixture");
        var capped = new CatalogGuard(bounded.ManifestPath, bounded.Cache); capped.Verify(now); Check(capped.Changed, "runtime 513 catalogs advisory and bounded"); bounded.Unchanged();
        _runtimeChecks = _checks - start;
    }
    private static void Native(string scratch)
    {
        var start = _checks;
        foreach (var test in new[] { ("exact", Array.Empty<string>(), true), ("older", new[] { "catalog_3.1.0.hash", "catalog_3.1.0.json" }, true), ("unknown", new[] { "catalog_other.hash" }, false), ("newer", new[] { "catalog_3.2.1.json" }, false), ("alias", new[] { "catalog_03.1.0.json" }, false), ("uppercase", new[] { "catalog_3.3.0.HASH" }, false) })
        {
            var seed = new Seed(scratch, "native-" + test.Item1, extra: test.Item2);
            using (var fence = new WindowsFileFence(seed.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe"))
            {
                using var compatible = seed.Require(fence, image); compatible.Recheck();
                Check(test.Item3 || compatible.Warnings.Count > 0, "native " + test.Item1 + " drift is advisory");
            }
            seed.Unchanged();
        }
        foreach (var path in new[] { "fixture-cache/catalog_3.2.0.json", "AstralParty_INT_Data/StreamingAssets/aa/settings.json", "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle", "GameAssembly.dll" })
        {
            var seed = new Seed(scratch, "native-digest-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(Path.Combine(seed.Root, path), "modified fixture anchor");
            using (var fence = new WindowsFileFence(seed.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe")) {
                using var compatible = seed.Require(fence, image); Check(compatible.Warnings.Count > 0, "native game/catalog digest drift advisory: " + path);
            }
        }
        foreach (var test in new[] { ("malformed", "fixture-cache/catalog_3.2.0.hash", (string?)null), ("missing", (string?)null, "fixture-cache/catalog_3.2.0.json") })
        {
            var seed = new Seed(scratch, "native-" + test.Item1, wrongDigest: test.Item2, missing: test.Item3);
            using (var fence = new WindowsFileFence(seed.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe")) {
                if (test.Item1 == "malformed") { using var compatible = seed.Require(fence, image); Check(compatible.Warnings.Count > 0, "Malformed compatibility observation is advisory, not an update gate"); }
                else { using var compatible = seed.Require(fence, image); Check(compatible.Warnings.Count > 0, "native missing old catalog is advisory"); }
            }
            seed.Unchanged();
        }
        foreach (var scenario in new[] { (Name: "sharing", Error: (Exception)new IOException("inert share error", unchecked((int)0x80070020))), (Name: "denied", Error: (Exception)new UnauthorizedAccessException("inert denied error")), (Name: "security", Error: (Exception)new System.Security.SecurityException("inert Framework observation permission error")), (Name: "io", Error: (Exception)new IOException("inert unknown I/O error")) }) {
            var error = scenario.Error; var seed = new Seed(scratch, "native-cause-" + scenario.Name); var inject = false;
            using (var fence = new WindowsFileFence(seed.Root, point => { if (inject && point == "open") throw error; })) using (var image = fence.OpenFile("AstralParty_INT.exe")) {
                inject = true;
                using var compatible = seed.Require(fence, image); compatible.Recheck();
                Check(compatible.Warnings.Count > 0, "I/O failure retires only compatibility observation: " + scenario.Name);
            }
            seed.Unchanged();
        }
        foreach (var test in new[] { ("no-settings-exact", Current, Array.Empty<string>(), true), ("no-settings-older", Current, new[] { "catalog_3.1.0.hash" }, true), ("legacy-exact", new[] { "catalog_fixture.hash", "catalog_fixture.json" }, Array.Empty<string>(), true), ("legacy-extra", new[] { "catalog_fixture.hash", "catalog_fixture.json" }, new[] { "catalog_1.0.0.hash" }, false) })
        {
            var seed = new Seed(scratch, "native-" + test.Item1, expected: test.Item2, extra: test.Item3, settingsAnchor: false);
            using (var fence = new WindowsFileFence(seed.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe"))
            {
                using var compatible = seed.Require(fence, image); compatible.Recheck();
                Check(test.Item4 || compatible.Warnings.Count > 0, "native " + test.Item1 + " no-settings extras advisory");
            }
            seed.Unchanged();
        }
        foreach (var added in new[] { "catalog_3.3.0.json", "catalog_2.0.0.hash" })
        {
            var seed = new Seed(scratch, "native-race-" + Path.GetFileNameWithoutExtension(added));
            using (var fence = new WindowsFileFence(seed.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe")) using (var compatible = seed.Require(fence, image))
            {
                using (var file = fence.OpenFile("fixture-cache/" + added, true, true)) { file.Stream.Write(Encoding.ASCII.GetBytes("inventory changed after acceptance")); file.Flush(); }
                compatible.Recheck(); Check(compatible.Warnings.Count > 0, "native later catalog addition is advisory: " + added);
            }
            seed.Unchanged();
        }
        var many = new Seed(scratch, "native-limit", extra: Enumerable.Range(0, 510).Select(i => "catalog_1.0." + i + ".hash").ToArray());
        using (var fence = new WindowsFileFence(many.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe")) { using var compatible = many.Require(fence, image); Check(true, "native 512 catalogs accepted"); }
        File.WriteAllText(Path.Combine(many.Cache, "catalog_1.1.0.hash"), "limit fixture");
        using (var fence = new WindowsFileFence(many.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe")) { using var compatible = many.Require(fence, image); Check(compatible.Warnings.Count > 0, "native 513 files produce bounded advisory"); }
        many.Unchanged();
        foreach (var path in new[] { "winhttp.dll", "BepInEx/core/BepInEx.Core.dll" }) {
            var seed = new Seed(scratch, "native-loader-" + Path.GetFileNameWithoutExtension(path));
            File.WriteAllText(Path.Combine(seed.Root, path), "changed shared loader");
            using var fence = new WindowsFileFence(seed.Root); using var image = fence.OpenFile("AstralParty_INT.exe");
            using var compatible = seed.Require(fence, image); compatible.Recheck();Check(compatible.Warnings.Count > 0, "Unknown third-party loader is preserved without approval: " + path);
        }
        var absent = new Seed(scratch, "native-cache-absent");
        foreach (var path in Directory.GetFiles(absent.Cache)) File.Delete(path); Directory.Delete(absent.Cache);
        using (var fence = new WindowsFileFence(absent.Root)) using (var image = fence.OpenFile("AstralParty_INT.exe")) {
            using var compatible = absent.Require(fence, image); Check(compatible.Warnings.Count > 0, "native absent catalog directory is advisory"); compatible.Recheck();
            Check(!Directory.Exists(absent.Cache), "native advisory does not create a catalog directory");
        }
        foreach (var file in absent.Original.Where(file => !file.Key.StartsWith("fixture-cache/", StringComparison.Ordinal)))
            Check(Hash(File.ReadAllBytes(Path.Combine(absent.Root, file.Key))) == file.Value, "absent-cache observation preserves every game/mod input");
        foreach (var relative in new[] { "AstralParty_INT_Data/data.unity3d", "fixture-cache/" + Current[0], "GameAssembly.dll", "winhttp.dll" }) {
            var seed = new Seed(scratch, "native-busy-" + Path.GetFileName(relative));
            using var held = File.Open(Path.Combine(seed.Root, relative), FileMode.Open, FileAccess.Read, FileShare.None);
            using var fence = new WindowsFileFence(seed.Root); using var image = fence.OpenFile("AstralParty_INT.exe");
            using var compatible = seed.Require(fence, image); compatible.Recheck();Check(compatible.Warnings.Count > 0, "Compatibility observation lock never blocks owned update: " + relative);
        }
        _nativeChecks = _checks - start;
    }
    internal static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || !OperatingSystem.IsWindows()) throw new ArgumentException("Fresh scratch and artifact parent on Windows required.");
            var scratch = Path.GetFullPath(args[0]); var parent = Path.GetFullPath(args[1]);
            if (Path.GetDirectoryName(scratch) != parent || Directory.Exists(scratch) || Path.GetFileName(scratch).Length > 48) throw new ArgumentException("Fresh short direct artifact child required.");
            Directory.CreateDirectory(scratch);
            var cases = Cases(); var results = cases.Select(test => new PolicyResult(test.Name, test.Expected, test.Actual, test.Allowed, CatalogInventory.Matches(test.Expected, test.Actual))).ToArray();
            foreach (var result in results) Check(result.CsResult == result.Allowed, "policy: " + result.Name);
            JsonFile(Path.Combine(scratch, "policy-cases.json"), results);
            Runtime(scratch); Native(scratch);
            JsonFile(Path.Combine(scratch, "native-results.json"), new { Passed = true, PolicyCases = results.Length, RuntimeChecks = _runtimeChecks, NativeChecks = _nativeChecks, Checks = _checks, IsolatedScratchOnly = true, ExecutedGameOrInstaller = false });
            Console.WriteLine("Catalog compatibility PASS: " + _checks + " checks; " + results.Length + " shared policy cases; runtime=" + _runtimeChecks + "; native=" + _nativeChecks + "; " + scratch);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
