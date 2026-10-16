using System.Runtime.InteropServices;
using BetterAstralParty.Updating;
using Microsoft.Win32.SafeHandles;

internal static partial class NativeCompatibilityFixture
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint size, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    private static string LinkScope(string scratch, string path)
    {
        var full = WindowsPath.Absolute(path.Replace('/', '\\'));
        if (!full.StartsWith(WindowsPath.Absolute(scratch).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Inert link fixture escaped its fresh artifact root.");
        return full;
    }
    private static void Junction(string scratch, string path, string target)
    {
        path = LinkScope(scratch, path); target = LinkScope(scratch, target);
        Directory.CreateDirectory(WindowsPath.ForIo(path));
        var substitute = System.Text.Encoding.Unicode.GetBytes(@"\??\" + target); var print = System.Text.Encoding.Unicode.GetBytes(target);
        var data = new byte[16 + substitute.Length + print.Length + 4];
        BitConverter.GetBytes(0xA0000003U).CopyTo(data, 0); BitConverter.GetBytes(checked((ushort)(data.Length - 8))).CopyTo(data, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(data, 10); BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(data, 12); BitConverter.GetBytes(checked((ushort)print.Length)).CopyTo(data, 14);
        substitute.CopyTo(data, 16); print.CopyTo(data, 18 + substitute.Length);
        using var handle = CreateFileW(WindowsPath.ForIo(path), 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x900A4, data, (uint)data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new IOException("Scoped inert junction setup failed: " + Marshal.GetLastWin32Error());
    }
    private static void MoveToJunction(string scratch, string path, string target)
    {
        path = LinkScope(scratch, path); target = LinkScope(scratch, target);
        Directory.Move(WindowsPath.ForIo(path), WindowsPath.ForIo(target));
        Junction(scratch, path, target);
    }
    private static void LinkedInputs(string scratch)
    {
        foreach (var relative in new[] { "AstralParty_INT.exe", "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", "BepInEx/core/BepInEx.Core.dll" }) {
            var loader = relative is "winhttp.dll" or "BepInEx/core/BepInEx.Core.dll";
            Case(scratch, "hardlink-" + Path.GetFileName(relative), loader, seed => {
                var existing = LinkScope(scratch, Path.Combine(seed.Root, relative)); var alias = LinkScope(scratch, Path.Combine(seed.Root, "fixture-alias.keep"));
                Check(CreateHardLinkW(WindowsPath.ForIo(alias), WindowsPath.ForIo(existing), IntPtr.Zero), "synthetic mandatory hardlink created");
            }, expectedFailure: loader ? null : ApplyFailure.LinkedFile, warningCount: loader ? 1 : 0);
        }
        Case(scratch, "loader-ancestor-junction", true, seed => MoveToJunction(scratch, Path.Combine(seed.Root, "BepInEx/core"), Path.Combine(scratch, "loader-junction-target")), warningCount: 1);
        Case(scratch, "bundled-loader-hardlink", true, seed => {
            seed.Bundle();
            var existing = LinkScope(scratch, Path.Combine(seed.Loader, "winhttp.dll")); var alias = LinkScope(scratch, Path.Combine(seed.Loader, "fixture-alias.keep"));
            Check(CreateHardLinkW(WindowsPath.ForIo(alias), WindowsPath.ForIo(existing), IntPtr.Zero), "synthetic bundled-loader hardlink created");
        }, warningCount: 1);
        Case(scratch, "bundled-loader-ancestor-junction", true, seed => {
            seed.Bundle(); MoveToJunction(scratch, Path.Combine(seed.Loader, "BepInEx/core"), Path.Combine(scratch, "bundled-loader-junction-target"));
        }, warningCount: 1);
        Case(scratch, "optional-content-hardlink", true, seed => {
            var existing = LinkScope(scratch, Path.Combine(seed.Root, Resource)); var alias = LinkScope(scratch, Path.Combine(seed.Root, "resource-alias.keep"));
            Check(CreateHardLinkW(WindowsPath.ForIo(alias), WindowsPath.ForIo(existing), IntPtr.Zero), "synthetic optional-content hardlink created");
        }, warningCount: 1);
        Case(scratch, "optional-cache-hardlink", true, seed => {
            var existing = LinkScope(scratch, Path.Combine(seed.Cache, Current[0])); var alias = LinkScope(scratch, Path.Combine(seed.Root, "catalog-alias.keep"));
            Check(CreateHardLinkW(WindowsPath.ForIo(alias), WindowsPath.ForIo(existing), IntPtr.Zero), "synthetic optional-cache hardlink created");
        }, warningCount: 2);
        Case(scratch, "optional-content-ancestor-junction", true, seed => MoveToJunction(scratch, Path.Combine(seed.Root, "AstralParty_INT_Data"), Path.Combine(scratch, "content-junction-target")), warningCount: 3);
        Case(scratch, "optional-cache-junction", true, seed => MoveToJunction(scratch, seed.Cache, Path.Combine(scratch, "cache-junction-target")), warningCount: 2);
    }
}
