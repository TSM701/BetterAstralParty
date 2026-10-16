using System.Security.Cryptography;
using System.Text;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void MaintenanceHistory(DevContext context,string stage,Action<string> pin)
    {
        using var key=RSA.Create(3072);var publicKey=key.ExportParameters(false);var trustSource=Path.Combine(stage,"FixturePublicPins.cs");
        File.WriteAllText(trustSource,"namespace BetterAstralParty.Updating { internal static class ProductionUpdateKeys { internal static TrustedUpdateKey[] Read() { return new[] { new TrustedUpdateKey(\"history-remote-fixture\",new System.Security.Cryptography.RSAParameters { Modulus=System.Convert.FromBase64String(\""+Convert.ToBase64String(publicKey.Modulus!)+"\"), Exponent=new byte[]{1,0,1} }) }; } } }",new UTF8Encoding(false));
        var descriptor=GaPackageContract.PrepareUpdateDescriptor("1.3.1","Stable",1,2,[1],Enumerable.Repeat(1L,InstallReceipt.Owned.Length).ToArray(),Enumerable.Repeat(new string('A',64),InstallReceipt.Owned.Length).ToArray());
        var proof=Path.Combine(stage,"proof");Directory.CreateDirectory(proof);File.WriteAllBytes(Path.Combine(proof,"update.descriptor"),descriptor);File.WriteAllBytes(Path.Combine(proof,"update.signature"),new DetachedSignature("history-remote-fixture",key.SignData(descriptor,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)).Bytes());
        pin(trustSource);pin(Path.Combine(proof,"update.descriptor"));pin(Path.Combine(proof,"update.signature"));
        var assembly=BuildProject(context,"focus-tests/maintenance-history/MaintenanceHistoryFixture.csproj",Path.Combine(stage,"compiled"),pin,["-p:FixtureTrustSource="+trustSource]);
        var fixture=Path.Combine(context.Root,"artifacts","mhi-"+Guid.NewGuid().ToString("N")[..8]);
        Ensure(context.Run(Dotnet(context),[assembly,fixture,Path.Combine(context.Root,"artifacts"),proof],env:EnvironmentFor(stage)),"actual current-generation history inventory and retention");
        context.WriteJsonCreateNew(Path.Combine(stage,"history-port.json"),new{passed=true,protocol=4,fixtureOnlyInMemorySigning=true,operationalSigning=false,immutablePriorPackagesReadOrModified=false,retiredChecks=new[]{"Pre-1.3.0 protocol-3 compiled consumer parity, outside the supported baseline"}});
    }
}
