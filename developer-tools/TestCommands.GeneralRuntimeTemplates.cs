namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private const string GeneralPingFocus0 = """""""
namespace UnityEngine {
    public enum KeyCode { LeftAlt, RightAlt, Tab }
    public static class Application { public static bool isFocused = true; }
    public static class Input {
        public static bool Alt, Right, Tab;
        public static bool GetKey(KeyCode k) => k == KeyCode.Tab ? Tab : k == KeyCode.LeftAlt ? Alt : Right;
    }
}
static class Compatibility {
    public static bool Allowed(string s) => true;
    public static void Block(string s, Exception ex) { throw ex; }
}
static class Plugin { internal static class PingFocus { internal static bool Value=true; } }
class RuntimeObject {
    public static bool Active, Present = true;
    public static bool Target, Hover;
    public static int Exits, Sent, Timer;
    public static IntPtr FindClass(string ns, string n) => Present ? new IntPtr(1) : IntPtr.Zero;
    public static RuntimeObject StaticField(IntPtr c, string n) => new RuntimeObject();
    public RuntimeObject Field(string n) => this;
    public T Get<T>(string n) => (T)(object)Active;
    public RuntimeObject Get(string n) => this;
    public void SetField(string name, RuntimeObject? value) {
        if (name != "_currentTarget" || value != null) throw new Exception("Unexpected field mutation");
        Target = false;
    }
    public void Call(string n) {
        if (n == "ExitCurrentHover") { Hover = false; Timer = 0; return; }
        if (n != "ExitMark") throw new Exception(n);
        if (Target) Sent++; // Native MarkExit -> SelectedCurrentTarget -> OnMarkSelected.
        Target = false; Active = false; Exits++;
    }
}
public static class PingFocusCheck {
    static void Check(bool pass) { if(!pass) throw new Exception("Ping focus regression"); }
    static void Reset() {
        Plugin.PingFocus.Value=true; PingFocusGuard.Reset();
        RuntimeObject.Active=RuntimeObject.Target=RuntimeObject.Hover=false; RuntimeObject.Present=true;
        RuntimeObject.Exits=RuntimeObject.Sent=RuntimeObject.Timer=0;
        Input.Alt=Input.Right=Input.Tab=false; Application.isFocused=true;
    }
    public static void Run() {
        Reset();
        RuntimeObject.Target = true; new RuntimeObject().Call("ExitMark");
        Check(RuntimeObject.Sent == 1); // Prove the old close-only path sends a ping.
        RuntimeObject.Sent = 0;
        foreach (var target in new[] { "card", "relic", "skill", "task", "player", "land" }) {
            RuntimeObject.Active = RuntimeObject.Target = RuntimeObject.Hover = true;
            RuntimeObject.Timer = 1;
            Input.Alt = Input.Tab = true; PingFocusGuard.Tick();
            Check(!RuntimeObject.Active && !RuntimeObject.Target && !RuntimeObject.Hover && RuntimeObject.Timer == 0 && RuntimeObject.Sent == 0);
            Input.Alt = Input.Tab = false; PingFocusGuard.Tick();
        }
        RuntimeObject.Target = true; new RuntimeObject().Call("ExitMark");
        Check(RuntimeObject.Sent == 1); // Ordinary native confirmation is unchanged.
        RuntimeObject.Sent = 0;
        RuntimeObject.Active = true; Input.Alt = true; PingFocusGuard.Tick();
        Check(RuntimeObject.Active); // Standalone Alt remains native.
        Input.Tab = true; PingFocusGuard.Tick(); Check(!RuntimeObject.Active);
        Input.Tab = false; RuntimeObject.Active = true; PingFocusGuard.Tick(); Check(!RuntimeObject.Active);
        Input.Alt = false; PingFocusGuard.Tick();
        RuntimeObject.Active = true; PingFocusGuard.Tick(); Check(RuntimeObject.Active); // G/mouse unaffected.
        Application.isFocused = false; RuntimeObject.Target = true; PingFocusGuard.Tick();
        Check(RuntimeObject.Sent == 0 && !RuntimeObject.Target);
        Application.isFocused = true; RuntimeObject.Active = true;
        PingFocusGuard.Tick(); Check(!RuntimeObject.Active);
        RuntimeObject.Active = true; PingFocusGuard.Tick(); Check(RuntimeObject.Active);
        Input.Right = true; Input.Tab = true; PingFocusGuard.Tick(); Check(!RuntimeObject.Active);
        Input.Right = Input.Tab = false; PingFocusGuard.Tick();
        Application.isFocused = false; RuntimeObject.Active = true; PingFocusGuard.Tick(); Check(!RuntimeObject.Active);
        RuntimeObject.Present = false; PingFocusGuard.Tick(); // Loading/closed scene.
        Application.isFocused = true; PingFocusGuard.Tick();
        RuntimeObject.Present = true; RuntimeObject.Active = true; PingFocusGuard.Tick(); Check(RuntimeObject.Active);
        Reset(); Plugin.PingFocus.Value=false; Input.Alt=Input.Tab=true;
        RuntimeObject.Active=RuntimeObject.Target=RuntimeObject.Hover=true; RuntimeObject.Timer=1;
        PingFocusGuard.Tick();
        Check(RuntimeObject.Active && RuntimeObject.Target && RuntimeObject.Hover && RuntimeObject.Timer==1 && RuntimeObject.Exits==0 && RuntimeObject.Sent==0);
        Plugin.PingFocus.Value=true; PingFocusGuard.Tick();
        Check(!RuntimeObject.Active && !RuntimeObject.Target && !RuntimeObject.Hover && RuntimeObject.Timer==0 && RuntimeObject.Exits==1 && RuntimeObject.Sent==0);
        Plugin.PingFocus.Value=false; Input.Alt=Input.Tab=false; PingFocusGuard.Tick();
        RuntimeObject.Active=RuntimeObject.Target=RuntimeObject.Hover=true; RuntimeObject.Timer=1;
        Plugin.PingFocus.Value=true; PingFocusGuard.Tick();
        Check(RuntimeObject.Active && RuntimeObject.Target && RuntimeObject.Hover && RuntimeObject.Timer==1 && RuntimeObject.Exits==1 && RuntimeObject.Sent==0);
        Input.Alt=Input.Tab=true; PingFocusGuard.Tick();
        Check(!RuntimeObject.Active && !RuntimeObject.Target && RuntimeObject.Sent==0 && RuntimeObject.Exits==2);
        Reset();
    }
}
""""""";

    private const string GeneralShushuShield0 = """""""
#nullable enable
using System;
using System.Collections.Generic;
namespace ShieldUiTest {
using UnityEngine;
namespace UnityEngine {
    public class Object {
        public IntPtr Pointer = RuntimeObject.NewPointer();
        public bool Destroyed;
        public static void Destroy(Object obj) { obj.Destroyed=true; if(obj is GameObject go)go.SetActive(false); }
    }
    public class Transform : Object {
        public Transform(){ }
        public Transform(IntPtr p){Pointer=p;}
        public Transform? parent; public Vector3 localPosition;
        public void SetParent(Transform p,bool world){parent=p;RuntimeObject.DataAt(Pointer)["parent"]=new RuntimeObject(p.Pointer);}
        public bool IsChildOf(Transform t)=>false;
        public Vector3 TransformVector(Vector3 v)=>v;
        public Vector3 InverseTransformVector(Vector3 v)=>v;
    }
    public class GameObject : Object {
        public Transform transform = new();
        public bool activeSelf=true;
        public GameObject(string name){RuntimeObject.DataAt(Pointer)["transform"]=new RuntimeObject(transform.Pointer);}
        public void SetActive(bool state){activeSelf=state;}
    }
    public struct Vector3 { public float x; public static Vector3 zero => new(); public static Vector3 operator -(Vector3 a,Vector3 b)=>new(){x=a.x-b.x}; }
    public struct Quaternion { public static Quaternion identity => new(); }
    public static class Time { public static float unscaledTime; }
    public static class Application { public static bool isFocused=true; }
}
sealed class RuntimeObject {
    static int next;
    static Dictionary<IntPtr,Dictionary<string,object>> all=new();
    public IntPtr Pointer;
    public Dictionary<string,object> Data => DataAt(Pointer);
    public int Writes, Releases, Reads, ParticleStarts;
    public RuntimeObject(){Pointer=NewPointer();}
    public RuntimeObject(IntPtr p){Pointer=p;}
    public static IntPtr NewPointer() => (IntPtr)(++next);
    public static Dictionary<string,object> DataAt(IntPtr p){if(!all.ContainsKey(p))all[p]=new();return all[p];}
    public RuntimeObject? Get(string key)=>Data.GetValueOrDefault(key) as RuntimeObject;
    public RuntimeObject? Field(string key)=>Get(key);
    public T Get<T>(string key) where T:unmanaged {
        if(key=="numChildren")return (T)(object)1;
        if(key=="Count")return (T)(object)((List<RuntimeObject>)Data["items"]).Count;
        return (T)Data[key];
    }
    public T Value<T>()where T:unmanaged=>(T)Data["value"];
    public string String()=>(string)Data["value"];
    public void Set(string key,object value){Data[key]=value; Writes++;}
    public static IntPtr FindClass(string ns,string name)=>(IntPtr)(name=="UIHelper"?1:name=="GameLogicManager"?3:name=="BattleSceneController"?4:2);
    public static RuntimeObject? StaticField(IntPtr type,string name)=>type==(IntPtr)3?Probe.Logic:Probe.Scene;
    public static RuntimeObject? StaticCall(IntPtr type,string method,params object?[] args){if(method=="GetBuffConfigure"){Probe.ConfigReads++;return Probe.Config;}return method=="get_draggingObject"?Probe.Dragging:Probe.Manager;}
    public RuntimeObject? Call(string method,params object?[] args){
        Reads++;
        switch(method){
        case "StopDrag":Probe.Dragging=null;return null;
        case "GetShowBuffs":
            if(args.Length!=2||!Equals(args[1],false))throw new Exception("Must use public filter");
            return Get("shown");
        case "get_Item":
            if(Data.ContainsKey("items"))return ((List<RuntimeObject>)Data["items"])[(int)args[0]!];
            return new(){Data={{"value",((Dictionary<int,int>)Data["map"])[(int)args[0]!]}}};
        case "ContainsKey":return new(){Data={{"value",((Dictionary<int,int>)Data["map"]).ContainsKey((int)args[0]!)}}};
        case "GetPlayerDataByHeroId": if(!Equals(args[0],107))throw new Exception("Wrong caster");return Get("caster");
        case "PlayById":
            Probe.PlayAttempts++;
            if(Probe.FailPlay)throw new InvalidCastException("Quaternion probe");
            if(!Equals(args[5],.25f))throw new Exception("Wrong combat scale");
            Probe.LastEffectId=(int)args[0]!;
            var effect=new RuntimeObject{Data={{"IsActive",true},{"transform",new RuntimeObject{Data={{"parent",args[3]!}}}}}};
            var awaiter=new RuntimeObject{Data={{"IsCompleted",false},{"result",effect}}};
            Probe.Loads.Add(awaiter);return awaiter;
        case "GetAwaiter":return this;
        case "GetResult":if(Data.ContainsKey("failResult"))throw new InvalidOperationException("Load probe");return Get("result");
        case "ReleaseEffect":Releases++;if(Data.ContainsKey("failRelease"))throw new InvalidOperationException("Release probe");Set("IsActive",false);return null;
        case "PlayAllParticles":ParticleStarts++;return null;
        case "GetChild":return Get("header");
        case "GetChildIndex":return new(){Data={{"value",0}}};
        case "GetTransition":return this;
        default:return null;
        }
    }
}
static class Probe {
    public static RuntimeObject Config=new(){Data={{"EffectID",901},{"IsShowEffectDuringPK",false},
        {"SkinReplaceEffectID",new RuntimeObject{Data={{"map",new Dictionary<int,int>{{710,902}}}}}},
        {"Icon",new RuntimeObject{Data={{"value","shield_icon"}}}}}};
    public static RuntimeObject Manager=new();
    public static List<RuntimeObject> Loads=new();
    public static int LastEffectId, PlayAttempts, ConfigReads;
    public static bool FailPlay;
    public static RuntimeObject? Logic, Scene, Dragging;
}
static class Plugin {
    public static FloatSetting UiScale=new(); public sealed class FloatSetting{public float Value=1;}
    public static BoolSetting ShushuShield=new(), ShushuShieldRestrictions=new(), BattleStatus=new(), ShushuMaster=new();public sealed class BoolSetting{public bool Value=true;}
}
static class ModUi { public static bool IsOpen=>false; }
static class VisibleCombatAdvisor { public static bool IsPve(int map)=>map==9; }
static class Compatibility {
    public static HashSet<string> Blocked=new();
    public static bool Allowed(string key)=>!Blocked.Contains(key)&&!Blocked.Contains("CoreUi");
    public static void Block(string key,Exception ex,params Action[] clear){
        if(!Blocked.Add(key))return;
        foreach(var c in clear){try{c();}catch{}}
    }
}
static class BattleStatusUi {
    public static int Clears;
    public static void Clear(){Clears++;}
    static RuntimeObject? _counterParent, _counterHeader, _counterIcon;
    static IntPtr _counterDefender;
    static bool _counterAvailable;
    static void ClearCounter(){_counterIcon=_counterParent=_counterHeader=null;_counterDefender=IntPtr.Zero;_counterAvailable=false;}
__COUNTER__
    public static void CheckPlayers(){
        foreach(var type in new[]{1,2}){
            var value=new RuntimeObject{Data={{"Value",true}}};
            var player=new RuntimeObject{Data={{"characterType",type},{"Property",new RuntimeObject{Data={{"Counter",value}}}}}};
            var actor=new RuntimeObject{Data={{"isDisposed",false},{"defenderData",player},
                {"com_Defend",new RuntimeObject{Data={{"header",new RuntimeObject()}}}}}};
            RefreshCounter(actor);
            if(!_counterAvailable||_counterIcon==null)throw new Exception("Player/monster counter omitted");
            value.Set("Value",false);RefreshCounter(actor);
            if(_counterAvailable)throw new Exception("Removed counter still available");
        }
        RefreshCounter(null);if(_counterAvailable||_counterIcon!=null)throw new Exception("Counter cleanup");
    }
}
static class NativeUi {
    public static RuntimeObject? CounterIndicator(RuntimeObject parent){
        return new(){Data={{"isDisposed",false},{"visible",false}}};
    }
    public static void Play(RuntimeObject obj,string name){if(name!="MaxPoint")throw new Exception("Wrong transition");}
}
static class BattleStatusLayout {
    public static bool CounterReady(bool visible,float now,ref float since){if(!visible){since=-1;return false;}if(since<0)since=now;return now-since>=.75f;}
}
static class GameUi {
    public static RuntimeObject? Root=new(), Window;
    public static bool Visible(RuntimeObject? obj)=>obj!=null && !Equals(obj.Data.GetValueOrDefault("isDisposed"),true) && !Equals(obj.Data.GetValueOrDefault("visible"),false);
    public static RuntimeObject? Find(RuntimeObject root,string name,int depth=0,int maxDepth=4)=>Window;
    public static void Dispose(RuntimeObject? obj){obj?.Set("isDisposed",true);}
}
__SOURCE__
public static class Check {
    static void Assert(bool ok,string what){if(!ok)throw new Exception(what);}
    static RuntimeObject Button(float alpha=1,bool touch=true)=>new(){Data={{"alpha",alpha},{"touchable",touch},{"visible",true},{"isDisposed",false}}};
    static RuntimeObject Buff(int id,long sn,int delay=0)=>new(){Data={{"BuffId",id},{"UniqueId",sn},{"DelayRound",delay}}};
    static RuntimeObject Card(int effect=2,bool touch=true) {
        var card=Button(touch:touch);card.Set("CardData",new RuntimeObject());
        card.Set("_config",new RuntimeObject{Data={{"EffectType",effect}}});
        card.Set("IsRelease",new RuntimeObject{Data={{"value",false}}});return card;
    }
    static RuntimeObject Player(List<RuntimeObject> buffs)=>new(){Data={{"buffContainer",new RuntimeObject{Data={{"shown",new RuntimeObject{Data={{"Item1",new RuntimeObject{Data={{"items",buffs}}}}}}}}}}}};
    static RuntimeObject Actor(RuntimeObject player,bool atk){
        var header=new RuntimeObject{Data={{"alpha",1f},{"scaleX",1f},{"scaleY",1f},{"xMin",94f},{"yMin",3f},{"width",120f},{"height",120f}}};
        var ui=new RuntimeObject{Data={{"isDisposed",false},{atk?"attackerData":"defenderData",player},
            {"com_Defend",new RuntimeObject{Data={{"header",header}}}}}};
        return new(){Data={{"effectParent",new RuntimeObject()},{"actorRenderer",new RuntimeObject{Data={{"activeInHierarchy",true}}}},{"_UI",ui}}};
    }
    public static void Run(){
        BattleStatusUi.CheckPlayers();
        var ownership=new ShieldDefenseButton();var button=Button(.8f);
        ownership.Update(button,true,true);Assert(!button.Get<bool>("touchable")&&Math.Abs(button.Get<float>("alpha")-.36f)<.001,"Dim/lock");
        var writes=button.Writes;ownership.Update(button,true,true);Assert(button.Writes==writes,"Idle writes");
        ownership.Update(button,false,true);Assert(button.Get<bool>("touchable")&&button.Get<float>("alpha")==.8f,"Shield removal restores");
        ownership.Update(button,true,true);ownership.Update(button,false,false);Assert(!button.Get<bool>("touchable"),"Never re-enable post-choice");
        var restricted=Button(.5f,false);ownership.Update(restricted,true,true);Assert(!ownership.IsBlocking(restricted)&&restricted.Writes==0,"Preserve native restriction");
        button=Button();ownership.Update(button,true,true);button.Set("alpha",.2f);ownership.Clear();Assert(button.Get<float>("alpha")==.2f,"Preserve foreign alpha");
        button=Button();ownership.Update(button,true,true);var replacement=Button();ownership.Update(replacement,true,true);
        Assert(button.Get<bool>("touchable")&&ownership.IsBlocking(replacement),"Replacement restore");ownership.Clear(false);
        var atkBuffs=new List<RuntimeObject>{Buff(ShushuShieldUi.BuffId,1)};
        var defBuffs=new List<RuntimeObject>{Buff(ShushuShieldUi.BuffId,2)};
        Assert(ShushuShieldUi.ActiveBuff(Player(new(){Buff(999,3),Buff(ShushuShieldUi.BuffId,4,1)}))==null,"Ignore unrelated/delayed");
        var atk=Actor(Player(atkBuffs),true);var def=Actor(Player(defBuffs),false);
        var director=new RuntimeObject{Data={{"attacker",atk},{"victim",def}}};
        var skin=new RuntimeObject{Data={{"ItemID",710}}};
        var caster=new RuntimeObject{Data={{"player",new RuntimeObject{Data={{"standingPainting",skin}}}}}};
        var logic=new RuntimeObject{Data={{"battle",new RuntimeObject{Data={{"caster",caster}}}}}};
        var step=new RuntimeObject{Data={{"selectedIndex",5}}};var role=new RuntimeObject{Data={{"selectedIndex",1}}};
        var defend=Button();var dodge=Button();var ui=new RuntimeObject{Data={{"step",step},{"playerState",role},{"btn_Defend",defend},{"btn_Dodge",dodge}}};
        GameUi.Window=new(){Data={{"contentPane",ui}}};
        ShushuShieldUi.Tick(logic,director);
        Assert(Probe.Loads.Count==2&&Probe.LastEffectId==902,"Both actors/caster skin");
        Assert(!defend.Get<bool>("touchable")&&dodge.Get<bool>("touchable")&&ShushuShieldUi.IsBlocking(defend),"Local shield decision");
        var oldLoad=Probe.Loads[0];var oldEffect=oldLoad.Get("result")!;
        atkBuffs.Clear();Time.unscaledTime=.11f;ShushuShieldUi.Tick(logic,director);
        oldLoad.Set("IsCompleted",true);ShushuShieldUi.PollLoads();Assert(oldEffect.Releases==1,"Consumed shield late load released");
        Probe.Loads[1].Set("IsCompleted",true);ShushuShieldUi.PollLoads();Time.unscaledTime=.25f;ShushuShieldUi.Tick(logic,director);
        Assert(Probe.Loads[1].Get("result")!.ParticleStarts==1,"Activate native particles after staging");
        ShushuShieldUi.Tick(logic,director);Assert(Probe.Loads[1].Get("result")!.ParticleStarts==1,"Do not restart unchanged particles");
        defBuffs.Clear();Time.unscaledTime=.4f;ShushuShieldUi.Tick(logic,director);
        Assert(defend.Get<bool>("touchable")&&defend.Get<float>("alpha")==1,"Live removal unlock");
        Assert(Probe.Loads[1].Get("result")!.Releases==1,"Owned active effect released");
        defBuffs.Add(Buff(ShushuShieldUi.BuffId,5));role.Set("selectedIndex",2);Time.unscaledTime=.6f;ShushuShieldUi.Tick(logic,director);
        Assert(defend.Get<bool>("touchable")&&Probe.Loads.Count==3,"Spectator shows without blocking");
        ShushuShieldUi.Clear();Probe.Loads[2].Set("IsCompleted",true);ShushuShieldUi.PollLoads();ShushuShieldUi.PollLoads();
        Assert(Probe.Loads[2].Get("result")!.Releases==1,"Cleanup late load released once");
        role.Set("selectedIndex",1);Time.unscaledTime=1;ShushuShieldUi.Tick(logic,director);step.Set("selectedIndex",6);dodge.Set("touchable",false);ShushuShieldUi.Clear();
        Assert(!defend.Get<bool>("touchable"),"Cleanup preserves final native restriction");
        Probe.Loads[3].Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Probe.Config.Set("IsShowEffectDuringPK",true);step.Set("selectedIndex",5);dodge.Set("touchable",true);defend.Set("touchable",true);
        ShushuShieldUi.Tick(logic,director);Assert(Probe.Loads.Count==4,"No duplicate native shield");ShushuShieldUi.Clear();
        Probe.Config.Set("IsShowEffectDuringPK",false);Time.unscaledTime=2;ShushuShieldUi.Tick(logic,director);
        defBuffs.Clear();defBuffs.Add(Buff(ShushuShieldUi.BuffId,6));Time.unscaledTime=2.2f;ShushuShieldUi.Tick(logic,director);
        Probe.Loads[4].Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Assert(Probe.Loads[4].Get("result")!.Releases==1 && !defend.Get<bool>("touchable"),"Old load cannot replace new shield");
        Probe.Loads[5].Set("IsCompleted",true);ShushuShieldUi.PollLoads();ShushuShieldUi.Clear();
        Assert(Probe.Loads[5].Get("result")!.Releases==1&&defend.Get<bool>("touchable"),"Current load/setting cleanup restore");
        Probe.FailPlay=true;Time.unscaledTime=3;ShushuShieldUi.Tick(logic,director);
        Assert(Compatibility.Blocked.Count==1&&Compatibility.Blocked.Contains(ShushuShieldUi.Feature)
            &&Compatibility.Allowed("BattleStatus")&&BattleStatusUi.Clears==0,"Creation failure must not disable counters/public rows");
        var attempts=Probe.PlayAttempts;ShushuShieldUi.Tick(logic,director);
        Assert(Probe.PlayAttempts==attempts&&!defend.Get<bool>("touchable")&&Compatibility.Allowed(ShushuShieldUi.RestrictionsFeature),"Display breaker prevents effect retries without disabling the independent restriction");
        Compatibility.Blocked.Clear();Probe.FailPlay=false;Time.unscaledTime=4;ShushuShieldUi.Tick(logic,director);
        Assert(!defend.Get<bool>("touchable"),"Pending shield owns defense lock");
        var failedLoad=Probe.Loads[^1];failedLoad.Set("failResult",true);failedLoad.Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Assert(Compatibility.Blocked.Count==1&&Compatibility.Blocked.Contains(ShushuShieldUi.Feature)
            &&Compatibility.Allowed("BattleStatus")&&BattleStatusUi.Clears==0,"Async failure must not disable counters/public rows");
        Assert(!defend.Get<bool>("touchable")&&Compatibility.Allowed(ShushuShieldUi.RestrictionsFeature),"Async display failure leaves the independent defense restriction intact");
        Compatibility.Blocked.Clear();Time.unscaledTime=5;ShushuShieldUi.Tick(logic,director);
        var recycledLoad=Probe.Loads[^1];var recycledEffect=recycledLoad.Get("result")!;
        ShushuShieldUi.Clear();
        recycledEffect.Get("transform")!.Set("parent",new RuntimeObject());
        recycledLoad.Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Assert(recycledEffect.Releases==0,"Late completion must not release a pooled effect now owned by native UI");
        Time.unscaledTime=5.5f;ShushuShieldUi.Tick(logic,director);
        var activeRecycledLoad=Probe.Loads[^1];var activeRecycledEffect=activeRecycledLoad.Get("result")!;
        activeRecycledLoad.Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        activeRecycledEffect.Get("transform")!.Set("parent",new RuntimeObject());
        Time.unscaledTime=5.7f;ShushuShieldUi.Tick(logic,director);ShushuShieldUi.Clear();
        Assert(activeRecycledEffect.ParticleStarts==0&&activeRecycledEffect.Releases==0,"Never restart or release an active recycled native effect");
        atkBuffs.Add(Buff(ShushuShieldUi.BuffId,9));Time.unscaledTime=6;ShushuShieldUi.Tick(logic,director);
        var brokenLoad=Probe.Loads[^2];var healthyLoad=Probe.Loads[^1];
        brokenLoad.Set("IsCompleted",true);healthyLoad.Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        brokenLoad.Get("result")!.Set("failRelease",true);
        ShushuShieldUi.Clear();
        Assert(defend.Get<bool>("touchable")&&defend.Get<float>("alpha")==1,"Effect cleanup failure must not prevent button restoration");
        Assert(healthyLoad.Get("result")!.Releases==1,"One failed actor must not prevent other actor cleanup");
        Assert(Compatibility.Allowed("BattleStatus")&&BattleStatusUi.Clears==0,"Cleanup failure must remain isolated from existing counters");
        ShushuShieldUi.Clear();
        Assert(brokenLoad.Get("result")!.Releases==1,"Failed cleanup must not repeat every frame");
        Compatibility.Blocked.Clear();atkBuffs.Clear();Time.unscaledTime=7;
        var info=new RuntimeObject{Data={{"MapType",9}}};
        var roomInfo=new RuntimeObject();roomInfo.Set("info",info);
        var room=new RuntimeObject();room.Set("curRoomInfo",roomInfo);logic.Set("room",room);
        var fightStatus=new RuntimeObject{Data={{"value",true}}};logic.Set("fight",new RuntimeObject{Data={{"fightStatus",fightStatus}}});
        Probe.Logic=logic;Probe.Scene=new RuntimeObject{Data={{"directorManager",director}}};
        Plugin.BattleStatus.Value=false;ShushuShieldUi.Tick();
        Assert(!defend.Get<bool>("touchable"),"Shield setting must work independently of Battle Info");
        Plugin.ShushuShield.Value=false;Plugin.ShushuShieldRestrictions.Value=false;ShushuShieldUi.Tick();
        Assert(defend.Get<bool>("touchable")&&defend.Get<float>("alpha")==1,"Both settings OFF remove native button lock");
        Probe.Loads[^1].Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Assert(Probe.Loads[^1].Get("result")!.Releases==1,"OFF drains late effects");
        Plugin.ShushuShield.Value=true;Plugin.ShushuShieldRestrictions.Value=true;Time.unscaledTime=8;ShushuShieldUi.Tick();
        Assert(!defend.Get<bool>("touchable"),"Setting ON restores shield feature");
        info.Set("MapType",1);ShushuShieldUi.Tick();
        Assert(defend.Get<bool>("touchable"),"Independent shield retains PvE gate");
        Probe.Loads[^1].Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        info.Set("MapType",9);step.Set("selectedIndex",3);Time.unscaledTime=9;
        var ready=Button();ui.Set("com_Card",new RuntimeObject{Data={{"btn_FinishPkCard",ready}}});
        var defenseCard=Card();var otherCard=Card(1);var nativeLocked=Card(touch:false);
        var hand=new List<RuntimeObject>{defenseCard,otherCard,nativeLocked};
        GameUi.Window!.Set("cardItemList",new RuntimeObject{Data={{"items",hand}}});
        Probe.Dragging=defenseCard;ShushuShieldUi.Tick();
        Assert(!defenseCard.Get<bool>("touchable")&&defenseCard.Get<float>("alpha")==.45f&&Probe.Dragging==null,"Shield locks/dims defense card and cancels live drag");
        Assert(otherCard.Writes==3&&nativeLocked.Writes==3&&ready.Get<bool>("touchable"),"Other cards/native locks/Ready untouched");
        writes=defenseCard.Writes;ShushuShieldUi.Tick();Assert(writes==defenseCard.Writes,"No idle card writes");
        Plugin.ShushuShieldRestrictions.Value=false;ShushuShieldUi.Tick();
        Assert(defenseCard.Get<bool>("touchable")&&defenseCard.Get<float>("alpha")==1,"OFF restores cards");
        Plugin.ShushuShieldRestrictions.Value=true;Time.unscaledTime=10;ShushuShieldUi.Tick();
        defBuffs.Clear();Time.unscaledTime=10.2f;ShushuShieldUi.Tick();
        Assert(defenseCard.Get<bool>("touchable"),"Buff removal unlocks cards");
        defBuffs.Add(Buff(ShushuShieldUi.BuffId,100));Time.unscaledTime=10.4f;
        foreach(var view in new[]{0,2}) {
            role.Set("selectedIndex",view);ShushuShieldUi.Tick();
            Assert(defenseCard.Get<bool>("touchable"),"Attacker/spectator hand untouched");
        }
        role.Set("selectedIndex",1);ShushuShieldUi.Tick();
        defenseCard.Set("CardData",new RuntimeObject());defenseCard.Set("alpha",1f);defenseCard.Set("touchable",true);
        ShushuShieldUi.Tick();Assert(!defenseCard.Get<bool>("touchable"),"Recycled card acquired afresh");
        defenseCard.Get("IsRelease")!.Set("value",true);defenseCard.Set("visible",false);ShushuShieldUi.Tick();
        Assert(!defenseCard.Get<bool>("touchable"),"Never enable released card");
        var fresh=Card();hand.Add(fresh);ShushuShieldUi.Tick();
        step.Set("selectedIndex",4);ShushuShieldUi.Tick();
        Assert(!fresh.Get<bool>("touchable")&&fresh.Get<float>("alpha")==1,"Post-choice input stays restricted, dim removed");
        ShushuShieldUi.Clear();

        // Each user-visible part must retain its own setting and compatibility breaker.
        Compatibility.Blocked.Clear();role.Set("selectedIndex",1);step.Set("selectedIndex",5);
        defend.Set("touchable",true);dodge.Set("touchable",true);Time.unscaledTime=20;
        Plugin.ShushuShield.Value=false;Plugin.ShushuShieldRestrictions.Value=false;
        var loadCount=Probe.Loads.Count;ShushuShieldUi.Tick();
        Assert(Probe.Loads.Count==loadCount&&defend.Get<bool>("touchable"),"Display OFF restrictions OFF has no effect or lock");
        Plugin.ShushuShieldRestrictions.Value=true;Probe.FailPlay=true;var configReads=Probe.ConfigReads;ShushuShieldUi.Tick();
        Assert(Probe.Loads.Count==loadCount&&Probe.ConfigReads==configReads&&!defend.Get<bool>("touchable")&&Compatibility.Blocked.Count==0,"Display OFF restrictions ON does not read effect/configuration or start a load");
        Plugin.ShushuShield.Value=true;Plugin.ShushuShieldRestrictions.Value=false;Probe.FailPlay=false;Time.unscaledTime=21;ShushuShieldUi.Tick();
        Assert(Probe.Loads.Count==loadCount+1&&defend.Get<bool>("touchable"),"Display ON restrictions OFF draws without a button lock");
        var pendingOff=Probe.Loads[^1];Plugin.ShushuShield.Value=false;Plugin.ShushuShieldRestrictions.Value=true;ShushuShieldUi.Tick();
        pendingOff.Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Assert(pendingOff.Get("result")!.Releases==1&&!defend.Get<bool>("touchable"),"Display OFF drains a pending effect without releasing restriction ownership");
        Plugin.ShushuShield.Value=true;Time.unscaledTime=22;ShushuShieldUi.Tick();
        Assert(Probe.Loads.Count==loadCount+2&&!defend.Get<bool>("touchable"),"Display ON restrictions ON operates both parts");
        Plugin.ShushuShieldRestrictions.Value=false;ShushuShieldUi.ClearRestrictions();
        Assert(defend.Get<bool>("touchable")&&Probe.Loads[^1].Get("result")!.Releases==0,"Restrictions OFF restores only its lock and retains the pending display");
        Probe.Loads[^1].Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Plugin.ShushuShieldRestrictions.Value=true;ShushuShieldUi.Tick();
        Plugin.ShushuShield.Value=false;ShushuShieldUi.ClearDisplay();
        Assert(!defend.Get<bool>("touchable"),"Display toggle cleanup never restores restriction-owned inputs");
        Plugin.ShushuShield.Value=true;Time.unscaledTime=23;Probe.FailPlay=true;ShushuShieldUi.Tick();
        Assert(Compatibility.Blocked.Contains(ShushuShieldUi.Feature)&&Compatibility.Allowed(ShushuShieldUi.RestrictionsFeature)&&!defend.Get<bool>("touchable"),"Display failure is isolated from restrictions");
        Probe.FailPlay=false;Compatibility.Blocked.Clear();ShushuShieldUi.Clear();
        Plugin.ShushuShieldRestrictions.Value=true;step.Set("selectedIndex",3);Time.unscaledTime=24;
        var invalidHand=new List<RuntimeObject>();for(var i=0;i<12;i++)invalidHand.Add(Card());
        GameUi.Window!.Set("cardItemList",new RuntimeObject{Data={{"items",invalidHand}}});
        loadCount=Probe.Loads.Count;ShushuShieldUi.Tick();
        Assert(Compatibility.Blocked.Contains(ShushuShieldUi.RestrictionsFeature)&&Compatibility.Allowed(ShushuShieldUi.Feature)&&Probe.Loads.Count==loadCount+1,"Restriction ABI failure does not disable display");
        ShushuShieldUi.Clear();foreach(var load in Probe.Loads)load.Set("IsCompleted",true);ShushuShieldUi.PollLoads();Compatibility.Blocked.Clear();
        step.Set("selectedIndex",3);Time.unscaledTime=25;defenseCard=Card();nativeLocked=Card(touch:false);
        GameUi.Window!.Set("cardItemList",new RuntimeObject{Data={{"items",new List<RuntimeObject>{defenseCard,nativeLocked}}}});
        Plugin.ShushuShield.Value=false;Plugin.ShushuShieldRestrictions.Value=true;ShushuShieldUi.Tick();
        Assert(!defenseCard.Get<bool>("touchable")&&!nativeLocked.Get<bool>("touchable"),"Restrictions-only mode locks only available defense cards");
        Plugin.ShushuShieldRestrictions.Value=false;ShushuShieldUi.Tick();
        Assert(defenseCard.Get<bool>("touchable")&&!nativeLocked.Get<bool>("touchable"),"Restrictions OFF restores owned card locks but leaves native restrictions");
        Plugin.ShushuShieldRestrictions.Value=true;ShushuShieldUi.Tick();info.Set("MapType",1);ShushuShieldUi.Tick();
        Assert(defenseCard.Get<bool>("touchable")&&!nativeLocked.Get<bool>("touchable"),"Restrictions-only PvP cleanup preserves native card restrictions");
        info.Set("MapType",9);Plugin.ShushuShield.Value=true;Time.unscaledTime=26;ShushuShieldUi.Tick();
        var pvpPending=Probe.Loads[^1];info.Set("MapType",1);ShushuShieldUi.Tick();pvpPending.Set("IsCompleted",true);ShushuShieldUi.PollLoads();
        Assert(pvpPending.Get("result")!.Releases==1&&defenseCard.Get<bool>("touchable"),"PvP cleanup restores restrictions and drains display independently");
        ShushuShieldUi.Clear();
    }
}
}
""""""";

    private const string GeneralRemainingHp0 = """""""
public static class SpectatorKoProbe {
    static int shown, cleared;
    static class Plugin {
        internal static class ShowDetails { internal static bool Value=true; }
        internal static class Enabled { internal static bool Value=true; }
        internal static class KoMinimum { internal static bool Value=true; }
        internal static class FinalDamage { internal static bool Value=true; }
        internal static class KoSpectator { internal static bool Value=true; }
        internal static class KoCardPreview { internal static bool Value=true; }
        internal static class CardAdvice { internal static bool Value=true; }
    }
    static class CardDiceUi {
        internal static void Show(int ui, int point, int input, object card, bool hover, bool unsupported) {
            if (card != null || hover || unsupported) throw new System.Exception("Spectator preview leaked");
            shown++;
        }
        internal static void Clear() { cleared++; }
    }
    static void Clear(bool keepDice) {
        if (!keepDice) throw new System.Exception("Spectator animation reset");
    }
    static void Update(int role) {
        int ui = 0, attackPoint = 0, input = 0;
__OBSERVER__
        throw new System.Exception("Spectator reached local ready/hand path");
    }
    public static void Run() {
        foreach (var advice in new[] { false, true })
        foreach (var details in new[] { false, true })
        foreach (var conditions in new[] { false, true })
        foreach (var damage in new[] { false, true }) {
            Plugin.Enabled.Value = advice;
            Plugin.ShowDetails.Value = details;
            Plugin.KoMinimum.Value = conditions;
            Plugin.FinalDamage.Value = damage;
            int beforeShown = shown, beforeCleared = cleared;
            Update(2);
            if (shown - beforeShown != (conditions ? 1 : 0) || cleared - beforeCleared != (conditions ? 0 : 1))
                throw new System.Exception("Independent spectator indicator setting failed");
        }
        Plugin.Enabled.Value=Plugin.ShowDetails.Value=Plugin.KoMinimum.Value=Plugin.FinalDamage.Value=true;
    }
}
""""""";

    private const string GeneralRemainingHp1 = """""""
public static class CombatSettingEntryProbe {
    static class Plugin {
        internal static class Enabled { internal static bool Value=true; }
        internal static class KoMinimum { internal static bool Value=true; }
        internal static class FinalDamage { internal static bool Value=true; }
        internal static class KoSpectator { internal static bool Value=true; }
        internal static class KoCardPreview { internal static bool Value=true; }
        internal static class CardAdvice { internal static bool Value=true; }
        internal static class Diagnostics { internal static void EndCombat() { ended++; } }
    }
    static class CardUi { internal static void Clear() { cardCleared++; } }
    static class RemainingHpUi { internal static void Clear() { damageCleared++; } }
    static int reached, ended, cardCleared, damageCleared;
    static void Update() {
__ENTRY__
        reached++;
    }
    public static void Run() {
        reached=ended=cardCleared=damageCleared=0;
        Plugin.Enabled.Value=Plugin.KoMinimum.Value=Plugin.FinalDamage.Value=true;
        foreach (var advice in new[] { false, true })
        foreach (var conditions in new[] { false, true })
        foreach (var damage in new[] { false, true }) {
            Plugin.Enabled.Value = advice; Plugin.KoMinimum.Value = conditions; Plugin.FinalDamage.Value = damage;
            int before = reached, beforeEnded=ended, beforeCards=cardCleared, beforeDamage=damageCleared;
            Update();
            var active=advice || conditions || damage;
            if (reached - before != (active ? 1 : 0) || ended-beforeEnded != (active ? 0 : 1)
                || cardCleared-beforeCards != (active ? 0 : 1) || damageCleared-beforeDamage != (active ? 0 : 1))
                throw new System.Exception("Independent combat entry failed");
        }
        Plugin.Enabled.Value=Plugin.KoMinimum.Value=Plugin.FinalDamage.Value=true;
    }
}
""""""";

    private const string GeneralRemainingHp2 = """""""
using System;
using System.Globalization;
using System.Collections.Generic;
__COMBAT__
sealed class RuntimeObject {
    static int next;
    internal IntPtr Pointer=(IntPtr)(++next);
    internal readonly Dictionary<string,object?> Data=new() { ["visible"]=true, ["isDisposed"]=false };
    internal RuntimeObject? Get(string name)=>Data[name] as RuntimeObject;
    internal RuntimeObject? Field(string name)=>Get(name);
    internal T Get<T>(string name)=>(T)Data[name]!;
    internal T Value<T>()=>(T)Data["value"]!;
    internal string String()=>(string)Data["value"]!;
    internal RuntimeObject? Call(string name,params object[] args)=>name=="GetTransition"
        ? Get("transition:"+(string)args[0]) : throw new Exception("Unexpected native call: "+name);
    internal static RuntimeObject Box(object value) { var result=new RuntimeObject(); result.Data["value"]=value; return result; }
}
static class GameUi {
    internal static RuntimeObject? Root;
    internal static bool HomeAvailable;
    internal static int Disposed;
    internal static bool Visible(RuntimeObject? value)=>value!=null && value.Get<bool>("visible") && !value.Get<bool>("isDisposed");
    internal static void Dispose(RuntimeObject? value) { if(value!=null && !value.Get<bool>("isDisposed")){value.Data["isDisposed"]=true;Disposed++;} }
}
static class Plugin {
    internal static class Enabled { internal static bool Value=true; }
    internal static class KoMinimum { internal static bool Value=true; }
    internal static class FinalDamage { internal static bool Value=true; }
    internal static class KoSpectator { internal static bool Value=true; }
    internal static class KoCardPreview { internal static bool Value=true; }
    internal static class CardAdvice { internal static bool Value=true; }
    internal static class Logger { internal static void LogWarning(string value)=>throw new Exception(value); }
    internal static class Diagnostics {
        internal static bool IsRecording=true;
        internal static int Events,Results;
        internal static (int Hp,int Damage,int Observed) LastResult;
        internal static void CombatEvent(string value) { Events++; }
        internal static void CombatResult(int hp,int damage,int observed,HitModifiers modifiers,bool monsterDefeat,int? changeHp=null) { Results++;LastResult=(hp,damage,observed); }
        internal static void Error(string name,Exception error)=>throw new Exception(name,error);
    }
}
static class PublicCombatEffects {
    internal static HitModifiers? Read(RuntimeObject defender,string context,RuntimeObject? attacker)=>default(HitModifiers);
}
static class AdviceText {
    internal static bool Ready=true;
    internal static bool FinalDamageReady(bool saw,bool finished,bool attackPulse,bool defensePulse,bool escaped)=>Ready;
}
sealed class UnknownEffectsRow {
    internal static int Shown;
    internal UnknownEffectsRow(RuntimeObject parent,float width,float y,bool help) {}
    internal void Show(UnknownEffects unknown) { Shown++; }
}
static class CardDiceUi {
    internal static int Calls,Damage;
    internal static void ShowFinal(RuntimeObject parent,RuntimeObject attack,int hp,int damage,HitModifiers modifiers,int die) {
        if(!Plugin.KoMinimum.Value)return;
        Calls++;Damage=damage;
    }
}
internal static class RemainingHpUi {
__FIELDS__
    internal static int Rendered;
__UPDATE__
__NUMBER__
__ANIMATE__
__RESET__
__DISPLAY_CLEAR__
__CLEAR__
__POLL__
    sealed class World {
        internal readonly RuntimeObject Fight=new(),Parent=new(),Attack=new(),Defense=new(),Attacker=new(),Defender=new(),AttackPoint=new(),DefensePoint=new();
        internal World() {
            Clear(); _failed=false; Rendered=0;
            Plugin.Enabled.Value=Plugin.KoMinimum.Value=Plugin.FinalDamage.Value=true;
            Plugin.Diagnostics.Events=Plugin.Diagnostics.Results=0; Plugin.Diagnostics.LastResult=default;
            CardDiceUi.Calls=CardDiceUi.Damage=GameUi.Disposed=0; AdviceText.Ready=true;
            GameUi.Root=new();GameUi.HomeAvailable=false;
            Fight.Data["defenderData"]=Defender;Fight.Data["attackerData"]=Attacker;Defender.Data["Property"]=null;
            Attacker.Data["characterType"]=RuntimeObject.Box(1);
            Parent.Data["txt_Failure"]=new RuntimeObject();((RuntimeObject)Parent.Data["txt_Failure"]!).Data["visible"]=false;
            Parent.Data["step"]=new RuntimeObject();((RuntimeObject)Parent.Data["step"]!).Data["selectedIndex"]=5;
            var values=new RuntimeObject();Parent.Data["com_value"]=values;
            var cut=new RuntimeObject();cut.Data["playing"]=false;values.Data["transition:Cut_out"]=cut;
            foreach(var actor in new[]{Attack,Defense}) {
                var state=new RuntimeObject();state.Data["selectedIndex"]=1;actor.Data["showPoint"]=state;
            }
            Attack.Data["com_Point"]=AttackPoint;Defense.Data["com_Point"]=DefensePoint;
            foreach(var point in new[]{AttackPoint,DefensePoint}) {
                point.Data["txt_Point"]=new RuntimeObject();((RuntimeObject)point.Data["txt_Point"]!).Data["text"]=RuntimeObject.Box("3");
                var pulse=new RuntimeObject();pulse.Data["playing"]=true;point.Data["transition:PointChange"]=pulse;
            }
            foreach(var name in new[]{"changeState","showResult"}) { var state=new RuntimeObject();state.Data["selectedIndex"]=0;Defense.Data[name]=state; }
            Defense.Data["txt_Life"]=new RuntimeObject();
        }
        internal void Capture() { Update(Fight,Parent,Attack,Defense,3,10);Update(Fight,Parent,Attack,Defense,5,10); }
        internal void Totals(int attack,int defense) {
            Attack.Field("showPoint")!.Data["selectedIndex"]=Defense.Field("showPoint")!.Data["selectedIndex"]=2;
            AttackPoint.Field("txt_Point")!.Data["text"]=RuntimeObject.Box(attack.ToString(CultureInfo.InvariantCulture));
            DefensePoint.Field("txt_Point")!.Data["text"]=RuntimeObject.Box(defense.ToString(CultureInfo.InvariantCulture));
        }
    }
    static void Check(bool pass,string reason) { if(!pass)throw new Exception("Final damage lifetime: "+reason); }
    static void Display() { _point=new();_pointContext=_parent!.Pointer;_layout=(1,1,1);_alpha=.5f;_showing=_animated=true; }
    internal static void Run() {
        var w=new World();w.Capture();w.Totals(8,3);Update(w.Fight,w.Parent,w.Attack,w.Defense,5,10);
        Check(_hp==10 && _die==3 && _prediction==5 && _captured && Plugin.Diagnostics.Events==1 && CardDiceUi.Calls==1 && CardDiceUi.Damage==5 && Rendered==1,"public final hit frozen once");
        Display();var oldPoint=_point;Plugin.FinalDamage.Value=false;Animate();ClearDisplay();
        Check(_point==null && _pointContext==IntPtr.Zero && _layout==null && _alpha==-1 && !_showing && !_animated && GameUi.Disposed==1 && oldPoint!.Get<bool>("isDisposed"),"OFF removes only owned artwork idempotently");
        Check(_hp==10 && _die==3 && _prediction==5 && _captured && !_resultLogged && _parent==w.Parent && _fight==w.Fight,"OFF preserves the original HP/dice and frozen evidence");
        Update(w.Fight,w.Parent,w.Attack,w.Defense,6,5);
        Check(Plugin.Diagnostics.Results==1 && Plugin.Diagnostics.LastResult==(10,5,5) && _hp==10 && _prediction==5,"result after OFF uses original HP rather than recapturing post-hit HP");
        Plugin.FinalDamage.Value=true;Update(w.Fight,w.Parent,w.Attack,w.Defense,6,5);
        Check(Plugin.Diagnostics.Results==1 && Plugin.Diagnostics.Events==1 && CardDiceUi.Calls==1,"ON cannot duplicate consumed evidence/results");
        Clear();Check(_hp==0 && _die==0 && _prediction==-1 && !_captured && !_resultLogged && _fight==null && _parent==null,"native scene clear resets the capture");
        w=new World();Plugin.Enabled.Value=false;Plugin.FinalDamage.Value=false;w.Capture();w.Totals(4,1);PollFinalPoints();
        Check(_hp==10 && _die==3 && _prediction==3 && _captured && CardDiceUi.Calls==1 && CardDiceUi.Damage==3 && Rendered==0 && Plugin.Diagnostics.Events==1,"KO ON polls new public final evidence while advice and damage art are OFF");
        Update(w.Fight,w.Parent,w.Attack,w.Defense,6,7);
        Check(Plugin.Diagnostics.LastResult==(10,3,7) && Plugin.Diagnostics.Results==1,"OFF new-hit result retains original HP");
        w=new World();Plugin.FinalDamage.Value=Plugin.KoMinimum.Value=false;w.Capture();w.Totals(4,1);PollFinalPoints();
        Check(_prediction==-1 && CardDiceUi.Calls==0 && Rendered==0,"both independent final consumers OFF skip pending polling");
        Plugin.KoMinimum.Value=true;PollFinalPoints();Check(_prediction==3 && CardDiceUi.Damage==3 && Rendered==0,"KO re-enable resumes the same public pending hit");
        Display();GameUi.HomeAvailable=true;Plugin.FinalDamage.Value=true;Animate();
        Check(_prediction==-1 && !_captured && _parent==null && _point==null,"scene exit still performs full cleanup");
        GameUi.HomeAvailable=false;Clear();Plugin.Enabled.Value=Plugin.KoMinimum.Value=Plugin.FinalDamage.Value=true;
    }
}
""""""";

}

