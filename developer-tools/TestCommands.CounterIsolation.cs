namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void CounterIsolation(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context,"src/BattleStatusUi.cs");
        var tick = Derive(source,@"(?s)    internal static void Tick\(\).*?(?=    private static void Scan)","Actual public rows tick with isolated counter");
        var boundary = Derive(source,@"(?s)    private static void UpdateCounter\(.*?\r?\n    \}","Actual counter-only exception boundary");
        Contains(boundary,new[]{"Compatibility.Allowed(CounterFeature)","RefreshCounter(actorUi); PositionCounter();","Compatibility.Block(CounterFeature, ex, ClearCounter, EncounterCounterUi.Clear)"},"Counter failures clean up only counter indicators");
        Check(!boundary.Contains("ClearRows",StringComparison.Ordinal) && !boundary.Contains("BattleStatusUi.Clear",StringComparison.Ordinal),"Counter breaker never owns public row cleanup");
        var encounter = GeneralWithoutNamespace(ReadSource(context,"src/EncounterCounterUi.cs")).Replace("using UnityEngine;","",StringComparison.Ordinal);
        Check(encounter.Contains("Compatibility.Block(BattleStatusUi.CounterFeature, ex, Clear, BattleStatusUi.ClearCounter)",StringComparison.Ordinal)
            && !encounter.Contains("Compatibility.Block(\"BattleStatus\"",StringComparison.Ordinal),"Encounter failures do not disable public rows");
        var path=Path.Combine(context.Root,"focus-tests/counter-isolation/Fixture.cs");pin(path);var fixture=File.ReadAllText(path);
        foreach(var marker in new[]{"__BATTLE_TICK__","__COUNTER_BOUNDARY__","__ENCOUNTER__"})Check(fixture.Split(marker,StringSplitOptions.None).Length==2,"Exact literal counter isolation substitution "+marker);
        fixture=fixture.Replace("__BATTLE_TICK__",tick,StringComparison.Ordinal).Replace("__COUNTER_BOUNDARY__",boundary,StringComparison.Ordinal).Replace("__ENCOUNTER__",encounter,StringComparison.Ordinal);
        RunGeneralFixture(context,Path.Combine(stage,"counter-isolation"),fixture,"CounterIsolationCheck.Run()",pin);
    }
}
