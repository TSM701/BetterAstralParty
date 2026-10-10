$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../src/HandLayoutUi.cs') -Raw
$prefix = [regex]::Match($source, '(?s)    private static void Update\(\).*?(?=        // ReadyFight)').Value
$clearState = [regex]::Match($source, '(?s)        _ui = _container = _hotZone = null;.*?(?=\r?\n    \})').Value
if (!$prefix -or !$clearState) { throw 'Production hand scan/cleanup missing' }
$harness = @'
#nullable enable
using System;
namespace HandScanTest {
struct Vector2 { public Vector2(float x,float y){} }
static class Time { public static float unscaledTime; }
sealed class Setting<T> { public T Value; public Setting(T value){Value=value;} }
static class Plugin { public static Setting<bool> GroupHand=new(true); public static Setting<string> HandExpandMode=new("Hover"); }
static class HandLayout { public static int Mode(bool enabled,string mode)=>!enabled?0:mode=="Click"?2:1; }
static class HandClickBinding { public static void Clear(){} }
static class VisibleCombatAdvisor { public static bool IsPve(int map)=>map==9; }
sealed class RuntimeObject {
    static int next;
    public IntPtr Pointer=(IntPtr)(++next);
    public bool Visible=true;
    public static int Scans, Map=9;
    public static bool Watcher;
    public static IntPtr FindClass(string ns,string name)=>(IntPtr)1;
    public static RuntimeObject Shared=new();
    public static RuntimeObject StaticField(IntPtr type,string name){Scans++;return Shared;}
    public RuntimeObject Get(string name)=>this;
    public T Get<T>(string name)=>(T)(name=="MapType"?(object)Map:0f);
    public RuntimeObject Field(string name)=>this;
    public RuntimeObject Call(string name)=>this;
    public T Value<T>()=>(T)(object)Watcher;
}
static class GameUi {
    public static RuntimeObject? Root=new(), Panel;
    public static bool Fight;
    public static RuntimeObject? Find(RuntimeObject root,string name)=>name=="FightWindow"?(Fight?RuntimeObject.Shared:null):Panel;
    public static bool Visible(RuntimeObject obj)=>obj.Visible;
}
public static class Check {
    static RuntimeObject? _ui,_container,_hotZone;
    static Vector2 _hotPosition,_hotSize;
    static float _scanAt;
    static IntPtr _tween,_tweenManager,_gobject,_logic,_pool,_focused;
    static int _mode,Layouts,Clears;
    static int? _expanded;
    static bool _cardPress;
    static void Report(string state){}
    static void Clear(){ Clears++;
__CLEAR__
    }
__PREFIX__
        Layouts++; // Actual per-frame geometry begins here; not part of the search throttle.
    }
    static void Assert(bool ok){if(!ok)throw new Exception("Hand search lifecycle regression");}
    public static void Run(){
        foreach(var reason in new[]{"missing","combat","spectator","pvp"}){
            Clear(); Layouts=Clears=RuntimeObject.Scans=0;
            GameUi.Panel=reason=="missing"?null:new(); GameUi.Fight=reason=="combat";
            RuntimeObject.Watcher=reason=="spectator"; RuntimeObject.Map=reason=="pvp"?2:9;
            for(int frame=0;frame<3000;frame++){Time.unscaledTime=frame/300f;Update();}
            Assert(Layouts==0 && RuntimeObject.Scans>=49 && RuntimeObject.Scans<=50);
            Console.WriteLine($"Hand search {reason}: {RuntimeObject.Scans} scans / 3000 frames (production prefix, substitute).");
        }
        Clear(); GameUi.Fight=false; RuntimeObject.Watcher=false; RuntimeObject.Map=9; GameUi.Panel=null;
        Time.unscaledTime=20;Update(); GameUi.Panel=new();
        Time.unscaledTime=20.1f;Update(); Assert(_ui==null);
        Time.unscaledTime=20.21f;Update(); Assert(_ui==GameUi.Panel);
        int before=RuntimeObject.Scans,layouts=Layouts;
        for(int i=1;i<=30;i++){Time.unscaledTime=20.21f+i/300f;Update();}
        Assert(RuntimeObject.Scans==before && Layouts==layouts+30); // Visible hand stays frame-rate driven.
        GameUi.Panel.Visible=false; Update(); Assert(_ui==null); // Immediate hidden cleanup.
        GameUi.Panel=null;Update(); before=RuntimeObject.Scans;Update();Assert(RuntimeObject.Scans==before);
        GameUi.Panel=new();Time.unscaledTime+=.21f;Update();Assert(_ui==GameUi.Panel);
        var old=GameUi.Panel; GameUi.Panel=new(); Time.unscaledTime+=.21f;Update();
        Assert(_ui==GameUi.Panel && _ui!=old); before=RuntimeObject.Scans;Update();Assert(RuntimeObject.Scans==before);
        Plugin.GroupHand.Value=false;Update();Assert(_ui==null);
        Plugin.GroupHand.Value=true;Update();Assert(_ui==GameUi.Panel); // Setting switch reconnects immediately.
        Clear(); Assert(_scanAt==0 && _ui==null && _focused==IntPtr.Zero && _expanded==null && !_cardPress);
        Update(); Assert(_ui==GameUi.Panel); // PvP/scene cleanup recovery.
        GameUi.Root=null;Update();Assert(_ui==null);
        GameUi.Root=new();Update();Assert(_ui==GameUi.Panel);
        Console.WriteLine("PASS: late panel, hidden/replaced panel, every-frame layout, settings, explicit cleanup and root recovery.");
    }
}
}
'@
Add-Type -TypeDefinition $harness.Replace('__PREFIX__',$prefix).Replace('__CLEAR__',$clearState)
[HandScanTest.Check]::Run()
