#nullable enable
using System;
using System.Globalization;

namespace BetterAstralParty.Updating
{
    // Public channels share one verified repository identity; only Beta is private.
    internal sealed class ReleaseFeed
    {
        internal readonly string Repository, Channel;
        internal readonly long RepositoryId;
        internal bool RequiresAuthentication { get { return Channel == "Beta"; } }
        internal bool Accepts(bool prerelease) { return Channel == "Beta" || prerelease == (Channel == "PreRelease"); }
        internal bool Configured { get { return RepositoryId > 0; } }
        internal string Identity { get; }
        internal string AnonymousIdentity { get; }
        internal string Purpose { get { return "release/" + Channel; } }
        internal ReleaseFeed(string repository, long repositoryId, string channel)
        {
            if (repositoryId < 0 || !ReleaseFeedPolicy.IsChannel(channel)
                || repository != (channel == "Beta" ? ReleaseFeedPolicy.BetaRepository : ReleaseFeedPolicy.StableRepository))
                throw new ArgumentException("Invalid release feed");
            Repository = repository; RepositoryId = repositoryId; Channel = channel;
            Identity = Repository + "|" + RepositoryId.ToString(CultureInfo.InvariantCulture) + "|" + Channel;
            AnonymousIdentity = "anonymous|" + Identity;
        }
    }
    internal static class ReleaseFeedPolicy
    {
        internal const string StableRepository = "TSM701/BetterAstralParty";
        internal const string BetaRepository = "TSM701/BetterAstralPartyBeta";
        internal const long BetaRepositoryId = 1401226962;
        internal const long StableRepositoryId = 1410226758;
        // The new installation generation is never discovered by earlier clients.
        // Protocol-4 clients (1.3.x) look for the v4 names, so they only show 1.4.0+ as a manual download.
        internal const string UpdateManifestAsset = "BetterAstralParty.update-v5.manifest";
        internal const string UpdateSignatureAsset = "BetterAstralParty.update-v5.signature";
        internal const string Baseline = "1.4.0";
        internal const string PublicPurpose = "release/Public";
        internal static ReleaseFeed Stable { get { return new ReleaseFeed(StableRepository, StableRepositoryId, "Stable"); } }
        internal static ReleaseFeed PreRelease { get { return new ReleaseFeed(StableRepository, StableRepositoryId, "PreRelease"); } }
        internal static ReleaseFeed Beta { get { return new ReleaseFeed(BetaRepository, BetaRepositoryId, "Beta"); } }
        internal static bool IsChannel(string channel) { return channel == "Stable" || channel == "PreRelease" || channel == "Beta"; }
        internal static bool IsPublicChannel(string channel) { return channel == "Stable" || channel == "PreRelease"; }
        internal static bool SupportsAutomaticSource(string version)
            => UpdateVersion.TryParse(version, out var parsed) && parsed!.CompareTo(UpdateVersion.Parse(Baseline + "-0")) >= 0;
        internal static bool SupportsAutomaticTarget(string version, string channel)
            => IsChannel(channel) && UpdateVersion.TryParse(version, out var parsed) && !parsed!.IsLocalPrerelease
                && parsed.CompareTo(UpdateVersion.Parse(channel == "Beta" ? Baseline + "-0" : Baseline)) >= 0;
        internal static ReleaseFeed For(string channel) { return channel == "Stable" ? Stable : channel == "PreRelease" ? PreRelease : channel == "Beta" ? Beta : throw new ArgumentException("Invalid channel"); }
        internal static bool Matches(UpdateContext context)
        {
            var feed = For(context.Channel);
            return feed.Configured && context.Repository == feed.Repository && context.RepositoryId == feed.RepositoryId;
        }
    }
}
