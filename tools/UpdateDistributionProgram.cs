#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Packaging
{
    internal static class UpdateDistributionProgram
    {
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--help") {
                Console.WriteLine("Offline: --prepare|--finalize <source root> <new output root> <current version> <release tag> <Stable|Beta> <release id> <ZIP asset id>");
                Console.WriteLine("Prepare reads eight owned files plus helper. Finalize reads prepared request plus update.signature/helper.signature and requires independently compiled public trust.");
                Console.WriteLine("First bootstrap: --bootstrap <game root> <extracted bundle root> <independently trusted helper SHA256>");
                return 0;
            }
            var bootstrap = args.Length == 4 && args[0] == "--bootstrap";
            if (!bootstrap && (args.Length != 8 || (args[0] != "--prepare" && args[0] != "--finalize"))) return 64;
            try {
                var trust = UpdateTrust.Production();
                if (bootstrap) return TrustedBootstrap.Run(args[1], args[2], args[3], trust);
                // Reject unavailable operational finalization before any filesystem access.
                if (args[0] == "--finalize" && !trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
                var context = new UpdateContext("TSM701/BetterAstralParty", args[5], args[3], args[4],
                    long.Parse(args[6], NumberStyles.None, CultureInfo.InvariantCulture),
                    long.Parse(args[7], NumberStyles.None, CultureInfo.InvariantCulture), UpdateTrust.PlatformId);
                Dictionary<string, byte[]> files;
                using (var source = new WindowsFileFence(args[1])) {
                    byte[] Read(string name, long maximum) { using (var file = source.OpenFile(name)) return UpdateTicket.Read(file, maximum); }
                    if (args[0] == "--prepare") {
                        var inputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                        foreach (var path in InstallReceipt.Owned) inputs.Add(path, Read(path, UpdateTrust.MaxFileBytes));
                        var request = UpdateDistribution.Prepare(context, inputs, Read(HelperDescriptor.Image, UpdateTrust.MaxFileBytes));
                        files = new Dictionary<string, byte[]>(StringComparer.Ordinal) {
                            { "payload.zip", request.Zip }, { "update.descriptor", request.Descriptor },
                            { "helper.descriptor", request.HelperDescriptor }, { HelperDescriptor.Image, request.HelperImage },
                            { "UNSIGNED.txt", Encoding.UTF8.GetBytes("Unsigned local signing request. Not installable or publishable.\nOnly independently approved external signatures and compiled public trust may finalize it.\n") }
                        };
                    } else {
                        files = UpdateDistribution.Finalize(UpdateDistribution.ReadRequest(source, context),
                            Read("update.signature", 1024), Read("helper.signature", 1024), trust);
                    }
                }
                var outputPath = Path.GetFullPath(args[2]);
                var parent = Path.GetDirectoryName(outputPath) ?? throw new ArgumentException("Output needs a parent directory");
                var leaf = Path.GetFileName(outputPath);
                using (var destinationParent = new WindowsFileFence(parent)) {
                    // CREATE_NEW semantics: never overwrite/adopt a prior output directory.
                    destinationParent.CreatePrivateDirectory(leaf);
                    using (var destination = new WindowsFileFence(outputPath)) UpdateDistribution.WriteNew(destination, files);
                }
                Console.WriteLine(args[0] == "--prepare" ? "BAP-PACKAGE:UnsignedRequest" : "BAP-PACKAGE:VerifiedAssets");
                return 0;
            } catch (UpdateValidationException error) { Console.Error.WriteLine("BAP-PACKAGE:" + error.Failure); return 2; }
            catch (ApplySafetyException error) { Console.Error.WriteLine("BAP-PACKAGE:" + error.Failure); return 5; }
            catch (Exception error) when (error is ArgumentException || error is FormatException || error is OverflowException) {
                Console.Error.WriteLine("BAP-PACKAGE:InvalidArguments"); return 64;
            }
            catch { Console.Error.WriteLine("BAP-PACKAGE:Failed"); return 6; }
        }
    }
}
