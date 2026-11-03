using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BetterAstralParty;

// Development builds only (X.Y.Z-dev.N, user-decided 2026-11-02): Unity's warnings, errors and exceptions are copied into the
// runtime log while diagnostics record. BepInEx's UnityLogListening registers a managed delegate through Il2CppInterop class
// injection, which this game's Unity build cannot do ("Class::Init signatures have been exhausted"), and the game closed at
// startup; Harmony detours on the default log handler need no injection. Bounded per session; never in a public release.
internal static class UnityLogTap
{
    private const int MaxLines = 300, MaxText = 1200;
    private static readonly object Gate = new();
    private static int _lines;
    private static string? _last;

    internal static void Register()
    {
        if (!Plugin.Version.Contains("-dev.", StringComparison.Ordinal)) return;
        var harmony = new Harmony(Plugin.Guid + ".UnityLog");
        try
        {
            harmony.CreateClassProcessor(typeof(UnityLogFormatPatch)).Patch();
            harmony.CreateClassProcessor(typeof(UnityLogExceptionPatch)).Patch();
        }
        catch (Exception ex) { Compatibility.Block("UnityLog", ex, () => harmony.UnpatchSelf()); }
    }

    // Any thread may log; repeats of the previous line are skipped.
    internal static void Record(string level, string? text)
    {
        if (text == null || !Plugin.Diagnostics.IsRecording) return;
        if (text.Length > MaxText) text = text.Substring(0, MaxText) + "...";
        text = text.Replace("\r", "").Replace('\n', '|');
        lock (Gate)
        {
            if (_lines >= MaxLines || text == _last) return;
            _last = text;
            _lines++;
        }
        Plugin.Diagnostics.Write("unity." + level + " " + text);
        if (_lines == MaxLines) Plugin.Diagnostics.Write("unity.log limit reached; later Unity messages are not copied this session");
    }
}

[HarmonyPatch(typeof(DebugLogHandler), nameof(DebugLogHandler.LogFormat), typeof(LogType), typeof(UnityEngine.Object), typeof(string), typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]
internal static class UnityLogFormatPatch
{
    // Plain Log lines are left out; a failure here must never reach the game's own logging.
    [HarmonyPrefix]
    private static void Prefix(LogType __0, string __2, Il2CppReferenceArray<Il2CppSystem.Object> __3)
    {
        try
        {
            if (__0 == LogType.Log) return;
            var text = __2;
            if (__3 != null && __3.Length > 0)
            {
                var values = new object?[__3.Length];
                for (var i = 0; i < values.Length; i++) values[i] = __3[i]?.ToString();
                try { text = string.Format(__2, values); } catch (FormatException) { }
            }
            UnityLogTap.Record(__0.ToString(), text);
        }
        catch { }
    }
}

[HarmonyPatch(typeof(DebugLogHandler), nameof(DebugLogHandler.LogException))]
internal static class UnityLogExceptionPatch
{
    [HarmonyPrefix]
    private static void Prefix(Il2CppSystem.Exception __0)
    {
        try { UnityLogTap.Record("Exception", __0?.ToString()); }
        catch { }
    }
}
