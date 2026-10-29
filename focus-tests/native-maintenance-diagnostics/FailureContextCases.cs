using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Installation;
using BetterAstralParty.Observability;
using BetterAstralParty.Updating;

internal static partial class NativeMaintenanceDiagnosticsFixture
{
    private static (string Text,Dictionary<string,object> Summary,string ArchivePath) ContextZip(string game="")
    {
        var root=FixtureFolders.Root;var bundle=DiagnosticBundle.Collect(game,Path.Combine(root,"synthetic-local/BetterAstralParty/Logs"),Path.Combine(root,"synthetic-temp/BetterAstralParty"),"",Path.Combine(root,"hub"),Path.Combine(root,"hub2"),"4.8","1.3.0");
        using(var zip=ZipFile.OpenRead(Io(bundle.ArchivePath))){var all=new StringBuilder();string summary="";foreach(var entry in zip.Entries)using(var reader=new StreamReader(entry.Open())){var text=reader.ReadToEnd();all.Append(text);if(entry.FullName=="summary.json")summary=text;}return(all.ToString(),Map(new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=4194304}.DeserializeObject(summary)),bundle.ArchivePath);}
    }
    private static void ContextRoot(string root,string name){FixtureFolders.Root=Path.Combine(root,name);Directory.CreateDirectory(Io(FixtureFolders.Root));FixtureFolders.BlockLocal=FixtureFolders.BlockTemp=false;}
    private static Exception ContextSafety(string reason,ApplyFailure apply=ApplyFailure.RecoveryRequired,int native=0,int depth=3)
    {
        Exception error=new ApplySafetyException(apply,native);error.Data["BapRecoveryReason"]=reason;error.Data["BapInstallStage"]="Recovery";error.Data["token"]="FAKE_PRIVATE_METADATA_SECRET";error.Data["NativeError"]="FAKE_PRIVATE_SPOOFED_NUMBER";
        for(var n=1;n<depth;n++)error=new InvalidOperationException("Bounded wrapper",error);return error;
    }
    private static Exception ContextSteam()
    {
        var status=new SteamProcessStatus();status.Facts.Add(new SteamProcessObservation{Pid=12345,Name="steam",Available=true,SessionMatch=true,PathMatch=null});status.Facts.Add(new SteamProcessObservation{Pid=12346,Name="steam",Image=@"C:\inert\steam.exe",Available=true,SessionMatch=false,PathMatch=false,Creation=new DateTime(2026,10,11,18,0,0,DateTimeKind.Utc).ToFileTimeUtc()});
        var error=(Exception)typeof(NativeSteamMaintenance).GetMethod("Failure",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(string),typeof(SteamProcessStatus)},null)!.Invoke(null,new object[]{"SteamOwnershipUnknown","ShutdownObservation",status})!;error.Data["BapInstallStage"]="Steam shortcut planning";return error;
    }
    private static int RunFailureContext()
    {
        var basis=FixtureFolders.Root;
        try
        {
            ContextRoot(basis,"valid");var inner=ContextSteam();Exception failure=new InvalidOperationException("Wrapper failure",new InvalidOperationException("Wrapper failure",inner));var raw=Save(failure);
            Check(raw.Contains("BapInstallStage: Steam shortcut planning")&&raw.Contains("SteamDiagnostic: schema=1; reason=SteamOwnershipUnknown"),"nested original stage and actual native Steam reason recorded at failure time");
            Check(raw.Contains("sessionMatch=true; pathAvailable=false; pathMatch=unknown; startAvailable=false; startedUtc=unknown")&&raw.Contains("startedUtc=2026-10-11T18:00:00.0000000Z"),"unknown process facts remain distinct from false and observed UTC time");Check(raw.Contains("exceptionDepth=3; exceptionChainOmitted=false; textTruncated=false; steamContext=included"),"native source records original context coverage");
            var first=ContextZip();Check(first.Text.Contains("SteamOwnershipUnknown")&&first.Text.Contains("pid=12345")&&first.Text.Contains("pathMatch=unknown")&&first.Text.Contains("exceptionDepth=3"),"actual native guard exception to writer to ZIP retains bounded decision facts");
            Check(!first.Text.Contains(basis)&&!first.Text.Contains("FAKE_PRIVATE"),"private roots and arbitrary exception Data excluded");
            ContextCorrelation(basis);ContextWaiting(basis);ContextCompatibility(basis);ContextTermination(basis);
            foreach(var malformed in new[]{"unknown-field","invalid-time","wrong-type","oversize"})
            {
                ContextRoot(basis,malformed);var invalid=ContextSteam();var facts=(string)invalid.Data["BapSteamLog"]!;
                if(malformed=="unknown-field")facts=facts.Replace("schema=1;","schema=1; token=FAKE_PRIVATE_SECRET;");
                if(malformed=="invalid-time")facts=Regex.Replace(facts,@"utc=[^;\r\n]+","utc=2026-99-99T19:00:00.0000000Z");
                if(malformed=="wrong-type")facts=facts.Replace("pathAvailable=false","pathAvailable=FAKE_PRIVATE_SECRET");
                if(malformed=="oversize")facts="FAKE_PRIVATE_SECRET"+new string('x',16384);
                invalid.Data["BapSteamLog"]=facts;raw=Save(invalid);var zip=ContextZip();Check(!raw.Contains("FAKE_PRIVATE")&&!zip.Text.Contains("FAKE_PRIVATE")&&zip.Text.Contains("steamContext="+(malformed=="oversize"?"context-limit":"invalid-context")),"invalid native context rejected at recorder, then ZIP: "+malformed);
            }
            foreach(var reason in new[]{"HighWaterInvalid","HighWaterRootMismatch","HighWaterRepositoryMismatch","HighWaterUnbound","HighWaterProofMissing","HighWaterProofChanged","HighWaterProofAmbiguous","HighWaterProofBusy","MaintenancePathTooLong","MaintenancePathInvalid","OrdinaryHelperRepairMalformed","OrdinaryHelperRepairProofChanged","OrdinaryHelperRepairJournalChanged","OrdinaryHelperRepairObjectChanged","OrdinaryHelperRepairFailed"})
            {
                ContextRoot(basis,"safety-"+reason);var native=reason=="MaintenancePathTooLong"?206:reason=="HighWaterProofBusy"||reason=="OrdinaryHelperRepairFailed"?32:0;var apply=native==206?ApplyFailure.UnsafePath:native==32?ApplyFailure.FileBusy:ApplyFailure.RecoveryRequired;raw=Save(ContextSafety(reason,apply,native));var zip=ContextZip();
                Check(raw.Contains(reason)&&zip.Text.Contains(reason)&&raw.Contains("applyCode="+(int)apply+"; nativeCode="+native)&&zip.Text.Contains("applyCode="+(int)apply+"; nativeCode="+native)&&raw.Contains("BapInstallStage: Recovery")&&zip.Text.Contains("BapInstallStage: Recovery"),"production native safety reason, exact recovery stage and typed codes reach ZIP: "+reason);
                Check(raw.Contains("Bounded wrapper")&&zip.Text.Contains("Bounded wrapper")&&!raw.Contains("FAKE_PRIVATE")&&!zip.Text.Contains("FAKE_PRIVATE")&&!zip.Text.Contains(basis),"original failure chain preserved without arbitrary Data or personal paths: "+reason);
            }
            foreach(var reason in new[]{"FAKE_PRIVATE_METADATA_SECRET","HighWaterInvalid\ntoken=FAKE_PRIVATE_METADATA_SECRET","OrdinaryHelperRepairFailed\ntoken=FAKE_PRIVATE_METADATA_SECRET",""})
            {
                ContextRoot(basis,"unknown-"+Guid.NewGuid().ToString("N"));raw=Save(ContextSafety(reason));var zip=ContextZip();Check(!raw.Contains("FAKE_PRIVATE")&&!zip.Text.Contains("FAKE_PRIVATE")&&raw.Contains("unknown")&&zip.Text.Contains("unknown"),"unknown or newline-injected safety reason classified before raw log and ZIP");
            }
            ContextRoot(basis,"transient");raw=Save(ContextSafety("",ApplyFailure.FileBusy,32));Check(ContextZip().Text.Contains("applyCode=3; nativeCode=32"),"transient lock remains distinct from history refusal");
            ContextRoot(basis,"depth-limit");raw=Save(ContextSafety("HighWaterProofMissing",depth:10));var deep=ContextZip();Check(deep.Text.Contains("exceptionDepth=8; exceptionChainOmitted=true")&&!deep.Text.Contains("HighWaterProofMissing")&&!deep.Text.Contains("FAKE_PRIVATE"),"unobserved ninth-depth safety Data never reconstructed");
            ContextRoot(basis,"forged");var logs=Path.Combine(FixtureFolders.Root,"logs");Directory.CreateDirectory(Io(logs));File.WriteAllText(Io(Path.Combine(logs,"BetterAstralParty-InstallError-20261012-010000-1234abcd.log")),"SteamDiagnostic: schema=1; token=FAKE_PRIVATE_SECRET\n FAKE_PRIVATE_CONTINUATION\nCode: BAP-INSTALL-FAILED\n");var forged=DiagnosticBundle.Collect("",logs,"","",Path.Combine(FixtureFolders.Root,"h"),Path.Combine(FixtureFolders.Root,"h2"),"4.8");string forgedText;using(var zip=ZipFile.OpenRead(Io(forged.ArchivePath)))forgedText=string.Join("\n",zip.Entries.Select(entry=>{using(var reader=new StreamReader(entry.Open()))return reader.ReadToEnd();}));Check(!forgedText.Contains("FAKE_PRIVATE")&&forgedText.Contains("invalid-failure-context"),"forged fixed-context fields and continuation omitted with counted rejection");
            File.WriteAllText(Io(Path.Combine(logs,"BetterAstralParty-InstallError-20261012-010000-1234abcd.log")),"-----BEGIN PRIVATE KEY-----\nSteamDiagnostic: schema=1; token=FAKE_PRIVATE_SECRET\n-----END PRIVATE KEY-----\nCode: BAP-INSTALL-FAILED\n");forged=DiagnosticBundle.Collect("",logs,"","",Path.Combine(FixtureFolders.Root,"h3"),Path.Combine(FixtureFolders.Root,"h4"),"4.8");using(var zip=ZipFile.OpenRead(Io(forged.ArchivePath)))forgedText=string.Join("\n",zip.Entries.Select(entry=>{using(var reader=new StreamReader(entry.Open()))return reader.ReadToEnd();}));Check(!forgedText.Contains("FAKE_PRIVATE")&&forgedText.Contains("BAP-INSTALL-FAILED"),"private key block cannot inject typed context");
            FixtureFolders.Root=basis;File.WriteAllText(Io(Path.Combine(basis,"failure-context-verification.json")),Json(new{Passed=true,Checks=Passed,ActualNativeGuardProducerToZip=true,Retired="PowerShell wrapper/runtime recovery bridge comparisons; actual native held-object recovery is separately exercised",WaitingDiagnosticParity=true,RealSteamGameKeysOrNetworkUsed=false}));return 0;
        }
        finally{FixtureFolders.Root=basis;}
    }
    private static void ContextTermination(string basis)
    {
        foreach(var item in new[]{(Reason:"ProcessCaptureFailed",Native:5),(Reason:"ProcessTerminateOpenFailed",Native:5),(Reason:"ProcessTerminateFailed",Native:5),(Reason:"ProcessExitWaitFailed",Native:6),(Reason:"ProcessExitTimeout",Native:0)})
        {
            ContextRoot(basis,"termination-"+item.Reason);
            var status=new SteamProcessStatus();status.Facts.Add(new SteamProcessObservation {Pid=5432,Name="steamwebhelper",Image=@"C:\inert\steam\bin\steamwebhelper.exe",Available=true,SessionMatch=true,PathMatch=true,Creation=new DateTime(2026,10,11,18,0,0,DateTimeKind.Utc).ToFileTimeUtc(),NativeError=item.Native});
            var context=(Exception)typeof(NativeSteamMaintenance).GetMethod("Failure",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(string),typeof(SteamProcessStatus)},null)!.Invoke(null,new object[]{item.Reason,"ProcessTermination",status})!;
            Exception cause=item.Reason=="ProcessCaptureFailed"?new ApplySafetyException(ApplyFailure.ProcessUncertain,item.Native):item.Native==0?context:new System.ComponentModel.Win32Exception(item.Native);
            if(cause!=context) foreach(System.Collections.DictionaryEntry field in context.Data) cause.Data[field.Key]=field.Value;
            cause.Data["untrusted"]="FAKE_PRIVATE_TERMINATION_DATA";
            var raw=Save(Captured(cause));var zip=ContextZip();
            foreach(var text in new[]{"reason="+item.Reason+"; phase=ProcessTermination;","kind=SteamWebHelper; querySource=Native; nativeError="+item.Native+"; exitAvailable=true; confirmedExit=false","SteamObservationCoverage: status=included","steamContext=included"})
                Check(raw.Contains(text)&&zip.Text.Contains(text),"production-shaped Steam termination cause reaches recorder and ZIP: "+item.Reason+"/"+text);
            var type=item.Reason=="ProcessCaptureFailed"?typeof(ApplySafetyException).FullName:item.Native==0?"System.InvalidOperationException":"System.ComponentModel.Win32Exception";
            Check(raw.Contains("type="+type)&&zip.Text.Contains(type),"Steam termination exception provenance retained: "+item.Reason);
            Check(!raw.Contains("FAKE_PRIVATE")&&!zip.Text.Contains("FAKE_PRIVATE")&&!zip.Text.Contains(basis)&&!zip.Text.Contains(@"C:\inert"),"Steam termination context omits arbitrary Data and process paths: "+item.Reason);
            if(item.Native==0) Check(!raw.Contains("nativeError=5")&&!zip.Text.Contains("nativeError=5")&&!raw.Contains("Win32Exception")&&!zip.Text.Contains("Win32Exception"),"wait timeout carries no stale Win32 access-denied cause");
        }
    }
    private static void ContextCompatibility(string basis)
    {
        ContextRoot(basis,"compatibility-code");var game=Path.Combine(FixtureFolders.Root,"synthetic-game");Directory.CreateDirectory(Io(game));
        var cause=new ApplySafetyException(ApplyFailure.CompatibilityRequired);
        var failure=new InvalidOperationException("호환성 검사 실패 / Compatibility check failed: Changed [fixture]: game/GameAssembly.dll",cause);
        failure.Data["BapInstallStage"]="Compatibility validation";failure.Data["token"]="FAKE_PRIVATE_COMPATIBILITY_DATA";
        var raw=Save(failure);
        Check(raw.Contains("BapInstallStage: Compatibility validation")&&raw.Contains("applyCode="+(int)ApplyFailure.CompatibilityRequired+"; nativeCode=0"),"original compatibility stage and typed cause reach native failure log");
        Check(DiagnosticHub.CodeForStatus("CompatibilityRequired")==DiagnosticCode.Prerequisite,"global updater status mapping remains unchanged");
        using(var sink=DiagnosticOwner.StartInstaller(game,"1.3.0",new string('a',32))) {
            Check(DiagnosticHub.Bind(sink),"isolated real installer diagnostic sink bound");
            try {
                SetupHost.RecordInstallerFailure("BootstrapGate",failure);
                SetupHost.RecordInstallerFailure("HelperInstall",new ApplySafetyException(ApplyFailure.FileBusy,32));
                Check(sink.Stop(DiagnosticEndReason.OwnerDisposed),"real installer failure mapper and writer flush before ZIP");
            } finally { DiagnosticHub.Unbind(sink); }
        }
        var events=Path.Combine(game,"BepInEx/BetterAstralParty-Diagnostics/minimal-v1/installer/events-current.jsonl");
        var rows=File.ReadAllLines(Io(events)).Select(line=>Map(new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(line))).Where(row=>(string)row["kind"]=="Failure").ToArray();
        Check(rows.Any(row=>(string)row["phase"]=="BootstrapGate"&&(string)row["code"]=="CompatibilityBlocked"&&(string)row["error_type"]=="ApplySafetyException"&&Convert.ToInt32(row["apply_code"])==(int)ApplyFailure.CompatibilityRequired&&Convert.ToInt32(row["native_code"])==0),"literal production mapper emits exact compatibility blocked event and underlying apply cause");
        Check(rows.Any(row=>(string)row["phase"]=="HelperInstall"&&(string)row["code"]=="FilesBusy"&&Convert.ToInt32(row["native_code"])==32),"unrelated installer failures retain their original phase and code");
        var collected=ContextZip(game);
        using(var zip=ZipFile.OpenRead(Io(collected.ArchivePath))) {
            var entry=zip.GetEntry("logs/game-minimal-installer-current.log");Check(entry!=null,"collector includes actual installer minimal event stream");
            using(var reader=new StreamReader(entry!.Open())) {
                var records=reader.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(line=>Map(new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(line))).Where(row=>(string)row["kind"]=="Failure").ToArray();
                Check(records.Any(row=>(string)row["phase"]=="BootstrapGate"&&(string)row["code"]=="CompatibilityBlocked"&&(string)row["error_type"]=="ApplySafetyException"&&Convert.ToInt32(row["apply_code"])==(int)ApplyFailure.CompatibilityRequired&&Convert.ToInt32(row["native_code"])==0),"actual production mapper to writer to ZIP retains compatibility phase/code/cause");
            }
        }
        Check(collected.Text.Contains("BapInstallStage: Compatibility validation")&&!collected.Text.Contains("FAKE_PRIVATE_COMPATIBILITY_DATA")&&!collected.Text.Contains(basis),"compatibility failure ZIP preserves detailed stage without arbitrary Data or source paths");
    }
    private static void ContextFlatWork(string basis)
    {
        ContextRoot(basis,"flat-work-action");var game=Path.Combine(FixtureFolders.Root,"synthetic-game");Directory.CreateDirectory(Io(game));
        Exception cause;
        try { UpdateWorkPath.RequireContained("bap-v4-txn-"+new string('a',32),"txn");throw new InvalidOperationException("Flat work accepted"); }
        catch(ApplySafetyException error){cause=error;}
        cause.Data["BapRecoveryAction"]="FAKE_PRIVATE_UNTRUSTED_ACTION";cause.Data["BapInstallStage"]="Recovery";
        var raw=Save(cause);var action=NativeMaintenanceEntry.RecoveryAction(cause);
        Check(action!=null&&action.Contains("이전 설치 패키지")&&action.Contains("matching previous package")&&raw.Contains("BapRecoveryReason: LegacyFlatWorkPending")&&raw.Contains(action)&&!raw.Contains("FAKE_PRIVATE"),"actual flat-work guard emits allowlisted bilingual action without trusting free-form exception Data");
        using(var sink=DiagnosticOwner.StartInstaller(game,"1.3.0",new string('a',32))) {
            Check(DiagnosticHub.Bind(sink),"flat-work diagnostic sink bound");
            try { SetupHost.RecordInstallerFailure("Recovery",cause);Check(sink.Stop(DiagnosticEndReason.OwnerDisposed),"flat-work prerequisite event flushed"); }
            finally{DiagnosticHub.Unbind(sink);}
        }
        var collected=ContextZip(game);
        Check(collected.Text.Contains("LegacyFlatWorkPending")&&collected.Text.Contains("matching previous package")&&collected.Text.Contains("\"code\":\"Prerequisite\"")&&!collected.Text.Contains("FAKE_PRIVATE")&&!collected.Text.Contains(basis),"actual work guard to maintenance recorder and minimal writer to ZIP retains actionable reason and prerequisite code");
        var unknown=new InvalidOperationException("inert");unknown.Data["BapRecoveryReason"]="FAKE_PRIVATE_UNTRUSTED_REASON";unknown.Data["BapRecoveryAction"]="FAKE_PRIVATE_UNTRUSTED_ACTION";
        Check(NativeMaintenanceEntry.RecoveryAction(unknown)==null,"unknown reasons cannot select or inject recovery actions");
        var unverifiable=NativeMaintenanceEntry.RecoveryAction(new InvalidOperationException("inert wrapper",new ApplySafetyException(ApplyFailure.InvalidState)));
        Check(unverifiable!=null&&unverifiable.Contains("Force-Uninstall.cmd")&&unverifiable.Contains("자동 업데이트 기록")&&unverifiable.Contains("could not be verified"),"unverifiable update records name the clean-up path in both languages");
        Check(NativeMaintenanceEntry.RecoveryAction(new ApplySafetyException(ApplyFailure.FileBusy))==null,"other update-safety failures add no recovery action");
    }
    private static void ContextCorrelation(string basis)
    {
        ContextRoot(basis,"correlation");const string run="12345678123412341234123456789abc";Set("_run",run);Set("_version","1.3.0");Set("_operation","Installation");Set("_package",Path.Combine(FixtureFolders.Root,"package"));Set("_game",Path.Combine(FixtureFolders.Root,"game"));
        var error=ContextSteam();var first=NativeMaintenanceEntry.Failure(error)!;var before=Hash(first);var second=NativeMaintenanceEntry.Failure(error)!;Check(first!=second&&Hash(first)==before,"same-operation duplicate native failures never overwrite prior bytes");
        var logs=Path.Combine(FixtureFolders.Root,"synthetic-local/BetterAstralParty/Logs");Directory.CreateDirectory(Io(logs));File.WriteAllText(Io(Path.Combine(logs,"Bootstrap-"+run+".log")),"BAP-BOOTSTRAP-FAILED\nRun: "+run+"\n");
        var game=Path.Combine(FixtureFolders.Root,"game");WriteContextStage(game,run,"Configuration","Failed","Prerequisite");var collected=ContextZip(game);var rows=((object[])collected.Summary["files"]).Select(Map).Where(row=>(string)row["status"]=="included").ToArray();var aliases=rows.SelectMany(row=>(object[])row["operationAliases"]).Cast<string>().Distinct().ToArray();
        Check(aliases.Length==1&&Regex.IsMatch(aliases[0],@"\A[a-f0-9]{32}\z")&&collected.Text.Contains("\"operation\":\""+aliases[0]+"\""),"duplicate native errors and historical bootstrap correlate with minimal installer stage using one ZIP-local alias");
        Check(rows.Count(row=>(string)row["sourceKind"]=="installer-uninstaller-error")==2&&rows.Count(row=>(string)row["sourceKind"]=="bootstrap-error")==1,"fixed native and historical bootstrap provenance survives redaction");
        Check(!collected.Text.Contains(run)&&!collected.Text.Contains(basis),"raw operation and absolute source identities omitted");var again=ContextZip(game);var later=((object[])again.Summary["files"]).Select(Map).Where(row=>(string)row["status"]=="included").SelectMany(row=>(object[])row["operationAliases"]).Cast<string>().Distinct().ToArray();Check(later.Length==1&&later[0]!=aliases[0],"new ZIP receives fresh operation aliases");
    }
    private static void WriteContextStage(string game,string run,string phase,string outcome,string code)
    {
        var events=Path.Combine(game,"BepInEx/BetterAstralParty-Diagnostics/minimal-v1/installer");Directory.CreateDirectory(Io(events));
        var minimal=new Dictionary<string,object>{{"schema","bap.minimal-diagnostic/v1"},{"process_session",new string('9',32)},{"sequence",1},{"utc",DateTimeOffset.UtcNow.ToString("O")},{"elapsed_ms",0},{"mod_version","1.3.0"},{"build_id","unknown"},{"runtime_version","4.8"},{"kind","Stage"},{"feature","Installer"},{"phase",phase},{"outcome",outcome},{"code",code},{"ui_action","Unknown"},{"error_site","None"},{"error_type","None"},{"operation",run},{"related_operation","-"},{"target_version","unknown"},{"update_state","Unknown"},{"count",0},{"secondary_count",0},{"native_code",0},{"validation_code",0},{"apply_code",0}};
        File.WriteAllText(Io(Path.Combine(events,"events-current.jsonl")),Json(minimal)+"\n",new UTF8Encoding(false));
    }
    private static void ContextWaiting(string basis)
    {
        var priorStatus=NativeSteamMaintenance.SteamStatusObserver;var priorProcesses=NativeSteamMaintenance.ProcessObserver;var priorWaiting=NativeSteamMaintenance.WaitingDiagnostic;
        try
        {
            NativeSteamMaintenance.ProcessObserver=()=>throw new InvalidOperationException("Live process enumeration is forbidden in the waiting fixture");
            NativeSteamMaintenance.WaitingDiagnostic=NativeMaintenanceEntry.Waiting;
            foreach(var kind in new[]{"Unknown","OtherSession"})
            {
                ContextRoot(basis,"waiting-"+kind);var run=Guid.NewGuid().ToString("N");var game=Path.Combine(FixtureFolders.Root,"game");var steam=Path.Combine(FixtureFolders.Root,"steam");Directory.CreateDirectory(Io(steam));
                Set("_run",run);Set("_version","1.3.0");Set("_operation","Installation");Set("_package",Path.Combine(FixtureFolders.Root,"package"));Set("_game",game);WriteContextStage(game,run,"WaitForExit","Begin","None");
                var status=new SteamProcessStatus{State=kind=="Helper"?"Running":kind};status.Facts.Add(new SteamProcessObservation{Pid=5432,Name="steam",NativeError=5,SessionMatch=kind=="OtherSession"?(bool?)false:null});
                var prompts=0;var state=new SteamSessionState();var logs=Path.Combine(FixtureFolders.Root,"synthetic-local/BetterAstralParty/Logs");string? firstPath=null;string? firstHash=null;
                NativeSteamMaintenance.SteamStatusObserver=()=>prompts<2?status:new SteamProcessStatus();
                if(kind=="Helper")
                {
                    File.WriteAllText(Io(Path.Combine(steam,"steam.exe")),"inert, never executed");var apps=Path.Combine(steam,"steamapps");Directory.CreateDirectory(Io(apps));File.WriteAllText(Io(Path.Combine(apps,"appmanifest_1.acf")),"\"installdir\" \"FixtureGame\"");
                    var helper=new SteamProcessObservation{Pid=9876,Name="BetterAstralParty-UpdateHelper",Image=Path.Combine(apps,"common","FixtureGame","BetterAstralParty-UpdateHelper.exe"),Available=true,Session=(uint)System.Diagnostics.Process.GetCurrentProcess().SessionId,Creation=new DateTime(2026,10,11,18,0,0,DateTimeKind.Utc).ToFileTimeUtc()};
                    Exception? classified=null;try{NativeSteamMaintenance.SelectSteamTargets(new[]{helper},steam,new[]{Path.GetDirectoryName(helper.Image)!},helper.Session);}catch(InvalidOperationException error){classified=error;}
                    Check(classified?.Data["BapSteamLog"] is string facts&&facts.Contains("reason=HelperRunning; phase=ShutdownObservation;")&&facts.Contains("kind=UpdateHelper"),"canonical observed helper is classified as a helper wait, not a force-close target");
                    NativeSteamMaintenance.ProcessObserver=()=>new[]{helper};
                }
                else NativeSteamMaintenance.ProcessObserver=()=>throw new InvalidOperationException("Live process enumeration is forbidden in the waiting fixture");
                Exception? callbackFailure=null;
                try{NativeSteamMaintenance.StopSteam(steam,state,prompt=>
                {
                    try{
                    prompts++;var files=Directory.GetFiles(Io(logs),"BetterAstralParty-InstallError-*.log");Check(files.Length==1,"waiting reason is durable before each consent and unchanged rechecks are deduplicated: "+kind);
                    var raw=File.ReadAllText(Io(files[0]));var reason=kind=="Unknown"?"SteamOwnershipUnknown":kind=="OtherSession"?"SteamOtherSession":"HelperRunning";
                    Check(raw.Contains("Code: BAP-INSTALL-WAITING")&&!raw.Contains("BAP-INSTALL-FAILED")&&raw.Contains("reason="+reason)&&raw.Contains(kind=="Helper"?"kind=UpdateHelper":"pathMatch=unknown"),"durable waiting record retains typed observations without claiming terminal failure: "+kind);
                    if(firstPath==null){firstPath=files[0];firstHash=Hash(firstPath);}else Check(files[0]==firstPath&&Hash(firstPath)==firstHash,"successful later recheck does not rewrite the original waiting observation: "+kind);
                    var collected=ContextZip(game);var included=((object[])collected.Summary["files"]).Select(Map).Where(row=>(string)row["status"]=="included").ToArray();var aliases=included.SelectMany(row=>(object[])row["operationAliases"]).Cast<string>().Distinct().ToArray();
                    Check(aliases.Length==1&&collected.Text.Contains("\"phase\":\"WaitForExit\"")&&collected.Text.Contains("\"outcome\":\"Begin\"")&&collected.Text.Contains(reason)&&!collected.Text.Contains(run)&&!collected.Text.Contains(basis),"waiting producer and minimal WaitForExit.Begin share ZIP-local correlation before input: "+kind);return "r";
                    }catch(Exception error){callbackFailure=error;throw;}
                },message=>{});}
                catch when(callbackFailure!=null){ExceptionDispatchInfo.Capture(callbackFailure).Throw();throw;}
                Check(prompts==2&&!state.Restart&&Directory.GetFiles(Io(logs),"BetterAstralParty-InstallError-*.log").Length==1,"normal-exit recheck keeps its waiting evidence without restarting or terminating any process: "+kind);
            }
            ContextRoot(basis,"waiting-preview");typeof(NativeMaintenanceEntry).GetField("_preview",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,true);
            NativeMaintenanceEntry.Waiting(ContextSteam());Check(!Directory.Exists(Io(Path.Combine(FixtureFolders.Root,"synthetic-local"))),"preview records no waiting log");
        }
        finally
        {
            typeof(NativeMaintenanceEntry).GetField("_preview",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,false);NativeSteamMaintenance.SteamStatusObserver=priorStatus;NativeSteamMaintenance.ProcessObserver=priorProcesses;NativeSteamMaintenance.WaitingDiagnostic=priorWaiting;
        }
    }
}
