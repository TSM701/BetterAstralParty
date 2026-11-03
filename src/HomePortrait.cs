using System.Globalization;
using UnityEngine;

namespace BetterAstralParty;

// The home Mod Settings button's character (user-chosen 2026-11-01): the "매혹적인 가희 - 테루" skin (100124004, card art
// UT_Hero_Card_124_02) on a plate in Teru's own colour from the Character table, framed by draft 3 and clipped by draft A
// (artifacts/cc/home-portrait-drafts): the plate's rounded edge on the left, bottom and right, open above it for the head,
// that opening's left edge following the left ear so the card's sparkle under it stays out. A stencil mask cannot be
// antialiased and steps once the native hover tilts the button, so once the native loader has the art the mod bakes the
// visible part into its own texture: a GPU copy of that rectangle with its alpha multiplied by the clip's coverage
// (ShapeRaster), shown in plain images that bilinear filtering keeps smooth at any angle. Until then, and if baking fails,
// the loader shows the art under a rectangular stencil over the same rectangle.
internal static unsafe class HomePortrait
{
    private const string Art = "UT_Hero_Card_124_02";
    private const int Hero = 124;
    private static readonly Color Fallback = new(253 / 255f, 124 / 255f, 127 / 255f); // Character 124's #fd7c7f
    // Button units. The art at 0.34 of the 1616-wide card, its top-left; the visible part spans the plate's width from the
    // art's top (the hat) to the plate's bottom. The button sits 38 under the screen top, so the art is 25 lower than draft 3
    // put it and the hat stays on screen (top-drafts A, user-chosen 2026-11-01).
    private const float ArtX = -160f, ArtY = -37f, ArtWidth = 549.44f;
    private static readonly Rect Bounds = new(2f, -37f, 343f, 209f);
    private static RuntimeObject? _art, _portrait, _loader, _native;
    private static Texture2D? _texture;
    private static bool _pending;

    // Draft A's clip, as coverage at a point in button units; the left ear's line moves down with the art.
    internal static float Clip(float x, float y) => Math.Max(ShapeRaster.RoundRect(x, y, 2f, 6f, 343f, 166f, 12f),
        Math.Min(ShapeRaster.HalfPlane(x, y, 12f, 9f, 12f, -75f), ShapeRaster.HalfPlane(x, y, 80f, 45f, 12f, 9f)));

    // The character's colour (the Character table's one colour field, "#fd7c7f" for Teru), probed by its possible names.
    internal static Color Plate()
    {
        try
        {
            var config = CharacterGuideUi.Config(Hero);
            foreach (var name in new[] { "ProfileColor", "NameBGColor", "CardFrontColor" })
                if (config != null && config.HasGetter(name) && Hex(config.Get(name)?.String()) is { } color)
                {
                    Plugin.Diagnostics.State("home.portraitColor", name);
                    return color;
                }
        }
        catch (Exception ex) { Plugin.Diagnostics.Error("HomePortrait.color", ex); }
        return Fallback;
    }

    // The stencilled portrait under `art` (button units); Tick bakes it once the art has loaded.
    internal static void Build(RuntimeObject art)
    {
        Release(); // A rebuilt home menu.
        _art = art;
        _portrait = NativeUi.Component(art, Bounds.width, Bounds.height);
        var mask = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
        mask.Call("SetSize", Bounds.width, Bounds.height);
        mask.Get("shape")!.Call("DrawRect", 0f, Color.white, Color.white);
        mask.Set("touchable", false);
        _portrait.Call("AddChild", mask);
        _portrait.Set("mask", mask.Get("displayObject")!);
        // Shadow first, portrait second: share the native cached texture/alpha silhouette; tint only the private loader.
        foreach (var shadow in new[] { true, false })
        {
            var skin = RuntimeObject.New(RuntimeObject.FindClass("UI", "TextureLoader"));
            skin.Set("touchable", false);
            // Native ScaleMatchWidth preserves the full source width and aspect ratio.
            skin.Set("autoSize", false);
            skin.Set("fill", 3); // FairyGUI.FillType.ScaleMatchWidth (not ScaleFree).
            skin.Call("SetSize", ArtWidth, ArtWidth);
            skin.Call("SetXY", ArtX - Bounds.x + (shadow ? 8f : 0f), ArtY - Bounds.y + (shadow ? 6f : 0f));
            if (shadow)
            {
                skin.Set("color", Color.black);
                skin.Set("alpha", 0.35f);
            }
            _portrait.Call("AddChild", skin);
            // Native async cache owns the shared texture and releases each loader's reference.
            skin.Set("url", Art);
            if (!shadow) _loader = skin;
        }
        _portrait.Call("SetXY", Bounds.x, Bounds.y);
        _pending = true;
    }

    internal static void Tick()
    {
        if (!_pending || _art == null || _loader == null) return;
        try
        {
            if (_art.Get<bool>("isDisposed")) { _pending = false; return; }
            var source = _loader.Get("texture");
            if (source == null) return; // Still loading.
            _pending = false;
            Show(Bake(source));
            Plugin.Diagnostics.State("home.portrait", "baked");
        }
        catch (Exception ex)
        {
            _pending = false;
            Release();
            Plugin.Diagnostics.Error("HomePortrait", ex); // The stencilled portrait stays.
        }
    }

    private static void Release()
    {
        if (_native != null) { SmoothShape.Unwatch(_native); _native = null; }
        if (_texture != null) { UnityEngine.Object.Destroy(_texture); _texture = null; }
    }

    private static Texture2D Bake(RuntimeObject source)
    {
        if (source.Field<bool>("rotated")) throw new NotSupportedException("rotated atlas sprite");
        var native = new Texture(source.Get("nativeTexture")!.Pointer);
        var uv = source.Field<Rect>("uvRect");
        var art = new Rect(ArtX, ArtY, ArtWidth, ArtWidth * source.Get<int>("height") / source.Get<int>("width"));
        int width = (int)(Bounds.width * ShapeRaster.Density), height = (int)(Bounds.height * ShapeRaster.Density);
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        try
        {
            // Target uv (0..1, up) to source uv: the visible rectangle inside the art, inside the texture's own uvRect.
            Graphics.Blit(native, target,
                new Vector2(Bounds.width / art.width * uv.width, Bounds.height / art.height * uv.height),
                new Vector2(uv.x + (Bounds.x - art.x) / art.width * uv.width, uv.y + (art.yMax - Bounds.yMax) / art.height * uv.height));
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
        var raw = texture.GetRawTextureData();
        var pixels = new byte[raw.Length];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = raw[i];
        // Rows bottom-up; each texel's centre in button units.
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                var alpha = (row * width + column) * 4 + 3;
                var coverage = Clip(Bounds.x + (column + 0.5f) / ShapeRaster.Density, Bounds.yMax - (row + 0.5f) / ShapeRaster.Density);
                pixels[alpha] = (byte)MathF.Round(pixels[alpha] * coverage);
            }
        fixed (byte* data = pixels) texture.LoadRawTextureData((IntPtr)data, pixels.Length);
        texture.Apply(false, true);
        return texture;
    }

    // The baked art and its shadow replace the stencilled portrait at the same place.
    private static void Show(Texture2D texture)
    {
        // NTexture(Texture, Texture alphaTexture, float xScale, float yScale), as SmoothShape builds it.
        var native = _native = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "NTexture"), new RuntimeObject(texture.Pointer), null!, 1f, 1f);
        SmoothShape.Watch(native); // Its materials face the same unused-asset sweep as the atlas's.
        foreach (var shadow in new[] { true, false })
        {
            var image = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GImage"));
            image.Set("texture", native);
            image.Set("touchable", false);
            image.Call("SetSize", Bounds.width, Bounds.height);
            image.Call("SetXY", Bounds.x + (shadow ? 8f : 0f), Bounds.y + (shadow ? 6f : 0f));
            if (shadow)
            {
                image.Set("color", Color.black);
                image.Set("alpha", 0.35f);
            }
            _art!.Call("AddChild", image);
        }
        _portrait!.Set("visible", false);
    }

    private static Color? Hex(string? text) =>
        text is { Length: 7 } && text[0] == '#' && uint.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f) : null;
}
