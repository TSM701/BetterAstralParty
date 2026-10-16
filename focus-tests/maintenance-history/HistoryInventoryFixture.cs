using BetterAstralParty.Updating;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;

// Inventory tests do not execute installers, helpers, games or Steam. Transactions/
// intents are structural fixtures, not proof that a signed update was applied.
// Helper provenance is separately verified with an ephemeral fixture RSA key.
internal static class HistoryInventoryFixture
{
    private enum Kind { Transaction, Helper, AutomaticHelper, WholeBridge, WholeIntent }
    private sealed record Case(string Name, bool Passed, string? Failure = null, long ElapsedMilliseconds = 0);
    private sealed record HelperRecord(string Work, string WorkId, string ImageId, string ReceiptId, byte[] Receipt);
    private sealed record HandoffRecord(UpdateTicket Ticket, byte[] Bytes, string? AttemptWork);
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string Digest = new('A', 64);
    private static readonly List<Case> Cases = new();
    private static string FixtureRoot = "";
    private static int Sequence, Assertions;
    private static bool RetainedFlat;
    private static RSA Key = null!;
    private static UpdateTrust Trust = null!;
    private static byte[] Image = null!, Descriptor = null!, Signature = null!;
    private static byte[] PublicDescriptor = null!, PublicSignature = null!;
    private static DetachedSignature PublicEnvelope = null!;
    private static UpdateContext PublicContext = null!;
    private static string PublicProofRoot = "", PublicDescriptorHash = "";
    private static byte[] CancelDescriptor = null!, CancelSignature = null!;
    private static byte[] CancelPackage = null!;
    private static UpdateContext CancelContext = null!;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || !OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows fixture root, artifact root and readonly inert remote proof root required.");
            FixtureRoot = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
            var artifacts = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
            if (!FixtureRoot.StartsWith(artifacts + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(FixtureRoot) || FixtureRoot.Length > 135)
                throw new InvalidOperationException("Fresh, short, isolated artifact descendant required.");
            Directory.CreateDirectory(FixtureRoot);
            PublicProofRoot = Path.GetFullPath(args[2]).TrimEnd(Path.DirectorySeparatorChar);
            if (!PublicProofRoot.StartsWith(Path.GetDirectoryName(artifacts)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || PublicProofRoot.StartsWith(FixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Public proof must be a readonly selected-project path outside the fresh fixture.");
            PublicDescriptor = File.ReadAllBytes(Path.Combine(PublicProofRoot, "update.descriptor"));
            PublicSignature = File.ReadAllBytes(Path.Combine(PublicProofRoot, "update.signature"));
            if (PublicDescriptor.Length > UpdateTrust.MaxDescriptorBytes || PublicSignature.Length > 1024) throw new InvalidOperationException("Public proof bounds exceeded.");
            var publicLines = Encoding.ASCII.GetString(PublicDescriptor).Split('\n');
            if (publicLines.Length < 15 || publicLines[0] != "BetterAstralParty.UpdateDescriptor/v4") throw new InvalidOperationException("A signed baseline RemoteUpdate proof, not a LocalInstall or legacy proof, is required.");
            PublicEnvelope = DetachedSignature.Parse(PublicSignature);
            PublicContext = new UpdateContext(publicLines[1][11..], publicLines[3][8..], "1.3.0-0", publicLines[2][8..],
                long.Parse(publicLines[5][11..], CultureInfo.InvariantCulture), long.Parse(publicLines[6][9..], CultureInfo.InvariantCulture), UpdateTrust.PlatformId,
                repositoryId: long.Parse(publicLines[12][14..], CultureInfo.InvariantCulture));
            var publicVerified = UpdateTrust.Production().VerifyDescriptor(PublicDescriptor, PublicEnvelope.KeyId, PublicEnvelope.Signature, PublicContext);
            PublicDescriptorHash = publicVerified.DescriptorSha256;
            using var key = RSA.Create(3072); Key = key;
            Trust = new UpdateTrust(new[] { new TrustedUpdateKey("maintenance-history-fixture", Key.ExportParameters(false)) });
            Image = new byte[512]; Image[0] = 77; Image[1] = 90;
            BitConverter.GetBytes(128).CopyTo(Image, 60); Image[128] = 80; Image[129] = 69;
            BitConverter.GetBytes((ushort)0x8664).CopyTo(Image, 132);
            Descriptor = HelperDescriptor.Prepare("1.3.0", Image, 4);
            Signature = new DetachedSignature("maintenance-history-fixture", Key.SignData(Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)).Bytes();
            // Fail fast if the fixture's ephemeral signature ever diverges from the
            // product verifier, instead of misreporting that setup error as a regression.
            HelperDescriptor.Verify(Trust, Descriptor, DetachedSignature.Parse(Signature), Image);
            foreach (var kind in Enum.GetValues<Kind>())
            {
                CheckCase(kind + ":retained-completed-over-old-limit", () => CompletedInventory(kind));
                CheckCase(kind + ":flat-completed-preserved-and-pending-actionable", () => FlatHistory(kind));
                CheckCase(kind + ":terminal-marker-tamper-rejected", () => MarkerTamper(kind));
                CheckCase(kind + ":completed-native-root-mismatch-rejected", () => WrongRoot(kind));
            }
            CheckCase("Transaction:mixed-completed-and-pending", MixedTransaction);
            CheckCase("Helper:mixed-completed-and-pending", MixedHelper);
            CheckCase("Transaction:257-completed-payload-stages-and-exact-active-handoff", RetainedStageDiscovery);
            foreach (var kind in new[] { Kind.Transaction, Kind.Helper, Kind.AutomaticHelper })
                CheckCase(kind + ":pre-journal-empty-active-over-limit-rejected", () => EmptyActive(kind));
            CheckCase("Transaction:committed-and-rolled-back-conflict-rejected", TransactionConflict);
            CheckCase("WholeIntent:committed-and-cancelled-conflict-rejected", WholeConflict);
            CheckCase("Helper:retained-65-live-and-removed-signed-provenance", HelperProvenance);
            foreach (var terminal in new[] { "Committed", "Recovered", "Cancelled", "FaultDisabled", "FaultDisabledNoChanges" })
                CheckCase("SignedTerminal:" + terminal + ":exact-proof-retired", () => SignedTerminalPositive(terminal));
            foreach (var fault in new[] { "result-hash", "attempt-binding", "signature", "descriptor-binding", "unfinished-attempt", "resolution", "missing-resolution" })
                CheckCase("SignedTerminal:" + fault + ":not-retired", () => SignedTerminalNegative(fault));
            CheckCase("SignedTerminal:257-retained-mixed-terminal-handoffs-and-one-active", SignedTerminalDiscovery);
            PrepareCancellationProof();
            // Protocol-3 published binary parity is retired at the explicit 1.3.0 protocol-4 support reset.
            CheckCase("CancelledBeforeQueue:durable-and-idempotent", CancelledBeforeQueue);
            CheckCase("CancelledBeforeQueue:80-authenticated-packages-cleaned", CancelledPackages);
            CheckCase("CancelledBeforeQueue:257-retired-and-one-pending", CancelledDiscovery);
            foreach (var blocker in new[] { "claim", "attempt", "owner", "operation", "busy-ticket", "busy-lock", "ticket", "signature", "descriptor", "cancel-hash" })
                CheckCase("CancelledBeforeQueue:" + blocker + ":preserved", () => CancellationBlocked(blocker));
            foreach (var terminal in new[] { "ManualUpgradeRequired", "FilesBusy", "AlreadyClaimed", "Abandoned" })
                CheckCase("TerminalRetention:" + terminal + ":authenticated-package-cleaned", () => NoChangeTerminalCleanup(terminal));
            CheckCase("TerminalRetention:unresolved-recovery-required-preserved", UnresolvedRetention);
            var passed = Cases.All(test => test.Passed);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Passed = passed, Assertions, Cases, Runtime = Environment.Version.ToString(), FixtureRoot,
                NoGameOrSteamExecution = true, NoNetworkRequests = true, EphemeralFixtureKeyOnly = true,
                StructuralInventoryNotSignedUpdateExecution = true,
                ExistingEvidencePathsPreserved = true,
                ActualPublishedBaselineBinaryChecked = false,
                Retired = "Pre-1.3.0 protocol-3 compiled consumer compatibility; immutable archives remain unchanged",
                PublicRemoteProof = new { Path = PublicProofRoot, DescriptorSha256 = PublicDescriptorHash, SignatureSha256 = UpdateTrust.Hash(PublicSignature), PublicContext.Repository, PublicContext.RepositoryId, PublicContext.ReleaseTag, PublicContext.ReleaseId, PublicContext.AssetId, KeyId = PublicEnvelope.KeyId },
                Scope = "Canonical inventory/native ID/terminal marker tests; separately ephemeral-signed helper provenance and protocol4 RemoteUpdate terminal classification. Default remote proof uses a compiled fixture-only public pin; an explicit public proof uses unchanged product pins. Terminal journals/tickets are structural simulations, not successful signed update executions. No helper/installer process execution, live game root, operating signing key or network."
            }));
            return passed ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static void Check(bool value, string message)
    { Assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void CheckCase(string name, Action body)
    {
        var timer = Stopwatch.StartNew();
        try { body(); Cases.Add(new Case(name, true, ElapsedMilliseconds: timer.ElapsedMilliseconds)); }
        catch (Exception error) { Cases.Add(new Case(name, false, error.GetType().Name + ": " + error.Message, timer.ElapsedMilliseconds)); }
        Console.WriteLine((Cases[^1].Passed ? "PASS " : "FAIL ") + Cases.Count + ": " + name + " (" + timer.ElapsedMilliseconds + " ms)");
    }
    private static WindowsFileFence Fresh()
    {
        var root = Path.Combine(FixtureRoot, (++Sequence).ToString("D2")); Directory.CreateDirectory(root);
        return new WindowsFileFence(root);
    }
    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);
    private static void Write(WindowsFileFence fence, string path, byte[] bytes)
    { using var file = fence.OpenFile(path, true, true); file.Stream.Write(bytes); file.Flush(); }
    private static void Replace(WindowsFileFence fence, string path, byte[] bytes)
    { using var file = fence.OpenFile(path, true); file.Stream.SetLength(0); file.Stream.Write(bytes); file.Flush(); }
    private static string Id(WindowsFileFence fence, string path)
    { using var file = fence.OpenFile(path); return file.Identity.Text; }
    private static byte[] Read(WindowsFileFence fence, string path)
    { using var file = fence.OpenFile(path); return UpdateTicket.Read(file, 16384); }
    private static string Private(WindowsFileFence fence, string prefix)
    { var path = (RetainedFlat ? "" : UpdateWorkPath.Root + "/") + prefix + Guid.NewGuid().ToString("N"); if (RetainedFlat) fence.CreatePrivateDirectory(path); else UpdateWorkPath.Create(fence, path); return path; }
    private static void Terminal(WindowsFileFence fence, string work, byte[] plan, string marker)
    { Write(fence, work + "/" + marker, Bytes(UpdateTrust.Hash(plan) + "\n")); }
    private static object? Invoke(Type type, string method, WindowsFileFence fence)
    {
        try { return type.GetMethod(method, Hidden)!.Invoke(null, new object[] { fence }); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static int Pending(Kind kind, WindowsFileFence fence)
        => kind switch
        {
            Kind.Transaction => UpdateRecovery.Scan(fence).Count(entry => entry.Pending),
            Kind.Helper => HelperDelivery.PendingInstall(fence) ? 1 : 0,
            Kind.AutomaticHelper => ((ICollection)Invoke(typeof(AutomaticHelperUpgrade), "Scan", fence)!).Count,
            Kind.WholeBridge => ((ICollection)Invoke(typeof(WholeInstallBridge), "Scan", fence)!).Count,
            _ => ((ICollection)Invoke(typeof(WholeInstallBridge), "ScanIntents", fence)!).Count
        };
    private static void Recovery(Action body)
    {
        Assertions++;
        try { body(); }
        catch (ApplySafetyException error) when (error.Failure == ApplyFailure.RecoveryRequired) { return; }
        throw new InvalidOperationException("Expected RecoveryRequired, but the unsafe evidence was accepted.");
    }
    private static HelperRecord AddHelper(WindowsFileFence f, bool completed = true, bool wrongRoot = false)
    {
        var work = Private(f, "bap-v4-helper-"); var workId = f.DirectoryIdentity(work).Text;
        Write(f, work + "/image", Image); var imageId = Id(f, work + "/image");
        var receipt = Bytes("BetterAstralParty.HelperReceipt/v1\nroot=" + f.RootIdentity.Text + "\nimage-id=" + imageId
            + "\nversion=1.3.0\nsha256=" + UpdateTrust.Hash(Image) + "\ndescriptor=" + UpdateTrust.Hash(Descriptor) + "\nkey-id=maintenance-history-fixture\n");
        Write(f, work + "/receipt", receipt); var receiptId = Id(f, work + "/receipt");
        Write(f, work + "/descriptor", Descriptor); Write(f, work + "/signature", Signature);
        var plan = Bytes("BetterAstralParty.HelperInstall/v1\nroot=" + (wrongRoot ? "00000000:0000000000000000" : f.RootIdentity.Text)
            + "\nwork=" + work + "\nwork-id=" + workId + "\ndescriptor=" + UpdateTrust.Hash(Descriptor)
            + "\nimage-id=" + imageId + "\nreceipt-id=" + receiptId + "\n");
        Write(f, work + "/plan", plan); if (completed) Terminal(f, work, plan, "committed");
        return new HelperRecord(work, workId, imageId, receiptId, receipt);
    }
    private static string AddRecord(WindowsFileFence f, Kind kind, bool completed = true, bool wrongRoot = false, bool cancelled = false, string? descriptorHash = null)
    {
        if (kind == Kind.Helper) return AddHelper(f, completed, wrongRoot).Work;
        var prefix = kind switch { Kind.Transaction => "bap-v4-txn-", Kind.AutomaticHelper => "bap-v4-helperauto-", Kind.WholeBridge => "bap-v4-helperbridge-", _ => "bap-v4-wholeintent-" };
        var work = Private(f, prefix); var workId = f.DirectoryIdentity(work).Text;
        var root = wrongRoot ? "00000000:0000000000000000" : f.RootIdentity.Text;
        string plan;
        if (kind == Kind.Transaction)
        {
            var stage = Private(f, "bap-v4-stage-");
            var text = new StringBuilder("BetterAstralParty.Transaction/v4\nroot=" + root + "\nwork=" + work + "\nwork-id=" + workId
                + "\nstage=" + stage + "\nstage-id=" + f.DirectoryIdentity(stage).Text + "\ndescriptor=" + (descriptorHash ?? Digest)
                + "\noriginal=" + (descriptorHash == null ? "1.3.0" : PublicContext.CurrentVersion) + "\ncount=" + (InstallReceipt.Owned.Length + 2) + "\n");
            foreach (var path in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path, UpdateTransaction.HighWaterPath }))
                text.Append("file=").Append(path).Append("\t-\t-\t").Append(f.RootIdentity.Text).Append('\t').Append(Digest).Append('\n');
            plan = text.Append("settings=-\t-\n").ToString();
        }
        else if (kind == Kind.WholeIntent)
        {
            var observation = "BetterAstralParty.WholeObservation/v1\nroot=" + root + "\nplugin=-\nreceipt=-\nimage=-\t-\nhelper-receipt=-\t-\nwater=-\t-\nupdate="
                + Digest + "\nhelper=" + Digest + "\nmode=Fresh\nnonce=00000000000000000000000000000000\n";
            plan = "BetterAstralParty.WholeInstallIntent/v1\nroot=" + root + "\nwork=" + work + "\nwork-id=" + workId
                + "\nobservation=" + Convert.ToBase64String(Bytes(observation)) + "\ntarget-receipt=" + Digest + "\nold-plugin=-\t-\nold-receipt=-\t-\n";
        }
        else
        {
            var helper = AddHelper(f); var slot = f.RootIdentity.Text + "\t" + Digest;
            if (kind == Kind.AutomaticHelper)
                plan = "BetterAstralParty.AutomaticHelper/v1\nroot=" + root + "\nwork=" + work + "\nwork-id=" + workId
                    + "\nhelper-work=" + helper.Work + "\nhelper-id=" + helper.WorkId + "\ndescriptor=" + UpdateTrust.Hash(Descriptor)
                    + "\nreceipt=" + slot + "\nwater=-\t-\nold-image=" + slot + "\nold-receipt=" + slot + "\n";
            else
                plan = "BetterAstralParty.WholeHelperMigration/v1\nroot=" + root + "\nwork=" + work + "\nwork-id=" + workId
                    + "\nhelper-work=" + helper.Work + "\nhelper-work-id=" + helper.WorkId + "\nupdate=" + Digest + "\nhelper=" + UpdateTrust.Hash(Descriptor)
                    + "\nreceipt=" + Digest + "\nwater=-\t-\nold-image=-\t-\nold-receipt=-\t-\nnew-image=" + helper.ImageId + "\t" + UpdateTrust.Hash(Image)
                    + "\nnew-receipt=" + helper.ReceiptId + "\t" + UpdateTrust.Hash(helper.Receipt) + "\n";
        }
        var bytes = Bytes(plan); Write(f, work + "/plan", bytes);
        if (completed)
        {
            if (kind == Kind.Transaction) Terminal(f, work, bytes, "commit-intent");
            Terminal(f, work, bytes, cancelled ? "cancelled" : "committed");
        }
        return work;
    }
    private static void CompletedInventory(Kind kind)
    {
        using var f = Fresh(); var count = kind == Kind.Transaction ? 129 : 65;
        for (var i = 0; i < count; i++) AddRecord(f, kind, cancelled: kind == Kind.WholeIntent && i % 2 == 0);
        Check(Pending(kind, f) == 0, "Completed histories must not consume active recovery quota.");
        if (kind == Kind.Transaction) Check(UpdateRecovery.Scan(f).Count == count, "All completed transaction evidence remains discoverable.");
        UpdateLaunchGate.Check(f); Assertions++;
    }
    private static void FlatHistory(Kind kind)
    {
        using var f = Fresh(); string work;
        try { RetainedFlat = true; work = AddRecord(f, kind); }
        finally { RetainedFlat = false; }
        var id = f.DirectoryIdentity(work).Text;
        var before = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => UpdateTrust.Hash(File.ReadAllBytes(path)), StringComparer.OrdinalIgnoreCase);
        Check(Pending(kind, f) == 0, "valid completed flat history remains read-only discoverable: " + kind);
        UpdateLaunchGate.Check(f); UpdateRetention.Clean(f, Trust);
        Check(f.DirectoryIdentity(work).Text == id && before.All(row => File.Exists(row.Key) && UpdateTrust.Hash(File.ReadAllBytes(row.Key)) == row.Value), "flat completed evidence is neither migrated, archived nor reclaimed: " + kind);
        try { RetainedFlat = true; AddRecord(f, kind, completed: false); }
        finally { RetainedFlat = false; }
        try { Pending(kind, f); throw new InvalidOperationException("Pending flat work silently accepted"); }
        catch (ApplySafetyException error) { Check(error.Failure == ApplyFailure.RecoveryRequired && error.Data["BapRecoveryReason"] as string == "LegacyFlatWorkPending", "pending flat history has actionable recovery without mutation: " + kind); }
    }
    private static void MarkerTamper(Kind kind)
    {
        using var f = Fresh(); var work = AddRecord(f, kind);
        Replace(f, work + "/committed", Bytes(new string('B', 64) + "\n"));
        Recovery(() => Pending(kind, f)); Recovery(() => UpdateLaunchGate.Check(f));
    }
    private static void WrongRoot(Kind kind)
    {
        using var f = Fresh(); AddRecord(f, kind, wrongRoot: true);
        Recovery(() => Pending(kind, f));
    }
    private static void MixedTransaction()
    {
        using var f = Fresh(); for (var i = 0; i < 129; i++) AddRecord(f, Kind.Transaction);
        AddRecord(f, Kind.Transaction, completed: false);
        Check(Pending(Kind.Transaction, f) == 1, "Only unfinished transaction consumes active quota.");
        Recovery(() => UpdateLaunchGate.Check(f));
    }
    private static void EmptyActive(Kind kind)
    {
        using var f = Fresh(); var prefix = kind switch { Kind.Transaction => "bap-v4-txn-", Kind.Helper => "bap-v4-helper-", _ => "bap-v4-helperauto-" };
        for (var i = 0; i < (kind == Kind.Transaction ? 129 : 65); i++) Private(f, prefix);
        Recovery(() => Pending(kind, f));
    }
    private static void MixedHelper()
    {
        using var f = Fresh(); for (var i = 0; i < 65; i++) AddHelper(f);
        AddHelper(f, completed: false);
        Check(HelperDelivery.PendingInstall(f), "Completed helper history must not prevent discovering one exact pending helper plan.");
        Recovery(() => UpdateLaunchGate.Check(f));
    }
    private static void RetainedStageDiscovery()
    {
        using var f = Fresh(); for (var i = 0; i < 257; i++) AddRecord(f, Kind.Transaction);
        var pendingWork = AddRecord(f, Kind.Transaction, completed: false);
        var stage = Private(f, "bap-v4-stage-"); var feed = ReleaseFeedPolicy.For("Stable");
        var ticket = new UpdateTicket
        {
            RootId = f.RootIdentity.Text, Stage = stage, StageId = f.DirectoryIdentity(stage).Text, DescriptorHash = Digest,
            Context = new UpdateContext(feed.Repository, "Stable", "1.1.3", "1.1.4", 1, 2, UpdateTrust.PlatformId, repositoryId: feed.RepositoryId),
            GamePid = 1, GameCreation = 1
        };
        var bytes = ticket.Bytes(); Write(f, stage + "/handoff.ticket", bytes);
        Write(f, stage + "/helper.attempt", HelperRecoveryBinding.Attempt(UpdateTrust.Hash(bytes), pendingWork));
        Check(UpdateRecovery.PendingTicket(f) == stage, "257 retained completed payload stages must not consume active handoff discovery quota.");
        Recovery(() => UpdateLaunchGate.Check(f));
    }
    private static void TransactionConflict()
    {
        using var f = Fresh(); var work = AddRecord(f, Kind.Transaction);
        Terminal(f, work, Read(f, work + "/plan"), "rolled-back"); Recovery(() => UpdateRecovery.Scan(f));
    }
    private static void WholeConflict()
    {
        using var f = Fresh(); var work = AddRecord(f, Kind.WholeIntent);
        Terminal(f, work, Read(f, work + "/plan"), "cancelled"); Recovery(() => Pending(Kind.WholeIntent, f));
    }
    private static void HelperProvenance()
    {
        using var f = Fresh(); var history = new List<HelperRecord>();
        for (var i = 0; i < 65; i++) history.Add(AddHelper(f));
        var latest = history[^1];
        using (var file = f.OpenFile(latest.Work + "/image", true)) { file.RenameTo(f, HelperDescriptor.Image); file.Flush(); }
        using (var file = f.OpenFile(latest.Work + "/receipt", true)) { file.RenameTo(f, HelperDelivery.Receipt); file.Flush(); }
        Check(HelperDelivery.Fingerprint(f, Trust, 3) == UpdateTrust.Hash(Image), "Live helper authenticates its exact retained journal beyond old cap.");
        var removed = HelperDelivery.RemovedEvidence(f, Trust, Image, history[0].Receipt);
        Check(removed.Contains(history[0].Work + "/plan=", StringComparison.Ordinal), "Removed helper copy authenticates exactly its original retained signed evidence.");
        UpdateLaunchGate.Check(f); Assertions++;
        Replace(f, latest.Work + "/signature", Bytes("invalid signature envelope\n"));
        Assertions++;
        try { HelperDelivery.Fingerprint(f, Trust, 3); }
        catch (UpdateValidationException error) when (error.Failure == UpdateFailure.InvalidSignature) { return; }
        throw new InvalidOperationException("Malformed signature must still reject the exact live helper provenance.");
    }
    private static HandoffRecord SignedHandoff(WindowsFileFence f, string terminal, bool pendingAttempt = false, bool wrongDescriptor = false, string? retainedWork = null)
    {
        var digest = wrongDescriptor ? Digest : PublicDescriptorHash;
        var noAttempt = terminal is "Cancelled" or "FaultDisabledNoChanges";
        var work = noAttempt ? null : retainedWork ?? AddRecord(f, Kind.Transaction, completed: terminal is not ("FaultDisabled" or "Recovered") && !pendingAttempt, descriptorHash: digest);
        if (work != null && (terminal is "FaultDisabled" or "Recovered") && !pendingAttempt && retainedWork == null)
            Terminal(f, work, Read(f, work + "/plan"), "rolled-back");
        var stage = Private(f, "bap-v4-stage-");
        var ticket = new UpdateTicket { RootId = f.RootIdentity.Text, Stage = stage, StageId = f.DirectoryIdentity(stage).Text, DescriptorHash = digest,
            Context = PublicContext, GamePid = 1, GameCreation = 1 };
        var bytes = ticket.Bytes(); var hash = UpdateTrust.Hash(bytes);
        Write(f, stage + "/handoff.ticket", bytes);
        if (work != null) Write(f, stage + "/helper.attempt", HelperRecoveryBinding.Attempt(hash, work));
        Write(f, stage + "/helper.result", Bytes(hash + "\n" + (terminal == "FaultDisabledNoChanges" ? "FaultDisabled" : terminal) + "\n"));
        if (terminal is "FaultDisabled" or "FaultDisabledNoChanges") Write(f, stage + "/helper.recovery", HelperRecoveryBinding.Resolution(hash, work,
            work == null ? RecoveryOutcome.NoChanges : RecoveryOutcome.OriginalRestored));
        Write(f, stage + "/descriptor.bin", PublicDescriptor); Write(f, stage + "/signature.bin", PublicEnvelope.Signature);
        Write(f, stage + "/key-id.bin", Bytes(PublicEnvelope.KeyId));
        return new HandoffRecord(ticket, bytes, work);
    }
    private static bool Retired(WindowsFileFence f, HandoffRecord handoff)
    {
        var entries = UpdateRecovery.Scan(f); handoff.Ticket.Recheck(f);
        var method = typeof(UpdateRecovery).GetMethod("RetiredTicket", Hidden)
            ?? throw new InvalidOperationException("The production read-only terminal classification kernel is not yet present.");
        try { return (bool)method.Invoke(null, new object[] { f, handoff.Ticket, handoff.Bytes, entries, UpdateTrust.Production() })!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void SignedTerminalPositive(string terminal)
    {
        using var f = Fresh(); var handoff = SignedHandoff(f, terminal);
        Check(Retired(f, handoff), "Exact public-signed terminal proof must retire only the finished/no-change handoff inventory.");
    }
    private static void SignedTerminalNegative(string fault)
    {
        using var f = Fresh(); var handoff = SignedHandoff(f, fault is "resolution" or "missing-resolution" ? "FaultDisabled" : "Committed",
            pendingAttempt: fault == "unfinished-attempt", wrongDescriptor: fault == "descriptor-binding");
        var stage = handoff.Ticket.Stage;
        switch (fault)
        {
            case "result-hash": Replace(f, stage + "/helper.result", Bytes(Digest + "\nCommitted\n")); break;
            case "attempt-binding": Replace(f, stage + "/helper.attempt", HelperRecoveryBinding.Attempt(UpdateTrust.Hash(handoff.Bytes), UpdateWorkPath.New("txn"))); break;
            case "signature": var corrupted = (byte[])PublicEnvelope.Signature.Clone(); corrupted[0] ^= 1; Replace(f, stage + "/signature.bin", corrupted); break;
            case "resolution": Replace(f, stage + "/helper.recovery", HelperRecoveryBinding.Resolution(UpdateTrust.Hash(handoff.Bytes), handoff.AttemptWork, RecoveryOutcome.TargetCompleted)); break;
            case "missing-resolution":
                // Rename only this fresh fixture-owned evidence file by a native handle;
                // no real installation, retained user file or directory is touched.
                using (var file = f.OpenFile(stage + "/helper.recovery", true)) { file.RenameTo(f, stage + "/fixture-resolution-withheld"); file.Flush(); } break;
        }
        try { Check(!Retired(f, handoff), "Unverified/unresolved handoff evidence must remain an active candidate."); }
        catch (ApplySafetyException error) when (fault == "missing-resolution" && error.NativeError == 2)
        { Assertions++; } // PendingTicket must classify this unretired candidate, not infer completion.
    }
    private static void SignedTerminalDiscovery()
    {
        using var f = Fresh(); string? committed = null, rolled = null;
        var terminals = new[] { "Committed", "Recovered", "Cancelled", "FaultDisabled", "FaultDisabledNoChanges" };
        for (var i = 0; i < 257; i++)
        {
            var terminal = terminals[i % terminals.Length];
            var handoff = SignedHandoff(f, terminal, retainedWork: terminal is "FaultDisabled" or "Recovered" ? rolled : terminal == "Committed" ? committed : null);
            if (terminal is "FaultDisabled" or "Recovered") rolled ??= handoff.AttemptWork;
            else if (terminal == "Committed") committed ??= handoff.AttemptWork;
        }
        var pending = AddRecord(f, Kind.Transaction, completed: false, descriptorHash: PublicDescriptorHash);
        var stage = Private(f, "bap-v4-stage-");
        var ticket = new UpdateTicket { RootId = f.RootIdentity.Text, Stage = stage, StageId = f.DirectoryIdentity(stage).Text,
            DescriptorHash = PublicDescriptorHash, Context = PublicContext, GamePid = 1, GameCreation = 1 };
        var bytes = ticket.Bytes(); Write(f, stage + "/handoff.ticket", bytes);
        Write(f, stage + "/helper.attempt", HelperRecoveryBinding.Attempt(UpdateTrust.Hash(bytes), pending));
        Check(UpdateRecovery.PendingTicket(f) == stage, "257 signed terminal handoff histories must not consume the active discovery quota.");
        Recovery(() => UpdateLaunchGate.Check(f));
    }

    private static void PrepareCancellationProof()
    {
        using (var memory = new MemoryStream()) {
            using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
                foreach (var path in InstallReceipt.Owned.OrderBy(path => path, StringComparer.Ordinal)) {
                    var entry = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.NoCompression);
                    entry.ExternalAttributes = 0;
                    using var content = entry.Open(); content.WriteByte(80);
                }
            CancelPackage = memory.ToArray();
        }
        CancelContext = new UpdateContext(ReleaseFeedPolicy.StableRepository, "Stable", "1.3.0", "1.3.1", 123, 456,
            UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
        var rows = new List<string> { "BetterAstralParty.UpdateDescriptor/v4", "repository=" + CancelContext.Repository,
            "version=1.3.1", "channel=Stable", "platform=" + UpdateTrust.PlatformId, "release-id=123", "asset-id=456",
            "asset-name=BetterAstralParty-1.3.1-update.zip", "zip-bytes=" + CancelPackage.Length, "zip-sha256=" + UpdateTrust.Hash(CancelPackage),
            "file-count=" + InstallReceipt.Owned.Length, "purpose=release/Stable", "repository-id=" + ReleaseFeedPolicy.StableRepositoryId, "settings-schema=1" };
        foreach (var path in InstallReceipt.Owned.OrderBy(path => path, StringComparer.Ordinal)) rows.Add("file=" + path + "\t1\t" + UpdateTrust.Hash(new byte[] { 80 }));
        CancelDescriptor = Bytes(string.Join("\n", rows) + "\n");
        CancelSignature = Key.SignData(CancelDescriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var verified = Trust.VerifyDescriptor(CancelDescriptor, "maintenance-history-fixture", CancelSignature, CancelContext);
        UpdatePackage.VerifyPayload(CancelPackage, verified);
    }
    private static HandoffRecord CancellationHandoff(WindowsFileFence f, string? terminal = null)
    {
        var stage = Private(f, "bap-v4-stage-");
        var ticket = new UpdateTicket { RootId = f.RootIdentity.Text, Stage = stage, StageId = f.DirectoryIdentity(stage).Text,
            DescriptorHash = UpdateTrust.Hash(CancelDescriptor), Context = CancelContext, GamePid = 1, GameCreation = 1 };
        var bytes = ticket.Bytes(); var hash = UpdateTrust.Hash(bytes);
        Write(f, stage + "/handoff.ticket", bytes); Write(f, stage + "/descriptor.bin", CancelDescriptor);
        Write(f, stage + "/signature.bin", CancelSignature); Write(f, stage + "/key-id.bin", Bytes("maintenance-history-fixture"));
        Write(f, stage + "/package.zip", CancelPackage);
        if (terminal == null) Write(f, stage + "/helper.cancel", Bytes(hash + "\n"));
        else Write(f, stage + "/helper.result", Bytes(hash + "\n" + terminal + "\n"));
        return new HandoffRecord(ticket, bytes, null);
    }
    private static bool CancelUnclaimed(WindowsFileFence f, HandoffRecord handoff)
        => UpdateRetention.CancelUnclaimed(f, handoff.Ticket, Trust);
    private static bool RetiredCancellation(WindowsFileFence f, HandoffRecord handoff)
    {
        var method = typeof(UpdateRecovery).GetMethod("RetiredTicket", Hidden)!;
        var args = new object?[] { f, handoff.Ticket, handoff.Bytes, UpdateRecovery.Scan(f), Trust };
        try { return (bool)method.Invoke(null, args)!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void CancelledBeforeQueue()
    {
        using var f = Fresh(); var handoff = CancellationHandoff(f);
        Check(CancelUnclaimed(f, handoff), "Unclaimed, authenticated cancellation must gain a durable terminal record.");
        var terminal = Read(f, handoff.Ticket.Stage + "/helper.result");
        Check(Encoding.ASCII.GetString(terminal) == UpdateTrust.Hash(handoff.Bytes) + "\nCancelled\n", "Terminal cancellation remains bound to the exact ticket.");
        Check(CancelUnclaimed(f, handoff), "Repeating the same completed cancellation must be harmless.");
        Check(Read(f, handoff.Ticket.Stage + "/helper.result").SequenceEqual(terminal), "Cancellation never rewrites terminal evidence.");
        Check(RetiredCancellation(f, handoff), "Cancelled without a helper must retire active inventory.");
    }
    private static void CancelledPackages()
    {
        using var f = Fresh(); var handoffs = new List<HandoffRecord>();
        for (var i = 0; i < 80; i++) { var handoff = CancellationHandoff(f); Check(CancelUnclaimed(f, handoff), "Each cancelled preparation finishes without a helper."); handoffs.Add(handoff); }
        var result = UpdateRetention.Clean(f, Trust);
        Check(result.RemovedPackages == 80 && result.Preserved == 0, "Only duplicate authenticated cancelled package bytes are reclaimed.");
        foreach (var handoff in handoffs) { Check(Read(f, handoff.Ticket.Stage + "/handoff.ticket").SequenceEqual(handoff.Bytes), "Retained cancellation ticket remains unchanged."); Check(RetiredCancellation(f, handoff), "Cleaning duplicate ZIP bytes does not undo retirement."); }
        Check(UpdateRetention.Clean(f, Trust).AlreadyClean == 80, "A second clean observes the retained exact cleanup proofs.");
    }
    private static void CancelledDiscovery()
    {
        using var f = Fresh();
        for (var i = 0; i < 257; i++) Check(CancelUnclaimed(f, CancellationHandoff(f)), "Historical no-helper cancellation is proven terminal.");
        var pending = AddRecord(f, Kind.Transaction, completed: false, descriptorHash: UpdateTrust.Hash(CancelDescriptor));
        var active = CancellationHandoff(f, "RecoveryRequired");
        Write(f, active.Ticket.Stage + "/helper.attempt", HelperRecoveryBinding.Attempt(UpdateTrust.Hash(active.Bytes), pending));
        Check(UpdateRecovery.PendingTicket(f, trust: Trust) == active.Ticket.Stage, "257 cancelled preparations do not hide one exact pending recovery.");
        Recovery(() => UpdateLaunchGate.Check(f));
    }
    private static void CancellationBlocked(string blocker)
    {
        using var f = Fresh(); var handoff = CancellationHandoff(f); var stage = handoff.Ticket.Stage; WindowsFileFence.FileLease? held = null;
        try {
            switch (blocker) {
                case "claim": Write(f, stage + "/helper.claim", Bytes(UpdateTrust.Hash(handoff.Bytes) + "\n")); break;
                case "attempt": Write(f, stage + "/helper.attempt", HelperRecoveryBinding.Attempt(UpdateTrust.Hash(handoff.Bytes), UpdateWorkPath.New("txn"))); break;
                case "owner": Write(f, stage + "/helper.owner", Bytes("fixture owner evidence\n")); break;
                case "operation": UpdateFaults.Begin(f, CancelContext, FaultPhase.Apply, handoff.Ticket); break;
                case "busy-ticket": held = f.OpenFile(stage + "/handoff.ticket", true); break;
                case "busy-lock": held = f.OpenFile("BetterAstralParty.update.lock", true, true); break;
                case "ticket": Replace(f, stage + "/handoff.ticket", handoff.Bytes.Select((b, i) => i == 0 ? (byte)88 : b).ToArray()); break;
                case "signature": var signature = (byte[])CancelSignature.Clone(); signature[0] ^= 1; Replace(f, stage + "/signature.bin", signature); break;
                case "descriptor": Replace(f, stage + "/descriptor.bin", PublicDescriptor); break;
                case "cancel-hash": Replace(f, stage + "/helper.cancel", Bytes(Digest + "\n")); break;
            }
            var refused = false;
            try { refused = !CancelUnclaimed(f, handoff); }
            catch (ApplySafetyException) { refused = true; }
            catch (UpdateValidationException) { refused = true; }
            Check(refused, "Busy, claimed, changed or unverified cancellation must not become terminal.");
        } finally { held?.Dispose(); }
        Check(!File.Exists(WindowsPath.ForIo(f.Full(stage + "/helper.result"))), "Blocked cancellation creates no terminal record.");
        Check(Read(f, stage + "/package.zip").SequenceEqual(CancelPackage), "Blocked cancellation preserves all staged package bytes.");
    }
    private static void NoChangeTerminalCleanup(string terminal)
    {
        using var f = Fresh(); var handoff = CancellationHandoff(f, terminal);
        Check(RetiredCancellation(f, handoff), "A supported signed no-change terminal state is classified consistently.");
        var result = UpdateRetention.Clean(f, Trust);
        Check(result.RemovedPackages == 1 && Read(f, handoff.Ticket.Stage + "/handoff.ticket").SequenceEqual(handoff.Bytes), "A known prerequisite exit permits duplicate ZIP cleanup, not journal deletion.");
    }
    private static void UnresolvedRetention()
    {
        using var f = Fresh(); var handoff = CancellationHandoff(f, "RecoveryRequired");
        Check(!RetiredCancellation(f, handoff), "An unresolved recovery result must not count as a finished cancellation.");
        var result = UpdateRetention.Clean(f, Trust);
        Check(result.RemovedPackages == 0 && Read(f, handoff.Ticket.Stage + "/package.zip").SequenceEqual(CancelPackage), "No cleanup permission comes from a signed descriptor alone.");
    }
}
