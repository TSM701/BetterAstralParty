using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static class BuildVersion
{
    internal const string GeneratedPath = "version-override/Plugin.cs";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex Literal = new("(?m)^    public const string Version = \"(?<version>[^\"\\r\\n]+)\";\\r?$", RegexOptions.CultureInvariant);

    internal static string Canonical(string value)
    {
        if (!UpdateVersion.TryParse(value, out var version) || version == null || version.Tag != value
            || version.Major > 65535 || version.Minor > 65535 || version.Patch > 65535)
            throw new InvalidDataException("Canonical version with Windows metadata bounds required.");
        return value;
    }

    private static (string Text, Group Version) Source(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 1024 * 1024) throw new InvalidDataException("Bounded Plugin.cs source required.");
        var text = Utf8.GetString(bytes); var matches = Literal.Matches(text);
        if (matches.Count != 1) throw new InvalidDataException("Exactly one canonical Plugin.Version literal required.");
        var version = matches[0].Groups["version"]; Canonical(version.Value); return (text, version);
    }

    internal static string SourceVersion(byte[] bytes) => Source(bytes).Version.Value;
    internal static byte[] Transform(byte[] source, string version)
    {
        Canonical(version); var (text, literal) = Source(source);
        return Utf8.GetBytes(text[..literal.Index] + version + text[(literal.Index + literal.Length)..]);
    }

    private static string? Override(JsonElement record)
    {
        if (!record.TryGetProperty("versionOverride", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Build version override must be text or null.");
        return Canonical(value.GetString()!);
    }

    internal static void Validate(byte[] source, JsonElement record, string productVersion, byte[]? generated = null)
    {
        var version = Override(record); var expected = version ?? SourceVersion(source);
        if (Canonical(productVersion) != expected) throw new InvalidDataException("Build source/override/product version mismatch.");
        var hasGenerated = record.TryGetProperty("generatedPlugin", out var row) && row.ValueKind != JsonValueKind.Null;
        if (version == null) {
            if (hasGenerated || generated != null) throw new InvalidDataException("Default builds cannot claim generated plugin source.");
            return;
        }
        var bytes = Transform(source, version);
        if (!hasGenerated || row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != 3
            || !row.EnumerateObject().Select(field => field.Name).ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "path", "bytes", "sha256" })
            || PackagingBoundary.Text(row, "path") != GeneratedPath || row.GetProperty("bytes").GetInt64() != bytes.LongLength
            || PackagingBoundary.Text(row, "sha256") != PackagingBoundary.Digest(bytes)
            || generated != null && !bytes.SequenceEqual(generated))
            throw new InvalidDataException("Generated Plugin.cs must equal the exact recorded version-only transform.");
    }

    internal static void ValidateBuild(PackagingBoundary boundary, string build, JsonElement record,
        Dictionary<string, PackagingInput> sourceRows, string productVersion)
    {
        if (!sourceRows.TryGetValue("src/Plugin.cs", out var source)) throw new InvalidDataException("Recorded Plugin.cs required.");
        var original = boundary.Capture(source.path, source.sha256, source.bytes);
        Validate(original, record, productVersion);
        if (Override(record) is { } version) {
            var expected = Transform(original, version);
            Validate(original, record, productVersion, boundary.Capture(build + '/' + GeneratedPath, PackagingBoundary.Digest(expected), expected.LongLength));
        }
    }

    internal static void ValidateNative(byte[] bytes, string version)
    {
        Canonical(version); using var stream = new MemoryStream(bytes, false); using var image = AssemblyDefinition.ReadAssembly(stream);
        var informational = image.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == "System.Reflection.AssemblyInformationalVersionAttribute").ToArray();
        var parsed = UpdateVersion.Parse(version);
        if (informational.Length != 1 || informational[0].ConstructorArguments.Count != 1
            || informational[0].ConstructorArguments[0].Value as string != version
            || image.Name.Version != new Version(parsed.Major, parsed.Minor, parsed.Patch, 0))
            throw new InvalidDataException("Native executable build version differs from the plugin build.");
    }
}
