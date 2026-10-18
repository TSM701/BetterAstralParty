#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating {
    // Reviewed incoming inspector only. Hold journal, backup, target and parent handles
    // through mutation and the final journal rename. Never reopen a verified backup by path.
    internal static class OrdinaryInstallRecovery {
        private sealed class Entry {
            internal string Relative="", Hash="",Expected="";
            internal bool Existed, Restore, Created, Remove;
            internal WindowsFileFence.FileLease? Target, Backup;
        }
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.RecoveryRequired); }
        private static string Text(Dictionary<string,object?> obj,string key) {
            object? value;if(!obj.TryGetValue(key,out value) || !(value is string))throw Bad();return (string)value;
        }
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence f,string relative,bool mutable=false) {
            try {return f.OpenFile(relative,mutable);}catch(ApplySafetyException e) when(e.NativeError==2 || e.NativeError==3){return null;}
        }
        private static string Relative(WindowsFileFence f,string absolute) {
            if(WindowsPath.Absolute(absolute)!=absolute || !absolute.StartsWith(f.Root+"\\",StringComparison.OrdinalIgnoreCase))throw Bad();
            var relative=absolute.Substring(f.Root.Length+1).Replace('\\','/');if(f.Full(relative)!=absolute)throw Bad();return relative;
        }
        private static string BackupLeaf(string journalRoot,string backupDir) {
            var leaf=Path.GetFileName(backupDir);
            if(!Regex.IsMatch(leaf,@"\A[0-9a-f]{32}\z") || backupDir!=Path.Combine(journalRoot,leaf))throw Bad();
            return leaf;
        }
        private static void Parents(WindowsFileFence f,string relative) {
            var parts=relative.Split('/');var parent="";
            for(var i=0;i<parts.Length-1;i++) {
                parent+=(parent.Length==0?"":"/")+parts[i];
                try {f.DirectoryIdentity(parent);}catch(ApplySafetyException e) when(e.NativeError==2 || e.NativeError==3){f.CreatePrivateDirectory(parent);}
            }
        }
        // Used only after the uninstaller has previewed and backed up the exact fixed owned set.
        // Protocol-4 (1.3.x) executables and helper receipt, removed when 1.4.0 takes over or uninstalls.
        internal const string LegacyLauncher = "BetterAstralParty-Launcher.exe", LegacyHelper = "BetterAstralParty-UpdateHelper.exe", LegacyHelperReceipt = "BetterAstralParty.helper.receipt";
        internal sealed class OwnedRemovalLease : IDisposable {
            private readonly List<IDisposable> _leases=new List<IDisposable>();
            private readonly Dictionary<string,Entry> _entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
            private WindowsFileFence? _game,_backups;
            private WindowsFileFence.OrdinaryJournal? _mapped;
            private WindowsFileFence.FileLease? _journal;
            private WindowsProcessGuard.LaunchLease? _processes;
            private string[] _guardImages=Array.Empty<string>();
            private readonly string _journalHash;private readonly bool _allowLoader;
            private bool _disposed;
            private static bool Owned(string relative,bool loader) {
                foreach(var name in InstallReceipt.Owned)if(name==relative)return true;
                foreach(var name in new[]{
                    InstallReceipt.Path,LegacyLauncher,LegacyHelper,LegacyHelperReceipt,
                    "BepInEx/config/kr.betterastralparty.mod.cfg",
                    "BepInEx/BetterAstralParty-Diagnostics/current.log",
                    "BepInEx/BetterAstralParty-Diagnostics/previous.log",
                    "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log",
                    "BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log",
                    "BetterAstralParty-Compatibility.log","BetterAstralParty-LaunchError.log",ChannelTransitionIntent.AdoptionPath})
                    if(name==relative)return true;
                return loader && relative=="doorstop_config.ini";
            }
            internal OwnedRemovalLease(string root,string journalRoot,string journalHash,string[] allowedTargets,string rootIdentity,bool allowLoader) {
                _journalHash=journalHash;_allowLoader=allowLoader;
                if(allowedTargets==null || allowedTargets.Length<1 || allowedTargets.Length>300 || !Regex.IsMatch(journalHash,@"\A[A-F0-9]{64}\z"))throw Bad();
                try {
                    var canonical=WindowsPath.Absolute(root).TrimEnd('\\');var local=Environment.GetEnvironmentVariable("LOCALAPPDATA");
                    if(string.IsNullOrEmpty(local))throw Bad();
                    var rootKey=UpdateTrust.Hash(Encoding.UTF8.GetBytes(canonical.ToLowerInvariant()));
                    if(journalRoot!=WindowsPath.Absolute(Path.Combine(local,"BetterAstralParty","Backups","v5",rootKey)))throw Bad();
                    _mapped=new WindowsFileFence.OrdinaryJournal(journalRoot);_leases.Add(_mapped);
                    _game=new WindowsFileFence(canonical);_leases.Add(_game);
                    _backups=new WindowsFileFence(Path.GetDirectoryName(_mapped.PhysicalPath)!);_leases.Add(_backups);
                    _journal=_backups.OpenFile("pending.json",true);_leases.Add(_journal);
                    if(!_mapped.Identity.Same(_journal.Identity) || Path.GetFileName(_mapped.PhysicalPath)!="pending.json" ||
                        _game.RootIdentity.Text!=rootIdentity || _journal.Hash()!=journalHash)throw Bad();
                    _mapped.Recheck(_backups.Full("pending.json"));
                    if(_game.Root.Equals(_backups.Root,StringComparison.OrdinalIgnoreCase) || _game.Root.StartsWith(_backups.Root+"\\",StringComparison.OrdinalIgnoreCase) ||
                        _backups.Root.StartsWith(_game.Root+"\\",StringComparison.OrdinalIgnoreCase))throw Bad();
                    UpdateLaunchGate.Check(_game);
                    _guardImages=new[]{_game.Full("AstralParty_INT.exe"),_game.Full(LegacyLauncher),_game.Full(LegacyHelper)};
                    WindowsProcessGuard.AssertNoRunning(_guardImages);
                    // Legacy launcher/helper are mutable removal targets; their exclusive target leases close their open/start race.
                    _processes=WindowsProcessGuard.Acquire(_game,new[]{"AstralParty_INT.exe"},allowMissing:true);_leases.Add(_processes);
                    var state=new InstallReceipt.Json(UpdateTicket.Read(_journal,1024*1024),1024*1024,300,WindowsPath.MaximumLength).Parse() as Dictionary<string,object?>;
                    if(state==null || state.Count!=3 || Text(state,"GameRoot")!=_game.Root)throw Bad();
                    var backupDir=Text(state,"BackupDir");var leaf=BackupLeaf(journalRoot,backupDir);_backups.DirectoryIdentity(leaf);
                    object? value;if(!state.TryGetValue("Entries",out value))throw Bad();var rows=value as List<object?>;
                    if(rows==null || rows.Count<1 || rows.Count>300)throw Bad();
                    var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var path in allowedTargets) {
                        var relative=Relative(_game,path);
                        if(!Owned(relative,allowLoader) || !allowed.Add(relative))throw Bad();
                    }
                    for(var i=0;i<rows.Count;i++) {
                        var row=rows[i] as Dictionary<string,object?>;
                        if(row==null || row.Count!=4 || !row.ContainsKey("Existed") || !(row["Existed"] is bool) || !row.ContainsKey("Hash"))throw Bad();
                        var relative=Relative(_game,Text(row,"Target"));
                        if(!allowed.Contains(relative) || _entries.ContainsKey(relative) ||
                            Text(row,"Backup")!=Path.Combine(backupDir,i.ToString(CultureInfo.InvariantCulture)+".bin"))throw Bad();
                        var entry=new Entry {Relative=relative,Existed=(bool)row["Existed"]!};
                        _entries.Add(relative,entry);
                        entry.Backup=Maybe(_backups,leaf+"/"+i.ToString(CultureInfo.InvariantCulture)+".bin");if(entry.Backup!=null)_leases.Add(entry.Backup);
                        entry.Target=Maybe(_game,relative,true);if(entry.Target!=null)_leases.Add(entry.Target);
                        if(entry.Existed) {
                            if(!(row["Hash"] is string))throw Bad();entry.Hash=(string)row["Hash"]!;
                            if(!Regex.IsMatch(entry.Hash,@"\A[A-F0-9]{64}\z") || entry.Backup==null || entry.Backup.Hash()!=entry.Hash ||
                                entry.Target==null || entry.Target.Hash()!=entry.Hash)throw Bad();
                        } else {
                            if(row["Hash"]!=null || entry.Backup!=null || entry.Target!=null)throw Bad();
                            Parents(_game,relative);entry.Target=_game.OpenFile(relative,true,true);entry.Created=true;_leases.Add(entry.Target);
                        }
                    }
                    if(_entries.Count!=allowed.Count)throw Bad();
                    Recheck();
                } catch {Dispose();throw;}
            }
            private void Recheck() {
                if(_disposed || _game==null || _backups==null || _mapped==null || _journal==null || _processes==null)throw Bad();
                _game.Recheck();_backups.Recheck();_mapped.Recheck(_backups.Full("pending.json"));_processes.Recheck();
                WindowsProcessGuard.AssertNoRunning(_guardImages);
                if(_journal.Hash()!=_journalHash)throw Bad();
                foreach(var entry in _entries.Values) {
                    if(entry.Existed && entry.Backup!.Hash()!=entry.Hash)throw Bad();
                    entry.Target?.Recheck();
                }
            }
            private Entry Target(string absolute) {
                Recheck();if(_game==null)throw Bad();var relative=Relative(_game,absolute);
                if(!_entries.TryGetValue(relative,out var entry) || entry.Target==null)throw Bad();
                return entry;
            }
            public void Remove(string absolute) {
                var entry=Target(absolute);
                var target=entry.Target??throw Bad();
                if(entry.Relative=="doorstop_config.ini")throw Bad();
                if(entry.Existed && target.Hash()!=entry.Hash || !entry.Existed && target.Stream.Length!=0)throw Bad();
                target.RemoveOrdinaryRecoveryTarget(entry.Relative);
                target.Dispose();entry.Target=null;entry.Created=false;entry.Remove=true;
            }
            public void WriteLoader(string absolute,string next) {
                var entry=Target(absolute);
                var target=entry.Target??throw Bad();
                if(!_allowLoader || entry.Relative!="doorstop_config.ini" || !entry.Existed || target.Hash()!=entry.Hash)throw Bad();
                var old=new UTF8Encoding(false,true).GetString(UpdateTicket.Read(target,1024*1024)).TrimStart('\uFEFF');
                const string pattern=@"(?m)^enabled\s*=\s*(true|false)\s*$";
                if(Regex.Matches(old,pattern).Count!=1 ||
                    next!=Regex.Replace(old,pattern,"enabled=true") && next!=Regex.Replace(old,pattern,"enabled=false"))throw Bad();
                var bytes=new UTF8Encoding(false,true).GetBytes(next);
                target.Stream.Position=0;target.Stream.Write(bytes,0,bytes.Length);target.Stream.SetLength(bytes.Length);target.Flush();
                if(target.Hash()!=UpdateTrust.Hash(bytes))throw Bad();
            }
            public void Dispose() {
                if(_disposed)return;
                foreach(var entry in _entries.Values)if(entry.Created && entry.Target!=null)
                    try {entry.Target.RemoveOrdinaryRecoveryTarget(entry.Relative);}catch(ApplySafetyException){ }
                for(var i=_leases.Count-1;i>=0;i--)_leases[i].Dispose();
                _leases.Clear();_disposed=true;
            }
        }
        internal static void Run(string root,string journalRoot,string expectedJournalHash,string[] allowedTargets,string? expectedCurrentJson,bool completeOnly,string expectedRootIdentity,Action<string>? boundary=null,UpdateTrust? trust=null
#if BAP_SAFETY_FIXTURE
            ,Action<string>? gameOperation=null
#endif
        ) {
            if(allowedTargets==null || allowedTargets.Length<1 || allowedTargets.Length>300 || !Regex.IsMatch(expectedJournalHash,@"\A[A-F0-9]{64}\z"))throw Bad();
            var canonical=WindowsPath.Absolute(root).TrimEnd('\\');
            var local=Environment.GetEnvironmentVariable("LOCALAPPDATA");if(string.IsNullOrEmpty(local))throw Bad();
            var rootKey=UpdateTrust.Hash(Encoding.UTF8.GetBytes(canonical.ToLowerInvariant()));
            if(journalRoot!=WindowsPath.Absolute(Path.Combine(local,"BetterAstralParty","Backups","v5",rootKey)))throw Bad();
            using(var mapped=new WindowsFileFence.OrdinaryJournal(journalRoot))
            using(var game=new WindowsFileFence(canonical
#if BAP_SAFETY_FIXTURE
                ,gameOperation
#endif
            ))using(var backups=new WindowsFileFence(Path.GetDirectoryName(mapped.PhysicalPath)!))using(var journal=backups.OpenFile("pending.json",true)) {
                if(!mapped.Identity.Same(journal.Identity) || Path.GetFileName(mapped.PhysicalPath)!="pending.json")throw Bad();
                mapped.Recheck(backups.Full("pending.json"));
                if(game.RootIdentity.Text!=expectedRootIdentity)throw Bad();
                if(game.Root.Equals(backups.Root,StringComparison.OrdinalIgnoreCase) || game.Root.StartsWith(backups.Root+"\\",StringComparison.OrdinalIgnoreCase) || backups.Root.StartsWith(game.Root+"\\",StringComparison.OrdinalIgnoreCase))throw Bad();
                if(journal.Hash()!=expectedJournalHash)throw Bad();
                var state=new InstallReceipt.Json(UpdateTicket.Read(journal,1024*1024),1024*1024,300,WindowsPath.MaximumLength).Parse() as Dictionary<string,object?>;
                if(state==null || state.Count!=3 || Text(state,"GameRoot")!=game.Root)throw Bad();
                var backupDir=Text(state,"BackupDir");var backupLeaf=BackupLeaf(journalRoot,backupDir);
                if(!Regex.IsMatch(backupLeaf,@"\A[0-9a-f]{32}\z"))throw Bad();backups.DirectoryIdentity(backupLeaf);
                object? rowsValue;if(!state.TryGetValue("Entries",out rowsValue))throw Bad();var rows=rowsValue as List<object?>;
                if(rows==null || rows.Count<1 || rows.Count>300)throw Bad();
                var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var path in allowedTargets)if(!allowed.Add(Relative(game,path)))throw Bad();
                Dictionary<string,object?>? expected=null;
                if(!string.IsNullOrEmpty(expectedCurrentJson)) {
                    expected=new InstallReceipt.Json(Encoding.UTF8.GetBytes(expectedCurrentJson!),1024*1024,300,WindowsPath.MaximumLength).Parse() as Dictionary<string,object?>;
                    if(expected==null)throw Bad();
                    foreach(var pair in expected)if(!allowed.Contains(Relative(game,pair.Key)) || pair.Value!=null && (!(pair.Value is string) || !Regex.IsMatch((string)pair.Value,@"\A[A-F0-9]{64}\z")))throw Bad();
                }
                var entries=new List<Entry>();var leases=new List<IDisposable>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var mutationStarted=false;
                try {
                    for(var i=0;i<rows.Count;i++) {
                        var row=rows[i] as Dictionary<string,object?>;
                        if(row==null || row.Count!=4 || !row.ContainsKey("Existed") || !(row["Existed"] is bool) || !row.ContainsKey("Hash"))throw Bad();
                        var absolute=Text(row,"Target");var relative=Relative(game,absolute);
                        if(!allowed.Contains(relative) || !seen.Add(relative))throw Bad();
                        var backup=backupLeaf+"/"+i.ToString(CultureInfo.InvariantCulture)+".bin";
                        if(Text(row,"Backup")!=Path.Combine(backupDir,i.ToString(CultureInfo.InvariantCulture)+".bin"))throw Bad();
                        var e=new Entry {Relative=relative,Existed=(bool)row["Existed"]!};
                        e.Target=Maybe(game,relative,true);if(e.Target!=null)leases.Add(e.Target);
                        e.Backup=Maybe(backups,backup);if(e.Backup!=null)leases.Add(e.Backup);
                        if(e.Existed) {
                            if(!(row["Hash"] is string))throw Bad();e.Hash=(string)row["Hash"]!;e.Expected=e.Hash;
                            if(!Regex.IsMatch(e.Hash,@"\A[A-F0-9]{64}\z") || e.Backup==null || e.Backup.Hash()!=e.Hash)throw Bad();
                        } else if(row["Hash"]!=null || e.Backup!=null)throw Bad();
                        var current=e.Target?.Hash();var oldMatches=e.Existed?current==e.Hash:current==null;
                        e.Restore=!completeOnly && e.Existed && !oldMatches;
                        e.Remove=!completeOnly && !e.Existed && e.Target!=null;
                        entries.Add(e);
                    }
                    foreach(var e in entries)if(!completeOnly && expected!=null) {
                        var current=e.Target?.Hash();var oldMatches=e.Existed?current==e.Hash:current==null;
                        if(!oldMatches) {
                            object? expectedHash;if(!expected.TryGetValue(game.Full(e.Relative),out expectedHash) || !Equals(expectedHash,current))throw Bad();
                        }
                    }
                    boundary?.Invoke("ordinary-handles-pinned");
                    // CreateNew is the absence fence: an intervening object causes refusal.
                    // New empty parents may remain, just as in the existing restore contract.
                    foreach(var e in entries)if(e.Target==null) {
                        Parents(game,e.Relative);e.Target=game.OpenFile(e.Relative,true,true);e.Created=true;leases.Add(e.Target);
                        if(completeOnly || !e.Existed)e.Remove=true;
                    }
                    if(journal.Hash()!=expectedJournalHash)throw Bad();
                    foreach(var e in entries) {e.Target!.Recheck();if(e.Existed && e.Backup!.Hash()!=e.Hash)throw Bad();}
                    boundary?.Invoke("ordinary-before-mutation");
                    foreach(var e in entries) {e.Target!.Recheck();if(e.Existed && e.Backup!.Hash()!=e.Hash)throw Bad();}
                    if(journal.Hash()!=expectedJournalHash)throw Bad();mutationStarted=true;
                    foreach(var e in entries) {
                        if(e.Restore) {
                            var source=e.Backup??throw Bad();var target=e.Target??throw Bad();
                            source.Recheck();source.Stream.Position=0;target.Recheck();target.Stream.Position=0;
                            source.Stream.CopyTo(target.Stream);target.Stream.SetLength(source.Stream.Length);target.Flush();
                            if(target.Hash()!=e.Hash)throw Bad();
                        }
                        if(e.Remove)e.Target!.RemoveOrdinaryRecoveryTarget(e.Relative);
                        boundary?.Invoke("ordinary-restored:"+e.Relative);
                    }
                    if(journal.Hash()!=expectedJournalHash)throw Bad();
                    boundary?.Invoke("ordinary-before-journal-retire");
                    foreach(var e in entries)if(!e.Remove) {
                        var target=e.Target??throw Bad();target.Recheck();if(!completeOnly && e.Existed && target.Hash()!=e.Expected)throw Bad();
                    }
                    game.Recheck();backups.Recheck();mapped.Recheck(backups.Full("pending.json"));if(journal.Hash()!=expectedJournalHash)throw Bad();
                    journal.RenameTo(backups,backupLeaf+(completeOnly?"/completed.json":"/restored.json"));journal.Flush();
                    mapped.Recheck(backups.Full(backupLeaf+(completeOnly?"/completed.json":"/restored.json")));
                    boundary?.Invoke("ordinary-after-journal-retire");
                } finally {
                    // A preflight failure may clean up only its still-exclusively-held empty reservations.
                    if(!mutationStarted)foreach(var e in entries)if(e.Created && e.Target!=null)try{e.Target.RemoveOrdinaryRecoveryTarget(e.Relative);}catch(ApplySafetyException){ }
                    for(var i=leases.Count-1;i>=0;i--)leases[i].Dispose();
                }
            }
        }
    }
}
