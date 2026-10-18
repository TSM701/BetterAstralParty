#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static class EmergencyFixtureFolders
{
    internal static string GetFolderPath(Environment.SpecialFolder folder) { return FixtureBoundary.Root.TrimEnd('\\') + (folder == Environment.SpecialFolder.UserProfile ? "\\synthetic-profile" : "\\synthetic-local"); }
    internal static string GetTempPath() { return FixtureBoundary.Root.TrimEnd('\\') + "\\synthetic-temp"; }
}

internal static partial class NativeMaintenanceIntegrationFixture
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateHardLinkW(string path, string source, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateSymbolicLinkW(string path, string source, uint flags);
    private static readonly string[] EmergencyFiles = InstallReceipt.Owned.Concat(new[] { OrdinaryInstallRecovery.LegacyLauncher, OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyHelperReceipt, InstallReceipt.Path, "BetterAstralParty-Steam.ps1",
        "BepInEx/config/kr.betterastralparty.mod.cfg", "BepInEx/BetterAstralParty-Diagnostics/current.log", "BepInEx/BetterAstralParty-Diagnostics/previous.log",
        "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log", "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log", "BetterAstralParty-Compatibility.log", "BetterAstralParty-LaunchError.log", ChannelTransitionIntent.AdoptionPath }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    // v5 is the current journal scope; v4 (1.3.x) and v3 are the selected-root legacy scopes force removal also deletes.
    private static string EmergencyLegacyBackups(string root, string version = "v4")
    { var current = NativeMaintenance.JournalRoot(root); return Full(WindowsPath.Parent(WindowsPath.Parent(current)), version + "/" + WindowsPath.Leaf(current)); }
    private static string EmergencySeed(string name, out string steam)
    {
        var root = Full(Scratch, "emergency-cases/" + name + "/game"); steam = Full(Scratch, "emergency-cases/" + name + "/steam");
        Directory.CreateDirectory(WindowsPath.ForIo(root));
        using (var fence = new WindowsFileFence(root)) fence.CreatePrivateDirectory(UpdateWorkPath.Root);
        foreach (var dir in Directory.GetDirectories(WindowsPath.ForIo(Game), "*", SearchOption.AllDirectories)) {
            var relative = NativeMaintenance.Relative(Game, WindowsPath.FromIo(dir)); var target = Full(root, relative);
            Directory.CreateDirectory(WindowsPath.ForIo(target));
        }
        foreach (var source in Directory.GetFiles(WindowsPath.ForIo(Game), "*", SearchOption.AllDirectories)) Write(Full(root, NativeMaintenance.Relative(Game, WindowsPath.FromIo(source))), Read(WindowsPath.FromIo(source)));
        foreach (var path in EmergencyFiles.Where(path => !File.Exists(WindowsPath.ForIo(Full(root, path))))) Text(Full(root, path), "unverified emergency fixture: " + path);
        Text(Full(root, "BepInEx/plugins/BetterAstralParty/user-extra.keep"), "unrelated user bytes retained");
        Text(Full(root, "BetterAstralParty.UpdateLog.jsonl"), "diagnostic history retained");
        Text(Full(root, "check-compatibility.ps1"), "unverified generic script retained"); Text(Full(root, "steam-shortcut.ps1"), "unverified generic script retained");
        var shortcuts = NativeSteamMaintenance.EmptyShortcuts(); var unrelated = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
        unrelated.Add("appid", 987654U); unrelated.Add("AppName", "Unrelated fixture shortcut"); unrelated.Add("exe", "inert-other.exe"); unrelated.Add("LaunchOptions", "other");
        ((OrderedDictionary)shortcuts["shortcuts"]!).Add("99", unrelated); Write(Full(steam, "userdata/1/config/shortcuts.vdf"), NativeSteamMaintenance.WriteShortcuts(shortcuts));
        NativeSteamMaintenance.Register(steam, 1, root); return root;
    }
    private static Dictionary<string, string> EmergencyState(string root, string steam)
    {
        var result = Snapshot(root); foreach (var row in Snapshot(steam).Concat(Snapshot(NativeMaintenance.JournalRoot(root))).Concat(Snapshot(EmergencyLegacyBackups(root))).Concat(Snapshot(EmergencyLegacyBackups(root, "v3")))) result.Add(row.Key, row.Value); return result;
    }
    private static void EmergencySame(Dictionary<string, string> before, string root, string steam, string label)
    { var after = EmergencyState(root, steam); Check(before.Count == after.Count && before.All(row => after.TryGetValue(row.Key, out var hash) && hash == row.Value), label); }
    private static Exception EmergencyReject(Action action, string label)
    {
        try { action(); } catch (Exception error) when (error is ApplySafetyException || error is InvalidOperationException || error is InvalidDataException || error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
        { Say("PASS: " + label + "; " + error.GetType().Name); return error; }
        throw new InvalidOperationException("Accepted invalid emergency input: " + label);
    }
    private static int EmergencyRun(string root, string? steam, bool preview = false, Func<string, string?>? consent = null, Action<string>? output = null)
    {
        using (var bundle = new NativeMaintenanceBundle(Package, root)) return NativeEmergencyUninstall.Run(bundle, steam, steam == null ? 0U : 1U, preview,
            consent ?? (prompt => prompt.Contains("Permanently delete every listed unverified file") ? "y" : null), output ?? Say);
    }
    private static bool EmergencyOwned(string root, string path)
    {
        var relative = NativeMaintenance.Relative(root, path); var first = relative.Split('/')[0];
        return EmergencyFiles.Contains(relative, StringComparer.OrdinalIgnoreCase) || relative == "BetterAstralParty.update.lock"
            || Regex.IsMatch(relative, @"\ABetterAstralParty\.Update/bap-v[45]-(?:stage|txn|helperauto|helper|helperbridge|wholeintent|wholehist)-[0-9a-f]{32}/")
            || Regex.IsMatch(relative, @"\ABetterAstralParty\.Update/bap-v4-transition-[0-9a-f]{32}\z")
            || Regex.IsMatch(first, @"\ABetterAstralParty\.(?:update-v[345]\.(?:version(?:\.uninstalled-[0-9a-f]{32})?|failure|operation|preferences-failed|failed)|reinstall-v[345]\.(?:pending|completed-[0-9a-f]{32}))\z")
            || Regex.IsMatch(first, @"\A(?:bap-v[345]-(?:stage|txn|helperauto|helper|helperbridge|wholeintent|wholehist)-[0-9a-f]{32}|BetterAstralParty-emergency-[0-9a-f]{32})\z");
    }
    private static void EmergencyPreserved(string root, string steam, Dictionary<string, string> before)
    {
        foreach (var row in before.Where(row => row.Value != "directory" && !EmergencyOwned(root, row.Key)))
            Check(File.Exists(WindowsPath.ForIo(row.Key)) && UpdateTrust.Hash(Read(row.Key)) == row.Value, "force cleanup preserves non-allowlisted original: " + NativeMaintenance.Relative(root, row.Key));
        var shortcuts = NativeSteamMaintenance.ReadShortcuts(Read(Full(steam, "userdata/1/config/shortcuts.vdf")));
        Check(((OrderedDictionary)shortcuts["shortcuts"]!).Contains("99") && !NativeSteamMaintenance.PlanRemove(steam, 1, root), "force cleanup removes only exact owned shortcut");
    }
    private static void EmergencyNoCopies(string root)
    {
        Check(!Directory.Exists(WindowsPath.ForIo(NativeMaintenance.JournalRoot(root))) && !Directory.Exists(WindowsPath.ForIo(EmergencyLegacyBackups(root))) && !Directory.Exists(WindowsPath.ForIo(EmergencyLegacyBackups(root, "v3"))), "selected-root v3/v4/v5 backups, journals and lock namespaces are actually deleted");
        Check(!Directory.GetDirectories(WindowsPath.ForIo(root), "BetterAstralParty-emergency-*", SearchOption.TopDirectoryOnly).Any(), "no old/new quarantine copies remain");
        Check(!File.Exists(WindowsPath.ForIo(Full(root, "BetterAstralParty.update.lock"))), "own idle game update lock is deleted after backup cleanup");
    }
    private static void EmergencySuccessful(string name, Action<string> damage)
    {
        var root = EmergencySeed(name, out var steam); damage(root); var before = Snapshot(root); var originals = EmergencyFiles.Where(path => File.Exists(WindowsPath.ForIo(Full(root, path)))).ToArray(); var checkedBoundary = false;
        NativeEmergencyUninstall.Boundary = phase => { if (phase == "BeforeFirstDelete") { Check(originals.All(path => File.Exists(WindowsPath.ForIo(Full(root, path)))), "all original objects still present before first irreversible delete"); checkedBoundary = true; } };
        try { EmergencyRun(root, steam); } finally { NativeEmergencyUninstall.Boundary = null; }
        Check(checkedBoundary && originals.All(path => !File.Exists(WindowsPath.ForIo(Full(root, path)))), "force cleanup deletes exact listed files without copying: " + name); EmergencyNoCopies(root); EmergencyPreserved(root, steam, before);
        var after = EmergencyState(root, steam); EmergencyRun(root, steam); EmergencySame(after, root, steam, "empty emergency retry is idempotent and creates no lock/history");
    }
    private static void EmergencyReadOnly()
    {
        var root = EmergencySeed("preview-refusal", out var steam); Text(Full(root, InstallReceipt.Path), "damaged receipt"); File.Delete(WindowsPath.ForIo(Full(root, "BetterAstralParty-Launcher.exe")));
        Text(Full(NativeMaintenance.JournalRoot(root), "saved/backup.bin"), "inert saved bytes"); Text(Full(EmergencyLegacyBackups(root), "pending.json"), "inert legacy journal");
        var before = EmergencyState(root, steam); var preview = new List<string>(); EmergencyRun(root, steam, true, _ => throw new InvalidOperationException("Preview requested consent"), preview.Add);
        EmergencySame(before, root, steam, "WhatIf makes no target/Steam/directory/journal/lock writes");
        Check(preview.Any(line => line.Contains(UpdateTrust.PluginPath.Replace('/', '\\')) && line.Contains(UpdateTrust.Hash(Read(Full(root, UpdateTrust.PluginPath))))), "preview lists exact full target path and SHA-256");
        Check(preview.Any(line => line.Contains("ownership unverified")) && preview.Any(line => line.Contains("absent") && line.Contains("BetterAstralParty-Launcher.exe")), "preview distinguishes unverified and absent targets");
        Check(preview.Any(line => line.Contains(EmergencyLegacyBackups(root))) && preview.Any(line => line.Contains(NativeMaintenance.JournalRoot(root))) && preview.Any(line => line.Contains("partial deletion cannot be rolled back")), "preview includes both exact recovery roots and irreversible warning");
        Check(preview.Any(line => line.Contains("separate metadata backups may remain")), "preview distinguishes protected shared Steam metadata backups from deleted mod recovery backups");
        foreach (var answer in new string?[] { null, "", "n", "Y", " y", "y " }) { Check(EmergencyRun(root, steam, consent: _ => answer) == 0, "refused confirmation cancels"); EmergencySame(before, root, steam, "blank/refused consent has zero writes"); }
        EmergencyReject(() => EmergencyRun(root, steam, consent: _ => throw new InvalidOperationException("interactive input unavailable")), "noninteractive consent fails closed"); EmergencySame(before, root, steam, "unavailable consent has zero writes");
    }
    private static void EmergencyUnsafeFiles()
    {
        foreach (var kind in new[] { "target-hardlink", "target-symlink", "backup-hardlink", "backup-symlink", "target-busy", "update-lock", "backup-lock", "legacy-lock", "readonly", "backup-data-hardlink" })
        {
            var root = EmergencySeed(kind, out var steam); var target = Full(root, "BetterAstralParty-Mod.cmd"); var foreign = Full(Scratch, "emergency-cases/" + kind + "/foreign.keep"); Text(foreign, "foreign bytes retained " + kind); IDisposable? held = null;
            if (kind.StartsWith("target-", StringComparison.Ordinal) && kind != "target-busy") { File.Delete(WindowsPath.ForIo(target)); Check(kind.EndsWith("hardlink", StringComparison.Ordinal) ? CreateHardLinkW(WindowsPath.ForIo(target), WindowsPath.ForIo(foreign), IntPtr.Zero) : CreateSymbolicLinkW(WindowsPath.ForIo(target), WindowsPath.ForIo(foreign), 2), "actual Windows target link created"); }
            if (kind == "backup-hardlink" || kind == "backup-data-hardlink") { var path = Full(NativeMaintenance.JournalRoot(root), kind == "backup-hardlink" ? "pending.json.lock" : "saved/data.bin"); Directory.CreateDirectory(WindowsPath.ForIo(WindowsPath.Parent(path))); Check(CreateHardLinkW(WindowsPath.ForIo(path), WindowsPath.ForIo(foreign), IntPtr.Zero), "actual hardlinked backup object created"); }
            if (kind == "backup-symlink") { var backup = NativeMaintenance.JournalRoot(root); var destination = Full(Scratch, "emergency-cases/" + kind + "/foreign-backups"); Text(Full(destination, "preserved.keep"), "foreign directory bytes"); Directory.CreateDirectory(WindowsPath.ForIo(WindowsPath.Parent(backup))); Check(CreateSymbolicLinkW(WindowsPath.ForIo(backup), WindowsPath.ForIo(destination), 3), "actual linked backup root created"); }
            var lockPath = kind == "update-lock" ? Full(root, "BetterAstralParty.update.lock") : kind == "legacy-lock" ? Full(EmergencyLegacyBackups(root), "pending.json.lock") : NativeMaintenance.JournalPath(root) + ".lock";
            if (kind.EndsWith("lock", StringComparison.Ordinal)) Text(lockPath, "existing lock bytes");
            if (kind == "readonly") File.SetAttributes(WindowsPath.ForIo(target), FileAttributes.ReadOnly);
            var before = EmergencyState(root, steam); var foreignBytes = Read(foreign);
            try { if (kind == "target-busy") held = new FileStream(WindowsPath.ForIo(target), FileMode.Open, FileAccess.Read, FileShare.Read); else if (kind.EndsWith("lock", StringComparison.Ordinal)) held = new FileStream(WindowsPath.ForIo(lockPath), FileMode.Open, FileAccess.Read, FileShare.Read);
                EmergencyReject(() => EmergencyRun(root, steam), "force cleanup refuses linked/busy/read-only object before deletion: " + kind);
            } finally { held?.Dispose(); if (kind == "readonly") File.SetAttributes(WindowsPath.ForIo(target), FileAttributes.Normal); }
            EmergencySame(before, root, steam, "unsafe rejection retains all exact bytes: " + kind); Check(Read(foreign).SequenceEqual(foreignBytes), "foreign object bytes untouched: " + kind);
        }
    }
    private static void EmergencyHold(string signal)
    {
        Scope(signal); Text(Full(signal, "ready"), "inert process ready"); var watch = Stopwatch.StartNew(); while (!File.Exists(WindowsPath.ForIo(Full(signal, "release"))) && watch.ElapsedMilliseconds < 30000) Thread.Sleep(50);
        if (!File.Exists(WindowsPath.ForIo(Full(signal, "release")))) throw new InvalidOperationException("Inert hold expired; never terminate a process.");
    }
    private static void EmergencyBusyProcesses()
    {
        foreach (var name in new[] { OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyLauncher, "AstralParty_INT.exe" })
        {
            var root = EmergencySeed("busy-" + name, out var steam); Write(Full(root, name), Read(WindowsPath.FromIo(typeof(SetupHost).Assembly.Location))); var signal = Full(Scratch, "emergency-cases/busy-" + name + "/signal"); var before = EmergencyState(root, steam);
            var info = new ProcessStartInfo { FileName = WindowsPath.ForProcess(Full(root, name)), Arguments = NativeMaintenanceEntry.CommandLine(new[] { "emergency-hold", signal }), WorkingDirectory = WindowsPath.ForProcess(root), UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(info) ?? throw new InvalidOperationException("Inert process start failed")) {
                try { var watch = Stopwatch.StartNew(); while (!File.Exists(WindowsPath.ForIo(Full(signal, "ready"))) && watch.ElapsedMilliseconds < 10000) Thread.Sleep(50); Check(!process.HasExited && File.Exists(WindowsPath.ForIo(Full(signal, "ready"))), "verified inert process active: " + name);
                    EmergencyReject(() => EmergencyRun(root, steam), "force cleanup refuses active game/helper/launcher: " + name); Check(!process.HasExited, "force cleanup never kills inert process");
                } finally { Text(Full(signal, "release"), "normal inert process exit"); Check(process.WaitForExit(10000) && process.ExitCode == 0, "inert process exits normally after release"); }
            }
            var after = EmergencyState(root, steam); Check(before.Where(row => row.Value != "directory").All(row => after.TryGetValue(row.Key, out var hash) && hash == row.Value), "busy process stop preserves payload bytes");
        }
    }
    private static void EmergencyChangedAfterConsent()
    {
        foreach (var absent in new[] { false, true }) {
            var root = EmergencySeed(absent ? "appeared-after-preview" : "changed-after-preview", out var steam); var path = Full(root, "BetterAstralParty-Mod.cmd"); if (absent) File.Delete(WindowsPath.ForIo(path));
            EmergencyReject(() => EmergencyRun(root, steam, consent: _ => { Text(path, "new exact bytes after preview"); return "y"; }), "post-preview target appearance/change stops before first deletion");
            Check(Encoding.UTF8.GetString(Read(path)) == "new exact bytes after preview" && EmergencyFiles.Where(relative => relative != "BetterAstralParty-Mod.cmd").All(relative => File.Exists(WindowsPath.ForIo(Full(root, relative)))), "stale preview retains payload and newly observed bytes");
        }
        var changed = EmergencySeed("changed-tree", out var changedSteam); var tree = "bap-v4-stage-" + new string('a', 32); Text(Full(changed, tree + "/prior.bin"), "prior");
        EmergencyReject(() => EmergencyRun(changed, changedSteam, consent: _ => { Text(Full(changed, tree + "/foreign-new.keep"), "new after preview"); return "y"; }), "new recovery member is not silently added to consent");
        Check(EmergencyFiles.All(path => File.Exists(WindowsPath.ForIo(Full(changed, path)))), "changed recovery member stops before payload deletion");
    }
    private static void EmergencyInterrupted()
    {
        var root = EmergencySeed("interrupted", out var steam); Text(Full(root, "BepInEx/config/kr.betterastralparty.mod.cfg"), "FAKE_EMERGENCY_RAW_CONFIG_CONTENT"); var before = Snapshot(root); var deletedPath = "";
        NativeEmergencyUninstall.Boundary = phase => { if (phase.StartsWith("Deleted:", StringComparison.Ordinal)) { deletedPath = phase.Substring("Deleted:".Length); throw new IOException("inert interrupted delete\r\nAuthorization: Bearer FAKE_EMERGENCY_SECRET"); } };
        Exception failure; try { failure = EmergencyReject(() => EmergencyRun(root, steam), "injected interruption stops after real held-object delete"); } finally { NativeEmergencyUninstall.Boundary = null; }
        Check(deletedPath.Length != 0 && !File.Exists(WindowsPath.ForIo(Full(root, deletedPath))), "first object was really deleted, not quarantined");
        Check(failure.Data["BapEmergencyDeletedFiles"] is int count && count == 1 && failure.Data["BapEmergencyLastTarget"] as string == Full(root, deletedPath), "partial failure exposes truthful deleted count and exact last target");
        Check(EmergencyFiles.Where(path => path != deletedPath).All(path => File.Exists(WindowsPath.ForIo(Full(root, path)))), "interruption does not silently delete additional objects"); EmergencyDiagnostics(root, failure);
        EmergencyRun(root, steam); EmergencyNoCopies(root); EmergencyPreserved(root, steam, before);
    }
    private static void EmergencyNewLock()
    {
        var root = EmergencySeed("foreign-new-lock", out var steam); Text(Full(NativeMaintenance.JournalRoot(root), "saved.bin"), "prior backup"); var lockPath = NativeMaintenance.JournalPath(root) + ".lock";
        NativeEmergencyUninstall.Boundary = phase => { if (phase == "InstallLockReleased") Text(lockPath, "foreign newly appeared lock, never delete"); };
        Exception failure; try { failure = EmergencyReject(() => EmergencyRun(root, steam), "foreign new lock causes nonempty stop while game lock remains held"); } finally { NativeEmergencyUninstall.Boundary = null; }
        Check(Encoding.UTF8.GetString(Read(lockPath)).Contains("foreign newly appeared lock") && Directory.Exists(WindowsPath.ForIo(NativeMaintenance.JournalRoot(root))), "foreign replacement lock and its nonempty namespace remain untouched");
        Check(failure.Data["BapEmergencyPhase"] as string == "RecoveryDirectories" && failure.Data["BapEmergencyDeletedFiles"] is int count && count > 0, "foreign lock stop is explicitly partial, not all-files-retained");
    }
    private static void EmergencyReappeared()
    {
        foreach (var kind in new[] { "directory", "dangling-link" })
        {
            var root = EmergencySeed("reappeared-" + kind, out var steam); var replacement = "";
            NativeEmergencyUninstall.Boundary = phase => {
                if (!phase.StartsWith("Deleted:", StringComparison.Ordinal) || replacement.Length != 0) return;
                replacement = Full(root, phase.Substring("Deleted:".Length));
                if (kind == "directory") Directory.CreateDirectory(WindowsPath.ForIo(replacement));
                else Check(CreateSymbolicLinkW(WindowsPath.ForIo(replacement), WindowsPath.ForIo(Full(Scratch, "never-present-foreign-file")), 2), "actual dangling link created after held-object deletion");
            };
            Exception failure; try { failure = EmergencyReject(() => EmergencyRun(root, steam), "same-path " + kind + " recreation cannot be mistaken for clean absence"); } finally { NativeEmergencyUninstall.Boundary = null; }
            Check(failure.Data["BapEmergencyPhase"] as string == "FinalCheck" && failure.Data["BapEmergencyDeletedFiles"] is int count && count > 0, "same-path recreation is diagnosed as partial final-check failure");
            Check((File.GetAttributes(WindowsPath.ForIo(replacement)) & (kind == "directory" ? FileAttributes.Directory : FileAttributes.ReparsePoint)) != 0, "unexpected replacement is retained, never silently deleted: " + kind);
        }
    }
    private static void EmergencyBeforeDelete()
    {
        var root = EmergencySeed("before-delete", out var steam); var before = Snapshot(root); NativeEmergencyUninstall.Boundary = phase => { if (phase == "BeforeFirstDelete") throw new IOException("inert stop before first delete"); };
        Exception failure; try { failure = EmergencyReject(() => EmergencyRun(root, steam), "injected pre-delete interruption"); } finally { NativeEmergencyUninstall.Boundary = null; }
        Check(before.Where(row => row.Value != "directory").All(row => File.Exists(WindowsPath.ForIo(row.Key)) && UpdateTrust.Hash(Read(row.Key)) == row.Value), "all payload bytes survive pre-delete failure");
        Check(failure.Data["BapEmergencyDeletedFiles"] is int count && count == 0 && !Directory.GetDirectories(WindowsPath.ForIo(root), "BetterAstralParty-emergency-*", SearchOption.TopDirectoryOnly).Any(), "pre-delete failure reports zero deletion and creates no copy namespace");
    }
    private static void EmergencyDirectoryPrimitive()
    {
        var root = Full(Scratch, "emergency-directory-primitives"); Text(Full(root, "nonempty/item.keep"), "keep"); Directory.CreateDirectory(WindowsPath.ForIo(Full(root, "empty"))); Directory.CreateDirectory(WindowsPath.ForIo(Full(root, "pinned/child")));
        using (var fence = new WindowsFileFence(root)) { var id = fence.DirectoryIdentity("empty").Text; fence.RemoveEmptyRecoveryDirectory("empty", id); Check(!Directory.Exists(WindowsPath.ForIo(Full(root, "empty"))), "identity-bound primitive actually deletes an empty directory");
            EmergencyReject(() => fence.RemoveEmptyRecoveryDirectory("", fence.RootIdentity.Text), "directory primitive refuses root"); EmergencyReject(() => fence.RemoveEmptyRecoveryDirectory("nonempty", fence.DirectoryIdentity("nonempty").Text), "directory primitive refuses nonempty object");
            var parentId = fence.DirectoryIdentity("pinned").Text; fence.DirectoryIdentity("pinned/child"); EmergencyReject(() => fence.RemoveEmptyRecoveryDirectory("pinned", parentId), "directory primitive refuses pinned descendant"); }
        Directory.CreateDirectory(WindowsPath.ForIo(Full(root, "swap"))); var swapped = false;
        using (var fence = new WindowsFileFence(root, phase => { if (phase == "remove-empty-open" && !swapped) { Directory.Move(WindowsPath.ForIo(Full(root, "swap")), WindowsPath.ForIo(Full(root, "original.keep"))); Directory.CreateDirectory(WindowsPath.ForIo(Full(root, "swap"))); swapped = true; } }))
            EmergencyReject(() => fence.RemoveEmptyRecoveryDirectory("swap", fence.DirectoryIdentity("swap").Text), "directory identity swap between own-pin release and relative reopen is rejected");
        Check(swapped && Directory.Exists(WindowsPath.ForIo(Full(root, "swap"))) && Directory.Exists(WindowsPath.ForIo(Full(root, "original.keep"))) && File.Exists(WindowsPath.ForIo(Full(root, "nonempty/item.keep"))), "directory race never deletes the foreign replacement or nonempty payload");
    }
    private static void EmergencyScopeAndLimits()
    {
        var root = EmergencySeed("scoped-data", out var steam);
        var kept = new[] { Full(WindowsPath.Parent(NativeMaintenance.JournalRoot(root)), new string('D', 64) + "/other-root.keep"),
            Full(WindowsPath.Parent(EmergencyLegacyBackups(root)), new string('E', 64) + "/other-root.keep"),
            Full(Scratch, "synthetic-local/BetterAstralParty/Diagnostics/global-user.zip"), Full(Scratch, "synthetic-local/BetterAstralParty/fake-credential-store.keep"),
            Full(root, "bap-v3-" + new string('f', 32) + "/pending"), Full(root, "bap-v4-unrelated-" + new string('f', 32) + "/user.keep") };
        foreach (var path in kept) Text(path, "inert unrelated bytes: " + path); var saved = kept.ToDictionary(path => path, path => Read(path), StringComparer.OrdinalIgnoreCase);
        Text(Full(NativeMaintenance.JournalRoot(root), "owned/saved.bin"), "owned current backup"); Text(Full(EmergencyLegacyBackups(root), "owned/saved.bin"), "owned old backup"); EmergencyRun(root, steam); EmergencyNoCopies(root);
        Check(saved.All(row => File.Exists(WindowsPath.ForIo(row.Key)) && Read(row.Key).SequenceEqual(row.Value)), "exact selected-root cleanup preserves other-root backups, global ZIP, fake credential store and unknown/bare namespace");
        var limited = EmergencySeed("bounded-inventory", out var limitedSteam); var tree = "bap-v4-stage-" + new string('a', 32);
        for (var i = 0; i < 4100; i++) Text(Full(limited, tree + "/" + i.ToString("D4") + ".bin"), "inert bounded fixture");
        var before = EmergencyState(limited, limitedSteam); var limit = EmergencyReject(() => EmergencyRun(limited, limitedSteam), "bounded inventory rejects oversized work tree before consent or deletion");
        Check(limit.Data["BapRecoveryReason"] as string == "EmergencyFileLimitExceeded", "oversized inventory reports exact quota cause"); EmergencySame(before, limited, limitedSteam, "quota stop is zero-write");
    }
    private static void EmergencyUnknownTypesAndAccess()
    {
        foreach (var kind in new[] { "marker-directory", "work-file", "backup-root-file", "marker-link" })
        {
            var root = EmergencySeed(kind, out var steam); var path = kind == "backup-root-file" ? NativeMaintenance.JournalRoot(root)
                : Full(root, kind == "work-file" ? "bap-v4-stage-" + new string('a', 32) : UpdateTransaction.HighWaterPath);
            if (kind == "marker-directory" || kind == "marker-link") {
                Scope(path); var seeded = File.GetAttributes(WindowsPath.ForIo(path));
                Check((seeded & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0, "wrong-type fixture replaces only its exact ordinary seeded marker: " + kind);
                File.Delete(WindowsPath.ForIo(path));
            }
            if (kind == "marker-directory") Directory.CreateDirectory(WindowsPath.ForIo(path));
            else if (kind == "marker-link") Check(CreateSymbolicLinkW(WindowsPath.ForIo(path), WindowsPath.ForIo(Full(Scratch, "absent-marker-target")), 2), "actual dangling recovery-marker link created");
            else Text(path, "unexpected object type, retain");
            var before = kind == "marker-link" ? null : EmergencyState(root, steam); var payload = EmergencyFiles.ToDictionary(name => name, name => Read(Full(root, name)), StringComparer.Ordinal);
            var error = EmergencyReject(() => EmergencyRun(root, steam), "known recovery namespace with wrong object type/link stops: " + kind);
            Check(error.Data["BapEmergencyDeletedFiles"] is int count && count == 0, "type mismatch is diagnosed before any deletion");
            if (before != null) EmergencySame(before, root, steam, "unknown type remains untouched: " + kind);
            var attributes = File.GetAttributes(WindowsPath.ForIo(path));
            var typeRemains = kind == "marker-link" ? (attributes & FileAttributes.ReparsePoint) != 0 : kind == "marker-directory" ? (attributes & FileAttributes.Directory) != 0
                : (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 && Encoding.UTF8.GetString(Read(path)) == "unexpected object type, retain";
            Check(payload.All(row => Read(Full(root, row.Key)).SequenceEqual(row.Value)) && typeRemains, "wrong-type recovery object and payload remain intact: " + kind);
        }
        foreach (var version in new[] { "v3", "v4", "v5" })
        {
            var root = EmergencySeed("unreadable-" + version, out var steam); var backup = version == "v5" ? NativeMaintenance.JournalRoot(root) : EmergencyLegacyBackups(root, version);
            Text(Full(backup, "saved/backup.bin"), "inert unreadable recovery evidence"); Scope(backup); var before = EmergencyState(root, steam);
            var original = Directory.GetAccessControl(WindowsPath.ForIo(backup), AccessControlSections.Access); var denied = Directory.GetAccessControl(WindowsPath.ForIo(backup), AccessControlSections.Access);
            var originalDescriptor = original.GetSecurityDescriptorBinaryForm(); Write(Full(Scratch, "results/fixture-" + version + "-original-dacl.bin"), originalDescriptor);
            var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Inert ACL fixture owner unavailable");
            denied.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
            try { Directory.SetAccessControl(WindowsPath.ForIo(backup), denied); var failure = EmergencyReject(() => EmergencyRun(root, steam, consent: _ => throw new InvalidOperationException("Unreadable scope must stop before consent")), "unreadable exact " + version + " backup root cannot be labelled absent");
                Check(failure.Data["BapEmergencyDeletedFiles"] is int count && count == 0 && failure.Data["BapEmergencyPhase"] as string == "Preflight" && (failure is ApplySafetyException || failure is UnauthorizedAccessException), "unavailable recovery scope stops during preflight, before consent/deletion");
            } finally {
                var restored = new DirectorySecurity(); restored.SetSecurityDescriptorBinaryForm(originalDescriptor, AccessControlSections.Access);
                Directory.SetAccessControl(WindowsPath.ForIo(backup), restored);
                Check(Directory.GetAccessControl(WindowsPath.ForIo(backup), AccessControlSections.Access).GetSecurityDescriptorBinaryForm().SequenceEqual(originalDescriptor), "inert backup original binary DACL is exactly restored: " + version);
            }
            EmergencySame(before, root, steam, "unreadable backup stop preserves all payload/recovery bytes and restores only inert fixture ACL");
        }
    }
    private static void EmergencyDiagnostics(string root, Exception failure)
    {
        foreach (var field in new Dictionary<string, string> { { "_run", Guid.NewGuid().ToString("N") }, { "_version", FixtureBoundary.Version }, { "_operation", "EmergencyUninstall" }, { "_package", Package }, { "_game", root } }) typeof(NativeMaintenanceEntry).GetField(field.Key, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, field.Value);
        var log = NativeMaintenanceEntry.Failure(failure) ?? throw new InvalidOperationException("Actual emergency failure producer unavailable"); Scope(log); var bytes = Read(log); var raw = Encoding.UTF8.GetString(bytes);
        Check(raw.Contains("BapEmergencyDeletedFiles: 1") && raw.Contains("BapEmergencyDeletedDirectories: 0") && raw.Contains("BapEmergencyObservedFiles:") && raw.Contains("BapEmergencyLastTarget:"), "producer captures bounded partial counts and last target before collection");
        var result = DiagnosticBundle.Collect(root, Full(Scratch, "synthetic-local/BetterAstralParty/Logs"), Full(Scratch, "synthetic-temp/BetterAstralParty"), Full(Scratch, "absent-unity"), Full(Scratch, "emergency-diagnostics"), Full(Scratch, "emergency-diagnostics-fallback"), "4.8"); string collected, summary, allText;
        using (var zip = ZipFile.OpenRead(result.ArchivePath)) { using (var reader = new StreamReader(zip.GetEntry("summary.json")!.Open())) summary = reader.ReadToEnd(); collected = string.Join("\n", zip.Entries.Where(entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(entry => { using (var reader = new StreamReader(entry.Open())) return reader.ReadToEnd(); })); allText = string.Join("\n", zip.Entries.Select(entry => { using (var reader = new StreamReader(entry.Open())) return reader.ReadToEnd(); })); }
        Check(collected.Contains("inert interrupted delete") && collected.Contains("BapInstallStage:") && collected.Contains("BapRecoveryReason:") && collected.Contains("BapEmergencyDeletedFiles: 1") && collected.Contains("BapEmergencyLastTarget:"), "real force failure stage/reason/counts/target reach diagnostic ZIP");
        Check(!collected.Contains(Scratch) && summary.Contains("installer-uninstaller-error") && summary.Contains("missingOrOmitted"), "force diagnostic ZIP redacts paths and reports provenance/omissions");
        Check(!allText.Contains("FAKE_EMERGENCY_SECRET") && !allText.Contains("FAKE_EMERGENCY_RAW_CONFIG_CONTENT") && Read(log).SequenceEqual(bytes), "collector redacts credentials, excludes raw config and retains original failure bytes");
    }
    private static void EmergencyContainedWork()
    {
        foreach (var name in new[] { "complete", "unknown-child", "interrupted" }) {
            var root = EmergencySeed("contained-" + name, out var steam); var owned = new List<string>();
            using (var fence = new WindowsFileFence(root)) {
                foreach (var kind in new[] { "stage", "txn" }) {
                    var work = UpdateWorkPath.New(kind); UpdateWorkPath.Create(fence, work); var file = work + "/pending.bin"; owned.Add(file); Text(Full(root, file), "inert contained history");
                }
                // 1.3.x (v4) contained work kinds, including the removed helper/whole-bridge kinds, remain force-removal scope.
                foreach (var kind in new[] { "stage", "txn", "helperauto", "helper", "helperbridge", "wholeintent", "wholehist" }) {
                    var file = UpdateWorkPath.Root + "/bap-v4-" + kind + "-" + Guid.NewGuid().ToString("N") + "/pending.bin"; owned.Add(file); Text(Full(root, file), "inert legacy contained history");
                }
                var marker = UpdateWorkPath.Root + "/bap-v4-transition-" + Guid.NewGuid().ToString("N"); owned.Add(marker); Text(Full(root, marker), "inert transition");
            }
            var unknown = Full(root, UpdateWorkPath.Root + "/user-subtree/user.keep");
            if (name == "unknown-child") Text(unknown, "unrelated namespace child survives");
            var before = Snapshot(root);
            if (name == "interrupted") {
                NativeEmergencyUninstall.Boundary = phase => { if (phase.StartsWith("Deleted:" + UpdateWorkPath.Root + "/", StringComparison.Ordinal)) throw new IOException("inert contained interruption"); };
                try { EmergencyReject(() => EmergencyRun(root, steam), "contained held-object deletion interruption remains recoverable by explicit retry"); }
                finally { NativeEmergencyUninstall.Boundary = null; }
            }
            EmergencyRun(root, steam); EmergencyNoCopies(root); EmergencyPreserved(root, steam, before);
            Check(owned.All(path => !File.Exists(WindowsPath.ForIo(Full(root, path)))), "force removal deletes only selected contained v5/v4 work and transition objects");
            Check(name == "unknown-child" ? File.Exists(WindowsPath.ForIo(unknown)) && Directory.Exists(WindowsPath.ForIo(Full(root, UpdateWorkPath.Root))) : !Directory.Exists(WindowsPath.ForIo(Full(root, UpdateWorkPath.Root))), "namespace removed only after exact owned children leave it empty: " + name);
        }
        foreach (var kind in new[] { "container-link", "work-link", "transition-hardlink" }) {
            var root = EmergencySeed("contained-" + kind, out var steam); var outside = Full(Scratch, "emergency-cases/contained-" + kind + "/outside"); Text(Full(outside, "user.keep"), "unrelated linked target");
            using (var fence = new WindowsFileFence(root)) if (!DirectoryPresentForFixture(fence, UpdateWorkPath.Root)) fence.CreatePrivateDirectory(UpdateWorkPath.Root);
            if (kind == "container-link") {
                // The seed may contain history; retain it at an inert non-owned fixture path before substituting the parent.
                Directory.Move(WindowsPath.ForIo(Full(root, UpdateWorkPath.Root)), WindowsPath.ForIo(Full(root, "fixture-saved-namespace")));
                ArchiveJunctionForEmergency(Full(root, UpdateWorkPath.Root), outside);
            } else if (kind == "work-link") ArchiveJunctionForEmergency(Full(root, UpdateWorkPath.New("stage")), outside);
            else Check(CreateHardLinkW(WindowsPath.ForIo(Full(root, UpdateWorkPath.Root + "/bap-v4-transition-" + Guid.NewGuid().ToString("N"))), WindowsPath.ForIo(Full(outside, "user.keep")), IntPtr.Zero), "actual contained transition hardlink fixture");
            var payload = Read(Full(root, UpdateTrust.PluginPath)); var error = EmergencyReject(() => EmergencyRun(root, steam), "contained exact-owned links are refused: " + kind);
            Check(error.Data["BapEmergencyDeletedFiles"] is int count && count == 0 && Read(Full(root, UpdateTrust.PluginPath)).SequenceEqual(payload) && Encoding.UTF8.GetString(Read(Full(outside, "user.keep"))) == "unrelated linked target", "contained link refusal precedes mutation and preserves foreign objects");
        }
    }
    private static bool DirectoryPresentForFixture(WindowsFileFence fence, string relative)
    { try { fence.DirectoryIdentity(relative); return true; } catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return false; } }
    private static void ArchiveJunctionForEmergency(string path, string target)
    { Check(CreateSymbolicLinkW(WindowsPath.ForIo(path), WindowsPath.ForIo(target), 3), "actual contained directory symlink fixture"); }
    private static void Emergency()
    {
        EmergencyReadOnly(); EmergencySuccessful("missing-receipt-modified", root => { File.Delete(WindowsPath.ForIo(Full(root, InstallReceipt.Path))); Text(Full(root, UpdateTrust.PluginPath), "modified known plugin bytes 가"); });
        EmergencySuccessful("damaged-receipt-partial", root => { Text(Full(root, InstallReceipt.Path), "{ damaged receipt"); foreach (var path in new[] { UpdateTrust.PluginPath, OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyLauncher }) File.Delete(WindowsPath.ForIo(Full(root, path))); });
        EmergencySuccessful("pending-history", root => {
            foreach (var version in new[] { "3", "4", "5" }) { Text(Full(root, "BetterAstralParty.reinstall-v" + version + ".pending"), "damaged pending data"); Text(Full(root, "BetterAstralParty.update-v" + version + ".version"), "damaged high water"); foreach (var prefix in new[] { "stage", "txn", "helperauto", "helper", "helperbridge", "wholeintent", "wholehist" }) Text(Full(root, "bap-v" + version + "-" + prefix + "-" + new string('a', 32) + "/nested/pending.bin"), "inert owned history"); }
            Text(Full(root, "BetterAstralParty-emergency-" + new string('b', 32) + "/old-quarantine.bin"), "old quarantined bytes");
            foreach (var backup in new[] { NativeMaintenance.JournalRoot(root), EmergencyLegacyBackups(root), EmergencyLegacyBackups(root, "v3") }) { Text(Full(backup, "pending.json"), "{ damaged ordinary pending"); Text(Full(backup, "historical-completed/completed.json"), "historical metadata"); Text(Full(backup, "historical-completed/backup.bin"), "previous recovery bytes"); Text(Full(backup, "pending.json.lock"), "idle old lock"); }
            Text(Full(root, "bap-v4-unrelated-" + new string('c', 32) + "/user.keep"), "unknown prefix preserved");
            var sibling = Full(WindowsPath.Parent(NativeMaintenance.JournalRoot(root)), new string('D', 64) + "/other-root.keep"); Text(sibling, "other installation backup preserved");
        });
        EmergencyChangedAfterConsent(); EmergencyUnsafeFiles(); EmergencyBusyProcesses(); EmergencyBeforeDelete(); EmergencyInterrupted(); EmergencyNewLock(); EmergencyReappeared(); EmergencyDirectoryPrimitive(); EmergencyScopeAndLimits(); EmergencyUnknownTypesAndAccess(); EmergencyContainedWork();
        var root = EmergencySeed("steam-unavailable", out var steam); var shortcut = Read(Full(steam, "userdata/1/config/shortcuts.vdf")); EmergencyRun(root, null); EmergencyNoCopies(root);
        Check(EmergencyFiles.All(path => !File.Exists(WindowsPath.ForIo(Full(root, path)))) && Read(Full(steam, "userdata/1/config/shortcuts.vdf")).SequenceEqual(shortcut), "unavailable Steam context allows exact file cleanup without shortcut changes");
    }
}
