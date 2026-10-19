using UnityEngine;

namespace BetterAstralParty;

// Mod settings entry in the in-battle menu: a mod-owned clone of the window's own settings button, placed one row
// above its highest visible button. Native buttons, their order, listeners and the window's behavior are untouched.
// SettingInBattleWindow (players and spectators) and SinglePlayerSettingInBattleWindow show different button sets.
internal static class BattleMenuUi
{
    internal const string Feature = "BattleMenuSettings";
    private static readonly string[] Windows = { "SettingInBattleWindow", "SinglePlayerSettingInBattleWindow" };
    private static readonly string[] Templates = { "btn_OpenSetting", "btn_AudienceOpenSetting" };
    private static readonly string[] Column = { "btn_Continue", "btn_OpenSetting", "btn_MonsterLibrary", "btn_LeaveRoom", "btn_QuitGame",
        "btn_Reset", "btn_AudienceContinue", "btn_AudienceOpenSetting", "btn_AudienceLeaveRoom", "btn_AudienceQuitGame" };
    private static readonly List<float> Rows = new();
    private static RuntimeObject? _button, _pane;
    private static int _language = -1;
    private static bool _pressed;
    private static Vector2 _pressAt;

    internal static bool Showing => _button != null && !_button.Get<bool>("isDisposed") && GameUi.Visible(_button);

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { Update(); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Update()
    {
        var root = GameUi.Root;
        RuntimeObject? window = null;
        if (root != null && !PvpSafety.Suspended && !GameUi.HomeAvailable)
            foreach (var name in Windows)
                if ((window = GameUi.Find(root, name, maxDepth: 1)) != null) break;
        var pane = window?.Get("contentPane");
        RuntimeObject? template = null;
        if (pane != null)
            foreach (var name in Templates)
            {
                var candidate = pane.Call("GetChild", name);
                if (candidate != null && candidate.Get<bool>("visible")) { template = candidate; break; }
            }
        // The settings window reuses shared UI packages; wait instead of failing while they load in battle.
        if (pane == null || template == null || !NativeUi.PackageReady("Common")) { Clear(); return; }
        if (_pane?.Pointer != pane.Pointer || _button == null || _button.Get<bool>("isDisposed")) { Clear(); Create(pane, template); }
        Layout(pane, template);
        HandleInput();
    }

    private static void Create(RuntimeObject pane, RuntimeObject template)
    {
        var url = template.Get("resourceURL")?.String();
        var type = RuntimeObject.FindClass("FairyGUI", "UIPackage");
        _button = url == null ? null : RuntimeObject.StaticCall(type, "CreateObjectFromURL", url);
        if (_button == null) throw new TypeLoadException("In-battle settings button resource unavailable");
        _button.SetField("changeStateOnClick", false);
        _button.Call("SetPivot", template.Get<float>("pivotX"), template.Get<float>("pivotY"), template.Get<bool>("pivotAsAnchor"));
        _button.Call("SetSize", template.Get<float>("width"), template.Get<float>("height"));
        _button.Call("SetScale", template.Get<float>("scaleX"), template.Get<float>("scaleY"));
        pane.Call("AddChild", _button);
        _pane = pane; _language = -1;
    }

    private static void Layout(RuntimeObject pane, RuntimeObject template)
    {
        var button = _button!;
        if (_language != ModText.Revision)
        {
            // Keep the native title format; only the wording is the mod's.
            button.Set("title", ModText.Text("모드 설정"));
            _language = ModText.Revision;
        }
        // The menu column: visible buttons sharing the settings button's x (the top-left back button is not in it).
        var x = template.Get<float>("x");
        RuntimeObject? first = null; var firstIndex = int.MaxValue;
        Rows.Clear();
        foreach (var name in Column)
        {
            var item = pane.Call("GetChild", name);
            if (item == null || !item.Get<bool>("visible") || Math.Abs(item.Get<float>("x") - x) > 2f) continue;
            Rows.Add(item.Get<float>("y"));
            var index = pane.Call("GetChildIndex", item)!.Value<int>();
            if (index < firstIndex) { firstIndex = index; first = item; }
        }
        Rows.Sort();
        // Rest spacing is measured only while the column is evenly spaced; the entrance cascade is not.
        if (Rows.Count >= 2)
        {
            float low = float.MaxValue, high = 0;
            for (var i = 1; i < Rows.Count; i++) { low = Math.Min(low, Rows[i] - Rows[i - 1]); high = Math.Max(high, Rows[i] - Rows[i - 1]); }
            if (low > 1f && high - low < 1f) _step = low;
        }
        var step = _step > 0 ? _step : template.Get<float>("height") * 1.07f;
        // Native CutIn raises the rows from below the screen one after another, top row first; riding one row above
        // the top row keeps this button in the same cascade.
        var y = (first ?? template).Get<float>("y") - step;
        if (button.Get<float>("x") != x || button.Get<float>("y") != y) button.Call("SetXY", x, y);
        if (!button.Get<bool>("visible")) button.Set("visible", true);
    }

    private static float _step;

    // Clicks are read from the hit path; no managed listener is registered on the native object.
    private static void HandleInput()
    {
        if (ModUi.IsOpen) { _pressed = false; return; }
        var mouse = Input.mousePosition;
        var pointer = new Vector2(mouse.x, mouse.y);
        if (Input.GetMouseButtonDown(0))
        {
            _pressed = false;
            foreach (var item in GameUi.PointerPath(hitTest: true))
                if (item.Pointer == _button!.Pointer) { _pressed = true; _pressAt = pointer; break; }
            return;
        }
        if (!_pressed) return;
        if (Input.GetMouseButtonUp(0))
        {
            _pressed = false;
            var sensitivity = RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "UIConfig"), "clickDragSensitivity")!.Value<int>();
            var over = false;
            foreach (var item in GameUi.PointerPath(hitTest: true))
                if (item.Pointer == _button!.Pointer) { over = true; break; }
            if (over && (pointer - _pressAt).sqrMagnitude <= sensitivity * (float)sensitivity) ModUi.OpenFromBattleMenu();
        }
        else if (!Input.GetMouseButton(0)) _pressed = false;
    }

    internal static void Clear()
    {
        if (_button != null && !_button.Get<bool>("isDisposed")) GameUi.Dispose(_button);
        _button = null; _pane = null; _pressed = false; _language = -1;
    }
}
