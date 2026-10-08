using UnityEngine;

namespace BetterAstralParty;

internal static class FocusAudio
{
    private static bool _muted;

    internal static void Tick()
    {
        if (!Compatibility.Allowed("MuteUnfocused")) return;
        var mute = Plugin.MuteUnfocused.Value && !Application.isFocused;
        if (!mute && !_muted) return;
        try
        {
            if (!AkSoundEngine.IsInitialized()) { _muted = false; return; }
            if (mute == _muted) return;
            // Keep audio timelines/callbacks advancing silently; never edit saved volume.
            var result = mute ? AkSoundEngine.Suspend(true) : AkSoundEngine.WakeupFromSuspend();
            if (result != AKRESULT.AK_Success) throw new InvalidOperationException($"Wwise: {result}");
            _muted = mute;
            AkSoundEngine.RenderAudio();
            Plugin.Diagnostics.State("audio.focusMute", mute.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("FocusAudio", ex);
            Compatibility.Block("MuteUnfocused", ex, Restore);
        }
    }

    internal static void Restore()
    {
        if (!_muted) return;
        if (AkSoundEngine.IsInitialized())
        {
            var result = AkSoundEngine.WakeupFromSuspend();
            if (result != AKRESULT.AK_Success) throw new InvalidOperationException($"Wwise restore: {result}");
            AkSoundEngine.RenderAudio();
        }
        _muted = false;
    }
}
