using System.IO.Compression;
using System.Text.Json;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Observability;
using BetterAstralParty.Updating;

var root = Path.GetFullPath(args[0]);
if (Directory.Exists(root)) throw new Exception("Fresh synthetic root required");
Directory.CreateDirectory(root);
File.WriteAllText(Path.Combine(root, "AstralParty_INT.exe"), "INERT_NEVER_EXECUTED");
Directory.CreateDirectory(Path.Combine(root, "AstralParty_INT_Data"));
Directory.CreateDirectory(Path.Combine(root, "fixture-cache"));
const string version = "1.3.0-rc.1.local.2";
var checks = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
Check(DiagnosticIdentity.SafeVersion(version) == version, "bounded local version retained");
Check(DiagnosticIdentity.SafeVersion("1.3.0-rc.1.local.0") == "unknown", "invalid local version omitted");
Check(DiagnosticIdentity.SafeVersion(version + "/PRIVATE") == "unknown", "arbitrary version text omitted");
Check(System.Text.RegularExpressions.Regex.IsMatch(version, MinimalDiagnosticProjection.VersionPattern), "collector shares the bounded structured local version grammar");
using (var writer = DiagnosticOwner.Start(root, false, version, new string('A', 32))) {
    Check(DiagnosticHub.Bind(writer), "synthetic compatibility observer bound to production diagnostics");
    try {
    writer.UpdateStatus(DiagnosticFeature.Coordinator, DiagnosticUpdateState.NoCompatibleUpdate);
    writer.UpdateStatus(DiagnosticFeature.Coordinator, DiagnosticUpdateState.CompatibilityRejected);
    writer.Failure(DiagnosticFeature.Download, DiagnosticPhase.PackageAsset, DiagnosticCode.Transport,
        new IOException("https://invalid.example/PRIVATE_TOKEN"));
    using (var fence = new WindowsFileFence(root)) using (var image = fence.OpenFile("AstralParty_INT.exe")) {
        foreach (var invalid in new[] { "{\"schema\":null,\"files\":[],\"cacheCatalogs\":[]}", "{" }) {
            using var compatibility = UpdateCompatibility.Require(fence, System.Text.Encoding.UTF8.GetBytes(invalid), image);
            Check(compatibility.Warnings.Count > 0, "invalid observation schema warns without blocking the owned operation");
            compatibility.Recheck();
        }
    }
    Check(writer.Stop(DiagnosticEndReason.OwnerDisposed), "production writer completed synthetic recovery observations");
    var health = new LiveDiagnosticHealth(true, false, true, false, 0, version, new string('A', 32), writer.ProcessSession, DateTime.UtcNow);
    var bundle = DiagnosticBundle.Collect(root, "", "", "", Path.Combine(root, "hub"), Path.Combine(root, "fallback"), "synthetic", version, health);
    using var zip = ZipFile.OpenRead(bundle.ArchivePath);
    using var reader = new StreamReader(zip.GetEntry("logs/game-minimal-current.log")!.Open());
    var text = reader.ReadToEnd();
    Check(text.Contains(version), "local version survives writer to ZIP");
    Check(text.Contains("NoCompatibleUpdate") && text.Contains("CompatibilityRejected"), "patch absence and incompatible candidate remain distinct in ZIP");
    Check(text.Contains("Transport") && text.Contains("PackageAsset"), "lookup failure phase and reason survive ZIP");
    Check(text.Contains("\"phase\":\"Catalog\"") && text.Contains("\"outcome\":\"Partial\"") && !text.Contains("\"apply_code\":" + (int)ApplyFailure.CompatibilityRequired), "actual observation schema failure reaches ZIP without inventing an update block");
    Check(!text.Contains("PRIVATE_TOKEN") && !text.Contains("invalid.example") && !text.Contains(root), "credentials URLs and private paths absent from ZIP");
    using var summaryReader = new StreamReader(zip.GetEntry("summary.json")!.Open());
    using var summary = JsonDocument.Parse(summaryReader.ReadToEnd());
    Check(summary.RootElement.GetProperty("environment").GetProperty("pluginVersion").GetString() == version, "controlled local running version retained in summary");
    Check(summary.RootElement.GetProperty("liveHostHealth").GetProperty("runtime").GetProperty("modVersion").GetString() == version, "controlled local live owner version retained in summary");
    } finally { DiagnosticHub.Unbind(writer); }
}
File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, syntheticOnly = true, installed = false, published = false }));
Console.WriteLine($"Recovery diagnostic checks passed: {checks.Count}");
