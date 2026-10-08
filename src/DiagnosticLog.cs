using System.Diagnostics;
using System.Text;

namespace BetterAstralParty;

// Local, opt-in lifecycle diagnostics. Never subscribes to chat, network or Unity's global log.
internal sealed class DiagnosticLog
{
    internal const int MaxBytes = 1024 * 1024;
    private readonly string _directory;
    private readonly Action<string> _warning;
    private readonly Dictionary<string, string> _states = new();
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private bool _enabled;
    private long _bytes, _pendingBytes, _lastFlush;
    private string _header = "", _session = "";
    private readonly Queue<string> _combat = new();
    private int _combatBytes, _combatSequence, _combatStep = -1;
    private bool _combatTruncated, _combatArchived;
    private IntPtr _combatContext, _combatAttacker, _combatDefender;
    internal const int CombatMaxBytes = 64 * 1024;
    internal string CombatId { get; private set; } = "";
    internal bool IsRecording => _writer != null;
    private double _sampleStart = -1, _uiTotal, _uiMax, _gameMax, _popupMax, _statusMax, _frameMax;
    private long _sampleBytes, _frames, _slowFrames;
    private double _fieldTotal, _fieldMax;
    private double _handTotal, _handMax;
    private long _handBytes;
    private int _fieldPlates, _fieldEffects;

    internal DiagnosticLog(string directory, Action<string> warning)
    {
        _directory = directory;
        _warning = warning;
    }

    internal void SetEnabled(bool enabled, string header)
    {
        lock (_gate)
        {
            if (_enabled == enabled) return;
            if (!enabled) Write("diagnostics OFF");
            Close();
            _enabled = enabled;
            _states.Clear();
            EndCombat();
            ResetPerformance();
            if (!enabled) return;
            try
            {
                Directory.CreateDirectory(_directory);
                var current = Path.Combine(_directory, "current.log");
                if (File.Exists(current)) File.Copy(current, Path.Combine(_directory, "previous.log"), true);
                _header = header;
                _session = Guid.NewGuid().ToString("N")[..12];
                _combatSequence = 0;
                OpenCurrent();
                Write("diagnostics ON | " + header + "; session=" + _session);
                Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail();
            }
        }
    }

    internal void Write(string message)
    {
        lock (_gate)
        {
            if (_writer == null) return;
            try
            {
                // Fixed stages, numeric settings and mod-owned menu captions only, never game text.
                if (message.Length > 4096) message = message[..4085] + "[truncated]";
                var line = $"{DateTimeOffset.Now:O} | {message}{Environment.NewLine}";
                var bytes = Encoding.UTF8.GetByteCount(line);
                if (_bytes + bytes > MaxBytes)
                {
                    Close();
                    File.Copy(Path.Combine(_directory, "current.log"), Path.Combine(_directory, "previous.log"), true);
                    OpenCurrent();
                    var continuation = $"diagnostics CONTINUE | {_header}; session={_session}{Environment.NewLine}";
                    _writer!.Write(continuation);
                    _bytes = Encoding.UTF8.GetByteCount(continuation);
                    // Replay only fixed safe lifecycle settings; never local hand-derived states.
                    foreach (var key in new[] { "settings", "settingsPanel", "pvpSafety", "home", "field.gate", "hand.layout" })
                    {
                        if (!_states.TryGetValue(key, out var value)) continue;
                        var snapshot = "snapshot " + key + "=" + value + Environment.NewLine;
                        _writer.Write(snapshot); var snapshotBytes = Encoding.UTF8.GetByteCount(snapshot);
                        _bytes += snapshotBytes; _pendingBytes += snapshotBytes;
                    }
                }
                _writer!.Write(line);
                _bytes += bytes; _pendingBytes += bytes;
                if (_pendingBytes >= 16 * 1024 || Stopwatch.GetTimestamp() - _lastFlush >= Stopwatch.Frequency)
                    Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail();
            }
        }
    }

    internal void State(string key, string value)
    {
        lock (_gate)
        {
            if (value.Length > 3500) value = value[..3500] + "[truncated]";
            if (_writer == null || (_states.TryGetValue(key, out var previous) && previous == value)) return;
            _states[key] = value;
            if (_states.Count > 256) _states.Clear();
            var message = key + "=" + value;
            if (CombatId.Length > 0 && IsCombatState(key)) CombatEvent(message);
            else Write(message);
        }
    }

    internal void Error(string feature, Exception error)
    {
        lock (_gate)
        {
            if (_writer == null) return;
            // Omit Message/Data/InnerException and source paths, which can contain user data.
            State("error." + feature, error.GetType().FullName + "\n" + new StackTrace(error, false));
            Flush();
        }
    }

    // Called only with diagnostics ON; aggregate numbers, never emit per-frame lines.
    internal void Performance(double seconds, double uiMs, double gameMs, double popupMs,
        double statusMs, long managedBytes, double frameMs, double fieldMs = 0, int fieldPlates = 0, int fieldEffects = 0,
        double handMs = 0, long handBytes = 0)
    {
        lock (_gate)
        {
            if (_writer == null) return;
            if (_pendingBytes > 0 && Stopwatch.GetTimestamp() - _lastFlush >= Stopwatch.Frequency) Flush();
            if (_sampleStart < 0) _sampleStart = seconds;
            _frames++; _sampleBytes += managedBytes;
            _uiTotal += uiMs; _uiMax = Math.Max(_uiMax, uiMs);
            _gameMax = Math.Max(_gameMax, gameMs); _popupMax = Math.Max(_popupMax, popupMs);
            _statusMax = Math.Max(_statusMax, statusMs); _frameMax = Math.Max(_frameMax, frameMs);
            // Field Tick only: native per-frame placement/rendering occurs outside ModUi.Tick.
            _fieldTotal += fieldMs; _fieldMax = Math.Max(_fieldMax, fieldMs);
            _fieldPlates = Math.Max(_fieldPlates, fieldPlates); _fieldEffects = Math.Max(_fieldEffects, fieldEffects);
            _handTotal += handMs; _handMax = Math.Max(_handMax, handMs); _handBytes += handBytes;
            if (frameMs >= 50) _slowFrames++;
            if (seconds - _sampleStart < 10) return;
            Write(FormattableString.Invariant($"performance frames={_frames}; ui_avg_ms={_uiTotal / _frames:F3}; ui_max_ms={_uiMax:F3}; gameui_max_ms={_gameMax:F3}; popup_max_ms={_popupMax:F3}; status_max_ms={_statusMax:F3}; managed_bytes_per_frame={_sampleBytes / _frames}; game_frame_max_ms={_frameMax:F2}; game_frames_ge50ms={_slowFrames}"));
            Write(FormattableString.Invariant($"performance_field tick_avg_ms={_fieldTotal / _frames:F3}; tick_max_ms={_fieldMax:F3}; plates_max={_fieldPlates}; effects_max={_fieldEffects}; native_layout_excluded=True"));
            Write(FormattableString.Invariant($"performance_hand tick_avg_ms={_handTotal / _frames:F3}; tick_max_ms={_handMax:F3}; managed_bytes_per_frame={_handBytes / _frames}; native_layout_excluded=True"));
            ResetPerformance();
        }
    }

    private void ResetPerformance()
    {
        _sampleStart = -1;
        _uiTotal = _uiMax = _gameMax = _popupMax = _statusMax = _frameMax = 0;
        _sampleBytes = _frames = _slowFrames = 0;
        _fieldTotal = _fieldMax = 0;
        _handTotal = _handMax = 0; _handBytes = 0;
        _fieldPlates = _fieldEffects = 0;
    }

    private void Fail()
    {
        Close();
        _warning("[충돌 진단] 파일 기록에 실패했습니다. 게임은 계속 실행하며, 재시도는 OFF → ON으로 할 수 있습니다.");
    }

    private void OpenCurrent()
    {
        _writer = new StreamWriter(Path.Combine(_directory, "current.log"), false, new UTF8Encoding(false), 16 * 1024);
        _bytes = _pendingBytes = 0;
        _lastFlush = Stopwatch.GetTimestamp();
    }

    internal void Flush()
    {
        lock (_gate)
        {
            try { _writer?.Flush(); _pendingBytes = 0; _lastFlush = Stopwatch.GetTimestamp(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail(); }
        }
    }

    private static bool IsCombatState(string key) => key.StartsWith("combat", StringComparison.Ordinal)
        || key.StartsWith("damage", StringComparison.Ordinal) || key.StartsWith("koMinimum", StringComparison.Ordinal)
        || key.StartsWith("cardCounter", StringComparison.Ordinal);

    // Native pointers are identity tokens only; never serialize them or account identifiers.
    internal bool CombatPhase(IntPtr context, IntPtr attacker, IntPtr defender, int step, int map, int role)
    {
        lock (_gate)
        {
            if (!IsRecording) return false;
            var started = CombatId.Length == 0 || context != _combatContext || attacker != _combatAttacker
                || defender != _combatDefender || step < _combatStep;
            if (started)
            {
                EndCombat();
                CombatId = _session + "-" + ++_combatSequence;
                _combatContext = context; _combatAttacker = attacker; _combatDefender = defender;
                foreach (var key in _states.Keys.Where(IsCombatState).ToArray()) _states.Remove(key);
                CombatEvent($"begin map={map}; role={role}; partial={step > 3}");
            }
            var changed = step != _combatStep;
            if (changed) CombatEvent($"phase={step}");
            _combatStep = step;
            return started || changed;
        }
    }

    internal void EndCombat()
    {
        lock (_gate)
        {
            CombatId = ""; _combatStep = -1; _combat.Clear(); _combatBytes = 0;
            _combatTruncated = _combatArchived = false;
        }
    }

    internal void CombatEvent(string message)
    {
        lock (_gate)
        {
            if (!IsRecording || CombatId.Length == 0) return;
            if (message.Length > 3500) { message = message[..3500]; _combatTruncated = true; }
            var line = $"{DateTimeOffset.Now:O} | combat={CombatId}; {message}";
            var bytes = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            while (_combat.Count > 0 && (_combat.Count >= 128 || _combatBytes + bytes > CombatMaxBytes))
            {
                _combatBytes -= Encoding.UTF8.GetByteCount(_combat.Dequeue() + Environment.NewLine);
                _combatTruncated = true;
            }
            _combat.Enqueue(line); _combatBytes += bytes;
            Write($"combat={CombatId}; {message}");
        }
    }

    internal void CombatResult(int before, int predictedDamage, int observed, HitModifiers modifiers,
        bool monsterDefeatPresentation = false)
    {
        lock (_gate)
        {
            if (!IsRecording || CombatId.Length == 0) return;
            var expected = Math.Max(0, before - predictedDamage);
            var expectedDisplay = monsterDefeatPresentation ? Math.Max(1, expected) : expected;
            var mismatch = expectedDisplay != observed;
            CombatEvent($"damage.result before={before}; predictedDamage={predictedDamage}; expectedHP={expected}; "
                + $"observedHP={observed}; expectedDisplayHP={expectedDisplay}; monsterDefeatPresentation={monsterDefeatPresentation}; "
                + $"hpMismatch={mismatch}; overkillUnverifiable={observed == 0 || monsterDefeatPresentation && observed == 1}; modifiers={modifiers}");
            Flush();
            if (!mismatch || _combatArchived) return;
            try
            {
                var path = Path.Combine(_directory, "combat-mismatch.log");
                if (File.Exists(path)) File.Copy(path, Path.Combine(_directory, "combat-mismatch-previous.log"), true);
                using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
                writer.WriteLine($"{_header}; combat={CombatId}; truncated={_combatTruncated}; HP delta may include healing/revival; public data only");
                // Keep final state even when a long battle evicts early timeline entries.
                var snapshotBytes = 0;
                foreach (var state in _states.Where(pair => IsCombatState(pair.Key)))
                {
                    var line = "latest " + state.Key + "=" + state.Value;
                    snapshotBytes += Encoding.UTF8.GetByteCount(line + Environment.NewLine);
                    if (snapshotBytes > CombatMaxBytes) { writer.WriteLine("latest snapshot truncated"); break; }
                    writer.WriteLine(line);
                }
                foreach (var line in _combat) writer.WriteLine(line);
                _combatArchived = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Error("combatArchive", ex); }
        }
    }

    private void Close()
    {
        try { _writer?.Dispose(); }
        catch (IOException) { }
        finally { _writer = null; }
    }
}
