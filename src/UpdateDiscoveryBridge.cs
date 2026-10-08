namespace BetterAstralParty;

// Discovery refreshes do not retire an already authenticated operation merely
// because metadata is temporarily unavailable. Auth/feed/generation changes do.
internal static class UpdateDiscoveryBridge
{
    internal static bool KeepOperation(ReleaseUpdateResult result, int generation,
        string feedIdentity, ReleaseChannel channel, AutomaticUpdateSnapshot operation)
        => result.Status is ReleaseUpdateStatus.Checking or ReleaseUpdateStatus.Stale
            or ReleaseUpdateStatus.Failed or ReleaseUpdateStatus.Incomplete
            or ReleaseUpdateStatus.RateLimited or ReleaseUpdateStatus.Cancelled
        && result.Failure is not (ReleaseUpdateStatus.AuthenticationRequired or ReleaseUpdateStatus.AccessUnavailable)
        && operation.RequestIdentity.Length != 0 && operation.SourceGeneration == generation
        && operation.FeedIdentity == feedIdentity && operation.Channel == channel.ToString()
        && operation.Status is AutomaticUpdateStatus.Downloading or AutomaticUpdateStatus.Preparing
            or AutomaticUpdateStatus.Ready or AutomaticUpdateStatus.Queued or AutomaticUpdateStatus.RetryWaiting;

    internal static void Apply(ReleaseUpdates discovery, AutomaticUpdates automatic, string current)
    {
        var result = discovery.Result;
        var release = result.Status == ReleaseUpdateStatus.Available
            && result.Release is { TransitionCandidate: false } found
            && found.Generation == discovery.Generation ? found : null;
        if (release == null && KeepOperation(result, discovery.Generation, discovery.FeedIdentity,
            discovery.Channel, automatic.Snapshot)) return;
        automatic.Context(release?.DownloadRequest(current, discovery.Channel),
            release?.Generation ?? discovery.Generation);
    }
}
