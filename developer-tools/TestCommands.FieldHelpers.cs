using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static string WithoutNamespace(string source) => source.Replace("namespace BetterAstralParty;", "", StringComparison.Ordinal);
    private static string Fragment(string source, string pattern, string label)
    {
        var match = Regex.Match(source, pattern);
        Check(match.Success && match.Value.Length != 0, "Exact production fragment: " + label);
        return match.Value;
    }
    private static string MethodFragment(string source, string declaration) => Fragment(source, @"(?s)    " + Regex.Escape(declaration) + @"\(.*?\r?\n    \}", declaration);
    private static string Template(DevContext context, string path, Action<string> pin)
    { var full = Path.Combine(context.Root, path); pin(full); return File.ReadAllText(full); }
    private static string Body(MethodDefinition method) => string.Join("\n", method.Body.Instructions.Select(instruction => instruction.ToString()));
    private static string Operands(MethodDefinition method) => string.Join(" ", method.Body.Instructions.Select(instruction => instruction.Operand));
    private static string[] Calls(MethodDefinition method) => method.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().Select(reference => reference.FullName).ToArray();
    private static MethodDefinition ExactMethod(AssemblyDefinition assembly, string type, string name, string result, params string[] parameters)
    {
        var methods = assembly.MainModule.GetType(type).Methods.Where(method => method.Name == name && method.IsPublic && !method.IsStatic && !method.HasGenericParameters
            && method.ReturnType.FullName == result && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
        Check(methods.Length == 1, type + "." + name + " exact public instance ABI"); return methods[0];
    }
    private static MethodDefinition NativeMethod(AssemblyDefinition assembly, string type, string name, int? argc = null)
    {
        var methods = assembly.MainModule.GetType(type).Methods.Where(method => method.Name == name && (!argc.HasValue || method.Parameters.Count == argc)).ToArray();
        Check(methods.Length == 1 && methods[0].HasBody, type + "." + name + " exact native body"); return methods[0];
    }
    private static FieldDefinition ExactField(AssemblyDefinition assembly, string type, string name, string result, bool isPublic = true, bool isStatic = false)
    {
        var fields = assembly.MainModule.GetType(type).Fields.Where(field => field.Name == name).ToArray();
        Check(fields.Length == 1 && fields[0].FieldType.FullName == result && fields[0].IsPublic == isPublic && fields[0].IsStatic == isStatic, type + "." + name + " exact field ABI"); return fields[0];
    }
    private static void Pattern(string value, string pattern, string label) => Check(Regex.IsMatch(value, pattern), label);
    private static void NoPattern(string value, string pattern, string label) => Check(!Regex.IsMatch(value, pattern), label);
    private static void RunEmbedded(DevContext context, string stage, string name, string code, string invocation, Action<string> pin, bool warnAsError = false, string? noWarn = null, string? expectedFailure = null)
    {
        Check(!Regex.IsMatch(code, @"__[A-Za-z_]+__"), name + " all literal C# template placeholders bound");
        var run = Path.Combine(stage, name); Check(!Directory.Exists(run), "Fresh generated fixture: " + name); Directory.CreateDirectory(run);
        var source = Path.Combine(run, "Fixture.cs");
        var wrapper = expectedFailure == null
            ? invocation + "; System.Console.WriteLine(\"PASS: " + name + "\");"
            : "bool rejected=false; try { " + invocation + "; } catch(System.Exception e) { if(!System.Text.RegularExpressions.Regex.IsMatch(e.ToString(), " + Literal(expectedFailure) + ")) throw; rejected=true; } if(!rejected) throw new System.Exception(\"Mutation escaped: " + name + "\"); System.Console.WriteLine(\"PASS: rejected " + name + "\");";
        var runType = Regex.Matches(invocation, @"([A-Za-z_][A-Za-z0-9_.]+)\.Run\(").LastOrDefault()?.Groups[1].Value;
        if (expectedFailure == null && runType != null) wrapper += " var counts=new System.Collections.Generic.Dictionary<string,long>(); foreach(var f in typeof(" + runType + ").GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)) if(f.FieldType==typeof(int)||f.FieldType==typeof(long)) counts[f.Name]=System.Convert.ToInt64(f.GetValue(null)); System.Console.WriteLine(\"FIXTURE-COUNTERS:\"+System.Text.Json.JsonSerializer.Serialize(counts));";
        File.WriteAllText(source, code + "\ninternal static class HarnessMain { public static int Main() { try { " + wrapper + " return 0; } catch(System.Exception error) { System.Console.Error.WriteLine(error); return 1; } } }\n", new UTF8Encoding(false)); pin(source);
        var referenceRoot = Directory.EnumerateDirectories(Path.Combine(context.DotnetRoot, "packs/Microsoft.NETCore.App.Ref"))
            .Where(path => Version.Parse(Path.GetFileName(path)).Major == 8).OrderByDescending(path => Version.Parse(Path.GetFileName(path))).First();
        var references = Path.Combine(referenceRoot, "ref/net8.0");
        var image = Path.Combine(run, "Fixture.dll");
        var arguments = new List<string> { "exec", Compiler(context), "/nologo", "/noconfig", "/nostdlib+", "/langversion:latest", "/nullable:enable", "/unsafe+", "/deterministic+", "/platform:x64", "/target:exe", "/main:HarnessMain", "/out:" + image };
        if (warnAsError) arguments.Add("/warnaserror+"); if (noWarn != null) arguments.Add("/nowarn:" + noWarn);
        arguments.AddRange(Directory.EnumerateFiles(references, "*.dll").OrderBy(path => path, StringComparer.Ordinal).Select(path => "/reference:" + path)); arguments.Add(source);
        Ensure(context.Run(Dotnet(context), arguments, env: EnvironmentFor(run), timeoutMilliseconds: 60000, logPath: Path.Combine(run, "compile.log")), name + " compile");
        var runtimeVersion = Path.GetFileName(referenceRoot);
        context.WriteJsonCreateNew(Path.Combine(run, "Fixture.runtimeconfig.json"), new { runtimeOptions = new { tfm = "net8.0", framework = new { name = "Microsoft.NETCore.App", version = runtimeVersion } } });
        Ensure(context.Run(Dotnet(context), new[] { image }, env: EnvironmentFor(run), timeoutMilliseconds: 60000, logPath: Path.Combine(run, "run.log")), name + " runtime");
    }
    private static string Literal(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
