using System.Diagnostics;
using UnityEngine;

namespace BetterAstralParty;

// Development builds only (2026-11-18, frame drops reported as monsters pile up, late 원림 중정 above all): the native
// monster plate pass UIBattleInfoPanel.UpdateAttrInfoPosition runs every frame and compares every pair of plates, a dozen
// interpreted calls a pair. While diagnostics record in a PvE field outside fights, the pass is called once more a second
// and timed; it places every plate again from its owner each time, so the extra call changes nothing the frame's own pass
// does not. Each 10-second window logs `performance_native attrPass_us` (median and max) with the plate counts.
internal static class NativePassProbe
{
    private const string Feature = "NativePassProbe";
    private static readonly bool Enabled = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);
    private static readonly List<double> Samples = new();
    private static IntPtr _logicClass;
    private static float _sampleAt, _windowEnd;
    private static int _plates, _shown;

    internal static void Tick()
    {
        if (!Enabled || !Plugin.Diagnostics.IsRecording || PvpSafety.Suspended || !Compatibility.Allowed(Feature)) return;
        var now = Time.unscaledTime;
        if (now < _sampleAt) return;
        _sampleAt = now + 1;
        try { Sample(now); }
        catch (Exception ex) { Compatibility.Block(Feature, ex); }
    }

    private static void Sample(float now)
    {
        if (_logicClass == IntPtr.Zero) _logicClass = RuntimeObject.FindClass("GameLogic", "GameLogicManager");
        var logic = RuntimeObject.StaticField(_logicClass, "_inst");
        var room = logic?.Get("room")?.Get("curRoomInfo");
        if (room == null || !VisibleCombatAdvisor.IsPve(room.Field("info")?.Get<int>("MapType") ?? 0)
            || logic!.Get("fight")?.Field("fightStatus")?.Value<bool>() == true) return;
        var ui = logic.Get("battle")?.Field("battleInfo")?.Get("ui");
        var parent = ui?.Field("com_AttrInfos");
        if (!GameUi.Visible(parent)) return;
        int plates = 0, shown = 0;
        for (int i = 0, count = parent!.Get<int>("numChildren"); i < count; i++)
        {
            if (parent.Call("GetChildAt", i) is not { TypeName: "UICom_AttrInfo" } child) continue;
            plates++;
            if (child.Get<bool>("visible")) shown++;
        }
        var started = Stopwatch.GetTimestamp();
        ui!.Call("UpdateAttrInfoPosition");
        Samples.Add((Stopwatch.GetTimestamp() - started) * 1e6 / Stopwatch.Frequency);
        _plates = Math.Max(_plates, plates); _shown = Math.Max(_shown, shown);
        if (Samples.Count == 1) _windowEnd = now + 10;
        if (now < _windowEnd) return;
        Samples.Sort();
        Plugin.Diagnostics.Write(FormattableString.Invariant(
            $"performance_native attrPass_us_median={Samples[Samples.Count / 2]:F1}; attrPass_us_max={Samples[^1]:F1}; samples={Samples.Count}; plates_max={_plates}; shown_max={_shown}"));
        Samples.Clear(); _plates = _shown = 0;
    }
}
