using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Diagnostics;

internal static partial class Program
{
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Document(Inspection inspection,string name)=>JsonDocument.Parse(inspection.Entries[name]).RootElement.Clone();
    private static IEnumerable<JsonElement> Rows(Inspection inspection)=>inspection.Summary.GetProperty("files").EnumerateArray();
    private static long LogBytes(Inspection inspection)=>inspection.Entries.Where(pair=>pair.Key.StartsWith("logs/",StringComparison.Ordinal)).Sum(pair=>(long)Encoding.UTF8.GetByteCount(pair.Value));
    private static BundleResult Collect(Roots roots,string game,string host="8.0",string version="")=>DiagnosticBundle.Collect(game,roots.Local,roots.Fallback,roots.Unity,roots.Hub,roots.Hub2,host,version);
    private static void FullLogs()
    {
        var r=New("privacy");var p=Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log");
        Put(p,"""
2026-10-04T10:00:00Z | phase=3
Authorization: Bearer FAKE_BEARER_NEVER_VALID
 FAKE_CONTINUED_SECRET
2026-10-04T10:00:01Z | performance frames=42
access_token = FAKE_ACCESS_NEVER_VALID
refresh_token = FAKE_REFRESH_NEVER_VALID
Cookie: FAKE_COOKIE_NEVER_VALID
signature = FAKE_SIGNATURE_NEVER_VALID
client_secret = FAKE_CLIENT_SECRET_NEVER_VALID
nickname=FAKE_NICKNAME
userId=FAKE_ACCOUNT
C:\Users\FAKE_PERSON\Some Folder\private.log
https://fake.invalid/?access_token=FAKE_URL_SECRET
fake_person@example.invalid 192.0.2.23 76561198000000000
12345678-1234-1234-1234-123456789abc
-----BEGIN PRIVATE KEY-----
FAKE_KEY_MATERIAL
2026-10-04T10:00:12Z | FAKE_KEY_CONTINUATION
-----END PRIVATE KEY-----
2026-10-04T10:00:13Z | fightStep=9
"""+"\n");var before=Hash(p);
        foreach(var pair in new[]{("BepInEx/config/kr.betterastralparty.mod.cfg","FORBIDDEN_CONFIGURATION"),("crash.dmp","FORBIDDEN_MEMORY")})Put(Path.Combine(r.Game,pair.Item1),pair.Item2);
        Put(Path.Combine(r.Local,"credential-store.json"),"FORBIDDEN_CREDENTIAL_STORE");Put(Path.Combine(r.Local,"arbitrary.log"),"FORBIDDEN_ARBITRARY_LOG");Put(Path.Combine(r.Local,"Launch-"+new string('a',32)+".log"),"BAP-LAUNCH-FAILED\npassword=FAKE_PASSWORD\n");Put(Path.Combine(r.Unity,"Player.log"),"[Info: Unity] startup\nusername=FAKE_USERNAME");
        var bundle=Collect(r);var i=Inspect(bundle);Check(bundle.Included==2&&Text(FileRow(i,"unity/player"),"status")=="metadata-only","mod and launcher projected but Unity metadata only");
        foreach(var marker in new[]{"FAKE_BEARER","FAKE_CONTINUED","FAKE_ACCESS","FAKE_REFRESH","FAKE_COOKIE","FAKE_SIGNATURE","FAKE_CLIENT_SECRET","FAKE_NICKNAME","FAKE_ACCOUNT","FAKE_PERSON","FAKE_URL_SECRET","fake_person@","192.0.2.23","76561198000000000","12345678-1234-1234-1234-123456789abc","FAKE_KEY","FAKE_PASSWORD","FAKE_USERNAME","FORBIDDEN_"})Check(!i.All.Contains(marker,StringComparison.Ordinal),"privacy excludes "+marker);
        Check(i.All.Contains("public-phase; phase=3",StringComparison.Ordinal)&&i.All.Contains("fightStep=9",StringComparison.Ordinal),"allowed public state survives");Check(i.Summary.GetProperty("environment").GetProperty("gameRootValidated").GetBoolean()&&Text(i.Summary.GetProperty("environment"),"hostVersion")=="8.0","safe environment summary");Check(!i.All.Contains(Base,StringComparison.Ordinal)&&!i.All.Contains(r.Game,StringComparison.Ordinal),"absolute roots omitted");Check(i.Summary.GetProperty("missingOrOmitted").GetArrayLength()>0,"missing inputs reported");Check(!i.Entries.Keys.Any(name=>name.Contains("..",StringComparison.Ordinal)||name.IndexOfAny([':', '\\'])>=0||name.StartsWith('/')),"ZIP names safe");Check(Hash(p)==before,"source hash preserved");Check(!Directory.EnumerateFiles(r.Hub,"*.partial").Any(),"committed ZIP has no partial output");var second=Collect(r);Check(second.ArchivePath!=bundle.ArchivePath&&File.Exists(bundle.ArchivePath),"new collection never overwrites existing ZIP");Check(!Directory.EnumerateFiles(r.Hub,"*.partial").Any(),"existing guide cleanup preserves committed ZIP");
        var type=typeof(DiagnosticBundle).GetNestedType("Fence",BindingFlags.NonPublic)!;var constructor=type.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single();var open=type.GetMethod("Open",BindingFlags.Instance|BindingFlags.NonPublic)!;
        using(var fence=(IDisposable)constructor.Invoke([r.Game]))using(var held=(IDisposable)open.Invoke(fence,["BepInEx/BetterAstralParty-Diagnostics/current.log",false,false])!){File.Move(p,p+".rotation");Check(File.Exists(p+".rotation"),"source handle allows writer rotation");var blocked=false;try{Directory.Move(Path.Combine(r.Game,"BepInEx"),Path.Combine(r.Game,"BepInEx.moved"));}catch(IOException){blocked=true;}catch(UnauthorizedAccessException){blocked=true;}Check(blocked,"ancestor identity pinned during read");}File.Move(p+".rotation",p);
        r=New("locked");p=Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log");Put(p,"phase=3\n");using(var held=File.Open(p,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(Text(FileRow(Inspect(Collect(r)),"game/diagnostics-current"),"status")=="locked-or-unreadable","exclusive lock is omission not ZIP failure");using(var writer=File.Open(p,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))Check(Collect(r).Included==1&&writer.CanWrite,"running writer stays writable");File.Move(p,p+".moved");Check(File.Exists(p+".moved"),"all collector source handles released");
        r=New("times");p=Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log");Put(p,"safe old fixture");File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddDays(-8));var previous=Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/previous.log");Put(previous,"safe future fixture");File.SetLastWriteTimeUtc(previous,DateTime.UtcNow.AddDays(2));i=Inspect(Collect(r));Check(i.All.Contains("older-than-7-days",StringComparison.Ordinal)&&i.All.Contains("invalid-future-time",StringComparison.Ordinal)&&!i.All.Contains("safe old fixture",StringComparison.Ordinal),"old/future source bounds");
        r=New("input-limit");p=Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log");Put(p,new string('x',2097153));i=Inspect(Collect(r));Check(i.All.Contains("input-file-limit",StringComparison.Ordinal)&&!i.All.Contains(new string('x',100),StringComparison.Ordinal),"oversize file omitted before tail read");previous=Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/previous.log");File.WriteAllBytes(previous,[0,255,254,0]);Check(Inspect(Collect(r)).All.Contains("invalid-text",StringComparison.Ordinal),"binary log omitted");
        r=New("output-limit");Put(Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log"),string.Concat(Enumerable.Repeat("performance frames=5; ui_avg_ms=4\n",50000)));i=Inspect(Collect(r));Check(LogBytes(i)<=524288&&Rows(i).Count(row=>row.GetProperty("truncated").GetBoolean())==1,"sanitized per-file cap and truncation");
        r=New("long-line");Put(Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log"),new string('L',17000)+"\nFAKE_LONG_CONTINUATION\n2026-10-04T10:00:00Z | fightStep=5\n");i=Inspect(Collect(r));Check(!i.All.Contains("FAKE_LONG_CONTINUATION",StringComparison.Ordinal)&&i.All.Contains("fightStep=5",StringComparison.Ordinal),"oversized line recovers at safe boundary");
        r=New("recent-count");for(var n=0;n<12;n++)Put(Path.Combine(r.Local,"Launch-"+n.ToString("x32")+".log"),"BAP-LAUNCH-FAILED\n");bundle=Collect(r);i=Inspect(bundle);Check(bundle.Included==8&&i.All.Contains("recent-file-limit",StringComparison.Ordinal),"recent file count capped");
        r=New("raw-budget");foreach(var directory in new[]{r.Local,r.Fallback})for(var n=0;n<8;n++)Put(Path.Combine(directory,"Launch-"+n.ToString("x32")+".log"),string.Concat(Enumerable.Repeat("public sample n=5\n",65000)));bundle=Collect(r);i=Inspect(bundle);Check(LogBytes(i)<=4194304&&i.All.Contains("bundle-limit",StringComparison.Ordinal)&&bundle.Included<16,"raw and output total budgets");
        r=New("scan-bound");for(var n=0;n<513;n++)Put(Path.Combine(r.Local,n.ToString("D4")+".txt"),"FAKE_EXCLUDED_SENTINEL");i=Inspect(Collect(r));Check(i.All.Contains("directory-scan-limit",StringComparison.Ordinal)&&!i.All.Contains("FAKE_EXCLUDED_SENTINEL",StringComparison.Ordinal),"nonrecursive enumeration bounded");
        r=New("invalid-root");foreach(var invalid in new[]{r.Game+"\\..\\g",@"\\fake.invalid\share\game",r.Game+":stream",r.Game+"."})Check(!Inspect(Collect(r,invalid)).Summary.GetProperty("environment").GetProperty("gameRootValidated").GetBoolean(),"unsafe game identity refused without access");
        r=New("write-failure");Put(r.Hub,"occupied");Put(r.Hub2,"occupied");var rejected=false;try{Collect(r);}catch{rejected=true;}Check(rejected&&File.ReadAllText(r.Hub)=="occupied"&&File.ReadAllText(r.Hub2)=="occupied","unavailable hubs fail preserving existing bytes");
        r=New("missing-game");i=Inspect(Collect(r,"","PRIVATE_INVALID_HOST"));Check(!i.Summary.GetProperty("environment").GetProperty("gameRootValidated").GetBoolean()&&Text(i.Summary.GetProperty("environment"),"hostVersion")=="unknown"&&!i.All.Contains("PRIVATE_INVALID_HOST",StringComparison.Ordinal),"install-only safe ZIP without game or host validation");
        r=New("file-count");foreach(var path in new[]{"current.log","previous.log","combat-mismatch.log","combat-mismatch-previous.log"})Put(Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics",path),"phase=3\n");Put(Path.Combine(r.Game,"BetterAstralParty-Compatibility.log"),"Allowed: True\n");Put(Path.Combine(r.Game,"BetterAstralParty-LaunchError.log"),"BAP-LAUNCH-FAILED\n");Put(Path.Combine(r.Game,"BepInEx/LogOutput.log"),"raw never read");foreach(var directory in new[]{r.Local,r.Fallback})for(var n=0;n<8;n++)Put(Path.Combine(directory,"Launch-"+n.ToString("x32")+".log"),"BAP-LAUNCH-FAILED\n");Put(Path.Combine(r.Unity,"Player.log"),"safe Unity");Put(Path.Combine(r.Unity,"Player-prev.log"),"safe previous");bundle=Collect(r);i=Inspect(bundle);Check(bundle.Included==22&&Rows(i).Count(row=>Text(row,"status")=="metadata-only")==3&&bundle.Included<=24,"content count and engine-only metadata sources");
        r=New("unicode");p=Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log");Put(p,"");File.WriteAllText(p,"2026-10-04T10:00:00Z | phase=8\n토큰=FAKE_KOREAN_KEY\n",Encoding.Unicode);i=Inspect(Collect(r));Check(i.All.Contains("public-phase; phase=8",StringComparison.Ordinal)&&!i.All.Contains("FAKE_KOREAN_KEY",StringComparison.Ordinal)&&i.All.Contains("로컬 진단",StringComparison.Ordinal),"UTF16 and bilingual text retain safe glyphs");
        r=New("more-secrets");Put(Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log"),"[Error] ghp_FAKE_GITHUB_NEVER_VALID\n[Error] github_pat_FAKE_NEVER_VALID\n[Error] eyJFAKE.eyJFAKE.signatureFAKE\n[Error] 2001:db8::1\n[Error] /home/fake_person/private/file\n[Error] account:\nFAKE_MULTILINE_ACCOUNT\n");i=Inspect(Collect(r));Check(!i.All.Contains("FAKE_",StringComparison.Ordinal)&&!i.All.Contains("2001:db8",StringComparison.Ordinal)&&!i.All.Contains("/home/fake_person",StringComparison.Ordinal),"GitHub/JWT/IPv6/Unix and multiline secrets excluded");
        FullErrorProjection();FullVersionProjection();FullSettingsProjection();FullSteamExitFailure();
    }
    private static void FullErrorProjection()
    {
        var r=New("private-game-data");var text="""
2026-10-04T10:00:00Z | cardEffect.123456=native; min=FAKE_PRIVATE_CARD; max=8
2026-10-04T10:00:01Z | cardCounter available=True; selfKoPrevented=0.5
2026-10-04T10:00:02Z | koMinimum.content=card={FAKE_CARD_EFFECT_STRUCT}; input={FAKE_PREVIEW}
latest koMinimum.content=card={FAKE_ARCHIVED_HAND}
2026-10-04T10:00:03Z | hand={FAKE_HAND_IDS}
2026-10-04T10:00:04Z | combat.actor.attacker=heroId=123456
2026-10-04T10:00:05Z | chat=FAKE_CHAT_TEXT
2026-10-04T10:00:06Z | unknown=FAKE_UNKNOWN_RAW
FAKE_ERROR_STACK_CONTINUATION
2026-10-04T10:00:07Z | performance frames=42; unrelated=FAKE_UNKNOWN_FIELD
2026-10-04T10:00:08Z | combat=abcdef123456-1; damage.result before=20; predictedDamage=7; expectedHP=13; observedHP=13; hpMismatch=False; modifiers={FAKE_UNKNOWN_MODIFIERS}
2026-10-04T10:00:09Z | error.ModUi=System.NullReferenceException
 at FAKE_PRIVATE_METHOD C:\Users\FAKE_ACCOUNT\private.cs:22
2026-10-04T10:00:10Z | performance frames=FAKE_MALFORMED_NUMBER
"""+"\n";foreach(var file in new[]{"current.log","combat-mismatch.log"})Put(Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics",file),text);Put(Path.Combine(r.Game,"BepInEx/LogOutput.log"),"FAKE_OTHER_MOD FAKE_CHAT_UNLABELLED\n");Put(Path.Combine(r.Unity,"Player.log"),"FAKE_UNLABELLED_NICK_AND_CHAT\n");var i=Inspect(Collect(r));Check(!i.All.Contains("FAKE_",StringComparison.Ordinal)&&!i.All.Contains("123456",StringComparison.Ordinal)&&!i.All.Contains("cardEffect",StringComparison.Ordinal)&&!i.All.Contains("koMinimum.content",StringComparison.Ordinal),"private game/card/chat/stack contents excluded");Check(i.All.Contains("public-hp-result; before=20",StringComparison.Ordinal)&&i.All.Contains("expectedHP=13",StringComparison.Ordinal)&&i.All.Contains("managed-error; type=System.NullReferenceException",StringComparison.Ordinal),"public HP and error types retained");Check(Rows(i).Count(row=>row.GetProperty("droppedLines").GetInt32()>0&&row.GetProperty("projectedRecords").GetInt32()>0)==2,"drop and projection counts retained");Check(Rows(i).Count(row=>Text(row,"status")=="metadata-only")==2&&Rows(i).Where(row=>Text(row,"status")=="metadata-only").All(row=>row.GetProperty("readBytes").GetInt64()==0),"engine and Unity content neverread");
        r=New("installer-details");text="""
Code: BAP-INSTALL-FAILED
Version: 1.3.0-rc.1.local.3
ErrorType: System.IO.FileLoadException
Stage: Compatibility validation
Message: Could not load file or assembly 'C:\Users\FAKE_PERSON\Some Folder\Mono.Cecil.dll'. Operation is not supported. HRESULT: 0x80131515
Message: Could not load 'Mono.Cecil, Version=4.0.0.0, Culture=neutral'.
HResult: 0x80131515
Location: E:\Some Game Folder\tools\install.ps1:104
Stack: at Install, E:\Some Game Folder\tools\install.ps1: line 104
Exception: System.IO.FileLoadException: assembly load failed
 ---> System.NotSupportedException: blocked assembly
 at System.Reflection.Assembly.LoadFrom(String assemblyFile)
Authorization: Bearer FAKE_DETAIL_TOKEN
 FAKE_DETAIL_CONTINUATION
Message: Retry with a fully extracted package.
Game: E:\Steam\userdata\76561198000000000
Package: %USERPROFILE%\Downloads\FAKE_PRIVATE Folder\BetterAstralParty
Message: Directory not found: 'C:\Users\Alice\FAKE_DIRECTORY Private Folder'
Location: C:\Users\Alice\bank_statement.json
Message: Detached signature verification failed.
ExpectedSha256: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
""";Put(Path.Combine(r.Local,"BetterAstralParty-InstallError-20261004-100000-1234abcd.log"),text);Put(Path.Combine(r.Local,"Bootstrap-"+new string('b',32)+".log"),"BAP-BOOTSTRAP-FAILED\nRun: "+new string('b',32)+"\nSystem.IO.FileLoadException: Mono.Cecil.dll 0x80131515");Put(Path.Combine(r.Game,"BetterAstralParty-Compatibility.log"),"Allowed: False\nChanged [catalog]: downloaded catalog inventory\nMissing [loader]: BepInEx/core/BepInEx.dll\nCheck failed: catalog_3.2.0.json\n");i=Inspect(Collect(r));foreach(var marker in new[]{"Mono.Cecil.dll","0x80131515","System.NotSupportedException","Assembly.LoadFrom","install.ps1:104","Retry with a fully extracted package","Changed [catalog]: downloaded catalog inventory","BepInEx/core/BepInEx.dll","catalog_3.2.0.json","Version=4.0.0.0","Detached signature verification failed",new string('A',64),"BAP-BOOTSTRAP-FAILED"})Check(i.All.Contains(marker,StringComparison.Ordinal),"error cause retained "+marker);foreach(var marker in new[]{"FAKE_","userdata",new string('b',32),"Some Game Folder","bank_statement"})Check(!i.All.Contains(marker,StringComparison.Ordinal),"error private content omitted");Check(Rows(i).Count(row=>Text(row,"contentKind")=="redacted-error-text"&&Text(row,"status")=="included")==3,"detailed mode covers all error sources");
        r=New("error-privacy");Put(Path.Combine(r.Local,"Launch-"+new string('c',32)+".log"),"BAP-LAUNCH-FAILED\nException: DLL load failed from /home/FAKE_UNIX/tools/test.dll\nMessage: contact fake_person@example.invalid 192.0.2.23 2001:db8::1\nLocation: C:\\Users\\FAKE_QUOTED\\A Folder\\test.cs:22\nMessage: https://example.invalid/path?unlabelled=FAKE_QUERY\n-----BEGIN PRIVATE KEY-----\nFAKE_PRIVATE_KEY\n-----END PRIVATE KEY-----\nFAKE_KEY_CONTINUATION\nHResult: 0x80070005\n");i=Inspect(Collect(r));Check(!i.All.Contains("FAKE_",StringComparison.Ordinal)&&!i.All.Contains("fake_person@",StringComparison.Ordinal)&&!i.All.Contains("192.0.2.23",StringComparison.Ordinal)&&!i.All.Contains("2001:db8",StringComparison.Ordinal)&&i.All.Contains("test.dll",StringComparison.Ordinal)&&i.All.Contains("test.cs:22",StringComparison.Ordinal)&&i.All.Contains("0x80070005",StringComparison.Ordinal),"detailed errors mask paths/IP/key secrets butpreservecause");
        r=New("partial-record");Put(Path.Combine(r.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log"),"performance frames=42");i=Inspect(Collect(r));Check(!i.All.Contains("frames=42",StringComparison.Ordinal)&&Rows(i).Count(row=>row.GetProperty("droppedLines").GetInt32()>0)==1,"unfinished final record omitted");
        r=New("output-total");foreach(var directory in new[]{r.Local,r.Fallback})for(var n=0;n<8;n++)Put(Path.Combine(directory,"Launch-"+n.ToString("x32")+".log"),string.Concat(Enumerable.Repeat("Code: BAP-INSTALL-FAILED\n",30000)));i=Inspect(Collect(r));Check(LogBytes(i)<=4194304&&LogBytes(i)>4194000&&Rows(i).Any(row=>row.GetProperty("truncated").GetBoolean()||Text(row,"status")=="output-limit"),"actualaggregate output reaches cap without exceeding");
    }
    private static void FullVersionProjection()
    {
        var roots=New("version-provenance");foreach(var version in new[]{"1.0.0-rc.2","1.3.0-rc.1.local.2","1.3.0-dev.local.999999","1.2.1","1.2.1.0"}){var i=Inspect(Collect(roots,roots.Game,"6.0",version));Check(Text(i.Summary.GetProperty("environment"),"pluginVersion")==version&&Text(i.Summary.GetProperty("environment"),"pluginVersionSource")=="running-plugin","controlled historic/current version retainsprovenance "+version);Check(Text(i.Summary.GetProperty("environment"),"pluginFileProductVersion")=="unknown","runningconstant differsfromunknownfileversion");}
        foreach(var version in new[]{"FAKE_INVALID_VERSION","1.3.0-rc.1.local.0","1.3.0-rc.1.local.1000000","1.3.0-rc.1.local.2/PRIVATE","github_pat_FAKE_VERSION"}){var i=Inspect(Collect(roots,roots.Game,"6.0",version));Check(Text(i.Summary.GetProperty("environment"),"pluginVersion")=="unknown"&&!i.All.Contains(version,StringComparison.Ordinal),"uncontrolledversion omitted");}var unknown=Inspect(Collect(roots,roots.Game,"6.0",""));Check(Text(unknown.Summary.GetProperty("environment"),"pluginVersion")=="unknown"&&Text(unknown.Summary.GetProperty("environment"),"pluginVersionSource")=="unknown","launcher collection doesnotfabricate runningplugin");
    }
    private static void FullSettingsProjection()
    {
        var roots=New("current-settings");
        var features="autoThanks,cardTargetButtons,characterGuide,characterRoles,indicatorFade,indicatorHide,counterSound,counterAnimation,suppressNotices,pingFocus,updateNotifications,automaticChecks,shushuShieldRestrictions,finalDamage".Split(',');
        var actions=new[]{"FieldZoom","MatchFocus","AutoThanks","CardTargetButtons","CharacterGuide","CharacterRoles","IndicatorFade","IndicatorHide","CounterSound","CounterAnimation","SuppressNotices","PingFocus","UpdateNotifications","AutomaticChecks","ShushuShieldRestrictions","FinalDamage"};
        var text=new StringBuilder();
        foreach(var enabled in new[]{true,false}) {
            text.Append("2026-10-04T10:00:00Z | settings=fieldZoom=").Append(enabled).Append("; matchFocus=").Append(enabled).Append("; unrelated=FAKE_UNKNOWN_SETTING\n");
            text.Append("2026-10-04T10:00:01Z | settings.features=").Append(string.Join("; ",features.Select(key=>key+"="+enabled))).Append("; unknownSetting=FAKE_X\n");
        }
        foreach(var action in actions) foreach(var phase in new[]{"begin","complete"}) text.Append("2026-10-04T10:00:02Z | settingsAction ").Append(phase).Append('=').Append(action).Append('\n');
        text.Append("2026-10-04T10:00:03Z | settings.features=autoThanks=FAKE_BOOL; finalDamage=False; unknownSetting=FAKE_X\n")
            .Append("2026-10-04T10:00:04Z | settingsAction begin=FAKE_UNKNOWN_ACTION\n")
            .Append("2026-10-04T10:00:05Z | error.settingsAction.AutoThanks=System.InvalidOperationException\n")
            .Append("2026-10-04T10:00:06Z | settings.features=autoThanks=True; unknownSetting=").Append(new string('S',40)).Append('\n')
            .Append("2026-10-04T10:00:07Z | settingsAction complete=ShushuShieldRestrictions; unknownSetting=FAKE_X\n")
            .Append("2026-10-04T10:00:08Z | settingsAction complete=ShushuShieldRestrictions password=FAKE_PASSWORD\n")
            .Append("2026-10-04T10:00:09Z | settingsAction complete=ShushuShieldRestrictionsFAKE_SUFFIX\n")
            .Append("2026-10-04T19:00:10.1234567+09:00 | settingsAction complete=ShushuShieldRestrictions\n");
        var path=Path.Combine(roots.Game,"BepInEx/BetterAstralParty-Diagnostics/current.log");Put(path,text.ToString());var before=Hash(path);
        var collected=Inspect(Collect(roots));var output=collected.Entries["logs/game-diagnostics-current.log"];
        foreach(var enabled in new[]{"true","false"}) {
            Check(output.Contains("settings-subset; fieldZoom="+enabled+"; matchFocus="+enabled,StringComparison.Ordinal),"camera and focus boolean state survives ZIP: "+enabled);
            foreach(var key in features) Check(output.Contains(key+"="+enabled,StringComparison.Ordinal),"current feature boolean survives ZIP: "+key+"="+enabled);
        }
        foreach(var action in actions) foreach(var phase in new[]{"begin","complete"}) Check(output.Contains("settingsAction; phase="+phase+"; action="+action,StringComparison.Ordinal),"current settings action survives ZIP: "+action+"/"+phase);
        Check(output.Contains("feature=Settings; legacy_feature=SettingsAction",StringComparison.Ordinal),"new settings action failure retains bounded error classification");
        Check(!collected.All.Contains("FAKE_",StringComparison.Ordinal)&&!output.Contains("unknownSetting",StringComparison.Ordinal)&&!output.Contains("unrelated",StringComparison.Ordinal),"unknown setting names, string values and actions never pass through ZIP");
        Check(!output.Contains("2026-10-04T10:00:06",StringComparison.Ordinal)&&!collected.All.Contains(new string('S',40),StringComparison.Ordinal)&&FileRow(collected,"game/diagnostics-current").GetProperty("redactedLines").GetInt32()>0,"secret-looking long setting tokens still reject the complete record before projection");
        foreach(var second in new[]{"07","08","09"}) Check(!output.Contains("2026-10-04T10:00:"+second,StringComparison.Ordinal),"known action prefixes do not admit extra fields, secrets or suffixes: "+second);
        Check(output.Contains("2026-10-04T10:00:10.1234567+00:00 | settingsAction; phase=complete; action=ShushuShieldRestrictions",StringComparison.Ordinal),"production O-format timestamp and exact long action retain normalized fixed metadata");
        Check(Hash(path)==before&&FileRow(collected,"game/diagnostics-current").GetProperty("droppedLines").GetInt32()>0,"new settings projection preserves source bytes and reports rejected records");
    }
    private static void FullSteamExitFailure()
    {
        var roots=New("steam-exit-failure");
        Put(Path.Combine(roots.Local,"BetterAstralParty-InstallError-20261004-100000-87abcdef.log"),"""
BetterAstralParty Uninstall failure
Code: BAP-INSTALL-FAILED
Version: 1.3.0-rc.3.local.6
Exception: depth=0; type=BetterAstralParty.Updating.ApplySafetyException; HResult=0x80131500
Exception: depth=0; recoveryReason=unknown; applyCode=5; nativeCode=87
SteamDiagnostic: schema=1; reason=ProcessTerminationFailed; phase=ProcessTermination; utc=2026-10-04T10:00:00.0000000Z; observedCount=1; omittedCount=0
SteamProcess: index=1; pid=4321; sessionMatch=true; pathAvailable=true; pathMatch=true; startAvailable=true; startedUtc=2026-10-04T09:59:00.0000000Z
SteamObservationProcess: index=1; pid=4321; kind=SteamWebHelper; querySource=Native; nativeError=87; exitAvailable=true; confirmedExit=false
SteamObservationCoverage: status=included
Message: Update safety: ProcessUncertain
Stack: at BetterAstralParty.Installation.NativeSteamMaintenance.StopVerified(SteamProcessObservation target)
DiagnosticCoverage: exceptionDepth=1; exceptionChainOmitted=false; textTruncated=false; steamContext=included
"""+"\n");
        var collected=Inspect(Collect(roots));
        foreach(var marker in new[]{"ProcessTerminationFailed","phase=ProcessTermination","kind=SteamWebHelper","nativeError=87","applyCode=5; nativeCode=87","confirmedExit=false","StopVerified","steamContext=included"}) Check(collected.All.Contains(marker,StringComparison.Ordinal),"native87 uninstall failure detail survives ZIP: "+marker);
        Check(!collected.All.Contains(roots.Root,StringComparison.Ordinal),"Steam failure collection exports no fixture root");
    }
}
