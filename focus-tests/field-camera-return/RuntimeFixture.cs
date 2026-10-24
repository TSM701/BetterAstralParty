#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; } }
    public static class Time { public static int frameCount; }
    public static class Application { public static bool isFocused = true; }
    public static class Input
    {
        public static bool Down, Up;
        public static bool[] Held = new bool[3];
        public static int touchCount;
        public static Vector3 mousePosition;
        public static bool GetMouseButton(int button) => Held[button];
        public static bool GetMouseButtonDown(int button) => button == 0 && Down;
        public static bool GetMouseButtonUp(int button) => button == 0 && Up;
    }
}

namespace FieldCameraReturnTest
{
    // Read only the geometry/display data that the native clone actually uses.
    public sealed class NativeReturnAsset
    {
        private readonly byte[] _bytes;
        private int _position;
        private string[] _strings = Array.Empty<string>();
        internal static float Width, Height, DownScale;
        internal static readonly List<RuntimeObject> Children = new();
        internal static readonly Dictionary<string, string> Pages = new();
        private static readonly Dictionary<string, Vector2> ImageBorders = new();
        private NativeReturnAsset(byte[] bytes) { _bytes = bytes; }
        private byte Byte() => _bytes[_position++];
        private bool Bool() => Byte() != 0;
        private int Short() => Byte() * 256 + Byte();
        private int Int() => unchecked(Byte() << 24 | Byte() << 16 | Byte() << 8 | Byte());
        private float Float() => BitConverter.Int32BitsToSingle(Int());
        private string String() { var length = Short(); var value = System.Text.Encoding.UTF8.GetString(_bytes, _position, length); _position += length; return value; }
        private string Ref() { var index = Short(); return index == 65534 ? "<null>" : index == 65533 ? "" : _strings[index]; }
        private bool Seek(int start, int block)
        {
            var old = _position; _position = start; var count = Byte();
            if (block >= count) { _position = old; return false; }
            var small = Bool(); _position += block * (small ? 2 : 4); var offset = small ? Short() : Int();
            if (offset <= 0) { _position = old; return false; }
            _position = start + offset; return true;
        }
        private NativeReturnAsset Buffer()
        { var length = Int(); var value = new NativeReturnAsset(_bytes.AsSpan(_position, length).ToArray()) { _strings = _strings }; _position += length; return value; }
        private static void Require(bool valid) { if (!valid) throw new Exception("Native Return resource contract changed"); }
        public static void Load(byte[] bytes)
        {
            Children.Clear(); Pages.Clear(); ImageBorders.Clear(); var reader = new NativeReturnAsset(bytes);
            Require(reader.Int() == 0x46475549 && reader.Int() == 6 && !reader.Bool());
            reader.String(); reader.String(); reader._position += 20; var start = reader._position;
            Require(reader.Seek(start, 4)); reader._strings = new string[reader.Int()];
            for (var i = 0; i < reader._strings.Length; i++) reader._strings[i] = reader.String();
            Require(reader.Seek(start, 1)); var count = reader.Short(); var found = false;
            for (var i = 0; i < count; i++)
            {
                var length = reader.Int(); var end = reader._position + length;
                var type = reader.Byte(); var id = reader.Ref(); var name = reader.Ref(); reader.Ref(); reader.Ref(); reader.Bool();
                var width = reader.Int(); var height = reader.Int();
                if (type == 0 && reader.Byte() == 1)
                { reader.Int(); reader.Int(); var centreWidth = reader.Int(); var centreHeight = reader.Int(); ImageBorders.Add(id, new(width - centreWidth, height - centreHeight)); }
                if (type == 3 && name == "Button_ReturnRounded")
                { reader.Byte(); Width = width; Height = height; reader.Buffer().ReadComponent(); found = true; }
                reader._position = end;
            }
            Require(found && Width == 298 && Height == 102 && DownScale == .75f && Children.Count == 3);
            Require(Children.Select(child => child.Kind).SequenceEqual(new[] { "n42", "n48", "title" }));
            Require(Children[0].Width == 298 && Children[0].Height == 101 && Children[0].SizeRelations.SetEquals(new[] { 14, 15 }));
            Require(Children[1].Width == 298 && Children[1].Height == 101 && Children[1].SizeRelations.Count == 0);
            Require(Children[2].Width == 250 && Children[2].Height == 74 && Children[2].PivotX == .5f && Children[2].PivotY == .5f);
            Require(Pages.ContainsKey("up") && Pages.ContainsKey("over") && Pages.ContainsKey("down"));
            foreach (var child in Children.Where(child => child.Kind is "n42" or "n48"))
            { var border = ImageBorders[child.ImageSource]; child.BorderWidth = border.x; child.BorderHeight = border.y; Require(child.BorderHeight == 52); }
        }
        private void ReadComponent()
        {
            Require(Seek(0, 1)); var controllers = Short();
            for (var i = 0; i < controllers; i++)
            {
                var length = Short(); var start = _position; var end = start + length;
                Require(Seek(start, 0)); var name = Ref();
                Require(Seek(start, 1)); var pages = Short();
                for (var j = 0; j < pages; j++) { var id = Ref(); var page = Ref(); if (name == "button") Pages.Add(page, id); }
                _position = end;
            }
            Require(Seek(0, 2)); var children = Short();
            for (var i = 0; i < children; i++)
            {
                var length = Short(); var start = _position; var end = start + length;
                Require(Seek(start, 0)); Byte(); var source = Ref(); Ref(); Ref(); var child = new RuntimeObject(Ref()) { X = Int(), Y = Int(), ImageSource = source };
                Require(Bool()); child.Width = Int(); child.Height = Int(); Require(!Bool());
                if (Bool()) { child.ScaleX = Float(); child.ScaleY = Float(); }
                if (Bool()) _position += 8;
                if (Bool()) { child.PivotX = Float(); child.PivotY = Float(); Require(Bool()); }
                if (Seek(start, 3))
                {
                    var relations = Byte(); for (var j = 0; j < relations; j++)
                    { Require((short)Short() == -1); var n = Byte(); for (var k = 0; k < n; k++) { child.SizeRelations.Add(Byte()); Require(!Bool()); } }
                }
                Require(Seek(start, 2)); var gears = Short();
                for (var j = 0; j < gears; j++)
                {
                    var gearLength = Short(); var gearEnd = _position + gearLength; var type = Byte();
                    Require(type != 1 && type != 2); // No state-dependent XY/size to substitute with invented values.
                    if (type == 0) { Require(Short() == 0); var n = Short(); child.DisplayPages = new string[n]; for (var k = 0; k < n; k++) child.DisplayPages[k] = Ref(); }
                    _position = gearEnd;
                }
                Children.Add(child); _position = end;
            }
            Require(Seek(0, 5) && Short() == 0); // Hover is a display gear, not a size transition.
            Require(Seek(0, 6) && Byte() == 0); Ref(); Float(); Require(Byte() == 2); DownScale = Float();
        }
    }
    internal sealed class RuntimeObject
    {
        private static int _next;
        internal readonly IntPtr Pointer = new(++_next);
        internal readonly string Kind;
        internal RuntimeObject? Parent;
        internal readonly Dictionary<string, RuntimeObject> References = new();
        internal readonly List<RuntimeObject> Children = new();
        internal bool Visible = true, Touchable = true, Disposed, Opaque, ChangeState = true;
        internal bool Modal, Waiting, Popup;
        internal bool ContainsTestPoint;
        internal int SortingOrder, SortingWrites, PositionWrites, ScaleWrites, SizeWrites;
        internal float X, Y, Width = 298, Height = 102, ScaleX = 1, ScaleY = 1, Alpha = 1;
        internal float PivotX, PivotY, DownScale;
        internal bool DownScaled, NativePressed, PivotAsAnchor;
        internal float BorderWidth, BorderHeight;
        internal string ImageSource = "";
        internal float CentreFillHeight => Math.Max(0, Height - BorderHeight);
        internal readonly HashSet<int> SizeRelations = new();
        internal string[] DisplayPages = Array.Empty<string>();
        internal int FontSize, AutoSize = 3, AutoSizeWrites, MetricReads;
        internal float LastTextWidth, LastTextHeight;
        internal string Title = "";
        internal int Writes, DisposeCalls, FailDispose, FailVisible, FailDisposedRead;
        internal object? Boxed;
        internal RuntimeObject(string kind) { Kind = kind; }
        internal RuntimeObject? Get(string name) => name switch
        {
            "parent" => Parent,
            "relations" when Kind is "n42" or "n48" or "title" => References.GetValueOrDefault(name),
            "battle" when Kind == "Logic" => References.GetValueOrDefault(name),
            "ui" when Kind == "BattleInfo" => References.GetValueOrDefault(name),
            _ => throw new InvalidOperationException("Unexpected object getter " + Kind + "." + name)
        };
        internal T Get<T>(string name) where T : unmanaged
        {
            if (name == "isDisposed" && FailDisposedRead > 0) { FailDisposedRead--; throw new InvalidOperationException("Injected disposed getter"); }
            if (name is "textWidth" or "textHeight")
            {
                if (Kind != "title" || AutoSize != 0 || typeof(T) != typeof(float)) throw new InvalidOperationException("Unshrunk native text metric ABI/order");
                MetricReads++; if (ModFont.FailMetric) throw new InvalidOperationException("Injected native text getter failure");
                var metric = name == "textWidth" ? ModFont.WidthOverride ?? FontSize * (ModText.English ? 8 : 10) * ModFont.WidthFactor
                    : ModFont.HeightOverride ?? FontSize * 1.1f * ModFont.HeightFactor;
                if (name == "textWidth") LastTextWidth = metric; else LastTextHeight = metric;
                return (T)(object)metric;
            }
            object value = name switch
            {
                "isDisposed" => Disposed,
                "hasModalWindow" when Kind == "Root" => Modal,
                "modalWaiting" when Kind == "Root" => Waiting,
                "hasAnyPopup" when Kind == "Root" => Popup,
                "modal" when Kind is "Window" or "CardWindow" => Modal,
                "width" => Width, "height" => Height, "scaleX" => ScaleX, "scaleY" => ScaleY, "alpha" => Alpha,
                "pivotX" => PivotX, "pivotY" => PivotY,
                "xMin" when Kind == "Pin" => PivotAsAnchor ? X - Width * PivotX : X,
                "sortingOrder" => SortingOrder,
                _ => throw new InvalidOperationException("Unexpected typed getter " + Kind + "." + name)
            };
            return value is T typed ? typed : throw new InvalidCastException("Getter type " + name);
        }
        internal RuntimeObject? Field(string name)
        {
            if (!(Kind == "Battle" && name == "battleInfo" || Kind == "Field" && name == "com_MapInfo"
                || Kind == "Strip" && name == "btn_ShowMap")) throw new InvalidOperationException("Unexpected native field " + Kind + "." + name);
            return References.GetValueOrDefault(name);
        }
        internal T Value<T>() where T : unmanaged => Boxed is T value ? value : throw new InvalidCastException("Box type");
        internal T Field<T>(string name) where T : unmanaged
        {
            object value = Kind == "Button" ? name switch { "_downScaled" => DownScaled, "_downEffectValue" => DownScale,
                _ => throw new InvalidOperationException("Native button field ABI") } : throw new InvalidOperationException("Native field target");
            return value is T typed ? typed : throw new InvalidCastException("Native field type");
        }
        internal void Set(string name, object value)
        {
            Owned();
            if (name == "autoSize" && Kind == "title" && value is int autoSize && autoSize is 0 or 3)
            { AutoSize = autoSize; AutoSizeWrites++; return; }
            if (name == "sortingOrder")
            {
                if (value is not int order) throw new InvalidCastException("Sorting setter requires native Int32");
                SortingWrites++; NativeSortingChanged(order); return;
            }
            if (value is not bool flag || name is not ("visible" or "touchable" or "opaque")) throw new InvalidCastException("Setter signature " + name);
            if (name == "visible" && FailVisible > 0) { FailVisible--; throw new InvalidOperationException("Injected visibility write"); }
            if (name == "visible") Visible = flag;
            else if (name == "touchable") Touchable = flag;
            else Opaque = flag;
        }
        internal void SetField(string name, bool value)
        { Owned(); if (name != "changeStateOnClick") throw new InvalidOperationException("Forbidden native field write"); ChangeState = value; }
        internal RuntimeObject? Call(string name, params object?[] args)
        {
            if (name == "GetChild" && Kind == "Button" && args is [string childName]) return Children.SingleOrDefault(child => child.Kind == childName);
            if (name == "Add" && Kind == "Relations" && args is [RuntimeObject target, int relation])
            {
                Owned(); if (target != Parent!.Parent || relation != 24) throw new InvalidOperationException("Parent Size relation ABI");
                Parent.SizeRelations.Add(14); Parent.SizeRelations.Add(15); return null;
            }
            if (name == "GetTopWindow" && Kind == "Root" && args.Length == 0) return Check.TopWindow;
            if (name == "LocalToRoot" && Kind == "Strip")
            {
                var pin = References.GetValueOrDefault("btn_ShowMap");
                var right = pin == null || pin.Disposed ? Width : (pin.PivotAsAnchor ? pin.X - pin.Width * pin.PivotX : pin.X) + pin.Width;
                if (args is not [Vector2 point, RuntimeObject root] || root != GameUi.Root
                    || !point.x.Equals(right) || point.y != 0 && point.y != Height) throw new InvalidOperationException("LocalToRoot signature/resting strip anchor");
                Check.AnchorCalls++; Check.LastAnchor = this;
                for (RuntimeObject? node = this; node != root; node = node?.Parent)
                {
                    if (node == null) throw new InvalidOperationException("Detached anchor");
                    point = new(node.X + point.x * node.ScaleX, node.Y + point.y * node.ScaleY);
                }
                return new("Box") { Boxed = point };
            }
            if (name == "AddChild" && Kind == "Root" && args is [RuntimeObject child] && child.Kind == "Button")
            { NativeAddChild(child); return child; }
            if (name == "SetScale" && args is [float sx, float sy]) { Owned(); ScaleWrites++; ScaleX = sx; ScaleY = sy; return null; }
            if (name == "SetSize" && args is [float width, float height])
            {
                Owned(); SizeWrites++;
                foreach (var ownedChild in Children)
                { if (ownedChild.SizeRelations.Contains(14)) ownedChild.Width += width - Width; if (ownedChild.SizeRelations.Contains(15)) ownedChild.Height += height - Height; }
                Width = width; Height = height; return null;
            }
            if (name == "SetXY" && args is [float x, float y]) { Owned(); PositionWrites++; X = x; Y = y; return null; }
            if (name == "Dispose" && args.Length == 0)
            {
                Owned(); DisposeCalls++;
                if (FailDispose > 0) { FailDispose--; throw new InvalidOperationException("Injected disposal failure"); }
                Disposed = true; foreach (var ownedChild in Children) ownedChild.Disposed = true; Parent?.Children.Remove(this); Parent = null; return null;
            }
            throw new InvalidOperationException("Unexpected call/signature " + Kind + "." + name);
        }
        internal void Owned()
        {
            var owner = this; while (owner.Parent != null && owner.Kind != "Button") owner = owner.Parent;
            if (owner.Kind != "Button" || owner.Disposed || Disposed) throw new InvalidOperationException("Native/non-owned mutation"); Writes++;
        }
        internal void NativeState(string state)
        {
            foreach (var child in Children) child.Visible = child.DisplayPages.Length == 0 || child.DisplayPages.Contains(NativeReturnAsset.Pages[state]);
            if (state == "down" && !DownScaled) { DownScaled = true; ScaleX *= DownScale; ScaleY *= DownScale; }
            else if (state != "down" && DownScaled) { DownScaled = false; ScaleX /= DownScale; ScaleY /= DownScale; }
        }
        // Native sorted children form the ascending tail; ordinary late children cannot cross it.
        internal void NativeAddChild(RuntimeObject child)
        {
            child.Parent?.Children.Remove(child); child.Parent = this;
            var index = child.SortingOrder == 0 ? Children.Count(item => item.SortingOrder == 0)
                : Children.FindIndex(item => child.SortingOrder < item.SortingOrder);
            Children.Insert(index < 0 ? Children.Count : index, child);
        }
        internal void NativeSortingChanged(int order)
        {
            order = Math.Max(0, order); if (order == SortingOrder) return;
            SortingOrder = order;
            Parent?.NativeAddChild(this);
        }
        internal void NativeBringToFront()
        { if (SortingOrder == 0) Parent?.NativeAddChild(this); }
        internal static IntPtr FindClass(string ns, string name) => (ns, name) switch
        { ("GameLogic", "GameLogicManager") => (IntPtr)1, ("FairyGUI", "GObject") => (IntPtr)2, ("FairyGUI", "UIConfig") => (IntPtr)3, _ => throw new TypeLoadException(ns + "." + name) };
        internal static RuntimeObject? StaticField(IntPtr type, string name)
        {
            if (type == (IntPtr)1 && name == "_inst") return Check.Logic;
            if (type == (IntPtr)3 && name == "clickDragSensitivity") return new("Box") { Boxed = 8 };
            throw new InvalidOperationException("Static field ABI");
        }
        internal static RuntimeObject? StaticCall(IntPtr type, string name, params object?[] args)
        { if (type != (IntPtr)2 || name != "get_draggingObject" || args.Length != 0) throw new InvalidOperationException("Static call ABI"); return Check.Dragging; }
    }
    internal static class GameUi
    {
        internal static RuntimeObject? LiveDrag() => Check.Dragging;
        internal static RuntimeObject? Root;
        internal static readonly List<RuntimeObject> FreshHit = new(), StaleHit = new();
        internal static bool TreeHit;
        internal static IReadOnlyList<RuntimeObject> PointerPath(bool refresh = false)
        {
            Check.RefreshCalls++; if (!refresh) throw new InvalidOperationException("Must request fresh draw-order hit");
            if (TreeHit)
            {
                FreshHit.Clear();
                var top = Root?.Children.AsEnumerable().Reverse().FirstOrDefault(item => Visible(item) && item.Touchable && item.ContainsTestPoint);
                for (var node = top; node != null; node = node.Parent) FreshHit.Add(node);
            }
            return FreshHit;
        }
        internal static bool Visible(RuntimeObject? node)
        {
            if (node == null) return false;
            for (; node != null; node = node.Parent) if (node.Disposed || !node.Visible) return false;
            return true;
        }
        internal static void Dispose(RuntimeObject? node) { if (node != null && !node.Get<bool>("isDisposed")) node.Call("Dispose"); }
    }
    internal static class NativeUi
    {
        internal static readonly List<RuntimeObject> Buttons = new();
        internal static int Styled, Hovered;
        internal static bool FailCreate;
        internal static RuntimeObject? Create(string package, string item)
        {
            if (package != "Common" || item != "Button_ReturnRounded") throw new InvalidOperationException("Unexpected native asset");
            if (FailCreate) return null;
            var button = new RuntimeObject("Button") { ContainsTestPoint = true, Width = NativeReturnAsset.Width, Height = NativeReturnAsset.Height,
                PivotX = .5f, PivotY = .5f, DownScale = NativeReturnAsset.DownScale };
            foreach (var asset in NativeReturnAsset.Children)
            {
                var child = new RuntimeObject(asset.Kind) { Parent = button, X = asset.X, Y = asset.Y, Width = asset.Width, Height = asset.Height,
                    ScaleX = asset.ScaleX, ScaleY = asset.ScaleY, PivotX = asset.PivotX, PivotY = asset.PivotY, DisplayPages = asset.DisplayPages,
                    BorderWidth = asset.BorderWidth, BorderHeight = asset.BorderHeight };
                child.SizeRelations.UnionWith(asset.SizeRelations);
                child.References["relations"] = new("Relations") { Parent = child }; button.Children.Add(child);
            }
            button.NativeState("up"); Buttons.Add(button); return button;
        }
        __NATIVE_POSITION__
        internal static void StyleTitle(RuntimeObject button, string title, int size = 26)
        {
            button.Owned(); if (size < 1 || title != ModText.Text("자유 시점 카메라 종료")) throw new InvalidOperationException("Title contract");
            button.Title = title; var label = button.Children.Single(child => child.Kind == "title"); label.FontSize = size; label.AutoSize = 3; Styled++;
            if (ModFont.ChangeDuringStyle) { ModFont.ChangeDuringStyle = false; ModFont.Revision++; }
        }
        internal static void Play(RuntimeObject button, string name)
        { button.Owned(); if (name is not ("Switchin" or "Switchout")) throw new InvalidOperationException("Unexpected hover transition"); Hovered++; }
        internal static void NativeFrame(bool hit, bool down, bool up, bool held)
        {
            var button = Buttons.LastOrDefault(); if (button == null || button.Disposed) return;
            if (down && hit) button.NativePressed = true;
            if (up || !held) button.NativePressed = false;
            button.NativeState(button.NativePressed ? "down" : hit ? "over" : "up");
        }
    }
    internal static class ModUi { internal static bool IsOpen; }
    internal static class ModFont
    {
        internal static int Revision;
        internal static float WidthFactor = 1, HeightFactor = 1;
        internal static float? WidthOverride, HeightOverride;
        internal static bool FailMetric, ChangeDuringStyle;
    }
    internal static class Plugin
    {
        internal static readonly DiagnosticStub Diagnostics = new();
        internal sealed class DiagnosticStub
        {
            internal bool IsRecording => true;
            internal readonly HashSet<string> States = new();
            internal string Last = "";
            internal void State(string feature, string state)
            {
                if (feature != "fieldCameraReturn.gate" || state is not ("inactive-or-input-blocked" or "window-occluded"
                    or "hud-unavailable" or "anchor-unavailable" or "anchor-hidden" or "waiting-mouse-release" or "invalid-layout" or "visible"))
                    throw new InvalidOperationException("Return diagnostics must contain only fixed gate names");
                States.Add(state); Last = state;
            }
        }
    }
    internal static class ModText
    {
        internal static int Revision;
        internal static bool English;
        internal static string Text(string text) => text != "자유 시점 카메라 종료" ? throw new InvalidOperationException("Unexpected localization key") : English ? "Exit Free Camera" : text;
    }
    internal static class FieldFreeCamera { internal static int Returns; internal static void Return() => Returns++; }
    internal static class Compatibility
    {
        internal static bool Blocked;
        internal static readonly List<Exception> Errors = new();
        internal static bool Allowed(string feature) { if (feature != "FieldCameraReturn") throw new InvalidOperationException("Independent breaker required"); return !Blocked; }
        internal static void Block(string feature, Exception error, params Action[] cleanup)
        {
            if (feature != "FieldCameraReturn") throw new InvalidOperationException("Breaker scope");
            if (Blocked) return;
            Blocked = true; Errors.Add(error);
            foreach (var action in cleanup) { try { action(); } catch (Exception ex) { Errors.Add(ex); } }
        }
    }
    __PRODUCTION__

    public static class Check
    {
        internal static RuntimeObject? Logic, TopWindow, Dragging, LastAnchor;
        internal static int AnchorCalls, RefreshCalls;
        private static int _assertions;
        private static void Assert(bool ok, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0, string context = "")
        { _assertions++; if (!ok) throw new Exception("Return UI regression at fixture line " + line + " " + context); }
        private static void Near(float a, float b) => Assert(Math.Abs(a - b) < .001f);
        private sealed class Scene
        {
            internal RuntimeObject Root = new("Root"), Field = new("Field"), Strip = new("Strip"), Pin = new("Pin");
            internal Scene(float width = 1920, float height = 1080, RuntimeObject? topWindow = null)
            {
                FieldCameraReturnUi.Clear(); Compatibility.Blocked = false; Compatibility.Errors.Clear();
                NativeUi.Buttons.Clear(); NativeUi.Styled = NativeUi.Hovered = 0; NativeUi.FailCreate = false;
                GameUi.FreshHit.Clear(); GameUi.StaleHit.Clear(); GameUi.TreeHit = false; Dragging = LastAnchor = null; TopWindow = topWindow;
                ModUi.IsOpen = false; Application.isFocused = true; Input.touchCount = 0;
                Input.Held = new bool[3]; Input.Down = Input.Up = false; Input.mousePosition = new(100, 100);
                ModText.English = false; ModText.Revision++; FieldFreeCamera.Returns = 0;
                ModFont.Revision++; ModFont.WidthFactor = ModFont.HeightFactor = 1;
                ModFont.WidthOverride = ModFont.HeightOverride = null; ModFont.FailMetric = ModFont.ChangeDuringStyle = false;
                Root.Width = width; Root.Height = height; GameUi.Root = Root;
                if (topWindow != null) topWindow.Parent = Root;
                Field.Width = 1920; Field.Height = 1080; Field.Parent = Root; Field.SortingOrder = 1;
                Strip.Width = 480; Strip.Height = 36; Strip.X = 720; Strip.Y = 70; Strip.Parent = Field;
                Pin.Width = 145; Pin.Height = 36; Pin.X = 167; Pin.Parent = Strip;
                Strip.References["btn_ShowMap"] = Pin; Field.References["com_MapInfo"] = Strip;
                var info = new RuntimeObject("BattleInfo"); info.References["ui"] = Field;
                var battle = new RuntimeObject("Battle"); battle.References["battleInfo"] = info;
                Logic = new("Logic"); Logic.References["battle"] = battle;
                Resize(width, height); Frame();
            }
            internal RuntimeObject Button => NativeUi.Buttons.Last();
            internal void Resize(float width, float height)
            { Root.Width = width; Root.Height = height; Field.ScaleX = Field.ScaleY = Math.Min(width / 1920, height / 1080); }
            internal void ReplaceField()
            {
                Field = new("Field") { Parent = Root, SortingOrder = Field.SortingOrder }; Strip = new("Strip") { Parent = Field, X = 720, Y = 70, Width = 480, Height = 36 };
                Pin = new("Pin") { Parent = Strip, X = 167, Width = 145, Height = 36 };
                Strip.References["btn_ShowMap"] = Pin; Field.References["com_MapInfo"] = Strip;
                Logic!.References["battle"].References["battleInfo"].References["ui"] = Field;
                Resize(Root.Width, Root.Height);
            }
            internal void Frame(bool visible = true, bool down = false, bool up = false, bool held = false, bool hit = false, float x = 100, float y = 100, RuntimeObject? occluder = null)
            {
                Time.frameCount++; Input.Down = down; Input.Up = up; Input.Held[0] = held; Input.mousePosition = new(x, y);
                GameUi.FreshHit.Clear(); if (hit && NativeUi.Buttons.Count > 0) GameUi.FreshHit.Add(Button);
                for (var node = occluder; node != null; node = node.Parent) GameUi.FreshHit.Add(node);
                NativeUi.NativeFrame(hit, down, up, held);
                FieldCameraReturnUi.Tick(visible); Input.Down = Input.Up = false;
            }
            internal void Press() => Frame(down: true, held: true, hit: true);
            internal void Release(bool hit = true, float x = 100, float y = 100) => Frame(up: true, hit: hit, x: x, y: y);
            internal void Clean() { FieldCameraReturnUi.Clear(); Assert(Root.Children.All(child => child.Kind != "Button")); }
        }
        private static void LayoutAndLanguage()
        {
            foreach (var size in new[] { (1280f, 720f), (1920f, 1080f), (2560f, 1440f), (3840f, 2160f), (3440f, 1440f), (1280f, 1024f), (720f, 1280f) })
            {
                var s = new Scene(size.Item1, size.Item2); var button = s.Button;
                var unit = Math.Min(size.Item1 / 1920, size.Item2 / 1080);
                Near(button.Height * button.ScaleY, s.Strip.Height * unit * 1.25f);
                Assert(button.Width >= NativeReturnAsset.Width && button.Height >= NativeReturnAsset.Height);
                Near(button.X - button.Width * .5f * button.ScaleX, (720 + 167 + 145 + 18) * unit);
                Near(button.Y, (70 + 22.5f) * unit);
                Near(button.Y - button.Height * .5f * button.ScaleY, 70 * unit);
                Assert(button.Parent == s.Root && button.Visible && FieldCameraReturnUi.Visible && button.Opaque && !button.ChangeState && button.Title == "자유 시점 카메라 종료");
                var styled = NativeUi.Styled; s.Frame(); Assert(NativeUi.Styled == styled);
                ModText.English = true; ModText.Revision++; s.Frame(); Assert(button.Title == "Exit Free Camera" && NativeUi.Styled == styled + 1);
                s.Pin.Visible = false; s.Frame(); Assert(s.Button == button && LastAnchor == s.Strip && !button.Disposed);
                Near(s.Button.X - s.Button.Width * .5f * s.Button.ScaleX, (720 + 167 + 145 + 18) * unit);
                Near(s.Button.Y, (70 + 22.5f) * unit);
                Assert(s.Button.Y - s.Button.Height * .5f * s.Button.ScaleY >= 70 * unit - .001f);
                s.Strip.Visible = false; s.Frame(); Assert(s.Root.Children.Count == 0 && !FieldCameraReturnUi.Visible); s.Clean();
            }
            var resized = new Scene(); var original = resized.Button; resized.Resize(3840, 2160); resized.Frame();
            Assert(resized.Button == original); Near(original.Height * original.ScaleY, 90); resized.Clean();
            var moved = new Scene(); original = moved.Button;
            moved.Field.X = 21; moved.Field.Y = 15; moved.Strip.X = 510; moved.Strip.Y = 125;
            moved.Strip.ScaleX = 1.3f; moved.Strip.ScaleY = .8f; moved.Pin.X = 39; moved.Pin.Y = 11;
            moved.Pin.Width = 172; moved.Pin.Height = 48; moved.Pin.ScaleX = .75f; moved.Pin.ScaleY = 1.25f;
            moved.Frame(); Assert(moved.Button == original && original.Visible);
            Near(original.Height * original.ScaleY, 36 * .8f * 1.25f);
            Near(original.X - original.Width * .5f * original.ScaleX, 21 + 510 + (39 + 172) * 1.3f + 36 * .8f / 2);
            Near(original.Y, 15 + 125 + 36 * .8f * 1.25f / 2);
            Near(original.Y - original.Height * .5f * original.ScaleY, 15 + 125);
            var positionWrites = original.PositionWrites; var scaleWrites = original.ScaleWrites; var sizeWrites = original.SizeWrites;
            moved.Frame(); Assert(original.PositionWrites == positionWrites && original.ScaleWrites == scaleWrites && original.SizeWrites == sizeWrites);
            moved.Press(); moved.Strip.Height = 60; moved.Frame(held: true, hit: true); moved.Release();
            Assert(FieldFreeCamera.Returns == 0); Near(original.Height * original.ScaleY, 60 * .8f * 1.25f);
            Assert(original.Y - original.Height * .5f * original.ScaleY >= 15 + 125 - .001f);
            moved.Clean();
            var narrow = new Scene(); original = narrow.Button; narrow.Root.Width = 1060; narrow.Frame();
            Assert(narrow.Button == original && original.Visible);
            Near(original.X - original.Width * .5f * original.ScaleX, 1050);
            Near(original.X + original.Width * .5f * original.ScaleX, 1060);
            Near(original.Y, 70 + original.Height * original.ScaleY / 2);
            Near(original.Y - original.Height * .5f * original.ScaleY, 70); narrow.Clean();

            foreach (var invalid in new[] { "above", "left", "below", "no-width", "nonfinite-pin" })
            {
                var scene = new Scene(); original = scene.Button;
                if (invalid == "above") scene.Strip.Y = -1;
                if (invalid == "left") scene.Strip.X = -2000;
                if (invalid == "below") scene.Strip.Y = 1060;
                if (invalid == "no-width") scene.Root.Width = 1050;
                if (invalid == "nonfinite-pin") scene.Pin.X = float.NaN;
                scene.Frame(); Assert(original.Disposed && !FieldCameraReturnUi.Visible && FieldFreeCamera.Returns == 0 && !Compatibility.Blocked, context: invalid);
                scene.Clean();
            }
        }
        private static void NativeVisualStates()
        {
            void Bounds(RuntimeObject button, float unit, float rowHeight, bool pressed)
            {
                var normal = button.Children.Single(child => child.Kind == "n42");
                var hover = button.Children.Single(child => child.Kind == "n48");
                var title = button.Children.Single(child => child.Kind == "title");
                Near(normal.Width, button.Width); Near(normal.Height, button.Height - 1);
                Near(hover.Width, normal.Width); Near(hover.Height, normal.Height); Near(hover.X, normal.X); Near(hover.Y, normal.Y);
                Assert(button.Height >= NativeReturnAsset.Height && normal.Height >= 101 && hover.Height >= 101);
                Assert(normal.CentreFillHeight > 0 && hover.CentreFillHeight > 0);
                Near(title.Width, button.Width - (NativeReturnAsset.Width - NativeReturnAsset.Children[2].Width));
                Near(title.Height, button.Height - (NativeReturnAsset.Height - NativeReturnAsset.Children[2].Height));
                Near(title.X, button.Width / 2); Near(title.Y, button.Height / 2);
                Assert(title.FontSize == Math.Max(1, (int)Math.Floor(20 * NativeReturnAsset.Height / rowHeight))
                    && title.Height >= title.LastTextHeight && title.Width >= title.LastTextWidth && title.AutoSize == 3);
                var restingScale = unit * rowHeight * 1.25f / button.Height;
                Near(button.ScaleX, restingScale * (pressed ? NativeReturnAsset.DownScale : 1)); Near(button.ScaleY, button.ScaleX);
                Near(hover.Width * button.ScaleX, normal.Width * button.ScaleX);
                Near(hover.Height * button.ScaleY, normal.Height * button.ScaleY);
                Assert(button.Y - button.Height * .5f * button.ScaleY >= 70 * unit - .001f);
            }
            foreach (var scale in new[] { .6666667f, 1f, 2f })
            {
                var scene = new Scene(1920 * scale, 1080 * scale); var button = scene.Button;
                Bounds(button, scale, 36, false); Assert(!button.Children[1].Visible);
                scene.Frame(hit: true); Bounds(button, scale, 36, false); Assert(button.Children[1].Visible);
                scene.Press(); Bounds(button, scale, 36, true); scene.Frame(held: true, hit: true); Bounds(button, scale, 36, true);
                scene.Release(); Bounds(button, scale, 36, false); Assert(FieldFreeCamera.Returns == 1);
                ModText.English = true; ModText.Revision++; scene.Frame(hit: true); Bounds(button, scale, 36, false); Assert(button.Title == "Exit Free Camera");
                scene.Frame(); Bounds(button, scale, 36, false); Assert(!button.Children[1].Visible);
                var childWrites = button.Children.Sum(child => child.SizeWrites + child.PositionWrites);
                for (var i = 0; i < 100; i++) scene.Frame(hit: i % 2 == 0);
                Assert(button.Children.Sum(child => child.SizeWrites + child.PositionWrites) == childWrites);
                scene.Press(); scene.Resize(3840, 2160); scene.Strip.Height = 60; scene.Frame(held: true, hit: true);
                Bounds(button, 2, 60, true); scene.Release(); Bounds(button, 2, 60, false); Assert(FieldFreeCamera.Returns == 1);
                scene.Strip.Height = 24; scene.Frame(); Assert(button.Children[2].FontSize == 85); Bounds(button, 2, 24, false);
                scene.Clean(); Assert(button.Children.All(child => child.Disposed));
            }
            // Actual native SliceFill collapses its centre when height is below fixed border height.
            foreach (var image in NativeReturnAsset.Children.Take(2))
            { Assert(image.BorderHeight == 52 && image.CentreFillHeight == 49); Assert(Math.Max(0, 35 - image.BorderHeight) == 0); }
        }
        private static void Capture()
        {
            var s = new Scene(); s.Press(); Assert(FieldCameraReturnUi.ConsumedInput); s.Frame(held: true, hit: true);
            Assert(FieldCameraReturnUi.ConsumedInput); s.Release(); Assert(FieldFreeCamera.Returns == 1 && FieldCameraReturnUi.ConsumedInput);
            FieldCameraReturnUi.Tick(true); Assert(FieldFreeCamera.Returns == 1); s.Frame(); Assert(!FieldCameraReturnUi.ConsumedInput); s.Clean();
            s = new Scene(); s.Frame(down: true, held: true); s.Release(); Assert(FieldFreeCamera.Returns == 0 && !FieldCameraReturnUi.ConsumedInput); s.Clean();
            s = new Scene(); s.Press(); s.Release(hit: false); Assert(FieldFreeCamera.Returns == 0 && FieldCameraReturnUi.ConsumedInput); s.Clean();
            s = new Scene(); s.Press(); s.Release(x: 108); Assert(FieldFreeCamera.Returns == 1); s.Clean();
            s = new Scene(); s.Press(); s.Frame(held: true, hit: true, x: 109); s.Release(); Assert(FieldFreeCamera.Returns == 0); s.Clean();
            s = new Scene(); s.Press(); s.Release(x: 106, y: 106); Assert(FieldFreeCamera.Returns == 0); s.Clean();
            s = new Scene(); s.Press(); s.Frame(); s.Release(); Assert(FieldFreeCamera.Returns == 0); s.Clean();
            s = new Scene(); s.Press(); s.Resize(1280, 720); s.Frame(held: true, hit: true); s.Release(); Assert(FieldFreeCamera.Returns == 0); s.Clean();
            s = new Scene(); GameUi.StaleHit.Add(s.Button); s.Frame(down: true, held: true); s.Release(); Assert(FieldFreeCamera.Returns == 0); s.Clean();
            s = new Scene(); s.Frame(down: true, held: true, hit: true); s.Release(); Assert(FieldFreeCamera.Returns == 1 && RefreshCalls > 0); s.Clean();
            s = new Scene(); s.Clean(); s.Frame(down: true, held: true); Assert(s.Root.Children.Count == 0);
            s.Frame(up: true); Assert(s.Root.Children.Count == 0); s.Frame(); Assert(s.Root.Children.Count == 1); s.Clean();
            foreach (var button in new[] { 1, 2 }) { s = new Scene(); s.Clean(); Input.Held[button] = true; s.Frame(); Assert(s.Root.Children.Count == 0); Input.Held[button] = false; s.Frame(); Assert(s.Root.Children.Count == 1); s.Clean(); }
        }
        private static void PinIndependence()
        {
            var s = new Scene(); var button = s.Button; var nativePin = s.Pin;
            var pose = (button.X, button.Y, button.Width, button.Height, button.ScaleX, button.ScaleY);
            nativePin.PivotAsAnchor = true; nativePin.PivotX = nativePin.PivotY = .5f;
            nativePin.X += nativePin.Width / 2; nativePin.Y += nativePin.Height / 2;
            foreach (var scale in new[] { .75f, 1.5f, 0f })
            {
                nativePin.ScaleX = nativePin.ScaleY = scale; nativePin.Visible = false; nativePin.Alpha = 0; nativePin.Touchable = false;
                s.Frame(); Assert(s.Button == button && FieldCameraReturnUi.Visible && !button.Disposed && button.Touchable);
                Assert((button.X, button.Y, button.Width, button.Height, button.ScaleX, button.ScaleY) == pose);
            }
            s.Frame(down: true, held: true, occluder: nativePin); Assert(!FieldCameraReturnUi.ConsumedInput);
            s.Frame(up: true, occluder: nativePin); Assert(FieldFreeCamera.Returns == 0);
            s.Press(); nativePin.Visible = true; s.Release(); Assert(FieldFreeCamera.Returns == 1 && s.Button == button);
            s.Press(); s.Pin = new("Pin") { Parent = s.Strip, Width = 145, Height = 36, X = 167 };
            s.Strip.References["btn_ShowMap"] = s.Pin; s.Frame(held: true, hit: true); s.Release();
            Assert(FieldFreeCamera.Returns == 2 && s.Button == button && !button.Disposed);
            s.Pin.Disposed = true; s.Frame(); Assert(s.Button == button && FieldCameraReturnUi.Visible);
            Near(button.X - button.Width * .5f * button.ScaleX, 720 + 480 + 18);
            s.Strip.References.Remove("btn_ShowMap"); s.Frame(); Assert(s.Button == button && FieldCameraReturnUi.Visible);
            s.Press(); s.Release(); Assert(FieldFreeCamera.Returns == 3 && s.Button == button);
            Assert(nativePin.Writes == 0 && s.Pin.Writes == 0 && s.Strip.Writes == 0 && s.Field.Writes == 0 && s.Root.Writes == 0); s.Clean();
        }
        private static void TextMetrics()
        {
            var s = new Scene(); var button = s.Button; var label = button.Children[2];
            Assert(label.FontSize == 56 && label.MetricReads == 2 && label.AutoSizeWrites == 2 && label.AutoSize == 3);
            var width = button.Width; var styled = NativeUi.Styled; var reads = label.MetricReads;
            for (var i = 0; i < 100; i++) s.Frame(); Assert(NativeUi.Styled == styled && label.MetricReads == reads);
            ModFont.WidthFactor = 2; ModFont.HeightFactor = 2; ModFont.Revision++; s.Frame();
            Assert(button.Width > width && button.Height > NativeReturnAsset.Height && s.Button == button);
            Near(label.Width, label.LastTextWidth); Near(label.Height, label.LastTextHeight);
            Near(button.Height * button.ScaleY, s.Strip.Height * 1.25f);
            Near(button.Y - button.Height * .5f * button.ScaleY, s.Strip.Y);
            Assert(label.AutoSize == 3 && label.MetricReads == reads + 2 && NativeUi.Styled == styled + 1);
            ModText.English = true; ModText.Revision++; s.Frame(); Assert(button.Title == "Exit Free Camera" && label.MetricReads == reads + 4);
            ModFont.ChangeDuringStyle = true; ModFont.Revision++; s.Frame(); styled = NativeUi.Styled; reads = label.MetricReads;
            for (var i = 0; i < 100; i++) s.Frame(); Assert(NativeUi.Styled == styled && label.MetricReads == reads && label.AutoSize == 3);
            s.Clean();
            foreach (var metric in new[] { "width", "height" })
                foreach (var invalid in new[] { float.NaN, float.PositiveInfinity })
                {
                    s = new Scene(); button = s.Button;
                    if (metric == "width") ModFont.WidthOverride = invalid; else ModFont.HeightOverride = invalid;
                    ModFont.Revision++; s.Frame(); Assert(button.Disposed && !FieldCameraReturnUi.Visible && !Compatibility.Blocked);
                    ModFont.WidthOverride = ModFont.HeightOverride = null; ModFont.Revision++; s.Frame();
                    Assert(FieldCameraReturnUi.Visible && s.Button != button); s.Clean();
                }
            s = new Scene(); button = s.Button; ModFont.FailMetric = true; ModFont.Revision++; s.Frame();
            Assert(button.Disposed && !FieldCameraReturnUi.Visible && Compatibility.Blocked && FieldFreeCamera.Returns == 0); s.Clean();
        }
        private static void GatesAndLifecycle()
        {
            foreach (var gate in new Action<Scene>[] {
                s => Application.isFocused = false, s => ModUi.IsOpen = true, s => Input.touchCount = 1,
                s => s.Root.Modal = true, s => s.Root.Waiting = true, s => s.Root.Popup = true,
                s => TopWindow = new("Window") { Modal = true }, s => Dragging = new("NativeDrag") })
            {
                var s = new Scene(); var button = s.Button; s.Press(); gate(s); s.Frame(held: true, hit: true);
                Assert(s.Button == button && !button.Disposed && button.Visible && button.Touchable && FieldCameraReturnUi.Visible);
                Assert(FieldFreeCamera.Returns == 0 && FieldCameraReturnUi.ConsumedInput);
                s.Frame(held: true, hit: true); Assert(FieldCameraReturnUi.ConsumedInput);
                s.Release(); Assert(FieldFreeCamera.Returns == 0 && FieldCameraReturnUi.ConsumedInput); s.Clean();
                s = new Scene(); button = s.Button; gate(s); s.Frame(down: true, held: true, hit: true);
                Assert(FieldCameraReturnUi.Visible && !FieldCameraReturnUi.ConsumedInput && FieldFreeCamera.Returns == 0);
                Application.isFocused = true; ModUi.IsOpen = false; Input.touchCount = 0;
                s.Root.Modal = s.Root.Waiting = s.Root.Popup = false; TopWindow = Dragging = null;
                s.Frame(held: true, hit: true); s.Release(); Assert(FieldFreeCamera.Returns == 0);
                s.Press(); s.Release(); Assert(FieldFreeCamera.Returns == 1 && s.Button == button); s.Clean();
            }
            foreach (var gate in new Action<Scene>[] {
                s => GameUi.Root = null,
                s => s.Field.Visible = false, s => s.Field.Alpha = 0, s => s.Field.ScaleX = 0,
                s => s.Root.Disposed = true, s => s.Field.Disposed = true,
                s => s.Strip.Disposed = true, s => { s.Pin.Visible = false; s.Strip.Height = 0; } })
            {
                var s = new Scene(); var button = s.Button; s.Press(); gate(s); s.Frame(held: true, hit: true);
                Assert(button.Disposed && FieldFreeCamera.Returns == 0 && FieldCameraReturnUi.ConsumedInput);
                s.Release(); Assert(FieldFreeCamera.Returns == 0); s.Clean();
            }
            var recovered = new Scene(); recovered.Press(); Application.isFocused = false; recovered.Frame(held: true);
            var recoveredButton = recovered.Button;
            Application.isFocused = true; recovered.Frame(held: true, hit: true);
            Assert(FieldCameraReturnUi.Visible && recovered.Button == recoveredButton && FieldCameraReturnUi.ConsumedInput);
            recovered.Release(); Assert(FieldFreeCamera.Returns == 0); recovered.Press(); recovered.Release();
            Assert(FieldCameraReturnUi.Visible && FieldFreeCamera.Returns == 1); recovered.Clean();
            // A hand-origin gesture never owns this button; returning after it needs no recreation.
            var hand = new Scene(); var handButton = hand.Button;
            hand.Frame(down: true, held: true); Dragging = new("NativeHandCard"); hand.Frame(held: true);
            Assert(hand.Button == handButton && FieldCameraReturnUi.Visible && !FieldCameraReturnUi.ConsumedInput);
            Dragging = null; hand.Release(hit: true); Assert(FieldFreeCamera.Returns == 0);
            hand.Press(); hand.Release(); Assert(FieldFreeCamera.Returns == 1 && hand.Button == handButton); hand.Clean();
            var hidden = new Scene(); hidden.Press(); hidden.Frame(visible: false, held: true); Assert(FieldCameraReturnUi.ConsumedInput && hidden.Button.Disposed); hidden.Release(); Assert(FieldFreeCamera.Returns == 0); hidden.Clean();
            foreach (var replace in new Action<Scene>[] {
                s => s.ReplaceField(),
                s => { s.Root = new("Root") { Width = 1920, Height = 1080 }; GameUi.Root = s.Root; s.Field.Parent = s.Root; } })
            {
                var s = new Scene(); var button = s.Button; s.Press(); replace(s); s.Frame(held: true, hit: true);
                Assert(button.Disposed && FieldCameraReturnUi.ConsumedInput); s.Release(); Assert(FieldFreeCamera.Returns == 0);
                s.Frame(); Assert(!s.Button.Disposed && s.Button.Parent == s.Root); s.Clean();
            }
            var disposed = new Scene(); var old = disposed.Button; old.Call("Dispose"); Assert(!FieldCameraReturnUi.Visible); disposed.Frame(); Assert(old != disposed.Button && !disposed.Button.Disposed); disposed.Clean();
            var reparented = new Scene(); old = reparented.Button; old.Parent!.Children.Remove(old);
            old.Parent = new("OtherParent"); old.Parent.Children.Add(old); reparented.Frame(); Assert(old.Disposed && old != reparented.Button); reparented.Clean();
            foreach (var bad in new[] { 0f, float.NaN, float.PositiveInfinity }) { var s = new Scene(); s.Root.Width = bad; s.Frame(); Assert(s.Button.Disposed); s.Clean(); }
            var native = new Scene(); native.Frame(hit: true); native.Press(); native.Release(); Assert(native.Pin.Writes == 0 && native.Strip.Writes == 0 && native.Field.Writes == 0 && native.Root.Writes == 0); native.Clean();
        }
        private static void OrdinaryFieldWindows()
        {
            // A normal card-use window does not disable an unobscured camera exit.
            var card = new RuntimeObject("CardWindow"); var cardScene = new Scene(topWindow: card);
            var cardButton = cardScene.Button;
            cardScene.Frame(down: true, held: true); cardScene.Release();
            Assert(cardScene.Button == cardButton && cardButton.Visible && cardButton.Touchable && FieldFreeCamera.Returns == 0);
            cardScene.Press(); cardScene.Release(); Assert(FieldFreeCamera.Returns == 1 && cardScene.Button == cardButton);
            cardScene.Frame(occluder: card); cardScene.Press(); cardScene.Frame(up: true, hit: true, occluder: card);
            Assert(FieldFreeCamera.Returns == 1 && FieldCameraReturnUi.Visible && card.Writes == 0); cardScene.Clean();
            // Native expression/chat and operation timer windows are ordinary root children.
            var window = new RuntimeObject("Window");
            var s = new Scene(topWindow: window);
            Assert(FieldCameraReturnUi.Visible && s.Root.Children.Count == 1 && !window.Modal);
            Assert(Plugin.Diagnostics.Last == "visible");
            var button = s.Button;
            s.Frame(); Assert(s.Button == button && !button.Disposed);
            s.Press(); s.Release(); Assert(FieldFreeCamera.Returns == 1);
            Assert(window.Writes == 0); s.Clean();

            window = new("Window"); s = new Scene(topWindow: window); button = s.Button;
            var windowChild = new RuntimeObject("NativeWindowChild") { Parent = window };
            s.Frame(occluder: windowChild);
            Assert(s.Button == button && !button.Disposed && FieldCameraReturnUi.Visible && FieldFreeCamera.Returns == 0);
            Assert(Plugin.Diagnostics.Last == "window-occluded");
            s.Frame(); Assert(FieldCameraReturnUi.Visible && s.Button == button);
            s.Press(); button = s.Button; s.Frame(held: true, hit: true, occluder: windowChild);
            Assert(!button.Disposed && FieldCameraReturnUi.Visible && FieldCameraReturnUi.ConsumedInput);
            s.Release(); Assert(FieldFreeCamera.Returns == 0); s.Frame(); s.Clean();

            foreach (var modal in new[] { false, true })
            {
                window = new("Window") { Visible = false, Modal = modal };
                s = new Scene(topWindow: window); Assert(FieldCameraReturnUi.Visible);
                button = s.Button; s.Frame(occluder: window);
                Assert(s.Button == button && !button.Disposed && FieldCameraReturnUi.Visible); s.Clean();
            }
            window = new("Window") { Modal = true }; s = new Scene(topWindow: window);
            Assert(FieldCameraReturnUi.Visible && s.Root.Children.Count == 1);
            Assert(Plugin.Diagnostics.Last == "window-occluded");
            window.Modal = false; s.Frame(); Assert(FieldCameraReturnUi.Visible); s.Clean();

            window = new("Window"); s = new Scene(topWindow: window);
            GameUi.StaleHit.Add(window); s.Frame(); Assert(FieldCameraReturnUi.Visible);
            s.Clean(); Assert(window.Writes == 0);
        }
        private static void CleanupRetry()
        {
            var s = new Scene(); var button = s.Button; s.Press(); button.FailDispose = 2; s.Frame(visible: false, held: true);
            Assert(!button.Disposed && !button.Visible && !button.Touchable && button.DisposeCalls == 2 && FieldCameraReturnUi.ConsumedInput && Compatibility.Blocked);
            s.Frame(visible: false); Assert(button.Disposed && button.DisposeCalls == 3); s.Clean();
            foreach (var inject in new Action<RuntimeObject>[] { b => b.FailVisible = 1, b => b.FailDisposedRead = 1 })
            { s = new Scene(); button = s.Button; inject(button); s.Frame(visible: false); Assert(button.Disposed && Compatibility.Blocked); s.Clean(); }
            s = new Scene(); s.Clean(); NativeUi.FailCreate = true; s.Frame(); Assert(!FieldCameraReturnUi.Visible && Compatibility.Blocked && FieldFreeCamera.Returns == 0); s.Clean();
        }
        private static void ForegroundOrdering()
        {
            var s = new Scene(); var button = s.Button;
            Assert(button.SortingOrder == s.Field.SortingOrder && button.SortingWrites == 1);
            var pose = (button.X, button.Y, button.ScaleX, button.ScaleY, button.Width, button.Height);
            var writes = (button.SortingWrites, button.PositionWrites, button.ScaleWrites, button.SizeWrites, NativeUi.Styled);
            var siblings = new List<RuntimeObject>();
            foreach (var order in new[] { 0, s.Field.SortingOrder - 1 })
            {
                var hud = new RuntimeObject("NativeHud") { SortingOrder = order, ContainsTestPoint = true };
                s.Root.NativeAddChild(hud); siblings.Add(hud);
                Assert(s.Root.Children.Last() == button);
            }
            var window = new RuntimeObject("Window") { SortingOrder = s.Field.SortingOrder + 1 };
            s.Root.NativeAddChild(window); siblings.Add(window); TopWindow = window;
            siblings[0].NativeBringToFront();
            GameUi.TreeHit = true;
            Assert(s.Root.Children.Last() == window && s.Root.Children.IndexOf(button) > s.Root.Children.IndexOf(siblings[0]) && GameUi.PointerPath(refresh: true)[0] == button);
            for (var frame = 0; frame < 100; frame++) s.Frame();
            Assert(FieldCameraReturnUi.Visible && s.Button == button && s.Root.Children.Last() == window);
            Assert((button.SortingWrites, button.PositionWrites, button.ScaleWrites, button.SizeWrites, NativeUi.Styled) == writes);
            Assert((button.X, button.Y, button.ScaleX, button.ScaleY, button.Width, button.Height) == pose);
            s.Press(); s.Release(); Assert(FieldFreeCamera.Returns == 1);
            window.ContainsTestPoint = true; s.Frame();
            Assert(s.Button == button && !button.Disposed && FieldCameraReturnUi.Visible && Plugin.Diagnostics.Last == "window-occluded");
            window.ContainsTestPoint = false; s.Frame();
            Assert(FieldCameraReturnUi.Visible && s.Root.Children.Last() == window && s.Button.SortingOrder == s.Field.SortingOrder);
            foreach (var gate in new Action[] { () => s.Root.Modal = true, () => s.Root.Popup = true, () => s.Root.Waiting = true })
            {
                button = s.Button; s.Press(); gate(); s.Frame(held: true);
                Assert(s.Button == button && !button.Disposed && FieldCameraReturnUi.Visible && FieldCameraReturnUi.ConsumedInput);
                s.Release(); Assert(FieldFreeCamera.Returns == 1);
                s.Root.Modal = s.Root.Popup = s.Root.Waiting = false;
                s.Frame(); Assert(FieldCameraReturnUi.Visible && s.Button.SortingOrder == s.Field.SortingOrder && s.Root.Children.Last() == window);
            }
            s.Clean();
            Assert(siblings.All(hud => !hud.Disposed && hud.Parent == s.Root && hud.Writes == 0));
            s = new Scene(); s.Clean(); s.Field.SortingOrder = 37; s.Root.NativeAddChild(s.Field); s.Frame();
            Assert(s.Button.SortingOrder == 37 && s.Root.Children.IndexOf(s.Button) > s.Root.Children.IndexOf(s.Field) && s.Field.Writes == 0); s.Clean();
        }
        public static void Run()
        {
            LayoutAndLanguage(); NativeVisualStates(); PinIndependence(); TextMetrics(); Capture(); GatesAndLifecycle(); OrdinaryFieldWindows(); CleanupRetry(); ForegroundOrdering();
            Assert(AnchorCalls > 0 && NativeUi.Buttons.All(button => button.Disposed));
            Assert(Plugin.Diagnostics.States.SetEquals(new[] { "inactive-or-input-blocked", "window-occluded", "hud-unavailable",
                "anchor-unavailable", "anchor-hidden", "waiting-mouse-release", "invalid-layout", "visible" }));
            Console.WriteLine("Return UI assertions: " + _assertions);
        }
    }
}
