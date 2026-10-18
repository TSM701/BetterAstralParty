#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BetterAstralParty.Installation;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

internal static partial class NativeMaintenanceIntegrationFixture
{
    private static int _checks;
    private static NativeMaintenance.InstallLock? _heldBackupLock;
    private static readonly List<string> Output = new List<string>();
    private static readonly string Scratch = FixtureBoundary.Root.TrimEnd('\\');
    private static readonly string Game = Scratch + "\\game", Steam = Scratch + "\\steam";
    private static readonly string Package = WindowsPath.Parent(WindowsPath.Parent(WindowsPath.FromIo(typeof(SetupHost).Assembly.Location)));
    private static string Full(string root, string relative) { var path = WindowsPath.Absolute(root + "\\" + relative.Replace('/', '\\')); Scope(path); return path; }
    private static void Scope(string path) { if (!path.StartsWith(Scratch + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture path outside fresh artifact root."); }
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException("FAIL: " + name); _checks++; Output.Add("PASS: " + name); }
    private static byte[] Read(string path) { Scope(path); return File.ReadAllBytes(WindowsPath.ForIo(path)); }
    private static void Write(string path, byte[] bytes)
    { Scope(path); Directory.CreateDirectory(WindowsPath.ForIo(WindowsPath.Parent(path))); File.WriteAllBytes(WindowsPath.ForIo(path), bytes); }
    private static void Text(string path, string text) { Write(path, new UTF8Encoding(false).GetBytes(text)); }
    private static void Json(string path, object value) { Write(path, MaintenanceJson.Write(value)); }
    private static Dictionary<string, string> Snapshot(string root)
    {
        Scope(root); var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(WindowsPath.ForIo(root))) return map;
        map.Add(root, "directory");
        foreach (var dir in Directory.GetDirectories(WindowsPath.ForIo(root), "*", SearchOption.AllDirectories)) map.Add(WindowsPath.FromIo(dir), "directory");
        foreach (var path in Directory.GetFiles(WindowsPath.ForIo(root), "*", SearchOption.AllDirectories)) {
            var logical = WindowsPath.FromIo(path);
            map.Add(logical, _heldBackupLock != null && _heldBackupLock.File.Path == logical ? _heldBackupLock.File.Hash() : UpdateTrust.Hash(Read(logical)));
        }
        return map;
    }
    private static Dictionary<string, string> State()
    {
        var map = Snapshot(Game);
        foreach (var pair in Snapshot(Steam).Concat(Snapshot(NativeMaintenance.JournalRoot(Game)))) map.Add(pair.Key, pair.Value);
        return map;
    }
    private static void Same(Dictionary<string, string> before, string name)
    { var after = State(); Check(before.Count == after.Count && before.All(row => after.TryGetValue(row.Key, out var hash) && hash == row.Value), name); }
    private static string? Consent(string prompt)
    {
        if (prompt.StartsWith("BetterAstralParty 파일", StringComparison.Ordinal) || prompt.StartsWith("새 설치", StringComparison.Ordinal)
            || prompt.StartsWith("저장된 작업", StringComparison.Ordinal) || prompt.StartsWith("명시한 복구", StringComparison.Ordinal)) return "y";
        throw new InvalidOperationException("Unexpected fixture consent; no process termination is authorized: " + prompt);
    }
    private static void Say(string text) { if (Output.Count < 300) Output.Add(text); }
    private static void Seed()
    {
        Text(Full(Game, "AstralParty_INT.exe"), "inert synthetic game image; never execute");
        foreach (var path in new[] { "GameAssembly.dll", "UnityPlayer.dll", "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle", "AstralParty_INT_Data/StreamingAssets/aa/settings.json" }) Text(Full(Game, path), "synthetic native anchor: " + path);
        Text(Full(Game, "AstralParty_INT_Data/data.unity3d"), "UnityFS\0" + new string('x', 56));
        foreach (var name in new[] { "catalog_3.2.0.hash", "catalog_3.2.0.json" }) Text(Full(Game, "fixture-cache/" + name), "synthetic current cache: " + name);
        Text(Full(Game, "BepInEx/plugins/OtherMod/Other.dll"), "unrelated mod retained");
        Text(Full(Game, "changelog.txt"), "unrelated existing loader release notes retained");
        Text(Full(Game, "README.md"), "unrelated existing installation notes retained");
        var profile = Full(Steam, "userdata/1/config"); Directory.CreateDirectory(WindowsPath.ForIo(profile));
        var shortcuts = NativeSteamMaintenance.EmptyShortcuts();
        var unrelated = new OrderedDictionary(StringComparer.OrdinalIgnoreCase); unrelated.Add("appid", 987654U); unrelated.Add("AppName", "Unrelated fixture shortcut"); unrelated.Add("exe", "inert-other.exe"); unrelated.Add("LaunchOptions", "other");
        ((OrderedDictionary)shortcuts["shortcuts"]!).Add("99", unrelated); Write(Full(profile, "shortcuts.vdf"), NativeSteamMaintenance.WriteShortcuts(shortcuts));
        Check(NativeSteamMaintenance.PlanRegister(Steam, 1, Game), "explicit synthetic Steam profile has no owned shortcut");
    }
    private static void Prepare()
    {
        var catalogs = new[] { "catalog_3.2.0.hash", "catalog_3.2.0.json" }; var rows = new List<object>();
        foreach (var path in new[] { "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle",
            "AstralParty_INT_Data/StreamingAssets/aa/settings.json", "AstralParty_INT_Data/data.unity3d", "BepInEx/core/BepInEx.Core.dll" })
        {
            var source = path == "winhttp.dll" || path == "BepInEx/core/BepInEx.Core.dll" ? Full(Package, ".deps/bepinex/" + path) : Full(Game, path);
            rows.Add(new { root = "game", path, kind = "fixture", sha256 = UpdateTrust.Hash(Read(source)) });
        }
        foreach (var name in catalogs) rows.Add(new { root = "cache", path = name, kind = "catalog", sha256 = UpdateTrust.Hash(Read(Full(Game, "fixture-cache/" + name))) });
        Json(Full(Package, "artifacts/BetterAstralParty.compatibility.json"), new { schema = 1, cacheCatalogs = catalogs, files = rows });
        var payload = InstallReceipt.Owned.Select(path => Read(Full(Package, NativeMaintenanceBundle.OwnedSources[path]))).ToArray();
        var zip = GaPackageContract.PreviewUpdateZip(payload);
        Write(Full(Package, "update-proof/update.zip"), zip);
        Write(Full(Package, "update-proof/update.descriptor"), GaPackageContract.PrepareLocalInstallDescriptor(FixtureBoundary.Version, FixtureBoundary.Channel, zip, payload.Select(bytes => (long)bytes.Length).ToArray(), payload.Select(UpdateTrust.Hash).ToArray()));
        Check(InstalledPluginMetadata.ReadMaintenance(Read(Full(Package, "artifacts/BetterAstralParty.dll"))).Version == FixtureBoundary.Version, "strict actual maintenance metadata accepts synthetic BepInPlugin");
    }
    private static void Rebind()
    {
        var manifest = MaintenanceJson.Read(Read(Full(Package, "package-manifest.json"))); var rows = MaintenanceJson.List(manifest, "files");
        foreach (var raw in rows) { var row = MaintenanceJson.Map(raw); var name = MaintenanceJson.Text(row, "path"); if (name == "tools/approved-install-bundles.json") continue; var bytes = Read(Full(Package, name)); row["sha256"] = UpdateTrust.Hash(bytes); row["bytes"] = bytes.Length; }
        var version = MaintenanceJson.Text(manifest, "version"); var kind = MaintenanceJson.Text(manifest, "kind");
        var identity = new StringBuilder("BetterAstralParty.InstallBundle/v1\nversion=" + version + "\nkind=" + kind + "\nprotocol=1\n");
        foreach (var row in rows.Select(MaintenanceJson.Map).Where(row => MaintenanceJson.Text(row, "path") != "tools/approved-install-bundles.json").OrderBy(row => MaintenanceJson.Text(row, "path"), StringComparer.Ordinal)) identity.Append(MaintenanceJson.Text(row, "path")).Append('=').Append(MaintenanceJson.Text(row, "sha256")).Append('\n');
        var build = UpdateTrust.Hash(Encoding.UTF8.GetBytes(identity.ToString())); manifest["buildIdentity"] = build;
        var catalogPath = Full(Package, "tools/approved-install-bundles.json"); Json(catalogPath, new { schema = 1, bundles = new[] { new { buildIdentity = build, version, kind, protocol = 1 } } });
        var catalog = rows.Select(MaintenanceJson.Map).Single(row => MaintenanceJson.Text(row, "path") == "tools/approved-install-bundles.json"); var catalogBytes = Read(catalogPath); catalog["sha256"] = UpdateTrust.Hash(catalogBytes); catalog["bytes"] = catalogBytes.Length;
        Json(Full(Package, "package-manifest.json"), manifest);
    }
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (Exception error) when (error is ApplySafetyException || error is UpdateValidationException || error is InvalidDataException || error is ArgumentException || error is InvalidOperationException)
        { Check(true, label + "; rejected=" + error.GetType().Name); return; }
        throw new InvalidOperationException("Accepted invalid input: " + label);
    }
    private static string[] DocumentationPaths()
    {
        var manifest = MaintenanceJson.Read(Read(Full(Package, "package-manifest.json")));
        return MaintenanceJson.List(manifest, "files").Select(MaintenanceJson.Map).Select(row => MaintenanceJson.Text(row, "path")).Where(NativeMaintenanceFiles.IsDocumentation).ToArray();
    }
    private static void Documents(bool missing)
    {
        var before = State(); var paths = DocumentationPaths(); Check(paths.Length == 8, "fixture covers root, documentation, vendor and source-archive nonconsumed roles");
        foreach (var name in paths) {
            var path = Full(Package, name);
            if (missing) File.Delete(WindowsPath.ForIo(path)); else Text(path, "changed distribution-only text; never consumed: " + name);
        }
        using (var bundle = new NativeMaintenanceBundle(Package, Game)) {
            Check(bundle.Expected.Count == InstallReceipt.Owned.Length, "missing/edited documentation does not change signed owned inventory");
            var recovery = NativeMaintenance.RecoveryTargets(bundle);
            Check(recovery.Contains(Full(Game, "changelog.txt"), StringComparer.OrdinalIgnoreCase) && !recovery.Contains(Full(Game, "unlisted-notes.txt"), StringComparer.OrdinalIgnoreCase), "only exact manifest-declared loader documentation remains eligible for existing pending recovery");
        }
        Same(before, "documentation preparation changes only the inert incoming package, not game/profile/recovery");
    }
    private static void Authenticate()
    {
        var files = Directory.GetFiles(WindowsPath.ForIo(Package), "*", SearchOption.AllDirectories).Select(WindowsPath.FromIo).Where(path => !path.EndsWith("BetterAstralParty-Setup.exe", StringComparison.Ordinal)).ToDictionary(path => path, Read, StringComparer.Ordinal);
        Action restore = () => { foreach (var file in files) Write(file.Key, file.Value); };
        var before = State();
        try
        {
            using (var bundle = new NativeMaintenanceBundle(Package, Game)) Check(bundle.Version == FixtureBoundary.Version && bundle.Expected.Count == InstallReceipt.Owned.Length && new HashSet<string>(bundle.Expected.Keys, StringComparer.Ordinal).SetEquals(InstallReceipt.Owned), "real complete package authentication and self-binding");
            foreach (var strict in new[] { "artifacts/BetterAstralParty.dll", "artifacts/BetterAstralParty.compatibility.json", "artifacts/BetterAstralParty-Setup.exe", "update-proof/update.descriptor", "update-proof/update.signature", ".deps/bepinex/doorstop_config.ini", ".deps/bepinex/dotnet/runtimeconfig.json", ".deps/bepinex/BepInEx/core/README.txt", "docs/readme.txt.exe", "docs/settings.json", "THIRD-PARTY-NOTICES/settings.json" })
                Check(!NativeMaintenanceFiles.IsDocumentation(strict), "executable, configuration and proof paths remain strict: " + strict);
            foreach (var name in DocumentationPaths()) {
                var path = Full(Package, name);
                using (var locked = File.Open(WindowsPath.ForIo(path), FileMode.Open, FileAccess.Read, FileShare.None))
                using (var bundle = new NativeMaintenanceBundle(Package, Game)) {
                    Check(bundle.Hashes.ContainsKey(name), "unopened document retains its expected identity metadata: " + name);
                    Reject(() => bundle.Bytes(name, 1024), "documentation cannot be consumed through maintenance byte access: " + name);
                }
                restore(); Text(path, "edited distribution documentation"); using (var bundle = new NativeMaintenanceBundle(Package, Game)) Check(bundle.Hashes.ContainsKey(name), "edited document is not an execution prerequisite: " + name);
                File.Delete(WindowsPath.ForIo(path)); using (var bundle = new NativeMaintenanceBundle(Package, Game)) Check(bundle.Hashes.ContainsKey(name), "missing document is not an execution prerequisite: " + name);
                restore(); Same(before, "document authentication preserves game/profile/recovery: " + name);
            }
            foreach (var scenario in new[] { "schema", "malformed-json", "duplicate-key", "duplicate-case-key", "duplicate-row", "escape-row", "build-identity", "metadata", "update-signature", "owned-hash", "loader-config", "loader-config-missing", "compatibility-manifest", "proof-descriptor", "document-manifest-hash" })
            {
                restore(); var path = Full(Package, "package-manifest.json"); var manifest = MaintenanceJson.Read(Read(path)); var rows = MaintenanceJson.List(manifest, "files");
                if (scenario == "schema") manifest["schema"] = 2;
                else if (scenario == "malformed-json") Text(path, "{ malformed fixture manifest");
                else if (scenario == "duplicate-key" || scenario == "duplicate-case-key") Text(path, Encoding.UTF8.GetString(Read(path)).Replace("\"schema\":1", scenario == "duplicate-key" ? "\"schema\":1,\"schema\":1" : "\"schema\":1,\"Schema\":1"));
                else if (scenario == "duplicate-row") rows.Add(rows[0]);
                else if (scenario == "escape-row") MaintenanceJson.Map(rows[0])["path"] = "../outside";
                else if (scenario == "build-identity") manifest["buildIdentity"] = new string('0', 64);
                else if (scenario == "metadata") { Write(Full(Package, "artifacts/BetterAstralParty.dll"), new byte[512]); Rebind(); }
                else if (scenario.EndsWith("signature", StringComparison.Ordinal)) { var signaturePath = Full(Package, "update-proof/update.signature"); var envelope = DetachedSignature.Parse(Read(signaturePath)); envelope.Signature[0] ^= 1; Write(signaturePath, new DetachedSignature(envelope.KeyId, envelope.Signature).Bytes()); Rebind(); }
                else if (scenario == "owned-hash") Text(Full(Package, "tools/BetterAstralParty-Mod.cmd"), "tampered fixture payload");
                else if (scenario == "loader-config") Text(Full(Package, ".deps/bepinex/doorstop_config.ini"), "tampered configuration");
                else if (scenario == "loader-config-missing") File.Delete(WindowsPath.ForIo(Full(Package, ".deps/bepinex/doorstop_config.ini")));
                else if (scenario == "compatibility-manifest") Text(Full(Package, "artifacts/BetterAstralParty.compatibility.json"), "tampered configuration");
                else if (scenario == "proof-descriptor") Text(Full(Package, "update-proof/update.descriptor"), "tampered update descriptor");
                else if (scenario == "document-manifest-hash") rows.Select(MaintenanceJson.Map).Single(row => MaintenanceJson.Text(row, "path") == "README.md")["sha256"] = new string('0', 64);
                if (scenario == "schema" || scenario.EndsWith("row", StringComparison.Ordinal) || scenario == "build-identity" || scenario == "document-manifest-hash") Json(path, manifest);
                Reject(() => { using (var incoming = new NativeMaintenanceBundle(Package, Game)) {
                    if (scenario == "loader-config" || scenario == "loader-config-missing") incoming.Bytes(".deps/bepinex/doorstop_config.ini", 1024 * 1024);
                    else if (scenario == "owned-hash") incoming.Bytes("tools/BetterAstralParty-Mod.cmd", 1024 * 1024);
                    else if (scenario == "compatibility-manifest") incoming.Bytes("artifacts/BetterAstralParty.compatibility.json", 1024 * 1024);
                } }, "authentication rejects consumed invalid input " + scenario);
                Same(before, "authentication rejection preserves selected game, profile and backup tree: " + scenario);
            }
            restore(); Reject(() => { using (var ignored = new NativeMaintenanceBundle(Package, Package)) { } }, "package/game overlap rejected");
            var other = Full(Scratch, FixtureBoundary.Channel == "Stable" ? "incoming/package" : "baseline/package");
            Reject(() => { using (var ignored = new NativeMaintenanceBundle(other, Game)) { } }, "different package cannot substitute for actual executing incoming host");
        }
        finally { restore(); }
    }
    private static void UnusedPackageInputs()
    {
        var before = State();
        foreach (var name in new[] { "Install.cmd", "Uninstall.cmd", "Force-Uninstall.cmd", "update-proof/update.zip", "artifacts/BetterAstralParty.dll", "artifacts/BetterAstralParty.compatibility.json", "tools/BetterAstralParty-Mod.cmd", ".deps/bepinex/doorstop_config.ini", ".deps/bepinex/winhttp.dll", ".deps/bepinex/BepInEx/core/BepInEx.Core.dll" })
        foreach (var mode in new[] { "missing", "changed", "locked" }) {
            var path = Full(Package, name); var original = Read(path);
            try {
                if (mode == "missing") File.Delete(WindowsPath.ForIo(path));
                if (mode == "changed") Text(path, "unused incoming bytes changed");
                using (var locked = mode == "locked" ? File.Open(WindowsPath.ForIo(path), FileMode.Open, FileAccess.Read, FileShare.None) : null)
                using (var bundle = new NativeMaintenanceBundle(Package, Game)) {
                    Check(NativeUninstall.Run(bundle, Steam, 1, "Keep", true, Consent, Say) == 0, "removal never consumes unused package input: " + name + "/" + mode);
                    if (name != "update-proof/update.zip")
                        Reject(() => bundle.Bytes(name, 64 * 1048576), "invalid input is rejected if actually consumed: " + name + "/" + mode);
                }
            } finally { Write(path, original); }
            Same(before, "unused-input removal preview preserves game/profile/recovery: " + name + "/" + mode);
        }
        foreach (var name in new[] { ".deps/bepinex/doorstop_config.ini", ".deps/bepinex/winhttp.dll", ".deps/bepinex/BepInEx/core/BepInEx.Core.dll" }) {
            var path = Full(Package, name); var original = Read(path); var target = Full(Game, "winhttp.dll");
            try {
                File.Delete(WindowsPath.ForIo(path));
                using (var bundle = new NativeMaintenanceBundle(Package, Game)) Reject(() => NativeInstall.Run(bundle, false, Steam, 1, Consent, Say, checkOnly: true), "fresh loader installation validates selected missing source: " + name);
                Text(target, "foreign loader preserved without using bundled files"); var foreign = State();
                using (var bundle = new NativeMaintenanceBundle(Package, Game)) NativeInstall.Run(bundle, false, Steam, 1, Consent, Say, checkOnly: true);
                Same(foreign, "existing foreign loader installation does not require unused source: " + name);
            } finally { Write(path, original); File.Delete(WindowsPath.ForIo(target)); }
        }
        Same(before, "entire operation-scoped package matrix preserves selected roots");
    }
    private static void Install(bool update, bool preview)
    {
        var before = State(); var loader = File.Exists(WindowsPath.ForIo(Full(Game, "doorstop_config.ini"))) ? Read(Full(Game, "doorstop_config.ini")) : null;
        using (var bundle = new NativeMaintenanceBundle(Package, Game))
        {
            NativeInstall.Run(bundle, update, update ? null : Steam, update ? 0U : 1U, Consent, Say, checkOnly: preview);
            if (preview) { Same(before, "actual install CheckOnly creates no files/directories/locks or Steam changes"); return; }
            foreach (var file in bundle.Expected) Check(bundle.TargetHash(file.Key) == file.Value, "actual authenticated native install hash: " + file.Key);
            Check(Encoding.UTF8.GetString(Read(Full(Game, "changelog.txt"))) == "unrelated existing loader release notes retained" && Encoding.UTF8.GetString(Read(Full(Game, "README.md"))) == "unrelated existing installation notes retained", "actual install never copies, changes or deletes unrelated loader documentation");
            var journals = Directory.GetFiles(WindowsPath.ForIo(NativeMaintenance.JournalRoot(Game)), "completed.json", SearchOption.AllDirectories);
            Check(journals.Length > 0 && journals.All(path => MaintenanceJson.List(MaintenanceJson.Read(Read(WindowsPath.FromIo(path))), "Entries").Select(MaintenanceJson.Map).All(row => !new[] { Full(Game, "changelog.txt"), Full(Game, "README.md") }.Contains(MaintenanceJson.Text(row, "Target"), StringComparer.OrdinalIgnoreCase))), "new actual install transactions do not read or back up loader documentation");
            var receipt = InstallReceipt.Parse(bundle.ReadTarget(InstallReceipt.Path, 16384)!);
            Check(receipt.Channels?.ActiveVersion == FixtureBoundary.Version && receipt.Channels.ActiveChannel == FixtureBoundary.Channel, "installed receipt binds actual version and channel");
            Check(NativeSteamMaintenance.PlanRemove(Steam, 1, Game), "selected synthetic profile owns exactly the native shortcut");
            if (loader == null) Check(Encoding.UTF8.GetString(Read(Full(Game, "doorstop_config.ini"))).Contains("enabled=true"), "new approved shared loader defaults ON");
            else Check(loader.SequenceEqual(Read(Full(Game, "doorstop_config.ini"))), "existing shared loader bytes preserved");
            if (update) Check(Read(Full(Steam, "userdata/1/config/shortcuts.vdf")).SequenceEqual(Read(Full(Scratch, "expected-shortcuts.bin"))), "UpdateOnly is Steam-independent");
        }
        Check(Encoding.UTF8.GetString(Read(Full(Game, "BepInEx/plugins/OtherMod/Other.dll"))) == "unrelated mod retained", "native install retains unrelated mod");
    }
    private static void HistoricalWater()
    {
        using (var bundle = new NativeMaintenanceBundle(Package, Game)) using (var game = new WindowsFileFence(Game))
        {
            var proof = InstallProof.VerifyUpdateProof(bundle.Bytes("update-proof/update.descriptor", UpdateTrust.MaxDescriptorBytes), bundle.Bytes("update-proof/update.signature", 1024));
            Text(game.Full(UpdateTransaction.HighWaterPath), "BetterAstralParty.HighWater/v1\nroot=" + game.RootIdentity.Text + "\nversion=" + proof.Version + "\ndescriptor=" + proof.DescriptorSha256 + "\n");
            SetupHost.ValidateInstalledHistory(Game); Check(true, "known historical Stable high-water fixture passes actual history validation");
        }
        Text(Full(Game, "doorstop_config.ini"), "[General]\r\nenabled=false\r\nunrelated=preserved\r\n");
        Text(Full(Game, "BepInEx/config/BepInEx.cfg"), "[Logging]\r\nUnityLogListening=false\r\nCustom=preserved\r\n");
        Text(Full(Game, "BepInEx/config/kr.betterastralparty.mod.cfg"), "user setting preserved on update");
        Write(Full(Scratch, "expected-loader.bin"), Read(Full(Game, "doorstop_config.ini")));
        Write(Full(Scratch, "expected-config.bin"), Read(Full(Game, "BepInEx/config/BepInEx.cfg")));
        Write(Full(Scratch, "expected-water.bin"), Read(Full(Game, UpdateTransaction.HighWaterPath)));
        Write(Full(Scratch, "expected-shortcuts.bin"), Read(Full(Steam, "userdata/1/config/shortcuts.vdf")));
    }
    private static void Pending()
    {
        using (var bundle = new NativeMaintenanceBundle(Package, Game)) using (var heldLock = NativeMaintenance.AcquireInstallLock(bundle))
        {
            _heldBackupLock = heldLock;
            try {
            foreach (var relative in new[] { "BepInEx/config/kr.betterastralparty.mod.cfg", "changelog.txt" }) {
            var path = Full(Game, relative); var bytes = Read(path);
            var txn = NativeMaintenance.StartTransaction(bundle, new[] { path }, 0); Text(path, "inert edit after interruption");
            Check(NativeMaintenance.HasPending(bundle), "actual ordinary journal exists after simulated interruption"); var before = State();
            Check(!NativeMaintenance.RecoverPending(bundle, Consent, Say, true), "pending recovery preview detects actual journal"); Same(before, "pending recovery preview is read-only");
            Check(NativeMaintenance.RecoverPending(bundle, Consent, Say) && !NativeMaintenance.HasPending(bundle), "actual authenticated ordinary pending recovery completes");
            Check(Read(path).SequenceEqual(bytes) && File.Exists(WindowsPath.ForIo(Full(txn.BackupDir, "restored.json"))), "pending recovery restores exact bytes and retains journal evidence");
            }
            } finally { _heldBackupLock = null; }
        }
    }
    private static void ChangedOwned()
    {
        var path = Full(Game, "BetterAstralParty-Mod.cmd"); var original = Read(path);
        try
        {
            Text(path, "changed owned fixture script, never executed"); var before = State();
            using (var bundle = new NativeMaintenanceBundle(Package, Game)) Reject(() => NativeInstall.Run(bundle, true, null, 0, Consent, Say), "actual upgrade rejects changed installed ownership");
            Same(before, "changed owned rejection retains installation bytes");
        }
        finally { Write(path, original); }
    }
    private static void Uninstall(bool preview)
    {
        if (preview) LegacyWorkSeed(Game);
        var before = State(); using (var bundle = new NativeMaintenanceBundle(Package, Game)) Check(NativeUninstall.Run(bundle, Steam, 1, "Keep", preview, Consent, Say) == 0, "actual native uninstall result");
        if (preview) { Same(before, "actual uninstall WhatIf has zero writes"); return; }
        foreach (var name in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path })) Check(!File.Exists(WindowsPath.ForIo(Full(Game, name))), "actual uninstall removes verified owned/provenance target: " + name);
        Check(!NativeSteamMaintenance.PlanRemove(Steam, 1, Game), "actual uninstall removes only matching native shortcut");
        LegacyWorkCleaned(Game, "uninstall");
        var shortcuts = NativeSteamMaintenance.ReadShortcuts(Read(Full(Steam, "userdata/1/config/shortcuts.vdf"))); Check(((OrderedDictionary)shortcuts["shortcuts"]!).Contains("99"), "unrelated Steam shortcut survives native removal");
        Check(Read(Full(Game, UpdateTransaction.HighWaterPath)).SequenceEqual(Read(Full(Scratch, "expected-water.bin"))), "uninstall retains the upgraded high-water bytes");
        Preserved();
    }
    private static void GameUpdatedUninstall(bool preview)
    {
        var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var updated = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var created = new List<string>();
        try {
            foreach (var relative in new[] { "GameAssembly.dll", "AstralParty_INT_Data/StreamingAssets/aa/settings.json", "AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle" }) {
                var path = Full(Game, relative); var bytes = Encoding.ASCII.GetBytes("updated inert native game: " + relative);
                originals.Add(path, Read(path)); updated.Add(path, bytes); Write(path, bytes);
            }
            foreach (var name in new[] { "catalog_3.3.0.hash", "catalog_3.3.0.json" }) {
                var path = Full(Game, "fixture-cache/" + name);
                if (File.Exists(WindowsPath.ForIo(path)) || Directory.Exists(WindowsPath.ForIo(path))) throw new InvalidOperationException("Fresh game-update fixture catalog required.");
                var bytes = Encoding.ASCII.GetBytes("updated inert catalog: " + name); Write(path, bytes); created.Add(path); updated.Add(path, bytes);
            }
            var before = State();
            using (var bundle = new NativeMaintenanceBundle(Package, Game)) {
                var report = GameCompatibility.Check(Game, bundle.Source.Full("artifacts/BetterAstralParty.compatibility.json"), bundle.Source.Full(".deps/bepinex"));
                Check(report.Allowed && report.Warnings.Any(issue => issue.Contains("game/GameAssembly.dll")) && report.Warnings.Any(issue => issue.Contains("downloaded catalog inventory")), "updated game remains installable with explicit native-content advisories");
                NativeInstall.Run(bundle, true, null, 0, Consent, Say, checkOnly: true);
                Same(before, "updated-game installation preview preserves all game/mod/Steam/history targets");
                if (!preview) NativeInstall.Run(bundle, true, null, 0, Consent, Say);
                foreach (var row in updated) Check(Read(row.Key).SequenceEqual(row.Value), "UpdateOnly preserves updated native game/catalog bytes: " + WindowsPath.Leaf(row.Key));
                var water = bundle.ReadTarget(UpdateTransaction.HighWaterPath, 2048);
                Check(NativeUninstall.Run(bundle, Steam, 1, "Keep", preview, Consent, Say) == 0, "ordinary uninstall remains available after a native game update");
                foreach (var row in updated) Check(Read(row.Key).SequenceEqual(row.Value), "ordinary uninstall preserves updated native game/catalog bytes: " + WindowsPath.Leaf(row.Key));
                if (preview) Same(before, "updated-game uninstall WhatIf creates no files/directories/locks or target changes");
                else {
                    foreach (var name in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path })) Check(!File.Exists(WindowsPath.ForIo(Full(Game, name))), "updated-game uninstall removes only verified owned/provenance target: " + name);
                    Check(!NativeSteamMaintenance.PlanRemove(Steam, 1, Game), "updated-game uninstall removes only matching BetterAstralParty shortcut");
                    var shortcuts = NativeSteamMaintenance.ReadShortcuts(Read(Full(Steam, "userdata/1/config/shortcuts.vdf")));
                    Check(((OrderedDictionary)shortcuts["shortcuts"]!).Contains("99"), "updated-game uninstall preserves unrelated Steam shortcut");
                    var retained = bundle.ReadTarget(UpdateTransaction.HighWaterPath, 2048);
                    Check(water == null ? retained == null : retained != null && water.SequenceEqual(retained), "updated-game uninstall preserves historical high-water state");
                    Preserved();
                }
            }
        } finally {
            foreach (var row in originals) if (File.Exists(WindowsPath.ForIo(row.Key)) && Read(row.Key).SequenceEqual(updated[row.Key])) Write(row.Key, row.Value);
            foreach (var path in created) if (File.Exists(WindowsPath.ForIo(path)) && Read(path).SequenceEqual(updated[path])) { Scope(path); File.Delete(WindowsPath.ForIo(path)); }
        }
    }
    private static void Preserved()
    {
        Check(Read(Full(Game, "doorstop_config.ini")).SequenceEqual(Read(Full(Scratch, "expected-loader.bin"))), "shared loader exact bytes survive maintenance");
        Check(Read(Full(Game, "BepInEx/config/BepInEx.cfg")).SequenceEqual(Read(Full(Scratch, "expected-config.bin"))), "shared BepInEx config exact bytes survive maintenance");
        Check(Encoding.UTF8.GetString(Read(Full(Game, "BepInEx/plugins/OtherMod/Other.dll"))) == "unrelated mod retained", "other mod survives maintenance");
        Check(Encoding.UTF8.GetString(Read(Full(Game, "changelog.txt"))) == "unrelated existing loader release notes retained" && Encoding.UTF8.GetString(Read(Full(Game, "README.md"))) == "unrelated existing installation notes retained", "existing third-party documentation survives install, uninstall and reinstall");
    }
    private static void OptionalSteamUninstall()
    {
        foreach (var kind in new[] { "root-null", "root-absent", "profile-absent", "profile-encoding", "profile-locked", "shortcuts-malformed", "shortcuts-locked", "no-shortcut" })
        {
            Install(false, false);
            var shortcutsPath = Full(Steam, "userdata/1/config/shortcuts.vdf");
            if (kind == "no-shortcut") NativeSteamMaintenance.Remove(Steam, 1, Game);
            var originalShortcutBytes = Read(shortcutsPath);
            if (kind == "shortcuts-malformed") Write(shortcutsPath, new byte[] { 3, 0, 8 });
            var shortcutBytes = Read(shortcutsPath); var backups = Snapshot(NativeMaintenance.JournalRoot(Game));
            string? steam = kind == "root-null" ? null : kind == "no-shortcut" || kind.StartsWith("shortcuts-", StringComparison.Ordinal) ? Steam : Full(Scratch, "optional-" + kind);
            if (kind.StartsWith("profile-", StringComparison.Ordinal)) Directory.CreateDirectory(WindowsPath.ForIo(steam!));
            var metadata = steam == null ? null : Full(steam, "config/loginusers.vdf");
            if (kind == "profile-encoding") Write(metadata!, new byte[] { 0xC3, 0x28 });
            if (kind == "profile-locked") Text(metadata!, "inert optional Steam metadata");
            var reasons = new List<string>(); var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var observations = (List<string>)typeof(NativeMaintenanceEntry).GetField("Observations", flags)!.GetValue(null)!;
            observations.Clear(); typeof(NativeMaintenanceEntry).GetField("_preview", flags)!.SetValue(null, false);
            foreach (var field in new Dictionary<string, string> { { "_run", Guid.NewGuid().ToString("N") }, { "_version", FixtureBoundary.Version }, { "_operation", "Uninstall" }, { "_package", Package }, { "_game", Game } })
                typeof(NativeMaintenanceEntry).GetField(field.Key, flags)!.SetValue(null, field.Value);
            using (var metadataLock = kind == "profile-locked" || kind == "shortcuts-locked"
                ? new FileStream(WindowsPath.ForIo(kind == "shortcuts-locked" ? shortcutsPath : metadata!), FileMode.Open, FileAccess.Read, FileShare.None) : null)
            using (var bundle = new NativeMaintenanceBundle(Package, Game))
            {
                foreach (var preview in new[] { true, false })
                {
                    var before = kind == "shortcuts-locked" ? Snapshot(Game) : State();
                    Check(NativeUninstall.Run(bundle, steam, kind == "no-shortcut" || kind.StartsWith("shortcuts-", StringComparison.Ordinal) ? 1U : 0U, "Keep", preview, _ => "", Say, reasons.Add) == 0 && reasons.Count == 0,
                        "actual unavailable-Steam preview/refusal has no diagnostic callback: " + kind + "/" + preview);
                    if (kind == "shortcuts-locked") {
                        var after = Snapshot(Game); var previewBackups = Snapshot(NativeMaintenance.JournalRoot(Game));
                        Check(before.Count == after.Count && before.All(row => after.TryGetValue(row.Key, out var hash) && hash == row.Value)
                            && backups.Count == previewBackups.Count && backups.All(row => previewBackups.TryGetValue(row.Key, out var hash) && hash == row.Value),
                            "locked shortcut preview/refusal creates no owned files, locks or backup changes");
                    } else Same(before, "actual unavailable-Steam preview/refusal creates no files, directories, locks or backups: " + kind + "/" + preview);
                }
                Check(NativeUninstall.Run(bundle, steam, kind == "no-shortcut" || kind.StartsWith("shortcuts-", StringComparison.Ordinal) ? 1U : 0U, "Keep", false, Consent, Say, text => {
                    reasons.Add(text); typeof(NativeMaintenanceEntry).GetMethod("Observe", flags)!.Invoke(null, new object[] { text });
                }) == 0, "actual authenticated ordinary file removal without Steam shortcut cleanup: " + kind);
            }
            foreach (var name in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path }))
                Check(!File.Exists(WindowsPath.ForIo(Full(Game, name))), "optional Steam ordinary removal removes only owned target: " + kind + "/" + name);
            Check(Read(shortcutsPath).SequenceEqual(shortcutBytes), "missing Steam/root/account or absent owned shortcut leaves every profile byte unchanged: " + kind);
            var afterBackups = Snapshot(NativeMaintenance.JournalRoot(Game));
            Check(backups.All(row => afterBackups.TryGetValue(row.Key, out var hash) && hash == row.Value) && afterBackups.Count > backups.Count,
                "optional Steam ordinary removal adds a recoverable backup without deleting any prior evidence: " + kind);
            if (kind != "no-shortcut")
            {
                var reason = kind == "profile-absent" ? "SteamProfileAbsent" : kind.StartsWith("shortcuts-", StringComparison.Ordinal) ? "SteamShortcutPlanUnavailable"
                    : kind == "profile-encoding" || kind == "profile-locked" ? "SteamProfileReadUnavailable" : "SteamRootAbsent";
                Check(reasons.Count == 1 && reasons[0].StartsWith("NativeUninstallShortcut: action=Skipped; reason=" + reason, StringComparison.Ordinal), "actual skip producer emits bounded exact reason without account identifiers: " + kind);
                if (reason == "SteamProfileReadUnavailable" || reason == "SteamShortcutPlanUnavailable") Check(reasons[0].Contains("; exception=") && reasons[0].Contains("; hresult=0x") && !reasons[0].Contains(Scratch), "actual unreadable metadata records type/native HRESULT but no personal path or content");
                NativeMaintenanceEntry.SaveObservations();
                var collected = DiagnosticBundle.Collect(Game, Full(Scratch, "synthetic-local/BetterAstralParty/Logs"), Full(Scratch, "synthetic-temp/BetterAstralParty"), Full(Scratch, "absent-unity"), Full(Scratch, "optional-diagnostics-" + kind), Full(Scratch, "optional-fallback-" + kind), "4.8");
                string text;
                using (var zip = System.IO.Compression.ZipFile.OpenRead(collected.ArchivePath)) text = string.Join("\n", zip.Entries.Select(entry => { using (var reader = new StreamReader(entry.Open())) return reader.ReadToEnd(); }));
                Check(text.Contains("NativeUninstallShortcut: action=Skipped; reason=" + reason) && text.Contains("installer-uninstaller-error") && !text.Contains(Scratch),
                    "actual skip producer through native observation writer reaches redacted diagnostic ZIP: " + kind);
            }
            else Check(reasons.Count == 0, "verified absent owned shortcut is a normal no-op, not unavailable discovery");
            Preserved();
            if (kind == "shortcuts-malformed") Write(shortcutsPath, originalShortcutBytes);
        }
        Install(false, false);
        using (var bundle = new NativeMaintenanceBundle(Package, Game))
        {
            void Stop(Action action, string name)
            {
                var before = State(); Exception? failure = null;
                try { action(); } catch (Exception error) { failure = error; }
                Check(failure != null, "uncertain/explicit Steam metadata remains a blocking error: " + name);
                Same(before, "Steam metadata safety refusal retains all installed files, shortcuts and recovery records: " + name);
            }
            Stop(() => NativeUninstall.Run(bundle, null, 1, "Keep", false, Consent, Say), "explicit-account-without-root");
            Stop(() => NativeUninstall.Run(bundle, Steam, 2, "Keep", false, Consent, Say), "explicit-unknown-account");
        }
        using (var bundle = new NativeMaintenanceBundle(Package, Game)) Check(NativeUninstall.Run(bundle, Steam, 1, "Keep", false, Consent, Say) == 0, "ordinary shortcut removal remains available after optional-Steam checks");
        Preserved();
        ForeignLoaderUninstall();
    }
    private static void ForeignLoaderUninstall()
    {
        var loader = Full(Game, "doorstop_config.ini"); var expected = Read(loader); var flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (var choice in new[] { "Keep", "Recorded", "Enabled", "Disabled" }) foreach (var kind in new[] { "missing", "ambiguous", "locked" })
        {
            Install(false, false);
            if (kind == "missing") { Scope(loader); File.Delete(WindowsPath.ForIo(loader)); }
            else Text(loader, kind == "ambiguous" ? "enabled=true\nenabled=false\nforeign=keep" : "enabled=false\nforeign=keep");
            var original = kind == "missing" ? null : Read(loader); var reasons = new List<string>();
            var beforeJournals = new HashSet<string>(Directory.GetFiles(WindowsPath.ForIo(NativeMaintenance.JournalRoot(Game)), "completed.json", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
            var observations = (List<string>)typeof(NativeMaintenanceEntry).GetField("Observations", flags)!.GetValue(null)!; observations.Clear();
            using (var held = kind == "locked" ? new FileStream(WindowsPath.ForIo(loader), FileMode.Open, FileAccess.Read, FileShare.None) : null)
            using (var bundle = new NativeMaintenanceBundle(Package, Game))
                Check(NativeUninstall.Run(bundle, Steam, 1, choice, false, Consent, Say, text => {
                    reasons.Add(text); typeof(NativeMaintenanceEntry).GetMethod("Observe", flags)!.Invoke(null, new object[] { text });
                }) == 0, "actual ordinary owned removal does not depend on foreign configuration compatibility: " + choice + "/" + kind);
            Check(original == null ? !File.Exists(WindowsPath.ForIo(loader)) : Read(loader).SequenceEqual(original), "actual ordinary remover preserves absent/malformed/locked foreign config: " + choice + "/" + kind);
            foreach (var name in InstallReceipt.Owned.Concat(new[] { InstallReceipt.Path }))
                Check(!File.Exists(WindowsPath.ForIo(Full(Game, name))), "foreign-config independent removal deletes only verified mod target: " + choice + "/" + kind + "/" + name);
            var latest = Directory.GetFiles(WindowsPath.ForIo(NativeMaintenance.JournalRoot(Game)), "completed.json", SearchOption.AllDirectories).Where(path => !beforeJournals.Contains(path))
                .Select(path => MaintenanceJson.Read(Read(WindowsPath.FromIo(path)))).ToArray();
            Check(latest.Length == 1 && latest.All(journal => MaintenanceJson.List(journal, "Entries").Select(MaintenanceJson.Map).All(row => MaintenanceJson.Text(row, "Target") != loader)),
                "foreign configuration is never backed up or read for a skipped change: " + choice + "/" + kind);
            if (choice == "Keep") Check(reasons.Count == 0, "default Keep requires neither foreign reads nor restoration advisories: " + kind);
            else {
                var reason = kind == "missing" ? "ForeignConfigurationAbsent" : kind == "ambiguous" ? "ForeignConfigurationAmbiguous" : "ForeignConfigurationReadUnavailable";
                Check(reasons.SequenceEqual(new[] { "NativeUninstallLoader: action=Skipped; reason=" + reason }), "actual explicit unsupported foreign mutation is skipped with exact bounded reason: " + choice + "/" + kind);
                NativeMaintenanceEntry.SaveObservations();
            }
            Write(loader, expected); Preserved();
        }
        var collected = DiagnosticBundle.Collect(Game, Full(Scratch, "synthetic-local/BetterAstralParty/Logs"), Full(Scratch, "synthetic-temp/BetterAstralParty"), Full(Scratch, "absent-unity"), Full(Scratch, "foreign-config-diagnostics"), Full(Scratch, "foreign-config-fallback"), "4.8");
        string all;
        using (var zip = System.IO.Compression.ZipFile.OpenRead(collected.ArchivePath)) all = string.Join("\n", zip.Entries.Select(entry => { using (var reader = new StreamReader(entry.Open())) return reader.ReadToEnd(); }));
        Check(new[] { "ForeignConfigurationAbsent", "ForeignConfigurationAmbiguous", "ForeignConfigurationReadUnavailable" }.All(reason => all.Contains("NativeUninstallLoader: action=Skipped; reason=" + reason)) && !all.Contains(Scratch),
            "actual unsupported foreign-configuration skip producer reaches the existing redacted diagnostic ZIP");
    }
    private static void Reinstall(bool preview)
    {
        Install(false, preview); if (preview) return; Preserved();
        Check(Directory.GetFiles(WindowsPath.ForIo(Game), "BetterAstralParty.reinstall-v5.completed-*", SearchOption.TopDirectoryOnly).Length == 1
            && Encoding.ASCII.GetString(Read(Full(Game, UpdateTransaction.HighWaterPath))).Contains("\nversion=" + FixtureBoundary.Version + "\n"), "complete removal permits a new installation generation with a fresh high-water record");
        Check(Directory.GetFiles(WindowsPath.ForIo(Game), UpdateTransaction.HighWaterPath + ".uninstalled-*", SearchOption.TopDirectoryOnly).Any(path => Read(WindowsPath.FromIo(path)).SequenceEqual(Read(Full(Scratch, "expected-water.bin")))), "generation retains exact archived historical high-water bytes");
    }
    private static void RemovalWithoutGame()
    {
        Install(false, false);
        var ownedBytes = InstallReceipt.Owned.ToDictionary(path => path, path => Read(Full(Game, path)), StringComparer.Ordinal);
        var retainedReceipt = Read(Full(Game, InstallReceipt.Path));
        Text(Full(Game, "BetterAstralParty-Mod.cmd"), "modified installed launch script; never execute");
        using (var bundle = new NativeMaintenanceBundle(Package, Game)) NativeUninstall.Run(bundle, Steam, 1, "Keep", false, Consent, Say);
        Check(InstallReceipt.Owned.Where(path => path != "BetterAstralParty-Mod.cmd").All(path => !File.Exists(WindowsPath.ForIo(Full(Game, path)))), "modified installed launch script does not block independent verified removal");
        Check(Encoding.UTF8.GetString(Read(Full(Game, "BetterAstralParty-Mod.cmd"))) == "modified installed launch script; never execute"
            && Read(Full(Game, InstallReceipt.Path)).SequenceEqual(retainedReceipt), "modified launch script and its receipt are preserved rather than guessed or deleted");
        foreach (var row in ownedBytes) Write(Full(Game, row.Key), row.Value);
        var image = Full(Game, "AstralParty_INT.exe"); var original = Read(image);
        try {
            File.Delete(WindowsPath.ForIo(image));
            Check(NativeSteamMaintenance.ResolveIntDirectory(Game) == null, "ordinary installation discovery still requires its executable");
            Check(NativeMaintenanceEntry.ResolveOperationGame(Game, null, Package, true) == Game, "explicit removal root uses receipt identity instead of a missing game image");
            Reject(() => NativeMaintenanceEntry.ResolveOperationGame(Game, null, Package, false), "installation does not inherit removal-only discovery");
            var receiptPath = Full(Game, InstallReceipt.Path); var receiptBytes = Read(receiptPath);
            try {
                var receipt = InstallReceipt.Parse(receiptBytes); var identity = receipt.Channels!.Root;
                var json = MaintenanceJson.Read(receiptBytes);
                json["channelState"] = Convert.ToBase64String(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(Convert.FromBase64String(MaintenanceJson.Text(json, "channelState"))).Replace(identity, "00000000:0000000000000000")));
                Json(receiptPath, json);
                Reject(() => NativeMaintenanceEntry.ResolveOperationGame(Game, null, Package, true), "normal removal discovery rejects another-root receipt");
            } finally { Write(receiptPath, receiptBytes); }
            foreach (var missing in new[] { "BetterAstralParty.compatibility.json" }) File.Delete(WindowsPath.ForIo(Full(Game, missing)));
            var before = State();
            using (var bundle = new NativeMaintenanceBundle(Package, Game)) NativeUninstall.Run(bundle, Steam, 1, "Keep", true, Consent, Say);
            Same(before, "missing-game normal removal preview has zero writes");
            var unusedPlugin = Full(Package, "artifacts/BetterAstralParty.dll"); var pluginBytes = Read(unusedPlugin);
            try {
                File.Delete(WindowsPath.ForIo(unusedPlugin));
                using (var bundle = new NativeMaintenanceBundle(Package, Game)) NativeUninstall.Run(bundle, Steam, 1, "Keep", false, Consent, Say);
            } finally { Write(unusedPlugin, pluginBytes); }
            Check(!File.Exists(WindowsPath.ForIo(image)) && InstallReceipt.Owned.All(path => !File.Exists(WindowsPath.ForIo(Full(Game, path)))), "normal removal succeeds without creating or requiring a game executable");
        } finally { Write(image, original); }
        var force = EmergencySeed("missing-game-executable", out var steam); File.Delete(WindowsPath.ForIo(Full(force, "AstralParty_INT.exe"))); Text(Full(force, InstallReceipt.Path), "damaged receipt");
        Check(NativeMaintenanceEntry.ResolveOperationGame(force, null, Package, true, true) == force, "explicit force root uses native data and exact residue when receipt and game executable are unavailable");
        Reject(() => NativeMaintenanceEntry.ResolveOperationGame(force, null, Package, true), "ordinary removal does not adopt damaged ownership");
        var forceBefore = Snapshot(force); EmergencyRun(force, steam); EmergencyPreserved(force, steam, forceBefore);
        Check(!File.Exists(WindowsPath.ForIo(Full(force, "AstralParty_INT.exe"))) && !File.Exists(WindowsPath.ForIo(Full(force, InstallReceipt.Path))), "force removal succeeds without a placeholder game image");
        var unknown = Full(Scratch, "unknown-removal-root"); Directory.CreateDirectory(WindowsPath.ForIo(unknown)); Text(Full(unknown, "BetterAstralParty-unrelated.keep"), "not ownership");
        Reject(() => NativeMaintenanceEntry.ResolveOperationGame(unknown, null, Package, true, true), "a mod-like name alone never identifies a deletion root");
    }
    // Two finished 1.3.x work folders and one without a plan, which cleanup must keep.
    private static void LegacyWorkSeed(string game)
    {
        // The updater creates its work container with a private ACL; seed it the same way.
        if (!Directory.Exists(WindowsPath.ForIo(Full(game, UpdateWorkPath.Root)))) using (var fence = new WindowsFileFence(game)) fence.CreatePrivateDirectory(UpdateWorkPath.Root);
        foreach (var kind in new[] { "helper", "wholeintent" }) {
            var work = Full(game, UpdateWorkPath.Root + "/bap-v4-" + kind + "-" + Guid.NewGuid().ToString("N"));
            Text(Full(work, "plan"), "inert finished 1.3.x plan"); Text(Full(work, "committed"), "inert marker"); Text(Full(work, "old-plugin"), "inert 1.3.x plugin backup");
        }
        Text(Full(game, UpdateWorkPath.Root + "/bap-v4-stage-" + new string('e', 32) + "/descriptor"), "inert stage without a plan");
    }
    private static void LegacyWorkCleaned(string game, string name)
    {
        var left = Directory.GetDirectories(WindowsPath.ForIo(Full(game, UpdateWorkPath.Root)), "bap-v4-*").Select(path => WindowsPath.Leaf(WindowsPath.FromIo(path))).ToArray();
        Check(left.Length == 1 && left[0] == "bap-v4-stage-" + new string('e', 32), name + ": finished 1.3.x work removed, folder without a plan kept");
    }
    // A finished 1.3.x installation in its own roots: five owned files, its helper pair, the v4 high-water and the launcher shortcut.
    private static string LegacySeed(string name, out string steam)
    {
        var game = Full(Scratch, name + "-game"); steam = Full(Scratch, name + "-steam");
        Text(Full(game, "AstralParty_INT.exe"), "inert synthetic game image; never execute");
        Text(Full(game, "BepInEx/plugins/OtherMod/Other.dll"), "unrelated mod retained");
        Text(Full(game, "BepInEx/config/kr.betterastralparty.mod.cfg"), "1.3.x player setting");
        Text(Full(game, "doorstop_config.ini"), "[General]\r\nenabled=true\r\nunrelated=preserved\r\n");
        foreach (var path in LegacyReceipt.Owned) Text(Full(game, path), "inert 1.3.x owned file: " + path);
        string identity; using (var fence = new WindowsFileFence(game)) identity = fence.RootIdentity.Text;
        Text(Full(game, OrdinaryInstallRecovery.LegacyHelper), "inert 1.3.x update helper");
        Text(Full(game, OrdinaryInstallRecovery.LegacyHelperReceipt), "BetterAstralParty.HelperReceipt/v1\nroot=" + identity + "\nsha256=" + UpdateTrust.Hash(Read(Full(game, OrdinaryInstallRecovery.LegacyHelper))) + "\n");
        Text(Full(game, NativeInstall.LegacyHighWater), "inert 1.3.x high-water"); LegacyWorkSeed(game);
        Json(Full(game, InstallReceipt.Path), new Dictionary<string, object?> { ["schema"] = 1, ["baseline"] = LegacyReceipt.Baseline, ["owner"] = InstalledPluginMetadata.Owner, ["loaderBefore"] = true,
            ["files"] = LegacyReceipt.Owned.Select(path => new Dictionary<string, string> { ["path"] = path, ["sha256"] = UpdateTrust.Hash(Read(Full(game, path))) }).ToArray(), ["channelState"] = "inert-protocol4-state" });
        var shortcuts = NativeSteamMaintenance.EmptyShortcuts(); var items = (OrderedDictionary)shortcuts["shortcuts"]!;
        var unrelated = new OrderedDictionary(StringComparer.OrdinalIgnoreCase); unrelated.Add("appid", 987654U); unrelated.Add("AppName", "Unrelated fixture shortcut"); unrelated.Add("exe", "inert-other.exe"); unrelated.Add("LaunchOptions", "other"); items.Add("99", unrelated);
        var legacy = new OrderedDictionary(StringComparer.OrdinalIgnoreCase); legacy.Add("appid", 0x80001234U); legacy.Add("AppName", "Astral Party - Mod"); legacy.Add("exe", "\"" + Full(game, NativeSteamMaintenance.LegacyEntry) + "\""); legacy.Add("LaunchOptions", ""); items.Add("7", legacy);
        Write(Full(steam, "userdata/1/config/shortcuts.vdf"), NativeSteamMaintenance.WriteShortcuts(shortcuts));
        return game;
    }
    private static string? TakeoverConsent(string prompt) => prompt.StartsWith("1.3.x", StringComparison.Ordinal) ? "y" : Consent(prompt);
    // The real file fences, journal and rollback run here; native-install covers the decision matrix with substitutes.
    private static void Takeover()
    {
        var game = LegacySeed("takeover", out var steam);
        using (var bundle = new NativeMaintenanceBundle(Package, game)) {
            NativeInstall.Run(bundle, false, steam, 1, TakeoverConsent, Say);
            foreach (var file in bundle.Expected) Check(bundle.TargetHash(file.Key) == file.Value, "takeover installs the authenticated 1.4 payload: " + file.Key);
            var receipt = InstallReceipt.Parse(bundle.ReadTarget(InstallReceipt.Path, 16384)!);
            Check(receipt.LoaderBefore == true && receipt.Channels?.ActiveVersion == FixtureBoundary.Version, "takeover writes a protocol-5 receipt and keeps the first loader baseline");
        }
        foreach (var gone in new[] { OrdinaryInstallRecovery.LegacyLauncher, OrdinaryInstallRecovery.LegacyHelper, OrdinaryInstallRecovery.LegacyHelperReceipt, NativeInstall.LegacyHighWater })
            Check(!File.Exists(WindowsPath.ForIo(Full(game, gone))), "takeover removes owned 1.3.x file: " + gone);
        Check(File.Exists(WindowsPath.ForIo(Full(game, UpdateTransaction.HighWaterPath))), "takeover writes the v5 high-water");
        LegacyWorkCleaned(game, "takeover");
        Check(Encoding.UTF8.GetString(Read(Full(game, "BepInEx/config/kr.betterastralparty.mod.cfg"))) == "1.3.x player setting" && Encoding.UTF8.GetString(Read(Full(game, "BepInEx/plugins/OtherMod/Other.dll"))) == "unrelated mod retained"
            && Encoding.UTF8.GetString(Read(Full(game, "doorstop_config.ini"))).Contains("unrelated=preserved"), "takeover keeps settings, shared loader and other mods");
        var shortcuts = NativeSteamMaintenance.ReadShortcuts(Read(Full(steam, "userdata/1/config/shortcuts.vdf"))); bool legacy;
        var owned = NativeSteamMaintenance.OwnedShortcutKeys(shortcuts, game, out legacy); var entry = (OrderedDictionary)((OrderedDictionary)shortcuts["shortcuts"]!)[owned[0]]!;
        Check(owned.SequenceEqual(new[] { "7" }) && !legacy && (uint)entry["appid"]! == 0x80001234U && ((OrderedDictionary)shortcuts["shortcuts"]!).Contains("99"), "launcher shortcut converted in place to Mod.cmd with the same app ID; unrelated shortcut kept");
        // The shortcut write is the last step, after every legacy removal. Steam reported running only while the
        // journal is open fails that write inside the transaction and forces the real journal rollback.
        var failing = LegacySeed("takeover-rollback", out var failingSteam);
        var before = Snapshot(failing); foreach (var pair in Snapshot(failingSteam)) before.Add(pair.Key, pair.Value);
        var observer = NativeSteamMaintenance.SteamStatusObserver;
        NativeSteamMaintenance.SteamStatusObserver = () => new SteamProcessStatus { State = File.Exists(WindowsPath.ForIo(NativeMaintenance.JournalPath(failing))) ? "Running" : "Stopped" };
        try { using (var bundle = new NativeMaintenanceBundle(Package, failing))
            try { NativeInstall.Run(bundle, false, failingSteam, 1, TakeoverConsent, Say); throw new InvalidOperationException("Takeover accepted a held shortcuts file."); }
            catch (Exception error) when (!error.Message.StartsWith("Takeover accepted", StringComparison.Ordinal)) { Check(Equals(error.Data["BapInstallStage"], "Steam shortcut registration"), "takeover stops at the shortcut write after legacy removal; stage=" + error.Data["BapInstallStage"]); } }
        finally { NativeSteamMaintenance.SteamStatusObserver = observer; }
        Check(Directory.GetFiles(WindowsPath.ForIo(NativeMaintenance.JournalRoot(failing)), "restored.json", SearchOption.AllDirectories).Length == 1, "failure happened inside the journal and was rolled back by it");
        var after = Snapshot(failing); foreach (var pair in Snapshot(failingSteam)) after.Add(pair.Key, pair.Value);
        var changed = before.Where(row => !after.TryGetValue(row.Key, out var hash) || hash != row.Value).Select(row => row.Key).Concat(after.Keys.Where(key => !before.ContainsKey(key) && key != Full(failing, "BetterAstralParty.update.lock"))).ToArray(); // The maintenance lock file is kept, as after any install.
        Check(changed.Length == 0, "failed takeover restores the exact 1.3.x game and Steam bytes; changed=" + string.Join(";", changed.Take(5)));
    }
    private static int Main(string[] args)
    {
        var prior = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            if (args.Length == 2 && args[0] == "emergency-hold") { EmergencyHold(args[1]); return 0; }
            Scope(Package); if (args.Length != 1) throw new ArgumentException("One isolated fixture mode required.");
            Environment.SetEnvironmentVariable("LOCALAPPDATA", Scratch);
            switch (args[0])
            {
                case "seed": Seed(); break; case "prepare": Prepare(); break; case "rebind": Rebind(); break; case "auth": Authenticate(); break; case "unused-inputs": UnusedPackageInputs(); break;
                case "documents-edited": Documents(false); break; case "documents-missing": Documents(true); break;
                case "preview": Install(false, true); break; case "install": Install(false, false); break; case "water": HistoricalWater(); break;
                case "pending": Pending(); break; case "upgrade-preview": Install(true, true); break; case "upgrade": Install(true, false); Preserved(); Check(Encoding.UTF8.GetString(Read(Full(Game, "BepInEx/config/kr.betterastralparty.mod.cfg"))) == "user setting preserved on update", "UpdateOnly retains mod user settings");
                    Check(Encoding.ASCII.GetString(Read(Full(Game, UpdateTransaction.HighWaterPath))).Contains("\nversion=" + FixtureBoundary.Version + "\n"), "newer signed whole upgrade advances the high-water");
                    Write(Full(Scratch, "expected-water.bin"), Read(Full(Game, UpdateTransaction.HighWaterPath))); break;
                case "changed-owned": ChangedOwned(); break; case "uninstall-preview": Uninstall(true); break; case "uninstall": Uninstall(false); break;
                case "game-updated-uninstall-preview": GameUpdatedUninstall(true); break; case "game-updated-uninstall": GameUpdatedUninstall(false); break;
                case "optional-steam-uninstall": OptionalSteamUninstall(); break;
                case "missing-game-removal": RemovalWithoutGame(); break;
                case "emergency": Emergency(); break; case "takeover": Takeover(); break;
                case "reinstall-preview": Reinstall(true); break; case "reinstall": Reinstall(false); break; default: throw new ArgumentException("Unknown inert fixture mode.");
            }
            Json(Full(Scratch, "results/" + FixtureBoundary.Channel + "-" + args[0] + ".json"), new { Passed = true, Mode = args[0], Version = FixtureBoundary.Version, Checks = _checks, ActualNativeAuthInstallUninstallAndSteamApis = true, Facades = false, Output });
            Console.WriteLine("PASS " + FixtureBoundary.Channel + "/" + args[0] + ": " + _checks + " checks"); return 0;
        }
        catch (Exception error)
        {
            var mode = args.Length == 1 ? args[0] : "invalid";
            string Bounded(object? value) { var text = (value as string ?? "unknown").Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' '); return text.Substring(0, Math.Min(text.Length, 256)); }
            var facts = new List<object>(); Exception? at = error;
            for (var depth = 0; at != null && depth < 8; depth++, at = at.InnerException) {
                var safety = at as ApplySafetyException;
                facts.Add(new { depth, mode = Bounded(mode), type = Bounded(at.GetType().FullName), hresult = "0x" + at.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                    apply = safety == null || !Enum.IsDefined(typeof(ApplyFailure), safety.Failure) ? "unknown" : safety.Failure.ToString(), native = safety?.NativeError,
                    phase = Bounded(at.Data["BapEmergencyPhase"] ?? at.Data["BapInstallStage"]), reason = Bounded(at.Data["BapRecoveryReason"]) });
            }
            Json(Full(Scratch, "results/" + FixtureBoundary.Channel + "-" + mode + "-failed.json"), new { Passed = false, Mode = mode, Checks = _checks, Error = error.ToString(), Stage = error.Data["BapInstallStage"], Reason = error.Data["BapRecoveryReason"], Rollback = (error.Data["BapRollbackFailure"] as Exception)?.ToString(), FailureFacts = facts, Output });
            Console.Error.WriteLine("FixtureFailureFacts: " + Encoding.UTF8.GetString(MaintenanceJson.Write(facts)));
            Console.Error.WriteLine(error); return 1;
        }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", prior); }
    }
}
