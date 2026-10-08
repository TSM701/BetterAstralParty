using System.Globalization;
using UnityEngine;

namespace BetterAstralParty;

internal static class GameUi
{
    private static string? _attackIcon, _defenseIcon, _hpIcon;
    private static bool _iconsResolved;
    private static IntPtr _rootClass, _textClass, _logicClass, _battleClass;
    private static RuntimeObject? _root, _home, _menu, _pressed, _fight;
    private static AdviceBadge? _defBadge, _dodgeBadge;
    private static RuntimeObject? _settingsHome;
    private static bool _settingsHomeTouchable;
    private static RuntimeObject? _menuAnchor;
    private static float _menuOffsetX;
    private static int _menuLanguageRevision = -1;
    private static (IntPtr Menu, int Language, string? Version)? _releaseNotice;
    private static (float X, float Y, float Alpha, bool Visible, bool Touchable)? _menuState;
    private static RuntimeObject? _defButton, _dodgeButton;
    private static ChoiceVisual? _observerSummary;
    private static IntPtr _observerParent;
    private static bool _adviceShowing;
    private static string _lastError = "", _lastAdvice = "";
    private static float _nextScan;
    private static VisibleCombat? _calculationInput;
    private static VisibleAdvice? _calculationResult;
    private static EncounterInput? _encounterInput;
    private static EncounterAdvice? _encounterResult;
    private static readonly List<RuntimeObject> HitPath = new();
    private static int _hitFrame = -1;
    internal static bool HomeAvailable { get; private set; }
    internal static RuntimeObject? Root => _root;

    // Read current native state at display time; HomeAvailable is a sampled discovery hint.
    internal static UpdateNotificationHome NotificationHome()
    {
        if (!HomeAvailable || !Visible(_home) || !Visible(_menuAnchor) || _root == null)
            return default;
        RuntimeObject.RequireClasses(UpdateNotificationUi.Feature, ("UI", "UIManager"), ("GameLogic", "GameLogicManager"));
        var manager = RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst");
        var panel = manager?.Get("currentPanel");
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        var match = logic?.Get("match")?.Field("matchData");
        var room = logic?.Get("room")?.Field("roomController");
        if (manager == null || panel == null || logic == null || match == null || room == null)
            return default;
        var currentHome = panel.TypeName == "HomePanel" && panel.Get("ui")?.Pointer == _home!.Pointer;
        var controlsReady = _home!.Get<bool>("touchable") && _home.Get<float>("alpha") > 0
            && _home.Get<float>("scaleX") > 0 && _home.Get<float>("scaleY") > 0
            && !_home.Field("Cut_in")!.Get<bool>("playing") && _menuAnchor!.Get<bool>("touchable")
            && _menuAnchor.Get<float>("alpha") > 0 && _menuAnchor.Get<float>("scaleX") > 0
            && _menuAnchor.Get<float>("scaleY") > 0;
        var blocked = ModUi.IsOpen || WindowsReleaseCredentialInput.PromptInProgress
            || Plugin.UpdateSession?.Status == ReleaseSessionStatus.AwaitingInput
            || _root.Call("GetTopWindow") != null || _root.Get<bool>("hasModalWindow")
            || _root.Get<bool>("modalWaiting") || _root.Get<bool>("hasAnyPopup");
        // Verified native NONE=0; a disposed team has TeamId=0 with stale MatchStatus.
        return new(true, Visible(_home), currentHome, controlsReady, blocked, match.Get<long>("TeamId"),
            room.Field("roomStateType")!.Value<int>(), logic.Get<long>("connectRoomId"));
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed("CoreUi")) return;
        // Do not keep last frame's scene objects alive when no feature needs hit testing.
        if (_hitFrame != Time.frameCount) HitPath.Clear();
        try
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.2f;
                if (_rootClass == IntPtr.Zero)
                {
                    _rootClass = RuntimeObject.FindClass("FairyGUI", "GRoot");
                    if (_rootClass == IntPtr.Zero) return;
                    _textClass = RuntimeObject.FindClass("FairyGUI", "GTextField");
                    _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
                    _battleClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
                }
                _root = RuntimeObject.StaticField(_rootClass, "_inst");
                if (_root == null) return;
                RuntimeObject.RequireClasses("CoreUi", ("FairyGUI", "GTextField"), ("FairyGUI", "GComponent"), ("FairyGUI", "GGraph"));
                UpdateHome();
                if (Compatibility.Allowed("Enabled"))
                {
                    try { UpdateFight(); }
                    catch (Exception ex) { Compatibility.Block("Enabled", ex, ClearAdvice); }
                }
            }
            if (_root != null) CharacterNameUi.Tick(_root);
            FollowHomeMenu();
            HandleMenuInput();
            if (Compatibility.Allowed("Enabled"))
            {
                try
                {
                    UpdateHover();
                    _defBadge?.Animate();
                    _dodgeBadge?.Animate();
                    _observerSummary?.Tick();
                    CardDiceUi.PollResolved();
                    CardUi.Animate();
                    RemainingHpUi.PollFinalPoints();
                    RemainingHpUi.Animate();
                }
                catch (Exception ex) { Compatibility.Block("Enabled", ex, ClearAdvice); }
            }
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("GameUi", ex);
            ModUi.FailUi(ex);
            HomeAvailable = false;
            try { HideAdvice(); Dispose(_menu); } catch { /* A disposed scene may already have removed its UI. */ }
            _menu = null;
            if (_lastError != ex.Message)
            {
                _lastError = ex.Message;
                Plugin.Logger.LogWarning($"[게임 UI 연결] {ex}");
            }
        }
    }

    internal static bool Visible(RuntimeObject? obj)
    {
        if (obj == null || obj.Get<bool>("isDisposed") || !obj.Get<bool>("onStage")) return false;
        for (var current = obj; current != null; current = current.Get("parent"))
            if (!current.Get<bool>("internalVisible") || !current.Get<bool>("internalVisible2")) return false;
        return true;
    }

    internal static RuntimeObject? Find(RuntimeObject node, string type, int depth = 0, int maxDepth = 4)
    {
        // Ancestors were already checked by the preceding recursive call.
        if (depth == 0 ? !Visible(node) : node.Get<bool>("isDisposed") || !node.Get<bool>("onStage")
            || !node.Get<bool>("internalVisible") || !node.Get<bool>("internalVisible2")) return null;
        if (node.TypeName == type) return node;
        // Window/panel searches cannot succeed inside a field plate or buff icon.
        // Avoid walking every nickname, stat and effect whenever a window is absent.
        if (node.TypeName is "UICom_AttrInfo" or "UIButton_Buff") return null;
        if (depth >= maxDepth || node.Get("asCom") == null) return null;
        var count = node.Get<int>("numChildren");
        for (var i = 0; i < count; i++)
        {
            var child = node.Call("GetChildAt", i);
            if (child == null) continue;
            var found = Find(child, type, depth + 1, maxDepth);
            if (found != null) return found;
        }
        return null;
    }

    internal static IReadOnlyList<RuntimeObject> PointerPath(bool refresh = false)
    {
        if (!refresh && _hitFrame == Time.frameCount) return HitPath;
        _hitFrame = Time.frameCount;
        HitPath.Clear();
        for (var hit = _root?.Get("touchTarget"); hit != null && HitPath.Count < 64; hit = hit.Get("parent"))
            HitPath.Add(hit);
        return HitPath;
    }

    private static void UpdateHome()
    {
        var home = Visible(_home) ? _home : Find(_root!, "UIHomePanel");
        HomeAvailable = home != null;
        Plugin.Diagnostics.State("home", HomeAvailable ? "visible" : "hidden");
        if (home == null)
        {
            if (_menu != null && !_menu.Get<bool>("isDisposed")) _menu.Set("visible", false);
            _menuState = null;
            return;
        }
        // Read one known native menu label; never scan player names or translated mod labels.
        ModText.Select(Plugin.Language.Value, home.Field("btn_Activity")?.Call("GetChild", "n8")?.Get("text")?.String());
        if (_home?.Pointer != home.Pointer || _menu == null || _menu.Get<bool>("isDisposed"))
        {
            Dispose(_menu);
            _releaseNotice = null;
            Plugin.Diagnostics.Write("home menu create begin");
            _home = home;
            _menuLanguageRevision = ModText.Revision;
            var template = _menuAnchor = home.Field("btn_Dictionary")!;
            var activity = home.Field("btn_Activity")!;
            _menu = NativeUi.Component(home, activity.Get<float>("width"), activity.Get<float>("height"));
            _menu.Set("visible", false);
            _menu.Set("tooltips", ModText.Text("BetterAstralParty · 모드 설정 (F8)"));
            var nativeButton = NativeUi.Create("Home", "Home_Button_Activity")
                ?? throw new InvalidOperationException("원본 메뉴 버튼 준비 대기");
            _menu.Call("AddChild", nativeButton);
            // Keep the native button controller, hover/down gears and sound.
            // No HomePanel event/shop callbacks are attached to this private instance.
            nativeButton.Field("redPoint")!.Set("selectedIndex", 0);
            var driver = nativeButton.Call("GetChild", "n11")!;
            driver.Set("visible", false);
            // Preserve the original image's pivot/extent: its GearLook supplies the actual
            // hover/down/rollout rotation (including the game's custom easing curve).
            var art = NativeUi.Component(nativeButton, driver.Get<float>("width"), driver.Get<float>("height"));
            art.Set("touchable", false);
            art.Call("SetPivot", driver.Get<float>("pivotX"), driver.Get<float>("pivotY"), driver.Get<bool>("pivotAsAnchor"));
            art.Call("SetXY", driver.Get<float>("x"), driver.Get<float>("y"));
            // This gear belongs to our private button clone, never to the live Activity button.
            // Retarget it directly: native timing/curve/press/reversal all run on the artwork,
            // with no polling, duplicate tweens or one-frame transform lag.
            driver.Call("GetGear", 3)!.SetField("_owner", art);
            nativeButton.Call("SetChildIndex", art, 0);
            // Native menu composition: rounded colour plate, drop shadow, oversized portrait,
            // and the original outlined title. No separate label pill or miniature card.
            AddRoundedRect(art, 347, 170, 0, new Color(0f, 0f, 0f, 0.10f));
            art.Call("GetChildAt", 0)!.Call("SetXY", 4f, 8f);
            AddRoundedRect(art, 343, 166, 0, new Color(0f, 0f, 0f, 0.24f));
            art.Call("GetChildAt", 1)!.Call("SetXY", 6f, 10f);
            AddRoundedRect(art, 343, 166, 0, new Color(0.10f, 0.72f, 0.80f));
            art.Call("GetChildAt", 2)!.Call("SetXY", 2f, 6f);
            var portrait = NativeUi.Component(art, 354, 194);
            portrait.Call("SetXY", -10f, -22f);
            // Overflow's screen-aligned clip rect cannot follow a rotated lower edge.
            // A native stencil mask inherits the same transform as the portrait and plate.
            var portraitMask = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            portraitMask.Call("SetSize", 370f, 234f);
            portraitMask.Call("SetXY", -4f, -40f);
            portraitMask.Get("shape")!.Call("DrawRect", 0f, Color.white, Color.white);
            portraitMask.Set("touchable", false);
            portrait.Call("AddChild", portraitMask);
            portrait.Set("mask", portraitMask.Get("displayObject")!);
            // Shadow first, portrait second: share the native cached texture/alpha silhouette.
            // Both inherit the same mask and hover transform; tint only the private loader.
            foreach (var shadow in new[] { true, false })
            {
                var skin = RuntimeObject.New(RuntimeObject.FindClass("UI", "TextureLoader"));
                skin.Set("touchable", false);
                // Native ScaleMatchWidth preserves the full source width and aspect ratio.
                skin.Set("autoSize", false);
                skin.Set("fill", 3); // FairyGUI.FillType.ScaleMatchWidth (not ScaleFree).
                skin.Call("SetSize", 354f, 194f);
                skin.Call("SetXY", shadow ? 8f : 0f, shadow ? -6f : -12f);
                if (shadow)
                {
                    skin.Set("color", Color.black);
                    skin.Set("alpha", 0.35f);
                }
                portrait.Call("AddChild", skin);
                // Native async cache owns the shared texture and releases each loader's reference.
                skin.Set("url", "UT_Hero_Card_101_01");
            }
            var title = nativeButton.Call("GetChild", "n8")!;
            title.Set("text", ModText.Text("모드 설정"));
            title.Set("autoSize", 3);
            title.Set("singleLine", true);
            NativeUi.OutlineText(title);
            // Keep the native title's font, size and spacing.
            NativeUi.Position(nativeButton, 0f, 0f, 1f);
            var scale = Math.Min(activity.Get<float>("scaleX"), activity.Get<float>("scaleY"));
            _menu.Call("SetScale", scale, scale);
            // Character art extends 17px left of its hit box; leave another 16px clear.
            _menuOffsetX = _menu.Get<float>("width") * scale + 33f * scale;
            _menuState = null;
            Plugin.Logger.LogInfo("[게임 UI 연결] 메인메뉴에 모드 설정 버튼 추가됨");
            Plugin.Diagnostics.Write("home menu create complete");
        }
    }

    // Only the mod-owned Activity clone's known title/tooltip; no native controller indices or callbacks.
    internal static void RefreshReleaseNotification()
    {
        if (_menu == null || _menu.Get<bool>("isDisposed")) return;
        var version = Plugin.Updates.NotificationVersion;
        var state = (_menu.Pointer, ModText.Revision, version);
        if (_releaseNotice == state) return;
        var title = ModText.Text("모드 설정") + (version == null ? "" : " · " + ModText.Text("새 버전"));
        _menu.Call("GetChildAt", 0)!.Call("GetChild", "n8")!.Set("text", title);
        var tooltip = ModText.Text("BetterAstralParty · 모드 설정 (F8)");
        if (version != null) tooltip += "\n" + ModText.Text("새 버전") + ": " + version;
        _menu.Set("tooltips", tooltip);
        _releaseNotice = state;
    }

    internal static void RefreshLanguage()
    {
        // UpdateHome may already have created the menu in the new language this
        // frame. Do not destroy/reload its portrait and controls a second time.
        if (_menuLanguageRevision != ModText.Revision) { Dispose(_menu); _menu = null; }
        ClearAdvice();
        CardPreviewUi.Reset();
        BattleStatusUi.Clear();
        FieldBuffUi.Clear("language");
    }

    internal static void AddRoundedRect(RuntimeObject parent, float width, float height, float inset, Color color)
    {
        var graph = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
        var radii = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(new[] { 12f, 12f, 12f, 12f });
        graph.Call("DrawRoundRect", width, height, color, new RuntimeObject(radii.Pointer));
        graph.Call("SetXY", inset, inset);
        graph.Set("touchable", false);
        parent.Call("AddChild", graph);
        GC.KeepAlive(radii);
    }

    private static void FollowHomeMenu()
    {
        if (_menu == null || _menu.Get<bool>("isDisposed")) return;
        var visible = Visible(_menuAnchor);
        if (!visible)
        {
            if (_menuState != null) _menu.Set("visible", false);
            _menuState = null; _pressed = null;
            return;
        }
        var anchor = _menuAnchor!;
        // Sample native Cut in/Hide/BGset motion every frame, including reverse/re-entry.
        // Move only our wrapper so its child's own hover/press transform is untouched.
        // xMin/yMin handle both pivot modes; native centre pivots are NOT necessarily anchors.
        var state = (X: anchor.Get<float>("xMin") - _menuOffsetX, Y: anchor.Get<float>("yMin"),
            Alpha: anchor.Get<float>("alpha"), Visible: visible, Touchable: anchor.Get<bool>("touchable"));
        if (_menuState == state) return;
        _menuState = state;
        _menu.Call("SetXY", state.X, state.Y);
        _menu.Set("alpha", state.Alpha);
        _menu.Set("visible", state.Visible);
        _menu.Set("touchable", state.Touchable);
    }

    internal static void SetSettingsModal(bool visible)
    {
        if (!visible)
        {
            if (_settingsHome != null && !_settingsHome.Get<bool>("isDisposed"))
                _settingsHome.Set("touchable", _settingsHomeTouchable);
            _settingsHome = null;
            return;
        }
        // Block the home subtree while our settings are open, then restore it.
        if (_settingsHome == null && _home != null && !_home.Get<bool>("isDisposed"))
        {
            _settingsHome = _home;
            _settingsHomeTouchable = _home.Get<bool>("touchable");
            _home.Set("touchable", false);
        }
    }

    private static void HandleMenuInput()
    {
        if (!HomeAvailable || !Visible(_menu) || _root == null || !_menu!.Get<bool>("touchable")
            || _menu.Get<float>("alpha") <= 0) { _pressed = null; return; }
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonUp(0)) return;
        var hit = PointerPath().FirstOrDefault(item => item.Pointer == _menu.Pointer);
        if (Input.GetMouseButtonDown(0)) _pressed = hit;
        if (Input.GetMouseButtonUp(0))
        {
            if (hit != null && _pressed?.Pointer == hit.Pointer) ModUi.TogglePanel();
            _pressed = null;
        }
    }

    private static void UpdateFight()
    {
        HideAdvice(keepDice: true);
        if (!Plugin.Enabled.Value && !Plugin.KoMinimum.Value) { Plugin.Diagnostics.EndCombat(); CardUi.Clear(); RemainingHpUi.Clear(); return; }
        if (HomeAvailable) RuntimeObject.RequireClasses("Enabled", ("GameLogic", "GameLogicManager"), ("Core.Scene", "BattleSceneController"));
        var fight = Visible(_fight) ? _fight : Find(_root!, "FightWindow");
        if (fight == null) { Plugin.Diagnostics.EndCombat(); Plugin.Diagnostics.State("fightStep", "hidden"); _lastAdvice = ""; CardUi.Clear(); RemainingHpUi.ResetCapture(); return; }
        _fight = fight;
        var ui = fight.Get("contentPane");
        // A window can be on stage before its asynchronously loaded content is attached.
        if (ui == null || ui.Get<bool>("isDisposed"))
        {
            CardUi.Clear(); RemainingHpUi.ResetCapture();
            Plugin.Diagnostics.State("fightReady", "waiting-content");
            return;
        }
        if (ui.TypeName != "UIFightWindow") { RemainingHpUi.Clear(); return; }
        var stepController = ui.Field("step");
        if (stepController == null)
        {
            CardUi.Clear(); RemainingHpUi.ResetCapture();
            Plugin.Diagnostics.State("fightReady", "waiting-step");
            return;
        }
        var step = stepController.Get<int>("selectedIndex");
        Plugin.Diagnostics.State("fightReady", "ready");
        Plugin.Diagnostics.State("fightStep", step.ToString(CultureInfo.InvariantCulture));
        if (step != 3) CardUi.Clear(keepDice: step is 4 or 5 or 6);
        if (step is not (1 or 3 or 5 or 6)) { CardDiceUi.Hide(); RemainingHpUi.ResetCapture(); return; }
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        var mapType = room?.Field("info")?.Get<int>("MapType") ?? 0;
        if (!VisibleCombatAdvisor.IsPve(mapType)) { Plugin.Diagnostics.EndCombat(); CardUi.Clear(); RemainingHpUi.Clear(); return; }
        if (Plugin.Diagnostics.IsRecording)
        {
            // Diagnostic failure must not disable the advisor or damage view.
            try
            {
                var source = fight.Get("attackerData");
                var target = fight.Get("defenderData");
                if (source != null && target != null && Plugin.Diagnostics.CombatPhase(fight.Pointer,
                    source.Pointer, target.Pointer, step, mapType, ui.Field("playerState")!.Get<int>("selectedIndex")))
                {
                    foreach (var actor in new[] { ("attacker", source), ("defender", target) })
                    {
                        var player = actor.Item2.Field("player")!;
                        Plugin.Diagnostics.State("combat.actor." + actor.Item1,
                            $"heroId={player.Get("Hero")!.Get<int>("HeroId")}; type={actor.Item2.Get<int>("characterType")}");
                    }
                    // The normal reader captures the defender. Capture the other public
                    // side at phase boundaries only, without inspecting any hand cards.
                    PublicCombatEffects.Read(source, "attacker", target);
                    PublicCombatEffects.Read(target, "phaseDefender", source);
                }
            }
            catch (Exception ex) { Plugin.Diagnostics.Error("combatSnapshot", ex); }
        }
        if (step == 1) { RemainingHpUi.ResetCapture(); if (Plugin.Enabled.Value) UpdateEncounter(fight, ui, mapType); return; }
        var director = RuntimeObject.StaticField(_battleClass, "inst")?.Field("directorManager");
        var atk = director?.Get("attacker")?.Field("_UI");
        var def = director?.Get("victim")?.Field("_UI");
        if (!Visible(atk) || !Visible(def)) return;
        var attackerUi = atk!.Field("com_Attack")!;
        var defenderUi = def!.Field("com_Defend")!;
        if (!int.TryParse(defenderUi.Field("txt_Life")!.Get("text")!.String(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var hp)) return;
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("combat.inputs", $"hp={hp}; attackMin={atk.Field("minATK")!.Value<int>()}; "
                + $"attackMax={atk.Field("maxATK")!.Value<int>()}; defenseMin={def!.Field("minDEF")!.Value<int>()}; "
                + $"defenseMax={def.Field("maxDEF")!.Value<int>()}");
        RemainingHpUi.Update(fight, ui, attackerUi, defenderUi, step, hp);
        if (Plugin.Diagnostics.IsRecording)
        {
            string PublicNumber(RuntimeObject? label) => Visible(label)
                && int.TryParse(label!.Get("text")!.String(), out var value) ? value.ToString(CultureInfo.InvariantCulture) : "hidden";
            Plugin.Diagnostics.State("combatVisible", $"step={step}; defender_hp={hp}; "
                + $"attacker_hp={PublicNumber(attackerUi.Field("txt_Life"))}; "
                + $"attack_point={PublicNumber(attackerUi.Field("com_Point")?.Field("txt_Point"))}; "
                + $"defense_point={PublicNumber(defenderUi.Field("com_Point")?.Field("txt_Point"))}");
        }
        if (step == 6) { CardDiceUi.Hide(); return; } // Observe visible results only; never use future outcome fields.
        var incomingBonus = PublicCombatEffects.Read(fight.Get("defenderData"), "defender", fight.Get("attackerData"));
        if (incomingBonus == null) { CardDiceUi.Clear(); return; }
        if (step == 3)
        {
            var counter = fight.Get("defenderData")?.Get("Property")?.Field("Counter");
            int.TryParse(attackerUi.Field("txt_Life")!.Get("text")!.String(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var attackerHp);
            CardUi.Update(fight, ui, new(mapType, hp, atk.Field("minATK")!.Value<int>(), atk.Field("maxATK")!.Value<int>(),
                def.Field("minDEF")!.Value<int>(), def.Field("maxDEF")!.Value<int>())
                { Modifiers = incomingBonus.Value,
                    CounterAvailable = counter?.Get<bool>("Value"), AttackerHp = attackerHp },
                attackerUi.Field("com_Point")!);
            return;
        }
        if (step == 5 && ui.Field("playerState")!.Get<int>("selectedIndex") is 0 or 1 or 2 && Plugin.KoMinimum.Value)
            CardDiceUi.BindResolved(ui, atk, def!, new(mapType, hp, atk.Field("minATK")!.Value<int>(),
                atk.Field("maxATK")!.Value<int>(), def!.Field("minDEF")!.Value<int>(), def.Field("maxDEF")!.Value<int>())
                { Modifiers = incomingBonus.Value });
        if (!Plugin.Enabled.Value) return;
        var point = attackerUi.Field("com_Point")!.Field("txt_Point")!;
        if (!Visible(point) || !int.TryParse(point.Get("text")!.String(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var die)) return;
        // The same label later contains ATK + die. A total of 1..6 is not a roll.
        if (!CardKoMinimum.RevealedDie(attackerUi.Field("showPoint")!.Get<int>("selectedIndex"),
                Visible(point), die)) return;
        var input = new VisibleCombat(mapType, hp, atk.Field("minATK")!.Value<int>(), atk.Field("maxATK")!.Value<int>(),
            die, def.Field("minDEF")!.Value<int>(), def.Field("maxDEF")!.Value<int>(), !fight.Field("dodgeNoAvailable")!.Value<bool>())
            { Modifiers = incomingBonus.Value };
        if (_calculationInput != input)
        {
            _calculationInput = input;
            _calculationResult = VisibleCombatAdvisor.Calculate(input);
        }
        var advice = _calculationResult;
        if (advice == null) return;
        var defend = ui.Field("btn_Defend")!;
        var dodge = ui.Field("btn_Dodge")!;
        var localChoice = Visible(defend) && Visible(dodge) && (defend.Get<bool>("touchable")
            || (ShushuShieldUi.IsBlocking(defend) && dodge.Get<bool>("touchable")));
        // Do not switch a local defender to spectator advice after they already chose.
        if (!localChoice && ui.Field("playerState")!.Get<int>("selectedIndex") == 1) return;
        if (!localChoice)
        {
            if (_observerSummary == null || _observerSummary.Root.Get<bool>("isDisposed")
                || _observerParent != ui.Pointer)
            {
                _observerSummary?.Dispose();
                _observerSummary = new ChoiceVisual(ui);
                _observerParent = ui.Pointer;
            }
            var scale = Plugin.UiScale.Value;
            _observerSummary.Show(advice.Value, scale, (ui.Get<float>("width") - 360 * scale) / 2,
                ui.Get<float>("height") - 220 * scale);
            return;
        }
        EnsureBadges(defend, dodge);
        // Clamp the badge scale to the existing gap; never move the actual buttons.
        var badgeScale = Math.Min(Plugin.UiScale.Value,
            Math.Max(180f, Math.Abs(dodge.Get<float>("x") - defend.Get<float>("x")) - 12f) / 240f);
        _defBadge!.Show(advice.Value.DefendQuick, advice.Value.Recommendation == RecommendedAction.Defend,
            badgeScale, -76f * badgeScale - 70f, visualOnly: true);
        _dodgeBadge!.Show(advice.Value.DodgeQuick, advice.Value.Recommendation == RecommendedAction.Dodge,
            badgeScale, -76f * badgeScale - 70f, visualOnly: true);
        _adviceShowing = true;
        if (_lastAdvice != input.ToString())
        {
            _lastAdvice = input.ToString();
            Plugin.Logger.LogInfo($"[전투 추천 표시] {_lastAdvice} / {advice.Value.Defend.Replace('\n', ' ')} / {advice.Value.Dodge.Replace('\n', ' ')}");
        }
    }

    private static void EnsureBadges(RuntimeObject first, RuntimeObject second, float width = 240, int fontSize = 23)
    {
        if (_defButton?.Pointer == first.Pointer && _dodgeButton?.Pointer == second.Pointer
            && _defBadge != null && !_defBadge.Panel.Get<bool>("isDisposed")
            && _dodgeBadge != null && !_dodgeBadge.Panel.Get<bool>("isDisposed")) return;
        _defBadge?.Dispose();
        _dodgeBadge?.Dispose();
        _defButton = first; _dodgeButton = second;
        _defBadge = new AdviceBadge(first, width, fontSize: fontSize);
        _dodgeBadge = new AdviceBadge(second, width, fontSize: fontSize);
    }

    private static void UpdateEncounter(RuntimeObject fight, RuntimeObject ui, int mapType)
    {
        var launch = ui.Field("com_LaunchPK");
        if (!Visible(launch) || ui.Field("playerState")!.Get<int>("selectedIndex") != 0) return;
        var attack = launch!.Field("btn_PK");
        var leave = launch.Field("btn_Leave");
        // Forced encounters, spectators and hidden/disabled buttons are not choices.
        if (!Visible(attack) || !Visible(leave) || !attack!.Get<bool>("touchable")
            || !leave!.Get<bool>("touchable") || attack.Get<bool>("grayed") || leave.Get<bool>("grayed")) return;
        var enemy = fight.Get("defenderData");
        if (enemy?.Get<int>("characterType") != 2 || !Visible(launch.Field("txt_HP"))) return;
        var player = fight.Get("attackerData")?.Get("Property");
        var enemyProperty = enemy.Get("Property");
        if (player == null || enemyProperty == null) return;
        // Enemy numbers are the exact public labels used by the official encounter screen.
        int? Stat(string name) => int.TryParse(launch.Field(name)!.Get("text")!.String(),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        var hp = Stat("txt_HP"); var atk = Stat("txt_ATK"); var def = Stat("txt_DEF");
        if (hp == null || atk == null || def == null) return;
        var counter = enemyProperty.Field("Counter");
        if (counter == null) return;
        var selfBonus = PublicCombatEffects.Read(fight.Get("attackerData"), "encounter.self", enemy);
        var enemyBonus = PublicCombatEffects.Read(enemy, "encounter.enemy", fight.Get("attackerData"));
        if (selfBonus == null || enemyBonus == null) return;
        var input = new EncounterInput(mapType, player.Field("HP")!.Get<int>("Value"),
            player.Field("ATK")!.Get<int>("Value"), player.Field("DEF")!.Get<int>("Value"),
            hp.Value, atk.Value, def.Value, counter.Get<bool>("Value"))
            { Modifiers = selfBonus.Value, EnemyModifiers = enemyBonus.Value };
        if (_encounterInput != input)
        {
            _encounterInput = input;
            _encounterResult = EncounterAdvisor.Calculate(input);
        }
        if (_encounterResult is not { } advice) return;
        EnsureBadges(attack, leave, width: 280, fontSize: 21);
        var scale = Math.Min(Plugin.UiScale.Value,
            Math.Max(180f, Math.Abs(attack.Get<float>("x") - leave.Get<float>("x")) - 12f) / 280f);
        _defBadge!.Show(advice.Quick, advice.Attack == true, scale, -86f * scale, visualOnly: true, guide: false);
        _dodgeBadge!.Show(string.Empty, advice.Attack == false, scale, -86f * scale,
            visualOnly: true, guide: false);
        _adviceShowing = true;
    }

    private static void UpdateHover()
    {
        if (!_adviceShowing || _root == null) return;
        var hit = PointerPath().FirstOrDefault(item => item.Pointer == _defButton?.Pointer || item.Pointer == _dodgeButton?.Pointer);
        _defBadge?.Hover(hit != null && hit.Pointer == _defButton?.Pointer);
        _dodgeBadge?.Hover(hit != null && hit.Pointer == _dodgeButton?.Pointer);
    }

    internal static RuntimeObject NewLabel(RuntimeObject parent, string text, int size,
        int align = 1, bool rich = false, bool dark = false)
    {
        var label = RuntimeObject.New(rich ? RuntimeObject.FindClass("FairyGUI", "GRichTextField") : _textClass);
        label.Set("touchable", false);
        label.Set("autoSize", 0);
        label.Set("text", ModText.Text(text));
        StyleText(label, size, align);
        NativeUi.InheritText(label, dark);
        parent.Call("AddChild", label);
        return label;
    }

    // Mod-owned labels only. Preserve native typography; size/alignment belong to the layout.
    internal static void StyleText(RuntimeObject label, int size, int align = 1)
    {
        var format = label.Get("textFormat")!;
        format.SetField("size", size);
        format.SetField("align", align);
        label.Set("textFormat", format);
        NativeUi.OutlineText(label);
    }

    internal static void SetAdvice(RuntimeObject label, string text)
    {
        if (!_iconsResolved)
        {
            _iconsResolved = true;
            try
            {
                var package = RuntimeObject.FindClass("FairyGUI", "UIPackage");
                _attackIcon = RuntimeObject.StaticCall(package, "GetItemURL", "Common", "Com_Icon_Atk")?.String();
                _defenseIcon = RuntimeObject.StaticCall(package, "GetItemURL", "Common", "Com_Icon_Def")?.String();
                _hpIcon = RuntimeObject.StaticCall(package, "GetItemURL", "Common", "Com_Icon_Hp")?.String();
                Plugin.Logger.LogInfo($"[추천 아이콘] 공격={_attackIcon != null}, 방어={_defenseIcon != null}, 체력={_hpIcon != null}");
            }
            catch (Exception ex)
            {
                // Icon availability must never disable advice after a game asset update.
                Plugin.Logger.LogWarning($"[추천 아이콘] 기본 기호로 대체: {ex.Message}");
            }
        }
        var size = label.Get("textFormat")!.Field("size")!.Value<int>();
        label.Set("text", AdviceText.Format(text, size, _attackIcon, _defenseIcon, _hpIcon));
    }

    private static void HideAdvice(bool keepDice = false)
    {
        CardUi.Hide(keepDice);
        _observerSummary?.Hide();
        _adviceShowing = false;
        _defBadge?.Hide();
        _dodgeBadge?.Hide();
    }

    private static void ClearAdvice()
    {
        HideAdvice(); CardUi.Clear(); RemainingHpUi.Clear();
        _defBadge?.Dispose(); _defBadge = null;
        _dodgeBadge?.Dispose(); _dodgeBadge = null;
        _observerSummary?.Dispose(); _observerSummary = null;
    }

    internal static void Stop()
    {
        try { UpdateNotificationUi.Clear(); }
        catch (Exception ex) { Plugin.Diagnostics.Error("UpdateNotification.cleanup", ex); }
        HomeAvailable = false;
        SetSettingsModal(false);
        Dispose(_menu); _menu = null;
        ClearAdvice();
    }

    internal static void Dispose(RuntimeObject? obj)
    {
        if (obj != null && !obj.Get<bool>("isDisposed")) obj.Call("Dispose");
    }
}
