#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// Compile-time substitutes only: no production switches, native processes, registry or signing.
internal static class FixtureControl
{
    internal static readonly List<string> Events = new List<string>();
    internal static bool NeedsShortcut, Pending, Generation, FailConfig, FailRemoval, FailComplete, LockHeld, PendingUpdate;
    internal static bool CompatibilityAllowed = true;
    internal static Exception? CompatibilityError, LastFailure, PlanFailure;
    internal static string? LastFailurePhase;
    internal static Action? PendingRestore;
    internal static BetterAstralParty.Installation.NativeMaintenanceBundle? Bundle;
    internal const string Identity = "00000001:0000000000000001", Observation = "one-process-observation";
    internal static void Reset() { Events.Clear(); NeedsShortcut = Pending = Generation = FailConfig = FailRemoval = FailComplete = LockHeld = PendingUpdate = false; CompatibilityAllowed = true; CompatibilityError = LastFailure = PlanFailure = null; LastFailurePhase = null; PendingRestore = null; }
    internal sealed class Lease : IDisposable { private readonly Action _close; internal Lease(Action close) { _close = close; } public void Dispose() { _close(); } }
}
namespace BetterAstralParty.Updating
{
    internal enum UpdateFailure { InvalidDescriptor }
    internal sealed class UpdateValidationException : Exception { internal UpdateValidationException(UpdateFailure failure) { } }
    internal enum ApplyFailure { CompatibilityRequired }
    internal sealed class ApplySafetyException : Exception {
        internal readonly ApplyFailure Failure;
        internal ApplySafetyException(ApplyFailure failure, int error = 0) : base("Update safety: " + failure) { Failure = failure; }
    }
    internal static class UpdateTrust
    {
        internal const string PluginPath = "BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll";
        internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
    internal static class UpdateTransaction { internal const string HighWaterPath = "BetterAstralParty.update-v5.version"; }
    internal static class UpdateWorkPath { internal const string Root = "BetterAstralParty.Update"; }
    internal static class LegacyWork {
        internal static List<string> Finished(string game) { FixtureControl.Events.Add("legacy-work-plan"); return new List<string>(); }
        internal static int Remove(string game, Action<string> output) { FixtureControl.Events.Add("legacy-work-clean"); return 0; }
    }
    internal static class UpdateFaults {
        internal static int RemovableArchived(string game) { FixtureControl.Events.Add("archived-plan"); return 0; }
        internal static int RemoveArchived(string game, Action<string> output) { FixtureControl.Events.Add("archived-clean"); return 0; }
    }
    internal static class OrdinaryInstallRecovery { internal const string LegacyLauncher = "BetterAstralParty-Launcher.exe", LegacyHelper = "BetterAstralParty-UpdateHelper.exe", LegacyHelperReceipt = "BetterAstralParty.helper.receipt"; }
    // 1.3.x receipt substitute: baseline 1.3.0 is the only legacy shape the takeover accepts.
    internal sealed class LegacyReceipt
    {
        internal bool? LoaderBefore;
        internal readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static bool Is(byte[] bytes) { try { Parse(bytes); return true; } catch (InvalidDataException) { return false; } }
        internal static LegacyReceipt Parse(byte[] bytes) {
            var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
            if ((string)map["baseline"] != "1.3.0") throw new InvalidDataException("not a 1.3.x receipt");
            var result = new LegacyReceipt { LoaderBefore = map["loaderBefore"] as bool? };
            foreach (var row in ((System.Collections.IEnumerable)map["files"]).Cast<object>().Cast<Dictionary<string, object>>()) result.Files.Add((string)row["path"], (string)row["sha256"]);
            return result;
        }
    }
    internal sealed class InstalledPluginMetadata
    {
        internal string Version = "";
        internal static InstalledPluginMetadata Read(byte[] bytes) { return new InstalledPluginMetadata { Version = Encoding.UTF8.GetString(bytes) }; }
        internal static InstalledPluginMetadata ReadMaintenance(byte[] bytes) { return Read(bytes); }
    }
    internal sealed class InstallReceipt
    {
        internal const string Path = "BetterAstralParty.install.json";
        internal bool? LoaderBefore;
        internal readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static InstallReceipt Parse(byte[] bytes) {
            var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
            if ((string)map["baseline"] != "1.4.0") throw new InvalidDataException("unsupported receipt baseline");
            var result = new InstallReceipt { LoaderBefore = map["loaderBefore"] as bool? };
            foreach (var row in ((System.Collections.IEnumerable)map["files"]).Cast<object>().Cast<Dictionary<string, object>>()) result.Files.Add((string)row["path"], (string)row["sha256"]);
            return result;
        }
    }
    internal sealed class WindowsFileFence : IDisposable
    {
        internal readonly string Root;
        internal readonly Identity RootIdentity = new Identity();
        internal sealed class Identity { internal string Text => FixtureControl.Identity; }
        internal WindowsFileFence(string root) { Root = root; }
        internal string Full(string relative) { return System.IO.Path.Combine(Root, relative.Replace('/', '\\')); }
        internal void EnsureDirectory(string relative) { Directory.CreateDirectory(Full(relative)); }
        internal FileLease OpenFile(string relative, bool mutable = false, bool create = false) { return new FileLease(Full(relative), mutable, create); }
        internal sealed class FileLease : IDisposable {
            internal readonly FileStream Stream; internal readonly string Path;
            internal FileLease(string path, bool mutable, bool create) { Path = path; Stream = new FileStream(path, create ? FileMode.CreateNew : FileMode.Open, mutable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.None); }
            internal string Hash() { var position = Stream.Position; Stream.Position = 0; using (var sha = SHA256.Create()) { var value = BitConverter.ToString(sha.ComputeHash(Stream)).Replace("-", ""); Stream.Position = position; return value; } }
            internal void RemoveOrdinaryRecoveryTarget(string relative) { if (FixtureControl.FailRemoval) throw new IOException("fixture legacy removal failure"); Stream.Dispose(); File.Delete(Path); FixtureControl.Events.Add("remove:" + relative); }
            internal void Flush() { Stream.Flush(true); if (FixtureControl.FailConfig && Path.EndsWith("BepInEx.cfg", StringComparison.Ordinal)) throw new IOException("fixture config commit interrupted"); }
            public void Dispose() { Stream.Dispose(); }
        }
        public void Dispose() { }
    }
}
namespace BetterAstralParty.Installation
{
    internal static class MaintenanceJson
    {
        internal static byte[] Write(object value) { return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)); }
        internal static string Text(Dictionary<string, object?> map, string key) { return (string)map[key]!; }
        internal static Dictionary<string, object?> Map(object? value) { return (Dictionary<string, object?>)value!; }
    }
    internal sealed class NativeMaintenanceBundle : IDisposable
    {
        internal static readonly Dictionary<string, string> OwnedSources = new Dictionary<string, string> {
            {"AstralParty-Vanilla.cmd","tools/AstralParty-Vanilla.cmd"}, {Updating.UpdateTrust.PluginPath,"artifacts/BetterAstralParty.dll"},
            {"BetterAstralParty-Mod.cmd","tools/BetterAstralParty-Mod.cmd"},
            {"BetterAstralParty.compatibility.json","artifacts/BetterAstralParty.compatibility.json"} };
        internal readonly string GameRoot, PackageRoot, Version = "1.4.1", ManifestHash = new string('A', 64), RootIdentity = FixtureControl.Identity;
        internal readonly Updating.WindowsFileFence Source;
        internal readonly Dictionary<string, string> Hashes = new Dictionary<string, string>(), Expected = new Dictionary<string, string>();
        internal Dictionary<string, object?>? Recovery = null;
        internal string? Mode;
        internal Dictionary<string, string?>? Observed;
        internal NativeMaintenanceBundle(string root) {
            GameRoot = System.IO.Path.Combine(root,"Game"); PackageRoot = System.IO.Path.Combine(root,"Package"); Directory.CreateDirectory(GameRoot); Directory.CreateDirectory(PackageRoot); Source = new Updating.WindowsFileFence(PackageRoot);
            foreach (var row in OwnedSources) { var bytes = Encoding.UTF8.GetBytes(row.Key == Updating.UpdateTrust.PluginPath ? Version : "inert:" + row.Key); WriteSource(row.Value, bytes); Expected.Add(row.Key, Updating.UpdateTrust.Hash(bytes)); }
            WriteSource(".deps/bepinex/doorstop_config.ini", Encoding.UTF8.GetBytes("[General]\r\nenabled=true\r\n"));
            WriteSource(".deps/bepinex/BepInEx/core/inert.dll", Encoding.UTF8.GetBytes("inert shared core"));
            FixtureControl.Bundle = this;
        }
        private void WriteSource(string relative, byte[] bytes) { var path = Source.Full(relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); Hashes.Add(relative, Updating.UpdateTrust.Hash(bytes)); }
        internal byte[] Bytes(string relative, long limit) { return File.ReadAllBytes(Source.Full(relative)); }
        internal byte[]? ReadTarget(string relative, long limit = 64 * 1048576) { var path = System.IO.Path.Combine(GameRoot,relative.Replace('/', '\\')); return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        internal string? TargetHash(string relative) { var bytes = ReadTarget(relative); return bytes == null ? null : Updating.UpdateTrust.Hash(bytes); }
        internal void AssertRoot() { }
        internal void AssertUnchanged() { if (Observed == null || Observed.Any(row => TargetHash(row.Key) != row.Value)) throw new InvalidDataException("fixture changed after preflight"); }
        public void Dispose() { }
    }
    internal static class NativeMaintenance
    {
        internal sealed class Entry { internal string Target = "", Backup = ""; internal string? Hash; internal bool Existed; }
        internal sealed class Transaction { internal string JournalRoot = "", JournalPath = "", JournalHash = "", BackupDir = ""; internal readonly List<Entry> Entries = new List<Entry>(); }
        internal sealed class InstallLock : IDisposable {
            internal readonly string Root; internal readonly Updating.WindowsFileFence.FileLease File;
            internal InstallLock(string root) { Root = root; Directory.CreateDirectory(root); File = new Updating.WindowsFileFence.FileLease(System.IO.Path.Combine(root,"pending.json.lock"),true,true); FixtureControl.LockHeld=true; }
            public void Dispose() { FixtureControl.Events.Add("ordinary-unlock"); FixtureControl.LockHeld=false; File.Dispose(); }
        }
        internal static string JournalRoot(string game) { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(game)!,"Backups"); }
        internal static string JournalPath(string game) { return System.IO.Path.Combine(JournalRoot(game),"pending.json"); }
        internal static string LegacyJournalPath(string game) { return System.IO.Path.Combine(JournalRoot(game),"legacy-v4-pending.json"); }
        internal static InstallLock AcquireInstallLock(NativeMaintenanceBundle bundle) { FixtureControl.Events.Add("ordinary-lock"); return new InstallLock(JournalRoot(bundle.GameRoot)); }
        internal static bool RecoverPending(NativeMaintenanceBundle bundle, Func<string,string?> consent, Action<string> output, bool whatIf=false) { FixtureControl.Events.Add(whatIf?"preview-pending":"ordinary-recovery"); if (!FixtureControl.Pending) return true; if(whatIf)return false; if(!FixtureControl.LockHeld)throw new InvalidOperationException("Fixture recovery requires the ordinary lock"); if(consent("저장된 작업 복원 / Restore fixture pending maintenance? [y/N]")!="y")throw new OperationCanceledException("Fixture recovery cancelled"); FixtureControl.PendingRestore?.Invoke(); FixtureControl.Pending=false; File.Delete(JournalPath(bundle.GameRoot)); return true; }
        internal static void AssertTarget(string root,string absolute) { if(!absolute.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(); }
        internal static void TestWritable(string[] targets) { FixtureControl.Events.Add("write-probe"); }
        internal static Transaction StartTransaction(NativeMaintenanceBundle bundle,string[] targets,long installBytes) {
            FixtureControl.Events.Add("ordinary-start"); var txn = new Transaction {JournalRoot=JournalRoot(bundle.GameRoot),JournalPath=JournalPath(bundle.GameRoot),JournalHash=new string('B',64),BackupDir=System.IO.Path.Combine(JournalRoot(bundle.GameRoot),"saved")}; Directory.CreateDirectory(txn.BackupDir);
            foreach(var target in targets) { var exists=File.Exists(target); var backup=System.IO.Path.Combine(txn.BackupDir,txn.Entries.Count+".bin"); if(exists)File.Copy(target,backup,false); txn.Entries.Add(new Entry {Target=target,Backup=backup,Existed=exists,Hash=exists?Updating.UpdateTrust.Hash(File.ReadAllBytes(target)):null}); }
            File.WriteAllText(txn.JournalPath,"inert journal"); return txn;
        }
        internal static void CompleteTransaction(NativeMaintenanceBundle bundle,Transaction txn,string[] targets) { FixtureControl.Events.Add("ordinary-complete"); if(FixtureControl.FailComplete)throw new IOException("fixture completion failure"); File.Delete(txn.JournalPath); }
        internal static void RestoreTransaction(NativeMaintenanceBundle bundle,Transaction txn,string[] targets,Dictionary<string,string?> expected) {
            FixtureControl.Events.Add("ordinary-rollback"); if(!FixtureControl.LockHeld)throw new InvalidOperationException("rollback lost ordinary lock");
            foreach(var row in txn.Entries) { var current=File.Exists(row.Target)?Updating.UpdateTrust.Hash(File.ReadAllBytes(row.Target)):null; if(current!=row.Hash && (!expected.TryGetValue(row.Target,out var hash)||hash!=current))throw new InvalidOperationException("unapproved current rollback fingerprint"); if(row.Existed)File.Copy(row.Backup,row.Target,true); else if(File.Exists(row.Target))File.Delete(row.Target); }
            File.Delete(txn.JournalPath);
        }
    }
    internal sealed class SteamSessionState { internal bool Restart; }
    internal sealed class SteamProfileUnavailableException : InvalidOperationException
    { internal readonly string Reason; internal readonly string? Guidance; internal SteamProfileUnavailableException(string reason, string? guidance = null) : base(reason) { Reason = reason; Guidance = guidance; } }
    internal static class NativeSteamMaintenance {
        internal static System.Action<System.Exception>? WaitingDiagnostic = null;
        internal static void WaitGameClosed(string root,Func<string,string?> consent,Action<string> output) { FixtureControl.Events.Add("game-stop"); }
        internal static void AssertGameStopped(string root) { FixtureControl.Events.Add("game-recheck"); }
        internal static bool PlanRegister(string steam,uint account,string game,Action<Exception>? unavailable=null) {
            FixtureControl.Events.Add("shortcut-plan");
            if(FixtureControl.PlanFailure!=null) { if(unavailable==null)throw FixtureControl.PlanFailure; unavailable(FixtureControl.PlanFailure);return false; }
            return FixtureControl.NeedsShortcut;
        }
        internal static void StopSteam(string root,SteamSessionState state,Func<string,string?> consent,Action<string> output) { FixtureControl.Events.Add("steam-stop"); state.Restart=true; }
        internal static void AssertStopped(string root,string phase) { FixtureControl.Events.Add("steam-recheck"); }
        internal static object? Register(string steam,uint account,string game,Action<string> output) { FixtureControl.Events.Add("shortcut-register"); return null; }
        internal static void ResumeSteam(SteamSessionState state,Action<string> output) { if(state.Restart)FixtureControl.Events.Add("steam-resume"); }
    }
    internal static class NativeReinstallGeneration {
        internal sealed class Plan { internal readonly string Version = "1.4.0"; }
        internal static Plan? Review(string game,string package,string version,string manifest,string backups) { FixtureControl.Events.Add("generation-review"); return FixtureControl.Generation?new Plan():null; }
        internal static void Retire(Plan plan,string game,string package,string version,string manifest,string backups,Updating.WindowsFileFence.FileLease held) { FixtureControl.Events.Add("generation-retire"); FixtureControl.Generation=false; }
    }
    internal static class GameCompatibility {
        internal sealed class Result {
            internal readonly bool Allowed = FixtureControl.CompatibilityAllowed;
            internal readonly string[] Issues = FixtureControl.CompatibilityAllowed ? Array.Empty<string>() : new[] { "Changed [fixture]: game/GameAssembly.dll" }, Warnings=Array.Empty<string>();
            internal readonly Exception? Error = FixtureControl.CompatibilityError;
        }
        internal static Result Check(string game,string manifest,string? loader=null) { FixtureControl.Events.Add("compatibility"); return new Result(); }
    }
}
internal static class SetupHost
{
    internal static void RecordInstallerStage(string phase,string outcome) { FixtureControl.Events.Add("stage:"+phase+":"+outcome); }
    internal static void RecordInstallerFailure(string phase,Exception error) { FixtureControl.Events.Add("error:"+phase); FixtureControl.LastFailurePhase = phase; FixtureControl.LastFailure = error; }
    internal static void ValidateWholeInputs(string game,string package,string version) { FixtureControl.Events.Add("inputs-validate"); }
    internal static bool HasPendingUpdate(string game) { FixtureControl.Events.Add("update-check"); return FixtureControl.PendingUpdate; }
    internal static string RecoverPendingUpdate(string game) { FixtureControl.Events.Add("update-recover"); FixtureControl.PendingUpdate = false; return "RolledBack"; }
    internal static string ReadInstallerState(string game) {
        FixtureControl.Events.Add("state-observe");
        var path=System.IO.Path.Combine(game,BetterAstralParty.Updating.UpdateTransaction.HighWaterPath);var bytes=File.Exists(path)?File.ReadAllBytes(path):null;
        return "BetterAstralParty.InstallerState/v1\nroot="+FixtureControl.Identity+"\nversion="+(bytes==null?"":Encoding.UTF8.GetString(bytes))+"\nwater-sha="+(bytes==null?"":BetterAstralParty.Updating.UpdateTrust.Hash(bytes))+"\n";
    }
    internal static string PrepareWholeReceipt(string game,string package,string version,string before) {
        FixtureControl.Events.Add("receipt-prepare"); var bundle=FixtureControl.Bundle!;
        return new JavaScriptSerializer().Serialize(new {schema=1,baseline="1.4.0",owner="kr.betterastralparty.mod",loaderBefore=before=="null"?(bool?)null:before=="true",channelState="fixture",files=bundle.Expected.Select(row=>new {path=row.Key,sha256=row.Value}).ToArray()});
    }
    internal static byte[]? PrepareHighWater(string game,string package,string version) { FixtureControl.Events.Add("water-prepare"); return Encoding.UTF8.GetBytes(version); }
    internal static IDisposable GuardMaintenance(string game) { FixtureControl.Events.Add("guard"); return new FixtureControl.Lease(()=>FixtureControl.Events.Add("guard-release")); }
}
