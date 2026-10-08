#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    internal enum InstallEligibility { Supported, ManualUpgradeRequired }
    internal static class UpdateEligibility
    {
        internal static InstallEligibility Receipt(byte[] bytes)
        { try { InstallReceipt.Parse(bytes); return InstallEligibility.Supported; } catch (ApplySafetyException) { return InstallEligibility.ManualUpgradeRequired; } }
    }
    internal sealed class UpdateTicket
    {
        internal const int MaxBytes = 4096;
        internal string RootId = "", Stage = "", StageId = "", DescriptorHash = "";
        internal UpdateContext Context = null!;
        internal uint GamePid, GameSession, LauncherPid, LauncherSession;
        internal long GameCreation, LauncherCreation;
        internal byte[] Bytes()
        {
            return Encoding.ASCII.GetBytes("BetterAstralParty.Handoff/v" + (Context.RepositoryId > 0 ? "2" : "1") + "\nroot=" + RootId + "\nstage=" + Stage + "\nstage-id=" + StageId + "\ndescriptor=" + DescriptorHash
                + "\nrepository=" + Context.Repository + "\nchannel=" + Context.Channel + "\ncurrent=" + Context.CurrentVersion + "\ntag=" + Context.ReleaseTag
                + "\nrelease=" + Context.ReleaseId.ToString(CultureInfo.InvariantCulture) + "\nasset=" + Context.AssetId.ToString(CultureInfo.InvariantCulture)
                + "\ngame=" + GamePid.ToString(CultureInfo.InvariantCulture) + ":" + GameCreation.ToString(CultureInfo.InvariantCulture) + ":" + GameSession.ToString(CultureInfo.InvariantCulture)
                + "\nlauncher=" + LauncherPid.ToString(CultureInfo.InvariantCulture) + ":" + LauncherCreation.ToString(CultureInfo.InvariantCulture) + ":" + LauncherSession.ToString(CultureInfo.InvariantCulture) + "\n"
                + (Context.RepositoryId > 0 ? "repository-id=" + Context.RepositoryId.ToString(CultureInfo.InvariantCulture) + "\ntransition="
                    + (Context.Transition == null ? "-" : Convert.ToBase64String(Context.Transition.Bytes())) + "\n" : ""));
        }
        internal static UpdateTicket Parse(byte[] bytes)
        {
            if (bytes.Length > MaxBytes) throw Bad(); foreach (var value in bytes) if (value > 127 || value == 13) throw Bad();
            var lines = Encoding.ASCII.GetString(bytes).Split('\n'); var v2 = lines.Length == 16 && lines[0] == "BetterAstralParty.Handoff/v2";
            if (!v2 && (lines.Length != 14 || lines[0] != "BetterAstralParty.Handoff/v1") || lines[lines.Length - 1] != "") throw Bad();
            var keys = new[] { "root=", "stage=", "stage-id=", "descriptor=", "repository=", "channel=", "current=", "tag=", "release=", "asset=", "game=", "launcher=" }; var v = new string[keys.Length];
            for (var i = 0; i < keys.Length; i++) { if (!lines[i + 1].StartsWith(keys[i], StringComparison.Ordinal)) throw Bad(); v[i] = lines[i + 1].Substring(keys[i].Length); }
            if (!Regex.IsMatch(v[0], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[1], @"\Abap-stage-[0-9a-f]{32}\z") || !Regex.IsMatch(v[2], @"\A[0-9A-F]{8}:[0-9A-F]{16}\z") || !Regex.IsMatch(v[3], @"\A[0-9A-F]{64}\z")) throw Bad();
            long release, asset; if (!long.TryParse(v[8], NumberStyles.None, CultureInfo.InvariantCulture, out release) || !long.TryParse(v[9], NumberStyles.None, CultureInfo.InvariantCulture, out asset)) throw Bad();
            long repositoryId = 0; ChannelTransitionIntent? transition = null;
            if (v2) {
                if (!lines[13].StartsWith("repository-id=", StringComparison.Ordinal) || !long.TryParse(lines[13].Substring(14), NumberStyles.None, CultureInfo.InvariantCulture, out repositoryId) || repositoryId <= 0
                    || !lines[14].StartsWith("transition=", StringComparison.Ordinal)) throw Bad();
                var encoded = lines[14].Substring(11);
                if (encoded != "-") try { transition = ChannelTransitionIntent.Parse(Convert.FromBase64String(encoded)); } catch (FormatException) { throw Bad(); }
            }
            var result = new UpdateTicket { RootId = v[0], Stage = v[1], StageId = v[2], DescriptorHash = v[3], Context = new UpdateContext(v[4], v[5], v[6], v[7], release, asset, UpdateTrust.PlatformId, repositoryId: repositoryId, transition: transition) };
            Process(v[10], out result.GamePid, out result.GameCreation, out result.GameSession); Process(v[11], out result.LauncherPid, out result.LauncherCreation, out result.LauncherSession);
            if (result.GamePid == 0 || result.GameCreation <= 0 || result.LauncherPid == 0 && (result.LauncherCreation != 0 || result.LauncherSession != 0)) throw Bad();
            var canonical = result.Bytes(); if (canonical.Length != bytes.Length) throw Bad(); for (var i = 0; i < bytes.Length; i++) if (canonical[i] != bytes[i]) throw Bad(); return result;
        }
        private static void Process(string text, out uint pid, out long creation, out uint session)
        {
            var f = text.Split(':'); pid = 0; creation = 0; session = 0;
            if (f.Length != 3 || !uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out pid) || !long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out creation) || !uint.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out session)) throw Bad();
        }
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
        internal void Recheck(WindowsFileFence fence)
        { if (fence.RootIdentity.Text != RootId || fence.DirectoryIdentity(Stage).Text != StageId || Context.Transition != null && Context.Transition.Root != RootId) throw Bad(); fence.AssertPrivateDirectory(Stage); }
    }
}
