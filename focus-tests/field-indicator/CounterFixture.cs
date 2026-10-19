#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine {
    public readonly record struct Vector2(float x, float y) { public float sqrMagnitude => x*x+y*y; public static Vector2 up=>new(0,1); public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y); public static Vector2 operator *(Vector2 a,float d)=>new(a.x*d,a.y*d); }
    public sealed class Camera { public Camera(IntPtr pointer)=>throw new Exception("Unexpected field camera in counter fixture"); public bool enabled=>false; public Vector3 WorldToScreenPoint(Vector3 point)=>point; }
    public static class Screen { public static int height=>throw new Exception("Unexpected screen read in counter fixture"); }
    public readonly record struct Vector3(float x,float y,float z) { public static Vector3 zero=>default; }
    public readonly record struct Color(float r,float g,float b);
    public static class Time { public static float unscaledTime; }
    public static class Application { public static bool isFocused=true; }
    public static class Input { public static bool Down,Up,Held; public static Vector3 mousePosition; public static bool GetMouseButtonDown(int b)=>b==0&&Down; public static bool GetMouseButtonUp(int b)=>b==0&&Up; public static bool GetMouseButton(int b)=>b==0&&Held; }
}
namespace FieldCounterTest {
using UnityEngine;
__PRODUCTION__

internal sealed class RuntimeObject {
    private static int next;
    internal readonly IntPtr Pointer=(IntPtr)(++next);
    internal readonly string Kind;
    internal readonly Dictionary<string,object?> Data=new();
    internal readonly List<RuntimeObject> Children=new();
    internal RuntimeObject? Parent;
    internal bool Visible=true,Disposed,OnStage=true;
    internal float Alpha=1;
    internal int Writes,Opens;
    internal RuntimeObject(string kind){Kind=kind;}
    internal string TypeName=>Kind switch {"Attr"=>"UICom_AttrInfo","PlayerAttr"=>"UICom_PlayerAttrInfo",_=>"GComponent"};
    internal static RuntimeObject Box(object value)=>new("Box"){Data={{"value",value}}};
    internal RuntimeObject? Get(string name) {
        bool ok=Kind switch {
            "Logic"=>name is "room" or "fight" or "battle", "RoomLogic"=>name=="curRoomInfo", "BattleInfo"=>name=="ui",
            "Attr" or "PlayerAttr"=>name=="PlayerData", "Player"=>name is "Property" or "buffContainer", _=>false};
        if(!ok)throw new Exception("Unexpected native getter "+Kind+"."+name);
        return Data.GetValueOrDefault(name) as RuntimeObject;
    }
    internal T Get<T>(string name) where T:unmanaged {
        object? value=name switch {
            "isDisposed"=>Disposed,"onStage"=>OnStage,"visible"=>Visible,
            "numChildren" when Kind=="Parent"=>Children.Count,
            "width" or "height" when Kind=="Parent"=>Data.GetValueOrDefault(name) ?? 0f,
            "Count" when Kind=="List"=>Children.Count,
            _=>Data.GetValueOrDefault(name)};
        bool ok=name is "isDisposed" or "onStage" or "visible" || Kind switch {
            "Parent"=>name is "numChildren" or "width" or "height","List"=>name=="Count","Player"=>name=="characterType",
            "Info"=>name=="MapType","Property"=>name=="ExtraMovePoint","Reactive"=>name=="Value","RoomPlayer"=>name=="Id",
            "Anchor"=>name is "xMin" or "yMin" or "width" or "height","Icon"=>name is "width" or "height",_=>false};
        if(!ok||value is not T typed)throw new Exception("Strict typed getter "+Kind+"."+name+"/"+typeof(T).Name);
        return typed;
    }
    internal RuntimeObject? Field(string name) {
        bool ok=Kind switch {
            "Room"=>name=="info","Battle"=>name is "battleInfo" or "PlayerDatas","Ui"=>name is "com_AttrInfos" or "com_PlayerAttrInfos",
            "Player"=>name is "player" or "CharacterInst","Property"=>name is "Counter" or "HP" or "maxHP" or "ATK" or "DEF",
            "Tuple"=>name is "Item1" or "Item2","Attr"=>name is "txt_Name" or "txt_Number" or "txt_HP" or "graph_Line" or "graph_Guide" or "graph_Sign" or "list_Buff" or "graph_ShowBuff",
            "PlayerAttr"=>name=="loader_State","Fight"=>name=="fightStatus",_=>false};
        if(!ok)throw new Exception("Unexpected native field "+Kind+"."+name);
        return Data.GetValueOrDefault(name) as RuntimeObject;
    }
    internal void Set(string name,object? value) {
        bool ok=Kind switch { "Attr"=>name=="visible","Label"=>name is "text" or "color","Anchor"=>name is "text" or "color",
            "Graph"=>name=="color","List"=>name=="numItems","Icon"=>name=="visible",_=>false };
        if(!ok)throw new Exception("Forbidden native write "+Kind+"."+name);
        if(name=="visible")Visible=(bool)value!;else Data[name]=value;Writes++;
    }
    internal void SetField(string name,object value) {
        if(Kind!="Attr"||name!="_PlayerId"||value is not long id)throw new Exception("Forbidden native field write");
        Data["PlayerData"]=World.Owners[id];Writes++;
    }
    internal T Value<T>() where T:unmanaged=>Kind=="Box"&&Data["value"] is T v?v:throw new Exception("Strict boxed value");
    internal string String()=>Kind=="Box"&&(Data["value"] is string text)?text:throw new Exception("Strict string value");
    internal RuntimeObject? Call(string name,params object?[] args) {
        if(Kind=="Parent"&&name=="AddChild"&&args is [RuntimeObject child]){child.Parent=this;Children.Add(child);return child;}
        if(Kind=="Parent"&&name=="GetChildAt"&&args is [int i])return Children[i];
        if(Kind=="List"&&name=="get_Item"&&args is [int index])return Children[index];
        if(Kind=="RoomPlayer"&&name=="GetNick"&&args is [false])return Box("fixture");
        if(Kind=="Buffs"&&name=="GetShowBuffs"&&args is [RuntimeObject,false])return (RuntimeObject)Data["shown"]!;
        if(Kind=="Attr"&&name=="SwitchInfo"&&args is [false])return null;
        if(Kind=="Attr"&&(name=="UpdateHP"&&args is [int,int]||name is "UpdateATK" or "UpdateDEF" or "UpdateExtraMovePoint"&&args is [int]))return null;
        if(Kind=="Attr"&&name=="OpenBuffInfo"&&args is [null]){Opens++;return null;}
        throw new Exception("Forbidden native call "+Kind+"."+name);
    }
    internal static IntPtr FindClass(string ns,string name)=>(ns,name) switch {("GameLogic","GameLogicManager")=>(IntPtr)2,("UI","UICom_AttrInfo")=>(IntPtr)3,("FairyGUI","UIConfig")=>(IntPtr)1,_=>throw new Exception("Unknown native class")};
    internal static RuntimeObject? StaticField(IntPtr type,string name)=>type==(IntPtr)2&&name=="_inst"?World.Current!.Logic:type==(IntPtr)1&&name=="clickDragSensitivity"?Box(5):throw new Exception("Unknown singleton");
    internal static RuntimeObject? StaticCall(IntPtr type,string name,params object?[] args)=>type==(IntPtr)3&&name=="CreateInstance"&&args.Length==0?World.Plate("Attr",null):throw new Exception("Forbidden static call");
    internal static void RequireClasses(string feature,params (string,string)[] types)=>throw new Exception("Unexpected home construction");
}
internal static class GameUi {
    internal static RuntimeObject? Root;internal static bool HomeAvailable;internal static RuntimeObject[] Path=Array.Empty<RuntimeObject>();
    internal static bool Visible(RuntimeObject? item){for(var p=item;p!=null;p=p.Parent)if(p.Disposed||!p.OnStage||!p.Visible)return false;return item!=null;}
    internal static IEnumerable<RuntimeObject> PointerPath()=>Path;
    internal static RuntimeObject? Find(RuntimeObject root,string type)=>null;
    internal static void Dispose(RuntimeObject? item){if(item==null||item.Disposed)return;item.Disposed=true;foreach(var child in item.Children.ToArray())Dispose(child);item.Parent?.Children.Remove(item);}
}
internal static class NativeUi {
    internal static readonly List<RuntimeObject> Created=new();
    internal static RuntimeObject? Icon(RuntimeObject parent,string name,float x,float y,float size){if(name!="Com_Icon_Counter"||size!=36)throw new Exception("Unknown counter artwork");var icon=new RuntimeObject("Icon"){Parent=parent,Data={{"width",150f},{"height",150f},{"x",x},{"y",y},{"touchable",false}}};parent.Children.Add(icon);Created.Add(icon);return icon;}
    internal static void Position(RuntimeObject icon,float x,float y,float scale){if(icon.Kind!="Icon"||scale!=.24f)throw new Exception("Unexpected layout target");icon.Data["x"]=x;icon.Data["y"]=y;icon.Writes++;}
    // Owned hero-plate layer: an empty mod component inside the native parent.
    internal static RuntimeObject Component(RuntimeObject parent,float width,float height){if(width!=0||height!=0)throw new Exception("Unexpected layer size");var layer=new RuntimeObject("Parent");parent.Call("AddChild",layer);return layer;}
}
internal static class FieldZoomUi {internal static float IndicatorScale=>1f;}
internal static class FieldMonsterPlates { internal static readonly List<RuntimeObject> Adopted=new(); internal static IReadOnlyList<RuntimeObject> Plates=>Adopted; }
internal sealed class FloatSetting {internal float Value=1f;}
internal static class ModUi {internal static bool IsOpen;}
internal static class FieldIndicatorOpacity {internal static bool Hidden;}
internal static class FieldNameFont {internal static bool Apply(RuntimeObject label,RuntimeObject? ui)=>true;}
internal static class VisibleCombatAdvisor {internal static bool IsPve(int map)=>map==2;}
internal sealed class Setting {internal bool Value=true;}
internal sealed class Diagnostics {internal bool IsRecording=>false;internal void State(string key,string value){}internal void Write(string value){}internal void Error(string key,Exception ex)=>throw ex;}
internal static class Plugin {internal static readonly Setting FieldBuffs=new();internal static readonly FloatSetting FieldIndicatorHeight=new();internal static readonly Diagnostics Diagnostics=new();}
internal static class Compatibility {internal static bool Allowed(string feature)=>true;internal static void Block(string feature,Exception ex,Action cleanup){cleanup();throw new Exception("Unexpected production failure",ex);}}

internal sealed class World {
    internal static World? Current;internal static readonly Dictionary<long,RuntimeObject> Owners=new();
    internal readonly RuntimeObject Root=new("Parent"),AttrParent=new("Parent"),PlayerParent=new("Parent"),Ui=new("Ui"),Players=new("List"),Info=new("Info"),Fight=new("Fight"),Logic=new("Logic");
    internal World(){FieldBuffUi.Clear("fixture-reset");foreach(var pair in new[]{("_scanAt",(object)0f),("_plateClass",(object)(IntPtr)3),("_logicClass",(object)(IntPtr)2)})typeof(FieldBuffUi).GetField(pair.Item1,BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,pair.Item2);Current=this;Owners.Clear();NativeUi.Created.Clear();GameUi.Root=Root;GameUi.HomeAvailable=false;Plugin.FieldBuffs.Value=true;ModUi.IsOpen=false;FieldIndicatorOpacity.Hidden=false;Application.isFocused=true;Input.Down=Input.Up=Input.Held=false;Time.unscaledTime+=1;GameUi.Path=Array.Empty<RuntimeObject>();AttrParent.Parent=PlayerParent.Parent=Root;
        Info.Data["MapType"]=2;var room=new RuntimeObject("Room"){Data={{"info",Info}}};Logic.Data["room"]=new RuntimeObject("RoomLogic"){Data={{"curRoomInfo",room}}};Fight.Data["fightStatus"]=RuntimeObject.Box(false);Logic.Data["fight"]=Fight;Ui.Data["com_AttrInfos"]=AttrParent;Ui.Data["com_PlayerAttrInfos"]=PlayerParent;Logic.Data["battle"]=new RuntimeObject("Battle"){Data={{"battleInfo",new RuntimeObject("BattleInfo"){Data={{"ui",Ui}}}},{"PlayerDatas",Players}}};}
    internal static RuntimeObject Player(long id,bool counter,int type=2) {
        var property=new RuntimeObject("Property");
        foreach(var pair in new[]{("Counter",(object)counter),("HP",(object)5),("ATK",(object)2),("DEF",(object)1)})
            property.Data[pair.Item1]=new RuntimeObject("Reactive"){Data={{"Value",pair.Item2}}};
        property.Data["maxHP"]=RuntimeObject.Box(5);property.Data["ExtraMovePoint"]=0;
        var shown=new RuntimeObject("Tuple"){Data={{"Item1",new RuntimeObject("List")},{"Item2",new RuntimeObject("List")}}};
        var buffs=new RuntimeObject("Buffs");buffs.Data["shown"]=shown;
        var player=new RuntimeObject("Player"){Data={{"Property",property},{"characterType",type},{"CharacterInst",new RuntimeObject("Character")},
            {"player",new RuntimeObject("RoomPlayer"){Data={{"Id",id}}}},{"buffContainer",buffs}}};
        Owners[id]=player;return player;
    }
    internal static RuntimeObject Plate(string kind,RuntimeObject? owner){var ui=new RuntimeObject(kind);ui.Data["PlayerData"]=owner;if(kind=="Attr"){foreach(var name in new[]{"txt_Number","txt_HP","graph_Line","graph_Sign","list_Buff","graph_ShowBuff"})ui.Data[name]=new RuntimeObject(name=="list_Buff"?"List":name.StartsWith("graph")?"Graph":"Label");ui.Data["txt_Name"]=new RuntimeObject("Anchor"){Data={{"xMin",48f},{"yMin",9f},{"width",68f},{"height",44f}}};ui.Data["graph_Guide"]=new RuntimeObject("Anchor"){Data={{"xMin",199f},{"yMin",53f},{"width",5f},{"height",4f}}};}else ui.Data["loader_State"]=new RuntimeObject("Anchor"){Data={{"xMin",120f},{"yMin",64f},{"width",60f},{"height",60f}}};return ui;}
    internal RuntimeObject Add(string kind,RuntimeObject owner,RuntimeObject? parent=null){var ui=Plate(kind,owner);(parent??(kind=="Attr"?AttrParent:PlayerParent)).Call("AddChild",ui);return ui;}
}
public static class Check {
    private static int checks;private static readonly Type Product=typeof(FieldBuffUi);
    private static readonly MethodInfo Refresh=Product.GetMethod("RefreshCounters",BindingFlags.NonPublic|BindingFlags.Static)!;
    private static void Assert(bool good,string name){checks++;if(!good)throw new Exception(name);}
    private static void Scan(World w)=>Refresh.Invoke(null,new object[]{w.Ui});
    private static RuntimeObject Icon(RuntimeObject plate)=>NativeUi.Created.Single(x=>!x.Disposed&&x.Parent==plate);
    private static int Shown=>NativeUi.Created.Count(x=>!x.Disposed&&GameUi.Visible(x));
    private static void Frame(){Time.unscaledTime+=.21f;FieldBuffUi.Tick();}
    public static int Run(){checks=0;
        var w=new World();var monster=World.Player(1,true);var m=w.Add("Attr",monster);var hero=World.Player(2,true,1);var h=w.Add("PlayerAttr",hero);Scan(w);
        Assert(Shown==2,"Both public hero and monster counter owners are shown");Assert(Icon(m).Data["touchable"] is false&&Icon(h).Data["touchable"] is false,"Counter icons never intercept input");Assert((float)Icon(m).Data["x"]! ==207f&&(float)Icon(m).Data["y"]! ==13f,"Attribute header anchor");Assert((float)Icon(h).Data["x"]! ==186f&&(float)Icon(h).Data["y"]! ==76f,"Native player state anchor");Assert(m.Writes==0&&h.Writes==0,"Native plate state is untouched");
        var icon=Icon(m);var writes=icon.Writes;for(var i=0;i<100;i++)Scan(w);Assert(NativeUi.Created.Count==2&&icon.Writes==writes,"Idle scans neither recreate nor write geometry/visibility");
        monster.Get("Property")!.Field("Counter")!.Data["Value"]=false;Scan(w);Assert(!icon.Visible&&Shown==1,"Public counter false hides own badge only");monster.Get("Property")!.Field("Counter")!.Data["Value"]=true;Scan(w);Assert(icon.Visible&&NativeUi.Created.Count==2,"Public counter restored without recreation");
        m.Alpha=.3f;w.AttrParent.Alpha=.6f;Assert(icon.Alpha==1&&icon.Parent!.Alpha*.6f==.18f,"Fade inherited from native plate/parent without alpha writes");m.Visible=false;Scan(w);Assert(!icon.Visible&&Shown==1,"Hidden native owner hides badge");m.Visible=true;Scan(w);Assert(icon.Visible,"Native reveal restores badge");
        ((RuntimeObject)m.Data["graph_Guide"]!).Data["xMin"]=220f;Scan(w);Assert((float)icon.Data["x"]! ==228f,"Native responsive header geometry followed");((RuntimeObject)m.Data["graph_Guide"]!).Data["xMin"]=float.NaN;Scan(w);Assert(!icon.Visible,"Nonfinite anchor fails closed");((RuntimeObject)m.Data["graph_Guide"]!).Data["xMin"]=199f;Scan(w);
        var replacement=World.Player(3,true);m.Data["PlayerData"]=replacement;Scan(w);Assert(icon.Disposed&&Icon(m)!=icon,"Reused plate with different owner replaces private badge");icon=Icon(m);m.Data["PlayerData"]=null;Scan(w);Assert(icon.Disposed,"Missing owner disposes stale badge");
        w=new World();hero=World.Player(4,true,1);h=w.Add("PlayerAttr",hero);var clone=w.Add("Attr",hero);var plates=(IDictionary)Product.GetField("Plates",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;var plate=Activator.CreateInstance(Product.GetNestedType("Plate",BindingFlags.NonPublic)!,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{hero,w.AttrParent},null)!;var mod=(RuntimeObject)plate.GetType().GetField("Ui",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plate)!;mod.Visible=true;plates.Add(mod.Pointer,plate);Scan(w);Assert(Shown==1&&Icon(mod).Visible,"Mod hero HP clone wins over duplicate native player/attribute plates");Assert(NativeUi.Created.Count==1,"No duplicate counter created for same visible owner");mod.Visible=false;Scan(w);Assert(Shown==1,"Hidden clone permits native visible owner fallback without double display");mod.Visible=true;Scan(w);Assert(Shown==1&&Icon(mod).Visible,"Returning clone suppresses duplicate fallback");
        Product.GetField("_scanAt",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,float.PositiveInfinity);GameUi.Path=new[]{mod};Input.mousePosition=new Vector3(10,10,0);Input.Down=Input.Held=true;FieldBuffUi.Tick();Input.Down=false;Scan(w);Input.Up=true;Input.Held=false;GameUi.Path=new[]{Icon(mod),mod};FieldBuffUi.Tick();Assert(mod.Opens==1,"Badge refresh does not cancel pending buff click or steal hit ancestry");Input.Up=false;
        foreach(var gate in new[]{"setting","settings","root","pvp","combat","parent"}){w=new World();m=w.Add("Attr",World.Player(5,true));var nativeArt=new RuntimeObject("NativeState"){Parent=m};m.Children.Add(nativeArt);Frame();icon=Icon(m);var nativeChildren=m.Children.Count;switch(gate){case "setting":Plugin.FieldBuffs.Value=false;break;case "settings":ModUi.IsOpen=true;break;case "root":GameUi.Root=null;break;case "pvp":w.Info.Data["MapType"]=1;break;case "combat":w.Fight.Data["fightStatus"]=RuntimeObject.Box(true);break;case "parent":w.AttrParent.Visible=false;break;}Frame();Assert(icon.Disposed&&!m.Disposed&&m.Writes==0,"Cleanup preserves native owner: "+gate);Assert(m.Children.Count==nativeChildren-1&&!nativeArt.Disposed&&nativeArt.Writes==0,"Only private badge removed, native state art unchanged: "+gate);}
        w=new World();m=w.Add("Attr",World.Player(6,true));Frame();icon=Icon(m);FieldIndicatorOpacity.Hidden=true;w.AttrParent.Visible=false;Frame();Assert(!icon.Disposed&&!GameUi.Visible(icon),"Large zoom hide uses inherited parent ownership, not repeated disposal");FieldIndicatorOpacity.Hidden=false;w.AttrParent.Visible=true;Frame();Assert(Icon(m)==icon&&GameUi.Visible(icon),"Zoom reveal preserves same badge");
        w=new World();m=w.Add("Attr",World.Player(7,true));Scan(w);icon=Icon(m);m.Parent!.Children.Remove(m);m.Parent=null;Scan(w);Assert(icon.Disposed&&!m.Disposed,"Detached native plate removes only private badge");
        w=new World();m=w.Add("Attr",World.Player(8,true));Scan(w);icon=Icon(m);m.Disposed=true;Scan(w);Assert(icon.Disposed,"Disposed native plate cleans badge safely");
        w=new World();m=w.Add("Attr",World.Player(9,true));Scan(w);icon=Icon(m);w.Ui.Data["com_AttrInfos"]=new RuntimeObject("Parent"){Parent=w.Root};Scan(w);Assert(icon.Disposed&&!m.Disposed,"Replaced parent removes old owned icon only");
        w=new World();m=w.Add("Attr",World.Player(10,false));Scan(w);Assert(NativeUi.Created.Count==0,"Counter false never fabricates artwork");m.Get("PlayerData")!.Get("Property")!.Data["Counter"]=null;Scan(w);Assert(NativeUi.Created.Count==0,"Unknown public counter is no icon");w.AttrParent.Children.Add(new RuntimeObject("Unknown"));Scan(w);Assert(NativeUi.Created.Count==0,"Unknown field children are never inspected as units");
        FieldBuffUi.Clear("fixture-end");Console.WriteLine("PASS "+checks+" actual-source field-counter assertions; no native/game/RPC executed.");return checks;
    }
}
}
