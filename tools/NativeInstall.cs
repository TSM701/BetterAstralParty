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
        private static string Full(string root, string relative) { return WindowsPath.Absolute(root + "\\" + relative.Replace('/', '\\')); }
        private static void Stage(string phase, string outcome = "Begin") { try { SteamLauncher.RecordInstallerStage(phase, outcome); } catch { } }
        private static void Failure(string phase, Exception error) { try { SteamLauncher.RecordInstallerFailure(phase, error); } catch { } }
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
            var session = new SteamSessionState(); NativeMaintenance.Transaction? transaction = null; IDisposable? maintenance = null;
            NativeMaintenance.InstallLock? installLock = null;
            var rollback = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); string[] targets = Array.Empty<string>(); var registered = false;
            var stage = "Signed automatic update helper preflight"; var phase = "HelperInstall";
            try {
                Stage(phase); bundle.AssertRoot(); SteamLauncher.ValidateWholeInputs(bundle.GameRoot, bundle.PackageRoot, bundle.Version);
                var helperHash = bundle.Hashes["helper-bootstrap/BetterAstralParty-UpdateHelper.exe"];
                if (SteamLauncher.ValidateHelperBootstrap(Full(bundle.PackageRoot, "helper-bootstrap"), bundle.Version, helperHash) != helperHash) throw new InvalidDataException("Helper bootstrap hash mismatch.");
                Stage(phase, "Completed");
                installLock = NativeMaintenance.AcquireInstallLock(bundle);
                {
                    stage = "Interrupted installation/removal recovery"; phase = "Recovery"; Stage(phase);
                    if (File.Exists(WindowsPath.ForIo(NativeMaintenance.JournalPath(bundle.GameRoot)))) NativeMaintenance.RecoverPending(bundle, consent, output);
                    else NativeSteamMaintenance.StopGame(bundle.GameRoot, consent, output);
                    Stage(phase, "Completed");
                    // Compatibility is observation only; authenticated ownership and recovery still govern writes.
                    stage = "Compatibility validation"; phase = "BootstrapGate"; Stage(phase);
                    CheckCompatibility(bundle, _ => { }); Stage(phase, "Completed");
                    stage = "Completed removal / new installation generation validation"; phase = "Recovery"; Stage(phase);
                    var generation = NativeReinstallGeneration.Review(bundle.GameRoot, bundle.PackageRoot, bundle.Version, bundle.ManifestHash, installLock.Root);
                    if (generation != null) {
                        if (updateOnly || bundle.Recovery != null) throw new InvalidOperationException("Clean reinstall requires normal installation, not UpdateOnly or reviewed recovery.");
                        output("완전 제거 상태를 확인했습니다. 이전 업데이트 이력을 보존하고 새로 설치합니다. / Complete removal verified; archive previous history and install: " + generation.Version + " -> " + bundle.Version);
                        Consent(consent, "새 설치를 진행할까요? / Start a new installation generation? [y/N]");
                        NativeReinstallGeneration.Retire(generation, bundle.GameRoot, bundle.PackageRoot, bundle.Version, bundle.ManifestHash, installLock.Root, installLock.File);
                    }
                    stage = "Whole helper recovery"; SteamLauncher.RecoverWholeHelper(bundle.GameRoot, bundle.PackageRoot, bundle.Version); Stage(phase, "Completed");
                    stage = "Installed ownership / whole bridge validation"; phase = "DescriptorVerification"; Stage(phase);
                    var bridgeObservation = bundle.Recovery == null ? SteamLauncher.ValidateWholeBridge(bundle.GameRoot, bundle.PackageRoot, bundle.Version)
                        : SteamLauncher.ValidateReviewedWholeBridge(bundle.GameRoot, bundle.PackageRoot, bundle.Version, Utf8.GetString(MaintenanceJson.Write(bundle.Recovery)));
                    bundle.Observation = bridgeObservation;
                    stage = "Installed version / channel transition validation"; Transition(bundle, consent); Stage(phase, "Completed");
                    // Keep the existing later gate as well; recovery work must not make an earlier game observation authoritative.
                    stage = "Compatibility validation"; phase = "BootstrapGate"; Stage(phase);
                    CheckCompatibility(bundle, output); Stage(phase, "Completed");
                    stage = "Package file / shared loader validation"; phase = "BootstrapGate"; Stage(phase);
                    var files = OwnedPlan(); var shared = new Dictionary<string, string?>(StringComparer.Ordinal);
                    var freshLoader = !ExistingSharedLoader(bundle, output);
                    var loader = freshLoader ? null : ReadExistingLoader(bundle, output);
                    if (freshLoader) files.AddRange(SharedLoaderPlan(bundle, shared));
                    var oldReceiptBytes = bundle.ReadTarget(InstallReceipt.Path, 16384);
                    var oldReceipt = oldReceiptBytes == null ? null : InstallReceipt.Parse(oldReceiptBytes);
                    if (updateOnly && (oldReceipt == null || bundle.TargetHash(UpdateTrust.PluginPath) == null)) throw new InvalidOperationException("UpdateOnly requires an existing installation. / 먼저 일반 설치를 실행하세요.");
                    var loaderBefore = LoaderBaseline(oldReceipt, loader, freshLoader, bundle.TargetHash(UpdateTrust.PluginPath) == null);
                    var prepared = SteamLauncher.PrepareWholeReceipt(bundle.GameRoot, bundle.PackageRoot, bundle.Version, bridgeObservation, loaderBefore.HasValue ? loaderBefore.Value ? "true" : "false" : "null");
                    targets = files.Select(file => Full(bundle.GameRoot, file.Relative)).Concat(freshLoader ? new[] { Full(bundle.GameRoot, Configuration), Full(bundle.GameRoot, InstallReceipt.Path) } : new[] { Full(bundle.GameRoot, InstallReceipt.Path) }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                    long installBytes = 0; foreach (var file in files) installBytes = checked(installBytes + bundle.Bytes(file.Source, 64 * 1048576).Length);
                    stage = "Maintenance path planning / write permission preflight";
                    foreach (var target in targets) NativeMaintenance.AssertTarget(bundle.GameRoot, target);
                    NativeMaintenance.TestWritable(targets);
                    var needsShortcut = PlanShortcut(bundle, updateOnly, steamRoot, account, output, observation);
                    if (needsShortcut) NativeMaintenance.TestWritable(new[] { Full(steamRoot!, "userdata/" + account + "/config/shortcuts.vdf") });
                    if (bundle.TargetHash(UpdateTrust.PluginPath) != null) output("기존 설치: 사용자 설정을 유지하고 변경 대상 파일을 백업합니다. / Existing installation: settings preserved; target files backed up.");
                    Stage(phase, "Completed"); stage = "Steam shortcut planning / shutdown"; phase = "WaitForExit"; Stage(phase);
                    if (needsShortcut) NativeSteamMaintenance.StopSteam(steamRoot!, session, consent, output);
                    NativeSteamMaintenance.AssertGameStopped(bundle.GameRoot);
                    if (needsShortcut) NativeSteamMaintenance.AssertStopped(steamRoot!, "InstallPreflight");
                    Stage(phase, "Completed"); phase = "Apply"; Stage(phase);
                    stage = "Previous installation backup"; bundle.AssertUnchanged(); AssertSharedUnchanged(bundle, shared);
                    maintenance = SteamLauncher.BeginWholeInstall(bundle.GameRoot, bundle.PackageRoot, bundle.Version, bridgeObservation, prepared);
                    transaction = NativeMaintenance.StartTransaction(bundle, targets, installBytes);
                    stage = "Loader / mod / launcher copy";
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
                        var entry = transaction.Entries.Single(row => row.Target.Equals(Full(bundle.GameRoot, Configuration), StringComparison.OrdinalIgnoreCase));
                        WriteTarget(bundle, Configuration, updated!, entry.Hash, !entry.Existed, rollback);
                    }
                    foreach (var file in bundle.Expected) if (bundle.TargetHash(file.Key) != file.Value) throw new InvalidDataException("Installed payload differs from the verified bundle: " + file.Key);
                    var receipt = transaction.Entries.Single(entry => entry.Target.Equals(Full(bundle.GameRoot, InstallReceipt.Path), StringComparison.OrdinalIgnoreCase));
                    WriteTarget(bundle, InstallReceipt.Path, Utf8.GetBytes(prepared), receipt.Hash, !receipt.Existed, rollback);
                    Stage(phase, "Completed"); phase = "Apply"; Stage(phase); stage = "Steam shortcut registration";
                    if (needsShortcut) { NativeSteamMaintenance.Register(steamRoot!, account, bundle.GameRoot, output); registered = true; }
                    NativeMaintenance.CompleteTransaction(bundle, transaction, targets); transaction = null; Stage(phase, "Completed");
                    // HelperDelivery owns a separate recoverable transaction after ordinary files and routing commit.
                    maintenance.Dispose(); maintenance = null; phase = "HelperInstall"; Stage(phase); stage = "Automatic update helper installation / ownership verification";
                    SteamLauncher.InstallWholeHelper(bundle.GameRoot, bundle.PackageRoot, bundle.Version, bridgeObservation, UpdateTrust.Hash(Utf8.GetBytes(prepared)));
                    var owned = SteamLauncher.CheckOwnedHelper(bundle.GameRoot);
                    if (owned == null || !owned.StartsWith(helperHash + ":", StringComparison.Ordinal)) throw new InvalidOperationException("Whole helper commit ownership verification failed; retain records.");
                    Stage(phase, "Completed");
                    output(needsShortcut ? "적용 완료! Steam 라이브러리에 Mod가 등록되었습니다. / Installed Astral Party - Mod." : "기존 설치본 업데이트 완료. / Updated existing installation.");
                }
            } catch (Exception error) {
                if (transaction != null && !registered) {
                    try { Stage("Rollback"); NativeMaintenance.RestoreTransaction(bundle, transaction, targets, rollback); transaction = null; Stage("Rollback", "Completed"); }
                    catch (Exception restore) { error.Data["BapRollbackFailure"] = restore; Failure("Rollback", restore); try { output("복구가 끝나지 않았습니다. 게임을 닫고 현재 묶음·journal·백업을 보관하세요. / Recovery incomplete; retain the current package, journal and backups. reason=" + restore.GetType().Name); } catch { } }
                }
                error.Data["BapInstallStage"] = stage; Failure(phase, error); throw;
            } finally { try { maintenance?.Dispose(); } finally { installLock?.Dispose(); NativeSteamMaintenance.ResumeSteam(session, output); } }
        }
        private static void Preview(NativeMaintenanceBundle bundle, bool updateOnly, string? steamRoot, uint account, Func<string, string?> consent, Action<string> output)
        {
            bundle.AssertRoot(); SteamLauncher.ValidateWholeInputs(bundle.GameRoot, bundle.PackageRoot, bundle.Version);
            var helperHash = bundle.Hashes["helper-bootstrap/BetterAstralParty-UpdateHelper.exe"];
            if (SteamLauncher.ValidateHelperBootstrap(Full(bundle.PackageRoot, "helper-bootstrap"), bundle.Version, helperHash) != helperHash) throw new InvalidDataException("Helper bootstrap hash mismatch.");
            if (!NativeMaintenance.RecoverPending(bundle, consent, output, true)) return;
            CheckCompatibility(bundle, output, false);
            var generation = NativeReinstallGeneration.Review(bundle.GameRoot, bundle.PackageRoot, bundle.Version, bundle.ManifestHash, NativeMaintenance.JournalRoot(bundle.GameRoot));
            if (generation != null) {
                if (updateOnly || bundle.Recovery != null) throw new InvalidOperationException("Clean reinstall requires normal installation, not UpdateOnly or reviewed recovery.");
                output("CheckOnly: verified complete removal; new generation requires confirmation. / 완전 제거 확인: 새 설치 전에 동의가 필요합니다.");
            } else {
                bundle.Observation = bundle.Recovery == null ? SteamLauncher.ValidateWholeBridge(bundle.GameRoot, bundle.PackageRoot, bundle.Version)
                    : SteamLauncher.ValidateReviewedWholeBridge(bundle.GameRoot, bundle.PackageRoot, bundle.Version, Utf8.GetString(MaintenanceJson.Write(bundle.Recovery)));
                Transition(bundle, consent, false);
            }
            var existingLoader = ExistingSharedLoader(bundle, output);
            if (updateOnly && (bundle.ReadTarget(InstallReceipt.Path, 16384) == null || bundle.TargetHash(UpdateTrust.PluginPath) == null)) throw new InvalidOperationException("UpdateOnly requires an existing installation.");
            if (!existingLoader) SharedLoaderPlan(bundle, new Dictionary<string, string?>(StringComparer.Ordinal));
            var shortcut = PlanShortcut(bundle, updateOnly, steamRoot, account, output);
            output("CheckOnly: installation validation complete; shortcutWrite=" + shortcut + ". / 설치 사전 검사가 완료되었습니다.");
        }
        private static List<FilePlan> OwnedPlan()
        {
            var order = new[] { "BetterAstralParty-Launcher.exe", UpdateTrust.PluginPath, "BetterAstralParty.compatibility.json", "BetterAstralParty-Mod.cmd", "AstralParty-Vanilla.cmd" };
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
        private static void Transition(NativeMaintenanceBundle bundle, Func<string, string?> consent, bool confirmRecovery = true)
        {
            bundle.AssertRoot();
            var state = SteamLauncher.ReadInstallerState(bundle.GameRoot);
            var match = Regex.Match(state, @"\ABetterAstralParty.InstallerState/v1\nroot=([A-F0-9]{8}:[A-F0-9]{16})\nversion=([^\r\n]*)\nwater-sha=([A-F0-9]{64})?\n\z");
            if (!match.Success || match.Groups[1].Value != bundle.RootIdentity || state.Length > 320) throw new InvalidDataException("Invalid installer state response.");
            var observed = bundle.Expected.Keys.Concat(new[] { InstallReceipt.Path, UpdateTransaction.HighWaterPath }).ToDictionary(path => path, bundle.TargetHash, StringComparer.Ordinal);
            if (observed[UpdateTransaction.HighWaterPath] != (match.Groups[3].Success ? match.Groups[3].Value : null)) throw new InvalidDataException("High-water changed during preflight.");
            var receiptBytes = bundle.ReadTarget(InstallReceipt.Path, 16384);
            if (receiptBytes == null) {
                if (bundle.Recovery != null || bundle.Expected.Keys.Any(path => observed[path] != null) || observed[UpdateTransaction.HighWaterPath] != null) throw new InvalidDataException("Incomplete installation requires recovery review.");
                bundle.Mode = "FirstInstall"; bundle.Observed = observed; return;
            }
            var receipt = InstallReceipt.Parse(receiptBytes); string current;
            if (bundle.Recovery != null || observed[UpdateTrust.PluginPath] == null) {
                var signed = SteamLauncher.ReadVerifiedInstalledState(bundle.GameRoot, bundle.PackageRoot, bundle.Version).Split('\n');
                if (signed.Length != 6 || signed[0] != "BetterAstralParty.VerifiedInstalledState/v1" || signed[1] != "root=" + bundle.RootIdentity || !signed[2].StartsWith("version=", StringComparison.Ordinal) || signed[3] != "update-protocol=4" || signed[4] != "settings-schema=1" || signed[5] != "") throw new InvalidDataException("Invalid signed installed-state response.");
                current = signed[2].Substring(8);
            } else current = InstalledPluginMetadata.ReadMaintenance(bundle.ReadTarget(UpdateTrust.PluginPath) ?? throw new InvalidDataException("Installed plugin missing.")).Version;
            var high = match.Groups[2].Value;
            bundle.Mode = TransitionMode(bundle.Version, current, high.Length == 0 ? null : high, receipt.Files, bundle.Expected, observed, bundle.Recovery);
            if (bundle.Recovery != null && confirmRecovery) Consent(consent, "명시한 복구 묶음으로 교체합니다. 현재 파일은 백업하며 업데이트 버전 기록은 유지합니다. / Apply the reviewed recovery plan? [y/N]");
            bundle.Observed = observed;
        }
        internal static string TransitionMode(string incoming, string current, string? highest, IDictionary<string, string> previous, IDictionary<string, string> expected, IDictionary<string, string?> observed, Dictionary<string, object?>? recovery)
        {
            var next = UpdateVersion.Parse(incoming); var installed = UpdateVersion.Parse(current); var floor = installed;
            if (highest != null) { var high = UpdateVersion.Parse(highest); if (high.CompareTo(floor) > 0) floor = high; }
            if (recovery != null) {
                if (MaintenanceJson.Text(recovery, "fromVersion") != current || MaintenanceJson.Text(recovery, "receiptSha256") != observed[InstallReceipt.Path]) throw new InvalidDataException("Recovery plan does not match the current installation.");
                if (!recovery.TryGetValue("observedFiles", out var raw)) throw new InvalidDataException("Recovery observation missing."); var files = MaintenanceJson.Map(raw);
                if (files.Count != observed.Count || observed.Any(file => !files.TryGetValue(file.Key, out var hash) || !Equals(hash, file.Value))) throw new InvalidDataException("Installation changed since recovery planning.");
                return "ReviewedRecovery";
            }
            if (previous.Any(file => observed[file.Key] != null && observed[file.Key] != file.Value)) throw new InvalidDataException("Existing installation differs from its receipt; preserved.");
            if (next.CompareTo(floor) < 0) throw new InvalidDataException("Downgrade blocked; use the latest complete bundle.");
            if (next.CompareTo(installed) == 0 && previous.Any(file => expected[file.Key] != file.Value)) throw new InvalidDataException("Different build at the same version requires reviewed recovery.");
            return expected.All(file => observed[file.Key] == file.Value) ? "SameBuild" : "Upgrade";
        }
    }
}
