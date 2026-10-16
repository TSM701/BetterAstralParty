using BetterAstralParty.Updating;
using BetterAstralParty.Observability;
using BetterAstralParty.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class OrdinaryHelperRecoveryFixture {
    private static string[] WorkDirectories(string root, string kind)
    { using var fence = new WindowsFileFence(root); var paths = new System.Collections.Generic.List<string>(); foreach (var relative in UpdateWorkPath.Directories(fence, kind)) paths.Add(fence.Full(relative)); return paths.ToArray(); }
    private static string WorkRelative(string root, string path) => path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');

    internal static RSAParameters Public;
    private static RSA Key=null!;private static UpdateTrust Trust=null!;
    private static string Stage="",SavedLocal="";private static int Sequence,Assertions;
    private static readonly List<string> Cases=new();
    private static int ObservedInterruptions,PlanlessWorkDirectories;
    private static byte[] Image=null!,Descriptor=null!,Signature=null!;
    private sealed class State {
        internal string Root="",JournalRoot="",Leaf="",JournalHash="",RootId="",Work="";
        internal string[] Targets=Array.Empty<string>();internal byte[] OriginalReceipt=Array.Empty<byte>();
        internal Dictionary<string,string> Retained=new();internal string ImageId="",ReceiptId="";
    }
    private static byte[] Bytes(string text)=>Encoding.ASCII.GetBytes(text);
    private static void Check(bool condition,string message) {Assertions++;if(!condition)throw new InvalidOperationException(message);}
    private static void Write(WindowsFileFence f,string path,byte[] bytes) {using var file=f.OpenFile(path,true,true);file.Stream.Write(bytes);file.Flush();}
    private static byte[] Read(WindowsFileFence f,string path) {using var file=f.OpenFile(path);return UpdateTicket.Read(file,UpdateTrust.MaxFileBytes);}
    private static State Seed(string deletion="both") {
        var s=new State {Root=Path.Combine(Stage,"g"+(++Sequence))};Directory.CreateDirectory(s.Root);
        using(var f=new WindowsFileFence(s.Root)) {
            s.RootId=f.RootIdentity.Text;s.Work=UpdateWorkPath.New("helper");var workId=UpdateWorkPath.Create(f, s.Work).Text;
            Write(f,HelperDescriptor.Image,Image);using(var image=f.OpenFile(HelperDescriptor.Image))s.ImageId=image.Identity.Text;
            var verified=HelperDescriptor.Verify(Trust,Descriptor,DetachedSignature.Parse(Signature),Image);
            s.OriginalReceipt=HelperDelivery.WholeReceiptBytes(f,verified,s.ImageId);Write(f,HelperDelivery.Receipt,s.OriginalReceipt);
            using(var receipt=f.OpenFile(HelperDelivery.Receipt))s.ReceiptId=receipt.Identity.Text;
            var plan=Bytes("BetterAstralParty.HelperInstall/v1\nroot="+s.RootId+"\nwork="+s.Work+"\nwork-id="+workId+"\ndescriptor="+verified.DescriptorHash+"\nimage-id="+s.ImageId+"\nreceipt-id="+s.ReceiptId+"\n");
            Write(f,s.Work+"/descriptor",Descriptor);Write(f,s.Work+"/signature",Signature);Write(f,s.Work+"/plan",plan);Write(f,s.Work+"/committed",Bytes(UpdateTrust.Hash(plan)+"\n"));
            foreach(var name in new[]{"descriptor","signature","plan","committed"})using(var file=f.OpenFile(s.Work+"/"+name))s.Retained[s.Work+"/"+name]=file.Hash();
            Write(f,"other-mod.dll",Bytes("unrelated user mod"));
            Write(f,"AstralParty_INT.exe",Image);
            f.CreatePrivateDirectory("AstralParty_INT_Data");
            Check(HelperDelivery.Fingerprint(f,Trust)==UpdateTrust.Hash(Image),"Seed uses authentic signed exact-ID delivery.");
        }
        Journal(s);
        if(deletion!="none") {
            using var removal=new OrdinaryInstallRecovery.OwnedRemovalLease(s.Root,s.JournalRoot,s.JournalHash,s.Targets,s.RootId,false);
            if(deletion=="both" || deletion=="image")removal.Remove(s.Targets[0]);
            if(deletion=="both" || deletion=="receipt")removal.Remove(s.Targets[1]);
        }
        return s;
    }
    private static void Journal(State s) {
        var key=UpdateTrust.Hash(Encoding.UTF8.GetBytes(s.Root.ToLowerInvariant()));
        s.JournalRoot=Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!,"BetterAstralParty","Backups","v4",key);Directory.CreateDirectory(s.JournalRoot);
        s.Targets=new[]{Path.Combine(s.Root,HelperDescriptor.Image),Path.Combine(s.Root,HelperDelivery.Receipt)};s.Leaf=Guid.NewGuid().ToString("N");
        using var backups=new WindowsFileFence(s.JournalRoot);backups.CreatePrivateDirectory(s.Leaf);
        using var game=new WindowsFileFence(s.Root);var rows=new List<object>();
        for(var i=0;i<2;i++) {
            var bytes=Read(game,i==0?HelperDescriptor.Image:HelperDelivery.Receipt);Write(backups,s.Leaf+"/"+i+".bin",bytes);
            rows.Add(new {Target=s.Targets[i],Backup=Path.Combine(s.JournalRoot,s.Leaf,i+".bin"),Existed=true,Hash=UpdateTrust.Hash(bytes)});
        }
        var journal=JsonSerializer.SerializeToUtf8Bytes(new {GameRoot=s.Root,BackupDir=Path.Combine(s.JournalRoot,s.Leaf),Entries=rows});Write(backups,"pending.json",journal);s.JournalHash=UpdateTrust.Hash(journal);
    }
    private static void Run(State s,Action<string>? boundary=null,bool complete=false,Action<string>? gameOperation=null) {
        OrdinaryInstallRecovery.Run(s.Root,s.JournalRoot,s.JournalHash,s.Targets,null,complete,s.RootId,boundary,Trust,gameOperation);
    }
    private static void OtherState(State s,string? hash) {
        using var f=new WindowsFileFence(s.Root);WindowsFileFence.FileLease held;
        try {held=f.OpenFile("BetterAstralParty.update.lock",true);}catch(ApplySafetyException e)when(e.NativeError==2){held=f.OpenFile("BetterAstralParty.update.lock",true,true);}
        using var retained=held;
        var method=typeof(WholeInstallBridge).GetMethod("OtherState",BindingFlags.NonPublic|BindingFlags.Static)!;
        var pending=Activator.CreateInstance(method.GetParameters()[1].ParameterType);
        try {method.Invoke(null,new object?[]{f,pending,hash});}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();}
    }
    private static void Refuse(Action action,string message) {
        try {action();}catch(ApplySafetyException e)when(e.Failure==ApplyFailure.RecoveryRequired){Assertions++;return;}
        throw new InvalidOperationException(message);
    }
    private static void Verify(State s,bool idsChanged) {
        using var f=new WindowsFileFence(s.Root);
        Check(HelperDelivery.Fingerprint(f,Trust)==UpdateTrust.Hash(Image),"Recovered helper authenticates through unchanged live object-ID rules.");
        using(var image=f.OpenFile(HelperDescriptor.Image))using(var receipt=f.OpenFile(HelperDelivery.Receipt)) {
            var expected=HelperDelivery.WholeReceiptBytes(f,HelperDescriptor.Verify(Trust,Descriptor,DetachedSignature.Parse(Signature),Image),image.Identity.Text);
            Check(UpdateTrust.Hash(expected)==receipt.Hash(),"Canonical receipt binds the restored image ID and original signed descriptor/key.");
            if(idsChanged)Check(image.Identity.Text!=s.ImageId || receipt.Identity.Text!=s.ReceiptId,"Native deletion and restore changed physical IDs.");
            Check(HelperDelivery.RemovedEvidence(f,Trust,Read(f,HelperDescriptor.Image),Read(f,HelperDelivery.Receipt)).StartsWith("BetterAstralParty.RemovedHelperEvidence/v1\n",StringComparison.Ordinal),"Copied provenance remains verifiable after repeated equivalent plans.");
        }
        foreach(var pair in s.Retained)using(var file=f.OpenFile(pair.Key))Check(file.Hash()==pair.Value,"Original signed delivery evidence is immutable.");
        Check(Encoding.ASCII.GetString(Read(f,"other-mod.dll"))=="unrelated user mod","Unrelated mod bytes retained.");
        using var backups=new WindowsFileFence(s.JournalRoot);
        Check(UpdateTrust.Hash(Read(backups,s.Leaf+"/0.bin"))==UpdateTrust.Hash(Image),"Original helper backup bytes retained.");
        Check(UpdateTrust.Hash(Read(backups,s.Leaf+"/1.bin"))==UpdateTrust.Hash(s.OriginalReceipt),"Original receipt backup bytes retained.");
        using(var journal=backups.OpenFile(s.Leaf+"/restored.json"))Check(journal.Hash()==s.JournalHash,"Original journal retired unchanged only after helper authentication.");
        UpdateLaunchGate.Check(f);Assertions++;
    }
    private static void Basic() {
        foreach(var deletion in new[]{"both","image","receipt","none"}) {
            var s=Seed(deletion);var before=WorkDirectories(s.Root, "helper").Length;Run(s);Verify(s,deletion!="none");
            if(deletion=="none")Check(WorkDirectories(s.Root, "helper").Length==before,"Already authenticated rollback creates no helper repair.");
            Cases.Add("native-restore:"+deletion);
        }
        var repeated=Seed("receipt");Run(repeated);Verify(repeated,true);
        using(var game=new WindowsFileFence(repeated.Root))repeated.OriginalReceipt=Read(game,HelperDelivery.Receipt);
        Journal(repeated);
        using(var removal=new OrdinaryInstallRecovery.OwnedRemovalLease(repeated.Root,repeated.JournalRoot,repeated.JournalHash,repeated.Targets,repeated.RootId,false))removal.Remove(repeated.Targets[1]);
        Run(repeated);Verify(repeated,true);Cases.Add("receipt-only-repair:subsequent-removal-and-recovery");
    }
    private static void RepeatedFirstMarker(bool legacy,bool nativeRename=false) {
        var s=Seed();ObservedInterruptions=0;
        for(var i=0;i<80;i++) {
            var original=new IOException("inert repeated first marker interruption");var interrupted=false;
            try {
                if(nativeRename)Run(s,gameOperation:operation=>{if(operation=="rename"){interrupted=true;throw original;}});
                else Run(s,b=>{if(b=="before:ordinary-helper-ordinary-repair"){interrupted=true;throw original;}});
            } catch(IOException e) {Check(ReferenceEquals(e,original),"Repeated interruption preserves the original error.");ObservedInterruptions++;}
            catch(ApplySafetyException e)when(legacy && e.Failure==ApplyFailure.RecoveryRequired) {
                PlanlessWorkDirectories=WorkDirectories(s.Root, "helper").Count(d=>!File.Exists(Path.Combine(d,"plan")));
                Check(ObservedInterruptions==65 && PlanlessWorkDirectories==65 && !interrupted,"Original bug reaches the unchanged 64-active cap after exactly 65 legitimate interruptions.");
                Check(File.Exists(Path.Combine(s.JournalRoot,"pending.json")) && UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(s.JournalRoot,"pending.json")))==s.JournalHash,"Repeated bug preserves the exact journal but cannot resume.");
                Cases.Add("confirmed-old-first-marker-work-leak");return;
            }
            Check(interrupted,"Every repeated failure occurs at the requested exact marker boundary.");
            if(!legacy) {
                Check(WorkDirectories(s.Root, "helper").Length==2,"Same exact journal/current IDs reuse one private helper work directory.");
                if(nativeRename)Check(Directory.GetFiles(s.Root,"ordinary-repair-pending-*",SearchOption.AllDirectories).Length==1,"Repeated native rename failure reuses one exact atomic pending object.");
            }
        }
        if(legacy)throw new InvalidOperationException("Expected original cap failure was not reproduced.");
        PlanlessWorkDirectories=WorkDirectories(s.Root, "helper").Count(d=>!File.Exists(Path.Combine(d,"plan")));
        Run(s);Verify(s,true);Cases.Add(nativeRename?"80-first-marker-native-renames:one-work-one-pending-resumed":"80-first-marker-managed-interruptions:one-work-resumed");
    }
    private static void Boundaries() {
        var dry=Seed();var names=new List<string>();Run(dry,names.Add);Verify(dry,true);Check(names.Count==names.Distinct().Count(),"Dry run boundary names are unique.");
        foreach(var deletion in new[]{"both","image","receipt"}) {
            var probe=Seed(deletion);var boundaries=new List<string>();Run(probe,boundaries.Add);
            foreach(var name in boundaries) {
                var s=Seed(deletion);var original=new IOException("inert boundary "+name);var observed=false;
                try {Run(s,b=>{if(b==name){observed=true;throw original;}});}catch(IOException e){Check(ReferenceEquals(e,original),"Original interrupted operation exception preserved.");}
                Check(observed,"Every successful dry-run boundary is exercised.");
                if(!File.Exists(Path.Combine(s.JournalRoot,"pending.json"))) {Verify(s,true);Cases.Add(deletion+":"+name+":already-completed");continue;}
                using(var f=new WindowsFileFence(s.Root)) {
                    if(HelperDelivery.PendingInstall(f)) {
                        Refuse(()=>UpdateLaunchGate.Check(f),"Ordinary launch must not bypass pending helper provenance.");
                        Refuse(()=>OtherState(s,null),"Old general recovery guard must not bypass helper pending.");
                        OtherState(s,s.JournalHash);Assertions++;
                        Refuse(()=>OtherState(s,new string('A',64)),"Mismatched confirmed journal must not resume.");
                    }
                }
                Run(s);Verify(s,true);Cases.Add(deletion+":"+name+":resumed");
            }
        }
    }
    private static State Pending() {
        var s=Seed();try {Run(s,b=>{if(b=="after:ordinary-helper-plan")throw new IOException("inert stop");});}catch(IOException){ }
        using var f=new WindowsFileFence(s.Root);Check(HelperDelivery.PendingInstall(f),"Real repair plan remains pending.");return s;
    }
    private static void Negatives() {
        foreach(var kind in new[]{"journal","backup","object","marker","proof","unrelated-pending"}) {
            var s=Pending();var journal=Path.Combine(s.JournalRoot,"pending.json");var before=File.ReadAllBytes(journal);
            var work=WorkDirectories(s.Root, "helper").Single(d=>File.Exists(Path.Combine(d,"ordinary-repair")));
            if(kind=="journal")File.AppendAllText(journal," ");
            if(kind=="backup")File.WriteAllBytes(Path.Combine(s.JournalRoot,s.Leaf,"1.bin"),Bytes("damaged backup"));
            if(kind=="object") {File.Delete(s.Targets[1]);File.WriteAllBytes(s.Targets[1],s.OriginalReceipt);}
            if(kind=="marker")File.WriteAllText(Path.Combine(work,"ordinary-repair"),"malformed");
            if(kind=="proof")File.WriteAllText(Path.Combine(work,"signature"),"invalid signature");
            if(kind=="unrelated-pending") {
                using var f=new WindowsFileFence(s.Root);var other=UpdateWorkPath.New("helper");var id=UpdateWorkPath.Create(f, other).Text;
                Write(f,other+"/plan",Bytes("BetterAstralParty.HelperInstall/v1\nroot="+s.RootId+"\nwork="+other+"\nwork-id="+id+"\ndescriptor="+UpdateTrust.Hash(Descriptor)+"\nimage-id="+s.ImageId+"\nreceipt-id="+s.ReceiptId+"\n"));
            }
            Refuse(()=>OtherState(s,s.JournalHash),"Uncertain or conflicting recovery evidence must be rejected.");
            Check(File.Exists(journal),"Rejected recovery retains pending journal.");
            if(kind!="journal")Check(File.ReadAllBytes(journal).SequenceEqual(before),"Rejection never normalizes the original journal.");Cases.Add("rejected:"+kind);
        }
        var complete=Seed();Run(complete,complete:true);
        Check(!File.Exists(complete.Targets[0]) && !File.Exists(complete.Targets[1]),"CompleteOnly retains removed target absence.");
        Check(WorkDirectories(complete.Root, "helper").Length==1,"CompleteOnly creates no repair provenance.");Cases.Add("complete-only:no-repair");
        foreach(var target in new[]{"receipt","update-lock"}) {
            var s=Seed("none");var path=target=="receipt"?s.Targets[1]:Path.Combine(s.Root,"BetterAstralParty.update.lock");
            if(target=="update-lock")File.WriteAllText(path,"");
            using var busy=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            try {if(target=="receipt")Run(s);else OtherState(s,s.JournalHash);throw new InvalidOperationException("Expected exclusive sharing refusal.");}
            catch(ApplySafetyException e) {Check(e.Failure==ApplyFailure.FileBusy && e.NativeError==32,"Native sharing refusal is exact, not an arbitrary rejection.");}
            Check(File.Exists(Path.Combine(s.JournalRoot,"pending.json")),"Busy preflight retains original journal.");Cases.Add("native-sharing:"+target);
        }
        foreach(var kind in new[]{"unknown-file","mismatched-atomic-file"}) {
            var s=Seed();var original=new IOException("inert pre-marker stop");
            try {Run(s,b=>{if(b=="before:ordinary-helper-ordinary-repair")throw original;});}
            catch(IOException e){Check(ReferenceEquals(e,original),"Unknown-file setup stopped at the exact first marker.");}
            var work=WorkDirectories(s.Root, "helper").Single(d=>!File.Exists(Path.Combine(d,"plan")));
            var path=Path.Combine(work,kind=="unknown-file"?"user-content.keep":"ordinary-repair-pending-"+Guid.NewGuid().ToString("N"));
            var bytes=Bytes("unverified retained content");File.WriteAllBytes(path,bytes);
            Refuse(()=>Run(s),"Unverified content cannot authorize deterministic work reuse.");
            Check(File.ReadAllBytes(path).SequenceEqual(bytes),"Unknown or mismatched atomic content is never replaced or deleted.");
            Check(WorkDirectories(s.Root, "helper").Length==2,"Refused reuse allocates no alternate helper work.");
            Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(s.JournalRoot,"pending.json")))==s.JournalHash,"Refused reuse preserves exact original journal.");Cases.Add("deterministic-reuse-refused:"+kind);
        }
    }
    private static void Diagnostics() {
        var s=Seed();using var sink=DiagnosticOwner.StartInstaller(s.Root,"1.3.0",Guid.NewGuid().ToString("N"));
        Check(DiagnosticHub.Bind(sink),"Actual installer diagnostic owner binds.");
        try {
            var original=new ApplySafetyException(ApplyFailure.FileBusy,32);original.Data["private"]="INERT_PRIVATE_SENTINEL";
            try {Run(s,b=>{if(b=="after:ordinary-helper-plan")throw original;});throw new InvalidOperationException("Expected inert sharing interruption.");}
            catch(ApplySafetyException e) {Check(ReferenceEquals(e,original) && e.NativeError==32,"Original native failure remains unchanged.");}
            Run(s);Verify(s,true);
            Check(SpinWait.SpinUntil(()=>sink.Recording || sink.StorageFailed,5000) && SpinWait.SpinUntil(()=>sink.FlushBoundary(50),5000),"Actual repair diagnostic writer flushes.");
            var bundle=DiagnosticBundle.Collect(s.Root,Path.Combine(Stage,"absent-local"),Path.Combine(Stage,"absent-fallback"),Path.Combine(Stage,"absent-unity"),Path.Combine(Stage,"diagnostic-zip"),Path.Combine(Stage,"diagnostic-fallback"),"1.3.0","");
            using var zip=ZipFile.OpenRead(bundle.ArchivePath);
            var lines=string.Join("\n",zip.Entries.Where(e=>e.FullName.StartsWith("logs/game-minimal-installer-",StringComparison.Ordinal)).Select(e=>{using var reader=new StreamReader(e.Open());return reader.ReadToEnd();}));
            Check(!lines.Contains("INERT_PRIVATE_SENTINEL") && !lines.Contains(s.Root) && !lines.Contains(Encoding.ASCII.GetString(s.OriginalReceipt)),"Source-to-ZIP omits private values, paths and raw receipt content.");
            var events=lines.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonDocument.Parse(line)).ToList();
            try {
                var failure=events.Select(d=>d.RootElement).Single(e=>e.GetProperty("kind").GetString()=="Failure" && e.GetProperty("phase").GetString()=="Recovery" && e.GetProperty("native_code").GetInt32()==32);
                Check(failure.GetProperty("error_type").GetString()=="ApplySafetyException" && failure.GetProperty("apply_code").GetInt32()==(int)ApplyFailure.FileBusy,"Exact native/apply codes and Recovery phase survive ZIP projection.");
                var operation=failure.GetProperty("operation").GetString();Check(operation!="-","Interrupted repair retains operation correlation.");
                Check(events.Any(d=>d.RootElement.GetProperty("kind").GetString()=="Stage" && d.RootElement.GetProperty("phase").GetString()=="Recovery" && d.RootElement.GetProperty("outcome").GetString()=="Completed" && d.RootElement.GetProperty("operation").GetString()==operation),"Successful matching retry shares the failed repair operation in ZIP.");
            } finally {foreach(var e in events)e.Dispose();}
            Cases.Add("actual-repair-failure-and-resume:source-to-ZIP");
        } finally {DiagnosticHub.Unbind(sink);}
    }
    public static int Main(string[] args) {
        try {
            if(args.Length is <1 or >2 || !OperatingSystem.IsWindows())throw new InvalidOperationException("Fresh Windows artifact root required.");
            Stage=Path.GetFullPath(args[0]);var canonical=Path.GetFullPath(AppContext.BaseDirectory);
            if(!Stage.Contains("\\artifacts\\or-",StringComparison.OrdinalIgnoreCase) || Directory.Exists(Stage))throw new InvalidOperationException("Fresh isolated fixture directory required.");
            Directory.CreateDirectory(Stage);SavedLocal=Environment.GetEnvironmentVariable("LOCALAPPDATA")??"";Environment.SetEnvironmentVariable("LOCALAPPDATA",Path.Combine(Stage,"local"));
            using var key=RSA.Create(3072);Key=key;Public=key.ExportParameters(false);Trust=UpdateTrust.Production();
            Image=new byte[512];Image[0]=77;Image[1]=90;BitConverter.GetBytes(128).CopyTo(Image,60);Image[128]=80;Image[129]=69;BitConverter.GetBytes((ushort)0x8664).CopyTo(Image,132);
            Descriptor=HelperDescriptor.Prepare("1.3.0",Image);Signature=new DetachedSignature("ordinary-recovery-fixture",Key.SignData(Descriptor,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)).Bytes();
            var legacy=args.Length==2 && args[1]=="legacy-first-marker-leak";
            if(legacy)RepeatedFirstMarker(true);else {Basic();Boundaries();Negatives();Diagnostics();RepeatedFirstMarker(false);RepeatedFirstMarker(false,true);}
            Console.WriteLine(JsonSerializer.Serialize(new {Passed=true,Assertions,Cases,ConfirmedFirstMarkerLeak=legacy,ObservedInterruptions,PlanlessWorkDirectories,NoGameOrSteamExecution=true,NoNetworkRequests=true,EphemeralFixtureKeyOnly=true}));return 0;
        } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
        finally {if(SavedLocal.Length!=0)Environment.SetEnvironmentVariable("LOCALAPPDATA",SavedLocal);}
    }
}
namespace BetterAstralParty.Updating {
    internal static class ProductionUpdateKeys {
        internal static TrustedUpdateKey[] Read()=>new[]{new TrustedUpdateKey("ordinary-recovery-fixture",OrdinaryHelperRecoveryFixture.Public)};
    }
}
