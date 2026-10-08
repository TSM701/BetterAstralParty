#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BetterAstralParty.Updating
{
    internal sealed class VerifiedUpdatePackage
    {
        internal readonly VerifiedUpdateDescriptor Descriptor;
        internal readonly int FileCount;
        internal readonly long ExpandedBytes;
        internal VerifiedUpdatePackage(VerifiedUpdateDescriptor descriptor, int count, long bytes)
        { Descriptor = descriptor; FileCount = count; ExpandedBytes = bytes; }
    }
    // The byte image that passed validation stays private for its entire lifetime.
    // Readers cannot write, expose its backing array or substitute a different ZIP.
    internal sealed class VerifiedUpdatePayload
    {
        private readonly byte[] _image;
        internal readonly VerifiedUpdatePackage Package;
        internal VerifiedUpdatePayload(object stamp, byte[] image, VerifiedUpdatePackage package)
        {
            if (!UpdatePackage.ValidPayloadStamp(stamp)) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
            _image = image; Package = package;
        }
        internal Stream OpenRead() { return new MemoryStream(_image, 0, _image.Length, false, false); }
    }
    // Validation only: never extracts, executes, installs or writes an untrusted archive.
    internal static class UpdatePackage
    {
        private static readonly object PayloadStamp = new object();
        internal static bool ValidPayloadStamp(object stamp) { return ReferenceEquals(stamp, PayloadStamp); }
        private sealed class Entry
        {
            internal string Name = "";
            internal uint Crc, Compressed, Expanded, Offset;
            internal ushort Flags, Method;
        }
        private static UpdateValidationException Invalid() { return new UpdateValidationException(UpdateFailure.InvalidPackage); }
        private static ushort U16(byte[] b, int p)
        {
            if (p < 0 || p > b.Length - 2) throw Invalid();
            return (ushort)(b[p] | b[p + 1] << 8);
        }
        private static uint U32(byte[] b, int p)
        {
            if (p < 0 || p > b.Length - 4) throw Invalid();
            return (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24);
        }
        private static void Bytes(byte[] bytes, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length - length) throw Invalid();
        }
        private static string Name(byte[] b, int offset, int length)
        {
            Bytes(b, offset, length);
            if (length == 0 || length > 180) throw Invalid();
            for (var i = offset; i < offset + length; i++) if (b[i] < 32 || b[i] > 126) throw Invalid();
            var name = Encoding.ASCII.GetString(b, offset, length);
            if (!UpdateTrust.OwnedPath(name)) throw Invalid();
            return name;
        }
        private static List<Entry> Directory(byte[] bytes, VerifiedUpdateDescriptor descriptor, CancellationToken cancellation)
        {
            // v1 accepts single-disk ZIP32 stored/deflated files, no ZIP comments/extras/preamble.
            // This narrow format is intentional; unsupported formats require reviewed publisher tooling.
            var end = bytes.Length - 22;
            if (end < 0 || U32(bytes, end) != 0x06054b50 || U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0
                || U16(bytes, end + 20) != 0) throw Invalid();
            var count = U16(bytes, end + 10);
            if (count == 0 || count > UpdateTrust.MaxFiles || U16(bytes, end + 8) != count || count != descriptor.Files.Count) throw Invalid();
            var size = U32(bytes, end + 12); var offset = U32(bytes, end + 16);
            if ((long)offset + size != end || offset > int.MaxValue) throw Invalid();
            var position = (int)offset; var expected = new Dictionary<string, UpdateFile>(StringComparer.Ordinal);
            foreach (var file in descriptor.Files) expected.Add(file.Path, file);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<Entry>(); long total = 0;
            for (var i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                Bytes(bytes, position, 46);
                if (U32(bytes, position) != 0x02014b50 || U16(bytes, position + 6) > 20 || U16(bytes, position + 34) != 0) throw Invalid();
                var flags = U16(bytes, position + 8); var method = U16(bytes, position + 10);
                if ((flags & ~0x080e) != 0 || (method != 0 && method != 8)) throw Invalid();
                var compressed = U32(bytes, position + 20); var expanded = U32(bytes, position + 24);
                var nameLength = U16(bytes, position + 28);
                if (U16(bytes, position + 30) != 0 || U16(bytes, position + 32) != 0) throw Invalid();
                var attributes = U32(bytes, position + 38); var mode = (attributes >> 16) & 0xf000;
                if ((mode != 0 && mode != 0x8000) || (attributes & 0x450) != 0) throw Invalid();
                var name = Name(bytes, position + 46, nameLength);
                UpdateFile file;
                if (!seen.Add(name) || !expected.TryGetValue(name, out file!) || expanded != file.Bytes) throw Invalid();
                if (expanded == 0 || expanded > UpdateTrust.MaxFileBytes || compressed > UpdateTrust.MaxZipBytes
                    || compressed == 0 || (long)expanded > (long)compressed * 100)
                    throw new UpdateValidationException(UpdateFailure.LimitExceeded);
                if (method == 0 && compressed != expanded) throw Invalid();
                total += expanded;
                if (total > UpdateTrust.MaxExpandedBytes) throw new UpdateValidationException(UpdateFailure.LimitExceeded);
                entries.Add(new Entry { Name = name, Flags = flags, Method = method, Compressed = compressed,
                    Expanded = expanded, Crc = U32(bytes, position + 16), Offset = U32(bytes, position + 42) });
                position += 46 + nameLength;
            }
            if (position != end) throw Invalid();
            position = 0;
            foreach (var entry in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (entry.Offset != position) throw Invalid(); // Reject overlap, hidden entries and SFX preambles.
                Bytes(bytes, position, 30);
                if (U32(bytes, position) != 0x04034b50 || U16(bytes, position + 4) > 20
                    || U16(bytes, position + 6) != entry.Flags || U16(bytes, position + 8) != entry.Method
                    || U16(bytes, position + 28) != 0) throw Invalid();
                var length = U16(bytes, position + 26);
                if (Name(bytes, position + 30, length) != entry.Name) throw Invalid();
                var crc = U32(bytes, position + 14); var compressed = U32(bytes, position + 18); var expanded = U32(bytes, position + 22);
                if ((entry.Flags & 8) == 0)
                {
                    if (crc != entry.Crc || compressed != entry.Compressed || expanded != entry.Expanded) throw Invalid();
                }
                else if (crc != 0 || compressed != 0 || expanded != 0) throw Invalid();
                var next = (long)position + 30 + length + entry.Compressed;
                if (next > offset || next > int.MaxValue) throw Invalid();
                position = (int)next;
                if ((entry.Flags & 8) != 0)
                {
                    // Require the explicit ZIP32 data-descriptor signature.
                    if (U32(bytes, position) != 0x08074b50 || U32(bytes, position + 4) != entry.Crc
                        || U32(bytes, position + 8) != entry.Compressed || U32(bytes, position + 12) != entry.Expanded) throw Invalid();
                    position += 16;
                }
            }
            if (position != offset) throw Invalid();
            return entries;
        }
        private static uint Crc(uint crc, byte[] buffer, int length)
        {
            for (var i = 0; i < length; i++)
            {
                crc ^= buffer[i];
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0);
            }
            return crc;
        }
        internal static VerifiedUpdatePackage Verify(byte[] bytes, VerifiedUpdateDescriptor descriptor,
            CancellationToken cancellation = default(CancellationToken))
        { return VerifyPayload(bytes, descriptor, cancellation).Package; }
        internal static VerifiedUpdatePayload VerifyPayload(byte[] bytes, VerifiedUpdateDescriptor descriptor,
            CancellationToken cancellation = default(CancellationToken))
        {
            cancellation.ThrowIfCancellationRequested();
            if (bytes == null || bytes.LongLength > UpdateTrust.MaxZipBytes) throw new UpdateValidationException(UpdateFailure.LimitExceeded);
            if (bytes.LongLength != descriptor.ZipBytes) throw Invalid();
            var snapshot = (byte[])bytes.Clone(); // Validate a private snapshot, not a mutable caller-owned buffer.
            var package = VerifySnapshot(snapshot, descriptor, cancellation);
            return new VerifiedUpdatePayload(PayloadStamp, snapshot, package);
        }
        private static VerifiedUpdatePackage VerifySnapshot(byte[] snapshot, VerifiedUpdateDescriptor descriptor, CancellationToken cancellation)
        {
            if (UpdateTrust.Hash(snapshot) != descriptor.ZipSha256) throw Invalid();
            cancellation.ThrowIfCancellationRequested();
            var headers = Directory(snapshot, descriptor, cancellation); var total = 0L;
            try
            {
                using (var stream = new MemoryStream(snapshot, false))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false, new UTF8Encoding(false, true)))
                {
                    if (zip.Entries.Count != headers.Count) throw Invalid();
                    var buffer = new byte[32768];
                    for (var i = 0; i < headers.Count; i++)
                    {
                        var entry = zip.Entries[i]; var header = headers[i];
                        if (entry.FullName != header.Name || entry.Length != header.Expanded || entry.CompressedLength != header.Compressed) throw Invalid();
                        var file = descriptor.Files[0];
                        foreach (var candidate in descriptor.Files) if (candidate.Path == header.Name) { file = candidate; break; }
                        using (var content = entry.Open())
                        using (var hash = SHA256.Create())
                        {
                            long read = 0; uint crc = 0xffffffff;
                            while (true)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                var length = content.Read(buffer, 0, buffer.Length);
                                if (length == 0) break;
                                read += length; total += length;
                                if (read > file.Bytes || read > UpdateTrust.MaxFileBytes || total > UpdateTrust.MaxExpandedBytes)
                                    throw new UpdateValidationException(UpdateFailure.LimitExceeded);
                                crc = Crc(crc, buffer, length); hash.TransformBlock(buffer, 0, length, buffer, 0);
                            }
                            hash.TransformFinalBlock(new byte[0], 0, 0);
                            if (read != file.Bytes || (crc ^ 0xffffffff) != header.Crc || UpdateTrust.Hex(hash.Hash!) != file.Sha256) throw Invalid();
                        }
                    }
                }
            }
            catch (InvalidDataException) { throw Invalid(); }
            catch (IOException) { throw Invalid(); }
            cancellation.ThrowIfCancellationRequested();
            return new VerifiedUpdatePackage(descriptor, headers.Count, total);
        }
    }
}
