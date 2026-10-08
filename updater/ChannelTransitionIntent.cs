#nullable enable
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    internal sealed class ChannelTransitionReview
    {
        internal readonly string Root, ReceiptHash, SettingsHash, HighWaterHash, CurrentVersion, SourceChannel, SourceRepository;
        internal readonly long SourceRepositoryId;
        internal readonly VerifiedUpdateDescriptor Target;
        internal ChannelTransitionReview(string root, string receipt, string settings, string water, string current, ReceiptChannelState source, VerifiedUpdateDescriptor target)
        { Root = root; ReceiptHash = receipt; SettingsHash = settings; HighWaterHash = water; CurrentVersion = current;
            SourceChannel = source.ActiveChannel; SourceRepository = source.ActiveRepository; SourceRepositoryId = source.ActiveRepositoryId; Target = target; }
    }
    // An exact reviewed target and original root/receipt/configuration are retained in the
    // ticket. Only Beta -> Stable may lower the current version; a return to Beta
    // must be higher than the installed Stable version and retain its prior floor.
    internal sealed class ChannelTransitionIntent
    {
        internal const string SettingsPath = "BepInEx/config/kr.betterastralparty.mod.cfg";
        internal readonly string Root, Operation, ReceiptHash, SettingsHash, HighWaterHash, CurrentVersion, TargetVersion, DescriptorHash, ZipHash;
        internal readonly string SourceChannel, TargetChannel;
        internal readonly long SourceRepositoryId, TargetRepositoryId, ReleaseId, AssetId;
        private ChannelTransitionIntent(string root, string operation, string receipt, string settings, string water, string current,
            long sourceId, long targetId, string target, long release, long asset, string descriptor, string zip, string sourceChannel, string targetChannel)
        { Root = root; Operation = operation; ReceiptHash = receipt; SettingsHash = settings; HighWaterHash = water; CurrentVersion = current;
            SourceRepositoryId = sourceId; TargetRepositoryId = targetId; TargetVersion = target; ReleaseId = release; AssetId = asset; DescriptorHash = descriptor; ZipHash = zip; SourceChannel = sourceChannel; TargetChannel = targetChannel; }
        internal string Identity { get { return UpdateTrust.Hash(Bytes()); } }
        internal static ChannelTransitionReview Review(WindowsFileFence fence, VerifiedUpdateDescriptor target)
        {
            if (target.Protocol != 2 || target.Channel != "Stable" && target.Channel != "Beta" || target.Repository != ReleaseFeedPolicy.For(target.Channel).Repository || target.SettingsSchema != 1) throw Prerequisite();
            using (var file = fence.OpenFile(InstallReceipt.Path)) using (var plugin = fence.OpenFile(UpdateTrust.PluginPath)) {
                var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384)); var state = receipt.Channels;
                if (state == null || state.Root != fence.RootIdentity.Text || state.ActiveChannel == target.Channel || state.ActiveRepositoryId <= 0
                    || state.ActiveChannel == "Beta" && state.ActiveRepositoryId != ReleaseFeedPolicy.BetaRepositoryId) throw Prerequisite();
                var sourceFeed = ReleaseFeedPolicy.For(state.ActiveChannel);
                if (!sourceFeed.Configured || sourceFeed.RepositoryId != state.ActiveRepositoryId) throw Prerequisite();
                var metadata = InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes));
                if (metadata.UpdateProtocol != 2 || metadata.SettingsSchema != 1 || metadata.Version != state.ActiveVersion || plugin.Hash() != receipt.Files[UpdateTrust.PluginPath]) throw Prerequisite();
                if (target.Channel == "Beta" && UpdateVersion.Parse(target.Version).CompareTo(UpdateVersion.Parse(metadata.Version)) <= 0) throw Prerequisite();
                var floor = state.Floor(new UpdateContext(target.Repository, target.Channel, metadata.Version, target.Version, target.ReleaseId, target.AssetId, target.Platform, repositoryId: target.RepositoryId));
                if (floor != null) { var comparison = UpdateVersion.Parse(target.Version).CompareTo(UpdateVersion.Parse(floor));
                    if (comparison < 0 || comparison == 0 && (target.Channel == "Stable" ? state.StableDescriptor : state.BetaDescriptor) != target.DescriptorSha256) throw new UpdateValidationException(UpdateFailure.NotNewer); }
                string? water; UpdateTransaction.ReadHighWater(fence, out water);
                return new ChannelTransitionReview(fence.RootIdentity.Text, file.Hash(), SettingsHashAt(fence), water ?? "-", metadata.Version, state, target);
            }
        }
        // Integration calls this only from the explicit settings confirmation event after
        // presenting both versions, feeds and hashes. Popup close/settings is not approval.
        internal static ChannelTransitionIntent Approve(ChannelTransitionReview review)
        {
            if (review.SourceChannel == review.Target.Channel || review.SourceRepository != ReleaseFeedPolicy.For(review.SourceChannel).Repository
                || review.Target.Channel == "Beta" && UpdateVersion.Parse(review.Target.Version).CompareTo(UpdateVersion.Parse(review.CurrentVersion)) <= 0) throw Prerequisite();
            return new ChannelTransitionIntent(review.Root, Guid.NewGuid().ToString("N"), review.ReceiptHash, review.SettingsHash, review.HighWaterHash, review.CurrentVersion,
                review.SourceRepositoryId, review.Target.RepositoryId, review.Target.Version, review.Target.ReleaseId, review.Target.AssetId, review.Target.DescriptorSha256, review.Target.ZipSha256, review.SourceChannel, review.Target.Channel);
        }
        internal bool MatchesTarget(UpdateContext context)
        { return SourceChannel != TargetChannel && SourceRepositoryId > 0 && (SourceChannel != "Beta" || SourceRepositoryId == ReleaseFeedPolicy.BetaRepositoryId)
            && context.Repository == ReleaseFeedPolicy.For(TargetChannel).Repository && context.Channel == TargetChannel
            && context.RepositoryId > 0 && context.RepositoryId == TargetRepositoryId && context.CurrentVersion == CurrentVersion
            && context.ReleaseTag == TargetVersion && context.ReleaseId == ReleaseId && context.AssetId == AssetId
            && (TargetChannel != "Beta" || UpdateVersion.Parse(TargetVersion).CompareTo(UpdateVersion.Parse(CurrentVersion)) > 0); }
        internal void Require(WindowsFileFence fence, InstallReceipt receipt, string receiptHash, string? waterHash, string current, VerifiedUpdateDescriptor descriptor)
        { RequireEvidence(fence, receipt, receiptHash, waterHash, current, descriptor, SettingsHashAt(fence)); }
        // Recovery never writes configuration. The authenticated journal's original
        // backup is verified first; preserve later user edits and failure-OFF saves.
        internal void RequireRecovery(WindowsFileFence fence, InstallReceipt receipt, string receiptHash, string? waterHash, string current, VerifiedUpdateDescriptor descriptor, string originalSettingsHash)
        { if (originalSettingsHash != SettingsHash) throw Prerequisite(); RequireEvidence(fence, receipt, receiptHash, waterHash, current, descriptor, originalSettingsHash); }
        private void RequireEvidence(WindowsFileFence fence, InstallReceipt receipt, string receiptHash, string? waterHash, string current, VerifiedUpdateDescriptor descriptor, string settingsEvidence)
        {
            var state = receipt.Channels;
            if (fence.RootIdentity.Text != Root || state == null || state.Root != Root || state.ActiveChannel != SourceChannel || state.ActiveRepositoryId != SourceRepositoryId
                || state.ActiveVersion != CurrentVersion || current != CurrentVersion || receiptHash != ReceiptHash || (waterHash ?? "-") != HighWaterHash
                || settingsEvidence != SettingsHash || descriptor.Protocol != 2 || descriptor.RepositoryId != TargetRepositoryId || descriptor.Channel != TargetChannel
                || descriptor.Version != TargetVersion || descriptor.ReleaseId != ReleaseId || descriptor.AssetId != AssetId || descriptor.DescriptorSha256 != DescriptorHash || descriptor.ZipSha256 != ZipHash)
                throw Prerequisite();
            var sourceFeed = ReleaseFeedPolicy.For(SourceChannel);
            if (!sourceFeed.Configured || sourceFeed.RepositoryId != SourceRepositoryId) throw Prerequisite();
        }
        internal static string SettingsHashAt(WindowsFileFence fence)
        {
            try { using (var file = fence.OpenFile(SettingsPath)) { if (file.Stream.Length > 1024 * 1024) throw Prerequisite(); return file.Hash(); } }
            catch (ApplySafetyException error) when (error.NativeError == 2) { return "-"; }
        }
        internal static string? WaterHashAt(WindowsFileFence fence)
        {
            try { using (var file = fence.OpenFile(UpdateTransaction.HighWaterPath)) {
                var f = Encoding.ASCII.GetString(UpdateTicket.Read(file, 2048)).Split('\n');
                if (f.Length != 5 || f[0] != "BetterAstralParty.HighWater/v1" || f[1] != "root=" + fence.RootIdentity.Text || !f[2].StartsWith("version=", StringComparison.Ordinal)
                    || !Regex.IsMatch(f[3], @"\Adescriptor=[0-9A-F]{64}\z") || f[4] != "") throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                UpdateVersion.Parse(f[2].Substring(8)); return file.Hash();
            } } catch (ApplySafetyException error) when (error.NativeError == 2) { return null; }
        }
        internal void RequireStage(WindowsFileFence fence, UpdateTicket ticket)
        {
            using (var file = fence.OpenFile("bap-transition-" + Operation))
                if (Encoding.ASCII.GetString(UpdateTicket.Read(file, 256)) != Identity + "\nstage=" + ticket.Stage + "\n" || ticket.RootId != Root)
                    throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
        }
        internal byte[] Bytes()
        { return Encoding.ASCII.GetBytes("BetterAstralParty.ChannelTransition/v2\nroot=" + Root + "\noperation=" + Operation + "\nreceipt=" + ReceiptHash
            + "\nsettings=" + SettingsHash + "\nwater=" + HighWaterHash + "\ncurrent=" + CurrentVersion + "\nsource-id=" + SourceRepositoryId.ToString(CultureInfo.InvariantCulture)
            + "\ntarget-id=" + TargetRepositoryId.ToString(CultureInfo.InvariantCulture) + "\ntarget=" + TargetVersion + "\nrelease=" + ReleaseId.ToString(CultureInfo.InvariantCulture)
            + "\nasset=" + AssetId.ToString(CultureInfo.InvariantCulture) + "\ndescriptor=" + DescriptorHash + "\nzip=" + ZipHash + "\nsource-channel=" + SourceChannel + "\ntarget-channel=" + TargetChannel + "\n"); }
        internal static ChannelTransitionIntent Parse(byte[] bytes)
        {
            if (bytes.Length > 2048) throw Prerequisite(); foreach (var b in bytes) if (b > 127 || b == 13) throw Prerequisite();
            var f = Encoding.ASCII.GetString(bytes).Split('\n'); var names = new[] { "root", "operation", "receipt", "settings", "water", "current", "source-id", "target-id", "target", "release", "asset", "descriptor", "zip", "source-channel", "target-channel" };
            if (f.Length != 17 || f[0] != "BetterAstralParty.ChannelTransition/v2" || f[16] != "") throw Prerequisite();
            var v = new string[15]; for (var i = 0; i < v.Length; i++) { if (!f[i+1].StartsWith(names[i] + "=", StringComparison.Ordinal)) throw Prerequisite(); v[i] = f[i+1].Substring(names[i].Length + 1); }
            if (!Regex.IsMatch(v[0], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1], @"\A[0-9a-f]{32}\z")
                || !Digest(v[2]) || v[3] != "-" && !Digest(v[3]) || v[4] != "-" && !Digest(v[4]) || !Digest(v[11]) || !Digest(v[12])) throw Prerequisite();
            var current = UpdateVersion.Parse(v[5]); var target = UpdateVersion.Parse(v[8]);
            if (v[13] != "Stable" && v[13] != "Beta" || v[14] != "Stable" && v[14] != "Beta" || v[13] == v[14]
                || v[14] == "Stable" && (target.Major == 0 || target.IsPrerelease) || v[14] == "Beta" && target.CompareTo(current) <= 0) throw Prerequisite();
            var result = new ChannelTransitionIntent(v[0], v[1], v[2], v[3], v[4], v[5], Number(v[6]), Number(v[7]), v[8], Number(v[9]), Number(v[10]), v[11], v[12], v[13], v[14]);
            if (result.SourceChannel == "Beta" && result.SourceRepositoryId != ReleaseFeedPolicy.BetaRepositoryId || result.TargetChannel == "Beta" && result.TargetRepositoryId != ReleaseFeedPolicy.BetaRepositoryId
                || Encoding.ASCII.GetString(result.Bytes()) != Encoding.ASCII.GetString(bytes)) throw Prerequisite(); return result;
        }
        private static bool Digest(string value) { return Regex.IsMatch(value, @"\A[0-9A-F]{64}\z"); }
        private static long Number(string value) { long n; if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n <= 0) throw Prerequisite(); return n; }
        private static ApplySafetyException Prerequisite() { return new ApplySafetyException(ApplyFailure.InstallationRequired); }
    }
}
