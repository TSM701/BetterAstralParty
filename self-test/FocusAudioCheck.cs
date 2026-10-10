// Test doubles for the actual FocusAudio.Tick implementation; no game process required.
namespace UnityEngine
{
    internal static class Application { internal static bool isFocused = true; }
    internal static class Time { internal static float unscaledTime; }
}

internal enum AKRESULT { AK_Success, AK_Fail }
internal static class AkSoundEngine
{
    internal static bool Initialized = true;
    internal static AKRESULT Result = AKRESULT.AK_Success;
    internal static readonly List<string> Calls = new();
    internal static bool IsInitialized() => Initialized;
    internal static AKRESULT Suspend(bool renderAnyway)
    {
        if (!renderAnyway) throw new Exception("Background audio timelines must continue");
        Calls.Add("mute"); return Result;
    }
    internal static AKRESULT WakeupFromSuspend() { Calls.Add("restore"); return Result; }
    internal static void RenderAudio() { }
}

namespace BetterAstralParty
{
    internal static class Plugin
    {
        internal sealed class Flag { internal bool Value; }
        internal sealed class Log
        {
            internal bool IsRecording => true;
            internal void LogWarning(object value) { }
            internal void State(string key, string value) { }
            internal void Error(string key, Exception error) { }
        }
        internal static readonly Flag MuteUnfocused = new();
        internal static readonly Log Diagnostics = new();
        internal static readonly Log Logger = new();
    }

    internal static class FocusAudioCheck
    {
        internal static void Run()
        {
            void Expect(string expected)
            {
                FocusAudio.Tick();
                if (string.Join(",", AkSoundEngine.Calls) != expected)
                    throw new Exception("Focus audio transition failed: " + expected);
            }
            Expect(""); // Default OFF never touches native audio.
            UnityEngine.Application.isFocused = false;
            Expect("");
            Plugin.MuteUnfocused.Value = true;
            AkSoundEngine.Initialized = false;
            Expect(""); // Wait for the audio engine.
            AkSoundEngine.Initialized = true;
            Expect("mute");
            Expect("mute"); // No per-frame re-suspension.
            UnityEngine.Application.isFocused = true;
            Expect("mute,restore");
            UnityEngine.Application.isFocused = false;
            Expect("mute,restore,mute");
            Plugin.MuteUnfocused.Value = false;
            Expect("mute,restore,mute,restore"); // OFF restores even while unfocused.
            Plugin.MuteUnfocused.Value = true;
            Expect("mute,restore,mute,restore,mute");
            AkSoundEngine.Initialized = false;
            Expect("mute,restore,mute,restore,mute");
            AkSoundEngine.Initialized = true;
            Expect("mute,restore,mute,restore,mute,mute"); // Engine restart.
            UnityEngine.Application.isFocused = true;
            Expect("mute,restore,mute,restore,mute,mute,restore");
            UnityEngine.Application.isFocused = false;
            AkSoundEngine.Result = AKRESULT.AK_Fail;
            Expect("mute,restore,mute,restore,mute,mute,restore,mute");
            UnityEngine.Time.unscaledTime = 30;
            AkSoundEngine.Result = AKRESULT.AK_Success;
            Expect("mute,restore,mute,restore,mute,mute,restore,mute"); // No retry after failure.
            if (Compatibility.Allowed("MuteUnfocused") || !Plugin.MuteUnfocused.Value)
                throw new Exception("Audio failure must block the session without changing saved preference");
            CompatibilityCheck.Run();
            CatalogCheck.Run();
            Console.WriteLine("포커스 음소거 OFF·왕복·즉시 해제·초기화 대기·오류 차단·엔진 재시작 검사 통과");
        }
    }

    internal static class CompatibilityCheck
    {
        internal static void Run()
        {
            var cleanups = 0;
            var revision = Compatibility.Revision;
            Compatibility.Block("FieldBuffs", new MissingFieldException("test"), () => cleanups++);
            Compatibility.Block("FieldBuffs", new Exception("again"), () => cleanups++);
            if (cleanups != 1 || Compatibility.Revision != revision + 1 || Compatibility.Allowed("FieldBuffs")
                || !Compatibility.Allowed("Enabled")) throw new Exception("Feature isolation/dedup failed");
            Compatibility.Block("Enabled", new Exception("test"), () => throw new Exception("cleanup"), () => cleanups++);
            if (Compatibility.Allowed("Details") || cleanups != 2) throw new Exception("Dependency/cleanup isolation failed");
            Compatibility.Block("CoreUi", new Exception("test"));
            if (Compatibility.Allowed("CardPopups") || !Compatibility.Allowed("Diagnostics") || !Compatibility.Allowed("Notices"))
                throw new Exception("Common UI dependency failed");
            Console.WriteLine("호환성 기능 격리·의존 기능 차단·중복 방지·정리 실패 격리·설정 보존 검사 통과");
        }
    }
}
