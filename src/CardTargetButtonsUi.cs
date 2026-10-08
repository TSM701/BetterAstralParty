namespace BetterAstralParty;

// Native sibling sorting controls both drawing and hit-test order. Never move/reparent buttons.
internal static class CardTargetButtonsUi
{
    private sealed record Placement(RuntimeObject Button, int Original, int Applied);
    private static readonly List<Placement> Moved = new();
    private static RuntimeObject? _use;

    internal static void Tick()
    {
        if (!Compatibility.Allowed("CardTargetButtons")) return;
        try { Update(); }
        catch (Exception ex) { Compatibility.Block("CardTargetButtons", ex, Clear); }
    }

    private static void Update()
    {
        var root = GameUi.Root;
        // Window.Show/ShowOn -> GRoot.ShowWindow adds CardWindow directly to the root.
        // Poll every frame, but do not walk unrelated hand artwork, labels and effects.
        var window = root == null ? null : GameUi.Find(root, "CardWindow", maxDepth: 1);
        var use = window?.Get("contentPane")?.Field("com_UseCard");
        // Native RefreshCardInfo_SelectPlayer selects type 1. Land/dice/other windows stay native.
        if (use == null || !GameUi.Visible(use) || use.Field("type")!.Get<int>("selectedIndex") != 1)
        { Clear(); return; }
        if (_use?.Pointer == use.Pointer) return;
        Clear(); _use = use;
        // A button's local order cannot escape a hand panel drawn over its whole window.
        var hand = GameUi.Find(root!, "UIHandCardPanel");
        var parent = window!.Get("parent");
        while (hand != null && hand.Get("parent")?.Pointer != parent?.Pointer) hand = hand.Get("parent");
        if (hand != null) Raise(window, hand.Get<int>("sortingOrder") + 1);
        var cardOrder = use.Field("com_ShowCard")!.Get<int>("sortingOrder");
        foreach (var name in new[] { "btn_Cancel", "btn_Sure" })
            Raise(use.Field(name)!, cardOrder + 1);
    }

    private static void Raise(RuntimeObject item, int minimum)
    {
        var original = item.Get<int>("sortingOrder");
        if (original >= minimum) return;
        Moved.Add(new(item, original, minimum));
        item.Set("sortingOrder", minimum);
    }

    internal static void Clear()
    {
        foreach (var item in Moved)
            if (!item.Button.Get<bool>("isDisposed") && item.Button.Get<int>("sortingOrder") == item.Applied)
                item.Button.Set("sortingOrder", item.Original);
        Moved.Clear(); _use = null;
    }
}
