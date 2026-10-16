namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private const string GeneralCharacterNames0 = """""""
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace __NAMESPACE__ {
public class RuntimeObject {
    private static int next;
    public IntPtr Pointer = (IntPtr)(++next);
    public Dictionary<string, object> Props = new();
    public object? Scalar;
    public int Flushes;
    public RuntimeObject? Get(string key) => Props.TryGetValue(key, out var value)
        ? value as RuntimeObject ?? new RuntimeObject { Scalar = value } : null;
    public T Get<T>(string key) => Props.TryGetValue(key, out var value) ? (T)value : default!;
    public string? String() => (string?)Scalar;
    public T Value<T>() => (T)Scalar!;
    public void Set(string key, object value) => Props[key] = value;
    public RuntimeObject? Call(string method, params object[] args) {
        switch (method) {
            case "ContainsKey": return new RuntimeObject { Scalar = Props.ContainsKey((string)args[0]) };
            case "get_Item": return Get((string)args[0]);
            case "SetVar": Get("templateVars")!.Set((string)args[0], args[1]); return this;
            case "FlushVars": Flushes++; return null;
            default: throw new Exception(method);
        }
    }
}
static class Plugin {
    public static readonly Log Logger = new();
    public class Log { public void LogInfo(string value) {} }
}
__NAMES__
public static class Check {
    private static readonly Dictionary<IntPtr, (RuntimeObject Label, string Property, string Original, string Applied)> Changed = new();
    private static readonly Dictionary<string, string> _names = new() { ["시장의 지배자"] = "파루난" };
    private static bool _loggedApplied;
    private static readonly Stack<int> Pending = new();
__METHODS__
    static void Assert(bool value) { if (!value) throw new Exception("Character name regression"); }
    public static void Run() {
        foreach (var sentence in new[] { "선호 캐릭터: {data}", "Favorite character: {data}", "{data} / favorite" }) {
            var label = new RuntimeObject();
            var vars = new RuntimeObject();
            label.Set("text", sentence); label.Set("templateVars", vars);
            vars.Set("data", "시장의 지배자"); vars.Set("other", "untouched");
            Replace(label, "data"); Replace(label, "data");
            Assert(ReadLabel(label, "data") == "파루난" && label.Flushes == 1);
            Assert(label.Get("text")!.String() == sentence && vars.Get("other")!.String() == "untouched");
            RestoreAll(); Assert(ReadLabel(label, "data") == "시장의 지배자" && label.Flushes == 2);
            Replace(label, "data"); vars.Set("data", "다른 캐릭터");
            RestoreAll(); Assert(ReadLabel(label, "data") == "다른 캐릭터");
            vars.Set("data", "시장의 지배자"); Replace(label, "data");
            vars.Set("data", "시장의 지배자"); Replace(label, "data");
            Assert(ReadLabel(label, "data") == "파루난"); RestoreAll();
        }
        var plain = new RuntimeObject(); plain.Set("text", "시장의 지배자");
        Replace(plain, "text"); Assert(ReadLabel(plain, "text") == "파루난");
        RestoreAll(); Assert(ReadLabel(plain, "text") == "시장의 지배자");
        var missing = new RuntimeObject(); Replace(missing, "data");
        missing.Set("templateVars", new RuntimeObject()); Replace(missing, "data");
        Assert(missing.Flushes == 0 && Changed.Count == 0);
    }
}
}
""""""";

    private const string GeneralRuntimeArguments0 = """""""
#nullable enable
using System;
using System.Collections.Generic;
namespace __NAMESPACE__ {
namespace UnityEngine {
    public struct Color {} public struct Vector2 {} public struct Vector3 {}
    public struct Quaternion { public float x, y, z, w; }
}
public unsafe class RuntimeObject {
    private static readonly Dictionary<IntPtr, Type> Types = new();
    public IntPtr Class { get; }
    public IntPtr Pointer { get; }
    public RuntimeObject(Type type) { Class = Id(type); }
    public RuntimeObject(IntPtr pointer) { Pointer = pointer; }
    private static RuntimeObject? Wrap(IntPtr pointer) => pointer == IntPtr.Zero ? null : new(pointer);
    private static IntPtr MethodInfo(IntPtr klass, string name, int count) => klass;
    private static UnityEngine.Quaternion Captured;
    private static IntPtr Id(Type type) {
        foreach (var entry in Types) if (entry.Value == type) return entry.Key;
        var id = (IntPtr)(Types.Count + 1); Types.Add(id, type); return id;
    }
    private static IntPtr FindClass(string ns, string name) => Id(Type.GetType(ns + "." + name, true)!);
    private static void CheckValue<T>(IntPtr klass) where T : unmanaged {
        if (Types[klass] != typeof(T)) throw new InvalidCastException();
    }
    private static class IL2CPP {
        internal static bool il2cpp_type_is_byref(IntPtr type) => Types[type].IsByRef;
        internal static IntPtr il2cpp_class_from_type(IntPtr type) => type;
        internal static bool il2cpp_class_is_valuetype(IntPtr type) => Types[type].IsValueType;
        internal static bool il2cpp_class_is_assignable_from(IntPtr target, IntPtr value) => Types[target].IsAssignableFrom(Types[value]);
        internal static string il2cpp_type_get_name_(IntPtr type) => Types[type].FullName!;
        internal static IntPtr ManagedStringToIl2Cpp(string text) => Id(typeof(string));
        internal static IntPtr il2cpp_method_get_param(IntPtr method, uint index) => Id(typeof(UnityEngine.Quaternion));
        internal static IntPtr il2cpp_runtime_invoke(IntPtr method, IntPtr instance, void** args, ref IntPtr exception) {
            Captured = *(UnityEngine.Quaternion*)args[0]; return IntPtr.Zero;
        }
    }
__CHECK_ARGUMENT__
__INVOKE__
    private static bool Accepts(Type target, object? value) {
        try { CheckArgument(Id(target), value); return true; }
        catch (InvalidCastException) { return false; }
    }
    public static bool AcceptsObjectString() => Accepts(typeof(object), "중첩·진행 3 · 남은 턴 2");
    public static bool AcceptsQuaternion() => Accepts(typeof(UnityEngine.Quaternion), new UnityEngine.Quaternion());
    public static bool InvokesQuaternion() {
        var value = new UnityEngine.Quaternion { x = .125f, y = -.25f, z = .5f, w = 1 };
        try { Invoke((IntPtr)1, IntPtr.Zero, "QuaternionProbe", new object?[] { value }); }
        catch (ArgumentException) { return false; }
        catch (InvalidCastException) { return false; }
        return Captured.x == value.x && Captured.y == value.y && Captured.z == value.z && Captured.w == value.w;
    }
    public static void Run() {
        foreach (var target in new[] { typeof(string), typeof(object), typeof(IComparable), typeof(IEnumerable<char>) })
            foreach (var text in new[] { "", "중첩·진행 3 · 남은 턴 2" })
                if (!Accepts(target, text)) throw new Exception("Valid string reference rejected: " + target);
        foreach (var target in new[] { typeof(int), typeof(bool), typeof(float), typeof(Type), typeof(string[]), typeof(IEnumerable<int>), typeof(string).MakeByRefType() })
            if (Accepts(target, "text")) throw new Exception("Invalid string reference accepted: " + target);
        if (!Accepts(typeof(object), new RuntimeObject(typeof(string))) || !Accepts(typeof(int), 3)
            || Accepts(typeof(float), 3) || Accepts(typeof(string), new RuntimeObject(typeof(object)))
            || !Accepts(typeof(string), null) || Accepts(typeof(int), null))
            throw new Exception("Other argument guards changed");
        try { CheckArgument(IntPtr.Zero, "text"); throw new Exception("Missing metadata accepted"); }
        catch (InvalidCastException) { }
        if (!AcceptsQuaternion() || !InvokesQuaternion()) throw new Exception("Quaternion check/marshaling failed");
        foreach (var target in new[] { typeof(UnityEngine.Vector3), typeof(object), typeof(UnityEngine.Quaternion).MakeByRefType() })
            if (Accepts(target, new UnityEngine.Quaternion())) throw new Exception("Invalid Quaternion target accepted");
    }
}
}
""""""";

    private const string GeneralRuntimeGetters0 = """""""
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public unsafe class GetterProbe {
    static readonly Dictionary<string,string> Getters = new();
    static readonly IntPtr Storage = Marshal.AllocHGlobal(32);
    static Type ReturnType;
    static bool Fail, Missing;
    static int Calls;
    IntPtr Class => (IntPtr)1;
    IntPtr Pointer => (IntPtr)2;
    static IntPtr MethodInfo(IntPtr c, string name, int count) {
        if (name != "get_Value" || count != 0) throw new Exception("Wrong accessor");
        return (IntPtr)3;
    }
    static IntPtr FieldInfo(IntPtr c, string name) {
        if (c != (IntPtr)1 || name != "Value") throw new MissingFieldException(name);
        return (IntPtr)4;
    }
    static void CheckValue<T>(IntPtr c) where T : unmanaged {
        if (ReturnType != typeof(T)) throw new InvalidCastException();
    }
    static class IL2CPP {
        internal static IntPtr il2cpp_class_from_type(IntPtr p) => p;
        internal static IntPtr il2cpp_method_get_return_type(IntPtr p) => p;
        internal static IntPtr il2cpp_field_get_type(IntPtr p) => p;
        internal static void il2cpp_field_get_value(IntPtr instance, IntPtr field, void* value) {
            if (instance != (IntPtr)2 || field != (IntPtr)4) throw new Exception("Wrong field instance/metadata");
            Calls++;
            // IL2CPP Boolean is one byte, unlike Marshal.SizeOf<bool>().
            var size = ReturnType == typeof(bool) ? 1 : Marshal.SizeOf(ReturnType);
            Buffer.MemoryCopy((void*)Storage, value, size, size);
        }
        internal static bool il2cpp_class_is_valuetype(IntPtr p) => false;
        internal static IntPtr il2cpp_object_unbox(IntPtr p) => p;
        internal static IntPtr il2cpp_runtime_invoke(IntPtr m, IntPtr i, void** args, ref IntPtr ex) {
            Calls++;
            if (args != null) throw new Exception("Getter arguments not empty");
            if (Fail) ex = (IntPtr)1;
            return Missing ? IntPtr.Zero : Storage;
        }
    }
__ACCESSOR__
__GETTER__
__FIELD_GETTER__
    struct Point { public float X, Y; }
    static void RoundTrip<T>(T value) where T : unmanaged {
        ReturnType = typeof(T);
        *(T*)Storage = value;
        if (!new GetterProbe().Get<T>("Value").Equals(value)) throw new Exception("Wrong value: " + typeof(T));
        if (!new GetterProbe().Field<T>("Value").Equals(value)) throw new Exception("Wrong field value: " + typeof(T));
    }
    public static void Run() {
        try {
            RoundTrip(123); RoundTrip(-7.5f); RoundTrip(true); RoundTrip(false);
            RoundTrip(long.MaxValue); RoundTrip(new Point { X = 3, Y = -5 });
            ReturnType = typeof(string);
            int before = Calls;
            try { new GetterProbe().Get<int>("Value"); throw new Exception("Mismatch accepted"); }
            catch (InvalidCastException) { }
            try { new GetterProbe().Field<int>("Value"); throw new Exception("Field mismatch accepted"); }
            catch (InvalidCastException) { }
            if (Calls != before) throw new Exception("Type validation must precede invocation");
            ReturnType = typeof(int);
            Fail = true;
            try { new GetterProbe().Get<int>("Value"); throw new Exception("Native exception ignored"); }
            catch (InvalidOperationException) { }
            Fail = false; Missing = true;
            try { new GetterProbe().Get<int>("Value"); throw new Exception("Null return ignored"); }
            catch (InvalidOperationException) { }
            Missing = false;
            var probe = new GetterProbe();
            for (int i = 0; i < 1000; i++) probe.Get<int>("Value");
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) probe.Get<int>("Value");
            long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            if (bytes != 0) throw new Exception("Hot getter managed allocations: " + bytes);
            if (Getters.Count != 1) throw new Exception("Accessor cache grew per read");
            Console.WriteLine("Typed getter: values, pre-invoke type guard, native errors, nulls passed; 10000 warm reads = 0 managed bytes (native substitute).");
            try { probe.Field<int>("Missing"); throw new Exception("Missing field accepted"); }
            catch (MissingFieldException) { }
            for (int i = 0; i < 1000; i++) probe.Field<int>("Value");
            start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) {
                *(int*)Storage = i; // Read the current native value, not a cached game-state copy.
                if (probe.Field<int>("Value") != i) throw new Exception("Stale field value");
            }
            bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            if (bytes != 0) throw new Exception("Hot field getter managed allocations: " + bytes);
            Console.WriteLine("Typed field getter: primitives/structs, instance, missing/type guards, live changes passed; 10000 warm reads = 0 managed bytes (native substitute).");
        } finally { Marshal.FreeHGlobal(Storage); }
    }
}
""""""";

    private const string GeneralPvpSafety0 = """""""
using System;
using System.Collections.Generic;
namespace __NS__ {
__SOURCE__
static class VisibleCombatAdvisor { public static bool IsPve(int m)=>m is 4 or 6 or 9 or 10 or 12; }
static class Plugin { public static Log Diagnostics=new(); }
sealed class Log { public void Error(string scope,Exception ex) {} }
sealed class RuntimeObject {
    public static RuntimeObject Manager;
    public Dictionary<string,object> Data=new();
    public static IntPtr FindClass(string ns,string name)=>(IntPtr)1;
    public static RuntimeObject StaticField(IntPtr type,string name)=>Manager;
    public RuntimeObject Get(string key)=>Data[key] as RuntimeObject ?? Box(Data[key]);
    public RuntimeObject Field(string key)=>Get(key);
    public T Get<T>(string key)=>(T)Data[key];
    public T Value<T>()=>(T)Data["value"];
    public static RuntimeObject Box(object value)=>value==null ? null : new() { Data=new() { ["value"]=value } };
}
public static class Check {
    static RuntimeObject Node(string key,object value)=>new() { Data=new() { [key]=value } };
    public static void Run() {
        void Expect(bool blocked,string name,bool? suspended=null) {
            if(PvpSafety.Poll()!=blocked || PvpSafety.Suspended!=(suspended ?? blocked)) throw new Exception(name);
        }
        Expect(true,"cold startup pauses gameplay, not one-shot startup features",false);
        var match=new RuntimeObject { Data=new() { ["TeamId"]=0L,["MatchMode"]=1 } };
        var room=new RuntimeObject { Data=new() { ["roomStateType"]=0,["localRoom"]=Node("info",Node("MapType",1)) } };
        RuntimeObject.Manager=new() { Data=new() { ["match"]=Node("matchData",match),["room"]=Node("roomController",room) } };
        var manager=RuntimeObject.Manager;
        manager.Data["room"]=null;
        Expect(true,"partially initialized startup",false);
        match.Data["TeamId"]=1L;
        Expect(true,"known PvP queue blocks even before room reader exists");
        RuntimeObject.Manager=null; Expect(true,"PvP suspension survives missing manager");
        RuntimeObject.Manager=manager; match.Data["TeamId"]=0L;
        manager.Data["room"]=Node("roomController",room);
        Expect(false,"home with stale PvP mode/room");
        foreach(var mode in new[] { 0,1,2,3,5,7,8,11,99 }) {
            match.Data["TeamId"]=1L; match.Data["MatchMode"]=mode;
            Expect(true,"PvP/unknown waiting team");
            room.Data["roomStateType"]=1; room.Data["localRoom"]=Node("info",Node("MapType",mode));
            Expect(true,"match success overlapping team/room");
            match.Data["TeamId"]=0L;
            foreach(var state in new[] { 1,2,3,4,5,6 }) { room.Data["roomStateType"]=state; Expect(true,"room/reconnect/spectator"); }
            room.Data["roomStateType"]=0; Expect(false,"leave/cancel with stale native objects");
        }
        foreach(var mode in new[] { 4,6,9,10,12 }) {
            match.Data["TeamId"]=1L; match.Data["MatchMode"]=mode;
            room.Data["roomStateType"]=0; Expect(false,"PvE queue");
            room.Data["roomStateType"]=4; room.Data["localRoom"]=Node("info",Node("MapType",mode));
            Expect(false,"PvE play");
            match.Data["MatchMode"]=1; Expect(true,"PvP queue wins over stale PvE room");
            match.Data["TeamId"]=0L; Expect(false,"inactive stale match does not block PvE");
        }
        room.Data["localRoom"]=null; Expect(true,"active room not yet populated");
        room.Data.Remove("roomStateType"); Expect(true,"binding failure");
        RuntimeObject.Manager=null; Expect(true,"manager unavailable");
        RuntimeObject.Manager=manager;
        room.Data["roomStateType"]=0;
        Expect(false,"recovery after binding failure");
        manager.Data["match"]=null;
        room.Data["roomStateType"]=4; room.Data["localRoom"]=Node("info",Node("MapType",1));
        Expect(true,"known PvP room blocks without match reader");
        Console.WriteLine("PASS: PvP queue/room/transitions, unknown fail-closed, stale-state cancellation and PvE recovery; production poll with native substitute.");
    }
}
}
""""""";

    private const string GeneralFightReadiness0 = """""""
using System;
public static class FightReadinessProbe {
    sealed class RuntimeObject {
        internal RuntimeObject Content, Controller;
        internal bool Disposed;
        internal string TypeName = "UIFightWindow";
        internal RuntimeObject Get(string name) { return Content; }
        internal RuntimeObject Field(string name) { return Controller; }
        internal T Get<T>(string name) { return (T)(name == "isDisposed" ? (object)Disposed : 5); }
    }
    static class CardUi { internal static void Clear() {} }
    static class RemainingHpUi {
        internal static void Clear() {}
        internal static void ResetCapture() {}
    }
    static class Plugin {
        internal static class Diagnostics {
            internal static string Last;
            internal static void State(string key, string value) { Last = value; }
        }
    }
    static int Reached;
    static void RunFrame(RuntimeObject fight) {
__BLOCK__
        Reached++;
    }
    public static void Run() {
        var fight = new RuntimeObject();
        RunFrame(fight);
        if (Reached != 0 || Plugin.Diagnostics.Last != "waiting-content") throw new Exception("Null content");
        fight.Content = new RuntimeObject { Disposed = true };
        RunFrame(fight);
        if (Reached != 0) throw new Exception("Disposed content");
        fight.Content.Disposed = false;
        RunFrame(fight);
        if (Reached != 0 || Plugin.Diagnostics.Last != "waiting-step") throw new Exception("Null controller");
        fight.Content.Controller = new RuntimeObject();
        RunFrame(fight);
        if (Reached != 1 || Plugin.Diagnostics.Last != "ready") throw new Exception("Did not recover");
        fight.Content = null;
        RunFrame(fight);
        fight.Content = new RuntimeObject { Controller = new RuntimeObject() };
        RunFrame(fight);
        if (Reached != 2) throw new Exception("Scene re-entry did not recover");
        Console.WriteLine("PASS: delayed/disposed content, delayed controller, ready and scene re-entry (native substitute).");
    }
}
""""""";

    private const string GeneralProbability0 = """""""
#nullable enable
#pragma warning disable CS8618
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
namespace __NS__ {
""""""";

    private const string GeneralEncounter0 = """""""
#nullable enable
#pragma warning disable CS8618, CS9113
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
namespace __NS__ {
internal static class Time { internal static float unscaledTime; }
""""""";

    private const string GeneralEncounter1 = """""""
        double enemyMin = 1, enemyMax = 0, selfMin = 1, selfMax = 0;
        bool? consensus = null;
        var first = true;
        var disagrees = false;
        var outgoingOptions = new[] {
            CardAdvisor.BeforeRoll(input.EnemyHp, input.Attack, input.EnemyDefense, false, input.EnemyModifiers),
            CardAdvisor.BeforeRoll(input.EnemyHp, input.Attack, input.EnemyDefense, true, input.EnemyModifiers) };
        var incomingOptions = input.Counter ? new[] {
            CardAdvisor.BeforeRoll(input.Hp, input.EnemyAttack, input.Defense, false, input.Modifiers),
            CardAdvisor.BeforeRoll(input.Hp, input.EnemyAttack, input.Defense, true, input.Modifiers) } : new ChoiceStats[2];
        for (var enemyDodge = 0; enemyDodge < 2; enemyDodge++)
        for (var selfDodge = 0; selfDodge < 2; selfDodge++) {
            var outgoing = outgoingOptions[enemyDodge]; var incoming = incomingOptions[selfDodge];
            var survives = 1 - outgoing.KnockoutChance;
            var selfKo = incoming.KnockoutChance * survives; var selfDamage = incoming.ExpectedDamage * survives;
            enemyMin = Math.Min(enemyMin, outgoing.KnockoutChance); enemyMax = Math.Max(enemyMax, outgoing.KnockoutChance);
            selfMin = Math.Min(selfMin, selfKo); selfMax = Math.Max(selfMax, selfKo);
            const double epsilon = 0.000001;
            var koGain = outgoing.KnockoutChance - selfKo; var damageGain = outgoing.ExpectedDamage - selfDamage;
            bool? attack = Math.Abs(koGain) > epsilon ? koGain > 0 : Math.Abs(damageGain) > epsilon ? damageGain > 0 : null;
            if (first) { consensus = attack; first = false; } else if (consensus != attack) disagrees = true;
        }
        return new(disagrees ? null : consensus, enemyMin, enemyMax, selfMin, selfMax);
""""""";

    private const string GeneralKoMinimum0 = """""""
#nullable enable
#pragma warning disable CS8618 // Existing production result records are initialized by their resolver.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Reflection;
namespace __NS__ {
// Only diagnostics/localization boundaries are substituted; all resolver/display math is production code.
internal static class Plugin { internal static readonly CombatDiagnostics Diagnostics = new(); }
internal sealed class CombatDiagnostics {
    internal bool IsRecording => false;
    internal void State(string key, string value) {
        if (!key.StartsWith("combatEffects.", StringComparison.Ordinal) && !key.StartsWith("combatDice.", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected diagnostic boundary");
    }
}
internal static class ModText { internal static string Text(string text) => text; }
""""""";

    private const string GeneralKoMinimum1 = """""""
public static class FocusedCombat {
    public static void Run() { CombatEffectsCheck.Run(); Schema(); }
    public static void Thresholds() {
        typeof(CombatEffectsCheck).GetMethod("CheckThresholdRanges", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    }
    public static void Schema() {
__SCHEMA__
        var count = 0;
        void Check(bool value, string reason) {
            count++;
            if (!value) throw new Exception("Native card schema: " + reason);
        }
        foreach (var min in new[] { 0, 5, 7, 100 })
        foreach (var max in new[] { 0, 5, 9, 100 }) {
            var card = CardAdvisor.FromConfig(10009, 1, 1, new[] { min, max, 10010 }, 0, 0, 2);
            Check(card == (max >= min ? new CardBonus(true, min, max, 2) : (CardBonus?)null), "live additive bounds, no later-card current-hit effect");
            if (card is { } known) {
                Check(!known.PreventCounter, "later-card grant is not counter prevention");
                var input = new PreRollCombat(9, 10, 4, 4, 2, 2);
                Check(CardKoMinimum.Calculate(input, known, possible: true)
                    == CardKoMinimum.Calculate(input, new CardBonus(true, min, max, 2), possible: true), "later grant cannot alter current KO math");
            }
        }
        foreach (var parameters in new[] { new[] { -1, 5, 10010 }, new[] { 9, 5, 10010 },
            new[] { 5, 101, 10010 }, new[] { 5, 5, 10011 }, new[] { 5, 5, 1 }, new[] { 5, 5, 10010, 1 } })
            Check(CardAdvisor.FromConfig(10009, 1, 1, parameters, 0, 0, 2) == null, "invalid grant schema rejected");
        foreach (var cost in new[] { -1, 21 })
            Check(CardAdvisor.FromConfig(10009, 1, 1, new[] { 5, 5, 10010 }, 0, 0, cost) == null, "invalid cost rejected");
        foreach (var extra in new[] { (Buff:1, Card:0), (Buff:0, Card:1) })
            Check(CardAdvisor.FromConfig(10009, 1, 1, new[] { 5, 5, 10010 }, extra.Buff, extra.Card, 2) == null, "additional outcome-changing fields rejected");
        foreach (var id in new[] { 10008, 10010, 99999 })
            Check(CardAdvisor.FromConfig(id, 1, 1, new[] { 5, 5, 10010 }, 0, 0, 2) == null, "later-card semantic mapping not generalized to unknown IDs");
        foreach (var multiplier in new[] { 0, 1, 50, 10010 })
            Check(CardAdvisor.FromConfig(10010, 1, 1, new[] { 6, 6, multiplier }, 0, 0, 2) == null, "unverified final-ATK multiplier never downgraded to plain additive");
        Console.WriteLine($"Native card schema: existing Program assertions + {count} additive/bounds/ID/future-hand/multiplier checks passed.");
    }
}
}
""""""";

    private const string GeneralRelicBackground0 = """""""
#nullable enable
using System;
namespace RelicBackgroundTest {
struct Color {
    public float r,g,b,a;
    public Color(float r,float g,float b,float a=1) { this.r=r; this.g=g; this.b=b; this.a=a; }
    public static Color black => new(0,0,0);
}
static class Plugin { internal static Setting Opacity = new(); internal class Setting { public float Value = 1; } }
class RuntimeObject {
    public Color Fill, Border;
    public int Writes;
    public static IntPtr FindClass(string a,string b) => IntPtr.Zero;
    public static RuntimeObject New(IntPtr p) => new();
    public RuntimeObject Get(string name) => this;
    public void Set(string name,object value) {
        if(name == "color") { Fill=(Color)value; Writes++; }
        else if(name != "touchable") throw new Exception("Changed non-fill property: " + name);
    }
    public void Call(string name,params object[] args) {
        if(name == "DrawRoundRect") { Border=(Color)args[1]; Fill=(Color)args[2]; }
    }
}
static class NativeUi {
""""""";

    private const string GeneralRelicBackground1 = """""""
}
public static class RelicBackgroundCheck {
    static void Equal(float a,float b) { if(Math.Abs(a-b)>0.0001) throw new Exception(a+" != "+b); }
    public static void Run() {
        var root = new RuntimeObject();
        foreach(var initial in new[]{0f,0.5f,1f}) {
            Plugin.Opacity.Value=initial;
            var relic=new NativeUi.Surface(root,400,600,fill:new Color(0,0,0,0.88f));
            var normal=new NativeUi.Surface(root,400,600);
            Equal(relic.Graph.Fill.a,initial*0.88f);
            foreach(var opacity in new[]{0f,0.5f,1f}) {
                Plugin.Opacity.Value=opacity; relic.Refresh(); normal.Refresh();
                Equal(relic.Graph.Fill.a,opacity*0.88f); Equal(relic.Graph.Fill.r,0);
                Equal(normal.Graph.Fill.a,opacity); Equal(normal.Graph.Fill.r,1);
                Equal(normal.Graph.Fill.g,0.985f); Equal(normal.Graph.Fill.b,0.95f);
                Equal(relic.Graph.Border.a,1);
                var writes=relic.Graph.Writes; relic.Refresh(); Equal(relic.Graph.Writes,writes);
                relic.Resize(400,800); Equal(relic.Graph.Fill.a,opacity*0.88f);
            }
        }
    }
}
}
""""""";

    private const string GeneralTypography0 = """""""
public static class FontCoverageCheck {
    public static void Run() {
        System.Func<char,bool> latin = c => c < 128;
        if (FontCoverage.NeedsFallback("Mod Settings", latin)) throw new System.Exception("Native Latin must stay");
        if (!FontCoverage.NeedsFallback("모드 Settings", latin)) throw new System.Exception("Mixed phrase must fall back");
        if (FontCoverage.NeedsFallback("모드 Settings", c => true)) throw new System.Exception("Patched native coverage must stay");
        if (FontCoverage.NeedsFallback(" \n\t\uFFFC", c => false)) throw new System.Exception("Non-glyph placeholders");
        if (!FontCoverage.NeedsFallback("모드", c => false)) throw new System.Exception("Unavailable fallback must not claim coverage");
        if (FontCoverage.NeedsFallback("Back", latin)) throw new System.Exception("Return to native");
    }
}
""""""";

    private const string GeneralCharacterGuide0 = """""""
public static class CharacterRoleCheck {
    public static void Run() {
        if (CharacterRole.Label(2, true) != "딜러" || CharacterRole.Label(4, true) != "서포터") throw new System.Exception("Role-only labels");
        if (CharacterRole.Label(3, true) != "카드 특화" || CharacterRole.Label(3, false) != "Card") throw new System.Exception("Card role translation");
        for (int tag = -1; tag <= 10; tag++)
            if (CharacterRole.Label(tag, true).Contains("추천") || CharacterRole.Label(tag, false).Contains("Slots")) throw new System.Exception("Slot recommendation remains");
        if (CharacterRole.Label(1000, true) != "" || CharacterRole.Label(0, false) != "") throw new System.Exception("Unknown tag");
        for (int tag = 1; tag <= 5; tag++)
            if (CharacterRole.Label(tag, true) == "" || CharacterRole.Label(tag, false) == "") throw new System.Exception("Missing language");
    }
}
""""""";

}

