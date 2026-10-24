#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BetterAstralParty.Updating;

internal static class TransactionFixture
{
    private static string[] WorkDirectories(string root, string kind)
    { using var fence = new WindowsFileFence(root); var paths = new System.Collections.Generic.List<string>(); foreach (var relative in UpdateWorkPath.Directories(fence, kind)) paths.Add(fence.Full(relative)); return paths.ToArray(); }
    private static string WorkRelative(string root, string path) => path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');

    private static int _checks;
    private const string AbsentOwned = "BetterAstralParty-Mod.cmd";
    private static readonly UpdateContext Selection = new UpdateContext("TSM701/BetterAstralParty", "Stable", "99.0.0", "1.4.0", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
    private sealed class Interruption : Exception { }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); _checks++; }
    private static void Reject(Action action, string name)
    { try { action(); throw new Exception("Accepted: " + name); } catch (ApplySafetyException) { _checks++; } catch (UpdateValidationException) { _checks++; } }
    private sealed class Package : IDisposable
    {
        private readonly RSA _signing;
        internal readonly UpdateTrust Trust;
        internal readonly byte[] Zip, Descriptor, Signature;
        internal readonly Dictionary<string, byte[]> Old = new Dictionary<string, byte[]>(StringComparer.Ordinal), New = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        internal byte[] OldReceipt;
        internal byte[] PublicModulus { get { return _signing.ExportParameters(false).Modulus!; } }
        internal Package(byte[] oldPlugin, byte[] newPlugin, byte[] inert, byte[]? compatibility = null)
        {
#if NETFRAMEWORK
            _signing = new RSACng(3072);
#else
            _signing = RSA.Create(3072);
#endif
            foreach (var path in InstallReceipt.Owned) {
                Old[path] = path == UpdateTrust.PluginPath ? oldPlugin :  Encoding.ASCII.GetBytes("old fixture " + path);
                New[path] = path == UpdateTrust.PluginPath ? newPlugin :  Encoding.ASCII.GetBytes("new fixture " + path);
            }
            New["BetterAstralParty.compatibility.json"] = compatibility ?? CompatibilityFixtureData.Manifest(inert);
            OldReceipt = Receipt("ABCD1234:0000000000000100");
            using (var output = new MemoryStream()) {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var path in InstallReceipt.Owned) {
                    var entry = archive.CreateEntry(path, CompressionLevel.NoCompression); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using (var stream = entry.Open()) stream.Write(New[path], 0, New[path].Length);
                }
                Zip = output.ToArray();
            }
            var descriptor = new StringBuilder("BetterAstralParty.UpdateDescriptor/v5\nrepository=TSM701/BetterAstralParty\nversion=1.4.0\nchannel=Stable\nplatform=" + UpdateTrust.PlatformId
                + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-1.4.0-update.zip\nzip-bytes=" + Zip.Length + "\nzip-sha256=" + UpdateTrust.Hash(Zip) + "\nfile-count=4\npurpose=release/Stable\nrepository-id=" + ReleaseFeedPolicy.StableRepositoryId + "\nsettings-schema=1\n");
            foreach (var path in InstallReceipt.Owned) descriptor.Append("file=").Append(path).Append('\t').Append(New[path].Length).Append('\t').Append(UpdateTrust.Hash(New[path])).Append('\n');
            Descriptor = Encoding.ASCII.GetBytes(descriptor.ToString()); Signature = _signing.SignData(Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            Trust = new UpdateTrust(new[] { new TrustedUpdateKey("transaction-fixture", _signing.ExportParameters(false)) });
        }
        internal byte[] Receipt(string root)
        {
            var version = InstalledPluginMetadata.Read(Old[UpdateTrust.PluginPath]).Version;
            var channels = new ReceiptChannelState(root, "Stable", new string('a', 32), ReleaseFeedPolicy.StableRepositoryId, version, new string('A', 64), 0, null, "-");
            var receipt = new StringBuilder("{\"schema\":1,\"baseline\":\"1.4.0\",\"owner\":\"" + InstalledPluginMetadata.Owner + "\",\"loaderBefore\":false,\"files\":[");
            for (var i = 0; i < InstallReceipt.Owned.Length; i++) { var path = InstallReceipt.Owned[i]; if (i != 0) receipt.Append(','); receipt.Append("{\"path\":\"").Append(path).Append("\",\"sha256\":\"").Append(UpdateTrust.Hash(Old[path])).Append("\"}"); }
            return Encoding.ASCII.GetBytes(receipt.Append("],\"channelState\":\"").Append(channels.Encoded).Append("\"}\n").ToString());
        }
        internal byte[] Sign(byte[] descriptor) { return _signing.SignData(descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); }
        public void Dispose() { _signing.Dispose(); }
    }
    private static string Install(string baseDirectory, string name, Package package, byte[] inert, bool watermark = true)
    {
        var root = Path.Combine(baseDirectory, name); Directory.CreateDirectory(Path.Combine(root, "BepInEx/plugins/BetterAstralParty"));
        CompatibilityFixtureData.Seed(root,inert);
        foreach (var file in package.Old) if (file.Key != AbsentOwned) File.WriteAllBytes(Path.Combine(root, file.Key), file.Value);
        using (var fence = new WindowsFileFence(root)) package.OldReceipt = package.Receipt(fence.RootIdentity.Text);
        File.WriteAllBytes(Path.Combine(root, InstallReceipt.Path), package.OldReceipt); File.WriteAllBytes(Path.Combine(root, "AstralParty_INT.exe"), inert);
        File.WriteAllText(Path.Combine(root, "user-settings.keep"), "untouched");
        foreach (var file in SharedFiles(false)) {
            var target = Path.Combine(root, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, file.Value);
        }
        if (watermark) using (var fence = new WindowsFileFence(root)) File.WriteAllText(Path.Combine(root, UpdateTransaction.HighWaterPath), "BetterAstralParty.HighWater/v1\nroot=" + fence.RootIdentity.Text + "\nversion=" + InstalledPluginMetadata.Read(package.Old[UpdateTrust.PluginPath]).Version + "\ndescriptor=" + new string('A', 64) + "\n", new UTF8Encoding(false));
        return root;
    }
    private static string Work(string root) { var dirs = WorkDirectories(root, "txn"); if (dirs.Length != 1) throw new Exception("Expected one transaction"); return WorkRelative(root, dirs[0]); }
    private static TransactionResult Apply(string root, Package package, Action<string>? hook = null)
    { using (var fence = new WindowsFileFence(root)) return UpdateTransaction.Apply(fence, package.Descriptor, "transaction-fixture", package.Signature, package.Zip, package.Trust, Selection, hook); }
    private static TransactionResult Recover(string root, Package package, Action<string>? hook = null)
    { using (var fence = new WindowsFileFence(root)) return UpdateTransaction.Recover(fence, Work(root), package.Trust, Selection, hook); }
    private static Dictionary<string, byte[]> SharedFiles(bool enabled)
    {
        var text = "\ufeff[General]\r\nenabled = " + (enabled ? "true" : "false") + "\r\n# 사용자 설정 유지\r\ncustom_path = mods/custom-loader.dll\r\n";
        return new Dictionary<string, byte[]>(StringComparer.Ordinal) {
            { "doorstop_config.ini", Encoding.UTF8.GetBytes(text) },
            { "BepInEx/config/BepInEx.cfg", Encoding.UTF8.GetBytes("\ufeff[Logging]\r\nUnityLogListening = true\r\ncustom = 사용자 값\r\n") },
            { "BepInEx/plugins/OtherMod/OtherMod.dll", new byte[] { 0, 255, 17, 128, 3, 64 } },
            { "BepInEx/config/other.mod.cfg", Encoding.UTF8.GetBytes("[OtherMod]\r\nenabled = true\r\nuser_choice = 유지\r\n") }
        };
    }
    private static void Verify(string root, Package package, bool committed, byte[] originalWater, bool loaderEnabled = false, bool? loaderBefore = false)
    {
        foreach (var path in InstallReceipt.Owned) {
            var target = Path.Combine(root, path);
            if (!committed && path == AbsentOwned) Check(!File.Exists(target), "original absence restored");
            else Check(File.Exists(target) && UpdateTrust.Hash(File.ReadAllBytes(target)) == UpdateTrust.Hash((committed ? package.New : package.Old)[path]), "exact known file retained: " + path);
        }
        var receipt = InstallReceipt.Parse(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path)));
        Check(receipt.LoaderBefore == loaderBefore, "original loader baseline stays historical evidence");
        foreach (var path in InstallReceipt.Owned) Check(receipt.Files[path] == UpdateTrust.Hash((committed ? package.New : package.Old)[path]), "receipt matches result");
        var waterPath = Path.Combine(root, UpdateTransaction.HighWaterPath);
        Check(committed ? File.ReadAllText(waterPath).Contains("version=1.4.0\n") : originalWater.Length == 0 ? !File.Exists(waterPath) : UpdateTrust.Hash(File.ReadAllBytes(waterPath)) == UpdateTrust.Hash(originalWater), "high-water matches commit/rollback");
        Check(File.ReadAllText(Path.Combine(root, "user-settings.keep")) == "untouched", "unowned content preserved");
        foreach (var file in SharedFiles(loaderEnabled)) Check(File.Exists(Path.Combine(root, file.Key)) && UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, file.Key))) == UpdateTrust.Hash(file.Value), "shared loader/other mod exact bytes preserved: " + file.Key);
        Check(InstalledPluginMetadata.Read(File.ReadAllBytes(Path.Combine(root, UpdateTrust.PluginPath))).Version == (committed ? "1.4.0" : "1.4.0-dev"), "installed metadata result");
    }
    private static void ChannelMatrix(string baseDirectory, Package package, byte[] inert)
    {
        var feeds = new[] { ReleaseFeedPolicy.Stable, ReleaseFeedPolicy.PreRelease, ReleaseFeedPolicy.Beta };
        foreach (var source in feeds) foreach (var target in feeds)
        foreach (var publicScope in ReleaseFeedPolicy.IsPublicChannel(target.Channel) ? new[] { false, true } : new[] { false }) {
            if (source.Channel == target.Channel) continue;
            var descriptor = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(package.Descriptor)
                .Replace("repository=" + ReleaseFeedPolicy.StableRepository + "\n", "repository=" + target.Repository + "\n")
                .Replace("repository-id=" + ReleaseFeedPolicy.StableRepositoryId.ToString(CultureInfo.InvariantCulture) + "\n", "repository-id=" + target.RepositoryId.ToString(CultureInfo.InvariantCulture) + "\n")
                .Replace("channel=Stable\n", "channel=" + target.Channel + "\n").Replace("purpose=release/Stable\n", "purpose=release/" + target.Channel + "\n"));
            if(publicScope) descriptor=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(descriptor)
                .Replace("channel="+target.Channel+"\n","channel=Stable\n").Replace("purpose=release/"+target.Channel+"\n","purpose=release/Public\n"));
            var signature = package.Sign(descriptor);
            var proposalContext = new UpdateContext(target.Repository, target.Channel, "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId, repositoryId: target.RepositoryId);
            var proposal = package.Trust.VerifyProposal(descriptor, "transaction-fixture", signature, proposalContext);
            Check(proposal.PublicRelease==publicScope && proposal.Channel==target.Channel,"signed public scope keeps the explicitly selected transaction channel");
            foreach (var phase in new[] { "commit", "rollback", "forward" })
            foreach (var missingSettingsParent in source.Channel == "Stable" && target.Channel == "PreRelease" && publicScope ? new[] { false, true } : new[] { false }) {
                var label = source.Channel + "-" + target.Channel + "-" + phase + (publicScope ? "-public" : "-narrow") + (missingSettingsParent ? "-absent-settings-parent" : "");
                var root = Install(baseDirectory, "channel-" + label, package, inert);
                using (var fence = new WindowsFileFence(root)) {
                    var channels = new ReceiptChannelState(fence.RootIdentity.Text, source.Channel, new string('a', 32),
                        source.Channel == "Stable" ? source.RepositoryId : 0, source.Channel == "Stable" ? "1.4.0-dev" : null, source.Channel == "Stable" ? new string('A', 64) : "-",
                        source.Channel == "Beta" ? source.RepositoryId : 0, source.Channel == "Beta" ? "1.4.0-dev" : null, source.Channel == "Beta" ? new string('A', 64) : "-",
                        source.Channel == "PreRelease" ? source.RepositoryId : 0, source.Channel == "PreRelease" ? "1.4.0-dev" : null, source.Channel == "PreRelease" ? new string('A', 64) : "-");
                    var encoded = channels.Encoded;
                    if (source.Channel == "Stable" && phase == "commit") {
                        var prior = channels.Text().Replace("ChannelState/v2\n", "ChannelState/v1\n").Replace("PreRelease=0\t-\t-\n", "");
                        encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(prior));
                        Check(ReceiptChannelState.Parse(encoded).Encoded == encoded, "channel matrix starts from canonical current-protocol v1 receipt: " + label);
                    }
                    var originalText = Encoding.ASCII.GetString(package.OldReceipt);
                    var originalState = InstallReceipt.Parse(package.OldReceipt).Channels ?? throw new Exception("Fixture source channel missing");
                    var replacement = originalText.Replace("\"channelState\":\"" + originalState.Encoded + "\"", "\"channelState\":\"" + encoded + "\"");
                    Check(replacement != originalText || originalState.Encoded == encoded, "exact fixture receipt state replacement: " + label);
                    File.WriteAllBytes(Path.Combine(root, InstallReceipt.Path), Encoding.ASCII.GetBytes(replacement));
                }
                var originalReceipt = File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path));
                var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
                var settings = Encoding.UTF8.GetBytes("[일반]\n업데이트 채널=" + target.Channel + "\n[업데이트]\n자동 다운로드=false\n종료 후 적용=false\n");
                var configParent = Path.Combine(root, "BepInEx/config"); var retainedConfig = Path.Combine(root, "retained-inert-config");
                if (missingSettingsParent) Directory.Move(configParent, retainedConfig);
                else File.WriteAllBytes(Path.Combine(root, ChannelTransitionIntent.SettingsPath), settings);
                ChannelTransitionIntent intent;
                using (var fence = new WindowsFileFence(root)) intent = ChannelTransitionIntent.Parse(ChannelTransitionIntent.Approve(ChannelTransitionIntent.Review(fence, proposal)).Bytes());
                if (missingSettingsParent) Check(intent.SettingsHash == "-" && !Directory.Exists(configParent), "explicit channel review binds confirmed settings-parent absence: " + label);
                var selected = new UpdateContext(target.Repository, target.Channel, "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId, repositoryId: target.RepositoryId, transition: intent);
                if (phase == "commit") {
                    using var fence = new WindowsFileFence(root);
                    Check(UpdateTransaction.Apply(fence, descriptor, "transaction-fixture", signature, package.Zip, package.Trust, selected) == TransactionResult.Committed, "approved actual channel transaction commits: " + label);
                } else {
                    var interrupted = false; var boundary = phase == "rollback" ? "before:commit-intent" : "after:commit-intent";
                    try {
                        using var fence = new WindowsFileFence(root);
                        UpdateTransaction.Apply(fence, descriptor, "transaction-fixture", signature, package.Zip, package.Trust, selected, step => { if (step == boundary) { interrupted = true; throw new Interruption(); } });
                    } catch (Interruption) { }
                    Check(interrupted, "actual channel transaction reached its selected interruption boundary: " + label);
                }
                var committed = phase != "rollback";
                for (var repeat = 0; repeat < 2; repeat++) {
                    using var fence = new WindowsFileFence(root);
                    // The first recovery rebuilds its context from the stage like startup does (public releases are channel-neutral).
                    var pending = repeat == 0 && phase != "commit" ? UpdateRecovery.PendingWork(fence) : null;
                    var context = pending != null ? UpdateRecovery.RecoveryContext(fence, pending, package.Trust) : selected;
                    Check(UpdateTransaction.Recover(fence, Work(root), package.Trust, context) == (committed ? TransactionResult.Committed : TransactionResult.RolledBack), "actual channel recovery is repeatable: " + label + "/" + repeat);
                    if (missingSettingsParent) Check(!Directory.Exists(configParent), "channel apply/recovery never creates an absent settings parent: " + label + "/" + repeat);
                }
                if (missingSettingsParent) {
                    Check(File.ReadAllText(Path.Combine(root, Work(root), "plan")).Contains("\nsettings=-\t-\n")
                        && !File.Exists(Path.Combine(root, Work(root), "settings.backup")), "journal retains actual settings absence without an invented backup: " + label);
                    Directory.Move(retainedConfig, configParent);
                }
                Verify(root, package, committed, water);
                var receipt = InstallReceipt.Parse(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path)));
                var state = receipt.Channels ?? throw new Exception("Updated fixture channels missing");
                Check(state.ActiveChannel == (committed ? target.Channel : source.Channel) && state.VersionFor(source.Channel) == "1.4.0-dev" && state.DescriptorFor(source.Channel) == new string('A', 64), "actual channel result retains the source floor/evidence: " + label);
                Check(committed ? state.VersionFor(target.Channel) == "1.4.0" && state.DescriptorFor(target.Channel) == proposal.DescriptorSha256 && state.Text().StartsWith("BetterAstralParty.ChannelState/v2\n", StringComparison.Ordinal)
                    : UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path))) == UpdateTrust.Hash(originalReceipt), "actual channel commit adds the selected v2 row or restores the exact prior receipt: " + label);
                foreach (var other in feeds) if (other.Channel != source.Channel && other.Channel != target.Channel) Check(state.VersionFor(other.Channel) == null && state.DescriptorFor(other.Channel) == "-", "unvisited third channel remains absent: " + label);
                Check(missingSettingsParent ? !File.Exists(Path.Combine(root, ChannelTransitionIntent.SettingsPath))
                    : UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, ChannelTransitionIntent.SettingsPath))) == UpdateTrust.Hash(settings), "explicit channel transaction preserves saved preferences or their absence: " + label);
            }
        }
        Console.WriteLine("Three-channel transactions: all directions and shared-public variants x commit/rollback/forward, plus 3 absent-settings-parent cases with repeated real NTFS recovery; ephemeral fixture signatures only.");
    }
    // A public release applied on the Pre-release channel without a transition is recovered from the channel the
    // plan recorded (channel.bin): interrupted before and after its commit intent, the receipt stays on Pre-release.
    private static void SameChannelPublic(string baseDirectory, Package package, byte[] inert)
    {
        var feed = ReleaseFeedPolicy.PreRelease;
        var descriptor = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(package.Descriptor)
            .Replace("repository=" + ReleaseFeedPolicy.StableRepository + "\n", "repository=" + feed.Repository + "\n")
            .Replace("repository-id=" + ReleaseFeedPolicy.StableRepositoryId.ToString(CultureInfo.InvariantCulture) + "\n", "repository-id=" + feed.RepositoryId.ToString(CultureInfo.InvariantCulture) + "\n")
            .Replace("purpose=release/Stable\n", "purpose=release/Public\n"));
        var signature = package.Sign(descriptor);
        var selected = new UpdateContext(feed.Repository, feed.Channel, "1.4.0-dev", "1.4.0", 123, 456, UpdateTrust.PlatformId, repositoryId: feed.RepositoryId);
        foreach (var boundary in new[] { "before:commit-intent", "after:commit-intent" })
        {
            var root = Install(baseDirectory, "same-prerelease-" + boundary.Replace(':', '-'), package, inert);
            using (var fence = new WindowsFileFence(root))
            {
                var channels = new ReceiptChannelState(fence.RootIdentity.Text, feed.Channel, new string('a', 32), 0, null, "-", 0, null, "-", feed.RepositoryId, "1.4.0-dev", new string('A', 64));
                var originalState = InstallReceipt.Parse(package.OldReceipt).Channels ?? throw new Exception("Fixture source channel missing");
                File.WriteAllBytes(Path.Combine(root, InstallReceipt.Path), Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(package.OldReceipt)
                    .Replace("\"channelState\":\"" + originalState.Encoded + "\"", "\"channelState\":\"" + channels.Encoded + "\"")));
            }
            var originalReceipt = File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path));
            var interrupted = false;
            try {
                using var fence = new WindowsFileFence(root);
                UpdateTransaction.Apply(fence, descriptor, "transaction-fixture", signature, package.Zip, package.Trust, selected, step => { if (step == boundary) { interrupted = true; throw new Interruption(); } });
            } catch (Interruption) { }
            Check(interrupted, "same-channel public apply reached " + boundary);
            var committed = boundary.StartsWith("after", StringComparison.Ordinal);
            for (var repeat = 0; repeat < 2; repeat++) {
                using var fence = new WindowsFileFence(root);
                // Recovery rebuilds its context from the plan, as startup does.
                var pending = UpdateRecovery.PendingWork(fence);
                var context = pending != null ? UpdateRecovery.RecoveryContext(fence, pending, package.Trust) : selected;
                Check(repeat == 1 || pending != null, "interrupted same-channel public apply leaves pending work");
                Check(UpdateTransaction.Recover(fence, Work(root), package.Trust, context) == (committed ? TransactionResult.Committed : TransactionResult.RolledBack),
                    "same-channel public recovery: " + boundary + "/" + repeat);
            }
            var state = InstallReceipt.Parse(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path))).Channels ?? throw new Exception("Recovered fixture channels missing");
            Check(state.ActiveChannel == feed.Channel && (committed ? state.VersionFor(feed.Channel) == "1.4.0"
                : UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Path))) == UpdateTrust.Hash(originalReceipt)), "recovered receipt stays on Pre-release: " + boundary);
        }
        Console.WriteLine("Same-channel public release on Pre-release: recovery from channel.bin before and after commit intent.");
    }
    private static void SharedLoaderPreservation(string baseDirectory, Package package, byte[] inert)
    {
        foreach (bool? baseline in new bool?[] { false, true, null })
        foreach (var enabled in new[] { false, true })
        foreach (var phase in new[] { "commit", "rollback", "forward" }) {
            var baselineText = baseline.HasValue ? baseline.Value ? "true" : "false" : "null";
            var root = Install(baseDirectory, "loader-" + baselineText + "-" + enabled + "-" + phase, package, inert);
            File.WriteAllBytes(Path.Combine(root, InstallReceipt.Path), Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(package.OldReceipt).Replace("\"loaderBefore\":false", "\"loaderBefore\":" + baselineText)));
            foreach (var file in SharedFiles(enabled)) File.WriteAllBytes(Path.Combine(root, file.Key), file.Value);
            var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
            var committed = phase != "rollback";
            if (phase == "commit") Check(Apply(root, package) == TransactionResult.Committed, "shared loader normal commit");
            else {
                var hit = false; var cut = phase == "rollback" ? "before:commit-intent" : "after:commit-intent";
                try { Apply(root, package, label => { if (label == cut) { hit = true; throw new Interruption(); } }); throw new Exception("No shared loader interruption"); } catch (Interruption) { }
                Check(hit, "shared loader interruption: " + cut);
            }
            foreach (var file in SharedFiles(enabled)) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, file.Key))) == UpdateTrust.Hash(file.Value), "shared bytes unchanged before recovery: " + file.Key);
            Check(Recover(root, package) == (committed ? TransactionResult.Committed : TransactionResult.RolledBack), "shared loader recovery direction");
            Verify(root, package, committed, water, enabled, baseline);
            Check(Recover(root, package) == (committed ? TransactionResult.Committed : TransactionResult.RolledBack), "shared loader recovery repeat");
            Verify(root, package, committed, water, enabled, baseline);
        }
        Console.WriteLine("Shared loader ON/OFF, historical false/true/null baseline and other mod bytes preserved across 18 commit/recovery cases.");
    }
    private static void InProcess(string baseDirectory, Package package, byte[] inert)
    {
        var root = Install(baseDirectory, "in-process", package, inert); var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
        var plugin = Path.Combine(root, UpdateTrust.PluginPath);
        // The real loader image lock: the plugin stays mapped by this process while the in-game update renames it aside.
        System.Reflection.Assembly.LoadFrom(plugin); var locked = false; try { using (new FileStream(plugin, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { } } catch (IOException) { locked = true; } catch (UnauthorizedAccessException) { locked = true; }
        Check(locked, "loaded inert plugin image cannot be overwritten in place");
        using (var fence = new WindowsFileFence(root))
            Check(UpdateTransaction.Apply(fence, package.Descriptor, "transaction-fixture", package.Signature, package.Zip, package.Trust, Selection, inProcess: true) == TransactionResult.Committed, "in-process apply replaces a loaded plugin");
        Verify(root, package, true, water);
        Check(Directory.GetFiles(Path.Combine(root, "BetterAstralParty.Update"), "*", SearchOption.AllDirectories).Any(path => UpdateTrust.Hash(File.ReadAllBytes(path)) == UpdateTrust.Hash(package.Old[UpdateTrust.PluginPath])), "loaded original stays aside in contained work until a later start");
        // An apply guard left after this commit is finished work, not an interruption; other phases and targets stay interrupted.
        using (var fence = new WindowsFileFence(root))
        {
            var guard = UpdateFaults.Begin(fence, Selection, FaultPhase.Apply);
            Check(UpdateFaults.Committed(fence, guard), "apply guard whose target is the installed plugin counts as committed");
            UpdateFaults.Complete(fence, guard);
            var download = UpdateFaults.Begin(fence, Selection, FaultPhase.Download);
            Check(!UpdateFaults.Committed(fence, download), "a download guard never counts as committed");
            UpdateFaults.Complete(fence, download);
            var other = UpdateFaults.Begin(fence, new UpdateContext(Selection.Repository, Selection.Channel, Selection.CurrentVersion, "1.4.1", Selection.ReleaseId, Selection.AssetId, UpdateTrust.PlatformId, repositoryId: Selection.RepositoryId), FaultPhase.Apply);
            Check(!UpdateFaults.Committed(fence, other), "an apply guard for another target stays interrupted");
            UpdateFaults.Complete(fence, other);
        }
        Console.WriteLine("In-process apply: loaded plugin renamed aside and replaced; next launch uses the new version.");
    }
    // Finished operations and acknowledged faults leave nothing in the game root; records earlier versions archived
    // there are removed only when they are this root's v5 records, and anything unverified stays.
    private static void FinishedRecords(string baseDirectory)
    {
        var root = Path.Combine(baseDirectory, "finished-records"); Directory.CreateDirectory(root);
        string Name(string kind) => "bap-" + kind + "-" + Guid.NewGuid().ToString("N");
        string[] Records() => Directory.GetFiles(root).Select(path => Path.GetFileName(path)).Where(name => name.StartsWith("bap-", StringComparison.Ordinal)
            || name.StartsWith("BetterAstralParty.update-v5.", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        using (var fence = new WindowsFileFence(root))
        {
            var first = UpdateFaults.Begin(fence, Selection, FaultPhase.Download);
            var own = File.ReadAllBytes(Path.Combine(root, UpdateFaults.OperationPath));
            string done = Name("operation-done"), ack = Name("fault-ack"), foreign = Name("operation-done"), garbage = Name("fault-ack"), other = "bap-operation-done-keep";
            File.WriteAllBytes(Path.Combine(root, done), own);
            File.WriteAllText(Path.Combine(root, ack), UpdateTrust.Hash(own) + "\n");
            File.WriteAllText(Path.Combine(root, foreign), Encoding.ASCII.GetString(own).Replace("root=" + fence.RootIdentity.Text + "\n", "root=00000000:0000000000000000\n"));
            File.WriteAllText(Path.Combine(root, garbage), "not a record");
            File.WriteAllBytes(Path.Combine(root, other), own);
            // Well-formed records whose values do not validate (release 0, an unparsable tag) stay without failing the cleanup.
            string zero = Name("fault-ack"), badTag = Name("operation-done"), text = Encoding.ASCII.GetString(own);
            File.WriteAllText(Path.Combine(root, zero), text.Replace("\nrelease=" + Selection.ReleaseId.ToString(CultureInfo.InvariantCulture) + "\n", "\nrelease=0\n"));
            File.WriteAllText(Path.Combine(root, badTag), text.Replace("\ntag=" + Selection.ReleaseTag + "\n", "\ntag=not-a-version\n"));
            Check(UpdateFaults.RemovableArchived(root) == 2, "previews count only the records removal will take");
            UpdateFaults.Complete(fence, first);
            Check(!File.Exists(Path.Combine(root, UpdateFaults.OperationPath)) && !File.Exists(Path.Combine(root, done)) && !File.Exists(Path.Combine(root, ack)),
                "completed operation and this root's archived records leave the game root");
            Check(Records().SequenceEqual(new[] { foreign, garbage, other, zero, badTag }.OrderBy(name => name, StringComparer.Ordinal)), "foreign, unreadable, invalid and differently named records stay");
            foreach (var name in new[] { foreign, garbage, other, zero, badTag }) File.Delete(Path.Combine(root, name));
            UpdateFaults.Begin(fence, Selection, FaultPhase.Download);
            UpdateFaults.Fail(fence, Selection, FaultPhase.Download, FaultReason.Network);
            UpdateFaults.SaveFailed(fence, UpdateFaults.Read(fence)!);
            Check(Records().Length == 3, "operation, failure latch and preferences marker are live");
            UpdateFaults.Acknowledge(fence);
            Check(Records().Length == 0 && UpdateFaults.Read(fence) == null, "an acknowledged fault removes its records instead of archiving them");
        }
        Console.WriteLine("Finished update records: none left in the game root; unverified leftovers kept.");
    }
    private static void Boundaries(string baseDirectory, Package package, byte[] inert)
    {
        var labels = new List<string>(); var root = Install(baseDirectory, "success", package, inert); var original = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
        Check(Apply(root, package, labels.Add) == TransactionResult.Committed, "successful transaction"); Verify(root, package, true, original);
        Check(Recover(root, package) == TransactionResult.Committed, "committed restart");
        Reject(() => Apply(root, package), "current installed version prevents replay");
        var commit = labels.IndexOf("after:commit-intent"); Check(commit >= 0, "commit boundary observed");
        for (var index = 0; index < InstallReceipt.Owned.Length + 2; index++)
            foreach (var phase in new[] { "before:install-", "after:install-" }) Check(labels.Contains(phase + index), "every owned/receipt/high-water mutation boundary observed");
        for (var i = 0; i < labels.Count; i++) {
            root = Install(baseDirectory, "f" + i.ToString("D2", CultureInfo.InvariantCulture), package, inert); original = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
            var hit = false; var label = labels[i];
            try { Apply(root, package, value => { if (value == label) { hit = true; throw new Interruption(); } }); throw new Exception("No interruption"); } catch (Interruption) { }
            Check(hit, "injected " + label); var result = Recover(root, package); var forward = i >= commit;
            Check(result == (i == 0 ? TransactionResult.Abandoned : forward ? TransactionResult.Committed : TransactionResult.RolledBack), "restart outcome " + label);
            Verify(root, package, forward, original); Check(Recover(root, package) == result, "restart idempotence " + label);
        }
        Console.WriteLine("Mutation boundaries injected: " + labels.Count + "; independent fence/plan reconstruction for every restart.");
    }
    private static void RecoveryFailures(string baseDirectory, Package package, byte[] inert)
    {
        // Record all rollback boundaries from a late pre-intent interruption, then interrupt each restart.
        var rollbackLabels = new List<string>(); var root = Install(baseDirectory, "rollback-labels", package, inert);
        try { Apply(root, package, label => { if (label == "before:commit-intent") throw new Interruption(); }); } catch (Interruption) { }
        Check(Recover(root, package, rollbackLabels.Add) == TransactionResult.RolledBack, "rollback reference");
        for (var i = 0; i < rollbackLabels.Count; i++) {
            root = Install(baseDirectory, "r" + i.ToString("D2", CultureInfo.InvariantCulture), package, inert); var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
            try { Apply(root, package, label => { if (label == "before:commit-intent") throw new Interruption(); }); } catch (Interruption) { }
            var hit = false;
            try { Recover(root, package, label => { if (label == rollbackLabels[i]) { hit = true; throw new Interruption(); } }); throw new Exception("Recovery interruption missing"); } catch (Interruption) { }
            Check(hit, "rollback boundary injected"); Check(Recover(root, package) == TransactionResult.RolledBack, "rollback resumes after interruption"); Verify(root, package, false, water);
        }
        Console.WriteLine("Rollback mutation boundaries injected: " + rollbackLabels.Count + ".");
        var forwardLabels = new List<string>(); root = Install(baseDirectory, "forward-labels", package, inert);
        try { Apply(root, package, label => { if (label == "after:commit-intent") throw new Interruption(); }); } catch (Interruption) { }
        Check(Recover(root, package, forwardLabels.Add) == TransactionResult.Committed, "forward recovery reference");
        for (var i = 0; i < forwardLabels.Count; i++) {
            root = Install(baseDirectory, "v" + i.ToString("D2", CultureInfo.InvariantCulture), package, inert); var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
            try { Apply(root, package, label => { if (label == "after:commit-intent") throw new Interruption(); }); } catch (Interruption) { }
            var hit = false;
            try { Recover(root, package, label => { if (label == forwardLabels[i]) { hit = true; throw new Interruption(); } }); throw new Exception("Forward interruption missing"); } catch (Interruption) { }
            Check(hit, "forward boundary injected"); Check(Recover(root, package) == TransactionResult.Committed, "forward restart resumes"); Verify(root, package, true, water);
        }
        Console.WriteLine("Forward recovery mutation boundaries injected: " + forwardLabels.Count + ".");
        root = Install(baseDirectory, "no-water", package, inert, false); Check(Apply(root, package) == TransactionResult.Committed, "initial watermark created last"); Verify(root, package, true, new byte[0]);
        root = Install(baseDirectory, "unknown", package, inert);
        try { Apply(root, package, label => { if (label == "after:install-0") throw new Interruption(); }); } catch (Interruption) { }
        File.WriteAllText(Path.Combine(root, InstallReceipt.Owned[0]), "unknown replacement");
        var before = File.ReadAllBytes(Path.Combine(root, InstallReceipt.Owned[0])); Reject(() => Recover(root, package), "unknown bytes block recovery");
        Check(UpdateTrust.Hash(before) == UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root, InstallReceipt.Owned[0]))), "unknown bytes not overwritten");
        root = Install(baseDirectory, "bad-receipt", package, inert); var text = Encoding.ASCII.GetString(package.OldReceipt).Replace("AstralParty-Vanilla.cmd", "user-settings.keep");
        File.WriteAllText(Path.Combine(root, InstallReceipt.Path), text); Reject(() => Apply(root, package), "receipt cannot authorize unowned path");
        Check(WorkDirectories(root, "txn").Length == 0 && File.ReadAllText(Path.Combine(root, "user-settings.keep")) == "untouched", "receipt reject before private work/mutations");
        Reject(() => InstallReceipt.Parse(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(package.OldReceipt).Replace("\"schema\":1", "\"schema\":1,\"schema\":1"))), "duplicate receipt key");
        root = Install(baseDirectory, "hash-mismatch", package, inert); File.WriteAllText(Path.Combine(root, "BetterAstralParty.compatibility.json"), "user modification");
        Reject(() => Apply(root, package), "receipt hash mismatch preserves modified owned file"); Check(File.ReadAllText(Path.Combine(root, "BetterAstralParty.compatibility.json")) == "user modification", "modified owned bytes retained");
        root = Install(baseDirectory, "no-trust", package, inert);
        using (var fence = new WindowsFileFence(root)) Reject(() => UpdateTransaction.Apply(fence, package.Descriptor, "transaction-fixture", package.Signature, package.Zip, new UpdateTrust(new TrustedUpdateKey[0]), Selection), "empty trust root");
        Check(!File.Exists(Path.Combine(root, "BetterAstralParty.update.lock")), "empty trust does not create operation lock");
        root = Install(baseDirectory, "no-water-rollback", package, inert, false);
        try { Apply(root, package, label => { if (label == "before:commit-intent") throw new Interruption(); }); } catch (Interruption) { }
        Check(Recover(root, package) == TransactionResult.RolledBack, "absent watermark rollback"); Verify(root, package, false, new byte[0]);
        root = Install(baseDirectory, "journal-path", package, inert);
        try { Apply(root, package, label => { if (label == "after:prepared") throw new Interruption(); }); } catch (Interruption) { }
        var journalPath = Path.Combine(root, Work(root), "plan"); File.WriteAllText(journalPath, File.ReadAllText(journalPath).Replace("file=AstralParty-Vanilla.cmd", "file=user-settings.keep"), new UTF8Encoding(false));
        Reject(() => Recover(root, package), "journal cannot choose an unrelated path"); Check(File.ReadAllText(Path.Combine(root, "user-settings.keep")) == "untouched", "journal unrelated target kept");
        root = Install(baseDirectory, "root-identity", package, inert);
        try { Apply(root, package, label => { if (label == "after:prepared") throw new Interruption(); }); } catch (Interruption) { }
        journalPath = Path.Combine(root, Work(root), "plan"); var journalText = File.ReadAllText(journalPath);
        using (var fence = new WindowsFileFence(root)) journalText = journalText.Replace("root=" + fence.RootIdentity.Text + "\n", "root=00000000:0000000000000000\n");
        File.WriteAllText(journalPath, journalText, new UTF8Encoding(false)); Reject(() => Recover(root, package), "journal installation root identity mismatch");
        root = Install(baseDirectory, "future-water", package, inert);
        var watermarkPath = Path.Combine(root, UpdateTransaction.HighWaterPath); var originalWatermark = File.ReadAllText(watermarkPath);
        var futureWatermark = originalWatermark.Replace("version=" + InstalledPluginMetadata.Read(package.Old[UpdateTrust.PluginPath]).Version + "\n", "version=1.3.1\n");
        Check(futureWatermark != originalWatermark && futureWatermark.Contains("version=1.3.1\n"), "future watermark negative control changes the actual installed baseline");
        File.WriteAllText(watermarkPath, futureWatermark, new UTF8Encoding(false));
        Reject(() => Apply(root, package), "highest-applied blocks an older signed release"); Check(WorkDirectories(root, "txn").Length == 0, "high-water reject before work creation");
        root = Install(baseDirectory, "occupied-absent", package, inert);
        try { Apply(root, package, label => { if (label == "before:install-" + Array.IndexOf(InstallReceipt.Owned, AbsentOwned)) File.WriteAllText(Path.Combine(root, AbsentOwned), "unexpected occupant"); }); throw new Exception("Occupant overwritten"); }
        catch (ApplySafetyException) { _checks++; }
        Reject(() => Recover(root, package), "unexpected occupant blocks recovery"); Check(File.ReadAllText(Path.Combine(root, AbsentOwned)) == "unexpected occupant", "originally absent target never overwrites occupant");
        root = Install(baseDirectory, "pending", package, inert);
        try { Apply(root, package, label => { if (label == "after:prepared") throw new Interruption(); }); } catch (Interruption) { }
        Reject(() => Apply(root, package), "pending journal blocks a second transaction"); Check(WorkDirectories(root, "txn").Length == 1, "pending journal not bypassed with new work");
        Check(Recover(root, package) == TransactionResult.RolledBack, "pending journal recovered"); Check(Apply(root, package) == TransactionResult.Committed, "resolved journal permits next transaction");
        root = Install(baseDirectory, "torn-marker", package, inert);
        try { Apply(root, package, label => { if (label == "after:commit-intent") throw new Interruption(); }); } catch (Interruption) { }
        File.WriteAllText(Path.Combine(root, Work(root), "commit-intent"), "partial");
        Reject(() => Recover(root, package), "partial commit marker cannot produce success or rollback");
        Reject(() => Apply(root, package), "invalid pending marker blocks another transaction");
    }
    private static void ChildRecovery(string baseDirectory, Package package, byte[] inert)
    {
        var key = Path.Combine(baseDirectory, "fixture-public-key.bin"); File.WriteAllBytes(key, package.PublicModulus);
        foreach (var forward in new[] { false, true }) {
            var root = Install(baseDirectory, forward ? "child-forward" : "child-rollback", package, inert); var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
            try { Apply(root, package, label => { if (label == (forward ? "after:commit-intent" : "before:commit-intent")) throw new Interruption(); }); } catch (Interruption) { }
            var assembly = typeof(TransactionFixture).Assembly.Location;
            var info = new ProcessStartInfo { FileName = assembly.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? assembly : Process.GetCurrentProcess().MainModule!.FileName,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.Arguments = (assembly.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "" : "\"" + assembly + "\" ") + "--recover \"" + root + "\" \"" + Work(root) + "\" \"" + key + "\" " + (forward ? "Committed" : "RolledBack");
            using (var process = Process.Start(info) ?? throw new Exception("Child recovery start failed")) {
                var output = process.StandardOutput.ReadToEnd(); var errors = process.StandardError.ReadToEnd();
                Check(process.WaitForExit(30000) && process.ExitCode == 0, "separate process recovery: " + output + errors);
            }
            Verify(root, package, forward, water);
        }
        Console.WriteLine("Separate-process recovery: rollback and forward commit passed; only fixture public key persisted, no private key.");
    }
    private static int Main(string[] args)
    {
        try {
            if (args.Length == 5 && args[0] == "--recover") {
                var childRoot = UpdaterFixtureScope.Require(args[1]);
                var publicKey = Path.GetFullPath(args[3]);
                if (publicKey != Path.Combine(Path.GetDirectoryName(childRoot)!, "fixture-public-key.bin") || args[4] != "Committed" && args[4] != "RolledBack") throw new Exception("Invalid isolated recovery arguments");
                var trust = new UpdateTrust(new[] { new TrustedUpdateKey("transaction-fixture", new RSAParameters { Modulus = File.ReadAllBytes(publicKey), Exponent = new byte[] { 1, 0, 1 } }) });
                using (var fence = new WindowsFileFence(childRoot)) { var result = UpdateTransaction.Recover(fence, args[2], trust, Selection); if (result.ToString() != args[4]) throw new Exception("Child outcome mismatch"); Console.WriteLine("CHILD PASS: " + result); }
                return 0;
            }
            var sharedOnly = args.Length == 5 && args[4] == "--shared-loader-only";
            if (args.Length != 5) throw new Exception("Fixture requires fresh root, old/new inert metadata DLLs, inert game EXE and exact compiled plugin metadata probe; --shared-loader-only may replace the probe");
            var directory = UpdaterFixtureScope.Require(args[0]);
            if (!directory.EndsWith("\\fixtures", StringComparison.OrdinalIgnoreCase) || Directory.Exists(directory)) throw new Exception("Fresh isolated fixture root required");
            var oldPlugin = File.ReadAllBytes(args[1]); var newPlugin = File.ReadAllBytes(args[2]); var inert = File.ReadAllBytes(args[3]); Directory.CreateDirectory(directory);
            Check(InstalledPluginMetadata.Read(oldPlugin).Version == "1.4.0-dev", "nonexecuting original metadata"); Check(InstalledPluginMetadata.Read(newPlugin).Version == "1.4.0", "nonexecuting replacement metadata");
            var malformed = (byte[])oldPlugin.Clone(); malformed[0] = 0; Reject(() => InstalledPluginMetadata.Read(malformed), "malformed PE");
            foreach (var length in new[] { 0, 64, oldPlugin.Length / 2 }) { var shortImage = new byte[length]; Buffer.BlockCopy(oldPlugin, 0, shortImage, 0, length); Reject(() => InstalledPluginMetadata.Read(shortImage), "truncated metadata"); }
            if (!sharedOnly) { var probe = InstalledPluginMetadata.Read(File.ReadAllBytes(args[4])); Check(probe.UpdateProtocol == 5 && probe.SettingsSchema == 1 && UpdateVersion.Parse(probe.Version).CompareTo(UpdateVersion.Parse("1.4.0-0")) >= 0, "actual compiled supported DLL metadata read only"); }
            using (var package = new Package(oldPlugin, newPlugin, inert)) {
                SharedLoaderPreservation(directory, package, inert);
                if (!sharedOnly) { ChannelMatrix(directory, package, inert); SameChannelPublic(directory, package, inert); Boundaries(directory, package, inert); InProcess(directory, package, inert); RecoveryFailures(directory, package, inert); ChildRecovery(directory, package, inert); FinishedRecords(directory); }
            }
            if (!sharedOnly) foreach (var metadata in new[] { new byte[] { 255 }, new byte[65537] }) {
                using var package = new Package(oldPlugin, newPlugin, inert, metadata);
                var root = Install(directory, "advisory-metadata-" + metadata.Length, package, inert);
                var water = File.ReadAllBytes(Path.Combine(root, UpdateTransaction.HighWaterPath));
                Check(Apply(root, package) == TransactionResult.Committed, "unreadable or oversized compatibility observations do not block an authenticated owned-file update");
                Verify(root, package, true, water);
            }
            Console.WriteLine(sharedOnly
                ? "PASS: shared-loader preservation " + _checks + " assertions; 18 isolated NTFS commit/rollback/forward cases with repeated recovery; no full boundary suite, production install, signing credentials or process termination."
                : "PASS: transaction " + _checks + " assertions; isolated NTFS install only; metadata/receipt/owned targets/identity, every observed rename/commit and rollback boundary, repeatable recovery; no production install, signing credentials or process termination."); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); if (error is ApplySafetyException safety) Console.Error.WriteLine("Fixture native error: " + safety.NativeError); return 1; }
    }
}
