#nullable enable
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Stored inside the owned receipt, so files, active channel and both floors share its
    // transaction slot. The legacy global high-water file is retained separately.
    internal sealed class ReceiptChannelState
    {
        internal readonly string Root, ActiveChannel, Operation;
        internal readonly string? StableVersion, BetaVersion;
        internal readonly string StableDescriptor, BetaDescriptor;
        internal readonly long StableRepositoryId, BetaRepositoryId;
        internal ReceiptChannelState(string root, string activeChannel, string operation,
            long stableId, string? stable, string stableDescriptor, long betaId, string? beta, string betaDescriptor)
        {
            Root = root; ActiveChannel = activeChannel; Operation = operation;
            StableRepositoryId = stableId; StableVersion = stable; StableDescriptor = stableDescriptor;
            BetaRepositoryId = betaId; BetaVersion = beta; BetaDescriptor = betaDescriptor;
        }
        internal string? Floor(UpdateContext context)
        {
            var id = context.Channel == "Stable" ? StableRepositoryId : BetaRepositoryId;
            if (id != 0 && id != context.RepositoryId) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            return context.Channel == "Stable" ? StableVersion : BetaVersion;
        }
        internal string ActiveVersion { get { return (ActiveChannel == "Stable" ? StableVersion : BetaVersion) ?? throw new ApplySafetyException(ApplyFailure.RecoveryRequired); } }
        internal long ActiveRepositoryId { get { return ActiveChannel == "Stable" ? StableRepositoryId : BetaRepositoryId; } }
        internal string ActiveRepository { get { return ActiveChannel == "Stable" ? ReleaseFeedPolicy.StableRepository : ReleaseFeedPolicy.BetaRepository; } }
        internal ReceiptChannelState Applied(string root, VerifiedUpdateDescriptor descriptor, string operation, ChannelTransitionIntent? intent = null)
        {
            if (Root != root || descriptor.Protocol != 2) throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
            var stable = descriptor.Channel == "Stable";
            var old = stable ? StableVersion : BetaVersion;
            var id = stable ? StableRepositoryId : BetaRepositoryId;
            var comparison = old == null ? 1 : UpdateVersion.Parse(descriptor.Version).CompareTo(UpdateVersion.Parse(old));
            var exactReplay = intent != null && intent.TargetChannel == descriptor.Channel && intent.SourceChannel == ActiveChannel
                && intent.DescriptorHash == descriptor.DescriptorSha256 && (stable ? StableDescriptor : BetaDescriptor) == descriptor.DescriptorSha256 && comparison == 0;
            if (id != 0 && id != descriptor.RepositoryId || comparison <= 0 && !exactReplay)
                throw new UpdateValidationException(UpdateFailure.NotNewer);
            return new ReceiptChannelState(root, descriptor.Channel, operation,
                stable ? descriptor.RepositoryId : StableRepositoryId, stable ? UpdateVersion.Parse(descriptor.Version).Tag : StableVersion, stable ? descriptor.DescriptorSha256 : StableDescriptor,
                stable ? BetaRepositoryId : descriptor.RepositoryId, stable ? BetaVersion : UpdateVersion.Parse(descriptor.Version).Tag, stable ? BetaDescriptor : descriptor.DescriptorSha256);
        }
        internal string Text()
        {
            return "BetterAstralParty.ChannelState/v1\nroot=" + Root + "\nactive=" + ActiveChannel + "\noperation=" + Operation
                + "\nStable=" + Row(StableRepositoryId, StableVersion, StableDescriptor)
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
                if (f.Length != 7 || f[0] != "BetterAstralParty.ChannelState/v1" || f[6] != ""
                    || !Regex.IsMatch(f[1], @"\Aroot=[0-9A-F]{8}:[0-9A-F]{16}\z") || f[2] != "active=Stable" && f[2] != "active=Beta"
                    || !Regex.IsMatch(f[3], @"\Aoperation=[0-9a-f]{32}\z") || !f[4].StartsWith("Stable=", StringComparison.Ordinal) || !f[5].StartsWith("Beta=", StringComparison.Ordinal)) throw Bad();
                long stableId, betaId; string? stable, beta; string stableHash, betaHash;
                ParseRow(f[4].Substring(7), out stableId, out stable, out stableHash);
                ParseRow(f[5].Substring(5), out betaId, out beta, out betaHash);
                var value = new ReceiptChannelState(f[1].Substring(5), f[2].Substring(7), f[3].Substring(10), stableId, stable, stableHash, betaId, beta, betaHash);
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
