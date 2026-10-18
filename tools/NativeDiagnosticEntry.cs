using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    // Diagnostics must also work when installation receipts or game files are missing.
    internal static class NativeDiagnosticEntry
    {
        internal sealed class Options
        {
            internal string Action = "", GameRoot = "", Language = "ko";
            internal bool NoOpen;
        }
        internal static Options Parse(string[] args)
        {
            if (args.Length < 2 || args.Length > 9 || args[0] != "--diagnostics") throw new ArgumentException("Invalid diagnostics arguments.");
            var result = new Options { Action = args[1] };
            if (result.Action != "collect" && result.Action != "open" && result.Action != "menu") throw new ArgumentException("Invalid diagnostics action.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 2; i < args.Length; i++) {
                var name = args[i]; if (!seen.Add(name)) throw new ArgumentException("Duplicate diagnostics argument.");
                if (name == "--no-open") { result.NoOpen = true; continue; }
                if ((name != "--game-root" && name != "--language") || ++i == args.Length) throw new ArgumentException("Invalid diagnostics argument.");
                if (name == "--game-root") { if (args[i].Length > WindowsPath.MaximumLength) throw new ArgumentException("Game path exceeds limit."); result.GameRoot = args[i]; }
                else { if (args[i] != "ko" && args[i] != "en") throw new ArgumentException("Invalid diagnostics language."); result.Language = args[i]; }
            }
            return result;
        }
        internal static string ResolveRoot(string selected, string deployment)
        {
            var candidates = selected.Length != 0 ? new[] { selected, Path.Combine(selected, "8vJXnINT") }
                : new[] { deployment, Path.Combine(deployment, "8vJXnINT"), WindowsPath.Parent(deployment), Path.Combine(WindowsPath.Parent(deployment), "8vJXnINT") };
            foreach (var candidate in candidates) try {
                var root = WindowsPath.Absolute(candidate);
                if (File.Exists(WindowsPath.ForIo(Path.Combine(root, "AstralParty_INT.exe")))
                    && Directory.Exists(WindowsPath.ForIo(Path.Combine(root, "AstralParty_INT_Data")))) return root;
            } catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException) { }
            if (selected.Length != 0) {
                try {
                    var root = WindowsPath.Absolute(selected);
                    var child = Path.Combine(root, "8vJXnINT");
                    return !Path.GetFileName(root.TrimEnd('\\')).Equals("8vJXnINT", StringComparison.OrdinalIgnoreCase) && Directory.Exists(WindowsPath.ForIo(child)) ? child : root;
                } catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException) { return ""; }
            }
            foreach (var candidate in candidates) try {
                var root = WindowsPath.Absolute(candidate);
                if (Path.GetFileName(root.TrimEnd('\\')).Equals("8vJXnINT", StringComparison.OrdinalIgnoreCase) && Directory.Exists(WindowsPath.ForIo(root))) return root;
            } catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException) { }
            return "";
        }
        internal static int Run(string[] args)
        {
            NativeMaintenanceEntry.ConsoleReady(); var language = "ko";
            Action<string, string> say = (ko, en) => Console.WriteLine(language == "ko" ? ko : en);
            try {
                var options = Parse(args); language = options.Language;
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var temporary = Path.GetTempPath();
                var primary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BetterAstralParty-Diagnostics");
                var fallback = Path.Combine(local, "BetterAstralParty", "Diagnostics");
                Action<string> open = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                if (options.Action == "menu") {
                    var choice = NativeMaintenanceEntry.Consent(language == "ko" ? "1: 진단 ZIP 만들기  2: 진단 폴더 열기  Enter: 종료" : "1: Create diagnostics ZIP  2: Open diagnostics folder  Enter: Exit");
                    if (choice == "1") options.Action = "collect"; else if (choice == "2") options.Action = "open"; else return 0;
                }
                if (options.Action == "open") {
                    using (var hub = DiagnosticBundle.AcquireHub(primary, fallback)) {
                        say("진단 폴더: " + hub.Path, "Diagnostics folder: " + hub.Path);
                        say("Create-Diagnostics.cmd로 ZIP을 만든 뒤 ZIP 안의 로그를 볼 수 있습니다.", "Create a ZIP with Create-Diagnostics.cmd, then open its logs.");
                        if (!options.NoOpen) hub.Open(open);
                    }
                    return 0;
                }
                var deployment = WindowsPath.FromIo(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\');
                if (Path.GetFileName(deployment).Equals("artifacts", StringComparison.OrdinalIgnoreCase)) deployment = WindowsPath.Parent(deployment);
                var game = ResolveRoot(options.GameRoot, deployment);
                if (game.Length == 0 && options.GameRoot.Length == 0 && !Console.IsInputRedirected) {
                    var selected = NativeMaintenanceEntry.Consent(language == "ko" ? "Steam의 설치 폴더 경로를 입력하세요. 비워 두면 설치·런처·Unity 로그만 수집합니다." : "Enter the INT installation folder from Steam. Leave blank for install/launcher/Unity logs only.");
                    if (!string.IsNullOrWhiteSpace(selected)) game = ResolveRoot(selected!, deployment);
                }
                say("최근 오류 로그, 게임·BepInEx 로그의 오류 줄, 게임 튕김 기록을 수집합니다. 토큰·개인 경로는 가리고 카드·손패·채팅·덤프·전체 설정은 제외합니다.", "Collecting recent error logs, error lines from game/BepInEx logs and game crash records. Tokens/private paths are redacted; card/hand/chat payloads, dumps and full settings are excluded.");
                say("카탈로그·모드 파일·다른 모드·복구 표식은 이름·크기·SHA-256 목록만 수집하며 설치나 안전 복구를 보장하지 않습니다.", "Catalogs, mod files, other mods and recovery markers contribute names, sizes and SHA-256 only; this does not verify installation or authorize recovery.");
                var version = FileVersionInfo.GetVersionInfo(typeof(NativeDiagnosticEntry).Assembly.Location).ProductVersion ?? "";
                var bundle = DiagnosticBundle.Collect(game, Path.Combine(local, "BetterAstralParty", "Logs"), Path.Combine(temporary, "BetterAstralParty"),
                    Path.Combine(profile, "AppData", "LocalLow", "feimo", "AstralParty_INT"), primary, fallback, "", version, null, DiagnosticSources.Production());
                say("ZIP 생성됨: " + bundle.ArchivePath, "ZIP created: " + bundle.ArchivePath);
                say("포함 로그 " + bundle.Included + "개, 제외·누락 " + bundle.Omitted + "개. summary.json에서 이유를 확인하세요.", "Included " + bundle.Included + " logs; omitted/missing " + bundle.Omitted + ". See summary.json for reasons.");
                say("개인정보가 남을 수 있습니다. ZIP 내용을 확인한 뒤 직접 첨부하세요.", "Unrecognised personal data may remain. Review the ZIP, then attach it yourself.");
                if (!options.NoOpen) try { bundle.OpenFolder(open); }
                    catch { say("ZIP은 생성됐지만 폴더를 열지 못했습니다. 위 경로로 직접 열어 주세요.", "The ZIP was created, but the folder could not open. Use the path above."); }
                return 0;
            } catch {
                say("BAP-DIAGNOSTICS-FAILED: 도구 또는 쓰기 권한을 확인하세요. 기존 로그는 보존됩니다.", "BAP-DIAGNOSTICS-FAILED: Check the tool and write permissions. Existing logs are preserved.");
                return 1;
            }
        }
    }
}
