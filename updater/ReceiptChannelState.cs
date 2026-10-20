#nullable enable
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Stored inside the owned receipt, so files, active channel and all floors share its
    // transaction slot. The legacy global high-water file is retained separately.
    internal sealed class ReceiptChannelState
    {
        internal readonly string Root, ActiveChannel, Operation;
        internal readonly string? StableVersion, BetaVersion, PreReleaseVersion;
        internal readonly string StableDescriptor, BetaDescriptor, PreReleaseDescriptor;
        internal readonly long StableRepositoryId, BetaRepositoryId, PreReleaseRepositoryId;
        private int _format = 2;
        internal ReceiptChannelState(string root, string activeChannel, string operation,
            long stableId, string? stable, string stableDescriptor, long betaId, string? beta, string betaDescriptor,
            long preReleaseId = 0, string? preRelease = null, string preReleaseDescriptor = "-")
        {
            Root = root; ActiveChannel = activeChannel; Operation = operation;
            StableRepositoryId = stableId; StableVersion = stable; StableDescriptor = stableDescriptor;
            BetaRepositoryId = betaId; BetaVersion = beta; BetaDescriptor = betaDescriptor;
            PreReleaseRepositoryId = preReleaseId; PreReleaseVersion = preRelease; PreReleaseDescriptor = preReleaseDescriptor;
        }
        internal string? VersionFor(string channel) { return channel == "Stable" ? StableVersion : channel == "PreRelease" ? PreReleaseVersion : channel == "Beta" ? BetaVersion : throw Bad(); }
        internal string DescriptorFor(string channel) { return channel == "Stable" ? StableDescriptor : channel == "PreRelease" ? PreReleaseDescriptor : channel == "Beta" ? BetaDescriptor : throw Bad(); }
        internal long RepositoryIdFor(string channel) { return channel == "Stable" ? StableRepositoryId : channel == "PreRelease" ? PreReleaseRepositoryId : channel == "Beta" ? BetaRepositoryId : throw Bad(); }
        internal string? Floor(UpdateContext context)
        {
            var id = RepositoryIdFor(context.Channel);
            if (id != 0 && id != context.RepositoryId) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            return VersionFor(context.Channel);
        }
        internal string ActiveVersion { get { return VersionFor(ActiveChannel) ?? throw new ApplySafetyException(ApplyFailure.RecoveryRequired); } }
        internal long ActiveRepositoryId { get { return RepositoryIdFor(ActiveChannel); } }
        internal string ActiveRepository { get { return ReleaseFeedPolicy.For(ActiveChannel).Repository; } }
        // reinstall: a signed whole package may return to its channel row only with the exact package recorded there.
        internal ReceiptChannelState Applied(string root, VerifiedUpdateDescriptor descriptor, string operation, ChannelTransitionIntent? intent = null, bool reinstall = false)
        {
            if (Root != root || descriptor.Protocol != UpdateTrust.Protocol) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            var stable = descriptor.Channel == "Stable"; var beta = descriptor.Channel == "Beta"; var preRelease = descriptor.Channel == "PreRelease";
            var old = VersionFor(descriptor.Channel);
            var id = RepositoryIdFor(descriptor.Channel);
            var comparison = old == null ? 1 : UpdateVersion.Parse(descriptor.Version).CompareTo(UpdateVersion.Parse(old));
            var exactReplay = intent != null && intent.TargetChannel == descriptor.Channel && intent.SourceChannel == ActiveChannel
                && intent.DescriptorHash == descriptor.DescriptorSha256 && DescriptorFor(descriptor.Channel) == descriptor.DescriptorSha256 && comparison == 0;
            var exactReinstall = reinstall && comparison == 0 && DescriptorFor(descriptor.Channel) == descriptor.DescriptorSha256;
            if (id != 0 && id != descriptor.RepositoryId || comparison <= 0 && !exactReplay && !exactReinstall)
                throw new UpdateValidationException(UpdateFailure.NotNewer);
            return new ReceiptChannelState(root, descriptor.Channel, operation,
                stable ? descriptor.RepositoryId : StableRepositoryId, stable ? UpdateVersion.Parse(descriptor.Version).Tag : StableVersion, stable ? descriptor.DescriptorSha256 : StableDescriptor,
                beta ? descriptor.RepositoryId : BetaRepositoryId, beta ? UpdateVersion.Parse(descriptor.Version).Tag : BetaVersion, beta ? descriptor.DescriptorSha256 : BetaDescriptor,
                preRelease ? descriptor.RepositoryId : PreReleaseRepositoryId, preRelease ? UpdateVersion.Parse(descriptor.Version).Tag : PreReleaseVersion, preRelease ? descriptor.DescriptorSha256 : PreReleaseDescriptor);
        }
        internal string Text()
        {
            return "BetterAstralParty.ChannelState/v" + _format + "\nroot=" + Root + "\nactive=" + ActiveChannel + "\noperation=" + Operation
                + "\nStable=" + Row(StableRepositoryId, StableVersion, StableDescriptor)
                + (_format == 1 ? "" : "\nPreRelease=" + Row(PreReleaseRepositoryId, PreReleaseVersion, PreReleaseDescriptor))
                + "\nBeta=" + Row(BetaRepositoryId, BetaVersion, BetaDescriptor) + "\n";
        }
        private static string Row(long id, string? version, string hash) { return id.ToString(CultureInfo.InvariantCulture) + "\t" + (version ?? "-") + "\t" + hash; }
        internal string Encoded { get { return Convert.ToBase64String(Encoding.ASCII.GetBytes(Text())); } }
        internal static ReceiptChannelState Parse(string encoded)
        {
            try {
                var bytes = Convert.FromBase64String(encoded); if (bytes.Length > 1024) throw Bad();
                foreach (var b in bytes) if (b > 127 || b == 13) throw Bad();
                var f = Encoding.ASCII.GetString(bytes).Split('\n');
                var legacy = f.Length == 7 && f[0] == "BetterAstralParty.ChannelState/v1";
                if ((!legacy && (f.Length != 8 || f[0] != "BetterAstralParty.ChannelState/v2")) || f[f.Length - 1] != "") throw Bad();
                var betaRow = legacy ? 5 : 6;
                if (!Regex.IsMatch(f[1], @"\Aroot=[0-9A-F]{8}:[0-9A-F]{16}\z") || !f[2].StartsWith("active=", StringComparison.Ordinal)
                    || !ReleaseFeedPolicy.IsChannel(f[2].Substring(7)) || legacy && f[2] == "active=PreRelease"
                    || !Regex.IsMatch(f[3], @"\Aoperation=[0-9a-f]{32}\z") || !f[4].StartsWith("Stable=", StringComparison.Ordinal)
                    || !f[betaRow].StartsWith("Beta=", StringComparison.Ordinal) || !legacy && !f[5].StartsWith("PreRelease=", StringComparison.Ordinal)) throw Bad();
                long stableId, betaId; string? stable, beta; string stableHash, betaHash;
                ParseRow(f[4].Substring(7), out stableId, out stable, out stableHash);
                ParseRow(f[betaRow].Substring(5), out betaId, out beta, out betaHash);
                long preId = 0; string? pre = null; var preHash = "-";
                if (!legacy) ParseRow(f[5].Substring(11), out preId, out pre, out preHash);
                var value = new ReceiptChannelState(f[1].Substring(5), f[2].Substring(7), f[3].Substring(10), stableId, stable, stableHash, betaId, beta, betaHash, preId, pre, preHash) { _format = legacy ? 1 : 2 };
                _ = value.ActiveVersion;
                if (value.Encoded != encoded) throw Bad(); return value;
            } catch (FormatException) { throw Bad(); } catch (UpdateValidationException) { throw Bad(); }
        }
        private static void ParseRow(string row, out long id, out string? version, out string hash)
        {
            var f = row.Split('\t'); id = 0; version = null; hash = "-";
            if (f.Length != 3 || !long.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out id) || id < 0) throw Bad();
            if (f[1] == "-") { if (id != 0 || f[2] != "-") throw Bad(); return; }
            if (id <= 0 || !Regex.IsMatch(f[2], @"\A[0-9A-F]{64}\z")) throw Bad();
            UpdateVersion.Parse(f[1]); version = f[1]; hash = f[2];
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.InvalidState); }
    }
}
