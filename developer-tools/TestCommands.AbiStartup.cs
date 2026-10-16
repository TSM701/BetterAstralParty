using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void StartupNotice(DevContext context, Dictionary<string, string> options, Action<string> pin)
    {
        if (!options.TryGetValue("plugin-path", out var pluginPath)) throw new ArgumentException("Startup notice requires explicit --plugin-path metadata input.");
        pluginPath = Path.GetFullPath(pluginPath); pin(pluginPath); using var plugin = AssemblyDefinition.ReadAssembly(pluginPath); using var native = Native(context);
        var gated = new List<MethodDefinition>(); var manual = new List<MethodDefinition>();
        foreach (var type in native.MainModule.GetTypes()) foreach (var method in type.Methods.Where(method => method.HasBody)) {
            var calls = GeneralCalls(method); var gate = calls.Contains("System.Boolean UGUIController::IsNeedOpenNoticeWindow()", StringComparer.Ordinal);
            if (gate) gated.Add(method);
            if (!gate && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "ShowNoticeWindow")) manual.Add(method);
        }
        var automatic = NativeAsyncMoveNext(native, "Core.Scene.LoginSceneController", "Start", 0);
        Check(gated.Count == 1 && gated[0] == automatic, "Automatic notice condition has only the exact startup source method caller");
        foreach (var (owner, method) in new[] { ("UI.LoginPanel", "ShowNotice"), ("UI.BottomMenuPanel", "ExecuteOperate") })
            Check(manual.Contains(NativeAsyncMoveNext(native, owner, method, 1)), "Native manual NOTICE opening preserved " + owner + "." + method);
        MethodDefinition Prefix(string name, string target, string method)
        {
            var type = plugin.MainModule.Types.Single(type => type.Name == name); var attribute = type.CustomAttributes.Single(attribute => attribute.AttributeType.Name == "HarmonyPatch");
            Check(attribute.ConstructorArguments.Count == 2 && attribute.ConstructorArguments[0].Value is TypeReference reference && reference.FullName == target && Equals(attribute.ConstructorArguments[1].Value, method), "Notice patch targets only approved automatic method " + name);
            var prefix = type.Methods.Single(row => row.Name == "Prefix"); Check(prefix.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "HarmonyPrefix"), "Notice Harmony prefix " + name); return prefix;
        }
        var startup = Prefix("StartupNoticePatch", "UGUIController", "IsNeedOpenNoticeWindow"); var ops = String.Join(",", startup.Body.Instructions.Where(instruction => instruction.OpCode.Name != "nop").Select(instruction => instruction.OpCode.Name));
        Check(startup.Parameters[0].Name == "__result" && Il(startup).Contains("PvpSafety::get_Suspended", StringComparison.Ordinal) && Regex.IsMatch(ops, "ldarg.0,ldc.i4.0,stind.i1,ldc.i4.0") && ops.Contains("ldc.i4.1", StringComparison.Ordinal) && ops.Contains("ret", StringComparison.Ordinal), "Automatic condition false and original execution skipped only outside PvP");
        var launcher = Prefix("LauncherNoticePatch", "UGUILoginController", "TryShowNoticeWin");
        Contains(Il(launcher), new[] { "PvpSafety::get_Suspended", "UGUILoginController::get_btn_Notices", "GameObject::SetActive", "ldc.i4.1", "Compatibility::Block", "ldc.i4.0" }, "Manual launcher NOTICE button and original fallback");
        Check(!Regex.IsMatch(Il(launcher), "OpenNotice|CloseWindow|TryShowWindow") && launcher.Body.ExceptionHandlers.Count > 0, "Launcher patch suppresses only automatic opening and has failure fallback");
        foreach (var prefix in new[] { startup, launcher }) NoticeOffReturnsOriginal(prefix);
        Contains(ReadSource(context, "src/Plugin.cs"), new[] { "if (PvpSafety.Suspended || !Plugin.SuppressNotices.Value) return true;", "if (PvpSafety.Suspended || !Plugin.SuppressNotices.Value || !Compatibility.Allowed(\"Notices\")) return true;" }, "Notice preference OFF yields to native startup and launcher behavior before owned changes");
        var game = options.TryGetValue("game-root", out var selectedGame) ? Path.GetFullPath(selectedGame)
            : Path.Combine(Directory.GetParent(context.Root)!.FullName, "8vJXnINT");
        var interop = options.TryGetValue("interop-root", out var supplied) ? Path.GetFullPath(supplied) : Path.Combine(game, "BepInEx", "interop");
        var callers = NativeNoticeCallers(interop, pin, out var automaticAot);
        Check(callers.Length == 1 && callers[0] == automaticAot, "Native AOT launcher notice has only the exact automatic game-module caller");
        Console.WriteLine("Startup notice: static native interop metadata read only; no native assembly executed or game process opened.");
    }

    private static void NoticeOffReturnsOriginal(MethodDefinition prefix)
    {
        var reads = prefix.Body.Instructions.Where(instruction => instruction.Operand is MethodReference method && method.DeclaringType.FullName == "BetterAstralParty.Plugin" && method.Name == "get_SuppressNotices").ToArray();
        Check(reads.Length == 1, "Compiled notice prefix reads its independent preference: " + prefix.DeclaringType.Name);
        Instruction Next(Instruction instruction) { var next = instruction.Next; while (next != null && next.OpCode.Code == Code.Nop) next = next.Next; return next ?? throw new InvalidDataException("Incomplete notice preference branch."); }
        var value = Next(reads[0]);
        Check(value.Operand is MethodReference getter && getter.Name == "get_Value" && getter.DeclaringType.FullName == "BepInEx.Configuration.ConfigEntry`1<System.Boolean>"
            && (getter.ReturnType.FullName == "System.Boolean" || getter.ReturnType is GenericParameter parameter && parameter.Type == GenericParameterType.Type && parameter.Position == 0), "Notice preference uses the saved Boolean value: " + prefix.DeclaringType.Name);
        var branch = Next(value);
        Check(branch.OpCode.Code is Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S, "Notice OFF has a direct conditional native-fallback branch: " + prefix.DeclaringType.Name);
        var current = branch.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? (Instruction)branch.Operand : Next(branch);
        var returnsTrue = false;
        for (var steps = 0; steps < 16; steps++) {
            if (current.OpCode.Code == Code.Nop) { current = Next(current); continue; }
            if (current.OpCode.Code is Code.Br or Code.Br_S) { current = (Instruction)current.Operand; continue; }
            if (current.OpCode.Code == Code.Ldc_I4_1 && Next(current).OpCode.Code == Code.Ret) returnsTrue = true;
            break;
        }
        Check(returnsTrue, "Preference OFF returns true before native button/result writes: " + prefix.DeclaringType.Name);
    }

    private static MethodDefinition NativeAotAsyncMoveNext(AssemblyDefinition assembly, string type, string method)
    {
        var source = NativeMethod(assembly, type, method, 0);
        Check(source.ReturnType.FullName == "System.Void", "Exact AOT automatic source method signature");
        var originalName = @"\A" + Regex.Escape(type + "+<" + source.Name + ">d__") + @"[0-9]+\z";
        var states = source.DeclaringType.NestedTypes.Where(state => state.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "Il2CppInterop.Common.Attributes.ObfuscatedNameAttribute"
            && attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is string name && Regex.IsMatch(name, originalName))).ToArray();
        Check(states.Length == 1 && states[0].Module == assembly.MainModule && states[0].BaseType.FullName == "Il2CppSystem.ValueType", "Exact AOT original-name mapping belongs to the automatic source owner");
        var next = NativeMethod(assembly, states[0].FullName, "MoveNext", 0); var set = NativeMethod(assembly, states[0].FullName, "SetStateMachine", 1);
        Check(!next.IsStatic && next.ReturnType.FullName == "System.Void" && !set.IsStatic && set.ReturnType.FullName == "System.Void"
            && set.Parameters[0].ParameterType.FullName == "Il2CppSystem.Runtime.CompilerServices.IAsyncStateMachine", "AOT original-name mapping retains the exact async state-machine ABI");
        return next;
    }

    private static string[] NativeNoticeCallers(string interop, Action<string> pin, out string automaticAot)
    {
        using var fence = new WindowsFileFence(interop);
        var metadata = new List<WindowsFileFence.FileLease>();
        static byte[] ReadBounded(string path, int maximum, Action<string> pin, WindowsFileFence fence)
        {
            using var file = fence.OpenFile(Path.GetFileName(path)); pin(path);
            if (file.Stream.Length < 20 || file.Stream.Length > maximum) throw new InvalidDataException("Native xref metadata bounds.");
            var bytes = new byte[(int)file.Stream.Length]; file.Stream.ReadExactly(bytes); file.Recheck(); return bytes;
        }
        var map = ReadBounded(fence.Full("MethodAddressToToken.db"), 256 * 1024 * 1024, pin, fence);
        var cache = ReadBounded(fence.Full("MethodXrefScanCache.db"), 512 * 1024 * 1024, pin, fence);
        Check(BitConverter.ToInt32(map, 0) == 1297370453 && BitConverter.ToInt32(cache, 0) == 1129860437, "Native address/xref metadata format");
        var nameCount = BitConverter.ToInt32(map, 8); var count = BitConverter.ToInt32(map, 12); var offset = BitConverter.ToInt32(map, 16);
        Check(nameCount is > 0 and <= 512 && count is > 0 and <= 10000000 && offset >= 20 && (long)offset + (long)count * 16 <= map.Length, "Native address table extent and row bounds");
        var names = new List<string>(); using (var stream = new MemoryStream(map, false)) using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true))) {
            stream.Position = 20;
            for (var index = 0; index < nameCount; index++) {
                uint length = 0; var shift = 0;
                while (true) { if (shift >= 35) throw new InvalidDataException("Native assembly-name length."); var value = reader.ReadByte(); length |= (uint)(value & 127) << shift; if ((value & 128) == 0) break; shift += 7; }
                if (length is 0 or > 4096 || stream.Position + length > offset) throw new InvalidDataException("Native assembly-name bounds.");
                names.Add(new UTF8Encoding(false, true).GetString(reader.ReadBytes((int)length)));
            }
        }
        var loaded = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
        AssemblyDefinition Load(string assemblyName)
        {
            var name = assemblyName.Split(',')[0];
            if (!Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]+\z") || name.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("Native assembly-name path.");
            if (loaded.TryGetValue(name, out var value)) return value;
            var path = fence.Full(name + ".dll"); metadata.Add(fence.OpenFile(name + ".dll")); pin(path); value = AssemblyDefinition.ReadAssembly(path); loaded.Add(name, value); return value;
        }
        string Resolve(long address)
        {
            var low = 0; var high = count - 1;
            while (low <= high) {
                var middle = low + (high - low) / 2; var candidate = BitConverter.ToInt64(map, offset + middle * 8);
                if (candidate == address) {
                    var row = offset + count * 8 + middle * 8; var token = BitConverter.ToInt32(map, row); var assembly = BitConverter.ToInt32(map, row + 4);
                    if (assembly < 0 || assembly >= names.Count) throw new InvalidDataException("Native assembly index.");
                    return Load(names[assembly]).MainModule.LookupToken(token) is MemberReference member ? member.FullName : throw new InvalidDataException("Native method token.");
                }
                if (candidate < address) low = middle + 1; else high = middle - 1;
            }
            return "0x" + address.ToString("X");
        }
        try {
            var wrapper = Load("Assembly-CSharp"); automaticAot = NativeAotAsyncMoveNext(wrapper, "AppLauncher", "ExecuteGameModule").FullName;
            var method = wrapper.MainModule.GetType("UGUILoginController").Methods.Single(row => row.Name == "TryShowNoticeWin");
            var attributes = method.CustomAttributes.Single(attribute => attribute.AttributeType.Name == "CachedScanResultsAttribute").Fields.ToDictionary(field => field.Name, field => Convert.ToInt32(field.Argument.Value), StringComparer.Ordinal);
            var start = attributes["RefRangeStart"]; var end = attributes["RefRangeEnd"];
            if (start < 0 || end < start || (long)16 + (long)end * 24 > cache.Length || end - start > 1000000) throw new InvalidDataException("Native xref range.");
            var result = new List<string>();
            for (var index = start; index < end; index++) { var row = 16 + index * 24; if (BitConverter.ToInt32(cache, row + 16) == 1) result.Add(Resolve(BitConverter.ToInt64(cache, row))); }
            fence.Recheck(); return result.ToArray();
        } finally { foreach (var assembly in loaded.Values) assembly.Dispose(); foreach (var file in metadata) file.Dispose(); }
    }
}
