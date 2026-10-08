#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BetterAstralParty.Updating
{
    internal enum UpdateFailure { NotConfigured, InvalidSignature, InvalidDescriptor, WrongTarget, NotNewer, InvalidPackage, LimitExceeded }
    internal sealed class UpdateValidationException : Exception
    {
        internal readonly UpdateFailure Failure;
        internal UpdateValidationException(UpdateFailure failure) : base("Update validation: " + failure) { Failure = failure; }
    }
    internal sealed class UpdateContext
    {
        internal readonly string Repository, Channel, CurrentVersion, ReleaseTag, Platform;
        internal readonly string? HighestAppliedVersion;
        internal readonly string? HighestAppliedDescriptorSha256;
        internal readonly long ReleaseId, AssetId;
        internal readonly long RepositoryId;
        internal readonly ChannelTransitionIntent? Transition;
        internal UpdateContext(string repository, string channel, string currentVersion, string releaseTag,
            long releaseId, long assetId, string platform, string? highestAppliedVersion = null,
            long repositoryId = 0, ChannelTransitionIntent? transition = null, string? highestAppliedDescriptorSha256 = null)
            : this(repository, channel, currentVersion, releaseTag, releaseId, assetId, platform, highestAppliedVersion,
                repositoryId, transition, highestAppliedDescriptorSha256, false) { }
        internal static UpdateContext LocalInstall(string repository, string channel, string version, long repositoryId)
        { return new UpdateContext(repository, channel, "0.0.0", version, 0, 0, UpdateTrust.PlatformId, null, repositoryId, null, null, true); }
        private UpdateContext(string repository, string channel, string currentVersion, string releaseTag,
            long releaseId, long assetId, string platform, string? highestAppliedVersion,
            long repositoryId, ChannelTransitionIntent? transition, string? highestAppliedDescriptorSha256, bool localInstall)
        {
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}\z")
                || (channel != "Stable" && channel != "Beta") || (localInstall ? releaseId != 0 || assetId != 0 : releaseId <= 0 || assetId <= 0)
                || platform != UpdateTrust.PlatformId) throw new ArgumentException("Invalid update context");
            UpdateVersion.Parse(currentVersion); UpdateVersion.Parse(releaseTag);
            if (highestAppliedVersion != null) UpdateVersion.Parse(highestAppliedVersion);
            Repository = repository; Channel = channel; CurrentVersion = currentVersion; ReleaseTag = releaseTag;
            ReleaseId = releaseId; AssetId = assetId; Platform = platform; HighestAppliedVersion = highestAppliedVersion;
            RepositoryId = repositoryId == 0 && repository == ReleaseFeedPolicy.BetaRepository ? ReleaseFeedPolicy.BetaRepositoryId : repositoryId;
            if (RepositoryId < 0 || transition != null && !transition.MatchesTarget(this)) throw new ArgumentException("Invalid feed identity");
            Transition = transition;
            if (highestAppliedDescriptorSha256 != null && !Regex.IsMatch(highestAppliedDescriptorSha256, @"\A[0-9A-F]{64}\z")) throw new ArgumentException("Invalid channel evidence");
            HighestAppliedDescriptorSha256 = highestAppliedDescriptorSha256;
        }
    }
    internal sealed class TrustedUpdateKey
    {
        internal readonly string Id;
        private readonly byte[] _modulus, _exponent;
        internal TrustedUpdateKey(string id, RSAParameters publicKey)
        {
            if (!Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,40}\z") || publicKey.Modulus == null
                || (publicKey.Modulus.Length != 384 && publicKey.Modulus.Length != 512)
                || (publicKey.Modulus[0] & 128) == 0 || publicKey.Exponent == null
                || publicKey.Exponent.Length != 3 || publicKey.Exponent[0] != 1 || publicKey.Exponent[1] != 0 || publicKey.Exponent[2] != 1
                || publicKey.D != null || publicKey.P != null || publicKey.Q != null || publicKey.DP != null
                || publicKey.DQ != null || publicKey.InverseQ != null) throw new ArgumentException("Invalid public update key");
            Id = id; _modulus = (byte[])publicKey.Modulus.Clone(); _exponent = (byte[])publicKey.Exponent.Clone();
        }
        internal RSAParameters Parameters()
        { return new RSAParameters { Modulus = (byte[])_modulus.Clone(), Exponent = (byte[])_exponent.Clone() }; }
    }
    internal sealed class UpdateFile
    {
        internal readonly string Path, Sha256;
        internal readonly long Bytes;
        internal UpdateFile(string path, long bytes, string sha256) { Path = path; Bytes = bytes; Sha256 = sha256; }
    }
    internal sealed class VerifiedUpdateDescriptor
    {
        internal readonly string Repository, Channel, Version, Platform, AssetName, ZipSha256, DescriptorSha256, KeyId;
        internal readonly long ReleaseId, AssetId, ZipBytes;
        internal int Protocol { get; private set; } = 1;
        internal long RepositoryId { get; private set; }
        internal int SettingsSchema { get; private set; }
        internal readonly ReadOnlyCollection<UpdateFile> Files;
        // Created only by the trusted verifier; no untrusted deserialization into this type.
        internal VerifiedUpdateDescriptor(object stamp, string keyId, string repository, string channel, string version, string platform, string assetName,
            long releaseId, long assetId, long zipBytes, string zipSha256, string descriptorSha256, List<UpdateFile> files)
        {
            if (!UpdateTrust.ValidStamp(stamp)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            KeyId = keyId;
            Repository = repository; Channel = channel; Version = version; Platform = platform; AssetName = assetName;
            ReleaseId = releaseId; AssetId = assetId; ZipBytes = zipBytes; ZipSha256 = zipSha256; DescriptorSha256 = descriptorSha256;
            Files = new ReadOnlyCollection<UpdateFile>(files.ToArray());
        }
        internal void FeedContract(object stamp, long repositoryId, int settingsSchema)
        {
            if (!UpdateTrust.ValidStamp(stamp)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            Protocol = 2; RepositoryId = repositoryId; SettingsSchema = settingsSchema;
        }
    }
    internal sealed class UpdateTrust
    {
        internal const string PlatformId = "windows-x64-int";
        internal const string PluginPath = "BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll";
        internal const int MaxDescriptorBytes = 64 * 1024, MaxSignatureBytes = 512, MaxFiles = 32;
        internal const long MaxZipBytes = 64L * 1024 * 1024, MaxFileBytes = 64L * 1024 * 1024, MaxExpandedBytes = 128L * 1024 * 1024;
        private readonly Dictionary<string, TrustedUpdateKey> _keys = new Dictionary<string, TrustedUpdateKey>(StringComparer.Ordinal);
        private bool _production;
        private static readonly object VerificationStamp = new object();
        internal static bool ValidStamp(object stamp) { return ReferenceEquals(stamp, VerificationStamp); }
        internal bool Configured { get { return _keys.Count != 0; } }
        internal bool AllowsLegacyContracts { get { return !_production; } }
        // Only reviewed compiled PUBLIC pins. Fixture keys never enter the product trust root.
        internal static UpdateTrust Production() { return new UpdateTrust(ProductionUpdateKeys.Read()) { _production = true }; }
        internal UpdateTrust(IEnumerable<TrustedUpdateKey> trustedKeys)
        {
            foreach (var key in trustedKeys)
            {
                if (key == null || _keys.ContainsKey(key.Id)) throw new ArgumentException("Duplicate/invalid trust key");
                _keys.Add(key.Id, key);
            }
        }
        internal static bool OwnedPath(string path)
        {
            // Exact ASCII allowlist also rejects traversal, ADS, reserved names, case aliases and user settings.
            return path == PluginPath || path == "BetterAstralParty-Launcher.exe" || path == "BetterAstralParty.compatibility.json"
                || path == "BetterAstralParty-Mod.cmd" || path == "AstralParty-Vanilla.cmd" || path == "BetterAstralParty-Steam.ps1"
                || path == "check-compatibility.ps1" || path == "steam-shortcut.ps1";
        }
        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }
        internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }
        private static long Number(string value, long max)
        {
            long result;
            if (!Regex.IsMatch(value, @"\A[1-9][0-9]{0,18}\z")
                || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) || result > max)
                throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            return result;
        }
        private static string Digest(string value)
        {
            if (!Regex.IsMatch(value, @"\A[0-9A-F]{64}\z")) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            return value;
        }
        internal byte[] VerifyDetached(byte[] bytes, string keyId, byte[] signature, CancellationToken cancellation = default(CancellationToken))
        {
            if (!Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            cancellation.ThrowIfCancellationRequested();
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxDescriptorBytes
                || signature == null || signature.Length == 0 || signature.Length > MaxSignatureBytes)
                throw new UpdateValidationException(UpdateFailure.LimitExceeded);
            TrustedUpdateKey key;
            if (keyId == null || !_keys.TryGetValue(keyId, out key!)) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            var snapshot = (byte[])bytes.Clone(); var signed = (byte[])signature.Clone();
            try
            {
#if NETFRAMEWORK
                using (RSA rsa = new RSACng())
#else
                using (var rsa = RSA.Create())
#endif
                {
                    rsa.ImportParameters(key.Parameters());
                    if (!rsa.VerifyData(snapshot, signed, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                        throw new UpdateValidationException(UpdateFailure.InvalidSignature);
                }
            }
            catch (CryptographicException) { throw new UpdateValidationException(UpdateFailure.InvalidSignature); }
            cancellation.ThrowIfCancellationRequested();
            return snapshot;
        }
        internal VerifiedUpdateDescriptor VerifyDescriptor(byte[] bytes, string keyId, byte[] signature,
            UpdateContext context, CancellationToken cancellation = default(CancellationToken))
        {
            var snapshot = VerifyDetached(bytes, keyId, signature, cancellation);
            return ParseVerified(snapshot, keyId, context, cancellation);
        }
        internal VerifiedUpdateDescriptor VerifyProposal(byte[] bytes, string keyId, byte[] signature, UpdateContext context, CancellationToken cancellation = default(CancellationToken))
        {
            // Read-only proposal: signature/target/purpose still checked, but it grants no
            // installation or payload stamp. Apply always rechecks an approved exact intent.
            if (context.RepositoryId <= 0 || context.Repository != ReleaseFeedPolicy.For(context.Channel).Repository)
                throw new UpdateValidationException(UpdateFailure.WrongTarget);
            return VerifyDescriptor(bytes, keyId, signature, new UpdateContext(context.Repository, context.Channel, "0.0.0", context.ReleaseTag,
                context.ReleaseId, context.AssetId, context.Platform, repositoryId: context.RepositoryId), cancellation);
        }
        // Local whole installation has its own signed purpose, never a fabricated GitHub asset.
        internal VerifiedUpdateDescriptor VerifyLocalInstall(byte[] bytes, string keyId, byte[] signature, UpdateContext context)
        {
            if (context.ReleaseId != 0 || context.AssetId != 0 || !ReleaseFeedPolicy.Matches(context)
                || context.Transition != null || context.CurrentVersion != "0.0.0")
                throw new UpdateValidationException(UpdateFailure.WrongTarget);
            return ParseOwnedDescriptor(VerifyDetached(bytes, keyId, signature), keyId, context, default(CancellationToken), localInstall: true);
        }
        // Private so production callers cannot mint a verified descriptor from unsigned bytes.
        // Structural fixtures invoke this exact parser separately from signature acceptance.
        private VerifiedUpdateDescriptor ParseVerified(byte[] snapshot, string keyId, UpdateContext context, CancellationToken cancellation)
        { return ParseOwnedDescriptor(snapshot, keyId, context, cancellation, localInstall: false); }
        private VerifiedUpdateDescriptor ParseOwnedDescriptor(byte[] snapshot, string keyId, UpdateContext context, CancellationToken cancellation, bool localInstall)
        {
            // v1 is printable ASCII in UTF-8 with LF and one final newline; no BOM, CR, NUL or ambiguous fields.
            foreach (var b in snapshot)
                if (b != 10 && b != 9 && (b < 32 || b > 126)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            var lines = Encoding.UTF8.GetString(snapshot).Split('\n');
            var protocol = localInstall ? (lines[0] == "BetterAstralParty.LocalInstallDescriptor/v1" ? 2 : 0)
                : lines[0] == "BetterAstralParty.UpdateDescriptor/v1" ? 1 : lines[0] == "BetterAstralParty.UpdateDescriptor/v2" ? 2 : 0;
            if (lines.Length < 13 || protocol == 0 || lines[lines.Length - 1] != "")
                throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            string Value(int i, string name)
            {
                var prefix = name + "=";
                if (!lines[i].StartsWith(prefix, StringComparison.Ordinal)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
                var value = lines[i].Substring(prefix.Length);
                if (value.Length == 0 || value.IndexOf('\t') >= 0) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
                return value;
            }
            var repository = Value(1, "repository"); var tag = Value(2, "version"); var channel = Value(3, "channel");
            var platform = Value(4, "platform");
            var releaseId = localInstall && Value(5, "release-id") == "0" ? 0 : Number(Value(5, "release-id"), long.MaxValue);
            var assetId = localInstall && Value(6, "asset-id") == "0" ? 0 : Number(Value(6, "asset-id"), long.MaxValue);
            if (localInstall && (releaseId != 0 || assetId != 0)) throw new UpdateValidationException(UpdateFailure.WrongTarget);
            var assetName = Value(7, "asset-name");
            var zipBytes = Number(Value(8, "zip-bytes"), MaxZipBytes); var zipHash = Digest(Value(9, "zip-sha256"));
            var count = (int)Number(Value(10, "file-count"), MaxFiles);
            if (protocol == 2 && count != 8) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            var start = protocol == 1 ? 11 : 14;
            if (lines.Length != count + start + 1) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            var repositoryId = protocol == 2 ? Number(Value(12, "repository-id"), long.MaxValue) : 0;
            var settingsSchema = protocol == 2 ? (int)Number(Value(13, "settings-schema"), 1) : 0;
            if (protocol == 2 && (Value(11, "purpose") != (localInstall ? "local-install/" : "release/") + channel || context.RepositoryId != repositoryId
                || repository != (channel == "Stable" ? ReleaseFeedPolicy.StableRepository : ReleaseFeedPolicy.BetaRepository)
                || channel != "Stable" && channel != "Beta")) throw new UpdateValidationException(UpdateFailure.WrongTarget);
            if (_production && (protocol != 2 || !ReleaseFeedPolicy.Matches(context))) throw new UpdateValidationException(UpdateFailure.WrongTarget);
            var version = UpdateVersion.Parse(tag);
            if (repository != context.Repository || channel != context.Channel || platform != context.Platform
                || platform != PlatformId || releaseId != context.ReleaseId || assetId != context.AssetId || tag != context.ReleaseTag
                || assetName != "BetterAstralParty-" + version.Tag + "-update.zip")
                throw new UpdateValidationException(UpdateFailure.WrongTarget);
            if (channel == "Stable" && (version.Major == 0 || version.IsPrerelease && !(localInstall && version.IsLocalPrerelease))) throw new UpdateValidationException(UpdateFailure.WrongTarget);
            if (context.Transition != null && (protocol != 2 || !context.Transition.MatchesTarget(context)))
                throw new UpdateValidationException(UpdateFailure.WrongTarget);
            if (context.Transition != null && (context.Transition.DescriptorHash != Hash(snapshot) || context.Transition.ZipHash != zipHash))
                throw new UpdateValidationException(UpdateFailure.WrongTarget);
            var floorComparison = context.HighestAppliedVersion == null ? 1 : version.CompareTo(UpdateVersion.Parse(context.HighestAppliedVersion));
            var exactTransitionReplay = context.Transition != null && floorComparison == 0 && context.HighestAppliedDescriptorSha256 == Hash(snapshot);
            if (context.Transition == null && version.CompareTo(UpdateVersion.Parse(context.CurrentVersion)) <= 0
                || floorComparison <= 0 && !exactTransitionReplay)
                throw new UpdateValidationException(UpdateFailure.NotNewer);
            var files = new List<UpdateFile>(); var total = 0L; var previous = "";
            for (var i = 0; i < count; i++)
            {
                var line = lines[start + i];
                if (!line.StartsWith("file=", StringComparison.Ordinal)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
                var parts = line.Substring(5).Split('\t');
                if (parts.Length != 3 || !OwnedPath(parts[0]) || string.CompareOrdinal(previous, parts[0]) >= 0)
                    throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
                var size = Number(parts[1], MaxFileBytes); total += size;
                if (total > MaxExpandedBytes) throw new UpdateValidationException(UpdateFailure.LimitExceeded);
                files.Add(new UpdateFile(parts[0], size, Digest(parts[2]))); previous = parts[0];
            }
            if (!files.Exists(file => file.Path == PluginPath)) throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
            cancellation.ThrowIfCancellationRequested();
            var result = new VerifiedUpdateDescriptor(VerificationStamp, keyId, repository, channel, tag, platform, assetName, releaseId, assetId,
                zipBytes, zipHash, Hash(snapshot), files);
            if (protocol == 2) result.FeedContract(VerificationStamp, repositoryId, settingsSchema);
            return result;
        }
    }
}
