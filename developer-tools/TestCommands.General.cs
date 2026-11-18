using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static readonly string[] GeneralSupported = {
        "typography", "relic-background", "character-guide", "character-names", "probability-ui", "ko-minimum",
        "encounter-recommendation", "fight-readiness", "runtime-arguments", "runtime-getters", "button-pivots", "surfaces",
        "input-attention", "pvp-safety", "matching-event-audit", "auto-thanks", "shushu-shield", "ping-focus", "ping-popups", "battle-status", "remaining-hp-projection", "startup-notice"
    };

    private static bool TryGeneral(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        switch (name) {
            case "typography": Typography(context, stage, pin); break;
            case "relic-background": RelicBackground(context, stage, pin); break;
            case "character-guide": PrepSkillInfo(context, stage, pin); break;
            case "character-names": CharacterNames(context, stage, pin); break;
            case "fight-readiness": FightReadiness(context, stage, pin); break;
            case "runtime-arguments": RuntimeArguments(context, stage, pin); break;
            case "runtime-getters": RuntimeGetters(context, stage, pin); break;
            case "probability-ui": ProbabilityUi(context, stage, pin); break;
            case "encounter-recommendation": EncounterRecommendation(context, stage, pin); break;
            case "ko-minimum": KoDiceTarget(context, stage, pin); break;
            case "button-pivots": ButtonPivots(context); break;
            case "surfaces": Surfaces(context, options, pin); break;
            case "input-attention": InputAttention(context); break;
            case "pvp-safety": PvpSafety(context, stage, pin); break;
            case "matching-event-audit": MatchingEventAudit(context); break;
            case "auto-thanks": AutoThanks(context, stage, pin); break;
            case "shushu-shield": ShushuShield(context, stage, pin); break;
            case "ping-focus": PingFocus(context, stage, pin); break;
            case "ping-popups": PingPopups(context, stage, pin); break;
            case "battle-status": BattleStatus(context, stage, pin); break;
            case "remaining-hp-projection": RemainingHpProjection(context, stage, pin); break;
            case "startup-notice": StartupNotice(context, options, pin); break;
            default: return false;
        }
        return true;
    }

    private static string Derive(string source, string pattern, string reason)
    {
        var match = Regex.Match(source, pattern);
        Check(match.Success && match.Length != 0, reason);
        return match.Value;
    }

    private static string GeneralWithoutNamespace(string source) => source.Replace("namespace BetterAstralParty;", "", StringComparison.Ordinal);
    private static string WithoutImports(string source) => Regex.Replace(GeneralWithoutNamespace(source), @"(?m)^using [^;]+;\r?\n", "");

    private static void RunGeneralFixture(DevContext context, string stage, string code, string invoke, Action<string> pin,
        string? rejection = null, bool allowUnsafe = false)
    {
        Directory.CreateDirectory(stage);
        var source = Path.Combine(stage, "GeneralFixture.cs");
        var project = Path.Combine(stage, "GeneralFixture.csproj");
        File.WriteAllText(source, "#nullable enable\n#pragma warning disable CS8618, CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS0649, CS0169, CS0414, CS9113\n"
            + code + "\ninternal static class GeneralFixtureEntry { private static int Main() { try { " + invoke + "; return 0; } catch (System.Exception error) { System.Console.Error.WriteLine(error); return 1; } } }\n", new UTF8Encoding(false));
        var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net8.0"),
                new XElement("EnableDefaultCompileItems", "false"), new XElement("AssemblyName", "GeneralFixture"),
                new XElement("StartupObject", "GeneralFixtureEntry"), new XElement("AllowUnsafeBlocks", allowUnsafe ? "true" : "false")),
            new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", source))));
        using (var file = new FileStream(project, FileMode.CreateNew, FileAccess.Write, FileShare.None)) xml.Save(file);
        pin(source); pin(project);
        var image = BuildProject(context, project, stage, pin);
        var log = Path.Combine(stage, "runtime.log");
        var exit = context.Run(Dotnet(context), new[] { image }, env: EnvironmentFor(stage), logPath: log);
        if (rejection == null) Ensure(exit, "Production-source focused fixture");
        else Check(exit != 0 && File.ReadAllText(log).Contains(rejection, StringComparison.Ordinal), "Regression control rejected for the expected reason: " + rejection);
        context.WriteJsonCreateNew(Path.Combine(stage, "derived-source.json"), new { derivedSourceSha256 = context.Hash(source), invocation = invoke,
            nativeSubstitutesOnly = true, expectedRejection = rejection, sourceFilesUnmodified = true });
    }

    private static void RuntimeArguments(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/RuntimeObject.cs");
        var check = Derive(source, @"(?s)    private static void CheckArgument\(.*?\r?\n    \}", "Production argument checker boundary");
        var invoke = Derive(source, @"(?s)    private static RuntimeObject\? Invoke\(.*?\r?\n    \}", "Production native invocation boundary");
        string Code(string argument, string invocation) => GeneralRuntimeArguments0.Replace("__NAMESPACE__", "BapArgumentFixture", StringComparison.Ordinal)
            .Replace("__CHECK_ARGUMENT__", argument, StringComparison.Ordinal).Replace("__INVOKE__", invocation, StringComparison.Ordinal);
        RunGeneralFixture(context, Path.Combine(stage, "current"), Code(check, invoke), "BapArgumentFixture.RuntimeObject.Run()", pin, allowUnsafe: true);
        var old = Regex.Replace(check, "case string when.*?: return;", "case string when IL2CPP.il2cpp_type_get_name_(type) == \"System.String\": return;", RegexOptions.Singleline);
        Check(old != check, "Object-string regression anchor exists");
        RunGeneralFixture(context, Path.Combine(stage, "old-object-string"), Code(old, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsObjectString()) throw new System.Exception(\"Old rejection not reproduced\")", pin, allowUnsafe: true);
        var absentCheck = Regex.Replace(check, @"case UnityEngine\.Quaternion:.*?return;", "", RegexOptions.Singleline);
        var absentInvoke = Regex.Replace(invoke, @"case UnityEngine\.Quaternion rotation:.*?break;", "", RegexOptions.Singleline);
        Check(absentCheck != check && absentInvoke != invoke, "Quaternion guard and marshaling regression anchors exist");
        RunGeneralFixture(context, Path.Combine(stage, "absent-quaternion"), Code(absentCheck, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsQuaternion() || BapArgumentFixture.RuntimeObject.InvokesQuaternion()) throw new System.Exception(\"Missing Quaternion guard accepted\")", pin, allowUnsafe: true);
        RunGeneralFixture(context, Path.Combine(stage, "absent-quaternion-marshaling"), Code(check, absentInvoke),
            "if (BapArgumentFixture.RuntimeObject.InvokesQuaternion()) throw new System.Exception(\"Missing Quaternion marshaling accepted\")", pin, allowUnsafe: true);
        var absentDoubleCheck = Regex.Replace(check, @"case double:.*?return;", "", RegexOptions.Singleline);
        var absentDoubleInvoke = Regex.Replace(invoke, @"case double d:.*?break;", "", RegexOptions.Singleline);
        Check(absentDoubleCheck != check && absentDoubleInvoke != invoke, "Double guard and marshaling regression anchors exist");
        RunGeneralFixture(context, Path.Combine(stage, "absent-double"), Code(absentDoubleCheck, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsDouble()) throw new System.Exception(\"Missing Double guard accepted\")", pin, allowUnsafe: true);
        RunGeneralFixture(context, Path.Combine(stage, "absent-double-marshaling"), Code(check, absentDoubleInvoke),
            "if (BapArgumentFixture.RuntimeObject.InvokesDouble()) throw new System.Exception(\"Missing Double marshaling accepted\")", pin, allowUnsafe: true);
        var absentLongCheck = Regex.Replace(check, @"case long:.*?return;", "", RegexOptions.Singleline);
        var absentLongInvoke = Regex.Replace(invoke, @"case long id:.*?break;", "", RegexOptions.Singleline);
        Check(absentLongCheck != check && absentLongInvoke != invoke, "Int64 guard and marshaling regression anchors exist");
        RunGeneralFixture(context, Path.Combine(stage, "absent-long"), Code(absentLongCheck, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsLong()) throw new System.Exception(\"Missing Int64 guard accepted\")", pin, allowUnsafe: true);
        RunGeneralFixture(context, Path.Combine(stage, "absent-long-marshaling"), Code(check, absentLongInvoke),
            "if (BapArgumentFixture.RuntimeObject.InvokesLong()) throw new System.Exception(\"Missing Int64 marshaling accepted\")", pin, allowUnsafe: true);
        var absentRectCheck = Regex.Replace(check, @"case UnityEngine\.Rect:.*?return;[^\r\n]*", "", RegexOptions.Singleline);
        var absentRectInvoke = Regex.Replace(invoke, @"case UnityEngine\.Rect region:.*?break;", "", RegexOptions.Singleline);
        Check(absentRectCheck != check && absentRectInvoke != invoke, "Rect guard and marshaling regression anchors exist");
        RunGeneralFixture(context, Path.Combine(stage, "absent-rect"), Code(absentRectCheck, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsRect()) throw new System.Exception(\"Missing Rect guard accepted\")", pin, allowUnsafe: true);
        RunGeneralFixture(context, Path.Combine(stage, "absent-rect-marshaling"), Code(check, absentRectInvoke),
            "if (BapArgumentFixture.RuntimeObject.InvokesRect()) throw new System.Exception(\"Missing Rect marshaling accepted\")", pin, allowUnsafe: true);
        var absentGridCheck = Regex.Replace(check, @"case NullableRect:.*?return;[^\r\n]*", "", RegexOptions.Singleline);
        var absentGridInvoke = Regex.Replace(invoke, @"case NullableRect grid:.*?break;", "", RegexOptions.Singleline);
        Check(absentGridCheck != check && absentGridInvoke != invoke, "Nullable<Rect> guard and marshaling regression anchors exist");
        RunGeneralFixture(context, Path.Combine(stage, "absent-grid"), Code(absentGridCheck, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsNullableRect()) throw new System.Exception(\"Missing Nullable<Rect> guard accepted\")", pin, allowUnsafe: true);
        RunGeneralFixture(context, Path.Combine(stage, "absent-grid-marshaling"), Code(check, absentGridInvoke),
            "if (BapArgumentFixture.RuntimeObject.InvokesNullableRect()) throw new System.Exception(\"Missing Nullable<Rect> marshaling accepted\")", pin, allowUnsafe: true);
        // The real layout check, against metadata shaped like the runtime's own Nullable<Rect> and against mismatches.
        var layout = Derive(source, @"(?s)    private static void CheckNullableRect\(.*?\r?\n    \}", "Production Nullable<Rect> layout check");
        RunGeneralFixture(context, Path.Combine(stage, "nullable-rect-layout"), GeneralNullableRectLayout0.Replace("__CHECK__", layout, StringComparison.Ordinal),
            "BapNullableRect.RuntimeObject.Run()", pin, allowUnsafe: true);
        // The allocation-free forms (second performance round, 2026-11-13): calls with one or two unmanaged values, or a value
        // and an object, bind there instead of the params form, pass each value's own bytes, keep the exact type guards and
        // send Nullable<Rect> back to the params form; Result<T> unboxes a value result without a wrapper.
        var generic = Derive(source, @"(?s)    internal RuntimeObject\? Call<A>\(string method, A a\) where A : unmanaged.*?    private static T Unbox<T>\(.*?;\r?\n",
            "Production allocation-free call forms");
        RunGeneralFixture(context, Path.Combine(stage, "generic-calls"), GeneralRuntimeGeneric0.Replace("__GENERIC__", generic, StringComparison.Ordinal),
            "BapGenericFixture.RuntimeObject.Run()", pin, allowUnsafe: true);
    }

    private static void RuntimeGetters(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/RuntimeObject.cs");
        var code = GeneralRuntimeGetters0
            .Replace("__ACCESSOR__", Derive(source, @"(?s)    private static string Accessor\(.*?\r?\n    \}", "Production accessor boundary"), StringComparison.Ordinal)
            .Replace("__GETTER__", Derive(source, @"(?s)    internal T Get<T>\(.*?\r?\n    \}", "Production typed getter boundary"), StringComparison.Ordinal)
            .Replace("__FIELD_GETTER__", Derive(source, @"(?s)    internal T Field<T>\(.*?\r?\n    \}", "Production typed field getter boundary"), StringComparison.Ordinal);
        RunGeneralFixture(context, stage, code, "GetterProbe.Run()", pin, allowUnsafe: true);
        PlainGetterBodies(context, source);
        // Shared wrappers (2026-11-17): only short weak references, one table per thread, boxed values and strings fresh, and
        // a dead entry refilled with a new wrapper rather than handing out one whose handle may be freed.
        Contains(source, new[] {
            "private static RuntimeObject? Wrap(IntPtr pointer) => pointer == IntPtr.Zero ? null : Shared(pointer);",
            "[ThreadStatic] private static Dictionary<IntPtr, WeakReference<RuntimeObject>>? _shared;",
            "if (klass == _stringClass || IL2CPP.il2cpp_class_is_valuetype(klass)) return new RuntimeObject(pointer);",
            "if (weak.TryGetTarget(out var known)) return known;",
            "table[pointer] = new WeakReference<RuntimeObject>(made);",
            "~RuntimeObject() => IL2CPP.il2cpp_gchandle_free(_handle);",
        }, "Shared wrappers hold weak references only");
        Check(!Regex.IsMatch(source, @"WeakReference<RuntimeObject>\([^)]*true\)"), "Shared wrappers never track resurrection");
    }

    // RuntimeObject.PlainGetters reads these getters as their fields and GameUi.Visible reads onStage and internalVisible(2)
    // as their bodies do (2026-11-17): each listed getter must be exactly `ldarg.0; ldfld <its own field>; ret`, the
    // composite bodies must keep their shape, and nothing may override what Visible reads.
    private static void PlainGetterBodies(DevContext context, string source)
    {
        using var native = Native(context);
        using var corlib = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(context.Root, ".research", "extracted", "mscorlib.dll.dll"));
        var table = source[source.IndexOf("PlainGetters =", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("};", StringComparison.Ordinal)];
        var rows = Regex.Matches(table, "\\(\"([\\w.`]+)\", \"(\\w+)\", \"([<>\\w]+)\"\\)");
        Check(rows.Count == Regex.Matches(table, "\\(\"").Count && rows.Count >= 25, "Plain getter table read whole: " + rows.Count);
        foreach (Match row in rows)
        {
            var (type, property, field) = (row.Groups[1].Value, row.Groups[2].Value, row.Groups[3].Value);
            var module = type.StartsWith("System.", StringComparison.Ordinal) ? corlib.MainModule : native.MainModule;
            var getter = module.GetType(type)?.Methods.SingleOrDefault(method => method.Name == "get_" + property && !method.HasParameters && !method.IsStatic);
            var body = getter?.Body.Instructions.Where(instruction => instruction.OpCode.Code != Mono.Cecil.Cil.Code.Nop).ToArray();
            // The field is the getter's own or a base class's (ReadOnlyReactiveProperty reads ReactiveProperty._value); the runtime
            // finds it by name through the parents (il2cpp_class_get_field_from_name).
            bool Owns(Mono.Cecil.TypeReference declaring)
            {
                var owner = declaring.Resolve()?.FullName;
                for (var at = getter?.DeclaringType; at != null; at = at.BaseType?.Resolve())
                    if (at.FullName == owner) return true;
                return false;
            }
            Check(body is { Length: 3 } && body[0].OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_0 && body[1].OpCode.Code == Mono.Cecil.Cil.Code.Ldfld
                && body[1].Operand is Mono.Cecil.FieldReference read && read.Name == field && Owns(read.DeclaringType)
                && body[2].OpCode.Code == Mono.Cecil.Cil.Code.Ret, "Plain getter " + type + "." + property + " returns its field " + field);
        }
        // Chained getters: `ldarg.0; ldfld F; callvirt get_P; ret`.
        var chains = source[source.IndexOf("ChainGetters =", StringComparison.Ordinal)..];
        chains = chains[..chains.IndexOf("};", StringComparison.Ordinal)];
        var links = Regex.Matches(chains, "\\(\"([\\w.`]+)\", \"(\\w+)\", \"([<>\\w]+)\", \"(\\w+)\"\\)");
        Check(links.Count == Regex.Matches(chains, "\\(\"").Count && links.Count >= 4, "Chained getter table read whole: " + links.Count);
        foreach (Match link in links)
        {
            var (type, property, field, next) = (link.Groups[1].Value, link.Groups[2].Value, link.Groups[3].Value, link.Groups[4].Value);
            var getter = native.MainModule.GetType(type)?.Methods.SingleOrDefault(method => method.Name == "get_" + property && !method.HasParameters && !method.IsStatic);
            var body = getter?.Body.Instructions.Where(instruction => instruction.OpCode.Code != Mono.Cecil.Cil.Code.Nop).ToArray();
            // The first link reads the field, or calls a plain getter of the same object that returns it.
            bool Holds(Mono.Cecil.Cil.Instruction instruction) => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld
                && instruction.Operand is Mono.Cecil.FieldReference held && held.Name == field
                || instruction.OpCode.Code == Mono.Cecil.Cil.Code.Call && instruction.Operand is Mono.Cecil.MethodReference plain && plain.Resolve() is { } hop
                && hop.Body.Instructions.Where(step => step.OpCode.Code != Mono.Cecil.Cil.Code.Nop).ToArray() is { Length: 3 } steps
                && steps[0].OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_0 && steps[1].Operand is Mono.Cecil.FieldReference backing && backing.Name == field
                && steps[2].OpCode.Code == Mono.Cecil.Cil.Code.Ret;
            Check(body is { Length: 4 } && body[0].OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_0 && Holds(body[1])
                && body[2].OpCode.Code is Mono.Cecil.Cil.Code.Callvirt or Mono.Cecil.Cil.Code.Call && body[2].Operand is Mono.Cecil.MethodReference called
                && called.Name == "get_" + next && !called.HasParameters && body[3].OpCode.Code == Mono.Cecil.Cil.Code.Ret,
                "Chained getter " + type + "." + property + " returns " + field + "." + next);
        }
        // Guarded setters: their first comparison against the listed fields returns at once when the value already stands.
        var gobject = native.MainModule.GetType("FairyGUI.GObject");
        var display = native.MainModule.GetType("FairyGUI.DisplayObject");
        Mono.Cecil.MethodDefinition Method(string name, int count) => gobject.Methods.Single(method => method.Name == name && method.Parameters.Count == count);
        Mono.Cecil.Cil.Instruction[] Body(Mono.Cecil.MethodDefinition method) =>
            method.Body.Instructions.Where(instruction => instruction.OpCode.Code != Mono.Cecil.Cil.Code.Nop).ToArray();
        bool Reads(Mono.Cecil.Cil.Instruction instruction, string field) => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld
            && instruction.Operand is Mono.Cecil.FieldReference read && read.Name == field && read.DeclaringType.FullName == "FairyGUI.GObject";
        bool Branch(Mono.Cecil.Cil.Instruction instruction, bool equal) => (equal
            ? instruction.OpCode.Code is Mono.Cecil.Cil.Code.Beq or Mono.Cecil.Cil.Code.Beq_S
            : instruction.OpCode.Code is Mono.Cecil.Cil.Code.Bne_Un or Mono.Cecil.Cil.Code.Bne_Un_S) && instruction.Operand is Mono.Cecil.Cil.Instruction;
        bool Returns(Mono.Cecil.Cil.Instruction branch) => ((Mono.Cecil.Cil.Instruction)branch.Operand).OpCode.Code == Mono.Cecil.Cil.Code.Ret;
        foreach (var (setter, field) in new[] { ("set_visible", "_visible"), ("set_touchable", "_touchable"), ("set_grayed", "_grayed") })
        {
            var body = Body(Method(setter, 1));
            Check(body[0].OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_0 && Reads(body[1], field) && body[2].OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_1
                && Branch(body[3], true) && Returns(body[3]), "Guarded setter " + setter + " returns when " + field + " already holds the value");
        }
        bool Guard(Mono.Cecil.Cil.Instruction[] body, params string[] fields)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                var at = i * 4;
                var argument = body[at + 2].OpCode.Code switch
                {
                    Mono.Cecil.Cil.Code.Ldarg_1 => 0, Mono.Cecil.Cil.Code.Ldarg_2 => 1, Mono.Cecil.Cil.Code.Ldarg_3 => 2, _ => -1,
                };
                if (body[at].OpCode.Code != Mono.Cecil.Cil.Code.Ldarg_0 || !Reads(body[at + 1], fields[i]) || argument != i) return false;
                var last = i == fields.Length - 1;
                if (!Branch(body[at + 3], last) || last && !Returns(body[at + 3])) return false;
            }
            return true;
        }
        Check(Guard(Body(Method("SetScale", 2)), "_scaleX", "_scaleY"), "Guarded SetScale returns when both scales already stand");
        Check(Guard(Body(Method("SetSize", 3)), "_rawWidth", "_rawHeight") && Pattern2(Method("SetSize", 2), "GObject::SetSize(System.Single,System.Single,System.Boolean)"),
            "Guarded SetSize returns when both raw sizes already stand");
        Check(Guard(Body(Method("SetPosition", 3)), "_x", "_y", "_z") && Pattern2(Method("SetXY", 2), "GObject::_z") && Pattern2(Method("SetXY", 2), "GObject::SetPosition("),
            "Guarded SetXY (SetPosition with its own z) returns when x and y already stand");
        bool Pattern2(Mono.Cecil.MethodDefinition method, string text) => Il(method).Contains(text, StringComparison.Ordinal);
        string Getter(Mono.Cecil.TypeDefinition type, string name) => Il(type.Methods.Single(method => method.Name == name && !method.HasParameters));
        Pattern(Getter(gobject, "get_onStage"), @"GObject::get_displayObject\(\)[\s\S]*DisplayObject::get_stage\(\)", "onStage reads the display object's stage");
        Pattern(Getter(display, "get_stage"), @"DisplayObject::get_topmost\(\)[\s\S]*isinst FairyGUI.Stage", "stage is the topmost container as a Stage");
        Pattern(Getter(display, "get_topmost"), @"DisplayObject::get_parent\(\)[\s\S]*DisplayObject::get_parent\(\)", "topmost follows parent to the top");
        Pattern(Getter(gobject, "get_internalVisible"), @"GObject::_internalVisible[\s\S]*GObject::get_group\(\)[\s\S]*GObject::get_internalVisible\(\)", "internalVisible: own flag, then the group's");
        Pattern(Getter(gobject, "get_internalVisible2"), @"GObject::_visible[\s\S]*GObject::get_group\(\)[\s\S]*GObject::get_internalVisible2\(\)", "internalVisible2: own visible, then the group's");
        var component = native.MainModule.GetType("FairyGUI.GComponent");
        Pattern(Getter(component, "get_numChildren"), @"GComponent::_children[\s\S]*List`1<FairyGUI.GObject>::get_Count\(\)", "numChildren is the child list's count");
        Pattern(Il(component.Methods.Single(method => method.Name == "GetChildAt" && method.Parameters.Count == 1)),
            @"GComponent::get_numChildren\(\)[\s\S]*GComponent::_children[\s\S]*List`1<FairyGUI.GObject>::get_Item\(System.Int32\)", "GetChildAt is the child list's item, in range");
        Pattern(Getter(gobject, "get_asCom"), @"isinst FairyGUI.GComponent", "asCom is the object as a GComponent");
        Pattern(Il(component.Methods.Single(method => method.Name == "GetChildIndex" && method.Parameters.Count == 1)),
            @"GComponent::_children[\s\S]*List`1<FairyGUI.GObject>::IndexOf\(", "GetChildIndex is the child list's IndexOf");
        foreach (var name in new[] { "FairyGUI.GObject", "FairyGUI.EventDispatcher" })
            Check(!native.MainModule.GetType(name).Methods.Any(method => method.Name is "Equals" or "GetHashCode"), "Child lists compare FairyGUI objects by reference: " + name);
        var list = corlib.MainModule.GetType("System.Collections.Generic.List`1");
        Pattern(Il(list.Methods.Single(method => method.Name == "get_Item")), @"List`1<T>::_size[\s\S]*List`1<T>::_items[\s\S]*ldelem", "List item is _items[index] below _size");
        bool Derives(Mono.Cecil.TypeDefinition? type, string name)
        {
            for (; type != null; type = Resolve(type.BaseType)) if (type.FullName == name) return true;
            return false;
        }
        static Mono.Cecil.TypeDefinition? Resolve(Mono.Cecil.TypeReference? type)
        {
            try { return type?.Resolve(); } catch { return null; }
        }
        foreach (var type in native.MainModule.GetTypes())
            foreach (var method in type.Methods)
            {
                var gobjectGetter = method.Name is "get_onStage" or "get_internalVisible" or "get_internalVisible2" or "get_displayObject" or "get_group" or "get_parent";
                var displayGetter = method.Name is "get_stage" or "get_topmost" or "get_parent";
                Check(!(gobjectGetter && type != gobject && Derives(type, "FairyGUI.GObject") || displayGetter && type != display && Derives(type, "FairyGUI.DisplayObject")),
                    "Nothing overrides what GameUi.Visible reads: " + type.FullName + "." + method.Name);
            }
    }

    private static void FightReadiness(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/GameUi.cs");
        var start = source.IndexOf("        var ui = fight.Get(\"contentPane\");", StringComparison.Ordinal);
        var end = source.IndexOf("        Plugin.Diagnostics.State(\"fightStep\"", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Production fight readiness block");
        RunGeneralFixture(context, stage, GeneralFightReadiness0.Replace("__BLOCK__", source[start..end], StringComparison.Ordinal), "FightReadinessProbe.Run()", pin);
    }

    private static void RelicBackground(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/NativeUi.cs");
        var start = source.IndexOf("    internal sealed class Surface", StringComparison.Ordinal);
        var end = source.IndexOf("    internal static void OutlineText", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Production Surface extraction");
        RunGeneralFixture(context, stage, GeneralRelicBackground0 + source[start..end] + GeneralRelicBackground1, "RelicBackgroundTest.RelicBackgroundCheck.Run()", pin);
        Contains(ReadSource(context, "src/PingDetailsUi.cs"), new[] { "fill: new Color(0f, 0f, 0f, 0.88f)", "NativeUi.InheritText(title, dark: true)",
            "NativeUi.InheritText(description, dark: true)", "_relicBackground?.Refresh();" }, "Relic-only styling and live refresh");
    }

    private static void CharacterNames(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        var profile = native.MainModule.GetType("UI.AccountInfoWindow");
        var body = Il(profile.Methods.Single(method => method.Name == "RefreshStatisticalData"));
        Check(Regex.IsMatch(body, "txt_Hero\\r?\\n[^\\n]*ldstr \"data\"[\\s\\S]*?GetCharacterNickName[\\s\\S]*?GTextField::SetVar"), "Native favorite-character template binding");
        foreach (var name in new[] { "get_templateVars", "SetVar", "FlushVars" }) Check(native.MainModule.GetType("FairyGUI.GTextField").Methods.Any(method => method.Name == name), "Native template API " + name);
        var source = ReadSource(context, "src/CharacterNameUi.cs");
        Check(native.MainModule.GetType("UI.UIStringType").Fields.Single(field => field.Name == "Character").IsLiteral, "Native character string category is a named enum literal");
        Contains(source, new[] { "RuntimeObject.EnumValue(\"UI\", \"UIStringType\", \"Character\")", "\"GetLocal\", nameId, stringType", "\"GetLocal\", nickId, stringType" }, "Character name and epithet use the current native category by name");
        Check(!Regex.IsMatch(source, "\"GetLocal\"[^\\r\\n]*,\\s*[0-9]+\\s*\\)"), "Character localization has no ordinal fallback");
        var methods = new[] { "Replace", "ReadLabel", "WriteLabel", "RestoreAll" }.Select(name =>
            Derive(source, @"(?s)    (?:private|internal) static (?:void|string) " + name + @"\(.*?\r?\n    \}", "Production character-name method " + name));
        var code = GeneralCharacterNames0.Replace("__NAMESPACE__", "BapCharacterNames", StringComparison.Ordinal)
            .Replace("__NAMES__", GeneralWithoutNamespace(ReadSource(context, "src/CharacterNames.cs")), StringComparison.Ordinal)
            .Replace("__METHODS__", String.Join("\n", methods), StringComparison.Ordinal);
        RunGeneralFixture(context, stage, code, "BapCharacterNames.Check.Run()", pin);
    }

    private static void ProbabilityUi(DevContext context, string stage, Action<string> pin)
    {
        var paths = new[] { "src/CombatAdvisor.cs", "src/VisibleCombat.cs", "src/CardAdvisor.cs", "src/EncounterAdvisor.cs", "src/AdviceText.cs", "focus-tests/probability/PresentationCheck.cs" };
        var text = ReadSource(context, "src/ModText.cs");
        var start = text.IndexOf("    internal static bool Korean", StringComparison.Ordinal);
        var end = text.IndexOf("    internal static string AutomaticStatus", StringComparison.Ordinal);
        Check(start >= 0 && end > start && text[start..end].Contains("[\"전투불능\"] = \"KO\"", StringComparison.Ordinal), "Exact production localization boundary");
        var code = GeneralProbability0.Replace("__NS__", "BapProbability", StringComparison.Ordinal) + "\n" + String.Join("\n", paths.Select(path => WithoutImports(ReadSource(context, path))))
            + "\ninternal static class ModText {\n" + text[start..end] + "\n}\n}";
        RunGeneralFixture(context, Path.Combine(stage, "current"), code, "BapProbability.PresentationCheck.Run()", pin);
        var mutants = new[] {
            ("attack-minimum", "Probability(afterKoMax)", "Probability(afterKoMin)", "attack card chooses favorable endpoint"),
            ("defense-last-outcome", "defKoLow = Math.Min(defKoLow, result.Defend.KnockoutChance);", "defKoLow = result.Defend.KnockoutChance;", "own defense shows minimum risk"),
            ("dodge-last-outcome", "dodgeKoLow = Math.Min(dodgeKoLow, result.Dodge.KnockoutChance);", "dodgeKoLow = result.Dodge.KnockoutChance;", "all visible dodge endpoints"),
            ("encounter-self-maximum", "Probability(SelfKoMin)", "Probability(SelfKoMax)", "enemy maximum / own minimum encounter display"),
            ("dodge-on-risk", "defend.KnockoutChance >= 1", "defend.KnockoutChance > 0", "dodge only when defense is a certain KO")
        };
        foreach (var (name, from, to, reason) in mutants) {
            Check(code.Contains(from, StringComparison.Ordinal), "Probability mutation anchor " + name);
            RunGeneralFixture(context, Path.Combine(stage, name), code.Replace(from, to, StringComparison.Ordinal), "BapProbability.PresentationCheck.Run()", pin, reason);
        }
    }

    private static void EncounterRecommendation(DevContext context, string stage, Action<string> pin)
    {
        var ui = ReadSource(context, "src/GameUi.cs");
        var start = ui.IndexOf("    private static void EnsureBadges(", StringComparison.Ordinal); var end = ui.IndexOf("    private static void UpdateHover()", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Exact production encounter UI boundary");
        var paths = new[] { "src/CombatAdvisor.cs", "src/VisibleCombat.cs", "src/CardAdvisor.cs", "src/EncounterAdvisor.cs", "src/AdviceBadge.cs", "src/AdviceText.cs" };
        var code = GeneralEncounter0.Replace("__NS__", "BapEncounter", StringComparison.Ordinal) + "\n" + String.Join("\n", paths.Select(path => WithoutImports(ReadSource(context, path))))
            + "\n" + ReadSource(context, "focus-tests/encounter-recommendation/Checks.cs").Replace("__ENCOUNTER_UI__", ui[start..end], StringComparison.Ordinal) + "\n}";
        RunGeneralFixture(context, Path.Combine(stage, "current"), code, "BapEncounter.EncounterRecommendationCheck.Policy(); BapEncounter.EncounterRecommendationCheck.Badges()", pin);
        var policy = ReadSource(context, "src/EncounterAdvisor.cs");
        var policyStart = policy.IndexOf("        double enemyMin = ", StringComparison.Ordinal); var policyEnd = policy.LastIndexOf("\n    }", StringComparison.Ordinal);
        Check(policyStart >= 0 && policyEnd > policyStart, "Production encounter recommendation policy boundary");
        var mutants = new[] {
            ("old-consensus", policy[policyStart..policyEnd].TrimEnd('\r'), GeneralEncounter1, "Policy", "single favorable paired recommendation"),
            ("attack-on-tie", "bestDamageGain > epsilon;", "bestDamageGain >= -epsilon;", "Policy", "single favorable paired recommendation"),
            ("two-thumbs", "advice.Attack == false, scale", "advice.Attack.HasValue, scale", "Badges", "actual encounter/badge path shows one")
        };
        foreach (var (name, from, to, method, reason) in mutants) {
            Check(code.Contains(from, StringComparison.Ordinal), "Encounter mutation anchor " + name);
            RunGeneralFixture(context, Path.Combine(stage, name), code.Replace(from, to, StringComparison.Ordinal), "BapEncounter.EncounterRecommendationCheck." + method + "()", pin, reason);
        }
    }

    private static void KoDiceTarget(DevContext context, string stage, Action<string> pin)
    {
        var paths = new[] { "src/CombatAdvisor.cs", "src/PublicCombatEffects.cs", "src/CombatBuffAudit.cs", "src/ShootingStar.cs", "src/VisibleCombat.cs", "src/CardAdvisor.cs", "src/CardKoMinimum.cs", "src/CardDiceLayout.cs",
            "src/EncounterAdvisor.cs", "src/AdviceText.cs", "self-test/CombatEffectsCheck.cs", "self-test/CombatSimulationCheck.cs", "self-test/CombatLogReplayCheck.cs" };
        var publicSource = ReadSource(context, "self-test/PublicCombatEffectsCheck.cs");
        var fixture = Regex.Match(publicSource, @"(?s)namespace BetterAstralParty\s*\{\s*(internal sealed class RuntimeObject.*?)\r?\n\}\r?\n\r?\ninternal static class PublicCombatEffectsCheck").Groups[1].Value;
        var at = publicSource.IndexOf("internal static class PublicCombatEffectsCheck", StringComparison.Ordinal);
        var schema = Derive(ReadSource(context, "self-test/Program.cs"), @"(?s)var shadow = CardAdvisor\.FromConfig.*?(?=var protectionOnly =)", "Exact card-schema assertion boundary");
        Check(fixture.Length != 0 && at >= 0 && schema.Contains("10009", StringComparison.Ordinal) && schema.Contains("10010", StringComparison.Ordinal), "Focused combat native substitutes");
        var code = GeneralKoMinimum0.Replace("__NS__", "BapKoMinimum", StringComparison.Ordinal) + "\n" + String.Join("\n", paths.Select(path => WithoutImports(ReadSource(context, path))))
            + "\n" + fixture + "\n" + publicSource[at..] + "\n" + GeneralKoMinimum1.Replace("__SCHEMA__", schema, StringComparison.Ordinal);
        RunGeneralFixture(context, Path.Combine(stage, "current"), code, "BapKoMinimum.FocusedCombat.Run()", pin);
        var grant = "id == 10009 && effectType == 1 && parameters[2] == 10010";
        var mutants = new[] {
            ("vacuous-endpoint", "if (row == 1 && pair.Value.DefenseDie == (survival ? defenseDice.Min : defenseDice.Max)) return (0, \"\", \"—\");", "/* removed vacuous endpoint */", "Thresholds", "vacuous defender bound"),
            ("missing-final-damage", "if (damage is { } bounds)", "if (pair == null) return \"계산 정보 부족\"; if (damage is { } bounds)", "Thresholds", "final arithmetic known without pre-roll support"),
            ("missing-final-defense", "if (unconditional) return (0, \"\", \"—\");", "if (pair == null) return (0, \"\", \"?\"); if (unconditional) return (0, \"\", \"—\");", "Thresholds", "known final condition-free defense"),
            ("unknown-grants", grant, "effectType == 1 && parameters[2] == 10010", "Schema", "later-card semantic mapping not generalized"),
            ("final-atk-as-additive", "FinalPercent = finalBoost ? parameters[2] : 0", "FinalPercent = 0", "Schema", "final-ATK percent")
        };
        foreach (var (name, from, to, method, reason) in mutants) {
            Check(code.Contains(from, StringComparison.Ordinal), "KO mutation anchor " + name);
            RunGeneralFixture(context, Path.Combine(stage, name), code.Replace(from, to, StringComparison.Ordinal), "BapKoMinimum.FocusedCombat." + method + "()", pin, reason);
        }
    }

    private static string Il(MethodDefinition method) => method.HasBody ? String.Join("\n", method.Body.Instructions.Select(instruction => instruction.ToString())) : "";
}
