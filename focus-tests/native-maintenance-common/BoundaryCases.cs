using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static partial class NativeCommonFixture
{
    private static void BundleMissing()
    {
        var game=Scratch+"\\bundle-game";Directory.CreateDirectory(Io(game));Write(game+"\\installed-state.keep",Encoding.UTF8.GetBytes("exact installed state"));Write(game+"\\OtherMod.keep",Encoding.UTF8.GetBytes("exact unrelated mod"));var before=Snapshot(game);
        foreach(var version in new[]{"1.3.0","1.3.0-0","1.3.0-rc.1.local.1"})Check(UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse("1.3.0-0"))>=0,"strict native version comparison preserves prerelease arrays");
        foreach(var mode in new[]{"missing-setup","missing-directory","changed-present-hash"})
        {
            var package=Scratch+"\\"+mode;Directory.CreateDirectory(Io(package));var bytes=Encoding.ASCII.GetBytes("inert bundle payload never execute");var first=package+"\\first.keep";Write(first,bytes);var launcher=package+"\\artifacts\\BetterAstralParty-Setup.exe";
            if(mode!="missing-directory")Directory.CreateDirectory(WindowsPath.ForIo(WindowsPath.Parent(launcher)));if(mode=="changed-present-hash")Write(launcher,bytes);
            var rows=new List<object>{new{path="first.keep",bytes=bytes.Length,sha256=UpdateTrust.Hash(bytes)},new{path="artifacts/BetterAstralParty-Setup.exe",bytes=bytes.Length,sha256=new string('0',64)}};
            for(var n=0;n<8;n++)rows.Add(new{path="later-"+n+".keep",bytes=0,sha256=new string('0',64)});
            var manifest=package+"\\package-manifest.json";Write(manifest,MaintenanceJson.Write(new{schema=1,version="1.4.0",kind="inert incomplete fixture",files=rows}));Exception? failure=null;
            try{using(var bundle=new NativeMaintenanceBundle(package,game)){} }catch(Exception error){failure=error;}
            Check(failure is InvalidDataException,"incomplete native bundle fails before metadata execution");var message=failure!.Message;
            if(mode=="changed-present-hash")Check(message.Contains("hash/size mismatch")&&!message.Contains("BAP-PACKAGE-INCOMPLETE"),"present changed bytes retain their distinct cause");
            else Check(message.Contains("BAP-PACKAGE-INCOMPLETE")&&message.Contains("artifacts/BetterAstralParty-Setup.exe")&&message.Contains("전체 ZIP")&&message.Contains("complete ZIP"),"missing package file names actionable bilingual full-archive fix");
            foreach(var file in new[]{manifest,first})using(var released=File.Open(Io(file),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(released.CanWrite,"failed bundle releases all retained input leases");Unchanged(before,game,"missing bundle leaves installed and other-mod inventory exact");
        }
    }
    private static void ReaderOrigin(string image)
    {
        var bytes=File.ReadAllBytes(Io(image));var sourceHash=UpdateTrust.Hash(bytes);var before=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.FullName).ToArray();
        foreach(var zone in new[]{0,3,4})
        {
            var directory=Scratch+"\\origin-"+zone;Directory.CreateDirectory(Io(directory));var path=directory+"\\BetterAstralParty.dll";Write(path,bytes);var origin="[ZoneTransfer]\r\nZoneId="+zone+"\r\n";
            if(zone!=0)File.WriteAllText(WindowsPath.ForIo(path)+":Zone.Identifier",origin,Encoding.ASCII);
            using(var fence=new WindowsFileFence(directory))using(var held=fence.OpenLaunchImage("BetterAstralParty.dll"))
            {
                var parsed=InstalledPluginMetadata.ReadMaintenance(UpdateTicket.Read(held,UpdateTrust.MaxFileBytes));Check(parsed.Version=="1.4.0"&&parsed.UpdateProtocol==5&&parsed.SettingsSchema==1,"native metadata parsing ignores download-origin execution policy, zone "+zone);
                Check(held.Hash()==sourceHash,"metadata input remains exact bytes");var denied=false;try{using(var writer=File.Open(Io(path),FileMode.Open,FileAccess.Write,FileShare.Read)){} }catch(IOException){denied=true;}Check(denied,"held native metadata input excludes concurrent writes");
            }
            Check(zone==0?!File.Exists(WindowsPath.ForIo(path)+":Zone.Identifier"):File.ReadAllText(WindowsPath.ForIo(path)+":Zone.Identifier")==origin,"download origin stream never cleared or fabricated");using(var released=File.Open(Io(path),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(released.CanWrite,"metadata lease released after parse");
            using(var locked=File.Open(Io(path),FileMode.Open,FileAccess.ReadWrite,FileShare.None))using(var fence=new WindowsFileFence(directory))Reject(()=>{using(var held=fence.OpenLaunchImage("BetterAstralParty.dll")){}},"exclusive metadata source remains a bounded read refusal");
        }
        Reject(()=>InstalledPluginMetadata.ReadMaintenance(Encoding.ASCII.GetBytes("Not a managed image")),"malformed image rejected without execution");
        var changed=(byte[])bytes.Clone();var literal=Encoding.ASCII.GetBytes("1.4.0");var offset=-1;for(var n=0;n<=changed.Length-literal.Length;n++)if(changed.Skip(n).Take(literal.Length).SequenceEqual(literal)){offset=n;break;}Check(offset>=0,"inert metadata version literal found");changed[offset+2]=(byte)'2';Reject(()=>InstalledPluginMetadata.ReadMaintenance(changed),"tampered serialized attribute must match the literal metadata version");
        Check(!CoreResourceSatellite(typeof(NativeCommonFixture).Assembly)&&!CoreResourceSatellite(typeof(Enumerable).Assembly),"core satellite exception never admits the fixture or System.Core");
        var after=AppDomain.CurrentDomain.GetAssemblies();var newlyLoaded=after.Where(a=>!before.Contains(a.FullName)).Select(a=>a.FullName+"; GAC="+a.GlobalAssemblyCache+"; Location="+(a.IsDynamic?"dynamic":a.Location)).ToArray();
        Check(after.Where(a=>before.Contains(a.FullName)||!CoreResourceSatellite(a)).Select(a=>a.FullName).SequenceEqual(before),"incoming plugin and foreign metadata readers never loaded for execution; newlyLoaded="+string.Join(" | ",newlyLoaded));Check(UpdateTrust.Hash(File.ReadAllBytes(Io(image)))==sourceHash,"original inert source preserved");
    }
    private static bool CoreResourceSatellite(System.Reflection.Assembly assembly)
    {
        var core=typeof(object).Assembly.GetName();var candidate=assembly.GetName();
        var expected=core.GetPublicKeyToken();var actual=candidate.GetPublicKeyToken();
        if(!assembly.GlobalAssemblyCache||candidate.Name!=core.Name+".resources"||!Equals(candidate.Version,core.Version)||expected==null||actual==null||!actual.SequenceEqual(expected))return false;
        for(var culture=System.Globalization.CultureInfo.CurrentUICulture;culture.Name.Length!=0;culture=culture.Parent)
            if(candidate.CultureName==culture.Name)return true;
        return false;
    }
    private static string Deep(string root,int length)
    {
        if(root!=WindowsPath.Absolute(root))throw new ArgumentException("Canonical deep fixture prefix required.",nameof(root));
        if(length<root.Length)throw new ArgumentOutOfRangeException(nameof(length));
        if(length-root.Length==1){var leaf=root.Length==3?"d":WindowsPath.Leaf(root)+"d";if(!WindowsPath.SafeComponent(leaf))throw new ArgumentException("Deep fixture leaf cannot be extended safely.",nameof(length));root+="d";}
        while(root.Length<length){var remaining=length-root.Length;var separator=root.Length==3?"":"\\";var size=Math.Min(80,remaining-separator.Length);if(remaining-size-separator.Length==1)--size;root+=separator+new string('d',size);}
        if(root.Length!=length)throw new InvalidOperationException("Deep fixture must reach its exact requested length.");
        if(root.Length!=3)foreach(var component in root.Substring(3).Split('\\'))if(!WindowsPath.SafeComponent(component))throw new InvalidOperationException("Unsafe deep fixture component.");
        return root;
    }
    private static void DeepPadding()
    {
        foreach(var prefix in new[]{Scratch+"\\padding",Scratch+"\\"+new string('x',254)})
        foreach(var remaining in new[]{1,2,80,81,82,161,162,163})
        {
            var path=Deep(prefix,prefix.Length+remaining);
            Check(path.Length==prefix.Length+remaining&&path.StartsWith(prefix,StringComparison.Ordinal)&&WindowsPath.Absolute(path)==path
                &&path.Substring(prefix.Length).TrimStart('\\').Split('\\').All(component=>component.Length<=80&&WindowsPath.SafeComponent(component)),"deep padding exact safe remainder "+remaining+" with 80-character chunks");
        }
        var saturated=Scratch+"\\"+new string('x',255);var rejected=false;
        try{Deep(saturated,saturated.Length+1);}catch(ArgumentException){rejected=true;}
        Check(rejected,"deep padding refuses one-character extension of a saturated leaf");
        var unchanged=Scratch+"\\padding";Check(Deep(unchanged,unchanged.Length)==unchanged,"zero deep padding preserves prefix");
        rejected=false;try{Deep(unchanged,unchanged.Length-1);}catch(ArgumentOutOfRangeException){rejected=true;}
        Check(rejected,"deep padding never silently accepts a shorter target");
    }
    private static void TransactionScenario(string mode)
    {
        DeepPadding();
        var original=Scratch;var backup=Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            if(mode=="transaction-long-ps7"||mode=="transaction-long-ps5"){Scratch=Deep(Scratch,340);Directory.CreateDirectory(WindowsPath.ForIo(Scratch));Environment.SetEnvironmentVariable("LOCALAPPDATA",Scratch);Transactions();}
            else if(mode=="transaction-root1100-ps7"||mode=="transaction-root1100-ps5"){Scratch=Deep(Scratch,1100);Directory.CreateDirectory(WindowsPath.ForIo(Scratch));Environment.SetEnvironmentVariable("LOCALAPPDATA",Scratch);Transactions();}
            else if(mode=="transaction-target1100-ps7"||mode=="transaction-target1100-ps5")
            {
                using(var holder=new Bundle("deep-target")){var bundle=holder.Value;var folder=Deep(bundle.GameRoot+"\\target",1100);var target=folder+"\\owned.keep";var unrelated=bundle.GameRoot+"\\OtherMod.keep";Write(target,Encoding.ASCII.GetBytes("original"));Write(unrelated,Encoding.ASCII.GetBytes("unrelated exact"));
                    using(NativeMaintenance.AcquireInstallLock(bundle)){var txn=NativeMaintenance.StartTransaction(bundle,new[]{target},4096);Write(target,Encoding.ASCII.GetBytes("changed"));using(var gate=new UpdateLaunchGate.Lease(bundle.GameRoot))NativeMaintenance.RestoreTransaction(bundle,txn,new[]{target},new Dictionary<string,string?>{{target,UpdateTrust.Hash(Read(target))}});Check(Encoding.ASCII.GetString(Read(target))=="original"&&Encoding.ASCII.GetString(Read(unrelated))=="unrelated exact","1100-character target transaction restores exact original without affecting other mods");}}
            }
            else if(mode=="transaction-standard")Transactions();else throw new ArgumentException("Unknown transaction matrix");
            Check(true,"native C# transaction exercised "+mode+"; PS-specific host comparison explicitly retired");
        }
        finally{Scratch=original;Environment.SetEnvironmentVariable("LOCALAPPDATA",backup);}
    }
}
