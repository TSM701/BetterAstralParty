#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BetterAstralParty.Observability
{
    internal sealed class DiagnosticOptions
    {
        internal int QueueCapacity = 256, FileBytes = 256 * 1024, FlushMilliseconds = 1000, SampleMilliseconds = 10000;
        internal void Validate()
        {
            if (QueueCapacity < 8 || QueueCapacity > 1024 || FileBytes < 64 * 1024 || FileBytes > 1024 * 1024 || FlushMilliseconds < 50 || FlushMilliseconds > 2000 || SampleMilliseconds < 100 || SampleMilliseconds > 60000) throw new ArgumentOutOfRangeException(nameof(DiagnosticOptions));
        }
    }
    internal readonly struct PreviousDiagnosticSession
    {
        internal readonly DiagnosticOutcome Outcome;
        internal readonly DiagnosticCode Code;
        internal PreviousDiagnosticSession(DiagnosticOutcome outcome, DiagnosticCode code) { Outcome = outcome; Code = code; }
    }
    internal interface IDiagnosticEventStore : IDisposable
    {
        long Bytes { get; }
        PreviousDiagnosticSession Previous();
        void Open();
        void Append(string line);
        void Flush();
        void Rotate();
        void Mark(DiagnosticIdentity identity, bool ended, DiagnosticEndReason reason);
    }
    internal sealed class MinimalEventLog : IDisposable
    {
        private readonly IDiagnosticEventStore _store;
        private readonly DiagnosticIdentity _identity;
        private readonly DiagnosticOptions _options;
        private readonly Action? _legacyFlush;
        private readonly Queue<SafeDiagnosticEvent> _priority = new Queue<SafeDiagnosticEvent>(), _routine = new Queue<SafeDiagnosticEvent>();
        private readonly object _gate = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEvent _done = new ManualResetEvent(false);
        private readonly long[] _hooks = new long[3], _hookLast = new long[3];
        private readonly int[] _hookSeen = new int[3];
        private readonly int[] _registered = { -1, -1, -1 };
        private readonly DiagnosticCode[] _gates = new DiagnosticCode[12];
        private readonly bool[] _gateSet = new bool[12];
        private readonly DiagnosticUpdateState[] _status = new DiagnosticUpdateState[12];
        private readonly string[] _targets = new string[12];
        private readonly string?[] _statusOperations = new string?[12];
        private readonly bool[] _statusSet = new bool[12];
        private readonly SafeDiagnosticEvent?[] _failures = new SafeDiagnosticEvent?[12];
        private readonly SafeDiagnosticEvent?[] _stages = new SafeDiagnosticEvent?[12];
        private readonly string?[] _failureKeys = new string?[12];
        private readonly long[] _failureCounts = new long[12], _failurePublished = new long[12];
        private readonly long _started = Stopwatch.GetTimestamp();
        private long _sequence, _dropped, _reportedDropped, _mainFrames, _lastMainFrames, _lastSample, _flushRequests, _flushCompleted;
        private int _closing, _failed, _detail, _recording, _coverage = -1;
        private DiagnosticSettings? _settings;
        private string _settingsKey = "";
        private DiagnosticEndReason _endReason;
        internal bool Recording => Volatile.Read(ref _recording) == 1;
        internal bool Detailed => Volatile.Read(ref _detail) == 1;
        internal bool StorageFailed => Volatile.Read(ref _failed) == 1;
        internal long Dropped => Interlocked.Read(ref _dropped);
        internal string ProcessSession => _identity.Session;
        internal string ModVersion => _identity.Version;
        internal string BuildId => _identity.Build;
        internal MinimalEventLog(IDiagnosticEventStore store, DiagnosticIdentity identity, DiagnosticOptions? options = null, Action? legacyFlush = null)
        {
            _store = store; _identity = identity; _options = options ?? new DiagnosticOptions(); _options.Validate(); _legacyFlush = legacyFlush;
            var thread = new Thread(Pump) { IsBackground = true, Name = "BAP minimal diagnostics" };
            try { thread.Start(); } catch { Volatile.Write(ref _failed, 1); _done.Set(); }
        }
        internal static MinimalEventLog Start(string directory, string version, string build, Action? legacyFlush = null) => new MinimalEventLog(new FileDiagnosticEventStore(directory), new DiagnosticIdentity(version, build, Environment.Version.ToString()), legacyFlush: legacyFlush);
        private SafeDiagnosticEvent Event(DiagnosticKind kind, DiagnosticFeature feature = DiagnosticFeature.Loader, DiagnosticPhase phase = DiagnosticPhase.None, DiagnosticOutcome outcome = DiagnosticOutcome.Observed, DiagnosticCode code = DiagnosticCode.None, bool priority = false)
            => new SafeDiagnosticEvent { Kind = kind, Feature = SafeEnum.Value(feature), Phase = SafeEnum.Value(phase), Outcome = SafeEnum.Value(outcome), Code = SafeEnum.Value(code), EnqueuedStamp = Stopwatch.GetTimestamp(), Utc = DateTimeOffset.UtcNow, Priority = priority };
        private bool Enqueue(SafeDiagnosticEvent value)
        {
            lock (_gate) {
                if (Volatile.Read(ref _closing) != 0 || StorageFailed) return false;
                if (_priority.Count + _routine.Count >= _options.QueueCapacity) {
                    if (value.Priority && _routine.Count > 0) { _routine.Dequeue(); Interlocked.Increment(ref _dropped); }
                    else { Interlocked.Increment(ref _dropped); return false; }
                }
                (value.Priority ? _priority : _routine).Enqueue(value);
            }
            _wake.Set(); return true;
        }
        internal bool Stage(DiagnosticFeature feature, DiagnosticPhase phase, DiagnosticOutcome outcome, DiagnosticOperation operation = default, string? targetVersion = null, DiagnosticCode code = DiagnosticCode.None)
        { var e = Event(DiagnosticKind.Stage, feature, phase, outcome, SafeEnum.Value(code), priority: true); e.Operation = operation; e.TargetVersion = DiagnosticIdentity.SafeVersion(targetVersion); lock (_gate) _stages[(int)e.Feature] = e; return Enqueue(e); }
        internal bool UiAction(DiagnosticUiAction action, DiagnosticOutcome outcome, DiagnosticOperation operation)
        { var e = Event(DiagnosticKind.Stage, DiagnosticFeature.Settings, DiagnosticPhase.UiAction, outcome, priority: true); e.UiAction = SafeEnum.Value(action); e.Operation = operation; lock (_gate) _stages[(int)e.Feature] = e; return Enqueue(e); }
        internal void RelateOperation(DiagnosticOperation parent, DiagnosticOperation child)
        { var e = Event(DiagnosticKind.OperationRelation, DiagnosticFeature.Coordinator, priority: true); e.Operation = child; e.RelatedOperation = parent; Enqueue(e); }
        internal bool Failure(DiagnosticFeature feature, DiagnosticPhase phase, DiagnosticCode code, Exception? exception = null, int nativeCode = 0, int validationCode = 0, DiagnosticOperation operation = default, int applyCode = 0, string? targetVersion = null)
        {
            feature = SafeEnum.Value(feature); var index = (int)feature;
            var e = Event(DiagnosticKind.Failure, feature, phase, DiagnosticOutcome.Failed, code, true); e.Error = SafeEnum.Error(exception); e.NativeCode = nativeCode; e.ValidationCode = Math.Max(0, Math.Min(65535, validationCode)); e.ApplyCode = Math.Max(0, Math.Min(65535, applyCode)); e.Operation = operation; e.TargetVersion = DiagnosticIdentity.SafeVersion(targetVersion); e.Count = 1;
            var key = (int)e.Phase + ":" + (int)e.Code + ":" + (int)e.Error + ":" + e.NativeCode + ":" + e.ValidationCode + ":" + e.ApplyCode + ":" + operation.Value + ":" + e.TargetVersion;
            lock (_gate) {
                if (_closing != 0 || StorageFailed) return false;
                if (_failureKeys[index] == key) { _failureCounts[index]++; return true; }
                e.Site = SafeDiagnosticSite.From(exception);
                if (_failures[index] != null && _failureCounts[index] > _failurePublished[index]) Enqueue(FailureSummary(index));
                _failureKeys[index] = key; _failures[index] = e; _failureCounts[index] = 1; _failurePublished[index] = 1;
            }
            return Enqueue(e);
        }
        internal void HookRegistered(DiagnosticHook hook, bool success)
        { var h = SafeEnum.Value(hook); Interlocked.Exchange(ref _registered[(int)h], success ? 1 : 0); var e = Event(DiagnosticKind.HookRegistered, h == DiagnosticHook.CoreUi ? DiagnosticFeature.CoreUi : DiagnosticFeature.Notices, DiagnosticPhase.PatchRegistration, success ? DiagnosticOutcome.Completed : DiagnosticOutcome.Failed, success ? DiagnosticCode.None : DiagnosticCode.CompatibilityBlocked, true); e.Count = (int)h; Enqueue(e); }
        internal void ObserveHook(DiagnosticHook hook)
        {
            if (Volatile.Read(ref _closing) != 0 || StorageFailed) return;
            var index = (int)hook; if ((uint)index >= (uint)_hooks.Length) return; Interlocked.Increment(ref _hooks[index]);
            if (index == 0) Interlocked.Increment(ref _mainFrames);
            if (Interlocked.CompareExchange(ref _hookSeen[index], 1, 0) == 0) { var e = Event(DiagnosticKind.HookObserved, index == 0 ? DiagnosticFeature.CoreUi : DiagnosticFeature.Notices, outcome: DiagnosticOutcome.Observed, priority: true); e.Count = index; Enqueue(e); }
        }
        internal void Gate(DiagnosticFeature feature, DiagnosticCode code)
        {
            var index = (int)feature; if ((uint)index >= (uint)_gates.Length) return;
            lock (_gate) { if (_gateSet[index] && _gates[index] == code) return; code = SafeEnum.Value(code); if (_gateSet[index] && _gates[index] == code) return; _gateSet[index] = true; _gates[index] = code; }
            Enqueue(Event(DiagnosticKind.Gate, feature, code: code));
        }
        internal void Settings(DiagnosticSettings value)
        {
            lock (_gate) { if (_settingsKey == value.Fingerprint) return; _settingsKey = value.Fingerprint; _settings = value; }
            var e = Event(DiagnosticKind.Settings, DiagnosticFeature.Settings); e.Settings = value; Enqueue(e);
        }
        internal void SetDetailed(bool enabled)
        { if (Interlocked.Exchange(ref _detail, enabled ? 1 : 0) == (enabled ? 1 : 0)) return; if (enabled) Interlocked.Exchange(ref _coverage, -1); var e = Event(DiagnosticKind.DetailChanged, DiagnosticFeature.Settings); e.Count = enabled ? 1 : 0; Enqueue(e); }
        internal void CardCoverage(int supported, int unsupported)
        {
            if (!Detailed) return; supported = Math.Max(0, Math.Min(64, supported)); unsupported = Math.Max(0, Math.Min(64, unsupported));
            var key = supported * 128 + unsupported; if (Interlocked.Exchange(ref _coverage, key) == key) return;
            var e = Event(DiagnosticKind.CardCoverage, DiagnosticFeature.PublicCombat); e.Count = supported; e.SecondaryCount = unsupported; Enqueue(e);
        }
        internal void UpdateStatus(DiagnosticFeature component, DiagnosticUpdateState state, string? targetVersion = null, DiagnosticOperation operation = default, bool authoritative = false)
        {
            component = SafeEnum.Value(component); state = SafeEnum.Value(state); var index = (int)component; var target = DiagnosticIdentity.SafeVersion(targetVersion);
            lock (_gate) { if (component == DiagnosticFeature.Coordinator && _statusSet[index] && _statusOperations[index] != null && operation.Value == null && !authoritative) return; if (_statusSet[index] && _status[index] == state && _targets[index] == target && (authoritative ? _statusOperations[index] == operation.Value : (operation.Value == null || _statusOperations[index] == operation.Value))) return; _statusSet[index] = true; _status[index] = state; _targets[index] = target; _statusOperations[index] = operation.Value; }
            var e = Event(DiagnosticKind.UpdateStatus, component); e.UpdateState = state; e.TargetVersion = target; e.Operation = operation; Enqueue(e);
        }
        internal void RequestFlush() { if (Volatile.Read(ref _closing) != 0 || StorageFailed) return; Interlocked.Increment(ref _flushRequests); _wake.Set(); }
        // Optional low-frequency boundary barrier. Never call once per frame. No caller-thread disk I/O.
        internal bool FlushBoundary(int maximumMilliseconds = 30)
        {
            if (maximumMilliseconds < 0 || maximumMilliseconds > 50) throw new ArgumentOutOfRangeException(nameof(maximumMilliseconds));
            if (StorageFailed || Volatile.Read(ref _closing) != 0) return false;
            var wanted = Interlocked.Increment(ref _flushRequests); _wake.Set(); var start = Stopwatch.StartNew();
            while (Interlocked.Read(ref _flushCompleted) < wanted && !StorageFailed && !_done.WaitOne(0)) { if (start.ElapsedMilliseconds >= maximumMilliseconds) return false; Thread.Sleep(1); }
            return !StorageFailed && Interlocked.Read(ref _flushCompleted) >= wanted;
        }
        internal bool Stop(DiagnosticEndReason reason, int maximumMilliseconds = 2000)
        {
            if (maximumMilliseconds < 0 || maximumMilliseconds > 2000) throw new ArgumentOutOfRangeException(nameof(maximumMilliseconds));
            lock (_gate) { if (Interlocked.Exchange(ref _closing, 1) == 0) _endReason = SafeEnum.Value(reason); }
            _wake.Set(); return _done.WaitOne(maximumMilliseconds) && !StorageFailed;
        }
        public void Dispose() { Stop(DiagnosticEndReason.OwnerDisposed); }
        private SafeDiagnosticEvent[] Snapshot()
        {
            var items = new List<SafeDiagnosticEvent>();
            lock (_gate) {
                var detail = Event(DiagnosticKind.DetailChanged, DiagnosticFeature.Settings); detail.Count = Detailed ? 1 : 0; items.Add(detail);
                for (var i = 0; i < _registered.Length; i++) { var registered = Volatile.Read(ref _registered[i]); if (registered < 0) continue; var e = Event(DiagnosticKind.HookRegistered, i == 0 ? DiagnosticFeature.CoreUi : DiagnosticFeature.Notices, DiagnosticPhase.PatchRegistration, registered == 1 ? DiagnosticOutcome.Completed : DiagnosticOutcome.Failed, registered == 1 ? DiagnosticCode.None : DiagnosticCode.CompatibilityBlocked); e.Count = i; items.Add(e); }
                for (var i = 0; i < _gates.Length; i++) if (_gateSet[i]) items.Add(Event(DiagnosticKind.Gate, (DiagnosticFeature)i, code: _gates[i]));
                if (_settings.HasValue) { var e = Event(DiagnosticKind.Settings, DiagnosticFeature.Settings); e.Settings = _settings; items.Add(e); }
                for (var i = 0; i < _status.Length; i++) if (_statusSet[i]) { var e = Event(DiagnosticKind.UpdateStatus, (DiagnosticFeature)i); e.UpdateState = _status[i]; e.TargetVersion = _targets[i]; e.Operation = DiagnosticOperation.VerifiedGuid(_statusOperations[i]); items.Add(e); }
                for (var i = 0; i < _stages.Length; i++) if (_stages[i] is SafeDiagnosticEvent stage) {
                    var e = Event(DiagnosticKind.StageSnapshot, stage.Feature, stage.Phase, stage.Outcome, stage.Code); e.Utc = stage.Utc; e.EnqueuedStamp = stage.EnqueuedStamp; e.Operation = stage.Operation; e.TargetVersion = stage.TargetVersion; e.UiAction = stage.UiAction; items.Add(e);
                }
                for (var i = 0; i < _failures.Length; i++) if (_failures[i] is SafeDiagnosticEvent failure) {
                    var e = Event(DiagnosticKind.FailureSnapshot, failure.Feature, failure.Phase, failure.Outcome, failure.Code); e.Utc = failure.Utc; e.EnqueuedStamp = failure.EnqueuedStamp; e.Error = failure.Error; e.Site = failure.Site; e.Operation = failure.Operation; e.TargetVersion = failure.TargetVersion; e.NativeCode = failure.NativeCode; e.ValidationCode = failure.ValidationCode; e.ApplyCode = failure.ApplyCode; e.Count = _failureCounts[i]; items.Add(e);
                }
            }
            return items.ToArray();
        }
        private void Raw(SafeDiagnosticEvent item)
        {
            var json = SafeDiagnosticJson.Encode(item, _identity, ++_sequence, _started);
            if (Encoding.UTF8.GetByteCount(json) > 2048) throw new InvalidDataException("Controlled event exceeds budget");
            _store.Append(json);
        }
        private void Write(SafeDiagnosticEvent item)
        {
            var estimate = SafeDiagnosticJson.Encode(item, _identity, _sequence + 1, _started);
            if (_store.Bytes + Encoding.UTF8.GetByteCount(estimate + Environment.NewLine) > _options.FileBytes) {
                _store.Rotate(); Raw(Event(DiagnosticKind.Rotation)); foreach (var snapshot in Snapshot()) Raw(snapshot);
                if (_store.Bytes + Encoding.UTF8.GetByteCount(estimate + Environment.NewLine) > _options.FileBytes) throw new InvalidDataException("Snapshot exceeds file budget");
            }
            Raw(item);
        }
        private void Flush(long completedRequests)
        {
            _store.Flush();
            try { _legacyFlush?.Invoke(); } catch { /* Legacy sink failure never stops the new safe sink. */ }
            Interlocked.Exchange(ref _flushCompleted, completedRequests);
        }
        private void Samples()
        {
            var now = Stopwatch.GetTimestamp(); if ((now - _lastSample) * 1000d / Stopwatch.Frequency < _options.SampleMilliseconds) return; _lastSample = now;
            var frames = Interlocked.Read(ref _mainFrames); var progress = Event(DiagnosticKind.ProgressSample, DiagnosticFeature.CoreUi, outcome: frames == 0 ? DiagnosticOutcome.NotObserved : frames == _lastMainFrames ? DiagnosticOutcome.IncompleteUnknown : DiagnosticOutcome.Observed, code: frames > 0 && frames == _lastMainFrames ? DiagnosticCode.InterruptedUnknown : DiagnosticCode.None);
            progress.Count = frames - _lastMainFrames; _lastMainFrames = frames; Write(progress);
            for (var i = 0; i < _hooks.Length; i++) { var count = Interlocked.Read(ref _hooks[i]); if (count == _hookLast[i]) continue; var e = Event(DiagnosticKind.HookSample, i == 0 ? DiagnosticFeature.CoreUi : DiagnosticFeature.Notices); e.Count = i; e.SecondaryCount = count - _hookLast[i]; _hookLast[i] = count; Write(e); }
            FailureSamples();
        }
        private void FailureSamples()
        {
            var summaries = new List<SafeDiagnosticEvent>();
            lock (_gate) for (var i = 0; i < _failures.Length; i++) {
                var old = _failures[i]; if (old == null || _failureCounts[i] <= _failurePublished[i]) continue;
                summaries.Add(FailureSummary(i));
            }
            foreach (var summary in summaries) Write(summary);
        }
        private SafeDiagnosticEvent FailureSummary(int i)
        {
            var old = _failures[i]!;
            var e = Event(DiagnosticKind.Failure, old.Feature, old.Phase, old.Outcome, old.Code, true); e.Error = old.Error; e.Site = old.Site; e.NativeCode = old.NativeCode; e.ValidationCode = old.ValidationCode; e.ApplyCode = old.ApplyCode; e.Operation = old.Operation; e.TargetVersion = old.TargetVersion;
            e.Count = _failureCounts[i]; e.SecondaryCount = _failureCounts[i] - _failurePublished[i]; _failurePublished[i] = _failureCounts[i]; return e;
        }
        private void Pump()
        {
            try {
                var prior = _store.Previous(); _store.Open(); _store.Mark(_identity, false, DiagnosticEndReason.Unknown); Volatile.Write(ref _recording, 1);
                Raw(Event(DiagnosticKind.SessionStarted, outcome: DiagnosticOutcome.Active));
                Raw(Event(DiagnosticKind.PreviousSession, outcome: prior.Outcome, code: prior.Code)); Flush(0); _lastSample = Stopwatch.GetTimestamp(); var lastFlush = Stopwatch.GetTimestamp();
                while (true) {
                    var batch = new List<SafeDiagnosticEvent>(_options.QueueCapacity);
                    long flushRequests;
                    lock (_gate) { while (_priority.Count > 0) batch.Add(_priority.Dequeue()); while (_routine.Count > 0) batch.Add(_routine.Dequeue()); flushRequests = Interlocked.Read(ref _flushRequests); }
                    var critical = false; foreach (var item in batch) { Write(item); critical |= item.Priority; }
                    var dropped = Dropped; if (dropped != _reportedDropped) { var h = Event(DiagnosticKind.WriterHealth, code: DiagnosticCode.QueueCapped); h.Count = dropped; Write(h); _reportedDropped = dropped; }
                    Samples();
                    if (Volatile.Read(ref _closing) != 0) {
                        // Drain the final accepted queue before ending. No future producer is accepted.
                        lock (_gate) { if (_priority.Count + _routine.Count > 0) continue; }
                        FailureSamples(); var ended = Event(DiagnosticKind.SessionEnded, outcome: _endReason == DiagnosticEndReason.ProcessExitCallback ? DiagnosticOutcome.EndCallbackObserved : DiagnosticOutcome.Completed); ended.Count = (int)_endReason; Write(ended); Flush(flushRequests); _store.Mark(_identity, true, _endReason); break;
                    }
                    var elapsed = (Stopwatch.GetTimestamp() - lastFlush) * 1000d / Stopwatch.Frequency;
                    if (critical || elapsed >= _options.FlushMilliseconds || flushRequests > Interlocked.Read(ref _flushCompleted)) { Flush(flushRequests); lastFlush = Stopwatch.GetTimestamp(); }
                    _wake.WaitOne(_options.FlushMilliseconds);
                }
            } catch { Volatile.Write(ref _failed, 1); }
            finally { Volatile.Write(ref _recording, 0); try { _store.Dispose(); } catch { Volatile.Write(ref _failed, 1); } _done.Set(); }
        }
    }
}
