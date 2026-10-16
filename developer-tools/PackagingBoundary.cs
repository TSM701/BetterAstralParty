using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Development;

internal sealed class PackagingBoundary : IDisposable
{
    internal const string ArtifactRoot = "artifacts/automatic-update-development/";
    internal readonly string Root;
    internal readonly DevelopmentPackageFiles Files;
    internal string? OutputIdentity { get; private set; }
    private readonly HashSet<string> captured = new(StringComparer.Ordinal);
    private readonly HashSet<string> directories = new(StringComparer.Ordinal);
    internal PackagingBoundary(string root) { Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); Files = new DevelopmentPackageFiles(Root); }
    internal static void PathName(string path)
    {
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || Regex.IsMatch(path, "[\\\\<>:\"|?*\\x00-\\x1F]")) throw new InvalidDataException("Unsafe package path.");
        foreach (var part in path.Split('/'))
            if (part.Length == 0 || part != part.Trim() || part.EndsWith('.') || part is "." or ".." || Regex.IsMatch(part, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Package path alias rejected.");
    }
    internal string Relative(string path, bool artifact = true)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Project-contained path required.");
        var result = full[(Root.Length + 1)..].Replace('\\', '/'); PathName(result);
        if (artifact && !result.StartsWith("artifacts/", StringComparison.Ordinal)) throw new InvalidDataException("Isolated artifact path required.");
        return result;
    }
    internal string Stage(string path) { var result = Relative(path); if (!result.StartsWith(ArtifactRoot, StringComparison.Ordinal)) throw new InvalidDataException("Private artifact boundary required."); return result; }
    internal static void Separate(string input, string output)
    {
        if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || input.StartsWith(output + '/', StringComparison.OrdinalIgnoreCase) || output.StartsWith(input + '/', StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input/output overlap rejected.");
    }
    internal byte[] Capture(string path, string? expectedHash = null, long? expectedSize = null)
    {
        PathName(path); if (captured.Add(path)) Files.Capture(path);
        if (expectedHash != null) { Hash(expectedHash); if (Files.SourceHash(path) != expectedHash) throw new InvalidDataException("Pinned input changed: " + path); }
        var bytes = Files.ReviewBytes(path);
        if (expectedSize != null && bytes.LongLength != expectedSize) throw new InvalidDataException("Pinned input length changed: " + path);
        return bytes;
    }
    internal JsonElement Json(string path, string? hash = null) => Parse(Capture(path, hash));
    internal static JsonElement Parse(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Packaging JSON bound.");
        var span = bytes.AsSpan(); if (span.Length >= 3 && span[0] == 239 && span[1] == 187 && span[2] == 191) span = span[3..];
        using var document = JsonDocument.Parse(span.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        Validate(document.RootElement); return document.RootElement.Clone();
    }
    private static void Validate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var count = 0;
            foreach (var field in value.EnumerateObject()) { if (++count > 4096 || !names.Add(field.Name)) throw new InvalidDataException("Duplicate or excessive JSON fields."); Validate(field.Value); }
        } else if (value.ValueKind == JsonValueKind.Array) {
            if (value.GetArrayLength() > 8192) throw new InvalidDataException("JSON array bound."); foreach (var item in value.EnumerateArray()) Validate(item);
        } else if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 32767) throw new InvalidDataException("JSON text bound.");
    }
    internal static string Text(JsonElement value, string key) => value.GetProperty(key).ValueKind == JsonValueKind.String ? value.GetProperty(key).GetString()! : throw new InvalidDataException("JSON text required: " + key);
    internal static int Number(JsonElement value, string key) => value.GetProperty(key).GetInt32();
    internal static bool Flag(JsonElement value, string key) => value.GetProperty(key).GetBoolean();
    internal static byte[] Bytes(object value) => JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
    internal static string Digest(byte[] bytes) => UpdateTrust.Hash(bytes);
    internal static void Hash(string value) { if (!Regex.IsMatch(value, "\\A[A-F0-9]{64}\\z")) throw new InvalidDataException("Exact uppercase SHA-256 required."); }
    internal static string CanonicalVersion(string value) { var parsed = UpdateVersion.Parse(value); if (parsed.Tag != value) throw new InvalidDataException("Canonical version required."); return value; }
    internal void Fresh(string path) { if (Directory.Exists(Path.Combine(Root, path)) || File.Exists(Path.Combine(Root, path))) throw new IOException("Existing output retained; fresh output required."); }
    internal void Output(string path) { Fresh(path); OutputIdentity = Files.CreateOutput(path); }
    internal void Parents(string path)
    {
        var parts = path.Split('/'); var parent = "";
        foreach (var part in parts.Take(parts.Length - 1)) { parent = parent.Length == 0 ? part : parent + '/' + part; if (directories.Add(parent)) Files.CreateSubdirectory(parent); }
    }
    internal void Write(string path, byte[] bytes) { PathName(path); Parents(path); Files.Write(path, bytes); }
    internal void Copy(string source, string destination) { PathName(destination); Parents(destination); Files.CopySource(source, destination); }
    internal string[] Inventory(string path, int maximum = 8192)
    {
        PathName(path); var full = Path.Combine(Root, path); var result = new List<string>(); var pending = new Stack<string>(); var visited = 0; pending.Push(full);
        while (pending.Count != 0) {
            var directory = pending.Pop();
            if (++visited > maximum) throw new InvalidDataException("Input directory inventory bound.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked input directory rejected.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
                var attributes = File.GetAttributes(entry); if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked input rejected.");
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); if (pending.Count + result.Count > maximum) throw new InvalidDataException("Input inventory bound."); }
                else { var relative = Relative(entry, false); PathName(relative); result.Add(relative); if (result.Count > maximum) throw new InvalidDataException("Input inventory bound."); }
            }
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
    internal static Dictionary<string, PackagingInput> Rows(JsonElement spec, int minimum = 1, int maximum = 1024)
    {
        var values = spec.GetProperty("files"); if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() < minimum || values.GetArrayLength() > maximum) throw new InvalidDataException("Bounded input inventory required.");
        var result = new Dictionary<string, PackagingInput>(StringComparer.Ordinal); var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var row in values.EnumerateArray()) {
            var path = Text(row, "path"); PathName(path); var hash = Text(row, "sha256"); Hash(hash); var size = row.GetProperty("bytes").GetInt64();
            if (size <= 0 || size > 64 * 1024 * 1024 || (total += size) > 256 * 1024 * 1024 || !aliases.Add(path)) throw new InvalidDataException("Input alias/expansion bound.");
            result.Add(path, new PackagingInput(path, size, hash));
        }
        return result;
    }
    public void Dispose() => Files.Dispose();
}

internal sealed record PackagingInput(string path, long bytes, string sha256);
internal sealed record PackagingCaptured(string Source, byte[] Bytes, string Hash);
