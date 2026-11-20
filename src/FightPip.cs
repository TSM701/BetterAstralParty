using UnityEngine;

namespace BetterAstralParty;

// 전투 PIP / Fight PiP (code name FightPip, FightCctv until 2026.11.2-dev.44; user-requested 2026-11-16 as "전투 CCTV", named after picture-in-picture on
// 2026-11-17, a regular feature, default ON and starting full screen, since 2026-11-18; decisions and native facts in
// .claude/rules/fight-pip.md, drafts in artifacts/cc/cctv-fight). A fight the local player only watches (FightWindow playerState
// 2, the local player not the chain attacker) shows in a small window from the moment its window opens, while the field
// stays the main view and takes input outside it:
// - FightWindow is scaled onto the PIP's picture and clipped to it (rootContainer.clipRect), over a dark screen. While the
//   fight platform is open (BattleShowDirector switched the PK camera on and the field camera off) the PK camera renders
//   into a screen-sized RenderTexture shown under FightWindow's content, FairyGUI's stage camera leaves the PK camera's URP
//   stack (the field camera's stack draws the UI) and the field camera is on again.
// - The field HUD the game hides for a fight shows (BattleInfoPanel, its player bars, HandCardPanel), the hand dealt again
//   with its cards untouchable, its skill button taking clicks only in the ping mode and its move button none. What the game
//   last set on each panel is followed (readyFight writes 0 or 1, from the encounter step on), so full screen and every
//   exit give the HUD the game's own state.
// - A black ring frames it (thin frame, user-chosen 2026-11-17), with a names capsule (blinking live light) on its top edge
//   and an enlarge button on its bottom edge. A drag moves it, a corner sizes it in the screen's aspect, and the place is
//   kept in the settings file (전투 PIP 자리). A still click on it (only when it is what the pointer hits, not a window over
//   it) turns it into the game's own full-screen fight (window, cameras and HUD as the game has them) with a "PIP 모드"
//   capsule at the top left that brings it back, as Esc does there without opening the game's settings. Each match starts
//   in the view of 시작 화면 (전체 화면 by default), and a change carries to the next watched fights of that match only.
// - A fight that starts full screen before the player has shrunk one in this match waits (Session.Waiting): no field hold
//   and no library reopening until "PIP 모드" or Esc shrinks it (option 2, user-chosen 2026-11-18, so a default-ON feature
//   leaves full-screen fights to the game until it is used); Esc shrinks it from the first fight too (user-requested the
//   same day, after dev.42 left Esc to the game's settings while waiting).
// - While the toolbar's emoji or chat panel is open over it, it shrinks to half towards its bottom-left corner (draft 가-2);
//   with the map tool on it takes no clicks; battle chat bubbles over it fade to 30 % and take no clicks (option 3 of
//   anim/chat).
// - The mod's final-damage view (FinalDamageUi) moves into FightWindow, which carries it into the PIP (Holder).
// Motion: it pops out of its top-right corner, the picture turns on like a monitor, and once the platform closes the
// picture turns off and the frame drops away; the window then stays transparent until the game hides it.
// Every exit (the window hiding, the setting OFF, a role change, PvP, the breaker, a scene change) puts the stage camera back
// into the PK camera's stack at its index, clears the target, releases the texture, gives the field camera the game's state
// and returns the window (scale, place, clip, alpha), the HUD and the bubbles. Development builds add DevSolo.
internal static class FightPip
{
    internal const string Feature = "FightPip";
    private static readonly bool DevelopmentBuild = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    // Rim: the faint white line inside the black capsules' and the corner knob's edge.
    private static readonly Color Clear0 = new(0f, 0f, 0f, 0f), Rim = new(1f, 1f, 1f, .35f);
    private static readonly Color Red = new(181f / 255, 22f / 255, 59f / 255, 1f), Dark = new(14f / 255, 14f / 255, 18f / 255, 1f);
    private static IntPtr _logicClass, _sceneClass, _urpClass, _stageClass, _managerClass;
    private static Session? _session;
    private static Frame? _leaving;
    private static float _lastTick;
    // Full screen for the next watched fights: each match starts with 시작 화면 (_start, the setting last seen), and a click
    // or "PIP 모드"/Esc in the match changes it for the rest of that match (user-chosen 2026-11-16 and 2026-11-17). The match
    // is told by its room (_match, checked as each watched fight starts), since a surrender and a new match need not pass
    // the home, and the last match's choice carried into the next (user-reported 2026-11-17).
    private static bool _full, _start;
    // "PIP 모드" shrank a fight in this match: later fights that start full screen hold the field and Esc from the start.
    private static bool _shrunk;
    private static (long Id, long Created, IntPtr Room) _match;
    // The game library (도감) showing on the last tick read before a session (see ReopenLibrary), and the page it showed then.
    private static bool _libraryShown;
    private static LibraryView? _libraryPage;
    // BattleInfoPanel while full screen holds its Esc focus off (HoldFocus), until ReturnFocus gives it back.
    private static RuntimeObject? _focusPanel;
    private static readonly Dictionary<IntPtr, (RuntimeObject Item, float Alpha, bool Touchable)> Faded = new();

    private sealed record Context(RuntimeObject Logic, RuntimeObject? Scene, RuntimeObject? Director, RuntimeObject? Field,
        RuntimeObject? Pk, RuntimeObject? Stage);

    // FinalDamageUi's parent while a fight runs in the PiP: FightWindow's own coordinates are the root's when it is full.
    internal static RuntimeObject? Holder => _session is { } s && !s.Window.Get<bool>("isDisposed") ? s.Window : null;

    // HandLayoutUi keeps the field hand grouped through fight steps while the PiP shows the field (Cards deals it).
    internal static bool ShowsHand => _session is { Closing: false, Grow: < 1f };

    // The window showing the PK camera's texture full size at its own origin while the PIP has the camera, else null.
    internal static RuntimeObject? Picture => _session is { Redirected: true } s ? s.Window : null;
    // The small window shows the fight with the field in view (2026-11-20, user-reported: player indicators and Move Direction
    // Highlight vanished during a PIP fight): the field overlays that clear for a fight keep showing while this holds.
    internal static bool ShowsField => _session is { Waiting: false, Expanded: false, Closing: false };

    // FieldZoomUi lets the field have input while the PIP shows the fight: the keys (WASD) and the wheel wherever the pointer
    // rests, a press only outside the PIP (user-reported: WASD stopped over the PIP 2026-11-17, the wheel 2026-11-19).
    internal static bool FieldOpen(bool pointer = true)
    {
        if (_session is not { Expanded: false } s || s.Grow > 0f || s.Pressed) return false;
        if (!pointer) return true;
        try { return !NativeUi.Contains(s.Frame.Hit, NativeUi.Pointer()); }
        catch { return false; /* The next tick ends a session whose frame is gone. */ }
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { TickCore(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // PvP, breaker and scene cleanup: everything back as the game had it.
    internal static void Clear()
    {
        if (_session != null) End("cleanup");
        ReturnFocus(force: true);
        if (_leaving != null) { try { _leaving.Dispose(); } catch { } _leaving = null; }
        RestoreBubbles();
        RestoreLandTip();
        FightPipTutorial.Clear();
    }

    private static void TickCore()
    {
        ReturnFocus(force: GameUi.HomeAvailable);
        var now = Time.unscaledTime;
        var dt = Math.Min(.1f, Math.Max(0f, now - _lastTick));
        _lastTick = now;
        if (_leaving != null && !_leaving.Leave(now)) _leaving = null;
        var root = GameUi.Root;
        if (GameUi.HomeAvailable || _start != Plugin.FightPipStartFull.Value) _full = _start = Plugin.FightPipStartFull.Value;
        if (!Plugin.FightPip.Value || root == null || GameUi.HomeAvailable)
        {
            if (_session != null) End(root == null ? "no-root" : GameUi.HomeAvailable ? "home" : "off");
            FightPipTutorial.Tick(null);
            return;
        }
        var window = ShownFightWindow();
        var context = window == null && _session == null && !DevelopmentBuild ? null : Read();
        if (_session != null)
        {
            var reason = context == null ? "left-pve" : window == null || window.Pointer != _session.Window.Pointer ? "window"
                : (context.Director?.Pointer ?? IntPtr.Zero) != _session.Director ? "scene" : Watcher(window, context) ? null : "role";
            if (reason != null) End(reason, context == null ? null : PlatformOpen(context));
        }
        // No PIP without the scene's director: a surrender tears the scene down while the fight window still shows, and on
        // 2026.11.2-dev.28 a session started and ended there every frame for a second (218 times; the director's null pointer
        // never equalled the session's zero).
        if (_session == null)
        {
            var library = RuntimeObject.StaticField(_managerClass, "_inst")?.Field("_cachedNewGameLibraryWindow");
            var libraryShown = GameUi.Visible(library);
            if (libraryShown) _libraryPage = LibraryPage(library!);
            if (window != null && context?.Director != null && Watcher(window, context))
            {
                Start(root, window, context, now);
                if (_libraryShown && !libraryShown && library != null && _session is { Waiting: false }) ReopenLibrary(library);
            }
            _libraryShown = libraryShown;
        }
        _session?.Tick(root, context!, now, dt);
        if (_session?.Failed == true) End("camera");
        FightPipTutorial.Tick(_session?.Tutorial(root));
        if (DevelopmentBuild && context != null) DevSolo(context);
    }

    // The game's one fight window (UIManager._cachedFightWindow, read as a field: get_Fight would make it), seen the frame it
    // shows. The shared window walk (GameUi.Find) can be 0.1 s old, and on 2026.11.2-dev.20 the start of a watched fight
    // showed full screen until it caught up: a monster's challenge screen with the defender's large bust (user-reported).
    private static RuntimeObject? ShownFightWindow()
    {
        if (_managerClass == IntPtr.Zero) _managerClass = RuntimeObject.FindClass("UI", "UIManager");
        var window = RuntimeObject.StaticField(_managerClass, "_inst")?.Field("_cachedFightWindow");
        return GameUi.Visible(window) ? window : null;
    }

    // BattleShowDirector switched the PK camera on: the fight's stage shows.
    // While a blur holds the PK camera off (Session.Pause), FightLogic.fightStatus tells instead: ActiveBattlePlatform sets it
    // with the camera on, CloseBattlePlatform clears it with the camera off.
    private static bool PlatformOpen(Context context) => context.Pk != null && (new UnityEngine.Camera(context.Pk.Pointer).enabled
        || _session is { PkPaused: true } && context.Logic.Get("fight")?.Field<bool>("fightStatus") == true);

    // Full screen owns Esc. BattleInfoPanel.OnKeyDown opens the game's settings on Esc whenever its _FocusStatus is set,
    // fights included. FairyGUI hands keys out in OnGUI, and an Esc can reach it a frame before Input reports it to this
    // tick (dev.18 cleared the flag only on the frame Input saw the key, and the settings opened as well), so the flag stays
    // off for as long as the fight is full screen; the game setting it again (a window that took it hiding) is undone too.
    private static void HoldFocus(Context context)
    {
        var panel = context.Logic.Get("battle")?.Field("battleInfo");
        if (panel == null || !panel.Field<bool>("_FocusStatus")) return;
        panel.SetField("_FocusStatus", false);
        if (_focusPanel?.Pointer != panel.Pointer) Plugin.Diagnostics.State("fightPip.esc", "held");
        _focusPanel = panel;
    }

    // Once full screen ends, the HUD answers Esc again after the key that ended it is up, whatever window is on top then
    // (dev.18 waited for the fight window and kept it off for good when the settings had come up). shortcut: a window that
    // took the HUD's focus while full screen held it (a message box over the fight) gets it back early, so Esc under it
    // also opens the settings; only if such a window is seen over full-screen fights, keep holding while it shows.
    private static void ReturnFocus(bool force = false)
    {
        var panel = _focusPanel;
        if (panel == null || !force && _session?.HoldsEsc == true) return;
        if (!force && (UnityEngine.Input.GetKey(KeyCode.Escape) || UnityEngine.Input.GetKeyUp(KeyCode.Escape))) return;
        _focusPanel = null;
        try
        {
            if (!panel.Field<bool>("_FocusStatus")) panel.SetField("_FocusStatus", true);
            Plugin.Diagnostics.State("fightPip.esc", "returned");
        }
        catch (Exception ex) { Plugin.Diagnostics.State("fightPip.esc", "focus not returned: " + ex.GetType().Name); }
    }

    private static Context? Read()
    {
        if (_logicClass == IntPtr.Zero)
        {
            _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
            _urpClass = RuntimeObject.FindClass("UnityEngine.Rendering.Universal", "CameraExtensions");
            _stageClass = RuntimeObject.FindClass("FairyGUI", "StageCamera");
            _managerClass = RuntimeObject.FindClass("UI", "UIManager");
        }
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (logic == null || room == null || !VisibleCombatAdvisor.IsPve(room.Field("info")?.Get<int>("MapType") ?? 0)) return null;
        var scene = RuntimeObject.StaticField(_sceneClass, "inst");
        var director = scene?.Field("directorManager");
        return new Context(logic, scene, director, scene?.Get("mainCamera"), director?.Field("PKCamera")?.Field("PKMainCamera"),
            RuntimeObject.StaticField(_stageClass, "main"));
    }

    // Only a fight the local player watches: FightWindow's audience state, and not the chain attacker of this fight.
    private static bool Watcher(RuntimeObject window, Context context)
    {
        if (window.Get("contentPane")?.Field("playerState")?.Get<int>("selectedIndex") != 2) return false;
        var chain = context.Director?.Field("_chainAttacker");
        if (chain == null || chain.Get("gameObject")?.Get<bool>("activeInHierarchy") != true) return true;
        var local = context.Logic.Get("account")?.Call("GetPlayerID")?.Value<long>() ?? 0;
        return chain.Field("playerData")?.Field("player")?.Get<long>("Id") != local;
    }

    private static void Start(RuntimeObject root, RuntimeObject window, Context context, float now)
    {
        var match = Match(context);
        if (match != _match)
        {
            _match = match;
            _full = _start = Plugin.FightPipStartFull.Value;
            _shrunk = false;
            // A player who starts matches in the small window already knows the PiP: no first-time tutorial.
            if (!_start) FightPipTutorial.Known("start-small");
        }
        _session = new Session(root, window, context, _full, now) { Waiting = _full && !_shrunk };
        Plugin.Diagnostics.State("fightPip", _full ? "full by=match" + (_session.Waiting ? "; waiting" : "") : "cctv");
        // The field the PIP shows beside the fight is held by the owned free view (FieldZoomUi.HoldForFight), entered here,
        // before the battle platform opens, unless it already holds. A waiting session only reads what the hold needs.
        string hold;
        try { hold = FieldZoomUi.HoldForFight(out _session.Later, enter: !_session.Waiting); }
        catch (Exception ex) { hold = "failed:" + ex.GetType().Name; Plugin.Diagnostics.Error("FightPip.hold", ex); }
        _session.HeldField = hold == "entered";
        Plugin.Diagnostics.State("fightPip.camera", "hold " + hold);
    }

    // A watched fight leaves the game library (도감) open (가 of artifacts/cc/cctv-fight/v6-library, user-chosen 2026-11-17).
    // The game hides it as the fight window shows (FightWindow.OnShown: UIManager.CloseAllUnFightWin) and clears its page,
    // search and filters as it hides (OnHide: ClearBusinessData), so it opens again the way the game opens it in a match, on
    // the current map's page (TryShowAsync: ShowGameLibrary_Map), above the fight window and the PIP (sortingOrder 10001
    // against 1010) until the player closes it. Own fights start no session, so they still close it as the game does.
    // That page (user-reported 2026-11-18: it came back on 목록 whatever tab was open; the scroll asked for the same day) is
    // put back after TryShowAsync through the library's own handlers: the tab as the tab buttons change it (the tab
    // controller set without its event, then OnTabChanged: ExitSubpages, IsLegalTab, RefreshCurrentPage), the monster tab's
    // monster through RememberMonster, the exclusive/common switch of the 칩, 카드, 패널 and 이벤트 tabs through
    // OnMapGroupChanged as its switch does, the chosen chip as OnSelectRelic stores it (_selectedRelicId, which the list's
    // items read as they render), and the scroll of the tab's list and of the monster tab's description once their content
    // size is set (GList.CheckVirtualList runs the list's deferred refresh; ScrollPane.SetPosY calls EnsureBoundsCorrect and
    // clamps to the content). The description (UICom_LibraryFileContent, the one scrolling part of the monster tab: its
    // monster list barely scrolls) is what RenderMonster puts back to the top (percY 0), so dev.42, which kept only the
    // lists, logged `tab=1; scroll=0,0` (user-reported 2026-11-18). A shown, initialized window opens synchronously
    // (EnsureShownAsync awaits only an uninitialized one).
    private static void ReopenLibrary(RuntimeObject library)
    {
        try
        {
            library.Call("TryShowAsync");
            var page = _libraryPage;
            var main = page != null && library.Field<int>("_rootPage") == 6 ? MapMain(library) : null;
            if (page is { } view && main != null)
            {
                if (view.Tab > 0)
                {
                    if (view.Monster > 0) main.Call("RememberMonster", view.Monster);
                    main.Field("tab")!.Call("SetSelectedIndex", view.Tab);
                    main.Call("OnTabChanged");
                    if (view.Tab >= 2 && view.Common >= 0 && main.Field("com_TabMap")?.Field("Iscom")?.Get<int>("selectedIndex") != view.Common)
                        main.Call("OnMapGroupChanged", view.Tab - 2, view.Common == 1);
                }
                if (view.Relic > 0) main.Field("com_TabMap")?.SetField("_selectedRelicId", view.Relic);
                var list = TabList(main, view.Tab);
                list?.Call("CheckVirtualList");
                Scroll(list, view.X, view.Y);
                Scroll(TabDetail(main, view.Tab), 0f, view.DetailY);
            }
            Plugin.Diagnostics.State("fightPip.library", page is { } shown
                ? FormattableString.Invariant($"reopened tab={shown.Tab}; scroll={shown.X:F0},{shown.Y:F0}; detail={shown.DetailY:F0}")
                    + (main == null ? "; page not ready" : "")
                : "reopened");
        }
        catch (Exception ex) { Plugin.Diagnostics.State("fightPip.library", "failed: " + ex.GetType().Name); }
    }

    // The map detail a match's library shows (NewGameLibraryWindow._rootPage 6, MapDetail): its tab (0 목록, 1 몬스터, 2-5
    // 칩, 카드, 패널, 이벤트), the monster the monster tab remembers, the exclusive (0) or common (1) switch, the chip chosen
    // on the 칩 tab, the scroll of the tab's list and that of the monster tab's description.
    private readonly record struct LibraryView(int Tab, int Monster, int Common, int Relic, float X, float Y, float DetailY);

    private static LibraryView? LibraryPage(RuntimeObject library)
    {
        var main = library.Field<int>("_rootPage") == 6 ? MapMain(library) : null;
        if (main == null) return null;
        var tab = main.Field("tab")?.Get<int>("selectedIndex") ?? -1;
        var map = main.Field("com_TabMap");
        var pane = TabList(main, tab)?.Get("scrollPane");
        return new LibraryView(tab, main.Field<int>("currentMonsterId"), map?.Field("Iscom")?.Get<int>("selectedIndex") ?? -1,
            tab == 2 ? map?.Field<int>("_selectedRelicId") ?? 0 : 0, pane?.Get<float>("posX") ?? 0f, pane?.Get<float>("posY") ?? 0f,
            TabDetail(main, tab)?.Get("scrollPane")?.Get<float>("posY") ?? 0f);
    }

    // The monster tab's description (UICom_LibraryCharacterFile.com_LibraryFile, a scrolling UICom_LibraryFileContent: the
    // monster's mechanism, skills and story).
    private static RuntimeObject? TabDetail(RuntimeObject main, int tab) =>
        tab == 1 ? main.Field("com_TabMonster")?.Field("Com_LibraryFile")?.Field("com_LibraryFile") : null;

    private static void Scroll(RuntimeObject? owner, float x, float y)
    {
        if (owner?.Get("scrollPane") is not { } pane) return;
        pane.Set("posX", x);
        pane.Set("posY", y);
    }

    // Each tab's list: the overview's monsters, the monster list, and com_TabMap's chips (2), cards and events (3, 5) and
    // lands (4), as RefreshCategory fills them.
    private static RuntimeObject? TabList(RuntimeObject main, int tab) => tab switch
    {
        0 => main.Field("com_TabAll")?.Field("list_Items"),
        1 => main.Field("com_TabMonster")?.Field("list_Items"),
        2 => main.Field("com_TabMap")?.Field("list_Relics"),
        3 or 5 => main.Field("com_TabMap")?.Field("list_Cards"),
        4 => main.Field("com_TabMap")?.Field("list_Lands"),
        _ => null
    };

    private static RuntimeObject? MapMain(RuntimeObject library) => library.Get("contentPane")?.Field("com_Map")?.Field("Com_main");

    // The match: the server's room id and creation time (party.model.Room), or, where the room has no id (solo practice),
    // the room object itself.
    private static (long Id, long Created, IntPtr Room) Match(Context context)
    {
        var room = context.Logic.Get("room")?.Get("curRoomInfo");
        var info = room?.Field("info");
        var id = info?.Get<long>("Id") ?? 0;
        return id != 0 ? (id, info!.Get<long>("CreateTime"), IntPtr.Zero) : (0, 0, room?.Pointer ?? IntPtr.Zero);
    }

    // While a session lasts the owned free view keeps its override (FieldFreeCamera): no block, return or focus click ends
    // it, since the field camera's brain would follow the fight's cameras without it.
    internal static bool HoldsField => _session is { Waiting: false };

    // `platform`: whether the stage shows now, when this frame read it.
    private static void End(string reason, bool? platform = null)
    {
        var session = _session;
        if (session == null) return;
        _session = null;
        var failed = session.Restore(platform);
        RestoreBubbles();
        RestoreLandTip();
        // The free view this session entered ends with it, so the camera follows the game again.
        if (session.HeldField && FieldFreeCamera.Holding)
            try { FieldZoomUi.Clear(); }
            catch (Exception ex) { failed.Add("hold:" + ex.GetType().Name); }
        if (session.Closing && !session.Frame.Gone) { _leaving?.Dispose(); _leaving = session.Frame; }
        else session.Frame.Dispose();
        Plugin.Diagnostics.State("fightPip", $"off reason={reason}" + (failed.Count > 0 ? "; failed=" + string.Join(",", failed) : ""));
    }

    // UniversalAdditionalCameraData.cameraStack of a base camera (BattleShowDirector.UpdateFightConfig reads it the same way).
    private static RuntimeObject? Stack(RuntimeObject camera) =>
        RuntimeObject.StaticCall(_urpClass, "GetUniversalAdditionalCameraData", camera)?.Get("cameraStack");

    private static int IndexOf(RuntimeObject? list, RuntimeObject? item)
    {
        if (list == null || item == null) return -2;
        for (int i = 0, count = list.Get<int>("Count"); i < count; i++)
            if (list.CallPointer("get_Item", i) == item.Pointer) return i;
        return -1;
    }

    private static string Name(RuntimeObject? data)
    {
        var player = data?.Field("player");
        if (player == null) return "";
        if (data!.Get<int>("characterType") != 2) return player.Call("GetNick", false)?.String() ?? "";
        // Monsters: the character name, as FightWindow.RefreshDefendReadyChoice names them.
        var config = player.Field("characterConfig");
        return config == null ? "" : RuntimeObject.StaticCall(RuntimeObject.FindClass("", "CharacterHandle"), "GetCharacterName",
            config.Field<int>("Id"), config.Field<int>("CharacterType"))?.String() ?? "";
    }

    private static (float X, float Y, float W, float H) GlobalRect(RuntimeObject item)
    {
        // Display coordinates start at the rendered top-left (NativeUi.Contains reads them the same way).
        var display = item.Get("displayObject")!;
        var from = display.Call("LocalToGlobal", Vector2.zero)!.Value<Vector2>();
        var to = display.Call("LocalToGlobal", new Vector2(item.Get<float>("width"), item.Get<float>("height")))!.Value<Vector2>();
        return (Math.Min(from.x, to.x), Math.Min(from.y, to.y), Math.Abs(to.x - from.x), Math.Abs(to.y - from.y));
    }

    // The PIP steps aside only for the toolbar panel that covers it: the emoji (n20) or chat (n26) panel beside its list in
    // com_Interact_PC, or the list when the panel is not there, against its resting place, both in screen coordinates.
    private static bool UnderPanel(RuntimeObject toolbar, int style, RuntimeObject root, (float X, float Y, float W, float H) rest)
    {
        var list = toolbar.Field(style == 1 ? "_list_Expression" : "_list_Chat");
        if (list == null) return true;
        var panel = list.Get("parent")?.Call("GetChild", style == 1 ? "n20" : "n26") ?? list;
        var display = root.Get("displayObject")!;
        var from = display.Call("LocalToGlobal", new Vector2(rest.X, rest.Y))!.Value<Vector2>();
        var to = display.Call("LocalToGlobal", new Vector2(rest.X + rest.W, rest.Y + rest.H))!.Value<Vector2>();
        return FightPipLayout.Overlaps(GlobalRect(panel), (from.x, from.y, to.x - from.x, to.y - from.y));
    }

    // Option 3: chat bubbles over the PiP fade to 30 % and take no clicks; the rest, and all of them afterwards, as they were.
    private static void FadeBubbles(RuntimeObject root, RuntimeObject hit)
    {
        var list = GameUi.Find(root, "ExpressionListWindow", maxDepth: 1)?.Get("contentPane")?.Field("list_Plaform");
        var over = new HashSet<IntPtr>();
        if (list != null && GameUi.Visible(hit))
        {
            var area = GlobalRect(hit);
            for (int i = 0, count = list.Get<int>("numChildren"); i < count; i++)
            {
                var item = list.Call("GetChildAt", i);
                if (item == null || !item.Get<bool>("visible") || !FightPipLayout.Overlaps(GlobalRect(item), area)) continue;
                over.Add(item.Pointer);
                if (!Faded.ContainsKey(item.Pointer)) Faded[item.Pointer] = (item, item.Get<float>("alpha"), item.Get<bool>("touchable"));
                if (item.Get<float>("alpha") != FightPipLayout.BubbleAlpha) item.Set("alpha", FightPipLayout.BubbleAlpha);
                if (item.Get<bool>("touchable")) item.Set("touchable", false);
            }
        }
        foreach (var pointer in Faded.Keys.Where(key => !over.Contains(key)).ToList()) Unfade(pointer);
    }

    private static void RestoreBubbles()
    {
        foreach (var pointer in Faded.Keys.ToList()) Unfade(pointer);
    }

    // The land tip the field shows over a hovered land (BattleLandTipWindow) sat under the PIP (user-reported 2026-11-17): a
    // window's sortingOrder is its layer, set once in BaseWindow.OnInit, and FightWindow's is higher. While the small window
    // shows, the tip is put just above it (the frame's holder has the window's order), and back to its own order afterwards.
    private static RuntimeObject? _landTip;
    private static int _landTipOrder;

    private static void RaiseLandTip(RuntimeObject root, RuntimeObject window)
    {
        var tip = GameUi.Find(root, "BattleLandTipWindow", maxDepth: 1);
        if (tip == null || !GameUi.Visible(tip)) return;
        if (_landTip?.Pointer != tip.Pointer) { RestoreLandTip(); _landTip = tip; _landTipOrder = tip.Get<int>("sortingOrder"); }
        var above = window.Get<int>("sortingOrder") + 1;
        if (_landTipOrder < above && tip.Get<int>("sortingOrder") != above) tip.Set("sortingOrder", above);
    }

    private static void RestoreLandTip()
    {
        var tip = _landTip;
        _landTip = null;
        try
        {
            if (tip == null || tip.Get<bool>("isDisposed")) return;
            if (tip.Get<int>("sortingOrder") != _landTipOrder) tip.Set("sortingOrder", _landTipOrder);
        }
        catch { /* A disposed window has nothing to restore. */ }
    }

    // Pooled list items come back for later messages, so every faded one is put back, shown or not.
    private static void Unfade(IntPtr pointer)
    {
        var (item, alpha, touchable) = Faded[pointer];
        Faded.Remove(pointer);
        try
        {
            if (item.Get<bool>("isDisposed")) return;
            item.Set("alpha", alpha);
            item.Set("touchable", touchable);
        }
        catch { /* A disposed list item has nothing to restore. */ }
    }

    private sealed class Session
    {
        internal readonly RuntimeObject Window;
        internal readonly IntPtr Director;
        internal Frame Frame;
        internal bool Expanded, Closing, Failed, HoldsEsc;
        // Started full screen and not shrunk yet: the field, Esc and the library are the game's (see the class comment).
        internal bool Waiting;
        // What the field hold needs, read as the fight window opened, for a waiting session's first shrink.
        internal FieldZoomUi.FightHold? Later;
        // A press on the PIP: 1 a click so far, 2 a move, 3 a corner resize, 4 a press on "PIP 모드".
        internal bool Pressed => _mode != 0;
        internal bool Redirected => _redirected;
        internal bool PkPaused => _pkPaused;

        // What the first-time tutorial needs: this session as its token, the "PIP 모드" capsule and when the platform opened.
        internal FightPipTutorial.Fight Tutorial(RuntimeObject root) =>
            new(this, root, Window, _corner, Waiting, Expanded, Closing, _platformAt);
        internal float Grow;
        private readonly RuntimeObject _root;
        private readonly float _x, _y, _scaleX, _scaleY, _alpha;
        private float _started;
        private readonly RuntimeObject? _hand, _handPanel;
        private bool _dealt;
        // BattleInfoPanel.ui, its player bars, HandCardPanel.ui; the scale the game last set on each and the one this set.
        private readonly RuntimeObject?[] _panels;
        private readonly float[] _kept, _set;
        // The hand's card container, skill and move buttons (HandCardPanel.ui container_Card, com_Skill.btn_Skill, btn_Move); see Hud.
        private readonly Clickable? _cards, _skill, _move;
        private readonly string _names;
        private readonly RuntimeObject _screen;
        private RuntimeObject? _image, _native, _pkObject, _stage, _corner;
        private RenderTexture? _texture;
        private UnityEngine.Camera? _pk, _field;
        private int _stageIndex = -1, _mode, _dragCorner = -1;
        // Where it rests (shares of the screen) and where a move or resize started.
        private FightPipLayout.Spot _spot, _startSpot;
        private bool _redirected, _platformSeen, _platformOpen, _pkPaused;
        private float _aside, _closedAt = -1, _followAt, _platformAt = -1;
        // Whether this session entered the owned free view to hold the field (FieldZoomUi.HoldForFight); diagnostics state.
        internal bool HeldField;
        private int _followLogs, _blockedLogs;
        private IntPtr _following, _blockedBy;
        private Vector2 _pressedAt;

        internal Session(RuntimeObject root, RuntimeObject window, Context context, bool full, float now)
        {
            _root = root; Window = window; Director = context.Director?.Pointer ?? IntPtr.Zero;
            _x = window.Get<float>("x"); _y = window.Get<float>("y");
            _scaleX = window.Get<float>("scaleX"); _scaleY = window.Get<float>("scaleY"); _alpha = window.Get<float>("alpha");
            _started = now;
            _spot = FightPipLayout.Fit(FightPipLayout.Parse(Plugin.FightPipPlace.Value) ?? FightPipLayout.First,
                root.Get<float>("width"), root.Get<float>("height"));
            // Chosen earlier in this match: the game's own full screen from the start, as if popped and turned on long ago.
            if (full) { Expanded = true; Grow = 1f; _started = now - FightPipLayout.FadeSeconds; }
            var battle = context.Logic.Get("battle");
            var hud = battle?.Field("battleInfo")?.Get("ui");
            _handPanel = battle?.Field("handCard");
            _hand = _handPanel?.Get("ui");
            _panels = new[] { hud, hud?.Field("com_BattlePlayer"), _hand };
            _kept = _panels.Select(panel => panel?.Get<float>("scaleX") ?? 1f).ToArray();
            _set = _panels.Select(_ => float.NaN).ToArray();
            _cards = Clickable.Of(_hand?.Field("container_Card"));
            _skill = Clickable.Of(_hand?.Field("com_Skill")?.Field("btn_Skill"));
            _move = Clickable.Of(_hand?.Field("btn_Move"));
            _names = Name(window.Get("attackerData")) + " → " + Name(window.Get("defenderData"));
            // The dark screen the picture shows on before the platform opens; the texture goes above it.
            float width = root.Get<float>("width"), height = root.Get<float>("height");
            _screen = SmoothShape.Create(window, 0f, 0f, width, height, Dark, 0f, 0f, 0f, 0f);
            window.Call("SetChildIndex", _screen, 0);
            window.Get("rootContainer")!.Set("clipRect", new NullableRect(new Rect(0f, 0f, window.Get<float>("width"), window.Get<float>("height"))));
            Frame = new Frame(root, window.Get<int>("sortingOrder"), _names);
        }

        internal void Tick(RuntimeObject root, Context context, float now, float dt)
        {
            float rootWidth = root.Get<float>("width"), rootHeight = root.Get<float>("height");
            if (Frame.Outdated(rootWidth, rootHeight)) { Frame.Dispose(); Frame = new Frame(root, Window.Get<int>("sortingOrder"), _names); }
            _spot = FightPipLayout.Fit(_spot, rootWidth, rootHeight);
            var rest = FightPipLayout.Rect(_spot, rootWidth, rootHeight);
            var toolbar = GameUi.Find(root, "ExpressionWindow", maxDepth: 1);
            var style = toolbar?.Field("_interactStyle")?.Get<int>("selectedIndex") ?? 0;
            // The map tool's mark mode is on: ExpressionWindow.IsMapTool only names the kind of the last mark mode entered
            // (SwitchMapToolCursorType sets it, only OnShown/OnHide clear it), so it stayed true after the mode ended and the
            // PIP took no clicks or drags for the rest of the match (user-reported on dev.22 to dev.25, after the library or the
            // map explanation). Whether the mode is on is the scene's MarkManager.IsActive, which is absent in solo.
            var mapTool = toolbar?.Get<bool>("IsMapTool") == true && RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Scene",
                "BattleSceneController"), "inst")?.Field("MarkManager")?.Get<bool>("IsActive") == true;
            var platform = PlatformOpen(context);
            _platformOpen = platform;
            if (platform && !_platformSeen) _platformAt = now;
            if (platform) _platformSeen = true;
            // A platform opening again in the same window (another battle of this fight) brings the PiP back as at the start.
            if (platform && Closing)
            {
                Closing = false; _closedAt = -1;
                if (Grow <= 0f) _started = now;
                Plugin.Diagnostics.State("fightPip", "platform again");
            }
            if (_platformSeen && !platform && !Closing)
            {
                Closing = true; _closedAt = now;
                // CloseBattlePlatform's readyFight(false) set every panel to 1, maybe over the 1 this held.
                for (var i = 0; i < _kept.Length; i++) _kept[i] = 1f;
                Plugin.Diagnostics.State("fightPip", "closing");
            }
            Clicks(mapTool);
            Grow = FightPipLayout.Step(Grow, Expanded ? 1f : 0f, dt, FightPipLayout.GrowSeconds);
            var aside = style is 1 or 2 && !Expanded && !Closing && UnderPanel(toolbar!, style, root, rest);
            _aside = FightPipLayout.Step(_aside, aside ? 1f : 0f, dt, FightPipLayout.AsideSeconds);
            Cameras(context, platform && !Closing && Grow < 1f);
            Place(rootWidth, rootHeight, now, mapTool, rest);
            // Cards first: dealing them sets the container as readyFight(true) had it, and Hud then holds it.
            if (!Closing)
            {
                if (Grow < 1f) Cards();
                Hud(Grow < 1f, context.Scene?.Field("MarkManager")?.Get<bool>("IsActive") == true, NoOwnAction(context.Logic));
            }
            else { _cards?.Hold(null); _skill?.Hold(null); _move?.Hold(null); }
            if (Grow < 1f && !Closing) { FadeBubbles(root, Frame.Hit); RaiseLandTip(root, Window); }
            else { RestoreBubbles(); RestoreLandTip(); }
            HoldsEsc = Expanded && !Closing;
            if (HoldsEsc) HoldFocus(context);
        }

        // A still click on the PIP gives full screen, a press that moves more than DragStart moves it, a press on a corner
        // square sizes it from that corner, and a click on "PIP 모드" brings it back; a move or resize is saved on release.
        private void Clicks(bool mapTool)
        {
            if (Closing) { if (_mode is 2 or 3) Save(); _mode = 0; Knob = -1; Hover = false; return; }
            if (Expanded && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                if (EscapeFree()) { Small(); return; }
                Plugin.Diagnostics.State("fightPip.esc", "left to " + (ModUi.IsOpen ? "mod settings" : GameUi.FocusWindow()?.TypeName ?? "none"));
            }
            // In root units, as the place is: the stage's touch position is in screen pixels.
            var point = _root.Get("displayObject")!.Call("GlobalToLocal", NativeUi.Pointer())!.Value<Vector2>();
            // What the pointer hits, so a window over the PIP (a player's info, the settings) keeps its own clicks.
            var path = GameUi.PointerPath(refresh: true);
            var small = !Expanded && Grow <= 0f && !mapTool;
            var over = small && Over(path, Frame.Hit);
            var cornerAt = small ? Frame.CornerIn(path) : -1;
            var corner = Expanded && Grow >= 1f && Over(path, _corner);
            Knob = _mode == 3 ? _dragCorner : _mode == 0 ? cornerAt : -1;
            Hover = small && (over || cornerAt >= 0) || _mode is 2 or 3;
            if (UnityEngine.Input.GetMouseButtonDown(0) && !ModUi.IsOpen && !Expanded && Grow <= 0f && !over && cornerAt < 0) Blocked(path, mapTool);
            if (UnityEngine.Input.GetMouseButtonDown(0) && !ModUi.IsOpen && (over || cornerAt >= 0 || corner))
            {
                _mode = cornerAt >= 0 ? 3 : over ? 1 : 4;
                _dragCorner = cornerAt;
                _pressedAt = point;
                _startSpot = _spot;
            }
            if (_mode == 0) return;
            float width = _root.Get<float>("width"), height = _root.Get<float>("height");
            if (_mode == 1 && (Math.Abs(point.x - _pressedAt.x) > FightPipLayout.DragStart || Math.Abs(point.y - _pressedAt.y) > FightPipLayout.DragStart))
                _mode = 2;
            if (_mode == 2) _spot = FightPipLayout.Moved(_startSpot, point.x - _pressedAt.x, point.y - _pressedAt.y, width, height);
            if (_mode == 3) _spot = FightPipLayout.Resized(_startSpot, _dragCorner, point.x, point.y, width, height);
            if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                // FairyGUI's click rule for "PIP 모드": the press may stray up to 50 pixels on an axis.
                var still = Math.Abs(point.x - _pressedAt.x) <= 50f && Math.Abs(point.y - _pressedAt.y) <= 50f;
                if (_mode == 1 && over || _mode == 4 && corner && still)
                {
                    Expanded = _full = _mode == 1;
                    Plugin.Diagnostics.State("fightPip", Expanded ? "full" : "cctv");
                    if (!Expanded && Waiting) Wake();
                    if (!Expanded) FightPipTutorial.Shrunk("pip");
                }
                if (_mode is 2 or 3) Save();
                _mode = 0;
            }
            else if (!UnityEngine.Input.GetMouseButton(0)) { if (_mode is 2 or 3) Save(); _mode = 0; }
        }

        internal int Knob { get; private set; } = -1;
        // The pointer over the small PIP or one of its corners, or a move or resize going on: every corner shows its bracket.
        internal bool Hover { get; private set; }

        // The place goes into the settings file (전투 PIP 자리) when a move or resize ends.
        internal void Save()
        {
            var text = FightPipLayout.Format(_spot);
            if (Plugin.FightPipPlace.Value != text) Plugin.FightPipPlace.Value = text;
            Plugin.Diagnostics.State("fightPip.place", text);
        }

        private static bool Over(IReadOnlyList<RuntimeObject> path, RuntimeObject? part) =>
            part != null && path.Any(item => item.Pointer == part.Pointer);

        // Esc is the fight's own only while the fight window holds the keys: not under another window, the mod's settings,
        // the update notice or a card popup, each of which closes on it.
        private bool EscapeFree() =>
            !ModUi.IsOpen && !UpdateNotificationUi.ConsumedInput && !PingDetailsUi.Showing && GameUi.FocusWindow()?.Pointer == Window.Pointer;

        // The first shrink of a waiting session ("PIP 모드" or Esc): the field hold from what the fight window's opening left,
        // before this tick turns the field camera on (the brain applies the override in its LateUpdate). From here on the
        // later sessions of this match hold the field from their start.
        private void Wake()
        {
            Waiting = false;
            _shrunk = true;
            string hold;
            try { hold = FieldZoomUi.HoldLater(Later); }
            catch (Exception ex) { hold = "failed:" + ex.GetType().Name; Plugin.Diagnostics.Error("FightPip.hold", ex); }
            Later = null;
            HeldField = hold == "entered";
            Plugin.Diagnostics.State("fightPip.camera", "hold late " + hold);
        }

        // "PIP 모드" by Esc; the HUD's Esc focus, held while full screen, comes back once the key is up (ReturnFocus).
        private void Small()
        {
            Expanded = _full = false;
            Plugin.Diagnostics.State("fightPip", "cctv by=esc");
            if (Waiting) Wake();
            FightPipTutorial.Shrunk("esc");
        }

        private void Cameras(Context context, bool redirect)
        {
            if (Failed || context.Pk == null || context.Field == null || context.Stage == null) return;
            if (redirect && !_redirected) Redirect(context);
            else if (!redirect && _redirected) Unredirect();
            if (!_redirected) return;
            if (!_field!.enabled) _field.enabled = true;
            Pause(BlurRequested());
            Follow();
            if (_texture != null && (_texture.width != Screen.width || _texture.height != Screen.height)) NewTexture();
        }

        private void Redirect(Context context)
        {
            var stack = Stack(context.Pk!);
            var index = IndexOf(stack, context.Stage);
            // Without it in the stack the picture cannot be moved safely: the session ends and the game keeps its full screen.
            if (index < 0) { Failed = true; Plugin.Diagnostics.State("fightPip.camera", "stage camera not in the PK stack"); return; }
            _pkObject = context.Pk; _stage = context.Stage; _stageIndex = index;
            _pk = new UnityEngine.Camera(context.Pk!.Pointer); _field = new UnityEngine.Camera(context.Field!.Pointer);
            if (_image == null)
            {
                _image = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GImage"));
                _image.Set("touchable", false);
                Window.Call("AddChildAt", _image, 1);
            }
            NewTexture();
            _image.Set("visible", true);
            stack!.Call("RemoveAt", index);
            _pk.targetTexture = _texture;
            _field.enabled = true;
            _redirected = true;
            Plugin.Diagnostics.State("fightPip.camera", "redirect");
        }

        private void NewTexture()
        {
            var old = _texture;
            var oldNative = _native;
            _texture = new RenderTexture(Screen.width, Screen.height, 24) { hideFlags = HideFlags.HideAndDontSave };
            _texture.Create();
            // NTexture(Texture, Texture alphaTexture, float xScale, float yScale), as HomePortrait and SmoothShape build theirs.
            _native = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "NTexture"), new RuntimeObject(_texture.Pointer), null!, 1f, 1f);
            SmoothShape.Watch(_native);
            _image!.Set("texture", _native);
            _image.Call("SetSize", _root.Get<float>("width"), _root.Get<float>("height"));
            if (_pk != null && _redirected) _pk.targetTexture = _texture;
            if (oldNative != null) SmoothShape.Unwatch(oldNative);
            if (old != null) { old.Release(); UnityEngine.Object.Destroy(old); }
        }

        // The game's camera state: the stage camera back at its index, the PK camera on the screen, and the field camera off
        // while the platform is open (the game turned it on when the platform closed).
        // The in-battle settings window (SettingInBattleWindow, the field's Esc menu) blurs the Main cameras' picture every frame
        // for its background (BlurBgTextureController.CreateDynmicBlurTex: GraphicEventManager.CreateBlurRTRequest, drawn by the
        // CaptureBlur renderer feature). In a fight the game has only the PK camera on; with the field camera on as well both
        // wrote that blur in turn, and under the menu the small window's fight kept flashing up full size over the field
        // (user-reported 2026-11-18). While such a request stands the PK camera pauses, so the blur shows the field the PIP
        // showed and the small window keeps its last picture under the menu; it resumes when the request goes, or here at
        // Unredirect while the platform is still open (the game turns it off itself when the platform closes).
        private void Pause(bool pause)
        {
            if (_pk is null || _pk.m_CachedPtr == IntPtr.Zero) return;
            if (pause)
            {
                // Held off every frame, since the game turns it on again for another battle of the fight; a camera the game
                // turned off itself is not this pause's to bring back.
                if (!_pk.enabled) return;
                _pk.enabled = false;
                if (!_pkPaused) { _pkPaused = true; Plugin.Diagnostics.State("fightPip.camera", "pk paused for a blur"); }
                return;
            }
            if (!_pkPaused) return;
            _pkPaused = false;
            if (_platformOpen) _pk.enabled = true;
            Plugin.Diagnostics.State("fightPip.camera", "pk resumed" + (_platformOpen ? "" : "; platform closed"));
        }

        private static IntPtr _graphicsClass;
        private static bool _blurUnreadable;

        // Whether a dynamic blur of a game camera is asked for (GraphicEventManager.captureBlurRTReqDic, lists by camera flag).
        // One-time captures (captureBlurCallbackDic: the player info, the map explanation, solo practice's menu) are left
        // alone: one asked after this tick is drawn before the next, and pausing for one could hold the PK camera off for good
        // if only it draws the blur. A game change here only gives up the pause, never the PIP: it logs once and reads no more.
        private static bool BlurRequested()
        {
            if (_blurUnreadable) return false;
            try
            {
                if (_graphicsClass == IntPtr.Zero) _graphicsClass = RuntimeObject.FindClass("", "GraphicEventManager");
                if (_graphicsClass == IntPtr.Zero) throw new TypeLoadException("GraphicEventManager");
                return Listed(RuntimeObject.StaticField(_graphicsClass, "instance")?.Field("captureBlurRTReqDic"));
            }
            catch (Exception ex)
            {
                _blurUnreadable = true;
                Plugin.Diagnostics.State("fightPip.camera", "blur requests unreadable: " + ex.GetType().Name);
                return false;
            }
        }

        // Any list in a dictionary of lists that holds something.
        private static bool Listed(RuntimeObject? lists)
        {
            if (lists == null || lists.Get<int>("Count") == 0) return false;
            var values = lists.Get("Values")!.Call("GetEnumerator")!;
            try
            {
                while (values.Call("MoveNext")!.Value<bool>())
                    if ((values.Get("Current")?.Get<int>("Count") ?? 0) > 0) return true;
            }
            finally { values.Call("Dispose"); }
            return false;
        }

        private void Unredirect()
        {
            Pause(false);
            _redirected = false;
            var stack = Stack(_pkObject!);
            if (stack != null && IndexOf(stack, _stage) < 0) stack.Call("Insert", Math.Min(_stageIndex, stack.Get<int>("Count")), _stage);
            if (_pk!.targetTexture?.Pointer == _texture?.Pointer) _pk.targetTexture = null;
            if (_pk.enabled) _field!.enabled = false;
            // Full screen: the window shows the game's own picture. Closing: the last frame stays under the turning-off cover.
            if (Expanded) _image?.Set("visible", false);
            Plugin.Diagnostics.State("fightPip.camera", $"restore ui={IndexOf(stack, _stage)}; field={_field!.enabled}; pk={_pk.enabled}");
        }

        // Diagnostics: the virtual camera the field camera follows while the PIP shows the field, when it changes.
        private void Follow()
        {
            if (!Plugin.Diagnostics.IsRecording || _followLogs >= 12 || Time.unscaledTime < _followAt) return;
            _followAt = Time.unscaledTime + .25f;
            try
            {
                var core = RuntimeObject.StaticCall(RuntimeObject.FindClass("Cinemachine", "CinemachineCore"), "get_Instance");
                for (int i = 0, count = core?.Get<int>("BrainCount") ?? 0; i < count; i++)
                {
                    var brain = core!.Call("GetActiveBrain", i);
                    if (brain?.Get("OutputCamera")?.Pointer != _field!.Pointer) continue;
                    var vcam = brain.Get("ActiveVirtualCamera");
                    if ((vcam?.Pointer ?? IntPtr.Zero) == _following) return;
                    _following = vcam?.Pointer ?? IntPtr.Zero;
                    _followLogs++;
                    Plugin.Diagnostics.State("fightPip.field", vcam == null ? "follows none"
                        : $"follows {vcam.Get("Name")?.String()}; layer={vcam.Get("VirtualCameraGameObject")?.Get<int>("layer")}; priority={vcam.Get<int>("Priority")}");
                    return;
                }
            }
            catch (Exception ex) { _followLogs = 12; Plugin.Diagnostics.State("fightPip.field", "unreadable: " + ex.GetType().Name); }
        }

        // Diagnostics: a press inside the PIP that something else took (user-reported 2026-11-17: after the game library the PIP
        // took no clicks or drags), with where that sits among the root's children, a few times a session.
        private void Blocked(IReadOnlyList<RuntimeObject> path, bool mapTool)
        {
            if (!Plugin.Diagnostics.IsRecording || _blockedLogs >= 6 || !NativeUi.Contains(Frame.Hit, NativeUi.Pointer())) return;
            RuntimeObject? owner = null;
            foreach (var item in path)
                if (item.Get("parent")?.Pointer == _root.Pointer) { owner = item; break; }
            if ((owner?.Pointer ?? IntPtr.Zero) == _blockedBy && !mapTool) return;
            _blockedBy = owner?.Pointer ?? IntPtr.Zero;
            _blockedLogs++;
            int At(RuntimeObject? item) => item == null ? -1 : _root.Call("GetChildIndex", item)?.Value<int>() ?? -1;
            Plugin.Diagnostics.State("fightPip.blocked", $"by={owner?.TypeName ?? "none"}; hit={(path.Count > 0 ? path[0].TypeName : "none")}; "
                + $"order={owner?.Get<int>("sortingOrder")}; at={At(owner)}; holder={At(Frame.Holder)}/{Frame.Holder.Get<int>("sortingOrder")}; "
                + $"window={At(Window)}/{Window.Get<int>("sortingOrder")}; mapTool={mapTool}; hitTouchable={Frame.Hit.Get<bool>("touchable")}; "
                + $"holderVisible={Frame.Holder.Get<bool>("visible")}");
        }

        private void Place(float rootWidth, float rootHeight, float now, bool mapTool, (float X, float Y, float W, float H) rest)
        {
            var (px, py, pw, ph) = rest;
            var pad = FightPipLayout.Pad;
            (float X, float Y, float W, float H) rect = (px, py, pw, ph);
            static (float, float, float, float) About((float X, float Y, float W, float H) r, float scale, float ax, float ay) =>
                (ax + (r.X - ax) * scale, ay + (r.Y - ay) * scale, r.W * scale, r.H * scale);
            rect = About(rect, FightPipLayout.Aside(_aside), px - pad, py + ph + pad);
            // A quick fade in and out, nothing else moving (user-chosen 2026-11-17). No hover lift or press dip (removed the
            // same day, user-requested): they moved the corners under the pointer. The exit plays only from the PIP; a
            // full-screen fight ends as the game ends it.
            var fadeIn = FightPipLayout.Fade(now - _started);
            var fadeOut = _closedAt < 0 || Grow > 0f ? 0f : FightPipLayout.Fade(now - _closedAt);
            var shown = fadeIn * (1f - fadeOut);
            var grow = FightPipLayout.InOut(Grow);
            rect = (FightPipLayout.Lerp(rect.X, 0f, grow), FightPipLayout.Lerp(rect.Y, 0f, grow),
                FightPipLayout.Lerp(rect.W, rootWidth, grow), FightPipLayout.Lerp(rect.H, rootHeight, grow));
            var scale = rect.W / rootWidth;
            Window.Call("SetScale", scale, scale);
            Window.Call("SetXY", rect.X, rect.Y);
            // Its 불투명도 slider (20-100 %, user-requested 2026-11-17) for the fight inside the small window only, back to the
            // game's 100 % as it grows to full screen; the frame, its capsules and the turning-on cover stay solid (on dev.21 the
            // slider faded them too, user-reported). After the fade out the window stays transparent until the game hides it.
            var opacity = FightPipLayout.Lerp(Math.Clamp(Plugin.FightPipOpacity.Value, FightPipLayout.LeastOpacity, 1f), 1f, grow);
            var alpha = shown * opacity;
            if (Window.Get<float>("alpha") != alpha) Window.Set("alpha", alpha);
            var full = Grow >= 1f;
            if (full) _image?.Set("visible", false);
            else if (_redirected || Closing) _image?.Set("visible", _image != null && _texture != null);
            // The camera's picture as the screen shows it: its colours whole, whatever alpha the stage's sprites leave in the
            // texture (BlendMode.Off). It blends only while the window is see-through (fading, 불투명도), with nothing of the
            // PIP's own under it then. The texture fills the window exactly, so the clip has nothing to cut.
            var picture = _image != null && _image.Get<bool>("visible");
            var solid = alpha >= .999f;
            if (_image != null)
            {
                var blend = solid ? 8 : 0; // FairyGUI.BlendMode.Off, Normal
                if (_image.Get<int>("blendMode") != blend) _image.Set("blendMode", blend);
            }
            _screen.Set("visible", !full && !picture);
            Frame.Layout(rest);
            Frame.Show(rect.X, rect.Y, rect.W / pw, (1f - FightPipLayout.Clamp(grow * 2.5f)) * shown,
                1f - FightPipLayout.InOut(_aside), !mapTool && !Closing && Grow <= 0f, Knob, Hover,
                FightPipLayout.Live(now - _started), Window);
            Corner(full);
        }

        // "PIP 모드" at the top left of the full-screen fight.
        private void Corner(bool full)
        {
            if (full && _corner == null) _corner = Frame.Corner(_root, Window.Get<int>("sortingOrder"));
            if (_corner == null) return;
            _corner.Set("visible", full);
            _corner.Set("touchable", full);
            Frame.Above(_root, _corner, Window);
        }

        // The field HUD the game hides for a fight shows in the PiP; full screen gives it back to the game's fight state.
        // The hand panel keeps the touchability the game gave it, so the grouped hand's piles open and the skill takes the
        // ping (MarkDetectionSystem.DetectUI walks up from GRoot.touchTarget), which the whole panel made untouchable stopped
        // (user-reported 2026-11-18). The card container takes input while no action of the local player can be open
        // (NoOwnAction), as on other players' turns, where RefreshUsableCards leaves it touchable: native hover, drag and
        // the card ping (UIHandCard_Button_Card is an IMarkTarget) work in both layouts (user-reported 2026-11-19: neither
        // took input or pings). Otherwise it stays untouchable, as readyFight(true) left it. The skill button takes clicks
        // only while the ping (mark) mode is on, whose release ExitMarkMode consumes before FairyGUI's click, so
        // ReleaseActiveSkill returns (MarkInputConsume.IsConsumed); the move button takes none.
        private void Hud(bool shown, bool marking, bool idle)
        {
            for (var i = 0; i < _panels.Length; i++)
            {
                var panel = _panels[i];
                if (panel == null || panel.Get<bool>("isDisposed")) continue;
                var game = Mirror(i);
                var scale = shown ? 1f : game;
                if (panel.Get<float>("scaleX") != scale) panel.Call("SetScale", scale, scale);
                _set[i] = scale;
            }
            _cards?.Hold(shown ? idle : null);
            _skill?.Hold(shown ? marking : null);
            _move?.Hold(shown ? false : null);
        }

        // No action of the local player can be open: none is (ActionLogic.CardSN 0; a drop or quick use reads it first), or
        // it is another player's turn. The second is needed because CardSN keeps the last card turn's serial: only a cancel
        // (OnCancelUseEffectC2S), a skill (Skill.SkillReleaseAction) and the match's end (Dispose) clear it, not the dice,
        // so CardSN alone (2026.11.3-dev.5) would leave the cards untouchable after one's own card turn.
        private static bool NoOwnAction(RuntimeObject logic)
        {
            if (logic.Get("action")?.Field<long>("CardSN") == 0) return true;
            var self = logic.Get("account")?.Call("GetPlayerID")?.Value<long>();
            var turn = logic.Get("battle")?.Get<long>("curPlayerId");
            return self is > 0 && turn is long current && current != self;
        }

        // One object's touchable: held to a value or given back (null). A value other than the one this set is the game's.
        private sealed class Clickable
        {
            private readonly RuntimeObject _item;
            private bool _kept;
            private bool? _set;
            private Clickable(RuntimeObject item) { _item = item; _kept = item.Get<bool>("touchable"); }
            internal static Clickable? Of(RuntimeObject? item) => item == null ? null : new Clickable(item);

            internal void Hold(bool? value)
            {
                if (_item.Get<bool>("isDisposed")) return;
                var current = _item.Get<bool>("touchable");
                if (_set != current) _kept = current;
                var want = value ?? _kept;
                if (current != want) _item.Set("touchable", want);
                _set = value == null ? null : want;
            }
        }

        // The field's cards. Every fight sends them back to the pool as it starts (FightLogic's recycleCard, watched fights
        // included) and CloseBattlePlatform deals them again (resetCardList), so the PiP's field showed an empty hand. While
        // it shows the field they are dealt from the hand's data (HandCardPanel.UpdateCardList, the part of ResetCardList
        // that builds the cards; the rest re-arms the skill and move buttons) and left untouchable as readyFight(true) left
        // container_Card, which UpdateCardList makes touchable at its end. Once per empty hand, so an empty hand stays empty.
        private void Cards()
        {
            if (_handPanel == null || _hand == null || _hand.Get<bool>("isDisposed")) return;
            if (_handPanel.Field("cardItemList")?.Get<int>("Count") is not 0) { _dealt = false; return; }
            if (_dealt) return;
            _dealt = true;
            _handPanel.Call("UpdateCardList");
            _hand.Field("container_Card")?.Set("touchable", false);
            Plugin.Diagnostics.State("fightPip.hand", "dealt " + (_handPanel.Field("cardItemList")?.Get<int>("Count") ?? 0));
        }

        // The game's own scale for panel i. readyFight(true) writes 0 when the fight is set up, which a session started at the
        // encounter step (a monster's attack) sees after it began; a value other than the one this last set is the game's.
        private float Mirror(int i)
        {
            var current = _panels[i]!.Get<float>("scaleX");
            if (!float.IsNaN(_set[i]) && current != _set[i]) _kept[i] = current;
            return _kept[i];
        }

        internal List<string> Restore(bool? platform)
        {
            var failed = new List<string>();
            void Step(string name, Action action)
            {
                try { action(); }
                catch (Exception ex) { failed.Add(name + ":" + ex.GetType().Name); }
            }
            Step("place", () => { if (_mode is 2 or 3) Save(); });
            Step("camera", () => { if (_redirected) Unredirect(); });
            Step("window", () =>
            {
                if (Window.Get<bool>("isDisposed")) return;
                Window.Call("SetScale", _scaleX, _scaleY);
                Window.Call("SetXY", _x, _y);
                Window.Set("alpha", _alpha);
                Window.Get("rootContainer")!.Set("clipRect", default(NullableRect));
            });
            Step("image", () => { GameUi.Dispose(_image); GameUi.Dispose(_screen); if (_native != null) SmoothShape.Unwatch(_native); });
            Step("texture", () => { if (_texture != null) { _texture.Release(); UnityEngine.Object.Destroy(_texture); } });
            // While the game still shows the fight its HUD gets what the game last set; once the stage has closed (also when this
            // frame saw it close first) readyFight(false) has shown it, as this kept it.
            var closed = Closing || _platformSeen && !(platform ?? _platformOpen);
            var fighting = !closed && !Window.Get<bool>("isDisposed") && GameUi.Visible(Window);
            Step("hud", () =>
            {
                for (var i = 0; i < _panels.Length; i++)
                {
                    var panel = _panels[i];
                    if (!fighting || panel == null || panel.Get<bool>("isDisposed")) continue;
                    var kept = Mirror(i);
                    panel.Call("SetScale", kept, kept);
                }
                _cards?.Hold(null); _skill?.Hold(null); _move?.Hold(null);
            });
            Step("corner", () => GameUi.Dispose(_corner));
            return failed;
        }
    }

    // The PIP's own parts in root units. They are laid out for its resting place (Layout, every frame: SmoothShape keeps its
    // corners and ring at any size) and moved as a whole for the motion. A holder above FightWindow (same sortingOrder, added
    // after it) carries the black ring, the capsules, the click area and a hit square on each corner
    // for the resize (thin frame, draft 가 of v3-quiet, user-chosen 2026-11-17). Its handles are black (v5-handles,
    // user-chosen 2026-11-17, after the cream corner dot did not say it resizes and the round enlarge button read like a
    // screenshot icon): while the pointer is over it every corner shows a black L, the corner under the pointer a black disc
    // with a two-headed diagonal arrow (corner draft 다 in black), and the bottom edge a black "전체 화면" capsule with a mouse
    // (다-1). The names capsule with its live light is black too (user-requested the same day, for one look). Its soft shadow under the window is gone:
    // FairyGUI never moves a child with a sortingOrder by index, so the shade sat over the picture and darkened it on
    // dev.19-21 (user-reported).
    private sealed class Frame
    {
        internal readonly RuntimeObject Holder, Hit;
        private readonly RuntimeObject _ring, _capsules, _header, _short, _button;
        private readonly RuntimeObject[] _corners = new RuntimeObject[4], _knobs = new RuntimeObject[4], _brackets = new RuntimeObject[4];
        // The live light of the names capsule and of the short one, blinking like a recording light.
        private readonly List<RuntimeObject> _dots = new();
        private bool _lit = true;
        private readonly float _rootWidth, _rootHeight, _headerWidth, _buttonWidth;
        private readonly int _language;
        private (float X, float Y, float W, float H) _rest = (-1f, -1f, -1f, -1f);
        private float _leftAt = -1, _leftAlpha = 1f;
        internal bool Gone;
        private const float HeaderHeight = 26f, ButtonHeight = 30f, TextSize = 14f;

        internal Frame(RuntimeObject root, int sortingOrder, string names)
        {
            _rootWidth = root.Get<float>("width"); _rootHeight = root.Get<float>("height"); _language = ModText.Revision;
            var outer = FightPipLayout.OuterRadius;
            Holder = NativeUi.Component(root, _rootWidth, _rootHeight);
            Holder.Set("sortingOrder", sortingOrder);
            Holder.Set("visible", false);
            // The ring's inner radius (7) is small enough for its band to cover the picture's square corners.
            _ring = SmoothShape.Create(Holder, 0f, 0f, 10f, 10f, Clear0, outer, outer, outer, outer, FightPipLayout.Ring, Color.black);
            Hit = NativeUi.Component(Holder, 10f, 10f);
            Hit.Set("opaque", true);
            for (var i = 0; i < 4; i++)
            {
                _corners[i] = NativeUi.Component(Holder, FightPipLayout.CornerSize, FightPipLayout.CornerSize);
                _corners[i].Set("opaque", true);
                float sx = i is 0 or 2 ? -1f : 1f, sy = i is 0 or 1 ? -1f : 1f;
                _brackets[i] = Bracket(_corners[i], sx, sy);
                _knobs[i] = Knob(_corners[i], sx, sy);
            }
            _capsules = NativeUi.Component(Holder, _rootWidth, _rootHeight);
            _capsules.Set("touchable", false);
            _header = Capsule(_capsules, HeaderHeight, out _headerWidth, (box, at) =>
            {
                _dots.Add(SmoothShape.Create(box, at, HeaderHeight / 2 - 5f, 10f, 10f, Red, 5f, 5f, 5f, 5f));
                return Label(box, names, at + 18f, HeaderHeight, track: true, color: Color.white);
            }, dark: true);
            _short = Capsule(_capsules, HeaderHeight, out _, (box, at) =>
            {
                _dots.Add(SmoothShape.Create(box, at, HeaderHeight / 2 - 5f, 10f, 10f, Red, 5f, 5f, 5f, 5f));
                return Label(box, ModText.Text("전투 중계"), at + 18f, HeaderHeight, color: Color.white);
            }, dark: true);
            _button = Capsule(_capsules, ButtonHeight, out _buttonWidth, (box, at) =>
            {
                MouseIcon(box, at, ButtonHeight / 2 + .5f);
                return Label(box, ModText.Text("전체 화면"), at + 21f, ButtonHeight, size: 15, color: Color.white);
            }, dark: true);
        }

        internal bool Outdated(float rootWidth, float rootHeight) =>
            Holder.Get<bool>("isDisposed") || rootWidth != _rootWidth || rootHeight != _rootHeight || _language != ModText.Revision;

        // The parts around the picture at rest: ring, click area with the
        // capsules' outer halves, a corner square centred on each corner of the ring, the names capsule on the top edge (the
        // short one when the names do not fit) and the "전체 화면" capsule on the bottom edge.
        internal void Layout((float X, float Y, float W, float H) rest)
        {
            if (rest == _rest) return;
            _rest = rest;
            var (x, y, w, h) = rest;
            float r = FightPipLayout.Ring, half = HeaderHeight / 2, corner = FightPipLayout.CornerSize;
            Place(_ring, x - r, y - r, w + 2 * r, h + 2 * r);
            Place(Hit, x - r, y - r - half, w + 2 * r, h + 2 * r + half + ButtonHeight / 2);
            for (var i = 0; i < 4; i++)
                _corners[i].Call("SetXY", (i is 0 or 2 ? x - r : x + w + r) - corner / 2, (i is 0 or 1 ? y - r : y + h + r) - corner / 2);
            var wide = _headerWidth <= w - 50f;
            _header.Set("visible", wide);
            _short.Set("visible", !wide);
            _header.Call("SetXY", x + 10f, y - r - half);
            _short.Call("SetXY", x + 10f, y - r - half);
            _button.Call("SetXY", x + w + r - 10f - _buttonWidth, y + h + r - ButtonHeight / 2);
        }

        private static void Place(RuntimeObject part, float x, float y, float w, float h)
        {
            part.Call("SetXY", x, y);
            part.Call("SetSize", w, h);
        }

        // Moves the frame from its resting place to the picture's current top-left and scale, with its alphas, input, the
        // knob of the corner under the pointer or being dragged (-1: none) and the brackets on the other corners (`corners`).
        internal void Show(float x, float y, float scale, float alpha, float capsules, bool takesInput, int knob, bool corners,
            bool live, RuntimeObject window)
        {
            if (_lit != live)
            {
                _lit = live;
                foreach (var dot in _dots) dot.Set("alpha", live ? 1f : .25f);
            }
            var root = Holder.Get("parent")!;
            Holder.Set("visible", alpha > .001f);
            Holder.Call("SetScale", scale, scale);
            Holder.Call("SetXY", x - _rest.X * scale, y - _rest.Y * scale);
            if (Holder.Get<float>("alpha") != alpha) Holder.Set("alpha", alpha);
            if (_capsules.Get<float>("alpha") != capsules) _capsules.Set("alpha", capsules);
            Hit.Set("touchable", takesInput);
            for (var i = 0; i < 4; i++)
            {
                _corners[i].Set("touchable", takesInput);
                _knobs[i].Set("visible", i == knob);
                _brackets[i].Set("visible", takesInput && corners && i != knob);
            }
            Above(root, Holder, window);
        }

        // The corner whose square the pointer path holds, or -1.
        internal int CornerIn(IReadOnlyList<RuntimeObject> path)
        {
            for (var i = 0; i < 4; i++)
                if (path.Any(item => item.Pointer == _corners[i].Pointer)) return i;
            return -1;
        }

        // Keeps a mod part drawn and hit after FightWindow: both have the window's sortingOrder, and a window shown again is
        // inserted after every child of its order. FairyGUI's SetChildIndex (also AddChild of a child) does nothing to a child
        // with a sortingOrder, so the part goes out and back in, which inserts it after the window; on dev.19-21 the window
        // shown again sat over the PIP's click area and the PIP took no clicks or drags (user-reported).
        internal static void Above(RuntimeObject root, RuntimeObject part, RuntimeObject window)
        {
            var index = root.IndexOfChild(part);
            var windowIndex = root.IndexOfChild(window);
            if (index < 0 || windowIndex < 0 || index > windowIndex) return;
            root.Call("RemoveChild", part);
            root.Call("AddChild", part);
        }

        // The exit after the window has gone: the frame finishes fading out, then goes. False once it has gone.
        internal bool Leave(float now)
        {
            if (Holder.Get<bool>("isDisposed")) { Gone = true; return false; }
            if (_leftAt < 0) { _leftAt = now; _leftAlpha = Holder.Get<float>("alpha"); }
            var u = FightPipLayout.Fade(now - _leftAt);
            Holder.Set("alpha", (1f - u) * _leftAlpha);
            if (u < 1f) return true;
            Dispose();
            return false;
        }

        internal void Dispose()
        {
            Gone = true;
            GameUi.Dispose(Holder);
        }

        // "PIP 모드": its own capsule at the top left of the full-screen fight, above FightWindow, taking the clicks. A black
        // capsule 44 high, like the game's own 스킬 button, with a picture-in-picture icon and an Esc key cap, since Esc does
        // the same (draft 다 of artifacts/cc/cctv-fight/v5-handles, user-chosen 2026-11-17: "축소 [Esc]", 34 high and white,
        // was too small to notice and did not say where it went).
        internal RuntimeObject Corner(RuntimeObject root, int sortingOrder)
        {
            const float h = 44f;
            var holder = NativeUi.Component(root, 10f, h + 3f);
            holder.Set("sortingOrder", sortingOrder);
            Capsule(holder, h, out var width, (box, at) =>
            {
                PipIcon(box, at, h / 2, 26f, 20f, 2.6f);
                var end = Label(box, ModText.Text("PIP 모드"), at + 36f, h, size: 20, color: Color.white);
                return KeyCap(box, "Esc", end + 10f, h, 26f, 14, Color.black, Color.white, 2f);
            }, dark: true);
            holder.Call("SetSize", width, h + 3f);
            holder.Set("opaque", true);
            holder.Call("SetXY", 24f, 21f);
            return holder;
        }

        // A capsule at height h: white with a black ring, dark text and a soft shadow (NoteUi's look without its grey stripes,
        // user-requested 2026-11-17), its contents drawn by `content`, which gets the left edge to start at and returns where
        // its contents end.
        // `dark`: black with a faint white rim inside its ring, for white contents.
        private static RuntimeObject Capsule(RuntimeObject parent, float h, out float width, Func<RuntimeObject, float, float> content,
            bool dark = false)
        {
            var ring = h >= 30f ? 2.5f : 2f;
            var box = NativeUi.Component(parent, 10f, h + 3f);
            box.Set("touchable", false);
            var end = content(box, h >= 40f ? 18f : h >= 30f ? 16f : 12f);
            width = end + (h >= 40f ? 16f : h >= 30f ? 14f : 12f);
            box.Call("SetSize", width, h + 3f);
            var shadow = SmoothShape.Create(box, 0f, 3f, width, h, NoteUi.Shadow, h / 2, h / 2, h / 2, h / 2);
            var body = SmoothShape.Create(box, 0f, 0f, width, h, dark ? Color.black : Color.white, h / 2, h / 2, h / 2, h / 2, ring, Color.black);
            box.Call("SetChildIndex", shadow, 0);
            box.Call("SetChildIndex", body, 1);
            if (dark)
            {
                var r = h / 2 - ring;
                var rim = SmoothShape.Create(box, ring, ring, width - ring * 2, h - ring * 2, Clear0, r, r, r, r, 1.5f, Rim);
                box.Call("SetChildIndex", rim, 2);
            }
            return box;
        }

        private static float Label(RuntimeObject box, string text, float at, float h, bool track = false, int size = (int)TextSize,
            Color? color = null)
        {
            var label = NoteUi.Text(box, text, at, .5f - NoteUi.CentreLift * size, 600f, h, size, color ?? NoteUi.Ink);
            if (track) ModFont.Track(label);
            var width = label.Get<float>("textWidth");
            label.Call("SetSize", width + 8f, h);
            return at + width;
        }

        // A key's name in a small rounded cap with an outline in its ink, as keyboard hints are drawn; returns where it ends.
        private static float KeyCap(RuntimeObject box, string key, float at, float h, float capHeight, int size, Color fill, Color ink, float ring)
        {
            var y = (h - capHeight) / 2;
            var label = NoteUi.Text(box, key, at, y + .5f - NoteUi.CentreLift * size, 100f, capHeight, size, ink, align: 1);
            var width = label.Get<float>("textWidth") + size;
            label.Call("SetSize", width, capHeight);
            var corner = capHeight * .26f;
            var cap = SmoothShape.Create(box, at, y, width, capHeight, fill, corner, corner, corner, corner, ring, ink);
            box.Call("SetChildIndex", cap, box.Call("GetChildIndex", label)!.Value<int>());
            return at + width;
        }

        // Picture-in-picture: a white screen outline w x h from x, centred on cy, with a small filled picture at its bottom right.
        private static void PipIcon(RuntimeObject box, float x, float cy, float w, float h, float t)
        {
            var r = 2.5f * w / 20f;
            SmoothShape.Create(box, x, cy - h / 2, w, h, Clear0, r, r, r, r, t, Color.white);
            float iw = w * .46f, ih = h * .46f, inset = t + .8f, ir = 1.2f * w / 20f;
            SmoothShape.Create(box, x + w - inset - iw, cy + h / 2 - inset - ih, iw, ih, Color.white, ir, ir, ir, ir);
        }

        // A rounded stroke from (x0, y0) to (x1, y1), its ends round.
        private static void Stroke(RuntimeObject box, float x0, float y0, float x1, float y1, float t, Color color)
        {
            float dx = x1 - x0, dy = y1 - y0, length = (float)Math.Sqrt(dx * dx + dy * dy) + t;
            var bar = SmoothShape.Create(box, 0f, 0f, length, t, color, t / 2, t / 2, t / 2, t / 2);
            bar.Call("SetPivot", .5f, .5f, true);
            bar.Call("SetXY", (x0 + x1) / 2, (y0 + y1) / 2);
            if (dy != 0f) bar.Set("rotation", (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI));
        }

        // An L on the ring's corner, black with a thin white edge, its legs 14 along the edges into the picture.
        private static RuntimeObject Bracket(RuntimeObject corner, float sx, float sy)
        {
            const float leg = 14f, width = 5f, edge = 1.5f;
            var size = FightPipLayout.CornerSize;
            var box = NativeUi.Component(corner, size, size);
            box.Set("touchable", false);
            box.Set("visible", false);
            // The square is centred on the ring's outer corner; the L lies on the ring's middle.
            float px = size / 2 - sx * FightPipLayout.Ring / 2, py = size / 2 - sy * FightPipLayout.Ring / 2;
            foreach (var (t, color) in new[] { (width + edge * 2, Color.white), (width, Color.black) })
            {
                Stroke(box, px, py, px, py - sy * leg, t, color);
                Stroke(box, px, py, px - sx * leg, py, t, color);
            }
            return box;
        }

        // A black disc 24 across with the capsules' faint rim and a white two-headed arrow along the corner's diagonal.
        private static RuntimeObject Knob(RuntimeObject corner, float sx, float sy)
        {
            const float d = 24f, k = 4.6f, head = 4.2f, t = 2.2f, inset = 2.2f;
            var at = (FightPipLayout.CornerSize - d) / 2;
            var box = NativeUi.Component(corner, d, d + 3f);
            box.Call("SetXY", at, at);
            box.Set("touchable", false);
            box.Set("visible", false);
            SmoothShape.Create(box, .5f, 2.5f, d, d, NoteUi.Shadow, d / 2, d / 2, d / 2, d / 2);
            SmoothShape.Create(box, 0f, 0f, d, d, Color.black, d / 2, d / 2, d / 2, d / 2);
            var r = d / 2 - inset;
            SmoothShape.Create(box, inset, inset, d - inset * 2, d - inset * 2, Clear0, r, r, r, r, 1.5f, Rim);
            float c = d / 2, ax = c + sx * k, ay = c + sy * k, bx = c - sx * k, by = c - sy * k;
            Stroke(box, ax, ay, bx, by, t, Color.white);
            foreach (var (hx, hy, dx, dy) in new[] { (ax, ay, sx, sy), (bx, by, -sx, -sy) })
            {
                Stroke(box, hx - dx * head, hy, hx, hy, t, Color.white);
                Stroke(box, hx, hy, hx, hy - dy * head, t, Color.white);
            }
            return box;
        }

        // A white mouse 12 x 17 from x, centred on cy, its left button filled: a click on the PIP gives full screen.
        private static void MouseIcon(RuntimeObject box, float x, float cy)
        {
            const float w = 12f, h = 17f, t = 1.8f;
            var top = cy - h / 2;
            var split = top + h * .44f;
            SmoothShape.Create(box, x, top, w / 2, split - top, Color.white, w / 2, 0f, 0f, 0f);
            SmoothShape.Create(box, x, top, w, h, Clear0, w / 2, w / 2, w / 2, w / 2, t, Color.white);
            SmoothShape.Create(box, x + t / 2, split - t * .45f, w - t, t * .9f, Color.white, 0f, 0f, 0f, 0f);
            SmoothShape.Create(box, x + w / 2 - t * .45f, top + t / 2, t * .9f, split - top - t / 2, Color.white, 0f, 0f, 0f, 0f);
        }
    }

    // Development builds only (user-requested 2026-11-16): solo practice has no emoji/chat toolbar (BattleLogic.ReadyBattleUI
    // shows ExpressionWindow and ExpressionListWindow only when the map type is 10 or the room is not a single-game model), so
    // the PiP's panel and bubble behaviour could not be tried there. With the PiP ON, this shows both windows once per room
    // and makes everything that would send something take no clicks: the emoji and chat lists, the quick reply and the ping
    // menu (com_ChatMenu); the panels still open and close. A click on an emoji draws the local player's own bubble the way
    // ExpressionWindow.OnExpressionSelected does before its request (CommunicateLogic.Signal.battleMessage with a
    // BattleMessage(EXPRESSION, self, id)) and closes the panel; nothing is sent. Ctrl+Shift+F7 adds a chat bubble from another
    // player through ExpressionListWindow.AddMessage, which only draws it. The chat panel, the map tool and its ping stay
    // inert in solo: OnOpenChatList finds no battle chat lines for a solo map and SwitchCursorType needs the scene's
    // MarkManager, which solo leaves off.
    private static IntPtr _soloRoom;
    private static int _soloLine, _soloPressed = -1;
    private static Vector2 _soloPressedAt;
    private static readonly string[] SoloLines = { "고마워!", "정말 멋져!", "나이스!", "가자!", "조심해!", "기다려 줘!" };

    private static void DevSolo(Context context)
    {
        if (!Plugin.FightPip.Value) return;
        var room = context.Logic.Get("room")?.Get("curRoomInfo");
        var battle = context.Logic.Get("battle");
        if (room == null || room.Get<int>("MapType") == 10 || room.Call("IsSingleGameModel")?.Value<bool>() != true
            || battle?.Field<bool>("clientFinishReady") != true) return;
        var manager = RuntimeObject.StaticField(_managerClass, "_inst");
        var toolbar = manager?.Get("expression");
        var list = manager?.Get("ExpressionList");
        if (toolbar == null || list == null) return;
        if (_soloRoom != room.Pointer)
        {
            _soloRoom = room.Pointer;
            toolbar.Call("TryShowAsync");
            list.Call("TryShowAsync");
            Plugin.Diagnostics.State("fightPip.solo", "toolbar shown; sending parts take no clicks");
        }
        foreach (var part in new[] { toolbar.Field("_list_Expression"), toolbar.Field("_list_Chat"), toolbar.Field("_btn_QuickReply"),
            toolbar.Get("contentPane")?.Field("com_ChatMenu") })
            if (part != null && part.Get<bool>("touchable")) part.Set("touchable", false);
        SoloEmoji(context, toolbar, battle);
        if (!UnityEngine.Input.GetKeyDown(KeyCode.F7)
            || !(UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
            || !(UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))) return;
        var players = battle.Field("PlayerDatas");
        var self = battle.Call("GetSelfPlayerData")?.Pointer ?? IntPtr.Zero;
        var others = new List<RuntimeObject>();
        for (int i = 0, count = players?.Get<int>("Count") ?? 0; i < count; i++)
            if (players!.Call("get_Item", i) is { } player && player.Pointer != self && player.Field("player") != null) others.Add(player);
        if (others.Count == 0) return;
        var sender = others[_soloLine % others.Count];
        var message = RuntimeObject.New(RuntimeObject.FindClass("UI", "BattleMessage"), sender, SoloLines[_soloLine++ % SoloLines.Length]);
        list.Call("AddMessage", message);
    }

    // An emoji clicked in the open panel (the list itself takes no clicks): the game's own local bubble, without the request.
    // A failure here only logs, so the test aid never trips the PiP's breaker.
    private static void SoloEmoji(Context context, RuntimeObject toolbar, RuntimeObject battle)
    {
        try
        {
            var list = toolbar.Field("_list_Expression");
            var style = toolbar.Field("_interactStyle");
            if (list == null || style?.Get<int>("selectedIndex") != 1 || !GameUi.Visible(list)) { _soloPressed = -1; return; }
            var point = NativeUi.Pointer();
            int Item()
            {
                for (int i = 0, count = list.Get<int>("numChildren"); i < count; i++)
                    if (list.Call("GetChildAt", i) is { } child && child.Get<bool>("visible") && NativeUi.Contains(child, point)) return i;
                return -1;
            }
            if (UnityEngine.Input.GetMouseButtonDown(0)) { _soloPressed = Item(); _soloPressedAt = point; }
            if (!UnityEngine.Input.GetMouseButtonUp(0) || _soloPressed < 0) return;
            var pressed = _soloPressed;
            _soloPressed = -1;
            if (Item() != pressed || Math.Abs(point.x - _soloPressedAt.x) > 50f || Math.Abs(point.y - _soloPressedAt.y) > 50f) return;
            var index = list.Call("ChildIndexToItemIndex", pressed)?.Value<int>() ?? -1;
            var expressions = toolbar.Field("_selfExpressions");
            var self = battle.Call("GetSelfPlayerData");
            if (expressions == null || self == null || index < 0 || index >= expressions.Get<int>("Count")) return;
            var id = expressions.Call("get_Item", index)!.Get<int>("Key");
            style.Set("selectedIndex", 0);
            // BattleMessage(MessageType.EXPRESSION = 2, sender, expression id), as OnExpressionSelected builds it.
            var message = RuntimeObject.New(RuntimeObject.FindClass("UI", "BattleMessage"), 2, self, id);
            context.Logic.Get("communicate")?.Field("Signal")?.Field("battleMessage")?.Call("Dispatch", message);
            Plugin.Diagnostics.State("fightPip.solo", "emoji drawn locally");
        }
        catch (Exception ex) { _soloPressed = -1; Plugin.Diagnostics.State("fightPip.solo", "emoji failed: " + ex.GetType().Name + ": " + ex.Message); }
    }
}
