using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void NativeLauncherMatrix(DevContext context,string stage,Action<string> pin)
    {
        var root=stage+Path.DirectorySeparatorChar;const string version="1.3.0";var boundary=Path.Combine(stage,"FixtureBoundary.cs");
        File.WriteAllText(boundary,"using System.Reflection;[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.8\")][assembly:AssemblyProduct(\"BetterAstralParty\")][assembly:AssemblyTitle(\"BetterAstralParty native launch host\")][assembly:AssemblyInformationalVersion(\""+version+"\")]internal static class FixtureBoundary { internal const string Root=@\""+root+"\";internal const string Version=\""+version+"\"; }",new UTF8Encoding(false));
        var sourcePath=Path.Combine(context.Root,"tools/SteamLauncher.cs");pin(sourcePath);var source=File.ReadAllText(sourcePath);const string create="new LauncherRecoveryClient(root)";Check(source.Split(create,StringSplitOptions.None).Length==2,"one explicit helper-completion dependency injection point");
        var scoped=source.Replace(create,"new LauncherRecoveryClient(root,new LaunchFixtureNoNetwork(),fixtureCredential:()=>throw new InvalidOperationException(\"Fixture credentials forbidden\"))",StringComparison.Ordinal).Replace("Environment.GetFolderPath(","LaunchFixtureFolders.GetFolderPath(",StringComparison.Ordinal).Replace("Path.GetTempPath()","LaunchFixtureFolders.GetTempPath()",StringComparison.Ordinal);
        var derived=Path.Combine(stage,"SteamLauncher.derived.cs");File.WriteAllText(derived,scoped,new UTF8Encoding(false));
        var globals=Path.Combine(stage,"Globals.cs");File.WriteAllText(globals,"global using System;global using System.IO;global using System.Collections.Generic;global using System.Threading;global using System.Threading.Tasks;internal static class LaunchFixtureFolders { internal static string GetFolderPath(Environment.SpecialFolder folder)=>Path.Combine(FixtureBoundary.Root,folder==Environment.SpecialFolder.UserProfile?\"profile\":\"local\");internal static string GetTempPath()=>Path.Combine(FixtureBoundary.Root,\"temp\"); }internal sealed class LaunchFixtureNoNetwork:System.Net.Http.HttpMessageHandler {protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)=>throw new InvalidOperationException(\"Fixture network forbidden\");}",new UTF8Encoding(false));
        var collectorPath=Path.Combine(context.Root,"diagnostics/DiagnosticBundle.cs");pin(collectorPath);var collector=Path.Combine(stage,"Collector.derived.cs");File.WriteAllText(collector,"#nullable disable\n"+File.ReadAllText(collectorPath),new UTF8Encoding(false));
        using var key=RSA.Create(3072);var publicKey=key.ExportParameters(false);var pins=Path.Combine(stage,"FixturePublicPins.cs");File.WriteAllText(pins,"namespace BetterAstralParty.Updating {internal static class ProductionUpdateKeys {internal static TrustedUpdateKey[] Read()=>new[]{new TrustedUpdateKey(\"native-launch-fixture\",new System.Security.Cryptography.RSAParameters {Modulus=System.Convert.FromBase64String(\""+Convert.ToBase64String(publicKey.Modulus!)+"\"),Exponent=new byte[]{1,0,1}})};}}",new UTF8Encoding(false));
        var sources=new[]{derived,globals,collector,pins,boundary,"tools/InstallHelperBootstrap.cs","tools/GameCompatibility.cs","src/ReleaseHttpClient.cs","src/WindowsReleaseCredentialStore.cs","diagnostics/MinimalDiagnosticProjection.cs","focus-tests/native-launcher/FixtureEntry.cs"}
            .Concat(Directory.EnumerateFiles(Path.Combine(context.Root,"tools"),"Native*.cs").Select(path=>Path.GetRelativePath(context.Root,path)))
            .Concat(Directory.EnumerateFiles(Path.Combine(context.Root,"tools"),"LauncherRecovery*.cs").Select(path=>Path.GetRelativePath(context.Root,path)))
            .Concat(UpdateSources(context).Where(path=>!path.EndsWith("ProductionUpdateKeys.cs",StringComparison.Ordinal))).ToArray();
        var launcher=CompileFrameworkOnly(context,Path.Combine(stage,"launcher"),"FixtureEntry",sources,pin,target:"winexe",defines:"NETFRAMEWORK,BAP_SAFETY_FIXTURE,BAP_LOADER_FIXTURE",output:Path.Combine(stage,"compiled/BetterAstralParty-Launcher.exe"));
        var game=CompileFrameworkOnly(context,Path.Combine(stage,"game"),"InertGame",["focus-tests/native-launcher/InertGame.cs",boundary],pin,target:"winexe",defines:"",output:Path.Combine(stage,"compiled/AstralParty_INT.exe"));
        var helper=new byte[512];helper[0]=77;helper[1]=90;BitConverter.GetBytes(128).CopyTo(helper,60);helper[128]=80;helper[129]=69;BitConverter.GetBytes((ushort)0x8664).CopyTo(helper,132);var descriptor=HelperDescriptor.Prepare(version,helper,4);var signature=new DetachedSignature("native-launch-fixture",key.SignData(descriptor,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)).Bytes();
        Process Start(string image,string[]? arguments=null,bool steamContext=true)
        {
            Check(Path.GetFullPath(image).StartsWith(root,StringComparison.OrdinalIgnoreCase),"only inert launcher/game fixture processes start");var info=new ProcessStartInfo(image){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(image)!};foreach(var argument in arguments??[])info.ArgumentList.Add(argument);
            foreach(var name in new[]{"BAP_UPDATER_LAUNCHER_PID","BAP_UPDATER_LAUNCHER_CREATION","BAP_UPDATER_LAUNCHER_SESSION","BAP_COMPATIBILITY_CHECKED"})info.Environment.Remove(name);foreach(var pair in new[]{("SteamAppId","fixture-app"),("SteamGameId","fixture-game"),("SteamOverlayGameId","fixture-overlay"),("PSModulePath","fixture-parent-module")})info.Environment[pair.Item1]=pair.Item2;
            if(!steamContext)foreach(var name in new[]{"SteamAppId","SteamGameId","SteamOverlayGameId"})info.Environment.Remove(name);
            return Process.Start(info)??throw new IOException("Inert fixture process did not start.");
        }
        int Wait(Process process){using(process){if(!process.WaitForExit(30000))throw new TimeoutException("Inert fixture timed out; no unrelated termination attempted.");return process.ExitCode;}}
        void WaitFile(string path){var deadline=DateTime.UtcNow.AddSeconds(10);while(!File.Exists(path)){if(DateTime.UtcNow>=deadline)throw new TimeoutException("Inert observation missing: "+Path.GetFileName(path));Thread.Sleep(20);}}
        string New(string name,string behavior="ok"){var directory=Path.Combine(stage,name);Directory.CreateDirectory(directory);File.Copy(launcher,Path.Combine(directory,"BetterAstralParty-Launcher.exe"));File.Copy(game,Path.Combine(directory,"AstralParty_INT.exe"));File.WriteAllBytes(Path.Combine(directory,"fixture-helper.image"),helper);File.WriteAllBytes(Path.Combine(directory,"fixture-helper.descriptor"),descriptor);File.WriteAllBytes(Path.Combine(directory,"fixture-helper.signature"),signature);File.WriteAllText(Path.Combine(directory,"checker-behavior"),behavior);File.WriteAllText(Path.Combine(directory,"doorstop_config.ini"),"[General]\r\nenabled=false\r\ncustom=fixture\r\n");Check(Wait(Start(Path.Combine(directory,"BetterAstralParty-Launcher.exe"),["--setup"]))==0,"authenticated inert launcher setup "+name);return directory;}
        string Details(string directory){var lines=File.ReadAllLines(Path.Combine(directory,"host-result"));Check(lines[2]=="ParentUnchanged","launcher preserves parent process environment");return Encoding.UTF8.GetString(Convert.FromBase64String(lines[1]));}
        var metadata=FileVersionInfo.GetVersionInfo(launcher);Check(metadata.ProductName=="BetterAstralParty"&&metadata.FileDescription=="BetterAstralParty native launch host"&&metadata.ProductVersion==version,"native windowless executable identity and version");
        var current=New("resources");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe"),["--resources"]))==0&&File.ReadAllText(Path.Combine(current,"resource-result"))=="NoLegacyHelperOrEmbeddedPE","no embedded legacy helper or PE resources");
        current=New("success");File.WriteAllText(Path.Combine(current,"hold"),"inert wait");var host=Start(Path.Combine(current,"BetterAstralParty-Launcher.exe"));try
        {
            WaitFile(Path.Combine(current,"game-ready"));Check(!host.HasExited,"native host retained through inert game lifetime");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe"),["--probe"]))==0&&File.ReadAllText(Path.Combine(current,"probe-result"))=="FileBusy","same held launch lease prevents competing maintenance");
            var observation=File.ReadAllText(Path.Combine(current,"game-ready"));var identity=File.ReadAllLines(Path.Combine(current,"host-identity"));foreach(var name in new[]{"SteamAppId","SteamGameId","SteamOverlayGameId"})Check(observation.Contains(name+"=2622000",StringComparison.Ordinal),"game-only native Steam identity "+name);
            Check(observation.Contains("Args=--doorstop-enabled|true|--bap-compatibility-checked",StringComparison.Ordinal)&&observation.Contains("BAP_COMPATIBILITY_CHECKED=1",StringComparison.Ordinal),"one-shot native launch flags retained");foreach(var pair in new[]{("BAP_UPDATER_LAUNCHER_PID",identity[0]),("BAP_UPDATER_LAUNCHER_CREATION",identity[1]),("BAP_UPDATER_LAUNCHER_SESSION",identity[2])})Check(observation.Contains(pair.Item1+"="+pair.Item2,StringComparison.Ordinal),"exact native parent identity "+pair.Item1);
            Check(identity[3]=="0"&&!File.Exists(Path.Combine(current,"checker-observation"))&&observation.Contains("PSModulePath=fixture-parent-module",StringComparison.Ordinal),"windowless in-process compatibility, no interpreter and parent module value retained");
            Check(File.ReadAllText(Path.Combine(current,"doorstop_config.ini"))=="[General]\r\nenabled=true \r\ncustom=fixture\r\n","successful compatibility enables only exact shared loader bytes");var backups=Directory.GetFiles(Path.Combine(current,"BepInEx/config"),"BetterAstralParty.loader-before-*.bak");Check(backups.Length==1&&File.ReadAllText(backups[0])=="[General]\r\nenabled=false\r\ncustom=fixture\r\n","exact original shared loader backup retained");
        }
        finally{File.WriteAllText(Path.Combine(current,"release"),"finish inert game");}
        Check(Wait(host)==0,"native success exit retained");Details(current);Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe"),["--probe"]))==0&&File.ReadAllText(Path.Combine(current,"probe-result"))=="Available","launch lease released after game exit");
        current=New("missing-steam-context");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe"),steamContext:false))==0&&Details(current)==""&&File.ReadAllText(Path.Combine(current,"game-ready")).Contains("SteamGameId=2622000",StringComparison.Ordinal),"missing inherited Steam context is not a launch gate; native child identity is still supplied");
        foreach(var behavior in new[]{"reject","parser"}) {
            current=New(behavior,behavior);
            var foreign=File.ReadAllBytes(Path.Combine(current,"winhttp.dll"));
            Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==0&&Details(current)==""&&File.Exists(Path.Combine(current,"game-ready")),"foreign loader or compatibility-parser observation still starts inert game "+behavior);
            Check(context.Hash(Path.Combine(current,"winhttp.dll"))==UpdateTrust.Hash(foreign),"advisory compatibility never replaces foreign shared loader "+behavior);
            Check(File.Exists(Path.Combine(current,"BetterAstralParty-Compatibility.log")),"advisory compatibility reason is recorded "+behavior);
        }
        foreach(var behavior in new[]{"malformed","missing","readonly","busy"}) {
            current=New("foreign-ini-"+behavior);var ini=Path.Combine(current,"doorstop_config.ini");
            if(behavior=="malformed")File.WriteAllText(ini,"[General]\r\nenabled=other-loader-value\r\ncustom=foreign\r\n",new UTF8Encoding(false));
            if(behavior=="missing")File.Delete(ini);
            var original=File.Exists(ini)?File.ReadAllBytes(ini):null;
            var receipt=context.Hash(Path.Combine(current,InstallReceipt.Path));
            var owned=InstallReceipt.Owned.ToDictionary(path=>path,path=>context.Hash(Path.Combine(current,path.Replace('/',Path.DirectorySeparatorChar))));
            var unrelated=Path.Combine(current,"BepInEx/plugins/OtherMod/OtherMod.dll");Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);File.WriteAllText(unrelated,"inert unrelated mod");
            FileStream? retained=null;
            try {
                if(behavior=="readonly")File.SetAttributes(ini,File.GetAttributes(ini)|FileAttributes.ReadOnly);
                if(behavior=="busy")retained=new FileStream(ini,FileMode.Open,FileAccess.Read,FileShare.Read);
                Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==0&&Details(current)==""&&File.Exists(Path.Combine(current,"game-ready")),"foreign INI "+behavior+" skips optional mutation and still starts inert game");
                Check(original==null?!File.Exists(ini):File.ReadAllBytes(ini).SequenceEqual(original),"foreign INI bytes or absence preserved "+behavior);
                Check(!Directory.GetFiles(Path.Combine(current,"BepInEx/config"),"BetterAstralParty.loader-before-*.bak").Any(),"skipped foreign INI creates no guessed loader backup "+behavior);
                Check(File.ReadAllText(Path.Combine(current,"BetterAstralParty-Compatibility.log")).Contains("configuration left unchanged",StringComparison.Ordinal),"skipped foreign INI is explicitly diagnosed "+behavior);
                Check(context.Hash(Path.Combine(current,InstallReceipt.Path))==receipt&&owned.All(row=>context.Hash(Path.Combine(current,row.Key.Replace('/',Path.DirectorySeparatorChar)))==row.Value)
                    &&File.ReadAllText(unrelated)=="inert unrelated mod","advisory foreign INI preserves authenticated payload/receipt and other mod "+behavior);
            } finally {retained?.Dispose();if(behavior=="readonly")File.SetAttributes(ini,File.GetAttributes(ini)&~FileAttributes.ReadOnly);}
        }
        foreach(var mode in new[]{"before-write","verified-rollback","failed-rollback","unverified-rollback","failed-rollback-cleanup"}) {
            current=New("loader-"+mode);var ini=Path.Combine(current,"doorstop_config.ini");var original=File.ReadAllBytes(ini);
            var receipt=context.Hash(Path.Combine(current,InstallReceipt.Path));var owned=InstallReceipt.Owned.ToDictionary(path=>path,path=>context.Hash(Path.Combine(current,path.Replace('/',Path.DirectorySeparatorChar))));
            File.WriteAllText(Path.Combine(current,"loader-write-failure"),mode);var code=Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")));var details=Details(current);
            var uncertain=mode!="before-write"&&mode!="verified-rollback";
            Check(code==(uncertain?1:0)&&File.Exists(Path.Combine(current,"game-ready"))==!uncertain,"Only uncertain own INI mutation stops inert game: "+mode);
            if(uncertain)Check(details.Contains("phase=SharedLoader",StringComparison.Ordinal)&&details.Contains("SharedLoaderRecoveryException",StringComparison.Ordinal)&&details.Contains("Restore doorstop_config.ini",StringComparison.Ordinal),"Uncertain mutation retains actionable typed recovery failure: "+mode);
            else Check(details=="","Prewrite/verified rollback stays advisory: "+mode);
            var backups=Directory.GetFiles(Path.Combine(current,"BepInEx/config"),"BetterAstralParty.loader-before-*.bak");Check(backups.Length==1&&File.ReadAllBytes(backups[0]).SequenceEqual(original),"Original INI backup is preserved exactly: "+mode);
            Check(mode.StartsWith("failed-rollback",StringComparison.Ordinal)?!File.ReadAllBytes(ini).SequenceEqual(original):File.ReadAllBytes(ini).SequenceEqual(original),"Mutation/restoration outcome is truthful: "+mode);
            Check(context.Hash(Path.Combine(current,InstallReceipt.Path))==receipt&&owned.All(row=>context.Hash(Path.Combine(current,row.Key.Replace('/',Path.DirectorySeparatorChar)))==row.Value),"Loader write recovery preserves authenticated payload and receipt: "+mode);
        }
        current=New("changed-plugin");File.AppendAllText(Path.Combine(current,UpdateTrust.PluginPath),"changed inert owned bytes");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==1&&Details(current).Contains("phase=UpdateGate",StringComparison.Ordinal)&&!File.Exists(Path.Combine(current,"game-ready")),"changed executable plugin refused before game");
        foreach(var unused in new[]{"BetterAstralParty-Mod.cmd","AstralParty-Vanilla.cmd","BetterAstralParty.compatibility.json",HelperDescriptor.Image,HelperDelivery.Receipt})
        foreach(var missing in new[]{false,true}) {
            current=New("unused-"+Path.GetFileName(unused)+"-"+missing);var path=Path.Combine(current,unused.Replace('/',Path.DirectorySeparatorChar));
            if(missing)File.Delete(path);else File.AppendAllText(path,"changed unused input");var original=File.Exists(path)?File.ReadAllBytes(path):null;
            Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==0&&Details(current)==""&&File.Exists(Path.Combine(current,"game-ready")),"unused launch input does not gate game: "+unused+";missing="+missing);
            Check(original==null?!File.Exists(path):File.ReadAllBytes(path).SequenceEqual(original),"launch preserves unused input or absence: "+unused);
        }
        current=New("warning","warning");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==0&&File.Exists(Path.Combine(current,"game-ready")),"UnityFS advisory warning preserves native launch");
        current=New("game-drift","drift");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==0&&File.Exists(Path.Combine(current,"game-ready")),"game and newer catalog drift remain advisory for native launch");
        current=New("crash");File.WriteAllText(Path.Combine(current,"game-exit-code"),"7");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==7&&Details(current).Contains("phase=GameExit",StringComparison.Ordinal),"inert game exit code and phase preserved");
        current=New("startfailure");File.WriteAllText(Path.Combine(current,"AstralParty_INT.exe"),"inert invalid image");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe"),["--refresh-compatibility"]))==0,"invalid inert image has exact refreshed fixture compatibility");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==1&&Details(current).Contains("phase=GameStart",StringComparison.Ordinal),"invalid image attributed to actual native GameStart");
        var busyRoot=Path.Combine(stage,"busy-sibling");Directory.CreateDirectory(busyRoot);File.Copy(game,Path.Combine(busyRoot,"AstralParty_INT.exe"));File.WriteAllText(Path.Combine(busyRoot,"hold"),"inert wait");var busy=Start(Path.Combine(busyRoot,"AstralParty_INT.exe"));try{WaitFile(Path.Combine(busyRoot,"game-ready"));current=New("busy");Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==0&&File.Exists(Path.Combine(current,"game-ready")),"running different-root game does not gate this installation");}finally{File.WriteAllText(Path.Combine(busyRoot,"release"),"finish inert sibling");}Check(Wait(busy)==0,"only explicitly started inert sibling exits normally");
        current=New("busy-exact");File.WriteAllText(Path.Combine(current,"hold"),"inert wait");busy=Start(Path.Combine(current,"AstralParty_INT.exe"));try{WaitFile(Path.Combine(current,"game-ready"));Check(Wait(Start(Path.Combine(current,"BetterAstralParty-Launcher.exe")))==1&&Details(current).Contains("phase=SteamContext",StringComparison.Ordinal),"running same-root game still prevents a competing launch");}finally{File.WriteAllText(Path.Combine(current,"release"),"finish exact inert game");}Check(Wait(busy)==0,"exact inert process exits normally");
        context.WriteJsonCreateNew(Path.Combine(stage,"native-launcher-port.json"),new{passed=true,actualProductionRunBody=true,fixtureDependencyInjection="Folder broker plus no-network handler and forbidden credential callback",literalSourceSha256=context.Hash(sourcePath),derivedSourceSha256=context.Hash(derived),realSteamOrGameOrKeysUsed=false,retired=new[]{"PowerShell shim launch rejection cases; no active interpreter route remains"}});
    }
}
