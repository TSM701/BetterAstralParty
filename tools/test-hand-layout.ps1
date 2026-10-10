$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/test-hand-scan.ps1"
& "$PSScriptRoot/test-hand-outline.ps1"
& "$PSScriptRoot/test-hand-pointer-input.ps1"
& "$PSScriptRoot/test-hand-submit.ps1"
$root = Split-Path -Parent $PSScriptRoot
Add-Type -Path "$root/.deps/bepinex/BepInEx/core/Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$root/.research/extracted/AstralParty.Runtime.dll.dll")
try {
    $types = @{}; foreach ($type in $asm.MainModule.GetTypes()) { $types[$type.FullName] = $type }
    foreach ($pair in @(
        @('UI.UIHandCardPanel', 'container_Card,hotZone'),
        @('UI.UIHandCard_Button_Card', 'CustomPosition,CustomRotation,_CustomSortingOrder,zoomInScale,zoomOutScale,IsRelease,com_Card,effectOutline,effectOutline_Suggest_Bottom,effectTempCard'),
        @('GameLogic.CardPool', 'ShowCardStatus'),
        @('FairyGUI.TweenManager', '_activeTweens,_totalActiveTweens'),
        @('FairyGUI.GTweener', '_killed,_propType'),
        @('FairyGUI.EventListener', '_bridge'), @('FairyGUI.EventBridge', '_isLocking'),
        @('GameLogic.HandCardData', 'Guid'), @('GameLogic.GameLogicManager', '_inst'))) {
        $type = $types[$pair[0]]
        foreach ($field in $pair[1].Split(',')) {
            $found = $false
            for ($current = $type; $null -ne $current; $current = $(if ($current.BaseType) { $current.BaseType.Resolve() })) {
                if ($current.Fields.Name -contains $field) { $found = $true; break }
            }
            if (!$found) { throw "Missing native field $($pair[0]).$field" }
        }
    }
    foreach ($pair in @(@('GameLogic.WatchLogic','PlayerIsWatcher'), @('GameLogic.HandCardData','get_Config'),
        @('UI.UIHandCard_Button_Card','get_CardData'), @('FairyGUI.GObject','get_draggingObject'),
        @('FairyGUI.GObject','get_onRollOut'),
        @('FairyGUI.GObject','RootToLocal'), @('FairyGUI.GComponent','set_opaque'))) {
        if (!($types[$pair[0]].Methods | Where-Object Name -eq $pair[1])) { throw "Missing method $pair" }
    }
    # Direct value reads must use the exact native field size/type (including enum backing type).
    foreach ($contract in @(
        'UI.UIHandCard_Button_Card|CustomPosition|UnityEngine.Vector2',
        'UI.UIHandCard_Button_Card|CustomRotation|System.Single',
        'UI.UIHandCard_Button_Card|_CustomSortingOrder|System.Int32',
        'UI.UIHandCard_Button_Card|zoomInScale|UnityEngine.Vector2',
        'UI.UIHandCard_Button_Card|zoomOutScale|UnityEngine.Vector2',
        'UI.UIHandCard_Button_Card|IsRelease|System.Boolean',
        'GameLogic.HandCardData|Guid|System.Int32',
        'FairyGUI.GObject|_pivotAsAnchor|System.Boolean',
        'FairyGUI.GTweener|_killed|System.Boolean',
        'FairyGUI.GTweener|_propType|System.Int32',
        'FairyGUI.TweenValue|x|System.Single',
        'FairyGUI.EventBridge|_isLocking|System.Boolean')) {
        $owner, $name, $expected = $contract.Split('|')
        $field = $null
        for ($current = $types[$owner]; $null -ne $current; $current = $(if ($current.BaseType) { $current.BaseType.Resolve() })) {
            $field = $current.Fields | Where-Object Name -eq $name
            if ($field) { break }
        }
        if (!$field -or $field.IsStatic) { throw "Missing instance value field: $contract" }
        $actual = $field.FieldType.FullName
        if ($actual -ne $expected) {
            $valueType = $field.FieldType.Resolve()
            if ($valueType.IsEnum) { $actual = ($valueType.Fields | Where-Object Name -eq 'value__').FieldType.FullName }
        }
        if ($actual -ne $expected) { throw "Native value field type changed: $contract ($actual)" }
    }
    foreach ($pair in @(@('FairyGUI.GTweener','get_endValue'), @('FairyGUI.TweenValue','set_vec2'),
        @('FairyGUI.GObject','TweenMove'), @('FairyGUI.GObject','TweenScale'),
        @('FairyGUI.GTweener','get_startValue'), @('FairyGUI.GTweener','get_normalizedTime'),
        @('FairyGUI.GTweener','get_target'), @('UI.UIHandCard_Button_Card','get__ShowPosition'))) {
        if ($pair[1] -notin $types[$pair[0]].Methods.Name) { throw "Missing native animation binding: $pair" }
    }
    $query = @($types['FairyGUI.GTween'].Methods | Where-Object { $_.Name -eq 'IsTweening' -and $_.Parameters.Count -eq 1 })
    if ($query.Count -ne 1 -or $query[0].Parameters[0].ParameterType.FullName -ne 'System.Object') { throw 'Tween query overload changed' }
    foreach ($pair in @(@('PlayTransition','Retain'), @('TranslateCard','OnComplete'), @('Release','set_visible'), @('IsDistribute','get_height'))) {
        $method = $types['UI.UIHandCard_Button_Card'].Methods | Where-Object Name -eq $pair[0]
        if (($method.Body.Instructions.Operand -join ' ') -notmatch $pair[1]) { throw "Native lifecycle changed: $pair" }
    }
    $drop = $types['UI.HandCardPanel/<DragEndEvent>d__37'].Methods | Where-Object Name -eq 'MoveNext'
    if (($drop.Body.Instructions.Operand -join ' ') -notmatch 'UIHandCardPanel::hotZone') { throw 'Drop target path changed' }
    $source = Get-Content "$root/src/HandLayoutUi.cs" -Raw
    # Verify the actual renderer path, not only GObject.sortingOrder (0.28.7 missed this).
    foreach ($pair in @(@('UI.BasePanel`1','Show','set_fairyBatching'),
        @('FairyGUI.GComponent','set_fairyBatching','set_fairyBatching'),
        @('FairyGUI.Container','UpdateBatchingFlags','InvalidateBatchingState'),
        @('FairyGUI.Container','DoFairyBatching','get_material'),
        @('FairyGUI.Container','CollectChildren','DoFairyBatching'),
        @('FairyGUI.Container','SetRenderingOrder','SetRenderingOrder'))) {
        $method = $types[$pair[0]].Methods | Where-Object Name -eq $pair[1]
        if (($method.Body.Instructions.Operand -join ' ') -notmatch $pair[2]) { throw "Native batching contract changed: $pair" }
    }
    foreach ($name in @('SetPosition','SetScale')) {
        $method = $types['FairyGUI.DisplayObject'].Methods | Where-Object Name -eq $name
        if (($method.Body.Instructions.Operand -join ' ') -match 'InvalidateBatchingState') { throw "Re-audit transform invalidation: $name" }
    }
    $readOnlySource = $source.Replace('card.Get("onRollOut")!.Field("_bridge")!.Field<bool>("_isLocking")', 'nativeUsePending')
    foreach ($forbidden in @('RequestUse', 'ShowUseCardWin', 'SetDragEvent', 'onRollOver', 'onRollOut', '"Retain"', '"Release"', 'HandCards', 'SetField("_CardData"', 'SetField("_EnableUse"', '"Kill"', 'ReadyAt', '.Face.Set("alpha"', 'card.Set("visible"', 'card.Set("touchable"', 'Strip')) {
        # HandCards is allowed in the explanatory comment but never called/mutated.
        if ($forbidden -eq 'HandCards') { continue }
        if ($readOnlySource.Contains($forbidden)) { throw "Unexpected gameplay/input mutation: $forbidden" }
    }
    foreach ($required in @('PlayerIsWatcher','FightWindow','get_draggingObject','Input.GetMouseButton(0)',
        '"IsDistribute"','"IsTweening"','HandLayout.Resting','"ShowCardStatus"','_hotPosition','_hotSize','node.Get<float>("scaleX") <= 0',
        'HandLayout.CanResume(shown, waiting, usePending, foreignMotion)', 'HandLayout.RetargetStart', '"_activeTweens"', 'HandLayout.InColumn',
        'var inputLocked = !_container!.Get<bool>("touchable");', 'if (hoverLocked) { _expanded = null;',
        'if (CardPointerBusy(dragging))', 'var hoverLocked = inputLocked || (!clickMode && Input.GetMouseButton(0));',
        'HoldHover(e, motion, pos)', 'HandLayout.Focus(slot)', 'FitEffects(e, true)',
        'GameUi.PointerPath(refresh: true)',
        '_focused = hovered.Pointer;', 'HandLayout.InFocus', 'if (e.CenterLocked)',
        'card.SetField("zoomInScale", zoom)', 'RestoreZoom(old)', 'RestoreZoom(Entries[key])', 'RestoreZoom(e)')) {
        if (!$source.Contains($required)) { throw "Missing layout safety: $required" }
    }
    if ($source.Contains('StaticCall(_tween, "GetTween"') -or $source.Contains('"ShowCardStatus")!.Value<int>() != 0')) {
        throw 'First-tween-only or persistent zoom gate regression'
    }
    if ($source.Contains('_drawAt')) { throw 'Render correction must not be throttled across frames' }
    if ($source.Contains('Input.GetMouseButton(0) || ModUi.IsOpen') -or $source.Contains('if (dragging != null)')) { throw 'Unrelated mouse/drag input must not stop hand layout' }
    foreach ($unsafeBinding in @('DelegateSupport', 'Il2CppSystem.Action', 'add_beforeUpdate', 'remove_beforeUpdate', 'ClassInjector')) {
        if ($source.Contains($unsafeBinding)) { throw "Native crash regression: $unsafeBinding" }
    }
    $driver = [regex]::Match($source, '(?s)    internal static void Tick\(.*?\r?\n    \}').Value
    if (!$driver.Contains('try { Update(); }') -or !$driver.Contains('Compatibility.Block("HandLayout", ex, Clear)')) {
        throw 'Hand layout must use the existing managed UI driver and circuit breaker'
    }
    Write-Host 'PASS: stacked-hand lifecycle bindings, native tween preservation, distribution/resting/hidden/drag gates; no replacement strips or gameplay actions. Runtime interaction remains unverified.'
} finally { $asm.Dispose() }

# Execute the production motion classifier/retargeter with a tiny native substitute.
# Metadata checks above cover the native array and field bindings; this is not a rendering test.
$retarget = [regex]::Match($source, '(?s)    private static void Retarget\(.*?\r?\n    \}').Value
$classify = [regex]::Match($source, '(?s)    private static bool LayoutMotion\(.*?\r?\n    \}').Value
$pending = [regex]::Match($source, '(?s)    private static bool UsePending\(.*?\r?\n    \}').Value
$hold = [regex]::Match($source, '(?s)    private static bool HoldHover\(.*?\r?\n    \}').Value
$displayReset = [regex]::Match($source, '(?s)    private static bool DisplayZoomReset\(.*?\r?\n    \}').Value
$focusBlock = [regex]::Match($source, '(?s)            if \(_expanded == group\)\r?\n            \{.*?\r?\n            \}').Value
$grouping = [regex]::Match($source, '(?s)        var cards = FrameCards;.*?aspects\[column\] = Aspect\(piles\[column\]\[0\]\);').Value
if (!$grouping) { throw 'Missing per-frame grouping' }
$clearFrame = [regex]::Match($source, '(?s)    private static void ClearFrame\(.*?\r?\n    \}').Value
$clearMotions = [regex]::Match($source, '(?s)    private static void ClearMotions\(.*?\r?\n    \}').Value
if (!$clearFrame -or !$clearMotions -or !$source.Contains('finally { ClearFrame(); }')) { throw 'Missing frame scratch/tween cleanup' }
if (!$focusBlock.Contains('if (clickMode) _focused = IntPtr.Zero;')) { throw 'Missing click focus exit reset' }
$effects = [regex]::Match($source, '(?s)    private static void FitEffects\(.*?\r?\n    \}').Value
$pointer = [regex]::Match($source, '(?s)    private static bool CardPointerBusy\(.*?\r?\n    \}').Value
$shown = [regex]::Match($source, '(?s)    private static bool Shown\(.*?\r?\n    \}').Value
$order = [regex]::Match($source, '(?s)    private static void SetStackOrder\(.*?\r?\n    \}').Value
$batch = [regex]::Match($source, '(?s)    private static void SetCardBatching\(.*?\r?\n    \}').Value
if (!$batch -or !$source.Contains('SetCardBatching(e, false)') -or !$source.Contains('SetCardBatching(e, true)')) { throw 'Missing card batching ownership/restore' }
if (!$order -or !$source.Contains('SetStackOrder(card, row, _expanded != null && _focused == card.Pointer)')) { throw 'Missing focused-only stack order restoration' }
if (!$retarget -or !$classify -or !$pending -or !$hold -or !$effects) { throw 'Production motion helpers missing' }
$layout = (Get-Content "$root/src/HandLayout.cs" -Raw).Replace('namespace BetterAstralParty;', '')
$opacity = [regex]::Match($source, '(?s)    private static void SetFoldedOpacity\(.*?\r?\n    \}').Value
$click = [regex]::Match($source, '(?s)    private static void PollPileClick\(.*?\r?\n    \}').Value
if (!$opacity -or !$click) { throw 'Missing grouped opacity/click helpers' }
$harness = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace __NS__ {
__LAYOUT__
struct Vector2 {
    public float x,y;
    public Vector2(float x,float y) { this.x=x; this.y=y; }
    public float sqrMagnitude => x*x+y*y;
    public static Vector2 operator -(Vector2 a,Vector2 b) => new(a.x-b.x,a.y-b.y);
}
sealed class RuntimeObject {
    public static int Reads;
    static int next;
    public IntPtr Pointer = (IntPtr)(++next);
    public Dictionary<string,object> Data = new();
    public string TypeName = "";
    public RuntimeObject Field(string key) => Data[key] as RuntimeObject ?? new() { Data = new() { ["boxed"] = Data[key] } };
    public T Field<T>(string key) => (T)Data[key];
    public T Value<T>() => (T)Data["boxed"];
    public RuntimeObject Get(string key) { Reads++; return (RuntimeObject)Data[key]; }
    public T Get<T>(string key) { Reads++; return (T)Data[key]; }
    public int Writes;
    public void Set(string key,object value) { Writes++; Data[key]=value; }
    public void SetField(string key,object value) => Data[key]=value;
}
static class Input {
    public static bool Held, Down, Up;
    public static bool GetMouseButton(int button)=>Held;
    public static bool GetMouseButtonDown(int button)=>Down;
    public static bool GetMouseButtonUp(int button)=>Up;
}
static class Application { public static bool isFocused = true; }
static class Plugin { public static readonly DiagnosticStub Diagnostics=new(); }
sealed class DiagnosticStub { public bool IsRecording=false; public readonly List<string> Lines=new(); public void Write(string message)=>Lines.Add(message); }
static class GameUi {
    public static RuntimeObject[] Path=Array.Empty<RuntimeObject>();
    public static RuntimeObject[] PointerPath(bool refresh)=>Path;
}
public static class MotionCheck {
    static RuntimeObject? _container = new();
    static bool _cardPress;
    static HandGroup? _expanded, _pressedPile;
    static bool _outsidePress;
    static Vector2 _pilePressAt;
    static IntPtr _focused;
    static Dictionary<HandGroup,RuntimeObject> PileTargets = new();
    sealed class Entry { public bool HasLayout, AlphaOwned, Rendered, Owned; public bool? Batching; public Vector2 Applied,LastShow; public RuntimeObject? ScaleTween; public RuntimeObject Card = new(); public HandGroup Group; public int Id,Order,FrameOrder; public float AspectValue; }
    static readonly Dictionary<IntPtr,Entry> Entries = new();
    static readonly List<Entry> FrameCards = new();
    static readonly List<HandGroup> FrameGroups = new();
    static readonly List<Entry>[] FramePiles = Enum.GetValues<HandGroup>().Select(_=>new List<Entry>()).ToArray();
    static readonly float[] FrameAspects = new float[FramePiles.Length];
    static readonly List<IntPtr> RemovedCards = new();
    static readonly List<HandGroup> RemovedGroups = new();
    static readonly Dictionary<IntPtr,List<RuntimeObject>> Motions = new();
    static readonly Stack<List<RuntimeObject>> MotionPool = new();
    static int AspectReads;
    static float Aspect(Entry e) { AspectReads++; return e.AspectValue; }
    static void CheckGrouping() {
        for (int count=0;count<=28;count++) {
            Entries.Clear();
            for(int i=0;i<count;i++) {
                var e=new Entry { Group=(HandGroup)(i%7), Id=i%3, Order=i%2, AspectValue=1.5f+i*.001f };
                Entries.Add(e.Card.Pointer,e);
            }
            for(int frame=0;frame<3;frame++) {
                AspectReads=0;
                var allocated=GC.GetAllocatedBytesForCurrentThread();
__GROUPING__
                allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
                if(frame>0) Check(allocated==0);
                Check(AspectReads==groups.Count);
                var expected=Entries.Values.OrderBy(e=>e.Group).ThenBy(e=>e.Id).ThenBy(e=>e.Order).ToArray();
                Check(piles.SelectMany(p=>p).SequenceEqual(expected));
                for(int column=0;column<groups.Count;column++) {
                    var oldPile=expected.Where(e=>e.Group==groups[column]).ToArray();
                    Check(piles[column].SequenceEqual(oldPile) && aspects[column]==oldPile[0].AspectValue);
                    foreach(var height in new[]{720f,1080f,1440f,2160f})
                        for(int row=0;row<oldPile.Length;row++)
                            Check(HandLayout.Place(height*16/9,height,groups.Count,column,piles[column].Count,row,aspects[column])
                                ==HandLayout.Place(height*16/9,height,groups.Count,column,oldPile.Length,row,oldPile[0].AspectValue));
                }
                // Native dimensions/order and membership can change on the next frame.
                foreach(var e in Entries.Values) { e.AspectValue+=.02f; e.Order++; }
                if(Entries.Count>0) Entries.Remove(Entries.Keys.First());
            }
        }
        Entries.Clear();
        Console.WriteLine("PASS: per-frame piles preserve stable duplicate ordering, removals, live dimensions and HD-4K geometry; warm grouping allocates zero bytes.");
    }
    static readonly string[] EffectFields = { "effectOutline", "effectOutline_Suggest_Bottom", "effectTempCard" };
    static bool Near(Vector2 a,Vector2 b) => HandLayout.Resting(a.x,a.y,b.x,b.y);
__CLEARFRAME__
__CLEARMOTIONS__
__RETARGET__
__CLASSIFY__
__PENDING__
__HOLD__
__DISPLAYRESET__
__EFFECTS__
__POINTER__
__SHOWN__
__ORDER__
__BATCH__
__OPACITY__
__CLICK__
    static RuntimeObject Node(RuntimeObject? parent) => new() { Data=new() {
        ["parent"]=parent!, ["isDisposed"]=false,["onStage"]=true,["internalVisible"]=true,
        ["internalVisible2"]=true,["alpha"]=1f,["scaleX"]=1f,["scaleY"]=1f
    }};
    static bool OldShown(RuntimeObject item) {
        if(item.Get<bool>("isDisposed") || !item.Get<bool>("onStage")) return false;
        for(var n=item;n!=null;n=n.Get("parent"))
            if(!n.Get<bool>("internalVisible") || !n.Get<bool>("internalVisible2")) return false;
        for(var n=item;n!=null;n=n.Get("parent"))
            if(n.Get<float>("alpha")<=0 || n.Get<float>("scaleX")<=0 || n.Get<float>("scaleY")<=0) return false;
        return true;
    }
    static RuntimeObject Motion(int prop,Vector2 end,float t) => new() { Data = new() {
        ["_propType"]=prop, ["normalizedTime"]=t,
        ["startValue"]=new RuntimeObject { Data = new() { ["vec2"]=new Vector2(0,0) } },
        ["endValue"]=new RuntimeObject { Data = new() { ["vec2"]=end } },
        ["duration"]=.2f, ["callback"]=new object()
    }};
    static void Check(bool v) { if(!v) throw new Exception("Hand motion regression"); }
    public static void Run() {
        CheckGrouping();
        var frameMotion = Motion(4,new(10,20),.4f);
        var frameCallback = frameMotion.Data["callback"];
        var frameList = new List<RuntimeObject> { frameMotion };
        for(int frame=0;frame<10;frame++) {
            FrameCards.Add(new Entry()); FrameGroups.Add(HandGroup.Attack);
            FramePiles[0].Add(FrameCards[0]); RemovedCards.Add((IntPtr)1); RemovedGroups.Add(HandGroup.Defense);
            if(frame>0) { frameList=MotionPool.Pop(); frameList.Add(frameMotion); }
            Motions.Add((IntPtr)1,frameList);
            ClearFrame(); // Same production cleanup used by tick's finally and feature shutdown.
            Check(FrameCards.Count==0 && FrameGroups.Count==0 && FramePiles.All(p=>p.Count==0));
            Check(RemovedCards.Count==0 && RemovedGroups.Count==0 && Motions.Count==0);
            Check(MotionPool.Count==1 && ReferenceEquals(MotionPool.Peek(),frameList) && frameList.Count==0);
            Check(frameMotion.Writes==0 && ReferenceEquals(frameMotion.Data["callback"],frameCallback));
        }
        MotionPool.Clear();
        Console.WriteLine("PASS: production frame cleanup releases memberships/tween references, reuses list capacity and leaves native callbacks untouched (substitute).");
        var pile = new List<Entry> { new Entry(), new Entry() };
        var ready = new HashSet<Entry>(pile);
        var group = HandGroup.Attack;
        var groups = new List<HandGroup> { group };
        var column = 0; var width = 1920f; var height = 1080f; var aspect = 1.5f; var pileTop = 730f; var unit = 1f;
        void FocusFrame(bool clickMode, Vector2 pointer) {
__FOCUS_BLOCK__
        }
        foreach(var clickMode in new[]{true,false}) {
            _expanded=group; _focused=IntPtr.Zero;
            HandLayout.Slot Cell(int row) => clickMode ? HandLayout.ClickRow(width,height,pile.Count,row,aspect,pileTop)
                : HandLayout.Place(width,height,groups.Count,column,pile.Count,row,aspect,true);
            var a=Cell(0); var b=Cell(1);
            FocusFrame(clickMode,new(a.X+a.Width/2,a.Y+a.Height/2)); Check(_focused==pile[0].Card.Pointer);
            var edge=HandLayout.Focus(a);
            FocusFrame(clickMode,new(edge.X,edge.Y+edge.Height/2)); Check(_focused==pile[0].Card.Pointer);
            FocusFrame(clickMode,new(0,0)); Check(_focused==(clickMode?IntPtr.Zero:pile[0].Card.Pointer));
            FocusFrame(clickMode,new(b.X+b.Width/2,b.Y+b.Height/2)); Check(_focused==pile[1].Card.Pointer);
            FocusFrame(clickMode,new(0,0)); Check(_focused==(clickMode?IntPtr.Zero:pile[1].Card.Pointer));
            Check(_expanded==group);
        }
        _expanded=null; _focused=IntPtr.Zero;
        var resetEntry = new Entry { Card=Node(null), Rendered=true, Owned=true, Applied=new(20,500) };
        Check(DisplayZoomReset(resetEntry,new(20,600)));
        Check(!DisplayZoomReset(resetEntry,new(20,500)));
        Check(!DisplayZoomReset(resetEntry,new(21,600)));
        resetEntry.Card.Set("scaleX",1.6f); resetEntry.Card.Set("scaleY",1.6f);
        Check(DisplayZoomReset(resetEntry,new(20,400)));
        Check(DisplayZoomReset(resetEntry,new(20,600))); // Scale can already have been restored.
        resetEntry.Card.Set("scaleX",.73f); resetEntry.Card.Set("scaleY",.73f);
        Check(DisplayZoomReset(resetEntry,new(20,600)) && DisplayZoomReset(resetEntry,new(20,400)));
        resetEntry.Rendered=false; Check(!DisplayZoomReset(resetEntry,new(20,400)));
        resetEntry.Rendered=true; resetEntry.Owned=false; Check(!DisplayZoomReset(resetEntry,new(20,400)));
        Console.WriteLine("PASS: native DisplayZoom direct reset detection; drag/lifecycle invalidation; both native zoom directions.");
        var alphaEntry = new Entry { Card = Node(null) };
        SetFoldedOpacity(alphaEntry,true);
        Check(alphaEntry.Card.Get<float>("alpha")==.75f);
        var alphaWrites=alphaEntry.Card.Writes;
        for(int i=0;i<100;i++) SetFoldedOpacity(alphaEntry,true);
        Check(alphaEntry.Card.Writes==alphaWrites);
        SetFoldedOpacity(alphaEntry,false); Check(alphaEntry.Card.Get<float>("alpha")==1f);
        SetFoldedOpacity(alphaEntry,true); alphaEntry.Card.Set("alpha",.3f);
        SetFoldedOpacity(alphaEntry,false); Check(alphaEntry.Card.Get<float>("alpha")==.3f);
        SetFoldedOpacity(alphaEntry,true); Check(alphaEntry.Card.Get<float>("alpha")==.3f);
        var pileTarget=Node(null); pileTarget.Set("visible",true);
        PileTargets[HandGroup.Attack]=pileTarget;
        var hitPath=new[]{pileTarget};
        void Click(float dx=0) {
            Input.Down=Input.Held=true; Input.Up=false;
            PollPileClick(hitPath,new(100,100),1,false);
            Input.Down=Input.Held=false; Input.Up=true;
            PollPileClick(hitPath,new(100+dx,100),1,false);
            Input.Up=false;
        }
        Plugin.Diagnostics.IsRecording=true;
        Click(); Check(_expanded==HandGroup.Attack);
        Check(Plugin.Diagnostics.Lines.Count==2 && Plugin.Diagnostics.Lines[0].Contains("phase=down")
            && Plugin.Diagnostics.Lines[1].Contains("phase=up") && Plugin.Diagnostics.Lines[0].Contains("pileHit=True"));
        Plugin.Diagnostics.IsRecording=false;
        PollPileClick(Array.Empty<RuntimeObject>(),new(0,0),1,false); Check(_expanded==HandGroup.Attack);
        Click(); Check(_expanded==null);
        Click(30); Check(_expanded==null);
        Application.isFocused=false; Click(); Check(_expanded==null); Application.isFocused=true;
        Click(); Check(_expanded==HandGroup.Attack);
        var outside=Array.Empty<RuntimeObject>();
        Input.Down=Input.Held=true;
        PollPileClick(outside,new(100,100),1,false);
        Input.Down=Input.Held=false; Input.Up=true;
        PollPileClick(outside,new(100,100),1,false);
        Check(_expanded==null); Input.Up=false;
        Click();
        var cardPath=new[]{new RuntimeObject { TypeName="UIHandCard_Button_Card" }};
        Input.Down=Input.Held=true;
        PollPileClick(cardPath,new(100,100),1,false);
        Input.Down=Input.Held=false; Input.Up=true;
        PollPileClick(outside,new(100,100),1,false);
        Check(_expanded==HandGroup.Attack); Input.Up=false; // Card-origin release is not an outside click.
        Input.Down=Input.Held=true;
        PollPileClick(outside,new(100,100),1,true); // Native use/target/cancel window owns press.
        Input.Down=Input.Held=false; Input.Up=true;
        PollPileClick(outside,new(100,100),1,false); // Window closed on release.
        Check(_expanded==HandGroup.Attack); Input.Up=false;
        Input.Down=Input.Held=true;
        PollPileClick(outside,new(100,100),1,false);
        Input.Down=false;
        PollPileClick(outside,new(150,100),1,false);
        Input.Held=false; Input.Up=true;
        PollPileClick(outside,new(100,100),1,false);
        Check(_expanded==HandGroup.Attack); Input.Up=false; // Pan out and back is not a click.
        foreach (var original in new[] { false, true }) {
            var batchEntry=new Entry { Card=new RuntimeObject { Data=new() { ["fairyBatching"]=original,["isDisposed"]=false } } };
            for(int cycle=0;cycle<3;cycle++) {
                SetCardBatching(batchEntry,false);
                Check(batchEntry.Card.Get<bool>("fairyBatching"));
                var writes=batchEntry.Card.Writes;
                for(int frame=0;frame<100;frame++) SetCardBatching(batchEntry,false);
                Check(batchEntry.Card.Writes==writes); // No per-frame rebuild.
                SetCardBatching(batchEntry,true);
                Check(batchEntry.Card.Get<bool>("fairyBatching")==original && batchEntry.Batching==null);
            }
            SetCardBatching(batchEntry,false);
            batchEntry.Card.Data["isDisposed"]=true;
            var before=batchEntry.Card.Writes;
            SetCardBatching(batchEntry,true);
            Check(batchEntry.Card.Writes==before && batchEntry.Batching==null);
        }
        // Renderer model of the verified native CollectChildren/SetRenderingOrder boundary:
        // expanded disjoint cards can batch both backgrounds before both texts. Moving alone
        // leaves that cache intact; per-card boundaries prevent this cross-card flattening.
        var flatExpandedCache = new[] { "back.art", "front.art", "back.text", "front.text" };
        Check(Array.IndexOf(flatExpandedCache,"back.text") > Array.IndexOf(flatExpandedCache,"front.art"));
        foreach(var focus in new[] { -1,0,1,-1,1,0,-1 }) {
            var entries=Enumerable.Range(0,2).Select(_=>new Entry { Card=new RuntimeObject { Data=new() {
                ["fairyBatching"]=false,["isDisposed"]=false,["sortingOrder"]=0,["_CustomSortingOrder"]=0 } } }).ToArray();
            for(int row=0;row<2;row++) { SetCardBatching(entries[row],false); SetStackOrder(entries[row].Card,row,row==focus); }
            var sorted=entries.OrderBy(e=>e.Card.Get<int>("sortingOrder")).ToArray();
            var rendered=sorted.SelectMany(e=>e.Card.Get<bool>("fairyBatching")
                ? new[] { (e.Card.Pointer,"art"),(e.Card.Pointer,"text") } : throw new Exception("Flattened card")).ToArray();
            Check(rendered[0].Pointer==rendered[1].Pointer && rendered[2].Pointer==rendered[3].Pointer);
        }
        Console.WriteLine("PASS: per-card render boundaries, hover/collapse model, idle no-write, native baseline/disposal restoration. Not an in-game renderer test.");
        var stack = Enumerable.Range(0,4).Select(_=>new RuntimeObject { Data=new() { ["sortingOrder"]=100,["_CustomSortingOrder"]=0 } }).ToArray();
        // Enter each card, cross rows, leave the column, and repeat with stale native hover priority.
        foreach (var focus in new[] { 0, 3, 1, -1, 2, -1 }) {
            for (int row=0;row<stack.Length;row++) {
                stack[row].Data["sortingOrder"]=100;
                SetStackOrder(stack[row],row,row==focus);
                Check(stack[row].Get<int>("sortingOrder")== (row==focus ? 100 : row));
                Check(stack[row].Field("_CustomSortingOrder").Value<int>()==row);
                var writes=stack[row].Writes;
                SetStackOrder(stack[row],row,row==focus);
                Check(stack[row].Writes==writes); // Idle frames must not rebuild native child sorting.
            }
        }
        Console.WriteLine("PASS: hover/cross-row/collapse restores stack order; unchanged sorting performs no setter calls.");
        var root=Node(null); var window=Node(root); var container=Node(window);
        var cards=Enumerable.Range(0,12).Select(_=>Node(container)).ToArray();
        var faces=cards.Select(c=>Node(c)).ToArray();
        RuntimeObject.Reads=0;
        Check(OldShown(container));
        for(int i=0;i<cards.Length;i++) Check(OldShown(cards[i]) && OldShown(faces[i]));
        var oldReads=RuntimeObject.Reads;
        RuntimeObject.Reads=0;
        Check(Shown(container));
        for(int i=0;i<cards.Length;i++) Check(Shown(cards[i],container) && Shown(faces[i],cards[i]));
        Check(RuntimeObject.Reads < oldReads/2);
        Console.WriteLine($"Hand visibility, 12 cards: {oldReads} -> {RuntimeObject.Reads} native-call substitutes; same result.");
        foreach(var key in new[]{"isDisposed","onStage","internalVisible","internalVisible2","alpha","scaleX","scaleY"}) {
            var faceSample=faces[0]; var original=faceSample.Data[key];
            faceSample.Data[key]=key=="isDisposed"?(object)true:original is bool?(object)false:0f;
            Check(!Shown(faceSample,cards[0])); faceSample.Data[key]=original;
        }
        root.Data["scaleX"]=0f; Check(!Shown(container)); root.Data["scaleX"]=1f;
        var handCard=new RuntimeObject { TypeName="UIHandCard_Button_Card", Data=new() { ["parent"]=_container! } };
        var otherCard=new RuntimeObject { TypeName="UIHandCard_Button_Card", Data=new() { ["parent"]=new RuntimeObject() } };
        Input.Held=Input.Down=true;
        Check(!CardPointerBusy(null)); // press on world, not hand
        Input.Down=false; GameUi.Path=new[]{handCard};
        Check(!CardPointerBusy(null)); // pan crosses a card: not a new card press
        Check(!CardPointerBusy(otherCard)); // another UI drag cannot suspend field hand
        Input.Down=true;
        Check(CardPointerBusy(null)); // actual hand press, before native drag threshold
        Input.Down=false; GameUi.Path=Array.Empty<RuntimeObject>();
        Check(CardPointerBusy(null)); // card press moved outside the original bounds
        Input.Held=false;
        Check(CardPointerBusy(handCard)); // native drag still active on release frame
        Check(!CardPointerBusy(null)); // native drag ended; resume immediately
        var old = new Vector2(100,800); var rest = new Vector2(700,750);
        var show = new Vector2(700,400); var target = new Vector2(900,200);
        var entry = new Entry { HasLayout=true, Applied=old, LastShow=new(100,400) };
        foreach(var field in EffectFields) entry.Card.Data[field] = new RuntimeObject { Data = new() {
            ["displayObject"]=new RuntimeObject { Data = new() { ["scale"]=new Vector2(43,43) } }
        }};
        for(var frame=0; frame<90; frame++) {
            entry.Card.Data["sortingOrder"] = frame%2==0 ? 100 : 0;
            // Native HandleEvents runs AFTER EventSystem.Update and writes 72 on enter.
            foreach(var field in EffectFields)
                entry.Card.Field(field).Get("displayObject").Set("scale",new Vector2(frame%2==0?72:43,frame%2==0?72:43));
            FitEffects(entry,false); // correction helper only; native event/render timing is not simulated
            foreach(var field in EffectFields)
                Check(Near(entry.Card.Field(field).Get("displayObject").Get<Vector2>("scale"),new(43,43)));
        }
        entry.Card.Data["sortingOrder"]=100;
        FitEffects(entry,true);
        Check(Near(entry.Card.Field(EffectFields[0]).Get("displayObject").Get<Vector2>("scale"),new(72,72)));
        // Repeated native enter/exit in the same column must not send a settled card toward the screen bottom.
        for(var crossing=0; crossing<30; crossing++) {
            var hover=Motion(4,show,.15f);
            hover.Get("startValue").Set("vec2",old);
            var callback=hover.Data["callback"];
            Check(HoldHover(entry,hover,old));
            Check(Near(hover.Get("startValue").Get<Vector2>("vec2"),old));
            Check(Near(hover.Get("endValue").Get<Vector2>("vec2"),old));
            Check(ReferenceEquals(callback,hover.Data["callback"]));
            Check(!HoldHover(entry,hover,target)); // genuine focus/column change must animate
        }
        var moves = new[] { Motion(4,old,.4f), Motion(4,rest,.2f), Motion(4,show,.6f) };
        foreach(var move in moves) {
            // Native reflow wrote a NEW rest point; the preceding hover/reflow still exists.
            Check(LayoutMotion(entry,move,rest,show));
            var callback=move.Data["callback"]; var duration=move.Data["duration"];
            var current=new Vector2(450,600);
            Retarget(move,current,target);
            var start=move.Get("startValue").Get<Vector2>("vec2");
            var end=move.Get("endValue").Get<Vector2>("vec2");
            var t=move.Get<float>("normalizedTime");
            Check(Near(new(start.x+(end.x-start.x)*t,start.y+(end.y-start.y)*t),current));
            Check(Near(end,target));
            Check(ReferenceEquals(callback,move.Data["callback"]) && duration.Equals(move.Data["duration"]));
            Retarget(move,current,target); // unchanged target must not continually rebase
            Check(Near(start,move.Get("startValue").Get<Vector2>("vec2")));
        }
        Check(!LayoutMotion(entry,Motion(4,new(0,0),.1f),rest,show)); // deal waypoint/use motion
        Check(!LayoutMotion(entry,Motion(15,old,.1f),rest,show)); // fade
        Check(!LayoutMotion(entry,Motion(11,old,.1f),rest,show)); // native scale
        entry.ScaleTween=Motion(11,old,.1f);
        Check(LayoutMotion(entry,entry.ScaleTween,rest,show));
        entry.HasLayout=false;
        Check(!LayoutMotion(entry,Motion(4,old,.1f),rest,show)); // recycled/new identity
        Check(HandLayout.CanResume(true,false,false,false)); // completed off-target move
        var bridge = new RuntimeObject { Data = new() { ["_isLocking"]=false } };
        var card = new RuntimeObject { Data = new() {
            ["onRollOut"]=new RuntimeObject { Data = new() { ["_bridge"]=bridge } }
        }};
        // Timeout/move/other-player turns lock the container, not the passive cards' roll-out.
        // Deal/reflow retains only roll-over; it must also remain eligible.
        Check(!UsePending(card,true));
        Check(HandLayout.CanResume(true,false,UsePending(card,true),false));
        bridge.Set("_isLocking",true); // DisplayCard followed by ForbidOperate: only used card is excluded.
        Check(UsePending(card,true));
        Check(!HandLayout.CanResume(true,false,UsePending(card,true),false));
        Check(!UsePending(card,false)); // native input unlock
        bridge.Set("_isLocking",false); // cancel/rebind releases native event lock
        Check(HandLayout.CanResume(true,false,UsePending(card,true),false));
        Check(!HandLayout.CanResume(false,false,UsePending(card,true),false));
        Check(!HandLayout.CanResume(true,true,UsePending(card,true),false));
        Check(!HandLayout.CanResume(true,false,UsePending(card,true),true));
        Check(Plugin.Diagnostics.Lines.Count==2); // Click tracing remains opt-in after it is disabled.
        Console.WriteLine("PASS: input-locked passive hand recovery, per-card pending-use protection and cancel/unlock recovery (production helper, native substitute).");
        Console.WriteLine("PASS: production retarget continuity, concurrent reflow/hover endpoints, callbacks/durations unchanged, foreign motion and recycled identity isolation (native substitute).");
    }
}
}
'@
$ns = 'HandMotion' + [Guid]::NewGuid().ToString('N')
$harness = $harness.Replace('__NS__',$ns).Replace('__LAYOUT__',$layout).Replace('__RETARGET__',$retarget).Replace('__CLASSIFY__',$classify).Replace('__PENDING__',$pending).Replace('__HOLD__',$hold).Replace('__EFFECTS__',$effects).Replace('__POINTER__',$pointer).Replace('__SHOWN__',$shown).Replace('__ORDER__',$order).Replace('__BATCH__',$batch)
$harness = $harness.Replace('__OPACITY__',$opacity).Replace('__CLICK__',$click)
$harness = $harness.Replace('__DISPLAYRESET__',$displayReset).Replace('__FOCUS_BLOCK__',$focusBlock)
$harness = $harness.Replace('__GROUPING__',$grouping)
$harness = $harness.Replace('__CLEARFRAME__',$clearFrame).Replace('__CLEARMOTIONS__',$clearMotions)
Add-Type -TypeDefinition $harness
([type]"$ns.MotionCheck")::Run()
if ($source -match 'HandTargetUi|_targetCeiling|BattlePreMonsterWindow|BattleSelectMonsterWindow' -or $layout -match 'TargetPanel') {
    throw 'Target UI rollback regression: grouped hand must leave native target windows unchanged'
}
