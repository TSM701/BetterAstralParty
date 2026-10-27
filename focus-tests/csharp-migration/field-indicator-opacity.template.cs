#nullable enable
using System;
using System.Collections.Generic;
namespace BetterAstralParty {
internal sealed class RuntimeObject {
    static long _next;
    internal readonly IntPtr Pointer = new(++_next);
    internal readonly string Kind;
    internal readonly Dictionary<string,RuntimeObject> References = new();
    internal float Alpha=1f;
    internal bool Disposed, Visible=true, GroupVisible=true, FailDisposedOnce, FailRestoreOnce, FailSetAfterWriteOnce,
        FailSetBeforeWriteOnce, FailAlphaReadOnce;
    internal int Writes;
    internal RuntimeObject(string kind) {
        Kind=kind;
        if(kind is "MonsterParent" or "PlayerParent") References["displayObject"]=new("Display");
    }
    internal RuntimeObject Display => References["displayObject"];
    internal void NativeVisible(bool visible) { if(Visible!=visible){Visible=visible;Display.Visible=Visible&&GroupVisible;} }
    internal void NativeGroup(bool visible) { GroupVisible=visible;Display.Visible=Visible&&GroupVisible; }
    internal void NativeAlpha(float alpha) { Alpha=alpha;Display.Alpha=alpha; }
    internal RuntimeObject? Get(string name) => (Kind,name) switch {
        ("Logic","battle") or ("BattlePanel","ui") or ("Field","parent") or ("Root","parent") or
        ("MonsterParent","displayObject") or ("PlayerParent","displayObject") => References.GetValueOrDefault(name),
        _ => throw new InvalidOperationException("Unexpected object getter: "+Kind+"."+name) };
    internal RuntimeObject? Field(string name) => (Kind,name) switch {
        ("Battle","battleInfo") or ("Field","com_AttrInfos") or ("Field","com_PlayerAttrInfos") => References.GetValueOrDefault(name),
        _ => throw new InvalidOperationException("Unexpected field: "+Kind+"."+name) };
    internal T Get<T>(string name) where T:unmanaged {
        if(Kind is not ("MonsterParent" or "PlayerParent" or "Display"))throw new InvalidOperationException("Unexpected typed owner");
        if(name=="isDisposed" && typeof(T)==typeof(bool)) {
            if(FailDisposedOnce){FailDisposedOnce=false;throw new InvalidOperationException("Injected disposed read");}
            return (T)(object)Disposed;
        }
        if(Disposed)throw new InvalidOperationException("Getter on disposed object");
        if(name=="alpha" && typeof(T)==typeof(float) && Kind!="Display") {
            if(FailAlphaReadOnce){FailAlphaReadOnce=false;throw new InvalidOperationException("Injected alpha read failure");}
            return (T)(object)Alpha;
        }
        if(name=="internalVisible2" && typeof(T)==typeof(bool) && Kind!="Display")return (T)(object)(Visible&&GroupVisible);
        if(name=="visible" && typeof(T)==typeof(bool) && Kind=="Display")return (T)(object)Visible;
        throw new InvalidOperationException("Unexpected typed getter "+Kind+"."+name);
    }
    internal void Set(string name,object value) {
        if(Disposed)throw new InvalidOperationException("Setter on disposed object");
        if(FailSetBeforeWriteOnce){FailSetBeforeWriteOnce=false;throw new InvalidOperationException("Injected pre-write setter failure");}
        if(Kind is "MonsterParent" or "PlayerParent" && name=="alpha" && value is float alpha) {
            if(FailRestoreOnce && alpha>.6f){FailRestoreOnce=false;throw new InvalidOperationException("Injected restoration failure");}
            NativeAlpha(alpha);
        } else if(Kind=="Display" && name=="visible" && value is bool visible) {
            if(FailRestoreOnce && visible){FailRestoreOnce=false;throw new InvalidOperationException("Injected visibility restore failure");}
            Visible=visible;
        } else throw new InvalidOperationException("Only parent alpha or display visibility may be changed");
        Writes++;
        if(FailSetAfterWriteOnce){FailSetAfterWriteOnce=false;throw new InvalidOperationException("Injected post-write failure");}
    }
    internal static RuntimeObject? Logic;
    internal static IntPtr FindClass(string ns,string name) => ns=="GameLogic"&&name=="GameLogicManager"?new(1):throw new InvalidOperationException("Unexpected class");
    internal static RuntimeObject? StaticField(IntPtr type,string name) => type==new IntPtr(1)&&name=="_inst"?Logic:throw new InvalidOperationException("Unexpected singleton");
}
internal static class GameUi {
    internal static RuntimeObject? Root;
    internal static bool Visible(RuntimeObject? field) => field!=null&&field.Kind=="Field"&&!field.Disposed&&field.Visible;
}
internal static class PvpSafety { internal static bool Suspended; }
internal sealed class OpacitySetting { internal bool Value=true; }
internal static class Plugin { internal static readonly OpacitySetting IndicatorFade=new(), IndicatorHide=new(); internal static readonly StrengthSetting IndicatorFadeStrength=new(), IndicatorHideDistance=new(){Value=.6f}; }
internal sealed class StrengthSetting { internal float Value=1f; }
__SOURCE__
public static class OpacityCheck {
    public static int Assertions;
    static void Assert(bool ok) { Assertions++;if(!ok)throw new InvalidOperationException("Opacity assertion "+Assertions); }
    static void Equal(float actual,float expected) => Assert(Math.Abs(actual-expected)<.000001f);
    sealed class Scene {
        internal readonly RuntimeObject Root=new("Root"),Field=new("Field"),Monster=new("MonsterParent"),Player=new("PlayerParent");
        internal Scene() {
            FieldIndicatorOpacity.Clear();PvpSafety.Suspended=false;GameUi.Root=Root;
            Plugin.IndicatorFade.Value=Plugin.IndicatorHide.Value=true;
            Field.References["parent"]=Root;Field.References["com_AttrInfos"]=Monster;Field.References["com_PlayerAttrInfos"]=Player;
            var panel=new RuntimeObject("BattlePanel");panel.References["ui"]=Field;
            var battle=new RuntimeObject("Battle");battle.References["battleInfo"]=panel;
            RuntimeObject.Logic=new("Logic");RuntimeObject.Logic.References["battle"]=battle;
        }
    }
    public static void Run() {
        var s=new Scene();s.Player.NativeAlpha(.5f);FieldIndicatorOpacity.Tick(.5f);Equal(s.Monster.Alpha,.45f);Equal(s.Player.Alpha,.225f);
        for(int i=0;i<100;i++)FieldIndicatorOpacity.Tick(.5f);
        Assert(s.Monster.Writes==1&&s.Player.Writes==1);
        // The mod hero clones share the monster parent; native player overlays use the other parent.
        Equal(.8f*s.Player.Display.Alpha,.18f);Equal(.4f*s.Monster.Display.Alpha,.18f);
        FieldIndicatorOpacity.Tick(0);Equal(s.Monster.Alpha,1);Equal(s.Player.Alpha,.5f);
        int writes=s.Monster.Writes+s.Player.Writes;FieldIndicatorOpacity.Clear();Assert(writes==s.Monster.Writes+s.Player.Writes);
        var weak=new Scene();Plugin.IndicatorFadeStrength.Value=.5f;FieldIndicatorOpacity.Tick(.5f);Equal(weak.Monster.Alpha,.725f);Plugin.IndicatorFadeStrength.Value=0;FieldIndicatorOpacity.Tick(.5f);Equal(weak.Monster.Alpha,1);Plugin.IndicatorFadeStrength.Value=1;FieldIndicatorOpacity.Clear();
        foreach(float alpha in new[]{0f,.1f,.5f,1f,1.5f}) {
            s=new Scene();s.Monster.NativeAlpha(alpha);s.Player.NativeAlpha(alpha);FieldIndicatorOpacity.Tick(.5f);
            Equal(s.Monster.Alpha,alpha*.45f);Equal(s.Player.Alpha,alpha*.45f);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,alpha);Equal(s.Player.Alpha,alpha);
        }
        s=new Scene();float previous=1;
        for(int i=1;i<=59;i++){FieldIndicatorOpacity.Tick(i*.01f);Assert(s.Monster.Alpha<previous&&!FieldIndicatorOpacity.Hidden);previous=s.Monster.Alpha;}
        FieldIndicatorOpacity.Tick(.60f);Assert(FieldIndicatorOpacity.Hidden&&!s.Monster.Display.Visible&&!s.Player.Display.Visible);
        Assert(s.Monster.Visible&&s.Player.Visible); // Native logical visibility is never overwritten.
        for(int i=0;i<100;i++)FieldIndicatorOpacity.Tick(.60f);
        Assert(s.Monster.Display.Writes==1&&s.Player.Display.Writes==1);
        // Render and hit-test both consult DisplayObject.visible, independently of child alpha/touchable.
        Assert(!s.Monster.Display.Visible&&!s.Player.Display.Visible);
        FieldIndicatorOpacity.Tick(.55f);Assert(FieldIndicatorOpacity.Hidden&&!s.Monster.Display.Visible);
        FieldIndicatorOpacity.Tick(.50f);Assert(!FieldIndicatorOpacity.Hidden&&s.Monster.Display.Visible&&s.Player.Display.Visible);
        FieldIndicatorOpacity.Tick(.55f);Assert(!FieldIndicatorOpacity.Hidden&&s.Monster.Display.Visible);
        FieldIndicatorOpacity.Tick(1f);FieldIndicatorOpacity.Tick(2f);Equal(s.Monster.Alpha,.3f);Assert(FieldIndicatorOpacity.Hidden);
        FieldIndicatorOpacity.Clear();Assert(!FieldIndicatorOpacity.Hidden&&s.Monster.Display.Visible&&s.Player.Display.Visible);Equal(s.Monster.Alpha,1);
        // Hide distance 0-100 % (issue #16): 0 % hides at any zoom-out, 100 % only at the stop.
        Plugin.IndicatorHideDistance.Value=0;FieldIndicatorOpacity.Tick(.01f);Assert(FieldIndicatorOpacity.Hidden);FieldIndicatorOpacity.Clear();
        Plugin.IndicatorHideDistance.Value=1;FieldIndicatorOpacity.Tick(.99f);Assert(!FieldIndicatorOpacity.Hidden);FieldIndicatorOpacity.Tick(1f);Assert(FieldIndicatorOpacity.Hidden);FieldIndicatorOpacity.Clear();
        Plugin.IndicatorHideDistance.Value=.6f;
        // Native logical visibility or group changes during our hidden overlay remain authoritative on reveal.
        foreach(bool group in new[]{false,true}) {
            s=new Scene();FieldIndicatorOpacity.Tick(1);if(group)s.Monster.NativeGroup(false);else s.Monster.NativeVisible(false);
            FieldIndicatorOpacity.Tick(.50f);Assert(!s.Monster.Display.Visible&&s.Player.Display.Visible);Assert(s.Monster.Visible==group);
            FieldIndicatorOpacity.Clear();Assert(!s.Monster.Display.Visible);Equal(s.Monster.Alpha,1);
        }
        s=new Scene();s.Monster.NativeVisible(false);FieldIndicatorOpacity.Tick(1);FieldIndicatorOpacity.Clear();Assert(!s.Monster.Visible&&!s.Monster.Display.Visible);
        s=new Scene();s.Monster.Display.Visible=false;FieldIndicatorOpacity.Tick(1);FieldIndicatorOpacity.Tick(.50f);
        Assert(!s.Monster.Display.Visible&&s.Monster.Visible&&s.Monster.Display.Writes==0);
        FieldIndicatorOpacity.Clear();Assert(!s.Monster.Display.Visible&&s.Monster.Display.Writes==0);
        // Reapplying native visibility while hidden must be hidden again, without changing its logical value.
        s=new Scene();FieldIndicatorOpacity.Tick(1);s.Monster.NativeVisible(false);s.Monster.NativeVisible(true);Assert(s.Monster.Display.Visible);
        FieldIndicatorOpacity.Tick(1);Assert(!s.Monster.Display.Visible&&s.Monster.Visible);FieldIndicatorOpacity.Tick(.50f);Assert(s.Monster.Display.Visible);
        // Do not overwrite another owner that has already restored the display.
        s=new Scene();FieldIndicatorOpacity.Tick(1);s.Monster.Display.Visible=true;int displayWrites=s.Monster.Display.Writes;
        FieldIndicatorOpacity.Clear();Assert(s.Monster.Display.Visible&&s.Monster.Display.Writes==displayWrites);
        s=new Scene();FieldIndicatorOpacity.Tick(.5f);s.Monster.NativeAlpha(.8f);FieldIndicatorOpacity.Tick(.5f);Equal(s.Monster.Alpha,.36f);
        FieldIndicatorOpacity.Tick(.5f);Equal(s.Monster.Alpha,.36f);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,.8f);
        s=new Scene();FieldIndicatorOpacity.Tick(.5f);s.Monster.NativeAlpha(.7f);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,.7f);Equal(s.Player.Alpha,1);
        s=new Scene();FieldIndicatorOpacity.Tick(1);var replacement=new RuntimeObject("MonsterParent");replacement.NativeAlpha(.5f);s.Field.References["com_AttrInfos"]=replacement;
        FieldIndicatorOpacity.Tick(1);Equal(s.Monster.Alpha,1);Assert(s.Monster.Display.Visible);Equal(replacement.Alpha,.15f);Assert(!replacement.Display.Visible);
        FieldIndicatorOpacity.Clear();Equal(replacement.Alpha,.5f);Assert(replacement.Display.Visible);
        s=new Scene();FieldIndicatorOpacity.Tick(1);s.Field.References.Remove("com_AttrInfos");FieldIndicatorOpacity.Tick(1);Equal(s.Monster.Alpha,1);Assert(s.Monster.Display.Visible&&!s.Player.Display.Visible);
        foreach(var gate in new Action<Scene>[] {a=>GameUi.Root=null,a=>RuntimeObject.Logic=null,a=>a.Field.Visible=false,a=>a.Field.Disposed=true,
            a=>PvpSafety.Suspended=true,a=>GameUi.Root=new("Root")}) {
            s=new Scene();FieldIndicatorOpacity.Tick(1);gate(s);FieldIndicatorOpacity.Tick(1);Equal(s.Monster.Alpha,1);Equal(s.Player.Alpha,1);
            Assert(!FieldIndicatorOpacity.Hidden&&s.Monster.Display.Visible&&s.Player.Display.Visible);
        }
        s=new Scene();FieldIndicatorOpacity.Tick(1);s.Monster.Disposed=s.Monster.Display.Disposed=true;int before=s.Monster.Writes;FieldIndicatorOpacity.Clear();Assert(s.Monster.Writes==before);Equal(s.Player.Alpha,1);Assert(s.Player.Display.Visible);
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}) {
            s=new Scene();s.Monster.NativeAlpha(invalid);FieldIndicatorOpacity.Tick(.5f);Assert(s.Monster.Writes==0);Equal(s.Player.Alpha,.45f);FieldIndicatorOpacity.Clear();Equal(s.Player.Alpha,1);
            s=new Scene();FieldIndicatorOpacity.Tick(1);FieldIndicatorOpacity.Tick(invalid);Assert(!FieldIndicatorOpacity.Hidden&&s.Monster.Display.Visible);Equal(s.Monster.Alpha,1);
        }
        s=new Scene();FieldIndicatorOpacity.Tick(.5f);s.Monster.NativeAlpha(float.NaN);FieldIndicatorOpacity.Tick(.5f);Assert(float.IsNaN(s.Monster.Alpha));Equal(s.Player.Alpha,.45f);
        s=new Scene();FieldIndicatorOpacity.Tick(1);s.Monster.FailRestoreOnce=true;
        bool threw=false;try{FieldIndicatorOpacity.Clear();}catch(InvalidOperationException){threw=true;}
        Assert(threw);Equal(s.Player.Alpha,1);Equal(s.Monster.Alpha,.3f);Assert(s.Monster.Display.Visible&&s.Player.Display.Visible);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,1);
        s=new Scene();FieldIndicatorOpacity.Tick(1);s.Monster.FailDisposedOnce=true;
        threw=false;try{FieldIndicatorOpacity.Clear();}catch(InvalidOperationException){threw=true;}
        Assert(threw);Equal(s.Player.Alpha,1);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,1);Assert(s.Monster.Display.Visible);
        s=new Scene();s.Monster.FailSetAfterWriteOnce=true;
        threw=false;try{FieldIndicatorOpacity.Tick(.5f);}catch(InvalidOperationException){threw=true;}
        Assert(threw);Equal(s.Monster.Alpha,.45f);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,1);Equal(s.Player.Alpha,1);
        s=new Scene();s.Monster.Display.FailSetAfterWriteOnce=true;
        threw=false;try{FieldIndicatorOpacity.Tick(1);}catch(InvalidOperationException){threw=true;}
        Assert(threw&&!s.Monster.Display.Visible);FieldIndicatorOpacity.Clear();Assert(s.Monster.Display.Visible);Equal(s.Monster.Alpha,1);
        s=new Scene();FieldIndicatorOpacity.Tick(1);s.Monster.Display.FailRestoreOnce=true;
        threw=false;try{FieldIndicatorOpacity.Clear();}catch(InvalidOperationException){threw=true;}
        Assert(threw&&!s.Monster.Display.Visible&&s.Player.Display.Visible);FieldIndicatorOpacity.Clear();Assert(s.Monster.Display.Visible);
        // A later fade change must not forget an earlier owned value when native Set fails before writing.
        foreach(bool afterWrite in new[]{false,true}) {
            s=new Scene();FieldIndicatorOpacity.Tick(.5f);
            s.Monster.FailSetBeforeWriteOnce=!afterWrite;s.Monster.FailSetAfterWriteOnce=afterWrite;
            threw=false;try{FieldIndicatorOpacity.Tick(.7f);}catch(InvalidOperationException){threw=true;}
            Assert(threw);Equal(s.Monster.Alpha,afterWrite?.39f:.45f);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,1);
        }
        // Getter or restore errors retain either successful or uncertain written ownership for the next cleanup.
        foreach(bool afterWrite in new[]{false,true}) {
            s=new Scene();FieldIndicatorOpacity.Tick(.5f);
            s.Monster.FailSetBeforeWriteOnce=!afterWrite;s.Monster.FailSetAfterWriteOnce=afterWrite;
            try{FieldIndicatorOpacity.Tick(.7f);}catch(InvalidOperationException){}
            s.Monster.FailAlphaReadOnce=true;threw=false;
            try{FieldIndicatorOpacity.Clear();}catch(InvalidOperationException){threw=true;}
            Assert(threw);Equal(s.Player.Alpha,1);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,1);
            s=new Scene();FieldIndicatorOpacity.Tick(.5f);
            s.Monster.FailSetBeforeWriteOnce=!afterWrite;s.Monster.FailSetAfterWriteOnce=afterWrite;
            try{FieldIndicatorOpacity.Tick(.7f);}catch(InvalidOperationException){}
            s.Monster.FailRestoreOnce=true;threw=false;
            try{FieldIndicatorOpacity.Clear();}catch(InvalidOperationException){threw=true;}
            Assert(threw);Equal(s.Player.Alpha,1);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,1);
            s=new Scene();FieldIndicatorOpacity.Tick(.5f);
            s.Monster.FailSetBeforeWriteOnce=!afterWrite;s.Monster.FailSetAfterWriteOnce=afterWrite;
            try{FieldIndicatorOpacity.Tick(.7f);}catch(InvalidOperationException){}
            s.Monster.NativeAlpha(.8f);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,.8f);
        }
        s=new Scene();FieldIndicatorOpacity.Tick(.5f);s.Monster.FailAlphaReadOnce=true;threw=false;
        try{FieldIndicatorOpacity.Tick(.7f);}catch(InvalidOperationException){threw=true;}
        Assert(threw);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,1);
        s=new Scene();s.Monster.FailSetBeforeWriteOnce=true;
        try{FieldIndicatorOpacity.Tick(.5f);}catch(InvalidOperationException){}
        s.Monster.NativeAlpha(0);FieldIndicatorOpacity.Clear();Equal(s.Monster.Alpha,0);
        s=new Scene();FieldIndicatorOpacity.Tick(0);Assert(s.Monster.Writes==0&&s.Player.Writes==0);
        // Fade and distant hiding are independent and restore only their own current presentation.
        foreach(bool fade in new[]{false,true})foreach(bool hide in new[]{false,true}) {
            s=new Scene();s.Player.NativeAlpha(.5f);Plugin.IndicatorFade.Value=fade;Plugin.IndicatorHide.Value=hide;
            FieldIndicatorOpacity.Tick(1);Equal(s.Monster.Alpha,fade?.3f:1f);Equal(s.Player.Alpha,fade?.15f:.5f);
            Assert(FieldIndicatorOpacity.Hidden==hide&&s.Monster.Display.Visible==!hide&&s.Player.Display.Visible==!hide);
            Plugin.IndicatorFade.Value=Plugin.IndicatorHide.Value=false;FieldIndicatorOpacity.Tick(1);
            Equal(s.Monster.Alpha,1);Equal(s.Player.Alpha,.5f);Assert(!FieldIndicatorOpacity.Hidden&&s.Monster.Display.Visible&&s.Player.Display.Visible);
        }
        s=new Scene();FieldIndicatorOpacity.Tick(1);Plugin.IndicatorHide.Value=false;FieldIndicatorOpacity.Tick(1);
        Equal(s.Monster.Alpha,.3f);Assert(!FieldIndicatorOpacity.Hidden&&s.Monster.Display.Visible);
        Plugin.IndicatorFade.Value=false;Plugin.IndicatorHide.Value=true;FieldIndicatorOpacity.Tick(1);
        Equal(s.Monster.Alpha,1);Assert(FieldIndicatorOpacity.Hidden&&!s.Monster.Display.Visible);
        Plugin.IndicatorHide.Value=false;s.Monster.NativeAlpha(.8f);s.Monster.NativeGroup(false);FieldIndicatorOpacity.Tick(1);
        Equal(s.Monster.Alpha,.8f);Assert(!s.Monster.Display.Visible&&s.Player.Display.Visible);
        Plugin.IndicatorFade.Value=Plugin.IndicatorHide.Value=true;FieldIndicatorOpacity.Tick(.55f);
        Assert(!FieldIndicatorOpacity.Hidden);FieldIndicatorOpacity.Tick(.6f);Assert(FieldIndicatorOpacity.Hidden);
        FieldIndicatorOpacity.Clear();
    }
}
}
