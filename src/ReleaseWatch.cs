namespace BetterAstralParty;

// Diagnostics only (logging ON): after the player lets go, record what the server did to the monster's public HP,
// so combat-audit can test the release-damage rules against real results. Nothing is shown in game.
internal static class ReleaseWatch
{
    // Two held relics hit one after the other: the result waits until the HP has been still this long.
    private const float Settle = 2, Timeout = 10;
    private static IntPtr _monster;
    private static int _hp, _lastHp, _changeTotal, _sequence;
    private static readonly List<int> Hits = new();
    private static string _evidence = "";
    private static float _armedAt = -1, _changedAt;

    // Offered on the encounter screen; refreshed every frame until the screen closes.
    internal static void Offer(RuntimeObject monster, string evidence)
    {
        if (_armedAt >= 0) return;
        _monster = monster.Pointer;
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
        var now = UnityEngine.Time.unscaledTime;
        if (_armedAt < 0)
        {
            if (_monster == IntPtr.Zero || step == 1) return;
            if (step == -1 && _hp >= 0) { _armedAt = now; _lastHp = _hp; _changeTotal = 0; Hits.Clear(); }
            else _monster = IntPtr.Zero;
            return;
        }
        // A fight that starts before the result arrives can move the same HP; that change is not the release's.
        if (step >= 2) { Log("after=interrupted"); return; }
        // Only objects the game hands back this frame are read: the remembered pointer is compared, never dereferenced.
        // GetPlayerDataByHeroId found another monster of the same kind, so most releases logged as unresolved.
        var monster = Find(logic?.Get("battle")?.Field("PlayerDatas"));
        var property = monster?.Get("Property");
        if (property != null)
        {
            var hp = property.Field("HP")!.Get<int>("Value");
            if (hp != _lastHp)
            {
                // ChangeHP is the native applied change (BattleProperty.OnLifeChanged); on a KO it may keep the overkill.
                var change = property.Field("ChangeHP")!.Get<int>("Value");
                Hits.Add(hp - _lastHp); _changeTotal += change; _lastHp = hp; _changedAt = now;
            }
        }
        if (Hits.Count > 0 && (property == null || now - _changedAt > Settle))
            Log($"after={_lastHp}; changeHP={_changeTotal}; hits={string.Join(",", Hits)}" + (property == null ? "; removed=True" : ""));
        else if (property == null) Log("after=removed");
        else if (now - _armedAt > Timeout) Log("after=unchanged");
    }

    private static RuntimeObject? Find(RuntimeObject? players)
    {
        for (int i = 0, count = players?.Get<int>("Count") ?? 0; i < count; i++)
            if (players!.Call("get_Item", i) is { } player && player.Pointer == _monster) return player;
        return null;
    }

    private static void Log(string result)
    {
        Plugin.Diagnostics.State("shootingStar.result", $"release={++_sequence}; before={_hp}; {result}; {_evidence}");
        _armedAt = -1; _monster = IntPtr.Zero;
    }
}
