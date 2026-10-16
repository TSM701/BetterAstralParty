#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace BetterAstralParty.Updating;

internal sealed class SharedLoaderRecoveryException : IOException
{
    internal readonly string BackupPath;
    internal SharedLoaderRecoveryException(string backupPath, Exception writeError, Exception rollbackError)
        : base("공용 로더 설정 복원을 확인할 수 없어 게임 실행을 중단했습니다. 다음 백업으로 doorstop_config.ini를 복구한 뒤 다시 실행하세요. / Shared loader rollback could not be verified; launch stopped. Restore doorstop_config.ini from this retained backup before retrying: " + backupPath,
            new AggregateException(writeError, rollbackError)) { BackupPath = backupPath; }
    // Framework's outer Exception formatter can omit later AggregateException causes.
    public override string ToString()
    {
        var causes = ((AggregateException)InnerException!).InnerExceptions;
        return GetType().FullName + ": " + Message + "\r\n" + StackTrace
            + "\r\nWrite cause: " + causes[0] + "\r\nRestore or cleanup cause: " + causes[1];
    }
}

internal static class SharedLoaderConfiguration
{
    internal const string BackupPrefix = "BepInEx/config/BetterAstralParty.loader-before-";
    private const string Ini = "doorstop_config.ini";
    private const int MaximumBytes = 64 * 1024;
#if BAP_LOADER_FIXTURE
    internal static Action<string>? FixtureBoundary = null;
#endif

    internal static void Enable(string root, UpdateLaunchGate.Lease operation)
    {
        var backupPath="";SharedLoaderRecoveryException? recoveryFailure=null;
        try {
        using var fence = new WindowsFileFence(root);
        operation.Recheck(fence);
        using var file = fence.OpenFile(Ini, mutable: true);
        var original = Read(file);
        var parsed = Inspect(original);
        if (parsed.Enabled) return; // Respect an intervening user/other-mod change.
        var updated = (byte[])original.Clone();
        parsed.Replacement.CopyTo(updated, parsed.Offset);
        backupPath = BackupPrefix + Guid.NewGuid().ToString("N") + ".bak";
        using var backup = fence.OpenFile(backupPath, mutable: true, create: true);
        backup.Stream.Write(original, 0, original.Length); backup.Flush();
        if (backup.Hash() != UpdateTrust.Hash(original))
            throw new IOException("Shared-loader backup verification failed");
#if BAP_LOADER_FIXTURE
        FixtureBoundary?.Invoke("before-enable-write");
#endif
        file.Recheck();
        file.Stream.Position = parsed.Offset;
        try
        {
            file.Stream.Write(parsed.Replacement, 0, parsed.Replacement.Length); file.Flush();
#if BAP_LOADER_FIXTURE
            FixtureBoundary?.Invoke("after-enable-write");
#endif
            if (file.Hash() != UpdateTrust.Hash(updated))
                throw new IOException("Shared-loader verification failed");
        }
        catch (Exception writeError)
        {
            // The exclusive retained handle still names the same file; keep the backup even after rollback.
            try {
#if BAP_LOADER_FIXTURE
                FixtureBoundary?.Invoke("before-enable-rollback");
#endif
                file.Recheck(); file.Stream.Position = 0; file.Stream.Write(original, 0, original.Length); file.Flush();
#if BAP_LOADER_FIXTURE
                FixtureBoundary?.Invoke("after-enable-rollback");
#endif
                if (file.Hash() != UpdateTrust.Hash(original)) throw new IOException("Shared-loader rollback verification failed");
            } catch (Exception rollbackError) { recoveryFailure=new SharedLoaderRecoveryException(backupPath, writeError, rollbackError);throw recoveryFailure; }
            throw;
        }
        finally {
#if BAP_LOADER_FIXTURE
            FixtureBoundary?.Invoke("after-enable-attempt");
#endif
        }
        } catch(Exception cleanupError) when(recoveryFailure!=null && !ReferenceEquals(cleanupError,recoveryFailure)) {
            // Disposing a failed stream must not downgrade its uncertain write to an advisory I/O error.
            throw new SharedLoaderRecoveryException(backupPath,recoveryFailure,cleanupError);
        }
    }

    private static byte[] Read(WindowsFileFence.FileLease file)
    {
        file.Recheck();
        if (file.Stream.Length is < 1 or > MaximumBytes) throw new InvalidDataException("Shared-loader size");
        var bytes = new byte[(int)file.Stream.Length];
        file.Stream.Position = 0;
        var position = 0;
        while (position < bytes.Length)
        {
            var count = file.Stream.Read(bytes, position, bytes.Length - position);
            if (count == 0) throw new EndOfStreamException();
            position += count;
        }
        file.Recheck(); return bytes;
    }

    internal static (bool Enabled, int Offset, byte[] Replacement) Inspect(byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaximumBytes) throw new InvalidDataException("Shared-loader size");
        Encoding encoding = new UTF8Encoding(false, true);
        var start = 0;
        if (bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) start = 3;
        else if (bytes.Take(2).SequenceEqual(new byte[] { 0xFF, 0xFE })) { start = 2; encoding = new UnicodeEncoding(false, false, true); }
        else if (bytes.Take(2).SequenceEqual(new byte[] { 0xFE, 0xFF })) throw new InvalidDataException("Unsupported Windows INI encoding");
        var text = encoding.GetString(bytes, start, bytes.Length - start);
        if (text.Contains('\0') || text.Replace("\r\n", "").Contains('\r')) throw new InvalidDataException("Shared-loader encoding");
        var general = false; var sections = 0; Match? value = null; var valueIndex = 0;
        foreach (Match line in Regex.Matches(text, @"(?m)^[^\r\n]+"))
        {
            var header = Regex.Match(line.Value, @"\A[ \t]*\[([^\]]+)\][ \t]*\z");
            if (header.Success)
            {
                general = string.Equals(header.Groups[1].Value, "General", StringComparison.OrdinalIgnoreCase);
                if (general) sections++;
                continue;
            }
            if (!general) continue;
            var entry = Regex.Match(line.Value, @"\A[ \t]*enabled[ \t]*=[ \t]*(true|false)[ \t]*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!entry.Success)
            {
                if (Regex.IsMatch(line.Value, @"\A[ \t]*enabled[ \t]*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Shared-loader enabled value");
                continue;
            }
            if (value != null) throw new InvalidDataException("Shared-loader duplicate enabled");
            value = entry; valueIndex = line.Index + entry.Groups[1].Index;
        }
        if (sections != 1 || value == null) throw new InvalidDataException("Shared-loader General setting");
        // Equal-size replacement preserves every unrelated byte, including BOM, comments and line endings.
        return (value.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase),
            start + encoding.GetByteCount(text.Substring(0, valueIndex)), encoding.GetBytes("true "));
    }
}
