using System.Net;
using System.Net.Http;

namespace BetterAstralParty;

// Shared production boundary for release metadata and assets. Authentication is request-scoped;
// an API response may never silently forward it to a redirect or reuse cookies/OS credentials.
internal static class ReleaseHttpClient
{
    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        PreAuthenticate = false,
        Credentials = null,
        AutomaticDecompression = DecompressionMethods.None
    };

    // Callers impose their own bounded operation/idle timeout and own the returned client.
    internal static HttpClient Create(HttpMessageHandler? fixtureHandler = null) =>
        new(fixtureHandler ?? CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
}
