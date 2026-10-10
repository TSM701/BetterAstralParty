$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -Path "$root/.deps/bepinex/BepInEx/core/Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$root/.research/extracted/AstralParty.Runtime.dll.dll")
try {
    $object = $asm.MainModule.Types | Where-Object FullName -eq 'FairyGUI.GObject'
    $scale = $object.Methods | Where-Object Name -eq 'HandleScaleChanged'
    if ($scale.Parameters.Count -ne 0 -or ($scale.Body.Instructions.Operand -join ' ') -notmatch '_scaleX.*_scaleY.*DisplayObject::SetScale') { throw 'Native presentation callback changed' }
    $bridge = $asm.MainModule.Types | Where-Object FullName -eq 'FairyGUI.EventBridge'
    $body = ($bridge.Methods | Where-Object Name -eq 'CallInternal').Body.Instructions.Operand -join ' '
    if ($body -notmatch '_isLocking.*EventCallback1::Invoke.*EventCallback0::Invoke') { throw 'Native callback order/lock changed' }
    $card = $asm.MainModule.Types | Where-Object FullName -eq 'UI.UIHandCard_Button_Card'
    $body = ($card.Methods | Where-Object Name -eq 'RendererCard').Body.Instructions.Operand -join ' '
    if ($body -notmatch 'get_onRollOver.*EventListener::Set\(FairyGUI.EventCallback0\).*get_onRollOut.*EventListener::Set\(FairyGUI.EventCallback0\)') { throw 'Native hover callback type changed' }
    foreach ($contract in @(
        @('FairyGUI.StageEngine','LateUpdate','Stage::InternalUpdate'),
        @('FairyGUI.Stage','InternalUpdate','HandleEvents.*beforeUpdate.*DisplayObject::Update'),
        @('FairyGUI.Stage','get_touchTarget','GetHitTarget'),
        @('UI.HandCardPanel','AddEvent','get_onTouchBegin.*ZoomCard.*EventCallback1'),
        @('UI.HandCardPanel','ZoomCard','get_touchTarget.*LookCardByZoom'),
        @('FairyGUI.GearBase','set_controller','Init'),
        @('FairyGUI.GearSize','Init','get_scaleX.*get_scaleY.*_default'),
        @('FairyGUI.GearSize','Apply','_tweenConfig.*SetSize.*SetScale'),
        @('FairyGUI.GearXY','Init','get_x.*get_y.*_default'),
        @('FairyGUI.GearXY','Apply','_tweenConfig.*SetXY'))) {
        $t = $asm.MainModule.Types | Where-Object FullName -eq $contract[0]
        $il = ($t.Methods | Where-Object Name -eq $contract[1]).Body.Instructions.Operand -join ' '
        if ($il -notmatch $contract[2]) { throw "World press presentation contract changed: $contract" }
    }
} finally { $asm.Dispose() }

$source = Get-Content "$root/src/HandOutlineBinding.cs" -Raw
$clickSource = Get-Content "$root/src/HandClickBinding.cs" -Raw
foreach ($forbidden in @('DelegateSupport','ClassInjector','System.Action','beforeUpdate','SetField("_isLocking"','"Retain"','"Release"')) {
    if ($source.Contains($forbidden) -or $clickSource.Contains($forbidden)) { throw "Unsafe presentation binding: $forbidden" }
}
# Execute the actual binding class with native API substitutes. This verifies event ordering,
# replacement and ownership, NOT HybridCLR delegate execution or in-game rendering.
$harness = @'
#nullable enable
using System;
using System.Collections.Generic;
namespace UnityEngine { public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; } } }
namespace Il2CppInterop.Runtime { public static class IL2CPP {
    public static IntPtr il2cpp_type_get_object(IntPtr p)=>p;
    public static IntPtr il2cpp_class_get_type(IntPtr p)=>p;
} }
namespace Il2CppSystem {
    public class Object { public IntPtr Pointer; public Object(IntPtr p) { Pointer=p; } }
    public class Type : Object { public Type(IntPtr p):base(p) {} }
    public class Delegate : Object {
        static int next=10000;
        static Dictionary<IntPtr,Action> all=new();
        public Delegate(IntPtr p):base(p) {}
        public Delegate(Action a):base((IntPtr)(++next)) { all[Pointer]=a; }
        public static Delegate CreateDelegate(Type type,Object target,string method) {
            if(method=="Apply") return new Delegate(()=>Check.RuntimeObject.All[target.Pointer].Apply());
            if(method!="HandleScaleChanged") throw new Exception("Unexpected native method");
            return new Delegate(()=>Check.RuntimeObject.All[target.Pointer].HandleScaleChanged());
        }
        public static Delegate? Remove(Delegate? a,Delegate? b) {
            if(a==null) return null;
            var result=(Action?)System.Delegate.Remove(all[a.Pointer],b==null?null:all[b.Pointer]);
            return result==null?null:new Delegate(result);
        }
        public static Delegate Combine(Delegate? a,Delegate? b) => new Delegate((Action)System.Delegate.Combine(a==null?null:all[a.Pointer],b==null?null:all[b.Pointer])!);
        public void Invoke()=>all[Pointer]();
    }
}
namespace Check {
    public class RuntimeObject {
        public static int Writes;
        static int next;
        public static Dictionary<IntPtr,RuntimeObject> All=new();
        public IntPtr Pointer;
        public Dictionary<string,object?> Data;
        public RuntimeObject() { Pointer=(IntPtr)(++next); Data=new(); All[Pointer]=this; }
        public RuntimeObject(IntPtr p) { Pointer=p;Data=All.TryGetValue(p,out var obj)?obj.Data:new(); }
        public static IntPtr FindClass(string ns,string name)=>(IntPtr)(name=="GearXY"?2:name=="GearSize"?3:1);
        public static RuntimeObject New(IntPtr klass,params object[] args) => new() { Data=new() {
            ["kind"]=(int)klass, ["owner"]=args.Length==0?null:args[0] } };
        public void Set(string key,object value) {
            Data[key]=value;
            if(key=="controller") {
                var owner=(RuntimeObject)Data["owner"]!;
                foreach(var field in new[]{"x","y","_scaleX","_scaleY"}) Data[field]=owner.Data[field];
            }
        }
        public void Apply() {
            var owner=(RuntimeObject)Data["owner"]!;
            foreach(var field in ((int)Data["kind"]! ==2 ? new[]{"x","y"}:new[]{"_scaleX","_scaleY"})) owner.Data[field]=Data[field];
        }
        public RuntimeObject? Field(string key)=>Data.GetValueOrDefault(key) as RuntimeObject;
        public RuntimeObject? Get(string key)=>Field(key);
        public T Get<T>(string key)=>(T)Data[key switch { "scaleX"=>"_scaleX", "scaleY"=>"_scaleY", _=>key }]!;
        public void SetField(string key,object? value) { Writes++; Data[key]=value; }
        public void HandleScaleChanged() { Data["renderX"]=Data["_scaleX"];Data["renderY"]=Data["_scaleY"]; }
    }
    public static class Regression {
        static void Assert(bool v) { if(!v) throw new Exception("Outline ordering/ownership regression"); }
        public static void Run() {
            var worldCard=new RuntimeObject { Data=new() { ["x"]=25f,["y"]=700f,["_scaleX"]=.73f,["_scaleY"]=.73f } };
            var touchBridge=new RuntimeObject();
            var stage=new RuntimeObject { Data=new() { ["onTouchBegin"]=new RuntimeObject { Data=new() { ["_bridge"]=touchBridge } } } };
            var nativeCalls=0;
            var other=new Il2CppSystem.Delegate(()=>nativeCalls++);
            touchBridge.Data["_callback0"]=new RuntimeObject(other.Pointer);
            foreach(var mode in new[]{"Click","Hover"})
                foreach(var shift in new[]{0f,100f,-100f}) {
                    HandClickBinding.Arm(stage,new[]{worldCard}); // Update snapshot
                    worldCard.Data["y"]=700f+shift; worldCard.Data["_scaleX"]=worldCard.Data["_scaleY"]=1f; // LateUpdate callback1
                    new Il2CppSystem.Delegate(touchBridge.Field("_callback0")!.Pointer).Invoke(); // native callback0 before render
                    Assert((float)worldCard.Data["y"]! ==700f && worldCard.Get<float>("scaleX")==.73f);
                    HandClickBinding.Clear(); // Next frame, or lifecycle cleanup
                    worldCard.Data["y"]=400f;
                    new Il2CppSystem.Delegate(touchBridge.Field("_callback0")!.Pointer).Invoke();
                    Assert((float)worldCard.Data["y"]! ==400f); // No stale correction on subsequent drag/use
                    worldCard.Data["y"]=700f;
                }
            Assert(nativeCalls==12);
            var card=new RuntimeObject();
            var fields=new[]{"outline","suggestion","temporary"};
            foreach(var field in fields) card.Data[field]=new RuntimeObject { Data=new() { ["_scaleX"]=1f,["_scaleY"]=2f,["isDisposed"]=false } };
            var calls=0;
            var native=new Il2CppSystem.Delegate(()=> { calls++; foreach(var field in fields) card.Field(field)!.Data["renderX"]=72f; });
            var extra=new Il2CppSystem.Delegate(()=>calls++);
            foreach(var name in new[]{"onRollOver","onRollOut"}) card.Data[name]=new RuntimeObject { Data=new() { ["_bridge"]=new RuntimeObject { Data=new() { ["_callback0"]=new RuntimeObject(native.Pointer),["_isLocking"]=false } } } };
            var binding=new HandOutlineBinding(card,fields);
            for(var frame=0;frame<100;frame++) {
                var bridge=card.Get(frame%2==0?"onRollOver":"onRollOut")!.Field("_bridge")!;
                if(frame%7==0) bridge.SetField("_callback0",new RuntimeObject(native.Pointer)); // RendererCard.Set
                binding.Apply();
                var writes=RuntimeObject.Writes;
                Assert(!binding.Apply() && RuntimeObject.Writes==writes); // idle layout: no writes/rebinding
                var before=calls;
                new Il2CppSystem.Delegate(bridge.Field("_callback0")!.Pointer).Invoke(); // AFTER managed tick
                Assert(calls==before+1);
                foreach(var field in fields) Assert((float)card.Field(field)!.Data["renderX"]! ==43f); // same-frame render
                Assert(!(bool)bridge.Data["_isLocking"]!);
            }
            var over=card.Get("onRollOver")!.Field("_bridge")!;
            over.SetField("_callback0",new RuntimeObject(Il2CppSystem.Delegate.Combine(new(over.Field("_callback0")!.Pointer),extra).Pointer));
            binding.Restore();
            var count=calls;
            new Il2CppSystem.Delegate(over.Field("_callback0")!.Pointer).Invoke();
            Assert(calls==count+2); // native and third-party callbacks preserved
            foreach(var field in fields) { var g=card.Field(field)!; Assert((float)g.Data["renderX"]! ==72f && g.Get<float>("scaleX")==1f && g.Get<float>("scaleY")==2f); }
        }
    }
}
'@
$source = $source.Replace('namespace BetterAstralParty;', 'namespace Check {') + "`n}"
$source += "`nnamespace Check {`n" + ($clickSource -replace '(?m)^using .*;\r?\n','' -replace 'namespace BetterAstralParty;','') + "`n}"
$namespace = 'OutlineCheck' + [guid]::NewGuid().ToString('N')
$combined = $source + "`n" + $harness.Replace('using System;','').Replace('using System.Collections.Generic;','').Replace('#nullable enable','')
$combined = "#nullable enable`nusing System;`nusing System.Collections.Generic;`n" + $combined.Replace('Check.', "$namespace.").Replace('namespace Check {', "namespace $namespace {")
Add-Type -TypeDefinition $combined -Language CSharp
([type]"$namespace.Regression")::Run()
Write-Host 'PASS: same-event world-press geometry in Click/Hover (including scale-only reset), cleanup before subsequent drag/use, 100 outline transitions, native/third-party callback preservation. Native runtime execution remains unverified.'
