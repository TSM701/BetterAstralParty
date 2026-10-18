#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    internal static class NativeMaintenanceEntry
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int type);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        private static bool _interactive;
        private static string _run = "", _version = "unknown", _package = "", _game = "", _operation = "Maintenance";
        private static bool _preview;
        private static readonly List<string> Observations = new List<string>();
        internal static string? Consent(string prompt)
        {
            Console.WriteLine(prompt);
            if (!_interactive || Console.IsInputRedirected) throw new InvalidOperationException("대화형 콘솔에서 직접 확인해야 합니다. / Fresh interactive console consent required.");
            return Console.ReadLine();
        }
        internal static void ConsoleReady()
        {
            if (!AttachConsole(uint.MaxValue) && Marshal.GetLastWin32Error() != 5 && GetFileType(GetStdHandle(-11)) != 3 && GetFileType(GetStdHandle(-12)) != 3) AllocConsole();
            uint mode; _interactive = GetConsoleMode(GetStdHandle(-10), out mode) && Environment.UserInteractive;
            if (_interactive) Console.SetIn(new StreamReader(Console.OpenStandardInput(), Console.InputEncoding));
            if (GetStdHandle(-11) != IntPtr.Zero && GetStdHandle(-11) != new IntPtr(-1)) {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            }
        }
        internal static int Run(string[] args)
        {
            ConsoleReady(); _run = Guid.NewGuid().ToString("N"); Observations.Clear(); _preview = Array.Exists(args, arg => arg.TrimStart('-').Equals("whatif", StringComparison.OrdinalIgnoreCase) || arg.TrimStart('-').Equals("checkonly", StringComparison.OrdinalIgnoreCase));
            IDisposable? diagnostics = null; var priorWaiting = NativeSteamMaintenance.WaitingDiagnostic;
            NativeSteamMaintenance.WaitingDiagnostic = Waiting;
            try {
                var values = Options(args);
                var emergency = values.ContainsKey("force-uninstall");
                _operation = emergency ? "EmergencyUninstall" : values.ContainsKey("install") ? "Installation" : "Uninstall";
                _preview = values.ContainsKey("whatif") || values.ContainsKey("checkonly");
                if (values.ContainsKey("development")) return Development(args, values);
                _package = WindowsPath.Parent(WindowsPath.FromIo(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'));
                var supplied = Get(values, "gameroot"); var updateOnly = values.ContainsKey("updateonly");
                var accountText = Get(values, "accountid");
                var account = accountText == null ? 0U : uint.Parse(accountText, NumberStyles.None, CultureInfo.InvariantCulture);
                var removing = _operation != "Installation";
                if (updateOnly && (supplied == null || values.ContainsKey("restartsteam") || account != 0 || _operation != "Installation")) throw new ArgumentException("UpdateOnly requires GameRoot and cannot register/restart Steam.");
                string? steamObservation = null;
                var steam = updateOnly ? null : FindSteamRoot(account == 0, value => {
                    steamObservation = value;
                    Console.WriteLine("Steam 자동 탐색을 사용할 수 없어 바로가기는 변경하지 않습니다. / Steam discovery unavailable; shortcuts will remain unchanged.");
                });
                try { _game = ResolveOperationGame(supplied, steam, _package, removing, emergency); }
                catch (InvalidOperationException) when (supplied == null && !_preview) {
                    var candidates = NativeSteamMaintenance.FindGameDirectories(steam, _package, Observe);
                    for (var index = 0; index < candidates.Count; index++) Console.WriteLine((index + 1).ToString(CultureInfo.InvariantCulture) + ": " + candidates[index]);
                    var choice = Consent("INT 게임 설치 폴더를 입력하세요 (Astral Party 또는 8vJXnINT). 빈 값은 취소합니다. / Enter INT game installation folder; blank cancels.");
                    if (string.IsNullOrWhiteSpace(choice)) throw new InvalidOperationException("Maintenance cancelled; no game files changed.");
                    int selected; if (int.TryParse(choice, NumberStyles.None, CultureInfo.InvariantCulture, out selected) && selected > 0 && selected <= candidates.Count) choice = candidates[selected - 1];
                    _game = ResolveOperationGame(choice, steam, _package, removing, emergency);
                }
                if (!updateOnly && _operation == "Installation") {
                    if (steam == null && account != 0) throw new InvalidOperationException("Cannot verify the requested Steam account without its installation directory.");
                    if (steam != null) try { account = NativeSteamMaintenance.ResolveAccount(steam, account); }
                        catch (Exception error) when (account == 0 && (error is SteamProfileUnavailableException || error is IOException || error is InvalidDataException || error is InvalidOperationException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is DecoderFallbackException)) {
                            Observe("NativeInstallShortcut: action=Skipped; reason=SteamProfileUnavailable; exception=" + error.GetType().Name
                                + "; hresult=0x" + error.HResult.ToString("X8", CultureInfo.InvariantCulture));
                            Console.WriteLine("Steam 계정을 확인하지 못해 파일 설치만 진행합니다. / Steam profile unavailable; install files without changing shortcuts.");
                        }
                }
                var recoveryPlan = Get(values, "recoveryplan");
                using (var bundle = new NativeMaintenanceBundle(_package, _game, recoveryPlan == null ? null : NativeSteamMaintenance.ResolveUserPath(recoveryPlan))) {
                    _version = bundle.Version;
                    Console.WriteLine("INT installation: " + _game);
                    if (!_preview) diagnostics = SetupHost.BeginInstallerDiagnostics(_game, _version);
                    SetupHost.ValidateMaintenancePaths(_game, NativeMaintenance.JournalRoot(_game));
                    if (_operation == "Installation") {
                        if (steamObservation != null) Observe(steamObservation);
                        NativeInstall.Run(bundle, updateOnly, steam, account, Consent, Console.WriteLine, restartSteam: values.ContainsKey("restartsteam"), checkOnly: _preview, observation: Observe);
                    }
                    else if (emergency) NativeEmergencyUninstall.Run(bundle, steam, account, _preview, Consent, Console.WriteLine);
                    else NativeUninstall.Run(bundle, steam, account, Get(values, "restoreloader") ?? "Keep", _preview, Consent, Console.WriteLine, value => {
                        Observe(value); if (steamObservation != null) { Observe(steamObservation); steamObservation = null; }
                    });
                }
                Console.WriteLine((_preview ? "검사 완료. 변경 없음. / Check completed; no maintenance executed." : "작업 완료. / Maintenance completed.") + " Version: " + _version + "; run: " + _run);
                SaveObservations();
                return 0;
            } catch (Exception error) {
                if (!_preview) { SetupHost.RecordInstallerFailure("BootstrapGate", error); var path = Failure(error); if (path != null) Console.WriteLine("Debug log: " + path); }
                var action = RecoveryAction(error); if (action != null) Console.Error.WriteLine(action);
                Console.Error.WriteLine(error.Message); return 1;
            } finally { NativeSteamMaintenance.WaitingDiagnostic = priorWaiting; diagnostics?.Dispose(); }
        }
        internal static Dictionary<string, string?> Options(string[] args)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(new[] { "install", "uninstall", "force-uninstall", "development", "updateonly", "restartsteam", "whatif", "checkonly" }, StringComparer.OrdinalIgnoreCase);
            var fields = new HashSet<string>(new[] { "gameroot", "accountid", "recoveryplan", "restoreloader" }, StringComparer.OrdinalIgnoreCase);
            for (var at = 0; at < args.Length; at++) {
                var name = args[at].TrimStart('-');
                if (result.ContainsKey(name) || (!flags.Contains(name) && !fields.Contains(name))) throw new ArgumentException("Unsupported/duplicate maintenance argument: " + args[at]);
                if (flags.Contains(name)) result.Add(name, null);
                else { if (++at == args.Length || args[at].Length == 0) throw new ArgumentException("Maintenance argument value required."); result.Add(name, args[at]); }
            }
            if (Array.FindAll(new[] { "install", "uninstall", "force-uninstall" }, result.ContainsKey).Length != 1) throw new ArgumentException("Select exactly one maintenance operation.");
            if (result.ContainsKey("install") && result.ContainsKey("restoreloader") || result.ContainsKey("uninstall") && (result.ContainsKey("updateonly") || result.ContainsKey("restartsteam") || result.ContainsKey("recoveryplan"))) throw new ArgumentException("Unsupported argument for this maintenance operation.");
            if (result.ContainsKey("force-uninstall") && (result.ContainsKey("updateonly") || result.ContainsKey("restartsteam") || result.ContainsKey("recoveryplan") || result.ContainsKey("restoreloader"))) throw new ArgumentException("Emergency removal preserves loader settings and cannot install or adopt a recovery plan.");
            var accountText = Get(result, "accountid"); uint account;
            if (accountText != null && (!uint.TryParse(accountText, NumberStyles.None, CultureInfo.InvariantCulture, out account) || account == 0)) throw new ArgumentException("Invalid Steam account identifier.");
            var restore = Get(result, "restoreloader");
            if (restore != null && !new HashSet<string>(new[] { "Keep", "Recorded", "Enabled", "Disabled" }, StringComparer.OrdinalIgnoreCase).Contains(restore)) throw new ArgumentException("RestoreLoader must be Keep/Recorded/Enabled/Disabled.");
            return result;
        }
        private static string? Get(Dictionary<string, string?> values, string name) { string? value; return values.TryGetValue(name, out value) ? value : null; }
        internal static string ResolveOperationGame(string? supplied, string? steam, string package, bool removal, bool emergency = false)
        {
            if (!removal || string.IsNullOrWhiteSpace(supplied)) return NativeSteamMaintenance.ResolveGame(supplied, steam, package, Observe);
            var runnable = NativeSteamMaintenance.ResolveIntDirectory(supplied);
            if (runnable != null) return runnable;
            var root = NativeSteamMaintenance.ResolveUserPath(supplied!);
            foreach (var candidate in new[] { root, root + "\\8vJXnINT" }) {
                WindowsFileFence? fence = null;
                try {
                    fence = new WindowsFileFence(candidate);
                    try {
                        using (var file = fence.OpenFile(InstallReceipt.Path)) {
                            var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384));
                            if (receipt.Channels == null || receipt.Channels.Root != fence.RootIdentity.Text) throw new InvalidDataException("Removal receipt belongs to another root.");
                            Observe("RemovalRoot: evidence=ReceiptRootIdentity; originalExecutable=Absent"); return fence.Root;
                        }
                    } catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3 || emergency && error.Failure == ApplyFailure.InvalidState) { }
                      catch (DecoderFallbackException) when (emergency) { }
                      catch (InvalidDataException) when (emergency) { }
                    if (!emergency) continue;
                    fence.DirectoryIdentity("AstralParty_INT_Data");
                    foreach (var path in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path, OrdinaryInstallRecovery.LegacyLauncher, OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyHelperReceipt, "BepInEx/config/kr.betterastralparty.mod.cfg" })) {
                        try { using (var file = fence.OpenFile(path)) { file.Recheck(); fence.Recheck(); Observe("RemovalRoot: evidence=NativeDataAndExactResidue; originalExecutable=Absent"); return fence.Root; } }
                        catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { }
                    }
                } catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { }
                finally { fence?.Dispose(); }
            }
            throw new InvalidOperationException("제거할 게임 폴더의 소유 기록이나 원본 데이터 기준점을 확인할 수 없습니다. / Select the removal root with its installation receipt or native game-data evidence.");
        }
        internal static string? FindSteamRoot(bool optional, Action<string> unavailable)
        {
            try { return NativeSteamMaintenance.ResolveSteamRoot(); }
            catch (Exception error) when (optional && (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ArgumentException))
            {
                unavailable("NativeUninstallSteamDiscovery: action=Skipped; reason=SteamRootDiscoveryUnavailable; exception=" + error.GetType().Name
                    + "; hresult=0x" + error.HResult.ToString("X8", CultureInfo.InvariantCulture));
                return null;
            }
        }
        private static int Development(string[] args, Dictionary<string, string?> values)
        {
            var project = WindowsPath.Parent(WindowsPath.FromIo(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'));
            using (var fence = new WindowsFileFence(project)) using (var pointer = fence.OpenFile("artifacts/development-maintenance.json")) {
                var json = MaintenanceJson.Read(UpdateTicket.Read(pointer, 4096));
                if (json.Count != 4 || MaintenanceJson.Number(json, "schema") != 1) throw new InvalidDataException("Invalid development package pointer.");
                var version = MaintenanceJson.Text(json, "version"); UpdateVersion.Parse(version);
                var relative = MaintenanceJson.Text(json, "packagePath"); var digest = MaintenanceJson.Text(json, "manifestSha256");
                if (relative.Length > 512 || !Regex.IsMatch(relative, @"\A(?:artifacts/automatic-update-development|releases)/.+\z") || !MaintenanceJson.Hash(digest)) throw new InvalidDataException("Development package outside approved scope.");
                var package = fence.Full(relative);
                var game = ResolveOperationGame(Get(values, "gameroot") ?? WindowsPath.Parent(project) + "\\8vJXnINT", null, project, !values.ContainsKey("install"), values.ContainsKey("force-uninstall"));
                if (package.Equals(game, StringComparison.OrdinalIgnoreCase) || package.StartsWith(game + "\\", StringComparison.OrdinalIgnoreCase) || game.StartsWith(package + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package and game directories must be separate.");
                using (var manifest = fence.OpenFile(relative + "/package-manifest.json")) using (var image = fence.OpenLaunchImage(relative + "/" + NativeMaintenanceBundle.SetupPath)) {
                    var bytes = UpdateTicket.Read(manifest, 1024 * 1024);
                    if (manifest.Hash() != digest) throw new InvalidDataException("Pinned complete package changed.");
                    var inventory = MaintenanceJson.Read(bytes);
                    if (MaintenanceJson.Text(inventory, "version") != version) throw new InvalidDataException("Pinned package version changed.");
                    var matches = 0;
                    foreach (var raw in MaintenanceJson.List(inventory, "files")) { var row = MaintenanceJson.Map(raw); if (MaintenanceJson.Text(row, "path") == NativeMaintenanceBundle.SetupPath) { if (MaintenanceJson.Text(row, "sha256") != image.Hash() || MaintenanceJson.Number(row, "bytes") != image.Stream.Length) throw new InvalidDataException("Pinned setup tool changed."); matches++; } }
                    if (matches != 1) throw new InvalidDataException("Pinned native setup tool missing.");
                    var forward = new List<string>(); for (var at = 0; at < args.Length; at++) {
                        var name = args[at].TrimStart('-');
                        if (name.Equals("development", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.Equals("gameroot", StringComparison.OrdinalIgnoreCase)) { at++; continue; }
                        forward.Add(args[at]);
                    }
                    forward.Add("-GameRoot"); forward.Add(game);
                    Console.WriteLine("개발 폴더 실행 기준 / Development maintenance baseline: " + version);
                    using (var child = Process.Start(new ProcessStartInfo { FileName = WindowsPath.ForProcess(image.Path), Arguments = CommandLine(forward), WorkingDirectory = WindowsPath.ForProcess(package), UseShellExecute = false }) ?? throw new InvalidOperationException("Native setup tool could not start.")) { child.WaitForExit(); pointer.Recheck(); manifest.Recheck(); image.Recheck(); return child.ExitCode; }
                }
            }
        }
        internal static string CommandLine(IEnumerable<string> args)
        {
            var output = new List<string>();
            foreach (var arg in args) {
                if (arg.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new ArgumentException("Invalid command-line text.");
                var quoted = new StringBuilder("\""); var slashes = 0;
                foreach (var c in arg) {
                    if (c == '\\') { slashes++; continue; }
                    quoted.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); quoted.Append(c); slashes = 0;
                }
                quoted.Append('\\', slashes * 2).Append('"'); output.Add(quoted.ToString());
            }
            return string.Join(" ", output);
        }
        private static void Observe(string value) { if (!_preview) { Console.WriteLine(value); if (Observations.Count < 16) Observations.Add(value.Substring(0, Math.Min(value.Length, 2048))); } }
        internal static void SaveObservations()
        { if (!_preview && Observations.Count != 0) SaveLog("BetterAstralParty Maintenance observation\r\nRun: " + _run + "\r\nVersion: " + _version + "\r\n" + string.Join("\r\n", Observations)); }
        internal static void SaveCompatibilityWarnings(string root, string[] warnings)
        {
            try { using (var fence = new WindowsFileFence(root)) {
                WindowsFileFence.FileLease file;
                try { file = fence.OpenFile("BetterAstralParty-Compatibility.log", true); } catch (ApplySafetyException error) when (error.NativeError == 2) { file = fence.OpenFile("BetterAstralParty-Compatibility.log", true, true); }
                using (file) { var bytes = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, warnings)); if (bytes.Length > 65536) throw new InvalidDataException("Compatibility log bounds exceeded."); file.Stream.SetLength(0); file.Stream.Write(bytes, 0, bytes.Length); file.Flush(); }
            } } catch { }
        }
        internal static string? Failure(Exception error)
        { try { return Record(error, false); } catch { return null; } }
        internal static string? RecoveryAction(Exception error)
        {
            var at = error; var depth = 0;
            while (at != null && depth++ < 8) {
                if (at.Data["BapRecoveryReason"] as string == "LegacyFlatWorkPending")
                    return "이전 설치 패키지로 복구 또는 제거를 완료한 뒤 다시 시도하세요. 남은 작업 자료를 임의로 이동하거나 삭제하지 마세요. / Complete recovery or removal with the matching previous package, then retry. Do not manually move or delete retained work.";
                at = at.InnerException;
            }
            return null;
        }
        private static string? Record(Exception error, bool waiting)
        {
            if (_preview) return null;
            var text = new StringBuilder("BetterAstralParty ").Append(_operation).Append(waiting ? " observation\r\nCode: BAP-INSTALL-WAITING" : " failure\r\nCode: BAP-INSTALL-FAILED").Append("\r\nRuntime: Precompiled .NET Framework\r\nTime: ").Append(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture))
                .Append("\r\nRun: ").Append(_run).Append("\r\nVersion: ").Append(_version).Append("\r\nPackage: ").Append(_package).Append("\r\nGame: ").Append(_game).Append("\r\n");
            var at = error; var depth = 0; var textTruncated = false; var steamContext = "not-observed";
            while (at != null && depth < 8) {
                var index = depth++;
                text.Append("Exception: depth=").Append(index).Append("; type=").Append(at.GetType().FullName).Append("; HResult=0x").Append(at.HResult.ToString("X8", CultureInfo.InvariantCulture));
                if (at is ApplySafetyException || at.Data.Contains("BapRecoveryReason")) {
                    var reason = at.Data["BapRecoveryReason"] is string candidate && RecoveryReasons.Contains(candidate) ? candidate : "unknown";
                    var apply = "unknown"; var native = "unknown";
                    if (at is ApplySafetyException safety) {
                        if (Enum.IsDefined(typeof(ApplyFailure), safety.Failure)) apply = ((int)safety.Failure).ToString(CultureInfo.InvariantCulture);
                        if (safety.NativeError >= 0) native = safety.NativeError.ToString(CultureInfo.InvariantCulture);
                    }
                    text.Append("\r\nException: depth=").Append(index).Append("; recoveryReason=").Append(reason).Append("; applyCode=").Append(apply).Append("; nativeCode=").Append(native);
                    text.Append("\r\nBapRecoveryReason: ").Append(reason);
                    var action = RecoveryAction(at); if (action != null) text.Append("\r\nRecovery action: ").Append(action);
                }
                if (at.Data.Contains("BapInstallStage")) text.Append("\r\nBapInstallStage: ").Append(KnownStage(at.Data["BapInstallStage"]));
                if (at.Data.Contains("BapEmergencyPhase")) text.Append("\r\nBapEmergencyPhase: ").Append(at.Data["BapEmergencyPhase"] is string phase && EmergencyPhases.Contains(phase) ? phase : "unknown");
                foreach (var name in new[] { "BapEmergencyBackup", "BapEmergencyQuarantine", "BapEmergencyLastTarget" })
                    if (at.Data[name] is string value) {
                        textTruncated |= value.Length > 16384;
                        text.Append("\r\n").Append(name).Append(": ").Append(value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ? "unknown" : value.Substring(0, Math.Min(value.Length, 16384)));
                    }
                foreach (var name in new[] { "BapEmergencyDeletedFiles", "BapEmergencyDeletedDirectories", "BapEmergencyObservedFiles" })
                    if (at.Data[name] is int count && count >= 0 && count <= (name == "BapEmergencyDeletedDirectories" ? 2048 : 4096))
                        text.Append("\r\n").Append(name).Append(": ").Append(count.ToString(CultureInfo.InvariantCulture));
                if (steamContext == "not-observed" && HasSteamContext(at)) {
                    string facts; SteamContext(at.Data["BapSteamLog"], out facts, out steamContext);
                    if (facts.Length != 0) text.Append("\r\n").Append(facts);
                    else text.Append("\r\nSteamObservationCoverage: status=invalid-context\r\n");
                }
                textTruncated |= at.Message.Length > 8192 || (at.StackTrace ?? "").Length > 8192;
                text.Append("\r\nMessage: ").Append(at.Message.Substring(0, Math.Min(at.Message.Length, 8192))).Append("\r\nStack: ").Append((at.StackTrace ?? "").Substring(0, Math.Min((at.StackTrace ?? "").Length, 8192))).Append("\r\n");
                if (at.Data["BapRollbackFailure"] is Exception rollback) { textTruncated |= rollback.Message.Length > 8192; text.Append("RollbackFailure: ").Append(rollback.Message.Substring(0, Math.Min(rollback.Message.Length, 8192))).Append("\r\n"); }
                if (at.Data["BapShortcutRecovery"] is ShortcutRecovery shortcut) {
                    text.Append("ShortcutRecovery: path=").Append(shortcut.Path).Append("; backup=").Append(shortcut.Backup ?? "-").Append("; hash=").Append(shortcut.Hash).Append("; backupHash=").Append(shortcut.BackupHash).Append("\r\n");
                    var count = 0; foreach (var art in shortcut.Artwork) { if (count++ == 16) break; text.Append("ShortcutArtworkRecovery: path=").Append(art.Path).Append("; backup=").Append(art.Backup).Append("; hash=").Append(art.Hash).Append("\r\n"); }
                }
                at = at.InnerException;
            }
            text.Append("DiagnosticCoverage: exceptionDepth=").Append(depth).Append("; exceptionChainOmitted=").Append(at != null ? "true" : "false")
                .Append("; textTruncated=").Append(textTruncated ? "true" : "false").Append("; steamContext=").Append(steamContext).Append("\r\n");
            text.Append(string.Join("\r\n", Observations));
            var output = text.ToString(); if (output.Length > 196608) output = output.Substring(0, 196608) + "\r\nDiagnosticCoverage: log-text-limit";
            return SaveLog(output);
        }
        private static string? _waitingKey;
        internal static void Waiting(Exception error)
        {
            if (_preview) return;
            try {
                var at = error; var depth = 0; var facts = ""; var status = "not-observed";
                while (at != null && depth++ < 8) { if (HasSteamContext(at)) { SteamContext(at.Data["BapSteamLog"], out facts, out status); break; } at = at.InnerException; }
                var key = _run + "|" + _game + "|" + status + "|" + Regex.Replace(facts, @"; utc=[^;\r\n]+;", "; utc=omitted;");
                if (key == _waitingKey) return;
                // Record flushes the create-new file before returning to the consent callback.
                if (Record(error, true) != null) _waitingKey = key;
            } catch { }
        }
        private static bool HasSteamContext(Exception error)
        { return error.Data.Contains("BapSteamDiagnostic") || error.Data.Contains("BapSteamLog") || error.Data.Contains("BapSteamLogStatus"); }
        private static readonly HashSet<string> EmergencyPhases = new HashSet<string>(new[] { "Preflight", "Processes", "Locks", "Capture", "Delete", "Shortcut", "RecoveryDirectories", "FinalCheck" }, StringComparer.Ordinal);
        private static string KnownStage(object? value)
        {
            if (!(value is string stage)) return "unknown";
            if (stage.StartsWith("Emergency ", StringComparison.Ordinal) && EmergencyPhases.Contains(stage.Substring(10))) return stage;
            return InstallStages.Contains(stage) ? stage : "unknown";
        }
        private static readonly HashSet<string> InstallStages = new HashSet<string>(new[] {
            "Recovery", "Steam shortcut planning", "Signed automatic update helper preflight", "Interrupted installation/removal recovery", "Completed removal / new installation generation validation",
            "Whole helper recovery", "Installed ownership / whole bridge validation", "Installed version / channel transition validation", "Compatibility validation", "Package file / shared loader validation",
            "Maintenance path planning / write permission preflight", "Steam shortcut planning / shutdown", "Previous installation backup", "Loader / mod / launcher copy", "Loader configuration", "Steam shortcut registration", "Automatic update helper installation / ownership verification"
        }, StringComparer.Ordinal);
private static readonly HashSet<string> RecoveryReasons = new HashSet<string>(new[] {
            "EmergencyAllowlistInvalid", "EmergencyBackupScopeInvalid", "EmergencyCaptureStopped", "EmergencyDeleteStopped",
            "EmergencyDirectoryLimitExceeded", "EmergencyFileLimitExceeded", "EmergencyFinalCheckStopped", "EmergencyGameRootUnavailable",
            "EmergencyInstallLockLost", "EmergencyLockPreviewChanged", "EmergencyLocksStopped", "EmergencyPreflightStopped",
            "EmergencyPreviewChanged", "EmergencyProcessesStopped", "EmergencyRecoveryDirectoriesStopped", "EmergencyRecoveryReappeared",
            "EmergencyRootChanged", "EmergencyRootInventoryLimitExceeded", "EmergencyWorkInventoryLimitExceeded", "EmergencyShortcutPreviewChanged", "EmergencyShortcutStopped",
            "EmergencyTargetChanged", "EmergencyTargetReappeared", "HelperEvidenceMissing", "HelperInstallConflict",
            "HelperInstallPending", "HelperUpgradePending", "HighWaterInvalid", "HighWaterProofAmbiguous",
            "HighWaterProofBusy", "HighWaterProofChanged", "HighWaterProofMissing", "HighWaterRepositoryMismatch",
            "HighWaterRootMismatch", "HighWaterUnbound", "HistoricalCommitMismatch", "HistoricalOwnedProofMismatch",
            "HistoricalProofAmbiguous", "HistoricalReceiptMismatch", "HistoricalSigningKeyInvalid", "InstallReceiptMissing",
            "LegacyFlatWorkPending", "MaintenancePathInvalid", "MaintenancePathTooLong", "OrdinaryHelperRepairFailed", "OrdinaryHelperRepairJournalChanged",
            "OrdinaryHelperRepairMalformed", "OrdinaryHelperRepairObjectChanged", "OrdinaryHelperRepairProofChanged", "OwnedFileChanged",
            "PriorInstalledProofMismatch", "PriorProofDigestInvalid", "PriorProofDigestMismatch", "ReinstallArchiveChanged",
            "ReinstallBackupLockMismatch", "ReinstallInputChanged", "ReinstallInputInvalid", "ReinstallOperationPending",
            "ReinstallOwnedFilesRemain", "ReinstallPlanChanged", "ReinstallRootChanged", "ReinstallWaterChanged",
            "RemovalActiveVersionMismatch", "RemovalBackupChanged", "RemovalBackupIncomplete", "RemovalBackupInvalid",
            "RemovalBackupMissingCore", "RemovalBackupRequired", "RemovalBackupRootMismatch", "RemovalBackupUnexpected",
            "RemovalChannelMissing", "RemovalEvidenceChanged", "RemovalEvidenceConflict", "RemovalHistoricalAmbiguous",
            "RemovalHistoricalCommitChanged", "RemovalHistoricalDescriptorInvalid", "RemovalHistoricalMismatch", "RemovalHistoricalProofRequired",
            "RemovalHistoricalSlotInvalid", "RemovalHistoryMismatch", "RemovalJournalDuplicateKey", "RemovalJournalEntryInvalid",
            "RemovalJournalInvalid", "RemovalPluginUnsupported", "RemovalProofRequired", "RemovalReceiptMismatch",
            "RemovalRepositoryMismatch", "RemovedHelperProofInvalid", "RemovedInstallProofInvalid", "ReviewHelperChanged",
            "ReviewIntentLimit", "ReviewIntentShape", "ReviewObservedHashMismatch", "ReviewObservedSetInvalid",
            "ReviewOriginChanged", "ReviewOriginObjectChanged", "ReviewOriginalReceiptRequired", "ReviewOriginalSnapshotMismatch",
            "ReviewPlanBindingMismatch", "ReviewPlanLimit", "ReviewPlanShape", "ReviewPriorProofChanged",
            "ReviewPriorProofMismatch", "ReviewPriorProofMissing", "ReviewReceiptMismatch", "ReviewSnapshotPathInvalid",
            "ReviewSnapshotRootMismatch", "ReviewSnapshotSlotInvalid", "SignedInstalledProofRequired", "UnreviewedChangedOwnedFile",
            "UnsafePath", "UnverifiedReviewIntent", "UpdateOperationPending", "UpdateTransactionPending",
            "WholeProofCommitMismatch", "WholeProofIncomplete", "WholeProofPathInvalid", "WholeProofReceiptMismatch",
            "WholeProofRootMismatch"
        }, StringComparer.Ordinal);
        private static bool IntField(string value, int minimum, out int result)
        { return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= minimum; }
        private static bool UtcField(string value)
        { DateTime parsed; return DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed); }
        private static Regex ContextPattern(string value) { return new Regex(value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        private static readonly Regex SteamHeader = ContextPattern(@"\ASteamDiagnostic: schema=1; reason=(ProcessObservationUnavailable|SteamOwnershipUnknown|SteamOtherSession|SteamRunning|ProcessListChanged|ProcessIdentityChanged|ProcessTerminationFailed|ProcessCaptureFailed|ProcessTerminateOpenFailed|ProcessTerminateFailed|ProcessExitWaitFailed|ProcessExitTimeout|ConsentCancelled|ConsentUnavailable|HelperRunning|GameProcessesRemain|SteamGameProcessesRemain|InvalidSteamAppDirectory|SteamExecutableMissing|Unknown|OtherSession); phase=(ShutdownObservation|ShutdownConsent|ShutdownRecheck|ProcessIdentity|ProcessTermination|ShutdownCompletion|InstallPreflight|UninstallPreflight|ShortcutPreflight|ShortcutCommit|ShortcutRollback|ShortcutRollbackCommit|Resume|HelperWait|GameCompletion|GamePreflight); utc=([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z); observedCount=([0-9]{1,10}); omittedCount=([0-9]{1,10})\z");
        private static readonly Regex SteamProcessLine = ContextPattern(@"\ASteamProcess: index=([1-9][0-9]?); pid=([1-9][0-9]{0,9}); sessionMatch=(true|false|unknown); pathAvailable=(true|false); pathMatch=(true|false|unknown); startAvailable=(true|false); startedUtc=(unknown|[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z)\z");
        private static readonly Regex SteamObservationLine = ContextPattern(@"\ASteamObservationProcess: index=([1-9][0-9]?); pid=([1-9][0-9]{0,9}); kind=(SteamClient|SteamWebHelper|SteamOverlay|SteamErrorReporter|Game|Launcher|UpdateHelper|Other); querySource=(Managed|Native|Unavailable); nativeError=([0-9]{1,10}); exitAvailable=(true|false); confirmedExit=(true|false)\z");
        private static void SteamContext(object? value, out string facts, out string status)
        {
            facts = ""; status = "invalid-context";
            if (!(value is string text)) return;
            if (text.Length > 16384 || Encoding.UTF8.GetByteCount(text) > 16384) { status = "context-limit"; return; }
            try {
                var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length < 3 || lines.Length > 99 || lines[lines.Length - 1] != "" || lines[lines.Length - 2] != "SteamObservationCoverage: status=included" || (lines.Length - 3) % 2 != 0) return;
                var header = SteamHeader.Match(lines[0]); int observed, omitted;
                if (!header.Success || !UtcField(header.Groups[3].Value) || !IntField(header.Groups[4].Value, 0, out observed) || !IntField(header.Groups[5].Value, 0, out omitted)) return;
                var count = (lines.Length - 3) / 2;
                if (count > 48 || (long)count + omitted != observed) return;
                for (var i = 0; i < count; i++) {
                    var process = SteamProcessLine.Match(lines[1 + i * 2]); var observation = SteamObservationLine.Match(lines[2 + i * 2]); int index, pid, otherIndex, otherPid, native;
                    if (!process.Success || !observation.Success || !IntField(process.Groups[1].Value, 1, out index) || index != i + 1 || !IntField(process.Groups[2].Value, 1, out pid)
                        || !IntField(observation.Groups[1].Value, 1, out otherIndex) || otherIndex != index || !IntField(observation.Groups[2].Value, 1, out otherPid) || otherPid != pid || !IntField(observation.Groups[5].Value, 0, out native)) return;
                    if (process.Groups[6].Value == "true" ? !UtcField(process.Groups[7].Value) : process.Groups[7].Value != "unknown") return;
                    if (observation.Groups[7].Value == "true" && observation.Groups[6].Value != "true") return;
                }
                facts = text; status = omitted == 0 ? "included" : "context-limit";
            } catch { }
        }
        private static string? SaveLog(string output)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.Length != 0) output = Regex.Replace(output, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
            foreach (var root in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty/Logs"), Path.Combine(Path.GetTempPath(), "BetterAstralParty") }) try {
                var directory = WindowsPath.Absolute(root.Replace('/', '\\')); Directory.CreateDirectory(WindowsPath.ForIo(directory)); var path = WindowsPath.Absolute(directory + "\\BetterAstralParty-InstallError-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
                using (var file = new FileStream(WindowsPath.ForIo(path), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { var bytes = new UTF8Encoding(true).GetBytes(output); file.Write(bytes, 0, bytes.Length); file.Flush(true); } return path;
            } catch { }
            return null;
        }
    }
}
