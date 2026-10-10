#nullable enable
using System;
using System.Globalization;

namespace BetterAstralParty.Updating
{
    // Repository IDs are identity, not display names; Stable is the independently verified public repository.
    internal sealed class ReleaseFeed
    {
        internal readonly string Repository, Channel;
        internal readonly long RepositoryId;
        internal bool RequiresAuthentication { get { return Channel == "Beta"; } }
        internal bool Configured { get { return RepositoryId > 0; } }
        internal string Identity { get; }
        internal string AnonymousIdentity { get; }
        internal string Purpose { get { return "release/" + Channel; } }
        internal ReleaseFeed(string repository, long repositoryId, string channel)
        {
            if (repositoryId < 0 || channel != "Stable" && channel != "Beta"
                || repository != (channel == "Stable" ? ReleaseFeedPolicy.StableRepository : ReleaseFeedPolicy.BetaRepository))
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
        internal static ReleaseFeed Stable { get { return new ReleaseFeed(StableRepository, StableRepositoryId, "Stable"); } }
        internal static ReleaseFeed Beta { get { return new ReleaseFeed(BetaRepository, BetaRepositoryId, "Beta"); } }
        internal static ReleaseFeed For(string channel) { return channel == "Stable" ? Stable : channel == "Beta" ? Beta : throw new ArgumentException("Invalid channel"); }
        internal static bool Matches(UpdateContext context)
        {
            var feed = For(context.Channel);
            return feed.Configured && context.Repository == feed.Repository && context.RepositoryId == feed.RepositoryId;
        }
    }
}
