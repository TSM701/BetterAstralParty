$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../src/RuntimeObject.cs') -Raw
$check = [regex]::Match($source, '(?s)    private static void CheckArgument\(.*?\r?\n    \}').Value
$invoke = [regex]::Match($source, '(?s)    private static RuntimeObject\? Invoke\(.*?\r?\n    \}').Value
if (!$check -or !$invoke) { throw 'Production argument check/invoke method not found' }

# Execute the production branch logic, substituting only native metadata (the game need not run).
$harness = @'
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
'@
$current = $harness.Replace('__NAMESPACE__', 'CurrentArguments').Replace('__CHECK_ARGUMENT__', $check).Replace('__INVOKE__', $invoke)
$test = Add-Type -TypeDefinition $current -CompilerOptions '/unsafe' -PassThru | Where-Object FullName -eq 'CurrentArguments.RuntimeObject'
$test::Run()

# Prove this check catches the reported regression, not just the new happy path.
$oldCheck = [regex]::Replace($check, 'case string when.*?: return;', 'case string when IL2CPP.il2cpp_type_get_name_(type) == "System.String": return;', 'Singleline')
$old = $harness.Replace('__NAMESPACE__', 'OldArguments').Replace('__CHECK_ARGUMENT__', $oldCheck).Replace('__INVOKE__', $invoke)
$regression = Add-Type -TypeDefinition $old -CompilerOptions '/unsafe' -PassThru | Where-Object FullName -eq 'OldArguments.RuntimeObject'
if ($regression::AcceptsObjectString()) { throw 'Regression probe did not reproduce the old rejection' }
$oldCheck = [regex]::Replace($check, 'case UnityEngine\.Quaternion:.*?return;', '')
$oldInvoke = [regex]::Replace($invoke, 'case UnityEngine\.Quaternion rotation:.*?break;', '')
$old = $harness.Replace('__NAMESPACE__', 'MissingQuaternionGuard').Replace('__CHECK_ARGUMENT__', $oldCheck).Replace('__INVOKE__', $invoke)
$regression = Add-Type -TypeDefinition $old -CompilerOptions '/unsafe' -PassThru | Where-Object FullName -eq 'MissingQuaternionGuard.RuntimeObject'
if ($regression::AcceptsQuaternion() -or $regression::InvokesQuaternion()) { throw 'Missing Quaternion guard regression not reproduced' }
$old = $harness.Replace('__NAMESPACE__', 'MissingQuaternionMarshaling').Replace('__CHECK_ARGUMENT__', $check).Replace('__INVOKE__', $oldInvoke)
$regression = Add-Type -TypeDefinition $old -CompilerOptions '/unsafe' -PassThru | Where-Object FullName -eq 'MissingQuaternionMarshaling.RuntimeObject'
if ($regression::InvokesQuaternion()) { throw 'Missing Quaternion marshaling regression not reproduced' }
Write-Host 'Native argument regression: production check/invoke preserve all four Quaternion values; invalid targets rejected; missing guard/marshaling and old Object rejection reproduced (native metadata/invocation substitute).'
