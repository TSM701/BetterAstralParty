using UnityEngine;

namespace BetterAstralParty;

internal static class CardUi
{
    private static readonly Dictionary<IntPtr, AdviceBadge> Badges = new();
    private static readonly Dictionary<CardBonus, CardImpact?> Results = new();
    private static readonly List<(RuntimeObject Card, RuntimeObject Face, CardBonus? Bonus, bool Usable)> Candidates = new(11);
    private static readonly HashSet<IntPtr> Live = new();
    private static readonly List<IntPtr> Stale = new();
    private static PreRollCombat? _input;

    internal static void Update(RuntimeObject fight, RuntimeObject ui, PreRollCombat input, RuntimeObject attackPoint)
    {
        if (!CardAdvisor.Valid(input)) { Clear(); return; }
        var role = ui.Field("playerState")!.Get<int>("selectedIndex");
        // 0 = local attacker, 1 = local defender, 2 = audience. Never read audience hands.
        if (role == 2)
        {
            // Spectators have no local ready button. Use public actor stats only,
            // before accessing cardItemList or the local hand/hover path.
            Clear(keepDice: true);
            if (Plugin.KoMinimum.Value) CardDiceUi.Show(ui, attackPoint, input, null, false, false);
            else CardDiceUi.Clear();
            return;
        }
        if (role is not (0 or 1)) { Clear(); return; }
        var ready = ui.Field("com_Card")!.Field("btn_FinishPkCard")!;
        if (!GameUi.Visible(ready))
        {
            Clear(keepDice: true);
            if (Plugin.KoMinimum.Value) CardDiceUi.Show(ui, attackPoint, input, null, false, false);
            else CardDiceUi.Clear();
            return;
        }
        if (!Plugin.Enabled.Value) Clear(keepDice: true);
        if (_input != input) { Results.Clear(); _input = input; }

        var cards = fight.Field("cardItemList");
        var candidates = Candidates;
        candidates.Clear();
        var count = cards == null ? 0 : Math.Min(11, cards.Get<int>("Count"));
        for (var i = 0; i < count; i++)
        {
            var card = cards!.Call("get_Item", i)!;
            if (!GameUi.Visible(card) || card.Field("IsRelease")!.Value<bool>()) continue;
            var face = card.Field("com_Card");
            if (!GameUi.Visible(face) || face!.TypeName != "UICom_Card" || !face.Get<bool>("opened")) continue;
            var cost = face.Field("com_CardIcon")!.Field("Cost")!.Get<int>("selectedIndex");
            var config = card.Get("_config")!;
            var parameters = config.Get("Params")!;
            var length = parameters.Get<int>("Count");
            var values = length is >= 2 and <= 3 ? new int[length] : Array.Empty<int>();
            for (var index = 0; index < values.Length; index++)
                values[index] = parameters.Call("get_Item", index)!.Value<int>();
            var bonus = CardAdvisor.FromConfig(config.Get<int>("Id"), config.Get<int>("EffectType"),
                config.Get<int>("CardType"), values, config.Get("BuffIds")!.Get<int>("Count"),
                config.Get("CardIds")!.Get<int>("Count"), cost);
            if (bonus is { } parsed && parsed.Attack != (role == 0)) bonus = null;
            candidates.Add((card, face, bonus, card.Field("_EnableUse")!.Value<bool>()));
        }

        var hit = Plugin.KoMinimum.Value
            ? GameUi.PointerPath().FirstOrDefault(item => item.TypeName == "UIHandCard_Button_Card") : null;
        var hovered = default((RuntimeObject Card, RuntimeObject Face, CardBonus? Bonus, bool Usable));
        foreach (var candidate in candidates)
            if (candidate.Card.Pointer == hit?.Pointer) { hovered = candidate; break; }
        if (Plugin.KoMinimum.Value)
        {
            var preview = hovered.Card != null && hovered.Usable && ready.Get<bool>("touchable");
            CardDiceUi.Show(ui, attackPoint, input, preview ? hovered.Bonus : null, preview, preview && hovered.Bonus == null);
        }
        else CardDiceUi.Clear();
        if (!Plugin.Enabled.Value) { candidates.Clear(); return; }
        var live = Live;
        live.Clear();
        foreach (var item in candidates)
        {
            live.Add(item.Card.Pointer);
            if (!Badges.TryGetValue(item.Card.Pointer, out var badge) || badge.Panel.Get<bool>("isDisposed"))
            {
                badge = new AdviceBadge(item.Face, 220, 54, fontSize: 28);
                Badges[item.Card.Pointer] = badge;
            }
            CardImpact? result = null;
            if (item.Bonus is { } bonus && item.Usable)
            {
                if (!Results.TryGetValue(bonus, out result))
                {
                    if (Results.Count > 64) Results.Clear();
                    result = CardAdvisor.Calculate(input, bonus);
                    Results[bonus] = result;

                }
            }
            var cheaper = false;
            if (item.Bonus is { } current)
                foreach (var other in candidates)
                    if (other.Usable && other.Bonus is { } otherBonus && CardAdvisor.Dominates(otherBonus, current))
                    { cheaper = true; break; }
            var title = !item.Usable ? "사용 불가" : result == null ? "판단 보류*"
                : cheaper ? "더 싼 카드 우선*" : result.Value.Title;
            // Static native-style capsule: no entrance, hover motion, or recommendation thumb on cards.
            badge.Show(AdviceText.CardTitle(title), false, Plugin.UiScale.Value, -70f * Plugin.UiScale.Value);
        }
        Stale.Clear();
        foreach (var key in Badges.Keys) if (!live.Contains(key)) Stale.Add(key);
        foreach (var stale in Stale)
        {
            Badges[stale].Dispose();
            Badges.Remove(stale);
        }
        // Optional counts only; no card IDs, local hand effects or private preview structures.
        if (Plugin.MinimalDiagnostics?.Detailed == true)
        { var supported = 0; foreach (var candidate in candidates) if (candidate.Bonus != null) supported++; Plugin.MinimalDiagnostics.CardCoverage(supported, candidates.Count - supported); }
        candidates.Clear(); // Do not retain native cards between refreshes.
    }

    internal static void Hide(bool keepDice = false)
    {
        foreach (var badge in Badges.Values)
            badge.Hide();
        if (!keepDice) CardDiceUi.Hide();
    }

    internal static void Animate()
    {
        CardDiceUi.Animate();
    }

    internal static void Clear(bool keepDice = false)
    {
        foreach (var badge in Badges.Values) badge.Dispose();
        Badges.Clear();
        Candidates.Clear(); Live.Clear(); Stale.Clear();
        Results.Clear();
        _input = null;
        if (!keepDice) CardDiceUi.Clear();
    }
}
