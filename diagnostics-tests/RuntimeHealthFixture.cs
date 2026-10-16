#nullable enable
using System;using System.Threading;using System.Diagnostics;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text;using System.Text.Json;using System.IO.Compression;using System.Security.Cryptography;using System.Runtime.InteropServices;
using BetterAstralParty.Observability;using BetterAstralParty.Diagnostics;
internal static class RuntimeHealthFixture {
 static readonly List<string> Cases=new();static string Base="";
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLinkW(string next,string prior,IntPtr reserved);
 static void Check(bool v,string n){if(!v)throw new Exception("FAIL:"+n);Cases.Add(n);}
 static bool Flush(MinimalEventLog owner){if(!SpinWait.SpinUntil(()=>owner.Recording||owner.StorageFailed,TimeSpan.FromSeconds(5)))return false;var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<2000){if(owner.StorageFailed)return false;if(owner.FlushBoundary(50))return true;Thread.Sleep(5);}return false;}
 static string Root(string leaf){var p=Path.Combine(Base,leaf);Directory.CreateDirectory(Path.Combine(p,"AstralParty_INT_Data"));File.WriteAllText(Path.Combine(p,"AstralParty_INT.exe"),"inert never executed");return p;}
 static string Minimum(string root)=>Path.Combine(root,"BepInEx","BetterAstralParty-Diagnostics","minimal-v1");
 static Dictionary<string,string> Hashes(string directory)=>Directory.GetFiles(directory,"*",SearchOption.AllDirectories).ToDictionary(p=>p.Substring(directory.Length+1),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
 static LiveDiagnosticHealth Health(MinimalEventLog? owner)=>new(owner!=null,owner?.Detailed??false,owner?.Recording??false,owner?.StorageFailed??false,owner?.Dropped??0,owner?.ModVersion??"unknown",owner?.BuildId??"unknown",owner?.ProcessSession??"",DateTime.UtcNow);
 static (JsonDocument Summary,string JsonLines,string Entire) Collect(string root,string name,LiveDiagnosticHealth? health) {
  var result=DiagnosticBundle.Collect(root,Path.Combine(Base,"absent-local"),Path.Combine(Base,"absent-temp"),Path.Combine(Base,"absent-unity"),Path.Combine(Base,"hub-"+name),Path.Combine(Base,"fallback-"+name),"","1.0.0-rc.2",health!);
  using var zip=ZipFile.OpenRead(result.ArchivePath);string Read(string name){using var sr=new StreamReader(zip.GetEntry(name)!.Open());return sr.ReadToEnd();}
  var summary=Read("summary.json");var lines=string.Join("\n",zip.Entries.Where(e=>e.FullName.StartsWith("logs/")).Select(e=>{using var sr=new StreamReader(e.Open());return sr.ReadToEnd();}));
  return(JsonDocument.Parse(summary),lines,summary+"\n"+lines);
 }
 public static int Main(string[] args){
  Base=args[0];Directory.CreateDirectory(Base);var root=Root("fresh");
  using(var runtime=DiagnosticOwner.Start(root,false,"1.0.0-rc.2",new string('A',32)))
  using(var helper=DiagnosticOwner.Start(root,true,"1.0.0-rc.2",new string('B',32)))
  using(var installer=DiagnosticOwner.StartInstaller(root,"1.0.0-rc.2",new string('C',32))){
   Check(Flush(runtime)&&runtime.Recording&&!runtime.StorageFailed,"actual runtime owner uses secure store");
   Check(Flush(helper)&&helper.Recording&&!helper.StorageFailed,"actual helper owner uses separate secure store");
   Check(Flush(installer)&&installer.Recording&&!installer.StorageFailed,"actual installer owner uses separate secure store");
   Check(new[]{runtime.ProcessSession,helper.ProcessSession,installer.ProcessSession}.Distinct().Count()==3,"three production owners have separate process sessions");
   runtime.Stage(DiagnosticFeature.Settings,DiagnosticPhase.UiAction,DiagnosticOutcome.Begin);
   var settingsPhases=Enum.GetValues<DiagnosticPhase>().Where(phase=>phase.ToString().StartsWith("Settings",StringComparison.Ordinal)).ToArray();
   foreach(var phase in settingsPhases)runtime.Stage(DiagnosticFeature.Settings,phase,DiagnosticOutcome.Observed);
   helper.Stage(DiagnosticFeature.Helper,DiagnosticPhase.Recovery,DiagnosticOutcome.Begin);
   installer.Stage(DiagnosticFeature.Installer,DiagnosticPhase.Configuration,DiagnosticOutcome.Begin);
   foreach(var channel in new[]{DiagnosticChannel.Stable,DiagnosticChannel.PreRelease,DiagnosticChannel.Beta})runtime.Settings(new DiagnosticSettings(15,DiagnosticLanguage.Korean,channel,DiagnosticHandMode.Hover,100,75));
   var newActions=new[]{DiagnosticUiAction.AutoThanks,DiagnosticUiAction.CardTargetButtons,DiagnosticUiAction.CharacterGuide,DiagnosticUiAction.CharacterRoles,DiagnosticUiAction.IndicatorFade,DiagnosticUiAction.IndicatorHide,DiagnosticUiAction.CounterSound,DiagnosticUiAction.CounterAnimation,DiagnosticUiAction.SuppressNotices,DiagnosticUiAction.PingFocus,DiagnosticUiAction.UpdateNotifications,DiagnosticUiAction.AutomaticChecks,DiagnosticUiAction.ShushuShieldRestrictions,DiagnosticUiAction.FinalDamage,DiagnosticUiAction.FieldZoom,DiagnosticUiAction.MatchFocus};
   foreach(var action in newActions)runtime.UiAction(action,DiagnosticOutcome.Completed,DiagnosticOperation.New());
   var updateStates=new[]{DiagnosticUpdateState.NoCompatibleUpdate,DiagnosticUpdateState.CompatibilityRejected};
   foreach(var state in updateStates)runtime.UpdateStatus(DiagnosticFeature.ReleaseCheck,state);
   Check(Flush(runtime)&&Flush(helper)&&Flush(installer),"actual owner records flush without shared files");
   var snapshot=Health(runtime);var first=Collect(root,"first",snapshot);using(first.Summary){
    var live=first.Summary.RootElement.GetProperty("liveHostHealth");var actual=live.GetProperty("runtime");
    Check(actual.GetProperty("availability").GetString()=="captured"&&actual.GetProperty("source").GetString()=="running-plugin-owner","live host identifies real memory capture");
    Check(actual.GetProperty("modulePresent").GetBoolean()&&actual.GetProperty("recording").GetBoolean()&&!actual.GetProperty("storageFailed").GetBoolean(),"live captured recording and storage flags");
    Check(!actual.GetProperty("requestedDetailed").GetBoolean(),"minimum recording does not imply requested detailed logging");
    Check(actual.GetProperty("capturedUtc").GetString()!.EndsWith("Z")&&actual.GetProperty("modVersion").GetString()=="1.0.0-rc.2"&&actual.GetProperty("buildId").GetString()==new string('A',32),"capture UTC and controlled source/build identity");
    var alias=actual.GetProperty("processSession").GetString()!;Check(alias!=runtime.ProcessSession&&first.JsonLines.Contains(alias),"live health reuses runtime event ZIP-local session alias");
    Check(!first.Entire.Contains(runtime.ProcessSession)&&!first.Entire.Contains(helper.ProcessSession)&&!first.Entire.Contains(installer.ProcessSession),"raw owner process GUIDs never exported");
    var channelRows=new HashSet<string>();var actionRows=new HashSet<string>();var stateRows=new HashSet<string>();foreach(var line in first.JsonLines.Split('\n').Where(line=>line.Length>0)){using var doc=JsonDocument.Parse(line);if(doc.RootElement.GetProperty("kind").GetString()=="Settings")channelRows.Add(doc.RootElement.GetProperty("settings").GetProperty("channel").GetString()!);actionRows.Add(doc.RootElement.GetProperty("ui_action").GetString()!);stateRows.Add(doc.RootElement.GetProperty("update_state").GetString()!);}
    Check(new[]{"Stable","PreRelease","Beta"}.All(channelRows.Contains),"actual typed writer, fenced store and diagnostic ZIP preserve all three bounded channel values");
    Check(newActions.All(action=>actionRows.Contains(action.ToString())),"actual typed writer, fenced store and diagnostic ZIP preserve all sixteen current bounded settings actions");
    Check(updateStates.All(state=>stateRows.Contains(state.ToString())),"actual typed writer, fenced store and diagnostic ZIP preserve bounded compatibility update states");
    var currentRecords=first.JsonLines.Split('\n').Where(line=>line.Length>0).ToArray();
    Check(currentRecords.All(line=>OwnedDiagnosticRecord.Inspect(line,out _,out _,out _)),"owned record parser accepts the actual writer-to-ZIP action and update-state closure");
    Check(settingsPhases.Length==21&&settingsPhases.All(phase=>currentRecords.Any(line=>line.Contains("\"phase\":\""+phase+"\"",StringComparison.Ordinal))),"detailed-OFF settings creation checkpoints survive actual background writer, fenced store and ZIP projection");
    var settingsRecord=currentRecords.First(line=>line.Contains("\"phase\":\"SettingsModal\"",StringComparison.Ordinal));
    Check(!OwnedDiagnosticRecord.TryProject(settingsRecord.Replace("\"SettingsModal\"","\"FAKE_PRIVATE_PHASE\"",StringComparison.Ordinal),out _),"settings checkpoint allowlist still rejects arbitrary phase strings");
    var actionRecord=currentRecords.First(line=>line.Contains("\"ui_action\":\"FieldZoom\"",StringComparison.Ordinal));var stateRecord=currentRecords.First(line=>line.Contains("\"update_state\":\"NoCompatibleUpdate\"",StringComparison.Ordinal));
    Check(!OwnedDiagnosticRecord.TryProject(actionRecord.Replace("\"FieldZoom\"","\"FAKE_UNKNOWN_ACTION\"",StringComparison.Ordinal),out _)&&!OwnedDiagnosticRecord.TryProject(stateRecord.Replace("\"NoCompatibleUpdate\"","\"FAKE_UNKNOWN_STATE\"",StringComparison.Ordinal),out _),"owned action/state additions do not accept unknown strings");
    Check(live.GetProperty("helper").GetProperty("availability").GetString()=="unavailable"&&live.GetProperty("installer").GetProperty("availability").GetString()=="unavailable","separate process live health remains unavailable");
    var second=Collect(root,"second",Health(runtime));using(second.Summary)Check(second.Summary.RootElement.GetProperty("liveHostHealth").GetProperty("runtime").GetProperty("processSession").GetString()!=alias,"new bundle refreshes health/event session aliases");
   }
   Check(!CreateHardLinkW(Path.Combine(Base,"live-hardlink.jsonl"),Path.Combine(Minimum(root),"events-current.jsonl"),IntPtr.Zero),"production owner data-only ACL prevents live hardlink creation");
   Check(runtime.Recording&&!runtime.StorageFailed&&Flush(runtime),"collector live read leaves secure writer usable");
  }
  using(var reopened=DiagnosticOwner.Start(root,false,"1.0.0-rc.2",new string('A',32))) {
   Check(Flush(reopened)&&reopened.Recording&&!reopened.StorageFailed,"secure owner reopens controlled settings checkpoint records");
  }
  var old=Root("legacy");var directory=Minimum(old);Directory.CreateDirectory(directory);
  foreach(var leaf in new[]{"events-current.jsonl","events-previous.jsonl","session.state","writer.lock","custom-user.txt"})File.WriteAllText(Path.Combine(directory,leaf),"FAKE_UNVERIFIED_"+leaf);
  var hashes=Hashes(directory);using(var owner=DiagnosticOwner.Start(old,false,"1.0.0-rc.2",new string('A',32))){
   Flush(owner);Check(owner.StorageFailed&&!owner.Recording,"legacy unverified ACL/evidence refuses minimum writer");
   owner.SetDetailed(true);Check(owner.Detailed,"requested detail remains independent of refused storage");
   var result=Collect(old,"legacy",Health(owner));using(result.Summary){
    var h=result.Summary.RootElement.GetProperty("liveHostHealth").GetProperty("runtime");
    Check(h.GetProperty("storageFailed").GetBoolean()&&!h.GetProperty("recording").GetBoolean()&&h.GetProperty("requestedDetailed").GetBoolean(),"refused real writer health reaches ZIP despite no event file");
    Check(!result.Entire.Contains("FAKE_UNVERIFIED_"),"legacy unknown contents omitted from collected ZIP");
   }
  }
  Check(hashes.OrderBy(x=>x.Key).SequenceEqual(Hashes(directory).OrderBy(x=>x.Key)),"unknown legacy files all preserved without adoption or migration");
  var local=Root("local-reopen");const string localVersion="1.3.0-rc.3.local.6";
  using(var owner=DiagnosticOwner.Start(local,false,localVersion,new string('D',32))) {
   Check(Flush(owner)&&owner.Recording&&!owner.StorageFailed,"local-version owner starts with secure storage");
   owner.UpdateStatus(DiagnosticFeature.ReleaseCheck,DiagnosticUpdateState.NoCompatibleUpdate,localVersion);
   owner.UiAction(DiagnosticUiAction.FieldZoom,DiagnosticOutcome.Completed,DiagnosticOperation.New());
   Check(Flush(owner)&&owner.Stop(DiagnosticEndReason.OwnerDisposed),"local-version owner durably closes controlled local records");
  }
  using(var owner=DiagnosticOwner.Start(local,false,localVersion,new string('D',32))) {
   Check(Flush(owner)&&owner.Recording&&!owner.StorageFailed,"local-version owner reopens its prior records without storage refusal");
   var result=Collect(local,"local-reopened",Health(owner));using(result.Summary) {
    var records=result.JsonLines.Split('\n').Where(line=>line.Length>0).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).ToArray();
    Check(records.All(row=>row.GetProperty("mod_version").GetString()==localVersion)&&records.Any(row=>row.GetProperty("target_version").GetString()==localVersion),"reopened local mod and target versions survive previous/current ZIP streams");
    Check(records.Any(row=>row.GetProperty("kind").GetString()=="PreviousSession"&&row.GetProperty("outcome").GetString()=="Completed"),"local-version reopen retains verified completed prior-session outcome");
    var record=result.JsonLines.Split('\n').First(line=>line.Length>0);
    foreach(var invalid in new[]{"1.3.0-rc.3.local.0","1.3.0-rc.3.local.1000000","v1.3.0-rc.3.local.6","FAKE_PRIVATE_VERSION"}) Check(!OwnedDiagnosticRecord.TryProject(record.Replace(localVersion,invalid,StringComparison.Ordinal),out _),"owned record version rejects uncontrolled value: "+invalid);
   }
  }
  var absent=Collect(root,"unavailable",null);using(absent.Summary){
   var h=absent.Summary.RootElement.GetProperty("liveHostHealth").GetProperty("runtime");
   Check(h.GetProperty("availability").GetString()=="unavailable"&&h.GetProperty("recording").ValueKind==JsonValueKind.Null&&h.GetProperty("storageFailed").ValueKind==JsonValueKind.Null,"standalone collection does not infer live health from existing logs");
  }
  Console.WriteLine(JsonSerializer.Serialize(new{checks=Cases.Count,cases=Cases,scope="Actual secure runtime/helper/installer owner factories and collector APIs with inert game/log inputs; no real helper/game process, signing, credentials or shell."},new JsonSerializerOptions{WriteIndented=true}));return 0;
 }
}
