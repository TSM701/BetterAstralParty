using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static (string[] Packages,string PublicPins) PrepareMaintenancePackages(DevContext context, string stage, Action<string> pin)
    {
        Directory.CreateDirectory(stage);
        var sources = new[] { "tools/SetupHost.cs", "tools/GameCompatibility.cs", "tools/GaPackageContract.cs", "src/ReleaseHttpClient.cs", "src/WindowsReleaseCredentialStore.cs", "diagnostics/MinimalDiagnosticProjection.cs", "focus-tests/native-maintenance-integration/FixtureEntry.cs", "focus-tests/native-maintenance-integration/EmergencyFixture.cs" }
            .Concat(Directory.EnumerateFiles(Path.Combine(context.Root,"tools"),"Native*.cs").Select(path=>Path.GetRelativePath(context.Root,path)))
            .Concat(UpdateSources(context).Where(path=>!path.EndsWith("ProductionUpdateKeys.cs",StringComparison.Ordinal))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(var source in sources) pin(Path.Combine(context.Root,source)); pin(Path.Combine(context.Root,"updater/ProductionUpdateKeys.cs"));
        var entryPath=Path.GetFullPath(Path.Combine(context.Root,"tools","NativeMaintenanceEntry.cs")); var entry=File.ReadAllText(entryPath); var scoped=entry.Replace("Environment.GetFolderPath(","EmergencyFixtureFolders.GetFolderPath(",StringComparison.Ordinal).Replace("Path.GetTempPath()","EmergencyFixtureFolders.GetTempPath()",StringComparison.Ordinal);
        Check(scoped.Replace("EmergencyFixtureFolders.GetFolderPath(","Environment.GetFolderPath(",StringComparison.Ordinal).Replace("EmergencyFixtureFolders.GetTempPath()","Path.GetTempPath()",StringComparison.Ordinal)==entry,"native entry substitution changesonlyfolderbroker");
        var derived=Path.GetFullPath(Path.Combine(stage,"NativeMaintenanceEntry.derived.cs")); File.WriteAllText(derived,scoped,new UTF8Encoding(false)); var replacementCount=0;
        sources=sources.Select(path=> { if(!Path.GetFullPath(Path.Combine(context.Root,path)).Equals(entryPath,StringComparison.OrdinalIgnoreCase))return path; replacementCount++;return derived; }).ToArray();
        var derivedCount=sources.Count(path=>Path.GetFullPath(Path.Combine(context.Root,path)).Equals(derived,StringComparison.OrdinalIgnoreCase));
        Check(replacementCount==1 && derivedCount==1 && !sources.Any(path=>Path.GetFullPath(Path.Combine(context.Root,path)).Equals(entryPath,StringComparison.OrdinalIgnoreCase)),"compiled integration source replaces exactly one original entry with the scoped broker");
        context.WriteJsonCreateNew(Path.Combine(stage,"native-entry-source-substitution.json"),new { originalSource=entryPath,originalSha256=context.Hash(entryPath),derivedSource=derived,derivedSha256=context.Hash(derived),replacementCount,compiledDerivedCount=derivedCount,originalCompiled=false,folderBrokerOnly=true });
        var global=Path.Combine(stage,"GlobalUsings.cs"); File.WriteAllText(global,"global using System; global using System.IO; global using System.Collections.Generic; global using System.Threading; global using System.Threading.Tasks; global using BetterAstralParty.Diagnostics;",new UTF8Encoding(false));
        var collectorPath=Path.Combine(context.Root,"diagnostics/DiagnosticBundle.cs"); pin(collectorPath); var collector=Path.Combine(stage,"DiagnosticBundle.derived.cs"); File.WriteAllText(collector,"#nullable disable\n"+File.ReadAllText(collectorPath),new UTF8Encoding(false));
        using var key=RSA.Create(3072); var publicKey=key.ExportParameters(false); var pins=Path.Combine(stage,"FixturePublicPins.cs");
        File.WriteAllText(pins,"namespace BetterAstralParty.Updating { internal static class ProductionUpdateKeys { internal static TrustedUpdateKey[] Read() { return new[] { new TrustedUpdateKey(\"native-maint-fixture\",new System.Security.Cryptography.RSAParameters { Modulus=System.Convert.FromBase64String(\""+Convert.ToBase64String(publicKey.Modulus!)+"\"), Exponent=new byte[]{1,0,1} }) }; } } }",new UTF8Encoding(false));
        void Put(string path,string text) { Check(!File.Exists(path)&&Path.GetFullPath(path).StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"integration create-new scoped fixture file"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,text,new UTF8Encoding(false)); }
        void Run(string package,string mode) { var image=Path.Combine(package,"artifacts/BetterAstralParty-Setup.exe"); Check(image.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"only inert integrationhost executes"); Ensure(context.Run(image,[mode],cwd:package,env:new Dictionary<string,string?> { ["LOCALAPPDATA"]=stage }),"actual native maintenance fixture "+mode); }
        var packages=new List<string>();
        foreach(var variant in new[]{(Name:"baseline",Version:"1.4.0",Channel:"Stable"),(Name:"incoming",Version:"1.5.0-rc.1.local.99",Channel:"Beta")}) {
            var package=Path.Combine(stage,variant.Name,"package"); Directory.CreateDirectory(Path.Combine(package,"artifacts")); var boundary=Path.Combine(stage,variant.Name+"-boundary.cs");
            Put(boundary,"using System.Reflection; [assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.8\")] [assembly:AssemblyProduct(\"BetterAstralParty\")] [assembly:AssemblyTitle(\"BetterAstralParty native launch host\")] [assembly:AssemblyInformationalVersion(\""+variant.Version+"\")] internal static class FixtureBoundary { internal const string Root=@\""+stage+"\\\"; internal const string Version=\""+variant.Version+"\"; internal const string Channel=\""+variant.Channel+"\"; }");
            CompileFrameworkOnly(context,Path.Combine(stage,variant.Name,"compile"),"NativeMaintenanceIntegrationFixture",sources.Concat([global,collector,pins,boundary]).ToArray(),pin,output:Path.Combine(package,"artifacts/BetterAstralParty-Setup.exe"));
            CompileFrameworkOnly(context,Path.Combine(stage,variant.Name,"plugin-compile"),"FixturePlugin",["focus-tests/native-maintenance-integration/FixturePlugin.cs",boundary],pin,target:"library",output:Path.Combine(package,"artifacts/BetterAstralParty.dll"));
            foreach(var name in new[]{"Install.cmd","Uninstall.cmd","Force-Uninstall.cmd","tools/AstralParty-Vanilla.cmd","tools/BetterAstralParty-Mod.cmd"}) Put(Path.Combine(package,name),"inert fixture payload; never execute: "+name);
            foreach(var name in new[]{"README.md","LICENSE","docs/INSTALL.md",".deps/bepinex/changelog.txt",".deps/bepinex/README.md","THIRD-PARTY-NOTICES/Loader-LICENSE.txt","THIRD-PARTY-NOTICES/component-matrix.json","THIRD-PARTY-SOURCE/Loader-source.zip"}) Put(Path.Combine(package,name),"distribution-only fixture documentation: "+name);
            Put(Path.Combine(package,".deps/bepinex/doorstop_config.ini"),"[General]\r\nenabled=true\r\ncustom=sharedfixture\r\n"); Put(Path.Combine(package,".deps/bepinex/winhttp.dll"),"inert shared winhttp loader fixture"); Put(Path.Combine(package,".deps/bepinex/BepInEx/core/BepInEx.Core.dll"),"inert shared BepInEx core fixture");
            if(variant.Name=="baseline") Run(package,"seed"); Run(package,"prepare");
            { var descriptor=File.ReadAllBytes(Path.Combine(package,"update-proof/update.descriptor")); var signature=key.SignData(descriptor,HashAlgorithmName.SHA256,RSASignaturePadding.Pss); Put(Path.Combine(package,"update-proof/update.signature"),"BetterAstralParty.UpdateSignature/v1\nkey-id=native-maint-fixture\nsignature="+Convert.ToBase64String(signature)+"\n"); }
            if(packages.Count!=0) { var prior=packages[0]; var descriptor=File.ReadAllBytes(Path.Combine(prior,"update-proof/update.descriptor")); var digest=UpdateTrust.Hash(descriptor); var directory=Path.Combine(package,"update-proof/prior"); Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory,digest+".descriptor"),descriptor); File.Copy(Path.Combine(prior,"update-proof/update.signature"),Path.Combine(directory,digest+".signature"),false); }
            Put(Path.Combine(package,"tools/approved-install-bundles.json"),"{\"schema\":1,\"bundles\":[]}");
            var rows=Directory.EnumerateFiles(package,"*",SearchOption.AllDirectories).Select(path=>new PackagingInput(Path.GetRelativePath(package,path).Replace('\\','/'),new FileInfo(path).Length,context.Hash(path))).ToArray();
            Put(Path.Combine(package,"package-manifest.json"),JsonSerializer.Serialize(new{schema=1,version=variant.Version,kind="native-maintenance-integration-fixture",buildIdentity=new string('0',64),files=rows})); Run(package,"rebind"); packages.Add(package);
        }
        return (packages.ToArray(),pins);
    }
    private static void NativeMaintenanceIntegration(DevContext context,string stage,Action<string> pin)
    {
        var prepared=PrepareMaintenancePackages(context,stage,pin);var packages=prepared.Packages;
        void Run(string package,string mode) => Ensure(context.Run(Path.Combine(package,"artifacts/BetterAstralParty-Setup.exe"),[mode],cwd:package,env:new Dictionary<string,string?>{["LOCALAPPDATA"]=stage}),"actual native maintenance fixture "+mode);
        foreach(var mode in new[]{"auth","unused-inputs","documents-edited","preview","install","water","pending"}) Run(packages[0],mode);
        foreach(var mode in new[]{"auth","documents-missing","upgrade-preview","upgrade","changed-owned","emergency","uninstall-preview","uninstall","reinstall-preview","reinstall","game-updated-uninstall-preview","game-updated-uninstall","optional-steam-uninstall","missing-game-removal","takeover"}) Run(packages[1],mode);
        var reports=Directory.EnumerateFiles(Path.Combine(stage,"results"),"*.json").Select(path=>PackagingBoundary.Parse(File.ReadAllBytes(path))).ToArray(); Check(reports.Length!=0&&reports.All(report=>report.GetProperty("Passed").GetBoolean()),"allactualnativeintegration reports passed");
        context.WriteJsonCreateNew(Path.Combine(stage,"native-integration.json"),new{passed=true,checks=reports.Sum(report=>report.GetProperty("Checks").GetInt32()),reports,actualProductionMaintenanceApis=true,facades=false,onlyEphemeralInMemoryFixtureSigning=true,productionKeysOpened=false,actualGameSteamOrRegistryChanged=false});
    }
    private static void MaintenanceHistoryBinding(DevContext context,string stage,Action<string> pin)
    {
        var templatePath=Path.Combine(context.Root,"focus-tests/history-binding/MetadataPlugin.cs"); pin(templatePath); var template=File.ReadAllText(templatePath); Check(System.Text.RegularExpressions.Regex.Matches(template,"\"1\\.4\\.0\"").Count==1,"one inertmetadata version literal");
        var images=new List<string>(); foreach(var version in new[]{"1.4.0","1.4.1-rc.1","1.4.2","1.4.3","1.4.4"}) { var metadata=Path.Combine(stage,"metadata-"+version+".cs"); File.WriteAllText(metadata,template.Replace("\"1.4.0\"","\""+version+"\"",StringComparison.Ordinal),new UTF8Encoding(false)); images.Add(CompileFrameworkOnly(context,Path.Combine(stage,"metadata-"+version),"Metadata",[metadata],pin,target:"library",defines:"")); }
        var inert=CompileFrameworkOnly(context,Path.Combine(stage,"inert"),"InertProcess",["updater-safety-tests/InertProcess.cs"],pin,defines:""); var assembly=BuildProject(context,"focus-tests/history-binding/HistoryBindingFixture.csproj",Path.Combine(stage,"host"),pin);
        Ensure(context.Run(Dotnet(context),new[]{assembly,Path.Combine(stage,"fixtures"),inert}.Concat(images),env:EnvironmentFor(stage)),"actual history/receipt binding and producerZIPfixture");
    }
}
