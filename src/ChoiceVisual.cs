using UnityEngine;

namespace BetterAstralParty;

// Defend/dodge advice for a defender the viewer does not control: spectated fights, and a player or a monster the local
// player attacks (user-chosen 2026-11-16, draft 나-3 of artifacts/cc/defense-advice-drafts, replacing a copy of the local
// buttons whose numbers showed only under the pointer). One 520 x 126 cream panel at the bottom centre with two cells, the
// game's own fight-header icons (Fight/防御图标 and 闪避图标, the pair the DEFENSE header swaps by the defender's choice),
// each choice's KO chance as its main figure (a percent above 0 % in the advice's warning red) and its damage under it, and
// the recommendation thumb on the recommended cell. The defender's name sits above it in the colour the game's choice tip
// gives it. Once the game announces the choice (FightWindow.ShowChoiceResult), the chosen cell gets a 선택 tag and the
// other dims. Same defender-benefit comparison as the local badges; it never predicts the defender's input.
// Sizes since 2026-11-16 (draft 가 of artifacts/cc/defense-advice-drafts/readability, the user finding the 400 x 96 panel
// hard to read in a real match on 2026.11.2-dev.14): no text below 16, the damage figure in ink at 22, the unchosen cell
// at 72 % with its icon in colour, and a 48 thumb.
internal sealed class ChoiceVisual
{
    internal const float Width = 520, Height = 126;
    private const float CellWidth = Width / 2, TextLeft = 84, TextRight = 14, ThumbSize = 48, DimAlpha = 0.72f;
    private static readonly Color Grey = new(80f / 255, 76f / 255, 70f / 255, 1f);
    private static readonly Color Rule = new(205f / 255, 200f / 255, 190f / 255, 1f), Warning = new(181f / 255, 22f / 255, 59f / 255, 1f);
    internal readonly RuntimeObject Root;
    private readonly RuntimeObject _parent, _name;
    private readonly NativeUi.Surface _surface;
    private readonly Cell _defend, _dodge;
    private RecommendedAction? _chosen, _last;
    private float _lastShown = -10, _shownAt, _freshAt;
    private bool _thumbSettled;
    private static bool _thumbMissing;
    private (VisibleAdvice Advice, RecommendedAction? Chosen, int Language)? _drawn;
    private string? _nameText;

    private sealed class Cell
    {
        internal readonly RuntimeObject Root, Icon, Title, KoLabel, Ko, DamageLabel, Damage, Tag, TagText, TagShape;
        internal RuntimeObject? Thumb;
        internal Cell(RuntimeObject parent, float left, string icon)
        {
            Root = NativeUi.Component(parent, CellWidth, Height);
            Root.Set("touchable", false);
            Root.Call("SetXY", left, 0f);
            // The fight header's icons are 120-unit items with their art trimmed inside; a 72 box shows it about 60 high.
            Icon = NativeUi.Create("Fight", icon)!;
            Icon.Set("touchable", false);
            Icon.Call("SetSize", 72f, 72f);
            Icon.Call("SetXY", 6f, 30f);
            Root.Call("AddChild", Icon);
            float Top(float centre, float box, int size) => centre - box / 2 - NoteUi.CentreLift * size;
            Title = NoteUi.Text(Root, "", TextLeft, Top(28, 30, 22), 140, 30, 22, NoteUi.Ink);
            KoLabel = NoteUi.Text(Root, "", TextLeft, Top(66, 24, 16), 120, 24, 16, Grey);
            Ko = NoteUi.Text(Root, "", TextLeft, Top(64, 40, 30), 100, 40, 30, NoteUi.Ink);
            DamageLabel = NoteUi.Text(Root, "", TextLeft, Top(100, 24, 16), 140, 24, 16, Grey);
            Damage = NoteUi.Text(Root, "", TextLeft, Top(99, 30, 22), 100, 30, 22, NoteUi.Ink);
            // A long figure such as ">99.99%" or English "Average damage 12~15" shrinks to the cell instead of leaving it.
            Ko.Set("autoSize", 3);
            Damage.Set("autoSize", 3);
            // The 선택 tag: a small light grey capsule like the notes', at the cell's top right.
            Tag = NativeUi.Component(Root, 60, 26);
            Tag.Set("touchable", false);
            TagShape = SmoothShape.Create(Tag, 0f, 0f, 60f, 26f, NoteUi.Grey, 13f, 13f, 13f, 13f, 2f, Color.black);
            TagText = NoteUi.Text(Tag, "", 0f, -NoteUi.CentreLift * 15, 200, 26, 15, NoteUi.Ink, 1);
            Tag.Set("visible", false);
        }

        // A label and its figure on one line: the figure starts after the label and shrinks to what is left of the cell.
        private static void Figure(RuntimeObject label, RuntimeObject figure, string text, float height, Color colour)
        {
            var x = TextLeft + label.Get<float>("textWidth") + 8;
            figure.Call("SetXY", x, figure.Get<float>("y"));
            figure.Call("SetSize", Math.Max(1f, CellWidth - TextRight - x), height);
            var format = figure.Get("textFormat")!;
            format.SetField("color", colour);
            figure.Set("textFormat", format);
            figure.Set("text", text);
        }

        internal void Draw(string title, string? damageLabel, string? damage, string? knockout, bool chosen, bool dimmed)
        {
            Title.Set("text", title);
            KoLabel.Set("text", knockout == null ? ModText.Text("회피 불가") : ModText.Text("전투불능"));
            Ko.Set("visible", knockout != null);
            if (knockout != null) Figure(KoLabel, Ko, knockout, 40f, knockout == "0%" ? NoteUi.Ink : Warning);
            DamageLabel.Set("text", damage == null ? "" : damageLabel ?? "");
            Damage.Set("visible", damage != null);
            if (damage != null) Figure(DamageLabel, Damage, damage, 30f, NoteUi.Ink);
            Tag.Set("visible", chosen);
            if (chosen)
            {
                TagText.Set("text", ModText.Korean ? "선택" : "Chosen");
                var width = TagText.Get<float>("textWidth") + 18;
                foreach (var part in new[] { Tag, TagText, TagShape }) part.Call("SetSize", width, 26f);
                Tag.Call("SetXY", CellWidth - 14 - width, 15f);
            }
            // The unchosen choice stays readable: lighter, its icon still in colour.
            Root.Set("alpha", dimmed ? DimAlpha : 1f);
        }
    }

    internal ChoiceVisual(RuntimeObject parent)
    {
        _parent = parent;
        Root = NativeUi.Component(parent, Width, Height);
        Root.Set("touchable", false);
        _surface = new NativeUi.Surface(Root, Width, Height);
        SmoothShape.Create(Root, CellWidth - 1, 16f, 2f, Height - 32, Rule, 0f, 0f, 0f, 0f);
        _defend = new Cell(Root, 0f, "防御图标");
        _dodge = new Cell(Root, CellWidth, "闪避图标");
        // Above the panel, in the template face with its outline, like the native banner's name.
        _name = NativeUi.Label(Root, "", 0f, -34f, Width, 28, 20, dark: true);
    }

    internal void Show(VisibleAdvice advice, float scale, float x, float y)
    {
        var now = Time.unscaledTime;
        var fresh = now - _lastShown > 0.35f;
        _lastShown = now;
        if (fresh) { _chosen = null; _nameText = null; _freshAt = now; }
        Root.Set("visible", true);
        _surface.Refresh();
        Root.Call("SetXY", x, y);
        Root.Call("SetScale", scale, scale);
        // FightWindow.ShowChoiceResult shows this banner group for 1.5 s with txt_Dodge or txt_Defend: the defender has chosen.
        if (_chosen == null && _parent.Field("group__audienceChoiceResult")?.Get<bool>("visible") == true)
            _chosen = _parent.Field("txt_Dodge")?.Get<bool>("visible") == true ? RecommendedAction.Dodge : RecommendedAction.Defend;
        // The tip's variables change when this fight's choice starts; read them for a while so an earlier fight's name never stays.
        if (now - _freshAt < 2f) ReadName();
        if (fresh || _last != advice.Recommendation) { _shownAt = now; _thumbSettled = false; }
        _last = advice.Recommendation;
        if (_drawn != (advice, _chosen, ModText.Revision))
        {
            _drawn = (advice, _chosen, ModText.Revision);
            _defend.Draw(ModText.Text("방어"), ModText.Text("평균 피해"), advice.DefendDamage, advice.DefendKnockout,
                _chosen == RecommendedAction.Defend, _chosen == RecommendedAction.Dodge);
            _dodge.Draw(ModText.Text("회피"), ModText.Text("실패 시 피해"), advice.DodgeDamage, advice.DodgeKnockout,
                _chosen == RecommendedAction.Dodge, _chosen == RecommendedAction.Defend);
        }
        Tick();
    }

    // RefreshDefendReadyChoice gives the choice tip the defender's name: the nickname for a player, the character name for a
    // monster, and its slot colour. Reading those variables names the defender exactly as the game does.
    private void ReadName()
    {
        var vars = _parent.Field("txt_ChoiceTip")?.Get("templateVars");
        if (vars == null || vars.Call("ContainsKey", "playerName")?.Value<bool>() != true) { _name.Set("visible", false); return; }
        var name = vars.Call("get_Item", "playerName")?.String() ?? "";
        if (name == _nameText) return;
        _nameText = name;
        _name.Set("text", name);
        _name.Set("visible", name.Length > 0);
        var hex = vars.Call("ContainsKey", "color")?.Value<bool>() == true ? vars.Call("get_Item", "color")?.String() : null;
        if (HomePortrait.Hex("#" + hex) is { } colour)
        {
            var format = _name.Get("textFormat")!;
            format.SetField("color", colour);
            _name.Set("textFormat", format);
        }
    }

    internal void Tick()
    {
        if (_thumbSettled || _last == null || Root.Get<bool>("isDisposed") || !Root.Get<bool>("visible")) return;
        // The recommended cell's thumb pops like the local badges' (AdviceBadge): the recommendation thumb at its top left.
        var cell = _last == RecommendedAction.Defend ? _defend : _dodge;
        var other = cell == _defend ? _dodge : _defend;
        other.Thumb?.Set("visible", false);
        if (cell.Thumb == null && !_thumbMissing) cell.Thumb = Thumb(cell.Root);
        if (cell.Thumb == null) { _thumbSettled = true; return; }
        var elapsed = Time.unscaledTime - _shownAt;
        var pop = AdviceText.RecommendationScale(elapsed);
        cell.Thumb.Set("visible", true);
        cell.Thumb.Call("SetScale", pop, pop);
        cell.Thumb.Set("rotation", elapsed < 0.5f ? -12f * (1 - elapsed / 0.5f) : 0f);
        _thumbSettled = elapsed >= 0.5f;
    }

    private static RuntimeObject? Thumb(RuntimeObject parent)
    {
        // Relic_Button_Item.txt_Recommend's thumb (Common_Internal/r0lyc7), as AdviceBadge uses it.
        var thumb = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "UIPackage"), "CreateObjectFromURL", "ui://1ov1i0v9r0lyc7");
        if (thumb == null) { _thumbMissing = true; return null; }
        var ratio = ThumbSize / Math.Max(thumb.Get<float>("width"), thumb.Get<float>("height"));
        thumb.Call("SetSize", thumb.Get<float>("width") * ratio, thumb.Get<float>("height") * ratio);
        thumb.Call("SetPivot", 0.5f, 0.5f, true);
        thumb.Set("touchable", false);
        parent.Call("AddChild", thumb);
        thumb.Call("SetXY", 24f, 14f);
        return thumb;
    }

    internal void Hide() { if (!Root.Get<bool>("isDisposed")) Root.Set("visible", false); }
    internal void Dispose() => GameUi.Dispose(Root);
}
