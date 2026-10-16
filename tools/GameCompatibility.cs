#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    internal static class GameCompatibility
    {
        internal sealed class Result
        {
            internal readonly bool Allowed;
            internal readonly string[] Issues, Warnings;
            internal readonly Exception? Error;
            internal Result(List<string> issues, List<string> warnings, Exception? error)
            { Allowed = issues.Count == 0; Issues = issues.ToArray(); Warnings = warnings.ToArray(); Error = error; }
        }

        private static string Text(Dictionary<string, object?> value, string name)
        { object? item; return value.TryGetValue(name, out item) && item is string text ? text : ""; }

        private static string Full(string root, string relative)
        { return WindowsPath.Absolute(root.TrimEnd('\\') + "\\" + relative.Replace('/', '\\')); }

        private static bool Exists(string path)
        {
            try {
                if ((File.GetAttributes(WindowsPath.ForIo(path)) & FileAttributes.ReparsePoint) != 0) throw new ApplySafetyException(ApplyFailure.LinkedFile);
                return true;
            } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; }
        }

        private static Dictionary<string, object?> Manifest(string path)
        {
            byte[] bytes;
            using (var stream = File.OpenRead(WindowsPath.ForIo(path)))
            {
                if (stream.Length > 65536) throw new InvalidDataException("Invalid or empty compatibility manifest");
                bytes = new byte[(int)stream.Length];
                var at = 0;
                while (at < bytes.Length) { var read = stream.Read(bytes, at, bytes.Length - at); if (read == 0) throw new EndOfStreamException(); at += read; }
            }
            // PowerShell Get-Content accepts a UTF-8 BOM; the shared receipt parser deliberately does not.
            if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) bytes = bytes.Skip(3).ToArray();
            try
            {
                var value = new InstallReceipt.Json(bytes, 65536, 128, 1024).Parse() as Dictionary<string, object?>;
                object? schema, files;
                if (value == null || !value.TryGetValue("schema", out schema) || !Equals(schema, 1)
                    || !value.TryGetValue("files", out files) || !(files is List<object?> entries) || entries.Count < 5)
                    throw new InvalidDataException("Invalid or empty compatibility manifest");
                return value;
            }
            catch (Exception error) when (error is ApplySafetyException || error is DecoderFallbackException)
            { throw new InvalidDataException("Invalid or empty compatibility manifest", error); }
        }

        internal static Result Check(string gameRoot, string manifestPath, string? loaderRoot = null)
        {
            var issues = new List<string>(); var warnings = new List<string>(); Exception? failure = null;
            WindowsFileFence? gameFence = null, loaderFence = null;
            var held = new List<WindowsFileFence.FileLease>();
            try
            {
                gameRoot = WindowsPath.Absolute(gameRoot); manifestPath = WindowsPath.Absolute(manifestPath);
#if BAP_SAFETY_FIXTURE || BAP_FIXTURE_HELPER
                var cacheRoot = Full(gameRoot, "fixture-cache");
#else
                var cacheRoot = WindowsPath.Absolute(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "feimo", "AstralParty_INT", "com.unity.addressables"));
#endif
                var baseline = Manifest(manifestPath);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var cacheReadable = true;
                foreach (var raw in (List<object?>)baseline["files"]!)
                {
                    var entry = raw as Dictionary<string, object?>;
                    if (entry == null) throw new InvalidDataException("Invalid manifest entry");
                    var scope = Text(entry, "root"); var relative = Text(entry, "path"); var digest = Text(entry, "sha256"); var kind = Text(entry, "kind");
                    var game = string.Equals(scope, "game", StringComparison.OrdinalIgnoreCase);
                    if ((!game && !string.Equals(scope, "cache", StringComparison.OrdinalIgnoreCase)) || relative.Length == 0
                        || Path.IsPathRooted(relative) || Regex.IsMatch(relative, @"(^|[\\/])\.\.([\\/]|$)") || relative.IndexOf(':') >= 0
                        || relative.IndexOf('\\') >= 0 || relative.Split('/').Any(part => !WindowsPath.SafeComponent(part))
                        || kind.Length == 0 || game && UpdateTrust.OwnedPath(relative) || !Regex.IsMatch(digest, @"\A[A-Fa-f0-9]{64}\z")) throw new InvalidDataException("Invalid manifest entry");
                    var key = scope + "/" + relative;
                    if (!seen.Add(key)) throw new InvalidDataException("Duplicate manifest entry");
                    if (!game && !cacheReadable) continue;
                    var mandatory = game && GameCompatibilityPolicy.RequiredGame(relative);
                    WindowsFileFence? observation = null; WindowsFileFence.FileLease? transient = null;
                    try {
                    var selectedRoot = game ? gameRoot : cacheRoot; var bundled = false;
                    var path = Full(game ? gameRoot : cacheRoot, relative);
                    if (!string.IsNullOrEmpty(loaderRoot) && !Exists(Full(gameRoot, "doorstop_config.ini")) && !Exists(path) && game
                        && (string.Equals(relative, "winhttp.dll", StringComparison.OrdinalIgnoreCase)
                            || Regex.IsMatch(relative, @"^BepInEx/core/[^/\\]+\.dll$", RegexOptions.IgnoreCase)))
                    { selectedRoot = WindowsPath.Absolute(loaderRoot!); path = Full(selectedRoot, relative); bundled = true; }
                    if (!Exists(path)) { (mandatory ? issues : warnings).Add("Missing [" + kind + "]: " + key); continue; }
                    WindowsFileFence fence;
                    if (mandatory) {
                        if (bundled) { if (loaderFence == null) loaderFence = new WindowsFileFence(selectedRoot); fence = loaderFence; }
                        else { if (gameFence == null) gameFence = new WindowsFileFence(gameRoot); fence = gameFence; }
                    } else fence = observation = new WindowsFileFence(selectedRoot);
                    var file = fence.OpenFile(relative);
                    if (mandatory) held.Add(file); else transient = file;
                    var stream = file.Stream;
                    {
                        if (game && GameCompatibilityPolicy.RequiredGame(relative) && stream.Length == 0) { issues.Add("Empty required game file: " + key); continue; }
                        var resource = string.Equals(key, "game/AstralParty_INT_Data/data.unity3d", StringComparison.OrdinalIgnoreCase);
                        if (resource)
                        {
                            var header = new byte[8];
                            if (stream.Length < 32 || stream.Read(header, 0, 8) != 8 || Encoding.ASCII.GetString(header) != "UnityFS\0")
                                warnings.Add("Invalid resource bundle: " + key);
                        }
                        stream.Position = 0;
                        using (var hash = SHA256.Create())
                        {
                            if (!string.Equals(UpdateTrust.Hex(hash.ComputeHash(stream)), digest, StringComparison.OrdinalIgnoreCase))
                            {
                                warnings.Add("Changed [" + kind + "]; runtime feature guards remain active: " + key);
                            }
                        }
                        file.Recheck();
                    }
                    } catch (Exception error) when (!mandatory && GameCompatibilityPolicy.CacheReadError(error)) {
                        if (!game) cacheReadable = false;
                        warnings.Add("Optional game/catalog observation unavailable [" + error.GetType().Name + "]; runtime feature guards remain active.");
                    } finally { transient?.Dispose(); observation?.Dispose(); }
                }
                foreach (var required in new[] { "game/AstralParty_INT.exe", "game/GameAssembly.dll", "game/UnityPlayer.dll", "game/winhttp.dll",
                    "game/AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle", "game/AstralParty_INT_Data/data.unity3d", "game/BepInEx/core/BepInEx.Core.dll" })
                    if (!seen.Contains(required)) throw new InvalidDataException("Missing required manifest entry: " + required);
                object? catalogs;
                baseline.TryGetValue("cacheCatalogs", out catalogs);
                var expected = catalogs is List<object?> list ? list.Select(item => item as string ?? "").ToArray() : throw new InvalidDataException("Invalid catalog manifest");
                if (expected.Length == 0 || expected.Length > 128 || expected.Distinct(StringComparer.Ordinal).Count() != expected.Length
                    || expected.Any(name => !Regex.IsMatch(name, @"\Acatalog[^/\\:]*\.(json|hash)\z"))) throw new InvalidDataException("Invalid catalog manifest");
                foreach (var name in expected) if (!seen.Contains("cache/" + name)) throw new InvalidDataException("Invalid catalog manifest");
                var actual = new string[0];
                try {
                    if (cacheReadable && Exists(cacheRoot)) using (var cache = new WindowsFileFence(cacheRoot)) {
                        actual = Directory.EnumerateFiles(WindowsPath.ForIo(cache.Root), "*", SearchOption.TopDirectoryOnly)
                            .Select(WindowsPath.Leaf).Where(name => Regex.IsMatch(name, @"^catalog.*\.(json|hash)$", RegexOptions.IgnoreCase)).Take(513).ToArray();
                        cache.Recheck();
                    }
                }
                catch (Exception error) when (GameCompatibilityPolicy.CacheReadError(error)) { warnings.Add("Downloaded catalog inventory unavailable [" + error.GetType().Name + "]; runtime feature guards remain active."); }
                Array.Sort(actual, StringComparer.CurrentCultureIgnoreCase);
                if ((!seen.Contains("game/AstralParty_INT_Data/StreamingAssets/aa/settings.json") && actual.Length != expected.Length)
                    || !CatalogInventory.Matches(expected, actual))
                    warnings.Add("Changed [catalog]: downloaded catalog inventory; extra=[" + string.Join(", ", actual.Where(name => !expected.Contains(name, StringComparer.Ordinal)))
                        + "]; missing=[" + string.Join(", ", expected.Where(name => !actual.Contains(name, StringComparer.Ordinal))) + "]");
                foreach (var file in held) file.Recheck();
                gameFence?.Recheck(); loaderFence?.Recheck();
            }
            catch (Exception error) { failure = error; issues.Add("Check failed: " + error.Message); }
            finally { foreach (var file in held) file.Dispose(); loaderFence?.Dispose(); gameFence?.Dispose(); }
            return new Result(issues, warnings, failure);
        }
    }
}
