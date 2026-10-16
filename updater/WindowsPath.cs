#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Identities stay ordinary drive paths; the extended namespace is derived only at I/O boundaries.
    internal static class WindowsPath
    {
        internal const int MaximumLength = 32760;
        internal static bool SafeComponent(string component)
        {
            if (component.Length == 0 || component.Length > 255 || component == "." || component == ".."
                || component.EndsWith(".", StringComparison.Ordinal) || component.EndsWith(" ", StringComparison.Ordinal)
                || component.IndexOfAny(new[] { ':', '*', '?', '<', '>', '|', '"', '/', '\\' }) >= 0) return false;
            foreach (var c in component) if (c < 32) return false;
            return !Regex.IsMatch(component, @"\A(CONIN\$|CONOUT\$|CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        internal static string Absolute(string path)
        {
            if (path == null || path.Length > MaximumLength || !Regex.IsMatch(path, @"\A[A-Za-z]:\\") || path.IndexOf(':', 2) >= 0)
                throw new ArgumentException("Unsafe ordinary Windows path.");
            var result = path.Length == 3 ? path : path.TrimEnd('\\');
            if (result.Length < 3) throw new ArgumentException("Unsafe Windows drive root.");
            if (result.Length > 3) foreach (var part in result.Substring(3).Split('\\'))
                if (!SafeComponent(part)) throw new ArgumentException("Unsafe Windows path component.");
            return result;
        }
        internal static string ForIo(string path) { return @"\\?\" + Absolute(path); }
        internal static string ForProcess(string path) { var logical = Absolute(path); return logical.Length < 260 ? logical : ForIo(logical); }
        // Only trusted OS/enumeration results may cross this boundary, not caller-supplied roots.
        internal static string FromIo(string path) { return Absolute(path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path); }
        internal static string Parent(string path) { var index = path.LastIndexOf('\\'); return index < 3 ? path.Substring(0, 3) : path.Substring(0, index); }
        internal static string Leaf(string path) { return path.Substring(path.LastIndexOf('\\') + 1); }
        internal static IEnumerable<string> Directories(string root, string pattern)
        {
            foreach (var path in Directory.EnumerateDirectories(ForIo(root), pattern, SearchOption.TopDirectoryOnly)) yield return FromIo(path);
        }
    }
}
