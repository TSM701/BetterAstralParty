using UnityEngine;

namespace BetterAstralParty;

// Public buff 1071101 only. Reuse native field art, never change buffs or combat outcomes.
internal static class ShushuShieldUi
{
    internal const int BuffId = 1071101;
    internal const string Feature = "ShushuShield";
    private static IntPtr _logicClass, _battleClass;
    private static readonly ActorShield Attacker = new(), Defender = new();
    private sealed record PendingEffect(ActorShield Owner, RuntimeObject Awaiter, GameObject Container);
    private static readonly List<PendingEffect> Loading = new();
    private static RuntimeObject? _window;
    private static readonly ShieldDefenseButton Defense = new();
    private static readonly Dictionary<IntPtr, (RuntimeObject Card, IntPtr Data, ShieldDefenseButton Lock)> Cards = new();
    private static readonly HashSet<IntPtr> LiveCards = new();
    private static readonly List<IntPtr> StaleCards = new();
    private static IntPtr _gobject;
    private static float _findAt;
    private static readonly Action[] Cleanup = { ClearCards, ClearDefense, Attacker.Clear, Defender.Clear };

    private sealed class ActorShield
    {
        internal RuntimeObject? Actor, Player, Buff, Effect, Awaiter;
        internal GameObject? Container;
        internal long BuffKey;
        internal float ScanAt;
        internal bool Retired;

        internal void Clear()
        {
            var container = Container;
            var effect = Effect;
            var loading = Awaiter != null;
            Retired = true;
            Effect = Awaiter = null; Container = null;
            Actor = Player = Buff = null; BuffKey = 0; ScanAt = 0;
            try
            {
                if (container != null) container.SetActive(false);
                ReleaseOwned(effect, container);
            }
            finally
            {
                if (!loading && container != null) UnityEngine.Object.Destroy(container);
            }
        }
    }

    // Async results can also refer to an instance already recycled by the native pool.
    private static bool OwnsEffect(RuntimeObject? effect, GameObject? container) => container != null
        && effect?.Get<bool>("IsActive") == true
        && effect.Get("transform")?.Get("parent")?.Pointer == container.transform.Pointer;

    private static void ReleaseOwned(RuntimeObject? effect, GameObject? container)
    {
        if (OwnsEffect(effect, container)) effect!.Call("ReleaseEffect");
    }

    internal static RuntimeObject? ActiveBuff(RuntimeObject? player)
    {
        var shown = player?.Get("buffContainer")?.Call("GetShowBuffs", player, false)?.Field("Item1");
        var count = shown?.Get<int>("Count") ?? 0;
        if (count < 0 || count > 256) throw new InvalidOperationException("Invalid public buff count");
        for (var i = 0; i < count; i++)
        {
            var buff = shown!.Call("get_Item", i)!;
            if (buff.Get<int>("BuffId") == BuffId && buff.Get<int>("DelayRound") <= 0) return buff;
        }
        return null;
    }

    // Drain even when PvP/settings/compatibility cleanup has retired a load. No native callbacks.
    internal static void PollLoads()
    {
        for (var i = Loading.Count - 1; i >= 0; i--)
        {
            var pending = Loading[i];
            if (!pending.Awaiter.Get<bool>("IsCompleted")) continue;
            var slot = pending.Owner;
            Loading.RemoveAt(i);
            try
            {
                var effect = pending.Awaiter.Call("GetResult");
                if (slot.Retired || pending.Container == null || slot.Container?.Pointer != pending.Container.Pointer
                    || slot.Awaiter?.Pointer != pending.Awaiter.Pointer)
                {
                    ReleaseOwned(effect, pending.Container);
                    if (pending.Container != null) UnityEngine.Object.Destroy(pending.Container);
                }
                else { slot.Effect = effect; slot.Awaiter = null; }
            }
            catch (Exception ex)
            {
                if (pending.Container != null) UnityEngine.Object.Destroy(pending.Container);
                if (slot.Awaiter?.Pointer == pending.Awaiter.Pointer) slot.Awaiter = null;
                slot.Clear();
                Compatibility.Block(Feature, ex, Clear);
            }
        }
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            if (!Plugin.ShushuShield.Value || !Application.isFocused || ModUi.IsOpen || GameUi.Root == null)
            { Clear(); return; }
            if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            if (_battleClass == IntPtr.Zero) _battleClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
            if (_logicClass == IntPtr.Zero || _battleClass == IntPtr.Zero) return;
            var logic = RuntimeObject.StaticField(_logicClass, "_inst");
            var map = logic?.Get("room")?.Get("curRoomInfo")?.Field("info")?.Get<int>("MapType") ?? 0;
            if (!VisibleCombatAdvisor.IsPve(map) || logic?.Get("fight")?.Field("fightStatus")?.Value<bool>() != true)
            { Clear(); return; }
            Tick(logic!, RuntimeObject.StaticField(_battleClass, "inst")?.Field("directorManager"));
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    internal static void Tick(RuntimeObject logic, RuntimeObject? director)
    {
        if (!Plugin.ShushuShield.Value) { Clear(); return; }
        if (!Compatibility.Allowed(Feature)) return;
        try { Update(logic, director); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update(RuntimeObject logic, RuntimeObject? director)
    {
        UpdateActor(Attacker, director?.Get("attacker"), logic, true);
        UpdateActor(Defender, director?.Get("victim"), logic, false);
        var root = GameUi.Root;
        if (!GameUi.Visible(_window) && root != null && Time.unscaledTime >= _findAt)
        {
            _findAt = Time.unscaledTime + 0.2f;
            _window = GameUi.Find(root, "FightWindow", maxDepth: 1);
        }
        var ui = GameUi.Visible(_window) ? _window!.Get("contentPane") : null;
        var deciding = Deciding(ui);
        Defense.Update(ui?.Field("btn_Defend"), Defender.Buff != null && deciding, deciding);
        UpdateCards(ui);
    }

    private static bool ChoosingCards(RuntimeObject? ui) => ui?.Field("playerState")?.Get<int>("selectedIndex") == 1
        && ui.Field("step")?.Get<int>("selectedIndex") == 3
        && GameUi.Visible(ui.Field("com_Card")?.Field("btn_FinishPkCard"));

    private static bool SameCard(RuntimeObject card, IntPtr data) => !card.Get<bool>("isDisposed")
        && card.Get("CardData")?.Pointer == data && card.Field("IsRelease")?.Value<bool>() == false;

    private static void UpdateCards(RuntimeObject? ui)
    {
        // GetVailCard selects EffectType 2 for the local defender. Never inspect spectator hands.
        if (Defender.Buff == null || !ChoosingCards(ui)) { ClearCards(); return; }
        var list = _window?.Field("cardItemList");
        var count = list?.Get<int>("Count") ?? 0;
        if (count is < 0 or > 11) throw new InvalidOperationException("Invalid combat hand count");
        LiveCards.Clear();
        if (_gobject == IntPtr.Zero) _gobject = RuntimeObject.FindClass("FairyGUI", "GObject");
        var dragging = RuntimeObject.StaticCall(_gobject, "get_draggingObject");
        for (var i = 0; i < count; i++)
        {
            var card = list!.Call("get_Item", i)!;
            var data = card.Get("CardData");
            if (data == null || !SameCard(card, data.Pointer) || !GameUi.Visible(card)
                || card.Get("_config")?.Get<int>("EffectType") != 2) continue;
            LiveCards.Add(card.Pointer);
            if (Cards.TryGetValue(card.Pointer, out var old) && old.Data != data.Pointer)
            { old.Lock.Clear(false); Cards.Remove(card.Pointer); }
            if (!Cards.TryGetValue(card.Pointer, out var entry))
            {
                entry = (card, data.Pointer, new ShieldDefenseButton());
                Cards.Add(card.Pointer, entry);
            }
            entry.Lock.Update(card, true, true);
            // A setting enabled during an existing drag must not leave a pending drop.
            if (dragging?.Pointer == card.Pointer) card.Call("StopDrag");
        }
        StaleCards.Clear();
        foreach (var key in Cards.Keys) if (!LiveCards.Contains(key)) StaleCards.Add(key);
        foreach (var key in StaleCards)
        {
            var entry = Cards[key]; Cards.Remove(key);
            entry.Lock.Clear(SameCard(entry.Card, entry.Data));
        }
    }

    private static void ClearCards()
    {
        if (Cards.Count == 0) return;
        var choosing = ChoosingCards(GameUi.Visible(_window) ? _window!.Get("contentPane") : null);
        StaleCards.Clear(); StaleCards.AddRange(Cards.Keys);
        foreach (var key in StaleCards)
        {
            var entry = Cards[key]; Cards.Remove(key);
            try { entry.Lock.Clear(choosing && SameCard(entry.Card, entry.Data)); }
            catch (Exception ex) { Compatibility.Block(Feature, ex); }
        }
        LiveCards.Clear(); StaleCards.Clear();
    }

    private static bool Deciding(RuntimeObject? ui) => ui != null && ui.Field("step")?.Get<int>("selectedIndex") == 5
            && ui.Field("playerState")?.Get<int>("selectedIndex") == 1
            && GameUi.Visible(ui.Field("btn_Defend")) && GameUi.Visible(ui.Field("btn_Dodge"))
            && ui.Field("btn_Dodge")!.Get<bool>("touchable");

    internal static bool IsBlocking(RuntimeObject button) => Defense.IsBlocking(button);

    private static void UpdateActor(ActorShield slot, RuntimeObject? actor, RuntimeObject logic, bool attacking)
    {
        var ui = actor?.Field("_UI");
        var player = ui?.Get<bool>("isDisposed") == false ? ui.Field(attacking ? "attackerData" : "defenderData") : null;
        if (actor?.Pointer != slot.Actor?.Pointer || player?.Pointer != slot.Player?.Pointer)
        { slot.Clear(); slot.Actor = actor; slot.Player = player; }
        if (player == null) return;
        if (Time.unscaledTime >= slot.ScanAt)
        {
            var buff = ActiveBuff(player);
            var key = buff?.Get<long>("UniqueId") ?? 0;
            if ((buff == null) != (slot.Buff == null) || key != slot.BuffKey)
            {
                // Keep a retired in-flight slot alive until its native awaiter is consumed.
                slot.Clear(); slot.Actor = actor; slot.Player = player;
                slot.Buff = buff; slot.BuffKey = key; slot.Retired = false;
                if (buff != null) CreateEffect(slot, logic);
            }
            slot.ScanAt = Time.unscaledTime + 0.1f;
        }
        if (slot.Container != null && !slot.Retired && slot.Awaiter == null)
        {
            var renderer = actor!.Field("actorRenderer");
            var visible = renderer?.Get<bool>("activeInHierarchy") == true && GameUi.Visible(ui);
            if (slot.Container.activeSelf != visible)
            {
                slot.Container.SetActive(visible);
                // Loading under an inactive staging parent must not leave particles stopped.
                if (visible && OwnsEffect(slot.Effect, slot.Container))
                    slot.Effect!.Call("PlayAllParticles", slot.Effect.Get("transform"));
            }
        }
    }

    private static RuntimeObject Config() => RuntimeObject.StaticCall(RuntimeObject.FindClass("UI", "UIHelper"), "GetBuffConfigure", BuffId)!;

    private static void CreateEffect(ActorShield slot, RuntimeObject logic)
    {
        var config = Config();
        // If a future native build already displays it, do not draw a second shield.
        if (config.Get<bool>("IsShowEffectDuringPK")) return;
        var id = config.Get<int>("EffectID");
        // Same caster-skin lookup as LianSkillShieldBuffEffectHandler.Play, not the target's skin.
        var caster = logic.Get("battle")?.Call("GetPlayerDataByHeroId", 107);
        var skin = caster?.Field("player")?.Get("standingPainting")?.Get<int>("ItemID");
        var replacements = config.Get("SkinReplaceEffectID");
        if (skin is { } item && replacements?.Call("ContainsKey", item)?.Value<bool>() == true)
            id = replacements.Call("get_Item", item)!.Value<int>();
        var parent = slot.Actor!.Field("effectParent");
        if (id == 0 || parent == null) return;
        slot.Container = new GameObject("BetterAstralParty.ShushuShield");
        slot.Container.SetActive(false);
        slot.Container.transform.SetParent(new Transform(parent.Pointer), false);
        var manager = RuntimeObject.StaticCall(RuntimeObject.FindClass("Core", "EffectManager"), "get_inst")!;
        // Match BattleActor.CreateBuffEffect's local placement and combat scale.
        slot.Awaiter = manager.Call("PlayById", id, Vector3.zero, Quaternion.identity,
            new RuntimeObject(slot.Container.transform.Pointer), null, 0.25f)!.Call("GetAwaiter")!;
        Loading.Add(new(slot, slot.Awaiter, slot.Container));
    }

    internal static void Clear()
    {
        // One failed native effect must not strand the input lock or escape into BattleStatus.
        foreach (var cleanup in Cleanup)
            try { cleanup(); }
            catch (Exception ex) { Compatibility.Block(Feature, ex); }
    }

    private static void ClearDefense()
    {
        var deciding = false;
        try { deciding = Deciding(GameUi.Visible(_window) ? _window!.Get("contentPane") : null); }
        finally
        {
            _window = null; _findAt = 0;
            Defense.Clear(deciding);
        }
    }
}

// User-authorized, reversible native button exception; no click handlers or automatic Dodge.
internal sealed class ShieldDefenseButton
{
    private RuntimeObject? _button;
    private float _originalAlpha, _appliedAlpha;
    private bool _canRestore;
    internal bool IsBlocking(RuntimeObject button) => _button?.Pointer == button.Pointer;

    internal void Update(RuntimeObject? button, bool blocked, bool deciding)
    {
        if (_button?.Pointer != button?.Pointer) Clear(_canRestore && deciding);
        _canRestore = deciding;
        if (!blocked) Clear();
        if (!blocked || button == null || button.Get<bool>("isDisposed")) return;
        if (_button == null)
        {
            if (!button.Get<bool>("touchable")) return; // Do not take ownership of a native restriction.
            _button = button; _originalAlpha = button.Get<float>("alpha"); _appliedAlpha = _originalAlpha * 0.45f;
        }
        _canRestore = deciding;
        if (button.Get<bool>("touchable")) button.Set("touchable", false);
        if (button.Get<float>("alpha") == _originalAlpha) button.Set("alpha", _appliedAlpha);
    }

    internal void Clear(bool? canRestore = null)
    {
        var button = _button;
        var restore = canRestore ?? _canRestore;
        _button = null; _canRestore = false;
        if (button != null && !button.Get<bool>("isDisposed"))
        {
            if (restore && !button.Get<bool>("touchable")) button.Set("touchable", true);
            if (button.Get<float>("alpha") == _appliedAlpha) button.Set("alpha", _originalAlpha);
        }
    }
}
