namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void UpdateNotificationMetadata(DevContext context, string stage, Dictionary<string, string> options, Action<string> pin) => UpdateNotificationMetadataCore(context, stage, options, pin);
}
