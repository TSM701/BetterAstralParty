#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterAstralParty.Diagnostics;
using BetterAstralParty.Observability;

// This window never releases the compatibility block or starts an unverified modded game.
internal sealed class LauncherRecoveryForm : Form
{
    private readonly string _root;
    private readonly LauncherRecoveryClient _client;
    private readonly Label _intro = new Label(), _state = new Label(), _version = new Label();
    private readonly TextBox _notes = new TextBox();
    private readonly Button _retry = new Button(), _apply = new Button(), _original = new Button(), _diagnostics = new Button(), _close = new Button();
    private readonly ComboBox _language = new ComboBox(), _channel = new ComboBox();
    private readonly Label _channelLabel = new Label();
    private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };
    private CancellationTokenSource? _cancel;
    private LauncherRecoveryStatus _status = LauncherRecoveryStatus.NotChecked;
    private bool _korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko", _busy, _closing, _queued, _syncingChannel;
    private DateTimeOffset _nextCheck;
    internal LauncherRecoveryForm(string root
#if BAP_LAUNCHER_RECOVERY_FIXTURE
        , LauncherRecoveryClient? fixtureClient = null
#endif
        )
    {
        _root = root;
#if BAP_LAUNCHER_RECOVERY_FIXTURE
        _client = fixtureClient ?? new LauncherRecoveryClient(root);
#else
        _client = new LauncherRecoveryClient(root);
#endif
        Text = "BetterAstralParty"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(760, 490); ClientSize = new Size(820, 500);
        AutoScaleMode = AutoScaleMode.Dpi; Font = SystemFonts.MessageBoxFont;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _intro.AutoSize = _state.AutoSize = _version.AutoSize = true;
        _intro.Dock = _state.Dock = _version.Dock = DockStyle.Fill; _intro.Margin = _state.Margin = _version.Margin = new Padding(0, 0, 0, 12);
        _notes.Multiline = true; _notes.ReadOnly = true; _notes.ScrollBars = ScrollBars.Vertical; _notes.Dock = DockStyle.Fill; _notes.WordWrap = true;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 12, 0, 0) };
        foreach (var button in new[] { _retry, _apply, _original, _diagnostics, _close }) { button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink; button.Padding = new Padding(8, 4, 8, 4); actions.Controls.Add(button); }
        _language.DropDownStyle = ComboBoxStyle.DropDownList; _language.Items.AddRange(new object[] { "한국어", "English" }); _language.SelectedIndex = _korean ? 0 : 1;
        _language.SelectedIndexChanged += (_, __) => { _language.Tag = true; _korean = _language.SelectedIndex == 0; RefreshText(); };
        _language.SelectionChangeCommitted += (_, __) => { _language.Tag = true; };
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 12, 0, 0) };
        _channelLabel.AutoSize = true; _channelLabel.Margin = new Padding(0, 5, 8, 0);
        _channel.DropDownStyle = ComboBoxStyle.DropDownList; _channel.Width = 190;
        _channel.Items.AddRange(new object[] { "Release", "Pre-release" }); _channel.SelectedIndex = 0;
        _channel.SelectedIndexChanged += async (_, __) => {
            if (_busy || _queued || _syncingChannel) return;
            if (!_client.SelectChannel(_channel.SelectedIndex == 1 ? "PreRelease" : "Stable")) return;
            _status = LauncherRecoveryStatus.NotChecked; RefreshText(); await Check(false);
        };
        options.Controls.Add(_channelLabel); options.Controls.Add(_channel); options.Controls.Add(_language);
        layout.Controls.Add(_intro, 0, 0); layout.Controls.Add(_version, 0, 1); layout.Controls.Add(_state, 0, 2); layout.Controls.Add(_notes, 0, 3); layout.Controls.Add(actions, 0, 4); layout.Controls.Add(options, 0, 5); Controls.Add(layout);
        _retry.Click += async (_, __) => await Check(false);
        _apply.Click += async (_, __) => {
            if (_client.Prepared == null) { await Check(true); return; }
            var prepared = _client.Prepared;
            if (prepared.Transition != null && MessageBox.Show(this,
                ChannelText(prepared.Installation.ActiveChannel) + " " + prepared.Installation.Version + " → " + ChannelText(prepared.Installation.Channel) + " " + prepared.Release.Version
                + "\r\n" + T("검증된 다른 채널로 전환할까요? 현재 설정과 백업은 보존합니다.", "Switch to this verified channel? Settings and backups are preserved.")
                + "\r\n" + T("이번 업데이트의 선택이며, 게임 내 저장된 채널 설정은 변경하지 않습니다.", "This choice applies once; the saved in-game channel setting is unchanged.")
                + "\r\nSHA-256: " + prepared.Verified.DescriptorSha256, "BetterAstralParty", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await Work(async token => { await _client.Queue(prepared.Transition != null, token).ConfigureAwait(false); return LauncherRecoveryStatus.Queued; });
        };
        _original.Click += (_, __) => {
            if (_busy || _queued) return;
            try { Process.Start(new ProcessStartInfo("steam://launch/2622000/dialog") { UseShellExecute = true }); Close(); }
            catch (Exception error) { Failure(DiagnosticPhase.UiAction, error); _status = LauncherRecoveryStatus.Failed; RefreshText(); }
        };
        _diagnostics.Click += async (_, __) => await Collect();
        _close.Click += (_, __) => Close();
        Shown += async (_, __) => await Check(false);
        _timer.Tick += async (_, __) => {
            RefreshText();
            if (!_busy && DateTimeOffset.UtcNow >= _nextCheck && DateTimeOffset.UtcNow >= _client.RetryAt
                && _status is LauncherRecoveryStatus.NoPatch or LauncherRecoveryStatus.Incompatible or LauncherRecoveryStatus.Failed) await Check(false, metadataOnly: true);
        };
        _timer.Start(); RefreshText();
    }
    private string T(string korean, string english) => _korean ? korean : english;
    private string ChannelText(string channel) => channel switch {
        "Stable" => T("릴리즈", "Release"),
        "PreRelease" => T("프리 릴리즈", "Pre-release"),
        "Beta" => T("비공개 베타", "Private Beta"),
        _ => channel
    };
    internal static string StatusText(LauncherRecoveryStatus status, bool korean)
    {
        var text = status switch {
            LauncherRecoveryStatus.NotChecked => new[] { "호환 업데이트를 확인할 수 있습니다.", "A compatible update can be checked." },
            LauncherRecoveryStatus.Checking => new[] { "업데이트를 확인하고 검증하는 중입니다.", "Checking and verifying the update." },
            LauncherRecoveryStatus.NoPatch => new[] { "조회는 완료했지만 이 채널에 새 대응 업데이트가 없습니다. 설치된 파일은 변경하지 않았습니다.", "The lookup succeeded, but this channel has no newer patch. Installed files were not changed." },
            LauncherRecoveryStatus.Incompatible => new[] { "최신 업데이트도 현재 게임과 호환되지 않습니다. 설치된 파일은 변경하지 않았습니다.", "The latest update is not compatible with this game. Installed files were not changed." },
            LauncherRecoveryStatus.Available => new[] { "선택한 채널의 최신 버전을 사용할 수 있습니다. 내려받아 현재 게임과의 호환성을 확인해주세요.", "The latest version in this channel is available. Download it to verify compatibility with this game." },
            LauncherRecoveryStatus.Ready => new[] { "서명·파일·현재 게임과의 호환성 검증을 통과했습니다. 적용하면 런처가 종료된 뒤 업데이트합니다.", "Signature, files and game compatibility passed. The update applies after this launcher closes." },
            LauncherRecoveryStatus.AuthenticationRequired => new[] { "공개 업데이트 저장소가 인증을 요구했습니다. 이 창에서는 비공개 인증을 사용하지 않습니다. 재확인하거나 공식 전체 패키지를 이용해주세요.", "The public update repository requested authentication. This window does not use private credentials. Retry or use the official complete package." },
            LauncherRecoveryStatus.AccessUnavailable => new[] { "저장소에 접근할 수 없습니다. 대응 패치가 없는 상태와는 다릅니다.", "The repository is inaccessible. This does not mean that no patch exists." },
            LauncherRecoveryStatus.RateLimited => new[] { "서버 요청 제한으로 대기 중입니다. 잠시 후 재확인할 수 있습니다.", "Waiting for the server request limit. Retry after the cooldown." },
            LauncherRecoveryStatus.Incomplete => new[] { "릴리즈 정보 또는 필수 첨부 파일이 불완전합니다. 적용하지 않았습니다.", "Release metadata or required assets are incomplete. Nothing was applied." },
            LauncherRecoveryStatus.VerificationFailed => new[] { "업데이트 검증에 실패했습니다. 적용하지 않았습니다.", "Update verification failed. Nothing was applied." },
            LauncherRecoveryStatus.FilesBusy => new[] { "파일이나 다른 실행기가 사용 중입니다. 종료한 뒤 재확인해주세요.", "Files or another launcher are in use. Close them, then retry." },
            LauncherRecoveryStatus.RecoveryRequired => new[] { "설치 소유 기록 또는 복구 상태를 확인해야 합니다. 진단 ZIP과 함께 제보하거나 새 전체 패키지로 복구해주세요.", "Installation ownership or recovery needs attention. Report a diagnostics ZIP or recover with the new complete package." },
            LauncherRecoveryStatus.Cancelled => new[] { "작업을 취소했습니다.", "The operation was cancelled." },
            LauncherRecoveryStatus.Queued => new[] { "도우미가 업데이트를 인계받았습니다. 런처를 종료합니다.", "The helper accepted the update. Closing the launcher." },
            _ => new[] { "업데이트 확인 또는 처리가 실패했습니다. 대응 패치가 없는 상태와는 다릅니다. 재확인 또는 진단 ZIP 만들기를 이용해주세요.", "Update lookup or processing failed; this is not a no-patch result. Retry or collect diagnostics." }
        };
        return text[korean ? 0 : 1];
    }
    private void RefreshText()
    {
        _intro.Text = T("게임 호환성 또는 실행 보호 검사로 모드 실행을 중단했습니다. 검증된 업데이트만 적용할 수 있습니다.", "Game compatibility or launch safety prevented mod startup. Only a verified update can be applied.");
        var installation = _client.Installation;
        _version.Text = installation == null ? "BetterAstralParty" : ChannelText(installation.ActiveChannel) + " " + installation.Version + " · " + T("선택 채널: ", "Selected channel: ") + ChannelText(installation.Channel)
            + (_client.Release == null ? "" : " → " + _client.Release.Version);
        _state.Text = StatusText(_status, _korean);
        if (_status == LauncherRecoveryStatus.RateLimited) _state.Text += " " + Math.Max(0, (int)(_client.RetryAt - DateTimeOffset.UtcNow).TotalSeconds) + "s";
        _notes.Text = _client.Release?.Notes ?? "";
        _retry.Text = T("재확인", "Retry"); _apply.Text = _client.Prepared == null ? T("다운로드 및 검증", "Download and verify") : T("업데이트 후 런처 종료", "Update and close launcher");
        _original.Text = T("순정 Steam 클라이언트로 실행", "Launch through Steam client"); _diagnostics.Text = T("진단 ZIP 만들기", "Create diagnostics ZIP"); _close.Text = _busy ? T("취소 후 닫기", "Cancel and close") : T("닫기", "Close");
        _retry.Enabled = !_busy && !_queued && DateTimeOffset.UtcNow >= _client.RetryAt;
        _apply.Enabled = !_busy && !_queued && _status is LauncherRecoveryStatus.Available or LauncherRecoveryStatus.Ready;
        _original.Enabled = _diagnostics.Enabled = !_busy && !_queued;
        _syncingChannel = true;
        try {
            _channelLabel.Text = T("이번 비상 업데이트 채널", "Channel for this emergency update");
            _channel.Items[0] = ChannelText("Stable"); _channel.Items[1] = ChannelText("PreRelease");
            _channel.SelectedIndex = _client.SelectedChannel == "PreRelease" ? 1 : 0;
            _channel.Enabled = !_busy && !_queued;
        } finally { _syncingChannel = false; }
    }
    private async Task Check(bool download, bool metadataOnly = false)
    {
        await Work(async token => {
            var status = await _client.Check(download, token).ConfigureAwait(false);
            if (!metadataOnly && !download && status == LauncherRecoveryStatus.Available && _client.Installation?.AutoDownload == true) status = await _client.Check(true, token).ConfigureAwait(false);
            return status;
        });
        if (_closing || IsDisposed || Disposing) return;
        if (_client.Installation != null && _status != LauncherRecoveryStatus.Checking && _language.Focused == false) {
            // Initial choice comes from the saved mod setting; later UI changes stay local to this window.
            if (_language.Tag == null) { _language.Tag = true; _language.SelectedIndex = _client.Installation.Korean ? 0 : 1; }
        }
    }
    private async Task Work(Func<CancellationToken, Task<LauncherRecoveryStatus>> action)
    {
        if (_busy) return; _busy = true; _cancel = new CancellationTokenSource(); _status = LauncherRecoveryStatus.Checking; RefreshText();
        try { _status = await Task.Run(() => action(_cancel.Token)); if (_status == LauncherRecoveryStatus.Queued) { _queued = true; _closing = true; } }
        catch (OperationCanceledException) { _status = LauncherRecoveryStatus.Cancelled; }
        catch (Exception error) { Failure(DiagnosticPhase.HelperQueue, error); _status = LauncherRecoveryStatus.RecoveryRequired; }
        finally { _busy = false; _cancel.Dispose(); _cancel = null; _nextCheck = DateTimeOffset.UtcNow.AddSeconds(60); RefreshText(); if (_closing) Close(); }
    }
    private async Task Collect()
    {
        if (_busy) return; _busy = true; RefreshText();
        DiagnosticHub.UiAction(DiagnosticUiAction.DiagnosticsCollect, DiagnosticOutcome.Begin, default);
        try {
            var result = await Task.Run(() => DiagnosticBundle.Collect(_root,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Logs"), Path.Combine(Path.GetTempPath(), "BetterAstralParty"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "feimo", "AstralParty_INT"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAstralParty", "Diagnostics"), Path.Combine(Path.GetTempPath(), "BetterAstralParty", "Diagnostics"),
                "", ""));
            result.OpenFolder(path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
            DiagnosticHub.UiAction(DiagnosticUiAction.DiagnosticsCollect, DiagnosticOutcome.Completed, default);
        } catch (Exception error) { Failure(DiagnosticPhase.UiAction, error); _status = LauncherRecoveryStatus.Failed; }
        finally { _busy = false; RefreshText(); if (_closing) Close(); }
    }
    private static void Failure(DiagnosticPhase phase, Exception error) => DiagnosticHub.Failure(DiagnosticFeature.Coordinator, phase, DiagnosticCode.Unknown, error);
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy) { _closing = true; _cancel?.Cancel(); e.Cancel = true; }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Stop(); _timer.Dispose(); _client.Dispose(); base.OnFormClosed(e); }
#if BAP_LAUNCHER_RECOVERY_FIXTURE
    protected override bool ShowWithoutActivation => true;
    internal bool FixtureBusy => _busy;
#endif
    internal static void Open(string root)
    {
        Application.EnableVisualStyles();
        using var form = new LauncherRecoveryForm(root); form.ShowDialog();
    }
}
