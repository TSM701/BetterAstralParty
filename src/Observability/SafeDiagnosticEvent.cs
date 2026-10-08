#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Observability
{
    internal enum DiagnosticKind { SessionStarted, PreviousSession, Stage, Failure, HookRegistered, HookObserved, HookSample, Gate, Settings, DetailChanged, CardCoverage, UpdateStatus, OperationRelation, ProgressSample, WriterHealth, Rotation, SessionEnded, StageSnapshot, FailureSnapshot }
    internal enum DiagnosticFeature { Loader, CoreUi, Notices, Settings, HandLayout, PublicCombat, ReleaseCheck, Authentication, Download, Coordinator, Helper, Installer }
    internal enum DiagnosticPhase { None, BootstrapGate, Configuration, UpdateReadiness, Catalog, PatchRegistration, UiAction, ReleaseMetadata, AuthenticationInput, DescriptorAsset, SignatureAsset, DescriptorVerification, PackageAsset, PayloadVerification, Staging, HelperQueue, WaitForExit, Apply, Rollback, Recovery, HelperInstall, Uninstall }
    internal enum DiagnosticOutcome { Begin, Completed, Failed, Cancelled, NotObserved, Observed, Active, EndCallbackObserved, IncompleteUnknown, Unknown, Unavailable, Partial }
    internal enum DiagnosticCode { None, Unknown, NotConfigured, FeatureOff, PvpExcluded, StartupWaiting, RootMissing, ContentWaiting, SchemaUnsupported, CompatibilityBlocked, Timeout, Transport, AuthenticationRequired, AccessUnavailable, RateLimited, MetadataInvalid, LimitExceeded, InvalidSignature, WrongTarget, Integrity, StorageUnavailable, FilesBusy, InterruptedUnknown, QueueCapped, StateMalformed, StateUnreadable, NativeCallFailed, Prerequisite, Recovered, CommitCompleted, RollbackCompleted, RollbackIncomplete, UnhandledTerminating, UnhandledNonTerminating, UnobservedManaged }
    internal enum DiagnosticErrorType { None, Unknown, IOException, UnauthorizedAccessException, TimeoutException, OperationCanceledException, ArgumentException, InvalidOperationException, MissingFieldException, MissingMethodException, InvalidCastException, TypeLoadException, FormatException, AggregateException, HttpRequestException, NullReferenceException, ObjectDisposedException, NotSupportedException, SecurityException, FileNotFoundException, DirectoryNotFoundException, UpdateValidationException, ApplySafetyException, AutomaticUpdateException, ReleaseInputRejectedException, ReleaseMetadataFailure, DownloadFailure }
    internal enum DiagnosticHook { CoreUi, StartupNotice, LauncherNotice }
    internal enum DiagnosticUpdateState { Unknown, NotConfigured, NotChecked, Checking, UpToDate, Available, AuthenticationRequired, AccessUnavailable, RateLimited, Failed, Stale, Incomplete, Cancelled, Idle, MissingAssets, Downloading, Preparing, Ready, Queued, Cancelling, CancelFailed, CheckingInstallation, RecoveryRequired, FilesBusy, AlreadyClaimed, ManualUpgradeRequired, Committed, RetryWaiting, FaultDisabled, FaultStorageFailed, Reactivating, TransitionRequired, SignedOut, Connected, Expired, InvalidInput, InputFailed, Connecting, AwaitingInput, TimedOut, VerificationFailed, TooLarge, Restoring, StorageFailed }
    internal enum DiagnosticLanguage { Auto, Korean, English, Unknown }
    internal enum DiagnosticChannel { Stable, Beta, Unknown }
    internal enum DiagnosticHandMode { Hover, Click, Unknown }
    internal enum DiagnosticEndReason { ProcessExitCallback, OwnerDisposed, Unknown }
    internal enum DiagnosticUiAction { Unknown, Open, Close, Features, General, Enabled, ShowDetails, KoMinimum, UseRealNames, CardPopups, BattleStatus, ShushuShield, FieldBuffs, DiagnosticLogging, MuteUnfocused, InputAttention, UiScale, Opacity, Language, HandLayout, UpdateAuth, UpdateSignOut, UpdateChannel, UpdateCheck, UpdateDownload, UpdateCancel, AutoDownload, ApplyAfterExit, UpdateReactivate, UpdateRecover, Details, Names, AfterExit, UpdateGet, Diagnostics, DiagnosticsOpen, DiagnosticsCollect, ScaleMinus, ScalePlus, OpacityMinus, OpacityPlus }

    internal readonly struct DiagnosticOperation
    {
        internal readonly string? Value;
        private DiagnosticOperation(string value) { Value = value; }
        internal static DiagnosticOperation New() => new DiagnosticOperation(Guid.NewGuid().ToString("N"));
        // Only use for a verified updater operation GUID, never auth/cache/account identities.
        internal static DiagnosticOperation VerifiedGuid(string? value) => Guid.TryParseExact(value, "N", out var parsed) ? new DiagnosticOperation(parsed.ToString("N")) : default;
    }

    internal sealed class DiagnosticIdentity
    {
        internal readonly string Version, Build, Runtime, Session;
        internal DiagnosticIdentity(string version, string build, string runtime)
        {
            var safeBuild = build ?? "";
            var safeRuntime = runtime ?? "";
            Version = SafeVersion(version); Build = Regex.IsMatch(safeBuild, "\\A(?:[0-9A-Fa-f]{32}|[0-9A-Fa-f]{64})\\z") ? safeBuild.ToUpperInvariant() : "unknown";
            Runtime = Regex.IsMatch(safeRuntime, "\\A[0-9]{1,5}(?:\\.[0-9]{1,5}){1,3}\\z") ? safeRuntime : "unknown";
            Session = Guid.NewGuid().ToString("N");
        }
        internal static string SafeVersion(string? value)
        {
            if (value != null && value.StartsWith("v", StringComparison.Ordinal)) value = value.Substring(1);
            return value != null && Regex.IsMatch(value, "\\A[0-9]{1,6}\\.[0-9]{1,6}\\.[0-9]{1,6}(?:-(?:rc|beta|alpha|dev)(?:\\.[0-9]{1,6}){0,3})?(?:\\+[0-9A-Fa-f]{1,64})?\\z") ? value : "unknown";
        }
    }

    internal readonly struct DiagnosticSettings
    {
        internal readonly uint EnabledFlags;
        internal readonly DiagnosticLanguage Language;
        internal readonly DiagnosticChannel Channel;
        internal readonly DiagnosticHandMode HandMode;
        internal readonly int ScalePercent, OpacityPercent;
        // Flags are documented fixed booleans; all bits outside the owned 16 are removed.
        internal DiagnosticSettings(uint enabledFlags, DiagnosticLanguage language, DiagnosticChannel channel, DiagnosticHandMode handMode, int scalePercent, int opacityPercent)
        { EnabledFlags = enabledFlags & 65535; Language = SafeEnum.Value(language); Channel = SafeEnum.Value(channel); HandMode = SafeEnum.Value(handMode); ScalePercent = Math.Max(75, Math.Min(150, scalePercent)); OpacityPercent = Math.Max(0, Math.Min(100, opacityPercent)); }
        internal string Fingerprint => EnabledFlags + ":" + Language + ":" + Channel + ":" + HandMode + ":" + ScalePercent + ":" + OpacityPercent;
    }

    internal static class SafeEnum
    {
        internal static T Value<T>(T value) where T : struct => Enum.IsDefined(typeof(T), value) ? value : default;
        internal static T Parse<T>(string? name) where T : struct => Enum.TryParse(name, false, out T value) && Enum.IsDefined(typeof(T), value) ? value : default;
        internal static DiagnosticErrorType Error(Exception? error)
        {
            if (error == null) return DiagnosticErrorType.None;
            if (error.GetType().Assembly == typeof(SafeEnum).Assembly) switch (error.GetType().FullName) {
                case "BetterAstralParty.Updating.UpdateValidationException": return DiagnosticErrorType.UpdateValidationException;
                case "BetterAstralParty.Updating.ApplySafetyException": return DiagnosticErrorType.ApplySafetyException;
                case "BetterAstralParty.AutomaticUpdateException": return DiagnosticErrorType.AutomaticUpdateException;
                case "BetterAstralParty.ReleaseInputRejectedException": return DiagnosticErrorType.ReleaseInputRejectedException;
                case "BetterAstralParty.ReleaseUpdates+CheckFailure": return DiagnosticErrorType.ReleaseMetadataFailure;
                case "BetterAstralParty.Updating.UpdateDownloads+DownloadFailure": return DiagnosticErrorType.DownloadFailure;
            }
            if (error.GetType().FullName == "System.Net.Http.HttpRequestException") return DiagnosticErrorType.HttpRequestException;
            if (error.GetType().FullName == "System.Security.SecurityException") return DiagnosticErrorType.SecurityException;
            if (error is NullReferenceException) return DiagnosticErrorType.NullReferenceException;
            if (error is ObjectDisposedException) return DiagnosticErrorType.ObjectDisposedException;
            if (error is NotSupportedException) return DiagnosticErrorType.NotSupportedException;
            if (error is UnauthorizedAccessException) return DiagnosticErrorType.UnauthorizedAccessException;
            if (error is FileNotFoundException) return DiagnosticErrorType.FileNotFoundException;
            if (error is DirectoryNotFoundException) return DiagnosticErrorType.DirectoryNotFoundException;
            if (error is IOException) return DiagnosticErrorType.IOException;
            if (error is TimeoutException) return DiagnosticErrorType.TimeoutException;
            if (error is OperationCanceledException) return DiagnosticErrorType.OperationCanceledException;
            if (error is MissingFieldException) return DiagnosticErrorType.MissingFieldException;
            if (error is MissingMethodException) return DiagnosticErrorType.MissingMethodException;
            if (error is InvalidCastException) return DiagnosticErrorType.InvalidCastException;
            if (error is TypeLoadException) return DiagnosticErrorType.TypeLoadException;
            if (error is FormatException) return DiagnosticErrorType.FormatException;
            if (error is AggregateException) return DiagnosticErrorType.AggregateException;
            if (error is ArgumentException) return DiagnosticErrorType.ArgumentException;
            if (error is InvalidOperationException) return DiagnosticErrorType.InvalidOperationException;
            return DiagnosticErrorType.Unknown;
        }
    }

    internal sealed class SafeDiagnosticEvent
    {
        internal DiagnosticKind Kind;
        internal DiagnosticFeature Feature;
        internal DiagnosticPhase Phase;
        internal DiagnosticOutcome Outcome;
        internal DiagnosticCode Code;
        internal DiagnosticUiAction UiAction;
        internal DiagnosticSite Site;
        internal DiagnosticErrorType Error;
        internal DiagnosticOperation Operation;
        internal DiagnosticOperation RelatedOperation;
        internal DiagnosticUpdateState UpdateState;
        internal DiagnosticSettings? Settings;
        internal string TargetVersion = "unknown";
        internal long Count, SecondaryCount;
        internal int NativeCode, ValidationCode, ApplyCode;
        internal long EnqueuedStamp;
        internal DateTimeOffset Utc;
        internal bool Priority;
    }

    internal static class SafeDiagnosticJson
    {
        // Every string is an enum, controlled version, hex identity or GUID. No arbitrary payload API.
        internal static string Encode(SafeDiagnosticEvent item, DiagnosticIdentity identity, long sequence, long startStamp)
        {
            var b = new StringBuilder(640);
            b.Append("{\"schema\":\"bap.minimal-diagnostic/v1\",\"process_session\":\"").Append(identity.Session)
             .Append("\",\"sequence\":").Append(sequence.ToString(CultureInfo.InvariantCulture))
             .Append(",\"utc\":\"").Append(item.Utc.ToString("O", CultureInfo.InvariantCulture))
             .Append("\",\"elapsed_ms\":").Append(Math.Max(0, (long)((item.EnqueuedStamp - startStamp) * 1000d / System.Diagnostics.Stopwatch.Frequency)).ToString(CultureInfo.InvariantCulture))
             .Append(",\"mod_version\":\"").Append(identity.Version).Append("\",\"build_id\":\"").Append(identity.Build)
             .Append("\",\"runtime_version\":\"").Append(identity.Runtime).Append("\",\"kind\":\"").Append(SafeEnum.Value(item.Kind))
             .Append("\",\"feature\":\"").Append(SafeEnum.Value(item.Feature)).Append("\",\"phase\":\"").Append(SafeEnum.Value(item.Phase))
             .Append("\",\"outcome\":\"").Append(SafeEnum.Value(item.Outcome)).Append("\",\"code\":\"").Append(SafeEnum.Value(item.Code))
             .Append("\",\"ui_action\":\"").Append(SafeEnum.Value(item.UiAction))
             .Append("\",\"error_site\":\"").Append(SafeEnum.Value(item.Site))
             .Append("\",\"error_type\":\"").Append(SafeEnum.Value(item.Error)).Append("\",\"operation\":\"").Append(item.Operation.Value ?? "-")
             .Append("\",\"related_operation\":\"").Append(item.RelatedOperation.Value ?? "-")
             .Append("\",\"target_version\":\"").Append(DiagnosticIdentity.SafeVersion(item.TargetVersion)).Append("\",\"update_state\":\"").Append(SafeEnum.Value(item.UpdateState))
             .Append("\",\"count\":").Append(Math.Max(0, item.Count).ToString(CultureInfo.InvariantCulture))
             .Append(",\"secondary_count\":").Append(Math.Max(0, item.SecondaryCount).ToString(CultureInfo.InvariantCulture))
             .Append(",\"native_code\":").Append(item.NativeCode.ToString(CultureInfo.InvariantCulture))
             .Append(",\"validation_code\":").Append(Math.Max(0, Math.Min(65535, item.ValidationCode)).ToString(CultureInfo.InvariantCulture))
             .Append(",\"apply_code\":").Append(Math.Max(0, Math.Min(65535, item.ApplyCode)).ToString(CultureInfo.InvariantCulture));
            if (item.Settings is DiagnosticSettings s) b.Append(",\"settings\":{\"enabled_flags\":").Append(s.EnabledFlags.ToString(CultureInfo.InvariantCulture))
                .Append(",\"language\":\"").Append(s.Language).Append("\",\"channel\":\"").Append(s.Channel).Append("\",\"hand_mode\":\"").Append(s.HandMode)
                .Append("\",\"scale_percent\":").Append(s.ScalePercent.ToString(CultureInfo.InvariantCulture)).Append(",\"opacity_percent\":").Append(s.OpacityPercent.ToString(CultureInfo.InvariantCulture)).Append('}');
            return b.Append('}').ToString();
        }
    }
}
