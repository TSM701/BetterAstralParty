using TMPro;
using UnityEngine;

namespace BetterAstralParty;

// Only mod-owned labels. No asset files, TMP atlases or global alias replacement.
internal static class ModFont
{
    private sealed class Entry
    {
        internal readonly RuntimeObject Label;
        internal string Native, Applied, Text = "";
        internal IntPtr Asset;
        internal RuntimeObject? NativeFormat;
        internal Entry(RuntimeObject label, string font) { Label = label; Native = Applied = font; }
    }
    private static readonly Dictionary<IntPtr, Entry> Labels = new();
    private static readonly List<IntPtr> Order = new();
    private static int _cursor;
    private static float _nextCheck;
    private static bool? _hasGothic;
    private static bool _warned;
    internal static int Revision { get; private set; }

    internal static void Track(RuntimeObject label, bool reset = false)
    {
        if (reset || !Labels.ContainsKey(label.Pointer))
            Labels[label.Pointer] = new Entry(label, label.Get("textFormat")!.Field("font")?.String() ?? "");
        Check(Labels[label.Pointer]);
    }

    internal static void Tick()
    {
        if (_cursor >= Order.Count)
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.1f;
            Order.Clear(); Order.AddRange(Labels.Keys); _cursor = 0;
        }
        // A pass over every tracked label in one frame stalled it for 20-36 ms (2026-10-24 performance log);
        // resume the pass across frames under a 1 ms budget.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_cursor < Order.Count)
        {
            var key = Order[_cursor++];
            if (!Labels.TryGetValue(key, out var entry)) continue;
            try
            {
                if (entry.Label.Get<bool>("isDisposed")) { Labels.Remove(key); continue; }
                if (GameUi.Visible(entry.Label)) Check(entry);
            }
            catch { Labels.Remove(key); } // Scene disposal must not disable the UI driver.
            if ((System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency >= 0.001) break;
        }
    }

    private static void Check(Entry entry)
    {
        try
        {
            var label = entry.Label;
            var format = label.Get("textFormat")!;
            var current = format.Field("font")?.String() ?? "";
            if (current != entry.Applied) { entry.Native = current; entry.Asset = IntPtr.Zero; }
            var family = entry.Native;
            if (string.IsNullOrEmpty(family))
                family = RuntimeObject.StaticField(RuntimeObject.FindClass("FairyGUI", "UIConfig"), "defaultFont")!.String();
            var font = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "FontManager"), "GetFont", family)!;
            // Wait for the game's asynchronous locale font instead of caching a false miss.
            var isTmp = font.TypeName is "TMPFont" or "MyTMPFont";
            var tmp = isTmp ? font.Get("fontAsset") : null;
            if (isTmp && tmp == null) return;
            var asset = tmp?.Pointer ?? font.Pointer;
            var textField = label.Field("_textField")!;
            // Read native layout results without laying out a new, not-yet-sized label.
            var text = textField.Get("parsedText")?.String() ?? "";
            if (entry.Text == text && entry.Asset == asset) return;
            bool Has(char c) => tmp != null
                ? new TMP_FontAsset(tmp.Pointer).HasCharacter(c, false, false)
                : font.TypeName == "DynamicFont"
                    ? new Font(font.Get("nativeFont")!.Pointer).HasCharacter(c)
                    : true; // Preserve other native renderers; do not guess glyph coverage.
            var selected = entry.Native;
            if (FontCoverage.NeedsFallback(text, Has))
            {
                _hasGothic ??= Font.GetOSInstalledFontNames().Any(n => n == "Malgun Gothic" || n == "맑은 고딕");
                if (_hasGothic == true)
                {
                    var fallback = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "FontManager"), "GetFont", "Malgun Gothic")!;
                    var source = new Font(fallback.Get("nativeFont")!.Pointer);
                    if (!FontCoverage.NeedsFallback(text, source.HasCharacter)) selected = "Malgun Gothic";
                }
            }
            if (current != selected)
            {
                if (selected == "Malgun Gothic")
                {
                    entry.NativeFormat = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "TextFormat"));
                    NativeUi.CopyFormat(entry.NativeFormat, format);
                    // DynamicFont uses pixel strokes, not TMP's normalized SDF material values.
                    format.SetField("outline", format.Field("outline")!.Value<float>() > 0 ? 1f : 0f);
                    format.SetField("faceDilate", 0f);
                    format.SetField("outlineSoftness", 0f);
                    format.SetField("underlaySoftness", 0f);
                }
                else if (entry.NativeFormat != null)
                {
                    foreach (var field in new[] { "outline", "faceDilate", "outlineSoftness", "underlaySoftness" })
                        format.SetField(field, entry.NativeFormat.Field(field)!.Value<float>());
                    entry.NativeFormat = null;
                }
                format.SetField("font", selected);
                label.Set("textFormat", format);
                Revision++;
            }
            entry.Applied = selected; entry.Text = text; entry.Asset = asset;
        }
        catch (Exception ex)
        {
            // Font compatibility must never disable a menu or gameplay information.
            if (!_warned) { _warned = true; Plugin.Logger.LogWarning("[Font fallback] Native font retained: " + ex.Message); }
        }
    }
}
