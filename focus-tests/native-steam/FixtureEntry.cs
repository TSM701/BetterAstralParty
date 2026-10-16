#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static partial class FixtureEntry
{
    private static int checks;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string alias, string existing, IntPtr security);
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); checks++; }
    private static void Reject(Action action, string reason) { try { action(); } catch { checks++; return; } throw new InvalidOperationException(reason); }
    private static void Text(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value, new UTF8Encoding(false)); }
    private static SteamProcessObservation Process(uint id, string name, string image, uint session = 7, long creation = 133900000000000000)
    { return new SteamProcessObservation { Pid = id, Name = name, Image = image, Session = session, Creation = creation, Available = true }; }
    private static void UnavailableDiagnostic(Action action, SteamProcessObservation item, string kind, string phase = "ShutdownObservation")
    {
        InvalidOperationException? failure = null; try { action(); } catch (InvalidOperationException error) { failure = error; }
        Check(failure != null, "unavailable process remains a blocking refusal");
        var serializer = new JavaScriptSerializer();
        var context = (System.Collections.Generic.Dictionary<string, object>)serializer.DeserializeObject((string)failure!.Data["BapSteamDiagnostic"]!);
        var rows = (object[])context["processes"];
        Check((string)context["reason"] == "ProcessObservationUnavailable" && (string)context["phase"] == phase && (int)context["observedCount"] == 1 && (int)context["omittedCount"] == 0 && rows.Length == 1, "safe-stop diagnostic retains its reason, phase and observed count");
        var row = (System.Collections.Generic.Dictionary<string, object>)rows[0];
        Check(Convert.ToUInt32(row["pid"]) == item.Pid && (bool)row["pathAvailable"] == (item.Image.Length != 0) && (bool)row["startAvailable"] == (item.Creation > 0) && Equals(row["pathMatch"], item.PathMatch) && Equals(row["sessionMatch"], item.SessionMatch), "safe-stop diagnostic preserves observed identity availability without inventing matches");
        var observation = (System.Collections.Generic.Dictionary<string, object>)serializer.DeserializeObject((string)failure.Data["BapSteamObservation"]!);
        var facts = (object[])observation["processes"]; var fact = (System.Collections.Generic.Dictionary<string, object>)facts[0];
        Check(facts.Length == 1 && Convert.ToUInt32(fact["pid"]) == item.Pid && (string)fact["kind"] == kind && (string)fact["querySource"] == (item.Available ? "Native" : "Unavailable") && (int)fact["nativeError"] == item.NativeError && (bool)fact["exitAvailable"] == item.Available && (bool)fact["confirmedExit"] == item.Exited, "safe-stop observation preserves process kind and exact native query failure");
    }
    private static OrderedDictionary Item(string exe, string name = "Astral Party - Mod", string args = "")
    {
        var item = new OrderedDictionary(StringComparer.OrdinalIgnoreCase); item.Add("appid", 0x80000001U); item.Add("AppName", name); item.Add("exe", exe); item.Add("LaunchOptions", args); item.Add("custom", "preserve me"); return item;
    }
    private static int Main(string[] args)
    {
        try {
            var root = Path.GetFullPath(args[0]); if (!root.Contains("native-steam-")) throw new InvalidOperationException("Isolated fixture root required.");
            var steam = Path.Combine(root, "Steam"); var game = Path.Combine(root, "Game"); Directory.CreateDirectory(steam); Directory.CreateDirectory(game);
            Text(Path.Combine(game, "AstralParty_INT.exe"), "inert game"); Text(Path.Combine(game, "BetterAstralParty-Launcher.exe"), "inert launcher");
            var mode=args.Length>1?args[1]:"all";
            if(mode=="all"){Discovery(root,steam,game);DiscoveryExtended(root);UserPaths(root,steam,game);Accounts(steam);Binary(root,game);Mutations(steam,game);ProcessPolicy(steam,game);TypedDiagnostics(game);}
            else if(mode=="discovery"){Discovery(root,steam,game);DiscoveryExtended(root);UserPaths(root,steam,game);}
            else if(mode=="session")ProcessPolicy(steam,game);
            else if(mode=="shortcut"){Accounts(steam);Binary(root,game);Mutations(steam,game);}
            else if(mode=="observation"){ProcessPolicy(steam,game);TypedDiagnostics(game);}
            else throw new ArgumentException("Unknown native Steam semantic matrix.");
            Console.WriteLine("Native Steam maintenance: " + checks + " isolated checks passed; no registry access or native process observation/termination."); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Discovery(string root, string steam, string game)
    {
        Check(NativeSteamMaintenance.ResolveIntDirectory(game + "\\") == game, "manual INT discovery: expected=" + game + ";actual=" + NativeSteamMaintenance.ResolveIntDirectory(game + "\\"));
        Check(NativeSteamMaintenance.ResolveGame(game, null, root) == game, "explicit installation takes precedence");
        var parent = Path.Combine(root, "nested"); Text(Path.Combine(parent, "8vJXnINT", "AstralParty_INT.exe"), "inert");
        Check(NativeSteamMaintenance.ResolveIntDirectory(parent) == Path.Combine(parent, "8vJXnINT"), "8vJXnINT nested discovery");
        Check(NativeSteamMaintenance.InstallDirectoryName("\"installdir\" \"Astral Party\"") == "Astral Party", "manifest folder");
        Check(NativeSteamMaintenance.InstallDirectoryName("\"installdir\" \"..\\elsewhere\"") == null, "manifest traversal rejected");
        Check(NativeSteamMaintenance.InstallDirectoryName("\"installdir\" \"A\" \"installdir\" \"B\"") == null, "ambiguous manifest rejected");
        var library = Path.Combine(root, "library"); Text(Path.Combine(library, "steamapps", "common", "DifferentFolder", "AstralParty_INT.exe"), "inert");
        Text(Path.Combine(library, "steamapps", "appmanifest_2622000.acf"), "\"installdir\" \"DifferentFolder\"");
        var paths = "\"libraryfolders\" { \"0\" { \"path\" \"relative-invalid\" } \"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }";
        Text(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), paths);
        var package = Path.Combine(root, "package"); Directory.CreateDirectory(package);
        var diagnostics = new System.Collections.Generic.List<string>();
        Check(NativeSteamMaintenance.ResolveGame(null, steam, package, diagnostics.Add) == Path.Combine(library, "steamapps", "common", "DifferentFolder"), "bad library does not hide valid manifest installation");
        Check(diagnostics.Count == 1 && diagnostics[0].Contains("LibraryFoldersPath") && !diagnostics[0].Contains(library), "tolerated discovery failure stays observable without exporting path metadata");
        Text(Path.Combine(steam, "steamapps", "common", "Astral Party", "AstralParty_INT.exe"), "inert");
        Reject(() => NativeSteamMaintenance.ResolveGame(null, steam, package), "multiple installations cannot be selected arbitrarily");
        Text(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"path\" \"" + library.Replace("\\", "\\\\") + "\"");
    }
    private static void UserPaths(string root, string steam, string game)
    {
        var captured = Environment.GetEnvironmentVariable("BAP_MAINTENANCE_CWD"); var current = Environment.CurrentDirectory;
        try {
            Environment.SetEnvironmentVariable("BAP_MAINTENANCE_CWD", root); Environment.CurrentDirectory = steam;
            Check(NativeSteamMaintenance.ResolveUserPath("./Game") == game, "relative CLI path uses captured caller directory, not dispatcher pushd");
            Check(NativeSteamMaintenance.ResolveIntDirectory("\"nested/../Game\"") == game, "quoted parent-relative discovery remains canonical");
            Check(NativeSteamMaintenance.ResolveIntDirectory("nested/.") == Path.Combine(root, "nested", "8vJXnINT"), "relative parent installation retains nested INT discovery");
            Check(NativeSteamMaintenance.ResolveUserPath(game + "\\..\\Game") == game, "ordinary absolute dot path is canonicalized");
            Check(NativeSteamMaintenance.ResolveUserPath("reviews/../review.json") == Path.Combine(root, "review.json"), "RecoveryPlan can share the same ordinary user-path boundary");
            foreach (var unsafePath in new[] { @"\\server\share\Game", @"\\?\C:\Game", @"\\.\C:\Game", @"\Game", "C:Game", @"Game\NUL", @"Game.\..\Game", @"Game \..\Game", "Game:alternate" })
                Reject(() => NativeSteamMaintenance.ResolveUserPath(unsafePath), "relative normalization cannot launder device, namespace or unsafe-component aliases");
            var longPath = root + "\\" + string.Join("\\", Enumerable.Repeat(new string('L', 40), 8));
            Check(longPath.Length > 260 && NativeSteamMaintenance.ResolveUserPath(longPath) == longPath, "already safe absolute long paths retain strict-parser fast path");
            Environment.SetEnvironmentVariable("BAP_MAINTENANCE_CWD", @"\\?\C:\invalid");
            Check(NativeSteamMaintenance.ResolveUserPath(game) == game, "safe absolute selection does not depend on captured cwd");
            Check(NativeSteamMaintenance.ResolveUserPath(game + "\\..\\Game") == game, "absolute dot normalization does not depend on captured cwd");
            Reject(() => NativeSteamMaintenance.ResolveUserPath("Game"), "invalid captured cwd cannot authorize a relative path");
            Environment.SetEnvironmentVariable("BAP_MAINTENANCE_CWD", null); Environment.CurrentDirectory = root;
            Check(NativeSteamMaintenance.ResolveIntDirectory("Game") == game, "direct native CLI relative path uses its current working directory");
        } finally { Environment.CurrentDirectory = current; Environment.SetEnvironmentVariable("BAP_MAINTENANCE_CWD", captured); }
    }
    private static void Accounts(string steam)
    {
        try { NativeSteamMaintenance.ResolveAccountFromMetadata(Path.Combine(steam, "absent"), 0, 0, null, null); throw new InvalidOperationException("Missing Steam directory accepted"); }
        catch (SteamProfileUnavailableException absent) { Check(absent.Reason == "SteamRootAbsent", "absent Steam root has an exact optional-discovery classification"); }
        try { NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, null, null); throw new InvalidOperationException("Missing Steam profile accepted"); }
        catch (SteamProfileUnavailableException absent) { Check(absent.Reason == "SteamProfileAbsent", "zero local Steam profiles has an exact optional-discovery classification"); }
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "11", "config"));
        Check(NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, null, null) == 11, "only local profile");
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "12", "config"));
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "00011", "config"));
        Check(NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 11, 12, null, "garbage") == 11, "explicit profile precedence");
        Check(NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 12, null, "garbage") == 12, "active registry profile precedence");
        Reject(() => NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 13, 11, null, null), "missing explicit profile rejected");
        const string saved = "\"users\" { \"76561197960265739\" { \"AccountName\" \"fixture-one\" \"MostRecent\" \"0\" } \"76561197960265740\" { \"AccountName\" \"fixture-two\" \"MostRecent\" \"1\" } }";
        Check(NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, null, saved) == 12, "saved most recent profile");
        Check(NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, "FIXTURE-TWO", saved) == 12, "saved auto-login profile case equivalence");
        Reject(() => NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, "fixture-one", saved), "conflicting auto/recent profiles rejected");
        Reject(() => NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, null, saved.Replace("\"0\"", "\"1\"")), "multiple most recent profiles rejected");
        Reject(() => NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, null, null), "multiple local profiles rejected");
        try { NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 13, 0, null, null); throw new Exception("Missing explicit Steam account accepted"); }
        catch (InvalidOperationException error) { Check(!(error is SteamProfileUnavailableException), "explicit invalid account is never optional discovery"); }
        try { NativeSteamMaintenance.ResolveAccountFromMetadata(steam, 0, 0, null, null); throw new Exception("Ambiguous Steam account accepted"); }
        catch (SteamProfileUnavailableException error) { Check(error.Reason == "SteamProfileAmbiguous", "ambiguous auto account has a distinct optional-discovery classification without choosing a profile"); }
    }
    private static void Binary(string root, string game)
    {
        var empty = NativeSteamMaintenance.EmptyShortcuts(); var items = (OrderedDictionary)empty["shortcuts"]!;
        var exe = "\"" + Path.Combine(game, "BetterAstralParty-Launcher.exe") + "\"";
        items.Add("0", Item(exe)); items.Add("5", Item("\"" + Path.Combine(root, "other", "BetterAstralParty-Launcher.exe") + "\""));
        var bytes = NativeSteamMaintenance.WriteShortcuts(empty);
        Check(NativeSteamMaintenance.WriteShortcuts(NativeSteamMaintenance.ReadShortcuts(bytes)).SequenceEqual(bytes), "ordered binary VDF roundtrip preserves all fields");
        Check(NativeSteamMaintenance.OwnedShortcutKeys(empty, game).SequenceEqual(new[] { "0" }), "ownership requires exact selected executable");
        items.Add("9", Item(exe, args: "custom")); Check(NativeSteamMaintenance.OwnedShortcutKeys(empty, game).Count == 1, "custom launch options are not owned");
        items.Add("2", Item(exe)); Reject(() => NativeSteamMaintenance.OwnedShortcutKeys(empty, game), "duplicate owned shortcut rejected"); items.Remove("2");
        Reject(() => NativeSteamMaintenance.ReadShortcuts(bytes.Concat(new byte[] { 8 }).ToArray()), "trailing binary bytes rejected");
        Reject(() => NativeSteamMaintenance.ReadShortcuts(bytes.Take(bytes.Length - 1).ToArray()), "truncated binary rejected");
        Reject(() => NativeSteamMaintenance.ReadShortcuts(new byte[] { 3, 0, 8 }), "unsupported field type rejected");
        Reject(() => NativeSteamMaintenance.ReadShortcuts(new byte[] { 0, 115, 104, 111, 114, 116, 99, 117, 116, 115, 0, 1, 97, 0, 120, 0, 1, 65, 0, 121, 0, 8, 8 }), "case duplicate keys rejected");
    }
    private static void Mutations(string steam, string game)
    {
        var path = Path.Combine(steam, "userdata", "11", "config", "shortcuts.vdf");
        var originalRoot = NativeSteamMaintenance.EmptyShortcuts(); var entries = (OrderedDictionary)originalRoot["shortcuts"]!; entries.Add("5", Item("unrelated", "Other shortcut"));
        var original = NativeSteamMaintenance.WriteShortcuts(originalRoot); File.WriteAllBytes(path, original);
        foreach (var malformed in new[] { false, true }) {
            if (malformed) File.WriteAllBytes(path, new byte[] { 3, 0, 8 });
            var preserved = File.ReadAllBytes(path); var beforeFiles = Directory.GetFiles(steam, "*", SearchOption.AllDirectories).Length;
            using (var locked = malformed ? null : File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Reject(() => NativeSteamMaintenance.PlanRegister(steam, 11, game), "default shortcut registration planning remains strict");
                Reject(() => NativeSteamMaintenance.PlanRemove(steam, 11, game), "default shortcut removal planning remains strict");
                var failures = new List<Exception>();
                Check(!NativeSteamMaintenance.PlanRegister(steam, 11, game, failures.Add) && !NativeSteamMaintenance.PlanRemove(steam, 11, game, failures.Add)
                    && failures.Count == 2, "opted-in read-only planning skips corrupt/locked shared data");
                Check(!NativeSteamMaintenance.PlanRemove(steam, 11, game, _ => throw new IOException("inert diagnostic failure")), "optional diagnostic callback cannot force unsafe shortcut mutation");
            }
            Check(File.ReadAllBytes(path).SequenceEqual(preserved) && Directory.GetFiles(steam, "*", SearchOption.AllDirectories).Length == beforeFiles,
                "optional planning neither rewrites nor backs up shared profile data");
            File.WriteAllBytes(path, original);
        }
        Check(NativeSteamMaintenance.PlanRegister(steam, 11, game), "absent owned shortcut needs registration");
        var registered = NativeSteamMaintenance.Register(steam, 11, game)!;
        Check(registered.Backup != null && File.ReadAllBytes(registered.Backup).SequenceEqual(original), "exact original VDF retained by no-overwrite backup");
        Check(!NativeSteamMaintenance.PlanRegister(steam, 11, game) && NativeSteamMaintenance.PlanRemove(steam, 11, game), "registered shortcut skips Steam mutation");
        var current = File.ReadAllBytes(path); Check(NativeSteamMaintenance.Register(steam, 11, game) == null && File.ReadAllBytes(path).SequenceEqual(current), "already registered is a byte-preserving no-op");
        var parsed = NativeSteamMaintenance.ReadShortcuts(current); var owned = NativeSteamMaintenance.OwnedShortcutKeys(parsed, game)[0]; var item = (OrderedDictionary)((OrderedDictionary)parsed["shortcuts"]!)[owned]!;
        var id = (uint)item["appid"]!; var hero = Path.Combine(Path.GetDirectoryName(path)!, "grid", id + "_hero.png"); var image = Encoding.UTF8.GetBytes("owned old hero");
        Directory.CreateDirectory(Path.GetDirectoryName(hero)!); File.WriteAllBytes(hero, image); string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(image)).Replace("-", "");
        Text(hero + ".BetterAstralParty.json", new JavaScriptSerializer().Serialize(new { Owner = "kr.betterastralparty.mod", GameRoot = game, AppId = id, Hash = hash }));
        var removed = NativeSteamMaintenance.Remove(steam, 11, game)!;
        Check(!NativeSteamMaintenance.PlanRemove(steam, 11, game), "only owned shortcut removed");
        Check(removed.Artwork.Count == 2 && !File.Exists(hero), "owned hero and receipt retained as no-overwrite backups");
        Check(File.ReadAllBytes(path).SequenceEqual(original), "unrelated shortcut remains byte-for-byte");
        NativeSteamMaintenance.Rollback(removed);
        Check(File.ReadAllBytes(path).SequenceEqual(current) && File.ReadAllBytes(hero).SequenceEqual(image), "rollback restores exact shortcut and owned artwork");
        var again = NativeSteamMaintenance.Remove(steam, 11, game)!;
        var backupBytes = File.ReadAllBytes(again.Backup!); File.WriteAllBytes(again.Backup!, new byte[] { 8 });
        Reject(() => NativeSteamMaintenance.Rollback(again), "rollback rejects changed original backup");
        Check(File.ReadAllBytes(path).SequenceEqual(original), "changed backup cannot change current shortcut file"); File.WriteAllBytes(again.Backup!, backupBytes);
        File.WriteAllBytes(path, new byte[] { 8 }); Reject(() => NativeSteamMaintenance.Rollback(again), "rollback never overwrites modified VDF");
        Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 8 }), "modified VDF preserved"); File.WriteAllBytes(path, original);
        Check(NativeSteamMaintenance.Remove(steam, 11, game) == null, "missing owned shortcut is a no-op");
        var alias = Path.Combine(steam, "inert-linked-shortcuts.vdf");
        if (!CreateHardLinkW(alias, path, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
            Reject(() => NativeSteamMaintenance.PlanRegister(steam, 11, game), "hard-linked profile data cannot authorize shortcut mutation");
            var skipped = 0; Check(!NativeSteamMaintenance.PlanRegister(steam, 11, game, _ => skipped++) && !NativeSteamMaintenance.PlanRemove(steam, 11, game, _ => skipped++) && skipped == 2,
                "optional linked shared shortcut data is preserved rather than accessed for mutation");
            Check(File.ReadAllBytes(path).SequenceEqual(original), "linked shortcuts and unrelated entries remain unchanged");
        }
        finally { File.Delete(alias); }
    }
    private static void ProcessPolicy(string steam, string game)
    {
        var selected = Process(10, "steam", Path.Combine(steam, "steam.exe"));
        var unrelated = Process(11, "steam", Path.Combine(steam + "Other", "steam.exe"));
        Check(NativeSteamMaintenance.ClassifySteam(new[] { unrelated }, steam, 7).State == "Stopped", "Steam path boundary preserves other installation");
        Check(NativeSteamMaintenance.ClassifySteam(new[] { selected }, steam, 7).State == "Running", "selected current session Steam");
        var otherSession = Process(12, "steamwebhelper", Path.Combine(steam, "bin", "steamwebhelper.exe"), 8);
        Check(NativeSteamMaintenance.ClassifySteam(new[] { selected, otherSession }, steam, 7).State == "OtherSession", "another selected Steam session blocks force closure");
        var unknown = new SteamProcessObservation { Pid = 13, Name = "steam" };
        Check(NativeSteamMaintenance.ClassifySteam(new[] { selected, otherSession, unknown }, steam, 7).State == "Unknown", "unknown Steam observation takes precedence");
        Check(NativeSteamMaintenance.ClassifySteam(new[] { new SteamProcessObservation { Pid = 24 } }, steam, 7).State == "Unknown", "unavailable process name/path cannot prove Steam stopped");
        unknown.Exited = true; Check(NativeSteamMaintenance.ClassifySteam(new[] { unknown }, steam, 7).State == "Stopped", "confirmed exit is not running");
        NativeSteamMaintenance.EnsureApprovedList(new[] { selected, unrelated }, new[] { selected }); checks++;
        Reject(() => NativeSteamMaintenance.EnsureApprovedList(new[] { selected }, new[] { Process(10, "steam", selected.Image, creation: selected.Creation + 1) }), "PID reuse invalidates consent");
        Reject(() => NativeSteamMaintenance.EnsureApprovedList(new[] { selected }, new[] { Process(14, "AstralParty_INT", Path.Combine(game, "AstralParty_INT.exe")) }), "new game invalidates force consent");
        Reject(() => NativeSteamMaintenance.EnsureApprovedList(new[] { selected }, new[] { Process(10, "steam", selected.Image, 8) }), "session change invalidates consent");
        var listedGame = Process(20, "AnotherInstalledGame", Path.Combine(game, "OtherGame.exe"));
        var helper = Process(21, "steamwebhelper", Path.Combine(steam, "bin", "steamwebhelper.exe"));
        var outside = Process(22, "UnrelatedApp", Path.Combine(game + "Outside", "outside.exe"));
        var targets = NativeSteamMaintenance.SelectSteamTargets(new[] { listedGame, outside, helper, selected, unrelated, otherSession }, steam, new[] { game }, 7);
        Check(targets.Count == 3 && targets.Contains(listedGame) && !targets.Contains(outside) && !targets.Contains(otherSession), "Steam force list includes only installed apps and selected current-session client");
        Check(NativeSteamMaintenance.SteamStopOrder(targets).Select(x => x.Pid).SequenceEqual(new uint[] { 10, 21, 20 }), "Steam client stops before helpers and listed installed apps");
        var updater = Process(23, "BetterAstralParty-UpdateHelper", Path.Combine(game, "BetterAstralParty-UpdateHelper.exe"));
        Reject(() => NativeSteamMaintenance.SelectSteamTargets(new[] { updater }, steam, new[] { game }, 7), "update helper is never a forced Steam target");
        Reject(() => NativeSteamMaintenance.SteamStopOrder(new[] { updater }).ToArray(), "update helper is rejected at final ordering boundary");
        unknown.Exited = false; Reject(() => NativeSteamMaintenance.SelectSteamTargets(new[] { unknown }, steam, new[] { game }, 7), "unknown Steam cannot become an approved force target");
        foreach (var item in new[] { new SteamProcessObservation { Pid = 31, Name = "steam", NativeError = 5 }, new SteamProcessObservation { Pid = 32, NativeError = 87 },
            Process(33, "steam", selected.Image, creation: 0) }) {
            if (item.Pid == 33) item.NativeError = 6;
            UnavailableDiagnostic(() => NativeSteamMaintenance.SelectSteamTargets(new[] { item }, steam, new[] { game }, 7), item, item.Name == "steam" ? "SteamClient" : "Other");
        }
        var missingGame = new SteamProcessObservation { Pid = 34, Name = "AstralParty_INT", NativeError = 5 };
        var gameFailure = (Exception)typeof(NativeSteamMaintenance).GetMethod("Failure", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null,
            new[] { typeof(string), typeof(string), typeof(SteamProcessObservation) }, null)!.Invoke(null, new object[] { "ProcessObservationUnavailable", "GameObservation", missingGame })!;
        UnavailableDiagnostic(() => { throw gameFailure; }, missingGame, "Game", "GameObservation");
        var shown = new System.Collections.Generic.List<string>();
        Check(NativeSteamMaintenance.ConfirmForce(new[] { selected, listedGame }, prompt => "y", shown.Add), "fresh y accepts only the shown force list");
        Check(shown.Count == 3 && shown[0].Contains("Unsaved progress") && shown[1].Contains("PID 10") && shown[2].Contains("PID 20"), "fresh consent shows data-loss warning and complete target list first");
        Check(!NativeSteamMaintenance.ConfirmForce(new[] { selected }, prompt => "r", shown.Add), "r rechecks without terminating");
        Reject(() => NativeSteamMaintenance.ConfirmForce(new[] { selected }, prompt => null, shown.Add), "blank unavailable consent cancels");
        Reject(() => NativeSteamMaintenance.ConfirmForce(new[] { selected }, prompt => "yes", shown.Add), "only explicit y allows force");
        try { NativeSteamMaintenance.ConfirmForce(new[] { selected }, prompt => { throw new IOException("fixture input unavailable"); }, shown.Add); throw new InvalidOperationException("Expected unavailable input cancellation."); }
        catch (InvalidOperationException error) {
            var context = new JavaScriptSerializer().DeserializeObject((string)error.Data["BapSteamDiagnostic"]!) as System.Collections.Generic.Dictionary<string, object>;
            Check(context != null && (string)context["reason"] == "ConsentUnavailable" && (string)context["phase"] == "ShutdownConsent", "noninteractive input failure carries bounded diagnostic context");
            Check(error.Data.Contains("BapSteamObservation"), "process diagnostic companion is preserved");
        }
        TerminationRaces(steam, helper);
        TerminationApiCases(steam);
    }
    private static void TerminationRaces(string steam, SteamProcessObservation target)
    {
        var observer = WindowsProcessGuard.Observer;
        var processes = NativeSteamMaintenance.ProcessObserver;
        var statusObserver = NativeSteamMaintenance.SteamStatusObserver;
        var stop = typeof(NativeSteamMaintenance).GetMethod("StopVerified", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        void Run()
        {
            try { stop.Invoke(null, new object[] { target }); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }
        void Refuse(int nativeError, string reason)
        {
            try { Run(); }
            catch (ApplySafetyException error) {
                Check(error.Failure == ApplyFailure.ProcessUncertain && error.NativeError == nativeError, reason);
                Check(error.Data.Contains("BapSteamDiagnostic") && error.Data.Contains("BapSteamObservation"), "termination failure preserves diagnostic context");
                return;
            }
            throw new InvalidOperationException(reason);
        }
        try {
            var enumerations = 0;
            WindowsProcessGuard.Observer = (pid, allowExited) => { Check(pid == target.Pid && allowExited, "termination observes the approved PID with exit allowed"); throw new ApplySafetyException(ApplyFailure.ProcessUncertain, 87); };
            NativeSteamMaintenance.ProcessObserver = () => { enumerations++; return Array.Empty<SteamProcessObservation>(); };
            Run(); Check(enumerations == 1, "Steam helper gone before handle capture does not fail removal");
            Check(WindowsProcessGuard.LastMetadataExitWaitMilliseconds == 2000, "approved target capture passes the bounded metadata-exit wait without changing broad scans");
            NativeSteamMaintenance.ProcessObserver = () => new[] { target };
            Refuse(87, "error 87 is not exit evidence while the approved PID remains listed");
            NativeSteamMaintenance.ProcessObserver = () => new[] { new SteamProcessObservation { Pid = target.Pid } };
            Refuse(87, "unavailable same-PID metadata cannot authorize skipping termination");
            NativeSteamMaintenance.ProcessObserver = () => new[] { Process(target.Pid, "UnrelatedApp", target.Image + ".other", creation: target.Creation + 1) };
            Refuse(87, "a reused PID is never treated as an absent approved process");
            NativeSteamMaintenance.ProcessObserver = () => { throw new IOException("inert enumeration failure"); };
            try { Run(); throw new InvalidOperationException("Expected enumeration failure."); }
            catch (IOException error) { Check(error.Data.Contains("BapSteamDiagnostic"), "failed fresh enumeration never authorizes a skip and retains diagnostics"); }
            WindowsProcessGuard.Observer = (pid, allowExited) => { throw new ApplySafetyException(ApplyFailure.ProcessUncertain, 5); };
            NativeSteamMaintenance.ProcessObserver = () => { throw new InvalidOperationException("Access denied must not query an exit fallback."); };
            Refuse(5, "access denied is not interpreted as natural process exit");
            Text(Path.Combine(steam, "steam.exe"), "inert client, never executed");
            foreach (var reappear in new[] { false, true }) {
                uint session; using (var self = System.Diagnostics.Process.GetCurrentProcess()) session = (uint)self.SessionId;
                var client = Process(51, "steam", Path.Combine(steam, "steam.exe"), session);
                var child = Process(52, "steamwebhelper", Path.Combine(steam, "bin", "steamwebhelper.exe"), session);
                var clientExited = false; var snapshots = 0; var statusReads = 0; var helperCaptures = 0;
                NativeSteamMaintenance.SteamStatusObserver = () => { statusReads++; return new SteamProcessStatus { State = clientExited ? "Stopped" : "Running" }; };
                NativeSteamMaintenance.ProcessObserver = () => {
                    snapshots++;
                    return !clientExited ? new[] { client, child } : reappear && snapshots >= 4
                        ? new[] { Process(53, "steamwebhelper", child.Image, session) } : Array.Empty<SteamProcessObservation>();
                };
                WindowsProcessGuard.Observer = (pid, allowExited) => {
                    if (pid == client.Pid) { clientExited = true; return new WindowsProcessGuard.Captured(); }
                    Check(pid == child.Pid && clientExited && allowExited, "client exits before its naturally disappearing helper is captured");
                    helperCaptures++; throw new ApplySafetyException(ApplyFailure.ProcessUncertain, 87);
                };
                var state = new SteamSessionState();
                if (!reappear) {
                    NativeSteamMaintenance.StopSteam(steam, state, prompt => "y", _ => { });
                    Check(state.Restart && state.Root == steam, "successful client-first shutdown retains restart intent without starting Steam");
                }
                else {
                    try { NativeSteamMaintenance.StopSteam(steam, state, prompt => "y", _ => { }); throw new Exception("Expected final process barrier."); }
                    catch (InvalidOperationException error) { Check(error.Message.Contains("SteamGameProcessesRemain"), "a new process after the absent-PID skip still blocks final shutdown completion"); }
                }
                Check(helperCaptures == 1 && snapshots == 4 && statusReads >= 5, "normal shutdown reaches final process barriers after the helper-exit race");
            }
        }
        finally { WindowsProcessGuard.Observer = observer; NativeSteamMaintenance.ProcessObserver = processes; NativeSteamMaintenance.SteamStatusObserver = statusObserver; }
    }
    private static void TerminationApiCases(string steam)
    {
        var observer = WindowsProcessGuard.Observer; var processes = NativeSteamMaintenance.ProcessObserver; var statusObserver = NativeSteamMaintenance.SteamStatusObserver;
        var open = NativeSteamMaintenance.TerminationOpen; var creation = NativeSteamMaintenance.TerminationCreation;
        var terminate = NativeSteamMaintenance.TerminationStop; var wait = NativeSteamMaintenance.TerminationWait; var lastError = NativeSteamMaintenance.TerminationNativeError;
        var stop = typeof(NativeSteamMaintenance).GetMethod("StopVerified", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        uint session; using (var self = System.Diagnostics.Process.GetCurrentProcess()) session = (uint)self.SessionId;
        var target = Process(91, "steamwebhelper", Path.Combine(steam, "bin", "steamwebhelper.exe"), session);
        var opens = 0; var stops = 0; var waits = 0; var heldWaits = 0;
        void Prepare(bool openDenied = false, bool terminateDenied = false, bool exitedAfterDenial = false)
        {
            target = Process(91, "steamwebhelper", Path.Combine(steam, "bin", "steamwebhelper.exe"), session);
            opens = stops = waits = heldWaits = 0; NativeSteamMaintenance.TerminationNativeError = 5;
            WindowsProcessGuard.Observer = (pid, allowExited) => {
                Check(pid == target.Pid && allowExited, "API termination keeps the approved captured identity");
                return new WindowsProcessGuard.Captured(target.Image, target.Session, target.Creation, () => false, milliseconds => {
                    Check(milliseconds == 2000, "access-denied exit confirmation is bounded to two seconds"); heldWaits++; return exitedAfterDenial;
                });
            };
            NativeSteamMaintenance.TerminationOpen = pid => { Check(pid == target.Pid, "terminate handle opens only approved PID"); opens++; return new Microsoft.Win32.SafeHandles.SafeWaitHandle(openDenied ? IntPtr.Zero : new IntPtr(1), false); };
            NativeSteamMaintenance.TerminationCreation = handle => target.Creation;
            NativeSteamMaintenance.TerminationStop = handle => { stops++; return !terminateDenied; };
            NativeSteamMaintenance.TerminationWait = (handle, milliseconds) => { Check(milliseconds == 5000, "termination wait remains bounded"); waits++; return 0; };
        }
        void Run()
        {
            try { stop.Invoke(null, new object[] { target }); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }
        void Failed(string reason, int nativeError, Type type, Action? action = null)
        {
            Exception? failure = null; try { (action ?? Run)(); } catch (Exception error) { failure = error; }
            Check(failure != null && failure.GetType() == type && target.NativeError == nativeError && !target.Exited, "live/uncertain process remains a truthful refusal: " + reason);
            var log = (string)failure!.Data["BapSteamLog"]!;
            Check((string)failure.Data["BapSteamLogStatus"]! == "included" && log.Contains("reason=" + reason + "; phase=ProcessTermination;")
                && log.Contains("nativeError=" + nativeError + "; exitAvailable=true; confirmedExit=false"), "exact native API/result reaches canonical diagnostics: " + reason);
        }
        try {
            foreach (var openDenied in new[] { true, false }) {
                Prepare(openDenied, !openDenied, true); Run();
                Check(target.Exited && opens == 1 && stops == (openDenied ? 0 : 1) && waits == 0 && heldWaits == 1, "already-terminating access denied succeeds only after same-held exit confirmation");
                Prepare(openDenied, !openDenied);
                Failed(openDenied ? "ProcessTerminateOpenFailed" : "ProcessTerminateFailed", 5, typeof(System.ComponentModel.Win32Exception));
                Check(heldWaits == 1 && waits == 0, "still-live denial is not retried or treated as exit");
            }
            Prepare(); NativeSteamMaintenance.TerminationWait = (handle, milliseconds) => { waits++; return 258; };
            Failed("ProcessExitTimeout", 0, typeof(InvalidOperationException));
            Check(waits == 1 && heldWaits == 0, "timeout is distinct from stale native error 5 and does not enter denied-exit fallback");
            Prepare(); NativeSteamMaintenance.TerminationNativeError = 6; NativeSteamMaintenance.TerminationWait = (handle, milliseconds) => uint.MaxValue;
            Failed("ProcessExitWaitFailed", 6, typeof(System.ComponentModel.Win32Exception));
            Check(heldWaits == 0, "failed native wait is not ignored as exit");
            foreach (var api in new[] { "open", "wait", "timeout" }) {
                Prepare(api == "open"); var exitReads = 0; NativeSteamMaintenance.TerminationNativeError = 6;
                WindowsProcessGuard.Observer = (pid, allowExited) => new WindowsProcessGuard.Captured(target.Image, target.Session, target.Creation, () => ++exitReads > 1);
                NativeSteamMaintenance.TerminationWait = (handle, milliseconds) => api == "timeout" ? 258U : uint.MaxValue;
                Run(); Check(target.Exited && exitReads == 2 && heldWaits == 0, "positive same-held exit still resolves a completed non-denial API failure: " + api);
            }
            Prepare(); Run(); Check(target.Exited && opens == 1 && stops == 1 && waits == 1 && heldWaits == 0, "successful native wait confirms actual exit");
            Prepare(true); WindowsProcessGuard.Observer = (pid, allowExited) => new WindowsProcessGuard.Captured(target.Image, target.Session, target.Creation,
                () => false, milliseconds => { throw new ApplySafetyException(ApplyFailure.ProcessUncertain, 6); });
            Failed("ProcessExitWaitFailed", 6, typeof(ApplySafetyException));
            Prepare(); NativeSteamMaintenance.TerminationCreation = handle => target.Creation + 1;
            Reject(Run, "reused termination PID is rejected before termination"); Check(stops == 0 && heldWaits == 0, "identity failure has no natural-exit bypass");
            Prepare(); WindowsProcessGuard.Observer = (pid, allowExited) => new WindowsProcessGuard.Captured(target.Image, target.Session + 1, target.Creation, () => false);
            Reject(Run, "changed process session is rejected"); Check(opens == 0 && stops == 0, "captured session mismatch never obtains a terminate handle");
            foreach (var captureDenied in new[] { false, true })
            foreach (var mode in new[] { "closed", "closed-helper", "new-pid", "cancel" }) {
                Prepare(true); var prompts = 0; var closed = false;
                if (captureDenied) WindowsProcessGuard.Observer = (pid, allowExited) => { throw new ApplySafetyException(ApplyFailure.ProcessUncertain, 5); };
                NativeSteamMaintenance.SteamStatusObserver = () => new SteamProcessStatus { State = closed ? "Stopped" : "Running" };
                NativeSteamMaintenance.ProcessObserver = () => closed ? mode == "closed-helper" ? new[] { Process(92, target.Name, target.Image, session) } : Array.Empty<SteamProcessObservation>() : new[] { target };
                string? Consent(string prompt) {
                    prompts++; if (prompts == 1) return "y";
                    if (prompts == 2 && mode != "cancel") { if (mode.StartsWith("closed", StringComparison.Ordinal)) closed = true; else target = Process(92, target.Name, target.Image, session, target.Creation + 1); return "r"; }
                    return "";
                }
                var state = new SteamSessionState();
                if (mode == "closed") { NativeSteamMaintenance.StopSteam(steam, state, Consent, _ => { }); Check(prompts == 2, "normal manual exit resumes without another termination attempt"); }
                else if (mode == "closed-helper") {
                    try { NativeSteamMaintenance.StopSteam(steam, state, Consent, _ => { }); throw new Exception("Expected remaining helper barrier."); }
                    catch (InvalidOperationException error) { Check(error.Message.Contains("SteamGameProcessesRemain") && prompts == 2, "manual Steam exit cannot skip the final remaining-process barrier"); }
                }
                else if (mode == "cancel") Failed(captureDenied ? "ProcessCaptureFailed" : "ProcessTerminateOpenFailed", 5,
                    captureDenied ? typeof(ApplySafetyException) : typeof(System.ComponentModel.Win32Exception), () => NativeSteamMaintenance.StopSteam(steam, state, Consent, _ => { }));
                else { Reject(() => NativeSteamMaintenance.StopSteam(steam, state, Consent, _ => { }), "replacement PID requires fresh force-close consent"); Check(prompts == 3, "explicit recheck does not authorize terminating replacement process"); }
                Check(opens == (captureDenied ? 0 : 1) && stops == 0 && heldWaits == (captureDenied ? 0 : 1), "manual recheck/refusal never auto-retries capture or native termination");
            }
            Prepare(); var nonDenialPrompts = 0;
            WindowsProcessGuard.Observer = (pid, allowExited) => { throw new ApplySafetyException(ApplyFailure.ProcessUncertain, 6); };
            NativeSteamMaintenance.SteamStatusObserver = () => new SteamProcessStatus { State = "Running" };
            NativeSteamMaintenance.ProcessObserver = () => new[] { target };
            Failed("ProcessCaptureFailed", 6, typeof(ApplySafetyException), () => NativeSteamMaintenance.StopSteam(steam, new SteamSessionState(), prompt => { nonDenialPrompts++; return "y"; }, _ => { }));
            Check(nonDenialPrompts == 1 && opens == 0 && stops == 0, "non-denial capture uncertainty is not swallowed or retried");
        }
        finally {
            WindowsProcessGuard.Observer = observer; NativeSteamMaintenance.ProcessObserver = processes; NativeSteamMaintenance.SteamStatusObserver = statusObserver;
            NativeSteamMaintenance.TerminationOpen = open; NativeSteamMaintenance.TerminationCreation = creation; NativeSteamMaintenance.TerminationStop = terminate;
            NativeSteamMaintenance.TerminationWait = wait; NativeSteamMaintenance.TerminationNativeError = lastError;
        }
    }
    private static Exception Diagnostic(string reason, string phase, SteamProcessStatus status)
    {
        return (Exception)typeof(NativeSteamMaintenance).GetMethod("Failure", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, null,
            new[] { typeof(string), typeof(string), typeof(SteamProcessStatus) }, null)!.Invoke(null, new object[] { reason, phase, status })!;
    }
    private static void TypedDiagnostics(string game)
    {
        var status = new SteamProcessStatus();
        var missing = new SteamProcessObservation { Pid = 6, Name = "steam", NativeError = 87 };
        var running = Process(42, "BetterAstralParty-Launcher", game + "\\private-path-not-for-export.exe"); running.PathMatch = false; running.SessionMatch = true;
        status.Facts.Add(running); status.Facts.Add(missing);
        var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var failure = Diagnostic("ProcessObservationUnavailable", "GameObservation", status);
            var log = (string)failure.Data["BapSteamLog"]!; var lines = log.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Check((string)failure.Data["BapSteamLogStatus"]! == "included" && log.Length <= 16384 && lines.Length == 6, "canonical typed diagnostic is bounded and includes complete process/observation pairs");
            Check(System.Text.RegularExpressions.Regex.IsMatch(lines[0], @"\ASteamDiagnostic: schema=1; reason=ProcessObservationUnavailable; phase=ShutdownObservation; utc=\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z; observedCount=2; omittedCount=0\z"), "native game observation phase maps to canonical invariant UTC header");
            Check(lines[1] == "SteamProcess: index=1; pid=6; sessionMatch=unknown; pathAvailable=false; pathMatch=unknown; startAvailable=false; startedUtc=unknown"
                && lines[2] == "SteamObservationProcess: index=1; pid=6; kind=SteamClient; querySource=Unavailable; nativeError=87; exitAvailable=false; confirmedExit=false", "unknown native identity and native error remain exact lowercase typed facts");
            Check(lines[3] == "SteamProcess: index=2; pid=42; sessionMatch=true; pathAvailable=true; pathMatch=false; startAvailable=true; startedUtc=" + DateTime.FromFileTimeUtc(running.Creation).ToString("o", CultureInfo.InvariantCulture)
                && lines[4] == "SteamObservationProcess: index=2; pid=42; kind=Launcher; querySource=Native; nativeError=0; exitAvailable=true; confirmedExit=false" && lines[5] == "SteamObservationCoverage: status=included", "observed path/session/start facts are rendered without names or paths");
            var legacy = (System.Collections.Generic.Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject((string)failure.Data["BapSteamDiagnostic"]!);
            Check((string)legacy["phase"] == "GameObservation" && failure.Data.Contains("BapSteamObservation") && !log.Contains(game), "legacy JSON cause stays unchanged and typed log exports no executable path");
            foreach (var mapping in new[] { new[] { "GameIdentity", "ProcessIdentity" }, new[] { "GameTermination", "ProcessTermination" } })
                Check(((string)Diagnostic("ProcessObservationUnavailable", mapping[0], status).Data["BapSteamLog"]!).Contains("; phase=" + mapping[1] + ";"), "private native game phase maps only at canonical diagnostic boundary");
            foreach (var cause in new[] { new[] { "HelperRunning", "HelperWait" }, new[] { "GameProcessesRemain", "GameCompletion" }, new[] { "InvalidSteamAppDirectory", "ShutdownObservation" },
                new[] { "SteamExecutableMissing", "ShutdownObservation" }, new[] { "Unknown", "ShutdownRecheck" }, new[] { "OtherSession", "ShutdownRecheck" }, new[] { "SteamGameProcessesRemain", "ShutdownCompletion" }, new[] { "SteamRunning", "ShortcutRollbackCommit" }, new[] { "HelperRunning", "GamePreflight" } }) {
                var causeFailure = Diagnostic(cause[0], cause[1], status);
                Check((string)causeFailure.Data["BapSteamLogStatus"]! == "included" && ((string)causeFailure.Data["BapSteamLog"]!).Contains("reason=" + cause[0] + "; phase=" + cause[1] + ";"), "finite native-only cause is preserved as a controlled collector record");
            }
            foreach (var invalid in new[] { new SteamProcessObservation { Pid = 0 }, new SteamProcessObservation { Pid = uint.MaxValue }, new SteamProcessObservation { Pid = 1, NativeError = -1 },
                new SteamProcessObservation { Pid = 1, Creation = long.MaxValue }, new SteamProcessObservation { Pid = 1, Exited = true } }) {
                var invalidStatus = new SteamProcessStatus(); invalidStatus.Facts.Add(invalid); var invalidFailure = Diagnostic("SteamRunning", "ShutdownObservation", invalidStatus);
                Check((string)invalidFailure.Data["BapSteamLogStatus"]! == "invalid-context" && (string)invalidFailure.Data["BapSteamLog"]! == "SteamObservationCoverage: status=invalid-context\r\n", "invalid synthetic facts cannot enter canonical diagnostic records");
            }
            Check((string)Diagnostic("SteamRunning\r\nInjected", "ShutdownObservation", status).Data["BapSteamLogStatus"]! == "invalid-context"
                && (string)Diagnostic("SteamRunning", "UnrecognizedPhase", status).Data["BapSteamLogStatus"]! == "invalid-context", "unrecognized reason or phase cannot inject text records");
            var many = new SteamProcessStatus(); for (uint offset = 0; offset < 70; offset++) { var item = Process(2000000000 + offset, "steamerrorreporter64", ""); item.NativeError = int.MaxValue; item.Available = false; many.Facts.Add(item); }
            var capped = Diagnostic("ProcessListChanged", "ShutdownRecheck", many); var cappedText = (string)capped.Data["BapSteamLog"]!;
            var cappedLines = cappedText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries); var pairs = (cappedLines.Length - 2) / 2;
            Check((string)capped.Data["BapSteamLogStatus"]! == "context-limit" && cappedText.Length <= 16384 && pairs > 0 && pairs < 48
                && cappedLines[0].EndsWith("observedCount=70; omittedCount=" + (70 - pairs).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "16KiB cap retains a complete paired prefix and recomputes exact omitted count");
            Check(cappedLines.Skip(1).Take(pairs * 2).Where((line, index) => line.StartsWith(index % 2 == 0 ? "SteamProcess: index=" : "SteamObservationProcess: index=", StringComparison.Ordinal)).Count() == pairs * 2
                && cappedLines.Last() == "SteamObservationCoverage: status=included", "paired truncation never leaves a partial fact or loses observation coverage");
        } finally { System.Threading.Thread.CurrentThread.CurrentCulture = culture; }
    }
}
