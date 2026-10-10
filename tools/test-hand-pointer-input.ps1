$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -Path "$root/.deps/bepinex/BepInEx/core/Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$root/.research/extracted/AstralParty.Runtime.dll.dll")
try {
    foreach ($contract in @(
        'FairyGUI.Stage|HitTest|UnityEngine.Vector2,System.Boolean|FairyGUI.DisplayObject|False',
        'FairyGUI.GRoot|DisplayObjectToGObject|FairyGUI.DisplayObject|FairyGUI.GObject|False',
        'FairyGUI.HitTestContext|ClearRaycastHitCache||System.Void|True')) {
        $owner, $name, $parameters, $returns, $static = $contract.Split('|')
        $method = $null
        for ($type = $asm.MainModule.GetTypes() | Where-Object FullName -eq $owner; $type; $type = $(if ($type.BaseType) { $type.BaseType.Resolve() })) {
            $method = @($type.Methods | Where-Object { $_.Name -eq $name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $parameters })
            if ($method.Count) { break }
        }
        if ($method.Count -ne 1 -or $method[0].ReturnType.FullName -ne $returns -or $method[0].IsStatic.ToString() -ne $static) {
            throw "Native pointer contract changed: $contract"
        }
    }
    # Native monster selection reuses a shown, empty CardWindow and cancels only its own window.
    # Bind the regression model to the real reset/unlock/drag-release lifecycle, not a card ID.
    $nativeTypes = @{}; foreach ($type in $asm.MainModule.GetTypes()) { $nativeTypes[$type.FullName] = $type }
    foreach ($contract in @(
        @('GameLogic.Card/<CardAction>d__4','MoveNext','UI.CardWindow::ShowCard','UI.CardWindow::UpdateConfig'),
        @('UI.CardWindow/<ShowCard>d__8','MoveNext','FairyGUI.Window::Show','UI.UICardWindow::showContent'),
        @('UI.BattleSelectMonsterWindow/<>c__DisplayClass9_0','<ShowCardVailMonsterTarget>b__1','GameLogic.CardSignal::resetCardList','FairyGUI.Window::Hide'),
        @('UI.HandCardPanel','ResetCardList','UI.HandCardPanel::UpdateCardList','UI.HandCardPanel::RefreshUsableCards'),
        @('UI.HandCardPanel','RefreshUsableCards','UI.UIHandCardPanel::container_Card','FairyGUI.GObject::set_touchable'),
        @('FairyGUI.GObject','__touchEnd','FairyGUI.GObject::set_draggingObject','FairyGUI.EventDispatcher::DispatchEvent'))) {
        $method = @($nativeTypes[$contract[0]].Methods | Where-Object Name -eq $contract[1])
        if ($contract[1] -eq 'ResetCardList') { $method = @($method | Where-Object { $_.Parameters.Count -eq 1 }) }
        if ($method.Count -ne 1 -or !$method[0].HasBody) { throw "Native hand cancel method missing: $($contract[0]).$($contract[1])" }
        $operands = $method[0].Body.Instructions.Operand -join ' '
        foreach ($required in $contract[2..3]) {
            if (!$operands.Contains($required)) { throw "Native hand cancel lifecycle changed: $required" }
        }
    }
    $cancel = $nativeTypes['UI.BattleSelectMonsterWindow/<>c__DisplayClass9_0'].Methods | Where-Object Name -eq '<ShowCardVailMonsterTarget>b__1'
    if (($cancel.Body.Instructions.Operand -join ' ') -match 'UI.CardWindow::|FairyGUI.Window::HideImmediately') {
        throw 'Re-audit native monster cancel: retained empty CardWindow contract changed'
    }
} finally { $asm.Dispose() }

$game = Get-Content "$root/src/GameUi.cs" -Raw
$preview = Get-Content "$root/src/CardPreviewUi.cs" -Raw
$hand = Get-Content "$root/src/HandLayoutUi.cs" -Raw
$pointer = [regex]::Match($game, '(?s)    internal static IReadOnlyList<RuntimeObject> PointerPath\(.*?\r?\n    \}').Value
$targets = [regex]::Match($preview, '(?s)    private static void UpdatePingTargets\(.*?\r?\n    \}').Value
$update = [regex]::Match($preview, '(?s)    private static void Update\(\).*?\r?\n    \}').Value
$click = [regex]::Match($hand, '(?s)    private static void PollPileClick\(.*?\r?\n    \}').Value
$interaction = [regex]::Match($hand, '(?s)        if \(clickMode\)\r?\n        \{.*?(?=        if \(_expanded is \{ \} missing)').Value
$handLayout = (Get-Content "$root/src/HandLayout.cs" -Raw).Replace('namespace BetterAstralParty;', '')
$layout = (Get-Content "$root/src/CardPreviewLayout.cs" -Raw).Replace('namespace BetterAstralParty;', '')
if (!$pointer -or !$targets -or !$update -or !$click -or !$interaction) { throw 'Production pointer/ping/click helpers missing' }
if ($interaction -notmatch 'PollPileClick\(path, pointer, unit, inputLocked \|\| pending > 0 \|\| usingCard \|\| FieldCameraReturnUi.ConsumedInput\)') {
    throw 'Re-audit grouped-hand input branch and native lock ownership'
}

# Execute production input paths against a native hit-test oracle with separate cached/live targets.
# This checks ownership and ordering; it does not substitute for FairyGUI runtime verification.
$harness = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace __NS__ {
struct Vector2 {
    public float x,y;
    public Vector2(float x,float y) { this.x=x;this.y=y; }
    public float sqrMagnitude=>x*x+y*y;
    public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
}
static class Screen { public static int width=1920,height=1080; }
static class Time { public static int frameCount; public static float unscaledTime; }
enum KeyCode { Escape }
static class Input {
    public static readonly bool[] Down=new bool[3],Up=new bool[3],Held=new bool[3];
    public static Vector2 mousePosition=new(100,960);
    public static bool GetMouseButtonDown(int n)=>Down[n];
    public static bool GetMouseButtonUp(int n)=>Up[n];
    public static bool GetMouseButton(int n)=>Held[n];
    public static bool GetKeyDown(KeyCode key)=>false;
    public static void Frame(bool down=false,bool up=false) {
        Array.Clear(Down,0,3);Array.Clear(Up,0,3);Array.Clear(Held,0,3);
        Down[0]=Held[0]=down;Up[0]=up;Time.frameCount++;Time.unscaledTime+=.01f;
    }
}
static class Application { public static bool isFocused=true; }
sealed class Setting { public bool Value=true; }
sealed class Log { public bool IsRecording=false; public void Write(string value){} public void LogInfo(string value){} }
static class Plugin { public static readonly Setting CardPopups=new();public static readonly Log Logger=new(),Diagnostics=new(); }
static class ModUi { public static bool IsOpen; }
static class FieldCameraReturnUi { public static bool ConsumedInput; }
static class VisibleCombatAdvisor { public static bool IsPve(int map)=>map==9; }
sealed class RuntimeObject {
    static int next;
    public readonly IntPtr Pointer=(IntPtr)(++next);
    public string TypeName;
    public RuntimeObject? Parent;
    public readonly Dictionary<string,object?> Data=new();
    public readonly List<RuntimeObject> Children=new();
    public RuntimeObject(string kind="Fixture") { TypeName=kind;Data["visible"]=true;Data["isDisposed"]=false; }
    public RuntimeObject? Get(string key)=>key=="parent"?Parent:key=="touchTarget"?Native.Cached:Data.GetValueOrDefault(key) as RuntimeObject;
    public T Get<T>(string key)=>key=="numChildren"?(T)(object)Children.Count:(T)Data[key]!;
    public RuntimeObject? Field(string key)=>Get(key);
    public string String()=>(string)Data["value"]!;
    public void Set(string key,object value)=>Data[key]=value;
    public RuntimeObject? Call(string method,params object?[] args) {
        switch(method) {
            case "HitTest":
                if(args is not [Vector2 point,bool touchable] || !touchable) throw new Exception("HitTest signature");
                Native.HitTests++;Native.Point=point;
                if(Native.FailHit) throw new InvalidOperationException("Injected native hit failure");
                var hit=Native.Layers.FirstOrDefault(o=>o.Contains(point));
                if(hit==null)return null;
                var display=new RuntimeObject("DisplayObject");display.Data["gObject"]=hit;return display;
            case "DisplayObjectToGObject":
                if(args.Length!=1 || args[0] is not null and not RuntimeObject) throw new Exception("Display mapping signature");
                if(args[0] is RuntimeObject rendered && rendered.TypeName!="DisplayObject") throw new Exception("Display mapping argument type");
                Native.Mappings++;return (args[0] as RuntimeObject)?.Field("gObject");
            case "GetChildAt":return Children[(int)args[0]!];
            case "SetXY":Data["x"]=args[0];Data["y"]=args[1];return null;
            case "SetSize":Data["width"]=args[0];Data["height"]=args[1];return null;
            case "Dispose":Data["isDisposed"]=true;Parent?.Children.Remove(this);return null;
            default:throw new Exception("Unexpected native call "+method);
        }
    }
    public bool Contains(Vector2 p) {
        for(var o=this;o!=null;o=o.Parent) if(o.Get<bool>("isDisposed") || !o.Get<bool>("visible")) return false;
        float x=0,y=0;for(var o=this;o!=null;o=o.Parent) { x+=o.Data.GetValueOrDefault("x") is float a?a:0;y+=o.Data.GetValueOrDefault("y") is float b?b:0; }
        return Data.TryGetValue("width",out var w) && Data.TryGetValue("height",out var h)
            && p.x>=x && p.y>=y && p.x<x+(float)w! && p.y<y+(float)h!;
    }
    public static IntPtr FindClass(string ns,string name)=>name=="HitTestContext"?(IntPtr)2:(IntPtr)1;
    public static RuntimeObject? StaticCall(IntPtr type,string method,params object[] args) {
        if(type==(IntPtr)2 && method=="ClearRaycastHitCache" && args.Length==0) { Native.Clears++;return null; }
        if(method=="get_inst" && args.Length==0)return Native.Stage;
        throw new Exception("Unexpected native static call "+method);
    }
    public static RuntimeObject StaticField(IntPtr type,string name)=>Native.Manager;
    public static void RequireClasses(string feature,params (string,string)[] types){}
}
static class Native {
    public static readonly RuntimeObject Stage=new("Stage"),Manager=new("Manager"),Room=new("Room");
    public static RuntimeObject? Cached,Window,CardWindow;
    public static readonly List<RuntimeObject> Layers=new();
    public static int HitTests,Mappings,Clears;
    public static bool FailHit;
    public static Vector2 Point;
}
static class NativeUi {
    public static RuntimeObject Component(RuntimeObject parent,float w,float h) {
        var target=new RuntimeObject("OwnedPingTarget") { Parent=parent };
        target.Set("width",w);target.Set("height",h);parent.Children.Add(target);Native.Layers.Insert(0,target);return target;
    }
}
static class GameUi {
    static RuntimeObject? _root=new("GRoot");
    static readonly List<RuntimeObject> HitPath=new();
    static int _hitFrame=-1;
    public static RuntimeObject? Root=>_root;
    public static bool HomeAvailable=>false;
    public static bool Visible(RuntimeObject? obj)=>obj!=null && !obj.Get<bool>("isDisposed") && obj.Get<bool>("visible")
        && (obj.Parent==null || Visible(obj.Parent));
    public static RuntimeObject? Find(RuntimeObject root,string type,int depth=0,int maxDepth=4)=>type switch {
        "ExpressionListWindow" when Visible(Native.Window)=>Native.Window,
        "CardWindow" when Visible(Native.CardWindow)=>Native.CardWindow,
        _=>null
    };
    public static void Dispose(RuntimeObject? obj) { if(obj!=null && !obj.Get<bool>("isDisposed"))obj.Call("Dispose"); }
__POINTER__
}
__LAYOUT__
static class Preview {
    static readonly Dictionary<string,int> Pings=new(),RelicPings=new();
    public static readonly Dictionary<IntPtr,RuntimeObject> PingTargets=new();
    static readonly HashSet<IntPtr> LiveTargets=new();
    static readonly List<IntPtr> StaleTargets=new();
    static RuntimeObject? _pingWindow,_popup=null;
    static IntPtr _logicClass,_room,_pressed;
    static float _scanAt;
    static bool _allowed,_pressedPopup,_pinned=false,_shownRelic=false;
    static int _shownId=0;
    static string _pressedText="";
    static (int Id,bool Relic,Vector2 Anchor)? _pending;
    public static int Shown,Resets;
    static bool LoadIndex()=>true;
    static bool RelicUiReady()=>true;
    static void BeginClose()=>_pending=null;
    static void Animate(RuntimeObject root){}
    static void Show(RuntimeObject root,int id,bool relic,Vector2 anchor)=>Shown=id;
    static void Reset() {
        Resets++;foreach(var target in PingTargets.Values)GameUi.Dispose(target);PingTargets.Clear();
        _pingWindow=null;_pending=null;_pressed=IntPtr.Zero;_pressedText="";_pressedPopup=false;
    }
    public static void Fixture() {
        Reset();_scanAt=0;_room=Native.Room.Pointer;_allowed=true;Shown=0;
        Pings.Clear();RelicPings.Clear();Pings.Add("fixture-card",1);RelicPings.Add("fixture-relic",2);
    }
    public static void Step()=>Update();
__TARGETS__
__UPDATE__
}
__HAND_LAYOUT__
static class Hand {
    public static readonly Dictionary<HandGroup,RuntimeObject> PileTargets=new();
    static HandGroup? _expanded,_pressedPile;
    static bool _outsidePress;
    static Vector2 _pilePressAt;
    static IntPtr _focused;
    public static bool Expanded=>_expanded!=null;
    public static IntPtr Focused=>_focused;
    sealed class Entry { public HandGroup Group=HandGroup.Attack; }
    static readonly Dictionary<IntPtr,Entry> Entries=new();
    public static void Reset(){_expanded=_pressedPile=null;_outsidePress=false;_focused=IntPtr.Zero;}
    public static void Step(bool locked=false)=>PollPileClick(GameUi.PointerPath(refresh:true),new(100,120),1,locked);
    public static void StepMode(bool clickMode,bool inputLocked=false,int pending=0) {
        var root=GameUi.Root!;
        var path=GameUi.PointerPath(refresh:true);
        var hovered=path.FirstOrDefault(item=>Entries.ContainsKey(item.Pointer));
        var pointer=new Vector2(100,120);
        var unit=1f;var width=1920f;var height=1080f;
        var groups=new List<HandGroup>{HandGroup.Attack};
        var piles=new[]{new List<Entry>{new()}};var aspects=new[]{1.5f};
__INTERACTION__
    }
    public static void NativeCard(RuntimeObject card) { Entries.Clear();Entries.Add(card.Pointer,new()); }
__CLICK__
}
public static class Check {
    static int assertions;
    static void Assert(bool value,string reason) { assertions++;if(!value)throw new Exception("Pointer/ping regression: "+reason); }
    static RuntimeObject Rect(string kind,float x=0,float y=0,float w=300,float h=300) {
        var o=new RuntimeObject(kind);o.Set("x",x);o.Set("y",y);o.Set("width",w);o.Set("height",h);return o;
    }
    static RuntimeObject Chat(string text) {
        var item=new RuntimeObject("UIExpression_Com_ChatItem");
        var title=Rect("Title",80,100,80,40);var value=new RuntimeObject();value.Set("value",text);title.Data["text"]=value;item.Data["title"]=title;return item;
    }
    static void Text(RuntimeObject item,string text)=>item.Field("title")!.Get("text")!.Set("value",text);
    public static void Run() {
        var root=GameUi.Root!;var pile=Rect("OwnedPileTarget");pile.Parent=root;
        Hand.PileTargets.Add(HandGroup.Attack,pile);
        var stale=Rect("RemovedOverlay");stale.Parent=root;Native.Cached=stale;
        Native.Layers.Add(stale);Native.Layers.Add(pile);
        Input.Frame();int hits=Native.HitTests;GameUi.PointerPath(refresh:true);
        Assert(Native.HitTests==hits,"idle refresh must use native cache");
        GameUi.Dispose(stale);Input.Frame(down:true);
        Assert(GameUi.PointerPath(refresh:true)[0]==pile,"removed surface must reveal actual hand");
        Assert(Native.Cached==stale && Native.Point.x==100 && Native.Point.y==120,"top-left coordinates and untouched Stage cache");
        hits=Native.HitTests;GameUi.PointerPath();Assert(Native.HitTests==hits,"same-frame ordinary cache");
        var resized=Rect("ResizedOverlay");resized.Parent=root;Native.Layers.Insert(0,resized);Native.Cached=resized;
        Assert(GameUi.PointerPath(refresh:true)[0]==resized,"real overlay must win native hit order");
        resized.Call("SetSize",20f,20f);
        Assert(GameUi.PointerPath(refresh:true)[0]==pile,"fresh hit must observe same-frame resize");
        Native.FailHit=true;int clears=Native.Clears;
        try {GameUi.PointerPath(refresh:true);throw new Exception("Expected hit failure");}catch(InvalidOperationException){}
        Native.FailHit=false;Assert(Native.Clears==clears+1,"raycast scratch cleanup on native failure");
        foreach(var button in new[]{1,2}) {Input.Frame();Input.Down[button]=true;hits=Native.HitTests;GameUi.PointerPath(refresh:true);Assert(Native.HitTests==hits+1,"secondary press edge");}
        Input.Frame();hits=Native.HitTests;GameUi.PointerPath(refresh:true);Assert(Native.HitTests==hits,"idle after click adds no HitTest");

        var info=new RuntimeObject();info.Set("MapType",9);Native.Room.Data["info"]=info;
        var roomHolder=new RuntimeObject();roomHolder.Data["curRoomInfo"]=Native.Room;Native.Manager.Data["room"]=roomHolder;
        Native.Window=new RuntimeObject("ExpressionListWindow");var content=new RuntimeObject();var list=new RuntimeObject();
        Native.Window.Data["contentPane"]=content;content.Data["list_Plaform"]=list;
        var message=Chat("fixture-card");list.Children.Add(message);Preview.Fixture();
        Native.Layers.Clear();Native.Layers.Add(pile);Input.Frame();hits=Native.HitTests;Preview.Step();
        Assert(Preview.PingTargets.Count==1 && Native.HitTests==hits,"discovery creates target without idle hit test");
        var oldTarget=Preview.PingTargets[message.Pointer];Native.Cached=oldTarget;Text(message,"fixture-non-card");
        Input.Frame(down:true);Preview.Step();Hand.Step();
        Assert(oldTarget.Get<bool>("isDisposed") && Preview.PingTargets.Count==0,"pooled non-card removes stale target before input");
        Input.Frame(up:true);Preview.Step();Hand.Step();
        Assert(Hand.Expanded && Preview.Shown==0 && Native.Cached==oldTarget,"stale ping hands click to native pile without cache mutation");

        Hand.Reset();Text(message,"fixture-card");Time.unscaledTime+=.3f;Input.Frame();Preview.Step();
        var liveTarget=Preview.PingTargets[message.Pointer];Native.Cached=liveTarget;Text(message,"fixture-relic");
        message.Field("title")!.Set("width",20f);
        Input.Frame(down:true);Preview.Step();Hand.Step();Input.Frame(up:true);Preview.Step();Hand.Step();
        Assert(Preview.PingTargets[message.Pointer]==liveTarget && !liveTarget.Get<bool>("isDisposed")
            && liveTarget.Get<float>("width")==36 && Preview.Shown==2 && !Hand.Expanded,"different valid pooled ping stays owned and resizes");
        Native.Window.Set("visible",false);Input.Frame(down:true);Preview.Step();Hand.Step();Input.Frame(up:true);Preview.Step();Hand.Step();
        Assert(Preview.PingTargets.Count==0 && Hand.Expanded,"removed native window releases owned hit surface");

        Hand.Reset();var overlay=Rect("NativeModalOverlay");overlay.Parent=root;Native.Layers.Insert(0,overlay);Native.Cached=pile;
        Input.Frame(down:true);Hand.Step();Input.Frame(up:true);Hand.Step();
        Assert(!Hand.Expanded && GameUi.PointerPath()[0]==overlay,"genuine overlay blocks hand; no geometric bypass");
        GameUi.Dispose(overlay);Input.Frame(down:true);Hand.Step(locked:true);Input.Frame(up:true);Hand.Step();
        Assert(!Hand.Expanded,"native input lock owns press/release");

        // Execute the actual Update Click/Hover branch and its production PollPileClick.
        // Contentless CardWindow stays live through native monster cancellation; it is not a hit surface.
        Native.CardWindow=new RuntimeObject("CardWindow") { Parent=root };
        var cancelSurface=Rect("GButton");cancelSurface.Parent=Native.CardWindow;
        Native.Layers.Clear();Native.Layers.Add(cancelSurface);Native.Layers.Add(pile);
        Hand.Reset();Input.Frame(down:true);Hand.StepMode(true,inputLocked:true,pending:1);
        Assert(!Hand.Expanded,"real target selection retains native input/pending ownership");
        GameUi.Dispose(cancelSurface);Native.Cached=cancelSurface;
        Input.Frame(up:true);Hand.StepMode(true);
        Assert(!Hand.Expanded && GameUi.Visible(Native.CardWindow),"cancel release cannot turn into pile click; empty CardWindow remains on stage");
        for(int cycle=0;cycle<3;cycle++) {
            Input.Frame(down:true);Hand.StepMode(true);Input.Frame(up:true);Hand.StepMode(true);
            Assert(Hand.Expanded==(cycle%2==0),"retained empty CardWindow must not disable subsequent pile open/close clicks");
        }
        Hand.Reset();Input.Frame(down:true);Hand.StepMode(true,pending:1);Input.Frame(up:true);Hand.StepMode(true);
        Assert(!Hand.Expanded,"pending native use cannot acquire pile press even with unlocked container");
        FieldCameraReturnUi.ConsumedInput=true;Input.Frame(down:true);Hand.StepMode(true);
        FieldCameraReturnUi.ConsumedInput=false;Input.Frame(up:true);Hand.StepMode(true);
        Assert(!Hand.Expanded,"camera return consumed press cannot acquire pile release");
        var confirmSurface=Rect("GButton");confirmSurface.Parent=Native.CardWindow;
        Native.Layers.Insert(0,confirmSurface);Native.Cached=pile;
        Input.Frame(down:true);Hand.StepMode(true);GameUi.Dispose(confirmSurface);
        Input.Frame(up:true);Hand.StepMode(true);
        Assert(!Hand.Expanded,"real CardWindow confirm/cancel press retains ownership when window surface vanishes on release");
        Hand.Reset();Input.Frame(down:true);Hand.StepMode(true);Input.Frame(up:true);Hand.StepMode(true);
        Assert(Hand.Expanded,"pile recovers immediately after native window surface closure");
        // Hover has no window-existence gate; actual native hit order remains authoritative.
        var hoverCard=Rect("UIHandCard_Button_Card");hoverCard.Parent=root;Hand.NativeCard(hoverCard);
        Native.Layers.Clear();Native.Layers.Add(hoverCard);Native.Cached=hoverCard;
        Hand.Reset();Input.Frame();Hand.StepMode(false);
        Assert(Hand.Expanded && Hand.Focused==hoverCard.Pointer,"Hover resumes over original artwork while empty CardWindow survives");
        Input.Frame(down:true);Hand.StepMode(false);
        Assert(!Hand.Expanded && Hand.Focused==IntPtr.Zero,"Hover keeps held-card gesture exclusion");
        Input.Frame();Hand.StepMode(false,inputLocked:true);
        Assert(!Hand.Expanded,"Hover keeps native container input lock");
        var realOpaque=Rect("NativeModalOverlay");realOpaque.Parent=Native.CardWindow;
        Native.Layers.Insert(0,realOpaque);Native.Cached=realOpaque;Hand.Reset();Input.Frame();Hand.StepMode(false);
        Assert(!Hand.Expanded,"Hover never bypasses actual opaque native overlay hit");
        GameUi.Dispose(realOpaque);Native.Cached=hoverCard;Input.Frame();Hand.StepMode(false);
        Assert(Hand.Expanded,"Hover recovers after native overlay is removed");
        Hand.Reset();Native.Layers.Clear();Native.Layers.Add(pile);Native.CardWindow=null;
        Application.isFocused=false;Input.Frame(down:true);Hand.Step();Input.Frame(up:true);Hand.Step();
        Assert(!Hand.Expanded,"unfocused click stays ignored");
        int focusResets=Preview.Resets;Preview.Step();Assert(Preview.Resets==focusResets+1,"popup focus gate retained");Application.isFocused=true;
        int resets=Preview.Resets;Plugin.CardPopups.Value=false;Input.Frame(down:true);Preview.Step();
        Assert(Preview.Resets==resets+1,"popup setting gate retained");Plugin.CardPopups.Value=true;
        ModUi.IsOpen=true;resets=Preview.Resets;Preview.Step();Assert(Preview.Resets==resets+1,"settings modal gate retained");ModUi.IsOpen=false;
        info.Set("MapType",2);Time.unscaledTime+=.3f;resets=Preview.Resets;Preview.Step();
        Assert(Preview.Resets==resets+1,"popup PvP gate retained");
        Assert(Native.HitTests==Native.Clears,"each completed or failed hit test clears native raycast scratch");
        Assert(Native.Mappings==Native.HitTests-1,"every successful hit test maps its display object");
        Console.WriteLine($"PASS: {assertions} production fresh-pointer/Click/Hover assertions, retained-empty-window cancel recovery, true native locks/modal ownership, pooled ping handoff; native lifecycle/signatures verified. Runtime input remains unverified.");
    }
}
}
'@
$ns = 'HandPointerInput' + [Guid]::NewGuid().ToString('N')
$harness = $harness.Replace('__NS__',$ns).Replace('__POINTER__',$pointer).Replace('__TARGETS__',$targets).Replace('__UPDATE__',$update).Replace('__CLICK__',$click).Replace('__LAYOUT__',$layout).Replace('__HAND_LAYOUT__',$handLayout).Replace('__INTERACTION__',$interaction)
Add-Type -TypeDefinition $harness
([type]"$ns.Check")::Run()

# The original global-window gate must fail the same actual-source cancellation regression.
$legacy = $harness.Replace('path.Any(item => item.TypeName == "CardWindow")','GameUi.Find(root, "CardWindow", maxDepth: 1) != null')
if ($legacy -eq $harness) { throw 'Missing exact production usingCard predicate for legacy mutant' }
$legacyNs = $ns + 'Legacy'
Add-Type -TypeDefinition $legacy.Replace($ns,$legacyNs)
try { ([type]"$legacyNs.Check")::Run(); throw 'Legacy global CardWindow guard was not detected' }
catch {
    if ($_.Exception.ToString() -notmatch 'retained empty CardWindow must not disable') { throw }
    Write-Host 'PASS: original global CardWindow-existence guard rejected by retained-window Click cancellation regression (memory-only mutant).'
}
