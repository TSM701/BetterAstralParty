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
        private static bool PlanShortcut(NativeMaintenanceBundle bundle, bool updateOnly, string? steamRoot, uint account, Action<string> output, Action<string>? observation = null)
        {
            return !updateOnly && steamRoot != null && account != 0 && NativeSteamMaintenance.PlanRegister(steamRoot, account, bundle.GameRoot, error => {
                var detail = "NativeInstallShortcut: action=Skipped; reason=SteamShortcutPlanUnavailable; exception=" + error.GetType().Name
                    + "; hresult=0x" + error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
                output("Steam 바로가기 정보를 읽지 못해 기존 정보를 보존하고 파일만 설치합니다. / Steam shortcut planning unavailable; preserve profile data and install files only. " + detail);
                try { observation?.Invoke(detail); } catch { }
            });
        }
        internal static void Run(NativeMaintenanceBundle bundle, bool updateOnly, string? steamRoot, uint account, Func<string, string?> consent, Action<string> output, bool restartSteam = false, bool checkOnly = false, Action<string>? observation = null)
        {
            if (updateOnly && (restartSteam || account != 0 || steamRoot != null)) throw new InvalidOperationException("UpdateOnly cannot register or restart Steam. / UpdateOnly는 Steam을 변경할 수 없습니다.");
            if (!updateOnly && (steamRoot == null || account == 0)) output("Steam 바로가기를 변경하지 않고 파일을 설치합니다. Steam에서 순정 게임 실행 후 설치기를 다시 실행하면 바로가기를 추가할 수 있습니다. / Install files without changing Steam shortcuts; run the original game through Steam and rerun installation to add the shortcut.");
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
                var extra = new List<string> { InstallReceipt.Path };
                if (freshLoader) extra.Add(Configuration);
                if (water != null) extra.Add(UpdateTransaction.HighWaterPath);
                if (takeover != null) extra.AddRange(takeover.Removals.Keys);
                targets = files.Select(file => Full(bundle.GameRoot, file.Relative)).Concat(extra.Select(path => Full(bundle.GameRoot, path))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                long installBytes = 0; foreach (var file in files) installBytes = checked(installBytes + bundle.Bytes(file.Source, 64 * 1048576).Length);
                stage = "Maintenance path planning / write permission preflight";
                foreach (var target in targets) NativeMaintenance.AssertTarget(bundle.GameRoot, target);
                NativeMaintenance.TestWritable(targets);
                var needsShortcut = PlanShortcut(bundle, updateOnly, steamRoot, account, output, observation);
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
                    var config = freshLoader ? bundle.ReadTarget(Configuration) : null;
                    var updated = freshLoader ? DisableUnityListening(config) : null;
                    if (freshLoader && (config == null || !updated!.SequenceEqual(config))) {
                        var entry = Entry(transaction, bundle, Configuration);
                        WriteTarget(bundle, Configuration, updated!, entry.Hash, !entry.Existed, rollback);
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
                }
                output(needsShortcut ? "적용 완료! Steam 라이브러리의 \"Astral Party - Mod\"로 실행하세요. / Installed; start \"Astral Party - Mod\" from Steam." : "기존 설치본 업데이트 완료. / Updated existing installation.");
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
            if (generation != null) {
                if (updateOnly) throw new InvalidOperationException("Clean reinstall requires normal installation, not UpdateOnly.");
                output("CheckOnly: verified complete removal; new generation requires confirmation. / 완전 제거 확인: 새 설치 전에 동의가 필요합니다.");
            } else if (LegacyTakeover(bundle, output) != null) {
                if (updateOnly) throw new InvalidOperationException("Taking over 1.3.x requires a normal installation, not UpdateOnly.");
                output("CheckOnly: 1.3.x installation will be taken over after confirmation. / 1.3.x 설치는 동의 후 인계합니다.");
            } else Transition(bundle);
            var existingLoader = ExistingSharedLoader(bundle, output);
            if (updateOnly && (bundle.ReadTarget(InstallReceipt.Path, 16384) == null || bundle.TargetHash(UpdateTrust.PluginPath) == null)) throw new InvalidOperationException("UpdateOnly requires an existing installation.");
            if (!existingLoader) SharedLoaderPlan(bundle, new Dictionary<string, string?>(StringComparer.Ordinal));
            var shortcut = PlanShortcut(bundle, updateOnly, steamRoot, account, output);
            var legacyWork = LegacyWork.Finished(bundle.GameRoot).Count;
            if (legacyWork != 0) output("CheckOnly: 끝난 1.3.x 업데이트 작업 기록 " + legacyWork + "개를 설치 후 정리합니다. / " + legacyWork + " finished 1.3.x update work records will be removed after installation.");
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
