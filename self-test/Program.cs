using BetterAstralParty;
if (args.Contains("--release-updates-only"))
{
    ReleaseUpdateCheck.Run();
    return;
}
LanguageCheck.Run();
ReleaseUpdateCheck.Run();
ReleaseSessionCheck.Run();
CombatEffectsCheck.Run();

if (Array.IndexOf(args, "--compare-assembly") is var comparison && comparison >= 0)
{
    var baseline = System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[comparison + 1]));
    var comparisons = 0;
    void Compare(string calculator, object expected, params object[] inputs)
    {
        var parameters = inputs.Select(input =>
        {
            var localType = input.GetType();
            var previousType = baseline.GetType(localType.FullName!, true)!;
            var constructor = localType.GetConstructors().Single();
            var values = constructor.GetParameters().Select(p => localType.GetProperty(p.Name!)!.GetValue(input)).ToArray();
            return Activator.CreateInstance(previousType, values)!;
        }).ToArray();
        var method = baseline.GetType("BetterAstralParty." + calculator, true)!
            .GetMethod("Calculate", parameters.Select(p => p.GetType()).ToArray())!;
        var actual = method.Invoke(null, parameters);
        if (actual == null) throw new Exception($"기존 DLL 계산 결과 누락: {calculator}");
        // Probability presentation intentionally changed; retain numeric bounds and recommendation comparisons.
        foreach (var property in expected.GetType().GetProperties().Where(p => p.Name is not ("Details" or "DefendQuick" or "DodgeQuick")))
            if (property.GetValue(expected)?.ToString() != actual.GetType().GetProperty(property.Name)!.GetValue(actual)?.ToString())
                throw new Exception($"기존 DLL 계산 결과 불일치: {calculator}.{property.Name} / {string.Join(",", inputs)}");
        comparisons++;
    }
    foreach (var hp in new[] { 1, 9, 100 })
    foreach (var attack in new[] { 0, 5, 17 })
    foreach (var defense in new[] { -4, 0, 8 })
    foreach (var die in Enumerable.Range(1, 6))
    {
        var input = new CombatInput(hp, attack, die, defense, 0, 3);
        Compare(nameof(CombatAdvisor), CombatAdvisor.Calculate(input), input);
        var visible = new VisibleCombat(9, hp, attack, attack + 3, die, defense, defense + 3, true);
        Compare(nameof(VisibleCombatAdvisor), VisibleCombatAdvisor.Calculate(visible)!, visible);
    }
    foreach (var attack in new[] { true, false })
    foreach (var hp in new[] { 1, 9, 100 })
    foreach (var width in new[] { 0, 5, 20 })
    {
        var input = new PreRollCombat(9, hp, 0, width, 0, width);
        var card = new CardBonus(attack, 1, 6, 2);
        Compare(nameof(CardAdvisor), CardAdvisor.Calculate(input, card)!, input, card);
    }
    Console.WriteLine($"기존 DLL과 {comparisons}개 수치·추천 일치 (확률 표시 UI 변경 제외)");
    return;
}

if (args.Contains("--benchmark"))
{
    foreach (var (label, run) in new (string, Action)[] {
        ("visible-wide", () => VisibleCombatAdvisor.Calculate(new(9, 10, 0, 40, 4, 0, 40, true))),
        ("card-wide", () => CardAdvisor.Calculate(new(9, 10, 0, 20, 0, 20), new(true, 1, 6, 2))),
        ("card-normal", () => CardAdvisor.Calculate(new(9, 10, 7, 7, 1, 1), new(true, 1, 6, 2))) })
    {
        for (var i = 0; i < 15; i++) run();
        var times = new List<double>(); var allocations = new List<long>();
        for (var sample = 0; sample < 7; sample++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 30; i++) run();
            timer.Stop();
            allocations.Add((GC.GetAllocatedBytesForCurrentThread() - bytes) / 30);
            times.Add(timer.Elapsed.TotalMilliseconds / 30);
        }
        times.Sort(); allocations.Sort();
        Console.WriteLine($"{label}: median={times[3]:F4} ms/call, managed={allocations[3]} bytes/call");
    }
    return;
}

var diagnosticTemp = Directory.CreateTempSubdirectory("BetterAstralParty-diagnostics-test-").FullName;
DiagnosticLog? diagnostic = null;
try
{
    var directory = Path.Combine(diagnosticTemp, "logs");
    var warnings = new List<string>();
    diagnostic = new DiagnosticLog(directory, warnings.Add);
    diagnostic.SetEnabled(false, "test");
    diagnostic.Write("must not be recorded");
    diagnostic.State("home", "hidden");
    diagnostic.Performance(0, 1, 0.4, 0.2, 0.3, 128, 16, 0.1, 4, 6);
    if (Directory.Exists(directory)) throw new Exception("진단 기본 OFF에서 파일 생성");
    diagnostic.SetEnabled(true, "test version");
    var bufferedPath = Path.Combine(directory, "current.log");
    var flushedSize = new FileInfo(bufferedPath).Length;
    diagnostic.Write("batched_marker");
    if (new FileInfo(bufferedPath).Length != flushedSize) throw new Exception("ordinary lines flush individually");
    diagnostic.Flush();
    if (new FileInfo(bufferedPath).Length <= flushedSize) throw new Exception("batch flush failed");
    diagnostic.Performance(0, 1, 0.4, 0.2, 0.3, 128, 16);
    diagnostic.Performance(5, 2, 0.8, 0.1, 0.5, 256, 60, 0.2, 4, 12, 0.2, 32);
    diagnostic.State("home", "visible");
    diagnostic.State("home", "visible");
    try { throw new InvalidOperationException("PRIVATE_ACCOUNT_OR_CHAT_TEXT"); }
    catch (Exception ex) { diagnostic.Error("test", ex); }
    var current = Path.Combine(directory, "current.log");
    string first;
    using (var reader = new StreamReader(new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
        first = reader.ReadToEnd();
    if (first.Split("home=visible").Length != 2 || !first.Contains("System.InvalidOperationException")
        || first.Contains("PRIVATE_ACCOUNT_OR_CHAT_TEXT") || first.Contains("performance frames="))
        throw new Exception("진단 중복 억제/예외 기록/개인정보 제외/성능 로그 간격 실패");
    diagnostic.Performance(10, 3, 1, 0.3, 0.6, 384, 20, 0.4, 3, 3, 0.4, 64);
    diagnostic.SetEnabled(false, "");
    first = File.ReadAllText(current);
    if (!first.Contains("performance frames=3; ui_avg_ms=2.000; ui_max_ms=3.000")
        || !first.Contains("managed_bytes_per_frame=256; game_frame_max_ms=60.00; game_frames_ge50ms=1"))
        throw new Exception("10초 성능 요약 평균·최대·할당 집계 실패");
    if (!first.Contains("performance_field tick_avg_ms=0.200; tick_max_ms=0.400; plates_max=4; effects_max=12; native_layout_excluded=True"))
        throw new Exception("필드 전용 시간·표시기·버프 수 집계 실패");
    if (!first.Contains("performance_hand tick_avg_ms=0.200; tick_max_ms=0.400; managed_bytes_per_frame=32; native_layout_excluded=True"))
        throw new Exception("Hand performance aggregation failed");
    diagnostic.Write("OFF ignored");
    diagnostic.Error("test", new Exception("OFF ignored"));
    diagnostic.Performance(30, 100, 100, 0, 0, 1024, 100);
    if (File.ReadAllText(current) != first) throw new Exception("OFF 이후 기록됨");
    diagnostic.SetEnabled(true, "second session");
    diagnostic.Performance(40, 1, 1, 0, 0, 16, 10);
    if (File.ReadAllText(Path.Combine(directory, "previous.log")) != first)
        throw new Exception("직전 진단 세션 보존 실패");
    for (var i = 0; i < 400; i++) diagnostic.Write(new string('가', 4096));
    diagnostic.Flush();
    if (new FileInfo(current).Length > DiagnosticLog.MaxBytes || warnings.Count != 0 || !diagnostic.IsRecording
        || new FileInfo(Path.Combine(directory, "previous.log")).Length > DiagnosticLog.MaxBytes)
        throw new Exception("진단 용량 제한/경고 중복 방지 실패");
    diagnostic.SetEnabled(false, "");
    diagnostic.SetEnabled(true, "third session");
    diagnostic.SetEnabled(false, "");
    if (Directory.GetFiles(directory).Length != 2 || !File.ReadAllText(current).Contains("third session"))
        throw new Exception("진단 재활성화/보관 개수 제한 실패");
    // A file in place of the directory simulates an unwritable destination without permission changes.
    var blocked = Path.Combine(diagnosticTemp, "blocked");
    File.WriteAllText(blocked, "keep");
    var failed = new DiagnosticLog(blocked, warnings.Add);
    failed.SetEnabled(true, "test");
    failed.Write("ignored");
    failed.SetEnabled(false, "");
    if (warnings.Count != 1 || File.ReadAllText(blocked) != "keep")
        throw new Exception("진단 기록 실패 격리 실패");
    diagnostic.SetEnabled(true, "combat tests");
    if (!diagnostic.CombatPhase((IntPtr)111, (IntPtr)222, (IntPtr)333, 3, 9, 0)) throw new Exception("combat start");
    var combatId = diagnostic.CombatId;
    diagnostic.State("combat.actor.attacker", "heroId=127; type=1");
    diagnostic.State("combat.inputs", "hp=12; attackMin=4");
    diagnostic.State("combat.inputs", "hp=12; attackMin=4");
    if (diagnostic.CombatPhase((IntPtr)111, (IntPtr)222, (IntPtr)333, 3, 9, 0)) throw new Exception("duplicate phase");
    diagnostic.CombatPhase((IntPtr)111, (IntPtr)222, (IntPtr)333, 5, 9, 0);
    diagnostic.CombatEvent("prediction damage=11");
    diagnostic.CombatResult(12, 11, 2, new(2));
    var mismatch = Path.Combine(directory, "combat-mismatch.log");
    var evidence = File.ReadAllText(mismatch);
    if (!evidence.Contains(combatId) || !evidence.Contains("prediction damage=11")
        || !evidence.Contains("observedHP=2") || !evidence.Contains("heroId=127")
        || evidence.Contains("PRIVATE_ACCOUNT_OR_CHAT_TEXT")) throw new Exception("combat evidence incomplete");
    diagnostic.CombatPhase((IntPtr)111, (IntPtr)333, (IntPtr)222, 3, 9, 1);
    if (diagnostic.CombatId == combatId) throw new Exception("counterattack identity");
    diagnostic.CombatResult(12, 10, 2, new(1));
    if (File.ReadAllText(mismatch) != evidence) throw new Exception("matching result overwrote mismatch");
    // 0.30.7 local combats 41/59/61: monster defeat UI clamps HP to 1, not a survival rule.
    foreach (var row in new[] { (7,13,6,0), (9,17,9,2), (12,12,0,0) })
    {
        var damage = CombatAdvisor.FinalDamage(row.Item1,row.Item2,row.Item3,false,1,new(row.Item4));
        if (damage < row.Item1) throw new Exception("native display clamp must not alter KO damage");
        diagnostic.CombatResult(row.Item1,damage,1,new(row.Item4),true);
        if (File.ReadAllText(mismatch) != evidence) throw new Exception("monster defeat display false mismatch");
    }
    diagnostic.CombatResult(10,2,1,new(0),true);
    if (File.ReadAllText(mismatch) == evidence) throw new Exception("real nonlethal difference hidden by presentation clamp");
    diagnostic.CombatPhase((IntPtr)444, (IntPtr)222, (IntPtr)333, 5, 9, 2);
    diagnostic.State("combat.actor.attacker", "heroId=1067; type=2");
    for (var i=0; i<400; i++) diagnostic.CombatEvent("effect=" + i + new string('가', 2000));
    diagnostic.CombatResult(10, 2, 5, new(0));
    evidence = File.ReadAllText(mismatch);
    if (!evidence.Contains("truncated=True") || !evidence.Contains("heroId=1067")
        || evidence.Contains("heroId=127") || new FileInfo(mismatch).Length > 2 * DiagnosticLog.CombatMaxBytes + 4096
        || !File.Exists(Path.Combine(directory,"combat-mismatch-previous.log"))) throw new Exception("bounded isolated combat archive");
    diagnostic.SetEnabled(false, "");
    diagnostic.CombatPhase((IntPtr)555, (IntPtr)222, (IntPtr)333, 3, 9, 0);
    diagnostic.CombatResult(10, 2, 5, new(0));
    if (diagnostic.CombatId != "" || File.ReadAllText(mismatch) != evidence) throw new Exception("OFF combat collection");
    Console.WriteLine("Combat diagnostics: correlated phases, snapshot retention, bounded rotation, mismatch archives and OFF isolation passed.");
}
finally
{
    diagnostic?.SetEnabled(false, "");
    // Only the unique test-owned temporary directory is removed.
    if (!Path.GetFullPath(diagnosticTemp).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        throw new Exception("테스트 임시 경로 경계 오류");
    Directory.Delete(diagnosticTemp, true);
}
Console.WriteLine("충돌 진단 기본 OFF·즉시 ON/OFF·개인정보 제외·중복/용량 제한·직전 기록·쓰기 실패 격리 검증 통과");
Console.WriteLine("성능 진단 OFF 무기록·10초 집계·평균/최대/할당·긴 프레임 수 검증 통과");

foreach (var hp in new[] { 1, 9, 100 })
foreach (var attack in new[] { 0, 5, 17 })
foreach (var defense in new[] { -4, 0, 8 })
foreach (var die in Enumerable.Range(1, 6))
{
    var input = new CombatInput(hp, attack, die, defense, 0, 3);
    var detailed = CombatAdvisor.Calculate(input);
    var numeric = CombatAdvisor.Calculate(input, false);
    if (detailed.Defend != numeric.Defend || detailed.Dodge != numeric.Dodge
        || detailed.Recommendation != numeric.Recommendation || numeric.ReasonKo != "" || detailed.ReasonKo.Length == 0)
        throw new Exception("문자열 생략 계산 경로의 수치/추천 회귀");
}
Console.WriteLine("162개 입력에서 문자열 생략 경로의 피해·전투불능·추천 일치 검증 통과");

foreach (var action in new[] { "Enabled", "Details", "CardPopups", "BattleStatus", "FieldBuffs", "Names", "Diagnostics", "MuteUnfocused", "InputAttention" })
{
    if (MenuLayout.ToggleWidth(action) != 480 || MenuLayout.ToggleHeight != 60)
        throw new Exception("토글 고정 크기 규칙 변경");
    var x = 440;
    if (x + MenuLayout.ToggleWidth(action) > MenuLayout.Width - 24)
        throw new Exception("토글 폭이 설정창을 벗어남");
}
for (var row = 0; row < 7; row++)
    if (MenuLayout.ToggleY(row) + MenuLayout.ToggleHeight > MenuLayout.ToggleY(row + 1))
        throw new Exception("기능 토글 간격 침범");
if (MenuLayout.Height != 620 || MenuLayout.SettingsY + MenuLayout.SettingsHeight >= MenuLayout.FooterY
    || MenuLayout.FooterY + 80 > MenuLayout.Height - 20 || MenuLayout.ContentHeight(0) != MenuLayout.SettingsHeight)
    throw new Exception("Settings viewport/footer must fit the native frame");
foreach (var content in new[] { 390f, 460f, 2000f })
{
    var top = MenuLayout.ScrollMarker(390, content, -10);
    var bottom = MenuLayout.ScrollMarker(390, content, content);
    if (top.Y != 0 || top.Height < 24 || top.Height > 390 || Math.Abs(bottom.Y + bottom.Height - 390) > 0.001f)
        throw new Exception("Scroll marker must stay within its track at both ends");
}
foreach (var rows in new[] { 0, 1, 7, 20 })
{
    var bottom = rows == 0 ? 0 : MenuLayout.ToggleY(rows - 1) + MenuLayout.ToggleHeight - MenuLayout.SettingsY;
    var height = MenuLayout.ContentHeight(bottom);
    if (height < bottom || height < MenuLayout.SettingsHeight || bottom - (height - MenuLayout.SettingsHeight) > MenuLayout.SettingsHeight)
        throw new Exception("Last settings row must remain reachable as the list grows");
}
if (!MenuLayout.InSettings(0, 0) || !MenuLayout.InSettings(200, 389)
    || MenuLayout.InSettings(-1, 20) || MenuLayout.InSettings(20, -1)
    || MenuLayout.InSettings(MenuLayout.SettingsWidth, 20) || MenuLayout.InSettings(20, MenuLayout.SettingsHeight)
    || !MenuLayout.FixedControl("Close") || MenuLayout.FixedControl("HandLayout"))
    throw new Exception("Settings clipping must exclude offscreen controls, not fixed footer/tab controls");
foreach (var renderedHeight in new[] { 60f, 85f, 100f, 120f })
{
    var next = MenuLayout.NextTabY(330, renderedHeight);
    if (next - (330 + renderedHeight) < 16 || next + renderedHeight > MenuLayout.Height - 24)
        throw new Exception("Native tab button height must leave a gap and stay inside the sheet");
}
if (125 + 200 > 330 || 255 + MenuLayout.ToggleHeight > 320 || 320 + MenuLayout.ToggleHeight > 390 || 455 + 60 > 520)
    throw new Exception("왼쪽 표시 설정·진단 로그 겹침/설정창 경계 침범");
foreach (var action in new[] { "Enabled", "Details", "CardPopups", "BattleStatus", "FieldBuffs", "Names", "Diagnostics", "MuteUnfocused", "InputAttention",
    "ScaleMinus", "ScalePlus", "OpacityMinus", "OpacityPlus", "Close" })
{
    var help = MenuLayout.Help(action);
    if (help == MenuLayout.Help("") || help.Title.Length == 0 || help.Body.Length == 0 || help.Body.Length > 80)
        throw new Exception($"호버 설명 누락/장문: {action}");
}
if (MenuLayout.Help("ScaleMinus") != MenuLayout.Help("ScalePlus")
    || MenuLayout.Help("OpacityMinus") != MenuLayout.Help("OpacityPlus"))
    throw new Exception("같은 설정의 증감 버튼 설명 불일치");
Console.WriteLine("고정 토글 크기·행 간격·좌우 경계·전체 버튼 호버 설명 검증 통과");
HandLayoutCheck.Run();
ResolutionCheck.Run();
FocusAudioCheck.Run();
foreach (var (old, expected) in new[] { ("true", "Taskbar"), ("True", "Taskbar"), ("false", "Off"),
    ("False", "Off"), ("Taskbar", "Taskbar"), ("Windows", "Windows"), ("Off", "Off"), ("invalid", "Taskbar") })
    if (InputAttentionState.Normalize(old) != expected) throw new Exception("Input alert config migration");
if (InputAttentionState.Next("Taskbar") != "Windows" || InputAttentionState.Next("Windows") != "Off"
    || InputAttentionState.Next("Off") != "Taskbar") throw new Exception("Input alert mode cycle");
if (System.Runtime.InteropServices.Marshal.SizeOf<WindowsInputNotification.NotifyIconData>() != 976
    || System.Runtime.InteropServices.Marshal.OffsetOf<WindowsInputNotification.NotifyIconData>("Info").ToInt32() != 304
    || System.Runtime.InteropServices.Marshal.OffsetOf<WindowsInputNotification.NotifyIconData>("Identity").ToInt32() != 952)
    throw new Exception("NOTIFYICONDATAW x64 layout mismatch");
for (var bits = 0; bits < 64; bits++)
foreach (var pending in new[] { 0, 1, 3 })
{
    var actual = InputAttentionState.ShouldFlash((bits & 1) != 0, (bits & 2) != 0,
        (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0, (bits & 32) != 0, pending);
    if (actual != (bits == 45 && pending > 0)) throw new Exception("Input alert focus/role/room/settings gate");
}
foreach (var name in new[] { "RelicWindow", "LoseCardWindow", "LandShopWindow", "AssistVoteWindow" })
    if (!InputAttentionState.InputWindow(name)) throw new Exception("Untimed input dialog missing");
foreach (var name in new[] { "SettingWindow", "ChatWindow", "BattlePlayerInfoWindow", "ReplayWindow" })
    if (InputAttentionState.InputWindow(name)) throw new Exception("Non-input dialog alerts");
if (!MenuLayout.GeneralControl("InputAttention")) throw new Exception("Input alert settings tab");
ModText.Select("English");
foreach (var mode in new[] { "Taskbar", "Windows", "Off" })
    if (ModText.Text(InputAttentionState.Label(mode)).Any(c => c is >= '\uAC00' and <= '\uD7A3'))
        throw new Exception("Input alert mode English label missing");
if (ModText.Text("게임에서 입력을 기다리고 있습니다.").Any(c => c is >= '\uAC00' and <= '\uD7A3'))
    throw new Exception("Windows notification English message missing");
if (ModText.Text(MenuLayout.Help("InputAttention").Title) != "Input Alert"
    || ModText.Text(MenuLayout.Help("InputAttention").Body).Any(c => c is >= '\uAC00' and <= '\uD7A3'))
    throw new Exception("Input alert English translation");
ModText.Select("한국어");
Console.WriteLine("Input alert: 192 focus/role/settings/timer cases and untimed dialog filters passed.");

foreach (var map in new[] { 0, 1, 2, 3, 4, 6, 9, 10, 12, 99 })
foreach (var step in Enumerable.Range(0, 9))
{
    if (BattleStatusLayout.Allowed(true, map, step) != (VisibleCombatAdvisor.IsPve(map) && step >= 1 && step <= 6)
        || BattleStatusLayout.Allowed(false, map, step)) throw new Exception("전투 공개 상태 PvE/단계/OFF 제한 실패");
}
foreach (var count in Enumerable.Range(0, 100))
{
    var pages = BattleStatusLayout.Pages(count);
    var indices = Enumerable.Range(0, pages).SelectMany(p => Enumerable.Range(p * BattleStatusLayout.PageSize,
        Math.Min(BattleStatusLayout.PageSize, Math.Max(0, count - p * BattleStatusLayout.PageSize)))).ToArray();
    if (!indices.SequenceEqual(Enumerable.Range(0, count)) || BattleStatusLayout.ClampPage(100, count) != pages - 1
        || BattleStatusLayout.ClampPage(-1, count) != 0) throw new Exception("공개 효과 페이지 누락/중복/삭제 후 경계 오류");
}
if (BattleStatusLayout.Counters(3, 2, false) != "중첩·진행 3 · 남은 턴 2"
    || BattleStatusLayout.Counters(7, 0, true) != "현재 수치 7"
    || BattleStatusLayout.Counters(0, 0, false) != ""
    || BattleStatusLayout.Counters(0, -1, false) != "") throw new Exception("공식 진행/수치 구분 또는 미지정 지속시간 오표시");
Console.WriteLine("전투 공개 상태 범위·효과 페이지·중첩/남은 턴 표기 검증 통과");

foreach (var (w, h) in new[] { (1920f, 1080f), (1280f, 720f), (2560f, 1080f) })
{
    var foot = BattleStatusLayout.Project(0.35f, 0.3f, 10, 0, 0, 1, 1, w, h)!.Value;
    if (Math.Abs(foot.X - w * 0.35f) > 0.001 || Math.Abs(foot.Y - h * 0.7f) > 0.001)
        throw new Exception("캐릭터 발밑 투영: 배율/상하 반전 오류");
    var viewport = BattleStatusLayout.Project(0.5f, 0.25f, 10, 0.1f, 0.2f, 0.8f, 0.6f, w, h)!.Value;
    if (Math.Abs(viewport.X - w * 0.5f) > 0.001 || Math.Abs(viewport.Y - h * 0.65f) > 0.001)
        throw new Exception("전투 카메라 부분 뷰포트 좌표 오류");
}
foreach (var (x, y, z) in new[] { (-1f, 0.5f, 10f), (0.5f, 2f, 10f), (0.5f, 0.5f, -1f), (float.NaN, 0f, 10f) })
    if (BattleStatusLayout.Project(x, y, z, 0, 0, 1, 1, 1920, 1080) != null)
        throw new Exception("잘못된 투영점을 화면 모서리로 강제 배치함");
Console.WriteLine("전투 카메라 투영·해상도·뷰포트·화면 밖 숨김 검증 통과");

// Installed Fight_Com_Defenser: heart n0 x=295, number x=352 (background x=271).
Equal(-57, BattleStatusLayout.HpEdgeX(false, 352, 100, 295), "오른쪽 하트 왼쪽 정렬");
Equal(100, BattleStatusLayout.HpEdgeX(true, 87, 100, 6), "왼쪽 HP 기준점 유지");
Equal(-57, BattleStatusLayout.HpEdgeX(false, 442, 100, 385), "HP 전체 이동 시 정렬 유지");
var alignedRight = BattleStatusLayout.BelowHp(1344 - 57 * 0.8f, 728, 128, 1, 1920, 1080, false)!.Value;
Equal(1298.4f, alignedRight.X, "오른쪽 줄을 배경 여백 대신 하트에 정렬");
Equal(1, alignedRight.Scale, "오른쪽 펼침 크기 유지");

foreach (var scale in new[] { 0.5f, 0.75f, 1f, 1.5f })
foreach (var width in new[] { 80f, 176f, BattleStatusLayout.Width })
foreach (var attacker in new[] { true, false })
{
    var hpX = attacker ? 800f : 1120f;
    var start = BattleStatusLayout.BelowHp(hpX, 728, width, scale, 1920, 1080, attacker)!.Value;
    var moved = BattleStatusLayout.BelowHp(hpX + 90, 688, width, scale, 1920, 1080, attacker)!.Value;
    Equal(hpX, attacker ? start.X + width * scale : start.X, "HP 바깥 방향 정렬");
    Equal(736, start.Y, "HP 아래 8px 간격");
    Equal(90, moved.X - start.X, "캐릭터 수평 이동 추적");
    Equal(-40, moved.Y - start.Y, "캐릭터 수직 이동 추적");
    foreach (var (x, y) in new[] { (50f, 20f), (1870f, 1000f) })
    {
        var edge = BattleStatusLayout.BelowHp(x, y, width, scale, 1920, 1080, attacker)!.Value;
        if (edge.X < 7.99f || edge.Y < 8 || edge.X + width * edge.Scale > 1912.01f
            || edge.Y + BattleStatusLayout.Height * edge.Scale > 1072.01f
            || (attacker ? edge.X + width * edge.Scale > x + 0.01f : edge.X < x))
            throw new Exception("HP 아래 버프 표시 화면 경계/안쪽 침범 실패");
    }
}
foreach (var count in Enumerable.Range(0, 7))
foreach (var paged in new[] { false, true })
foreach (var attacker in new[] { false, true })
{
    var width = BattleStatusLayout.RowWidth(count, paged);
    var padding = BattleStatusLayout.EdgePadding;
    var cardX = attacker ? width - padding - BattleStatusLayout.CardWidth : padding;
    for (var i = 0; i < count; i++)
    {
        var x = BattleStatusLayout.IconX(i, width, attacker);
        if (x < padding || x + 48 > width - padding || (attacker ? x + 48 > cardX : x < cardX + 80)
            || (paged && (attacker ? x < padding + 50 : x + 48 > width - padding - 50)))
            throw new Exception("한 줄 카드/버프/페이지 영역 겹침");
        // Every corner of square artwork must be inside the capsule's fill, not on its 3px border.
        var iconX = x + BattleStatusLayout.IconInset;
        var iconY = (BattleStatusLayout.Height - BattleStatusLayout.IconSize) / 2;
        foreach (var cornerX in new[] { iconX, iconX + BattleStatusLayout.IconSize })
        foreach (var cornerY in new[] { iconY, iconY + BattleStatusLayout.IconSize })
        {
            var radius = BattleStatusLayout.Height / 2;
            var centerX = Math.Clamp(cornerX, radius, width - radius);
            var dx = cornerX - centerX;
            var dy = cornerY - radius;
            if (dx * dx + dy * dy >= (radius - 3) * (radius - 3))
                throw new Exception("버프 사각 아이콘이 캡슐 곡선/테두리를 침범함");
        }
    }
}
Console.WriteLine("양쪽 버프 0~6개·페이지 유무별 캡슐 여백·아이콘 네 모서리 경계 검사 통과");
if (BattleStatusLayout.BelowHp(float.NaN, 300, 100, 1, 1920, 1080, true) != null
    || BattleStatusLayout.BelowHp(500, 1080, 100, 1, 1920, 1080, true) != null
    || BattleStatusLayout.BelowHp(-5, 400, 100, 1, 1920, 1080, true) != null)
    throw new Exception("잘못된 HP 앵커 숨김 실패");
Console.WriteLine("HP 아래 바깥 정렬·한 줄 영역·8px 간격·HP 이동 추적·화면 경계 검증 통과");

foreach (var (w, h) in new[] { (1920f, 1080f), (1280f, 720f), (800f, 600f), (2560f, 1080f) })
foreach (var (x, y) in new[] { (0f, 0f), (w, h), (w / 2, h / 2), (w - 150, h - 80) })
foreach (var desired in new[] { 0.75f, 1f, 1.5f })
{
    var p = CardPreviewLayout.Place(w, h, x, y, desired);
    if (p.Scale <= 0 || p.X < 16 || p.Y < 16 || p.X + CardPreviewLayout.Width * p.Scale > w - 15.9f
        || p.Y + CardPreviewLayout.Height * p.Scale > h - 15.9f) throw new Exception("카드 팝업 화면 경계 실패");
}
var cardLookup = new Dictionary<string, int>();
CardPreviewLayout.AddUnique(cardLookup, "나 [color=#FF0000]공격(대)[/color] 있어", 123);
CardPreviewLayout.AddUnique(cardLookup, "중복", 1);
CardPreviewLayout.AddUnique(cardLookup, "중복", 2);
CardPreviewLayout.AddUnique(cardLookup, "중복", 1);
CardPreviewLayout.AddUnique(cardLookup, "", 123);
if (cardLookup.Count != 2 || cardLookup["중복"] != 0 || cardLookup.ContainsKey("공격(대)")
    || cardLookup["나 [color=#FF0000]공격(대)[/color] 있어"] != 123)
    throw new Exception("카드 핑 완전 일치·모호한 이름 차단 실패");
Console.WriteLine("카드 팝업 해상도·가장자리·배율·핑 완전 일치 검증 통과");

var relicLookup = new Dictionary<string, int>();
const string chipPing = "나 [color=#9700E6]휴대용 선풍기 - 대형[/color] 있어";
CardPreviewLayout.AddUnique(relicLookup, chipPing, 123); // Same numeric ID as a card is not the same item.
if (CardPreviewLayout.Resolve(cardLookup, relicLookup, chipPing) != (true, 123)
    || CardPreviewLayout.Resolve(cardLookup, relicLookup, "나 [color=#FF0000]공격(대)[/color] 있어") != (false, 123)
    || CardPreviewLayout.Resolve(cardLookup, relicLookup, "휴대용 선풍기").Id != 0)
    throw new Exception("카드/칩 종류 구분 또는 핑 완전 일치 실패");
CardPreviewLayout.AddUnique(cardLookup, chipPing, 999);
if (CardPreviewLayout.Resolve(cardLookup, relicLookup, chipPing).Id != 0)
    throw new Exception("카드와 칩의 동일 문구 충돌 차단 실패");
CardPreviewLayout.AddUnique(relicLookup, "중복 칩", 1);
CardPreviewLayout.AddUnique(relicLookup, "중복 칩", 2);
if (CardPreviewLayout.Resolve(cardLookup, relicLookup, "중복 칩").Id != 0)
    throw new Exception("중복 칩 문구 차단 실패");
foreach (var (w, h) in new[] { (800f, 600f), (1920f, 1080f), (2560f, 1080f) })
foreach (var chipHeight in new[] { 620f, 900f, 1400f })
foreach (var (x, y) in new[] { (0f, 0f), (w, h), (w / 2, h / 2) })
{
    var p = CardPreviewLayout.Place(w, h, x, y, 1.5f, 460, chipHeight);
    if (p.X < 16 || p.Y < 16 || p.X + 460 * p.Scale > w - 15.9f || p.Y + chipHeight * p.Scale > h - 15.9f)
        throw new Exception("칩 긴 설명 팝업 화면 경계 실패");
}
Console.WriteLine("칩 핑 완전 일치·카드/칩 ID 분리·문구 충돌·긴 설명 화면 경계 검증 통과");

var nameMap = CharacterNames.BuildMap(new (string, string)[]
{
    ("이명 A", "본명 A"), ("이명 A", "본명 A"), ("중복", "첫째"), ("중복", "둘째"),
    ("빈 이름", ""), ("", "이름"), ("동일", "동일")
});
if (nameMap.Count != 1 || nameMap["이명 A"] != "본명 A" || nameMap.ContainsKey("이명 A의 설명"))
    throw new Exception("본명 대응/중복·누락·부분 일치 안전 처리 실패");
if (CharacterNames.Restore("본명 A", "이명 A", "본명 A") != "이명 A"
    || CharacterNames.Restore("다른 캐릭터", "이명 A", "본명 A") != "다른 캐릭터")
    throw new Exception("이명 복원/재사용 라벨 보호 실패");
if (CharacterNames.LabelField("UIHero_Button_Hero") != "txt_chrname"
    || CharacterNames.LabelField("UIRoomHero_Com_CharacterName") != "txt_Title"
    || CharacterNames.LabelField("UIAccountInfo_Button_RankInfo") != "txt_HeroNick"
    || CharacterNames.LabelField("UIAccountInfo_Com_Main") != "txt_Hero"
    || CharacterNames.LabelProperty("UIAccountInfo_Com_Main") != "data"
    || CharacterNames.LabelProperty("UIAccountInfo_Button_RankInfo") != "text"
    || CharacterNames.LabelProperty("UIActivityStoreSeason_Button_Hero") != "title"
    || CharacterNames.LabelField("UIHero_Com_FileArchive") != null
    || CharacterNames.LabelField("UIChat_Com_ChatItem") != null
    || CharacterNames.LabelField("UIRoomHero_Com_Player") != null)
    throw new Exception("캐릭터 이름 칸 범위 제한 실패");
Console.WriteLine("본명/이명 대응·복원·계정/채팅/프로필 보호 검증 통과");
if (!CharacterNames.SkipSubtree("UICom_AttrInfo") || !CharacterNames.SkipSubtree("UIButton_Buff")
    || CharacterNames.SkipSubtree("UIAccountInfo_Com_Main") || CharacterNames.SkipSubtree("UIHero_Button_Hero"))
    throw new Exception("필드 검색 제외/실제 본명 칸 유지 실패");

for (var step = 0; step <= 8; step++)
{
    if (BattleStatusLayout.RowInputOrder(step) != (step >= 3 && step <= 6 ? 1 : 0))
        throw new Exception("버프 행 입력 순서: 준비 단계 카드 위/컷신 아래 유지 실패");
}
Console.WriteLine("전투 버프: 조우~결과 단계 준비 및 원본 컷신 입력 순서 검증 통과");

static void Equal(double expected, double actual, string name)
{
    if (Math.Abs(expected - actual) > 0.000_001)
        throw new Exception($"{name}: expected {expected}, got {actual}");
}

var lowRoll = CombatAdvisor.Calculate(new CombatInput(Hp: 10, FinalAttack: 6, AttackerDie: 1, BaseDefense: 2));
Equal(5d / 6d, lowRoll.Dodge.NoDamageChance, "공격 주사위 1 회피율");

var sixRoll = CombatAdvisor.Calculate(new CombatInput(Hp: 10, FinalAttack: 6, AttackerDie: 6, BaseDefense: 2));
Equal(1d / 6d, sixRoll.Dodge.NoDamageChance, "공격 주사위 6 회피율");

var hpOne = CombatAdvisor.Calculate(new CombatInput(Hp: 1, FinalAttack: 3, AttackerDie: 1, BaseDefense: 99));
if (hpOne.Recommendation != RecommendedAction.Dodge)
    throw new Exception("HP 1에서는 최소 피해 1 때문에 회피를 추천해야 합니다.");

var highAttack = CombatAdvisor.Calculate(new CombatInput(Hp: 5, FinalAttack: 10, AttackerDie: 5, BaseDefense: 4, DefenseBonusMin: 1, DefenseBonusMax: 3));
if (highAttack.Defend.ExpectedDamage <= 0 || highAttack.Dodge.ExpectedDamage <= 0)
    throw new Exception("피해 기대값은 양수여야 합니다.");

Console.WriteLine("BetterAstralParty 전투 계산기 자체 검증 통과");

foreach (var map in new[] { 0, 1, 2, 3, 5, 7, 8, 11, 99 })
    if (VisibleCombatAdvisor.Calculate(new(map, 10, 4, 4, 4, 4, 9, true)) != null)
        throw new Exception($"PvP/미확인 모드 {map}에서 추천 노출");
foreach (var map in new[] { 4, 6, 9, 10, 12 })
    if (VisibleCombatAdvisor.Calculate(new(map, 10, 4, 4, 4, 4, 9, true)) == null)
        throw new Exception($"PvE 모드 {map}에서 추천 없음");
var blockedDodge = VisibleCombatAdvisor.Calculate(new(4, 1, 4, 4, 1, 4, 9, false))!.Value;
if (blockedDodge.Dodge.Contains("추천") || !blockedDodge.Dodge.Contains("불가"))
    throw new Exception("사용 불가능한 회피 추천");
if (VisibleCombatAdvisor.Calculate(new(4, 10, 4, 3, 4, 4, 9, true)) != null
    || VisibleCombatAdvisor.Calculate(new(4, 10, 4, 4, 7, 4, 9, true)) != null)
    throw new Exception("잘못된 범위/특수 주사위 입력 허용");
var uncertain = VisibleCombatAdvisor.Calculate(new(4, 2, 1, 10, 3, 0, 10, true))!.Value;
if (uncertain.Recommendation != RecommendedAction.Dodge || !uncertain.Dodge.Contains("추천")
    || !uncertain.DefendQuick.Contains('~') || uncertain.DodgeQuick != "예상 피해 1\n전투불능 50%")
    throw new Exception("불확실 범위 보존/최악 KO 기준 추천 실패");
Console.WriteLine("PvE 제한·회피 불가·불확실 범위 검증 통과");

foreach (var (width, height) in new[] { (1920f, 1080f), (1280f, 720f), (1024f, 768f), (2560f, 1080f), (800f, 600f) })
{
    var scale = MenuLayout.FitScale(width, height);
    if (scale <= 0 || scale > 1 || MenuLayout.Width * scale > width - 47 || MenuLayout.Height * scale > height - 47)
        throw new Exception($"설정창이 화면을 벗어남: {width} x {height}");
}
Equal(1, MenuLayout.FitScale(1920, 1080), "기준 해상도 설정창 배율");
Console.WriteLine("모드 메뉴 해상도별 화면 경계 검증 통과");

var attackCard = CardAdvisor.Parse("전투 중 공격자 사용 가능\n공격력 +1~6", 2);
if (attackCard != new CardBonus(true, 1, 6, 2)) throw new Exception("공격 카드 범위 해석 실패");
if (CardAdvisor.Parse("전투 중 방어자 사용 가능\n[color=#fff]방어력 +2[/color]", 1) != new CardBonus(false, 2, 2, 1))
    throw new Exception("방어 카드/서식 해석 실패");
foreach (var description in new[] { "공격력 +1~6\n회피 불가", "공격력 +1\n체력 -1", "방어력 +6~1", "공격력 +999",
    "전투 중 방어자 사용 가능 공격력 +3", "공격력 +1%", "공격력 +1~6 이외 효과", "알 수 없음" })
    if (CardAdvisor.Parse(description, 1) != null) throw new Exception($"복합/잘못된 카드 허용: {description}");

Equal(0, CardDiceLayout.Alpha(0, 0, -1), "KO entrance start");
// Public rolls fix the attack condition for every role; before revelation search all faces.
var requiredKo = new PreRollCombat(9, 9, 4, 4, 0, 0);
foreach (var role in new[] { 0, 1, 2 })
foreach (var revealed in Enumerable.Range(0, 7))
{
    var thresholdDie = revealed;
    var pair = role == 1 ? CardKoMinimum.Survival(requiredKo, null, thresholdDie)
        : CardKoMinimum.Calculate(requiredKo, null, thresholdDie);
    if (role != 1 && revealed == 0 && (pair != new KoPair(6, 1)
        || CardDiceLayout.Content(pair, thresholdDie, 0) != (6, "≥", "")
        || CardDiceLayout.Content(pair, thresholdDie, 1) != (1, "≤", "")
        || CardDiceLayout.Title() != "KO 최소 조건"))
        throw new Exception("Pre-roll minimum KO conditions");
    if (revealed > 0
        && CardDiceLayout.Content(pair, thresholdDie, 0, role == 1) != (revealed, "=", ""))
        throw new Exception("Every role must show the revealed roll as an equality");
    if (role != 1 && revealed is > 0 and < 6 && pair != new KoPair(revealed, 0))
        throw new Exception("Impossible revealed rolls must not reuse pre-roll KO thresholds");
}
// 0.17.2 log at 03:17:47: public attack 5..14, roll 2, target HP9/DEF0.
// Random card gain is not yet resolved: KO can occur, but is not guaranteed.
var loggedRangedKo = new PreRollCombat(9, 9, 5, 14, 0, 0);
var loggedPair = CardKoMinimum.Calculate(loggedRangedKo, null, 2);
if (loggedPair != new KoPair(2, 0)
    || CardKoMinimum.Calculate(loggedRangedKo, null, 2, true) != new KoPair(2, 6))
    throw new Exception("Logged revealed roll must retain unresolved card bounds without claiming impossible KO");
// A higher revealed die relaxes the defender threshold instead of keeping the minimum pair.
if (CardKoMinimum.Calculate(new(9, 5, 4, 4, 0, 0), null, 6) != new KoPair(6, 5))
    throw new Exception("Revealed high roll must recompute defense limit");
Equal(14, CombatAdvisor.FinalDamage(10, 17, 4, false, 3, new(1)), "03:17:25 final damage");
Equal(10, CombatAdvisor.FinalDamage(9, 14, 4, false, 2, default), "03:17:50 final damage");
foreach (var row in new[] { 0, 1 })
{
    Equal((row + .5) * CardDiceLayout.ColumnWidth,
        CardDiceLayout.Row(row) + CardDiceLayout.Diameter / 2, "KO circle centred on role");
    foreach (var fixedDie in Enumerable.Range(0, 7))
    foreach (var pair in new KoPair?[] { null, new(0, 0), new(fixedDie, 0), new(4, 1), new(1, 6) })
    {
        var content = CardDiceLayout.Content(pair, fixedDie, row);
        if (row == 0 && fixedDie > 0)
        {
            if (content != (fixedDie, "=", "")) throw new Exception("Revealed roll must not imply guaranteed KO");
        }
        else if (pair == null)
        {
            if (content != (0, "", "?")) throw new Exception("Unknown must keep circle with question mark");
        }
        else if (pair.Value.DefenseDie == 0)
        {
            if (content != (0, "", "—")) throw new Exception("No threshold must not invent comparison dice");
        }
        else if (row == 1 && pair.Value.DefenseDie == 6)
        {
            if (content != (0, "", "—")) throw new Exception("Every achievable defense face requires no comparison");
        }
        else if (content.Symbol != (row == 0 ? "≥" : "≤") || content.Die is < 1 or > 6)
            throw new Exception("Inclusive conditions inside both circles");
    }
}
if (CardDiceLayout.Status(null, null, 9) != "계산 정보 부족"
    || CardDiceLayout.Status(new(3, 0), (4, 8), 9) != "KO 불가"
    || CardDiceLayout.Status(new(3, 0), (4, 9), 9) != "계산 정보 부족"
    || CardDiceLayout.Status(new(3, 0), (0, 0), 9) != "KO 불가"
    || CardDiceLayout.Status(new(3, 0), (9, 12), 9, true) != "확정 KO"
    || CardDiceLayout.Status(new(3, 0), (8, 12), 9, true) != "계산 정보 부족"
    || CardDiceLayout.Status(new(3, 0), null, 9) != "계산 정보 부족"
    || CardDiceLayout.Status(new(3, 1), null, 9) != ""
    || CardDiceLayout.Title() != "KO 최소 조건")
    throw new Exception("Condition-only status or impossible/uncertain distinction mismatch");
var rangeInput = new PreRollCombat(9, 9, 1, 5, 2, 3);
// Certainty must cover every possible die, including unresolved card gains and immunity.
foreach (var hp in new[] { 1, 5, 20 })
foreach (var fixedRoll in Enumerable.Range(0, 7))
foreach (var modifiers in new HitModifiers[] { default, new(Immune: true), new(Bonus: -1), new(Cap: 2), new() { MinimumHp = 1 }, new() { OnHitBonus = 1 } })
foreach (var bonus in new CardBonus?[] { null, new(true, 1, 6, 1), new(false, 1, 6, 1) })
{
    var input = new PreRollCombat(9, hp, 2, 4, 1, 3) { Modifiers = modifiers };
    long min = long.MaxValue, max = long.MinValue;
    foreach (var atk in Enumerable.Range(1, 6).Where(d => fixedRoll == 0 || d == fixedRoll))
    foreach (var def in Enumerable.Range(1, 6))
    {
        var hit = CardKoMinimum.DamageRange(input, bonus, atk, def)!.Value;
        min = Math.Min(min, hit.Min); max = Math.Max(max, hit.Max);
    }
    var bounds = CardKoMinimum.OutcomeRange(input, bonus, fixedRoll);
    if (bounds != (min, max)) throw new Exception("Outcome bounds missed a die/card possibility");
    foreach (var survival in new[] { false, true })
    {
        var pair = survival ? CardKoMinimum.Survival(input, bonus, fixedRoll) : CardKoMinimum.Calculate(input, bonus, fixedRoll);
        var status = CardDiceLayout.Status(pair, bounds, hp, survival);
        if (min < hp && max >= hp && pair is { DefenseDie: 0 })
        {
            var candidate = survival ? CardKoMinimum.Survival(input, bonus, fixedRoll, true) : CardKoMinimum.Calculate(input, bonus, fixedRoll, true);
            if (candidate is not { DefenseDie: > 0 }) throw new Exception("Mixed outcome needs numeric possible conditions");
            var hit = CardKoMinimum.DamageRange(input, bonus, candidate.Value.AttackDie, candidate.Value.DefenseDie)!.Value;
            if (survival ? hit.Min >= hp : hit.Max < hp) throw new Exception("Possible threshold is not achievable");
            if (CardDiceLayout.Status(candidate, bounds, hp, survival) != "") throw new Exception("Possible conditions must not be withheld");
        }
        if ((status == "확정 KO") != (min >= hp) || (status == "KO 불가") != (max < hp))
            throw new Exception("Certain outcome differs by role or ignores immunity/card range");
        if (status is "확정 KO" or "KO 불가")
        {
            if (CardDiceLayout.Content(pair, fixedRoll, 1, survival, true) != (0, "", "—"))
                throw new Exception("Unconditional outcome must not show a misleading defense threshold");
            if (fixedRoll > 0 && CardDiceLayout.Content(pair, fixedRoll, 0, survival, true) != (fixedRoll, "=", ""))
                throw new Exception("Keep the actual public attack die");
        }
    }
}
if (CardDiceLayout.Title(true) != "생존 최소 조건"
    || CardDiceLayout.Content(new(6, 1), 0, 0, true) != (6, "≤", "")
    || CardDiceLayout.Content(new(6, 1), 0, 1, true) != (0, "", "—")
    || CardDiceLayout.Content(new(4, 2), 4, 0, true) != (4, "=", "")
    || CardDiceLayout.Content(new(0, 0), 0, 0, true) != (0, "", "—")
    || CardDiceLayout.Content(new(0, 0), 0, 1, true) != (0, "", "—")
    || CardDiceLayout.Content(new(4, 0), 4, 0, true) != (4, "=", "")
    || CardDiceLayout.Content(null, 0, 1, true) != (0, "", "?")
    || CardKoMinimum.Survival(rangeInput with { MapType = 0 }, null) != null
    || CardKoMinimum.Survival(rangeInput, null, 7) != null
    || CardKoMinimum.Survival(rangeInput, new(false, 5, 1, 1)) != null)
    throw new Exception("Survival role, signs, fallback or validation mismatch");
if (CardKoMinimum.DamageRange(rangeInput, null, 6, 1) != (3L, 8L)
    || CardKoMinimum.DamageRange(rangeInput, new(true, 1, 6, 2), 6, 1) != (4L, 14L)
    || CardKoMinimum.DamageRange(rangeInput, new(false, 1, 6, 2), 6, 1) != (1L, 7L)
    || CardKoMinimum.DamageRange(rangeInput, null, 3, 1) != (1L, 5L)
    || CardKoMinimum.DamageRange(rangeInput with { MapType = 0 }, null, 6, 1) != null
    || CardKoMinimum.DamageRange(rangeInput, null, 0, 1) != null
    || CardKoMinimum.DamageRange(rangeInput, null, 6, 7) != null
    || CardKoMinimum.DamageRange(rangeInput, new(true, 6, 1, 2), 6, 1) != null)
    throw new Exception("Displayed-dice damage bounds/validation mismatch");
foreach (var screen in new[] { (800f, 600f), (1280f, 720f), (1920f, 1080f), (3440f, 1440f) })
foreach (var scale in new[] { .5f, 1f, 1.5f, 2f })
    if (CardDiceLayout.Place(screen.Item1, screen.Item2, CardDiceLayout.Width * scale, CardDiceLayout.Height * scale) == null)
        throw new Exception("New KO layout must fit supported screen/scale combinations");
Equal(1, CardDiceLayout.Alpha(.2f, 0, -1), "KO entrance complete");
Equal(1, CardDiceLayout.Alpha(1.9f, 0, 2), "KO revealed reading hold");
Equal(1, CardDiceLayout.Alpha(2, 0, 2), "KO exit start");
Equal(0, CardDiceLayout.Alpha(2.25f, 0, 2), "KO exit complete");
if (CardDiceLayout.Alpha(2.1f, 0, 2) <= CardDiceLayout.Alpha(2.2f, 0, 2))
    throw new Exception("KO fade-out must be monotonic");
var beforeRoll = new PreRollCombat(9, 10, 3, 3, 1, 1);
var center = CardDiceLayout.Place(1920, 1080, 224, 192);
if (center is not { X: 848, Y: 444 })
    throw new Exception("Minimum-roll indicator must be screen-centered");
if (CardDiceLayout.Place(240, 200, 224, 192) != null)
    throw new Exception("Indicator must retain screen-edge padding");
for (var screen = 1280; screen <= 2560; screen += 80)
{
    var scale = screen / 1920f;
    var spot = CardDiceLayout.Place(screen, 1080 * scale, 224 * scale, 192 * scale);
    if (spot is not { } p || Math.Abs(p.X + 112 * scale - screen / 2f) > .001f
        || Math.Abs(p.Y + 96 * scale - 540 * scale) > .001f)
        throw new Exception("Scaled screen centering failed");
}
// Independent exhaustive verification: every public outcome must KO for a pair.
for (var life = 1; life <= 15; life++)
foreach (var adjustment in new[] { -3, 0, 2 })
foreach (var cap in new[] { 0, 5 })
for (var fixedDie = 0; fixedDie <= 6; fixedDie++)
{
    var input = new PreRollCombat(9, life, 2, 5, -1, 3)
        { Modifiers = new(adjustment, cap) };
    var expected = new KoPair(fixedDie, 0);
    for (var attackDie = fixedDie == 0 ? 1 : fixedDie; attackDie <= (fixedDie == 0 ? 6 : fixedDie); attackDie++)
    {
        var limit = 0;
        for (var defenseDie = 1; defenseDie <= 6; defenseDie++)
        {
            var allKo = true;
            for (var atk = 2; atk <= 5; atk++)
            for (var def = -1; def <= 3; def++)
            for (var gain = 1; gain <= 6; gain++)
            {
                var damage = Math.Max(0, Math.Max(1, atk + gain + attackDie - def - defenseDie) + adjustment);
                if (cap > 0) damage = Math.Min(cap, damage);
                allKo &= damage >= life;
            }
            if (allKo) limit = defenseDie;
        }
        if (limit > 0) { expected = new(attackDie, limit); break; }
    }
    if (CardKoMinimum.Calculate(input, new(true, 1, 6, 2), fixedDie) != expected)
        throw new Exception("KO pair exhaustive mismatch");
}
if (CardKoMinimum.Calculate(beforeRoll, null) != CardKoMinimum.Calculate(beforeRoll, new(true, 0, 0, 0))
    || CardKoMinimum.Calculate(beforeRoll with { MapType = 0 }, null) != null
    || CardKoMinimum.Calculate(beforeRoll, new(false, 1, 3, 1))
        != CardKoMinimum.Calculate(beforeRoll with { DefenseMin = beforeRoll.DefenseMin + 3,
            DefenseMax = beforeRoll.DefenseMax + 3 }, null)
    || CardKoMinimum.Calculate(beforeRoll, null, 7) != null)
    throw new Exception("KO pair validity/baseline failed");
var pairInput = new PreRollCombat(9, 9, 4, 4, 0, 0);
if (CardKoMinimum.Calculate(pairInput, null) != new KoPair(6, 1)
    || CardKoMinimum.Calculate(pairInput, null, 5) != new KoPair(5, 0)
    || CardKoMinimum.Calculate(pairInput with { Hp = 8 }, null, 6) != new KoPair(6, 2))
    throw new Exception("KO pair preview/revealed transition failed");
if (!CardKoMinimum.RevealedDie(1, true, 6) || CardKoMinimum.RevealedDie(1, false, 6)
    || CardKoMinimum.RevealedDie(2, true, 6) || CardKoMinimum.RevealedDie(1, true, 7))
    throw new Exception("Hidden roll/final total mistaken for a revealed die");
Console.WriteLine("KO pairs: exhaustive worst-case outcomes, revealed rolls, caps and visibility guards passed.");
var shadow = CardAdvisor.FromConfig(10008, 1, 1, new[] { 3, 3, 1 }, 0, 0, 2)!.Value;
if (!shadow.PreventCounter || shadow.Min != 3 || shadow.Max != 3)
    throw new Exception("Native shadow effect missing");
if (CardAdvisor.FromConfig(10008, 1, 1, new[] { 7, 9, 1 }, 0, 0, 2) is not { Min: 7, Max: 9 }
    || CardAdvisor.FromConfig(99999, 2, 2, new[] { 2, 4 }, 0, 0, 1) != new CardBonus(false, 2, 4, 1))
    throw new Exception("Live values/new additive content must not be hard-coded");
if (CardAdvisor.FromConfig(10009, 1, 1, new[] { 5, 5, 10010 }, 0, 0, 2) != new CardBonus(true, 5, 5, 2)
    || CardAdvisor.FromConfig(10009, 1, 1, new[] { 7, 9, 10010 }, 0, 0, 2) != new CardBonus(true, 7, 9, 2)
    || CardAdvisor.FromConfig(10009, 2, 2, new[] { 5, 5, 10010 }, 0, 0, 2) != null
    || CardAdvisor.FromConfig(10010, 1, 1, new[] { 6, 6, 50 }, 0, 0, 2) != null)
    throw new Exception("Known later-hand side effect must not suppress this hit or hide a final-ATK multiplier");
foreach (var id in new[] { 10009, 10010, 99999 })
    if (CardAdvisor.FromConfig(id, 1, 1, new[] { 3, 3, 1 }, 0, 0, 2) != null)
        throw new Exception("Extra parameter semantics must not be guessed");
if (CardAdvisor.FromConfig(10008, 1, 1, new[] { 3, 3, 1 }, 1, 0, 2) != null
    || CardAdvisor.FromConfig(10008, 1, 1, new[] { 3, 3, 2 }, 0, 0, 2) != null
    || CardAdvisor.Dominates(new(true, 6, 6, 1), shadow))
    throw new Exception("Unknown schema or lost counter protection");
var protectionOnly = shadow with { Min = 0, Max = 0 };
var counterInput = beforeRoll with { CounterAvailable = true, AttackerHp = 13 };
var protectedResult = CardAdvisor.Calculate(counterInput, protectionOnly)!.Value;
if (protectedResult.Title != "조건부 이득*" || protectedResult.PreventedSelfKoMax <= 0
    || protectedResult.PreventedSelfDamageMax > 13)
    throw new Exception("Counter protection bounds/classification failed");
var noCounter = CardAdvisor.Calculate(counterInput with { CounterAvailable = false }, protectionOnly)!.Value;
if (!noCounter.Title.Contains("보존") || noCounter.PreventedSelfKoMax != 0)
    throw new Exception("Unavailable counter has no prevention value");
var lowHpDefender = CardAdvisor.Calculate(new(9, 1, 20, 20, 0, 0)
    { CounterAvailable = true, AttackerHp = 13 }, shadow)!.Value;
// Pre-roll dodge remains possible even with overwhelming attack: do not call this a certain KO.
if (lowHpDefender.PreventedSelfKoMax <= 0 || lowHpDefender.PreventedSelfKoMax >= 1
    || lowHpDefender.Title != "조건부 이득*")
    throw new Exception("Surviving dodge scenarios must retain counter protection value");
var shadowAttack = CardAdvisor.Calculate(counterInput, shadow)!.Value;
var plainAttack = CardAdvisor.Calculate(counterInput, shadow with { PreventCounter = false })!.Value;
if (shadowAttack.Details != plainAttack.Details || shadowAttack.KoGainMax != plainAttack.KoGainMax)
    throw new Exception("Counter protection must not alter outgoing KO calculation");
Console.WriteLine("Native card schema, live values, compound protection, dominance and counter bounds passed.");
var effect = CardAdvisor.Calculate(beforeRoll, attackCard!.Value)!.Value;
if (effect.DamageGainMax <= 0 || effect.KoGainMax <= 0) throw new Exception("공격 카드 이득 계산 실패");
var defenseEffect = CardAdvisor.Calculate(beforeRoll, new(false, 1, 6, 2))!.Value;
if (defenseEffect.DamageGainMax <= 0) throw new Exception("방어 카드 이득 계산 실패");
if (defenseEffect.Details.Length != 0 || effect.Details.Contains('\n')
    || effect.Details.Contains("%p") || !effect.Details.EndsWith('%'))
    throw new Exception("공격 카드 확률 한 줄/증가폭·방어 카드 설명 제거 실패");
var safeTarget = CardAdvisor.Calculate(new(9, 100, 0, 0, 20, 20), new(true, 1, 1, 1))!.Value;
if (safeTarget.Details != "0%") throw new Exception("전투불능 불가 입력의 확률 비교 오류");
if (typeof(VisibleAdvice).GetProperty("Details") != null) throw new Exception("방어 추가 설명 데이터가 남아 있음");
Console.WriteLine("공격 카드 확률 한 줄·증가폭 숨김/방어 장문 데이터 제거 검증 통과");
var noEffect = CardAdvisor.Calculate(beforeRoll, new(true, 0, 0, 1))!.Value;
Equal(0, noEffect.DamageGainMax, "보정 0 피해 변화");
Equal(0, noEffect.KoGainMax, "보정 0 전투불능 변화");
var overkill = CardAdvisor.Calculate(new(9, 1, 20, 20, 0, 0), new(true, 1, 6, 2))!.Value;
if (!overkill.Title.Contains("보존")) throw new Exception("초과 피해를 유효 피해로 오판");
var excessDefense = CardAdvisor.Calculate(new(9, 10, 0, 0, 20, 20), new(false, 1, 6, 2))!.Value;
if (!excessDefense.Title.Contains("보존")) throw new Exception("최소 피해 이하 방어 과잉을 놓침");
var partial = CardAdvisor.Calculate(beforeRoll, new(true, 0, 6, 1))!.Value;
if (partial.Title != "조건부 이득*" || partial.DamageGainMin > 0.000001)
    throw new Exception("무작위 카드 범위의 무효 결과를 누락");
if (!CardAdvisor.Dominates(new(true, 6, 6, 1), new(true, 1, 6, 2))
    || CardAdvisor.Dominates(new(true, 1, 6, 1), new(true, 3, 6, 2))
    || CardAdvisor.Dominates(new(false, 6, 6, 1), new(true, 1, 6, 2)))
    throw new Exception("저비용 카드 비교 오류");
foreach (var map in new[] { 0, 1, 2, 3, 5, 7, 8, 11, 99 })
    if (CardAdvisor.Calculate(beforeRoll with { MapType = map }, attackCard.Value) != null)
        throw new Exception("PvP/미확인 모드 카드 추천 노출");
if (CardAdvisor.Calculate(beforeRoll with { Hp = 0 }, attackCard.Value) != null
    || CardAdvisor.Calculate(beforeRoll with { AttackMax = 2 }, attackCard.Value) != null
    || CardAdvisor.Calculate(beforeRoll with { AttackMax = 100, DefenseMax = 100 }, new(true, 1, 100, 1)) != null)
    throw new Exception("잘못된 입력/계산량 상한 미적용");
// A range must enclose every fixed-bonus scenario, not average unknown card odds.
for (var bonus = 1; bonus <= 6; bonus++)
{
    var fixedEffect = CardAdvisor.Calculate(beforeRoll, new(true, bonus, bonus, 2))!.Value;
    if (fixedEffect.DamageGainMin < effect.DamageGainMin - 0.000001 || fixedEffect.DamageGainMax > effect.DamageGainMax + 0.000001
        || fixedEffect.KoGainMin < effect.KoGainMin - 0.000001 || fixedEffect.KoGainMax > effect.KoGainMax + 0.000001)
        throw new Exception("무작위 보정 결과가 표시 범위를 벗어남");
}
if (CardAdvisor.Calculate(beforeRoll, attackCard.Value) != CardAdvisor.Calculate(beforeRoll, attackCard.Value))
    throw new Exception("동일 공개 입력의 결과가 비결정적");
Console.WriteLine("카드 비교·과잉 투자·비공개 확률 미가정·PvE 제한 검증 통과");

var styled = AdviceText.Format("★ 방어 추천*\n평균 피해 1~1.2\n방어 전투불능 0%", 24,
    "ui://testAtk", "ui://testDef", "ui://testHp");
if (!styled.Contains("src='ui://testDef'") || !styled.Contains("src='ui://testHp'")
    || !styled.Contains("<font color='#17663C'>방어 추천</font>")
    || !styled.Contains("<font color='#005B83'>1~1.2</font>")
    || !styled.Contains("<font color='#005B83'>0%</font>"))
    throw new Exception("추천/수치 색 강조 또는 게임 아이콘 연결 실패");
var caution = AdviceText.Format("조건부 이득*\n회피 불가\n기본 규칙 · 특수 효과 미반영", 20);
if (!caution.Contains("#795000") || !caution.Contains("#B5163B")
    || !System.Net.WebUtility.HtmlDecode(caution).EndsWith("기본 규칙 · 특수 효과 미반영") || caution.Contains("<img"))
    throw new Exception("주의 색상/기본 본문/아이콘 없는 대체 표시 실패");
var unsafeText = AdviceText.Format("<img src='https://example.com'/> & 평균 피해 -1,2~3,4", 20,
    hp: "https://example.com");
if (unsafeText.Contains("<img") || !unsafeText.Contains("&lt;img") || !unsafeText.Contains("&amp;"))
    throw new Exception("외부 이미지 또는 원문 마크업 주입 허용");
foreach (var value in new[] { styled, caution, unsafeText,
    AdviceText.Format(effect.Title + "\n" + effect.Details, 20, "ui://testAtk", "ui://testDef", "ui://testHp"),
    AdviceText.Format(uncertain.DefendQuick, 20) })
    System.Xml.Linq.XElement.Parse("<root>" + value + "</root>");
if (!AdviceText.Format("평균 피해 1", 100, hp: "ui://testHp").Contains("width='48'"))
    throw new Exception("아이콘 크기 상한 실패");
Console.WriteLine("크림색 패널용 진한 강조색·아이콘 대체·안전한 서식 검증 통과");

var quick = VisibleCombatAdvisor.Calculate(new(9, 8, 3, 3, 5, 4, 4, true))!.Value;
if (quick.Recommendation != RecommendedAction.Defend || quick.DefendQuick != "예상 피해 1.5\n전투불능 0%"
    || quick.DodgeQuick != "예상 피해 6.7\n전투불능 83.33%")
    throw new Exception("스크린샷 전투의 요약/추천 오류");
if (uncertain.Recommendation != RecommendedAction.Dodge || blockedDodge.Recommendation != RecommendedAction.Defend)
    throw new Exception("불확실/회피 불가 상태의 단일 추천 누락");
var compact = AdviceText.Quick(quick.DefendQuick);
if (compact.Contains("<b>") || styled.Contains("<b>"))
    throw new Exception("게임 POP 글꼴에 합성 굵기가 중복 적용됨");
if (compact.Contains("<img") || compact.Contains("♥") || compact.Split('\n').Length != 2)
    throw new Exception("핵심 수치가 두 줄을 넘거나 반복 아이콘이 삽입됨");
if (!AdviceText.Quick(quick.DodgeQuick).Contains("#B5163B") || compact.Contains("#B5163B") || compact.Contains("#FFFFFF"))
    throw new Exception("전투불능 위험 강조 오류");
if (AdviceText.CardTitle("효과 없음 · 보존*") != "보존" || AdviceText.CardTitle("조건부 이득*") != "조건부")
    throw new Exception("카드 요약이 불확실성을 삭제함");
Equal(0.5, AdviceText.RecommendationScale(0), "따봉 시작 배율");
Equal(1.1, AdviceText.RecommendationScale(11f / 60), "따봉 확대 정점");
Equal(0.95, AdviceText.RecommendationScale(20f / 60), "따봉 수축");
Equal(1, AdviceText.RecommendationScale(0.5f), "따봉 정착");
Equal(1, AdviceText.RecommendationScale(60), "반복 흔들림 없음");
Console.WriteLine("두 줄 요약·위험 강조·추천 제한·칩 추천 모션 키프레임 검증 통과");

// Highlight numbers in the original text, never inside encoded entities (e.g. · → &#183;).
foreach (var plain in new[] {
    "방어 추천 · 방어자 기준\n방어 예상 피해 5.5\n회피 예상 피해 7.5",
    "기본 규칙 · 특수 효과 제외\n내 전투불능 0% → 83.3%",
    "é © · ' \" & <img src='https://example.com/183'/> &#183; &#xB7; 😀",
    "피해 -1,2~3,4 · 전투불능 0~100%" })
{
    var markup = AdviceText.Quick(plain);
    var parsed = System.Xml.Linq.XElement.Parse("<root>" + markup + "</root>");
    if (parsed.Value != plain || parsed.Descendants("img").Any())
        throw new Exception("빠른 요약의 엔티티/원문 보존 또는 마크업 주입 방지 실패");
    var formatted = System.Xml.Linq.XElement.Parse("<root>" + AdviceText.Format(plain, 20) + "</root>");
    if (formatted.Descendants("img").Any())
        throw new Exception("일반 서식에서 원문 마크업 주입 허용");
}
if (!AdviceText.Quick("방어 추천 · 방어자 기준").Contains("추천 · 방어자")
    || !AdviceText.Format("기본 규칙 · 특수 효과 제외 😀", 20).Contains("· 특수 효과 제외 😀"))
    throw new Exception("유니코드 문자를 불필요한 숫자 엔티티로 변환함");
Console.WriteLine("가운데점·특수문자·숫자 엔티티 서식 회귀 검증 통과");

Equal(0, AdviceText.Reveal(-1), "등장 전 투명");
Equal(0, AdviceText.Reveal(0), "등장 시작");
Equal(0.75, AdviceText.Reveal(0.08f), "호버 감속");
Equal(1, AdviceText.Reveal(0.16f), "호버 종료");
Equal(1, AdviceText.Reveal(10, 0.22f), "메뉴 애니메이션 정착");
for (var i = 0; i < 100; i++)
    if (AdviceText.Reveal(i / 100f) > AdviceText.Reveal((i + 1) / 100f))
        throw new Exception("등장 모션이 역행함");
Console.WriteLine("메뉴·호버 등장 모션 경계 및 단조 증가 검증 통과");

var encounter = new EncounterInput(9, 10, 3, 2, 9, 0, 0, false);
var harmless = EncounterAdvisor.Calculate(encounter)!.Value;
if (harmless.Attack != true || harmless.SelfKoMin != 0 || harmless.SelfKoMax != 0)
    throw new Exception("반격 없는 상대의 공격 추천/자기 피해 오류");
var dangerous = EncounterAdvisor.Calculate(new(9, 1, 0, 0, 100, 20, 20, true))!.Value;
if (dangerous.Attack != false || dangerous.SelfKoMin <= 0 || dangerous.EnemyKoMax != 0)
    throw new Exception("치명적 반격 상대에게 놓아주기 추천 실패");
var defeated = EncounterAdvisor.Calculate(new(9, 1, 20, 0, 1, 20, 0, true))!.Value;
Equal(1, defeated.EnemyKoMax, "방어 시 상대 확정 KO");
Equal(0, defeated.SelfKoMin, "상대 KO 시 반격 없음");
// 15 strictly higher defending dice plus the game's 6/6 exception survive: 16/36.
Equal(16d / 36, defeated.SelfKoMax, "생존한 상대에게만 반격 확률 적용");
foreach (var myHp in new[] { 1, 3, 10 })
foreach (var enemyHp in new[] { 1, 4, 10 })
foreach (var myAtk in new[] { 0, 3, 10 })
foreach (var enemyAtk in new[] { 0, 3, 10 })
{
    var input = new EncounterInput(9, myHp, myAtk, 0, enemyHp, enemyAtk, 0, true);
    var result = EncounterAdvisor.Calculate(input)!.Value;
    if (result != EncounterAdvisor.Calculate(input) || result.SelfKoMin < 0 || result.SelfKoMax > 1
        || result.EnemyKoMin < 0 || result.EnemyKoMax > 1 || result.SelfKoMin > result.SelfKoMax
        || result.EnemyKoMin > result.EnemyKoMax) throw new Exception("조우 확률 범위/결정성 오류");
    if (result.Attack == null) throw new Exception("지원되는 조우에서 단일 추천 누락");
    foreach (var dodge in new[] { false, true })
    {
        var outgoing = CardAdvisor.BeforeRoll(enemyHp, myAtk, 0, dodge);
        if (outgoing.KnockoutChance < result.EnemyKoMin - 1e-6 || outgoing.KnockoutChance > result.EnemyKoMax + 1e-6)
            throw new Exception("회피 조건에 따른 조우 확률 범위 누락");
    }
}
var tiedEncounter = EncounterAdvisor.Calculate(encounter with {
    Counter = true, Modifiers = new(Immune: true), EnemyModifiers = new(Immune: true) })!.Value;
if (tiedEncounter.Attack != false) throw new Exception("조우 득실 동률에서 놓아주기 추천 누락");
foreach (var map in new[] { 0, 1, 2, 3, 5, 7, 8, 11, 99 })
    if (EncounterAdvisor.Calculate(encounter with { MapType = map }) != null)
        throw new Exception("PvP/알 수 없는 맵에서 조우 추천 허용");
foreach (var invalid in new[] { encounter with { Hp = 0 }, encounter with { EnemyHp = 0 },
    encounter with { Attack = -1 }, encounter with { EnemyAttack = 1001 },
    encounter with { Defense = -1001 }, encounter with { EnemyDefense = 1001 } })
    if (EncounterAdvisor.Calculate(invalid) != null) throw new Exception("조우 입력 범위 검증 실패");
if (harmless.Quick.Split('\n').Length != 2 || !harmless.Quick.EndsWith('%'))
    throw new Exception("조우 요약 두 줄/확률 뒤 부가 문구 제거 실패");
if (MenuLayout.Help("BattleStatus").Title != "전투 정보 표시"
    || !MenuLayout.Help("Enabled").Body.Contains("놓아주기")) throw new Exception("설정 기능 연결 안내 누락");
Console.WriteLine("조우 단일 공격·놓아주기/득실 동률·반격 생존 조건·PvE 제한 검증 통과");

// Independent 36-outcome oracle: first choose a response for each revealed attack die,
// then average its six defender dice. Never choose after seeing the defender's roll.
ChoiceStats EnumerateExchange(int hp, int attack, int defense, bool canDodge)
{
    double total = 0, ko = 0, safe = 0;
    for (var a = 1; a <= 6; a++)
    {
        var block = Enumerable.Range(1, 6).Select(d => Math.Min(hp, Math.Max(1, attack + a - defense - d))).ToArray();
        var evade = Enumerable.Range(1, 6).Select(d => d > a || d == 6 && a == 6 ? 0 : Math.Min(hp, attack + a)).ToArray();
        var blockKo = block.Count(d => d == hp);
        var evadeKo = evade.Count(d => d == hp);
        var chosen = canDodge && (evadeKo < blockKo || evadeKo == blockKo && evade.Sum() < block.Sum()) ? evade : block;
        total += chosen.Sum(); ko += chosen.Count(d => d == hp); safe += chosen.Count(d => d == 0);
    }
    return new(total / 36, ko / 36, safe / 36);
}
var oracleCases = 0;
foreach (var hp in new[] { 1, 2, 5, 10, 100 })
foreach (var atk in new[] { 0, 1, 4, 10, 20 })
foreach (var def in new[] { -4, 0, 3, 10 })
foreach (var dodge in new[] { false, true })
{
    var expected = EnumerateExchange(hp, atk, def, dodge);
    var actual = CardAdvisor.BeforeRoll(hp, atk, def, dodge);
    Equal(expected.ExpectedDamage, actual.ExpectedDamage, "36결과 체력 손실");
    Equal(expected.KnockoutChance, actual.KnockoutChance, "36결과 KO");
    Equal(expected.NoDamageChance, actual.NoDamageChance, "36결과 무피해");
    if (actual.ExpectedDamage > hp || actual.ExpectedDamage < 0) throw new Exception("초과 피해가 기대값에 포함됨");
    oracleCases++;
}
Console.WriteLine($"독립 36결과 열거 {oracleCases}개 조건: HP 제한·사전 선택·KO·무피해 일치");

// Compare delta bounds to independently paired scenarios, including unknown existing stats.
var pairedInput = new PreRollCombat(9, 5, 2, 5, 0, 3);
foreach (var attack in new[] { true, false })
{
    var paired = CardAdvisor.Calculate(pairedInput, new(attack, 1, 3, 1))!.Value;
    var changes = new List<(double Damage, double Ko)>();
    for (var atk = 2; atk <= 5; atk++)
    for (var def = 0; def <= 3; def++)
    foreach (var dodge in new[] { false, true })
    for (var bonus = 1; bonus <= 3; bonus++)
    {
        var before = EnumerateExchange(5, atk, def, dodge);
        var after = EnumerateExchange(5, atk + (attack ? bonus : 0), def + (attack ? 0 : bonus), dodge);
        var sign = attack ? 1 : -1;
        changes.Add((sign * (after.ExpectedDamage - before.ExpectedDamage), sign * (after.KnockoutChance - before.KnockoutChance)));
    }
    Equal(changes.Min(c => c.Damage), paired.DamageGainMin, "짝지은 피해 하한");
    Equal(changes.Max(c => c.Damage), paired.DamageGainMax, "짝지은 피해 상한");
    Equal(changes.Min(c => c.Ko), paired.KoGainMin, "짝지은 KO 하한");
    Equal(changes.Max(c => c.Ko), paired.KoGainMax, "짝지은 KO 상한");
}
var zeroWide = CardAdvisor.Calculate(pairedInput, new(true, 0, 0, 0))!.Value;
if (zeroWide.Details != "58.33%" || zeroWide.KoGainMin != 0 || zeroWide.KoGainMax != 0)
    throw new Exception("유리한 KO 확률 표시와 변화 없는 카드의 내부 증가폭 0을 보존해야 함");
Equal(2, CombatAdvisor.Calculate(new(2, int.MaxValue, 6, int.MinValue)).Defend.ExpectedDamage, "정수 오버플로 방지");
Equal(1, CombatAdvisor.Calculate(new(2, 0, 6, 0)).Dodge.NoDamageChance, "0공격 무피해");
foreach (var input in new[] { new CombatInput(1, 1, 1, 0, int.MinValue, int.MaxValue), new CombatInput(1, 1, 1, 0, 2, 1) })
{
    try { CombatAdvisor.Calculate(input); throw new Exception("과대/역전 보정 범위를 허용함"); }
    catch (ArgumentOutOfRangeException) { }
}
foreach (var (chance, expected) in new[] { (0d, "0"), (1d, "100"),
    (0.00001, "<0.01"), (0.99999, ">99.99"), (1d / 36, "2.78"), (5d / 6, "83.33") })
    if (CombatAdvisor.Probability(chance) != expected) throw new Exception("확률 반올림/경계 표기 오류");
Console.WriteLine("카드 짝지은 증가폭·폭넓은 기존 조건의 0효과·오버플로·확률 경계 검증 통과");

var tied = VisibleCombatAdvisor.Calculate(new(9, 100, 5, 5, 1, 4, 4, true))!.Value;
if (tied.Recommendation != RecommendedAction.Defend || !tied.Summary.Contains("방어 추천") || !tied.Defend.Contains("추천"))
    throw new Exception("완전 동률 시 방어 추천 누락");
var weaklyBetter = VisibleCombatAdvisor.Calculate(new(9, 100, 5, 6, 1, 10, 10, true))!.Value;
if (weaklyBetter.Recommendation != RecommendedAction.Defend)
    throw new Exception("동률 시나리오가 포함된 일관된 방어 우위 누락");
var dodgeBetter = VisibleCombatAdvisor.Calculate(new(9, 1, 3, 3, 2, 20, 20, true))!.Value;
if (dodgeBetter.Recommendation != RecommendedAction.Dodge || !dodgeBetter.Summary.Contains("회피 추천")
    || !quick.Summary.Contains("방어 추천") || !blockedDodge.Summary.Contains("회피 불가")
    || !uncertain.Summary.Contains("회피 추천")) throw new Exception("방어자 단일 추천/회피 불가 표제 오류");
foreach (var advice in new[] { tied, weaklyBetter, dodgeBetter, quick, blockedDodge, uncertain })
    if (!advice.Summary.StartsWith("방어자 기준") || advice.Summary.Contains("선택"))
        throw new Exception("방어자 이득 비교를 행동 예측처럼 표시함");
Console.WriteLine("방어자 단일 추천·양쪽 우위·동률 방어·회피 불가 표제 검증 통과");

// Independent integer six-roll oracle for uncertain stats: no assumed bonus distribution.
var singleChoiceCases = 0;
foreach (var hp in new[] { 1, 2, 5, 10, 100 })
foreach (var atkMin in new[] { 0, 4, 10 })
foreach (var defMin in new[] { -2, 0, 8 })
foreach (var die in Enumerable.Range(1, 6))
foreach (var canDodge in new[] { false, true })
{
    var blockAlways = true; var evadeAlways = true;
    var blockWorst = (Ko: 0, Loss: 0); var evadeWorst = (Ko: 0, Loss: 0);
    for (var atk = atkMin; atk <= atkMin + 3; atk++)
    for (var def = defMin; def <= defMin + 3; def++)
    {
        var block = Enumerable.Range(1, 6).Select(d => Math.Min(hp, Math.Max(1, atk + die - def - d))).ToArray();
        var evade = Enumerable.Range(1, 6).Select(d => d > die || d == 6 && die == 6 ? 0 : Math.Min(hp, atk + die)).ToArray();
        var b = (Ko: block.Count(d => d == hp), Loss: block.Sum());
        var e = (Ko: evade.Count(d => d == hp), Loss: evade.Sum());
        blockAlways &= b.CompareTo(e) <= 0;
        evadeAlways &= b.CompareTo(e) >= 0;
        blockWorst = (Math.Max(blockWorst.Ko, b.Ko), Math.Max(blockWorst.Loss, b.Loss));
        evadeWorst = (Math.Max(evadeWorst.Ko, e.Ko), Math.Max(evadeWorst.Loss, e.Loss));
    }
    var expected = !canDodge || blockAlways ? RecommendedAction.Defend
        : evadeAlways || blockWorst.CompareTo(evadeWorst) > 0 ? RecommendedAction.Dodge : RecommendedAction.Defend;
    var actual = VisibleCombatAdvisor.Calculate(new(9, hp, atkMin, atkMin + 3, die, defMin, defMin + 3, canDodge))!.Value;
    if (actual.Recommendation != expected || actual.Summary.Contains("보류") || actual.Summary.Contains("동률")
        || actual.Defend.Contains("추천") == actual.Dodge.Contains("추천"))
        throw new Exception($"단일 추천 독립 열거 불일치: {hp}/{atkMin}/{defMin}/{die}/{canDodge}");
    singleChoiceCases++;
}
Console.WriteLine($"방어·회피 {singleChoiceCases}개 조건: 독립 주사위 열거·최악 KO/손실 비교·항상 한쪽 추천 검증 통과");

foreach (var scale in new[] { 0.75f, 1f, 1.5f })
{
    var size = 72 * scale;
    var anchor = BattleStatusLayout.CounterAnchor(-23, -16, 500, size);
    var moved = BattleStatusLayout.CounterAnchor(77, 34, 500, size);
    if (Math.Abs(moved.X - anchor.X - 100) > 0.001f || Math.Abs(moved.Y - anchor.Y - 50) > 0.001f)
        throw new Exception("반격 표시 이동 종속 실패");
    if (!(anchor.X < 477 && anchor.X + size > 477 && anchor.Y < -16 && anchor.Y + size > -16))
        throw new Exception("반격 아이콘이 DEFENSE 우상단 모서리에 겹치지 않음");
}
Console.WriteLine("반격 아이콘 우상단 겹침·배율·헤더 이동 종속 검증 통과");

float counterSince = -1;
foreach (var (visible, time, expected) in new[] {
    (false, 0f, false), (true, 1f, false), (true, 1.74f, false),
    (true, 1.75f, true), (true, 3f, true), // One uninterrupted visible period.
    (false, 3.1f, false), (true, 4f, false), (false, 4.5f, false), // Cancel while waiting.
    (true, 8f, false), (true, 8.74f, false), (true, 8.75f, true) })
    if (BattleStatusLayout.CounterReady(visible, time, ref counterSince) != expected)
        throw new Exception($"반격 등장 0.75초 지연/취소 오류: {visible}/{time}");
counterSince = -1; // Target change/cleanup starts a new wait even if the next header is visible.
if (BattleStatusLayout.CounterReady(true, 9f, ref counterSince))
    throw new Exception("반격 새 대상 대기 초기화 실패");
Console.WriteLine("반격 0.75초 지연·유지·대기 중 숨김 취소·재등장/대상 변경 검증 통과");

foreach (var uiScale in new[] { 0.75f, 1f, 1.5f })
foreach (var iconX in new[] { 18f, 226f })
foreach (var height in new[] { 76f, 92f })
for (var frame = 0; frame <= 16; frame++)
{
    const float anchorY = 26f, hoverScale = 0.8f;
    var y = AdviceText.HoverAbovePanel(anchorY, height, hoverScale);
    var slide = (1 - AdviceText.Reveal(frame / 100f)) * AdviceText.HoverSlide;
    var bottom = (anchorY + y + slide + height * hoverScale) * uiScale;
    if (bottom > -12f * uiScale + 0.001f)
        throw new Exception($"방어/회피 호버 제목 겹침: x={iconX}, 배율={uiScale}, 프레임={frame}");
}
Console.WriteLine("방어·회피 호버: 배율/높이/등장 전체 프레임에서 패널 위 12px 여백 검증 통과");

foreach (var name in new[] { "折鶴", "漢字", "中文", "한글漢字", "Player鶴42", "〇", "\uF900", "\U00020000", "\U00030000" })
    if (!FieldNameText.HasHan(name)) throw new Exception("한자 닉네임 감지 실패");
foreach (var name in new[] { "", "Tse06", "DDockson", "한글닉", "ひらがな", "カタカナ", "123", "😀", "\U00017000", "\ud800" })
    if (FieldNameText.HasHan(name)) throw new Exception("한자 없는 닉네임의 기본 폰트 변경");
Console.WriteLine("필드 닉네임: 한자·호환/확장 한자·혼합 감지, 한글/영문/가나/이모지 제외 검사 통과");
