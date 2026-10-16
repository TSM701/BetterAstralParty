#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Observability;
using BetterAstralParty.Updating;

namespace BetterAstralParty { internal static class Plugin { public const string Guid = "kr.betterastralparty.mod", Version = "1.3.0", UpdateProtocol = "4", SettingsSchema = "1"; } }
internal static class FixtureEntry
{
    private static string _base = "", _inputs = ""; private static int _cases, _checks;
    private static readonly List<string> Passed = new List<string>();
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); _checks++; Passed.Add(name); }
    private static void Reject(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
    private static void Group(string name, Action action)
    {
        Console.WriteLine("GROUP BEGIN " + name + " cases=" + _cases + " checks=" + _checks); Console.Out.Flush();
        action();
        Console.WriteLine("GROUP END " + name + " cases=" + _cases + " checks=" + _checks); Console.Out.Flush();
    }
    private static void RunUi(LauncherRecoveryForm form, Action assertions)
    {
        ExceptionDispatchInfo? failure = null;
        form.Shown += (_, __) => form.BeginInvoke(new Action(() => {
            try { VerifyUi(form, "message-loop entry"); assertions(); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
            finally { form.Close(); }
        }));
        System.Windows.Forms.Application.Run(form);
        failure?.Throw();
    }
    private static void VerifyUi(System.Windows.Forms.Control control, string stage) => Check(System.Windows.Forms.Application.MessageLoop
        && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && SynchronizationContext.Current is System.Windows.Forms.WindowsFormsSynchronizationContext && !control.InvokeRequired,
        "recovery UI standard message loop; stage=" + stage + "; thread=" + Thread.CurrentThread.ManagedThreadId + "; apartment=" + Thread.CurrentThread.GetApartmentState()
        + "; loop=" + System.Windows.Forms.Application.MessageLoop + "; context=" + SynchronizationContext.Current?.GetType().FullName + "; invokeRequired=" + control.InvokeRequired);
    private static void PumpUi(Func<bool> complete, Func<string> state, string operation)
    {
        var due = DateTime.UtcNow.AddSeconds(5);
        do {
            System.Windows.Forms.Application.DoEvents();
            if (complete()) return;
            if (DateTime.UtcNow > due) throw new TimeoutException(operation + ": " + state());
            Thread.Sleep(5);
        } while (true);
    }
    private static string Full(string root, string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
    private static string[] StagePaths(string root) { using var fence = new WindowsFileFence(root); return UpdateWorkPath.Directories(fence, "stage").Select(relative => Full(root, relative)).ToArray(); }
    private static byte[] Input(string path) => File.ReadAllBytes(Path.Combine(_inputs, path));
    private static string Seed(string? root = null)
    {
        root ??= Path.Combine(_base, "c" + _cases++); Directory.CreateDirectory(Full(root, "BepInEx/plugins/BetterAstralParty")); Directory.CreateDirectory(Full(root, "BepInEx/config")); Directory.CreateDirectory(Full(root, "fixture-cache"));
        foreach (var path in InstallReceipt.Owned) {
            var destination = Full(root, path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (path == UpdateTrust.PluginPath || path == "BetterAstralParty-Launcher.exe") { if (Path.GetFullPath(destination) != typeof(FixtureEntry).Assembly.Location) File.Copy(typeof(FixtureEntry).Assembly.Location, destination); }
            else File.WriteAllBytes(destination, Input("owned/" + path));
        }
        foreach (var path in Directory.GetFiles(Path.Combine(_inputs, "game"), "*", SearchOption.AllDirectories)) {
            var destination = Full(root, path.Substring(Path.Combine(_inputs, "game").Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination);
        }
        File.WriteAllText(Full(root, ChannelTransitionIntent.SettingsPath), "[일반]\r\n언어 = 한국어\r\n업데이트 채널 = Stable\r\n[업데이트]\r\n자동 다운로드 = false\r\n종료 후 적용 = false\r\n", new UTF8Encoding(false));
        Directory.CreateDirectory(Full(root, "BepInEx/plugins/OtherMod"));
        File.WriteAllText(Full(root, "BepInEx/plugins/OtherMod/OtherMod.dll"), "inert other mod");
        File.WriteAllText(Full(root, "doorstop_config.ini"), "[General]\nenabled=true\nother=preserved\n");
        File.WriteAllText(Full(root, "retained-backup.keep"), "inert retained backup");
        using var fence = new WindowsFileFence(root);
        var state = new ReceiptChannelState(fence.RootIdentity.Text, "Stable", Guid.NewGuid().ToString("N"), ReleaseFeedPolicy.StableRepositoryId, "1.3.0", new string('A', 64), 0, null, "-");
        var receipt = "{\"schema\":1,\"baseline\":\"1.3.0\",\"owner\":\"kr.betterastralparty.mod\",\"loaderBefore\":false,\"files\":["
            + string.Join(",", InstallReceipt.Owned.Select(path => "{\"path\":\"" + path + "\",\"sha256\":\"" + UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) + "\"}")) + "],\"channelState\":\"" + state.Encoded + "\"}";
        File.WriteAllBytes(Full(root, InstallReceipt.Path), Bytes(receipt));
        var descriptor = Input("old-helper.descriptor"); var signature = Input("old-helper.signature"); var image = Input("old-helper.image");
        var verified = HelperDescriptor.Verify(UpdateTrust.Production(), descriptor, DetachedSignature.Parse(signature), image);
        var work = UpdateWorkPath.New("helper"); var identity = UpdateWorkPath.Create(fence, work).Text;
        HelperDelivery.Write(fence, work + "/descriptor", descriptor); HelperDelivery.Write(fence, work + "/signature", signature); HelperDelivery.Write(fence, work + "/image", image);
        string imageId; using (var file = fence.OpenFile(work + "/image")) imageId = file.Identity.Text;
        HelperDelivery.Write(fence, work + "/receipt", HelperDelivery.WholeReceiptBytes(fence, verified, imageId));
        string receiptId; using (var file = fence.OpenFile(work + "/receipt")) receiptId = file.Identity.Text;
        HelperDelivery.Write(fence, work + "/plan", Bytes("BetterAstralParty.HelperInstall/v1\nroot=" + fence.RootIdentity.Text + "\nwork=" + work + "\nwork-id=" + identity + "\ndescriptor=" + verified.DescriptorHash + "\nimage-id=" + imageId + "\nreceipt-id=" + receiptId + "\n"));
        HelperDelivery.FinishWholeMigration(fence, work, UpdateTrust.Production(), null);
        return root;
    }
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => UpdateTrust.Hash(File.ReadAllBytes(p)));
    private static void Unchanged(string root, Dictionary<string, string> before, string label) => Check(before.OrderBy(p => p.Key).SequenceEqual(Snapshot(root).OrderBy(p => p.Key)), label + " preserves every file");
    private sealed class Fake : HttpMessageHandler
    {
        internal string Mode = "ready"; internal int Calls, Credentials; internal bool Beta, PreRelease, WrongPurpose; internal Action? BeforeRequest; internal Action<HttpRequestMessage>? OnRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; if (request.Headers.Authorization != null) Credentials++;
            BeforeRequest?.Invoke();
            OnRequest?.Invoke(request);
            var path = request.RequestUri!.AbsolutePath; var status = HttpStatusCode.OK; byte[] bytes;
            if (Mode == "network") throw new HttpRequestException("inert transport failure");
            if (Mode == "denied") status = HttpStatusCode.Forbidden;
            if (Mode == "limited") status = (HttpStatusCode)429;
            if (path.Contains("/releases/tags/") || path.EndsWith("/releases/123", StringComparison.Ordinal)) {
                var metadata = File.ReadAllText(Path.Combine(_inputs, "releases.json")).Trim();
                if (PreRelease) metadata = System.Text.RegularExpressions.Regex.Replace(metadata, "(\"prerelease\"\\s*:\\s*)false", "$1true");
                if (Mode == "missing") metadata = metadata.Replace(ReleaseFeedPolicy.UpdateManifestAsset, "missing-manifest");
                bytes = Bytes(metadata.Substring(1, metadata.Length - 2));
                if (Mode == "wrong-installed-tag") bytes = Bytes(Encoding.UTF8.GetString(bytes).Replace("1.3.1", "1.4.0"));
            } else if (path.EndsWith("/releases", StringComparison.Ordinal)) {
                var metadata = File.ReadAllText(Path.Combine(_inputs, "releases.json"));
                if (PreRelease) metadata = System.Text.RegularExpressions.Regex.Replace(metadata, "(\"prerelease\"\\s*:\\s*)false", "$1true");
                if (Mode == "none") metadata = metadata.Replace("1.3.1", "1.3.0");
                if (Mode == "missing") metadata = metadata.Replace(ReleaseFeedPolicy.UpdateManifestAsset, "missing-manifest");
                bytes = Bytes(metadata);
            } else if (path.Contains("/releases/assets/")) {
                var id = path.Substring(path.LastIndexOf('/') + 1);
                var name = id == "456" ? "payload.zip" : id == "457" ? "update.descriptor" : id == "458" ? "update.signature" : id == "459" ? "helper.descriptor" : id == "460" ? "helper.signature" : "helper.image";
                bytes = Input(name);
                if (Beta && (name == "update.descriptor" || name == "update.signature")) bytes = Input("beta-" + name);
                if (PreRelease && !WrongPurpose && (name == "update.descriptor" || name == "update.signature")) bytes = Input("pre-" + name);
                if (WrongPurpose && (name == "update.descriptor" || name == "update.signature")) bytes = Input("narrow-" + name);
                if (Mode == "signature" && name == "update.signature") bytes = Input("old-helper.signature");
                if (Mode == "zip" && name == "payload.zip") { bytes[100] ^= 1; }
                if (Mode == "old-helper") {
                    if (name == "helper.descriptor") bytes = Input("old-helper.descriptor");
                    if (name == "helper.signature") bytes = Input("old-helper.signature");
                    if (name == "helper.image") bytes = Input("old-helper.image");
                }
            } else bytes = Bytes("{\"id\":" + (Beta ? ReleaseFeedPolicy.BetaRepositoryId : ReleaseFeedPolicy.StableRepositoryId) + ",\"full_name\":\"" + (Beta ? ReleaseFeedPolicy.BetaRepository : ReleaseFeedPolicy.StableRepository) + "\",\"private\":" + (Beta ? "true" : "false") + "}");
            var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(bytes) };
            if (Mode == "redirect") { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new Uri("https://untrusted.invalid/asset"); }
            if (Mode == "limited") response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(response);
        }
    }
    private static void InstalledHelperCompletion()
    {
        foreach (var mode in new[] { "ready", "demoted", "promoted", "promoted-descriptor-binding", "download-off", "apply-off", "fault", "signature", "descriptor-binding", "wrong-installed-tag", "network", "missing", "old-helper" }) {
            var root = Seed();
            var active = mode.StartsWith("promoted", StringComparison.Ordinal) ? "PreRelease" : "Stable";
            var succeeds = mode == "ready" || mode == "demoted" || mode == "promoted";
            foreach (var path in InstallReceipt.Owned) File.WriteAllBytes(Full(root, path), Input("owned/" + path));
            File.WriteAllText(Full(root, ChannelTransitionIntent.SettingsPath), "[일반]\n언어=한국어\n업데이트 채널=" + active + "\n[업데이트]\n자동 다운로드="
                + (mode == "download-off" ? "false" : "true") + "\n종료 후 적용=" + (mode == "apply-off" ? "false" : "true") + "\n", new UTF8Encoding(false));
            using (var fence = new WindowsFileFence(root)) {
                var digest = mode.EndsWith("descriptor-binding", StringComparison.Ordinal) ? new string('A', 64) : UpdateTrust.Hash(Input("update.descriptor"));
                var state = new ReceiptChannelState(fence.RootIdentity.Text, active, Guid.NewGuid().ToString("N"), active == "Stable" ? ReleaseFeedPolicy.StableRepositoryId : 0, active == "Stable" ? "1.3.1" : null,
                    active == "Stable" ? digest : "-", 0, null, "-", active == "PreRelease" ? ReleaseFeedPolicy.StableRepositoryId : 0, active == "PreRelease" ? "1.3.1" : null, active == "PreRelease" ? digest : "-");
                var receipt = "{\"schema\":1,\"baseline\":\"1.3.0\",\"owner\":\"kr.betterastralparty.mod\",\"loaderBefore\":false,\"files\":["
                    + string.Join(",", InstallReceipt.Owned.Select(path => "{\"path\":\"" + path + "\",\"sha256\":\"" + UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) + "\"}"))
                    + "],\"channelState\":\"" + state.Encoded + "\"}";
                File.WriteAllBytes(Full(root, InstallReceipt.Path), Bytes(receipt));
                if (mode == "fault") UpdateFaults.Fail(fence, new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "1.3.1", "1.4.0", 123, 456,
                    UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId), FaultPhase.Download, FaultReason.Network, null);
            }
            var before = Snapshot(root); var fake = new Fake { Mode = mode, PreRelease = mode == "demoted" }; var credentialCalls = 0;
            using var client = new LauncherRecoveryClient(root, fake, fixtureCredential: () => { credentialCalls++; return null; });
            var rejected = false;
            try { client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult(); }
            catch { rejected = true; }
            var off = mode == "download-off" || mode == "apply-off" || mode == "fault";
            Check(rejected == (!off && !succeeds), "ordinary installed helper completion outcome: " + mode);
            Check(credentialCalls == 0 && fake.Credentials == 0, "ordinary Stable helper completion never reads or sends private credentials: " + mode);
            foreach (var row in before.Where(row => row.Key != Full(root, HelperDescriptor.Image) && row.Key != Full(root, HelperDelivery.Receipt)))
                Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "ordinary helper completion preserves prior owned/history/settings/other-mod bytes: " + mode);
            using var verifiedFence = new WindowsFileFence(root);
            var hash = HelperDelivery.Fingerprint(verifiedFence, UpdateTrust.Production(), 4);
            Check(hash == UpdateTrust.Hash(Input(succeeds ? "helper.image" : "old-helper.image")), "ordinary helper completion changes only the signed eligible helper: " + mode);
            if (off) { Check(fake.Calls == 0, "saved OFF or a failure latch defers helper completion without HTTP: " + mode); Unchanged(root, before, "deferred ordinary helper completion: " + mode); }
            if (succeeds) {
                Check(fake.Calls == 7, "ordinary next launch fetches one exact installed tag and only its signed helper assets, not a new update ZIP");
                var completed = Snapshot(root);
                client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult();
                Check(fake.Calls == 7, "ordinary helper completion is idempotent with no further HTTP");
                Unchanged(root, completed, "already current helper completion");
            }
        }
    }
    private static void BetaInstalledHelperCompletion()
    {
        foreach (var authenticated in new[] { false, true }) {
            var root = Seed();
            foreach (var path in InstallReceipt.Owned) File.WriteAllBytes(Full(root, path), Input("owned/" + path));
            File.WriteAllText(Full(root, ChannelTransitionIntent.SettingsPath), "[일반]\n업데이트 채널=Stable\n[업데이트]\n자동 다운로드=true\n종료 후 적용=true\n", new UTF8Encoding(false));
            using (var fence = new WindowsFileFence(root)) {
                var state = new ReceiptChannelState(fence.RootIdentity.Text, "Beta", Guid.NewGuid().ToString("N"), 0, null, "-", ReleaseFeedPolicy.BetaRepositoryId,
                    "1.3.1", UpdateTrust.Hash(Input("beta-update.descriptor")));
                var receipt = "{\"schema\":1,\"baseline\":\"1.3.0\",\"owner\":\"kr.betterastralparty.mod\",\"loaderBefore\":false,\"files\":["
                    + string.Join(",", InstallReceipt.Owned.Select(path => "{\"path\":\"" + path + "\",\"sha256\":\"" + UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) + "\"}"))
                    + "],\"channelState\":\"" + state.Encoded + "\"}";
                File.WriteAllBytes(Full(root, InstallReceipt.Path), Bytes(receipt));
            }
            var before = Snapshot(root); var fake = new Fake { Beta = true }; var credentials = 0;
            using var client = new LauncherRecoveryClient(root, fake, fixtureCredential: () => { credentials++; return authenticated ? "github_pat_INERT_TEST_ONLY_DO_NOT_USE_" + new string('x', 40) : null; });
            if (!authenticated) {
                Reject(() => client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult(), "active Beta helper completion requires credentials even with selected Stable preference");
                Check(fake.Calls == 0 && fake.Credentials == 0 && credentials == 1, "missing private authentication never falls back to Stable or sends HTTP");
                foreach (var row in before) Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "missing Beta authentication preserves all previous bytes");
            } else {
                client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult();
                Check(fake.Calls == 7 && fake.Credentials == 7 && credentials == 1, "installed Beta uses its exact private identity/tag/assets, not selected Stable");
                using var fence = new WindowsFileFence(root);
                Check(HelperDelivery.Fingerprint(fence, UpdateTrust.Production(), 4) == UpdateTrust.Hash(Input("helper.image")), "Beta next-launch helper has the exact verified signed image");
                foreach (var row in before.Where(row => row.Key != Full(root, HelperDescriptor.Image) && row.Key != Full(root, HelperDelivery.Receipt)))
                    Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "Beta helper completion leaves channel preference, receipt, other mods and history unchanged");
                client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult();
                Check(fake.Calls == 7 && credentials == 1, "completed Beta helper does not reopen private credentials or HTTP");
            }
        }
    }
    private static void PendingHelperEntrypoints()
    {
        var points = new List<string>(); var probe = Seed();
        using (var lease = new UpdateLaunchGate.Lease(probe, fence => AutomaticHelperUpgrade.Ensure(fence, Input("helper.descriptor"), Input("helper.signature"), Input("helper.image"),
            UpdateTrust.Production(), hit => points.Add(hit)))) { }
        Check(points.Count != 0 && points.Distinct(StringComparer.Ordinal).Count() == points.Count, "pending helper entrypoint boundaries are derived from the exact successful production path");
        foreach (var entrypoint in new[] { "Check", "CompleteInstalledHelper" }) foreach (var point in points) {
            var root = Seed();
            if (entrypoint == "Check") File.WriteAllText(Full(root, ChannelTransitionIntent.SettingsPath), "[일반]\n업데이트 채널=Beta\n", new UTF8Encoding(false));
            var before = Snapshot(root);
            var interrupted = false;
            try { using var lease = new UpdateLaunchGate.Lease(root, fence => AutomaticHelperUpgrade.Ensure(fence, Input("helper.descriptor"), Input("helper.signature"), Input("helper.image"),
                UpdateTrust.Production(), hit => { if (hit == point) throw new IOException("inert helper boundary " + point); })); }
            catch (IOException) { interrupted = true; }
            Check(interrupted, "pending helper exact interruption reached: " + entrypoint + "/" + point);
            using (var fence = new WindowsFileFence(root)) {
                var pending = AutomaticHelperUpgrade.Pending(fence);
                Check(pending || point == points[points.Count - 1] && !HelperDelivery.PendingInstall(fence), "interruption leaves resumable signed intent or exact final terminal evidence: " + point);
            }
            var fake = new Fake(); var credentials = 0; using var client = new LauncherRecoveryClient(root, fake, fixtureCredential: () => { credentials++; throw new Exception("Pending helper public discovery must not open credentials"); });
            if (entrypoint == "Check") {
                Check(client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Available, "Check resumes pending helper before offering metadata: " + point);
                Check(client.SelectedChannel == "Stable" && credentials == 0 && fake.Credentials == 0, "pending helper plus saved Beta still discovers only anonymous public Release: " + point);
            }
            else { client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult(); Check(fake.Calls == 0, "pending helper completion reuses exact signed durable inputs without network: " + point); }
            using (var fence = new WindowsFileFence(root)) {
                UpdateLaunchGate.Check(fence);
                Check(!AutomaticHelperUpgrade.Pending(fence) && !HelperDelivery.PendingInstall(fence), "entrypoint retires both exact helper intents: " + entrypoint + "/" + point);
                Check(HelperDelivery.Fingerprint(fence, UpdateTrust.Production(), 4) == UpdateTrust.Hash(Input("helper.image")), "resumed helper matches the original signed incoming bytes: " + point);
            }
            foreach (var row in before.Where(row => row.Key != Full(root, HelperDescriptor.Image) && row.Key != Full(root, HelperDelivery.Receipt)))
                Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "pending helper resume preserves previous owned/history/settings/unrelated bytes: " + point);
        }
    }
    private static void FaultAwareDiscovery()
    {
        foreach (var mode in new[] { "retired", "changed", "operation" }) {
            var root = Seed();
            var context = new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "1.3.0", "1.3.1", 123, 456,
                UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
            using (var fence = new WindowsFileFence(root)) UpdateFaults.Fail(fence, context, FaultPhase.Download, FaultReason.Network, null);
            var before = Snapshot(root);
            using var client = new LauncherRecoveryClient(root, new Fake());
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready,
                "retired failure permits a verified external patch offer without game UI: " + mode);
            Check(client.Prepared!.Installation.FaultHash == UpdateTrust.Hash(File.ReadAllBytes(Full(root, UpdateFaults.FailurePath))), "external offer binds the exact retained failure latch: " + mode);
            Unchanged(root, before, "viewing an external patch offer with a failure latch: " + mode);
            if (mode == "changed") {
                using (var fence = new WindowsFileFence(root)) {
                    var fault = UpdateFault.Parse(File.ReadAllBytes(Full(root, UpdateFaults.FailurePath))); fault.Reason = FaultReason.Timeout;
                    using var file = fence.OpenFile(UpdateFaults.FailurePath, true); file.Stream.SetLength(0); var bytes = fault.Bytes(); file.Stream.Write(bytes, 0, bytes.Length); file.Flush();
                }
                before = Snapshot(root);
                Reject(() => client.Queue(false, CancellationToken.None).GetAwaiter().GetResult(), "a changed failure latch invalidates previous Apply consent");
                foreach (var row in before) Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "changed failure is not acknowledged or replaced");
            } else if (mode == "operation") {
                using (var fence = new WindowsFileFence(root)) {
                    var fault = UpdateFault.Parse(File.ReadAllBytes(Full(root, UpdateFaults.FailurePath)));
                    File.WriteAllBytes(Full(root, UpdateFaults.OperationPath), fault.Bytes());
                }
                var fake = new Fake(); using var blocked = new LauncherRecoveryClient(root, fake);
                Check(blocked.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.RecoveryRequired && fake.Calls == 0,
                    "a still-pending operation cannot be treated as a retired network failure");
                Check(File.Exists(Full(root, UpdateFaults.FailurePath)) && File.Exists(Full(root, UpdateFaults.OperationPath)), "pending failure and operation evidence are retained");
            }
        }
    }
    private static void Queries()
    {
        foreach (var row in new[] { new[] { "none", "NoPatch" }, new[] { "network", "Failed" }, new[] { "denied", "AccessUnavailable" }, new[] { "limited", "RateLimited" }, new[] { "missing", "Incomplete" }, new[] { "redirect", "Failed" }, new[] { "signature", "VerificationFailed" }, new[] { "zip", "VerificationFailed" }, new[] { "old-helper", "VerificationFailed" }, new[] { "ready", "Ready" } }) {
            var root = Seed(); var before = Snapshot(root); var fake = new Fake { Mode = row[0] }; var credentialCalls = 0;
            using var client = new LauncherRecoveryClient(root, fake, fixtureCredential: () => { credentialCalls++; return null; });
            var result = client.Check(true, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.ToString() == row[1], row[0] + " distinct status " + result); Check(credentialCalls == 0 && fake.Credentials == 0, row[0] + " stable never reads/sends private credentials");
            Unchanged(root, before, row[0]);
            if (row[0] == "limited") { var calls = fake.Calls; Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.RateLimited && fake.Calls == calls, "server cooldown survives immediate retry"); }
        }
        {
            var root = Seed(); File.AppendAllText(Full(root, "GameAssembly.dll"), "changed game"); var before = Snapshot(root); var fake = new Fake();
            using var client = new LauncherRecoveryClient(root, fake); Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "game update digest drift does not block a signed new patch"); Unchanged(root, before, "game-content advisory");
        }
        {
            var root = Seed(); var before = Snapshot(root); using var client = new LauncherRecoveryClient(root, new Fake());
            Check(client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Available, "manual download preference leaves unverified offer");
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "selected download bypasses only local metadata cooldown"); Unchanged(root, before, "manual download");
            Check(client.Installation!.Korean && !client.Installation.AutoDownload && !client.Installation.ApplyAfterExit, "existing language and both OFF preferences read unchanged");
        }
        {
            var root = Seed(); File.WriteAllText(Full(root, ChannelTransitionIntent.SettingsPath), "[일반]\n업데이트 채널 = Beta\n", new UTF8Encoding(false)); var before = Snapshot(root); var fake = new Fake(); var credentials = 0;
            using var client = new LauncherRecoveryClient(root, fake, fixtureCredential: () => { credentials++; throw new Exception("Emergency discovery must remain public"); });
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready && client.SelectedChannel == "Stable", "saved private preference defaults emergency discovery to public Release");
            Check(credentials == 0 && fake.Credentials == 0, "saved Beta never opens credentials or sends a public bearer token"); Unchanged(root, before, "public emergency default");
        }
        {
            var root = Seed(); File.WriteAllText(Full(root, ChannelTransitionIntent.SettingsPath), "[일반]\n업데이트 채널 = PreRelease\n", new UTF8Encoding(false)); var before = Snapshot(root); var fake = new Fake { PreRelease = true }; var credentialCalls = 0;
            using var client = new LauncherRecoveryClient(root, fake, fixtureCredential: () => { credentialCalls++; throw new Exception("Public prerelease must never access private credentials"); });
            Check(client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Available && client.Installation!.Channel == "PreRelease" && client.Installation.ActiveChannel == "Stable",
                "public prerelease selected from settings has a distinct explicit-transition offer");
            Check(credentialCalls == 0 && fake.Credentials == 0 && client.Prepared == null, "public prerelease discovery is anonymous and grants no installation or approval");
            Unchanged(root, before, "public prerelease offer");
        }
        PublicChannels();
    }
    private static void ChannelSource(string root, string active, string version, string? target = null, string? floor = null, string? floorHash = null)
    {
        if (version != "1.3.0") File.WriteAllBytes(Full(root, UpdateTrust.PluginPath), Input(version == "1.4.0" ? "higher-plugin.dll" : "owned/" + UpdateTrust.PluginPath));
        using var fence = new WindowsFileFence(root);
        string? Version(string channel) => active == channel ? version : target == channel ? floor : null;
        string Digest(string channel) => Version(channel) == null ? "-" : active == channel ? new string('B', 64) : floorHash ?? new string('F', 64);
        long Id(string channel) => Version(channel) == null ? 0 : ReleaseFeedPolicy.For(channel).RepositoryId;
        var state = new ReceiptChannelState(fence.RootIdentity.Text, active, Guid.NewGuid().ToString("N"), Id("Stable"), Version("Stable"), Digest("Stable"),
            Id("Beta"), Version("Beta"), Digest("Beta"), Id("PreRelease"), Version("PreRelease"), Digest("PreRelease"));
        File.WriteAllBytes(Full(root, InstallReceipt.Path), Bytes("{\"schema\":1,\"baseline\":\"1.3.0\",\"owner\":\"kr.betterastralparty.mod\",\"loaderBefore\":false,\"files\":["
            + string.Join(",", InstallReceipt.Owned.Select(path => "{\"path\":\"" + path + "\",\"sha256\":\"" + UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) + "\"}"))
            + "],\"channelState\":\"" + state.Encoded + "\"}"));
        File.WriteAllText(Full(root, UpdateTransaction.HighWaterPath), "BetterAstralParty.HighWater/v1\nroot=" + fence.RootIdentity.Text + "\nversion=" + version + "\ndescriptor=" + Digest(active) + "\n", new UTF8Encoding(false));
    }
    private static void PublicChannels()
    {
        foreach (var target in new[] { "Stable", "PreRelease" }) foreach (var version in new[] { "1.3.0", "1.3.1", "1.4.0" }) {
            var root = Seed(); ChannelSource(root, "Beta", version); var before = Snapshot(root); var fake = new Fake { PreRelease = target == "PreRelease" }; var credentials = 0;
            using var client = new LauncherRecoveryClient(root, fake, fixtureCredential: () => { credentials++; throw new Exception("Public channel opened a credential"); });
            Check(client.SelectChannel(target) && client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "existing Beta to public " + target + " latest is ready from installed " + version);
            var prepared = client.Prepared!;
            Check(prepared.Transition != null && prepared.Verified.Channel == target && prepared.Release.Version == "1.3.1", "public lower/equal/higher target retains exact transition review and signed purpose");
            Check(prepared.Verified.PublicRelease, "both public destinations use one immutable signed public scope");
            var intent = ChannelTransitionIntent.Approve(prepared.Transition!); var feed = ReleaseFeedPolicy.For(target);
            var context = new UpdateContext(feed.Repository, target, version, prepared.Release.Version, prepared.Release.Id, prepared.Verified.AssetId, UpdateTrust.PlatformId, repositoryId: feed.RepositoryId, transition: intent);
            Check(UpdateTrust.Production().VerifyDescriptor(prepared.Descriptor, prepared.Key, prepared.Signature, context).DescriptorSha256 == prepared.Verified.DescriptorSha256,
                "explicit lower/equal/higher approval authorizes only the exact verified public descriptor");
            if (version == "1.3.0") {
                var metadata = File.ReadAllText(Path.Combine(_inputs, "releases.json")).Trim(); metadata = metadata.Substring(1, metadata.Length - 2);
                if (target == "PreRelease") metadata = System.Text.RegularExpressions.Regex.Replace(metadata, "(\"prerelease\"\\s*:\\s*)false", "$1true");
                foreach (var malformed in new[] { metadata.Replace("\"id\":123", "\"id\":124"), metadata.Replace("\"id\":456", "\"id\":999"), metadata.Replace("1.3.1", "1.3.2"),
                    metadata.Replace("\"id\":123", "\"id\":123,\"id\":123"), metadata.Replace("\"id\":123", "\"id\":123.0"), metadata.Replace("\"id\":457", "\"id\":459") })
                    Reject(() => PublicReleaseEligibility.Release(Bytes(malformed), context, prepared.Verified, 457, 458), "Framework live release parser rejects mismatched IDs/tag/ZIP/metadata assets/duplicate/decimal fields");
            }
            Check(credentials == 0 && fake.Credentials == 0, "Beta receipt is observed without private discovery or token access");
            Unchanged(root, before, "public one-operation transition " + target + " " + version);
        }
        {
            var root = Seed(); var before = Snapshot(root); var fake = new Fake(); using var client = new LauncherRecoveryClient(root, fake);
            Reject(() => client.SelectChannel("Beta"), "emergency client cannot select private Beta");
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "old public offer is initially prepared"); var old = client.Prepared;
            Check(client.SelectChannel("PreRelease") && client.Prepared == null && client.Release == null && client.Installation == null, "changing public channel invalidates all prior prepared version/notes and installation view");
            Reject(() => client.Queue(false, CancellationToken.None).GetAwaiter().GetResult(), "stale prepared offer cannot queue after channel switch");
            fake.PreRelease = true;
            Check(client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Available, "channel switch refreshes metadata without the old local cooldown");
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready && client.Prepared != old && client.Prepared!.Verified.Channel == "PreRelease", "same immutable public proof is prepared for newly selected classification");
            Check(client.Prepared!.Descriptor.SequenceEqual(old!.Descriptor) && client.Prepared.Signature.SequenceEqual(old.Signature), "promotion/demotion never rebuilds or re-signs the public update");
            Reject(() => client.Queue(false, CancellationToken.None).GetAwaiter().GetResult(), "cross-channel apply requires fresh explicit approval");
            foreach(var row in before) Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "declining a transition preserves original file " + Path.GetFileName(row.Key));
        }
        {
            var root = Seed(); var before = Snapshot(root); var fake = new Fake(); using var client = new LauncherRecoveryClient(root, fake);
            fake.BeforeRequest = () => { fake.BeforeRequest = null; Check(!client.SelectChannel("PreRelease"), "busy metadata/download request cannot change destination"); };
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready && client.SelectedChannel == "Stable", "busy switch cannot publish a stale different-channel result"); Unchanged(root, before, "busy channel selection");
        }
        {
            var root = Seed(); var before = Snapshot(root); var fake = new Fake { Mode = "limited" }; using var client = new LauncherRecoveryClient(root, fake);
            Check(client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.RateLimited, "public rate-limit observed before switching"); var calls = fake.Calls;
            Check(client.SelectChannel("PreRelease") && client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.RateLimited && fake.Calls == calls, "channel change preserves server cooldown while invalidating local metadata"); Unchanged(root, before, "cross-channel server cooldown");
        }
        foreach (var target in new[] { "Stable", "PreRelease" }) foreach (var evidence in new[] { "higher-floor", "different-proof", "exact-replay" }) {
            var root = Seed(); var descriptor = Input(target == "PreRelease" ? "pre-update.descriptor" : "update.descriptor");
            ChannelSource(root, "Beta", "1.4.0", target, evidence == "higher-floor" ? "1.3.2" : "1.3.1", evidence == "exact-replay" ? UpdateTrust.Hash(descriptor) : new string('F', 64));
            var before = Snapshot(root); using var client = new LauncherRecoveryClient(root, new Fake { PreRelease = target == "PreRelease" }); client.SelectChannel(target);
            var status = client.Check(true, CancellationToken.None).GetAwaiter().GetResult();
            Check(status == (evidence == "exact-replay" ? LauncherRecoveryStatus.Ready : LauncherRecoveryStatus.VerificationFailed), "destination floor and exact replay protected for " + target + " " + evidence);
            Check(evidence == "exact-replay" ? client.Prepared!.Transition != null : client.Prepared == null, "only exact previously approved destination proof can be prepared"); Unchanged(root, before, "destination history " + evidence);
        }
        {
            var root = Seed(); var before = Snapshot(root); using var client = new LauncherRecoveryClient(root, new Fake { PreRelease = true, WrongPurpose = true }); client.SelectChannel("PreRelease");
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.VerificationFailed && client.Prepared == null, "public release metadata cannot relabel signed Stable purpose as PreRelease"); Unchanged(root, before, "signed public purpose mismatch");
        }
        foreach (var target in new[] { "Stable", "PreRelease" }) foreach (var failure in new[] { "reclassified", "network", "cancelled" }) {
            var root = Seed(); var fake = new Fake { PreRelease = target == "PreRelease" }; using var client = new LauncherRecoveryClient(root, fake); client.SelectChannel(target);
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "public offer prepared before " + target + " " + failure);
            var before = Snapshot(root);
            if (failure == "reclassified") fake.PreRelease = !fake.PreRelease;
            if (failure == "network") fake.Mode = "network";
            using var cancelled = new CancellationTokenSource(); if (failure == "cancelled") cancelled.Cancel();
            Reject(() => client.Queue(target == "PreRelease", cancelled.Token).GetAwaiter().GetResult(), "public queue rejects " + target + " " + failure + " before maintenance writes");
            Unchanged(root, before, "public queue " + target + " " + failure);
            Check(!File.Exists(Full(root, UpdateFaults.FailurePath)) && !StagePaths(root).Any(), "classification/network/cancel never disables updater or creates pending stage");
        }
        foreach (var target in new[] { "Stable", "PreRelease" }) {
            var root = Seed(); var before = Snapshot(root); var fake = new Fake { PreRelease = target == "PreRelease" };
            fake.OnRequest = request => { if (request.RequestUri!.AbsolutePath.EndsWith("/releases/123", StringComparison.Ordinal)) fake.PreRelease = !fake.PreRelease; };
            using var client = new LauncherRecoveryClient(root, fake); client.SelectChannel(target);
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Cancelled && client.Prepared == null, "final fresh public metadata rejects classification change during download " + target);
            Unchanged(root, before, "public download classification change " + target);
        }
        {
            var root = Seed(); ChannelSource(root, "Beta", "1.4.0"); using var client = new LauncherRecoveryClient(root, new Fake { PreRelease = true }); client.SelectChannel("PreRelease");
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "exact root-state transition prepared before external history change");
            File.Delete(Full(root, UpdateTransaction.HighWaterPath)); var before = Snapshot(root);
            Reject(() => client.Queue(true, CancellationToken.None).GetAwaiter().GetResult(), "changed highwater invalidates prior explicit review before helper or staging writes");
            Check(!StagePaths(root).Any(), "changed reviewed highwater creates no new update stage");
            foreach (var row in before) Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "changed root-state rejection preserves original " + Path.GetFileName(row.Key));
        }
    }
    private static void Protocol()
    {
        var framework = typeof(SteamLauncher).Assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false).Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().Single();
        Check(framework.FrameworkName == ".NETFramework,Version=v4.8" && AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName == ".NETFramework,Version=v4.8", "native launcher targets Framework4.8 modern system TLS defaults");
        var context = new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "1.3.0", "1.3.1", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
        using var process = Process.GetCurrentProcess();
        var ticket = new UpdateTicket { RootId = "12345678:1234567890123456", Stage = UpdateWorkPath.New("stage"), StageId = "12345678:1234567890123456", DescriptorHash = new string('A', 64), Context = context,
            LauncherPid = (uint)process.Id, LauncherCreation = process.StartTime.ToFileTimeUtc(), LauncherSession = (uint)process.SessionId };
        Check(UpdateTicket.Parse(ticket.Bytes()).GamePid == 0, "canonical launcher-only v4 ticket round trip");
        ticket.GameSession = 1; Reject(() => UpdateTicket.Parse(ticket.Bytes()), "zero game cannot carry a session"); ticket.GameSession = 0;
        ticket.LauncherPid = 0; Reject(() => UpdateTicket.Parse(ticket.Bytes()), "zero game requires positive launcher"); ticket.LauncherPid = (uint)process.Id;
        ticket.LauncherCreation = 0; Reject(() => UpdateTicket.Parse(ticket.Bytes()), "launcher creation is mandatory");
        foreach (LauncherRecoveryStatus status in Enum.GetValues(typeof(LauncherRecoveryStatus))) { Check(LauncherRecoveryForm.StatusText(status, true).Length > 0 && LauncherRecoveryForm.StatusText(status, false).Length > 0, "KR/EN " + status); }
        var uiRoot = Seed();
        File.WriteAllText(Full(uiRoot, ChannelTransitionIntent.SettingsPath), "[일반]\n언어=한국어\n업데이트 채널=Stable\n[업데이트]\n자동 다운로드=true\n종료 후 적용=false\n", new UTF8Encoding(false));
        var uiFake = new Fake(); var uiClient = new LauncherRecoveryClient(uiRoot, uiFake);
        using (var form = new LauncherRecoveryForm(uiRoot, uiClient)) {
            var table = form.Controls.OfType<System.Windows.Forms.TableLayoutPanel>().Single();
            var options = table.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single(panel => panel.Controls.OfType<System.Windows.Forms.ComboBox>().Any());
            var language = options.Controls.OfType<System.Windows.Forms.ComboBox>().Single(combo => combo.Items.Contains("한국어"));
            var channel = options.Controls.OfType<System.Windows.Forms.ComboBox>().Single(combo => combo != language);
            Check(channel.Items.Count == 2 && channel.DropDownStyle == System.Windows.Forms.ComboBoxStyle.DropDownList && channel.Items.Cast<object>().All(item => !item.ToString()!.Contains("Beta")), "emergency selector offers only public Release and PreRelease");
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000); form.ShowInTaskbar = false;
            var shown = false; form.Shown += (_, __) => shown = true;
            string UiState() => "shown=" + shown + "; busy=" + form.FixtureBusy + "; prepared=" + (uiClient.Prepared != null) + "; requests=" + uiFake.Calls
                + "; language=" + language.SelectedIndex + "; selected=" + uiClient.SelectedChannel + "; preparedChannel=" + uiClient.Prepared?.Verified.Channel
                + "; sync=" + SynchronizationContext.Current?.GetType().FullName + "; state=" + string.Join(" | ", table.Controls.OfType<System.Windows.Forms.Label>().Select(label => label.Text));
            bool Ready(string selected, string label) => shown && !form.FixtureBusy && uiClient.Prepared?.Verified.Channel == selected
                && table.Controls.OfType<System.Windows.Forms.Label>().Any(item => item.Text == LauncherRecoveryForm.StatusText(LauncherRecoveryStatus.Ready, language.SelectedIndex == 0))
                && table.Controls.OfType<System.Windows.Forms.Label>().Any(item => item.Text.Contains((language.SelectedIndex == 0 ? "선택 채널: " : "Selected channel: ") + label));
            RunUi(form, () => {
            PumpUi(() => Ready("Stable", language.SelectedIndex == 0 ? "릴리즈" : "Release"), UiState, "Initial recovery UI completion");
            VerifyUi(form, "initial ready");
            Check(uiClient.Prepared != null && !uiClient.Installation!.ApplyAfterExit && !StagePaths(uiRoot).Any(), "fake recovery window auto-downloads but never applies an OFF preference without explicit action");
            foreach (var size in new[] { new System.Drawing.Size(760, 490), new System.Drawing.Size(1000, 650) }) foreach (var locale in new[] { 0, 1 }) {
                form.ClientSize = size; language.SelectedIndex = locale; form.CreateControl(); form.PerformLayout();
                Check(channel.Items.Cast<string>().SequenceEqual(locale == 0 ? new[] { "릴리즈", "프리 릴리즈" } : new[] { "Release", "Pre-release" }), "selector uses exact localized public channel labels " + locale);
                var channelText = typeof(LauncherRecoveryForm).GetMethod("ChannelText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                Check((string)channelText.Invoke(form, new object[] { "Beta" })! == (locale == 0 ? "비공개 베타" : "Private Beta"), "observed private source channel is localized but never selectable " + locale);
                Check(table.Controls.OfType<System.Windows.Forms.Label>().Any(label => label.Text.Contains((locale == 0 ? "릴리즈" : "Release") + " 1.3.0")), "active and selected version row localizes the public channel " + locale);
                var actions = table.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single(panel => panel.Controls.OfType<System.Windows.Forms.Button>().Any());
                Check(actions.Controls.OfType<System.Windows.Forms.Button>().All(b => b.AutoSize && b.PreferredSize.Width <= table.Width), "native KR/EN action labels fit without fixed text clipping " + locale + " " + size.Width);
                Check(channel.PreferredSize.Width <= table.Width && options.Controls.OfType<System.Windows.Forms.Label>().Single().PreferredSize.Width <= table.Width,
                    "KR/EN one-operation channel label and selector fit native resizable layout " + locale + " " + size.Width);
                using var image = new System.Drawing.Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height));
                image.Save(Path.Combine(_base, "ui-" + locale + "-" + size.Width + ".png"));
            }
            foreach (var blocked in new[] { "_busy", "_queued" }) {
                typeof(LauncherRecoveryForm).GetField(blocked, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(form, true);
                typeof(LauncherRecoveryForm).GetMethod("RefreshText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(form, null);
                Check(!channel.Enabled, "channel selector is disabled while " + blocked);
                var calls = uiFake.Calls; channel.SelectedIndex = 1;
                Check(uiClient.SelectedChannel == "Stable" && uiFake.Calls == calls, "even programmatic selection cannot bypass " + blocked + " handler gate");
                typeof(LauncherRecoveryForm).GetField(blocked, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(form, false);
                typeof(LauncherRecoveryForm).GetMethod("RefreshText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(form, null);
            }
            var previous = uiClient.Prepared; uiFake.PreRelease = true; channel.SelectedIndex = 1;
            Check(uiClient.Prepared == null && form.FixtureBusy, "UI channel change immediately invalidates prepared old-channel payload");
            PumpUi(() => Ready("PreRelease", language.SelectedIndex == 0 ? "프리 릴리즈" : "Pre-release"), UiState, "Public channel recovery UI completion");
            VerifyUi(form, "public channel ready");
            Check(uiClient.Prepared != previous && uiClient.Prepared!.Verified.Channel == "PreRelease" && channel.SelectedIndex == 1 && uiFake.Credentials == 0,
                "UI refresh publishes only new anonymously verified public-channel offer");
            var labels = string.Join(" | ", table.Controls.OfType<System.Windows.Forms.Label>().Select(label => label.Text));
            Check(language.SelectedIndex == 1, "manual English selection survives subsequent channel check; index=" + language.SelectedIndex + "; labels=" + labels);
            var selectedLabel = language.SelectedIndex == 0 ? "프리 릴리즈" : "Pre-release";
            Check(labels.Contains(selectedLabel) && !labels.Contains("PreRelease"), "selected version row displays chosen-language label rather than protocol identity; index=" + language.SelectedIndex + "; expected=" + selectedLabel + "; labels=" + labels);
            Check(File.ReadAllText(Full(uiRoot, ChannelTransitionIntent.SettingsPath)).Contains("업데이트 채널=Stable"), "one-operation UI selection leaves stored in-game channel unchanged");
            });
        }
        LateLanguageChoice();
        foreach (var json in new[] { "{\"id\":1,\"id\":2}", "{\"id\":1,\"\\u0069d\":2}", "{\"a\":{\"id\":1,\"id\":2}}", "{\"id\":01}", "{\"a\":NaN}", "[1] trailing" }) Reject(() => LauncherRecoveryJson.Read(Bytes(json), 1000), "strict JSON rejects " + json);
        Check(LauncherRecoveryJson.Id(LauncherRecoveryJson.Object(LauncherRecoveryJson.Read(Bytes("{\"id\":9007199254740991,\"body\":\"a\\nb\"}"), 1000)), "id") == 9007199254740991, "integer identifier precision retained");
        Reject(() => LauncherRecoveryJson.Id(LauncherRecoveryJson.Object(LauncherRecoveryJson.Read(Bytes("{\"id\":1.0}"), 1000)), "id"), "decimal identifiers rejected");
        Reject(() => LauncherRecoveryClient.Ini(Bytes("[일반]\n업데이트 채널=Stable\n업데이트 채널=Beta\n")), "duplicate channel settings rejected");
        var channelMetadata = Bytes("[{\"id\":1,\"tag_name\":\"1.4.0\",\"draft\":false,\"prerelease\":false,\"body\":\"\",\"assets\":[]},{\"id\":2,\"tag_name\":\"2.0.0\",\"draft\":false,\"prerelease\":true,\"body\":\"\",\"assets\":[]},{\"id\":3,\"tag_name\":\"7.0.0-rc.1\",\"draft\":false,\"prerelease\":false,\"body\":\"\",\"assets\":[]}]");
        Check(LauncherRecoveryClient.Latest(channelMetadata, ReleaseFeedPolicy.Stable)!.Version == "1.4.0", "native recovery release excludes flagged releases and unsafe prerelease tags");
        Check(LauncherRecoveryClient.Latest(channelMetadata, ReleaseFeedPolicy.PreRelease)!.Version == "2.0.0", "native recovery prerelease uses GitHub flag rather than inferring from a tag");
        Check(LauncherRecoveryClient.Latest(channelMetadata, ReleaseFeedPolicy.Beta)!.Version == "7.0.0-rc.1", "private Beta recovery preserves its independent repository latest policy");
        foreach (var feed in new[] { ReleaseFeedPolicy.Stable, ReleaseFeedPolicy.PreRelease, ReleaseFeedPolicy.Beta }) {
            byte[] Release(string version) => Bytes("[{\"id\":123,\"draft\":false,\"prerelease\":" + (feed.Channel == "PreRelease" ? "true" : "false")
                + ",\"tag_name\":\"" + version + "\",\"body\":\"\",\"assets\":[]}]");
            foreach (var old in new[] { "1.2.1", "1.2.99", "1.3.0-rc.3.local.12" })
                Check(LauncherRecoveryClient.Latest(Release(old), feed) == null, "recovery excludes old or local remote targets: " + feed.Channel + "/" + old);
            Check((LauncherRecoveryClient.Latest(Release("1.3.0-rc.1"), feed) != null) == (feed.Channel == "Beta"), "only private Beta accepts pre-GA 1.3 RC destinations");
            Check(LauncherRecoveryClient.Latest(Release("1.3.0"), feed)!.Version == "1.3.0", "recovery accepts the exact public baseline: " + feed.Channel);
        }
    }
    private static void LateLanguageChoice()
    {
        foreach (var choice in new[] { "changed", "same", "saved-default" }) {
            var sameChoice = choice == "same"; var manual = choice != "saved-default";
            var previousCulture = Thread.CurrentThread.CurrentUICulture;
            try {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(sameChoice || !manual ? "en-US" : "ko-KR");
                var root = Seed(); var before = Snapshot(root); var fake = new Fake();
                using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
                fake.BeforeRequest = () => { fake.BeforeRequest = null; entered.Set(); if (!resume.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Inert initial language lookup gate"); };
                using var client = new LauncherRecoveryClient(root, fake);
                using var form = new LauncherRecoveryForm(root, client);
                var table = form.Controls.OfType<System.Windows.Forms.TableLayoutPanel>().Single();
                var options = table.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single(panel => panel.Controls.OfType<System.Windows.Forms.ComboBox>().Any());
                var language = options.Controls.OfType<System.Windows.Forms.ComboBox>().Single(combo => combo.Items.Contains("한국어"));
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000); form.ShowInTaskbar = false;
                try {
                    RunUi(form, () => {
                    var due = DateTime.UtcNow.AddSeconds(5);
                    while (!entered.IsSet) { System.Windows.Forms.Application.DoEvents(); if (DateTime.UtcNow > due) throw new TimeoutException("Initial language lookup did not enter inert gate"); Thread.Sleep(5); }
                    Check(form.FixtureBusy && language.SelectedIndex == (sameChoice || !manual ? 1 : 0), "language test begins before saved-language initialization completes; choice=" + choice);
                    if (manual) {
                        language.SelectedIndex = 1;
                        if (sameChoice) typeof(System.Windows.Forms.ComboBox).GetMethod("OnSelectionChangeCommitted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(language, new object[] { EventArgs.Empty });
                    } else { form.ActiveControl = table.Controls.OfType<System.Windows.Forms.TextBox>().Single(); Check(!language.Focused, "saved-default initialization has no focused language choice"); }
                    resume.Set();
                    string UiState() => "choice=" + choice + "; busy=" + form.FixtureBusy + "; index=" + language.SelectedIndex + "; selected=" + client.SelectedChannel
                        + "; sync=" + SynchronizationContext.Current?.GetType().FullName + "; labels=" + string.Join(" | ", table.Controls.OfType<System.Windows.Forms.Label>().Select(label => label.Text));
                    PumpUi(() => !form.FixtureBusy && client.Release != null && language.SelectedIndex == (manual ? 1 : 0)
                        && table.Controls.OfType<System.Windows.Forms.Label>().Any(label => label.Text == LauncherRecoveryForm.StatusText(LauncherRecoveryStatus.Available, !manual))
                        && table.Controls.OfType<System.Windows.Forms.Label>().Any(label => label.Text.Contains(manual ? "Selected channel: Release" : "선택 채널: 릴리즈")),
                        UiState, "Initial saved-language recovery UI completion");
                    VerifyUi(form, "late saved-language " + choice);
                    var labels = string.Join(" | ", table.Controls.OfType<System.Windows.Forms.Label>().Select(label => label.Text));
                    Check(client.Installation!.Korean && language.SelectedIndex == (manual ? 1 : 0) && labels.Contains(manual ? "Selected channel: Release" : "선택 채널: 릴리즈"),
                        "late saved Korean respects explicit English and still supplies untouched defaults; choice=" + choice + "; index=" + language.SelectedIndex + "; labels=" + labels);
                    Unchanged(root, before, "manual language choice stays local to emergency window");
                    });
                } finally { resume.Set(); form.Close(); }
            } finally { Thread.CurrentThread.CurrentUICulture = previousCulture; }
        }
    }
    private static void Diagnostics()
    {
        var root = Seed(); using var owner = DiagnosticOwner.StartInstaller(root, "1.3.1-rc.1.local.1", new string('A', 32)); DiagnosticHub.Bind(owner);
        try {
            using var first = new LauncherRecoveryClient(root, new Fake { Mode = "none" }); first.Check(true, CancellationToken.None).GetAwaiter().GetResult();
            using var second = new LauncherRecoveryClient(root, new Fake { Mode = "network" }); second.Check(true, CancellationToken.None).GetAwaiter().GetResult();
            Check(SpinWait.SpinUntil(() => owner.Recording || owner.StorageFailed, 5000)
                && SpinWait.SpinUntil(() => owner.FlushBoundary(50), 5000), "new launcher decision writer flushes");
            var result = DiagnosticBundle.Collect(root, Path.Combine(_base, "absent-logs"), Path.Combine(_base, "absent-fallback"), Path.Combine(_base, "absent-unity"), Path.Combine(_base, "diagnostics"), Path.Combine(_base, "fallback"), "", "");
            using var zip = ZipFile.OpenRead(result.ArchivePath); var text = string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(e => { using var read = new StreamReader(e.Open()); return read.ReadToEnd(); }));
            Check(text.Contains("NoCompatibleUpdate") && text.Contains("Failed"), "source-to-ZIP preserves no-patch versus lookup failure");
            Check(!text.Contains("untrusted.invalid") && !text.Contains("github_pat_"), "new diagnostics never contain token or raw transport URL");
            using var summaryReader = new StreamReader(zip.GetEntry("summary.json")!.Open());
            var summary = LauncherRecoveryJson.Object(LauncherRecoveryJson.Read(Bytes(summaryReader.ReadToEnd()), 65536));
            var environment = LauncherRecoveryJson.Object(LauncherRecoveryJson.Get(summary, "environment"));
            Check(LauncherRecoveryJson.String(environment, "pluginVersionSource") == "unknown", "launcher collection never claims a running plugin was observed");
        } finally { DiagnosticHub.Unbind(owner); }
        CompatibilityDiagnostics();
    }
    private static void RestoreAccess(string path, System.Security.AccessControl.FileSecurity original)
    {
        // A descriptor read from disk must mark its DACL modified before File.SetAccessControl persists it.
        var restored = new System.Security.AccessControl.FileSecurity();
        restored.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), System.Security.AccessControl.AccessControlSections.Access);
        File.SetAccessControl(path, restored);
        Check(File.GetAccessControl(path).GetSecurityDescriptorBinaryForm().SequenceEqual(original.GetSecurityDescriptorBinaryForm()), "inert original file DACL is exactly restored");
    }
    private static void SettingsFallback()
    {
        foreach (var scenario in new[] { "channel", "duplicate", "boolean", "utf8", "oversized", "locked", "denied", "missing", "missing-parent" }) {
            var root = Seed(); var path = Full(root, ChannelTransitionIntent.SettingsPath);
            var valid = "[일반]\n언어=English\n업데이트 채널=Stable\n[업데이트]\n자동 다운로드=true\n종료 후 적용=true\n";
            if (scenario == "channel") valid = valid.Replace("채널=Stable", "채널=INERT_PRIVATE_SETTING_DO_NOT_EXPORT");
            else if (scenario == "duplicate") valid += "자동 다운로드=false\n";
            else if (scenario == "boolean") valid = valid.Replace("종료 후 적용=true", "종료 후 적용=INERT_PRIVATE_SETTING_DO_NOT_EXPORT");
            File.WriteAllBytes(path, scenario == "utf8" ? new byte[] { 0xc3, 0x28 } : scenario == "oversized" ? new byte[1024 * 1024 + 1] : Bytes(valid));
            var missing = scenario == "missing" || scenario == "missing-parent";
            if (missing) File.Move(path, Full(root, "settings.preserved"));
            if (scenario == "missing-parent") Directory.Delete(Full(root, "BepInEx/config"));
            var before = Snapshot(root); FileStream? held = null; System.Security.AccessControl.FileSecurity? original = null;
            using var owner = DiagnosticOwner.StartInstaller(root, "1.3.1-rc.1.local.2", new string('A', 32)); DiagnosticHub.Bind(owner);
            try {
                if (scenario == "locked") held = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                else if (scenario == "denied") {
                    original = File.GetAccessControl(path); var denied = File.GetAccessControl(path);
                    denied.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                        System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny));
                    File.SetAccessControl(path, denied);
                }
                var fake = new Fake(); using var client = new LauncherRecoveryClient(root, fake);
                client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult();
                Check(fake.Calls == 0, scenario + " fallback does not automatically download or install a helper");
                Check(client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Available,
                    scenario + " optional preferences never prevent emergency update discovery");
                var settings = client.Installation!;
                Check(!settings.AutoDownload && !settings.ApplyAfterExit && settings.Channel == "Stable",
                    scenario + " unreadable or invalid preferences disable automatic actions without saving defaults");
                var readable = scenario != "oversized" && scenario != "locked" && scenario != "denied";
                Check(settings.SettingsReadable == readable, scenario + " raw settings snapshot availability remains distinct from preference parsing");
                Check(settings.SettingsHash == (missing || !readable ? "-" : UpdateTrust.Hash(File.ReadAllBytes(path))),
                    scenario + " exact readable settings hash is retained without pretending unavailable bytes are absent");
                Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready,
                    scenario + " explicit same-channel download remains available without automatic application");
                if (!readable) {
                    var rejected = false;
                    try { client.Queue(false, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (LauncherRecoveryFailure error) { rejected = error.Status == LauncherRecoveryStatus.FilesBusy; }
                    Check(rejected, scenario + " unavailable raw settings snapshot prevents helper/staging writes");
                    Check(!StagePaths(root).Any(), scenario + " rejected application creates no update stage");
                }
                fake.PreRelease = true;
                Check(client.SelectChannel("PreRelease") && client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Available
                    && client.Installation!.Channel == "PreRelease", scenario + " explicit public channel selection does not parse or overwrite the invalid preference");
                if (readable) Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready
                    && client.Prepared!.Transition!.SettingsHash == settings.SettingsHash,
                    scenario + " explicit transition review still binds the exact raw settings snapshot");
                held?.Dispose(); held = null;
                if (original != null) { RestoreAccess(path, original); original = null; }
                foreach (var row in before) Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value,
                    scenario + " preference fallback preserves all settings, installed payload and unrelated bytes");
                Check(SpinWait.SpinUntil(() => owner.Recording || owner.StorageFailed, 5000) && SpinWait.SpinUntil(() => owner.FlushBoundary(50), 5000), scenario + " preference observation writer flushes");
                var result = DiagnosticBundle.Collect(root, "", "", "", Path.Combine(_base, "diagnostics"), Path.Combine(_base, "fallback"), "", "");
                using var zip = ZipFile.OpenRead(result.ArchivePath);
                var text = string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(e => { using var read = new StreamReader(e.Open()); return read.ReadToEnd(); }));
                Check(text.Contains("\"phase\":\"Configuration\"") && text.Contains("\"outcome\":\"Partial\"")
                    && text.Contains(missing ? "NotConfigured" : readable ? "StateMalformed" : "StateUnreadable"), scenario + " optional preference warning survives source-to-ZIP collection");
                Check(!text.Contains("INERT_PRIVATE_SETTING_DO_NOT_EXPORT") && !text.Contains(root) && !text.Contains("github_pat_"), scenario + " warning exports no settings values, personal path or credential");
            } finally { held?.Dispose(); if (original != null) RestoreAccess(path, original); DiagnosticHub.Unbind(owner); }
        }
    }
    private static void DiscoveryInputs()
    {
        foreach (var path in InstallReceipt.Owned) foreach (var missing in new[] { false, true }) {
            var root = Seed(); var target = Full(root, path);
            if (missing) File.Move(target, target + ".preserved"); else File.AppendAllText(target, "inert changed owned bytes");
            var before = Snapshot(root); var fake = new Fake(); using var client = new LauncherRecoveryClient(root, fake);
            var executable = path == UpdateTrust.PluginPath || path == "BetterAstralParty-Launcher.exe";
            var label = (missing ? "missing " : "changed ") + path;
            Check(client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == (executable ? LauncherRecoveryStatus.RecoveryRequired : LauncherRecoveryStatus.Available),
                label + " discovery validates only consumed executable inputs, not unused routes or observations");
            if (executable) Check(fake.Calls == 0 && client.Prepared == null, label + " changed or absent executable never grants network or application readiness");
            else {
                client.CompleteInstalledHelper(CancellationToken.None).GetAwaiter().GetResult();
                Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready,
                    label + " signed read-only preparation remains available");
                var stopped = false;
                try { client.Queue(false, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (LauncherRecoveryFailure error) { stopped = error.Status == LauncherRecoveryStatus.RecoveryRequired; }
                catch (ApplySafetyException error) { stopped = error.NativeError == 2 || error.NativeError == 3; }
                Check(stopped && !StagePaths(root).Any(), label + " Queue retains full replacement-target ownership validation before helper or staging writes");
            }
            foreach (var row in before) Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value,
                label + " preserves every observed payload, setting, other mod and backup byte");
        }
    }
    private static void CompatibilityDiagnostics()
    {
        foreach (var scenario in new[] { "missing", "locked", "denied", "hash" }) {
            var root = Seed(); var path = Full(root, "GameAssembly.dll"); FileStream? held = null;
            System.Security.AccessControl.FileSecurity? original = null;
            using var owner = DiagnosticOwner.StartInstaller(root, "1.3.1-rc.1.local.2", new string('A', 32));
            DiagnosticHub.Bind(owner);
            try {
                if (scenario == "missing") File.Move(path, path + ".preserved");
                else if (scenario == "hash") File.AppendAllText(path, "inert changed bytes");
                else if (scenario == "locked") held = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                else {
                    original = File.GetAccessControl(path);
                    var denied = File.GetAccessControl(path);
                    denied.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                        System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny));
                    File.SetAccessControl(path, denied);
                }
                using var client = new LauncherRecoveryClient(root, new Fake());
                Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, scenario + " unavailable compatibility observation never blocks a verified emergency update");
                held?.Dispose(); held = null;
                if (original != null) { RestoreAccess(path, original); original = null; }
                Check(SpinWait.SpinUntil(() => owner.Recording || owner.StorageFailed, 5000)
                    && SpinWait.SpinUntil(() => owner.FlushBoundary(50), 5000), scenario + " compatibility failure writer flushes");
                var result = DiagnosticBundle.Collect(root, "", "", "", Path.Combine(_base, "diagnostics"), Path.Combine(_base, "fallback"), "", "");
                using var zip = ZipFile.OpenRead(result.ArchivePath);
                var text = string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(e => { using var read = new StreamReader(e.Open()); return read.ReadToEnd(); }));
                var native = scenario == "missing" ? 2 : scenario == "locked" ? 32 : scenario == "denied" ? 5 : 0;
                Check(text.Contains("\"phase\":\"Catalog\"") && text.Contains("\"outcome\":\"Partial\"") && !text.Contains("\"apply_code\":" + (int)ApplyFailure.CompatibilityRequired)
                    && (scenario == "hash" || text.Contains("\"native_code\":" + native)), scenario + " unavailable observations reach ZIP without fabricating an update block");
                Check(!text.Contains(root) && !text.Contains("github_pat_"), scenario + " cause ZIP contains no personal root or credentials");
            } finally { held?.Dispose(); if (original != null) RestoreAccess(path, original); DiagnosticHub.Unbind(owner); }
        }
    }
    private static void CompatibilityMetadata()
    {
        var root = Seed(); var before = Snapshot(root);
        using var owner = DiagnosticOwner.StartInstaller(root, "1.3.1-rc.1.local.2", new string('A', 32));
        DiagnosticHub.Bind(owner);
        try {
            using var client = new LauncherRecoveryClient(root, new Fake());
            Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready,
                "oversized signed compatibility observation never blocks emergency update readiness");
            Check(client.Prepared!.Manifest.Length == 0, "oversized observation is skipped within the bounded observer budget");
            using (var payload = new ZipArchive(new MemoryStream(client.Prepared.Zip), ZipArchiveMode.Read)) {
                using var stream = payload.GetEntry("BetterAstralParty.compatibility.json")!.Open(); using var output = new MemoryStream(); stream.CopyTo(output);
                Check(output.ToArray().SequenceEqual(Input("owned/BetterAstralParty.compatibility.json")), "skipping observation never edits the authenticated payload");
            }
            foreach (var row in before) Check(File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value, "metadata observation preserves existing installation and other mod bytes");
            Check(SpinWait.SpinUntil(() => owner.Recording || owner.StorageFailed, 5000) && SpinWait.SpinUntil(() => owner.FlushBoundary(50), 5000), "metadata observation writer flushes");
            var result = DiagnosticBundle.Collect(root, "", "", "", Path.Combine(_base, "diagnostics"), Path.Combine(_base, "fallback"), "", "");
            using var zip = ZipFile.OpenRead(result.ArchivePath);
            var text = string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(e => { using var read = new StreamReader(e.Open()); return read.ReadToEnd(); }));
            Check(text.Contains("\"phase\":\"Catalog\"") && text.Contains("\"outcome\":\"Partial\"") && !text.Contains(root), "skipped oversized observation reaches redacted diagnostic ZIP");
        } finally { DiagnosticHub.Unbind(owner); }
    }
    private static string WaitHelperResult(string stage)
    {
        var due = DateTime.UtcNow.AddSeconds(10);
        while (true) {
            try { return File.ReadAllText(Path.Combine(stage, "helper.result")); }
            catch (IOException error) when ((error.HResult & 0xffff) == 2 || (error.HResult & 0xffff) == 32) {
                if (DateTime.UtcNow > due) throw new TimeoutException("Inert helper terminal result unavailable", error);
                Thread.Sleep(20);
            }
        }
    }
    private static void Queue(string mode)
    {
        var root = Seed(Path.GetDirectoryName(typeof(FixtureEntry).Assembly.Location)!);
        var transitionMode = mode == "releasequeue" || mode == "prequeue";
        var targetChannel = mode == "prequeue" ? "PreRelease" : "Stable";
        if (transitionMode) ChannelSource(root, "Beta", "1.4.0");
        string? retiredFault=null;
        if(mode=="faultqueue") {
            File.WriteAllText(Full(root,ChannelTransitionIntent.SettingsPath),"[일반]\n업데이트 채널=Stable\n[업데이트]\n자동 다운로드=false\n종료 후 적용=false\n",new UTF8Encoding(false));
            using var failed=new WindowsFileFence(root);
            var context=new UpdateContext(ReleaseFeedPolicy.StableRepository,"Stable","1.3.0","1.3.1",123,456,UpdateTrust.PlatformId,repositoryId:ReleaseFeedPolicy.StableRepositoryId);
            retiredFault=UpdateTrust.Hash(UpdateFaults.Fail(failed,context,FaultPhase.Download,FaultReason.Network,null).Bytes());
        }
        var before = Snapshot(root);
        using var client = new LauncherRecoveryClient(root, new Fake { PreRelease = targetChannel == "PreRelease" });
        if (transitionMode) Check(client.SelectChannel(targetChannel), "explicit synthetic public emergency destination selected");
        Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "launcher child verifies ready patch");
        if (mode == "precancel") {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Reject(() => client.Queue(false, cancel.Token).GetAwaiter().GetResult(), "pre-cancelled queue rejected before helper upgrade");
            Unchanged(root, before, "pre-cancelled queue"); return;
        }
        if (mode == "startfailure") {
            var reached=false;
            LauncherRecoveryClient.BeforeHelperStart = () => { reached=true;Check(!client.SelectChannel("PreRelease"), "queue-in-progress cannot change destination");throw new IOException("inert helper start boundary"); };
            try {
                try { client.Queue(false, CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Expected injected start failure"); }
                catch(IOException error) { Check(reached && error.Message=="inert helper start boundary","exact injected start failure survives cancellation cleanup"); }
            }
            finally { LauncherRecoveryClient.BeforeHelperStart = null; }
            using var failed = new WindowsFileFence(root);
            Check(UpdateFaults.ReadFile(failed, UpdateFaults.OperationPath) == null, "pre-start failure retires exact guard, never appears as live owner");
            var cancelledStage=StagePaths(root).Single();
            var cancelledTicket=File.ReadAllBytes(Path.Combine(cancelledStage,"handoff.ticket"));
            Check(File.ReadAllText(Path.Combine(cancelledStage,"helper.result"))==UpdateTrust.Hash(cancelledTicket)+"\nCancelled\n","pre-start failure produces exact terminal cancellation");
            Check(UpdateRetention.Clean(failed,UpdateTrust.Production()).RemovedPackages==1 && !File.Exists(Path.Combine(cancelledStage,"package.zip")),"pre-start cancellation package retires under signed retention rules");
            Check(LauncherRecoveryClient.Inspect(root).Version == "1.3.0", "pre-start failure can be rechecked without weakening recovery");
            foreach (var path in InstallReceipt.Owned) Check(UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) == before[Full(root, path)], "pre-start failure preserves " + path);
            return;
        }
        if (mode == "cleanupfailure") {
            using var owner = DiagnosticOwner.StartInstaller(root, "1.3.1-rc.1.local.1", new string('A', 32)); DiagnosticHub.Bind(owner);
            LauncherRecoveryClient.BeforeHelperStart = () => { var stage = StagePaths(root).Single(); Directory.CreateDirectory(Path.Combine(stage, "helper.cancel")); throw new IOException("inert original start failure"); };
            try {
                try { client.Queue(false, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Expected queue failure"); }
                catch (IOException) { Check(true, "cleanup failure preserves original process-start exception type"); }
                using var failed = new WindowsFileFence(root);
                Check(UpdateFaults.ReadFile(failed, UpdateFaults.OperationPath) != null, "uncertain cancellation marker never retires operation guard");
                owner.Stop(DiagnosticEndReason.OwnerDisposed);
                var result = DiagnosticBundle.Collect(root, Path.Combine(_base, "absent-logs"), Path.Combine(_base, "absent-fallback"), Path.Combine(_base, "absent-unity"), Path.Combine(_base, "diagnostics"), Path.Combine(_base, "fallback"), "", "1.3.0");
                using var zip = ZipFile.OpenRead(result.ArchivePath); var text = string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(e => { using var read = new StreamReader(e.Open()); return read.ReadToEnd(); }));
                Check(text.Contains("IOException") && text.Contains("StorageUnavailable") && text.Contains("HelperQueue") && text.Contains("Recovery"), "source-to-ZIP retains original and secondary cleanup failures separately");
            } finally { LauncherRecoveryClient.BeforeHelperStart = null; DiagnosticHub.Unbind(owner); }
            return;
        }
        client.Queue(transitionMode, CancellationToken.None).GetAwaiter().GetResult();
        Check(!client.SelectChannel("PreRelease") && client.Check(false, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Queued, "queued helper cannot be replaced by a new channel or discovery");
        if(retiredFault!=null) {
            Check(!File.Exists(Full(root,UpdateFaults.FailurePath)),"explicit verified recovery apply acknowledges only the retired failure latch");
            Check(Directory.GetFiles(root,"bap-fault-ack-*").Any(path=>UpdateTrust.Hash(File.ReadAllBytes(path))==retiredFault),"previous failure evidence is archived with exact bytes");
            Check(UpdateTrust.Hash(File.ReadAllBytes(Full(root,ChannelTransitionIntent.SettingsPath)))==before[Full(root,ChannelTransitionIntent.SettingsPath)],"explicit retry preserves saved automatic-update OFF values");
        }
        using var fence = new WindowsFileFence(root); var stage = StagePaths(root).Single(); var relative = UpdateWorkPath.Directories(fence, "stage").Single();
        using (var ticketFile = fence.OpenFile(relative + "/handoff.ticket")) {
            var queuedTicket = UpdateTicket.Parse(UpdateTicket.Read(ticketFile, UpdateTicket.MaxBytes));
            Check(queuedTicket.Context.Channel == targetChannel && (queuedTicket.Context.Transition != null) == transitionMode, "queued ticket retains exact selected public purpose and explicit transition approval");
            if (transitionMode) Check(queuedTicket.Context.CurrentVersion == "1.4.0" && queuedTicket.Context.ReleaseTag == "1.3.1"
                && queuedTicket.Context.Transition!.SourceChannel == "Beta" && queuedTicket.Context.Transition.TargetChannel == targetChannel,
                "actual lower public handoff is rooted in authenticated Beta source and exact target");
        }
        using (var ready = fence.OpenFile(relative + "/helper.ready")) Check(ready.Stream.Length > 0, "real inert helper accepted launcher-only ticket");
        Check(!File.Exists(Path.Combine(stage, "helper.result")), "helper cannot commit while its launcher is alive");
        if (mode == "commit") { File.WriteAllText(Path.Combine(root, "queued-stage"), relative); return; }
        client.CancelQueued();
        Check(WaitHelperResult(stage).Contains("Cancelled"), "queued helper cancellation retires without applying");
        foreach (var path in InstallReceipt.Owned) Check(UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) == before[Full(root, path)], "queue cancellation preserves owned file " + path);
        foreach (var path in new[] { "BepInEx/plugins/OtherMod/OtherMod.dll", "doorstop_config.ini", "retained-backup.keep" }) Check(UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) == before[Full(root, path)], "queue cancellation preserves unrelated file " + path);
        foreach (var path in new[] { ChannelTransitionIntent.SettingsPath, UpdateTransaction.HighWaterPath }.Where(path => before.ContainsKey(Full(root, path))))
            Check(UpdateTrust.Hash(File.ReadAllBytes(Full(root, path))) == before[Full(root, path)], "public emergency cancellation preserves exact saved settings and global highwater " + path);
        Check(HelperDelivery.AuthenticatedAt(fence, UpdateTrust.Production(), HelperDescriptor.Image, HelperDelivery.Receipt)!.Version == "1.3.1", "old 1.3.0 helper upgraded to signed launcher-capable helper");
        var oldStages = StagePaths(root);
        using var again = new LauncherRecoveryClient(root, new Fake { PreRelease = targetChannel == "PreRelease" });
        if (transitionMode) Check(again.SelectChannel(targetChannel), "fresh retry selects public target without rewriting saved settings");
        Check(again.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "cancelled handoff permits a fresh verified retry");
        again.Queue(transitionMode, CancellationToken.None).GetAwaiter().GetResult();
        var next = StagePaths(root).Except(oldStages).Single(); again.CancelQueued();
        Check(WaitHelperResult(next).Contains("Cancelled"), "same signed helper is a no-op upgrade and second cancellation remains safe");
    }
    private static void GameQueue()
    {
        var root = Path.GetDirectoryName(typeof(FixtureEntry).Assembly.Location)!;
        using var client = new LauncherRecoveryClient(root, new Fake());
        Check(client.Check(true, CancellationToken.None).GetAwaiter().GetResult() == LauncherRecoveryStatus.Ready, "existing game actor verifies incoming payload");
        var prepared = client.Prepared!; using var fence = new WindowsFileFence(root); using var process = Process.GetCurrentProcess();
        using var game = WindowsProcessGuard.Capture((uint)process.Id, fence.Full("AstralParty_INT.exe"), process.StartTime.ToFileTimeUtc(), WindowsProcessGuard.CurrentSession);
        var context = new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "1.3.0", "1.3.1", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
        var stage = UpdateStaging.Create(fence, prepared.Descriptor, prepared.Key, prepared.Signature, prepared.Zip, UpdateTrust.Production(), context);
        var ticket = new UpdateTicket { RootId = fence.RootIdentity.Text, Stage = stage.Directory, StageId = stage.DirectoryIdentity.Text, DescriptorHash = stage.DescriptorSha256, Context = context,
            GamePid = game.Pid, GameCreation = game.Creation, GameSession = game.Session };
        Check(UpdateTicket.Parse(ticket.Bytes()).GamePid > 0 && ticket.LauncherPid == 0, "existing nonzero-game/no-launcher v4 contract retained");
        UpdateTicket.Write(fence, ticket.Stage + "/handoff.ticket", ticket.Bytes()); using var signal = UpdateCancelSignal.Create(UpdateTrust.Hash(ticket.Bytes()));
        UpdateFaults.Begin(fence, context, FaultPhase.Apply, ticket);
        using var helper = Process.Start(new ProcessStartInfo { FileName = fence.Full(HelperDescriptor.Image), WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, Arguments = "--apply \"" + root + "\" " + ticket.Stage })!;
        var due = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(Full(root, ticket.Stage + "/helper.ready"))) { if (helper.HasExited || DateTime.UtcNow > due) throw new TimeoutException("Nonzero game ready"); Thread.Sleep(20); }
        Check(!File.Exists(Full(root, ticket.Stage + "/helper.result")), "existing game actor blocks commit until natural exit");
        File.WriteAllText(Path.Combine(root, "queued-stage"), ticket.Stage);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try {
            if ((args.Length != 2 && args.Length != 3) || !Path.GetFullPath(args[0]).StartsWith(FixtureBoundary.Root, StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(args[1]).StartsWith(FixtureBoundary.Root, StringComparison.OrdinalIgnoreCase)) return 90;
            _base = args[0]; _inputs = args[1]; Directory.CreateDirectory(_base);
            var mode = args.Length == 3 ? args[2] : Path.GetFileName(_base);
            if (mode == "setupgame") {
                var root = Seed(Path.GetDirectoryName(typeof(FixtureEntry).Assembly.Location)!); using var maintenance = new UpdateLaunchGate.Lease(root); using var fence = new WindowsFileFence(root);
                AutomaticHelperUpgrade.Ensure(fence, Input("helper.descriptor"), Input("helper.signature"), Input("helper.image"), UpdateTrust.Production());
                Check(true, "signed helper delivered before inert game process starts");
            }
            else if (mode == "gamequeue") GameQueue();
            else if (mode == "compatibility-metadata") CompatibilityMetadata();
            else if (mode == "tls") { using var probe = new LauncherRecoveryClient(_base); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); probe.PublicIdentityProbe(timeout.Token).GetAwaiter().GetResult(); Check(true, "actual Framework4.8 anonymous HTTPS repository identity verified"); }
            else if (mode == "queue" || mode == "faultqueue" || mode == "precancel" || mode == "startfailure" || mode == "cleanupfailure" || mode == "commit" || mode == "releasequeue" || mode == "prequeue") Queue(mode); else {
                Group(nameof(Protocol), Protocol);
                Group(nameof(Queries), Queries);
                Group(nameof(InstalledHelperCompletion), InstalledHelperCompletion);
                Group(nameof(BetaInstalledHelperCompletion), BetaInstalledHelperCompletion);
                Group(nameof(PendingHelperEntrypoints), PendingHelperEntrypoints);
                Group(nameof(FaultAwareDiscovery), FaultAwareDiscovery);
                Group(nameof(SettingsFallback), SettingsFallback);
                Group(nameof(DiscoveryInputs), DiscoveryInputs);
                Group(nameof(Diagnostics), Diagnostics);
            }
            File.WriteAllLines(Path.Combine(_base, "passed.txt"), Passed); Console.WriteLine("PASS " + _checks + " launcher recovery checks"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
