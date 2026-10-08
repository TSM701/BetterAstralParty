#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterAstralParty.Updating
{
    // Bounded PE/CLI metadata reader. Never Assembly.Load/execute an installed DLL.
    // ECMA-335 II.22/24/25; intentionally supports optimized #~ metadata only.
    internal sealed class InstalledPluginMetadata
    {
        internal const string Owner = "kr.betterastralparty.mod";
        internal readonly string Version;
        internal readonly int UpdateProtocol, SettingsSchema;
        private InstalledPluginMetadata(string version, int protocol = 0, int schema = 0) { Version = version; UpdateProtocol = protocol; SettingsSchema = schema; }
        private sealed class Reader
        {
            internal readonly byte[] B;
            internal Reader(byte[] bytes) { B = bytes; }
            internal void Range(int p, int n) { if (p < 0 || n < 0 || p > B.Length - n) throw Bad(); }
            internal int U16(int p) { Range(p, 2); return B[p] | B[p + 1] << 8; }
            internal uint U32(int p) { Range(p, 4); return (uint)(B[p] | B[p + 1] << 8 | B[p + 2] << 16 | B[p + 3] << 24); }
            internal int I32(int p) { var v = U32(p); if (v > int.MaxValue) throw Bad(); return (int)v; }
            internal int Index(int p, int width) { return width == 2 ? U16(p) : I32(p); }
            internal string String(int start, int size, int offset)
            {
                if (offset < 0 || offset >= size) throw Bad(); var p = start + offset; var end = p;
                while (end < start + size && end - p <= 256 && B[end] != 0) end++;
                if (end >= start + size || end - p > 256) throw Bad();
                return new UTF8Encoding(false, true).GetString(B, p, end - p);
            }
            internal byte[] Blob(int start, int size, int offset)
            {
                if (offset <= 0 || offset >= size) throw Bad(); var p = start + offset; var first = B[p++]; int length;
                if (first < 128) length = first;
                else if (first < 192) { if (p >= start + size) throw Bad(); length = ((first & 63) << 8) | B[p++]; }
                else if (first < 224) { if (p > start + size - 3) throw Bad(); length = ((first & 31) << 24) | B[p++] << 16 | B[p++] << 8 | B[p++]; }
                else throw Bad();
                if (length < 0 || p > start + size - length || length > 512) throw Bad();
                var result = new byte[length]; Buffer.BlockCopy(B, p, result, 0, length); return result;
            }
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.InvalidState); }
        internal static InstalledPluginMetadata Read(byte[] image)
        {
            try { return Parse(image); }
            catch (ApplySafetyException) { throw; }
            catch (Exception error) when (error is OverflowException || error is DecoderFallbackException || error is ArgumentException || error is IndexOutOfRangeException)
            { throw Bad(); }
        }
        private static InstalledPluginMetadata Parse(byte[] image)
        {
            if (image == null || image.Length < 256 || image.LongLength > UpdateTrust.MaxFileBytes) throw Bad();
            var r = new Reader(image); if (r.U16(0) != 0x5a4d) throw Bad(); var pe = r.I32(60);
            if (r.U32(pe) != 0x4550) throw Bad(); var sections = r.U16(pe + 6); var optionalSize = r.U16(pe + 20); var optional = pe + 24;
            if (sections < 1 || sections > 96) throw Bad(); r.Range(optional, optionalSize);
            var magic = r.U16(optional); var directory = magic == 0x10b ? 96 : magic == 0x20b ? 112 : throw Bad();
            if (optionalSize < directory + 120 || r.U32(optional + directory - 4) < 15) throw Bad();
            var sectionStart = optional + optionalSize; r.Range(sectionStart, sections * 40);
            Func<uint, int, int> map = (address, length) => {
                int found = -1;
                for (var i = 0; i < sections; i++) {
                    var p = sectionStart + i * 40; var va = r.U32(p + 12); var rawSize = r.U32(p + 16); var raw = r.U32(p + 20);
                    if (address >= va && (ulong)address - va + (uint)length <= rawSize) {
                        var off = checked((int)(raw + (ulong)address - va)); r.Range(off, length);
                        if (found >= 0) throw Bad(); found = off;
                    }
                }
                if (found < 0) throw Bad(); return found;
            };
            var cli = map(r.U32(optional + directory + 14 * 8), 72);
            if (r.U32(cli) < 72 || (r.U32(cli + 16) & 1) == 0) throw Bad();
            var metadataSize = r.I32(cli + 12); var metadata = map(r.U32(cli + 8), metadataSize);
            if (metadataSize < 32 || r.U32(metadata) != 0x424a5342) throw Bad();
            var versionSize = r.I32(metadata + 12); if (versionSize > 256) throw Bad();
            var cursor = checked(metadata + 16 + ((versionSize + 3) & ~3)); r.Range(cursor, 4); var streams = r.U16(cursor + 2); cursor += 4;
            if (streams < 3 || streams > 8) throw Bad(); var heaps = new Dictionary<string, int[]>(StringComparer.Ordinal);
            for (var i = 0; i < streams; i++) {
                var offset = r.I32(cursor); var size = r.I32(cursor + 4); cursor += 8;
                var name = r.String(cursor, Math.Min(32, metadata + metadataSize - cursor), 0); cursor += (name.Length + 1 + 3) & ~3;
                if (offset < 0 || size < 0 || offset > metadataSize - size || heaps.ContainsKey(name)) throw Bad();
                heaps.Add(name, new[] { metadata + offset, size });
            }
            if (!heaps.ContainsKey("#~") || !heaps.ContainsKey("#Strings") || !heaps.ContainsKey("#Blob") || heaps.ContainsKey("#-")) throw Bad();
            foreach (var a in heaps.Values) { if (a[0] < cursor) throw Bad(); foreach (var b in heaps.Values) if (!ReferenceEquals(a, b) && a[0] < b[0] + b[1] && b[0] < a[0] + a[1]) throw Bad(); }
            var t = heaps["#~"]; var s = heaps["#Strings"]; var bheap = heaps["#Blob"]; r.Range(t[0], 24);
            if (image[t[0] + 4] != 2 || image[t[0] + 5] != 0 || (image[t[0] + 6] & ~7) != 0) throw Bad();
            var valid = r.U32(t[0] + 8) | ((ulong)r.U32(t[0] + 12) << 32); if ((valid >> 45) != 0) throw Bad();
            var rows = new int[45]; cursor = t[0] + 24;
            for (var i = 0; i < rows.Length; i++) if ((valid & (1UL << i)) != 0) { rows[i] = r.I32(cursor); cursor += 4; if (rows[i] > 1000000) throw Bad(); }
            if (rows[3] != 0 || rows[5] != 0 || rows[7] != 0) throw Bad();
            Func<int, int> idx = table => rows[table] < 65536 ? 2 : 4;
            Func<int, int[], int> coded = (bits, tables) => { var max = 0; foreach (var table in tables) max = Math.Max(max, rows[table]); return max < (1 << (16 - bits)) ? 2 : 4; };
            var sw = (image[t[0] + 6] & 1) == 0 ? 2 : 4; var gw = (image[t[0] + 6] & 2) == 0 ? 2 : 4; var bw = (image[t[0] + 6] & 4) == 0 ? 2 : 4;
            var tdor = coded(2, new[] { 2, 1, 27 }); var hc = coded(2, new[] { 4, 8, 23 });
            var widths = new[] { 2 + sw + 3 * gw, coded(2, new[] { 0, 26, 35, 1 }) + 2 * sw,
                4 + 2 * sw + tdor + idx(4) + idx(6), idx(4), 2 + sw + bw, idx(6), 8 + sw + bw + idx(8), idx(8), 4 + sw,
                idx(2) + tdor, coded(3, new[] { 2, 1, 26, 6, 27 }) + sw + bw, 2 + hc + bw };
            var starts = new int[12];
            for (var i = 0; i < starts.Length; i++) { starts[i] = cursor; cursor = checked(cursor + rows[i] * widths[i]); if (cursor > t[0] + t[1]) throw Bad(); }
            var firstField = 0; var lastField = 0;
            for (var i = 0; i < rows[2]; i++) {
                var p = starts[2] + i * widths[2];
                if (r.String(s[0], s[1], r.Index(p + 4, sw)) == "Plugin" && r.String(s[0], s[1], r.Index(p + 4 + sw, sw)) == "BetterAstralParty") {
                    if (firstField != 0) throw Bad(); firstField = r.Index(p + 4 + 2 * sw + tdor, idx(4));
                    lastField = i + 1 < rows[2] ? r.Index(p + widths[2] + 4 + 2 * sw + tdor, idx(4)) : rows[4] + 1;
                }
            }
            if (firstField < 1 || lastField <= firstField || lastField > rows[4] + 1) throw Bad();
            var wanted = new Dictionary<int, string>(); var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = firstField; i < lastField; i++) {
                var p = starts[4] + (i - 1) * widths[4]; var name = r.String(s[0], s[1], r.Index(p + 2, sw));
                if (name != "Version" && name != "Guid" && name != "UpdateProtocol" && name != "SettingsSchema") continue;
                if ((r.U16(p) & 0x50) != 0x50 || wanted.ContainsValue(name)) throw Bad();
                var signature = r.Blob(bheap[0], bheap[1], r.Index(p + 2 + sw, bw));
                if (signature.Length != 2 || signature[0] != 6 || signature[1] != 14) throw Bad(); wanted.Add(i, name);
            }
            foreach (var field in wanted) for (var i = 0; i < rows[11]; i++) {
                var p = starts[11] + i * widths[11]; if (r.Index(p + 2, hc) != (field.Key << 2)) continue;
                if (r.U16(p) != 14 || values.ContainsKey(field.Value)) throw Bad();
                var blob = r.Blob(bheap[0], bheap[1], r.Index(p + 2 + hc, bw)); if ((blob.Length & 1) != 0) throw Bad();
                values.Add(field.Value, new UnicodeEncoding(false, false, true).GetString(blob));
            }
            if (!values.ContainsKey("Guid") || !values.ContainsKey("Version") || values["Guid"] != Owner) throw Bad(); UpdateVersion.Parse(values["Version"]);
            var extended = values.ContainsKey("UpdateProtocol") || values.ContainsKey("SettingsSchema");
            if (extended && (values.Count != 4 || values["UpdateProtocol"] != "2" || values["SettingsSchema"] != "1")) throw Bad();
            return new InstalledPluginMetadata(values["Version"], extended ? 2 : 0, extended ? 1 : 0);
        }
    }
}
