#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    internal static class MaintenanceJson
    {
        internal static Dictionary<string, object?> Read(byte[] bytes, int limit = 1024 * 1024, int rows = 4096)
        {
            if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) {
                var plain = new byte[bytes.Length - 3]; Buffer.BlockCopy(bytes, 3, plain, 0, plain.Length); bytes = plain;
            }
            var value = Normalize(LauncherRecoveryJson.Read(bytes, limit), rows) as Dictionary<string, object?>;
            if (value == null) throw new InvalidDataException("JSON root must be an object.");
            return value;
        }
        private static object? Normalize(object? value, int rows)
        {
            if (value is Dictionary<string, object?> map) {
                if (map.Count > rows) throw new InvalidDataException("JSON object exceeds limit.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var entry in map) { if (!seen.Add(entry.Key)) throw new InvalidDataException("Duplicate JSON property."); result.Add(entry.Key, Normalize(entry.Value, rows)); }
                return result;
            }
            if (value is object?[] array) {
                if (array.Length > rows) throw new InvalidDataException("JSON array exceeds limit.");
                var result = new List<object?>(); foreach (var entry in array) result.Add(Normalize(entry, rows)); return result;
            }
            if (value is string text && text.Length > WindowsPath.MaximumLength) throw new InvalidDataException("JSON text exceeds limit.");
            return value;
        }
        internal static string Text(Dictionary<string, object?> map, string key)
        { object? value; if (!map.TryGetValue(key, out value) || !(value is string)) throw new InvalidDataException("Invalid JSON string: " + key); return (string)value; }
        internal static List<object?> List(Dictionary<string, object?> map, string key)
        { object? value; if (!map.TryGetValue(key, out value) || !(value is List<object?>)) throw new InvalidDataException("Invalid JSON array: " + key); return (List<object?>)value; }
        internal static Dictionary<string, object?> Map(object? value)
        { return value as Dictionary<string, object?> ?? throw new InvalidDataException("Invalid JSON object."); }
        internal static int Number(Dictionary<string, object?> map, string key)
        { object? value; if (!map.TryGetValue(key, out value) || !(value is int)) throw new InvalidDataException("Invalid JSON integer: " + key); return (int)value; }
        internal static byte[] Write(object value)
        { return new UTF8Encoding(false).GetBytes(new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 }.Serialize(value)); }
        internal static bool Hash(string value) { return Regex.IsMatch(value, @"\A[A-F0-9]{64}\z"); }
    }

    // Authenticate declared identity and every consumed file; distribution-only documents are never opened.
    internal sealed class NativeMaintenanceBundle : IDisposable
    {
        internal static readonly Dictionary<string, string> OwnedSources = new Dictionary<string, string>(StringComparer.Ordinal) {
            {"AstralParty-Vanilla.cmd", "tools/AstralParty-Vanilla.cmd"},
            {UpdateTrust.PluginPath, "artifacts/BetterAstralParty.dll"},
            {"BetterAstralParty-Launcher.exe", "artifacts/BetterAstralParty-Launcher.exe"},
            {"BetterAstralParty-Mod.cmd", "tools/BetterAstralParty-Mod.cmd"},
            {"BetterAstralParty.compatibility.json", "artifacts/BetterAstralParty.compatibility.json"}
        };
        internal readonly WindowsFileFence Source;
        internal readonly string PackageRoot, GameRoot, Version, ManifestHash, BuildIdentity, RootIdentity;
        internal readonly Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, string> Expected = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _sizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WindowsFileFence.FileLease> _files = new Dictionary<string, WindowsFileFence.FileLease>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, object?>? Recovery;
        internal string? Observation;
        internal string? Mode;
        internal Dictionary<string, string?>? Observed;
        internal NativeMaintenanceBundle(string packageRoot, string gameRoot, string? recoveryPath = null)
        {
            PackageRoot = WindowsPath.Absolute(packageRoot).TrimEnd('\\'); GameRoot = WindowsPath.Absolute(gameRoot).TrimEnd('\\');
            if (PackageRoot.Equals(GameRoot, StringComparison.OrdinalIgnoreCase) || PackageRoot.StartsWith(GameRoot + "\\", StringComparison.OrdinalIgnoreCase) || GameRoot.StartsWith(PackageRoot + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("설치 묶음과 게임 폴더는 분리되어야 합니다. / Package and game directories must be separate.");
            Source = new WindowsFileFence(PackageRoot);
            try {
                Capture("package-manifest.json"); var manifestBytes = Bytes("package-manifest.json", 1024 * 1024);
                ManifestHash = UpdateTrust.Hash(manifestBytes); var manifest = MaintenanceJson.Read(manifestBytes);
                if (MaintenanceJson.Number(manifest, "schema") != 1) throw new InvalidDataException("Invalid package schema.");
                Version = MaintenanceJson.Text(manifest, "version"); var kind = MaintenanceJson.Text(manifest, "kind");
                if (kind.Length == 0 || UpdateVersion.Parse(Version).CompareTo(UpdateVersion.Parse("1.3.0-0")) < 0) throw new InvalidDataException("New 1.3.0 installation baseline required.");
                var files = MaintenanceJson.List(manifest, "files"); if (files.Count < 10) throw new InvalidDataException("Incomplete installation manifest.");
                foreach (var raw in files) {
                    var row = MaintenanceJson.Map(raw); var name = MaintenanceJson.Text(row, "path"); var hash = MaintenanceJson.Text(row, "sha256"); var size = MaintenanceJson.Number(row, "bytes");
                    Source.Full(name);
                    if (!MaintenanceJson.Hash(hash) || size < 0 || Hashes.ContainsKey(name)) throw new InvalidDataException("Invalid/duplicate package inventory.");
                    Hashes.Add(name, hash);
                    _sizes.Add(name, size);
                }
                Bytes("artifacts/BetterAstralParty-Launcher.exe", UpdateTrust.MaxFileBytes);
                foreach (var name in new[] { "Install.cmd", "Uninstall.cmd", "tools/approved-install-bundles.json", "helper-bootstrap/BetterAstralParty-UpdateHelper.exe", "helper-bootstrap/helper.descriptor", "helper-bootstrap/helper.signature", "update-proof/update.descriptor", "update-proof/update.signature" })
                    if (!Hashes.ContainsKey(name)) throw new InvalidDataException("필수 설치 파일이 없습니다. / Required package file missing: " + name);
                foreach (var entry in OwnedSources) {
                    string hash; if (!Hashes.TryGetValue(entry.Value, out hash!)) throw new InvalidDataException("Missing owned installation file: " + entry.Value); Expected.Add(entry.Key, hash);
                }
                var ownPath = WindowsPath.FromIo(typeof(SteamLauncher).Assembly.Location);
                if (!ownPath.Equals(Source.Full("artifacts/BetterAstralParty-Launcher.exe"), StringComparison.OrdinalIgnoreCase) || _files["artifacts/BetterAstralParty-Launcher.exe"].Hash() != Expected["BetterAstralParty-Launcher.exe"])
                    throw new InvalidDataException("The exact incoming package launcher must execute maintenance.");
                var names = new List<string>(Hashes.Keys); names.RemoveAll(n => n.Equals("tools/approved-install-bundles.json", StringComparison.OrdinalIgnoreCase)); names.Sort(StringComparer.Ordinal);
                var identity = new StringBuilder("BetterAstralParty.InstallBundle/v1\nversion=").Append(Version).Append("\nkind=").Append(kind).Append("\nprotocol=1\n");
                foreach (var name in names) identity.Append(name).Append('=').Append(Hashes[name]).Append('\n');
                BuildIdentity = UpdateTrust.Hash(Encoding.UTF8.GetBytes(identity.ToString()));
                if (MaintenanceJson.Text(manifest, "buildIdentity") != BuildIdentity) throw new InvalidDataException("Bundle identity mismatch.");
                var catalog = MaintenanceJson.Read(Bytes("tools/approved-install-bundles.json", 1024 * 1024));
                if (MaintenanceJson.Number(catalog, "schema") != 1) throw new InvalidDataException("Invalid build review catalog.");
                var approved = 0;
                foreach (var item in MaintenanceJson.List(catalog, "bundles")) {
                    var row = MaintenanceJson.Map(item);
                    if (MaintenanceJson.Text(row, "buildIdentity") == BuildIdentity && MaintenanceJson.Text(row, "version") == Version && MaintenanceJson.Text(row, "kind") == kind && MaintenanceJson.Number(row, "protocol") == 1) approved++;
                }
                if (approved != 1) throw new InvalidDataException("검토 등록되지 않은 설치 묶음입니다. / Unregistered installation bundle.");
                var proof = WholeInstallBridge.VerifyUpdateProof(Bytes("update-proof/update.descriptor", UpdateTrust.MaxDescriptorBytes), Bytes("update-proof/update.signature", 1024));
                if (proof.Version != Version || proof.Protocol != 4 || proof.SettingsSchema != 1 || proof.Files.Count != OwnedSources.Count || SteamLauncher.InstallBundleProtocol != 1)
                    throw new InvalidDataException("Incompatible installation components.");
                foreach (var row in proof.Files) {
                    string source;
                    if (!OwnedSources.TryGetValue(row.Path, out source!) || Expected[row.Path] != row.Sha256 || _sizes[source] != row.Bytes)
                        throw new InvalidDataException("Signed installation inventory mismatch.");
                }
                using (var game = new WindowsFileFence(GameRoot)) RootIdentity = game.RootIdentity.Text;
                if (!string.IsNullOrEmpty(recoveryPath)) {
                    using (var input = File.Open(WindowsPath.ForIo(WindowsPath.Absolute(recoveryPath!)), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        if (input.Length > 1024 * 1024) throw new InvalidDataException("Recovery plan exceeds limit.");
                        using (var memory = new MemoryStream()) { input.CopyTo(memory); Recovery = MaintenanceJson.Read(memory.ToArray()); }
                    }
                    if (MaintenanceJson.Number(Recovery, "schema") != 1 || MaintenanceJson.Text(Recovery, "purpose") != "manual-recovery" || MaintenanceJson.Text(Recovery, "packageManifestSha256") != ManifestHash || MaintenanceJson.Text(Recovery, "gameRoot") != GameRoot || MaintenanceJson.Text(Recovery, "gameRootIdentity") != RootIdentity || MaintenanceJson.Text(Recovery, "toVersion") != Version)
                        throw new InvalidDataException("Recovery plan binding mismatch.");
                }
                AssertRoot();
            } catch { Dispose(); throw; }
        }
        private void Capture(string path)
        {
            if (_files.ContainsKey(path)) throw new InvalidDataException("Duplicate captured package file.");
            try { _files.Add(path, Source.OpenLaunchImage(path)); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) {
                throw new InvalidDataException("BAP-PACKAGE-INCOMPLETE: 설치 묶음 파일이 없습니다. 공식 전체 ZIP을 새 폴더에 압축 해제하세요. / Package file missing: " + path + "; extract the official complete ZIP into a new folder.", error);
            }
        }
        internal byte[] Bytes(string path, long limit)
        {
            if (NativeMaintenanceFiles.IsDocumentation(path)) throw new InvalidOperationException("Distribution-only documentation is not a maintenance input.");
            if (!_files.ContainsKey(path)) {
                if (!Hashes.ContainsKey(path)) throw new InvalidDataException("Unlisted package input: " + path);
                Capture(path);
                var captured = _files[path];
                try {
                    if (captured.Stream.Length != _sizes[path] || captured.Hash() != Hashes[path])
                        throw new InvalidDataException("설치 묶음 파일이 변경되었습니다. / Package hash/size mismatch: " + path);
                } catch { _files.Remove(path); captured.Dispose(); throw; }
            }
            var file = _files[path]; file.Recheck(); file.Stream.Position = 0; return UpdateTicket.Read(file, limit);
        }
        internal string? TargetHash(string relative)
        {
            using (var game = new WindowsFileFence(GameRoot)) {
                try { using (var file = game.OpenLaunchImage(relative)) return file.Hash(); }
                catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
            }
        }
        internal byte[]? ReadTarget(string relative, int maxBytes = 64 * 1024 * 1024)
        {
            using (var game = new WindowsFileFence(GameRoot)) {
                try { using (var file = game.OpenLaunchImage(relative)) return UpdateTicket.Read(file, maxBytes); }
                catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
            }
        }
        internal void AssertRoot()
        {
            using (var game = new WindowsFileFence(GameRoot)) if (game.RootIdentity.Text != RootIdentity) throw new InvalidDataException("Installation root identity changed.");
            Source.Recheck(); foreach (var file in _files.Values) file.Recheck();
        }
        internal void AssertUnchanged()
        {
            AssertRoot(); if (Observed == null) throw new InvalidOperationException("Installation observation missing.");
            foreach (var item in Observed) if (TargetHash(item.Key) != item.Value) throw new InvalidDataException("Installation changed after preflight: " + item.Key);
        }
        public void Dispose() { foreach (var file in _files.Values) file.Dispose(); _files.Clear(); Source.Dispose(); }
    }
}
