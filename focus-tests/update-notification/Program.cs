using System.Net;
using System.Reflection;
using System.Text;
using BetterAstralParty;
using UnityEngine;
using UInput = UnityEngine.Input;

var cases = new List<string>();
void Check(bool ok,string message) { if (!ok) throw new InvalidOperationException(message); }
void Case(string name,Action run) { run(); cases.Add(name); Console.WriteLine("PASS "+name); }
ReleaseUpdate Release(string version) => new(version,"Fixture title","[literal notes]",new Uri("https://github.com/Fixture/Repo/releases/tag/"+version));
ReleaseUpdateResult Available(string version) => new(ReleaseUpdateStatus.Available,Release(version));
void Inject(ReleaseUpdateResult result)
{
    if(result.Release is { } release) result=result with{Release=release with{Generation=Plugin.Updates.Generation}};
    typeof(ReleaseUpdates).GetProperty("Result",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Plugin.Updates,result);
    var revision=typeof(ReleaseUpdates).GetProperty("Revision",BindingFlags.Instance|BindingFlags.NonPublic)!;
    revision.SetValue(Plugin.Updates,(int)revision.GetValue(Plugin.Updates)!+1);
}
void Frame(Action? input=null)
{
    UInput.Reset(); GameUi.Hit.Clear(); Time.frameCount++; Time.unscaledTime+=.05f;
    input?.Invoke(); UpdateNotificationUi.Observe(); UpdateNotificationUi.Tick();
}
int Live() => NativeUi.Panels.Count(p=>p.Parent?.Kind=="Root" && !p.Get<bool>("isDisposed") && p.Get<bool>("visible"));
RuntimeObject NoticePanel() => NativeUi.Panels.Last(p=>p.Parent?.Kind=="Root");
IEnumerable<RuntimeObject> Descendants(RuntimeObject parent) => parent.Children.Values.SelectMany(child=>new[]{child}.Concat(Descendants(child)));
RuntimeObject Body() => Descendants(NoticePanel()).First(child=>child.Kind=="Label"); // The body label precedes the progress percentage label.
RuntimeObject ScrollView() => Descendants(NoticePanel()).Single(child=>child.Kind=="GList");
RuntimeObject Marker(bool thumb) => NoticePanel().Children.Values.Where(child=>child.Kind=="GGraph").ElementAt(thumb?1:0);
int MarkerWrites(RuntimeObject marker) => marker.SetCalls.Values.Sum()+marker.CallCounts.GetValueOrDefault("SetXY")+marker.CallCounts.GetValueOrDefault("SetSize");
RuntimeObject Button(string kind) => NativeUi.Buttons.Last(b=>b.Kind==kind && !b.Get<bool>("isDisposed"));
void Show(string version) { Inject(Available(version)); Frame(); Frame(); Check(Live()==1,"notice should show"); }
void Click(string kind,bool down) => Frame(()=>{GameUi.Hit.Add(Button(kind)); UInput.Down=down; UInput.Up=!down;});

Case("late Available and deferred successful-display acknowledgment",()=>{
    var s=new UpdateNotificationState(); s.Observe(new(ReleaseUpdateStatus.Checking),ReleaseChannel.Stable);
    Check(s.Next==null,"checking"); s.Observe(Available("2.0.0"),ReleaseChannel.Stable);
    Check(s.Next?.Key=="2.0.0" && !s.Shown("wrong"),"late candidate");
    s.Close(); Check(s.Next!=null,"defer keeps unseen"); Check(s.Shown("2.0.0") && s.Next==null,"confirm shown");
});
Case("once per run across reentry channel account and semantic prefixes",()=>{
    var s=new UpdateNotificationState(); s.Observe(Available("2.0.0"),ReleaseChannel.Stable); s.Shown("2.0.0"); s.Close();
    foreach(var tag in new[]{"2.0.0","v2.0.0","2.0.0+build"})
        foreach(var channel in Enum.GetValues<ReleaseChannel>()) {
            s.Observe(new(ReleaseUpdateStatus.NotConfigured),channel); s.Observe(Available(tag),channel);
            Check(s.Next==null,"duplicate "+tag);
        }
    s.Observe(Available("2.0.0-rc.1"),ReleaseChannel.Beta); Check(s.Next!=null,"different prerelease");
    var fresh=new UpdateNotificationState(); fresh.Observe(Available("2.0.0"),ReleaseChannel.Stable); Check(fresh.Next!=null,"new execution");
});
Case("invalid Available and nonfresh results cannot first-show stale data",()=>{
    foreach(var r in new[]{new ReleaseUpdateResult(ReleaseUpdateStatus.Available), Available("vv2.0.0"),Available("2.0")}) {
        var s=new UpdateNotificationState(); s.Observe(Available("2.0.0"),ReleaseChannel.Stable); s.Observe(r,ReleaseChannel.Stable);
        Check(s.Next==null,"invalid candidate");
    }
    foreach(var status in new[]{ReleaseUpdateStatus.Checking,ReleaseUpdateStatus.Stale,ReleaseUpdateStatus.Cancelled,ReleaseUpdateStatus.Failed,ReleaseUpdateStatus.Incomplete,ReleaseUpdateStatus.RateLimited}) {
        var s=new UpdateNotificationState(); s.Observe(Available("2.0.0"),ReleaseChannel.Stable);
        s.Observe(new(status,Release("2.0.0")),ReleaseChannel.Stable); Check(s.Next==null,"freshness "+status);
    }
});
Case("authorization and context clear private pending and active contents",()=>{
    foreach(var r in new[]{new ReleaseUpdateResult(ReleaseUpdateStatus.AuthenticationRequired),new(ReleaseUpdateStatus.AccessUnavailable),
        new(ReleaseUpdateStatus.Stale,Release("2.0.0"),Failure:ReleaseUpdateStatus.AccessUnavailable),
        new(ReleaseUpdateStatus.RateLimited),new(ReleaseUpdateStatus.UpToDate)}) {
        var s=new UpdateNotificationState(); s.Observe(Available("2.0.0"),ReleaseChannel.Stable); s.Shown("2.0.0");
        s.Observe(r,ReleaseChannel.Stable); Check(s.Active==null && s.Next==null,"private clear");
    }
    var pending=new UpdateNotificationState(); pending.Observe(Available("2.0.0"),ReleaseChannel.Stable);
    var generation=pending.ContextGeneration; pending.Observe(Available("2.0.0"),ReleaseChannel.Beta);
    Check(pending.ContextGeneration!=generation,"channel generation");
});
Case("new target defers without replacing active click target",()=>{
    var s=new UpdateNotificationState(); s.Observe(Available("2.0.0"),ReleaseChannel.Stable); s.Shown("2.0.0");
    s.Observe(Available("3.0.0"),ReleaseChannel.Stable); Check(s.Active?.Key=="2.0.0" && s.Next==null,"stable target");
    s.Close(); Check(s.Next?.Key=="3.0.0","next target");
});
Case("a release seen in Update Settings is not announced again",()=>{
    var s=new UpdateNotificationState(); s.Seen(); Check(s.Next==null,"nothing to mark");
    s.Observe(Available("2.0.0"),ReleaseChannel.Stable); s.Seen(); Check(s.Next==null && s.Active==null,"seen in settings");
    s.Observe(Available("v2.0.0"),ReleaseChannel.Stable); Check(s.Next==null,"later scans of the same release");
    s.Observe(Available("3.0.0"),ReleaseChannel.Stable); Check(s.Next?.Key=="3.0.0","a newer release still notifies");
});
Case("home safety independent blockers all room phases and stale team disposal",()=>{
    var good=new UpdateNotificationHome(true,true,true,true,false,0,0,0); Check(good.Eligible,"home");
    foreach(var bad in new[]{good with{Known=false},good with{HomeVisible=false},good with{CurrentHome=false},good with{ControlsReady=false},
        good with{Blocked=true},good with{TeamId=1},good with{ConnectRoomId=1}}) Check(!bad.Eligible,"blocker");
    foreach(var room in new[]{-1,1,2,3,4,5,100}) Check(!(good with{RoomState=room}).Eligible,"room state");
    Check(good.Eligible,"disposed TeamId=0 independent of stale MatchStatus");
});
Case("opening frame mismatch duplicate dispatch and keyboard input",()=>{
    var i=new UpdateNotificationInput();
    Check(i.Step(1,1,UpdateNoticeAction.Settings,true,true,false,false,false,new IntPtr(2))==UpdateNoticeAction.None,"opening");
    i.Step(2,1,UpdateNoticeAction.Settings,true,false,false,false,false,new IntPtr(2));
    Check(i.Step(3,1,UpdateNoticeAction.Close,false,true,false,false,false,new IntPtr(1))==UpdateNoticeAction.None,"mismatch");
    i.Step(4,1,UpdateNoticeAction.Settings,true,false,false,false,false,new IntPtr(2));
    Check(i.Step(5,1,UpdateNoticeAction.Settings,false,true,false,false,false,new IntPtr(2))==UpdateNoticeAction.Settings,"click");
    Check(i.Step(5,1,UpdateNoticeAction.Settings,false,true,false,false,false,new IntPtr(2))==UpdateNoticeAction.None,"duplicate");
    Check(i.Step(6,1,UpdateNoticeAction.None,false,false,true,false,false,IntPtr.Zero)==UpdateNoticeAction.Close,"escape");
    Check(i.Step(7,1,UpdateNoticeAction.None,false,false,false,true,false,IntPtr.Zero)==UpdateNoticeAction.Close,"enter");
    Check(i.Step(8,1,UpdateNoticeAction.None,false,false,false,false,true,IntPtr.Zero)==UpdateNoticeAction.Settings,"F8");
});
Case("both languages for every updater state",()=>{
    foreach(var language in new[]{"한국어","English"}) {
        ModText.Select(language);
        foreach(var label in new[]{"새 업데이트","업데이트 설정","닫기","확인 대기","다음 확인"})
            Check(language!="English" || !ModText.Text(label).Any(c=>c is >= '\uac00' and <= '\ud7a3'),"button translation "+label);
        var retryAt=DateTimeOffset.UnixEpoch.AddDays(20000);
        var queued=ModText.UpdateDetails(new(ReleaseUpdateStatus.RetryWaiting,RetryAt:retryAt),Plugin.Version);
        Check(queued.Contains(ModText.Text("확인 대기")) && queued.Contains(ModText.Text("다음 확인"))
            && queued.Contains(retryAt.ToLocalTime().ToString("MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture)) && !queued.Contains("Z"),"queued discovery shows its retry deadline");
        Check(language!="English" || !queued.Any(c=>c is >= '\uac00' and <= '\ud7a3'),"queued discovery translation");
        foreach(var status in Enum.GetValues<AutomaticUpdateStatus>()) {
            var text=ModText.UpdateNotificationDetails(Release("2.0.0-rc.2"),Plugin.Version,status,ReleaseUpdateStatus.Available);
            Check(text.Contains("2.0.0-rc.2") && !text.Contains("추정"),"literal version");
            if(language=="English") Check(!text.Any(c=>c is >= '\uac00' and <= '\ud7a3'),"translation "+status);
        }
        foreach(var status in Enum.GetValues<ReleaseSessionStatus>()) {
            var text=ModText.SessionDetails(status);
            Check(!string.IsNullOrWhiteSpace(text) && !text.Contains("eight hours") && !text.Contains("8시간"),"persistent authentication wording "+status);
            Check(language=="English" ? !text.Any(c=>c is >= '\uac00' and <= '\ud7a3') && text.Contains("Credential Manager")
                : text.Contains("자격 증명 관리자"),"localized persistent authentication "+status);
        }
    }
});
Case("notes move from hover to literal bilingual popup with full bounded content",()=>{
    const string malicious="<a href='javascript:alert(1)'>literal</a>[url=evil]plain[/url]\n현재 버전\u0000\u202e";
    var checkedAt=DateTimeOffset.UnixEpoch.AddDays(20000);var retryAt=checkedAt.AddMinutes(1);
    foreach(var language in new[]{"한국어","English"}) {
        ModText.Select(language);
        var release=Release("2.0.0") with{Notes=malicious};
        var hover=ModText.UpdateDetails(new(ReleaseUpdateStatus.Stale,release,checkedAt,ReleaseUpdateStatus.RateLimited,retryAt),Plugin.Version);
        Check(hover.Contains(Plugin.Version)&&hover.Contains(release.Version)&&hover.Contains(ModText.UpdateStatus(ReleaseUpdateStatus.Stale))
            &&hover.Contains(ModText.UpdateStatus(ReleaseUpdateStatus.RateLimited)),"hover keeps versions and both statuses");
        Check(hover.Contains(checkedAt.ToLocalTime().ToString("MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture))
            &&hover.Contains(retryAt.ToLocalTime().ToString("MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture)),"hover keeps check/retry timestamps");
        Check(!hover.Contains("javascript")&&!hover.Contains("[url=")&&!hover.Contains(ModText.Text("변경 사항")),"hover has no release-note body or heading");
        var popup=ModText.UpdateNotificationDetails(release,Plugin.Version,AutomaticUpdateStatus.Queued,ReleaseUpdateStatus.Stale);
        var notes=ReleaseUpdates.PlainText(malicious,ReleaseUpdates.MaxNotes);
        Check(popup.EndsWith(notes)&&popup.Contains("<a href='javascript:alert(1)'>")&&popup.Contains("[url=evil]")
            &&popup.Contains("현재 버전")&&!popup.Contains('\0')&&!popup.Contains('\u202e'),"notes remain literal and are not localized or parsed as markup");
        Check(popup.Contains(ModText.AutomaticStatus(AutomaticUpdateStatus.Queued))&&popup.Contains(ModText.UpdateStatus(ReleaseUpdateStatus.Stale)),"notes do not replace status/guidance");
        var empty=ModText.UpdateNotificationDetails(release with{Notes=""},Plugin.Version,AutomaticUpdateStatus.Ready,ReleaseUpdateStatus.Available);
        Check(empty.EndsWith(ModText.Text("변경 사항 없음")),"localized empty-note fallback");
        var longText=ModText.UpdateNotificationDetails(release with{Notes=new string('x',ReleaseUpdates.MaxNotes+100)},Plugin.Version,AutomaticUpdateStatus.Ready,ReleaseUpdateStatus.Available);
        Check(longText.EndsWith(new string('x',ReleaseUpdates.MaxNotes))&&!longText.Contains(new string('x',ReleaseUpdates.MaxNotes+1)),"popup shows full MaxNotes limit rather than old hover 700-character truncation");
        Check(language!="English"||!empty.Any(c=>c is >= '\uac00' and <= '\ud7a3'),"English empty notes/status translated");
    }
});
GameUi.Root!.Values["width"]=1920f; GameUi.Root.Values["height"]=1080f;
Case("linked Ui waits for late result then visible reveal",()=>{
    Inject(new(ReleaseUpdateStatus.Checking)); Frame(); Check(NativeUi.Panels.Count==0,"checking");
    Inject(Available("2.1.0")); Frame(); Check(Live()==1,"created"); Frame(); Check(Live()==1,"revealed");
});
Case("linked Ui updates queued status and language without recreate",()=>{
    var count=NativeUi.Panels.Count; Plugin.Automatic.Status=AutomaticUpdateStatus.Queued; Plugin.Automatic.Revision++; Frame();
    Check(Body().Get<string>("text").Contains("Applying"),"queue wording");
    ModText.Select("한국어"); Frame(); ModText.Select("English"); Frame(); for(var i=0;i<100;i++)Frame();
    Check(NativeUi.Panels.Count==count,"no recreate");
});
Case("long notes reuse native scroll list and preserve reading position on refresh",()=>{
    Frame(()=>UInput.Escape=true);
    var notes=string.Join("\n",Enumerable.Repeat("한글 <literal> [url=not-action] long release note line.",80));
    Inject(new(ReleaseUpdateStatus.Available,Release("2.1.1") with{Notes=notes}));Frame();Frame();
    Check(Live()==1,"long-note popup shown");
    var panel=NoticePanel();var view=ScrollView();var scroll=view.ScrollPane!;var body=Body();var content=body.Parent!;
    var track=Marker(false);var thumb=Marker(true);
    Check(NativeUi.ScrollTemplates.Last().Get<bool>("isDisposed")&&!view.Get<bool>("isDisposed")&&view.Parent==panel,"reparented native list survives template disposal");
    Check(view.Relations!.RelationClears==1&&view.RemoveChildrenCalls==1&&view.Children.Count==1,"native relations and pooled placeholder children cleared exactly once");
    Check(view.Get<float>("x")==110&&view.Get<float>("y")==110&&view.Get<float>("width")==840&&view.Get<float>("height")==360,"body viewport reaches the buttons when no progress bar is shown");
    Check(view.Get<bool>("touchable")&&view.Get<bool>("opaque")&&!view.Get<bool>("autoResizeItem")&&view.Get<int>("layout")==0
        &&view.Get<int>("selectionMode")==3&&!view.Get<bool>("scrollItemToViewOnClick"),"native clipping/hit/selection contract");
    Check(scroll.Get<bool>("mouseWheelEnabled")&&!scroll.Get<bool>("bouncebackEffect")&&scroll.Get<float>("scrollStep")==48,"native wheel remains enabled");
    Check(!body.Get<bool>("touchable")&&!body.Get<bool>("UBBEnabled")&&!body.Get<bool>("singleLine")&&body.Get<int>("autoSize")==2
        &&body.Get<int>("fontSize")==30&&body.Get<int>("align")==0,"plain wrapped native text, no shrink-to-fit");
    Check(body.Get<float>("width")==scroll.Get<float>("viewWidth")&&content.Get<float>("width")==body.Get<float>("width")
        &&content.Get<float>("height")==body.Get<float>("textHeight")&&scroll.Get<float>("contentHeight")>scroll.Get<float>("viewHeight"),"content height exposes all long text to native scroll");
    Check(body.Get<string>("text").EndsWith(ReleaseUpdates.PlainText(notes,ReleaseUpdates.MaxNotes)),"full bounded notes present");
    Check(track.Get<bool>("visible")&&thumb.Get<bool>("visible")&&!track.Get<bool>("touchable")&&!thumb.Get<bool>("touchable"),"overflow marker visible but never takes native input");
    Check(track.Parent==panel&&thumb.Parent==panel&&track.Get<float>("x")==958&&track.Get<float>("y")==110
        &&track.Get<float>("height")==360&&thumb.Get<float>("x")==958&&thumb.Get<float>("y")==110
        &&thumb.Get<float>("height")>=24&&thumb.Get<float>("height")<360,"marker fits beside body, above action button area");
    foreach(var button in new[]{Button("Button_ReturnRounded"),Button("Button_ConfirmRounded")})
        Check(button.Parent==panel&&button.Get<float>("y")>=view.Get<float>("y")+view.Get<float>("height"),"action buttons remain outside scrolling clip");
    Check(scroll.ScrollTopCalls==1,"only initial Create resets scroll");scroll.Values["posY"]=240f;
    var markerY=thumb.Get<float>("y");Frame();
    Check(thumb.Get<float>("y")>markerY&&thumb.Get<float>("y")+thumb.Get<float>("height")<=470,"native scroll position moves marker within body bounds");
    var trackWrites=MarkerWrites(track);var thumbWrites=MarkerWrites(thumb);Frame();Frame();
    Check(MarkerWrites(track)==trackWrites&&MarkerWrites(thumb)==thumbWrites,"unchanged frames do not write marker visibility/size/position");
    var count=NativeUi.Panels.Count;var bounds=view.BoundsCalls;var opens=ModUi.Opens;
    foreach(var status in new[]{AutomaticUpdateStatus.Preparing,AutomaticUpdateStatus.Queued,AutomaticUpdateStatus.Ready}) {
        Plugin.Automatic.Status=status;Plugin.Automatic.Revision++;Frame();
        Check(scroll.Get<float>("posY")==240&&scroll.ScrollTopCalls==1&&view.BoundsCalls>bounds,"status refresh preserves scroll position and updates content bounds");bounds=view.BoundsCalls;
        Check(body.Get<string>("text").Contains(ModText.AutomaticStatus(status)),"live status still refreshed");
    }
    foreach(var language in new[]{"한국어","English"}) {ModText.Select(language);Frame();Check(scroll.Get<float>("posY")==240&&scroll.ScrollTopCalls==1,"language refresh preserves reading position");}
    var oldHeight=content.Get<float>("height");var fontRevision=ModFont.Revision;
    body.Values["fontHeightScale"]=1.6f;ModFont.Advance();
    ModFont.AdvanceOnNextTrack=ModFont.AdvanceOnNextButtonStyle=true;bounds=view.BoundsCalls;
    Frame();
    Check(ModFont.Revision==fontRevision+3&&view.BoundsCalls==bounds+1&&content.Get<float>("height")>oldHeight
        &&content.Get<float>("height")==body.Get<float>("textHeight"),"delayed font fallback remeasures body and content bounds");
    Check(scroll.Get<float>("posY")==240&&scroll.ScrollTopCalls==1,"font fallback refresh preserves reading position");
    bounds=view.BoundsCalls;trackWrites=MarkerWrites(track);thumbWrites=MarkerWrites(thumb);Frame();Frame();
    Check(view.BoundsCalls==bounds&&MarkerWrites(track)==trackWrites&&MarkerWrites(thumb)==thumbWrites,"all body/button font revisions captured after refresh without idle refresh or marker loop");
    Inject(new(ReleaseUpdateStatus.Available,Release("2.1.1") with{Notes=notes}));Frame();
    Check(NativeUi.Panels.Count==count&&ModUi.Opens==opens&&scroll.Get<float>("posY")==240,"release revision neither recreates nor invokes settings");
    Frame(()=>{GameUi.Hit.Add(body);GameUi.Hit.Add(view);UInput.Down=true;});
    Frame(()=>{GameUi.Hit.Add(body);GameUi.Hit.Add(view);UInput.Up=true;});
    Check(Live()==1&&ModUi.Opens==opens,"scroll body clicks do not dispatch action buttons");
    Frame(()=>UInput.Escape=true);
    Check(view.Get<bool>("isDisposed")&&content.Get<bool>("isDisposed")&&body.Get<bool>("isDisposed")&&scroll.Get<bool>("isDisposed")
        &&track.Get<bool>("isDisposed")&&thumb.Get<bool>("isDisposed"),"close releases owned scroll subtree and position-only marker");
    foreach(var name in new[]{"_scrollTrack","_scrollThumb","_scrollMarker"})
        Check(typeof(UpdateNotificationUi).GetField(name,BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)==null,"closed marker ownership reset");
    Show("2.1.2");Check(ScrollView().ScrollPane!.ScrollTopCalls==1&&ScrollView().ScrollPane!.Get<float>("posY")==0,"fresh popup starts at top");
});
Case("short notes with non-overflow native metrics hide position-only marker",()=>{
    Frame(()=>UInput.Escape=true);Inject(new(ReleaseUpdateStatus.Available,Release("2.1.3") with{Notes=""}));Frame();Frame();
    var body=Body();var view=ScrollView();var scroll=view.ScrollPane!;var track=Marker(false);var thumb=Marker(true);
    body.Values["fontHeightScale"]=.5f;ModFont.Advance();Frame();
    Check(body.Get<string>("text").EndsWith(ModText.Text("변경 사항 없음"))&&scroll.Get<float>("contentHeight")==scroll.Get<float>("viewHeight"),"short notes use non-overflow content metrics");
    Check(!track.Get<bool>("visible")&&!thumb.Get<bool>("visible")&&!track.Get<bool>("touchable")&&!thumb.Get<bool>("touchable"),"non-overflow track/thumb hidden and non-interactive");
    var trackWrites=MarkerWrites(track);var thumbWrites=MarkerWrites(thumb);Frame();Frame();
    Check(MarkerWrites(track)==trackWrites&&MarkerWrites(thumb)==thumbWrites&&scroll.ScrollTopCalls==1,"unchanged hidden marker does not reset or write native scroll state");
});
Case("download ready queued cancellation prerequisites and faults preserve preferences",()=>{
    var count=NativeUi.Panels.Count;
    foreach(var download in new[]{false,true}) foreach(var apply in new[]{false,true}) {
        Plugin.Automatic.AutoDownload=download; Plugin.Automatic.ApplyAfterExit=apply;
        foreach(var status in new[]{AutomaticUpdateStatus.Downloading,AutomaticUpdateStatus.Preparing,AutomaticUpdateStatus.Ready,
            AutomaticUpdateStatus.Queued,AutomaticUpdateStatus.Cancelling,AutomaticUpdateStatus.Cancelled,
            AutomaticUpdateStatus.ManualUpgradeRequired,AutomaticUpdateStatus.FaultDisabled,AutomaticUpdateStatus.FaultStorageFailed}) {
            Plugin.Automatic.Status=status; Plugin.Automatic.Revision++; Frame();
            Check(Live()==1 && NativeUi.Panels.Count==count,"state transition recreates notice");
            Check(Plugin.Automatic.AutoDownload==download && Plugin.Automatic.ApplyAfterExit==apply,"preference neutrality");
        }
    }
});
Case("linked Ui mismatch then Close does not open settings",()=>{
    var closeX=NoticePanel().Children.Values.First(c=>c.Kind=="Com_PopUpWindow_Bottom").Children["closeButton"];
    Frame(()=>{GameUi.Hit.Add(closeX); UInput.Down=true;});
    Click("Button_ReturnRounded",false); Check(Live()==1,"same action different control must not dispatch");
    Click("Button_ConfirmRounded",true); Click("Button_ReturnRounded",false); Check(Live()==1 && ModUi.Opens==0,"mismatch");
    Click("Button_ReturnRounded",true); Click("Button_ReturnRounded",false); Check(Live()==0 && ModUi.Opens==0,"close");
    Frame(); Check(Live()==0,"once after close");
});
Case("native blocker deferred then F8 settings dispatch once",()=>{
    GameUi.Home=GameUi.Home with{Blocked=true}; Inject(Available("2.2.0")); Frame(); Check(Live()==0,"blocked");
    GameUi.Home=GameUi.Home with{Blocked=false}; Frame(); Frame(); Check(Live()==1,"eligible");
    Frame(()=>UInput.Settings=true); UpdateNotificationUi.Tick();
    Check(Live()==0 && ModUi.Opens==1 && UpdateNotificationUi.ConsumedInput,"dispatch once");
});
Case("a release read in Update Settings does not open the notice when they close",()=>{
    GameUi.Home=GameUi.Home with{Blocked=true}; ModUi.ShowsUpdates=true; Inject(Available("2.2.5")); Frame(); Check(Live()==0,"settings open");
    ModUi.ShowsUpdates=false; GameUi.Home=GameUi.Home with{Blocked=false}; Frame(); Frame(); Check(Live()==0,"seen in settings");
    GameUi.Home=GameUi.Home with{Blocked=true}; Inject(Available("2.2.6")); Frame();
    GameUi.Home=GameUi.Home with{Blocked=false}; Frame(); Frame(); Check(Live()==1,"settings on another tab keep the notice");
    Frame(()=>UInput.Escape=true); Check(Live()==0 && ModUi.Opens==1,"Escape");
});
Case("matching play suppress and Return only dismisses notice",()=>{
    GameUi.Home=GameUi.Home with{TeamId=4}; Inject(Available("2.3.0")); Frame(); Check(Live()==0,"match");
    GameUi.Home=GameUi.Home with{TeamId=0,RoomState=4}; Frame(); Check(Live()==0,"play");
    GameUi.Home=GameUi.Home with{RoomState=0}; Frame(); Frame(); Check(Live()==1,"home");
    Frame(()=>UInput.Enter=true); Check(Live()==0 && ModUi.Opens==1,"Return");
});
Case("home departure and root recreation release owned UI",()=>{
    Show("2.4.0"); GameUi.Home=GameUi.Home with{CurrentHome=false}; Frame(); Check(Live()==0,"departure");
    GameUi.Home=GameUi.Home with{CurrentHome=true}; Frame(); Check(Live()==0,"once");
    Show("2.5.0"); GameUi.Root=new RuntimeObject("Root"); GameUi.Root.Values["width"]=1280f; GameUi.Root.Values["height"]=720f;
    Frame(); Check(Live()==0,"root changed");
});
Case("unshown authorization clear then fresh arrival and Escape",()=>{
    Inject(Available("2.6.0")); Frame(); Inject(new(ReleaseUpdateStatus.AccessUnavailable)); Frame(); Check(Live()==0,"cleared");
    Inject(Available("2.6.0")); Frame(); Frame(); Check(Live()==1,"not marked before visible");
    Frame(()=>UInput.Escape=true); Check(Live()==0,"Escape");
});
Case("focus and opening click defer without stealing input",()=>{
    Inject(Available("2.7.0")); Application.isFocused=false; Frame(); Check(Live()==0,"focus");
    Application.isFocused=true; Frame(()=>UInput.Down=true); Check(Live()==0,"existing click");
    Frame(); Frame(); Check(Live()==1,"next frame");
    Click("Button_ConfirmRounded",true); Application.isFocused=false; Frame();
    Application.isFocused=true; Click("Button_ConfirmRounded",false);
    Check(Live()==1 && ModUi.Opens==1,"focus loss clears pending press");
    Frame(()=>UInput.Escape=true);
});
Case("native close X and mouse settings each dispatch once",()=>{
    Show("2.7.1");
    var closeX=NoticePanel().Children.Values.First(c=>c.Kind=="Com_PopUpWindow_Bottom").Children["closeButton"];
    Frame(()=>{GameUi.Hit.Add(closeX); UInput.Down=true;});
    Frame(()=>{GameUi.Hit.Add(closeX); UInput.Up=true;});
    Check(Live()==0 && ModUi.Opens==1,"close X");
    Show("2.7.2"); Click("Button_ConfirmRounded",true); Click("Button_ConfirmRounded",false);
    UpdateNotificationUi.Tick();
    Check(Live()==0 && ModUi.Opens==2 && UpdateNotificationUi.ConsumedInput,"mouse settings once");
});
Case("ordinary notifications OFF restores owned UI and leaves unseen releases available",()=>{
    Show("2.7.40"); Plugin.UpdateNotifications.Value=false; Frame();
    Check(Live()==0 && Compatibility.Blocked.Count==0,"OFF releases the ordinary popup without a compatibility failure");
    Inject(Available("2.7.41")); Frame(); Frame();
    Check(Live()==0,"a newer ordinary release stays hidden while OFF");
    Plugin.UpdateNotifications.Value=true; Frame(); Frame();
    Check(Live()==1 && Body().Get<string>("text").Contains("2.7.41"),"re-ON can display the release not acknowledged while hidden");
    Frame(()=>UInput.Escape=true);
});
Case("w2 transition and stale generation never consume notification identity",()=>{
    var s=new UpdateNotificationState(); var r=Release("4.0.0") with{Generation=7,TransitionCandidate=true};
    s.Observe(new(ReleaseUpdateStatus.Available,r),ReleaseChannel.Stable,7); Check(s.Next==null,"transition hidden");
    r=r with{TransitionCandidate=false};s.Observe(new(ReleaseUpdateStatus.Available,r),ReleaseChannel.Stable,8);Check(s.Next==null,"old generation");
    s.Observe(new(ReleaseUpdateStatus.Available,r),ReleaseChannel.Stable,7);Check(s.Next!=null,"not consumed");
});
Case("w2 snapshot mismatch avoids target-specific ready and queued statements",()=>{
    Show("2.7.3"); Plugin.Automatic.SnapshotMismatch=true;
    foreach(var status in new[]{AutomaticUpdateStatus.Ready,AutomaticUpdateStatus.Queued}) {
        Plugin.Automatic.Status=status;Plugin.Automatic.Revision++;Frame();
        var text=Body().Get<string>("text");
        Check(text.Contains("No operation status matches this release") && !text.Contains("Waiting for the game") && !text.Contains("The download is ready"),"stale target assertion");
    }
    Plugin.Automatic.SnapshotMismatch=false;Frame(()=>UInput.Escape=true);
});
foreach(var fault in new[]{"getter","visible","dispose"}) Case("cleanup retains ownership and retries "+fault,()=>{
    Show(fault=="getter"?"2.7.4":fault=="visible"?"2.7.5":"2.7.6");var panel=NoticePanel();
    if(fault=="getter")RuntimeObject.FailNextDisposedRead=true;
    else if(fault=="visible")RuntimeObject.FailNextVisibleWrite=true;
    else RuntimeObject.FailNextDispose=true;
    GameUi.Home=GameUi.Home with{CurrentHome=false};Frame();
    Check(!(bool)panel.Values["touchable"] && ((bool)panel.Values["isDisposed"] || !(bool)panel.Values["visible"]),"hit layer removed or disposed despite cleanup failure");
    Frame();Check((bool)panel.Values["isDisposed"] && typeof(UpdateNotificationUi).GetField("_panel",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)==null,"owned disposal confirmed");
    Check(Compatibility.Blocked.SetEquals(new[]{UpdateNotificationUi.Feature}),"feature-local failure");
    Compatibility.Blocked.Clear();GameUi.Home=GameUi.Home with{CurrentHome=true};
});
Case("invalid native viewport width fails only the notification and releases scroll clone",()=>{
    var sequence=0;
    foreach(var width in new[]{0f,-1f,float.NaN,float.PositiveInfinity}) {
        RuntimeObject.NextViewWidth=width;Inject(Available("2.7."+(20+sequence++)));Frame();
        Check(Compatibility.Blocked.SetEquals(new[]{UpdateNotificationUi.Feature}),"width failure is feature-local");
        Check(Live()==0&&Descendants(NoticePanel()).All(item=>item.Get<bool>("isDisposed")),"partial native scroll subtree released");
        Check(typeof(UpdateNotificationUi).GetField("_view",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)==null,"owned scroll reference cleared");
        Compatibility.Blocked.Clear();
    }
});
Case("missing native scroll template leaves no owned hit layer",()=>{
    RuntimeObject.FailNextScrollTemplate=true;Inject(Available("2.7.30"));Frame();
    Check(Compatibility.Blocked.SetEquals(new[]{UpdateNotificationUi.Feature})&&Live()==0,"scroll creation failure isolated and cleaned");
    Check(typeof(UpdateNotificationUi).GetField("_panel",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)==null,"partial panel disposal confirmed");
    Compatibility.Blocked.Clear();
});
Case("partial creation failure isolates notification breaker and hit layer",()=>{
    RuntimeObject.FailNextFrame=true; Inject(Available("2.8.0")); Frame();
    Check(Compatibility.Blocked.SetEquals(new[]{UpdateNotificationUi.Feature}),"only own breaker");
    Check(Live()==0 && ModUi.Opens==2,"partial cleanup");
});

var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var handler=new DelayedHandler(entered,finish);
using(var client=new HttpClient(handler))
using(var updates=new ReleaseUpdates("1.0.0-rc.2","Fixture/Repo",client,new FixtureAuthentication()))
{
    updates.Tick(DateTimeOffset.UtcNow,true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(updates.Result.Status==ReleaseUpdateStatus.Checking,"real worker pending");
    finish.SetResult();
    for(var i=0;i<200 && updates.Result.Status==ReleaseUpdateStatus.Checking;i++) { await Task.Delay(5); updates.Tick(DateTimeOffset.UtcNow,true); }
    Check(updates.Result.Status==ReleaseUpdateStatus.Available && handler.Requests==1,"late completion single flight");
    var notice=new UpdateNotificationState(); notice.Observe(updates.Result,updates.Channel);
    Check(notice.Next?.Key=="3.0.0","actual async result");
    cases.Add("linked production delayed mock HTTP completes without menu reentry"); Console.WriteLine("PASS "+cases[^1]);
}
void ReviewSwitch()
{
    Compatibility.Blocked.Clear(); UpdateNotificationUi.Clear(); Plugin.Automatic.CanReviewTransition=true; Plugin.Automatic.ProposalCurrent=true;
    Plugin.Updates.SetChannel(ReleaseChannel.Stable);
    Inject(new(ReleaseUpdateStatus.Available,Release("2.0.0") with {
        Repository=BetterAstralParty.Updating.ReleaseFeedPolicy.StableRepository,
        RepositoryId=BetterAstralParty.Updating.ReleaseFeedPolicy.StableRepositoryId, FeedChannel="Stable",
        ReleaseId=91,ZipAssetId=92,DescriptorAssetId=93,SignatureAssetId=94,TransitionCandidate=true }));
    Check(UpdateNotificationUi.CanConfirmTransition,"fresh complete signed-proposal target can be requested");
    UpdateNotificationUi.BeginTransition();
    for(var i=0;i<200 && Live()==0;i++){Frame();Thread.Sleep(2);}
    Check(Live()==1,"review uses the existing native popup");
}
Case("explicit lower-channel native confirmation displays source target and one-shot apply",()=>{
    Plugin.Automatic.ProposalWorker=null; ReviewSwitch();
    var before=Plugin.Automatic.ConfirmCalls;
    Check(Body().Get<string>("text").Contains("3.0.0")&&Body().Get<string>("text").Contains("2.0.0"),"both versions are visible before approval");
    Check(Plugin.Automatic.ConfirmCalls==before,"showing review does not approve");
    Check(ScrollView().Get<float>("height")==360,"review text reaches the buttons without a progress bar");
    Click("Button_ConfirmRounded",true);Click("Button_ConfirmRounded",false);
    Check(Plugin.Automatic.ConfirmCalls==before+1&&Plugin.Automatic.ApplyOnce&&Live()==1,"only exact confirm click approves once and keeps the window");
    Plugin.Automatic.Status=AutomaticUpdateStatus.Downloading;Plugin.Automatic.Revision++;Frame();
    var exit=Button("Button_ConfirmRounded");
    Check(exit.Get<string>("title")==ModText.ChannelTransitionExit&&exit.Get<bool>("grayed")&&Body().Get<string>("text").Contains("2.0.0"),"Exit and Switch stays gray while downloading");
    UpdateProgress.Shared.Observe(AutomaticUpdateStatus.Downloading,"2.0.0",true,100,400);Frame();
    Check(ScrollView().Get<float>("height")==300,"progress bar shortens the text to keep its row clear");
    var quits=UnityEngine.Application.QuitCalls;
    Click("Button_ConfirmRounded",true);Click("Button_ConfirmRounded",false);
    Check(UnityEngine.Application.QuitCalls==quits&&Live()==1,"gray Exit and Switch never closes the game");
    Plugin.Automatic.Status=AutomaticUpdateStatus.Committed;Plugin.Automatic.Revision++;Frame();
    Check(!Button("Button_ConfirmRounded").Get<bool>("grayed")&&Body().Get<string>("text")==ModText.ChannelTransitionReady("2.0.0"),"committed switch enables Exit and Switch");
    Plugin.Automatic.Status=AutomaticUpdateStatus.TransitionRequired;Plugin.Automatic.Revision++;Frame();
    Check(!Button("Button_ConfirmRounded").Get<bool>("grayed")&&Body().Get<string>("text")==ModText.ChannelTransitionReady("2.0.0"),"a later status refresh cannot undo a seen commit");
    Click("Button_ConfirmRounded",true);Click("Button_ConfirmRounded",false);
    Check(UnityEngine.Application.QuitCalls==quits+1&&Live()==0,"Exit and Switch closes the game normally once");
    UpdateProgress.Shared.Observe(AutomaticUpdateStatus.Preparing,"",false);UpdateProgress.Shared.Observe(AutomaticUpdateStatus.Cancelled,"",false);
    Plugin.Automatic.Status=AutomaticUpdateStatus.Idle;Plugin.Automatic.Revision++;
});
Case("update progress stages never step back and a commit stays until restart",()=>{
    var p=new UpdateProgress();
    p.Observe(AutomaticUpdateStatus.Idle,"",false);Check(!p.Visible,"idle shows nothing");
    var steps=new List<int>();
    foreach(var s in new[]{AutomaticUpdateStatus.Preparing,AutomaticUpdateStatus.Downloading,AutomaticUpdateStatus.Preparing,AutomaticUpdateStatus.Ready,AutomaticUpdateStatus.Preparing,AutomaticUpdateStatus.Queued,AutomaticUpdateStatus.Committed})
    { p.Observe(s,"1.5.0",true); steps.Add(p.Step); }
    Check(string.Join(",",steps)=="1,2,3,4,4,4,5"&&p.Done&&p.Version=="1.5.0"&&p.Transition,"check, download, verify, apply, done in order");
    p.Observe(AutomaticUpdateStatus.TransitionRequired,"",false);Check(p.Done&&p.Visible,"a later status cannot undo a commit");
    var q=new UpdateProgress();q.Observe(AutomaticUpdateStatus.Downloading,"2.0.0",false);q.Observe(AutomaticUpdateStatus.FaultDisabled,"",false);
    Check(q.Stopped&&q.Step==2,"a failure keeps the reached step and marks it stopped");
    q.Observe(AutomaticUpdateStatus.Preparing,"2.0.0",false);Check(!q.Stopped&&q.Step==1,"a new attempt starts over");
    q.Observe(AutomaticUpdateStatus.Cancelled,"",false);Check(!q.Visible,"cancel hides the capsule");
});
Case("update progress percent follows received bytes and a stop keeps its code",()=>{
    var p=new UpdateProgress();
    p.Observe(AutomaticUpdateStatus.Preparing,"1.5.0",false);Check(p.Percent==2,"operation guard starts at 2%");
    p.Observe(AutomaticUpdateStatus.Downloading,"1.5.0",false,0,400);Check(p.Percent==5,"download starts at 5%");
    p.Observe(AutomaticUpdateStatus.Downloading,"1.5.0",false,200,400);Check(p.Percent==45&&p.Read==200&&p.Total==400,"half the bytes is 45%");
    p.Observe(AutomaticUpdateStatus.Preparing,"1.5.0",false);Check(p.Percent==88,"verification 88%");
    p.Observe(AutomaticUpdateStatus.Ready,"1.5.0",false);p.Observe(AutomaticUpdateStatus.Queued,"1.5.0",false);Check(p.Percent==95,"applying 95%");
    p.Observe(AutomaticUpdateStatus.Committed,"",false);Check(p.Percent==100&&p.Read==400,"done 100%");
    var q=new UpdateProgress();q.Observe(AutomaticUpdateStatus.Downloading,"2.0.0",false,100,400);
    q.Observe(AutomaticUpdateStatus.FaultDisabled,"",false,0,0,UpdateProgress.FaultCode("Download","Timeout"),"timeout");
    Check(q.Stopped&&q.Code=="BAP-UPD-DOWNLOAD-TIMEOUT"&&q.Reason=="timeout"&&q.Percent==25,"a stop keeps its percentage, code and reason");
    Check(ModText.UpdateStopDetails("2.0.0",q.Percent,q.Reason,q.Code).Contains("BAP-UPD-DOWNLOAD-TIMEOUT"),"window text carries the code");
    var r=new UpdateProgress();r.Observe(AutomaticUpdateStatus.Downloading,"2.0.0",false);r.Observe(AutomaticUpdateStatus.RecoveryRequired,"",false);
    Check(r.Code=="BAP-UPD-RECOVERYREQUIRED","a stop without a fault record still has a code");
});
Case("a stopped switch shows the reason and Try Again re-enables updates",()=>{
    Plugin.Automatic.ProposalWorker=null; ReviewSwitch();
    Click("Button_ConfirmRounded",true);Click("Button_ConfirmRounded",false);
    UpdateProgress.Shared.Observe(AutomaticUpdateStatus.Downloading,"2.0.0",true,100,400);
    UpdateProgress.Shared.Observe(AutomaticUpdateStatus.FaultDisabled,"",false,0,0,"BAP-UPD-DOWNLOAD-NETWORK","network");
    Plugin.Automatic.Status=AutomaticUpdateStatus.FaultDisabled;Plugin.Automatic.Revision++;Frame();
    Check(Button("Button_ConfirmRounded").Get<string>("title")==ModText.UpdateRetry&&Body().Get<string>("text").Contains("BAP-UPD-DOWNLOAD-NETWORK"),"stopped window shows the code and Try Again");
    Plugin.Automatic.CanReactivate=true;var calls=Plugin.Automatic.ReactivateCalls;
    Click("Button_ConfirmRounded",true);Click("Button_ConfirmRounded",false);
    Check(Plugin.Automatic.ReactivateCalls==calls+1&&Live()==0,"Try Again re-enables updates once and closes the window");
    Plugin.Automatic.CanReactivate=false;Plugin.Automatic.Status=AutomaticUpdateStatus.Idle;Plugin.Automatic.Revision++;
    UpdateProgress.Shared.Observe(AutomaticUpdateStatus.Preparing,"",false);UpdateProgress.Shared.Observe(AutomaticUpdateStatus.Cancelled,"",false);
});
Case("a newer scan finding the same release keeps the review",()=>{
    Plugin.Automatic.ProposalWorker=null; ReviewSwitch();
    Inject(Plugin.Updates.Result with {Release=Plugin.Updates.Result.Release! with{Generation=Plugin.Updates.Result.Release!.Generation+7}});
    Frame();Frame();
    Check(Live()==1&&Button("Button_ConfirmRounded").Get<string>("title")==ModText.ChannelTransitionConfirm,"same target survives a rescan");
    UpdateNotificationUi.Clear();
});
Case("ordinary notification OFF preserves an explicit channel-switch approval",()=>{
    Plugin.UpdateNotifications.Value=false; Plugin.Automatic.ProposalWorker=null; ReviewSwitch();
    var before=Plugin.Automatic.ConfirmCalls; Frame();
    Check(Live()==1 && Plugin.Automatic.ConfirmCalls==before,"explicit approval remains visible without automatic confirmation");
    Click("Button_ConfirmRounded",true); Click("Button_ConfirmRounded",false);
    Check(Live()==1 && Plugin.Automatic.ConfirmCalls==before+1,"only the explicit confirm still applies a one-shot transition");
    UpdateNotificationUi.Clear();
    Plugin.UpdateNotifications.Value=true;
});
Case("Escape Enter and close never approve a channel switch",()=>{
    foreach(var key in new[]{"escape","enter","close"}){
        ReviewSwitch();var before=Plugin.Automatic.ConfirmCalls;
        if(key=="close"){Click("Button_ReturnRounded",true);Click("Button_ReturnRounded",false);}
        else Frame(()=>{UInput.Escape=key=="escape";UInput.Enter=key=="enter";});
        Check(Plugin.Automatic.ConfirmCalls==before&&Live()==0,"dismissal is not approval: "+key);
    }
});
Case("changed asset or proposal generation retires approval and shows retry guidance",()=>{
    foreach(var change in new[]{"asset","proposal"}){
        ReviewSwitch();var before=Plugin.Automatic.ConfirmCalls;
        if(change=="asset") Inject(Plugin.Updates.Result with {Release=Plugin.Updates.Result.Release! with{SignatureAssetId=95}});
        else Plugin.Automatic.ProposalCurrent=false;
        Frame();Frame();
        Check(Plugin.Automatic.ConfirmCalls==before&&Body().Get<string>("text").Contains(ModText.ChannelTransitionUnavailable),"stale target cannot approve: "+change);
        Check(Button("Button_ConfirmRounded").Get<string>("title")!=ModText.ChannelTransitionConfirm,"failure popup has no approval control");
        UpdateNotificationUi.Clear();
    }
});
Case("cancelled proposal worker cannot resurrect a hidden approval",()=>{
    UpdateNotificationUi.Clear();Plugin.Automatic.ProposalCurrent=true;Plugin.Automatic.CanReviewTransition=true;
    var worker=new TaskCompletionSource<ChannelTransitionProposal>(TaskCreationOptions.RunContinuationsAsynchronously);Plugin.Automatic.ProposalWorker=worker;
    Check(UpdateNotificationUi.CanConfirmTransition,"fresh target exists");UpdateNotificationUi.BeginTransition();UpdateNotificationUi.Clear();
    worker.SetResult(new(new("fixture","Stable","2.0.0",new string('A',64))));
    Thread.Sleep(10);Frame();Frame();Check(Live()==0,"late worker is discarded");Plugin.Automatic.ProposalWorker=null;
});
Case("pending explicit review suppresses a newer ordinary notice and reports changed target",()=>{
    UpdateNotificationUi.Clear();Plugin.Automatic.ProposalCurrent=true;Plugin.Automatic.CanReviewTransition=true;
    Inject(Plugin.Updates.Result with{Release=Plugin.Updates.Result.Release! with{Version="9.0.0",TransitionCandidate=false}});
    var worker=new TaskCompletionSource<ChannelTransitionProposal>(TaskCreationOptions.RunContinuationsAsynchronously);Plugin.Automatic.ProposalWorker=worker;
    UpdateNotificationUi.BeginTransition();Frame();Frame();Check(Live()==0,"ordinary notice cannot occupy confirmation UI while proposal is pending");
    Inject(Plugin.Updates.Result with{Release=Plugin.Updates.Result.Release! with{SignatureAssetId=96}});Frame();Frame();
    Check(Live()==1&&Body().Get<string>("text").StartsWith(ModText.ChannelTransitionUnavailable)&&Body().Get<string>("text").Contains(ModText.ChannelTransitionReason("changed")),"changed pending target reports a stop and its reason without approval");
    worker.SetResult(new(new("fixture","Stable","9.0.0",new string('A',64))));Thread.Sleep(10);
    UpdateNotificationUi.Clear();Plugin.Automatic.ProposalWorker=null;
});
Plugin.Automatic.CanReviewTransition=false;
ChannelDiagnosticsFixture.Run(Inject, Frame, ReviewSwitch, Click);
Console.WriteLine($"RESULT {cases.Count} cases passed. Managed/source fixtures only. In-game UI unexecuted.");

sealed class FixtureAuthentication : IReleaseAuthentication
{
    public string CacheIdentity => "nonsecret-fixture";
    public bool TryAuthorize(HttpRequestMessage request) { request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer","inert-not-a-credential"); return true; }
}
sealed class DelayedHandler(TaskCompletionSource entered,TaskCompletionSource finish) : HttpMessageHandler
{
    internal int Requests;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Requests++; entered.TrySetResult(); await finish.Task.WaitAsync(token);
        return new(HttpStatusCode.OK) { Content=new StringContent("""[{"tag_name":"3.0.0","name":"fixture","body":"fixture","html_url":"https://github.com/Fixture/Repo/releases/tag/3.0.0","draft":false,"prerelease":false}]""",Encoding.UTF8,"application/json"),RequestMessage=request };
    }
}
