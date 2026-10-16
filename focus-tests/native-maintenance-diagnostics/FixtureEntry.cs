#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class FixtureFolders
{
    internal static string Root = "";
    internal static bool BlockLocal, BlockTemp;
    internal static string GetFolderPath(Environment.SpecialFolder folder) => Path.Combine(Root, folder == Environment.SpecialFolder.UserProfile ? "synthetic-profile" : BlockLocal ? "occupied" : "synthetic-local");
    internal static string GetTempPath() => Path.Combine(Root, BlockTemp ? "occupied" : "synthetic-temp");
}
internal static partial class NativeMaintenanceDiagnosticsFixture
{
    private static readonly List<string> Passed = new List<string>();
    private static readonly Dictionary<string, string> Logs = new Dictionary<string, string>();
    private static int sequence;
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException("FAIL: " + name); Passed.Add(name); }
    private static string Io(string path) => WindowsPath.ForIo(WindowsPath.FromIo(path.Replace('/', '\\')));
    private static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Io(path)))).Replace("-", ""); }
    private static void Set(string name, string value) => typeof(NativeMaintenanceEntry).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
    private static Exception Captured(Exception value) { try { throw value; } catch (Exception error) { return error; } }
    private static string Save(Exception error)
    {
        var run = (++sequence).ToString("x8") + new string('0', 24);
        Set("_run", run); Set("_version", "1.3.0-rc.3.local.1"); Set("_operation", "Installation");
        Set("_package", Path.Combine(FixtureFolders.Root, "synthetic-package")); Set("_game", Path.Combine(FixtureFolders.Root, "synthetic-game"));
        var path = NativeMaintenanceEntry.Failure(error) ?? throw new InvalidOperationException("Synthetic failure log unavailable");
        Check(path.StartsWith(FixtureFolders.Root + "\\", StringComparison.OrdinalIgnoreCase), "producer log remains within isolated artifact root " + sequence);
        Logs.Add(path, Hash(path)); return File.ReadAllText(Io(path));
    }
    private static Exception Steam(string reason, string phase, int count = 1, bool invalidPid = false)
    {
        var status = new SteamProcessStatus();
        for (var i = 0; i < count; i++) status.Facts.Add(new SteamProcessObservation { Pid = invalidPid ? 0 : (uint)(4321 + i), Name = "steam", NativeError = 5 });
        return (Exception)typeof(NativeSteamMaintenance).GetMethod("Failure", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(string), typeof(string), typeof(SteamProcessStatus) }, null)!.Invoke(null, new object[] { reason, phase, status })!;
    }
    private static string Json(object value) => new JavaScriptSerializer { MaxJsonLength = 1048576 }.Serialize(value);
    private static Dictionary<string, object> Map(object value) => (Dictionary<string, object>)value;
    public static int Main(string[] args)
    {
        try {
            if (args.Length < 2 || args.Length > 3) throw new ArgumentException("Fresh fixture root and canonical artifacts root required");
            FixtureFolders.Root = Path.GetFullPath(args[0]);
            var artifacts = Path.GetFullPath(args[1]).TrimEnd('\\');
            if (!FixtureFolders.Root.StartsWith(artifacts + "\\", StringComparison.OrdinalIgnoreCase) || !FixtureFolders.Root.Contains("native-maintenance-diagnostics-") || Directory.Exists(Io(FixtureFolders.Root))) throw new InvalidOperationException("Fresh isolated artifact fixture required");
            Directory.CreateDirectory(Io(FixtureFolders.Root));
            if(args.Length==3&&args[2]=="failure-context")return RunFailureContext();
            var steam = Steam("ProcessObservationUnavailable", "InstallPreflight");
            steam.Data["BapInstallStage"] = "Steam shortcut planning";
            var first = Save(Captured(new ApplySafetyException(ApplyFailure.ProcessUncertain, 5, steam)));
            Check(first.Contains("type=BetterAstralParty.Updating.ApplySafetyException; HResult=0x80131500") && first.Contains("Exception: depth=0; recoveryReason=unknown; applyCode=5; nativeCode=5"), "producer preserves exact apply and native error provenance");
            Check(first.Contains("SteamDiagnostic: schema=1; reason=ProcessObservationUnavailable; phase=InstallPreflight;") && first.Contains("SteamProcess: index=1; pid=4321;")
                && first.Contains("SteamObservationProcess: index=1; pid=4321; kind=SteamClient; querySource=Unavailable; nativeError=5; exitAvailable=false; confirmedExit=false"), "actual native Steam producer reaches exact typed failure text");
            Check(!first.Contains("BapSteamDiagnostic:") && !first.Contains("BapSteamObservation:") && first.Contains("DiagnosticCoverage: exceptionDepth=2; exceptionChainOmitted=false; textTruncated=false; steamContext=included"), "native failure uses typed context and canonical coverage");
            var boundedSteam = Save(Steam("ProcessObservationUnavailable", "ShutdownObservation", 50));
            var written = Regex.Matches(boundedSteam, @"(?m)^SteamProcess:").Count;
            Check(written > 0 && written <= 48 && Regex.Matches(boundedSteam, @"(?m)^SteamObservationProcess:").Count == written
                && boundedSteam.Contains("observedCount=50; omittedCount=" + (50 - written)) && boundedSteam.Contains("steamContext=context-limit"),
                "actual native Steam producer caps complete paired facts and reports context omission");
            Exception deep = new IOException("Synthetic unrecorded tail"); for (var i = 0; i < 9; i++) deep = new IOException("Synthetic chain " + i, deep);
            var chain = Save(deep);
            Check(Regex.Matches(chain, @"(?m)^Exception:").Count == 8 && !chain.Contains("Synthetic unrecorded tail") && chain.Contains("DiagnosticCoverage: exceptionDepth=8; exceptionChainOmitted=true; textTruncated=false; steamContext=not-observed"), "producer bounds exception depth and marks omitted chain");
            var profile = FixtureFolders.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var textError = Captured(new IOException("Synthetic path " + profile + "\\owned.dll\r\nAuthorization: Bearer FAKE_NATIVE_DIAGNOSTIC_SECRET\r\n" + new string('M', 9000)));
            textError.Data["BapEmergencyLastTarget"] = new string('R', 17000); var boundedText = Save(textError);
            Check(boundedText.Contains("%USERPROFILE%\\owned.dll") && !boundedText.Contains("synthetic-profile\\owned.dll"), "exact SaveLog masks synthetic profile before collection");
            var message = Regex.Match(boundedText, @"(?s)Message: (.*?)\r\nStack:").Groups[1].Value;
            var data = Regex.Match(boundedText, @"(?m)^BapEmergencyLastTarget: ([R]+)").Groups[1].Value;
            Check(message.Length <= 8192 && data.Length == 16384 && boundedText.Contains("textTruncated=true; steamContext=not-observed"), "producer bounds message/data and reports text truncation");
            var invalid = Steam("ProcessObservationUnavailable", "InstallPreflight", invalidPid: true);
            Check(Save(invalid).Contains("steamContext=invalid-context"), "actual native Steam renderer rejects malformed process identity");
            var missingTyped = Steam("ProcessObservationUnavailable", "InstallPreflight"); missingTyped.Data.Remove("BapSteamLog"); missingTyped.Data.Remove("BapSteamLogStatus");
            missingTyped.Data["BapSteamDiagnostic"] = "LEGACY_JSON_MUST_NOT_PASS_THROUGH";
            Check(Save(missingTyped).Contains("steamContext=invalid-context"), "missing typed producer metadata reports unavailable context without raw JSON fallback");
            Exception large = new IOException(new string('L', 9000));
            for (var i = 0; i < 8; i++) { large.Data["BapEmergencyLastTarget"] = new string('R', 17000); large.Data["BapInstallStage"] = new string('S', 17000); if (i < 7) large = new IOException(new string('L', 9000), large); }
            var limited = Save(large);
            Check(limited.Length <= 196608 + 64 && limited.Contains("DiagnosticCoverage: log-text-limit"), "producer total text limit reports global omission");
            File.WriteAllText(Io(Path.Combine(FixtureFolders.Root, "occupied")), "inert path broker obstruction"); FixtureFolders.BlockLocal = FixtureFolders.BlockTemp = true;
            var original = new IOException("original failure remains original"); var hresult = original.HResult;
            Check(NativeMaintenanceEntry.Failure(original) == null && original.Message == "original failure remains original" && original.HResult == hresult && original.Data.Count == 0, "diagnostic storage failure does not mask or mutate original failure");
            FixtureFolders.BlockTemp = false; var fallback = Save(new IOException("Synthetic fallback storage"));
            Check(fallback.Contains("BAP-INSTALL-FAILED") && Logs.Keys.Last().Contains("synthetic-temp"), "producer fallback remains isolated when primary folder is blocked"); FixtureFolders.BlockLocal = false;
            var result = DiagnosticBundle.Collect("", Path.Combine(FixtureFolders.Root, "synthetic-local", "BetterAstralParty", "Logs"), Path.Combine(FixtureFolders.Root, "synthetic-temp", "BetterAstralParty"),
                Path.Combine(FixtureFolders.Root, "absent-unity"), Path.Combine(FixtureFolders.Root, "hub"), Path.Combine(FixtureFolders.Root, "hub-fallback"), "4.8");
            string summary, collected;
            using (var zip = ZipFile.OpenRead(Io(result.ArchivePath))) {
                using (var read = new StreamReader(zip.GetEntry("summary.json")!.Open())) summary = read.ReadToEnd();
                collected = string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(e => { using (var read = new StreamReader(e.Open())) return read.ReadToEnd(); }));
            }
            Check(result.Included == Logs.Count && collected.Contains("applyCode=5; nativeCode=5") && collected.Contains("BapInstallStage: Steam shortcut planning"), "actual source-to-ZIP includes every bounded native producer log and cause/stage");
            Check(collected.Contains("SteamObservationProcess: index=1; pid=4321; kind=SteamClient; querySource=Unavailable; nativeError=5; exitAvailable=false; confirmedExit=false")
                && collected.Contains("observedCount=50; omittedCount=" + (50 - written)), "ZIP preserves actual typed Steam availability and omitted process count");
            Check(collected.Contains("DiagnosticCoverage: exceptionDepth=8; exceptionChainOmitted=true; textTruncated=false; steamContext=not-observed") && collected.Contains("DiagnosticCoverage: log-text-limit"), "ZIP retains exception-chain and total text omission coverage");
            Check(!collected.Contains("LEGACY_JSON_MUST_NOT_PASS_THROUGH") && !collected.Contains("BapSteamDiagnostic:") && !collected.Contains("BapSteamObservation:")
                && collected.Contains("steamContext=invalid-context") && !summary.Contains("invalid-failure-context"), "ZIP preserves actual invalid-context coverage and accepts all valid producer records without raw JSON passthrough");
            Check(!collected.Contains("FAKE_NATIVE_DIAGNOSTIC_SECRET") && !collected.Contains(FixtureFolders.Root) && !collected.Contains("synthetic-profile"), "ZIP redacts fake bearer and original isolated paths");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 }; var root = Map(serializer.DeserializeObject(summary));
            var rows = ((object[])root["files"]).Select(Map).Where(row => (string)row["status"] == "included").ToArray();
            var aliases = rows.SelectMany(row => (object[])row["operationAliases"]).Cast<string>().ToArray();
            Check(rows.Length == Logs.Count && rows.All(row => (string)row["sourceKind"] == "installer-uninstaller-error") && aliases.Length == Logs.Count && aliases.Distinct().Count() == aliases.Length
                && aliases.All(alias => Regex.IsMatch(alias, @"\A[a-f0-9]{32}\z") && collected.Contains("Run: " + alias) && !Enumerable.Range(1, sequence).Select(i => i.ToString("x8") + new string('0', 24)).Contains(alias)), "ZIP and summary preserve source provenance using correlated fresh operation aliases");
            Check(summary.Contains("missingOrOmitted") && summary.Contains("\"reason\":\"missing\"") && Logs.All(row => Hash(row.Key) == row.Value), "collector reports missing inputs and preserves producer log bytes");
            EmergencyProducerBounds();
            var native = new { Passed = true, Checks = Passed, ArchivePath = result.ArchivePath, ArchiveSha256 = Hash(result.ArchivePath), ProducerLogs = Logs,
                FolderBroker = "Only derived Environment.GetFolderPath/Path.GetTempPath discovery is substituted to artifact-root synthetic folders. Producer literal body is hash-bound by runner.", RealMaintenance = false };
            File.WriteAllText(Io(Path.Combine(FixtureFolders.Root, "verification.json")), Json(native), new UTF8Encoding(false));
            Console.WriteLine("Native maintenance diagnostics: " + Passed.Count + " isolated assertions passed; exact producer and collector, no real user folders/process/Steam/game/registry/key/network/AV."); return 0;
        } catch (Exception error) {
            File.WriteAllText(Io(Path.Combine(FixtureFolders.Root, "verification.json")), Json(new { Passed = false, Checks = Passed, ErrorType = error.GetType().FullName, Error = error.Message }), new UTF8Encoding(false));
            Console.Error.WriteLine(error); return 1;
        }
    }
    private static void EmergencyProducerBounds()
    {
        var original=FixtureFolders.Root;FixtureFolders.Root=Path.Combine(original,"emergency-producer-bounds");Directory.CreateDirectory(Io(FixtureFolders.Root));
        try
        {
            var profile=FixtureFolders.GetFolderPath(Environment.SpecialFolder.UserProfile);var valid=new IOException("inert emergency partial cleanup");
            valid.Data["BapEmergencyLastTarget"]=profile+"\\owned.cmd";valid.Data["BapEmergencyDeletedFiles"]=4096;valid.Data["BapEmergencyDeletedDirectories"]=2048;valid.Data["BapEmergencyObservedFiles"]=4096;
            var text=Save(valid);Check(text.Contains("BapEmergencyLastTarget: %USERPROFILE%\\owned.cmd")&&text.Contains("BapEmergencyDeletedFiles: 4096")&&text.Contains("BapEmergencyDeletedDirectories: 2048")&&text.Contains("BapEmergencyObservedFiles: 4096"),"emergency counts retain exact inclusive bounds and last-target profile mask");
            foreach(var value in new object[]{-1,4097,"12"}){var invalid=new IOException("inert rejected counter");invalid.Data["BapEmergencyDeletedFiles"]=value;invalid.Data["BapEmergencyDeletedDirectories"]=value is int number&&number==4097?2049:value;invalid.Data["BapEmergencyObservedFiles"]=value;var body=Save(invalid);Check(!body.Contains("BapEmergencyDeletedFiles:")&&!body.Contains("BapEmergencyDeletedDirectories:")&&!body.Contains("BapEmergencyObservedFiles:"),"negative, oversized or string emergency counts never exported");}
            var huge=new IOException("inert emergency oversized target");huge.Data["BapEmergencyLastTarget"]=new string('T',17000)+"FORBIDDEN_TARGET_TAIL";text=Save(huge);var target=Regex.Match(text,@"(?m)^BapEmergencyLastTarget: ([T]+)").Groups[1].Value;Check(target.Length==16384&&!text.Contains("FORBIDDEN_TARGET_TAIL")&&text.Contains("textTruncated=true"),"emergency last-target field bounded with explicit truncation");
            var result=DiagnosticBundle.Collect("",Path.Combine(FixtureFolders.Root,"synthetic-local/BetterAstralParty/Logs"),"","",Path.Combine(FixtureFolders.Root,"hub"),Path.Combine(FixtureFolders.Root,"fallback"),"4.8");string all;
            using(var zip=ZipFile.OpenRead(Io(result.ArchivePath)))all=string.Join("\n",zip.Entries.Select(entry=>{using(var reader=new StreamReader(entry.Open()))return reader.ReadToEnd();}));
            Check(all.Contains("BapEmergencyDeletedFiles: 4096")&&all.Contains("BapEmergencyDeletedDirectories: 2048")&&all.Contains("BapEmergencyObservedFiles: 4096")&&all.Contains("BapEmergencyLastTarget:")&&!all.Contains(FixtureFolders.Root)&&!all.Contains(profile)&&!all.Contains("FORBIDDEN_TARGET_TAIL"),"emergency actual producer to ZIP retains bounded counts and masks source target identities");
        }
        finally{FixtureFolders.Root=original;}
    }
}
