using Il2CppInterop.Runtime;

namespace BetterAstralParty;

// The game's FairyGUI assembly is loaded after BepInEx generates its bindings.
// Resolve its actual runtime metadata; never load a second copy of game code.
internal sealed unsafe class RuntimeObject
{
    private readonly Il2CppSystem.Object _object;
    internal IntPtr Pointer => _object.Pointer;
    internal IntPtr Class => _object.ObjectClass;
    internal string TypeName
    {
        get
        {
            var klass = Class;
            if (!Names.TryGetValue(klass, out var name)) Names[klass] = name = IL2CPP.il2cpp_class_get_name_(klass) ?? "";
            return name;
        }
    }
    private static readonly Dictionary<(IntPtr, string, int), IntPtr> Methods = new();
    // Metadata only; never retain live game state or cache failed late-loaded lookups.
    private static readonly Dictionary<(string, string), IntPtr> Classes = new();
    private static readonly Dictionary<(IntPtr, string), IntPtr> Fields = new();
    private static readonly Dictionary<IntPtr, string> Names = new();
    private static readonly HashSet<(IntPtr, Type)> CheckedValues = new();
    private static readonly HashSet<string> CheckedFeatures = new();
    private static readonly Dictionary<string, string> Getters = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Setters = new(StringComparer.Ordinal);

    internal static void RequireClasses(string feature, params (string Namespace, string Name)[] classes)
    {
        if (CheckedFeatures.Contains(feature)) return;
        foreach (var (ns, name) in classes)
            if (FindClass(ns, name) == IntPtr.Zero) throw new TypeLoadException(ns + "." + name);
        CheckedFeatures.Add(feature);
    }

    internal RuntimeObject(IntPtr pointer) : this(new Il2CppSystem.Object(pointer)) { }
    internal RuntimeObject(Il2CppSystem.Object value) => _object = value;
    private static RuntimeObject? Wrap(IntPtr pointer) => pointer == IntPtr.Zero ? null : new(pointer);

    internal static IntPtr FindClass(string ns, string name)
    {
        if (Classes.TryGetValue((ns, name), out var cached)) return cached;
        uint count = 0;
        var assemblies = IL2CPP.il2cpp_domain_get_assemblies(IL2CPP.il2cpp_domain_get(), ref count);
        for (var i = 0; i < count; i++)
        {
            var image = IL2CPP.il2cpp_assembly_get_image(assemblies[i]);
            var klass = IL2CPP.il2cpp_class_from_name(image, ns, name);
            if (klass != IntPtr.Zero) { Classes[(ns, name)] = klass; return klass; }
        }
        return IntPtr.Zero;
    }

    private static IntPtr FieldInfo(IntPtr klass, string name)
    {
        if (klass == IntPtr.Zero) throw new TypeLoadException("Required runtime class is unavailable");
        if (Fields.TryGetValue((klass, name), out var cached)) return cached;
        for (var c = klass; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
        {
            var field = IL2CPP.il2cpp_class_get_field_from_name(c, name);
            if (field != IntPtr.Zero) { Fields[(klass, name)] = field; return field; }
        }
        throw new MissingFieldException(name);
    }

    internal RuntimeObject? Field(string name) => Wrap(IL2CPP.il2cpp_field_get_value_object(FieldInfo(Class, name), Pointer));
    internal T Field<T>(string name) where T : unmanaged
    {
        var field = FieldInfo(Class, name);
        CheckValue<T>(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field)));
        // Copy the live value without a native box, managed wrapper or extra GC handle.
        T value = default;
        IL2CPP.il2cpp_field_get_value(Pointer, field, &value);
        GC.KeepAlive(this);
        return value;
    }
    internal static RuntimeObject? StaticField(IntPtr klass, string name) => Wrap(IL2CPP.il2cpp_field_get_value_object(FieldInfo(klass, name), IntPtr.Zero));
    internal T Value<T>() where T : unmanaged
    {
        CheckValue<T>(Class);
        return *(T*)IL2CPP.il2cpp_object_unbox(Pointer);
    }
    internal string String()
    {
        if (TypeName != "String") throw new InvalidCastException("Expected System.String");
        return IL2CPP.Il2CppStringToManaged(Pointer) ?? "";
    }
    private static string Accessor(Dictionary<string, string> names, string prefix, string property)
    {
        if (!names.TryGetValue(property, out var name)) names[property] = name = prefix + property;
        return name;
    }
    internal RuntimeObject? Get(string property) => Call(Accessor(Getters, "get_", property));
    internal T Get<T>(string property) where T : unmanaged
    {
        var method = MethodInfo(Class, Accessor(Getters, "get_", property), 0);
        // Validate immutable metadata before invoking. Copy the boxed value immediately;
        // no managed wrapper/GC handle is needed for a value we never retain.
        CheckValue<T>(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_method_get_return_type(method)));
        var instance = IL2CPP.il2cpp_class_is_valuetype(Class) ? IL2CPP.il2cpp_object_unbox(Pointer) : Pointer;
        IntPtr exception = IntPtr.Zero;
        var result = IL2CPP.il2cpp_runtime_invoke(method, instance, null, ref exception);
        if (exception != IntPtr.Zero || result == IntPtr.Zero) throw new InvalidOperationException("Game UI getter failed: " + property);
        var value = *(T*)IL2CPP.il2cpp_object_unbox(result);
        GC.KeepAlive(this);
        return value;
    }
    internal void Set(string property, object value) => Call(Accessor(Setters, "set_", property), value);
    internal void SetField<T>(string name, T value) where T : unmanaged
    {
        var field = FieldInfo(Class, name);
        CheckValue<T>(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field)));
        IL2CPP.il2cpp_field_set_value(Pointer, field, &value);
    }
    internal void SetField(string name, RuntimeObject? value)
    {
        var field = FieldInfo(Class, name);
        CheckArgument(IL2CPP.il2cpp_field_get_type(field), value);
        IL2CPP.il2cpp_field_set_value(Pointer, field, (void*)(value?.Pointer ?? IntPtr.Zero));
        GC.KeepAlive(value);
        GC.KeepAlive(this);
    }
    internal void SetField(string name, string value)
    {
        var str = new RuntimeObject(IL2CPP.ManagedStringToIl2Cpp(value));
        var field = FieldInfo(Class, name);
        CheckArgument(IL2CPP.il2cpp_field_get_type(field), value);
        // Reference fields take the object pointer itself, unlike unmanaged value fields.
        IL2CPP.il2cpp_field_set_value(Pointer, field, (void*)str.Pointer);
        GC.KeepAlive(str);
        GC.KeepAlive(this);
    }

    internal RuntimeObject? Call(string method, params object?[] args)
    {
        // Boxed native structs (including UniTask awaiters) need their value address as this.
        var instance = IL2CPP.il2cpp_class_is_valuetype(Class) ? IL2CPP.il2cpp_object_unbox(Pointer) : Pointer;
        var result = Invoke(Class, instance, method, args);
        GC.KeepAlive(this);
        return result;
    }
    internal static RuntimeObject? StaticCall(IntPtr klass, string method, params object?[] args) => Invoke(klass, IntPtr.Zero, method, args);
    internal static RuntimeObject New(IntPtr klass, params object[] args)
    {
        if (klass == IntPtr.Zero) throw new TypeLoadException("Required runtime class is unavailable");
        var obj = new RuntimeObject(IL2CPP.il2cpp_object_new(klass));
        obj.Call(".ctor", args);
        return obj;
    }

    private static IntPtr MethodInfo(IntPtr klass, string name, int count)
    {
        if (klass == IntPtr.Zero) throw new TypeLoadException("Required runtime class is unavailable: " + name);
        var key = (klass, name, count);
        if (!Methods.TryGetValue(key, out var method))
        {
            for (var c = klass; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
            {
                method = IL2CPP.il2cpp_class_get_method_from_name(c, name, count);
                if (method != IntPtr.Zero) break;
            }
            if (method == IntPtr.Zero) throw new MissingMethodException(name);
            Methods[key] = method;
        }
        return method;
    }

    private static RuntimeObject? Invoke(IntPtr klass, IntPtr instance, string name, object?[] args)
    {
        var method = MethodInfo(klass, name, args.Length);
        var values = stackalloc byte[args.Length * 32];
        var pointers = stackalloc void*[args.Length];
        List<RuntimeObject>? strings = null;
        for (var i = 0; i < args.Length; i++)
        {
            CheckArgument(IL2CPP.il2cpp_method_get_param(method, (uint)i), args[i]);
            var slot = values + i * 32;
            pointers[i] = slot;
            switch (args[i])
            {
                case null: pointers[i] = null; break;
                case RuntimeObject obj: pointers[i] = (void*)obj.Pointer; break;
                case string s:
                    var str = new RuntimeObject(IL2CPP.ManagedStringToIl2Cpp(s));
                    (strings ??= new()).Add(str);
                    pointers[i] = (void*)str.Pointer;
                    break;
                case int n: *(int*)slot = n; break;
                case float f: *(float*)slot = f; break;
                case bool b: *slot = b ? (byte)1 : (byte)0; break;
                case UnityEngine.Color color: *(UnityEngine.Color*)slot = color; break;
                case UnityEngine.Vector2 point: *(UnityEngine.Vector2*)slot = point; break;
                case UnityEngine.Vector3 point: *(UnityEngine.Vector3*)slot = point; break;
                case UnityEngine.Quaternion rotation: *(UnityEngine.Quaternion*)slot = rotation; break;
                default: throw new ArgumentException("Unsupported runtime argument");
            }
        }
        IntPtr exception = IntPtr.Zero;
        var result = IL2CPP.il2cpp_runtime_invoke(method, instance, pointers, ref exception);
        GC.KeepAlive(strings);
        GC.KeepAlive(args);
        if (exception != IntPtr.Zero) throw new InvalidOperationException($"Game UI call failed: {name}");
        return Wrap(result);
    }

    // Verify before unboxing/writing/invoking; never guess an overload after an update.
    private static void CheckValue<T>(IntPtr klass) where T : unmanaged
    {
        if (CheckedValues.Contains((klass, typeof(T)))) return;
        if (klass == IntPtr.Zero || !IL2CPP.il2cpp_class_is_valuetype(klass))
            throw new InvalidCastException("Expected value type " + typeof(T).FullName);
        var type = IL2CPP.il2cpp_class_is_enum(klass) ? IL2CPP.il2cpp_class_enum_basetype(klass)
            : IL2CPP.il2cpp_class_get_type(klass);
        var name = IL2CPP.il2cpp_type_get_name_(type);
        uint alignment = 0;
        if (name != typeof(T).FullName || IL2CPP.il2cpp_class_value_size(klass, ref alignment) != sizeof(T))
            throw new InvalidCastException($"Runtime value mismatch: {name} / {typeof(T).FullName}");
        CheckedValues.Add((klass, typeof(T)));
    }

    private static void CheckArgument(IntPtr type, object? value)
    {
        if (type == IntPtr.Zero || IL2CPP.il2cpp_type_is_byref(type))
            throw new InvalidCastException("Unsupported by-ref runtime argument");
        var klass = IL2CPP.il2cpp_class_from_type(type);
        if (klass == IntPtr.Zero) throw new TypeLoadException("Runtime argument type unavailable");
        switch (value)
        {
            case int: CheckValue<int>(klass); return;
            case float: CheckValue<float>(klass); return;
            case bool: CheckValue<bool>(klass); return;
            case UnityEngine.Color: CheckValue<UnityEngine.Color>(klass); return;
            case UnityEngine.Vector2: CheckValue<UnityEngine.Vector2>(klass); return;
            case UnityEngine.Vector3: CheckValue<UnityEngine.Vector3>(klass); return;
            case UnityEngine.Quaternion: CheckValue<UnityEngine.Quaternion>(klass); return;
            // Managed strings are marshalled as native String references, also valid for Object/interfaces.
            case string when !IL2CPP.il2cpp_class_is_valuetype(klass)
                && IL2CPP.il2cpp_class_is_assignable_from(klass, FindClass("System", "String")): return;
            case RuntimeObject obj when !IL2CPP.il2cpp_class_is_valuetype(klass)
                && IL2CPP.il2cpp_class_is_assignable_from(klass, obj.Class): return;
            case null when !IL2CPP.il2cpp_class_is_valuetype(klass): return;
            default: throw new InvalidCastException("Runtime argument mismatch: " + IL2CPP.il2cpp_type_get_name_(type));
        }
    }
}
