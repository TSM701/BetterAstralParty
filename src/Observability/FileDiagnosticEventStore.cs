#nullable enable
using System;
using System.IO;
using System.Text;

namespace BetterAstralParty.Observability
{
    // Windows x64 / local NTFS only. No fallback path, adoption, unlink, replace or rename.
    // Unknown evidence blocks the complete store before any event/marker write.
    internal sealed class FileDiagnosticEventStore : IDiagnosticEventStore
    {
        private readonly string _directory;
        private readonly string? _privateParent;
        private DiagnosticFileFence? _fence;
        private DiagnosticFileFence.FileLease? _ownership, _current, _previous, _state;
        private StreamWriter? _writer;
        private bool _prepared, _refused, _fresh;
        private string _relative = "";
        public long Bytes { get; private set; }
        internal FileDiagnosticEventStore(string directory,string? privateParent=null) {
            _directory=Path.GetFullPath(directory);_privateParent=privateParent==null?null:Path.GetFullPath(privateParent);
            if(_privateParent!=null && _directory!=_privateParent && Path.GetDirectoryName(_directory)!=_privateParent)throw new IOException("Diagnostic private parent invalid");
        }
        private static void RequirePrivateDirectory(DiagnosticFileFence fence,string relative) {
            try {fence.AssertPrivateDirectory(relative);}
            catch(DiagnosticStorageException error)when(error.NativeError==2||error.NativeError==3) {
                try {fence.CreatePrivateDirectory(relative);}
                catch(DiagnosticStorageException race)when(race.NativeError==80||race.NativeError==183){fence.AssertPrivateDirectory(relative);}
            }
        }
        private string Name(string leaf) => _relative + "/" + leaf;
        private DiagnosticFileFence.FileLease? Optional(string leaf)
        {
            try { return _fence!.OpenFile(Name(leaf), true); }
            catch (DiagnosticStorageException error) when (error.NativeError == 2) { return null; }
        }
        private void AcquireOwnership()
        {
            if (_ownership != null) return;
            if (_refused) throw new IOException("Diagnostic store refused");
            // Only missing ancestors may be created. Existing ACLs are never repaired.
            var parent = Path.GetDirectoryName(_privateParent ?? _directory)!;
            while (!Directory.Exists(parent)) parent = Path.GetDirectoryName(parent) ?? throw new IOException("Diagnostic parent unavailable");
            var fence = new DiagnosticFileFence(parent);
            DiagnosticFileFence.FileLease? ownership = null;
            try {
                var relative = _directory.Substring(parent.TrimEnd('\\').Length + 1).Replace('\\', '/');
                var last = relative.LastIndexOf('/');
                if(_privateParent!=null) {
                    var shared=_privateParent.Substring(parent.TrimEnd('\\').Length+1).Replace('\\','/');
                    var split=shared.LastIndexOf('/');if(split>=0)fence.EnsureDirectory(shared.Substring(0,split));
                    RequirePrivateDirectory(fence,shared);
                } else if(last>=0)fence.EnsureDirectory(relative.Substring(0,last));
                RequirePrivateDirectory(fence,relative);
                try { ownership = fence.OpenFile(relative + "/writer.lock", true); }
                catch (DiagnosticStorageException error) when (error.NativeError == 2) { ownership = fence.OpenFile(relative + "/writer.lock", true, true); }
                if (ownership.Stream.Length != 0) throw new IOException("Unknown ownership evidence retained");
                ownership.Recheck();
                _relative = relative; _fence = fence; _ownership = ownership; // Transfer only after every ownership check.
            } catch { ownership?.Dispose(); fence.Dispose(); _refused = true; throw; }
        }
        private static byte[] Read(DiagnosticFileFence.FileLease file, int maximum)
        {
            file.Recheck(); if (file.Stream.Length > maximum) throw new IOException("Unknown oversized evidence retained");
            file.Stream.Position = 0; var data = new byte[(int)file.Stream.Length]; var at = 0;
            while (at < data.Length) { var n = file.Stream.Read(data, at, data.Length - at); if (n == 0) throw new IOException("Diagnostic evidence changed"); at += n; }
            file.Recheck(); return data;
        }
        private static string EvidenceSession(DiagnosticFileFence.FileLease file, bool mayBeEmpty)
        {
            var data = Read(file, 1024 * 1024);
            if (data.Length == 0) { if (mayBeEmpty) return ""; throw new IOException("Unknown empty event evidence retained"); }
            // Current schema writes ASCII fields and complete LF/CRLF records. Do not adopt partial tails.
            foreach (var b in data) if (b > 127) throw new IOException("Unknown event encoding retained");
            var text = Encoding.ASCII.GetString(data);
            if (!text.EndsWith("\n", StringComparison.Ordinal)) throw new IOException("Partial event evidence retained");
            string session = ""; long sequence = 0; var first = true;
            using (var reader = new StringReader(text)) {
                string? line;
                while ((line = reader.ReadLine()) != null) {
                    if (!OwnedDiagnosticRecord.Inspect(line, out var recordSession, out var recordSequence, out var kind)
                        || recordSequence <= sequence || !first && recordSession != session
                        || first && kind != "SessionStarted" && kind != "Rotation") throw new IOException("Unknown event evidence retained");
                    session = recordSession; sequence = recordSequence; first = false;
                }
            }
            return session;
        }
        public PreviousDiagnosticSession Previous()
        {
            if (_prepared || _refused) throw new IOException("Diagnostic preparation already resolved");
            try {
                AcquireOwnership(); // Refusal is fatal, outside a marker-read fallback.
                using (var orphan = Optional("session-write.tmp")) if (orphan != null) throw new IOException("Unknown temporary evidence retained");
                _current = Optional("events-current.jsonl"); _previous = Optional("events-previous.jsonl"); _state = Optional("session.state");
                _fresh = _current == null && _previous == null && _state == null;
                if (_fresh) { _prepared = true; return new PreviousDiagnosticSession(DiagnosticOutcome.NotObserved, DiagnosticCode.None); }
                if (_current == null || _previous == null || _state == null) throw new IOException("Incomplete ownership evidence retained");
                var bytes = Read(_state, 1024);
                foreach (var b in bytes) if (b > 127) throw new IOException("Unknown marker encoding retained");
                var text = Encoding.ASCII.GetString(bytes).Replace("\r\n", "\n");
                if (!text.EndsWith("\n", StringComparison.Ordinal)) throw new IOException("Unknown marker retained");
                var lines = text.Substring(0, text.Length - 1).Split('\n');
                if (lines.Length != 5 || lines[0] != "BAP-MinimalSession/v1" || !Guid.TryParseExact(lines[1], "N", out _)
                    || (lines[2] != "active" && lines[2] != "ended") || !Enum.TryParse(lines[3], out DiagnosticEndReason reason)
                    || !Enum.IsDefined(typeof(DiagnosticEndReason), reason) || lines[4] != "end") throw new IOException("Unknown marker retained");
                if (EvidenceSession(_current, false) != lines[1]) throw new IOException("Unrelated event evidence retained");
                EvidenceSession(_previous, true);
                _prepared = true;
                return new PreviousDiagnosticSession(lines[2] == "ended" ? reason == DiagnosticEndReason.ProcessExitCallback ? DiagnosticOutcome.EndCallbackObserved : DiagnosticOutcome.Completed : DiagnosticOutcome.IncompleteUnknown,
                    lines[2] == "ended" ? DiagnosticCode.None : DiagnosticCode.InterruptedUnknown);
            } catch { _refused = true; throw; }
        }
        public void Open()
        {
            if (!_prepared || _refused || _ownership == null || _writer != null) throw new IOException("Diagnostic ownership required");
            _ownership.Recheck(); _fence!.Recheck();
            if (_fresh) {
                _current = _fence.OpenFile(Name("events-current.jsonl"), true, true);
                _previous = _fence.OpenFile(Name("events-previous.jsonl"), true, true);
                _state = _fence.OpenFile(Name("session.state"), true, true);
            } else CopyToPrevious();
            ResetCurrent();
        }
        private void CopyToPrevious()
        {
            _current!.Recheck(); _previous!.Recheck();
            var data = Read(_current, 1024 * 1024);
            // Both handles remain open, deny write/delete sharing and have one link. No path-based copy/replace.
            _previous.Recheck(); _previous.Stream.Position = 0; _previous.Stream.SetLength(0);
            _previous.Stream.Write(data, 0, data.Length); _previous.Flush();
        }
        private void ResetCurrent()
        {
            _current!.Recheck(); _current.Stream.Position = 0; _current.Stream.SetLength(0);
            _writer = new StreamWriter(_current.Stream, new UTF8Encoding(false), 4096, true); Bytes = 0;
        }
        public void Append(string line)
        {
            _current!.Recheck(); _writer!.WriteLine(line); Bytes += Encoding.UTF8.GetByteCount(line + Environment.NewLine);
        }
        public void Flush() { _current!.Recheck(); _writer!.Flush(); _current.Flush(); }
        public void Rotate() { Flush(); _writer!.Dispose(); _writer = null; CopyToPrevious(); ResetCurrent(); }
        public void Mark(DiagnosticIdentity identity, bool ended, DiagnosticEndReason reason)
        {
            if (!_prepared || _refused || _state == null) throw new IOException("Diagnostic marker ownership required");
            var bytes = Encoding.ASCII.GetBytes("BAP-MinimalSession/v1\n" + identity.Session + "\n" + (ended ? "ended" : "active") + "\n" + SafeEnum.Value(reason) + "\nend\n");
            _state.Recheck(); _state.Stream.Position = 0; _state.Stream.Write(bytes, 0, bytes.Length); _state.Stream.SetLength(bytes.Length); _state.Flush();
        }
        public void Dispose()
        {
            try { _writer?.Dispose(); }
            finally {
                _writer = null;
                try { _current?.Dispose(); } finally {
                    try { _previous?.Dispose(); } finally {
                        try { _state?.Dispose(); } finally {
                            try { _ownership?.Dispose(); } finally { _fence?.Dispose(); _fence = null; _ownership = null; }
                        }
                    }
                }
            }
        }
    }
}
