using UnityEngine;

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
            if (Math.Abs(Mathf.DeltaAngle(angle, _spin)) <= .1f) return;
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
        internal void Place(Vector2 at, Vector2 x, Vector2 y)
        {
            var (turn, scaleX, scaleY, spin) = ShapeRaster.Ground(x.x, x.y, y.x, y.y);
            if (!float.IsFinite(at.x + at.y + turn + scaleX + scaleY + spin) || scaleY <= 0) { Show(false); return; }
            if (!(Math.Abs(at.x - _x) <= .05f && Math.Abs(at.y - _y) <= .05f))
            {
                _x = at.x; _y = at.y;
                Holder.Call("SetXY", at.x, at.y);
            }
            if (Math.Abs(Mathf.DeltaAngle(turn, _turn)) > .1f)
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
            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + .25f;
                Scan();
            }
            if (_layer == null) return;
            if (_layer.Get<bool>("isDisposed")) { Clear(); return; } // The battle UI went with its scene.
            Layout();
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
            if (layer.Call("GetChildIndex", Ordered[i].Group)!.Value<int>() != index) layer.Call("SetChildIndex", Ordered[i].Group, index);
        }
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("walkDirection", $"mode={MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value)}; units={Units.Count}; marks={marks}");
    }

    private static void Layout()
    {
        if (_camera is not { } camera || !camera || !camera.enabled) { HideAll(); return; }
        // The layer's GlobalToLocal is affine; three calls a frame map every point.
        var origin = Local(0, 0);
        var stepX = (Local(1000, 0) - origin) / 1000;
        var stepY = (Local(0, 1000) - origin) / 1000;
        var height = Screen.height;
        Vector2 Project(Vector3 world)
        {
            var point = camera.WorldToScreenPoint(world);
            return point.z > 0 ? origin + stepX * point.x + stepY * (height - point.y) : new Vector2(float.NaN, float.NaN);
        }
        // The ground's look at a point: its screen point and the screen steps of one ground unit along `along` and a quarter
        // turn clockwise from it seen from above (toward the camera for the camera's right), measured `reach` either side.
        (Vector2 At, Vector2 Along, Vector2 Across) Look(Vector3 at, Vector3 along, float reach)
        {
            var across = Vector3.Cross(Vector3.up, along);
            Vector2 a = Project(at + along * reach), b = Project(at - along * reach), c = Project(at + across * reach), d = Project(at - across * reach);
            return ((a + b + c + d) / 4, (a - b) / (2 * reach), (c - d) / (2 * reach));
        }
        var circles = MenuLayout.WalkDirectionMode(Plugin.WalkDirection.Value) == "Circles";
        var right = camera.transform.right;
        right.y = 0;
        right.Normalize();
        foreach (var unit in Ordered)
        {
            Transform? ring = null;
            foreach (var arrow in unit.Arrows)
            {
                var live = arrow.Effect != null && arrow.Effect && arrow.Ring != null && arrow.Ring
                    && arrow.Effect.parent is { } parent && parent.Pointer == unit.Container.Pointer
                    && arrow.Effect.gameObject.activeInHierarchy;
                if (!live) { arrow.Hide(); arrow.Effect = null; continue; } // A pooled mark may come back elsewhere: look it up again.
                ring ??= arrow.Ring;
            }
            if (ring == null || !circles) unit.Ring.Show(false);
            if (ring == null) continue;
            var centre = ring.position;
            var scale = ring.lossyScale.x;
            var ground = Look(centre, right, RingMiddle * scale);
            var perPixel = 1 / ground.Along.magnitude; // Ground units per screen pixel across the ring.
            // The drafts' arrow size: 20 px for a ring 46 px across horizontally, growing with the root of the ring.
            var size = 20 * Mathf.Sqrt(RingOuter * scale / perPixel / 46);
            if (circles)
            {
                var pixel = RingMiddle * scale / RingRadius; // Ground units per mask pixel.
                unit.Ring.Place(ground.At, ground.Along * pixel, ground.Across * pixel);
            }
            foreach (var arrow in unit.Arrows)
            {
                if (arrow.Effect == null) continue;
                var forward = arrow.Effect.forward;
                forward.y = 0;
                forward.Normalize();
                // The arrowhead lies on the ground just past the ring's middle; its size stays in screen pixels as drawn across.
                var at = centre + forward * (RingMiddle * scale + size * .55f * perPixel);
                var step = size * perPixel / ArrowLength; // Ground units per mask pixel.
                var look = Look(at, forward, ArrowLength * step / 2);
                arrow.Look.Place(look.At, look.Along * step, look.Across * step);
            }
        }
    }

    private static Vector2 Local(float x, float y) => _layer!.Call("GlobalToLocal", new Vector2(x, y))!.Value<Vector2>();

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
