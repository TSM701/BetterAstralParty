#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    internal sealed class SteamProfileUnavailableException : InvalidOperationException
    {
        internal readonly string Reason;
        internal SteamProfileUnavailableException(string reason) : base("Steam shortcut discovery unavailable: " + reason) { Reason = reason; }
    }
    internal sealed class SteamSessionState { internal string? Root; internal bool Restart; }
    internal sealed class SteamArtworkRecovery
    {
        internal string Path = "", Backup = "", Hash = "";
    }
    internal sealed class ShortcutRecovery
    {
        internal string Path = "", Hash = "", BackupHash = "";
        internal string? Backup;
        internal readonly List<SteamArtworkRecovery> Artwork = new List<SteamArtworkRecovery>();
    }
    internal sealed class SteamProcessObservation
    {
        internal uint Pid, Session;
        internal long Creation;
        internal string Name = "", Image = "";
        internal bool Available, Exited, Client;
        internal bool? PathMatch, SessionMatch;
        internal int NativeError;
    }
    internal sealed class SteamProcessStatus
    {
        internal string State = "Stopped";
        internal readonly List<SteamProcessObservation> Facts = new List<SteamProcessObservation>();
        internal readonly List<SteamProcessObservation> Targets = new List<SteamProcessObservation>();
    }

    // Native maintenance shares the selected-root and fresh-consent contract with steam-session.ps1.
    internal static class NativeSteamMaintenance
    {
        private const string ShortcutName = "Astral Party - Mod";
        private static readonly string[] ClientNames = { "steam", "steamwebhelper", "GameOverlayUI", "steamerrorreporter", "steamerrorreporter64" };
        private static readonly string[] GameNames = { "AstralParty_INT", "AstralParty_CN", "BetterAstralParty-Launcher" };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly RegexOptions Rx = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        internal static Action<Exception>? WaitingDiagnostic = null;
#if BAP_SAFETY_FIXTURE
        internal static Func<SteamProcessStatus>? SteamStatusObserver = null;
        internal static Func<IEnumerable<SteamProcessObservation>>? ProcessObserver = null;
#endif
        private static string PathOf(string value) { return WindowsPath.Absolute(value.Replace('/', '\\')); }
        private static string Io(string value) { return WindowsPath.ForIo(value); }
        private static string Join(string root, string relative) { return WindowsPath.Absolute(root.TrimEnd('\\') + "\\" + relative.Replace('/', '\\')); }
        private static bool Under(string image, string root) { return image.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
        private static bool Named(string name, string[] names) { return names.Contains(name, StringComparer.OrdinalIgnoreCase); }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private static byte[] Read(string path, int maximum)
        {
            using (var file = new FileStream(Io(path), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (file.Length > maximum) throw new InvalidOperationException("Steam metadata is too large; no files changed.");
                var bytes = new byte[(int)file.Length]; var at = 0;
                while (at < bytes.Length) { var count = file.Read(bytes, at, bytes.Length - at); if (count == 0) throw new EndOfStreamException(); at += count; }
                return bytes;
            }
        }
        internal static string? ResolveSteamRoot()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) {
                var value = key?.GetValue("SteamPath") as string;
                return string.IsNullOrWhiteSpace(value) ? null : PathOf(value!);
            }
        }
        internal static string ResolveUserPath(string supplied)
        {
            var value = supplied.Trim().Trim('"').Replace('/', '\\');
            try { return WindowsPath.Absolute(value); } catch (ArgumentException) { }
            var absolute = Regex.IsMatch(value, @"\A[A-Za-z]:\\");
            if (value.Length == 0 || value.Length > WindowsPath.MaximumLength || value.StartsWith("\\", StringComparison.Ordinal)
                || value.IndexOf(':', absolute ? 2 : 0) >= 0) throw new ArgumentException("Unsafe ordinary Windows path.");
            foreach (var part in (absolute ? value.Substring(3) : value).TrimEnd('\\').Split('\\'))
                if (part != "." && part != ".." && !WindowsPath.SafeComponent(part)) throw new ArgumentException("Unsafe Windows path component.");
            if (absolute) return PathOf(Path.GetFullPath(value));
            var captured = Environment.GetEnvironmentVariable("BAP_MAINTENANCE_CWD");
            var basis = PathOf(string.IsNullOrWhiteSpace(captured) ? Environment.CurrentDirectory : captured!);
            return PathOf(Path.GetFullPath(Path.Combine(basis, value)));
        }
        internal static string? ResolveIntDirectory(string? supplied)
        {
            if (string.IsNullOrWhiteSpace(supplied)) return null;
            var root = ResolveUserPath(supplied!);
            foreach (var candidate in new[] { root, Join(root, "8vJXnINT") })
                if (File.Exists(Io(Join(candidate, "AstralParty_INT.exe")))) return candidate;
            return null;
        }
        internal static List<string> LibraryRoots(string steamRoot, string? text)
        {
            var roots = new List<string> { PathOf(steamRoot) };
            if (text != null) foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", Rx))
                roots.Add(PathOf(match.Groups[1].Value.Replace(@"\\", @"\")));
            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal static string? InstallDirectoryName(string text)
        {
            var fields = Regex.Matches(text, "\"installdir\"\\s+\"([^\"]+)\"", Rx);
            if (fields.Count != 1 || !WindowsPath.SafeComponent(fields[0].Groups[1].Value)) return null;
            return fields[0].Groups[1].Value;
        }
        internal static string ResolveGame(string? supplied, string? steamRoot, string packageRoot, Action<string>? diagnostics = null)
        {
            if (!string.IsNullOrWhiteSpace(supplied)) return ResolveIntDirectory(supplied) ?? throw new InvalidOperationException("INT game directory unavailable. / INT 게임 폴더를 찾을 수 없습니다.");
            var distinct = FindGameDirectories(steamRoot, packageRoot, diagnostics);
            if (distinct.Count == 1) return distinct[0];
            throw new InvalidOperationException(distinct.Count == 0 ? "INT game directory unavailable; select the installation folder. / INT 게임 설치 폴더를 지정하세요." : "Multiple INT installations found; select the installation folder. / 여러 INT 설치가 발견되었습니다. 폴더를 지정하세요.");
        }
        internal static List<string> FindGameDirectories(string? steamRoot, string packageRoot, Action<string>? diagnostics = null)
        {
            var found = new List<string>();
            var logged = 0; var omitted = 0;
            Action<string, Exception> unavailable = (kind, error) => { if (logged++ < 8) DiscoveryDiagnostic(kind, error, diagnostics); else omitted++; };
            Action<string> candidate = value => { try { var resolved = ResolveIntDirectory(value); if (resolved != null) found.Add(resolved); } catch (ArgumentException) { } };
            var package = PathOf(packageRoot); candidate(package); candidate(WindowsPath.Parent(package));
            if (!string.IsNullOrWhiteSpace(steamRoot)) {
                var steam = PathOf(steamRoot!); string? libraries = null;
                try { var path = Join(steam, "steamapps/libraryfolders.vdf"); if (File.Exists(Io(path))) libraries = Utf8.GetString(Read(path, 1048576)); }
                catch (Exception error) { unavailable("LibraryFolders", error); }
                var roots = new List<string> { steam };
                if (libraries != null) foreach (Match match in Regex.Matches(libraries, "\"path\"\\s+\"([^\"]+)\"", Rx))
                    try { roots.Add(PathOf(match.Groups[1].Value.Replace(@"\\", @"\"))); } catch (ArgumentException error) { unavailable("LibraryFoldersPath", error); }
                foreach (var library in roots) {
                    if (!Directory.Exists(Io(library))) continue;
                    candidate(Join(library, "steamapps/common/Astral Party"));
                    try {
                        var manifest = Join(library, "steamapps/appmanifest_2622000.acf");
                        if (File.Exists(Io(manifest))) { var dir = InstallDirectoryName(Utf8.GetString(Read(manifest, 1048576))); if (dir != null) candidate(Join(library, "steamapps/common/" + dir)); }
                    } catch (Exception error) { unavailable("AppManifest", error); }
                }
            }
            if (omitted != 0) try { diagnostics?.Invoke("NativeSteamDiscovery omitted=" + omitted); } catch { }
            return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        private static void DiscoveryDiagnostic(string kind, Exception error, Action<string>? diagnostics)
        {
            // Discovery remains read-only when an unavailable library does not hide another installation.
            try { diagnostics?.Invoke("NativeSteamDiscovery kind=" + kind + ";exception=" + error.GetType().Name + ";native=" + error.HResult); } catch { }
        }
        internal static uint ResolveAccount(string steamRoot, uint requested)
        {
            RequireSteamDirectory(PathOf(steamRoot));
            uint active = 0; string auto = "";
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) auto = key?.GetValue("AutoLoginUser") as string ?? "";
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess")) { var value = key?.GetValue("ActiveUser"); if (value is int number) active = unchecked((uint)number); }
            var root = PathOf(steamRoot); var saved = Join(root, "config/loginusers.vdf");
            // Explicit and active profiles take precedence even when saved metadata cannot be read.
            if (HasProfile(root, requested)) return requested;
            if (requested != 0) throw new InvalidOperationException("Requested Steam profile has no local configuration directory.");
            if (HasProfile(root, active)) return active;
            string? metadata;
            try { metadata = Utf8.GetString(Read(saved, 1048576)); }
            catch (FileNotFoundException) { metadata = null; }
            catch (DirectoryNotFoundException) { metadata = null; }
            return ResolveAccountFromMetadata(root, requested, active, auto, metadata);
        }
        private static bool HasProfile(string root, uint id) { return id != 0 && ProfileDirectoryExists(Join(root, "userdata/" + id + "/config")); }
        private static bool ProfileDirectoryExists(string path)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(Io(path)); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Steam profile paths must be ordinary directories.");
            return true;
        }
        private static void RequireSteamDirectory(string root)
        { if (!ProfileDirectoryExists(root)) throw new SteamProfileUnavailableException("SteamRootAbsent"); }
        internal static uint ResolveAccountFromMetadata(string steamRoot, uint requested, uint active, string? autoLogin, string? text)
        {
            var root = PathOf(steamRoot);
            RequireSteamDirectory(root);
            if (requested != 0) { if (!HasProfile(root, requested)) throw new InvalidOperationException("Requested Steam profile has no local configuration directory."); return requested; }
            if (HasProfile(root, active)) return active;
            var automatic = new List<uint>(); var recent = new List<uint>();
            if (text != null) {
                if (Utf8.GetByteCount(text) > 1048576) throw new SteamProfileUnavailableException("SteamProfileMetadataInvalid");
                foreach (Match match in Regex.Matches(text, "\"(?<id>\\d{17})\"\\s*\\{(?<body>[^{}]*)\\}", Rx)) {
                    var steamId = ulong.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (steamId <= 76561197960265728UL || steamId > 76561202255233023UL) continue;
                    var id = (uint)(steamId - 76561197960265728UL); if (!HasProfile(root, id)) continue;
                    var body = match.Groups["body"].Value;
                    var names = Regex.Matches(body, "\"AccountName\"\\s*\"([^\"\\r\\n]*)\"", Rx);
                    var flags = Regex.Matches(body, "\"MostRecent\"\\s*\"([01])\"", Rx);
                    if (names.Count > 1 || flags.Count > 1) throw new SteamProfileUnavailableException("SteamProfileAmbiguous");
                    if (!string.IsNullOrEmpty(autoLogin) && names.Count == 1 && string.Equals(names[0].Groups[1].Value, autoLogin, StringComparison.OrdinalIgnoreCase)) automatic.Add(id);
                    if (flags.Count == 1 && flags[0].Groups[1].Value == "1") recent.Add(id);
                }
            }
            if (automatic.Count > 1 || recent.Count > 1 || automatic.Count == 1 && recent.Count == 1 && automatic[0] != recent[0])
                throw new SteamProfileUnavailableException("SteamProfileAmbiguous");
            if (automatic.Count == 1) return automatic[0]; if (recent.Count == 1) return recent[0];
            var profiles = new List<uint>(); var userdata = Join(root, "userdata");
            if (ProfileDirectoryExists(userdata)) foreach (var directory in WindowsPath.Directories(userdata, "*")) {
                uint id; var name = WindowsPath.Leaf(directory);
                if (uint.TryParse(name, out id) && name == id.ToString(System.Globalization.CultureInfo.InvariantCulture) && HasProfile(root, id)) profiles.Add(id);
            }
            if (profiles.Count == 1) return profiles[0];
            if (profiles.Count == 0) throw new SteamProfileUnavailableException("SteamProfileAbsent");
            throw new SteamProfileUnavailableException("SteamProfileAmbiguous");
        }
        private static string Zero(BinaryReader reader)
        {
            var bytes = new List<byte>(); byte item;
            while ((item = reader.ReadByte()) != 0) { if (bytes.Count >= 1048576) throw new InvalidDataException("Invalid shortcut string length."); bytes.Add(item); }
            return Utf8.GetString(bytes.ToArray());
        }
        private static OrderedDictionary Object(BinaryReader reader, int depth)
        {
            if (depth > 16) throw new InvalidDataException("Invalid shortcut nesting.");
            var result = new OrderedDictionary(StringComparer.OrdinalIgnoreCase); byte type;
            while ((type = reader.ReadByte()) != 8) {
                var key = Zero(reader); if (result.Contains(key)) throw new InvalidDataException("Duplicate shortcut key.");
                object value;
                if (type == 0) value = Object(reader, depth + 1); else if (type == 1) value = Zero(reader); else if (type == 2) value = reader.ReadUInt32();
                else throw new InvalidDataException("Unsupported shortcut field type: " + type);
                result.Add(key, value);
            }
            return result;
        }
        internal static OrderedDictionary ReadShortcuts(byte[] bytes)
        {
            if (bytes.Length > 16 * 1048576) throw new InvalidDataException("Steam shortcuts exceed the supported limit.");
            using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream)) {
                var root = Object(reader, 0);
                if (stream.Position != stream.Length || root.Count != 1 || !(root["shortcuts"] is OrderedDictionary)) throw new InvalidDataException("Invalid shortcuts root.");
                return root;
            }
        }
        private static void Zero(BinaryWriter writer, string text)
        {
            if (text.IndexOf('\0') >= 0 || Utf8.GetByteCount(text) > 1048576) throw new InvalidDataException("Invalid shortcut string.");
            writer.Write(Utf8.GetBytes(text)); writer.Write((byte)0);
        }
        private static void Object(BinaryWriter writer, OrderedDictionary value)
        {
            foreach (DictionaryEntry field in value) {
                var item = field.Value;
                writer.Write(item is OrderedDictionary ? (byte)0 : item is string ? (byte)1 : item is uint ? (byte)2 : throw new InvalidDataException("Unsupported shortcut value."));
                Zero(writer, (string)field.Key);
                if (item is OrderedDictionary child) Object(writer, child); else if (item is string text) Zero(writer, text); else writer.Write((uint)item!);
            }
            writer.Write((byte)8);
        }
        internal static byte[] WriteShortcuts(OrderedDictionary root)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) { Object(writer, root); return stream.ToArray(); }
        }
        internal static OrderedDictionary EmptyShortcuts() { var root = new OrderedDictionary(StringComparer.OrdinalIgnoreCase); root.Add("shortcuts", new OrderedDictionary(StringComparer.OrdinalIgnoreCase)); return root; }
        internal static List<string> OwnedShortcutKeys(OrderedDictionary root, string gameRoot)
        {
            var exe = "\"" + Join(PathOf(gameRoot), "BetterAstralParty-Launcher.exe") + "\""; var keys = new List<string>();
            foreach (DictionaryEntry field in (OrderedDictionary)root["shortcuts"]!) if (field.Value is OrderedDictionary item &&
                string.Equals(item["AppName"] as string, ShortcutName, StringComparison.OrdinalIgnoreCase) && string.Equals(item["exe"] as string, exe, StringComparison.OrdinalIgnoreCase) && item["LaunchOptions"] is string options && options.Length == 0) keys.Add((string)field.Key);
            if (keys.Count > 1) throw new InvalidOperationException("Duplicate mod shortcuts; refusing to choose one.");
            return keys;
        }
        private static string ShortcutPath(string steamRoot, uint account)
        {
            var root = PathOf(steamRoot); if (!HasProfile(root, account)) throw new InvalidOperationException("Steam account configuration directory missing.");
            return Join(root, "userdata/" + account + "/config/shortcuts.vdf");
        }
        private static WindowsFileFence.FileLease? Optional(WindowsFileFence fence, string relative, bool mutable = false)
        {
            try { return fence.OpenFile(relative, mutable); }
            catch (ApplySafetyException error) when (error.NativeError == 2 || error.NativeError == 3) { return null; }
        }
        private static OrderedDictionary LoadShortcuts(string path)
        {
            using (var fence = new WindowsFileFence(WindowsPath.Parent(path))) using (var file = Optional(fence, WindowsPath.Leaf(path)))
                return file == null ? EmptyShortcuts() : ReadShortcuts(UpdateTicket.Read(file, 16 * 1048576));
        }
        internal static bool PlanRegister(string steamRoot, uint account, string gameRoot, Action<Exception>? unavailable = null) { return PlanShortcut(steamRoot, account, gameRoot, false, unavailable); }
        internal static bool PlanRemove(string steamRoot, uint account, string gameRoot, Action<Exception>? unavailable = null) { return PlanShortcut(steamRoot, account, gameRoot, true, unavailable); }
        private static bool PlanShortcut(string steamRoot, uint account, string gameRoot, bool remove, Action<Exception>? unavailable)
        {
            try { return OwnedShortcutKeys(LoadShortcuts(ShortcutPath(steamRoot, account)), gameRoot).Count == (remove ? 1 : 0); }
            catch (Exception error) when (unavailable != null && (error is ApplySafetyException || error is IOException || error is InvalidDataException
                || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is InvalidOperationException || error is ArgumentException))
            {
                try { unavailable(error); } catch { }
                return false;
            }
        }
        internal static ShortcutRecovery? Register(string steamRoot, uint account, string gameRoot, Action<string>? output = null) { return Mutate(steamRoot, account, gameRoot, false, output); }
        internal static ShortcutRecovery? Remove(string steamRoot, uint account, string gameRoot, Action<string>? output = null) { return Mutate(steamRoot, account, gameRoot, true, output); }
        private static ShortcutRecovery? Mutate(string steamRoot, uint account, string gameRoot, bool remove, Action<string>? output)
        {
            var steam = PathOf(steamRoot); var game = PathOf(gameRoot); var path = ShortcutPath(steam, account);
            using var configFence = new WindowsFileFence(WindowsPath.Parent(path));
            byte[]? original;
            using (var file = Optional(configFence, "shortcuts.vdf")) original = file == null ? null : UpdateTicket.Read(file, 16 * 1048576);
            var root = original == null ? EmptyShortcuts() : ReadShortcuts(original); var items = (OrderedDictionary)root["shortcuts"]!; var owned = OwnedShortcutKeys(root, game);
            if (remove && owned.Count == 0 || !remove && owned.Count == 1) return null;
            AssertStopped(steam, "ShortcutPreflight");
            uint appId;
            if (remove) { var item = (OrderedDictionary)items[owned[0]]!; if (!(item["appid"] is uint id)) throw new InvalidDataException("Owned shortcut app ID is invalid."); appId = id; items.Remove(owned[0]); }
            else {
                var launcher = Join(game, "BetterAstralParty-Launcher.exe"); if (!File.Exists(Io(launcher))) throw new FileNotFoundException("Missing installed launcher.");
                appId = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) | 0x80000000U;
                while (items.Values.Cast<object>().OfType<OrderedDictionary>().Any(x => x["appid"] is uint id && id == appId)) appId = checked(appId + 1);
                var index = 0; while (items.Contains(index.ToString(System.Globalization.CultureInfo.InvariantCulture))) index++;
                var item = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
                item.Add("appid", appId); item.Add("AppName", ShortcutName); item.Add("exe", "\"" + launcher + "\""); item.Add("StartDir", "\"" + game + "\""); item.Add("icon", Join(game, "AstralParty_INT.exe"));
                item.Add("ShortcutPath", ""); item.Add("LaunchOptions", ""); item.Add("IsHidden", 0U); item.Add("AllowDesktopConfig", 1U); item.Add("AllowOverlay", 1U); item.Add("OpenVR", 0U); item.Add("Devkit", 0U); item.Add("DevkitGameID", ""); item.Add("LastPlayTime", 0U); item.Add("tags", new OrderedDictionary(StringComparer.OrdinalIgnoreCase));
                items.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture), item);
            }
            var bytes = WriteShortcuts(root); ReadShortcuts(bytes);
            var recovery = new ShortcutRecovery { Path = path, Hash = Hash(bytes), BackupHash = original == null ? "" : Hash(original) };
            using (var temp = configFence.OpenFile("shortcuts.vdf." + Guid.NewGuid().ToString("N") + ".tmp", true, true)) {
                temp.Stream.Write(bytes, 0, bytes.Length); temp.Flush();
                AssertStopped(steam, "ShortcutCommit");
                using (var held = Optional(configFence, "shortcuts.vdf", true)) try {
                    if (original == null ? held != null : held == null || held.Hash() != recovery.BackupHash) throw new InvalidOperationException("Steam shortcuts changed; cancelled.");
                    if (held != null) {
                        var backup = "shortcuts.vdf.BetterAstralParty-" + Guid.NewGuid().ToString("N") + ".bak";
                        held.RenameTo(configFence, backup); recovery.Backup = held.Path;
                        if (held.Hash() != recovery.BackupHash) throw new InvalidOperationException("Steam shortcut backup changed; retained for review.");
                    }
                    temp.RenameTo(configFence, "shortcuts.vdf");
                } catch (Exception error) {
                    error.Data["BapShortcutRecovery"] = recovery;
                    if (recovery.Backup != null && held != null) try { CopyAbsent(configFence, held, "shortcuts.vdf"); } catch { }
                    throw;
                }
            }
            if (remove) recovery.Artwork.AddRange(RemoveHero(WindowsPath.Parent(path), appId, game, output));
            try { output?.Invoke(remove ? "Removed Astral Party - Mod. / Mod 바로가기를 제거했습니다." : "Registered Astral Party - Mod. Restart Steam to load it. / Steam을 다시 실행해 Mod 바로가기를 불러오세요."); } catch { }
            return recovery;
        }
        private static IEnumerable<SteamArtworkRecovery> RemoveHero(string config, uint id, string game, Action<string>? output)
        {
            var image = Join(config, "grid/" + id + "_hero.png"); var receipt = image + ".BetterAstralParty.json"; var moved = new List<SteamArtworkRecovery>();
            var held = new List<WindowsFileFence.FileLease>();
            WindowsFileFence? fence = null;
            try {
                fence = new WindowsFileFence(config);
                var proof = Optional(fence, "grid/" + id + "_hero.png.BetterAstralParty.json", true); if (proof == null) return moved; held.Add(proof);
                var fields = new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 8 }.DeserializeObject(Utf8.GetString(UpdateTicket.Read(proof, 4096))) as Dictionary<string, object>;
                if (fields == null || fields.Count != 4 || !fields.TryGetValue("Owner", out var owner) || !(owner is string ownerText) || ownerText != "kr.betterastralparty.mod" ||
                    !fields.TryGetValue("GameRoot", out var root) || !(root is string rootText) || !string.Equals(rootText, game, StringComparison.OrdinalIgnoreCase) ||
                    !fields.TryGetValue("AppId", out var app) || !(app is int || app is long) || Convert.ToInt64(app) != id ||
                    !fields.TryGetValue("Hash", out var hash) || !(hash is string imageHash) || !Regex.IsMatch(imageHash, @"\A[A-Fa-f0-9]{64}\z")) return moved;
                var targets = new List<WindowsFileFence.FileLease> { proof }; var art = Optional(fence, "grid/" + id + "_hero.png", true);
                if (art != null) { held.Add(art); if (string.Equals(art.Hash(), imageHash, StringComparison.OrdinalIgnoreCase)) targets.Insert(0, art); }
                foreach (var target in targets) {
                    var item = new SteamArtworkRecovery { Path = target.Path, Hash = target.Hash() };
                    var relative = target.Path.Substring(config.Length + 1).Replace('\\', '/') + ".BetterAstralParty-" + Guid.NewGuid().ToString("N") + ".bak";
                    target.RenameTo(fence, relative); item.Backup = target.Path; moved.Add(item);
                }
            } catch (Exception error) {
                foreach (var item in moved) try { if (fence != null) CopyAbsent(fence, held.Single(file => file.Path == item.Backup), item.Path.Substring(config.Length + 1).Replace('\\', '/')); } catch { }
                try { output?.Invoke("Hero cleanup retained for review. / Steam 배경 정리는 보류되었습니다. reason=" + error.GetType().Name); } catch { }
            } finally { foreach (var file in held) file.Dispose(); fence?.Dispose(); }
            return moved;
        }
        private static void CopyAbsent(WindowsFileFence fence, WindowsFileFence.FileLease source, string relative)
        {
            using (var exists = Optional(fence, relative)) if (exists != null) return;
            using (var target = fence.OpenFile(relative, true, true)) { source.Recheck(); source.Stream.Position = 0; source.Stream.CopyTo(target.Stream); target.Flush(); if (target.Hash() != source.Hash()) throw new IOException("Steam recovery copy failed."); }
        }
        internal static void Rollback(ShortcutRecovery recovery)
        {
            var path = PathOf(recovery.Path);
            var steamRoot = WindowsPath.Parent(WindowsPath.Parent(WindowsPath.Parent(WindowsPath.Parent(path))));
            AssertStopped(steamRoot, "ShortcutRollback");
            var config = WindowsPath.Parent(path); using var fence = new WindowsFileFence(config);
            if (WindowsPath.Leaf(path) != "shortcuts.vdf") throw new InvalidDataException("Steam recovery path invalid.");
            var backupName = recovery.Backup == null ? null : RecoveryRelative(config, path, recovery.Backup);
            using var backup = backupName == null ? null : fence.OpenFile(backupName);
            if (backup != null && (recovery.BackupHash.Length != 64 || backup.Hash() != recovery.BackupHash)) throw new InvalidOperationException("Steam shortcut backup changed; rollback retained for review.");
            using var current = fence.OpenFile("shortcuts.vdf", true);
            if (current.Hash() != recovery.Hash) throw new InvalidOperationException("Steam shortcut changed after removal; rollback retained for review.");
            foreach (var item in recovery.Artwork) {
                if (!Regex.IsMatch(item.Path.Substring(config.Length + 1).Replace('\\', '/'), @"\Agrid/[0-9]+_hero\.png(?:\.BetterAstralParty\.json)?\z")) throw new InvalidDataException("Steam artwork recovery path invalid.");
                using var source = fence.OpenFile(RecoveryRelative(config, item.Path, item.Backup));
                if (item.Hash.Length != 64 || source.Hash() != item.Hash) throw new InvalidOperationException("Steam artwork backup changed; retained for review.");
                CopyAbsent(fence, source, item.Path.Substring(config.Length + 1).Replace('\\', '/'));
            }
            AssertStopped(steamRoot, "ShortcutRollbackCommit");
            current.RenameTo(fence, "shortcuts.vdf.BetterAstralParty-rollback-" + Guid.NewGuid().ToString("N") + ".bak");
            if (backup != null) CopyAbsent(fence, backup, "shortcuts.vdf");
        }
        private static string RecoveryRelative(string config, string target, string backup)
        {
            if (!PathOf(target).StartsWith(config + "\\", StringComparison.OrdinalIgnoreCase) || !backup.StartsWith(target + ".BetterAstralParty-", StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(backup.Substring(target.Length), @"\A\.BetterAstralParty-[0-9a-f]{32}\.bak\z")) throw new InvalidDataException("Steam backup recovery path invalid.");
            return PathOf(backup).Substring(config.Length + 1).Replace('\\', '/');
        }
        private static List<SteamProcessObservation> Observe()
        {
#if BAP_SAFETY_FIXTURE
            if (ProcessObserver != null) return ProcessObserver().ToList();
#endif
            var observations = new List<SteamProcessObservation>();
            int own; using (var current = Process.GetCurrentProcess()) own = current.Id;
            foreach (var process in Process.GetProcesses()) using (process) {
                if (process.Id == own) continue;
                var item = new SteamProcessObservation { Pid = (uint)process.Id };
                try { item.Name = process.ProcessName; } catch { }
                try { using (var captured = WindowsProcessGuard.Observe(item.Pid, true)) { item.Exited = captured.Exited; item.Available = true; item.Image = captured.Image; item.Session = captured.Session; item.Creation = captured.Creation; if (item.Image.Length != 0) item.Name = Path.GetFileNameWithoutExtension(WindowsPath.Leaf(item.Image)); } }
                catch (ApplySafetyException error) { item.NativeError = error.NativeError; }
                catch { }
                observations.Add(item);
            }
            return observations;
        }
        internal static SteamProcessStatus ClassifySteam(IEnumerable<SteamProcessObservation> observations, string steamRoot, uint session)
        {
            var status = new SteamProcessStatus(); var root = PathOf(steamRoot); bool unknown = false, other = false;
            foreach (var item in observations) {
                if (item.Exited) continue;
                if (item.Name.Length == 0 && item.Image.Length == 0) { status.Facts.Add(item); unknown = true; continue; }
                if (!Named(item.Name, ClientNames)) continue;
                item.PathMatch = item.Image.Length == 0 ? (bool?)null : Under(item.Image, root);
                item.SessionMatch = item.Available ? (bool?)(item.Session == session) : null;
                status.Facts.Add(item);
                if (item.Image.Length != 0 && !Under(item.Image, root)) continue;
                if (!item.Available || item.Image.Length == 0 || item.Creation <= 0) { unknown = true; continue; }
                if (item.Session != session) { other = true; continue; }
                item.Client = true; status.Targets.Add(item);
            }
            status.State = unknown ? "Unknown" : other ? "OtherSession" : status.Targets.Count != 0 ? "Running" : "Stopped"; return status;
        }
        private static uint Session { get { using (var current = Process.GetCurrentProcess()) return (uint)current.SessionId; } }
        private static SteamProcessStatus Status(string root) {
#if BAP_SAFETY_FIXTURE
            if (SteamStatusObserver != null) return SteamStatusObserver();
#endif
            return ClassifySteam(Observe(), root, Session);
        }
        private static Exception Failure(string reason, string phase, SteamProcessStatus? status = null)
        {
            var error = new InvalidOperationException("Steam maintenance stopped: " + reason + ". / Steam 확인 후 다시 시도하세요.");
            AddTypedFailure(error, reason, phase, status);
            try {
                var facts = status?.Facts ?? new List<SteamProcessObservation>(); var bounded = facts.OrderBy(x => x.Pid).Take(48).ToArray();
                var serializer = new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 8 };
                var context = new { schema = 1, reason, phase, utc = DateTime.UtcNow.ToString("o"), observedCount = facts.Count, omittedCount = Math.Max(0, facts.Count - 48),
                    processes = bounded.Select(item => new { pid = item.Pid, sessionMatch = item.SessionMatch, pathAvailable = item.Image.Length != 0, pathMatch = item.PathMatch,
                        startAvailable = item.Creation > 0, startedUtc = item.Creation > 0 ? DateTime.FromFileTimeUtc(item.Creation).ToString("o") : null }).ToArray() };
                error.Data["BapSteamDiagnostic"] = serializer.Serialize(context);
                error.Data["BapSteamObservation"] = serializer.Serialize(new { schema = 1, processes = bounded.Select(item => new { pid = item.Pid, kind = Kind(item.Name), querySource = item.Available ? "Native" : "Unavailable", nativeError = item.NativeError, exitAvailable = item.Available, confirmedExit = item.Exited }).ToArray() });
            } catch { }
            return error;
        }
        private static string DiagnosticBool(bool? value) { return value.HasValue ? value.Value ? "true" : "false" : "unknown"; }
        private static void AddTypedFailure(Exception error, string reason, string phase, SteamProcessStatus? status)
        {
            error.Data["BapSteamLogStatus"] = "invalid-context";
            error.Data["BapSteamLog"] = "SteamObservationCoverage: status=invalid-context\r\n";
            try {
                if (phase == "GameObservation") phase = "ShutdownObservation";
                else if (phase == "GameIdentity") phase = "ProcessIdentity";
                else if (phase == "GameTermination") phase = "ProcessTermination";
                if (!new[] { "ProcessObservationUnavailable", "SteamOwnershipUnknown", "SteamOtherSession", "SteamRunning", "ProcessListChanged", "ProcessIdentityChanged", "ProcessCaptureFailed", "ProcessTerminationFailed", "ProcessTerminateOpenFailed", "ProcessTerminateFailed", "ProcessExitWaitFailed", "ProcessExitTimeout", "ConsentCancelled", "ConsentUnavailable",
                    "HelperRunning", "GameProcessesRemain", "InvalidSteamAppDirectory", "SteamExecutableMissing", "Unknown", "OtherSession", "SteamGameProcessesRemain" }.Contains(reason, StringComparer.Ordinal)
                    || !new[] { "ShutdownObservation", "ShutdownConsent", "ShutdownRecheck", "ProcessIdentity", "ProcessTermination", "ShutdownCompletion", "InstallPreflight", "UninstallPreflight", "ShortcutPreflight", "ShortcutCommit", "ShortcutRollback", "Resume",
                        "HelperWait", "GameCompletion", "GamePreflight", "ShortcutRollbackCommit" }.Contains(phase, StringComparer.Ordinal)) return;
                var facts = status?.Facts ?? new List<SteamProcessObservation>();
                var bounded = facts.OrderBy(x => x.Pid).Take(48).ToArray();
                var rows = new StringBuilder(); var included = 0; var limited = false;
                const string coverage = "SteamObservationCoverage: status=included\r\n";
                foreach (var item in bounded) {
                    if (item.Pid == 0 || item.Pid > int.MaxValue || item.NativeError < 0 || item.Creation < 0 || item.Exited && !item.Available) return;
                    var started = item.Creation > 0 ? DateTime.FromFileTimeUtc(item.Creation).ToString("o", CultureInfo.InvariantCulture) : "unknown";
                    var index = (included + 1).ToString(CultureInfo.InvariantCulture);
                    var pid = item.Pid.ToString(CultureInfo.InvariantCulture);
                    var pair = "SteamProcess: index=" + index + "; pid=" + pid + "; sessionMatch=" + DiagnosticBool(item.SessionMatch) + "; pathAvailable=" + DiagnosticBool(item.Image.Length != 0)
                        + "; pathMatch=" + DiagnosticBool(item.PathMatch) + "; startAvailable=" + DiagnosticBool(item.Creation > 0) + "; startedUtc=" + started + "\r\n"
                        + "SteamObservationProcess: index=" + index + "; pid=" + pid + "; kind=" + Kind(item.Name) + "; querySource=" + (item.Available ? "Native" : "Unavailable")
                        + "; nativeError=" + item.NativeError.ToString(CultureInfo.InvariantCulture) + "; exitAvailable=" + DiagnosticBool(item.Available) + "; confirmedExit=" + DiagnosticBool(item.Exited) + "\r\n";
                    // Reserve the bounded header before adding an indivisible identity/observation pair.
                    if (limited || rows.Length + pair.Length + 512 + coverage.Length > 16384) { limited = true; continue; }
                    rows.Append(pair); included++;
                }
                var text = "SteamDiagnostic: schema=1; reason=" + reason + "; phase=" + phase + "; utc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                    + "; observedCount=" + facts.Count.ToString(CultureInfo.InvariantCulture) + "; omittedCount=" + (facts.Count - included).ToString(CultureInfo.InvariantCulture) + "\r\n" + rows + coverage;
                if (text.Length > 16384) { error.Data["BapSteamLogStatus"] = "context-limit"; return; }
                error.Data["BapSteamLog"] = text;
                error.Data["BapSteamLogStatus"] = limited || facts.Count > bounded.Length ? "context-limit" : "included";
            } catch { }
        }
        private static Exception Failure(string reason, string phase, SteamProcessObservation observation)
        {
            var status = new SteamProcessStatus(); status.Facts.Add(observation);
            return Failure(reason, phase, status);
        }
        private static string Kind(string name)
        {
            if (string.Equals(name, "steam", StringComparison.OrdinalIgnoreCase)) return "SteamClient";
            if (string.Equals(name, "steamwebhelper", StringComparison.OrdinalIgnoreCase)) return "SteamWebHelper";
            if (string.Equals(name, "GameOverlayUI", StringComparison.OrdinalIgnoreCase)) return "SteamOverlay";
            if (name.StartsWith("steamerrorreporter", StringComparison.OrdinalIgnoreCase)) return "SteamErrorReporter";
            if (name.StartsWith("AstralParty_", StringComparison.OrdinalIgnoreCase)) return "Game";
            if (string.Equals(name, "BetterAstralParty-Launcher", StringComparison.OrdinalIgnoreCase)) return "Launcher";
            if (string.Equals(name, "BetterAstralParty-UpdateHelper", StringComparison.OrdinalIgnoreCase)) return "UpdateHelper";
            return "Other";
        }
        internal static void AssertStopped(string root, string phase) { var status = Status(root); if (status.State != "Stopped") throw Failure(status.State == "Unknown" ? "SteamOwnershipUnknown" : status.State == "OtherSession" ? "SteamOtherSession" : "SteamRunning", phase, status); }
        private static string Choice(Func<string, string?> consent, string prompt)
        {
            try { return consent(prompt) ?? ""; } catch { throw Failure("ConsentUnavailable", "ShutdownConsent"); }
        }
        internal static bool ConfirmForce(IEnumerable<SteamProcessObservation> targets, Func<string, string?> consent, Action<string> output)
        {
            output("목록의 게임/앱을 강제 종료합니다. 저장하지 않은 진행 상황과 동기화 중인 데이터가 손실될 수 있습니다. 게임은 다시 실행하지 않습니다. / Force-close the listed processes. Unsaved progress and syncing data may be lost. Games will not restart.");
            foreach (var item in targets) output("  " + item.Name + " (PID " + item.Pid + ")");
            var answer = Choice(consent, "r: 직접 종료 후 재확인 / recheck, y: 목록 강제 종료 / force-close listed processes, Enter: 취소 / cancel");
            if (string.Equals(answer, "r", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)) throw Failure("ConsentCancelled", "ShutdownConsent");
            return true;
        }
        internal static void EnsureApprovedList(IEnumerable<SteamProcessObservation> approved, IEnumerable<SteamProcessObservation> fresh)
        {
            var status = new SteamProcessStatus(); status.Facts.AddRange(fresh);
            foreach (var item in status.Facts) if (!approved.Any(x => x.Pid == item.Pid && x.Creation == item.Creation && x.Session == item.Session && string.Equals(x.Image, item.Image, StringComparison.OrdinalIgnoreCase))) throw Failure("ProcessListChanged", "ShutdownRecheck", status);
        }
        private static List<SteamProcessObservation> GameTargets(string root, bool helper)
        {
            var directory = PathOf(root); var list = new List<SteamProcessObservation>();
            foreach (var item in Observe()) {
                if (item.Exited || !(helper ? string.Equals(item.Name, "BetterAstralParty-UpdateHelper", StringComparison.OrdinalIgnoreCase) : Named(item.Name, GameNames))) continue;
                if (!item.Available || item.Image.Length == 0 || item.Creation <= 0) throw Failure("ProcessObservationUnavailable", "GameObservation", item);
                item.PathMatch = string.Equals(WindowsPath.Parent(item.Image), directory, StringComparison.OrdinalIgnoreCase); item.SessionMatch = item.Session == Session;
                if (item.SessionMatch == true && item.PathMatch == true) list.Add(item);
            }
            return list;
        }
        private static void WaitHelper(string gameRoot, Func<string, string?> consent, Action<string> output)
        {
            while (true) {
                var targets = GameTargets(gameRoot, true); if (targets.Count == 0) return;
                var status = new SteamProcessStatus(); status.Facts.AddRange(targets);
                Waiting(Failure("HelperRunning", "HelperWait", status));
                output("업데이트 helper가 실행 중입니다. 정상 종료를 기다리세요. / Update helper is running; wait for it to finish.");
                if (Choice(consent, "r: 종료 후 재확인 / recheck, Enter: 취소 / cancel") != "r") throw Failure("HelperRunning", "HelperWait", status);
            }
        }
        private static void Waiting(Exception error) { try { WaitingDiagnostic?.Invoke(error); } catch { } }
        internal static void StopGame(string gameRoot, Func<string, string?> consent, Action<string> output)
        {
            WaitHelper(gameRoot, consent, output); List<SteamProcessObservation> approved;
            while (true) { approved = GameTargets(gameRoot, false); if (approved.Count == 0) return; if (ConfirmForce(approved, consent, output)) break; WaitHelper(gameRoot, consent, output); }
            WaitHelper(gameRoot, consent, output); var fresh = GameTargets(gameRoot, false); EnsureApprovedList(approved, fresh);
            foreach (var item in fresh.OrderBy(x => string.Equals(x.Name, "BetterAstralParty-Launcher", StringComparison.OrdinalIgnoreCase) ? 0 : 1)) StopVerified(item);
            if (GameTargets(gameRoot, false).Count != 0) throw Failure("GameProcessesRemain", "GameCompletion");
        }
        internal static void AssertGameStopped(string gameRoot)
        {
            if (GameTargets(gameRoot, true).Count != 0) throw Failure("HelperRunning", "GamePreflight");
            if (GameTargets(gameRoot, false).Count != 0) throw Failure("GameProcessesRemain", "GamePreflight");
        }
        private static List<SteamProcessObservation> SteamTargets(string steamRoot)
        {
            var root = PathOf(steamRoot); var library = Join(root, "steamapps/libraryfolders.vdf");
            var roots = LibraryRoots(root, File.Exists(Io(library)) ? Utf8.GetString(Read(library, 1048576)) : null); var games = new List<string>();
            foreach (var value in roots) {
                var apps = Join(value, "steamapps"); if (!Directory.Exists(Io(apps))) continue;
                foreach (var manifest in Directory.EnumerateFiles(Io(apps), "appmanifest_*.acf", SearchOption.TopDirectoryOnly)) {
                    var directory = InstallDirectoryName(Utf8.GetString(Read(WindowsPath.FromIo(manifest), 1048576)));
                    if (directory == null) throw Failure("InvalidSteamAppDirectory", "ShutdownObservation"); games.Add(Join(value, "steamapps/common/" + directory));
                }
            }
            return SelectSteamTargets(Observe(), root, games, Session);
        }
        internal static List<SteamProcessObservation> SelectSteamTargets(IEnumerable<SteamProcessObservation> observations, string root, IEnumerable<string> games, uint session)
        {
            var result = new List<SteamProcessObservation>();
            foreach (var item in observations) {
                if (item.Exited || item.Available && item.Session != session) continue;
                if (item.Image.Length == 0) { if (item.Name.Length == 0 || Named(item.Name, ClientNames)) throw Failure("ProcessObservationUnavailable", "ShutdownObservation", item); continue; }
                var client = Named(item.Name, ClientNames) && Under(item.Image, root); var game = games.Any(value => Under(item.Image, value));
                if (!client && !game) continue;
                item.PathMatch = true; item.SessionMatch = item.Session == session;
                if (!item.Available || item.Creation <= 0) throw Failure("ProcessObservationUnavailable", "ShutdownObservation", item);
                if (string.Equals(item.Name, "BetterAstralParty-UpdateHelper", StringComparison.OrdinalIgnoreCase)) throw Failure("HelperRunning", "ShutdownObservation", item);
                item.Client = client; result.Add(item);
            }
            return result;
        }
        internal static void StopSteam(string steamRoot, SteamSessionState state, Func<string, string?> consent, Action<string> output)
        {
            var root = PathOf(steamRoot); List<SteamProcessObservation> approved; var shutdownAttempted = false;
            while (true) {
                var status = Status(root);
                if (status.State == "Stopped") {
                    if (shutdownAttempted && SteamTargets(root).Count != 0) throw Failure("SteamGameProcessesRemain", "ShutdownCompletion");
                    return;
                }
                if (status.State == "Unknown" || status.State == "OtherSession") {
                    Waiting(Failure(status.State == "Unknown" ? "SteamOwnershipUnknown" : "SteamOtherSession", "ShutdownObservation", status));
                    output(status.State == "Unknown" ? "Steam process ownership unavailable; close Steam normally and recheck. / Steam 경로·세션을 확인할 수 없습니다." : "Selected Steam is running in another Windows session; close it normally and recheck. / 다른 Windows 세션에서 Steam이 실행 중입니다.");
                    if (!string.Equals(Choice(consent, "r: 정상 종료 후 재확인 / recheck after normal exit, Enter: 취소 / cancel"), "r", StringComparison.OrdinalIgnoreCase)) throw Failure(status.State == "Unknown" ? "SteamOwnershipUnknown" : "SteamOtherSession", "ShutdownConsent", status);
                    continue;
                }
                if (!File.Exists(Io(Join(root, "steam.exe")))) throw Failure("SteamExecutableMissing", "ShutdownObservation", status);
                try { approved = SteamTargets(root); }
                catch (InvalidOperationException error) when (error.Message.Contains("HelperRunning")) { Waiting(error); output("Update helper is running; wait for it to finish. / 업데이트 helper 정상 종료를 기다리세요."); if (Choice(consent, "r: recheck / 재확인, Enter: cancel / 취소") != "r") throw; continue; }
                if (!ConfirmForce(approved, consent, output)) continue;
                var rechecked = Status(root); if (rechecked.State == "Unknown" || rechecked.State == "OtherSession") throw Failure(rechecked.State, "ShutdownRecheck", rechecked);
                var fresh = SteamTargets(root); EnsureApprovedList(approved, fresh);
                rechecked = Status(root); if (rechecked.State == "Unknown" || rechecked.State == "OtherSession") throw Failure(rechecked.State, "ShutdownRecheck", rechecked);
                state.Root = root; shutdownAttempted = true;
                try { foreach (var item in SteamStopOrder(fresh)) { StopVerified(item); if (item.Client && string.Equals(item.Name, "steam", StringComparison.OrdinalIgnoreCase)) state.Restart = true; } }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception native && native.NativeErrorCode == 5
                    || error is ApplySafetyException safety && safety.Failure == ApplyFailure.ProcessUncertain && safety.NativeError == 5) {
                    Waiting(error);
                    output("Steam did not close; close it normally and recheck. / Steam을 정상 종료한 뒤 재확인하세요.");
                    if (!string.Equals(Choice(consent, "r: 정상 종료 후 재확인 / recheck after normal exit, Enter: 취소 / cancel"), "r", StringComparison.OrdinalIgnoreCase)) throw;
                    continue;
                }
                for (var attempt = 0; attempt < 20 && Status(root).State != "Stopped"; attempt++) Thread.Sleep(100);
                AssertStopped(root, "ShutdownCompletion"); if (SteamTargets(root).Count != 0) throw Failure("SteamGameProcessesRemain", "ShutdownCompletion");
                return;
            }
        }
        internal static void ResumeSteam(SteamSessionState state, Action<string> output)
        {
            if (!state.Restart || state.Root == null) return;
            try { if (Status(state.Root).State == "Stopped") Process.Start(new ProcessStartInfo { FileName = WindowsPath.ForProcess(Join(state.Root, "steam.exe")), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }); }
            catch (Exception error) { try { output("Steam restart failed; start Steam manually. / Steam을 직접 실행하세요. reason=" + error.GetType().Name); } catch { } }
        }
        internal static IEnumerable<SteamProcessObservation> SteamStopOrder(IEnumerable<SteamProcessObservation> targets)
        {
            if (targets.Any(x => string.Equals(x.Name, "BetterAstralParty-UpdateHelper", StringComparison.OrdinalIgnoreCase))) throw Failure("HelperRunning", "ProcessTermination");
            return targets.OrderBy(x => x.Client && string.Equals(x.Name, "steam", StringComparison.OrdinalIgnoreCase) ? 0 : x.Client ? 1 : 2);
        }
#if BAP_SAFETY_FIXTURE
        internal static Func<uint, SafeWaitHandle>? TerminationOpen = null;
        internal static Func<SafeWaitHandle, long>? TerminationCreation = null;
        internal static Func<SafeWaitHandle, bool>? TerminationStop = null;
        internal static Func<SafeWaitHandle, uint, uint>? TerminationWait = null;
        internal static int TerminationNativeError = 0;
        private static SafeWaitHandle OpenProcess(uint access, bool inherit, uint pid) { return TerminationOpen == null ? throw new InvalidOperationException("Inert termination facade required.") : TerminationOpen(pid); }
        private static bool GetProcessTimes(SafeWaitHandle process, out long creation, out long exit, out long kernel, out long user) { creation = TerminationCreation == null ? throw new InvalidOperationException("Inert process times required.") : TerminationCreation(process); exit = kernel = user = 0; return true; }
        private static bool TerminateProcess(SafeWaitHandle process, uint code) { return TerminationStop == null ? throw new InvalidOperationException("Inert termination required.") : TerminationStop(process); }
        private static uint WaitForSingleObject(SafeWaitHandle process, uint milliseconds) { return TerminationWait == null ? throw new InvalidOperationException("Inert wait required.") : TerminationWait(process, milliseconds); }
        private static int TerminationError() { return TerminationNativeError; }
#else
        [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeWaitHandle process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeWaitHandle process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle process, uint milliseconds);
        private static int TerminationError() { return Marshal.GetLastWin32Error(); }
#endif
        private static void StopVerified(SteamProcessObservation target)
        {
            var status = new SteamProcessStatus(); status.Facts.Add(target); var failureReason = "ProcessCaptureFailed";
            try {
                if (string.Equals(target.Name, "BetterAstralParty-UpdateHelper", StringComparison.OrdinalIgnoreCase)) throw Failure("HelperRunning", "ProcessTermination", status);
                WindowsProcessGuard.Captured captured;
                try { captured = WindowsProcessGuard.Observe(target.Pid, true, 2000); }
                catch (ApplySafetyException error) when (target.Pid != 0 && error.Failure == ApplyFailure.ProcessUncertain && error.NativeError == 87) {
                    // Steam can exit its helpers before capture; a still-listed or reused PID must never be skipped.
                    if (Observe().Any(item => item.Pid == target.Pid)) throw;
                    return;
                }
                using (captured) {
                    if (captured.Exited) { target.Exited = true; return; }
                    target.PathMatch = string.Equals(captured.Image, target.Image, StringComparison.OrdinalIgnoreCase); target.SessionMatch = captured.Session == target.Session;
                    if (target.SessionMatch != true || captured.Creation != target.Creation || target.PathMatch != true) throw Failure("ProcessIdentityChanged", "ProcessIdentity", status);
                    try {
                        failureReason = "ProcessTerminateOpenFailed";
                        using (var terminate = OpenProcess(1 | 0x1000 | 0x100000, false, target.Pid)) {
                            if (terminate.IsInvalid) throw new System.ComponentModel.Win32Exception(TerminationError());
                            long creation, exit, kernel, user;
                            if (!GetProcessTimes(terminate, out creation, out exit, out kernel, out user) || creation != target.Creation) throw Failure("ProcessIdentityChanged", "ProcessIdentity", status);
                            failureReason = "ProcessTerminateFailed";
                            if (!TerminateProcess(terminate, 1)) throw new System.ComponentModel.Win32Exception(TerminationError());
                            failureReason = "ProcessExitWaitFailed";
                            var wait = WaitForSingleObject(terminate, 5000);
                            if (wait == uint.MaxValue) throw new System.ComponentModel.Win32Exception(TerminationError());
                            if (wait == 258) {
                                if (captured.Exited) { target.Exited = true; return; }
                                target.NativeError = 0; throw Failure("ProcessExitTimeout", "ProcessTermination", status);
                            }
                            if (wait != 0) { target.NativeError = 0; throw Failure("ProcessExitWaitFailed", "ProcessTermination", status); }
                            target.Exited = true;
                        }
                    } catch (System.ComponentModel.Win32Exception error) {
                        // Steam shuts helpers down asynchronously; confirm exit on the same held process, never by PID alone.
                        bool exited;
                        try { exited = error.NativeErrorCode == 5 && (failureReason == "ProcessTerminateOpenFailed" || failureReason == "ProcessTerminateFailed") ? captured.WaitForExit(2000) : captured.Exited; }
                        catch { failureReason = "ProcessExitWaitFailed"; throw; }
                        if (exited) { target.Exited = true; return; }
                        throw;
                    }
                }
            } catch (Exception error) {
                if (error is System.ComponentModel.Win32Exception native) target.NativeError = native.NativeErrorCode;
                else if (error is ApplySafetyException safety) target.NativeError = safety.NativeError;
                if (!error.Data.Contains("BapSteamDiagnostic")) { var diagnostic = Failure(failureReason, "ProcessTermination", status); foreach (DictionaryEntry field in diagnostic.Data) error.Data[field.Key] = field.Value; }
                throw;
            }
        }
    }
}
