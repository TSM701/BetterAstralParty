#nullable enable
using System;
using System.Collections.Generic;
namespace CardResultClicksTest {
sealed class RuntimeObject {
    static int next;
    public IntPtr Pointer = (IntPtr)(++next);
    public Dictionary<string,object?> Data = new(){{"isDisposed",false}};
    public int Writes;
    public RuntimeObject? Get(string key)=>Data.GetValueOrDefault(key) as RuntimeObject;
    public RuntimeObject? Field(string key)=>Get(key);
    public T Get<T>(string key)=>(T)Data[key]!;
    public void Set(string key,object value){Data[key]=value;Writes++;}
    public RuntimeObject? Call(string method,params object[] args)=>method=="GetPlayerID"?new(){Data=new(){{"result",Data["id"]}}}:throw new Exception("Unexpected call "+method);
    public T Value<T>()=>(T)Data["result"]!;
    public static RuntimeObject? Logic;
    public static IntPtr FindClass(string ns,string name)=>ns=="GameLogic"&&name=="GameLogicManager"?(IntPtr)1:throw new Exception("Unexpected class "+name);
    public static RuntimeObject? StaticField(IntPtr type,string name)=>type==(IntPtr)1&&name=="_inst"?Logic:throw new Exception("Unexpected static "+name);
}
static class GameUi {
    public static RuntimeObject? Root=new(), Window;
    public static RuntimeObject? Find(RuntimeObject root,string name,int depth=0,int maxDepth=4){
        if(name!="CardWindow"||maxDepth!=1)throw new Exception("CardWindow is a root child");
        return Window;
    }
    public static bool Visible(RuntimeObject? obj)=>obj!=null&&obj.Get<bool>("visible");
}
static class Compatibility {
    public static bool Allowed(string key)=>key=="CardResultClicks";
    public static void Block(string key,Exception ex,Action clear){clear();throw ex;}
}
static class PvpSafety { public static bool Suspended; }
__SOURCE__
public static class Check {
    static int assertions;
    static void Assert(bool ok,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){assertions++;if(!ok)throw new Exception("Card result clicks regression at fixture line "+line);}
    static RuntimeObject Result(bool opaque=true)=>new(){Data=new(){{"isDisposed",false},{"visible",true},{"opaque",opaque}}};
    static void Scene(RuntimeObject result,long local,long turn){
        var pane=new RuntimeObject(); pane.Data["com_CardResult"]=result;
        GameUi.Window=new RuntimeObject(); GameUi.Window.Data["contentPane"]=pane;
        var account=new RuntimeObject(); account.Data["id"]=local;
        var battle=new RuntimeObject(); battle.Data["curPlayerId"]=turn;
        RuntimeObject.Logic=new RuntimeObject(); RuntimeObject.Logic.Data["account"]=account; RuntimeObject.Logic.Data["battle"]=battle;
    }
    static void Turn(long turn)=>RuntimeObject.Logic!.Get("battle")!.Data["curPlayerId"]=turn;
    public static void Run(){
        // Another player's card result lets clicks through its empty area, once, and gets its opacity back as it hides.
        var r=Result(); Scene(r,7,9); CardResultClicks.Tick(); Assert(!r.Get<bool>("opaque")&&r.Writes==1);
        CardResultClicks.Tick(); Assert(r.Writes==1);
        r.Data["visible"]=false; CardResultClicks.Tick(); Assert(r.Get<bool>("opaque")&&r.Writes==2);
        // The local player's own turn, or the turn unknown, keeps the game's block.
        r=Result(); Scene(r,7,7); CardResultClicks.Tick(); Assert(r.Get<bool>("opaque")&&r.Writes==0);
        Turn(0); CardResultClicks.Tick(); Assert(r.Get<bool>("opaque")&&r.Writes==0);
        Turn(9); CardResultClicks.Tick(); Assert(!r.Get<bool>("opaque"));
        Turn(7); CardResultClicks.Tick(); Assert(r.Get<bool>("opaque"));
        // PvP and a cleared window give it back; a result that was never opaque is left as it is.
        Turn(9); CardResultClicks.Tick(); Assert(!r.Get<bool>("opaque"));
        PvpSafety.Suspended=true; CardResultClicks.Tick(); Assert(r.Get<bool>("opaque")); PvpSafety.Suspended=false;
        r=Result(opaque:false); Scene(r,7,9); CardResultClicks.Tick(); CardResultClicks.Clear(); Assert(!r.Get<bool>("opaque")&&r.Writes==0);
        // A new result object gives the old one back first; a disposed one is not written.
        var a=Result(); Scene(a,7,9); CardResultClicks.Tick(); var b=Result(); Scene(b,7,9); CardResultClicks.Tick();
        Assert(a.Get<bool>("opaque")&&!b.Get<bool>("opaque"));
        b.Data["isDisposed"]=true; var writes=b.Writes; CardResultClicks.Clear(); Assert(b.Writes==writes);
        GameUi.Window=null; CardResultClicks.Tick();
        Console.WriteLine("Card result clicks assertions: "+assertions);
    }
}
}
