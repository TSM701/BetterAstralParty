#nullable enable
using System;
using BetterAstralParty.Observability;
using System.IO;
using System.Security.Cryptography;

namespace BetterAstralParty.Updating
{
    internal static class UpdateHelperProgram
    {
        private static string DiagnosticVersion => System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(UpdateHelperProgram).Assembly.Location).ProductVersion ?? "unknown";
        private static MinimalEventLog? _diagnostics;
        private static DiagnosticPhase _phase=DiagnosticPhase.BootstrapGate;
        private static void BindDiagnostics(string root) {
            _diagnostics=DiagnosticOwner.Start(root,true,DiagnosticVersion,typeof(UpdateHelperProgram).Module.ModuleVersionId.ToString("N"));
            if(!DiagnosticHub.Bind(_diagnostics)){_diagnostics.Dispose();_diagnostics=null;}
        }
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit+=(_,__)=>_diagnostics?.Stop(DiagnosticEndReason.ProcessExitCallback);
            try {
                if (args.Length == 1 && args[0] == "--help") {
                    Console.WriteLine("BetterAstralParty-UpdateHelper.exe --apply|--recover <game INT root> <verified-game-relative-stage-path>");
                    Console.WriteLine("Recovery requires the game/mod launcher closed; preserves backups and verifies signatures again.");
                    Console.WriteLine("From extracted trusted bootstrap outside the game root: --install-helper <root> <bundle>; --recover-helper <root> <verified-game-relative-helper-path>.");
                    Console.WriteLine("--clean-completed <root> - removes only authenticated duplicate handoff ZIPs; preserves journals/backups."); return 0;
                }
                var trust = UpdateTrust.Production();
#if BAP_FIXTURE_HELPER
                if ((args.Length != 3 && args.Length != 4) || (args[0] != "--apply" && args[0] != "--recover" && args[0] != "--install-helper" && args[0] != "--recover-helper" && args[0] != "--clean-completed")) return 64;
                var fixtureDirectory = Environment.GetEnvironmentVariable("BAP_HELPER_FIXTURE_ROOT");
                if (string.IsNullOrWhiteSpace(fixtureDirectory) || !Path.IsPathRooted(fixtureDirectory)) return 65;
                var allowed = Path.GetFullPath(fixtureDirectory!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var temporaryDirectory = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(Path.GetDirectoryName(allowed), temporaryDirectory, StringComparison.OrdinalIgnoreCase)
                    || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(allowed), @"\Abap-v4-helper-fixture-[a-f0-9]{32}\z")) return 65;
                using (var boundary = new WindowsFileFence(allowed))
                using (var marker = boundary.OpenFile(".bap-v4-helper-fixture-root")) {
                    var expected = System.Text.Encoding.ASCII.GetBytes("BetterAstralParty.HelperFixtureRoot/v1\n");
                    var actual = UpdateTicket.Read(marker, expected.Length);
                    if (actual.Length != expected.Length || UpdateTrust.Hash(actual) != UpdateTrust.Hash(expected)) return 65;
                }
                var root = Path.GetFullPath(args[1]); var keyPath = Path.GetFullPath(args.Length == 4 ? args[3] : Path.Combine(Path.GetDirectoryName(root)!, "fixture-public-key.bin"));
                if (!root.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || keyPath != Path.Combine(Path.GetDirectoryName(root)!, "fixture-public-key.bin")) return 65;
                using (var boundary = new WindowsFileFence(allowed))
                using (var key = boundary.OpenFile(keyPath.Substring(allowed.Length + 1).Replace(Path.DirectorySeparatorChar, '/')))
                    trust = new UpdateTrust(new[] { new TrustedUpdateKey("helper-fixture", new RSAParameters { Modulus = UpdateTicket.Read(key, 512), Exponent = new byte[] { 1, 0, 1 } }) });
                PublicReleaseEligibility.FixtureRead = (uri, context, descriptor) => {
                    if (uri == PublicReleaseEligibility.RepositoryEndpoint(context)) return PublicReleaseEligibility.FixtureMetadata(uri, context, descriptor);
                    var source = Environment.GetEnvironmentVariable("BAP_FIXTURE_PUBLIC_RELEASE");
                    if (source != null) {
                        var path = Path.GetFullPath(source);
                        if (!path.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Fixture release metadata outside inert root");
                        using var boundary = new WindowsFileFence(allowed); using var file = boundary.OpenFile(path.Substring(allowed.Length + 1).Replace(Path.DirectorySeparatorChar, '/'));
                        return UpdateTicket.Read(file, PublicReleaseEligibility.MaxReleaseBytes);
                    }
                    return PublicReleaseEligibility.FixtureMetadata(uri, context, descriptor);
                };
#else
                if (args.Length != 3 || (args[0] != "--apply" && args[0] != "--recover" && args[0] != "--install-helper" && args[0] != "--recover-helper" && args[0] != "--clean-completed")) return 64;
#if BAP_LAUNCHER_RECOVERY_FIXTURE
                if (!Path.GetFullPath(args[1]).StartsWith(FixtureBoundary.Root, StringComparison.OrdinalIgnoreCase)) return 65;
                PublicReleaseEligibility.FixtureRead = PublicReleaseEligibility.FixtureMetadata;
#endif
#endif
                if (!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
                if (args[0] == "--install-helper" || args[0] == "--recover-helper" || args[0] == "--clean-completed") {
                    using(var target=new WindowsFileFence(args[1])) {
                        if(args[0]=="--clean-completed") {
                            if(args[2]!="-") return 64;
                            var cleaned=UpdateRetention.Clean(target,trust);
                            Console.WriteLine("BAP-CLEAN:removed="+cleaned.RemovedPackages+"; already="+cleaned.AlreadyClean+"; preserved="+cleaned.Preserved);return 0;
                        }
                        HelperInstallResult installed;
                        if(args[0]=="--recover-helper") installed=HelperDelivery.Recover(target,args[2],trust);
                        else using(var source=new WindowsFileFence(args[2])) {
                            byte[] Read(string name,long maximum) {using(var file=name==HelperDescriptor.Image ? source.OpenLaunchImage(name) : source.OpenFile(name)) return UpdateTicket.Read(file,maximum);}
                            installed=HelperDelivery.Install(target,Read("helper.descriptor",UpdateTrust.MaxDescriptorBytes),Read("helper.signature",1024),Read(HelperDescriptor.Image,UpdateTrust.MaxFileBytes),trust);
                        }
                        Console.WriteLine("BAP-BOOTSTRAP:"+installed);
                        return installed==HelperInstallResult.Installed || installed==HelperInstallResult.AlreadyInstalled ? 0 : installed==HelperInstallResult.ManualUpgradeRequired ? 4 : 9;
                    }
                }
                TimeSpan? maximumWait = null;
#if BAP_FIXTURE_HELPER
                var wait = Environment.GetEnvironmentVariable("BAP_FIXTURE_MAX_WAIT_MS");
                if (wait != null) { int ms; if (!int.TryParse(wait,out ms) || ms < 100 || ms > 10000) return 65; maximumWait=TimeSpan.FromMilliseconds(ms); }
#endif
                using(var diagnosticRoot=new WindowsFileFence(args[1]))using(var diagnosticTicket=diagnosticRoot.OpenFile(args[2]+"/handoff.ticket")) {
                    var checkedTicket=UpdateTicket.Parse(UpdateTicket.Read(diagnosticTicket,UpdateTicket.MaxBytes));checkedTicket.Recheck(diagnosticRoot);
                    if(checkedTicket.Stage!=args[2])throw new ApplySafetyException(ApplyFailure.RecoveryRequired);
                }
                try {BindDiagnostics(args[1]);}catch {}
                _phase=args[0]=="--recover"?DiagnosticPhase.Recovery:DiagnosticPhase.Apply;
                var result = args[0] == "--recover" ? UpdateHelperService.Recover(args[1], args[2], trust) : UpdateHelperService.Run(args[1], args[2], trust, maximumWait);
                Console.WriteLine("BAP-HELPER:" + result);
                return result == HelperResult.Committed ? 0 : result == HelperResult.Cancelled ? 3 : result == HelperResult.ManualUpgradeRequired ? 4 : result == HelperResult.FilesBusy ? 7 : result == HelperResult.AlreadyClaimed ? 8 : result == HelperResult.RecoveryRequired ? 9 : result == HelperResult.Recovered ? 0 : result == HelperResult.FaultDisabled ? 12 : 11;
            } catch (UpdateValidationException error) { DiagnosticHub.Failure(DiagnosticFeature.Helper,_phase,DiagnosticHub.CodeForStatus(error.Failure.ToString()),error,validationCode:(int)error.Failure); Console.Error.WriteLine("BAP-HELPER:" + error.Failure); return 2; }
            catch (ApplySafetyException error) {
                DiagnosticHub.Failure(DiagnosticFeature.Helper,_phase,DiagnosticHub.CodeForStatus(error.Failure.ToString()),error,nativeCode:error.NativeError,applyCode:(int)error.Failure);
#if BAP_FIXTURE_HELPER
                Console.Error.WriteLine(error.ToString() + "; native=" + error.NativeError);
#endif
                Console.Error.WriteLine("BAP-HELPER:" + (error.NativeError == 32 ? "FilesBusy" : error.Failure.ToString())); return error.NativeError == 32 ? 7 : error.Failure == ApplyFailure.RecoveryRequired ? 9 : 5;
            }
            catch { Console.Error.WriteLine("BAP-HELPER:Failed"); return 6; }
            finally {if(_diagnostics!=null){_diagnostics.Stop(DiagnosticEndReason.OwnerDisposed);DiagnosticHub.Unbind(_diagnostics);}}
        }
    }
}
