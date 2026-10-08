#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BetterAstralParty.Updating
{
    internal sealed class StagedUpdate
    {
        internal readonly string Directory;
        internal readonly WindowsFileIdentity DirectoryIdentity;
        internal readonly string DescriptorSha256;
        internal StagedUpdate(object stamp, string directory, WindowsFileIdentity identity, string descriptorHash)
        {
            if (!UpdateStaging.ValidStamp(stamp)) throw new ApplySafetyException(ApplyFailure.InvalidState);
            Directory = directory; DirectoryIdentity = identity; DescriptorSha256 = descriptorHash;
        }
    }
    internal sealed class LockedStagedUpdate : IDisposable
    {
        private readonly WindowsFileFence _fence;
        private readonly StagedUpdate _expected;
        private readonly List<WindowsFileFence.FileLease> _files;
        private bool _disposed;
        internal readonly VerifiedUpdatePayload Payload;
        internal LockedStagedUpdate(object stamp, WindowsFileFence fence, StagedUpdate expected,
            List<WindowsFileFence.FileLease> files, VerifiedUpdatePayload payload)
        {
            if (!UpdateStaging.ValidStamp(stamp)) throw new ApplySafetyException(ApplyFailure.InvalidState);
            _fence = fence; _expected = expected; _files = files; Payload = payload;
        }
        internal void Recheck()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LockedStagedUpdate));
            _fence.AssertPrivateDirectory(_expected.Directory);
            if (!_expected.DirectoryIdentity.Same(_fence.DirectoryIdentity(_expected.Directory)))
                throw new ApplySafetyException(ApplyFailure.IdentityChanged);
            foreach (var file in _files) file.Recheck();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; foreach (var file in _files) file.Dispose(); _files.Clear();
        }
    }
    // Staging/reverification only: no extraction, game installation, execution, journal or commit.
    internal static class UpdateStaging
    {
        private static readonly object Stamp = new object();
        internal static bool ValidStamp(object stamp) { return ReferenceEquals(stamp, Stamp); }
        private static string Name(string directory)
        {
            if (!Regex.IsMatch(directory, @"\Abap-stage-[0-9a-f]{32}\z")) throw new ApplySafetyException(ApplyFailure.UnsafePath);
            return directory;
        }
        private static void Write(WindowsFileFence fence, string path, Stream input, CancellationToken cancellation)
        {
            using (var target = fence.OpenFile(path, mutable: true, create: true))
            {
                var buffer = new byte[32768];
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested(); var count = input.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    target.Stream.Write(buffer, 0, count);
                }
                cancellation.ThrowIfCancellationRequested(); target.Flush();
            }
        }
        internal static StagedUpdate Create(WindowsFileFence fence, byte[] descriptorBytes, string keyId, byte[] signatureBytes,
            byte[] zipBytes, UpdateTrust trust, UpdateContext context, CancellationToken cancellation = default(CancellationToken))
        {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            cancellation.ThrowIfCancellationRequested();
            if (descriptorBytes == null || signatureBytes == null) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            var descriptor = (byte[])descriptorBytes.Clone(); var signature = (byte[])signatureBytes.Clone();
            var verified = trust.VerifyDescriptor(descriptor, keyId, signature, context, cancellation);
            var payload = UpdatePackage.VerifyPayload(zipBytes, verified, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var directory = "bap-stage-" + Guid.NewGuid().ToString("N");
            var identity = fence.CreatePrivateDirectory(directory);
            using (var input = new MemoryStream(descriptor, false)) Write(fence, directory + "/descriptor.bin", input, cancellation);
            using (var input = new MemoryStream(signature, false)) Write(fence, directory + "/signature.bin", input, cancellation);
            using (var input = new MemoryStream(Encoding.ASCII.GetBytes(verified.KeyId), false)) Write(fence, directory + "/key-id.bin", input, cancellation);
            using (var input = payload.OpenRead()) Write(fence, directory + "/package.zip", input, cancellation);
            var staged = new StagedUpdate(Stamp, directory, identity, verified.DescriptorSha256);
            using (var locked = VerifyLocked(fence, staged, trust, context, cancellation)) locked.Recheck();
            return staged; // Partial/cancelled staging never reaches this return.
        }
        private static byte[] Read(WindowsFileFence.FileLease file, long limit, CancellationToken cancellation)
        {
            file.Recheck(); var input = file.Stream; input.Position = 0;
            if (input.Length <= 0 || input.Length > limit) throw new UpdateValidationException(UpdateFailure.LimitExceeded);
            using (var output = new MemoryStream())
            {
                var buffer = new byte[32768]; long total = 0;
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested(); var count = input.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    total += count;
                    if (total > limit) throw new UpdateValidationException(UpdateFailure.LimitExceeded);
                    output.Write(buffer, 0, count);
                }
                file.Recheck(); return output.ToArray();
            }
        }
        internal static LockedStagedUpdate VerifyLocked(WindowsFileFence fence, StagedUpdate staged, UpdateTrust trust,
            UpdateContext expectedContext, CancellationToken cancellation = default(CancellationToken))
        {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            cancellation.ThrowIfCancellationRequested(); var directory = Name(staged.Directory);
            fence.AssertPrivateDirectory(directory);
            if (!staged.DirectoryIdentity.Same(fence.DirectoryIdentity(directory))) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
            var files = new List<WindowsFileFence.FileLease>();
            try
            {
                foreach (var name in new[] { "descriptor.bin", "signature.bin", "key-id.bin", "package.zip" })
                    files.Add(fence.OpenFile(directory + "/" + name));
                var descriptorBytes = Read(files[0], UpdateTrust.MaxDescriptorBytes, cancellation);
                var signature = Read(files[1], UpdateTrust.MaxSignatureBytes, cancellation);
                var keyBytes = Read(files[2], 40, cancellation);
                foreach (var value in keyBytes) if (value > 127) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
                var keyId = Encoding.ASCII.GetString(keyBytes);
                if (!Regex.IsMatch(keyId, @"\A[A-Za-z0-9_-]{1,40}\z")) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
                // Caller must derive this context from verified installed metadata/receipt before apply.
                var descriptor = trust.VerifyDescriptor(descriptorBytes, keyId, signature, expectedContext, cancellation);
                if (descriptor.DescriptorSha256 != staged.DescriptorSha256) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
                var zip = Read(files[3], UpdateTrust.MaxZipBytes, cancellation);
                var payload = UpdatePackage.VerifyPayload(zip, descriptor, cancellation);
                var result = new LockedStagedUpdate(Stamp, fence, staged, files, payload); result.Recheck();
                return result; // Lease ownership transfers; the actual on-disk bytes remain locked.
            }
            catch { foreach (var file in files) file.Dispose(); throw; }
        }
    }
}
