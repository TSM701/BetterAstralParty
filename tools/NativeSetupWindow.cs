#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Installation
{
    // Install window behind Install.cmd, Uninstall.cmd (since 1.8.1-dev.3, draft 1안 in artifacts/cc/installer-gui-drafts) and
    // Force-Uninstall.cmd (since 1.8.1-dev.5) double-clicked without arguments. It decides nothing itself:
    // NativeMaintenanceEntry.Operate runs on a worker thread, first as CheckOnly/WhatIf for the ready screen, then for real;
    // its prompts become buttons and its output the details log.
    internal static class NativeSetupWindow
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        // null: the window could not open, so the caller runs the console instead.
        internal static int? Run(string[] args)
        {
            SetupForm form;
            try {
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                form = new SetupForm(args);
            } catch (Exception) { return null; }
            Application.ThreadException += (sender, e) => form.Crash(e.Exception);
            using (form) { Application.Run(form); return form.ExitCode; }
        }

        // Engine lines are "한국어 / English"; the window shows one half.
        internal static string Pick(string line, bool korean)
        {
            var at = line.IndexOf(" / ", StringComparison.Ordinal);
            if (at <= 0 || !Regex.IsMatch(line.Substring(0, at), "[가-힣]")) return line.Trim();
            return (korean ? line.Substring(0, at) : line.Substring(at + 3)).Trim();
        }

        internal enum PromptKind { YesNo, Recheck, Choice, Other }
        // The engine's prompts: "[y/N]" consents, "r: … Enter: 취소" waits and the "1 / 2" loader choice (a source check keeps
        // every prompt in one of these kinds).
        internal static PromptKind Kind(string prompt)
        {
            if (prompt.Contains("[y/N]")) return PromptKind.YesNo;
            if (prompt.StartsWith("r: ", StringComparison.Ordinal) && prompt.Contains("Enter")) return PromptKind.Recheck;
            if (prompt.Contains("1 / 2")) return PromptKind.Choice;
            return PromptKind.Other;
        }
    }

    internal enum GlyphKind { Check, Pending, Running, Info, Warning, Error, Question, Clock }

    internal sealed class SetupForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(32, 32, 32), Muted = Color.FromArgb(96, 96, 96), Line = Color.FromArgb(214, 214, 214),
            Foot = Color.FromArgb(243, 243, 243), Yellow = Color.FromArgb(255, 216, 77), YellowEdge = Color.FromArgb(214, 170, 32),
            Cream = Color.FromArgb(255, 248, 227), CreamEdge = Color.FromArgb(232, 217, 168), Brown = Color.FromArgb(60, 40, 0),
            Rose = Color.FromArgb(253, 244, 243), RoseEdge = Color.FromArgb(232, 190, 185), Wine = Color.FromArgb(120, 30, 20), Link = Color.FromArgb(0, 95, 184);
        private enum View { Checking, NoGame, Ready, Running, Prompt, Done, Failed }

        private readonly float _scale;
        private readonly string _package;
        private readonly string? _supplied;
        private readonly Panel _header = new Panel(), _body = new Panel(), _footer = new Panel();
        private readonly LinkLabel _language = new LinkLabel();
        private readonly object _gate = new object();
        private readonly List<string> _log = new List<string>(), _recent = new List<string>();
        private bool _korean, _removal, _emergency, _busy, _gameRunning, _takeover, _generation, _chosen, _confirmed, _shortcut, _userCancelled, _hideConsole;
        // The check's loaderConsole= state: "fresh" or "on" offer turning BepInEx's log window off; ticked by default only for fresh.
        private string _console = "";
        private View _view = View.Checking;
        private string? _steam, _game, _installed, _finalLine, _debugLog;
        private string _version = "", _failureText = "", _failureAction = "";
        private bool _previewFailure;
        private int _removeCount, _historyMoves, _step, _deleteDirectories, _estimate;
        // Time spent running, without the time a question waits for the player; shown against the estimate.
        private readonly Stopwatch _runClock = new Stopwatch();
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        private Label? _clock;
        // Force-Uninstall: the deletion list the ready screen showed, and the lines of the run that asks for consent.
        private List<string> _previewListing = new List<string>();
        private readonly List<string> _runLines = new List<string>();
        private string _promptText = "";
        private string[] _promptContext = new string[0];
        private WindowsPromptAnswer? _promptAnswer;
        internal int ExitCode { get; private set; } = 1;

        private sealed class WindowsPromptAnswer
        {
            private readonly Action<string?> _resolve; private int _done;
            internal WindowsPromptAnswer(Action<string?> resolve) { _resolve = resolve; }
            internal void Resolve(string? value) { if (Interlocked.Exchange(ref _done, 1) == 0) _resolve(value); }
        }

        internal SetupForm(string[] args)
        {
            _emergency = args.Any(arg => arg.TrimStart('-').Equals("force-uninstall", StringComparison.OrdinalIgnoreCase));
            _removal = _emergency || args.Any(arg => arg.TrimStart('-').Equals("uninstall", StringComparison.OrdinalIgnoreCase));
            for (var i = 0; i + 1 < args.Length; i++) if (args[i].TrimStart('-').Equals("gameroot", StringComparison.OrdinalIgnoreCase)) _supplied = args[i + 1];
            _korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
            _package = WindowsPath.Parent(WindowsPath.FromIo(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'));
            using (var graphics = CreateGraphics()) _scale = graphics.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None; Font = new Font("Malgun Gothic", 9.75f); BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(560), S(460));
            _tick.Tick += (sender, e) => { if (_view == View.Running && _clock != null && !_clock.IsDisposed) _clock.Text = ClockText(); };
            _tick.Start();
            FormClosed += (sender, e) => _tick.Dispose();
            _header.Dock = DockStyle.Top; _header.Height = S(64); _header.BackColor = Cream; _header.Paint += PaintHeader;
            _footer.Dock = DockStyle.Bottom; _footer.Height = S(56); _footer.BackColor = Foot;
            _footer.Paint += (sender, e) => { using (var pen = new Pen(Line)) e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0); };
            _body.Dock = DockStyle.Fill; _body.BackColor = Color.White;
            _language.AutoSize = true; _language.LinkColor = Link; _language.BackColor = Cream;
            _language.LinkClicked += (sender, e) => { _korean = !_korean; Render(); };
            _header.Controls.Add(_language);
            Controls.Add(_body); Controls.Add(_footer); Controls.Add(_header);
            FormClosing += (sender, e) => {
                if (!_busy) return;
                e.Cancel = true;
                if (_promptAnswer != null) _userCancelled = true;
                _promptAnswer?.Resolve(null); // closing during a question answers it with cancel; the run then ends by itself
            };
            Shown += (sender, e) => Check();
            Render();
        }

        private int S(float value) { return (int)Math.Round(value * _scale); }
        private string T(string ko, string en) { return _korean ? ko : en; }
        private string Pick(string line) { return NativeSetupWindow.Pick(line, _korean); }

        // ---- engine work -----------------------------------------------------------------------------------------------
        private void Work(Action job)
        {
            _busy = true;
            var thread = new Thread(() => {
                try { job(); }
                catch (Exception error) { UI(() => Fail(error, false)); }
                finally { UI(() => { _busy = false; }); }
            }) { IsBackground = true };
            thread.Start();
        }
        private void UI(Action action) { try { if (!IsDisposed) BeginInvoke(action); } catch (InvalidOperationException) { } }
        private void Say(string line)
        {
            lock (_gate) {
                if (_log.Count < 4000) _log.Add(line);
                _recent.Add(line); if (_recent.Count > 12) _recent.RemoveAt(0);
                if (line.StartsWith("Debug log: ", StringComparison.Ordinal)) _debugLog = line.Substring(11);
            }
        }
        private string? Ask(string prompt)
        {
            // The window's 제거 button confirmed the listed removal; the engine asks the same question once more.
            if (_removal && prompt.StartsWith("BetterAstralParty 파일을 백업 후 제거할까요?", StringComparison.Ordinal)) return "y";
            // The 영구 삭제 button confirmed the listed deletion. The run lists it again before asking; any difference asks anew.
            if (_emergency && prompt.StartsWith("표시한 파일과 복구 백업을 복원 불가능하게 영구 삭제할까요?", StringComparison.Ordinal)) {
                List<string> listing; lock (_gate) listing = Listing(_runLines);
                if (listing.SequenceEqual(_previewListing, StringComparer.Ordinal)) return "y";
                lock (_gate) { _recent.Clear(); _recent.Add("지울 대상이 미리 확인한 목록과 달라졌습니다. 자세히 보기에서 새 목록을 확인하세요. / The deletion list changed since the check. Review the new list under Details."); }
            }
            string? answer = null;
            using (var done = new ManualResetEventSlim(false)) {
                string[] context; lock (_gate) { context = _recent.ToArray(); _recent.Clear(); }
                UI(() => {
                    _runClock.Stop(); // the player's time on a question is not part of the run
                    _promptText = prompt; _promptContext = context;
                    _promptAnswer = new WindowsPromptAnswer(value => { answer = value; done.Set(); });
                    _view = View.Prompt; Render();
                });
                done.Wait();
            }
            UI(() => { _promptAnswer = null; _runClock.Start(); if (_view == View.Prompt) { _view = View.Running; Render(); } });
            return answer;
        }

        private void Check()
        {
            _view = View.Checking; Render();
            Work(() => {
                _steam = NativeMaintenanceEntry.FindSteamRoot(true, Say);
                string? game = _game;
                if (game == null)
                    try { game = NativeMaintenanceEntry.ResolveOperationGame(_supplied, _steam, _package, _removal, _emergency); }
                    catch (Exception error) when (error is InvalidOperationException || error is IOException || error is ArgumentException || error is UnauthorizedAccessException || error is ApplySafetyException) { Say(error.Message); }
                if (game == null) { UI(() => { _view = View.NoGame; Render(); }); return; }
                Preview(game);
            });
        }
        private void Preview(string game)
        {
            lock (_gate) _log.Add(T("— 미리 확인 —", "— Check —"));
            var lines = new List<string>(); Exception? failure; var watch = Stopwatch.StartNew();
            var code = NativeMaintenanceEntry.Operate(new[] { Operation, _removal ? "-WhatIf" : "-CheckOnly", "-GameRoot", game },
                line => { lines.Add(line); Say(line); }, prompt => null, out failure);
            var estimate = Estimate(watch.Elapsed.TotalSeconds, _emergency);
            var deletion = Listing(lines);
            var installed = InstalledVersion(game); bool running;
            try { running = NativeSteamMaintenance.GameRunning(game); } catch (Exception) { running = false; }
            var version = lines.Select(line => Regex.Match(line, @"Version: ([0-9A-Za-z.+-]+);")).Where(match => match.Success).Select(match => match.Groups[1].Value).LastOrDefault() ?? "";
            var listing = lines.SkipWhile(line => !line.StartsWith("제거 대상 / Files to remove", StringComparison.Ordinal)).Skip(1).TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal)).Count();
            var moves = lines.Select(line => Regex.Match(line, @"([0-9]+) earlier reinstall records")).Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).FirstOrDefault();
            UI(() => {
                _game = game; _installed = installed; _gameRunning = running; _version = version; _removeCount = listing; _historyMoves = moves; _estimate = estimate;
                if (_emergency) {
                    _previewListing = deletion; _confirmed = false;
                    _removeCount = deletion.Count(line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains(" | SHA256="));
                    _deleteDirectories = deletion.Count(line => line.StartsWith("  폴더 삭제 / ", StringComparison.Ordinal));
                    _shortcut = deletion.Any(line => line.StartsWith("일치하는 BetterAstralParty Steam 외부 게임만 제거합니다.", StringComparison.Ordinal));
                }
                _takeover = lines.Any(line => line.Contains("1.3.x installation will be taken over"));
                _console = lines.Select(line => Regex.Match(line, @"loaderConsole=(fresh|on|off|unknown)")).Where(match => match.Success).Select(match => match.Groups[1].Value).LastOrDefault() ?? "";
                _hideConsole = _console == "fresh";
                _generation = lines.Any(line => line.Contains("verified complete removal"));
                if (code == 0) { _view = View.Ready; Render(); } else Fail(failure, true);
            });
        }
        private string Operation { get { return _emergency ? "--force-uninstall" : _removal ? "--uninstall" : "--install"; } }
        // Seconds the real run takes, from the check the window just timed on this PC: the run repeats the check's reads
        // (an install verifies the package again, a removal hashes its targets again; force-uninstall hashes them twice
        // more, when it holds them and before each delete) and then writes. Rounded up to 5 seconds.
        internal static int Estimate(double check, bool emergency)
        {
            var seconds = (emergency ? 3 : 2) * Math.Max(0, check) + 2;
            return Math.Max(5, (int)Math.Ceiling(seconds / 5) * 5);
        }
        private string Span(int seconds)
        {
            return seconds < 60 ? T("약 " + seconds + "초", "about " + seconds + " s")
                : T("약 " + (seconds + 59) / 60 + "분", "about " + (seconds + 59) / 60 + " min");
        }
        private string ClockText()
        {
            var elapsed = (int)_runClock.Elapsed.TotalSeconds;
            return elapsed <= _estimate ? T("창을 닫지 마세요. 경과 " + elapsed + "초, 예상 " + Span(_estimate), "Keep this window open. " + elapsed + " s so far, " + Span(_estimate) + " expected")
                : T("창을 닫지 마세요. 경과 " + elapsed + "초, 예상보다 오래 걸리고 있습니다", "Keep this window open. " + elapsed + " s so far, longer than expected");
        }
        // The deletion list Force-Uninstall prints: from its target line up to its closing warning.
        private static List<string> Listing(IEnumerable<string> lines)
        {
            return lines.SkipWhile(line => !line.StartsWith("영구 삭제 대상 / ", StringComparison.Ordinal))
                .TakeWhile(line => !line.StartsWith("위 목록과 ", StringComparison.Ordinal)).ToList();
        }
        private void Execute()
        {
            lock (_gate) { _log.Add(_emergency ? T("— 강제 제거 —", "— Force uninstall —") : _removal ? T("— 제거 —", "— Removal —") : T("— 설치 —", "— Installation —")); _runLines.Clear(); }
            _step = 0; _userCancelled = false; _runClock.Restart(); _view = View.Running; Render();
            var game = _game!;
            SetupHost.StageObserved = (phase, outcome) => { if (outcome == "Begin") UI(() => Advance(phase)); };
            Work(() => {
                Exception? failure; string? final = null; var cancelled = false;
                var arguments = new List<string> { Operation, "-GameRoot", game };
                if (!_removal && (_console == "fresh" || _console == "on")) { arguments.Add("-LoaderConsole"); arguments.Add(_hideConsole ? "Off" : "Keep"); }
                var code = NativeMaintenanceEntry.Operate(arguments.ToArray(), line => {
                    Say(line); lock (_gate) if (_runLines.Count < 8000) _runLines.Add(line);
                    if (line.StartsWith("적용 완료", StringComparison.Ordinal) || line.StartsWith("기존 설치본 업데이트 완료", StringComparison.Ordinal)
                        || line.StartsWith("제거 완료", StringComparison.Ordinal) || line.StartsWith("모드 파일 제거 완료", StringComparison.Ordinal)) final = line;
                    if (line.StartsWith("취소했습니다.", StringComparison.Ordinal)) cancelled = true;
                }, Ask, out failure);
                SetupHost.StageObserved = null;
                UI(() => {
                    _runClock.Stop();
                    // A question the player cancelled leads back to a fresh check, never to a done or failed screen.
                    if (cancelled || _userCancelled) { UI(Check); return; }
                    if (code == 0) { _finalLine = final; ExitCode = 0; _view = View.Done; Render(); }
                    else Fail(failure, false);
                });
            });
        }
        private void Advance(string phase)
        {
            var step = _removal ? (phase == "Uninstall" ? 1 : _step)
                : phase == "BootstrapGate" ? 0 : phase == "Recovery" || phase == "DescriptorVerification" ? 1 : phase == "WaitForExit" ? 2 : phase == "Apply" || phase == "Configuration" ? 3 : _step;
            if (step > _step) { _step = step; if (_view == View.Running) Render(); }
        }
        private void Fail(Exception? failure, bool preview)
        {
            _previewFailure = preview; ExitCode = 1;
            _failureText = failure == null ? T("알 수 없는 오류입니다.", "Unknown error.") : Pick(failure.Message);
            var action = failure == null ? null : NativeMaintenanceEntry.RecoveryAction(failure);
            _failureAction = action == null ? "" : Pick(action);
            _view = View.Failed; Render();
        }
        internal void Crash(Exception error) { _busy = false; Fail(error, false); }

        private static string? InstalledVersion(string game)
        {
            try {
                var path = WindowsPath.ForIo(Path.Combine(game, "BepInEx", "plugins", "BetterAstralParty", "BetterAstralParty.dll"));
                return File.Exists(path) ? InstalledPluginMetadata.Read(File.ReadAllBytes(path)).Version : null;
            } catch (Exception) { return null; }
        }
        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = T("Steam의 설치된 파일 → 찾아보기로 연 Astral Party 폴더를 고르세요.", "Choose the Astral Party folder Steam opens from Installed Files → Browse."), ShowNewFolderButton = false }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var selected = dialog.SelectedPath; _chosen = true;
                _view = View.Checking; Render();
                Work(() => {
                    string game;
                    try { game = NativeMaintenanceEntry.ResolveOperationGame(selected, _steam, _package, _removal, _emergency); }
                    catch (Exception error) when (error is InvalidOperationException || error is IOException || error is ArgumentException || error is UnauthorizedAccessException || error is ApplySafetyException) {
                        UI(() => Fail(error, true)); return;
                    }
                    Preview(game);
                });
            }
        }
        private void Diagnostics()
        {
            var game = _game; if (game == null || _busy) return;
            var language = _korean ? "ko" : "en";
            Work(() => {
                lock (_gate) _log.Add(T("— 진단 ZIP —", "— Diagnostics ZIP —"));
                var code = NativeDiagnosticEntry.Run(new[] { "--diagnostics", "collect", "--game-root", game, "--language", language }, Say);
                UI(() => MessageBox.Show(this, code == 0 ? T("진단 ZIP을 만들었습니다. 열린 폴더에서 확인한 뒤 직접 첨부하세요.", "The diagnostics ZIP is ready. Check it in the folder that opened, then attach it yourself.")
                    : T("진단 ZIP을 만들지 못했습니다.", "The diagnostics ZIP could not be created."), Text, MessageBoxButtons.OK, code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning));
            });
        }
        private void ShowLog()
        {
            string text; lock (_gate) text = string.Join(Environment.NewLine, _log);
            var window = new Form { Text = T("자세히 보기", "Details"), ClientSize = new Size(S(720), S(420)), StartPosition = FormStartPosition.CenterParent, Font = Font };
            window.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), Text = text, BackColor = Color.White });
            window.Show(this);
        }
        private void OpenGameFolder()
        {
            if (_game != null) try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _game + "\"") { UseShellExecute = true }); } catch (Exception) { }
        }

        // ---- views ------------------------------------------------------------------------------------------------------
        private void Render()
        {
            SuspendLayout();
            Text = _emergency ? T("BetterAstralParty 강제 제거", "Force-uninstall BetterAstralParty")
                : _removal ? T("BetterAstralParty 제거", "Uninstall BetterAstralParty") : T("BetterAstralParty 설치", "Install BetterAstralParty");
            _language.Text = _korean ? "English" : "한국어"; _language.Location = new Point(ClientSize.Width - _language.PreferredWidth - S(20), S(24));
            _header.Invalidate();
            foreach (Control control in _body.Controls.Cast<Control>().ToArray()) control.Dispose();
            foreach (Control control in _footer.Controls.Cast<Control>().ToArray()) control.Dispose();
            switch (_view) {
                case View.Checking: RenderChecking(); break;
                case View.NoGame: RenderNoGame(); break;
                case View.Ready: RenderReady(); break;
                case View.Running: RenderRunning(); break;
                case View.Prompt: RenderPrompt(); break;
                case View.Done: RenderDone(); break;
                default: RenderFailed(); break;
            }
            ResumeLayout();
        }
        private void RenderChecking()
        {
            var y = Title(T("확인하는 중…", "Checking…"), 20);
            Note(T("게임 폴더와 패키지를 확인하고 있습니다.", "Checking the game folder and the package."), y);
            _body.Controls.Add(new ProgressBar { Style = ProgressBarStyle.Marquee, Location = new Point(S(24), S(96)), Size = new Size(S(512), S(8)) });
            Footer(new[] { (T("취소", "Cancel"), false, (Action)Close, false) });
        }
        private void RenderNoGame()
        {
            AddGlyph(GlyphKind.Warning, 24, S(20));
            var y = Title(T("게임 폴더를 찾지 못했습니다", "The game folder was not found"), 20, 76);
            y = Note(T("Steam에서 Astral Party를 우클릭하고 관리 → 로컬 파일 보기로 연 폴더를 고르세요.", "In Steam, right-click Astral Party and choose Manage → Browse local files, then pick that folder."), y + S(4), null, 76, 460);
            Footer(new[] { (T("취소", "Cancel"), false, (Action)Close, true), (T("폴더 선택…", "Choose folder…"), true, (Action)ChooseFolder, true) });
        }
        // Force-Uninstall's ready screen: what goes for good, and a check box as the consent the console took as "y".
        private void RenderForceReady()
        {
            var y = Title(T("BetterAstralParty 강제 제거", "Force-uninstall BetterAstralParty"), 20);
            y = Note(T("Uninstall.cmd로 지우지 못할 때 씁니다. 지운 파일은 되돌릴 수 없습니다.", "Use it when Uninstall.cmd fails. Deleted files cannot be restored."), y);
            y += S(14);
            AddLabel(T("게임 폴더", "Game folder"), 24, y, 0, 9.75f, true, Ink); y += S(22);
            _body.Controls.Add(new TextBox { ReadOnly = true, Text = _game ?? "", Location = new Point(S(24), y), Width = S(424), BackColor = Color.White });
            var change = SecondaryButton(T("변경…", "Change…"), ChooseFolder); change.Location = new Point(S(456), y - S(2)); change.Width = S(80); _body.Controls.Add(change);
            y += S(40);
            var something = _removeCount != 0 || _deleteDirectories != 0 || _shortcut;
            var rows = new List<(GlyphKind, string)> { (GlyphKind.Check, _chosen ? T("선택한 게임 폴더입니다", "The folder you chose") : T("Steam에서 찾은 게임 폴더입니다", "The folder Steam reports")) };
            rows.Add(_gameRunning ? (GlyphKind.Warning, T("게임이 켜져 있습니다. 진행하기 전에 종료하세요", "The game is running. Close it before you continue"))
                : (GlyphKind.Check, T("게임이 꺼져 있습니다", "The game is closed")));
            rows.Add(_removeCount == 0 && _deleteDirectories == 0 ? (GlyphKind.Info, T("지울 BetterAstralParty 파일이 없습니다", "No BetterAstralParty files to delete"))
                : (GlyphKind.Warning, T("파일 " + _removeCount + "개와 폴더 " + _deleteDirectories + "개를 백업 없이 지웁니다", "Deletes " + _removeCount + " files and " + _deleteDirectories + " folders without a backup")));
            if (something) rows.Add((GlyphKind.Clock, T("예상 시간 " + Span(_estimate), "Takes " + Span(_estimate))));
            rows.Add((GlyphKind.Check, T("공용 로더와 다른 모드, 한국어 패치는 그대로입니다", "The shared loader and other mods stay")));
            if (_shortcut) rows.Add((GlyphKind.Info, T("예전 Steam 외부 게임도 지웁니다. 지우기 전에 Steam 종료를 요청합니다", "Also removes the earlier non-Steam game entry; Steam must be closed")));
            foreach (var (kind, text) in rows) { AddGlyph(kind, 24, y, 16); AddLabel(text, 48, y - 1, 488, 9.75f, false, Ink); y += S(26); }
            if (something) {
                var confirm = new CheckBox { Text = T("지운 파일은 되돌릴 수 없다는 것을 확인했습니다", "I understand deleted files cannot be restored"), Checked = _confirmed,
                    AutoSize = true, Location = new Point(S(24), y + S(4)), BackColor = Color.White, ForeColor = Ink, UseMnemonic = false };
                confirm.CheckedChanged += (sender, e) => { _confirmed = confirm.Checked; UI(Render); }; // after the click, never inside its own event
                _body.Controls.Add(confirm);
            }
            Footer(new[] { (T("취소", "Cancel"), false, (Action)Close, true), (T("영구 삭제", "Delete permanently"), true, (Action)Execute, something && _confirmed) },
                new[] { (T("자세히 보기", "Details"), (Action)ShowLog) });
        }
        private void RenderReady()
        {
            if (_emergency) { RenderForceReady(); return; }
            var update = _installed != null && !_removal;
            var same = update && _installed == _version;
            var title = _removal ? T("BetterAstralParty 제거", "Uninstall BetterAstralParty")
                : same ? T("BetterAstralParty " + _version + " 다시 설치", "Reinstall BetterAstralParty " + _version)
                : update ? T("BetterAstralParty " + _version + " 업데이트", "Update to BetterAstralParty " + _version)
                : T("BetterAstralParty " + _version + " 설치", "Install BetterAstralParty " + _version);
            var y = Title(title, 20);
            y = Note(_removal ? T("설치된 버전: " + (_installed ?? "확인 불가"), "Installed version: " + (_installed ?? "unknown"))
                : update ? T("지금 설치된 버전: " + _installed + ". 설정은 그대로 둡니다.", "Installed now: " + _installed + ". Your settings stay.")
                : T("게임 폴더에 BetterAstralParty를 설치합니다.", "Installs BetterAstralParty into the game folder."), y);
            y += S(14);
            AddLabel(T("게임 폴더", "Game folder"), 24, y, 0, 9.75f, true, Ink); y += S(22);
            _body.Controls.Add(new TextBox { ReadOnly = true, Text = _game ?? "", Location = new Point(S(24), y), Width = S(424), BackColor = Color.White });
            var change = SecondaryButton(T("변경…", "Change…"), ChooseFolder); change.Location = new Point(S(456), y - S(2)); change.Width = S(80); _body.Controls.Add(change);
            y += S(40);
            var rows = new List<(GlyphKind, string)> { (GlyphKind.Check, _chosen ? T("선택한 게임 폴더입니다", "The folder you chose") : T("Steam에서 찾은 게임 폴더입니다", "The folder Steam reports")) };
            rows.Add(_gameRunning ? (GlyphKind.Warning, T("게임이 켜져 있습니다. 진행하기 전에 종료하세요", "The game is running. Close it before you continue"))
                : (GlyphKind.Check, T("게임이 꺼져 있습니다", "The game is closed")));
            if (_removal) {
                rows.Add(_removeCount == 0 ? (GlyphKind.Info, T("지울 BetterAstralParty 파일이 없습니다", "No BetterAstralParty files to remove"))
                    : (GlyphKind.Info, T("BetterAstralParty 파일 " + _removeCount + "개를 백업한 뒤 지웁니다", "Backs up, then removes " + _removeCount + " BetterAstralParty files")));
                if (_removeCount != 0) rows.Add((GlyphKind.Clock, T("예상 시간 " + Span(_estimate), "Takes " + Span(_estimate))));
                rows.Add((GlyphKind.Check, T("공용 로더와 다른 모드, 한국어 패치는 그대로입니다", "The shared loader and other mods stay")));
                rows.Add((GlyphKind.Info, T("예전 Steam 외부 게임이 있으면 지우기 전에 Steam 종료를 요청합니다", "An earlier non-Steam game entry, if any, needs Steam closed to remove")));
            } else {
                rows.Add((GlyphKind.Check, T("패키지 서명을 확인했습니다", "The package signature is verified")));
                rows.Add((GlyphKind.Clock, T("예상 시간 " + Span(_estimate), "Takes " + Span(_estimate))));
                if (_takeover) rows.Add((GlyphKind.Info, T("1.3.x 설치를 이어받습니다. 진행 중에 한 번 더 묻습니다", "Takes over the 1.3.x installation; it asks once more")));
                else if (_generation) rows.Add((GlyphKind.Info, T("제거된 이전 설치를 확인했습니다. 진행 중에 한 번 더 묻습니다", "A removed earlier installation was found; it asks once more")));
                else rows.Add((GlyphKind.Info, T("Steam은 켜 둔 채 설치해도 됩니다", "Steam can stay open")));
                if (_historyMoves > 0) rows.Add((GlyphKind.Info, T("이전 재설치 기록 " + _historyMoves + "개를 BetterAstralParty.Update\\history로 옮깁니다", "Moves " + _historyMoves + " earlier reinstall records to BetterAstralParty.Update\\history")));
            }
            foreach (var (kind, text) in rows) { AddGlyph(kind, 24, y, 16); AddLabel(text, 48, y - 1, 488, 9.75f, false, Ink); y += S(26); }
            if (!_removal && (_console == "fresh" || _console == "on")) {
                var console = new CheckBox { Text = T("게임을 켤 때 뜨는 BepInEx 로그 창 끄기", "Turn off the BepInEx log window shown at game start"), Checked = _hideConsole,
                    AutoSize = true, Location = new Point(S(24), y + S(4)), BackColor = Color.White, ForeColor = Ink, UseMnemonic = false };
                console.CheckedChanged += (sender, e) => _hideConsole = console.Checked;
                _body.Controls.Add(console);
            }
            var primary = _removal ? T("제거", "Uninstall") : same ? T("다시 설치", "Reinstall") : update ? T("업데이트", "Update") : T("설치", "Install");
            Footer(new[] { (T("취소", "Cancel"), false, (Action)Close, true), (primary, true, (Action)Execute, !_removal || _removeCount != 0) },
                _removal ? new[] { (T("자세히 보기", "Details"), (Action)ShowLog) }
                    : new[] { (T("제거", "Uninstall"), (Action)(() => { _removal = true; Check(); })), (T("진단 ZIP 만들기", "Diagnostics ZIP"), (Action)Diagnostics) });
        }
        private void RenderRunning()
        {
            var y = Title(_emergency ? T("영구 삭제하는 중…", "Deleting…") : _removal ? T("제거하는 중…", "Uninstalling…") : T("설치하는 중…", "Installing…"), 20);
            _clock = new Label { Text = ClockText(), Font = Font, ForeColor = Muted, BackColor = Color.Transparent, Location = new Point(S(24), y), Size = new Size(S(512), S(20)), UseMnemonic = false };
            _body.Controls.Add(_clock);
            var steps = _emergency ? new[] { T("삭제 목록 다시 확인", "Check the list again"), T("영구 삭제", "Delete"), T("마무리", "Finish") }
                : _removal ? new[] { T("제거 목록 확인", "Check what to remove"), T("백업 후 제거", "Back up and remove"), T("마무리", "Finish") }
                : new[] { T("패키지 확인", "Check the package"), T("기존 설치 확인", "Check the installation"), T("게임 상태 확인", "Check the game"), T("파일 설치", "Install files"), T("마무리", "Finish") };
            _body.Controls.Add(new ProgressBar { Style = ProgressBarStyle.Continuous, Location = new Point(S(24), S(92)), Size = new Size(S(512), S(8)), Maximum = steps.Length, Value = Math.Min(_step, steps.Length) });
            for (var i = 0; i < steps.Length; i++) {
                var top = 120 + i * 26;
                AddGlyph(i < _step ? GlyphKind.Check : i == _step ? GlyphKind.Running : GlyphKind.Pending, 24, S(top), 16);
                AddLabel(steps[i], 48, S(top) - 1, 0, 9.75f, i == _step, i > _step ? Color.FromArgb(150, 150, 150) : Ink);
            }
            Footer(new[] { (T("취소", "Cancel"), false, (Action)(() => { }), false) }, new[] { (T("자세히 보기", "Details"), (Action)ShowLog) });
        }
        private void RenderPrompt()
        {
            var kind = NativeSetupWindow.Kind(_promptText);
            var context = _promptContext.Where(line => !line.StartsWith("  ", StringComparison.Ordinal) && !Regex.IsMatch(line, @"\A[12]: ")).Select(Pick).LastOrDefault() ?? "";
            // Only the indented lines after the message (the running processes), not earlier listings such as the removal list.
            var last = Array.FindLastIndex(_promptContext, line => !line.StartsWith("  ", StringComparison.Ordinal) && !Regex.IsMatch(line, @"\A[12]: "));
            var details = _promptContext.Skip(last + 1).Where(line => line.StartsWith("  ", StringComparison.Ordinal)).Select(line => line.Trim()).ToArray();
            AddGlyph(kind == NativeSetupWindow.PromptKind.Recheck ? GlyphKind.Warning : GlyphKind.Question, 24, S(20));
            var question = Pick(_promptText.Replace(" [y/N]", "").Replace("[y/N]", ""));
            var y = Title(kind == NativeSetupWindow.PromptKind.Recheck ? T("확인이 필요합니다", "Action needed") : T("확인해 주세요", "Please confirm"), 20, 76);
            var message = kind == NativeSetupWindow.PromptKind.Recheck ? context : kind == NativeSetupWindow.PromptKind.YesNo ? (context.Length != 0 ? context + Environment.NewLine + question : question) : context;
            if (message.Length != 0) y = Note(message, y + S(8), Ink, 76, 460);
            if (details.Length != 0) y = Note(string.Join(Environment.NewLine, details.Take(4)), y + S(4), null, 76, 460);
            var answer = _promptAnswer;
            Action<string?> reply = value => answer?.Resolve(value);
            Action<string?> cancel = value => { _userCancelled = true; reply(value); };
            var links = new[] { (T("자세히 보기", "Details"), (Action)ShowLog) };
            if (kind == NativeSetupWindow.PromptKind.Choice) {
                var options = _promptContext.Where(line => Regex.IsMatch(line, @"\A[12]: ")).ToArray();
                var radios = new List<RadioButton>();
                foreach (var option in options) {
                    var radio = new RadioButton { Text = Pick(option.Substring(3)), Location = new Point(S(28), y + S(8)), AutoSize = true, Tag = option.Substring(0, 1), Checked = radios.Count == 0 };
                    radios.Add(radio); _body.Controls.Add(radio); y += S(28);
                }
                Footer(new[] { (T("취소", "Cancel"), false, (Action)(() => cancel("")), true), (T("확인", "OK"), true, (Action)(() => reply(radios.FirstOrDefault(radio => radio.Checked)?.Tag as string)), true) }, links);
            } else if (kind == NativeSetupWindow.PromptKind.Other) {
                var input = new TextBox { Location = new Point(S(24), y + S(12)), Width = S(512) }; _body.Controls.Add(input);
                Note(Pick(_promptText), y + S(44));
                Footer(new[] { (T("취소", "Cancel"), false, (Action)(() => cancel(null)), true), (T("확인", "OK"), true, (Action)(() => reply(input.Text)), true) }, links);
            } else if (kind == NativeSetupWindow.PromptKind.Recheck) {
                Footer(new[] { (T("취소", "Cancel"), false, (Action)(() => cancel("")), true), (T("다시 확인", "Check again"), true, (Action)(() => reply("r")), true) }, links);
            } else {
                Footer(new[] { (T("아니요", "No"), false, (Action)(() => cancel(null)), true), (T("예", "Yes"), true, (Action)(() => reply("y")), true) }, links);
            }
        }
        private void RenderDone()
        {
            AddGlyph(GlyphKind.Check, 24, S(20));
            var y = Title(_removal ? T("제거 완료", "Uninstalled") : T("설치 완료", "Installed"), 20, 76);
            y = Note(_emergency ? T("BetterAstralParty 파일과 이 게임의 복구 백업을 영구 삭제했습니다.", "BetterAstralParty files and this game's recovery backups were deleted.")
                : _removal ? T("BetterAstralParty 파일을 지웠습니다.", "BetterAstralParty files were removed.") : "BetterAstralParty " + _version, y, null, 76, 460);
            var text = _finalLine == null ? "" : Pick(_finalLine);
            if (text.Length != 0) Box(text, y + S(16), Cream, CreamEdge, Brown);
            Footer(new[] { (T("게임 폴더 열기", "Open game folder"), false, (Action)OpenGameFolder, _game != null), (T("닫기", "Close"), true, (Action)Close, true) },
                new[] { (T("자세히 보기", "Details"), (Action)ShowLog) });
        }
        private void RenderFailed()
        {
            AddGlyph(GlyphKind.Error, 24, S(20));
            var y = Title(_previewFailure ? (_removal ? T("제거할 수 없습니다", "Cannot uninstall") : T("설치할 수 없습니다", "Cannot install"))
                : _removal ? T("제거하지 못했습니다", "Uninstall did not finish") : T("설치하지 못했습니다", "Installation did not finish"), 20, 76);
            if (_previewFailure) y = Note(T("아무 파일도 바꾸지 않았습니다.", "Nothing was changed."), y, null, 76, 460);
            else if (_emergency) y = Note(T("이미 지운 파일은 되돌리지 않습니다. 남은 파일과 기록은 그대로 두세요.", "Files already deleted are not restored. Keep the remaining files and logs."), y, null, 76, 460);
            var text = _failureText + (_failureAction.Length != 0 ? Environment.NewLine + Environment.NewLine + _failureAction : "");
            y = Box(text, y + S(14), Rose, RoseEdge, Wine);
            if (_debugLog != null) Note(T("오류 기록: ", "Error log: ") + _debugLog, y + S(10));
            var links = new List<(string, Action)> { (T("자세히 보기", "Details"), (Action)ShowLog) };
            if (_previewFailure) links.Add((T("다른 게임 폴더 선택", "Choose another folder"), (Action)ChooseFolder));
            Footer(new[] { (T("진단 ZIP 만들기", "Diagnostics ZIP"), false, (Action)Diagnostics, _game != null), (T("닫기", "Close"), true, (Action)Close, true) }, links.ToArray());
        }

        // ---- building blocks ------------------------------------------------------------------------------------------
        private int Title(string text, int top, int left = 24) { return AddLabel(text, left, S(top), 0, 13.5f, true, Ink) + S(4); }
        private int Note(string text, int y, Color? color = null, int left = 24, int width = 512) { return AddLabel(text, left, y, width, 9.75f, false, color ?? Muted) + S(2); }
        // Returns the bottom of the label; width 0 keeps one line.
        private int AddLabel(string text, int left, int y, int width, float size, bool bold, Color color)
        {
            var font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
            var label = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, Location = new Point(S(left), y), AutoSize = width == 0, UseMnemonic = false };
            if (width != 0) {
                var limit = S(width);
                label.Size = TextRenderer.MeasureText(text, font, new Size(limit, int.MaxValue), TextFormatFlags.WordBreak);
                label.Width = limit;
            }
            _body.Controls.Add(label);
            return y + (width == 0 ? label.PreferredHeight : label.Height);
        }
        private int Box(string text, int y, Color fill, Color edge, Color ink)
        {
            var font = Font; var width = S(480);
            var size = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
            var box = new Panel { Location = new Point(S(24), y), Size = new Size(S(512), size.Height + S(28)), BackColor = Color.White };
            box.Paint += (sender, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(new RectangleF(0.5f, 0.5f, box.Width - 1.5f, box.Height - 1.5f), S(8)))
                using (var brush = new SolidBrush(fill)) using (var pen = new Pen(edge)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            };
            box.Controls.Add(new Label { Text = text, Font = font, ForeColor = ink, BackColor = fill, Location = new Point(S(16), S(14)), Size = new Size(width, size.Height), UseMnemonic = false });
            _body.Controls.Add(box);
            return y + box.Height;
        }
        private void AddGlyph(GlyphKind kind, int left, int y, int size = 40)
        {
            var glyph = new Glyph(kind) { Location = new Point(S(left), y), Size = new Size(S(size), S(size)), BackColor = Color.White };
            _body.Controls.Add(glyph);
        }
        private Button SecondaryButton(string text, Action click)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(251, 251, 251), ForeColor = Ink, Height = S(32), UseMnemonic = false, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            button.Click += (sender, e) => click();
            return button;
        }
        private void Footer((string Text, bool Primary, Action Click, bool Enabled)[] buttons, (string Text, Action Click)[]? links = null)
        {
            AcceptButton = null;
            var x = ClientSize.Width - S(20);
            foreach (var (text, primary, click, enabled) in buttons.Reverse()) {
                var button = SecondaryButton(text, click);
                if (primary) button.Font = new Font(Font, FontStyle.Bold);
                // A flat button keeps its colours when disabled, so an unavailable primary is drawn gray instead of yellow.
                if (primary && enabled) { button.BackColor = Yellow; button.ForeColor = Brown; button.FlatAppearance.BorderColor = YellowEdge; }
                else if (!enabled) { button.BackColor = Color.FromArgb(240, 240, 240); button.FlatAppearance.BorderColor = Color.FromArgb(222, 222, 222); }
                button.Enabled = enabled;
                button.Width = Math.Max(S(88), TextRenderer.MeasureText(text, button.Font).Width + S(32));
                x -= button.Width; button.Location = new Point(x, S(12)); x -= S(8);
                _footer.Controls.Add(button);
                if (primary && enabled && !_emergency) AcceptButton = button; // Enter never deletes for good
            }
            var left = S(20);
            foreach (var (text, click) in links ?? new (string, Action)[0]) {
                var link = new LinkLabel { Text = text, AutoSize = true, LinkColor = Link, Location = new Point(left, S(19)), UseMnemonic = false, BackColor = Foot };
                link.LinkClicked += (sender, e) => click();
                _footer.Controls.Add(link); left += link.PreferredWidth + S(16);
            }
        }
        private void PaintHeader(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var pen = new Pen(CreamEdge)) g.DrawLine(pen, 0, _header.Height - 1, ClientSize.Width, _header.Height - 1);
            using (var brush = new SolidBrush(Yellow)) g.FillEllipse(brush, S(20), S(16), S(32), S(32));
            var star = new PointF[10];
            for (var i = 0; i < 10; i++) { var r = S(i % 2 == 0 ? 12 : 5); var a = Math.PI * (-90 + i * 36) / 180; star[i] = new PointF(S(36) + (float)(r * Math.Cos(a)), S(32) + (float)(r * Math.Sin(a))); }
            using (var brush = new SolidBrush(Color.FromArgb(90, 60, 10))) g.FillPolygon(brush, star);
            using (var font = new Font(Font.FontFamily, 13f, FontStyle.Bold)) TextRenderer.DrawText(g, "BetterAstralParty", font, new Point(S(62), S(12)), Brown);
            TextRenderer.DrawText(g, T("Astral Party 모드 설치 도구", "Astral Party mod installer"), Font, new Point(S(63), S(36)), Color.FromArgb(110, 90, 50));
        }
        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath(); var d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
            return path;
        }

        private sealed class Glyph : Control
        {
            private readonly GlyphKind _kind;
            internal Glyph(GlyphKind kind) { _kind = kind; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                float w = Width - 1, h = Height - 1, k = w / 16f;
                Color color = _kind == GlyphKind.Check ? Color.FromArgb(16, 124, 16) : _kind == GlyphKind.Error ? Color.FromArgb(196, 43, 28)
                    : _kind == GlyphKind.Warning ? Color.FromArgb(196, 126, 0) : _kind == GlyphKind.Running ? YellowEdge : _kind == GlyphKind.Pending ? Color.FromArgb(170, 170, 170) : Link;
                using (var brush = new SolidBrush(color)) using (var white = new Pen(Color.White, Math.Max(1.6f, 2.2f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var ring = new Pen(color, Math.Max(1f, 1.4f * k))) {
                    switch (_kind) {
                        case GlyphKind.Pending: g.DrawEllipse(ring, 1, 1, w - 2, h - 2); break;
                        case GlyphKind.Clock:
                            g.DrawEllipse(ring, 1, 1, w - 2, h - 2);
                            using (var hand = new Pen(color, Math.Max(1.2f, 1.6f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                                g.DrawLine(hand, w / 2, h / 2, w / 2, h * 0.24f); g.DrawLine(hand, w / 2, h / 2, w * 0.72f, h / 2);
                            }
                            break;
                        case GlyphKind.Running:
                            g.DrawEllipse(ring, 1, 1, w - 2, h - 2); g.FillPie(brush, w * 0.22f, h * 0.22f, w * 0.56f, h * 0.56f, -90, 210); break;
                        case GlyphKind.Warning:
                            g.FillPolygon(brush, new[] { new PointF(w / 2, h * 0.06f), new PointF(w * 0.98f, h * 0.92f), new PointF(w * 0.02f, h * 0.92f) });
                            g.DrawLine(white, w / 2, h * 0.38f, w / 2, h * 0.62f); g.DrawLine(white, w / 2, h * 0.78f, w / 2, h * 0.785f); break;
                        default:
                            g.FillEllipse(brush, 0, 0, w, h);
                            if (_kind == GlyphKind.Check) g.DrawLines(white, new[] { new PointF(w * 0.28f, h * 0.52f), new PointF(w * 0.44f, h * 0.68f), new PointF(w * 0.74f, h * 0.34f) });
                            else if (_kind == GlyphKind.Error) { g.DrawLine(white, w * 0.33f, h * 0.33f, w * 0.67f, h * 0.67f); g.DrawLine(white, w * 0.33f, h * 0.67f, w * 0.67f, h * 0.33f); }
                            else if (_kind == GlyphKind.Info) { g.DrawLine(white, w / 2, h * 0.46f, w / 2, h * 0.74f); g.DrawLine(white, w / 2, h * 0.28f, w / 2, h * 0.285f); }
                            else { using (var font = new Font("Segoe UI", Math.Max(6f, h * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel)) TextRenderer.DrawText(g, "?", font, new Rectangle(0, 0, (int)w + 1, (int)h + 1), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                            break;
                    }
                }
            }
        }
    }
}
