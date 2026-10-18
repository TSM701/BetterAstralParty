using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

// Protocol-5 signed whole-package channel transitions through the production InstallProof
// receipt/high-water derivation. NativeInstall itself requires the executing package Setup host,
// so this fixture copies the signed owned files exactly as the installer would.
internal static class WholeChannelTransitionFixture
{
    private static int _checks;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); _checks++; Console.WriteLine("PASS: " + name); }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ApplySafetyException or UpdateValidationException)
        { Check(true, name + "; rejected=" + error.GetType().Name); return; }
        throw new Exception("Accepted: " + name);
    }
    private static byte[] Read(WindowsFileFence fence, string path, long limit = UpdateTrust.MaxFileBytes)
    { using var file = fence.OpenLaunchImage(path); return UpdateTicket.Read(file, limit); }
    private static void Write(WindowsFileFence fence, string path, byte[] bytes)
    {
        var parent = path.LastIndexOf('/'); if (parent >= 0) fence.EnsureDirectory(path.Substring(0, parent));
        using var file = fence.OpenFile(path, true, !File.Exists(fence.Full(path)));
        file.Stream.Position = 0; file.Stream.SetLength(0); file.Stream.Write(bytes); file.Flush();
    }
    private sealed class Package : IDisposable
    {
        internal readonly WindowsFileFence Fence;
        internal readonly VerifiedUpdateDescriptor Descriptor;
        internal readonly byte[] DescriptorBytes;
        internal readonly DetachedSignature Signature;
        internal string Root => Fence.Root;
        internal string Version => Descriptor.Version;
        internal Package(string path)
        {
            Fence = new WindowsFileFence(path);
            try {
                DescriptorBytes = Read(Fence, "update-proof/update.descriptor", UpdateTrust.MaxDescriptorBytes);
                var signatureBytes = Read(Fence, "update-proof/update.signature", 1024);
                Signature = DetachedSignature.Parse(signatureBytes);
                Descriptor = InstallProof.VerifyUpdateProof(DescriptorBytes, signatureBytes);
            } catch { Fence.Dispose(); throw; }
        }
        public void Dispose() => Fence.Dispose();
    }
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path.Substring(root.Length + 1).Replace('\\', '/'), path => UpdateTrust.Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
    private static void Same(Dictionary<string, string> before, string root, string label)
    { var after = Snapshot(root); Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value), label); }
    private static string NewRoot(string scratch, string name, Dictionary<string, byte[]> preserved)
    {
        var root = Path.Combine(scratch, name);
        if (Directory.Exists(root) || File.Exists(root)) throw new Exception("Fresh case required");
        Directory.CreateDirectory(root);
        using (var fence = new WindowsFileFence(root)) foreach (var item in preserved) Write(fence, item.Key, item.Value);
        return root;
    }
    private static void Install(string root, Package package)
    {
        var receipt = Encoding.ASCII.GetBytes(InstallProof.PrepareReceipt(root, package.Root, package.Version, "false"));
        var water = InstallProof.HighWater(root, package.Root, package.Version);
        using var target = new WindowsFileFence(root);
        foreach (var file in package.Descriptor.Files) {
            var bytes = Read(package.Fence, InstallProof.PackagePath(file.Path));
            Check(bytes.LongLength == file.Bytes && UpdateTrust.Hash(bytes) == file.Sha256, "scratch copy matches signed " + file.Path);
            Write(target, file.Path, bytes);
        }
        Write(target, InstallReceipt.Path, receipt);
        if (water != null) Write(target, UpdateTransaction.HighWaterPath, water);
    }
    private static Dictionary<string, byte[]> Preserved() => new() {
        ["AstralParty_INT.exe"] = Encoding.ASCII.GetBytes("inert fixture; never executed\n"),
        ["doorstop_config.ini"] = Encoding.UTF8.GetBytes("# keep all bytes\r\n[General]\r\nenabled=false\r\ncustom=keep\r\n"),
        ["BepInEx/config/kr.betterastralparty.mod.cfg"] = Encoding.UTF8.GetBytes("[Updates]\nUpdateChannel = Stable\n사용자 설정\n"),
        ["BepInEx/plugins/OtherMod.dll"] = Encoding.ASCII.GetBytes("inert unrelated mod\n")
    };
    private static void Transition(string scratch, Package stable, Package beta)
    {
        var preserved = Preserved(); var root = NewRoot(scratch, "stable-to-beta", preserved);
        Install(root, stable);
        using (var target = new WindowsFileFence(root)) {
            var old = InstallReceipt.Parse(Read(target, InstallReceipt.Path, 16384));
            Check(old.Channels?.ActiveChannel == "Stable" && old.Channels.ActiveVersion == stable.Version && old.Channels.BetaVersion == null && old.Channels.BetaDescriptor == "-" && old.LoaderBefore == false,
                "fresh Stable whole install binds Stable channel with unvisited Beta history");
            Check(UpdateTransaction.ReadHighWater(target) == stable.Version, "fresh install records the signed Stable high-water");
            SetupHostFree.ValidateHistory(target);
        }
        Install(root, beta);
        using (var target = new WindowsFileFence(root)) {
            var receipt = InstallReceipt.Parse(Read(target, InstallReceipt.Path, 16384));
            Check(receipt.LoaderBefore == false && receipt.Channels?.ActiveChannel == "Beta" && receipt.Channels.ActiveVersion == beta.Version
                && receipt.Channels.BetaRepositoryId == ReleaseFeedPolicy.BetaRepositoryId && receipt.Channels.BetaDescriptor == beta.Descriptor.DescriptorSha256
                && receipt.Channels.StableVersion == stable.Version && receipt.Channels.StableDescriptor == stable.Descriptor.DescriptorSha256,
                "Beta transition receipt preserves Stable history and loader baseline");
            foreach (var item in beta.Descriptor.Files) Check(receipt.Files[item.Path] == item.Sha256 && UpdateTrust.Hash(Read(target, item.Path)) == item.Sha256, "exact active owned hash " + item.Path);
            Check(UpdateTransaction.ReadHighWater(target) == beta.Version, "newer channel transition advances the high-water");
            SetupHostFree.ValidateHistory(target);
            foreach (var item in preserved) Check(Read(target, item.Key).SequenceEqual(item.Value), "shared/user bytes preserved " + item.Key);
            UpdateLaunchGate.Check(target);
        }
        var before = Snapshot(root);
        Reject(() => InstallProof.PrepareReceipt(root, stable.Root, stable.Version, "false"), "reverse older Stable whole install after Beta remains blocked");
        Same(before, root, "reverse refusal makes no file change");
        var again = Encoding.ASCII.GetBytes(InstallProof.PrepareReceipt(root, beta.Root, beta.Version, "false"));
        Check(InstallReceipt.Parse(again).Channels?.ActiveChannel == "Beta" && InstallProof.HighWater(root, beta.Root, beta.Version) == null,
            "same-channel same-version reinstall keeps the receipt channel and leaves the equal high-water unchanged");
        Same(before, root, "receipt/high-water preparation is read-only");
    }
    private static void Refusals(string scratch, Package stable, Package beta)
    {
        var root = NewRoot(scratch, "refusals", Preserved()); Install(root, stable);
        Reject(() => InstallProof.ValidateInputs(root, beta.Root, stable.Version), "package proof must match the requested version");
        Reject(() => InstallProof.ValidateInputs(Path.Combine(beta.Root, "nested-game"), beta.Root, beta.Version), "package inside the game root is rejected");
        using (var target = new WindowsFileFence(root)) {
            var original = Read(target, InstallReceipt.Path, 16384);
            var channels = InstallReceipt.Parse(original).Channels!.Text();
            var forged = channels.Replace("root=" + target.RootIdentity.Text + "\n", "root=00000000:0000000000000000\n", StringComparison.Ordinal);
            var changed = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(original).Replace(Convert.ToBase64String(Encoding.ASCII.GetBytes(channels)), Convert.ToBase64String(Encoding.ASCII.GetBytes(forged)), StringComparison.Ordinal));
            Check(InstallReceipt.Parse(changed).Channels!.Root != target.RootIdentity.Text, "structurally valid wrong-root channel prepared");
            Write(target, InstallReceipt.Path, changed);
            var before = Snapshot(root);
            Reject(() => InstallProof.PrepareReceipt(root, beta.Root, beta.Version, "false"), "wrong-root channel history cannot transition");
            Same(before, root, "wrong-root refusal makes no file change");
            Write(target, InstallReceipt.Path, original);
            var plugin = Read(target, UpdateTrust.PluginPath); var hidden = "fixture-hidden-" + Guid.NewGuid().ToString("N");
            using (var file = target.OpenFile(UpdateTrust.PluginPath, true)) file.RenameTo(target, hidden);
            Reject(() => InstallProof.PrepareReceipt(root, beta.Root, beta.Version, "false"), "receipt without its installed plugin cannot transition");
            using (var file = target.OpenFile(hidden, true)) file.RenameTo(target, UpdateTrust.PluginPath);
            Check(Read(target, UpdateTrust.PluginPath).SequenceEqual(plugin), "hidden plugin restored by handle");
        }
        var fresh = NewRoot(scratch, "fresh-loader-token", Preserved());
        Reject(() => InstallProof.PrepareReceipt(fresh, stable.Root, stable.Version, "maybe"), "fresh install rejects an invalid loader baseline token");
    }
    private static void LegacyTakeover(string scratch, Package stable)
    {
        var root = NewRoot(scratch, "legacy-1.3", Preserved());
        var files = LegacyReceipt.Owned.Select(path => new Dictionary<string, string> { ["path"] = path, ["sha256"] = new string('B', 64) }).ToArray();
        var legacy = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?> { ["schema"] = 1, ["baseline"] = LegacyReceipt.Baseline, ["owner"] = InstalledPluginMetadata.Owner,
            ["loaderBefore"] = true, ["files"] = files, ["channelState"] = "legacy-protocol4-state" });
        using (var target = new WindowsFileFence(root)) Write(target, InstallReceipt.Path, legacy);
        Check(LegacyReceipt.Is(legacy) && LegacyReceipt.Parse(legacy).Files.ContainsKey(OrdinaryInstallRecovery.LegacyLauncher), "synthetic 1.3.x receipt recognized with its five owned files");
        var receipt = InstallReceipt.Parse(Encoding.ASCII.GetBytes(InstallProof.PrepareReceipt(root, stable.Root, stable.Version, "true")));
        Check(receipt.Channels?.ActiveVersion == stable.Version && receipt.Channels.ActiveChannel == "Stable" && receipt.Files.Count == InstallReceipt.Owned.Length
            && !receipt.Files.ContainsKey(OrdinaryInstallRecovery.LegacyLauncher), "1.3.x takeover starts a fresh protocol-5 receipt without the removed launcher");
    }
    internal static void Main(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("fresh-scratch artifact-root Stable-package Beta-package");
        var scratch = Path.GetFullPath(args[0]); var artifacts = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
        if (!Path.GetDirectoryName(scratch)!.Equals(artifacts, StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(Path.GetFileName(scratch), @"\Awct-[0-9a-f]{32}\z") || Directory.Exists(scratch) || File.Exists(scratch))
            throw new Exception("Only a new direct artifact scratch may be changed");
        using var stable = new Package(args[2]); using var beta = new Package(args[3]);
        Check(stable.Descriptor.Channel == "Stable" && stable.Descriptor.Protocol == UpdateTrust.Protocol && UpdateVersion.Parse(stable.Version).CompareTo(UpdateVersion.Parse(ReleaseFeedPolicy.Baseline + "-0")) >= 0 && stable.Descriptor.RepositoryId == ReleaseFeedPolicy.StableRepositoryId,
            "exact supported Stable baseline signed proof");
        Check(beta.Descriptor.Channel == "Beta" && beta.Descriptor.RepositoryId == ReleaseFeedPolicy.BetaRepositoryId
            && UpdateVersion.Parse(beta.Version).CompareTo(UpdateVersion.Parse(stable.Version)) > 0, "verified target Beta strictly above supported Stable baseline");
        if (Encoding.ASCII.GetString(beta.DescriptorBytes).StartsWith(UpdateTrust.LocalDescriptorHeader + "\n", StringComparison.Ordinal)) {
            Check(beta.Descriptor.ReleaseId == 0 && beta.Descriptor.AssetId == 0 && UpdateVersion.Parse(beta.Version).IsLocalPrerelease, "local proof retains zero remote asset identities");
            var remote = new UpdateContext(beta.Descriptor.Repository, "Beta", "0.0.0", beta.Version, 1, 1, beta.Descriptor.Platform, repositoryId: beta.Descriptor.RepositoryId);
            Reject(() => UpdateTrust.Production().VerifyDescriptor(beta.DescriptorBytes, beta.Signature.KeyId, beta.Signature.Signature, remote), "local proof remains rejected by remote update verification");
        }
        var stableBefore = Snapshot(stable.Root); var betaBefore = Snapshot(beta.Root);
        Directory.CreateDirectory(scratch);
        try {
            Transition(scratch, stable, beta); Refusals(scratch, stable, beta); LegacyTakeover(scratch, stable);
            Same(stableBefore, stable.Root, "Stable package unchanged"); Same(betaBefore, beta.Root, "Beta package unchanged");
            File.WriteAllText(Path.Combine(scratch, "native-results.json"), JsonSerializer.Serialize(new {
                Passed = true, Checks = _checks, StableVersion = stable.Version, BetaVersion = beta.Version,
                StableDescriptor = stable.Descriptor.DescriptorSha256, BetaDescriptor = beta.Descriptor.DescriptorSha256,
                Retired = "Protocol-4 WholeInstallBridge migration boundaries, helper delivery, partial-removal validator, launcher resource scan and the unregistered signed automatic Apply mode.",
                Scope = "Production InstallProof receipt/high-water channel derivation in fresh scratch; no game/Steam execution, private key, real install/removal or network."
            }));
            Console.WriteLine("PASS: " + _checks + " signed whole/channel scratch assertions; evidence retained at " + scratch);
        } catch (Exception error) { File.WriteAllText(Path.Combine(scratch, "failure.txt"), error.ToString()); throw; }
    }
}
// Read-only history validation the installer runs (SetupHost.ValidateInstalledHistory) without linking the host.
internal static class SetupHostFree
{
    internal static void ValidateHistory(WindowsFileFence fence)
    {
        using var file = fence.OpenFile(InstallReceipt.Path, false);
        using var history = UpdateTransaction.ValidateHistory(fence, InstallReceipt.Parse(UpdateTicket.Read(file, 16384)));
    }
}
