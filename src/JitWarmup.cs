using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BetterAstralParty;

// Home-screen JIT warm-up (user-approved 2026-11-18). .NET compiles each method on its first call: in 2026.11.2-dev.37 the
// home-entry hitch was 66.6 of 70.8 ms JIT and the first field tick 21.4 of 30.4 ms. Once the home screen shows, this
// compiles the mod's own methods ahead of their first call, at most 1.5 ms a frame (one method at least), so matches start
// without those hitches. It is quiet: no bar or text, since nothing waits on it and a method it has not reached yet simply
// compiles on its first call as before. It pauses away from home, in PvP and on a frame over 100 ms, and stops once done.
// Left out: types with an explicit static constructor (preparing one of their methods would run it early), generic,
// abstract and external methods, and the update and diagnostics code outside this namespace, which runs at start-up or
// rarely. A method that cannot be prepared is counted and skipped.
internal static class JitWarmup
{
    private const string Feature = "JitWarmup";
    private const double SliceMs = 1.5;
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type[]? _types;
    private static MethodBase[] _methods = Array.Empty<MethodBase>();
    private static int _type, _method, _prepared, _failed, _frames;
    private static bool _done;
    private static long _started;
    private static TimeSpan _jitAtStart;

    internal static void Tick()
    {
        if (_done || !GameUi.HomeAvailable || PvpSafety.Suspended || !Compatibility.Allowed(Feature)) return;
        if (Time.unscaledDeltaTime > .1f) return;
        try { Slice(); }
        catch (Exception ex) { _done = true; Compatibility.Block(Feature, ex); }
    }

    private static void Slice()
    {
        var now = Stopwatch.GetTimestamp();
        var end = now + (long)(SliceMs * Stopwatch.Frequency / 1000);
        if (_types == null)
        {
            _types = Types();
            _started = now; _jitAtStart = JitInfo.GetCompilationTime(true);
            Plugin.Diagnostics.Write("jitWarmup begin; types=" + _types.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        _frames++;
        do
        {
            if (_method >= _methods.Length)
            {
                if (_type >= _types.Length) { Finish(); return; }
                _methods = Methods(_types[_type++]); _method = 0;
                continue;
            }
            var method = _methods[_method++];
            try { RuntimeHelpers.PrepareMethod(method.MethodHandle); _prepared++; }
            catch (Exception ex)
            {
                if (++_failed <= 5) Plugin.Diagnostics.Write("jitWarmup skip " + method.DeclaringType?.Name + "." + method.Name + ": " + ex.GetType().Name);
            }
        } while (Stopwatch.GetTimestamp() < end);
    }

    private static Type[] Types()
    {
        Type?[] types;
        try { types = typeof(JitWarmup).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }
        return types.OfType<Type>().Where(type => type.Namespace == nameof(BetterAstralParty)).ToArray();
    }

    private static MethodBase[] Methods(Type type)
    {
        if (type.ContainsGenericParameters || type.IsInterface || type.IsSubclassOf(typeof(Delegate))
            || type.TypeInitializer != null && (type.Attributes & TypeAttributes.BeforeFieldInit) == 0) return Array.Empty<MethodBase>();
        static bool Compiled(MethodBase method) => !method.IsAbstract && !method.ContainsGenericParameters
            && (method.Attributes & MethodAttributes.PinvokeImpl) == 0
            && (method.GetMethodImplementationFlags() & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) == 0;
        return type.GetMethods(Declared).Where(Compiled).Cast<MethodBase>()
            .Concat(type.GetConstructors(Declared).Where(constructor => !constructor.IsStatic && Compiled(constructor))).ToArray();
    }

    private static void Finish()
    {
        _done = true; _types = null; _methods = Array.Empty<MethodBase>();
        Plugin.Diagnostics.Write(FormattableString.Invariant(
            $"jitWarmup done; prepared={_prepared}; failed={_failed}; frames={_frames}; main_thread_jit_ms={(JitInfo.GetCompilationTime(true) - _jitAtStart).TotalMilliseconds:F1}; elapsed_s={(Stopwatch.GetTimestamp() - _started) / (double)Stopwatch.Frequency:F1}"));
    }
}
