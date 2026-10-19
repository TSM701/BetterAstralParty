#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float d) => new(a.x * d, a.y * d);
        public static Vector2 up => new(0, 1);
    }
    public sealed class Camera
    {
        public Camera(IntPtr pointer) => throw new Exception("Unexpected field camera in input fixture");
        public bool enabled => false;
        public Vector3 WorldToScreenPoint(Vector3 point) => point;
    }
    public static class Screen { public static int height => throw new Exception("Unexpected screen read in input fixture"); }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => default;
    }
    public readonly record struct Color(float r, float g, float b);
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float unscaledTime; }
    public static class Input
    {
        public static bool Down, Up, Held;
        public static Vector3 mousePosition;
        public static bool GetMouseButtonDown(int button) => button == 0 && Down;
        public static bool GetMouseButtonUp(int button) => button == 0 && Up;
        public static bool GetMouseButton(int button) => button == 0 && Held;
    }
}

namespace FieldIndicatorInputTest
{
    using UnityEngine;

__PRODUCTION__

    internal sealed class RuntimeObject
    {
        private static int _next;
        internal readonly IntPtr Pointer = (IntPtr)(++_next);
        internal string TypeName => "UICom_AttrInfo";
        internal readonly Dictionary<string, object?> Values = new();
        internal int OpenCount;
        internal RuntimeObject() { Values["visible"] = true; }
        private static RuntimeObject Box(object? value)
        { var box = new RuntimeObject(); box.Values["value"] = value; return box; }
        internal RuntimeObject? Field(string name) => Get(name);
        internal RuntimeObject? Get(string name) => Values.TryGetValue(name, out var value) ? value as RuntimeObject : null;
        internal T Get<T>(string name) where T : unmanaged => Values.TryGetValue(name, out var value) ? (T)value! : default;
        internal T Value<T>() where T : unmanaged => (T)Values["value"]!;
        internal string String() => (string)Values["value"]!;
        internal void Set(string name, object? value) => Values[name] = value;
        internal void SetField(string name, object? value) => Set(name, value);
        internal RuntimeObject? Call(string name, params object?[] args)
        {
            switch (name)
            {
                case "GetNick": return Box("fixture");
                case "SwitchInfo": return null;
                case "AddChild": ((RuntimeObject)args[0]!).Set("parent", this); return (RuntimeObject)args[0]!;
                case "OpenBuffInfo": OpenCount++; return null;
                default: throw new MissingMethodException(name);
            }
        }
        internal static IntPtr FindClass(string ns, string name) => name == "UIConfig" ? (IntPtr)1 : (IntPtr)2;
        internal static RuntimeObject? StaticField(IntPtr type, string name)
        {
            if (type == (IntPtr)1 && name == "clickDragSensitivity") return Box(5);
            throw new MissingFieldException(name);
        }
        internal static RuntimeObject? StaticCall(IntPtr type, string name, params object?[] args)
        {
            if (name != "CreateInstance") throw new MissingMethodException(name);
            var plate = new RuntimeObject();
            foreach (var field in new[] { "txt_Name", "txt_Number", "txt_HP", "graph_Line", "graph_Guide", "graph_Sign", "list_Buff", "graph_ShowBuff" })
                plate.Values[field] = new RuntimeObject();
            return plate;
        }
        internal static void RequireClasses(string feature, params (string Namespace, string Name)[] types) => throw new Exception("Unexpected home scan");
    }
    internal static class GameUi
    {
        internal static RuntimeObject? Root = new();
        internal static bool HomeAvailable = false;
        internal static RuntimeObject[] Path = Array.Empty<RuntimeObject>();
        internal static bool Visible(RuntimeObject? item) => item != null && !item.Get<bool>("isDisposed") && item.Get<bool>("visible");
        internal static IEnumerable<RuntimeObject> PointerPath() => Path;
        internal static RuntimeObject? Find(RuntimeObject root, string type) => null;
        internal static void Dispose(RuntimeObject? item) => item?.Set("isDisposed", true);
    }
    internal static class FieldNameFont
    { internal static bool Apply(RuntimeObject label, RuntimeObject? ui) => true; }
    internal static class NativeUi
    {
        internal static RuntimeObject? Icon(RuntimeObject parent, string item, float x, float y, float size) => throw new Exception("Unexpected counter creation in click-only fixture");
        internal static void Position(RuntimeObject item, float x, float y, float scale) => throw new Exception("Unexpected counter layout in click-only fixture");
        internal static RuntimeObject Component(RuntimeObject parent, float width, float height) => throw new Exception("Unexpected layer creation in click-only fixture");
    }
    internal static class FieldZoomUi { internal static float IndicatorScale => 1f; }
    internal static class FieldMonsterPlates { internal static readonly List<RuntimeObject> Adopted=new(); internal static IReadOnlyList<RuntimeObject> Plates=>Adopted; }
    internal sealed class FloatSetting { internal float Value = 1f; }
    internal static class VisibleCombatAdvisor
    { internal static bool IsPve(int map) => map == 2; }
    internal static class ModUi { internal static bool IsOpen; }
    internal static class FieldIndicatorOpacity { internal static bool Hidden; }
    internal sealed class Setting { internal bool Value = true; }
    internal sealed class Diagnostics
    {
        internal bool IsRecording => false;
        internal void State(string key, string value) { }
        internal void Write(string value) { }
        internal void Error(string feature, Exception error) => throw new Exception("Unexpected diagnostic failure", error);
    }
    internal static class Plugin
    {
        internal static readonly Setting FieldBuffs = new();
        internal static readonly FloatSetting FieldIndicatorHeight = new();
        internal static readonly Diagnostics Diagnostics = new();
    }
    internal static class Compatibility
    {
        internal static bool Allowed(string feature) => true;
        internal static void Block(string feature, Exception error, Action cleanup) => throw new Exception("Production Tick failed", error);
    }

    public static class Check
    {
        private static readonly Type Ui = typeof(FieldBuffUi);
        private static readonly IDictionary Plates = (IDictionary)Ui.GetField("Plates", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        private static int _checks;
        private static RuntimeObject Seed()
        {
            var owner = new RuntimeObject(); owner.Set("Id", 1L);
            var player = new RuntimeObject(); player.Set("player", owner);
            var plate = Activator.CreateInstance(Ui.GetNestedType("Plate", BindingFlags.NonPublic)!, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { player, new RuntimeObject() }, null)!;
            var instance = (RuntimeObject)plate.GetType().GetField("Ui", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plate)!;
            instance.Set("visible", true);
            Plates.Add(instance.Pointer, plate);
            Ui.GetField("_scanAt", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, float.PositiveInfinity);
            return instance;
        }
        private static void Frame(RuntimeObject? hit, float x, float y, bool down = false, bool held = false, bool up = false)
        {
            Input.Down = down; Input.Held = held; Input.Up = up; Input.mousePosition = new Vector3(x, y, 0);
            GameUi.Path = hit == null ? Array.Empty<RuntimeObject>() : new[] { hit.Field("graph_ShowBuff")!, hit };
            FieldBuffUi.Tick();
        }
        private static void Assert(bool passed, string name)
        { if (!passed) throw new Exception("Field indicator input: " + name); _checks++; }
        private static RuntimeObject Reset()
        {
            Application.isFocused = true; Plugin.FieldBuffs.Value = true; ModUi.IsOpen = false; GameUi.Root = new();
            FieldIndicatorOpacity.Hidden = false;
            FieldBuffUi.Clear("fixture");
            return Seed();
        }
        public static void Run()
        {
            var plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 1, "ordinary public-effect click remains available");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Frame(plate, 103, 102, held: true); Frame(plate, 103, 102, up: true);
            Assert(plate.OpenCount == 1, "sub-threshold jitter remains a click");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Frame(null, 130, 100, held: true); Frame(plate, 100, 100, held: true); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "a drag out and back cannot open the effect popup");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Frame(plate, 106, 100, up: true);
            Assert(plate.OpenCount == 0, "release-only movement cancels click");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Frame(plate, 105, 100, up: true);
            Assert(plate.OpenCount == 1, "exact native drag threshold remains inclusive for clicks");
            plate = Reset(); Frame(null, 100, 100, down: true, held: true); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "drag entering a player indicator does not acquire a click");
            plate = Reset(); var other = Seed(); Frame(plate, 100, 100, down: true, held: true); Frame(other, 100, 100, up: true);
            Assert(plate.OpenCount == 0 && other.OpenCount == 0, "releasing over another indicator does not activate either");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Frame(null, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "release outside the originating indicator cancels click");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Application.isFocused = false; Frame(plate, 100, 100, held: true);
            Assert(FieldBuffUi.ActivePlateCount == 1, "focus loss preserves passive player plates");
            Application.isFocused = true; Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "focus restoration cannot revive a stale press");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Frame(plate, 100, 100); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "lost release clears pending click");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); var root = GameUi.Root; GameUi.Root = null; Frame(plate, 100, 100, held: true);
            GameUi.Root = root; Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "missing root cancels pending click without resurrecting it");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); ModUi.IsOpen = true; Frame(plate, 100, 100, held: true);
            Assert(FieldBuffUi.ActivePlateCount == 0, "opening settings clears owned plates");
            ModUi.IsOpen = false; plate = Seed(); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "settings closure cannot reuse a stale press");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); Plugin.FieldBuffs.Value = false; Frame(plate, 100, 100, held: true);
            Assert(FieldBuffUi.ActivePlateCount == 0, "OFF clears owned plates");
            Plugin.FieldBuffs.Value = true; plate = Seed(); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "re-enabling the setting does not revive a click");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); plate.Field("graph_ShowBuff")!.Set("visible", false); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "hidden public-effect affordance does not open a popup");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true); FieldBuffUi.Clear("fixture"); plate = Seed(); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "explicit cleanup cancels pending click");
            plate = Reset(); FieldIndicatorOpacity.Hidden = true;
            Ui.GetField("_scanAt", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0f);
            Frame(plate, 100, 100, down: true, held: true); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0 && FieldBuffUi.ActivePlateCount == 1 && !plate.Get<bool>("isDisposed"),
                "zoom-hidden plates retain their clones without scan or cached-pointer clicks");
            FieldIndicatorOpacity.Hidden = false;
            Ui.GetField("_scanAt", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, float.PositiveInfinity);
            Frame(plate, 100, 100, down: true, held: true); Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 1, "a fresh click works after zoom reveal");
            plate = Reset(); Frame(plate, 100, 100, down: true, held: true);
            FieldIndicatorOpacity.Hidden = true; Frame(plate, 100, 100, held: true);
            FieldIndicatorOpacity.Hidden = false; Frame(plate, 100, 100, up: true);
            Assert(plate.OpenCount == 0, "zoom hide cancels a pending click before reveal");
            plate = Reset(); FieldIndicatorOpacity.Hidden = true; Plugin.FieldBuffs.Value = false;
            Frame(plate, 100, 100);
            Assert(FieldBuffUi.ActivePlateCount == 0, "setting OFF still disposes zoom-hidden clones");
            plate = Reset(); FieldIndicatorOpacity.Hidden = true; ModUi.IsOpen = true; Frame(plate, 100, 100);
            Assert(FieldBuffUi.ActivePlateCount == 0, "settings still clear zoom-hidden clones");
            Console.WriteLine("PASS: " + _checks + " production field-indicator input assertions; actual native rendering remains untested.");
        }
    }
}
