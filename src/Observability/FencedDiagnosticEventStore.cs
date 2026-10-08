#nullable enable
using System;
using System.IO;
namespace BetterAstralParty.Observability {
    // Single production store for runtime/helper/installer. Unknown v1 evidence is
    // retained and refused; no legacy adapter, ACL repair or alternate path fallback.
    internal static class DiagnosticOwner {
        private static string DirectoryFor(string root,bool helper,bool installer) {
            var path=Path.Combine(root,"BepInEx","BetterAstralParty-Diagnostics","minimal-v1");
            return helper?Path.Combine(path,"helper"):installer?Path.Combine(path,"installer"):path;
        }
        internal static MinimalEventLog StartInstaller(string root,string version,string build) {
            return new MinimalEventLog(new FileDiagnosticEventStore(DirectoryFor(root,false,true),DirectoryFor(root,false,false)),new DiagnosticIdentity(version,build,Environment.Version.ToString()));
        }
        internal static MinimalEventLog Start(string root,bool helper,string version,string build,Action? legacyFlush=null) {
            return new MinimalEventLog(new FileDiagnosticEventStore(DirectoryFor(root,helper,false),DirectoryFor(root,false,false)),new DiagnosticIdentity(version,build,Environment.Version.ToString()),legacyFlush:legacyFlush);
        }
    }
}
