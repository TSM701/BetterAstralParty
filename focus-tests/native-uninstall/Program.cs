using System.Text;
using System.Text.Json;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static class NativeUninstallFixture
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Write(string root, string relative, string text)
    { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    private static NativeMaintenanceBundle Seed(string scratch, string name, bool installed = true, bool? loader = false, bool legacy = false)
    {
        var root = Path.Combine(scratch, name); Directory.CreateDirectory(root); Write(root, "AstralParty_INT.exe", "inert game image");
        var bundle = new NativeMaintenanceBundle(root);
        if (installed)
        {
            var owned = legacy ? LegacyReceipt.Owned : InstallReceipt.Owned; var baseline = legacy ? LegacyReceipt.Baseline : InstallReceipt.Baseline;
            foreach (var relative in owned) Write(root, relative, "installed previous build: " + relative);
            var channels = new ReceiptChannelState(bundle.RootIdentity, "Stable", new string('a', 32), ReleaseFeedPolicy.StableRepositoryId, baseline, new string('B', 64), 0, null, "-");
            var files = owned.Select(relative => new { path = relative, sha256 = bundle.TargetHash(relative) }).ToArray();
            File.WriteAllBytes(Path.Combine(root, InstallReceipt.Path), JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, baseline, owner = InstalledPluginMetadata.Owner, loaderBefore = loader, files, channelState = channels.Encoded }));
        }
        Reset(); return bundle;
    }
    private static void HelperPair(NativeMaintenanceBundle bundle, bool receipt = true)
    {
        Write(bundle.GameRoot, OrdinaryInstallRecovery.LegacyHelper, "inert 1.3.x helper");
        if (receipt) Write(bundle.GameRoot, OrdinaryInstallRecovery.LegacyHelperReceipt, "BetterAstralParty.HelperReceipt/v1\nroot=" + bundle.RootIdentity + "\nsha256=" + bundle.TargetHash(OrdinaryInstallRecovery.LegacyHelper) + "\n");
    }
    private static void Reset()
    {
        SetupHost.History = SetupHost.Stages = SetupHost.Failures = 0; SetupHost.UpdateChecks = SetupHost.UpdateRecoveries = SetupHost.GamesClosedAtRecovery = 0; SetupHost.PendingPlugin = null;
        NativeSteamMaintenance.Shortcut = false; NativeSteamMaintenance.StopGames = NativeSteamMaintenance.StopSteams = NativeSteamMaintenance.Removes = NativeSteamMaintenance.Rollbacks = NativeSteamMaintenance.Resumes = 0;
        NativeSteamMaintenance.AccountQueries = NativeSteamMaintenance.Plans = 0;
        NativeSteamMaintenance.RemoveFailure = NativeSteamMaintenance.AccountFailure = NativeSteamMaintenance.PlanFailure = null; NativeMaintenance.Pending = false; NativeMaintenance.CompletionFailure = NativeMaintenance.RestoreFailure = null;
        NativeMaintenance.Locks = NativeMaintenance.Starts = NativeMaintenance.Completes = NativeMaintenance.Restores = NativeMaintenance.PendingChecks = 0;
    }
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => UpdateTrust.Hash(File.ReadAllBytes(path)));
    private static void Unchanged(Dictionary<string, string> before)
    { foreach (var item in before) Check(File.Exists(item.Key) && UpdateTrust.Hash(File.ReadAllBytes(item.Key)) == item.Value, "original preserved: " + item.Key); }
    private static int Run(NativeMaintenanceBundle bundle, bool whatIf = false, string loader = "Keep", string? answer = "y")
        => NativeUninstall.Run(bundle, Path.Combine(Path.GetDirectoryName(bundle.GameRoot)!, "inert-steam"), 1, loader, whatIf, _ => answer, _ => { });
    private static void Reject(Action action, string name)
    { try { action(); } catch (Exception) { Check(true, name); return; } throw new Exception("Accepted: " + name); }
    private static int Main(string[] args)
    {
        try { return RunFixture(args); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static int RunFixture(string[] args)
    {
        if (args.Length != 2 || Path.GetDirectoryName(Path.GetFullPath(args[0])) != Path.GetFullPath(args[1]) || Directory.Exists(args[0])) throw new ArgumentException("Fresh direct artifact scratch required.");
        var scratch = args[0]; Directory.CreateDirectory(scratch); var prior = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(scratch, "inert-profile"));
        try
        {
            var bundle = Seed(scratch, "policy-receipt"); var receipt = NativeUninstall.ReadReceipt(bundle)!;
            var plan = NativeUninstall.PlanFiles(bundle, receipt, _ => { });
            Check(plan.Targets.Count == InstallReceipt.Owned.Length + 1 && !plan.Retained && plan.MissingOwned.Count == 0, "current receipt supersedes differing incoming build hashes");
            Write(bundle.GameRoot, UpdateTrust.PluginPath, "modified plugin retained");
            plan = NativeUninstall.PlanFiles(bundle, receipt, _ => { });
            Check(plan.Retained && plan.Targets.Count == InstallReceipt.Owned.Length - 1 && !plan.Targets.Contains(Path.Combine(bundle.GameRoot, InstallReceipt.Path)), "modified owned file retains receipt");
            bundle = Seed(scratch, "current-with-helper"); receipt = NativeUninstall.ReadReceipt(bundle)!; HelperPair(bundle);
            plan = NativeUninstall.PlanFiles(bundle, receipt, _ => { }); Check(plan.Retained && !plan.Targets.Contains(Path.Combine(bundle.GameRoot, OrdinaryInstallRecovery.LegacyHelper)), "a 1.4.0 receipt never claims a helper; helper and receipt retained");
            bundle = Seed(scratch, "legacy-helper", legacy: true); receipt = NativeUninstall.ReadReceipt(bundle)!; HelperPair(bundle);
            plan = NativeUninstall.PlanFiles(bundle, receipt, _ => { });
            Check(receipt.Legacy && !plan.Retained && plan.Targets.Count == LegacyReceipt.Owned.Length + 3 && plan.Targets.Contains(Path.Combine(bundle.GameRoot, OrdinaryInstallRecovery.LegacyHelperReceipt)), "1.3.x receipt removes its five files plus the verified helper pair");
            bundle = Seed(scratch, "legacy-unverified-helper", legacy: true); receipt = NativeUninstall.ReadReceipt(bundle)!; HelperPair(bundle, false);
            plan = NativeUninstall.PlanFiles(bundle, receipt, _ => { }); Check(plan.Retained && plan.Targets.Count == LegacyReceipt.Owned.Length, "unverified helper retains receipt and helper bytes");
            bundle = Seed(scratch, "missing-owned"); receipt = NativeUninstall.ReadReceipt(bundle)!; File.Delete(Path.Combine(bundle.GameRoot, "BetterAstralParty-Mod.cmd"));
            plan = NativeUninstall.PlanFiles(bundle, receipt, _ => { }); Check(plan.MissingOwned.Count == 1 && plan.Hashes[plan.MissingOwned[0]] == null, "missing owned absence retained in transaction plan");
            bundle = Seed(scratch, "missing-plugin"); File.Delete(Path.Combine(bundle.GameRoot, UpdateTrust.PluginPath.Replace('/', Path.DirectorySeparatorChar)));
            var kept = Snapshot(bundle.GameRoot); Reject(() => Run(bundle), "missing plugin stops removal with Force-Uninstall guidance"); Unchanged(kept);
            // An in-game update cut off between its file moves: the preview says recovery comes first, and removal
            // recovers after closing the game and before the plugin check instead of sending the user to Force-Uninstall.
            bundle = Seed(scratch, "pending-update-missing-plugin"); var plugin = Path.Combine(bundle.GameRoot, UpdateTrust.PluginPath);
            SetupHost.PendingPlugin = File.ReadAllBytes(plugin); File.Delete(plugin); var notices = new List<string>();
            Check(NativeUninstall.Run(bundle, Path.Combine(scratch, "inert-steam"), 1, "Keep", true, _ => "y", notices.Add) == 0 && SetupHost.UpdateRecoveries == 0
                && notices.Any(line => line.Contains("recovered first")) && NativeMaintenance.Starts == 0, "pending-update preview reports recovery first instead of failing");
            Check(Run(bundle) == 0 && SetupHost.UpdateRecoveries == 1 && SetupHost.GamesClosedAtRecovery == 1 && NativeMaintenance.Completes == 1
                && bundle.ReadTarget(UpdateTrust.PluginPath) == null, "removal recovers the interrupted update after closing the game, then removes normally");
            bundle = Seed(scratch, "pending-update-cancel"); SetupHost.PendingPlugin = File.ReadAllBytes(Path.Combine(bundle.GameRoot, UpdateTrust.PluginPath)); notices = new List<string>();
            Check(NativeUninstall.Run(bundle, Path.Combine(scratch, "inert-steam"), 1, "Keep", false, _ => "", notices.Add) == 0 && SetupHost.UpdateRecoveries == 1 && NativeMaintenance.Starts == 0
                && notices.Any(line => line.Contains("only the interrupted automatic update was recovered")) && !notices.Any(line => line.Contains("nothing changed")), "cancel after update recovery does not claim nothing changed");
            bundle = Seed(scratch, "missing-receipt"); File.Delete(Path.Combine(bundle.GameRoot, InstallReceipt.Path)); Reject(() => NativeUninstall.ReadReceipt(bundle), "baseline receipt missing keeps files");
            bundle = Seed(scratch, "invalid-receipt"); Write(bundle.GameRoot, InstallReceipt.Path, "{}"); Reject(() => NativeUninstall.ReadReceipt(bundle), "invalid baseline receipt rejected");
            bundle = Seed(scratch, "preview"); Write(bundle.GameRoot, "doorstop_config.ini", "enabled=false\r\nunrelated=x"); var before = Snapshot(bundle.GameRoot); var fileCount = before.Count;
            Check(Run(bundle, true) == 0, "preview accepted"); Unchanged(before); Check(Snapshot(bundle.GameRoot).Count == fileCount && NativeMaintenance.Locks == 0 && NativeMaintenance.Starts == 0 && SetupHost.Stages == 0, "preview makes no locks/journals/logs/files");
            Check(NativeSteamMaintenance.StopGames == 0 && NativeSteamMaintenance.StopSteams == 0 && NativeSteamMaintenance.Removes == 0, "preview does no process or shortcut actions");
            bundle = Seed(scratch, "pending-preview", false); before = Snapshot(bundle.GameRoot); NativeMaintenance.Pending = true;
            Check(Run(bundle, true) == 0 && NativeMaintenance.PendingChecks == 1 && NativeMaintenance.Locks == 0, "pending preview checks recovery without lock"); Unchanged(before);
            bundle = Seed(scratch, "noop-preview", false); before = Snapshot(bundle.GameRoot); Check(Run(bundle, true) == 0 && NativeMaintenance.Starts == 0, "no-op preview skips transaction"); Unchanged(before);
            bundle = Seed(scratch, "cancel"); before = Snapshot(bundle.GameRoot); Check(Run(bundle, answer: "") == 0 && NativeMaintenance.Starts == 0 && SetupHost.Stages == 0, "blank removal confirmation cancels"); Unchanged(before);
            bundle = Seed(scratch, "shortcut-only", false); NativeSteamMaintenance.Shortcut = true;
            Check(Run(bundle) == 0 && NativeMaintenance.Starts == 0 && NativeMaintenance.Completes == 0 && NativeMaintenance.Locks == 0, "shortcut-only bypasses empty ordinary journal");
            Check(NativeSteamMaintenance.StopSteams == 1 && NativeSteamMaintenance.Removes == 1 && NativeSteamMaintenance.Resumes == 1, "shortcut-only stops and resumes selected Steam through facade");
            bundle = Seed(scratch, "remove"); Write(bundle.GameRoot, "doorstop_config.ini", "enabled=false\r\nunrelated=x");
            Write(bundle.GameRoot, "BepInEx/plugins/OtherMod/Other.dll", "unrelated mod");
            var exclusive = new[] { "BepInEx/config/kr.betterastralparty.mod.cfg", "BepInEx/BetterAstralParty-Diagnostics/current.log", "BepInEx/BetterAstralParty-Diagnostics/previous.log",
                "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log", "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log", "BetterAstralParty-Compatibility.log", "BetterAstralParty-LaunchError.log", ChannelTransitionIntent.AdoptionPath };
            foreach (var relative in exclusive) Write(bundle.GameRoot, relative, "mod settings or log: " + relative);
            Write(bundle.GameRoot, "BepInEx/BetterAstralParty-Diagnostics/unrecognized.log", "preserved unrelated diagnostics");
            using (var fence = new WindowsFileFence(bundle.GameRoot))
            {
                var latch = UpdateFaults.Fail(fence, new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "1.4.0", "1.4.1", 1, 2, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId), FaultPhase.Recovery, FaultReason.Interrupted);
                UpdateFaults.SaveFailed(fence, latch);
            }
            Check(Run(bundle) == 0 && NativeMaintenance.Starts == 1 && NativeMaintenance.Completes == 1, "native held-object removal and ordinary completion");
            Check(!File.Exists(Path.Combine(bundle.GameRoot, UpdateFaults.FailurePath)) && !File.Exists(Path.Combine(bundle.GameRoot, UpdateFaults.SaveFailurePath)),
                "the verified automatic-update OFF latch is BAP-owned state and leaves with the uninstall");
            Check(InstallReceipt.Owned.All(name => !File.Exists(Path.Combine(bundle.GameRoot, name))) && !File.Exists(Path.Combine(bundle.GameRoot, InstallReceipt.Path)), "verified owned8 and receipt removed");
            Check(File.ReadAllText(Path.Combine(bundle.GameRoot, "doorstop_config.ini")) == "enabled=false\r\nunrelated=x" && File.ReadAllText(Path.Combine(bundle.GameRoot, "BepInEx/plugins/OtherMod/Other.dll")) == "unrelated mod", "shared loader and unrelated mod preserved");
            foreach (var relative in exclusive) Check(!File.Exists(Path.Combine(bundle.GameRoot, relative)), "fixed exclusive removal allowlist: " + relative);
            Check(File.ReadAllText(Path.Combine(bundle.GameRoot, "BepInEx/BetterAstralParty-Diagnostics/unrecognized.log")) == "preserved unrelated diagnostics", "unrecognized diagnostics path preserved");
            Check(NativeSteamMaintenance.StopSteams == 0, "file-only uninstall leaves Steam independent");
            bundle = Seed(scratch, "legacy-remove", legacy: true); HelperPair(bundle);
            Check(Run(bundle) == 0 && LegacyReceipt.Owned.Concat(new[] { OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyHelperReceipt, InstallReceipt.Path }).All(name => bundle.ReadTarget(name) == null), "1.4.0 uninstaller removes a 1.3.x installation and its verified helper");
            bundle = Seed(scratch, "changed-installed-launcher", legacy: true);Write(bundle.GameRoot,"BetterAstralParty-Launcher.exe","changed installed launcher");
            var keptReceipt = bundle.ReadTarget(InstallReceipt.Path)!;
            Check(Run(bundle)==0 && NativeMaintenance.Completes==1,"changed old launcher is not an unrelated removal gate");
            Check(Encoding.UTF8.GetString(bundle.ReadTarget("BetterAstralParty-Launcher.exe")!)=="changed installed launcher"&&bundle.ReadTarget(InstallReceipt.Path)!.SequenceEqual(keptReceipt)
                &&LegacyReceipt.Owned.Where(path=>path!="BetterAstralParty-Launcher.exe").All(path=>bundle.ReadTarget(path)==null),"retain unknown launcher and receipt while independently verified targets are removed");
            foreach (var reason in new[] { "SteamRootAbsent", "SteamProfileAbsent", "SteamProfileAmbiguous", "SteamProfileMetadataInvalid", "SteamProfileReadUnavailable" })
            {
                bundle = Seed(scratch, "optional-" + reason); Write(bundle.GameRoot, "BepInEx/plugins/OtherMod/Other.dll", "unrelated mod");
                var observations = new List<string>(); var output = new List<string>();
                var steam = reason == "SteamRootAbsent" ? null : Path.Combine(scratch, "inert-steam");
                if (steam != null) NativeSteamMaintenance.AccountFailure = reason == "SteamProfileReadUnavailable" ? new IOException("FAKE_PRIVATE_PROFILE_READ_PATH") : new SteamProfileUnavailableException(reason);
                Check(NativeUninstall.Run(bundle, steam, 0, "Keep", false, _ => "y", output.Add, observations.Add) == 0 && NativeMaintenance.Starts == 1 && NativeMaintenance.Completes == 1, "verified file removal keeps ordinary backups without auto Steam discovery: " + reason);
                Check(InstallReceipt.Owned.All(name => !File.Exists(Path.Combine(bundle.GameRoot, name))) && !File.Exists(Path.Combine(bundle.GameRoot, InstallReceipt.Path)), "owned files removed without Steam discovery: " + reason);
                Check(observations.Count == 1 && observations[0].StartsWith("NativeUninstallShortcut: action=Skipped; reason=" + reason) && !observations[0].Contains("FAKE_PRIVATE") && output.Any(line => line.Contains("Steam shortcut cleanup skipped")), "bounded cause and truthful final notice: " + reason);
                if (reason == "SteamProfileReadUnavailable") Check(observations[0].Contains("exception=IOException; hresult=0x"), "read-unavailable observation retains typed cause without raw message or paths");
                Check(NativeSteamMaintenance.Plans == 0 && NativeSteamMaintenance.StopSteams == 0 && NativeSteamMaintenance.Removes == 0 && File.ReadAllText(Path.Combine(bundle.GameRoot, "BepInEx/plugins/OtherMod/Other.dll")) == "unrelated mod", "unavailable Steam never guesses or touches profiles and unrelated mods: " + reason);
                var backup = Directory.GetFiles(NativeMaintenance.JournalRoot(bundle.GameRoot), "completed.json", SearchOption.AllDirectories).Single();
                Check(File.Exists(backup), "normal removal retains recovery evidence: " + reason);
            }
            foreach (var preview in new[] { true, false })
            {
                bundle = Seed(scratch, "optional-no-write-" + preview); before = Snapshot(bundle.GameRoot); var observations = new List<string>();
                Check(NativeUninstall.Run(bundle, null, 0, "Keep", preview, _ => "", _ => { }, observations.Add) == 0 && NativeMaintenance.Starts == 0 && observations.Count == 0, "optional Steam WhatIf/refusal has no removal or diagnostic callback: " + preview); Unchanged(before);
            }
            bundle = Seed(scratch, "explicit-account-missing-steam"); before = Snapshot(bundle.GameRoot);
            Reject(() => NativeUninstall.Run(bundle, null, 1, "Keep", false, _ => "y", _ => { }), "explicit account with missing Steam is not ignored"); Unchanged(before);
            foreach (var failure in new Exception[] { new InvalidDataException("FAKE_PRIVATE_PROFILE"), new InvalidOperationException("FAKE_PRIVATE_PROFILE") })
            {
                bundle = Seed(scratch, "automatic-profile-" + failure.GetType().Name); NativeSteamMaintenance.AccountFailure = failure; var observations = new List<string>();
                Check(NativeUninstall.Run(bundle, Path.Combine(scratch, "inert-steam"), 0, "Keep", false, _ => "y", _ => { }, observations.Add) == 0
                    && NativeMaintenance.Completes == 1 && NativeSteamMaintenance.Plans == 0 && NativeSteamMaintenance.StopSteams == 0, "automatic invalid profile remains an optional metadata failure");
                Check(observations.Count == 1 && observations[0].Contains("reason=SteamProfileReadUnavailable; exception=" + failure.GetType().Name)
                    && !observations[0].Contains("FAKE_PRIVATE"), "automatic invalid-profile observation excludes private raw message");
            }
            foreach (var failure in new Exception[] { new SteamProfileUnavailableException("SteamProfileAbsent"), new InvalidDataException("inert malformed profile"), new InvalidOperationException("inert invalid profile"), new IOException("inert profile read failure") })
            {
                bundle = Seed(scratch, "account-failure-" + failure.GetType().Name); before = Snapshot(bundle.GameRoot); NativeSteamMaintenance.AccountFailure = failure;
                Reject(() => Run(bundle), "explicit account/profile read/parse error is not downgraded: " + failure.GetType().Name); Check(NativeMaintenance.Starts == 0 && NativeSteamMaintenance.StopGames == 0, "account failure precedes destructive stages"); Unchanged(before);
            }
            {
                // A Mod entry with the user's launch options is theirs: removal keeps it, prints why and finishes as shortcut-skipped.
                bundle = Seed(scratch, "shortcut-customized"); NativeSteamMaintenance.PlanFailure = new SteamProfileUnavailableException("SteamShortcutCustomized", "INERT CUSTOMIZED GUIDANCE");
                var observations = new List<string>(); var output = new List<string>();
                Check(NativeUninstall.Run(bundle, Path.Combine(scratch, "inert-steam"), 1, "Keep", false, _ => "y", output.Add, observations.Add) == 0 && NativeMaintenance.Completes == 1
                    && output.Contains("INERT CUSTOMIZED GUIDANCE") && output.Last().Contains("Steam shortcut cleanup skipped") && observations.Single().Contains("reason=SteamShortcutCustomized"), "customized Mod entry kept with guidance and a truthful completion");
            }
            foreach (var failure in new Exception[] { new InvalidDataException("inert changed shortcut"), new IOException("inert locked shortcut") })
            {
                bundle = Seed(scratch, "shortcut-plan-failure-" + failure.GetType().Name); NativeSteamMaintenance.PlanFailure = failure; NativeSteamMaintenance.Shortcut = true;
                var observations = new List<string>();
                Check(NativeUninstall.Run(bundle, Path.Combine(scratch, "inert-steam"), 1, "Keep", false, _ => "y", _ => { }, observations.Add) == 0 && NativeMaintenance.Completes == 1,
                    "optional shortcut planning failure permits independently verified file removal: " + failure.GetType().Name);
                Check(InstallReceipt.Owned.All(name => bundle.ReadTarget(name) == null) && bundle.ReadTarget(InstallReceipt.Path) == null && NativeSteamMaintenance.Shortcut
                    && NativeSteamMaintenance.StopSteams == 0 && NativeSteamMaintenance.Removes == 0, "unreadable shortcut remains untouched without Steam shutdown");
                Check(observations.Count == 1 && observations[0].Contains("reason=SteamShortcutPlanUnavailable; exception=" + failure.GetType().Name + "; hresult=0x")
                    && !observations[0].Contains(failure.Message), "optional shortcut removal records bounded cause without raw message");
            }
            bundle = Seed(scratch, "loader-write"); Write(bundle.GameRoot, "doorstop_config.ini", "enabled=false\r\nunrelated=x");
            Check(Run(bundle, loader: "Enabled") == 0 && File.ReadAllText(Path.Combine(bundle.GameRoot, "doorstop_config.ini")) == "enabled=true\nunrelated=x", "actual held-object WriteLoader honors explicit enabled choice");
            var completed = Directory.GetFiles(NativeMaintenance.JournalRoot(bundle.GameRoot), "completed.json", SearchOption.AllDirectories).Single();
            using (var json = JsonDocument.Parse(File.ReadAllBytes(completed)))
            {
                var row = json.RootElement.GetProperty("Entries").EnumerateArray().Single(entry => entry.GetProperty("Target").GetString() == Path.Combine(bundle.GameRoot, "doorstop_config.ini"));
                Check(File.ReadAllText(row.GetProperty("Backup").GetString()!) == "enabled=false\r\nunrelated=x", "explicit loader write preserves exact original backup");
            }
            bundle = Seed(scratch, "rollback"); before = Snapshot(bundle.GameRoot); NativeSteamMaintenance.Shortcut = true; NativeSteamMaintenance.RemoveFailure = new IOException("inert shortcut failure");
            Reject(() => Run(bundle), "shortcut failure propagates"); Check(NativeMaintenance.Restores == 1 && NativeSteamMaintenance.Resumes == 1, "file rollback runs and selected Steam resumes"); Unchanged(before);
            bundle = Seed(scratch, "shortcut-rollback"); before = Snapshot(bundle.GameRoot); NativeSteamMaintenance.Shortcut = true; NativeMaintenance.CompletionFailure = new IOException("inert complete failure");
            Reject(() => Run(bundle), "completion failure propagates"); Check(NativeSteamMaintenance.Rollbacks == 1 && NativeMaintenance.Restores == 1, "shortcut fingerprint rollback precedes ordinary file rollback"); Unchanged(before);
            bundle = Seed(scratch, "rollback-failed"); NativeSteamMaintenance.Shortcut = true; var original = new IOException("inert removal failure"); NativeSteamMaintenance.RemoveFailure = original; var recovery = new IOException("inert rollback failure"); NativeMaintenance.RestoreFailure = recovery;
            Reject(() => Run(bundle), "failed rollback preserves original failure"); Check(ReferenceEquals(original.Data["BapRollbackFailure"], recovery) && SetupHost.Failures == 1 && File.Exists(NativeMaintenance.JournalPath(bundle.GameRoot)), "rollback cause recorded and pending evidence retained");
            bundle = Seed(scratch, "loader-recorded", loader: false); receipt = NativeUninstall.ReadReceipt(bundle)!; Write(bundle.GameRoot, "doorstop_config.ini", "enabled=true\r\nunrelated=x");
            Check(NativeUninstall.LoaderChange(bundle, receipt, "Recorded", false, _ => "", _ => { }) == "enabled=true\nunrelated=x", "Recorded preserves already enabled shared loader");
            Check(NativeUninstall.LoaderChange(bundle, receipt, "Disabled", false, _ => "", _ => { }) == "enabled=false\nunrelated=x", "explicit Disabled preserves unrelated loader text");
            bundle = Seed(scratch, "loader-choice", loader: null); receipt = NativeUninstall.ReadReceipt(bundle)!; Write(bundle.GameRoot, "doorstop_config.ini", "enabled=false\r\nunrelated=x");
            Check(NativeUninstall.LoaderChange(bundle, receipt, "Recorded", true, _ => throw new Exception("Preview prompted"), _ => { }) == null, "missing loader baseline preview does not prompt");
            Check(NativeUninstall.LoaderChange(bundle, receipt, "Recorded", false, _ => "1", _ => { }) == "enabled=true\nunrelated=x", "missing loader baseline explicit choice enables");
            before = Snapshot(bundle.GameRoot); Check(Run(bundle, loader: "Recorded", answer: "") == 0 && NativeMaintenance.Starts == 0, "loader choice cancellation returns without removal"); Unchanged(before);
            Write(bundle.GameRoot, "doorstop_config.ini", "enabled=true\nenabled=false\n");
            Check(NativeUninstall.LoaderChange(bundle, receipt, "Disabled", false, _ => "y", _ => { }) == null, "ambiguous foreign loader mutation skipped without blocking verified file removal");
            foreach (var choice in new[] { "Keep", "Recorded", "Enabled", "Disabled" }) foreach (var kind in new[] { "missing", "ambiguous", "locked" })
            {
                bundle = Seed(scratch, "foreign-loader-" + choice + "-" + kind); var loader = Path.Combine(bundle.GameRoot, "doorstop_config.ini");
                if (kind != "missing") Write(bundle.GameRoot, "doorstop_config.ini", kind == "ambiguous" ? "enabled=true\nenabled=false\nother=preserve" : "enabled=false\nother=preserve");
                var originalLoader = kind == "missing" ? null : File.ReadAllBytes(loader); var observations = new List<string>(); var output = new List<string>();
                using (var held = kind == "locked" ? new FileStream(loader, FileMode.Open, FileAccess.Read, FileShare.None) : null)
                {
                    if (choice == "Keep") Check(NativeUninstall.LoaderChange(bundle, NativeUninstall.ReadReceipt(bundle), choice, false, _ => throw new Exception("Keep must not prompt"), output.Add) == null,
                        "Keep helper never reads even locked/malformed foreign configuration: " + kind);
                    Check(NativeUninstall.Run(bundle, Path.Combine(scratch, "inert-steam"), 1, choice, false, _ => "y", output.Add, observations.Add) == 0,
                        "unsupported optional foreign configuration cannot stop ordinary owned removal: " + choice + "/" + kind);
                }
                Check(originalLoader == null ? !File.Exists(loader) : File.ReadAllBytes(loader).SequenceEqual(originalLoader), "unsupported foreign loader bytes/absence preserved: " + choice + "/" + kind);
                Check(InstallReceipt.Owned.All(name => !File.Exists(Path.Combine(bundle.GameRoot, name))) && NativeMaintenance.Completes == 1, "owned removal completes without a foreign loader write: " + choice + "/" + kind);
                var journal = Directory.GetFiles(NativeMaintenance.JournalRoot(bundle.GameRoot), "completed.json", SearchOption.AllDirectories).Single();
                using (var doc = JsonDocument.Parse(File.ReadAllBytes(journal))) Check(doc.RootElement.GetProperty("Entries").EnumerateArray().All(row => row.GetProperty("Target").GetString() != loader), "skipped foreign loader is not read into backups: " + choice + "/" + kind);
                Check(choice == "Keep" ? observations.Count == 0 : observations.Count == 1 && observations[0].StartsWith("NativeUninstallLoader: action=Skipped; reason=ForeignConfiguration"), "foreign skip records only the optional explicit-restoration observation: " + choice + "/" + kind);
            }
            File.WriteAllText(Path.Combine(scratch, "verification.json"), JsonSerializer.Serialize(new { Passed = true, Checks = _checks, RealOwnedRemovalAndOrdinaryRecovery = true, PackageAuthenticationAndSteamUseInertFacades = true, ActualGameSteamRegistryOrKeysUsed = false }));
            Console.WriteLine("Native uninstall: " + _checks + " checks PASS; actual held-object removal/recovery on inert fixtures."); return 0;
        }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", prior); }
    }
}
