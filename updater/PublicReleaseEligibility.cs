#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
#if NET6_0_OR_GREATER
using System.Text.Json;
#endif

namespace BetterAstralParty.Updating
{
    // The signed public scope is immutable; GitHub's current release classification is not.
    internal static class PublicReleaseEligibility
    {
        internal const int MaxRepositoryBytes = 32768, MaxReleaseBytes = 1024 * 1024;
        internal static bool Applies(UpdateContext context) => ReleaseFeedPolicy.IsPublicChannel(context.Channel) && context.RepositoryId > 0;
        internal static Uri RepositoryEndpoint(UpdateContext context) => new Uri("https://api.github.com/repos/" + context.Repository);
        internal static Uri ReleaseEndpoint(UpdateContext context) => new Uri(RepositoryEndpoint(context) + "/releases/" + context.ReleaseId.ToString(CultureInfo.InvariantCulture));
        private static InvalidDataException Bad() => new InvalidDataException("Invalid public release eligibility metadata");
#if NET6_0_OR_GREATER
        private static object? Convert(JsonElement item, ref int count)
        {
            if (++count > 50000) throw Bad();
            if (item.ValueKind == JsonValueKind.Object) {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var field in item.EnumerateObject()) {
                    if (result.Count >= 256 || result.ContainsKey(field.Name)) throw Bad();
                    result.Add(field.Name, Convert(field.Value, ref count)!);
                }
                return result;
            }
            if (item.ValueKind == JsonValueKind.Array) {
                var result = new List<object?>(); foreach (var value in item.EnumerateArray()) result.Add(Convert(value, ref count)); return result.ToArray();
            }
            if (item.ValueKind == JsonValueKind.String) return item.GetString();
            if (item.ValueKind == JsonValueKind.True || item.ValueKind == JsonValueKind.False) return item.GetBoolean();
            if (item.ValueKind == JsonValueKind.Number) {
                if (item.TryGetInt64(out var integer)) return integer;
                return item.GetDouble();
            }
            if (item.ValueKind == JsonValueKind.Null) return null;
            throw Bad();
        }
#endif
        private static Dictionary<string, object> Read(byte[] bytes, int limit)
        {
            if (bytes.Length == 0 || bytes.Length > limit) throw Bad();
#if NET6_0_OR_GREATER
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); var count = 0;
            return Convert(json.RootElement, ref count) as Dictionary<string, object> ?? throw Bad();
#else
            return MaintenanceJsonReader.Object(MaintenanceJsonReader.Read(bytes, limit));
#endif
        }
        private static object? Get(Dictionary<string, object> value, string key) => value.TryGetValue(key, out var field) ? field : throw Bad();
        private static string Text(Dictionary<string, object> value, string key, int limit) => Get(value, key) is string text && text.Length <= limit ? text : throw Bad();
        private static bool Flag(Dictionary<string, object> value, string key) => Get(value, key) is bool flag ? flag : throw Bad();
        private static long Id(Dictionary<string, object> value, string key)
        {
            var field = Get(value, key); var number = field is long wide ? wide : field is int small ? small : 0;
            return number > 0 ? number : throw Bad();
        }
        internal static void Repository(byte[] bytes, UpdateContext context)
        {
            if (!Applies(context) || context.Repository != ReleaseFeedPolicy.StableRepository) throw Bad();
            var item = Read(bytes, MaxRepositoryBytes);
            if (Id(item, "id") != context.RepositoryId || !string.Equals(Text(item, "full_name", 256), context.Repository, StringComparison.OrdinalIgnoreCase) || Flag(item, "private")) throw Bad();
        }
        internal static bool Release(byte[] bytes, UpdateContext context, VerifiedUpdateDescriptor descriptor, long descriptorAsset = 0, long signatureAsset = 0)
        {
            if (!Applies(context) || descriptor.Repository != context.Repository || descriptor.RepositoryId != context.RepositoryId || descriptor.ReleaseId != context.ReleaseId
                || descriptor.AssetId != context.AssetId || !descriptor.AllowsChannel(context.Channel)) throw Bad();
            var item = Read(bytes, MaxReleaseBytes); var tag = Text(item, "tag_name", 128);
            if (Id(item, "id") != context.ReleaseId || UpdateVersion.Parse(tag).Tag != UpdateVersion.Parse(context.ReleaseTag).Tag
                || UpdateVersion.Parse(tag).Tag != descriptor.Version) throw Bad();
            if (!(Get(item, "assets") is object[] assets) || assets.Length > 32) throw Bad();
            var ids = new HashSet<long>(); var names = new HashSet<string>(StringComparer.Ordinal); long zip = 0, manifest = 0, signature = 0;
            foreach (var raw in assets) {
                var asset = raw as Dictionary<string, object> ?? throw Bad(); var name = Text(asset, "name", 256); var id = Id(asset, "id");
                if (!ids.Add(id) || !names.Add(name)) throw Bad();
                if (name == descriptor.AssetName) zip = id;
                else if (name == ReleaseFeedPolicy.UpdateManifestAsset) manifest = id;
                else if (name == ReleaseFeedPolicy.UpdateSignatureAsset) signature = id;
            }
            if (zip != descriptor.AssetId || manifest == 0 || signature == 0 || descriptorAsset != 0 && manifest != descriptorAsset || signatureAsset != 0 && signature != signatureAsset) throw Bad();
            return !Flag(item, "draft") && ReleaseFeedPolicy.For(context.Channel).Accepts(Flag(item, "prerelease"))
                && (context.Channel != "Stable" || !UpdateVersion.Parse(tag).IsPrerelease);
        }
#if BAP_FIXTURE_HELPER || BAP_LAUNCHER_RECOVERY_FIXTURE
        internal static Func<Uri, UpdateContext, VerifiedUpdateDescriptor, byte[]>? FixtureRead = null;
        internal static byte[] FixtureMetadata(Uri uri, UpdateContext context, VerifiedUpdateDescriptor descriptor)
        {
            var value = uri == RepositoryEndpoint(context) ? "{\"id\":" + context.RepositoryId + ",\"full_name\":\"" + context.Repository + "\",\"private\":false}"
                : "{\"id\":" + context.ReleaseId + ",\"draft\":false,\"prerelease\":" + (context.Channel == "PreRelease" ? "true" : "false")
                + ",\"tag_name\":\"" + context.ReleaseTag + "\",\"html_url\":\"https://github.com/" + context.Repository + "/releases/tag/" + context.ReleaseTag + "\",\"assets\":[{\"id\":" + context.AssetId
                + ",\"name\":\"" + descriptor.AssetName + "\"},{\"id\":777001,\"name\":\"" + ReleaseFeedPolicy.UpdateManifestAsset + "\"},{\"id\":777002,\"name\":\"" + ReleaseFeedPolicy.UpdateSignatureAsset + "\"}]}";
            return System.Text.Encoding.UTF8.GetBytes(value);
        }
#endif
        internal static bool Recheck(UpdateContext context, VerifiedUpdateDescriptor descriptor, CancellationToken cancellation = default(CancellationToken))
        {
            if (!Applies(context)) return true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, Credentials = null, AutomaticDecompression = DecompressionMethods.None }) { Timeout = TimeSpan.FromSeconds(30) };
            byte[] ReadEndpoint(Uri uri, int limit) {
                deadline.Token.ThrowIfCancellationRequested();
#if BAP_FIXTURE_HELPER || BAP_LAUNCHER_RECOVERY_FIXTURE
                if (FixtureRead != null) return FixtureRead(uri, context, descriptor);
#endif
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("BetterAstralParty-UpdateHelper/1.3"); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                using var cancel = deadline.Token.Register(client.CancelPendingRequests);
                using var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).GetAwaiter().GetResult();
                if (response.StatusCode != HttpStatusCode.OK || response.RequestMessage?.RequestUri?.AbsoluteUri != uri.AbsoluteUri || response.Content.Headers.ContentEncoding.Count != 0
                    || response.Content.Headers.ContentLength < 0 || response.Content.Headers.ContentLength > limit) throw Bad();
                using var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult(); using var output = new MemoryStream(); var buffer = new byte[8192];
                while (true) {
                    deadline.Token.ThrowIfCancellationRequested(); var length = input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).GetAwaiter().GetResult(); if (length == 0) break;
                    if (output.Length + length > limit) throw Bad(); output.Write(buffer, 0, length);
                }
                deadline.Token.ThrowIfCancellationRequested();
                if (response.Content.Headers.ContentLength is long declared && output.Length != declared) throw Bad();
                return output.ToArray();
            }
            Repository(ReadEndpoint(RepositoryEndpoint(context), MaxRepositoryBytes), context);
            return Release(ReadEndpoint(ReleaseEndpoint(context), MaxReleaseBytes), context, descriptor);
        }
    }
}
