using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void CounterPreferences(DevContext context, string stage, Action<string> pin, Dictionary<string, TypeDefinition> types)
    {
        Check(types["FairyGUI.Transition"].Fields.Single(field => field.Name == "_items").FieldType.FullName == "FairyGUI.TransitionItem[]", "Counter transition keeps the actual native typed array");
        Check(types["FairyGUI.TransitionItem"].Fields.Single(field => field.Name == "type").FieldType.FullName == "FairyGUI.TransitionActionType"
            && types["FairyGUI.TransitionItem"].Fields.Single(field => field.Name == "value").FieldType.FullName == "System.Object", "Native counter action and payload field types");
        Check(types["FairyGUI.TransitionActionType"].Fields.Single(field => field.Name == "Sound").HasConstant
            && types["FairyGUI.TValue_Sound"].Fields.Single(field => field.Name == "sound").FieldType.FullName == "System.Int32"
            && types["FairyGUI.TValue_Sound"].Fields.Single(field => field.Name == "volume").FieldType.FullName == "System.Single", "Native named sound action and sound-ID payload ABI");
        var apply = types["FairyGUI.Transition"].Methods.Single(method => method.Name == "ApplyValue");
        var instructions = apply.Body.Instructions;
        var soundRead = instructions.Select((instruction,index)=>(instruction,index)).First(row => row.instruction.Operand is FieldReference field && field.FullName == "System.Int32 FairyGUI.TValue_Sound::sound").index;
        var playback = instructions.Select((instruction,index)=>(instruction,index)).First(row => row.instruction.Operand is MethodReference method && method.FullName == "System.Void FairyGUI.Stage::PlayOneShotSound(System.Int32)").index;
        Check(playback > soundRead && instructions.Skip(soundRead).Take(playback-soundRead).Any(instruction=>instruction.OpCode.Code is Code.Ble or Code.Ble_S), "Native sound-ID nonpositive branch precedes playback");
        var interopPath = Path.Combine(context.Root,".deps/bepinex/BepInEx/core/Il2CppInterop.Runtime.dll"); pin(interopPath);
        using var interop = AssemblyDefinition.ReadAssembly(interopPath);
        var api = interop.MainModule.GetType("Il2CppInterop.Runtime.IL2CPP");
        foreach(var signature in new[]{"System.IntPtr Il2CppInterop.Runtime.IL2CPP::il2cpp_array_new(System.IntPtr,System.UInt64)","System.IntPtr Il2CppInterop.Runtime.IL2CPP::il2cpp_object_get_class(System.IntPtr)","System.Int32 Il2CppInterop.Runtime.IL2CPP::il2cpp_class_get_rank(System.IntPtr)","System.IntPtr Il2CppInterop.Runtime.IL2CPP::il2cpp_class_get_element_class(System.IntPtr)"})
            Check(api.Methods.Any(method=>method.FullName==signature),"Counter typed array allocation ABI "+signature);
        var source = Derive(ReadSource(context,"src/NativeUi.cs"),@"(?s)    internal static RuntimeObject\? CounterIndicator\(.*?\r?\n    \}","Actual private counter constructor");
        var wrapper = source.IndexOf("var items = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray",StringComparison.Ordinal);
        var rank = source.IndexOf("il2cpp_class_get_rank(arrayClass) != 1",StringComparison.Ordinal);
        var element = source.IndexOf("element != RuntimeObject.FindClass(\"FairyGUI\", \"TransitionItem\")",StringComparison.Ordinal);
        Check(rank >= 0 && element > rank && wrapper > element && source.IndexOf("items.Length",StringComparison.Ordinal) > wrapper
            && source.IndexOf("if (!Plugin.CounterAnimation.Value)",StringComparison.Ordinal) > wrapper,
            "Rank and exact element type are common preflight before any array wrapper, length, item read or preference branch");
        Check(source.Contains("filtered.SetValue(sounds[i], i)",StringComparison.Ordinal)
            && !Regex.IsMatch(source,@"items\[[^\]]+\]\s*=|filtered\[[^\]]+\]\s*=|SetField\(""type""|SetField\(""volume"""),"Counter uses native typed SetValue, never raw reference stores or fake action types/volume muting");
        var fixturePath = Path.Combine(context.Root,"focus-tests/counter-preferences/Fixture.cs"); pin(fixturePath);
        var fixture = File.ReadAllText(fixturePath); Check(fixture.Split("__COUNTER_SOURCE__",StringSplitOptions.None).Length==2,"One literal counter production substitution");
        RunGeneralFixture(context,Path.Combine(stage,"counter-preferences"),fixture.Replace("__COUNTER_SOURCE__",source,StringComparison.Ordinal),"CounterPreferencesCheck.Run()",pin);
    }
}
