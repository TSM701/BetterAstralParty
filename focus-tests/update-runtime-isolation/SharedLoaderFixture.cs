using System.Text;
using System.Text.Json;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static class SharedLoaderPolicyFixture
{
    private static int Checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Checks++; }
    private static InstallReceipt Receipt(NativeMaintenanceBundle bundle, bool? baseline)
    {
        var channels = new ReceiptChannelState(bundle.RootIdentity, "Stable", new string('a', 32), ReleaseFeedPolicy.StableRepositoryId, "1.3.0", new string('B', 64), 0, null, "-");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, baseline = "1.3.0", owner = InstalledPluginMetadata.Owner, loaderBefore = baseline,
            files = InstallReceipt.Owned.Select(path => new { path, sha256 = new string('A', 64) }).ToArray(), channelState = channels.Encoded });
        return InstallReceipt.Parse(bytes);
    }
    public static int Main(string[] args)
    {
        try {
            SteamLauncher.Helper = null; SteamLauncher.AfterHelper = null;
            NativeMaintenance.Pending = false; NativeMaintenance.RestoreFailure = null; NativeMaintenance.CompletionFailure = null;
            NativeSteamMaintenance.RemoveFailure = null;
            if (args.Length != 2 || Directory.Exists(args[0]) || Path.GetDirectoryName(Path.GetFullPath(args[0])) != Path.GetFullPath(args[1])) throw new Exception("Fresh direct project artifact required.");
            Directory.CreateDirectory(args[0]); var bundle = new NativeMaintenanceBundle(args[0]);
            var loader = Path.Combine(args[0], "doorstop_config.ini");
            foreach (var baseline in new bool?[] { true, false, null }) foreach (var current in new[] { "true", "false", "malformed" }) {
                File.WriteAllText(loader, "[General]\nenabled=" + current + "\ncustom=keep\n", new UTF8Encoding(true));
                var before = File.ReadAllBytes(loader); var choice = "Keep"; string? replacement = null;
                if (choice != "Keep") replacement = NativeUninstall.LoaderChange(bundle, Receipt(bundle, baseline), choice, false, _ => throw new Exception("Default removal must not prompt."), _ => { });
                Check(replacement == null && before.SequenceEqual(File.ReadAllBytes(loader)), "Default removal preserves exact loader ON/OFF/unknown bytes for all recorded baselines");
            }
            foreach (var choice in new[] { "Enabled", "Disabled" }) {
                File.WriteAllText(loader, "[General]\nenabled=false\ncustom=keep\n");
                var changed = NativeUninstall.LoaderChange(bundle, null, choice, false, _ => throw new Exception("Known choice must not prompt."), _ => { });
                Check(changed == "[General]\nenabled=" + (choice == "Enabled" ? "true" : "false") + "\ncustom=keep\n", "Explicit choice preserves unrelated configuration");
            }
            foreach (var baseline in new[] { true, false }) foreach (var current in new[] { "true", "false" }) {
                File.WriteAllText(loader, "[General]\nenabled=" + current + "\ncustom=keep\n");
                var changed = NativeUninstall.LoaderChange(bundle, Receipt(bundle, baseline), "Recorded", false, _ => throw new Exception("Recorded choice must not prompt."), _ => { });
                Check(changed == "[General]\nenabled=" + (current == "true" || baseline ? "true" : "false") + "\ncustom=keep\n", "Recorded ON baseline never disables existing ON loader");
            }
            File.WriteAllText(loader, "[General]\nenabled=false\ncustom=keep\n");
            Check(NativeUninstall.LoaderChange(bundle, Receipt(bundle, null), "Recorded", false, _ => "1", _ => { }) == "[General]\nenabled=true\ncustom=keep\n", "Unknown recorded baseline uses explicit choice");
            foreach (var answer in new string?[] { null, "", "y", "3" }) {
                var rejected = false; try { NativeUninstall.LoaderChange(bundle, Receipt(bundle, null), "Recorded", false, _ => answer, _ => { }); } catch (OperationCanceledException) { rejected = true; }
                Check(rejected && File.ReadAllText(loader) == "[General]\nenabled=false\ncustom=keep\n", "Unknown loader choice cancels without file writes");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks = Checks, gameSteamOrUserInstallationUsed = false, sharedLoaderOnly = true })); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
