using Il2CppInterop.Runtime;
using UnityEngine;

namespace BetterAstralParty;

// Magic Academy (map 017) replaces its board at a phase change: MapGimmickManager017.SwitchMap moves the camera to the
// centre and plays the scene's SubMapSwitchController, whose IsPlaying stays true until the new board has replaced the
// old one. The field zoom yields while it runs (user-requested 2026-11-11, after players reported the game stopping at
// that switch with BAP; the cause was not confirmed). The game finds the controller with FindObjectOfType, so this
// looks it up once per land dictionary, retrying every few seconds while none is found. A failure here only stops the
// detection for the session, never the zoom.
internal static class SubMapSwitch
{
    private static IntPtr _lands;
    private static UnityEngine.Object? _controller;
    private static float _retryAt;
    private static bool _failed;

    internal static bool Running(IntPtr lands)
    {
        if (_failed) return false;
        try
        {
            if (lands == IntPtr.Zero) { _lands = IntPtr.Zero; _controller = null; return false; }
            if (lands != _lands || !_controller && Time.unscaledTime >= _retryAt)
            {
                _lands = lands; _controller = null; _retryAt = Time.unscaledTime + 5;
                var klass = RuntimeObject.FindClass("GameLogic", "SubMapSwitchController");
                if (klass == IntPtr.Zero) throw new TypeLoadException("GameLogic.SubMapSwitchController");
                var type = new Il2CppSystem.Type(IL2CPP.il2cpp_type_get_object(IL2CPP.il2cpp_class_get_type(klass)));
                _controller = UnityEngine.Object.FindAnyObjectByType(type);
            }
            return _controller && new RuntimeObject(_controller!.Pointer).Field<bool>("IsPlaying");
        }
        catch (Exception ex)
        {
            _failed = true; _controller = null;
            Plugin.Diagnostics.Error("SubMapSwitch", ex);
            return false;
        }
    }
}
