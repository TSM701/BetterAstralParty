using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Diagnostics;

namespace BetterAstralParty;

// Real Compatibility.Block and DiagnosticLog run; game/native calls and BepInEx logger are inert.
internal static class Program
{
    private static void Main()
    {
        var root=Path.Combine(Path.GetTempPath(),"bap-diag-b3-producer-"+Guid.NewGuid().ToString("N")[..8]);
        var game=Path.Combine(root,"g");Directory.CreateDirectory(Path.Combine(game,"AstralParty_INT_Data"));
        File.WriteAllText(Path.Combine(game,"AstralParty_INT.exe"),"inert never executed");
        var folder=Path.Combine(game,"BepInEx","BetterAstralParty-Diagnostics");
        Plugin.Diagnostics=new DiagnosticLog(folder,_=>throw new Exception("Synthetic writer failed"));
        Plugin.Diagnostics.SetEnabled(true,"mod=1.0.0-rc.2");
        var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        Exception same;
        try{NativeUi.Call();throw new Exception("fixture did not throw");}catch(ArgumentException error){same=error;}
        var cleanup=0;
        // Exact call expressions from ShushuShieldUi/ModUi and BattleStatusUi, with inert cleanup only.
        Compatibility.Block(ShushuShieldUi.Feature,same,()=>cleanup++);
        Compatibility.Block("BattleStatus",same,()=>cleanup++);
        foreach(var key in RegisteredKeys.Values.Where(key=>key is not ("ShushuShield" or "BattleStatus")))
            Compatibility.Block(key,same,()=>cleanup++);
        var revision=Compatibility.Revision;
        Compatibility.Block(ShushuShieldUi.Feature,same,()=>cleanup++);
        Check(revision==15&&Compatibility.Revision==15&&cleanup==15,"real breaker emits once per registered feature and preserves cleanup/revision semantics");
        foreach(var key in new[]{"shushushield","ShushuShieldUi","Bearer_FAKE_ACCOUNT_FEATURE"}) Compatibility.Block(key,same,()=>cleanup++);
        Plugin.Diagnostics.Error("Compatibility.ShushuShield",new MissingMethodException("FAKE_PRIVATE_MESSAGE"));
        Plugin.Diagnostics.Error("Compatibility.BattleStatus",new FAKE_TOKEN_BRIDGE_Exception("FAKE_PRIVATE_MESSAGE"));
        Plugin.Diagnostics.Flush();
        var raw=Path.Combine(folder,"current.log");
        byte[] ReadRaw(){using var input=new FileStream(raw,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var bytes=new MemoryStream();input.CopyTo(bytes);return bytes.ToArray();}
        var before=SHA256.HashData(ReadRaw());
        var rawText=Encoding.UTF8.GetString(ReadRaw());
        Check(rawText.Contains("error.Compatibility."+ShushuShieldUi.Feature+"=System.ArgumentException")&&rawText.Contains("error.Compatibility.BattleStatus=System.ArgumentException"),"real Compatibility.Block and DiagnosticLog produce actual feature-key envelopes");
        BundleResult Collect()=>DiagnosticBundle.Collect(game,"","","",Path.Combine(root,"hub"),Path.Combine(root,"hub2"),"6.0");
        var bundle=Collect();string export,all="";string summaryText;
        using(var zip=ZipFile.OpenRead(bundle.ArchivePath))
        {
            using(var reader=new StreamReader(zip.GetEntry("logs/game-diagnostics-current.log")!.Open()))export=reader.ReadToEnd();
            using(var reader=new StreamReader(zip.GetEntry("summary.json")!.Open()))summaryText=reader.ReadToEnd();
            foreach(var entry in zip.Entries){using var reader=new StreamReader(entry.Open());all+=reader.ReadToEnd();}
        }
        var events=export.Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(line=>line.Contains("managed-error;")).Select(line=>line[(line.IndexOf(" | ",StringComparison.Ordinal)+3)..]).ToArray();
        string Find(string key,string type="ArgumentException")=>events.Single(line=>line.Contains("legacy_feature=Compatibility"+key+";")&&line.Contains("error_type="+type+";"));
        var shield=Find("ShushuShield");var battle=Find("BattleStatus");
        Check(shield!=battle&&shield.Contains("feature=PublicCombat;")&&battle.Contains("feature=PublicCombat;")&&shield.EndsWith("error_site=NativeUi")&&battle.EndsWith("error_site=NativeUi"),"same real exception/site retains distinct ShushuShield and BattleStatus breaker codes in ZIP");
        foreach(var key in RegisteredKeys.Values)
        {
            var feature=key switch{"ShushuShield" or "BattleStatus" or "FieldBuffs" or "Enabled"=>"PublicCombat","HandLayout"=>"HandLayout","UpdateNotification" or "Notices"=>"Notices",_=>"CoreUi"};
            Check(Find(key).Contains("feature="+feature+";"),"audited actual producer key classified: "+key);
        }
        Check(events.Count(line=>line.Contains("feature=Unknown; legacy_feature=Unknown;"))==3,"unregistered/case-mismatched/type-name labels remain safe Unknown events");
        Check(Find("ShushuShield","MissingMethodException").Contains("type=System.MissingMethodException;")&&Find("BattleStatus","Unknown").Contains("type=Unknown;"),"registered functionality retained for known and unsupported exception classes");
        Check(!all.Contains("FAKE_")&&!all.Contains(root)&&!all.Contains(" at ")&&!all.Contains("Bearer_")&&!all.Contains("FAKE_TOKEN_BRIDGE_Exception"),"ZIP excludes raw unknown labels/types/messages/stack/private path canaries");
        Check(before.SequenceEqual(SHA256.HashData(ReadRaw()))&&Plugin.Diagnostics.IsRecording,"collection preserves original producer bytes and running writer");
        using(var summary=JsonDocument.Parse(summaryText))
        {
            var row=summary.RootElement.GetProperty("files").EnumerateArray().Single(row=>row.GetProperty("source").GetString()=="game/diagnostics-current");
            Check(row.GetProperty("status").GetString()=="included"&&row.GetProperty("projectedRecords").GetInt32()>=20,"actual producer error occurrences exported with row counters");
        }
        Plugin.Diagnostics.SetEnabled(false,"fixture complete");
        var report=JsonSerializer.Serialize(new{runtime=Environment.Version.ToString(),passed=checks.Count,cases=checks,scratch=root,syntheticZip=bundle.ArchivePath,producerSources=new[]{"src/Compatibility.cs","src/DiagnosticLog.cs"},auditedKeys=RegisteredKeys.Values,realLogsRead=false,realGameRun=false,nativeUiExecuted=false,shellDispatched=false,upload=false},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(root,"test-results.json"),report,new UTF8Encoding(false));Console.WriteLine(report);
    }
}
internal static class Plugin {internal static DiagnosticLog Diagnostics=null!;internal static InertLogger Logger=new();}
internal sealed class InertLogger {internal void LogWarning(string value){} }
internal static class ModText {internal static string Text(string value)=>value;}
internal static class NativeUi
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Call()=>throw new ArgumentException("FAKE_AUTH_TOKEN C:\\Users\\FAKE_ACCOUNT\\private");
}
internal sealed class FAKE_TOKEN_BRIDGE_Exception:Exception {internal FAKE_TOKEN_BRIDGE_Exception(string message):base(message){} }
// The unrelated combat API is never called by this fixture.
internal readonly struct HitModifiers {}
