using BetterAstralParty;
using System.Runtime.InteropServices;

var assertions = 0;
void Check(bool value, string scenario) { assertions++; if (!value) throw new Exception(scenario); }
MatchFocusResult Found(MatchFocusState state, WindowFake window, long room = 10, bool enabled = true, bool match = true, bool choosing = true, bool reconnect = false) =>
    state.Success(enabled, room, match, choosing, reconnect, window);

foreach (var enabled in new[] { false, true })
foreach (var room in new long[] { -1, 0, 10 })
foreach (var match in new[] { false, true })
foreach (var choosing in new[] { false, true })
foreach (var reconnect in new[] { false, true })
{
    var fake = new WindowFake(); var state = new MatchFocusState();
    var accepted = enabled && room > 0 && match && choosing && !reconnect;
    Check(Found(state, fake, room, enabled, match, choosing, reconnect) == (accepted ? MatchFocusResult.Activated : MatchFocusResult.Ignored), "event gates");
    Check(fake.Activations == (accepted ? 1 : 0) && fake.Raises == (accepted ? 1 : 0) && fake.Flashes == 0 && fake.Restores == 0, "gate side effects");
}
{
    var state = new MatchFocusState(); var fake = new WindowFake { Foreground = true };
    Check(Found(state, fake) == MatchFocusResult.Activated && fake.Activations == 0 && fake.Raises == 1, "already exact Unity foreground still raises without activation");
    fake.Foreground = false;
    Check(Found(state, fake) == MatchFocusResult.Duplicate && fake.Activations == 0, "one event already consumed");
    Check(Found(state, fake, 11) == MatchFocusResult.Activated, "next actual match");
}
{
    var state = new MatchFocusState(); var fake = new WindowFake();
    state.ObserveExisting(10);
    Check(Found(state, fake) == MatchFocusResult.Duplicate && fake.Calls == 0, "enable/rebind does not refocus existing room");
    Check(Found(state, fake, 11, enabled: false) == MatchFocusResult.Ignored, "OFF does nothing");
    Check(Found(state, fake, 11) == MatchFocusResult.Duplicate && fake.Calls == 0, "OFF to ON does not replay event");
    Check(Found(state, fake, 12, reconnect: true) == MatchFocusResult.Ignored, "reconnect does nothing");
    Check(Found(state, fake, 12) == MatchFocusResult.Duplicate && fake.Calls == 0, "reconnect cannot be replayed after flag clears");
}
foreach (var minimize in new[] { false, true })
foreach (var activate in new[] { false, true })
{
    var state = new MatchFocusState(); var fake = new WindowFake { Minimized = minimize, AcceptActivation = activate };
    Check(Found(state, fake) == (activate ? MatchFocusResult.Activated : MatchFocusResult.Pending), "OS allows/refuses focus");
    if (!activate) Check(state.Poll(true, 10, true, true, true, 0, false, fake, MatchFocusState.ActivationTimeout) == MatchFocusResult.TaskbarNotified, "OS refusal finishes after nonblocking deadline");
    Check(fake.Restores == (minimize ? 1 : 0) && fake.Activations == 1 && fake.Raises == (activate ? 1 : 0) && fake.Flashes == (activate ? 0 : 1), "restore once, activation/raise bounded, fallback once");
    for (var repeat = 0; repeat < 100; repeat++) Check(Found(state, fake) == MatchFocusResult.Duplicate, "duplicate native events");
    Check(fake.Activations == 1 && fake.Flashes <= 1 && fake.Restores <= 1, "no retries");
}
{
    var fake = new WindowFake { Handle = IntPtr.Zero };
    Check(Found(new(), fake) == MatchFocusResult.WindowUnavailable && fake.Activations == 0 && fake.Flashes == 0, "missing/ambiguous window");
    fake = new() { Valid = false };
    Check(Found(new(), fake) == MatchFocusResult.WindowUnavailable && fake.Activations == 0, "foreign process/class/handle rejected");
}
foreach (var invalidateAt in new[] { 2, 3 })
{
    var fake = new WindowFake { Minimized = true, InvalidateAtValidation = invalidateAt };
    Check(Found(new(), fake) == MatchFocusResult.WindowUnavailable, "destroy/reuse during window checks");
    Check(fake.Activations == 0 && fake.Flashes == 0 && fake.Restores <= 1, "reused handle is not activated/flashed");
}
{
    var fake = new WindowFake { Minimized = true, AcceptRestore = false };
    Check(Found(new(), fake) == MatchFocusResult.RestoreFailed && fake.Activations == 0 && fake.Restores == 1 && fake.Flashes == 1, "restore failure fallback");
    fake = new() { AcceptActivation = false, InvalidateAtValidation = 3 };
    Check(Found(new(), fake) == MatchFocusResult.WindowUnavailable && fake.Flashes == 0, "OS refusal then destroyed/reused window");
    fake = new() { ForegroundOnRead = 2 };
    Check(Found(new(), fake) == MatchFocusResult.AlreadyForeground && fake.Activations == 0, "user returned before activation");
    fake = new() { AcceptActivation = false, ForegroundOnRead = 3 };
    Check(Found(new(), fake) == MatchFocusResult.AlreadyForeground && fake.Flashes == 0, "user returned before flash");
    fake = new() { Minimized = true, AcceptRestore = false, ForegroundOnRead = 2 };
    Check(Found(new(), fake) == MatchFocusResult.AlreadyForeground && fake.Flashes == 0, "user returned during restore refusal");
}
{
    var state = new MatchFocusState(); var fake = new WindowFake { Foreground = true };
    for (var id = 1; id <= MatchFocusState.MaximumMatches; id++) Check(Found(state, fake, id) == MatchFocusResult.Activated, "bounded session identities");
    Check(Found(state, fake, MatchFocusState.MaximumMatches + 1) == MatchFocusResult.CapacityReached, "safe capacity cap");
    fake.Foreground = false;
    Check(Found(state, fake, 1) == MatchFocusResult.Duplicate && fake.Activations == 0, "old reconnect identity retained");
}
// Metadata only. Never invoke user32 functions or affect a real window in this fixture.
var native = typeof(WindowsMatchFocus).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
    .Select(method => (Method: method, Import: method.GetCustomAttributes(typeof(DllImportAttribute), false).Cast<DllImportAttribute>().SingleOrDefault()))
    .Where(item => item.Import != null).ToArray();
Check(native.Length == 13 && native.All(item => item.Import!.Value == "user32.dll"), "bounded supported Windows API surface");
Check(native.All(item => !new[] { "SendInput", "keybd_event", "AttachThreadInput", "AllowSetForegroundWindow", "SystemParametersInfo", "SwitchToThisWindow" }.Contains(item.Method.Name)), "no input/privilege/input-queue workaround");
var position = native.Single(item => item.Method.Name == "SetWindowPos");
Check(position.Method.ReturnType == typeof(bool) && position.Import!.SetLastError
    && position.Method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(IntPtr), typeof(IntPtr), typeof(int), typeof(int), typeof(int), typeof(int), typeof(uint) }), "native asynchronous SetWindowPos ABI");
var style = native.Single(item => item.Method.Name == "GetWindowLongW");
Check(style.Method.ReturnType == typeof(int) && style.Import!.SetLastError
    && style.Method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(IntPtr), typeof(int) }), "native extended-style read ABI");
MatchFocusPendingChecks.Run(Check);
MatchFocusPollingChecks.Run(Check);
MatchFocusNativeChecks.Run(args.Single(), Check);
Console.WriteLine($"PASS: {assertions} matching-focus production polling/mock/native-metadata assertions; no native/game/event-binding execution.");

sealed class WindowFake : IMatchFocusWindow
{
    internal IntPtr Handle = (IntPtr)42;
    internal bool Foreground, ProcessForeground, Valid = true, Minimized, AcceptRestore = true, AcceptActivation = true, AcceptRaise = true, DelayRestore, DelayActivation;
    internal int Activations, Raises, Restores, Flashes, Calls, InvalidateAtValidation, ForegroundOnRead;
    internal int TopmostRestores;
    internal bool AcceptTopmostRestore = true, ThrowTopmostRestore;
    internal Action? AfterRaise;
    private int _validation, _foregroundReads;
    public bool GameProcessIsForeground { get { Calls++; return Foreground || ProcessForeground || (++_foregroundReads == ForegroundOnRead); } }
    public IntPtr FindGameWindow() { Calls++; return Handle; }
    public bool IsGameWindow(IntPtr window) { Calls++; _validation++; return window != IntPtr.Zero && window == Handle && Valid && (InvalidateAtValidation == 0 || _validation < InvalidateAtValidation); }
    public bool IsMinimized(IntPtr window) { Calls++; return Minimized; }
    public bool Restore(IntPtr window) { Calls++; Restores++; if (AcceptRestore && !DelayRestore) Minimized = false; return AcceptRestore; }
    public bool IsForeground(IntPtr window) { Calls++; return window != IntPtr.Zero && window == Handle && Valid && Foreground; }
    public bool Activate(IntPtr window) { Calls++; Activations++; if (AcceptActivation && !DelayActivation) Foreground = true; return Foreground; }
    public bool RestoreTopmost() { TopmostRestores++; if (ThrowTopmostRestore) throw new Exception("fake topmost restore"); return AcceptTopmostRestore; }
    public bool Raise(IntPtr window) { Calls++; Raises++; if (window != Handle || !Foreground || Minimized) throw new Exception("Raise requires exact restored foreground Unity window"); AfterRaise?.Invoke(); return AcceptRaise; }
    public void Flash(IntPtr window) { Calls++; Flashes++; }
}
