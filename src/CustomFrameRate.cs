using UnityEngine;

namespace BetterAstralParty;

// 사용자 정의 프레임 / Custom Frame Rate (user-requested 2026-11-19, draft 2나 of artifacts/cc/frame-custom-drafts; rules in
// .claude/rules/frame-rate.md). The game's 화면 설정 > 주사율 row offers 30/60/90/120 FPS (Settings table RefreshRates,
// SettingWindow.SelectFrame saves the chosen index and sets Application.targetFrameRate) or V-Sync (vSyncCount 1, which
// ignores the frame rate). BAP adds "사용자 정의" under the game's own list, a card to type 30-999, and while V-Sync is off it
// keeps Application.targetFrameRate at that number. Nothing the game saves is written: its index and V-Sync stay as the
// player chose them in its own list, so without BAP the game starts at its own choice. The game writes the frame rate at
// start (GameSettings.InitScreenSetting) and on its list (SelectFrame), so the number is put back whenever it differs.
// It runs in PvP too (user-decided 2026-11-19: a display setting with no game information), so it sits before the PvP gate.
internal static class CustomFrameRate
{
    internal const string Feature = "CustomFrameRate";
    private static readonly Color Pink = new(1f, 43f / 255, 145f / 255, 1f), Black = Color.black;
    private static readonly Color Field = new(30f / 255, 30f / 255, 30f / 255, 1f), Hover = new(60f / 255, 14f / 255, 38f / 255, 1f);
    private static readonly Color Line = new(90f / 255, 90f / 255, 90f / 255, 1f), HintGrey = new(190f / 255, 190f / 255, 190f / 255, 1f);
    private static readonly Color Error = new(1f, 120f / 255, 120f / 255, 1f), Clear0 = new(0f, 0f, 0f, 0f);
    private const float ItemHeight = 96f, Overlap = 20f, CardGap = 11f, CardHeight = 198f;
    private static bool _applied, _cardOpen, _error, _pressItem, _pressList, _pressInside;
    private static IntPtr _gameSettings;
    private static RuntimeObject? _combo, _window, _item, _itemHover, _card, _input, _ok, _hint, _face, _faceLabel, _title;
    private static float _titleAlpha = 1f;
    private static int _language = -1;
    // The frame the card opened: the release that opened it is still down there and is no press outside the card.
    private static int _openedFrame = -1;

    // Every frame, before the PvP gate.
    internal static void Apply()
    {
        if (!Plugin.CustomFrameOn.Value && !_applied) return;
        // The development frame test holds V-Sync and the frame rate off while it measures and puts them back itself.
        if (FrameProbe.HoldsFrameRate) return;
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            var want = CustomFrameRatePlan.Target(Plugin.CustomFrameOn.Value, Plugin.CustomFrame.Value, QualitySettings.vSyncCount);
            if (want > 0)
            {
                if (Application.targetFrameRate != want)
                {
                    Application.targetFrameRate = want;
                    Plugin.Diagnostics.State("customFrame", "applied " + want);
                }
                _applied = true;
            }
            else if (_applied) Restore(QualitySettings.vSyncCount != 0 ? "v-sync" : "off");
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // The game's own frame rate back (its saved index), also under V-Sync: the game's V-Sync switch never writes the frame
    // rate, so a number left there would come back when V-Sync goes off again.
    private static void Restore(string reason)
    {
        _applied = false;
        if (_gameSettings == IntPtr.Zero) _gameSettings = RuntimeObject.FindClass("Core", "GameSettings");
        Application.targetFrameRate = RuntimeObject.StaticCall(_gameSettings, "GetFrames")!.Value<int>();
        Plugin.Diagnostics.State("customFrame", "game value back (" + reason + ")");
    }

    // The settings window's parts, also before the PvP gate.
    internal static void TickUi()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { TickUiCore(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void TickUiCore()
    {
        var root = GameUi.Root;
        var window = root == null ? null : GameUi.Find(root, "SettingWindow", maxDepth: 1);
        var combo = window?.Get("contentPane")?.Field("com_Screen")?.Field("com_FrameRate");
        if (combo == null || !GameUi.Visible(combo)) { Dispose(); return; }
        if (combo.Pointer != _combo?.Pointer || _language != ModText.Revision) { Dispose(); _combo = combo; _window = window; _language = ModText.Revision; }
        var vSync = QualitySettings.vSyncCount != 0;
        if (vSync) _cardOpen = false;
        Popup(root!, combo);
        Card(window!, combo);
        Face(combo, Plugin.CustomFrameOn.Value && !vSync);
    }

    // "사용자 정의" under the game's list, inside its dropdown (so a press there keeps the popup and never reaches the list's
    // selection), on a black strip that continues the game's panel.
    private static void Popup(RuntimeObject root, RuntimeObject combo)
    {
        var dropdown = combo.Field("dropdown");
        if (dropdown == null || !GameUi.Visible(dropdown)) { _item?.Set("visible", false); _pressItem = _pressList = false; return; }
        var width = dropdown.Get<float>("width");
        var height = dropdown.Get<float>("height");
        var below = dropdown.Call("LocalToRoot", Vector2.zero, root)!.Value<Vector2>().y >= combo.Call("LocalToRoot", Vector2.zero, root)!.Value<Vector2>().y;
        if (_item == null || _item.Get<bool>("isDisposed") || _item.Get("parent")?.Pointer != dropdown.Pointer) BuildItem(dropdown, width);
        var item = _item!;
        item.Call("SetXY", 0f, below ? height - Overlap : -ItemHeight - 12f);
        item.Set("visible", true);
        var path = GameUi.PointerPath(refresh: true);
        var overItem = path.Any(hit => hit.Pointer == item.Pointer);
        _itemHover?.Set("visible", overItem);
        var list = combo.Field("_list");
        var overList = list != null && path.Any(hit => hit.Pointer == list.Pointer);
        if (Input.GetMouseButtonDown(0)) { _pressItem = overItem; _pressList = overList; }
        if (!Input.GetMouseButtonUp(0)) return;
        if (_pressItem && overItem)
        {
            root.Call("HidePopup");
            _cardOpen = true; _error = false; _openedFrame = Time.frameCount;
            Plugin.Diagnostics.State("customFrame.ui", "card open");
        }
        else if (_pressList && overList && Plugin.CustomFrameOn.Value)
        {
            // A choice in the game's own list ends the custom number; the game saves and applies that choice itself.
            Plugin.CustomFrameOn.Value = false;
            Plugin.Diagnostics.State("customFrame.ui", "game choice");
        }
        _pressItem = _pressList = false;
    }

    private static void BuildItem(RuntimeObject dropdown, float width)
    {
        GameUi.Dispose(_item);
        var item = _item = NativeUi.Component(dropdown, width, ItemHeight + Overlap + 12f);
        item.Set("opaque", true);
        // The strip: square where it meets the game's panel, round at its free end.
        SmoothShape.Create(item, 0, 0, width, ItemHeight + Overlap + 12f, Black, 0f, 0f, 44f, 44f);
        SmoothShape.Create(item, 60, Overlap + 3f, width - 120, 2, Line, 1f, 1f, 1f, 1f);
        var hover = _itemHover = SmoothShape.Create(item, 14, Overlap + 10f, width - 28, ItemHeight - 8, Hover, 36f, 36f, 36f, 36f);
        hover.Set("visible", false);
        Keyboard(item, 82, Overlap + 10f + (ItemHeight - 8) / 2 - 15);
        var label = NativeUi.Label(item, "사용자 정의", 140, Overlap + 6f, width - 170, ItemHeight, 40);
        ModUi.Plain(label, Pink);
        label.Set("verticalAlign", 1);
    }

    // A small keyboard: rounded outline and two rows of keys over a space bar.
    private static void Keyboard(RuntimeObject parent, float x, float y)
    {
        SmoothShape.Create(parent, x, y, 46, 30, Clear0, 6f, 6f, 6f, 6f, 3f, Pink);
        for (var row = 0; row < 2; row++)
            for (var i = 0; i < 5; i++) SmoothShape.Create(parent, x + 7 + i * 7, y + 8 + row * 7, 4, 4, Pink, 1f, 1f, 1f, 1f);
        SmoothShape.Create(parent, x + 12, y + 22, 22, 4, Pink, 1.5f, 1.5f, 1.5f, 1.5f);
    }

    // The card under the row: title, the field with FPS, OK, and one line for the range or the error.
    private static void Card(RuntimeObject window, RuntimeObject combo)
    {
        if (!_cardOpen) { if (_card != null && _card.Get<bool>("visible")) { _card.Set("visible", false); _input?.Set("text", ""); } return; }
        var pane = window.Get("contentPane")!;
        var root = GameUi.Root!;
        var width = combo.Get<float>("width");
        if (_card == null || _card.Get<bool>("isDisposed")) BuildCard(pane, width);
        var card = _card!;
        var top = combo.Call("LocalToRoot", new Vector2(0, combo.Get<float>("height") + CardGap), root)!.Value<Vector2>();
        var at = pane.Call("RootToLocal", top, root)!.Value<Vector2>();
        card.Call("SetXY", at.x, at.y);
        if (!card.Get<bool>("visible"))
        {
            card.Set("visible", true);
            pane.Call("SetChildIndex", card, pane.Get<int>("numChildren") - 1);
            _input!.Set("text", Plugin.CustomFrame.Value >= CustomFrameRatePlan.Min ? Plugin.CustomFrame.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            _input.Call("RequestFocus");
        }
        _hint!.Set("text", ModText.Text(_error ? "30~999 사이로 입력하세요" : "30~999 · Enter 적용"));
        ModUi.Plain(_hint, _error ? Error : HintGrey);
        var path = GameUi.PointerPath(refresh: true);
        var inside = path.Any(hit => hit.Pointer == card.Pointer);
        if (Input.GetMouseButtonDown(0)) _pressInside = inside;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Submit(); return; }
        if (!Input.GetMouseButtonUp(0) || Time.frameCount == _openedFrame) return;
        if (_pressInside && path.Any(hit => hit.Pointer == _ok!.Pointer)) Submit();
        else if (!_pressInside && !inside) { _cardOpen = false; Plugin.Diagnostics.State("customFrame.ui", "card closed"); }
    }

    private static void BuildCard(RuntimeObject pane, float width)
    {
        GameUi.Dispose(_card);
        var card = _card = NativeUi.Component(pane, width, CardHeight);
        card.Set("opaque", true);
        card.Set("visible", false);
        SmoothShape.Create(card, 0, 0, width, CardHeight, Black, 36f, 36f, 36f, 36f);
        var title = NativeUi.Label(card, "프레임 직접 입력", 40, 14, width - 80, 40, 28);
        GameUi.StyleText(title, 28, align: 0);
        ModUi.Plain(title, Color.white);
        title.Set("verticalAlign", 1);
        SmoothShape.Create(card, 36, 60, 314, 78, Field, 22f, 22f, 22f, 22f, 3f, Pink);
        var input = _input = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GTextInput"));
        card.Call("AddChild", input);
        input.Call("SetXY", 52f, 60f);
        input.Call("SetSize", 210f, 78f);
        input.Set("singleLine", true);
        GameUi.StyleText(input, 44, align: 1);
        NativeUi.InheritText(input, dark: false);
        ModUi.Plain(input, Pink);
        input.Set("verticalAlign", 1);
        input.Set("maxLength", 3);
        input.Set("restrict", "[0-9]");
        var fps = NativeUi.Label(card, "FPS", 262, 60, 80, 78, 28);
        ModUi.Plain(fps, new Color(1f, 150f / 255, 200f / 255, 1f));
        fps.Set("verticalAlign", 1);
        var ok = _ok = NativeUi.Component(card, 104, 70);
        ok.Call("SetXY", 366f, 64f);
        ok.Set("opaque", true);
        SmoothShape.Create(ok, 0, 0, 104, 70, Pink, 22f, 22f, 22f, 22f);
        var okLabel = NativeUi.Label(ok, "확인", 0, 0, 104, 70, 30);
        ModUi.Plain(okLabel, Black);
        okLabel.Set("verticalAlign", 1);
        var hint = _hint = NativeUi.Label(card, "", 20, 148, width - 40, 36, 21);
        hint.Set("verticalAlign", 1);
    }

    private static void Submit()
    {
        var value = CustomFrameRatePlan.Parse(_input?.Get("text")?.String());
        if (value == null) { _error = true; Plugin.Diagnostics.State("customFrame.ui", "rejected"); return; }
        Plugin.CustomFrame.Value = value.Value;
        Plugin.CustomFrameOn.Value = true;
        _cardOpen = false; _error = false;
        Plugin.Diagnostics.State("customFrame.ui", "set " + value.Value);
    }

    // While the number applies, the combo reads "사용자 정의 · N FPS": the game's title hides (alpha) and a capsule and label
    // of the same face show over it; the title comes back whenever the number does not apply.
    private static void Face(RuntimeObject combo, bool custom)
    {
        var title = combo.Call("GetTextField");
        if (!custom)
        {
            if (_face != null) _face.Set("visible", false);
            if (_title != null && !_title.Get<bool>("isDisposed") && _title.Get<float>("alpha") == 0f) _title.Set("alpha", _titleAlpha);
            return;
        }
        if (title == null) return;
        if (_title?.Pointer != title.Pointer) { _title = title; _titleAlpha = title.Get<float>("alpha") is > 0f and var alpha ? alpha : 1f; }
        if (_face == null || _face.Get<bool>("isDisposed"))
        {
            var face = _face = NativeUi.Component(combo, combo.Get<float>("width"), combo.Get<float>("height"));
            face.Set("touchable", false);
            var height = combo.Get<float>("height");
            SmoothShape.Create(face, 26, height / 2 - 20, 124, 40, Pink, 20f, 20f, 20f, 20f);
            var tag = NativeUi.Label(face, "사용자 정의", 26, height / 2 - 20, 124, 40, 19);
            ModUi.Plain(tag, Black);
            tag.Set("verticalAlign", 1);
            var label = _faceLabel = NativeUi.Label(face, "", 156, 0, 300, height, 45);
            NativeUi.CopyTextFormat(label, title);
            label.Set("verticalAlign", 1);
        }
        var text = Plugin.CustomFrame.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " FPS";
        if (_faceLabel!.Get("text")?.String() != text) _faceLabel.Set("text", text);
        _face.Set("visible", true);
        if (title.Get<float>("alpha") != 0f) title.Set("alpha", 0f);
    }

    // The owned parts go; the game's title shows again.
    private static void Dispose()
    {
        // Called every frame while the game's settings window is closed, so nothing to do costs nothing.
        if (_combo == null && _item == null && _card == null && _face == null && _title == null) return;
        if (_title != null && !_title.Get<bool>("isDisposed") && _title.Get<float>("alpha") == 0f) _title.Set("alpha", _titleAlpha);
        GameUi.Dispose(_item);
        GameUi.Dispose(_card);
        GameUi.Dispose(_face);
        _item = _itemHover = _card = _input = _ok = _hint = _face = _faceLabel = _title = null;
        _combo = _window = null;
        _cardOpen = _error = _pressItem = _pressList = _pressInside = false;
    }

    // Breaker and core failure: the parts go and the game's own frame rate comes back.
    internal static void Clear()
    {
        try { Dispose(); } catch { /* A disposed window has nothing left to restore. */ }
        try { if (_applied) Restore("cleared"); } catch { _applied = false; }
    }
}
