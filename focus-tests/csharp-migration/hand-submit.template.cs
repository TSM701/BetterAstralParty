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
}
static class ModText {public static bool Korean=true;}
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
    sealed class Entry {public RuntimeObject Card=new();public float? GuideRotation;}
    static RuntimeObject? _hotZone,_dropHint;
    static NativeUi.Surface? _dropHighlight;
    static (Vector2 Size,float Unit,bool Korean)? _dropHintLayout;
__UpdateDropZone__
__UpdateDropHint__
__SetGuideDirection__
__ClearDropHint__
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
                var width=height*16/9;var unit=Math.Min(width/1920,height/1080);
                UpdateDropZone(root,width,height,unit,false,0);
                Check(Near(_hotZone.Get<float>("x"),width*.79f/scale));
                Check(Near(_hotZone.Get<float>("y"),height*.12f/scale));
                Check(Near(_hotZone.Get<float>("width"),width*(.96f-.79f)/scale));
                Check(Near(_hotZone.Get<float>("height"),height*(.72f-.12f)/scale));
                Check(_dropHighlight!.Graph.Get<float>("width")==_hotZone.Get<float>("width"));
                Check(_dropHighlight.Graph.Get<float>("height")==_hotZone.Get<float>("height"));
                Check(!_dropHint!.Get<bool>("touchable")&&!_dropHighlight.Graph.Get<bool>("touchable"));
                Check(ReferenceEquals(_dropHint.Get("parent"),_hotZone));
                Check(ReferenceEquals(_dropHighlight.Graph.Get("parent"),_hotZone));
                Check(!_hotZone.Get<bool>("visible")&&!_hotZone.Get<bool>("touchable")&&_hotZone.Get<float>("alpha")==.3f);
                var writes=_hotZone.Writes+_dropHighlight.Graph.Writes+_dropHint.Writes;
                for(int frame=0;frame<100;frame++)UpdateDropZone(root,width,height,unit,false,0);
                Check(writes==_hotZone.Writes+_dropHighlight.Graph.Writes+_dropHint.Writes);
            }
        }
        Check(NativeUi.Created.Count==2);
        Check(((Color)_dropHighlight!.Graph.Get("shape").Data["stroke"]).a==1f);
        Check(((Color)_dropHighlight.Graph.Get("shape").Data["fill"]).a==.18f);
        var hint=_dropHint!;var highlight=_dropHighlight.Graph;
        ModText.Korean=false;UpdateDropHint(new(300,600),1);
        Check(hint.Get<string>("text")=="Drop card\nhere");
        ModText.Korean=true;UpdateDropHint(new(300,600),1);
        Check(hint.Get<string>("text")=="카드를 이곳에\n놓으세요");
        ClearDropHint();Check(hint.Get<bool>("isDisposed")&&highlight.Get<bool>("isDisposed"));
        Check(_dropHint==null&&_dropHighlight==null&&_dropHintLayout==null);
        parent.Data["localScale"]=1f;
        UpdateDropZone(root,1920,1080,1,true,500);
        Check(_hotZone.Get<float>("x")==480&&Near(_hotZone.Get<float>("y"),86.4f));
        Check(_hotZone.Get<float>("width")==960&&Near(_hotZone.Get<float>("height"),500-86.4f));
        Check(_dropHint==null&&_dropHighlight==null&&NativeUi.Created.Count==2);
        foreach(var original in new[]{0f,12f,-20f}) {
            var guide=new RuntimeObject {Data={["rotation"]=original,["alpha"]=.45f,["visible"]=false,["touchable"]=false}};
            var entry=new Entry {Card=new RuntimeObject {Data={["effectGuide"]=guide}}};
            SetGuideDirection(entry,false);Check(guide.Writes==0); // Native/Click never take ownership.
            SetGuideDirection(entry,true);Check(guide.Get<float>("rotation")==original+90);
            var writes=guide.Writes;RuntimeObject.Reads=0;
            for(int frame=0;frame<100;frame++)SetGuideDirection(entry,true);
            Check(guide.Writes==writes&&RuntimeObject.Reads==0);
            Check(guide.Get<float>("alpha")==.45f&&!guide.Get<bool>("visible")&&!guide.Get<bool>("touchable"));
            SetGuideDirection(entry,false);Check(guide.Get<float>("rotation")==original&&entry.GuideRotation==null);
            SetGuideDirection(entry,true);guide.Set("rotation",45f);
            SetGuideDirection(entry,false);Check(guide.Get<float>("rotation")==45f); // Preserve a foreign write.
            SetGuideDirection(entry,true);entry.Card.Data["isDisposed"]=true;writes=guide.Writes;
            SetGuideDirection(entry,false);Check(guide.Writes==writes&&entry.GuideRotation==null);
        }
        Console.WriteLine("PASS: production hover drop bounds and non-hit artwork agree at HD–4K/scaled parents; KR/EN, native fade/visibility/input preserved, idle no-write, click unchanged.");
        Console.WriteLine("PASS: production guide rotation ownership, Native/Click isolation, foreign-write/disposal cleanup and idle zero reads. Native late-load transfer verified by IL; in-game rendering unverified.");
    }
}
}
