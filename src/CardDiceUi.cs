using UnityEngine;

namespace BetterAstralParty;

internal static class CardDiceUi
{
    private static RuntimeObject? _panel, _title, _state, _status;
    private static readonly List<(RuntimeObject Disc, RuntimeObject Number, RuntimeObject Comparison, RuntimeObject Empty)> Rows = new();
    private static (PreRollCombat Input, CardBonus? Card, bool Hover, bool Unsupported, int FixedDie, int Language, int Role, int? FinalDamage)? _key;
    private static float _shownAt, _hideAt = -1, _holdUntil;
    private static float _x, _y, _scale = 1;
    private static RuntimeObject? _resolvedUi, _attacker, _defender;
    private static PreRollCombat _resolvedInput;
    private const float Width = CardDiceLayout.Width, Height = CardDiceLayout.Height;

    internal static void Show(RuntimeObject ui, RuntimeObject attackPoint,
        PreRollCombat input, CardBonus? card, bool hover, bool unsupported, int fixedDie = 0, int? finalDamage = null)
    {
        if (!Plugin.KoMinimum.Value) { Clear(); return; }
        if (fixedDie == 0) _resolvedUi = _attacker = _defender = null; // New card phase/hover owns the display.
        if (_key is { } old && old.Language != ModText.Revision) Clear(keepObservation: true);
        if (_panel == null || _panel.Get<bool>("isDisposed") || _panel.Get("parent")?.Pointer != ui.Pointer
            || Rows.Any(row => row.Disc.Get("texture") == null))
        {
            Clear(keepObservation: true);
            _panel = NativeUi.Component(ui, Width, Height); _panel.Set("touchable", false);
            _panel.Set("visible", false);
            _title = NativeUi.Label(_panel, "", 8, 6, Width - 16, 28, 17, dark: true);
            _state = NativeUi.Label(_panel, "", 8, 34, Width - 16, 24, 15, dark: true);
            _status = NativeUi.Label(_panel, "", 8, 198, Width - 16, 28, 16, dark: true);
            for (var row = 0; row < 2; row++)
            {
                NativeUi.Label(_panel, ModText.Text(row == 0 ? "공격자" : "방어자"),
                    row * CardDiceLayout.ColumnWidth, 166, CardDiceLayout.ColumnWidth, 28, 18, dark: true);
                // No Fight_Com_Point controllers/groups/transitions: they can hide cloned artwork.
                // Standalone native images keep the circles visible even when no KO pair exists.
                var point = NativeUi.Component(_panel, 160, 160);
                point.Set("touchable", false);
                var disc = NativeUi.Create("Fight", row == 0 ? "骰子底P1" : "骰子底P3")!;
                disc.Set("touchable", false); point.Call("AddChild", disc);
                if (row == 1) TintArtwork(disc, Color.cyan);
                // Fight_Com_Point's separate FightDiceBG image (a90c4x), without its controllers.
                var diamond = NativeUi.Create("Fight", "修改为 90-90")!;
                diamond.Set("touchable", false); point.Call("AddChild", diamond);
                diamond.Call("SetXY", 35f, 35f);
                diamond.Set("alpha", 0.25f); // Decoration only; keep the disc and glyphs fully opaque.
                point.Call("SetScale", 0.625f, 0.625f);
                NativeUi.Position(point, CardDiceLayout.Row(row), 62, 0.625f);
                var number = NativeUi.Label(point, "", 76, 30, 56, 100, 70, dark: true);
                var comparison = NativeUi.Label(point, "", 38, 30, 36, 100, 36, dark: true);
                var empty = NativeUi.Label(point, "", 20, 30, 120, 100, 60, dark: true);
                foreach (var label in new[] { number, comparison, empty })
                {
                    label.Set("verticalAlign", 1);
                    label.Set("singleLine", true);
                }
                Rows.Add((disc, number, comparison, empty));
            }
        }
        var role = ui.Field("playerState")!.Get<int>("selectedIndex");
        var survival = role == 1;
        var key = (input, card, hover, unsupported, fixedDie, ModText.Revision, role, finalDamage);
        if (_key != key)
        {
            if (fixedDie > 0 && (_key == null || _key.Value.FixedDie != fixedDie))
                _holdUntil = Time.unscaledTime + CardDiceLayout.ResolvedHold;
            if (fixedDie == 0) _holdUntil = 0;
            _key = key;
            // All roles use the publicly revealed roll. Zero means it is still unknown.
            var thresholdDie = fixedDie;
            var pair = unsupported ? (KoPair?)null : survival
                ? CardKoMinimum.Survival(input, card, thresholdDie, possible: true)
                : CardKoMinimum.Calculate(input, card, thresholdDie, possible: true);
            var damage = pair == null ? null : CardKoMinimum.OutcomeRange(input, card, thresholdDie);
            // Supplied only by RemainingHpUi after its public final-number readiness gate.
            if (finalDamage is >= 0) damage = ((long)finalDamage.Value, (long)finalDamage.Value);
            var status = CardDiceLayout.Status(pair, damage, input.Hp, survival);
            var unconditional = status is "KO 불가" or "확정 KO";
            _title!.Set("text", ModText.Text(CardDiceLayout.Title(survival)));
            _state!.Set("text", ModText.Text(fixedDie > 0 ? "" : hover ? "카드 사용 후" : "현재 상태"));
            _status!.Set("text", ModText.Text(status));
            for (var index = 0; index < Rows.Count; index++)
            {
                var row = Rows[index];
                var content = CardDiceLayout.Content(pair, thresholdDie, index, survival, unconditional, input.Modifiers.DefenseDice);
                // Number font follows the live dice; symbols use normal UI typography/fallback.
                NativeUi.CopyTextFormat(row.Number, attackPoint.Field("txt_Point")!);
                var color = index == 0 ? Color.red : Color.cyan;
                foreach (var label in new[] { row.Number, row.Comparison, row.Empty })
                {
                    var format = label.Get("textFormat")!;
                    format.SetField("color", Color.black);
                    format.SetField("outlineColor", color);
                    // Native dice use negative tracking; keep our single-glyph cells centred.
                    format.SetField("letterSpacing", 0);
                    if (label == row.Empty) format.SetField("size", 60);
                    label.Set("textFormat", format);
                }
                row.Number.Set("text", content.Die > 0 ? content.Die.ToString() : "");
                row.Comparison.Set("text", content.Symbol);
                row.Empty.Set("text", content.Empty);
                // Never hide the disc or its parent on an unavailable threshold.
            }
            if (Plugin.Diagnostics.IsRecording)
                Plugin.Diagnostics.State("koMinimum.content", $"role={role}; revealed={fixedDie}; preview={hover}; rows={Rows.Count}; supported={pair != null}; reason={(unsupported ? "card-effect" : pair == null ? "public-input" : "none")}; status={status}; metadataOnly=True");

        }
        var scaleUi = Math.Max(0.1f, Math.Min(Plugin.UiScale.Value, ui.Get<float>("height") / 1080f));
        var position = CardDiceLayout.Place(ui.Get<float>("width"), ui.Get<float>("height"),
            Width * scaleUi, Height * scaleUi);
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("koMinimum.layout",
                $"screen={ui.Get<float>("width"):F0}x{ui.Get<float>("height"):F0}; scale={scaleUi:F2}; "
                + (position is { } p ? $"shown={p.X:F0},{p.Y:F0}" : "hidden=no-space"));
        if (position == null) { Hide(); return; }
        _scale = scaleUi; _x = position.Value.X; _y = position.Value.Y;
        if (!_panel.Get<bool>("visible")) _shownAt = Time.unscaledTime;
        _hideAt = -1;
        _panel.Set("visible", true);
        Animate();
    }

    private static void TintArtwork(RuntimeObject image, Color color)
    {
        // Native filter recolours baked pixels; vertex tint alone cannot turn cyan into red.
        // Owned images only. Keep shared textures and all native UI instances untouched.
        var filter = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "ColorFilter"));
        filter.Call("Tint", color, 1f);
        image.Set("filter", filter);
    }

    internal static void BindResolved(RuntimeObject ui, RuntimeObject attacker, RuntimeObject defender, PreRollCombat input)
    {
        if (!Plugin.KoMinimum.Value) { Clear(); return; }
        _resolvedUi = ui; _attacker = attacker; _defender = defender; _resolvedInput = input;
    }

    internal static void ShowFinal(RuntimeObject ui, RuntimeObject point, int hp, int damage,
        HitModifiers modifiers, int die)
    {
        if (_resolvedUi?.Pointer != ui.Pointer || _key == null || die is < 1 or > 6) return;
        Show(ui, point, _resolvedInput with { Hp = hp, Modifiers = modifiers }, null, false, false, die, damage);
        _holdUntil = Time.unscaledTime + CardDiceLayout.ResolvedHold;
    }

    // Same visible-number polling as the damage indicator. The roll is written before
    // the movie ends; only group_Point/number visibility reveals it publicly.
    internal static void PollResolved()
    {
        if (_resolvedUi == null) return;
        if (!Plugin.KoMinimum.Value || !GameUi.Visible(_resolvedUi))
        { Clear(); return; }
        if (!GameUi.Visible(_attacker) || !GameUi.Visible(_defender)
            || _resolvedUi.Field("step")!.Get<int>("selectedIndex") != 5)
        { Hide(); return; }
        var attack = _attacker!.Field("com_Attack")!;
        var defense = _defender!.Field("com_Defend")!;
        var point = attack.Field("com_Point")!;
        var number = point.Field("txt_Point")!;
        int.TryParse(number.Get("text")?.String(), out var die);
        if (CombatAdvisor.IsDodge(defense.Field("changeState")!.Get<int>("selectedIndex"),
            defense.Field("showResult")!.Get<int>("selectedIndex")))
        { Clear(); return; } // Defense-only conditions must not linger after choosing dodge.
        if (!CardKoMinimum.RevealedDie(attack.Field("showPoint")!.Get<int>("selectedIndex"),
            GameUi.Visible(number), die))
        { Hide(); return; } // Retain the last revealed condition briefly; never read totals as dice.
        var input = _resolvedInput with {
            AttackMin = _attacker.Field("minATK")!.Value<int>(),
            AttackMax = _attacker.Field("maxATK")!.Value<int>(),
            DefenseMin = _defender.Field("minDEF")!.Value<int>(),
            DefenseMax = _defender.Field("maxDEF")!.Value<int>()
        };
        Show(_resolvedUi, point, input, null, false, false, die);
    }

    internal static void Animate()
    {
        if (_panel == null || _panel.Get<bool>("isDisposed") || !_panel.Get<bool>("visible")) return;
        if (!Plugin.KoMinimum.Value)
        { Clear(); return; }
        var now = Time.unscaledTime;
        var alpha = CardDiceLayout.Alpha(now, _shownAt, _hideAt);
        _panel.Set("alpha", alpha);
        _panel.Call("SetScale", _scale, _scale);
        _panel.Call("SetXY", _x, _y + (1 - alpha) * 8 * _scale);
        if (_hideAt >= 0 && alpha <= 0) _panel.Set("visible", false);
    }
    internal static void Hide()
    {
        if (_panel == null || _panel.Get<bool>("isDisposed") || !_panel.Get<bool>("visible") || _hideAt >= 0) return;
        _hideAt = Math.Max(Time.unscaledTime, _holdUntil);
    }
    internal static void Clear(bool keepObservation = false)
    {
        if (!keepObservation) _resolvedUi = _attacker = _defender = null;
        _hideAt = -1; _holdUntil = 0;
        GameUi.Dispose(_panel); _panel = _title = _state = _status = null; _key = null; Rows.Clear();
    }
}
