#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BetterAstralParty.Updating;

internal static class TrustFixture
{
    private static int _checks;
    private static readonly byte[] Payload = new byte[2048];
    private static readonly UpdateContext Context = new UpdateContext("TSM701/BetterAstralParty", "Stable",
        "1.3.0", "1.3.1", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId);
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new Exception("Trust fixture: " + message); }
    private static void Reject(Action action, string message, UpdateFailure? expected = null)
    {
        try { action(); }
        catch (UpdateValidationException ex) { Check(!expected.HasValue || ex.Failure == expected.Value, message + " [" + ex.Failure + "]"); return; }
        throw new Exception("Trust fixture accepted: " + message);
    }
    private sealed class StreamingSink : Stream
    {
        private readonly Stream _sink;
        internal StreamingSink(Stream sink) { _sink = sink; }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { throw new NotSupportedException(); } }
        // Framework's ZIP writer queries the write position even when CanSeek is false.
        public override long Position { get { return _sink.Position; } set { throw new NotSupportedException(); } }
        public override void Write(byte[] buffer, int offset, int count) { _sink.Write(buffer, offset, count); }
        public override void Flush() { _sink.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long length) { throw new NotSupportedException(); }
    }
    private static byte[] Zip(string[] paths, byte[]? payload = null, CompressionLevel level = CompressionLevel.Optimal, bool streaming = false)
    {
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(streaming ? new StreamingSink(memory) : memory, ZipArchiveMode.Create, true))
                foreach (var path in paths)
                {
                    var entry = zip.CreateEntry(path, level);
                    entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using (var target = entry.Open()) target.Write(payload ?? Payload, 0, (payload ?? Payload).Length);
                }
            return memory.ToArray();
        }
    }
    private static string Manifest(byte[] zip, string version = "1.3.1", string channel = "Stable",
        string platform = UpdateTrust.PlatformId, string[]? files = null, byte[]? payload = null)
    {
        var paths = files ?? InstallReceipt.Owned;
        var data = payload ?? Payload;
        var feed = ReleaseFeedPolicy.For(channel);
        return "BetterAstralParty.UpdateDescriptor/v4\nrepository=" + feed.Repository + "\nversion=" + version
            + "\nchannel=" + channel + "\nplatform=" + platform + "\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-"
            + (version.StartsWith("v", StringComparison.Ordinal) ? version.Substring(1) : version)
            + "-update.zip\nzip-bytes=" + zip.Length.ToString(CultureInfo.InvariantCulture) + "\nzip-sha256=" + UpdateTrust.Hash(zip)
            + "\nfile-count=" + paths.Length.ToString(CultureInfo.InvariantCulture) + "\npurpose=release/" + channel
            + "\nrepository-id=" + feed.RepositoryId.ToString(CultureInfo.InvariantCulture) + "\nsettings-schema=1\n"
            + string.Join("", paths.OrderBy(x => x, StringComparer.Ordinal).Select(path => "file=" + path + "\t"
                + data.Length.ToString(CultureInfo.InvariantCulture) + "\t" + UpdateTrust.Hash(data) + "\n"));
    }
    private static VerifiedUpdateDescriptor Verify(UpdateTrust trust, RSA signing, string text, UpdateContext? context = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return trust.VerifyDescriptor(bytes, "fixture-only", signing.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), context ?? Context);
    }
    private static int U16(byte[] bytes, int offset) { return bytes[offset] | bytes[offset + 1] << 8; }
    private static int U32(byte[] bytes, int offset)
    { return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24; }
    private static void Set16(byte[] bytes, int offset, int value)
    { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); }
    private static void Set32(byte[] bytes, int offset, uint value)
    { for (var i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i)); }
    private static int Central(byte[] bytes) { return U32(bytes, bytes.Length - 6); }
    private static void PackageReject(UpdateTrust trust, RSA signing, byte[] zip, string message,
        string? descriptor = null, UpdateFailure? expected = null)
    {
        var verified = Verify(trust, signing, descriptor ?? Manifest(zip));
        Reject(() => UpdatePackage.Verify(zip, verified), message, expected);
    }
    private static void Mutation(UpdateTrust trust, RSA signing, byte[] original, Action<byte[], int> mutate, string message)
    {
        var zip = (byte[])original.Clone(); mutate(zip, Central(zip));
        PackageReject(trust, signing, zip, message);
    }
    private static void Versions()
    {
        var valid = new[] { "0.1.0", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "v1.0.0", "1.0.0+build.1",
            "1.0.0+build.2", "1.1.0-dev", "1.1.0", "1.2.0", "1.9.0", "1.10.0", "2.0.0-rc.2",
            "2.0.0-rc.10", "2.0.0", "2147483647.0.0", "1.0.0-999999999999999999999", "1.0.0-1000000000000000000000" };
        foreach (var text in valid) { UpdateVersion? value; Check(UpdateVersion.TryParse(text, out value), "valid SemVer " + text); }
        var invalid = new[] { "", "1", "1.2", "01.2.3", "1.02.3", "1.2.03", "v", "vv1.2.3", "V1.2.3",
            " 1.2.3", "1.2.3 ", "1.2.3\n", "1.2.3-01", "1.2.3-a..b", "1.2.3+", "1.2.3+a..b", "2147483648.0.0", "1.2.3-한국어" };
        foreach (var text in invalid) { UpdateVersion? value; Check(!UpdateVersion.TryParse(text, out value), "invalid SemVer " + text); }
        Check(UpdateVersion.Parse("1.0.0+old").CompareTo(UpdateVersion.Parse("v1.0.0+new")) == 0, "build/v do not bypass equality");
        Check(UpdateVersion.Parse(valid[22]).CompareTo(UpdateVersion.Parse(valid[21])) > 0, "large numeric identifiers use numeric precedence");
        for (var i = 1; i < 9; i++) Check(UpdateVersion.Parse(valid[i]).CompareTo(UpdateVersion.Parse(valid[i - 1])) > 0, "official prerelease precedence sequence");
#if !NETFRAMEWORK
        // Compare the new helper parser against the existing notification implementation's dependency.
        foreach (var left in valid.Take(21))
        foreach (var right in valid.Take(21))
        {
            SemanticVersioning.Version lv = null!, rv = null!;
            Check(BetterAstralParty.ReleaseUpdates.TryVersion(left, out lv) && BetterAstralParty.ReleaseUpdates.TryVersion(right, out rv),
                "existing notification oracle accepts pair");
            Check(Math.Sign(UpdateVersion.Parse(left).CompareTo(UpdateVersion.Parse(right))) == Math.Sign(lv.CompareTo(rv)),
                "helper/notification precedence agreement: " + left + " / " + right);
        }
#endif
    }
    private static void Descriptors(UpdateTrust trust, RSA signing, byte[] zip)
    {
        var text = Manifest(zip); var bytes = Encoding.UTF8.GetBytes(text);
        var signature = signing.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var production = UpdateTrust.Production();
        Check(production.Configured, "compiled public production trust root is configured");
        Reject(() => production.VerifyDescriptor(bytes, "fixture-only", signature, Context), "fixture key cannot configure production", UpdateFailure.InvalidSignature);
        var d = Verify(trust, signing, text);
        Check(d.Platform == UpdateTrust.PlatformId && d.Repository == Context.Repository && d.Channel == Context.Channel
            && d.Version == Context.ReleaseTag && d.KeyId == "fixture-only" && d.Files.Count == InstallReceipt.Owned.Length && d.Protocol == 4, "signed baseline target and owned5 binding");
        foreach (var oldHeader in new[] { "BetterAstralParty.UpdateDescriptor/v1", "BetterAstralParty.UpdateDescriptor/v2", "BetterAstralParty.UpdateDescriptor/v3" })
            Reject(() => Verify(trust, signing, text.Replace("BetterAstralParty.UpdateDescriptor/v4", oldHeader)), "signed prior protocol is unsupported", UpdateFailure.InvalidDescriptor);
        Reject(() => new VerifiedUpdateDescriptor(new object(), "fixture-only", d.Repository, d.Channel, d.Version, d.Platform, d.AssetName,
            d.ReleaseId, d.AssetId, d.ZipBytes, d.ZipSha256, d.DescriptorSha256, d.Files.ToList()), "unverified descriptor cannot be constructed");
        Reject(() => trust.VerifyDescriptor(bytes, "unknown", signature, Context), "unknown key ID", UpdateFailure.InvalidSignature);
        var changed = (byte[])bytes.Clone(); changed[changed.Length - 2] ^= 1;
        Reject(() => trust.VerifyDescriptor(changed, "fixture-only", signature, Context), "tampered signed bytes", UpdateFailure.InvalidSignature);
        var badSignature = (byte[])signature.Clone(); badSignature[0] ^= 1;
        Reject(() => trust.VerifyDescriptor(bytes, "fixture-only", badSignature, Context), "bad PSS signature", UpdateFailure.InvalidSignature);
        var pkcs = signing.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Reject(() => trust.VerifyDescriptor(bytes, "fixture-only", pkcs, Context), "wrong signature padding", UpdateFailure.InvalidSignature);
        Reject(() => trust.VerifyDescriptor(new byte[UpdateTrust.MaxDescriptorBytes + 1], "fixture-only", signature, Context), "descriptor byte budget", UpdateFailure.LimitExceeded);
        Reject(() => trust.VerifyDescriptor(bytes, "fixture-only", new byte[513], Context), "signature byte budget", UpdateFailure.LimitExceeded);
        foreach (var pair in new[] {
            new[] { "repository=TSM701/BetterAstralParty", "repository=Other/BetterAstralParty" },
            new[] { "platform=windows-x64-int", "platform=windows-arm64-int" },
            new[] { "channel=Stable", "channel=Beta" },
            new[] { "release-id=123", "release-id=124" }, new[] { "asset-id=456", "asset-id=457" },
            new[] { "asset-name=BetterAstralParty-1.3.1-update.zip", "asset-name=Other.zip" } })
            Reject(() => Verify(trust, signing, text.Replace(pair[0], pair[1])), "signed wrong target " + pair[0], UpdateFailure.WrongTarget);
        foreach (var broken in new[] { text.Replace("platform=windows-x64-int\n", ""), text + "unknown=value\n",
            text.Replace("channel=Stable\n", "channel=Stable\nchannel=Stable\n"), text.Replace("\n", "\r\n"), "\ufeff" + text,
            text.TrimEnd('\n'), text.Replace("release-id=123", "release-id=0123"), text.Replace("asset-id=456", "asset-id=0"),
            text.Replace("zip-sha256=" + UpdateTrust.Hash(zip), "zip-sha256=" + UpdateTrust.Hash(zip).ToLowerInvariant()),
            text.Replace("file-count=5", "file-count=33"), text.Replace("\t2048\t", "\t0\t"),
            text.Replace(UpdateTrust.PluginPath, "../BetterAstralParty.dll") })
            Reject(() => Verify(trust, signing, broken), "malformed signed descriptor", UpdateFailure.InvalidDescriptor);
        var allPaths = InstallReceipt.Owned;
        var manifest = Manifest(zip, files: allPaths);
        var rows = manifest.Split('\n').ToList();
        var lastRows = rows.Skip(14).Take(InstallReceipt.Owned.Length).ToArray();
        rows.RemoveRange(14, InstallReceipt.Owned.Length); rows.InsertRange(14, lastRows.Reverse());
        Reject(() => Verify(trust, signing, string.Join("\n", rows)), "unsorted signed file set", UpdateFailure.InvalidDescriptor);
        Reject(() => Verify(trust, signing, Manifest(zip, files: new[] { UpdateTrust.PluginPath, UpdateTrust.PluginPath })), "duplicate signed file set", UpdateFailure.InvalidDescriptor);
        Reject(() => Verify(trust, signing, Manifest(zip, files: new[] { "BetterAstralParty-Launcher.exe" })), "mandatory plugin omitted", UpdateFailure.InvalidDescriptor);
        foreach (var path in new[] { "BepInEx/config/kr.betterastralparty.mod.cfg", "BepInEx/core/Other.dll", "doorstop_config.ini",
            "winhttp.dll", "/absolute.dll", @"C:\escape.dll", "../escape.dll", "CON", UpdateTrust.PluginPath.ToLowerInvariant(),
            UpdateTrust.PluginPath + ":stream", UpdateTrust.PluginPath + ".", UpdateTrust.PluginPath + " ", "BetterAstralParty-Updater.exe" })
            Check(!UpdateTrust.OwnedPath(path), "forbidden owned path " + path);
        foreach (var current in new[] { "1.3.1", "1.3.1+new", "1.3.2", "2.0.0" })
            Reject(() => Verify(trust, signing, text, new UpdateContext(Context.Repository, "Stable", current, "1.3.1", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId)),
                "old valid signature cannot downgrade/equal " + current, UpdateFailure.NotNewer);
        Reject(() => Verify(trust, signing, text, new UpdateContext(Context.Repository, "Stable", "1.3.0", "1.3.1", 123, 456, UpdateTrust.PlatformId, "1.3.2", ReleaseFeedPolicy.StableRepositoryId)),
            "high-water version blocks replay after rollback", UpdateFailure.NotNewer);
        foreach (var version in new[] { "0.99.0", "1.3.1-rc.1" })
            Reject(() => Verify(trust, signing, Manifest(zip, version), new UpdateContext(Context.Repository, "Stable", "1.3.0", version, 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.StableRepositoryId)),
                "stable excludes beta/prerelease", UpdateFailure.WrongTarget);
        var beta = Verify(trust, signing, Manifest(zip, "v1.3.1-rc.10", "Beta"),
            new UpdateContext(ReleaseFeedPolicy.BetaRepository, "Beta", "1.3.1-rc.2", "v1.3.1-rc.10", 123, 456, UpdateTrust.PlatformId, repositoryId: ReleaseFeedPolicy.BetaRepositoryId));
        Check(beta.Channel == "Beta", "strictly newer Beta and one leading v accepted");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { trust.VerifyDescriptor(bytes, "fixture-only", signature, Context, cancellation.Token); throw new Exception("Cancelled verify accepted"); }
            catch (OperationCanceledException) { Check(true, "descriptor cancellation"); }
        }
        var huge = Manifest(zip)
            .Replace("\t2048\t", "\t67108864\t");
        Reject(() => Verify(trust, signing, huge), "aggregate signed expansion budget", UpdateFailure.LimitExceeded);
    }
    private static void Packages(UpdateTrust trust, RSA signing, byte[] zip)
    {
        var descriptor = Verify(trust, signing, Manifest(zip));
        var result = UpdatePackage.Verify(zip, descriptor);
        Check(result.FileCount == InstallReceipt.Owned.Length && result.ExpandedBytes == InstallReceipt.Owned.Length * Payload.Length, "valid signed owned5 package");
        var stored = Zip(InstallReceipt.Owned, level: CompressionLevel.NoCompression);
        Check(UpdatePackage.Verify(stored, Verify(trust, signing, Manifest(stored))).FileCount == InstallReceipt.Owned.Length, "stored/no-compression archive");
        var streamed = Zip(InstallReceipt.Owned, streaming: true);
        Check((U16(streamed, 6) & 8) != 0, "fixture actually uses data descriptors");
        Check(UpdatePackage.Verify(streamed, Verify(trust, signing, Manifest(streamed))).FileCount == InstallReceipt.Owned.Length, "ZIP32 streaming data descriptor");
        var pair = InstallReceipt.Owned;
        var two = Zip(pair);
        Check(UpdatePackage.Verify(two, Verify(trust, signing, Manifest(two, files: pair))).FileCount == InstallReceipt.Owned.Length, "complete multi-file inventory");
        Reject(() => Verify(trust, signing, Manifest(two, files: new[] { UpdateTrust.PluginPath })), "legacy partial inventory is unsupported", UpdateFailure.InvalidDescriptor);
        var bad = (byte[])zip.Clone(); bad[35] ^= 1;
        Reject(() => UpdatePackage.Verify(bad, descriptor), "ZIP hash mismatch", UpdateFailure.InvalidPackage);
        Reject(() => UpdatePackage.Verify(zip.Take(zip.Length - 1).ToArray(), descriptor), "ZIP length mismatch", UpdateFailure.InvalidPackage);
        Reject(() => UpdatePackage.Verify(new byte[(int)UpdateTrust.MaxZipBytes + 1], descriptor), "ZIP byte budget", UpdateFailure.LimitExceeded);
        PackageReject(trust, signing, zip, "signed wrong file digest", Manifest(zip).Replace(UpdateTrust.Hash(Payload), new string('0', 64)));
        PackageReject(trust, signing, zip, "signed wrong file length", Manifest(zip).Replace("\t2048\t", "\t2049\t"));
        foreach (var path in new[] { "../escape.dll", "/escape.dll", @"C:\escape.dll", "CON", "AUX.txt",
            UpdateTrust.PluginPath + ":ads", UpdateTrust.PluginPath.ToLowerInvariant(), UpdateTrust.PluginPath + ".", UpdateTrust.PluginPath + " ",
            "BepInEx/config/user.cfg", "winhttp.dll", UpdateTrust.PluginPath + "/" })
            PackageReject(trust, signing, Zip(new[] { path }), "ZIP illegal path " + path);
        var duplicate = Zip(new[] { UpdateTrust.PluginPath, UpdateTrust.PluginPath });
        PackageReject(trust, signing, duplicate, "duplicate archive entries", Manifest(duplicate, files: pair));
        Mutation(trust, signing, zip, (b, c) => Set32(b, c + 38, 0xa1ed0000), "Unix symlink");
        Mutation(trust, signing, zip, (b, c) => Set32(b, c + 38, 0x400), "DOS reparse attribute");
        Mutation(trust, signing, zip, (b, c) => Set32(b, c + 38, 0x10), "directory attribute");
        Mutation(trust, signing, zip, (b, c) => { Set16(b, c + 8, 1); Set16(b, 6, 1); }, "encrypted flag");
        Mutation(trust, signing, zip, (b, c) => { Set16(b, c + 10, 9); Set16(b, 8, 9); }, "unsupported compression");
        Mutation(trust, signing, zip, (b, c) => Set16(b, c + 6, 45), "ZIP64/version policy");
        Mutation(trust, signing, zip, (b, c) => Set16(b, c + 30, 1), "extra field policy");
        Mutation(trust, signing, zip, (b, c) => Set16(b, b.Length - 2, 1), "archive comment policy");
        Mutation(trust, signing, zip, (b, c) => Set16(b, b.Length - 18, 1), "multi-disk archive");
        Mutation(trust, signing, zip, (b, c) => b[30] ^= 1, "central/local name mismatch");
        Mutation(trust, signing, zip, (b, c) => Set32(b, c + 42, 1), "preamble/hidden local bytes");
        Mutation(trust, signing, zip, (b, c) => { Set32(b, c + 16, 0); Set32(b, 14, 0); }, "CRC check independent of signed SHA");
        Mutation(trust, signing, zip, (b, c) => b[30 + U16(b, 26) + U32(b, c + 20) - 2] ^= 1, "damaged compressed payload");
        var overlap = (byte[])two.Clone(); var central = Central(overlap);
        Set32(overlap, central + 46 + U16(overlap, central + 28) + 42, 0);
        PackageReject(trust, signing, overlap, "overlapping local file offsets", Manifest(overlap, files: pair));
        var bombPayload = new byte[8192]; var bomb = Zip(InstallReceipt.Owned, bombPayload);
        PackageReject(trust, signing, bomb, "compression ratio bomb", Manifest(bomb, payload: bombPayload), UpdateFailure.LimitExceeded);
        var lying = (byte[])zip.Clone(); central = Central(lying);
        Set32(lying, central + 24, 1); Set32(lying, 22, 1);
        PackageReject(trust, signing, lying, "actual expanded stream exceeds signed length", Manifest(lying).Replace("\t2048\t", "\t1\t"));
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { UpdatePackage.Verify(zip, descriptor, cancellation.Token); throw new Exception("Cancelled package accepted"); }
            catch (OperationCanceledException) { Check(true, "package cancellation"); }
        }
        Check(!Directory.Exists("BepInEx"), "validator creates no extracted tree in fixture working directory");
    }
    private static int Main()
    {
        try
        {
            new Random(701).NextBytes(Payload);
            Versions();
#if NETFRAMEWORK
            using (RSA signing = new RSACng(3072))
#else
            using (var signing = RSA.Create(3072))
#endif
            {
                var publicKey = signing.ExportParameters(false);
                var trusted = new TrustedUpdateKey("fixture-only", publicKey);
                publicKey.Modulus![0] ^= 1; // Constructor must own its public bytes.
                var trust = new UpdateTrust(new[] { trusted });
                var zip = Zip(InstallReceipt.Owned);
                Descriptors(trust, signing, zip); Packages(trust, signing, zip);
            }
#if NETFRAMEWORK
            using (RSA signing4096 = new RSACng(4096))
#else
            using (var signing4096 = RSA.Create(4096))
#endif
            {
                var trust4096 = new UpdateTrust(new[] { new TrustedUpdateKey("fixture-only", signing4096.ExportParameters(false)) });
                var zip4096 = Zip(InstallReceipt.Owned);
                Check(UpdatePackage.Verify(zip4096, Verify(trust4096, signing4096, Manifest(zip4096))).FileCount == InstallReceipt.Owned.Length, "4096-bit trusted PSS owned5 package");
            }
            Console.WriteLine("PASS: update trust " + _checks + " assertions; RSA-PSS/ZIP/SemVer; ephemeral fixture key only; no extraction, install, credentials or HTTP.");
#if !NETFRAMEWORK
            DownloadFixture.Run().GetAwaiter().GetResult();
#endif
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
