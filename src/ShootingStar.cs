namespace BetterAstralParty;

// What releasing a monster yields with the Shooting Star relics (#14): damage to that monster and Starlight stacks.
internal readonly record struct ReleaseGain(int DamageMin, int DamageMax, int Stacks)
{
    internal string Damage => DamageMin == DamageMax ? DamageMin.ToString() : $"{DamageMin}~{DamageMax}";
}

internal static class ShootingStar
{
    // Relic IDs and their scalar relic buffs; values come from the live Relic table (Params [-damage, +stacks]).
    internal const int Purple = 50078, Gold = 50079, PurpleBuff = 5007801, GoldBuff = 5007901, Starlight = 10005;
    // party.model.Hero.Types.MonsterType: Boss = 2, PveBoss = 10 (protobuf enum constants).
    internal static bool IsBoss(int monsterType) => monsterType is 2 or 10;

    // The server resolves release effects; the client has no formula. Gold adds the Starlight stacks against
    // bosses; observed in game (2026-10-22), the stacks this release grants are not part of its own damage, so the
    // bonus is the count held before letting go. Fixed damage takes the monster's incoming modifiers (2026-10-23:
    // 2 + 15 Starlight + Weakness Mark +1 = 18). A per-hit cap (Treasure Barrel) is not observed on this damage yet,
    // so it widens the result to a range instead of being assumed. Two held relics hit one after the other, each its
    // own hit with the modifiers (user-confirmed 2026-10-24).
    internal static ReleaseGain? Calculate((int Damage, int Stacks)? purple, (int Damage, int Stacks)? gold, bool boss, int starlight,
        HitModifiers incoming = default)
    {
        if (purple == null && gold == null || starlight < 0) return null;
        // OnHitBonus is the erosion stack a combat hit applies; a release is not a hit.
        incoming = incoming with { OnHitBonus = 0 };
        var uncapped = incoming with { Cap = 0 };
        var goldDamage = gold == null ? 0 : gold.Value.Damage + (boss ? starlight : 0);
        return new(Hits(purple, gold, goldDamage, incoming), Hits(purple, gold, goldDamage, uncapped), (purple?.Stacks ?? 0) + (gold?.Stacks ?? 0));
    }

    private static int Hits((int Damage, int Stacks)? purple, (int Damage, int Stacks)? gold, int goldDamage, HitModifiers modifiers) =>
        (purple == null ? 0 : Hit(purple.Value.Damage, modifiers)) + (gold == null ? 0 : Hit(goldDamage, modifiers));

    private static int Hit(int damage, HitModifiers modifiers) => (int)CombatAdvisor.HitDamage(damage, modifiers, int.MaxValue);

    // Public data only: the relic list the player-info window shows, the public Starlight property, the monster type
    // and the monster's shown incoming modifiers (the same reader the encounter advice uses).
    internal static ReleaseGain? Read(RuntimeObject? self, RuntimeObject? monster, HitModifiers? incoming)
    {
        if (incoming == null) return null;
        var relics = self?.Call("GetRelicIds");
        var count = relics?.Get<int>("Count") ?? -1;
        if (count is < 0 or > 256) return null;
        bool purple = false, gold = false;
        for (var i = 0; i < count; i++)
        {
            var id = relics!.Call("get_Item", i)!.Value<int>();
            purple |= id == Purple; gold |= id == Gold;
        }
        if (!purple && !gold) return null;
        (int, int)? Relic(bool owned, int relic, int buff)
        {
            if (!owned) return null;
            var damage = PublicCombatEffects.Parameter("Relic", relic, 0, 2, buff);
            var stacks = PublicCombatEffects.Parameter("Relic", relic, 1, 2, buff);
            return damage is < 0 && stacks is >= 0 ? (-damage.Value, stacks.Value) : null;
        }
        var purpleValues = Relic(purple, Purple, PurpleBuff);
        var goldValues = Relic(gold, Gold, GoldBuff);
        if (purple && purpleValues == null || gold && goldValues == null) return null;
        var boss = IsBoss(monster?.Field("player")?.Get("Hero")?.Get<int>("MonsterType") ?? 0);
        var starlight = StarlightStacks(self);
        var gain = Calculate(purpleValues, goldValues, boss, starlight, incoming.Value);
        // The raw inputs let combat-audit try other rules against the observed result.
        Evidence = $"purple={purpleValues?.Item1 ?? 0}; gold={goldValues?.Item1 ?? 0}; boss={boss}; starlight={starlight}; "
            + $"incoming={incoming.Value.Bonus}; cap={incoming.Value.Cap}; stacks={gain?.Stacks}; damage={gain?.Damage}";
        return gain;
    }

    internal static string Evidence { get; private set; } = "";

    private static int StarlightStacks(RuntimeObject? self)
    {
        var properties = self?.Get("buffContainer")?.Call("GetShowBuffs", self, false)?.Field("Item2");
        var count = properties?.Get<int>("Count") ?? -1;
        if (count is < 0 or > 256) return -1;
        for (var i = 0; i < count; i++)
        {
            var property = properties!.Call("get_Item", i)!;
            if (property.Field("buffId")!.Value<int>() == Starlight) return property.Field("property")!.Get<int>("Value");
        }
        return 0;
    }
}
