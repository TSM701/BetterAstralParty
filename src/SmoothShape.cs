using System.Runtime.CompilerServices;
using UnityEngine;

namespace BetterAstralParty;

// Smooth rounded rectangles for all mod UI (user-requested 2026-11-01): FairyGUI's Shape meshes are not antialiased, so
// their corners and thin outlines step. Since 2026-11-02 they are drawn the way the game draws its package images: ShapeRaster
// rasterizes white antialiased masks (a fill per corner set, an outline ring per corner set and line width) into one shared
// atlas that is only ever appended to, each mask is a sprite of that atlas (an NTexture region of one root texture, as
// FairyGUI builds package atlases) shown by a GImage with Image.scale9Grid and Image.textureScale, and colours are image
// tints only. An outlined shape is a small component with the fill image and the ring image on top, both sized with it.
// The earlier one-texture-per-colour design grew hundreds of textures and materials during a match and destroyed unused
// textures behind FairyGUI, and after a solo game every UI draw was confined to the settings list rect (see CLAUDE.md).
// If any step fails, this and later shapes fall back to the native Shape and only lose their smooth edges.
internal static unsafe class SmoothShape
{
    private sealed class State
    {
        internal float TopLeft, TopRight, BottomLeft, BottomRight, Line;
        internal Color Fill, Stroke;
        internal RuntimeObject? FillImage, Ring; // Both null for a native Shape; Ring only on an outlined shape.
    }
    private static readonly ConditionalWeakTable<RuntimeObject, State> States = new();

    // Atlas pages, appended to and never destroyed: a sprite keeps its place for the session.
    private sealed class Page
    {
        internal Texture2D Texture = null!;
        internal RuntimeObject Root = null!;
        internal byte[] Pixels = null!;
        internal int Size, X, Y, Row;
    }
    private static readonly List<Page> Pages = new();
    private static readonly Dictionary<(float, float, float, float, float), RuntimeObject> Sprites = new(); // Corners and line (0: fill).
    private static readonly List<RuntimeObject> Others = new(); // Other mod root textures Tick checks (HomePortrait's bake).
    private const int PageSize = 512, Padding = 2;
    private static bool _failed, _tickFailed;
    private static float _tickAt;
    private static int _lostMaterials;
    private static string? _imageShader;

    // FairyGUI DrawRoundRect's shape (corner radii top-left, top-right, bottom-left, bottom-right; the line inside the bounds).
    internal static RuntimeObject Create(RuntimeObject parent, float x, float y, float width, float height, Color fill,
        float topLeft, float topRight, float bottomLeft, float bottomRight, float line = 0f, Color? lineColor = null)
    {
        var state = new State { TopLeft = topLeft, TopRight = topRight, BottomLeft = bottomLeft, BottomRight = bottomRight, Line = line, Fill = fill, Stroke = lineColor ?? Color.black };
        RuntimeObject? shape = null;
        if (!_failed)
        {
            try { shape = line > 0 ? NewOutlined(state, width, height) : (state.FillImage = NewImage(state, 0f, fill)); }
            catch (Exception ex) { _failed = true; state.FillImage = state.Ring = null; Plugin.Diagnostics.Error("SmoothShape", ex); }
        }
        shape ??= NewGraph(state, width, height);
        States.AddOrUpdate(shape, state);
        shape.Set("touchable", false);
        parent.Call("AddChild", shape);
        shape.Call("SetSize", width, height);
        shape.Call("SetXY", x, y);
        return shape;
    }

    // A new fill colour; corners and outline stay.
    internal static void Fill(RuntimeObject shape, Color fill)
    {
        if (!States.TryGetValue(shape, out var state)) { shape.Set("color", fill); return; }
        if (Pack(state.Fill) == Pack(fill)) return;
        state.Fill = fill;
        if (state.FillImage == null) shape.Set("color", fill); // Native Shape colour is the fill.
        else Tint(state);
    }

    // A new fill and outline (the outline may change width); corners stay.
    internal static void Restyle(RuntimeObject shape, Color fill, float line, Color lineColor)
    {
        if (!States.TryGetValue(shape, out var state)) return;
        if (Pack(state.Fill) == Pack(fill) && state.Line == line && Pack(state.Stroke) == Pack(lineColor)) return;
        var lineChanged = state.Line != line;
        state.Fill = fill; state.Line = line; state.Stroke = lineColor;
        if (state.FillImage == null) { Draw(shape, state); return; }
        // A shape made without an outline keeps its single image; every caller restyles shapes made with one.
        if (state.Ring != null && lineChanged && line > 0) Show(state.Ring, state, line);
        Tint(state);
    }

    // The fill image shows the fill colour; the ring, drawn over it, the outline colour (hidden without an outline).
    private static void Tint(State state)
    {
        state.FillImage!.Set("color", state.Fill);
        if (state.Ring == null) return;
        state.FillImage.Set("visible", state.Fill.a > 0);
        state.Ring.Set("color", state.Stroke);
        state.Ring.Set("visible", state.Line > 0);
    }

    private static RuntimeObject NewImage(State state, float line, Color color)
    {
        var image = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GImage"));
        try
        {
            Show(image, state, line);
            image.Set("color", color);
            image.Set("touchable", false);
            return image;
        }
        catch { GameUi.Dispose(image); throw; }
    }

    private static RuntimeObject NewOutlined(State state, float width, float height)
    {
        var shape = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GComponent"));
        try
        {
            shape.Call("SetSize", width, height);
            state.FillImage = NewImage(state, 0f, state.Fill);
            state.Ring = NewImage(state, state.Line, state.Stroke);
            var size = RuntimeObject.EnumValue("FairyGUI", "RelationType", "Size");
            foreach (var part in new[] { state.FillImage, state.Ring })
            {
                shape.Call("AddChild", part);
                part.Call("SetSize", width, height);
                part.Call("AddRelation", shape, size); // The parts keep the shape's size.
            }
            Tint(state);
            return shape;
        }
        catch { GameUi.Dispose(shape); throw; }
    }

    private static RuntimeObject NewGraph(State state, float width, float height)
    {
        var graph = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
        graph.Call("SetSize", width, height);
        Draw(graph, state);
        return graph;
    }

    private static void Draw(RuntimeObject graph, State state) =>
        graph.Get("shape")!.Call("DrawRoundRect", state.Line, state.Stroke, state.Fill, state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight);

    // The sprite with its corner margins and a two-pixel stretched centre. Image.UpdateTexture resets textureScale to one,
    // so the scale and the grid follow the texture.
    private static void Show(RuntimeObject image, State state, float line)
    {
        image.Set("texture", Sprite(state, line));
        var display = image.Get("displayObject")!;
        display.Set("textureScale", new Vector2(ShapeRaster.Density, ShapeRaster.Density));
        var margin = ShapeRaster.Margin(state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, line);
        display.Set("scale9Grid", new NullableRect(new Rect(margin, margin, 2, 2)));
    }

    // A white mask: the whole shape for a fill (line 0), only the outline band for a ring.
    private static RuntimeObject Sprite(State state, float line)
    {
        var key = (state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, line);
        if (Sprites.TryGetValue(key, out var sprite)) return sprite;
        var margin = ShapeRaster.Margin(state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, line);
        var size = ShapeRaster.Size(margin);
        var mask = ShapeRaster.Pixels(margin, state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, line,
            (1f, 1f, 1f, line > 0 ? 0f : 1f), (1f, 1f, 1f, 1f));
        var (page, x, y) = Place(size + 2 * Padding);
        ShapeRaster.Blit(mask, size, page.Pixels, page.Size, x + Padding, y + Padding, Padding);
        fixed (byte* data = page.Pixels) page.Texture.LoadRawTextureData((IntPtr)data, page.Pixels.Length);
        page.Texture.Apply(false, false);
        // NTexture(NTexture root, Rect region, bool rotated): the region in pixels from the top-left, as package atlases use it.
        sprite = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "NTexture"), page.Root, new Rect(x + Padding, y + Padding, size, size), false);
        Sprites[key] = sprite;
        return sprite;
    }

    // Shelf packing: left to right in rows, a new page when the last one is full.
    private static (Page Page, int X, int Y) Place(int cell)
    {
        var page = Pages.Count > 0 ? Pages[^1] : null;
        if (page != null && page.X + cell > page.Size) { page.X = 0; page.Y += page.Row; page.Row = 0; }
        if (page == null || page.Y + cell > page.Size) page = NewPage(Math.Max(PageSize, cell));
        var at = (page, page.X, page.Y);
        page.X += cell;
        page.Row = Math.Max(page.Row, cell);
        return at;
    }

    private static Page NewPage(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        // Transparent white, so filtering next to a sprite never darkens it.
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4) pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
        // NTexture(Texture, Texture alphaTexture, float xScale, float yScale): its only four-argument constructor.
        var root = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "NTexture"), new RuntimeObject(texture.Pointer), null!, 1f, 1f);
        var page = new Page { Texture = texture, Root = root, Pixels = pixels, Size = size };
        Pages.Add(page);
        Plugin.Diagnostics.State("smoothShape.atlas", $"pages={Pages.Count}; sprites={Sprites.Count}");
        return page;
    }

    // Another mod root texture whose materials Tick checks; unwatch it before destroying its texture.
    internal static void Watch(RuntimeObject root) => Others.Add(root);
    internal static void Unwatch(RuntimeObject root) => Others.RemoveAll(other => other.Pointer == root.Pointer);

    // FairyGUI makes one material per clip context for a mod texture and hands it out for the texture's life without checking
    // it. The game calls Resources.UnloadUnusedAssets on scene loads, which can destroy one that no image showed at that
    // moment; every later image of that context then draws in Unity's error magenta (the card advice badges, 2026-11-02).
    // Drop destroyed materials so FairyGUI makes new ones.
    internal static void Tick()
    {
        if (_tickFailed || Pages.Count + Others.Count == 0 || Time.unscaledTime < _tickAt) return;
        _tickAt = Time.unscaledTime + 1f;
        try
        {
            _imageShader ??= RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "ShaderConfig"), "imageShader")!.String();
            foreach (var root in Pages.Select(page => page.Root).Concat(Others))
            {
                // MaterialManager._materials: material flags -> List<MaterialRef>.
                var entries = root.Call("GetMaterialManager", _imageShader)!.Field("_materials")!.Call("GetEnumerator")!;
                try
                {
                    while (entries.Call("MoveNext")!.Value<bool>())
                    {
                        var entry = entries.Get("Current")!;
                        var refs = entry.Get("Value")!;
                        for (var i = refs.Get<int>("Count") - 1; i >= 0; i--)
                        {
                            var item = refs.Call("get_Item", i)!;
                            var pointer = item.FieldPointer("material");
                            if (pointer != IntPtr.Zero && new Material(pointer)) continue;
                            refs.Call("RemoveAt", i);
                            Plugin.Diagnostics.State("smoothShape.materialLost", $"count={++_lostMaterials}; flags={entry.Get<int>("Key")}; group={item.Field<uint>("group")}");
                        }
                    }
                }
                finally { entries.Call("Dispose"); }
            }
        }
        catch (Exception ex) { _tickFailed = true; Plugin.Diagnostics.Error("SmoothShape.tick", ex); }
    }

    private static uint Pack(Color color) => Byte(color.r) | (uint)Byte(color.g) << 8 | (uint)Byte(color.b) << 16 | (uint)Byte(color.a) << 24;
    private static byte Byte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
