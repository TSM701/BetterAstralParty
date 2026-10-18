using BetterAstralParty.Updating;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;

// Inventory tests do not execute installers, games or Steam. Transactions are structural
// fixtures, not proof that a signed update was applied. Protocol 5 has no update helper,
// handoff ticket or whole-install intent; only contained bap-v5 txn/stage work remains.
internal static class HistoryInventoryFixture
{
    private sealed record Case(string Name, bool Passed, string? Failure = null, long ElapsedMilliseconds = 0);
    private static readonly string Digest = new('A', 64);
    private static readonly List<Case> Cases = new();
    private static string FixtureRoot = "";
    private static int Sequence, Assertions;
    private static byte[] PublicDescriptor = null!, PublicSignature = null!;
    private static DetachedSignature PublicEnvelope = null!;
    private static UpdateContext PublicContext = null!;
    private static VerifiedUpdateDescriptor PublicVerified = null!;
    private static string PublicProofRoot = "";

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
            if (publicLines.Length < 15 || publicLines[0] != UpdateTrust.DescriptorHeader) throw new InvalidOperationException("A signed baseline RemoteUpdate proof, not a LocalInstall or legacy proof, is required.");
            PublicEnvelope = DetachedSignature.Parse(PublicSignature);
            PublicContext = new UpdateContext(publicLines[1][11..], publicLines[3][8..], "1.4.0-0", publicLines[2][8..],
                long.Parse(publicLines[5][11..], CultureInfo.InvariantCulture), long.Parse(publicLines[6][9..], CultureInfo.InvariantCulture), UpdateTrust.PlatformId,
                repositoryId: long.Parse(publicLines[12][14..], CultureInfo.InvariantCulture));
            PublicVerified = UpdateTrust.Production().VerifyDescriptor(PublicDescriptor, PublicEnvelope.KeyId, PublicEnvelope.Signature, PublicContext);
            CheckCase("Transaction:retained-completed-over-old-limit", CompletedInventory);
            CheckCase("Transaction:terminal-marker-tamper-rejected", MarkerTamper);
            CheckCase("Transaction:completed-native-root-mismatch-rejected", WrongRoot);
            CheckCase("Transaction:mixed-completed-and-pending", MixedTransaction);
            CheckCase("Transaction:pre-journal-empty-active-over-limit-rejected", EmptyActive);
            CheckCase("Transaction:committed-and-rolled-back-conflict-rejected", TransactionConflict);
            CheckCase("Transaction:protocol4-and-flat-work-never-matched", LegacyWorkIgnored);
            CheckCase("TerminalRetention:committed-authenticated-package-cleaned", CommittedPackageCleanup);
            CheckCase("TerminalRetention:changed-package-preserved", ChangedPackagePreserved);
            CheckCase("TerminalRetention:pending-transaction-blocks-cleanup", PendingBlocksCleanup);
            var passed = Cases.All(test => test.Passed);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Passed = passed, Assertions, Cases, Runtime = Environment.Version.ToString(), FixtureRoot,
                NoGameOrSteamExecution = true, NoNetworkRequests = true, EphemeralFixtureKeyOnly = true,
                StructuralInventoryNotSignedUpdateExecution = true,
                ExistingEvidencePathsPreserved = true,
                Retired = "Protocol-4 update-helper, automatic-helper, whole-bridge/intent, handoff-ticket and cancellation inventories; protocol 5 has no helper or whole-install intent",
                PublicRemoteProof = new { Path = PublicProofRoot, PublicVerified.DescriptorSha256, SignatureSha256 = UpdateTrust.Hash(PublicSignature), PublicContext.Repository, PublicContext.RepositoryId, PublicContext.ReleaseTag, PublicContext.ReleaseId, PublicContext.AssetId, KeyId = PublicEnvelope.KeyId },
                Scope = "Canonical protocol-5 transaction inventory/native ID/terminal marker tests and authenticated terminal package retention. Remote proof uses a compiled fixture-only public pin. Transaction journals are structural simulations, not successful signed update executions. No installer process execution, live game root, operating signing key or network."
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
    private static byte[] Read(WindowsFileFence fence, string path)
    { using var file = fence.OpenFile(path); return UpdateTicket.Read(file, 16384); }
    private static string Private(WindowsFileFence fence, string kind)
    { var path = UpdateWorkPath.New(kind); UpdateWorkPath.Create(fence, path); return path; }
    private static void Terminal(WindowsFileFence fence, string work, byte[] plan, string marker)
    { Write(fence, work + "/" + marker, Bytes(UpdateTrust.Hash(plan) + "\n")); }
    private static int Pending(WindowsFileFence fence) => UpdateRecovery.Scan(fence).Count(entry => entry.Pending);
    private static void Recovery(Action body)
    {
        Assertions++;
        try { body(); }
        catch (ApplySafetyException error) when (error.Failure == ApplyFailure.RecoveryRequired) { return; }
        throw new InvalidOperationException("Expected RecoveryRequired, but the unsafe evidence was accepted.");
    }
    // Structural protocol-5 transaction plan in the exact layout UpdateRecovery.Scan validates.
    private static (string Work, string Stage) AddRecord(WindowsFileFence f, bool completed = true, bool wrongRoot = false, string? descriptorHash = null)
    {
        var work = Private(f, "txn"); var workId = f.DirectoryIdentity(work).Text;
        var root = wrongRoot ? "00000000:0000000000000000" : f.RootIdentity.Text;
        var stage = Private(f, "stage");
        var text = new StringBuilder(UpdateTransaction.Header + "\nroot=" + root + "\nwork=" + work + "\nwork-id=" + workId
            + "\nstage=" + stage + "\nstage-id=" + f.DirectoryIdentity(stage).Text + "\ndescriptor=" + (descriptorHash ?? Digest)
            + "\noriginal=1.4.0\ncount=" + (InstallReceipt.Owned.Length + 2) + "\n");
        foreach (var path in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path, UpdateTransaction.HighWaterPath }))
            text.Append("file=").Append(path).Append("\t-\t-\t").Append(f.RootIdentity.Text).Append('\t').Append(Digest).Append('\n');
        var bytes = Bytes(text.Append("settings=-\t-\n").ToString()); Write(f, work + "/plan", bytes);
        if (completed) { Terminal(f, work, bytes, "commit-intent"); Terminal(f, work, bytes, "committed"); }
        return (work, stage);
    }
    private static void CompletedInventory()
    {
        using var f = Fresh(); const int count = 129;
        for (var i = 0; i < count; i++) AddRecord(f);
        Check(Pending(f) == 0, "Completed histories must not consume active recovery quota.");
        Check(UpdateRecovery.Scan(f).Count == count, "All completed transaction evidence remains discoverable.");
        UpdateLaunchGate.Check(f); Assertions++;
    }
    private static void MarkerTamper()
    {
        using var f = Fresh(); var work = AddRecord(f).Work;
        Replace(f, work + "/committed", Bytes(new string('B', 64) + "\n"));
        Recovery(() => Pending(f)); Recovery(() => UpdateLaunchGate.Check(f));
    }
    private static void WrongRoot()
    {
        using var f = Fresh(); AddRecord(f, wrongRoot: true);
        Recovery(() => Pending(f));
    }
    private static void MixedTransaction()
    {
        using var f = Fresh(); for (var i = 0; i < 129; i++) AddRecord(f);
        AddRecord(f, completed: false);
        Check(Pending(f) == 1, "Only unfinished transaction consumes active quota.");
        Recovery(() => UpdateLaunchGate.Check(f));
    }
    private static void EmptyActive()
    {
        using var f = Fresh();
        for (var i = 0; i < 129; i++) Private(f, "txn");
        Recovery(() => Pending(f));
    }
    private static void TransactionConflict()
    {
        using var f = Fresh(); var work = AddRecord(f).Work;
        Terminal(f, work, Read(f, work + "/plan"), "rolled-back"); Recovery(() => UpdateRecovery.Scan(f));
    }
    private static void LegacyWorkIgnored()
    {
        using var f = Fresh();
        // 1.3.x (protocol 4) contained work and pre-container flat work are never matched as protocol-5 evidence.
        f.CreatePrivateDirectory(UpdateWorkPath.Root);
        foreach (var path in new[] { UpdateWorkPath.Root + "/bap-v4-txn-" + Guid.NewGuid().ToString("N"), "bap-v4-txn-" + Guid.NewGuid().ToString("N") }) {
            f.CreatePrivateDirectory(path); Write(f, path + "/plan", Bytes("unfinished legacy fixture plan\n"));
        }
        Check(Pending(f) == 0 && UpdateRecovery.Scan(f).Count == 0, "protocol-4 and flat work directories are outside the protocol-5 scan");
        UpdateLaunchGate.Check(f); Assertions++;
    }
    private static (string Work, string Stage) SignedCommitted(WindowsFileFence f, byte[] package)
    {
        var record = AddRecord(f, descriptorHash: PublicVerified.DescriptorSha256);
        Write(f, record.Stage + "/descriptor.bin", PublicDescriptor); Write(f, record.Stage + "/signature.bin", PublicEnvelope.Signature);
        Write(f, record.Stage + "/key-id.bin", Bytes(PublicEnvelope.KeyId)); Write(f, record.Stage + "/package.zip", package);
        return record;
    }
    // The fixture proof signs a one-byte ZIP {1}; only those exact bytes are reclaimable.
    private static readonly byte[] SignedZip = { 1 };
    private static void CommittedPackageCleanup()
    {
        using var f = Fresh();
        Check(PublicVerified.ZipBytes == 1 && PublicVerified.ZipSha256 == UpdateTrust.Hash(SignedZip), "fixture remote proof binds the inert one-byte package");
        var record = SignedCommitted(f, SignedZip); var plan = Read(f, record.Work + "/plan");
        var result = UpdateRetention.Clean(f, UpdateTrust.Production());
        Check(result.RemovedPackages == 1 && result.Preserved == 0, "authenticated committed package bytes are reclaimed");
        Check(!File.Exists(WindowsPath.ForIo(f.Full(record.Stage + "/package.zip"))) && Read(f, record.Work + "/plan").SequenceEqual(plan)
            && Read(f, record.Stage + "/descriptor.bin").SequenceEqual(PublicDescriptor), "cleanup removes only the package, never the plan or signed descriptor");
        Check(UpdateRetention.Clean(f, UpdateTrust.Production()).AlreadyClean == 1, "a second clean observes the already-reclaimed package");
    }
    private static void ChangedPackagePreserved()
    {
        using var f = Fresh(); var changed = new byte[] { 2 }; var record = SignedCommitted(f, changed);
        var result = UpdateRetention.Clean(f, UpdateTrust.Production());
        Check(result.RemovedPackages == 0 && result.Preserved == 1 && Read(f, record.Stage + "/package.zip").SequenceEqual(changed), "no cleanup permission comes from a signed descriptor alone");
    }
    private static void PendingBlocksCleanup()
    {
        using var f = Fresh(); var record = SignedCommitted(f, SignedZip); AddRecord(f, completed: false);
        Recovery(() => UpdateRetention.Clean(f, UpdateTrust.Production()));
        Check(Read(f, record.Stage + "/package.zip").SequenceEqual(SignedZip), "pending recovery preserves every retained package");
    }
}
