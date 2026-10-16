using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Updating;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class LaunchFixtureFolders
{
    internal static string Root="";internal static bool BlockLocal,BlockTemp;
    internal static string GetFolderPath(Environment.SpecialFolder folder) => folder==Environment.SpecialFolder.UserProfile?Path.Combine(Root,"private-user"):Path.Combine(Root,BlockLocal?"blocked-local":"local");
    internal static string GetTempPath() => Path.Combine(Root,BlockTemp?"blocked-temp":"temp");
}
internal static class NativeLaunchFailureFixture
{
    private static int Checks;private static string Root="";
    private static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);Checks++;Console.WriteLine("PASS: "+name);}
    private static void Put(string path,string text){path=path.Replace('/','\\');if(!WindowsPath.Absolute(path).StartsWith(Root+"\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Fixture escaped artifacts.");Directory.CreateDirectory(WindowsPath.ForIo(WindowsPath.Parent(path)));File.WriteAllText(WindowsPath.ForIo(path),text,new UTF8Encoding(false));}
    private static string Zip(string game,string logs,string label)
    {
        var bundle=DiagnosticBundle.Collect(game,logs,"","",Path.Combine(Root,label,"hub"),Path.Combine(Root,label,"fallback"),"4.8","1.3.0");
        using(var zip=ZipFile.OpenRead(WindowsPath.ForIo(bundle.ArchivePath)))return string.Join("\n",zip.Entries.Select(entry=>{using(var reader=new StreamReader(entry.Open()))return reader.ReadToEnd();}));
    }
    private static int Main(string[] args)
    {
        try
        {
            Root=WindowsPath.Absolute(args.Single());if(!Root.Contains("native-launch-failure-")||Directory.Exists(Root))throw new InvalidOperationException("Fresh isolated native launch fixture root required.");Directory.CreateDirectory(Root);LaunchFixtureFolders.Root=Root;
            var game=Path.Combine(Root,"game");foreach(var name in new[]{"AstralParty_INT.exe",UpdateTrust.PluginPath,"BetterAstralParty.compatibility.json","BetterAstralParty-Launcher.exe"})Put(Path.Combine(game,name),"inert never execute");Directory.CreateDirectory(Path.Combine(game,"AstralParty_INT_Data"));
            var context=SteamLauncher.CaptureLaunchContext(game);Check(context.Contains("LaunchRoot: source=LauncherDirectory; state=Directory; hresult=0x00000000")&&Regex.Matches(context,"state=File; hresult=0x00000000").Count==4&&context.Contains("kind=Plugin; state=File; hresult=0x00000000"),"actual launcher observes selected root and four fixed native files");
            var plugin=Path.Combine(game,UpdateTrust.PluginPath.Replace('/','\\'));File.Delete(plugin);var pluginContext=SteamLauncher.CaptureLaunchContext(game);Check(pluginContext.Contains("kind=Plugin; state=Missing; hresult=0x80070002"),"native slash-delimited plugin inventory reports exact missing file");Put(plugin,"inert never execute");
            File.Delete(Path.Combine(game,"AstralParty_INT.exe"));context=SteamLauncher.CaptureLaunchContext(game);Check(context.Contains("kind=GameExecutable; state=Missing; hresult=0x80070002"),"missing game is observed, not guessed antivirus denial");
            File.Delete(Path.Combine(game,"BetterAstralParty.compatibility.json"));Directory.CreateDirectory(Path.Combine(game,"BetterAstralParty.compatibility.json"));Check(SteamLauncher.CaptureLaunchContext(game).Contains("kind=CompatibilityManifest; state=Directory; hresult=0x00000000"),"wrong native manifest type distinct from missing");
            var stale=Path.Combine(Root,"stale");Directory.CreateDirectory(stale);Check(Regex.Matches(SteamLauncher.CaptureLaunchContext(stale),"state=Missing;").Count==4,"stale selected root retains four missing observations");Check(SteamLauncher.CaptureLaunchContext(Path.Combine(Root,"absent")).Contains("LaunchRoot: source=LauncherDirectory; state=Missing;"),"absent launcher root remains observable");Check(SteamLauncher.CaptureLaunchContext("\0").Contains("state=Invalid;"),"invalid supplied root never replaces original failure");
            var failures=new StringBuilder();foreach(var phase in new[]{"SteamContext","UpdateGate","Compatibility","SharedLoader","GameStart","GameExit","HelperInstall"}){var text=SteamLauncher.LaunchFailure(phase,new FileNotFoundException("Inert missing file"));Check(text.Contains("LaunchFailure: schema=1; phase="+phase+"; errorType=System.IO.FileNotFoundException; hresult=0x80070002"),"native failure exact phase and code "+phase);failures.Append(text);}
            var profile=LaunchFixtureFolders.GetFolderPath(Environment.SpecialFolder.UserProfile);var saved=SteamLauncher.SaveFailure(context+pluginContext+failures+"Fixture profile: "+profile)!;var hash=UpdateTrust.Hash(File.ReadAllBytes(saved));var textLog=File.ReadAllText(saved);Check(Regex.IsMatch(Path.GetFileName(saved),"\\ALaunch-[a-f0-9]{32}\\.log\\z")&&textLog.Contains("BAP-LAUNCH-FAILED")&&textLog.Contains("%USERPROFILE%")&&!textLog.Contains(profile),"actual native failure writer creates scoped unique masked log");
            var second=SteamLauncher.SaveFailure("second inert cause");Check(second!=saved&&File.Exists(saved)&&UpdateTrust.Hash(File.ReadAllBytes(saved))==hash,"subsequent failures never replace previous evidence");var logs=Path.GetDirectoryName(saved)!;var all=Zip(game,logs,"written");Check(all.Contains("kind=GameExecutable; state=Missing;")&&all.Contains("kind=Plugin; state=Missing;")&&all.Contains("phase=GameStart; errorType=System.IO.FileNotFoundException; hresult=0x80070002")&&!all.Contains(Root),"actual source to ZIP retains cause without private root");
            var observed=SteamLauncher.SaveFailure(SteamLauncher.LaunchFailure("Compatibility",new IOException("Inert foreign-loader observation unavailable")),true)!;
            Check(File.ReadAllText(observed).Contains("BAP-LAUNCH-OBSERVATION")&&!File.ReadAllText(observed).Contains("BAP-LAUNCH-FAILED"),"Successful launch observation is not recorded as a failed launch");
            all=Zip(game,logs,"observation");Check(all.Contains("BAP-LAUNCH-OBSERVATION")&&all.Contains("phase=Compatibility; errorType=System.IO.IOException; hresult=0x80131620"),"Advisory source-to-ZIP retains distinct outcome and original cause");
            var backupRelative=SharedLoaderConfiguration.BackupPrefix+new string('d',32)+".bak";var backup=Path.Combine(game,backupRelative.Replace('/','\\'));
            Put(backup,"inert original INI backup retained; not automatically uploaded");var backupHash=UpdateTrust.Hash(File.ReadAllBytes(WindowsPath.ForIo(backup)));
            var uncertain=new SharedLoaderRecoveryException(backupRelative,new IOException("inert loader write interrupted"),new UnauthorizedAccessException("inert rollback denied"));
            var recoveryLog=SteamLauncher.SaveFailure(SteamLauncher.LaunchFailure("SharedLoader",uncertain))!;
            Check(File.ReadAllText(recoveryLog).Contains("BAP-LAUNCH-FAILED")&&!File.ReadAllText(recoveryLog).Contains("BAP-LAUNCH-OBSERVATION"),"Uncertain own write is a failure, never an advisory");
            all=Zip(game,logs,"loader-recovery");Check(all.Contains("phase=SharedLoader;")&&all.Contains("SharedLoaderRecoveryException")&&all.Contains(backupRelative)&&all.Contains("inert loader write interrupted")&&all.Contains("inert rollback denied"),"Typed uncertain write, both causes and exact backup reference survive source-to-ZIP");
            SteamLauncher.SaveFailure(SteamLauncher.LaunchFailure("SharedLoader",new SharedLoaderRecoveryException(backupRelative,uncertain,new IOException("inert cleanup failed"))));
            all=Zip(game,logs,"loader-recovery-cleanup");Check(all.Contains("inert loader write interrupted")&&all.Contains("inert rollback denied")&&all.Contains("inert cleanup failed")&&all.Contains(backupRelative),"Nested cleanup preserves write, rollback and cleanup causes in the diagnostic ZIP");
            Check(File.Exists(WindowsPath.ForIo(backup))&&UpdateTrust.Hash(File.ReadAllBytes(WindowsPath.ForIo(backup)))==backupHash&&!all.Contains("inert original INI backup retained"),"Diagnostic collection preserves recovery evidence without uploading foreign configuration");
            Put(Path.Combine(logs,"Launch-"+new string('b',32)+".log"),"BAP-LAUNCH-FAILED\r\nLaunchFile: kind=GameExecutable; state=Missing; hresult=0x80070002; token=FORBIDDEN_SECRET\r\nLaunchFailure: schema=1; phase=GuessedDefenderCause; errorType=Unknown; hresult=0x00000000\r\n");all=Zip(game,logs,"malformed");Check(!all.Contains("FORBIDDEN_SECRET")&&!all.Contains("GuessedDefenderCause")&&all.Contains("invalid-failure-context"),"unknown failure fields and invented causes are dropped");
            Put(Path.Combine(Root,"blocked-local"),"ordinary file");LaunchFixtureFolders.BlockLocal=true;var fallback=SteamLauncher.SaveFailure("fallback inert cause");Check(fallback!=null&&fallback.StartsWith(Path.Combine(Root,"temp"),StringComparison.OrdinalIgnoreCase),"native launch log falls back on inaccessible primary path");Put(Path.Combine(Root,"blocked-temp"),"ordinary file");LaunchFixtureFolders.BlockTemp=true;Check(SteamLauncher.SaveFailure("original error must survive")==null,"complete diagnostic write failure does not mask original error");
            File.WriteAllText(Path.Combine(Root,"verification.json"),"{\"passed\":true,\"checks\":"+Checks+",\"actualNativeFailureMethods\":true,\"folderBrokerOnly\":true,\"realGameOrLogsRead\":false}");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
