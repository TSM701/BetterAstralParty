#nullable enable
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Framework-compatible SemVer precedence; no dependency on the game's loader runtime.
    internal sealed class UpdateVersion : IComparable<UpdateVersion>
    {
        private static readonly Regex Format = new Regex(
            @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
            RegexOptions.CultureInvariant);
        internal readonly int Major, Minor, Patch;
        internal readonly string Tag;
        private readonly string[] _prerelease;
        internal bool IsPrerelease { get { return _prerelease.Length != 0; } }
        private UpdateVersion(int major, int minor, int patch, string tag, string[] prerelease)
        { Major = major; Minor = minor; Patch = patch; Tag = tag; _prerelease = prerelease; }

        internal static bool TryParse(string? text, out UpdateVersion? version)
        {
            version = null;
            if (text == null || text.Length == 0 || text.Length > 128) return false;
            var tag = text[0] == 'v' ? text.Substring(1) : text;
            var match = Format.Match(tag);
            int major, minor, patch;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major)
                || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor)
                || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : new string[0];
            foreach (var item in pre)
                if (Numeric(item) && item.Length > 1 && item[0] == '0') return false;
            version = new UpdateVersion(major, minor, patch, tag, pre);
            return true;
        }
        internal static UpdateVersion Parse(string text)
        {
            UpdateVersion? version;
            if (!TryParse(text, out version)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            return version!;
        }
        private static bool Numeric(string text)
        {
            foreach (var c in text) if (c < '0' || c > '9') return false;
            return text.Length != 0;
        }
        public int CompareTo(UpdateVersion? other)
        {
            if (other == null) return 1;
            var value = Major.CompareTo(other.Major);
            if (value == 0) value = Minor.CompareTo(other.Minor);
            if (value == 0) value = Patch.CompareTo(other.Patch);
            if (value != 0) return value;
            if (_prerelease.Length == 0) return other._prerelease.Length == 0 ? 0 : 1;
            if (other._prerelease.Length == 0) return -1;
            for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
            {
                var left = _prerelease[i]; var right = other._prerelease[i];
                var ln = Numeric(left); var rn = Numeric(right);
                value = ln && rn ? left.Length.CompareTo(right.Length) : ln != rn ? (ln ? -1 : 1) : 0;
                if (value == 0) value = string.CompareOrdinal(left, right);
                if (value != 0) return value;
            }
            return _prerelease.Length.CompareTo(other._prerelease.Length);
        }
    }
}
