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
        or "LandPursuitWindow" or "LandRelicWindow" or "LandVendorWindow" or "LandShopWindow";
    internal static bool ShouldFlash(bool enabled, bool focused, bool running, bool participant,
        bool replay, bool pve, int pending) => enabled && !focused && running && participant
        && !replay && pve && pending > 0;
}
