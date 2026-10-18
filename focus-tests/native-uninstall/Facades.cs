using System.Text;
using System.Text.Json;
using BetterAstralParty.Updating;

internal static class SetupHost
{
    internal static int History, Stages, Failures;
    internal static void ValidateInstalledHistory(string game) { History++; }
    internal static void RecordInstallerStage(string phase, string outcome) { Stages++; }
    internal static void RecordInstallerFailure(string phase, Exception error) { Failures++; }
}
namespace BetterAstralParty.Installation
{
    // Authentication and Steam actions are inert facades; actual file fences/removal/recovery remain production code.
    internal sealed class NativeMaintenanceBundle
    {
        internal readonly string GameRoot, PackageRoot, RootIdentity;
        internal readonly string Version = "1.4.0";
        internal readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal);
        internal NativeMaintenanceBundle(string root)
        {
            GameRoot = root; PackageRoot = Path.GetDirectoryName(root)!;
            using var game = new WindowsFileFence(root); RootIdentity = game.RootIdentity.Text;
            foreach (var name in InstallReceipt.Owned) Expected[name] = UpdateTrust.Hash(Encoding.ASCII.GetBytes("incoming: " + name));
        }
        internal string? TargetHash(string relative)
        {
            using var game = new WindowsFileFence(GameRoot);
            try { using var file = game.OpenFile(relative); return file.Hash(); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
        }
        internal byte[]? ReadTarget(string relative, long maxBytes = 64 * 1024 * 1024)
        {
            using var game = new WindowsFileFence(GameRoot);
            try { using var file = game.OpenFile(relative); return UpdateTicket.Read(file, maxBytes); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
        }
        internal void AssertRoot() { using var game = new WindowsFileFence(GameRoot); if (game.RootIdentity.Text != RootIdentity) throw new Exception("Fixture root changed."); }
    }
    internal sealed class SteamSessionState { internal bool Restart; }
    internal sealed class ShortcutRecovery { internal string Backup = "inert shortcut backup"; }
    internal sealed class SteamProfileUnavailableException : InvalidOperationException
    {
        internal readonly string Reason;
        internal SteamProfileUnavailableException(string reason) : base(reason) { Reason = reason; }
    }
    internal static class NativeSteamMaintenance
    {
        internal static System.Action<System.Exception>? WaitingDiagnostic = null;
        internal static bool Shortcut;
        internal static int StopGames, StopSteams, Removes, Rollbacks, Resumes, AccountQueries, Plans;
        internal static Exception? RemoveFailure = null, AccountFailure = null, PlanFailure = null;
        internal static uint ResolveAccount(string root, uint requested) { AccountQueries++; if (AccountFailure != null) throw AccountFailure; return requested == 0 ? 1U : requested; }
        internal static bool PlanRemove(string root, uint account, string game, Action<Exception>? unavailable = null) {
            Plans++; if (AccountFailure != null) throw AccountFailure;
            if (PlanFailure != null) { if (unavailable == null) throw PlanFailure; unavailable(PlanFailure); return false; }
            return Shortcut;
        }
        internal static void WaitGameClosed(string game, Func<string, string?> consent, Action<string> output) { StopGames++; }
        internal static void StopSteam(string root, SteamSessionState state, Func<string, string?> consent, Action<string> output) { StopSteams++; state.Restart = true; }
        internal static void AssertGameStopped(string game) { }
        internal static void AssertStopped(string root, string phase) { }
        internal static ShortcutRecovery? Remove(string root, uint account, string game, Action<string> output)
        { Removes++; if (RemoveFailure != null) throw RemoveFailure; Shortcut = false; return new ShortcutRecovery(); }
        internal static void Rollback(ShortcutRecovery recovery) { Rollbacks++; Shortcut = true; }
        internal static void ResumeSteam(SteamSessionState state, Action<string> output) { if (state.Restart) Resumes++; }
    }
    internal static class NativeMaintenance
    {
        internal static bool Pending;
        internal static int Locks, Starts, Completes, Restores, PendingChecks;
        internal static Exception? CompletionFailure, RestoreFailure;
        internal sealed class Entry { internal string Target = "", Backup = ""; internal string? Hash; internal bool Existed; }
        internal sealed class Transaction
        {
            internal string JournalRoot = "", JournalPath = "", JournalHash = "", BackupDir = "";
            internal List<Entry> Entries = new();
        }
        internal sealed class InstallLock : IDisposable
        {
            private readonly WindowsFileFence _fence;
            internal readonly WindowsFileFence.FileLease File;
            internal readonly string Root;
            internal InstallLock(string root)
            {
                Root = root; Directory.CreateDirectory(root); _fence = new WindowsFileFence(root);
                try { File = _fence.OpenFile("pending.json.lock", true, true); }
                catch { _fence.Dispose(); throw; }
            }
            public void Dispose() { File.Dispose(); _fence.Dispose(); }
        }
        internal static string JournalRoot(string root) => Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "BetterAstralParty", "Backups", "v5", UpdateTrust.Hash(Encoding.UTF8.GetBytes(root.ToLowerInvariant())));
        internal static string JournalPath(string root) => Path.Combine(JournalRoot(root), "pending.json");
        internal static bool HasPending(NativeMaintenanceBundle bundle) => Pending || System.IO.File.Exists(JournalPath(bundle.GameRoot));
        internal static InstallLock AcquireInstallLock(NativeMaintenanceBundle bundle) { Locks++; return new InstallLock(JournalRoot(bundle.GameRoot)); }
        internal static bool RecoverPending(NativeMaintenanceBundle bundle, Func<string, string?> consent, Action<string> output, bool whatIf = false)
        { PendingChecks++; if (whatIf) return false; throw new InvalidOperationException("Inert pending recovery must be reviewed; no files changed."); }
        internal static void AssertTarget(string root, string target)
        {
            using var game = new WindowsFileFence(root);
            if (!target.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe fixture target.");
            game.Full(target[(root.Length + 1)..].Replace('\\', '/'));
        }
        internal static Transaction StartTransaction(NativeMaintenanceBundle bundle, string[] targets, long installBytes)
        {
            Starts++; if (targets.Length == 0) throw new Exception("Empty ordinary transaction.");
            var transaction = new Transaction { JournalRoot = JournalRoot(bundle.GameRoot), JournalPath = JournalPath(bundle.GameRoot) };
            transaction.BackupDir = Path.Combine(transaction.JournalRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(transaction.BackupDir);
            foreach (var target in targets.OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var entry = new Entry { Target = target, Backup = Path.Combine(transaction.BackupDir, transaction.Entries.Count + ".bin"), Existed = System.IO.File.Exists(target) };
                if (entry.Existed) { System.IO.File.Copy(target, entry.Backup, false); entry.Hash = UpdateTrust.Hash(System.IO.File.ReadAllBytes(entry.Backup)); }
                transaction.Entries.Add(entry);
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { GameRoot = bundle.GameRoot, Entries = transaction.Entries.Select(entry => new { entry.Target, entry.Existed, entry.Backup, entry.Hash }).ToArray(), transaction.BackupDir });
            System.IO.File.WriteAllBytes(transaction.JournalPath, bytes); transaction.JournalHash = UpdateTrust.Hash(bytes); return transaction;
        }
        internal static void CompleteTransaction(NativeMaintenanceBundle bundle, Transaction transaction, string[] targets)
        {
            Completes++; if (CompletionFailure != null) throw CompletionFailure;
            OrdinaryInstallRecovery.Run(bundle.GameRoot, transaction.JournalRoot, transaction.JournalHash, targets, null, true, bundle.RootIdentity);
        }
        internal static void RestoreTransaction(NativeMaintenanceBundle bundle, Transaction transaction, string[] targets, Dictionary<string, string?> expectedCurrent)
        {
            Restores++; if (RestoreFailure != null) throw RestoreFailure;
            OrdinaryInstallRecovery.Run(bundle.GameRoot, transaction.JournalRoot, transaction.JournalHash, targets, JsonSerializer.Serialize(expectedCurrent), false, bundle.RootIdentity);
        }
    }
}
