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
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterAstralParty;
using BetterAstralParty.Observability;
using BetterAstralParty.Updating;

internal enum LauncherRecoveryStatus { NotChecked, Checking, Available, Ready, NoPatch, Incompatible, AuthenticationRequired, AccessUnavailable, RateLimited, Incomplete, VerificationFailed, Failed, Cancelled, FilesBusy, RecoveryRequired, Queued }
internal sealed class LauncherRecoveryFailure : Exception
{
    internal readonly LauncherRecoveryStatus Status;
    internal LauncherRecoveryFailure(LauncherRecoveryStatus status) : base("Launcher recovery: " + status) { Status = status; }
}
internal sealed class LauncherRecoveryInstallation
{
    internal string Version = "", ActiveChannel = "", Channel = "Stable", ReceiptHash = "", SettingsHash = "-", DescriptorHash = "", FaultHash = "-";
    internal bool AutoDownload = true, ApplyAfterExit = true, Korean, SettingsReadable;
}
internal sealed class LauncherRecoveryRelease
{
    internal string Version = "", Notes = "";
    internal long Id;
    internal readonly Dictionary<string, long> Assets = new Dictionary<string, long>(StringComparer.Ordinal);
}
internal sealed class LauncherRecoveryPrepared
{
    internal readonly LauncherRecoveryRelease Release;
    internal readonly LauncherRecoveryInstallation Installation;
    internal readonly byte[] Descriptor, Signature, Zip, Manifest, HelperDescriptor, HelperSignature, HelperImage;
    internal readonly string Key;
    internal readonly VerifiedUpdateDescriptor Verified;
    internal readonly ChannelTransitionReview? Transition;
    internal LauncherRecoveryPrepared(LauncherRecoveryRelease release, LauncherRecoveryInstallation installation, byte[] descriptor, DetachedSignature signature, byte[] zip,
        byte[] manifest, byte[] helperDescriptor, byte[] helperSignature, byte[] helperImage, VerifiedUpdateDescriptor verified, ChannelTransitionReview? transition)
    { Release = release; Installation = installation; Descriptor = descriptor; Signature = signature.Signature; Key = signature.KeyId; Zip = zip; Manifest = manifest;
        HelperDescriptor = helperDescriptor; HelperSignature = helperSignature; HelperImage = helperImage; Verified = verified; Transition = transition; }
}

// Network work stays off the UI thread; no game-owned files change before an exact verified offer is accepted.
internal sealed class LauncherRecoveryClient : IDisposable
{
#if BAP_LAUNCHER_RECOVERY_FIXTURE
    internal static Action? BeforeHelperStart = null;
    internal async Task PublicIdentityProbe(CancellationToken token)
    {
        var feed = ReleaseFeedPolicy.Stable;
        Identity(await Read(new Uri("https://api.github.com/repos/" + feed.Repository), feed, 32768, false, token).ConfigureAwait(false), feed);
    }
#endif
    private readonly string _root;
    private readonly UpdateTrust _trust;
    private readonly HttpClient _http;
    private readonly Func<string?> _credential;
    private DateTimeOffset _retryAt, _metadataRetryAt;
    private string? _token;
    private string? _selectedChannel;
    private UpdateCancelSignal? _signal;
    private Process? _helper;
    private bool _selectionAvailable, _checking, _queueing, _queued;
    internal LauncherRecoveryInstallation? Installation { get; private set; }
    internal LauncherRecoveryRelease? Release { get; private set; }
    internal LauncherRecoveryPrepared? Prepared { get; private set; }
    internal string SelectedChannel => _selectedChannel ?? "Stable";
    internal DateTimeOffset RetryAt => _retryAt > _metadataRetryAt ? _retryAt : _metadataRetryAt;
    internal bool SelectChannel(string channel)
    {
        if (channel != "Stable" && channel != "PreRelease") throw new ArgumentException("Public update channel required", nameof(channel));
        if (_checking || _queueing || _queued) return false;
        if (_selectedChannel == channel) return true;
        _selectedChannel = channel; Installation = null; Release = null; Prepared = null; _selectionAvailable = false;
        _metadataRetryAt = DateTimeOffset.MinValue;
        return true;
    }
    private LauncherRecoveryStatus Result(LauncherRecoveryStatus status)
    {
        var diagnostic = status == LauncherRecoveryStatus.NoPatch ? DiagnosticUpdateState.NoCompatibleUpdate
            : status == LauncherRecoveryStatus.Incompatible ? DiagnosticUpdateState.CompatibilityRejected : SafeEnum.Parse<DiagnosticUpdateState>(status.ToString());
        DiagnosticHub.Status(DiagnosticFeature.Coordinator, diagnostic, Release?.Version);
        return status;
    }
    internal LauncherRecoveryClient(string root, HttpMessageHandler? fixtureHandler = null, UpdateTrust? fixtureTrust = null, Func<string?>? fixtureCredential = null)
    { _root = WindowsPath.Absolute(root); _trust = fixtureTrust ?? UpdateTrust.Production(); _http = ReleaseHttpClient.Create(fixtureHandler);
        _credential = fixtureCredential ?? (() => new WindowsReleaseCredentialStore().Load()); }
    internal static LauncherRecoveryInstallation Inspect(string root,bool helperRecovery=false,bool validateUpdateTargets=true)
    {
        using var fence = new WindowsFileFence(root);
        if(helperRecovery) UpdateLaunchGate.CheckHelperRecovery(fence); else UpdateLaunchGate.Check(fence);
        var fault=UpdateFaults.Read(fence);
        if (UpdateRecovery.PendingTicket(fence) != null) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
        using var file = fence.OpenFile(InstallReceipt.Path);
        var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384));
        foreach (var path in validateUpdateTargets ? InstallReceipt.Owned : new[] { UpdateTrust.PluginPath, "BetterAstralParty-Launcher.exe" }) {
            using var owned = path == "BetterAstralParty-Launcher.exe" ? fence.OpenLaunchImage(path) : fence.OpenFile(path);
            if (owned.Hash() != receipt.Files[path]) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
        }
        using var plugin = fence.OpenFile(UpdateTrust.PluginPath);
        var metadata = InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes));
        var state = receipt.Channels;
        if (metadata.UpdateProtocol != 4 || metadata.SettingsSchema != 1 || !ReleaseFeedPolicy.SupportsAutomaticSource(metadata.Version)
            || state == null || state.Root != fence.RootIdentity.Text || state.ActiveVersion != metadata.Version)
            throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
        using var history = UpdateTransaction.ValidateHistory(fence, receipt);
        var value = new LauncherRecoveryInstallation { Version = metadata.Version, ActiveChannel = state.ActiveChannel, ReceiptHash = file.Hash(), DescriptorHash = state.DescriptorFor(state.ActiveChannel), FaultHash = fault==null ? "-" : UpdateTrust.Hash(fault.Bytes()), Korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" };
        try {
            using var config = fence.OpenFile(ChannelTransitionIntent.SettingsPath);
            if (config.Stream.Length > 1024 * 1024) throw new InvalidDataException("Settings bounds");
            value.SettingsHash = config.Hash();
            value.SettingsReadable = true;
            var settings = Ini(UpdateTicket.Read(config, 1024 * 1024));
            string? setting; if (settings.TryGetValue("일반/업데이트 채널", out setting)) { if (!ReleaseFeedPolicy.IsChannel(setting)) throw new InvalidDataException("Invalid channel preference"); value.Channel = setting; }
            if (settings.TryGetValue("일반/언어", out setting)) { if (setting == "한국어") value.Korean = true; else if (setting == "English") value.Korean = false; }
            if (settings.TryGetValue("업데이트/자동 다운로드", out setting)) value.AutoDownload = Preference(setting);
            if (settings.TryGetValue("업데이트/종료 후 적용", out setting)) value.ApplyAfterExit = Preference(setting);
        } catch (Exception error) when (error is InvalidDataException || error is DecoderFallbackException || error is IOException
            || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ApplySafetyException) {
            var missing = error is ApplySafetyException absent && (absent.NativeError == 2 || absent.NativeError == 3);
            value.SettingsReadable = value.SettingsHash != "-" || missing;
            value.Channel = state.ActiveChannel == "PreRelease" ? "PreRelease" : "Stable";
            value.Korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
            value.AutoDownload = value.ApplyAfterExit = false;
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.Configuration, DiagnosticOutcome.Partial);
            DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Configuration,
                missing ? DiagnosticCode.NotConfigured : value.SettingsReadable ? DiagnosticCode.StateMalformed : DiagnosticCode.StateUnreadable, error,
                nativeCode: error is ApplySafetyException safety ? safety.NativeError : 0);
        }
        fence.Recheck();
        return value;
    }
    internal static Dictionary<string, string> Ini(byte[] bytes)
    {
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Settings bounds");
        var result = new Dictionary<string, string>(StringComparer.Ordinal); var section = "";
        foreach (var raw in new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff').Split('\n')) {
            var line = raw.Trim(); if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal)) { section = line.Substring(1, line.Length - 2); continue; }
            var equals = line.IndexOf('='); if (equals < 1) continue;
            var key = section + "/" + line.Substring(0, equals).Trim();
            if (key != "일반/업데이트 채널" && key != "일반/언어" && key != "업데이트/자동 다운로드" && key != "업데이트/종료 후 적용") continue;
            if (result.ContainsKey(key)) throw new InvalidDataException("Ambiguous update settings");
            result.Add(key, line.Substring(equals + 1).Trim());
        }
        return result;
    }
    private static bool Preference(string value) => bool.TryParse(value, out var result) ? result : throw new InvalidDataException("Invalid update preference");
    private static void Identity(byte[] bytes, ReleaseFeed feed)
    {
        var value = LauncherRecoveryJson.Object(LauncherRecoveryJson.Read(bytes, 32768));
        if (LauncherRecoveryJson.Id(value, "id") != feed.RepositoryId || !string.Equals(LauncherRecoveryJson.String(value, "full_name", 256), feed.Repository, StringComparison.OrdinalIgnoreCase)
            || LauncherRecoveryJson.Bool(value, "private") != feed.RequiresAuthentication) throw new InvalidDataException("Repository identity mismatch");
    }
    internal static LauncherRecoveryRelease? Latest(byte[] bytes, ReleaseFeed feed, LauncherRecoveryRelease? selected = null, string? installedPublicVersion = null)
    {
        var seen = new HashSet<long>();
        foreach (var raw in LauncherRecoveryJson.Array(LauncherRecoveryJson.Read(bytes, 1024 * 1024))) {
            var item = LauncherRecoveryJson.Object(raw); var id = LauncherRecoveryJson.Id(item, "id"); if (!seen.Add(id)) throw new InvalidDataException("Duplicate release identifier");
            if (LauncherRecoveryJson.Bool(item, "draft")) continue;
            var prerelease = LauncherRecoveryJson.Bool(item, "prerelease");
            var tag = LauncherRecoveryJson.String(item, "tag_name", 128);
            if (!ReleaseFeedPolicy.SupportsAutomaticTarget(tag, feed.Channel)) continue;
            var version = UpdateVersion.Parse(tag);
            var installedPublic = !feed.RequiresAuthentication && installedPublicVersion == version.Tag;
            if (installedPublicVersion != null && !installedPublic || !installedPublic && (!feed.Accepts(prerelease) || feed.Channel == "Stable" && version.IsPrerelease)) continue;
            if (selected != null && UpdateVersion.Parse(selected.Version).CompareTo(version) >= 0) continue;
            var release = new LauncherRecoveryRelease { Id = id, Version = version.Tag };
            var notes = LauncherRecoveryJson.Get(item, "body"); if (notes is string text) release.Notes = text.Substring(0, Math.Min(4096, text.Length));
            foreach (var assetRaw in LauncherRecoveryJson.Array(LauncherRecoveryJson.Get(item, "assets"))) {
                var asset = LauncherRecoveryJson.Object(assetRaw); var name = LauncherRecoveryJson.String(asset, "name", 256); var assetId = LauncherRecoveryJson.Id(asset, "id");
                if (release.Assets.ContainsKey(name) || release.Assets.ContainsValue(assetId)) throw new InvalidDataException("Ambiguous release assets"); release.Assets.Add(name, assetId);
            }
            selected = release;
        }
        return selected;
    }
    private static long AssetId(LauncherRecoveryRelease release, string name) => release.Assets.TryGetValue(name, out var id) ? id : throw new LauncherRecoveryFailure(LauncherRecoveryStatus.Incomplete);
    private static bool Cdn(Uri uri, ReleaseFeed feed)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != "release-assets.githubusercontent.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.OriginalString.Length > 8192) return false;
        var prefix = "/github-production-release-asset/" + feed.RepositoryId.ToString(CultureInfo.InvariantCulture) + "/";
        return uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(uri.AbsolutePath.Substring(prefix.Length), "D", out _)
            && uri.OriginalString == "https://release-assets.githubusercontent.com" + uri.AbsolutePath + uri.Query;
    }
    private void Limited(HttpResponseMessage response)
    {
        if ((int)response.StatusCode != 429 && !((int)response.StatusCode == 403 && (response.Headers.RetryAfter != null
            || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0")))) return;
        var now = DateTimeOffset.UtcNow; var due = now.AddMinutes(1); var retry = response.Headers.RetryAfter;
        if (retry?.Delta is TimeSpan delta && delta > TimeSpan.Zero) due = now.Add(delta > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delta);
        else if (retry?.Date is DateTimeOffset date && date > now) due = date;
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var rows) && long.TryParse(rows.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) {
            try { var date = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds); if (date > due) due = date; } catch (ArgumentOutOfRangeException) { }
        }
        _retryAt = due > now.AddDays(1) ? now.AddDays(1) : due;
        throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RateLimited);
    }
    private async Task<byte[]> Read(Uri endpoint, ReleaseFeed feed, int limit, bool binary, CancellationToken token, Action<HttpResponseMessage>? headers = null)
    {
        var destination = endpoint;
        for (var hop = 0; hop < 2; hop++) {
            using var request = new HttpRequestMessage(HttpMethod.Get, destination);
            request.Headers.UserAgent.ParseAdd("BetterAstralParty-Recovery/1.3");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/octet-stream" : "application/vnd.github+json"));
            if (!binary) request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            if (hop == 0 && feed.RequiresAuthentication) {
                if (!ReleaseCredentialPolicy.ValidToken(_token)) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.AuthenticationRequired);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token); request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            }
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri?.AbsoluteUri != destination.AbsoluteUri) throw new InvalidDataException("Unexpected transport redirect");
            Limited(response);
            if (response.StatusCode == HttpStatusCode.Found) {
                var next = response.Headers.Location;
                if (!binary || hop != 0 || next == null || !Cdn(next, feed)) throw new InvalidDataException("Unexpected asset redirect"); destination = next; continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) throw new LauncherRecoveryFailure((int)response.StatusCode == 401 ? LauncherRecoveryStatus.AuthenticationRequired
                : (int)response.StatusCode == 403 || (int)response.StatusCode == 404 ? LauncherRecoveryStatus.AccessUnavailable : LauncherRecoveryStatus.Failed);
            if (response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength > limit || response.Content.Headers.ContentLength < 0) throw new InvalidDataException("Response bounds");
            headers?.Invoke(response);
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false); using var output = new MemoryStream(); var buffer = new byte[32768];
            while (true) {
                idle.CancelAfter(TimeSpan.FromSeconds(15)); var count = await input.ReadAsync(buffer, 0, buffer.Length, idle.Token).ConfigureAwait(false); if (count == 0) break;
                if (output.Length + count > limit) throw new InvalidDataException("Response bounds"); output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        throw new InvalidDataException("Asset redirect bounds");
    }
    private Task<byte[]> Asset(LauncherRecoveryRelease release, string name, ReleaseFeed feed, int limit, CancellationToken token) =>
        Read(new Uri("https://api.github.com/repos/" + feed.Repository + "/releases/assets/" + AssetId(release, name).ToString(CultureInfo.InvariantCulture)), feed, limit, true, token);
    private async Task CurrentPublicRelease(LauncherRecoveryRelease release, UpdateContext context, VerifiedUpdateDescriptor descriptor, CancellationToken token)
    {
        if (!PublicReleaseEligibility.Applies(context)) return;
        try {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var feed = ReleaseFeedPolicy.For(context.Channel);
            PublicReleaseEligibility.Repository(await Read(PublicReleaseEligibility.RepositoryEndpoint(context), feed,
                PublicReleaseEligibility.MaxRepositoryBytes, false, deadline.Token).ConfigureAwait(false), context);
            var metadata = await Read(PublicReleaseEligibility.ReleaseEndpoint(context), feed, PublicReleaseEligibility.MaxReleaseBytes, false, deadline.Token).ConfigureAwait(false);
            if (!PublicReleaseEligibility.Release(metadata, context, descriptor, AssetId(release, ReleaseFeedPolicy.UpdateManifestAsset), AssetId(release, ReleaseFeedPolicy.UpdateSignatureAsset)))
                throw new LauncherRecoveryFailure(LauncherRecoveryStatus.Cancelled);
        } catch (Exception error) {
            Prepared = null;
            if (token.IsCancellationRequested) DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.ReleaseMetadata, DiagnosticOutcome.Cancelled, targetVersion: context.ReleaseTag);
            else DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.ReleaseMetadata,
                error is InvalidDataException or UpdateValidationException ? DiagnosticCode.MetadataInvalid
                    : error is LauncherRecoveryFailure rejected ? rejected.Status == LauncherRecoveryStatus.Cancelled ? DiagnosticCode.WrongTarget : DiagnosticHub.CodeForStatus(rejected.Status.ToString()) : DiagnosticCode.Transport,
                error, targetVersion: context.ReleaseTag);
            throw new LauncherRecoveryFailure(LauncherRecoveryStatus.Cancelled);
        }
    }
    private void RecoverPendingHelper() {
        using var fence=new WindowsFileFence(_root);UpdateLaunchGate.CheckHelperRecovery(fence);
        if(AutomaticHelperUpgrade.Pending(fence)) using(var maintenance=new UpdateLaunchGate.Lease(_root,f=>AutomaticHelperUpgrade.ResumePending(f,_trust))) { }
    }
    internal async Task CompleteInstalledHelper(CancellationToken cancellation) {
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(cancellation);operation.CancelAfter(TimeSpan.FromMinutes(3));
        RecoverPendingHelper();
        var installed=Inspect(_root,validateUpdateTargets:false);using var fence=new WindowsFileFence(_root);
        var current=HelperDelivery.AuthenticatedAt(fence,_trust,HelperDescriptor.Image,HelperDelivery.Receipt)??throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
        if(UpdateVersion.Parse(current.Version).CompareTo(UpdateVersion.Parse(installed.Version))>=0 || !installed.AutoDownload || !installed.ApplyAfterExit || installed.FaultHash!="-")return;
        using var maintenance=new UpdateLaunchGate.Lease(_root,f=>AutomaticHelperUpgrade.ResumePending(f,_trust));
        var feed=ReleaseFeedPolicy.For(installed.ActiveChannel);
        try {
            _token=feed.RequiresAuthentication?_credential():null;
            if(feed.RequiresAuthentication && !ReleaseCredentialPolicy.ValidToken(_token))throw new LauncherRecoveryFailure(LauncherRecoveryStatus.AuthenticationRequired);
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator,DiagnosticPhase.HelperInstall,DiagnosticOutcome.Begin,targetVersion:installed.Version);
            Identity(await Read(new Uri("https://api.github.com/repos/"+feed.Repository),feed,32768,false,operation.Token).ConfigureAwait(false),feed);
            var body=await Read(new Uri("https://api.github.com/repos/"+feed.Repository+"/releases/tags/"+Uri.EscapeDataString(installed.Version)),feed,1024*1024-2,false,operation.Token).ConfigureAwait(false);
            var wrapped=new byte[body.Length+2];wrapped[0]=(byte)'[';Buffer.BlockCopy(body,0,wrapped,1,body.Length);wrapped[wrapped.Length-1]=(byte)']';
            // Finish the exact installed proof, even if its public tag was reclassified since commit.
            var release=Latest(wrapped,feed,installedPublicVersion:feed.RequiresAuthentication?null:installed.Version)??throw new LauncherRecoveryFailure(LauncherRecoveryStatus.Incomplete);
            if(release.Version!=installed.Version)throw new LauncherRecoveryFailure(LauncherRecoveryStatus.VerificationFailed);
            var descriptor=await Asset(release,ReleaseFeedPolicy.UpdateManifestAsset,feed,UpdateTrust.MaxDescriptorBytes,operation.Token).ConfigureAwait(false);
            var key=DetachedSignature.Parse(await Asset(release,ReleaseFeedPolicy.UpdateSignatureAsset,feed,1024,operation.Token).ConfigureAwait(false));
            var verified=_trust.VerifyProposal(descriptor,key.KeyId,key.Signature,Context(installed,release,feed),operation.Token);
            if(verified.DescriptorSha256!=installed.DescriptorHash)throw new LauncherRecoveryFailure(LauncherRecoveryStatus.VerificationFailed);
            var helperDescriptor=await Asset(release,"helper.descriptor",feed,1024,operation.Token).ConfigureAwait(false);
            var helperSignature=await Asset(release,"helper.signature",feed,1024,operation.Token).ConfigureAwait(false);
            var helperImage=await Asset(release,HelperDescriptor.Image,feed,checked((int)UpdateTrust.MaxFileBytes),operation.Token).ConfigureAwait(false);
            var helper=HelperDescriptor.Verify(_trust,helperDescriptor,DetachedSignature.Parse(helperSignature),helperImage);
            if(helper.Protocol!=4 || helper.Version!=installed.Version)throw new LauncherRecoveryFailure(LauncherRecoveryStatus.VerificationFailed);
            var recheck=Inspect(_root,validateUpdateTargets:false);if(recheck.ReceiptHash!=installed.ReceiptHash || recheck.SettingsHash!=installed.SettingsHash)throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
            maintenance.Recheck(fence);AutomaticHelperUpgrade.Ensure(fence,helperDescriptor,helperSignature,helperImage,_trust);maintenance.Recheck(fence);
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator,DiagnosticPhase.HelperInstall,DiagnosticOutcome.Completed,targetVersion:installed.Version);
        } catch(Exception error) {
            DiagnosticHub.Failure(DiagnosticFeature.Coordinator,DiagnosticPhase.HelperInstall,DiagnosticCode.Unknown,error,
                nativeCode:error is ApplySafetyException safety?safety.NativeError:0,validationCode:error is UpdateValidationException validation?(int)validation.Failure:0,targetVersion:installed.Version);
            throw;
        } finally { _token=null; }
    }
    internal async Task<LauncherRecoveryStatus> Check(bool download, CancellationToken cancellation)
    {
        if (_queued) return Result(LauncherRecoveryStatus.Queued);
        if (_checking || _queueing) return Result(LauncherRecoveryStatus.FilesBusy);
        var selectedDownload = download && _selectionAvailable;
        var previous = Installation;
        Prepared = null;
        if (DateTimeOffset.UtcNow < _retryAt || !selectedDownload && DateTimeOffset.UtcNow < _metadataRetryAt) return Result(LauncherRecoveryStatus.RateLimited);
        if (!selectedDownload) { Release = null; _metadataRetryAt = DateTimeOffset.UtcNow.AddSeconds(60); }
        _selectionAvailable = false;
        _checking = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation); operation.CancelAfter(TimeSpan.FromMinutes(3));
        var phase = DiagnosticPhase.UpdateReadiness;
        try {
            RecoverPendingHelper();
            Installation = Inspect(_root,validateUpdateTargets:false);
            _selectedChannel ??= Installation.Channel == "PreRelease" ? "PreRelease" : "Stable";
            Installation.Channel = _selectedChannel;
            var feed = ReleaseFeedPolicy.For(Installation.Channel);
            if (selectedDownload && (previous == null || previous.ReceiptHash != Installation.ReceiptHash || previous.SettingsHash != Installation.SettingsHash || previous.FaultHash != Installation.FaultHash))
                throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
            if (!feed.Configured || !_trust.Configured) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.VerificationFailed);
            _token = null;
            phase = DiagnosticPhase.ReleaseMetadata; DiagnosticHub.Stage(DiagnosticFeature.ReleaseCheck, phase, DiagnosticOutcome.Begin);
            Identity(await Read(new Uri("https://api.github.com/repos/" + feed.Repository), feed, 32768, false, operation.Token).ConfigureAwait(false), feed);
            if (!selectedDownload) {
            var endpoint = "https://api.github.com/repos/" + feed.Repository + "/releases"; Uri? next = new Uri(endpoint + "?per_page=100");
            for (var page = 0; next != null; page++) {
                if (page == 4) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.Incomplete);
                Uri? following = null;
                var bytes = await Read(next, feed, 1024 * 1024, false, operation.Token, response => {
                    if (!response.Headers.TryGetValues("Link", out var links)) return;
                    var matches = Regex.Matches(string.Join(",", links), "<([^<>]+)>\\s*;\\s*rel=\"next\""); if (matches.Count == 0) return;
                    if (matches.Count != 1 || !Uri.TryCreate(matches[0].Groups[1].Value, UriKind.Absolute, out following) || following.OriginalString != endpoint + following.Query
                        || !Regex.IsMatch(following.Query, @"\A\?(?:per_page=100&page=[1-9][0-9]{0,5}|page=[1-9][0-9]{0,5}&per_page=100)\z")) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.Incomplete);
                }).ConfigureAwait(false);
                Release = Latest(bytes, feed, Release); next = following;
            }
            }
            DiagnosticHub.Stage(DiagnosticFeature.ReleaseCheck, phase, DiagnosticOutcome.Completed);
            if (Release == null || Installation.ActiveChannel == Installation.Channel && UpdateVersion.Parse(Release.Version).CompareTo(UpdateVersion.Parse(Installation.Version)) <= 0)
                return Result(LauncherRecoveryStatus.NoPatch);
            AssetId(Release, ReleaseFeedPolicy.UpdateManifestAsset); AssetId(Release, ReleaseFeedPolicy.UpdateSignatureAsset); AssetId(Release, "BetterAstralParty-" + Release.Version + "-update.zip");
            if (!download) { _selectionAvailable = true; return Result(LauncherRecoveryStatus.Available); }
            phase = DiagnosticPhase.DescriptorAsset;
            var descriptor = await Asset(Release, ReleaseFeedPolicy.UpdateManifestAsset, feed, UpdateTrust.MaxDescriptorBytes, operation.Token).ConfigureAwait(false);
            phase = DiagnosticPhase.SignatureAsset;
            var signature = DetachedSignature.Parse(await Asset(Release, ReleaseFeedPolicy.UpdateSignatureAsset, feed, 1024, operation.Token).ConfigureAwait(false));
            var context = Context(Installation, Release, feed);
            phase = DiagnosticPhase.DescriptorVerification;
            var verified = Installation.ActiveChannel == Installation.Channel ? _trust.VerifyDescriptor(descriptor, signature.KeyId, signature.Signature, context, operation.Token)
                : _trust.VerifyProposal(descriptor, signature.KeyId, signature.Signature, context, operation.Token);
            phase = DiagnosticPhase.PackageAsset;
            var zip = await Asset(Release, "BetterAstralParty-" + Release.Version + "-update.zip", feed, checked((int)UpdateTrust.MaxZipBytes), operation.Token).ConfigureAwait(false);
            phase = DiagnosticPhase.PayloadVerification;
            var payload = UpdatePackage.VerifyPayload(zip, verified, operation.Token);
            byte[] manifest; using (var archive = new ZipArchive(payload.OpenRead(), ZipArchiveMode.Read)) {
                var entry = archive.GetEntry("BetterAstralParty.compatibility.json") ?? throw new InvalidDataException("Missing compatibility baseline");
                manifest = Array.Empty<byte>();
                if (entry.Length <= 65536) { using var data = entry.Open(); using var output = new MemoryStream(); data.CopyTo(output); manifest = output.ToArray(); }
            }
            using (var fence = new WindowsFileFence(_root)) using (var game = fence.OpenFile("AstralParty_INT.exe")) using (UpdateCompatibility.Require(fence, manifest, game)) { }
            phase = DiagnosticPhase.HelperInstall;
            var helperDescriptor = await Asset(Release, "helper.descriptor", feed, 1024, operation.Token).ConfigureAwait(false);
            var helperSignature = await Asset(Release, "helper.signature", feed, 1024, operation.Token).ConfigureAwait(false);
            var helperImage = await Asset(Release, HelperDescriptor.Image, feed, checked((int)UpdateTrust.MaxFileBytes), operation.Token).ConfigureAwait(false);
            var helper = HelperDescriptor.Verify(_trust, helperDescriptor, DetachedSignature.Parse(helperSignature), helperImage);
            if (helper.Protocol != 4 || helper.Version != Release.Version || UpdateVersion.Parse(helper.Version).CompareTo(UpdateVersion.Parse("1.3.0-0")) < 0) throw new UpdateValidationException(UpdateFailure.WrongTarget);
            ChannelTransitionReview? review = null;
            if (Installation.ActiveChannel != Installation.Channel) { using var fence = new WindowsFileFence(_root); review = ChannelTransitionIntent.Review(fence, verified); }
            phase = DiagnosticPhase.ReleaseMetadata;
            await CurrentPublicRelease(Release, context, verified, operation.Token).ConfigureAwait(false);
            Prepared = new LauncherRecoveryPrepared(Release, Installation, descriptor, signature, zip, manifest, helperDescriptor, helperSignature, helperImage, verified, review);
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.UpdateReadiness, DiagnosticOutcome.Completed, targetVersion: Release.Version);
            return Result(LauncherRecoveryStatus.Ready);
        } catch (Exception error) {
            var status = error is LauncherRecoveryFailure known ? known.Status : error is OperationCanceledException ? cancellation.IsCancellationRequested ? LauncherRecoveryStatus.Cancelled : LauncherRecoveryStatus.Failed
                : error is UpdateValidationException ? LauncherRecoveryStatus.VerificationFailed
                : error is ApplySafetyException apply ? apply.Failure == ApplyFailure.CompatibilityRequired ? LauncherRecoveryStatus.Incompatible : apply.Failure == ApplyFailure.FileBusy || apply.Failure == ApplyFailure.ProcessBusy ? LauncherRecoveryStatus.FilesBusy : LauncherRecoveryStatus.RecoveryRequired
                : LauncherRecoveryStatus.Failed;
            DiagnosticHub.Failure(DiagnosticFeature.Coordinator, phase, DiagnosticHub.CodeForStatus(status.ToString()), error,
                nativeCode: error is ApplySafetyException safety ? safety.NativeError : 0, validationCode: error is UpdateValidationException validation ? (int)validation.Failure : 0,
                applyCode: error is ApplySafetyException fault ? (int)fault.Failure : 0, targetVersion: Release?.Version);
            return Result(status);
        } finally { _token = null; _checking = false; }
    }
    private static UpdateContext Context(LauncherRecoveryInstallation installed, LauncherRecoveryRelease release, ReleaseFeed feed, ChannelTransitionIntent? transition = null) =>
        new UpdateContext(feed.Repository, feed.Channel, installed.Version, release.Version, release.Id, AssetId(release, "BetterAstralParty-" + release.Version + "-update.zip"), UpdateTrust.PlatformId, repositoryId: feed.RepositoryId, transition: transition);
    internal async Task Queue(bool approveTransition, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_checking || _queueing || _queued) throw new LauncherRecoveryFailure(_queued ? LauncherRecoveryStatus.Queued : LauncherRecoveryStatus.FilesBusy);
        var prepared = Prepared ?? throw new InvalidOperationException("No verified update");
        if (prepared.Installation.Channel != SelectedChannel || prepared.Verified.Channel != SelectedChannel) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
        _queueing = true;
        try {
        await CurrentPublicRelease(prepared.Release, Context(prepared.Installation, prepared.Release, ReleaseFeedPolicy.For(SelectedChannel)), prepared.Verified, token).ConfigureAwait(false);
        using var maintenance = new UpdateLaunchGate.Lease(_root,f=>AutomaticHelperUpgrade.ResumePending(f,_trust));
        var current = Inspect(_root);
        if (!current.SettingsReadable || !prepared.Installation.SettingsReadable) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.FilesBusy);
        if (current.ReceiptHash != prepared.Installation.ReceiptHash || current.SettingsHash != prepared.Installation.SettingsHash || current.FaultHash != prepared.Installation.FaultHash)
            throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
        current.Channel = SelectedChannel;
        var intent = prepared.Transition == null ? null : approveTransition ? ChannelTransitionIntent.Approve(prepared.Transition) : throw new OperationCanceledException();
        var context = Context(current, prepared.Release, ReleaseFeedPolicy.For(current.Channel), intent);
        using var fence = new WindowsFileFence(_root);
        if (intent != null) using (var receipt = fence.OpenFile(InstallReceipt.Path))
            intent.Require(fence, InstallReceipt.Parse(UpdateTicket.Read(receipt, 16384)), receipt.Hash(), ChannelTransitionIntent.WaterHashAt(fence), current.Version, prepared.Verified);
        using var processes = WindowsProcessGuard.Acquire(fence, new[] { "AstralParty_INT.exe" });
        var game = processes.BorrowImage(fence.Full("AstralParty_INT.exe"));
        using var compatible = UpdateCompatibility.Require(fence, prepared.Manifest, game);
        if(current.FaultHash!="-") {
            using(var helperBarrier=WindowsProcessGuard.Acquire(fence,new[]{HelperDescriptor.Image})) {
                processes.Recheck();helperBarrier.Recheck();UpdateFaults.Acknowledge(fence);helperBarrier.Recheck();processes.Recheck();
            }
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator,DiagnosticPhase.Recovery,DiagnosticOutcome.Completed,targetVersion:prepared.Release.Version);
        }
        using var process = Process.GetCurrentProcess();
        using var launcher = WindowsProcessGuard.Capture((uint)process.Id, fence.Full("BetterAstralParty-Launcher.exe"), process.StartTime.ToFileTimeUtc(), WindowsProcessGuard.CurrentSession);
        token.ThrowIfCancellationRequested();
        AutomaticHelperUpgrade.Ensure(fence, prepared.HelperDescriptor, prepared.HelperSignature, prepared.HelperImage, _trust, processGuard: processes);
        var helper = HelperDelivery.AuthenticatedAt(fence, _trust, HelperDescriptor.Image, HelperDelivery.Receipt) ?? throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
        if (helper.Protocol != 4 || UpdateVersion.Parse(helper.Version).CompareTo(UpdateVersion.Parse("1.3.0-0")) < 0) throw new UpdateValidationException(UpdateFailure.WrongTarget);
        maintenance.Recheck(fence); processes.Recheck(); compatible.Recheck(); token.ThrowIfCancellationRequested();
        var staged = UpdateStaging.Create(fence, prepared.Descriptor, prepared.Key, prepared.Signature, prepared.Zip, _trust, context, token);
        var ticket = new UpdateTicket { RootId = fence.RootIdentity.Text, Stage = staged.Directory, StageId = staged.DirectoryIdentity.Text, DescriptorHash = staged.DescriptorSha256,
            Context = context, LauncherPid = launcher.Pid, LauncherCreation = launcher.Creation, LauncherSession = launcher.Session };
        UpdateTicket.Parse(ticket.Bytes());
        UpdateTicket.Write(fence, ticket.Stage + "/handoff.ticket", ticket.Bytes());
        if (intent != null) UpdateTicket.Write(fence, UpdateWorkPath.Transition(intent.Operation), Encoding.ASCII.GetBytes(intent.Identity + "\nstage=" + ticket.Stage + "\n"));
        _signal?.Dispose(); _helper?.Dispose(); _helper = null;
        _signal = UpdateCancelSignal.Create(UpdateTrust.Hash(ticket.Bytes()));
        var guard = UpdateFaults.Begin(fence, context, FaultPhase.Apply, ticket);
        try {
            using var image = fence.OpenLaunchImage(HelperDescriptor.Image); if (image.Hash() != helper.Sha256) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            var start = new ProcessStartInfo { FileName = WindowsPath.ForProcess(fence.Full(HelperDescriptor.Image)), WorkingDirectory = WindowsPath.ForProcess(fence.Root), UseShellExecute = false, CreateNoWindow = true,
                Arguments = "--apply \"" + fence.Root + "\" \"" + ticket.Stage + "\"" };
            using var cancel = token.Register(() => _signal?.Set());
#if BAP_LAUNCHER_RECOVERY_FIXTURE
            BeforeHelperStart?.Invoke();
#endif
            token.ThrowIfCancellationRequested(); _helper = Process.Start(start) ?? throw new InvalidOperationException("Helper did not start");
            var due = DateTime.UtcNow.AddSeconds(15);
            while (true) {
                token.ThrowIfCancellationRequested();
                try { using var ready = fence.OpenFile(ticket.Stage + "/helper.ready"); if (Encoding.ASCII.GetString(UpdateTicket.Read(ready, 128)) != UpdateTrust.Hash(ticket.Bytes()) + "\n") throw new InvalidDataException("Helper handshake mismatch"); break; }
                catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 32) { }
                if (_helper.HasExited || DateTime.UtcNow > due) throw new LauncherRecoveryFailure(LauncherRecoveryStatus.RecoveryRequired);
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            DiagnosticHub.Stage(DiagnosticFeature.Coordinator, DiagnosticPhase.WaitForExit, DiagnosticOutcome.Active,
                DiagnosticOperation.VerifiedGuid(guard.Operation), prepared.Release.Version);
            _queued = true;
        } catch (Exception error) {
            DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.HelperQueue, DiagnosticCode.InterruptedUnknown, error,
                operation: DiagnosticOperation.VerifiedGuid(guard.Operation), targetVersion: prepared.Release.Version);
            try {
                _signal.Set();
                if (_helper == null) {
                    UpdateTicket.Write(fence, ticket.Stage + "/helper.cancel", Encoding.ASCII.GetBytes(UpdateTrust.Hash(ticket.Bytes()) + "\n"));
                    UpdateFaults.Complete(fence, guard);
                    if(!UpdateRetention.CancelUnclaimed(fence,ticket,_trust,maintenance)) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                }
            } catch (Exception cleanup) {
                DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Recovery, DiagnosticCode.StorageUnavailable, cleanup,
                    operation: DiagnosticOperation.VerifiedGuid(guard.Operation), targetVersion: prepared.Release.Version);
            }
            throw;
        }
        } finally { _queueing = false; }
    }
    internal void CancelQueued() => _signal?.Set();
    public void Dispose() { _token = null; _signal?.Dispose(); _helper?.Dispose(); _http.Dispose(); }
}
