using System;
using System.IO;
using System.Runtime.InteropServices;
using BetterAstralParty.Updating;

// Install/uninstall/diagnostics host behind Install.cmd, Uninstall.cmd, Force-Uninstall.cmd,
// Create-Diagnostics.cmd and Open-Logs.cmd. It never launches the game, downloads, applies
// automatic updates or terminates processes; the game starts from BetterAstralParty-Mod.cmd.
internal static class SetupHost
{
    public const int InstallBundleProtocol = 1;
    // The host's own component version, independent of the release version, so an unchanged host
    // stays byte-identical across releases (a stable hash keeps antivirus clearance and reputation).
    // Bump it whenever this host or anything it compiles changes.
    public const string SetupVersion = "1.4.0";
    internal const string Image = "BetterAstralParty-Setup.exe";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0 && args[0] == "--diagnostics") return BetterAstralParty.Installation.NativeDiagnosticEntry.Run(args);
        if (args.Length != 0) return BetterAstralParty.Installation.NativeMaintenanceEntry.Run(args);
        MessageBoxW(IntPtr.Zero, "Install.cmd로 설치하고 Steam의 \"Astral Party - Mod\"로 실행하세요.\r\nRun Install.cmd, then start \"Astral Party - Mod\" from Steam.", "BetterAstralParty", 0x40);
        return 2;
    }

    public static IDisposable GuardMaintenance(string root) { return new UpdateLaunchGate.Lease(root); }
    public static object[] ReadMaintenanceProcess(int pid) {
        if(pid<=0)throw new ArgumentOutOfRangeException(nameof(pid));
        using(var process=WindowsProcessGuard.Observe((uint)pid, allowExited: true))
            return new object[]{process.Exited,process.Image,process.Creation,process.Session};
    }
    public static string ReadInstallerState(string root)
    {
        using (var fence = new WindowsFileFence(root)) {
            var water = UpdateTransaction.ReadHighWater(fence, out var hash);
            var version = water == null ? "" : UpdateVersion.Parse(water).Tag;
            return "BetterAstralParty.InstallerState/v1\nroot=" + fence.RootIdentity.Text + "\nversion=" + version + "\nwater-sha=" + (hash ?? "") + "\n";
        }
    }
    public static void ValidateMaintenancePaths(string root,string journalRoot) {InstallProof.ValidateMaintenancePaths(root,journalRoot);}
    public static void ValidateInstalledHistory(string root) {
        using(var fence=new WindowsFileFence(root))using(var file=fence.OpenFile(InstallReceipt.Path,false))
        using(var history=UpdateTransaction.ValidateHistory(fence,InstallReceipt.Parse(UpdateTicket.Read(file,16384)))) { }
    }
    public static void CheckUpdateState(string root) {using(var fence=new WindowsFileFence(root)) UpdateLaunchGate.Check(fence);}

    private sealed class InstallerDiagnosticLease : IDisposable {
        private readonly BetterAstralParty.Observability.MinimalEventLog _sink;
        internal InstallerDiagnosticLease(string root,string version) {
            _sink=BetterAstralParty.Observability.DiagnosticOwner.StartInstaller(root,version,typeof(SetupHost).Module.ModuleVersionId.ToString("N"));
            if(!BetterAstralParty.Observability.DiagnosticHub.Bind(_sink)) {_sink.Dispose();throw new InvalidOperationException("Diagnostic owner already bound.");}
        }
        public void Dispose() {_sink.Stop(BetterAstralParty.Observability.DiagnosticEndReason.OwnerDisposed);BetterAstralParty.Observability.DiagnosticHub.Unbind(_sink);}
    }
    public static IDisposable BeginInstallerDiagnostics(string root,string version) {return new InstallerDiagnosticLease(root,version);}
    public static void RecordInstallerStage(string phase,string outcome) {
        BetterAstralParty.Observability.DiagnosticPhase p;BetterAstralParty.Observability.DiagnosticOutcome o;
        if(!Enum.TryParse(phase,out p) || !Enum.IsDefined(typeof(BetterAstralParty.Observability.DiagnosticPhase),p) || !Enum.TryParse(outcome,out o) || !Enum.IsDefined(typeof(BetterAstralParty.Observability.DiagnosticOutcome),o))return;
        BetterAstralParty.Observability.DiagnosticHub.Stage(BetterAstralParty.Observability.DiagnosticFeature.Installer,p,o);
    }
    public static void RecordInstallerFailure(string phase,Exception error) {
        BetterAstralParty.Observability.DiagnosticPhase p;
        if(!Enum.TryParse(phase,out p) || !Enum.IsDefined(typeof(BetterAstralParty.Observability.DiagnosticPhase),p))return;
        var cause=error;var code=BetterAstralParty.Observability.DiagnosticCode.Unknown;var native=0;var apply=0;var validation=0;
        for(var depth=0;depth<8;depth++) {
            if(cause is ApplySafetyException safety) {
                native=safety.NativeError;apply=(int)safety.Failure;
                var reason=safety.Data["BapRecoveryReason"] as string;
                code=reason=="MaintenancePathTooLong"?BetterAstralParty.Observability.DiagnosticCode.LimitExceeded
                    : reason=="HighWaterRootMismatch" || reason=="HighWaterRepositoryMismatch"?BetterAstralParty.Observability.DiagnosticCode.WrongTarget
                    : reason=="HighWaterProofMissing"?BetterAstralParty.Observability.DiagnosticCode.Prerequisite
                    : reason!=null && reason.StartsWith("HighWater",StringComparison.Ordinal)?BetterAstralParty.Observability.DiagnosticCode.Integrity
                    : safety.Failure==ApplyFailure.CompatibilityRequired?BetterAstralParty.Observability.DiagnosticCode.CompatibilityBlocked
                    : BetterAstralParty.Observability.DiagnosticHub.CodeForStatus(safety.Failure.ToString());break;
            }
            if(cause is UpdateValidationException invalid) {validation=(int)invalid.Failure;code=BetterAstralParty.Observability.DiagnosticHub.CodeForStatus(invalid.Failure.ToString());break;}
            if(cause.InnerException==null)break;cause=cause.InnerException;
        }
        BetterAstralParty.Observability.DiagnosticHub.Failure(BetterAstralParty.Observability.DiagnosticFeature.Installer,p,code,cause,native,validation,applyCode:apply);
    }

    public static void ValidateInstalledReceipt(byte[] bytes) {InstallReceipt.Parse(bytes);}
    public static void ValidateWholeInputs(string root,string package,string version) {InstallProof.ValidateInputs(root,package,version);}
    public static string PrepareWholeReceipt(string root,string package,string version,string loaderBefore) {return InstallProof.PrepareReceipt(root,package,version,loaderBefore);}
    public static byte[]? PrepareHighWater(string root,string package,string version) {return InstallProof.HighWater(root,package,version);}
    // Ordinary install/removal recovery runs under the update lock so no in-game update can overlap.
    public static IDisposable GuardOrdinaryRecovery(string root) {
        try {return new UpdateLaunchGate.Lease(root);}
        catch(Exception error) {RecordInstallerFailure("Recovery",error);throw;}
    }
    public static void RestoreOrdinaryInstall(string root,string journalRoot,string journalHash,string[] allowedTargets,string expectedCurrent,bool completeOnly,string rootIdentity) {
        RecordInstallerStage("Recovery","Begin");
        try {OrdinaryInstallRecovery.Run(root,journalRoot,journalHash,allowedTargets,expectedCurrent,completeOnly,rootIdentity);RecordInstallerStage("Recovery","Completed");}
        catch(Exception error) {RecordInstallerFailure("Recovery",error);throw;}
    }
    public static IDisposable BeginOwnedRemoval(string root,string journalRoot,string journalHash,string[] allowedTargets,string rootIdentity,bool allowLoader) {
        return new OrdinaryInstallRecovery.OwnedRemovalLease(root,journalRoot,journalHash,allowedTargets,rootIdentity,allowLoader);
    }
    // Finishes or rolls back an update transaction a game session left unfinished (game must be closed).
    public static string RecoverPendingUpdate(string root) {
        var trust=UpdateTrust.Production();
        using(var fence=new WindowsFileFence(root)) {
            var pending=UpdateRecovery.PendingWork(fence); if(pending==null) return "none";
            var context=UpdateRecovery.RecoveryContext(fence,pending,trust);
            var result=UpdateTransaction.Recover(fence,pending.Work,trust,context);
            var operation=UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath);
            if(operation!=null){UpdateFaults.LatchInterrupted(fence,operation);UpdateFaults.Complete(fence,operation);}
            else UpdateFaults.Fail(fence,context,FaultPhase.Recovery,FaultReason.Interrupted,pending.Stage);
            return result.ToString();
        }
    }
    public static bool HasPendingUpdate(string root) { using(var fence=new WindowsFileFence(root)) return UpdateRecovery.PendingWork(fence)!=null; }

    internal static string LaunchFailure(string phase, Exception error)
    {
        var failure = error.GetBaseException();
        var type = failure.GetType().FullName ?? "Unknown";
        var allowed = new[] { "System.IO.FileNotFoundException", "System.IO.DirectoryNotFoundException", "System.UnauthorizedAccessException", "System.IO.IOException", "System.ArgumentException", "System.InvalidOperationException", "System.NotSupportedException", "System.ComponentModel.Win32Exception", "System.Security.SecurityException" };
        if (Array.IndexOf(allowed, type) < 0) type = "Unknown";
        return "LaunchFailure: schema=1; phase=" + phase + "; errorType=" + type + "; hresult=0x" + failure.HResult.ToString("X8") + "\r\n" + error;
    }
    internal static string? SaveFailure(string details, bool observation = false)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!String.IsNullOrEmpty(profile)) details = System.Text.RegularExpressions.Regex.Replace(details,
            System.Text.RegularExpressions.Regex.Escape(profile), "%USERPROFILE%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (string directory in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BetterAstralParty\Logs"),
            Path.Combine(Path.GetTempPath(), "BetterAstralParty") })
        {
            try {
                Directory.CreateDirectory(WindowsPath.ForIo(directory));
                string path = Path.Combine(directory, "Launch-" + Guid.NewGuid().ToString("N") + ".log");
                File.WriteAllText(WindowsPath.ForIo(path), (observation ? "BAP-LAUNCH-OBSERVATION" : "BAP-LAUNCH-FAILED") + "\r\nTime: " + DateTimeOffset.Now.ToString("o") + "\r\n" + details);
                return path;
            } catch { /* A failed log write must not prevent the error report. */ }
        }
        return null;
    }
}
