#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    internal sealed class InstallReceipt
    {
        internal const string Path = "BetterAstralParty.install.json";
        // Protocol 5 (1.4.0 baseline) installs no executable into the game root.
        internal const string Baseline = "1.4.0";
        internal static readonly string[] Owned = { "AstralParty-Vanilla.cmd", UpdateTrust.PluginPath, "BetterAstralParty-Mod.cmd", "BetterAstralParty.compatibility.json" };
        internal readonly bool? LoaderBefore;
        internal readonly Dictionary<string, string> Files;
        internal readonly ReceiptChannelState? Channels;
        private InstallReceipt(bool? loader, Dictionary<string, string> files, ReceiptChannelState? channels = null) { LoaderBefore = loader; Files = files; Channels = channels; }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.InvalidState); }
        internal sealed class Json
        {
            private readonly string _s; private int _p; private readonly int _maxItems, _maxText;
            internal Json(byte[] bytes,int maxBytes=16384,int maxItems=32,int maxText=256) { if (bytes.Length > maxBytes) throw Bad(); _maxItems=maxItems; _maxText=maxText; _s = new UTF8Encoding(false, true).GetString(bytes); }
            private void Space() { while (_p < _s.Length && (_s[_p] == ' ' || _s[_p] == '\r' || _s[_p] == '\n' || _s[_p] == '\t')) _p++; }
            private bool Take(char c) { Space(); if (_p < _s.Length && _s[_p] == c) { _p++; return true; } return false; }
            private void Need(char c) { if (!Take(c)) throw Bad(); }
            private string Text()
            {
                Need('"'); var output = new StringBuilder();
                while (_p < _s.Length) {
                    var c = _s[_p++]; if (c == '"') return output.ToString(); if (c < 32) throw Bad();
                    if (c == '\\') { if (_p >= _s.Length) throw Bad(); c = _s[_p++];
                        if (c == 'u') { if (_p > _s.Length - 4) throw Bad(); int value; if (!int.TryParse(_s.Substring(_p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) || value >= 0xd800 && value <= 0xdfff) throw Bad(); c = (char)value; _p += 4; }
                        else if (c != '"' && c != '\\' && c != '/') throw Bad();
                    }
                    output.Append(c); if (output.Length > _maxText) throw Bad();
                } throw Bad();
            }
            private object? Value(int depth)
            {
                Space(); if (depth > 6 || _p >= _s.Length) throw Bad();
                if (_s[_p] == '"') return Text();
                if (Take('{')) { var result = new Dictionary<string, object?>(StringComparer.Ordinal); if (Take('}')) return result;
                    do { var name = Text(); Need(':'); if (result.Count >= _maxItems || result.ContainsKey(name)) throw Bad(); result.Add(name, Value(depth + 1)); } while (Take(',')); Need('}'); return result; }
                if (Take('[')) { var result = new List<object?>(); if (Take(']')) return result;
                    do { if (result.Count >= _maxItems) throw Bad(); result.Add(Value(depth + 1)); } while (Take(',')); Need(']'); return result; }
                foreach (var token in new[] { "true", "false", "null" }) if (_s.Length - _p >= token.Length && _s.Substring(_p, token.Length) == token) { _p += token.Length; return token == "null" ? null : (object)(token == "true"); }
                if (_s[_p] == '1') { _p++; return 1; } throw Bad();
            }
            internal object? Parse() { var value = Value(0); Space(); if (_p != _s.Length) throw Bad(); return value; }
        }
        internal static InstallReceipt Parse(byte[] bytes)
        {
            try {
                var obj = new Json(bytes, maxText: 2048).Parse() as Dictionary<string, object?>;
                if (obj == null || obj.Count != 6 || !obj.ContainsKey("baseline") || !Equals(obj["baseline"], Baseline) || !obj.ContainsKey("channelState") || !obj.ContainsKey("schema") || !(obj["schema"] is int) || (int)obj["schema"]! != 1
                    || !obj.ContainsKey("owner") || !Equals(obj["owner"], InstalledPluginMetadata.Owner) || !obj.ContainsKey("loaderBefore") || !obj.ContainsKey("files")) throw Bad();
                if (obj["loaderBefore"] != null && !(obj["loaderBefore"] is bool)) throw Bad();
                var array = obj["files"] as List<object?>; if (array == null || array.Count != Owned.Length) throw Bad();
                var files = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in array) { var item = entry as Dictionary<string, object?>;
                    if (item == null || item.Count != 2 || !item.ContainsKey("path") || !item.ContainsKey("sha256") || !(item["path"] is string) || !(item["sha256"] is string)) throw Bad();
                    var path = (string)item["path"]!; var hash = (string)item["sha256"]!;
                    if (!UpdateTrust.OwnedPath(path) || !Regex.IsMatch(hash, @"\A[0-9A-F]{64}\z") || files.ContainsKey(path)) throw Bad(); files.Add(path, hash);
                }
                foreach (var path in Owned) if (!files.ContainsKey(path)) throw Bad();
                ReceiptChannelState? channels = null;
                if (obj.ContainsKey("channelState")) { if (!(obj["channelState"] is string)) throw Bad(); channels = ReceiptChannelState.Parse((string)obj["channelState"]!); }
                return new InstallReceipt((bool?)obj["loaderBefore"], files, channels);
            } catch (DecoderFallbackException) { throw Bad(); }
        }
        internal byte[] Updated(VerifiedUpdateDescriptor descriptor, ReceiptChannelState? channels = null)
        {
            if (descriptor.Files.Count != Owned.Length) throw Bad(); var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in descriptor.Files) { if (!Files.ContainsKey(file.Path) || hashes.ContainsKey(file.Path)) throw Bad(); hashes.Add(file.Path, file.Sha256); }
            var output = new StringBuilder("{\"schema\":1,\"baseline\":\"" + Baseline + "\",\"owner\":\"" + InstalledPluginMetadata.Owner + "\",\"loaderBefore\":" + (LoaderBefore.HasValue ? LoaderBefore.Value ? "true" : "false" : "null") + ",\"files\":[");
            for (var i = 0; i < Owned.Length; i++) { if (i != 0) output.Append(','); output.Append("{\"path\":\"").Append(Owned[i]).Append("\",\"sha256\":\"").Append(hashes[Owned[i]]).Append("\"}"); }
            channels ??= Channels;
            output.Append(']'); if (channels != null) output.Append(",\"channelState\":\"").Append(channels.Encoded).Append('"');
            return Encoding.ASCII.GetBytes(output.Append("}\n").ToString());
        }
        // Whole/bootstrap integration uses an independently verified native payload descriptor
        // after its exact file hashes are installed. Never derive history from a saved
        // channel preference, and never discard existing history or the loader baseline.
        internal static byte[] Create(string root, VerifiedUpdateDescriptor descriptor, bool? loader)
        {
            if (!Regex.IsMatch(root, @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || descriptor.Protocol != UpdateTrust.Protocol || descriptor.RepositoryId <= 0 || descriptor.Files.Count != Owned.Length) throw Bad();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in descriptor.Files) { if (!UpdateTrust.OwnedPath(file.Path) || hashes.ContainsKey(file.Path)) throw Bad(); hashes.Add(file.Path, file.Sha256); }
            var stable = descriptor.Channel == "Stable"; var beta = descriptor.Channel == "Beta"; var preRelease = descriptor.Channel == "PreRelease";
            var channels = new ReceiptChannelState(root, descriptor.Channel, Guid.NewGuid().ToString("N"),
                stable ? descriptor.RepositoryId : 0, stable ? descriptor.Version : null, stable ? descriptor.DescriptorSha256 : "-",
                beta ? descriptor.RepositoryId : 0, beta ? descriptor.Version : null, beta ? descriptor.DescriptorSha256 : "-",
                preRelease ? descriptor.RepositoryId : 0, preRelease ? descriptor.Version : null, preRelease ? descriptor.DescriptorSha256 : "-");
            return new InstallReceipt(loader, hashes, channels).Updated(descriptor, channels);
        }
        internal byte[] InitializeChannel(string root, VerifiedUpdateDescriptor descriptor)
        {
            if (!Regex.IsMatch(root, @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || descriptor.Protocol != UpdateTrust.Protocol || descriptor.RepositoryId <= 0 || descriptor.Files.Count != Owned.Length) throw Bad();
            foreach (var file in descriptor.Files) if (!Files.ContainsKey(file.Path) || Files[file.Path] != file.Sha256) throw Bad();
            ReceiptChannelState channels;
            if (Channels != null) {
                if (Channels.Root != root) throw Bad();
                var currentDigest = Channels.DescriptorFor(descriptor.Channel);
                if (Channels.ActiveChannel == descriptor.Channel && Channels.ActiveVersion == UpdateVersion.Parse(descriptor.Version).Tag && currentDigest == descriptor.DescriptorSha256)
                    return Updated(descriptor, Channels);
                channels = Channels.Applied(root, descriptor, Guid.NewGuid().ToString("N"), reinstall: true);
            } else throw Bad();
            return Updated(descriptor, channels);
        }
    }
}
