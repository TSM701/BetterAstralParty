using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static partial class NativeCompatibilityFixture
{
    private static int _checks;
    private static readonly List<object> Reports = new();
    private static readonly string[] Current = { "catalog_3.2.0.hash", "catalog_3.2.0.json" };
    private const string Resource = "AstralParty_INT_Data/data.unity3d";
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void JsonFile(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value), new UTF8Encoding(false));
    private sealed class Entry
    {
        public string root { get; set; } = "game";
        public string path { get; set; } = "";
        public string kind { get; set; } = "fixture";
        public string sha256 { get; set; } = "";
    }
    private sealed class Seed
    {
        internal readonly string Name, Root, Cache, Manifest, Loader;
        internal readonly List<Entry> Entries = new();
        internal bool Settings = true, Bundled;
        internal int Schema = 1;
        internal Seed(string scratch, string name)
        {
            Name = name; Root = Path.Combine(scratch, name); Cache = Path.Combine(Root, "fixture-cache");
            Manifest = Path.Combine(Root, "baseline.json"); Loader = Path.Combine(Root, "fixture-loader");
            Directory.CreateDirectory(Cache); Directory.CreateDirectory(Loader);
            foreach (var path in new[] { "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", Resource,
                "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle", "AstralParty_INT_Data/StreamingAssets/aa/settings.json", "BepInEx/core/BepInEx.Core.dll" })
            {
                var bytes = path == Resource ? Encoding.ASCII.GetBytes("UnityFS\0" + new string('x', 56)) : Encoding.ASCII.GetBytes("inert fixture: " + path);
                Write(path, bytes); Entries.Add(new Entry { path = path, sha256 = Hash(bytes) });
            }
            foreach (var namePart in Current)
            {
                var bytes = Encoding.ASCII.GetBytes("inert current catalog: " + namePart);
                File.WriteAllBytes(Path.Combine(Cache, namePart), bytes);
                Entries.Add(new Entry { root = "cache", path = namePart, sha256 = Hash(bytes) });
            }
        }
        internal void Write(string relative, byte[] bytes)
        { var path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
        internal void Extra(params string[] names) { foreach (var name in names) File.WriteAllText(Path.Combine(Cache, name), "unused historical catalog fixture"); }
        internal void Bundle()
        {
            Bundled = true;
            foreach (var name in new[] { "winhttp.dll", "BepInEx/core/BepInEx.Core.dll" })
            {
                var destination = Path.Combine(Loader, name); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(Path.Combine(Root, name), destination);
            }
        }
        internal void Save()
        {
            if (!Settings) Entries.RemoveAll(entry => entry.path == "AstralParty_INT_Data/StreamingAssets/aa/settings.json");
            JsonFile(Manifest, new { schema = Schema, cacheCatalogs = Current, files = Entries });
        }
    }
    private static void Case(string scratch, string name, bool allowed, Action<Seed>? arrange = null, string? issue = null, int warningCount = 0,
        bool compareExact = true, Action<Seed>? afterManifest = null, ApplyFailure? expectedFailure = null)
    {
        var seed = new Seed(scratch, name); arrange?.Invoke(seed); seed.Save(); afterManifest?.Invoke(seed);
        var before = Directory.GetFiles(seed.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)));
        var result = GameCompatibility.Check(seed.Root, seed.Manifest, seed.Bundled ? seed.Loader : null);
        Check(result.Allowed == allowed, name + ": expected Allowed; " + string.Join("; ", result.Issues));
        Check(result.Warnings.Length == warningCount, name + ": warnings");
        if (expectedFailure.HasValue) Check(result.Error is ApplySafetyException safety && safety.Failure == expectedFailure.Value, name + ": exact typed path/identity failure retained");
        if (issue != null) Check(result.Issues.Any(line => line.Contains(issue, StringComparison.Ordinal)), name + ": expected issue");
        Check(Directory.GetFiles(seed.Root, "*", SearchOption.AllDirectories).Length == before.Count, name + ": no writes");
        foreach (var file in before) Check(File.Exists(file.Key) && Hash(File.ReadAllBytes(file.Key)) == file.Value, name + ": unchanged fixture");
        Reports.Add(new { Name = name, GameRoot = seed.Root, CacheRoot = seed.Cache, Manifest = seed.Manifest, LoaderRoot = seed.Bundled ? seed.Loader : null,
            result.Allowed, result.Issues, result.Warnings, CompareExact = compareExact, ErrorType = result.Error?.GetType().FullName });
    }
    private static int Main(string[] args)
    {
        if (args.Length != 2 || Path.GetDirectoryName(Path.GetFullPath(args[0])) != Path.GetFullPath(args[1]) || Directory.Exists(args[0]))
            throw new ArgumentException("Fresh isolated direct artifact scratch required.");
        var scratch = args[0]; Directory.CreateDirectory(scratch);
        Case(scratch, "exact", true);
        Case(scratch, "additional-entry-metadata", true, afterManifest: seed => JsonFile(seed.Manifest, new { schema = 1, cacheCatalogs = Current,
            files = seed.Entries.Select(entry => new { entry.root, entry.path, entry.sha256, entry.kind, note = "harmless annotation" }) }));
        Case(scratch, "long-path-한글-" + new string('L', 130), true);
        Case(scratch, "older-pair", true, seed => seed.Extra("catalog_3.1.0.json", "catalog_3.1.0.hash"));
        Case(scratch, "older-orphan", true, seed => seed.Extra("catalog_0.0.0.hash"));
        Case(scratch, "unknown-catalog", true, seed => seed.Extra("catalog_unknown.json"), warningCount: 1);
        Case(scratch, "newer-catalog", true, seed => seed.Extra("catalog_3.2.1.hash"), warningCount: 1);
        Case(scratch, "alias-catalog", true, seed => seed.Extra("catalog_03.1.0.json"), warningCount: 1);
        Case(scratch, "uppercase-catalog", true, seed => seed.Extra("Catalog_3.1.0.hash"), warningCount: 1);
        Case(scratch, "no-settings-exact", true, seed => seed.Settings = false);
        Case(scratch, "no-settings-older", true, seed => { seed.Settings = false; seed.Extra("catalog_3.1.0.hash"); }, warningCount: 1);
        Case(scratch, "missing-current", true, seed => File.Delete(Path.Combine(seed.Cache, Current[0])), warningCount: 2);
        Case(scratch, "changed-current", true, seed => File.WriteAllText(Path.Combine(seed.Cache, Current[0]), "changed fixture"), warningCount: 1);
        Case(scratch, "changed-executable", true, seed => seed.Write("AstralParty_INT.exe", new byte[64]), warningCount: 1);
        Case(scratch, "game-kind-advisory", true, seed => { seed.Entries.Single(entry => entry.path == "GameAssembly.dll").kind = "resource"; seed.Write("GameAssembly.dll", new byte[64]); }, warningCount: 1);
        Case(scratch, "loader-kind-is-observation", true, seed => { seed.Entries.Single(entry => entry.path == "winhttp.dll").kind = "resource"; seed.Write("winhttp.dll", new byte[64]); }, warningCount: 1);
        Case(scratch, "unknown-other-loader", true, seed => { seed.Write("winhttp.dll", Encoding.UTF8.GetBytes("unreviewed other-mod bootstrap")); seed.Write("BepInEx/core/BepInEx.Core.dll", Encoding.UTF8.GetBytes("unreviewed other-mod API")); seed.Write("BepInEx/plugins/OtherMod.dll", new byte[]{1,2,3}); }, warningCount: 2);
        Case(scratch, "missing-main-executable", false, seed => File.Delete(Path.Combine(seed.Root, "AstralParty_INT.exe")), "Missing [fixture]: game/AstralParty_INT.exe");
        Case(scratch, "empty-main-executable", false, seed => seed.Write("AstralParty_INT.exe", Array.Empty<byte>()), "Empty required game file:");
        Case(scratch, "resource-advisory", true, seed => seed.Write(Resource, Encoding.ASCII.GetBytes("UnityFS\0" + new string('y', 56))), warningCount: 1);
        Case(scratch, "resource-invalid", true, seed => seed.Write(Resource, new byte[64]), warningCount: 2);
        Case(scratch, "resource-invalid-pinned", true, seed => { var bytes = new byte[64]; seed.Write(Resource, bytes); seed.Entries.Single(entry => entry.path == Resource).sha256 = Hash(bytes); }, warningCount: 1);
        Case(scratch, "resource-missing", true, seed => File.Delete(Path.Combine(seed.Root, Resource)), warningCount: 1);
        Case(scratch, "cache-directory-missing", true, seed => { foreach (var path in Directory.GetFiles(seed.Cache)) File.Delete(path); Directory.Delete(seed.Cache); }, warningCount: 3);
        Case(scratch, "bundled-loader", true, seed => seed.Bundle());
        Case(scratch, "bundled-loader-existing-config", true, seed => { seed.Bundle(); seed.Write("doorstop_config.ini", Encoding.ASCII.GetBytes("inert existing loader")); }, warningCount: 2);
        Case(scratch, "bundled-loader-existing-mismatch", true, seed => { seed.Bundle(); seed.Write("winhttp.dll", new byte[32]); }, warningCount: 1);
        Case(scratch, "bundled-loader-cannot-replace-game", false, seed => { seed.Bundle(); File.Delete(Path.Combine(seed.Root, "GameAssembly.dll")); File.WriteAllText(Path.Combine(seed.Loader, "GameAssembly.dll"), "inert substitute"); }, "Missing [fixture]: game/GameAssembly.dll");
        Case(scratch, "missing-required", false, seed => seed.Entries.RemoveAll(entry => entry.path == "UnityPlayer.dll"), "Check failed: Missing required manifest entry: game/UnityPlayer.dll");
        Case(scratch, "duplicate", false, seed => seed.Entries.Add(seed.Entries[0]), "Check failed: Duplicate manifest entry");
        Case(scratch, "bad-hash", false, seed => seed.Entries[0].sha256 = "not-a-hash", "Check failed: Invalid manifest entry");
        Case(scratch, "bad-root", false, seed => seed.Entries[0].root = "elsewhere", "Check failed: Invalid manifest entry");
        Case(scratch, "traversal", false, seed => seed.Entries[0].path = "../outside.exe", "Check failed: Invalid manifest entry");
        Case(scratch, "rooted", false, seed => seed.Entries[0].path = "C:/outside.exe", "Check failed: Invalid manifest entry");
        Case(scratch, "invalid-schema", false, seed => seed.Schema = 2, "Check failed: Invalid or empty compatibility manifest");
        Case(scratch, "utf8-bom", true, afterManifest: seed => File.WriteAllText(seed.Manifest, File.ReadAllText(seed.Manifest), new UTF8Encoding(true)));
        Case(scratch, "catalog-property-missing", false, issue: "Invalid catalog manifest", compareExact: false, afterManifest: seed =>
            JsonFile(seed.Manifest, new { schema = 1, files = seed.Entries }));
        Case(scratch, "catalog-property-missing-empty-cache", false, seed =>
        {
            seed.Entries.RemoveAll(entry => entry.root == "cache");
            foreach (var path in Directory.GetFiles(seed.Cache)) File.Delete(path);
        }, "Invalid catalog manifest", compareExact: false, afterManifest: seed => JsonFile(seed.Manifest, new { schema = 1, files = seed.Entries }));
        Case(scratch, "catalog-duplicate-missing", false, seed => File.Delete(Path.Combine(seed.Cache, Current[0])),
            "Invalid catalog manifest", warningCount: 1, afterManifest: seed => JsonFile(seed.Manifest, new { schema = 1, cacheCatalogs = new[] { Current[0], Current[0] }, files = seed.Entries }));
        Case(scratch, "malformed-json", false, issue: "Check failed:", compareExact: false, afterManifest: seed => File.WriteAllText(seed.Manifest, "{bad fixture json"));
        Case(scratch, "manifest-missing", false, issue: "Check failed:", compareExact: false, afterManifest: seed => File.Delete(seed.Manifest));
        Case(scratch, "catalog-limit", true, seed => seed.Extra(Enumerable.Range(0, 511).Select(i => "catalog_1.0." + i + ".hash").ToArray()), warningCount: 1);
        foreach (var relative in new[] { Resource, "fixture-cache/" + Current[0], "GameAssembly.dll", "winhttp.dll" }) {
            var seed = new Seed(scratch, "busy-" + Path.GetFileName(relative)); seed.Save();
            var before = Directory.GetFiles(seed.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)));
            GameCompatibility.Result result;
            using (var held = File.Open(Path.Combine(seed.Root, relative), FileMode.Open, FileAccess.Read, FileShare.None))
                result = GameCompatibility.Check(seed.Root, seed.Manifest);
            var mandatory = relative is "GameAssembly.dll";
            Check(result.Allowed == !mandatory && (mandatory ? result.Error is ApplySafetyException safety && safety.Failure == ApplyFailure.FileBusy && safety.NativeError == 32 : result.Warnings.Length > 0), "Only required game/loader observation locks block with their original native cause: " + relative);
            foreach (var file in before) Check(Hash(File.ReadAllBytes(file.Key)) == file.Value, "Busy observation preserves all input bytes");
        }
        foreach (var root in new[] { @"\\server\share", @"C:\..\outside", @"C:\fixture\NUL", "C:\\" + new string('x', 32760) })
        {
            var invalid = GameCompatibility.Check(root, Path.Combine(scratch, "not-read.json"));
            Check(!invalid.Allowed && invalid.Error is ArgumentException && invalid.Issues.Length == 1, "invalid ordinary root rejected before I/O");
        }
        LinkedInputs(scratch);
        JsonFile(Path.Combine(scratch, "native-results.json"), new { Passed = true, Checks = _checks, ReportCases = Reports.Count, Reports });
        Console.WriteLine("Native compatibility: " + _checks + " assertions PASS across " + Reports.Count + " inert fixtures.");
        return 0;
    }
}
