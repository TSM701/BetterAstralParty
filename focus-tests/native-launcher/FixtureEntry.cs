using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

namespace BetterAstralParty
{
    internal static class Plugin
    {
        public const string Guid = "kr.betterastralparty.mod", Version = FixtureBoundary.Version, UpdateProtocol = "4", SettingsSchema = "1";
    }
}

internal static class FixtureEntry
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    private static readonly string[] Names = { "SteamAppId", "SteamGameId", "SteamOverlayGameId", "BAP_COMPATIBILITY_CHECKED", "BAP_UPDATER_LAUNCHER_PID", "BAP_UPDATER_LAUNCHER_CREATION", "BAP_UPDATER_LAUNCHER_SESSION", "PSModulePath" };
    private static string Snapshot()
    {
        var text = new StringBuilder();
        foreach (var name in Names) text.Append(name).Append('=').Append(Environment.GetEnvironmentVariable(name) ?? "<absent>").Append('\n');
        return text.ToString();
    }
    private static void Setup(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "BepInEx", "config"));
        foreach (var path in InstallReceipt.Owned) {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (path == UpdateTrust.PluginPath) File.Copy(typeof(FixtureEntry).Assembly.Location, full, true);
            else if (!File.Exists(full)) File.WriteAllText(full, "inert fixture input; never execute\n");
        }
        Compatibility(root);
        var receipt = new StringBuilder("{\"schema\":1,\"baseline\":\"1.3.0\",\"owner\":\"" + InstalledPluginMetadata.Owner + "\",\"loaderBefore\":false,\"files\":[");
        foreach (var path in InstallReceipt.Owned) {
            if (receipt[receipt.Length - 1] != '[') receipt.Append(',');
            receipt.Append("{\"path\":\"").Append(path).Append("\",\"sha256\":\"")
                .Append(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))).Append("\"}");
        }
        var descriptor = File.ReadAllBytes(Path.Combine(root, "fixture-helper.descriptor"));
        var signature = File.ReadAllBytes(Path.Combine(root, "fixture-helper.signature"));
        var image = File.ReadAllBytes(Path.Combine(root, "fixture-helper.image"));
        var verified = HelperDescriptor.Verify(UpdateTrust.Production(), descriptor, DetachedSignature.Parse(signature), image);
        using (var fence = new WindowsFileFence(root)) {
            var channels = new ReceiptChannelState(fence.RootIdentity.Text, "Stable", Guid.NewGuid().ToString("N"),
                ReleaseFeedPolicy.StableRepositoryId, FixtureBoundary.Version, new string('A', 64), 0, null, "-");
            receipt.Append("],\"channelState\":\"").Append(channels.Encoded).Append("\"}");
            File.WriteAllText(Path.Combine(root, InstallReceipt.Path), receipt.ToString());
            InstallReceipt.Parse(Encoding.ASCII.GetBytes(receipt.ToString()));
            var work = UpdateWorkPath.New("helper"); var workId = UpdateWorkPath.Create(fence, work).Text;
            HelperDelivery.Write(fence, work + "/descriptor", descriptor); HelperDelivery.Write(fence, work + "/signature", signature);
            HelperDelivery.Write(fence, work + "/image", image);
            string imageId; using (var file = fence.OpenFile(work + "/image")) imageId = file.Identity.Text;
            HelperDelivery.Write(fence, work + "/receipt", HelperDelivery.WholeReceiptBytes(fence, verified, imageId));
            string receiptId; using (var file = fence.OpenFile(work + "/receipt")) receiptId = file.Identity.Text;
            var plan = "BetterAstralParty.HelperInstall/v1\nroot=" + fence.RootIdentity.Text + "\nwork=" + work + "\nwork-id=" + workId + "\ndescriptor=" + verified.DescriptorHash + "\nimage-id=" + imageId + "\nreceipt-id=" + receiptId + "\n";
            HelperDelivery.Write(fence, work + "/plan", Encoding.ASCII.GetBytes(plan));
            HelperDelivery.FinishWholeMigration(fence, work, UpdateTrust.Production(), null);
            if (HelperDelivery.Fingerprint(fence, UpdateTrust.Production(), 4) != verified.Sha256) throw new IOException("Fixture helper proof did not validate");
        }
    }
    private static void Compatibility(string root)
    {
        var cache = Path.Combine(root, "fixture-cache"); Directory.CreateDirectory(cache);
        var rows = new List<object>();
        foreach (var name in new[] { "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll",
            "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle", "AstralParty_INT_Data/StreamingAssets/aa/settings.json",
            "AstralParty_INT_Data/data.unity3d", "BepInEx/core/BepInEx.Core.dll" })
        {
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllBytes(path, Encoding.ASCII.GetBytes(name.EndsWith("data.unity3d", StringComparison.Ordinal) ? "UnityFS\0" + new string('x', 56) : "inert native compatibility: " + name));
            rows.Add(new { root = "game", path = name, kind = "fixture", sha256 = UpdateTrust.Hash(File.ReadAllBytes(path)) });
        }
        var catalogs = new[] { "catalog_3.2.0.hash", "catalog_3.2.0.json" };
        foreach (var name in catalogs)
        {
            var path = Path.Combine(cache, name); if (!File.Exists(path)) File.WriteAllText(path, "inert current catalog: " + name);
            rows.Add(new { root = "cache", path = name, kind = "catalog", sha256 = UpdateTrust.Hash(File.ReadAllBytes(path)) });
        }
        var bytes = MaintenanceJson.Write(new { schema = 1, cacheCatalogs = catalogs, files = rows });
        var behavior = File.ReadAllText(Path.Combine(root, "checker-behavior"));
        if (behavior == "parser") bytes = Encoding.ASCII.GetBytes("{ malformed fixture manifest");
        File.WriteAllBytes(Path.Combine(root, "BetterAstralParty.compatibility.json"), bytes);
        if (behavior == "reject") File.WriteAllText(Path.Combine(root, "winhttp.dll"), "changed inert shared loader");
        if (behavior == "drift") {
            File.WriteAllText(Path.Combine(root, "GameAssembly.dll"), "updated inert game content");
            File.WriteAllText(Path.Combine(cache, "catalog_3.4.0.hash"), "updated inert catalog");
        }
        if (behavior == "warning") File.WriteAllBytes(Path.Combine(root, "AstralParty_INT_Data", "data.unity3d"), Encoding.ASCII.GetBytes("UnityFS\0" + new string('y', 56)));
    }
    private static void RefreshCompatibility(string root)
    {
        Compatibility(root);
        var path = Path.Combine(root, InstallReceipt.Path); var receipt = MaintenanceJson.Read(File.ReadAllBytes(path));
        foreach (var raw in MaintenanceJson.List(receipt, "files")) {
            var row = MaintenanceJson.Map(raw);
            if (MaintenanceJson.Text(row, "path") == "BetterAstralParty.compatibility.json") row["sha256"] = UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, "BetterAstralParty.compatibility.json")));
        }
        File.WriteAllBytes(path, MaintenanceJson.Write(receipt));
    }
    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!root.StartsWith(FixtureBoundary.Root, StringComparison.OrdinalIgnoreCase)) return 90;
        try {
            if (args.Length == 1 && args[0] == "--setup") { Setup(root); return 0; }
            if (args.Length == 1 && args[0] == "--refresh-compatibility") { RefreshCompatibility(root); return 0; }
            if (args.Length == 1 && args[0] == "--resources") {
                foreach (var name in typeof(SteamLauncher).Assembly.GetManifestResourceNames()) {
                    if (name.StartsWith("BetterAstralParty.HelperBootstrap.", StringComparison.Ordinal)) throw new IOException("Legacy helper resource remains");
                    using (var input = typeof(SteamLauncher).Assembly.GetManifestResourceStream(name)!) {
                        if (input.ReadByte() == 77 && input.ReadByte() == 90) throw new IOException("Embedded PE remains");
                    }
                }
                File.WriteAllText(Path.Combine(root, "resource-result"), "NoLegacyHelperOrEmbeddedPE"); return 0;
            }
            if (args.Length == 1 && args[0] == "--probe") {
                try { using (SteamLauncher.GuardMaintenance(root)) File.WriteAllText(Path.Combine(root, "probe-result"), "Available"); }
                catch (ApplySafetyException error) { File.WriteAllText(Path.Combine(root, "probe-result"), error.Failure.ToString()); }
                return 0;
            }
            if (args.Length != 0) return 91;
            var before = Snapshot();
            using (var self = Process.GetCurrentProcess()) File.WriteAllText(Path.Combine(root, "host-identity"), self.Id + "\n" + self.StartTime.ToFileTimeUtc() + "\n" + self.SessionId + "\n" + GetConsoleWindow().ToInt64() + "\n");
            var failure=Path.Combine(root,"loader-write-failure");
#if BAP_LOADER_FIXTURE
            if(File.Exists(failure)) {
                var mode=File.ReadAllText(failure);
                SharedLoaderConfiguration.FixtureBoundary=point=>{
                    if(point==(mode=="before-write"?"before-enable-write":"after-enable-write"))throw new IOException("inert loader write failure");
                    if(mode.StartsWith("failed-rollback",StringComparison.Ordinal)&&point=="before-enable-rollback" || mode=="unverified-rollback"&&point=="after-enable-rollback")throw new UnauthorizedAccessException("inert loader rollback unavailable");
                    if(mode=="failed-rollback-cleanup"&&point=="after-enable-attempt")throw new IOException("inert failed-stream cleanup unavailable");
                };
            }
#endif
            string details; int code;
            try { code = SteamLauncher.Run(root, out details); }
            finally {
#if BAP_LOADER_FIXTURE
                SharedLoaderConfiguration.FixtureBoundary=null;
#endif
            }
            File.WriteAllText(Path.Combine(root, "host-result"), code + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(details)) + "\n" + (before == Snapshot() ? "ParentUnchanged" : "ParentChanged") + "\n");
            return code;
        } catch (Exception error) { File.WriteAllText(Path.Combine(root, "fixture-error"), error.ToString()); return 92; }
    }
}

