#nullable enable
using System;
using BetterAstralParty.Installation;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class ObservedGameProbe
{
    private static int Main(string[] args)
    {
        if (args.Length != 3) return 2;
        var result = GameCompatibility.Check(args[0], args[1], args[2]);
        foreach (var warning in result.Warnings) Console.WriteLine("WARNING: " + warning);
        foreach (var issue in result.Issues) Console.Error.WriteLine(issue);
        if (result.Error != null) Console.Error.WriteLine(result.Error);
        if (!result.Allowed) return 1;
        Console.WriteLine("PASS: observed game and catalog match the explicit manifest; read-only, no game execution or installation.");
        return 0;
    }
}
