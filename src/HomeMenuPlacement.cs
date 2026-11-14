using System.Globalization;
using UnityEngine;

namespace BetterAstralParty;

// Issue #18 (user-chosen 2026-11-14, drafts v7 and v8 in artifacts/cc/home-button-drafts): the home Mod Settings button is
// moved in the game's own background-adjust mode (HomePanel's BGset controller, opened by the move square Btn_BGSet). While
// that mode shows, the button stays fully opaque with its title hidden, a white rim and a move disc, the whole button is the
// drag area, and it is drawn under the mode's hints and buttons (Com_Tip, raised to the top), so a drag passes under them.
// The hints get soft dark bands, Text_DragTip names the button too, and the row becomes [모드 설정 위치 초기화][배경 초기화]
// [취소][확인]: the game's Reset, renamed, still resets only the background, and the new button (a private copy of it) puts the
// Mod Settings button back at its first place. Confirm keeps the place; Cancel or any other way out returns to the place the
// mode started with. The place is an offset from the default in home units, kept in the settings file. Every native change
// is undone when the mode ends, on this feature's failure and when the home menu is cleared.
internal static class HomeMenuPlacement
{
    internal const string Feature = "HomeMenuPlacement";
    private const float RowGap = 28f, HintGap = 56f, PlateWidth = 343f, PlateHeight = 166f, PlateRadius = 12f;
    private const int TitleSize = 34;
    private static readonly Color Band = new(15 / 255f, 10 / 255f, 30 / 255f);

    private sealed class Mode
    {
        internal RuntimeObject Home = null!, Menu = null!;
        internal RuntimeObject? Reset, Cancel, Confirm, Clone;
        internal readonly List<Action> Undo = new();
        internal readonly List<RuntimeObject> Owned = new();
        internal (float X, float Y) Saved;
        internal bool Confirmed;
    }

    private static Mode? _mode;
    private static (float X, float Y) _pending, _stored;
    private static string? _parsedFrom;
    private static (Vector2 Pointer, (float X, float Y) Offset)? _drag;
    private static RuntimeObject? _pressedTip;
    private static RuntimeObject? _bgset;
    private static IntPtr _bgsetHome;

    internal static bool Active => _mode != null;

    // The offset from the default place: the one being dragged while the mode shows, else the saved one.
    internal static (float X, float Y) Offset => _mode != null ? _pending : Stored();

    private static (float X, float Y) Stored()
    {
        var text = Plugin.HomeMenuPosition.Value;
        if (!ReferenceEquals(text, _parsedFrom)) { _parsedFrom = text; _stored = Parse(text); }
        return _stored;
    }

    internal static (float X, float Y) Parse(string? text)
    {
        var parts = (text ?? "").Split(',');
        return parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && float.IsFinite(x) && float.IsFinite(y)
            ? (x, y) : (0f, 0f);
    }

    internal static string Format((float X, float Y) offset) =>
        Math.Abs(offset.X) < 0.5f && Math.Abs(offset.Y) < 0.5f ? ""
            : string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#}", offset.X, offset.Y);

    // Every frame from GameUi.FollowHomeMenu, before the button is placed: enters and leaves the mode, moves a drag and reads
    // the row's clicks. `x` and `y` are the button's default place in home units. True while the mode owns the button.
    internal static bool Tick(RuntimeObject home, RuntimeObject menu, RuntimeObject button, float x, float y)
    {
        if (!Compatibility.Allowed(Feature)) return false;
        try
        {
            if (_bgsetHome != home.Pointer) { _bgset = home.Field("BGset"); _bgsetHome = home.Pointer; }
            var open = _bgset!.Get<int>("selectedIndex") == 1;
            if (_mode != null && (_mode.Home.Pointer != home.Pointer || _mode.Menu.Pointer != menu.Pointer)) Leave(false);
            if (_mode == null)
            {
                if (open) Enter(home, menu, button);
                return _mode != null;
            }
            ObserveRow();
            if (!open) { Leave(_mode.Confirmed); return false; }
            if (_drag is { } drag)
            {
                if (Input.GetMouseButton(0))
                {
                    var (px, py) = GameUi.PointerIn(home);
                    _pending = (drag.Offset.X + px - drag.Pointer.x, drag.Offset.Y + py - drag.Pointer.y);
                }
                else _drag = null;
            }
            // Inside the home: an offset saved on a wider screen comes back into view here, where the anchor is at rest.
            var scale = menu.Get<float>("scaleX");
            _pending = (Math.Clamp(_pending.X, -x, Math.Max(-x, home.Get<float>("width") - menu.Get<float>("width") * scale - x)),
                Math.Clamp(_pending.Y, -y, Math.Max(-y, home.Get<float>("height") - menu.Get<float>("height") * scale - y)));
            return true;
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); return false; }
    }

    // From GameUi.HandleMenuInput while the mode shows: a press on the drawn plate starts a drag.
    internal static void Press(RuntimeObject home, RuntimeObject menu, RuntimeObject plate)
    {
        if (_mode == null || !Input.GetMouseButtonDown(0)) return;
        try
        {
            if (!GameUi.PointerPath().Any(item => item.Pointer == menu.Pointer) || !OnPlate(plate)) return;
            var (px, py) = GameUi.PointerIn(home);
            _drag = (new Vector2(px, py), _pending);
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // The drawn plate, in its own (tilted) coordinates: the button's click area matches what is drawn (user-requested
    // 2026-11-14, after a click on the tilted lower edge missed the button).
    internal static bool OnPlate(RuntimeObject plate)
    {
        var (px, py) = GameUi.PointerIn(plate);
        return ShapeRaster.RoundRect(px, py, 0f, 0f, PlateWidth, PlateHeight, PlateRadius) >= 0.5f;
    }

    // FairyGUI fires the row's clicks in its LateUpdate, after this tick: a release over the button the press began on is the
    // click the game is about to handle, read here while the row still shows.
    private static void ObserveRow()
    {
        var mode = _mode!;
        if (Input.GetMouseButtonDown(0)) _pressedTip = RowButton(mode);
        if (!Input.GetMouseButtonUp(0)) return;
        var released = RowButton(mode);
        if (released != null && released.Pointer == _pressedTip?.Pointer)
        {
            if (released.Pointer == mode.Confirm?.Pointer) mode.Confirmed = true;
            else if (released.Pointer == mode.Cancel?.Pointer) mode.Confirmed = false;
            else if (released.Pointer == mode.Clone?.Pointer) { _pending = (0f, 0f); _drag = null; }
            Plugin.Diagnostics.Write("homeMenu.place click=" + (released.Pointer == mode.Clone?.Pointer ? "reset" : released.Pointer == mode.Confirm?.Pointer ? "confirm" : "cancel"));
        }
        _pressedTip = null;
    }

    private static RuntimeObject? RowButton(Mode mode) =>
        GameUi.PointerPath().FirstOrDefault(item => item.Pointer == mode.Confirm?.Pointer || item.Pointer == mode.Cancel?.Pointer || item.Pointer == mode.Clone?.Pointer);

    internal static void Clear()
    {
        _drag = null; _pressedTip = null;
        if (_mode != null) Leave(false);
        _bgset = null; _bgsetHome = IntPtr.Zero;
    }

    private static void Enter(RuntimeObject home, RuntimeObject menu, RuntimeObject button)
    {
        var mode = _mode = new Mode { Home = home, Menu = menu, Saved = Stored() };
        _pending = mode.Saved; _drag = null; _pressedTip = null;
        try
        {
            var tip = home.Field("Com_Tip") ?? throw new MissingFieldException("UIHomePanel", "Com_Tip");
            Raise(mode, home, menu, tip);
            Mark(mode, menu, button);
            var center = Row(mode, home, tip);
            Hints(mode, tip, center);
            Plugin.Diagnostics.Write(FormattableString.Invariant($"homeMenu.place enter offset={mode.Saved.X:0},{mode.Saved.Y:0}"));
        }
        catch { Leave(false); throw; }
    }

    private static void Leave(bool keep)
    {
        var mode = _mode;
        _mode = null; _drag = null; _pressedTip = null;
        if (mode == null) return;
        if (keep) Plugin.HomeMenuPosition.Value = Format(_pending);
        for (var i = mode.Undo.Count - 1; i >= 0; i--)
        {
            try { mode.Undo[i](); }
            catch (Exception ex) { Plugin.Diagnostics.Error("HomeMenuPlacement.restore", ex); }
        }
        foreach (var owned in mode.Owned)
        {
            try { GameUi.Dispose(owned); }
            catch (Exception ex) { Plugin.Diagnostics.Error("HomeMenuPlacement.dispose", ex); }
        }
        Plugin.Diagnostics.Write(FormattableString.Invariant($"homeMenu.place leave {(keep ? "kept" : "reverted")} offset={(keep ? _pending : mode.Saved).X:0},{(keep ? _pending : mode.Saved).Y:0}"));
    }

    private static bool Live(RuntimeObject obj) => !obj.Get<bool>("isDisposed");

    // Com_Tip (the hints and the row) to the top of the home and the button right under it: above the background and the
    // home's own buttons, under the row it may be dragged across.
    private static void Raise(Mode mode, RuntimeObject home, RuntimeObject menu, RuntimeObject tip)
    {
        var layer = tip;
        while (layer.Get("parent") is { } parent && parent.Pointer != home.Pointer) layer = parent;
        var layerIndex = home.Call("GetChildIndex", layer)!.Value<int>();
        var menuIndex = home.Call("GetChildIndex", menu)!.Value<int>();
        var top = home.Get<int>("numChildren") - 1;
        home.Call("SetChildIndex", layer, top);
        home.Call("SetChildIndex", menu, top - 1);
        mode.Undo.Add(() =>
        {
            if (!Live(home)) return;
            if (Live(layer)) home.Call("SetChildIndex", layer, layerIndex);
            if (Live(menu)) home.Call("SetChildIndex", menu, menuIndex);
        });
    }

    // The button as a thing to move: no tilt, title hidden, the whole button the hit area, a white rim and a move disc.
    private static void Mark(Mode mode, RuntimeObject menu, RuntimeObject button)
    {
        var opaque = menu.Get<bool>("opaque");
        var title = button.Call("GetChild", "n8");
        menu.Set("opaque", true);
        button.Set("touchable", false);
        button.Call("GetController", "button")?.Set("selectedIndex", 0);
        title?.Set("visible", false);
        mode.Undo.Add(() =>
        {
            if (Live(menu)) menu.Set("opaque", opaque);
            if (Live(button)) button.Set("touchable", true);
            if (title != null && Live(title)) title.Set("visible", true);
        });
        var marks = NativeUi.Component(menu, PlateWidth, PlateHeight);
        mode.Owned.Add(marks);
        marks.Set("touchable", false);
        menu.Call("SetChildIndex", marks, menu.Call("GetChildIndex", button)!.Value<int>() + 1);
        SmoothShape.Create(marks, -5f, -5f, PlateWidth + 10f, PlateHeight + 10f, new Color(1f, 1f, 1f, 0f), 16f, 16f, 16f, 16f, 4f, new Color(1f, 1f, 1f, 0.9f));
        float cx = PlateWidth / 2f, cy = PlateHeight / 2f;
        SmoothShape.Create(marks, cx - 32f, cy - 32f, 64f, 64f, new Color(1f, 1f, 1f, 0.31f), 32f, 32f, 32f, 32f, 3f, new Color(1f, 1f, 1f, 0.82f));
        // Draft A's move glyph: a cross of rounded bars with an arrowhead at each end.
        SmoothShape.Create(marks, cx - 15f, cy - 2.3f, 30f, 4.6f, Color.white, 2.3f, 2.3f, 2.3f, 2.3f);
        SmoothShape.Create(marks, cx - 2.3f, cy - 15f, 4.6f, 30f, Color.white, 2.3f, 2.3f, 2.3f, 2.3f);
        for (var turn = 0; turn < 4; turn++)
        {
            var head = SmoothShape.MaskImage("homeMove.head", 64, () => ShapeRaster.Arrowhead(64, 40, 0));
            if (head == null) break; // Smooth shapes failed: the cross alone.
            marks.Call("AddChild", head);
            head.Call("SetSize", 20f, 20f);
            head.Call("SetPivot", 0.5f, 0.5f, true);
            var angle = turn * 90f;
            head.Call("SetXY", cx + 12.6f * MathF.Cos(angle * MathF.PI / 180f), cy + 12.6f * MathF.Sin(angle * MathF.PI / 180f));
            head.Set("rotation", angle);
        }
    }

    // [모드 설정 위치 초기화][배경 초기화][취소][확인] at one title size, centred where the game's row was. Returns that centre.
    private static float Row(Mode mode, RuntimeObject home, RuntimeObject tip)
    {
        var reset = mode.Reset = tip.Call("GetChild", "Btn_Reset") ?? throw new MissingFieldException("UIHome_Com_TipBGSet", "Btn_Reset");
        var cancel = mode.Cancel = tip.Call("GetChild", "Btn_Cancel") ?? throw new MissingFieldException("UIHome_Com_TipBGSet", "Btn_Cancel");
        var confirm = mode.Confirm = tip.Call("GetChild", "Btn_Confirm") ?? throw new MissingFieldException("UIHome_Com_TipBGSet", "Btn_Confirm");
        var natives = new[] { reset, cancel, confirm };
        var left = natives.Min(button => button.Get<float>("x"));
        var center = (left + natives.Max(button => button.Get<float>("x") + button.Get<float>("width"))) / 2f;
        var url = reset.Get("resourceURL")?.String() ?? throw new MissingFieldException("GObject", "resourceURL");
        var clone = mode.Clone = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "UIPackage"), "CreateObjectFromURL", url)
            ?? throw new TypeLoadException("Btn_Reset copy unavailable");
        mode.Owned.Add(clone);
        tip.Call("AddChild", clone);
        // The copy shows the reset icon: its Type controller (UIHero_Button_Set.Type) at the page the game's Reset uses.
        (clone.Field("Type") ?? throw new MissingFieldException("UIHero_Button_Set", "Type"))
            .Set("selectedIndex", (reset.Field("Type") ?? throw new MissingFieldException("UIHero_Button_Set", "Type")).Get<int>("selectedIndex"));
        clone.Call("SetXY", reset.Get<float>("x"), reset.Get<float>("y"));
        clone.Call("SetSize", reset.Get<float>("width"), reset.Get<float>("height"));
        var row = new[] { clone, reset, cancel, confirm };
        var texts = new[] { ModText.Text("모드 설정 위치 초기화"), ModText.Text("배경 초기화"), null, null };
        var widths = new float[row.Length];
        var natural = new float[row.Length];
        for (var i = 0; i < row.Length; i++)
        {
            var button = row[i];
            var title = button.Call("GetChild", "title") ?? throw new MissingFieldException("Hero_Button_Set", "title");
            if (button.Pointer != clone.Pointer) Remember(mode, button, title);
            if (texts[i] != null) button.Set("title", texts[i]!);
            var format = title.Get("textFormat")!;
            format.SetField("size", TitleSize);
            title.Set("textFormat", format);
            ModFont.Track(title);
            // Measured wide, before the shrink-to-fit title can scale it; the width relation keeps the title's margins.
            var width = button.Get<float>("width");
            button.Call("SetSize", 2000f, button.Get<float>("height"));
            natural[i] = title.Get<float>("textWidth");
            var margins = button.Get<float>("width") - title.Get<float>("width");
            widths[i] = Math.Max(width, margins + natural[i] + 14f);
            button.Call("SetSize", width, button.Get<float>("height"));
        }
        // Too wide for the screen (long English labels): the titles shrink to fit by FairyGUI's own shrink-to-fit.
        var available = home.Get<float>("width") - 80f;
        var total = widths.Sum() + RowGap * (row.Length - 1);
        if (total > available)
        {
            var fit = (available - RowGap * (row.Length - 1)) / widths.Sum();
            for (var i = 0; i < row.Length; i++) widths[i] *= fit;
            total = available;
        }
        var x = center - total / 2f;
        for (var i = 0; i < row.Length; i++)
        {
            Stretch(row[i], widths[i]);
            row[i].Call("SetXY", x, row[i].Get<float>("y"));
            x += widths[i] + RowGap;
        }
        return center;
    }

    // Native state of one row button, put back when the mode ends.
    private static void Remember(Mode mode, RuntimeObject button, RuntimeObject title)
    {
        float x = button.Get<float>("x"), width = button.Get<float>("width"), height = button.Get<float>("height");
        var text = button.Get("title")?.String() ?? "";
        var size = title.Get("textFormat")!.Field("size")!.Value<int>();
        var frame = button.Call("GetChild", "n2");
        var frameSize = frame == null ? Vector2.zero : new Vector2(frame.Get<float>("width"), frame.Get<float>("height"));
        mode.Undo.Add(() =>
        {
            if (!Live(button)) return;
            button.Set("title", text);
            if (Live(title))
            {
                var format = title.Get("textFormat")!;
                format.SetField("size", size);
                title.Set("textFormat", format);
            }
            if (frame != null && Live(frame))
            {
                frame.Get("displayObject")!.Set("scale9Grid", new NullableRect());
                frame.Call("SetSize", frameSize.x, frameSize.y);
            }
            button.Call("SetSize", width, height);
            button.Call("SetXY", x, button.Get<float>("y"));
        });
    }

    // A wider button: its frame image (219 x 80, no grid of its own) stretched by a nine-slice grid that keeps its rounded
    // ends, as SmoothShape's images use Image.scale9Grid.
    private static void Stretch(RuntimeObject button, float width)
    {
        var frame = button.Call("GetChild", "n2");
        if (frame != null && Math.Abs(width - frame.Get<float>("width")) > 0.5f)
        {
            var texture = frame.Get("texture");
            float w = texture?.Get<int>("width") ?? 219, h = texture?.Get<int>("height") ?? 80;
            frame.Get("displayObject")!.Set("scale9Grid", new NullableRect(new Rect(24f, 24f, Math.Max(2f, w - 48f), Math.Max(2f, h - 48f))));
            frame.Call("SetSize", width, frame.Get<float>("height"));
        }
        button.Call("SetSize", width, button.Get<float>("height"));
    }

    // The drag hint names the button too; both hints sit on soft dark bands, centred over the row.
    private static void Hints(Mode mode, RuntimeObject tip, float center)
    {
        var drag = tip.Call("GetChild", "Text_DragTip") ?? throw new MissingFieldException("UIHome_Com_TipBGSet", "Text_DragTip");
        var scale = tip.Call("GetChild", "Text_ScaleTip") ?? throw new MissingFieldException("UIHome_Com_TipBGSet", "Text_ScaleTip");
        var text = drag.Get("text")?.String() ?? "";
        float dragX = drag.Get<float>("x"), dragWidth = drag.Get<float>("width"), scaleX = scale.Get<float>("x"), scaleWidth = scale.Get<float>("width");
        mode.Undo.Add(() =>
        {
            if (Live(drag)) { drag.Set("text", text); drag.Call("SetSize", dragWidth, drag.Get<float>("height")); drag.Call("SetXY", dragX, drag.Get<float>("y")); }
            if (Live(scale)) { scale.Call("SetSize", scaleWidth, scale.Get<float>("height")); scale.Call("SetXY", scaleX, scale.Get<float>("y")); }
        });
        drag.Set("text", ModText.Text("드래그해서 배경 위치 및 모드 설정 위치 조정"));
        ModFont.Track(drag);
        // Measured unshrunk; the drag hint is right-aligned and the scale hint left-aligned, as the game draws them.
        foreach (var label in new[] { drag, scale }) label.Call("SetSize", 2000f, label.Get<float>("height"));
        float dw = drag.Get<float>("textWidth"), sw = scale.Get<float>("textWidth");
        var start = center - (dw + HintGap + sw) / 2f;
        drag.Call("SetSize", dw + 8f, drag.Get<float>("height"));
        drag.Call("SetXY", start - 6f, drag.Get<float>("y"));
        scale.Call("SetSize", sw + 8f, scale.Get<float>("height"));
        scale.Call("SetXY", start + dw + HintGap - 2f, scale.Get<float>("y"));
        foreach (var (label, x, width) in new[] { (drag, start, dw), (scale, start + dw + HintGap, sw) })
        {
            var height = label.Get<float>("textHeight");
            var top = label.Get<float>("y") + 2f;
            // Three nested capsules stand in for the draft's blurred band.
            foreach (var (pad, alpha) in new[] { (12f, 0.16f), (7f, 0.2f), (2f, 0.3f) })
            {
                var band = SmoothShape.Create(tip, x - 10f - pad, top - pad, width + 20f + 2 * pad, height + 2 * pad,
                    new Color(Band.r, Band.g, Band.b, alpha), height / 2 + pad, height / 2 + pad, height / 2 + pad, height / 2 + pad);
                mode.Owned.Add(band);
                tip.Call("SetChildIndex", band, 0);
            }
        }
    }
}
