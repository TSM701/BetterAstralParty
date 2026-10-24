using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

// In-game verification coverage: compares the plugin's native access ledger ("native.access" lines in the
// runtime diagnostic logs) with the native members the source reads. Read-only; it never starts the game.
// shortcut: source receivers are untyped strings, so a member counts as exercised when its name was resolved on
// any class; a typed per-call-site ledger would need caller info on every native call.
internal static class NativeCoverage
{
    internal sealed record Access(string Kind, string Name); // Kind: field, method or class.
    internal sealed class Ledger
    {
        internal readonly HashSet<Access> Seen = new();
        internal readonly SortedSet<string> Missing = new(StringComparer.Ordinal);
        internal int Sessions;
    }

    // Plugin.Version from the canonical source, used to select this build's sessions by default.
    private static string SourceVersion(string root) =>
        Regex.Match(File.ReadAllText(Path.Combine(root, "src", "Plugin.cs")), "const string Version = \"([^\"]+)\"").Groups[1].Value;

    internal static Ledger Parse(IEnumerable<string> lines, string? version)
    {
        var ledger = new Ledger(); var active = false;
        // A lookup made before the game finished loading can fail and succeed later in the same session; only
        // lookups that never succeeded within their session count as failures.
        var missing = new HashSet<string>(StringComparer.Ordinal); var resolved = new HashSet<string>(StringComparer.Ordinal);
        void EndSession() { foreach (var miss in missing) if (!resolved.Contains(miss)) ledger.Missing.Add(miss); missing.Clear(); resolved.Clear(); }
        foreach (var line in lines)
        {
            var at = line.IndexOf(" | ", StringComparison.Ordinal);
            if (at < 0) continue;
            var message = line[(at + 3)..];
            if (message.StartsWith("diagnostics ON | ", StringComparison.Ordinal) || message.StartsWith("diagnostics CONTINUE | ", StringComparison.Ordinal))
            {
                var mod = Regex.Match(message, @"\bmod=([^;\s]+)").Groups[1].Value;
                if (message.StartsWith("diagnostics ON", StringComparison.Ordinal)) EndSession();
                active = version == null || mod == version;
                if (active && message.StartsWith("diagnostics ON", StringComparison.Ordinal)) ledger.Sessions++;
                continue;
            }
            if (!active || !message.StartsWith("native.access ", StringComparison.Ordinal)) continue;
            foreach (var entry in message["native.access ".Length..].Split("; ", StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.StartsWith("missing ", StringComparison.Ordinal)) { missing.Add(entry["missing ".Length..]); continue; }
                var space = entry.IndexOf(' ');
                if (space < 0) continue;
                resolved.Add(entry);
                var kind = entry[..space]; var target = entry[(space + 1)..];
                if (kind == "class") { ledger.Seen.Add(new("class", target)); continue; }
                var dot = target.LastIndexOf('.');
                var member = dot < 0 ? target : target[(dot + 1)..];
                var slash = member.IndexOf('/');
                if (slash >= 0) member = member[..slash];
                if (kind is "field" or "method") ledger.Seen.Add(new(kind, member));
            }
        }
        EndSession();
        return ledger;
    }

    private static readonly (Regex Pattern, string Kind, string Prefix)[] Patterns =
    {
        (new(@"\.Get(?:<[^>()]+>)?\(""([A-Za-z_]\w*)"""), "method", "get_"),
        (new(@"\.Set\(""([A-Za-z_]\w*)"""), "method", "set_"),
        (new(@"\.(?:Field(?:<[^>()]+>)?|FieldPointer|SetField(?:<[^>()]+>)?)\(""([A-Za-z_]\w*)"""), "field", ""),
        (new(@"StaticField\([^,()]+(?:\([^()]*\))?,\s*""([A-Za-z_]\w*)"""), "field", ""),
        (new(@"\.Call\(""([.A-Za-z_]\w*)"""), "method", ""),
        (new(@"StaticCall\([^,()]+(?:\([^()]*\))?,\s*""([.A-Za-z_]\w*)"""), "method", ""),
    };
    private static readonly Regex ClassPattern = new(@"FindClass\(""([\w.]*)"",\s*""(\w+)""\)");

    // Distinct native accesses named by string literals in one source file.
    internal static SortedSet<string> Scan(string source)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (pattern, kind, prefix) in Patterns)
            foreach (Match match in pattern.Matches(source)) found.Add(kind + " " + prefix + match.Groups[1].Value);
        foreach (Match match in ClassPattern.Matches(source))
            found.Add("class " + (match.Groups[1].Value.Length == 0 ? "" : match.Groups[1].Value + ".") + match.Groups[2].Value);
        return found;
    }

    internal static bool Exercised(Ledger ledger, string access)
    {
        var space = access.IndexOf(' ');
        return ledger.Seen.Contains(new(access[..space], access[(space + 1)..]));
    }

    // The plugin's per-user runtime logs, oldest first.
    internal static IEnumerable<string> RuntimeLogs()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Runtime", "Logs");
        return new[] { "previous-4.log", "previous-3.log", "previous-2.log", "previous-overflow.log", "previous.log", "current-overflow.log", "current.log" }
            .Select(name => Path.Combine(folder, name)).Where(File.Exists);
    }

    internal static int Run(DevContext context, string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var logs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) throw new ArgumentException("Usage: native-coverage [--log <file>]... [--version <mod version>|any] [--since <build directory>] [--output <file>]");
            var key = args[i][2..]; var value = args[++i];
            if (key.Equals("log", StringComparison.OrdinalIgnoreCase)) logs.Add(Path.GetFullPath(value)); else options[key] = value;
        }
        if (logs.Count == 0) logs.AddRange(RuntimeLogs());
        var version = options.TryGetValue("version", out var requested) ? requested == "any" ? null : requested : SourceVersion(context.Root);
        var ledger = Parse(logs.SelectMany(path => File.ReadLines(path, Encoding.UTF8)), version);

        var src = Path.Combine(context.Root, "src");
        var files = Directory.GetFiles(src, "*.cs").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        HashSet<string>? changed = null;
        if (options.TryGetValue("since", out var since))
        {
            var record = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Path.GetFullPath(since), "build-source.json")));
            var before = record.RootElement.EnumerateArray().ToDictionary(row => row.GetProperty("path").GetString()!.Replace('\\', '/'), row => row.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
            changed = files.Select(path => "src/" + Path.GetFileName(path))
                .Where(rel => !before.TryGetValue(rel, out var hash) || hash != context.Hash(Path.Combine(context.Root, rel))).ToHashSet(StringComparer.Ordinal);
        }

        var output = new StringBuilder();
        output.AppendLine($"Native coverage for {(version ?? "any version")}: {ledger.Sessions} session(s) in {logs.Count} log file(s); {ledger.Seen.Count} distinct names resolved in game.");
        output.AppendLine("Exercised means the member name was resolved on some class in those sessions (diagnostic logging ON); it is not a per-call-site guarantee.");
        output.AppendLine();
        output.AppendLine(ledger.Missing.Count == 0 ? "Failed native lookups: none" : $"Failed native lookups ({ledger.Missing.Count}):");
        foreach (var miss in ledger.Missing) output.AppendLine("  " + miss);
        output.AppendLine();
        int total = 0, hit = 0;
        var rows = new List<(string File, int Total, int Hit, string[] Unseen)>();
        foreach (var path in files)
        {
            var accesses = Scan(File.ReadAllText(path));
            if (accesses.Count == 0) continue;
            var unseen = accesses.Where(access => !Exercised(ledger, access)).ToArray();
            rows.Add(("src/" + Path.GetFileName(path), accesses.Count, accesses.Count - unseen.Length, unseen));
            total += accesses.Count; hit += accesses.Count - unseen.Length;
        }
        output.AppendLine($"Source native accesses exercised: {hit}/{total}");
        if (changed != null) output.AppendLine($"Changed since {since}: {(changed.Count == 0 ? "none" : string.Join(", ", changed.Order(StringComparer.Ordinal)))}");
        foreach (var row in rows.OrderByDescending(row => changed?.Contains(row.File) == true).ThenBy(row => row.Hit == row.Total).ThenBy(row => row.File, StringComparer.Ordinal))
        {
            var mark = changed?.Contains(row.File) == true ? "* " : "  ";
            output.AppendLine($"{mark}{row.File}: {row.Hit}/{row.Total}" + (row.Unseen.Length == 0 ? "" : " not seen: " + string.Join(", ", row.Unseen.Take(12)) + (row.Unseen.Length > 12 ? $", +{row.Unseen.Length - 12} more" : "")));
        }
        Console.Write(output.ToString());
        if (options.TryGetValue("output", out var file)) File.WriteAllText(Path.GetFullPath(file), output.ToString(), new UTF8Encoding(false));
        return ledger.Missing.Count == 0 ? 0 : 2;
    }

    // Parser/scanner self-check with synthetic logs and source; runs as the native-coverage build check.
    internal static void SelfCheck(Action<bool, string> check)
    {
        var lines = new[]
        {
            "2026-10-20T00:00:00+09:00 | diagnostics ON | mod=9.9.9-dev.1; runtime=6.0; session=a",
            "2026-10-20T00:00:01+09:00 | native.access class FairyGUI.GObject; field FairyGUI.GObject.name; method FairyGUI.GObject.get_visible/0",
            "2026-10-20T00:00:02+09:00 | native.access missing method FairyGUI.GObject.get_name/0 (a field of that name exists, read it with Field)",
            "2026-10-20T00:00:02+09:00 | native.access missing class Late.Type; missing class Cross.Type",
            "2026-10-20T00:00:02+09:00 | native.access class Late.Type",
            "2026-10-20T00:00:03+09:00 | diagnostics ON | mod=1.0.0; runtime=6.0; session=b",
            "2026-10-20T00:00:04+09:00 | native.access field Other.Type.secret",
            "2026-10-20T00:00:05+09:00 | diagnostics ON | mod=9.9.9-dev.1; runtime=6.0; session=c",
            "2026-10-20T00:00:06+09:00 | native.access class Cross.Type",
        };
        var ledger = Parse(lines, "9.9.9-dev.1");
        check(ledger.Sessions == 2 && ledger.Seen.Contains(new("field", "name")) && ledger.Seen.Contains(new("method", "get_visible"))
            && ledger.Seen.Contains(new("class", "FairyGUI.GObject")) && !ledger.Seen.Contains(new("field", "secret")), "Native ledger keeps only the selected version's sessions");
        check(ledger.Missing.Count == 2 && ledger.Missing.Contains("method FairyGUI.GObject.get_name/0 (a field of that name exists, read it with Field)"), "Native ledger reports failed lookups with class and hint");
        check(!ledger.Missing.Contains("class Late.Type") && ledger.Missing.Contains("class Cross.Type"), "A lookup that succeeds later in its own session is not a failure; one in another session still is");
        var source = "x.Get<bool>(\"visible\"); x.Get(\"name\"); x.Field(\"name\")?.String(); RuntimeObject.StaticField(RuntimeObject.FindClass(\"UI\", \"UIConfig\"), \"clickDragSensitivity\"); x.Call(\"SetXY\", 1f, 2f); x.Set(\"grayed\", true); RuntimeObject.StaticCall(klass, \"get_inst\");";
        var scanned = Scan(source);
        check(scanned.SetEquals(new[] { "method get_visible", "method get_name", "field name", "field clickDragSensitivity", "class UI.UIConfig", "method SetXY", "method set_grayed", "method get_inst" }), "Source scan finds literal native accesses by kind");
        check(Exercised(ledger, "field name") && !Exercised(ledger, "method get_name") && !Exercised(ledger, "method SetXY"), "Coverage matches by kind and member name");
    }
}
