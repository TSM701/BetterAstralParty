using System.Text.Json;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Publishing;

// Credential-free, read-only preparation tool. There is deliberately no private-key API here.
internal static class PublisherDraft
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "--plan")
        {
            Console.WriteLine(JsonSerializer.Serialize(new {
                operationalSigning = false, createsKeys = false, readsPrivateKeys = false, publishes = false,
                provider = "Microsoft Software Key Storage Provider", scope = "CurrentUser",
                proposedName = "BetterAstralParty.PrivateTrial.20261002", bits = 3072, exponent = 65537,
                signingOnly = true, exportPolicy = 0, algorithm = "RSA-PSS/SHA-256", saltBytes = 32,
                keyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Crypto", "Keys"),
                proposedToolDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty-Publisher"),
                publicKeyId = "bap-trial-20261002", fingerprint = "SHA256(DER SubjectPublicKeyInfo)",
                commands = new[] { "--plan", "--review-public <key-id> <PUBLIC modulus-base64> <expected-SPKI-SHA256>" }
            }));
            return 0;
        }
        if (args.Length == 4 && args[0] == "--review-public")
        {
            try { ProductionUpdateKeys.Review(args[1], args[2], args[3]); Console.WriteLine("BAP-PUBLIC-PIN:Matched"); return 0; }
            catch { Console.Error.WriteLine("BAP-PUBLIC-PIN:Rejected"); return 2; }
        }
        // Rejected before filesystem access. No switch can authorize operations in this draft.
        if (args.Length != 0 && args[0] is "--create-key" or "--sign" or "--export-private" or "--publish")
        { Console.Error.WriteLine("BAP-PUBLISHER:SecurityApprovalAndOperationalImplementationRequired"); return 3; }
        Console.Error.WriteLine("BAP-PUBLISHER:InvalidArguments"); return 64;
    }
}
