global using System;
global using System.IO;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;
using System.Collections;
using System.Linq;
using System.Web.Script.Serialization;
using BetterAstralParty.Installation;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class FrameworkCompatibilityParity
{
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    private static string[] Lines(object value) => ((IEnumerable)value).Cast<object>().Select(item => (string)item).ToArray();
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine("Framework compatibility fixture failed: " + error.GetType().FullName);
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
    private static int Run(string[] args)
    {
        Check(args.Length == 2 && Path.GetDirectoryName(Path.GetFullPath(args[0])) == Path.GetFullPath(args[1]), "Isolated direct artifact scratch required.");
        var path = Path.Combine(args[0], "native-results.json");
        Check(new FileInfo(path).Length < 1024 * 1024, "Bounded inert fixture report required.");
        var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        var native = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(path));
        var reports = ((IEnumerable)native["Reports"]).Cast<Dictionary<string, object>>().ToArray();
        var checks = 0;
        foreach (var report in reports)
        {
            var result = GameCompatibility.Check((string)report["GameRoot"], (string)report["Manifest"], (string?)report["LoaderRoot"]);
            Check(result.Allowed == (bool)report["Allowed"], (string)report["Name"] + ": .NET Framework Allowed parity; " + string.Join("; ", result.Issues)); checks++;
            if ((bool)report["CompareExact"])
            {
                Check(result.Issues.SequenceEqual(Lines(report["Issues"])), (string)report["Name"] + ": .NET Framework issues parity"); checks++;
                Check(result.Warnings.SequenceEqual(Lines(report["Warnings"])), (string)report["Name"] + ": .NET Framework warnings parity"); checks++;
            }
        }
        Console.WriteLine(".NET Framework 4.8: " + checks + " compatibility parity checks PASS across " + reports.Length + " inert fixtures.");
        return 0;
    }
}
