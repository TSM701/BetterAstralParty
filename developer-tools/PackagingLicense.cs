using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BetterAstralParty.Development;

internal static class PackagingLicense
{
    internal static object Verify(Dictionary<string, PackagingCaptured> captured, HashSet<string> loader, string upstreamHash)
    {
        foreach (var name in new[] { "LICENSE", "THIRD-PARTY-NOTICES/component-matrix.json", "THIRD-PARTY-NOTICES/SOURCES.md" }) if (!captured.ContainsKey(name)) throw new InvalidDataException("Missing license closure: " + name);
        var license = new UTF8Encoding(false, true).GetString(captured["LICENSE"].Bytes);
        if (!license.Contains("Copyright (c) 2026 TSM701", StringComparison.Ordinal) || !license.Contains("Permission is hereby granted, free of charge", StringComparison.Ordinal)) throw new InvalidDataException("MIT copyright/license missing.");
        var matrix = PackagingBoundary.Parse(captured["THIRD-PARTY-NOTICES/component-matrix.json"].Bytes);
        var components = matrix.GetProperty("components"); var files = matrix.GetProperty("files");
        if (PackagingBoundary.Number(matrix, "schema") != 1 || PackagingBoundary.Text(matrix, "upstreamArchiveSha256") != upstreamHash || components.GetArrayLength() is < 3 or > 64 || files.GetArrayLength() != loader.Count) throw new InvalidDataException("License matrix closure mismatch.");
        var componentNames = new HashSet<string>(StringComparer.Ordinal); var notices = new HashSet<string>(StringComparer.Ordinal); var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components.EnumerateArray()) {
            var name = PackagingBoundary.Text(component, "component"); var componentLicense = PackagingBoundary.Text(component, "license");
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "\\A[A-Za-z0-9.-]{1,64}\\z") || !componentNames.Add(name) || PackagingBoundary.Text(component, "version").Length == 0 || componentLicense.Length == 0 || !PackagingBoundary.Text(component, "sourceUrl").StartsWith("https://github.com/", StringComparison.Ordinal) || component.GetProperty("notices").GetArrayLength() == 0) throw new InvalidDataException("Invalid component license row.");
            foreach (var notice in component.GetProperty("notices").EnumerateArray()) {
                var path = PackagingBoundary.Text(notice, "path"); var hash = PackagingBoundary.Text(notice, "sha256"); PackagingBoundary.PathName(path); PackagingBoundary.Hash(hash);
                if (!path.StartsWith("THIRD-PARTY-NOTICES/", StringComparison.Ordinal) || !captured.TryGetValue(path, out var input) || input.Hash != hash) throw new InvalidDataException("Component notice missing or changed.");
                notices.Add(path);
            }
            if (componentLicense.StartsWith("LGPL", StringComparison.Ordinal)) {
                var source = component.GetProperty("correspondingSource"); var path = PackagingBoundary.Text(source, "path"); var hash = PackagingBoundary.Text(source, "sha256"); PackagingBoundary.PathName(path); PackagingBoundary.Hash(hash);
                if (!path.StartsWith("THIRD-PARTY-SOURCE/", StringComparison.Ordinal) || !path.EndsWith(".zip", StringComparison.Ordinal) || !System.Text.RegularExpressions.Regex.IsMatch(PackagingBoundary.Text(source, "sourceRevision"), "\\A[a-f0-9]{40}\\z") || !PackagingBoundary.Flag(source, "sourceCodeUnmodified") || !captured.TryGetValue(path, out var input) || input.Hash != hash || !sources.Add(path)) throw new InvalidDataException("LGPL corresponding source missing or changed.");
                CheckSource(input.Bytes);
            }
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in files.EnumerateArray()) {
            var path = PackagingBoundary.Text(row, "path"); var hash = PackagingBoundary.Text(row, "sha256"); PackagingBoundary.Hash(hash);
            if (!loader.Contains(path) || !seen.Add(path) || !componentNames.Contains(PackagingBoundary.Text(row, "component")) || !captured.TryGetValue(path, out var input) || input.Hash != hash || row.GetProperty("bytes").GetInt64() != input.Bytes.LongLength || input.Bytes.Length == 0) throw new InvalidDataException("Loader/license matrix mismatch.");
        }
        foreach (var path in captured.Keys) {
            if (path.StartsWith("THIRD-PARTY-SOURCE/", StringComparison.Ordinal) && !sources.Contains(path)) throw new InvalidDataException("Unlisted corresponding source.");
            if (path.StartsWith("THIRD-PARTY-NOTICES/", StringComparison.Ordinal) && path is not ("THIRD-PARTY-NOTICES/component-matrix.json" or "THIRD-PARTY-NOTICES/SOURCES.md") && !notices.Contains(path)) throw new InvalidDataException("Unlisted third-party notice.");
        }
        return new { matrixSha256 = captured["THIRD-PARTY-NOTICES/component-matrix.json"].Hash, loaderFiles = seen.Count, components = componentNames.Count, correspondingSourceArchives = sources.Count, vendorBinarySourceEntriesAbsent = true, independentReviewPending = true };
    }
    private static void CheckSource(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, false); using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        if (archive.Entries.Count is 0 or > 8192) throw new InvalidDataException("Source archive entry bound.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0; var code = false; var license = false;
        foreach (var entry in archive.Entries) {
            var path = entry.FullName.Replace('\\', '/'); if (path.EndsWith('/')) continue; PackagingBoundary.PathName(path);
            if (!names.Add(path) || entry.Length > 16 * 1024 * 1024 || (total += entry.Length) > 64 * 1024 * 1024) throw new InvalidDataException("Source archive alias/expansion bound.");
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".dll" or ".exe" or ".so" or ".dylib" or ".lib" or ".a" or ".pdb" or ".ttf" or ".otf") throw new InvalidDataException("Binary in corresponding source rejected.");
            code |= extension is ".cs" or ".c" or ".cpp" or ".h"; license |= Path.GetFileName(path) is "LICENSE" or "LICENSE.txt" or "COPYING" or "COPYING.txt";
        }
        if (!code || !license) throw new InvalidDataException("Corresponding source lacks code/license.");
    }
}
