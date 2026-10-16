using System.Text;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

internal static class WholeChannelTransitionFixture
{
    private static string[] WorkDirectories(string root, string kind)
    { using var fence = new WindowsFileFence(root); var paths = new System.Collections.Generic.List<string>(); foreach (var relative in UpdateWorkPath.Directories(fence, kind)) paths.Add(fence.Full(relative)); return paths.ToArray(); }
    private static string WorkRelative(string root, string path) => path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');

    private sealed class Interruption : Exception { }
    private static int _checks;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); _checks++; Console.WriteLine("PASS: " + name); }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ApplySafetyException or UpdateValidationException)
        { Check(true, name); return; }
        throw new Exception("Accepted: " + name);
    }
    private static byte[] Read(WindowsFileFence fence, string path, long limit = UpdateTrust.MaxFileBytes)
    { using var file = fence.OpenLaunchImage(path); return UpdateTicket.Read(file, limit); }
    private static void Write(WindowsFileFence fence, string path, byte[] bytes, bool fresh = true)
    {
        using var file = fence.OpenFile(path, true, fresh);
        file.Stream.Position = 0; file.Stream.SetLength(0); file.Stream.Write(bytes); file.Flush();
    }
    private static string Source(string name) => name == UpdateTrust.PluginPath ? "artifacts/BetterAstralParty.dll"
        : name is "BetterAstralParty-Launcher.exe" or "BetterAstralParty.compatibility.json" ? "artifacts/" + name : "tools/" + name;
    private sealed class Package : IDisposable
    {
        internal readonly WindowsFileFence Fence;
        internal readonly VerifiedUpdateDescriptor Descriptor;
        internal readonly HelperDescriptor Helper;
        internal readonly byte[] DescriptorBytes;
        internal readonly DetachedSignature Signature;
        internal Package(string path)
        {
            Fence = new WindowsFileFence(path);
            try {
                DescriptorBytes = Read(Fence, "update-proof/update.descriptor", UpdateTrust.MaxDescriptorBytes);
                var signatureBytes = Read(Fence, "update-proof/update.signature", 1024);
                Signature = DetachedSignature.Parse(signatureBytes);
                Descriptor = WholeInstallBridge.VerifyUpdateProof(DescriptorBytes, signatureBytes);
                Helper = HelperDescriptor.Verify(UpdateTrust.Production(), Read(Fence, "helper-bootstrap/helper.descriptor", UpdateTrust.MaxDescriptorBytes),
                    DetachedSignature.Parse(Read(Fence, "helper-bootstrap/helper.signature", 1024)), Read(Fence, "helper-bootstrap/" + HelperDescriptor.Image), Descriptor.Version);
            } catch { Fence.Dispose(); throw; }
        }
        public void Dispose() => Fence.Dispose();
    }
    private static void CopyOwned(WindowsFileFence target, Package package, byte[] receipt)
    {
        foreach (var file in package.Descriptor.Files) {
            var bytes = Read(package.Fence, Source(file.Path));
            Check(bytes.LongLength == file.Bytes && UpdateTrust.Hash(bytes) == file.Sha256, "scratch copy matches signed " + file.Path);
            Write(target, file.Path, bytes, !File.Exists(target.Full(file.Path)));
        }
        Write(target, InstallReceipt.Path, receipt, !File.Exists(target.Full(InstallReceipt.Path)));
    }
    private static byte[] Prepared(string root, Package package, string observation) =>
        Encoding.ASCII.GetBytes(WholeInstallBridge.PrepareReceipt(root, package.Fence.Root, package.Descriptor.Version, observation, "false"));
    private static void InstallFresh(string root, Package package)
    {
        var observation = WholeInstallBridge.Validate(root, package.Fence.Root, package.Descriptor.Version);
        var receipt = Prepared(root, package, observation);
        using (WholeInstallBridge.BeginInstall(root, package.Fence.Root, package.Descriptor.Version, observation, Encoding.ASCII.GetString(receipt)))
        using (var target = new WindowsFileFence(root)) CopyOwned(target, package, receipt);
        WholeInstallBridge.Install(root, package.Fence.Root, package.Descriptor.Version, observation, UpdateTrust.Hash(receipt));
    }
    private static Dictionary<string, string> SeedCompatibility(WindowsFileFence target, Package beta, string gameRoot, string cacheRoot)
    {
        using var game = new WindowsFileFence(gameRoot);
        using var cache = new WindowsFileFence(cacheRoot);
        using var document = JsonDocument.Parse(Read(beta.Fence, Source("BetterAstralParty.compatibility.json"), 65536));
        var copied = new Dictionary<string, string>(StringComparer.Ordinal);
        var publicGameFiles = new HashSet<string>(StringComparer.Ordinal) {
            "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", "baselib.dll",
            "AstralParty_INT_Data/il2cpp_data/Metadata/global-metadata.dat", "AstralParty_INT_Data/ScriptingAssemblies.json",
            "AstralParty_INT_Data/RuntimeInitializeOnLoads.json", "AstralParty_INT_Data/StreamingAssets/aa/settings.json",
            "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle", "AstralParty_INT_Data/data.unity3d"
        };
        foreach (var entry in document.RootElement.GetProperty("files").EnumerateArray()) {
            var scope = entry.GetProperty("root").GetString();
            var path = entry.GetProperty("path").GetString() ?? throw new Exception("Missing public compatibility path");
            var hash = entry.GetProperty("sha256").GetString() ?? "";
            var publicLibrary = Regex.IsMatch(path, @"\A(?:BepInEx/core|AstralParty_INT_Data/Plugins)/(?:[^/\\:]+/)*[^/\\:]+\.dll\z", RegexOptions.IgnoreCase);
            if (scope != "game" && scope != "cache" || scope == "game" && !publicGameFiles.Contains(path) && !publicLibrary || UpdateTrust.OwnedPath(path) || !Regex.IsMatch(hash, @"\A[0-9A-Fa-f]{64}\z")
                || scope == "cache" && !Regex.IsMatch(path, @"\Acatalog[^/\\:]*\.(json|hash)\z", RegexOptions.IgnoreCase))
                throw new Exception("Only signed public compatibility entries may be copied");
            var destination = scope == "cache" ? "fixture-cache/" + path : path;
            using var source = (scope == "game" ? game : cache).OpenLaunchImage(path);
            if (source.Stream.Length > 512L * 1024 * 1024 || source.Hash() != hash.ToUpperInvariant())
                throw new Exception("Public compatibility input differs from signed baseline: " + scope + "/" + path);
            var parentEnd = destination.LastIndexOf('/');
            if (parentEnd >= 0) target.EnsureDirectory(destination.Substring(0, parentEnd));
            using var copy = target.OpenFile(destination, true, true);
            source.Stream.Position = 0; source.Stream.CopyTo(copy.Stream); copy.Flush(); source.Recheck();
            Check(copy.Hash() == hash.ToUpperInvariant(), "exact signed public compatibility scratch copy: " + scope + "/" + path);
            copied.Add(destination, hash.ToUpperInvariant());
        }
        return copied;
    }
    private static void AutomaticCase(string scratch, string name, Package stable, Package beta, byte[] zip, string gameRoot, string cacheRoot)
    {
        var root = Path.Combine(scratch, "automatic-" + name);
        if (Directory.Exists(root) || File.Exists(root)) throw new Exception("Fresh automatic case required");
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/plugins/BetterAstralParty"));
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/config"));
        using var target = new WindowsFileFence(root);
        var compatibility = SeedCompatibility(target, beta, gameRoot, cacheRoot);
        var preserved = new Dictionary<string, byte[]> {
            ["doorstop_config.ini"] = Encoding.UTF8.GetBytes("# keep shared bytes\r\n[General]\r\nenabled=true\r\ncustom=keep\r\n"),
            [ChannelTransitionIntent.SettingsPath] = Encoding.UTF8.GetBytes("[Updates]\nAutoDownload = false\nApplyAfterExit = false\nUpdateChannel = Beta\n사용자 설정\n"),
            ["BepInEx/plugins/OtherMod.dll"] = Encoding.ASCII.GetBytes("inert unrelated mod\n")
        };
        foreach (var item in preserved) Write(target, item.Key, item.Value);
        InstallFresh(root, stable);
        var oldReceipt = Read(target, InstallReceipt.Path, 16384);
        var old = InstallReceipt.Parse(oldReceipt);
        var oldChannels = old.Channels ?? throw new Exception("Stable channel history required");
        Check(oldChannels.ActiveChannel == "Stable" && oldChannels.BetaVersion == null && oldChannels.BetaDescriptor == "-",
            name + ": real supported Stable baseline receipt has unvisited Beta history");
        var water = Encoding.ASCII.GetBytes("BetterAstralParty.HighWater/v1\nroot=" + target.RootIdentity.Text + "\nversion=" + stable.Descriptor.Version
            + "\ndescriptor=" + stable.Descriptor.DescriptorSha256 + "\n");
        Write(target, UpdateTransaction.HighWaterPath, water);
        var selection = new UpdateContext(beta.Descriptor.Repository, "Beta", stable.Descriptor.Version, beta.Descriptor.Version,
            beta.Descriptor.ReleaseId, beta.Descriptor.AssetId, beta.Descriptor.Platform, repositoryId: beta.Descriptor.RepositoryId);
        var invalidSignature = (byte[])beta.Signature.Signature.Clone(); invalidSignature[0] ^= 1;
        Reject(() => UpdateTransaction.Apply(target, beta.DescriptorBytes, beta.Signature.KeyId, invalidSignature, zip, UpdateTrust.Production(), selection),
            name + ": invalid signature is rejected before owned mutation");
        Check(Read(target, InstallReceipt.Path, 16384).SequenceEqual(oldReceipt) && Read(target, UpdateTransaction.HighWaterPath, 2048).SequenceEqual(water),
            name + ": rejection preserves receipt and high-water bytes");
        foreach (var item in stable.Descriptor.Files) Check(UpdateTrust.Hash(Read(target, item.Path)) == item.Sha256, name + ": rejection retains old owned " + item.Path);
        if (name == "commit") Check(UpdateTransaction.Apply(target, beta.DescriptorBytes, beta.Signature.KeyId, beta.Signature.Signature, zip, UpdateTrust.Production(), selection)
            == TransactionResult.Committed, name + ": signed automatic Apply commits with absent Beta evidence");
        else {
            var hit = false; var boundary = name == "rollback" ? "before:commit-intent" : "after:commit-intent";
            try { UpdateTransaction.Apply(target, beta.DescriptorBytes, beta.Signature.KeyId, beta.Signature.Signature, zip, UpdateTrust.Production(), selection,
                label => { if (label == boundary) { hit = true; throw new Interruption(); } }); } catch (Interruption) { }
            Check(hit, name + ": exact automatic boundary interrupted");
        }
        var work = WorkRelative(root, WorkDirectories(root, "txn").Single());
        var committed = name != "rollback";
        for (var attempt = 0; attempt < 2; attempt++) {
            Check(UpdateTransaction.Recover(target, work, UpdateTrust.Production(), selection) == (committed ? TransactionResult.Committed : TransactionResult.RolledBack),
                name + ": automatic recovery direction and repeated recovery " + attempt);
            var expected = committed ? beta : stable;
            var receipt = InstallReceipt.Parse(Read(target, InstallReceipt.Path, 16384));
            var channels = receipt.Channels ?? throw new Exception("Recovered channel history required");
            Check(receipt.LoaderBefore == old.LoaderBefore && channels.StableVersion == oldChannels.StableVersion
                && channels.StableDescriptor == oldChannels.StableDescriptor && channels.ActiveChannel == expected.Descriptor.Channel,
                name + ": recovery retains original loader baseline and Stable floor/evidence");
            Check(committed ? channels.BetaVersion == beta.Descriptor.Version && channels.BetaDescriptor == beta.Descriptor.DescriptorSha256
                : Read(target, InstallReceipt.Path, 16384).SequenceEqual(oldReceipt), name + ": target Beta evidence or exact original receipt restored");
            foreach (var item in expected.Descriptor.Files) Check(receipt.Files[item.Path] == item.Sha256 && UpdateTrust.Hash(Read(target, item.Path)) == item.Sha256,
                name + ": automatic resolved native-owned exact hash " + item.Path);
            Check(committed ? UpdateTransaction.ReadHighWater(target, out _) == beta.Descriptor.Version : Read(target, UpdateTransaction.HighWaterPath, 2048).SequenceEqual(water),
                name + ": automatic global high-water follows commit or exact rollback");
            foreach (var item in preserved) Check(Read(target, item.Key).SequenceEqual(item.Value), name + ": automatic shared/user bytes preserved " + item.Key);
            foreach (var item in compatibility) { using var file = target.OpenLaunchImage(item.Key); Check(file.Hash() == item.Value, name + ": public compatibility copy unchanged " + item.Key); }
        }
    }
    private static void Case(string scratch, string name, Package stable, Package beta)
    {
        var root = Path.Combine(scratch, name.Replace(':', '-'));
        if (Directory.Exists(root) || File.Exists(root)) throw new Exception("Fresh case required");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/plugins/BetterAstralParty"));
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/config"));
        var preserved = new Dictionary<string, byte[]> {
            ["AstralParty_INT.exe"] = Encoding.ASCII.GetBytes("inert fixture; never executed\n"),
            ["doorstop_config.ini"] = Encoding.UTF8.GetBytes("# keep all bytes\r\n[General]\r\nenabled=false\r\ncustom=keep\r\n"),
            ["BepInEx/config/kr.betterastralparty.mod.cfg"] = Encoding.UTF8.GetBytes("[Updates]\nAutoDownload = false\nApplyAfterExit = false\nUpdateChannel = Stable\n사용자 설정\n"),
            ["BepInEx/plugins/OtherMod.dll"] = Encoding.ASCII.GetBytes("inert unrelated mod\n")
        };
        using (var fence = new WindowsFileFence(root)) foreach (var item in preserved) Write(fence, item.Key, item.Value);
        InstallFresh(root, stable);
        using var target = new WindowsFileFence(root);
        var oldReceipt = Read(target, InstallReceipt.Path, 16384);
        var old = InstallReceipt.Parse(oldReceipt);
        Check(old.Channels?.ActiveChannel == "Stable" && old.Channels.ActiveVersion == stable.Descriptor.Version, name + ": initial Stable signed whole committed");
        // Constructed watermark tests preservation only; it is not a fabricated committed update transaction.
        var water = Encoding.ASCII.GetBytes("BetterAstralParty.HighWater/v1\nroot=" + target.RootIdentity.Text + "\nversion=" + stable.Descriptor.Version
            + "\ndescriptor=" + stable.Descriptor.DescriptorSha256 + "\n");
        Write(target, UpdateTransaction.HighWaterPath, water);
        string waterIdentity;
        using (var file = target.OpenFile(UpdateTransaction.HighWaterPath)) waterIdentity = file.Identity.Text;
        var observation = WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version);
        var nextReceipt = Prepared(root, beta, observation);
        using (WholeInstallBridge.BeginInstall(root, beta.Fence.Root, beta.Descriptor.Version, observation, Encoding.ASCII.GetString(nextReceipt))) {
            if (name != "cancel-before-owned") CopyOwned(target, beta, nextReceipt);
        }
        if (name == "cancel-before-owned") {
            WholeInstallBridge.Recover(root, beta.Fence.Root, beta.Descriptor.Version);
            Check(Read(target, InstallReceipt.Path, 16384).SequenceEqual(oldReceipt), name + ": original receipt restored without changing owned files");
            foreach (var item in stable.Descriptor.Files) Check(UpdateTrust.Hash(Read(target, item.Path)) == item.Sha256, name + ": original owned hash retained");
        } else {
            if (name == "normal") WholeInstallBridge.Install(root, beta.Fence.Root, beta.Descriptor.Version, observation, UpdateTrust.Hash(nextReceipt));
            else {
                var hit = false;
                try { WholeInstallBridge.Install(root, beta.Fence.Root, beta.Descriptor.Version, observation, UpdateTrust.Hash(nextReceipt), label => {
                    if (label == name) { hit = true; throw new Interruption(); }
                }); } catch (Interruption) { }
                Check(hit, name + ": exact native migration boundary interrupted");
                Reject(() => UpdateLaunchGate.Check(target), name + ": pending whole state blocks ordinary launch");
                WholeInstallBridge.Recover(root, beta.Fence.Root, beta.Descriptor.Version);
            }
            var receipt = InstallReceipt.Parse(Read(target, InstallReceipt.Path, 16384));
            Check(receipt.LoaderBefore == false && receipt.Channels?.ActiveChannel == "Beta" && receipt.Channels.ActiveVersion == beta.Descriptor.Version
                && receipt.Channels.BetaRepositoryId == ReleaseFeedPolicy.BetaRepositoryId && receipt.Channels.BetaDescriptor == beta.Descriptor.DescriptorSha256
                && receipt.Channels.StableRepositoryId == old.Channels!.StableRepositoryId && receipt.Channels.StableVersion == old.Channels.StableVersion
                && receipt.Channels.StableDescriptor == old.Channels.StableDescriptor, name + ": Beta active receipt preserves Stable history and loader baseline");
            foreach (var item in beta.Descriptor.Files) Check(receipt.Files[item.Path] == item.Sha256 && UpdateTrust.Hash(Read(target, item.Path)) == item.Sha256,
                name + ": exact active native-owned hash " + item.Path);
            Check(HelperDelivery.Fingerprint(target, UpdateTrust.Production(), 4) == beta.Helper.Sha256, name + ": public-verified helper is exactly current");
            var removal = HelperDelivery.RemovedEvidence(target, UpdateTrust.Production(), Read(target, HelperDescriptor.Image), Read(target, HelperDelivery.Receipt, 1024));
            Check(removal.StartsWith("BetterAstralParty.RemovedHelperEvidence/v1\n", StringComparison.Ordinal), name + ": copied removal helper proof uses signed original object history");
            Reject(() => WholeInstallBridge.Validate(root, stable.Fence.Root, stable.Descriptor.Version), name + ": reverse older whole install remains blocked");
            Reject(() => WholeInstallBridge.PrepareReceipt(root, beta.Fence.Root, beta.Descriptor.Version, observation, "false"), name + ": stale source observation cannot prepare again");
            WholeInstallBridge.Recover(root, beta.Fence.Root, beta.Descriptor.Version);
            File.WriteAllText(Path.Combine(scratch, name.Replace(':', '-') + "-proof.json"), JsonSerializer.Serialize(new {
                Root = target.RootIdentity.Text, PluginVersion = beta.Descriptor.Version,
                Channel = receipt.Channels!.Text().Split('\n'), Water = Encoding.ASCII.GetString(water).Split('\n'),
                Receipt = Encoding.ASCII.GetString(Read(target, InstallReceipt.Path, 16384)), Files = receipt.Files, HelperRemovalProof = removal
            }));
        }
        Check(Read(target, UpdateTransaction.HighWaterPath, 2048).SequenceEqual(water), name + ": original global high-water bytes retained");
        using (var file = target.OpenFile(UpdateTransaction.HighWaterPath)) Check(file.Identity.Text == waterIdentity, name + ": original global high-water object retained");
        foreach (var item in preserved) Check(Read(target, item.Key).SequenceEqual(item.Value), name + ": shared/user bytes preserved " + item.Key);
        UpdateLaunchGate.Check(target);
        Check(!WholeInstallBridge.Pending(target), name + ": no active whole migration after completion/recovery");
    }
    private static void Remove(WindowsFileFence target, string path)
    { using var file = target.OpenFile(path, true); file.RemoveOrdinaryRecoveryTarget(path); }
    private static Dictionary<string, string> Snapshot(WindowsFileFence target) => Directory.GetFiles(target.Root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path.Substring(target.Root.Length + 1).Replace('\\', '/'), path => {
            using var file = target.OpenLaunchImage(path.Substring(target.Root.Length + 1).Replace('\\', '/'));
            return file.Hash();
        }, StringComparer.Ordinal);
    private static void PartialRemovalEvidence(string scratch, WindowsFileFence target, Package beta, string[] missing, Dictionary<string, byte[]> preserved)
    {
        foreach (var path in missing) Remove(target, path);
        WholeInstallBridge.ValidatePartialRemoval(target.Root);
        Check(true, "partial removal: current production validator accepts only the known absent non-plugin set");
        var receipt = Read(target, InstallReceipt.Path, 16384);
        var helperImage = Read(target, HelperDescriptor.Image);
        var helperReceipt = Read(target, HelperDelivery.Receipt, 1024);
        var helperProof = HelperDelivery.RemovedEvidence(target, UpdateTrust.Production(), helperImage, helperReceipt);
        var water = Read(target, UpdateTransaction.HighWaterPath, 2048);
        string waterIdentity;
        using (var file = target.OpenFile(UpdateTransaction.HighWaterPath)) waterIdentity = file.Identity.Text;
        var backupRoot = Path.Combine(scratch, "partial-removal-backups");
        var backupId = Guid.NewGuid().ToString("N");
        var backupDir = Path.Combine(backupRoot, backupId);
        Directory.CreateDirectory(backupDir);
        using var backup = new WindowsFileFence(backupDir);
        var paths = InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path, HelperDescriptor.Image, HelperDelivery.Receipt, ChannelTransitionIntent.SettingsPath }).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var entries = new List<object>();
        foreach (var path in paths) {
            var exists = File.Exists(target.Full(path));
            var leaf = entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin";
            string? hash = null;
            if (exists) {
                var bytes = Read(target, path); hash = UpdateTrust.Hash(bytes); Write(backup, leaf, bytes);
                Check(UpdateTrust.Hash(Read(backup, leaf)) == hash, "partial removal: exact retained backup " + path);
            } else Check(missing.Contains(path, StringComparer.Ordinal) && path != UpdateTrust.PluginPath, "partial removal: false row is only a known absent non-plugin file " + path);
            entries.Add(new { Target = target.Full(path), Existed = exists, Backup = backup.Full(leaf), Hash = hash });
        }
        var journal = JsonSerializer.SerializeToUtf8Bytes(new { GameRoot = target.Root, Entries = entries, BackupDir = backupDir });
        Write(backup, "completed.json", journal);
        foreach (var path in paths) if (File.Exists(target.Full(path))) Remove(target, path);
        foreach (var path in paths) Check(!File.Exists(target.Full(path)), "partial removal: owned target absent " + path);
        Check(HelperDelivery.RemovedEvidence(target, UpdateTrust.Production(), helperImage, helperReceipt) == helperProof,
            "partial removal: signed helper history still authenticates retained copies after removal");
        Check(Read(target, UpdateTransaction.HighWaterPath, 2048).SequenceEqual(water), "partial removal: original high-water bytes retained");
        using (var file = target.OpenFile(UpdateTransaction.HighWaterPath)) Check(file.Identity.Text == waterIdentity, "partial removal: original high-water identity retained");
        foreach (var item in preserved.Where(item => item.Key != ChannelTransitionIntent.SettingsPath))
            Check(Read(target, item.Key).SequenceEqual(item.Value), "partial removal: shared/unknown files preserved " + item.Key);
        var channels = InstallReceipt.Parse(receipt).Channels ?? throw new Exception("Retained removal channels required");
        File.WriteAllText(Path.Combine(scratch, "bounded-partial-removal.json"), JsonSerializer.Serialize(new {
            GameRoot = target.Root, Root = target.RootIdentity.Text, PluginVersion = beta.Descriptor.Version,
            Receipt = Encoding.ASCII.GetString(receipt), Channel = channels.Text().Split('\n'), Water = Encoding.ASCII.GetString(water).Split('\n'),
            WaterIdentity = waterIdentity, BackupRoot = backupRoot, BackupId = backupId, JournalSha256 = UpdateTrust.Hash(journal),
            Missing = missing, Preserved = preserved.Where(item => item.Key != ChannelTransitionIntent.SettingsPath).ToDictionary(item => item.Key, item => UpdateTrust.Hash(item.Value)),
            Scope = "Constructed scratch-only completed removal journal with native held-file backups/removal; actual native removal/reinstall predicates are checked separately."
        }));
    }
    private static void PartialRepairCase(string scratch, string name, Package stable, Package beta, string[] missing, bool upgrade)
    {
        var root = Path.Combine(scratch, name);
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/plugins/BetterAstralParty"));
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/config"));
        using var target = new WindowsFileFence(root);
        var preserved = new Dictionary<string, byte[]> {
            ["AstralParty_INT.exe"] = Encoding.ASCII.GetBytes("inert fixture; never executed\n"),
            ["doorstop_config.ini"] = Encoding.ASCII.GetBytes("[General]\r\nenabled=false\r\nother=keep\r\n"),
            [ChannelTransitionIntent.SettingsPath] = Encoding.UTF8.GetBytes("[Updates]\nAutoDownload = false\nApplyAfterExit = false\nUpdateChannel = Beta\n"),
            ["BepInEx/plugins/OtherMod.dll"] = Encoding.ASCII.GetBytes("inert unrelated mod\n"),
            ["unknown-user-file.keep"] = Encoding.ASCII.GetBytes("keep unknown file\n")
        };
        foreach (var item in preserved) Write(target, item.Key, item.Value);
        var initial = upgrade ? stable : beta;
        InstallFresh(root, initial);
        var water = Encoding.ASCII.GetBytes("BetterAstralParty.HighWater/v1\nroot=" + target.RootIdentity.Text + "\nversion=" + initial.Descriptor.Version
            + "\ndescriptor=" + initial.Descriptor.DescriptorSha256 + "\n");
        Write(target, UpdateTransaction.HighWaterPath, water);
        var helperBefore = HelperDelivery.Fingerprint(target, UpdateTrust.Production(), 4);
        foreach (var path in missing) Remove(target, path);
        var removalBefore = Snapshot(target);
        WholeInstallBridge.ValidatePartialRemoval(root);
        var removalAfter = Snapshot(target);
        Check(removalBefore.Count == removalAfter.Count && removalBefore.All(pair => removalAfter.TryGetValue(pair.Key, out var hash) && hash == pair.Value),
            name + ": read-only removal validation accepts known missing owned files without writes");
        var observation = WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version);
        var receipt = Prepared(root, beta, observation);
        using (WholeInstallBridge.BeginInstall(root, beta.Fence.Root, beta.Descriptor.Version, observation, Encoding.ASCII.GetString(receipt))) CopyOwned(target, beta, receipt);
        WholeInstallBridge.Install(root, beta.Fence.Root, beta.Descriptor.Version, observation, UpdateTrust.Hash(receipt));
        var installed = InstallReceipt.Parse(Read(target, InstallReceipt.Path, 16384));
        foreach (var file in beta.Descriptor.Files) Check(installed.Files[file.Path] == file.Sha256 && UpdateTrust.Hash(Read(target, file.Path)) == file.Sha256,
            name + ": exact repaired signed owned file " + file.Path);
        Check(InstalledPluginMetadata.Read(Read(target, UpdateTrust.PluginPath)).Version == beta.Descriptor.Version, name + ": actual plugin version preserved/replaced as requested");
        Check(HelperDelivery.Fingerprint(target, UpdateTrust.Production(), 4) == beta.Helper.Sha256 && (upgrade || helperBefore == beta.Helper.Sha256),
            name + ": exact signed helper remains valid");
        Check(Read(target, UpdateTransaction.HighWaterPath, 2048).SequenceEqual(water), name + ": same-install high-water history is not retired");
        foreach (var item in preserved) Check(Read(target, item.Key).SequenceEqual(item.Value), name + ": shared/user/unknown bytes preserved " + item.Key);
        if (upgrade) {
            Check(installed.Channels?.StableVersion == stable.Descriptor.Version && installed.Channels.ActiveChannel == "Beta", name + ": prior Stable channel floor retained");
            PartialRemovalEvidence(scratch, target, beta, missing, preserved);
        }
    }
    private static void PartialRefusals(string scratch, Package beta)
    {
        var root = Path.Combine(scratch, "partial-refusals");
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/plugins/BetterAstralParty"));
        using var target = new WindowsFileFence(root);
        Write(target, "AstralParty_INT.exe", Encoding.ASCII.GetBytes("inert fixture; never executed\n"));
        InstallFresh(root, beta);
        foreach (var kind in new[] { "changed-launcher", "missing-plugin", "missing-install-receipt", "missing-helper", "missing-helper-receipt", "missing-helper-pair", "changed-helper", "changed-helper-receipt", "forged-helper-receipt", "wrong-channel-root", "same-build-different-receipt-hash", "pending-reinstall", "pending-operation" }) {
            var paths = kind switch {
                "missing-plugin" => new[] { UpdateTrust.PluginPath },
                "missing-install-receipt" => new[] { InstallReceipt.Path },
                "missing-helper" or "changed-helper" => new[] { HelperDescriptor.Image },
                "missing-helper-receipt" or "changed-helper-receipt" or "forged-helper-receipt" => new[] { HelperDelivery.Receipt },
                "missing-helper-pair" => new[] { HelperDescriptor.Image, HelperDelivery.Receipt },
                "wrong-channel-root" => new[] { "BetterAstralParty-Launcher.exe", InstallReceipt.Path },
                "same-build-different-receipt-hash" => new[] { "BetterAstralParty-Launcher.exe", InstallReceipt.Path },
                "pending-reinstall" => new[] { "BetterAstralParty.reinstall-v4.pending" },
                "pending-operation" => new[] { "BetterAstralParty.update-v4.operation" },
                _ => new[] { "BetterAstralParty-Launcher.exe" }
            };
            var originals = paths.ToDictionary(path => path, path => File.Exists(target.Full(path)) ? Read(target, path) : null, StringComparer.Ordinal);
            var hidden = new Dictionary<string, string>(StringComparer.Ordinal);
            if (kind == "wrong-channel-root") {
                var channels = InstallReceipt.Parse(originals[InstallReceipt.Path]!).Channels!.Text();
                var forged = channels.Replace("root=" + target.RootIdentity.Text + "\n", "root=00000000:0000000000000000\n", StringComparison.Ordinal);
                var changed = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(originals[InstallReceipt.Path]!).Replace(Convert.ToBase64String(Encoding.ASCII.GetBytes(channels)), Convert.ToBase64String(Encoding.ASCII.GetBytes(forged)), StringComparison.Ordinal));
                Check(InstallReceipt.Parse(changed).Channels!.Root != target.RootIdentity.Text, kind + ": structurally valid wrong-root channel prepared");
                Remove(target, "BetterAstralParty-Launcher.exe"); Write(target, InstallReceipt.Path, changed, false);
            } else if (kind == "forged-helper-receipt") {
                var original = Encoding.ASCII.GetString(originals[HelperDelivery.Receipt]!);
                var changed = Encoding.ASCII.GetBytes(original.Replace("sha256=" + beta.Helper.Sha256 + "\n", "sha256=" + new string('0', 64) + "\n", StringComparison.Ordinal));
                Check(!changed.SequenceEqual(originals[HelperDelivery.Receipt]!), kind + ": structured receipt digest forged without changing its shape");
                Write(target, HelperDelivery.Receipt, changed, false);
            } else if (kind == "same-build-different-receipt-hash") {
                var oldHash = UpdateTrust.Hash(originals["BetterAstralParty-Launcher.exe"]!);
                Remove(target, "BetterAstralParty-Launcher.exe");
                var changed = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(originals[InstallReceipt.Path]!).Replace(oldHash, new string('0', 64), StringComparison.Ordinal));
                Check(InstallReceipt.Parse(changed).Files["BetterAstralParty-Launcher.exe"] == new string('0', 64), kind + ": exact different receipt payload prepared");
                Write(target, InstallReceipt.Path, changed, false);
            } else foreach (var path in paths) {
                if (kind.StartsWith("missing-", StringComparison.Ordinal)) {
                    var hiddenPath = "fixture-hidden-" + Guid.NewGuid().ToString("N");
                    using var file = target.OpenFile(path, true); file.RenameTo(target, hiddenPath); hidden.Add(path, hiddenPath);
                }
                else Write(target, path, Encoding.ASCII.GetBytes("inert changed/pending fixture\n"), originals[path] == null);
            }
            if (kind.Contains("helper", StringComparison.Ordinal)) Remove(target, "BetterAstralParty-Launcher.exe");
            var before = Snapshot(target);
            if (kind == "missing-plugin") {
                // The exact incoming signed package authenticates this unchanged receipt.
                // Removing the plugin alone must not erase its root/channel/version proof.
                var missingObservation = WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version);
                var receiptBytes = Read(target, InstallReceipt.Path, 16384);
                var missingReceipt = InstallReceipt.Parse(receiptBytes);
                Check(missingObservation.Contains("root=" + target.RootIdentity.Text + "\n", StringComparison.Ordinal)
                    && missingObservation.Contains("plugin=-\n", StringComparison.Ordinal)
                    && missingObservation.Contains("receipt=" + UpdateTrust.Hash(receiptBytes) + "\n", StringComparison.Ordinal)
                    && missingObservation.Contains("update=" + beta.Descriptor.DescriptorSha256 + "\n", StringComparison.Ordinal)
                    && missingObservation.Contains("helper=" + beta.Helper.DescriptorHash + "\n", StringComparison.Ordinal),
                    kind + ": read-only observation binds native root, missing plugin, unchanged receipt and exact incoming signed proof");
                Check(missingReceipt.Channels?.Root == target.RootIdentity.Text && missingReceipt.Channels.ActiveChannel == "Beta"
                    && missingReceipt.Channels.ActiveVersion == beta.Descriptor.Version && missingReceipt.Channels.ActiveRepositoryId == beta.Descriptor.RepositoryId
                    && missingReceipt.Files.All(pair => beta.Descriptor.Files.Any(file => file.Path == pair.Key && file.Sha256 == pair.Value)),
                    kind + ": authenticated receipt retains exact installed version/channel/owned hashes");
            } else Reject(() => WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version), kind + ": bounded repair rejects missing core evidence/changed present files/pending work");
            if(kind is "missing-install-receipt" or "changed-launcher" or "missing-helper-pair") {
                var expected=kind switch {
                    "missing-install-receipt" => "InstallReceiptMissing",
                    "changed-launcher" => "OwnedFileChanged",
                    _ => "HelperEvidenceMissing"
                };
                try { WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version); throw new Exception("Accepted missing/changed evidence"); }
                catch(ApplySafetyException error) { Check(error.Failure==ApplyFailure.RecoveryRequired && (string?)error.Data["BapRecoveryReason"]==expected, kind+": specific recovery blocker retained without changing refusal"); }
            }
            if (kind is "missing-plugin" or "missing-helper-pair") {
                WholeInstallBridge.ValidatePartialRemoval(root);
                Check(true, kind + ": retained installed ownership/history permits read-only partial removal");
            } else if (kind != "same-build-different-receipt-hash")
                Reject(() => WholeInstallBridge.ValidatePartialRemoval(root), kind + ": production partial-removal validator rejects unsafe evidence");
            var after = Snapshot(target);
            Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value), kind +
                (kind is "missing-plugin" or "missing-helper-pair" ? ": validation makes no owned/history mutation" : ": rejection makes no owned/history mutation"));
            foreach (var item in originals) {
                if (hidden.TryGetValue(item.Key, out var hiddenPath)) { using var file = target.OpenFile(hiddenPath, true); file.RenameTo(target, item.Key); }
                else if (item.Value == null) Remove(target, item.Key);
                else Write(target, item.Key, item.Value, !File.Exists(target.Full(item.Key)));
            }
            if (kind.Contains("helper", StringComparison.Ordinal)) Write(target, "BetterAstralParty-Launcher.exe", Read(beta.Fence, Source("BetterAstralParty-Launcher.exe")));
        }
        Remove(target, "BetterAstralParty-Launcher.exe");
        var observation = WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version);
        var receipt = Prepared(root, beta, observation);
        var unknown = Encoding.ASCII.GetBytes("unknown file appeared after observation; preserve\n");
        Write(target, "BetterAstralParty-Launcher.exe", unknown);
        Reject(() => { using var lease = WholeInstallBridge.BeginInstall(root, beta.Fence.Root, beta.Descriptor.Version, observation, Encoding.ASCII.GetString(receipt)); },
            "partial repair: late unknown present file is rejected before copying");
        Reject(() => WholeInstallBridge.ValidatePartialRemoval(root), "partial removal: late unknown present file is rejected");
        Check(Read(target, "BetterAstralParty-Launcher.exe").SequenceEqual(unknown), "partial repair: late unknown file remains intact");
    }
    private static void NoEmbeddedHelperCase(Package package)
    {
        using var image = new MemoryStream(Read(package.Fence, Source("BetterAstralParty-Launcher.exe")), false);
        using var pe = new System.Reflection.PortableExecutable.PEReader(image);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.ManifestResources) {
            var resource = metadata.GetManifestResource(handle);
            Check(!metadata.GetString(resource.Name).StartsWith("BetterAstralParty.HelperBootstrap.", StringComparison.Ordinal),
                "baseline launcher has no legacy helper resource");
            if (!resource.Implementation.IsNil) continue;
            var directory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
            var content = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent();
            var offset = checked((int)resource.Offset);
            Check(offset >= 0 && offset <= content.Length - 6, "embedded resource is bounded");
            Check(content[offset + 4] != 77 || content[offset + 5] != 90, "baseline launcher embeds no PE");
        }
        Check(true, "actual packaged launcher resource metadata checked without loading or executing it");
    }
    private static Dictionary<string, string> ArchiveSnapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path.Substring(root.Length + 1).Replace('\\', '/'), path => UpdateTrust.Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
    private static void SameSnapshot(Dictionary<string, string> before, string root, string label)
    {
        var after = ArchiveSnapshot(root);
        Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value), label);
    }
    private static void CurrentProbes(string root, bool expected, string label)
    {
        foreach (var kind in new[] { "LaunchGate", "MaintenanceLease", "PartialRemoval" }) {
            var before = ArchiveSnapshot(root); var accepted = false; ApplySafetyException? failure = null;
            try {
                if (kind == "LaunchGate") { using var target = new WindowsFileFence(root); UpdateLaunchGate.Check(target); }
                else if (kind == "MaintenanceLease") { using var lease = new UpdateLaunchGate.Lease(root); }
                else WholeInstallBridge.ValidatePartialRemoval(root);
                accepted = true;
            } catch (ApplySafetyException error) { failure = error; }
            Check(accepted == expected && (expected || failure?.Failure == ApplyFailure.RecoveryRequired), label + ": current " + kind + (expected ? " accepts" : " refuses pending"));
            SameSnapshot(before, root, label + ": current " + kind + " inspection preserves every file");
        }
    }
    private static string ArchiveRoot(string scratch, string name, Package beta)
    {
        var root = Path.Combine(scratch, "archive-" + name);
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/plugins/BetterAstralParty"));
        using (var fence = new WindowsFileFence(root)) {
            Write(fence, "AstralParty_INT.exe", Encoding.ASCII.GetBytes("inert archive fixture; never executed\n"));
            Write(fence, "BepInEx/plugins/OtherMod.dll", Encoding.ASCII.GetBytes("preserve unrelated mod\n"));
        }
        InstallFresh(root, beta);
        using (var fence = new WindowsFileFence(root)) if (!File.Exists(fence.Full("BetterAstralParty.update.lock"))) Write(fence, "BetterAstralParty.update.lock", Array.Empty<byte>());
        return root;
    }
    private static (string Observation, byte[] Receipt, string Work) CurrentIntent(string root, Package beta, bool copy)
    {
        var observation = WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version);
        Check(observation.Contains("mode=Current\n", StringComparison.Ordinal), "archive: actual Current observation");
        var receipt = Prepared(root, beta, observation);
        using (WholeInstallBridge.BeginInstall(root, beta.Fence.Root, beta.Descriptor.Version, observation, Encoding.ASCII.GetString(receipt)))
            if (copy) using (var target = new WindowsFileFence(root)) CopyOwned(target, beta, receipt);
        var work = WorkDirectories(root, "wholeintent").Select(path => WorkRelative(root, path))
            .Single(name => File.ReadAllText(Path.Combine(root, name!, "plan")).StartsWith("BetterAstralParty.WholeInstallIntent/v2\n", StringComparison.Ordinal))!;
        return (observation, receipt, work);
    }
    private static Dictionary<string, string> IntentFacts(string root, string work)
    {
        using var target = new WindowsFileFence(root);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["directory"] = target.DirectoryIdentity(work).Text };
        foreach (var file in Directory.GetFiles(Path.Combine(root, work))) {
            using var held = target.OpenLaunchImage(work + "/" + Path.GetFileName(file));
            facts[Path.GetFileName(file)] = held.Identity.Text + "\t" + held.Hash();
        }
        return facts;
    }
    private static string ArchivedIntent(string root, string work, Dictionary<string, string> before, string terminal)
    {
        var history = UpdateWorkPath.WholeHistory(work);
        Check(!Directory.Exists(Path.Combine(root, work)) && Directory.Exists(Path.Combine(root, history)), "archive: terminal directory leaves old scanning prefix");
        var after = IntentFacts(root, history);
        Check(before.All(pair => after.TryGetValue(pair.Key, out var fact) && fact == pair.Value), "archive: directory and all prior file IDs/hashes unchanged");
        Check(File.ReadAllText(Path.Combine(root, history, "plan")).Contains("work=" + work + "\n", StringComparison.Ordinal), "archive: plan retains exact logical original work name");
        Check(File.ReadAllText(Path.Combine(root, history, terminal)) == UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, history, "plan"))) + "\n", "archive: terminal hash binds unchanged plan");
        using (var target = new WindowsFileFence(root)) { UpdateLaunchGate.Check(target); Check(!WholeInstallBridge.Pending(target), "archive: current native gate validates terminal history"); }
        return history;
    }
    private static void TerminalArchiveCases(string scratch, Package beta)
    {
        foreach (var name in new[] { "normal", "recover-after-marker", "begin-after-marker", "cancelled" }) {
            var root = ArchiveRoot(scratch, name, beta);
            if (name == "cancelled") using (var target = new WindowsFileFence(root)) Remove(target, "BetterAstralParty-Launcher.exe");
            var current = CurrentIntent(root, beta, name != "cancelled");
            var facts = IntentFacts(root, current.Work);
            CurrentProbes(root, false, name + " pending v2");
            using (var target = new WindowsFileFence(root)) Reject(() => UpdateLaunchGate.Check(target), name + ": current gate refuses pending v2");
            if (name == "cancelled") WholeInstallBridge.Recover(root, beta.Fence.Root, beta.Descriptor.Version);
            else if (name == "normal") WholeInstallBridge.Install(root, beta.Fence.Root, beta.Descriptor.Version, current.Observation, UpdateTrust.Hash(current.Receipt));
            else {
                var interrupted = false;
                try { WholeInstallBridge.Install(root, beta.Fence.Root, beta.Descriptor.Version, current.Observation, UpdateTrust.Hash(current.Receipt), boundary => {
                    if (boundary == "before:whole-history-archive") { interrupted = true; throw new Interruption(); }
                }); } catch (Interruption) { }
                Check(interrupted && File.Exists(Path.Combine(root, current.Work, "committed")), name + ": interruption preserves completed v2 before archive");
                CurrentProbes(root, true, name + " valid terminal marker before archive");
                var before = ArchiveSnapshot(root);
                WholeInstallBridge.Validate(root, beta.Fence.Root, beta.Descriptor.Version);
                SameSnapshot(before, root, name + ": read-only validation does not archive terminal leftover");
                if (name == "recover-after-marker") WholeInstallBridge.Recover(root, beta.Fence.Root, beta.Descriptor.Version);
                else {
                    var next = CurrentIntent(root, beta, true);
                    Check(next.Work != current.Work, "archive: BeginInstall archives completed leftover before creating a new Current intent");
                    WholeInstallBridge.Install(root, beta.Fence.Root, beta.Descriptor.Version, next.Observation, UpdateTrust.Hash(next.Receipt));
                }
            }
            var history = ArchivedIntent(root, current.Work, facts, name == "cancelled" ? "cancelled" : "committed");
            CurrentProbes(root, true, name + " archived terminal");
            if (name == "normal") {
                var marker = Path.Combine(root, history, "committed"); var saved = File.ReadAllBytes(marker);
                File.WriteAllText(marker, new string('0', 64) + "\n");
                var before = ArchiveSnapshot(root);
                using (var target = new WindowsFileFence(root)) Reject(() => UpdateLaunchGate.Check(target), "archive: changed terminal history marker remains a current safety blocker");
                SameSnapshot(before, root, "archive: malformed terminal marker refusal writes nothing");
                File.WriteAllBytes(marker, saved);
                var hidden = marker + ".fixture-hidden"; File.Move(marker, hidden);
                before = ArchiveSnapshot(root);
                using (var target = new WindowsFileFence(root)) Reject(() => UpdateLaunchGate.Check(target), "archive: history without terminal marker is never treated as completed");
                SameSnapshot(before, root, "archive: missing terminal marker refusal writes nothing");
                File.Move(hidden, marker);
            }
        }
        var generations = ArchiveRoot(scratch, "same-version-generations", beta);
        var initial = File.ReadAllBytes(Path.Combine(generations, InstallReceipt.Path));
        var previous = CurrentIntent(generations, beta, true);
        WholeInstallBridge.Install(generations, beta.Fence.Root, beta.Descriptor.Version, previous.Observation, UpdateTrust.Hash(previous.Receipt));
        using (var target = new WindowsFileFence(generations)) foreach (var path in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path, HelperDescriptor.Image, HelperDelivery.Receipt })) Remove(target, path);
        InstallFresh(generations, beta);
        var nextReceipt = File.ReadAllBytes(Path.Combine(generations, InstallReceipt.Path));
        Check(!initial.SequenceEqual(nextReceipt), "archive: fresh same-signed-version generation has a distinct receipt nonce/hash");
        using (var target = new WindowsFileFence(generations)) Remove(target, UpdateTrust.PluginPath);
        var snapshot = ArchiveSnapshot(generations);
        WholeInstallBridge.ValidatePartialRemoval(generations);
        SameSnapshot(snapshot, generations, "archive: valid current generation proof is found past valid older different-receipt history without mutation");
        var cancelled = ArchiveRoot(scratch, "cancelled-without-committed-proof", beta);
        using (var target = new WindowsFileFence(cancelled)) Remove(target, "BetterAstralParty-Launcher.exe");
        var cancellation = CurrentIntent(cancelled, beta, false);
        var original = IntentFacts(cancelled, cancellation.Work);
        WholeInstallBridge.Recover(cancelled, beta.Fence.Root, beta.Descriptor.Version);
        var cancelledHistory = ArchivedIntent(cancelled, cancellation.Work, original, "cancelled");
        // Keep prior evidence byte-for-byte outside the history scanner; cancellation alone grants no ownership.
        using (var target = new WindowsFileFence(cancelled)) foreach (var directory in WorkDirectories(cancelled, "wholehist").Concat(WorkDirectories(cancelled, "wholeintent"))) {
            var leaf = WorkRelative(cancelled, directory);
            if (leaf == cancelledHistory) continue;
            Check(File.Exists(Path.Combine(directory, "committed")), "archive: only committed prior proof is retained outside the scanner");
            target.ArchiveDirectory(leaf, UpdateWorkPath.Root + "/fixture-retained-proof-" + Guid.NewGuid().ToString("N"), target.DirectoryIdentity(leaf).Text);
        }
        Check(WorkDirectories(cancelled, "wholeintent").Length == 0 && WorkDirectories(cancelled, "wholehist").SequenceEqual(new[] { WindowsPath.Absolute(Path.Combine(cancelled, cancelledHistory.Replace('/', '\\'))) }), "archive: cancelled-only ownership refusal has no surviving committed proof");
        using (var target = new WindowsFileFence(cancelled)) Remove(target, UpdateTrust.PluginPath);
        var cancelledSnapshot = ArchiveSnapshot(cancelled);
        Reject(() => WholeInstallBridge.ValidatePartialRemoval(cancelled), "archive: cancelled target proof alone never authorizes missing-core ownership");
        SameSnapshot(cancelledSnapshot, cancelled, "archive: cancelled target proof refusal preserves all evidence");
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code, byte[] input, uint size, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    private static void ArchiveJunction(string path, string target)
    {
        Directory.CreateDirectory(path);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target); var print = Encoding.Unicode.GetBytes(target);
        var data = new byte[16 + substitute.Length + print.Length + 4];
        BitConverter.GetBytes(0xA0000003U).CopyTo(data, 0); BitConverter.GetBytes(checked((ushort)(data.Length - 8))).CopyTo(data, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(data, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(data, 12); BitConverter.GetBytes(checked((ushort)print.Length)).CopyTo(data, 14);
        substitute.CopyTo(data, 16); print.CopyTo(data, 18 + substitute.Length);
        using var handle = CreateFileW(path, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, data, (uint)data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
    private static void DirectoryArchiveRefusals(string scratch)
    {
        foreach (var name in new[] { "collision", "wrong-id", "id-race", "reparse-race", "held-child" }) {
            var root = Path.Combine(scratch, "archive-directory-" + name); Directory.CreateDirectory(root);
            var work = UpdateWorkPath.New("wholeintent"); var history = UpdateWorkPath.WholeHistory(work);
            string identity;
            using (var target = new WindowsFileFence(root)) {
                identity = UpdateWorkPath.Create(target, work).Text; Write(target, work + "/plan", Encoding.ASCII.GetBytes("keep exact source fixture\n"));
                if (name == "collision") { UpdateWorkPath.Create(target, history); Write(target, history + "/unknown.keep", Encoding.ASCII.GetBytes("do not overwrite\n")); }
            }
            var source = Path.Combine(root, work); var moved = Path.Combine(root, "fixture-saved-source"); var other = Path.Combine(root, "fixture-other");
            Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "unknown.keep"), "preserve reparse target\n");
            var hit = false;
            using (var target = new WindowsFileFence(root, boundary => {
                if (boundary != "archive-open" || name is not ("id-race" or "reparse-race")) return;
                hit = true; Directory.Move(source, moved);
                if (name == "reparse-race") ArchiveJunction(source, other);
                else using (var peer = new WindowsFileFence(root)) { UpdateWorkPath.Create(peer, work); Write(peer, work + "/unknown.keep", Encoding.ASCII.GetBytes("preserve replacement\n")); }
            })) {
                using var child = name == "held-child" ? target.OpenLaunchImage(work + "/plan") : null;
                Reject(() => target.ArchiveDirectory(work, history, name == "wrong-id" ? "00000000:0000000000000000" : identity), "archive directory: " + name + " refuses unsafe rename");
            }
            if (name is "id-race" or "reparse-race") {
                Check(hit && File.ReadAllText(Path.Combine(moved, "plan")) == "keep exact source fixture\n", "archive directory: exact close/reopen race preserves original object " + name);
                if (name == "reparse-race") { Check((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0, "archive directory: actual NTFS reparse substitution tested"); Directory.Delete(source); }
            } else Check(File.ReadAllText(Path.Combine(source, "plan")) == "keep exact source fixture\n", "archive directory: rejected source preserved " + name);
            Check(File.ReadAllText(Path.Combine(other, "unknown.keep")) == "preserve reparse target\n", "archive directory: unrelated bytes preserved " + name);
            if (name == "collision") Check(File.ReadAllText(Path.Combine(root, history, "unknown.keep")) == "do not overwrite\n", "archive directory: no-overwrite collision preserves destination");
            else Check(!Directory.Exists(Path.Combine(root, history)), "archive directory: refusal publishes no history " + name);
        }
    }
    internal static void Main(string[] args)
    {
        var archiveOnly = args.Length == 5 && args[4] == "archive-only";
        if (args.Length != 4 && args.Length != 7 && !archiveOnly) throw new ArgumentException("fresh-scratch artifact-root Stable-package Beta-package [signed-update-ZIP public-game-root public-cache-root | archive-only]");
        var scratch = Path.GetFullPath(args[0]); var artifacts = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
        if (!Path.GetDirectoryName(scratch)!.Equals(artifacts, StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(Path.GetFileName(scratch), @"\Awct-[0-9a-f]{32}\z") || Directory.Exists(scratch) || File.Exists(scratch))
            throw new Exception("Only a new direct artifact scratch may be changed");
        using var stable = new Package(args[2]); using var beta = new Package(args[3]);
        Check(stable.Descriptor.Channel == "Stable" && stable.Descriptor.Protocol == 4 && UpdateVersion.Parse(stable.Descriptor.Version).CompareTo(UpdateVersion.Parse("1.3.0-0")) >= 0 && stable.Descriptor.RepositoryId == ReleaseFeedPolicy.StableRepositoryId,
            "exact public supported Stable baseline signed baseline");
        Check(beta.Descriptor.Channel == "Beta" && beta.Descriptor.RepositoryId == ReleaseFeedPolicy.BetaRepositoryId
            && UpdateVersion.Parse(beta.Descriptor.Version).CompareTo(UpdateVersion.Parse(stable.Descriptor.Version)) > 0,
            "verified target Beta strictly above supported Stable baseline");
        if (Encoding.ASCII.GetString(beta.DescriptorBytes).StartsWith("BetterAstralParty.LocalInstallDescriptor/v1\n", StringComparison.Ordinal)) {
            Check(beta.Descriptor.ReleaseId == 0 && beta.Descriptor.AssetId == 0 && UpdateVersion.Parse(beta.Descriptor.Version).IsLocalPrerelease, "local proof retains zero remote asset identities");
            var remote = new UpdateContext(beta.Descriptor.Repository, "Beta", "0.0.0", beta.Descriptor.Version, 1, 1, beta.Descriptor.Platform, repositoryId: beta.Descriptor.RepositoryId);
            Reject(() => UpdateTrust.Production().VerifyDescriptor(beta.DescriptorBytes, beta.Signature.KeyId, beta.Signature.Signature, remote), "local proof remains rejected by remote update verification");
        }
        byte[]? zip = null;
        if (args.Length == 7) {
            using var zipFile = File.Open(args[4], FileMode.Open, FileAccess.Read, FileShare.Read);
            if (zipFile.Length != beta.Descriptor.ZipBytes || zipFile.Length > UpdateTrust.MaxZipBytes) throw new Exception("Exact signed update ZIP required");
            zip = new byte[checked((int)zipFile.Length)]; zipFile.ReadExactly(zip);
            var context = new UpdateContext(beta.Descriptor.Repository, "Beta", "0.0.0", beta.Descriptor.Version, beta.Descriptor.ReleaseId,
                beta.Descriptor.AssetId, beta.Descriptor.Platform, repositoryId: beta.Descriptor.RepositoryId);
            var remote = UpdateTrust.Production().VerifyDescriptor(beta.DescriptorBytes, beta.Signature.KeyId, beta.Signature.Signature, context);
            UpdatePackage.VerifyPayload(zip, remote);
            Check(remote.DescriptorSha256 == beta.Descriptor.DescriptorSha256, "actual automatic update ZIP and remote release signature independently verified");
        }
        Directory.CreateDirectory(scratch);
        try {
            if (archiveOnly) {
                TerminalArchiveCases(scratch, beta);
                DirectoryArchiveRefusals(scratch);
                File.WriteAllText(Path.Combine(scratch, "native-results.json"), JsonSerializer.Serialize(new {
                    Passed = true, Checks = _checks, StableVersion = stable.Descriptor.Version, BetaVersion = beta.Descriptor.Version,
                    StableDescriptor = stable.Descriptor.DescriptorSha256, BetaDescriptor = beta.Descriptor.DescriptorSha256,
                    TerminalArchiveCases = 6, DirectoryArchiveRefusalCases = 5,
                    Retired = "Immutable 1.1.3/1.1.4 inspector parity and v1 frozen-seed compatibility; the fresh 1.3.0 baseline does not support those generations.",
                    Scope = "Current protocol4 signed native terminal history, current launch/maintenance/partial-removal guards and exact file/directory identity preservation. Ephemeral fixture trust may be injected at compile time; no game/helper Main, operational install/removal, Steam, production key or network."
                }));
                Console.WriteLine("PASS: " + _checks + " isolated terminal archive/native assertions; evidence retained at " + scratch); return;
            }
            foreach (var name in new[] { "normal", "cancel-before-owned", "bridge-prepared", "after:old-image", "after:receipt", "after:bridge-committed" })
                Case(scratch, name, stable, beta);
            PartialRepairCase(scratch, "repair-launcher", stable, beta, new[] { "BetterAstralParty-Launcher.exe" }, false);
            PartialRepairCase(scratch, "repair-compatibility", stable, beta, new[] { "BetterAstralParty.compatibility.json" }, false);
            PartialRepairCase(scratch, "repair-launch-script", stable, beta, new[] { "BetterAstralParty-Mod.cmd", "AstralParty-Vanilla.cmd" }, false);
            PartialRepairCase(scratch, "repair-all-nonplugin", stable, beta, InstallReceipt.Owned.Where(path => path != UpdateTrust.PluginPath).ToArray(), true);
            PartialRefusals(scratch, beta);
            NoEmbeddedHelperCase(stable); NoEmbeddedHelperCase(beta);
            if (zip != null) foreach (var name in new[] { "commit", "rollback", "forward" }) AutomaticCase(scratch, name, stable, beta, zip, args[5], args[6]);
            File.WriteAllText(Path.Combine(scratch, "native-results.json"), JsonSerializer.Serialize(new {
                Passed = true, Checks = _checks, StableVersion = stable.Descriptor.Version, BetaVersion = beta.Descriptor.Version,
                StableDescriptor = stable.Descriptor.DescriptorSha256, BetaDescriptor = beta.Descriptor.DescriptorSha256,
                AutomaticCases = zip == null ? 0 : 3,
                PartialRepairCases = 4, PartialRefusalCases = 14,
                NoEmbeddedHelperCases = 2,
                Scope = "Production public-signature/native whole bridge and optional signed automatic Apply/recovery in fresh scratch; signed public compatibility input copies only; no game/Steam execution, private key, real install/removal or network."
            }));
            Console.WriteLine("PASS: " + _checks + " signed whole/native scratch assertions; evidence retained at " + scratch);
        } catch (Exception error) { File.WriteAllText(Path.Combine(scratch, "failure.txt"), error.ToString()); throw; }
    }
}
