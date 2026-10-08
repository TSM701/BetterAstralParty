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
            internal string Relative="", Hash="";
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
            if(Path.GetFullPath(absolute)!=absolute || !absolute.StartsWith(f.Root+"\\",StringComparison.OrdinalIgnoreCase))throw Bad();
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
        internal static void Run(string root,string journalRoot,string expectedJournalHash,string[] allowedTargets,string? expectedCurrentJson,bool completeOnly,string expectedRootIdentity,Action<string>? boundary=null) {
            if(allowedTargets==null || allowedTargets.Length<1 || allowedTargets.Length>300 || !Regex.IsMatch(expectedJournalHash,@"\A[A-F0-9]{64}\z"))throw Bad();
            var canonical=Path.GetFullPath(root).TrimEnd('\\');
            var local=Environment.GetEnvironmentVariable("LOCALAPPDATA");if(string.IsNullOrEmpty(local))throw Bad();
            var rootKey=UpdateTrust.Hash(Encoding.UTF8.GetBytes(canonical.ToLowerInvariant()));
            if(journalRoot!=Path.GetFullPath(Path.Combine(local,"BetterAstralParty","Backups",rootKey)))throw Bad();
            using(var mapped=new WindowsFileFence.OrdinaryJournal(journalRoot))
            using(var game=new WindowsFileFence(canonical))using(var backups=new WindowsFileFence(Path.GetDirectoryName(mapped.PhysicalPath)!))using(var journal=backups.OpenFile("pending.json",true)) {
                if(!mapped.Identity.Same(journal.Identity) || Path.GetFileName(mapped.PhysicalPath)!="pending.json")throw Bad();
                mapped.Recheck(backups.Full("pending.json"));
                if(game.RootIdentity.Text!=expectedRootIdentity)throw Bad();
                if(game.Root.Equals(backups.Root,StringComparison.OrdinalIgnoreCase) || game.Root.StartsWith(backups.Root+"\\",StringComparison.OrdinalIgnoreCase) || backups.Root.StartsWith(game.Root+"\\",StringComparison.OrdinalIgnoreCase))throw Bad();
                if(journal.Hash()!=expectedJournalHash)throw Bad();
                var state=new InstallReceipt.Json(UpdateTicket.Read(journal,1024*1024),1024*1024,300,1024).Parse() as Dictionary<string,object?>;
                if(state==null || state.Count!=3 || Text(state,"GameRoot")!=game.Root)throw Bad();
                var backupDir=Text(state,"BackupDir");var backupLeaf=BackupLeaf(journalRoot,backupDir);
                if(!Regex.IsMatch(backupLeaf,@"\A[0-9a-f]{32}\z"))throw Bad();backups.DirectoryIdentity(backupLeaf);
                object? rowsValue;if(!state.TryGetValue("Entries",out rowsValue))throw Bad();var rows=rowsValue as List<object?>;
                if(rows==null || rows.Count<1 || rows.Count>300)throw Bad();
                var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var path in allowedTargets)if(!allowed.Add(Relative(game,path)))throw Bad();
                Dictionary<string,object?>? expected=null;
                if(!string.IsNullOrEmpty(expectedCurrentJson)) {
                    expected=new InstallReceipt.Json(Encoding.UTF8.GetBytes(expectedCurrentJson!),1024*1024,300,1024).Parse() as Dictionary<string,object?>;
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
                            if(!(row["Hash"] is string))throw Bad();e.Hash=(string)row["Hash"]!;
                            if(!Regex.IsMatch(e.Hash,@"\A[A-F0-9]{64}\z") || e.Backup==null || e.Backup.Hash()!=e.Hash)throw Bad();
                        } else if(row["Hash"]!=null || e.Backup!=null)throw Bad();
                        var current=e.Target?.Hash();var oldMatches=e.Existed?current==e.Hash:current==null;
                        if(!completeOnly && !oldMatches && expected!=null) {
                            object? expectedHash;if(!expected.TryGetValue(absolute,out expectedHash) || !Equals(expectedHash,current))throw Bad();
                        }
                        e.Restore=!completeOnly && e.Existed && !oldMatches;
                        e.Remove=!completeOnly && !e.Existed && e.Target!=null;
                        entries.Add(e);
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
                        var target=e.Target??throw Bad();target.Recheck();if(!completeOnly && e.Existed && target.Hash()!=e.Hash)throw Bad();
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
