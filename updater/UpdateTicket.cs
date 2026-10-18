#nullable enable
using System;
using System.IO;

namespace BetterAstralParty.Updating
{
    internal enum InstallEligibility { Supported, ManualUpgradeRequired }
    internal static class UpdateEligibility
    {
        internal static InstallEligibility Receipt(byte[] bytes)
        { try { InstallReceipt.Parse(bytes); return InstallEligibility.Supported; } catch (ApplySafetyException) { return InstallEligibility.ManualUpgradeRequired; } }
    }
    // Owner of an in-process update operation (the running game) plus bounded fenced read/write helpers.
    internal sealed class UpdateTicket
    {
        internal const int MaxBytes = 4096;
        internal string Stage = "-";
        internal uint GamePid = 0, GameSession = 0;
        internal long GameCreation = 0;
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.InvalidState); }
        internal static byte[] Read(WindowsFileFence.FileLease file, long limit)
        {
            file.Recheck(); file.Stream.Position = 0; if (file.Stream.Length < 1 || file.Stream.Length > limit) throw Bad();
            var bytes = new byte[checked((int)file.Stream.Length)]; var offset = 0;
            while (offset < bytes.Length) { var count = file.Stream.Read(bytes, offset, bytes.Length - offset); if (count < 1) throw Bad(); offset += count; }
            file.Recheck(); return bytes;
        }
        internal static void Write(WindowsFileFence fence, string relative, byte[] bytes)
        { using (var file = fence.OpenFile(relative, true, true)) { file.Stream.Write(bytes, 0, bytes.Length); file.Flush(); } }
    }
}
