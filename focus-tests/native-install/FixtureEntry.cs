#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
internal static class FixtureEntry
{
    private static int checks, sequence;
    private static string root = "";
    private static void Check(bool value,string reason) { if(!value)throw new InvalidOperationException(reason+"; events="+string.Join(",",FixtureControl.Events));checks++; }
    private static void Reject(Action action,string reason) { try{action();}catch{checks++;return;}throw new InvalidOperationException(reason); }
    private static bool Before(string first,string second) { return FixtureControl.Events.IndexOf(first)>=0&&FixtureControl.Events.IndexOf(first)<FixtureControl.Events.IndexOf(second); }
    private static void Write(string rootPath,string relative,byte[] bytes) { var path=Path.Combine(rootPath,relative.Replace('/', '\\'));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes); }
    private static NativeMaintenanceBundle Case(bool existing=false,bool legacy=false) {
        FixtureControl.Reset();var bundle=new NativeMaintenanceBundle(Path.Combine(root,(sequence++).ToString()));
        if(existing||legacy) {
            var owned=NativeMaintenanceBundle.OwnedSources.Keys.Concat(legacy?new[]{OrdinaryInstallRecovery.LegacyLauncher}:new string[0]).ToArray();
            foreach(var path in owned)Write(bundle.GameRoot,path,path==UpdateTrust.PluginPath?Encoding.UTF8.GetBytes(legacy?"1.3.2":"1.4.0"):Encoding.UTF8.GetBytes("prior:"+path));
            Write(bundle.GameRoot,"doorstop_config.ini",Encoding.UTF8.GetBytes("[General]\r\nenabled=false\r\ncustom=preserved\r\n"));
            Write(bundle.GameRoot,"BepInEx/config/BepInEx.cfg",Encoding.UTF8.GetBytes("[Logging]\r\nUnityLogListening = false\r\ncustom=preserved\r\n"));
            var files=owned.Select(path=>new {path,sha256=bundle.TargetHash(path)}).ToArray();
            Write(bundle.GameRoot,InstallReceipt.Path,Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {schema=1,baseline=legacy?"1.3.0":"1.4.0",owner="kr.betterastralparty.mod",loaderBefore=true,channelState="fixture",files})));
            if(legacy)Write(bundle.GameRoot,NativeInstall.LegacyHighWater,Encoding.UTF8.GetBytes("1.3.2"));
        }
        return bundle;
    }
    private static void Run(NativeMaintenanceBundle bundle,bool update=false,bool preview=false) { NativeInstall.Run(bundle,update,update?null:Path.Combine(root,"Steam"),update?0U:11U,prompt=>"y",message=>{},checkOnly:preview); }
    private static int Main(string[] args) {
        try { root=Path.GetFullPath(args[0]);if(!root.Contains("native-install-"))throw new InvalidOperationException("Isolated root required.");Pure();Flow();CompatibilityFailures();Console.WriteLine("Native install: "+checks+" isolated assertions passed; no real install, Steam, registry, process termination or signing.");return 0; }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static void Pure() {
        var original=Encoding.UTF8.GetBytes("[Logging]\r\n  UnityLogListening = TRUE \r\nother=true\r\n");
        Check(Encoding.UTF8.GetString(NativeInstall.DisableUnityListening(original))=="[Logging]\r\n  UnityLogListening = false \r\nother=true\r\n","only existing documented configuration field changed");
        var unchanged=new byte[]{239,187,191}.Concat(Encoding.UTF8.GetBytes("UnityLogListening = false\ncustom=true\n")).ToArray();
        Check(object.ReferenceEquals(NativeInstall.DisableUnityListening(unchanged),unchanged),"unchanged configuration preserves exact bytes and BOM");
        var opaque = new byte[]{255,128}.Concat(Encoding.ASCII.GetBytes("\nUnityLogListening = true\ncustom=")).Concat(new byte[]{233,250}).ToArray();
        Check(NativeInstall.DisableUnityListening(opaque).SequenceEqual(new byte[]{255,128}.Concat(Encoding.ASCII.GetBytes("\nUnityLogListening = false\ncustom=")).Concat(new byte[]{233,250})),"ANSI or opaque unrelated bytes survive setting change exactly");
        var unicode = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("UnityLogListening = true\r\n사용자 설정=보존\r\n")).ToArray();
        Check(NativeInstall.DisableUnityListening(unicode).SequenceEqual(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("UnityLogListening = false\r\n사용자 설정=보존\r\n"))),"UTF16 BOM and unrelated Korean configuration bytes preserved");
        Check(Encoding.UTF8.GetString(NativeInstall.DisableUnityListening(null))=="[Logging]\r\nUnityLogListening = false\r\n","absent configuration uses existing baseline");
        Check(NativeInstall.LoaderBaseline(null,null,true,true)==false,"fresh loader baseline false while installed source remains ON");
        Check(NativeInstall.LoaderBaseline(null,Encoding.UTF8.GetBytes("enabled=false\n"),false,true)==false,"existing disabled loader baseline recorded");
        Check(NativeInstall.LoaderBaseline(null,Encoding.UTF8.GetBytes("enabled=true\nenabled=false\n"),false,true)==null,"ambiguous loader baseline remains unknown");
        var old=new Dictionary<string,string>{{"owned","A"}};var expected=new Dictionary<string,string>{{"owned","B"}};var observed=new Dictionary<string,string?>{{"owned","A"},{InstallReceipt.Path,"R"}};
        Check(NativeInstall.TransitionMode("1.4.1","1.4.0",null,old,expected,observed)=="Upgrade","version skip upgrade");
        Check(NativeInstall.TransitionMode("1.4.1","1.4.1",null,expected,expected,new Dictionary<string,string?>{{"owned","B"},{InstallReceipt.Path,"R"}})=="SameBuild","same immutable build repair");
        Reject(()=>NativeInstall.TransitionMode("1.4.0","1.4.1",null,old,expected,observed),"installed downgrade rejected");
        Reject(()=>NativeInstall.TransitionMode("1.4.1","1.4.0","1.5.0",old,expected,observed),"retained global high-water blocks downgrade");
        Reject(()=>NativeInstall.TransitionMode("1.4.0","1.4.0",null,old,expected,observed),"different same-version build rejected");
        Reject(()=>NativeInstall.TransitionMode("1.4.1","1.4.0",null,old,expected,new Dictionary<string,string?>{{"owned","CUSTOM"},{InstallReceipt.Path,"R"}}),"unverified modified installation preserved");
    }
    private static void Flow() {
        var fresh=Case();FixtureControl.NeedsShortcut=true;Run(fresh);
        Check(Before("inputs-validate","ordinary-lock")&&Before("ordinary-lock","game-stop")&&Before("game-stop","update-check")&&Before("update-check","state-observe"),"ordinary recovery and process preflight precede owned observation");
        Check(Before("steam-stop","guard")&&Before("guard","ordinary-start")&&Before("ordinary-start","shortcut-register")&&Before("shortcut-register","ordinary-complete"),"Steam stop and the maintenance guard precede ordinary target mutation");
        Check(Before("ordinary-complete","guard-release")&&Before("guard-release","ordinary-unlock")&&Before("ordinary-unlock","steam-resume"),"guard and locks released after completion; Steam restarted last");
        Check(fresh.Expected.All(row=>fresh.TargetHash(row.Key)==row.Value),"fresh complete owned payload fingerprints");
        Check(Encoding.UTF8.GetString(fresh.ReadTarget("doorstop_config.ini")!).Contains("enabled=true"),"approved fresh shared loader ON bytes");
        Check(InstallReceipt.Parse(fresh.ReadTarget(InstallReceipt.Path)!).LoaderBefore==false,"historical loader baseline preserved separately");
        var withoutSteam=Case();NativeInstall.Run(withoutSteam,false,null,0,prompt=>"y",message=>{});
        Check(withoutSteam.Expected.All(row=>withoutSteam.TargetHash(row.Key)==row.Value)&&!FixtureControl.Events.Any(value=>value.StartsWith("steam-stop",StringComparison.Ordinal)||value.StartsWith("shortcut",StringComparison.Ordinal)),"normal file installation survives unavailable Steam shortcut discovery");
        foreach(var shortcutFailure in new Exception[]{new InvalidDataException("FAKE_PRIVATE_SHORTCUT"),new IOException("FAKE_PRIVATE_SHORTCUT")}) {
            var optional=Case();FixtureControl.PlanFailure=shortcutFailure;FixtureControl.NeedsShortcut=true;var observations=new List<string>();
            NativeInstall.Run(optional,false,Path.Combine(root,"Steam"),11,prompt=>"y",message=>{},observation:observations.Add);
            Check(optional.Expected.All(row=>optional.TargetHash(row.Key)==row.Value)&&Encoding.UTF8.GetString(optional.ReadTarget("doorstop_config.ini")!).Contains("enabled=true"),"unreadable optional shortcut planning retains complete fresh-loader installation");
            Check(!FixtureControl.Events.Contains("steam-stop")&&!FixtureControl.Events.Contains("shortcut-register")&&observations.Count==1&&observations[0].Contains("reason=SteamShortcutPlanUnavailable; exception="+shortcutFailure.GetType().Name+"; hresult=0x")&&!observations[0].Contains("FAKE_PRIVATE"),"optional shortcut install emits bounded cause and never changes Steam");
        }
        var upgrade=Case(true);var loader=upgrade.ReadTarget("doorstop_config.ini")!;var config=upgrade.ReadTarget("BepInEx/config/BepInEx.cfg")!;Run(upgrade,true);
        Check(!FixtureControl.Events.Any(value=>value.StartsWith("steam",StringComparison.Ordinal)||value.StartsWith("shortcut",StringComparison.Ordinal)),"UpdateOnly never queries/stops/registers/restarts Steam");
        Check(upgrade.ReadTarget("doorstop_config.ini")!.SequenceEqual(loader)&&upgrade.ReadTarget("BepInEx/config/BepInEx.cfg")!.SequenceEqual(config),"existing shared loader/config bytes preserved");
        Check(InstallReceipt.Parse(upgrade.ReadTarget(InstallReceipt.Path)!).LoaderBefore==true,"first historical loader baseline retained across update");
        var sameRouting=Case(true);Run(sameRouting);Check(!FixtureControl.Events.Contains("steam-stop")&&!FixtureControl.Events.Contains("shortcut-register"),"registered exact shortcut skips Steam lifecycle");
        var pending=Case(true);FixtureControl.Pending=true;Directory.CreateDirectory(NativeMaintenance.JournalRoot(pending.GameRoot));File.WriteAllText(NativeMaintenance.JournalPath(pending.GameRoot),"inert pending");Run(pending);
        Check(Before("ordinary-recovery","state-observe"),"partial ordinary journal restored before current owned observation");
        var interrupted=Case(true);FixtureControl.PendingUpdate=true;Run(interrupted);Check(Before("update-recover","state-observe")&&!FixtureControl.PendingUpdate,"interrupted in-game update recovered before owned observation");
        var generation=Case();FixtureControl.Generation=true;Run(generation);Check(Before("generation-retire","state-observe"),"new generation retirement precedes observation");
        var failure=Case();var before=GameFiles(failure);FixtureControl.FailConfig=true;Reject(()=>Run(failure),"interrupted fresh-loader configuration write fails");
        Check(FixtureControl.Events.Contains("ordinary-rollback")&&Before("ordinary-rollback","ordinary-unlock"),"automatic rollback retains ordinary lock");SameGame(failure,before,"rollback exact fingerprints restore pre-install bytes");
        Takeover();
        var routed=Case(true);FixtureControl.NeedsShortcut=true;FixtureControl.FailComplete=true;Reject(()=>Run(routed),"post-registration journal completion failure reported");Check(!FixtureControl.Events.Contains("ordinary-rollback")&&File.Exists(NativeMaintenance.JournalPath(routed.GameRoot)),"registered routing keeps pending ordinary transaction for recovery");
        var preview=Case(true);var snapshot=preview.Expected.Keys.Concat(new[]{InstallReceipt.Path,"doorstop_config.ini"}).ToDictionary(path=>path,path=>preview.ReadTarget(path)!);Run(preview,preview:true);
        Check(!FixtureControl.Events.Any(value=>value.Contains("lock")||value.StartsWith("stage:",StringComparison.Ordinal)||value=="write-probe"||value=="game-stop"||value=="steam-stop"||value=="ordinary-start"||value=="guard"),"CheckOnly has no locks/writes/process stops or diagnostic stages");
        Check(snapshot.All(row=>preview.ReadTarget(row.Key)!.SequenceEqual(row.Value))&&!Directory.Exists(NativeMaintenance.JournalRoot(preview.GameRoot)),"CheckOnly exact install bytes and absence of journal directory preserved");
        var pendingPreview=Case(true);FixtureControl.Pending=true;Run(pendingPreview,preview:true);Check(!FixtureControl.Events.Contains("state-observe")&&!FixtureControl.Events.Contains("ordinary-recovery"),"CheckOnly pending journal previews before owned observation");
        var generationPreview=Case();FixtureControl.Generation=true;Run(generationPreview,preview:true);Check(!FixtureControl.Events.Contains("generation-retire")&&!FixtureControl.Events.Contains("state-observe"),"CheckOnly new generation retains high-water and skips inappropriate installed observation");
        var invalid=Case();Reject(()=>Run(invalid,true),"UpdateOnly fresh installation rejected");Check(!FixtureControl.Events.Contains("ordinary-start"),"invalid UpdateOnly stops before file mutation");
        invalid=Case(true);Reject(()=>NativeInstall.Run(invalid,true,Path.Combine(root,"Steam"),11,p=>"y",m=>{}),"UpdateOnly Steam options rejected");
    }
    // 1.3.x installations are taken over in place: owned launcher/helper/high-water removed in the same journal, settings kept.
    private static void Takeover()
    {
        var legacy=Case(legacy:true);var helperImage=Encoding.UTF8.GetBytes("inert 1.3.x helper");Write(legacy.GameRoot,OrdinaryInstallRecovery.LegacyHelper,helperImage);
        Write(legacy.GameRoot,OrdinaryInstallRecovery.LegacyHelperReceipt,Encoding.ASCII.GetBytes("BetterAstralParty.HelperReceipt/v1\nroot="+legacy.RootIdentity+"\nsha256="+UpdateTrust.Hash(helperImage)+"\n"));
        var setting="BepInEx/config/kr.betterastralparty.mod.cfg";Write(legacy.GameRoot,setting,Encoding.UTF8.GetBytes("saved player choice"));var prompts=new List<string>();
        NativeInstall.Run(legacy,false,Path.Combine(root,"Steam"),11,prompt=>{prompts.Add(prompt);return "y";},message=>{});
        Check(legacy.Expected.All(row=>legacy.TargetHash(row.Key)==row.Value)&&InstallReceipt.Parse(legacy.ReadTarget(InstallReceipt.Path)!).LoaderBefore==true,"takeover installs the 1.4.0 payload and keeps the first loader baseline");
        foreach(var gone in new[]{OrdinaryInstallRecovery.LegacyLauncher,OrdinaryInstallRecovery.LegacyHelper,OrdinaryInstallRecovery.LegacyHelperReceipt,NativeInstall.LegacyHighWater})Check(legacy.ReadTarget(gone)==null&&FixtureControl.Events.Contains("remove:"+gone),"owned 1.3.x file removed: "+gone);
        Check(Encoding.UTF8.GetString(legacy.ReadTarget(setting)!)=="saved player choice"&&prompts.Count==1&&prompts[0].Contains("1.3.x"),"settings preserved; one explicit takeover consent");
        Check(Before("ordinary-start","remove:"+OrdinaryInstallRecovery.LegacyLauncher)&&Before("remove:"+OrdinaryInstallRecovery.LegacyLauncher,"ordinary-complete"),"legacy removal runs inside the ordinary journal");
        var refused=Case(legacy:true);var before=GameFiles(refused);Reject(()=>NativeInstall.Run(refused,false,Path.Combine(root,"Steam"),11,_=>"n",_=>{}),"refused takeover cancels");SameGame(refused,before,"refused takeover leaves exact 1.3.x bytes");
        var modified=Case(legacy:true);Write(modified.GameRoot,OrdinaryInstallRecovery.LegacyLauncher,Encoding.UTF8.GetBytes("user replaced launcher"));before=GameFiles(modified);
        Reject(()=>Run(modified),"modified 1.3.x owned file blocks takeover");SameGame(modified,before,"modified 1.3.x file preserved");
        var strayHelper=Case(legacy:true);Write(strayHelper.GameRoot,OrdinaryInstallRecovery.LegacyHelper,Encoding.UTF8.GetBytes("helper without its receipt"));before=GameFiles(strayHelper);
        Reject(()=>Run(strayHelper),"unverified helper pair blocks takeover");SameGame(strayHelper,before,"unverified helper preserved");
        var updateOnly=Case(legacy:true);Reject(()=>Run(updateOnly,true),"UpdateOnly cannot take over 1.3.x");
        var interrupted=Case(legacy:true);before=GameFiles(interrupted);FixtureControl.FailRemoval=true;Reject(()=>Run(interrupted),"legacy removal failure reported");
        Check(FixtureControl.Events.Contains("ordinary-rollback"),"failed takeover rolls back");SameGame(interrupted,before,"rollback restores the exact 1.3.x installation");
        var preview=Case(legacy:true);before=GameFiles(preview);Run(preview,preview:true);SameGame(preview,before,"CheckOnly takeover preview writes nothing");
    }
    private static Dictionary<string,byte[]> GameFiles(NativeMaintenanceBundle bundle)
    { return Directory.GetFiles(bundle.GameRoot,"*",SearchOption.AllDirectories).ToDictionary(path=>path,File.ReadAllBytes,StringComparer.Ordinal); }
    private static void SameGame(NativeMaintenanceBundle bundle,Dictionary<string,byte[]> before,string name)
    {
        var after=GameFiles(bundle);Check(before.Count==after.Count&&before.All(row=>after.TryGetValue(row.Key,out var bytes)&&row.Value.SequenceEqual(bytes)),name);
    }
    private static Exception Failed(Action action,string name)
    { try{action();}catch(Exception error){checks++;return error;}throw new InvalidOperationException(name); }
    private static void NoNewInstallation(string name)
    {
        foreach(var forbidden in new[]{"generation-review","generation-retire","update-recover","state-observe","receipt-prepare","ordinary-start","ordinary-rollback","write-probe","guard","shortcut-plan","shortcut-register","steam-stop"})
            Check(!FixtureControl.Events.Contains(forbidden),name+": no "+forbidden);
        Check(!FixtureControl.LockHeld,name+": ordinary lock released");
    }
    private static NativeMaintenanceBundle Unsupported(bool existing=false)
    {
        var bundle=Case(existing);Write(bundle.GameRoot,"GameAssembly.dll",Encoding.ASCII.GetBytes("updated native fixture image"));
        Write(bundle.GameRoot,"BepInEx/plugins/OtherMod/Other.dll",Encoding.ASCII.GetBytes("unrelated fixture mod"));
        FixtureControl.CompatibilityAllowed=false;return bundle;
    }
    private static void CompatibilityFailures()
    {
        foreach(var existing in new[]{false,true}) {
            var bundle=Unsupported(existing);var game=bundle.ReadTarget("GameAssembly.dll")!;var other=bundle.ReadTarget("BepInEx/plugins/OtherMod/Other.dll")!;Run(bundle,existing);
            Check(bundle.Expected.All(row=>bundle.TargetHash(row.Key)==row.Value),"Unreviewed game does not block owned installation/update");
            Check(bundle.ReadTarget("GameAssembly.dll")!.SequenceEqual(game)&&bundle.ReadTarget("BepInEx/plugins/OtherMod/Other.dll")!.SequenceEqual(other),"Unreviewed game and unrelated plugin remain unchanged");
            Check(Before("ordinary-lock","game-stop")&&Before("game-stop","compatibility")&&FixtureControl.Events.Contains("stage:BootstrapGate:Completed"),"Compatibility observation does not bypass locks or process preflight");
        }
        var generation=Unsupported();FixtureControl.Generation=true;Run(generation);
        Check(FixtureControl.Events.Contains("generation-retire")&&FixtureControl.Events.Contains("ordinary-complete"),"Unreviewed game permits separately verified/consented clean generation");

        var pending=Unsupported(true);FixtureControl.Pending=true;Directory.CreateDirectory(NativeMaintenance.JournalRoot(pending.GameRoot));File.WriteAllText(NativeMaintenance.JournalPath(pending.GameRoot),"inert pending");
        var setting="BepInEx/config/kr.betterastralparty.mod.cfg";var restored=Encoding.UTF8.GetBytes("explicitly restored fixture setting");Write(pending.GameRoot,setting,Encoding.UTF8.GetBytes("interrupted fixture setting"));
        var confirmations=0;
        FixtureControl.PendingRestore=()=>Write(pending.GameRoot,setting,restored);
        NativeInstall.Run(pending,false,Path.Combine(root,"Steam"),11,prompt=>{confirmations++;return "y";},_=>{});
        Check(confirmations==1&&Before("ordinary-recovery","compatibility")&&!FixtureControl.Pending&&!File.Exists(NativeMaintenance.JournalPath(pending.GameRoot)),"Pending repair retains consent before advisory compatibility observation");
        Check(pending.ReadTarget(setting)!.SequenceEqual(restored),"Restored user setting survives subsequent installation");

        var refused=Unsupported(true);FixtureControl.Pending=true;Directory.CreateDirectory(NativeMaintenance.JournalRoot(refused.GameRoot));var journal=NativeMaintenance.JournalPath(refused.GameRoot);File.WriteAllText(journal,"refused pending fixture");var refusedBefore=GameFiles(refused);
        var refusedError=Failed(()=>NativeInstall.Run(refused,false,Path.Combine(root,"Steam"),11,_=>"n",_=>{}),"refused pending recovery");
        Check(refusedError is OperationCanceledException&&!FixtureControl.Events.Contains("compatibility")&&File.ReadAllText(journal)=="refused pending fixture","refusal never bypasses or removes pending recovery");
        SameGame(refused,refusedBefore,"refused pending recovery leaves exact game bytes");NoNewInstallation("refused pending recovery");

        var unreadable=Unsupported(true);var io=new IOException("inert compatibility input read failure");FixtureControl.CompatibilityError=io;Run(unreadable,true);
        Check(object.ReferenceEquals(FixtureControl.LastFailure,io)&&FixtureControl.LastFailurePhase=="BootstrapGate","Unavailable compatibility observation is recorded without blocking update");
        var security=Unsupported(true);var securityError=new System.Security.SecurityException("inert Framework foreign-loader permission failure");FixtureControl.CompatibilityError=securityError;Run(security,true);
        Check(object.ReferenceEquals(FixtureControl.LastFailure,securityError)&&security.Expected.All(row=>security.TargetHash(row.Key)==row.Value),"Framework security observation is preserved without blocking owned update");

        foreach(var existing in new[]{false,true}) {
            var preview=Unsupported(existing);FixtureControl.Generation=!existing;var before=GameFiles(preview);
            Run(preview,existing,true);
            Check(!FixtureControl.Events.Any(value=>value.Contains("lock")||value.StartsWith("stage:",StringComparison.Ordinal)||value.StartsWith("error:",StringComparison.Ordinal)||value=="game-stop")&&!Directory.Exists(NativeMaintenance.JournalRoot(preview.GameRoot)),"unsupported CheckOnly has no locks/stages/errors/process stops or backup root");
            SameGame(preview,before,"unsupported CheckOnly preserves exact target bytes");
        }
        foreach(var partial in new[]{"winhttp.dll","BepInEx/core/OtherLoader.dll","dotnet/coreclr.dll","BepInEx/config/BepInEx.cfg"}) {
            var bundle=Case();var bytes=Encoding.UTF8.GetBytes("unknown foreign loader: "+partial);Write(bundle.GameRoot,partial,bytes);Run(bundle);
            Check(bundle.ReadTarget(partial)!.SequenceEqual(bytes),"Unknown/partial foreign loader is not overwritten: "+partial);
            Check(bundle.ReadTarget("doorstop_config.ini")==null,"Missing foreign config is not filled from another loader: "+partial);
            Check(!File.Exists(Path.Combine(bundle.GameRoot,"BepInEx/core/inert.dll")),"Foreign loader dependency set is not mixed: "+partial);
        }
    }
}
