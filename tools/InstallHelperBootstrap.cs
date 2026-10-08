#nullable enable
using System;
using System.IO;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    // Called only from the approved incoming launcher; never loads installed executable inspection code.
    internal static class InstallHelperBootstrap
    {
        private sealed class Inputs : IDisposable
        {
            internal readonly WindowsFileFence Fence;
            internal readonly WindowsFileFence.FileLease Image;
            private readonly WindowsFileFence.FileLease _descriptor, _signature;
            internal readonly byte[] Descriptor, Signature, ImageBytes;
            internal Inputs(string bundleRoot, string version, string expectedHash)
            {
                if (UpdateVersion.Parse(version).Tag != version ||
                    expectedHash.Length != 64 || expectedHash != expectedHash.ToUpperInvariant())
                    throw new ArgumentException("Exact canonical helper version and hash required.");
                foreach (var c in expectedHash) if (!(c >= '0' && c <= '9' || c >= 'A' && c <= 'F'))
                    throw new ArgumentException("Invalid helper hash.");
                Fence = new WindowsFileFence(bundleRoot);
                WindowsFileFence.FileLease? image = null, descriptor = null, signature = null;
                try {
                    descriptor = Fence.OpenLaunchImage("helper.descriptor");
                    signature = Fence.OpenLaunchImage("helper.signature");
                    image = Fence.OpenLaunchImage(HelperDescriptor.Image);
                    // Read sharing keeps the verified complete-package leases compatible while denying writes/deletes.
                    Descriptor = UpdateTicket.Read(descriptor, UpdateTrust.MaxDescriptorBytes);
                    Signature = UpdateTicket.Read(signature, 1024);
                    ImageBytes = UpdateTicket.Read(image, UpdateTrust.MaxFileBytes);
                    var signed = HelperDescriptor.Verify(UpdateTrust.Production(), Descriptor,
                        DetachedSignature.Parse(Signature), ImageBytes, version);
                    if (signed.Sha256 != expectedHash || image.Hash() != expectedHash)
                        throw new UpdateValidationException(UpdateFailure.WrongTarget);
                    Image = image; _descriptor = descriptor; _signature = signature;
                } catch { image?.Dispose(); descriptor?.Dispose(); signature?.Dispose(); Fence.Dispose(); throw; }
            }
            public void Dispose() { Image.Dispose(); _signature.Dispose(); _descriptor.Dispose(); Fence.Dispose(); }
        }

        internal static string Validate(string bundleRoot, string version, string expectedHash)
        {
            using (var inputs = new Inputs(bundleRoot, version, expectedHash)) return inputs.Image.Hash();
        }

        internal static int Install(string root, string bundleRoot, string version, string expectedHash)
        {
            using (var inputs = new Inputs(bundleRoot, version, expectedHash))
            using (var target = new WindowsFileFence(root)) {
                if (String.Equals(inputs.Fence.Root, target.Root, StringComparison.OrdinalIgnoreCase) ||
                    inputs.Fence.Root.StartsWith(target.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    target.Root.StartsWith(inputs.Fence.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Helper bundle and game root must be separate.");
                UpdateLaunchGate.Check(target);
                if (!HelperDelivery.Eligible(target)) return 4;
                using (var plugin = target.OpenFile(UpdateTrust.PluginPath))
                    if (InstalledPluginMetadata.Read(UpdateTicket.Read(plugin, UpdateTrust.MaxFileBytes)).Version != version)
                        throw new UpdateValidationException(UpdateFailure.WrongTarget);
                WindowsProcessGuard.AssertNoRunning(new[] { target.Full("AstralParty_INT.exe"),
                    target.Full("BetterAstralParty-Launcher.exe"), target.Full(HelperDescriptor.Image) });
                // Use the approved incoming launcher's helper engine with the exact pinned bytes.
                // No installed executable or downloaded helper Main is invoked. The engine takes
                // the native update lock and repeats receipt, signature, process and journal checks.
                var result = HelperDelivery.Install(target, inputs.Descriptor, inputs.Signature,
                    inputs.ImageBytes, UpdateTrust.Production());
                return result == HelperInstallResult.Installed || result == HelperInstallResult.AlreadyInstalled
                    ? 0 : result == HelperInstallResult.ManualUpgradeRequired ? 4 : 9;
            }
        }
    }
}
