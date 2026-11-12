#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    internal static class NativeInstall
    {
        private const string Configuration = "BepInEx/config/BepInEx.cfg";
        private const string LoaderConfiguration = "doorstop_config.ini";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private sealed class FilePlan
        {
            internal string Source = "", Relative = "";
            internal bool Shared;
        }
        // A 1.3.x installation taken over by this package: its receipt and the exact files to remove.
        internal const string LegacyHighWater = "BetterAstralParty.update-v4.version";
        internal sealed class Takeover
        {
            internal LegacyReceipt Receipt = null!;
            internal readonly Dictionary<string, string> Removals = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        private static string Full(string root, string relative) { return WindowsPath.Absolute(root + "\\" + relative.Replace('/', '\\')); }
        private static void Stage(string phase, string outcome = "Begin") { try { SetupHost.RecordInstallerStage(phase, outcome); } catch { } }
        private static void Failure(string phase, Exception error) { try { SetupHost.RecordInstallerFailure(phase, error); } catch { } }
        private static void Consent(Func<string, string?> input, string prompt)
        {
            string? answer; try { answer = input(prompt); } catch (Exception error) { throw new InvalidOperationException("Interactive maintenance confirmation unavailable. / 대화형 동의를 확인할 수 없습니다.", error); }
            if (answer != "y") throw new InvalidOperationException("Cancelled; installation and recovery records retained. / 취소했습니다. 기존 설치와 복구 기록을 보존합니다.");
        }
        private static void CheckCompatibility(NativeMaintenanceBundle bundle, Action<string> output, bool record = true)
        {
            var report = GameCompatibility.Check(bundle.GameRoot, bundle.Source.Full("artifacts/BetterAstralParty.compatibility.json"), bundle.Source.Full(".deps/bepinex"));
            foreach (var issue in report.Issues) output("호환성 참고 / Compatibility observation: " + issue);
            if (record && report.Error != null) Failure("BootstrapGate", report.Error);
            foreach (var warning in report.Warnings) output(warning);
        }
        // Installations add no Steam entry since 1.8.1-dev.1: BAP runs on every launch (user-decided 2026-11-07). A 1.3.x
        // takeover still rewrites the entry that starts the launcher it removes, so that entry keeps working.
        private static bool PlanShortcut(NativeMaintenanceBundle bundle, bool takeover, string? steamRoot, uint account, Action<string> output, out bool skipped, Action<string>? observation = null)
        {
            skipped = false;
            if (!takeover) return false;
            const string Kept = "1.3.x가 추가한 Steam 외부 게임은 바꾸지 않습니다. / The 1.3.x Steam library entry is left unchanged. ";
            if (steamRoot == null || account == 0) { skipped = true; output("Steam 계정을 확인하지 못했습니다. / Steam profile unavailable. " + Kept); return false; }
            var unavailable = false;
            var plan = NativeSteamMaintenance.PlanConvert(steamRoot, account, bundle.GameRoot, error => {
                unavailable = true; var known = error as SteamProfileUnavailableException;
                var detail = "NativeInstallShortcut: action=Skipped; reason=" + (known?.Reason ?? "SteamShortcutPlanUnavailable") + "; exception=" + error.GetType().Name
                    + "; hresult=0x" + error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
                output((known?.Guidance != null ? known.Guidance + " " : "Steam 외부 게임 정보를 읽지 못해 기존 정보를 보존합니다. / Steam shortcut data unavailable; profile data preserved. ") + Kept + detail);
                try { observation?.Invoke(detail); } catch { }
            });
            skipped = unavailable; return plan;
        }
        // hideConsole: -LoaderConsole Off/Keep; without it a BepInEx installed now starts with its log console off and an
        // existing configuration keeps its own choice (user-decided 2026-11-08).
        internal static void Run(NativeMaintenanceBundle bundle, bool updateOnly, string? steamRoot, uint account, Func<string, string?> consent, Action<string> output, bool restartSteam = false, bool checkOnly = false, Action<string>? observation = null, bool? hideConsole = null)
        {
            if (updateOnly && (restartSteam || account != 0 || steamRoot != null)) throw new InvalidOperationException("UpdateOnly cannot register or restart Steam. / UpdateOnly는 Steam을 변경할 수 없습니다.");
            if (checkOnly) { Preview(bundle, updateOnly, steamRoot, account, consent, output); return; }
            var session = new SteamSessionState(); NativeMaintenance.Transaction? transaction = null;
            NativeMaintenance.InstallLock? installLock = null;
            var rollback = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); string[] targets = Array.Empty<string>(); var registered = false;
            var stage = "Signed package validation"; var phase = "BootstrapGate";
            try {
                Stage(phase); bundle.AssertRoot(); SetupHost.ValidateWholeInputs(bundle.GameRoot, bundle.PackageRoot, bundle.Version); Stage(phase, "Completed");
                installLock = NativeMaintenance.AcquireInstallLock(bundle);
                stage = "Interrupted installation/removal recovery"; phase = "Recovery"; Stage(phase);
                if (File.Exists(WindowsPath.ForIo(NativeMaintenance.JournalPath(bundle.GameRoot)))) NativeMaintenance.RecoverPending(bundle, consent, output);
                NativeSteamMaintenance.WaitGameClosed(bundle.GameRoot, consent, output);
                if (SetupHost.HasPendingUpdate(bundle.GameRoot)) {
                    output("중단된 자동 업데이트를 복구합니다. / Recovering an interrupted automatic update.");
                    output("자동 업데이트 복구 결과 / Update recovery result: " + SetupHost.RecoverPendingUpdate(bundle.GameRoot));
                }
                Stage(phase, "Completed");
                stage = "Compatibility validation"; phase = "BootstrapGate"; Stage(phase);
                CheckCompatibility(bundle, _ => { }); Stage(phase, "Completed");
                stage = "Completed removal / new installation generation validation"; phase = "Recovery"; Stage(phase);
                var generation = NativeReinstallGeneration.Review(bundle.GameRoot, bundle.PackageRoot, bundle.Version, bundle.ManifestHash, installLock.Root);
                if (generation != null) {
                    if (updateOnly) throw new InvalidOperationException("Clean reinstall requires normal installation, not UpdateOnly.");
                    output("완전 제거 상태를 확인했습니다. 이전 업데이트 이력을 보존하고 새로 설치합니다. / Complete removal verified; archive previous history and install: " + generation.Version + " -> " + bundle.Version);
                    Consent(consent, "새 설치를 진행할까요? / Start a new installation generation? [y/N]");
                    NativeReinstallGeneration.Retire(generation, bundle.GameRoot, bundle.PackageRoot, bundle.Version, bundle.ManifestHash, installLock.Root, installLock.File);
                }
                Stage(phase, "Completed");
                stage = "Installed version / ownership validation"; phase = "DescriptorVerification"; Stage(phase);
                var takeover = LegacyTakeover(bundle, output);
                if (takeover != null) {
                    if (updateOnly) throw new InvalidOperationException("1.3.x를 인계하려면 일반 설치가 필요합니다. / Taking over 1.3.x requires a normal installation, not UpdateOnly.");
                    Consent(consent, "1.3.x 설치를 1.4.0 구조로 옮길까요? 런처·업데이트 도우미 파일을 백업 후 제거합니다. / Move the 1.3.x installation to 1.4.0? Launcher and update-helper files are backed up and removed. [y/N]");
                    bundle.Mode = "Takeover"; bundle.Observed = ObservedFiles(bundle, takeover);
                } else Transition(bundle);
                Stage(phase, "Completed");
                stage = "Compatibility validation"; phase = "BootstrapGate"; Stage(phase);
                CheckCompatibility(bundle, output); Stage(phase, "Completed");
                stage = "Package file / shared loader validation"; Stage(phase);
                var files = OwnedPlan(); var shared = new Dictionary<string, string?>(StringComparer.Ordinal);
                var freshLoader = !ExistingSharedLoader(bundle, output);
                var loader = freshLoader ? null : ReadExistingLoader(bundle, output);
                if (freshLoader) files.AddRange(SharedLoaderPlan(bundle, shared));
                var oldReceiptBytes = bundle.ReadTarget(InstallReceipt.Path, 16384);
                var oldReceipt = oldReceiptBytes == null || takeover != null ? null : InstallReceipt.Parse(oldReceiptBytes);
                if (updateOnly && (oldReceipt == null || bundle.TargetHash(UpdateTrust.PluginPath) == null)) throw new InvalidOperationException("UpdateOnly requires an existing installation. / 먼저 일반 설치를 실행하세요.");
                var loaderBefore = takeover != null ? takeover.Receipt.LoaderBefore : LoaderBaseline(oldReceipt, loader, freshLoader, bundle.TargetHash(UpdateTrust.PluginPath) == null);
                var prepared = SetupHost.PrepareWholeReceipt(bundle.GameRoot, bundle.PackageRoot, bundle.Version, loaderBefore.HasValue ? loaderBefore.Value ? "true" : "false" : "null");
                var water = SetupHost.PrepareHighWater(bundle.GameRoot, bundle.PackageRoot, bundle.Version);
                var (configBefore, configAfter, consoleOff) = PlanConfiguration(bundle, freshLoader, hideConsole ?? freshLoader, output);
                var extra = new List<string> { InstallReceipt.Path };
                if (configAfter != null) extra.Add(Configuration);
                if (water != null) extra.Add(UpdateTransaction.HighWaterPath);
                if (takeover != null) extra.AddRange(takeover.Removals.Keys);
                targets = files.Select(file => Full(bundle.GameRoot, file.Relative)).Concat(extra.Select(path => Full(bundle.GameRoot, path))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                long installBytes = 0; foreach (var file in files) installBytes = checked(installBytes + bundle.Bytes(file.Source, 64 * 1048576).Length);
                stage = "Maintenance path planning / write permission preflight";
                foreach (var target in targets) NativeMaintenance.AssertTarget(bundle.GameRoot, target);
                NativeMaintenance.TestWritable(targets);
                var needsShortcut = PlanShortcut(bundle, takeover != null, steamRoot, account, output, out var shortcutSkipped, observation);
                if (needsShortcut) NativeMaintenance.TestWritable(new[] { Full(steamRoot!, "userdata/" + account + "/config/shortcuts.vdf") });
                if (bundle.TargetHash(UpdateTrust.PluginPath) != null) output("기존 설치: 사용자 설정을 유지하고 변경 대상 파일을 백업합니다. / Existing installation: settings preserved; target files backed up.");
                Stage(phase, "Completed"); stage = "Steam shortcut planning / Steam closed"; phase = "WaitForExit"; Stage(phase);
                if (needsShortcut) NativeSteamMaintenance.StopSteam(steamRoot!, session, consent, output);
                NativeSteamMaintenance.AssertGameStopped(bundle.GameRoot);
                if (needsShortcut) NativeSteamMaintenance.AssertStopped(steamRoot!, "InstallPreflight");
                Stage(phase, "Completed"); phase = "Apply"; Stage(phase);
                stage = "Previous installation backup"; bundle.AssertUnchanged(); AssertSharedUnchanged(bundle, shared);
                using (SetupHost.GuardMaintenance(bundle.GameRoot)) {
                    transaction = NativeMaintenance.StartTransaction(bundle, targets, installBytes);
                    stage = "Loader / mod copy";
                    foreach (var file in files) {
                        var bytes = bundle.Bytes(file.Source, 64 * 1048576); var target = Full(bundle.GameRoot, file.Relative);
                        var original = transaction.Entries.Single(entry => entry.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
                        if (file.Shared && original.Existed) throw new InvalidOperationException("Shared loader destination appeared after preflight; preserved.");
                        WriteTarget(bundle, file.Relative, bytes, original.Hash, !original.Existed, rollback);
                    }
                    Stage(phase, "Completed"); phase = "Configuration"; Stage(phase); stage = "Loader configuration";
                    if (configAfter != null && (configBefore == null || !configAfter.SequenceEqual(configBefore))) {
                        var entry = Entry(transaction, bundle, Configuration);
                        if (bundle.TargetHash(Configuration) != (configBefore == null ? null : UpdateTrust.Hash(configBefore))) throw new InvalidDataException("Loader configuration changed after preflight; original bytes retained.");
                        WriteTarget(bundle, Configuration, configAfter, entry.Hash, !entry.Existed, rollback);
                    }
                    foreach (var file in bundle.Expected) if (bundle.TargetHash(file.Key) != file.Value) throw new InvalidDataException("Installed payload differs from the verified bundle: " + file.Key);
                    var receipt = Entry(transaction, bundle, InstallReceipt.Path);
                    WriteTarget(bundle, InstallReceipt.Path, Utf8.GetBytes(prepared), receipt.Hash, !receipt.Existed, rollback);
                    if (water != null) { var entry = Entry(transaction, bundle, UpdateTransaction.HighWaterPath); WriteTarget(bundle, UpdateTransaction.HighWaterPath, water, entry.Hash, !entry.Existed, rollback); }
                    if (takeover != null) { stage = "1.3.x launcher/helper removal"; foreach (var removal in takeover.Removals) RemoveTarget(bundle, removal.Key, removal.Value, rollback); }
                    Stage(phase, "Completed"); phase = "Apply"; Stage(phase); stage = "Steam shortcut registration";
                    if (needsShortcut) { NativeSteamMaintenance.Register(steamRoot!, account, bundle.GameRoot, output); registered = true; }
                    NativeMaintenance.CompleteTransaction(bundle, transaction, targets); transaction = null; Stage(phase, "Completed");
                    LegacyWork.Remove(bundle.GameRoot, output);
                    UpdateFaults.RemoveArchived(bundle.GameRoot, output);
                    ReinstallHistory.Move(bundle.GameRoot, output);
                }
                if (shortcutSkipped) output("1.3.x가 추가한 Steam 외부 게임은 바꾸지 않았습니다. 위 안내를 확인하세요. / The 1.3.x Steam library entry was left unchanged; see the notice above.");
                if (consoleOff) output("게임을 켤 때 뜨는 BepInEx 로그 창을 껐습니다. / The BepInEx log window shown at game start is now off.");
                output(updateOnly ? "기존 설치본 업데이트 완료. / Updated existing installation."
                    // An existing loader switched off starts no mods from Steam; Mod.cmd passes the loader switch as launch arguments.
                    : !freshLoader && LoaderEnabled(loader) == false ? "적용 완료! 공용 로더가 꺼져 있어 Steam에서 그냥 실행하면 모드가 켜지지 않습니다. 게임 폴더의 BetterAstralParty-Mod.cmd로 실행하고 Steam의 실행 옵션 확인 창에서 허용하세요. / Installed. The shared loader is off, so a normal Steam launch starts no mods; run BetterAstralParty-Mod.cmd in the game folder and allow Steam's launch options prompt."
                    : "적용 완료! Steam에서 Astral Party를 실행하면 BetterAstralParty가 켜집니다. 모드 없이 실행하려면 게임 폴더의 AstralParty-Vanilla.cmd를 실행하세요. / Installed. Start Astral Party from Steam to play with BetterAstralParty, or run AstralParty-Vanilla.cmd in the game folder to play without it.");
            } catch (Exception error) {
                if (transaction != null && !registered) {
                    try { Stage("Rollback"); NativeMaintenance.RestoreTransaction(bundle, transaction, targets, rollback); transaction = null; Stage("Rollback", "Completed"); }
                    catch (Exception restore) { error.Data["BapRollbackFailure"] = restore; Failure("Rollback", restore); try { output("복구가 끝나지 않았습니다. 게임을 닫고 현재 묶음·journal·백업을 보관하세요. / Recovery incomplete; retain the current package, journal and backups. reason=" + restore.GetType().Name); } catch { } }
                }
                error.Data["BapInstallStage"] = stage; Failure(phase, error); throw;
            } finally { try { installLock?.Dispose(); } finally { NativeSteamMaintenance.ResumeSteam(session, output); } }
        }
        private static NativeMaintenance.Entry Entry(NativeMaintenance.Transaction transaction, NativeMaintenanceBundle bundle, string relative)
        { return transaction.Entries.Single(row => row.Target.Equals(Full(bundle.GameRoot, relative), StringComparison.OrdinalIgnoreCase)); }
        private static void Preview(NativeMaintenanceBundle bundle, bool updateOnly, string? steamRoot, uint account, Func<string, string?> consent, Action<string> output)
        {
            bundle.AssertRoot(); SetupHost.ValidateWholeInputs(bundle.GameRoot, bundle.PackageRoot, bundle.Version);
            if (!NativeMaintenance.RecoverPending(bundle, consent, output, true)) return;
            if (SetupHost.HasPendingUpdate(bundle.GameRoot)) output("CheckOnly: an interrupted automatic update will be recovered first. / 중단된 자동 업데이트를 먼저 복구합니다.");
            CheckCompatibility(bundle, output, false);
            var generation = NativeReinstallGeneration.Review(bundle.GameRoot, bundle.PackageRoot, bundle.Version, bundle.ManifestHash, NativeMaintenance.JournalRoot(bundle.GameRoot));
            var takeover = false;
            if (generation != null) {
                if (updateOnly) throw new InvalidOperationException("Clean reinstall requires normal installation, not UpdateOnly.");
                output("CheckOnly: verified complete removal; new generation requires confirmation. / 완전 제거 확인: 새 설치 전에 동의가 필요합니다.");
            } else if (LegacyTakeover(bundle, output) != null) {
                if (updateOnly) throw new InvalidOperationException("Taking over 1.3.x requires a normal installation, not UpdateOnly.");
                output("CheckOnly: 1.3.x installation will be taken over after confirmation. / 1.3.x 설치는 동의 후 인계합니다.");
                takeover = true;
            } else {
                Transition(bundle);
                // Read-only: the same receipt/channel validation the real installation runs, so CheckOnly reports its refusal.
                SetupHost.PrepareWholeReceipt(bundle.GameRoot, bundle.PackageRoot, bundle.Version, "null");
            }
            var existingLoader = ExistingSharedLoader(bundle, output);
            if (updateOnly && (bundle.ReadTarget(InstallReceipt.Path, 16384) == null || bundle.TargetHash(UpdateTrust.PluginPath) == null)) throw new InvalidOperationException("UpdateOnly requires an existing installation.");
            if (!existingLoader) SharedLoaderPlan(bundle, new Dictionary<string, string?>(StringComparer.Ordinal));
            var shortcut = PlanShortcut(bundle, takeover, steamRoot, account, output, out _);
            var legacyWork = LegacyWork.Finished(bundle.GameRoot).Count;
            if (legacyWork != 0) output("CheckOnly: 끝난 1.3.x 업데이트 작업 기록 " + legacyWork + "개를 설치 후 정리합니다. / " + legacyWork + " finished 1.3.x update work records will be removed after installation.");
            var archived = UpdateFaults.RemovableArchived(bundle.GameRoot);
            if (archived != 0) output("CheckOnly: 끝난 업데이트 기록 " + archived + "개를 설치 후 정리합니다. / " + archived + " finished update records will be removed after installation.");
            var history = ReinstallHistory.Move(bundle.GameRoot, null, false);
            if (history != 0) output("CheckOnly: 이전 재설치 기록 " + history + "개를 설치 후 BetterAstralParty.Update\\history로 옮깁니다. / " + history + " earlier reinstall records will move to BetterAstralParty.Update\\history after installation.");
            // The install window reads loaderConsole= to offer turning BepInEx's log window off.
            string console;
            if (!existingLoader) console = "fresh";
            else try { var state = ConsoleEnabled(bundle.ReadTarget(Configuration)); console = state == true ? "on" : state == false ? "off" : "unknown"; }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ApplySafetyException) { console = "unknown"; }
            output(console == "fresh" ? "CheckOnly: 새로 설치하는 BepInEx의 로그 창은 꺼 둡니다. / CheckOnly: loaderConsole=fresh; the new BepInEx log window starts off."
                : console == "on" ? "CheckOnly: 게임을 켤 때 BepInEx 로그 창이 뜹니다. -LoaderConsole Off로 끌 수 있습니다. / CheckOnly: loaderConsole=on; -LoaderConsole Off turns the BepInEx log window off."
                : "CheckOnly: BepInEx 로그 창 설정은 그대로 둡니다. / CheckOnly: loaderConsole=" + console + ".");
            output("CheckOnly: installation validation complete; shortcutWrite=" + shortcut + ". / 설치 사전 검사가 완료되었습니다.");
        }
        // 1.3.x takeover is allowed only from a finished, unmodified protocol-4 installation.
        internal static Takeover? LegacyTakeover(NativeMaintenanceBundle bundle, Action<string> output)
        {
            var bytes = bundle.ReadTarget(InstallReceipt.Path, 16384);
            if (bytes == null || !LegacyReceipt.Is(bytes)) return null;
            var result = new Takeover { Receipt = LegacyReceipt.Parse(bytes) };
            if (File.Exists(WindowsPath.ForIo(NativeMaintenance.LegacyJournalPath(bundle.GameRoot))))
                throw new InvalidOperationException("1.3.x 설치·제거 작업이 끝나지 않았습니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / An unfinished 1.3.x install/removal exists; run Force-Uninstall.cmd, then install again.");
            foreach (var file in result.Receipt.Files) {
                var current = bundle.TargetHash(file.Key);
                if (current != null && current != file.Value)
                    throw new InvalidOperationException("1.3.x 파일이 기록과 다릅니다(" + file.Key + "). 변경된 파일은 보존합니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / A 1.3.x file differs from its receipt (" + file.Key + "); it is preserved. Run Force-Uninstall.cmd, then install again.");
            }
            using (var game = new WindowsFileFence(bundle.GameRoot)) {
                foreach (var name in new[] { "BetterAstralParty.update-v4.operation", "BetterAstralParty.reinstall-v4.pending" })
                    if (File.Exists(WindowsPath.ForIo(game.Full(name)))) throw new InvalidOperationException("1.3.x 업데이트 작업이 끝나지 않았습니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / An unfinished 1.3.x update exists; run Force-Uninstall.cmd, then install again.");
                foreach (var parent in new[] { game.Root, game.Full(UpdateWorkPath.Root) }) {
                    if (!Directory.Exists(WindowsPath.ForIo(parent))) continue;
                    foreach (var work in Directory.GetDirectories(WindowsPath.ForIo(parent), "bap-v4-*"))
                        if (File.Exists(Path.Combine(work, "plan")) && !File.Exists(Path.Combine(work, "committed")) && !File.Exists(Path.Combine(work, "rolled-back")) && !File.Exists(Path.Combine(work, "cancelled")))
                            throw new InvalidOperationException("1.3.x 업데이트 기록이 끝나지 않았습니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / Unfinished 1.3.x update records exist; run Force-Uninstall.cmd, then install again.");
                }
            }
            // The protocol-4 high-water is superseded by the v5 record written in the same transaction.
            var oldWater = bundle.TargetHash(LegacyHighWater); if (oldWater != null) result.Removals.Add(LegacyHighWater, oldWater);
            var launcher = OrdinaryInstallRecovery.LegacyLauncher;
            var launcherHash = bundle.TargetHash(launcher); if (launcherHash != null) result.Removals.Add(launcher, launcherHash);
            var helper = bundle.TargetHash(OrdinaryInstallRecovery.LegacyHelper);
            var helperReceipt = bundle.ReadTarget(OrdinaryInstallRecovery.LegacyHelperReceipt, 1024);
            if (helper != null || helperReceipt != null) {
                // Remove the helper only as the pair its own receipt describes; anything else is preserved.
                var text = helperReceipt == null ? "" : Encoding.ASCII.GetString(helperReceipt);
                if (helper == null || helperReceipt == null || !text.StartsWith("BetterAstralParty.HelperReceipt/v1\nroot=" + bundle.RootIdentity + "\n", StringComparison.Ordinal) || !text.Contains("\nsha256=" + helper + "\n"))
                    throw new InvalidOperationException("1.3.x 업데이트 도우미 파일을 확인할 수 없습니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / The 1.3.x update helper could not be verified; run Force-Uninstall.cmd, then install again.");
                result.Removals.Add(OrdinaryInstallRecovery.LegacyHelper, helper);
                result.Removals.Add(OrdinaryInstallRecovery.LegacyHelperReceipt, UpdateTrust.Hash(helperReceipt));
            }
            output("1.3.x 설치를 확인했습니다. 설정과 다른 모드는 유지하고 런처·업데이트 도우미를 제거합니다. / 1.3.x installation found; settings and other mods are kept, the launcher and update helper are removed.");
            return result;
        }
        private static Dictionary<string, string?> ObservedFiles(NativeMaintenanceBundle bundle, Takeover takeover)
        {
            var paths = bundle.Expected.Keys.Concat(new[] { InstallReceipt.Path, UpdateTransaction.HighWaterPath }).Concat(takeover.Removals.Keys).Distinct(StringComparer.Ordinal);
            return paths.ToDictionary(path => path, bundle.TargetHash, StringComparer.Ordinal);
        }
        private static List<FilePlan> OwnedPlan()
        {
            var order = new[] { UpdateTrust.PluginPath, "BetterAstralParty.compatibility.json", "BetterAstralParty-Mod.cmd", "AstralParty-Vanilla.cmd" };
            return order.Select(relative => new FilePlan { Relative = relative, Source = NativeMaintenanceBundle.OwnedSources[relative] }).ToList();
        }
        private static bool ExistingSharedLoader(NativeMaintenanceBundle bundle, Action<string> output)
        {
            foreach (var relative in new[] { LoaderConfiguration, "winhttp.dll", ".doorstop_version", "BepInEx/core", "dotnet", Configuration }) {
                try { File.GetAttributes(WindowsPath.ForIo(Full(bundle.GameRoot, relative))); output("기존 공용 로더와 설정을 유지합니다. / Existing shared loader and settings are preserved."); return true; }
                catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException) {
                    output("공용 로더 확인 불가: 기존 파일을 변경하지 않습니다. / Shared loader observation unavailable; existing files are preserved."); Failure("BootstrapGate", error); return true;
                }
            }
            return false;
        }
        private static byte[]? ReadExistingLoader(NativeMaintenanceBundle bundle, Action<string> output)
        {
            try { return bundle.ReadTarget(LoaderConfiguration); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ApplySafetyException) {
                output("공용 로더 설정을 읽지 못해 원래 상태를 기록하지 않습니다. / Shared loader baseline unavailable; existing configuration is untouched."); Failure("BootstrapGate", error); return null;
            }
        }
        private static List<FilePlan> SharedLoaderPlan(NativeMaintenanceBundle bundle, Dictionary<string, string?> observed)
        {
            var result = new List<FilePlan>();
            foreach (var source in bundle.Hashes.Keys.Where(path => path.StartsWith(".deps/bepinex/", StringComparison.Ordinal)).OrderBy(path => path, StringComparer.Ordinal)) {
                if (NativeMaintenanceFiles.IsDocumentation(source)) continue;
                if (!source.StartsWith(".deps/bepinex/", StringComparison.Ordinal) || !bundle.Hashes.TryGetValue(source, out var expected)) throw new InvalidDataException("Unlisted shared loader component rejected.");
                if (UpdateTrust.Hash(bundle.Bytes(source, 64 * 1048576)) != expected) throw new InvalidDataException("Shared loader source changed.");
                var relative = source.Substring(".deps/bepinex/".Length); NativeMaintenance.AssertTarget(bundle.GameRoot, Full(bundle.GameRoot, relative));
                var current = bundle.TargetHash(relative);
                if (current != null && current != expected) throw new InvalidDataException("Existing shared loader component differs; preserved for review: " + relative);
                observed.Add(relative, current); if (current == null) result.Add(new FilePlan { Source = source, Relative = relative, Shared = true });
            }
            return result;
        }
        private static void AssertSharedUnchanged(NativeMaintenanceBundle bundle, Dictionary<string, string?> observed)
        { foreach (var file in observed) if (bundle.TargetHash(file.Key) != file.Value) throw new InvalidDataException("Shared loader destination changed after preflight; preserved."); }
        internal static bool? LoaderBaseline(InstallReceipt? oldReceipt, byte[]? loader, bool freshLoader, bool pluginAbsent)
        {
            if (oldReceipt != null) return oldReceipt.LoaderBefore;
            if (!pluginAbsent) return null; if (freshLoader) return false;
            return LoaderEnabled(loader);
        }
        // The shared loader's single enabled value, or null when absent or ambiguous.
        internal static bool? LoaderEnabled(byte[]? loader)
        {
            if (loader == null) return null;
            var enabled = Regex.Matches(Decode(loader), @"(?m)^enabled\s*=\s*(true|false)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return enabled.Count == 1 ? (bool?)string.Equals(enabled[0].Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase) : null;
        }
        private static string Decode(byte[] bytes) { Encoding encoding; return Decode(bytes, out encoding); }
        private static string Decode(byte[] bytes, out Encoding encoding)
        {
            using (var stream = new MemoryStream(bytes, false)) using (var reader = new StreamReader(stream, Utf8, true)) {
                try { var text = reader.ReadToEnd(); encoding = reader.CurrentEncoding; return text; }
                catch (DecoderFallbackException) { encoding = Encoding.GetEncoding(28591); return encoding.GetString(bytes); }
            }
        }
        // BepInEx.cfg before and after: a BepInEx installed now gets UnityLogListening off, and the log console goes off when
        // asked and still on. An existing configuration that cannot be read or edited losslessly keeps its bytes.
        private static (byte[]? Before, byte[]? After, bool ConsoleOff) PlanConfiguration(NativeMaintenanceBundle bundle, bool freshLoader, bool hideConsole, Action<string> output)
        {
            byte[]? before = null;
            if (freshLoader || hideConsole)
                try { before = bundle.ReadTarget(Configuration); }
                catch (Exception error) when (!freshLoader && (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ApplySafetyException)) {
                    output("BepInEx 설정을 읽지 못해 로그 창 설정은 바꾸지 않습니다. / BepInEx configuration unreadable; the log window setting is left unchanged.");
                    return (null, null, false);
                }
            var after = freshLoader ? DisableUnityListening(before) : null;
            if (!hideConsole) return (before, after, false);
            var state = ConsoleEnabled(before);
            if (state == null) output("BepInEx 로그 창 설정이 여러 줄이라 바꾸지 않습니다. / The BepInEx log window setting is ambiguous; left unchanged.");
            if (state != true) return (before, after, false);
            try { return (before, DisableConsole(after ?? before), true); }
            catch (InvalidDataException) {
                output("BepInEx 설정의 인코딩을 그대로 지킬 수 없어 로그 창 설정은 바꾸지 않습니다. / BepInEx configuration encoding not lossless; the log window setting is left unchanged.");
                return (before, after, false);
            }
        }
        // BepInEx's [Logging.Console] Enabled: true when absent (BepInEx's own default shows the console), null when ambiguous.
        internal static bool? ConsoleEnabled(byte[]? bytes)
        {
            if (bytes == null) return true;
            var text = Decode(bytes); var section = ConsoleSection(text);
            if (section == null) return true;
            var lines = ConsoleLine.Matches(text.Substring(section.Value.Start, section.Value.Length));
            return lines.Count == 0 ? true : lines.Count == 1 ? string.Equals(lines[0].Groups[2].Value, "true", StringComparison.OrdinalIgnoreCase) : (bool?)null;
        }
        private static readonly Regex ConsoleLine = new Regex(@"(?m)^([ \t]*Enabled[ \t]*=[ \t]*)(true|false)([ \t]*)(?=\r?$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // The [Logging.Console] body: from the line after its header to the next section header.
        private static (int Start, int Length)? ConsoleSection(string text)
        {
            var header = Regex.Match(text, @"(?m)^[ \t]*\[Logging\.Console\][ \t]*\r?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!header.Success) return null;
            var end = header.Index + header.Length; var start = end < text.Length && text[end] == '\n' ? end + 1 : end;
            var next = Regex.Match(text.Substring(start), @"(?m)^[ \t]*\[", RegexOptions.CultureInvariant);
            return (start, next.Success ? next.Index : text.Length - start);
        }
        // Turns the log console off through that one Enabled line, keeping every other byte as DisableUnityListening does.
        internal static byte[] DisableConsole(byte[]? bytes)
        {
            if (bytes == null) return Utf8.GetBytes("[Logging.Console]\r\nEnabled = false\r\n");
            Encoding encoding; var content = Decode(bytes, out encoding);
            var newline = content.Contains("\r\n") || !content.Contains("\n") ? "\r\n" : "\n";
            var section = ConsoleSection(content); string updated;
            if (section == null) updated = content + (content.Length == 0 || content.EndsWith("\n", StringComparison.Ordinal) ? "" : newline) + "[Logging.Console]" + newline + "Enabled = false" + newline;
            else {
                var lines = ConsoleLine.Matches(content.Substring(section.Value.Start, section.Value.Length));
                if (lines.Count > 1) throw new InvalidDataException("BepInEx log console setting is ambiguous; original bytes retained.");
                if (lines.Count == 1) {
                    var at = section.Value.Start + lines[0].Index;
                    updated = content.Substring(0, at) + lines[0].Groups[1].Value + "false" + lines[0].Groups[3].Value + content.Substring(at + lines[0].Length);
                } else {
                    var start = section.Value.Start;
                    updated = content.Substring(0, start) + (start == content.Length && !content.EndsWith("\n", StringComparison.Ordinal) ? newline : "") + "Enabled = false" + newline + content.Substring(start);
                }
            }
            if (updated == content) return bytes;
            var preamble = encoding.GetPreamble(); var prefix = preamble.Length != 0 && bytes.Take(preamble.Length).SequenceEqual(preamble) ? preamble : Array.Empty<byte>();
            if (!prefix.Concat(encoding.GetBytes(content)).SequenceEqual(bytes)) throw new InvalidDataException("Loader logging configuration encoding is not lossless; original bytes retained.");
            return prefix.Concat(encoding.GetBytes(updated)).ToArray();
        }
        internal static byte[] DisableUnityListening(byte[]? bytes)
        {
            if (bytes == null) return Utf8.GetBytes("[Logging]\r\nUnityLogListening = false\r\n");
            Encoding encoding; var content = Decode(bytes, out encoding);
            // BepInEx's existing workaround avoids Unity IL2CPP type-hook false positives.
            var updated = Regex.Replace(content, @"(?m)^([ \t]*UnityLogListening[ \t]*=[ \t]*)true([ \t]*)(?=\r?$)", "${1}false${2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (updated == content) return bytes;
            var preamble = encoding.GetPreamble(); var prefix = preamble.Length != 0 && bytes.Take(preamble.Length).SequenceEqual(preamble) ? preamble : Array.Empty<byte>();
            if (!prefix.Concat(encoding.GetBytes(content)).SequenceEqual(bytes)) throw new InvalidDataException("Loader logging configuration encoding is not lossless; original bytes retained.");
            return prefix.Concat(encoding.GetBytes(updated)).ToArray();
        }
        private static void WriteTarget(NativeMaintenanceBundle bundle, string relative, byte[] bytes, string? previous, bool create, Dictionary<string, string?> rollback)
        {
            using (var game = new WindowsFileFence(bundle.GameRoot)) {
                if (game.RootIdentity.Text != bundle.RootIdentity) throw new InvalidDataException("Installation root changed.");
                var slash = relative.LastIndexOf('/'); if (slash >= 0) game.EnsureDirectory(relative.Substring(0, slash));
                using (var target = game.OpenFile(relative, true, create)) {
                    if (!create && target.Hash() != previous) throw new InvalidDataException("Installation target changed since backup: " + relative);
                    var full = game.Full(relative); rollback[full] = UpdateTrust.Hash(bytes);
                    try { target.Stream.Position = 0; target.Stream.Write(bytes, 0, bytes.Length); target.Stream.SetLength(bytes.Length); target.Flush(); if (target.Hash() != rollback[full]) throw new InvalidDataException("Copied file verification failed: " + relative); }
                    catch { try { rollback[full] = target.Hash(); } catch { } throw; }
                }
            }
        }
        // Takeover removal: only the exact backed-up legacy file is deleted, by its held handle.
        private static void RemoveTarget(NativeMaintenanceBundle bundle, string relative, string expected, Dictionary<string, string?> rollback)
        {
            using (var game = new WindowsFileFence(bundle.GameRoot)) {
                if (game.RootIdentity.Text != bundle.RootIdentity) throw new InvalidDataException("Installation root changed.");
                using (var target = game.OpenFile(relative, true)) {
                    if (target.Hash() != expected) throw new InvalidDataException("Legacy file changed since backup: " + relative);
                    target.RemoveOrdinaryRecoveryTarget(relative); rollback[game.Full(relative)] = null;
                }
            }
        }
        private static void Transition(NativeMaintenanceBundle bundle)
        {
            bundle.AssertRoot();
            var state = SetupHost.ReadInstallerState(bundle.GameRoot);
            var match = Regex.Match(state, @"\ABetterAstralParty.InstallerState/v1\nroot=([A-F0-9]{8}:[A-F0-9]{16})\nversion=([^\r\n]*)\nwater-sha=([A-F0-9]{64})?\n\z");
            if (!match.Success || match.Groups[1].Value != bundle.RootIdentity || state.Length > 320) throw new InvalidDataException("Invalid installer state response.");
            var observed = bundle.Expected.Keys.Concat(new[] { InstallReceipt.Path, UpdateTransaction.HighWaterPath }).ToDictionary(path => path, bundle.TargetHash, StringComparer.Ordinal);
            if (observed[UpdateTransaction.HighWaterPath] != (match.Groups[3].Success ? match.Groups[3].Value : null)) throw new InvalidDataException("High-water changed during preflight.");
            var receiptBytes = bundle.ReadTarget(InstallReceipt.Path, 16384);
            if (receiptBytes == null) {
                if (bundle.Expected.Keys.Any(path => observed[path] != null) || observed[UpdateTransaction.HighWaterPath] != null)
                    throw new InvalidDataException("설치 기록 없이 모드 파일이 남아 있습니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / Mod files exist without an install receipt; run Force-Uninstall.cmd, then install again.");
                bundle.Mode = "FirstInstall"; bundle.Observed = observed; return;
            }
            var receipt = InstallReceipt.Parse(receiptBytes);
            var plugin = bundle.ReadTarget(UpdateTrust.PluginPath) ?? throw new InvalidDataException("설치된 플러그인이 없습니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / Installed plugin missing; run Force-Uninstall.cmd, then install again.");
            var current = InstalledPluginMetadata.ReadMaintenance(plugin).Version;
            var high = match.Groups[2].Value;
            bundle.Mode = TransitionMode(bundle.Version, current, high.Length == 0 ? null : high, receipt.Files, bundle.Expected, observed);
            bundle.Observed = observed;
        }
        internal static string TransitionMode(string incoming, string current, string? highest, IDictionary<string, string> previous, IDictionary<string, string> expected, IDictionary<string, string?> observed)
        {
            var next = UpdateVersion.Parse(incoming); var installed = UpdateVersion.Parse(current); var floor = installed;
            if (highest != null) { var high = UpdateVersion.Parse(highest); if (high.CompareTo(floor) > 0) floor = high; }
            if (previous.Any(file => observed[file.Key] != null && observed[file.Key] != file.Value)) throw new InvalidDataException("Existing installation differs from its receipt; preserved.");
            if (next.CompareTo(floor) < 0) throw new InvalidDataException("Downgrade blocked; use the latest complete bundle.");
            if (next.CompareTo(installed) == 0 && previous.Any(file => expected[file.Key] != file.Value)) throw new InvalidDataException("Different build at the same version; run Force-Uninstall.cmd, then install again.");
            return expected.All(file => observed[file.Key] == file.Value) ? "SameBuild" : "Upgrade";
        }
    }
}
