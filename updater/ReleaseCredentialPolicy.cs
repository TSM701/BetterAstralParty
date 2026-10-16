#nullable enable
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    internal static class ReleaseCredentialPolicy
    {
        internal static bool ValidToken(string? token) => token is { Length: <= 256 }
            && Regex.IsMatch(token, @"\Agithub_pat_[A-Za-z0-9_]{20,245}\z", RegexOptions.CultureInvariant);
    }
}
