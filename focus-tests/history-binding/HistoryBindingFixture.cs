#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty.Updating;
using BetterAstralParty.Observability;
using BetterAstralParty.Diagnostics;

internal static class HistoryBindingFixture {
    private static string[] WorkDirectories(string root, string kind)
    { using var fence = new WindowsFileFence(root); var paths = new System.Collections.Generic.List<string>(); foreach (var relative in UpdateWorkPath.Directories(fence, kind)) paths.Add(fence.Full(relative)); return paths.ToArray(); }
    private static string WorkRelative(string root, string path) => path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');

    private const string KeyId = "history-binding-fixture";
    private static readonly List<object> Cases = new();
    private static string Base = "";
    private static byte[] Inert = null!;
    private static int Assertions, Sequence;
    private static RSA Key = null!;
    private static UpdateTrust Trust = null!;
    private static void Check(bool value, string name) { Assertions++; if (!value) throw new Exception(name); }
    private static void Case(string name, Action body) {
        try { body(); Cases.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { Cases.Add(new { name, passed = false, failure = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private sealed class Package {
        internal readonly string Version, Channel;
        internal readonly ReleaseFeed Feed;
        internal readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        internal readonly byte[] Descriptor, Signature, Zip;
        internal readonly VerifiedUpdateDescriptor Verified;
        internal Package(string version, string channel, byte[] plugin, bool publicScope = false) {
            Version = version; Channel = channel; Feed = ReleaseFeedPolicy.For(channel);
            foreach (var path in InstallReceipt.Owned)
                Files[path] = path == UpdateTrust.PluginPath ? plugin :  Encoding.ASCII.GetBytes("inert owned fixture " + version + " " + path);
            Files["BetterAstralParty.compatibility.json"] = CompatibilityFixtureData.Manifest(Inert);
            using (var memory = new MemoryStream()) {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var file in Files) {
                    using var stream = zip.CreateEntry(file.Key, CompressionLevel.NoCompression).Open(); stream.Write(file.Value);
                }
                Zip = memory.ToArray();
            }
            var text = new StringBuilder("BetterAstralParty.UpdateDescriptor/v5\nrepository=" + Feed.Repository + "\nversion=" + version + "\nchannel=" + (publicScope ? "Stable" : channel) + "\nplatform=" + UpdateTrust.PlatformId
                + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-" + version + "-update.zip\nzip-bytes=" + Zip.Length + "\nzip-sha256=" + UpdateTrust.Hash(Zip)
                + "\nfile-count=" + InstallReceipt.Owned.Length + "\npurpose=" + (publicScope ? ReleaseFeedPolicy.PublicPurpose : "release/" + channel) + "\nrepository-id=" + Feed.RepositoryId + "\nsettings-schema=1\n");
            foreach (var path in InstallReceipt.Owned) text.Append("file=").Append(path).Append('\t').Append(Files[path].Length).Append('\t').Append(UpdateTrust.Hash(Files[path])).Append('\n');
            Descriptor = Encoding.ASCII.GetBytes(text.ToString()); Signature = Key.SignData(Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            Verified = Trust.VerifyProposal(Descriptor, KeyId, Signature, new UpdateContext(Feed.Repository, channel, "0.0.0", version, 123, 456, UpdateTrust.PlatformId, repositoryId: Feed.RepositoryId));
        }
    }
    private static void Put(string root, string relative, byte[] bytes) {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }
    private static string Fresh(Package initial, bool water = true) {
        var root = Path.Combine(Base, (++Sequence).ToString("D2")); Directory.CreateDirectory(root); CompatibilityFixtureData.Seed(root, Inert);
        foreach (var file in initial.Files) Put(root, file.Key, file.Value);
        Put(root, "doorstop_config.ini", Encoding.ASCII.GetBytes("[General]\nenabled=false\n"));
        Put(root, ChannelTransitionIntent.SettingsPath, Encoding.ASCII.GetBytes("[Updates]\nUpdateChannel = Stable\nAutoDownload = false\nApplyAfterExit = false\n"));
        Put(root, "user-settings.keep", Encoding.UTF8.GetBytes("unowned fixture must survive"));
        using var fence = new WindowsFileFence(root);
        Put(root, InstallReceipt.Path, InstallReceipt.Create(fence.RootIdentity.Text, initial.Verified, false));
        if (water) Put(root, UpdateTransaction.HighWaterPath, Water(fence.RootIdentity.Text, initial.Version, initial.Verified.DescriptorSha256));
        return root;
    }
    private static byte[] Water(string root, string version, string digest) => Encoding.ASCII.GetBytes("BetterAstralParty.HighWater/v1\nroot=" + root + "\nversion=" + version + "\ndescriptor=" + digest + "\n");
    private static InstallReceipt Receipt(string root) => InstallReceipt.Parse(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path)));
    private sealed class HistoryLease : IDisposable {
        private readonly WindowsFileFence Fence;
        private readonly IDisposable Proof;
        internal HistoryLease(WindowsFileFence fence, IDisposable proof) { Fence = fence; Proof = proof; }
        public void Dispose() { try { Proof.Dispose(); } finally { Fence.Dispose(); } }
    }
    private static IDisposable Validate(string root) {
        var fence = new WindowsFileFence(root);
        try { return new HistoryLease(fence, UpdateTransaction.ValidateHistory(fence, Receipt(root), Trust)); }
        catch { fence.Dispose(); throw; }
    }
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetFileName(path) != "BetterAstralParty.update.lock")
        .ToDictionary(path => Path.GetRelativePath(root, path), path => UpdateTrust.Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
    private static void Preserved(string root, Dictionary<string, string> before, string name) {
        var after = Snapshot(root); Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value), name + " leaves all fixture/evidence bytes unchanged");
    }
    private static ApplySafetyException Refused(string root, string reason) {
        var before = Snapshot(root);
        try { using var proof = Validate(root); throw new Exception("Accepted " + reason); }
        catch (ApplySafetyException error) { Check((string?)error.Data["BapRecoveryReason"] == reason, "exact refusal " + reason); Preserved(root, before, reason); return error; }
    }
    private static string Apply(string root, Package package, ChannelTransitionIntent? intent = null, Action<string>? boundary = null) {
        var prior = WorkDirectories(root, "txn").ToHashSet(StringComparer.Ordinal);
        using var fence = new WindowsFileFence(root); var current = Receipt(root).Channels!.ActiveVersion;
        var context = new UpdateContext(package.Feed.Repository, package.Channel, current, package.Version, 123, 456, UpdateTrust.PlatformId, repositoryId: package.Feed.RepositoryId, transition: intent);
        Check(UpdateTransaction.Apply(fence, package.Descriptor, KeyId, package.Signature, package.Zip, Trust, context, boundary) == TransactionResult.Committed, "real native apply committed " + package.Version);
        return WorkRelative(root, WorkDirectories(root, "txn").Single(path => !prior.Contains(path)));
    }
    private static void RecoverTwice(string root, string work, Package package) {
        using var fence = new WindowsFileFence(root);
        var context = new UpdateContext(package.Feed.Repository, package.Channel, "0.0.0", package.Version, 123, 456, UpdateTrust.PlatformId, repositoryId: package.Feed.RepositoryId);
        Check(UpdateTransaction.Recover(fence, work, Trust, context) == TransactionResult.Committed, "committed recovery");
        Check(UpdateTransaction.Recover(fence, work, Trust, context) == TransactionResult.Committed, "committed recovery repeat");
    }
    private static void ManualAdvance(string root, Package package) {
        var prior = Receipt(root); using var fence = new WindowsFileFence(root);
        var channels = prior.Channels!.Applied(fence.RootIdentity.Text, package.Verified, Guid.NewGuid().ToString("N"));
        foreach (var file in package.Files) Put(root, file.Key, file.Value);
        Put(root, InstallReceipt.Path, prior.Updated(package.Verified, channels));
    }
    private static ChannelTransitionIntent Approve(string root, Package target) {
        using var fence = new WindowsFileFence(root); return ChannelTransitionIntent.Approve(ChannelTransitionIntent.Review(fence, target.Verified));
    }
    public static int Main(string[] args) {
        try {
            if (args.Length != 7) throw new ArgumentException("Fresh fixture root, inert PE and five metadata images required");
            Base = Path.GetFullPath(args[0]); if (Directory.Exists(Base) || !Base.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar)) throw new ArgumentException("Fresh artifact-only fixture root required");
            Directory.CreateDirectory(Base); Inert = File.ReadAllBytes(args[1]);
            using var key = RSA.Create(3072); Key = key;
            Trust = new UpdateTrust(new[] { new TrustedUpdateKey(KeyId, key.ExportParameters(false)) });
            // Exercise production feed/metadata policy with only this in-memory fixture key.
            typeof(UpdateTrust).GetField("_production", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Trust, true);
            var initial = new Package("1.4.0", "Stable", File.ReadAllBytes(args[2]));
            var beta = new Package("1.4.1-rc.1", "Beta", File.ReadAllBytes(args[3]));
            var signed = new Package("1.4.2", "Stable", File.ReadAllBytes(args[4]));
            var manual = new Package("1.4.3", "Stable", File.ReadAllBytes(args[5]));
            var next = new Package("1.4.4", "Stable", File.ReadAllBytes(args[6]));
            var publicRc = new Package("1.4.1-rc.1", "PreRelease", File.ReadAllBytes(args[3]), true);
            var manualPre = new Package("1.4.3", "PreRelease", File.ReadAllBytes(args[5]), true);
            var diagnosticsRoot = Path.Combine(Base, "diagnostics"); Directory.CreateDirectory(diagnosticsRoot);
            using var owner = DiagnosticOwner.Start(diagnosticsRoot, true, "1.4.0", new string('A', 32));
            Check(DiagnosticHub.Bind(owner), "real diagnostic owner bound");
            try {
                Case("optional absent high-water", () => { var root = Fresh(initial, false); var before = Snapshot(root); using (Validate(root)) { } Preserved(root, before, "optional absence"); });
                Case("exact version and descriptor pair", () => { var root = Fresh(initial); var before = Snapshot(root); using (Validate(root)) { } Preserved(root, before, "exact pair"); });
                Case("same version wrong descriptor", () => { var root = Fresh(initial); using var f = new WindowsFileFence(root); Put(root, UpdateTransaction.HighWaterPath, Water(f.RootIdentity.Text, initial.Version, new string('B', 64))); Refused(root, "HighWaterUnbound"); });
                Case("orphan older water needs proof", () => { var root = Fresh(initial); using var f = new WindowsFileFence(root); Put(root, UpdateTransaction.HighWaterPath, Water(f.RootIdentity.Text, "1.4.0-rc.1", new string('A', 64))); Refused(root, "HighWaterProofMissing"); });
                Case("wrong root", () => { var root = Fresh(initial); Put(root, UpdateTransaction.HighWaterPath, Water("00000000:0000000000000000", initial.Version, initial.Verified.DescriptorSha256)); Refused(root, "HighWaterRootMismatch"); });
                Case("malformed water", () => { var root = Fresh(initial); Put(root, UpdateTransaction.HighWaterPath, new byte[] { 255, 13 }); Refused(root, "HighWaterInvalid"); });
                Case("repository mismatch", () => { var root = Fresh(initial); var receipt = Receipt(root); var state = receipt.Channels!; var wrong = new ReceiptChannelState(state.Root, "Stable", state.Operation, state.StableRepositoryId + 1, state.StableVersion, state.StableDescriptor, 0, null, "-"); Put(root, InstallReceipt.Path, receipt.Updated(initial.Verified, wrong)); Refused(root, "HighWaterRepositoryMismatch"); });
                Case("transient native lock remains 32", () => {
                    var root = Fresh(initial); var before = Snapshot(root);
                    using (var held = File.Open(Path.Combine(root, UpdateTransaction.HighWaterPath), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                        try { using var proof = Validate(root); throw new Exception("Held water accepted"); }
                        catch (ApplySafetyException error) {
                            Check(error.Failure == ApplyFailure.FileBusy && error.NativeError == 32, "original native sharing violation retained");
                            error.Data["token"] = "FAKE_PRIVATE_DIAGNOSTIC_DATA";
                            DiagnosticHub.Failure(DiagnosticFeature.Helper, DiagnosticPhase.DescriptorVerification, DiagnosticCode.FilesBusy, error, error.NativeError, applyCode: (int)error.Failure, targetVersion: signed.Version);
                        }
                    }
                    Preserved(root, before, "held water");
                });
                Case("manual row advancement retains authenticated older signed history", () => {
                    var root = Fresh(initial); Apply(root, signed); var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath)); ManualAdvance(root, manual);
                    var before = Snapshot(root); using (Validate(root)) { } Preserved(root, before, "manual newer row");
                    Check(File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath)).SequenceEqual(water), "manual advance retains old history bytes");
                    var work = Apply(root, next); RecoverTwice(root, work, next);
                });
                Case("public RC history survives a newer PreRelease-only row", () => {
                    var root = Fresh(initial); var work = Apply(root, publicRc, Approve(root, publicRc));
                    var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath)); ManualAdvance(root, manualPre);
                    var state = Receipt(root).Channels!;
                    Check(new[] { "Stable", "PreRelease", "Beta" }.All(channel => state.VersionFor(channel) != publicRc.Version || state.DescriptorFor(channel) != publicRc.Verified.DescriptorSha256), "public RC validation cannot use an exact current channel-row shortcut");
                    string stage; using (var fence = new WindowsFileFence(root)) stage = UpdateRecovery.Scan(fence).Single(entry => entry.Work == work).Stage;
                    var before = Snapshot(root);
                    using (Validate(root)) {
                        try { using var replace = File.Open(Path.Combine(root, stage, "descriptor.bin"), FileMode.Open, FileAccess.Write, FileShare.None); throw new Exception("Public RC history was not read or leased"); }
                        catch (IOException) { Check(true, "nonexact public RC history branch retains its authenticated descriptor lease"); }
                    }
                    Preserved(root, before, "public RC historical row");
                    Check(File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath)).SequenceEqual(water), "public RC historical validation preserves the retained watermark bytes");
                });
                Case("historical proof stays leased", () => {
                    var root = Fresh(initial); var work = Apply(root, signed); ManualAdvance(root, manual);
                    string stage;
                    using (var fence = new WindowsFileFence(root)) stage = UpdateRecovery.Scan(fence).Single(entry => entry.Work == work).Stage;
                    using (var proof = Validate(root)) {
                        try { using var replace = File.Open(Path.Combine(root, stage, "descriptor.bin"), FileMode.Open, FileAccess.Write, FileShare.None); throw new Exception("Historical descriptor not pinned"); }
                        catch (IOException) { Check(true, "proof replacement denied while validator lease retained"); }
                        try { File.Delete(Path.Combine(root, stage, "descriptor.bin")); throw new Exception("Historical descriptor deletion accepted"); }
                        catch (IOException) { Check(true, "proof deletion denied while validator lease retained"); }
                        try { File.Move(Path.Combine(root, stage, "descriptor.bin"), Path.Combine(root, stage, "descriptor.renamed")); throw new Exception("Historical descriptor rename accepted"); }
                        catch (IOException) { Check(true, "proof rename denied while validator lease retained"); }
                        using var mutableFence = new WindowsFileFence(root);
                        using (var reader = mutableFence.OpenFile(stage + "/descriptor.bin", false)) Check(reader.Hash() == signed.Verified.DescriptorSha256, "common immutable proof reader succeeds while history lease retained");
                        try { using var mutable = mutableFence.OpenFile(stage + "/descriptor.bin", true); throw new Exception("Mutable proof reopen accepted"); }
                        catch (ApplySafetyException error) { Check(error.Failure == ApplyFailure.FileBusy && error.NativeError == 32, "mutable proof reopen denied with original lock code"); }
                    }
                });
                Case("corrupt historical signature refuses without repairs", () => {
                    var root = Fresh(initial); var work = Apply(root, signed); ManualAdvance(root, manual);
                    using var fence = new WindowsFileFence(root); var stage = UpdateRecovery.Scan(fence).Single(entry => entry.Work == work).Stage;
                    var path = Path.Combine(root, stage, "signature.bin"); var bytes = File.ReadAllBytes(path); bytes[0] ^= 1; File.WriteAllBytes(path, bytes); Refused(root, "HighWaterProofChanged");
                });
                Case("locked historical proof records fixed busy reason", () => {
                    var root = Fresh(initial); var work = Apply(root, signed); ManualAdvance(root, manual);
                    string stage; using (var fence = new WindowsFileFence(root)) stage = UpdateRecovery.Scan(fence).Single(entry => entry.Work == work).Stage;
                    var before = Snapshot(root);
                    using (var locked = File.Open(Path.Combine(root, stage, "descriptor.bin"), FileMode.Open, FileAccess.Read, FileShare.None)) {
                        try { using var proof = Validate(root); throw new Exception("Locked historical descriptor accepted"); }
                        catch (ApplySafetyException error) { Check(error.Failure == ApplyFailure.FileBusy && error.NativeError == 32 && (string?)error.Data["BapRecoveryReason"] == "HighWaterProofBusy", "proof busy keeps exact fixed reason and original native code"); }
                    }
                    Preserved(root, before, "locked historical proof");
                });
                Case("higher cross-channel target without approval refuses before mutation", () => {
                    var root = Fresh(initial); var before = Snapshot(root); using var fence = new WindowsFileFence(root);
                    var context = new UpdateContext(beta.Feed.Repository, beta.Channel, initial.Version, beta.Version, 123, 456, UpdateTrust.PlatformId, repositoryId: beta.Feed.RepositoryId);
                    try { UpdateTransaction.Apply(fence, beta.Descriptor, KeyId, beta.Signature, beta.Zip, Trust, context); throw new Exception("Unapproved cross-channel apply accepted"); }
                    catch (UpdateValidationException error) { Check(error.Failure == UpdateFailure.WrongTarget, "higher selected-repository switch still requires explicit approval"); }
                    Preserved(root, before, "cross without approval");
                    Check(WorkDirectories(root, "stage").Length == 0 && WorkDirectories(root, "txn").Length == 0, "no stage or transaction created for unapproved cross-channel target");
                });
                Case("copied other-channel water authenticates lineage after manual advancement", () => {
                    var root = Fresh(initial); Apply(root, signed); string oldId;
                    using (var fence = new WindowsFileFence(root)) using (var file = fence.OpenFile(UpdateTransaction.HighWaterPath)) oldId = file.Identity.Text;
                    var approval = Approve(root, beta); Apply(root, beta, approval);
                    using (var fence = new WindowsFileFence(root)) using (var file = fence.OpenFile(UpdateTransaction.HighWaterPath)) Check(file.Identity.Text != oldId, "cross-channel copy creates a new water slot identity");
                    Check(Receipt(root).Channels!.ActiveChannel == "Beta", "real cross-channel native apply");
                    ManualAdvance(root, manual); var before = Snapshot(root); using (Validate(root)) { } Preserved(root, before, "copied historical lineage");
                    var work = Apply(root, next); RecoverTwice(root, work, next);
                });
                Case("copied lineage rejects changed original water evidence", () => {
                    var root = Fresh(initial); Apply(root, signed); var work = Apply(root, beta, Approve(root, beta)); ManualAdvance(root, manual);
                    var oldWater = Path.Combine(root, work, "old-" + (InstallReceipt.Owned.Length + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); var bytes = File.ReadAllBytes(oldWater); bytes[0] ^= 1; File.WriteAllBytes(oldWater, bytes); Refused(root, "HighWaterProofChanged");
                });
                Case("actual history recorder to actual ZIP privacy", () => {
                    Check(owner.Stop(DiagnosticEndReason.OwnerDisposed, 2000), "real typed diagnostics flushed");
                    var bundle = DiagnosticBundle.Collect(diagnosticsRoot, "", "", "", Path.Combine(Base, "hub"), Path.Combine(Base, "fallback"), "8.0");
                    using var zip = ZipFile.OpenRead(bundle.ArchivePath); var all = new StringBuilder();
                    foreach (var entry in zip.Entries) { using var reader = new StreamReader(entry.Open()); all.Append(reader.ReadToEnd()); }
                    var text = all.ToString();
                    foreach (var code in new[] { "Integrity", "WrongTarget", "Prerequisite", "FilesBusy" }) Check(text.Contains("\"code\":\"" + code + "\""), "existing history/lock code included: " + code);
                    Check(text.Contains("\"native_code\":32") && text.Contains("\"apply_code\":3") && text.Contains("\"apply_code\":7"), "native/apply distinctions survive existing ZIP projection");
                    Check(!text.Contains("FAKE_PRIVATE") && !text.Contains(owner.ProcessSession) && !text.Contains(Base), "raw exception Data, session and source paths omitted");
                });
            } finally { owner.Stop(DiagnosticEndReason.OwnerDisposed, 2000); DiagnosticHub.Unbind(owner); }
            var passed = Cases.All(value => JsonSerializer.Serialize(value).Contains("\"passed\":true"));
            Console.WriteLine(JsonSerializer.Serialize(new { passed, assertions = Assertions, cases = Cases, ephemeralFixtureKeyOnly = true, productionPrivateKeyOpened = false, actualGameSteamOrProductMainExecuted = false, networkRequests = false, sourceLinkedNativeCore = true }));
            return passed ? 0 : 1;
        } catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }
}
