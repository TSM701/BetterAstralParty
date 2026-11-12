namespace BetterAstralParty;

internal static class InputAttentionState
{
    internal static string Normalize(string? mode) => mode switch
    {
        "Windows" => "Windows",
        "Off" or "false" or "False" => "Off",
        _ => "Taskbar"
    };
    internal static string Next(string mode) => Normalize(mode) switch
    { "Taskbar" => "Windows", "Windows" => "Off", _ => "Taskbar" };
    internal static string Label(string mode) => Normalize(mode) switch
    { "Taskbar" => "작업표시줄", "Windows" => "Windows 알림", _ => "OFF" };
    // Native action-entry dialogs; deliberately exclude settings, chat and information windows.
    internal static bool InputWindow(string type) => type is "RelicWindow" or "LoseCardWindow"
        or "ChooseRoundCardWindow" or "AssistVoteWindow" or "AssistVoteS7Window"
        or "BattleSelectMonsterWindow" or "CardWindow" or "LandBatteryWindow"
        or "LandDivinationWindow" or "LandEventWindow" or "LandFillingStationWindow"
        or "LandGambleWindow" or "LandHospitalWindow" or "LandLotteryWindow"
        or "LandPursuitWindow" or "LandRelicWindow" or "LandVendorWindow" or "LandShopWindow"
        // Plant Building Shop (2026-11-12): a buy/cancel choice. LandBuildingMissionWindow only shows progress.
        or "LandBuildShopWindow";
    // CardChoiceTimer: a watched card choice ends when its window closes (Sn 0) or shows another action, and its input
    // timer is cancelled only while the timer registered under its Sn is still the object seen with the window open.
    internal static long StaleChoiceTimer(long watched, IntPtr seen, long open, IntPtr registered) =>
        watched != 0 && watched != open && seen != IntPtr.Zero && registered == seen ? watched : 0;
    internal static bool ShouldFlash(bool enabled, bool focused, bool running, bool participant,
        bool replay, bool pve, int pending) => enabled && !focused && running && participant
        && !replay && pve && pending > 0;
}

// HomeEscQuit: an Esc asks only on a home that was idle the frame before it and still is the frame after it, so an Esc that
// closes a window never asks and a window opened for that Esc wins. A skipped frame forgets both.
internal sealed class HomeEscape
{
    private int _frame = -2;
    private bool _idle, _pending;

    internal bool Step(int frame, bool idle, bool escape)
    {
        if (frame != _frame + 1) _idle = _pending = false;
        _frame = frame;
        var ask = _pending && idle;
        _pending = !_pending && _idle && idle && escape;
        _idle = idle;
        return ask;
    }
}
