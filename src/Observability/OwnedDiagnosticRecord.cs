// C# 5 / .NET Framework 4.8, net6.0 and net8.0. Independent typed projection; no raw JSON pass-through.
#if NET6_0_OR_GREATER
#pragma warning disable CS8600
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace BetterAstralParty.Observability
{
    internal static class OwnedDiagnosticRecord
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly string[] Required = "schema,process_session,sequence,utc,elapsed_ms,mod_version,build_id,runtime_version,kind,feature,phase,outcome,code,ui_action,error_site,error_type,operation,related_operation,target_version,update_state,count,secondary_count,native_code,validation_code,apply_code".Split(',');
        private static readonly string[] Settings = "enabled_flags,language,channel,hand_mode,scale_percent,opacity_percent".Split(',');
        private static readonly HashSet<string> Numbers = new HashSet<string>("sequence,elapsed_ms,count,secondary_count,native_code,validation_code,apply_code,settings.enabled_flags,settings.scale_percent,settings.opacity_percent".Split(','), StringComparer.Ordinal);
        private static readonly Dictionary<string, string[]> Enums = new Dictionary<string, string[]>(StringComparer.Ordinal) {
            { "kind", "SessionStarted,PreviousSession,Stage,Failure,HookRegistered,HookObserved,HookSample,Gate,Settings,DetailChanged,CardCoverage,UpdateStatus,OperationRelation,ProgressSample,WriterHealth,Rotation,SessionEnded,StageSnapshot,FailureSnapshot".Split(',') },
            { "feature", "Loader,CoreUi,Notices,Settings,HandLayout,PublicCombat,ReleaseCheck,Authentication,Download,Coordinator,Helper,Installer".Split(',') },
            { "phase", "None,BootstrapGate,Configuration,UpdateReadiness,Catalog,PatchRegistration,UiAction,ReleaseMetadata,AuthenticationInput,DescriptorAsset,SignatureAsset,DescriptorVerification,PackageAsset,PayloadVerification,Staging,HelperQueue,WaitForExit,Apply,Rollback,Recovery,HelperInstall,Uninstall".Split(',') },
            { "outcome", "Begin,Completed,Failed,Cancelled,NotObserved,Observed,Active,EndCallbackObserved,IncompleteUnknown,Unknown,Unavailable,Partial".Split(',') },
            { "code", "None,Unknown,NotConfigured,FeatureOff,PvpExcluded,StartupWaiting,RootMissing,ContentWaiting,SchemaUnsupported,CompatibilityBlocked,Timeout,Transport,AuthenticationRequired,AccessUnavailable,RateLimited,MetadataInvalid,LimitExceeded,InvalidSignature,WrongTarget,Integrity,StorageUnavailable,FilesBusy,InterruptedUnknown,QueueCapped,StateMalformed,StateUnreadable,NativeCallFailed,Prerequisite,Recovered,CommitCompleted,RollbackCompleted,RollbackIncomplete,UnhandledTerminating,UnhandledNonTerminating,UnobservedManaged".Split(',') },
            { "ui_action", "Unknown,Open,Close,Features,General,Enabled,ShowDetails,KoMinimum,UseRealNames,CardPopups,BattleStatus,ShushuShield,FieldBuffs,DiagnosticLogging,MuteUnfocused,InputAttention,UiScale,Opacity,Language,HandLayout,UpdateAuth,UpdateSignOut,UpdateChannel,UpdateCheck,UpdateDownload,UpdateCancel,AutoDownload,ApplyAfterExit,UpdateReactivate,UpdateRecover,Details,Names,AfterExit,UpdateGet,Diagnostics,DiagnosticsOpen,DiagnosticsCollect,ScaleMinus,ScalePlus,OpacityMinus,OpacityPlus".Split(',') },
            { "error_type", "None,Unknown,IOException,UnauthorizedAccessException,TimeoutException,OperationCanceledException,ArgumentException,InvalidOperationException,MissingFieldException,MissingMethodException,InvalidCastException,TypeLoadException,FormatException,AggregateException,HttpRequestException,NullReferenceException,ObjectDisposedException,NotSupportedException,SecurityException,FileNotFoundException,DirectoryNotFoundException,UpdateValidationException,ApplySafetyException,AutomaticUpdateException,ReleaseInputRejectedException,ReleaseMetadataFailure,DownloadFailure".Split(',') },
            { "error_site", "None,Unknown,PluginLoad,PluginSettings,ModUiTick,ModUiApply,CardUiUpdate,CardDiceShow,ReleaseMetadata,UpdateDownload,CoordinatorTick,CredentialPoll,HelperRun,HelperRecover,TransactionApply,TransactionRecover,RuntimeRead,RuntimeCall,NativeUi".Split(',') },
            { "update_state", "Unknown,NotConfigured,NotChecked,Checking,UpToDate,Available,AuthenticationRequired,AccessUnavailable,RateLimited,Failed,Stale,Incomplete,Cancelled,Idle,MissingAssets,Downloading,Preparing,Ready,Queued,Cancelling,CancelFailed,CheckingInstallation,RecoveryRequired,FilesBusy,AlreadyClaimed,ManualUpgradeRequired,Committed,RetryWaiting,FaultDisabled,FaultStorageFailed,Reactivating,TransitionRequired,SignedOut,Connected,Expired,InvalidInput,InputFailed,Connecting,AwaitingInput,TimedOut,VerificationFailed,TooLarge,Restoring,StorageFailed".Split(',') },
            { "settings.language", "Auto,Korean,English,Unknown".Split(',') },
            { "settings.channel", "Stable,Beta,Unknown".Split(',') },
            { "settings.hand_mode", "Hover,Click,Unknown".Split(',') },
        };
        private static bool Match(string value, string pattern) { return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        private sealed class Parser
        {
            internal readonly Dictionary<string,string> Values = new Dictionary<string,string>(StringComparer.Ordinal);
            internal readonly HashSet<string> Numeric = new HashSet<string>(StringComparer.Ordinal);
            internal bool HasSettings;
            private readonly string _text; private int _at;
            internal Parser(string text) { _text = text; }
            private void Space() { while (_at < _text.Length && _text[_at] == ' ') _at++; }
            private void Expect(char c) { Space(); if (_at >= _text.Length || _text[_at++] != c) throw new FormatException(); }
            private string String()
            {
                Expect('"'); var start = _at;
                while (_at < _text.Length && _text[_at] != '"') { char c = _text[_at++]; if (c < 32 || c > 126 || c == '\\') throw new FormatException(); }
                if (_at >= _text.Length) throw new FormatException(); var result = _text.Substring(start, _at - start); _at++; return result;
            }
            private void Object(string prefix)
            {
                Expect('{'); Space(); if (_at < _text.Length && _text[_at] == '}') { _at++; return; }
                while (true) {
                    var key = String(); Expect(':'); Space();
                    if (prefix == "" && key == "settings") { if (HasSettings) throw new FormatException(); HasSettings = true; Object("settings."); }
                    else {
                        key = prefix + key; if (Values.ContainsKey(key)) throw new FormatException();
                        string value;
                        if (_at < _text.Length && _text[_at] == '"') value = String();
                        else {
                            int start = _at; if (_at < _text.Length && _text[_at] == '-') _at++;
                            int digits = _at; while (_at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9') _at++;
                            if (_at == digits || _at - start > 20) throw new FormatException();
                            value = _text.Substring(start, _at - start); Numeric.Add(key);
                        }
                        Values.Add(key, value);
                    }
                    Space(); if (_at < _text.Length && _text[_at] == '}') { _at++; return; } Expect(',');
                }
            }
            internal void Parse() { Object(""); Space(); if (_at != _text.Length) throw new FormatException(); }
        }
        private static bool GuidField(string value, bool optional) { Guid g; return optional && value == "-" || value.Length == 32 && Guid.TryParseExact(value, "N", out g) && value == value.ToLowerInvariant(); }
        private static bool Valid(Parser p)
        {
            if (p.Values.Count != Required.Length + (p.HasSettings ? Settings.Length : 0)) return false;
            foreach (var key in Required) if (!p.Values.ContainsKey(key)) return false;
            if (p.HasSettings) foreach (var key in Settings) if (!p.Values.ContainsKey("settings." + key)) return false;
            foreach (var pair in p.Values) {
                bool numeric = Numbers.Contains(pair.Key); if (numeric != p.Numeric.Contains(pair.Key)) return false;
                string[] allowed; if (Enums.TryGetValue(pair.Key, out allowed) && Array.IndexOf(allowed, pair.Value) < 0) return false;
                if (numeric) {
                    long n; if (!Int64.TryParse(pair.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)) return false;
                    if (pair.Key == "native_code") { if (n < Int32.MinValue || n > Int32.MaxValue) return false; }
                    else if (n < 0) return false;
                    if ((pair.Key == "validation_code" || pair.Key == "apply_code" || pair.Key == "settings.enabled_flags") && n > 65535) return false;
                    if (pair.Key == "sequence" && n == 0) return false;
                    if (pair.Key == "settings.scale_percent" && (n < 75 || n > 150)) return false;
                    if (pair.Key == "settings.opacity_percent" && n > 100) return false;
                }
            }
            if (p.Values["schema"] != "bap.minimal-diagnostic/v1" || !GuidField(p.Values["process_session"], false)
                || !GuidField(p.Values["operation"], true) || !GuidField(p.Values["related_operation"], true)) return false;
            DateTimeOffset time;
            if (!DateTimeOffset.TryParseExact(p.Values["utc"], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out time) || time.Offset != TimeSpan.Zero) return false;
            foreach (var key in new[]{"mod_version", "target_version"}) if (p.Values[key] != "unknown" && !Match(p.Values[key], @"\A[0-9]{1,6}\.[0-9]{1,6}\.[0-9]{1,6}(?:-(?:rc|beta|alpha|dev)(?:\.[0-9]{1,6}){0,3})?(?:\+[0-9A-Fa-f]{1,64})?\z")) return false;
            if (p.Values["build_id"] != "unknown" && !Match(p.Values["build_id"], @"\A(?:[0-9A-F]{32}|[0-9A-F]{64})\z")) return false;
            if (p.Values["runtime_version"] != "unknown" && !Match(p.Values["runtime_version"], @"\A[0-9]{1,5}(?:\.[0-9]{1,5}){1,3}\z")) return false;
            if (p.Values["kind"] == "CardCoverage") { long a = Int64.Parse(p.Values["count"], CultureInfo.InvariantCulture), b = Int64.Parse(p.Values["secondary_count"], CultureInfo.InvariantCulture); if (a > 64 || b > 64) return false; }
            if (p.Values["kind"] == "HookRegistered" || p.Values["kind"] == "HookObserved" || p.Values["kind"] == "HookSample") if (Int64.Parse(p.Values["count"], CultureInfo.InvariantCulture) > 2) return false;
            return true;
        }
        private static void Field(StringBuilder b, string key, string value, bool numeric)
        { b.Append('"').Append(key).Append("\":"); if (!numeric) b.Append('"'); b.Append(numeric ? Int64.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : value); if (!numeric) b.Append('"'); }
        internal static bool Inspect(string line, out string session, out long sequence, out string kind)
        {
            session = kind = ""; sequence = 0;
            try {
                if (line == null || line.Length > 2048 || Utf8.GetByteCount(line) > 2048) return false;
                var p = new Parser(line); p.Parse(); if (!Valid(p)) return false;
                session = p.Values["process_session"]; kind = p.Values["kind"];
                sequence = Int64.Parse(p.Values["sequence"], CultureInfo.InvariantCulture); return true;
            } catch { session = kind = ""; sequence = 0; return false; }
        }
        public static bool TryProject(string line, out string projected)
        {
            projected = "";
            try {
                if (line == null || line.Length > 2048 || Utf8.GetByteCount(line) > 2048) return false;
                var p = new Parser(line); p.Parse(); if (!Valid(p)) return false;
                var b = new StringBuilder("{");
                for (int i = 0; i < Required.Length; i++) { if (i > 0) b.Append(','); Field(b, Required[i], p.Values[Required[i]], Numbers.Contains(Required[i])); }
                if (p.HasSettings) { b.Append(",\"settings\":{"); for (int i=0; i<Settings.Length; i++) { if(i>0)b.Append(','); Field(b, Settings[i], p.Values["settings."+Settings[i]], Numbers.Contains("settings."+Settings[i])); } b.Append('}'); }
                projected = b.Append('}').ToString(); return true;
            } catch { projected = ""; return false; }
        }
        public static byte[] ProjectText(string text, int maximumBytes, out int accepted, out int dropped, out bool truncated)
        {
            accepted = dropped = 0; truncated = false; var b = new StringBuilder(); int size = 0;
            if (text == null || maximumBytes < 0) return new byte[0];
            using (var reader = new StringReader(text)) {
                string line; while ((line = reader.ReadLine()) != null) {
                    if (reader.Peek() == -1 && !text.EndsWith("\n", StringComparison.Ordinal)) { dropped++; break; }
                    string clean; if (!TryProject(line, out clean)) { dropped++; continue; }
                    int bytes = Utf8.GetByteCount(clean) + 1; if (size + bytes > maximumBytes) { truncated = true; break; }
                    b.Append(clean).Append('\n'); size += bytes; accepted++;
                }
            }
            return Utf8.GetBytes(b.ToString());
        }
    }
}
