using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterAstralParty;
using BetterAstralParty.Updating;

internal static class ChannelTransitionTests
{
    private static int _checks;
    private const string Root="ABCD1234:0000000000000100";
    private static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);_checks++;}
    private static void Refuse(Action action,string reason){try{action();}catch(Exception error)when(error is UpdateValidationException or ApplySafetyException or ArgumentException){Check(true,reason);return;}throw new InvalidOperationException(reason);}
    private static UpdateContext Context(ReleaseFeed feed,string current,string target,string? floor=null,ChannelTransitionIntent? intent=null,string? hash=null)
        =>new(feed.Repository,feed.Channel,current,target,123,456,UpdateTrust.PlatformId,floor,feed.RepositoryId,intent,hash);
    private static byte[] Descriptor(UpdateContext context,string schema="1",string protocol="5"){
        var text=$"BetterAstralParty.UpdateDescriptor/v{protocol}\nrepository={context.Repository}\nversion={context.ReleaseTag}\nchannel={context.Channel}\nplatform={UpdateTrust.PlatformId}\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-{context.ReleaseTag}-update.zip\nzip-bytes=1\nzip-sha256={new string('A',64)}\nfile-count={InstallReceipt.Owned.Length}\npurpose=release/{context.Channel}\nrepository-id={context.RepositoryId}\nsettings-schema={schema}\n";
        foreach(var path in InstallReceipt.Owned.OrderBy(value=>value,StringComparer.Ordinal))text+=$"file={path}\t1\t{new string('B',64)}\n";
        return Encoding.ASCII.GetBytes(text);
    }
    private static ReleaseFeed[] Feeds()=>new[]{ReleaseFeedPolicy.Stable,ReleaseFeedPolicy.PreRelease,ReleaseFeedPolicy.Beta};
    private static ReceiptChannelState Source(ReleaseFeed feed,string version,ReleaseFeed target,string? floor,string floorHash){
        long Id(string channel)=>feed.Channel==channel?feed.RepositoryId:target.Channel==channel&&floor!=null?target.RepositoryId:0;
        string? Version(string channel)=>feed.Channel==channel?version:target.Channel==channel?floor:null;
        string Hash(string channel)=>feed.Channel==channel?new string('C',64):target.Channel==channel&&floor!=null?floorHash:"-";
        return new(Root,feed.Channel,new string('a',32),Id("Stable"),Version("Stable"),Hash("Stable"),Id("Beta"),Version("Beta"),Hash("Beta"),Id("PreRelease"),Version("PreRelease"),Hash("PreRelease"));
    }
    private static byte[] Sign(RSA key,byte[] bytes)=>key.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
    private static void PublicPackage(RSA key,UpdateTrust trust){
        var bytes=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(Descriptor(Context(ReleaseFeedPolicy.Stable,"0.0.0","1.4.1")))
            .Replace("purpose=release/Stable\n","purpose=release/Public\n",StringComparison.Ordinal));
        var signature=Sign(key,bytes);var digest=UpdateTrust.Hash(bytes);
        foreach(var feed in new[]{ReleaseFeedPolicy.Stable,ReleaseFeedPolicy.PreRelease}){
            var verified=trust.VerifyProposal(bytes,"ephemeral-transition-fixture",signature,Context(feed,"1.4.0","1.4.1"));
            Check(verified.PublicRelease&&verified.Channel==feed.Channel&&verified.DescriptorSha256==digest,"one public signature retains exact bytes and the selected channel after promotion/demotion");
            Check(verified.AllowsChannel("Stable")&&verified.AllowsChannel("PreRelease")&&!verified.AllowsChannel("Beta"),"public historical proof cannot grant private Beta ownership");
            Refuse(()=>trust.VerifyDescriptor(bytes,"ephemeral-transition-fixture",signature,Context(feed,"1.4.1","1.4.1")),"classification changes never authorize an ordinary same-version apply");
            Refuse(()=>trust.VerifyDescriptor(bytes,"ephemeral-transition-fixture",signature,Context(feed,"1.5.0","1.4.1")),"classification changes never authorize an ordinary downgrade");
            foreach(var flag in new[]{true,false,true}){
                var assets=new[]{new{id=456,name="BetterAstralParty-1.4.1-update.zip"},new{id=457,name=ReleaseFeedPolicy.UpdateManifestAsset},new{id=458,name=ReleaseFeedPolicy.UpdateSignatureAsset}};
                var body=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{new{id=123,tag_name="1.4.1",name="fixture",body="",draft=false,prerelease=flag,html_url="https://github.com/"+feed.Repository+"/releases/tag/1.4.1",assets}}));
                var selected=ReleaseUpdates.Select(ReleaseUpdates.Parse(body,feed.Repository),"1.4.0",Enum.Parse<ReleaseChannel>(feed.Channel));
                Check((selected!=null)==feed.Delivers(flag),"live GitHub classification alone selects the channel without changing signed assets");
            }
        }
        Refuse(()=>trust.VerifyProposal(bytes,"ephemeral-transition-fixture",signature,Context(ReleaseFeedPolicy.Beta,"1.4.0","1.4.1")),"shared public scope never enters the private repository");
        foreach(var replacement in new[]{("channel=Stable\n","channel=PreRelease\n"),("repository-id="+ReleaseFeedPolicy.StableRepositoryId,"repository-id="+ReleaseFeedPolicy.BetaRepositoryId)}){
            var changed=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace(replacement.Item1,replacement.Item2,StringComparison.Ordinal));
            Refuse(()=>trust.VerifyProposal(changed,"ephemeral-transition-fixture",Sign(key,changed),Context(ReleaseFeedPolicy.PreRelease,"1.4.0","1.4.1")),"public scope still requires its exact canonical public repository/channel identity");
        }
        var rc=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace("1.4.1","1.4.1-rc.1",StringComparison.Ordinal));
        Check(trust.VerifyProposal(rc,"ephemeral-transition-fixture",Sign(key,rc),Context(ReleaseFeedPolicy.PreRelease,"1.4.0","1.4.1-rc.1")).Channel=="PreRelease","semantic RC tags remain eligible in PreRelease");
        Refuse(()=>trust.VerifyProposal(rc,"ephemeral-transition-fixture",Sign(key,rc),Context(ReleaseFeedPolicy.Stable,"1.4.0","1.4.1-rc.1")),"GitHub promotion cannot make a semantic RC tag a stable version");
    }
    private static void Policy(RSA key,UpdateTrust trust){
        foreach(var source in Feeds())foreach(var target in Feeds().Where(feed=>feed.Channel!=source.Channel))foreach(var version in new[]{"1.4.1","1.4.2","1.4.3"}){
            var proposalContext=Context(target,"0.0.0",version);var bytes=Descriptor(proposalContext);var signature=Sign(key,bytes);
            var descriptor=trust.VerifyProposal(bytes,"ephemeral-transition-fixture",signature,proposalContext);
            var state=Source(source,"1.4.2",target,null,"-");
            var review=new ChannelTransitionReview(Root,new string('E',64),new string('F',64),new string('A',64),"1.4.2",state,descriptor);
            var intent=ChannelTransitionIntent.Parse(ChannelTransitionIntent.Approve(review).Bytes());var approved=Context(target,"1.4.2",version,intent:intent);
            Check(intent.MatchesTarget(approved),"symmetric exact intent roundtrip "+source.Channel+" -> "+target.Channel+" "+version);
            Check(trust.VerifyDescriptor(bytes,"ephemeral-transition-fixture",signature,approved).Version==version,"explicit lower/equal/higher installed version accepted");
            var replay=Context(target,"1.4.2",version,version,intent,descriptor.DescriptorSha256);
            Check(trust.VerifyDescriptor(bytes,"ephemeral-transition-fixture",signature,replay).Version==version,"destination exact floor replay accepted");
            var applied=state.Applied(Root,descriptor,intent.Operation,intent);
            Check(applied.ActiveChannel==target.Channel&&applied.VersionFor(source.Channel)==state.ActiveVersion&&applied.DescriptorFor(source.Channel)==state.DescriptorFor(source.Channel),"receipt keeps explicit target channel and prior channel floor");
            Check(ReceiptChannelState.Parse(applied.Encoded).Encoded==applied.Encoded&&applied.Text().StartsWith("BetterAstralParty.ChannelState/v2\n",StringComparison.Ordinal),"three-channel receipt canonical roundtrip");
            var retained=Source(source,"1.4.2",target,version,descriptor.DescriptorSha256);
            Check(retained.Applied(Root,descriptor,intent.Operation,intent).ActiveChannel==target.Channel,"receipt permits only exact destination-floor replay");
            if(UpdateVersion.Parse(version).CompareTo(UpdateVersion.Parse("1.4.2"))>0){
                InstallProof.ValidateChannel(state,Root,"1.4.2",5,1,descriptor,"1.4.2");Check(true,"manual whole-package cross-channel upgrade preserves its strict newer-only policy");
            }else Refuse(()=>InstallProof.ValidateChannel(state,Root,"1.4.2",5,1,descriptor,"1.4.2"),"manual whole-package channel selection cannot bypass the existing installed/global floor");
            Refuse(()=>trust.VerifyDescriptor(bytes,"ephemeral-transition-fixture",signature,Context(target,"1.4.2",version,"9.0.0",intent,descriptor.DescriptorSha256)),"destination floor cannot be lowered");
            Refuse(()=>trust.VerifyDescriptor(bytes,"ephemeral-transition-fixture",signature,Context(target,"1.4.2",version,version,intent,new string('F',64))),"equal destination floor requires exact descriptor");
            Refuse(()=>Context(target,"1.4.4",version,intent:intent),"changed installed source cannot reuse approval");
            var changed=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace("zip-bytes=1","zip-bytes=2"));
            Refuse(()=>trust.VerifyDescriptor(changed,"ephemeral-transition-fixture",Sign(key,changed),approved),"signed changed target cannot reuse approval");
        }
        foreach(var feed in Feeds())foreach(var version in new[]{"1.4.1","1.4.2"}){
            var context=Context(feed,"1.4.2",version);var bytes=Descriptor(context);
            Refuse(()=>trust.VerifyDescriptor(bytes,"ephemeral-transition-fixture",Sign(key,bytes),context),"ordinary same-channel lower/equal refused");
        }
        var stable=Context(ReleaseFeedPolicy.Stable,"0.0.0","1.4.1");
        foreach(var bad in new[]{Descriptor(stable,"2"),Descriptor(stable,protocol:"4"),Descriptor(Context(ReleaseFeedPolicy.Stable,"0.0.0","1.3.0"))})
            Refuse(()=>trust.VerifyDescriptor(bad,"ephemeral-transition-fixture",Sign(key,bad),stable),"unsupported schema/protocol/baseline refused");
        var valid=Descriptor(stable);var broken=Sign(key,valid);broken[0]^=1;
        Refuse(()=>trust.VerifyDescriptor(valid,"ephemeral-transition-fixture",broken,stable),"invalid signature refused");
        Refuse(()=>trust.VerifyDescriptor(valid,"unknown-fixture-key",Sign(key,valid),stable),"unknown key refused");
        var preContext=Context(ReleaseFeedPolicy.PreRelease,"0.0.0","1.4.1");var preBytes=Descriptor(preContext);var preSignature=Sign(key,preBytes);
        Refuse(()=>trust.VerifyDescriptor(preBytes,"ephemeral-transition-fixture",preSignature,stable),"same public repository cannot relabel a signed prerelease descriptor as Stable");
        Refuse(()=>trust.VerifyDescriptor(valid,"ephemeral-transition-fixture",Sign(key,valid),preContext),"same public repository cannot relabel a signed Stable descriptor as PreRelease");
        var legacyText="BetterAstralParty.ChannelState/v1\nroot="+Root+"\nactive=Stable\noperation="+new string('a',32)+"\nStable="+ReleaseFeedPolicy.StableRepositoryId+"\t1.4.0\t"+new string('C',64)+"\nBeta=0\t-\t-\n";
        var legacy=ReceiptChannelState.Parse(Convert.ToBase64String(Encoding.ASCII.GetBytes(legacyText)));
        Check(legacy.Text()==legacyText&&legacy.VersionFor("PreRelease")==null&&legacy.DescriptorFor("PreRelease")=="-","existing protocol4 two-channel state retains its exact canonical bytes and an empty new floor");
        var preDescriptor=trust.VerifyProposal(preBytes,"ephemeral-transition-fixture",preSignature,preContext);
        var upgraded=legacy.Applied(Root,preDescriptor,new string('b',32));
        Check(upgraded.Text().StartsWith("BetterAstralParty.ChannelState/v2\n",StringComparison.Ordinal)&&upgraded.StableVersion=="1.4.0"&&upgraded.PreReleaseVersion=="1.4.1"&&upgraded.BetaVersion==null,"next verified apply adds a distinct prerelease row without replacing prior history");
        Refuse(()=>ReceiptChannelState.Parse(Convert.ToBase64String(Encoding.ASCII.GetBytes(legacyText.Replace("active=Stable","active=PreRelease")))),"v1 cannot claim an absent prerelease row");
        Refuse(()=>ReceiptChannelState.Parse(Convert.ToBase64String(Encoding.ASCII.GetBytes(upgraded.Text().Replace("PreRelease=","Beta=")))),"duplicate or reordered three-channel rows are refused");
        var full=new ReceiptChannelState(Root,"Beta",new string('c',32),ReleaseFeedPolicy.StableRepositoryId,"1.4.0",new string('A',64),ReleaseFeedPolicy.BetaRepositoryId,"1.7.0",new string('B',64),ReleaseFeedPolicy.StableRepositoryId,"1.4.1",preDescriptor.DescriptorSha256);
        Check(full.Floor(Context(ReleaseFeedPolicy.Stable,"1.7.0","2.0.0"))=="1.4.0"&&full.Floor(Context(ReleaseFeedPolicy.PreRelease,"1.7.0","2.0.0"))=="1.4.1"&&full.Floor(Context(ReleaseFeedPolicy.Beta,"1.7.0","2.0.0"))=="1.7.0","all three retained floors remain independent even when public repository IDs match");
        Refuse(()=>full.Floor(Context(new ReleaseFeed(ReleaseFeedPolicy.StableRepository,ReleaseFeedPolicy.StableRepositoryId+1,"PreRelease"),"1.7.0","2.0.0")),"prerelease floor cannot bind to a replacement public repository identity");
        var nextPreContext=Context(ReleaseFeedPolicy.PreRelease,"0.0.0","1.5.0");var nextPreBytes=Descriptor(nextPreContext);var nextPre=trust.VerifyProposal(nextPreBytes,"ephemeral-transition-fixture",Sign(key,nextPreBytes),nextPreContext);
        var fullNext=full.Applied(Root,nextPre,new string('d',32));
        Check(fullNext.StableVersion==full.StableVersion&&fullNext.StableDescriptor==full.StableDescriptor&&fullNext.BetaVersion==full.BetaVersion&&fullNext.BetaDescriptor==full.BetaDescriptor&&fullNext.PreReleaseVersion=="1.5.0","updating one channel retains both other channel rows and their exact evidence");
    }
    private sealed class NoNetwork : HttpMessageHandler { internal int Requests; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;throw new InvalidOperationException("No network allowed in policy fixture");} }
    private sealed class Authentication : IReleaseAuthentication { internal string IdentityValue="inert-fixture";internal int IdentityReads,Authorizations;public string CacheIdentity{get{IdentityReads++;return IdentityValue;}}public bool TryAuthorize(HttpRequestMessage request){Authorizations++;request.Headers.Authorization=new("Bearer","inert-fixture-not-a-credential");return true;} }
    private sealed class DiscoveryHandler(ReleaseFeed feed,string target,bool missingLatest=false,string older="1.4.0-rc.1") : HttpMessageHandler {
        internal int Requests;
        private object Release(string version,long id,bool complete,bool? prerelease=null){
            var assets=new List<object>{new{id=id+1,name="BetterAstralParty-"+version+"-update.zip"},new{id=id+2,name=ReleaseFeedPolicy.UpdateManifestAsset}};
            if(complete)assets.Add(new{id=id+3,name=ReleaseFeedPolicy.UpdateSignatureAsset});
            return new{tag_name=version,name="fixture",body="fixture",html_url="https://github.com/"+feed.Repository+"/releases/tag/"+version,draft=false,prerelease=prerelease??feed.Channel=="PreRelease",id,assets};
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
            Requests++;Check(request.RequestUri!.Host=="api.github.com"&&request.RequestUri.AbsolutePath.StartsWith("/repos/"+feed.Repository,StringComparison.Ordinal),"discovery uses only the selected repository");
            Check(feed.RequiresAuthentication?request.Headers.Authorization?.Parameter=="inert-fixture-not-a-credential":request.Headers.Authorization==null,"only the private Beta feed carries authentication");
            var identity=request.RequestUri.AbsolutePath=="/repos/"+feed.Repository;
            Check(UpdateVersion.Parse(older).CompareTo(UpdateVersion.Parse(target))<0,"secondary fixture release is older than the selected latest release");
            var releases=new List<object>{Release(target,900,!missingLatest),Release(older,800,true)};
            // Decoy outside the channel: a flagged release for Release users, an unflagged suffix tag for Pre-release users.
            if(!feed.RequiresAuthentication)releases.Add(feed.Channel=="PreRelease"?Release("9.0.0-rc.1",1000,true,false):Release("9.0.0",1000,true,true));
            var json=identity?JsonSerializer.Serialize(new{id=feed.RepositoryId,full_name=feed.Repository,@private=feed.RequiresAuthentication}):JsonSerializer.Serialize(releases);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(json,Encoding.UTF8,"application/json")});
        }
    }
    private static async Task Finish(ReleaseUpdates discovery){
        for(var i=0;i<300&&discovery.Result.Status==ReleaseUpdateStatus.Checking;i++){discovery.Poll();await Task.Delay(2);}discovery.Poll();
        Check(discovery.Result.Status!=ReleaseUpdateStatus.Checking,"bounded mocked discovery completes");
    }
    private static async Task AutomaticBaseline(RSA key,UpdateTrust trust){
        const string local="1.4.0-rc.3.local.12";
        foreach(var source in new[]{"1.4.0","1.4.1","1.5.0",local,"1.4.0-rc.3.local.13"})
            Check(ReleaseFeedPolicy.SupportsAutomaticSource(source),"fresh baseline and local 1.3 test builds remain automatic sources: "+source);
        foreach(var (version,isLocal) in new[]{("1.5.1-dev.1",true),("1.5.1-dev.12",true),(local,true),("1.5.1-beta.1",false),("1.5.1-dev.0",false),("1.5.1-devel.1",false),("1.5.1",false),("1.5.1-dev.1+meta",false)})
            Check(UpdateVersion.Parse(version).IsLocalPrerelease==isLocal,"development version classification: "+version);
        foreach(var feed in Feeds()){
            foreach(var target in new[]{"1.3.1","1.3.99","1.4.0-rc.1",local,"1.4.0","1.4.1","1.4.1-dev.3"}){
                var allowed=target is "1.4.0" or "1.4.1" || feed.Channel=="Beta"&&target=="1.4.0-rc.1";
                Check(ReleaseFeedPolicy.SupportsAutomaticTarget(target,feed.Channel)==allowed,"channel-aware destination baseline: "+feed.Channel+"/"+target);
                var context=Context(feed,"1.5.0",target);var bytes=Descriptor(context);var signature=Sign(key,bytes);
                if(allowed)Check(trust.VerifyProposal(bytes,"ephemeral-transition-fixture",signature,context).Version==target,"signed eligible transition proposal preserves the channel latest policy");
                else Refuse(()=>trust.VerifyProposal(bytes,"ephemeral-transition-fixture",signature,context),"signed old or local remote target cannot bypass the destination baseline");
            }
            var release=new ReleaseUpdate("1.5.0","fixture","",new Uri("https://github.com/"+feed.Repository+"/releases/tag/1.5.0")){
                Repository=feed.Repository,RepositoryId=feed.RepositoryId,FeedChannel=feed.Channel,Beta=feed.Channel=="Beta",GitHubPrerelease=feed.Channel=="PreRelease",
                ReleaseId=123,ZipAssetId=456,DescriptorAssetId=457,SignatureAssetId=458};
            Check(release.DownloadRequest(local,Enum.Parse<ReleaseChannel>(feed.Channel))!=null&&release.ProposalRequest(local)!=null,"local 1.3 source can create ordinary and explicit transition requests");
            foreach(var source in new[]{"1.1.4","1.3.1","1.3.99"}){
                Check(!ReleaseFeedPolicy.SupportsAutomaticSource(source)&&release.DownloadRequest(source,Enum.Parse<ReleaseChannel>(feed.Channel))==null&&release.ProposalRequest(source)==null,"prebaseline source cannot create automatic or transition requests");
                var auth=new Authentication();using var noNetwork=new NoNetwork();using var client=new HttpClient(noNetwork);
                using var discovery=new ReleaseUpdates(source,feed,client,auth);discovery.Check(DateTimeOffset.UtcNow);
                Check(discovery.Result.Status==ReleaseUpdateStatus.NotConfigured&&noNetwork.Requests==0,"prebaseline production discovery does not contact a feed");
                using var downloadHandler=new NoNetwork();using var downloads=new UpdateDownloads(trust,auth,downloadHandler,feed:feed);
                var request=new UpdateDownloadRequest(Context(feed,source,"1.5.0"),457,458);downloads.Start(request,DateTimeOffset.UtcNow);
                Check(downloads.Result.Status==UpdateDownloadStatus.NotConfigured&&downloadHandler.Requests==0,"direct prebaseline download requests stop before network or staging");
                try{await downloads.Propose(request,DateTimeOffset.UtcNow,CancellationToken.None);throw new Exception("Prebaseline proposal unexpectedly allowed");}
                catch(InvalidOperationException){Check(downloadHandler.Requests==0,"direct prebaseline transition proposals stop before network");}
            }
            foreach(var target in feed.Channel=="Beta"?new[]{"1.3.1"}:new[]{"1.3.1","1.4.0-rc.1"}){
                using var handler=new DiscoveryHandler(feed,target,older:"1.3.0");using var client=new HttpClient(handler);using var discovery=new ReleaseUpdates(local,feed,client,new Authentication());
                discovery.InstalledChannel(feed.Channel=="Beta"?"Stable":"Beta");discovery.Check(DateTimeOffset.UtcNow);await Finish(discovery);
                Check(discovery.Result.Status==ReleaseUpdateStatus.UpToDate&&discovery.Result.Release==null,"selected channel never falls back to a prebaseline destination");
            }
        }
    }
    private static async Task Discovery(UpdateTrust trust){
        foreach(var source in Feeds())foreach(var selected in Feeds().Where(feed=>feed.Channel!=source.Channel))foreach(var target in new[]{"1.4.0","1.5.0","1.6.0"}){
            var auth=new Authentication();
            using var handler=new DiscoveryHandler(selected,target);using var client=new HttpClient(handler);using var discovery=new ReleaseUpdates("1.5.0",selected,client,auth);
            using var noNetwork=new NoNetwork();using var downloads=new UpdateDownloads(trust,auth,noNetwork,feed:selected);
            using var automatic=new AutomaticUpdates(downloads,new Handoff(),new Safety(Source(source,"1.5.0",selected,null,"-")));automatic.Configure(auth);
            for(var i=0;i<100&&automatic.Status==AutomaticUpdateStatus.CheckingInstallation;i++){automatic.Tick(DateTimeOffset.UtcNow);await Task.Delay(2);}
            UpdateDiscoveryBridge.Apply(discovery,automatic,"1.5.0");
            var now=DateTimeOffset.UtcNow;discovery.Check(now);await Finish(discovery);
            if(target=="1.5.0"&&source.Channel is "Stable" or "PreRelease"&&selected.Channel is "Stable" or "PreRelease"){
                Check(discovery.Result.Status==ReleaseUpdateStatus.UpToDate&&discovery.Result.Release==null,"same public version on the other public channel is not offered for reinstall");
                UpdateDiscoveryBridge.Apply(discovery,automatic,"1.5.0");
                Check(automatic.TransitionWaiting&&!automatic.CanDownload&&automatic.Status==AutomaticUpdateStatus.TransitionRequired,"switched channel without a target waits instead of asking for a review");
                continue;
            }
            Check(discovery.Result.Status==ReleaseUpdateStatus.Available&&discovery.Result.Release?.Version==target,$"fresh config picks selected repository latest: {source.Channel}/{selected.Channel}/{target}, {discovery.Result.Status}/{discovery.Result.Release?.Version}, requests={handler.Requests}");
            Check(selected.RequiresAuthentication?auth.IdentityReads>0&&auth.Authorizations>0:auth.IdentityReads==0&&auth.Authorizations==0,"public channels never read or authorize a private credential provider");
            Check(discovery.Result.Release!.TransitionCandidate==(UpdateVersion.Parse(target).CompareTo(UpdateVersion.Parse("1.5.0"))<=0),"lower/equal discovery remains explicit-only");
            UpdateDiscoveryBridge.Apply(discovery,automatic,"1.5.0");Check(automatic.CanReviewTransition&&!automatic.CanDownload&&automatic.Status==AutomaticUpdateStatus.TransitionRequired&&!automatic.TransitionWaiting,"real bridge keeps explicit approval even for a lower candidate without an ordinary download request");
            auth.IdentityValue="inert-fixture-refreshed";discovery.Configure(client,auth);discovery.Check(now.AddMinutes(2));await Finish(discovery);
            Check(discovery.Result.Release?.Version==target,"auth configure/clear retains installed-channel latest mode");
            auth.IdentityValue="inert-fixture-second-session";discovery.Poll();discovery.Check(now.AddMinutes(4));await Finish(discovery);
            Check(discovery.Result.Release?.Version==target,"identity refresh cannot drop lower/equal latest mode");
        }
        foreach(var selected in Feeds()){
            using var handler=new DiscoveryHandler(selected,"1.4.0");using var client=new HttpClient(handler);using var discovery=new ReleaseUpdates("1.5.0",selected,client,new Authentication());
            discovery.InstalledChannel(selected.Channel);discovery.Check(DateTimeOffset.UtcNow);await Finish(discovery);
            Check(discovery.Result.Status==ReleaseUpdateStatus.UpToDate&&discovery.Result.Release==null,"ordinary same-channel discovery keeps strict newer filter");
        }
        foreach(var selected in Feeds().Where(feed=>!feed.RequiresAuthentication)){
            var auth=new Authentication();using var handler=new DiscoveryHandler(selected,"1.4.0");using var client=new HttpClient(handler);
            using var discovery=new ReleaseUpdates("1.5.0",selected,client,auth);var now=DateTimeOffset.UtcNow;
            discovery.InstalledChannel("Beta");discovery.Check(now);await Finish(discovery);var requests=handler.Requests;
            discovery.CheckTransition(now.AddSeconds(1));
            Check(discovery.TransitionCheckPending&&discovery.Result.Status==ReleaseUpdateStatus.RetryWaiting&&handler.Requests==requests,
                "explicit channel refresh respects the retained cooldown even with periodic checks OFF");
            discovery.Tick(now.AddSeconds(59),discovery.TransitionCheckPending);
            Check(discovery.TransitionCheckPending&&handler.Requests==requests,"pending explicit refresh never bypasses its cooldown");
            discovery.Tick(now.AddSeconds(60),discovery.TransitionCheckPending);await Finish(discovery);
            Check(!discovery.TransitionCheckPending&&discovery.Result.Release?.Version=="1.4.0"&&handler.Requests>requests,
                "the explicit deferred refresh selects destination latest even below the installed version");
            requests=handler.Requests;discovery.Tick(now.AddHours(7),false);
            Check(handler.Requests==requests&&!discovery.TransitionCheckPending&&auth.IdentityReads==0&&auth.Authorizations==0,
                "completed explicit refresh does not enable periodic checks or read private credentials");
        }
        using var missingHandler=new DiscoveryHandler(ReleaseFeedPolicy.Stable,"1.6.0",true);using var missingClient=new HttpClient(missingHandler);
        using var missing=new ReleaseUpdates("1.5.0",ReleaseFeedPolicy.Stable,missingClient);missing.InstalledChannel("Beta");missing.Check(DateTimeOffset.UtcNow);await Finish(missing);
        Check(missing.Result.Release?.Version=="1.6.0"&&missing.Result.Release.ProposalRequest("1.5.0")==null,"missing latest asset never falls back silently to an older complete release");
    }
    private sealed class Handoff : IUpdateHandoff {
        public bool Configured=>true;public Task<string?> Inspect()=>Task.FromResult<string?>(null);
        public Task<object> Prepare(DownloadedUpdate update,UpdateContext context,CancellationToken token)=>throw new InvalidOperationException("No staging allowed");
        public Task<object> Queue(object prepared,CancellationToken token)=>throw new InvalidOperationException("No process allowed");
        public void SignalCancel(object prepared){}public Task Cancel(object prepared)=>Task.CompletedTask;
    }
    private sealed class Safety(ReceiptChannelState state) : IUpdateSafety {
        public Task<UpdateSafetySnapshot> Probe()=>Task.FromResult(new UpdateSafetySnapshot(Channels:state,InstalledVersion:state.ActiveVersion));
        public Task<UpdateFault> Begin(UpdateContext context)=>throw new InvalidOperationException("No mutation allowed");
        public Task Complete(UpdateFault? operation)=>Task.CompletedTask;
        public Task<UpdateFault> Fail(UpdateContext context,FaultPhase phase,FaultReason reason,string? stage)=>throw new InvalidOperationException("No fault persistence allowed");
        public Task SavePreferences(bool download,bool apply)=>throw new InvalidOperationException("Preferences must not change");
        public Task SaveFailed(UpdateFault fault)=>Task.CompletedTask;public Task Acknowledge()=>Task.CompletedTask;
    }
    private static async Task Coordinator(UpdateTrust trust){
        foreach(var source in Feeds())foreach(var target in Feeds().Where(feed=>feed.Channel!=source.Channel))foreach(var version in new[]{"1.4.1","1.4.2","1.4.3"}){
            var state=Source(source,"1.4.2",target,null,"-");
            using var handler=new NoNetwork();var auth=new Authentication();using var downloads=new UpdateDownloads(trust,auth,handler,feed:target);
            using var automatic=new AutomaticUpdates(downloads,new Handoff(),new Safety(state));automatic.Configure(auth);
            automatic.Context(new(Context(target,"1.4.2",version),457,458),12);
            for(var i=0;i<100&&automatic.Status==AutomaticUpdateStatus.CheckingInstallation;i++){automatic.Tick(DateTimeOffset.UtcNow);await Task.Delay(2);}
            for(var i=0;i<5;i++)automatic.Tick(DateTimeOffset.UtcNow);
            Check(automatic.Status==AutomaticUpdateStatus.TransitionRequired&&!automatic.CanDownload&&automatic.CanReviewTransition,"all cross-channel directions/sizes require approval");
            Check(handler.Requests==0&&automatic.AutoDownload&&automatic.ApplyAfterExit,"channel selection cannot download or change saved choices");
        }
        using var noNetwork=new NoNetwork();using var client=new HttpClient(noNetwork);using var discovery=new ReleaseUpdates("1.4.2",ReleaseFeedPolicy.Stable,client);
        var latest=new ReleaseUpdate("1.4.3","fixture","",new Uri("https://github.com/"+ReleaseFeedPolicy.StableRepository+"/releases/tag/1.4.3")){
            Repository=ReleaseFeedPolicy.StableRepository,RepositoryId=ReleaseFeedPolicy.StableRepositoryId,FeedChannel="Stable",Generation=discovery.Generation,ReleaseId=123,ZipAssetId=456,DescriptorAssetId=457,SignatureAssetId=458};
        var result=typeof(ReleaseUpdates).GetProperty("Result",BindingFlags.Instance|BindingFlags.NonPublic)!;result.SetValue(discovery,new ReleaseUpdateResult(ReleaseUpdateStatus.Available,latest));
        Check(discovery.CurrentRelease(latest),"fresh exact current repository latest target accepted");
        foreach(var changed in new[]{latest with{ZipAssetId=459},latest with{DescriptorAssetId=459},latest with{SignatureAssetId=459},latest with{Generation=latest.Generation+1},latest with{Repository=ReleaseFeedPolicy.BetaRepository},latest with{RepositoryId=ReleaseFeedPolicy.BetaRepositoryId},latest with{FeedChannel="Beta"},latest with{ReleaseId=124}})
            Check(!discovery.CurrentRelease(changed),"changed latest identity cannot be approved");
        foreach(var status in new[]{ReleaseUpdateStatus.Stale,ReleaseUpdateStatus.Incomplete,ReleaseUpdateStatus.Checking}){result.SetValue(discovery,new ReleaseUpdateResult(status,latest));Check(!discovery.CurrentRelease(latest),"nonfresh discovery cannot approve old release");}
    }
    internal static async Task Main(string[] args){
        using var key=RSA.Create(3072);var trust=new UpdateTrust(new[]{new TrustedUpdateKey("ephemeral-transition-fixture",key.ExportParameters(false))});
        typeof(UpdateTrust).GetField("_production",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(trust,true);
        Policy(key,trust);PublicPackage(key,trust);await AutomaticBaseline(key,trust);await Coordinator(trust);await Discovery(trust);
        var report=new{checks=_checks,scope="Actual policy/coordinator sources, stricter production floor with an ephemeral fixture key only. No operational key, live network, native UI, game, Steam, helper process, staging or installation.",pass=true};
        File.WriteAllText(Path.Combine(args[0],"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+_checks+" channel-policy/coordinator checks.");
    }
}
