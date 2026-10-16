namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void NativeReinstallMatrix(DevContext context,string stage,string scratch,Action<string> pin)
    {
        var prepared=PrepareMaintenancePackages(context,Path.Combine(stage,"packages"),pin);var packages=prepared.Packages;var metadata=Path.Combine(stage,"metadata");Directory.CreateDirectory(metadata);
        foreach(var mode in new[]{"GOOD","WRONG_NAME","WRONG_PROTOCOL","STRING_PROTOCOL","NONLITERAL_PROTOCOL","NO_ATTRIBUTE","DUPLICATE_ATTRIBUTE","WRONG_ATTRIBUTE"})CompileFrameworkOnly(context,Path.Combine(stage,"metadata-"+mode),"Metadata",["focus-tests/native-reinstall-generation/MetadataPlugin.cs"],pin,target:"library",defines:mode,output:Path.Combine(metadata,mode+".dll"));
        CompileFrameworkOnly(context,Path.Combine(stage,"framework"),"NativeReinstallFrameworkFixture",new[]{"tools/NativeReinstallGeneration.cs","focus-tests/native-reinstall-generation/FrameworkParity.cs"}.Concat(UpdateSources(context)).ToArray(),pin,defines:"NETFRAMEWORK");
        var assembly=BuildProject(context,"focus-tests/native-reinstall-generation/NativeReinstallGenerationFixture.csproj",Path.Combine(stage,"host"),pin,["-p:FixtureTrustSource="+prepared.PublicPins]);
        var args=new[]{assembly,scratch,Path.Combine(context.Root,"artifacts"),packages[0],context.Hash(Path.Combine(packages[0],"package-manifest.json")),packages[1],context.Hash(Path.Combine(packages[1],"package-manifest.json")),metadata};
        Ensure(context.Run(Dotnet(context),args,env:EnvironmentFor(stage)),"actual native removed-generation proof and supported metadata matrix");
        context.WriteJsonCreateNew(Path.Combine(stage,"reinstall-port.json"),new{passed=true,protocol=4,actualWholeAndGenerationApis=true,ephemeralFixtureSignaturesOnly=true,realInstallationPerformed=false,retired=new[]{"Immutable protocol3/1.2.1 and prior rc2 package inputs; supported same-generation ephemeral packages replace them"}});
    }
}
