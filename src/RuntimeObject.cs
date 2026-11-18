using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime;

namespace BetterAstralParty;

// The game's FairyGUI assembly is loaded after BepInEx generates its bindings.
// Resolve its actual runtime metadata; never load a second copy of game code.
internal sealed unsafe class RuntimeObject
{
    // One strong IL2CPP GC handle per wrapper, as Il2CppInterop's Il2CppObjectBase takes, without that second managed object.
    // The IL2CPP heap never moves objects, so the pointer and class are read once instead of on every access. Hundreds of
    // wrappers are made each frame; their finalizable objects set the CLR GC pauses the 2026-11-12 play logs showed.
    private readonly IntPtr _handle;
    private IntPtr _class;
    internal IntPtr Pointer { get; }
    internal IntPtr Class => _class != IntPtr.Zero ? _class : _class = IL2CPP.il2cpp_object_get_class(Pointer);
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
    private static readonly HashSet<IntPtr> CheckedReferences = new();
    private static readonly HashSet<(IntPtr, Type)> CheckedFields = new();
    private static readonly HashSet<string> CheckedFeatures = new();
    private static readonly Dictionary<string, string> Getters = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Setters = new(StringComparer.Ordinal);

    // Getters whose whole body returns one field of their own class (`ldarg.0; ldfld F; ret`), read as that field when the
    // getter an object resolves is that class's own, not an override; runtime-getters checks each against the research
    // extract. FairyGUI and the game's UI run interpreted (HybridCLR, AstralParty.Runtime), so every such call went through
    // the interpreter and boxed a value result on the IL2CPP heap, hundreds a frame: the 2026-11-17 logs put 6-7 IL2CPP
    // collections a 10-second window inside the mod's tick against 1-2 outside it, and the tick at 17 % of a frame.
    internal static readonly (string Class, string Property, string Field)[] PlainGetters =
    {
        ("FairyGUI.GObject", "isDisposed", "_disposed"), ("FairyGUI.GObject", "visible", "_visible"),
        ("FairyGUI.GObject", "alpha", "_alpha"), ("FairyGUI.GObject", "touchable", "_touchable"),
        ("FairyGUI.GObject", "grayed", "_grayed"), ("FairyGUI.GObject", "sortingOrder", "_sortingOrder"),
        ("FairyGUI.GObject", "x", "_x"), ("FairyGUI.GObject", "y", "_y"),
        ("FairyGUI.GObject", "width", "_width"), ("FairyGUI.GObject", "height", "_height"),
        ("FairyGUI.GObject", "scaleX", "_scaleX"), ("FairyGUI.GObject", "scaleY", "_scaleY"),
        ("FairyGUI.GObject", "pivotX", "_pivotX"), ("FairyGUI.GObject", "group", "_group"),
        ("FairyGUI.GObject", "parent", "<parent>k__BackingField"),
        ("FairyGUI.GObject", "displayObject", "<displayObject>k__BackingField"),
        ("FairyGUI.DisplayObject", "parent", "<parent>k__BackingField"),
        ("FairyGUI.Controller", "selectedIndex", "_selectedIndex"),
        ("FairyGUI.Window", "contentPane", "_contentPane"),
        ("GameLogic.GameLogicManager", "room", "<room>k__BackingField"),
        ("GameLogic.GameLogicManager", "battle", "<battle>k__BackingField"),
        ("GameLogic.GameLogicManager", "fight", "<fight>k__BackingField"),
        ("GameLogic.GameLogicManager", "account", "<account>k__BackingField"),
        ("GameLogic.GameLogicManager", "watch", "<watch>k__BackingField"),
        ("GameLogic.GameLogicManager", "communicate", "<communicate>k__BackingField"),
        ("GameLogic.GameLogicManager", "replay", "<replay>k__BackingField"),
        ("System.Collections.Generic.List`1", "Count", "_size"),
        ("FairyGUI.MovieClip", "frame", "_frame"),
        ("Core.Scene.BattleSceneController", "mainCamera", "<mainCamera>k__BackingField"),
        ("GameLogic.RoomController", "localRoom", "_LocalRoom"),
        ("party.model.Room", "MapType", "mapType_"),
        ("party.model.Room", "Id", "id_"),
        ("FairyGUI.NGraphics", "color", "_color"),
        ("FairyGUI.DisplayObject", "graphics", "<graphics>k__BackingField"),
        ("Core.Scene.BattleSceneController", "cinemachineBrain", "<cinemachineBrain>k__BackingField"),
        ("GameLogic.GameLogicManager", "connectRoomId", "<connectRoomId>k__BackingField"),
        ("BattleShowDirector", "victim", "_victim"),
        ("BattleShowDirector", "attacker", "_attacker"),
        ("Tools.ReactiveProperty`1", "Value", "_value"),
        ("UI.UIHandCard_Button_Card", "CardData", "_CardData"),
        ("FairyGUI.GObject", "rotation", "_rotation"),
        ("FairyGUI.GObject", "pivotY", "_pivotY"),
        ("Tools.ReadOnlyReactiveProperty`1", "Value", "_value"),
    };
    // Getters whose whole body returns a getter of one of their fields (`ldarg.0; ldfld F; callvirt get_P; ret`): read through
    // the field, then that getter of the object it holds, resolved on that object as the call would be (2026-11-17 call log:
    // curRoomInfo about 6 times a frame, the player tabs' movie-clip frame 8). A null field goes through the call, which
    // fails as the game's does.
    internal static readonly (string Class, string Property, string Field, string Next)[] ChainGetters =
    {
        ("GameLogic.RoomLogic", "curRoomInfo", "roomController", "localRoom"),
        ("FairyGUI.GMovieClip", "frame", "_content", "frame"),
        ("FairyGUI.GMovieClip", "color", "_content", "color"),
        // Image.color calls the plain DisplayObject.graphics getter instead of reading the field; the same field.
        ("FairyGUI.Image", "color", "<graphics>k__BackingField", "color"),
    };
    private static readonly Dictionary<IntPtr, (int Offset, string Next)> ChainOffsets = new();
    private static int ChainOffset(IntPtr method, string property, out string next)
    {
        if (!ChainOffsets.TryGetValue(method, out var chain))
        {
            chain = (-1, "");
            var owner = IL2CPP.il2cpp_method_get_class(method);
            var name = ClassName(owner);
            foreach (var (type, getter, fieldName, nextProperty) in ChainGetters)
            {
                if (getter != property || type != name) continue;
                var field = IntPtr.Zero;
                if (owner != IntPtr.Zero && !IL2CPP.il2cpp_class_is_valuetype(owner))
                    for (var c = owner; c != IntPtr.Zero && field == IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
                        field = IL2CPP.il2cpp_class_get_field_from_name(c, fieldName);
                if (field != IntPtr.Zero && (IL2CPP.il2cpp_field_get_flags(field) & 0x10) == 0
                    && !IL2CPP.il2cpp_class_is_valuetype(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field))))
                {
                    chain = ((int)IL2CPP.il2cpp_field_get_offset(field), nextProperty);
                    Note("chain " + name + "." + property);
                }
                break;
            }
            ChainOffsets[method] = chain;
        }
        next = chain.Next;
        return chain.Offset;
    }

    // Setters that return at once when the value already stands (their bodies begin with that comparison; runtime-getters
    // checks it): skipped when the fields already hold the value, as the setter itself would, without the interpreted call.
    // Many UI parts are written every frame with what they already show (2026-11-17 call log: set_visible, set_touchable,
    // SetXY, SetSize, SetScale up to 10 a frame each).
    internal static readonly (string Class, string Method, string First, string Second)[] GuardedSetters =
    {
        ("FairyGUI.GObject", "set_visible", "_visible", ""), ("FairyGUI.GObject", "set_touchable", "_touchable", ""),
        ("FairyGUI.GObject", "set_grayed", "_grayed", ""),
        ("FairyGUI.GObject", "SetXY", "_x", "_y"), ("FairyGUI.GObject", "SetScale", "_scaleX", "_scaleY"),
        ("FairyGUI.GObject", "SetSize", "_rawWidth", "_rawHeight"),
    };
    private static readonly Dictionary<IntPtr, (int First, int Second)> GuardOffsets = new();
    private static (int First, int Second) GuardOffset(IntPtr method, string name)
    {
        if (GuardOffsets.TryGetValue(method, out var guard)) return guard;
        guard = (-1, -1);
        var owner = IL2CPP.il2cpp_method_get_class(method);
        var type = ClassName(owner);
        foreach (var (declaring, setter, first, second) in GuardedSetters)
        {
            if (setter != name || declaring != type) continue;
            int Offset(string field)
            {
                var info = field.Length == 0 ? IntPtr.Zero : IL2CPP.il2cpp_class_get_field_from_name(owner, field);
                return info == IntPtr.Zero ? -1 : (int)IL2CPP.il2cpp_field_get_offset(info);
            }
            guard = (Offset(first), second.Length == 0 ? -1 : Offset(second));
            if (guard.First < 0 || second.Length > 0 && guard.Second < 0) guard = (-1, -1);
            else Note("guarded " + type + "." + name);
            break;
        }
        GuardOffsets[method] = guard;
        return guard;
    }
    // Per resolved getter: the offset of the field it returns, from the object's start (as il2cpp_field_get_value reads it),
    // or -1 to call it; and the plain getters whose field holds a reference.
    private static readonly Dictionary<IntPtr, int> PlainOffsets = new();
    private static readonly HashSet<IntPtr> PlainReferences = new();
    private static int PlainOffset(IntPtr method, string property)
    {
        if (PlainOffsets.TryGetValue(method, out var offset)) return offset;
        offset = -1;
        var owner = IL2CPP.il2cpp_method_get_class(method);
        var name = ClassName(owner);
        foreach (var (type, getter, fieldName) in PlainGetters)
        {
            if (getter != property || type != name) continue;
            // The field may be a base class's (ReadOnlyReactiveProperty reads ReactiveProperty._value).
            var field = IntPtr.Zero;
            if (owner != IntPtr.Zero && !IL2CPP.il2cpp_class_is_valuetype(owner))
                for (var c = owner; c != IntPtr.Zero && field == IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
                    field = IL2CPP.il2cpp_class_get_field_from_name(c, fieldName);
            var result = IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_method_get_return_type(method));
            if (field != IntPtr.Zero && (IL2CPP.il2cpp_field_get_flags(field) & 0x10) == 0 // FIELD_ATTRIBUTE_STATIC
                && IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field)) == result && result != IntPtr.Zero)
            {
                offset = (int)IL2CPP.il2cpp_field_get_offset(field);
                if (!IL2CPP.il2cpp_class_is_valuetype(result)) PlainReferences.Add(method);
                Note("plain " + name + "." + property);
            }
            break;
        }
        PlainOffsets[method] = offset;
        return offset;
    }

    // Getters and one method computed from fields as their bodies do (runtime-getters checks the bodies): FairyGUI's
    // visibility state, child count, children and asCom, read on every node of every window walk and visibility check,
    // when the method an object resolves is FairyGUI's own (an override goes through the call).
    private const int NotComputed = 0, OnStage = 1, InternalVisible = 2, InternalVisible2 = 3, NumChildren = 4, AsCom = 5, ChildAt = 6, ChildIndex = 7;
    private static readonly Dictionary<IntPtr, int> ComputedKinds = new();
    private static IntPtr _stageClass, _componentClass;
    private static int ComputedKind(IntPtr method, string name)
    {
        if (ComputedKinds.TryGetValue(method, out var kind)) return kind;
        var owner = ClassName(IL2CPP.il2cpp_method_get_class(method));
        kind = (owner, name) switch
        {
            ("FairyGUI.GObject", "get_onStage") => OnStage,
            ("FairyGUI.GObject", "get_internalVisible") => InternalVisible,
            ("FairyGUI.GObject", "get_internalVisible2") => InternalVisible2,
            ("FairyGUI.GObject", "get_asCom") => AsCom,
            ("FairyGUI.GComponent", "get_numChildren") => NumChildren,
            ("FairyGUI.GComponent", "GetChildAt") => ChildAt,
            ("FairyGUI.GComponent", "GetChildIndex") => ChildIndex,
            _ => NotComputed,
        };
        if (kind != NotComputed)
        {
            if (_stageClass == IntPtr.Zero) _stageClass = FindClass("FairyGUI", "Stage");
            if (_componentClass == IntPtr.Zero) _componentClass = FindClass("FairyGUI", "GComponent");
            if (_stageClass == IntPtr.Zero || _componentClass == IntPtr.Zero) kind = NotComputed;
            else Note("computed " + owner + "." + name);
        }
        ComputedKinds[method] = kind;
        return kind;
    }
    // A computed bool or int getter's value; false where the getter is not computed (or the component has no child list).
    private static bool Computed<T>(IntPtr method, string property, IntPtr instance, out T value) where T : unmanaged
    {
        value = default;
        var kind = ComputedKind(method, Accessor(Getters, "get_", property));
        if (kind is OnStage or InternalVisible or InternalVisible2 && typeof(T) == typeof(bool))
        {
            var shown = kind == OnStage ? OnStageOf(instance) : ShownOf(instance, kind == InternalVisible ? "_internalVisible" : "_visible");
            value = *(T*)&shown;
            return true;
        }
        if (kind == NumChildren && typeof(T) == typeof(int))
        {
            var list = FieldPointerOf(instance, "_children");
            if (list == IntPtr.Zero) return false;
            var count = FieldOf<int>(list, "_size");
            value = *(T*)&count;
            return true;
        }
        return false;
    }
    // onStage: the display object's topmost container (DisplayObject.topmost, up its parents) is the Stage.
    private static bool OnStageOf(IntPtr node)
    {
        var top = FieldPointerOf(node, "<displayObject>k__BackingField");
        if (top == IntPtr.Zero) return false;
        for (var up = FieldPointerOf(top, "<parent>k__BackingField"); up != IntPtr.Zero; up = FieldPointerOf(top, "<parent>k__BackingField")) top = up;
        return Is(top, _stageClass);
    }
    // internalVisible (flag _internalVisible) and internalVisible2 (flag _visible): the flag, then the same of the group.
    private static bool ShownOf(IntPtr node, string flag)
    {
        for (; node != IntPtr.Zero; node = FieldPointerOf(node, "_group"))
            if (!FieldOf<bool>(node, flag)) return false;
        return true;
    }
    // GComponent.GetChildIndex(child) as _children.IndexOf(child) (reference equality: no FairyGUI object overrides Equals,
    // runtime-getters checks), from the child list's fields; the call where the method is not FairyGUI's own.
    internal int IndexOfChild(RuntimeObject child)
    {
        var method = MethodInfo(Class, "GetChildIndex", 1);
        if (ComputedKind(method, "GetChildIndex") != ChildIndex) return Call("GetChildIndex", child)?.Value<int>() ?? -1;
        var list = FieldPointerOf(Pointer, "_children");
        if (list == IntPtr.Zero) return Call("GetChildIndex", child)?.Value<int>() ?? -1;
        var count = FieldOf<int>(list, "_size");
        var items = FieldPointerOf(list, "_items");
        var found = -1;
        for (var i = 0; i < count && items != IntPtr.Zero; i++)
            if (*(IntPtr*)(items + 4 * IntPtr.Size + i * IntPtr.Size) == child.Pointer) { found = i; break; }
        GC.KeepAlive(this);
        GC.KeepAlive(child);
        return found;
    }

    // GetChildAt(index) as _children[index]; zero out of range, where the call throws as the game's does.
    private static IntPtr ChildAtOf(IntPtr component, int index)
    {
        var list = FieldPointerOf(component, "_children");
        if (list == IntPtr.Zero || index < 0 || index >= FieldOf<int>(list, "_size")) return IntPtr.Zero;
        var items = FieldPointerOf(list, "_items");
        if (items == IntPtr.Zero || index >= (int)IL2CPP.il2cpp_array_length(items)) return IntPtr.Zero;
        return *(IntPtr*)(items + 4 * IntPtr.Size + index * IntPtr.Size);
    }

    // Native access ledger: each class/member is resolved once and then cached, so this records one entry per
    // distinct access while diagnostics run. Names only, never values. Read by the developer native-coverage command.
    private static readonly StringBuilder Ledger = new();
    private static readonly HashSet<string> LedgerMisses = new(StringComparer.Ordinal);
    private static long _ledgerAt;
    private static void Note(string entry)
    {
        if (Plugin.Diagnostics?.IsRecording != true) return;
        lock (Ledger)
        {
            if (entry.StartsWith("missing ", StringComparison.Ordinal) && !LedgerMisses.Add(entry)) return;
            if (Ledger.Length > 0) Ledger.Append("; ");
            Ledger.Append(entry);
            if (Ledger.Length >= 1500) FlushLedger();
        }
    }
    // Development builds with diagnostics recording: the native calls still made (il2cpp_runtime_invoke), counted per
    // method and logged every ten seconds as the most frequent per frame, to pick the next performance targets from data.
    private static readonly bool CountCalls = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private static readonly Dictionary<IntPtr, int> CallCounts = new();
    private static bool _counting;
    private static int _countedFrames;
    private static long _countedSince;
    // Beside the calls: time between named points of a tick (LapStart, then Lap at each point) and the wrappers made, to find
    // the managed costs the call log cannot show (2026-11-17: the grouped hand stayed at 0.2-0.3 ms a frame after its native
    // calls fell by 60).
    private static readonly Dictionary<string, long> LapTicks = new();
    private static long _wrappersMade;
    internal static long LapStart() => _counting ? Stopwatch.GetTimestamp() : 0;
    internal static void Lap(string label, ref long start)
    {
        if (start == 0) return;
        var now = Stopwatch.GetTimestamp();
        LapTicks.TryGetValue(label, out var ticks);
        LapTicks[label] = ticks + now - start;
        start = now;
    }
    private static void Counted(IntPtr method)
    {
        if (!_counting) return;
        CallCounts.TryGetValue(method, out var count);
        CallCounts[method] = count + 1;
    }
    private static void FlushCalls()
    {
        _counting = CountCalls && Plugin.Diagnostics?.IsRecording == true;
        if (!_counting) { CallCounts.Clear(); LapTicks.Clear(); _wrappersMade = 0; _countedFrames = 0; return; }
        _countedFrames++;
        if (_countedSince == 0) _countedSince = Stopwatch.GetTimestamp();
        if (Stopwatch.GetTimestamp() - _countedSince < 10 * Stopwatch.Frequency) return;
        var top = new List<KeyValuePair<IntPtr, int>>(CallCounts);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));
        var line = new StringBuilder("performance_calls frames=").Append(_countedFrames).Append("; per_frame=")
            .Append((CallCounts.Values.Sum() / (double)_countedFrames).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        for (var i = 0; i < Math.Min(20, top.Count); i++)
        {
            var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(IL2CPP.il2cpp_method_get_name(top[i].Key));
            line.Append("; ").Append(ClassName(IL2CPP.il2cpp_method_get_class(top[i].Key))).Append('.').Append(name).Append('=')
                .Append((top[i].Value / (double)_countedFrames).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }
        Plugin.Diagnostics?.Write(line.ToString());
        var laps = new StringBuilder("performance_laps frames=").Append(_countedFrames).Append("; wrappers_per_frame=")
            .Append((_wrappersMade / (double)_countedFrames).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var pair in LapTicks)
            laps.Append("; ").Append(pair.Key).Append("_us=")
                .Append((pair.Value * 1e6 / Stopwatch.Frequency / _countedFrames).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        Plugin.Diagnostics?.Write(laps.ToString());
        LapTicks.Clear(); _wrappersMade = 0;
        CallCounts.Clear(); _countedFrames = 0; _countedSince = Stopwatch.GetTimestamp();
    }

    // Called every frame; writes the pending entries at most every five seconds.
    internal static void FlushLedger(bool due = false)
    {
        if (due) FlushCalls();
        lock (Ledger)
        {
            if (Ledger.Length == 0 || due && Stopwatch.GetTimestamp() - _ledgerAt < 5 * Stopwatch.Frequency) return;
            Plugin.Diagnostics?.Write("native.access " + Ledger);
            Ledger.Clear(); _ledgerAt = Stopwatch.GetTimestamp();
        }
    }
    // Namespace-qualified native class name for failure messages and the ledger.
    internal static string ClassName(IntPtr klass)
    {
        if (klass == IntPtr.Zero) return "?";
        var ns = IL2CPP.il2cpp_class_get_namespace_(klass);
        var name = IL2CPP.il2cpp_class_get_name_(klass) ?? "?";
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
    private static bool HasField(IntPtr klass, string name)
    {
        for (var c = klass; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
            if (IL2CPP.il2cpp_class_get_field_from_name(c, name) != IntPtr.Zero) return true;
        return false;
    }
    private static bool HasMethod(IntPtr klass, string name, int count)
    {
        for (var c = klass; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
            if (IL2CPP.il2cpp_class_get_method_from_name(c, name, count) != IntPtr.Zero) return true;
        return false;
    }
    // A property probe that neither throws nor records a missing method.
    internal bool HasGetter(string property) => HasMethod(Class, "get_" + property, 0);

    internal static void RequireClasses(string feature, params (string Namespace, string Name)[] classes)
    {
        if (CheckedFeatures.Contains(feature)) return;
        foreach (var (ns, name) in classes)
            if (FindClass(ns, name) == IntPtr.Zero) throw new TypeLoadException(ns + "." + name);
        CheckedFeatures.Add(feature);
    }

    internal RuntimeObject(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) throw new NullReferenceException("Native object required");
        Pointer = pointer;
        _handle = IL2CPP.il2cpp_gchandle_new(pointer, false);
        if (_counting) _wrappersMade++;
    }
    internal RuntimeObject(Il2CppSystem.Object value) : this(value.Pointer) { }
    ~RuntimeObject() => IL2CPP.il2cpp_gchandle_free(_handle);
    private static RuntimeObject? Wrap(IntPtr pointer) => pointer == IntPtr.Zero ? null : Shared(pointer);

    // Wrappers are shared by pointer while one is alive (2026-11-17: about 185 were made each frame, mostly for the same
    // cards, parents and panels, and each 10-second window's slowest tick held a 10 ms CLR collection). The table keeps only
    // short weak references, cleared before a wrapper's finalizer frees its handle, so a wrapper still goes with the last
    // code holding it and the native object it holds is never handed out after its handle is freed. Boxed values and
    // strings, new objects on almost every call, are wrapped fresh. One table per thread.
    [ThreadStatic] private static Dictionary<IntPtr, WeakReference<RuntimeObject>>? _shared;
    [ThreadStatic] private static int _sweepAt;
    private static IntPtr _stringClass;
    private static RuntimeObject Shared(IntPtr pointer)
    {
        var klass = IL2CPP.il2cpp_object_get_class(pointer);
        if (_stringClass == IntPtr.Zero) _stringClass = IL2CPP.il2cpp_object_get_class(IL2CPP.ManagedStringToIl2Cpp(""));
        if (klass == _stringClass || IL2CPP.il2cpp_class_is_valuetype(klass)) return new RuntimeObject(pointer);
        var table = _shared ??= new Dictionary<IntPtr, WeakReference<RuntimeObject>>();
        if (table.TryGetValue(pointer, out var weak))
        {
            if (weak.TryGetTarget(out var known)) return known;
            var again = new RuntimeObject(pointer);
            weak.SetTarget(again);
            return again;
        }
        if (table.Count >= Math.Max(_sweepAt, 1024))
        {
            // Pointers whose wrappers are gone: a transient object's address may never come back.
            var dead = new List<IntPtr>();
            foreach (var pair in table) if (!pair.Value.TryGetTarget(out _)) dead.Add(pair.Key);
            foreach (var key in dead) table.Remove(key);
            _sweepAt = table.Count * 2;
        }
        var made = new RuntimeObject(pointer);
        table[pointer] = new WeakReference<RuntimeObject>(made);
        return made;
    }

    internal static IntPtr FindClass(string ns, string name)
    {
        if (Classes.TryGetValue((ns, name), out var cached)) return cached;
        uint count = 0;
        var assemblies = IL2CPP.il2cpp_domain_get_assemblies(IL2CPP.il2cpp_domain_get(), ref count);
        for (var i = 0; i < count; i++)
        {
            var image = IL2CPP.il2cpp_assembly_get_image(assemblies[i]);
            var klass = IL2CPP.il2cpp_class_from_name(image, ns, name);
            if (klass != IntPtr.Zero) { Classes[(ns, name)] = klass; Note("class " + ClassName(klass)); return klass; }
        }
        Note("missing class " + (ns.Length == 0 ? name : ns + "." + name));
        return IntPtr.Zero;
    }

    // The game mode table is static game data, read about six times a frame by several features (2026-11-17 call log, seven
    // interpreted calls each): each mode's answer is kept for five seconds.
    private static readonly Dictionary<int, (bool Pve, long Until)> PveModes = new();
    internal static bool IsPve(int mode)
    {
        var now = Stopwatch.GetTimestamp();
        if (PveModes.TryGetValue(mode, out var known) && now < known.Until) return known.Pve;
        var pve = ReadPve(mode);
        PveModes[mode] = (pve, now + 5 * Stopwatch.Frequency);
        return pve;
    }
    private static bool ReadPve(int mode)
    {
        var helper = FindClass("UI", "UIHelper");
        var configure = FindClass("", "StaticConfigure");
        if (helper == IntPtr.Zero || configure == IntPtr.Zero
            || StaticCall(configure, "get_GameMode")?.Get("InfoDict") == null) return false;
        var config = StaticCall(helper, "GetGameModeInfoConfigure", mode);
        return config != null && config.Get<int>("MapModeType") == mode && config.Get<int>("MapModeGroup") == 1;
    }

    private static IntPtr FieldInfo(IntPtr klass, string name)
    {
        if (klass == IntPtr.Zero) throw new TypeLoadException("Required runtime class is unavailable");
        if (Fields.TryGetValue((klass, name), out var cached)) return cached;
        for (var c = klass; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
        {
            var field = IL2CPP.il2cpp_class_get_field_from_name(c, name);
            if (field != IntPtr.Zero) { Fields[(klass, name)] = field; Note("field " + ClassName(klass) + "." + name); return field; }
        }
        // Name the class and point at the likely mix-up (a property read as a field).
        var target = ClassName(klass) + "." + name;
        var hint = HasMethod(klass, "get_" + name, 0) ? " (a property of that name exists, read it with Get)" : "";
        Note("missing field " + target + hint);
        throw new MissingFieldException("Native field missing: " + target + hint);
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
    // Reference-field identity without a managed wrapper or GC handle. Compare it within the frame only; never retain it.
    internal IntPtr FieldPointer(string name)
    {
        var field = FieldInfo(Class, name);
        if (!CheckedReferences.Contains(field))
        {
            if (IL2CPP.il2cpp_class_is_valuetype(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field))))
                throw new InvalidCastException("Expected reference field " + name);
            CheckedReferences.Add(field);
        }
        IntPtr value = IntPtr.Zero;
        IL2CPP.il2cpp_field_get_value(Pointer, field, &value);
        GC.KeepAlive(this);
        return value;
    }
    internal static IntPtr ClassOf(IntPtr pointer) => pointer == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_object_get_class(pointer);
    // C#'s `is`: the object's class is klass or derives from it.
    internal static bool Is(IntPtr pointer, IntPtr klass) =>
        pointer != IntPtr.Zero && klass != IntPtr.Zero && IL2CPP.il2cpp_class_is_assignable_from(klass, ClassOf(pointer));
    // A native byte[]'s contents in one copy, from after the array's four-pointer header (as Il2CppInterop's
    // ArrayStartPointer): indexing an Il2CppStructArray crosses into native code for every element, and the home portrait's
    // pixels took about 100 ms that way (2026-11-17 logs: home.portrait baked 130 ms after the home showed).
    internal static byte[] Bytes(IntPtr array)
    {
        var length = (int)IL2CPP.il2cpp_array_length(array);
        var bytes = new byte[length];
        if (length > 0) System.Runtime.InteropServices.Marshal.Copy(array + 4 * IntPtr.Size, bytes, 0, length);
        return bytes;
    }
    // Fields of an object read without wrapping it, to filter many objects of a live native collection within one frame;
    // nothing here allocates, so the object cannot be collected between the reads. Wrap it before keeping or calling it.
    internal static T FieldOf<T>(IntPtr instance, string name) where T : unmanaged
    {
        var field = FieldInfo(ClassOf(instance), name);
        if (!CheckedFields.Contains((field, typeof(T))))
        {
            CheckValue<T>(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field)));
            CheckedFields.Add((field, typeof(T)));
        }
        T value = default;
        IL2CPP.il2cpp_field_get_value(instance, field, &value);
        return value;
    }
    internal static IntPtr FieldPointerOf(IntPtr instance, string name)
    {
        var field = FieldInfo(ClassOf(instance), name);
        if (!CheckedReferences.Contains(field))
        {
            if (IL2CPP.il2cpp_class_is_valuetype(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field))))
                throw new InvalidCastException("Expected reference field " + name);
            CheckedReferences.Add(field);
        }
        IntPtr value = IntPtr.Zero;
        IL2CPP.il2cpp_field_get_value(instance, field, &value);
        return value;
    }
    internal static RuntimeObject? StaticField(IntPtr klass, string name) => Wrap(IL2CPP.il2cpp_field_get_value_object(FieldInfo(klass, name), IntPtr.Zero));
    // A static value field of klass, written after its class initializer ran. An instance field is refused: its offset
    // does not point into the class's static data.
    internal static void SetStaticField<T>(IntPtr klass, string name, T value) where T : unmanaged
    {
        var field = FieldInfo(klass, name);
        if ((IL2CPP.il2cpp_field_get_flags(field) & 0x10) == 0) throw new InvalidOperationException("Expected static field " + name); // FIELD_ATTRIBUTE_STATIC
        CheckValue<T>(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field)));
        IL2CPP.il2cpp_runtime_class_init(klass);
        IL2CPP.il2cpp_field_static_set_value(field, &value);
    }
    internal static int EnumValue(string ns, string name, string field) => StaticField(FindClass(ns, name), field)!.Value<int>();
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
    internal RuntimeObject? Get(string property)
    {
        var name = Accessor(Getters, "get_", property);
        var method = MethodInfo(Class, name, 0);
        if (ComputedKind(method, name) == AsCom) return Is(Pointer, _componentClass) ? this : null;
        var link = ChainOffset(method, property, out var nextProperty);
        if (link >= 0 && *(IntPtr*)((byte*)Pointer + link) is var holder && holder != IntPtr.Zero)
        {
            var reached = GetPointerOf(holder, nextProperty);
            GC.KeepAlive(this);
            return Wrap(reached);
        }
        var plain = PlainOffset(method, property);
        if (plain < 0 || !PlainReferences.Contains(method)) return Call(name);
        var pointer = *(IntPtr*)((byte*)Pointer + plain);
        GC.KeepAlive(this);
        return Wrap(pointer);
    }
    internal T Get<T>(string property) where T : unmanaged
    {
        var method = MethodInfo(Class, Accessor(Getters, "get_", property), 0);
        // Validate immutable metadata before invoking. Copy the boxed value immediately;
        // no managed wrapper/GC handle is needed for a value we never retain.
        CheckValue<T>(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_method_get_return_type(method)));
        var plain = PlainOffset(method, property);
        if (plain >= 0)
        {
            var read = *(T*)((byte*)Pointer + plain);
            GC.KeepAlive(this);
            return read;
        }
        if (Computed(method, property, Pointer, out T computed))
        {
            GC.KeepAlive(this);
            return computed;
        }
        var link = ChainOffset(method, property, out var nextProperty);
        if (link >= 0 && *(IntPtr*)((byte*)Pointer + link) is var holder && holder != IntPtr.Zero)
        {
            var reached = GetOf<T>(holder, nextProperty);
            GC.KeepAlive(this);
            return reached;
        }
        var instance = IL2CPP.il2cpp_class_is_valuetype(Class) ? IL2CPP.il2cpp_object_unbox(Pointer) : Pointer;
        IntPtr exception = IntPtr.Zero;
        Counted(method);
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
    // Allocation-free forms for per-frame paths (second performance round, 2026-11-13: zoomed-out field play still allocated
    // 30 KB a frame). A call with one or two unmanaged values, or one value and an object, binds here instead of the params
    // form, so no argument array and no boxed values; Set does the same. Result<T> also skips the wrapper of a value result.
    internal RuntimeObject? Call<A>(string method, A a) where A : unmanaged
    {
        if (typeof(A) == typeof(NullableRect)) return Call(method, new object?[] { a }); // Its own layout; see CheckNullableRect.
        var info = MethodInfo(Class, method, 1); ValueParameter<A>(info, 0);
        if (typeof(A) == typeof(int) && ComputedKind(info, method) == ChildAt && ChildAtOf(Pointer, *(int*)&a) is var child && child != IntPtr.Zero)
        {
            GC.KeepAlive(this);
            return Wrap(child)!;
        }
        if (typeof(A) == typeof(bool) && GuardOffset(info, method) is { First: >= 0, Second: < 0 } guard
            && *(bool*)((byte*)Pointer + guard.First) == *(bool*)&a)
        {
            GC.KeepAlive(this);
            return null;
        }
        void** args = stackalloc void*[1]; args[0] = &a;
        return Wrap(InvokeRaw(info, args, method));
    }
    internal RuntimeObject? Call<A, B>(string method, A a, B b) where A : unmanaged where B : unmanaged
    {
        if (typeof(A) == typeof(NullableRect) || typeof(B) == typeof(NullableRect)) return Call(method, new object?[] { a, b });
        var info = MethodInfo(Class, method, 2); ValueParameter<A>(info, 0); ValueParameter<B>(info, 1);
        if (typeof(A) == typeof(float) && typeof(B) == typeof(float) && GuardOffset(info, method) is { First: >= 0, Second: >= 0 } guard
            && *(float*)((byte*)Pointer + guard.First) == *(float*)&a && *(float*)((byte*)Pointer + guard.Second) == *(float*)&b)
        {
            GC.KeepAlive(this);
            return null;
        }
        void** args = stackalloc void*[2]; args[0] = &a; args[1] = &b;
        return Wrap(InvokeRaw(info, args, method));
    }
    internal RuntimeObject? Call<A>(string method, A a, RuntimeObject? b) where A : unmanaged
    {
        if (typeof(A) == typeof(NullableRect)) return Call(method, new object?[] { a, b });
        var info = MethodInfo(Class, method, 2); ValueParameter<A>(info, 0); CheckArgument(IL2CPP.il2cpp_method_get_param(info, 1), b);
        void** args = stackalloc void*[2]; args[0] = &a; args[1] = b == null ? null : (void*)b.Pointer;
        var result = Wrap(InvokeRaw(info, args, method));
        GC.KeepAlive(b);
        return result;
    }
    internal void Set<A>(string property, A value) where A : unmanaged => Call(Accessor(Setters, "set_", property), value);
    internal T Result<T>(string method) where T : unmanaged
    {
        var info = MethodInfo(Class, method, 0); ReturnValue<T>(info);
        return Unbox<T>(InvokeRaw(info, null, method), method);
    }
    internal T Result<T, A>(string method, A a) where T : unmanaged where A : unmanaged
    {
        var info = MethodInfo(Class, method, 1); ReturnValue<T>(info); ValueParameter<A>(info, 0);
        void** args = stackalloc void*[1]; args[0] = &a;
        return Unbox<T>(InvokeRaw(info, args, method), method);
    }
    internal T Result<T, A>(string method, A a, RuntimeObject? b) where T : unmanaged where A : unmanaged
    {
        var info = MethodInfo(Class, method, 2); ReturnValue<T>(info); ValueParameter<A>(info, 0); CheckArgument(IL2CPP.il2cpp_method_get_param(info, 1), b);
        void** args = stackalloc void*[2]; args[0] = &a; args[1] = b == null ? null : (void*)b.Pointer;
        var result = Unbox<T>(InvokeRaw(info, args, method), method);
        GC.KeepAlive(b);
        return result;
    }
    // A reference result as a bare pointer (GetChildAt, get_Item), for a child the caller's object keeps alive this frame.
    internal IntPtr CallPointer<A>(string method, A a) where A : unmanaged
    {
        var info = MethodInfo(Class, method, 1); ReturnReference(info); ValueParameter<A>(info, 0);
        if (typeof(A) == typeof(int) && ComputedKind(info, method) == ChildAt && ChildAtOf(Pointer, *(int*)&a) is var child && child != IntPtr.Zero)
        {
            GC.KeepAlive(this);
            return child;
        }
        void** args = stackalloc void*[1]; args[0] = &a;
        return InvokeRaw(info, args, method);
    }
    // Getters on a bare pointer, for ancestors and children an object the caller holds keeps alive (no wrapper each).
    internal static T GetOf<T>(IntPtr instance, string property) where T : unmanaged
    {
        var info = MethodInfo(ClassOf(Required(instance)), Accessor(Getters, "get_", property), 0); ReturnValue<T>(info);
        var plain = PlainOffset(info, property);
        if (plain >= 0) return *(T*)((byte*)instance + plain);
        if (Computed(info, property, instance, out T computed)) return computed;
        var link = ChainOffset(info, property, out var nextProperty);
        if (link >= 0 && *(IntPtr*)((byte*)instance + link) is var holder && holder != IntPtr.Zero) return GetOf<T>(holder, nextProperty);
        return Unbox<T>(InvokeOn(info, instance, property), property);
    }
    internal static IntPtr GetPointerOf(IntPtr instance, string property)
    {
        var info = MethodInfo(ClassOf(Required(instance)), Accessor(Getters, "get_", property), 0); ReturnReference(info);
        var plain = PlainOffset(info, property);
        if (plain >= 0) return *(IntPtr*)((byte*)instance + plain);
        var link = ChainOffset(info, property, out var nextProperty);
        if (link >= 0 && *(IntPtr*)((byte*)instance + link) is var holder && holder != IntPtr.Zero) return GetPointerOf(holder, nextProperty);
        return InvokeOn(info, instance, property);
    }
    private static IntPtr Required(IntPtr instance) => instance == IntPtr.Zero ? throw new NullReferenceException("Native object required") : instance;
    private static void ValueParameter<A>(IntPtr method, uint index) where A : unmanaged
    {
        var type = IL2CPP.il2cpp_method_get_param(method, index);
        if (type == IntPtr.Zero || IL2CPP.il2cpp_type_is_byref(type)) throw new InvalidCastException("Unsupported by-ref runtime argument");
        CheckValue<A>(IL2CPP.il2cpp_class_from_type(type));
    }
    private static void ReturnValue<T>(IntPtr method) where T : unmanaged =>
        CheckValue<T>(IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_method_get_return_type(method)));
    private static void ReturnReference(IntPtr method)
    {
        if (CheckedReferences.Contains(method)) return;
        var klass = IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_method_get_return_type(method));
        if (klass == IntPtr.Zero || IL2CPP.il2cpp_class_is_valuetype(klass)) throw new InvalidCastException("Expected reference result");
        CheckedReferences.Add(method);
    }
    private IntPtr InvokeRaw(IntPtr method, void** args, string name)
    {
        var instance = IL2CPP.il2cpp_class_is_valuetype(Class) ? IL2CPP.il2cpp_object_unbox(Pointer) : Pointer;
        IntPtr exception = IntPtr.Zero;
        Counted(method);
        var result = IL2CPP.il2cpp_runtime_invoke(method, instance, args, ref exception);
        GC.KeepAlive(this);
        if (exception != IntPtr.Zero) throw new InvalidOperationException("Game UI call failed: " + name);
        return result;
    }
    private static IntPtr InvokeOn(IntPtr method, IntPtr instance, string name)
    {
        IntPtr exception = IntPtr.Zero;
        Counted(method);
        var result = IL2CPP.il2cpp_runtime_invoke(method, instance, null, ref exception);
        if (exception != IntPtr.Zero) throw new InvalidOperationException("Game UI getter failed: " + name);
        return result;
    }
    private static T Unbox<T>(IntPtr result, string name) where T : unmanaged =>
        result == IntPtr.Zero ? throw new InvalidOperationException("Game UI call failed: " + name) : *(T*)IL2CPP.il2cpp_object_unbox(result);
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
            if (method == IntPtr.Zero)
            {
                // Name the class and point at the likely mix-up (a field read as a property, or another overload).
                var target = ClassName(klass) + "." + name + "/" + count;
                var property = name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal) ? name[4..] : null;
                var hint = property != null && HasField(klass, property) ? " (a field of that name exists, read it with Field)"
                    : HasMethod(klass, name, -1) ? " (it exists with a different argument count)" : "";
                Note("missing method " + target + hint);
                throw new MissingMethodException("Native method missing: " + target + hint);
            }
            Methods[key] = method;
            Note("method " + ClassName(klass) + "." + name + "/" + count);
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
                case long id: *(long*)slot = id; break;
                case float f: *(float*)slot = f; break;
                case double d: *(double*)slot = d; break;
                case bool b: *slot = b ? (byte)1 : (byte)0; break;
                case UnityEngine.Color color: *(UnityEngine.Color*)slot = color; break;
                case UnityEngine.Vector2 point: *(UnityEngine.Vector2*)slot = point; break;
                case UnityEngine.Vector3 point: *(UnityEngine.Vector3*)slot = point; break;
                case UnityEngine.Quaternion rotation: *(UnityEngine.Quaternion*)slot = rotation; break;
                case UnityEngine.Rect region: *(UnityEngine.Rect*)slot = region; break;
                case NullableRect grid:
                    // Where the runtime's own fields sit (CheckNullableRect read them for this parameter above).
                    new Span<byte>(slot, _nullableRect.Size).Clear();
                    slot[_nullableRect.Flag] = grid.HasValue ? (byte)1 : (byte)0;
                    *(UnityEngine.Rect*)(slot + _nullableRect.Value) = grid.Value;
                    break;
                default: throw new ArgumentException("Unsupported runtime argument");
            }
        }
        IntPtr exception = IntPtr.Zero;
        Counted(method);
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

    // System.Nullable<UnityEngine.Rect> (FairyGUI.Image.scale9Grid): Il2CppInterop passes Nullable values to
    // il2cpp_runtime_invoke as the raw struct. Read where the runtime's own hasValue and value fields sit, once, and write
    // there (1.7.0-dev.5 assumed hasValue at 0 and value at 4 after the header and was refused in game). Value-type field
    // offsets may count from the boxed object, after its header; the first field shows which. A mismatch names what it saw.
    private static (int Flag, int Value, int Size) _nullableRect = (-1, -1, 0);
    private static void CheckNullableRect(IntPtr klass)
    {
        if (CheckedValues.Contains((klass, typeof(NullableRect)))) return;
        if (klass == IntPtr.Zero || !IL2CPP.il2cpp_class_is_valuetype(klass)
            || IL2CPP.il2cpp_class_get_namespace_(klass) != "System" || IL2CPP.il2cpp_class_get_name_(klass) != "Nullable`1")
            throw new InvalidCastException("Expected System.Nullable<UnityEngine.Rect>: " + ClassName(klass));
        var flag = IL2CPP.il2cpp_class_get_field_from_name(klass, "hasValue");
        var value = IL2CPP.il2cpp_class_get_field_from_name(klass, "value");
        uint alignment = 0;
        var size = IL2CPP.il2cpp_class_value_size(klass, ref alignment);
        string Type(IntPtr field) => field == IntPtr.Zero ? "-" : IL2CPP.il2cpp_type_get_name_(IL2CPP.il2cpp_field_get_type(field)) ?? "?";
        long Offset(IntPtr field) => field == IntPtr.Zero ? -1 : IL2CPP.il2cpp_field_get_offset(field);
        long flagAt = Offset(flag), valueAt = Offset(value);
        var header = Math.Min(flagAt, valueAt) >= 2 * IntPtr.Size ? 2 * IntPtr.Size : 0;
        flagAt -= header; valueAt -= header;
        var seen = $"size {size}, hasValue {Type(flag)} at {flagAt}, value {Type(value)} at {valueAt}, header {header}";
        if (Type(flag) != "System.Boolean" || Type(value) != "UnityEngine.Rect" || size > 32 || flagAt < 0 || valueAt < 0
            || flagAt + 1 > size || valueAt + 16 > size || flagAt >= valueAt && flagAt < valueAt + 16)
            throw new InvalidCastException("Runtime value mismatch: System.Nullable<UnityEngine.Rect>: " + seen);
        _nullableRect = ((int)flagAt, (int)valueAt, size);
        Note("layout System.Nullable<UnityEngine.Rect> " + seen);
        CheckedValues.Add((klass, typeof(NullableRect)));
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
            case long: CheckValue<long>(klass); return; // Player IDs (BattleLogic.RequestGetHeroInfoC2S, GetPlayerDataById)
            case float: CheckValue<float>(klass); return;
            case double: CheckValue<double>(klass); return;
            case bool: CheckValue<bool>(klass); return;
            case UnityEngine.Color: CheckValue<UnityEngine.Color>(klass); return;
            case UnityEngine.Vector2: CheckValue<UnityEngine.Vector2>(klass); return;
            case UnityEngine.Vector3: CheckValue<UnityEngine.Vector3>(klass); return;
            case UnityEngine.Quaternion: CheckValue<UnityEngine.Quaternion>(klass); return;
            case UnityEngine.Rect: CheckValue<UnityEngine.Rect>(klass); return; // FairyGUI.NTexture(NTexture, Rect, bool) atlas sprites (SmoothShape)
            case NullableRect: CheckNullableRect(klass); return; // FairyGUI.Image.scale9Grid (SmoothShape)
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

// A System.Nullable<UnityEngine.Rect> argument. Only its values matter: RuntimeObject writes them where the runtime's own
// fields sit (CheckNullableRect), never this struct's managed layout.
internal struct NullableRect
{
    internal bool HasValue;
    internal UnityEngine.Rect Value;
    internal NullableRect(UnityEngine.Rect value) { HasValue = true; Value = value; }
}
