using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;
using Microsoft.Win32.SafeHandles;

namespace BetterAstralParty.Development;

internal static class CompatibilitySnapshot
{
    private static readonly string[] CodeFiles = ["AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", "baselib.dll",
        "AstralParty_INT_Data/il2cpp_data/Metadata/global-metadata.dat", "AstralParty_INT_Data/ScriptingAssemblies.json", "AstralParty_INT_Data/RuntimeInitializeOnLoads.json",
        "AstralParty_INT_Data/StreamingAssets/aa/settings.json", "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle"];
    private static readonly string[] NativeFolders = ["BepInEx/core", "AstralParty_INT_Data/Plugins"];
    private const string Resource = "AstralParty_INT_Data/data.unity3d", ManifestName = "BetterAstralParty.compatibility.json";
    private sealed record Limits(int Entries = 4096, int Depth = 32, int Files = 128, long FileBytes = 512L * 1024 * 1024, long TotalBytes = 8L * 1024 * 1024 * 1024);
    private sealed record ManifestFile(string root, string path, string kind, string sha256);
    private sealed record SourceFile(string root, string path, string kind, long bytes, string sha256, string fileIdentity);
    private sealed record Scan(string Directory, bool Recursive, string[] Entries, string[] Files);
    private sealed class Budget(Limits limits)
    {
        internal readonly Limits Bounds = limits;
        internal long Bytes; internal int Files;
    }
    private sealed class ReadScope : IDisposable
    {
        internal readonly WindowsFileFence Fence;
        internal readonly List<SourceFile> Sources = [];
        private readonly string label;
        private readonly Budget budget;
        private readonly List<(WindowsFileFence.FileLease Lease, string Hash)> held = [];
        private readonly List<Scan> scans = [];
        private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        internal ReadScope(string root, string label, Budget budget) { Fence = new WindowsFileFence(root); this.label = label; this.budget = budget; }
        internal void Add(string relative, string kind)
        {
            Text(relative, "Selected path");
            if (!selected.Add(relative) || ++budget.Files > budget.Bounds.Files) throw new InvalidDataException("Snapshot selected-file count or duplicate bound.");
            var file = Fence.OpenFile(relative);
            try {
                var length = file.Stream.Length;
                if (length <= 0 || length > budget.Bounds.FileBytes || checked(budget.Bytes + length) > budget.Bounds.TotalBytes) throw new InvalidDataException("Snapshot input byte bound.");
                var digest = file.Hash(); budget.Bytes += length; held.Add((file, digest));
                Sources.Add(new(label, relative, kind, length, digest, file.Identity.Text)); file = null!;
            } finally { file?.Dispose(); }
        }
        internal string[] Inventory(string directory, bool recursive)
        {
            var scan = Enumerate(directory, recursive); scans.Add(scan); return scan.Files;
        }
        private Scan Enumerate(string directory, bool recursive)
        {
            var pending = new Stack<(string Path, int Depth)>(); pending.Push((directory, 0));
            var entries = new List<string>(); var files = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pending.Count != 0) {
                var next = pending.Pop();
                if (next.Depth > budget.Bounds.Depth) throw new InvalidDataException("Snapshot directory depth bound.");
                if (next.Path.Length != 0) Fence.DirectoryIdentity(next.Path); else Fence.Recheck();
                var full = next.Path.Length == 0 ? Fence.Root : Fence.Full(next.Path);
                foreach (var entry in Directory.EnumerateFileSystemEntries(WindowsPath.ForIo(full))) {
                    if (entries.Count >= budget.Bounds.Entries) throw new InvalidDataException("Snapshot inventory entry bound.");
                    var name = Path.GetFileName(entry); var relative = next.Path.Length == 0 ? name : next.Path + '/' + name;
                    var path = Fence.Full(relative); var attributes = File.GetAttributes(WindowsPath.ForIo(path));
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
                    if (!seen.Add(relative)) throw new InvalidDataException("Snapshot inventory alias rejected.");
                    var child = (attributes & FileAttributes.Directory) != 0; entries.Add((child ? "D:" : "F:") + relative);
                    if (child) {
                        Fence.DirectoryIdentity(relative); // Pin before any descent, not after following a directory name.
                        if (recursive) pending.Push((relative, next.Depth + 1));
                    } else files.Add(relative);
                }
            }
            Fence.Recheck(); entries.Sort(StringComparer.Ordinal); files.Sort(StringComparer.Ordinal);
            return new(directory, recursive, entries.ToArray(), files.ToArray());
        }
        internal void Verify()
        {
            foreach (var scan in scans) if (!scan.Entries.SequenceEqual(Enumerate(scan.Directory, scan.Recursive).Entries, StringComparer.Ordinal)) throw new InvalidDataException("Snapshot inventory changed during capture.");
            foreach (var item in held) if (item.Lease.Hash() != item.Hash) throw new InvalidDataException("Snapshot held input changed during capture.");
            Fence.Recheck();
        }
        public void Dispose() { foreach (var item in held) item.Lease.Dispose(); Fence.Dispose(); }
    }

    private static void Text(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(character => character < 32 || char.IsSurrogate(character)))
            throw new ArgumentException(name + " must be one nonempty line of at most 1024 BMP characters, matching the native manifest reader.");
    }
    private static string Absolute(string value)
    {
        if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("Explicit absolute game/cache roots are required.");
        return WindowsPath.Absolute(Path.GetFullPath(value));
    }
    private static bool Within(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase) || first.StartsWith(second.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase);
    private static Dictionary<string, string> Arguments(string[] args)
    {
        var permitted = new HashSet<string>(["gameroot", "cacheroot", "outputdirectory", "reviewnote"], StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++) {
            var option = args[index]; var name = option.TrimStart('-').Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
            if (!option.StartsWith('-') || !permitted.Contains(name) || ++index >= args.Length || !values.TryAdd(name, args[index])) throw new ArgumentException("Unknown, duplicate or valueless compatibility-snapshot argument.");
        }
        if (values.Count != permitted.Count) throw new ArgumentException("Compatibility snapshot requires --game-root, --cache-root, --output-directory and --review-note.");
        return values;
    }
    internal static object Run(DevContext context, string[] args)
    {
        var values = Arguments(args);
        return Capture(context, values["gameroot"], values["cacheroot"], values["outputdirectory"], values["reviewnote"], new Limits());
    }
    private static object Capture(DevContext context, string gameRoot, string cacheRoot, string outputDirectory, string note, Limits bounds, Action? beforeVerification = null)
    {
        Text(note, "Review note"); gameRoot = Absolute(gameRoot); cacheRoot = Absolute(cacheRoot);
        var output = context.FreshArtifactOutput(Path.IsPathFullyQualified(outputDirectory) ? outputDirectory : Path.Combine(context.Root, outputDirectory));
        if (Within(gameRoot, cacheRoot) || Within(cacheRoot, gameRoot) || Within(output, gameRoot) || Within(gameRoot, output) || Within(output, cacheRoot) || Within(cacheRoot, output))
            throw new ArgumentException("Snapshot game/cache/output roots must be separate.");
        var budget = new Budget(bounds); using var game = new ReadScope(gameRoot, "game", budget); using var cache = new ReadScope(cacheRoot, "cache", budget);
        foreach (var path in CodeFiles) game.Add(path, "code-engine-catalog"); game.Add(Resource, "resources-review-required");
        foreach (var folder in NativeFolders) foreach (var path in game.Inventory(folder, true).Where(path => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))) game.Add(path, "loader-native");
        var catalogs = cache.Inventory("", false).Where(path => Regex.IsMatch(path, @"\Acatalog.*\.(json|hash)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        if (catalogs.Length == 0) throw new InvalidDataException("Downloaded catalog missing; update vanilla resources and review before creating a snapshot.");
        foreach (var path in catalogs) cache.Add(path, "downloaded-code-data-catalog");
        var sources = game.Sources.Concat(cache.Sources).OrderBy(row => row.root, StringComparer.Ordinal).ThenBy(row => row.path, StringComparer.Ordinal).ToArray();
        var created = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, created, reviewNote = note, cacheCatalogs = catalogs, files = sources.Select(row => new ManifestFile(row.root, row.path, row.kind, row.sha256)) }, new JsonSerializerOptions { WriteIndented = true });
        if (manifest.Length > 65536) throw new InvalidDataException("Snapshot exceeds native compatibility manifest byte bound.");
        var parsed = new InstallReceipt.Json(manifest, 65536, 128, 1024).Parse() as Dictionary<string, object?>;
        if (parsed == null || !Equals(parsed["schema"], 1) || parsed["files"] is not List<object?> rows || rows.Count != sources.Length) throw new InvalidDataException("Snapshot native manifest schema mismatch.");
        beforeVerification?.Invoke(); game.Verify(); cache.Verify();
        using var destination = new WindowsFileFence(context.Root);
        var relative = Path.GetRelativePath(context.Root, output).Replace('\\', '/');
        var slash = relative.LastIndexOf('/'); if (slash < 0 || !relative.StartsWith("artifacts/", StringComparison.Ordinal)) throw new InvalidDataException("Project artifact output required.");
        destination.EnsureDirectory(relative[..slash]); destination.CreatePrivateDirectory(relative);
        void Write(string name, byte[] bytes) { using var file = destination.OpenFile(relative + '/' + name, true, true); file.Stream.Write(bytes); file.Flush(); if (file.Hash() != UpdateTrust.Hash(bytes)) throw new IOException("Snapshot output verification failed."); }
        Write(ManifestName, manifest); game.Verify(); cache.Verify();
        var manifestHash = UpdateTrust.Hash(manifest);
        Write("compatibility-snapshot-source.json", JsonSerializer.SerializeToUtf8Bytes(new {
            schema = 1, kind = "unapproved-compatibility-snapshot", status = "ReviewRequired", created, manifest = ManifestName, manifestSha256 = manifestHash,
            sourceRoots = new[] { new { root = "game", identity = game.Fence.RootIdentity.Text }, new { root = "cache", identity = cache.Fence.RootIdentity.Text } },
            files = sources, selectedFiles = sources.Length, selectedBytes = budget.Bytes, generatorSha256 = context.Hash(typeof(CompatibilitySnapshot).Assembly.Location),
            automaticApproval = false, compatibilityVerified = false, installationPerformed = false, networkUsed = false, operationalKeyUsed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        game.Verify(); cache.Verify(); destination.Recheck();
        return new { status = "ReviewRequired", outputDirectory = output, manifest = Path.Combine(output, ManifestName), manifestSha256 = manifestHash, selectedFiles = sources.Length, snapshotOnly = true, automaticApproval = false };
    }

    internal static object SelfCheck(DevContext context, string stage)
    {
        var fixture = context.FreshArtifactOutput(Path.Combine(Path.GetFullPath(stage), "compatibility-snapshot-fixture"));
        Directory.CreateDirectory(fixture);
        using (var marker = new FileStream(Path.Combine(fixture, "synthetic-fixture.marker"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
            marker.Write(Encoding.ASCII.GetBytes("BetterAstralParty compatibility snapshot synthetic fixture v1\n")); marker.Flush(true);
        }
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Snapshot fixture: " + name); checks++; }
        void Reject(Action action, string name, string? message = null, ApplyFailure? failure = null) {
            try { action(); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or ApplySafetyException
                || error is InvalidOperationException && message != null && error.Message.StartsWith(message, StringComparison.Ordinal)) {
                Check((message == null || error.Message.StartsWith(message, StringComparison.Ordinal)) && (!failure.HasValue || error is ApplySafetyException safety && safety.Failure == failure), name); return;
            }
            throw new InvalidOperationException("Snapshot fixture accepted: " + name);
        }
        (string Game, string Cache, string Output) Seed(string name, string? missing = null, bool catalogs = true) {
            var root = Path.Combine(fixture, name); var game = Path.Combine(root, "game"); var cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(game); Directory.CreateDirectory(cache);
            void FileAt(string scope, string relative, string content) { var path = Path.Combine(scope, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content, new UTF8Encoding(false)); }
            foreach (var path in CodeFiles.Append(Resource)) if (path != missing) FileAt(game, path, path == Resource ? "UnityFS\0" + new string('x', 32) : "inert fixture " + path);
            foreach (var path in new[] { "BepInEx/core/BepInEx.Core.dll", "BepInEx/core/nested/Case.DLL", "AstralParty_INT_Data/Plugins/x64/native.dll" }) FileAt(game, path, "inert native " + path);
            FileAt(game, "BepInEx/core/readme.txt", "unselected"); FileAt(cache, "unrelated.keep", "unselected"); FileAt(cache, "nested/catalog_ignored.json", "not top-level");
            if (catalogs) { FileAt(cache, "catalog_fixture.json", "{}"); FileAt(cache, "catalog_fixture.hash", "inert catalog hash"); }
            return (game, cache, Path.Combine(root, "output"));
        }
        const string note = "Synthetic fixture only / 합성 검토 자료";
        var basic = Seed("basic"); var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in new[] { basic.Game, basic.Cache }) foreach (var file in Directory.EnumerateFiles(scope, "*", SearchOption.AllDirectories)) expected.Add(file, context.Hash(file));
        Capture(context, basic.Game, basic.Cache, basic.Output, note, new Limits(), () => {
            Reject(() => File.WriteAllText(Path.Combine(basic.Game, "GameAssembly.dll"), "must be blocked"), "held hash inputs deny concurrent writes");
            Reject(() => Directory.Move(Path.Combine(basic.Game, "BepInEx/core"), Path.Combine(basic.Game, "BepInEx/core-moved")), "pinned directories deny replacement");
        });
        var bytes = File.ReadAllBytes(Path.Combine(basic.Output, ManifestName));
        var parsed = (Dictionary<string, object?>)new InstallReceipt.Json(bytes, 65536, 128, 1024).Parse()!;
        var selected = ((List<object?>)parsed["files"]!).Cast<Dictionary<string, object?>>().ToArray();
        Check(selected.Length == 16, "all ten fixed files, resource, three recursive DLLs and two catalogs captured");
        Check((string)parsed["reviewNote"]! == note && Equals(parsed["schema"], 1), "Korean/English note and native schema preserved");
        Check(((List<object?>)parsed["cacheCatalogs"]!).Cast<string>().SequenceEqual(new[] { "catalog_fixture.hash", "catalog_fixture.json" }, StringComparer.Ordinal), "catalog order stable and top-level-only");
        foreach (var row in selected) Check((string)row["sha256"]! == context.Hash(Path.Combine((string)row["root"]! == "game" ? basic.Game : basic.Cache, ((string)row["path"]!).Replace('/', '\\'))), "exact held fingerprint");
        Check(expected.All(row => context.Hash(row.Key) == row.Value), "synthetic inputs unchanged");
        using (var evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(basic.Output, "compatibility-snapshot-source.json")))) {
            Check(!evidence.RootElement.GetProperty("automaticApproval").GetBoolean() && !evidence.RootElement.GetProperty("compatibilityVerified").GetBoolean()
                && evidence.RootElement.GetProperty("manifestSha256").GetString() == UpdateTrust.Hash(bytes), "source manifest binds exact unapproved snapshot");
        }
        Reject(() => Capture(context, basic.Game, basic.Cache, basic.Output, note, new Limits()), "existing output rejected", "Fresh output under canonical artifacts is required.");
        Check(File.ReadAllBytes(Path.Combine(basic.Output, ManifestName)).SequenceEqual(bytes), "existing output retained byte-for-byte");
        var cli = Seed("cli");
        Run(context, ["--game-root", cli.Game, "--cache-root", cli.Cache, "--output-directory", cli.Output, "--review-note", note]);
        Check(File.Exists(Path.Combine(cli.Output, ManifestName)) && File.Exists(Path.Combine(cli.Output, "compatibility-snapshot-source.json")), "explicit CLI creates both unapproved artifacts");
        foreach (var invalid in new[] { "", " ", "line\nbreak", new string('x', 1025), "unsupported \U0001F600" }) Reject(() => Text(invalid, "Review note"), "invalid native-reader note rejected");
        foreach (var invalid in new[] { Array.Empty<string>(), new[] { "--game-root", "x", "--game-root", "y" }, new[] { "--unknown", "x" }, new[] { "--review-note" } }) Reject(() => Arguments(invalid), "missing, duplicate and unknown options rejected");
        Reject(() => Absolute("relative/game"), "relative source root rejected", "Explicit absolute");
        var missing = Seed("missing", "GameAssembly.dll"); Reject(() => Capture(context, missing.Game, missing.Cache, missing.Output, note, new Limits()), "required input missing"); Check(!Directory.Exists(missing.Output), "missing input creates no snapshot output");
        var noCatalog = Seed("no-catalog", catalogs: false); Reject(() => Capture(context, noCatalog.Game, noCatalog.Cache, noCatalog.Output, note, new Limits()), "downloaded catalog required", "Downloaded catalog missing");
        var bounded = Seed("bounds");
        Reject(() => Capture(context, bounded.Game, bounded.Cache, bounded.Output, note, new Limits(FileBytes: 8)), "per-file byte bound", "Snapshot input byte bound");
        Reject(() => Capture(context, bounded.Game, bounded.Cache, bounded.Output, note, new Limits(TotalBytes: 8)), "aggregate byte bound", "Snapshot input byte bound");
        Reject(() => Capture(context, bounded.Game, bounded.Cache, bounded.Output, note, new Limits(Files: 12)), "native manifest count bound", "Snapshot selected-file count");
        Reject(() => Capture(context, bounded.Game, bounded.Cache, bounded.Output, note, new Limits(Entries: 1)), "enumeration bound", "Snapshot inventory entry bound");
        Reject(() => Capture(context, bounded.Game, bounded.Cache, bounded.Output, note, new Limits(Depth: 0)), "recursive depth bound", "Snapshot directory depth bound");
        Reject(() => Capture(context, bounded.Game, bounded.Game, bounded.Output, note, new Limits()), "overlapping source roots rejected", "Snapshot game/cache/output roots");
        Reject(() => Capture(context, bounded.Game, bounded.Cache, Path.Combine(bounded.Game, "output"), note, new Limits()), "output/source overlap rejected");
        using (var writer = new FileStream(Path.Combine(bounded.Game, "GameAssembly.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            Reject(() => Capture(context, bounded.Game, bounded.Cache, bounded.Output, note, new Limits()), "active writer rejected", failure: ApplyFailure.FileBusy);
        var race = Seed("inventory-race"); Reject(() => Capture(context, race.Game, race.Cache, race.Output, note, new Limits(), () => File.WriteAllText(Path.Combine(race.Game, "BepInEx/core/added.dll"), "inert")), "new DLL inventory rejected", "Snapshot inventory changed");
        Check(!Directory.Exists(race.Output), "inventory drift publishes no snapshot");
        var linked = Seed("linked"); var outside = Path.Combine(fixture, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "sentinel.keep"), "unchanged");
        FixtureJunction(fixture, Path.Combine(linked.Game, "BepInEx/core/linked"), outside);
        Reject(() => Capture(context, linked.Game, linked.Cache, linked.Output, note, new Limits()), "junction refused before descent", failure: ApplyFailure.LinkedFile);
        Check(File.ReadAllText(Path.Combine(outside, "sentinel.keep")) == "unchanged" && !Directory.Exists(linked.Output), "junction target untouched and output absent");
        var alias = Seed("hardlink"); var aliasPath = Path.Combine(alias.Cache, "catalog_alias.json");
        if (!CreateHardLinkW(aliasPath, Path.Combine(alias.Cache, "catalog_fixture.json"), IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Synthetic hardlink creation failed.");
        Reject(() => Capture(context, alias.Game, alias.Cache, alias.Output, note, new Limits()), "hardlinked input rejected", failure: ApplyFailure.LinkedFile);
        return new { passed = true, checks, stage = fixture, actualGameOrCacheRead = false, networkUsed = false, operationalKeyUsed = false, automaticApproval = false };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, uint length, IntPtr output, uint outputLength, out uint written, IntPtr overlapped);
    internal static void FixtureJunction(string fixture, string path, string target)
    {
        if (!Within(Path.GetFullPath(path), fixture) || !Within(Path.GetFullPath(target), fixture) || Directory.Exists(path) || File.Exists(path)) throw new InvalidDataException("Fresh synthetic junction paths required.");
        Directory.CreateDirectory(path);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target); var printable = Encoding.Unicode.GetBytes(target); var buffer = new byte[16 + substitute.Length + printable.Length + 4];
        BitConverter.GetBytes(0xa0000003U).CopyTo(buffer, 0); BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer, 10); BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(buffer, 12); BitConverter.GetBytes((ushort)printable.Length).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16); printable.CopyTo(buffer, 18 + substitute.Length);
        using var file = CreateFileW(path, 0x40000000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (file.IsInvalid || !DeviceIoControl(file, 0x900a4, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Synthetic junction creation failed.");
    }
}
