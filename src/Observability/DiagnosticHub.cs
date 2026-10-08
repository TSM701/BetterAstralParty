#nullable enable
using System;
using System.Threading;

namespace BetterAstralParty.Observability
{
    // Missing/failed diagnostics never alter product decisions; the process owner binds one sink.
    internal static class DiagnosticHub
    {
        private static MinimalEventLog? _sink;
        internal static MinimalEventLog? Sink => Volatile.Read(ref _sink);
        internal static bool Bind(MinimalEventLog sink) => Interlocked.CompareExchange(ref _sink, sink, null) == null;
        internal static bool Unbind(MinimalEventLog owner) => ReferenceEquals(Interlocked.CompareExchange(ref _sink, null, owner), owner);
        internal static void Stage(DiagnosticFeature feature, DiagnosticPhase phase, DiagnosticOutcome outcome, DiagnosticOperation operation = default, string? targetVersion = null, DiagnosticCode code = DiagnosticCode.None)
        { try { Sink?.Stage(feature, phase, outcome, operation, targetVersion, code); } catch { } }
        internal static void Failure(DiagnosticFeature feature, DiagnosticPhase phase, DiagnosticCode code, Exception? error = null, int nativeCode = 0, int validationCode = 0, DiagnosticOperation operation = default, int applyCode = 0, string? targetVersion = null)
        { try { Sink?.Failure(feature, phase, code, error, nativeCode, validationCode, operation, applyCode, targetVersion); } catch { } }
        internal static void Relate(DiagnosticOperation parent, DiagnosticOperation child) { try { Sink?.RelateOperation(parent, child); } catch { } }
        internal static void ObserveHook(DiagnosticHook hook) { try { Sink?.ObserveHook(hook); } catch { } }
        internal static void Registered(DiagnosticHook hook, bool success) { try { Sink?.HookRegistered(hook, success); } catch { } }
        internal static void Gate(DiagnosticFeature feature, DiagnosticCode code) { try { Sink?.Gate(feature, code); } catch { } }
        internal static void Status(DiagnosticFeature component, DiagnosticUpdateState state, string? targetVersion = null, DiagnosticOperation operation = default, bool authoritative = false)
        { try { Sink?.UpdateStatus(component, state, targetVersion, operation, authoritative); } catch { } }
        internal static void FlushSoon() { try { Sink?.RequestFlush(); } catch { } }
        internal static void UiAction(DiagnosticUiAction action, DiagnosticOutcome outcome, DiagnosticOperation operation)
        { try { Sink?.UiAction(action, outcome, operation); } catch { } }
        // Input is a product enum name, never an exception message, response, URI or auth identity.
        internal static DiagnosticCode CodeForStatus(string name)
        {
            switch (name)
            {
                case "NotConfigured": return DiagnosticCode.NotConfigured;
                case "AuthenticationRequired": return DiagnosticCode.AuthenticationRequired;
                case "AccessUnavailable": return DiagnosticCode.AccessUnavailable;
                case "RateLimited": return DiagnosticCode.RateLimited;
                case "TimedOut": return DiagnosticCode.Timeout;
                case "Incomplete": case "TooLarge": case "LimitExceeded": return DiagnosticCode.LimitExceeded;
                case "InvalidSignature": return DiagnosticCode.InvalidSignature;
                case "Integrity": case "VerificationFailed": case "HashMismatch": return DiagnosticCode.Integrity;
                case "WrongTarget": return DiagnosticCode.WrongTarget;
                case "FilesBusy": case "FileBusy": case "ProcessBusy": return DiagnosticCode.FilesBusy;
                case "FaultStorageFailed": return DiagnosticCode.StorageUnavailable;
                case "ManualUpgradeRequired": case "InstallationRequired": case "CompatibilityRequired": return DiagnosticCode.Prerequisite;
                case "Failed": return DiagnosticCode.Transport;
                default: return DiagnosticCode.Unknown;
            }
        }
    }
}
