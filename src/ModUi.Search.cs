using UnityEngine;

namespace BetterAstralParty;

// Settings search (user-chosen 2026-11-02, artifacts/cc/settings-search-drafts/1번-일치-박스.png): a white search row fixed
// above the list filters it as the player types, across both tabs, with each kept setting's category band and parent. Query
// words are marked yellow on names; a row found outside its name grows by a box holding its location tag (the help box's
// pin capsule) and the matching description sentence. Rows keep their controls: a search only moves and hides them and
// shows mod-owned marks and boxes, and an empty query puts the tab's list back.
internal static partial class ModUi
{
    // A top row grows 26 and an option row 24 for its box; a head gets a 26 line per option found outside its name.
    private const float BoxHeight = 22, TopBox = 26, ChildBox = 24, NoteLine = 26, NotePad = 2;
    private static readonly Color MarkFill = new(1f, 0.871f, 0.349f), TagMark = new(0.588f, 0.463f, 0.157f), TagFill = new(0.353f, 0.275f, 0.196f),
        BoxFill = new(1f, 0.992f, 0.965f), BoxLine = new(0.839f, 0.776f, 0.627f), SentenceText = new(0.439f, 0.388f, 0.306f),
        HintText = new(0.627f, 0.58f, 0.486f), IconGray = new(0.35f, 0.35f, 0.35f);
    private static RuntimeObject? _searchInput, _searchCount, _searchClear, _searchEmpty, _searchHint;
    private static string _query = "";
    private static string[] _words = Array.Empty<string>();
    private static MenuLayout.Arrangement _arrangement = MenuLayout.Default;
    private static int _searchRevision;
    private static readonly Dictionary<string, SettingsSearch.Hit> Hits = new();
    private static readonly Dictionary<string, RuntimeObject> RowButtons = new();
    // List items by the setting whose row they belong to, at their default-list y; BelowBox items sit under a row's box.
    private static readonly Dictionary<IntPtr, (RuntimeObject Item, string Action, float Y, bool BelowBox)> Placed = new();
    private static readonly List<RuntimeObject> SectionTabs = new();
    private static readonly List<RuntimeObject> Marks = new();
    private static int _marksUsed;
    private static readonly Dictionary<string, MatchBox> Boxes = new();
    private static readonly HashSet<IntPtr> CharPositions = new();

    private static bool Searching => _words.Length > 0;

    // Shown in the list: the current tab's settings, or a search's.
    private static bool Shown(string action) => Searching ? _arrangement.Rows.ContainsKey(action) : MenuLayout.GeneralControl(action) == _general;
    private static bool SectionShown(int section) => Searching ? _arrangement.Bands.ContainsKey(section) : MenuLayout.Sections[section].General == _general;
    private static bool TrayShown(string parent) => Searching
        ? _arrangement.Rows.ContainsKey(parent) && MenuLayout.Sections.Any(section => section.Actions.Any(action => MenuLayout.Parent(action) == parent && _arrangement.Rows.ContainsKey(action)))
        : MenuLayout.GeneralControl(parent) == _general;

    private static bool Typing() =>
        _searchInput != null && RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!.Get("focus")?.Pointer == _searchInput.Get("displayObject")!.Pointer;

    private static void ResetSearchState()
    {
        _searchInput = _searchCount = _searchClear = _searchEmpty = _searchHint = null;
        _query = ""; _words = Array.Empty<string>(); _arrangement = MenuLayout.Default; _searchRevision++;
        Hits.Clear(); RowButtons.Clear(); Placed.Clear(); SectionTabs.Clear(); Marks.Clear(); Boxes.Clear(); CharPositions.Clear();
        _marksUsed = 0;
    }

    // The search row: a top row's white with its black outline, the magnifier, the field, the count and the clear button.
    private static void CreateSearch()
    {
        var row = NativeUi.Component(_sheet!, MenuLayout.RowWidth, MenuLayout.SearchHeight);
        row.Call("SetXY", MenuLayout.SettingsX + MenuLayout.RowX, MenuLayout.SearchY);
        row.Set("opaque", true);
        Shape(row, 0, 0, MenuLayout.RowWidth, MenuLayout.SearchHeight, Color.white, 13f, 13f, 13f, 13f, 3f, Color.black);
        var lens = Shape(row, 14, 11, 15, 15, new Color(0, 0, 0, 0), 7.5f, 7.5f, 7.5f, 7.5f, 2.2f, IconGray);
        lens.Set("touchable", false);
        var handle = Shape(row, 26.5f, 25f, 7, 2.6f, IconGray, 1.3f, 1.3f, 1.3f, 1.3f);
        handle.Call("SetPivot", 0f, 0.5f, false);
        handle.Set("rotation", 45f);
        var input = _searchInput = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GTextInput"));
        row.Call("AddChild", input);
        input.Call("SetXY", 40f, 0f);
        input.Call("SetSize", MenuLayout.RowWidth - 40 - 104, MenuLayout.SearchHeight);
        input.Set("singleLine", true);
        GameUi.StyleText(input, 19, align: 0);
        NativeUi.InheritText(input, dark: false);
        Plain(input);
        input.Set("verticalAlign", 1);
        input.Set("maxLength", 30);
        input.Set("promptText", "[color=#A0947C]" + ModText.Text("이름·설명·위치로 찾기") + "[/color]");
        _searchCount = NativeUi.Label(row, "", MenuLayout.RowWidth - 104, 0, 62, MenuLayout.SearchHeight, 15);
        _searchCount.Set("singleLine", true);
        GameUi.StyleText(_searchCount, 15, align: 2);
        Plain(_searchCount, HintText);
        _searchCount.Set("verticalAlign", 1);
        // The library search's clear button: a dark disc with a white cross, shown while there is text.
        var clear = _searchClear = NativeUi.Component(row, 24, 24);
        clear.Call("SetXY", MenuLayout.RowWidth - 34f, (MenuLayout.SearchHeight - 24) / 2);
        clear.Set("opaque", true);
        Shape(clear, 0, 0, 24, 24, IconGray, 12f, 12f, 12f, 12f).Set("touchable", false);
        foreach (var angle in new[] { 45f, -45f })
        {
            var bar = Shape(clear, 5.5f, 10.75f, 13, 2.5f, Color.white, 1.25f, 1.25f, 1.25f, 1.25f);
            bar.Set("touchable", false);
            bar.Call("SetPivot", 0.5f, 0.5f, false);
            bar.Set("rotation", angle);
        }
        clear.Set("visible", false);
        Controls[row.Pointer] = (row, "Search");
        Controls[clear.Pointer] = (clear, "SearchClear");
        _searchEmpty = NativeUi.Label(_sheet!, "", MenuLayout.SettingsX, MenuLayout.SettingsY + 140, MenuLayout.RowWidth + 2 * MenuLayout.RowX, 30, 18);
        Plain(_searchEmpty);
        _searchHint = NativeUi.Label(_sheet!, ModText.Text("이름·설명·위치 글자로 찾습니다"), MenuLayout.SettingsX, MenuLayout.SettingsY + 172, MenuLayout.RowWidth + 2 * MenuLayout.RowX, 24, 15);
        Plain(_searchHint, HintText);
        foreach (var label in new[] { _searchEmpty, _searchHint }) { label.Set("verticalAlign", 1); label.Set("visible", false); }
    }

    // Records every list item at its default-list place; run once the window is built.
    private static void PlaceRows()
    {
        // Every control but the fixed ones sits in a row: switches, slider rows' hover areas and unavailable switches.
        foreach (var (button, action) in Controls.Values)
            if (!MenuLayout.FixedControl(action)) Place(button, action);
        foreach (var row in SettingRows.Values)
        {
            var action = Controls[row.Button.Pointer].Action;
            if (!MenuLayout.IsSlider(action)) RowButtons[action] = row.Button;
            foreach (var part in new[] { row.Fill, row.Track, row.Knob }) if (part != null) Place(part, action);
            if (row.Divider != null) Place(row.Divider, action, belowBox: true);
        }
        foreach (var action in MenuLayout.Sliders) { Place(Labels[action], action); Place(Labels[action + "Value"], action); }
        foreach (var (parts, action) in SliderBacks) foreach (var part in parts) Place(part, action);
        for (var i = 0; i < SectionHeaders.Count; i++)
        {
            // A General-tab band names its tab while a search mixes both tabs in one list.
            var tab = NativeUi.Label(SectionHeaders[i].View, ModText.Text("일반 설정"), MenuLayout.SectionBandX + MenuLayout.SectionBandWidth - 132,
                MenuLayout.SectionBandY, 120, MenuLayout.SectionBandHeight, 13);
            tab.Set("singleLine", true);
            GameUi.StyleText(tab, 13, align: 2);
            Plain(tab, HintText);
            tab.Set("verticalAlign", 1);
            tab.Set("visible", false);
            SectionTabs.Add(tab);
        }
    }

    private static void Place(RuntimeObject item, string action, bool belowBox = false)
    {
        var y = item.Get<float>("y");
        Placed[item.Pointer] = (item, action, y, belowBox);
        if (Searching) item.Set("y", PlacedY(action, y, belowBox));
    }

    private static float PlacedY(string action, float y, bool belowBox) =>
        _arrangement.Rows.TryGetValue(action, out var row) ? y + row - MenuLayout.Default.Y(action) + (belowBox ? _arrangement.Extra(action) : 0) : y;

    // Reads the field each frame, with the IME text being composed at the caret.
    private static void TickSearch()
    {
        if (_searchInput == null || _searchInput.Get<bool>("isDisposed")) return;
        var text = _searchInput.Get("text")?.String() ?? "";
        if (Typing() && Input.compositionString is { Length: > 0 } composing)
            text = text.Insert(Math.Clamp(_searchInput.Get<int>("caretPosition"), 0, text.Length), composing);
        if (text != _query) SetQuery(text, typed: true);
    }

    private static void SetQuery(string text, bool typed = false)
    {
        if (!typed) _searchInput?.Set("text", text);
        _query = text;
        _words = SettingsSearch.Words(text);
        Search();
        Relayout();
        Refresh();
        ShowMatches();
        _settingsScroll?.Call("ScrollTop", false);
        Plugin.Diagnostics.State("settingsSearch", Searching ? FormattableString.Invariant($"words={_words.Length}; found={Hits.Count}") : "off");
    }

    // Leaving a search for a tab or a fresh open: back to the tab's list without its own refresh.
    private static void EndSearch()
    {
        if (_query.Length == 0) return;
        _searchInput?.Set("text", "");
        _query = ""; _words = Array.Empty<string>();
        Search(); Relayout(); ShowMatches();
    }

    private static void Search()
    {
        Hits.Clear();
        _searchRevision++;
        if (!Searching) { _arrangement = MenuLayout.Default; return; }
        var beta = Plugin.UpdateChannel.Value == "Beta";
        foreach (var section in MenuLayout.Sections)
            foreach (var action in section.Actions)
            {
                if (action is "UpdateAuth" or "UpdateSignOut" && !beta) continue;
                var tag = MenuLayout.Subtitle(action);
                if (SettingsSearch.Match(_words, Names(action), tag == null ? null : ModText.Text(tag), Descriptions(action)) is { } hit) Hits[action] = hit;
            }
        // A found option keeps its parent (and a found chip its switch); a shown switch keeps all its options.
        var shown = new HashSet<string>(Hits.Keys);
        foreach (var action in Hits.Keys)
        {
            if (MenuLayout.SubParent(action) is { } head) shown.Add(head);
            if (MenuLayout.Parent(action) is { } parent) shown.Add(parent);
        }
        foreach (var section in MenuLayout.Sections)
            foreach (var action in section.Actions)
                if (MenuLayout.SubParent(action) is { } head && shown.Contains(head)) shown.Add(action);
        var grow = new Dictionary<string, float>(); var notes = new Dictionary<string, float>();
        foreach (var (action, hit) in Hits)
        {
            if (!hit.Boxed) continue;
            if (MenuLayout.SubParent(action) is { } head) notes[head] = notes.GetValueOrDefault(head, NotePad) + NoteLine;
            else grow[action] = MenuLayout.Parent(action) == null ? TopBox : ChildBox;
        }
        _arrangement = MenuLayout.Arrange(MenuLayout.Sections.Select((section, index) => (index, section.Actions.Where(shown.Contains).ToArray())), true, grow, notes);
    }

    // The name as the row shows it, and the setting's own name when they differ (a slider's label, the update row's role).
    private static string[] Names(string action)
    {
        var shown = MenuLayout.IsSlider(action) ? Labels[action].Get("text")?.String()
            : RowButtons.TryGetValue(action, out var button) ? button.Get("title")?.String() : null;
        return new[] { shown ?? "", ModText.Text(MenuLayout.Help(action).Title) }.Where(name => name.Length > 0).Distinct().ToArray();
    }

    // What the setting does, its current state's text and its choices.
    private static string[] Descriptions(string action)
    {
        var texts = new List<string> { ModText.Text(MenuLayout.Help(action).Body), ModText.Text(MenuLayout.StateHelp(action, ToggleEnabled(action), "")) };
        foreach (var option in MenuLayout.Options(action))
        {
            texts.Add(ModText.Text(MenuLayout.OptionLabel(action, option)));
            texts.Add(ModText.Text(MenuLayout.OptionHelp(action, option)));
        }
        return texts.Distinct().ToArray();
    }

    // Moves every list item by its row's shift and resizes rows, trays and bands for the current arrangement.
    private static void Relayout()
    {
        var arrangement = _arrangement;
        foreach (var (item, action, y, belowBox) in Placed.Values)
            if (!item.Get<bool>("isDisposed")) item.Set("y", PlacedY(action, y, belowBox));
        foreach (var row in SettingRows.Values)
        {
            var action = Controls[row.Button.Pointer].Action;
            if (row.Fill == null || MenuLayout.IsSlider(action) || !arrangement.Rows.ContainsKey(action)) continue;
            var height = MenuLayout.Head(action) ? MenuLayout.HeadHeight(arrangement, action) : MenuLayout.ToggleHeightFor(action) + arrangement.Extra(action);
            row.Fill.Call("SetSize", row.Fill.Get<float>("width"), height);
        }
        foreach (var (parts, action) in SliderBacks)
            foreach (var part in parts)
                if (arrangement.Rows.ContainsKey(action)) part.Call("SetSize", part.Get<float>("width"), MenuLayout.ToggleHeightFor(action) + arrangement.Extra(action));
        foreach (var (parts, parent) in Trays)
        {
            var last = MenuLayout.Sections.SelectMany(section => section.Actions).LastOrDefault(action => MenuLayout.Parent(action) == parent && arrangement.Rows.ContainsKey(action));
            if (last == null || !arrangement.Rows.ContainsKey(parent)) continue;
            foreach (var part in parts)
            {
                part.Set("y", MenuLayout.TrayY(arrangement, parent));
                part.Call("SetSize", part.Get<float>("width"), MenuLayout.TrayHeight(arrangement, parent, last));
            }
        }
        for (var i = 0; i < SectionHeaders.Count; i++)
        {
            SectionHeaders[i].View.Set("y", arrangement.Bands.TryGetValue(i, out var y) ? y : MenuLayout.SectionY(i));
            SectionTabs[i].Set("visible", Searching && MenuLayout.Sections[i].General);
        }
    }

    // Yellow marks on the query words of every found name, and the match boxes of the rows found outside their names.
    private static void ShowMatches()
    {
        _marksUsed = 0;
        foreach (var box in Boxes.Values) box.Hide();
        if (Searching)
        {
            var notes = new Dictionary<string, int>();
            foreach (var section in MenuLayout.Sections)
                foreach (var action in section.Actions)
                {
                    if (!Hits.TryGetValue(action, out var hit) || !_arrangement.Rows.ContainsKey(action)) continue;
                    var slider = MenuLayout.IsSlider(action);
                    // A slider whose feature failed hides its label and shows its gray unavailable switch, whose title carries the name.
                    var blocked = slider && SliderBlocked[action].Get<bool>("visible");
                    var owner = !slider ? RowButtons[action] : blocked ? SliderBlocked[action] : _settingsContent!;
                    var label = slider && !blocked ? Labels[action] : owner.Call("GetChild", "title")!;
                    var name = label.Get("text")?.String() ?? "";
                    MarkWords(owner, label, name, MarkFill);
                    if (!hit.Boxed) continue;
                    if (!Boxes.TryGetValue(action, out var box)) Boxes[action] = box = new MatchBox(_settingsContent!);
                    var tag = MenuLayout.Subtitle(action) is { } subtitle && hit.Tag ? ModText.Text(subtitle) : null;
                    if (MenuLayout.SubParent(action) is { } head)
                    {
                        // Under the head's option lines, one line each, led by the option's name.
                        var line = notes[head] = notes.GetValueOrDefault(head) + 1;
                        var lines = MenuLayout.Sections.SelectMany(s => s.Actions).Last(other => MenuLayout.SubParent(other) == head);
                        var top = _arrangement.Y(lines) + MenuLayout.LineHeight + NotePad + (line - 1) * NoteLine;
                        box.Show(MenuLayout.ChipX, top, MenuLayout.ChildX + MenuLayout.ChildWidth - 18 - MenuLayout.ChipX, name, tag, hit.Sentence);
                    }
                    else
                    {
                        var (x, width) = MenuLayout.RowBox(action);
                        box.Show(x + 14, _arrangement.Y(action) + MenuLayout.ToggleHeightFor(action) - 4, width - 28, null, tag, hit.Sentence);
                    }
                }
        }
        for (var i = _marksUsed; i < Marks.Count; i++) Marks[i].Set("visible", false);
        _searchCount?.Set("text", Searching ? ModText.SearchCount(Hits.Count) : "");
        _searchClear?.Set("visible", _query.Length > 0);
        var empty = Searching && Hits.Count == 0;
        _searchEmpty?.Set("text", empty ? ModText.SearchEmpty(_query.Trim()) : "");
        _searchEmpty?.Set("visible", empty);
        _searchHint?.Set("visible", empty);
    }

    // Marks the query words of a single-line label from the native glyph positions, behind the label in its parent.
    private static void MarkWords(RuntimeObject parent, RuntimeObject label, string text, Color color)
    {
        var ranges = SettingsSearch.Ranges(text, _words);
        if (ranges.Count == 0) return;
        var field = label.Get("displayObject")!;
        if (CharPositions.Add(field.Pointer)) field.Call("EnableCharPositionSupport");
        var positions = field.Get("charPositions");
        var count = positions?.Get<int>("Count") ?? 0;
        var size = label.Get("textFormat")!.Field<int>("size");
        var height = Math.Min(label.Get<float>("height"), size * 1.25f);
        var y = label.Get<float>("y") + (label.Get<float>("height") - height) / 2;
        foreach (var (start, end) in ranges)
        {
            if (end > count) continue;
            var first = positions!.Call("get_Item", start)!;
            var last = positions.Call("get_Item", end - 1)!;
            var left = first.Field<float>("offsetX");
            var right = last.Field<float>("offsetX") + last.Field<short>("width");
            if (right <= left) continue;
            var mark = _marksUsed < Marks.Count ? Marks[_marksUsed] : null;
            if (mark == null || mark.Get<bool>("isDisposed"))
            {
                // A plain component holds the shape: a native button's state change turns its image children white.
                mark = NativeUi.Component(parent, 10, 10);
                mark.Set("touchable", false);
                Shape(mark, 0, 0, 10, 10, color, 4f, 4f, 4f, 4f).Call("AddRelation", mark, RuntimeObject.EnumValue("FairyGUI", "RelationType", "Size"));
                if (_marksUsed < Marks.Count) Marks[_marksUsed] = mark; else Marks.Add(mark);
            }
            _marksUsed++;
            SmoothShape.Fill(mark.Call("GetChildAt", 0)!, color);
            // Out first: AddChildAt moves a child already in front of the label to after it.
            mark.Call("RemoveFromParent");
            parent.Call("AddChildAt", mark, parent.Call("GetChildIndex", label)!.Value<int>());
            mark.Call("SetSize", right - left + 4, height);
            mark.Call("SetXY", label.Get<float>("x") + left - 2, y);
            mark.Set("visible", true);
        }
    }

    // A row's match box: the location tag as the help box's pin capsule, then the description sentence cut to fit; an
    // option's line starts with its name and a small chevron.
    private sealed class MatchBox
    {
        private readonly RuntimeObject _root, _lead, _chevron, _fill, _tag, _tagFill, _tagText, _sentence;

        internal MatchBox(RuntimeObject parent)
        {
            _root = NativeUi.Component(parent, 400, BoxHeight);
            _root.Set("touchable", false);
            _lead = Text(_root, 15, LineText);
            _chevron = NativeUi.Component(_root, 8, 10);
            foreach (var angle in new[] { 40f, -40f })
            {
                // Both bars turn about their right ends on one tip.
                var bar = Shape(_chevron, 0, 4, 7, 2, SentenceText, 1f, 1f, 1f, 1f);
                bar.Call("SetPivot", 1f, 0.5f, false);
                bar.Set("rotation", angle);
            }
            _fill = Shape(_root, 0, 0, 100, BoxHeight, BoxFill, BoxHeight / 2, BoxHeight / 2, BoxHeight / 2, BoxHeight / 2, 1.5f, BoxLine);
            _tag = NativeUi.Component(_root, 100, 17);
            _tagFill = Shape(_tag, 0, 0, 100, 17, TagFill, 8.5f, 8.5f, 8.5f, 8.5f);
            var drop = Shape(_tag, 5.5f, 3.5f, 8, 8, new Color(0.91f, 0.278f, 0.29f), 4f, 4f, 4f, 0f);
            drop.Call("SetPivot", 0.5f, 0.5f, false);
            drop.Set("rotation", 45f);
            Shape(_tag, 8.25f, 6.25f, 2.5f, 2.5f, Color.white, 1.25f, 1.25f, 1.25f, 1.25f);
            _tagText = Text(_tag, 13, Color.white);
            _sentence = Text(_root, 13, SentenceText);
        }

        private static RuntimeObject Text(RuntimeObject parent, int size, Color color)
        {
            var label = NativeUi.Label(parent, "", 0, 0, 400, BoxHeight, size);
            label.Set("singleLine", true);
            label.Set("autoSize", 1); // Both: the width follows the text, so textWidth and the glyph positions are the text's own.
            GameUi.StyleText(label, size, align: 0);
            Plain(label, color);
            label.Set("verticalAlign", 1);
            return label;
        }

        internal void Hide() => _root.Set("visible", false);

        internal void Show(float x, float y, float width, string? lead, string? tag, string? sentence)
        {
            var at = 0f;
            _lead.Set("visible", lead != null); _chevron.Set("visible", lead != null);
            if (lead != null)
            {
                _lead.Set("text", lead);
                ModFont.Track(_lead);
                _lead.Call("SetXY", 0f, (BoxHeight - _lead.Get<float>("height")) / 2);
                at = _lead.Get<float>("textWidth") + 6;
                _chevron.Call("SetXY", at, (BoxHeight - 10) / 2);
                at += 14;
            }
            var inner = 8f;
            _tag.Set("visible", tag != null);
            if (tag != null)
            {
                _tagText.Set("text", tag);
                ModFont.Track(_tagText);
                var tagWidth = 19 + _tagText.Get<float>("textWidth") + 8;
                _tagText.Call("SetXY", 19f, (17 - _tagText.Get<float>("height")) / 2);
                _tagFill.Call("SetSize", tagWidth, 17f);
                _tag.Call("SetXY", at + inner, (BoxHeight - 17) / 2);
                MarkWords(_tag, _tagText, tag, TagMark);
                inner += tagWidth + (sentence != null ? 6 : 0);
            }
            _sentence.Set("visible", sentence != null);
            if (sentence != null)
            {
                _sentence.Set("text", sentence);
                ModFont.Track(_sentence);
                var room = width - at - inner - 8;
                var shown = SettingsSearch.Fit(sentence, _words, room, text => { _sentence.Set("text", text); return _sentence.Get<float>("textWidth"); });
                _sentence.Set("text", shown);
                _sentence.Call("SetXY", at + inner, (BoxHeight - _sentence.Get<float>("height")) / 2);
                MarkWords(_root, _sentence, shown, MarkFill);
                inner += Math.Min(room, _sentence.Get<float>("textWidth"));
            }
            _fill.Call("SetXY", at, 0f);
            _fill.Call("SetSize", inner + 8, BoxHeight);
            _root.Call("SetXY", x, y);
            _root.Set("visible", true);
        }
    }
}
