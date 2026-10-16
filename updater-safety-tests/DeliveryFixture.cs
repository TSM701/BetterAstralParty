using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;
using BetterAstralParty;
using System.Diagnostics;
using System.Text;
internal static partial class HelperFixture {
    [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
    private static extern bool CreateHardLinkW(string link,string existing,IntPtr security);
    private static void Refused(Action action,string name) {try {action();} catch(UpdateValidationException) {Check(true,name);return;} catch(ApplySafetyException) {Check(true,name);return;} throw new Exception("accepted "+name);}
    private sealed record DeliveryRequest(UpdateContext Context,byte[] Zip,byte[] Descriptor,byte[] HelperDescriptor,byte[] HelperImage);
    private static DeliveryRequest DeliveryPrepare(UpdateContext context,IDictionary<string,byte[]> inputs,byte[] helperImage) {
        var version=UpdateVersion.Parse(context.ReleaseTag);
        if(!ReleaseFeedPolicy.Matches(context) || version.CompareTo(UpdateVersion.Parse(context.CurrentVersion))<=0
            || context.Channel=="Stable" && version.IsPrerelease) throw new UpdateValidationException(UpdateFailure.WrongTarget);
        if(inputs.Count!=InstallReceipt.Owned.Length) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
        long total=0;
        var payload=InstallReceipt.Owned.Select(path=> {
            if(!inputs.TryGetValue(path,out var bytes) || bytes==null || bytes.Length==0 || bytes.LongLength>UpdateTrust.MaxFileBytes)
                throw new UpdateValidationException(UpdateFailure.InvalidPackage);
            total+=bytes.LongLength;return (byte[])bytes.Clone();
        }).ToArray();
        var metadata=InstalledPluginMetadata.Read(inputs[UpdateTrust.PluginPath]);
        if(total>UpdateTrust.MaxExpandedBytes || metadata.Version!=version.Tag || metadata.UpdateProtocol!=4 || metadata.SettingsSchema!=1)
            throw new UpdateValidationException(UpdateFailure.InvalidPackage);
        var normalized=new UpdateContext(context.Repository,context.Channel,context.CurrentVersion,version.Tag,context.ReleaseId,context.AssetId,context.Platform,
            context.HighestAppliedVersion,context.RepositoryId,context.Transition,context.HighestAppliedDescriptorSha256);
        var zip=GaPackageContract.PreviewUpdateZip(payload);
        var descriptor=GaPackageContract.PrepareUpdateDescriptor(version.Tag,context.Channel,context.ReleaseId,context.AssetId,zip,
            payload.Select(bytes=>bytes.LongLength).ToArray(),payload.Select(UpdateTrust.Hash).ToArray());
        var image=(byte[])helperImage.Clone();
        return new(normalized,zip,descriptor,GaPackageContract.PrepareHelper(version.Tag,image),image);
    }
    private static Dictionary<string,byte[]> DeliveryFinalize(DeliveryRequest request,byte[] signatureEnvelope,byte[] helperSignatureEnvelope,UpdateTrust trust) {
        var signature=DetachedSignature.Parse(signatureEnvelope);
        var verified=trust.VerifyDescriptor(request.Descriptor,signature.KeyId,signature.Signature,request.Context);
        Check(verified.Protocol==4 && verified.SettingsSchema==1 && verified.Files.Count==InstallReceipt.Owned.Length,"current native descriptor contract");
        UpdatePackage.VerifyPayload(request.Zip,verified);
        var helper=HelperDescriptor.Verify(trust,request.HelperDescriptor,DetachedSignature.Parse(helperSignatureEnvelope),request.HelperImage,verified.Version);
        Check(helper.Protocol==4,"current native helper contract");
        return new(StringComparer.Ordinal) {
            {verified.AssetName,(byte[])request.Zip.Clone()},{ReleaseFeedPolicy.UpdateManifestAsset,(byte[])request.Descriptor.Clone()},
            {ReleaseFeedPolicy.UpdateSignatureAsset,(byte[])signatureEnvelope.Clone()},{"helper.descriptor",(byte[])request.HelperDescriptor.Clone()},
            {"helper.signature",(byte[])helperSignatureEnvelope.Clone()},{HelperDescriptor.Image,(byte[])request.HelperImage.Clone()}
        };
    }
    private static async Task DeliveryIntegration(string baseRoot,Package package,byte[] inert,string helper) {
        var distribution=DeliveryPrepare(Context,package.New,File.ReadAllBytes(helper));
        var signature=new DetachedSignature("helper-fixture",package.Sign(distribution.Descriptor)).Bytes();
        var helperSignature=new DetachedSignature("helper-fixture",package.Sign(distribution.HelperDescriptor)).Bytes();
        var completed=DeliveryFinalize(distribution,signature,helperSignature,package.Trust);
        Check(completed.Count==6 && completed.ContainsKey(ReleaseFeedPolicy.UpdateManifestAsset) && completed.ContainsKey(ReleaseFeedPolicy.UpdateSignatureAsset)
            && !completed.ContainsKey("BetterAstralParty.update.manifest") && !completed.ContainsKey("BetterAstralParty.update.signature"),"new generation names/exact complete signed distribution without legacy aliases");
        Check(UpdateTrust.Hash(completed["BetterAstralParty-1.3.0-update.zip"])==UpdateTrust.Hash(distribution.Zip),"signed distribution ZIP immutable");
        var repeated=DeliveryPrepare(Context,package.New,File.ReadAllBytes(helper));
        Check(UpdateTrust.Hash(repeated.Zip)==UpdateTrust.Hash(distribution.Zip) && UpdateTrust.Hash(repeated.Descriptor)==UpdateTrust.Hash(distribution.Descriptor),"deterministic immutable packaging");
        var vtag=new UpdateContext(Context.Repository,"Stable",Context.CurrentVersion,"v1.3.0",123,456,Context.Platform,repositoryId:Context.RepositoryId);
        var vrequest=DeliveryPrepare(vtag,package.New,File.ReadAllBytes(helper));
        Check(vrequest.Context.ReleaseTag=="1.3.0" && DeliveryFinalize(vrequest,new DetachedSignature("helper-fixture",package.Sign(vrequest.Descriptor)).Bytes(),helperSignature,package.Trust).Count==6,"GitHub v-prefixed tag normalized before current producer/consumer verification");
        Refused(()=>DeliveryFinalize(distribution,signature,helperSignature,new UpdateTrust(Array.Empty<TrustedUpdateKey>())),"fixture finalization empty trust");
        Refused(()=>DeliveryFinalize(distribution,helperSignature,signature,package.Trust),"signature domain separation");
        var altered=(byte[])helperSignature.Clone();altered[altered.Length-3]^=1;
        Refused(()=>DeliveryFinalize(distribution,signature,altered,package.Trust),"helper detached signature corruption");
        var wrong=new Dictionary<string,byte[]>(package.New);wrong.Add("doorstop_config.ini",[1]);
        Refused(()=>DeliveryPrepare(Context,wrong,File.ReadAllBytes(helper)),"no shared-loader expansion in ZIP");
        wrong=new Dictionary<string,byte[]>(package.New);wrong[UpdateTrust.PluginPath]=package.Old[UpdateTrust.PluginPath];
        Refused(()=>DeliveryPrepare(Context,wrong,File.ReadAllBytes(helper)),"payload metadata version mismatch");
        var beta=new UpdateContext(ReleaseFeedPolicy.BetaRepository,"Beta",Context.CurrentVersion,"1.3.1-rc.1",123,456,Context.Platform,repositoryId:ReleaseFeedPolicy.BetaRepositoryId);
        Refused(()=>DeliveryPrepare(beta,package.New,File.ReadAllBytes(helper)),"supported beta tag must match built DLL");
        foreach(var replacement in new[]{new[]{"UpdateDescriptor/v4","UpdateDescriptor/v3"},new[]{"purpose=release/Public","purpose=local-install/Stable"},
            new[]{"repository-id="+Context.RepositoryId,"repository-id=1"},new[]{"settings-schema=1","settings-schema=2"}}) {
            var changed=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(distribution.Descriptor).Replace(replacement[0],replacement[1]));
            Check(!changed.SequenceEqual(distribution.Descriptor),"descriptor negative mutation applied "+replacement[0]);
            var request=distribution with {Descriptor=changed};
            Refused(()=>DeliveryFinalize(request,new DetachedSignature("helper-fixture",package.Sign(changed)).Bytes(),helperSignature,package.Trust),"authenticated current descriptor rejects "+replacement[0]);
        }
        var envelope=DetachedSignature.Parse(signature);Check(envelope.KeyId=="helper-fixture","canonical detached envelope");
        Refused(()=>DetachedSignature.Parse(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(signature).Replace("\n","\r\n"))),"envelope canonical line endings");
        Refused(()=>DetachedSignature.Parse(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(signature).Replace("signature=","signature= "))),"envelope no ambiguous whitespace");
        Console.WriteLine("PASS: current GaPackageContract producer plus actual v4 trust/payload/helper consumers; historical UpdateDistribution Prepare/Finalize retired. Operational release approval is covered separately by packaging/release-finalization checks.");
        var source=Path.Combine(baseRoot,"delivery-bundle");Directory.CreateDirectory(source);
        foreach(var file in completed) File.WriteAllBytes(Path.Combine(source,file.Key),file.Value);
        foreach(var mode in new[]{"first","launch","bad-pin","prepared","after-image","after-receipt","foreign","unsupported-receipt","empty-trust","tampered","pid-wait"}) {
            var root=Install(baseRoot,"d-"+mode,package,inert);string? work=null;
            var desc=completed["helper.descriptor"];var sig=helperSignature;var image=distribution.HelperImage;
            if(mode=="foreign") File.WriteAllText(Path.Combine(root,HelperDescriptor.Image),"other owner");
            if(mode=="unsupported-receipt") {
                var original=Encoding.ASCII.GetString(package.Receipt);
                var receipt=original.Replace("\"files\":[","\"files\":[{\"path\":\"doorstop_config.ini\",\"sha256\":\""+new string('0',64)+"\"},");
                Check(receipt!=original && UpdateEligibility.Receipt(Encoding.ASCII.GetBytes(receipt))==InstallEligibility.ManualUpgradeRequired,"unsupported receipt mutation really changes native owned inventory");
                File.WriteAllText(Path.Combine(root,InstallReceipt.Path),receipt);
            }
            if(mode=="tampered") {image=(byte[])image.Clone();image[image.Length-1]^=1;}
            if(mode=="pid-wait") {
                using var running=Start(Path.Combine(root,"AstralParty_INT.exe"),"500");
                using(var fence=new WindowsFileFence(root)) Refused(()=>HelperDelivery.Install(fence,desc,sig,image,package.Trust),"bootstrap refuses exact live fake game");
                Check(!File.Exists(Path.Combine(root,HelperDescriptor.Image)),"live game bootstrap no writes");await running.WaitForExitAsync();continue;
            }
            if(mode=="launch") {
                Check(await Task.Run(()=>TrustedBootstrap.Run(root,source,UpdateTrust.Hash(image),package.Trust))==0,"pinned signed bootstrap actually launches Framework helper");
            } else if(mode=="bad-pin") {
                Refused(()=>TrustedBootstrap.Run(root,source,new string('0',64),package.Trust),"independent bootstrap fingerprint mismatch before launch");
                Check(!WorkDirectories(root, "helper").Any(),"bad fingerprint creates no helper journal");
            } else if(mode is "first" or "unsupported-receipt" or "foreign") {
                var process=Start(helper,"--install-helper \""+root+"\" \""+source+"\" \""+Path.Combine(baseRoot,"fixture-public-key.bin")+"\"");
                await process.WaitForExitAsync();var error=await process.StandardError.ReadToEndAsync();Check(process.ExitCode==(mode=="first"?0:4),"actual Framework bootstrap CLI "+mode+" "+error);process.Dispose();
            } else using(var fence=new WindowsFileFence(root)) {
                if(mode=="empty-trust" || mode=="tampered") Refused(()=>HelperDelivery.Install(fence,desc,sig,image,mode=="empty-trust"?new UpdateTrust(Array.Empty<TrustedUpdateKey>()):package.Trust),"bootstrap untrusted/tampered "+mode);
                else {
                    var point=mode=="prepared"?"prepared":mode=="after-image"?"after:image":"after:receipt";
                    try {HelperDelivery.Install(fence,desc,sig,image,package.Trust,p=>{if(p==point) throw new Interruption();});throw new Exception("missing bootstrap interruption");} catch(Interruption) {}
                    work=WorkRelative(root, WorkDirectories(root, "helper").Single());
                    Check(HelperDelivery.Install(fence,desc,sig,image,package.Trust)==HelperInstallResult.RecoveryRequired,"partial bootstrap blocks fresh install");
                }
            }
            if(work!=null) {
                using var process=Start(helper,"--recover-helper \""+root+"\" "+work+" \""+Path.Combine(baseRoot,"fixture-public-key.bin")+"\"");await process.WaitForExitAsync();var error=await process.StandardError.ReadToEndAsync();Check(process.ExitCode==0,"actual bootstrap recovery "+mode+" "+error);
            }
            using(var fence=new WindowsFileFence(root)) {
                if(mode is "first" or "launch" or "prepared" or "after-image" or "after-receipt") {
                    Check(HelperDelivery.Fingerprint(fence,package.Trust)==UpdateTrust.Hash(image),"signed installed fingerprint "+mode);
                    Check(HelperDelivery.Install(fence,desc,sig,image,package.Trust)==HelperInstallResult.AlreadyInstalled,"bootstrap idempotent "+mode);
                    var handoff=new UpdateHandoff(root,package.Trust);Check(!handoff.Configured,"fingerprint initially unknown");await Task.Run(handoff.Initialize);Check(handoff.Configured,"worker resolves signed bootstrap fingerprint");
                } else {Check(!File.Exists(Path.Combine(root,HelperDelivery.Receipt)),"unsupported bootstrap no ownership receipt "+mode);}
            }
            foreach(var file in package.Old) Check(UpdateTrust.Hash(File.ReadAllBytes(Path.Combine(root,file.Key)))==UpdateTrust.Hash(file.Value),"bootstrap preserves native owned originals "+mode);
            Check(File.ReadAllText(Path.Combine(root,"other-mod.keep"))=="unchanged","bootstrap preserves unrelated mod");
            if(mode=="foreign") Check(File.ReadAllText(Path.Combine(root,HelperDescriptor.Image))=="other owner","unreceipted helper untouched");
        }
        RemovedDeliveryEvidence(baseRoot,package,inert,distribution.HelperImage);
        // Retention uses completed real helper roots already produced by the backend fixture.
        foreach(var name in new[]{"backend-apply","backend-cancel-recorded"}) {
            var root=Path.Combine(baseRoot,name);var stage=WorkDirectories(root, "stage").Single(p=>File.Exists(Path.Combine(p,"handoff.ticket")));
            if(name=="backend-cancel-recorded") {
                var zipPath=Path.Combine(stage,"package.zip");var originalZip=File.ReadAllBytes(zipPath);
                using(var fence=new WindowsFileFence(root)) Refused(()=>UpdateRetention.Clean(fence,new UpdateTrust(Array.Empty<TrustedUpdateKey>())),"cleanup empty trust before mutations");
                var corrupted=(byte[])originalZip.Clone();corrupted[corrupted.Length-1]^=1;File.WriteAllBytes(zipPath,corrupted);
                using(var fence=new WindowsFileFence(root)) Check(UpdateRetention.Clean(fence,package.Trust).RemovedPackages==0,"tampered duplicate ZIP preserved");
                Check(File.Exists(zipPath) && !File.Exists(Path.Combine(stage,"package.cleaned")),"tampered ZIP creates no cleanup proof");
                File.WriteAllBytes(zipPath,originalZip);
                var alias=Path.Combine(stage,"zip.alias");Check(CreateHardLinkW(alias,zipPath,IntPtr.Zero),"create fake cleanup hardlink");
                using(var fence=new WindowsFileFence(root)) Check(UpdateRetention.Clean(fence,package.Trust).RemovedPackages==0,"hardlinked ZIP cannot be deleted by retention");
                Check(UpdateTrust.Hash(File.ReadAllBytes(alias))==UpdateTrust.Hash(originalZip) && File.Exists(zipPath) && !File.Exists(Path.Combine(stage,"package.cleaned")),"hardlinked package/evidence unchanged");File.Delete(alias);
                using(var held=new FileStream(Path.Combine(stage,"handoff.ticket"),FileMode.Open,FileAccess.Read,FileShare.None))
                using(var fence=new WindowsFileFence(root)) Check(UpdateRetention.Clean(fence,package.Trust).RemovedPackages==0,"active ticket lease prevents cleanup");
                Check(File.Exists(zipPath),"leased completed request retained");
                var resultPath=Path.Combine(stage,"helper.result");var originalResult=File.ReadAllBytes(resultPath);File.WriteAllText(resultPath,"unknown status");
                using(var fence=new WindowsFileFence(root)) Check(UpdateRetention.Clean(fence,package.Trust).RemovedPackages==0,"unknown completion status preserved");
                File.WriteAllBytes(resultPath,originalResult);
                File.Delete(zipPath);
                using(var fence=new WindowsFileFence(root)) Check(UpdateRetention.Clean(fence,package.Trust).AlreadyClean==0,"missing ZIP without valid cleanup proof is unknown");
                Check(!File.Exists(Path.Combine(stage,"package.cleaned")),"unknown missing ZIP does not fabricate proof");File.WriteAllBytes(zipPath,originalZip);
                File.WriteAllText(Path.Combine(stage,"user.keep"),"user staging note");
                using(var fence=new WindowsFileFence(root)) {
                    try {UpdateRetention.Clean(fence,package.Trust,p=>{if(p=="before:delete") throw new Interruption();});throw new Exception("missing cleanup interruption");} catch(Interruption) {}
                }
                Check(File.Exists(zipPath) && File.Exists(Path.Combine(stage,"package.cleaned")),"cleanup interruption keeps ZIP and verified resume proof");
            }
            var transactions=WorkDirectories(root, "txn").SelectMany(p=>Directory.GetFiles(p)).ToDictionary(p=>p,p=>UpdateTrust.Hash(File.ReadAllBytes(p)));
            using(var fence=new WindowsFileFence(root)) {var result=UpdateRetention.Clean(fence,package.Trust);Check(result.RemovedPackages==1,"remove authenticated duplicate handoff ZIP "+name);var again=UpdateRetention.Clean(fence,package.Trust);Check(again.RemovedPackages==0 && again.AlreadyClean==1,"retention idempotent "+name);}
            Check(!File.Exists(Path.Combine(stage,"package.zip")) && File.Exists(Path.Combine(stage,"package.cleaned")) && File.Exists(Path.Combine(stage,"descriptor.bin")) && File.Exists(Path.Combine(stage,"handoff.ticket")),"retain authenticated metadata/ticket after duplicate removal");
            if(name=="backend-cancel-recorded") Check(File.ReadAllText(Path.Combine(stage,"user.keep"))=="user staging note","cleanup preserves unrelated staging files");
            foreach(var file in transactions) Check(UpdateTrust.Hash(File.ReadAllBytes(file.Key))==file.Value,"all transaction plans/backups/new slots retained");
            foreach(var core in WorkDirectories(root, "stage").Where(p=>p!=stage)) Check(File.Exists(Path.Combine(core,"package.zip")),"transaction stage ZIP retained");
            using var replay=Start(helper,"--apply \""+root+"\" "+WorkRelative(root, stage)+" \""+Path.Combine(baseRoot,"fixture-public-key.bin")+"\"");await replay.WaitForExitAsync();Check(replay.ExitCode==8,"cleaned completed ticket cannot reapply or latch a new failure");
        }
        var corrupt=Path.Combine(baseRoot,"backend-fail-corrupt");var before=WorkDirectories(corrupt, "stage").SelectMany(p=>Directory.GetFiles(p)).ToDictionary(p=>p,p=>UpdateTrust.Hash(File.ReadAllBytes(p)));
        using(var fence=new WindowsFileFence(corrupt)) Refused(()=>UpdateRetention.Clean(fence,package.Trust),"pending/corrupt/fault records block all cleanup");
        foreach(var file in before) Check(UpdateTrust.Hash(File.ReadAllBytes(file.Key))==file.Value,"blocked cleanup preserves staged recovery evidence");
        Console.WriteLine("PASS: signed offline payload/helper delivery, actual bootstrap/recovery CLI, unsupported inventory block, signed fingerprint readiness and duplicate-only retention.");
    }
    private static void RemovedDeliveryEvidence(string baseRoot,Package package,byte[] inert,byte[] incomingImage) {
        var root=Install(baseRoot,"d-removed-evidence",package,inert);
        var image=new byte[incomingImage.Length+1];Array.Copy(incomingImage,image,incomingImage.Length);image[image.Length-1]=90;
        var descriptor=HelperDescriptor.Prepare("1.3.0-rc.1",image,4);
        var signature=new DetachedSignature("helper-fixture",package.Sign(descriptor)).Bytes();
        using(var fence=new WindowsFileFence(root)) {
            Check(HelperDelivery.Install(fence,descriptor,signature,image,package.Trust)==HelperInstallResult.Installed,"removed evidence fixture installs a different signed helper version/image");
            Check(HelperDelivery.Fingerprint(fence,package.Trust,4)==UpdateTrust.Hash(image),"former helper public fingerprint before removal");
        }
        var imagePath=Path.Combine(root,HelperDescriptor.Image);var receiptPath=Path.Combine(root,HelperDelivery.Receipt);
        var savedImage=File.ReadAllBytes(imagePath);var savedReceipt=File.ReadAllBytes(receiptPath);
        var workPath=WorkDirectories(root, "helper").Single();var work=WorkRelative(root, workPath);
        var records=new[]{"plan","descriptor","signature","committed"}.ToDictionary(name=>name,name=>File.ReadAllBytes(Path.Combine(workPath,name)));
        File.Delete(imagePath);File.Delete(receiptPath);
        var expected="BetterAstralParty.RemovedHelperEvidence/v1\n"+string.Concat(new[]{"plan","descriptor","signature","committed"}.Select(name=>work+"/"+name+"="+UpdateTrust.Hash(records[name])+"\n"));
        var snapshot=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>UpdateTrust.Hash(File.ReadAllBytes(path)));
        void Preserved() {
            var files=Directory.GetFiles(root,"*",SearchOption.AllDirectories);
            Check(files.Length==snapshot.Count && files.All(path=>snapshot.TryGetValue(path,out var hash) && UpdateTrust.Hash(File.ReadAllBytes(path))==hash),"removed evidence validation preserves every retained/unrelated file");
            Check(!File.Exists(imagePath) && !File.Exists(receiptPath),"removed evidence validation never reinstalls former helper");
        }
        void RejectBytes(byte[] candidateImage,byte[] candidateReceipt,string name,UpdateTrust? trust=null) {
            using(var fence=new WindowsFileFence(root)) Refused(()=>HelperDelivery.RemovedEvidence(fence,trust??package.Trust,candidateImage,candidateReceipt),name);
            Preserved();
        }
        void RejectRecord(string name,byte[] changed,string label,bool recommit=false) {
            try {
                File.WriteAllBytes(Path.Combine(workPath,name),changed);
                if(recommit) File.WriteAllBytes(Path.Combine(workPath,"committed"),Encoding.ASCII.GetBytes(UpdateTrust.Hash(changed)+"\n"));
                using(var fence=new WindowsFileFence(root)) Refused(()=>HelperDelivery.RemovedEvidence(fence,package.Trust,savedImage,savedReceipt),label);
            } finally {File.WriteAllBytes(Path.Combine(workPath,name),records[name]);if(recommit)File.WriteAllBytes(Path.Combine(workPath,"committed"),records["committed"]);}
            Preserved();
        }
        using(var fence=new WindowsFileFence(root)) Check(HelperDelivery.RemovedEvidence(fence,package.Trust,savedImage,savedReceipt)==expected,"removed helper evidence pins exact canonical retained records independent of incoming version/image");
        Check(UpdateTrust.Hash(savedImage)!=UpdateTrust.Hash(incomingImage) && Encoding.ASCII.GetString(savedReceipt).Contains("\nversion=1.3.0-rc.1\n"),"removed evidence does not require incoming helper bytes or version");
        Preserved();
        var badImage=(byte[])savedImage.Clone();badImage[badImage.Length-1]^=1;
        RejectBytes(badImage,savedReceipt,"removed helper rejects changed image");
        RejectBytes(savedImage,savedReceipt,"removed helper requires configured trust",new UpdateTrust(Array.Empty<TrustedUpdateKey>()));
        var receiptText=Encoding.ASCII.GetString(savedReceipt);
        foreach(var replacement in new[]{
            new[]{"version=1.3.0-rc.1","version=1.3.0-rc.2"},
            new[]{"sha256="+UpdateTrust.Hash(savedImage),"sha256="+new string('0',64)},
            new[]{"descriptor="+UpdateTrust.Hash(descriptor),"descriptor="+new string('0',64)},
            new[]{"key-id=helper-fixture","key-id=foreign-fixture"}})
            RejectBytes(savedImage,Encoding.ASCII.GetBytes(receiptText.Replace(replacement[0],replacement[1])),"removed helper rejects changed receipt "+replacement[0].Split('=')[0]);
        foreach(var key in new[]{"root","image-id"}) {
            var line=receiptText.Split('\n').Single(value=>value.StartsWith(key+"=",StringComparison.Ordinal));
            RejectBytes(savedImage,Encoding.ASCII.GetBytes(receiptText.Replace(line,key+"=00000000:0000000000000000")),"removed helper rejects changed receipt "+key);
        }
        RejectBytes(savedImage,Encoding.ASCII.GetBytes(receiptText.Replace("\n","\r\n")),"removed helper rejects noncanonical receipt");
        foreach(var name in new[]{"descriptor","signature","committed"}) {
            var changed=(byte[])records[name].Clone();changed[changed.Length-2]^=1;
            RejectRecord(name,changed,"removed helper rejects changed retained "+name);
        }
        RejectRecord("signature",new DetachedSignature("untrusted-fixture",package.Sign(descriptor)).Bytes(),"removed helper rejects valid signature with unknown key identity");
        var planText=Encoding.ASCII.GetString(records["plan"]);
        foreach(var key in new[]{"root","work","work-id","image-id","receipt-id"}) {
            var line=planText.Split('\n').Single(value=>value.StartsWith(key+"=",StringComparison.Ordinal));
            var value=key=="work"?"bap-v4-helper-"+new string('0',32):key=="receipt-id"?"invalid": "00000000:0000000000000000";
            RejectRecord("plan",Encoding.ASCII.GetBytes(planText.Replace(line,key+"="+value)),"removed helper rejects changed retained plan "+key,true);
        }
        try {
            File.Delete(Path.Combine(workPath,"committed"));
            using(var fence=new WindowsFileFence(root)) Refused(()=>HelperDelivery.RemovedEvidence(fence,package.Trust,savedImage,savedReceipt),"removed helper rejects uncommitted retained plan");
        } finally {File.WriteAllBytes(Path.Combine(workPath,"committed"),records["committed"]);}
        Preserved();
        var pending=Path.Combine(root,"BetterAstralParty.reinstall-v4.pending");
        try {
            File.WriteAllText(pending,"fixture preserved interrupted reinstall intent");
            using(var fence=new WindowsFileFence(root)) {
                Refused(()=>UpdateLaunchGate.Check(fence),"pending reinstall generation blocks native maintenance/launch gate");
                Refused(()=>HelperDelivery.Install(fence,descriptor,signature,image,package.Trust),"pending reinstall generation blocks helper installation under lock");
                Refused(()=>HelperDelivery.Recover(fence,work,package.Trust),"pending reinstall generation blocks helper recovery under lock");
            }
            Check(File.ReadAllText(pending)=="fixture preserved interrupted reinstall intent","blocked helper operations preserve pending reinstall intent");
        } finally {File.Delete(pending);}
        Preserved();
        Check(File.ReadAllText(Path.Combine(root,"other-mod.keep"))=="unchanged","removed evidence preserves unrelated mod");
        Console.WriteLine("PASS: removed signed helper copies authenticate retained evidence across helper version/image changes; image/receipt/plan/descriptor/signature/commit tampering and pending reinstall are rejected without installing or rewriting evidence.");
    }
}
