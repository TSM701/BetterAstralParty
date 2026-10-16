using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void NativeInputSelection(DevContext context, string stage, Action<string> pin)
    {
        var historical = new DevContext(context.Root, context.DotnetRoot);
        Check(!historical.NativeAssemblyExplicit && historical.NativeAssemblyPath == Path.Combine(context.Root, ".research", "extracted", "AstralParty.Runtime.dll.dll"), "Default native metadata preserves the historical snapshot path");
        var directory = Path.Combine(stage, "native-input-selection"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "current-synthetic.dll");
        using (var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("NativeInputSelectionProbe", new Version(1, 0)), "NativeInputSelectionProbe", ModuleKind.Dll)) {
            NativeAsyncSelection(assembly);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); assembly.Write(file);
        }
        var selected = context.WithNativeAssembly(path);
        Check(selected.NativeAssemblyExplicit && selected.NativeAssemblyPath == path && selected.Root == context.Root && selected.DotnetRoot == context.DotnetRoot, "Explicit native selection is immutable and preserves project/SDK identity");
        Check(context.NativeAssemblyPath != selected.NativeAssemblyPath, "Selecting a native input does not mutate the caller context");
        using (var held = selected.OpenNativeAssemblyInput()) {
            var blocked = false;
            try { using var writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.Read); }
            catch (IOException) { blocked = true; }
            Check(blocked, "Retained native input lease rejects concurrent metadata writes");
        }
        pin(path);
        using (var assembly = Native(selected)) Check(assembly.Name.Name == "NativeInputSelectionProbe", "Native ABI reader consumes the exact explicit assembly rather than the historical fallback");
        var missing = context.WithNativeAssembly(Path.Combine(directory, "missing.dll")); var rejected = false;
        try { using var assembly = Native(missing); } catch (FileNotFoundException) { rejected = true; }
        Check(rejected, "Missing explicit native input fails without historical fallback");
        var empty = Path.Combine(directory, "empty.dll");
        using (var file = new FileStream(empty, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        rejected = false;
        try { using var file = context.WithNativeAssembly(empty).OpenNativeAssemblyInput(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Empty explicit native input fails without historical fallback");
        rejected = false;
        try { Options(new[] { "--native-assembly", path, "--native-assembly", missing.NativeAssemblyPath }); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Duplicate native selection is rejected rather than choosing a fallback or the last option");
        var invalid = Path.Combine(directory, "invalid.dll");
        using (var file = new FileStream(invalid, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.WriteByte(0);
        pin(invalid); rejected = false;
        try { using var assembly = Native(context.WithNativeAssembly(invalid)); } catch (BadImageFormatException) { rejected = true; }
        Check(rejected, "Malformed explicit native input fails without historical fallback");
        rejected = false;
        try { context.WithNativeAssembly(" "); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Blank explicit native selection is not treated as an omitted option");
        context.WriteJsonCreateNew(Path.Combine(directory, "native-input-selection.json"), new { passed = true, syntheticOnly = true,
            selected = new { path, sha256 = context.Hash(path) }, defaultPath = historical.NativeAssemblyPath,
            fallbackOnInvalidOverride = false, nativeAssemblyExecuted = false });
    }
    private static void NativeAsyncSelection(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        foreach (var ordinal in new[] { 2, 900 }) {
            var owner = new TypeDefinition("Fixture", "Owner" + ordinal, TypeAttributes.Public, module.TypeSystem.Object); module.Types.Add(owner);
            var source = new MethodDefinition("Execute", MethodAttributes.Public, module.TypeSystem.Void); owner.Methods.Add(source);
            source.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
            var state = new TypeDefinition("", "<Execute>d__" + ordinal, TypeAttributes.NestedPrivate, module.TypeSystem.Object); owner.NestedTypes.Add(state);
            var next = new MethodDefinition("MoveNext", MethodAttributes.Private, module.TypeSystem.Void); state.Methods.Add(next);
            next.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
            var constructor = new MethodReference(".ctor", module.TypeSystem.Void, new TypeReference("System.Runtime.CompilerServices", "AsyncStateMachineAttribute", module, module.TypeSystem.CoreLibrary)) { HasThis = true };
            var systemType = new TypeReference("System", "Type", module, module.TypeSystem.CoreLibrary); constructor.Parameters.Add(new ParameterDefinition(systemType));
            var attribute = new CustomAttribute(constructor); attribute.ConstructorArguments.Add(new CustomAttributeArgument(systemType, state)); source.CustomAttributes.Add(attribute);
            Check(ReferenceEquals(NativeAsyncMoveNext(assembly, owner.FullName, "Execute", 0), next), "Async body lookup follows the exact source attribute, independent of compiler ordinal " + ordinal);
            source.CustomAttributes.Clear(); var rejected = false;
            try { NativeAsyncMoveNext(assembly, owner.FullName, "Execute", 0); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Missing async attribute cannot fall back to a generated type name: " + ordinal);
        }
    }
}
