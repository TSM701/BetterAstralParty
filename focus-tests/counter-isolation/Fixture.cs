using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEngine {static class Application{internal static bool isFocused=true;}static class Time{internal static float unscaledTime=10;}}
sealed class RuntimeObject {
    static int next;public readonly IntPtr Pointer=(IntPtr)(++next);public readonly Dictionary<string,object> Data=new();
    public RuntimeObject? Get(string name)=>Data.TryGetValue(name,out var value)?value as RuntimeObject:null;
    public RuntimeObject? Field(string name)=>Get(name);
    public T Get<T>(string name) where T:unmanaged=>(T)Data[name];
    public T Value<T>() where T:unmanaged=>(T)Data["value"];
    public void Set(string name,object value)=>Data[name]=value;
    public RuntimeObject? Call(string name,params object?[] args)=>null;
    public static IntPtr FindClass(string ns,string name)=>(IntPtr)(name=="GameLogicManager"?1:2);
    public static RuntimeObject? StaticField(IntPtr type,string field)=>type==(IntPtr)1?Probe.Logic:Probe.Scene;
    public static void RequireClasses(string feature,params (string,string)[] types)=>throw new Exception("Native readiness calls must not run in the inert field fixture");
}
static class Probe {
    public static RuntimeObject Logic=new(),Scene=new(),Fight=new();
    public static bool FailRefresh,FailPosition,FailEncounter;
    public static int Refreshes,Positions,Scans,RowPositions,RowsCleared,CounterCleared,EncounterDisposed;
    public static RuntimeObject Obj(params (string,object)[] fields){var obj=new RuntimeObject();foreach(var field in fields)obj.Data[field.Item1]=field.Item2;return obj;}
    public static void Reset(){
        Compatibility.Blocked.Clear();FailRefresh=FailPosition=FailEncounter=false;Refreshes=Positions=Scans=RowPositions=RowsCleared=CounterCleared=EncounterDisposed=0;
        GameUi.Root=Obj(("width",1920f),("height",1080f));
        var victimUi=new RuntimeObject();var victim=Obj(("_UI",victimUi));Scene=Obj(("directorManager",Obj(("victim",victim))));
        var room=Obj(("info",Obj(("MapType",9))));var roomLogic=Obj(("curRoomInfo",room));
        var fightStatus=Obj(("value",true));var fightLogic=Obj(("fightStatus",fightStatus));Logic=Obj(("fight",fightLogic),("room",roomLogic));
        var anchor=Obj(("width",100f),("height",100f));var ui=Obj(("width",1100f),("height",700f),("step",Obj(("selectedIndex",1))),("com_LaunchPK",Obj(("com_Counter",anchor))));
        var property=Obj(("Counter",Obj(("Value",true))));var defender=Obj(("characterType",2),("Property",property));
        Fight=Obj(("isDisposed",false),("contentPane",ui),("defenderData",defender));
        BattleStatusUi.Reset(Fight,ui);EncounterCounterUi.Clear();EncounterDisposed=0;
    }
}
static class Compatibility {
    public static HashSet<string> Blocked=new();public static bool Allowed(string feature)=>!Blocked.Contains(feature);
    public static void Block(string feature,Exception error,params Action[] cleanup){if(!Blocked.Add(feature))return;foreach(var action in cleanup)action();}
}
static class Plugin {
    public sealed class Setting{public bool Value=true;}public static Setting BattleStatus=new();
    public sealed class Log{public void State(string key,string value){}}public static Log Diagnostics=new();
}
static class ModUi{public static bool IsOpen=>false;}
static class VisibleCombatAdvisor{public static bool IsPve(int mode)=>mode==9;}
static class NativeUi {
    public static RuntimeObject CounterIndicator(RuntimeObject parent)=>Probe.Obj(("isDisposed",false));
    public static void Play(RuntimeObject effect,string transition){if(Probe.FailEncounter)throw new MissingFieldException("Inert changed sound payload");}
}
static class GameUi {
    public static RuntimeObject? Root;public static bool HomeAvailable=>false;
    public static bool Visible(RuntimeObject? value)=>value!=null&&(!value.Data.TryGetValue("isDisposed",out var disposed)||!Equals(disposed,true));
    public static RuntimeObject? Find(RuntimeObject root,string name)=>Probe.Fight;
    public static void Dispose(RuntimeObject? value){if(value!=null){value.Set("isDisposed",true);Probe.EncounterDisposed++;}}
}
static class BattleStatusUi {
    internal const string CounterFeature="CounterIndicators";
    sealed class Row {internal bool NeedsData=>false;internal void UpdateInputOrder(int step){}internal void Position(){Probe.RowPositions++;}}
    static List<Row> Rows=new(){new(),new()};static IntPtr _logicClass=(IntPtr)1,_battleClass=(IntPtr)2,_helperClass,_commonClass;
    static RuntimeObject? _fight,_ui;static float _scanAt,_rootWidth,_rootHeight,_uiWidth,_uiHeight;
    public static void Reset(RuntimeObject fight,RuntimeObject ui){_fight=fight;_ui=ui;_scanAt=0;}
    private static void Scan(RuntimeObject room,int mode,RuntimeObject? director){Probe.Scans++;}
    private static void Hover(){}
    private static void ClearRows(){Probe.RowsCleared++;}
    public static void Clear(){ClearRows();ClearCounter();}
    public static void ClearCounter(){Probe.CounterCleared++;}
    private static void RefreshCounter(RuntimeObject? actor){Probe.Refreshes++;if(Probe.FailRefresh)throw new MissingFieldException("Inert changed counter construction");}
    private static void PositionCounter(){Probe.Positions++;if(Probe.FailPosition)throw new InvalidOperationException("Inert changed counter positioning");}
__COUNTER_BOUNDARY__
__BATTLE_TICK__
}
__ENCOUNTER__
public static class CounterIsolationCheck {
    static int checks;static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    public static void Run(){
        foreach(var position in new[]{false,true}){
            Probe.Reset();Probe.FailRefresh=!position;Probe.FailPosition=position;BattleStatusUi.Tick();
            Check(Compatibility.Blocked.SetEquals(new[]{BattleStatusUi.CounterFeature}),"Counter construction/position failure has only the counter breaker");
            Check(Probe.Scans==1&&Probe.RowPositions==2&&Probe.RowsCleared==0,"Actual public row tick continues after counter failure");
            Check(Probe.CounterCleared==1,"Failed counter ownership is cleaned up once");
            var refreshes=Probe.Refreshes;BattleStatusUi.Tick();Check(Probe.Refreshes==refreshes&&Probe.RowPositions==4,"Counter breaker prevents retries but not later public rows");
        }
        Probe.Reset();Probe.FailEncounter=true;EncounterCounterUi.Tick();
        Check(Compatibility.Blocked.SetEquals(new[]{BattleStatusUi.CounterFeature})&&Probe.EncounterDisposed==1,"Actual encounter failure cleans only its clone and counter state");
        BattleStatusUi.Tick();Check(Probe.Scans==1&&Probe.RowPositions==2&&Probe.RowsCleared==0,"Actual public row tick survives encounter effect failure");
        Check(Compatibility.Allowed("ShushuShield")&&Compatibility.Allowed("ShushuShieldRestrictions"),"Counter breaker does not affect shield settings");
        Console.WriteLine("PASS isolated counter failures: "+checks+" checks");
    }
}
