using UnityEngine;

namespace BetterAstralParty;

// Field player list (approved draft 3, 2026-10-23; moved and extended by approved draft A5, 2026-10-31): a tab hanging
// under each player's coin/star bar with the public hand count, the character's role and the active skill's cooldown.
// The tab is the bar's own layers continued downward: private clones of the native grey base (灰底) and the
// slot-coloured animated strip (PlayerInfo_Ani_Bottom), at the bar's x, behind the bar so its black bottom border is the
// tab's top edge and the portrait stays drawn above its left end. Owned objects only; native children, input and layout
// are never changed.
internal static class PlayerTabUi
{
    private const string Feature = "PlayerTab", SkillFeature = "PlayerTabSkill";
    // In UICom_PlayerInfo units: from under the coin (A5: 35 left of the first draft's 162) to the bar's rounded end
    // (298 with every part shown), under the bar's border.
    internal const float Left = 127, Top = 103, Height = 38, HandWidth = 44, RoleWidth = 86, SkillWidth = 65, Radius = 11, Inset = 3;
    // Beside the skill part the role gives it 24 (option 1, 2026-10-31: the turns were too small in game), so the turns
    // reach about the count's size and only a long role ("카드 특화") shrinks; without the skill part the role keeps 86.
    private const float SharedRoleWidth = 62, RoleLighten = 0.6f, RoleStroke = 1.6f;
    // The tab reaches this far up behind the bar: the bar's bottom outline curves up from x 292 to its tip at 302, and
    // the tab's right side rises to meet it there instead of leaving the field visible under the curve.
    private const float Rise = 10;
    // The clones are lowered so the tab shows the strip's middle rows, not its rounded bottom edge; the grey base is
    // widened so its own rounded end stays right of the tab.
    private const float Lower = 37, BaseExtra = 40, OutlineWidth = 6;
    // The player info window's stopwatch (BattlePlayerInfo_Com_Skill shows Common/时间 beside txt_CD).
    private const float ClockSize = 20;
    // The game's turn colours as the user chose them (2026-10-31): remaining turns green (#7FFD37, RefreshActiveSkillCD),
    // "/total" in the label's white, always.
    private const string Turns = "[color=#7FFD37]{0}[/color]/{1}";

    private sealed class Tab
    {
        internal RuntimeObject Owner = null!, Panel = null!, Clip = null!, Mask = null!, Base = null!, Strip = null!, Outline = null!, Hand = null!, Role = null!, Skill = null!;
        internal RuntimeObject? Card, Clock;
        internal (bool Hand, bool HasRole, bool HasSkill, int Count, string Text, int Left, int Total, bool Korean) Shown = (false, false, false, -1, "", -1, -1, false);
    }

    private static readonly Dictionary<IntPtr, Tab> Tabs = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static IntPtr _logicClass, _configClass, _asksRoom, _netClass;
    private static float _scanAt;

    // Other players' turns as the player info window gets them in PvE (RefreshHeroInfo_PVE): it asks the server
    // (RequestGetHeroInfoC2S) and shows the reply's BattlePlayerData.SkillCD, because every round start copies the
    // receiver's own skill map onto all characters and activeSkillCD is right only for the local player (user-chosen
    // 2026-11-01: ask the server too, once per player when a round starts or a skill use moves the count). A card that
    // changes another player's cooldown (운명의 인도: active skill cooldown -1) reaches only that player's client
    // (BattleLogic.UpdateBaseAttr applies HeroCdChangeS2C to the local player alone), so a move of the public hand count
    // asks too, and once more FollowUp seconds later in case the card resolved after its hand count moved (2026-11-15).
    // Anything else that lowers a cooldown (a land, a chip, a teammate's skill, an event) changes the public table too, so a
    // change of any player's hand, coins, HP, level, land or buff count asks every other player whose turns are not 0 yet,
    // at most once every Gap seconds each; changes in between go with the next ask (user-requested 2026-11-16).
    private sealed class Ask
    {
        internal int Round = -1, Client = -1, Hand = -1, Table = -1; internal float Again = float.PositiveInfinity, At = float.NegativeInfinity; internal bool Answered; internal RuntimeObject? Reply;
        // The round and client count of the last scan, and a client count newer than the stored answer (a skill use).
        internal int SeenRound = -1, SeenClient = -1, Fresh = -1; internal float FreshAt = float.NegativeInfinity;

        // A finished reply has already run the native callback that stores SkillCD (RPCMsgManager: DealServerCallback,
        // then Finish). A failed one is not retried until the round or the count moves again. Scan polls every reply too,
        // so one that fails as the game ends is recorded after the player bars are gone. An answer asked for after a skill
        // use replaces its client count.
        internal void Finish(float now)
        {
            if (Reply?.Field<bool>("isCompleted") != true) return;
            var failed = Reply.Field<bool>("isTimeOut") || Reply.Field<int>("errId") != 0;
            if (failed && Plugin.Diagnostics.IsRecording)
                Plugin.Diagnostics.State("playerTab.skillCd", "reply failed; err=" + Reply.Field<int>("errId") + "; timeout=" + Reply.Field<bool>("isTimeOut")
                    + "; sentMs=" + (int)((now - At) * 1000));
            Answered |= !failed;
            if (!failed && At >= FreshAt) Fresh = -1;
            Reply = null;
        }
    }
    private const float FollowUp = 3f, Gap = 2f;
    private static int _tableState, _tableChanges;
    private static readonly Dictionary<long, Ask> Asks = new();
    // A request goes out only once the room has been open (see Server) with the public table unchanged for Settle
    // seconds. On 2026.11.2-dev.21 (2026-11-17) 11001 came once more at a match's end, with the gates below: the local
    // player's last card went out 7 s before the settlement and its presentation ended 4 s before it, and the field still
    // changed in between, so the server's last pushes came apart and the queue was empty between them. A quiet room means
    // the server has sent nothing for a while.
    private const float Settle = 3f;
    private static float _quietAt;
    private static bool Quiet(bool open, bool changed, float now)
    {
        if (!open || changed) _quietAt = now + Settle;
        return open && now >= _quietAt;
    }
    // This scan's server side; null where the window would not ask either (replays, or no requests can be sent). Open: a
    // running room (RoomStateType RUNNING 4) outside fights, the only time a request goes out (2026-11-16). A request while
    // the server was ending the game came back as error 11001, which the game showed as "잘못된 명령 코드 - 11001 / 방에
    // 입장할 수 없습니다": twice on 2026-11-15/16, both the 3 s follow-up during the match's final fight, 1 and 7 s before
    // its settlement. GameFinishS2C switches the room to SETTLEMENT 5 at once; a move seen meanwhile asks once open again.
    // It came back once more on dev.10 (2026-11-16): an ask 2 s after a co-op challenge's final fight, 8 s before the
    // settlement, while the room still read RUNNING. Server pushes wait in RPCMsgManager._OnPushRPCMsg and leave it only after
    // their handler finished (ProcessNextPushRPCMsg), so GameFinishS2C sat there behind the fight's presentation; Open also
    // needs that queue empty, i.e. everything the server sent is handled, and stays closed until the room is quiet.
    // Table: how many times the public table state has changed this match. Running: a running room outside fights, which is
    // all a round start's request waits for (see Asked).
    private readonly record struct Server(RuntimeObject Battle, long Self, int Round, bool Open, int Table, bool Running);

    // Width for the parts that are ON.
    internal static float Width(bool hand, bool role, bool skill) => (hand ? HandWidth : 0) + (role ? RolePart(skill) : 0) + (skill ? SkillWidth : 0);
    private static float RolePart(bool skill) => skill ? SharedRoleWidth : RoleWidth;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            var hand = Plugin.PlayerTabHand.Value; var role = Plugin.PlayerTabRole.Value; var skill = Plugin.PlayerTabSkill.Value;
            if (!Plugin.PlayerTab.Value || !hand && !role && !skill || ModUi.IsOpen || GameUi.Root == null) { Clear(); return; }
            if (Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 0.2f; Scan(hand, role, skill); }
            // The coloured strip animates: keep each clone on the native frame and colour.
            foreach (var tab in Tabs.Values) Sync(tab);
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Scan(bool hand, bool role, bool skill)
    {
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (!VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)) { Clear(); return; }
        // Answers outlive tab rebuilds (settings, screens) and go with the match.
        if (room!.Pointer != _asksRoom) { Asks.Clear(); _asksRoom = room.Pointer; }
        var battle = logic?.Get("battle");
        var session = logic?.Get("replay")?.Get("Session");
        if (_netClass == IntPtr.Zero) _netClass = RuntimeObject.FindClass("Core.Net", "NetManager");
        var pushes = _netClass == IntPtr.Zero ? null : RuntimeObject.StaticField(_netClass, "_inst")?.Field("RPC")?.Field("_OnPushRPCMsg");
        var running = logic!.Get("room")?.Field("roomController")?.Field("roomStateType")?.Value<int>() == 4
            && logic.Get("fight")?.Field("fightStatus")?.Value<bool>() != true;
        var open = running && pushes?.Get<int>("Count") == 0;
        var asking = battle != null && session?.Get<bool>("IsReplay") == false && session.Get<bool>("CanSendC2S");
        var now = Time.unscaledTime;
        var changed = false;
        if (asking && skill)
        {
            var state = TableState(battle!);
            if (state != _tableState) { _tableState = state; _tableChanges++; changed = true; }
            foreach (var ask in Asks.Values) ask.Finish(now);
        }
        var quiet = Quiet(open, changed, now);
        var server = asking
            ? new Server(battle!, logic.Get("account")?.Call("GetPlayerID")?.Value<long>() ?? 0, room.Get<int>("Round"), quiet, _tableChanges, running) : (Server?)null;
        Seen.Clear();
        // No container gate: picking a player (sending coins) moves the selectable buttons into com_Empty
        // (AdjustPlayerParent) and hides the container (2026-10-25); each bar's own visibility decides.
        var container = logic?.Get("battle")?.Field("battleInfo")?.Get("ui")?.Field("com_BattlePlayer")?.Field("com_Container");
        if (container != null)
            for (var slot = 1; slot <= 4; slot++)
            {
                var button = container.Field("com_Player_" + slot);
                Attach(button?.Field("com_player"), button?.Field("PlayerData"), hand, role, skill, server);
            }
        // The encounter screen (Fight_LaunchPK) draws its own Com_PlayerInfo copies over the field list (2026-10-25:
        // the tabs vanished on the local player's encounter); FightWindow.RefreshPlayerInfo fills their _playerData.
        var launch = GameUi.Find(GameUi.Root!, "FightWindow", maxDepth: 1)?.Get("contentPane")?.Field("com_LaunchPK");
        if (GameUi.Visible(launch))
            for (var slot = 1; slot <= 4; slot++)
            {
                var owner = launch!.Field("com_P" + slot);
                Attach(owner, owner?.Field("_playerData"), hand, role, skill, server);
            }
        // Sending coins (ATMWindow) lists its own copies too: each list_Players item's com_playerInfo, filled by
        // RendererTargetPlayers through RefreshData.
        var atm = GameUi.Find(GameUi.Root!, "ATMWindow", maxDepth: 1)?.Get("contentPane")?.Field("list_Players");
        if (GameUi.Visible(atm))
            for (int i = 0, count = atm!.Get<int>("numChildren"); i < count; i++)
            {
                var owner = atm.Call("GetChildAt", i)?.Field("com_playerInfo");
                Attach(owner, owner?.Field("_playerData"), hand, role, skill, server);
            }
        foreach (var pair in Tabs.ToArray())
            if (!Seen.Contains(pair.Key)) { GameUi.Dispose(pair.Value.Panel); Tabs.Remove(pair.Key); }
    }

    private static void Attach(RuntimeObject? owner, RuntimeObject? player, bool hand, bool role, bool skill, Server? server)
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
        var text = CharacterRole.Label(id > 0 ? PrepSkillInfoUi.Config(id)?.Get<int>("TagType") ?? 0 : 0, ModText.Korean);
        (int Left, int Total)? cooldown = null;
        // The turns have their own breaker: a failed lookup or request hides only the skill part.
        if (skill && Compatibility.Allowed(SkillFeature))
            try { cooldown = Cooldown(player, server, count); }
            catch (Exception ex) { Compatibility.Block(SkillFeature, ex, Asks.Clear); }
        Refresh(tab, hand, role && text.Length > 0, count, text, cooldown);
    }

    // The player info window's own values (BattlePlayerInfoWindow.GetSkillInfo): the active skill's shown configuration
    // holds the total turns and the countdown BattleProperty.activeSkillCD the turns left (0 = ready now), which for
    // other players comes from the server as the window shows it.
    private static (int Left, int Total)? Cooldown(RuntimeObject player, Server? server, int hand)
    {
        var id = player.Field("player")?.Call("GetBattleActiveSkillId")?.Value<int>() ?? 0;
        if (_configClass == IntPtr.Zero) _configClass = RuntimeObject.FindClass("", "StaticConfigure");
        if (id <= 0 || _configClass == IntPtr.Zero) return null;
        var table = RuntimeObject.StaticCall(_configClass, "get_Skill")?.Get("InfoDict");
        var config = table != null && table.Call("ContainsKey", id)!.Value<bool>() ? table.Call("get_Item", id) : null;
        // The window lists shown skills only (UIHelper.GetSkillConfigs) and counts down active ones (SkillType 1).
        if (config == null || !config.Get<bool>("IsShow") || config.Get<int>("SkillType") != 1) return null;
        var total = config.Get<int>("Round");
        var left = player.Get("Property")?.Field("activeSkillCD")?.Get<int>("Value") ?? -1;
        var who = player.Field("player")?.Get<long>("Id") ?? 0;
        if (server is { } side && who != 0 && who != side.Self && left >= 0) left = Asked(side, who, left, hand, Time.unscaledTime);
        return total > 0 && left >= 0 ? (left, total) : null;
    }

    // The public table state: every player's hand, coins, HP, level, land and buff count (monsters left out).
    private static int TableState(RuntimeObject battle)
    {
        var players = battle.Field("PlayerDatas");
        var state = new HashCode();
        for (int i = 0, count = Math.Min(8, players?.Get<int>("Count") ?? 0); i < count; i++)
        {
            var data = players!.Call("get_Item", i);
            if (data == null || data.Get<int>("characterType") == 2) continue;
            var property = data.Get("Property");
            state.Add(data.Get("cardContainer")?.Get<int>("CardCount") ?? -1);
            state.Add(property?.Field("gold")?.Get<int>("Value") ?? -1);
            state.Add(property?.Field("HP")?.Get<int>("Value") ?? -1);
            state.Add(property?.Field("level")?.Get<int>("Value") ?? -1);
            state.Add(data.Field("CharacterInst")?.Get("standLand")?.Get<int>("Id") ?? -1);
            state.Add(data.Get("buffContainer")?.Field("_buffDict")?.Get<int>("Count") ?? -1);
        }
        return state.ToHashCode();
    }

    private static int Asked(Server server, long id, int client, int hand, float now)
    {
        if (!Asks.TryGetValue(id, out var ask)) Asks[id] = ask = new();
        ask.Finish(now);
        // A skill use shows at once (user-reported 2026-11-17: the tab still lagged the player info window, which asks when it
        // opens): CardLogic.TriggerSkill sets the user's client count from the server's map, so a count that moved outside a
        // round start is right and newer than the stored answer. A round start moves every count too, but
        // BattleLogic.OnRoundStartS2CServerCallBack copies the local player's map onto every character and updates the round
        // in the same frame, so a count moving with the round is that copy and is not shown.
        if (ask.SeenRound != server.Round) ask.Fresh = -1;
        else if (ask.SeenClient >= 0 && ask.SeenClient != client) (ask.Fresh, ask.FreshAt) = (client, now);
        (ask.SeenRound, ask.SeenClient) = (server.Round, client);
        // Before the first answer the count is the game start's (Hero.SkillCds) or a skill use's, both right until a round starts.
        var shown = ask.Fresh >= 0 ? ask.Fresh : ask.Answered ? server.Battle.Call("GetPlayerDataById", id)?.Field<int>("SkillCD") ?? client : client;
        // One request at a time per player, when a round starts, the count moves (a skill use) or the hand count moves
        // (a card play), and the one follow-up after such a move; when the table changed, at most every Gap seconds and
        // only while the turns are not 0; only while the room is open and quiet (see Server and Settle). A round start asks
        // as soon as the room runs outside a fight: the server ends a game with GameFinishS2C instead of a next round, so a
        // request right after RoundStartS2C cannot meet the game's end, and waiting for a quiet room kept the new round's
        // turns off the tab while the bots played (user-reported 2026-11-17).
        var round = ask.Round != server.Round;
        var moved = round || ask.Client != client || ask.Hand != hand;
        var table = ask.Table != server.Table && shown != 0 && now - ask.At >= Gap;
        if (ask.Reply == null && (round && server.Running || (moved || table || now >= ask.Again) && server.Open))
        {
            (ask.Round, ask.Client, ask.Hand, ask.Table, ask.At) = (server.Round, client, hand, server.Table, now);
            ask.Again = moved ? now + FollowUp : float.PositiveInfinity;
            ask.Reply = server.Battle.Call("RequestGetHeroInfoC2S", id);
        }
        return shown;
    }

    private static Tab Create(RuntimeObject owner)
    {
        var tab = new Tab { Owner = owner };
        tab.Panel = NativeUi.Component(owner, Width(true, true, true), Height + Rise);
        try
        {
            tab.Panel.Set("opaque", true);
            // Behind the bar: its grey base is the bar's first layer.
            owner.Call("SetChildIndex", tab.Panel, owner.Call("GetChildIndex", owner.Call("GetChild", "n82")!)!.Value<int>());
            // The panel clips to its rectangle (straight edges): the outline's top line stays hidden under the bar. Only the
            // native clones sit under the rounded stencil mask, whose stepped edge lies under the outline (OutlineWidth / 2
            // inside); the outline itself is antialiased outside the mask (SmoothShape, 2026-11-01).
            tab.Panel.Call("SetupOverflow", 1);
            tab.Clip = NativeUi.Component(tab.Panel, Width(true, true, true), Height + Rise);
            tab.Clip.Set("touchable", false);
            tab.Mask = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            tab.Mask.Set("touchable", false);
            tab.Clip.Call("AddChild", tab.Mask);
            tab.Base = NativeUi.Create("Common_Internal", "灰底")!;
            tab.Strip = NativeUi.Create("Common_Internal", "PlayerInfo_Ani_Bottom")!;
            foreach (var (clone, source, extra) in new[] { (tab.Base, "n82", BaseExtra), (tab.Strip, "ani", 0f) })
            {
                var native = owner.Call("GetChild", source)!;
                clone.Set("touchable", false);
                clone.Call("SetSize", native.Get<float>("width") + extra, native.Get<float>("height"));
                clone.Call("SetScale", native.Get<float>("scaleX"), native.Get<float>("scaleY"));
                clone.Call("SetXY", native.Get<float>("x") - Left, native.Get<float>("y") - Top + Lower + Rise);
                tab.Clip.Call("AddChild", clone);
            }
            tab.Strip.Set("playing", false);
            tab.Outline = SmoothShape.Create(tab.Panel, 0f, -8f, Width(true, true, true), Height + Rise + 8, new Color(0, 0, 0, 0), 0f, 0f, Radius, Radius, OutlineWidth);
            // The count uses the coin count's number format; the role the nickname's (its font has Hangul, the coin
            // font does not, and its player colour was kept on the user's call, 2026-10-23); the turns the count's
            // number format, as the player info window draws them in the same SHOWG_TMP face.
            tab.Hand = NativeUi.Label(tab.Panel, "", 24, Rise, HandWidth - 24, Height, 24);
            tab.Role = NativeUi.Label(tab.Panel, "", 0, Rise, RoleWidth - Inset * 2, Height, 20); // Clear of the outline
            // From 2 past the stopwatch to 1 inside the tab's 6-wide right outline.
            tab.Skill = NativeUi.Label(tab.Panel, "", 0, Rise, SkillWidth - ClockSize - 9, Height, 24);
            foreach (var (label, source) in new[] { (tab.Hand, "txt_Gold"), (tab.Role, "txt_PlayerName"), (tab.Skill, "txt_Gold") })
            {
                NativeUi.CopyTextFormat(label, owner.Field(source)!);
                label.Set("align", 1); label.Set("verticalAlign", 1);
                label.Set("touchable", false);
            }
            // Option C (user-chosen 2026-11-01): the nickname's player colour lightened 60 % with a black outline, so the role
            // reads on its own colour's halftone ("카드 특화" in red vanished on the red strip). The nickname face (SIMHEI)
            // is a dynamic font with a one-pixel stroke, so the role takes the draft's bold face and 1.6-pixel stroke.
            var roleFormat = tab.Role.Get("textFormat")!;
            roleFormat.SetField("color", Color.Lerp(roleFormat.Field<Color>("color"), Color.white, RoleLighten));
            roleFormat.SetField("outlineColor", Color.black);
            roleFormat.SetField("bold", true);
            if (RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "FontManager"), "GetFont", roleFormat.Field("font")?.String() ?? "")?.TypeName == "DynamicFont")
                roleFormat.SetField("outline", RoleStroke);
            tab.Role.Set("textFormat", roleFormat);
            tab.Skill.Set("UBBEnabled", true); // The remaining turns are coloured inside the text, as the native window does.
            // Shrink: a long role ("카드 특화") or a two-digit count (issue #17: "10" wrapped and showed "1") fits its part instead of being cut.
            foreach (var label in new[] { tab.Hand, tab.Role, tab.Skill }) { label.Set("singleLine", true); label.Set("autoSize", 3); }
            tab.Card = NativeUi.Icon(tab.Panel, "Com_Icon_Card", 5, Rise + 7, Height - 14);
            // Optional art: without the stopwatch the turns still show.
            try { tab.Clock = NativeUi.Icon(tab.Panel, "时间", 0, Rise + (Height - ClockSize) / 2, ClockSize); }
            catch (TypeLoadException) { tab.Clock = null; }
            tab.Clip.Set("mask", tab.Mask.Get("displayObject")!);
            return tab;
        }
        catch { GameUi.Dispose(tab.Panel); throw; }
    }

    private static void Refresh(Tab tab, bool hand, bool role, int count, string text, (int Left, int Total)? cooldown)
    {
        var skill = cooldown != null;
        var (left, total) = cooldown ?? (-1, -1);
        var shown = (hand, role, skill, count, text, left, total, ModText.Korean);
        if (tab.Shown == shown) return;
        if (tab.Shown.Hand != hand || tab.Shown.HasRole != role || tab.Shown.HasSkill != skill)
        {
            var width = Width(hand, role, skill);
            tab.Panel.Call("SetSize", width, Height + Rise);
            tab.Panel.Call("SetXY", Left, Top - Rise);
            tab.Clip.Call("SetSize", width, Height + Rise);
            const float inset = OutlineWidth / 2;
            tab.Mask.Call("SetXY", inset, 0f);
            tab.Mask.Call("SetSize", width - 2 * inset, Height + Rise - inset);
            tab.Mask.Get("shape")!.Call("DrawRoundRect", 0f, Color.white, Color.white, 0f, 0f, Radius - inset, Radius - inset);
            // Sides and bottom; the top line runs under the bar's border, cut off by the panel's clip.
            tab.Outline.Call("SetSize", width, Height + Rise + 8);
            tab.Hand.Set("visible", hand);
            tab.Card?.Set("visible", hand);
            tab.Role.Set("visible", role);
            tab.Role.Call("SetXY", (hand ? HandWidth : 0f) + Inset, Rise);
            tab.Role.Call("SetSize", RolePart(skill) - Inset * 2, Height);
            // The skill part closes the tab: stopwatch, then the turns, clear of the right outline.
            var x = (hand ? HandWidth : 0f) + (role ? RolePart(skill) : 0f);
            tab.Skill.Set("visible", skill);
            tab.Skill.Call("SetXY", x + ClockSize + 2, Rise);
            tab.Clock?.Set("visible", skill);
            if (tab.Clock != null) NativeUi.Position(tab.Clock, x + 1, Rise + (Height - ClockSize) / 2, tab.Clock.Get<float>("scaleX"));
        }
        var number = count < 0 ? "—" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (tab.Hand.Get("text")?.String() != number) tab.Hand.Set("text", number);
        if (tab.Role.Get("text")?.String() != text) tab.Role.Set("text", text);
        var turns = skill ? string.Format(System.Globalization.CultureInfo.InvariantCulture, Turns, left, total) : "";
        if (tab.Skill.Get("text")?.String() != turns) tab.Skill.Set("text", turns);
        tab.Panel.Set("tooltips", string.Join("\n", new[] { hand ? ModText.Text("공개 손패 장수") : "", skill ? ModText.Text("스킬 쿨타임: 남은 턴/전체 턴") : "" }.Where(tip => tip.Length > 0)));
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
