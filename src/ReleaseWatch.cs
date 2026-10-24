namespace BetterAstralParty;

// Diagnostics only (logging ON): after the player lets go, record what the server did to the monster's public HP,
// so combat-audit can test the release-damage rules against real results. Nothing is shown in game.
internal static class ReleaseWatch
{
    private static IntPtr _monster;
    private static int _heroId, _hp, _sequence;
    private static string _evidence = "";
    private static float _armedAt = -1;

    // Offered on the encounter screen; refreshed every frame until the screen closes.
    internal static void Offer(RuntimeObject monster, string evidence)
    {
        if (_armedAt >= 0) return;
        _monster = monster.Pointer;
        _heroId = monster.Field("player")?.Get("Hero")?.Get<int>("HeroId") ?? 0;
        _hp = monster.Get("Property")?.Field("HP")?.Get<int>("Value") ?? -1;
        _evidence = evidence;
    }

    // step: the fight window's step, or -1 when it is closed. An encounter (step 1) that closes without a fight
    // (step 2 or later) was a release; one that turns into a fight is not watched.
    internal static void Poll(RuntimeObject? logic, int step)
    {
        // A diagnostic failure never reaches the advice breaker.
        try { Watch(logic, step); }
        catch (Exception ex) { Plugin.Diagnostics.Error("ReleaseWatch", ex); _armedAt = -1; _monster = IntPtr.Zero; }
    }

    private static void Watch(RuntimeObject? logic, int step)
    {
        if (_armedAt < 0)
        {
            if (_monster == IntPtr.Zero || step == 1) return;
            if (step == -1 && _hp >= 0) _armedAt = UnityEngine.Time.unscaledTime;
            else _monster = IntPtr.Zero;
            return;
        }
        // A fight that starts before the result arrives can move the same HP; that change is not the release's.
        if (step >= 2) { Log("after=interrupted"); return; }
        // Only an object the game hands back this frame is read: never dereference the remembered pointer itself.
        var monster = logic?.Get("battle")?.Call("GetPlayerDataByHeroId", _heroId);
        var property = monster?.Pointer == _monster ? monster!.Get("Property") : null;
        if (property != null)
        {
            var hp = property.Field("HP")!.Get<int>("Value");
            // ChangeHP is the native applied change (BattleProperty.OnLifeChanged); on a KO it may keep the overkill.
            if (hp != _hp) { Log($"after={hp}; changeHP={property.Field("ChangeHP")!.Get<int>("Value")}"); return; }
        }
        if (UnityEngine.Time.unscaledTime - _armedAt > 10) Log(property == null ? "after=unresolved" : "after=unchanged");
    }

    private static void Log(string result)
    {
        Plugin.Diagnostics.State("shootingStar.result", $"release={++_sequence}; before={_hp}; {result}; {_evidence}");
        _armedAt = -1; _monster = IntPtr.Zero;
    }
}
