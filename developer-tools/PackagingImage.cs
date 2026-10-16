using BetterAstralParty.Updating;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static class PackagingImage
{
    internal static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    { foreach (var type in source) { yield return type; foreach (var child in Types(type.NestedTypes)) yield return child; } }
    internal static string PluginVersion(byte[] bytes)
    {
        var metadata = InstalledPluginMetadata.ReadMaintenance(bytes);
        if (metadata.UpdateProtocol != 4 || metadata.SettingsSchema != 1) throw new InvalidDataException("Native 1.3.0 maintenance generation required.");
        return PackagingBoundary.CanonicalVersion(metadata.Version);
    }
    internal static object Inspect(byte[] bytes, string role, string version)
    {
        using var stream = new MemoryStream(bytes, false); using var assembly = AssemblyDefinition.ReadAssembly(stream);
        var types = Types(assembly.MainModule.Types).ToArray(); bool? credentialInput = null;
        if (role == "Plugin") {
            if (PluginVersion(bytes) != version) throw new InvalidDataException("Plugin version mismatch.");
            foreach (var name in new[] { "BetterAstralParty.AutomaticUpdates", "BetterAstralParty.MemoryReleaseAuthentication", "BetterAstralParty.PrivateUpdateApproval" })
                if (types.Count(type => type.FullName == name) != 1) throw new InvalidDataException("Native auth/update integration missing.");
            var gate = types.Single(type => type.FullName == "BetterAstralParty.PrivateUpdateApproval").Methods.Single(method => method.Name == "get_CredentialInputEnabled");
            var instructions = gate.Body.Instructions;
            if (instructions.Count != 2 || instructions[1].OpCode.Name != "ret" || instructions[0].OpCode.Name is not ("ldc.i4.0" or "ldc.i4.1")) throw new InvalidDataException("Unknown credential gate IL.");
            credentialInput = instructions[0].OpCode.Name == "ldc.i4.1";
        } else if (role == "Launcher") {
            var launcher = types.Single(type => type.FullName == "SteamLauncher");
            if (launcher.Fields.Single(field => field.Name == "InstallBundleProtocol" && field.IsLiteral).Constant is not int protocol || protocol != 1) throw new InvalidDataException("Launcher maintenance protocol mismatch.");
            foreach (var name in new[] { "BetterAstralParty.Installation.NativeMaintenanceEntry", "BetterAstralParty.Installation.NativeInstall", "BetterAstralParty.Installation.NativeUninstall" })
                if (types.Count(type => type.FullName == name) != 1) throw new InvalidDataException("Native maintenance closure missing.");
            foreach (var resource in assembly.MainModule.Resources) {
                if (resource.Name.StartsWith("BetterAstralParty.HelperBootstrap.", StringComparison.Ordinal)) throw new InvalidDataException("Historical embedded helper forbidden.");
                if (resource is EmbeddedResource embedded) { var data = embedded.GetResourceData(); if (data.Length >= 2 && data[0] == 77 && data[1] == 90) throw new InvalidDataException("Embedded executable rejected."); }
            }
        } else if (role == "Helper") {
            foreach (var name in new[] { "BetterAstralParty.Updating.UpdateHelperProgram", "BetterAstralParty.Updating.UpdateHelperService" })
                if (types.Count(type => type.FullName == name) != 1) throw new InvalidDataException("Helper entrypoint/closure missing.");
        } else throw new ArgumentException("Unknown image role.");
        if (role is "Launcher" or "Helper") BuildVersion.ValidateNative(bytes, version);
        foreach (var type in types) {
            if (System.Text.RegularExpressions.Regex.IsMatch(type.FullName, "SettingsTraceProbe|SettingsTracePhase|Fixture|InertProcess|TrialVersionCheck|ReleaseSessionCheck")) throw new InvalidDataException("Fixture type entered product.");
            foreach (var method in type.Methods.Where(method => method.HasBody)) foreach (var instruction in method.Body.Instructions) {
                if (instruction.OpCode.Name == "ldstr" && instruction.Operand is string marker && System.Text.RegularExpressions.Regex.IsMatch(marker, "GS-20261003-B1|settingsProbe|fixture-public-key.bin|INERT_TEST_ONLY|fixture-placeholder-only")) throw new InvalidDataException("Fixture marker entered product.");
                if (instruction.Operand is MethodReference called && called.Name is "SignData" or "SignHash" or "ImportRSAPrivateKey" or "ImportPkcs8PrivateKey" or "ExportRSAPrivateKey") throw new InvalidDataException("Private key/signing operation entered product.");
            }
        }
        return new { role, credentialInputCompiled = credentialInput, diagnosticOrFixtureTypesAndMarkersAbsent = true, privateSigningCallsAbsent = true };
    }
}
