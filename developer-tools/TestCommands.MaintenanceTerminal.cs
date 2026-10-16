namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void WholeNativeHistory(DevContext context, string name, string stage, Action<string> pin)
    {
        var prepared = PrepareMaintenancePackages(context, Path.Combine(stage, "packages"), pin);
        var assembly = BuildProject(context, "focus-tests/whole-channel-transition/WholeChannelTransitionFixture.csproj", Path.Combine(stage, "host"), pin, ["-p:FixtureTrustSource=" + prepared.PublicPins]);
        var scratch = Path.Combine(context.Root, "artifacts", "wct-" + Guid.NewGuid().ToString("N"));
        var args = new List<string> { assembly, scratch, Path.Combine(context.Root, "artifacts"), prepared.Packages[0], prepared.Packages[1] };
        if (name == "terminal-whole-history") args.Add("archive-only");
        var environment = EnvironmentFor(stage); environment["LOCALAPPDATA"] = Path.Combine(stage, "inert-local-data");
        Ensure(context.Run(Dotnet(context), args, env: environment), "current native whole/channel and terminal history semantics");
        context.WriteJsonCreateNew(Path.Combine(stage, "whole-history-port.json"), new {
            passed = true, name, protocol = 4, signedFixturePackages = true, productionKeysOpened = false,
            privateHistoricalArtifactsRead = false, realInstallationPerformed = false,
            retired = "Immutable 1.1.3/1.1.4 inspector compatibility and frozen v1 seed; current protocol4 launch, maintenance and partial-removal guards replace them.",
            actualRuntimeGameOrSteamExecution = false
        });
    }
}
