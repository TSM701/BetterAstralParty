#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Framework JSON parser plus a bounded duplicate-key check before conversion.
internal static class MaintenanceJsonReader
{
    internal static object? Read(byte[] bytes, int limit)
    {
        if (bytes.Length == 0 || bytes.Length > limit) throw new InvalidDataException("Metadata bounds");
        var text = new UTF8Encoding(false, true).GetString(bytes);
        new Check(text).Run();
        return new JavaScriptSerializer { MaxJsonLength = limit, RecursionLimit = 16 }.DeserializeObject(text);
    }
    private sealed class Check
    {
        private readonly string _text; private int _at, _values;
        internal Check(string text) { _text = text; }
        private void Space() { while (_at < _text.Length && " \r\n\t".IndexOf(_text[_at]) >= 0) _at++; }
        private bool Take(char value) { Space(); if (_at < _text.Length && _text[_at] == value) { _at++; return true; } return false; }
        private static InvalidDataException Bad() => new InvalidDataException("Invalid or ambiguous metadata");
        private string Text()
        {
            Space(); var start = _at; if (!Take('"')) throw Bad();
            while (_at < _text.Length) {
                var c = _text[_at++];
                if (c == '"') return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<string>(_text.Substring(start, _at - start));
                if (c < 32) throw Bad();
                if (c == '\\') {
                    if (_at >= _text.Length) throw Bad(); c = _text[_at++];
                    if (c == 'u') { if (_at > _text.Length - 4 || !Regex.IsMatch(_text.Substring(_at, 4), "\\A[0-9A-Fa-f]{4}\\z")) throw Bad(); _at += 4; }
                    else if ("\"\\/bfnrt".IndexOf(c) < 0) throw Bad();
                }
            }
            throw Bad();
        }
        private void Value(int depth)
        {
            Space(); if (depth > 16 || ++_values > 50000 || _at >= _text.Length) throw Bad();
            if (_text[_at] == '"') { Text(); return; }
            if (Take('{')) {
                var keys = new HashSet<string>(StringComparer.Ordinal); if (Take('}')) return;
                do { var key = Text(); if (keys.Count >= 256 || !keys.Add(key) || !Take(':')) throw Bad(); Value(depth + 1); } while (Take(','));
                if (!Take('}')) throw Bad(); return;
            }
            if (Take('[')) { if (Take(']')) return; do { Value(depth + 1); } while (Take(',')); if (!Take(']')) throw Bad(); return; }
            var start = _at;
            while (_at < _text.Length && ",]} \r\n\t".IndexOf(_text[_at]) < 0) _at++;
            var token = _text.Substring(start, _at - start);
            if (token != "true" && token != "false" && token != "null"
                && !Regex.IsMatch(token, @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant)) throw Bad();
        }
        internal void Run() { Value(0); Space(); if (_at != _text.Length) throw Bad(); }
    }
    internal static Dictionary<string, object> Object(object? value) => value as Dictionary<string, object> ?? throw new InvalidDataException("Metadata object required");
    internal static object[] Array(object? value) => value as object[] ?? throw new InvalidDataException("Metadata list required");
    internal static object? Get(Dictionary<string, object> value, string name) { object? found; return value.TryGetValue(name, out found) ? found : throw new InvalidDataException("Missing metadata field"); }
    internal static string String(Dictionary<string, object> value, string name, int limit = 4096) => Get(value, name) is string s && s.Length <= limit ? s : throw new InvalidDataException("Invalid metadata text");
    internal static bool Bool(Dictionary<string, object> value, string name) => Get(value, name) is bool b ? b : throw new InvalidDataException("Invalid metadata flag");
    internal static long Id(Dictionary<string, object> value, string name)
    {
        var raw = Get(value, name); var result = raw is int i ? i : raw is long l ? l : 0;
        return result > 0 ? result : throw new InvalidDataException("Invalid metadata identifier");
    }
}
