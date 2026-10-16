#nullable enable
using System;
namespace BetterAstralParty.Updating
{
    // The isolated build never queries or terminates native processes.
    internal static class UpdateTrust { internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); } }
    internal static class UpdateTicket
    {
        internal static byte[] Read(WindowsFileFence.FileLease file, long maximum) { file.Stream.Position = 0; if (file.Stream.Length > maximum) throw new InvalidOperationException("Fixture read bound"); using (var memory = new System.IO.MemoryStream()) { file.Stream.CopyTo(memory); return memory.ToArray(); } }
    }
    internal static class WindowsProcessGuard
    {
        internal static Func<uint, bool, Captured>? Observer;
        internal static uint LastMetadataExitWaitMilliseconds;
        internal sealed class Captured : IDisposable
        {
            private readonly Func<bool> _exited;
            private readonly Func<uint, bool>? _wait;
            internal Captured(string image = "", uint session = 0, long creation = 0, Func<bool>? exited = null, Func<uint, bool>? wait = null)
            { Image = image; Session = session; Creation = creation; _exited = exited ?? (() => true); _wait = wait; }
            internal bool Exited => _exited();
            internal bool WaitForExit(uint milliseconds) { return _wait == null ? Exited : _wait(milliseconds); }
            internal readonly string Image;
            internal readonly uint Session;
            internal readonly long Creation;
            public void Dispose() { }
        }
        internal static Captured Observe(uint pid, bool allowExited = false, uint metadataExitWaitMilliseconds = 0)
        { if (metadataExitWaitMilliseconds > 2000 || !allowExited && metadataExitWaitMilliseconds != 0) throw new ArgumentOutOfRangeException(nameof(metadataExitWaitMilliseconds)); LastMetadataExitWaitMilliseconds = metadataExitWaitMilliseconds; return Observer == null ? new Captured() : Observer(pid, allowExited); }
    }
}
