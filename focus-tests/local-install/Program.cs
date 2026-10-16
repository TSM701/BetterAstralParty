using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

internal static class LocalInstallFixture
{
    private const string LocalVersion = "1.3.1-rc.6.local.1", PublishedVersion = "1.3.1-rc.7";
    private const string Root = "12345678:0000000000000001", KeyId = "fixture-only";
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Local install fixture: " + message); _checks++; Console.WriteLine("PASS: " + message); }
    private static void Reject(Action action, string message, UpdateFailure? failure = null)
    {
        try { action(); }
        catch (UpdateValidationException error) { Check(!failure.HasValue || error.Failure == failure.Value, message + " [" + error.Failure + "]"); return; }
        throw new Exception("Local install fixture accepted: " + message);
    }
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static byte[] Sign(RSA key, byte[] bytes) => key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    private static UpdateContext Local(string version = LocalVersion, long repositoryId = ReleaseFeedPolicy.BetaRepositoryId) =>
        UpdateContext.LocalInstall(ReleaseFeedPolicy.BetaRepository, "Beta", version, repositoryId);
    private static void RejectContext(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, message); return; }
        throw new Exception("Local install fixture accepted context: " + message);
    }
    private static void RejectWholeChannel(Action action, string message)
    {
        try { action(); }
        catch (ApplySafetyException error) { Check(error.Failure == ApplyFailure.RecoveryRequired, message); return; }
        catch (UpdateValidationException error) { Check(error.Failure == UpdateFailure.NotNewer, message); return; }
        throw new Exception("Local install fixture accepted whole channel: " + message);
    }
    private static UpdateContext Remote(string version = LocalVersion, string current = "1.3.1-rc.6", string? floor = null) =>
        new(ReleaseFeedPolicy.BetaRepository, "Beta", current, version, 123, 456, UpdateTrust.PlatformId, floor, ReleaseFeedPolicy.BetaRepositoryId);
    private static VerifiedUpdateDescriptor Verify(UpdateTrust trust, RSA key, byte[] bytes, UpdateContext? context = null) =>
        trust.VerifyLocalInstall(bytes, KeyId, Sign(key, bytes), context ?? Local());
    private static byte[] Receipt(VerifiedUpdateDescriptor descriptor)
        => InstallReceipt.Create(Root, descriptor, false);
    public static void Main()
    {
        // Ephemeral in-memory fixture key only; never opens the production key or writes package files.
        using var key = RSA.Create(3072);
        var trust = new UpdateTrust(new[] { new TrustedUpdateKey(KeyId, key.ExportParameters(false)) });
        var paths = GaPackageContract.OwnedPaths();
        var payload = paths.Select((_, index) => Bytes("inert owned payload " + index.ToString(CultureInfo.InvariantCulture) + "\n")).ToArray();
        var sizes = payload.Select(data => (long)data.Length).ToArray();
        var hashes = payload.Select(UpdateTrust.Hash).ToArray();
        var zip = GaPackageContract.PreviewUpdateZip(payload);
        var bytes = GaPackageContract.PrepareLocalInstallDescriptor(LocalVersion, "Beta", zip, sizes, hashes);
        var text = Encoding.UTF8.GetString(bytes);
        var descriptor = Verify(trust, key, bytes);
        Check(descriptor.Protocol == 4 && descriptor.Files.Count == paths.Length && descriptor.SettingsSchema == 1,
            "local signed proof retains the v4 native-owned/settings contract");
        Check(descriptor.ReleaseId == 0 && descriptor.AssetId == 0 && descriptor.RepositoryId == ReleaseFeedPolicy.BetaRepositoryId,
            "local proof has no fabricated remote IDs and retains the compiled repository pin");
        Check(UpdatePackage.VerifyPayload(zip, descriptor).Package.FileCount == paths.Length, "all current owned ZIP entries match the signed hashes");
        Check(UpdateVersion.Parse("1.3.1-rc.6").CompareTo(UpdateVersion.Parse(LocalVersion)) < 0
            && UpdateVersion.Parse(LocalVersion).CompareTo(UpdateVersion.Parse(PublishedVersion)) < 0,
            "published rc.6 < local candidate < future published rc.7");
        Check(UpdateVersion.Parse(LocalVersion).CompareTo(UpdateVersion.Parse("1.3.1-rc.6.local.2")) < 0,
            "successive local candidates increase monotonically");
        RejectContext(() => new UpdateContext(ReleaseFeedPolicy.BetaRepository, "Beta", "0.0.0", LocalVersion, 0, 0,
            UpdateTrust.PlatformId), "ordinary remote context constructor still rejects zero/zero IDs");
        RejectContext(() => new UpdateContext(ReleaseFeedPolicy.BetaRepository, "Beta", "0.0.0", LocalVersion, 0, 456,
            UpdateTrust.PlatformId), "ordinary remote context constructor rejects mixed local/remote IDs");

        Reject(() => trust.VerifyDescriptor(bytes, KeyId, Sign(key, bytes), Remote()), "remote update rejects a local proof", UpdateFailure.InvalidDescriptor);
        Reject(() => trust.VerifyProposal(bytes, KeyId, Sign(key, bytes), Remote()), "remote proposal rejects a local proof", UpdateFailure.InvalidDescriptor);
        Reject(() => UpdateTrust.Production().VerifyLocalInstall(bytes, KeyId, Sign(key, bytes), Local()),
            "fixture public key never enters production trust", UpdateFailure.InvalidSignature);
        var remoteBytes = GaPackageContract.PrepareUpdateDescriptor(PublishedVersion, "Beta", 123, 456, zip, sizes, hashes);
        Reject(() => Verify(trust, key, remoteBytes, Local(PublishedVersion)), "local verifier rejects a remote purpose/header", UpdateFailure.InvalidDescriptor);

        foreach (var change in new[] {
            ("BetterAstralParty.LocalInstallDescriptor/v1", "BetterAstralParty.LocalInstallDescriptor/v2", "unknown local schema"),
            ("purpose=local-install/Beta", "purpose=release/Beta", "cross-purpose local proof"),
            ("repository=" + ReleaseFeedPolicy.BetaRepository, "repository=someone/else", "repository display identity"),
            ("repository-id=" + ReleaseFeedPolicy.BetaRepositoryId.ToString(CultureInfo.InvariantCulture), "repository-id=1", "repository numeric identity"),
            ("platform=" + UpdateTrust.PlatformId, "platform=linux-x64", "platform"),
            ("release-id=0", "release-id=123", "nonzero local release ID"),
            ("asset-id=0", "asset-id=456", "nonzero local asset ID"),
            ("release-id=0", "release-id=00", "noncanonical zero ID"),
            ("settings-schema=1", "settings-schema=2", "unknown settings schema"),
            ("file-count=" + paths.Length, "file-count=" + (paths.Length - 1), "incomplete owned inventory"),
            ("file=" + paths[0], "file=../" + paths[0], "path traversal"),
            ("file=" + paths[0], "file=" + paths[1], "duplicate/colliding owned path"),
            ("\t" + sizes[0].ToString(CultureInfo.InvariantCulture) + "\t", "\t0\t", "zero owned file length"),
            ("zip-bytes=" + zip.Length.ToString(CultureInfo.InvariantCulture), "zip-bytes=67108865", "oversized ZIP declaration"),
            ("channel=Beta", "channel=Stable", "cross-channel identity"),
            ("asset-name=BetterAstralParty-", "asset-name=Other-", "asset filename binding")
        }) {
            var changed = text.Replace(change.Item1, change.Item2, StringComparison.Ordinal);
            Check(changed != text, "fixture mutation exercised: " + change.Item3);
            Reject(() => Verify(trust, key, Bytes(changed)), "reject signed " + change.Item3);
        }
        foreach (var changed in new[] { text.TrimEnd('\n'), text.Replace("\n", "\r\n", StringComparison.Ordinal), "\uFEFF" + text,
            text + "extra=value\n", text + "\0", text.Replace("purpose=local-install/Beta", "purpose=local-install/Beta\npurpose=local-install/Beta", StringComparison.Ordinal) })
            Reject(() => Verify(trust, key, Bytes(changed)), "reject malformed signed framing");
        Reject(() => Verify(trust, key, bytes, Remote()), "local verifier rejects a current-version remote context", UpdateFailure.WrongTarget);
        Reject(() => Verify(trust, key, bytes, Remote(current: "0.0.0")), "local context cannot claim remote IDs", UpdateFailure.WrongTarget);
        Reject(() => Verify(trust, key, bytes, Local(repositoryId: 1)), "local context requires the compiled numeric pin", UpdateFailure.WrongTarget);
        var signed = Sign(key, bytes); signed[0] ^= 1;
        Reject(() => trust.VerifyLocalInstall(bytes, KeyId, signed, Local()), "tampered signature rejected", UpdateFailure.InvalidSignature);
        Reject(() => trust.VerifyLocalInstall(Bytes(text + "\n"), KeyId, Sign(key, bytes), Local()), "tampered signed input rejected", UpdateFailure.InvalidSignature);

        var damagedZip = (byte[])zip.Clone(); damagedZip[damagedZip.Length / 2] ^= 1;
        Reject(() => UpdatePackage.VerifyPayload(damagedZip, descriptor), "changed archive bytes rejected", UpdateFailure.InvalidPackage);
        var wrongHashes = (string[])hashes.Clone(); wrongHashes[0] = new string('A', 64);
        var badPayloadProof = Verify(trust, key, GaPackageContract.PrepareLocalInstallDescriptor(LocalVersion, "Beta", zip, sizes, wrongHashes));
        Reject(() => UpdatePackage.VerifyPayload(zip, badPayloadProof), "signed but incorrect owned-file hash rejected", UpdateFailure.InvalidPackage);

        var initialBytes = Receipt(descriptor);
        var initial = InstallReceipt.Parse(initialBytes);
        foreach (var missing in new[] {
            Encoding.ASCII.GetString(initialBytes).Replace(",\"baseline\":\"1.3.0\"", "", StringComparison.Ordinal),
            Encoding.ASCII.GetString(initialBytes).Replace(",\"channelState\":\"" + initial.Channels!.Encoded + "\"", "", StringComparison.Ordinal)
        }) {
            try { InstallReceipt.Parse(Bytes(missing)); }
            catch (ApplySafetyException) { Check(true, "prior receipt without baseline/channel history rejected"); continue; }
            throw new Exception("Accepted pre-baseline receipt");
        }
        var localReceipt = InstallReceipt.Parse(initial.InitializeChannel(Root, descriptor));
        Check(localReceipt.LoaderBefore == false && localReceipt.Channels?.ActiveVersion == LocalVersion
            && localReceipt.Channels.BetaDescriptor == descriptor.DescriptorSha256 && localReceipt.Files.Count == paths.Length,
            "local install receipt parses with native-owned, loader baseline and channel history");
        Check(InstallReceipt.Parse(localReceipt.InitializeChannel(Root, descriptor)).Channels?.ActiveVersion == LocalVersion,
            "identical installed local proof remains idempotent");
        var changedSameVersion = Verify(trust, key, GaPackageContract.PrepareLocalInstallDescriptor(LocalVersion, "Beta", zip, sizes, wrongHashes));
        var changedReceipt = InstallReceipt.Parse(localReceipt.Updated(changedSameVersion));
        Reject(() => changedReceipt.InitializeChannel(Root, changedSameVersion), "same version with changed local proof cannot replace history", UpdateFailure.NotNewer);
        var published = trust.VerifyDescriptor(remoteBytes, KeyId, Sign(key, remoteBytes), Remote(PublishedVersion, LocalVersion, LocalVersion));
        Reject(() => trust.VerifyDescriptor(remoteBytes, KeyId, Sign(key, remoteBytes), Remote(PublishedVersion, LocalVersion, PublishedVersion)),
            "future published update cannot replay the applied published floor", UpdateFailure.NotNewer);
        var installedPublishedFiles = InstallReceipt.Parse(localReceipt.Updated(published));
        var publishedReceipt = InstallReceipt.Parse(installedPublishedFiles.InitializeChannel(Root, published));
        Check(publishedReceipt.LoaderBefore == false && publishedReceipt.Channels?.ActiveVersion == PublishedVersion
            && publishedReceipt.Channels.BetaDescriptor == published.DescriptorSha256 && publishedReceipt.Files.Count == paths.Length,
            "local receipt advances to published rc.7 without losing uninstall inventory or loader baseline");
        Reject(() => publishedReceipt.InitializeChannel(Root, descriptor), "published rc.7 receipt blocks local downgrade", UpdateFailure.NotNewer);

        var gaBytes = GaPackageContract.PrepareUpdateDescriptor("1.3.1", "Stable", 123, 456, zip, sizes, hashes);
        var gaText = Encoding.UTF8.GetString(gaBytes);
        UpdateContext GaContext(string version = "1.3.1", long repositoryId = ReleaseFeedPolicy.StableRepositoryId) =>
            new(ReleaseFeedPolicy.StableRepository, "Stable", "0.0.0", version, 123, 456, UpdateTrust.PlatformId, repositoryId: repositoryId);
        VerifiedUpdateDescriptor GaProof(byte[] input, UpdateContext? context = null) =>
            trust.VerifyProposal(input, KeyId, Sign(key, input), context ?? GaContext());
        var ga = GaProof(gaBytes);
        ReceiptChannelState BetaState(string version = LocalVersion, string root = Root, long betaId = ReleaseFeedPolicy.BetaRepositoryId,
            long stableId = 0, string? stableVersion = null) => new(root, "Beta", new string('a', 32), stableId,
                stableVersion, stableVersion == null ? "-" : new string('C', 64), betaId, version, descriptor.DescriptorSha256);
        void ValidateGa(ReceiptChannelState? state, string current = LocalVersion, int protocol = 4, int settings = 1,
            VerifiedUpdateDescriptor? incoming = null, string? high = "1.3.1-rc.4") =>
            WholeInstallBridge.ValidateWholeChannel(state, Root, current, protocol, settings, incoming ?? ga, high);
        Check(ReleaseFeedPolicy.StableRepositoryId == 1410226758 && ga.RepositoryId == ReleaseFeedPolicy.StableRepositoryId,
            "GA proof targets the independently pinned public repository, never the private Beta ID");
        foreach (var current in new[] { "1.3.1-rc.6", LocalVersion, PublishedVersion }) {
            ValidateGa(BetaState(current), current);
            Check(true, "first strictly newer GA whole accepts known Beta " + current);
        }
        var beforeChannels = localReceipt.Channels!.Text();
        ValidateGa(localReceipt.Channels);
        var gaReceipt = InstallReceipt.Parse(InstallReceipt.Parse(localReceipt.Updated(ga)).InitializeChannel(Root, ga));
        Check(gaReceipt.Channels?.ActiveChannel == "Stable" && gaReceipt.Channels.ActiveVersion == "1.3.1"
            && gaReceipt.Channels.StableRepositoryId == ReleaseFeedPolicy.StableRepositoryId
            && gaReceipt.Channels.StableDescriptor == ga.DescriptorSha256
            && gaReceipt.Channels.BetaVersion == LocalVersion && gaReceipt.Channels.BetaDescriptor == descriptor.DescriptorSha256
            && gaReceipt.LoaderBefore == false && gaReceipt.Files.Count == paths.Length && localReceipt.Channels.Text() == beforeChannels,
            "first GA conversion preserves the Beta floor/evidence, native-owned and original loader baseline");
        ValidateGa(BetaState(stableId: ReleaseFeedPolicy.StableRepositoryId, stableVersion: "1.3.0"));
        Check(true, "newer Stable whole may revisit an already initialized Stable channel");
        RejectWholeChannel(() => WholeInstallBridge.ValidateWholeChannel(null, Root, LocalVersion, 1, 0, descriptor, null), "legacy source without channel history is unsupported");
        RejectWholeChannel(() => WholeInstallBridge.ValidateWholeChannel(localReceipt.Channels, Root, LocalVersion, 1, 0, descriptor, null), "legacy source protocol is unsupported even on the same channel");
        ValidateGa(gaReceipt.Channels, "1.3.1");
        Check(true, "baseline same-channel Stable idempotence remains supported");

        foreach (var rejection in new (Action Action, string Name)[] {
            (() => ValidateGa(null), "existing receipt without channel state cannot adopt Stable"),
            (() => ValidateGa(BetaState(root: "FFFFFFFF:0000000000000001")), "wrong source root"),
            (() => ValidateGa(BetaState(), current: "1.3.1-rc.6"), "changed source version"),
            (() => ValidateGa(BetaState(betaId: 42)), "wrong source Beta repository ID"),
            (() => ValidateGa(BetaState(), protocol: 1), "unsupported current update protocol"),
            (() => ValidateGa(BetaState(), settings: 2), "unsupported current settings schema"),
            (() => ValidateGa(BetaState("1.3.1"), current: "1.3.1"), "same-version cross-channel replay"),
            (() => ValidateGa(BetaState("1.3.2-rc.1"), current: "1.3.2-rc.1"), "older GA cannot lower installed version"),
            (() => ValidateGa(BetaState(), high: "1.3.1"), "GA must exceed equal retained global floor"),
            (() => ValidateGa(BetaState(), high: "1.3.2"), "GA cannot lower retained global floor")
        }) RejectWholeChannel(rejection.Action, rejection.Name);
        Reject(() => GaProof(Bytes(gaText.Replace("repository-id=" + ReleaseFeedPolicy.StableRepositoryId,
            "repository-id=42", StringComparison.Ordinal)), GaContext(repositoryId: 42)),
            "public signed scope rejects a wrong target repository ID before whole transition", UpdateFailure.WrongTarget);
        var localGaBytes = GaPackageContract.PrepareLocalInstallDescriptor("1.3.1", "Stable", zip, sizes, hashes);
        var localGa = trust.VerifyLocalInstall(localGaBytes, KeyId, Sign(key, localGaBytes),
            UpdateContext.LocalInstall(ReleaseFeedPolicy.StableRepository, "Stable", "1.3.1", ReleaseFeedPolicy.StableRepositoryId));
        ValidateGa(BetaState(), incoming: localGa);
        Check(true, "verified local-purpose zero-ID proof may advance a manual Beta-to-Stable whole install");
        foreach (var ids in new[] { (0L, 456L), (123L, 0L) })
            RejectContext(() => new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "0.0.0", "1.3.1",
                ids.Item1, ids.Item2, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId), "GA context rejects a zero remote ID");
        var legacyGaBytes = Bytes(string.Join("\n", gaText.Replace("UpdateDescriptor/v4", "UpdateDescriptor/v1", StringComparison.Ordinal)
            .Split('\n').Where(line => !line.StartsWith("purpose=", StringComparison.Ordinal)
                && !line.StartsWith("repository-id=", StringComparison.Ordinal) && !line.StartsWith("settings-schema=", StringComparison.Ordinal))));
        Reject(() => GaProof(legacyGaBytes), "v1 incoming proof is unsupported", UpdateFailure.InvalidDescriptor);
        foreach (var mutation in new[] {
            ("repository=" + ReleaseFeedPolicy.StableRepository, "repository=someone/else", "wrong GA repository slug"),
            ("purpose=release/Public", "purpose=local-install/Stable", "wrong GA signed purpose"),
            ("settings-schema=1", "settings-schema=2", "unknown incoming GA settings schema"),
            ("file-count=" + paths.Length, "file-count=" + (paths.Length - 1), "incomplete GA owned inventory")
        }) Reject(() => GaProof(Bytes(gaText.Replace(mutation.Item1, mutation.Item2, StringComparison.Ordinal))), mutation.Item3);
        Reject(() => GaProof(Bytes(gaText.Replace("1.3.1", "1.3.1-rc.8", StringComparison.Ordinal)), GaContext("1.3.1-rc.8")),
            "prerelease proof cannot enter Stable GA conversion", UpdateFailure.WrongTarget);

        const string stableLocalVersion = "1.3.2-rc.1.local.1";
        UpdateContext StableLocalContext(string version = stableLocalVersion, string repository = ReleaseFeedPolicy.StableRepository,
            string channel = "Stable", long repositoryId = ReleaseFeedPolicy.StableRepositoryId) =>
            UpdateContext.LocalInstall(repository, channel, version, repositoryId);
        VerifiedUpdateDescriptor StableLocalProof(byte[] input, UpdateContext? context = null) =>
            trust.VerifyLocalInstall(input, KeyId, Sign(key, input), context ?? StableLocalContext());
        var stableLocalBytes = GaPackageContract.PrepareLocalInstallDescriptor(stableLocalVersion, "Stable", zip, sizes, hashes);
        var stableLocalText = Encoding.UTF8.GetString(stableLocalBytes);
        var stableLocal = StableLocalProof(stableLocalBytes);
        Check(stableLocal.Protocol == 4 && stableLocal.SettingsSchema == 1 && stableLocal.Files.Count == paths.Length
            && stableLocal.Channel == "Stable" && stableLocal.Repository == ReleaseFeedPolicy.StableRepository
            && stableLocal.RepositoryId == ReleaseFeedPolicy.StableRepositoryId && stableLocal.ReleaseId == 0 && stableLocal.AssetId == 0
            && stableLocalText.Contains("\npurpose=local-install/Stable\n", StringComparison.Ordinal),
            "Stable local prerelease uses the exact local-install purpose, public repository pin and zero remote IDs");
        Check(UpdatePackage.VerifyPayload(zip, stableLocal).Package.FileCount == paths.Length
            && UpdateVersion.Parse("1.3.1").CompareTo(UpdateVersion.Parse(stableLocalVersion)) < 0
            && UpdateVersion.Parse(stableLocalVersion).CompareTo(UpdateVersion.Parse("1.3.2")) < 0,
            "Stable local payload verifies between installed GA and its future public hotfix");
        WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 1, stableLocal, "1.3.1");
        var stableLocalReceipt = InstallReceipt.Parse(InstallReceipt.Parse(gaReceipt.Updated(stableLocal)).InitializeChannel(Root, stableLocal));
        Check(stableLocalReceipt.Channels?.ActiveChannel == "Stable" && stableLocalReceipt.Channels.ActiveVersion == stableLocalVersion
            && stableLocalReceipt.Channels.StableRepositoryId == ReleaseFeedPolicy.StableRepositoryId
            && stableLocalReceipt.Channels.StableDescriptor == stableLocal.DescriptorSha256
            && stableLocalReceipt.Channels.BetaVersion == gaReceipt.Channels!.BetaVersion
            && stableLocalReceipt.Channels.BetaDescriptor == gaReceipt.Channels.BetaDescriptor
            && stableLocalReceipt.LoaderBefore == false && stableLocalReceipt.Files.Count == paths.Length,
            "same-channel Stable local install preserves Beta history, native-owned and the original loader baseline");
        WholeInstallBridge.ValidateWholeChannel(stableLocalReceipt.Channels, Root, stableLocalVersion, 4, 1, stableLocal, stableLocalVersion);
        Check(InstallReceipt.Parse(stableLocalReceipt.InitializeChannel(Root, stableLocal)).Channels?.ActiveVersion == stableLocalVersion,
            "identical Stable local proof remains idempotent in whole-channel and receipt checks");
        ValidateGa(BetaState(), incoming: stableLocal);
        Check(true, "verified Stable local prerelease may advance a manual Beta-to-Stable whole install");

        const string nextBetaVersion = "1.4.0-rc.1";
        var nextBetaBytes = GaPackageContract.PrepareUpdateDescriptor(nextBetaVersion, "Beta", 123, 456, zip, sizes, hashes);
        var nextBetaText = Encoding.UTF8.GetString(nextBetaBytes);
        UpdateContext BetaContext(string version = nextBetaVersion, long repositoryId = ReleaseFeedPolicy.BetaRepositoryId) =>
            new(ReleaseFeedPolicy.BetaRepository, "Beta", "0.0.0", version, 123, 456, UpdateTrust.PlatformId, repositoryId: repositoryId);
        VerifiedUpdateDescriptor BetaProof(byte[] input, UpdateContext? context = null) =>
            trust.VerifyProposal(input, KeyId, Sign(key, input), context ?? BetaContext());
        var nextBeta = BetaProof(nextBetaBytes);
        WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 1, nextBeta, "1.3.1");
        var stableHistory = gaReceipt.Channels!.Text();
        var nextBetaReceipt = InstallReceipt.Parse(InstallReceipt.Parse(gaReceipt.Updated(nextBeta)).InitializeChannel(Root, nextBeta));
        Check(nextBetaReceipt.Channels?.ActiveChannel == "Beta" && nextBetaReceipt.Channels.ActiveVersion == nextBetaVersion
            && nextBetaReceipt.Channels.BetaRepositoryId == ReleaseFeedPolicy.BetaRepositoryId
            && nextBetaReceipt.Channels.BetaDescriptor == nextBeta.DescriptorSha256
            && nextBetaReceipt.Channels.StableVersion == gaReceipt.Channels.StableVersion
            && nextBetaReceipt.Channels.StableDescriptor == gaReceipt.Channels.StableDescriptor
            && nextBetaReceipt.Channels.StableRepositoryId == gaReceipt.Channels.StableRepositoryId
            && nextBetaReceipt.LoaderBefore == false && nextBetaReceipt.Files.Count == paths.Length && gaReceipt.Channels.Text() == stableHistory,
            "newer signed Beta whole preserves Stable history, native-owned and the original loader baseline");
        var nextGaBytes = GaPackageContract.PrepareUpdateDescriptor("1.4.0", "Stable", 123, 456, zip, sizes, hashes);
        var nextGa = GaProof(nextGaBytes, GaContext("1.4.0"));
        WholeInstallBridge.ValidateWholeChannel(nextBetaReceipt.Channels, Root, nextBetaVersion, 4, 1, nextGa, nextBetaVersion);
        var roundTrip = InstallReceipt.Parse(InstallReceipt.Parse(nextBetaReceipt.Updated(nextGa)).InitializeChannel(Root, nextGa));
        Check(roundTrip.Channels?.ActiveChannel == "Stable" && roundTrip.Channels.ActiveVersion == "1.4.0"
            && roundTrip.Channels.StableDescriptor == nextGa.DescriptorSha256
            && roundTrip.Channels.BetaVersion == nextBetaVersion && roundTrip.Channels.BetaDescriptor == nextBeta.DescriptorSha256
            && roundTrip.LoaderBefore == false && roundTrip.Files.Count == paths.Length,
            "manual Stable-to-Beta-to-Stable round trip advances both histories without resetting the baseline");
        var betaLocalBytes = GaPackageContract.PrepareLocalInstallDescriptor("1.4.0-rc.1.local.1", "Beta", zip, sizes, hashes);
        var betaLocal = Verify(trust, key, betaLocalBytes, Local("1.4.0-rc.1.local.1"));
        WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 1, betaLocal, "1.3.1");
        Check(true, "verified local Beta zero-ID proof may advance a manual Stable-to-Beta whole install");
        foreach (var rejection in new (Action Action, string Name)[] {
            (() => WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 1, 1, nextBeta, "1.3.1"), "Stable-to-Beta rejects unsupported current protocol"),
            (() => WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 2, nextBeta, "1.3.1"), "Stable-to-Beta rejects unsupported current settings"),
            (() => WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 1, nextBeta, nextBetaVersion), "Stable-to-Beta rejects equal global floor"),
            (() => WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 1, nextBeta, "1.4.0"), "Stable-to-Beta rejects higher global floor")
        }) RejectWholeChannel(rejection.Action, rejection.Name);
        foreach (var version in new[] { "1.3.1", "1.3.1-rc.8" }) {
            var input = GaPackageContract.PrepareUpdateDescriptor(version, "Beta", 123, 456, zip, sizes, hashes);
            var proof = BetaProof(input, BetaContext(version));
            RejectWholeChannel(() => WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 1, proof, null),
                "Stable-to-Beta rejects non-increasing target " + version);
        }
        var wrongBetaRepo = BetaProof(Bytes(nextBetaText.Replace("repository-id=" + ReleaseFeedPolicy.BetaRepositoryId,
            "repository-id=42", StringComparison.Ordinal)), BetaContext(repositoryId: 42));
        RejectWholeChannel(() => WholeInstallBridge.ValidateWholeChannel(gaReceipt.Channels, Root, "1.3.1", 4, 1, wrongBetaRepo, null),
            "Stable-to-Beta rejects a fixture-signed wrong target repository ID");
        var legacyBetaBytes = Bytes(string.Join("\n", nextBetaText.Replace("UpdateDescriptor/v4", "UpdateDescriptor/v1", StringComparison.Ordinal)
            .Split('\n').Where(line => !line.StartsWith("purpose=", StringComparison.Ordinal)
                && !line.StartsWith("repository-id=", StringComparison.Ordinal) && !line.StartsWith("settings-schema=", StringComparison.Ordinal))));
        Reject(() => BetaProof(legacyBetaBytes), "legacy incoming Beta proof is unsupported", UpdateFailure.InvalidDescriptor);
        foreach (var mutation in new[] {
            ("repository=" + ReleaseFeedPolicy.BetaRepository, "repository=someone/else", "wrong Beta repository slug"),
            ("purpose=release/Beta", "purpose=local-install/Beta", "wrong Beta signed purpose"),
            ("settings-schema=1", "settings-schema=2", "unknown incoming Beta settings schema"),
            ("file-count=" + paths.Length, "file-count=" + (paths.Length - 1), "incomplete Beta owned inventory")
        }) Reject(() => BetaProof(Bytes(nextBetaText.Replace(mutation.Item1, mutation.Item2, StringComparison.Ordinal))), mutation.Item3);
        foreach (var version in new[] { nextBetaVersion, "1.4.0-rc.2" }) {
            var retained = new ReceiptChannelState(Root, "Stable", new string('b', 32), ReleaseFeedPolicy.StableRepositoryId,
                "1.3.1", ga.DescriptorSha256, ReleaseFeedPolicy.BetaRepositoryId, version, nextBeta.DescriptorSha256);
            WholeInstallBridge.ValidateWholeChannel(retained, Root, "1.3.1", 4, 1, nextBeta, null);
            var receipt = InstallReceipt.Parse(gaReceipt.Updated(ga, retained));
            Reject(() => InstallReceipt.Parse(receipt.Updated(nextBeta)).InitializeChannel(Root, nextBeta),
                "manual channel switch cannot replay or lower the retained Beta floor " + version, UpdateFailure.NotNewer);
        }
        foreach (var version in new[] { "1.4.0", "1.5.0" }) {
            var retained = new ReceiptChannelState(Root, "Beta", new string('c', 32), ReleaseFeedPolicy.StableRepositoryId,
                version, nextGa.DescriptorSha256, ReleaseFeedPolicy.BetaRepositoryId, nextBetaVersion, nextBeta.DescriptorSha256);
            WholeInstallBridge.ValidateWholeChannel(retained, Root, nextBetaVersion, 4, 1, nextGa, null);
            var receipt = InstallReceipt.Parse(nextBetaReceipt.Updated(nextBeta, retained));
            Reject(() => InstallReceipt.Parse(receipt.Updated(nextGa)).InitializeChannel(Root, nextGa),
                "manual channel switch cannot replay or lower the retained Stable floor " + version, UpdateFailure.NotNewer);
        }

        var stableRemoteContext = new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "1.3.1", stableLocalVersion,
            123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
        Reject(() => trust.VerifyDescriptor(stableLocalBytes, KeyId, Sign(key, stableLocalBytes), stableRemoteContext),
            "remote Stable update rejects a Stable local-purpose proof", UpdateFailure.InvalidDescriptor);
        Reject(() => trust.VerifyProposal(stableLocalBytes, KeyId, Sign(key, stableLocalBytes), stableRemoteContext),
            "remote Stable proposal rejects a Stable local-purpose proof", UpdateFailure.InvalidDescriptor);
        RejectContext(() => GaPackageContract.PrepareUpdateDescriptor(stableLocalVersion, "Stable", 123, 456, zip, sizes, hashes),
            "remote Stable builder still rejects a local-looking prerelease");
        var remotePrerelease = stableLocalText.Replace("BetterAstralParty.LocalInstallDescriptor/v1", "BetterAstralParty.UpdateDescriptor/v4", StringComparison.Ordinal)
            .Replace("purpose=local-install/Stable", "purpose=release/Stable", StringComparison.Ordinal)
            .Replace("release-id=0", "release-id=123", StringComparison.Ordinal).Replace("asset-id=0", "asset-id=456", StringComparison.Ordinal);
        Reject(() => GaProof(Bytes(remotePrerelease), GaContext(stableLocalVersion)),
            "remote Stable verifier rejects a prerelease even with a canonical local suffix", UpdateFailure.WrongTarget);
        foreach (var version in new[] { "1.3.2-rc.1", "1.3.2-rc.1.local.0", "1.3.2-rc.1.local.01",
            "1.3.2-rc.1+test.local.1", "1.3.2-rc.1.local.1+build" }) {
            try { GaPackageContract.PrepareLocalInstallDescriptor(version, "Stable", zip, sizes, hashes); }
            catch (Exception error) when (error is ArgumentException or UpdateValidationException) {
                Check(true, "Stable local builder rejects noncanonical local version " + version);
                Reject(() => StableLocalProof(Bytes(stableLocalText.Replace(stableLocalVersion, version, StringComparison.Ordinal)), StableLocalContext(version)),
                    "signed Stable local proof rejects noncanonical local version " + version);
                continue;
            }
            throw new Exception("Local install fixture accepted Stable local version: " + version);
        }
        foreach (var mutation in new[] {
            ("purpose=local-install/Stable", "purpose=local-install/Beta", "wrong Stable local purpose"),
            ("channel=Stable", "channel=Beta", "wrong Stable local channel"),
            ("repository=" + ReleaseFeedPolicy.StableRepository, "repository=" + ReleaseFeedPolicy.BetaRepository, "wrong Stable local repository slug"),
            ("repository-id=" + ReleaseFeedPolicy.StableRepositoryId, "repository-id=" + ReleaseFeedPolicy.BetaRepositoryId, "wrong Stable local repository ID"),
            ("release-id=0", "release-id=123", "nonzero Stable local release ID"),
            ("asset-id=0", "asset-id=456", "nonzero Stable local asset ID")
        }) {
            var changed = stableLocalText.Replace(mutation.Item1, mutation.Item2, StringComparison.Ordinal);
            Check(changed != stableLocalText, "Stable fixture mutation exercised: " + mutation.Item3);
            Reject(() => StableLocalProof(Bytes(changed)), mutation.Item3);
        }
        Reject(() => StableLocalProof(stableLocalBytes, StableLocalContext(repository: ReleaseFeedPolicy.BetaRepository)),
            "Stable local context rejects the Beta repository slug", UpdateFailure.WrongTarget);
        Reject(() => StableLocalProof(stableLocalBytes, StableLocalContext(channel: "Beta")),
            "Stable local context rejects a cross-channel binding", UpdateFailure.WrongTarget);
        Reject(() => StableLocalProof(stableLocalBytes, StableLocalContext(repositoryId: ReleaseFeedPolicy.BetaRepositoryId)),
            "Stable local context rejects the Beta repository pin", UpdateFailure.WrongTarget);
        Console.WriteLine("PASS: " + _checks.ToString(CultureInfo.InvariantCulture) + " offline local install contract checks; no production key, HTTP, installation or removal.");
    }
}
