#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterAstralParty;
using BetterAstralParty.Updating;

internal static partial class HelperFixture
{
    private static string[] WorkDirectories(string root, string kind)
    { using var fence = new WindowsFileFence(root); var paths = new System.Collections.Generic.List<string>(); foreach (var relative in UpdateWorkPath.Directories(fence, kind)) paths.Add(fence.Full(relative)); return paths.ToArray(); }
    private static string WorkRelative(string root, string path) => path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');

    private static int _checks;
    private static readonly UpdateContext Context = new("TSM701/BetterAstralParty", "Stable", "1.3.0-dev", "1.3.0", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
    private static readonly UpdateDownloadRequest Request = new(Context, 457, 458);
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private sealed class Auth : IReleaseAuthentication { internal string Id = "fixture-account-a"; public string CacheIdentity => Id; public bool TryAuthorize(HttpRequestMessage request) { request.Headers.Authorization = new("Bearer", "fixture-placeholder-only"); return true; } }
    private sealed class Package : IDisposable
    {
        private readonly RSA _rsa = RSA.Create(3072);
        internal byte[] Descriptor=null!, Signature=null!, Zip=null!;
        internal byte[] Receipt;
        internal readonly UpdateTrust Trust;
        internal readonly Dictionary<string, byte[]> Old = new(StringComparer.Ordinal), New = new(StringComparer.Ordinal);
        internal Package(byte[] old, byte[] stable, byte[] inert)
        {
            foreach (var path in InstallReceipt.Owned) { Old[path] = path == UpdateTrust.PluginPath ? old : path == "BetterAstralParty-Launcher.exe" ? inert : Encoding.ASCII.GetBytes("old " + path); New[path] = path == UpdateTrust.PluginPath ? stable : path == "BetterAstralParty-Launcher.exe" ? inert : Encoding.ASCII.GetBytes("new " + path); }
            New["BetterAstralParty.compatibility.json"] = CompatibilityFixtureData.Manifest(inert);
            Receipt = CreateReceipt("ABCD1234:0000000000000100");
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var file in New) { var entry = archive.CreateEntry(file.Key, CompressionLevel.NoCompression); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); using var target = entry.Open(); target.Write(file.Value); }
            Zip = output.ToArray();
            Descriptor = Encoding.ASCII.GetBytes("BetterAstralParty.UpdateDescriptor/v4\nrepository=TSM701/BetterAstralParty\nversion=1.3.0\nchannel=Stable\nplatform=" + UpdateTrust.PlatformId + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-1.3.0-update.zip\nzip-bytes=" + Zip.Length + "\nzip-sha256=" + UpdateTrust.Hash(Zip) + "\nfile-count=5\npurpose=release/Stable\nrepository-id=" + ReleaseFeedPolicy.StableRepositoryId + "\nsettings-schema=1\n" + string.Concat(New.Select(file => "file=" + file.Key + "\t" + file.Value.Length + "\t" + UpdateTrust.Hash(file.Value) + "\n")));
            Signature = _rsa.SignData(Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); Trust = new(new[] { new TrustedUpdateKey("helper-fixture", _rsa.ExportParameters(false)) });
        }
        internal byte[] CreateReceipt(string root) {
            var version = InstalledPluginMetadata.Read(Old[UpdateTrust.PluginPath]).Version;
            var channels = new ReceiptChannelState(root, "Stable", new string('a', 32), ReleaseFeedPolicy.StableRepositoryId, version, new string('A', 64), 0, null, "-");
            return Encoding.ASCII.GetBytes("{\"schema\":1,\"baseline\":\"1.3.0\",\"owner\":\"kr.betterastralparty.mod\",\"loaderBefore\":false,\"files\":[" + string.Join(",", Old.Select(file => "{\"path\":\"" + file.Key + "\",\"sha256\":\"" + UpdateTrust.Hash(file.Value) + "\"}")) + "],\"channelState\":\"" + channels.Encoded + "\"}");
        }
        internal void RebindGame(byte[] game) {
            New["BetterAstralParty.compatibility.json"] = CompatibilityFixtureData.Manifest(game);
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var file in New) { var entry = archive.CreateEntry(file.Key, CompressionLevel.NoCompression); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); using var target = entry.Open(); target.Write(file.Value); }
            Zip = output.ToArray();
            Descriptor = Encoding.ASCII.GetBytes("BetterAstralParty.UpdateDescriptor/v4\nrepository=TSM701/BetterAstralParty\nversion=1.3.0\nchannel=Stable\nplatform=" + UpdateTrust.PlatformId + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-1.3.0-update.zip\nzip-bytes=" + Zip.Length + "\nzip-sha256=" + UpdateTrust.Hash(Zip) + "\nfile-count=5\npurpose=release/Stable\nrepository-id=" + ReleaseFeedPolicy.StableRepositoryId + "\nsettings-schema=1\n" + string.Concat(New.Select(file => "file=" + file.Key + "\t" + file.Value.Length + "\t" + UpdateTrust.Hash(file.Value) + "\n")));
            Signature = _rsa.SignData(Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        internal byte[] Sign(byte[] bytes) => _rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
        internal byte[] Public => _rsa.ExportParameters(false).Modulus!;
        internal byte[] Envelope => Encoding.ASCII.GetBytes("BetterAstralParty.UpdateSignature/v1\nkey-id=helper-fixture\nsignature=" + Convert.ToBase64String(Signature) + "\n");
        public void Dispose() => _rsa.Dispose();
    }
    private sealed class Handler(Package package) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Calls++; var data = request.RequestUri!.AbsolutePath.EndsWith("/457") ? package.Descriptor : request.RequestUri.AbsolutePath.EndsWith("/458") ? package.Envelope : package.Zip;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(data) });
        }
    }
    private sealed class Job : IUpdateHelperJob { internal AutomaticUpdateStatus? Result = null; internal bool PollFault = false; public AutomaticUpdateStatus? Poll() => PollFault ? throw new IOException("fixture poll") : Result; public void Dispose() { } }
    private static AutomaticUpdates CreateModel(UpdateDownloads downloads, IUpdateHandoff handoff) {
        var model = new AutomaticUpdates(downloads, handoff); model.Preferences(false, false); return model;
    }
    private sealed class Prepared : IPreparedUpdate { public string Stage => "bap-v4-stage-" + new string('b', 32); }
    private sealed class Handoff : IUpdateHandoff
    {
        public bool Configured => true;
        internal int Prepares, Queues, Cancels, Signals;
        internal string? Recovery = null; internal TaskCompletionSource? CancelDelay = null; internal bool InspectionFails = false;
        internal Job Current = new(); internal TaskCompletionSource<object>? Delay, QueueDelay;
        public Task<object> Prepare(DownloadedUpdate update, UpdateContext context, CancellationToken token) { Prepares++; return Delay?.Task ?? Task.FromResult<object>(new Prepared()); }
        public Task<object> Queue(object prepared, CancellationToken token) { Queues++; return QueueDelay?.Task ?? Task.FromResult<object>(Current); }
        public void SignalCancel(object prepared) { Signals++; }
        public Task<string?> Inspect() => InspectionFails ? Task.FromException<string?>(new IOException("fixture inspection")) : Task.FromResult(Recovery);
        public Task Cancel(object prepared) { Interlocked.Increment(ref Cancels); return CancelDelay?.Task ?? Task.CompletedTask; }
    }
    private static async Task Wait(AutomaticUpdates model, AutomaticUpdateStatus status) {
        for (var i = 0; i < 500 && model.Status != status; i++) { model.Tick(Now); await Task.Delay(5); }
        Check(model.Status == status, "lifecycle expected " + status + " actual " + model.Status);
    }
    private static async Task Lifecycle(Package package)
    {
        using (var disabled = new AutomaticUpdates(UpdateDownloads.Production(channel: ReleaseChannel.Beta), new Handoff())) { Check(disabled.AutoDownload && disabled.ApplyAfterExit, "new configuration both defaults on"); disabled.Preferences(true, true); disabled.Context(Request); disabled.Download(Now); disabled.Tick(Now); Check(disabled.Status == AutomaticUpdateStatus.NotConfigured && !disabled.CanDownload, "unauthenticated private feed cannot execute"); }
        var auth = new Auth(); var handler = new Handler(package); var handoff = new Handoff();
        using (var model = CreateModel(new UpdateDownloads(package.Trust, auth, handler), handoff)) {
            model.Configure(auth); model.Context(Request); await Wait(model, AutomaticUpdateStatus.Idle); model.Tick(Now); Check(handler.Calls == 0, "off preferences do not download");
            model.Preferences(true, false); await Wait(model, AutomaticUpdateStatus.Ready); Check(handler.Calls == 3 && handoff.Prepares == 1 && handoff.Queues == 0, "download only opt-in");
            model.Download(Now); model.Download(Now); Check(handler.Calls == 3, "repeated click does not duplicate ready download");
            var revision = model.Revision; ModText.Select("English"); Check(ModText.AutomaticStatus(model.Status) == "Ready", "English state"); ModText.Select("한국어"); Check(ModText.AutomaticStatus(model.Status) == "준비됨" && model.Revision == revision && handler.Calls == 3, "language switch preserves state/network");
            model.Preferences(true, true); await Wait(model, AutomaticUpdateStatus.Queued); model.Tick(Now); Check(handoff.Queues == 1, "apply after exit queued once");
            model.Cancel(); Check(model.Status == AutomaticUpdateStatus.Cancelling, "queued cancellation awaits persistence"); await Wait(model, AutomaticUpdateStatus.Cancelled); await Task.Delay(30); Check(handoff.Cancels > 0, "queued cancellation signals helper"); model.Tick(Now); Check(handoff.Queues == 1, "cancel does not requeue automatically");
        }
        var delay = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously); handoff = new Handoff { Delay = delay }; handler = new Handler(package);
        using (var model = CreateModel(new UpdateDownloads(package.Trust, auth, handler), handoff)) {
            model.Configure(auth); model.Context(Request); await Wait(model, AutomaticUpdateStatus.Idle); model.Download(Now); await Wait(model, AutomaticUpdateStatus.Preparing);
            model.Context(new(new UpdateContext(Context.Repository, "Beta", Context.CurrentVersion, Context.ReleaseTag, 123, 456, Context.Platform), 457, 458)); delay.SetResult(new object()); await Task.Delay(30); model.Tick(Now);
            await Wait(model, AutomaticUpdateStatus.Idle); Check(model.Status == AutomaticUpdateStatus.Idle && handoff.Cancels > 0 && handoff.Queues == 0, "channel change retires late candidate");
            auth.Id = "fixture-account-b"; model.Tick(Now); Check(!model.CanDownload && model.Status == AutomaticUpdateStatus.Idle, "account change clears old request");
        }
        using (var restarted = CreateModel(new UpdateDownloads(package.Trust, auth, new Handler(package)), new Handoff())) { restarted.Configure(auth); restarted.Preferences(true, true); await Wait(restarted, AutomaticUpdateStatus.Idle); Check(restarted.Status == AutomaticUpdateStatus.Idle && !restarted.CanCancel, "restart never resumes old ticket"); }
        var queueDelay = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously); handoff = new Handoff { QueueDelay = queueDelay };
        using (var model = CreateModel(new UpdateDownloads(package.Trust, auth, new Handler(package)), handoff)) {
            model.Configure(auth); model.Context(Request); await Wait(model, AutomaticUpdateStatus.Idle); model.Preferences(true, false); await Wait(model, AutomaticUpdateStatus.Ready);
            model.Preferences(true, true); model.Tick(Now); Check(model.Status == AutomaticUpdateStatus.Preparing, "queue worker pending");
            model.Preferences(true, false); queueDelay.SetResult(handoff.Current); await Task.Delay(30); model.Tick(Now);
            Check(model.Status == AutomaticUpdateStatus.Cancelled && handoff.Cancels > 0, "apply off during pending queue retires and cancels helper");
        }

        var persistence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); handoff = new Handoff { CancelDelay = persistence };
        using (var model = CreateModel(new UpdateDownloads(package.Trust, auth, new Handler(package)), handoff)) {
            model.Configure(auth); model.Context(Request); await Wait(model, AutomaticUpdateStatus.Idle); model.Preferences(true, true); await Wait(model, AutomaticUpdateStatus.Queued);
            model.Cancel(); Check(handoff.Signals == 1 && model.Status == AutomaticUpdateStatus.Cancelling && !model.CanDownload && !model.CanCancel, "signal synchronous while persistence pending");
            model.Context(new(new UpdateContext(Context.Repository, "Beta", Context.CurrentVersion, Context.ReleaseTag, 123, 456, Context.Platform), 457, 458)); model.Tick(Now);
            Check(model.Status == AutomaticUpdateStatus.Cancelling && !model.CanDownload && handoff.Prepares == 1, "channel change cannot bypass unconfirmed cancellation");
            persistence.SetException(new IOException("fixture cancel persistence")); await Wait(model, AutomaticUpdateStatus.CancelFailed); Check(!model.CanDownload, "failed cancellation blocks retry");
        }

        handoff = new Handoff();
        using (var model = CreateModel(new UpdateDownloads(package.Trust, auth, new Handler(package)), handoff)) {
            model.Configure(auth); model.Context(Request); await Wait(model, AutomaticUpdateStatus.Idle); model.Preferences(true, true); await Wait(model, AutomaticUpdateStatus.Queued);
            handoff.Current.PollFault = true; model.Tick(Now); Check(model.Status == AutomaticUpdateStatus.RecoveryRequired && !model.CanDownload && handoff.Signals > 0, "uncertain helper observation cancels and requires recovery");
            Check(model.RecoveryStage == new Prepared().Stage, "helper failure retains exact recovery stage without restart");
            Check(ModText.AutomaticDetails(model.Status, model.RecoveryStage).Contains(new Prepared().Stage), "failed helper recovery hint names validated stage");
        }

        foreach (var failure in new[] { false, true }) {
            handoff = new Handoff { Recovery = "bap-v4-stage-" + new string('a', 32), InspectionFails = failure };
            using var model = CreateModel(new UpdateDownloads(package.Trust, auth, new Handler(package)), handoff);
            model.Configure(auth); model.Context(Request); model.Preferences(true, true); await Wait(model, AutomaticUpdateStatus.RecoveryRequired);
            Check(!model.CanDownload && !model.CanCancel && handoff.Queues == 0, "restart/inspection failure blocks automatic apply");
            ModText.Select("English"); Check(ModText.AutomaticStatus(model.Status) == "Recovery Required", "English recovery state");
            if (!failure) Check(ModText.AutomaticDetails(model.Status, model.RecoveryStage).Contains("--recover"), "recovery command hint");
            ModText.Select("한국어"); Check(ModText.AutomaticStatus(model.Status) == "복구 필요", "Korean recovery state");
        }

        var text = Encoding.ASCII.GetString(package.Receipt).Replace("\"files\":[", "\"files\":[{\"path\":\"doorstop_config.ini\",\"sha256\":\"" + new string('A', 64) + "\"},");
        Check(UpdateEligibility.Receipt(Encoding.ASCII.GetBytes(text)) == InstallEligibility.ManualUpgradeRequired, "shared loader legacy receipt requires manual upgrade");
        Check(UpdateVersion.Parse("1.3.0-dev").CompareTo(UpdateVersion.Parse("1.3.0")) < 0, "development promotion ordering");
    }
    private static string Install(string baseRoot, string name, Package package, byte[] inert)
    {
        var root = Path.Combine(baseRoot, name); Directory.CreateDirectory(Path.Combine(root, "BepInEx/plugins/BetterAstralParty"));
        CompatibilityFixtureData.Seed(root,inert);
        foreach (var file in package.Old) File.WriteAllBytes(Path.Combine(root, file.Key), file.Value);
        using (var fence = new WindowsFileFence(root)) package.Receipt = package.CreateReceipt(fence.RootIdentity.Text);
        File.WriteAllBytes(Path.Combine(root, InstallReceipt.Path), package.Receipt); File.WriteAllBytes(Path.Combine(root, "AstralParty_INT.exe"), inert); File.WriteAllText(Path.Combine(root, "other-mod.keep"), "unchanged"); return root;
    }
    private static Process Start(string path, string args) => Process.Start(new ProcessStartInfo { FileName = path, Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }) ?? throw new Exception("Fixture process start");
    private static async Task<(int Code,string Output,string Error)> Observe(Process process,Task<string> output,Task<string> error,string label,Stopwatch elapsed)
    {
        var exit=process.WaitForExitAsync();
        Check(await Task.WhenAny(exit,Task.Delay(15000))==exit,"bounded inert helper exit: "+label);
        await exit;var stdout=await output;var stderr=await error;
        Console.WriteLine("HELPER-PROCESS:"+System.Text.Json.JsonSerializer.Serialize(new { label,pid=process.Id,exitCode=process.ExitCode,elapsedMs=elapsed.ElapsedMilliseconds,
            stdout=stdout.Length>65536?stdout[..65536]+"[truncated]":stdout,stderr=stderr.Length>65536?stderr[..65536]+"[truncated]":stderr }));
        return(process.ExitCode,stdout,stderr);
    }
    private static string GuardHash(WindowsFileFence fence,UpdateTicket ticket)
    {
        var guard=UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath)??throw new Exception("Active helper operation guard disappeared");
        Check(guard.Stage==ticket.Stage && UpdateFaults.Same(guard.Context,ticket.Context) && guard.GamePid==ticket.GamePid && guard.GameCreation==ticket.GameCreation
            && guard.GameSession==ticket.GameSession && guard.LauncherPid==ticket.LauncherPid && guard.LauncherCreation==ticket.LauncherCreation && guard.LauncherSession==ticket.LauncherSession,"ready helper guard binds the exact ticket");
        return UpdateTrust.Hash(guard.Bytes());
    }
    private static async Task<string> Ready(string root,UpdateTicket ticket,Process worker)
    {
        var wait=Stopwatch.StartNew();var hash=UpdateTrust.Hash(ticket.Bytes());
        while(wait.ElapsedMilliseconds<4000) {
            Check(!worker.HasExited,"helper remains alive until readiness");
            try {using var fence=new WindowsFileFence(root);using var ready=fence.OpenFile(ticket.Stage+"/helper.ready");
                Check(Encoding.ASCII.GetString(UpdateTicket.Read(ready,128))==hash+"\n","ready marker binds exact ticket hash");
                return GuardHash(fence,ticket);
            }catch(ApplySafetyException error)when(error.NativeError==2 || error.NativeError==32 || error.NativeError==33){await Task.Delay(20);}
        }
        throw new Exception("Helper readiness timed out before the inert game/launcher deadline");
    }
    private static async Task EndToEnd(string baseRoot, Package package, byte[] inert, string helper)
    {
        File.WriteAllBytes(Path.Combine(baseRoot, "fixture-public-key.bin"), package.Public);
        foreach (var mode in new[] { "promote", "cancel", "failure-signal", "wait-expired", "classification-changed", "classification-promoted", "classification-unavailable" }) {
            var cancel = mode != "promote"; var root = Install(baseRoot, mode, package, inert);
            var operationContext = mode == "classification-promoted" ? new UpdateContext(Context.Repository, "PreRelease", Context.CurrentVersion, Context.ReleaseTag, Context.ReleaseId, Context.AssetId, Context.Platform, repositoryId: Context.RepositoryId) : Context;
            var operationDescriptor = mode == "classification-promoted" ? Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(package.Descriptor).Replace("purpose=release/Stable\n", "purpose=" + ReleaseFeedPolicy.PublicPurpose + "\n")) : package.Descriptor;
            var operationSignature = mode == "classification-promoted" ? package.Sign(operationDescriptor) : package.Signature;
            if (mode == "classification-promoted") {
                var receiptPath = Path.Combine(root, InstallReceipt.Path); var bytes = File.ReadAllBytes(receiptPath); var old = InstallReceipt.Parse(bytes).Channels!;
                var state = new ReceiptChannelState(old.Root, "PreRelease", old.Operation, 0, null, "-", 0, null, "-", Context.RepositoryId, Context.CurrentVersion, new string('A', 64));
                File.WriteAllBytes(receiptPath, Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace(old.Encoded, state.Encoded)));
            }
            using var game = Start(Path.Combine(root, "AstralParty_INT.exe"), "5000");
            using var launcher = Start(Path.Combine(root, "BetterAstralParty-Launcher.exe"), "5000");
            UpdateTicket ticket;
            using (var fence = new WindowsFileFence(root)) {
                var staged = UpdateStaging.Create(fence, operationDescriptor, "helper-fixture", operationSignature, package.Zip, package.Trust, operationContext);
                ticket = new() { RootId = fence.RootIdentity.Text, Stage = staged.Directory, StageId = staged.DirectoryIdentity.Text, DescriptorHash = staged.DescriptorSha256,
                    Context = operationContext, GamePid = (uint)game.Id, GameCreation = game.StartTime.ToFileTimeUtc(), GameSession = (uint)game.SessionId,
                    LauncherPid = (uint)launcher.Id, LauncherCreation = launcher.StartTime.ToFileTimeUtc(), LauncherSession = (uint)launcher.SessionId };
                UpdateTicket.Write(fence, ticket.Stage + "/handoff.ticket", ticket.Bytes());
            }
            using var signal = UpdateCancelSignal.Create(UpdateTrust.Hash(ticket.Bytes()));
            var start=new ProcessStartInfo(helper) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("--apply");start.ArgumentList.Add(root);start.ArgumentList.Add(ticket.Stage);start.ArgumentList.Add(Path.Combine(baseRoot,"fixture-public-key.bin"));
            if(mode=="wait-expired") start.Environment["BAP_FIXTURE_MAX_WAIT_MS"]="600";
            var classification = mode.StartsWith("classification-", StringComparison.Ordinal);
            var metadataPath = Path.Combine(root, "public-release.fixture.json");
            if (classification) {
                var metadata = "{\"id\":123,\"draft\":false,\"prerelease\":" + (operationContext.Channel == "PreRelease" ? "true" : "false") + ",\"tag_name\":\"1.3.0\",\"html_url\":\"https://github.com/" + Context.Repository
                    + "/releases/tag/1.3.0\",\"assets\":[{\"id\":456,\"name\":\"BetterAstralParty-1.3.0-update.zip\"},{\"id\":457,\"name\":\"" + ReleaseFeedPolicy.UpdateManifestAsset + "\"},{\"id\":458,\"name\":\"" + ReleaseFeedPolicy.UpdateSignatureAsset + "\"}]}";
                File.WriteAllText(metadataPath, metadata, new UTF8Encoding(false)); start.Environment["BAP_FIXTURE_PUBLIC_RELEASE"] = metadataPath;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, ChannelTransitionIntent.SettingsPath))!);
                File.WriteAllText(Path.Combine(root, ChannelTransitionIntent.SettingsPath), "[업데이트]\n자동 다운로드=true\n종료 후 적용=true\n", new UTF8Encoding(false));
            }
            using var worker=Process.Start(start)!;
            var elapsed=Stopwatch.StartNew();var workerOutput=worker.StandardOutput.ReadToEndAsync();var workerError=worker.StandardError.ReadToEndAsync();var observed=false;
            try {
                var originalGuard=await Ready(root,ticket,worker);
                Check(!game.HasExited && !launcher.HasExited,"ready helper is waiting for both original inert processes");
                if (classification) {
                    Check(!File.Exists(Path.Combine(root, ticket.Stage, "helper.attempt")), "live classification not consumed while original game is alive");
                    if (mode == "classification-changed") File.WriteAllText(metadataPath, File.ReadAllText(metadataPath).Replace("\"prerelease\":false", "\"prerelease\":true"), new UTF8Encoding(false));
                    else if (mode == "classification-promoted") File.WriteAllText(metadataPath, File.ReadAllText(metadataPath).Replace("\"prerelease\":true", "\"prerelease\":false"), new UTF8Encoding(false));
                    else File.Delete(metadataPath);
                }
                if(mode=="promote") using (var duplicate = Start(helper, "--apply \"" + root + "\" " + ticket.Stage + " \"" + Path.Combine(baseRoot, "fixture-public-key.bin") + "\"")) {
                    var duplicateResult=await Observe(duplicate,duplicate.StandardOutput.ReadToEndAsync(),duplicate.StandardError.ReadToEndAsync(),mode+"/duplicate",elapsed);
                    Check(duplicateResult.Code==7 && !worker.HasExited,"active duplicate reports FilesBusy without ending its owner: "+duplicateResult.Output+duplicateResult.Error);
                    using var fence=new WindowsFileFence(root);
                    Check(GuardHash(fence,ticket)==originalGuard,"duplicate cannot retire or replace the borrowed active guard");
                    try {using var claim=fence.OpenFile(ticket.Stage+"/helper.claim");throw new Exception("Active helper claim is not held exclusively");}
                    catch(ApplySafetyException error)when(error.Failure==ApplyFailure.FileBusy && (error.NativeError==32 || error.NativeError==33)){Check(true,"active owner retains exclusive claim lease");}
                    Check(UpdateFaults.ReadFile(fence,UpdateFaults.FailurePath)==null && !File.Exists(Path.Combine(root,ticket.Stage,"helper.result")),"duplicate creates no terminal result or failure latch");
                }
                if (mode=="cancel") signal.Set();
                if (mode=="failure-signal") { signal.SetFailure(); signal.Dispose(); }
                if (mode=="cancel") using (var fence = new WindowsFileFence(root)) UpdateTicket.Write(fence, ticket.Stage + "/helper.cancel", Encoding.ASCII.GetBytes(UpdateTrust.Hash(ticket.Bytes()) + "\n"));
                await game.WaitForExitAsync(); await launcher.WaitForExitAsync(); game.Dispose(); launcher.Dispose();
                var result=await Observe(worker,workerOutput,workerError,mode+"/owner",elapsed);observed=true;
                Check(result.Code == (mode=="failure-signal" ? 12 : mode=="wait-expired" ? 7 : cancel ? 3 : 0), "helper result "+mode+": "+result.Output+result.Error);
            } finally {
                if(!observed)try{await Observe(worker,workerOutput,workerError,mode+"/failed-owner-observation",elapsed);}catch(Exception error){Console.Error.WriteLine("HELPER-OBSERVATION:"+mode+":"+error);}
            }
            Check(InstalledPluginMetadata.Read(File.ReadAllBytes(Path.Combine(root, UpdateTrust.PluginPath))).Version == (cancel ? "1.3.0-dev" : "1.3.0"), "helper independent metadata promotion/cancel");
            foreach (var file in cancel ? package.Old : package.New) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, file.Key))) == UpdateTrust.Hash(file.Value), "end-to-end exact owned payload");
            using (var replay = Start(helper, "--apply \"" + root + "\" " + ticket.Stage + " \"" + Path.Combine(baseRoot, "fixture-public-key.bin") + "\"")) {
                var replayResult=await Observe(replay,replay.StandardOutput.ReadToEndAsync(),replay.StandardError.ReadToEndAsync(),mode+"/replay",elapsed);
                Check(replayResult.Code == (mode=="failure-signal" ? 12 : 8), "closed claimed/faulted ticket never replays after restart: "+replayResult.Output+replayResult.Error);
            }
            if(mode=="promote") {
                using var fence=new WindowsFileFence(root);using var retained=fence.OpenFile(ticket.Stage+"/helper.result",true);
                var resultHash=retained.Hash();retained.RenameTo(fence,ticket.Stage+"/fixture-retained-helper-result");retained.Flush();
                try {
                    using var closedClaim=Start(helper,"--apply \""+root+"\" "+ticket.Stage+" \""+Path.Combine(baseRoot,"fixture-public-key.bin")+"\"");
                    var replayResult=await Observe(closedClaim,closedClaim.StandardOutput.ReadToEndAsync(),closedClaim.StandardError.ReadToEndAsync(),mode+"/closed-claim-without-result",elapsed);
                    Check(replayResult.Code==8,"closed valid claim alone prevents a second apply: "+replayResult.Output+replayResult.Error);
                    Check(UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath)==null && UpdateFaults.ReadFile(fence,UpdateFaults.FailurePath)==null
                        && !File.Exists(Path.Combine(root,ticket.Stage,"helper.result")),"closed-claim inspection creates no operation, failure or result");
                } finally {retained.RenameTo(fence,ticket.Stage+"/helper.result");retained.Flush();}
                Check(retained.Hash()==resultHash,"one-shot claim check restores original terminal evidence exactly");
            }
            Check(File.ReadAllText(Path.Combine(root, "other-mod.keep")) == "unchanged", "unrelated mod preserved");
            if (classification) {
                Check(File.ReadAllText(Path.Combine(root, ChannelTransitionIntent.SettingsPath)) == "[업데이트]\n자동 다운로드=true\n종료 후 적용=true\n", "public reclassification/unavailable metadata preserves automatic update preferences");
                Check(!File.Exists(Path.Combine(root, ticket.Stage, "helper.attempt")), "public classification is checked after exit and before transaction attempt/payload mutation");
                var bundle = BetterAstralParty.Diagnostics.DiagnosticBundle.Collect(root, Path.Combine(root, "absent-logs"), Path.Combine(root, "absent-fallback"), Path.Combine(root, "absent-unity"),
                    Path.Combine(baseRoot, "classification-diagnostics"), Path.Combine(baseRoot, "classification-fallback"), "", "1.3.0");
                using var zip = ZipFile.OpenRead(bundle.ArchivePath);
                var log = string.Join("\n", zip.Entries.Where(entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal)).Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }));
                Check(log.Contains("ReleaseMetadata") && log.Contains(mode == "classification-unavailable" ? "Transport" : "WrongTarget"), "post-exit public eligibility cause survives source-to-diagnostic-ZIP collection");
            }
            using(var fence=new WindowsFileFence(root)) {
                Check((UpdateFaults.ReadFile(fence,UpdateFaults.FailurePath)!=null)==(mode=="failure-signal"),"signal failure versus ordinary wait/cancel");
                Check(UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath)==null,"normal wait/cancel/signal terminal guard retirement");
                if(mode=="failure-signal") Check(!File.Exists(Path.Combine(root,ticket.Stage,"helper.cancel")),"helper-held failure event survives creator closure without disk cancel marker");
            }
        }
    }


    private sealed class ReleasesHandler(string assets) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            RequestMessage = request, Content = new StringContent("[{\"id\":123,\"draft\":false,\"prerelease\":false,\"tag_name\":\"1.3.0\",\"html_url\":\"https://github.com/TSM701/BetterAstralParty/releases/tag/1.3.0\",\"body\":\"fixture changes\",\"assets\":" + assets + "}]", Encoding.UTF8, "application/json") });
    }
    private static async Task AssetContract()
    {
        var good = "[{\"id\":456,\"name\":\"BetterAstralParty-1.3.0-update.zip\"},{\"id\":457,\"name\":\"" + ReleaseFeedPolicy.UpdateManifestAsset + "\"},{\"id\":458,\"name\":\"" + ReleaseFeedPolicy.UpdateSignatureAsset + "\"}]";
        foreach (var feed in new[] { ReleaseChannel.Stable, ReleaseChannel.Beta })
        foreach (var assets in new[] { good, "[]", good.Replace("458", "457"), good.Replace("458", "-1"), good.Replace("\"id\":458", "\"id\":\"458\""), good.Replace(ReleaseFeedPolicy.UpdateSignatureAsset, "wrong-name") }) {
            using var client = new HttpClient(new ReleasesHandler(assets)); using var releases = new ReleaseUpdates("1.3.0-dev", "TSM701/BetterAstralParty", client, new Auth());
            releases.SetChannel(feed); releases.Check(Now); for (var i = 0; i < 200 && releases.Result.Status == ReleaseUpdateStatus.Checking; i++) { releases.Tick(Now, false); await Task.Delay(5); }
            Check(releases.Result.Status == ReleaseUpdateStatus.Available, "asset contract preserves notification"); var request = releases.Result.Release!.DownloadRequest("1.3.0-dev", feed);
            Check(assets == good ? request != null && request.Context.AssetId == 456 && request.Context.Channel == "Stable" : request == null, "asset contract gated download");
        }
        var beta = new ReleaseUpdate("1.0.1-beta.1", "fixture", "fixture", new Uri("https://github.com/TSM701/BetterAstralParty/releases/tag/1.0.1-beta.1")) { Beta = true, ReleaseId = 123, ZipAssetId = 456, DescriptorAssetId = 457, SignatureAssetId = 458 };
        Check(beta.DownloadRequest("1.3.0-dev", ReleaseChannel.Stable) == null, "stable feed rejects beta request");
        Check(beta.DownloadRequest("1.3.0-dev", ReleaseChannel.Beta)?.Context.Channel == "Beta", "beta descriptor context");
    }

    private sealed class BytesHandler(byte[] descriptor, byte[] envelope, byte[] zip) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            RequestMessage = request, Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith("/457") ? descriptor : request.RequestUri.AbsolutePath.EndsWith("/458") ? envelope : zip) });
    }
    private sealed class DeferredPersistence(UpdateHandoff actual) : IUpdateHandoff
    {
        public bool Configured => actual.Configured;
        public Task<object> Prepare(DownloadedUpdate value, UpdateContext context, CancellationToken token) => actual.Prepare(value, context, token);
        public Task<object> Queue(object value, CancellationToken token) => actual.Queue(value, token);
        public void SignalCancel(object value) => actual.SignalCancel(value);
        public Task Cancel(object value) => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        public Task<string?> Inspect() => actual.Inspect();
    }
    private static async Task<int> BackendHost(string[] args)
    {
        var root = Path.GetFullPath(args[1]);
        if (!root.StartsWith(Path.GetFullPath(Environment.GetEnvironmentVariable("BAP_HELPER_FIXTURE_ROOT") ?? throw new Exception("Fixture root missing")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Backend fake roots only");
        var files = Path.GetDirectoryName(root)!;
        var key = new RSAParameters { Modulus = File.ReadAllBytes(Path.Combine(files, "fixture-public-key.bin")), Exponent = [1, 0, 1] };
        var trust = new UpdateTrust([new TrustedUpdateKey("helper-fixture", key)]); var auth = new Auth();
        var actual = new UpdateHandoff(root, trust, UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, "BetterAstralParty-UpdateHelper.exe"))));
        IUpdateHandoff handoff = args[2] == "cancel-before-record" ? new DeferredPersistence(actual) : actual;
        var cfg = new BepInEx.Configuration.ConfigFile(Path.Combine(root, "preferences.cfg"), true); var dl = cfg.Bind("Update", "Download", true); var ap = cfg.Bind("Update", "Apply", true); cfg.SaveOnConfigSet = false;
        var safety = new FileUpdateSafety(root, (d,a) => { dl.Value=d; ap.Value=a; cfg.Save(); return Task.CompletedTask; });
        var model = new AutomaticUpdates(new UpdateDownloads(trust, auth, new BytesHandler(File.ReadAllBytes(Path.Combine(files, "descriptor.bin")), File.ReadAllBytes(Path.Combine(files, "signature.envelope")), File.ReadAllBytes(Path.Combine(files, "package.zip")))), handoff, safety); model.Preferences(false,false);
        model.Configure(auth); model.Context(Request); await Wait(model, AutomaticUpdateStatus.Idle); model.Preferences(true, true);         if(args[2]=="missing-receipt") {await Wait(model,AutomaticUpdateStatus.ManualUpgradeRequired);Check(model.AutoDownload&&model.ApplyAfterExit,"missing receipt preserves both enabled settings");await Pump(model,()=>{using var fence=new WindowsFileFence(root);return UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath)==null;});Console.WriteLine("BACKEND:missing-receipt");return 0;}
await Wait(model, AutomaticUpdateStatus.Queued);
        if (args[2].StartsWith("cancel-", StringComparison.Ordinal)) {
            model.Cancel(); Check(model.Status == AutomaticUpdateStatus.Cancelling, "cancel awaits durable record");
            if (args[2] == "cancel-recorded") await Wait(model, AutomaticUpdateStatus.Cancelled);
            var stage = WorkDirectories(root, "stage").Single(); Check(File.Exists(Path.Combine(stage, "helper.cancel")) == (args[2] == "cancel-recorded"), "disk cancellation state before parent exit");
        }
        Console.WriteLine("BACKEND:" + args[2]); return 0; // Natural process exit, no Dispose that would cancel an opted-in apply.
    }
    private static async Task BackendEndToEnd(string baseRoot, Package package, byte[] inert, string helper)
    {
        var binary = Path.GetDirectoryName(typeof(HelperFixture).Assembly.Location)!;
        package.RebindGame(File.ReadAllBytes(Path.Combine(binary,"BetterAstralParty.HelperTests.exe")));
        File.WriteAllBytes(Path.Combine(baseRoot, "descriptor.bin"), package.Descriptor); File.WriteAllBytes(Path.Combine(baseRoot, "signature.envelope"), package.Envelope); File.WriteAllBytes(Path.Combine(baseRoot, "package.zip"), package.Zip);
        foreach (var mode in new[] { "apply", "game-updated", "lease-race", "incompatible", "missing-receipt", "cancel-before-record", "cancel-recorded", "fail-prepared", "fail-before-intent", "fail-after-intent", "fail-corrupt", "fail-storage" }) {
            var root = Install(baseRoot, "backend-" + mode, package, inert);
            File.Copy(Path.Combine(binary, "BetterAstralParty.HelperTests.exe"), Path.Combine(root, "AstralParty_INT.exe"), true);
            foreach (var file in Directory.GetFiles(binary)) if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            File.Copy(helper, Path.Combine(root, "BetterAstralParty-UpdateHelper.exe"));
            if(mode=="missing-receipt") File.Delete(Path.Combine(root,InstallReceipt.Path));
            if(mode=="incompatible") File.WriteAllText(Path.Combine(root,"winhttp.dll"),"changed shared loader fixture");
            if(mode=="game-updated") {
                File.WriteAllText(Path.Combine(root,"GameAssembly.dll"),"updated game fixture");
                File.WriteAllText(Path.Combine(root,"fixture-cache/catalog_3.4.0.json"),"updated catalog fixture");
            }
            var start = new ProcessStartInfo(Path.Combine(root, "AstralParty_INT.exe")) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardError=true, RedirectStandardOutput=true };
            start.ArgumentList.Add("--backend"); start.ArgumentList.Add(root); start.ArgumentList.Add(mode);
            if(mode=="lease-race") start.Environment["BAP_FIXTURE_GAME_LEASE_RACE"]="1";
            if (mode.StartsWith("fail-",StringComparison.Ordinal)) {
                start.Environment["BAP_FIXTURE_APPLY_BOUNDARY"] = mode == "fail-before-intent" ? "before:commit-intent" : mode == "fail-after-intent" ? "after:commit-intent" : "after:prepared";
                if(mode=="fail-corrupt") start.Environment["BAP_FIXTURE_CORRUPT_PLAN"]="1";
                if(mode=="fail-storage") start.Environment["BAP_FIXTURE_FAULT_STORAGE"]="1";
            }
            using var game = Process.Start(start)!;
            await game.WaitForExitAsync(); var error = await game.StandardError.ReadToEndAsync(); Check(game.ExitCode == 0, "actual backend child " + error); game.Dispose();
            if(mode=="missing-receipt") {
                using(var fence=new WindowsFileFence(root)) Check(UpdateFaults.Read(fence)==null,"coordinator missing receipt no fault/operation guard");
                Check(!WorkDirectories(root, "stage").Any(),"coordinator missing receipt no staging");continue;
            }
            var stage = WorkDirectories(root, "stage").Single(path => File.Exists(Path.Combine(path, "handoff.ticket"))); var result = Path.Combine(stage, "helper.result");
            string? text = null;
            for (var i = 0; i < 500 && text == null; i++) { try { if (File.Exists(result)) text = File.ReadAllText(result); } catch (IOException) { } if (text == null) await Task.Delay(20); }
            Check(text != null && text.EndsWith(mode is "apply" or "game-updated" or "incompatible" ? "Committed\n" : mode=="lease-race" ? "FilesBusy\n" : mode is "fail-corrupt" or "fail-storage" ? "RecoveryRequired\n" : mode.StartsWith("fail-",StringComparison.Ordinal) ? "FaultDisabled\n" : "Cancelled\n", StringComparison.Ordinal), "actual backend helper result " + text);
            foreach (var file in mode is "apply" or "game-updated" or "incompatible" or "fail-after-intent" ? package.New : package.Old) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, file.Key))) == UpdateTrust.Hash(file.Value), "actual backend owned hashes " + mode);
            if(mode=="incompatible") Check(File.ReadAllText(Path.Combine(root,"winhttp.dll"))=="changed shared loader fixture","an unrecognized foreign loader never blocks the owned update or gets overwritten");
            if(mode=="game-updated") Check(File.ReadAllText(Path.Combine(root,"GameAssembly.dll"))=="updated game fixture" && File.ReadAllText(Path.Combine(root,"fixture-cache/catalog_3.4.0.json"))=="updated catalog fixture","automatic coordinator/helper preserve updated game and cache bytes");
            Check(File.ReadAllText(Path.Combine(root, "other-mod.keep")) == "unchanged", "actual backend unrelated mod");
            if(mode is "lease-race" or "incompatible") {
                var preserved=new BepInEx.Configuration.ConfigFile(Path.Combine(root,"preferences.cfg"),false);
                Check(preserved.Bind("Update","Download",false).Value && preserved.Bind("Update","Apply",false).Value,"game lease/compatibility prerequisites preserve saved ON settings");
            }
            if (mode == "cancel-before-record") Check(!File.Exists(Path.Combine(stage, "helper.cancel")), "helper-held signal survives parent exit without persisted cancellation");
            using(var fence = new WindowsFileFence(root)) {
                var fault = mode=="fail-storage" ? null : UpdateFaults.ReadFile(fence,UpdateFaults.FailurePath);
                Check((fault!=null)==(mode.StartsWith("fail-",StringComparison.Ordinal) && mode!="fail-storage"),"helper persistent fault classification "+mode);
                if(mode is not ("fail-corrupt" or "fail-storage")) Check(UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath)==null,"helper terminal guard retired "+mode);
                else Check(UpdateFaults.ReadFile(fence,UpdateFaults.OperationPath)!=null,"corrupt recovery retains guard");
            }
            if(mode.StartsWith("fail-",StringComparison.Ordinal)) {
                var cfg=new BepInEx.Configuration.ConfigFile(Path.Combine(root,"preferences.cfg"),false);var d=cfg.Bind("Update","Download",true);var a=cfg.Bind("Update","Apply",true);cfg.SaveOnConfigSet=false;
                var safety=new FileUpdateSafety(root,(dl,ap)=>{d.Value=dl;a.Value=ap;cfg.Save();return Task.CompletedTask;});
                using var restarted=new AutomaticUpdates(UpdateDownloads.Production(channel: ReleaseChannel.Beta),new Handoff(),safety);
                restarted.Configure(null);
                await Pump(restarted,()=>!d.Value && !a.Value && restarted.Status is AutomaticUpdateStatus.FaultDisabled or AutomaticUpdateStatus.RecoveryRequired);
                Check(!restarted.AutoDownload && !restarted.ApplyAfterExit && !restarted.CanDownload,"helper fault OFF on restart "+mode);
                if(mode is "fail-corrupt" or "fail-storage") Check(!restarted.CanReactivate && restarted.Status==AutomaticUpdateStatus.RecoveryRequired,"corrupt plan remains manual recovery");
                else { await Pump(restarted,()=>restarted.CanReactivate); restarted.Reactivate(); await Pump(restarted,()=>restarted.AutoDownload && restarted.Status==AutomaticUpdateStatus.NotConfigured); }
                Check(File.ReadAllText(Path.Combine(root,"other-mod.keep"))=="unchanged","fault recovery unrelated preserved");
            }
        }
        package.RebindGame(inert);
    }
    private sealed class Interruption : Exception { }
    private static async Task RecoveryEndToEnd(string baseRoot, Package package, byte[] inert, string helper)
    {
        foreach (var label in new[] { "after:prepared", "before:commit-intent", "after:commit-intent", "corrupt-plan" }) {
            var root = Install(baseRoot, "recover-" + label.Replace(':', '-'), package, inert); string stage;
            using (var fence = new WindowsFileFence(root)) {
                var staged = UpdateStaging.Create(fence, package.Descriptor, "helper-fixture", package.Signature, package.Zip, package.Trust, Context); stage = staged.Directory;
                using var current = Start(Path.Combine(root, "AstralParty_INT.exe"), "250");
                var ticket = new UpdateTicket { RootId = fence.RootIdentity.Text, Stage = stage, StageId = staged.DirectoryIdentity.Text, DescriptorHash = staged.DescriptorSha256,
                    Context = Context, GamePid = (uint)current.Id, GameCreation = current.StartTime.ToFileTimeUtc(), GameSession = (uint)current.SessionId };
                UpdateTicket.Write(fence, stage + "/handoff.ticket", ticket.Bytes());
                await current.WaitForExitAsync();
                UpdateFaults.Begin(fence, Context, FaultPhase.Apply, ticket);
                var work = UpdateWorkPath.New("txn");
                UpdateTicket.Write(fence, stage + "/helper.attempt", HelperRecoveryBinding.Attempt(UpdateTrust.Hash(ticket.Bytes()), work));
                try { UpdateTransaction.Apply(fence, package.Descriptor, "helper-fixture", package.Signature, package.Zip, package.Trust, Context, value => { if (value == (label == "corrupt-plan" ? "after:prepared" : label)) throw new Interruption(); }, attemptWork: work); }
                catch (Interruption) { }
            }
            var backend = new UpdateHandoff(root, package.Trust, new string('A', 64));
            Check(await backend.Inspect() == stage, "restart discovers pending handoff");
            if (label == "corrupt-plan") {
                var plan = Path.Combine(WorkDirectories(root, "txn").Single(), "plan"); var bytes = File.ReadAllBytes(plan); bytes[0] = (byte)'X'; File.WriteAllBytes(plan, bytes);
            }

            if (label == "after:prepared") {
                using var game = Start(Path.Combine(root, "AstralParty_INT.exe"), "1500");
                using var blocked = Start(helper, "--recover \"" + root + "\" " + stage + " \"" + Path.Combine(baseRoot, "fixture-public-key.bin") + "\"");
                await blocked.WaitForExitAsync(); Check(blocked.ExitCode == 5 && !game.HasExited, "recovery blocks exact running fake game");
                foreach (var file in package.Old) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, file.Key))) == UpdateTrust.Hash(file.Value), "blocked recovery preserves originals");
                await game.WaitForExitAsync(); game.Dispose();
            }

            for (var attempt = 0; attempt < 2; attempt++) {
                using var process = Start(helper, "--recover \"" + root + "\" " + stage + " \"" + Path.Combine(baseRoot, "fixture-public-key.bin") + "\""); await process.WaitForExitAsync(); var error = await process.StandardError.ReadToEndAsync();
                Check(process.ExitCode == (label == "corrupt-plan" ? 9 : 0), "recovery CLI outcome/restart " + label + " " + error);
            }
            foreach (var file in label == "after:commit-intent" ? package.New : package.Old) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, file.Key))) == UpdateTrust.Hash(file.Value), "recovery exact payload " + label);
            Check(File.ReadAllText(Path.Combine(root, "other-mod.keep")) == "unchanged", "recovery preserves unowned");
            if (label != "corrupt-plan") Check(await backend.Inspect() == null, "terminal recovery no pending");
            Check(WorkDirectories(root, "txn").Length == 1, "recovery backups retained");
        }
    }


    private static async Task FaultPolicyPrototype(string baseRoot)
    {
        var root = Path.Combine(baseRoot, "fault-policy"); Directory.CreateDirectory(root);
        using var fence = new WindowsFileFence(root);
        Check(UpdateFaults.Read(fence) == null, "missing fault/guard record");
        var guard = UpdateFaults.Begin(fence, Context, FaultPhase.Download);
        Check(UpdateFaults.Read(fence)?.Reason == FaultReason.Interrupted, "durable operation guard survives missing final result");
        try { UpdateFaults.Begin(fence, Context, FaultPhase.Apply); throw new Exception("duplicate operation allowed"); } catch (ApplySafetyException) { Check(true, "second instance cannot overwrite operation guard"); }
        var wrong = UpdateFault.Parse(guard.Bytes()); wrong.Operation = new string('e', 32);
        try { UpdateFaults.Complete(fence, wrong); throw new Exception("wrong guard completion allowed"); } catch (ApplySafetyException) { Check(true, "guard completion binds exact operation"); }
        UpdateFaults.Complete(fence, guard); Check(UpdateFaults.Read(fence) == null, "successful/cancelled operation guard archived");
        Check(Directory.GetFiles(root, "bap-operation-done-*").Length == 1, "guard evidence retained");
        guard = UpdateFaults.Begin(fence, Context, FaultPhase.Apply);
        var failed = UpdateFaults.Fail(fence, Context, FaultPhase.Verification, FaultReason.Integrity);
        Check(UpdateFaults.Read(fence)?.Reason == FaultReason.Integrity, "failure record has priority over in-flight guard");
        Check(UpdateFaults.Fail(fence, Context, FaultPhase.Download, FaultReason.Network).Operation == failed.Operation, "existing failure never overwritten by competing writer");
        UpdateFaults.SaveFailed(fence, failed); Check(UpdateFaults.Read(fence)?.PreferencesSaveFailed == true, "separate preferences-save failure evidence");
        using (var reopened = new WindowsFileFence(root)) Check(UpdateFaults.Read(reopened)?.Operation == failed.Operation && UpdateFaults.Read(reopened)?.PreferencesSaveFailed == true, "fault survives reopened instance");
        UpdateFaults.Complete(fence, guard); UpdateFaults.Acknowledge(fence); Check(UpdateFaults.Read(fence) == null, "explicit acknowledgement archives known evidence");
        var tasks = Enumerable.Range(0, 2).Select(index => Task.Run(() => { using var other = new WindowsFileFence(root); return UpdateFaults.Fail(other, Context, FaultPhase.Download, index == 0 ? FaultReason.Network : FaultReason.Timeout); })).ToArray();
        var records = await Task.WhenAll(tasks); Check(records[0].Operation == records[1].Operation, "concurrent helper/mod failure writers retain one immutable latch");
        var cfgPath = Path.Combine(root, "fresh.cfg");
        var cfg = new BepInEx.Configuration.ConfigFile(cfgPath, false) { SaveOnConfigSet = false };
        var download = cfg.Bind("업데이트", "자동 다운로드", true, "fixture"); var apply = cfg.Bind("업데이트", "종료 후 적용", true, "fixture");
        Check(download.Value && apply.Value, "real BepInEx config fresh defaults ON"); download.Value = false; apply.Value = false; cfg.Save();
        var reload = new BepInEx.Configuration.ConfigFile(cfgPath, false) { SaveOnConfigSet = false };
        Check(!reload.Bind("업데이트", "자동 다운로드", true, "fixture").Value && !reload.Bind("업데이트", "종료 후 적용", true, "fixture").Value, "real BepInEx config stored OFF retained with new ON defaults");
        var badPath = Path.Combine(root, "blocked.cfg"); var bad = new BepInEx.Configuration.ConfigFile(badPath, false) { SaveOnConfigSet = false };
        bad.Bind("업데이트", "자동 다운로드", true, "fixture").Value = false; bad.Bind("업데이트", "종료 후 적용", true, "fixture").Value = false; Directory.CreateDirectory(badPath);
        try { bad.Save(); throw new Exception("blocked config save accepted"); } catch (UnauthorizedAccessException) { Check(true, "real preferences OFF save failure injected"); } catch (IOException) { Check(true, "real preferences OFF save failure injected"); }
        var record = UpdateFaults.Read(fence)!; UpdateFaults.SaveFailed(fence, record);
        using (var reopened = new WindowsFileFence(root)) Check(UpdateFaults.Read(reopened)?.PreferencesSaveFailed == true, "independent fault latch remains when config save fails");
        Check(File.ReadAllText(cfgPath).Contains("false", StringComparison.OrdinalIgnoreCase), "separate successful OFF config unchanged");
        Console.WriteLine("PASS: fault/guard persistence primitives and real configuration storage.");
    }

    private static async Task<int> Main(string[] args)
    {
        try {
            if (args.Length == 7 && args[0] == "--handoff-setup") return StartBoundarySetup(args);
            if (args.Length == 3 && args[0] == "--handoff-boundary") return await StartBoundaryHost(args);
            if (args.Length == 3 && args[0] == "--backend") return await BackendHost(args);
            if (args.Length == 3 && args[0] == "--receipt-backend") return await ReceiptBackendHost(args);
            if (args.Length == 4 && args[0] == "--whole-inputs") {
                WholeInstallBridge.ValidateInputs(args[1], args[2], args[3]);
                Console.WriteLine("PASS: whole inputs authenticated with production public keys; no installation or target mutation."); return 0;
            }
            var focusedRemoved = args.Length == 6 && args[0] == "--focused-removed";
            if (focusedRemoved) args = args.Skip(1).ToArray();
            if (args.Length != 5) throw new Exception("Fixture needs fresh root, old DLL, stable DLL, inert EXE and fixture helper");
            var root = Path.GetFullPath(args[0]); if (!root.StartsWith(Path.GetFullPath(Environment.GetEnvironmentVariable("BAP_HELPER_FIXTURE_ROOT") ?? throw new Exception("Fixture root missing")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !(root.EndsWith("\\fixtures", StringComparison.OrdinalIgnoreCase) || root.EndsWith("\\f", StringComparison.OrdinalIgnoreCase)) || Directory.Exists(root)) throw new Exception("Fresh isolated fixture root only");
            Directory.CreateDirectory(root); var inert = File.ReadAllBytes(args[3]);
            using var package = new Package(File.ReadAllBytes(args[1]), File.ReadAllBytes(args[2]), inert);
            if (focusedRemoved) {
                RemovedDeliveryEvidence(root, package, inert, File.ReadAllBytes(args[4]));
                Console.WriteLine("PASS: removed helper evidence " + _checks + " parent assertions; fake install only; no game or Steam mutation."); return 0;
            }
            await Lifecycle(package); await AssetContract(); await EndToEnd(root, package, inert, args[4]); await BackendEndToEnd(root, package, inert, args[4]); await RecoveryEndToEnd(root, package, inert, args[4]); await FaultPolicyPrototype(root); await PolicyIntegration(package, root, inert); await DeliveryIntegration(root, package, inert, args[4]); await ReviewIntegration(root, package, inert, args[4]);
            await ReceiptBoundaryIntegration(root, package, inert, args[4]);
            Console.WriteLine("PASS: helper/UI " + _checks + " parent assertions; child assertions reported separately; fake install only; 1.3.0-dev -> 1.3.0; original inert game/launcher natural exit; no audio or force-close."); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
