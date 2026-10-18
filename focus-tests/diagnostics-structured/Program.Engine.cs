using System.Text;
using System.Text.Json;
using BetterAstralParty.Diagnostics;

internal static partial class Program
{
    // Synthetic engine logs, crash folders, Windows events, other plugins and stage markers only.
    private static BundleResult CollectSources(Roots roots, DiagnosticSources sources) => DiagnosticBundle.Collect(roots.Game, roots.Local, roots.Fallback, roots.Unity, roots.Hub, roots.Hub2, "8.0", "1.4.0", null!, sources);
    private static string EventXml(string provider, int id, DateTime utc, params (string Name, string Value)[] data)
    {
        var body = new StringBuilder();
        foreach (var (name, value) in data) body.Append(name.Length == 0 ? "<Data>" : "<Data Name='" + name + "'>").Append(System.Net.WebUtility.HtmlEncode(value)).Append("</Data>");
        return "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='" + provider + "'/><EventID Qualifiers='0'>" + id + "</EventID><TimeCreated SystemTime='" + utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffff") + "Z'/></System><EventData>" + body + "</EventData></Event>";
    }
    private static void EngineSources()
    {
        var r = New("engine-sources");
        Put(Path.Combine(r.Game, "BepInEx/LogOutput.log"), string.Join("\n", new[] {
            "[Message:   BepInEx] BepInEx 6.0.0-be.788 - AstralParty_INT",
            "[Info   :   BepInEx] Loading [Better Astral Party 1.4.0]",
            "[Message:     Unity] FAKE_CHAT_LINE hello friend",
            "[Error  :Il2CppInterop] System.NullReferenceException: Object reference not set at C:\\Users\\FAKE_PERSON\\Games\\Other.dll",
            "  at OtherMod.Patch.Postfix () [0x00000] in <00000000000000000000000000000000>:0",
            "[Info   :     Unity] FAKE_PLAIN_INFO",
            "  at Orphan.Frame () FAKE_ORPHAN_FRAME",
            "[Warning:  OtherMod] token=FAKE_TOKEN_VALUE",
            "[Error  :  OtherMod] Player 76561198000000001 FAKE_SUFFIX failed",
            "[Error  :  OtherMod] card=FAKE_CARD_PAYLOAD rejected",
        }) + "\n");
        Put(Path.Combine(r.Unity, "Player.log"), string.Join("\n", new[] {
            "Initialize engine version: 2022.3.0f1",
            "username=FAKE_USERNAME",
            "Crash!!!",
            "SymInit: Symbol-SearchPath: 'C:\\Users\\FAKE_PERSON\\Game'",
            "0x00007FF6A1B2C3D4 (UnityPlayer) UnityMain",
            "========== END OF STACKTRACE ===========",
            "FAKE_AFTER_CRASH",
        }) + "\n");
        var crashes = Path.Combine(r.Root, "crashes");
        var folder = Path.Combine(crashes, "Crash_2026-10-18_101010123");
        Put(Path.Combine(folder, "error.log"), "AstralParty_INT.exe caused an Access Violation (0xc0000005)\nin module UnityPlayer.dll at 0033:7ffb1234.\nError occurred at 2026-10-18_101010.\nC:\\Users\\FAKE_PERSON\\Game\\AstralParty_INT.exe, run by FAKE_PERSON.\npassword=FAKE_CRASH_SECRET\n");
        Put(Path.Combine(folder, "crash.dmp"), "FORBIDDEN_DUMP_BYTES");
        Put(Path.Combine(folder, "Player.log"), "[Error  :  BepInEx] Plugin load failed\nFAKE_CRASH_PLAYER_INFO\n");
        Put(Path.Combine(crashes, "NotACrash", "error.log"), "FORBIDDEN_OTHER_FOLDER");
        var now = DateTime.UtcNow;
        var events = new List<string> {
            EventXml("Application Error", 1000, now.AddHours(-1), ("AppName", "AstralParty_INT.exe"), ("AppVersion", "2022.3.0.1"), ("AppTimeStamp", "6543210"), ("ModuleName", "GameAssembly.dll"), ("ModuleVersion", "0.0.0.0"), ("ModuleTimeStamp", "0"), ("ExceptionCode", "c0000005"), ("FaultingOffset", "0000000001234567"), ("ProcessId", "0x1a2b"), ("ProcessCreationTime", "0x01"), ("AppPath", "C:\\Users\\FAKE_PERSON\\steamapps\\common\\Astral Party\\AstralParty_INT.exe"), ("ModulePath", "E:\\SteamLibrary\\steamapps\\common\\Astral Party\\GameAssembly.dll"), ("IntegratorReportId", "FAKE_REPORT_ID")),
            EventXml("Application Error", 1000, now.AddHours(-2), ("AppName", "FAKE_OTHER_APP.exe"), ("AppVersion", "1.0.0.0")),
            EventXml("Application Error", 1000, now.AddDays(-9), ("AppName", "AstralParty_INT.exe"), ("AppVersion", "FAKE_OLD_EVENT")),
            EventXml("Application Hang", 1002, now.AddHours(-3), ("", "AstralParty_INT.exe"), ("", "2022.3.0.1")),
            EventXml(".NET Runtime", 1026, now.AddHours(-4), ("", "Application: AstralParty_INT.exe\nDescription: The process was terminated due to an unhandled exception.\nException Info: System.InvalidOperationException\n   at BetterAstralParty.Plugin.Load()\nC:\\Users\\FAKE_PERSON\\secret.txt")),
            "<Event><System><Provider Name='Application Error'/>FAKE_MALFORMED",
        };
        var runtime = Path.Combine(r.Root, "runtime");
        Put(Path.Combine(runtime, "stages-current.log"), "2026-10-18T10:00:00.0000000Z session=" + new string('a', 32) + " version=1.4.0 stage=PluginLoad outcome=Begin\n2026-10-18T10:00:01.0000000Z stage=HomeMenu outcome=Completed\n2026-10-18T10:00:02Z stage=Home FAKE_STAGE_TEXT\n");
        Put(Path.Combine(runtime, "safe-mode.state"), "failures=2\nmode=safe\nnote=FAKE STATE WITH SPACES\n");
        Put(Path.Combine(r.Game, "BepInEx/plugins/OtherMod/OtherMod.dll"), "FORBIDDEN_PLUGIN_CONTENT");
        Put(Path.Combine(r.Game, "BepInEx/patchers/Patcher.dll"), "FORBIDDEN_PATCHER_CONTENT");
        var sources = new DiagnosticSources { CrashRoot = crashes, WindowsEvents = () => events, RuntimeStateRoot = runtime };
        var i = Inspect(CollectSources(r, sources));
        var loader = i.Entries["logs/loader-log-output.log"];
        Check(loader.Contains("BepInEx 6.0.0-be.788") && loader.Contains("Loading [Better Astral Party 1.4.0]") && loader.Contains("System.NullReferenceException") && loader.Contains("at OtherMod.Patch.Postfix"), "loader start, error and attached stack lines kept");
        Check(loader.Contains("[path]/Other.dll") && !loader.Contains("FAKE_PERSON"), "engine paths reduced to code file names");
        Check(!loader.Contains("FAKE_CHAT_LINE") && !loader.Contains("FAKE_PLAIN_INFO") && !loader.Contains("FAKE_ORPHAN_FRAME"), "non-error and orphan stack lines dropped");
        Check(!loader.Contains("FAKE_TOKEN_VALUE") && !loader.Contains("76561198000000001") && !loader.Contains("FAKE_CARD_PAYLOAD") && loader.Contains("[redacted]"), "secrets, Steam IDs and game payload redacted");
        var loaderRow = FileRow(i, "loader/log-output");
        Check(Text(loaderRow, "contentKind") == "filtered-error-lines" && loaderRow.GetProperty("droppedLines").GetInt32() >= 3 && loaderRow.GetProperty("redactedLines").GetInt32() >= 3, "engine filter counts dropped and redacted lines");
        var player = i.Entries["logs/unity-player.log"];
        Check(player.Contains("Crash!!!") && player.Contains("0x00007FF6A1B2C3D4 (UnityPlayer)") && player.Contains("END OF STACKTRACE") && !player.Contains("FAKE_AFTER_CRASH") && !player.Contains("FAKE_USERNAME") && !player.Contains("FAKE_PERSON"), "Unity crash block kept and redacted, ordinary lines dropped");
        var error = i.Entries["logs/crash-01-error.log"];
        Check(error.Contains("Access Violation (0xc0000005)") && error.Contains("UnityPlayer.dll") && !error.Contains("FAKE_PERSON") && !error.Contains("FAKE_CRASH_SECRET"), "crash error.log kept with paths and secrets redacted");
        Check(i.Entries["logs/crash-01-player.log"].Contains("Plugin load failed") && !i.All.Contains("FAKE_CRASH_PLAYER_INFO"), "crash-time Player.log filtered");
        Check(Text(FileRow(i, "crash/01/dump"), "status") == "metadata-only" && FileRow(i, "crash/01/dump").GetProperty("inputBytes").GetInt64() == 20 && !i.All.Contains("FORBIDDEN_DUMP") && !i.All.Contains("FORBIDDEN_OTHER_FOLDER"), "dump size only and non-crash folders ignored");
        var eventLines = i.Entries["logs/windows-application-events.jsonl"].Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        Check(eventLines.Length == 3, "only recent game-process Windows events kept");
        var crash = eventLines.Single(e => Text(e, "provider") == "Application Error");
        Check(Text(crash, "module") == "GameAssembly.dll" && Text(crash, "exceptionCode") == "c0000005" && Text(crash, "faultOffset") == "0000000001234567" && Text(crash, "modulePathKind") == "game-folder" && Text(crash, "appVersion") == "2022.3.0.1", "Application Error typed fields");
        Check(Text(eventLines.Single(e => Text(e, "provider") == "Application Hang"), "module") == "n/a", "Application Hang uses ordered data");
        var runtimeText = Text(eventLines.Single(e => Text(e, "provider") == ".NET Runtime"), "text");
        Check(runtimeText.Contains("System.InvalidOperationException") && runtimeText.Contains("BetterAstralParty.Plugin.Load") && !runtimeText.Contains("FAKE_PERSON"), ".NET Runtime text sanitized");
        Check(!i.All.Contains("FAKE_OTHER_APP") && !i.All.Contains("FAKE_OLD_EVENT") && !i.All.Contains("FAKE_REPORT_ID") && !i.All.Contains("FAKE_MALFORMED"), "other apps, old, unrequested and malformed events excluded");
        var inventory = Document(i, "plugin-inventory.json");
        var paths = inventory.GetProperty("plugins").EnumerateArray().Select(p => Text(p, "path")).ToArray();
        Check(paths.Contains("plugins/OtherMod/OtherMod.dll") && paths.Contains("patchers/Patcher.dll") && inventory.GetProperty("plugins").EnumerateArray().All(p => Text(p, "sha256").Length == 64) && !i.All.Contains("FORBIDDEN_PLUGIN") && !i.All.Contains("FORBIDDEN_PATCHER"), "other plugins inventoried by name and hash only");
        var stages = i.Entries["logs/runtime-stages-current.log"];
        Check(stages.Contains("stage=PluginLoad outcome=Begin") && stages.Contains("stage=HomeMenu outcome=Completed") && !stages.Contains("FAKE_STAGE_TEXT"), "stage ladder keeps fixed tokens only");
        var state = i.Entries["logs/runtime-safe-mode-state.log"];
        Check(state.Contains("failures=2") && state.Contains("mode=safe") && !state.Contains("FAKE"), "safe-mode state keeps fixed key-values only");
        Check(i.Summary.GetProperty("privacy").GetProperty("memoryDumpsRead").ValueKind == JsonValueKind.False && Text(i.Summary.GetProperty("scope"), "engineLogs") == "filtered-error-lines", "summary declares the filtered scope");

        var plain = New("engine-defaults"); Put(Path.Combine(plain.Unity, "Player.log"), "[Error  :  BepInEx] Plugin load failed\n");
        var defaults = Inspect(Collect(plain));
        Check(!defaults.Entries.ContainsKey("logs/windows-application-events.jsonl") && !defaults.Summary.GetProperty("files").EnumerateArray().Any(row => Text(row, "source").StartsWith("crash", StringComparison.Ordinal)), "machine-wide sources stay off unless a caller opts in");

        var big = New("engine-tail"); var log = Path.Combine(big.Game, "BepInEx/LogOutput.log");
        Put(log, "");
        using (var writer = new StreamWriter(log, false, new UTF8Encoding(false)))
        {
            writer.Write("[Error  :  OtherMod] FAKE_HEAD_ERROR_OUTSIDE_WINDOW\n");
            var filler = "[Info   :     Unity] filler line for the engine tail window\n";
            for (long written = 0; written < DiagnosticBundle.MaxEngineInputBytes + 1024; written += filler.Length) writer.Write(filler);
            writer.Write("[Error  :  OtherMod] Newest failure kept\n");
        }
        var tail = Inspect(Collect(big));
        Check(tail.Entries["logs/loader-log-output.log"].Contains("Newest failure kept") && !tail.All.Contains("FAKE_HEAD_ERROR") && FileRow(tail, "loader/log-output").GetProperty("truncated").GetBoolean(), "oversized engine log reads the newest window from a line boundary");
    }
}
