#nullable enable
using System;

namespace BetterAstralParty.Installation
{
    internal static class NativeMaintenanceFiles
    {
        // These distribution-only roles are never consumed by native maintenance or copied into the game.
        internal static bool IsDocumentation(string path)
        {
            var leaf = path.Substring(path.LastIndexOf('/') + 1);
            var text = leaf.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || leaf.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            var license = leaf.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || leaf.Equals("COPYING", StringComparison.OrdinalIgnoreCase) || leaf.Equals("NOTICE", StringComparison.OrdinalIgnoreCase);
            if (path.IndexOf('/') < 0) return text || license;
            if (path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)) return text || license;
            if (path.StartsWith("THIRD-PARTY-NOTICES/", StringComparison.OrdinalIgnoreCase))
                return text || license || path.Equals("THIRD-PARTY-NOTICES/component-matrix.json", StringComparison.OrdinalIgnoreCase);
            if (path.StartsWith("THIRD-PARTY-SOURCE/", StringComparison.OrdinalIgnoreCase)) return leaf.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            const string loader = ".deps/bepinex/";
            return path.StartsWith(loader, StringComparison.OrdinalIgnoreCase) && path.IndexOf('/', loader.Length) < 0 && (text || license);
        }
    }
}
