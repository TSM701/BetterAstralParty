using System.Text.RegularExpressions;

namespace BetterAstralParty;

// Settings search (user-chosen 2026-11-02, the search row with the match box): a query keeps the settings whose name,
// location tag or description holds every word, ignoring case and spaces ("줌감도" finds "줌 감도"). A row whose name
// does not hold every word shows why it matched in a box under its name: its location tag when a word is in it, and the
// first description sentence that holds one.
internal static class SettingsSearch
{
    // Tag: a word is in the location tag. Sentence: the description sentence holding a word. Neither: the name holds them all.
    internal readonly record struct Hit(bool Tag, string? Sentence)
    {
        internal bool Boxed => Tag || Sentence != null;
    }

    internal static string[] Words(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToLowerInvariant()).Distinct().ToArray();

    // The text lower-cased without whitespace, and where each kept character sits in the original.
    private static (string Text, int[] Map) Squeeze(string text)
    {
        var kept = new System.Text.StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) { kept.Append(char.ToLowerInvariant(text[i])); map.Add(i); }
        return (kept.ToString(), map.ToArray());
    }

    internal static bool Has(string text, string word) => Squeeze(text).Text.Contains(word, StringComparison.Ordinal);

    // Every occurrence of every word, as [start, end) ranges of the original text, merged where they touch.
    internal static List<(int Start, int End)> Ranges(string text, string[] words)
    {
        var (squeezed, map) = Squeeze(text);
        var ranges = new List<(int Start, int End)>();
        foreach (var word in words)
            for (var at = squeezed.IndexOf(word, StringComparison.Ordinal); at >= 0; at = squeezed.IndexOf(word, at + 1, StringComparison.Ordinal))
                ranges.Add((map[at], map[at + word.Length - 1] + 1));
        ranges.Sort();
        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges)
            if (merged.Count > 0 && range.Start <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else merged.Add(range);
        return merged;
    }

    internal static Hit? Match(string[] words, IReadOnlyCollection<string> names, string? tag, IReadOnlyCollection<string> descriptions)
    {
        if (words.Length == 0) return null;
        static bool In(IEnumerable<string> texts, string word) => texts.Any(text => Has(text, word));
        var tags = tag == null ? Array.Empty<string>() : new[] { tag };
        if (!words.All(word => In(names, word) || In(tags, word) || In(descriptions, word))) return null;
        if (words.All(word => In(names, word))) return new Hit(false, null);
        var sentence = descriptions.SelectMany(Sentences).FirstOrDefault(line => words.Any(word => Has(line, word)));
        return new Hit(words.Any(word => In(tags, word)), sentence);
    }

    // A help text's lines joined, then split after each sentence end.
    internal static IEnumerable<string> Sentences(string text) =>
        Regex.Split(text.Replace('\n', ' '), @"(?<=[.!?])\s+").Select(line => line.Trim()).Where(line => line.Length > 0);

    // The part of a sentence around its first match that fits, cut between words, with an ellipsis where it was cut:
    // as much context before the match as fits with the match's own word, then as much after.
    internal static string Fit(string sentence, string[] words, float max, Func<string, float> measure)
    {
        if (measure(sentence) <= max) return sentence;
        var first = Ranges(sentence, words).FirstOrDefault();
        var starts = new List<int> { 0 }; var ends = new List<int>();
        for (var i = 0; i < sentence.Length; i++)
            if (sentence[i] == ' ') { ends.Add(i); starts.Add(i + 1); }
        ends.Add(sentence.Length);
        starts.RemoveAll(start => start > first.Start);
        ends.RemoveAll(end => end < first.End);
        string Cut(int start, int end) => (start > 0 ? "…" : "") + sentence[start..end] + (end < sentence.Length ? "…" : "");
        var from = starts.FirstOrDefault(start => measure(Cut(start, ends[0])) <= max, starts[^1]);
        var to = ends[0];
        foreach (var end in ends.Skip(1))
            if (measure(Cut(from, end)) <= max) to = end; else break;
        return Cut(from, to);
    }
}
