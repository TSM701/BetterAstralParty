using BetterAstralParty.Updating;
namespace BetterAstralParty;
internal sealed record AutomaticUpdateSnapshot(int Generation, int SourceGeneration, string RequestIdentity, string FeedIdentity, string Repository,
    long RepositoryId, string Channel, string TargetVersion, long ReleaseId, long ZipAssetId, long DescriptorAssetId, long SignatureAssetId,
    AutomaticUpdateStatus Status, bool CanDownload, bool CanCancel, bool IsTransition, string InstalledChannel, string InstalledVersion, bool RequiresTransition)
{
    internal bool Matches(ReleaseUpdate release) => RequestIdentity.Length != 0 && Repository == release.Repository && RepositoryId == release.RepositoryId
        && Channel == release.FeedChannel && TargetVersion == release.Version && SourceGeneration == release.Generation
        && ReleaseId == release.ReleaseId && ZipAssetId == release.ZipAssetId && DescriptorAssetId == release.DescriptorAssetId && SignatureAssetId == release.SignatureAssetId;
}
