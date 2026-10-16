using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BetterAstralParty.Updating;

internal static class SteamLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0 && args[0] == "--diagnostics") return BetterAstralParty.Installation.NativeDiagnosticEntry.Run(args);
        if (args.Length != 0) return BetterAstralParty.Installation.NativeMaintenanceEntry.Run(args);
        string root = WindowsPath.FromIo(AppDomain.CurrentDomain.BaseDirectory);
        int exitCode = 1;
        try
        {
            string details;
            exitCode = Run(root, out details);
            if (exitCode != 0 && (details.IndexOf("phase=Compatibility;", StringComparison.Ordinal) >= 0 || details.IndexOf("phase=UpdateGate;", StringComparison.Ordinal) >= 0)) {
                SaveFailure(CaptureLaunchContext(root) + details);
                IDisposable? diagnostics = null;
                try { diagnostics = BeginInstallerDiagnostics(root, System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(SteamLauncher).Assembly.Location).ProductVersion ?? "unknown"); } catch { }
                using (diagnostics) LauncherRecoveryForm.Open(root);
                return 0;
            }
            if (exitCode != 0) throw new Exception("Launch exit code: " + exitCode + "\r\n" + details);
            return exitCode;
        }
        catch (Exception error)
        {
            string? path = SaveFailure(CaptureLaunchContext(root) + error.ToString());
            MessageBox.Show("모드 실행에 실패했습니다. / Mod launch failed.\r\n" +
                (path == null ? error.Message : "로그 / Debug log: " + path), "BetterAstralParty");
            return exitCode == 0 ? 1 : exitCode;
        }
    }

    public const int InstallBundleProtocol = 1;
    public static IDisposable GuardModLaunchVerified(string root,string? snapshotHash) {
        return new BetterAstralParty.Updating.UpdateLaunchGate.Lease(root, fence => {
            var assembly=typeof(SteamLauncher).Assembly;
            if(assembly.Location.Length!=0 && !String.Equals(WindowsPath.FromIo(assembly.Location),fence.Full("BetterAstralParty-Launcher.exe"),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installed launcher required.");
            if(snapshotHash!=null){
                if(!System.Text.RegularExpressions.Regex.IsMatch(snapshotHash,@"\A[0-9A-F]{64}\z"))throw new InvalidOperationException("Invalid launcher snapshot hash.");
                using(var launcher=fence.OpenLaunchImage("BetterAstralParty-Launcher.exe"))
                    if(launcher.Hash()!=snapshotHash)throw new InvalidOperationException("Launcher changed after snapshot capture.");
            }
            BetterAstralParty.Updating.InstallReceipt installed;
            using(var receipt=fence.OpenFile(BetterAstralParty.Updating.InstallReceipt.Path))
                installed=BetterAstralParty.Updating.InstallReceipt.Parse(BetterAstralParty.Updating.UpdateTicket.Read(receipt,16384));
            if(installed.Channels==null || installed.Channels.Root!=fence.RootIdentity.Text)
                throw new InvalidOperationException("Installed receipt belongs to another game root.");
            foreach(var path in new[]{BetterAstralParty.Updating.UpdateTrust.PluginPath,"BetterAstralParty-Launcher.exe"}) {
                using(var file=path=="BetterAstralParty-Launcher.exe" ? fence.OpenLaunchImage(path) : fence.OpenFile(path)) {
                    if(file.Hash()!=installed.Files[path])throw new InvalidOperationException("Installed file changed: "+path);
                    if(path==BetterAstralParty.Updating.UpdateTrust.PluginPath) {
                        var metadata=BetterAstralParty.Updating.InstalledPluginMetadata.Read(BetterAstralParty.Updating.UpdateTicket.Read(file,(int)BetterAstralParty.Updating.UpdateTrust.MaxFileBytes));
                        if(metadata.Version!=installed.Channels.ActiveVersion || metadata.UpdateProtocol!=4 || metadata.SettingsSchema!=1)
                            throw new InvalidOperationException("Installed plugin does not match the supported receipt.");
                    }
                }
            }
        });
    }
    public static IDisposable GuardMaintenance(string root) { return new BetterAstralParty.Updating.UpdateLaunchGate.Lease(root); }
    public static object[] ReadMaintenanceProcess(int pid) {
        if(pid<=0)throw new ArgumentOutOfRangeException(nameof(pid));
        using(var process=BetterAstralParty.Updating.WindowsProcessGuard.Observe((uint)pid, allowExited: true))
            return new object[]{process.Exited,process.Image,process.Creation,process.Session};
    }
    public static void EnableSharedLoader(string root, IDisposable guardedLaunch) {
        if(guardedLaunch is not BetterAstralParty.Updating.UpdateLaunchGate.Lease operation)
            throw new InvalidOperationException("A retained launch lease is required");
        BetterAstralParty.Updating.SharedLoaderConfiguration.Enable(root,operation);
    }
    public static string ReadInstallerState(string root)
    {
        using (var fence = new BetterAstralParty.Updating.WindowsFileFence(root)) {
            var water = BetterAstralParty.Updating.UpdateTransaction.ReadHighWater(fence, out var hash);
            var version = water == null ? "" : BetterAstralParty.Updating.UpdateVersion.Parse(water).Tag;
            return "BetterAstralParty.InstallerState/v1\nroot=" + fence.RootIdentity.Text + "\nversion=" + version + "\nwater-sha=" + (hash ?? "") + "\n";
        }
    }
    public static void ValidateMaintenancePaths(string root,string journalRoot) {BetterAstralParty.Updating.WholeInstallBridge.ValidateMaintenancePaths(root,journalRoot);}
    public static void ValidateInstalledHistory(string root) {
        using(var fence=new BetterAstralParty.Updating.WindowsFileFence(root))using(var file=fence.OpenFile(BetterAstralParty.Updating.InstallReceipt.Path,false))
        using(var history=BetterAstralParty.Updating.UpdateTransaction.ValidateHistory(fence,BetterAstralParty.Updating.InstallReceipt.Parse(BetterAstralParty.Updating.UpdateTicket.Read(file,16384)))) { }
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
        var cause=error;var code=BetterAstralParty.Observability.DiagnosticCode.Unknown;var native=0;var apply=0;var validation=0;
        for(var depth=0;depth<8;depth++) {
            if(cause is BetterAstralParty.Updating.ApplySafetyException safety) {
                native=safety.NativeError;apply=(int)safety.Failure;
                var reason=safety.Data["BapRecoveryReason"] as string;
                code=reason=="MaintenancePathTooLong"?BetterAstralParty.Observability.DiagnosticCode.LimitExceeded
                    : reason=="HighWaterRootMismatch" || reason=="HighWaterRepositoryMismatch"?BetterAstralParty.Observability.DiagnosticCode.WrongTarget
                    : reason=="HighWaterProofMissing" || reason=="LegacyFlatWorkPending"?BetterAstralParty.Observability.DiagnosticCode.Prerequisite
                    : reason!=null && reason.StartsWith("HighWater",StringComparison.Ordinal)?BetterAstralParty.Observability.DiagnosticCode.Integrity
                    : safety.Failure==BetterAstralParty.Updating.ApplyFailure.CompatibilityRequired?BetterAstralParty.Observability.DiagnosticCode.CompatibilityBlocked
                    : BetterAstralParty.Observability.DiagnosticHub.CodeForStatus(safety.Failure.ToString());break;
            }
            if(cause is BetterAstralParty.Updating.UpdateValidationException invalid) {validation=(int)invalid.Failure;code=BetterAstralParty.Observability.DiagnosticHub.CodeForStatus(invalid.Failure.ToString());break;}
            if(cause.InnerException==null)break;cause=cause.InnerException;
        }
        BetterAstralParty.Observability.DiagnosticHub.Failure(BetterAstralParty.Observability.DiagnosticFeature.Installer,p,code,cause,native,validation,applyCode:apply);
    }

    public static void ValidateInstalledReceipt(byte[] bytes) {BetterAstralParty.Updating.InstallReceipt.Parse(bytes);}
    public static void ValidatePartialRemoval(string root) {BetterAstralParty.Updating.WholeInstallBridge.ValidatePartialRemoval(root);}
    public static void ValidatePartialRemovalWithPackage(string root,string package,string version) {BetterAstralParty.Updating.WholeInstallBridge.ValidatePartialRemovalWithPackage(root,package,version);}
    public static string ValidateRemovedInstallEvidence(string root,string package,string version,byte[] receipt) {return BetterAstralParty.Updating.WholeInstallBridge.RemovedInstallEvidence(root,package,version,receipt);}
    public static void ValidateWholeInputs(string root,string package,string version) {BetterAstralParty.Updating.WholeInstallBridge.ValidateInputs(root,package,version);}
    public static string ValidateWholeBridge(string root,string package,string version) {return BetterAstralParty.Updating.WholeInstallBridge.Validate(root,package,version);}
    public static string ValidateReviewedWholeBridge(string root,string package,string version,string review) {return BetterAstralParty.Updating.WholeInstallBridge.ValidateReviewed(root,package,version,review);}
    public static string ReadVerifiedInstalledState(string root,string package,string version) {return BetterAstralParty.Updating.WholeInstallBridge.VerifiedInstalledState(root,package,version);}
    public static string PrepareWholeReceipt(string root,string package,string version,string observation,string loaderBefore) {return BetterAstralParty.Updating.WholeInstallBridge.PrepareReceipt(root,package,version,observation,loaderBefore);}
    public static IDisposable GuardWholeRecovery(string root,string package,string version) {return BetterAstralParty.Updating.WholeInstallBridge.GuardRecovery(root,package,version);}
    public static IDisposable GuardOrdinaryRecovery(string root,string package,string version,string journalHash) {
        try {return BetterAstralParty.Updating.WholeInstallBridge.GuardRecovery(root,package,version,journalHash);}
        catch(Exception error) {RecordInstallerFailure("Recovery",error);throw;}
    }
    public static IDisposable BeginWholeInstall(string root,string package,string version,string observation,string receipt) {return BetterAstralParty.Updating.WholeInstallBridge.BeginInstall(root,package,version,observation,receipt);}
    public static void RecoverWholeHelper(string root,string package,string version) {BetterAstralParty.Updating.WholeInstallBridge.Recover(root,package,version);}
    public static void InstallWholeHelper(string root,string package,string version,string observation,string receiptHash) {BetterAstralParty.Updating.WholeInstallBridge.Install(root,package,version,observation,receiptHash);}
    public static void RestoreOrdinaryInstall(string root,string journalRoot,string journalHash,string[] allowedTargets,string expectedCurrent,bool completeOnly,string rootIdentity) {
        RecordInstallerStage("Recovery","Begin");
        try {BetterAstralParty.Updating.OrdinaryInstallRecovery.Run(root,journalRoot,journalHash,allowedTargets,expectedCurrent,completeOnly,rootIdentity);RecordInstallerStage("Recovery","Completed");}
        catch(Exception error) {RecordInstallerFailure("Recovery",error);throw;}
    }
    public static IDisposable BeginOwnedRemoval(string root,string journalRoot,string journalHash,string[] allowedTargets,string rootIdentity,bool allowLoader) {
        return new BetterAstralParty.Updating.OrdinaryInstallRecovery.OwnedRemovalLease(root,journalRoot,journalHash,allowedTargets,rootIdentity,allowLoader);
    }

    internal static int Run(string root, out string details)
    {
        details = "";
        string phase = "SteamContext";
        try {
            WindowsProcessGuard.AssertNoRunning(new[]{WindowsPath.Absolute(Path.Combine(root,"AstralParty_INT.exe"))});
            phase = "UpdateGate";
            // The first automatic hop still runs the baseline helper; finish its signed delivery before the next game launch.
            try { using(var recovery=new LauncherRecoveryClient(root)) recovery.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult(); }
            catch(Exception error) {
                SaveFailure(LaunchFailure("HelperInstall",error));
                using(var fence=new WindowsFileFence(root)) UpdateLaunchGate.Check(fence);
                // A supported unchanged helper can retry delivery later; a pending mutation remains blocked.
            }
            // The already running native host owns the lease; PowerShell never reloads this executable.
            using (var guardedLaunch = GuardModLaunchVerified(root, null)) {
                phase = "Compatibility";
                var check = BetterAstralParty.Installation.GameCompatibility.Check(root, Path.Combine(root,"BetterAstralParty.compatibility.json"));
                if (!check.Allowed) {
                    try { SaveFailure(LaunchFailure(phase, new InvalidOperationException("Compatibility observation unavailable: " + string.Join("; ", check.Issues), check.Error)), true); } catch { }
                }
                try { if (check.Issues.Length + check.Warnings.Length != 0) BetterAstralParty.Installation.NativeMaintenanceEntry.SaveCompatibilityWarnings(root, check.Issues.Concat(check.Warnings).ToArray()); } catch { }
                phase = "SharedLoader";
                try { EnableSharedLoader(root, guardedLaunch); }
                catch (Exception error) when (error is not SharedLoaderRecoveryException && (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is BetterAstralParty.Updating.ApplySafetyException || error is System.Text.DecoderFallbackException)) {
                    // Preserve an unsupported foreign INI; only our still-valid launch lease may continue.
                    using (var fence = new WindowsFileFence(root)) ((UpdateLaunchGate.Lease)guardedLaunch).Recheck(fence);
                    try { SaveFailure(LaunchFailure(phase, error), true); } catch { }
                    try { BetterAstralParty.Installation.NativeMaintenanceEntry.SaveCompatibilityWarnings(root, new[] { "공용 로더 설정을 변경하지 않고 게임을 실행합니다. / Shared loader configuration left unchanged; game launch continues." }); } catch { }
                }
                var start = new ProcessStartInfo {
                    FileName = WindowsPath.ForProcess(Path.Combine(root, "AstralParty_INT.exe")),
                    Arguments = "--doorstop-enabled true --bap-compatibility-checked",
                    WorkingDirectory = WindowsPath.ForProcess(root), UseShellExecute = false
                };
                foreach (var name in new[] { "SteamAppId", "SteamGameId", "SteamOverlayGameId" }) start.EnvironmentVariables[name] = "2622000";
                start.EnvironmentVariables["BAP_COMPATIBILITY_CHECKED"] = "1";
                using (var launcher = Process.GetCurrentProcess()) {
                    start.EnvironmentVariables["BAP_UPDATER_LAUNCHER_PID"] = launcher.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    start.EnvironmentVariables["BAP_UPDATER_LAUNCHER_CREATION"] = launcher.StartTime.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    start.EnvironmentVariables["BAP_UPDATER_LAUNCHER_SESSION"] = launcher.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                phase = "GameStart";
                using (var game = Process.Start(start) ?? throw new InvalidOperationException("Game could not start")) {
                    phase = "GameExit";
                    game.WaitForExit();
                    if (game.ExitCode != 0) details = LaunchFailure(phase, new InvalidOperationException("Game exited with code " + game.ExitCode + ". Include BepInEx/LogOutput.log and Player.log when reporting a crash."));
                    return game.ExitCode;
                }
            }
        } catch (Exception error) { details = LaunchFailure(phase, error); return 1; }
    }

    internal static string LaunchFailure(string phase, Exception error)
    {
        var failure = error.GetBaseException();
        var type = failure.GetType().FullName ?? "Unknown";
        var allowed = new[] { "System.IO.FileNotFoundException", "System.IO.DirectoryNotFoundException", "System.UnauthorizedAccessException", "System.IO.IOException", "System.ArgumentException", "System.InvalidOperationException", "System.NotSupportedException", "System.ComponentModel.Win32Exception", "System.Security.SecurityException" };
        if (Array.IndexOf(allowed, type) < 0) type = "Unknown";
        return "LaunchFailure: schema=1; phase=" + phase + "; errorType=" + type + "; hresult=0x" + failure.HResult.ToString("X8") + "\r\n" + error;
    }

    // Failure-time observations only. No diagnosis, execution or authorization is inferred from these paths.
    internal static string CaptureLaunchContext(string root)
    {
        string full;
        try { full = WindowsPath.Absolute(root); }
        catch (Exception error) {
            return "LaunchRoot: source=LauncherDirectory; state=Invalid; hresult=0x" + error.HResult.ToString("X8") + "\r\n";
        }
        var text = new System.Text.StringBuilder("Game: ").Append(full).Append("\r\nLaunchRoot: source=LauncherDirectory; ")
            .Append(LaunchEntryState(full)).Append("\r\n");
        string[] names = { "AstralParty_INT.exe", BetterAstralParty.Updating.UpdateTrust.PluginPath, "BetterAstralParty.compatibility.json", "BetterAstralParty-Launcher.exe" };
        string[] kinds = { "GameExecutable", "Plugin", "CompatibilityManifest", "Launcher" };
        for (int i = 0; i < names.Length; i++)
            text.Append("LaunchFile: kind=").Append(kinds[i]).Append("; ").Append(LaunchEntryState(Path.Combine(full, names[i].Replace('/', '\\')))).Append("\r\n");
        return text.ToString();
    }
    private static string LaunchEntryState(string path)
    {
        string state; int hresult = 0;
        try {
            var attributes = File.GetAttributes(WindowsPath.ForIo(path));
            state = (attributes & FileAttributes.ReparsePoint) != 0 ? "Linked"
                : (attributes & FileAttributes.Directory) != 0 ? "Directory" : "File";
        }
        catch (FileNotFoundException error) { state = "Missing"; hresult = error.HResult; }
        catch (DirectoryNotFoundException error) { state = "Missing"; hresult = error.HResult; }
        catch (UnauthorizedAccessException error) { state = "Denied"; hresult = error.HResult; }
        catch (ArgumentException error) { state = "Invalid"; hresult = error.HResult; }
        catch (Exception error) { state = "Unavailable"; hresult = error.HResult; }
        return "state=" + state + "; hresult=0x" + hresult.ToString("X8");
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
            } catch { /* A failed log write must not prevent the error dialog. */ }
        }
        return null;
    }
}
