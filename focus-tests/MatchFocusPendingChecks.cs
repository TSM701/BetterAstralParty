using BetterAstralParty;

internal static class MatchFocusPendingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        MatchFocusResult Poll(MatchFocusState state, WindowFake window, long now, bool enabled = true,
            long room = 10, bool match = true, bool choosing = true, bool pve = true, bool reconnect = false) =>
            state.Poll(enabled, room, match, choosing, pve, 0, reconnect, window, now);

        // A successful Z-order/request result is not proof that keyboard focus has arrived.
        var state = new MatchFocusState(); var window = new WindowFake { DelayActivation = true };
        check(state.Success(true, 10, true, true, false, window, 100) == MatchFocusResult.Pending, "accepted asynchronous activation stays pending");
        check(window.Activations == 1 && window.Raises == 0 && window.Flashes == 0, "pending activation never raises or flashes immediately");
        for (var now = 101; now < 350; now++)
            check(Poll(state, window, now) == MatchFocusResult.Pending && window.Activations == 1 && window.Flashes == 0, "no early retry or blocking wait");
        window.Foreground = true;
        check(Poll(state, window, 349) == MatchFocusResult.Activated && window.Activations == 1 && window.Raises == 1 && window.Flashes == 0, "later actual foreground raises once without activation retry");
        var calls = window.Calls;
        for (var repeat = 0; repeat < 100; repeat++) check(Poll(state, window, 2000 + repeat) == MatchFocusResult.Ignored, "completed episode does not reactivate");
        check(window.Calls == calls, "completed episode does not keep polling Windows");

        state = new(); window = new() { AcceptActivation = false };
        check(state.Success(true, 10, true, true, false, window, 100) == MatchFocusResult.Pending, "refused request enters bounded confirmation");
        check(Poll(state, window, 350) == MatchFocusResult.Pending && window.Activations == 2, "single spaced retry");
        for (var now = 351; now < 1100; now++)
            check(Poll(state, window, now) == MatchFocusResult.Pending && window.Activations == 2 && window.Flashes == 0, "retry count capped while pending");
        check(Poll(state, window, 1100) == MatchFocusResult.TaskbarNotified && window.Raises == 0 && window.Flashes == 1, "blocked foreground deadline flashes only once without raising");
        calls = window.Calls;
        check(Poll(state, window, 1200) == MatchFocusResult.Ignored && window.Calls == calls, "timed-out episode has no later Windows calls");

        state = new(); window = new() { Minimized = true, DelayRestore = true };
        check(state.Success(true, 10, true, true, false, window, 100) == MatchFocusResult.Pending && window.Restores == 1 && window.Activations == 0, "asynchronous restore precedes activation");
        check(Poll(state, window, 300) == MatchFocusResult.Pending && window.Activations == 0, "never activates still-minimized window");
        window.Minimized = false;
        check(Poll(state, window, 400) == MatchFocusResult.Activated && window.Activations == 1 && window.Flashes == 0, "restored window activates once ready");
        state = new(); window = new() { Minimized = true, DelayRestore = true };
        _ = state.Success(true, 10, true, true, false, window, 100);
        check(Poll(state, window, 1100) == MatchFocusResult.RestoreFailed && window.Activations == 0 && window.Restores == 1 && window.Flashes == 1, "stalled restore has bounded fallback");

        foreach (var change in new[] { "off", "room", "match", "choice", "pvp", "reconnect", "missing" })
        {
            state = new(); window = new() { DelayActivation = true };
            _ = state.Success(true, 10, true, true, false, window, 100);
            calls = window.Calls;
            if (change == "missing") state.Unavailable();
            _ = Poll(state, window, 350, enabled: change != "off", room: change == "room" ? 11 : 10,
                match: change != "match", choosing: change != "choice", pve: change != "pvp", reconnect: change == "reconnect");
            check(window.Calls == calls && window.Activations == 1 && window.Raises == 0 && window.Flashes == 0, "pending cancelled without OS calls: " + change);
            _ = Poll(state, window, 2000);
            check(window.Calls == calls, "cancelled room stays consumed: " + change);
        }
        foreach (var changedHandle in new[] { false, true })
        {
            state = new(); window = new() { DelayActivation = true };
            _ = state.Success(true, 10, true, true, false, window, 100);
            if (changedHandle) window.Handle = (IntPtr)43; else window.Valid = false;
            check(Poll(state, window, 350) == MatchFocusResult.WindowUnavailable && window.Activations == 1 && window.Flashes == 0, "destroyed/reused HWND cancels pending safely");
        }
        state = new(); window = new() { DelayActivation = true };
        _ = state.Success(true, 10, true, true, false, window, 100);
        check(Poll(state, window, 99) == MatchFocusResult.TaskbarNotified && window.Activations == 1 && window.Flashes == 1, "invalid monotonic interval cannot extend pending forever");

        state = new(); window = new() { Foreground = true, AcceptRaise = false };
        check(state.Success(true, 10, true, true, false, window, 100) == MatchFocusResult.Pending
            && window.Activations == 0 && window.Raises == 1, "already foreground request refusal stays pending without activation");
        for (var now = 101; now < 350; now++)
            check(Poll(state, window, now) == MatchFocusResult.Pending && window.Raises == 1 && window.Activations == 0, "no early Z-order retry");
        check(Poll(state, window, 350) == MatchFocusResult.Pending && window.Raises == 2, "single spaced raise retry");
        for (var now = 351; now < 1100; now++)
            check(Poll(state, window, now) == MatchFocusResult.Pending && window.Raises == 2 && window.Activations == 0 && window.Flashes == 0, "raise refusal bounded independently of activation");
        check(Poll(state, window, 1100) == MatchFocusResult.RaiseFailed && window.Flashes == 0, "raise deadline is explicit without flashing foreground Unity");
        calls = window.Calls;
        check(Poll(state, window, 1200) == MatchFocusResult.Ignored && window.Calls == calls, "expired raise never continues in background");

        state = new(); window = new() { DelayActivation = true, AcceptRaise = false };
        _ = state.Success(true, 10, true, true, false, window, 100);
        _ = Poll(state, window, 350);
        window.Foreground = true;
        check(Poll(state, window, 400) == MatchFocusResult.Pending && window.Activations == 2 && window.Raises == 1,
            "raise retry budget is separate after capped activation attempts");
        _ = Poll(state, window, 650);
        check(Poll(state, window, 1100) == MatchFocusResult.RaiseFailed && window.Activations == 2 && window.Raises == 2 && window.Flashes == 0,
            "combined request counts remain at most two activation and two raise attempts");

        state = new(); window = new() { Foreground = true, Minimized = true, DelayRestore = true };
        check(state.Success(true, 10, true, true, false, window, 100) == MatchFocusResult.Pending
            && window.Restores == 1 && window.Activations == 0 && window.Raises == 0, "foreground HWND still minimized waits for asynchronous restore");
        check(Poll(state, window, 349) == MatchFocusResult.Pending && window.Raises == 0, "foreground alone does not prove restored fullscreen presentation");
        window.Minimized = false;
        check(Poll(state, window, 350) == MatchFocusResult.Activated && window.Raises == 1 && window.Activations == 0, "restored already-foreground Unity is raised without reactivation");

        state = new(); window = new() { ProcessForeground = true };
        check(state.Success(true, 10, true, true, false, window, 100) == MatchFocusResult.AlreadyForeground
            && window.Raises == 0 && window.Activations == 0 && window.Restores == 0, "same-process modal is preserved instead of raising Unity behind it");
        state = new(); window = new() { DelayActivation = true };
        _ = state.Success(true, 10, true, true, false, window, 100);
        window.ProcessForeground = true;
        check(Poll(state, window, 350) == MatchFocusResult.AlreadyForeground && window.Raises == 0 && window.Activations == 1, "same-process modal appearing during pending stops activation and raise");

        foreach (var change in new[] { "off", "room", "match", "choice", "pvp", "reconnect", "missing" })
        {
            state = new(); window = new() { Foreground = true, AcceptRaise = false };
            _ = state.Success(true, 10, true, true, false, window, 100);
            calls = window.Calls;
            if (change == "missing") state.Unavailable();
            _ = Poll(state, window, 350, enabled: change != "off", room: change == "room" ? 11 : 10,
                match: change != "match", choosing: change != "choice", pve: change != "pvp", reconnect: change == "reconnect");
            check(window.Calls == calls && window.Raises == 1 && window.Activations == 0 && window.Flashes == 0, "raise cancelled without OS calls: " + change);
            _ = Poll(state, window, 2000);
            check(window.Calls == calls, "cancelled raised room stays consumed: " + change);
        }
        foreach (var race in new[] { "foreground", "modal", "destroyed", "reused", "minimized" })
        {
            state = new(); window = new() { Foreground = true };
            window.AfterRaise = () =>
            {
                if (race == "foreground" || race == "modal") window.Foreground = false;
                if (race == "modal") window.ProcessForeground = true;
                if (race == "destroyed") window.Valid = false;
                if (race == "reused") window.Handle = (IntPtr)43;
                if (race == "minimized") window.Minimized = true;
            };
            var result = state.Success(true, 10, true, true, false, window, 100);
            check(result == (race is "destroyed" or "reused" ? MatchFocusResult.WindowUnavailable : MatchFocusResult.Pending), "accepted request cannot hide post-raise race: " + race);
            check(window.Raises == 1 && window.Activations == 0 && window.Flashes == 0, "post-raise race has no immediate retry or unrelated flash: " + race);
            if (race == "modal") check(Poll(state, window, 350) == MatchFocusResult.AlreadyForeground && window.Raises == 1, "post-raise modal preserves native focus");
            if (race == "foreground")
            {
                window.AfterRaise = null;
                check(Poll(state, window, 350) == MatchFocusResult.Activated && window.Activations == 1 && window.Raises == 2, "foreground loss reacquires within same bounded episode");
            }
        }
        state = new(); window = new() { Foreground = true, AcceptRaise = false };
        _ = state.Success(true, 10, true, true, false, window, 100);
        check(Poll(state, window, 99) == MatchFocusResult.RaiseFailed && window.Raises == 1 && window.Flashes == 0, "invalid monotonic raise interval terminates without stealing focus");
        PrivateLobby(check);
    }

    private static void PrivateLobby(Action<bool, string> check)
    {
        MatchFocusResult Poll(MatchFocusState state, WindowFake window, long now, bool enabled = true,
            long room = 10, bool match = false, bool choosing = true, bool pve = true, bool reconnect = false, bool waiting = false, int queue = 0) =>
            state.Poll(enabled, room, match, choosing, pve, queue, reconnect, window, now, waiting);
        void Wait(MatchFocusState state, WindowFake window, long now = 0) => _ = Poll(state, window, now, choosing: false, waiting: true);

        var state = new MatchFocusState(); var window = new WindowFake();
        Wait(state, window);
        check(window.Calls == 0, "joining manual WAIT lobby never focuses");
        check(Poll(state, window, 100) == MatchFocusResult.Activated && window.Activations == 1 && window.Raises == 1,
            "manual PvE WAIT to CHOICE starts one shared activation episode");
        var calls = window.Calls;
        for (var repeat = 0; repeat < 100; repeat++)
            check(Poll(state, window, 101 + repeat) == MatchFocusResult.Ignored && window.Calls == calls, "manual CHOICE cannot replay host start");
        window.Foreground = false;
        Wait(state, window, 300);
        check(Poll(state, window, 400) == MatchFocusResult.Activated && window.Activations == 2,
            "return to WAIT and new host start focuses again in the same room");
        state = new(); window = new();
        check(Poll(state, window, 0) == MatchFocusResult.Ignored && window.Calls == 0, "startup/join directly in CHOICE stays silent");
        Wait(state, window, 100);
        check(Poll(state, window, 200) == MatchFocusResult.Activated, "observed later WAIT proves genuine new episode even after silent CHOICE join");

        foreach (var boundary in new[] { "off", "reconnect", "missing", "pvp", "identity", "none", "ready", "match", "contradictory" })
        {
            state = new(); window = new(); Wait(state, window);
            if (boundary == "missing") state.Unavailable();
            else _ = Poll(state, window, 50, enabled: boundary != "off", room: boundary == "identity" ? 11 : boundary == "none" ? 0 : 10,
                match: boundary == "match", choosing: boundary == "contradictory", pve: boundary != "pvp", reconnect: boundary == "reconnect",
                waiting: boundary is "off" or "reconnect" or "pvp" or "identity" or "match" or "contradictory");
            check(Poll(state, window, 100) == MatchFocusResult.Ignored && window.Calls == 0,
                "manual lobby latch cannot cross disabled/reconnect/missing/foreign state boundary: " + boundary);
            Wait(state, window, 200);
            check(Poll(state, window, 300) == MatchFocusResult.Activated && window.Activations == 1,
                "new observed manual WAIT recovers after boundary: " + boundary);
        }
        foreach (var disabledAtWait in new[] { false, true })
        {
            state = new(); window = new();
            _ = Poll(state, window, 0, enabled: !disabledAtWait, choosing: false, waiting: true);
            _ = Poll(state, window, 100, enabled: disabledAtWait);
            check(Poll(state, window, 200) == MatchFocusResult.Ignored && window.Calls == 0,
                "manual OFF/ON cannot replay disabled WAIT/start: " + disabledAtWait);
        }
        state = new(); window = new();
        _ = Poll(state, window, 0, room: 0, choosing: false, queue: 2);
        check(Poll(state, window, 100) == MatchFocusResult.Ignored && window.Calls == 0,
            "matching queue cannot turn an unobserved manual lobby into success");
        state = new(); window = new(); Wait(state, window);
        check(Poll(state, window, 100, match: true) == MatchFocusResult.Ignored && window.Calls == 0,
            "manual lobby proof cannot turn a classified matchmaking room into success");
        check(state.Success(true, 10, false, true, false, window) == MatchFocusResult.Ignored && window.Calls == 0,
            "public Success still cannot bypass manual WAIT proof");

        state = new(); window = new() { DelayActivation = true }; Wait(state, window);
        check(Poll(state, window, 100) == MatchFocusResult.Pending && window.Activations == 1, "manual activation awaits actual OS foreground");
        check(Poll(state, window, 349) == MatchFocusResult.Pending && window.Activations == 1, "manual pending respects activation retry interval");
        check(Poll(state, window, 350) == MatchFocusResult.Pending && window.Activations == 2, "manual pending retains its non-match classification for bounded retry");
        window.Foreground = true;
        check(Poll(state, window, 400) == MatchFocusResult.Activated && window.Raises == 1 && window.Flashes == 0,
            "manual pending completes with shared foreground confirmation and raise");
        state = new(); window = new() { AcceptActivation = false }; Wait(state, window);
        _ = Poll(state, window, 100); _ = Poll(state, window, 350);
        check(Poll(state, window, 1100) == MatchFocusResult.TaskbarNotified && window.Activations == 2 && window.Flashes == 1,
            "manual OS refusal has bounded attempts and one taskbar fallback");
        calls = window.Calls;
        check(Poll(state, window, 2000) == MatchFocusResult.Ignored && window.Calls == calls, "manual fallback consumes the start without later retries");
        foreach (var phase in new[] { "activation", "raise" })
        foreach (var boundary in new[] { "off", "identity", "classification", "ready", "pvp", "reconnect", "missing" })
        {
            state = new(); window = new() { DelayActivation = phase == "activation", Foreground = phase == "raise", AcceptRaise = false };
            Wait(state, window); _ = Poll(state, window, 100); calls = window.Calls;
            if (boundary == "missing") state.Unavailable();
            _ = Poll(state, window, 350, enabled: boundary != "off", room: boundary == "identity" ? 11 : 10,
                match: boundary == "classification", choosing: boundary != "ready", pve: boundary != "pvp", reconnect: boundary == "reconnect");
            check(window.Calls == calls && window.Flashes == 0, "manual pending aborts without OS work: " + phase + "/" + boundary);
            _ = Poll(state, window, 2000);
            check(window.Calls == calls, "aborted manual episode remains consumed: " + phase + "/" + boundary);
        }
        state = new(); window = new() { Minimized = true, DelayRestore = true }; Wait(state, window);
        check(Poll(state, window, 100) == MatchFocusResult.Pending && window.Restores == 1 && window.Activations == 0,
            "manual minimized window waits for normal restore");
        window.Minimized = false;
        check(Poll(state, window, 350) == MatchFocusResult.Activated && window.Restores == 1 && window.Activations == 1,
            "manual restored window resumes the same bounded episode");
        state = new(); window = new() { ProcessForeground = true }; Wait(state, window);
        check(Poll(state, window, 100) == MatchFocusResult.AlreadyForeground && window.Activations == 0 && window.Raises == 0,
            "manual host start preserves same-process modal focus");
    }
}
