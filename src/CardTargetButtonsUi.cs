namespace BetterAstralParty;

// Native sibling sorting controls both drawing and hit-test order. Never move/reparent buttons.
internal static class CardTargetButtonsUi
{
    private sealed record Placement(RuntimeObject Item, RuntimeObject? Parent, int Index, int Original, int Applied);
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
        if (_use?.Pointer != use.Pointer) { Clear(); _use = use; }
        // A button's local order cannot escape a hand panel drawn over its whole window.
        var hand = GameUi.Find(root!, "UIHandCardPanel", maxDepth: 1);
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
        var parent = item.Get("parent");
        var previous = Moved.FindIndex(p => p.Item.Pointer == item.Pointer);
        if (previous >= 0 && Moved[previous].Applied == original
            && Moved[previous].Parent?.Pointer == parent?.Pointer)
            Moved[previous] = Moved[previous] with { Applied = minimum };
        else
        {
            if (previous >= 0) Moved.RemoveAt(previous);
            Moved.Add(new(item, parent, parent?.Call("GetChildIndex", item)!.Value<int>() ?? -1, original, minimum));
        }
        item.Set("sortingOrder", minimum);
    }

    internal static void Clear()
    {
        for (var i = Moved.Count - 1; i >= 0; i--)
        {
            var item = Moved[i];
            if (item.Item.Get<bool>("isDisposed") || item.Parent?.Get<bool>("isDisposed") == true
                || item.Item.Get("parent")?.Pointer != item.Parent?.Pointer
                || item.Item.Get<int>("sortingOrder") != item.Applied) continue;
            item.Item.Set("sortingOrder", item.Original);
            // FairyGUI appends zero-sort children when sorting is reset; restore their native slot too.
            if (item.Original == 0 && item.Parent != null && item.Index >= 0)
                item.Parent.Call("SetChildIndex", item.Item, item.Index);
        }
        Moved.Clear(); _use = null;
    }
}
