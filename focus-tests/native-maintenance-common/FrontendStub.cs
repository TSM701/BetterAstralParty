using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class NativeFrontendStub
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder path, uint capacity);
    private static int Main(string[] args)
    {
        var module = new StringBuilder(32768);
        if (GetModuleFileNameW(IntPtr.Zero, module, (uint)module.Capacity) == 0) return 98;
        var executableRoot = Path.GetDirectoryName(module.ToString().Replace("\\\\?\\", "")).TrimEnd('\\');
        var root = Directory.GetParent(executableRoot).FullName;
        var result = Environment.GetEnvironmentVariable("BAP_ENTRY_RESULT");
        if (string.IsNullOrEmpty(result) || Path.GetDirectoryName(Path.GetFullPath(result)) != root) return 99;
        string? game = null, operation = null; var development = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--install" || args[i] == "--uninstall" || args[i] == "--force-uninstall") operation = args[i].Substring(2);
            else if (args[i] == "--development") development = true;
            else if (args[i].Equals("-GameRoot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) game = args[++i];
        }
        File.WriteAllText(result, new JavaScriptSerializer().Serialize(new { executableRoot, callerRoot = Environment.GetEnvironmentVariable("BAP_MAINTENANCE_CWD"), gameRoot = game, operation, development, actualInstallerExecuted = false, args }), new UTF8Encoding(false));
        return int.Parse(Environment.GetEnvironmentVariable("BAP_FIXTURE_EXIT"));
    }
}
