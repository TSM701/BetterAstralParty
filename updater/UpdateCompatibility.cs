#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BetterAstralParty.Observability;

namespace BetterAstralParty.Updating
{
    internal static class GameCompatibilityPolicy
    {
        internal static bool RequiredGame(string path) => string.Equals(path,"AstralParty_INT.exe",StringComparison.OrdinalIgnoreCase)
            || string.Equals(path,"GameAssembly.dll",StringComparison.OrdinalIgnoreCase) || string.Equals(path,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase);
        internal static bool CacheReadError(Exception error) => error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ApplySafetyException;
    }

    // This observer never approves foreign loaders or gates owned, authenticated payload updates.
    internal sealed class UpdateCompatibility : IDisposable
    {
        private readonly List<WindowsFileFence.FileLease> _files=new List<WindowsFileFence.FileLease>();
        private readonly List<WindowsFileFence.FileLease> _cacheFiles=new List<WindowsFileFence.FileLease>();
        private WindowsFileFence? _cache;
        internal readonly List<string> Warnings=new List<string>();
        private string _catalogs="";
        private UpdateCompatibility(string cacheRoot) {
            try {_cache=new WindowsFileFence(cacheRoot);}
            catch(Exception error) when(GameCompatibilityPolicy.CacheReadError(error) || error is ArgumentException) {StopCache(error);}
        }
        private void Warn(string message) {
            if(Warnings.Count==0) DiagnosticHub.Stage(DiagnosticFeature.Coordinator,DiagnosticPhase.Catalog,DiagnosticOutcome.Partial);
            if(Warnings.Count<128) Warnings.Add(message);
        }
        private void StopCache(Exception error) {
            foreach(var file in _cacheFiles) file.Dispose();_cacheFiles.Clear();_cache?.Dispose();_cache=null;
            Warn("Downloaded catalog observation unavailable; runtime feature guards remain active.");
            DiagnosticHub.Failure(DiagnosticFeature.Coordinator,DiagnosticPhase.Catalog,DiagnosticCode.StorageUnavailable,error,nativeCode:error is ApplySafetyException safety?safety.NativeError:0);
        }
        private void StopObservations(Exception error) {
            foreach(var file in _files)file.Dispose();_files.Clear();StopCache(error);
            Warn("Compatibility observation unavailable; existing game and third-party loader files are preserved.");
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.CompatibilityRequired); }
        private static string Text(Dictionary<string,object?> value,string name) {
            object? result; if(!value.TryGetValue(name,out result) || !(result is string)) throw Bad(); return (string)result;
        }
        private string Catalogs() {
            if(_cache==null) return "";
            try {
            var names=new List<string>();var count=0;foreach(var path in Directory.EnumerateFiles(WindowsPath.ForIo(_cache.Root), "*", SearchOption.TopDirectoryOnly)) {
                if(++count>512) {Warn("Downloaded catalog observation capped at 512 files.");break;}
                var name=WindowsPath.Leaf(path); if(Regex.IsMatch(name,@"\Acatalog.*\.(json|hash)\z",RegexOptions.IgnoreCase)) names.Add(name);
            }
            names.Sort(StringComparer.Ordinal); return string.Join("|",names);
            } catch(Exception error) when(GameCompatibilityPolicy.CacheReadError(error)) {StopCache(error);return "";}
        }
        internal static UpdateCompatibility Require(WindowsFileFence game,byte[] manifest,WindowsFileFence.FileLease gameImage) {
#if BAP_SAFETY_FIXTURE || BAP_FIXTURE_HELPER
            var cacheRoot=Path.Combine(game.Root,"fixture-cache");
#else
            var cacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","feimo","AstralParty_INT","com.unity.addressables");
#endif
            UpdateCompatibility? check=null;
            try {
                check=new UpdateCompatibility(cacheRoot);
                Dictionary<string,object?>? json;
                try {json=new InstallReceipt.Json(manifest,65536,128,1024).Parse() as Dictionary<string,object?>;}
                catch(Exception error) when(error is ApplySafetyException || error is DecoderFallbackException) {throw new ApplySafetyException(ApplyFailure.CompatibilityRequired,0,error);}
                if(json==null || !json.ContainsKey("schema") || !Equals(json["schema"],1) || !json.ContainsKey("files") || !json.ContainsKey("cacheCatalogs")) throw Bad();
                var entries=json["files"] as List<object?>;var catalogs=json["cacheCatalogs"] as List<object?>;
                if(entries==null || entries.Count<6 || catalogs==null || catalogs.Count==0 || catalogs.Count>128) throw Bad();
                var expected=new List<string>();foreach(var catalog in catalogs) {
                    var name=catalog as string;if(name==null || !Regex.IsMatch(name,@"\Acatalog[^/\\:]*\.(json|hash)\z") || expected.Contains(name)) throw Bad();expected.Add(name);
                }
                expected.Sort(StringComparer.Ordinal);check._catalogs=check.Catalogs();
                if(!CatalogInventory.Matches(expected.ToArray(),check._catalogs.Length==0?new string[0]:check._catalogs.Split('|'))) check.Warn("Downloaded catalog inventory changed; runtime feature guards remain active.");
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var raw in entries) {
                    var entry=raw as Dictionary<string,object?>;if(entry==null || entry.Count!=4) throw Bad();
                    var scope=Text(entry,"root");var path=Text(entry,"path");var hash=Text(entry,"sha256");if(Text(entry,"kind").Length==0) throw Bad();
                    if((scope!="game" && scope!="cache") || !Regex.IsMatch(hash,@"\A[0-9A-Fa-f]{64}\z") || !seen.Add(scope+"/"+path) || scope=="game" && UpdateTrust.OwnedPath(path)) throw Bad();
                    game.Full(path);
                    var mandatory=scope=="game" && GameCompatibilityPolicy.RequiredGame(path);
                    WindowsFileFence? observation=null;WindowsFileFence.FileLease? transient=null;
                    try {
                    var fence=scope=="game" ? mandatory?game:(observation=new WindowsFileFence(game.Root)) : check._cache;
                    if(fence==null) {check.Warn("Missing catalog observation: "+path);continue;}
                    WindowsFileFence.FileLease file;
                    if(scope=="game" && path=="AstralParty_INT.exe") file=gameImage;
                    else {
                        try {file=fence.OpenFile(path);if(mandatory) check._files.Add(file);else if(scope=="cache") check._cacheFiles.Add(file);else transient=file;}
                        catch(ApplySafetyException error) when(!mandatory && (error.NativeError==2 || error.NativeError==3)) {check.Warn("Missing game/catalog observation: "+scope+"/"+path);continue;}
                    }
                    if(scope=="game" && GameCompatibilityPolicy.RequiredGame(path) && file.Stream.Length==0) {check.Warn("Empty game observation: "+path);continue;}
                    if(file.Stream.Length>512L*1024*1024) {
                        check.Warn("Game/catalog observation exceeds hash budget: "+scope+"/"+path);continue;
                    }
                    if(scope=="game" && path=="AstralParty_INT_Data/data.unity3d") {
                        var header=new byte[8];file.Stream.Position=0;
                        if(file.Stream.Length<32 || file.Stream.Read(header,0,8)!=8 || Encoding.ASCII.GetString(header)!="UnityFS\0") check.Warn("Resource bundle format changed; runtime feature guards remain active.");
                    }
                    if(file.Hash()!=hash.ToUpperInvariant()) {
                        check.Warn("Game/catalog baseline changed: "+scope+"/"+path);
                    }
                    } catch(Exception error) when(GameCompatibilityPolicy.CacheReadError(error)) {
                        if(scope=="cache") check.StopCache(error);
                        else {
                            check.Warn("Optional game observation unavailable: "+path);
                            DiagnosticHub.Failure(DiagnosticFeature.Coordinator,DiagnosticPhase.Catalog,DiagnosticCode.StorageUnavailable,error,nativeCode:error is ApplySafetyException safety?safety.NativeError:0);
                        }
                    } finally {transient?.Dispose();observation?.Dispose();}
                }
                foreach(var required in new[]{"game/AstralParty_INT.exe","game/GameAssembly.dll","game/UnityPlayer.dll","game/winhttp.dll","game/AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle","game/AstralParty_INT_Data/data.unity3d","game/BepInEx/core/BepInEx.Core.dll"}) if(!seen.Contains(required)) throw Bad();
                foreach(var name in expected) if(!seen.Contains("cache/"+name)) throw Bad();
                check.Recheck();return check;
            } catch(Exception error) when(GameCompatibilityPolicy.CacheReadError(error) || error is ArgumentException || error is DecoderFallbackException) {
                if(check==null)throw;
                check.StopObservations(error);return check;
            }
        }
        internal void Recheck() {
            try {foreach(var file in _files) file.Recheck();}
            catch(Exception error) when(GameCompatibilityPolicy.CacheReadError(error)) {StopObservations(error);}
            try {foreach(var file in _cacheFiles) file.Recheck();_cache?.Recheck();}
            catch(Exception error) when(GameCompatibilityPolicy.CacheReadError(error)) {StopCache(error);}
            var catalogs=Catalogs();
            if(catalogs!=_catalogs) {Warn("Downloaded catalog inventory changed during observation.");_catalogs=catalogs;}
        }
        public void Dispose() {foreach(var file in _files) file.Dispose();_files.Clear();foreach(var file in _cacheFiles) file.Dispose();_cacheFiles.Clear();_cache?.Dispose();}
    }
}
