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
        internal static readonly string[] Owned = { "AstralParty-Vanilla.cmd", UpdateTrust.PluginPath, "BetterAstralParty-Launcher.exe", "BetterAstralParty-Mod.cmd", "BetterAstralParty-Steam.ps1", "BetterAstralParty.compatibility.json", "check-compatibility.ps1", "steam-shortcut.ps1" };
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
                if (obj == null || obj.Count != (obj.ContainsKey("channelState") ? 5 : 4) || !obj.ContainsKey("schema") || !(obj["schema"] is int) || (int)obj["schema"]! != 1
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
            var output = new StringBuilder("{\"schema\":1,\"owner\":\"" + InstalledPluginMetadata.Owner + "\",\"loaderBefore\":" + (LoaderBefore.HasValue ? LoaderBefore.Value ? "true" : "false" : "null") + ",\"files\":[");
            for (var i = 0; i < Owned.Length; i++) { if (i != 0) output.Append(','); output.Append("{\"path\":\"").Append(Owned[i]).Append("\",\"sha256\":\"").Append(hashes[Owned[i]]).Append("\"}"); }
            channels ??= Channels;
            output.Append(']'); if (channels != null) output.Append(",\"channelState\":\"").Append(channels.Encoded).Append('"');
            return Encoding.ASCII.GetBytes(output.Append("}\n").ToString());
        }
        // Whole/bootstrap integration uses an independently verified v2 owned8 descriptor
        // after its exact file hashes are installed. Never derive history from a saved
        // channel preference, and never discard existing history or the loader baseline.
        internal byte[] InitializeChannel(string root, VerifiedUpdateDescriptor descriptor, string? legacyBetaHighest = null, string? legacyBetaDescriptor = null)
        {
            if (!Regex.IsMatch(root, @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || descriptor.Protocol != 2 || descriptor.RepositoryId <= 0 || descriptor.Files.Count != 8) throw Bad();
            foreach (var file in descriptor.Files) if (!Files.ContainsKey(file.Path) || Files[file.Path] != file.Sha256) throw Bad();
            ReceiptChannelState channels;
            if (Channels != null) {
                if (Channels.Root != root) throw Bad();
                var currentDigest = descriptor.Channel == "Stable" ? Channels.StableDescriptor : Channels.BetaDescriptor;
                if (Channels.ActiveChannel == descriptor.Channel && Channels.ActiveVersion == UpdateVersion.Parse(descriptor.Version).Tag && currentDigest == descriptor.DescriptorSha256)
                    return Updated(descriptor, Channels);
                channels = Channels.Applied(root, descriptor, Guid.NewGuid().ToString("N"));
            } else {
                string? beta = null; var betaHash = "-";
                if (legacyBetaHighest != null) {
                    UpdateVersion.Parse(legacyBetaHighest);
                    if (legacyBetaDescriptor == null || !Regex.IsMatch(legacyBetaDescriptor, @"\A[0-9A-F]{64}\z")) throw Bad();
                    beta = legacyBetaHighest; betaHash = legacyBetaDescriptor;
                }
                if (descriptor.Channel == "Beta") {
                      if (beta != null) { var comparison = UpdateVersion.Parse(descriptor.Version).CompareTo(UpdateVersion.Parse(beta));
                          if (comparison < 0 || comparison == 0 && betaHash != descriptor.DescriptorSha256) throw new UpdateValidationException(UpdateFailure.NotNewer); }
                    beta = UpdateVersion.Parse(descriptor.Version).Tag; betaHash = descriptor.DescriptorSha256;
                }
                channels = new ReceiptChannelState(root, descriptor.Channel, Guid.NewGuid().ToString("N"),
                    descriptor.Channel == "Stable" ? descriptor.RepositoryId : 0, descriptor.Channel == "Stable" ? UpdateVersion.Parse(descriptor.Version).Tag : null, descriptor.Channel == "Stable" ? descriptor.DescriptorSha256 : "-",
                    beta == null ? 0 : ReleaseFeedPolicy.BetaRepositoryId, beta, betaHash);
            }
            return Updated(descriptor, channels);
        }
    }
}
