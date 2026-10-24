using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    internal static class Time { internal static float unscaledTime; }
    internal sealed class Font
    {
        internal Font(IntPtr pointer) { if (pointer != FontFallbackState.OsFont.Pointer) throw new Exception("Unexpected OS font"); }
        internal bool HasCharacter(char value) => FontFallbackState.OsHas(value);
        internal static string[] GetOSInstalledFontNames() { FontFallbackState.OsEnumerations++; return FontFallbackState.OsAvailable ? new[] { "Malgun Gothic" } : Array.Empty<string>(); }
    }
}
namespace TMPro
{
    internal sealed class TMP_FontAsset
    {
        internal TMP_FontAsset(IntPtr pointer) { if (pointer != FontFallbackState.Asset.Pointer) throw new Exception("Unexpected TMP asset"); }
        internal bool HasCharacter(char value, bool searchFallbacks, bool tryAddCharacter)
        {
            if (tryAddCharacter) { FontFallbackState.AtlasMutations++; throw new Exception("Unexpected atlas mutation"); }
            if (searchFallbacks) throw new Exception("Unexpected TMP fallback search");
            FontFallbackState.Characters.Add(value); return FontFallbackState.NativeHas(value);
        }
    }
}
internal sealed class RuntimeObject
{
    private static long _next;
    internal readonly IntPtr Pointer = new IntPtr(++_next);
    internal readonly string TypeName;
    internal readonly Dictionary<string, object?> Values = new();
    private readonly object? _scalar;
    internal RuntimeObject(string name = "Object", object? scalar = null) { TypeName = name; _scalar = scalar; Values["isDisposed"] = false; Values["visible"] = true; }
    internal RuntimeObject? Get(string name) => Values.TryGetValue(name, out var value) ? value as RuntimeObject : null;
    internal T Get<T>(string name) where T : unmanaged
    {
        if (name == "textWidth") { FontFallbackState.ForcedLayouts++; throw new Exception("Forced layout access"); }
        return Values.TryGetValue(name, out var value) && value is T result ? result : throw new Exception("Unexpected typed read: " + name);
    }
    internal RuntimeObject? Field(string name) => Values.TryGetValue(name, out var value) ? value as RuntimeObject ?? new RuntimeObject("Value", value) : null;
    internal T Value<T>() where T : unmanaged => _scalar is T value ? value : throw new Exception("Unexpected scalar");
    internal string String() => _scalar as string ?? throw new Exception("Unexpected string");
    internal void Set(string name, object value) => Values[name] = value;
    internal void SetField(string name, object value) => Values[name] = value;
    internal static IntPtr FindClass(string ns, string name) => ns == "FairyGUI" && name is "UIConfig" or "FontManager" or "TextFormat" ? (IntPtr)1 : throw new Exception("Unexpected class");
    internal static RuntimeObject StaticField(IntPtr type, string name) => name == "defaultFont" ? new RuntimeObject("String", "Native") : throw new Exception("Unexpected static field");
    internal static RuntimeObject StaticCall(IntPtr type, string name, params object[] args) => name == "GetFont" ? FontFallbackState.Fonts[(string)args[0]] : throw new Exception("Unexpected static call");
    internal static RuntimeObject New(IntPtr type) => FontFallbackState.Format();
}
internal static class NativeUi
{
    internal static void CopyFormat(RuntimeObject target, RuntimeObject source) { foreach (var pair in source.Values) target.Values[pair.Key] = pair.Value; }
}
internal static class GameUi
{
    internal static bool Visible(RuntimeObject obj) => obj.Get<bool>("visible") && !obj.Get<bool>("isDisposed");
}
internal static class Plugin
{
    internal static readonly FixtureLogger Logger = new();
    internal sealed class FixtureLogger { internal void LogWarning(string message) { } }
}
internal static class FontFallbackState
{
    internal static readonly Dictionary<string, RuntimeObject> Fonts = new();
    internal static readonly HashSet<char> Characters = new();
    internal static RuntimeObject Asset = null!, OsFont = null!;
    internal static Func<char, bool> NativeHas = _ => true, OsHas = _ => true;
    internal static bool OsAvailable;
    internal static int ForcedLayouts, AtlasMutations, OsEnumerations;
    internal static RuntimeObject Format()
    {
        var result = new RuntimeObject("TextFormat"); result.Values["font"] = "Native";
        result.Values["outline"] = .6f; result.Values["faceDilate"] = .2f;
        result.Values["outlineSoftness"] = .3f; result.Values["underlaySoftness"] = .4f; return result;
    }
    internal static void Reset(Func<char, bool> native, bool available = true, Func<char, bool>? os = null)
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        ((IDictionary)typeof(ModFont).GetField("Labels", flags)!.GetValue(null)!).Clear();
        typeof(ModFont).GetField("_nextCheck", flags)!.SetValue(null, 0f);
        ((IList)typeof(ModFont).GetField("Order", flags)!.GetValue(null)!).Clear();
        typeof(ModFont).GetField("_cursor", flags)!.SetValue(null, 0);
        typeof(ModFont).GetField("_hasGothic", flags)!.SetValue(null, null);
        typeof(ModFont).GetField("_warned", flags)!.SetValue(null, false);
        Fonts.Clear(); Characters.Clear(); NativeHas = native; OsHas = os ?? (_ => true); OsAvailable = available;
        ForcedLayouts = AtlasMutations = OsEnumerations = 0; UnityEngine.Time.unscaledTime = 1f;
        Asset = new RuntimeObject("TMP_FontAsset"); OsFont = new RuntimeObject("Font");
        Fonts["Native"] = new RuntimeObject("TMPFont"); Fonts["Native"].Values["fontAsset"] = Asset;
        Fonts["Malgun Gothic"] = new RuntimeObject("DynamicFont"); Fonts["Malgun Gothic"].Values["nativeFont"] = OsFont;
    }
    internal static RuntimeObject Label(string text)
    {
        var label = new RuntimeObject("Label"); var body = new RuntimeObject("TextField");
        body.Values["parsedText"] = new RuntimeObject("String", text);
        label.Values["_textField"] = body; label.Values["textFormat"] = Format(); return label;
    }
    internal static void Parsed(RuntimeObject label, string text) => label.Field("_textField")!.Values["parsedText"] = new RuntimeObject("String", text);
    internal static string Selected(RuntimeObject label) => label.Get("textFormat")!.Field("font")!.String();
    internal static void Invariants()
    {
        if (ForcedLayouts != 0) throw new Exception("Forced layout access");
        if (AtlasMutations != 0) throw new Exception("Unexpected atlas mutation");
    }
}
internal static class FontFallbackCheck
{
    internal static int Assertions;
    private static void Check(bool value, string message) { FontFallbackState.Invariants(); Assertions++; if (!value) throw new Exception("Font fallback: " + message); }
    internal static void Run()
    {
        const string title = "필드·카메라";
        FontFallbackState.Reset(_ => true); var label = FontFallbackState.Label(""); ModFont.Track(label);
        Check(FontFallbackState.Characters.Count == 0, "unparsed text waits for normal native layout");
        FontFallbackState.Parsed(label, title); ModFont.Tick();
        Check(FontFallbackState.Characters.Contains('·') && FontFallbackState.Characters.Count == 6, "normal parsed text checks the complete field/camera heading");
        Check(FontFallbackState.Selected(label) == "Native" && FontFallbackState.OsEnumerations == 0, "native coverage preserves native font without OS enumeration");

        FontFallbackState.Reset(c => c != '·'); label = FontFallbackState.Label(title); ModFont.Track(label);
        Check(FontFallbackState.Selected(label) == "Malgun Gothic", "verified missing glyph switches the whole label");
        var format = label.Get("textFormat")!;
        Check(format.Field("outline")!.Value<float>() == 1f && format.Field("faceDilate")!.Value<float>() == 0f, "OS fallback uses pixel-stroke formatting");
        FontFallbackState.Parsed(label, "전투 추천"); ModFont.Track(label);
        Check(FontFallbackState.Selected(label) == "Native" && format.Field("outline")!.Value<float>() == .6f && format.Field("faceDilate")!.Value<float>() == .2f && format.Field("outlineSoftness")!.Value<float>() == .3f && format.Field("underlaySoftness")!.Value<float>() == .4f, "return to native restores owned format changes");

        FontFallbackState.Reset(c => c != '·', available: false); label = FontFallbackState.Label(title); ModFont.Track(label);
        Check(FontFallbackState.Selected(label) == "Native", "unavailable OS font leaves native rendering unchanged");
        FontFallbackState.Reset(c => c != '·', os: c => c != '·'); label = FontFallbackState.Label(title); ModFont.Track(label);
        Check(FontFallbackState.Selected(label) == "Native", "OS font without the full phrase never replaces native rendering");

        FontFallbackState.Reset(_ => true); label = FontFallbackState.Label(title); FontFallbackState.Fonts["Native"].Values["fontAsset"] = null; ModFont.Track(label);
        Check(FontFallbackState.Characters.Count == 0 && FontFallbackState.OsEnumerations == 0, "asynchronous TMP asset absence waits without fallback");
        FontFallbackState.Fonts["Native"].Values["fontAsset"] = FontFallbackState.Asset; ModFont.Tick();
        Check(FontFallbackState.Characters.Contains('·') && FontFallbackState.Selected(label) == "Native", "ready TMP asset resumes normal coverage checking");
        label.Values["isDisposed"] = true; UnityEngine.Time.unscaledTime += 1; ModFont.Tick();
        Check(((IDictionary)typeof(ModFont).GetField("Labels", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Count == 0, "disposed labels release tracked state");

        // A pass cut by the frame budget resumes where it stopped instead of restarting or waiting for the next interval.
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        FontFallbackState.Reset(_ => true); var first = FontFallbackState.Label(title); var second = FontFallbackState.Label(title);
        ModFont.Track(first); ModFont.Track(second);
        var order = (List<IntPtr>)typeof(ModFont).GetField("Order", flags)!.GetValue(null)!;
        order.Clear(); order.Add(first.Pointer); order.Add(second.Pointer);
        typeof(ModFont).GetField("_cursor", flags)!.SetValue(null, 1);
        typeof(ModFont).GetField("_nextCheck", flags)!.SetValue(null, float.MaxValue);
        first.Values["isDisposed"] = true; second.Values["isDisposed"] = true; ModFont.Tick();
        var labels = (IDictionary)typeof(ModFont).GetField("Labels", flags)!.GetValue(null)!;
        Check(labels.Count == 1 && labels.Contains(first.Pointer), "an interrupted pass resumes at its cursor");
        typeof(ModFont).GetField("_nextCheck", flags)!.SetValue(null, 0f); ModFont.Tick();
        Check(labels.Count == 0, "the next pass starts over once the interval elapses");
    }
}
