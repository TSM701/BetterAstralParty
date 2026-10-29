// Strict managed substitutes. No game/native bridge, real input, settings IO or live HTTP.
namespace UnityEngine
{
    internal static class Time { internal static int frameCount; internal static float unscaledTime; }
    internal static class Application { internal static bool isFocused = true; internal static int QuitCalls; internal static void Quit() => QuitCalls++; }
    internal readonly record struct Color(float r,float g,float b,float a=1f);
    internal enum KeyCode { Escape, Return, F8 }
    internal static class Input
    {
        internal static bool Down, Up, Escape, Enter, Settings;
        internal static bool anyKeyDown => Escape || Enter || Settings;
        internal static bool GetMouseButtonDown(int button) => button == 0 && Down;
        internal static bool GetMouseButtonUp(int button) => button == 0 && Up;
        internal static bool GetKeyDown(KeyCode key) => key switch { KeyCode.Escape => Escape, KeyCode.Return => Enter, _ => Settings };
        internal static void Reset() { Down = Up = Escape = Enter = Settings = false; }
    }
}
namespace BetterAstralParty.Updating
{
    internal sealed class ChannelTransitionIntent {
        internal static ChannelTransitionReview Review(WindowsFileFence fence, VerifiedUpdateDescriptor descriptor)
            => new("Beta", "3.0.0", descriptor);
        internal static ChannelTransitionIntent Approve(ChannelTransitionReview review) => new();
    }
    internal sealed record VerifiedUpdateDescriptor(string Repository, string Channel, string Version, string DescriptorSha256);
    internal sealed record ChannelTransitionReview(string SourceChannel, string CurrentVersion, VerifiedUpdateDescriptor Target);
    internal sealed class WindowsFileFence(string root) : IDisposable { public void Dispose() { _ = root; } }
    internal sealed class ApplySafetyException(int failure = 0, int nativeError = 0) : Exception { internal int NativeError => nativeError; internal int Failure => failure; }
    internal enum UpdateFailure { NotConfigured, InvalidSignature, InvalidDescriptor, WrongTarget, NotNewer, InvalidPackage, LimitExceeded }
    internal sealed class UpdateValidationException(UpdateFailure failure = UpdateFailure.InvalidSignature) : Exception { internal UpdateFailure Failure => failure; }
    internal sealed record UpdateContext
    {
        internal readonly string Repository, Channel, CurrentVersion, ReleaseTag, Platform;
        internal readonly long RepositoryId, ReleaseId, AssetId;
        internal UpdateContext(string repository, string channel, string currentVersion, string releaseTag,
            long releaseId, long assetId, string platform, long repositoryId = 0, ChannelTransitionIntent? transition = null)
        { Repository=repository; Channel=channel; CurrentVersion=currentVersion; ReleaseTag=releaseTag;
          ReleaseId=releaseId; AssetId=assetId; Platform=platform; RepositoryId=repositoryId; }
    }
    internal sealed record UpdateDownloadRequest(UpdateContext Context, long DescriptorAssetId, long SignatureAssetId);
    internal static class UpdateTrust { internal const string PlatformId = "fixture"; }
    internal enum FaultPhase { Download, Verification, Preparation, Apply, Recovery }
    internal enum FaultReason { Network, Timeout, Authentication, Access, RateLimited, Integrity, Preparation, Apply, Recovery, Interrupted, Storage, ManualUpgrade }
    internal sealed class UpdateFault { internal FaultPhase Phase { get; init; } internal FaultReason Reason { get; init; } internal bool PreferencesSaveFailed { get; init; } }
}
namespace BetterAstralParty
{
    internal sealed record ChannelTransitionProposal(Updating.VerifiedUpdateDescriptor Descriptor);
    internal enum AutomaticUpdateStatus { NotConfigured, Idle, MissingAssets, Downloading, Preparing, Ready, Queued, Cancelling, Cancelled, CancelFailed, CheckingInstallation, RecoveryRequired, FilesBusy, AlreadyClaimed, ManualUpgradeRequired, Failed, Committed, RetryWaiting, FaultDisabled, FaultStorageFailed, Reactivating, TransitionRequired }
    internal enum ReleaseSessionStatus { NotConfigured, SignedOut, AwaitingInput, Connected, InvalidInput, Cancelled, Expired, AuthenticationRequired, AccessUnavailable, InputFailed, Restoring, StorageFailed }
    internal sealed class FixtureAutomatic
    {
        internal AutomaticUpdateStatus Status = AutomaticUpdateStatus.Idle;
        internal int Revision;
        internal bool AutoDownload = true, ApplyAfterExit = true, SnapshotMismatch;
        internal bool CanReviewTransition, ProposalCurrent = true;
        internal bool CanReactivate, CanRecheck = false; internal int ReactivateCalls, RecheckCalls, DownloadCalls;
        internal void Reactivate() => ReactivateCalls++;
        internal void Recheck() => RecheckCalls++;
        internal void Download(DateTimeOffset now) => DownloadCalls++;
        internal int ConfirmCalls;
        internal bool ApplyOnce;
        internal TaskCompletionSource<ChannelTransitionProposal>? ProposalWorker;
        internal Task<ChannelTransitionProposal> ProposeTransition(Updating.UpdateDownloadRequest request, DateTimeOffset now, CancellationToken cancellation)
            => ProposalWorker?.Task ?? Task.FromResult(new ChannelTransitionProposal(new(request.Context.Repository, request.Context.Channel, request.Context.ReleaseTag, new string('A',64))));
        internal bool CurrentTransitionProposal(ChannelTransitionProposal proposal) => ProposalCurrent && CanReviewTransition;
        internal bool ConfirmTransition(Updating.UpdateDownloadRequest request, int generation, DateTimeOffset now, bool applyOnceAfterExit)
        { ConfirmCalls++; ApplyOnce = applyOnceAfterExit; return true; }
        internal AutomaticUpdateSnapshot Snapshot {
            get {
                var r=Plugin.Updates.Result.Release;
                return new(1, r?.Generation ?? -1, "fixture", "fixture", r?.Repository ?? "", r?.RepositoryId ?? 0,
                    r?.FeedChannel ?? "", r?.Version ?? "", r?.ReleaseId ?? 0, r?.ZipAssetId ?? 0,
                    r?.DescriptorAssetId ?? 0, (r?.SignatureAssetId ?? 0)+(SnapshotMismatch?1:0), Status,
                    false,false,false,"Beta","3.0.0",false);
            }
        }
    }
    internal sealed class BoolPreference { internal bool Value = true; }
    internal static class Plugin
    {
        internal const string Version = "1.0.0-rc.2";
        internal static readonly ReleaseUpdates Updates = new(Version, "Fixture/Repo");
        internal static readonly FixtureAutomatic Automatic = new();
        internal static readonly BoolPreference UpdateNotifications = new();
        internal static readonly FixtureLog Diagnostics = new();
    }
    internal sealed class FixtureLog { internal readonly List<string> Lines = new(); internal void Write(string line) => Lines.Add(line); }
    internal sealed class RuntimeObject
    {
        private static int _sequence;
        internal readonly string Kind;
        internal IntPtr Pointer { get; } = new(++_sequence);
        internal readonly Dictionary<string, object> Values = new();
        internal readonly Dictionary<string, RuntimeObject> Children = new();
        internal readonly Dictionary<string,int> SetCalls = new(),CallCounts = new();
        internal RuntimeObject? Parent;
        internal RuntimeObject? Relations, ScrollPane;
        internal int ScrollTopCalls, BoundsCalls, RelationClears, RemoveChildrenCalls;
        internal static bool FailNextFrame, FailNextDispose, FailNextDisposedRead, FailNextVisibleWrite, FailNextScrollTemplate;
        internal static float? NextViewWidth;
        internal RuntimeObject(string kind)
        {
            Kind = kind;
            foreach (var item in new[] { "visible", "touchable" }) Values[item] = true;
            Values["isDisposed"] = false; Values["grayed"] = false;
            Values["alpha"] = 1f; Values["width"] = 298f; Values["height"] = 101f;
        }
        internal RuntimeObject? Get(string name) => name switch {
            "relations" when Kind == "GList" => Relations,
            "scrollPane" when Kind == "GList" => ScrollPane,
            _ => throw new InvalidOperationException("Unsupported object getter "+Kind+"."+name)
        };
        internal T Get<T>(string name) {
            if (Kind=="Panel" && name=="isDisposed" && FailNextDisposedRead) { FailNextDisposedRead=false; throw new InvalidOperationException("Injected getter failure"); }
            if (Kind == "Label" && name == "textHeight" && typeof(T) == typeof(float)) {
                var width = Get<float>("width"); var size = Get<int>("fontSize");
                var columns = Math.Max(1,(int)(width/(size*.6f)));
                var lines = Get<string>("text").Split('\n').Sum(line=>Math.Max(1,(line.Length+columns-1)/columns));
                var fontHeight = Values.TryGetValue("fontHeightScale",out var metric) && metric is float factor ? factor : 1f;
                return (T)(object)(lines*size*1.25f*fontHeight);
            }
            if(Kind=="ScrollPane"&&name=="scrollingPosY"&&typeof(T)==typeof(float)) return (T)Values["posY"];
            return Values.TryGetValue(name,out var value) && value is T typed ? typed
                : throw new InvalidOperationException($"Unsupported getter/type {Kind}.{name}:{typeof(T)}");
        }
        internal void Set(string name, object value)
        {
            SetCalls[name]=SetCalls.GetValueOrDefault(name)+1;
            if(Kind=="Panel" && name=="visible" && Equals(value,false) && FailNextVisibleWrite) { FailNextVisibleWrite=false;throw new InvalidOperationException("Injected visibility failure"); }
            var type = name switch {
                "visible" or "touchable" or "opaque" or "UBBEnabled" or "singleLine" or "grayed" => typeof(bool),
                "autoResizeItem" when Kind == "GList" => typeof(bool),
                "mouseWheelEnabled" or "bouncebackEffect" when Kind == "ScrollPane" => typeof(bool),
                "layout" when Kind == "GList" => typeof(int),
                "scrollStep" when Kind == "ScrollPane" => typeof(float),
                "sortingOrder" or "autoSize" => typeof(int), "alpha" => typeof(float), "color" => typeof(UnityEngine.Color),
                "text" => typeof(string), _ => throw new InvalidOperationException("Unsupported setter "+name) };
            if (value.GetType() != type) throw new InvalidOperationException("Setter type mismatch "+name);
            Values[name] = value;
        }
        internal void SetField(string name, object value)
        {
            if (name == "changeStateOnClick" && value is bool) return;
            if (Kind == "GList" && name == "selectionMode" && value is int selection && selection == 3) { Values[name]=value; return; }
            if (Kind == "GList" && name == "scrollItemToViewOnClick" && value is bool) { Values[name]=value; return; }
            throw new InvalidOperationException("Unsupported field/type "+Kind+"."+name);
        }
        internal RuntimeObject? Call(string name, params object?[] args)
        {
            CallCounts[name]=CallCounts.GetValueOrDefault(name)+1;
            switch (name)
            {
                case "AddChild":
                    if (args is not [RuntimeObject child]) throw new InvalidOperationException("AddChild signature");
                    if (child.Parent is { } old)
                        foreach(var oldKey in old.Children.Where(pair=>pair.Value==child).Select(pair=>pair.Key).ToArray()) old.Children.Remove(oldKey);
                    Children.Add(child.Pointer.ToString(), child); child.Parent = this; return child;
                case "GetChild":
                    if (args is not [string key] || !Children.TryGetValue(key, out var found)) throw new InvalidOperationException("Missing child");
                    return found;
                case "SetScale":
                case "SetXY":
                    if (args is not [float first, float second]) throw new InvalidOperationException(name+" signature");
                    Values[name=="SetScale"?"scaleX":"x"]=first; Values[name=="SetScale"?"scaleY":"y"]=second; return null;
                case "SetSize":
                    if (args is not [float width,float height]) throw new InvalidOperationException("SetSize signature");
                    Values["width"]=width; Values["height"]=height;
                    if(Kind=="GList" && ScrollPane!=null){ScrollPane.Values["viewWidth"]=NextViewWidth??width-16f;NextViewWidth=null;ScrollPane.Values["viewHeight"]=height;}
                    return null;
                case "ClearAll":
                    if (Kind != "Relations" || args.Length != 0) throw new InvalidOperationException("Relations ClearAll signature");
                    RelationClears++;return null;
                case "RemoveChildren":
                    if (Kind != "GList" || args is not [int start,int end,bool dispose] || start!=0 || end!=-1 || !dispose)
                        throw new InvalidOperationException("GList RemoveChildren signature");
                    foreach(var nested in Children.Values) { nested.Parent=null; nested.Call("Dispose"); }
                    Children.Clear();RemoveChildrenCalls++;return null;
                case "EnsureBoundsCorrect":
                    if (Kind != "GList" || args.Length != 0 || ScrollPane == null) throw new InvalidOperationException("GList bounds signature");
                    BoundsCalls++;
                    ScrollPane.Values["contentHeight"]=Children.Values.Select(child=>child.Get<float>("height")).DefaultIfEmpty(0).Max();
                    ScrollPane.Values["viewHeight"]=Get<float>("height");return null;
                case "ScrollTop":
                    if (Kind != "ScrollPane" || args is not [bool animated] || animated) throw new InvalidOperationException("ScrollTop signature");
                    ScrollTopCalls++;Values["posY"]=0f;return null;
                case "Dispose":
                    if (args.Length != 0) throw new InvalidOperationException("Dispose signature");
                    if (FailNextDispose) { FailNextDispose = false; throw new InvalidOperationException("Fixture dispose failed"); }
                    Values["isDisposed"] = true;
                    foreach (var nested in Children.Values) nested.Call("Dispose");
                    Relations?.Call("Dispose");ScrollPane?.Call("Dispose");
                    return null;
                default: throw new InvalidOperationException("Unsupported method "+name);
            }
        }
    }
    internal static class GameUi
    {
        internal static RuntimeObject? Root = new("Root");
        internal static UpdateNotificationHome Home = new(true, true, true, true, false, 0, 0, 0);
        internal static readonly List<RuntimeObject> Hit = new();
        internal static UpdateNotificationHome NotificationHome() => Home;
        internal static IEnumerable<RuntimeObject> PointerPath() => Hit;
        internal static bool Visible(RuntimeObject? obj) => obj != null && !obj.Get<bool>("isDisposed") && obj.Get<bool>("visible")
            && (obj.Parent == null || Visible(obj.Parent));
        internal static void Dispose(RuntimeObject? obj) { if (obj != null && !obj.Get<bool>("isDisposed")) obj.Call("Dispose"); }
        internal static void StyleText(RuntimeObject obj, int size,int align=0) {
            if(size <= 0 || align!=0) throw new InvalidOperationException();
            obj.Values["fontSize"]=size;obj.Values["align"]=align;
        }
    }
    internal static class NativeUi
    {
        internal sealed class Surface {
            internal readonly RuntimeObject Graph;
            internal Surface(RuntimeObject parent,float width,float height,bool fixedOpacity=false,UnityEngine.Color? fill=null) {
                if(!fixedOpacity||fill==null||!(width==12&&height is 24 or 300||height==18&&width==560||height==14&&width==14)) throw new InvalidOperationException("Unexpected scroll or progress surface");
                Graph=new RuntimeObject("GGraph");Graph.Values["fill"]=fill.Value;
                Graph.Set("touchable",false);Graph.Call("SetSize",width,height);parent.Call("AddChild",Graph);
            }
        }
        internal static readonly List<RuntimeObject> Panels = new();
        internal static readonly List<RuntimeObject> Buttons = new();
        internal static readonly List<RuntimeObject> ScrollTemplates = new();
        internal static RuntimeObject Component(RuntimeObject parent, float width, float height)
        {
            var result = new RuntimeObject("Panel"); result.Values["width"] = width; result.Values["height"] = height;
            parent.Call("AddChild", result); Panels.Add(result); return result;
        }
        internal static RuntimeObject? Create(string package, string item)
        {
            if (package != "Common") throw new InvalidOperationException("Package mismatch");
            if (item == "Com_BuffInfo") {
                if(RuntimeObject.FailNextScrollTemplate){RuntimeObject.FailNextScrollTemplate=false;throw new InvalidOperationException("Scroll template unavailable");}
                var template=new RuntimeObject(item);
                var list=new RuntimeObject("GList") {Parent=template,Relations=new("Relations"),ScrollPane=new("ScrollPane")};
                list.ScrollPane.Values["posY"]=0f;list.ScrollPane.Values["contentHeight"]=0f;list.ScrollPane.Values["viewHeight"]=0f;
                template.Children.Add("list_Buff",list);list.Call("AddChild",new RuntimeObject("placeholder"));
                ScrollTemplates.Add(template);return template;
            }
            if (item == "Com_PopUpWindow_Bottom")
            {
                if (RuntimeObject.FailNextFrame) { RuntimeObject.FailNextFrame = false; throw new InvalidOperationException("Frame unavailable"); }
                var frame = new RuntimeObject(item);
                foreach (var name in new[] {"title","closeButton","btn_Sure","btn_Sure_Only","btn_Cancel"})
                { var child = new RuntimeObject(name); child.Parent = frame; frame.Children.Add(name,child); }
                return frame;
            }
            if (item is not ("Button_ReturnRounded" or "Button_ConfirmRounded")) throw new InvalidOperationException("Item mismatch");
            var button = new RuntimeObject(item); Buttons.Add(button); return button;
        }
        internal static RuntimeObject Label(RuntimeObject parent,string text,float x,float y,float width,float height,int size)
        { var label = new RuntimeObject("Label"); parent.Call("AddChild",label); label.Values["text"]=text; label.Values["fontSize"]=size;
          label.Call("SetXY",x,y);label.Call("SetSize",width,height);return label; }
        internal static void StyleTitle(RuntimeObject button,string text) {
            button.Values["title"]=text;
            if(ModFont.AdvanceOnNextButtonStyle) { ModFont.AdvanceOnNextButtonStyle=false;ModFont.Advance(); }
        }
        internal static void Position(RuntimeObject button,float x,float y,float scale) { button.Call("SetXY",x,y); button.Call("SetScale",scale,scale); }
        internal static void Play(RuntimeObject button,string transition)
        { if (transition is not ("Switchin" or "Switchout")) throw new InvalidOperationException("Unexpected transition"); }
    }
    internal static class ModFont {
        internal static int Revision { get; private set; }
        internal static bool AdvanceOnNextTrack,AdvanceOnNextButtonStyle;
        internal static void Advance() => Revision++;
        internal static void Track(RuntimeObject label) {
            if(AdvanceOnNextTrack) { AdvanceOnNextTrack=false;Advance(); }
        }
    }
    internal static class MenuLayout
    {
        internal const float Width = 1060, Height = 760, NativeFrameHeight = 620;
        internal static (float Height,float Y) ScrollMarker(float viewHeight,float contentHeight,float position) {
            if(contentHeight<=viewHeight)return(viewHeight,0);
            var height=Math.Clamp(viewHeight*viewHeight/contentHeight,24,viewHeight);
            return(height,Math.Clamp(position/(contentHeight-viewHeight),0,1)*(viewHeight-height));
        }
        internal static float FitScale(float width,float height,float windowHeight = Height) => MathF.Min(width / Width,height / windowHeight);
    }
    internal static class AdviceText { internal static float Reveal(float elapsed,float duration=.22f) => Math.Clamp(elapsed / duration,0,1); }
    internal static class ModUi { internal static int Opens; internal static bool ShowsUpdates; internal static void OpenUpdateSettings() => Opens++;
        internal static void RetryUpdateFromNotice() { Opens++; if (Plugin.Automatic.CanReactivate) Plugin.Automatic.Reactivate(); else if (Plugin.Automatic.CanRecheck) Plugin.Automatic.Recheck(); else Plugin.Automatic.Download(DateTimeOffset.UtcNow); } }
    internal static class Compatibility
    {
        internal static readonly HashSet<string> Blocked = new();
        internal static bool Allowed(string feature) => !Blocked.Contains(feature);
        internal static void Block(string feature,Exception error,params Action[] cleanup)
        { Blocked.Add(feature); foreach(var action in cleanup) { try { action(); } catch { } } }
    }
}
namespace BepInEx { internal static class Paths { internal static string BepInExRootPath => Path.Combine(Path.GetTempPath(), "inert-bap-ui", "BepInEx"); } }
