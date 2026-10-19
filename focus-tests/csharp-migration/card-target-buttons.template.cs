#nullable enable
using System;
using System.Collections.Generic;
namespace TargetButtonsTest {
sealed class RuntimeObject {
    static int next;
    public IntPtr Pointer = (IntPtr)(++next);
    public Dictionary<string,object> Data = new(){{"sortingOrder",0},{"isDisposed",false}};
    public List<RuntimeObject> Children = new();
    public int Writes;
    public RuntimeObject? Get(string key)=>Data.GetValueOrDefault(key) as RuntimeObject;
    public RuntimeObject? Field(string key)=>Get(key);
    public T Get<T>(string key)=>(T)Data[key];
    public void Set(string key,object value){
        var old=Data.GetValueOrDefault(key); Data[key]=value; Writes++;
        if(key!="sortingOrder" || Equals(old,value) || Get("parent") is not {} parent)return;
        parent.Children.Remove(this);
        int order=(int)value;
        int index=parent.Children.FindIndex(c=>order==0?c.Get<int>("sortingOrder")!=0:c.Get<int>("sortingOrder")>order);
        parent.Children.Insert(index<0?parent.Children.Count:index,this);
    }
    public void AddChild(RuntimeObject child){
        child.Get("parent")?.Children.Remove(child); child.Data["parent"]=this; Children.Add(child);
    }
    public RuntimeObject? Call(string method,params object[] args){
        var child=(RuntimeObject)args[0];
        if(method=="GetChildIndex")return new(){Data=new(){{"result",Children.IndexOf(child)}}};
        if(method!="SetChildIndex" || child.Get<int>("sortingOrder")!=0)throw new Exception("Unexpected sibling mutation");
        Children.Remove(child);
        int sorted=Children.FindIndex(c=>c.Get<int>("sortingOrder")!=0);
        Children.Insert(Math.Min((int)args[1],sorted<0?Children.Count:sorted),child); return null;
    }
    public T Value<T>()=>(T)Data["result"];
}
static class GameUi {
    public static RuntimeObject? Root=new(), Window, Hand;
    public static RuntimeObject? Find(RuntimeObject root,string name,int depth=0,int maxDepth=4){
        if((name=="CardWindow" || name=="UIHandCardPanel") && maxDepth!=1)throw new Exception("Window/hand queries must stay root-only");
        return name=="CardWindow"?Window:name=="UIHandCardPanel"?Hand:null;
    }
    public static bool Visible(RuntimeObject obj)=>obj.Get<bool>("visible");
}
static class Compatibility {
    public static bool Allowed(string key)=>true;
    public static void Block(string key,Exception ex,Action clear){clear();throw ex;}
}
__SOURCE__
public static class Check {
    sealed class Scene {
        public RuntimeObject Root=new(), Window=new(), HandWindow=new(), Hand=new(), Use=new(),
            Controller=new(){Data=new(){{"selectedIndex",1}}}, Card=new(), Cancel=new(), Sure=new();
        public RuntimeObject[] NativeChildren;
        public Scene(float height){
            CardTargetButtonsUi.Clear();
            Cancel.Data["y"]=Sure.Data["y"]=height*.7f;
            foreach(var button in new[]{Cancel,Sure}){
                button.Data["x"]=42f; button.Data["width"]=298f; button.Data["height"]=101f;
                button.Data["scaleX"]=button.Data["scaleY"]=1f;
            }
            Use.Data["visible"]=true; Use.Data["type"]=Controller; Use.Data["com_ShowCard"]=Card;
            Use.Data["btn_Cancel"]=Cancel; Use.Data["btn_Sure"]=Sure;
            for(int i=0;i<12;i++)Use.AddChild(i==0?Card:i==9?Cancel:i==10?Sure:new());
            NativeChildren=Use.Children.ToArray();
            Window.Data["contentPane"]=new RuntimeObject{Data=new(){{"com_UseCard",Use}}};
            Root.AddChild(new()); Root.AddChild(Window); Root.AddChild(new());
            HandWindow.Data["sortingOrder"]=7; Root.AddChild(HandWindow); HandWindow.AddChild(Hand);
            GameUi.Root=Root; GameUi.Window=Window; GameUi.Hand=Hand;
        }
    }
    public static void Run() {
        void Assert(bool ok,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){if(!ok)throw new Exception("Target button regression at fixture line "+line);}
        foreach(var height in new[]{720f,1080f,2160f}) {
            var s=new Scene(height); var a=s.Cancel; var b=s.Sure;
            CardTargetButtonsUi.Tick();
            Assert(a.Get<float>("y")==height*.7f);
            Assert(a.Get<float>("y")==b.Get<float>("y"));
            Assert(a.Get<int>("sortingOrder")==1 && b.Get<int>("sortingOrder")==1);
            Assert(s.Window.Get<int>("sortingOrder")==8);
            CardTargetButtonsUi.Tick(); Assert(a.Writes==1);

            // A native panel Show or a later hand discovery must work with the same target instance.
            s.HandWindow.Set("sortingOrder",1000); CardTargetButtonsUi.Tick();
            Assert(s.Window.Get<int>("sortingOrder")==1001);
            s.HandWindow.Set("sortingOrder",1100); CardTargetButtonsUi.Tick();
            Assert(s.Window.Get<int>("sortingOrder")==1101);
            s.Controller.Set("selectedIndex",0); CardTargetButtonsUi.Tick();
            Assert(a.Get<int>("sortingOrder")==0 && s.Window.Get<int>("sortingOrder")==0);
            Assert(s.Root.Children.IndexOf(s.Window)==1);
            Assert(s.Use.Children.IndexOf(a)==9 && s.Use.Children.IndexOf(b)==10);
            Assert(s.Use.Children.TrueForAll(c=>Array.IndexOf(s.NativeChildren,c)==s.Use.Children.IndexOf(c)));

            GameUi.Hand=null; s.Controller.Set("selectedIndex",1); CardTargetButtonsUi.Tick();
            Assert(s.Window.Get<int>("sortingOrder")==0);
            GameUi.Hand=s.Hand; CardTargetButtonsUi.Tick();
            Assert(s.Window.Get<int>("sortingOrder")==1101);
            s.Window.Set("sortingOrder",0);
            int resetIndex=s.Root.Children.IndexOf(s.Window);
            CardTargetButtonsUi.Tick(); Assert(s.Window.Get<int>("sortingOrder")==1101);
            s.Controller.Set("selectedIndex",0); CardTargetButtonsUi.Tick();
            Assert(s.Window.Get<int>("sortingOrder")==0 && s.Root.Children.IndexOf(s.Window)==resetIndex);

            foreach(int type in new[]{0,2,5}){
                s.Controller.Set("selectedIndex",type); CardTargetButtonsUi.Tick();
                Assert(a.Get<int>("sortingOrder")==0 && b.Get<int>("sortingOrder")==0 && s.Window.Get<int>("sortingOrder")==0);
            }
            foreach(int type in new[]{3,4}){
                s.Controller.Set("selectedIndex",type); CardTargetButtonsUi.Tick();
                Assert(a.Get<int>("sortingOrder")==1 && b.Get<int>("sortingOrder")==1 && s.Window.Get<int>("sortingOrder")==1101);
                Assert(a.Get<float>("y")==height*.7f && b.Get<float>("y")==height*.7f);
            }
            s.Controller.Set("selectedIndex",1); CardTargetButtonsUi.Tick();
            a.Set("sortingOrder",500); GameUi.Window=null; CardTargetButtonsUi.Tick();
            Assert(a.Get<int>("sortingOrder")==500 && b.Get<int>("sortingOrder")==0 && s.Window.Get<int>("sortingOrder")==0);
            foreach(var button in new[]{a,b}){
                Assert(button.Get<float>("x")==42f && button.Get<float>("y")==height*.7f);
                Assert(button.Get<float>("width")==298f && button.Get<float>("height")==101f);
                Assert(button.Get<float>("scaleX")==1f && button.Get<float>("scaleY")==1f);
            }

            s=new Scene(height); CardTargetButtonsUi.Tick();
            var other=new RuntimeObject(); other.AddChild(s.Cancel);
            s.Window.Data["isDisposed"]=true; s.Sure.Data["isDisposed"]=true;
            GameUi.Window=null; CardTargetButtonsUi.Tick();
            Assert(s.Cancel.Get<int>("sortingOrder")==1 && s.Cancel.Get("parent")==other);
            Assert((int)s.Window.Data["sortingOrder"]==8 && (int)s.Sure.Data["sortingOrder"]==1);

            s=new Scene(height); CardTargetButtonsUi.Tick(); s.Use.Data["isDisposed"]=true;
            GameUi.Window=null; CardTargetButtonsUi.Tick();
            Assert(s.Cancel.Get<int>("sortingOrder")==1 && s.Sure.Get<int>("sortingOrder")==1);
            Assert(s.Window.Get<int>("sortingOrder")==0);

            s=new Scene(height); CardTargetButtonsUi.Tick(); s.Card.Set("sortingOrder",100);
            CardTargetButtonsUi.Tick(); Assert(s.Cancel.Get<int>("sortingOrder")==101 && s.Sure.Get<int>("sortingOrder")==101);
            s.Cancel.Set("sortingOrder",0);
            CardTargetButtonsUi.Tick(); Assert(s.Cancel.Get<int>("sortingOrder")==101);
            CardTargetButtonsUi.Clear();
            Assert(s.Cancel.Get<int>("sortingOrder")==0 && s.Sure.Get<int>("sortingOrder")==0);

            s=new Scene(height); CardTargetButtonsUi.Tick();
            Assert(s.Cancel.Get<int>("sortingOrder")==1 && s.Sure.Get<int>("sortingOrder")==1 && s.Window.Get<int>("sortingOrder")==8);
            s.Use.Set("visible",false); CardTargetButtonsUi.Tick();
            Assert(s.Cancel.Get<int>("sortingOrder")==0 && s.Sure.Get<int>("sortingOrder")==0 && s.Window.Get<int>("sortingOrder")==0);
            Assert(s.Use.Children.IndexOf(s.Cancel)==9 && s.Use.Children.IndexOf(s.Sure)==10 && s.Root.Children.IndexOf(s.Window)==1);
            s.Use.Set("visible",true); CardTargetButtonsUi.Tick();
            Assert(s.Cancel.Get<int>("sortingOrder")==1 && s.Sure.Get<int>("sortingOrder")==1 && s.Window.Get<int>("sortingOrder")==8);
            Assert(s.Cancel.Get<float>("y")==height*.7f && s.Sure.Get<float>("y")==height*.7f);
            CardTargetButtonsUi.Clear();
        }
    }
}
}
