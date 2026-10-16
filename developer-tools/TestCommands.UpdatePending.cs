namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void LauncherRecovery(DevContext context, string stage, Action<string> pin) => LauncherRecoveryCore(context, stage, pin);
    private static void HandoffStartFailure(DevContext context, string stage, Action<string> pin) => HandoffStartFailureCore(context, stage, pin);
    private static void UpdateNotificationMetadata(DevContext context, string stage, Dictionary<string, string> options, Action<string> pin) => UpdateNotificationMetadataCore(context, stage, options, pin);
}
