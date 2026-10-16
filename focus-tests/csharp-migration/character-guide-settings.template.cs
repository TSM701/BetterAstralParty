#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace UnityEngine {
internal struct Vector2 { internal float x,y;internal Vector2(float x,float y){this.x=x;this.y=y;}internal static Vector2 zero=>new(0,0); }
internal struct Vector3 { internal float x,y,z;internal Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
internal struct Color { internal Color(float r,float g,float b){}internal static Color white=>new(1,1,1); }
internal static class Application { internal static bool isFocused=true; }
internal static class Screen { internal static int width=1920,height=1080; }
internal static class Input { internal static Vector3 mousePosition=new(0,1080,0);internal static Vector2 mouseScrollDelta=new(0,0); }
internal static class Time { internal static float unscaledTime=1; }
}
namespace BetterAstralParty {
internal sealed class Setting { internal bool Value=true; }
internal static class Plugin { internal static readonly Setting CharacterGuide=new(),CharacterRoles=new(); }
internal sealed class RuntimeObject {
    static long _next;internal readonly IntPtr Pointer=new(++_next);internal readonly string TypeName;
    internal readonly Dictionary<string,object?> Values=new();internal object? Scalar;internal int ResultReads;internal string? FailCall;
    internal RuntimeObject(string type="Fixture"){TypeName=type;Values["isDisposed"]=false;Values["visible"]=true;Values["width"]=100f;Values["height"]=100f;Values["x"]=0f;}
    internal RuntimeObject? Field(string name)=>Get(name);
    internal RuntimeObject? Get(string name)=>Values.TryGetValue(name,out var value)?value as RuntimeObject:null;
    internal T Get<T>(string name)where T:unmanaged=>Values.TryGetValue(name,out var value)&&value is T typed?typed:throw new InvalidOperationException("Unexpected typed read: "+TypeName+"."+name);
    internal T Value<T>()where T:unmanaged=>Scalar is T value?value:throw new InvalidOperationException("Unexpected scalar");
    internal string String()=>Scalar as string??throw new InvalidOperationException("Unexpected string");
    internal void Set(string name,object value)=>Values[name]=value;
    internal RuntimeObject? Call(string name,params object[] args) {
        if(FailCall==name)throw new MissingMethodException("Injected role-only call failure: "+name);
        if(name=="GetResult"){ResultReads++;return Get("result");}
        if(name is "get_Item" or "GetChildAt")return Values.TryGetValue("items",out var items)?((List<RuntimeObject>)items!)[(int)args[0]]:Values[args[0].ToString()!] as RuntimeObject;
        if(name=="ContainsKey")return new(){Scalar=Values.ContainsKey(args[0].ToString()!)};
        if(name is "LocalToRoot" or "RootToLocal") {var v=(Vector2)args[0];for(RuntimeObject? p=this;p!=null;p=p.Get("parent"))v.x+=(name=="LocalToRoot"?1:-1)*p.Get<float>("x");return new(){Scalar=v};}
        if(name=="SetXY"){Values["x"]=(float)args[0];Values["y"]=(float)args[1];return null;}
        throw new InvalidOperationException("Unexpected native call: "+TypeName+"."+name);
    }
    internal static int ClassReads,PackageRequests,Bindings;internal static bool FailLocal=false;internal static RuntimeObject? Potentials;
    internal static IntPtr FindClass(string ns,string name){ClassReads++;return name switch{"UIPackage"=>(IntPtr)1,"UIHeroPanel"=>(IntPtr)2,"StaticConfigure"=>(IntPtr)3,"UIHelper"=>(IntPtr)4,_=>throw new InvalidOperationException("Unexpected class "+ns+"."+name)};}
    internal static RuntimeObject? StaticCall(IntPtr type,string name,params object[] args) {
        if(type==(IntPtr)1&&name=="GetByName"){PackageRequests++;return new("Package");}
        if(type==(IntPtr)2&&name=="BindAll"){Bindings++;return null;}
        if(type==(IntPtr)3&&name=="get_PVENurturance")return Potentials;
        if(type==(IntPtr)4&&name=="GetLocal"){if(FailLocal)throw new MissingMethodException("Injected guide-only GetLocal failure");return new(){Scalar="localized"};}
        throw new InvalidOperationException("Unexpected static call: "+name);
    }
    internal static RuntimeObject? StaticField(IntPtr type,string name)=>throw new InvalidOperationException("Unexpected static field");
    internal static RuntimeObject New(IntPtr type)=>new();
    internal static int EnumValue(string ns,string type,string name)=>ns=="UI"&&type=="UIStringType"&&name is "PVENurturance" or "Character"?1:throw new InvalidOperationException("Unexpected enum query");
}
internal static class Compatibility {
    internal static readonly Dictionary<string,Exception> Blocked=new();
    internal static bool Allowed(string name)=>!Blocked.ContainsKey(name);
    internal static void Block(string name,Exception error,Action clear){if(Blocked.TryAdd(name,error))clear();}
}
internal static class GameUi {
    internal static RuntimeObject? Root,Room;internal static int PointerReads;
    internal static RuntimeObject? Find(RuntimeObject root,string name)=>name=="UIRoomHeroPanel"?Room:null;
    internal static bool Visible(RuntimeObject obj)=>!obj.Get<bool>("isDisposed")&&obj.Get<bool>("visible");
    internal static RuntimeObject[] PointerPath(){PointerReads++;return Room==null?Array.Empty<RuntimeObject>():new[]{Room};}
    internal static void Dispose(RuntimeObject? obj){if(obj!=null)obj.Values["isDisposed"]=true;}
}
internal static class NativeUi {
    internal static int Created;
    internal static RuntimeObject? Create(string package,string item){Created++;return new("Template");}
    internal static RuntimeObject Component(RuntimeObject parent,float width,float height)=>throw new InvalidOperationException("Unexpected popup build");
    internal static RuntimeObject Label(RuntimeObject parent,string text,float x,float y,float width,float height,float font,bool dark=false)=>throw new InvalidOperationException("Unexpected badge build");
    internal static void OutlineTree(RuntimeObject obj){}
    internal sealed class Surface { internal readonly RuntimeObject Graph=new();internal Surface(RuntimeObject parent,float width,float height,bool fixedOpacity){} }
}
internal static class ModUi { internal static bool IsOpen=false; }
internal static class ModText { internal static int Revision=0;internal static bool Korean=>true; }
internal static class CharacterRole { internal static string Label(int role,bool korean)=>""; }
internal static class AdviceText { internal static float Reveal(float elapsed)=>1; }
internal static class VisibleCombatAdvisor { internal static bool IsPve(int map)=>map==4; }
__SOURCE__
internal static class GuideSettingsCheck {
    internal static int Assertions;
    private static readonly Type Guide=typeof(CharacterGuideUi);
    private static FieldInfo Field(string name)=>Guide.GetField(name,BindingFlags.Static|BindingFlags.NonPublic)??throw new InvalidOperationException("Missing actual production field: "+name);
    private static T Read<T>(string name)=>(T)Field(name).GetValue(null)!;
    private static void Set(string name,object? value)=>Field(name).SetValue(null,value);
    private static void Assert(bool value,string message){Assertions++;if(!value)throw new InvalidOperationException("Guide settings assertion: "+message+"; breakers="+string.Join(";",Compatibility.Blocked.Select(pair=>pair.Key+":"+pair.Value)));}
    private sealed class Scene {
        internal readonly RuntimeObject Root=new("Root"),Room=new("Room");internal readonly RuntimeObject[] Players=new RuntimeObject[4];
        internal Scene(){CharacterGuideUi.Clear();Compatibility.Blocked.Clear();RuntimeObject.FailLocal=false;RuntimeObject.Potentials=null;Set("_heroLoad",null);Set("_heroBound",false);Set("_guideEnabled",true);Set("_rolesEnabled",true);Set("_config",IntPtr.Zero);Set("_helper",IntPtr.Zero);Plugin.CharacterGuide.Value=Plugin.CharacterRoles.Value=true;GameUi.Root=Root;GameUi.Room=Room;Root.Values["width"]=1920f;Root.Values["height"]=1080f;Time.unscaledTime+=1;
            for(int i=0;i<4;i++){var p=Players[i]=new("Player");p.Values["mapType"]=new RuntimeObject(){Scalar=4};p.Values["isChoose"]=new RuntimeObject();p.Get("isChoose")!.Values["selectedIndex"]=0;p.Values["loader_Character"]=new RuntimeObject();var swap=new RuntimeObject("Swap");swap.Values["x"]=20f;var button=new RuntimeObject();button.Values["x"]=12f;button.Values["parent"]=swap;swap.Values["btn_ChangeSlot"]=button;p.Values["com_ChangeSlot"]=swap;Room.Values["com_player_"+(i+1)]=p;}
        }
    }
    private static RuntimeObject SeedPopup(){var popup=new RuntimeObject("Popup");Set("_popup",popup);Set("_content",new RuntimeObject());Set("_descriptionKey","retained description");return popup;}
    private static RuntimeObject SeedBadge(){var panel=new RuntimeObject("Badge");var type=Guide.GetNestedType("Badge",BindingFlags.NonPublic)!;var badge=Activator.CreateInstance(type,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object?[]{new RuntimeObject(),panel,new RuntimeObject(),new RuntimeObject(),null},null)!;((IDictionary)Field("Badges").GetValue(null)!).Add((IntPtr)777,badge);return panel;}
    private static void PublicGuideData(Scene scene){
        var player=scene.Players[0];player.Get("isChoose")!.Values["selectedIndex"]=1;var hero=new RuntimeObject("PublicHero");var config=new RuntimeObject("PublicConfig");config.Values["TagType"]=0;hero.Values["InfoConfig"]=config;player.Values["selectHero"]=hero;
        var breaks=new RuntimeObject();breaks.Values["Count"]=1;breaks.Values["items"]=new List<RuntimeObject>{new(){Scalar=123}};config.Values["PveBreak"]=breaks;
        var detail=new RuntimeObject();var rows=new RuntimeObject();var row=new RuntimeObject("UIRoomHero_Com_Skill");var name=new RuntimeObject();name.Values["text"]=new RuntimeObject(){Scalar="public skill"};row.Values["txt_SkillName"]=name;var desc=new RuntimeObject();desc.Values["text"]=new RuntimeObject(){Scalar="public description"};row.Values["txt_SkillDesc"]=desc;var kind=new RuntimeObject();kind.Values["selectedIndex"]=0;row.Values["skillType"]=kind;rows.Values["numChildren"]=1;rows.Values["items"]=new List<RuntimeObject>{row};detail.Values["list_Skill"]=rows;player.Values["btn_DetailInfo"]=detail;
        var table=new RuntimeObject();var potential=new RuntimeObject();potential.Values["DescriptionID"]=123;table.Values["123"]=potential;RuntimeObject.Potentials=new();RuntimeObject.Potentials.Values["BreakDict"]=table;
    }
    internal static void Run(){
        var s=new Scene();CharacterGuideUi.Tick();Assert(Read<ICollection>("Players").Count==4,"default both-on observes four public preparation slots");Assert(((IDictionary)Field("SwapPositions").GetValue(null)!).Count==4,"default roles retains original swap alignment");
        Time.unscaledTime+=.25f;CharacterGuideUi.Tick();Assert(s.Players.All(p=>p.Get("com_ChangeSlot")!.Get<float>("x")==-12f),"periodic scan keeps native swap alignment idempotent");
        var template=Read<RuntimeObject>("_cardTemplate");var popup=SeedPopup();Plugin.CharacterGuide.Value=false;CharacterGuideUi.Tick();
        Assert(popup.Get<bool>("isDisposed")&&Field("_popup").GetValue(null)==null,"guide off hides and disposes only its popup");Assert(!template.Get<bool>("isDisposed")&&((IDictionary)Field("SwapPositions").GetValue(null)!).Count==4,"roles-on survives guide off");Assert(Read<ICollection>("Players").Count==4,"role-only mode retains public player observation");
        s=new Scene();CharacterGuideUi.Tick();popup=SeedPopup();var badge=SeedBadge();template=Read<RuntimeObject>("_cardTemplate");var reads=GameUi.PointerReads;s.Players[0].Get("isChoose")!.Values["selectedIndex"]=1;Plugin.CharacterRoles.Value=false;CharacterGuideUi.Tick();
        Assert(badge.Get<bool>("isDisposed")&&template.Get<bool>("isDisposed")&&((IDictionary)Field("Badges").GetValue(null)!).Count==0,"roles off removes owned badges/template");Assert(!popup.Get<bool>("isDisposed")&&Read<ICollection>("Players").Count==4&&GameUi.PointerReads>reads&&Read<IntPtr>("_hover")==s.Players[0].Pointer,"guide-only mode retains popup ownership, hover path and public players");
        Assert(s.Players.All(p=>p.Get("com_ChangeSlot")!.Get<float>("x")==20f),"roles off restores exactly owned native swap coordinates");
        var created=NativeUi.Created;Plugin.CharacterRoles.Value=true;CharacterGuideUi.Tick();Assert(NativeUi.Created>created&&((IDictionary)Field("SwapPositions").GetValue(null)!).Count==4,"roles re-enable rescans and rebuilds presentation");
        s.Players[0].Get("com_ChangeSlot")!.Values["x"]=777f;Plugin.CharacterRoles.Value=false;CharacterGuideUi.Tick();Assert(s.Players[0].Get("com_ChangeSlot")!.Get<float>("x")==777f,"off never overwrites another owner's swap position");
        popup=SeedPopup();badge=SeedBadge();var pending=new RuntimeObject("Awaiter");pending.Values["IsCompleted"]=false;pending.Values["result"]=new RuntimeObject("LoadedPackage");Set("_heroLoad",pending);Plugin.CharacterGuide.Value=false;var classes=RuntimeObject.ClassReads;CharacterGuideUi.Tick();
        Assert(popup.Get<bool>("isDisposed")&&badge.Get<bool>("isDisposed")&&Read<ICollection>("Players").Count==0&&Read<IntPtr>("_hover")==IntPtr.Zero,"both off clears every owned presentation and observed slot");Assert(ReferenceEquals(Field("_heroLoad").GetValue(null),pending)&&pending.ResultReads==0&&RuntimeObject.ClassReads==classes,"both off retains incomplete awaiter without starting more native loads");
        GameUi.Root=null;pending.Values["IsCompleted"]=true;CharacterGuideUi.Tick();CharacterGuideUi.Tick();Assert(pending.ResultReads==1&&Field("_heroLoad").GetValue(null)==null,"late package completion is safely observed exactly once even with both off and no UI root");
        GameUi.Root=s.Root;Plugin.CharacterGuide.Value=true;CharacterGuideUi.Tick();Assert(Read<ICollection>("Players").Count==4&&Read<IntPtr>("_hover")==s.Players[0].Pointer,"guide re-enable resumes public hover independently of roles");
        foreach(var player in s.Players)player.Get("mapType")!.Scalar=1;Set("_scanAt",0f);Set("_room",null);CharacterGuideUi.Tick();Assert(Read<ICollection>("Players").Count==0,"unknown/PvP player slots do not enter the guide tracker");
        Assert(Compatibility.Blocked.Count==0,"normal independent-setting cases never trip a compatibility breaker");
        s=new Scene();CharacterGuideUi.Tick();PublicGuideData(s);CharacterGuideUi.Tick();badge=SeedBadge();template=Read<RuntimeObject>("_cardTemplate");Set("_scanAt",Time.unscaledTime+10);RuntimeObject.FailLocal=true;Time.unscaledTime+=.25f;CharacterGuideUi.Tick();
        Assert(Compatibility.Blocked.TryGetValue("CharacterGuide",out var failure)&&failure is MissingMethodException&&failure.Message.Contains("GetLocal",StringComparison.Ordinal)&&Compatibility.Allowed("CharacterRoles"),"native description failure blocks only CharacterGuide");
        Assert(!badge.Get<bool>("isDisposed")&&!template.Get<bool>("isDisposed")&&((IDictionary)Field("SwapPositions").GetValue(null)!).Count==4,"guide-only failure preserves normal owned role badge/template/swap presentation");
        Plugin.CharacterGuide.Value=false;CharacterGuideUi.Tick();Plugin.CharacterGuide.Value=true;CharacterGuideUi.Tick();Assert(Compatibility.Allowed("CharacterRoles")&&!badge.Get<bool>("isDisposed"),"blocked guide OFF/re-ON never blocks or clears roles");
        s=new Scene();CharacterGuideUi.Tick();popup=SeedPopup();s.Players[0].Get("isChoose")!.Values["selectedIndex"]=1;s.Players[0].Get("com_ChangeSlot")!.Get("btn_ChangeSlot")!.FailCall="LocalToRoot";Set("_scanAt",0f);CharacterGuideUi.Tick();
        Assert(!Compatibility.Allowed("CharacterRoles")&&Compatibility.Allowed("CharacterGuide")&&Read<ICollection>("Players").Count==4,"role alignment failure blocks only CharacterRoles and retains public guide tracking");
        Assert(!popup.Get<bool>("isDisposed")&&Read<IntPtr>("_hover")==s.Players[0].Pointer&&s.Players.All(p=>p.Get("com_ChangeSlot")!.Get<float>("x")==20f),"role-only failure restores owned swaps without clearing guide popup/hover");
        Plugin.CharacterRoles.Value=false;CharacterGuideUi.Tick();Plugin.CharacterRoles.Value=true;CharacterGuideUi.Tick();Assert(Compatibility.Allowed("CharacterGuide")&&!popup.Get<bool>("isDisposed"),"blocked role OFF/re-ON never blocks or clears guide");
        CharacterGuideUi.Clear();
    }
}
}
