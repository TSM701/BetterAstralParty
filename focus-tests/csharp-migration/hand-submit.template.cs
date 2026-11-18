#nullable enable
using System;
using System.Collections.Generic;
namespace __NS__ {
__LAYOUT__
struct Vector2 {
    public float x,y; public Vector2(float x,float y) {this.x=x;this.y=y;}
    public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
    public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y;
    public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b);
    public override bool Equals(object? value)=>value is Vector2 other&&this==other;
    public override int GetHashCode()=>HashCode.Combine(x,y);
}
struct Color {
    public float r,g,b,a; public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
}
sealed class RuntimeObject {
    public readonly Dictionary<string,object> Data = new() { ["isDisposed"]=false, ["alpha"]=1f,
        ["visible"]=true, ["touchable"]=true, ["x"]=0f,["y"]=0f,["width"]=1f,["height"]=1f,["rotation"]=0f };
    public int Writes; public static int Reads;
    public RuntimeObject Field(string key)=>(RuntimeObject)Data[key];
    public RuntimeObject Get(string key){Reads++;return (RuntimeObject)Data[key];}
    public T Get<T>(string key){Reads++;return (T)Data[key];}
    public T Value<T>()=>(T)Data["boxed"];
    public void Set(string key,object value){Writes++;Data[key]=value;}
    public RuntimeObject? Call(string name,params object[] args) {
        if(name=="RootToLocal") {
            var point=(Vector2)args[0]; var scale=Data.TryGetValue("localScale",out var value)?(float)value:1f;
            return new RuntimeObject {Data={["boxed"]=new Vector2(point.x/scale,point.y/scale)}};
        }
        if(name=="SetXY"){Set("x",args[0]);Set("y",args[1]);return null;}
        if(name=="SetSize"){Set("width",args[0]);Set("height",args[1]);return null;}
        if(name=="DrawRoundRect"){Set("stroke",args[1]);Set("fill",args[2]);return null;}
        throw new Exception("Unexpected native call: "+name);
    }
    // The production value-result form; the stub answers through Call and unboxes.
    public T Result<T,A>(string name,A a,RuntimeObject? b) where T:struct where A:struct => Call(name,a,b!)!.Value<T>();
}
static class ModText {public static bool Korean=true;}
static class SmoothShape {
    public static void Restyle(RuntimeObject shape,Color fill,float line,Color lineColor){var s=shape.Get("shape");s.Set("stroke",lineColor);s.Set("fill",fill);s.Set("line",line);}
}
static class GameUi {
    public static void StyleText(RuntimeObject label,int size)=>label.Set("fontSize",size);
    public static void Dispose(RuntimeObject? node){if(node!=null)node.Set("isDisposed",true);}
}
static class NativeUi {
    public static readonly List<RuntimeObject> Created=new();
    public sealed class Surface {
        public readonly RuntimeObject Graph;
        public Surface(RuntimeObject parent,float width,float height,bool fixedOpacity=false,Color? fill=null) {
            Graph=new RuntimeObject {Data={["parent"]=parent,["shape"]=new RuntimeObject()}};
            Graph.Set("touchable",false);Resize(width,height);Created.Add(Graph);
        }
        public void Resize(float width,float height)=>Graph.Call("SetSize",width,height);
    }
    public static RuntimeObject Label(RuntimeObject parent,string text,float x,float y,float width,float height,int size) {
        var label=new RuntimeObject {Data={["parent"]=parent}};Created.Add(label);return label;
    }
}
public static class SubmitCheck {
    static RuntimeObject? _hotZone;
__UpdateDropZone__
__TOLOCAL__
    static void Check(bool value){if(!value)throw new Exception("Submit presentation regression");}
    static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
    public static void Run() {
        var root=new RuntimeObject();
        var parent=new RuntimeObject();
        _hotZone=new RuntimeObject {Data={["parent"]=parent,["alpha"]=.3f,["visible"]=false,["touchable"]=false}};
        foreach(var scale in new[]{1f,1.5f}) {
            parent.Data["localScale"]=scale;
            foreach(var height in new[]{720f,1080f,1440f,2160f}) {
                var width=height*16/9;var unit=Math.Min(width/1920,height/1080);var bottom=height*.6f;
                UpdateDropZone(root,width,height,bottom);
                // The zone reaches half a screen above the screen top, where nothing of it is seen.
                var top=height*HandLayout.DropOverhang/scale;
                Check(Near(_hotZone.Get<float>("x"),width*.1f/scale));
                Check(Near(_hotZone.Get<float>("y"),-top));
                Check(Near(_hotZone.Get<float>("width"),width*.8f/scale));
                Check(Near(_hotZone.Get<float>("height"),bottom/scale+top));
                Check(!_hotZone.Get<bool>("visible")&&!_hotZone.Get<bool>("touchable")&&_hotZone.Get<float>("alpha")==.3f);
                var writes=_hotZone.Writes;
                for(int frame=0;frame<100;frame++)UpdateDropZone(root,width,height,bottom);
                Check(writes==_hotZone.Writes);
            }
        }
        parent.Data["localScale"]=1f;
        UpdateDropZone(root,1920,1080,500);
        Check(Near(_hotZone.Get<float>("x"),192)&&Near(_hotZone.Get<float>("y"),-540));
        Check(Near(_hotZone.Get<float>("width"),1536)&&Near(_hotZone.Get<float>("height"),1040));
        Check(NativeUi.Created.Count==0); // The zone draws nothing of its own.
        Console.WriteLine("PASS: production drop bounds (10-90 % wide, half a screen above the screen top down to the card line) at HD–4K/scaled parents; native fade/visibility/input preserved, idle no-write, nothing drawn by the mod.");
    }
}
}
