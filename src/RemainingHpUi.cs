using System.Globalization;
using UnityEngine;

namespace BetterAstralParty;

// A private native dice component. Never writes the game's HP, dice or result data.
internal static class RemainingHpUi
{
    private static RuntimeObject? _point, _parent;
    private static RuntimeObject? _title;
    private static IntPtr _pointContext;
    private static float _shownAt;
    // When the result step started while the figure was shown, or -1: the figure fades from then (AdviceText.DamageEnd).
    private static float _resultAt = -1;
    private static bool _showing, _lethal;
    private static (float Width, float Height, float Scale)? _layout;
    private static float _alpha = -1;
    // Fight_Com_Point's own size when created, and whether this hit already logged its view drifting from it.
    private static float _pointWidth, _pointHeight;
    private static bool _drifted;
    private static IntPtr _defender;
    private static int _hp, _step, _prediction = -1;
    private static HitModifiers _modifiers;
    private static bool _captured, _animated, _failed, _resultLogged;

    // decided: the server's fight data once both sides have chosen (ServerFight), else null.
    internal static void Update(RuntimeObject fight, RuntimeObject parent, RuntimeObject attack,
        RuntimeObject defense, int step, int hp, ServerFight? decided)
    {
        if (_failed) return;
        try
        {
            var defender = fight.Get("defenderData");
            if (defender == null) { ResetCapture(); return; }
            if (_parent?.Pointer != parent.Pointer || _defender != defender.Pointer || step < _step)
                ResetCapture();
            _parent = parent; _defender = defender.Pointer; _step = step;
            if (step == 6 && _showing && _resultAt < 0) _resultAt = Time.unscaledTime;
            if (step == 6 && !_resultLogged && _prediction >= 0
                && GameUi.Visible(defense.Field("txt_Life")) && hp is >= 0 and <= 10000)
            {
                _resultLogged = true;
                // HP loss is capped by death. It cannot verify exact overkill damage,
                // and a difference may include healing/other result-phase effects.
                // PlayDefenderDead displays max(1, HP) for monster attackers, even on KO.
                // Use the visible defeat caption only after results.
                var monsterDefeat = GameUi.Visible(parent.Field("txt_Failure"))
                    && fight.Get("attackerData")?.Get("characterType")?.Value<int>() == 2;
                // The native applied HP change: on a KO it may keep the overkill the HP label cannot show. It is read
                // for diagnostics only, so a failed read never hides the damage view.
                int? changeHp = null;
                if (Plugin.Diagnostics.IsRecording)
                    try { changeHp = defender.Get("Property")?.Field("ChangeHP")?.Get<int>("Value"); }
                    catch (Exception ex) { Plugin.Diagnostics.Error("RemainingHpUi.ChangeHP", ex); }
                Plugin.Diagnostics.CombatResult(_hp, _prediction, hp, _modifiers, monsterDefeat, changeHp);
            }
            if (step == 3 || CombatAdvisor.IsFinalHpPhase(step) && !_captured)
            {
                var bonus = PublicCombatEffects.Read(defender, "remainingHP", fight.Get("attackerData"));
                _captured = hp > 0 && bonus != null;
                _hp = hp; _modifiers = bonus ?? default;
            }
            // The server decides the hit once both sides have chosen; nothing before that is final.
            if (!CombatAdvisor.IsFinalHpPhase(step) || _prediction >= 0 || !_captured || decided is not { } server) return;
            var root = GameUi.Root;
            if (root == null) return;
            // Cards/skills can change public effects after the initial phase snapshot: the KO view and the model check use the latest.
            _modifiers = PublicCombatEffects.Read(defender, "remainingHP", fight.Get("attackerData")) ?? _modifiers;
            // The server's own HP change for this hit: every effect counted and overkill kept (the tutorial's copy of the
            // server builds it as Min(DEF - ATK, -1) and clamps only the new HP). Freeze it once.
            _prediction = Math.Max(0, -server.HpChange);
            // Record before creating/rendering optional UI: a rendering failure must not erase the numerical evidence.
            if (Plugin.Diagnostics.IsRecording) LogModel(server);
            var a = attack.Field("com_Point")!;
            // A failure in optional KO rendering must not disable the independent damage view/log.
            try { if (!server.Dodge) CardDiceUi.ShowFinal(parent, a, _hp, _prediction, _modifiers, server.AttackDie, server.DefenseDie); }
            catch (Exception ex) { Plugin.Diagnostics.Error("CardDiceUi.Final", ex); }
            if (!Plugin.FinalDamage.Value) return; // Retain public final evidence for the independent KO view.
            Ensure(root);
            RefreshArtwork(a);
            _point!.Call("GetTransition", "MaxPoint")!.Call("Stop", false, false);
            _point.Field("txt_max")!.Set("visible", false);
            _animated = false;
            _lethal = _prediction >= _hp;
            var color = _lethal ? new Color(1f, 0.12f, 0.2f) : new Color(0.8f, 0.85f, 0.9f);
            _point!.Field("FightDiceBG")!.Set("color", color);
            _point.Field("FightDiceBG")!.Set("grayed", !_lethal);
            foreach (var field in new[] { "txt_Point", "txt_max" })
            {
                var text = _point.Field(field)!;
                text.Set("text", _prediction.ToString(CultureInfo.InvariantCulture));
                var format = text.Get("textFormat")!;
                format.SetField("outlineColor", color);
                text.Set("textFormat", format);
            }
            _shownAt = Time.unscaledTime; _resultAt = -1;
            _layout = null; _alpha = -1; _drifted = false;
            _point.Set("visible", true);
            _showing = true;
            Animate();
            if (Plugin.Diagnostics.IsRecording)
            {
                Plugin.Diagnostics.State("damage.layout", $"x={_point.Get<float>("x"):F1}; y={_point.Get<float>("y"):F1}; "
                    + $"onStage={_point.Get<bool>("onStage")}; visible={GameUi.Visible(_point)}");
                Plugin.Diagnostics.State("damage", $"before={_hp}; atk={server.Attack}; def={server.Defense}; dodge={server.Dodge}; "
                    + $"modifiers={_modifiers}; damage={_prediction}; observedHP={hp}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("RemainingHpUi", ex);
            Plugin.Logger.LogWarning("[Damage] Indicator disabled for this session: " + ex.GetType().Name);
            Clear(); _failed = true; // Isolate this optional view from existing advice.
        }
    }

    // The damage model's figure from the server's totals, for combat-audit; the server's HP change is the answer it is
    // checked against. Totals the model does not accept are logged without a prediction line.
    private static void LogModel(ServerFight server)
    {
        var defense = server.Dodge ? server.DefenseDie : server.Defense; // A dodge is decided by the two dice (DefendDiceSuccess).
        var model = -1;
        try { model = CombatAdvisor.FinalDamage(_hp, server.Attack, defense, server.Dodge, server.AttackDie, _modifiers); }
        catch (ArgumentOutOfRangeException) { }
        if (model >= 0)
            Plugin.Diagnostics.CombatEvent($"prediction before={_hp}; atk={server.Attack}; def={defense}; attackDie={server.AttackDie}; "
                + $"dodge={server.Dodge}; damage={model}; modifiers={_modifiers}; "
                + "order=escape,base-minimum,additive,zero-clamp,on-hit,cap,HP-floor");
        Plugin.Diagnostics.CombatEvent($"damage.server before={_hp}; attackDie={server.AttackDie}; defenseDie={server.DefenseDie}; "
            + $"atk={server.Attack}; def={server.Defense}; dodge={server.Dodge}; incHp={server.HpChange}; model={model}; match={model == _prediction}");
    }

    private static void Ensure(RuntimeObject parent)
    {
        if (_point?.Get<bool>("isDisposed") == false
            && _point.Get("parent")?.Pointer == parent.Pointer
            && _pointContext == _parent?.Pointer) return;
        GameUi.Dispose(_point);
        _point = null;
        _pointContext = _parent?.Pointer ?? IntPtr.Zero;
        _point = NativeUi.Create("Fight", "Fight_Com_Point")!;
        _pointWidth = _point.Get<float>("width"); _pointHeight = _point.Get<float>("height");
        _point.Set("touchable", false);
        _point.Set("sortingOrder", BattleStatusLayout.RowInputOrder(5));
        foreach (var name in new[] { "PointChange", "MaxPoint", "ShowDiceEffect" })
        {
            var transition = _point.Call("GetTransition", name)!;
            transition.Call("SetAutoPlay", false, 1, 0f);
            transition.Call("Stop", false, false);
        }
        _point.Field("aMovie_Dice")!.Set("playing", false);
        foreach (var name in new[] { "aMovie_Dice", "graph_Effect", "txt_max", "Image_DiceEffect" })
            _point.Field(name)!.Set("visible", false);
        _point.Field("group_Point")!.Set("visible", true);
        _point.Field("FightDiceBG")!.Set("visible", true);
        _point.Field("txt_Point")!.Set("visible", true);
        parent.Call("AddChild", _point);
        _title = NativeUi.Label(_point, ModText.Text("피해량"), 0, _point.Get<float>("height"), _point.Get<float>("width"), 28, 22, dark: true);
    }

    private static void RefreshArtwork(RuntimeObject source)
    {
        // Number glyphs must follow the live, initialized dice font, not a generic text fallback.
        foreach (var name in new[] { "txt_Point", "txt_max" })
            NativeUi.CopyTextFormat(_point!.Field(name)!, source.Field(name)!);
        // A surviving GComponent does not prove its package textures survived a replay.
        // Rebind only our images to the current native dice; never dispose shared textures.
        _point!.Field("group_Point")!.Set("visible", true);
        _point.Field("group_Point")!.Set("alpha", 1f);
        for (var i = 0; i < _point.Get<int>("numChildren"); i++)
        {
            var child = _point.Call("GetChildAt", i)!;
            var image = child.Get("asImage");
            if (image == null) continue;
            var name = child.Field("name")!.String();
            var native = source.Call("GetChild", name)?.Get("asImage");
            var texture = native?.Get("texture");
            if (texture == null) continue;
            if (Plugin.Diagnostics.IsRecording)
                Plugin.Diagnostics.Write($"damage.artwork name={name}; visible={image.Get<bool>("visible")}; "
                    + $"alpha={image.Get<float>("alpha")}; oldTexture={image.Get("texture")?.Pointer}; sourceTexture={texture.Pointer}");
            image.Set("texture", texture);
            // Preserve this instance's controller/animation visibility (especially DiceEffect).
        }
        _point.Field("FightDiceBG")!.Set("visible", true);
        _point.Field("FightDiceBG")!.Set("alpha", 1f);
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.Write("damage.artwork refreshed from current dice");
    }

    internal static void Animate()
    {
        if (!_showing || _failed) return;
        try
        {
            var root = GameUi.Root;
            if (!Plugin.FinalDamage.Value) { ClearDisplay(); return; }
            if (GameUi.HomeAvailable || root == null
                || _point?.Get<bool>("isDisposed") != false) { Clear(); return; }
            var elapsed = Time.unscaledTime - _shownAt;
            var end = AdviceText.DamageEnd(_resultAt < 0 ? -1 : _resultAt - _shownAt);
            if (elapsed >= end)
            {
                _point.Set("visible", false);
                _showing = false;
                return;
            }
            var width = root.Get<float>("width");
            var height = root.Get<float>("height");
            // The view keeps the point's own size and the scale written here. In game (2026-11-06) a lethal figure was
            // drawn at about 0.08 of its size, as if the point had been sized to the screen: put both back and log once.
            float liveWidth = _point.Get<float>("width"), liveHeight = _point.Get<float>("height"), liveScale = _point.Get<float>("scaleX");
            if (_layout is { } last && (Math.Abs(liveWidth - _pointWidth) > .5f || Math.Abs(liveHeight - _pointHeight) > .5f
                || Math.Abs(liveScale - last.Scale) > .001f))
            {
                if (!_drifted && Plugin.Diagnostics.IsRecording)
                    Plugin.Diagnostics.Write($"damage.drift size={liveWidth:F1}x{liveHeight:F1}; own={_pointWidth:F1}x{_pointHeight:F1}; "
                        + $"scale={liveScale:F3}; written={last.Scale:F3}; root={width:F0}x{height:F0}; elapsed={elapsed:F2}");
                _drifted = true;
                if (liveWidth != _pointWidth || liveHeight != _pointHeight) _point.Call("SetSize", _pointWidth, _pointHeight);
                _layout = null;
            }
            var size = Math.Min(width, height) * 0.14f * Plugin.UiScale.Value;
            var scale = size / _pointWidth * (_lethal ? 1f : AdviceText.RecommendationScale(elapsed));
            var layout = (width, height, scale);
            if (_layout != layout)
            {
                _layout = layout;
                _point.Call("SetScale", scale, scale);
                NativeUi.Position(_point, (width - _pointWidth * scale) / 2, height * 0.24f - _pointHeight * scale / 2, scale);
            }
            var alpha = AdviceText.DamageAlpha(elapsed, end, _lethal);
            if (_alpha != alpha) { _alpha = alpha; _point.Set("alpha", alpha); }
            if (_lethal && !_animated)
            {
                _animated = true;
                NativeUi.Play(_point, "MaxPoint");
            }
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("DamageUi.Animate", ex);
            Clear(); _failed = true;
        }
    }

    internal static void ResetCapture()
    {
        _parent = null;
        _defender = IntPtr.Zero; _captured = _resultLogged = false;
        _step = _hp = 0; _modifiers = default; _prediction = -1;
    }

    // A language switch relabels the drawn number in place, so the current hit's final damage stays on screen.
    internal static void RefreshLanguage()
    {
        if (_point == null || _point.Get<bool>("isDisposed")) return;
        try { _title?.Set("text", ModText.Text("피해량")); }
        catch (Exception ex) { Plugin.Diagnostics.Error("RemainingHpUi.Language", ex); ClearDisplay(); }
    }

    internal static void ClearDisplay()
    {
        GameUi.Dispose(_point); _point = null; _title = null;
        _pointContext = IntPtr.Zero;
        _layout = null; _alpha = -1; _resultAt = -1;
        _showing = _animated = false;
    }

    internal static void Clear() { ClearDisplay(); ResetCapture(); }
}
