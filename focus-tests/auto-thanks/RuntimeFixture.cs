using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEngine
{
    public static class Application { public static bool isFocused = true; }
    public static class Input
    {
        public static int touchCount;
        public static readonly bool[] Held = new bool[3], Down = new bool[3], Up = new bool[3];
        public static bool GetMouseButton(int i) => Held[i];
        public static bool GetMouseButtonDown(int i) => Down[i];
        public static bool GetMouseButtonUp(int i) => Up[i];
        public static void Reset() { touchCount = 0; Array.Clear(Held, 0, 3); Array.Clear(Down, 0, 3); Array.Clear(Up, 0, 3); }
    }
}

namespace AutoThanksTest
{
    // Only the production read/call ABI is available. No setters, event mutation or sender exists.
    internal sealed class RuntimeObject
    {
        private static int _next;
        internal IntPtr Pointer;
        internal readonly string Kind;
        internal readonly Dictionary<string, RuntimeObject> References = new();
        internal readonly Dictionary<string, object> Values = new();
        internal RuntimeObject Parent;
        internal bool Visible = true, Disposed, OnStage = true;
        internal object Boxed;
        internal Action Callback;
        internal int Attempts, CallFailure;
        internal bool CallResult, FailCancellationRead;
        internal RuntimeObject(string kind) { Kind = kind; Pointer = new IntPtr(++_next); }
        internal static RuntimeObject Box(object value) => new("Box") { Boxed = value };
        internal RuntimeObject Get(string name)
        {
            bool allowed = Kind switch
            {
                "Logic" => name is "room" or "watch" or "replay" or "battle" or "account",
                "RoomLogic" => name == "curRoomInfo",
                "Replay" => name == "Session",
                "Button" => name == "onClick",
                "Delegate" => name == "Target",
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Unexpected object getter " + Kind + "." + name);
            return References.GetValueOrDefault(name);
        }
        internal T Get<T>(string name) where T : unmanaged
        {
            bool allowed = Kind switch
            {
                "Root" => name is "hasModalWindow" or "modalWaiting" or "hasAnyPopup",
                "Button" => name is "touchable" or "grayed",
                "Cts" => name == "IsCancellationRequested",
                "ReplaySession" => name == "CanSendC2S",
                "Player" => name == "Id",
                "Window" => name == "isShowing",
                "Listener" => name is "isEmpty" or "isDispatching",
                "DelegateArray" => name == "Length",
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Unexpected typed getter " + Kind + "." + name);
            if (Kind == "Cts" && FailCancellationRead) throw new InvalidOperationException("Injected disposed cancellation getter");
            if (Kind == "Listener" && name == "isEmpty" &&
                (References.GetValueOrDefault("_bridge") is not RuntimeObject bridge || bridge.References.GetValueOrDefault("owner") == null))
                throw new InvalidOperationException("Native isEmpty requires bridge owner");
            return Typed<T>(Values.GetValueOrDefault(name), name);
        }
        internal RuntimeObject Field(string name)
        {
            bool allowed = Kind switch
            {
                "Window" => name is "_btn_QuickReply" or "quickReplyCts" or "_showQuickReply",
                "Cts" => name == "cts",
                "RoomLogic" => name == "roomController",
                "RoomController" => name == "roomStateType",
                "BattlePlayer" => name == "player",
                "Listener" => name == "_bridge",
                "Bridge" => name is "owner" or "_callback0" or "_callback1" or "_captureCallback",
                "Closure" => name == "<>4__this",
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Unexpected native object field " + Kind + "." + name);
            return References.GetValueOrDefault(name);
        }
        internal T Field<T>(string name) where T : unmanaged
        {
            bool allowed = Kind switch
            {
                "Cts" => name is "_dispose" or "id",
                "Battle" => name == "clientFinishReady",
                "Bridge" => name == "_isLocking",
                "Closure" => name == "chatId",
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Unexpected native typed field " + Kind + "." + name);
            return Typed<T>(Values.GetValueOrDefault(name), name);
        }
        internal T Value<T>() where T : unmanaged => Kind == "Box" ? Typed<T>(Boxed, "boxed") : throw new InvalidOperationException("Non-box Value");
        private static T Typed<T>(object value, string name) where T : unmanaged => value is T typed ? typed : throw new InvalidCastException("Strict ABI type " + name + " expected " + typeof(T).Name);
        internal RuntimeObject Call(string name, params object[] arguments)
        {
            if (arguments.Length != 0) throw new InvalidOperationException("No native argument marshaling permitted: " + Kind + "." + name);
            if (Kind == "Listener" && name == "Call")
            {
                Attempts++;
                if (CallFailure == 1) throw new InvalidOperationException("Injected uncertain pre-callback failure");
                Callback?.Invoke();
                if (CallFailure == 2) throw new InvalidOperationException("Injected uncertain post-callback failure");
                return Box(CallResult);
            }
            bool allowed = Kind switch
            {
                "Room" => name is "IsPVE" or "GetSelfInfo",
                "Watch" => name == "PlayerIsWatcher",
                "Battle" => name == "GetSelfPlayerData",
                "Account" => name == "GetPlayerID",
                "Delegate" => name == "GetInvocationList",
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Unexpected native method " + Kind + "." + name);
            return References.GetValueOrDefault(name);
        }
        internal static IntPtr FindClass(string ns, string name) => (ns, name) switch
        {
            ("FairyGUI", "GObject") => new IntPtr(1),
            ("GameLogic", "GameLogicManager") => new IntPtr(2),
            _ => throw new InvalidOperationException("Unexpected native class " + ns + "." + name)
        };
        internal static RuntimeObject StaticField(IntPtr type, string name) => type == new IntPtr(2) && name == "_inst" ? World.Logic : throw new InvalidOperationException("Unexpected static field");
        internal static RuntimeObject StaticCall(IntPtr type, string name, params object[] arguments) => type == new IntPtr(1) && name == "get_draggingObject" && arguments.Length == 0 ? World.Dragging : throw new InvalidOperationException("Unexpected static call");
    }

    internal static class GameUi
    {
        internal static RuntimeObject LiveDrag() => World.Dragging;
        internal static RuntimeObject Root;
        internal static bool HomeAvailable;
        internal static RuntimeObject Find(RuntimeObject root, string name, int maxDepth)
        {
            if (root != Root || name != "ExpressionWindow" || maxDepth != 1) throw new InvalidOperationException("ExpressionWindow lookup contract");
            return World.Current.WindowPresent ? World.Current.Window : null;
        }
        internal static bool Visible(RuntimeObject node)
        {
            if (node == null) return false;
            for (var current = node; current != null; current = current.Parent)
                if (!current.Visible || current.Disposed || !current.OnStage) return false;
            return true;
        }
    }
    internal static class PvpSafety { internal static bool Suspended; }
    internal static class ModUi { internal static bool IsOpen; }
    internal static class Compatibility
    {
        internal static bool Blocked, OtherBlocked;
        internal static int Blocks;
        internal static Exception Error;
        internal static bool Allowed(string feature) { Exact(feature); return !Blocked; }
        internal static void Block(string feature, Exception error) { Exact(feature); Blocked = true; Blocks++; Error = error; }
        private static void Exact(string feature) { if (feature != "AutoThanks") throw new InvalidOperationException("Independent feature breaker"); }
    }
    internal static class Plugin
    {
        internal static class AutoThanks { internal static bool Value = true; }
        internal static class Diagnostics
        {
            internal static int States;
            internal static bool Fail;
            internal static void State(string key, string value)
            {
                if (key != "autoThanks" || value != "native-recommendation-confirmed") throw new InvalidOperationException("Unexpected diagnostic");
                States++;
                if (Fail) throw new InvalidOperationException("Injected post-dispatch diagnostic failure");
            }
        }
    }

    __PRODUCTION__

    internal sealed class World
    {
        internal static World Current;
        internal static RuntimeObject Logic, Dragging;
        internal readonly RuntimeObject Root = new("Root"), Window = new("Window"), Button = new("Button"), Cts = new("Cts");
        internal readonly RuntimeObject RoomLogic = new("RoomLogic"), Controller = new("RoomController"), Room = new("Room"), Watch = new("Watch");
        internal readonly RuntimeObject Replay = new("Replay"), Session = new("ReplaySession"), Battle = new("Battle"), BattlePlayer = new("BattlePlayer"), Player = new("Player"), Account = new("Account");
        internal readonly RuntimeObject Listener = new("Listener"), Bridge = new("Bridge"), Delegate = new("Delegate"), Closure = new("Closure");
        internal bool WindowPresent = true;
        internal int NativeHandlerCalls;
        internal World()
        {
            Current = this; AutoThanksUi.Clear(); Logic = new("Logic"); Dragging = null;
            GameUi.Root = Root; GameUi.HomeAvailable = false; PvpSafety.Suspended = false; ModUi.IsOpen = false;
            Application.isFocused = true; Input.Reset(); Compatibility.Blocked = false; Compatibility.OtherBlocked = true; Compatibility.Blocks = 0; Compatibility.Error = null;
            Plugin.Diagnostics.States = 0; Plugin.Diagnostics.Fail = false;
            Plugin.AutoThanks.Value = true;
            Window.Parent = Root; Button.Parent = Window;
            Ref(Window, "_btn_QuickReply", Button); Ref(Window, "quickReplyCts", Cts); Ref(Window, "_showQuickReply", new("Transition"));
            Val(Window, "isShowing", true); Val(Button, "touchable", true); Val(Button, "grayed", false); Ref(Button, "onClick", Listener);
            Val(Cts, "_dispose", false); Val(Cts, "id", 41); Val(Cts, "IsCancellationRequested", false); Ref(Cts, "cts", new("CancellationTokenSource"));
            Val(Root, "hasModalWindow", false); Val(Root, "modalWaiting", false); Val(Root, "hasAnyPopup", false);
            Ref(Logic, "room", RoomLogic); Ref(Logic, "watch", Watch); Ref(Logic, "replay", Replay); Ref(Logic, "battle", Battle); Ref(Logic, "account", Account);
            Ref(RoomLogic, "curRoomInfo", Room); Ref(RoomLogic, "roomController", Controller); Ref(Controller, "roomStateType", RuntimeObject.Box(4));
            Ref(Room, "IsPVE", RuntimeObject.Box(true)); Ref(Room, "GetSelfInfo", new("Player")); Ref(Watch, "PlayerIsWatcher", RuntimeObject.Box(false));
            Ref(Replay, "Session", Session); Val(Session, "CanSendC2S", true); Val(Battle, "clientFinishReady", true);
            Ref(Battle, "GetSelfPlayerData", BattlePlayer); Ref(BattlePlayer, "player", Player); Val(Player, "Id", 1234567890123L); Ref(Account, "GetPlayerID", RuntimeObject.Box(1234567890123L));
            Ref(Listener, "_bridge", Bridge); Val(Listener, "isEmpty", false); Val(Listener, "isDispatching", false);
            Ref(Bridge, "owner", Button); Val(Bridge, "_isLocking", false); Ref(Bridge, "_callback0", Delegate); Ref(Delegate, "Target", Closure); Ref(Closure, "<>4__this", Window); Val(Closure, "chatId", 50000);
            var invocationList = new RuntimeObject("DelegateArray"); Val(invocationList, "Length", 1); Ref(Delegate, "GetInvocationList", invocationList);
            // A substitute for the existing handler, not a native/network call. Keep CTS active by default to expose latch failures.
            Listener.Callback = () => NativeHandlerCalls++;
        }
        internal static void Ref(RuntimeObject node, string name, RuntimeObject value) => node.References[name] = value;
        internal static void Val(RuntimeObject node, string name, object value) => node.Values[name] = value;
    }

    public static class Check
    {
        public static int Assertions { get; private set; }
        public static int Cases { get; private set; }
        public static string[] CaseNames => Names.ToArray();
        private static readonly List<string> Names = new();
        private static void Assert(bool condition, string message) { Assertions++; if (!condition) throw new Exception(message + (Compatibility.Error == null ? "" : " | " + Compatibility.Error.Message)); }
        private static void Case(string name) { Cases++; Names.Add(name); }
        private static void Frames(int count = 64) { for (int i = 0; i < count; i++) AutoThanksUi.Tick(); }
        private static void Gate(string name, Action<World, bool> set, bool clears = false)
        {
            Case(name); var w = new World(); set(w, true); AutoThanksUi.Tick();
            Assert(w.Listener.Attempts == 0 && Compatibility.Blocks == 0, name + ": must delay without dispatch/fault");
            set(w, false); AutoThanksUi.Tick(); Assert(w.Listener.Attempts == 1, name + ": pending recommendation must dispatch after eligibility returns");
            set(w, true); AutoThanksUi.Tick(); set(w, false); Frames();
            Assert(w.Listener.Attempts == (clears ? 2 : 1), name + ": consumed episode lifetime");
            Assert(Compatibility.Blocks == 0 && Compatibility.OtherBlocked, name + ": independent breaker/read ABI");
        }
        private static void RefGate(string name, Func<World, RuntimeObject> node, string member)
        {
            RuntimeObject saved = null;
            Gate(name, (w, blocked) => { var n = node(w); if (blocked) { saved = n.References.GetValueOrDefault(member); World.Ref(n, member, null); } else World.Ref(n, member, saved); });
        }
        private static void BoolGate(string name, Func<World, RuntimeObject> node, string member, bool blockedValue) => Gate(name, (w, blocked) => World.Val(node(w), member, blocked ? blockedValue : !blockedValue));
        public static void Run()
        {
            Assertions = Cases = 0; Names.Clear();
            Case("one native episode across 256 eligible frames"); var w = new World(); Frames(256);
            Assert(w.Listener.Attempts == 1 && w.NativeHandlerCalls == 1 && Plugin.Diagnostics.States == 1, "Exactly one normal callback for live native episode");
            Assert(!Compatibility.Blocked && Compatibility.OtherBlocked, "No unrelated breaker mutation");
            Gate("user setting OFF to ON retains pending and consumed episodes", (_, off) => Plugin.AutoThanks.Value = !off);
            Case("disabled setting performs no native reads and cannot reset a consumed episode"); w = new World(); AutoThanksUi.Tick(); Plugin.AutoThanks.Value = false; w.Cts.FailCancellationRead = true; GameUi.Root = null; Frames();
            Assert(w.Listener.Attempts == 1 && Compatibility.Blocks == 0, "OFF must return before native reads, diagnostics and scene cleanup");
            GameUi.Root = w.Root; w.Cts.FailCancellationRead = false; Application.isFocused = false; Plugin.AutoThanks.Value = true; Frames();
            Assert(w.Listener.Attempts == 1 && w.NativeHandlerCalls == 1, "Re-enabling the same native episode, including in background, cannot resend");
            Case("new native episode during OFF remains eligible once after ON"); w = new World(); AutoThanksUi.Tick(); Plugin.AutoThanks.Value = false; World.Val(w.Cts, "id", 42); Frames();
            Assert(w.Listener.Attempts == 1, "A new native recommendation is not dispatched while OFF");
            Plugin.AutoThanks.Value = true; Frames(); Assert(w.Listener.Attempts == 2 && w.NativeHandlerCalls == 2 && Compatibility.Blocks == 0, "ON accepts the new recommendation once without retrying the consumed one");
            Case("latch precedes reentrant dispatch"); w = new World(); w.Listener.Callback = () => { w.NativeHandlerCalls++; if (w.NativeHandlerCalls == 1) AutoThanksUi.Tick(); }; Frames();
            Assert(w.Listener.Attempts == 1 && w.NativeHandlerCalls == 1, "Reentrant Tick must observe consumed episode");
            Case("Call false is not retry acknowledgement"); w = new World(); w.Listener.CallResult = false; Frames(); Assert(w.Listener.Attempts == 1, "False dispatch result must never retry");
            Case("Call true is not extra acknowledgement"); w = new World(); w.Listener.CallResult = true; Frames(); Assert(w.Listener.Attempts == 1, "True dispatch result remains once-only");
            Case("new CTS while button stays visible"); w = new World(); AutoThanksUi.Tick(); var next = new RuntimeObject("Cts"); next.Values["id"] = 42; next.Values["_dispose"] = false; next.Values["IsCancellationRequested"] = false; next.References["cts"] = new("CancellationTokenSource"); World.Ref(w.Window, "quickReplyCts", next); Frames();
            Assert(w.Listener.Attempts == 2 && w.Button.Visible, "New CTS must be new episode without visibility edge");
            Case("reused CTS pointer with new native id"); w = new World(); AutoThanksUi.Tick(); World.Val(w.Cts, "id", 42); Frames(); Assert(w.Listener.Attempts == 2, "Native id must distinguish pointer reuse");
            Case("redrawn button same native episode"); w = new World(); AutoThanksUi.Tick(); var replacement = new RuntimeObject("Button") { Parent = w.Window }; replacement.Values["touchable"] = true; replacement.Values["grayed"] = false; replacement.References["onClick"] = w.Listener; World.Ref(w.Window, "_btn_QuickReply", replacement); Frames(); Assert(w.Listener.Attempts == 1, "Button redraw must not reset episode");
            Case("new window identity"); w = new World(); AutoThanksUi.Tick(); w.Window.Pointer += 10000; Frames(); Assert(w.Listener.Attempts == 2, "Window pointer belongs to episode identity");
            Case("native handler manual cancellation"); w = new World(); World.Val(w.Cts, "IsCancellationRequested", true); Frames(); Assert(w.Listener.Attempts == 0, "Already manually accepted/cancelled CTS must not dispatch");
            Case("native handler cancellation after dispatch"); w = new World(); w.Listener.Callback = () => { w.NativeHandlerCalls++; World.Val(w.Cts, "IsCancellationRequested", true); }; Frames(); Assert(w.Listener.Attempts == 1 && w.NativeHandlerCalls == 1, "Respect native cancel side effect");
            Case("known nonrunning clears even hidden cancelled episode"); w = new World(); AutoThanksUi.Tick(); w.Button.Visible = false; World.Val(w.Cts, "IsCancellationRequested", true); World.Ref(w.Controller, "roomStateType", RuntimeObject.Box(3)); AutoThanksUi.Tick();
            World.Ref(w.Controller, "roomStateType", RuntimeObject.Box(4)); w.Button.Visible = true; World.Val(w.Cts, "IsCancellationRequested", false); Frames(); Assert(w.Listener.Attempts == 2, "Known scene exit must clear before hidden/cancelled early return");
            Case("all known nonrunning states clear consumed episode");
            foreach (var state in new[] { 0, 1, 2, 3, 5, 6 })
            {
                w = new World(); AutoThanksUi.Tick(); World.Ref(w.Controller, "roomStateType", RuntimeObject.Box(state)); AutoThanksUi.Tick(); World.Ref(w.Controller, "roomStateType", RuntimeObject.Box(4)); Frames();
                Assert(w.Listener.Attempts == 2, "Known nonrunning boundary must clean episode: " + state);
            }
            foreach (var id in new[] { 50000, 50001, 50002 })
            {
                Case("unfocused native gratitude " + id); w = new World();
                World.Val(w.Closure, "chatId", id); Application.isFocused = false; Frames(300);
                Assert(w.Listener.Attempts == 1 && w.NativeHandlerCalls == 1, "Live background recommendation confirms once: " + id);
                Application.isFocused = true; Frames(); Application.isFocused = false; Frames();
                Assert(w.Listener.Attempts == 1 && Compatibility.Blocks == 0, "Focus changes cannot resend the consumed recommendation");
            }
            foreach (var stale in new[] { "mouse", "touch", "drag" })
            {
                Case("unfocused stale input " + stale); w = new World(); Application.isFocused = false;
                if (stale == "mouse") for (var i = 0; i < 3; i++) Input.Held[i] = Input.Down[i] = Input.Up[i] = true;
                if (stale == "touch") Input.touchCount = 1;
                if (stale == "drag") World.Dragging = new("Dragging");
                Frames(); Assert(w.Listener.Attempts == 1 && Compatibility.Blocks == 0, "Stale background input must not delay native Thanks");
                Application.isFocused = true; Frames(); Input.Reset(); World.Dragging = null; Frames();
                Assert(w.Listener.Attempts == 1, "Clearing stale input/refocusing cannot repeat Thanks");
            }
            Gate("mod settings", (_, b) => ModUi.IsOpen = b);
            Gate("touch input", (_, b) => Input.touchCount = b ? 1 : 0);
            BoolGate("root modal waiting", x => x.Root, "modalWaiting", true);
            foreach (var id in new[] { 50000, 50001, 50002 })
            foreach (var context in new[] { 1, 2, 3 })
            {
                Case("native gratitude " + id + " with unrelated modal/popup " + context);
                w = new World(); World.Val(w.Closure, "chatId", id);
                World.Val(w.Root, "hasModalWindow", (context & 1) != 0);
                World.Val(w.Root, "hasAnyPopup", (context & 2) != 0);
                w.Listener.Callback = () =>
                {
                    w.NativeHandlerCalls++;
                    World.Val(w.Cts, "IsCancellationRequested", true);
                    World.Val(w.Bridge, "_isLocking", true);
                };
                AutoThanksUi.Tick();
                Assert(w.Listener.Attempts == 1 && w.NativeHandlerCalls == 1, "Native available recommendation must not expire behind unrelated UI: " + id);
                Frames(300); // Simulated 5s at 60fps, not game timing or a native/network invocation.
                World.Val(w.Root, "hasModalWindow", false); World.Val(w.Root, "hasAnyPopup", false);
                World.Val(w.Bridge, "_isLocking", false); Frames();
                Assert(w.Listener.Attempts == 1 && Compatibility.Blocks == 0, "Normal handler cancellation/retention and UI closure cannot retry");
            }
            foreach (var unavailable in new[] { "waiting", "locked", "manual", "expired", "foreign", "non-gratitude" })
            {
                Case("unrelated UI retains native/input safety: " + unavailable); w = new World();
                World.Val(w.Root, "hasModalWindow", true); World.Val(w.Root, "hasAnyPopup", true);
                switch (unavailable)
                {
                    case "waiting": World.Val(w.Root, "modalWaiting", true); break;
                    case "locked": World.Val(w.Bridge, "_isLocking", true); break;
                    case "manual": Input.Held[0] = true; break;
                    case "expired": World.Val(w.Cts, "IsCancellationRequested", true); break;
                    case "foreign": World.Ref(w.Closure, "<>4__this", new("Window")); break;
                    case "non-gratitude": World.Val(w.Closure, "chatId", 50003); break;
                }
                Frames(300); Assert(w.Listener.Attempts == 0 && Compatibility.Blocks == 0, "Popup/modal cannot bypass real unavailability: " + unavailable);
            }
            Case("manual acceptance during unrelated popup wins before next Tick"); w = new World();
            World.Val(w.Root, "hasAnyPopup", true); Input.Up[0] = true; AutoThanksUi.Tick();
            w.NativeHandlerCalls++; World.Val(w.Cts, "IsCancellationRequested", true); World.Val(w.Bridge, "_isLocking", true);
            Input.Reset(); Frames(); Assert(w.Listener.Attempts == 0 && w.NativeHandlerCalls == 1, "Manual native confirmation must not be followed by auto confirmation");
            Case("expired recommendation is not resurrected when UI closes"); w = new World();
            World.Val(w.Root, "hasModalWindow", true); Input.Held[0] = true; Frames(300);
            w.Button.Visible = false; World.Val(w.Cts, "IsCancellationRequested", true);
            Input.Reset(); World.Val(w.Root, "hasModalWindow", false); Frames();
            Assert(w.Listener.Attempts == 0, "Native expired episode stays expired after global eligibility returns");
            Gate("native dragging", (_, b) => World.Dragging = b ? new("Dragging") : null);
            for (int button = 0; button < 3; button++)
            {
                int index = button;
                Gate("mouse held " + index, (_, b) => Input.Held[index] = b);
                Gate("mouse down " + index, (_, b) => Input.Down[index] = b);
                Gate("mouse up " + index, (_, b) => Input.Up[index] = b);
            }
            Gate("temporarily hidden button", (x, b) => x.Button.Visible = !b);
            Gate("temporarily hidden window", (x, b) => x.Window.Visible = !b);
            Gate("button disposed", (x, b) => x.Button.Disposed = b);
            Gate("button off stage", (x, b) => x.Button.OnStage = !b);
            BoolGate("button not touchable", x => x.Button, "touchable", false);
            BoolGate("button grayed", x => x.Button, "grayed", true);
            Gate("no ExpressionWindow/cold startup", (x, b) => x.WindowPresent = !b);
            RefGate("no quick reply button", x => x.Window, "_btn_QuickReply");
            RefGate("no CTS", x => x.Window, "quickReplyCts");
            RefGate("no native CancellationTokenSource", x => x.Cts, "cts");
            BoolGate("disposed CTS guard", x => x.Cts, "_dispose", true);
            BoolGate("expired/cancelled CTS", x => x.Cts, "IsCancellationRequested", true);
            Gate("known PvP suspension boundary", (_, b) => PvpSafety.Suspended = b, true);
            Gate("root scene cleanup boundary", (x, b) => GameUi.Root = b ? null : x.Root, true);
            Gate("native home scene cleanup boundary", (_, b) => GameUi.HomeAvailable = b, true);
            Gate("missing manager context", (_, b) => World.Logic = b ? null : new RuntimeObject("Logic")
            { References = { ["room"] = World.Current.RoomLogic, ["watch"] = World.Current.Watch, ["replay"] = World.Current.Replay, ["battle"] = World.Current.Battle, ["account"] = World.Current.Account } });
            RefGate("missing room logic", _ => World.Logic, "room");
            RefGate("missing current room", x => x.RoomLogic, "curRoomInfo");
            RefGate("missing room controller", x => x.RoomLogic, "roomController");
            RefGate("missing room state", x => x.Controller, "roomStateType");
            Gate("known nonrunning room cleanup boundary", (x, b) => World.Ref(x.Controller, "roomStateType", RuntimeObject.Box(b ? 3 : 4)), true);
            Gate("PvP room", (x, b) => World.Ref(x.Room, "IsPVE", RuntimeObject.Box(!b)));
            RefGate("unverified PvE result", x => x.Room, "IsPVE");
            RefGate("missing room self", x => x.Room, "GetSelfInfo");
            RefGate("missing watcher logic", _ => World.Logic, "watch");
            Gate("spectator", (x, b) => World.Ref(x.Watch, "PlayerIsWatcher", RuntimeObject.Box(b)));
            RefGate("unverified watcher result", x => x.Watch, "PlayerIsWatcher");
            RefGate("missing replay logic", _ => World.Logic, "replay");
            RefGate("missing replay session", x => x.Replay, "Session");
            BoolGate("replay disallows C2S", x => x.Session, "CanSendC2S", false);
            RefGate("missing battle logic", _ => World.Logic, "battle");
            BoolGate("battle not ready", x => x.Battle, "clientFinishReady", false);
            RefGate("missing self battle data", x => x.Battle, "GetSelfPlayerData");
            RefGate("missing self player", x => x.BattlePlayer, "player");
            RefGate("missing account", _ => World.Logic, "account");
            RefGate("unverified account id", x => x.Account, "GetPlayerID");
            Gate("zero account id", (x, b) => World.Ref(x.Account, "GetPlayerID", RuntimeObject.Box(b ? 0L : 1234567890123L)));
            Gate("foreign/spectated player id", (x, b) => World.Val(x.Player, "Id", b ? 7654321098765L : 1234567890123L));
            RefGate("missing native transition", x => x.Window, "_showQuickReply");
            BoolGate("window not showing", x => x.Window, "isShowing", false);
            RefGate("no native listener", x => x.Button, "onClick");
            RefGate("no listener bridge", x => x.Listener, "_bridge");
            RefGate("no bridge owner before isEmpty", x => x.Bridge, "owner");
            BoolGate("locked native callback/manual race", x => x.Bridge, "_isLocking", true);
            BoolGate("empty native listener", x => x.Listener, "isEmpty", true);
            BoolGate("native listener already dispatching", x => x.Listener, "isDispatching", true);
            RefGate("no native zero-arg callback", x => x.Bridge, "_callback0");
            RefGate("missing invocation list", x => x.Delegate, "GetInvocationList");
            Gate("multicast invocation list", (x, b) => World.Val(x.Delegate.References["GetInvocationList"], "Length", b ? 2 : 1));
            Gate("zero callback invocation list", (x, b) => World.Val(x.Delegate.References["GetInvocationList"], "Length", b ? 0 : 1));
            Gate("additional context callback", (x, b) => World.Ref(x.Bridge, "_callback1", b ? new("Delegate") : null));
            Gate("additional capture callback", (x, b) => World.Ref(x.Bridge, "_captureCallback", b ? new("Delegate") : null));
            RefGate("no captured callback target", x => x.Delegate, "Target");
            RefGate("no captured recommendation window", x => x.Closure, "<>4__this");
            Gate("foreign captured recommendation window", (x, b) => World.Ref(x.Closure, "<>4__this", b ? new("Window") : x.Window));
            foreach (var id in new[] { 0, 1, 49999, 50003, 60000, -1 })
            {
                int value = id; Gate("non-gratitude chat id " + value, (x, b) => World.Val(x.Closure, "chatId", b ? value : 50000));
            }
            foreach (var id in new[] { 50000, 50001, 50002 })
            {
                Case("known native gratitude id " + id); w = new World(); World.Val(w.Closure, "chatId", id); Frames(); Assert(w.Listener.Attempts == 1, "Known native gratitude id allowed: " + id);
            }
            Case("disposed guard prevents throwing cancellation getter"); w = new World(); w.Cts.FailCancellationRead = true; World.Val(w.Cts, "_dispose", true); Frames(); Assert(w.Listener.Attempts == 0 && Compatibility.Blocks == 0, "Disposed CTS read must short circuit");
            Case("manual cancel while temporarily blocked"); w = new World(); ModUi.IsOpen = true; AutoThanksUi.Tick(); World.Val(w.Cts, "IsCancellationRequested", true); ModUi.IsOpen = false; Application.isFocused = false; Frames(); Assert(w.Listener.Attempts == 0, "Manual acceptance before resumed background Tick must win");
            Case("new episode while unfocused"); w = new World(); AutoThanksUi.Tick(); Application.isFocused = false; World.Val(w.Cts, "id", 42); Frames(); Assert(w.Listener.Attempts == 2, "New native episode confirms once without waiting for focus"); Application.isFocused = true; Frames(); Assert(w.Listener.Attempts == 2, "Refocusing cannot repeat the background episode");
            foreach (var unavailable in new[] { "pvp", "spectator", "replay", "expired", "locked", "foreign", "non-gratitude", "waiting", "settings", "nonrunning", "hidden" })
            {
                Case("background preserves native eligibility: " + unavailable); w = new World(); Application.isFocused = false;
                switch (unavailable)
                {
                    case "pvp": World.Ref(w.Room, "IsPVE", RuntimeObject.Box(false)); break;
                    case "spectator": World.Ref(w.Watch, "PlayerIsWatcher", RuntimeObject.Box(true)); break;
                    case "replay": World.Val(w.Session, "CanSendC2S", false); break;
                    case "expired": World.Val(w.Cts, "IsCancellationRequested", true); break;
                    case "locked": World.Val(w.Bridge, "_isLocking", true); break;
                    case "foreign": World.Val(w.Player, "Id", 987L); break;
                    case "non-gratitude": World.Val(w.Closure, "chatId", 50003); break;
                    case "waiting": World.Val(w.Root, "modalWaiting", true); break;
                    case "settings": ModUi.IsOpen = true; break;
                    case "nonrunning": World.Ref(w.Controller, "roomStateType", RuntimeObject.Box(3)); break;
                    case "hidden": w.Button.Visible = false; break;
                }
                Frames(); Assert(w.Listener.Attempts == 0 && Compatibility.Blocks == 0, "Background mode cannot bypass eligibility: " + unavailable);
            }
            foreach (var focused in new[] { true, false })
            foreach (var failure in new[] { 1, 2 })
            {
                Case("uncertain dispatch failure phase " + failure + " focused=" + focused); w = new World(); Application.isFocused = focused; w.Listener.CallFailure = failure; Frames();
                Assert(w.Listener.Attempts == 1 && Compatibility.Blocked && Compatibility.Blocks == 1, "Uncertain dispatch must trip independent breaker once");
                Assert(w.NativeHandlerCalls == (failure == 1 ? 0 : 1) && Compatibility.OtherBlocked, "Uncertain send boundary/native side effect scope");
                w.Listener.CallFailure = 0; Compatibility.Blocked = false; Frames(); Assert(w.Listener.Attempts == 1, "Even breaker reset cannot retry consumed uncertain dispatch");
            }
            Case("post-dispatch diagnostic failure"); w = new World(); Plugin.Diagnostics.Fail = true; AutoThanksUi.Tick(); Assert(w.Listener.Attempts == 1 && Compatibility.Blocks == 1, "Diagnostic throw after native callback is uncertain consumed episode"); Plugin.Diagnostics.Fail = false; Compatibility.Blocked = false; Frames(); Assert(w.Listener.Attempts == 1, "Diagnostic recovery must not retry");
            Case("unrelated failure breaker does not block AutoThanks"); w = new World(); Assert(Compatibility.OtherBlocked, "Unrelated feature starts disabled"); Frames(); Assert(w.Listener.Attempts == 1 && !Compatibility.Blocked && Compatibility.OtherBlocked, "AutoThanks still operates with unrelated feature breaker");
            Case("pre-dispatch native getter fault"); w = new World(); w.Cts.FailCancellationRead = true; Frames(); Assert(w.Listener.Attempts == 0 && Compatibility.Blocks == 1 && Compatibility.OtherBlocked, "Getter failure must isolate feature and send nothing");
            Case("compatibility denied"); w = new World(); Compatibility.Blocked = true; Frames(); Assert(w.Listener.Attempts == 0 && Compatibility.Blocks == 0, "Denied feature does no work");
            Case("other participant turn does not veto Thanks"); w = new World(); World.Val(w.Battle, "fixtureOtherPlayerTurn", true); Frames(); Assert(w.Listener.Attempts == 1, "Active local participant may accept native Thanks during another player's turn");
            Case("explicit scene cleanup"); w = new World(); AutoThanksUi.Tick(); AutoThanksUi.Clear(); Frames(); Assert(w.Listener.Attempts == 2, "Explicit scene-owned cleanup resets latch");
            Console.WriteLine("PASS: " + Assertions + " assertions across " + Cases + " AutoThanks production-source substitute cases. No native sender/game executed.");
        }
    }
}
