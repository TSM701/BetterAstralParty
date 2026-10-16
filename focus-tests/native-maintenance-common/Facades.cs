#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BetterAstralParty.Installation;
using BetterAstralParty.Updating;

internal static class SteamLauncher
{
    public const int InstallBundleProtocol = 1;
    public static void ValidateMaintenancePaths(string game, string backups) { WholeInstallBridge.ValidateMaintenancePaths(game, backups); }
    public static IDisposable GuardOrdinaryRecovery(string game, string package, string version, string hash)
    { NativeCommonFixture.AssertScope(game); return new UpdateLaunchGate.Lease(game); }
    public static void RestoreOrdinaryInstall(string game, string backups, string hash, string[] targets, string current, bool complete, string identity)
    { NativeCommonFixture.AssertScope(game); OrdinaryInstallRecovery.Run(game, backups, hash, targets, current, complete, identity); }
    public static IDisposable BeginInstallerDiagnostics(string game, string version) { throw new Exception("Fixture must not run live diagnostics"); }
    public static void RecordInstallerFailure(string phase, Exception error) { throw new Exception("Fixture must not run entry failure paths"); }
}

namespace BetterAstralParty.Installation
{
    internal sealed class SteamProfileUnavailableException : Exception { }
    internal sealed class SteamArtworkRecovery { internal string Path = "", Backup = "", Hash = ""; }
    internal sealed class ShortcutRecovery
    {
        internal string Path = "", Hash = "", BackupHash = "";
        internal string? Backup = null;
        internal readonly List<SteamArtworkRecovery> Artwork = new List<SteamArtworkRecovery>();
    }
    internal static class NativeSteamMaintenance
    {
        internal static Action<Exception>? WaitingDiagnostic = null;
        internal static Exception? RootFailure;
        internal static int Stops;
        internal static void StopGame(string game, Func<string, string?> consent, Action<string> output) { NativeCommonFixture.AssertScope(game); Stops++; }
        internal static string? ResolveSteamRoot() { throw RootFailure ?? new Exception("Fixture must not query Steam"); }
        internal static string ResolveUserPath(string path) { throw new Exception("Fixture must not run entry path resolution"); }
        internal static string ResolveGame(string? game, string? steam, string package, Action<string>? output) { throw new Exception("Fixture must not discover games"); }
        internal static List<string> FindGameDirectories(string? steam, string package, Action<string>? output) { throw new Exception("Fixture must not discover games"); }
        internal static string? ResolveIntDirectory(string? game) { throw new Exception("Fixture must not discover games"); }
        internal static uint ResolveAccount(string steam, uint account) { throw new Exception("Fixture must not query Steam accounts"); }
    }
    internal static class NativeInstall
    {
        internal static void Run(NativeMaintenanceBundle bundle, bool update, string? steam, uint account, Func<string, string?> consent, Action<string> output, bool restartSteam, bool checkOnly, Action<string>? observation = null)
        { throw new Exception("Fixture must not install a package"); }
    }
    internal static class NativeUninstall
    {
        internal static void Run(NativeMaintenanceBundle bundle, string? steam, uint account, string loader, bool preview, Func<string, string?> consent, Action<string> output, Action<string>? observation = null)
        { throw new Exception("Fixture must not uninstall a package"); }
    }
    internal static class NativeEmergencyUninstall
    {
        internal static void Run(NativeMaintenanceBundle bundle, string? steam, uint account, bool preview, Func<string, string?> consent, Action<string> output)
        { throw new Exception("Fixture must not force-uninstall a package"); }
    }
}
