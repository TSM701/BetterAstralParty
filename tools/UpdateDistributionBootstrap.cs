#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Packaging
{
    internal static class TrustedBootstrap
    {
        internal static int Run(string gameRoot, string bundleRoot, string independentlyTrustedHash, UpdateTrust trust)
        {
            if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            if (!Regex.IsMatch(independentlyTrustedHash, @"\A[0-9A-Fa-f]{64}\z")) throw new ArgumentException("Expected independent helper SHA256");
            using (var bundle = new WindowsFileFence(bundleRoot)) {
                if (string.Equals(bundle.Root, Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Extract the bootstrap outside the game root");
                byte[] Read(string path, long maximum) { using (var file = bundle.OpenFile(path)) return UpdateTicket.Read(file, maximum); }
                var descriptor = Read("helper.descriptor", UpdateTrust.MaxDescriptorBytes);
                var envelope = DetachedSignature.Parse(Read("helper.signature", 1024));
                var signed = HelperDescriptor.Verify(trust, descriptor, envelope, Read(HelperDescriptor.Image, UpdateTrust.MaxFileBytes));
                using (var image = bundle.OpenLaunchImage(HelperDescriptor.Image)) {
                    if (image.Hash() != signed.Sha256 || signed.Sha256 != independentlyTrustedHash.ToUpperInvariant())
                        throw new UpdateValidationException(UpdateFailure.InvalidSignature);
                    var start = new ProcessStartInfo(image.Path) { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add("--install-helper"); start.ArgumentList.Add(Path.GetFullPath(gameRoot)); start.ArgumentList.Add(bundle.Root);
                    // Parent pins and read-only image lease prevent path swaps until the trusted helper exits.
                    using (var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start trusted bootstrap")) {
                        process.WaitForExit(); return process.ExitCode;
                    }
                }
            }
        }
    }
}
