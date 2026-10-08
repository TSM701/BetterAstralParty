using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace BetterAstralParty;

// Observe native local input waits; never send requests or change the game's timers.
internal static class InputAttention
{
    private static float _nextPoll;
    private static IntPtr _window;
    private static bool _flashing;
    private static string _mode = "Off";

    internal static void Tick()
    {
        var mode = InputAttentionState.Normalize(Plugin.InputAttention.Value);
        if (_mode != mode) { Stop(); _mode = mode; }
        if (mode == "Off" || Application.isFocused || !Compatibility.Allowed("InputAttention"))
        { Stop(); return; }
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.2f;
        try
        {
            var logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
            var timerClass = RuntimeObject.FindClass("GameLogic", "OperationTimer");
            if (logicClass == IntPtr.Zero || timerClass == IntPtr.Zero) { Stop(); return; }
            var logic = RuntimeObject.StaticField(logicClass, "_inst");
            var roomLogic = logic?.Get("room");
            var room = roomLogic?.Get("curRoomInfo");
            var running = roomLogic?.Field("roomController")?.Field("roomStateType")?.Value<int>() == 4;
            var participant = room?.Call("GetSelfInfo") != null
                && logic?.Get("watch")?.Call("PlayerIsWatcher")?.Value<bool>() == false;
            var replay = logic?.Get("replay")?.Get("Session")?.Get<bool>("IsReplay") == true;
            var pve = room?.Call("IsPVE")?.Value<bool>() == true;
            var pending = running && participant && !replay && pve
                ? RuntimeObject.StaticField(timerClass, "timerDict")?.Get<int>("Count") ?? 0 : 0;
            // Native ActionDownTime deliberately skips campaign/map 10. Observe their
            // actual local controls instead; do not create a timer or change game timeouts.
            if (running && participant && !replay && pve && pending == 0
                && (room!.Call("IsCampaign")!.Value<bool>() || room.Get<int>("MapType") == 10))
                pending = UntimedInput(logic!) ? 1 : 0;
            if (!InputAttentionState.ShouldFlash(mode != "Off", Application.isFocused,
                    running, participant, replay, pve, pending)) { Stop(); return; }
            if (!OwnWindow(_window)) { Stop(); _window = FindWindow(); }
            if (_window == IntPtr.Zero || GetForegroundWindow() == _window) { Stop(); return; }
            if (_flashing) return;
            if (mode == "Windows") WindowsInputNotification.Show(_window,
                ModText.Text("게임에서 입력을 기다리고 있습니다."));
            else Flash(_window, 0x00000002 | 0x0000000C); // FLASHW_TRAY | FLASHW_TIMERNOFG
            _flashing = true;
            Plugin.Diagnostics.State("inputAttention", "waiting");
        }
        catch (Exception ex) { Compatibility.Block("InputAttention", ex, Stop); }
    }

    private static bool UntimedInput(RuntimeObject logic)
    {
        var root = GameUi.Root;
        if (!GameUi.Visible(root)) return false;
        var fight = GameUi.Find(root!, "FightWindow");
        if (fight != null)
        {
            var ui = fight.Get("contentPane");
            if (ui == null || ui.Field("playerState")?.Get<int>("selectedIndex") == 2) return false;
            var step = ui.Field("step")?.Get<int>("selectedIndex") ?? 0;
            return step is 1 or 3 or 5 && HasButton(ui);
        }
        for (var i = 0; i < root!.Get<int>("numChildren"); i++)
        {
            var window = root.Call("GetChildAt", i)!;
            if (InputAttentionState.InputWindow(window.TypeName) && HasButton(window)) return true;
        }
        // This native state is set for local movement/cards/target selection, not other turns.
        return logic.Get("action")?.Get<int>("playerAction") is > 0
            && GameUi.Find(root, "HandCardPanel") != null;
    }

    private static bool HasButton(RuntimeObject node, int depth = 0)
    {
        if (!GameUi.Visible(node) || !node.Get<bool>("touchable") || node.Get<bool>("grayed")) return false;
        var button = node.Get("asButton");
        if (button != null) return node.Get("name")?.String() != "closeButton";
        if (depth >= 8 || node.Get("asCom") == null) return false;
        for (var i = 0; i < node.Get<int>("numChildren"); i++)
            if (HasButton(node.Call("GetChildAt", i)!, depth + 1)) return true;
        return false;
    }

    internal static void Stop()
    {
        WindowsInputNotification.Hide();
        if (!_flashing) return;
        _flashing = false;
        if (_mode == "Taskbar" && OwnWindow(_window)) Flash(_window, 0); // FLASHW_STOP
        Plugin.Diagnostics.State("inputAttention", "stopped");
    }

    private static bool OwnWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window)) return false;
        GetWindowThreadProcessId(window, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static IntPtr FindWindow()
    {
        var found = IntPtr.Zero;
        // Avoid flashing BepInEx's console, another Unity game, or another user's window.
        EnumWindows((window, _) =>
        {
            if (!OwnWindow(window)) return true;
            var name = new StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "UnityWndClass") return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static void Flash(IntPtr window, uint flags)
    {
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window,
            Flags = flags, Count = uint.MaxValue };
        // Return value is the previous active state, not an operation-success flag.
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags, Count, Timeout; }
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
}
