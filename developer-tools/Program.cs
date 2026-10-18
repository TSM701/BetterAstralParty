namespace BetterAstralParty.Development;

internal static class Program
{
    private static int Main(string[] args)
    {
        try {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            if (args.Length < 3 || args[0] != "--project-root") throw new ArgumentException("Run Developer.cmd from the canonical project.");
            var context = new DevContext(args[1]); var command = args[2]; var values = args.Skip(3).ToArray();
            if (command == "help" || command == "--help") {
                Console.WriteLine("Developer.cmd build|build-setup|compatibility-snapshot|test|migration-status|" + string.Join("|", PackagingCommands.Names) + " [options]");
                Console.WriteLine("C# developer workflows only. Build does not install or publish. Signing requires a fresh reviewed policy; no certificate is generated automatically.");
                Console.WriteLine("Build/test --native-assembly <path> selects an exact read-only native metadata snapshot; omitted uses the historical regression snapshot, invalid explicit inputs never fall back.");
                Console.WriteLine("Build --checks <comma-separated names> runs targeted validation and records only those passes; sdk-path-safety is always included. Omit for the full suite.");
                return 0;
            }
            if (command == "build") return BuildCommands.Run(context, values);
            if (command == "build-setup") return BuildCommands.RunNative(context, "Setup", values);
            if (command == "test") return TestCommands.Run(context, values);
            if (command == "compatibility-snapshot") {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(CompatibilitySnapshot.Run(context, values), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if (command == "migration-status" && values.Length == 0) {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
                    requiredBuildChecks = BuildCommands.Checks,
                    runnableCSharpChecks = TestCommands.Supported,
                    pendingChecks = BuildCommands.Checks.Where(name => !TestCommands.Supported.Contains(name, StringComparer.Ordinal)).ToArray(),
                    requiredChecksImplemented = BuildCommands.Checks.All(name => TestCommands.Supported.Contains(name, StringComparer.Ordinal)),
                    validationExecutedByThisCommand = false, packageApprovalRequiresSeparateSignedValidation = true,
                    authenticodeSigningCompleted = false,
                    installationPerformed = false, publicationPerformed = false
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            return PackagingCommands.Run(context, command, values);
        } catch (Exception error) {
            Console.Error.WriteLine("BAP-DEVELOPMENT-FAILED: " + error.Message);
            return 1;
        }
    }
}
