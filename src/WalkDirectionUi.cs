using UnityEngine;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace BetterAstralParty;

// Move Direction Highlight (user-chosen 2026-11-06; drafts D and E in artifacts/cc/indicator-drafts). The native walk-direction
// marks (effect 37 WalkDirection, one per next land in CharacterMove._walkDirections, tinted GameConfig.slotColor, monsters
// slotColor[4]) are 3D: standees, monsters and trees hide them, and rings on one land pile up. A layer at the bottom of the
// battle UI draws over the field and under every panel, plate and card:
// - Arrows: an arrowhead on each ring where its native arrow leaves it, as Circles draws it (user-requested 2026-11-06:
//   lining same-way arrows on one land up outward read worse). The local player's marks are drawn on top.
// - Circles: each ring traced as well, its far half dashed so it reads as behind the standee, with its arrowheads.
// It only reads the native marks and follows them every frame, so it appears, moves and goes with them. Rings and arrowheads
// lie on the ground as the native marks do (user-requested 2026-11-06): each is fitted to the ground's screen look at its point.
internal static class WalkDirectionUi
{
    internal const string Feature = "WalkDirection";

    // The WalkDirection prefab: Rot/Ring is one particle quad 25 units wide (scaled with the hierarchy) lying on the ground with
    // texture yuanhuan_blj_05 (512 px), whose ring band runs from 150 to 173.5 px around its centre: in units, the band's
    // middle 7.91 and its outer edge 8.47.
    private const float RingMiddle = 7.91f, RingOuter = 8.47f;
    // Masks: an arrowhead 40 px long in 64 px and a ring of radius 64 px in 144 px. Outline bands as in the drafts (a 20 px
    // arrow has a 2.2 px white rim and a 1.4 px dark edge past it) and as for a 50 px ring, both 15 % wider because the
    // ground squashes them. Back halves: 8 dashes, as the drafts' 16 a turn.
    private const int ArrowSize = 64, RingSize = 144, Dashes = 8;
    private const float ArrowLength = 40, RingRadius = 64;
    private static readonly Color Dark = new(46 / 255f, 32 / 255f, 36 / 255f);

    private static readonly Dictionary<IntPtr, Unit> Units = new();
    private static readonly List<Unit> Ordered = new(); // Local player first: stacking order; drawn last, on top.
    private static readonly HashSet<IntPtr> Seen = new();
    private static readonly List<IntPtr> Stale = new();
    private static IntPtr _logicClass, _sceneClass, _configClass, _parent;
    private static RuntimeObject? _layer;
    private static Camera? _camera;
    private static float _scanAt;

    // A mask image at its mask's pixel size, centred on its holder, with the tint and turn last written.
    private sealed class Part
    {
        internal readonly RuntimeObject Image;
        private float _spin;
        private Color _color = Color.white;

        internal Part(RuntimeObject holder, string mask, Color color)
        {
            var size = mask.StartsWith("arrow", StringComparison.Ordinal) ? ArrowSize : RingSize;
            Image = mask switch
            {
                "arrow.dark" => SmoothShape.MaskImage(mask, ArrowSize, () => ShapeRaster.Arrowhead(ArrowSize, ArrowLength, ArrowLength * .207f)),
                "arrow.white" => SmoothShape.MaskImage(mask, ArrowSize, () => ShapeRaster.Arrowhead(ArrowSize, ArrowLength, ArrowLength * .127f)),
                "arrow.colour" => SmoothShape.MaskImage(mask, ArrowSize, () => ShapeRaster.Arrowhead(ArrowSize, ArrowLength, 0)),
                "ring.dark" => SmoothShape.MaskImage(mask, RingSize, () => ShapeRaster.Ring(RingSize, RingRadius, RingRadius * .17f, RingRadius * .115f, Dashes)),
                "ring.white" => SmoothShape.MaskImage(mask, RingSize, () => ShapeRaster.Ring(RingSize, RingRadius, RingRadius * .115f, 0, Dashes)),
                _ => SmoothShape.MaskImage(mask, RingSize, () => ShapeRaster.Ring(RingSize, RingRadius, RingRadius * .069f, RingRadius * .06f, Dashes))
            } ?? throw new TypeLoadException("SmoothShape masks unavailable");
            Image.Call("SetSize", (float)size, (float)size);
            Image.Call("SetPivot", .5f, .5f, true); // Positions name the image's centre: the holder's origin.
            holder.Call("AddChild", Image);
            Tint(color);
        }

        internal void Tint(Color color)
        {
            if (_color == color) return;
            _color = color;
            Image.Set("color", color);
        }

        internal void Spin(float angle)
        {
            if (Math.Abs(UnityMath.DeltaAngle(angle, _spin)) <= .1f) return;
            _spin = angle;
            Image.Set("rotation", angle);
        }
    }

    // Masks lying on the ground: a holder at the mark's screen point, turned and scaled to the ground's look there, whose
    // images turn inside it (ShapeRaster.Ground). The values last written are kept, so a still camera writes nothing.
    private sealed class Sticker
    {
        internal readonly RuntimeObject Holder;
        internal readonly Part[] Parts;
        private float _x = float.NaN, _y, _turn, _scaleX = 1, _scaleY = 1;
        private bool _shown = true;

        internal Sticker(RuntimeObject group, string kind, Color colour)
        {
            Holder = NativeUi.Component(group, 0, 0);
            Parts = new[] { new Part(Holder, kind + ".dark", Dark), new Part(Holder, kind + ".white", Color.white), new Part(Holder, kind + ".colour", colour) };
            Show(false);
        }

        internal void Show(bool shown)
        {
            if (_shown == shown) return;
            _shown = shown;
            Holder.Set("visible", shown);
        }

        // The holder at `at`; x and y are the screen steps of one mask pixel along the mask's axes there.
        internal void Place(NVector2 at, NVector2 x, NVector2 y)
        {
            var (turn, scaleX, scaleY, spin) = ShapeRaster.Ground(x.X, x.Y, y.X, y.Y);
            if (!float.IsFinite(at.X + at.Y + turn + scaleX + scaleY + spin) || scaleY <= 0) { Show(false); return; }
            if (!(Math.Abs(at.X - _x) <= .05f && Math.Abs(at.Y - _y) <= .05f))
            {
                _x = at.X; _y = at.Y;
                Holder.Call("SetXY", at.X, at.Y);
            }
            if (Math.Abs(UnityMath.DeltaAngle(turn, _turn)) > .1f)
            {
                _turn = turn;
                Holder.Set("rotation", turn);
            }
            if (Math.Abs(scaleX - _scaleX) > .002f || Math.Abs(scaleY - _scaleY) > .002f)
            {
                _scaleX = scaleX; _scaleY = scaleY;
                Holder.Call("SetScale", scaleX, scaleY);
            }
            foreach (var part in Parts) part.Spin(spin);
            Show(true);
        }
    }

    // One native mark (ring and arrow toward one next land) and the arrowhead drawn for it.
    private sealed class Arrow
    {
        internal Transform? Effect, Ring;
        internal readonly Sticker Look;
        internal Arrow(RuntimeObject group, Color colour) => Look = new Sticker(group, "arrow", colour);
        internal void Hide() => Look.Show(false);
        internal void Dispose() => GameUi.Dispose(Look.Holder); // Its images go with it.
    }

    private sealed class Unit
    {
        internal readonly RuntimeObject Group;
        internal readonly Sticker Ring;
        internal readonly List<Arrow> Arrows = new();
        internal Transform Container = null!;
        internal int Rank; // Rank: 0 local player, 1..4 players by slot, 5 monsters.
        internal Color Colour;

        internal Unit(RuntimeObject group, Color colour)
        {
            Group = group;
            Group.Set("touchable", false);
            Colour = colour;
            Ring = new Sticker(group, "ring", colour);
        }
    }

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            if (MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value) == "Off" || PvpSafety.Suspended || GameUi.Root == null) { Clear(); return; }
            var lap = RuntimeObject.LapStart();
            var now = Time.unscaledTime;
            if (now >= _scanAt)
            {
                _scanAt = now + .25f;
                Scan();
            }
            RuntimeObject.Lap("walk.scan", ref lap);
            if (_layer == null) return;
            if (_layer.Get<bool>("isDisposed")) { Clear(); return; } // The battle UI went with its scene.
            Layout();
            RuntimeObject.Lap("walk.layout", ref lap);
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    private static void Scan()
    {
        if (_logicClass == IntPtr.Zero)
        {
            _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            _sceneClass = RuntimeObject.FindClass("Core.Scene", "BattleSceneController");
            _configClass = RuntimeObject.FindClass("Core", "GameConfig");
            // Before the game's code has loaded: look again on the next scan.
            if (_logicClass == IntPtr.Zero || _sceneClass == IntPtr.Zero || _configClass == IntPtr.Zero) { _logicClass = IntPtr.Zero; return; }
        }
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (!VisibleCombatAdvisor.IsPve(room?.Field("info")?.Get<int>("MapType") ?? 0)
            || logic!.Get("fight")?.Field("fightStatus")?.Value<bool>() == true) { Clear(); return; }
        var battle = logic.Get("battle");
        var battleUi = battle?.Field("battleInfo")?.Get("ui");
        var players = battle?.Field("PlayerDatas");
        var camera = RuntimeObject.StaticField(_sceneClass, "inst")?.Get("mainCamera");
        if (battleUi == null || !GameUi.Visible(battleUi) || players == null || camera == null) { Clear(); return; }
        if (_layer == null || _layer.Get<bool>("isDisposed") || _parent != battleUi.Pointer)
        {
            Clear();
            _layer = NativeUi.Component(battleUi, 0, 0);
            _layer.Set("touchable", false);
            battleUi.Call("SetChildIndex", _layer, 0); // Under every battle panel, plate and card.
            _parent = battleUi.Pointer;
        }
        var layer = _layer;
        if (_camera == null || _camera.Pointer != camera.Pointer) _camera = new Camera(camera.Pointer);
        var self = battleUi.Field("_SelfPlayer")?.Field("CharacterInst")?.Pointer ?? IntPtr.Zero;
        var colours = RuntimeObject.StaticField(_configClass, "slotColor")!;
        Seen.Clear();
        var marks = 0;
        for (int i = 0, count = players.Get<int>("Count"); i < count; i++)
        {
            if (players.Call("get_Item", i) is not { } player || player.Field("CharacterInst") is not { } character
                || character.Field("move")?.Field("_walkDirections") is not { } list) continue;
            var effects = list.Get<int>("Count");
            if (effects == 0) continue;
            // The native tint: a monster's slot colour is 4, a player's its own slot.
            var monster = player.Get<int>("characterType") == 2;
            var slot = monster ? 4 : player.Field("player")!.Get<int>("Slot");
            var colour = colours.Call("get_Item", slot)!.Value<Color>();
            if (!Units.TryGetValue(character.Pointer, out var unit))
                Units[character.Pointer] = unit = new Unit(NativeUi.Component(layer, 0, 0), colour);
            Seen.Add(character.Pointer);
            if (unit.Colour != colour)
            {
                unit.Colour = colour;
                unit.Ring.Parts[2].Tint(colour);
                foreach (var arrow in unit.Arrows) arrow.Look.Parts[2].Tint(colour);
            }
            unit.Rank = character.Pointer == self ? 0 : monster ? 5 : 1 + slot;
            unit.Container = new Transform(character.Field("EffectContainer")!.Pointer);
            var used = 0;
            for (var k = 0; k < effects; k++)
            {
                var effect = list.Call("get_Item", k);
                if (effect == null) continue;
                var component = new Component(effect.Pointer);
                if (!component) continue; // Destroyed with its scene.
                var transform = component.transform;
                if (used == unit.Arrows.Count) unit.Arrows.Add(new Arrow(unit.Group, unit.Colour));
                var arrow = unit.Arrows[used++];
                if (arrow.Effect == null || arrow.Effect.Pointer != transform.Pointer)
                {
                    arrow.Effect = transform;
                    arrow.Ring = transform.Find("Rot/Ring") ?? throw new MissingMemberException("WalkDirection", "Rot/Ring");
                }
            }
            marks += used;
            while (unit.Arrows.Count > used) { unit.Arrows[^1].Dispose(); unit.Arrows.RemoveAt(unit.Arrows.Count - 1); }
        }
        Stale.Clear();
        foreach (var key in Units.Keys) if (!Seen.Contains(key)) Stale.Add(key);
        foreach (var key in Stale) { GameUi.Dispose(Units[key].Group); Units.Remove(key); }
        Ordered.Clear();
        Ordered.AddRange(Units.Values);
        Ordered.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        for (var i = 0; i < Ordered.Count; i++)
        {
            var index = Ordered.Count - 1 - i; // The local player's group last, on top.
            if (layer.IndexOfChild(Ordered[i].Group) != index) layer.Call("SetChildIndex", Ordered[i].Group, index);
        }
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("walkDirection", $"mode={MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value)}; units={Units.Count}; marks={marks}");
    }

    // Every UnityEngine value operation and Camera.WorldToScreenPoint is an il2cpp_runtime_invoke (UnityMath): with four
    // units this layout made about 300 a frame (2026-11-18). The camera's matrices and pixel rectangle are read once a frame
    // and points are projected in managed code as WorldToScreenPoint projects them; every 64th frame one ring centre is also
    // projected by the engine, and a miss over half a pixel turns the managed projection off for the session and is logged.
    private static bool _projectionOff;
    private static int _projectionFrames;

    private static void Layout()
    {
        if (_camera is not { } camera || !Alive(camera) || !camera.enabled) { HideAll(); return; }
        // The layer's GlobalToLocal is affine; three calls a frame map every point.
        var origin = Num(Local(0, 0));
        var stepX = (Num(Local(1000, 0)) - origin) / 1000;
        var stepY = (Num(Local(0, 1000)) - origin) / 1000;
        float height = Screen.height;
        var view = camera.worldToCameraMatrix;
        var projection = camera.projectionMatrix;
        var rect = camera.pixelRect;
        // Camera.WorldToScreenPoint: the clip-space point through both matrices, the viewport from the pixel rectangle and
        // the depth in front of the camera; zero for a point on the camera plane, as the engine returns.
        NVector3 ToScreen(NVector3 p)
        {
            if (_projectionOff) return Num(camera.WorldToScreenPoint(UnityMath.V3(p.X, p.Y, p.Z)));
            float vx = view.m00 * p.X + view.m01 * p.Y + view.m02 * p.Z + view.m03,
                vy = view.m10 * p.X + view.m11 * p.Y + view.m12 * p.Z + view.m13,
                vz = view.m20 * p.X + view.m21 * p.Y + view.m22 * p.Z + view.m23,
                vw = view.m30 * p.X + view.m31 * p.Y + view.m32 * p.Z + view.m33;
            float x = projection.m00 * vx + projection.m01 * vy + projection.m02 * vz + projection.m03 * vw,
                y = projection.m10 * vx + projection.m11 * vy + projection.m12 * vz + projection.m13 * vw,
                w = projection.m30 * vx + projection.m31 * vy + projection.m32 * vz + projection.m33 * vw;
            if (!(Math.Abs(w) > 1e-7f)) return default;
            return new(rect.m_XMin + (1 + x / w) * rect.m_Width * .5f, rect.m_YMin + (1 + y / w) * rect.m_Height * .5f, -vz);
        }
        NVector2 Project(NVector3 world)
        {
            var point = ToScreen(world);
            return point.Z > 0 ? origin + stepX * point.X + stepY * (height - point.Y) : new NVector2(float.NaN);
        }
        // The ground's look at a point: its screen point and the screen steps of one ground unit along `along` and a quarter
        // turn clockwise from it seen from above (toward the camera for the camera's right), measured `reach` either side.
        (NVector2 At, NVector2 Along, NVector2 Across) Look(NVector3 at, NVector3 along, float reach)
        {
            var across = new NVector3(along.Z, 0, -along.X); // Vector3.Cross(Vector3.up, along)
            NVector2 a = Project(at + along * reach), b = Project(at - along * reach), c = Project(at + across * reach), d = Project(at - across * reach);
            return ((a + b + c + d) / 4, (a - b) / (2 * reach), (c - d) / (2 * reach));
        }
        var circles = MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value) == "Circles";
        var right = Flat(new NVector3(view.m00, view.m01, view.m02)); // The view matrix's first row: the camera's right.
        var check = !_projectionOff && _projectionFrames++ % 64 == 0;
        foreach (var unit in Ordered)
        {
            Transform? ring = null;
            foreach (var arrow in unit.Arrows)
            {
                var live = Alive(arrow.Effect) && Alive(arrow.Ring)
                    && arrow.Effect!.parent is { } parent && parent.Pointer == unit.Container.Pointer
                    && arrow.Effect.gameObject.activeInHierarchy;
                if (!live) { arrow.Hide(); arrow.Effect = null; continue; } // A pooled mark may come back elsewhere: look it up again.
                ring ??= arrow.Ring;
            }
            if (ring is null || !circles) unit.Ring.Show(false);
            if (ring is null) continue;
            var centre = Num(ring.position);
            var scale = ring.lossyScale.x;
            if (check)
            {
                check = false;
                var managed = ToScreen(centre);
                var engine = camera.WorldToScreenPoint(UnityMath.V3(centre.X, centre.Y, centre.Z));
                if (!(Math.Abs(managed.X - engine.x) <= .5f && Math.Abs(managed.Y - engine.y) <= .5f && Math.Abs(managed.Z - engine.z) <= .01f + Math.Abs(engine.z) * 1e-4f))
                {
                    _projectionOff = true;
                    Plugin.Diagnostics.Write(FormattableString.Invariant($"walkDirection.projection off: managed=({managed.X:F2},{managed.Y:F2},{managed.Z:F3}); engine=({engine.x:F2},{engine.y:F2},{engine.z:F3})"));
                }
            }
            var ground = Look(centre, right, RingMiddle * scale);
            var perPixel = 1 / ground.Along.Length(); // Ground units per screen pixel across the ring.
            // The drafts' arrow size: 20 px for a ring 46 px across horizontally, growing with the root of the ring.
            var size = 20 * MathF.Sqrt(RingOuter * scale / perPixel / 46);
            if (circles)
            {
                var pixel = RingMiddle * scale / RingRadius; // Ground units per mask pixel.
                unit.Ring.Place(ground.At, ground.Along * pixel, ground.Across * pixel);
            }
            foreach (var arrow in unit.Arrows)
            {
                if (arrow.Effect is null) continue;
                var forward = Flat(Num(arrow.Effect.forward));
                // The arrowhead lies on the ground just past the ring's middle; its size stays in screen pixels as drawn across.
                var at = centre + forward * (RingMiddle * scale + size * .55f * perPixel);
                var step = size * perPixel / ArrowLength; // Ground units per mask pixel.
                var look = Look(at, forward, ArrowLength * step / 2);
                arrow.Look.Place(look.At, look.Along * step, look.Across * step);
            }
        }
    }

    private static Vector2 Local(float x, float y) => _layer!.Result<Vector2, Vector2>("GlobalToLocal", UnityMath.V2(x, y));
    private static NVector2 Num(Vector2 v) => new(v.x, v.y);
    private static NVector3 Num(Vector3 v) => new(v.x, v.y, v.z);
    // Vector3.Normalize after dropping the height: zero when shorter than 1e-5, as Unity returns.
    private static NVector3 Flat(NVector3 v)
    {
        v.Y = 0;
        var length = v.Length();
        return length > 1e-5f ? v / length : NVector3.Zero;
    }
    // UnityEngine.Object's bool test for scene objects without a call: the engine zeroes a destroyed object's cached pointer.
    private static bool Alive(UnityEngine.Object? obj) => obj is not null && obj.m_CachedPtr != IntPtr.Zero;

    private static void HideAll()
    {
        foreach (var unit in Units.Values)
        {
            unit.Ring.Show(false);
            foreach (var arrow in unit.Arrows) arrow.Hide();
        }
    }

    internal static void Clear()
    {
        if (_layer == null && Units.Count == 0) return;
        GameUi.Dispose(_layer); // Groups and images go with it.
        _layer = null;
        _camera = null;
        _parent = IntPtr.Zero;
        Units.Clear(); Ordered.Clear(); Seen.Clear(); Stale.Clear();
    }
}
