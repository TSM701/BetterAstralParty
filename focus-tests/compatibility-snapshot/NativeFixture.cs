#nullable enable
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Text;
using System.Web.Script.Serialization;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

#if !BAP_SAFETY_FIXTURE && !BAP_FIXTURE_HELPER
#error The snapshot semantic fixture must use the production fixture cache boundary.
#endif

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class NativeSnapshotFixture
{
    private const string Marker = "BetterAstralParty compatibility snapshot synthetic fixture v1\n";
    private const string Note = "Synthetic fixture only / 합성 검토 자료";
    private const string ManifestName = "BetterAstralParty.compatibility.json";
    private static int Checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Snapshot native consumer fixture: " + name);
        Checks++;
    }
    private static string Artifacts(string path)
    {
        DirectoryInfo? current = new DirectoryInfo(WindowsPath.Absolute(path));
        for (; current != null; current = current.Parent) if (current.Name.Equals("artifacts", StringComparison.OrdinalIgnoreCase)) return current.FullName;
        throw new InvalidOperationException("The existing synthetic fixture must be under project artifacts.");
    }
    private static byte[] Read(WindowsFileFence fence, string relative, int maximum = 65536)
    {
        using var file = fence.OpenFile(relative);
        if (file.Stream.Length <= 0 || file.Stream.Length > maximum) throw new InvalidDataException("Synthetic input length bound.");
        var bytes = new byte[(int)file.Stream.Length]; var at = 0;
        while (at < bytes.Length) { var read = file.Stream.Read(bytes, at, bytes.Length - at); if (read == 0) throw new EndOfStreamException(); at += read; }
        file.Recheck(); return bytes;
    }
    private static void Write(WindowsFileFence game, string relative, byte[] bytes)
    {
        using var file = game.OpenFile(relative, true);
        file.Stream.Position = 0; file.Stream.SetLength(0); file.Stream.Write(bytes, 0, bytes.Length); file.Flush();
        Check(file.Hash() == UpdateTrust.Hash(bytes), "synthetic mutation committed through held file");
    }
    private static bool UpdateAccepts(string game, byte[] manifest, bool warning = false)
    {
        try {
            using var fence = new WindowsFileFence(game);
            using var image = fence.OpenFile("AstralParty_INT.exe");
            using var validation = UpdateCompatibility.Require(fence, manifest, image);
            validation.Recheck();
            if (warning) Check(validation.Warnings.Count > 0, "advisory reaches the actual update consumer");
            return true;
        } catch (ApplySafetyException error) when (error.Failure == ApplyFailure.CompatibilityRequired) { return false; }
    }
    private static void Advisory(string game, string manifestPath, byte[] manifest, string warning)
    {
        var install = GameCompatibility.Check(game, manifestPath);
        Check(install.Allowed && install.Error == null && install.Issues.Length == 0 && install.Warnings.Any(value => value.Contains(warning)), "native install observes without blocking " + warning);
        Check(UpdateAccepts(game, manifest, true), "native update retains authenticated applicability after " + warning);
    }
    private static void MissingGameObservation(string game,string manifestPath,byte[] manifest,string issue)
    {
        var report=GameCompatibility.Check(game,manifestPath);
        Check(report.Issues.Any(value=>value.StartsWith(issue,StringComparison.Ordinal)),"Empty game input remains visible in observation");
        Check(UpdateAccepts(game,manifest,true),"Empty foreign game input does not block authenticated owned-file update");
    }
    private static void Mutated(WindowsFileFence game, string relative, byte[] bytes, Action action)
    {
        var original = Read(game, relative);
        try { Write(game, relative, bytes); action(); }
        finally { Write(game, relative, original); }
    }
    public static int Main(string[] args)
    {
        try {
            if (args.Length != 1 || !Path.IsPathRooted(args[0])) throw new ArgumentException("Explicit existing synthetic fixture root required.");
            var root = WindowsPath.Absolute(args[0]);
            Check(Path.GetFileName(root) == "compatibility-snapshot-fixture", "exact synthetic fixture root name");
            Check(Artifacts(root).Equals(Artifacts(AppDomain.CurrentDomain.BaseDirectory), StringComparison.OrdinalIgnoreCase), "fixture and executable share the same project artifact boundary");
            using var boundary = new WindowsFileFence(root);
            using var marker = boundary.OpenFile("synthetic-fixture.marker");
            Check(marker.Stream.Length == Encoding.ASCII.GetByteCount(Marker), "synthetic marker length");
            var markerBytes = new byte[(int)marker.Stream.Length]; var markerAt = 0;
            while (markerAt < markerBytes.Length) { var read = marker.Stream.Read(markerBytes, markerAt, markerBytes.Length - markerAt); if (read == 0) throw new EndOfStreamException(); markerAt += read; }
            Check(Encoding.ASCII.GetString(markerBytes) == Marker, "exact synthetic marker");
            boundary.DirectoryIdentity("basic"); boundary.DirectoryIdentity("basic/game"); boundary.DirectoryIdentity("basic/cache"); boundary.DirectoryIdentity("basic/output");
            var game = boundary.Full("basic/game"); var manifestPath = boundary.Full("basic/output/" + ManifestName);
            var manifest = Read(boundary, "basic/output/" + ManifestName);
            var sourceEvidence = Read(boundary, "basic/output/compatibility-snapshot-source.json", 128 * 1024);
            var source = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(sourceEvidence));
            Check((string)source["kind"] == "unapproved-compatibility-snapshot" && !(bool)source["automaticApproval"]
                && !(bool)source["compatibilityVerified"] && (string)source["manifestSha256"] == UpdateTrust.Hash(manifest), "source evidence binds this unapproved manifest");
            var parsed = (Dictionary<string, object?>)new InstallReceipt.Json(manifest, 65536, 128, 1024).Parse()!;
            Check(Equals(parsed["schema"], 1) && (string)parsed["reviewNote"]! == Note, "known synthetic baseline schema and note");
            var rows = ((List<object?>)parsed["files"]!).Cast<Dictionary<string, object?>>().ToArray();
            var catalogNames = ((List<object?>)parsed["cacheCatalogs"]!).Cast<string>().ToArray();
            Check(catalogNames.SequenceEqual(new[] { "catalog_fixture.hash", "catalog_fixture.json" }, StringComparer.Ordinal), "only the two top-level synthetic catalog names");
            var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var relative in new[] { "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", "baselib.dll", "AstralParty_INT_Data/il2cpp_data/Metadata/global-metadata.dat",
                "AstralParty_INT_Data/ScriptingAssemblies.json", "AstralParty_INT_Data/RuntimeInitializeOnLoads.json", "AstralParty_INT_Data/StreamingAssets/aa/settings.json", "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle" })
                expected.Add("game/" + relative, Encoding.UTF8.GetBytes("inert fixture " + relative));
            foreach (var relative in new[] { "BepInEx/core/BepInEx.Core.dll", "BepInEx/core/nested/Case.DLL", "AstralParty_INT_Data/Plugins/x64/native.dll" })
                expected.Add("game/" + relative, Encoding.UTF8.GetBytes("inert native " + relative));
            expected.Add("game/AstralParty_INT_Data/data.unity3d", Encoding.UTF8.GetBytes("UnityFS\0" + new string('x', 32)));
            expected.Add("cache/catalog_fixture.hash", Encoding.UTF8.GetBytes("inert catalog hash")); expected.Add("cache/catalog_fixture.json", Encoding.UTF8.GetBytes("{}"));
            Check(rows.Length == expected.Count && rows.Select(row => (string)row["root"]! + "/" + (string)row["path"]!).Distinct(StringComparer.Ordinal).Count() == expected.Count, "exact synthetic selected-file set");
            foreach (var row in rows) {
                var key = (string)row["root"]! + "/" + (string)row["path"]!;
                Check(expected.TryGetValue(key, out var bytes) && (string)row["sha256"]! == UpdateTrust.Hash(bytes!), "known inert source fingerprint " + key);
                Check(Read(boundary, "basic/" + key).SequenceEqual(bytes!), "source contents remain synthetic " + key);
            }
            using (var output = boundary.OpenFile("basic/output/" + ManifestName)) {
                using var gameFence = new WindowsFileFence(game);
                gameFence.CreatePrivateDirectory("fixture-cache");
                foreach (var name in catalogNames) {
                    using var input = boundary.OpenFile("basic/cache/" + name);
                    using var copy = gameFence.OpenFile("fixture-cache/" + name, true, true);
                    input.Stream.CopyTo(copy.Stream); copy.Flush(); Check(copy.Hash() == input.Hash(), "create-new synthetic fixture cache copy");
                }
                var initial = GameCompatibility.Check(game, manifestPath);
                Check(initial.Allowed && initial.Error == null && initial.Issues.Length == 0 && initial.Warnings.Length == 0,
                    "actual native install consumer accepts untouched snapshot; Error=" + initial.Error + "; Issues=[" + string.Join(" | ", initial.Issues) + "]; Warnings=[" + string.Join(" | ", initial.Warnings) + "]");
                Check(UpdateAccepts(game, manifest), "actual native update consumer accepts untouched snapshot");
                Mutated(gameFence, "AstralParty_INT_Data/data.unity3d", Encoding.ASCII.GetBytes("UnityFS\0" + new string('r', 48)), () => Advisory(game, manifestPath, manifest, "game/AstralParty_INT_Data/data.unity3d"));
                Mutated(gameFence, "AstralParty_INT_Data/data.unity3d", Encoding.ASCII.GetBytes(new string('x', 48)), () => Advisory(game, manifestPath, manifest, "Invalid resource bundle:"));
                Mutated(gameFence, "AstralParty_INT.exe", Encoding.ASCII.GetBytes("changed inert executable"), () => Advisory(game, manifestPath, manifest, "game/AstralParty_INT.exe"));
                Mutated(gameFence, "fixture-cache/catalog_fixture.json", Encoding.ASCII.GetBytes("{\"changed\":true}"), () => Advisory(game, manifestPath, manifest, "cache/catalog_fixture.json"));
                foreach (var required in new[] { "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll" })
                    Mutated(gameFence, required, Array.Empty<byte>(), () => MissingGameObservation(game, manifestPath, manifest, "Empty required game file: game/" + required));
                foreach (var loader in new[] { "winhttp.dll", "BepInEx/core/BepInEx.Core.dll" })
                    Mutated(gameFence, loader, Encoding.ASCII.GetBytes("changed inert shared loader"), () => Advisory(game, manifestPath, manifest, "Changed ["));
                Check(GameCompatibility.Check(game, manifestPath).Allowed && UpdateAccepts(game, manifest), "restored synthetic baseline accepted after all negative cases");
            }
            foreach (var row in expected) Check(Read(boundary, "basic/" + row.Key).SequenceEqual(row.Value), "original synthetic source restored " + row.Key);
            Check(Read(boundary, "basic/output/" + ManifestName).SequenceEqual(manifest)
                && Read(boundary, "basic/output/compatibility-snapshot-source.json", 128 * 1024).SequenceEqual(sourceEvidence), "snapshot artifacts remain byte-for-byte unchanged");
            boundary.Recheck(); marker.Recheck();
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { passed = true, checks = Checks, actualGameOrCacheRead = false, networkUsed = false, operationalKeyUsed = false,
                productionConsumers = new[] { "GameCompatibility.Check", "UpdateCompatibility.Require" }, syntheticFixtureRoot = root, automaticApproval = false }));
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
