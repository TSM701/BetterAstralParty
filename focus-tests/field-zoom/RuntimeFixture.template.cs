#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
public struct Vector2 { public float x,y; public Vector2(float a,float b){x=a;y=b;} }
public struct Vector3 {
    public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
    public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>MathF.Sqrt(sqrMagnitude);
    public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
    public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
    public static Vector3 Min(Vector3 a,Vector3 b)=>new(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));
    public static Vector3 Max(Vector3 a,Vector3 b)=>new(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));
}
public struct Quaternion { public float x,y,z,w; public static float Angle(Quaternion a,Quaternion b)=>a.w==b.w?0:1; }
public struct Bounds { public Vector3 min,max; public void SetMinMax(Vector3 a,Vector3 b){min=a;max=b;} }
public class Transform { public Vector3 position,right=new(1,0,0),up=new(0,1,0),forward=new(0,0,1); public Quaternion rotation; }
public class GameObject { public bool activeInHierarchy=true; }
public class Renderer { public bool enabled=true,Dead; public GameObject gameObject=new(); public Bounds bounds; public static bool operator!(Renderer r)=>r.Dead; }
public class Component {
    public IntPtr Pointer; public Component(IntPtr p){Pointer=p;}
    public T[] GetComponentsInChildren<T>() { FieldZoomTest.Check.Scans++; return (T[])(object)FieldZoomTest.RuntimeObject.Objects[Pointer].Renderers; }
}
public class Camera:Component {
    public Camera(IntPtr p):base(p){}
    FieldZoomTest.RuntimeObject State=>FieldZoomTest.RuntimeObject.Objects[Pointer];
    public bool enabled=>State.Get<bool>("enabled"); public bool orthographic=>State.Get<bool>("orthographic");
    public bool usePhysicalProperties=>State.Get<bool>("usePhysicalProperties"); public Vector2 lensShift=>State.Get<Vector2>("lensShift");
    public GameObject gameObject=>State.CameraGame; public Transform transform=>State.CameraTransform;
    public float fieldOfView=>State.Get<float>("fieldOfView"); public float aspect=>State.Get<float>("aspect");
    public float nearClipPlane=>State.Get<float>("nearClipPlane"); public float farClipPlane=>State.Get<float>("farClipPlane");
}
public static class Input { public static Vector2 mouseScrollDelta; public static Vector3 mousePosition; public static int touchCount; public static bool[] Buttons=new bool[3],Down=new bool[3],Up=new bool[3]; public static bool GetMouseButton(int n)=>Buttons[n]; public static bool GetMouseButtonDown(int n)=>Down[n]; public static bool GetMouseButtonUp(int n)=>Up[n]; }
public static class Application { public static bool isFocused=true; }
public static class Time { public static float unscaledTime,unscaledDeltaTime=1f/60,deltaTime=1f/60; public static int frameCount; }
public static class Screen { public static int height=1080; }
}
namespace FieldPointerTest {
sealed class MappingFailure:Exception {}
sealed class HitFailure:Exception {}
sealed class RuntimeObject {
    public RuntimeObject? Parent,TouchTarget;
    public static RuntimeObject Stage=new(),ActualHit=new();
    public static int Hits,Maps,CacheClears; public static bool FailMap,FailHit;
    public RuntimeObject? Get(string key)=>key switch {"parent"=>Parent,"touchTarget"=>TouchTarget,_=>throw new Exception("Unsupported pointer reader")};
    public static string FindClass(string ns,string name)=>ns+"."+name;
    public static RuntimeObject? StaticCall(string type,string method){
        if(type=="FairyGUI.Stage"&&method=="get_inst")return Stage;
        if(type=="FairyGUI.HitTestContext"&&method=="ClearRaycastHitCache"){CacheClears++;return null;}
        throw new Exception("Unsupported pointer static call");
    }
    public RuntimeObject? Call(string method,params object?[] args){
        if(method=="HitTest"){
            if(this!=Stage||args.Length!=2||args[0] is not Vector2 point||point.x!=31||point.y!=996||args[1] is not true)throw new Exception("Wrong native HitTest arguments");
            Hits++;if(FailHit)throw new HitFailure();return ActualHit;
        }
        if(method=="DisplayObjectToGObject"){
            if(args.Length!=1||args[0]!=ActualHit)throw new Exception("Wrong hit mapping");
            Maps++;if(FailMap)throw new MappingFailure();return ActualHit;
        }
        throw new Exception("Unsupported pointer call");
    }
}
public static class Check {
    static RuntimeObject? _root;
    static readonly List<RuntimeObject> HitPath=new(); static int _hitFrame=-1;
    static void Assert(bool value,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){if(!value)throw new Exception("Production pointer regression at fixture line "+line);}
__POINTER_PATH__
    public static void Run(){
        Input.Down=new bool[3];Input.Up=new bool[3];Input.mousePosition=new(31,84,0);Time.frameCount=1;
        var cached=new RuntimeObject();var fresh=new RuntimeObject();_root=new(){TouchTarget=cached};
        RuntimeObject.Hits=RuntimeObject.Maps=RuntimeObject.CacheClears=0;
        Assert(PointerPath()[0]==cached&&RuntimeObject.Hits==0);
        _root.TouchTarget=new RuntimeObject();Assert(PointerPath()[0]==cached&&RuntimeObject.Hits==0);
        cached=_root.TouchTarget;Assert(PointerPath(refresh:true)[0]==cached&&RuntimeObject.Hits==0);
        RuntimeObject.ActualHit=fresh;Assert(PointerPath(hitTest:true)[0]==fresh&&RuntimeObject.Hits==1&&RuntimeObject.CacheClears==1);
        fresh=new RuntimeObject();RuntimeObject.ActualHit=fresh;
        Assert(PointerPath(refresh:true,hitTest:true)[0]==fresh&&RuntimeObject.Hits==2&&RuntimeObject.Maps==2&&RuntimeObject.CacheClears==2);
        Time.frameCount++;Assert(PointerPath(refresh:true)[0]==cached&&RuntimeObject.Hits==2);
        foreach(var button in new[]{0,1,2}){
            Input.Down[button]=true;Assert(PointerPath(refresh:true)[0]==fresh);Input.Down[button]=false;
        }
        Input.Up[0]=true;Assert(PointerPath(refresh:true)[0]==fresh);Input.Up[0]=false;
        Assert(RuntimeObject.Hits==6&&RuntimeObject.Maps==6&&RuntimeObject.CacheClears==6);
        RuntimeObject.FailMap=true;
        try{PointerPath(hitTest:true);throw new Exception("Mapping failure must escape");}catch(MappingFailure){}
        Assert(RuntimeObject.CacheClears==7);RuntimeObject.FailMap=false;RuntimeObject.FailHit=true;
        try{PointerPath(hitTest:true);throw new Exception("Hit failure must escape");}catch(HitFailure){}
        Assert(RuntimeObject.CacheClears==8);RuntimeObject.FailHit=false;
        fresh.Parent=fresh;Assert(PointerPath(hitTest:true).Count==64&&RuntimeObject.CacheClears==9);fresh.Parent=null;
        _root=null;Assert(PointerPath(hitTest:true).Count==0&&RuntimeObject.Hits==9&&RuntimeObject.CacheClears==9);
    }
}
}
namespace FieldZoomTest {
sealed class RuntimeObject {
    static int next; public IntPtr Pointer=(IntPtr)(++next); public string TypeName="Object"; public bool Dead;
    public Dictionary<string,object> Data=new(); public Renderer[] Renderers=Array.Empty<Renderer>();
    public Transform CameraTransform=new(); public GameObject CameraGame=new(); public int Writes;
    public RuntimeObject[] Items=Array.Empty<RuntimeObject>(); int index=-1; public RuntimeObject? IteratorOwner; public int Disposals;
    public static Dictionary<IntPtr,RuntimeObject> Objects=new(), Statics=new();
    static Dictionary<string,IntPtr> Classes=new(); public static bool OnUi,Dragging;
    public RuntimeObject(){Objects[Pointer]=this;}
    public RuntimeObject? Get(string key)=>key=="CamB"?throw new Exception("CamB is a native field, not getter"):key=="Current"?Items[index]:Data.GetValueOrDefault(key) as RuntimeObject;
    public T Get<T>(string key)=>key is "Duration" or "TimeInBlend"?throw new Exception("Native blend times are fields, not getters"):key=="draggable"?(T)(object)(Data.GetValueOrDefault(key) is true):(T)Data[key];
    public RuntimeObject? Field(string key)=>key=="CamB"?Data.GetValueOrDefault(key) as RuntimeObject:Get(key);
    public T Field<T>(string key)=>key is "Duration" or "TimeInBlend"?(T)Data[key]:Get<T>(key); public T Value<T>()=>Get<T>("value");
    public void SetField<T>(string key,T value) where T:unmanaged { if(key!="m_FollowOffset")throw new Exception("Forbidden field write"); if(Dead)throw new Exception("Disposed write"); Data[key]=value;Writes++; }
    static RuntimeObject Box(object value)=>new(){Data=new(){{"value",value}}};
    public static IntPtr FindClass(string ns,string name) { var key=ns+"."+name; if(!Classes.TryGetValue(key,out var p))Classes[key]=p=(IntPtr)(++next);return p; }
    public static RuntimeObject? StaticField(IntPtr type,string key){
        if(key!=(type==FindClass("Core.Scene","BattleSceneController")?"inst":"_inst"))throw new Exception("Wrong singleton field "+key);
        return Statics.GetValueOrDefault(type);
    }
    public static RuntimeObject? StaticCall(IntPtr type,string method,params object?[] args)=>method switch {
        "op_Implicit"=>Box(!((RuntimeObject)args[0]!).Dead), "get_isTouchOnUI"=>Box(OnUi),
        "get_draggingObject"=>Dragging?new RuntimeObject():null, _=>throw new Exception("Unsupported static reader "+method) };
    public RuntimeObject? Call(string method,params object[] args)=>method switch {
        "GetCurPlayerCamera"=>Get("current"), "GetCinemachineComponent"=>Get((int)args[0]==0?"body":"aim"),
        "get_Item"=>args.Length==1&&args[0] is int at&&at>=0&&at<Items.Length?Items[at]:throw new Exception("Wrong native frame index ABI"),
        "GetEnumerator"=>new RuntimeObject{Items=Items,IteratorOwner=this}, "MoveNext"=>Box(++index<Items.Length), "Dispose"=>DisposeIterator(),
        _=>throw new Exception("Unsupported native reader "+method) };
    RuntimeObject? DisposeIterator(){if(IteratorOwner!=null)IteratorOwner.Disposals++;return null;}
}
static class GameUi {
    public static RuntimeObject? Root; public static bool FightWindowPresent,EncounterHidden; public static string? SettingsWindow;
    public static RuntimeObject? LiveDrag()=>RuntimeObject.Dragging?new RuntimeObject():null;
    public static List<RuntimeObject> Path=new(),CachedPath=new(); public static int FreshReads,CachedReads;
    public static IReadOnlyList<RuntimeObject> PointerPath(bool refresh=false,bool hitTest=false){if(!refresh)throw new Exception("Field gesture requires fresh hit");if(!hitTest){CachedReads++;return CachedPath;}FreshReads++;return Path;}
    public static RuntimeObject? Find(RuntimeObject root,string type,int depth=0,int maxDepth=4)=>maxDepth==1&&type is "FightWindow" or "SettingWindow" or "SettingInBattleWindow" or "SinglePlayerSettingInBattleWindow" or "SettingListWindow"
        ?(type=="FightWindow"?FightWindowPresent?Fight():null:SettingsWindow==type?new():null):throw new Exception("Unexpected window query");
    static RuntimeObject Fight(){var r=new RuntimeObject();var launch=new RuntimeObject();var tip=new RuntimeObject();tip.Data["visible"]=EncounterHidden;launch.Data["txt_HideTip"]=tip;var pane=new RuntimeObject();pane.Data["com_LaunchPK"]=launch;r.Data["contentPane"]=pane;return r;}
    public static bool Visible(RuntimeObject? window)=>window!=null&&!window.Dead&&window.Data.GetValueOrDefault("visible") is not false&&window.Data.GetValueOrDefault("onStage") is not false;
}
static class PvpSafety { public static bool Suspended; }
static class ModUi { public static bool IsOpen; }
static class FieldCameraReturnUi {
    public static bool Available,Visible,ConsumedInput;
    public static void Tick(bool visible)=>Visible=visible&&Available;
    public static void Clear(){Visible=false;ConsumedInput=false;}
}
static class FieldMonsterPlates { public static int Ticks, Clears; public static float LastScale=1f; internal static void Tick(float scale){Ticks++;LastScale=scale;} internal static void Clear(){Clears++;} }
static class FieldStatePlates { internal static void Tick(float scale){} internal static void Clear(){} }
static class FieldIndicatorOpacity {
    public static float Progress;
    public static bool Dimmed=>Progress>0;
    public static void Tick(float progress){if(!float.IsFinite(progress)||progress<0||progress>1)throw new Exception("Invalid normalized field opacity progress");Progress=progress;}
    public static void Clear()=>Progress=0;
}
static class FieldFreeCamera {
    public static bool Holding; public static RuntimeObject? Camera; public static int ClearAttempts,ClearFailures;
    public static bool LastBlocked,PanAllowed,PanOnUi,PanIndicator,Yield; public static int Enters;
    public static bool Update(RuntimeObject scene,RuntimeObject brain,RuntimeObject? free,RuntimeObject? player,bool blocked){LastBlocked=blocked;if(blocked)PanAllowed=false;return Holding&&!blocked&&!Yield;}
    public static void Pan(RuntimeObject free,bool allowed,bool onUi,bool fieldIndicator=false){PanAllowed=allowed;PanOnUi=onUi;PanIndicator=fieldIndicator;}
    public static void Enter(RuntimeObject scene,RuntimeObject brain,RuntimeObject active,RuntimeObject body,Camera output){
        Enters++;
        var ownedBody=new RuntimeObject{TypeName="CinemachineTransposer",Data=new(body.Data)};
        var anchor=new RuntimeObject{Data=new(){{"position",output.transform.position-body.Field<Vector3>("m_FollowOffset")}}};
        Camera=new RuntimeObject{TypeName="CinemachineVirtualCamera",Data=new(){
            {"body",ownedBody},{"Follow",anchor},{"transform",active.Get("transform")!}}};Holding=true;
    }
    public static void Clear(){ClearAttempts++;if(ClearFailures>0){ClearFailures--;throw new CleanupFailure();}Holding=false;Camera=null;}
}
static class VisibleCombatAdvisor { public static bool IsPve(int map)=>map==2; }
static class Plugin { public static Setting FieldZoom=new(); public static readonly FloatSetting ZoomSensitivity=new(), ZoomOutLimit=new(); public static readonly Diagnostics Diagnostics=new(); public sealed class Setting {public bool Value=true;} public sealed class FloatSetting {public float Value=1f;} }
sealed class Diagnostics {
    public readonly List<string> States=new();
    public bool IsRecording; public readonly List<string> Geometry=new();
    public void State(string key,string value){if(key=="fieldZoom.geometry"){if(!IsRecording)throw new Exception("Geometry recorded while diagnostics off");Geometry.Add(value);return;}if(key!="fieldZoom.wheel")throw new Exception("Unexpected diagnostic key");States.Add(value);}
}
sealed class CleanupFailure:Exception {}
static class Compatibility {
    public static bool Blocked; public static int Blocks,CleanupFailures;
    public static bool Allowed(string key)=>key!="FieldZoom"||!Blocked;
    public static void Reset(){Blocked=false;Blocks=CleanupFailures=0;}
    public static void Block(string key,Exception ex,Action clear){
        if(key!="FieldZoom"||ex is not CleanupFailure)throw ex;
        if(Blocked)return;
        Blocked=true;Blocks++;
        try{clear();}catch(CleanupFailure){CleanupFailures++;}
    }
}
__GEOMETRY__
__SOURCE__
public unsafe class Vector3Bridge {
    static readonly HashSet<(IntPtr,Type)> CheckedValues=new();
    static readonly IntPtr Storage=System.Runtime.InteropServices.Marshal.AllocHGlobal(12);
    static string NativeName=typeof(Vector3).FullName!; static int NativeSize=12, Reads,Writes; static bool NativeValue=true;
    IntPtr Class=>(IntPtr)1; IntPtr Pointer=>(IntPtr)2;
    static IntPtr FieldInfo(IntPtr c,string name)=>c==(IntPtr)1&&name=="m_FollowOffset"?(IntPtr)3:throw new MissingFieldException();
    static class IL2CPP {
        internal static IntPtr il2cpp_field_get_type(IntPtr f)=>(IntPtr)4;
        internal static IntPtr il2cpp_class_from_type(IntPtr t)=>t;
        internal static bool il2cpp_class_is_valuetype(IntPtr c)=>NativeValue;
        internal static bool il2cpp_class_is_enum(IntPtr c)=>false;
        internal static IntPtr il2cpp_class_enum_basetype(IntPtr c)=>throw new Exception("Unexpected enum");
        internal static IntPtr il2cpp_class_get_type(IntPtr c)=>c;
        internal static string il2cpp_type_get_name_(IntPtr t)=>NativeName;
        internal static int il2cpp_class_value_size(IntPtr c,ref uint alignment)=>NativeSize;
        internal static void il2cpp_field_get_value(IntPtr p,IntPtr f,void* value){if(p!=(IntPtr)2||f!=(IntPtr)3)throw new Exception("Wrong native field instance");Reads++;Buffer.MemoryCopy((void*)Storage,value,12,12);}
        internal static void il2cpp_field_set_value(IntPtr p,IntPtr f,void* value){if(p!=(IntPtr)2||f!=(IntPtr)3)throw new Exception("Wrong native field instance");Writes++;Buffer.MemoryCopy(value,(void*)Storage,12,12);}
    }
__VALUE_CHECK__
__FIELD_GET__
__FIELD_SET__
    public static void Run(){
        try {
            var probe=new Vector3Bridge();var expected=new Vector3(65,104.5f,-65);
            probe.SetField("m_FollowOffset",expected);var actual=probe.Field<Vector3>("m_FollowOffset");
            if(actual.x!=expected.x||actual.y!=expected.y||actual.z!=expected.z)throw new Exception("Production Vector3 field round-trip failed");
            foreach(var invalid in new Action[]{()=>NativeName="UnityEngine.Vector2",()=>NativeSize=16,()=>NativeValue=false}) {
                CheckedValues.Clear();NativeName=typeof(Vector3).FullName!;NativeSize=12;NativeValue=true;invalid();int calls=Reads+Writes;
                try{probe.SetField("m_FollowOffset",expected);throw new Exception("Invalid native write accepted");}catch(InvalidCastException){}
                try{probe.Field<Vector3>("m_FollowOffset");throw new Exception("Invalid native read accepted");}catch(InvalidCastException){}
                if(calls!=Reads+Writes)throw new Exception("Validation must precede native field access");
            }
        } finally {System.Runtime.InteropServices.Marshal.FreeHGlobal(Storage);}
    }
}
public static class Check {
    public static int Scans,Assertions;
    static void Assert(bool value,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){Assertions++;if(!value)throw new Exception("Field zoom regression at fixture line "+line);}
    static RuntimeObject Obj(params (string Key,object Value)[] pairs){var r=new RuntimeObject();foreach(var p in pairs)r.Data[p.Key]=p.Value;return r;}
    sealed class Scene {
        public RuntimeObject Root=Obj(("hasModalWindow",false),("modalWaiting",false),("hasAnyPopup",false),("touchTarget",Obj(("touchable",true))));
        public RuntimeObject Body=Obj(("m_FollowOffset",new Vector3(0,0,-10)),("m_BindingMode",Obj(("value",4))),("IsValid",true));
        public RuntimeObject Follow=Obj(("position",new Vector3()));
        public RuntimeObject Camera=Obj(("enabled",true),("orthographic",false),("usePhysicalProperties",false),("lensShift",new Vector2()),
            ("fieldOfView",90f),("aspect",16f/9),("nearClipPlane",.1f),("farClipPlane",1000f));
        public RuntimeObject Active,Brain,Frames,Blend,Free=Obj(("status",Obj(("value",0)))),SceneObject,Land=new();
        public RuntimeObject WindowStack=Obj(("Count",0));
        public RuntimeObject Attributes=new(),PlayerAttributes=new(),BattleUi;
        public Scene(bool clear=true){
            if(clear)FieldZoomUi.Clear(); Compatibility.Reset(); GameUi.FightWindowPresent=GameUi.EncounterHidden=false;GameUi.SettingsWindow=null; PvpSafety.Suspended=false; ModUi.IsOpen=false; Plugin.FieldZoom.Value=true;
            Input.Buttons=new bool[3];Input.Down=new bool[3];Input.Up=new bool[3];Input.touchCount=0;Input.mouseScrollDelta=new();Application.isFocused=true;RuntimeObject.OnUi=false;RuntimeObject.Dragging=false;Time.unscaledTime=0;Time.unscaledDeltaTime=1f/60;
            GameUi.Path.Clear();GameUi.CachedPath.Clear();GameUi.FreshReads=GameUi.CachedReads=0;
            Plugin.Diagnostics.States.Clear();Plugin.Diagnostics.Geometry.Clear();Plugin.Diagnostics.IsRecording=false;
            FieldFreeCamera.LastBlocked=FieldFreeCamera.PanAllowed=FieldFreeCamera.PanOnUi=FieldFreeCamera.PanIndicator=FieldFreeCamera.Yield=false;
            Active=Obj(("body",Body),("Follow",Follow),("transform",Obj(("rotation",new Quaternion()))));Active.TypeName="CinemachineVirtualCamera";
            Blend=Obj(("IsComplete",true),("CamB",Active),("Duration",0f),("TimeInBlend",0f));Frames=Obj(("Count",1));Frames.Items=new[]{Obj(("blend",Blend))};
            Brain=Obj(("IsBlending",false),("ActiveVirtualCamera",Active),("mFrameStack",Frames));Body.TypeName="CinemachineTransposer";
            SceneObject=Obj(("cinemachineBrain",Brain),("freeObject",Free),("freeCamera",Active),("mainCamera",Camera));
            RuntimeObject.Statics[RuntimeObject.FindClass("Core.Scene","BattleSceneController")]=SceneObject;
            var room=Obj(("info",Obj(("MapType",2))));
            BattleUi=Obj(("com_AttrInfos",Attributes),("com_PlayerAttrInfos",PlayerAttributes));
            RuntimeObject.Statics[RuntimeObject.FindClass("GameLogic","GameLogicManager")]=Obj(("room",Obj(("curRoomInfo",room))),
                ("fight",Obj(("fightStatus",false))),("battle",Obj(("clientFinishReady",true),("battleInfo",Obj(("ui",BattleUi))))));
            RuntimeObject.Statics[RuntimeObject.FindClass("Core.Camera","CameraManager")]=Obj(("current",Obj(("vCamera",Active))));
            RuntimeObject.Statics[RuntimeObject.FindClass("UI","UIManager")]=Obj(("propUpWindows",WindowStack));
            Land.Renderers=new[]{new Renderer{bounds=new Bounds{min=new(-40,-20,0),max=new(40,20,0)}}};
            RuntimeObject.Statics[RuntimeObject.FindClass("Core.Unit","LandManager")]=Obj(("NodeDict",Obj(("Count",1),("Values",new RuntimeObject{Items=new[]{Land}}))));
            GameUi.Root=Root; Settle();
        }
        public Vector3 Offset=>Body.Field<Vector3>("m_FollowOffset");
        public void Windows(params RuntimeObject[] items){WindowStack.Items=items;WindowStack.Data["Count"]=items.Length;}
        public void Settle(){Camera.CameraTransform.position=Offset;}
        public void Wheel(float value){Input.mouseScrollDelta=new(0,value);FieldZoomUi.Tick();Input.mouseScrollDelta=new();}
        public void Finish(){for(var i=0;i<120;i++){Time.unscaledTime+=Time.unscaledDeltaTime;FieldZoomUi.Tick();}Settle();}
    }
    static void Geometry(){
        FieldZoomGeometry.Point P(double x,double y,double z)=>new(x,y,z);
        var points=new[]{P(-40,-20,10),P(40,20,10)};
        bool Step(float wheel,float native,out float s)=>FieldZoomGeometry.TryStep(points,P(0,0,-10),P(0,0,10),90,16f/9,.1f,1000,native,wheel,out s,out _);
        Assert(Step(-1,1,out var s)&&s>1&&s<4.5);
        // Max Zoom-Out: a share of the travel from the native distance to the field-fit stop.
        Assert(FieldZoomGeometry.CappedLimit(10,50,1)==50&&FieldZoomGeometry.CappedLimit(10,50,.5f)==30&&FieldZoomGeometry.CappedLimit(10,50,.2f)==18&&FieldZoomGeometry.CappedLimit(10,50,0)==10&&FieldZoomGeometry.CappedLimit(10,0,.5f)==0&&FieldZoomGeometry.CappedLimit(10,8,.5f)==8);
        Assert(Step(-100,1,out s)&&Math.Abs(s-4.5)<.00001);
        Assert(Step(100,1,out s)&&s==.5f);
        Assert(!Step(-100,float.NaN,out _));
        Assert(!FieldZoomGeometry.TryStep(points,P(0,0,10),P(0,0,10),90,16f/9,.1f,1000,1,-1,out _,out _));
        Assert(FieldZoomGeometry.TryStep(points,P(0,0,-10),P(0,0,10),90,16f/9,.1f,15,1,-100,out s,out var unavailable)&&s==1.5f&&unavailable==s);
        Assert(!FieldZoomGeometry.TryStep(new[]{P(double.NaN,0,10)},P(0,0,-10),P(0,0,10),90,16f/9,.1f,1000,1,-1,out _,out _));
        Assert(FieldZoomGeometry.TryStep(points,P(0,0,-10),P(0,0,10),90,4,.1f,1000,1,-100,out s,out var maximum)&&Math.Abs(s-4.5)<.00001&&maximum==s);
        Assert(FieldZoomGeometry.TryStep(points,P(0,0,-10),P(0,0,10),90,1,.1f,1000,1,-100,out s,out maximum)&&s>=8&&s<8.00001&&maximum==s);
        Assert(FieldZoomGeometry.TryStep(new[]{P(0,-256,128),P(0,256,128)},P(0,0,-128),P(0,0,128),90,16f/9,.3f,1500,1,-100,out s,out maximum)&&s>4&&maximum==s);
        // Panning changes neither the field-centred stop nor the camera direction.
        Assert(Step(-100,1,out var full));
        foreach(var x in new[]{-200d,0d,200d})foreach(var y in new[]{-50d,0d,50d})foreach(var z in new[]{0d,12.5d,40d}){
            var moved=points.Select(p=>P(p.X+x,p.Y+y,p.Z+z)).ToArray();
            Assert(FieldZoomGeometry.TryStep(moved,P(0,0,-10),P(0,0,10),90,16f/9,.1f,1000,1,-100,out s,out maximum)&&s==full&&maximum==full);
        }
        // Near protects Follow rather than rejecting large fields outside the current close-up frustum.
        Assert(FieldZoomGeometry.TryStep(new[]{P(-40,0,-2),P(40,0,10)},P(0,0,-10),P(0,0,10),90,16f/9,.1f,1000,1,1,out s,out _)&&s<1);
        // An impossible whole-field stop must not reject a near/far-safe inward step.
        var wide=new[]{P(-100,-1,10),P(100,1,10)};
        foreach(var wheel in new[]{1f,100f}){
            Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,50,1,wheel,out s,out maximum));
            Assert(Math.Abs(s-(wheel==1?.9f:.5f))<.000001&&maximum==5&&10*s>=1&&10*s<=50);
        }
        Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,50,1,-1,out s,out maximum)&&s>1&&maximum==5);
        // An impossible whole-field stop still allows safe OUT, including beyond the native baseline.
        Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,50,2,-1,out s,out maximum)&&s>1&&s<2&&maximum==5);
        Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,50,2,-100,out s,out maximum)&&s==5&&maximum==s);
        Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,50,.5f,-100,out s,out maximum)&&s==5&&maximum==s);
        Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,15,2,-100,out s,out maximum)&&s==1.5f&&maximum==s&&10*s<=15);
        Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,10.1f,2,-100,out s,out maximum)&&s>1&&maximum==s&&10d*s<=10.1f);
        Assert(!FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,10,2,-1,out _,out maximum)&&maximum<=1);
        foreach(var calibration in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
            Assert(!FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,1,50,calibration,-1,out _,out maximum)&&maximum==0);
        Assert(FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,10),60,1,9.5f,50,1,100,out s,out maximum)&&s>=.95f&&s<.950001f&&maximum==5&&10*s>=9.5f);
        foreach(var depth in new[]{.5d,51d})Assert(!FieldZoomGeometry.TryStep(wide,P(0,0,-10),P(0,0,depth),60,1,1,50,1,1,out _,out maximum)&&maximum==0);
        Assert(!FieldZoomGeometry.TryStep(new[]{P(double.NaN,0,10)},P(0,0,-10),P(0,0,10),60,1,1,50,1,1,out _,out maximum)&&maximum==0);
        Assert(!FieldZoomGeometry.TryStep(Array.Empty<FieldZoomGeometry.Point>(),P(0,0,-10),P(0,0,10),60,1,1,50,1,1,out _,out maximum)&&maximum==0);
        Assert(FieldZoomGeometry.TryStep(points,P(0,0,-10),P(0,0,10),90,16f/9,.1f,1000,1,1,out s,out maximum)&&s<1&&Math.Abs(maximum-4.5)<.00001);
        // The first representable stop misses by 1e-13; only the next exact-fit float may be published.
        var boundaryDistance=22.766314f;
        var boundaryWidth=Math.Tan(146.14227f*Math.PI/360)*boundaryDistance*7.441292f/2;
        var boundary=new[]{P(-boundaryWidth,0,boundaryDistance),P(boundaryWidth,0,boundaryDistance)};
        Assert(FieldZoomGeometry.TryStep(boundary,P(0,0,-boundaryDistance),P(0,0,boundaryDistance),146.14227f,1,.01f,100000,1,1,out s,out maximum)&&s==.9f&&maximum==MathF.BitIncrement(7.441292f));
        Assert(FieldZoomGeometry.TryStep(boundary,P(0,0,-boundaryDistance),P(0,0,boundaryDistance),146.14227f,1,.01f,100000,1,-100,out s,out maximum)&&s==maximum);
        Assert(2*boundaryWidth<=Math.Tan(146.14227f*Math.PI/360)*boundaryDistance*s&&boundaryDistance*s<=100000);
        var boundaryFar=boundaryDistance*7.441292f;
        Assert(FieldZoomGeometry.TryStep(boundary,P(0,0,-boundaryDistance),P(0,0,boundaryDistance),146.14227f,1,.01f,boundaryFar,1,-100,out s,out maximum)&&maximum==s&&s<MathF.BitIncrement(7.441292f)&&boundaryDistance*(double)s<=boundaryFar);
        // Reconstructed logged field spans and offsets; the live log does not expose the corner centre.
        foreach(var row in new[]{(19.3692,-302.5237,438.8198,350.9582),(36.4465,-569.2518,439.6331,369.7135),(11.9114,-186.0427,439.6331,369.7135)}){
            var d=P(0,row.Item1,row.Item2);var f=P(0,-d.Y,-d.Z);
            var ground=new[]{P(-row.Item3/2,-row.Item4/2-d.Y,-row.Item4/2-d.Z),P(row.Item3/2,row.Item4/2-d.Y,row.Item4/2-d.Z)};
            var native=(float)(139.177/Math.Sqrt(d.Y*d.Y+d.Z*d.Z));
            Assert(FieldZoomGeometry.TryStep(ground,d,f,45,16f/9,.3f,1500,native,-1,out s,out maximum)&&s>1&&maximum>s);
            Assert(FieldZoomGeometry.TryStep(ground,d,f,45,16f/9,.3f,1500,native,-100,out var cap,out maximum)&&cap>s&&maximum==cap);
            foreach(var p in ground){Assert(p.Z-(s-1d)*d.Z>=.3f&&p.Z-(cap-1d)*d.Z<=1500);}
            // The cap is the board's far plane, not an arbitrary larger range or a whole-field proof.
            Assert(ground.Max(p=>p.Z-(cap-1d)*d.Z)<=1500&&1500-ground.Max(p=>p.Z-(cap-1d)*d.Z)<.001);
            var atCap=ground.Select(p=>P(p.X,p.Y,p.Z-(cap-1d)*d.Z)).ToArray();
            Assert(!FieldZoomGeometry.TryStep(atCap,P(d.X*cap,d.Y*cap,d.Z*cap),P(f.X,f.Y,f.Z-(cap-1d)*d.Z),45,16f/9,.3f,1500,native/cap,-1,out _,out maximum)&&maximum<=1);
        }
    }
    public static int SafeInwardCases,OpacityProgressCases;
    static void BranchGeometryAndOpacity(){
        var available=FieldCameraReturnUi.Available;FieldCameraReturnUi.Available=true;
        foreach(var status in new[]{0,1,2,3})foreach(var held in new[]{false,true}){
            var s=new Scene();if(held){s.Wheel(1);s.Finish();}
            s.Free.Data["status"]=Obj(("value",status));
            s.Camera.Data["fieldOfView"]=60f;s.Camera.Data["aspect"]=1f;s.Camera.Data["nearClipPlane"]=1f;s.Camera.Data["farClipPlane"]=50f;
            s.Land.Renderers[0].bounds=new(){min=new(-100,-1,0),max=new(100,1,0)};
            var before=(held?FieldFreeCamera.Camera!.Get("body")!:s.Body).Field<Vector3>("m_FollowOffset").magnitude;
            Plugin.Diagnostics.States.Clear();s.Wheel(1);
            Assert(FieldFreeCamera.Holding&&Plugin.Diagnostics.States.Contains("accepted")&&!FieldFreeCamera.LastBlocked);
            var body=FieldFreeCamera.Camera!.Get("body")!;s.Finish();
            Assert(Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-before*.9f)<.00001&&s.Body.Writes==0);
            Assert(FieldIndicatorOpacity.Progress==0&&FieldFreeCamera.PanAllowed);
            var stopped=body.Field<Vector3>("m_FollowOffset");Plugin.Diagnostics.States.Clear();s.Wheel(-1);s.Finish();
            Assert(Plugin.Diagnostics.States.Contains("accepted")&&Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-stopped.magnitude/.9f)<.0001&&FieldIndicatorOpacity.Progress<=.000001);
            s.Wheel(-100);s.Finish();Assert(Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-50)<.0001&&Math.Abs(FieldIndicatorOpacity.Progress-1)<.000001);
            var writes=body.Writes;stopped=body.Field<Vector3>("m_FollowOffset");Plugin.Diagnostics.States.Clear();s.Wheel(-1);s.Finish();
            Assert(Plugin.Diagnostics.States.Contains("geometry-limit")&&body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==stopped.z&&Math.Abs(FieldIndicatorOpacity.Progress-1)<.000001);
            SafeInwardCases++;
        }
        var near=new Scene();near.Camera.Data["fieldOfView"]=60f;near.Camera.Data["aspect"]=1f;
        near.Camera.Data["nearClipPlane"]=9.5f;near.Camera.Data["farClipPlane"]=50f;
        near.Land.Renderers[0].bounds=new(){min=new(-100,-1,0),max=new(100,1,0)};
        near.Wheel(100);near.Finish();var closeBody=FieldFreeCamera.Camera!.Get("body")!;
        Assert(closeBody.Field<Vector3>("m_FollowOffset").magnitude>=9.5f&&closeBody.Field<Vector3>("m_FollowOffset").magnitude<9.50001f&&FieldIndicatorOpacity.Progress==0);
        SafeInwardCases++;
        // Opacity follows the applied camera distance, not an unsmoothed requested stop.
        var scene=new Scene();scene.Wheel(-100);var owned=FieldFreeCamera.Camera!;var bodyAtStop=owned.Get("body")!;
        var applied=bodyAtStop.Field<Vector3>("m_FollowOffset").magnitude;
        Assert(FieldIndicatorOpacity.Progress>0&&FieldIndicatorOpacity.Progress<1&&Math.Abs(FieldIndicatorOpacity.Progress-(applied-10)/35)<.000001);
        var last=FieldIndicatorOpacity.Progress;
        for(var i=0;i<20;i++){FieldZoomUi.Tick();Assert(FieldIndicatorOpacity.Progress>=last&&FieldIndicatorOpacity.Progress<=1);last=FieldIndicatorOpacity.Progress;}
        scene.Finish();Assert(Math.Abs(FieldIndicatorOpacity.Progress-1)<.000001);OpacityProgressCases++;
        scene.Wheel(1);applied=bodyAtStop.Field<Vector3>("m_FollowOffset").magnitude;
        Assert(FieldIndicatorOpacity.Progress>0&&FieldIndicatorOpacity.Progress<1&&Math.Abs(FieldIndicatorOpacity.Progress-(applied-10)/35)<.000001);
        scene.Finish();Assert(Math.Abs(FieldIndicatorOpacity.Progress-(bodyAtStop.Field<Vector3>("m_FollowOffset").magnitude-10)/35)<.000001);OpacityProgressCases++;
        scene.Camera.Data["fieldOfView"]=60f;scene.Camera.Data["aspect"]=1f;scene.Camera.Data["nearClipPlane"]=1f;scene.Camera.Data["farClipPlane"]=50f;
        scene.Land.Renderers[0].bounds=new(){min=new(-100,-1,0),max=new(100,1,0)};
        scene.Wheel(1);applied=bodyAtStop.Field<Vector3>("m_FollowOffset").magnitude;
        Assert(applied>10&&Math.Abs(FieldIndicatorOpacity.Progress-(applied-10)/40)<.000001);
        scene.Finish();Assert(FieldIndicatorOpacity.Progress>0&&FieldIndicatorOpacity.Progress<1);OpacityProgressCases++;
        // A newly native/external baseline cannot inherit another pose's full-field opacity limit.
        bodyAtStop.Data["m_FollowOffset"]=new Vector3(0,0,-20);FieldZoomUi.Tick();Assert(FieldIndicatorOpacity.Progress==0);OpacityProgressCases++;
        FieldZoomUi.Clear();Assert(FieldIndicatorOpacity.Progress==0);
        scene=new Scene();scene.Wheel(-100);scene.Finish();Assert(FieldIndicatorOpacity.Progress==1);
        scene.Camera.Data["enabled"]=false;FieldZoomUi.Tick();Assert(FieldIndicatorOpacity.Progress==0);OpacityProgressCases++;
        FieldZoomUi.Clear();FieldCameraReturnUi.Available=available;
    }
    public static int IndicatorWheelCases,BlockedIndicatorWheelCases,InterruptedIndicatorCases;
    static RuntimeObject IndicatorPath(Scene scene,string kind="native-monster",string part="transparent"){
        var plate=new RuntimeObject{TypeName=kind=="native-player"?"UICom_PlayerAttrInfo":"UICom_AttrInfo"};
        var parent=kind=="native-player"?scene.PlayerAttributes:scene.Attributes;
        var leaf=new RuntimeObject{TypeName=part switch {"name"=>"GTextField","hp"=>"GRichTextField","icon"=>"UIButton_Buff",_=>"GGraph"}};
        if(part=="transparent")leaf.Data["alpha"]=0f;
        GameUi.Path.Clear();GameUi.Path.AddRange(new[]{leaf,plate,parent,scene.Root});
        GameUi.CachedPath.Clear();GameUi.CachedPath.AddRange(new[]{new RuntimeObject{TypeName="LandShopWindow"},scene.Root});
        return plate;
    }
    static void IndicatorWheel(){
        var available=FieldCameraReturnUi.Available;FieldCameraReturnUi.Available=true;
        foreach(var kind in new[]{"native-monster","native-player","mod-clone"})
        foreach(var part in new[]{"name","hp","icon","transparent"})
        foreach(var held in new[]{false,true})
        foreach(var direction in new[]{-1,1}){
            var scene=new Scene();if(held){scene.Wheel(1);scene.Finish();}
            var before=(FieldFreeCamera.Holding?FieldFreeCamera.Camera!.Get("body")!:scene.Body).Field<Vector3>("m_FollowOffset").magnitude;
            IndicatorPath(scene,kind,part);RuntimeObject.OnUi=true;
            var freshReads=GameUi.FreshReads;scene.Wheel(direction);
            var body=FieldFreeCamera.Camera!.Get("body")!;var first=body.Field<Vector3>("m_FollowOffset").magnitude;
            Assert((direction>0?first<before:first>before)&&GameUi.FreshReads==freshReads+1&&GameUi.CachedReads==0);
            Assert(!FieldFreeCamera.PanOnUi&&!FieldFreeCamera.PanIndicator);
            var writes=body.Writes;FieldZoomUi.Tick();
            Assert(body.Writes==writes+1&&GameUi.FreshReads==freshReads+2&&!FieldFreeCamera.PanIndicator);
            scene.Finish();var finished=body.Field<Vector3>("m_FollowOffset").magnitude;
            Assert(direction>0?finished<first:finished>first);
            freshReads=GameUi.FreshReads;writes=body.Writes;FieldZoomUi.Tick();
            Assert(GameUi.FreshReads==freshReads&&body.Writes==writes&&GameUi.CachedReads==0);
            IndicatorWheelCases++;
        }
        foreach(var direction in new[]{-1,1})
        foreach(var invalid in new Action<Scene>[] {
            a=>ScrollPath(a,true,true),
            a=>TextInputPath(a,true,true),
            a=>Application.isFocused=false,
            a=>ModUi.IsOpen=true,
            a=>Input.touchCount=1,
            a=>RuntimeObject.Dragging=true,
            a=>a.Root.Data["modalWaiting"]=true,
            a=>GameUi.FightWindowPresent=true,
            a=>GameUi.SettingsWindow="SettingWindow",a=>GameUi.SettingsWindow="SettingInBattleWindow",
            a=>GameUi.SettingsWindow="SinglePlayerSettingInBattleWindow",a=>GameUi.SettingsWindow="SettingListWindow",
            a=>FieldCameraReturnUi.ConsumedInput=true,
            a=>Input.Buttons[0]=true,a=>Input.Buttons[1]=true,a=>Input.Buttons[2]=true
        }){
            var scene=new Scene();scene.Wheel(1);scene.Finish();IndicatorPath(scene);
            GameUi.CachedPath.Clear();GameUi.CachedPath.AddRange(GameUi.Path);
            // Only fresh ancestry, not a stale cached field plate, can reserve native wheel input.
            GameUi.Path=new List<RuntimeObject>(GameUi.Path.Select(item=>item));invalid(scene);RuntimeObject.OnUi=true;
            var body=FieldFreeCamera.Camera!.Get("body")!;var writes=body.Writes;var before=body.Field<Vector3>("m_FollowOffset");
            scene.Wheel(direction);Assert(body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==before.z&&!FieldFreeCamera.PanIndicator&&GameUi.CachedReads==0);
            FieldCameraReturnUi.ConsumedInput=false;BlockedIndicatorWheelCases++;
        }
        foreach(var interrupt in new Action<Scene>[] {
            a=>ScrollPath(a,true,true),a=>TextInputPath(a,true,true),
            a=>a.Root.Data["modalWaiting"]=true,a=>FieldCameraReturnUi.ConsumedInput=true
        }){
            var scene=new Scene();IndicatorPath(scene);RuntimeObject.OnUi=true;scene.Wheel(-1);
            var body=FieldFreeCamera.Camera!.Get("body")!;var writes=body.Writes;var stopped=body.Field<Vector3>("m_FollowOffset").z;
            interrupt(scene);FieldZoomUi.Tick();Assert(body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==stopped);
            GameUi.Path.Clear();scene.Root.Data["modalWaiting"]=false;FieldCameraReturnUi.ConsumedInput=false;
            RuntimeObject.OnUi=false;scene.Finish();Assert(body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==stopped);
            InterruptedIndicatorCases++;
        }
        FieldZoomUi.Clear();FieldCameraReturnUi.Available=available;
    }
    static void ScrollPath(Scene scene,bool touch,bool wheel,int ancestor=1){
        var pane=Obj(("touchEffect",touch),("mouseWheelEnabled",wheel));
        var component=Obj(("scrollPane",pane));var owner=Obj(("asCom",component));
        GameUi.Path.Clear();GameUi.Path.Add(new RuntimeObject{TypeName="GTextField"});
        GameUi.Path.Insert(ancestor==0?0:1,owner);GameUi.Path.Add(scene.Root);
    }
    static void TextInputPath(Scene scene,bool focused,bool wheel){
        var input=Obj(("focused",focused),("mouseWheelEnabled",wheel));
        GameUi.Path.Clear();GameUi.Path.Add(Obj(("asTextInput",input)));GameUi.Path.Add(scene.Root);
    }
    public static int NonScrollUiCases,ConsumerCases,PresentationCases,FrameCases;
    public static int HiddenEncounterCases;
    static void HiddenEncounter(){
        foreach(var hidden in new[]{false,true}){
            var s=new Scene();s.Wheel(1);s.Finish();GameUi.FightWindowPresent=true;GameUi.EncounterHidden=hidden;
            var body=FieldFreeCamera.Camera!.Get("body")!;var before=body.Field<Vector3>("m_FollowOffset");
            s.Wheel(1);s.Finish();Assert((body.Field<Vector3>("m_FollowOffset").magnitude<before.magnitude)==hidden);
            Input.Buttons[0]=true;RuntimeObject.OnUi=true;FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed==hidden);Input.Buttons[0]=false;
            GameUi.FightWindowPresent=GameUi.EncounterHidden=false;HiddenEncounterCases++;
        }
    }
    public static int PanUiCases;
    static void PanUi(){
        FieldCameraReturnUi.Available=true;
        foreach(var type in new[]{"LandShopWindow","CardWindow","SelectMonsterWindow","ChooseRoundCardWindow","BattlePlayerInfoWindow","Popup","UnknownWindow","WorldOverlay"})
        foreach(var local in new[]{false,true})foreach(var status in new[]{0,1,2,3})foreach(var button in new[]{false,true}){
            var s=new Scene();s.Wheel(1);s.Finish();var own=FieldFreeCamera.Camera;
            s.Windows(new RuntimeObject{TypeName=type});s.Free.Data["status"]=Obj(("value",status));
            s.Root.Data["hasModalWindow"]=s.Root.Data["hasAnyPopup"]=true;
            if(!local)s.Windows(new RuntimeObject{TypeName="TipsWindow"},new RuntimeObject{TypeName=type});
            RuntimeObject.OnUi=true;Input.Buttons[0]=button;Input.Down[0]=false;
            GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName=type},s.Root});
            FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.PanOnUi==button&&!FieldFreeCamera.PanIndicator&&FieldFreeCamera.Camera==own);
            s.Windows();s.Root.Data["hasModalWindow"]=s.Root.Data["hasAnyPopup"]=false;FieldZoomUi.Tick();
            Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.Camera==own);PanUiCases++;
        }
        // Pointer-origin scrolling wins over Pan even when wheel scrolling is disabled.
        foreach(var touch in new[]{false,true})foreach(var wheel in new[]{false,true})foreach(var ancestor in new[]{0,1}){
            var s=new Scene();s.Wheel(1);s.Finish();ScrollPath(s,touch,wheel,ancestor);
            RuntimeObject.OnUi=true;Input.Buttons[0]=true;FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed==!touch);
            Input.Buttons[0]=false;FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed);PanUiCases++;
        }
        // Keyboard focus belongs to text input regardless of the pointer or its wheel preference.
        foreach(var focused in new[]{false,true})foreach(var wheel in new[]{false,true})foreach(var button in new[]{false,true}){
            var s=new Scene();s.Wheel(1);s.Finish();
            s.Root.Data["focus"]=Obj(("asTextInput",Obj(("focused",focused),("mouseWheelEnabled",wheel))));
            GameUi.Path.Add(new RuntimeObject{TypeName="WorldOverlay"});Input.Buttons[0]=button;FieldZoomUi.Tick();
            Assert(FieldFreeCamera.PanAllowed==!focused);Input.Buttons[0]=false;
            var writes=FieldFreeCamera.Camera!.Get("body")!.Writes;s.Wheel(-1);
            Assert((FieldFreeCamera.Camera.Get("body")!.Writes==writes)==focused);PanUiCases++;
        }
        foreach(var focused in new[]{false,true})foreach(var wheel in new[]{false,true}){
            var s=new Scene();s.Wheel(1);s.Finish();TextInputPath(s,focused,wheel);
            RuntimeObject.OnUi=true;Input.Buttons[0]=true;FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed==!focused);PanUiCases++;
        }
        foreach(var cancel in new Action<Scene>[] {a=>Application.isFocused=false,a=>ModUi.IsOpen=true,a=>a.Root.Data["modalWaiting"]=true,
            a=>RuntimeObject.Dragging=true,a=>Input.touchCount=1,a=>FieldCameraReturnUi.ConsumedInput=true,a=>GameUi.FightWindowPresent=true,
            a=>GameUi.SettingsWindow="SettingWindow",a=>GameUi.SettingsWindow="SettingInBattleWindow",
            a=>GameUi.SettingsWindow="SinglePlayerSettingInBattleWindow",a=>GameUi.SettingsWindow="SettingListWindow"}){
            var s=new Scene();s.Wheel(1);s.Finish();Input.Buttons[0]=true;cancel(s);FieldZoomUi.Tick();
            Assert(FieldFreeCamera.Holding&&!FieldFreeCamera.PanAllowed);PanUiCases++;
        }
    }
    static void WheelUi(){
        FieldCameraReturnUi.Available=true;
        foreach(var type in new[]{"CardWindow","SelectMonsterWindow","LandShopWindow","BattlePlayerInfoWindow","Popup","StaticLabel","UnknownWindow","WorldOverlay"})
        foreach(var held in new[]{false,true})
        foreach(var direction in new[]{-1,1}){
            var s=new Scene();if(held){s.Wheel(1);s.Finish();}
            s.Windows(new RuntimeObject{TypeName=type});RuntimeObject.OnUi=true;
            GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName=type},s.Root});
            s.Root.Data["hasModalWindow"]=s.Root.Data["hasAnyPopup"]=true;
            var before=(FieldFreeCamera.Holding?FieldFreeCamera.Camera!.Get("body")!:s.Body).Field<Vector3>("m_FollowOffset").magnitude;
            s.Wheel(direction);Assert(FieldFreeCamera.Holding&&FieldFreeCamera.PanAllowed&&!FieldFreeCamera.PanIndicator);
            var after=FieldFreeCamera.Camera!.Get("body")!.Field<Vector3>("m_FollowOffset").magnitude;
            Assert(direction>0?after<before:after>before);NonScrollUiCases++;
        }
        foreach(var touch in new[]{false,true})foreach(var wheel in new[]{false,true})foreach(var ancestor in new[]{0,1}){
            var s=new Scene();ScrollPath(s,touch,wheel,ancestor);s.Wheel(1);
            Assert(FieldFreeCamera.Holding==!(touch&&wheel)&&s.Body.Writes==0);ConsumerCases++;
        }
        foreach(var focused in new[]{false,true})foreach(var wheel in new[]{false,true}){
            var s=new Scene();TextInputPath(s,focused,wheel);s.Wheel(1);
            Assert(FieldFreeCamera.Holding==!(focused&&wheel)&&s.Body.Writes==0);ConsumerCases++;
        }
        // A cached consumer must not block today's non-scroll hit, and vice versa.
        var scene=new Scene();ScrollPath(scene,true,true);GameUi.CachedPath.AddRange(GameUi.Path);GameUi.Path.Clear();GameUi.Path.Add(new());scene.Wheel(1);
        Assert(FieldFreeCamera.Holding&&GameUi.CachedReads==0);ConsumerCases++;
        scene=new Scene();GameUi.CachedPath.Add(new());ScrollPath(scene,true,true);scene.Wheel(1);
        Assert(!FieldFreeCamera.Holding&&scene.Body.Writes==0&&GameUi.CachedReads==0);ConsumerCases++;
    }
    static void PresentationAndFrames(){
        FieldCameraReturnUi.Available=true;
        foreach(var inactiveHierarchy in new[]{false,true}){
            var s=new Scene();s.Wheel(-1);s.Finish();var own=FieldFreeCamera.Camera!;var body=own.Get("body")!;
            var offset=body.Field<Vector3>("m_FollowOffset");var anchor=own.Get("Follow");var enters=FieldFreeCamera.Enters;var writes=body.Writes;
            RuntimeObject.Statics[RuntimeObject.FindClass("GameLogic","GameLogicManager")].Get("fight")!.Data["fightStatus"]=true;
            GameUi.FightWindowPresent=true;s.Windows(new RuntimeObject{TypeName="FightWindow"});s.Wheel(100);
            Assert(FieldFreeCamera.Holding&&FieldFreeCamera.Camera==own&&!FieldFreeCamera.LastBlocked&&FieldCameraReturnUi.Visible&&!FieldFreeCamera.PanAllowed);
            Assert(body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==offset.z&&FieldFreeCamera.Enters==enters);
            if(inactiveHierarchy)s.Camera.CameraGame.activeInHierarchy=false;else s.Camera.Data["enabled"]=false;
            s.Wheel(-100);FieldZoomUi.Tick();Assert(FieldFreeCamera.LastBlocked&&FieldFreeCamera.Holding&&!FieldCameraReturnUi.Visible);
            Assert(FieldFreeCamera.Camera==own&&own.Get("Follow")==anchor&&body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==offset.z&&s.Offset.z==-10);
            s.Camera.CameraGame.activeInHierarchy=true;s.Camera.Data["enabled"]=true;GameUi.FightWindowPresent=false;s.Windows();
            s.Finish();Assert(!FieldFreeCamera.LastBlocked&&FieldFreeCamera.Camera==own&&FieldCameraReturnUi.Visible&&FieldFreeCamera.Enters==enters);
            Assert(body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==offset.z&&own.Get("Follow")==anchor);
            s.Wheel(1);Assert(body.Field<Vector3>("m_FollowOffset").magnitude<offset.magnitude&&FieldFreeCamera.Camera==own);
            PresentationCases++;
        }
        foreach(var foreign in new Action<Scene>[] {
            a=>a.Frames.Data["Count"]=0,
            a=>{a.Frames.Data["Count"]=2;a.Frames.Items=new[]{Obj(("blend",a.Blend)),Obj(("blend",a.Blend))};},
            a=>a.Blend.Data["IsComplete"]=false,
            a=>a.Frames.Items[0].Data["blend"]=null!,
            a=>a.Blend.Data["CamB"]=null!,
            a=>a.Blend.Data["CamB"]=new RuntimeObject(),
            a=>a.Brain.Data["mFrameStack"]=null!
        }){
            var s=new Scene();foreign(s);var enters=FieldFreeCamera.Enters;var scans=Scans;s.Wheel(1);
            Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0&&FieldFreeCamera.Enters==enters&&Scans==scans);FrameCases++;
        }
        var known=new Scene();known.Wheel(1);Assert(FieldFreeCamera.Holding&&known.Body.Writes==0);FrameCases++;
    }
    static void TurnTransitions(){
        FieldCameraReturnUi.Available=true;
        foreach(var status in new[]{0,1})foreach(var completeGate in new[]{false,true})foreach(var direction in new[]{-1,1}){
            var s=new Scene();s.Free.Data["status"]=Obj(("value",status));
            if(completeGate)s.Blend.Data["IsComplete"]=false;else s.Brain.Data["IsBlending"]=true;
            s.Wheel(direction);Time.unscaledTime=.1f;s.Wheel(direction);
            Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0&&Plugin.Diagnostics.States.Contains("camera-transition"));
            // Own-turn character -> dice/move-selection switches can replace the known base rig.
            var next=Obj(("body",s.Body),("Follow",s.Follow),("transform",Obj(("rotation",new Quaternion()))));next.TypeName="CinemachineVirtualCamera";
            s.Active=next;s.Brain.Data["ActiveVirtualCamera"]=next;s.Blend.Data["CamB"]=next;s.SceneObject.Data["freeCamera"]=next;
            RuntimeObject.Statics[RuntimeObject.FindClass("Core.Camera","CameraManager")].Get("current")!.Data["vCamera"]=next;
            s.Brain.Data["IsBlending"]=false;s.Blend.Data["IsComplete"]=true;Time.unscaledTime=.2f;FieldZoomUi.Tick();s.Finish();
            Assert(FieldFreeCamera.Holding);
            var offset=FieldFreeCamera.Camera!.Get("body")!.Field<Vector3>("m_FollowOffset").z;
            Assert(FieldFreeCamera.Holding&&Math.Abs(offset+10*MathF.Pow(.9f,2*direction))<.0001&&s.Body.Writes==0);
            Assert(Plugin.Diagnostics.States.Contains("transition-resumed"));
        }
        foreach(var cancel in new Action<Scene>[] {
            a=>Application.isFocused=false,a=>ModUi.IsOpen=true,a=>Input.Buttons[0]=true,
            a=>a.Root.Data["modalWaiting"]=true,a=>RuntimeObject.Dragging=true,a=>ScrollPath(a,true,true),
            a=>TextInputPath(a,true,true),a=>GameUi.FightWindowPresent=true,
            a=>a.Frames.Data["Count"]=2,a=>a.Blend.Data["CamB"]=new RuntimeObject(),
            a=>a.Camera.Data["enabled"]=false,a=>PvpSafety.Suspended=true,a=>Plugin.FieldZoom.Value=false
        }){
            var s=new Scene();s.Brain.Data["IsBlending"]=true;s.Wheel(-1);cancel(s);FieldZoomUi.Tick();
            Application.isFocused=true;ModUi.IsOpen=false;Input.Buttons[0]=false;RuntimeObject.Dragging=false;s.Root.Data["modalWaiting"]=false;
            GameUi.Path.Clear();GameUi.FightWindowPresent=false;s.Frames.Data["Count"]=1;s.Blend.Data["CamB"]=s.Active;
            s.Camera.Data["enabled"]=true;PvpSafety.Suspended=false;Plugin.FieldZoom.Value=true;
            s.Brain.Data["IsBlending"]=false;s.Blend.Data["IsComplete"]=true;FieldZoomUi.Tick();
            Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);
        }
        var zero=new Scene();zero.Brain.Data["IsBlending"]=true;zero.Wheel(1);zero.Wheel(-1);
        zero.Brain.Data["IsBlending"]=false;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&zero.Body.Writes==0);
        foreach(var invalid in new Action<Scene>[] {
            a=>a.Active.Data["aim"]=new RuntimeObject(),a=>a.Active.Data["LookAt"]=a.Follow,
            a=>a.Body.Data["IsValid"]=false,a=>a.Body.Data["m_BindingMode"]=Obj(("value",5))
        }){
            var s=new Scene();s.Brain.Data["IsBlending"]=true;invalid(s);s.Wheel(-1);
            s.Active.Data["aim"]=null!;s.Active.Data["LookAt"]=null!;s.Body.Data["IsValid"]=true;s.Body.Data["m_BindingMode"]=Obj(("value",4));
            s.Brain.Data["IsBlending"]=false;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);
        }
        foreach(var invalid in new Action<Scene>[] {
            a=>a.Camera.Data["orthographic"]=true,a=>a.Camera.Data["usePhysicalProperties"]=true,
            a=>a.Camera.Data["lensShift"]=new Vector2(1,0),a=>a.Active.Data["Follow"]=null!
        }){
            var s=new Scene();s.Brain.Data["IsBlending"]=true;s.Wheel(-1);invalid(s);FieldZoomUi.Tick();
            s.Camera.Data["orthographic"]=s.Camera.Data["usePhysicalProperties"]=false;s.Camera.Data["lensShift"]=new Vector2();s.Active.Data["Follow"]=s.Follow;
            s.Brain.Data["IsBlending"]=false;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);
        }
        // Compare corrected rendered orientations to native-transform mismatch, including an already-owned view.
        foreach(var held in new[]{false,true})foreach(var direction in new[]{-1,1}){
            var s=new Scene();if(held){s.Wheel(1);s.Finish();}
            s.Camera.CameraTransform.rotation=new Quaternion{w=1};
            var radians=5*MathF.PI/180;s.Camera.CameraTransform.right=new(MathF.Cos(radians),MathF.Sin(radians),0);
            s.Camera.CameraTransform.up=new(-MathF.Sin(radians),MathF.Cos(radians),0);
            var before=(held?FieldFreeCamera.Camera!.Get("body")!:s.Body).Field<Vector3>("m_FollowOffset").magnitude;
            s.Wheel(direction);Assert(FieldFreeCamera.Holding);var after=FieldFreeCamera.Camera!.Get("body")!.Field<Vector3>("m_FollowOffset").magnitude;
            Assert(FieldFreeCamera.Holding&&(direction>0?after<before:after>before)&&s.Body.Writes==0);
        }
    }
    public static int BlendLifetimeCases;
    static void BlendLifetime(){
        FieldCameraReturnUi.Available=true;
        foreach(var incomplete in new[]{false,true})foreach(var direction in new[]{-1,1}){
            var s=new Scene();s.Blend.Data["Duration"]=2f;
            s.Brain.Data["IsBlending"]=!incomplete;s.Blend.Data["IsComplete"]=!incomplete;
            s.Wheel(direction);Time.unscaledTime=1;FieldZoomUi.Tick();
            Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);
            s.Brain.Data["IsBlending"]=false;s.Blend.Data["IsComplete"]=true;s.Blend.Data["TimeInBlend"]=2f;
            Time.unscaledTime=2.1f;FieldZoomUi.Tick();s.Finish();
            Assert(FieldFreeCamera.Holding&&Plugin.Diagnostics.States.Contains("transition-resumed"));
            Assert(Math.Abs(FieldFreeCamera.Camera!.Get("body")!.Field<Vector3>("m_FollowOffset").magnitude-10*MathF.Pow(.9f,direction))<.0001&&s.Body.Writes==0);
            BlendLifetimeCases++;
        }
        // Wall time is not a native blend clock, even when its estimated remaining duration elapsed.
        foreach(var elapsed in new[]{.75f,30f}){
            var s=new Scene();s.Brain.Data["IsBlending"]=true;s.Blend.Data["Duration"]=2f;s.Blend.Data["TimeInBlend"]=1.5f;
            Time.unscaledTime=4;s.Wheel(1);Time.unscaledTime=4+elapsed;s.Brain.Data["IsBlending"]=false;FieldZoomUi.Tick();
            Assert(FieldFreeCamera.Holding&&s.Body.Writes==0);BlendLifetimeCases++;
        }
        foreach(var times in new[]{(float.NaN,0f),(float.PositiveInfinity,0f),(float.NegativeInfinity,0f),
            (2f,float.NaN),(2f,float.PositiveInfinity),(2f,float.NegativeInfinity),(float.MaxValue,-float.MaxValue),(-1f,0f),(1f,2f),(0f,0f)})
        foreach(var elapsed in new[]{.4f,30f}){
            var s=new Scene();s.Brain.Data["IsBlending"]=true;s.Blend.Data["Duration"]=times.Item1;s.Blend.Data["TimeInBlend"]=times.Item2;
            s.Wheel(1);Time.unscaledTime=elapsed;s.Brain.Data["IsBlending"]=false;FieldZoomUi.Tick();
            Assert(FieldFreeCamera.Holding&&s.Body.Writes==0);BlendLifetimeCases++;
        }
        // Local selection activates the known free rig; observing another participant uses the known
        // player rig and a passive thinking tip. Neither changes eligibility or the native blend clock.
        foreach(var local in new[]{false,true})foreach(var rate in new[]{0f,.1f})foreach(var direction in new[]{-1,1})
        foreach(var status in new[]{0,1,2,3}){
            var s=new Scene();s.Free.Data["status"]=Obj(("value",status));
            if(!local){s.SceneObject.Data["freeCamera"]=new RuntimeObject();s.Windows(new RuntimeObject{TypeName="TipsWindow"});}
            s.Brain.Data["IsBlending"]=true;s.Blend.Data["IsComplete"]=false;s.Blend.Data["Duration"]=2f;s.Wheel(direction);
            for(var i=0;i<120;i++){
                Time.unscaledTime+=.1f;Time.deltaTime=.1f*rate;
                s.Blend.Data["TimeInBlend"]=(float)s.Blend.Data["TimeInBlend"]+Time.deltaTime;
                FieldZoomUi.Tick();
            }
            Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);
            // The native transition can restart and replace its blend object while retaining frame0.
            var retarget=Obj(("body",s.Body),("Follow",s.Follow),("transform",Obj(("rotation",new Quaternion()))));retarget.TypeName="CinemachineVirtualCamera";
            s.Active=retarget;s.Brain.Data["ActiveVirtualCamera"]=retarget;
            if(local)s.SceneObject.Data["freeCamera"]=retarget;
            RuntimeObject.Statics[RuntimeObject.FindClass("Core.Camera","CameraManager")].Get("current")!.Data["vCamera"]=retarget;
            s.Blend=Obj(("CamB",retarget),("IsComplete",false),("Duration",10f),("TimeInBlend",0f));s.Frames.Items[0].Data["blend"]=s.Blend;
            Time.unscaledTime+=20;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);
            s.Brain.Data["IsBlending"]=false;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding);
            s.Blend.Data["IsComplete"]=true;FieldZoomUi.Tick();s.Finish();
            var body=FieldFreeCamera.Camera!.Get("body")!;
            Assert(FieldFreeCamera.Holding&&Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-10*MathF.Pow(.9f,direction))<.0001&&s.Body.Writes==0);
            var before=body.Field<Vector3>("m_FollowOffset");s.Finish();Assert(body.Field<Vector3>("m_FollowOffset").z==before.z);
            BlendLifetimeCases++;
        }
        foreach(var replace in new Action<Scene>[] {
            a=>a.Frames.Items[0]=Obj(("blend",a.Blend)),
            a=>{a.Brain=Obj(("IsBlending",true),("ActiveVirtualCamera",a.Active),("mFrameStack",a.Frames));a.SceneObject.Data["cinemachineBrain"]=a.Brain;},
            a=>{var camera=Obj();foreach(var p in a.Camera.Data)camera.Data[p.Key]=p.Value;a.Camera=camera;a.SceneObject.Data["mainCamera"]=camera;}
        }){
            var s=new Scene();s.Brain.Data["IsBlending"]=true;s.Wheel(1);replace(s);FieldZoomUi.Tick();
            s.Brain.Data["IsBlending"]=false;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);BlendLifetimeCases++;
        }
        foreach(var cancel in new Action<Scene>[] {
            a=>Application.isFocused=false,a=>ModUi.IsOpen=true,a=>Input.Buttons[0]=true,a=>Input.Buttons[1]=true,a=>Input.Buttons[2]=true,
            a=>a.Root.Data["modalWaiting"]=true,a=>RuntimeObject.Dragging=true,a=>Input.touchCount=1,a=>FieldCameraReturnUi.ConsumedInput=true,
            a=>ScrollPath(a,true,true),a=>TextInputPath(a,true,true),a=>GameUi.FightWindowPresent=true,
            a=>GameUi.SettingsWindow="SettingWindow",a=>a.Frames.Data["Count"]=2,a=>a.Blend.Data["CamB"]=new RuntimeObject(),
            a=>a.Camera.Data["enabled"]=false,a=>PvpSafety.Suspended=true,a=>Plugin.FieldZoom.Value=false,
            a=>a.Body.Data["m_FollowOffset"]=new Vector3(float.NaN,0,-10)
        }){
            var s=new Scene();s.Brain.Data["IsBlending"]=true;s.Blend.Data["Duration"]=2f;s.Wheel(1);Time.unscaledTime=1;cancel(s);FieldZoomUi.Tick();
            Application.isFocused=true;ModUi.IsOpen=false;Input.Buttons=new bool[3];Input.touchCount=0;FieldCameraReturnUi.ConsumedInput=false;
            RuntimeObject.Dragging=false;s.Root.Data["modalWaiting"]=false;GameUi.Path.Clear();GameUi.FightWindowPresent=GameUi.EncounterHidden=false;GameUi.SettingsWindow=null;
            s.Frames.Data["Count"]=1;s.Blend.Data["CamB"]=s.Active;s.Camera.Data["enabled"]=true;PvpSafety.Suspended=false;Plugin.FieldZoom.Value=true;
            s.Body.Data["m_FollowOffset"]=new Vector3(0,0,-10);
            s.Brain.Data["IsBlending"]=false;Time.unscaledTime=2.1f;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);BlendLifetimeCases++;
        }
        var replaced=new Scene();replaced.Brain.Data["IsBlending"]=true;replaced.Blend.Data["Duration"]=2f;replaced.Wheel(1);
        var next=new Scene(clear:false);Time.unscaledTime=2.1f;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&replaced.Body.Writes==0&&next.Body.Writes==0);BlendLifetimeCases++;
    }
    public static int RecoveryCases;
    static void FocusAndEncounterRecovery(){
        FieldCameraReturnUi.Available=true;
        foreach(var local in new[]{false,true})foreach(var change in new[]{"same","native-replaced","native-overwritten","scene-replaced"}){
            var s=new Scene();if(!local)s.Windows(new RuntimeObject{TypeName="TipsWindow"});
            s.Wheel(1);s.Finish();var ownedBody=FieldFreeCamera.Camera!.Get("body")!;
            var before=ownedBody.Field<Vector3>("m_FollowOffset");var writes=ownedBody.Writes;
            Application.isFocused=false;s.Wheel(-100);s.Wheel(100);Time.unscaledTime+=30;FieldZoomUi.Tick();
            Assert(ownedBody.Writes==writes&&ownedBody.Field<Vector3>("m_FollowOffset").z==before.z);
            if(change=="native-replaced"){
                var replacementBody=Obj(("m_FollowOffset",new Vector3(0,0,-20)),("m_BindingMode",Obj(("value",4))),("IsValid",true));replacementBody.TypeName="CinemachineTransposer";
                var replacement=Obj(("body",replacementBody),("Follow",s.Follow),("transform",Obj(("rotation",new Quaternion()))));replacement.TypeName="CinemachineVirtualCamera";
                s.Active=replacement;s.Brain.Data["ActiveVirtualCamera"]=replacement;s.Blend.Data["CamB"]=replacement;s.SceneObject.Data["freeCamera"]=replacement;
                RuntimeObject.Statics[RuntimeObject.FindClass("Core.Camera","CameraManager")].Get("current")!.Data["vCamera"]=replacement;
            }
            if(change=="native-overwritten")s.Body.Data["m_FollowOffset"]=new Vector3(0,0,-20);
            if(change=="scene-replaced"){s=new Scene(clear:false);Application.isFocused=false;FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&s.Body.Writes==0);}
            Application.isFocused=true;FieldZoomUi.Tick();
            // Public encounter optics/board bounds can invalidate whole-field coverage without
            // invalidating a close-up or its original field-camera calibration.
            s.Camera.Data["fieldOfView"]=60f;s.Camera.Data["aspect"]=1f;s.Camera.Data["nearClipPlane"]=1f;s.Camera.Data["farClipPlane"]=50f;
            s.Land.Renderers[0].bounds=new(){min=new(-100,-1,0),max=new(100,1,0)};
            s.Wheel(1);s.Finish();var body=FieldFreeCamera.Camera!.Get("body")!;var inward=body.Field<Vector3>("m_FollowOffset");
            Assert(inward.magnitude<10&&FieldIndicatorOpacity.Progress==0);
            s.Wheel(-1);s.Finish();Assert(body.Field<Vector3>("m_FollowOffset").magnitude>inward.magnitude&&FieldIndicatorOpacity.Progress==0);
            s.Wheel(-100);s.Finish();Assert(Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-50)<.0001);
            writes=body.Writes;s.Wheel(-1);s.Finish();Assert(body.Writes==writes&&Math.Abs(FieldIndicatorOpacity.Progress-1)<.000001);
            FieldZoomUi.Clear();Assert(s.Offset.z==(change=="native-overwritten"?-20:-10));RecoveryCases++;
        }
        foreach(var local in new[]{false,true}){
            var s=new Scene();if(!local)s.Windows(new RuntimeObject{TypeName="TipsWindow"});s.Wheel(1);s.Finish();
            var body=FieldFreeCamera.Camera!.Get("body")!;var pose=body.Field<Vector3>("m_FollowOffset");var writes=body.Writes;
            GameUi.FightWindowPresent=true;s.Wheel(-1);Assert(body.Writes==writes);
            s.Camera.Data["enabled"]=false;s.Wheel(-1);Assert(body.Writes==writes&&body.Field<Vector3>("m_FollowOffset").z==pose.z);
            s.Camera.Data["enabled"]=true;GameUi.FightWindowPresent=false;FieldZoomUi.Tick();
            s.Wheel(-1);s.Finish();Assert(body.Field<Vector3>("m_FollowOffset").magnitude>pose.magnitude&&s.Body.Writes==0);RecoveryCases++;
        }
        var diagnostic=new Scene();diagnostic.Camera.Data["fieldOfView"]=60f;diagnostic.Camera.Data["aspect"]=1f;diagnostic.Camera.Data["farClipPlane"]=10f;
        diagnostic.Land.Renderers[0].bounds=new(){min=new(-100,-1,0),max=new(100,1,0)};
        diagnostic.Wheel(-1);Assert(Plugin.Diagnostics.Geometry.Count==0);
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try{
            System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("fr-FR");Plugin.Diagnostics.IsRecording=true;diagnostic.Wheel(-1);
            Assert(Plugin.Diagnostics.Geometry.Count==1);
            Assert(Plugin.Diagnostics.Geometry[0].StartsWith("wheel=-1.000; held=False; offset=0.0000,0.0000,-10.0000; span=200.0000,2.0000,0.0000;"));
            Assert(!Plugin.Diagnostics.Geometry[0].Contains("Pointer")&&!Plugin.Diagnostics.Geometry[0].Contains("Player"));
        }finally{System.Globalization.CultureInfo.CurrentCulture=culture;Plugin.Diagnostics.IsRecording=false;}
        RecoveryCases++;
        foreach(var direction in new[]{-1,1})foreach(var invalid in new Action<Scene>[] {
            a=>a.Camera.Data["fieldOfView"]=float.NaN,a=>a.Camera.Data["aspect"]=float.PositiveInfinity,
            a=>a.Camera.Data["nearClipPlane"]=float.NaN,a=>a.Camera.Data["farClipPlane"]=float.PositiveInfinity,
            a=>a.Body.Data["m_FollowOffset"]=new Vector3(float.NaN,0,-10),a=>a.Land.Renderers[0].enabled=false
        }){
            var s=new Scene();invalid(s);var enters=FieldFreeCamera.Enters;s.Wheel(direction);
            Assert(s.Body.Writes==0&&!FieldFreeCamera.Holding&&FieldFreeCamera.Enters==enters);RecoveryCases++;
        }
    }
    public static int OutwardRecoveryCases;
    static void AlreadyOutwardRecovery(){
        FieldCameraReturnUi.Available=true;
        foreach(var local in new[]{false,true})foreach(var interruption in new[]{"encounter","focus","both"}){
            var s=new Scene();if(!local)s.Windows(new RuntimeObject{TypeName="TipsWindow"});
            s.Wheel(-100);s.Finish();var body=FieldFreeCamera.Camera!.Get("body")!;
            Assert(Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-45)<.0001&&s.Body.Writes==0);
            if(interruption!="focus"){
                GameUi.FightWindowPresent=true;var writes=body.Writes;s.Wheel(-1);Assert(body.Writes==writes);
                GameUi.FightWindowPresent=false;
            }
            if(interruption!="encounter"){
                Application.isFocused=false;var writes=body.Writes;s.Wheel(-1);s.Wheel(1);Assert(body.Writes==writes);
                Application.isFocused=true;
            }
            s.Camera.Data["fieldOfView"]=60f;s.Camera.Data["aspect"]=1f;s.Camera.Data["nearClipPlane"]=1f;s.Camera.Data["farClipPlane"]=50f;
            s.Land.Renderers[0].bounds=new(){min=new(-100,-1,0),max=new(100,1,0)};
            var before=body.Field<Vector3>("m_FollowOffset").magnitude;Plugin.Diagnostics.States.Clear();s.Wheel(-1);s.Finish();
            Assert(Plugin.Diagnostics.States.Contains("accepted")&&body.Field<Vector3>("m_FollowOffset").magnitude>before);
            Assert(Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-50)<.0001&&Math.Abs(FieldIndicatorOpacity.Progress-1)<.000001&&s.Body.Writes==0);
            var stopped=body.Writes;s.Wheel(-1);s.Finish();Assert(body.Writes==stopped);
            s.Wheel(1);s.Finish();Assert(body.Field<Vector3>("m_FollowOffset").magnitude<50&&FieldIndicatorOpacity.Progress>0&&FieldIndicatorOpacity.Progress<1);
            s.Wheel(-1);s.Finish();Assert(Math.Abs(body.Field<Vector3>("m_FollowOffset").magnitude-50)<.0001&&Math.Abs(FieldIndicatorOpacity.Progress-1)<.000001);
            FieldZoomUi.Clear();Assert(s.Offset.z==-10);OutwardRecoveryCases++;
        }
    }
    public static void Run(){
        FieldPointerTest.Check.Run();Vector3Bridge.Run();Geometry();var s=new Scene();FieldZoomUi.Tick();Assert(Scans==0&&s.Body.Writes==0);
        s.Wheel(1);Assert(s.Offset.z>-10&&s.Offset.z<-9);int scans=Scans;
        FieldZoomUi.Tick();Assert(Scans==scans&&s.Body.Writes==2);s.Finish();Assert(Math.Abs(s.Offset.z+9)<.00001);
        FieldZoomUi.Clear();Assert(s.Offset.z==-10);
        s=new Scene();s.Wheel(-100);s.Finish();Assert(Math.Abs(s.Offset.z+45f)<.0001);FieldZoomUi.Clear();
        // Normal geometry remains usable beside an invalid/outdated renderer.
        s=new Scene();var valid=s.Land.Renderers[0];
        s.Land.Renderers=new[]{valid,new Renderer{bounds=new(){min=new(float.NaN,0,0),max=new(100,100,100)}},
            new Renderer{bounds=new(){min=new(1,0,0),max=new(-1,0,0)}}};
        s.Wheel(-100);s.Finish();Assert(Math.Abs(s.Offset.z+45f)<.0001&&FieldIndicatorOpacity.Dimmed);
        s.Wheel(100);s.Finish();Assert(!FieldIndicatorOpacity.Dimmed);FieldZoomUi.Clear();
        // Repeated fractional ticks reach the same stop after a pan without native pose jitter.
        s=new Scene();s.Follow.Data["position"]=new Vector3(30,0,-12.5f);
        for(var tick=0;tick<160;tick++)s.Wheel(-.25f);
        s.Finish();Assert(Math.Abs(s.Offset.z+45f)<.0001&&FieldIndicatorOpacity.Dimmed);
        s.Camera.Data["enabled"]=false;FieldZoomUi.Tick();Assert(!FieldIndicatorOpacity.Dimmed);FieldZoomUi.Clear();
        // A rotated diamond/L-shaped field uses the same WORLD box as native Pan bounds.
        foreach(var anchorY in new[]{0f,50f}){
            s=new Scene();s.Land.Renderers=new[]{
                new Renderer{bounds=new(){min=new(-40,0,0),max=new(-40,0,0)}},
                new Renderer{bounds=new(){min=new(40,0,0),max=new(40,0,0)}},
                new Renderer{bounds=new(){min=new(0,0,-40),max=new(0,0,-40)}},
                new Renderer{bounds=new(){min=new(0,0,40),max=new(0,0,40)}}};
            s.Body.Data["m_FollowOffset"]=new Vector3(-10,20,-10);
            s.Follow.Data["position"]=new Vector3(0,anchorY,0);s.Settle();
            var view=s.Camera.CameraTransform;
            view.right=new(MathF.Sqrt(.5f),0,-MathF.Sqrt(.5f));
            view.up=new(.5f,MathF.Sqrt(.5f),.5f);view.forward=new(.5f,-MathF.Sqrt(.5f),.5f);
            s.Wheel(-100);s.Finish();var stop=s.Offset;
            Assert(stop.magnitude>MathF.Sqrt(600));
            foreach(var panX in new[]{-40f,0f,40f})foreach(var panZ in new[]{-40f,0f,40f})
            foreach(var edgeX in new[]{-40f,40f})foreach(var edgeZ in new[]{-40f,40f}){
                var relative=new Vector3(edgeX,0,edgeZ)-(new Vector3(panX,anchorY,panZ)+stop);
                var depth=Vector3.Dot(relative,view.forward);
                Assert(Math.Abs(Vector3.Dot(relative,view.right))<=depth*(16d/9)+.0001
                    &&Math.Abs(Vector3.Dot(relative,view.up))<=depth+.0001&&depth>=.1f&&depth<=1000);
            }
            FieldZoomUi.Clear();
        }
        foreach(var gate in new Action<Scene>[] {
            a=>ModUi.IsOpen=true,a=>Application.isFocused=false,a=>Input.Buttons[0]=true,a=>Input.Buttons[1]=true,a=>Input.Buttons[2]=true,
            a=>Input.touchCount=1,a=>RuntimeObject.Dragging=true,a=>a.Root.Data["modalWaiting"]=true,
            a=>ScrollPath(a,true,true),a=>TextInputPath(a,true,true),
            a=>a.Camera.Data["orthographic"]=true,a=>a.Camera.Data["usePhysicalProperties"]=true,a=>a.Camera.Data["lensShift"]=new Vector2(1,0),
            a=>a.Active.Data["aim"]=new RuntimeObject(),a=>a.Active.Data["LookAt"]=a.Follow,a=>a.Body.Data["m_BindingMode"]=Obj(("value",5)),
            a=>a.Brain.Data["IsBlending"]=true,
            a=>GameUi.FightWindowPresent=true,a=>PvpSafety.Suspended=true,a=>Plugin.FieldZoom.Value=false
        }){s=new Scene();int before=Scans;gate(s);s.Wheel(1);Assert(s.Body.Writes==0&&Scans==before);}
        // Foreign offsets, destroyed old objects and scene replacement preserve ownership.
        s=new Scene();s.Wheel(1);s.Body.Data["m_FollowOffset"]=new Vector3(0,0,-7);FieldZoomUi.Clear();Assert(s.Offset.z==-7);
        s=new Scene();s.Wheel(1);s.Body.Dead=true;FieldZoomUi.Clear();Assert(s.Body.Writes==1);
        s=new Scene();s.Wheel(1);var preFightOffset=s.Offset.z;GameUi.FightWindowPresent=true;FieldZoomUi.Tick();Assert(s.Offset.z==preFightOffset);
        s=new Scene();s.Wheel(1);var old=s;var next=new Scene(clear:false);Assert(old.Offset.z>-10);FieldZoomUi.Tick();Assert(old.Offset.z==-10&&next.Body.Writes==0);
        s=new Scene();s.Wheel(1);var oldBody=s.Body;
        s.Body=Obj(("m_FollowOffset",new Vector3(0,0,-20)),("m_BindingMode",Obj(("value",4))),("IsValid",true));s.Body.TypeName="CinemachineTransposer";
        s.Active.Data["body"]=s.Body;FieldZoomUi.Tick();Assert(oldBody.Field<Vector3>("m_FollowOffset").z==-10&&s.Body.Writes==0);FieldZoomUi.Clear();
        s=new Scene();FieldZoomUi.Tick();s.Body.Data["m_FollowOffset"]=new Vector3(0,0,-8);s.Settle();s.Wheel(1);FieldZoomUi.Clear();Assert(s.Offset.z==-8);
        s=new Scene();s.Wheel(1);Plugin.FieldZoom.Value=false;FieldZoomUi.Tick();Assert(s.Offset.z==-10);
        // An ordinary UI hit does not own the wheel; only verified native consumers do.
        s=new Scene();RuntimeObject.OnUi=true;s.Root.Data["touchTarget"]=Obj(("touchable",false));s.Wheel(1);Assert(s.Body.Writes==1);FieldZoomUi.Clear();
        // Input during native damping is accumulated, not dropped; repeated wheel ticks share renderer discovery.
        s=new Scene();s.Wheel(1);scans=Scans;s.Wheel(1);s.Finish();Assert(Math.Abs(s.Offset.z+8.1f)<.00001&&Scans==scans);
        s=new Scene();s.Camera.CameraTransform.position=new(0,0,-11);s.Wheel(1);s.Wheel(-1);s.Finish();Assert(Math.Abs(s.Offset.z+10)<.00001);
        // The interpolation is frame-rate independent and a long stall cannot jump to the target.
        float after30=0;
        foreach(var rate in new[]{30,60,120}){s=new Scene();Time.unscaledDeltaTime=1f/rate;s.Wheel(1);for(var i=1;i<rate/10;i++)FieldZoomUi.Tick();if(rate==30)after30=s.Offset.z;else Assert(Math.Abs(s.Offset.z-after30)<.00001);}
        s=new Scene();Time.unscaledDeltaTime=10;s.Wheel(1);Assert(s.Offset.z<-9&&s.Offset.z>-10);
        foreach(var dt in new[]{0f,-1f,float.NaN,float.PositiveInfinity}){s=new Scene();Time.unscaledDeltaTime=dt;s.Wheel(1);Assert(s.Body.Writes==0);}
        s=new Scene();s.Wheel(1);scans=Scans;Time.unscaledTime=1;s.Wheel(1);Assert(Scans==scans+1);
        s=new Scene();s.Wheel(1);s.Land.Renderers[0].Dead=true;s.Wheel(1);s.Finish();Assert(Math.Abs(s.Offset.z+9)<.00001);
        s=new Scene();s.Wheel(1);scans=Scans;
        var nodes=RuntimeObject.Statics[RuntimeObject.FindClass("Core.Unit","LandManager")].Field("NodeDict")!;
        nodes.Data["Count"]=0;s.Wheel(1);s.Finish();Assert(Math.Abs(s.Offset.z+9)<.00001&&Scans==scans);
        s=new Scene();s.Wheel(1);scans=Scans;
        nodes=RuntimeObject.Statics[RuntimeObject.FindClass("Core.Unit","LandManager")].Field("NodeDict")!;
        nodes.Data["Count"]=2;s.Wheel(1);Assert(Scans==scans+1);
        s=new Scene();s.Wheel(1);scans=Scans;s.Land.Renderers[0].bounds=new(){min=new(-80,-40,0),max=new(80,40,0)};
        s.Wheel(-100);s.Finish();Assert(Math.Abs(s.Offset.z+90)<.0001&&Scans==scans);
        s=new Scene();s.Land.Renderers[0].enabled=false;s.Wheel(1);Assert(s.Body.Writes==0);
        s=new Scene();s.Land.Renderers[0].gameObject.activeInHierarchy=false;s.Wheel(1);Assert(s.Body.Writes==0);
        foreach(var guard in new Action<Scene>[]{a=>ModUi.IsOpen=true,a=>Application.isFocused=false,a=>Input.Buttons[0]=true,a=>ScrollPath(a,true,true),a=>TextInputPath(a,true,true)}){
            s=new Scene();s.Wheel(1);var before=s.Offset.z;guard(s);FieldZoomUi.Tick();
            ModUi.IsOpen=false;Application.isFocused=true;Input.Buttons[0]=false;GameUi.Path.Clear();RuntimeObject.OnUi=false;
            s.Finish();Assert(s.Offset.z==before);
        }
        // No return anchor means ordinary wheel zoom only, never an inescapable owned view.
        s=new Scene();s.Wheel(1);Assert(!FieldFreeCamera.Holding);
        FieldCameraReturnUi.Available=true;s=new Scene();s.Wheel(1);Assert(FieldFreeCamera.Holding);
        var heldBody=FieldFreeCamera.Camera!.Get("body")!;var heldOffset=heldBody.Field<Vector3>("m_FollowOffset").z;
        GameUi.FightWindowPresent=true;FieldZoomUi.Tick();Assert(heldBody.Field<Vector3>("m_FollowOffset").z==heldOffset&&FieldFreeCamera.Holding&&FieldCameraReturnUi.Visible&&!FieldFreeCamera.LastBlocked);
        GameUi.FightWindowPresent=false;s.Finish();Assert(heldBody.Field<Vector3>("m_FollowOffset").z==heldOffset);
        FieldZoomUi.Clear();Assert(s.Offset.z==-10&&!FieldFreeCamera.Holding);
        // A native blend/foreign frame can yield the override without ending the owned session.
        s=new Scene();s.Wheel(1);heldBody=FieldFreeCamera.Camera!.Get("body")!;
        var yieldedWrites=heldBody.Writes;var yieldedCamera=FieldFreeCamera.Camera;scans=Scans;
        FieldFreeCamera.Yield=true;s.Wheel(-100);
        Assert(FieldFreeCamera.Holding&&!FieldFreeCamera.LastBlocked&&FieldCameraReturnUi.Visible);
        Assert(FieldFreeCamera.Camera==yieldedCamera&&heldBody.Writes==yieldedWrites&&Scans==scans);
        FieldFreeCamera.Yield=false;s.Finish();Assert(FieldCameraReturnUi.Visible&&heldBody.Writes==yieldedWrites);
        GameUi.FightWindowPresent=true;FieldZoomUi.Tick();Assert(FieldFreeCamera.Holding&&FieldCameraReturnUi.Visible&&!FieldFreeCamera.LastBlocked);
        FieldZoomUi.Clear();
        // Owned FIELD movement and wheel remain available through native effects/map markers.
        foreach(var state in new[]{2,3}){
            s=new Scene();s.Wheel(1);Assert(FieldFreeCamera.Holding);
            heldBody=FieldFreeCamera.Camera!.Get("body")!;
            var ownCamera=FieldFreeCamera.Camera;var ownOffset=heldBody.Field<Vector3>("m_FollowOffset");
            var enters=FieldFreeCamera.Enters;var writes=heldBody.Writes;scans=Scans;
            s.Free.Data["status"]=Obj(("value",state));s.Wheel(-100);
            Assert(!FieldFreeCamera.LastBlocked&&FieldFreeCamera.Holding&&FieldFreeCamera.Camera==ownCamera);
            Assert(FieldFreeCamera.PanAllowed&&FieldCameraReturnUi.Visible&&FieldFreeCamera.Enters==enters);
            Assert(heldBody.Writes==writes+1&&Scans>=scans&&heldBody.Field<Vector3>("m_FollowOffset").magnitude>ownOffset.magnitude&&s.Offset.z==-10);
            s.Free.Data["status"]=Obj(("value",0));s.Finish();
            Assert(FieldFreeCamera.Camera==ownCamera&&heldBody.Writes>writes&&FieldCameraReturnUi.Visible);
            FieldZoomUi.Clear();s=new Scene();s.Free.Data["status"]=Obj(("value",state));s.Wheel(1);
            Assert(FieldFreeCamera.Holding&&!FieldFreeCamera.LastBlocked&&FieldFreeCamera.PanAllowed&&FieldFreeCamera.Enters==enters+1&&s.Body.Writes==0);
        }
        // UI hit is an acquisition/zoom gate, not cancellation of an already captured world drag.
        s=new Scene();s.Wheel(1);Input.Buttons[0]=true;RuntimeObject.OnUi=true;FieldZoomUi.Tick();
        Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.PanOnUi&&FieldFreeCamera.Holding);
        // Only exact live field-attribute ancestry grants an indicator-origin drag.
        foreach(var type in new[]{"UICom_AttrInfo","UICom_PlayerAttrInfo"}){
            s=new Scene();s.Wheel(1);s.Finish();var plate=new RuntimeObject{TypeName=type};
            var parent=type=="UICom_AttrInfo"?s.Attributes:s.PlayerAttributes;
            GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName="GGraph"},plate,parent,s.Root});
            var freshReads=GameUi.FreshReads;Input.Buttons[0]=Input.Down[0]=RuntimeObject.OnUi=true;FieldZoomUi.Tick();
            Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.PanOnUi&&FieldFreeCamera.PanIndicator&&GameUi.FreshReads==freshReads+2);
            Input.Down[0]=false;GameUi.Path.Clear();FieldZoomUi.Tick();
            Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.PanOnUi&&!FieldFreeCamera.PanIndicator&&GameUi.FreshReads==freshReads+3);
            Input.Buttons[0]=false;GameUi.Path.AddRange(new[]{plate,parent,s.Root});
            var writes=FieldFreeCamera.Camera!.Get("body")!.Writes;s.Wheel(-1);
            Assert(FieldFreeCamera.Camera!.Get("body")!.Writes==writes+1&&!FieldFreeCamera.PanIndicator&&GameUi.FreshReads==freshReads+4);
        }
        foreach(var invalid in new Action<Scene>[] {
            a=>GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName="UICom_AttrInfo"},new RuntimeObject(),a.Root}),
            a=>GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName="GButton"},a.Attributes,a.Root}),
            a=>{a.Attributes.Dead=true;GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName="UICom_AttrInfo"},a.Attributes});},
            a=>{a.BattleUi.Data["com_AttrInfos"]=null!;GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName="UICom_AttrInfo"},a.Attributes});},
            a=>{a.Attributes.Data["visible"]=false;GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName="UICom_AttrInfo"},a.Attributes});},
            a=>GameUi.Path.AddRange(new[]{Obj(("draggable",true)),new RuntimeObject{TypeName="UICom_AttrInfo"},a.Attributes})
        }){
            s=new Scene();s.Wheel(1);invalid(s);Input.Buttons[0]=Input.Down[0]=RuntimeObject.OnUi=true;FieldZoomUi.Tick();
            Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.PanOnUi&&!FieldFreeCamera.PanIndicator);
        }
        s=new Scene();s.Wheel(1);s.Windows(new RuntimeObject{TypeName="BattlePlayerInfoWindow"});
        GameUi.Path.AddRange(new[]{new RuntimeObject{TypeName="UICom_AttrInfo"},s.Attributes});
        Input.Buttons[0]=Input.Down[0]=RuntimeObject.OnUi=true;FieldZoomUi.Tick();
        Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.PanIndicator);
        IndicatorWheel();WheelUi();HiddenEncounter();Assert(HiddenEncounterCases==2);PanUi();PresentationAndFrames();TurnTransitions();BlendLifetime();BranchGeometryAndOpacity();FocusAndEncounterRecovery();
        foreach(var cancel in new Action<Scene>[]{a=>Application.isFocused=false,a=>ModUi.IsOpen=true,a=>a.Root.Data["modalWaiting"]=true,a=>RuntimeObject.Dragging=true,a=>Input.touchCount=1,a=>FieldCameraReturnUi.ConsumedInput=true}){
            s=new Scene();s.Wheel(1);Input.Buttons[0]=true;cancel(s);FieldZoomUi.Tick();
            Assert(!FieldFreeCamera.PanAllowed&&FieldFreeCamera.Holding&&FieldCameraReturnUi.Visible);
        }
        FieldCameraReturnUi.ConsumedInput=false;
        // Shop/selection windows do not cancel a field-origin drag, even with modal/popup flags.
        foreach(var type in new[]{"LandShopWindow","CardWindow","SelectMonsterWindow","ChooseRoundCardWindow","UnknownWindow"}){
            s=new Scene();s.Wheel(1);heldBody=FieldFreeCamera.Camera!.Get("body")!;
            var writes=heldBody.Writes;var own=FieldFreeCamera.Camera;scans=Scans;
            Input.Buttons[0]=true;FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed);
            var interactive=new RuntimeObject{TypeName=type};
            s.Windows(new RuntimeObject{TypeName="TipsWindow"},new RuntimeObject{TypeName="OperateTimeWindow"},interactive);
            s.Root.Data["hasModalWindow"]=s.Root.Data["hasAnyPopup"]=true;
            int disposals=s.WindowStack.Disposals;s.Wheel(-100);
            Assert(!FieldFreeCamera.LastBlocked&&FieldFreeCamera.PanAllowed&&FieldFreeCamera.Camera==own&&FieldCameraReturnUi.Visible);
            Assert(heldBody.Writes==writes&&Scans==scans&&s.WindowStack.Disposals==disposals);
            s.Windows();FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.Camera==own);
            // No wheel replay after releasing the drag; Pan capture is checked in the production Pan fixture.
            Input.Buttons[0]=false;s.Finish();Assert(heldBody.Writes==writes&&FieldCameraReturnUi.Visible);
            FieldZoomUi.Clear();s=new Scene();s.Windows(interactive);s.Wheel(1);Assert(FieldFreeCamera.Holding&&FieldFreeCamera.PanAllowed&&s.Body.Writes==0);
        }
        foreach(var type in new[]{"ExpressionWindow","ExpressionListWindow","OperateTimeWindow","TipsWindow"}){
            s=new Scene();s.Windows(new RuntimeObject{TypeName=type});s.Wheel(-1);Assert(FieldFreeCamera.Holding);
            heldBody=FieldFreeCamera.Camera!.Get("body")!;s.Finish();
            var zoomedOut=heldBody.Field<Vector3>("m_FollowOffset").magnitude;Assert(zoomedOut>10);
            s.Wheel(1);s.Finish();Assert(heldBody.Field<Vector3>("m_FollowOffset").magnitude<zoomedOut);
            Input.Buttons[0]=true;RuntimeObject.OnUi=true;FieldZoomUi.Tick();Assert(FieldFreeCamera.PanAllowed&&FieldFreeCamera.PanOnUi);
        }
        foreach(var invalid in new Action<Scene>[] {
            a=>a.WindowStack.Data["Count"]=-1,a=>a.WindowStack.Data["Count"]=65,
            a=>{a.Windows(new RuntimeObject{TypeName="ExpressionWindow"});a.WindowStack.Data["Count"]=2;},
            a=>{a.Windows(new RuntimeObject{TypeName="ExpressionWindow"},new RuntimeObject{TypeName="ExpressionWindow"});a.WindowStack.Data["Count"]=1;},
            a=>a.Windows(new RuntimeObject[]{null!}),a=>RuntimeObject.Statics.Remove(RuntimeObject.FindClass("UI","UIManager"))}){
            s=new Scene();s.Wheel(1);invalid(s);FieldZoomUi.Tick();Assert(FieldFreeCamera.Holding&&FieldFreeCamera.PanAllowed&&FieldCameraReturnUi.Visible);
        }
        s=new Scene();s.Windows(new RuntimeObject{TypeName="LandShopWindow",Data=new(){{"visible",false}}},new RuntimeObject{TypeName="UnknownWindow",Dead=true});s.Wheel(1);Assert(FieldFreeCamera.Holding);
        foreach(var state in new[]{-1,4}){s=new Scene();s.Wheel(1);s.Free.Data["status"]=Obj(("value",state));FieldZoomUi.Tick();Assert(FieldFreeCamera.LastBlocked&&!FieldFreeCamera.PanAllowed);}
        // Compatibility blocks only once; cleanup must keep retrying on later blocked frames.
        s=new Scene();s.Wheel(1);heldBody=FieldFreeCamera.Camera!.Get("body")!;
        var cleanupStart=FieldFreeCamera.ClearAttempts;FieldFreeCamera.ClearFailures=4;
        Plugin.FieldZoom.Value=false;FieldZoomUi.Tick();
        Assert(Compatibility.Blocked&&Compatibility.Blocks==1&&Compatibility.CleanupFailures==1);
        Assert(FieldFreeCamera.Holding&&FieldFreeCamera.ClearAttempts==cleanupStart+2&&FieldFreeCamera.ClearFailures==2);
        Assert(heldBody.Field<Vector3>("m_FollowOffset").z==-10&&!FieldCameraReturnUi.Visible);
        var cleanupScans=Scans;var cleanupWrites=heldBody.Writes;
        Plugin.FieldZoom.Value=true;Input.mouseScrollDelta=new(0,1);
        for(var remaining=1;remaining>=0;remaining--){
            FieldZoomUi.Tick();Assert(FieldFreeCamera.Holding&&FieldFreeCamera.ClearFailures==remaining);
            Assert(Compatibility.Blocks==1&&Scans==cleanupScans&&heldBody.Writes==cleanupWrites);
        }
        FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&FieldFreeCamera.Camera==null);
        Assert(FieldFreeCamera.ClearAttempts==cleanupStart+5&&s.Offset.z==-10&&Scans==cleanupScans);
        FieldZoomUi.Tick();Assert(!FieldFreeCamera.Holding&&heldBody.Writes==cleanupWrites&&Scans==cleanupScans);
        Input.mouseScrollDelta=new();
        // Enter creates a distinct rendered-pose anchor while native Follow may still be damping.
        foreach(var pose in new[]{(NativeX:0f,RenderedX:20f,Depth:45f),(NativeX:20f,RenderedX:0f,Depth:45f)}){
            s=new Scene();s.Follow.Data["position"]=new Vector3(pose.NativeX,0,0);
            s.Camera.CameraTransform.position=new(pose.RenderedX,0,-10);s.Wheel(-100);
            Assert(FieldFreeCamera.Holding&&FieldFreeCamera.Camera!.Pointer!=s.Active.Pointer);
            heldBody=FieldFreeCamera.Camera!.Get("body")!;
            Assert(heldBody.Pointer!=s.Body.Pointer&&FieldFreeCamera.Camera!.Get("Follow")!.Get<Vector3>("position").x==pose.RenderedX);
            s.Finish();var applied=heldBody.Field<Vector3>("m_FollowOffset");
            Assert(Math.Abs(applied.z+pose.Depth)<.0001&&s.Offset.z==-10&&s.Body.Writes==0);
            var halfWidth=-applied.z*(16d/9);
            Assert(Math.Abs(-40-pose.RenderedX)<=halfWidth&&Math.Abs(40-pose.RenderedX)<=halfWidth);
            var writes=heldBody.Writes;s.Wheel(-100);s.Finish();Assert(heldBody.Writes==writes);
            FieldZoomUi.Clear();Assert(s.Offset.z==-10&&!FieldFreeCamera.Holding);
        }
        FieldCameraReturnUi.Available=false;
        s=new Scene();FieldZoomUi.Tick();FieldCameraReturnUi.ConsumedInput=true;s.Wheel(1);Assert(s.Body.Writes==0);
        FieldCameraReturnUi.ConsumedInput=false;
        foreach(var gate in new Action<Scene>[] {
            a=>a.Camera.Data["enabled"]=false,
            a=>a.Camera.CameraGame.activeInHierarchy=false,
            a=>RuntimeObject.Statics[RuntimeObject.FindClass("GameLogic","GameLogicManager")].Get("battle")!.Data["clientFinishReady"]=false
        }){s=new Scene();gate(s);s.Wheel(1);Assert(s.Body.Writes==0);}
        s=new Scene();Assert(FieldZoomUi.TryPanBounds(out var bounds)&&bounds.min.x==-40&&bounds.max.x==40);
        s.Land.Renderers[0].bounds=new(){min=new(float.NaN,0,0),max=new(1,1,1)};
        Assert(!FieldZoomUi.TryPanBounds(out _));
        AlreadyOutwardRecovery();
    }
}
}
