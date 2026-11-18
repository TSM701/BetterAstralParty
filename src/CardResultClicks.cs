namespace BetterAstralParty;

// Another player's card result left the rest of the screen dead (user-reported 2026-11-17: a player's info could not be
// opened while it showed). CardWindow (sortingOrder 11, over the field and the battle HUD panels) shows it in
// com_CardResult, a 1920 x 1080 component that is opaque in the Card package, so it took every click under it until the
// window closed itself on its timer (DelaySignalManager). While it shows on another player's turn its empty area lets
// clicks through; its own parts (the card, the target list, the quick-card buttons) keep theirs. On the local player's own
// turn, or with the turn unknown, it stays as the game made it, so nothing of the turn can be pressed while a card plays.
// The window's other steps (com_UseCard, com_ChooseCard) are left alone. Always on in PvE, with its own breaker.
internal static class CardResultClicks
{
    private const string Feature = "CardResultClicks";
    // The result this made click-through, to give its opacity back.
    private static RuntimeObject? _opened;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { Update(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update()
    {
        var root = GameUi.Root;
        var window = root == null || PvpSafety.Suspended ? null : GameUi.Find(root, "CardWindow", maxDepth: 1);
        var result = window?.Get("contentPane")?.Field("com_CardResult");
        if (result == null || !GameUi.Visible(result) || !OthersTurn()) { Clear(); return; }
        if (_opened != null && _opened.Pointer != result.Pointer) Clear();
        if (!result.Get<bool>("opaque")) return;
        result.Set("opaque", false);
        _opened = result;
    }

    // BattleLogic.curPlayerId against the local account, as MonsterMoveProbe reads the turn.
    private static bool OthersTurn()
    {
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var local = logic?.Get("account")?.Call("GetPlayerID")?.Value<long>() ?? 0;
        var turn = logic?.Get("battle")?.Get<long>("curPlayerId") ?? 0;
        return local != 0 && turn != 0 && turn != local;
    }

    internal static void Clear()
    {
        var opened = _opened;
        _opened = null;
        if (opened != null && !opened.Get<bool>("isDisposed") && !opened.Get<bool>("opaque")) opened.Set("opaque", true);
    }
}
