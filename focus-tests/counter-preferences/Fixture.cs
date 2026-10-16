using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppSystem {
    public class Object { public IntPtr Pointer; public Object(IntPtr pointer){Pointer=pointer;} }
    public sealed class Array : Object {
        public Array(IntPtr pointer):base(pointer){}
        public void SetValue(Object value,int index) {
            if(Probe.Arrays[Pointer].Element!=Probe.ItemClass)throw new InvalidOperationException("Not a native TransitionItem array");
            Probe.Arrays[Pointer].Items[index]=value;Probe.NativeStores++;
        }
    }
}
namespace Il2CppInterop.Runtime {
    public static class IL2CPP {
        public static IntPtr il2cpp_object_get_class(IntPtr pointer)=>Probe.NullClass?IntPtr.Zero:pointer;
        public static int il2cpp_class_get_rank(IntPtr klass)=>Probe.Arrays[klass].Rank;
        public static IntPtr il2cpp_class_get_element_class(IntPtr klass)=>Probe.Arrays[klass].Element;
        public static IntPtr il2cpp_array_new(IntPtr element,ulong count) {
            Probe.Allocations++;if(Probe.FailAllocation)return IntPtr.Zero;
            var pointer=Probe.Pointer();Probe.Arrays[pointer]=new Probe.ArrayData(element,new Il2CppSystem.Object[checked((int)count)]);return pointer;
        }
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
    public sealed class Il2CppReferenceArray<T> where T:Il2CppSystem.Object {
        public IntPtr Pointer;public Il2CppReferenceArray(IntPtr pointer){Pointer=pointer;}
        public int Length{get{Probe.LengthReads++;return Probe.Arrays[Pointer].Items.Length;}}
        public T this[int index]{get{Probe.ItemReads++;return (T)Probe.Arrays[Pointer].Items[index];}set=>throw new Exception("Raw reference index writes are forbidden in this fixture");}
    }
}
sealed class RuntimeObject {
    public IntPtr Pointer;
    public RuntimeObject(){Pointer=Probe.Pointer();Probe.Data[Pointer]=new();}
    public RuntimeObject(IntPtr pointer){Pointer=pointer;}
    Dictionary<string,object> Data=>Probe.Data[Pointer];
    public RuntimeObject? Get(string name)=>Data.TryGetValue(name,out var value)?value as RuntimeObject:null;
    public RuntimeObject? Field(string name){if(name is "type" or "targetId" or "value")Probe.ItemFieldReads++;return Get(name);}
    public T Get<T>(string name) where T:unmanaged=>name=="numChildren"?(T)(object)Children.Count:(T)Data[name];
    public T Value<T>()where T:unmanaged=>(T)Data["value"];
    public string String()=>(string)Data["value"];
    public void Set(string name,object value)=>Data[name]=value;
    public void SetField(string name,string value)=>Set(name,Text(value));
    public void SetField(string name,int value)=>Set(name,value);
    public void SetField(string name,RuntimeObject value){if(name=="_items"&&Probe.Arrays[value.Pointer].Element!=Probe.ItemClass)throw new Exception("Object[] cannot substitute for TransitionItem[]");Set(name,value);}
    public List<RuntimeObject> Children {get {if(!Data.TryGetValue("children",out var children))Data["children"]=children=new List<RuntimeObject>();return (List<RuntimeObject>)children;}}
    public RuntimeObject? Call(string name,params object?[] args){
        switch(name){case "GetTransition":return Get("transition");case "GetChildAt":return Children[(int)args[0]!];case "AddChild":Children.Add((RuntimeObject)args[0]!);return null;default:return null;}
    }
    public static RuntimeObject Text(string text){var value=new RuntimeObject();value.Set("value",text);return value;}
    public static RuntimeObject Integer(int number){var value=new RuntimeObject();value.Set("value",number);return value;}
    public static IntPtr FindClass(string ns,string name)=>ns=="FairyGUI"&&name=="TransitionItem"?Probe.ItemClass:throw new Exception("Unexpected native class");
    public static int EnumValue(string ns,string name,string field)=>ns=="FairyGUI"&&name=="TransitionActionType"&&field=="Sound"?Probe.SoundType:throw new Exception("Named native enum required");
}
static class Plugin {
    public sealed class Setting{public bool Value=true;}
    public static Setting CounterSound=new(),CounterAnimation=new();
}
static class GameUi {public static void Dispose(RuntimeObject value){Probe.Disposed++;value.Set("disposed",true);}}
static class Probe {
    public sealed record ArrayData(IntPtr Element,Il2CppSystem.Object[] Items,int Rank=1);
    public static readonly IntPtr ItemClass=(IntPtr)40;
    public const int SoundType=73;
    static int next=100;
    public static IntPtr Pointer()=>(IntPtr)(++next);
    public static Dictionary<IntPtr,Dictionary<string,object>> Data=new();
    public static Dictionary<IntPtr,ArrayData> Arrays=new();
    public static int Allocations,NativeStores,Disposed,LengthReads,ItemReads,ItemFieldReads,VisualCount=2,ArrayRank=1;
    public static bool FailAllocation,WrongElement,NullElement,NullClass,NoSound;
    public static RuntimeObject? LastPoint,LastTransition,SoundPayload;
    public static IntPtr OriginalArray;
    public static List<RuntimeObject> Components=new();
    public static RuntimeObject Value(int type,string target){var value=new RuntimeObject();value.Set("type",RuntimeObject.Integer(type));value.Set("targetId",RuntimeObject.Text(target));var payload=new RuntimeObject();payload.Set("sound",510);payload.Set("volume",.8f);value.Set("value",payload);if(type==SoundType)SoundPayload=payload;return value;}
    public static void Reset(){Allocations=NativeStores=Disposed=LengthReads=ItemReads=ItemFieldReads=0;FailAllocation=WrongElement=NullElement=NullClass=NoSound=false;VisualCount=2;ArrayRank=1;Components.Clear();LastPoint=LastTransition=SoundPayload=null;}
}
static partial class NativeUi {
    static RuntimeObject Create(string package,string name){
        if(package!="Fight"||name!="Fight_Com_Point")throw new Exception("Wrong asset");
        var point=new RuntimeObject();point.Set("aMovie_Dice",new RuntimeObject());
        foreach(var name0 in new[]{"txt_Point","txt_max"}){var child=new RuntimeObject();child.Set("id",RuntimeObject.Text(name0));point.Set(name0,child);point.Children.Add(child);}
        var transition=new RuntimeObject();point.Set("transition",transition);
        var items=new List<Il2CppSystem.Object>();for(var i=0;i<Probe.VisualCount;i++){var item=Probe.Value(i,"txt_Point");items.Add(new Il2CppSystem.Object(item.Pointer));}
        if(!Probe.NoSound){var sound=Probe.Value(Probe.SoundType,"txt_max");items.Add(new Il2CppSystem.Object(sound.Pointer));}
        var array=Probe.Pointer();Probe.Arrays[array]=new Probe.ArrayData(Probe.NullElement?IntPtr.Zero:Probe.WrongElement?(IntPtr)999:Probe.ItemClass,items.ToArray(),Probe.ArrayRank);
        transition.Set("_items",new RuntimeObject(array));Probe.OriginalArray=array;Probe.LastPoint=point;Probe.LastTransition=transition;return point;
    }
    static RuntimeObject Component(RuntimeObject parent,float width,float height){var value=new RuntimeObject();value.Set("visible",true);value.Set("id",RuntimeObject.Text("private-"+value.Pointer));parent.Children.Add(value);Probe.Components.Add(value);return value;}
    static RuntimeObject? Icon(RuntimeObject parent,string item,float x,float y,float size){if(item!="Com_Icon_Counter")throw new Exception("Wrong static icon");return new RuntimeObject();}
__COUNTER_SOURCE__
}
public static class CounterPreferencesCheck {
    static int checks;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    static void Rejected(Action action,string message){try{action();}catch(InvalidOperationException){Check(Probe.Disposed==1,message+" disposes only private failed clone");return;}throw new Exception(message+" accepted");}
    public static void Run(){
        foreach(var sound in new[]{false,true})foreach(var animation in new[]{false,true}){
            Probe.Reset();Plugin.CounterSound.Value=sound;Plugin.CounterAnimation.Value=animation;var parent=new RuntimeObject();
            var point=NativeUi.CounterIndicator(parent)!;var transition=Probe.LastTransition!;var array=transition.Field("_items")!.Pointer;
            Check(parent.Children.Count==1&&parent.Children[0].Pointer==point.Pointer,"Only the new private clone is attached");
            Check(!point.Get<bool>("visible")&&!point.Get<bool>("touchable"),"Counter parent retains caller-owned visibility timing");
            Check(!Probe.Components[0].Get<bool>("visible")&&Probe.Components[1].Get<bool>("visible"),"Static counter icon visible while afterimage is hidden");
            Check(Probe.SoundPayload!.Get<int>("sound")== (sound?510:0)&&Probe.SoundPayload.Get<float>("volume")==.8f,"Sound changes only the private native sound ID, never unused volume");
            Check(Probe.Arrays[array].Element==Probe.ItemClass,"Array remains native TransitionItem[]");
            Check(animation?array==Probe.OriginalArray&&Probe.Arrays[array].Items.Length==3:array!=Probe.OriginalArray&&Probe.Arrays[array].Items.Length==1,"Animation setting retains all visual actions or only original sound actions");
            Check(Probe.Allocations==(animation?0:1)&&Probe.NativeStores==(animation?0:1),"Typed native SetValue used once per retained sound, never per frame");
            Check(Probe.Arrays[Probe.OriginalArray].Items.Length==3,"Original private action array is not rewritten or truncated");
        }
        Probe.Reset();Probe.NoSound=true;Plugin.CounterAnimation.Value=false;var emptyParent=new RuntimeObject();NativeUi.CounterIndicator(emptyParent);
        Check(Probe.Arrays[Probe.LastTransition!.Field("_items")!.Pointer].Items.Length==0&&Probe.NativeStores==0,"No-sound transition accepts a correctly typed empty array");
        foreach(var sound in new[]{false,true})foreach(var animation in new[]{false,true})foreach(var shape in new[]{"null-class","rank-zero","rank-two","wrong-element","null-element"}){
            Probe.Reset();Plugin.CounterSound.Value=sound;Plugin.CounterAnimation.Value=animation;
            Probe.NullClass=shape=="null-class";Probe.ArrayRank=shape=="rank-zero"?0:shape=="rank-two"?2:1;
            Probe.WrongElement=shape=="wrong-element";Probe.NullElement=shape=="null-element";
            var badParent=new RuntimeObject();Rejected(()=>NativeUi.CounterIndicator(badParent),"Invalid "+shape+" with sound="+sound+" animation="+animation);
            Check(Probe.LengthReads==0&&Probe.ItemReads==0&&Probe.ItemFieldReads==0,"Invalid shape is rejected before any array length, element or action-field interpretation");
            Check(Probe.Allocations==0&&Probe.NativeStores==0&&badParent.Children.Count==0,"Invalid shape is neither rewritten nor attached");
        }
        Probe.Reset();Probe.FailAllocation=true;Plugin.CounterAnimation.Value=false;Rejected(()=>NativeUi.CounterIndicator(new RuntimeObject()),"Allocation failure");
        Probe.Reset();Probe.VisualCount=257;Rejected(()=>NativeUi.CounterIndicator(new RuntimeObject()),"Unbounded action inventory");
        Console.WriteLine("PASS private counter sound/animation preferences: "+checks+" checks");
    }
}
