using System.Globalization;
using UnityEngine;

namespace BetterAstralParty;

// A private native dice component. Never writes the game's HP, dice or result data.
internal static class RemainingHpUi
{
    private static RuntimeObject? _point, _parent;
    private static IntPtr _pointContext;
    private static RuntimeObject? _fight, _attack, _defense;
    private static float _shownAt;
    private static bool _showing, _lethal;
    private static (float Width, float Height, float Scale)? _layout;
    private static float _alpha = -1;
    private static IntPtr _defender;
    private static int _hp, _die, _step, _prediction = -1;
    private static HitModifiers _modifiers;
    private static bool _captured, _animated, _failed, _sawValues, _valuesFinished, _resultLogged;

    internal static void Update(RuntimeObject fight, RuntimeObject parent, RuntimeObject attack,
        RuntimeObject defense, int step, int hp)
    {
        if (_failed) return;
        try
        {
            var defender = fight.Get("defenderData");
            if (defender == null) { ResetCapture(); return; }
            if (_parent?.Pointer != parent.Pointer || _defender != defender.Pointer || step < _step)
                ResetCapture();
            _parent = parent; _defender = defender.Pointer; _step = step;
            _fight = fight; _attack = attack; _defense = defense;
            if (step == 6 && !_resultLogged && _prediction >= 0
                && GameUi.Visible(defense.Field("txt_Life")) && hp is >= 0 and <= 10000)
            {
                _resultLogged = true;
                // HP loss is capped by death. It cannot verify exact overkill damage,
                // and a difference may include healing/other result-phase effects.
                // PlayDefenderDead displays max(1, HP) for monster attackers, even on KO.
                // Use the visible defeat caption only after results; never read future fight data.
                var monsterDefeat = GameUi.Visible(parent.Field("txt_Failure"))
                    && fight.Get("attackerData")?.Get("characterType")?.Value<int>() == 2;
                Plugin.Diagnostics.CombatResult(_hp, _prediction, hp, _modifiers, monsterDefeat);
            }
            var a = attack.Field("com_Point")!;
            var d = defense.Field("com_Point")!;
            if (step == 3 || CombatAdvisor.IsFinalHpPhase(step) && !_captured)
            {
                var bonus = PublicCombatEffects.Read(defender, "remainingHP", fight.Get("attackerData"));
                _captured = hp > 0 && bonus != null;
                _hp = hp; _modifiers = bonus ?? default;
            }
            if (!CombatAdvisor.IsFinalHpPhase(step) || _prediction >= 0) return;
            var attackState = attack.Field("showPoint")!.Get<int>("selectedIndex");
            // State 1 is the die; state 2 may be a final total that also happens to be <=6.
            if (attackState == 1 && Number(a.Field("txt_Point"), out var die) && die is >= 1 and <= 6)
                _die = die;
            // Observe the arithmetic panel even while a point/transition is hidden.
            // Otherwise a short Cut_out can finish before any eligible polling sample.
            var values = parent.Field("com_value");
            var valuesVisible = GameUi.Visible(values);
            _sawValues |= valuesVisible;
            _valuesFinished |= values?.Call("GetTransition", "Cut_out")?.Get<bool>("playing") == true
                || _sawValues && !valuesVisible;
            if (!_captured || !GameUi.Visible(a) || !GameUi.Visible(d)
                || attackState != 2
                || defense.Field("showPoint")!.Get<int>("selectedIndex") != 2
                || !Number(a.Field("txt_Point"), out var atk) || !Number(d.Field("txt_Point"), out var def))
            { return; }
            var dodgeResult = defense.Field("showResult")!.Get<int>("selectedIndex");
            var dodge = CombatAdvisor.IsDodge(defense.Field("changeState")!.Get<int>("selectedIndex"), dodgeResult);
            // Failure is announced before the native arithmetic replaces the raw die.
            if (dodgeResult == 2 && def != 0) return;
            if (dodge && _die == 0) return;
            // Dice_Show writes both final labels and starts PointChange, then waits
            // 800ms before HideFightValue. The paired pulses are the early signal;
            // showPoint=2 alone is not (it is also set before arithmetic begins).
            var escaped = dodge && def is >= 1 and <= 6 && (def > _die || def == 6 && _die == 6);
            if (!AdviceText.FinalDamageReady(_sawValues, _valuesFinished,
                a.Call("GetTransition", "PointChange")!.Get<bool>("playing"),
                d.Call("GetTransition", "PointChange")!.Get<bool>("playing"), escaped)) return;
            var root = GameUi.Root;
            if (root == null) return;
            // Cards/skills can change public effects after the initial phase snapshot.
            // Refresh at the visible final-number boundary, before freezing this hit.
            var finalModifiers = PublicCombatEffects.Read(defender, "remainingHP", fight.Get("attackerData"));
            if (finalModifiers == null) return;
            _modifiers = finalModifiers.Value;
            // Freeze this hit once; overkill remains damage, not capped HP loss.
            _prediction = CombatAdvisor.FinalDamage(_hp, atk, def, dodge, _die, _modifiers);
            // Record before creating/rendering optional UI: a rendering failure must
            // not erase the numerical evidence. Rolls are already visibly revealed.
            if (Plugin.Diagnostics.IsRecording)
                Plugin.Diagnostics.CombatEvent($"prediction before={_hp}; atk={atk}; def={def}; attackDie={_die}; "
                    + $"dodge={dodge}; damage={_prediction}; modifiers={_modifiers}; "
                    + "order=escape,base-minimum,additive,zero-clamp,on-hit,cap,HP-floor");
            // A failure in optional KO rendering must not disable the independent damage view/log.
            try { if (!dodge) CardDiceUi.ShowFinal(parent, a, _hp, _prediction, _modifiers, _die); }
            catch (Exception ex) { Plugin.Diagnostics.Error("CardDiceUi.Final", ex); }
            if (!Plugin.Enabled.Value) return; // Retain public final evidence for the independent KO view.
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
            _shownAt = Time.unscaledTime;
            _layout = null; _alpha = -1;
            _point.Set("visible", true);
            _showing = true;
            Animate();
            if (Plugin.Diagnostics.IsRecording)
            {
                Plugin.Diagnostics.State("damage.layout", $"x={_point.Get<float>("x"):F1}; y={_point.Get<float>("y"):F1}; "
                    + $"onStage={_point.Get<bool>("onStage")}; visible={GameUi.Visible(_point)}");
                Plugin.Diagnostics.State("damage", $"before={_hp}; atk={atk}; def={def}; dodge={dodge}; "
                    + $"modifiers={_modifiers}; damage={_prediction}; observedHP={hp}; arithmeticExited={_valuesFinished}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("RemainingHpUi", ex);
            Plugin.Logger.LogWarning("[Damage] Indicator disabled for this session: " + ex.GetType().Name);
            Clear(); _failed = true; // Isolate this optional view from existing advice.
        }
    }

    private static bool Number(RuntimeObject? text, out int number)
    {
        number = 0;
        return GameUi.Visible(text) && int.TryParse(text!.Get("text")!.String(),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
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
        NativeUi.Label(_point, ModText.Text("피해량"), 0, _point.Get<float>("height"), _point.Get<float>("width"), 28, 22, dark: true);
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
            if (GameUi.HomeAvailable || !Plugin.Enabled.Value || root == null
                || _point?.Get<bool>("isDisposed") != false) { Clear(); return; }
            var elapsed = Time.unscaledTime - _shownAt;
            if (elapsed >= AdviceText.DamageLifetime)
            {
                _point.Set("visible", false);
                _showing = false;
                return;
            }
            var width = root.Get<float>("width");
            var height = root.Get<float>("height");
            var size = Math.Min(width, height) * 0.14f * Plugin.UiScale.Value;
            var scale = size / _point.Get<float>("width") * (_lethal ? 1f : AdviceText.RecommendationScale(elapsed));
            var layout = (width, height, scale);
            if (_layout != layout)
            {
                _layout = layout;
                _point.Call("SetScale", scale, scale);
                NativeUi.Position(_point, (width - _point.Get<float>("width") * scale) / 2,
                    height * 0.24f - _point.Get<float>("height") * scale / 2, scale);
            }
            var alpha = AdviceText.DamageAlpha(elapsed, _lethal);
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
        _fight = _attack = _defense = null;
        _defender = IntPtr.Zero; _captured = _sawValues = _valuesFinished = false;
        _resultLogged = false;
        _step = _hp = _die = 0; _modifiers = default; _prediction = -1;
    }

    internal static void Clear()
    {
        GameUi.Dispose(_point); _point = null;
        _pointContext = IntPtr.Zero;
        _layout = null; _alpha = -1;
        _showing = _animated = false;
        ResetCapture();
    }

    // Only the pending final-number window needs frame-rate polling. The main
    // scene scan and advice calculations keep their existing 0.2s cadence.
    internal static void PollFinalPoints()
    {
        if (_failed || _prediction >= 0 || _step != 5 || (!Plugin.Enabled.Value && !Plugin.KoMinimum.Value)) return;
        try
        {
            if (!GameUi.Visible(_parent) || !GameUi.Visible(_attack) || !GameUi.Visible(_defense)
                || _fight == null || _fight.Get<bool>("isDisposed")) return;
            if (_parent!.Field("step")!.Get<int>("selectedIndex") != 5) return;
            Update(_fight, _parent, _attack!, _defense!, 5, _hp);
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("DamageUi.PollFinalPoints", ex);
            Clear(); _failed = true;
        }
    }
}
