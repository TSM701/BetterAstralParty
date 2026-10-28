using UnityEngine;

namespace BetterAstralParty;

// Field player list (approved draft 3, 2026-10-23): a tab hanging under each player's coin/star bar with the public hand
// count and the character's role. The tab is the bar's own layers continued downward: private clones of the native
// grey base (灰底) and the slot-coloured animated strip (PlayerInfo_Ani_Bottom), at the bar's x, behind the bar so its
// black bottom border is the tab's top edge. Owned objects only; native children, input and layout are never changed.
internal static class PlayerTabUi
{
    private const string Feature = "PlayerTab";
    // In UICom_PlayerInfo units: from right of the coin to the bar's straight bottom edge, under the bar's border.
    internal const float Left = 162, Top = 103, Height = 38, HandWidth = 44, RoleWidth = 86, Radius = 11, Inset = 3;
    // The clones are lowered so the tab shows the strip's middle rows, not its rounded bottom edge.
    private const float Lower = 37;

    private sealed class Tab
    {
        internal RuntimeObject Owner = null!, Panel = null!, Mask = null!, Base = null!, Strip = null!, Outline = null!, Hand = null!, Role = null!;
        internal RuntimeObject? Card;
        internal (bool Hand, bool HasRole, int Count, string Text, bool Korean) Shown = (false, false, -1, "", false);
    }

    private static readonly Dictionary<IntPtr, Tab> Tabs = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static IntPtr _logicClass;
    private static float _scanAt;

    // Width for the parts that are ON.
    internal static float Width(bool hand, bool role) => (hand ? HandWidth : 0) + (role ? RoleWidth : 0);

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            var hand = Plugin.PlayerTabHand.Value; var role = Plugin.PlayerTabRole.Value;
            if (!Plugin.PlayerTab.Value || !hand && !role || ModUi.IsOpen || GameUi.Root == null) { Clear(); return; }
            if (Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 0.2f; Scan(hand, role); }
            // The coloured strip animates: keep each clone on the native frame and colour.
            foreach (var tab in Tabs.Values) Sync(tab);
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Scan(bool hand, bool role)
    {
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (!VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)) { Clear(); return; }
        Seen.Clear();
        // No container gate: picking a player (sending coins) moves the selectable buttons into com_Empty
        // (AdjustPlayerParent) and hides the container (2026-10-25); each bar's own visibility decides.
        var container = logic?.Get("battle")?.Field("battleInfo")?.Get("ui")?.Field("com_BattlePlayer")?.Field("com_Container");
        if (container != null)
            for (var slot = 1; slot <= 4; slot++)
            {
                var button = container.Field("com_Player_" + slot);
                Attach(button?.Field("com_player"), button?.Field("PlayerData"), hand, role);
            }
        // The encounter screen (Fight_LaunchPK) draws its own Com_PlayerInfo copies over the field list (2026-10-25:
        // the tabs vanished on the local player's encounter); FightWindow.RefreshPlayerInfo fills their _playerData.
        var launch = GameUi.Find(GameUi.Root!, "FightWindow", maxDepth: 1)?.Get("contentPane")?.Field("com_LaunchPK");
        if (GameUi.Visible(launch))
            for (var slot = 1; slot <= 4; slot++)
            {
                var owner = launch!.Field("com_P" + slot);
                Attach(owner, owner?.Field("_playerData"), hand, role);
            }
        // Sending coins (ATMWindow) lists its own copies too: each list_Players item's com_playerInfo, filled by
        // RendererTargetPlayers through RefreshData.
        var atm = GameUi.Find(GameUi.Root!, "ATMWindow", maxDepth: 1)?.Get("contentPane")?.Field("list_Players");
        if (GameUi.Visible(atm))
            for (int i = 0, count = atm!.Get<int>("numChildren"); i < count; i++)
            {
                var owner = atm.Call("GetChildAt", i)?.Field("com_playerInfo");
                Attach(owner, owner?.Field("_playerData"), hand, role);
            }
        foreach (var pair in Tabs.ToArray())
            if (!Seen.Contains(pair.Key)) { GameUi.Dispose(pair.Value.Panel); Tabs.Remove(pair.Key); }
    }

    private static void Attach(RuntimeObject? owner, RuntimeObject? player, bool hand, bool role)
    {
        if (owner == null || player == null || !GameUi.Visible(owner)) return;
        Seen.Add(owner.Pointer);
        if (!Tabs.TryGetValue(owner.Pointer, out var tab) || tab.Panel.Get<bool>("isDisposed"))
        {
            tab = Create(owner);
            Tabs[owner.Pointer] = tab;
        }
        // Same public count as Combat Status; the role is the native CharacterTagType the preparation screen shows.
        var count = player.Get("cardContainer")?.Get<int>("CardCount") ?? -1;
        // RoomPlayer.characterConfig is a CharacterHandle (Id only); the role lives in the Character table.
        var id = player.Field("player")?.Field("characterConfig")?.Field("Id")?.Value<int>() ?? 0;
        var text = CharacterRole.Label(id > 0 ? CharacterGuideUi.Config(id)?.Get<int>("TagType") ?? 0 : 0, ModText.Korean);
        Refresh(tab, hand, role && text.Length > 0, count, text);
    }

    private static Tab Create(RuntimeObject owner)
    {
        var tab = new Tab { Owner = owner };
        tab.Panel = NativeUi.Component(owner, Width(true, true), Height);
        try
        {
            tab.Panel.Set("opaque", true);
            // Behind the bar: its grey base is the bar's first layer.
            owner.Call("SetChildIndex", tab.Panel, owner.Call("GetChildIndex", owner.Call("GetChild", "n82")!)!.Value<int>());
            tab.Mask = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            tab.Mask.Set("touchable", false);
            tab.Panel.Call("AddChild", tab.Mask);
            tab.Base = NativeUi.Create("Common_Internal", "灰底")!;
            tab.Strip = NativeUi.Create("Common_Internal", "PlayerInfo_Ani_Bottom")!;
            foreach (var (clone, source) in new[] { (tab.Base, "n82"), (tab.Strip, "ani") })
            {
                var native = owner.Call("GetChild", source)!;
                clone.Set("touchable", false);
                clone.Call("SetSize", native.Get<float>("width"), native.Get<float>("height"));
                clone.Call("SetScale", native.Get<float>("scaleX"), native.Get<float>("scaleY"));
                clone.Call("SetXY", native.Get<float>("x") - Left, native.Get<float>("y") - Top + Lower);
                tab.Panel.Call("AddChild", clone);
            }
            tab.Strip.Set("playing", false);
            tab.Outline = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            tab.Outline.Set("touchable", false);
            tab.Panel.Call("AddChild", tab.Outline);
            // The count uses the coin count's number format; the role the nickname's (its font has Hangul, the coin
            // font does not, and its player colour was kept on the user's call, 2026-10-23).
            tab.Hand = NativeUi.Label(tab.Panel, "", 24, 0, HandWidth - 24, Height, 24);
            tab.Role = NativeUi.Label(tab.Panel, "", 0, 0, RoleWidth - Inset * 2, Height, 20); // Clear of the outline
            foreach (var (label, source) in new[] { (tab.Hand, "txt_Gold"), (tab.Role, "txt_PlayerName") })
            {
                NativeUi.CopyTextFormat(label, owner.Field(source)!);
                label.Set("align", 1); label.Set("verticalAlign", 1);
                label.Set("touchable", false);
            }
            // Shrink: a long role ("카드 특화") or a two-digit count (issue #17: "10" wrapped and showed "1") fits its part instead of being cut.
            foreach (var label in new[] { tab.Hand, tab.Role }) { label.Set("singleLine", true); label.Set("autoSize", 3); }
            tab.Card = NativeUi.Icon(tab.Panel, "Com_Icon_Card", 5, 7, Height - 14);
            tab.Panel.Set("mask", tab.Mask.Get("displayObject")!);
            return tab;
        }
        catch { GameUi.Dispose(tab.Panel); throw; }
    }

    private static void Refresh(Tab tab, bool hand, bool role, int count, string text)
    {
        var shown = (hand, role, count, text, ModText.Korean);
        if (tab.Shown == shown) return;
        var width = Width(hand, role);
        if (tab.Shown.Hand != hand || tab.Shown.HasRole != role)
        {
            tab.Panel.Call("SetSize", width, Height);
            tab.Panel.Call("SetXY", Left, Top);
            tab.Mask.Call("SetSize", width, Height);
            tab.Mask.Get("shape")!.Call("DrawRoundRect", 0f, Color.white, Color.white, 0f, 0f, Radius, Radius);
            // Sides and bottom; the top line runs under the bar's border. Twice the bar's width: the mask keeps the inner half.
            tab.Outline.Call("SetXY", 0f, -8f);
            tab.Outline.Call("SetSize", width, Height + 8);
            tab.Outline.Get("shape")!.Call("DrawRoundRect", 6f, Color.black, new Color(0, 0, 0, 0), 0f, 0f, Radius, Radius);
            tab.Hand.Set("visible", hand);
            tab.Card?.Set("visible", hand);
            tab.Role.Set("visible", role);
            tab.Role.Call("SetXY", (hand ? HandWidth : 0f) + Inset, 0f);
        }
        var number = count < 0 ? "—" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (tab.Hand.Get("text")?.String() != number) tab.Hand.Set("text", number);
        if (tab.Role.Get("text")?.String() != text) tab.Role.Set("text", text);
        tab.Panel.Set("tooltips", hand ? ModText.Text("공개 손패 장수") : "");
        tab.Shown = shown;
    }

    private static void Sync(Tab tab)
    {
        if (tab.Owner.Get<bool>("isDisposed") || tab.Panel.Get<bool>("isDisposed")) return;
        var native = tab.Owner.Field("ani");
        if (native == null) return;
        var frame = native.Get<int>("frame");
        if (tab.Strip.Get<int>("frame") != frame) tab.Strip.Set("frame", frame);
        var color = native.Get<Color>("color");
        if (tab.Strip.Get<Color>("color") != color) tab.Strip.Set("color", color);
    }

    internal static void Clear()
    {
        foreach (var tab in Tabs.Values) GameUi.Dispose(tab.Panel);
        Tabs.Clear();
        _scanAt = 0;
    }
}
