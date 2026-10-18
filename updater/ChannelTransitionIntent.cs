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
    // ticket. Every explicit cross-channel direction may lower the installed
    // version; the destination's retained floor and exact replay identity remain.
    internal sealed class ChannelTransitionIntent
    {
        internal const string SettingsPath = "BepInEx/config/kr.betterastralparty.mod.cfg";
        // v2 binds the whole settings file; v3 binds only the saved update-channel choice,
        // so unrelated settings edits after approval no longer cancel the switch.
        internal const int CurrentFormat = 3;
        internal readonly int Format;
        internal readonly string Root, Operation, ReceiptHash, SettingsHash, HighWaterHash, CurrentVersion, TargetVersion, DescriptorHash, ZipHash;
        internal readonly string SourceChannel, TargetChannel;
        internal readonly long SourceRepositoryId, TargetRepositoryId, ReleaseId, AssetId;
        private ChannelTransitionIntent(int format, string root, string operation, string receipt, string settings, string water, string current,
            long sourceId, long targetId, string target, long release, long asset, string descriptor, string zip, string sourceChannel, string targetChannel)
        { Format = format; Root = root; Operation = operation; ReceiptHash = receipt; SettingsHash = settings; HighWaterHash = water; CurrentVersion = current;
            SourceRepositoryId = sourceId; TargetRepositoryId = targetId; TargetVersion = target; ReleaseId = release; AssetId = asset; DescriptorHash = descriptor; ZipHash = zip; SourceChannel = sourceChannel; TargetChannel = targetChannel; }
        internal string Identity { get { return UpdateTrust.Hash(Bytes()); } }
        internal static ChannelTransitionReview Review(WindowsFileFence fence, VerifiedUpdateDescriptor target)
        {
            if (target.Protocol != UpdateTrust.Protocol || !ReleaseFeedPolicy.IsChannel(target.Channel) || target.Repository != ReleaseFeedPolicy.For(target.Channel).Repository || target.SettingsSchema != 1) throw Prerequisite();
            using (var file = fence.OpenFile(InstallReceipt.Path)) using (var plugin = fence.OpenFile(UpdateTrust.PluginPath)) {
                var receipt = InstallReceipt.Parse(UpdateTicket.Read(file, 16384)); var state = receipt.Channels;
                if (state == null || state.Root != fence.RootIdentity.Text || state.ActiveChannel == target.Channel || state.ActiveRepositoryId <= 0
                    || state.ActiveChannel == "Beta" && state.ActiveRepositoryId != ReleaseFeedPolicy.BetaRepositoryId) throw Prerequisite();
                var sourceFeed = ReleaseFeedPolicy.For(state.ActiveChannel);
                if (!sourceFeed.Configured || sourceFeed.RepositoryId != state.ActiveRepositoryId) throw Prerequisite();
                var metadata = InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes));
                if (metadata.UpdateProtocol != UpdateTrust.Protocol || metadata.SettingsSchema != 1 || metadata.Version != state.ActiveVersion || plugin.Hash() != receipt.Files[UpdateTrust.PluginPath]) throw Prerequisite();
                using var history=UpdateTransaction.ValidateHistory(fence,receipt);
                var floor = state.Floor(new UpdateContext(target.Repository, target.Channel, metadata.Version, target.Version, target.ReleaseId, target.AssetId, target.Platform, repositoryId: target.RepositoryId));
                if (floor != null) { var comparison = UpdateVersion.Parse(target.Version).CompareTo(UpdateVersion.Parse(floor));
                    if (comparison < 0 || comparison == 0 && state.DescriptorFor(target.Channel) != target.DescriptorSha256) throw new UpdateValidationException(UpdateFailure.NotNewer); }
                string? water; UpdateTransaction.ReadHighWater(fence, out water);
                return new ChannelTransitionReview(fence.RootIdentity.Text, file.Hash(), ChannelSettingHashAt(fence), water ?? "-", metadata.Version, state, target);
            }
        }
        // Integration calls this only from the explicit settings confirmation event after
        // presenting both versions, feeds and hashes. Popup close/settings is not approval.
        internal static ChannelTransitionIntent Approve(ChannelTransitionReview review)
        {
            if (review.SourceChannel == review.Target.Channel || review.SourceRepository != ReleaseFeedPolicy.For(review.SourceChannel).Repository) throw Prerequisite();
            return new ChannelTransitionIntent(CurrentFormat, review.Root, Guid.NewGuid().ToString("N"), review.ReceiptHash, review.SettingsHash, review.HighWaterHash, review.CurrentVersion,
                review.SourceRepositoryId, review.Target.RepositoryId, review.Target.Version, review.Target.ReleaseId, review.Target.AssetId, review.Target.DescriptorSha256, review.Target.ZipSha256, review.SourceChannel, review.Target.Channel);
        }
        internal bool MatchesTarget(UpdateContext context)
        { return SourceChannel != TargetChannel && SourceRepositoryId > 0 && (SourceChannel != "Beta" || SourceRepositoryId == ReleaseFeedPolicy.BetaRepositoryId)
            && context.Repository == ReleaseFeedPolicy.For(TargetChannel).Repository && context.Channel == TargetChannel
            && context.RepositoryId > 0 && context.RepositoryId == TargetRepositoryId && context.CurrentVersion == CurrentVersion
            && context.ReleaseTag == TargetVersion && context.ReleaseId == ReleaseId && context.AssetId == AssetId; }
        internal void Require(WindowsFileFence fence, InstallReceipt receipt, string receiptHash, string? waterHash, string current, VerifiedUpdateDescriptor descriptor)
        { RequireEvidence(fence, receipt, receiptHash, waterHash, current, descriptor, Format == 2 ? SettingsHashAt(fence) : ChannelSettingHashAt(fence)); }
        // Recovery never writes configuration. The authenticated journal's original
        // backup is verified first; preserve later user edits and failure-OFF saves.
        // originalSettings is that backup's content, or null when no settings file existed.
        internal void RequireRecovery(WindowsFileFence fence, InstallReceipt receipt, string receiptHash, string? waterHash, string current, VerifiedUpdateDescriptor descriptor, string originalSettingsHash, byte[]? originalSettings)
        {
            if (originalSettings == null ? originalSettingsHash != "-" : UpdateTrust.Hash(originalSettings) != originalSettingsHash) throw Prerequisite();
            var evidence = Format == 2 ? originalSettingsHash : originalSettings == null ? "-" : ChannelSettingHash(originalSettings);
            if (evidence != SettingsHash) throw Prerequisite(); RequireEvidence(fence, receipt, receiptHash, waterHash, current, descriptor, evidence);
        }
        private void RequireEvidence(WindowsFileFence fence, InstallReceipt receipt, string receiptHash, string? waterHash, string current, VerifiedUpdateDescriptor descriptor, string settingsEvidence)
        {
            var state = receipt.Channels;
            if (fence.RootIdentity.Text != Root || state == null || state.Root != Root || state.ActiveChannel != SourceChannel || state.ActiveRepositoryId != SourceRepositoryId
                || state.ActiveVersion != CurrentVersion || current != CurrentVersion || receiptHash != ReceiptHash || (waterHash ?? "-") != HighWaterHash
                || settingsEvidence != SettingsHash || descriptor.Protocol != UpdateTrust.Protocol || descriptor.RepositoryId != TargetRepositoryId || descriptor.Channel != TargetChannel
                || descriptor.Version != TargetVersion || descriptor.ReleaseId != ReleaseId || descriptor.AssetId != AssetId || descriptor.DescriptorSha256 != DescriptorHash || descriptor.ZipSha256 != ZipHash)
                throw Prerequisite();
            var sourceFeed = ReleaseFeedPolicy.For(SourceChannel);
            if (!sourceFeed.Configured || sourceFeed.RepositoryId != SourceRepositoryId) throw Prerequisite();
        }
        internal static string SettingsHashAt(WindowsFileFence fence)
        {
            try { using (var file = fence.OpenFile(SettingsPath)) { if (file.Stream.Length > 1024 * 1024) throw Prerequisite(); return file.Hash(); } }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { fence.Recheck(); return "-"; }
        }
        internal static string ChannelSettingHashAt(WindowsFileFence fence)
        {
            try { using (var file = fence.OpenFile(SettingsPath)) { if (file.Stream.Length > 1024 * 1024) throw Prerequisite(); return ChannelSettingHash(UpdateTicket.Read(file, 1024 * 1024)); } }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { fence.Recheck(); return "-"; }
        }
        // Digest of the saved [일반] 업데이트 채널 value (the key Plugin.cs binds); an absent key digests as empty.
        internal static string ChannelSettingHash(byte[] settings)
        {
            string text;
            // Undecodable settings keep the strict v2 behaviour: any byte change cancels the approval.
            try { text = new UTF8Encoding(false, true).GetString(settings); }
            catch (DecoderFallbackException) { return UpdateTrust.Hash(Encoding.ASCII.GetBytes("BetterAstralParty.ChannelSetting/raw\n" + UpdateTrust.Hash(settings) + "\n")); }
            string section = "", value = "";
            foreach (var raw in text.TrimStart('﻿').Split('\n')) {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                var equals = line.IndexOf('=');
                if (section == "일반" && equals > 0 && !line.StartsWith("#", StringComparison.Ordinal) && line.Substring(0, equals).Trim() == "업데이트 채널") { value = line.Substring(equals + 1).Trim(); break; }
            }
            return UpdateTrust.Hash(Encoding.UTF8.GetBytes("BetterAstralParty.ChannelSetting/v1\n" + value + "\n"));
        }
        // The emergency window records a confirmed cross-channel switch here; after it applies, the plugin
        // saves that channel through its normal config path so installed and saved channels stay equal.
        internal const string AdoptionPath = "BetterAstralParty.channel-adopt";
        private const string AdoptionHeader = "BetterAstralParty.ChannelAdopt/v1";
        internal static void WriteAdoption(WindowsFileFence fence, string channel, string version)
        {
            if (channel != "Stable" && channel != "PreRelease") throw Prerequisite(); UpdateVersion.Parse(version);
            RemoveAdoption(fence);
            UpdateTicket.Write(fence, AdoptionPath, Encoding.ASCII.GetBytes(AdoptionHeader + "\nroot=" + fence.RootIdentity.Text + "\nchannel=" + channel + "\nversion=" + version + "\n"));
        }
        // False when absent. A present but invalid marker yields empty values so the caller only removes it.
        internal static bool ReadAdoption(WindowsFileFence fence, out string channel, out string version)
        {
            channel = version = "";
            try {
                using (var file = fence.OpenFile(AdoptionPath)) {
                    var f = Encoding.ASCII.GetString(UpdateTicket.Read(file, 512)).Split('\n');
                    if (f.Length == 5 && f[0] == AdoptionHeader && f[1] == "root=" + fence.RootIdentity.Text && f[2].StartsWith("channel=", StringComparison.Ordinal)
                        && f[3].StartsWith("version=", StringComparison.Ordinal) && f[4] == "" && (f[2] == "channel=Stable" || f[2] == "channel=PreRelease")) {
                        UpdateVersion.Parse(f[3].Substring(8)); channel = f[2].Substring(8); version = f[3].Substring(8);
                    }
                }
            } catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { fence.Recheck(); return false; }
            catch (Exception error) when (error is ApplySafetyException || error is ArgumentException || error is FormatException || error is UpdateValidationException || error is System.IO.InvalidDataException) { channel = version = ""; }
            return true;
        }
        internal static void RemoveAdoption(WindowsFileFence fence)
        {
            try { using (var file = fence.OpenFile(AdoptionPath, true)) file.RemoveChannelAdoption(); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { fence.Recheck(); }
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
        internal byte[] Bytes()
        { return Encoding.ASCII.GetBytes("BetterAstralParty.ChannelTransition/v" + Format.ToString(CultureInfo.InvariantCulture) + "\nroot=" + Root + "\noperation=" + Operation + "\nreceipt=" + ReceiptHash
            + "\nsettings=" + SettingsHash + "\nwater=" + HighWaterHash + "\ncurrent=" + CurrentVersion + "\nsource-id=" + SourceRepositoryId.ToString(CultureInfo.InvariantCulture)
            + "\ntarget-id=" + TargetRepositoryId.ToString(CultureInfo.InvariantCulture) + "\ntarget=" + TargetVersion + "\nrelease=" + ReleaseId.ToString(CultureInfo.InvariantCulture)
            + "\nasset=" + AssetId.ToString(CultureInfo.InvariantCulture) + "\ndescriptor=" + DescriptorHash + "\nzip=" + ZipHash + "\nsource-channel=" + SourceChannel + "\ntarget-channel=" + TargetChannel + "\n"); }
        internal static ChannelTransitionIntent Parse(byte[] bytes)
        {
            if (bytes.Length > 2048) throw Prerequisite(); foreach (var b in bytes) if (b > 127 || b == 13) throw Prerequisite();
            var f = Encoding.ASCII.GetString(bytes).Split('\n'); var names = new[] { "root", "operation", "receipt", "settings", "water", "current", "source-id", "target-id", "target", "release", "asset", "descriptor", "zip", "source-channel", "target-channel" };
            var format = f[0] == "BetterAstralParty.ChannelTransition/v2" ? 2 : f[0] == "BetterAstralParty.ChannelTransition/v3" ? 3 : 0;
            if (f.Length != 17 || format == 0 || f[16] != "") throw Prerequisite();
            var v = new string[15]; for (var i = 0; i < v.Length; i++) { if (!f[i+1].StartsWith(names[i] + "=", StringComparison.Ordinal)) throw Prerequisite(); v[i] = f[i+1].Substring(names[i].Length + 1); }
            if (!Regex.IsMatch(v[0], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1], @"\A[0-9a-f]{32}\z")
                || !Digest(v[2]) || v[3] != "-" && !Digest(v[3]) || v[4] != "-" && !Digest(v[4]) || !Digest(v[11]) || !Digest(v[12])) throw Prerequisite();
            _ = UpdateVersion.Parse(v[5]); var target = UpdateVersion.Parse(v[8]);
            if (!ReleaseFeedPolicy.IsChannel(v[13]) || !ReleaseFeedPolicy.IsChannel(v[14]) || v[13] == v[14]
                || v[14] == "Stable" && (target.Major == 0 || target.IsPrerelease)) throw Prerequisite();
            var result = new ChannelTransitionIntent(format, v[0], v[1], v[2], v[3], v[4], v[5], Number(v[6]), Number(v[7]), v[8], Number(v[9]), Number(v[10]), v[11], v[12], v[13], v[14]);
            if (result.SourceChannel == "Beta" && result.SourceRepositoryId != ReleaseFeedPolicy.BetaRepositoryId || result.TargetChannel == "Beta" && result.TargetRepositoryId != ReleaseFeedPolicy.BetaRepositoryId
                || Encoding.ASCII.GetString(result.Bytes()) != Encoding.ASCII.GetString(bytes)) throw Prerequisite(); return result;
        }
        private static bool Digest(string value) { return Regex.IsMatch(value, @"\A[0-9A-F]{64}\z"); }
        private static long Number(string value) { long n; if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n <= 0) throw Prerequisite(); return n; }
        private static ApplySafetyException Prerequisite() { return new ApplySafetyException(ApplyFailure.InstallationRequired); }
    }
}
