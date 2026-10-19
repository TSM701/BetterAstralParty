using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static JsonElement Recovery(Inspection i) => Document(i,"recovery-inventory.json");
    private static JsonElement[] RecoveryRows(Inspection i,string collection) => Recovery(i).GetProperty(collection).EnumerateArray().ToArray();
    private static JsonElement RecoveryFile(Inspection i,string name) => RecoveryRows(i,"files").Single(row=>Text(row,"source")==name);
    private static JsonElement[] RecoveryMatch(Inspection i,string collection,string field,string pattern) => RecoveryRows(i,collection).Where(row=>Regex.IsMatch(Text(row,field),pattern,RegexOptions.CultureInvariant)).ToArray();
    private static int Observations(Inspection i,string code,bool? blocker=null) => RecoveryRows(i,"observations").Count(row=>Text(row,"code")==code&&(!blocker.HasValue||row.GetProperty("potentialBlocker").ValueKind is JsonValueKind.True or JsonValueKind.False&&row.GetProperty("potentialBlocker").GetBoolean()==blocker));
    private static void MetadataOnly(Inspection i,params string[] identities)
    {
        var recovery=Recovery(i);
        Check(!recovery.GetProperty("contentsIncluded").GetBoolean()&&!recovery.GetProperty("installationVerified").GetBoolean()&&!recovery.GetProperty("recoveryAuthenticated").GetBoolean(),"recovery inventory is observation, not ownership or authentication");
        Check(!i.All.Contains("FORBIDDEN",StringComparison.Ordinal)&&!i.All.Contains(Base,StringComparison.Ordinal)&&identities.All(value=>!i.Entries["recovery-inventory.json"].Contains(value,StringComparison.Ordinal)),"raw state contents, private identities and absolute roots excluded");
    }
    private static void FullRecovery()
    {
        foreach(var generation in new[]{"v4","v3"})
        {
            var r=New("recovery-markerless-"+generation);var game=Path.Combine(r.Root,"remnants");Directory.CreateDirectory(game);
            r=r with {Local=Path.Combine(r.Root,"state/BetterAstralParty/Logs")};Directory.CreateDirectory(r.Local);
            Put(Path.Combine(game,"BetterAstralParty.install.json"),"FORBIDDEN_RAW_RECEIPT");
            Put(Path.Combine(game,"BetterAstralParty.update-"+generation+".version"),"FORBIDDEN_RAW_HIGH_WATER");Put(Path.Combine(game,"BetterAstralParty.update-"+generation+".operation"),"FORBIDDEN_RAW_OPERATION");
            Put(Path.Combine(game,"BetterAstralParty-LaunchError.log"),"System.IO.FileNotFoundException: Assembly Example.Safe.dll missing\n");
            Put(Path.Combine(game,"BepInEx/BetterAstralParty-Diagnostics/current.log"),"phase=7\nAuthorization: Bearer FORBIDDEN_RAW_RUNTIME\n");
            Put(Path.Combine(game,"BepInEx/BetterAstralParty-Diagnostics/minimal-v1/helper/events-current.jsonl"),Event("Stage",("feature","Helper"),("phase","Recovery"),("outcome","Begin"),("update_state","RecoveryRequired"))+"\n{\"secret\":\"FORBIDDEN_RAW_MINIMAL\"}\n");
            var workId=new string('a',32);var atomicId=new string('b',32);var work=Path.Combine(game,"bap-"+generation+"-helperauto-"+workId);
            Put(Path.Combine(work,"plan"),"FORBIDDEN_RAW_PLAN");Put(Path.Combine(work,"committed-pending-"+atomicId),"FORBIDDEN_RAW_ATOMIC");Put(Path.Combine(work,"unrelated-secret.json"),"FORBIDDEN_ARBITRARY_FILE");Put(Path.Combine(game,"BepInEx/config/kr.betterastralparty.mod.cfg"),"FORBIDDEN_FULL_CONFIG");
            var credential=Path.Combine(r.Local,"credential-store.json");Put(credential,"FORBIDDEN_CREDENTIAL_STORE");
            var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(game).TrimEnd('\\').ToLowerInvariant())));var backup=Path.Combine(Path.GetDirectoryName(r.Local)!,"Backups",generation,key);
            Put(Path.Combine(backup,"pending.json"),"FORBIDDEN_RAW_INSTALL_PENDING");Put(Path.Combine(backup,atomicId,"completed.json"),"FORBIDDEN_RAW_COMPLETED");Put(Path.Combine(backup,atomicId,"0.bin"),"FORBIDDEN_BACKUP_PAYLOAD");Put(Path.Combine(Path.GetDirectoryName(backup)!,new string('c',64),"pending.json"),"FORBIDDEN_UNRELATED_ROOT");
            var receipt=Path.Combine(game,"BetterAstralParty.install.json");var before=Hash(receipt);Inspection i;
            using(var held=File.Open(credential,FileMode.Open,FileAccess.ReadWrite,FileShare.None))i=Inspect(Collect(r,game));
            Check(!i.Summary.GetProperty("environment").GetProperty("gameRootValidated").GetBoolean()&&!Recovery(i).GetProperty("gameMarkersValidated").GetBoolean(),generation+" markerless evidence never establishes valid game root");
            Check(Text(RecoveryFile(i,"game/BetterAstralParty.install.json"),"sha256")==before&&!RecoveryFile(i,"game/BetterAstralParty-Launcher.exe").GetProperty("present").GetBoolean(),generation+" markerless receipt exact hash and missing launcher");
            Check(RecoveryFile(i,"game/BetterAstralParty.update-"+generation+".version").GetProperty("present").GetBoolean()&&!RecoveryRows(i,"files").Any(row=>Text(row,"source").Contains("highwater",StringComparison.Ordinal)),generation+" actual version filename, no invented highwater alias");
            Check(Observations(i,"work-marker-observed",true)==1&&Observations(i,"unpublished-marker-observed",true)==1,generation+" intent and unpublished markers observed as potential blockers");
            Check(RecoveryRows(i,"files").Count(row=>Text(row,"source").EndsWith("/pending.json",StringComparison.Ordinal)&&row.GetProperty("present").ValueKind==JsonValueKind.True)==1&&RecoveryMatch(i,"files","source","/completed\\.json$").Length==1,generation+" only selected root backup markers included");
            Check(i.Entries.ContainsKey("logs/game-launch-error.log")&&i.All.Contains("Example.Safe.dll",StringComparison.Ordinal)&&i.All.Contains("phase=7",StringComparison.Ordinal)&&Rows(i).Any(row=>Text(row,"source")=="game/minimal-helper-current"&&row.GetProperty("projectedRecords").GetInt32()==1),generation+" markerless owned failures and typed helper events projected");
            MetadataOnly(i,workId,atomicId,key);Check(Hash(receipt)==before&&File.Exists(Path.Combine(backup,"pending.json")),generation+" original receipt and backup preserved");
            Put(Path.Combine(work,"committed"),"not authenticated terminal bytes");i=Inspect(Collect(r,game));Check(Observations(i,"work-marker-observed",false)==1&&!Recovery(i).GetProperty("recoveryAuthenticated").GetBoolean(),generation+" terminal presence is not authenticated completion");
            RecoveryContracts(generation);RecoveryInputs(generation);RecoveryLimits(generation);
        }
        RecoveryHashBudget();RecoveryBackupGenerations();RecoveryContainedWork();
    }
    private static void RecoveryBackupGenerations()
    {
        var r=New("recovery-backup-generations");r=r with {Local=Path.Combine(r.Root,"state/BetterAstralParty/Logs")};Directory.CreateDirectory(r.Local);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(r.Game).TrimEnd('\\').ToLowerInvariant())));
        var hashes=new Dictionary<string,string>();foreach(var generation in new[]{"v4","v3"})
        {
            var directory=Path.Combine(Path.GetDirectoryName(r.Local)!,"Backups",generation,key);var pending=Path.Combine(directory,"pending.json");Put(pending,"FORBIDDEN_"+generation+"_PENDING");hashes.Add(generation,Hash(pending));Put(Path.Combine(directory,new string('a',32),"completed.json"),"FORBIDDEN_"+generation+"_COMPLETED");
        }
        var i=Inspect(Collect(r));Check(Text(RecoveryFile(i,"installer-backups-v4/pending.json"),"sha256")==hashes["v4"]&&Text(RecoveryFile(i,"installer-backups/pending.json"),"sha256")==hashes["v3"],"current and historical backup inventories retain distinct generation provenance");
        Check(RecoveryRows(i,"files").Select(row=>Text(row,"source")).Distinct(StringComparer.Ordinal).Count()==RecoveryRows(i,"files").Length,"mixed-generation backup file aliases never collide");
        Check(RecoveryMatch(i,"files","source","^installer-backups-v4/generation/[0-9]{2}/completed\\.json$").Length==1&&RecoveryMatch(i,"files","source","^installer-backups/generation/[0-9]{2}/completed\\.json$").Length==1,"completed backup aliases remain generation-scoped");MetadataOnly(i,key);
    }
    private static void RecoveryContracts(string generation)
    {
        var r=New("recovery-contracts-"+generation);var n=0;
        foreach(var kind in new[]{"helper","helperbridge","helperauto","wholeintent","txn","stage"})
        {
            var dir=Path.Combine(r.Game,"bap-"+generation+"-"+kind+"-"+(++n).ToString("x32"));Put(Path.Combine(dir,kind=="stage"?"handoff.ticket":"plan"),"FORBIDDEN_TYPED_MARKER");
            if(kind=="wholeintent")Put(Path.Combine(dir,"cancelled"),"synthetic");
            if(kind=="txn"){Put(Path.Combine(dir,"rolled-back"),"synthetic");Put(Path.Combine(dir,"commit-intent"),"synthetic");}
            if(kind=="stage"){Put(Path.Combine(dir,"helper.attempt"),"FORBIDDEN_ATTEMPT_BINDING");Put(Path.Combine(dir,"helper.recovery"),"FORBIDDEN_RECOVERY_BINDING");}
        }
        Put(Path.Combine(r.Game,"bap-"+generation+"-txn-not-a-native-name/plan"),"FORBIDDEN_NON_CONTRACT");
        foreach(var pair in new[]{("BetterAstralParty.reinstall-"+generation+".completed-",'d'),("BetterAstralParty.update-"+generation+".version.uninstalled-",'e'),("bap-"+generation+"-transition-",'f')})Put(Path.Combine(r.Game,pair.Item1+new string(pair.Item2,32)),"FORBIDDEN_GENERATION");
        var i=Inspect(Collect(r));Check(Observations(i,"work-marker-observed")==6&&Observations(i,"work-marker-observed",false)==2,generation+" all native work kinds, cancellation and rollback covered");
        Check(RecoveryMatch(i,"files","source","/helper\\.(attempt|recovery)$").Count(row=>Text(row,"status")=="hashed"&&row.GetProperty("present").GetBoolean())==2,generation+" attempt and recovery markers metadata only");
        Check(RecoveryMatch(i,"files","source","reinstall-completed|archived-version|game/transition/").Length==3&&!i.Entries["recovery-inventory.json"].Contains("not-a-native-name",StringComparison.Ordinal),generation+" generation aliases hide IDs and exclude non-contract names");MetadataOnly(i);
    }
    private static void RecoveryInputs(string generation)
    {
        var r=New("recovery-helperauto-inputs-"+generation);var id=new string('a',32);var atomicId=new string('b',32);var work=Path.Combine(r.Game,"bap-"+generation+"-helperauto-"+id);Put(Path.Combine(work,"plan"),"FORBIDDEN_HELPER_AUTO_PLAN");
        var inputs=new[]{"incoming-descriptor","incoming-signature","incoming-image","old-image","old-receipt"};var hashes=new Dictionary<string,string>();
        foreach(var name in inputs){var p=Path.Combine(work,name);Put(p,"FORBIDDEN_HELPER_AUTO_"+name);hashes.Add(name,Hash(p));Put(Path.Combine(work,name+"-pending-"+atomicId),"FORBIDDEN_HELPER_ATOMIC_"+name);}
        Put(Path.Combine(work,"incoming-token-pending-"+atomicId),"FORBIDDEN_TOKEN_PAYLOAD");Put(Path.Combine(work,"incoming-image-pending-"+new string('E',32)),"FORBIDDEN_UPPERCASE_ALIAS");Put(Path.Combine(work,"incoming.descriptor"),"FORBIDDEN_DOTTED_ALIAS");
        var other=Path.Combine(r.Game,"bap-"+generation+"-helper-"+new string('c',32));Put(Path.Combine(other,"incoming-image"),"FORBIDDEN_OTHER_KIND");Put(Path.Combine(other,"incoming-image-pending-"+atomicId),"FORBIDDEN_OTHER_KIND");
        var i=Inspect(Collect(r));foreach(var name in inputs)
        {
            var row=RecoveryMatch(i,"files","source","^game/helperauto/[0-9]{2}/"+name+"$").Single();var atomic=RecoveryMatch(i,"files","source","^game/helperauto/[0-9]{2}/"+name+"-pending/[0-9]{2}$").Single();
            Check(Text(row,"status")=="hashed"&&Text(row,"sha256")==hashes[name]&&row.GetProperty("sizeBytes").GetInt64()==new FileInfo(Path.Combine(work,name)).Length,generation+" helperauto exact input metadata "+name);
            Check(Text(atomic,"status")=="hashed"&&Text(atomic,"sha256")==Hash(Path.Combine(work,name+"-pending-"+atomicId)),generation+" helperauto atomic input metadata "+name);Check(Hash(Path.Combine(work,name))==hashes[name],generation+" helperauto evidence unchanged "+name);
        }
        Check(Observations(i,"unpublished-marker-observed")==5&&RecoveryMatch(i,"files","source","^game/helper/[0-9]{2}/incoming-").Length==0&&!i.All.Contains("incoming-token",StringComparison.Ordinal)&&!i.All.Contains("incoming.descriptor",StringComparison.Ordinal),generation+" helperauto exact names do not broaden other kinds");
        Check(LogBytes(i)==0&&!i.Summary.GetProperty("recoveryInventory").GetProperty("contentsIncluded").GetBoolean(),generation+" helper inputs do not consume log quota");MetadataOnly(i,id,atomicId);
        r=New("recovery-helper-repair-"+generation);id=new string('1',32);atomicId=new string('2',32);work=Path.Combine(r.Game,"bap-"+generation+"-helper-"+id);
        foreach(var name in new[]{"plan","committed","ordinary-repair","ordinary-repair-pending-"+atomicId})Put(Path.Combine(work,name),"FORBIDDEN_REPAIR_"+name);var repairHash=Hash(Path.Combine(work,"ordinary-repair"));var atomicHash=Hash(Path.Combine(work,"ordinary-repair-pending-"+atomicId));
        Put(Path.Combine(work,"ordinary.repair"),"FORBIDDEN_DOTTED_ALIAS");Put(Path.Combine(work,"ordinary-repair-pending-"+new string('A',32)),"FORBIDDEN_UPPERCASE_ALIAS");foreach(var kind in new[]{"helperauto","helperbridge"}){other=Path.Combine(r.Game,"bap-"+generation+"-"+kind+"-"+new string('3',32));Put(Path.Combine(other,"ordinary-repair"),"FORBIDDEN_OTHER_KIND");Put(Path.Combine(other,"ordinary-repair-pending-"+atomicId),"FORBIDDEN_OTHER_KIND");}
        i=Inspect(Collect(r));var repair=RecoveryMatch(i,"files","source","^game/helper/[0-9]{2}/ordinary-repair$").Single();var pending=RecoveryMatch(i,"files","source","^game/helper/[0-9]{2}/ordinary-repair-pending/[0-9]{2}$").Single();
        Check(Text(repair,"status")=="hashed"&&Text(repair,"sha256")==repairHash&&repair.GetProperty("sizeBytes").GetInt64()==new FileInfo(Path.Combine(work,"ordinary-repair")).Length,generation+" ordinary repair exact size and digest");
        Check(Text(pending,"status")=="hashed"&&Text(pending,"sha256")==atomicHash&&Observations(i,"unpublished-marker-observed")==1,generation+" only canonical ordinary repair atomic marker");
        Check(Hash(Path.Combine(work,"ordinary-repair"))==repairHash&&Hash(Path.Combine(work,"ordinary-repair-pending-"+atomicId))==atomicHash&&RecoveryMatch(i,"files","source","^game/(helperauto|helperbridge)/[0-9]{2}/ordinary-repair").Length==0&&!i.All.Contains("ordinary.repair",StringComparison.Ordinal),generation+" repair originals and other kinds preserved");Check(LogBytes(i)==0,generation+" repair metadata has independent quota");MetadataOnly(i,id,atomicId);
    }
    private static void RecoveryLimits(string generation)
    {
        foreach(var kind in new[]{"helper","helperauto"})
        {
            var r=New("recovery-atomic-limit-"+generation+"-"+kind);var work=Path.Combine(r.Game,"bap-"+generation+"-"+kind+"-"+new string('4',32));Put(Path.Combine(work,"plan"),"FORBIDDEN_PLAN");
            for(var n=0;n<65;n++)Put(Path.Combine(work,(kind=="helper"?"ordinary-repair":"incoming-image")+"-pending-"+n.ToString("x32")),"FORBIDDEN_ATOMIC");
            var i=Inspect(Collect(r));var limit=RecoveryMatch(i,"roots","source","^game/"+kind+"/[0-9]{2}/atomic-markers$").Single();Check(limit.GetProperty("matchedEntries").GetInt32()==65&&limit.GetProperty("limitOmitted").GetInt32()==1&&Observations(i,"unpublished-marker-observed")==64,generation+" "+kind+" per-directory atomic cap and explicit omissions");MetadataOnly(i);
        }
        var roots=New("recovery-global-limit-"+generation);foreach(var id in new[]{'e','f'}){var work=Path.Combine(roots.Game,"bap-"+generation+"-helperauto-"+new string(id,32));Put(Path.Combine(work,"plan"),"FORBIDDEN_PLAN");for(var n=0;n<40;n++)Put(Path.Combine(work,"incoming-signature-pending-"+n.ToString("x32")),"FORBIDDEN_ATOMIC");}
        var snapshot=Inspect(Collect(roots));Check(Observations(snapshot,"unpublished-marker-observed")==64&&RecoveryRows(snapshot,"roots").Count(row=>Text(row,"status")=="file-limit")==1,generation+" global atomic cap across directories");MetadataOnly(snapshot);
        roots=New("recovery-links-"+generation);var outside=Path.Combine(roots.Root,"outside");Put(Path.Combine(outside,"plan"),"FORBIDDEN_LINK_TARGET");Junction(Path.Combine(roots.Game,"bap-"+generation+"-helperauto-"+new string('1',32)),outside);HardLink(Path.Combine(roots.Game,"BetterAstralParty.install.json"),Path.Combine(outside,"plan"));var locked=Path.Combine(roots.Game,"BetterAstralParty.update-"+generation+".version");Put(locked,"FORBIDDEN_LOCKED_WATER");using(var held=File.Open(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None))snapshot=Inspect(Collect(roots));
        Check(Text(RecoveryFile(snapshot,"game/BetterAstralParty.install.json"),"status")=="unsafe-or-denied"&&RecoveryFile(snapshot,"game/BetterAstralParty.install.json").GetProperty("sha256").ValueKind==JsonValueKind.Null,generation+" hard-linked recovery evidence never hashed");
        Check(Text(RecoveryFile(snapshot,"game/BetterAstralParty.update-"+generation+".version"),"status")=="locked-or-unreadable"&&RecoveryFile(snapshot,"game/BetterAstralParty.update-"+generation+".version").GetProperty("present").ValueKind==JsonValueKind.Null,generation+" busy recovery evidence unknown, not absent");Check(RecoveryMatch(snapshot,"roots","source","^game/helperauto/").Count(row=>Text(row,"status")=="unsafe-or-unreadable")==1,generation+" linked work directory never traversed");MetadataOnly(snapshot);
        roots=New("recovery-directory-limit-"+generation);for(var n=0;n<65;n++)Directory.CreateDirectory(Path.Combine(roots.Game,"bap-"+generation+"-wholeintent-"+n.ToString("x32")));snapshot=Inspect(Collect(roots));var row=RecoveryRows(snapshot,"roots").Single(row=>Text(row,"source")=="game/work-directories");Check(row.GetProperty("matchedEntries").GetInt32()==65&&row.GetProperty("limitOmitted").GetInt32()==1&&Observations(snapshot,"work-marker-observed")==64,generation+" recovery directories capped at64");
        roots=New("recovery-scan-limit-"+generation);for(var n=0;n<513;n++)Put(Path.Combine(roots.Game,"unrelated-"+n),"FORBIDDEN_UNUSED");snapshot=Inspect(Collect(roots));row=RecoveryRows(snapshot,"roots").Single(row=>Text(row,"source")=="game/work-directories");Check(row.GetProperty("scannedEntries").GetInt32()==512&&Text(row,"status")=="directory-scan-limit"&&!snapshot.Entries["recovery-inventory.json"].Contains("unrelated-",StringComparison.Ordinal),generation+" top-level recovery scan bounded, unrelated names excluded");
    }
    private static void RecoveryContainedWork()
    {
        var r=New("recovery-contained-v4");var container=Path.Combine(r.Game,"BetterAstralParty.Update");var id=new string('a',32);var hashes=new Dictionary<string,string>();
        foreach(var kind in new[]{"stage","txn","helper","helperauto","helperbridge","wholeintent","wholehist"}) {
            var marker=kind=="stage"?"handoff.ticket":"plan";var file=Path.Combine(container,"bap-v4-"+kind+"-"+id,marker);Put(file,"FORBIDDEN_CONTAINED_"+kind);hashes[kind]=Hash(file);
        }
        Put(Path.Combine(container,"bap-v4-helper-"+id,"ordinary-repair-pending-"+new string('b',32)),"FORBIDDEN_ATOMIC");
        var transition=Path.Combine(container,"bap-v4-transition-"+new string('c',32));Put(transition,"FORBIDDEN_TRANSITION");
        Put(Path.Combine(container,"user-subtree/user.keep"),"FORBIDDEN_UNKNOWN");
        var i=Inspect(Collect(r));
        foreach(var pair in hashes) {
            var marker=pair.Key=="stage"?"handoff.ticket":"plan";var row=RecoveryMatch(i,"files","source","^game/work-v4/"+pair.Key+"/[0-9]{2}/"+Regex.Escape(marker)+"$").Single();
            Check(Text(row,"status")=="hashed"&&Text(row,"sha256")==pair.Value,"contained selected-root exact metadata and provenance: "+pair.Key);
        }
        Check(Observations(i,"work-marker-observed")==7&&Observations(i,"transition-marker-observed",true)==1&&Observations(i,"unpublished-marker-observed",true)==1,"contained v4 work, transition and atomic markers included without contents");
        Check(RecoveryRows(i,"roots").Any(row=>Text(row,"source")=="game/work-v4"&&Text(row,"status")=="pinned-contained-selected-root")&&!i.Entries["recovery-inventory.json"].Contains("user-subtree",StringComparison.Ordinal),"contained provenance is distinct and unknown descendants are not inventoried");MetadataOnly(i,id);
        r=New("recovery-contained-shared-quota");container=Path.Combine(r.Game,"BetterAstralParty.Update");
        for(var n=0;n<32;n++)Put(Path.Combine(r.Game,"bap-v4-helper-"+n.ToString("x32"),"plan"),"FORBIDDEN_FLAT");
        for(var n=0;n<33;n++)Put(Path.Combine(container,"bap-v4-helperauto-"+n.ToString("x32"),"plan"),"FORBIDDEN_CONTAINED");
        i=Inspect(Collect(r));var omitted=RecoveryRows(i,"roots").Single(row=>Text(row,"source")=="game/work-v4/directories");
        Check(Observations(i,"work-marker-observed")==64&&omitted.GetProperty("matchedEntries").GetInt32()==33&&omitted.GetProperty("limitOmitted").GetInt32()==1,"flat and contained work share one 64-directory budget with explicit omission");MetadataOnly(i);
        foreach(var kind in new[]{"parent-link","work-link","transition-hardlink"}) {
            r=New("recovery-contained-"+kind);container=Path.Combine(r.Game,"BetterAstralParty.Update");var outside=Path.Combine(r.Root,"outside");Put(Path.Combine(outside,"plan"),"FORBIDDEN_FOREIGN");
            if(kind=="parent-link")Junction(container,outside);
            else if(kind=="work-link"){Directory.CreateDirectory(container);Junction(Path.Combine(container,"bap-v4-helper-"+id),outside);}
            else{Directory.CreateDirectory(container);HardLink(Path.Combine(container,"bap-v4-transition-"+id),Path.Combine(outside,"plan"));}
            var saved=Hash(Path.Combine(outside,"plan"));i=Inspect(Collect(r));
            Check(!RecoveryRows(i,"files").Any(row=>Text(row,"source").StartsWith("game/work-v4/",StringComparison.Ordinal)&&Text(row,"status")=="hashed")&&Hash(Path.Combine(outside,"plan"))==saved,"contained "+kind+" is not traversed/hashed and foreign data stays intact");
            Check(kind=="parent-link"?RecoveryRows(i,"roots").Any(row=>Text(row,"source")=="game/work-v4"&&Text(row,"status")=="unsafe-or-unreadable"):kind=="work-link"?RecoveryRows(i,"roots").Any(row=>Text(row,"source").StartsWith("game/work-v4/helper/",StringComparison.Ordinal)&&Text(row,"status")=="unsafe-or-unreadable"):RecoveryMatch(i,"files","source","^game/work-v4/transition/").All(row=>Text(row,"status")=="unsafe-or-denied"),"contained unsafe evidence has explicit failure provenance: "+kind);MetadataOnly(i,id);
        }
    }
    private static void RecoveryHashBudget()
    {
        var r=New("recovery-hash-budget");var gameBytes=new FileInfo(Path.Combine(r.Game,"AstralParty_INT.exe")).Length;
        Sized(Path.Combine(r.Game,"AstralParty-Vanilla.cmd"),33554432-gameBytes);var launcher=Path.Combine(r.Game,"BetterAstralParty-Launcher.exe");Sized(launcher,33554432);Put(Path.Combine(r.Game,"BetterAstralParty.install.json"),"small receipt");
        var i=Inspect(Collect(r));Check(Recovery(i).GetProperty("hashReservedBytes").GetInt64()==67108864&&Text(RecoveryFile(i,"game/BetterAstralParty.install.json"),"status")=="hash-budget-limit","recovery hash budget independent and bounded");
        using(var stream=File.Open(launcher,FileMode.Open,FileAccess.Write,FileShare.Read))stream.SetLength(67108865);i=Inspect(Collect(r));Check(Text(RecoveryFile(i,"game/BetterAstralParty-Launcher.exe"),"status")=="file-size-limit"&&RecoveryFile(i,"game/BetterAstralParty-Launcher.exe").GetProperty("sha256").ValueKind==JsonValueKind.Null,"oversized owned file retains metadata without digest");
    }
}
