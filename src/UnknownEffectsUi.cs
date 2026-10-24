using UnityEngine;

namespace BetterAstralParty;

// One centred row "[native buff icons] 미반영 효과 [?]" for effects the combat model does not count, shared by the
// KO dice indicator and the damage indicator. Mod-owned and never touchable; icons are native Button_Buff clones.
internal sealed class UnknownEffectsRow
{
    internal const float IconSize = 26, Gap = 4, Height = 28, MarkSize = 22;
    private readonly RuntimeObject _label;
    private readonly List<RuntimeObject> _icons = new();
    private readonly RuntimeObject? _mark;
    private readonly float _width, _y;
    private (UnknownEffects Effects, int Language)? _shown;
    internal int[] Ids { get; private set; } = Array.Empty<int>();

    internal UnknownEffectsRow(RuntimeObject parent, float width, float y, bool help)
    {
        _width = width; _y = y;
        _label = NativeUi.Label(parent, "", 0, y, width, Height, 16, dark: true);
        _label.Set("verticalAlign", 1);
        for (var i = 0; i < 4; i++) _icons.Add(BuffIcon(parent, IconSize));
        if (help) { _mark = NativeUi.QuestionMark(parent, MarkSize); _mark.Set("visible", false); }
    }

    // Same native button the Combat Status row uses; only its icon loader is filled, never RefreshBuff.
    internal static RuntimeObject BuffIcon(RuntimeObject parent, float size)
    {
        var icon = NativeUi.Create("Common_Internal", "Button_Buff") ?? throw new InvalidOperationException("Required buff icon unavailable");
        icon.Set("touchable", false);
        icon.Set("visible", false);
        foreach (var name in new[] { "txt_Num", "txt_Round" }) icon.Field(name)?.Set("visible", false);
        var scale = size / Math.Max(icon.Get<float>("width"), icon.Get<float>("height"));
        icon.Call("SetScale", scale, scale);
        parent.Call("AddChild", icon);
        return icon;
    }

    internal static RuntimeObject? Config(int id) =>
        RuntimeObject.StaticCall(RuntimeObject.FindClass("UI", "UIHelper"), "GetBuffConfigure", id);

    internal static bool SetIcon(RuntimeObject icon, int id, float x, float y)
    {
        var url = Config(id)?.Get("Icon")?.String();
        icon.Set("visible", !string.IsNullOrEmpty(url));
        if (string.IsNullOrEmpty(url)) return false;
        icon.Field("loader_Icon")!.Set("url", url);
        NativeUi.Position(icon, x, y, icon.Get<float>("scaleX"));
        return true;
    }

    internal void Show(UnknownEffects unknown)
    {
        if (_shown == (unknown, ModText.Revision)) return;
        _shown = (unknown, ModText.Revision);
        Ids = unknown.Shown;
        var any = Ids.Length > 0;
        _label.Set("text", any ? ModText.Text("미반영 효과") : "");
        _label.Call("SetSize", _width, Height);
        var text = any ? _label.Get<float>("textWidth") + 4 : 0;
        var mark = any && _mark != null ? MarkSize + Gap : 0;
        var x = (_width - Ids.Length * (IconSize + Gap) - text - mark) / 2;
        for (var i = 0; i < _icons.Count; i++)
            if (i >= Ids.Length || !SetIcon(_icons[i], Ids[i], x + i * (IconSize + Gap), _y + 1)) _icons[i].Set("visible", false);
        var textX = x + Ids.Length * (IconSize + Gap);
        _label.Call("SetXY", textX, _y);
        _label.Call("SetSize", Math.Max(1, text), Height);
        if (_mark == null) return;
        _mark.Set("visible", any);
        _mark.Call("SetXY", textX + text + Gap, _y + (Height - MarkSize) / 2);
    }

    internal bool Contains(Vector2 point) =>
        Ids.Length > 0 && (NativeUi.Contains(_label, point) || NativeUi.Contains(_mark, point) || IconAt(point) != 0);

    internal int IconAt(Vector2 point)
    {
        for (var i = 0; i < Ids.Length && i < _icons.Count; i++)
            if (NativeUi.Contains(_icons[i], point)) return Ids[i];
        return 0;
    }
}

// Hover help under the KO dice indicator's uncounted-effects row: what the row means, each effect's native icon and
// name, and the hovered effect's native description. It stays open while the pointer is over the row or the help
// itself, and opens once by itself the first time uncounted effects appear (remembered in the settings file).
internal sealed class UnknownEffectsHelp
{
    private const float Width = 380, Padding = 14, ItemHeight = 30, HoverDelay = 0.15f, AutoSeconds = 4;
    private readonly RuntimeObject _panel, _title, _body, _detail;
    private readonly NativeUi.Surface _background;
    private readonly List<(RuntimeObject Icon, RuntimeObject Name)> _items = new();
    private (int[] Ids, int Detail, int Language)? _drawn;
    private float _hoverSince = -1, _autoUntil;

    internal UnknownEffectsHelp(RuntimeObject parent, float centreX, float y)
    {
        _panel = NativeUi.Component(parent, Width, 120);
        _panel.Set("touchable", false);
        _panel.Set("visible", false);
        _panel.Call("SetXY", centreX - Width / 2, y);
        _background = new NativeUi.Surface(_panel, Width, 120);
        NativeUi.QuestionMark(_panel, UnknownEffectsRow.MarkSize).Call("SetXY", Padding, Padding);
        _title = NativeUi.Label(_panel, "", Padding + 30, Padding - 3, Width - 2 * Padding - 30, 28, 18, dark: true);
        _body = NativeUi.Label(_panel, "", Padding, 46, Width - 2 * Padding, 40, 15, dark: true);
        _body.Set("autoSize", 2);
        for (var i = 0; i < 4; i++)
            _items.Add((UnknownEffectsRow.BuffIcon(_panel, 22), NativeUi.Label(_panel, "", 0, 0, Width - 2 * Padding - 30, 26, 15, dark: true)));
        // RefreshHyperlinkDesc takes a rich text field (the Combat Status tooltip's native txt_Desc is one).
        _detail = GameUi.NewLabel(_panel, "", 14, rich: true, dark: true);
        _detail.Call("SetXY", Padding, 0f);
        _detail.Call("SetSize", Width - 2 * Padding, 40f);
        _detail.Set("autoSize", 2);
    }

    // The help has its own session breaker: its failure hides only the help, never the dice indicator or the advice.
    private const string Feature = "UnknownEffectsHelp";

    internal void Tick(UnknownEffectsRow row)
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { TickCore(row); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Hide); }
    }

    private void TickCore(UnknownEffectsRow row)
    {
        var now = Time.unscaledTime;
        if (row.Ids.Length == 0) { _hoverSince = -1; _autoUntil = 0; Hide(); return; }
        if (!Plugin.UnknownHelpSeen.Value) { Plugin.UnknownHelpSeen.Value = true; _autoUntil = now + AutoSeconds; }
        var point = NativeUi.Pointer();
        // Only an open help keeps itself open; a hidden one still has its rectangle under a resting pointer.
        var overHelp = _panel.Get<bool>("visible") && NativeUi.Contains(_panel, point);
        var overRow = row.Contains(point);
        if (overRow || overHelp) { if (_hoverSince < 0) _hoverSince = now; } else _hoverSince = -1;
        var open = overHelp || overRow && now - _hoverSince >= HoverDelay || now < _autoUntil;
        if (!open) { Hide(); return; }
        var detail = overHelp ? HelpIconAt(row.Ids, point) : overRow ? row.IconAt(point) : 0;
        Draw(row.Ids, detail);
        _background.Refresh();
        _panel.Set("visible", true);
    }

    private int HelpIconAt(int[] ids, Vector2 point)
    {
        for (var i = 0; i < ids.Length && i < _items.Count; i++)
            if (NativeUi.Contains(_items[i].Icon, point) || NativeUi.Contains(_items[i].Name, point)) return ids[i];
        return 0;
    }

    private void Draw(int[] ids, int detail)
    {
        if (_drawn is { } drawn && drawn.Ids.SequenceEqual(ids) && drawn.Detail == detail && drawn.Language == ModText.Revision) return;
        _drawn = (ids, detail, ModText.Revision);
        _title.Set("text", ModText.Text("미반영 효과"));
        _body.Set("text", ModText.Text("이 효과는 아직 모드 계산에 들어가지 않습니다. 위 조건은 이 효과를 빼고 계산해서 실제 결과와 다를 수 있습니다."));
        var y = 46 + _body.Get<float>("textHeight") + 10;
        for (var i = 0; i < _items.Count; i++)
        {
            var (icon, name) = _items[i];
            var shown = i < ids.Length && UnknownEffectsRow.SetIcon(icon, ids[i], Padding, y + 2);
            name.Set("visible", shown);
            if (!shown) { icon.Set("visible", false); continue; }
            name.Set("text", Local(UnknownEffectsRow.Config(ids[i]), "NameId"));
            name.Call("SetXY", Padding + 30, y);
            y += ItemHeight;
        }
        var config = detail == 0 ? null : UnknownEffectsRow.Config(detail);
        _detail.Set("visible", config != null);
        if (config != null)
        {
            _detail.Call("SetXY", Padding, y + 4);
            // Native rich description (keywords, colours), same renderer as the Combat Status tooltip.
            RuntimeObject.StaticCall(RuntimeObject.FindClass("UI", "CommonUIManager"), "RefreshHyperlinkDesc",
                Local(config, "DescId"), _detail, BattleStatusUi.Difficulty);
            ModFont.Track(_detail);
            y += 8 + _detail.Get<float>("textHeight");
        }
        var height = y + Padding;
        _panel.Call("SetSize", Width, height);
        _background.Resize(Width, height);
    }

    private static string Local(RuntimeObject? config, string property)
    {
        var id = config?.Get<int>(property) ?? 0;
        return id <= 0 ? "" : RuntimeObject.StaticCall(RuntimeObject.FindClass("UI", "UIHelper"), "GetLocal", id,
            RuntimeObject.EnumValue("UI", "UIStringType", "Buff"))?.String() ?? "";
    }

    internal void Hide()
    {
        if (_panel.Get<bool>("visible")) _panel.Set("visible", false);
    }
}
