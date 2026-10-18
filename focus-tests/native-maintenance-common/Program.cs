#nullable enable
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Web.Script.Serialization;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static partial class NativeCommonFixture
{
    private static string Scratch = "";
    private static int Checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Checks++; Console.WriteLine("PASS: " + name); }
    private static void Reject(Action body, string name)
    {
        try { body(); } catch (Exception error) when (error is InvalidDataException || error is ApplySafetyException || error is ArgumentException || error is InvalidOperationException)
        { Check(true, name); return; }
        throw new Exception("Accepted: " + name);
    }
    internal static void AssertScope(string path)
    { if (!WindowsPath.Absolute(path).StartsWith(Scratch + "\\", StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture escaped artifact scratch"); }
    private static string Io(string path) => WindowsPath.ForIo(WindowsPath.FromIo(path.Replace('/', '\\')));
    private static byte[] Read(string path) { return File.ReadAllBytes(WindowsPath.ForIo(path)); }
    private static void Write(string path, byte[] bytes)
    { AssertScope(path); Directory.CreateDirectory(WindowsPath.ForIo(WindowsPath.Parent(path))); File.WriteAllBytes(WindowsPath.ForIo(path), bytes); }
    private static Dictionary<string, string> Snapshot(string root)
    { return Directory.GetFiles(WindowsPath.ForIo(root), "*", SearchOption.AllDirectories).Where(path => WindowsPath.Leaf(WindowsPath.FromIo(path)) != "pending.json.lock").ToDictionary(WindowsPath.FromIo, path => UpdateTrust.Hash(Read(WindowsPath.FromIo(path))), StringComparer.Ordinal); }
    private static void Unchanged(Dictionary<string, string> before, string root, string name)
    { var after = Snapshot(root); Check(after.Count == before.Count && before.All(pair => after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value), name); }
    private sealed class Bundle : IDisposable
    {
        internal readonly NativeMaintenanceBundle Value;
        internal Bundle(string name)
        {
            var game = Scratch + "\\" + name; var package = Scratch + "\\package-" + name;
            Directory.CreateDirectory(WindowsPath.ForIo(game)); Directory.CreateDirectory(WindowsPath.ForIo(package));
            Write(game + "\\AstralParty_INT.exe", Encoding.ASCII.GetBytes("inert common fixture, never executed"));
            Value = (NativeMaintenanceBundle)FormatterServices.GetUninitializedObject(typeof(NativeMaintenanceBundle));
            using (var fence = new WindowsFileFence(game)) Set("RootIdentity", fence.RootIdentity.Text);
            Set("GameRoot", game); Set("PackageRoot", package); Set("Version", "1.4.0"); Set("ManifestHash", new string('A', 64)); Set("BuildIdentity", new string('B', 64));
            Set("Source", new WindowsFileFence(package));
            Set("_files", new Dictionary<string, WindowsFileFence.FileLease>(StringComparer.OrdinalIgnoreCase));
            Set("Expected", InstallReceipt.Owned.ToDictionary(key => key, key => new string('A', 64), StringComparer.Ordinal));
            Set("Hashes", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { ".deps/bepinex/BepInEx/core/fixture.dll", new string('C', 64) } });
            Value.Mode = null; Value.Observed = null;
        }
        private void Set(string name, object value) { typeof(NativeMaintenanceBundle).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Value, value); }
        public void Dispose() { Value.Dispose(); }
    }
    private static void JsonCases()
    {
        var value = MaintenanceJson.Read(Encoding.UTF8.GetBytes("{\"schema\":1,\"zero\":0,\"negative\":-17,\"large\":2147483648,\"fraction\":1.5,\"text\":\"a\\n\\r\\t\\b\\f\\/\\\"\\\\\\uAC00\",\"ok\":true,\"nil\":null}"));
        Check(MaintenanceJson.Number(value, "zero") == 0 && MaintenanceJson.Number(value, "negative") == -17 && (long)value["large"]! == 2147483648L,
            "general JSON numeric values preserve types and bounds");
        Check(MaintenanceJson.Text(value, "text") == "a\n\r\t\b\f/\"\\가", "general JSON escapes decoded exactly");
        var text = "quotes\" backslash\\ newline\n tab\t control\b 가";
        var roundtrip = MaintenanceJson.Read(MaintenanceJson.Write(new { text, count = 17, paths = new[] { "one", "two" } }));
        Check(MaintenanceJson.Text(roundtrip, "text") == text && MaintenanceJson.Number(roundtrip, "count") == 17 && MaintenanceJson.List(roundtrip, "paths").Count == 2,
            "Framework JSON writer roundtrips escapes and general integers");
        var bom = new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("{\"schema\":1}")).ToArray();
        Check(MaintenanceJson.Number(MaintenanceJson.Read(bom), "schema") == 1, "UTF-8 BOM accepted");
        foreach (var bad in new[] { "{\"a\":1,\"a\":2}", "{\"a\":1,\"A\":2}", "{\"a\":{\"b\":1,\"B\":2}}", "{\"a\":1,\"\\u0061\":2}", "[]", "null", "{\"n\":01}", "{\"n\":NaN}", "{\"x\":\"\\x20\"}" })
            Reject(() => MaintenanceJson.Read(Encoding.UTF8.GetBytes(bad)), "JSON trust boundary rejects " + bad);
        Reject(() => MaintenanceJson.Number(value, "large"), "32-bit schema/length accessor rejects a long");
        Reject(() => MaintenanceJson.Read(new byte[] { 0xff }), "invalid UTF-8 rejected");
        Reject(() => MaintenanceJson.Read(Encoding.UTF8.GetBytes("{\"a\":1}"), 2), "JSON byte limit enforced");
        Reject(() => MaintenanceJson.Read(Encoding.UTF8.GetBytes("{\"a\":[1,2,3]}"), rows: 2), "JSON row limit enforced");
        Reject(() => MaintenanceJson.Read(Encoding.UTF8.GetBytes("{\"a\":\"" + new string('x', WindowsPath.MaximumLength + 1) + "\"}")), "JSON text limit enforced");
        Reject(() => MaintenanceJson.Read(Encoding.UTF8.GetBytes("{\"a\":" + new string('[', 17) + "1" + new string(']', 17) + "}")), "JSON nesting limit enforced");
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateHardLinkW(string path, string source, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateSymbolicLinkW(string path, string source, uint flags);
    private static string[] Arguments(string command)
    {
        int count; var memory = CommandLineToArgvW("fixture.exe " + command, out count); if (memory == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try { return Enumerable.Range(1, count - 1).Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, index * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(memory); }
    }
    private static void OptionsCases()
    {
        var options = NativeMaintenanceEntry.Options(new[] { "--install", "-GameRoot", "E:\\fixture root", "-UpdateOnly", "--CheckOnly" });
        Check(options["gameroot"] == "E:\\fixture root" && options.ContainsKey("updateonly") && options.ContainsKey("checkonly"), "legacy field/switch names accepted");
        Check(NativeMaintenanceEntry.Options(new[] { "--uninstall", "-RestoreLoader", "enabled", "-WhatIf" }).ContainsKey("whatif"), "RestoreLoader values retain case-insensitive compatibility");
        var emergency = NativeMaintenanceEntry.Options(new[] { "--force-uninstall", "-GameRoot", "E:\\fixture root", "-AccountId", "1", "-WhatIf" });
        Check(emergency.ContainsKey("force-uninstall") && emergency.ContainsKey("whatif") && emergency["gameroot"] == "E:\\fixture root", "emergency removal is a separate native operation with read-only preview");
        Check(NativeMaintenanceEntry.Options(new[] { "--force-uninstall", "-CheckOnly" }).ContainsKey("checkonly"), "emergency CheckOnly retains the read-only preview alias");
        foreach (var bad in new[] { "0", "-1", "+1", " 1", "4294967296", "not-an-account" })
            Reject(() => NativeMaintenanceEntry.Options(new[] { "--uninstall", "-AccountId", bad, "-WhatIf" }), "explicit invalid account is not auto-discovery: " + bad);
        foreach (var error in new Exception[] { new UnauthorizedAccessException("FAKE_PRIVATE_REGISTRY_PATH"), new System.Security.SecurityException("FAKE_PRIVATE_REGISTRY_VALUE"), new IOException("FAKE_PRIVATE_REGISTRY_READ"), new ArgumentException("FAKE_PRIVATE_REGISTRY_PATH") })
        {
            NativeSteamMaintenance.RootFailure = error;
            try {
                var observations = new List<string>();
                Check(NativeMaintenanceEntry.FindSteamRoot(true, observations.Add) == null && observations.Count == 1
                    && observations[0].Contains("SteamRootDiscoveryUnavailable; exception=" + error.GetType().Name + "; hresult=0x") && !observations[0].Contains("FAKE_PRIVATE"),
                    "normal automatic Steam root failure is bounded optional discovery: " + error.GetType().Name);
                var rejected = false; try { NativeMaintenanceEntry.FindSteamRoot(false, _ => throw new Exception("Required discovery must not emit skip")); }
                catch (Exception required) { Check(ReferenceEquals(required, error), "required installation or explicit-account discovery preserves original error"); rejected = true; }
                Check(rejected, "required Steam discovery still fails: " + error.GetType().Name);
            } finally { NativeSteamMaintenance.RootFailure = null; }
        }
        Reject(() => NativeMaintenanceEntry.Consent("inert emergency confirmation"), "redirected input cannot confirm emergency removal");
        foreach (var bad in new[] { new string[0], new[] { "--install", "--uninstall" }, new[] { "--install", "-GameRoot" }, new[] { "--install", "--install" },
            new[] { "--install", "-WhatIf", "--WHATIF" }, new[] { "--install", "--unknown" }, new[] { "--install", "-RestoreLoader", "Keep" }, new[] { "--uninstall", "-RecoveryPlan", "E:\\plan.json" }, new[] { "--uninstall", "-RestoreLoader", "unknown" },
            new[] { "--force-uninstall", "--install" }, new[] { "--force-uninstall", "--uninstall" }, new[] { "--force-uninstall", "--force-uninstall" }, new[] { "--force-uninstall", "-UpdateOnly" },
            new[] { "--force-uninstall", "-RestartSteam" }, new[] { "--force-uninstall", "-RecoveryPlan", "E:\\plan.json" }, new[] { "--force-uninstall", "-RestoreLoader", "Enabled" } })
            Reject(() => NativeMaintenanceEntry.Options(bad), "unsupported/ambiguous CLI rejected");
        var args = new[] { "--install", "", "spaces name", "E:\\trailing slash\\", "embedded\"quote", "E:\\backslash\\\"quote", "가 [x] & % ! ^ | < >" };
        Check(Arguments(NativeMaintenanceEntry.CommandLine(args)).SequenceEqual(args), "native command-line quoting roundtrips quotes, slashes, empty and special text");
        foreach (var bad in new[] { "nul\0", "newline\n", "return\r" }) Reject(() => NativeMaintenanceEntry.CommandLine(new[] { bad }), "command-line controls rejected");
    }
    private static void Transactions()
    {
        using (var holder = new Bundle("restore"))
        {
            var bundle = holder.Value; var root = bundle.GameRoot;
            Check(WindowsPath.Parent(root) == Environment.GetEnvironmentVariable("LOCALAPPDATA"), "fixture profile and actual ordinary-recovery namespace remain identical");
            var old = root + "\\BetterAstralParty-Mod.cmd"; var created = root + "\\BetterAstralParty.compatibility.json";
            Write(old, Encoding.UTF8.GetBytes("old settings and bytes 가")); var oldBytes = Read(old);
            using (var lease = NativeMaintenance.AcquireInstallLock(bundle))
            {
                Check(lease.File.Path == NativeMaintenance.JournalPath(root) + ".lock", "native held backup lock has the canonical scope");
                Reject(() => NativeMaintenance.AcquireInstallLock(bundle), "second backup lock cannot bypass held operation");
                var before = Snapshot(root); NativeMaintenance.TestWritable(new[] { old, created }); Unchanged(before, root, "writable probes leave no target changes or probe files");
                var txn = NativeMaintenance.StartTransaction(bundle, new[] { old, created, old }, 8192);
                Check(txn.Entries.Count == 2 && NativeMaintenance.HasPending(bundle) && UpdateTrust.Hash(Read(txn.JournalPath)) == txn.JournalHash, "transaction deduplicates targets and publishes exact durable journal");
                var parsed = NativeMaintenance.ReadTransaction(bundle, new[] { old, created }, txn.JournalHash);
                Check(parsed.JournalHash == txn.JournalHash && parsed.Entries.Any(entry => !entry.Existed && entry.Hash == null), "strict native journal reader validates present and absent origins");
                Reject(() => NativeMaintenance.ReadTransaction(bundle, new[] { old, created }, new string('F', 64)), "changed confirmation hash rejected");
                Reject(() => NativeMaintenance.StartTransaction(bundle, new[] { old }, 0), "pending journal prevents replacing evidence");
                var snapshot = Snapshot(root); var backups = Snapshot(txn.JournalRoot); var prompts = 0;
                Check(!NativeMaintenance.RecoverPending(bundle, prompt => { prompts++; return "y"; }, _ => { }, true), "pending WhatIf reports required recovery");
                Check(prompts == 0 && NativeSteamMaintenance.Stops == 0, "preview does not prompt or stop a game");
                Unchanged(snapshot, root, "preview leaves game bytes untouched"); Unchanged(backups, txn.JournalRoot, "preview leaves all journal/backup bytes untouched");
                Reject(() => NativeMaintenance.RecoverPending(bundle, _ => "n", _ => { }), "explicit refusal stops pending recovery");
                Write(old, Encoding.ASCII.GetBytes("reviewed new bytes")); Write(created, Encoding.ASCII.GetBytes("reviewed new file"));
                var expected = new Dictionary<string, string?> { [old] = UpdateTrust.Hash(Read(old)), [created] = UpdateTrust.Hash(Read(created)) };
                using (var maintenance = new UpdateLaunchGate.Lease(root)) NativeMaintenance.RestoreTransaction(bundle, txn, new[] { old, created }, expected);
                Check(Read(old).SequenceEqual(oldBytes) && !File.Exists(Io(created)), "actual held-handle restore recovers original and removes only reviewed new file");
                Check(!NativeMaintenance.HasPending(bundle) && UpdateTrust.Hash(Read(txn.BackupDir + "\\restored.json")) == txn.JournalHash, "restore retains exact immutable journal evidence");
            }
        }
        using (var holder = new Bundle("complete"))
        {
            var bundle = holder.Value; var target = bundle.GameRoot + "\\BetterAstralParty-Mod.cmd"; Write(target, Encoding.ASCII.GetBytes("origin"));
            using (NativeMaintenance.AcquireInstallLock(bundle))
            {
                var txn = NativeMaintenance.StartTransaction(bundle, new[] { target }, 4096); Write(target, Encoding.ASCII.GetBytes("target"));
                using (var maintenance = new UpdateLaunchGate.Lease(bundle.GameRoot)) NativeMaintenance.CompleteTransaction(bundle, txn, new[] { target });
                Check(Encoding.ASCII.GetString(Read(target)) == "target" && !NativeMaintenance.HasPending(bundle), "actual completion preserves target bytes and retires pending journal");
                Check(UpdateTrust.Hash(Read(txn.BackupDir + "\\completed.json")) == txn.JournalHash, "completion preserves exact original journal bytes");
            }
        }
        using (var holder = new Bundle("unsafe"))
        {
            var bundle = holder.Value; var target = bundle.GameRoot + "\\BetterAstralParty-Mod.cmd"; Write(target, Encoding.ASCII.GetBytes("origin"));
            Reject(() => NativeMaintenance.AssertTarget(bundle.GameRoot, Scratch + "\\outside.keep"), "target outside game rejected");
            Reject(() => NativeMaintenance.StartTransaction(bundle, new string[0], 0), "empty backup plan rejected");
            Reject(() => NativeMaintenance.StartTransaction(bundle, new[] { target }, -1), "negative install budget rejected");
            var link = bundle.GameRoot + "\\hardlink.cmd";
            if (!CreateHardLinkW(Io(link), Io(target), IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Reject(() => NativeMaintenance.AssertTarget(bundle.GameRoot, target), "hard-linked target rejected before mutation");
            Reject(() => NativeMaintenance.TestWritable(new[] { target }), "writable preflight cannot follow a hard link");
            Reject(() => NativeMaintenance.AssertTarget(bundle.GameRoot, bundle.GameRoot + "\\AstralParty_INT.exe\\child"), "file ancestor cannot masquerade as a directory");
        }
    }
    private static void EmptyDirectoryCases()
    {
        var names = new[] { "BepInEx/plugins/BetterAstralParty", "BepInEx/BetterAstralParty-Diagnostics" };
        var roots = new[] { Scratch + "\\cleanup", Scratch + "\\가 긴 경로 " + new string('x', 70) + "\\" + new string('y', 80) + "\\" + new string('z', 80) };
        foreach (var root in roots) {
            AssertScope(root); Directory.CreateDirectory(WindowsPath.ForIo(root));
            foreach (var name in names) {
                using (var create = new WindowsFileFence(root)) create.EnsureDirectory(name);
                using (var remove = new WindowsFileFence(root)) remove.RemoveEmptyMaintenanceDirectory(name);
                Check(!Directory.Exists(WindowsPath.ForIo(root + "\\" + name.Replace('/', '\\'))), "held-handle empty cleanup accepts exact allowlist at " + (root.Length > 260 ? "Unicode long root" : "short root"));
            }
            using (var fence = new WindowsFileFence(root)) {
                Reject(() => fence.RemoveEmptyMaintenanceDirectory("BepInEx/plugins"), "cleanup cannot remove a wider shared directory");
                Reject(() => fence.RemoveEmptyMaintenanceDirectory("BepInEx/plugins/betterastralparty"), "cleanup allowlist remains exact, including casing");
            }
        }
        var nonempty = Scratch + "\\cleanup-nonempty"; Directory.CreateDirectory(Io(nonempty));
        using (var create = new WindowsFileFence(nonempty)) create.EnsureDirectory(names[0]);
        var keep = nonempty + "\\BepInEx\\plugins\\BetterAstralParty\\foreign.keep"; Write(keep, Encoding.ASCII.GetBytes("foreign file must survive"));
        var hash = UpdateTrust.Hash(Read(keep));
        using (var remove = new WindowsFileFence(nonempty)) Reject(() => remove.RemoveEmptyMaintenanceDirectory(names[0]), "nonempty allowlisted directory cannot be removed");
        Check(UpdateTrust.Hash(Read(keep)) == hash, "nonempty cleanup preserves foreign contents exactly");
        var linked = Scratch + "\\cleanup-linked"; var foreign = Scratch + "\\foreign-directory";
        Directory.CreateDirectory(Io(linked)); Directory.CreateDirectory(Io(foreign));
        using (var create = new WindowsFileFence(linked)) create.EnsureDirectory("BepInEx/plugins");
        var link = linked + "\\BepInEx\\plugins\\BetterAstralParty";
        Write(foreign + "\\foreign.keep", Encoding.ASCII.GetBytes("foreign linked bytes"));
        if (!CreateSymbolicLinkW(WindowsPath.ForIo(link), WindowsPath.ForIo(foreign), 3)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Artifact-only directory symlink fixture unavailable");
        using (var remove = new WindowsFileFence(linked)) Reject(() => remove.RemoveEmptyMaintenanceDirectory(names[0]), "cleanup refuses an allowlisted linked directory");
        Check(Directory.Exists(WindowsPath.ForIo(link)) && Encoding.ASCII.GetString(Read(foreign + "\\foreign.keep")) == "foreign linked bytes", "linked cleanup preserves both link and foreign data");
    }
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 4 || Directory.Exists(Io(args[0])) || WindowsPath.Parent(WindowsPath.Absolute(args[0])) != WindowsPath.Absolute(args[1])) throw new ArgumentException("Fresh direct artifact scratch required");
        Scratch = WindowsPath.Absolute(args[0]); Directory.CreateDirectory(Io(Scratch)); var oldLocal = Environment.GetEnvironmentVariable("LOCALAPPDATA"); Environment.SetEnvironmentVariable("LOCALAPPDATA", Scratch);
        try
        {
            var mode=args.Length>2?args[2]:"common";
            if(mode=="bundle-missing") BundleMissing();
            else if(mode=="reader-origin") ReaderOrigin(args[3]);
            else if(mode.StartsWith("transaction-",StringComparison.Ordinal)) TransactionScenario(mode);
            else if(mode=="preservation") {Transactions();EmptyDirectoryCases();}
            else if(mode=="safety") {JsonCases();OptionsCases();Transactions();EmptyDirectoryCases();}
            else if(mode=="common") {JsonCases();OptionsCases();Transactions();EmptyDirectoryCases();BundleMissing();}
            else throw new ArgumentException("Unknown native semantic matrix: "+mode);
            Write(Scratch + "\\verification.json", MaintenanceJson.Write(new { Passed = true, Checks, ActualBundleAuthenticationExcluded = true, SteamFacade = true,
                RealWindowsFileFenceAndOrdinaryRecovery = true, LiveGameSteamInstallationOrSigningUsed = false }));
            Console.WriteLine("Native maintenance common: " + Checks + " assertions PASS. Evidence: " + Scratch); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", oldLocal); }
    }
}
