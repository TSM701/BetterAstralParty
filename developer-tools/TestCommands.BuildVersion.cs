using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void BuildVersions(DevContext context, string stage)
    {
        var source = Encoding.UTF8.GetBytes("// 원문과 CRLF 유지\r\n    public const string Version = \"1.3.0-rc.3.local.12\";\r\n// unchanged\r\n");
        var baseline = source.ToArray(); var originalVersion = BuildVersion.SourceVersion(source);
        JsonElement Record(string? version, object? generated) => JsonSerializer.SerializeToElement(new { versionOverride = version, generatedPlugin = generated });
        BuildVersion.Validate(source, Record(null, null), originalVersion);
        BuildVersion.Validate(source, JsonSerializer.SerializeToElement(new { }), originalVersion);
        Check(source.SequenceEqual(baseline), "Default and historical build records retain the source version without generated source");
        foreach (var version in new[] { "1.3.0", "1.3.1-rc.2", "2.0.0+build.4" }) {
            var generated = BuildVersion.Transform(source, version);
            var row = new PackagingInput(BuildVersion.GeneratedPath, generated.LongLength, PackagingBoundary.Digest(generated));
            var record = Record(version, row);
            BuildVersion.Validate(source, record, version, generated);
            Check(Encoding.UTF8.GetString(generated) == Encoding.UTF8.GetString(source).Replace(originalVersion, version, StringComparison.Ordinal)
                && source.SequenceEqual(baseline), "Only the version literal changes; original bytes, Unicode and newlines are preserved: " + version);
            RefuseDistribution(() => BuildVersion.Validate(source, record, version, generated.Concat(new byte[] { 32 }).ToArray()), "Generated source byte tampering rejected");
            RefuseDistribution(() => BuildVersion.Validate(source, record, originalVersion), "Product version must match override");
            RefuseDistribution(() => BuildVersion.Validate(source, Record(version, row with { path = "other/Plugin.cs" }), version), "Generated source path is fixed");
            RefuseDistribution(() => BuildVersion.Validate(source, Record(version, row with { bytes = row.bytes + 1 }), version), "Generated source length is bound");
            RefuseDistribution(() => BuildVersion.Validate(source, Record(version, row with { sha256 = new string('A', 64) }), version), "Generated source hash is bound");
            RefuseDistribution(() => BuildVersion.Validate(source, Record(version, null), version), "Override requires generated source metadata");
        }
        foreach (var version in new[] { "", "v1.3.0", "01.3.0", "1.3.0-01", "65536.0.0", "1.3.0\n", "1.3.0\"; injected" })
            RefuseDistribution(() => BuildVersion.Transform(source, version), "Noncanonical, unbounded or injected version rejected");
        RefuseDistribution(() => BuildVersion.Transform(Encoding.UTF8.GetBytes("missing version"), "1.3.0"), "Missing version literal rejected");
        RefuseDistribution(() => BuildVersion.Transform(source.Concat(source).ToArray(), "1.3.0"), "Duplicate version literal rejected");
        RefuseDistribution(() => BuildVersion.Validate(source, Record(null, new PackagingInput(BuildVersion.GeneratedPath, 1, new string('A', 64))), originalVersion), "Default build cannot claim generated source");
        RefuseDistribution(() => BuildVersion.Validate(source, Record(null, null), "1.3.0"), "Default build cannot silently change product version");
        var canonicalPath = Path.Combine(context.Root, "src", "Plugin.cs"); var before = context.Hash(canonicalPath);
        foreach (var version in new string?[] { null, "1.3.0" }) {
            var directory = Path.Combine(stage, version == null ? "native-default" : "native-override"); Directory.CreateDirectory(directory);
            var identity = File.ReadAllText(BuildCommands.Identity(context.Root, directory, "Launcher", version));
            var expected = version ?? BuildVersion.SourceVersion(File.ReadAllBytes(canonicalPath));
            Check(identity.Contains("AssemblyInformationalVersion(\"" + expected + "\")", StringComparison.Ordinal), "Native identity uses the same default/override version");
        }
        Check(context.Hash(canonicalPath) == before, "Version preparation never changes canonical Plugin.cs");
        var project = XDocument.Load(Path.Combine(context.Root, "src", "BetterAstralParty.csproj"));
        var group = project.Descendants("ItemGroup").Single(row => row.Attribute("Condition")?.Value == "'$(BapVersionOverrideSource)' != ''");
        Check(group.Elements("Compile").Single(row => row.Attribute("Remove") != null).Attribute("Remove")!.Value == "Plugin.cs"
            && group.Elements("Compile").Single(row => row.Attribute("Include") != null).Attribute("Include")!.Value == "$(BapVersionOverrideSource)", "MSBuild substitutes only the bounded generated Plugin source; default source remains enabled otherwise");
        using var image = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("NativeVersionFixture", new Version(1, 3, 0, 0)), "NativeVersionFixture", ModuleKind.Console);
        var constructor = typeof(System.Reflection.AssemblyInformationalVersionAttribute).GetConstructor(new[] { typeof(string) })!;
        var attribute = new CustomAttribute(image.MainModule.ImportReference(constructor));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(image.MainModule.TypeSystem.String, "1.3.0")); image.CustomAttributes.Add(attribute);
        byte[] Image() { using var stream = new MemoryStream(); image.Write(stream); return stream.ToArray(); }
        BuildVersion.ValidateNative(Image(), "1.3.0"); Check(true, "Actual metadata validator accepts matching native informational and assembly versions");
        RefuseDistribution(() => BuildVersion.ValidateNative(Image(), "1.3.1"), "Native metadata version drift rejected");
        context.WriteJsonCreateNew(Path.Combine(stage, "version-override-check.json"), new { sourceUnchanged = true, actualTransformAndRecordValidator = true, nativeMetadataValidated = true, compilationExecuted = false, privateKeyAccessed = false, installationPerformed = false });
    }
}
