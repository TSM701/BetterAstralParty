using System.ComponentModel;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using BetterAstralParty;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Observability;
using BetterAstralParty.Updating;

internal static partial class HelperFixture
{
    private sealed class ObservedStartHandoff(UpdateHandoff actual) : IUpdateHandoff
    {
        internal Exception? QueueError;
        public bool Configured => actual.Configured;
        public Task<object> Prepare(DownloadedUpdate update, UpdateContext context, CancellationToken token) => actual.Prepare(update, context, token);
        public async Task<object> Queue(object prepared, CancellationToken token) {
            try { return await actual.Queue(prepared, token); }
            catch (Exception error) { QueueError = error; throw; }
        }
        public void SignalCancel(object prepared) => actual.SignalCancel(prepared);
        public void SignalFailure(object prepared) => actual.SignalFailure(prepared);
        public Task Cancel(object prepared) => actual.Cancel(prepared);
        public Task<string?> Inspect() => actual.Inspect();
    }
    private static string StartBoundaryRoot(string path)
    {
        var allowed = Path.GetFullPath(Environment.GetEnvironmentVariable("BAP_HELPER_FIXTURE_ROOT") ?? throw new InvalidOperationException("Inert fixture boundary missing."));
        var root = Path.GetFullPath(path);
        if (!root.StartsWith(allowed.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only an inert fixture descendant is allowed.");
        return root;
    }
    private static int StartBoundarySetup(string[] args)
    {
        var root = StartBoundaryRoot(args[1]);
        if (Directory.Exists(root)) throw new InvalidOperationException("Fresh inert root required.");
        var parent = Path.GetDirectoryName(root)!;
        using var package = new Package(File.ReadAllBytes(args[2]), File.ReadAllBytes(args[3]), File.ReadAllBytes(args[4]));
        Install(parent, Path.GetFileName(root), package, File.ReadAllBytes(args[4]));
        File.WriteAllBytes(Path.Combine(parent, "fixture-public-key.bin"), package.Public);
        File.WriteAllBytes(Path.Combine(parent, "descriptor.bin"), package.Descriptor);
        File.WriteAllBytes(Path.Combine(parent, "signature.envelope"), package.Envelope);
        File.WriteAllBytes(Path.Combine(parent, "package.zip"), package.Zip);
        File.Copy(args[5], Path.Combine(root, "BetterAstralParty-UpdateHelper.exe"));
        if (args[6].StartsWith("invalid-image", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(root, "BetterAstralParty-UpdateHelper.exe"), "INERT INVALID EXECUTABLE - MUST NEVER RUN");
        return 0;
    }
    private static bool StartEvent(string lines, string phase, int native, string? errorType = null)
    {
        foreach (var line in lines.Split('\n')) {
            if (!line.StartsWith("{", StringComparison.Ordinal)) continue;
            using var row = JsonDocument.Parse(line);
            var item = row.RootElement;
            if (item.GetProperty("kind").GetString() == "Failure" && item.GetProperty("phase").GetString() == phase && item.GetProperty("native_code").GetInt32() == native
                && (errorType == null || item.GetProperty("error_type").GetString() == errorType)) return true;
        }
        return false;
    }
    private static async Task<int> StartBoundaryHost(string[] args)
    {
        var root = StartBoundaryRoot(args[1]); var mode = args[2];
        if (Path.GetFullPath(Environment.ProcessPath!) != Path.Combine(root, "AstralParty_INT.exe"))
            throw new InvalidOperationException("The actor must be the generated inert game host.");
        if (mode is not ("ready-cancel" or "queued-cancel" or "prestart-cancel" or "invalid-image" or "invalid-image-legacy" or "injected-start" or "cleanup-busy"))
            throw new InvalidOperationException("Unknown inert start boundary.");
        var parent = Path.GetDirectoryName(root)!;
        var trust = new UpdateTrust(new[] { new TrustedUpdateKey("helper-fixture", new() { Modulus = File.ReadAllBytes(Path.Combine(parent, "fixture-public-key.bin")), Exponent = [1, 0, 1] }) });
        var owned = InstallReceipt.Owned.ToDictionary(path => path, path => UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, path))));
        var helperPath = Path.Combine(root, "BetterAstralParty-UpdateHelper.exe");
        var actual = new UpdateHandoff(root, trust, UpdateTrust.Hash(File.ReadAllBytes(helperPath)));
        var handoff = new ObservedStartHandoff(actual);
        var config = new BepInEx.Configuration.ConfigFile(Path.Combine(root, "preferences.cfg"), true) { SaveOnConfigSet = false };
        var download = config.Bind("Update", "Download", false); var apply = config.Bind("Update", "Apply", false);
        Task Save(bool d, bool a) { download.Value = d; apply.Value = a; config.Save(); return Task.CompletedTask; }
        var model = new AutomaticUpdates(new UpdateDownloads(trust, new Auth(), new BytesHandler(File.ReadAllBytes(Path.Combine(parent, "descriptor.bin")),
            File.ReadAllBytes(Path.Combine(parent, "signature.envelope")), File.ReadAllBytes(Path.Combine(parent, "package.zip")))), handoff, new FileUpdateSafety(root, Save));
        using var diagnostics = DiagnosticOwner.Start(root, false, "1.3.0-dev", new string('A', 32));
        Check(DiagnosticHub.Bind(diagnostics), "ordinary start fixture owns its diagnostic writer");
        var hook = typeof(UpdateHandoff).GetField("BeforeHelperStart", BindingFlags.Static | BindingFlags.NonPublic);
        WindowsFileFence? heldFence = null; WindowsFileFence.FileLease? heldTicket = null;
        var startNative = 0;
        Exception original = mode == "prestart-cancel" ? new OperationCanceledException("INERT_PRIVATE_SENTINEL_DO_NOT_COPY - synthetic pre-start cancellation")
            : new IOException("INERT_PRIVATE_SENTINEL_DO_NOT_COPY - synthetic ordinary start failure");
        try {
            if (mode is "injected-start" or "cleanup-busy" or "prestart-cancel") {
                if (hook == null) throw new InvalidOperationException("Reviewed start fixture hook missing.");
                hook.SetValue(null, (Action)(() => {
                    if (mode == "cleanup-busy") {
                        var stage = WorkRelative(root, WorkDirectories(root, "stage").Single(path => File.Exists(Path.Combine(path, "handoff.ticket"))));
                        heldFence = new WindowsFileFence(root); heldTicket = heldFence.OpenFile(stage + "/handoff.ticket", true);
                    }
                    throw original;
                }));
            }
            model.Preferences(false, false); model.Configure(new Auth()); model.Context(Request); await Wait(model, AutomaticUpdateStatus.Idle);
            await Save(true, mode != "ready-cancel"); model.Preferences(true, mode != "ready-cancel");
            if (mode is "ready-cancel" or "queued-cancel") {
                await Wait(model, mode == "ready-cancel" ? AutomaticUpdateStatus.Ready : AutomaticUpdateStatus.Queued);
                model.Cancel(); await Wait(model, AutomaticUpdateStatus.Cancelled);
            } else if (mode == "prestart-cancel") {
                await Wait(model, AutomaticUpdateStatus.Cancelled);
                Check(ReferenceEquals(handoff.QueueError, original), "ordinary pre-start cancellation preserves the identical OperationCanceledException");
                Check(model.Fault == null && model.AutoDownload && model.ApplyAfterExit && download.Value && apply.Value, "ordinary pre-start cancellation is not a hard failure and preserves opted-in preferences");
            } else {
                await Wait(model, AutomaticUpdateStatus.FaultDisabled);
                await Pump(model, () => model.Fault != null && !model.CanCancel && model.CanRecheck);
                startNative = handoff.QueueError is Win32Exception observedNative ? observedNative.NativeErrorCode : 0;
                Console.WriteLine("QUEUE-ERROR-TYPE:" + (handoff.QueueError?.GetType().FullName ?? "none") + "; native=" + startNative);
                if (mode.StartsWith("invalid-image", StringComparison.Ordinal))
                    Check(handoff.QueueError is Win32Exception && startNative is 193 or 216, "actual ordinary Process.Start preserves a Windows bad-image/machine-type error, not an arbitrary rejection");
                else Check(ReferenceEquals(handoff.QueueError, original), "ordinary queue preserves the identical original IOException rather than cleanup errors");
                Check(!download.Value && !apply.Value && !model.AutoDownload && !model.ApplyAfterExit, "ordinary hard failure persists both OFF preferences through actual coordinator");
            }
            var directory = WorkDirectories(root, "stage").Single(path => File.Exists(Path.Combine(path, "handoff.ticket")));
            if (mode == "cleanup-busy") heldTicket?.Dispose();
            var ticketHash = UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(directory, "handoff.ticket")));
            if (mode == "queued-cancel") Check(SpinWait.SpinUntil(() => !File.Exists(Path.Combine(root, UpdateFaults.OperationPath))
                && File.Exists(Path.Combine(directory, "helper.result")), 5000), "queued ordinary helper exits naturally and retires its cancellation before inspection");
            using (var fence = new WindowsFileFence(root)) {
                var operation = UpdateFaults.ReadFile(fence, UpdateFaults.OperationPath);
                if (mode is "invalid-image-legacy" or "cleanup-busy") {
                    Check(operation != null, "uncertain cleanup or original legacy bug retains the apply operation");
                    Check(!File.Exists(Path.Combine(directory, "helper.result")), "uncertain cleanup never publishes a false terminal cancellation");
                } else {
                    Check(operation == null, "proven ordinary cancellation or no-child failure retires its exact apply operation");
                    Check(SpinWait.SpinUntil(() => File.Exists(Path.Combine(directory, "helper.result")), 5000), "ordinary boundary publishes durable terminal result");
                    Check(File.ReadAllText(Path.Combine(directory, "helper.result")) == ticketHash + "\nCancelled\n", "ordinary terminal result binds the exact ticket hash");
                    UpdateLaunchGate.Check(fence);
                    if (mode is "ready-cancel" or "queued-cancel" or "prestart-cancel") {
                        var cleaned = UpdateRetention.Clean(fence, trust);
                        Check(cleaned.RemovedPackages == 1, "ordinary cancellation duplicate ZIP can be retired under signed retention rules");
                    }
                }
            }
            foreach (var row in owned) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, row.Key))) == row.Value, "ordinary start boundary preserves owned file " + row.Key);
            Check(File.ReadAllText(Path.Combine(root, "other-mod.keep")) == "unchanged" && !WorkDirectories(root, "txn").Any(), "ordinary start boundary preserves unrelated mod and has no target transaction");
            Check(SpinWait.SpinUntil(() => diagnostics.Recording || diagnostics.StorageFailed, 5000)
                && SpinWait.SpinUntil(() => diagnostics.FlushBoundary(50), 5000), "ordinary boundary source diagnostic writer flushes");
            var bundle = DiagnosticBundle.Collect(root, Path.Combine(parent, "absent-local"), Path.Combine(parent, "absent-fallback"), Path.Combine(parent, "absent-unity"),
                Path.Combine(parent, "zip-" + mode), Path.Combine(parent, "fallback-" + mode), "", "");
            using var zip = ZipFile.OpenRead(bundle.ArchivePath);
            var lines = string.Join("\n", zip.Entries.Where(entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }));
            Check(!lines.Contains("INERT_PRIVATE_SENTINEL") && !lines.Contains(root) && !lines.Contains("fixture-placeholder-only"), "ordinary source-to-ZIP excludes exception messages, personal paths and credentials");
            if (mode == "invalid-image") Check(StartEvent(lines, "HelperQueue", startNative), "ordinary bad-image original native exception survives source-to-ZIP");
            if (mode is "injected-start" or "cleanup-busy") Check(StartEvent(lines, "HelperQueue", 0, "IOException"), "ordinary original IOException survives source-to-ZIP");
            if (mode == "cleanup-busy") {
                Check(StartEvent(lines, "Recovery", 32, "ApplySafetyException"), "ordinary source-to-ZIP retains the exact native sharing violation from the shared cancellation kernel");
                Check(StartEvent(lines, "Recovery", 0, "ApplySafetyException"), "ordinary source-to-ZIP retains secondary uncertain cleanup separately from the original IOException");
            }
            if (mode is "ready-cancel" or "queued-cancel" or "prestart-cancel") Check(lines.Contains("\"update_state\":\"Cancelled\""), "ordinary cancellation status survives source-to-ZIP");
            if (mode == "prestart-cancel") Check(lines.Contains("\"phase\":\"HelperQueue\",\"outcome\":\"Cancelled\""), "ordinary pre-start cancellation retains its exact non-failure queue phase in ZIP");
            Console.WriteLine((mode == "invalid-image-legacy" ? "LEGACY-DEFECT-CONFIRMED " : "PASS ") + mode + ": " + _checks + " checks; " + bundle.ArchivePath);
            return 0;
        } finally { hook?.SetValue(null, null); heldTicket?.Dispose(); heldFence?.Dispose(); model.Dispose(); DiagnosticHub.Unbind(diagnostics); }
    }
}
