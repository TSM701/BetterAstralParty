#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Il2CppInterop.Runtime { public static class Il2CppType { public static Type Of<T>() => typeof(T); } }
namespace UnityEngine {
public struct Vector2 {
    public float x, y; public Vector2(float a, float b) { x = a; y = b; }
    public float sqrMagnitude => x*x+y*y;
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x-b.x, a.y-b.y);
}
public struct Vector3 {
    public float x, y, z; public Vector3(float a, float b, float c) { x=a; y=b; z=c; }
    public float sqrMagnitude => x*x+y*y+z*z;
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator *(Vector3 a, float s) => new(a.x*s,a.y*s,a.z*s);
}
public struct Quaternion { public float x,y,z,w; }
public struct Bounds { public Vector3 min,max; }
public class Object {
    static int next; public IntPtr Pointer; public bool Dead;
    public static readonly Dictionary<IntPtr,Object> Registry = new();
    protected Object() { Pointer=(IntPtr)(++next); Registry.Add(Pointer,this); }
    protected Object(IntPtr pointer) { if (!Registry.ContainsKey(pointer)) throw new Exception("Unknown native object"); Pointer=pointer; }
    public static implicit operator bool(Object? obj) => obj != null && !Registry[obj.Pointer].Dead;
    public static bool operator !(Object? obj) => !(bool)obj;
    public T Cast<T>() where T : Object => typeof(T)==typeof(ICinemachineCamera) ? (T)(Object)new ICinemachineCamera(Pointer) : (T)Registry[Pointer];
    public static void Destroy(Object value) {
        if (!value) throw new Exception("Destroyed object reused"); Registry[value.Pointer].Dead=true;
        if (value is GameObject game) foreach (var component in game.Components) component.Dead=true;
    }
}
public class Transform : Object {
    public GameObject gameObject; public Vector3 position,forward=new(0,0,1),right=new(1,0,0); public Quaternion rotation;
    public Transform(GameObject owner) { gameObject=owner; }
    public void SetPositionAndRotation(Vector3 p, Quaternion q) { position=p; rotation=q; }
}
public class GameObject : Object {
    public readonly string Name; public readonly Transform transform; public bool activeSelf=true;
    public readonly List<Component> Components = new(); public static bool FailAdd;
    public GameObject(string name) { Name=name; transform=new(this); }
    public void SetActive(bool active) { if (!this) throw new Exception("Dead GameObject write"); activeSelf=active; }
    public Component AddComponent(Type type) {
        if (FailAdd) { FailAdd=false; throw new InvalidOperationException("Injected AddComponent failure"); }
        Component component = type==typeof(CinemachineVirtualCamera) ? new CinemachineVirtualCamera(this)
            : type==typeof(CinemachineTransposer) ? new CinemachineTransposer(this)
            : throw new Exception("Unsupported component type");
        Components.Add(component); return component;
    }
}
public class Component : Object {
    public GameObject gameObject; public Transform transform => gameObject.transform;
    public Component(GameObject game) { gameObject=game; }
    public Component(IntPtr pointer) : base(pointer) { gameObject=((Component)Registry[pointer]).gameObject; }
}
public class Camera : Component { public Camera(GameObject game) : base(game) { } }
public static class Input {
    public static int touchCount; public static readonly bool[] Buttons=new bool[3],Down=new bool[3]; public static float Horizontal,Vertical;
    public static bool GetMouseButton(int i) => Buttons[i];
    public static bool GetMouseButtonDown(int i) => Down[i];
    public static float GetAxis(string name) => name switch { "Horizontal"=>Horizontal, "Vertical"=>Vertical, _=>throw new Exception("Unexpected input axis") };
}
public static class Application { public static bool isFocused=true; }
public static class Time { public static float deltaTime=1f/60f; }
}
namespace Cinemachine {
public struct LensSettings { public float FieldOfView,NearClipPlane,FarClipPlane,Dutch; }
public class ICinemachineCamera : UnityEngine.Object { public ICinemachineCamera(IntPtr pointer) : base(pointer) { } }
public class CinemachineBlend {
    public ICinemachineCamera? CamB; public bool IsComplete=true;
}
public class CinemachineVirtualCameraBase : Component {
    public enum StandbyUpdateMode { Never=0 }
    public CinemachineVirtualCameraBase(GameObject game) : base(game) { }
    public CinemachineVirtualCameraBase(IntPtr pointer) : base(pointer) { }
}
public class CinemachineVirtualCamera : CinemachineVirtualCameraBase {
    sealed class Data {
        public LensSettings Lens; public int Priority; public StandbyUpdateMode Standby; public Transform? Follow,Owner;
        public int Invalidations,Forces;
    }
    readonly Data data;
    public CinemachineVirtualCamera(GameObject game) : base(game) { data=new(); }
    public CinemachineVirtualCamera(IntPtr pointer) : base(pointer) { data=((CinemachineVirtualCamera)Registry[pointer]).data; }
    public LensSettings m_Lens { get=>data.Lens; set=>data.Lens=value; }
    public int Priority { get=>data.Priority; set=>data.Priority=value; }
    public StandbyUpdateMode m_StandbyUpdate { get=>data.Standby; set=>data.Standby=value; }
    public Transform? Follow { get=>data.Follow; set=>data.Follow=value; }
    public Transform GetComponentOwner() => data.Owner ??= new GameObject("cm").transform;
    public void InvalidateComponentPipeline() { data.Invalidations++; }
    public void ForceCameraPosition(Vector3 p, Quaternion q) { data.Forces++; transform.SetPositionAndRotation(p,q); }
    public int Forces=>data.Forces; public int Invalidations=>data.Invalidations;
}
public class CinemachineTransposer : Component {
    public enum BindingMode { WorldSpace=4 }
    sealed class Data { public BindingMode Binding; public Vector3 Offset; public float X,Y,Z; }
    readonly Data data;
    public CinemachineTransposer(GameObject game) : base(game) { data=new(); }
    public CinemachineTransposer(IntPtr pointer) : base(pointer) { data=((CinemachineTransposer)Registry[pointer]).data; }
    public BindingMode m_BindingMode { get=>data.Binding; set=>data.Binding=value; }
    public Vector3 m_FollowOffset { get=>data.Offset; set=>data.Offset=value; }
    public float m_XDamping { get=>data.X; set=>data.X=value; }
    public float m_YDamping { get=>data.Y; set=>data.Y=value; }
    public float m_ZDamping { get=>data.Z; set=>data.Z=value; }
}
public class CinemachineBrain : Component {
    public class BrainFrame {
        public int id; public bool FailNextRead; CinemachineBlend? value;
        public CinemachineBlend? blend { get { if(FailNextRead){FailNextRead=false;throw new InvalidOperationException("Injected frame read failure");}return value; } set=>this.value=value; }
    }
    sealed class Data { public List<BrainFrame> Frames=new(); public int Next=20,Acquires; public List<int> Releases=new(); public ICinemachineCamera? LastCamera; }
    readonly Data data; public static bool FailAcquire,FailRelease,InsertThenThrow,DuplicateInserted,UnreadableAfterInsert;
    public CinemachineBrain(GameObject game) : base(game) { data=new(); }
    public CinemachineBrain(IntPtr pointer) : base(pointer) { data=((CinemachineBrain)Registry[pointer]).data; }
    public List<BrainFrame> mFrameStack=>data.Frames;
    public int Acquires=>data.Acquires; public List<int> Releases=>data.Releases; public ICinemachineCamera? LastSetCamera=>data.LastCamera;
    public int SetCameraOverride(int id, ICinemachineCamera? a, ICinemachineCamera b,float weight,float dt) {
        if (!this || id!=-1 || a!=null || weight!=1 || dt!=-1 || !b) throw new Exception("Invalid native override contract");
        data.Acquires++;data.LastCamera=b; if (FailAcquire) { FailAcquire=false; return -1; }
        id=data.Next++; data.Frames.Add(new(){id=id,blend=new(){CamB=b}});
        if(InsertThenThrow) {
            InsertThenThrow=false;
            data.Frames.Add(new(){id=900,blend=new(){CamB=data.Frames[0].blend!.CamB},FailNextRead=UnreadableAfterInsert});UnreadableAfterInsert=false;
            if(DuplicateInserted){DuplicateInserted=false;data.Frames.Add(new(){id=data.Next++,blend=new(){CamB=b}});}
            throw new InvalidOperationException("Injected failure after native frame insertion");
        }
        return id;
    }
    public void ReleaseCameraOverride(int id) {
        if (!this) throw new Exception("Dead brain release"); data.Releases.Add(id);
        if (FailRelease) { FailRelease=false; throw new InvalidOperationException("Injected release failure"); }
        for (var i=data.Frames.Count-1;i>0;i--) if (data.Frames[i].id==id) { data.Frames.RemoveAt(i); return; }
    }
}
}
namespace FreeCameraTest {
internal sealed class RuntimeObject {
    static int next=100000; public IntPtr Pointer; public readonly string Kind;
    public readonly Dictionary<string,object?> Data=new();
    public static readonly Dictionary<string,IntPtr> Classes=new();
    public static readonly Dictionary<IntPtr,RuntimeObject?> Singletons=new();
    public static readonly List<string> Calls=new(); public static RuntimeObject? SwitchedCharacter;
    public string TypeName => UnityEngine.Object.Registry.TryGetValue(Pointer,out var native) ? native.GetType().Name : Kind;
    public RuntimeObject(string kind) { Kind=kind; Pointer=(IntPtr)(++next); }
    public RuntimeObject(IntPtr pointer) { if (!UnityEngine.Object.Registry.ContainsKey(pointer)) throw new Exception("Unknown component pointer"); Pointer=pointer; Kind="Component"; }
    object? Read(string member,string name,Type expected) {
        var type=(Kind,member,name) switch {
            ("Scene","Field","freeCamera")=>typeof(RuntimeObject),
            ("Free","Field","CameraSensitivity" or "CameraSpeed")=>typeof(float),
            ("Logic","Get","battle")=>typeof(RuntimeObject),
            ("BattlePlayer","Field","CharacterInst")=>typeof(RuntimeObject),
            ("Character","Field","vCamera")=>typeof(RuntimeObject),
            ("Stage","Get","touchPosition")=>typeof(Vector2),
            ("Box","Value","value")=>Data["value"]!.GetType(),
            _=>throw new Exception("Unsupported native reader "+Kind+"."+member+"."+name)
        };
        if (expected!=type || !Data.ContainsKey(name) || Data[name]!=null && Data[name]!.GetType()!=type) throw new Exception("Native reader type mismatch");
        return Data[name];
    }
    public RuntimeObject? Field(string name)=>(RuntimeObject?)Read("Field",name,typeof(RuntimeObject));
    public T Field<T>(string name) where T:unmanaged=>(T)Read("Field",name,typeof(T))!;
    public RuntimeObject? Get(string name)=>(RuntimeObject?)Read("Get",name,typeof(RuntimeObject));
    public T Get<T>(string name) where T:unmanaged=>(T)Read("Get",name,typeof(T))!;
    public T Value<T>() where T:unmanaged=>(T)Read("Value","value",typeof(T))!;
    public static IntPtr FindClass(string ns,string name) {
        var key=ns+"."+name;
        if (key is not ("Core.Camera.CameraManager" or "GameLogic.GameLogicManager" or "Core.GameSettings" or "FairyGUI.Stage" or "FairyGUI.UIConfig")) throw new Exception("Unexpected native class "+key);
        if (!Classes.TryGetValue(key,out var ptr)) Classes[key]=ptr=(IntPtr)(++next); return ptr;
    }
    public static RuntimeObject? StaticField(IntPtr type,string name) {
        var key=Classes.Single(x=>x.Value==type).Key;
        if (key=="FairyGUI.UIConfig" && name=="clickDragSensitivity") return Box(Check.DragThreshold);
        if (key=="Core.GameSettings") {
            if (name is not ("MouseControl" or "KeyControl")) throw new Exception("Unexpected native setting");
            return Box(name=="MouseControl" ? Check.MouseControl : Check.KeyControl);
        }
        if (name!="_inst") throw new Exception("Unexpected singleton field"); return Singletons.GetValueOrDefault(type);
    }
    public static RuntimeObject? StaticCall(IntPtr type,string name,params object?[] args) {
        if (type!=FindClass("FairyGUI","Stage") || args.Length!=0) throw new Exception("Unexpected static native call");
        return name switch { "get_inst"=>Check.Stage, "get_touchScreen"=>Box(Check.TouchScreen), _=>throw new Exception("Unexpected Stage reader") };
    }
    public RuntimeObject? Call(string name,params object?[] args) {
        if (Kind=="Stage" && name=="CancelClick" && args.Length==1 && args[0] is int id && id==0
            && !Check.TouchScreen && Input.touchCount==0 && !Input.Down[0] && !Input.Buttons[1] && !Input.Buttons[2])
        { Calls.Add("CancelClick");Check.ClickCancelled=true;return null; }
        if (Kind=="Battle" && name=="GetCurrentPlayer" && args.Length==0) return Check.CurrentPlayer;
        if (Kind=="Manager" && name=="CancelFreeStatus" && args.Length==0) { Calls.Add("Cancel"); return null; }
        if (Kind=="Manager" && name=="SwitchCamera" && args.Length==1 && args[0] is RuntimeObject character && character.Kind=="CharacterCamera") { Calls.Add("Switch"); SwitchedCharacter=character; return null; }
        if (Kind=="Free" && name=="GetHostStatus" && args.Length==0) return Box(Check.Host);
        throw new Exception("Unexpected native call "+Kind+"."+name);
    }
    static RuntimeObject Box(object value) { var box=new RuntimeObject("Box");box.Data["value"]=value;return box; }
}
internal static class FieldZoomUi {
    public static int Clears; public static bool BoundsAvailable=true; public static Bounds Bounds=new(){min=new(-10,-10,-10),max=new(10,10,10)};
    internal static bool TryPanBounds(out Bounds bounds) { bounds=Bounds; return BoundsAvailable; }
    internal static void Clear() { Clears++; FieldFreeCamera.Clear(); }
}
__PRODUCTION_SOURCE__
public static class Check {
    public static bool TouchScreen,ClickCancelled; public static int DragThreshold=2;
    public static bool MouseControl=true,KeyControl,Host; internal static RuntimeObject Stage=new("Stage"); internal static RuntimeObject? CurrentPlayer;
    static int assertions; static void Assert(bool okay,[System.Runtime.CompilerServices.CallerLineNumber]int line=0) { assertions++; if (!okay) throw new Exception("Free camera regression at fixture line "+line); }
    static bool Equal(Vector3 a,Vector3 b)=>a.x==b.x && a.y==b.y && a.z==b.z;
    static RuntimeObject Obj(string kind,params (string,object?)[] values) { var result=new RuntimeObject(kind);foreach(var item in values)result.Data[item.Item1]=item.Item2;return result; }
    sealed class Scene {
        public readonly RuntimeObject SceneObject,Free,CharacterCamera,PlayerRig;
        public readonly CinemachineBrain Brain=new(new GameObject("native brain"));
        public readonly CinemachineVirtualCamera NativeCamera=new(new GameObject("native camera")),CurrentCamera=new(new GameObject("current player camera"));
        public readonly CinemachineTransposer Body=new(new GameObject("native body"));
        public readonly UnityEngine.Camera Output=new(new GameObject("native output"));
        public readonly Component NativeFree=new(new GameObject("native free anchor"));
        public readonly RuntimeObject BrainObject;
        public Scene() {
            FieldFreeCamera.Clear(); RuntimeObject.Calls.Clear();RuntimeObject.SwitchedCharacter=null;FieldZoomUi.Clears=0;
            MouseControl=true;KeyControl=Host=false;Input.touchCount=0;Input.Horizontal=Input.Vertical=0;Array.Clear(Input.Buttons);Array.Clear(Input.Down);Application.isFocused=true;Time.deltaTime=1f/60;
            TouchScreen=ClickCancelled=false;DragThreshold=2;
            FieldZoomUi.BoundsAvailable=true;FieldZoomUi.Bounds=new(){min=new(-10,-10,-10),max=new(10,10,10)};
            Stage=Obj("Stage",("touchPosition",new Vector2()));
            NativeCamera.m_Lens=new(){FieldOfView=60,NearClipPlane=.3f,FarClipPlane=1500};NativeCamera.Priority=10;
            Body.m_FollowOffset=new(0,4,-6);Body.m_BindingMode=CinemachineTransposer.BindingMode.WorldSpace;Body.m_XDamping=.7f;Body.m_YDamping=.4f;Body.m_ZDamping=.6f;
            Output.transform.position=new(0,4,-6);Output.transform.rotation=new(){w=.5f};
            SceneObject=Obj("Scene",("freeCamera",new RuntimeObject(NativeCamera.Pointer)));BrainObject=new(Brain.Pointer);
            Free=Obj("Free",("CameraSensitivity",.5f),("CameraSpeed",10f));Free.Pointer=NativeFree.Pointer;
            PlayerRig=new(CurrentCamera.Pointer);CharacterCamera=Obj("CharacterCamera");
            CurrentPlayer=Obj("BattlePlayer",("CharacterInst",Obj("Character",("vCamera",CharacterCamera))));
            RuntimeObject.Singletons[RuntimeObject.FindClass("Core.Camera","CameraManager")]=Obj("Manager");
            RuntimeObject.Singletons[RuntimeObject.FindClass("GameLogic","GameLogicManager")]=Obj("Logic",("battle",Obj("Battle")));
            Brain.mFrameStack.Add(new(){id=0,blend=new(){CamB=NativeCamera.Cast<ICinemachineCamera>()}});
        }
        public void Enter()=>FieldFreeCamera.Enter(SceneObject,BrainObject,new RuntimeObject(NativeCamera.Pointer),new RuntimeObject(Body.Pointer),Output);
        public bool Update(bool blocked=false)=>FieldFreeCamera.Update(SceneObject,BrainObject,Free,PlayerRig,blocked);
        public CinemachineVirtualCamera Own=>(CinemachineVirtualCamera)UnityEngine.Object.Registry[FieldFreeCamera.Camera!.Pointer];
        public Transform Anchor=>Own.Follow!;
        public CinemachineTransposer OwnBody=>Own.GetComponentOwner().gameObject.Components.OfType<CinemachineTransposer>().Single();
        public int Id=>Brain.mFrameStack.Last().id;
        public void Point(float x,float y) => Stage.Data["touchPosition"]=new Vector2(x,y);
        public void DownAt(float x,float y) { Point(x,y);Input.Buttons[0]=Input.Down[0]=true; }
        public void Pan(bool allowed=true,bool onUi=false,bool fieldIndicator=false) { FieldFreeCamera.Pan(Free,allowed,onUi,fieldIndicator);Array.Clear(Input.Down); }
        public void Release() { Input.Buttons[0]=false;Pan(); }
    }
    static void Ownership() {
        var s=new Scene();s.Enter();var own=s.Own;var anchor=s.Anchor;int id=s.Id;
        Assert(FieldFreeCamera.Holding && s.Brain.mFrameStack.Count==2 && s.Brain.Acquires==1);
        Assert(own.Priority==int.MinValue && own.m_Lens.FieldOfView==60 && own.m_Lens.NearClipPlane==.3f && own.m_Lens.FarClipPlane==1500);
        Assert(s.NativeCamera.Priority==10 && Equal(s.Body.m_FollowOffset,new(0,4,-6)));
        Assert(Equal(anchor.position,new()) && Equal(own.transform.position,s.Output.transform.position) && own.Forces==1 && own.Invalidations==1);
        Assert(s.OwnBody.m_XDamping==.7f && s.OwnBody.m_YDamping==.4f && s.OwnBody.m_ZDamping==.6f && Equal(s.OwnBody.m_FollowOffset,s.Body.m_FollowOffset));
        s.Enter();Assert(s.Brain.Acquires==1);
        s.Brain.mFrameStack[0].blend!.CamB=s.CurrentCamera.Cast<ICinemachineCamera>();
        s.Brain.mFrameStack[0].blend!.IsComplete=false;
        Assert(s.Update() && s.Id==id && s.Brain.Acquires==1 && FieldFreeCamera.Holding);
        s.Brain.mFrameStack[0].blend!.IsComplete=true;
        anchor.position=new(3,0,4);s.OwnBody.m_FollowOffset=new(0,8,-12);
        Assert(!s.Update(blocked:true) && FieldFreeCamera.Holding && s.Brain.mFrameStack.Count==1 && s.Brain.Releases.SequenceEqual(new[]{id}));
        Assert(!own.gameObject.activeSelf && Equal(anchor.position,new(3,0,4)) && Equal(s.OwnBody.m_FollowOffset,new(0,8,-12)));
        s.Brain.mFrameStack[0].blend!.IsComplete=false;Assert(!s.Update() && s.Brain.Acquires==1);
        s.Brain.mFrameStack[0].blend!.IsComplete=true;Assert(s.Update() && s.Brain.Acquires==2 && s.Own.Pointer==own.Pointer && Equal(anchor.position,new(3,0,4)));
        // Ordinary field effects select other actors/show cameras and blend underneath us.
        foreach(var name in new[]{"other character camera","card/show camera"}){
            var effect=new CinemachineVirtualCamera(new GameObject(name)){Priority=30};
            s.Brain.mFrameStack[0].blend!.CamB=effect.Cast<ICinemachineCamera>();
            s.Brain.mFrameStack[0].blend!.IsComplete=false;
            int effectsId=s.Id,effectsAcquires=s.Brain.Acquires,effectsReleases=s.Brain.Releases.Count;
            Assert(s.Update() && s.Id==effectsId && s.Brain.Acquires==effectsAcquires && s.Brain.Releases.Count==effectsReleases);
            Assert(own.gameObject.activeSelf && Equal(anchor.position,new(3,0,4)) && Equal(s.OwnBody.m_FollowOffset,new(0,8,-12)) && effect.Priority==30);
            s.Brain.mFrameStack[0].blend!.IsComplete=true;
            Assert(s.Update() && s.Id==effectsId && Equal(anchor.position,new(3,0,4)));
        }
        var unknown=new Component(new GameObject("unsupported native cinematic"));
        s.Brain.mFrameStack[0].blend!.CamB=new ICinemachineCamera(unknown.Pointer);
        Assert(!s.Update() && FieldFreeCamera.Holding && s.Brain.mFrameStack.Count==1 && !own.gameObject.activeSelf);
        var dead=new CinemachineVirtualCamera(new GameObject("destroyed native camera"));UnityEngine.Object.Destroy(dead);
        s.Brain.mFrameStack[0].blend!.CamB=dead.Cast<ICinemachineCamera>();Assert(!s.Update() && s.Brain.mFrameStack.Count==1);
        s.Brain.mFrameStack[0].blend!.CamB=s.CurrentCamera.Cast<ICinemachineCamera>();Assert(s.Update() && Equal(anchor.position,new(3,0,4)));
        int second=s.Id;var foreign=new CinemachineBrain.BrainFrame{id=900,blend=new(){CamB=s.NativeCamera.Cast<ICinemachineCamera>()}};
        s.Brain.mFrameStack.Add(foreign);Assert(!s.Update() && s.Brain.mFrameStack.Count==2 && s.Brain.mFrameStack.Contains(foreign) && s.Brain.Releases.Last()==second);
        int acquired=s.Brain.Acquires;Assert(!s.Update() && s.Brain.Acquires==acquired);s.Brain.mFrameStack.Remove(foreign);Assert(s.Update());
        id=s.Id;s.Brain.mFrameStack[1].id=901;Assert(!s.Update() && s.Brain.mFrameStack[1].id==901 && s.Brain.Releases.Last()==id);
        FieldFreeCamera.Clear();Assert(!FieldFreeCamera.Holding && !own && !anchor.gameObject && s.NativeCamera && s.CurrentCamera && s.Brain.mFrameStack.Any(f=>f.id==901));
    }
    static void ReturnAndCleanup() {
        var s=new Scene();s.Enter();var own=s.Own;FieldFreeCamera.Return();Assert(FieldFreeCamera.Holding && s.Brain.mFrameStack.Count==1);
        Host=true;Assert(!s.Update() && RuntimeObject.Calls.Count==0 && FieldZoomUi.Clears==0);
        Host=false;
        s.Brain.mFrameStack[0].blend!.IsComplete=false;Assert(!s.Update() && RuntimeObject.Calls.Count==0 && FieldZoomUi.Clears==0);
        s.Brain.mFrameStack[0].blend!.IsComplete=true;
        var current=CurrentPlayer;CurrentPlayer=null;Assert(!s.Update() && RuntimeObject.Calls.Count==0);CurrentPlayer=current;
        Assert(!s.Update() && !FieldFreeCamera.Holding && RuntimeObject.Calls.SequenceEqual(new[]{"Cancel","Switch"}) && RuntimeObject.SwitchedCharacter==s.CharacterCamera && FieldZoomUi.Clears==1);
        Assert(!s.Update() && RuntimeObject.Calls.Count==2 && !own);
        s=new Scene();s.Enter();own=s.Own;var anchor=s.Anchor;Assert(!FieldFreeCamera.Update(Obj("Scene"),s.BrainObject,s.Free,s.PlayerRig,false));Assert(!FieldFreeCamera.Holding && !own && !anchor.gameObject);
        s=new Scene();s.Enter();own=s.Own;UnityEngine.Object.Destroy(s.Brain);FieldFreeCamera.Clear();Assert(!FieldFreeCamera.Holding && !own && s.Brain.Releases.Count==0);
        s=new Scene();s.Enter();own=s.Own;UnityEngine.Object.Destroy(s.Anchor.gameObject);Assert(!s.Update() && !FieldFreeCamera.Holding && !own);
        s=new Scene();s.Enter();own=s.Own;UnityEngine.Object.Destroy(own);Assert(!s.Update() && !FieldFreeCamera.Holding && s.Brain.mFrameStack.Count==1);
        s=new Scene();GameObject.FailAdd=true;try {s.Enter();throw new Exception("Creation fault swallowed");}catch(InvalidOperationException){}Assert(!FieldFreeCamera.Holding && s.Brain.Acquires==0);
        s=new Scene();CinemachineBrain.FailAcquire=true;try{s.Enter();throw new Exception("Acquire fault swallowed");}catch(InvalidOperationException){}Assert(!FieldFreeCamera.Holding && s.Brain.mFrameStack.Count==1);
        s=new Scene();s.Enter();own=s.Own;int id=s.Id;CinemachineBrain.FailRelease=true;
        try{FieldFreeCamera.Clear();throw new Exception("Release fault swallowed");}catch(InvalidOperationException){}
        Assert(FieldFreeCamera.Holding && own && s.Brain.mFrameStack.Count==2);
        FieldFreeCamera.Clear();Assert(!FieldFreeCamera.Holding && !own && s.Brain.mFrameStack.Count==1 && s.Brain.Releases.SequenceEqual(new[]{id,id}));
        // Native SetCameraOverride inserts its frame before EnsureStarted can throw.
        s=new Scene();CinemachineBrain.InsertThenThrow=true;
        try{s.Enter();throw new Exception("Partial acquire fault swallowed");}catch(InvalidOperationException){}
        own=(CinemachineVirtualCamera)UnityEngine.Object.Registry[s.Brain.LastSetCamera!.Pointer];
        Assert(!FieldFreeCamera.Holding && !own && s.Brain.mFrameStack.Count==2 && s.Brain.mFrameStack[1].id==900 && s.Brain.Releases.SequenceEqual(new[]{20}));
        s=new Scene();CinemachineBrain.InsertThenThrow=true;CinemachineBrain.FailRelease=true;
        try{s.Enter();throw new Exception("Partial acquire release fault swallowed");}catch(InvalidOperationException){}
        own=s.Own;Assert(FieldFreeCamera.Holding && own && s.Brain.mFrameStack.Count==3);
        FieldFreeCamera.Clear();Assert(!FieldFreeCamera.Holding && !own && s.Brain.mFrameStack.Count==2 && s.Brain.mFrameStack[1].id==900 && s.Brain.Releases.SequenceEqual(new[]{20,20}));
        s=new Scene();CinemachineBrain.InsertThenThrow=true;CinemachineBrain.UnreadableAfterInsert=true;
        try{s.Enter();throw new Exception("Partial cleanup read fault swallowed");}catch(InvalidOperationException){}
        own=s.Own;Assert(FieldFreeCamera.Holding && own && s.Brain.Releases.Count==0);
        FieldFreeCamera.Clear();Assert(!FieldFreeCamera.Holding && !own && s.Brain.mFrameStack.Count==2 && s.Brain.mFrameStack[1].id==900 && s.Brain.Releases.SequenceEqual(new[]{20}));
        s=new Scene();CinemachineBrain.InsertThenThrow=true;CinemachineBrain.DuplicateInserted=true;
        try{s.Enter();throw new Exception("Ambiguous cleanup fault swallowed");}catch(InvalidOperationException){}
        own=s.Own;Assert(FieldFreeCamera.Holding && own && s.Brain.Releases.Count==0 && s.Brain.mFrameStack.Count==4);
        s.Brain.mFrameStack.RemoveAt(3);FieldFreeCamera.Clear();
        Assert(!FieldFreeCamera.Holding && !own && s.Brain.mFrameStack.Count==2 && s.Brain.mFrameStack[1].id==900 && s.Brain.Releases.SequenceEqual(new[]{20}));
    }
    static void Pan() {
        // Icon clicks/jitter retain native popup; held movement cancels exactly desktop click 0 once.
        var icon=new Scene();icon.Enter();icon.DownAt(100,100);icon.Pan(onUi:true,fieldIndicator:true);
        Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled&&RuntimeObject.Calls.Count==0);
        icon.Point(100,100);icon.Pan(onUi:true);Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled);
        icon.Point(102,100);icon.Pan(onUi:true);Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled);
        icon.Release();Assert(!ClickCancelled);
        icon=new Scene();icon.Enter();icon.DownAt(100,100);icon.Pan(onUi:true,fieldIndicator:true);
        icon.Point(103,100);icon.Pan(onUi:true);Assert(Equal(icon.Anchor.position,new(-1.5f,0,0))&&ClickCancelled&&RuntimeObject.Calls.SequenceEqual(new[]{"CancelClick"}));
        icon.Point(100,100);icon.Pan(onUi:true);Assert(Equal(icon.Anchor.position,new())&&ClickCancelled&&RuntimeObject.Calls.Count==1);
        icon.Point(104,104);icon.Pan(onUi:false);Assert(Equal(icon.Anchor.position,new(-2,0,2))&&RuntimeObject.Calls.Count==1);
        icon.Release();Assert(ClickCancelled&&RuntimeObject.Calls.Count==1);
        foreach(var gate in new Action[]{()=>TouchScreen=true,()=>Input.touchCount=1,()=>Input.Buttons[1]=true,()=>Input.Buttons[2]=true}){
            icon=new Scene();icon.Enter();gate();icon.DownAt(100,100);icon.Pan(onUi:true,fieldIndicator:true);
            icon.Point(110,110);icon.Pan(onUi:true);Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled&&RuntimeObject.Calls.Count==0);
        }
        icon=new Scene();icon.Enter();icon.DownAt(100,100);icon.Pan(onUi:true,fieldIndicator:true);
        icon.Point(110,110);icon.Pan(allowed:false,onUi:true);icon.Point(112,112);icon.Pan();Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled);
        // A dragged ancestor/classification is blocked upstream; an ordinary UI hold cannot re-arm on departure.
        icon=new Scene();icon.Enter();icon.DownAt(100,100);icon.Pan(onUi:true);
        icon.Point(110,110);icon.Pan(onUi:false);Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled);
        icon=new Scene();icon.Enter();KeyControl=true;Input.Horizontal=1;icon.Pan(onUi:true,fieldIndicator:true);Assert(Equal(icon.Anchor.position,new()));
        icon=new Scene();icon.Enter();DragThreshold=-1;icon.DownAt(100,100);icon.Pan(onUi:true,fieldIndicator:true);icon.Point(110,110);icon.Pan(onUi:true);
        Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled);
        icon=new Scene();icon.Enter();DragThreshold=0;icon.DownAt(100,100);icon.Pan(onUi:true,fieldIndicator:true);icon.Pan(onUi:true);
        Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled);icon.Point(101,100);icon.Pan(onUi:true);Assert(ClickCancelled);
        icon=new Scene();icon.Enter();icon.DownAt(100,100);icon.Pan(onUi:true,fieldIndicator:true);icon.Point(float.NaN,100);icon.Pan(onUi:true);
        Assert(Equal(icon.Anchor.position,new())&&!ClickCancelled);
        var s=new Scene();s.Enter();s.DownAt(100,100);s.Pan();Assert(Equal(s.Anchor.position,new()));
        s.Point(104,106);s.Pan();Assert(Equal(s.Anchor.position,new(-2,0,3)));
        s.Point(106,108);s.Pan(onUi:true);Assert(Equal(s.Anchor.position,new(-3,0,4)));
        s.Point(108,110);s.Pan();Assert(Equal(s.Anchor.position,new(-4,0,5)));
        s.Point(1000,1000);s.Pan();Assert(Equal(s.Anchor.position,new(-10,0,10)));
        foreach(var gate in new Action[]{()=>Application.isFocused=false,()=>Input.touchCount=1}){
            s=new Scene();s.Enter();s.DownAt(10,10);s.Pan();gate();s.Point(100,100);s.Pan();Assert(Equal(s.Anchor.position,new()));
            Application.isFocused=true;Input.touchCount=0;s.Pan();Assert(Equal(s.Anchor.position,new()));s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new()));
            s.Release();s.DownAt(102,102);s.Pan();s.Point(104,104);s.Pan();Assert(Equal(s.Anchor.position,new(-1,0,1)));
        }
        // UI-origin and native-card-drag-origin holds cannot arm after entering empty field.
        s=new Scene();s.Enter();s.DownAt(10,10);s.Pan();s.Point(12,12);s.Pan();
        s.DownAt(100,100);s.Pan(onUi:true);Assert(Equal(s.Anchor.position,new(-1,0,1)));
        s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new(-1,0,1)));
        foreach(var origin in new[]{"ui","native-card-drag"}){
            s=new Scene();s.Enter();s.DownAt(10,10);s.Pan(allowed:origin!="native-card-drag",onUi:origin=="ui");s.Point(100,100);s.Pan();s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new()));
            s.Release();s.DownAt(102,102);s.Pan();s.Point(104,104);s.Pan();Assert(Equal(s.Anchor.position,new(-1,0,1)));
        }
        // The production caller allows field UI/host transitions; an acquired world drag stays continuous.
        foreach(var transition in new[]{"shop","target-selection","precombat","field-action","other-player-turn"}){
            s=new Scene();s.Enter();s.DownAt(10,10);s.Pan();s.Point(12,12);s.Pan();
            s.Point(14,14);s.Pan(onUi:true);s.Point(16,16);s.Pan(onUi:true);s.Point(18,18);s.Pan(onUi:false);
            Assert(Equal(s.Anchor.position,new(-4,0,4))&&!ClickCancelled);
            s.Release();s.DownAt(100,100);s.Pan(onUi:true);s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new(-4,0,4)));
            s.Release();s.DownAt(102,102);s.Pan();s.Point(104,104);s.Pan();Assert(Equal(s.Anchor.position,new(-5,0,5)));
        }
        // A world-origin gesture cancelled by real input ownership must also wait for release.
        foreach(var owner in new[]{"settings","scroll-pane","text-input","native-card-drag","return-button"}){
            s=new Scene();s.Enter();s.DownAt(10,10);s.Pan();s.Point(12,12);s.Pan();
            s.Point(100,100);s.Pan(allowed:false);s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new(-1,0,1)));
            s.Release();s.DownAt(102,102);s.Pan();s.Point(104,104);s.Pan();Assert(Equal(s.Anchor.position,new(-2,0,2)));
        }
        // Keyboard movement needs no pointer-origin gesture; native keyboard/mouse settings are never changed.
        s=new Scene();s.Enter();KeyControl=true;Input.Horizontal=1;s.Pan();Assert(s.Anchor.position.x>0&&KeyControl&&MouseControl);
        var beforeKeys=s.Anchor.position;s.Pan(allowed:false);Assert(Equal(s.Anchor.position,beforeKeys)&&KeyControl&&MouseControl);
        // A world-origin gesture cancelled by forbidden input must also wait for release.
        s=new Scene();s.Enter();s.DownAt(0,0);s.Pan();s.Point(2,2);s.Pan();Assert(Equal(s.Anchor.position,new(-1,0,1)));
        s.Point(100,100);s.Pan(false);s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new(-1,0,1)));
        s.Release();s.DownAt(102,102);s.Pan();s.Point(104,104);s.Pan();Assert(Equal(s.Anchor.position,new(-2,0,2)));
        s=new Scene();s.Enter();Input.Buttons[0]=true;s.Point(100,100);s.Pan();s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new()));
        s=new Scene();s.Enter();MouseControl=false;s.DownAt(100,100);s.Pan();Assert(Equal(s.Anchor.position,new()));
        MouseControl=true;s.Point(102,102);s.Pan();Assert(Equal(s.Anchor.position,new()));MouseControl=false;
        KeyControl=true;Input.Horizontal=1;Input.Vertical=-1;Time.deltaTime=1;s.Pan();Assert(Equal(s.Anchor.position,new(1,0,-1)));
        s.Pan(onUi:true);Assert(Equal(s.Anchor.position,new(1,0,-1)));
        KeyControl=false;s.Pan();Assert(Equal(s.Anchor.position,new(1,0,-1)));
        foreach(var invalid in new[]{float.NaN,float.PositiveInfinity,-1f,0f}){
            s=new Scene();s.Enter();KeyControl=true;Input.Horizontal=1;Time.deltaTime=invalid;s.Pan();Assert(Equal(s.Anchor.position,new()));
            Time.deltaTime=1;s.Free.Data["CameraSpeed"]=invalid;s.Pan();Assert(Equal(s.Anchor.position,new()));
        }
        s=new Scene();s.Enter();KeyControl=true;Input.Horizontal=float.NaN;s.Pan();Assert(Equal(s.Anchor.position,new()));
        Input.Horizontal=1;FieldZoomUi.BoundsAvailable=false;s.Pan();Assert(Equal(s.Anchor.position,new()));
        FieldZoomUi.BoundsAvailable=true;s.Update(true);s.Pan();Assert(Equal(s.Anchor.position,new()));
        FieldFreeCamera.Clear();
    }
    static void RenderedOrientation() {
        foreach(var dutch in new[]{-8f,5f}) {
            var s=new Scene();var lens=s.NativeCamera.m_Lens;lens.Dutch=dutch;s.NativeCamera.m_Lens=lens;
            var half=dutch*MathF.PI/360;var rendered=new Quaternion{z=MathF.Sin(half),w=MathF.Cos(half)};
            s.Output.transform.rotation=rendered;s.Enter();
            Assert(s.NativeCamera.m_Lens.Dutch==dutch && s.Own.m_Lens.Dutch==0);
            Assert(s.Own.m_Lens.FieldOfView==lens.FieldOfView && s.Own.m_Lens.NearClipPlane==lens.NearClipPlane && s.Own.m_Lens.FarClipPlane==lens.FarClipPlane);
            Assert(s.Own.transform.rotation.z==rendered.z && s.Own.transform.rotation.w==rendered.w && s.Own.Forces==1);
            FieldFreeCamera.Clear();Assert(s.NativeCamera.m_Lens.Dutch==dutch);
        }
    }
    public static void Run() { Ownership();ReturnAndCleanup();Pan();RenderedOrientation();Console.WriteLine("Free-camera production assertions: "+assertions); }
}
}
