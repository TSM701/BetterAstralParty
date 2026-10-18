using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static class NativeReinstallGenerationFixture
{
    private static int Checks;
    private static string Scratch = "", Local = "";
    private sealed class Interrupted : Exception { }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Checks++; Console.WriteLine("PASS: " + label); }
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (Exception error) when (error is ApplySafetyException or UpdateValidationException) { Check(true, label); return; }
        throw new Exception("Accepted: " + label);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Read(string path) => File.ReadAllBytes(WindowsPath.ForIo(path.Replace('/', '\\')));
    private static void Write(string path, byte[] bytes)
    { path = path.Replace('/', '\\'); Directory.CreateDirectory(WindowsPath.ForIo(Path.GetDirectoryName(path)!)); File.WriteAllBytes(WindowsPath.ForIo(path), bytes); }
    private static void Json(string path, object value) => Write(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(WindowsPath.ForIo(root), "*", SearchOption.AllDirectories)
        .ToDictionary(WindowsPath.FromIo, path => Hash(Read(WindowsPath.FromIo(path))), StringComparer.Ordinal);
    private static void Unchanged(Dictionary<string, string> before, string root, string label)
    { var after = Snapshot(root); Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value), label); }
    private sealed class Package
    {
        internal readonly string Root, ManifestHash, Version;
        internal readonly VerifiedUpdateDescriptor Descriptor;
        internal Package(string root, string hash)
        {
            Root = WindowsPath.Absolute(root); ManifestHash = hash;
            Check(Hash(Read(Path.Combine(Root, "package-manifest.json"))) == hash, "exact immutable package manifest");
            Descriptor = InstallProof.VerifyUpdateProof(Read(Path.Combine(Root, "update-proof/update.descriptor")), Read(Path.Combine(Root, "update-proof/update.signature")));
            Version = Descriptor.Version;
            Check(InstalledPluginMetadata.ReadMaintenance(Read(Path.Combine(Root, "artifacts/BetterAstralParty.dll"))).Version == Version, "real signed plugin maintenance metadata");
        }
    }
    // Mirrors the native installer's file outcome: receipt bytes come from the production InstallProof
    // (NativeInstall itself needs the executing package Setup host, so this fixture copies the signed files).
    private static void Install(string root, Package package)
    {
        var receipt = InstallProof.PrepareReceipt(root, package.Root, package.Version, "false");
        using (var target = new WindowsFileFence(root))
        {
            foreach (var item in package.Descriptor.Files)
            {
                var bytes = Read(Path.Combine(package.Root, InstallProof.PackagePath(item.Path)));
                Check(Hash(bytes) == item.Sha256, "signed fixture copy " + item.Path);
                var parent = item.Path.LastIndexOf('/'); if (parent >= 0) target.EnsureDirectory(item.Path.Substring(0, parent));
                using var file = target.OpenFile(item.Path, true, !File.Exists(WindowsPath.ForIo(target.Full(item.Path))));
                file.Stream.Position = 0; file.Stream.Write(bytes); file.Stream.SetLength(bytes.Length); file.Flush();
            }
            using var ownedReceipt = target.OpenFile(InstallReceipt.Path, true, !File.Exists(WindowsPath.ForIo(target.Full(InstallReceipt.Path))));
            var receiptBytes = Encoding.ASCII.GetBytes(receipt); ownedReceipt.Stream.Position = 0; ownedReceipt.Stream.Write(receiptBytes); ownedReceipt.Stream.SetLength(receiptBytes.Length); ownedReceipt.Flush();
        }
    }
    private sealed class Removal
    {
        internal readonly string Game, Backups, Id, Completed;
        internal readonly List<Dictionary<string, object?>> Rows = new();
        internal Removal(string name, Package stable, Package? upgrade = null)
        {
            Game = Path.Combine(Scratch, name); Directory.CreateDirectory(Game);
            Write(Path.Combine(Game, "AstralParty_INT.exe"), Encoding.ASCII.GetBytes("inert fixture, never executed"));
            Write(Path.Combine(Game, "unrelated.keep"), Encoding.ASCII.GetBytes("other mod/settings remain"));
            Install(Game, stable);
            using (var game = new WindowsFileFence(Game))
                Write(game.Full(UpdateTransaction.HighWaterPath), Encoding.ASCII.GetBytes("BetterAstralParty.HighWater/v1\nroot=" + game.RootIdentity.Text + "\nversion=" + stable.Version + "\ndescriptor=" + stable.Descriptor.DescriptorSha256 + "\n"));
            if (upgrade != null) Install(Game, upgrade);
            Backups = Path.Combine(Local, "BetterAstralParty", "Backups", "v5", Hash(Encoding.UTF8.GetBytes(Game.ToLowerInvariant())));
            Id = Guid.NewGuid().ToString("N"); Directory.CreateDirectory(Path.Combine(Backups, Id));
            var names = InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path }).ToArray();
            for (var i = 0; i < names.Length; i++)
            {
                var target = Path.Combine(Game, names[i].Replace('/', '\\')); var backup = Path.Combine(Backups, Id, i + ".bin"); var bytes = Read(target);
                Write(backup, bytes); Rows.Add(new Dictionary<string, object?> { ["Target"] = target, ["Backup"] = backup, ["Hash"] = Hash(bytes), ["Existed"] = true });
            }
            Write(Path.Combine(Backups, "pending.json.lock"), Array.Empty<byte>());
            var pending = Path.Combine(Backups, "pending.json"); Save(pending);
            var journalHash = Hash(Read(pending)); var targets = Rows.Select(row => (string)row["Target"]!).ToArray();
            using (var game = new WindowsFileFence(Game))
            using (var maintenance = new UpdateLaunchGate.Lease(Game))
            {
                using (var removal = new OrdinaryInstallRecovery.OwnedRemovalLease(Game, Backups, journalHash, targets, game.RootIdentity.Text, false))
                    foreach (var target in targets) removal.Remove(target);
                OrdinaryInstallRecovery.Run(Game, Backups, journalHash, targets, "", true, game.RootIdentity.Text);
            }
            Completed = Path.Combine(Backups, Id, "completed.json");
            Check(Hash(Read(Completed)) == journalHash, "real held-handle removal retains completed journal");
        }
        internal void Save(string? path = null) => Json(path ?? Completed, new { GameRoot = Game, BackupDir = Path.Combine(Backups, Id), Entries = Rows });
        internal NativeReinstallGeneration.Plan Review(Package package) => NativeReinstallGeneration.Review(Game, package.Root, package.Version, package.ManifestHash, Backups) ?? throw new Exception("Expected removal plan");
        internal void Retire(Package package, NativeReinstallGeneration.Plan plan)
        {
            using var backups = new WindowsFileFence(Backups);
            using var heldLock = backups.OpenFile("pending.json.lock", true, !File.Exists(Path.Combine(Backups, "pending.json.lock")));
            NativeReinstallGeneration.Retire(plan, Game, package.Root, package.Version, package.ManifestHash, Backups, heldLock);
        }
    }
    private static void Happy(Package stable, Package beta)
    {
        var removed = new Removal("active-beta-water-stable", stable, beta); var gameBefore = Snapshot(removed.Game); var backupsBefore = Snapshot(removed.Backups);
        var plan = removed.Review(beta);
        Check(plan.Version == stable.Version && InstallReceipt.Parse(Read((string)removed.Rows[InstallReceipt.Owned.Length]["Backup"]!)).Channels!.ActiveVersion == beta.Version,
            "active plugin and retained global high-water may differ");
        Unchanged(gameBefore, removed.Game, "review writes no game files or locks"); Unchanged(backupsBefore, removed.Backups, "review writes no backups or locks");
        removed.Retire(beta, plan);
        Check(!File.Exists(Path.Combine(removed.Game, UpdateTransaction.HighWaterPath)) && !File.Exists(Path.Combine(removed.Game, "BetterAstralParty.reinstall-v5.pending")), "successful retirement closes the generation");
        var archives = Directory.GetFiles(removed.Game, UpdateTransaction.HighWaterPath + ".uninstalled-*", SearchOption.TopDirectoryOnly);
        Check(archives.Length == 1 && Hash(Read(archives[0])) == plan.WaterHash, "exact previous high-water bytes archived");
        using (var game = new WindowsFileFence(removed.Game)) using (var archive = game.OpenFile(Path.GetFileName(archives[0])))
            Check(archive.Identity.Text == plan.WaterIdentity, "retirement preserves the original high-water object identity");
        Check(Directory.GetFiles(removed.Game, "BetterAstralParty.reinstall-v5.completed-*", SearchOption.TopDirectoryOnly).Length == 1, "durable completed generation record retained");
        Unchanged(backupsBefore, removed.Backups, "all removal backups remain after retirement");
        Check(Encoding.ASCII.GetString(Read(Path.Combine(removed.Game, "unrelated.keep"))) == "other mod/settings remain", "unrelated file retained");
    }
    private static void Refusals(Package stable, Package beta)
    {
        var removed = new Removal("backup-changed", stable); var plan = removed.Review(beta);
        Write((string)removed.Rows[0]["Backup"]!, Encoding.ASCII.GetBytes("changed fixture"));
        Reject(() => removed.Retire(beta, plan), "changed backup blocks retirement");
        Check(File.Exists(Path.Combine(removed.Game, UpdateTransaction.HighWaterPath)) && !File.Exists(Path.Combine(removed.Game, "BetterAstralParty.reinstall-v5.pending")), "changed evidence creates no intent");
        removed = new Removal("water-object-changed", stable); plan = removed.Review(beta);
        var waterPath = Path.Combine(removed.Game, UpdateTransaction.HighWaterPath); var bytes = Read(waterPath);
        File.Move(waterPath, waterPath + ".original-fixture"); Write(waterPath, bytes);
        Reject(() => removed.Retire(beta, plan), "same bytes in a replaced high-water object block retirement");
        removed = new Removal("latest-completed-invalid", stable);
        var latest = Path.Combine(removed.Backups, new string('f', 32), "completed.json"); Write(latest, Encoding.ASCII.GetBytes("{}")); File.SetLastWriteTimeUtc(latest, DateTime.UtcNow.AddDays(1));
        Reject(() => removed.Review(beta), "invalid latest completed backup cannot fall back to older evidence");
        removed = new Removal("duplicate-case-json", stable);
        var raw = Encoding.UTF8.GetString(Read(removed.Completed));
        Write(removed.Completed, Encoding.UTF8.GetBytes(raw.Insert(1, "\"gameroot\":\"duplicate\",")));
        Reject(() => removed.Review(beta), "case-insensitive duplicate journal keys rejected");
        removed = new Removal("pending-operation", stable);
        Write(Path.Combine(removed.Game, "BetterAstralParty.update-v5.failed"), Encoding.ASCII.GetBytes("inert pending fixture"));
        Reject(() => removed.Review(beta), "remaining failed operation marker blocks clean reinstall");
        removed = new Removal("owned-file-remains", stable); Write(Path.Combine(removed.Game, InstallReceipt.Owned[0]), Encoding.ASCII.GetBytes("remaining fixture"));
        Reject(() => removed.Review(beta), "partial installation cannot retire high-water");
        removed = new Removal("ordinary-pending-not-presence-guard", stable);
        Write(Path.Combine(removed.Backups, "pending.json"), Encoding.ASCII.GetBytes("unrelated ordinary journal fixture"));
        removed.Review(beta); Check(File.Exists(Path.Combine(removed.Backups, "pending.json")), "ordinary pending presence does not retire or normalize its journal");
        removed = new Removal("former-plugin-missing", stable);
        var plugin = removed.Rows.Single(row => ((string)row["Target"]!).EndsWith("BetterAstralParty.dll", StringComparison.Ordinal));
        File.Move((string)plugin["Backup"]!, (string)plugin["Backup"]! + ".fixture-original"); plugin["Existed"] = false; plugin["Hash"] = null; removed.Save();
        Reject(() => removed.Review(beta), "missing former plugin backup blocks retirement without a signed core-evidence fallback");
    }
    private static void Interruptions(Package stable, Package beta)
    {
        foreach (var boundary in new[] { "intent-durable", "water-archived" })
        {
            var removed = new Removal("interrupted-" + boundary, stable); var plan = removed.Review(beta); var before = Snapshot(removed.Backups); var hit = false;
            NativeReinstallGeneration.FixtureBoundary = (name, _) => { if (name == boundary) { hit = true; throw new Interrupted(); } };
            try { removed.Retire(beta, plan); throw new Exception("Expected interruption"); } catch (Interrupted) { }
            finally { NativeReinstallGeneration.FixtureBoundary = null; }
            Check(hit && File.Exists(Path.Combine(removed.Game, "BetterAstralParty.reinstall-v5.pending")), boundary + ": durable intent retained");
            var paths = Directory.GetFiles(removed.Game, UpdateTransaction.HighWaterPath + "*", SearchOption.TopDirectoryOnly);
            Check(paths.Length == 1 && Hash(Read(paths[0])) == plan.WaterHash, boundary + ": exact original history retained");
            Reject(() => removed.Review(beta), boundary + ": interruption blocks automatic retry");
            Unchanged(before, removed.Backups, boundary + ": removal backups unchanged");
        }
        var collision = new Removal("no-overwrite-archive", stable); var collisionPlan = collision.Review(beta); string? destination = null;
        NativeReinstallGeneration.FixtureBoundary = (name, archive) => {
            if (name != "intent-durable") return;
            destination = Path.Combine(collision.Game, archive); Write(destination, Encoding.ASCII.GetBytes("keep collision fixture"));
        };
        try { Reject(() => collision.Retire(beta, collisionPlan), "archive rename never overwrites a racing destination"); }
        finally { NativeReinstallGeneration.FixtureBoundary = null; }
        Check(destination != null && Encoding.ASCII.GetString(Read(destination)) == "keep collision fixture" && Hash(Read(Path.Combine(collision.Game, UpdateTransaction.HighWaterPath))) == collisionPlan.WaterHash,
            "rename collision preserves both objects and the pending intent");
        var guarded = new Removal("channel-row-history-pins", stable); var guardedPlan = guarded.Review(beta);
        Check(guardedPlan.GamePins.Count == 0 && guardedPlan.HelperProof.Length == 0 && guardedPlan.CoreProof.Length == 0, "channel-row history needs no game-side helper or core proof pins");
    }
    private static void HistoryPredicate(Package stable, Package beta)
    {
        var method = typeof(NativeReinstallGeneration).GetMethod("History", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var root = "ABCD1234:0000000000000123";
        var state = new ReceiptChannelState(root, "Beta", new string('a', 32), ReleaseFeedPolicy.StableRepositoryId, "1.4.1-rc.1.local.5", new string('B', 64),
            ReleaseFeedPolicy.BetaRepositoryId, beta.Version, beta.Descriptor.DescriptorSha256);
        Check((bool)method.Invoke(null, new object?[] { state, root, stable.Version, stable.Descriptor.DescriptorSha256, beta.Version, stable.Descriptor })!,
            "signed historical descriptor permits an advanced inactive channel row");
        state = new ReceiptChannelState(root, "Beta", new string('a', 32), ReleaseFeedPolicy.StableRepositoryId, stable.Version, new string('B', 64),
            ReleaseFeedPolicy.BetaRepositoryId, beta.Version, beta.Descriptor.DescriptorSha256);
        Check(!(bool)method.Invoke(null, new object?[] { state, root, stable.Version, stable.Descriptor.DescriptorSha256, beta.Version, stable.Descriptor })!,
            "same-version different-descriptor history cannot retire");
        using var key = RSA.Create(3072);
        var text = "BetterAstralParty.UpdateDescriptor/v5\nrepository=" + ReleaseFeedPolicy.StableRepository + "\nversion=1.4.1\nchannel=Stable\nplatform=" + UpdateTrust.PlatformId
            + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-1.4.1-update.zip\nzip-bytes=1\nzip-sha256=" + new string('A', 64)
            + "\nfile-count=" + InstallReceipt.Owned.Length + "\npurpose=" + ReleaseFeedPolicy.PublicPurpose + "\nrepository-id=" + ReleaseFeedPolicy.StableRepositoryId + "\nsettings-schema=1\n"
            + string.Concat(InstallReceipt.Owned.OrderBy(path => path, StringComparer.Ordinal).Select(path => "file=" + path + "\t1\t" + new string('B', 64) + "\n"));
        var trust = new UpdateTrust(new[] { new TrustedUpdateKey("public-history-fixture", key.ExportParameters(false)) });
        VerifiedUpdateDescriptor Verify(string value) {
            var bytes = Encoding.ASCII.GetBytes(value);
            return trust.VerifyProposal(bytes, "public-history-fixture", key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
                new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "0.0.0", "1.4.1", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId));
        }
        var publicProof = Verify(text);
        bool History(ReceiptChannelState channels, VerifiedUpdateDescriptor proof) => (bool)method.Invoke(null, new object?[] { channels, root, proof.Version, proof.DescriptorSha256, beta.Version, proof })!;
        void RefuseHistory(ReceiptChannelState channels, VerifiedUpdateDescriptor proof, string label) {
            try { History(channels, proof); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is ApplySafetyException) { Check(true, label); return; }
            throw new Exception("Accepted: " + label);
        }
        state = new ReceiptChannelState(root, "Beta", new string('a', 32), 0, null, "-", ReleaseFeedPolicy.BetaRepositoryId, beta.Version, beta.Descriptor.DescriptorSha256,
            ReleaseFeedPolicy.StableRepositoryId, "1.4.2", new string('B', 64));
        Check(publicProof.PublicRelease && publicProof.Channel == "Stable" && History(state, publicProof), "generic public GA history accepts an advanced PreRelease-only retained row");
        RefuseHistory(state, Verify(text.Replace("purpose=" + ReleaseFeedPolicy.PublicPurpose + "\n", "purpose=release/Stable\n", StringComparison.Ordinal)), "narrow Stable proof cannot borrow a PreRelease history row");
        state = new ReceiptChannelState(root, "Beta", new string('a', 32), 0, null, "-", ReleaseFeedPolicy.BetaRepositoryId, beta.Version, beta.Descriptor.DescriptorSha256,
            ReleaseFeedPolicy.StableRepositoryId, "1.4.1", new string('B', 64));
        Check(!History(state, publicProof), "same-version different public proof still cannot retire retained history");
        state = new ReceiptChannelState(root, "Beta", new string('a', 32), 0, null, "-", ReleaseFeedPolicy.BetaRepositoryId, beta.Version, beta.Descriptor.DescriptorSha256);
        RefuseHistory(state, publicProof, "public history cannot borrow a private Beta-only row");
    }
    private static void MetadataCases(string root)
    {
        var good = Read(Path.Combine(root, "GOOD.dll"));
        Check(InstalledPluginMetadata.ReadMaintenance(good).Version == "1.4.0", "maintenance parser accepts a local constructor MethodDef");
        foreach (var name in new[] { "WRONG_NAME", "WRONG_PROTOCOL", "STRING_PROTOCOL", "NONLITERAL_PROTOCOL", "NO_ATTRIBUTE", "DUPLICATE_ATTRIBUTE", "WRONG_ATTRIBUTE" })
        {
            var image = Read(Path.Combine(root, name + ".dll"));
            Check(InstalledPluginMetadata.Read(image).Version == "1.4.0", name + ": ordinary updater read remains compatible");
            Reject(() => InstalledPluginMetadata.ReadMaintenance(image), name + ": strict maintenance metadata rejected");
        }
        var needle = Encoding.UTF8.GetBytes(InstalledPluginMetadata.Owner); var matches = new List<int>();
        for (var i = 3; i <= good.Length - needle.Length; i++) if (good.AsSpan(i, needle.Length).SequenceEqual(needle)) matches.Add(i);
        Check(matches.Count == 1 && good[matches[0] - 1] == needle.Length && good[matches[0] - 3] == 1 && good[matches[0] - 2] == 0, "bounded custom-attribute mutation identifies the actual serialized GUID");
        var badGuid = (byte[])good.Clone(); badGuid[matches[0]] = (byte)'x';
        Reject(() => InstalledPluginMetadata.ReadMaintenance(badGuid), "attribute GUID must equal the literal owner");
        var badProlog = (byte[])good.Clone(); badProlog[matches[0] - 3] = 2;
        Reject(() => InstalledPluginMetadata.ReadMaintenance(badProlog), "malformed custom-attribute prolog rejected");
    }
    private static int Main(string[] args)
    {
        if (args.Length != 7 || Path.GetDirectoryName(Path.GetFullPath(args[0])) != Path.GetFullPath(args[1]) || Directory.Exists(args[0]))
            throw new ArgumentException("Fresh direct artifact scratch and two exact signed packages required");
        Scratch = args[0]; Local = Path.Combine(Scratch, "fixture-local"); Directory.CreateDirectory(Local);
        var saved = Environment.GetEnvironmentVariable("LOCALAPPDATA"); Environment.SetEnvironmentVariable("LOCALAPPDATA", Local);
        try
        {
            var stable = new Package(args[2], args[3]); var beta = new Package(args[4], args[5]); var stableBefore = Snapshot(stable.Root); var betaBefore = Snapshot(beta.Root);
            MetadataCases(args[6]); Happy(stable, beta); Refusals(stable, beta); Interruptions(stable, beta); HistoryPredicate(stable, beta);
            Unchanged(stableBefore, stable.Root, "immutable stable package unchanged"); Unchanged(betaBefore, beta.Root, "immutable beta package unchanged");
            Json(Path.Combine(Scratch, "verification.json"), new { Passed = true, Checks, Stable = stable.Root, StableManifest = stable.ManifestHash, Beta = beta.Root, BetaManifest = beta.ManifestHash,
                ActualInstallation = false, SigningOrKeys = false, GameSteamNetworkOrRuntimeExecuted = false });
            Console.WriteLine("Native reinstall generation: " + Checks + " assertions PASS. Evidence: " + Scratch); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", saved); }
    }
}
