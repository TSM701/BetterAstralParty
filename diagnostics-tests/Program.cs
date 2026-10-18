using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Diagnostics;

// Inert net6.0 runtime probe. Every root is a newly created synthetic TEMP fixture.
var root = Path.Combine(Path.GetTempPath(), "bap-diag-net6-" + Guid.NewGuid().ToString("N")[..8]);
var game = Path.Combine(root, "g");
Directory.CreateDirectory(Path.Combine(game, "AstralParty_INT_Data"));
File.WriteAllText(Path.Combine(game, "AstralParty_INT.exe"), "inert fixture never executed");
var log = Path.Combine(game, "BepInEx", "BetterAstralParty-Diagnostics", "current.log");
Directory.CreateDirectory(Path.GetDirectoryName(log)!);
File.WriteAllText(log, "2026-10-04T10:00:00Z | phase=7\nAuthorization: Bearer FAKE_NET6_BEARER\n2026-10-04T10:00:01Z | performance frames=42\n닉네임=FAKE_NET6_NICK\n");
var plugin = Path.Combine(game, "BepInEx", "plugins", "BetterAstralParty", "BetterAstralParty.dll");
Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
File.Copy(Assembly.GetExecutingAssembly().Location, plugin);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
BundleResult Collect(string hub, string fallback) => DiagnosticBundle.Collect(game, Path.Combine(root, "missing-local"), Path.Combine(root, "missing-temp"), Path.Combine(root, "missing-unity"), hub, fallback, "6.0");
var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log));
BundleResult bundle;
using (var writer = File.Open(log, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
{
    bundle = Collect(Path.Combine(root, "hub"), Path.Combine(root, "hub2"));
    Check(writer.CanWrite, "net6 shared writer preserved");
}
using (var zip = ZipFile.OpenRead(bundle.ArchivePath))
{
    var entry = zip.GetEntry("logs/game-diagnostics-current.log")!;
    using var text = new StreamReader(entry.Open());
    var value = text.ReadToEnd();
    Check(!value.Contains("FAKE_NET6"), "net6 English/Korean privacy filter");
    Check(value.Contains("public-phase; phase=7") && value.Contains("performance; frames=42"), "net6 useful public records survive");
    using var summaryReader = new StreamReader(zip.GetEntry("summary.json")!.Open());
    using var summary = JsonDocument.Parse(summaryReader.ReadToEnd());
    Check(summary.RootElement.GetProperty("environment").GetProperty("pluginFileProductVersion").GetString() == "1.0.0-rc.2"
        && summary.RootElement.GetProperty("environment").GetProperty("pluginVersion").GetString() == "unknown", "file product version kept distinct from actual plugin constant without assembly load");
    Check(summary.RootElement.GetProperty("missingOrOmitted").GetArrayLength() > 0, "net6 missing list recorded");
    using var catalogReader = new StreamReader(zip.GetEntry("catalog-inventory.json")!.Open());
    using var catalog = JsonDocument.Parse(catalogReader.ReadToEnd());
    Check(catalog.RootElement.GetProperty("status").GetString() == "missing" && !catalog.RootElement.GetProperty("contentsIncluded").GetBoolean(), "net6 missing catalog inventory has no raw contents");
}
Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(log))), "net6 source bytes preserved");
var unity = Path.Combine(root, "u");
var cache = Path.Combine(unity, "com.unity.addressables"); Directory.CreateDirectory(cache);
var catalogPath = Path.Combine(cache, "catalog_3.2.0.json");
File.WriteAllText(catalogPath, "FAKE_PRIVATE_CATALOG_CONTENT");
var catalogHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(catalogPath)));
var catalogBundle = DiagnosticBundle.Collect(game, "", "", unity, Path.Combine(root, "hub"), Path.Combine(root, "hub2"), "6.0");
using (var zip = ZipFile.OpenRead(catalogBundle.ArchivePath))
{
    using var reader = new StreamReader(zip.GetEntry("catalog-inventory.json")!.Open());
    var json = reader.ReadToEnd(); using var inventory = JsonDocument.Parse(json);
    var file = inventory.RootElement.GetProperty("files")[0];
    Check(file.GetProperty("filename").GetString() == "catalog_3.2.0.json" && file.GetProperty("sha256").GetString() == catalogHash
        && file.GetProperty("sizeBytes").GetInt64() == new FileInfo(catalogPath).Length, "net6 catalog filename size and SHA are exact");
    Check(!json.Contains("FAKE_PRIVATE") && catalogBundle.Included == bundle.Included, "net6 catalog contents and log quota stay unchanged");
}
Check(!Directory.EnumerateFiles(Path.GetDirectoryName(bundle.ArchivePath)!, "*.partial").Any(), "net6 atomic commit leaves no partial");
var blocked = Path.Combine(root, "occupied"); File.WriteAllText(blocked, "preserve");
var fail = false;
try { Collect(blocked, blocked); } catch (IOException) { fail = true; }
Check(fail && File.ReadAllText(blocked) == "preserve", "net6 unavailable output failure preserves file");
var remnants = Path.Combine(root, "remnants"); Directory.CreateDirectory(remnants);
var state = Path.Combine(root, "state", "BetterAstralParty"); var localLogs = Path.Combine(state, "Logs"); Directory.CreateDirectory(localLogs);
var receiptPath = Path.Combine(remnants, "BetterAstralParty.install.json"); File.WriteAllText(receiptPath, "FAKE_RAW_RECEIPT");
var remnantLog = Path.Combine(remnants, "BepInEx", "BetterAstralParty-Diagnostics", "current.log"); Directory.CreateDirectory(Path.GetDirectoryName(remnantLog)!);
File.WriteAllText(remnantLog, "2026-10-11T01:00:00Z | phase=7\nAuthorization: Bearer FAKE_RAW_RUNTIME_SECRET\n");
File.WriteAllText(Path.Combine(remnants, "BetterAstralParty.update-v3.version"), "FAKE_RAW_WATER");
var workId = new string('a', 32); var work = Path.Combine(remnants, "bap-v3-helperauto-" + workId); Directory.CreateDirectory(work);
File.WriteAllText(Path.Combine(work, "plan"), "FAKE_RAW_PLAN");
var targetKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(remnants).TrimEnd('\\').ToLowerInvariant())));
var backups = Path.Combine(state, "Backups", "v3", targetKey); Directory.CreateDirectory(backups);
File.WriteAllText(Path.Combine(backups, "pending.json"), "FAKE_RAW_BACKUP");
var credentials = Path.Combine(localLogs, "credential-store.json"); File.WriteAllText(credentials, "FAKE_RAW_CREDENTIAL");
var receiptHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(receiptPath)));
BundleResult damaged;
using (var secretLock = File.Open(credentials, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    damaged = DiagnosticBundle.Collect(remnants, localLogs, "", "", Path.Combine(root, "hub"), Path.Combine(root, "hub2"), "6.0");
    Check(secretLock.CanWrite, "net6 recovery inventory never opens credential stores");
}
using (var zip = ZipFile.OpenRead(damaged.ArchivePath))
{
    using var reader = new StreamReader(zip.GetEntry("recovery-inventory.json")!.Open()); var json = reader.ReadToEnd();
    using var inventory = JsonDocument.Parse(json); var data = inventory.RootElement;
    Check(!data.GetProperty("gameMarkersValidated").GetBoolean() && !data.GetProperty("installationVerified").GetBoolean()
        && !data.GetProperty("recoveryAuthenticated").GetBoolean(), "net6 markerless metadata is not installation or recovery verification");
    var files = data.GetProperty("files").EnumerateArray().ToArray();
    Check(files.Single(f => f.GetProperty("source").GetString() == "game/BetterAstralParty.install.json").GetProperty("sha256").GetString() == receiptHash
        && files.Single(f => f.GetProperty("source").GetString() == "game/BetterAstralParty-Launcher.exe").GetProperty("present").GetBoolean() == false,
        "net6 markerless exact file hash and missing owned file observed");
    Check(files.Single(f => f.GetProperty("source").GetString() == "installer-backups/pending.json").GetProperty("present").GetBoolean()
        && data.GetProperty("observations").EnumerateArray().Any(f => f.GetProperty("code").GetString() == "work-marker-observed" && f.GetProperty("potentialBlocker").GetBoolean()),
        "net6 target backup and intent-only work marker observed");
    Check(!json.Contains("FAKE_RAW") && !json.Contains(workId) && !json.Contains(targetKey) && !json.Contains(root), "net6 raw state and original root or operation identities excluded");
    using var summaryReader = new StreamReader(zip.GetEntry("summary.json")!.Open()); using var summary = JsonDocument.Parse(summaryReader.ReadToEnd());
    Check(!summary.RootElement.GetProperty("environment").GetProperty("gameRootValidated").GetBoolean()
        && summary.RootElement.GetProperty("collectorVersion").GetString() == "7", "net6 shared collection preserves markerless provenance");
    using var logReader = new StreamReader(zip.GetEntry("logs/game-diagnostics-current.log")!.Open()); var logText = logReader.ReadToEnd();
    Check(logText.Contains("public-phase; phase=7") && !logText.Contains("FAKE_RAW"), "net6 markerless owned logs preserve typed projection privacy");
}
Check(receiptHash == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(receiptPath)))
    && File.Exists(Path.Combine(backups, "pending.json")), "net6 diagnostics never mutates receipt or recovery evidence");
var report = JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), passed = checks.Count, cases = checks, scratch = root, realLogsRead = false, realGameRun = false, upload = false }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(root, "test-results.json"), report, new UTF8Encoding(false));
Console.WriteLine(report);
