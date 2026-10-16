using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void LauncherRecoveryCore(DevContext context, string stage, Action<string> pin)
    {
        const string targetVersion = "1.3.1", keyId = "launcher-recovery-fixture";
        var inputs = Path.Combine(stage, "inputs"); Directory.CreateDirectory(inputs);
        using var key = RSA.Create(3072); var publicKey = key.ExportParameters(false);
        var pins = UpdateGenerated(stage, "FixturePublicKey.cs", "namespace BetterAstralParty.Updating { internal static class ProductionUpdateKeys { internal static TrustedUpdateKey[] Read() { return new[] { new TrustedUpdateKey(\"" + keyId
            + "\", new System.Security.Cryptography.RSAParameters { Modulus=System.Convert.FromBase64String(\"" + Convert.ToBase64String(publicKey.Modulus!) + "\"), Exponent=new byte[]{1,0,1} }) }; } } }");
        var boundary = UpdateGenerated(stage, "FixtureBoundary.cs", "internal static class FixtureBoundary { internal const string Root=" + Literal(Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar) + "; }");
        var collectorPath = Path.Combine(context.Root, "diagnostics/DiagnosticBundle.cs"); pin(collectorPath);
        var collector = UpdateGenerated(stage, "FixtureDiagnosticBundle.cs", "#nullable disable\n" + File.ReadAllText(collectorPath));
        var usings = UpdateGenerated(stage, "FixtureGlobalUsings.cs", "global using System;\nglobal using System.IO;\nglobal using System.Collections.Generic;\nglobal using System.Threading;\nglobal using System.Threading.Tasks;\n");
        string Product(string path) => Path.Combine(context.Root, path);
        var launcherIdentityRoot = Path.Combine(stage, "launcher-identity"); Directory.CreateDirectory(launcherIdentityRoot);
        var helperIdentityRoot = Path.Combine(stage, "helper-identity"); Directory.CreateDirectory(helperIdentityRoot);
        var sources = new[] { Product("tools/SteamLauncher.cs"), Product("tools/InstallHelperBootstrap.cs"), Product("tools/GameCompatibility.cs"), Product("src/ReleaseHttpClient.cs"), Product("src/WindowsReleaseCredentialStore.cs") }
            .Concat(Directory.EnumerateFiles(Product("tools"), "Native*.cs")).Concat(Directory.EnumerateFiles(Product("tools"), "LauncherRecovery*.cs"))
            .Concat(UpdateFixtureSources(context, false, false)).Concat(new[] { collector, usings, boundary, pins, Product("focus-tests/launcher-recovery/FixtureEntry.cs"), BuildCommands.Identity(context.Root, launcherIdentityRoot, "Launcher") });
        var launcher = UpdateFrameworkCompile(context, stage, "BetterAstralParty-Launcher.exe", sources, pin, "FixtureEntry");
        var helper = UpdateFrameworkCompile(context, inputs, "helper.image", UpdateFixtureSources(context, true, false).Concat(new[] { pins, boundary }), pin, "BetterAstralParty.Updating.UpdateHelperProgram", "NETFRAMEWORK,BAP_LAUNCHER_RECOVERY_FIXTURE",
            helperIdentity: BuildCommands.Identity(context.Root, helperIdentityRoot, "Helper"));
        var pluginBoundary = UpdateGenerated(inputs, "TargetVersion.cs", "internal static class FixtureBoundary { internal const string Version=\"" + targetVersion + "\"; }");
        var targetPlugin = UpdateFrameworkCompile(context, inputs, "target-plugin.dll", new[] { Product("focus-tests/native-maintenance-integration/FixturePlugin.cs"), pluginBoundary }, pin);
        var higherBoundary = UpdateGenerated(inputs, "HigherVersion.cs", "internal static class FixtureBoundary { internal const string Version=\"1.4.0\"; }");
        UpdateFrameworkCompile(context, inputs, "higher-plugin.dll", new[] { Product("focus-tests/native-maintenance-integration/FixturePlugin.cs"), higherBoundary }, pin);
        byte[] Text(string value) => new UTF8Encoding(false).GetBytes(value);
        byte[] Signature(byte[] bytes) => Text("BetterAstralParty.UpdateSignature/v1\nkey-id=" + keyId + "\nsignature=" + Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) + "\n");
        void Write(string relative, byte[] bytes) {
            var path = Path.Combine(inputs, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        var oldHelper = UpdateInertImage(1); Write("old-helper.image", oldHelper);
        var oldDescriptor = HelperDescriptor.Prepare("1.3.0", oldHelper, 4); Write("old-helper.descriptor", oldDescriptor); Write("old-helper.signature", Signature(oldDescriptor));
        var helperBytes = File.ReadAllBytes(helper); var helperDescriptor = HelperDescriptor.Prepare(targetVersion, helperBytes, 4);
        Write("helper.descriptor", helperDescriptor); Write("helper.signature", Signature(helperDescriptor));
        var game = new Dictionary<string, byte[]>(StringComparer.Ordinal) {
            ["AstralParty_INT.exe"] = UpdateInertImage(2), ["GameAssembly.dll"] = Text("inert game assembly"), ["UnityPlayer.dll"] = Text("inert engine"),
            ["winhttp.dll"] = Text("inert shared loader"), ["AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle"] = Text("inert bundled catalog"),
            ["AstralParty_INT_Data/data.unity3d"] = Encoding.ASCII.GetBytes("UnityFS\0" + new string('x', 32)), ["BepInEx/core/BepInEx.Core.dll"] = Text("inert loader core"),
            ["fixture-cache/catalog_fixture.json"] = Text("inert downloaded catalog"), ["fixture-cache/catalog_fixture.hash"] = Text("inert downloaded catalog hash")
        };
        byte[] Compatibility() => JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, files = game.Select(row => {
            var cache = row.Key.StartsWith("fixture-cache/", StringComparison.Ordinal);
            return new { root = cache ? "cache" : "game", path = cache ? row.Key["fixture-cache/".Length..] : row.Key, sha256 = UpdateTrust.Hash(row.Value), kind = "runtime" };
        }), cacheCatalogs = game.Keys.Where(path => path.StartsWith("fixture-cache/", StringComparison.Ordinal)).Select(path => path["fixture-cache/".Length..]).Order(StringComparer.Ordinal).ToArray() });
        foreach (var row in game) Write("game/" + row.Key, row.Value);
        var owned = new Dictionary<string, byte[]>(StringComparer.Ordinal) {
            ["AstralParty-Vanilla.cmd"] = Text("inert original route"), [UpdateTrust.PluginPath] = File.ReadAllBytes(targetPlugin),
            ["BetterAstralParty-Launcher.exe"] = File.ReadAllBytes(launcher), ["BetterAstralParty-Mod.cmd"] = Text("inert mod route"), ["BetterAstralParty.compatibility.json"] = Compatibility()
        };
        Check(owned.Keys.Order(StringComparer.Ordinal).SequenceEqual(InstallReceipt.Owned.Order(StringComparer.Ordinal)), "Exact supported native owned payload closure");
        void Package() {
            foreach (var row in owned) Write("owned/" + row.Key, row.Value);
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) foreach (var row in owned.OrderBy(row => row.Key, StringComparer.Ordinal)) {
                using var output = archive.CreateEntry(row.Key, CompressionLevel.Optimal).Open(); output.Write(row.Value);
            }
            var zip = stream.ToArray(); Write("payload.zip", zip);
            var descriptor = new StringBuilder("BetterAstralParty.UpdateDescriptor/v4\nrepository=" + ReleaseFeedPolicy.StableRepository + "\nversion=" + targetVersion + "\nchannel=Stable\nplatform=" + UpdateTrust.PlatformId
                + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-" + targetVersion + "-update.zip\nzip-bytes=" + zip.Length.ToString(CultureInfo.InvariantCulture) + "\nzip-sha256=" + UpdateTrust.Hash(zip)
                + "\nfile-count=" + owned.Count.ToString(CultureInfo.InvariantCulture) + "\npurpose=" + ReleaseFeedPolicy.PublicPurpose + "\nrepository-id=" + ReleaseFeedPolicy.StableRepositoryId.ToString(CultureInfo.InvariantCulture) + "\nsettings-schema=1\n");
            foreach (var row in owned.OrderBy(row => row.Key, StringComparer.Ordinal)) descriptor.Append("file=").Append(row.Key).Append('\t').Append(row.Value.Length.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(UpdateTrust.Hash(row.Value)).Append('\n');
            var bytes = Text(descriptor.ToString()); var publicSignature = Signature(bytes);
            Write("update.descriptor", bytes); Write("update.signature", publicSignature);
            var narrow = Text(descriptor.ToString().Replace("purpose=" + ReleaseFeedPolicy.PublicPurpose + "\n", "purpose=release/Stable\n", StringComparison.Ordinal));
            Write("narrow-update.descriptor", narrow); Write("narrow-update.signature", Signature(narrow));
            var beta = Text(descriptor.ToString().Replace("repository=" + ReleaseFeedPolicy.StableRepository + "\n", "repository=" + ReleaseFeedPolicy.BetaRepository + "\n", StringComparison.Ordinal)
                .Replace("channel=Stable\n", "channel=Beta\n", StringComparison.Ordinal).Replace("purpose=" + ReleaseFeedPolicy.PublicPurpose + "\n", "purpose=release/Beta\n", StringComparison.Ordinal)
                .Replace("repository-id=" + ReleaseFeedPolicy.StableRepositoryId.ToString(CultureInfo.InvariantCulture) + "\n", "repository-id=" + ReleaseFeedPolicy.BetaRepositoryId.ToString(CultureInfo.InvariantCulture) + "\n", StringComparison.Ordinal));
            Write("beta-update.descriptor", beta); Write("beta-update.signature", Signature(beta));
            Write("pre-update.descriptor", bytes); Write("pre-update.signature", publicSignature);
        }
        Package();
        var names = new[] { "BetterAstralParty-" + targetVersion + "-update.zip", ReleaseFeedPolicy.UpdateManifestAsset, ReleaseFeedPolicy.UpdateSignatureAsset, "helper.descriptor", "helper.signature", HelperDescriptor.Image };
        Write("releases.json", JsonSerializer.SerializeToUtf8Bytes(new[] { new { id = 123, draft = false, prerelease = false, tag_name = targetVersion, html_url = "https://github.com/" + ReleaseFeedPolicy.StableRepository + "/releases/tag/" + targetVersion, body = "Inert signed fixture patch", assets = names.Select((name, index) => new { id = 456 + index, name }) } }));
        var checks = new List<string>();
        void Run(string image, string root, string? mode = null) {
            Check(Path.GetFullPath(image).StartsWith(Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Only exact isolated inert images execute");
            Directory.CreateDirectory(root); var args = new List<string> { root, inputs }; if (mode != null) args.Add(mode);
            Ensure(context.Run(image, args, cwd: Path.GetDirectoryName(image), env: new Dictionary<string, string?> { ["LOCALAPPDATA"] = Path.Combine(root, "inert-profile"), ["APPDATA"] = Path.Combine(root, "inert-roaming") },
                timeoutMilliseconds: Path.GetFileName(root) == "cases" && mode == null ? 180000 : 60000, logPath: Path.Combine(stage, "process-logs", Path.GetFileName(root) + "-" + (mode ?? Path.GetFileName(image)) + ".log")), "Inert launcher recovery: " + Path.GetFileName(root));
            checks.AddRange(File.ReadAllLines(Path.Combine(root, "passed.txt")));
        }
        void Committed(string root, string name) {
            var relative = File.ReadAllText(Path.Combine(root, "queued-stage")); Check(UpdateWorkPath.IsStage(relative) && UpdateWorkPath.IsContained(relative, "stage"), "Exact contained queued stage path");
            var result = Path.Combine(root, relative, "helper.result");
            Check(SpinWait.SpinUntil(() => File.Exists(result), TimeSpan.FromSeconds(15)), "Inert helper commit completed: " + name);
            Check(File.ReadAllText(result).Contains("\nCommitted\n", StringComparison.Ordinal), "Committed terminal evidence: " + name);
            foreach (var row in owned) Check(context.Hash(Path.Combine(root, row.Key.Replace('/', Path.DirectorySeparatorChar))) == UpdateTrust.Hash(row.Value), "Committed signed owned hash: " + row.Key);
            var receipt = InstallReceipt.Parse(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path)));
            Check(receipt.Channels != null && receipt.Channels.ActiveChannel == "Stable" && receipt.Channels.StableVersion == targetVersion, "Committed exact target channel/version");
            using (var fence = new WindowsFileFence(root)) Check(UpdateWorkPath.Directories(fence, "txn").Count() == 1, "Exact completed transaction evidence");
            Check(File.ReadAllText(Path.Combine(root, "BepInEx/plugins/OtherMod/OtherMod.dll")) == "inert other mod" && File.ReadAllText(Path.Combine(root, "retained-backup.keep")) == "inert retained backup"
                && File.ReadAllText(Path.Combine(root, "doorstop_config.ini")) == "[General]\nenabled=true\nother=preserved\n", "Unrelated mod, shared loader and retained backup preserved");
            checks.Add(name + ": natural exit commits exact signed native owned payload and target channel; unrelated data preserved");
        }
        Run(launcher, Path.Combine(stage, "cases"));
        foreach (var mode in new[] { "precancel", "startfailure", "cleanupfailure", "queue", "faultqueue", "releasequeue", "prequeue", "commit" }) {
            var root = Path.Combine(stage, mode); Directory.CreateDirectory(root); var image = Path.Combine(root, "BetterAstralParty-Launcher.exe"); File.Copy(launcher, image); Run(image, root);
            if (mode == "commit") Committed(root, "launcher-only handoff");
        }
        var originalInputs = inputs;
        inputs = Path.Combine(stage, "metadata-inputs"); UpdateCopyFixtureTree(originalInputs, inputs);
        owned["BetterAstralParty.compatibility.json"] = RandomNumberGenerator.GetBytes(65537); Package();
        Run(launcher, Path.Combine(stage, "metadata-observation"), "compatibility-metadata");
        inputs = Path.Combine(stage, "game-inputs"); UpdateCopyFixtureTree(originalInputs, inputs);
        game["AstralParty_INT.exe"] = File.ReadAllBytes(launcher); Write("game/AstralParty_INT.exe", game["AstralParty_INT.exe"]); owned["BetterAstralParty.compatibility.json"] = Compatibility(); Package();
        var gameRoot = Path.Combine(stage, "gamequeue"); Directory.CreateDirectory(gameRoot); var setup = Path.Combine(gameRoot, "BetterAstralParty-Launcher.exe"); File.Copy(launcher, setup);
        Run(setup, gameRoot, "setupgame"); Run(Path.Combine(gameRoot, "AstralParty_INT.exe"), gameRoot); Committed(gameRoot, "existing nonzero-game handoff");
        context.WriteJsonCreateNew(Path.Combine(stage, "launcher-recovery-parity.json"), new {
            schema = 1, status = "Passed", checks, count = checks.Count, protocol = 4, ownedFiles = owned.Keys.Order(StringComparer.Ordinal).ToArray(),
            ephemeralTestKeyOnly = true, compiledTargetPluginMetadata = true, legacyBinaryVersionReplacementRemoved = true,
            productionPinsChanged = false, liveNetworkUsed = false, productionIdentityProbe = "Not run; independent network probe remains outside isolated validation", operationalKeyUsed = false, gameSteamOrUserInstallationUsed = false
        });
    }

    private static byte[] UpdateInertImage(byte marker)
    {
        var bytes = new byte[512]; bytes[0] = 77; bytes[1] = 90; BitConverter.GetBytes(128).CopyTo(bytes, 60); bytes[128] = 80; bytes[129] = 69; BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes, 132); bytes[^1] = marker; return bytes;
    }
    private static void UpdateCopyFixtureTree(string source, string destination)
    {
        Check(!Directory.Exists(destination) && !File.Exists(destination), "Fresh cloned fixture inputs required");
        var root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Check((File.GetAttributes(source) & FileAttributes.ReparsePoint) == 0, "Inert fixture source root is not a link");
        Directory.CreateDirectory(destination);
        var directories = new Stack<string>(); directories.Push(source); var paths = 0; long bytes = 0;
        while (directories.Count != 0) foreach (var file in Directory.EnumerateFileSystemEntries(directories.Pop())) {
            Check(++paths <= 128 && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase), "Bounded inert source tree has no links");
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            if (Directory.Exists(file)) { Directory.CreateDirectory(target); directories.Push(file); }
            else { bytes = checked(bytes + new FileInfo(file).Length); Check(bytes <= 32 * 1024 * 1024, "Inert fixture clone byte bound"); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
        }
    }
}
