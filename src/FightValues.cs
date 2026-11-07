using System.Globalization;

namespace BetterAstralParty;

// The native arithmetic panel (FightWindow.ShowFightValue, UIFight_Com_Value) shows each side's value before its die and
// every played card's rolled bonus before the totals replace the raw dice. BattleShowDirector.GetPointInfos splits the
// server total into exactly these parts, so their sum plus the side's die is the total about to be shown.
internal static class FightValues
{
    // Session breaker: a failed read only drops the early figures; the dice and totals keep working.
    private static bool _failed;

    // Null until the side's value and every one of its cards are on screen.
    internal static int? Revealed(RuntimeObject? values, bool attacker)
    {
        if (_failed) return null;
        try { return Read(values, attacker); }
        catch (Exception ex)
        {
            _failed = true;
            Plugin.Diagnostics.Error("FightValues", ex);
            return null;
        }
    }

    private static int? Read(RuntimeObject? values, bool attacker)
    {
        if (!GameUi.Visible(values)
            || !Shown(values!.Field(attacker ? "player_atk" : "player_def")?.Field("title"), out var text)
            || !int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var total)) return null;
        var list = values.Field(attacker ? "list_atk" : "list_def")!;
        var count = list.Get<int>("numItems");
        // RefreshCardList shows at most seven cards; with more, the seventh sums the rest.
        if (count is < 0 or > 7 || list.Get<int>("numChildren") != count) return null;
        for (var i = 0; i < count; i++)
        {
            var item = list.Call("GetChildAt", i)!;
            // State 0/1: one attack/defense card ("+N"); 2/3: the rest summed ("...+N").
            var state = item.Field("state")!.Get<int>("selectedIndex");
            if (state % 2 != (attacker ? 0 : 1)
                || !Shown(item.Field(state >= 2 ? "title_total" : attacker ? "title_atk" : "title_def"), out text)
                || CardKoMinimum.CardValue(text) is not { } bonus) return null;
            total += bonus;
        }
        return total;
    }

    // Each card fades in on its turn (item alpha 0 until then) and the side's value half a second after the panel.
    private static bool Shown(RuntimeObject? label, out string text)
    {
        text = "";
        if (!GameUi.Visible(label)) return false;
        for (var current = label; current != null; current = current.Get("parent"))
            if (current.Get<float>("alpha") < 0.9f) return false;
        text = label!.Get("text")?.String() ?? "";
        return true;
    }
}
