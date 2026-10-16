#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BetterAstralParty.Updating;

internal static class StagingFixture
{
    private static string[] WorkDirectories(string root, string kind)
    { using var fence = new WindowsFileFence(root); var paths = new System.Collections.Generic.List<string>(); foreach (var relative in UpdateWorkPath.Directories(fence, kind)) paths.Add(fence.Full(relative)); return paths.ToArray(); }
    private static string WorkRelative(string root, string path) => path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');

    private static int _checks;
    private static readonly UpdateContext Context = new UpdateContext("TSM701/BetterAstralParty", "Stable", "1.3.0", "1.3.2", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existing, IntPtr security);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out IntPtr descriptor, out uint length);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out IntPtr dacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SetNamedSecurityInfoW(string path, int kind, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception("Staging fixture: " + message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (UpdateValidationException) { Check(true, message + " [validation]"); return; }
        catch (ApplySafetyException) { Check(true, message + " [safety]"); return; }
        throw new Exception("Staging fixture accepted: " + message);
    }
    private sealed class Package : IDisposable
    {
        private readonly RSA _signing;
        internal readonly byte[] Descriptor, Signature, Zip;
        internal readonly UpdateTrust Trust;
        internal Package()
        {
#if NETFRAMEWORK
            _signing = new RSACng(3072);
#else
            _signing = RSA.Create(3072);
#endif
            var bytes = new byte[2048]; new Random(145).NextBytes(bytes);
            using (var output = new MemoryStream())
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    foreach (var path in InstallReceipt.Owned) {
                        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
                        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using (var target = entry.Open()) target.Write(bytes, 0, bytes.Length);
                    }
                }
                Zip = output.ToArray();
            }
            Descriptor = Encoding.ASCII.GetBytes("BetterAstralParty.UpdateDescriptor/v4\nrepository=TSM701/BetterAstralParty\nversion=1.3.2\nchannel=Stable\nplatform="
                + UpdateTrust.PlatformId + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-1.3.2-update.zip\nzip-bytes=" + Zip.Length
                + "\nzip-sha256=" + UpdateTrust.Hash(Zip) + "\nfile-count=5\npurpose=release/Stable\nrepository-id=" + ReleaseFeedPolicy.StableRepositoryId + "\nsettings-schema=1\n"
                + string.Concat(InstallReceipt.Owned.Select(path => "file=" + path + "\t" + bytes.Length + "\t" + UpdateTrust.Hash(bytes) + "\n")));
            Signature = _signing.SignData(Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            Trust = new UpdateTrust(new[] { new TrustedUpdateKey("staging-fixture", _signing.ExportParameters(false)) });
        }
        internal StagedUpdate Create(WindowsFileFence fence, CancellationToken cancellation = default(CancellationToken))
        { return UpdateStaging.Create(fence, Descriptor, "staging-fixture", Signature, Zip, Trust, Context, cancellation); }
        public void Dispose() { _signing.Dispose(); }
    }
    private static void Initial(WindowsFileFence fence, string root, Package package)
    {
        var initial = Directory.GetDirectories(root).Length;
        Reject(() => UpdateStaging.Create(fence, package.Descriptor, "staging-fixture", package.Signature, package.Zip, UpdateTrust.Production(), Context), "unconfigured root rejects before private creation");
        Check(Directory.GetDirectories(root).Length == initial, "no trust root creates no directory/ACL");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { package.Create(fence, cancellation.Token); throw new Exception("Precancel accepted"); }
            catch (OperationCanceledException) { Check(true, "pre-cancelled staging rejected"); }
        }
        Check(Directory.GetDirectories(root).Length == initial, "pre-cancel creates no directory");
        var bad = (byte[])package.Descriptor.Clone(); bad[0] ^= 1;
        Reject(() => UpdateStaging.Create(fence, bad, "staging-fixture", package.Signature, package.Zip, package.Trust, Context), "bad signature stops staging");
        Check(Directory.GetDirectories(root).Length == initial, "untrusted descriptor has no disk side effect");
        Reject(() => fence.CreatePrivateDirectory("existing"), "existing directory not overwritten or resecured");
        Reject(() => fence.AssertPrivateDirectory("existing"), "ordinary inherited ACL rejected");
        Check(File.ReadAllText(Path.Combine(root, "existing", "keep.txt")) == "keep", "existing directory untouched");
    }
    private static void Valid(WindowsFileFence fence, string root, Package package)
    {
        var descriptor = (byte[])package.Descriptor.Clone(); var signature = (byte[])package.Signature.Clone(); var zip = (byte[])package.Zip.Clone();
        var staged = UpdateStaging.Create(fence, descriptor, "staging-fixture", signature, zip, package.Trust, Context);
        descriptor[0] ^= 1; signature[0] ^= 1; zip[0] ^= 1;
        fence.AssertPrivateDirectory(staged.Directory); Check(true, "private owner/protected exact user+SYSTEM ACL validated");
        Check(Directory.GetFiles(Path.Combine(root, staged.Directory)).Length == 4, "only four metadata/package files staged");
        using (var peer = new WindowsFileFence(root))
        using (var locked = UpdateStaging.VerifyLocked(peer, staged, package.Trust, Context))
        {
            locked.Recheck(); Check(locked.Payload.Package.Descriptor.Version == "1.3.2", "fresh peer revalidates signature/context/package");
            using (var input = locked.Payload.OpenRead())
            using (var output = new MemoryStream())
            { input.CopyTo(output); Check(UpdateTrust.Hash(output.ToArray()) == UpdateTrust.Hash(package.Zip), "staged image equals verified caller-independent snapshot"); }
            foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" })
            {
                try { File.WriteAllText(Path.Combine(root, staged.Directory, name), "changed"); throw new Exception("Locked data write accepted"); }
                catch (IOException) { Check(true, "staged file write blocked while verified lease held"); }
            }
            using (var second = UpdateStaging.VerifyLocked(fence, staged, package.Trust, Context)) {
                second.Recheck(); Check(true, "read-only verifiers share the same protected snapshot");
                foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" }) {
                    var path=Path.Combine(root,staged.Directory,name);
                    try { File.Delete(path); throw new Exception("Protected proof delete accepted"); }
                    catch (IOException) { Check(true, "read-only verifier leases deny deletion"); }
                    try { File.Move(path,path+"-moved"); throw new Exception("Protected proof rename accepted"); }
                    catch (IOException) { Check(true, "read-only verifier leases deny replacement"); }
                }
            }
        }
        using (UpdateStaging.VerifyLocked(fence, staged, package.Trust, Context)) Check(true, "leases released after dispose");
        var disposed = UpdateStaging.VerifyLocked(fence, staged, package.Trust, Context); disposed.Dispose(); disposed.Dispose();
        try { disposed.Recheck(); throw new Exception("Disposed lease recheck accepted"); }
        catch (ObjectDisposedException) { Check(true, "disposed verified lease cannot claim on-disk protection"); }
        var mismatch = new UpdateContext(Context.Repository, "Beta", Context.CurrentVersion, Context.ReleaseTag, Context.ReleaseId, Context.AssetId, Context.Platform);
        Reject(() => { using (UpdateStaging.VerifyLocked(fence, staged, package.Trust, mismatch)) { } }, "changed channel rejects staged package");
        var highWater = new UpdateContext(Context.Repository, "Stable", Context.CurrentVersion, Context.ReleaseTag, Context.ReleaseId, Context.AssetId, Context.Platform, "1.3.2", ReleaseFeedPolicy.StableRepositoryId);
        Reject(() => { using (UpdateStaging.VerifyLocked(fence, staged, package.Trust, highWater)) { } }, "replay against highest-applied rejected");
        using (UpdateStaging.VerifyLocked(fence, staged, package.Trust, Context)) Check(true, "failed revalidation released all handles");
        Reject(() => { var fake = new StagedUpdate(new object(), staged.Directory, staged.DirectoryIdentity, staged.DescriptorSha256); }, "unverified record stamp denied");
    }
    private static void Tampering(WindowsFileFence fence, string root, Package package)
    {
        foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" })
        {
            var staged = package.Create(fence); var path = Path.Combine(root, staged.Directory, name);
            var bytes = File.ReadAllBytes(path); bytes[0] ^= 1; File.WriteAllBytes(path, bytes);
            Reject(() => { using (UpdateStaging.VerifyLocked(fence, staged, package.Trust, Context)) { } }, name + " tamper denied");
        }
        var linked = package.Create(fence);
        Check(CreateHardLinkW(Path.Combine(root, "package-alias.zip"), Path.Combine(root, linked.Directory, "package.zip"), IntPtr.Zero), "fixture package hardlink created");
        Reject(() => { using (UpdateStaging.VerifyLocked(fence, linked, package.Trust, Context)) { } }, "hardlinked staged package denied");
        var truncated = package.Create(fence); File.WriteAllBytes(Path.Combine(root, truncated.Directory, "package.zip"), new byte[] { 1 });
        Reject(() => { using (UpdateStaging.VerifyLocked(fence, truncated, package.Trust, Context)) { } }, "truncated ZIP denied");
        var oversized = package.Create(fence); File.WriteAllBytes(Path.Combine(root, oversized.Directory, "descriptor.bin"), new byte[UpdateTrust.MaxDescriptorBytes + 1]);
        Reject(() => { using (UpdateStaging.VerifyLocked(fence, oversized, package.Trust, Context)) { } }, "oversized descriptor bounded before parse");
        var missing = package.Create(fence); File.Delete(Path.Combine(root, missing.Directory, "signature.bin"));
        Reject(() => { using (UpdateStaging.VerifyLocked(fence, missing, package.Trust, Context)) { } }, "missing stage file denied");
    }
    private static void Replacement(string root, Package package)
    {
        StagedUpdate original;
        using (var fence = new WindowsFileFence(root)) original = package.Create(fence);
        var oldPath = Path.Combine(root, original.Directory); var moved = oldPath + "-moved";
        Directory.Move(oldPath, moved); // Both known fixture paths are inside this freshly-created root.
        using (var fence = new WindowsFileFence(root))
        {
            UpdateWorkPath.Create(fence, original.Directory);
            foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" })
                File.Copy(Path.Combine(moved, name), Path.Combine(root, original.Directory, name));
            Reject(() => { using (UpdateStaging.VerifyLocked(fence, original, package.Trust, Context)) { } }, "replaced directory ID denied even for identical signed bytes");
        }
    }
    private static void Cancellation(string root, Package package)
    {
        using (var cancellation = new CancellationTokenSource())
        {
            var arm = true;
            using (var fence = new WindowsFileFence(root, operation => { if (operation == "open" && arm) { arm = false; cancellation.Cancel(); } }))
            {
                try { package.Create(fence, cancellation.Token); throw new Exception("Interrupted stage accepted"); }
                catch (OperationCanceledException) { Check(true, "mid-write cancellation returns no Ready stage"); }
            }
        }
        var candidates = WorkDirectories(root, "stage");
        Check(candidates.Length == 1 && Directory.GetFiles(candidates[0]).Length < 4, "cancelled private partial stage retained without complete candidate");
    }
    private static void PublicAcl(string path)
    {
        // Mutate only a newly-created fixture directory; production has no existing-ACL setter.
        IntPtr descriptor; uint length;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;OICI;FA;;;WD)", 1, out descriptor, out length)) throw new Exception("Fixture ACL construction");
        try
        {
            bool present, defaulted; IntPtr dacl;
            if (!GetSecurityDescriptorDacl(descriptor, out present, out dacl, out defaulted)
                || !present || SetNamedSecurityInfoW(path, 1, 4 | 0x80000000U, IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero) != 0)
                throw new Exception("Fixture ACL mutation failed");
        }
        finally { LocalFree(descriptor); }
    }
    private static void AclChanges(string root, Package package)
    {
        using (var fence = new WindowsFileFence(root))
        {
            var staged = package.Create(fence);
            using (var locked = UpdateStaging.VerifyLocked(fence, staged, package.Trust, Context))
            {
                PublicAcl(Path.Combine(root, staged.Directory));
                Reject(() => locked.Recheck(), "live staged ACL change invalidates verified lease");
            }
            Reject(() => { using (UpdateStaging.VerifyLocked(fence, staged, package.Trust, Context)) { } }, "public stage ACL rejected without repair");
        }
        var armed = false; StagedUpdate raced;
        using (var creator = new WindowsFileFence(root)) raced = package.Create(creator);
        using (var fence = new WindowsFileFence(root, operation => { if (armed && operation == "open") { armed = false; PublicAcl(Path.Combine(root, raced.Directory)); } }))
        {
            armed = true;
            Reject(() => { using (UpdateStaging.VerifyLocked(fence, raced, package.Trust, Context)) { } }, "ACL changed after initial check before read never returns Ready");
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new Exception("Fixture needs fresh isolated base");
            var baseDirectory = UpdaterFixtureScope.Require(args[0]);
            if (!baseDirectory.EndsWith("\\fixtures", StringComparison.OrdinalIgnoreCase) || Directory.Exists(baseDirectory))
                throw new Exception("Only a fresh authorized staging fixture directory is allowed");
            var root = Path.Combine(baseDirectory, "cache"); Directory.CreateDirectory(Path.Combine(root, "existing"));
            File.WriteAllText(Path.Combine(root, "existing", "keep.txt"), "keep");
            using (var package = new Package())
            {
                using (var fence = new WindowsFileFence(root)) { Initial(fence, root, package); Valid(fence, root, package); Tampering(fence, root, package); }
                var replacement = Path.Combine(baseDirectory, "replacement"); Directory.CreateDirectory(replacement); Replacement(replacement, package);
                var cancellation = Path.Combine(baseDirectory, "cancel"); Directory.CreateDirectory(cancellation); Cancellation(cancellation, package);
                var acls = Path.Combine(baseDirectory, "acl-cases"); Directory.CreateDirectory(acls); AclChanges(acls, package);
            }
            Console.WriteLine("PASS: private staging " + _checks + " assertions; ACL changes only for newly-created fixture directories; locked bytes/signature/ZIP/identity/cancellation; no install, extraction, credentials or processes.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (error is ApplySafetyException safety) Console.Error.WriteLine("Fixture native error: " + safety.NativeError);
            return 1;
        }
    }
}
