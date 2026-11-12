using Il2CppInterop.Runtime;
using UnityEngine;
using NativeDelegate = Il2CppSystem.Delegate;

namespace BetterAstralParty;

// Esc on the idle home opens the game's own quit question (user-requested 2026-11-12): UIManager.messageBox.ShowOKCancel with
// message 1014 and GameManager.CloseGame as its OK, as the settings' Exit Game button and the home's former exit function
// (BottomMenuPanel.ExecuteOperate) do. The game's home handles only Enter; its message box takes Esc as Cancel and Enter as OK.
// HomeEscape asks only on a home idle both around the key, and a message already showing or on its way is left alone, so if
// the game ever answers Esc on the home itself, its window or message comes first and this stays out of the way.
internal static class HomeEscQuit
{
    internal const string Feature = "HomeEscQuit";
    private const int QuitMessage = 1014;
    private static readonly HomeEscape State = new();
    // A native delegate bound to the game's own method, as HandClickBinding does: no managed callback or injected type.
    private static NativeDelegate? _ok;
    private static IntPtr _okTarget;
    private static int _count;

    internal static void Tick()
    {
        if (!Compatibility.Allowed(Feature)) return;
        var idle = GameUi.NotificationHome().Eligible && !UpdateNotificationUi.Open;
        if (State.Step(Time.frameCount, idle, Input.GetKeyDown(KeyCode.Escape))) Ask();
    }

    private static void Ask()
    {
        var box = RuntimeObject.StaticField(RuntimeObject.FindClass("UI", "UIManager"), "_inst")?.Get("messageBox");
        // MessageBoxWindow sets curMsg before its asynchronous show, and queues a second message behind it.
        var busy = box == null || box.Get<bool>("isShowing") || box.Field("curMsg")!.Field<int>("typeIndex") >= 0;
        if (!busy)
        {
            var game = RuntimeObject.StaticCall(RuntimeObject.FindClass("Core", "GameManager"), "get_inst")
                ?? throw new InvalidOperationException("Core.GameManager unavailable");
            if (_ok == null || _okTarget != game.Pointer)
            {
                var action = RuntimeObject.FindClass("System", "Action");
                if (action == IntPtr.Zero) throw new TypeLoadException("System.Action");
                var type = new Il2CppSystem.Type(IL2CPP.il2cpp_type_get_object(IL2CPP.il2cpp_class_get_type(action)));
                _ok = NativeDelegate.CreateDelegate(type, new Il2CppSystem.Object(game.Pointer), "CloseGame");
                _okTarget = game.Pointer;
            }
            box!.Call("ShowOKCancel", QuitMessage, new RuntimeObject(_ok.Pointer), null, false);
        }
        Plugin.Diagnostics.State("homeEscQuit", $"{++_count} {(busy ? "busy" : "asked")}");
    }
}
