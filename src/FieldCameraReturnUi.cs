using UnityEngine;

namespace BetterAstralParty;

// A private native clone, never a listener or mutation on the game's map button.
internal static class FieldCameraReturnUi
{
    private const string Feature = "FieldCameraReturn";
    private static RuntimeObject? _button;
    private static IntPtr _root, _field, _strip;
    private static bool _pressed, _cancelled, _hovered;
    private static Vector2 _pressAt, _titlePadding, _nativeSize, _size;
    private static (float X, float Y, float Scale)? _layout;
    private static int _language = -1, _fontRevision = -1, _fontSize = -1, _consumedFrame = -1;
    internal static bool ConsumedInput => _consumedFrame == Time.frameCount;
    internal static bool Visible => _button != null && GameUi.Visible(_button);

    internal static void Tick(bool visible)
    {
        try
        {
            var root = GameUi.Root;
            if (!visible || !Compatibility.Allowed(Feature) || root == null)
            { Hide("inactive-or-input-blocked"); return; }
            var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
            var field = logic?.Get("battle")?.Field("battleInfo")?.Get("ui");
            if (!GameUi.Visible(field)) { Hide("hud-unavailable"); return; }
            var strip = field!.Field("com_MapInfo");
            var pin = strip?.Field("btn_ShowMap");
            // Only the turn-bar row anchors us; pin visibility, hover and press never own this control.
            var anchor = strip;
            if (!GameUi.Visible(anchor) || anchor!.Get<float>("width") <= 0 || anchor.Get<float>("height") <= 0)
            { Hide("anchor-unavailable"); return; }
            for (var node = anchor; node != null; node = node.Get("parent"))
                if (!(node.Get<float>("alpha") > 0) || !(node.Get<float>("scaleX") > 0) || !(node.Get<float>("scaleY") > 0))
                { Hide("anchor-hidden"); return; }
            if (_root != root.Pointer || _field != field!.Pointer || _strip != anchor!.Pointer
                || _button?.Get<bool>("isDisposed") == true || _button != null && _button.Get("parent")?.Pointer != root.Pointer)
                Clear();
            if (_button == null)
            {
                // Do not create a hit surface underneath an input that started elsewhere.
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)
                    || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0))
                { Plugin.Diagnostics.State("fieldCameraReturn.gate", "waiting-mouse-release"); return; }
                _button = NativeUi.Create("Common", "Button_ReturnRounded")
                    ?? throw new TypeLoadException("Common/Button_ReturnRounded");
                _button.Set("visible", false);
                _button.Set("opaque", true);
                _button.Set("sortingOrder", field.Get<int>("sortingOrder"));
                _button.SetField("changeStateOnClick", false);
                var title = _button.Call("GetChild", "title")!;
                _nativeSize = new Vector2(_button.Get<float>("width"), _button.Get<float>("height"));
                _titlePadding = new Vector2(_nativeSize.x - title.Get<float>("width"),
                    _nativeSize.y - title.Get<float>("height"));
                // The original rollover image lacks the size relation of its normal background.
                _button.Call("GetChild", "n48")!.Get("relations")!.Call("Add", _button, 24);
                root.Call("AddChild", _button);
                _root = root.Pointer; _field = field!.Pointer; _strip = anchor!.Pointer;
            }
            var button = _button;
            var width = root.Get<float>("width"); var height = root.Get<float>("height");
            var rowHeight = anchor!.Get<float>("height");
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0
                || !float.IsFinite(rowHeight) || rowHeight <= 0) { Hide("invalid-layout"); return; }
            var titleLabel = button.Call("GetChild", "title")!;
            var fontSize = Math.Max(1, (int)Math.Floor(20 * _nativeSize.y / rowHeight));
            if (_language != ModText.Revision || _fontRevision != ModFont.Revision || _fontSize != fontSize)
            {
                NativeUi.StyleTitle(button, ModText.Text("자유 시점 카메라 종료"), fontSize);
                titleLabel.Set("autoSize", 0); // Measure unshrunk native glyphs before fitting the owned background.
                _size = new Vector2(Math.Max(_nativeSize.x, titleLabel.Get<float>("textWidth") + _titlePadding.x),
                    Math.Max(_nativeSize.y, titleLabel.Get<float>("textHeight") + _titlePadding.y));
                titleLabel.Set("autoSize", 3);
                _language = ModText.Revision; _fontRevision = ModFont.Revision; _fontSize = fontSize;
            }
            var buttonWidth = _size.x; var buttonHeight = _size.y;
            if (!float.IsFinite(buttonWidth) || !float.IsFinite(buttonHeight) || buttonWidth <= 0 || buttonHeight <= 0)
            { Hide("invalid-layout"); return; }
            var right = pin != null && !pin.Get<bool>("isDisposed") ? pin.Get<float>("xMin") + pin.Get<float>("width") : anchor.Get<float>("width");
            var top = anchor.Call("LocalToRoot", new Vector2(right, 0), root)!.Value<Vector2>();
            var bottom = anchor.Call("LocalToRoot", new Vector2(right, rowHeight), root)!.Value<Vector2>();
            var anchorHeight = bottom.y - top.y;
            // Preserve the prefab's 9-slice borders; shrink the whole control, not its image height.
            var x = Math.Max(top.x, bottom.x) + anchorHeight / 2;
            var scale = Math.Min(anchorHeight * 1.25f / buttonHeight, (width - x) / buttonWidth);
            var y = Math.Min(top.y, bottom.y); // Grow below the row, never into the turn bar above it.
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(scale) || scale <= 0
                || x < 0 || y < 0 || y + buttonHeight * scale > height) { Hide("invalid-layout"); return; }
            var titleWidth = buttonWidth - _titlePadding.x;
            var titleHeight = buttonHeight - _titlePadding.y;
            if (!float.IsFinite(titleWidth) || !float.IsFinite(titleHeight) || titleWidth <= 0 || titleHeight < 1)
            { Hide("invalid-layout"); return; }
            if (_layout != (x, y, scale) || button.Get<float>("width") != buttonWidth || button.Get<float>("height") != buttonHeight)
            {
                if (_pressed) _cancelled = true;
                _layout = (x, y, scale);
                if (button.Get<float>("width") != buttonWidth || button.Get<float>("height") != buttonHeight)
                    button.Call("SetSize", buttonWidth, buttonHeight);
                var buttonScale = scale * (button.Field<bool>("_downScaled") ? button.Field<float>("_downEffectValue") : 1f);
                button.Call("SetScale", buttonScale, buttonScale);
                NativeUi.Position(button, x, y, scale);
                titleLabel.Call("SetSize", titleWidth, titleHeight);
                NativeUi.Position(titleLabel, _titlePadding.x / 2, _titlePadding.y / 2, 1f);
            }
            button.Set("visible", true);
            Plugin.Diagnostics.State("fieldCameraReturn.gate", "visible");
            // Input locks cancel a click, not the exit surface of an already owned free view.
            if (!Application.isFocused || ModUi.IsOpen || Input.touchCount > 0
                || root.Get<bool>("hasModalWindow") || root.Get<bool>("modalWaiting") || root.Get<bool>("hasAnyPopup")
                || RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "GObject"), "get_draggingObject") != null)
            { CancelInput("inactive-or-input-blocked"); return; }
            var path = GameUi.PointerPath(refresh: true);
            // Keep ordinary PvE HUD windows; higher shop/target/modal windows still own their hits.
            var topWindow = root.Call("GetTopWindow");
            if (topWindow != null && GameUi.Visible(topWindow)
                && (topWindow.Get<bool>("modal") || path.Any(hit => hit.Pointer == topWindow.Pointer)))
            { CancelInput("window-occluded"); return; }
            var hit = path.Any(item => item.Pointer == button.Pointer);
            if (hit != _hovered) { NativeUi.Play(button, hit ? "Switchin" : "Switchout"); _hovered = hit; }
            var mouse = Input.mousePosition;
            var pointer = new Vector2(mouse.x, mouse.y);
            if (Input.GetMouseButtonDown(0)) { _pressed = hit; _cancelled = false; _pressAt = pointer; }
            if (!_pressed) return;
            _consumedFrame = Time.frameCount;
            var sensitivity = RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "UIConfig"), "clickDragSensitivity")!.Value<int>();
            if ((pointer - _pressAt).sqrMagnitude > sensitivity * (float)sensitivity) _cancelled = true;
            if (Input.GetMouseButtonUp(0))
            { var activate = hit && !_cancelled; _pressed = false; if (activate) FieldFreeCamera.Return(); }
            else if (!Input.GetMouseButton(0)) _pressed = false;
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void CancelInput(string reason)
    {
        Plugin.Diagnostics.State("fieldCameraReturn.gate", reason);
        if (_hovered) { NativeUi.Play(_button!, "Switchout"); _hovered = false; }
        if (!_pressed) return;
        _consumedFrame = Time.frameCount; _cancelled = true;
        if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)) _pressed = false;
    }

    private static void Hide(string reason)
    {
        Plugin.Diagnostics.State("fieldCameraReturn.gate", reason);
        Clear();
    }

    internal static void Clear()
    {
        if (_pressed) _consumedFrame = Time.frameCount;
        _pressed = _cancelled = _hovered = false;
        _root = _field = _strip = IntPtr.Zero; _layout = null; _language = _fontRevision = _fontSize = -1;
        _titlePadding = _nativeSize = _size = default;
        if (_button == null) return;
        var button = _button;
        Exception? failure = null;
        void Attempt(Action action) { try { action(); } catch (Exception ex) { failure ??= ex; } }
        // Retain ownership until disposal is confirmed, even when one cleanup operation fails.
        var disposed = false;
        Attempt(() => disposed = button.Get<bool>("isDisposed"));
        if (!disposed)
        {
            Attempt(() => button.Set("touchable", false));
            Attempt(() => button.Set("visible", false));
            Attempt(() => GameUi.Dispose(button));
        }
        Attempt(() => { if (button.Get<bool>("isDisposed")) _button = null; });
        if (failure != null) throw failure;
    }
}
