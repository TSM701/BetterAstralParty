#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading;
namespace BetterAstralParty.Updating
{
    // Cancel-only IPC. Never grants permission to install. Pre-existing/missing events fail closed.
    internal sealed class UpdateCancelSignal : IDisposable
    {
        private readonly EventWaitHandle _event, _failure;
        private readonly object _gate = new object();
        private bool _disposed;
        private static string Name(string hash) {
            if (!Regex.IsMatch(hash, @"\A[0-9A-F]{64}\z")) throw new ApplySafetyException(ApplyFailure.InvalidState);
            return @"Local\BetterAstralParty.Cancel." + hash;
        }
#pragma warning disable CA1416
        private UpdateCancelSignal(EventWaitHandle value, EventWaitHandle failure) { _event = value; _failure = failure; }
        internal static UpdateCancelSignal Create(string hash) {
            bool created; var value = new EventWaitHandle(false, EventResetMode.ManualReset, Name(hash), out created);
            if (!created) { value.Dispose(); throw new ApplySafetyException(ApplyFailure.InvalidState); }
            try {
                bool failureCreated; var failure = new EventWaitHandle(false, EventResetMode.ManualReset, Name(hash) + ".Failure", out failureCreated);
                if (!failureCreated) { failure.Dispose(); throw new ApplySafetyException(ApplyFailure.InvalidState); }
                return new UpdateCancelSignal(value, failure);
            } catch { value.Dispose(); throw; }
        }
        internal static UpdateCancelSignal Open(string hash) {
            try { var value = EventWaitHandle.OpenExisting(Name(hash)); try { return new UpdateCancelSignal(value, EventWaitHandle.OpenExisting(Name(hash) + ".Failure")); } catch { value.Dispose(); throw; } }
            catch (WaitHandleCannotBeOpenedException) { throw new ApplySafetyException(ApplyFailure.InvalidState); }
        }
        internal bool Cancelled { get { lock (_gate) { if (_disposed) return true; return _event.WaitOne(0); } } }
        internal bool Failure { get { lock (_gate) { return !_disposed && _failure.WaitOne(0); } } }
        internal void SetFailure() { lock (_gate) { if (!_disposed) { if (!_failure.Set() || !_event.Set()) throw new ApplySafetyException(ApplyFailure.InvalidState); } } }
        internal void Set() { lock (_gate) { if (!_disposed && !_event.Set()) throw new ApplySafetyException(ApplyFailure.InvalidState); } }
        public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _event.Dispose(); _failure.Dispose(); } }
#pragma warning restore CA1416
    }
}
