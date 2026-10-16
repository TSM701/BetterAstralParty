using BepInEx.Configuration;
using BetterAstralParty;
using BetterAstralParty.Updating;
using System.Diagnostics;
using System.Text;

internal static partial class HelperFixture
{
    private sealed class ReceiptBoundaryHandoff(UpdateHandoff actual, string root, string mode) : IUpdateHandoff, IDisposable
    {
        internal UpdateCancelSignal? Observer;
        internal string? Stage;
        internal bool PreparedBeforeMutation;
        public bool Configured => actual.Configured;
        public async Task<object> Prepare(DownloadedUpdate update, UpdateContext context, CancellationToken token) {
            object value;
            try { value = await actual.Prepare(update, context, token); }
            catch (Exception error) { Console.Error.WriteLine("TP02 actual Prepare: " + error + (error is ApplySafetyException native ? " native=" + native.NativeError : "")); throw; }
            Stage = ((IPreparedUpdate)value).Stage;
            Observer = UpdateCancelSignal.Open(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, Stage, "handoff.ticket"))));
            PreparedBeforeMutation = File.Exists(Path.Combine(root, InstallReceipt.Path));
            if (mode.StartsWith("prepare-", StringComparison.Ordinal)) MutateReceipt(root, mode, Stage);
            return value;
        }
        public Task<object> Queue(object value, CancellationToken token) => actual.Queue(value, token);
        public void SignalCancel(object value) => actual.SignalCancel(value);
        public void SignalFailure(object value) => actual.SignalFailure(value);
        public Task Cancel(object value) => actual.Cancel(value);
        public Task<string?> Inspect() => actual.Inspect();
        public void Dispose() => Observer?.Dispose();
    }
    private static void MutateReceipt(string root, string mode, string stage) {
        var receipt = Path.Combine(root, InstallReceipt.Path);
        if (mode.Contains("-delete-", StringComparison.Ordinal)) File.Delete(receipt);
        else if (mode.Contains("-unsupported-", StringComparison.Ordinal)) File.WriteAllText(receipt, "{\"schema\":0,\"legacy\":true}");
        else if (mode.Contains("-mismatch-", StringComparison.Ordinal)) {
            var old = File.ReadAllText(receipt); var parsed = InstallReceipt.Parse(Encoding.ASCII.GetBytes(old));
            var changed = old.Replace(parsed.Files[UpdateTrust.PluginPath], new string('A', 64), StringComparison.Ordinal);
            Check(changed != old && InstallReceipt.Parse(Encoding.ASCII.GetBytes(changed)).Files[UpdateTrust.PluginPath] != parsed.Files[UpdateTrust.PluginPath], "TP02 receipt mismatch mutation really changes the recorded plugin hash");
            File.WriteAllText(receipt, changed);
        } else if (mode.Contains("-linked-", StringComparison.Ordinal)) Check(CreateHardLinkW(Path.Combine(root, "receipt.alias"), receipt, IntPtr.Zero), "TP02 create isolated receipt hardlink");
        else if (mode.Contains("-corrupt-", StringComparison.Ordinal)) {
            var zip = Path.Combine(root, stage, "package.zip"); var bytes = File.ReadAllBytes(zip); bytes[^1] ^= 1; File.WriteAllBytes(zip, bytes);
        } else throw new Exception("Unknown receipt boundary");
        File.WriteAllText(Path.Combine(root, "receipt-boundary.marker"), mode);
    }
    private static async Task<int> ReceiptBackendHost(string[] args) {
        var root = Path.GetFullPath(args[1]); var mode = args[2];
        if (!root.StartsWith(Path.GetFullPath(Environment.GetEnvironmentVariable("BAP_HELPER_FIXTURE_ROOT") ?? throw new Exception("Fixture root missing")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Receipt fake roots only");
        var files = Path.GetDirectoryName(root)!;
        var trust = new UpdateTrust([new TrustedUpdateKey("helper-fixture", new() { Modulus = File.ReadAllBytes(Path.Combine(files, "fixture-public-key.bin")), Exponent = [1, 0, 1] })]);
        var auth = new Auth(); var download = mode.EndsWith("-on", StringComparison.Ordinal);
        using var handoff = new ReceiptBoundaryHandoff(new UpdateHandoff(root, trust, UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, "BetterAstralParty-UpdateHelper.exe")))), root, mode);
        var config = new ConfigFile(Path.Combine(root, "preferences.cfg"), true) { SaveOnConfigSet = false };
        var d = config.Bind("Update", "Download", true); var a = config.Bind("Update", "Apply", true);
        using var bridge = new UpdatePreferencesBridge(config, d, a);
        var model = new AutomaticUpdates(new UpdateDownloads(trust, auth, new BytesHandler(File.ReadAllBytes(Path.Combine(files, "descriptor.bin")), File.ReadAllBytes(Path.Combine(files, "signature.envelope")), File.ReadAllBytes(Path.Combine(files, "package.zip")))), handoff, new FileUpdateSafety(root, bridge.Save));
        model.Preferences(false, false); model.Configure(auth); model.Context(Request);
        await BridgePump(bridge, model, () => model.Status == AutomaticUpdateStatus.Idle);
        var waitReady = mode.StartsWith("ready-", StringComparison.Ordinal);
        await bridge.Save(download, !waitReady); model.Preferences(download, !waitReady);
        if (!download) model.Download(Now);
        if (waitReady) {
            await BridgePump(bridge, model, () => model.Status == AutomaticUpdateStatus.Ready);
            Check(handoff.PreparedBeforeMutation && handoff.Stage != null, "TP02 Ready boundary follows actual Prepare");
            MutateReceipt(root, mode, handoff.Stage!);
            a.Value = true; config.Save(); // Production external SettingChanged -> Poll -> Queue path.
        }
        if (mode.StartsWith("queued-", StringComparison.Ordinal)) {
            await BridgePump(bridge, model, () => model.Status == AutomaticUpdateStatus.Queued);
            Check(handoff.PreparedBeforeMutation && File.Exists(Path.Combine(root, handoff.Stage!, "helper.ready")), "TP02 mutation after authenticated ready");
            MutateReceipt(root, mode, handoff.Stage!);
            Console.WriteLine("PASS: TP02 child " + mode + " queued natural exit; " + _checks + " assertions");
            return 0; // Preserve opted-in helper, exit naturally without cancellation.
        }
        var integrity = mode.Contains("-linked-", StringComparison.Ordinal) || mode.Contains("-corrupt-", StringComparison.Ordinal);
        await BridgePump(bridge, model, () => model.Status == (integrity ? AutomaticUpdateStatus.FaultDisabled : AutomaticUpdateStatus.ManualUpgradeRequired));
        await BridgePump(bridge, model, () => {
            using var fence = new WindowsFileFence(root);
            return UpdateFaults.ReadFile(fence, UpdateFaults.OperationPath) == null && (!integrity || model.Fault != null && !d.Value && !a.Value);
        });
        Check(handoff.PreparedBeforeMutation && File.Exists(Path.Combine(root, "receipt-boundary.marker")), "TP02 receipt changed only after actual Prepare");
        Check(handoff.Observer != null && handoff.Observer.Failure == integrity, "TP02 prerequisite never emits failure IPC; integrity does");
        Check(model.AutoDownload == (integrity ? false : download) && model.ApplyAfterExit == !integrity, "TP02 runtime settings policy");
        Check(d.Value == (integrity ? false : download) && a.Value == !integrity, "TP02 real ConfigEntry policy");
        var reload = new ConfigFile(Path.Combine(root, "preferences.cfg"), false);
        Check(reload.Bind("Update", "Download", true).Value == (integrity ? false : download) && reload.Bind("Update", "Apply", true).Value == !integrity, "TP02 reloaded disk settings policy");
        Check(!File.Exists(Path.Combine(root, handoff.Stage!, "helper.ready")), "TP02 real helper stopped before ready");
        using (var fence = new WindowsFileFence(root)) Check((UpdateFaults.ReadFile(fence, UpdateFaults.FailurePath) != null) == integrity, "TP02 durable fault classification");
        Console.WriteLine("PASS: TP02 child " + mode + " " + model.Status + "; " + _checks + " assertions");
        model.Dispose(); return 0;
    }
    private static async Task ReceiptBoundaryIntegration(string baseRoot, Package package, byte[] inert, string helper) {
        var binary = Path.GetDirectoryName(typeof(HelperFixture).Assembly.Location)!;
        package.RebindGame(File.ReadAllBytes(Path.Combine(binary, "BetterAstralParty.HelperTests.exe")));
        File.WriteAllBytes(Path.Combine(baseRoot, "descriptor.bin"), package.Descriptor); File.WriteAllBytes(Path.Combine(baseRoot, "signature.envelope"), package.Envelope); File.WriteAllBytes(Path.Combine(baseRoot, "package.zip"), package.Zip);
        var fixtureIndex = 0;
        foreach (var mode in new[] { "prepare-delete-on", "prepare-unsupported-on", "prepare-delete-off", "prepare-unsupported-off", "ready-delete-on", "ready-unsupported-off", "prepare-corrupt-on", "prepare-linked-on", "queued-delete-on", "queued-unsupported-off", "queued-mismatch-on" }) {
            // Keep the longest pinned staging/owned path below the product's MAX_PATH fence.
            var root = Install(baseRoot, "r" + fixtureIndex++, package, inert);
            File.Copy(Path.Combine(binary, "BetterAstralParty.HelperTests.exe"), Path.Combine(root, "AstralParty_INT.exe"), true);
            foreach (var file in Directory.GetFiles(binary)) if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            File.Copy(helper, Path.Combine(root, "BetterAstralParty-UpdateHelper.exe"));
            var start = new ProcessStartInfo(Path.Combine(root, "AstralParty_INT.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("--receipt-backend"); start.ArgumentList.Add(root); start.ArgumentList.Add(mode);
            using (var child = Process.Start(start)!) {
                await child.WaitForExitAsync(); var error = await child.StandardError.ReadToEndAsync(); var output = await child.StandardOutput.ReadToEndAsync();
                File.WriteAllText(Path.Combine(root, "child.log"), output + error); Check(child.ExitCode == 0, "TP02 actual coordinator child " + mode + " " + error); Console.Write(output);
            }
            var integrity = mode.Contains("-linked-", StringComparison.Ordinal) || mode.Contains("-corrupt-", StringComparison.Ordinal) || mode.Contains("-mismatch-", StringComparison.Ordinal);
            var stage = WorkDirectories(root, "stage").Single(path => File.Exists(Path.Combine(path, "handoff.ticket")));
            var result = Path.Combine(stage, "helper.result"); string? text = null;
            for (var i = 0; i < 500 && text == null; i++) { try { if (File.Exists(result)) text = File.ReadAllText(result); } catch (IOException) { } if (text == null) await Task.Delay(20); }
            var ticketHash = UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(stage, "handoff.ticket")));
            Check(text == ticketHash + "\n" + (integrity ? "FaultDisabled" : "ManualUpgradeRequired") + "\n", "TP02 durable ticket-bound helper result " + mode);
            foreach (var file in package.Old) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, file.Key))) == UpdateTrust.Hash(file.Value), "TP02 " + InstallReceipt.Owned.Length + " owned targets preserved " + mode);
            Check(File.ReadAllText(Path.Combine(root, "other-mod.keep")) == "unchanged" && !WorkDirectories(root, "txn").Any(), "TP02 unrelated files/no transaction mutation " + mode);
            using (var fence = new WindowsFileFence(root)) {
                Check((UpdateFaults.ReadFile(fence, UpdateFaults.FailurePath) != null) == integrity, "TP02 parent persistent classification " + mode);
                Check(UpdateFaults.ReadFile(fence, UpdateFaults.OperationPath) == null, "TP02 completed helper guard retired " + mode);
            }
            if (!integrity) for (var attempt = 0; attempt < 2; attempt++) {
                using var replay = Start(helper, "--apply \"" + root + "\" " + WorkRelative(root, stage) + " \"" + Path.Combine(baseRoot, "fixture-public-key.bin") + "\"");
                await replay.WaitForExitAsync(); Check(replay.ExitCode == 4, "TP02 real Framework helper exit 4/idempotent manual replay");
            }
            if (mode.StartsWith("queued-", StringComparison.Ordinal) && integrity) {
                var config = new ConfigFile(Path.Combine(root, "preferences.cfg"), false); var d = config.Bind("Update", "Download", true); var a = config.Bind("Update", "Apply", true);
                using var bridge = new UpdatePreferencesBridge(config, d, a); using var restarted = new AutomaticUpdates(UpdateDownloads.Production(), new Handoff(), new FileUpdateSafety(root, bridge.Save));
                restarted.Configure(null); await BridgePump(bridge, restarted, () => restarted.Status == AutomaticUpdateStatus.FaultDisabled && !d.Value && !a.Value);
            }
            var reload = new ConfigFile(Path.Combine(root, "preferences.cfg"), false);
            Check(reload.Bind("Update", "Download", true).Value == (!integrity && mode.EndsWith("-on", StringComparison.Ordinal)) && reload.Bind("Update", "Apply", true).Value == !integrity, "TP02 parent reload saved ON/OFF policy " + mode);
        }
        package.RebindGame(inert);
        Console.WriteLine("PASS: TP02 after-Prepare/Ready/queued receipt boundaries, real coordinator + Framework helper, saved OFF, integrity guards.");
    }
}
