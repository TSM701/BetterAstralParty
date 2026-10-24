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
            var id = RepositoryIdFor(descriptor.Channel);
            // InstallPolicy rule 1: the exact recorded package returns only as an approved replay or a whole reinstall.
            var exactAllowed = reinstall || intent != null && intent.TargetChannel == descriptor.Channel && intent.SourceChannel == ActiveChannel
                && intent.DescriptorHash == descriptor.DescriptorSha256;
            if (id != 0 && id != descriptor.RepositoryId || !InstallPolicy.AboveRecord(this, descriptor.Channel, descriptor.Version, descriptor.DescriptorSha256, exactAllowed))
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
    // The one written policy for "may this signed package become the installed version". The channel-transition
    // check (focus-tests/channel-transition/Sequences.cs) runs every short sequence of whole installs, in-game
    // updates and approved transitions through the production code and compares it with this policy.
    // 1. A channel's recorded version never goes back. The exact package recorded there may be applied again only
    //    as a whole reinstall or an approved transition replay.
    // 2. A whole install repairs the exact installed package, or advances. When it switches channel it must be newer
    //    than the installed version and not below the highest version ever applied (equal only with that package).
    // 3. An in-game update stays in the active channel and must be newer than installed. Leaving the channel needs an
    //    approved transition, which may lower the installed version but never the target channel's record.
    internal static class InstallPolicy
    {
        internal enum Refusal { None, SwitchNotNewer, BelowHighest, BelowChannelRecord }

        internal static bool AboveRecord(ReceiptChannelState state, string channel, string version, string digest, bool exactAllowed)
        {
            var recorded = state.VersionFor(channel);
            if (recorded == null) return true;
            var comparison = UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse(recorded));
            return comparison > 0 || comparison == 0 && exactAllowed && state.DescriptorFor(channel) == digest;
        }

        internal static Refusal Whole(ReceiptChannelState state, string installed, VerifiedUpdateDescriptor incoming, string? high, string? highDigest)
        {
            var next = UpdateVersion.Parse(incoming.Version);
            var current = next.CompareTo(UpdateVersion.Parse(installed));
            var repair = current == 0 && incoming.DescriptorSha256 == state.DescriptorFor(state.ActiveChannel);
            if (!repair && state.ActiveChannel != incoming.Channel)
            {
                if (current <= 0) return Refusal.SwitchNotNewer;
                if (high != null)
                {
                    var top = next.CompareTo(UpdateVersion.Parse(high));
                    if (top < 0 || top == 0 && incoming.DescriptorSha256 != highDigest) return Refusal.BelowHighest;
                }
            }
            return AboveRecord(state, incoming.Channel, incoming.Version, incoming.DescriptorSha256, true) ? Refusal.None : Refusal.BelowChannelRecord;
        }

        // Shown by Install.cmd; says what blocked the install and what the user can do.
        internal static string Explain(Refusal refusal, ReceiptChannelState state, string installed, VerifiedUpdateDescriptor incoming, string? high)
        {
            const string Fresh = "Uninstall.cmd로 제거한 뒤 새로 설치할 수도 있습니다. / You can also remove it with Uninstall.cmd and install again.";
            string Channel(string channel) => channel == "Stable" ? "릴리즈" : channel == "PreRelease" ? "프리 릴리즈" : "비공개 베타";
            string English(string channel) => channel == "Stable" ? "Release" : channel == "PreRelease" ? "Pre-release" : "Private Beta";
            return refusal switch
            {
                Refusal.SwitchNotNewer => $"다른 채널의 패키지는 지금 설치된 {installed}보다 새 버전이어야 합니다. / A package from another channel must be newer than the installed {installed}. " + Fresh,
                Refusal.BelowHighest => $"이 PC에 적용된 가장 높은 버전은 {high}입니다. {high} 이상을 설치하세요. / The highest version applied on this PC is {high}; install {high} or newer. " + Fresh,
                Refusal.BelowChannelRecord => $"{Channel(incoming.Channel)} 채널에는 이미 {state.VersionFor(incoming.Channel)}이(가) 적용되었습니다. 그보다 새 버전을 설치하세요. / The {English(incoming.Channel)} channel already has {state.VersionFor(incoming.Channel)}; install a newer version. " + Fresh,
                _ => ""
            };
        }
    }
}
