using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

// Production parser, Windows fences, operation lease and exact launcher bridge; no game execution.
internal static class RuntimeFixture
{
    private static string _root = "";
    private static int _cases, _assertions;
    private const string Ini = "doorstop_config.ini";
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string file, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileStringW(string section, string key, string fallback, StringBuilder result, uint capacity, string file);

    private static void Assert(bool condition, string name)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
    }
    private static void Same(byte[] expected, byte[] actual, string name) => Assert(expected.AsSpan().SequenceEqual(actual), name);
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException or ApplySafetyException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { Assert(true, name); return; }
        throw new InvalidOperationException("FAIL: accepted " + name);
    }
    private static byte[] Encode(string text, Encoding encoding, bool bom)
    {
        var body = encoding.GetBytes(text);
        return bom ? encoding.GetPreamble().Concat(body).ToArray() : body;
    }
    private static string NewRoot(byte[] bytes)
    {
        var root = Path.Combine(_root, "c" + (++_cases));
        if (Directory.Exists(root)) throw new IOException("Fixture must be new");
        Directory.CreateDirectory(Path.Combine(root, "BepInEx", "config"));
        Directory.CreateDirectory(Path.Combine(root, "BepInEx", "plugins", "OtherMod"));
        File.WriteAllBytes(Path.Combine(root, Ini), bytes);
        File.WriteAllBytes(Path.Combine(root, "BepInEx", "config", "OtherMod.cfg"), new byte[] { 239, 187, 191, 35, 32, 70, 73, 88, 84, 85, 82, 69, 13, 10 });
        File.WriteAllBytes(Path.Combine(root, "BepInEx", "plugins", "OtherMod", "OtherMod.dll"), new byte[] { 70, 73, 88, 84, 85, 82, 69 });
        return root;
    }
    private static Dictionary<string, byte[]> OtherFiles(string root) => new[]
    {
        "BepInEx/config/OtherMod.cfg", "BepInEx/plugins/OtherMod/OtherMod.dll"
    }.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(root, path)));
    private static void CheckOther(string root, Dictionary<string, byte[]> files)
    {
        foreach (var item in files) Same(item.Value, File.ReadAllBytes(Path.Combine(root, item.Key)), "other mod preserved " + item.Key);
    }
    private static string[] Backups(string root) => Directory.GetFiles(Path.Combine(root, "BepInEx", "config"), "BetterAstralParty.loader-before-*.bak");
    private static string WindowsEnabled(string root)
    {
        var text = new StringBuilder(64);
        GetPrivateProfileStringW("General", "enabled", "missing", text, 64, Path.Combine(root, Ini));
        return text.ToString().ToLowerInvariant();
    }
    private static void Enable(string root)
    {
        using var operation = new UpdateLaunchGate.Lease(root);
        SharedLoaderConfiguration.Enable(root, operation);
    }

    private static void ParserAndWrites()
    {
        var formats = new (Encoding Encoding, bool Bom)[]
        {
            (new UTF8Encoding(false, true), false), (new UTF8Encoding(true, true), true),
            (new UnicodeEncoding(false, true, true), true)
        };
        foreach (var format in formats)
        foreach (var newline in new[] { "\r\n", "\n" })
        {
            var text = string.Join(newline, "# 한국어 and custom bytes", "[Other]", "enabled=invalid-is-unrelated", "[gEnErAl]", "custom = 日本語 / untouched", "\tenabled \t= \tFaLsE\t", "# keep", "[Tail]", "enabled=true", "");
            var original = Encode(text, format.Encoding, format.Bom);
            var parsed = SharedLoaderConfiguration.Inspect(original);
            Assert(!parsed.Enabled, "false parsed " + format.Encoding.WebName + newline.Length);
            var expected = (byte[])original.Clone(); parsed.Replacement.CopyTo(expected, parsed.Offset);
            Same(Encode(text.Replace("FaLsE", "true "), format.Encoding, format.Bom), expected, "byte offset and equal-size replacement");
            Assert(expected.Length == original.Length, "same length");
            var root = NewRoot(original); var other = OtherFiles(root);
            Assert(WindowsEnabled(root) == "false", "actual Windows API recognizes OFF");
            using (var operation = new UpdateLaunchGate.Lease(root))
            {
                SharedLoaderConfiguration.Enable(root, operation);
                Same(expected, File.ReadAllBytes(Path.Combine(root, Ini)), "actual fenced write exact");
                var backups = Backups(root);
                Assert(backups.Length == 1 && Regex.IsMatch(Path.GetFileName(backups[0]), @"\ABetterAstralParty\.loader-before-[0-9a-f]{32}\.bak\z"), "unique original backup name");
                Same(original, File.ReadAllBytes(backups[0]), "backup is original exact");
                Assert(WindowsEnabled(root) == "true", "actual Windows API recognizes ON after handle release");
                SharedLoaderConfiguration.Enable(root, operation);
                Same(expected, File.ReadAllBytes(Path.Combine(root, Ini)), "second enable no-op");
                Assert(Backups(root).Length == 1, "second enable no new backup");
                Same(original, File.ReadAllBytes(backups[0]), "second enable keeps backup");
            }
            CheckOther(root, other);

            var enabled = Encode(text.Replace("FaLsE", "TrUe"), format.Encoding, format.Bom);
            var trueRoot = NewRoot(enabled);
            Assert(SharedLoaderConfiguration.Inspect(enabled).Enabled, "true parsed");
            Enable(trueRoot);
            Same(enabled, File.ReadAllBytes(Path.Combine(trueRoot, Ini)), "already true byte-exact no-op");
            Assert(Backups(trueRoot).Length == 0, "already true no backup");
        }
        var bad = new[]
        {
            "", "[Other]\nenabled=false\n", "[General]\ncustom=1\n", "[General\nenabled=false\n",
            "[General]\nenabled=false\n[General]\ncustom=1\n", "[General]\nenabled=false\nenabled=true\n",
            "[General]\nenabled=1\n", "[General]\nenabled=false # invalid\n", "[General]\nenabled=\n",
            "[General]\renabled=false\r", "[General]\nenabled=false\n\0"
        };
        foreach (var text in bad) Reject(() => SharedLoaderConfiguration.Inspect(Encoding.UTF8.GetBytes(text)), "invalid parser input");
        Reject(() => SharedLoaderConfiguration.Inspect(new byte[] { 0xC3, 0x28 }), "invalid UTF8");
        Reject(() => SharedLoaderConfiguration.Inspect(new byte[] { 0xFF, 0xFE, 0x5B }), "invalid UTF16LE");
        var utf16be = Encode("[General]\nenabled=false\n", new UnicodeEncoding(true, true, true), true);
        Reject(() => SharedLoaderConfiguration.Inspect(utf16be), "UTF16BE rejected as Windows-incompatible");
        var beRoot = NewRoot(utf16be);
        Reject(() => Enable(beRoot), "UTF16BE writer rejected");
        Same(utf16be, File.ReadAllBytes(Path.Combine(beRoot, Ini)), "UTF16BE unchanged");
        Assert(Backups(beRoot).Length == 0, "UTF16BE no backup");
        Reject(() => SharedLoaderConfiguration.Inspect(new byte[65537]), "oversize parser");
        var oversize = NewRoot(new byte[65537]);
        Reject(() => Enable(oversize), "oversize fenced writer");
        Assert(Backups(oversize).Length == 0, "invalid file no backup");
        Same(new byte[65537], File.ReadAllBytes(Path.Combine(oversize, Ini)), "oversize unchanged");
    }

    private static void LaunchLeaseAndBridge()
    {
        var bytes = Encoding.UTF8.GetBytes("# fixture\r\n[General]\r\nenabled=false\r\ncustom=value\r\n");
        var root = NewRoot(bytes);
        // Shared-loader-only roots intentionally have no signed helper installation.
        using (var guarded = SteamLauncher.GuardMaintenance(root))
        {
            Assert(guarded is UpdateLaunchGate.Lease, "actual maintenance guard returns owned typed lease");
            Same(bytes, File.ReadAllBytes(Path.Combine(root, Ini)), "maintenance guard is read-only");
            Assert(Backups(root).Length == 0, "guard before compatibility no backup");
            SteamLauncher.EnableSharedLoader(root, guarded);
            Assert(WindowsEnabled(root) == "true", "actual public bridge enabled shared loader");
            Assert(Backups(root).Length == 1, "bridge original retained");
            Reject(() => { using var rejected = new UpdateLaunchGate.Lease(root); }, "public bridge retains launch operation lease");
        }
        using (var released = new UpdateLaunchGate.Lease(root)) Assert(true, "caller disposal releases operation lease");

        var nonTyped = NewRoot(bytes);
        using (var unrelated = new MemoryStream()) Reject(() => SteamLauncher.EnableSharedLoader(nonTyped, unrelated), "public bridge rejects non-typed lease");
        Same(bytes, File.ReadAllBytes(Path.Combine(nonTyped, Ini)), "unrelated lease no ini writes");
        Assert(Backups(nonTyped).Length == 0, "unrelated lease no backup");

        var own = NewRoot(bytes); var foreign = NewRoot(bytes);
        using (var operation = new UpdateLaunchGate.Lease(own))
        {
            Reject(() => SteamLauncher.EnableSharedLoader(foreign, operation), "wrong root owned lease denied");
            Same(bytes, File.ReadAllBytes(Path.Combine(foreign, Ini)), "wrong root unchanged");
            Assert(Backups(foreign).Length == 0, "wrong root no backup");
            File.WriteAllText(Path.Combine(own, "BetterAstralParty.reinstall-v4.pending"), "isolated pending");
            Reject(() => SteamLauncher.EnableSharedLoader(own, operation), "pending work introduced after acquisition denied");
            Same(bytes, File.ReadAllBytes(Path.Combine(own, Ini)), "pending work unchanged");
            Assert(Backups(own).Length == 0, "pending work no backup");
        }
        var disposedRoot = NewRoot(bytes); var disposed = new UpdateLaunchGate.Lease(disposedRoot); disposed.Dispose();
        Reject(() => SteamLauncher.EnableSharedLoader(disposedRoot, disposed), "disposed owned lease denied");
        Same(bytes, File.ReadAllBytes(Path.Combine(disposedRoot, Ini)), "disposed lease unchanged");
        Assert(Backups(disposedRoot).Length == 0, "disposed lease no backup");
    }

    private static void UnsafeWritesAndBackupPreservation()
    {
        var bytes = Encoding.UTF8.GetBytes("[General]\nenabled=false\ncustom=untouched\n");
        var existing = NewRoot(bytes);
        var retainedPath = Path.GetFullPath(Path.Combine(existing, SharedLoaderConfiguration.BackupPrefix + new string('0', 32) + ".bak"));
        var retained = Encoding.UTF8.GetBytes("existing backup - do not overwrite"); File.WriteAllBytes(retainedPath, retained);
        Enable(existing);
        Same(retained, File.ReadAllBytes(retainedPath), "older backup preserved");
        var first = Backups(existing).Single(path => path != retainedPath);
        Same(bytes, File.ReadAllBytes(first), "first automatic ON original exact");
        var later = Encoding.UTF8.GetBytes("[General]\nenabled=false\ncustom=later-user-value\n");
        File.WriteAllBytes(Path.Combine(existing, Ini), later); Enable(existing);
        Assert(Backups(existing).Length == 3, "subsequent OFF transition gets distinct backup");
        Same(retained, File.ReadAllBytes(retainedPath), "old backup survives second transition");
        Same(bytes, File.ReadAllBytes(first), "first transition backup survives second");
        var second = Backups(existing).Single(path => path != retainedPath && path != first);
        Same(later, File.ReadAllBytes(second), "later exact user state retained");
        Same(Encoding.UTF8.GetBytes("[General]\nenabled=true \ncustom=later-user-value\n"), File.ReadAllBytes(Path.Combine(existing, Ini)), "only enabled bytes changed on later transition");

        var busy = NewRoot(bytes);
        using (var held = new FileStream(Path.Combine(busy, Ini), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Reject(() => Enable(busy), "busy ini denied");
        Same(bytes, File.ReadAllBytes(Path.Combine(busy, Ini)), "busy ini unchanged");
        Assert(Backups(busy).Length == 0, "busy ini no backup");

        var readOnly = NewRoot(bytes); var ini = Path.Combine(readOnly, Ini);
        File.SetAttributes(ini, File.GetAttributes(ini) | FileAttributes.ReadOnly);
        try { Reject(() => Enable(readOnly), "readonly ini denied"); }
        finally { File.SetAttributes(ini, File.GetAttributes(ini) & ~FileAttributes.ReadOnly); }
        Same(bytes, File.ReadAllBytes(ini), "readonly ini unchanged");
        Assert(Backups(readOnly).Length == 0, "readonly ini no backup");

        var linked = NewRoot(bytes); var linkedIni = Path.Combine(linked, Ini);
        Assert(CreateHardLinkW(Path.Combine(linked, "linked-ini"), linkedIni, IntPtr.Zero), "isolated hardlink created");
        Reject(() => Enable(linked), "hardlinked ini denied");
        Same(bytes, File.ReadAllBytes(linkedIni), "linked ini unchanged");
        Same(bytes, File.ReadAllBytes(Path.Combine(linked, "linked-ini")), "linked alias unchanged");
        Assert(Backups(linked).Length == 0, "linked ini no backup");
    }

    private static void WriteFailureRecovery()
    {
        var original=Encoding.UTF8.GetBytes("[General]\nenabled=false\ncustom=retained\n");
        foreach(var mode in new[]{"before-write","verified-rollback","failed-rollback","unverified-rollback","failed-rollback-cleanup"}) {
            var root=NewRoot(original);var other=OtherFiles(root);var writeError=new IOException("inert enable write failure");
            var rollbackError=new UnauthorizedAccessException("inert rollback unavailable");Exception? observed=null;
            SharedLoaderConfiguration.FixtureBoundary=point=>{
                if(point==(mode=="before-write"?"before-enable-write":"after-enable-write"))throw writeError;
                if(mode.StartsWith("failed-rollback",StringComparison.Ordinal)&&point=="before-enable-rollback" || mode=="unverified-rollback"&&point=="after-enable-rollback")throw rollbackError;
                if(mode=="failed-rollback-cleanup"&&point=="after-enable-attempt")throw new IOException("inert failed-stream cleanup error");
            };
            try { Enable(root); } catch(Exception error) { observed=error; } finally { SharedLoaderConfiguration.FixtureBoundary=null; }
            var uncertain=mode!="before-write"&&mode!="verified-rollback";
            if(uncertain) {
                Assert(observed is SharedLoaderRecoveryException,"Uncertain mutation has a distinct non-advisory recovery error: "+mode);
                var error=(SharedLoaderRecoveryException)observed!;var causes=error.ToString();
                Assert(causes.Contains(writeError.Message,StringComparison.Ordinal)&&causes.Contains(rollbackError.Message,StringComparison.Ordinal),"Write and failed-rollback causes both retained: "+mode);
                if(mode=="failed-rollback-cleanup")Assert(causes.Contains("inert failed-stream cleanup error",StringComparison.Ordinal),"Cleanup failure cannot erase the typed uncertain-mutation outcome");
                Assert(Path.GetFullPath(Path.Combine(root,error.BackupPath))==Path.GetFullPath(Backups(root).Single())&&error.Message.Contains("Restore doorstop_config.ini",StringComparison.Ordinal),"Recovery error names exact retained original backup: "+mode);
            } else Assert(ReferenceEquals(observed,writeError),"Prewrite or verified rollback keeps original advisory error: "+mode);
            Same(original,File.ReadAllBytes(Backups(root).Single()),"Failure retains exact original backup: "+mode);
            if(!mode.StartsWith("failed-rollback",StringComparison.Ordinal))Same(original,File.ReadAllBytes(Path.Combine(root,Ini)),"Prewrite or completed restoration leaves original INI exact: "+mode);
            else Assert(WindowsEnabled(root)=="true","Failed rollback does not pretend the changed INI is untouched");
            CheckOther(root,other);
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || args.Length != 2) throw new ArgumentException("Windows isolated fixture root and allowed artifact root required");
            var allowed = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            _root = Path.GetFullPath(args[0]);
            if (!_root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || _root.Length > 175 || Directory.Exists(_root))
                throw new ArgumentException("Fixture root must be new, short, and inside the allowed artifact directory");
            Directory.CreateDirectory(_root);
            ParserAndWrites(); LaunchLeaseAndBridge(); UnsafeWritesAndBackupPreservation(); WriteFailureRecovery();
            Console.WriteLine($"PASS SharedLoaderEnable: {_assertions} assertions, {_cases} isolated roots; production parser/write/fence/lease/launcher bridge and Windows INI API, no game install or operating-key access.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
