#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating
{
    // Signed incoming baseline, read-only game/loader/cache leases retained through the transaction.
    internal sealed class UpdateCompatibility : IDisposable
    {
        private readonly List<WindowsFileFence.FileLease> _files=new List<WindowsFileFence.FileLease>();
        private readonly WindowsFileFence _cache;
        private string _catalogs="";
        private UpdateCompatibility(string cacheRoot) { _cache=new WindowsFileFence(cacheRoot); }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.CompatibilityRequired); }
        private static string Text(Dictionary<string,object?> value,string name) {
            object? result; if(!value.TryGetValue(name,out result) || !(result is string)) throw Bad(); return (string)result;
        }
        private string Catalogs() {
            var paths=Directory.GetFiles(_cache.Root,"*",SearchOption.TopDirectoryOnly); if(paths.Length>512) throw Bad();
            var names=new List<string>(); foreach(var path in paths) {
                var name=Path.GetFileName(path); if(Regex.IsMatch(name,@"\Acatalog.*\.(json|hash)\z",RegexOptions.IgnoreCase)) names.Add(name);
            }
            names.Sort(StringComparer.Ordinal); return string.Join("|",names);
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
                var json=new InstallReceipt.Json(manifest,65536,128,1024).Parse() as Dictionary<string,object?>;
                if(json==null || !json.ContainsKey("schema") || !Equals(json["schema"],1) || !json.ContainsKey("files") || !json.ContainsKey("cacheCatalogs")) throw Bad();
                var entries=json["files"] as List<object?>;var catalogs=json["cacheCatalogs"] as List<object?>;
                if(entries==null || entries.Count<6 || catalogs==null) throw Bad();
                var expected=new List<string>();foreach(var catalog in catalogs) {
                    var name=catalog as string;if(name==null || !Regex.IsMatch(name,@"\Acatalog[^/\\:]*\.(json|hash)\z") || expected.Contains(name)) throw Bad();expected.Add(name);
                }
                expected.Sort(StringComparer.Ordinal);check._catalogs=string.Join("|",expected);if(check.Catalogs()!=check._catalogs) throw Bad();
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var raw in entries) {
                    var entry=raw as Dictionary<string,object?>;if(entry==null || entry.Count!=4) throw Bad();
                    var scope=Text(entry,"root");var path=Text(entry,"path");var hash=Text(entry,"sha256");Text(entry,"kind");
                    if((scope!="game" && scope!="cache") || !Regex.IsMatch(hash,@"\A[0-9A-Fa-f]{64}\z") || !seen.Add(scope+"/"+path) || scope=="game" && UpdateTrust.OwnedPath(path)) throw Bad();
                    var fence=scope=="game" ? game : check._cache;fence.Full(path);
                    WindowsFileFence.FileLease file;
                    if(scope=="game" && path=="AstralParty_INT.exe") file=gameImage;
                    else {file=fence.OpenFile(path);check._files.Add(file);}
                    if(file.Stream.Length>512L*1024*1024) throw Bad();
                    if(scope=="game" && path=="AstralParty_INT_Data/data.unity3d") {
                        var header=new byte[8];file.Stream.Position=0;
                        if(file.Stream.Length<32 || file.Stream.Read(header,0,8)!=8 || Encoding.ASCII.GetString(header)!="UnityFS\0") throw Bad();
                        // Existing resource-only advisory policy; executable/loader/catalog hashes remain mandatory.
                    } else if(file.Hash()!=hash.ToUpperInvariant()) throw Bad();
                }
                foreach(var required in new[]{"game/AstralParty_INT.exe","game/GameAssembly.dll","game/UnityPlayer.dll","game/winhttp.dll","game/AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle","game/AstralParty_INT_Data/data.unity3d","game/BepInEx/core/BepInEx.Core.dll"}) if(!seen.Contains(required)) throw Bad();
                foreach(var name in expected) if(!seen.Contains("cache/"+name)) throw Bad();
                check.Recheck();return check;
            } catch {check?.Dispose();throw Bad();}
        }
        internal void Recheck() {foreach(var file in _files) file.Recheck();_cache.Recheck();if(Catalogs()!=_catalogs) throw Bad();}
        public void Dispose() {foreach(var file in _files) file.Dispose();_files.Clear();_cache.Dispose();}
    }
}
