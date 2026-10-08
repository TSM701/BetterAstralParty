// Local-only fixed allowlist. C# 5 / Framework 4.8 and net6.0 compatible.
#if NET6_0_OR_GREATER
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace BetterAstralParty.Diagnostics
{
    public sealed class BundleResult
    {
        public string ArchivePath { get; internal set; }
        public int Included { get; internal set; }
        public int Omitted { get; internal set; }
        internal string FolderIdentity;
        public BundleResult() { ArchivePath = ""; }
        // Identity belongs to the directory used for the commit, never to a later path replacement.
        public void OpenFolder(Action<string> dispatch)
        {
            using (var lease = DiagnosticBundle.AcquireCommittedFolder(Path.GetDirectoryName(ArchivePath), FolderIdentity)) lease.Open(dispatch);
        }
    }

    public sealed class LiveDiagnosticHealth
    {
        private readonly bool module, detail, recording, failed;
        private readonly long dropped;
        private readonly string version, build, session;
        private readonly DateTime captured;
        internal LiveDiagnosticHealth(bool modulePresent, bool requestedDetailed, bool isRecording, bool storageFailed,
            long droppedEvents, string modVersion, string buildId, string processSession, DateTime captureUtc)
        {
            module = modulePresent; detail = requestedDetailed; failed = storageFailed; recording = isRecording && !storageFailed;
            dropped = Math.Max(0, Math.Min(1000000000L, droppedEvents));
            version = Regex.IsMatch(modVersion ?? "", @"\A[0-9]{1,6}\.[0-9]{1,6}\.[0-9]{1,6}(?:-(?:rc|beta|alpha|dev)(?:\.[0-9]{1,6}){0,3})?(?:\+[0-9A-Fa-f]{1,64})?\z", RegexOptions.CultureInvariant) ? modVersion : "unknown";
            build = Regex.IsMatch(buildId ?? "", @"\A(?:[0-9A-F]{32}|[0-9A-F]{64})\z", RegexOptions.CultureInvariant) ? buildId : "unknown";
            Guid value; session = Guid.TryParseExact(processSession, "N", out value) ? value.ToString("N") : "";
            captured = captureUtc.Kind == DateTimeKind.Utc ? captureUtc : DateTime.MinValue;
        }
        private static string Bool(bool value) { return value ? "true" : "false"; }
        internal string Json(MinimalDiagnosticProjection.ProjectionContext identities)
        {
            if (captured == DateTime.MinValue) return Unavailable;
            string alias = "";
            if (module && session != "") {
                var fields = new Dictionary<string,string>(StringComparer.Ordinal) { {"process_session", session}, {"operation", "-"}, {"related_operation", "-"} };
                if (identities.Rewrite(fields)) alias = fields["process_session"];
            }
            return "{\"availability\":\"captured\",\"source\":\"running-plugin-owner\",\"capturedUtc\":\"" + captured.ToString("O", CultureInfo.InvariantCulture)
                + "\",\"modulePresent\":" + Bool(module) + ",\"requestedDetailed\":" + (module ? Bool(detail) : "null")
                + ",\"recording\":" + (module ? Bool(recording) : "null") + ",\"storageFailed\":" + (module ? Bool(failed) : "null")
                + ",\"dropped\":" + (module ? dropped.ToString(CultureInfo.InvariantCulture) : "null")
                + ",\"modVersion\":\"" + version + "\",\"buildId\":\"" + build + "\",\"processSession\":"
                + (alias == "" ? "null" : "\"" + alias + "\"") + "}";
        }
        internal const string Unavailable = "{\"availability\":\"unavailable\",\"source\":\"unavailable\",\"capturedUtc\":null,\"modulePresent\":null,\"requestedDetailed\":null,\"recording\":null,\"storageFailed\":null,\"dropped\":null,\"modVersion\":\"unknown\",\"buildId\":\"unknown\",\"processSession\":null}";
    }

    public static class DiagnosticBundle
    {
        public const int MaxInputFileBytes = 2 * 1024 * 1024, MaxInputBytes = 8 * 1024 * 1024;
        public const int MaxOutputFileBytes = 512 * 1024, MaxOutputBytes = 4 * 1024 * 1024;
        public const int MaxFiles = 24, MaxDirectoryEntries = 512, MaxRecentPerDirectory = 8, RecentDays = 7;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Regex Names = Pattern(@"\A(?:Launch-[0-9a-f]{32}|BetterAstralParty-InstallError-[0-9]{8}-[0-9]{6}-[0-9a-f]{8})\.log\z");
        private static readonly Regex Secret = Pattern(@"(?i)(?:\b(?:authorization|proxy-authorization|bearer|basic|cookie|set-cookie|password|passwd|pwd|credential|(?:client[ _-]?)?secret|signature|private[ _-]?key|access[ _-]?key|api[ _-]?key|(?:access|refresh|id|auth)[ _-]?token|token|session[ _-]?id|steam[ _-]?id|user[ _-]?id|account(?:[ _-]?(?:id|name))?|username|nickname|display[ _-]?name|player[ _-]?name|email|chat|ip[ _-]?address)\b|토큰|비밀번호|암호|닉네임|계정|사용자명|인증[ _-]?헤더|비밀[ _-]?키|서명[ _-]?키|gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|AKIA[A-Z0-9]{16}|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|-----BEGIN.*(?:PRIVATE|OPENSSH|PGP).*(?:KEY|BLOCK))");
        private static readonly Regex Private = Pattern(@"(?i)(?:[a-z]:[\\/]|\\\\|(?:https?|wss?|ftp)://|\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b|\b(?:\d{1,3}\.){3}\d{1,3}\b|\b(?:[0-9a-f]{1,4}:){4,}[0-9a-f:]+\b|\b[0-9a-f:]*::[0-9a-f:]+\b|\b[0-9]{17}\b|\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b|(?:^|[\s\""'=])/(?:home|Users|tmp|var|mnt|media|opt|data)/|\b[A-Za-z0-9_+/=-]{32,}\b)");
        private static readonly Regex Boundary = Pattern(@"\A(?:\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}|\[(?:Info|Warning|Error|Debug|Fatal|Message)[ :\]])");
        private static readonly Regex Ansi = Pattern(@"\x1b\[[0-?]*[ -/]*[@-~]");
        private static Regex Pattern(string value) { return new Regex(value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }

        public const string Guide =
            "BetterAstralParty local diagnostics / 로컬 진단\r\n\r\n" +
            "Create-Diagnostics.cmd: create a ZIP; Open-Logs.cmd: open this folder.\r\n" +
            "Create-Diagnostics.cmd로 ZIP 생성, Open-Logs.cmd로 이 폴더 열기.\r\n" +
            "Open the ZIP, review logs/*.log and summary.json, then attach it yourself.\r\n" +
            "ZIP의 logs/*.log와 summary.json을 확인한 뒤 직접 첨부하세요. 자동 전송은 없습니다.\r\n\r\n" +
            "Sources: last 7 days; mod current/previous and combat mismatch logs, game compatibility/launch errors,\r\n" +
            "fixed minimal-v1 runtime/helper current/previous JSONL: stages, failures/sites, hooks, health and rotation.\r\n" +
            "고정 minimal-v1 runtime/helper 현재·이전 JSONL의 단계·오류 위치·hook·writer 관측·회전도 포함합니다.\r\n" +
            "strict projections of up to 8 recent install/launcher errors per log folder.\r\n" +
            "BepInEx LogOutput and Unity Player/Player-prev: file metadata only, never contents.\r\n" +
            "수집: 최근 7일 모드/계산 불일치/호환성/실행 오류/BepInEx/Unity 로그,\r\n" +
            "로컬 및 임시 로그 폴더별 최근 설치·런처 오류 최대 8개의 허용 항목 요약.\r\n" +
            "BepInEx·Unity 로그는 존재·크기·수정 시각만 포함하며 내용을 읽지 않습니다.\r\n" +
            "Fixed paths and typed fields only; card/hand/chat payload, unknown text, full configs, credential stores,\r\n" +
            "updater state, dumps and arbitrary files are excluded. Original exception messages/stacks are excluded.\r\n" +
            "고정 경로와 허용 필드만 요약합니다. 카드·손패·채팅·알 수 없는 텍스트·전체 설정·인증 저장소·\r\n" +
            "업데이트 상태·덤프·임의 파일·원문 예외 메시지와 stack은 제외합니다.\r\n" +
            "Legacy errors retain fixed feature/subfeature/error-class/site codes. Unsupported values become Unknown.\r\n" +
            "Only up to 16 exact owned stack anchors are mapped; missing/async/native sites may remain Unknown.\r\n" +
            "기존 오류의 고정 기능·분류·위치 코드를 남깁니다. 미등록 값과 확인하지 못한 위치는 Unknown입니다.\r\n" +
            "Limits: 24 logs; 2 MiB input/file, 8 MiB input total; 512 KiB sanitized/file, 4 MiB sanitized total.\r\n" +
            "용량 제한: 24개, 원본 파일당 2 MiB/총 8 MiB, 처리 후 파일당 512 KiB/총 4 MiB.\r\n" +
            "Oversized inputs are skipped, never tailed, to avoid fragments of secrets.\r\n" +
            "너무 큰 원본은 제외합니다. 잘린 토큰 조각을 피하기 위해 끝부분만 읽지 않습니다.\r\n" +
            "Missing, old, locked, linked, invalid text and limited files are recorded in summary.json.\r\n" +
            "누락·오래됨·잠김·링크·잘못된 텍스트·제한 사유는 summary.json에 기록합니다.\r\n" +
            "Recognized records are reserialized from numeric/bool/enum/version/error-type allowlists.\r\n" +
            "Sensitive lines/continuations, unknown fields and long/unfinished lines are omitted and counted.\r\n" +
            "허용된 수치·bool·enum·버전·오류 타입만 새로 기록합니다. 민감한 줄·이어지는 내용·알 수 없는\r\n" +
            "필드·긴 줄·미완성 마지막 줄은 제외하고 개수를 기록합니다.\r\n" +
            "This is not complete anonymisation. Inspect before sharing. No automatic upload.\r\n" +
            "완전한 익명화를 보장하지 않습니다. 공유 전 내용을 확인하세요.\r\n" +
            "Folder identity is checked and pinned through shell dispatch; Explorer may resolve the path after return.\r\n" +
            "폴더 열기 호출까지 경로와 디렉터리를 검증·고정합니다. Explorer의 이후 경로 재해석까지 보장하지는 못합니다.\r\n" +
            "Structured session/operation IDs become fresh bundle-local aliases; original IDs are omitted.\r\n" +
            "Unknown structured records are omitted and counted by fixed rejection reason in summary.json.\r\n" +
            "구조화 세션·작업 ID는 ZIP 안에서만 연결되는 새 별칭으로 바꿉니다. 원래 ID는 제외합니다.\r\n" +
            "알 수 없는 구조화 기록은 원문을 제외하고 summary.json의 고정 거부 사유별 개수로 표시합니다.\r\n" +
            "Timestamps/public combat/errors may reveal play activity. Running logs may be incomplete.\r\n" +
            "시각·공개 전투 상태·오류는 플레이 활동을 드러낼 수 있고 실행 중 로그는 일부만 기록될 수 있습니다.\r\n" +
            "Missing means not observed, not success. Writer health/session correlation and cross-source ordering\r\n" +
            "are unknown for legacy sources. Structured relations are bundle-local; sequence is not causal proof.\r\n" +
            "Historical WriterHealth is not live health; snapshots are not new actions/errors.\r\n" +
            "HP agreement does not prove the full model. No updater recovery state is included.\r\n" +
            "누락은 관측되지 않았다는 뜻입니다. 정상 판정이 아닙니다. writer 상태·세션 연결·파일 간 순서는\r\n" +
            "legacy에서 확인하지 못합니다. 구조화 기록의 연결은 ZIP 안에 한정하며 순번은 인과 증명이 아닙니다.\r\n" +
            "과거 writer 관측은 현재 상태가 아니며 snapshot은 새 동작·오류가 아닙니다. HP 일치가 전체 모델\r\n" +
            "정확성을 입증하지 않습니다. 업데이트 복구 상태 원본은 제외합니다.\r\n" +
            "Nothing is deleted and no process is stopped. Enable optional detailed logging before reproducing an issue.\r\n" +
            "로그 삭제나 게임 종료는 없습니다. 상세 진단은 문제 재현 전에 켜야 하며 없는 기록은 복구하지 못합니다.\r\n";

        private sealed class Row
        {
            internal string Source = "", Status = "", Entry = "", Modified = "";
            internal long Input;
            internal int Output, Redacted, Dropped, Projected, RejectedCandidates;
            internal long ReadBytes;
            internal bool Truncated, Changing;
            internal Dictionary<string, int> Rejections;
        }
        private sealed class Payload
        {
            internal Row Row; internal byte[] Bytes;
            internal Payload(Row row, byte[] bytes) { Row = row; Bytes = bytes; }
        }

        // Hosts pass known roots, never arbitrary file paths or include patterns.
        public static BundleResult Collect(string gameRoot, string localLogs, string fallbackLogs,
            string unityLogs, string primaryHub, string fallbackHub, string hostVersion)
        {
            return Collect(gameRoot, localLogs, fallbackLogs, unityLogs, primaryHub, fallbackHub, hostVersion, "");
        }
        public static BundleResult Collect(string gameRoot, string localLogs, string fallbackLogs,
            string unityLogs, string primaryHub, string fallbackHub, string hostVersion, string runningPluginVersion)
        {
            return Collect(gameRoot, localLogs, fallbackLogs, unityLogs, primaryHub, fallbackHub, hostVersion, runningPluginVersion, null);
        }
        public static BundleResult Collect(string gameRoot, string localLogs, string fallbackLogs,
            string unityLogs, string primaryHub, string fallbackHub, string hostVersion, string runningPluginVersion, LiveDiagnosticHealth liveHealth)
        {
            var rows = new List<Row>(); var payloads = new List<Payload>();
            long raw = 0; int output = 0; DateTime now = DateTime.UtcNow;
            var identities = new MinimalDiagnosticProjection.ProjectionContext();
            string version = "unknown"; bool gameValid = false;
            using (var game = TryRoot(gameRoot, rows, "game-root"))
            {
                if (game != null)
                {
                    try
                    {
                        using (var exe = game.Open("AstralParty_INT.exe", false))
                        using (var data = game.Open("AstralParty_INT_Data", true)) { gameValid = true; }
                    }
                    catch { rows.Add(new Row { Source = "game-root", Status = "invalid-game-markers" }); }
                    if (gameValid)
                    {
                        // Minimal sources take priority inside unchanged per-file and total quotas.
                        string[] minimal = { "BepInEx/BetterAstralParty-Diagnostics/minimal-v1/events-current.jsonl",
                            "BepInEx/BetterAstralParty-Diagnostics/minimal-v1/events-previous.jsonl",
                            "BepInEx/BetterAstralParty-Diagnostics/minimal-v1/helper/events-current.jsonl",
                            "BepInEx/BetterAstralParty-Diagnostics/minimal-v1/helper/events-previous.jsonl",
                            "BepInEx/BetterAstralParty-Diagnostics/minimal-v1/installer/events-current.jsonl",
                            "BepInEx/BetterAstralParty-Diagnostics/minimal-v1/installer/events-previous.jsonl" };
                        string[] minimalIds = { "game/minimal-current", "game/minimal-previous", "game/minimal-helper-current", "game/minimal-helper-previous", "game/minimal-installer-current", "game/minimal-installer-previous" };
                        for (int i = 0; i < minimal.Length; ++i) Read(game, minimal[i], minimalIds[i], now, rows, payloads, ref raw, ref output, identities);
                        string[] paths = { "BepInEx/BetterAstralParty-Diagnostics/current.log", "BepInEx/BetterAstralParty-Diagnostics/previous.log",
                            "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log", "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log",
                            "BetterAstralParty-Compatibility.log", "BetterAstralParty-LaunchError.log", "BepInEx/LogOutput.log" };
                        string[] ids = { "game/diagnostics-current", "game/diagnostics-previous", "game/combat-mismatch", "game/combat-mismatch-previous",
                            "game/compatibility", "game/launch-error", "loader/log-output" };
                        for (int i = 0; i < paths.Length; ++i) Read(game, paths[i], ids[i], now, rows, payloads, ref raw, ref output);
                        try
                        {
                            using (var dll = game.Open("BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll", false, true))
                            {
                                if (dll.Length <= 16 * 1024 * 1024)
                                {
                                    // Metadata only, no assembly load. The lease prevents rename/delete.
                                    string candidate = FileVersionInfo.GetVersionInfo(dll.FullPath).ProductVersion ?? "";
                                    if (Regex.IsMatch(candidate, @"\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}(?:\.[0-9]{1,5}|-(?:rc|beta|alpha)\.[0-9]{1,5})?\z", RegexOptions.CultureInvariant)) version = candidate;
                                }
                            }
                        }
                        catch { rows.Add(new Row { Source = "plugin-version", Status = "unavailable" }); }
                    }
                }
            }
            ReadRecent(localLogs, "local/install-launch", now, rows, payloads, ref raw, ref output);
            ReadRecent(fallbackLogs, "fallback/install-launch", now, rows, payloads, ref raw, ref output);
            using (var unity = TryRoot(unityLogs, rows, "unity-root"))
            {
                if (unity != null)
                {
                    Read(unity, "Player.log", "unity/player", now, rows, payloads, ref raw, ref output);
                    Read(unity, "Player-prev.log", "unity/player-previous", now, rows, payloads, ref raw, ref output);
                }
            }
            byte[] zip;
            using (var memory = new MemoryStream())
            {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    Add(archive, "README.txt", Utf8.GetBytes(Guide));
                    Add(archive, "summary.json", Utf8.GetBytes(Summary(rows, gameValid, version, hostVersion, runningPluginVersion, now, liveHealth, identities)));
                    foreach (var payload in payloads) Add(archive, payload.Row.Entry, payload.Bytes);
                }
                zip = memory.ToArray();
            }
            if (zip.Length > MaxOutputBytes + 256 * 1024) throw new IOException("BAP-DIAGNOSTICS-ZIP-LIMIT");
            string name = "BetterAstralParty-Diagnostics-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip";
            using (var lease = AcquireHub(primaryHub, fallbackHub))
            {
                lease.WriteArchive(name, zip);
                return new BundleResult { ArchivePath = Path.Combine(lease.Path, name), FolderIdentity = lease.Identity,
                    Included = payloads.Count, Omitted = rows.FindAll(r => r.Status != "included").Count };
            }
        }

        // Do not use this convenience string API for shell dispatch. Use AcquireHub/Open instead.
        public static string GetHub(string primary, string fallback)
        {
            using (var lease = AcquireHub(primary, fallback)) return lease.Path;
        }
        public static FolderLease AcquireHub(string primary, string fallback)
        {
            foreach (var path in new[] { primary, fallback })
            {
                FolderLease lease = null;
                try
                {
                    Fence.CreateDirectories(path);
                    lease = new FolderLease(path);
                    lease.Prepare();
                    return lease;
                }
                catch { if (lease != null) lease.Dispose(); }
            }
            throw new IOException("BAP-DIAGNOSTICS-HUB-UNAVAILABLE");
        }
        internal static FolderLease AcquireCommittedFolder(string path, string identity)
        {
            if (String.IsNullOrEmpty(identity)) throw new IOException("BAP-DIAGNOSTICS-FOLDER-IDENTITY-UNKNOWN");
            var lease = new FolderLease(path);
            if (lease.Identity != identity) { lease.Dispose(); throw new IOException("BAP-DIAGNOSTICS-FOLDER-CHANGED"); }
            return lease;
        }
        public sealed class FolderLease : IDisposable
        {
            private Fence root;
            public string Path { get; private set; }
            internal string Identity { get; private set; }
            internal FolderLease(string path) { root = new Fence(path); Path = root.Root; Identity = root.Identity; }
            internal void Prepare()
            {
                root.ProbeWrite();
                try { root.WriteNew("READ-BEFORE-SHARING.txt", Utf8.GetBytes(Guide)); }
                catch (IOException) { using (var existing = root.Open("READ-BEFORE-SHARING.txt", false)) { } }
            }
            internal void WriteArchive(string name, byte[] bytes) { root.Revalidate(); root.WriteNew(name, bytes); }
            public void Open(Action<string> dispatch)
            {
                if (root == null) throw new ObjectDisposedException("FolderLease");
                if (dispatch == null) throw new ArgumentNullException("dispatch");
                root.Revalidate();
                // Keep every ancestor pinned through synchronous dispatch. Explorer may resolve later.
                dispatch(Path);
                root.Revalidate();
            }
            public void Dispose() { if (root != null) { root.Dispose(); root = null; } }
        }
        private static Fence TryRoot(string path, List<Row> rows, string id)
        {
            if (String.IsNullOrWhiteSpace(path)) { rows.Add(new Row { Source = id, Status = "not-selected" }); return null; }
            try { return new Fence(path); }
            catch (FileNotFoundException) { rows.Add(new Row { Source = id, Status = "missing" }); }
            catch (DirectoryNotFoundException) { rows.Add(new Row { Source = id, Status = "missing" }); }
            catch { rows.Add(new Row { Source = id, Status = "unsafe-or-unreadable" }); }
            return null;
        }
        private static void ReadRecent(string path, string id, DateTime now, List<Row> rows, List<Payload> payloads, ref long raw, ref int output)
        {
            using (var root = TryRoot(path, rows, id))
            {
                if (root == null) return;
                var matches = new List<KeyValuePair<string, DateTime>>(); int scanned = 0, rejected = 0;
                try
                {
                    foreach (var candidate in Directory.EnumerateFileSystemEntries(root.Root))
                    {
                        if (++scanned > MaxDirectoryEntries) { rows.Add(new Row { Source = id, Status = "directory-scan-limit" }); break; }
                        string name = Path.GetFileName(candidate);
                        if (!Names.IsMatch(name)) continue;
                        try { using (var file = root.Open(name, false)) matches.Add(new KeyValuePair<string, DateTime>(name, file.Modified)); }
                        catch { rejected++; }
                    }
                }
                catch { rows.Add(new Row { Source = id, Status = "enumeration-unavailable" }); }
                if (rejected > 0) rows.Add(new Row { Source = id, Status = "unsafe-or-locked-candidates", RejectedCandidates = rejected });
                matches.Sort((a, b) => { int date = b.Value.CompareTo(a.Value); return date == 0 ? StringComparer.Ordinal.Compare(a.Key, b.Key) : date; });
                if (matches.Count == 0) rows.Add(new Row { Source = id, Status = "no-matching-files" });
                for (int i = 0; i < Math.Min(matches.Count, MaxRecentPerDirectory); ++i)
                    Read(root, matches[i].Key, id + "/" + (i + 1).ToString("D2", CultureInfo.InvariantCulture), now, rows, payloads, ref raw, ref output);
                if (matches.Count > MaxRecentPerDirectory) rows.Add(new Row { Source = id, Status = "recent-file-limit", Input = matches.Count - MaxRecentPerDirectory });
            }
        }
        private static void Read(Fence root, string relative, string id, DateTime now, List<Row> rows, List<Payload> payloads, ref long raw, ref int totalOutput, MinimalDiagnosticProjection.ProjectionContext identities = null)
        {
            var row = new Row { Source = id }; rows.Add(row);
            try
            {
                using (var file = root.Open(relative, false))
                {
                    row.Input = file.Length; row.Modified = file.Modified.ToString("O", CultureInfo.InvariantCulture);
                    // General engine/loader logs can contain game/chat/auth payloads. Do not read their contents.
                    if (id.StartsWith("unity/", StringComparison.Ordinal) || id == "loader/log-output") { row.Status = "metadata-only"; return; }
                    if (file.Modified < now.AddDays(-RecentDays)) { row.Status = "older-than-7-days"; return; }
                    if (file.Modified > now.AddDays(1)) { row.Status = "invalid-future-time"; return; }
                    if (row.Input > MaxInputFileBytes) { row.Status = "input-file-limit"; return; }
                    if (raw + row.Input > MaxInputBytes || payloads.Count >= MaxFiles || totalOutput >= MaxOutputBytes) { row.Status = "bundle-limit"; return; }
                    raw += row.Input; var bytes = new byte[(int)row.Input]; int used = 0;
                    while (used < bytes.Length) { int n = file.Stream.Read(bytes, used, bytes.Length - used); if (n == 0) break; used += n; }
                    row.ReadBytes = used;
                    row.Changing = used != bytes.Length || file.Stream.Length != row.Input;
                    FileInformation after;
                    if (!GetFileInformationByHandle(file.Stream.SafeFileHandle, out after)
                        || DateTime.FromFileTimeUtc(((long)after.WriteHigh << 32) | after.WriteLow) != file.Modified) row.Changing = true;
                    var afterPath = new StringBuilder(512);
                    if (GetFinalPathNameByHandleW(file.Stream.SafeFileHandle, afterPath, 512, 0) == 0
                        || !String.Equals(afterPath.ToString(), @"\\?\" + file.FullPath, StringComparison.OrdinalIgnoreCase)) row.Changing = true;
                    if (used != bytes.Length) { row.Status = "changed-during-read"; return; }
                    string text;
                    if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) text = new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
                    else
                    {
                        int bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                        text = Utf8.GetString(bytes, bom, bytes.Length - bom);
                    }
                    Array.Clear(bytes, 0, bytes.Length);
                    if (text.IndexOf('\0') >= 0) { row.Status = "invalid-text"; return; }
                    byte[] clean;
                    if (id.StartsWith("game/minimal-", StringComparison.Ordinal))
                    {
                        int accepted, dropped; bool truncated; Dictionary<string, int> reasons;
                        clean = MinimalDiagnosticProjection.ProjectText(text, Math.Min(MaxOutputFileBytes, MaxOutputBytes - totalOutput), identities, out accepted, out dropped, out truncated, out reasons);
                        row.Projected = accepted; row.Dropped = dropped; row.Truncated = truncated; row.Rejections = reasons;
                    }
                    else clean = Sanitize(text, row, Math.Min(MaxOutputFileBytes, MaxOutputBytes - totalOutput));
                    if (clean.Length == 0) { row.Status = row.Truncated ? "output-limit" : "metadata-only-no-allowed-records"; return; }
                    row.Output = clean.Length; row.Entry = "logs/" + id.Replace('/', '-') + ".log"; row.Status = "included";
                    payloads.Add(new Payload(row, clean)); totalOutput += clean.Length;
                }
            }
            catch (FileNotFoundException) { row.Status = "missing"; }
            catch (DirectoryNotFoundException) { row.Status = "missing"; }
            catch (DecoderFallbackException) { row.Status = "invalid-text"; }
            catch (RegexMatchTimeoutException) { row.Status = "filter-timeout"; }
            catch (ArgumentException) { row.Status = "unsafe-or-invalid-text"; }
            catch (UnauthorizedAccessException) { row.Status = "unsafe-or-denied"; }
            catch (IOException) { row.Status = "locked-or-unreadable"; }
            catch { row.Status = "read-failed"; }
        }
        private static readonly Regex GamePayload = Pattern(@"(?i)(?:cardEffect|cardCounter|koMinimum\.content|\b(?:card|cards|hand|deck|chat|messageText|heroId|passives|buffId|playerName)\b)");
        private static readonly Regex RecordTime = Pattern(@"\A(?<time>\d{4}-\d{2}-\d{2}T[^|]{8,45}) \| (?<body>.*)\z");
        private static readonly Regex CombatPrefix = Pattern(@"\Acombat=[0-9a-f]{12}-[0-9]{1,6}; ");
        private static readonly Regex Number = Pattern(@"\A-?[0-9]{1,7}(?:\.[0-9]{1,6})?\z");
        private static readonly Regex Version = Pattern(@"\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}(?:\.[0-9]{1,5}|-(?:rc|beta|alpha)\.[0-9]{1,5})?\z");
        private static readonly string[] ErrorTypes = { "System.NullReferenceException", "System.InvalidOperationException", "System.InvalidCastException", "System.ArgumentException", "System.ArgumentNullException", "System.IO.IOException", "System.IO.FileNotFoundException", "System.IO.DirectoryNotFoundException", "System.UnauthorizedAccessException", "System.TypeLoadException", "System.MissingFieldException", "System.MissingMethodException", "System.TimeoutException", "System.Security.SecurityException", "System.NotSupportedException", "System.ObjectDisposedException" };
        private static readonly string[] Actions = { "Close", "Features", "General", "Language", "Diagnostics", "DiagnosticsOpen", "DiagnosticsCollect", "MuteUnfocused", "InputAttention", "UpdateAuth", "UpdateSignOut", "UpdateChannel", "UpdateCheck", "UpdateDownload", "AutoDownload", "AfterExit", "UpdateGet", "UpdateCancel", "Enabled", "Details", "KoMinimum", "Names", "CardPopups", "BattleStatus", "ShushuShield", "FieldBuffs", "HandLayout", "ScaleMinus", "ScalePlus", "OpacityMinus", "OpacityPlus" };
        private static bool In(string value, string[] values) { return Array.IndexOf(values, value) >= 0; }
        private static string Fields(string eventName, string body, string numericKeys, string booleanKeys)
        {
            var result = new StringBuilder(eventName);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in body.Split(';'))
            {
                int equal = part.IndexOf('='); if (equal < 1) continue;
                string key = part.Substring(0, equal).Trim(), value = part.Substring(equal + 1).Trim();
                if (!seen.Add(key)) continue;
                bool b;
                if (In(key, numericKeys.Split(',')) && Number.IsMatch(value)) result.Append("; ").Append(key).Append('=').Append(value);
                else if (In(key, booleanKeys.Split(',')) && Boolean.TryParse(value, out b)) result.Append("; ").Append(key).Append('=').Append(b ? "true" : "false");
            }
            return seen.Count > 0 && result.Length > eventName.Length ? result.ToString() : null;
        }
        private static string Project(string line, string source)
        {
            // No unknown free text is exported. Values are parsed and reserialized from fixed fields.
            string time = ""; var timed = RecordTime.Match(line);
            if (timed.Success)
            {
                DateTimeOffset stamp;
                if (!DateTimeOffset.TryParse(timed.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp)) return null;
                time = stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + " | ";
                line = timed.Groups["body"].Value;
            }
            if (line.StartsWith("latest ", StringComparison.Ordinal)) { time = ""; line = line.Substring(7); }
            line = CombatPrefix.Replace(line, "");
            string projected = null;
            bool mod = source.StartsWith("game/diagnostics-", StringComparison.Ordinal) || source.StartsWith("game/combat-mismatch", StringComparison.Ordinal);
            if (mod)
            {
                foreach (var literal in new[] { "patches begin", "patches complete", "home menu create begin", "home menu create complete", "battle status create complete", "nativeCrash scan complete" })
                    if (line == literal) projected = literal;
                foreach (var state in new[] { "ON", "OFF", "CONTINUE" })
                    if (line == "diagnostics " + state || line.StartsWith("diagnostics " + state + " | ", StringComparison.Ordinal))
                    {
                        projected = "diagnostics=" + state;
                        var logged = Regex.Match(line, @"\bmod=([^;]{1,40})(?:;|$)", RegexOptions.CultureInvariant);
                        if (logged.Success && Version.IsMatch(logged.Groups[1].Value)) projected += "; loggedModVersion=" + logged.Groups[1].Value;
                    }
                foreach (var gate in new[] { "home=visible", "home=hidden", "settingsPanel=open", "settingsPanel=closed", "pvpSafety=active", "pvpSafety=initializing", "pvpSafety=suspended", "fightReady=ready", "fightReady=waiting-content", "fightReady=waiting-step", "fightStep=hidden", "field.gate=scan", "field.focus=True", "field.focus=False", "audio.focusMute=True", "audio.focusMute=False", "inputAttention=waiting", "inputAttention=stopped" })
                    if (line == gate) projected = gate;
                if (Regex.IsMatch(line, @"\AfightStep=(?:[0-9]|1[0-9])\z")) projected = line;
                foreach (var phase in new[] { "begin", "complete" })
                    foreach (var action in Actions)
                        if (line == "settingsAction " + phase + "=" + action) projected = "settingsAction; phase=" + phase + "; action=" + action;
                if (line.StartsWith("performance ", StringComparison.Ordinal)) projected = Fields("performance", line.Substring(12), "frames,ui_avg_ms,ui_max_ms,gameui_max_ms,popup_max_ms,status_max_ms,managed_bytes_per_frame,game_frame_max_ms,game_frames_ge50ms", "");
                if (line.StartsWith("performance_field ", StringComparison.Ordinal)) projected = Fields("performance_field", line.Substring(18), "tick_avg_ms,tick_max_ms,plates_max,effects_max", "native_layout_excluded");
                if (line.StartsWith("performance_hand ", StringComparison.Ordinal)) projected = Fields("performance_hand", line.Substring(17), "tick_avg_ms,tick_max_ms,managed_bytes_per_frame", "native_layout_excluded");
                if (line.StartsWith("combat.inputs=", StringComparison.Ordinal)) projected = Fields("public-inputs", line.Substring(14), "hp,attackMin,attackMax,defenseMin,defenseMax", "");
                if (line.StartsWith("combatVisible=", StringComparison.Ordinal)) projected = Fields("public-visible", line.Substring(14), "step,defender_hp,attacker_hp,attack_point,defense_point", "");
                if (line.StartsWith("phase=", StringComparison.Ordinal)) projected = Fields("public-phase", line, "phase", "");
                if (line.StartsWith("begin ", StringComparison.Ordinal)) projected = Fields("public-combat-begin", line.Substring(6), "role", "partial");
                if (line.StartsWith("prediction ", StringComparison.Ordinal)) projected = Fields("public-prediction", line.Substring(11), "before,atk,def,attackDie,damage", "dodge");
                if (line.StartsWith("damage.result ", StringComparison.Ordinal)) projected = Fields("public-hp-result", line.Substring(14), "before,predictedDamage,expectedHP,observedHP,expectedDisplayHP", "monsterDefeatPresentation,hpMismatch,overkillUnverifiable");
                if (line.StartsWith("settings=", StringComparison.Ordinal)) projected = Fields("settings-subset", line.Substring(9), "scale,opacity", "advice,details,koMinimum,names,cardPopups,battleStatus,shushuShield,fieldBuffs,muteUnfocused");
            }
            else if (source == "game/compatibility")
            {
                if (line == "Allowed: True") projected = "compatibility; allowed=true";
                if (line == "Allowed: False") projected = "compatibility; allowed=false";
                foreach (var role in new[] { "game", "loader", "catalog", "interop" }) if (line.StartsWith("Changed [" + role + "]:", StringComparison.Ordinal)) projected = "compatibility; changedRole=" + role;
            }
            else // Installer/launcher sources: fixed markers and strict metadata fields only.
            {
                foreach (var code in new[] { "BAP-LAUNCH-FAILED", "BAP-INSTALL-FAILED" }) if (line == code || line == "Code: " + code) projected = "failure; code=" + code;
                if (line.StartsWith("Version: ", StringComparison.Ordinal) && Version.IsMatch(line.Substring(9))) projected = "failure; version=" + line.Substring(9);
                if (line.StartsWith("ErrorType: ", StringComparison.Ordinal) && In(line.Substring(11), ErrorTypes)) projected = "failure; errorType=" + line.Substring(11);
                foreach (var stage in new[] { "Compatibility validation", "File transaction", "Helper bootstrap", "Helper preparation", "Steam shortcut", "Uninstallation", "Preflight", "Bootstrap", "Backup", "Rollback", "Installation" })
                    if (line == "Stage: " + stage) projected = "failure; stage=" + stage.Replace(' ', '-');
            }
            return projected == null ? null : time + projected;
        }
        private sealed class LegacyError
        {
            internal string Time, Type, Class, Feature, LegacyFeature, Site = "Unknown";
            internal int Frames;
            internal string Render() { return Time + "managed-error; type=" + Type + "; error_type=" + Class
                + "; feature=" + Feature + "; legacy_feature=" + LegacyFeature + "; error_site=" + Site; }
        }
        private static LegacyError ParseError(string line, string source)
        {
            if (!(source.StartsWith("game/diagnostics-", StringComparison.Ordinal) || source.StartsWith("game/combat-mismatch", StringComparison.Ordinal))) return null;
            string time = ""; var timed = RecordTime.Match(line);
            if (timed.Success)
            {
                DateTimeOffset stamp;
                if (!DateTimeOffset.TryParse(timed.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp)) return null;
                time = stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + " | ";
                line = timed.Groups["body"].Value;
            }
            if (line.StartsWith("latest ", StringComparison.Ordinal)) { time = ""; line = line.Substring(7); }
            line = CombatPrefix.Replace(line, "");
            if (!line.StartsWith("error.", StringComparison.Ordinal)) return null;
            int eq = line.IndexOf('='); if (eq < 7) return null;
            // Raw labels/type names are lookup keys only, never export values.
            string label = line.Substring(6, eq - 6), rawType = line.Substring(eq + 1);
            string type = "Unknown", errorClass = "Unknown", feature = "Unknown", legacy = "Unknown";
            if (In(rawType, ErrorTypes)) { type = rawType; errorClass = rawType.Substring(rawType.LastIndexOf('.') + 1); }
            if (errorClass == "ArgumentNullException") errorClass = "ArgumentException";
            foreach (var pair in new[] {
                "System.Threading.Tasks.TaskCanceledException|OperationCanceledException", "System.OperationCanceledException|OperationCanceledException",
                "System.FormatException|FormatException", "System.AggregateException|AggregateException", "System.Net.Http.HttpRequestException|HttpRequestException",
                "BetterAstralParty.Updating.UpdateValidationException|UpdateValidationException", "BetterAstralParty.Updating.ApplySafetyException|ApplySafetyException",
                "BetterAstralParty.AutomaticUpdateException|AutomaticUpdateException", "BetterAstralParty.ReleaseInputRejectedException|ReleaseInputRejectedException",
                "BetterAstralParty.ReleaseUpdates+CheckFailure|ReleaseMetadataFailure", "BetterAstralParty.Updating.UpdateDownloads+DownloadFailure|DownloadFailure" })
            {
                var parts = pair.Split('|'); if (rawType == parts[0]) { errorClass = parts[1]; type = errorClass; }
            }
            foreach (var pair in new[] {
                "shushuShield|PublicCombat|ShushuShield", "status|PublicCombat|BattleStatus", "bridge|PublicCombat|Bridge",
                "ModUi|CoreUi|ModUi", "SettingsInputTrace|Settings|SettingsInputTrace", "GameUi|CoreUi|GameUi",
                "FieldBuff.inspect|PublicCombat|FieldBuffInspect", "FieldNickname.font|CoreUi|FieldNameFont", "FocusAudio|CoreUi|FocusAudio",
                "combatSnapshot|PublicCombat|CombatSnapshot", "UpdateNotification.cleanup|Notices|UpdateNotificationCleanup",
                "PvpSafety|PublicCombat|PvpSafety", "PvpSafety.cleanup|PublicCombat|PvpSafetyCleanup",
                "nativeCrashScan|Loader|NativeCrashScan", "patches|Loader|Patches", "CardDiceUi.Final|PublicCombat|CardDiceFinal",
                "RemainingHpUi|PublicCombat|RemainingHp", "DamageUi.Animate|PublicCombat|DamageAnimate", "DamageUi.PollFinalPoints|PublicCombat|DamagePoll" })
            {
                var parts = pair.Split('|'); if (label == parts[0]) { feature = parts[1]; legacy = parts[2]; }
            }
            if (label.StartsWith("settingsAction.", StringComparison.Ordinal) && In(label.Substring(15), Actions)) { feature = "Settings"; legacy = "SettingsAction"; }
            // Exact keys audited from Compatibility.Block callsites/constants, not implementation type names.
            foreach (var code in new[] {
                "ShushuShield|PublicCombat|CompatibilityShushuShield",
                "BattleStatus|PublicCombat|CompatibilityBattleStatus",
                "FieldBuffs|PublicCombat|CompatibilityFieldBuffs",
                "Enabled|PublicCombat|CompatibilityEnabled",
                "HandLayout|HandLayout|CompatibilityHandLayout",
                "CardPopups|CoreUi|CompatibilityCardPopups",
                "CardTargetButtons|CoreUi|CompatibilityCardTargetButtons",
                "CharacterGuide|CoreUi|CompatibilityCharacterGuide",
                "Names|CoreUi|CompatibilityNames",
                "MuteUnfocused|CoreUi|CompatibilityMuteUnfocused",
                "InputAttention|CoreUi|CompatibilityInputAttention",
                "PingFocus|CoreUi|CompatibilityPingFocus",
                "CoreUi|CoreUi|CompatibilityCoreUi",
                "UpdateNotification|Notices|CompatibilityUpdateNotification",
                "Notices|Notices|CompatibilityNotices"
            })
            {
                var parts = code.Split('|');
                if (label == "Compatibility." + parts[0]) { feature = parts[1]; legacy = parts[2]; }
            }
            return new LegacyError { Time = time, Type = type, Class = errorClass, Feature = feature, LegacyFeature = legacy };
        }
        private static string ErrorSite(string frame)
        {
            // Match only exact owned anchors. No stack, method parameters, files or line numbers leave this method.
            var match = Regex.Match(frame, @"\A\s*at BetterAstralParty\.(?<owner>[A-Za-z.]+)\.(?<method>[A-Za-z]+)\(", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            if (!match.Success) return "Unknown";
            string owner = match.Groups["owner"].Value, method = match.Groups["method"].Value;
            foreach (var anchor in new[] { "Plugin|Load|PluginLoad", "Plugin|LoadCore|PluginLoad", "Plugin|RecordSettings|PluginSettings",
                "ModUi|Tick|ModUiTick", "ModUi|Apply|ModUiApply", "ModUi|ApplyCore|ModUiApply", "CardUi|Update|CardUiUpdate", "CardDiceUi|Show|CardDiceShow",
                "ReleaseUpdates|ReadAsync|ReleaseMetadata", "ReleaseUpdates|ReadPageAsync|ReleaseMetadata", "Updating.UpdateDownloads|Read|UpdateDownload", "Updating.UpdateDownloads|Asset|UpdateDownload",
                "AutomaticUpdates|Tick|CoordinatorTick", "PrivateReleaseSession|Poll|CredentialPoll", "PrivateReleaseSession|PollCore|CredentialPoll",
                "Updating.UpdateHelperService|Run|HelperRun", "Updating.UpdateHelperService|Recover|HelperRecover", "Updating.UpdateHelperService|Failed|HelperRecover",
                "Updating.UpdateTransaction|Apply|TransactionApply", "Updating.UpdateTransaction|Recover|TransactionRecover",
                "RuntimeObject|Get|RuntimeRead", "RuntimeObject|Field|RuntimeRead", "RuntimeObject|Value|RuntimeRead", "RuntimeObject|Call|RuntimeCall" })
            { var parts = anchor.Split('|'); if (owner == parts[0] && method == parts[1]) return parts[2]; }
            return owner == "NativeUi" ? "NativeUi" : "Unknown";
        }
        private static bool AppendRecord(StringBuilder result, string clean, Row row, int max, ref int count)
        {
            foreach (char c in clean) if (Char.IsControl(c) && c != '\t') throw new ArgumentException("BAP-DIAGNOSTICS-CONTROL-TEXT");
            int next = Utf8.GetByteCount(clean) + 1;
            if (count + next > max) { row.Truncated = true; return false; }
            result.Append(clean).Append('\n'); count += next; row.Projected++; return true;
        }
        private static byte[] Sanitize(string text, Row row, int max)
        {
            var result = new StringBuilder(); int count = 0; bool continuation = false, keyBlock = false;
            LegacyError pending = null;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string clean = null;
                    if (reader.Peek() == -1 && !text.EndsWith("\n", StringComparison.Ordinal) && !text.EndsWith("\r", StringComparison.Ordinal)) { row.Dropped++; break; }
                    if (line.Length > 16384) { row.Redacted++; row.Dropped++; continuation = true; continue; }
                    line = Ansi.Replace(line.Normalize(NormalizationForm.FormKC), "");
                    bool boundary = Boundary.IsMatch(line);
                    if (line.IndexOf("-----BEGIN", StringComparison.OrdinalIgnoreCase) >= 0 && Secret.IsMatch(line)) keyBlock = true;
                    var error = keyBlock ? null : ParseError(line, row.Source);
                    if (pending != null)
                    {
                        if (boundary || error != null || line.Length == 0 || keyBlock)
                        {
                            if (!AppendRecord(result, pending.Render(), row, max, ref count)) break;
                            pending = null;
                        }
                        else
                        {
                            if (++pending.Frames <= 16 && pending.Site == "Unknown") pending.Site = ErrorSite(line);
                            row.Dropped++; continue;
                        }
                    }
                    if (line.IndexOf("-----BEGIN", StringComparison.OrdinalIgnoreCase) >= 0 && Secret.IsMatch(line)) keyBlock = true;
                    if (keyBlock)
                    {
                        row.Redacted++;
                        if (line.IndexOf("-----END", StringComparison.OrdinalIgnoreCase) >= 0) { keyBlock = false; continuation = true; }
                    }
                    else if (error != null)
                    {
                        // Preserve occurrence even for an unknown/sensitive raw typename. Only fixed codes are rendered.
                        pending = error; continuation = false; continue;
                    }
                    else
                    {
                        if (boundary) continuation = false;
                        if (Secret.IsMatch(line) || Private.IsMatch(line) || GamePayload.IsMatch(line)) { row.Redacted++; continuation = true; }
                        else if (continuation && line.Length != 0) row.Redacted++;
                        else clean = Project(line, row.Source);
                    }
                    if (clean == null) { row.Dropped++; continue; }
                    if (!AppendRecord(result, clean, row, max, ref count)) break;
                }
            }
            if (pending != null && !row.Truncated) AppendRecord(result, pending.Render(), row, max, ref count);
            return Utf8.GetBytes(result.ToString());
        }
        private static void Add(ZipArchive zip, string name, byte[] bytes)
        {
            using (var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) stream.Write(bytes, 0, bytes.Length);
        }
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }
        private static string Summary(List<Row> rows, bool gameValid, string fileProductVersion, string hostVersion, string runningPluginVersion, DateTime now, LiveDiagnosticHealth liveHealth, MinimalDiagnosticProjection.ProjectionContext identities)
        {
            string host = Regex.IsMatch(hostVersion ?? "", @"\A[0-9]{1,5}(?:\.[0-9]{1,5}){1,3}\z") ? hostVersion : "unknown";
            string plugin = Version.IsMatch(runningPluginVersion ?? "") ? runningPluginVersion : "unknown";
            var json = new StringBuilder("{\n\"schema\":1,\"collectorVersion\":\"2\",\"createdUtc\":").Append(Quote(now.ToString("O", CultureInfo.InvariantCulture)));
            json.Append(",\"environment\":{\"osVersion\":").Append(Quote(Environment.OSVersion.Version.ToString()))
                .Append(",\"runtimeVersion\":").Append(Quote(Environment.Version.ToString())).Append(",\"hostVersion\":").Append(Quote(host))
                .Append(",\"os64Bit\":").Append(Environment.Is64BitOperatingSystem ? "true" : "false")
                .Append(",\"process64Bit\":").Append(Environment.Is64BitProcess ? "true" : "false")
                .Append(",\"gameRootValidated\":").Append(gameValid ? "true" : "false").Append(",\"pluginVersion\":").Append(Quote(plugin))
                .Append(",\"pluginVersionSource\":").Append(Quote(plugin == "unknown" ? "unknown" : "running-plugin"))
                .Append(",\"pluginFileProductVersion\":").Append(Quote(fileProductVersion)).Append("},\n")
                .Append("\"limits\":{\"recentDays\":7,\"maxLogs\":24,\"inputFileBytes\":2097152,\"inputTotalBytes\":8388608,\"outputFileBytes\":524288,\"outputTotalBytes\":4194304},\n")
                .Append("\"privacy\":{\"configurationIncluded\":false,\"rawDumpsIncluded\":false,\"credentialStoresAccessed\":false,\"gamePayloadIncluded\":false,\"uploaded\":false,\"completeAnonymisation\":false},\n")
                .Append("\"structuredProjection\":{\"schema\":\"bap.minimal-diagnostic/v1\",\"identities\":\"bundle-local-aliases\",\"maxSessionAliases\":64,\"maxOperationAliases\":512,\"unknownEvents\":\"omitted-and-counted-by-fixed-reason\",\"snapshotsAreNewEvents\":false},\n")
                .Append("\"scope\":{\"writerHealth\":\"unknown\",\"requestedRecording\":\"unknown\",\"sessionCorrelation\":\"not-reconstructed\",\"crossSourceOrder\":\"unknown\",\"combatVerification\":\"public-hp-comparison-only\",\"missingMeans\":\"not-observed\",\"engineLogs\":\"metadata-only\"},\n\"files\":[");
            // Only a live owner snapshot can provide current writer health; historical logs cannot.
            json.Length -= "\"files\":[".Length;
            json.Append("\"liveHostHealth\":{\"runtime\":")
                .Append(gameValid && liveHealth != null ? liveHealth.Json(identities) : LiveDiagnosticHealth.Unavailable)
                .Append(",\"helper\":{\"availability\":\"unavailable\",\"reason\":\"separate-process\"},\"installer\":{\"availability\":\"unavailable\",\"reason\":\"separate-process\"}},\n\"files\":[");
            for (int i = 0; i < rows.Count; ++i)
            {
                var row = rows[i]; if (i > 0) json.Append(',');
                json.Append("\n{\"source\":").Append(Quote(row.Source)).Append(",\"status\":").Append(Quote(row.Status))
                    .Append(",\"zipEntry\":").Append(Quote(row.Entry)).Append(",\"modifiedUtc\":").Append(Quote(row.Modified))
                    .Append(",\"inputBytes\":").Append(row.Input.ToString(CultureInfo.InvariantCulture)).Append(",\"outputBytes\":").Append(row.Output.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"readBytes\":").Append(row.ReadBytes.ToString(CultureInfo.InvariantCulture)).Append(",\"rejectedCandidates\":").Append(row.RejectedCandidates.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"redactedLines\":").Append(row.Redacted.ToString(CultureInfo.InvariantCulture)).Append(",\"truncated\":").Append(row.Truncated ? "true" : "false")
                    .Append(",\"droppedLines\":").Append(row.Dropped.ToString(CultureInfo.InvariantCulture)).Append(",\"projectedRecords\":").Append(row.Projected.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"changingSnapshot\":").Append(row.Changing ? "true" : "false");
                json.Append(",\"projectionRejections\":{"); bool firstReason = true;
                if (row.Rejections != null) foreach (var reason in row.Rejections)
                { if (!firstReason) json.Append(','); firstReason = false; json.Append(Quote(reason.Key)).Append(':').Append(reason.Value.ToString(CultureInfo.InvariantCulture)); }
                json.Append("}}");
            }
            json.Append("],\n\"missingOrOmitted\":["); bool first = true;
            foreach (var row in rows)
            {
                if (row.Status == "included") continue;
                if (!first) json.Append(','); first = false;
                json.Append("{\"source\":").Append(Quote(row.Source)).Append(",\"reason\":").Append(Quote(row.Status)).Append('}');
            }
            return json.Append("]\n}\n").ToString();
        }

        // Pin all ancestors against deletion; final components are opened without following links.
        private sealed class Fence : IDisposable
        {
            internal string Root; private readonly List<SafeFileHandle> parents = new List<SafeFileHandle>();
            internal string Identity;
            internal Fence(string path)
            {
                Root = Canonical(path);
                try { Pin(Root, parents); Identity = HandleIdentity(parents[parents.Count - 1]); }
                catch { Dispose(); throw; }
            }
            private static string HandleIdentity(SafeFileHandle handle)
            {
                FileInformation info;
                if (!GetFileInformationByHandle(handle, out info)) throw new IOException("BAP-DIAGNOSTICS-HANDLE-INFO");
                return info.Volume.ToString("X8") + info.IndexHigh.ToString("X8") + info.IndexLow.ToString("X8");
            }
            internal void Revalidate()
            {
                if (parents.Count == 0) throw new ObjectDisposedException("Fence");
                using (var current = OpenHandle(Root, true, 1, 3, 0))
                    if (HandleIdentity(current) != Identity) throw new IOException("BAP-DIAGNOSTICS-FOLDER-CHANGED");
            }
            internal static string Canonical(string path)
            {
                if (String.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"\A[A-Za-z]:[\\/]") || path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("BAP-DIAGNOSTICS-UNSAFE-PATH");
                foreach (var part in path.Substring(3).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                    if (part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(new[] { ':', '<', '>', '"', '|', '?', '*' }) >= 0) throw new ArgumentException("BAP-DIAGNOSTICS-UNSAFE-PATH");
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                if (full.Length < 4 || full.Length > 230) throw new ArgumentException("BAP-DIAGNOSTICS-UNSAFE-PATH");
                return full;
            }
            private static void Pin(string path, List<SafeFileHandle> handles)
            {
                var dirs = new List<string>(); string p = path;
                while (!String.IsNullOrEmpty(p)) { dirs.Add(p); p = Path.GetDirectoryName(p); }
                // FILE_LIST_DIRECTORY is necessary: an access-zero handle does not enforce delete sharing.
                dirs.Reverse(); foreach (var dir in dirs) handles.Add(OpenHandle(dir, true, 1, 3, 0));
            }
            internal Opened Open(string relative, bool directory, bool pinFile = false)
            {
                if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 || relative.IndexOf('\\') >= 0) throw new ArgumentException("BAP-DIAGNOSTICS-UNSAFE-RELATIVE");
                foreach (string part in relative.Split('/')) if (String.IsNullOrEmpty(part) || part == "." || part == "..") throw new ArgumentException("BAP-DIAGNOSTICS-UNSAFE-RELATIVE");
                string full = Canonical(Path.Combine(Root, relative.Replace('/', '\\')));
                if (!full.StartsWith(Root + "\\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("BAP-DIAGNOSTICS-ESCAPE");
                var pinned = new List<SafeFileHandle>();
                try
                {
                    string parent = Path.GetDirectoryName(full);
                    if (!String.Equals(parent, Root, StringComparison.OrdinalIgnoreCase)) Pin(parent, pinned);
                    var handle = OpenHandle(full, directory, directory ? 1u : 0x80000000u, 3, 0, !directory && !pinFile);
                    return new Opened(handle, full, directory, pinned);
                }
                catch { foreach (var h in pinned) h.Dispose(); throw; }
            }
            internal static void CreateDirectories(string path)
            {
                path = Canonical(path); var dirs = new List<string>(); string p = path;
                while (!Directory.Exists(p)) { dirs.Add(p); p = Path.GetDirectoryName(p); if (String.IsNullOrEmpty(p)) throw new IOException("BAP-DIAGNOSTICS-ROOT-MISSING"); }
                var handles = new List<SafeFileHandle>();
                try { Pin(p, handles); dirs.Reverse(); foreach (var dir in dirs) { Directory.CreateDirectory(dir); handles.Add(OpenHandle(dir, true, 1, 3, 0)); } }
                finally { foreach (var h in handles) h.Dispose(); }
            }
            internal void ProbeWrite()
            {
                using (var handle = OpenHandle(Path.Combine(Root, "probe-" + Guid.NewGuid().ToString("N") + ".partial"), false, 0x40010000u, 1, 0x04000000u)) { }
            }
            internal void WriteNew(string name, byte[] bytes)
            {
                if (!Regex.IsMatch(name, @"\A[A-Za-z0-9.-]+\z")) throw new ArgumentException("BAP-DIAGNOSTICS-OUTPUT-NAME");
                string target = Path.Combine(Root, name), pending = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".partial");
                using (var handle = OpenHandle(pending, false, 0x40010000u, 1, 0))
                using (var stream = new FileStream(handle, FileAccess.Write, 16384, false))
                {
                    try { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); Rename(handle, target); }
                    catch { DeleteOpened(handle); throw; }
                }
            }
            public void Dispose() { for (int i = parents.Count - 1; i >= 0; --i) parents[i].Dispose(); parents.Clear(); }
        }
        private sealed class Opened : IDisposable
        {
            internal FileStream Stream; internal string FullPath; internal long Length; internal DateTime Modified;
            private readonly SafeFileHandle handle; private readonly List<SafeFileHandle> parents;
            internal Opened(SafeFileHandle opened, string path, bool directory, List<SafeFileHandle> pinned)
            {
                handle = opened; FullPath = path; parents = pinned;
                try
                {
                    FileInformation info; if (!GetFileInformationByHandle(handle, out info)) throw new IOException("BAP-DIAGNOSTICS-HANDLE-INFO");
                    Length = ((long)info.SizeHigh << 32) | info.SizeLow; Modified = DateTime.FromFileTimeUtc(((long)info.WriteHigh << 32) | info.WriteLow);
                    Stream = directory ? null : new FileStream(handle, FileAccess.Read, 16384, false);
                }
                catch { handle.Dispose(); throw; }
            }
            public void Dispose() { if (Stream != null) Stream.Dispose(); handle.Dispose(); foreach (var p in parents) p.Dispose(); }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        private static SafeFileHandle OpenHandle(string path, bool directory, uint access, uint disposition, uint extra, bool allowRotation = false)
        {
            // Source file identity belongs to this handle; deletion sharing permits logger rotation.
            // Ancestor/output/version-resource handles stay pinned without delete sharing.
            SafeFileHandle handle = CreateFileW(path, access, allowRotation ? 7u : 3u, IntPtr.Zero, disposition, 0x00200000u | (directory ? 0x02000000u : 0u) | extra, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error(); handle.Dispose();
                if (error == 2) throw new FileNotFoundException("BAP-DIAGNOSTICS-MISSING");
                if (error == 3) throw new DirectoryNotFoundException("BAP-DIAGNOSTICS-MISSING");
                if (error == 5) throw new UnauthorizedAccessException("BAP-DIAGNOSTICS-DENIED");
                throw new IOException("BAP-DIAGNOSTICS-OPEN-FAILED");
            }
            try
            {
                FileInformation info; var actual = new StringBuilder(512); uint size = GetFinalPathNameByHandleW(handle, actual, 512, 0);
                if (!GetFileInformationByHandle(handle, out info) || (info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory
                    || (!directory && info.Links != 1) || size == 0 || size >= 512 || !String.Equals(actual.ToString(), @"\\?\" + path, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("BAP-DIAGNOSTICS-LINK-OR-IDENTITY");
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
        private static void Rename(SafeFileHandle handle, string target)
        {
            int lengthOffset = IntPtr.Size == 8 ? 16 : 8, nameOffset = lengthOffset + 4; byte[] name = Encoding.Unicode.GetBytes(target);
            // Win32's rename buffer needs a terminating WCHAR as well as the byte length.
            int size = nameOffset + name.Length + 2;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; ++i) Marshal.WriteByte(buffer, i, 0);
                Marshal.WriteInt32(buffer, lengthOffset, name.Length); Marshal.Copy(name, 0, IntPtr.Add(buffer, nameOffset), name.Length);
                if (!SetFileInformationByHandle(handle, 3, buffer, (uint)size)) throw new IOException("BAP-DIAGNOSTICS-COMMIT-FAILED");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        private static void DeleteOpened(SafeFileHandle handle)
        {
            IntPtr buffer = Marshal.AllocHGlobal(4);
            try { Marshal.WriteInt32(buffer, 1); SetFileInformationByHandle(handle, 4, buffer, 4); }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder name, uint length, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, IntPtr information, uint size);
    }
}
