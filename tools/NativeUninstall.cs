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
    internal static class NativeUninstall
    {
        private static readonly string[] OwnedOrder = { UpdateTrust.PluginPath, "BetterAstralParty.compatibility.json",
            "BetterAstralParty-Mod.cmd", "AstralParty-Vanilla.cmd", OrdinaryInstallRecovery.LegacyLauncher };
        private static readonly string[] Exclusive = { "BepInEx/config/kr.betterastralparty.mod.cfg", "BepInEx/BetterAstralParty-Diagnostics/current.log",
            "BepInEx/BetterAstralParty-Diagnostics/previous.log", "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log",
            "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log", "BetterAstralParty-Compatibility.log", "BetterAstralParty-LaunchError.log", ChannelTransitionIntent.AdoptionPath };
        private static string Full(string root, string relative) { return WindowsPath.Absolute(root + "\\" + relative.Replace('/', '\\')); }

        internal sealed class Plan
        {
            internal readonly List<string> Targets = new List<string>(), MissingOwned = new List<string>();
            internal readonly Dictionary<string, string?> Hashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            internal bool Retained;
        }

        // Owned files from a 1.4.0 receipt, or from a 1.3.x receipt this package can also remove.
        internal sealed class Receipt
        {
            internal readonly Dictionary<string, string> Files; internal readonly bool? LoaderBefore; internal readonly bool Legacy;
            internal Receipt(Dictionary<string, string> files, bool? loader, bool legacy) { Files = files; LoaderBefore = loader; Legacy = legacy; }
        }
        internal static Receipt? ReadReceipt(NativeMaintenanceBundle bundle)
        {
            var bytes = bundle.ReadTarget(InstallReceipt.Path, 16384);
            if (bytes != null)
            {
                if (LegacyReceipt.Is(bytes)) { var legacy = LegacyReceipt.Parse(bytes); return new Receipt(legacy.Files, legacy.LoaderBefore, true); }
                try { var current = InstallReceipt.Parse(bytes); return new Receipt(current.Files, current.LoaderBefore, false); }
                catch (Exception error) when (error is ApplySafetyException || error is DecoderFallbackException)
                { throw new InvalidOperationException("설치 기록을 읽을 수 없습니다. 파일을 보존합니다. Force-Uninstall.cmd를 사용하세요. / The install receipt is unreadable; files are kept. Use Force-Uninstall.cmd.", error); }
            }
            if (bundle.Expected.Keys.Any(name => bundle.TargetHash(name) != null))
                throw new InvalidOperationException("설치 기록이 없거나 이전 설치가 남아 있습니다. 파일을 보존합니다. / Missing baseline receipt or previous installation retained.");
            return null;
        }

        internal static Plan PlanFiles(NativeMaintenanceBundle bundle, Receipt? receipt, Action<string> output)
        {
            var plan = new Plan(); var game = bundle.GameRoot;
            var helper = Full(game, OrdinaryInstallRecovery.LegacyHelper); var helperReceipt = Full(game, OrdinaryInstallRecovery.LegacyHelperReceipt);
            var imageHash = bundle.TargetHash(OrdinaryInstallRecovery.LegacyHelper); var receiptBytes = bundle.ReadTarget(OrdinaryInstallRecovery.LegacyHelperReceipt, 1024);
            if (imageHash != null || receiptBytes != null)
            {
                // A 1.3.x helper is removed only as the pair its own receipt describes.
                var text = receiptBytes == null ? "" : Encoding.ASCII.GetString(receiptBytes);
                if (receipt != null && receipt.Legacy && imageHash != null && receiptBytes != null && text.StartsWith("BetterAstralParty.HelperReceipt/v1\nroot=" + bundle.RootIdentity + "\n", StringComparison.Ordinal) && text.Contains("\nsha256=" + imageHash + "\n"))
                {
                    plan.Targets.Add(helper); plan.Targets.Add(helperReceipt); plan.Hashes[helper] = imageHash; plan.Hashes[helperReceipt] = UpdateTrust.Hash(receiptBytes);
                }
                else
                {
                    plan.Retained = true;
                    output("helper의 소유를 확인할 수 없어 파일·기록을 보존합니다. / Unverified update helper and receipt retained.");
                }
            }
            foreach (var relative in OwnedOrder.Concat(Exclusive))
            {
                var path = Full(game, relative); var hash = bundle.TargetHash(relative);
                string? declared = null; var recorded = receipt != null && receipt.Files.TryGetValue(relative, out declared);
                if (hash == null)
                {
                    if (recorded) { plan.MissingOwned.Add(path); plan.Hashes[path] = null; }
                    continue;
                }
                var owned = Exclusive.Contains(relative, StringComparer.Ordinal) || recorded && hash == declared;
                if (!owned && receipt == null && bundle.Expected.TryGetValue(relative, out var incoming)) owned = hash == incoming;
                if (owned) { plan.Targets.Add(path); plan.Hashes[path] = hash; }
                else
                {
                    if (receipt != null) plan.Retained = true;
                    output("보존: 소유 확인 불가 또는 변경된 파일 / Kept unknown or modified file: " + path);
                }
            }
            if (plan.Retained)
            {
                // Keep signed helper provenance with any retained receipt for reviewed repair.
                plan.Targets.RemoveAll(path => path == helper || path == helperReceipt); plan.Hashes.Remove(helper); plan.Hashes.Remove(helperReceipt);
                output("소유권이 불확실하거나 수정된 파일과 설치 기록을 보존합니다. / Unknown or modified files and their receipt are retained.");
            }
            else if (receipt != null)
            {
                var path = Full(game, InstallReceipt.Path); var hash = bundle.TargetHash(InstallReceipt.Path);
                if (hash == null) throw new InvalidDataException("Installation receipt changed during removal planning.");
                plan.Targets.Add(path); plan.Hashes[path] = hash;
            }
            return plan;
        }

        internal static string? LoaderChange(NativeMaintenanceBundle bundle, Receipt? receipt, string restoreLoader, bool whatIf,
            Func<string, string?> consent, Action<string> output, Action<string>? skipped = null)
        {
            if (restoreLoader.Equals("Keep", StringComparison.OrdinalIgnoreCase)) return null;
            string? Skip(string reason)
            {
                output("공용 로더 설정을 안전하게 변경할 수 없어 그대로 둡니다. 모드 파일 제거는 계속합니다. / Shared loader configuration retained; verified mod file removal can continue. Reason: " + reason);
                try { skipped?.Invoke(reason); } catch { }
                return null;
            }
            byte[]? bytes;
            try { bytes = bundle.ReadTarget("doorstop_config.ini", 1024 * 1024); }
            catch (Exception error) when (error is ApplySafetyException || error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            { return Skip("ForeignConfigurationReadUnavailable"); }
            if (bytes == null) return Skip("ForeignConfigurationAbsent");
            string text;
            try { using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true)) text = reader.ReadToEnd(); }
            catch (DecoderFallbackException) { return Skip("ForeignConfigurationUnsupported"); }
            const string pattern = @"(?m)^enabled\s*=\s*(true|false)\s*$";
            var enabled = Regex.Matches(text, pattern);
            if (enabled.Count > 1) return Skip("ForeignConfigurationAmbiguous");
            if (enabled.Count == 0) return Skip("ForeignConfigurationUnsupported");
            bool? restore = restoreLoader.Equals("Recorded", StringComparison.OrdinalIgnoreCase)
                ? enabled[0].Groups[1].Value == "true" ? true : receipt?.LoaderBefore
                : restoreLoader.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
            if (!restore.HasValue)
            {
                output("설치 전 로더 상태 기록이 없습니다. / Original loader state was not recorded.");
                if (whatIf) { output("실제 제거 시 로더 활성화/비활성화를 선택해야 합니다. / Actual removal requires an explicit loader choice."); return null; }
                output("1: 다른 BepInEx 모드 실행 유지 (로더 켜기, 권장) / Keep other BepInEx mods running (enable loader, recommended)");
                output("2: 모든 BepInEx 모드 실행 끄기 / Disable all BepInEx mods");
                var choice = consent("1 / 2 선택, Enter는 취소 / Choose 1 / 2, Enter cancels");
                if (choice != "1" && choice != "2") throw new OperationCanceledException("취소했습니다. 아무것도 변경하지 않았습니다. / Cancelled; nothing changed.");
                restore = choice == "1";
            }
            return Regex.Replace(text, pattern, "enabled=" + restore.Value.ToString().ToLowerInvariant());
        }

        internal static int Run(NativeMaintenanceBundle bundle, string? steamRoot, uint account, string restoreLoader, bool whatIf,
            Func<string, string?> consent, Action<string> output, Action<string>? observation = null)
        {
            if (!new[] { "Keep", "Recorded", "Enabled", "Disabled" }.Contains(restoreLoader, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Invalid loader restoration choice.");
            var game = bundle.GameRoot; var steamSession = new SteamSessionState();
            NativeMaintenance.InstallLock? installLock = null; UpdateLaunchGate.Lease? maintenance = null;
            NativeMaintenance.Transaction? transaction = null; OrdinaryInstallRecovery.OwnedRemovalLease? removal = null; ShortcutRecovery? removedShortcut = null;
            var rollback = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); string[] backupTargets = new string[0];
            try
            {
                string? shortcutSkipped = null; Exception? shortcutDiscoveryError = null;
                if (steamRoot == null) {
                    if (account != 0) throw new InvalidOperationException("명시한 Steam 계정을 확인할 설치 폴더가 없습니다. / Cannot verify the requested Steam account without its installation directory.");
                    shortcutSkipped = "SteamRootAbsent";
                } else if (account == 0) {
                    try { account = NativeSteamMaintenance.ResolveAccount(steamRoot, account); }
                    catch (SteamProfileUnavailableException unavailable) when (unavailable.Reason == "SteamRootAbsent" || unavailable.Reason == "SteamProfileAbsent" || unavailable.Reason == "SteamProfileAmbiguous" || unavailable.Reason == "SteamProfileMetadataInvalid") { shortcutSkipped = unavailable.Reason; }
                    catch (Exception unavailable) when (unavailable is IOException || unavailable is InvalidDataException || unavailable is InvalidOperationException || unavailable is UnauthorizedAccessException || unavailable is System.Security.SecurityException || unavailable is DecoderFallbackException)
                    { shortcutSkipped = "SteamProfileReadUnavailable"; shortcutDiscoveryError = unavailable; }
                } else account = NativeSteamMaintenance.ResolveAccount(steamRoot, account);
                var removeShortcut = shortcutSkipped == null && NativeSteamMaintenance.PlanRemove(steamRoot!, account, game, error => {
                    shortcutSkipped = (error as SteamProfileUnavailableException)?.Reason ?? "SteamShortcutPlanUnavailable"; shortcutDiscoveryError = error;
                });
                if (NativeMaintenance.HasPending(bundle))
                {
                    if (whatIf) { NativeMaintenance.RecoverPending(bundle, consent, output, true); return 0; }
                    installLock = NativeMaintenance.AcquireInstallLock(bundle);
                    if (!NativeMaintenance.RecoverPending(bundle, consent, output)) return 0;
                }
                if (!whatIf) NativeSteamMaintenance.WaitGameClosed(game, consent, output);
                // 7. Same interrupted-update recovery as Install, before the receipt and plugin checks: an update cut off
                // between its file moves leaves the plugin missing until this recovery puts it back.
                var pendingUpdate = SetupHost.HasPendingUpdate(game); var cancelled = "취소했습니다. 아무것도 변경하지 않았습니다. / Cancelled; nothing changed.";
                if (pendingUpdate && whatIf) output("WhatIf: 중단된 자동 업데이트를 먼저 복구합니다. / An interrupted automatic update will be recovered first.");
                else if (pendingUpdate) {
                    output("자동 업데이트 복구 결과 / Update recovery result: " + SetupHost.RecoverPendingUpdate(game));
                    cancelled = "취소했습니다. 중단된 자동 업데이트 복구 외에는 바꾸지 않았습니다. / Cancelled; only the interrupted automatic update was recovered.";
                }
                var receipt = ReadReceipt(bundle);
                // 10. Removing with the plugin already gone leaves history the next install cannot bind; stop before any change.
                // A preview cannot know what the pending recovery restores, so only the real run decides.
                if (receipt != null && !receipt.Legacy && bundle.TargetHash(UpdateTrust.PluginPath) == null && !(whatIf && pendingUpdate))
                    throw new InvalidOperationException("설치된 플러그인이 없어 파일을 보존합니다. Force-Uninstall.cmd로 정리한 뒤 다시 설치하세요. / Installed plugin missing; files are kept. Run Force-Uninstall.cmd, then install again.");
                if (whatIf) { if (!pendingUpdate) using (var fence = new WindowsFileFence(game)) UpdateLaunchGate.Check(fence); }
                else maintenance = new UpdateLaunchGate.Lease(game);
                bundle.AssertRoot();
                if (receipt != null && !receipt.Legacy) SetupHost.ValidateInstalledHistory(game);
                var plan = PlanFiles(bundle, receipt, output);
                if (plan.Targets.Count == 0 && !removeShortcut)
                { output("제거할 확인된 파일·Steam 라이브러리 항목이 없습니다. / No verified mod files or shortcut to remove."); return 0; }
                var loaderPath = Full(game, "doorstop_config.ini"); string? loaderUpdated = null, loaderSkipped = null;
                if (!restoreLoader.Equals("Keep", StringComparison.OrdinalIgnoreCase) && plan.Targets.Count != 0)
                    try { loaderUpdated = LoaderChange(bundle, receipt, restoreLoader, whatIf, consent, output, reason => loaderSkipped = reason); }
                    catch (OperationCanceledException) { output(cancelled); return 0; }
                var backups = plan.Targets.Concat(plan.MissingOwned).ToList();
                if (loaderUpdated != null) { backups.Add(loaderPath); plan.Hashes[loaderPath] = bundle.TargetHash("doorstop_config.ini"); }
                backupTargets = backups.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var path in backupTargets) NativeMaintenance.AssertTarget(game, path);
                output("제거 대상 / Files to remove (" + game + "):"); foreach (var path in plan.Targets) output("  " + path);
                if (loaderUpdated != null) output("공용 로더 enabled 설정을 명시한 선택으로 복원합니다. / Restore shared loader enabled setting; other settings preserved.");
                if (removeShortcut) output("Remove the matching Astral Party - Mod shortcut only; keep all other Steam entries.");
                if (shortcutSkipped != null) output((shortcutDiscoveryError as SteamProfileUnavailableException)?.Guidance
                    ?? "Steam 정보를 확정할 수 없어 Steam 라이브러리 항목은 남겨 둡니다. 확인된 모드 파일만 백업 후 제거합니다. / Steam discovery unavailable or ambiguous; keep shortcuts and back up/remove only verified mod files. Reason: " + shortcutSkipped);
                output("전용 설정·로그도 백업 후 제거합니다. 공용 로더·다른 모드는 유지합니다. / Includes mod settings/logs; shared loader and other mods stay.");
                var legacyWork = LegacyWork.Finished(game).Count;
                if (legacyWork != 0) output("끝난 1.3.x 업데이트 작업 기록 " + legacyWork + "개도 정리합니다. / Also removes " + legacyWork + " finished 1.3.x update work records.");
                var archived = UpdateFaults.RemovableArchived(game);
                if (archived != 0) output("끝난 업데이트 기록 " + archived + "개도 정리합니다. / Also removes " + archived + " finished update records.");
                if (whatIf) { output("WhatIf: BetterAstralParty 파일 백업 및 제거 / Back up and remove listed BetterAstralParty files"); return 0; }
                if (consent("BetterAstralParty 파일을 백업 후 제거할까요? / Back up and remove listed BetterAstralParty files? [y/N]") != "y")
                { output(cancelled); return 0; }
                if (shortcutSkipped != null) try { observation?.Invoke("NativeUninstallShortcut: action=Skipped; reason=" + shortcutSkipped
                    + (shortcutDiscoveryError == null ? "" : "; exception=" + shortcutDiscoveryError.GetType().Name + "; hresult=0x" + shortcutDiscoveryError.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture))); } catch { }
                if (loaderSkipped != null) try { observation?.Invoke("NativeUninstallLoader: action=Skipped; reason=" + loaderSkipped); } catch { }
                SetupHost.RecordInstallerStage("Uninstall", "Begin");
                if (backupTargets.Length != 0)
                {
                    if (installLock == null) installLock = NativeMaintenance.AcquireInstallLock(bundle);
                    if (NativeMaintenance.HasPending(bundle)) throw new InvalidOperationException("미완료 복구를 먼저 해결하세요. / Pending recovery: " + NativeMaintenance.JournalPath(game));
                }
                if (removeShortcut)
                {
                    NativeSteamMaintenance.StopSteam(steamRoot!, steamSession, consent, output);
                    removeShortcut = NativeSteamMaintenance.PlanRemove(steamRoot!, account, game);
                }
                NativeSteamMaintenance.AssertGameStopped(game);
                if (removeShortcut) NativeSteamMaintenance.AssertStopped(steamRoot!, "UninstallPreflight");
                bundle.AssertRoot();
                if (backupTargets.Length != 0)
                {
                    transaction = NativeMaintenance.StartTransaction(bundle, backupTargets, 0);
                    output("복구용 백업 / Recovery backup: " + transaction.BackupDir);
                    foreach (var entry in transaction.Entries)
                        if (!plan.Hashes.TryGetValue(entry.Target, out var hash) || entry.Hash != hash) throw new InvalidDataException("File changed since confirmation preview: " + entry.Target);
                    removal = new OrdinaryInstallRecovery.OwnedRemovalLease(game, transaction.JournalRoot, transaction.JournalHash, backupTargets, bundle.RootIdentity, loaderUpdated != null);
                    foreach (var entry in transaction.Entries)
                    {
                        if (entry.Existed && entry.Target == loaderPath)
                        { rollback[loaderPath] = UpdateTrust.Hash(new UTF8Encoding(false).GetBytes(loaderUpdated!)); removal.WriteLoader(loaderPath, loaderUpdated!); }
                        else { rollback[entry.Target] = null; removal.Remove(entry.Target); }
                    }
                }
                if (removeShortcut) removedShortcut = NativeSteamMaintenance.Remove(steamRoot!, account, game, output);
                removal?.Dispose(); removal = null;
                if (transaction != null) { NativeMaintenance.CompleteTransaction(bundle, transaction, backupTargets); transaction = null; }
                LegacyWork.Remove(game, output);
                UpdateFaults.RemoveArchived(game, output);
                maintenance?.Dispose(); maintenance = null;
                SetupHost.RecordInstallerStage("Uninstall", "Completed");
                foreach (var relative in new[] { "BepInEx/plugins/BetterAstralParty", "BepInEx/BetterAstralParty-Diagnostics" })
                {
                    var path = Full(game, relative);
                    try { using (var fence = new WindowsFileFence(game)) { if (fence.RootIdentity.Text != bundle.RootIdentity) throw new ApplySafetyException(ApplyFailure.IdentityChanged); fence.RemoveEmptyMaintenanceDirectory(relative); } }
                    catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { }
                    catch (ApplySafetyException) { output("Empty folder retained: " + path); }
                }
                output(plan.Retained || receipt == null
                    ? "확인된 파일 제거를 마쳤습니다. 미확인·변경 파일과 기록은 남아 있습니다. / Verified files removed; unknown or modified files and evidence remain."
                    : shortcutSkipped != null
                        ? "모드 파일 제거 완료. Steam 라이브러리 항목은 남겨 두었습니다. Astral Party - Mod가 남아 있으면 Steam에서 직접 제거하세요. 공용 로더·다른 모드는 유지합니다. / Removed verified mod files; Steam shortcut cleanup skipped. Shared loader and other mods preserved."
                        : "제거 완료. 모드 파일과 Steam 라이브러리의 Astral Party - Mod를 정리했습니다. 공용 로더 설정과 다른 모드는 유지합니다. / Removed mod files and shortcut; shared loader settings and other mods preserved.");
                return 0;
            }
            catch (Exception error)
            {
                removal?.Dispose(); removal = null;
                if (removedShortcut != null)
                    try { NativeSteamMaintenance.AssertStopped(steamRoot!, "ShortcutRollback"); NativeSteamMaintenance.Rollback(removedShortcut); }
                    catch (Exception rollbackError) { RecordRollback(error, rollbackError); output("Steam shortcut recovery needs review; retain backup: " + removedShortcut.Backup); }
                if (transaction != null)
                    try { SetupHost.RecordInstallerStage("Rollback", "Begin"); NativeMaintenance.RestoreTransaction(bundle, transaction, backupTargets, rollback); SetupHost.RecordInstallerStage("Rollback", "Completed"); }
                    catch (Exception rollbackError) { RecordRollback(error, rollbackError); output("복구 미완료: 백업을 보관하세요. / Recovery incomplete; retain " + transaction.JournalPath); }
                throw;
            }
            finally { removal?.Dispose(); installLock?.Dispose(); maintenance?.Dispose(); NativeSteamMaintenance.ResumeSteam(steamSession, output); }
        }
        private static void RecordRollback(Exception original, Exception failure)
        {
            SetupHost.RecordInstallerFailure("Rollback", failure);
            original.Data["BapRollbackFailure"] = original.Data["BapRollbackFailure"] is Exception prior ? new AggregateException(prior, failure) : failure;
        }
    }
}
