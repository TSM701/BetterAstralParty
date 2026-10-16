using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BetterAstralParty.Installation;

internal static partial class FixtureEntry
{
    private static void DiscoveryExtended(string root)
    {
        var isolated=Path.Combine(root,"extended-discovery");var steam=Path.Combine(isolated,"Steam");var game=Path.Combine(steam,"steamapps","common","Astral Party","8vJXnINT");Text(Path.Combine(game,"AstralParty_INT.exe"),"inert never execute");var vdf=Path.Combine(steam,"steamapps/libraryfolders.vdf");var package=Path.Combine(isolated,"Desktop/Package");Directory.CreateDirectory(package);
        var unavailable=Enumerable.Range('A',26).Select(number=>(char)number+@":\BapMissingLibrary").FirstOrDefault(path=>!Directory.Exists(Path.GetPathRoot(path)));if(unavailable==null)throw new InvalidOperationException("Isolated discovery test requires a missing drive; no drive is modified.");
        Text(vdf,"\"path\" \""+unavailable.Replace("\\","\\\\")+"\" \"path\" \""+steam.Replace("\\","\\\\")+"\" \"path\" \""+Path.Combine(isolated,"Disconnected Library").Replace("\\","\\\\")+"\" \"path\" \""+unavailable.Replace("\\","\\\\")+"\"");
        Check(NativeSteamMaintenance.ResolveIntDirectory(unavailable)==null&&NativeSteamMaintenance.FindGameDirectories(unavailable,unavailable).Count==0,"unavailable drive input is not accepted or guessed");Check(NativeSteamMaintenance.FindGameDirectories(unavailable,Path.Combine(game,"Package")).SequenceEqual(new[]{game}),"unavailable Steam drive does not hide adjacent INT game");
        Check(NativeSteamMaintenance.FindGameDirectories(steam.Replace('\\','/'),package).SequenceEqual(new[]{game}),"registry forward slash and VDF backslash duplicates normalize");Check(NativeSteamMaintenance.FindGameDirectories(steam.ToUpperInvariant()+"\\",game).Count==1,"case, trailing slash and package duplicates normalize");Check(NativeSteamMaintenance.ResolveIntDirectory("\""+Path.GetDirectoryName(game)+"\"")==game,"quoted parent INT selection works");
        var other=Path.Combine(isolated,"Library2");var otherGame=Path.Combine(other,"steamapps","common","Custom Astral","8vJXnINT");Text(Path.Combine(otherGame,"AstralParty_INT.exe"),"inert");Text(Path.Combine(other,"steamapps/appmanifest_2622000.acf"),"\"installdir\" \"Custom Astral\"");File.AppendAllText(vdf," \"path\" \""+other.Replace("\\","\\\\")+"\"");var found=NativeSteamMaintenance.FindGameDirectories(steam,package);Check(found.Count==2&&found.Contains(game)&&found.Contains(otherGame),"multiple libraries and custom manifest names discovered without arbitrary selection");
        var missing=Path.Combine(isolated,"MissingSteam");Check(NativeSteamMaintenance.FindGameDirectories(missing,package).Count==0&&NativeSteamMaintenance.FindGameDirectories(missing,Path.Combine(game,"Package")).SequenceEqual(new[]{game}),"missing Steam and nearby package handled independently");var cn=Path.Combine(isolated,"CN");Text(Path.Combine(cn,"AstralParty_CN.exe"),"inert");Check(NativeSteamMaintenance.ResolveIntDirectory(cn)==null,"CN-only game never accepted as INT");
        var unicode=Path.Combine(isolated,"가 library [2]");var unicodeGame=Path.Combine(unicode,"steamapps","common","나 game","8vJXnINT");Text(Path.Combine(unicodeGame,"AstralParty_INT.exe"),"inert");foreach(var bom in new[]{false,true}){var encoding=new UTF8Encoding(bom);File.WriteAllText(vdf,"\"path\" \""+unicode.Replace("\\","\\\\")+"\"",encoding);File.WriteAllText(Path.Combine(unicode,"steamapps/appmanifest_2622000.acf"),"\"installdir\" \"나 game\"",encoding);found=NativeSteamMaintenance.FindGameDirectories(steam,package);Check(found.Count==2&&found.Contains(unicodeGame),"UTF8 Unicode discovery retains optional BOM "+bom);}
        var facts=new List<string>();using(var held=File.Open(vdf,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){found=NativeSteamMaintenance.FindGameDirectories(steam,package,facts.Add);Check(found.SequenceEqual(new[]{game}),"sharing-denied library metadata preserves independent valid installation");}Check(facts.Count==1&&facts[0]=="NativeSteamDiscovery kind=LibraryFolders;exception=IOException;native="+unchecked((int)0x80070020)&&!facts[0].Contains(root)&&!facts[0].Contains("installdir"),"native discovery records exact bounded sharing failure without private metadata");
    }
}
