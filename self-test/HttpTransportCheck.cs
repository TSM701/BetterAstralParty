using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using BetterAstralParty;

internal static class HttpTransportCheck
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception("Release HTTP transport: " + message);
    }
    internal static void Run()
    {
        using (var handler = ReleaseHttpClient.CreateHandler())
        {
            Check(!handler.AllowAutoRedirect, "redirects disabled before any request");
            Check(!handler.UseCookies, "cookie persistence disabled");
            Check(!handler.UseDefaultCredentials && handler.Credentials == null && !handler.PreAuthenticate,
                "OS/default/preauthenticated credentials disabled");
            Check(handler.AutomaticDecompression == DecompressionMethods.None, "compressed-size bypass disabled");
        }
        using (var production = ReleaseUpdates.Production("1.0.0-dev", ReleaseChannel.Beta))
        {
            production.Check(DateTimeOffset.UtcNow); production.Poll();
            Check(production.Result.Status == ReleaseUpdateStatus.NotConfigured, "production transport alone cannot enable private HTTP");
            production.ConfigureAuthentication(null);
            Check(production.Result.Status == ReleaseUpdateStatus.NotConfigured, "null session remains unconfigured");
            using var external = new HttpClient();
            try { production.Configure(external, null); Check(false, "production transport replacement accepted"); }
            catch (InvalidOperationException) { Check(true, "production transport replacement rejected"); }
            production.Dispose(); production.ConfigureAuthentication(null); production.Dispose();
            Check(production.Result.Status == ReleaseUpdateStatus.NotConfigured, "disposed production session cannot restart");
        }
        Loopback().GetAwaiter().GetResult();
        Console.WriteLine($"Release HTTP transport: {_checks} checks passed (loopback and unconfigured production only; no operational credentials or GitHub requests).");
    }
    private static async Task Loopback()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            var address = new Uri("http://127.0.0.1:" + ((IPEndPoint)server.LocalEndpoint).Port + "/");
            var received = new List<string[]>();
            var serving = Task.Run(async () =>
            {
                for (var i = 0; i < 2; i++)
                {
                    using var connection = await server.AcceptTcpClientAsync().WaitAsync(deadline.Token);
                    using var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var lines = new List<string>(); var bytes = 0;
                    while (true)
                    {
                        var line = await reader.ReadLineAsync().WaitAsync(deadline.Token);
                        if (line == null) throw new IOException("Incomplete loopback request");
                        bytes += line.Length;
                        if (bytes > 8192) throw new IOException("Oversized loopback request");
                        if (line.Length == 0) break;
                        lines.Add(line);
                    }
                    received.Add(lines.ToArray());
                    var response = i == 0
                        ? "HTTP/1.1 302 Found\r\nLocation: " + new Uri(address, "follow") + "\r\nSet-Cookie: fixture_cookie=placeholder; Path=/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), deadline.Token);
                    await stream.FlushAsync(deadline.Token);
                    connection.Client.Shutdown(SocketShutdown.Send);
                    var drain = new byte[1024];
                    while (await stream.ReadAsync(drain, deadline.Token) != 0) { }
                }
            }, deadline.Token);
            using var client = ReleaseHttpClient.Create();
            Check(client.Timeout == Timeout.InfiniteTimeSpan, "caller owns bounded operation deadline");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "start"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-placeholder-only");
            using var redirected = await client.SendAsync(request, deadline.Token);
            Check(redirected.StatusCode == HttpStatusCode.Found && redirected.RequestMessage?.RequestUri == request.RequestUri,
                "real production handler returns redirect without following it");
            using var probe = await client.GetAsync(new Uri(address, "probe"), deadline.Token);
            Check(probe.StatusCode == HttpStatusCode.OK, "explicit next request succeeds without inherited headers");
            await serving;
            Check(received.Count == 2 && received[0][0].StartsWith("GET /start ", StringComparison.Ordinal)
                && received[1][0].StartsWith("GET /probe ", StringComparison.Ordinal), "redirect target received zero requests");
            Check(received[0].Contains("Authorization: Bearer fixture-placeholder-only"), "only the inert first request carries the placeholder");
            Check(!received[1].Any(line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)), "no authorization or response cookie reused");
        }
        finally { server.Stop(); }
    }
}
