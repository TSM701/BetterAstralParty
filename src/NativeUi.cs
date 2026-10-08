using UnityEngine;

namespace BetterAstralParty;

// Only creates new instances from installed UI assets. Never reparents live game controls.
internal static class NativeUi
{
    internal static Vector2? ProjectBattlePoint(RuntimeObject parent, RuntimeObject display,
        Vector3 local, Camera camera)
    {
        if (!camera.enabled || GameUi.Root == null) return null;
        var world = display.Call("LocalToWorld", local)!.Value<Vector3>();
        var point = camera.WorldToViewportPoint(world);
        var rect = camera.rect;
        var root = GameUi.Root;
        var projected = BattleStatusLayout.Project(point.x, point.y, point.z,
            rect.x, rect.y, rect.width, rect.height, root.Get<float>("width"), root.Get<float>("height"));
        return projected is { } p ? parent.Call("RootToLocal", new Vector2(p.X, p.Y), root)!.Value<Vector2>() : null;
    }

    private static readonly HashSet<string> Missing = new();

    // Source palette/renderer chosen for the destination, never a hard-coded font alias.
    internal static void InheritText(RuntimeObject label, bool dark)
    {
        var template = Create("Common", dark ? "Button_PreviewSkin" : "Button_Confirm_SmallRounded")!;
        try { CopyTextFormat(label, template.Call("GetChild", "title")!); }
        finally { GameUi.Dispose(template); }
        ModFont.Track(label, reset: true);
    }

    internal static void CopyTextFormat(RuntimeObject label, RuntimeObject source)
    {
        var format = label.Get("textFormat")!;
        var size = format.Field("size")!.Value<int>();
        var align = format.Field("align")!.Value<int>();
        CopyFormat(format, source.Get("textFormat")!);
        format.SetField("size", size);
        format.SetField("align", align);
        label.Set("textFormat", format);
    }

    internal static void CopyFormat(RuntimeObject format, RuntimeObject source)
    {
        format.Call("CopyFrom", source);
        // This game's CopyFrom omits stroke, shadow and SDF fields.
        foreach (var field in new[] { "faceDilate", "outline", "outlineSoftness", "underlaySoftness" })
            format.SetField(field, source.Field(field)!.Value<float>());
        foreach (var field in new[] { "outlineColor", "shadowColor" })
            format.SetField(field, source.Field(field)!.Value<Color>());
        format.SetField("shadowOffset", source.Field("shadowOffset")!.Value<Vector2>());
    }

    // Same native rounded-rectangle renderer as game UI; no texture copies or global changes.
    internal sealed class Surface
    {
        internal readonly RuntimeObject Graph;
        private readonly Color _fill;
        private float _opacity = -1;
        internal Surface(RuntimeObject parent, float width, float height, bool fixedOpacity = false, Color? fill = null)
        {
            _fill = fill ?? new Color(1f, 0.985f, 0.95f, 1f);
            Graph = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
            Graph.Set("touchable", false);
            Graph.Call("SetSize", width, height);
            var radius = height <= 80 ? height / 2 : 18f;
            var opacity = fixedOpacity ? 1f : Plugin.Opacity.Value;
            Graph.Get("shape")!.Call("DrawRoundRect", 3f, Color.black, Fill(opacity),
                radius, radius, radius, radius);
            _opacity = opacity;
            parent.Call("AddChild", Graph);
        }
        private Color Fill(float alpha) => new(_fill.r, _fill.g, _fill.b, alpha * _fill.a);
        internal void Refresh()
        {
            var opacity = Plugin.Opacity.Value;
            if (_opacity == opacity) return;
            _opacity = opacity;
            // Shape color controls fill vertices only; border color and child alpha stay intact.
            Graph.Set("color", Fill(opacity));
        }
        internal void Resize(float width, float height) => Graph.Call("SetSize", width, height);
    }

    internal static void OutlineText(RuntimeObject label)
    {
        ModFont.Track(label); // Preserve the prefab's complete native text format.
    }

    // Only private mod clones, after native renderers have assigned their text/colours.
    internal static void OutlineTree(RuntimeObject root)
    {
        var label = root.Get("asTextField");
        if (label != null) { OutlineText(label); return; }
        if (root.Get("asCom") == null) return;
        for (var i = 0; i < root.Get<int>("numChildren"); i++)
            OutlineTree(root.Call("GetChildAt", i)!);
    }

    internal static RuntimeObject? Create(string package, string item)
    {
        try
        {
            var type = RuntimeObject.FindClass("FairyGUI", "UIPackage");
            var url = RuntimeObject.StaticCall(type, "GetItemURL", package, item)?.String();
            var obj = url == null ? null : RuntimeObject.StaticCall(type, "CreateObjectFromURL", url);
            if (obj == null) throw new TypeLoadException($"Required UI resource unavailable: {package}/{item}");
            // GButton.ConstructExtension centres scale-down effects. Do not overwrite that
            // with a top-left pivot; Position converts our top-left layout to this anchor.
            var button = obj?.Get("asButton");
            var pivot = button?.Field("_downEffect")?.Value<int>() == 2 ? 0.5f : 0f;
            obj?.Call("SetPivot", pivot, pivot, true);
            return obj;
        }
        catch (Exception ex)
        {
            if (Missing.Add(package + "/" + item)) Plugin.Logger.LogWarning($"[원본 UI] {package}/{item}: {ex.Message}");
            throw;
        }
    }

    // Use the resting scale, not the temporary pressed scale, to keep the anchor stationary.
    internal static void Position(RuntimeObject obj, float x, float y, float restingScale) =>
        obj.Call("SetXY", x + obj.Get<float>("width") * obj.Get<float>("pivotX") * restingScale,
            y + obj.Get<float>("height") * obj.Get<float>("pivotY") * restingScale);

    internal static RuntimeObject Component(RuntimeObject parent, float width, float height)
    {
        var obj = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GComponent"));
        obj.Call("SetSize", width, height);
        parent.Call("AddChild", obj);
        return obj;
    }

    internal static RuntimeObject? Icon(RuntimeObject parent, string item, float x, float y, float size)
    {
        var icon = Create("Common", item);
        if (icon == null) return null;
        var scale = size / Math.Max(icon.Get<float>("width"), icon.Get<float>("height"));
        icon.Call("SetScale", scale, scale);
        Position(icon, x, y, scale);
        icon.Set("touchable", false);
        parent.Call("AddChild", icon);
        return icon;
    }

    internal static RuntimeObject Label(RuntimeObject parent, string text, float x, float y, float width, float height, int size = 22, bool dark = false)
    {
        var label = GameUi.NewLabel(parent, text, size, dark: dark);
        label.Call("SetXY", x, y);
        label.Call("SetSize", width, height);
        return label;
    }

    internal static RuntimeObject? CounterIndicator(RuntimeObject parent)
    {
        // A private instance of the game's six-roll animation, not the live dice UI.
        var point = Create("Fight", "Fight_Com_Point");
        if (point == null) return null;
        try
        {
            point.Set("touchable", false);
            point.Set("visible", false);
            var transition = point.Call("GetTransition", "MaxPoint")!;
            transition.Call("SetAutoPlay", false, 1, 0f);
            point.Field("aMovie_Dice")!.Set("playing", false);
            for (var i = 0; i < point.Get<int>("numChildren"); i++)
                point.Call("GetChildAt", i)!.Set("visible", false);
            var ghost = Component(point, 160, 160);
            var icon = Component(point, 160, 160);
            foreach (var image in new[] { ghost, icon })
            {
                // MaxPoint animates absolute scale 1 -> 1.9 -> 1; make 1 the resting size.
                if (Icon(image, "Com_Icon_Counter", 0, 0, 160) == null)
                    throw new InvalidOperationException("반격 아이콘 준비 대기");
                image.Set("touchable", false);
                image.Call("SetScale", 1f, 1f);
                image.Call("SetPivot", 0.5f, 0.5f, true);
                image.Call("SetXY", 80f, 80f);
            }
            ghost.Set("visible", false);
            var numberId = point.Field("txt_Point")!.Get("id")!.String();
            var ghostId = point.Field("txt_max")!.Get("id")!.String();
            var items = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>(
                transition.Field("_items")!.Pointer);
            for (var i = 0; i < items.Length; i++)
            {
                var item = new RuntimeObject(items[i].Pointer);
                var target = item.Field("targetId")!.String();
                if (target == numberId || target == ghostId)
                    item.SetField("targetId", (target == numberId ? icon : ghost).Get("id")!.String());
            }
            // Keep native timing, shake, fading afterimage and sound 510 unchanged.
            parent.Call("AddChild", point);
            return point;
        }
        catch { GameUi.Dispose(point); throw; }
    }

    internal static void Play(RuntimeObject obj, string name, int times = 1)
    {
        var transition = obj.Call("GetTransition", name)
            ?? obj.Call("GetChild", "NativeMotion")?.Call("GetTransition", name);
        if (transition == null) return;
        transition.Call("SetAutoPlay", false, 1, 0f);
        transition.Call("Play", times, 0f, null);
    }

    internal static RuntimeObject? Arrow(RuntimeObject parent, float x, float y, float size, bool left)
    {
        var native = Create("Common", "Button_Next2");
        if (native == null) return null;
        native.Call("GetTransition", "Loop")?.Call("SetAutoPlay", false, 1, 0f);
        var arrow = Component(parent, 86, 86);
        native.SetField("name", "NativeMotion");
        Position(native, 0f, 0f, 1f);
        arrow.Call("AddChild", native);
        arrow.Call("SetPivot", 0.5f, 0.5f, true);
        arrow.Call("SetScale", size / arrow.Get<float>("width"), size / arrow.Get<float>("height"));
        arrow.Call("SetXY", x, y);
        // The installed Next2 art points right (verified in-game).
        arrow.Set("rotation", left ? 180f : 0f);
        arrow.Set("touchable", false);
        return arrow;
    }

    internal static void StyleTitle(RuntimeObject button, string text, int size = 26)
    {
        button.Set("title", text);
        var label = button.Call("GetChild", "title");
        if (label == null) return;
        label.Set("autoSize", 3); // FairyGUI.AutoSizeType.Shrink, mod-owned button titles only.
        label.Set("singleLine", true);
        GameUi.StyleText(label, size);
        InheritText(label, dark: true);
    }
}
