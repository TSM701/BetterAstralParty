$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../src/RuntimeObject.cs') -Raw
$getter = [regex]::Match($source, '(?s)    internal T Get<T>\(.*?\r?\n    \}').Value
$accessor = [regex]::Match($source, '(?s)    private static string Accessor\(.*?\r?\n    \}').Value
$fieldGetter = [regex]::Match($source, '(?s)    internal T Field<T>\(.*?\r?\n    \}').Value
if (!$getter -or !$accessor -or !$fieldGetter) { throw 'Production getter missing' }
$harness = @'
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
'@
$code = $harness.Replace('__ACCESSOR__', $accessor).Replace('__GETTER__', $getter).Replace('__FIELD_GETTER__', $fieldGetter)
Add-Type -TypeDefinition $code -CompilerOptions '/unsafe'
[GetterProbe]::Run()
