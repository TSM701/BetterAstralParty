using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BetterAstralParty.GaSigning;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static readonly string[] DistributionNames = ["build-version-override", "export-release-source", "signing-boundary", "signing-preparation", "package-archive", "release-remote-verification", "release-finalization"];
    private static void RunDistribution(DevContext context,string name,string stage,string scratch,Dictionary<string,string> options,Action<string> pin)
    {
        foreach(var path in new[]{"developer-tools/PackagingBoundary.cs","developer-tools/PackagingExport.cs","developer-tools/PackagingWhole.cs","developer-tools/PackagingSigning.cs","tools/GaPackageContract.cs","tools/SignLocalInstall.cs","tools/SignRelease.cs"}) pin(Path.Combine(context.Root,path));
        switch(name) {
            case "build-version-override": BuildVersions(context, stage); break;
            case "export-release-source": DistributionExport(context,stage);break;
            case "signing-boundary": foreach(var result in new[]{SignLocalInstall.SelfCheck(),SignRelease.SelfCheck()}) { var row=PackagingBoundary.Parse(Encoding.UTF8.GetBytes(result)); Check(row.GetProperty("selfCheckPassed").GetBoolean()&&!row.GetProperty("keyOpened").GetBoolean()&&row.GetProperty("nativeSignCalls").GetInt32()==0&&!row.GetProperty("outputsCreated").GetBoolean(),"actual bounded signing selfcheck opens no keys"); } break;
            case "signing-preparation": DistributionDescriptors();break;
            case "package-archive": DistributionArchive(context,stage);break;
            case "release-remote-verification": DistributionRemote(context,stage);break;
            case "release-finalization": DistributionFinal(context,stage);break;
            default:throw new InvalidOperationException("Distribution test notported: "+name);
        }
    }
    private static void RefuseDistribution(Action action,string name)
    { try {action();} catch(Exception error) when(error is ArgumentException or InvalidDataException or IOException or JsonException or InvalidOperationException or UpdateValidationException) {Check(true,name);return;}throw new InvalidOperationException("Unsafe distribution input accepted: "+name); }
    private static void DistributionExport(DevContext context,string stage)
    {
        stage=DistributionStage(context,stage);
        PackagingExport.Checks(BuildCommands.Checks);Check(BuildCommands.Checks.Length>=57&&BuildCommands.Checks.All(name=>Supported.Contains(name,StringComparer.Ordinal)),"full canonical C# check inventory retained and runnable");
        RefuseDistribution(()=>PackagingExport.Checks(BuildCommands.Checks.Skip(1).ToArray()),"missing source-bound check"); RefuseDistribution(()=>PackagingExport.Checks(BuildCommands.Checks.Append("unlisted").ToArray()),"unlisted source-bound check"); RefuseDistribution(()=>PackagingExport.Checks(BuildCommands.Checks.Reverse().ToArray()),"reordered check list"); RefuseDistribution(()=>PackagingExport.Checks([]),"empty checks"); RefuseDistribution(()=>PackagingExport.Checks(["one","one"]),"duplicate checks");
        foreach(var count in new[]{239,285,312}) { var rows=Enumerable.Range(0,count).Select(index=>new PackagingInput("src/F"+index+".cs",1,new string('A',64))).ToArray(); var inventory=PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new{files=rows})),1,4096); PackagingExport.Closure(inventory.Keys,rows.Select(row=>row.path)); Check(inventory.Count==count,"bounded recordedsource inventory "+count); }
        foreach(var rows in new[]{new[]{new PackagingInput("src/A.cs",1,new string('A',64)),new PackagingInput("SRC/a.CS",1,new string('A',64))},new[]{new PackagingInput("../private.cs",1,new string('A',64))},new[]{new PackagingInput("src/A.cs",1,"A")}}) RefuseDistribution(()=>PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new{files=rows})),1,4096),"aliased/unsafe/unpinned inventory");
        RefuseDistribution(()=>PackagingBoundary.Rows(PackagingBoundary.Parse(Encoding.UTF8.GetBytes("{\"files\":[{\"path\":\"src/Safe.cs\",\"bytes\":\"1\",\"sha256\":\""+new string('A',64)+"\"}]}"))),"numeric-string byte count rejected");
        RefuseDistribution(()=>PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new{files=Enumerable.Range(0,4097).Select(index=>new PackagingInput("src/F"+index+".cs",1,new string('A',64))).ToArray()})),1,4096),"inventory count limit");
        RefuseDistribution(()=>PackagingBoundary.Rows(PackagingBoundary.Parse(PackagingBoundary.Bytes(new{files=Enumerable.Range(0,5).Select(index=>new PackagingInput("src/F"+index+".cs",64*1024*1024,new string('A',64))).ToArray()})),1,4096),"expanded inventory limit");
        PackagingExport.Closure(["src/a.cs","README.md"],["src/a.cs","README.md","src/a.cs"]);Check(true,"overlappingextras formexactunion"); RefuseDistribution(()=>PackagingExport.Closure(["src/a.cs"],["src/a.cs","README.md"]),"missing export file"); RefuseDistribution(()=>PackagingExport.Closure(["src/a.cs","extra.cs"],["src/a.cs","README.md"]),"unlisted exportfile");
        foreach(var path in new[]{"AGENTS.md",".env","artifacts/secret.cs","releases/secret.cs","src/secret.key","src/secret.dll","src/development-maintenance.json","tools/old.ps1","tools/RecoverGaLocal20261008.cs","tools/SignRc3DescriptorsOnce.cs","tools/SignGaRelease.cs","tools/review-candidates/rc3-signing-diagnostics-r1/SignRc3DescriptorsAutomaticReview.cs"}) RefuseDistribution(()=>PackagingExport.SourceName(path),"private source exclusion "+path);
        var existing=Path.Combine(stage,"retained-output");Directory.CreateDirectory(existing);var sentinel=Path.Combine(existing,"keep.txt");File.WriteAllText(sentinel,"retained");var hash=context.Hash(sentinel);
        RefuseDistribution(()=>PackagingExport.Run(context.Root,new(StringComparer.OrdinalIgnoreCase){["BuildDirectory"]=Path.Combine(stage,"unused-build"),["PackageDirectory"]=Path.Combine(stage,"unused-package"),["OutputDirectory"]=existing}),"realexporter early existing-output refusal");Check(context.Hash(sentinel)==hash,"existing exportoutput preserved");
    }
    private static void DistributionDescriptors()
    {
        var owned=GaPackageContract.OwnedPaths();Check(owned.Length==5&&!owned.Any(path=>path.EndsWith(".ps1",StringComparison.OrdinalIgnoreCase)),"freshnativeowned5hasnoPowerShell");var payload=owned.Select((_,index)=>new[]{(byte)(index+1)}).ToArray();var zip=GaPackageContract.PreviewUpdateZip(payload);var sizes=payload.Select(bytes=>(long)bytes.Length).ToArray();var hashes=payload.Select(UpdateTrust.Hash).ToArray();
        var remote=GaPackageContract.PrepareUpdateDescriptor("1.3.0","Stable",1,2,zip,sizes,hashes);Check(Encoding.ASCII.GetString(remote).StartsWith("BetterAstralParty.UpdateDescriptor/v4\n",StringComparison.Ordinal)&&Encoding.ASCII.GetString(remote).Contains("\nfile-count=5\n",StringComparison.Ordinal),"canonicalremotev4currentownership");var local=GaPackageContract.PrepareLocalInstallDescriptor("1.3.0-rc.1.local.1","Beta",zip,sizes,hashes);Check(Encoding.ASCII.GetString(local).Contains("\nrelease-id=0\nasset-id=0\n",StringComparison.Ordinal)&&Encoding.ASCII.GetString(local).Contains("\npurpose=local-install/Beta\n",StringComparison.Ordinal),"localproofboundtono remoteidentity");
        foreach(var channel in new[]{"Stable","PreRelease","Beta"}) {
            var feed=ReleaseFeedPolicy.For(channel);var version=channel=="Stable"?"1.3.0":"1.3.0-rc.1";var proof=Encoding.ASCII.GetString(GaPackageContract.PrepareUpdateDescriptor(version,channel,1,2,zip,sizes,hashes));
            var publicChannel=ReleaseFeedPolicy.IsPublicChannel(channel);
            Check(proof.Contains("\nrepository="+feed.Repository+"\n",StringComparison.Ordinal)&&proof.Contains("\nrepository-id="+feed.RepositoryId+"\n",StringComparison.Ordinal)&&proof.Contains("\nchannel="+(publicChannel?"Stable":channel)+"\n",StringComparison.Ordinal)&&proof.Contains("\npurpose="+(publicChannel?ReleaseFeedPolicy.PublicPurpose:feed.Purpose)+"\n",StringComparison.Ordinal),"public scope and private channel retain exact signed repository identity: "+channel);
            var localProof=Encoding.ASCII.GetString(GaPackageContract.PrepareLocalInstallDescriptor("1.3.0-rc.1.local.1",channel,zip,sizes,hashes));Check(localProof.Contains("\nrelease-id=0\nasset-id=0\n",StringComparison.Ordinal)&&localProof.Contains("\npurpose=local-install/"+channel+"\n",StringComparison.Ordinal),"local channel purpose never gains remote server identity: "+channel);
            RefuseDistribution(()=>GaPackageContract.PrepareUpdateDescriptor("1.3.0-rc.1.local.1",channel,1,2,zip,sizes,hashes),"local artifacts cannot become remote channel descriptors: "+channel);
        }
        Check(ReleaseFeedPolicy.Stable.Repository==ReleaseFeedPolicy.PreRelease.Repository&&ReleaseFeedPolicy.Stable.RepositoryId==ReleaseFeedPolicy.PreRelease.RepositoryId&&ReleaseFeedPolicy.Stable.Identity!=ReleaseFeedPolicy.PreRelease.Identity&&ReleaseFeedPolicy.Stable.Purpose!=ReleaseFeedPolicy.PreRelease.Purpose,"public channels share repository identity but retain separate signed channel identity");
        Check(remote.SequenceEqual(GaPackageContract.PrepareUpdateDescriptor("1.3.0","PreRelease",1,2,zip,sizes,hashes)),"same public release can change GitHub classification without regenerating its descriptor or ZIP");
        RefuseDistribution(()=>GaPackageContract.PrepareUpdateDescriptor("1.3.0","Prerelease",1,2,zip,sizes,hashes),"channel spelling is canonical and unknown channels stay rejected");
        foreach(var version in new[]{"1.2.1","1.2.9","01.3.0","1.3.0-rc.01"}) RefuseDistribution(()=>GaPackageContract.PrepareUpdateDescriptor(version,"Stable",1,2,zip,sizes,hashes),"old/noncanonicalrelease version rejected");
        RefuseDistribution(()=>GaPackageContract.PrepareUpdateDescriptor("1.3.0-rc.1","Stable",1,2,zip,sizes,hashes),"stable prerelease refused");RefuseDistribution(()=>GaPackageContract.PrepareUpdateDescriptor("1.3.0","Stable",0,2,zip,sizes,hashes),"server releaseID required");RefuseDistribution(()=>GaPackageContract.PrepareUpdateDescriptor("1.3.0","Stable",1,0,zip,sizes,hashes),"server assetID required");RefuseDistribution(()=>GaPackageContract.PreviewUpdateZip(payload.Take(4).ToArray()),"exactownedclosure required");
        var malformed=(string[])hashes.Clone();malformed[0]=new string('a',64);RefuseDistribution(()=>GaPackageContract.PrepareUpdateDescriptor("1.3.0","Stable",1,2,zip,sizes,malformed),"canonicalhash required");
        using var archive=new ZipArchive(new MemoryStream(zip),ZipArchiveMode.Read);Check(archive.Entries.Select(entry=>entry.FullName).SequenceEqual(owned.Order(StringComparer.Ordinal)),"canonicaldeterministicZIPorder");foreach(var entry in archive.Entries){using var stream=entry.Open();var bytes=PackagingWhole.ReadEntry(stream,entry.Length);Check(hashes[Array.IndexOf(owned,entry.FullName)]==UpdateTrust.Hash(bytes),"actualZIPentryboundhash");}
    }
    private static void DistributionArchive(DevContext context,string stage)
    {
        stage=DistributionStage(context,stage);
        var source=Path.Combine(stage,"source.txt");File.WriteAllText(source,"inert archive source");var relative=Path.GetRelativePath(context.Root,source).Replace('\\','/');
        byte[] bytes;string output,digest;
        using(var boundary=new PackagingBoundary(context.Root)){bytes=boundary.Capture(relative,context.Hash(source));output=boundary.Stage(Path.Combine(stage,"archive"));boundary.Output(output);boundary.Write("payload/source.txt",bytes);digest=boundary.Files.Archive("fixture.zip","payload",true);boundary.Files.Recheck();RefuseDistribution(()=>boundary.Output(output),"immutableoutputneveroverwritten");}
        using var archive=ZipFile.OpenRead(Path.Combine(context.Root,output,"fixture.zip"));Check(archive.Entries.Count==1&&archive.Entries[0].FullName=="source.txt","realarchiveexactclosure");using(var stream=archive.Entries[0].Open())Check(PackagingWhole.ReadEntry(stream,archive.Entries[0].Length).SequenceEqual(bytes),"archivecapturedbytespreserved");Check(context.Hash(Path.Combine(context.Root,output,"fixture.zip"))==digest,"archivehashboundtoactualfile");
        Check(context.Hash(source)==UpdateTrust.Hash(bytes),"archiveinputretained");
    }
    private static string DistributionStage(DevContext context,string parent)
    {
        var stage=Path.Combine(context.Root,PackagingBoundary.ArtifactRoot,"distribution-fixture-"+Guid.NewGuid().ToString("N"));Check(!Directory.Exists(stage)&&!File.Exists(stage),"freshisolateddistribution artifactroot");Directory.CreateDirectory(stage);context.WriteJsonCreateNew(Path.Combine(parent,"distribution-root.json"),new{path=Path.GetRelativePath(context.Root,stage),operationalSigning=false});return stage;
    }
}
