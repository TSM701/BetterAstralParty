using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class SteamLauncher
{
    [STAThread]
    private static int Main()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        int exitCode = 1;
        try
        {
            string details;
            exitCode = Run(root, out details);
            if (exitCode != 0) throw new Exception("PowerShell exit code: " + exitCode + "\r\n" + details);
            return exitCode;
        }
        catch (Exception error)
        {
            string? path = SaveFailure(error.ToString());
            MessageBox.Show("모드 실행에 실패했습니다. / Mod launch failed.\r\n" +
                (path == null ? error.Message : "로그 / Debug log: " + path), "BetterAstralParty");
            return exitCode == 0 ? 1 : exitCode;
        }
    }

    public const int InstallBundleProtocol = 1;
    public static IDisposable GuardModLaunch(string root)
    {
        // Recognizable old bootstrap/uninstall paths use the launch API.
        // Scripts which bypass the installed gate cannot be intercepted here.
        bool legacy = false;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) {
            if (!String.Equals(args[i], "-File", StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileName(args[i + 1]);
            if (String.Equals(name, "install-bootstrap.ps1", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "install.ps1", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "uninstall.ps1", StringComparison.OrdinalIgnoreCase)) legacy = true;
        }
        if (legacy) throw new InvalidOperationException(
            "구형 설치·제거 도구가 현재 설치를 변경하려고 합니다. 최신 전체 묶음의 Install.cmd / Uninstall.cmd를 사용하세요. / " +
            "Legacy maintenance tool blocked. Use Install.cmd / Uninstall.cmd from the current complete package.");
        return GuardMaintenance(root);
    }
    public static IDisposable GuardMaintenance(string root) { return new BetterAstralParty.Updating.UpdateLaunchGate.Lease(root); }
    public static string ReadInstallerState(string root)
    {
        using (var fence = new BetterAstralParty.Updating.WindowsFileFence(root)) {
            var water = BetterAstralParty.Updating.UpdateTransaction.ReadHighWater(fence, out var hash);
            var version = water == null ? "" : BetterAstralParty.Updating.UpdateVersion.Parse(water).Tag;
            return "BetterAstralParty.InstallerState/v1\nroot=" + fence.RootIdentity.Text + "\nversion=" + version + "\nwater-sha=" + (hash ?? "") + "\n";
        }
    }
    public static string? CheckOwnedHelper(string root)
    {
        using (var fence = new BetterAstralParty.Updating.WindowsFileFence(root)) {
            BetterAstralParty.Updating.UpdateLaunchGate.Check(fence);
            string? image;
            try { image = BetterAstralParty.Updating.HelperDelivery.Fingerprint(fence, BetterAstralParty.Updating.UpdateTrust.Production()); }
            catch (BetterAstralParty.Updating.UpdateValidationException) {
                BetterAstralParty.Updating.UpdateLaunchGate.Check(fence);
                return null;
            }
            catch (BetterAstralParty.Updating.ApplySafetyException error) {
                if (error.Failure != BetterAstralParty.Updating.ApplyFailure.RecoveryRequired &&
                    error.Failure != BetterAstralParty.Updating.ApplyFailure.InvalidState) throw;
                BetterAstralParty.Updating.UpdateLaunchGate.Check(fence);
                return null;
            }
            if (image == null) return null;
            using (var receipt = fence.OpenFile(BetterAstralParty.Updating.HelperDelivery.Receipt))
                return image + ":" + receipt.Hash();
        }
    }
    public static void CheckUpdateState(string root) {using(var fence=new BetterAstralParty.Updating.WindowsFileFence(root)) BetterAstralParty.Updating.UpdateLaunchGate.Check(fence);}

    public static string ValidateRemovedHelperEvidence(string root,byte[] image,byte[] receipt)
    {
        using(var fence=new BetterAstralParty.Updating.WindowsFileFence(root)) {
            BetterAstralParty.Updating.UpdateLaunchGate.Check(fence);
            return BetterAstralParty.Updating.HelperDelivery.RemovedEvidence(fence,BetterAstralParty.Updating.UpdateTrust.Production(),image,receipt);
        }
    }

    public static string ValidateHelperBootstrap(string bundleRoot, string version, string expectedHash)
    {
        return BetterAstralParty.Installation.InstallHelperBootstrap.Validate(bundleRoot, version, expectedHash);
    }
    public static int InstallHelperBootstrap(string root, string bundleRoot, string version, string expectedHash)
    {
        return BetterAstralParty.Installation.InstallHelperBootstrap.Install(root, bundleRoot, version, expectedHash);
    }

    private sealed class InstallerDiagnosticLease : IDisposable {
        private readonly BetterAstralParty.Observability.MinimalEventLog _sink;
        internal InstallerDiagnosticLease(string root,string version) {
            _sink=BetterAstralParty.Observability.DiagnosticOwner.StartInstaller(root,version,typeof(SteamLauncher).Module.ModuleVersionId.ToString("N"));
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
        BetterAstralParty.Observability.DiagnosticHub.Failure(BetterAstralParty.Observability.DiagnosticFeature.Installer,p,BetterAstralParty.Observability.DiagnosticCode.Unknown,error);
    }

    public static void ValidateInstalledReceipt(byte[] bytes) {BetterAstralParty.Updating.InstallReceipt.Parse(bytes);}
    public static void ValidateWholeInputs(string root,string package,string version) {BetterAstralParty.Updating.WholeInstallBridge.ValidateInputs(root,package,version);}
    public static string ValidateWholeBridge(string root,string package,string version) {return BetterAstralParty.Updating.WholeInstallBridge.Validate(root,package,version);}
    public static string PrepareWholeReceipt(string root,string package,string version,string observation,string loaderBefore) {return BetterAstralParty.Updating.WholeInstallBridge.PrepareReceipt(root,package,version,observation,loaderBefore);}
    public static IDisposable GuardWholeRecovery(string root,string package,string version) {return BetterAstralParty.Updating.WholeInstallBridge.GuardRecovery(root,package,version);}
    public static IDisposable BeginWholeInstall(string root,string package,string version,string observation,string receipt) {return BetterAstralParty.Updating.WholeInstallBridge.BeginInstall(root,package,version,observation,receipt);}
    public static void RecoverWholeHelper(string root,string package,string version) {BetterAstralParty.Updating.WholeInstallBridge.Recover(root,package,version);}
    public static void InstallWholeHelper(string root,string package,string version,string observation,string receiptHash) {BetterAstralParty.Updating.WholeInstallBridge.Install(root,package,version,observation,receiptHash);}
    public static void RestoreOrdinaryInstall(string root,string journalRoot,string journalHash,string[] allowedTargets,string expectedCurrent,bool completeOnly,string rootIdentity) {BetterAstralParty.Updating.OrdinaryInstallRecovery.Run(root,journalRoot,journalHash,allowedTargets,expectedCurrent,completeOnly,rootIdentity);}

    internal static int Run(string root, out string details)
    {
        var start = new ProcessStartInfo {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "BetterAstralParty-Steam.ps1") + "\"",
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var launcher = Process.GetCurrentProcess()) {
            start.EnvironmentVariables["BAP_UPDATER_LAUNCHER_PID"] = launcher.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.EnvironmentVariables["BAP_UPDATER_LAUNCHER_CREATION"] = launcher.StartTime.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.EnvironmentVariables["BAP_UPDATER_LAUNCHER_SESSION"] = launcher.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        using (var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell could not start")) {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            details = stderr.Result + "\r\n" + stdout.Result;
            return process.ExitCode;
        }
    }

    internal static string? SaveFailure(string details)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!String.IsNullOrEmpty(profile)) details = System.Text.RegularExpressions.Regex.Replace(details,
            System.Text.RegularExpressions.Regex.Escape(profile), "%USERPROFILE%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (string directory in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BetterAstralParty\Logs"),
            Path.Combine(Path.GetTempPath(), "BetterAstralParty") })
        {
            try {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "Launch-" + Guid.NewGuid().ToString("N") + ".log");
                File.WriteAllText(path, "BAP-LAUNCH-FAILED\r\nTime: " + DateTimeOffset.Now.ToString("o") + "\r\n" + details);
                return path;
            } catch { /* A failed log write must not prevent the error dialog. */ }
        }
        return null;
    }
}
