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
    private static readonly Dictionary<string, RuntimeObject> Masks = new();
    private static readonly List<RuntimeObject> Others = new(); // Other mod root textures Tick checks (HomePortrait's bake).
    private static readonly List<RuntimeObject> Trees = new(); // Mod components that clip their children (WatchTree).
    private static readonly List<RuntimeObject> Managers = new();
    private static readonly HashSet<IntPtr> SeenManagers = new();
    private static IntPtr _containerClass;
    // Owned materials unused this many frames are retired (about a minute at 300 fps, ten at 30).
    private const int StaleFrames = 18000;
    private static int _retired;
    private static bool _guard = true;
    private static readonly bool DevelopmentBuild = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private const float ReproHold = 0.3f;
    private static float _holdUntil;
    private static AsyncOperation? _unload;
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
        var under = FillLine(state);
        state.Fill = fill; state.Line = line; state.Stroke = lineColor;
        if (state.FillImage == null) { Draw(shape, state); return; }
        // A shape made without an outline keeps its single image; every caller restyles shapes made with one.
        if (state.Ring != null && lineChanged && line > 0) Show(state.Ring, state, line);
        if (state.Ring != null && FillLine(state) != under) Show(state.FillImage, state, FillLine(state));
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
            state.FillImage = NewImage(state, FillLine(state), state.Fill);
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
        var margin = ShapeRaster.Margin(state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, Math.Abs(line));
        display.Set("scale9Grid", new NullableRect(new Rect(margin, margin, 2, 2)));
    }

    // The fill's mask under an opaque ring ends half the ring inside the shape (-line), so its antialiased edge lies under the
    // ring's opaque band: a fill reaching the shape's edge showed through the ring's own antialiased outer pixels, a light
    // fringe around the Received Likes pills' black outline on a dark card (2026-11-16, 2026.11.2-dev.11). Under a
    // see-through ring the fill keeps the whole shape, as the ring lets it show.
    private static float FillLine(State state) => state.Line > 0 && state.Stroke.a >= 1f ? -state.Line : 0f;

    // A white mask: the whole shape for a fill (line 0), only the outline band for a ring (line > 0), and for the fill under an
    // opaque ring of that width (line < 0) the shape inset by half the ring.
    private static RuntimeObject Sprite(State state, float line)
    {
        var key = (state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, line);
        if (Sprites.TryGetValue(key, out var sprite)) return sprite;
        var margin = ShapeRaster.Margin(state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, Math.Abs(line));
        var size = ShapeRaster.Size(margin);
        var mask = line < 0
            ? ShapeRaster.Pixels(margin, state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, -line / 2, (1f, 1f, 1f, 1f), (1f, 1f, 1f, 0f))
            : ShapeRaster.Pixels(margin, state.TopLeft, state.TopRight, state.BottomLeft, state.BottomRight, line,
                (1f, 1f, 1f, line > 0 ? 0f : 1f), (1f, 1f, 1f, 1f));
        return Sprites[key] = Region(mask, size);
    }

    // A white mask of another shape (the move direction arrows and rings, 2026-11-06) in the same atlas, shown by a new image
    // stretched to the image's size and tinted by its colour. Null once smooth shapes have failed.
    internal static RuntimeObject? MaskImage(string key, int size, Func<byte[]> pixels)
    {
        if (_failed) return null;
        RuntimeObject? image = null;
        try
        {
            if (!Masks.TryGetValue(key, out var sprite)) Masks[key] = sprite = Region(pixels(), size);
            image = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GImage"));
            image.Set("texture", sprite);
            image.Set("touchable", false);
            return image;
        }
        catch (Exception ex) { _failed = true; GameUi.Dispose(image); Plugin.Diagnostics.Error("SmoothShape", ex); return null; }
    }

    private static RuntimeObject Region(byte[] mask, int size)
    {
        var (page, x, y) = Place(size + 2 * Padding);
        ShapeRaster.Blit(mask, size, page.Pixels, page.Size, x + Padding, y + Padding, Padding);
        fixed (byte* data = page.Pixels) page.Texture.LoadRawTextureData((IntPtr)data, page.Pixels.Length);
        page.Texture.Apply(false, false);
        // NTexture(NTexture root, Rect region, bool rotated): the region in pixels from the top-left, as package atlases use it.
        return RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "NTexture"), page.Root, new Rect(x + Padding, y + Padding, size, size), false);
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

    // A mod component that clips its children (an overflow clip or a stencil mask) is a clip context of its own, so every
    // texture drawn in it, the game's art and fonts included, gets materials FairyGUI keeps for that context alone. Tick
    // checks the material caches of every texture drawn in it too; a disposed component drops out by itself.
    internal static void WatchTree(RuntimeObject component) => Trees.Add(component);

    // FairyGUI makes one material per clip context for a texture and hands it out for the texture's life without checking
    // it. The game calls Resources.UnloadUnusedAssets on scene loads, which destroys mod materials (every match start and
    // results load logs some); every later image of that context then draws in Unity's error magenta (the card advice badges,
    // 2026-11-02), and in a clipped context NGraphics.Update throws in UpdateContext.ApplyClippingProperties. Magic Academy's
    // phase change is such a load in the middle of a match (MapLogic.MapSceneChange), and when FairyGUI drew the player list
    // tabs before this once-a-second purge (0.2 s of exceptions in the failing run), the hand, the player bars and the
    // in-battle menu drew only inside one tab for the rest of the match (user-reproduced 2026-11-24; again on a new match a
    // minute after a surrender, while 2026.11.4-dev.8 guarded only materials used in the last minute). So every material of
    // a texture the mod owns (the atlas pages, the other watched textures) is kept out of the unload
    // (HideFlags.DontUnloadUnusedAsset), and one unused for StaleFrames is taken out of FairyGUI's cache first and then
    // destroyed here, so FairyGUI never hands out a destroyed one and makes a new one if its clip context draws again. The
    // game's own textures drawn in watched clip contexts keep their materials as the game has them; destroyed ones are dropped.
    internal static void Tick()
    {
        if (DevelopmentBuild) DevKeys();
        if (_unload != null)
        {
            if (!_unload.isDone) return;
            _unload = null; _holdUntil = Time.unscaledTime + ReproHold;
            Plugin.Diagnostics.Write("smoothShape.repro unload done; purge held " + ReproHold + " s");
        }
        if (_tickFailed || Pages.Count + Others.Count + Trees.Count == 0 || Time.unscaledTime < _tickAt || Time.unscaledTime < _holdUntil) return;
        _tickAt = Time.unscaledTime + 1f;
        try { Pass(destroy: false); }
        catch (Exception ex) { _tickFailed = true; Plugin.Diagnostics.Error("SmoothShape.tick", ex); }
    }

    // Every material cache of the atlas pages, the other watched textures and the textures drawn in watched clip contexts.
    private static int Pass(bool destroy)
    {
        _imageShader ??= RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "ShaderConfig"), "imageShader")!.String();
        var count = 0;
        foreach (var root in Pages.Select(page => page.Root).Concat(Others)) count += Purge(root.Call("GetMaterialManager", _imageShader)!, "atlas", owned: true, destroy);
        if (Trees.Count == 0) return count;
        if (_containerClass == IntPtr.Zero) _containerClass = RuntimeObject.FindClass("FairyGUI", "Container");
        Managers.Clear(); SeenManagers.Clear();
        Trees.RemoveAll(tree => tree.Get<bool>("isDisposed"));
        foreach (var tree in Trees) Collect(tree.Get("displayObject"), 0);
        foreach (var manager in Managers) count += Purge(manager, "tree", owned: false, destroy);
        Managers.Clear();
        return count;
    }

    // Development builds only (2026-11-24, to reproduce the race on any map): Ctrl+Shift+F1 turns the unload guard off and
    // on (applied at the next pass); Ctrl+Shift+F2 destroys every watched material and Ctrl+Shift+F4 runs
    // Resources.UnloadUnusedAssets, each holding the purge ReproHold seconds as the late purge did in the failing run.
    private static void DevKeys()
    {
        if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return;
        if (Input.GetKeyDown(KeyCode.F1))
        {
            _guard = !_guard; _tickAt = 0;
            Plugin.Diagnostics.Write("smoothShape.guard=" + (_guard ? "on" : "off"));
        }
        else if (Input.GetKeyDown(KeyCode.F2))
        {
            _holdUntil = Time.unscaledTime + ReproHold;
            Plugin.Diagnostics.Write("smoothShape.repro destroyed=" + Pass(destroy: true) + "; purge held " + ReproHold + " s");
        }
        else if (Input.GetKeyDown(KeyCode.F4) && _unload == null)
        {
            _unload = Resources.UnloadUnusedAssets();
            Plugin.Diagnostics.Write("smoothShape.repro unload started; guard=" + (_guard ? "on" : "off"));
        }
    }

    // Each display object's NGraphics material cache (its texture's MaterialManager), once per cache.
    private static void Collect(RuntimeObject? display, int depth)
    {
        if (display == null) return;
        if (display.Get("graphics")?.Field("_manager") is { } manager && SeenManagers.Add(manager.Pointer)) Managers.Add(manager);
        if (depth >= 16 || !RuntimeObject.Is(display.Pointer, _containerClass)) return;
        var children = display.Field("_children")!;
        for (int i = 0, count = children.Get<int>("Count"); i < count; i++) Collect(children.Call("get_Item", i), depth + 1);
    }

    // Returns the live materials seen (or destroyed, for the development repro). `owned`: the mod's own texture, whose
    // materials are guarded and retired here.
    private static int Purge(RuntimeObject manager, string source, bool owned, bool destroy)
    {
        var live = 0;
        var frame = Time.frameCount;
        // MaterialManager._materials: material flags -> List<MaterialRef>.
        var entries = manager.Field("_materials")!.Call("GetEnumerator")!;
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
                    if (pointer != IntPtr.Zero && new Material(pointer) is var material && material)
                    {
                        live++;
                        if (destroy) { UnityEngine.Object.DestroyImmediate(material); continue; }
                        if (!owned) continue;
                        // MaterialRef.frame: the last frame GetMaterial handed it out.
                        if (frame - item.Field<int>("frame") > StaleFrames)
                        {
                            refs.Call("RemoveAt", i);
                            UnityEngine.Object.Destroy(material);
                            Plugin.Diagnostics.State("smoothShape.retired", (++_retired).ToString(System.Globalization.CultureInfo.InvariantCulture));
                            continue;
                        }
                        var flags = material.hideFlags;
                        var wanted = _guard ? flags | HideFlags.DontUnloadUnusedAsset : flags & ~HideFlags.DontUnloadUnusedAsset;
                        if (wanted != flags) material.hideFlags = wanted;
                        continue;
                    }
                    if (destroy) continue;
                    refs.Call("RemoveAt", i);
                    Plugin.Diagnostics.State("smoothShape.materialLost", $"count={++_lostMaterials}; flags={entry.Get<int>("Key")}; group={item.Field<uint>("group")}; from={source}");
                }
            }
        }
        finally { entries.Call("Dispose"); }
        return live;
    }

    private static uint Pack(Color color) => Byte(color.r) | (uint)Byte(color.g) << 8 | (uint)Byte(color.b) << 16 | (uint)Byte(color.a) << 24;
    private static byte Byte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
