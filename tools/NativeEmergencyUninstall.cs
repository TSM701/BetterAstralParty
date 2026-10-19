#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    internal static class NativeEmergencyUninstall
    {
        private const int MaxFiles = 4096, MaxDirectories = 2048, MaxDepth = 16;
        private const string UpdateLock = "BetterAstralParty.update.lock";
        private static readonly string[] Names = InstallReceipt.Owned.Concat(new[] {
            OrdinaryInstallRecovery.LegacyLauncher, OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyHelperReceipt, InstallReceipt.Path, "BetterAstralParty-Steam.ps1",
            "BepInEx/config/kr.betterastralparty.mod.cfg", "BepInEx/BetterAstralParty-Diagnostics/current.log",
            "BepInEx/BetterAstralParty-Diagnostics/previous.log", "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log",
            "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log", "BetterAstralParty-Compatibility.log", "BetterAstralParty-LaunchError.log", ChannelTransitionIntent.AdoptionPath, UpdateLock
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        private static readonly Regex RecoveryFile = new Regex(@"\ABetterAstralParty\.(?:update-v[345]\.(?:version(?:\.uninstalled-[0-9a-f]{32})?|failure|operation|preferences-failed|failed)|reinstall-v[345]\.(?:pending|completed-[0-9a-f]{32}))\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex RecoveryDirectory = new Regex(@"\A(?:bap-v[345]-(?:stage|txn|helperauto|helper|helperbridge|wholeintent|wholehist)-[0-9a-f]{32}|BetterAstralParty-emergency-[0-9a-f]{32})\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ContainedDirectory = new Regex(@"\Abap-v[45]-(?:stage|txn|helperauto|helper|helperbridge|wholeintent|wholehist)-[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ContainedTransition = new Regex(@"\Abap-v4-transition-[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private sealed class Scope { internal string Key = "", Root = "", Prefix = ""; }
        private sealed class Entry
        {
            internal string Scope = "", Relative = "", Target = "";
            internal bool Existed, Lock;
            internal long Bytes;
            internal string? Hash, Identity;
        }
        private sealed class DirectoryEntry { internal string Scope = "", Relative = "", Target = "", Identity = ""; }
        private sealed class Plan
        {
            internal readonly List<Scope> Scopes = new List<Scope>();
            internal readonly List<Entry> Files = new List<Entry>();
            internal readonly List<DirectoryEntry> Directories = new List<DirectoryEntry>();
        }
        private sealed class Held : IDisposable
        {
            internal Entry Entry = null!;
            internal WindowsFileFence.FileLease? File;
            public void Dispose() { File?.Dispose(); File = null; }
        }
#if BAP_SAFETY_FIXTURE
        internal static Action<string>? Boundary = null;
#endif
        private static void Hit(string phase)
        {
#if BAP_SAFETY_FIXTURE
            Boundary?.Invoke(phase);
#endif
        }
        private static Exception Stop(string reason, Exception? inner = null)
        {
            var error = new InvalidOperationException("긴급 제거를 중단했습니다. 이미 삭제한 파일은 복원하지 않습니다. / Emergency removal stopped; already deleted files are not restored. " + reason, inner);
            error.Data["BapRecoveryReason"] = reason; return error;
        }
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence fence, string relative, bool mutable = false)
        {
            try { return fence.OpenFile(relative, mutable); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
        }
        private static WindowsFileFence? MaybeScope(string root)
        {
            try { return new WindowsFileFence(root); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
        }
        private static bool DirectoryPresent(WindowsFileFence fence, string relative)
        {
            try { fence.DirectoryIdentity(relative); return true; }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return false; }
        }
        private static bool IsLock(string scope, string relative)
        { return scope == "game" ? relative == UpdateLock : relative.Count(c => c == '/') == 1 && relative.EndsWith("/pending.json.lock", StringComparison.Ordinal); }
        private static Scope[] Scopes(string gameRoot)
        {
            var backup = WindowsPath.Absolute(NativeMaintenance.JournalRoot(gameRoot));
            var key = WindowsPath.Leaf(backup); var v5 = WindowsPath.Parent(backup);
            if (!Regex.IsMatch(key, @"\A[0-9A-F]{64}\z") || WindowsPath.Leaf(v5) != "v5" || WindowsPath.Leaf(WindowsPath.Parent(v5)) != "Backups") throw Stop("EmergencyBackupScopeInvalid");
            return new[] { new Scope { Key = "game", Root = gameRoot }, new Scope { Key = "v5", Root = v5, Prefix = key },
                new Scope { Key = "v4", Root = WindowsPath.Parent(v5) + "\\v4", Prefix = key }, new Scope { Key = "v3", Root = WindowsPath.Parent(v5) + "\\v3", Prefix = key } };
        }
        private static void AddFile(Plan plan, Scope scope, WindowsFileFence? fence, string relative, bool skipData)
        {
            if (plan.Files.Count >= MaxFiles) throw Stop("EmergencyFileLimitExceeded");
            var row = new Entry { Scope = scope.Key, Relative = relative, Target = WindowsPath.Absolute(scope.Root + "\\" + relative.Replace('/', '\\')), Lock = IsLock(scope.Key, relative) };
            if (fence != null)
            {
                if (skipData) row.Existed = File.Exists(WindowsPath.ForIo(row.Target));
                else using (var file = Maybe(fence, relative, true)) if (file != null)
                { row.Existed = true; row.Bytes = file.Stream.Length; row.Hash = file.Hash(); row.Identity = file.Identity.Text; }
            }
            plan.Files.Add(row);
        }
        private static void Tree(Plan plan, Scope scope, WindowsFileFence fence, string relative, int depth, bool skipData)
        {
            if (depth > MaxDepth || plan.Directories.Count >= MaxDirectories) throw Stop("EmergencyDirectoryLimitExceeded");
            var path = fence.Full(relative); var identity = fence.DirectoryIdentity(relative).Text;
            plan.Directories.Add(new DirectoryEntry { Scope = scope.Key, Relative = relative, Target = path, Identity = identity });
            foreach (var childIo in Directory.EnumerateFileSystemEntries(WindowsPath.ForIo(path), "*", SearchOption.TopDirectoryOnly))
            {
                var child = WindowsPath.FromIo(childIo); var next = relative + "/" + WindowsPath.Leaf(child);
                fence.Full(next); fence.Recheck(); var attributes = File.GetAttributes(childIo);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
                if ((attributes & FileAttributes.Directory) != 0) Tree(plan, scope, fence, next, depth + 1, skipData);
                else if (!IsLock(scope.Key, next)) AddFile(plan, scope, fence, next, skipData);
            }
            fence.Recheck();
        }
        private static void WorkContainer(Plan plan, Scope scope, WindowsFileFence fence, bool skipData)
        {
            var identity = fence.DirectoryIdentity(UpdateWorkPath.Root).Text;
            fence.AssertPrivateDirectory(UpdateWorkPath.Root);
            var complete = true; var scanned = 0;
            foreach (var childIo in Directory.EnumerateFileSystemEntries(WindowsPath.ForIo(fence.Full(UpdateWorkPath.Root)), "*", SearchOption.TopDirectoryOnly))
            {
                if (++scanned > MaxFiles + MaxDirectories) throw Stop("EmergencyWorkInventoryLimitExceeded");
                var name = WindowsPath.Leaf(WindowsPath.FromIo(childIo)); var relative = UpdateWorkPath.Root + "/" + name;
                var directory = ContainedDirectory.IsMatch(name); var transition = ContainedTransition.IsMatch(name);
                if (!directory && !transition) { complete = false; continue; }
                var attributes = File.GetAttributes(childIo);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
                if (directory != ((attributes & FileAttributes.Directory) != 0)) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                if (directory) Tree(plan, scope, fence, relative, 2, skipData);
                else AddFile(plan, scope, fence, relative, skipData);
            }
            // A namespace is not ownership of arbitrary descendants. Remove it only when every child was selected.
            if (complete) {
                if (plan.Directories.Count >= MaxDirectories) throw Stop("EmergencyDirectoryLimitExceeded");
                plan.Directories.Add(new DirectoryEntry { Scope = scope.Key, Relative = UpdateWorkPath.Root, Target = fence.Full(UpdateWorkPath.Root), Identity = identity });
            }
            fence.Recheck();
        }
        private static Plan Observe(string gameRoot, bool skipData = false, IDictionary<string, WindowsFileFence>? heldFences = null)
        {
            var plan = new Plan(); plan.Scopes.AddRange(Scopes(gameRoot));
            foreach (var scope in plan.Scopes)
            {
                WindowsFileFence? fence = null; var owned = false;
                try
                {
                    if (heldFences != null) heldFences.TryGetValue(scope.Key, out fence);
                    else { fence = MaybeScope(scope.Root); owned = fence != null; }
                    if (heldFences != null && fence == null)
                        using (var unexpected = MaybeScope(scope.Root)) if (unexpected != null) throw Stop("EmergencyPreviewChanged");
                    if (scope.Key == "game")
                    {
                        if (fence == null) throw Stop("EmergencyGameRootUnavailable");
                        foreach (var name in Names) AddFile(plan, scope, fence, name, skipData);
                        var scanned = 0;
                        foreach (var childIo in Directory.EnumerateFileSystemEntries(WindowsPath.ForIo(scope.Root), "*", SearchOption.TopDirectoryOnly))
                        {
                            if (++scanned > MaxFiles + MaxDirectories) throw Stop("EmergencyRootInventoryLimitExceeded");
                            var path = WindowsPath.FromIo(childIo); var name = WindowsPath.Leaf(path); var attributes = File.GetAttributes(childIo);
                            if (RecoveryFile.IsMatch(name))
                            {
                                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
                                if ((attributes & FileAttributes.Directory) != 0) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                                AddFile(plan, scope, fence, name, skipData);
                            }
                            else if (String.Equals(name, UpdateWorkPath.Root, StringComparison.OrdinalIgnoreCase))
                            {
                                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
                                if ((attributes & FileAttributes.Directory) == 0) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                                WorkContainer(plan, scope, fence, skipData);
                            }
                            else if (RecoveryDirectory.IsMatch(name))
                            {
                                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
                                if ((attributes & FileAttributes.Directory) == 0) throw new ApplySafetyException(ApplyFailure.UnsafePath);
                                Tree(plan, scope, fence, name, 1, skipData);
                            }
                        }
                    }
                    else
                    {
                        if (fence != null && DirectoryPresent(fence, scope.Prefix)) Tree(plan, scope, fence, scope.Prefix, 1, skipData);
                        AddFile(plan, scope, fence, scope.Prefix + "/pending.json.lock", skipData);
                    }
                }
                finally { if (owned) fence?.Dispose(); }
            }
            if (plan.Files.Select(row => row.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Files.Count) throw Stop("EmergencyAllowlistInvalid");
            return plan;
        }
        private static string ReceiptStatus(Plan plan)
        {
            var observed = plan.Files.Single(row => row.Scope == "game" && row.Relative == InstallReceipt.Path);
            if (!observed.Existed) return "missing; ownership unverified";
            if (observed.Bytes > 16384) return "damaged or unsupported; ownership unverified";
            using (var game = new WindowsFileFence(plan.Scopes[0].Root)) using (var file = game.OpenFile(InstallReceipt.Path))
            {
                if (file.Identity.Text != observed.Identity || file.Hash() != observed.Hash) throw Stop("EmergencyPreviewChanged");
                try { var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384));
                    return receipt.Channels != null && receipt.Channels.Root == game.RootIdentity.Text ? "supported receipt parsed; file ownership not reauthenticated" : "receipt root unverified; ownership unverified";
                } catch (Exception error) when (error is ApplySafetyException || error is System.Text.DecoderFallbackException || error is InvalidDataException) { return "damaged or unsupported; ownership unverified"; }
            }
        }
        private static void SamePlan(Plan preview, Plan actual, bool currentCreated)
        {
            var prior = preview.Files.Where(row => !row.Lock).ToDictionary(row => row.Target, StringComparer.OrdinalIgnoreCase);
            var after = actual.Files.Where(row => !row.Lock).ToDictionary(row => row.Target, StringComparer.OrdinalIgnoreCase);
            if (prior.Count != after.Count || prior.Any(row => !after.TryGetValue(row.Key, out var now) || row.Value.Existed != now.Existed)) throw Stop("EmergencyPreviewChanged");
            var directories = preview.Directories.ToDictionary(row => row.Target, StringComparer.OrdinalIgnoreCase);
            foreach (var row in actual.Directories)
                if (directories.TryGetValue(row.Target, out var before)) { if (row.Identity != before.Identity) throw Stop("EmergencyPreviewChanged"); }
                else if (!currentCreated || row.Scope != "v5" || row.Relative != preview.Scopes.Single(s => s.Key == "v5").Prefix) throw Stop("EmergencyPreviewChanged");
            if (directories.Keys.Any(path => !actual.Directories.Any(row => row.Target.Equals(path, StringComparison.OrdinalIgnoreCase)))) throw Stop("EmergencyPreviewChanged");
        }
        private static void CheckLock(Entry observed, WindowsFileFence.FileLease? file, bool created)
        {
            if (observed.Existed ? file == null || file.Identity.Text != observed.Identity || file.Stream.Length != observed.Bytes || file.Hash() != observed.Hash : file != null && !created) throw Stop("EmergencyLockPreviewChanged");
        }
        private static string[] Images(WindowsFileFence game)
        { return new[] { game.Full("AstralParty_INT.exe"), game.Full(OrdinaryInstallRecovery.LegacyLauncher), game.Full(OrdinaryInstallRecovery.LegacyHelper) }; }
        private static void AbsentDirectory(WindowsFileFence fence, string relative)
        {
            try { fence.DirectoryIdentity(relative); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return; }
            throw Stop("EmergencyRecoveryReappeared");
        }
        internal static int Run(NativeMaintenanceBundle bundle, string? steamRoot, uint account, bool whatIf, Func<string, string?> consent, Action<string> output)
        {
            ShortcutRecovery? removedShortcut = null; var phase = "Preflight"; var deleted = 0; var deletedDirectories = 0; string? lastTarget = null; Plan? preview = null;
            var held = new List<Held>(); var fences = new Dictionary<string, WindowsFileFence>(StringComparer.Ordinal); NativeMaintenance.InstallLock? installLock = null; var legacyLocks = new Dictionary<string, WindowsFileFence.FileLease?>(StringComparer.Ordinal);
            try
            {
                bundle.AssertRoot(); preview = Observe(bundle.GameRoot); var receipt = ReceiptStatus(preview);
                var removeShortcut = false; Exception? shortcutFailure = null;
                if (steamRoot != null) try { if (account == 0) account = NativeSteamMaintenance.ResolveAccount(steamRoot, 0);
                    NativeSteamMaintenance.AssertStopped(steamRoot, "UninstallPreflight"); removeShortcut = NativeSteamMaintenance.PlanRemove(steamRoot, account, bundle.GameRoot);
                } catch (Exception error) { shortcutFailure = error; }
                output("영구 삭제 대상 / Irreversible emergency deletion: " + bundle.GameRoot); output("설치 기록 / Receipt: " + receipt);
                foreach (var row in preview.Files) output(row.Existed ? "  " + row.Target + " | bytes=" + row.Bytes.ToString(CultureInfo.InvariantCulture) + " | SHA256=" + row.Hash + " | identity=" + row.Identity + " | 소유 미검증 / ownership unverified" : "  없음 / absent: " + row.Target);
                foreach (var row in preview.Directories) output("  폴더 삭제 / Remove directory: " + row.Target + " | identity=" + row.Identity);
                foreach (var scope in preview.Scopes.Where(s => s.Key != "game")) output("  선택한 게임의 복구 백업 삭제 / Selected-game recovery backups: " + scope.Root + "\\" + scope.Prefix);
                output(removeShortcut ? "Steam 라이브러리에서 일치하는 Astral Party - Mod 항목만 제거합니다. / Remove only the exactly matching Astral Party - Mod shortcut." : "Steam 라이브러리 항목은 보존합니다. / Steam shortcut retained or absent. " + (shortcutFailure == null ? "" : shortcutFailure.GetType().Name));
                output("위 목록과 선택한 게임의 v3/v4/v5 복구 백업을 영구 삭제합니다. BetterAstralParty 파일의 백업·격리 복사본은 만들지 않으며, 중단되면 이미 삭제한 파일은 복원하지 않습니다. 공용 로더·다른 모드·추가 파일·다른 게임의 백업·전역 진단 ZIP은 보존합니다. / Permanently delete the listed files and selected-game v3/v4/v5 recovery backups. No BetterAstralParty payload/recovery copies; partial deletion cannot be rolled back. Shared loader, other mods, extra files, other game backups and global diagnostic ZIPs remain unchanged.");
                output("Steam의 공용 라이브러리 항목·표지 정보는 기존 안전한 롤백 절차를 사용하므로 해당 정보의 백업이 별도로 남을 수 있습니다. / Shared Steam shortcut/artwork metadata uses the existing safe rollback procedure; its separate metadata backups may remain.");
                if (whatIf) { output("WhatIf: 미리보기만 완료했습니다. / Preview only; nothing changed."); return 0; }
                if (!preview.Files.Any(row => row.Existed) && preview.Directories.Count == 0 && !removeShortcut) { output("삭제할 파일·확인된 Steam 라이브러리 항목이 없습니다. / No files or verified shortcut to delete."); return 0; }
                if (consent("표시한 파일과 복구 백업을 복원 불가능하게 영구 삭제할까요? / Permanently delete every listed unverified file and recovery backup without rollback? [y/N]") != "y") { output("취소했습니다. 아무것도 변경하지 않았습니다. / Cancelled; nothing changed."); return 0; }
                SetupHost.RecordInstallerStage("Uninstall", "Begin"); phase = "Processes"; NativeSteamMaintenance.AssertGameStopped(bundle.GameRoot);
                phase = "Locks"; installLock = NativeMaintenance.AcquireInstallLock(bundle);
                var game = new WindowsFileFence(bundle.GameRoot); fences.Add("game", game);
                using (var updateLock = Maybe(game, UpdateLock, true) ?? game.OpenFile(UpdateLock, true, true))
                using (var processes = WindowsProcessGuard.Acquire(game, new[] { "AstralParty_INT.exe" }, allowMissing: true))
                {
                    if (game.RootIdentity.Text != bundle.RootIdentity) throw Stop("EmergencyRootChanged");
                    foreach (var scope in preview.Scopes.Where(s => s.Key != "game")) { var fence = MaybeScope(scope.Root); if (fence != null) fences.Add(scope.Key, fence); }
                    // 1.3.x (v4) and older (v3) journal locks are legacy; only the v5 lock is this installer's own.
                    foreach (var key in new[] { "v4", "v3" }) { var scope = preview.Scopes.Single(s => s.Key == key); legacyLocks[key] = fences.TryGetValue(key, out var oldFence) ? Maybe(oldFence, scope.Prefix + "/pending.json.lock", true) : null; }
                    CheckLock(preview.Files.Single(r => r.Scope == "game" && r.Lock), updateLock, true); CheckLock(preview.Files.Single(r => r.Scope == "v5" && r.Lock), installLock.File, true);
                    foreach (var key in new[] { "v4", "v3" }) CheckLock(preview.Files.Single(r => r.Scope == key && r.Lock), legacyLocks[key], false);
                    phase = "Capture"; var actual = Observe(bundle.GameRoot, true, fences); var v5Prefix = preview.Scopes.Single(s => s.Key == "v5").Prefix;
                    var currentCreated = !preview.Directories.Any(r => r.Scope == "v5" && r.Relative == v5Prefix); SamePlan(preview, actual, currentCreated);
                    foreach (var row in preview.Files.Where(r => !r.Lock))
                    {
                        WindowsFileFence.FileLease? file = null; if (fences.TryGetValue(row.Scope, out var fence)) file = Maybe(fence, row.Relative, true);
                        held.Add(new Held { Entry = row, File = file });
                        if (row.Existed ? file == null || file.Stream.Length != row.Bytes || file.Identity.Text != row.Identity || file.Hash() != row.Hash : file != null) throw Stop("EmergencyPreviewChanged");
                    }
                    SamePlan(preview, Observe(bundle.GameRoot, true, fences), currentCreated);
                    bundle.AssertRoot(); processes.Recheck(); NativeSteamMaintenance.AssertGameStopped(bundle.GameRoot); WindowsProcessGuard.AssertNoRunning(Images(game)); Hit("BeforeFirstDelete");
                    SamePlan(preview, Observe(bundle.GameRoot, true, fences), currentCreated); phase = "Delete";
                    foreach (var item in held.Where(h => h.File != null))
                    {
                        updateLock.Recheck(); installLock.File.Recheck(); foreach (var legacy in legacyLocks.Values) legacy?.Recheck(); processes.Recheck(); WindowsProcessGuard.AssertNoRunning(Images(game)); lastTarget = item.Entry.Target; Hit("BeforeDelete:" + item.Entry.Relative);
                        if (item.File!.Hash() != item.Entry.Hash) throw Stop("EmergencyTargetChanged"); item.File.RemoveOrdinaryRecoveryTarget(item.Entry.Relative); item.Dispose(); deleted++;
                        output("  삭제 / Deleted: " + lastTarget + " | SHA256=" + item.Entry.Hash + " | identity=" + item.Entry.Identity); Hit("Deleted:" + item.Entry.Relative);
                    }
                    phase = "Shortcut"; if (removeShortcut) { NativeSteamMaintenance.AssertStopped(steamRoot!, "UninstallPreflight"); if (!NativeSteamMaintenance.PlanRemove(steamRoot!, account, bundle.GameRoot)) throw Stop("EmergencyShortcutPreviewChanged"); removedShortcut = NativeSteamMaintenance.Remove(steamRoot!, account, bundle.GameRoot, output); }
                    phase = "RecoveryDirectories";
                    foreach (var row in actual.Directories.OrderByDescending(r => r.Relative.Count(c => c == '/')).ThenBy(r => r.Target, StringComparer.OrdinalIgnoreCase))
                    {
                        updateLock.Recheck(); processes.Recheck(); WindowsProcessGuard.AssertNoRunning(Images(game)); lastTarget = row.Target;
                        if (row.Scope != "game" && row.Relative == preview.Scopes.Single(s => s.Key == row.Scope).Prefix)
                        {
                            if (row.Scope == "v5") { var ownLock = installLock ?? throw Stop("EmergencyInstallLockLost"); ownLock.File.RemoveOrdinaryRecoveryTarget("pending.json.lock"); ownLock.Dispose(); installLock = null; deleted++; Hit("InstallLockReleased"); }
                            else if (legacyLocks.TryGetValue(row.Scope, out var legacy) && legacy != null) { legacy.RemoveOrdinaryRecoveryTarget(row.Relative + "/pending.json.lock"); legacy.Dispose(); legacyLocks[row.Scope] = null; deleted++; Hit("LegacyLockReleased"); }
                        }
                        fences[row.Scope].RemoveEmptyRecoveryDirectory(row.Relative, row.Identity); deletedDirectories++; output("  폴더 삭제 / Directory deleted: " + row.Target); Hit("DirectoryDeleted:" + row.Relative);
                    }
                    phase = "FinalCheck";
                    foreach (var row in preview.Files.Where(r => !r.Lock))
                    {
                        if (fences.TryGetValue(row.Scope, out var fence)) {
                            using (var appeared = Maybe(fence, row.Relative)) if (appeared != null) throw Stop("EmergencyTargetReappeared");
                        } else {
                            using (var unexpected = MaybeScope(preview.Scopes.Single(s => s.Key == row.Scope).Root)) if (unexpected != null) throw Stop("EmergencyRecoveryReappeared");
                        }
                    }
                    foreach (var scope in preview.Scopes.Where(s => s.Key != "game"))
                        if (fences.TryGetValue(scope.Key, out var fence)) AbsentDirectory(fence, scope.Prefix);
                        else using (var unexpected = MaybeScope(scope.Root)) if (unexpected != null) throw Stop("EmergencyRecoveryReappeared");
                    foreach (var row in actual.Directories.Where(r => r.Scope == "game"))
                        AbsentDirectory(game, row.Relative);
                    processes.Recheck(); updateLock.Recheck(); lastTarget = game.Full(UpdateLock); updateLock.RemoveOrdinaryRecoveryTarget(UpdateLock); deleted++; Hit("Completed");
                }
                SetupHost.RecordInstallerStage("Uninstall", "Completed"); output("긴급 영구 제거를 마쳤습니다. / Irreversible emergency removal completed. deletedFiles=" + deleted + "; deletedDirectories=" + deletedDirectories + "; no BetterAstralParty payload/recovery copies created; protected Steam metadata backups may remain."); return 0;
            }
            catch (Exception error)
            {
                error.Data["BapEmergencyPhase"] = phase; error.Data["BapInstallStage"] = "Emergency " + phase; error.Data["BapEmergencyDeletedFiles"] = deleted; error.Data["BapEmergencyDeletedDirectories"] = deletedDirectories;
                error.Data["BapEmergencyObservedFiles"] = preview?.Files.Count(row => row.Existed) ?? 0; if (lastTarget != null) error.Data["BapEmergencyLastTarget"] = lastTarget;
                if (!error.Data.Contains("BapRecoveryReason")) error.Data["BapRecoveryReason"] = "Emergency" + phase + "Stopped";
                if (removedShortcut == null && error.Data["BapShortcutRecovery"] is ShortcutRecovery uncertain) removedShortcut = uncertain;
                if (removedShortcut != null) try { NativeSteamMaintenance.AssertStopped(steamRoot!, "ShortcutRollback"); NativeSteamMaintenance.Rollback(removedShortcut); } catch (Exception recoveryError) { error.Data["BapRollbackFailure"] = recoveryError; SetupHost.RecordInstallerFailure("Rollback", recoveryError); }
                output("긴급 제거 중단. 삭제한 파일은 복원하지 않습니다. / Emergency removal stopped; deleted files are not restored. deletedFiles=" + deleted + "; deletedDirectories=" + deletedDirectories + "; phase=" + phase);
                if (!whatIf) SetupHost.RecordInstallerFailure("Uninstall", error); throw;
            }
            finally { foreach (var item in held) item.Dispose(); foreach (var legacy in legacyLocks.Values) legacy?.Dispose(); installLock?.Dispose(); foreach (var fence in fences.Values) fence.Dispose(); }
        }
    }
}
