using System.Globalization;
using BetterAstralParty.Updating;

internal static class FeedIdentityCheck
{
    internal static void Run()
    {
        var feeds = new[] { ReleaseFeedPolicy.Stable, ReleaseFeedPolicy.Beta,
            new ReleaseFeed(ReleaseFeedPolicy.StableRepository, 0, "Stable") };
        static string Previous(ReleaseFeed feed) => feed.Repository + "|"
            + feed.RepositoryId.ToString(CultureInfo.InvariantCulture) + "|" + feed.Channel;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "ko-KR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                foreach (var feed in feeds)
                    if (feed.Identity != Previous(feed) || feed.AnonymousIdentity != "anonymous|" + Previous(feed)
                        || !ReferenceEquals(feed.Identity, feed.Identity)
                        || !ReferenceEquals(feed.AnonymousIdentity, feed.AnonymousIdentity))
                        throw new Exception("Feed identity changed or was rebuilt on access");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }

        foreach (var (repository, id, channel) in new[] {
            (ReleaseFeedPolicy.StableRepository, -1L, "Stable"),
            (ReleaseFeedPolicy.StableRepository, 1L, "Unknown"),
            (ReleaseFeedPolicy.BetaRepository, 1L, "Stable"),
            (ReleaseFeedPolicy.StableRepository, 1L, "Beta") })
        {
            try { _ = new ReleaseFeed(repository, id, channel); }
            catch (ArgumentException) { continue; }
            throw new Exception("Invalid feed accepted");
        }

        var stable = feeds[0];
        for (var i = 0; i < 1000; i++) { _ = Previous(stable); _ = stable.AnonymousIdentity; }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) GC.KeepAlive("anonymous|" + Previous(stable));
        var previousBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) GC.KeepAlive(stable.AnonymousIdentity);
        var currentBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        if (currentBytes != 0 || previousBytes <= currentBytes) throw new Exception("Feed identity allocation regression");
        Console.WriteLine($"Feed identity: exact Stable/Beta/unconfigured text and invariant culture passed; 10000 anonymous reads: {previousBytes} -> {currentBytes} managed bytes.");
    }
}
