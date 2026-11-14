namespace BetterAstralParty;

// Read-only, incremental coverage. Do not interpret arbitrary buff text as executable rules.
internal static class PublicCombatEffects
{
    // Called only after the caller's PvE gate. Ren's PvP -3 must not enter here.
    internal static HitModifiers? Read(RuntimeObject? player, string role, RuntimeObject? source)
    {
        var damageCap = 0;
        var immune = false;
        var shown = player?.Get("buffContainer")?.Call("GetShowBuffs", player, false);
        var buffs = shown?.Field("Item1");
        if (buffs == null) return null;
        var count = buffs.Get<int>("Count");
        if (count is < 0 or > 256) return null;
        var adjustment = 0;
        var mind = false;
        var precision = 0;
        var applied = new HashSet<int>();
        var evidence = Plugin.Diagnostics.IsRecording ? new List<string>(count) : null;
        var unknown = new SortedSet<int>();
        // Marks are public property effects (Item2), not ordinary buffs (Item1).
        // Read the same filtered list as BattleStatusUi; never inspect hidden effects.
        var properties = shown!.Field("Item2");
        if (properties == null) return null;
        var propertyCount = properties.Get<int>("Count");
        if (propertyCount is < 0 or > 256) return null;
        for (var i = 0; i < propertyCount; i++)
        {
            var property = properties.Call("get_Item", i)!;
            var id = property.Field("buffId")!.Value<int>();
            if (!CombatBuffAudit.Counted(id)) unknown.Add(id);
            // Every property's public stacks go to the evidence so combat-audit can tell when one (e.g. 10009) fires.
            if (id != 10006) { evidence?.Add($"property:{id}:{property.Field("property")!.Get<int>("Value")}"); continue; }
            var stacks = property.Field("property")!.Get<int>("Value");
            if (stacks is < 0 or > 100) return null;
            if (!applied.Add(id)) continue;
            // STRBuff 10006 says +1 while marked, not +1 per layer.
            adjustment = checked(adjustment + (stacks > 0 ? 1 : 0));
            evidence?.Add($"property:{id}:{stacks}");
        }
        for (var i = 0; i < count; i++)
        {
            var buff = buffs.Call("get_Item", i)!;
            var id = buff.Get<int>("BuffId");
            var delay = buff.Get<int>("DelayRound");
            evidence?.Add($"{id}:{buff.Get<int>("Progress")}:{buff.Get<int>("KeepRound")}:delay={delay}");
            if (delay > 0) continue;
            if (!CombatBuffAudit.Counted(id)) unknown.Add(id);
            // Progress is the public icon count. Only known per-stack rules multiply it.
            var stacks = buff.Get<int>("Progress");
            if (stacks is < 0 or > 10000) return null;
            if (id == 10012 && stacks > 0) mind = true;
            if (id == 1251201) precision = Math.Max(precision, stacks);
            // A stacking buff is one icon with a count. Card 20008 (Berserk) makes a new buff each time it is played, and
            // each adds: 2026-11-06, a hero with two shown took 8 from ATK 14 against DEF 8.
            if (!applied.Add(id) && id != 2000801) continue;
            var firewall = 0;
            if (id == 1201302)
            {
                // STRBuff 1201302 (Nancy's 防火墙): ATK +2 and DEF +2, already in the native totals, and "damage taken from
                // monsters more than two panels away -1". Lands that cannot be read keep it not counted.
                if (FarMonster(player, source) is not { } far) { unknown.Add(id); continue; }
                firewall = far ? -1 : 0;
            }
            int? value = id switch
            {
                1201302 => firewall,
                2000801 => Parameter("Card", 20008, 3, 4, id),
                1071101 => Parameter("Skill", 10712, 0, 1, id),
                // Skill 10711's -3 shield got its own buff in the 2026-10 tables; not the Shushu shield (no Dodge override).
                1071102 => Parameter("Skill", 10711, 0, 1, id),
                1140101 => Parameter("Skill", 11401, 0, 4, id),
                1140102 => Parameter("Skill", 11402, 0, 3, id),
                1261101 => Parameter("Skill", 12611, 0, 2, id) * stacks,
                10551101 => Parameter("Skill", 105511, 0, 1, id) * stacks,
                10141101 => Parameter("Skill", 101411, 0, 1, id) * stacks,
                // Existing public stacks only: never grant the stack earned by this hit.
                10261101 => Parameter("Skill", 102611, 0, 3, id) * stacks,
                10531201 => Parameter("Skill", 105312, 2, 3, id) * stacks,
                1101101 => Parameter("Skill", 11011, 1, 2, id),
                4000301 => Parameter("Destiny", 40003, 0, 1, id),
                4000401 => Parameter("Destiny", 40004, 0, 1, id),
                5006001 => Parameter("Relic", 50060, 0, 2, id) * stacks,
                1291202 => ErosionBonus(source, stacks),
                10671302 => Parameter("Skill", 106713, 5, 15, id, 3),
                10331101 or 10591101 => 0,
                // Includes stat-only/unrelated buffs; this is not a coverage assertion.
                _ => 0
            };
            if (value == null)
            {
                Plugin.Diagnostics.State("combatEffects.schema." + role, id.ToString());
                return null;
            }
            adjustment = checked(adjustment + value.Value);
            evidence?.Add($"resolved:{id}:add={value.Value}");
            // Original public buff description: at most one damage per hit.
            if (id == 10591101) damageCap = 1;
            // STRBuff 10331101 (boss skill 103311) reads "ignores attacks, cannot be selected, takes 0 damage", but it is
            // a targeting lock: the boss cannot be attacked until its condition is met, and the icon stays (progress 0)
            // after that. A fight with it as defender means the lock is open; observed 2026-10-22, a 30-damage hit KO'd
            // the 9-HP boss with the icon shown. So it changes nothing in a hit. Never infer immunity from NotSelect,
            // a shield magnitude or a character ID.
        }
        // The attacker's own shown effects (dodge locks, extra attacks, conditional ATK) can change this hit too.
        if (!AddUnknown(source, unknown)) return null;
        if (evidence != null)
        {
            Plugin.Diagnostics.State("combatEffects." + role, string.Join(",", evidence));
            Plugin.Diagnostics.State("combatEffects.resolver." + role,
                $"additive-before-cap; adjustment={adjustment}; cap={damageCap}; immune={immune}; single-hit");
            Plugin.Diagnostics.State("combatEffects.unknown." + role, string.Join(",", unknown));
        }
        // RoomPlayer selects the native passive list (including monster passives).
        // Skill 100312 preserves target heroes' HP; it is not defender immunity.
        var minimumHp = 0;
        var onHitBonus = 0;
        var passives = source?.Field("player")?.Call("GetBattlePassiveSkillId");
        if (passives != null)
        {
            var passiveCount = passives.Get<int>("Count");
            if (passiveCount is < 0 or > 256) return null;
            var ids = Plugin.Diagnostics.IsRecording ? new List<int>(passiveCount) : null;
            for (var i = 0; i < passiveCount; i++)
            {
                var id = passives.Call("get_Item", i)!.Value<int>();
                ids?.Add(id);
                if (id == 12912)
                {
                    // 0.22.4 nonlethal replays 113/120 include the newly applied erosion
                    // stack in this hit (+1), even when no old icon exists. Not a flat
                    // incoming modifier: a blocked hit/successful dodge cannot trigger it.
                    var gain = Parameter("Skill", id, 4, 6, 1291202, 2);
                    if (gain is null or < 0) return null;
                    onHitBonus = gain.Value;
                }
                if (id != 100312 || player?.Get("characterType")?.Value<int>() != 1) continue;
                var floor = Parameter("Skill", id, 0, 1, 10031201);
                if (floor is null or < 0) return null;
                minimumHp = floor.Value;
            }
            if (Plugin.Diagnostics.IsRecording)
                Plugin.Diagnostics.State("combatEffects.source." + role,
                    $"passives={string.Join(",", ids!)}; targetType={player?.Get("characterType")?.Value<int>()}; minimumHP={minimumHp}; onHitBonus={onHitBonus}");
        }
        var result = new HitModifiers(adjustment, damageCap, immune)
            { ShushuShield = applied.Contains(1071101), MinimumHp = minimumHp, OnHitBonus = onHitBonus, Unknown = Unknown(unknown) };
        result = ApplyDice(result, player!, source, applied, mind, precision, role);
        return result.Valid ? result : null;
    }

    private static HitModifiers ApplyDice(HitModifiers result, RuntimeObject target, RuntimeObject? source,
        HashSet<int> targetBuffs, bool mind, int precision, string role)
    {
        // Only public profile passives and the same filtered native icon list as BattleStatusUi.
        // Final observed numbers still use HitDamage even if a pre-roll rule is unsupported.
        for (var side = 0; side < 2; side++)
        {
            var actor = side == 0 ? target : source;
            if (actor == null) continue;
            var capped = side == 0 && targetBuffs.Contains(1250101);
            var marked = side == 0 && targetBuffs.Contains(1250201);
            var unsupported = false;
            if (side == 1 && actor.Get("buffContainer") is { } container)
            {
                var buffs = container.Call("GetShowBuffs", actor, false)?.Field("Item1");
                var count = buffs?.Get<int>("Count") ?? -1;
                if (count is < 0 or > 256) return result with { AttackUnsupported = true };
                for (var i = 0; i < count; i++)
                {
                    var buff = buffs!.Call("get_Item", i)!;
                    if (buff.Get<int>("DelayRound") > 0) continue;
                    capped |= buff.Get<int>("BuffId") == 1250101;
                    marked |= buff.Get<int>("BuffId") == 1250201;
                }
            }
            // STRBuff 1250201 (Moses's 약점 반격): "모제스와 교전 시, 공격 및 방어 주사위가 0이 됩니다". Character 125 is Moses
            // (CharacterHandle.Id, independent of skins). Logged 2026-11-08: a marked monster's shown attack and defense
            // dice were 0 against Moses (fights 5, 6, 13, 23, 24) and ordinary against hero 102 (fight 43).
            var zeroed = false;
            if (marked)
            {
                var opponent = (side == 0 ? source : target)?.Field("player")?.Field("characterConfig")?.Field("Id")?.Value<int>();
                if (opponent is null or <= 0) unsupported = true;
                else zeroed = opponent == 125;
            }
            // A mark whose opponent cannot be told apart from Moses; on the attacker it only affects pre-roll views.
            if (unsupported) result = side == 0 ? result with { DiceUnsupported = true } : result with { AttackUnsupported = true };
            var faces = default(DiceFaces);
            if (capped)
            {
                var maximum = Parameter("Skill", 12501, 0, 1, 1250101);
                if (maximum is null) return side == 0 ? result with { DiceUnsupported = true } : result with { AttackUnsupported = true };
                faces = DiceFaces.Range(1, maximum.Value);
            }
            var passives = actor.Field("player")?.Call("GetBattlePassiveSkillId");
            var passiveCount = passives?.Get<int>("Count") ?? 0;
            if (passiveCount is < 0 or > 256) return side == 0 ? result with { DiceUnsupported = true } : result with { AttackUnsupported = true };
            for (var i = 0; i < passiveCount; i++)
            {
                var id = passives!.Call("get_Item", i)!.Value<int>();
                if (side == 0 && id == 104011)
                {
                    // Skill104011.Params: movement face, dodge face, pass-through damage, candy IDs/weights. Since the
                    // 2026-11-12 tables its BuffId also lists 20004 (闪身, "always chooses Evade"), a standard icon.
                    var fixedFace = Parameter("Skill", id, 1, 9, 10401101, 2);
                    if (fixedFace is null) return result with { DiceUnsupported = true };
                    result = result with { DodgeDice = DiceFaces.Range(fixedFace.Value, fixedFace.Value) };
                }
                if (side == 1 && id == 106311)
                {
                    // Original STRSkill106311: 3..6; without public Mind, Skill.Params[0] is the fixed face.
                    // No server roll or private buff parameters are read. Overlapping cap precedence is unknown.
                    if (capped) return result with { AttackUnsupported = true };
                    var maximum = Parameter("Skill", id, 0, 4, 10631101);
                    if (maximum is null) return result with { AttackUnsupported = true };
                    faces = DiceFaces.Range(mind ? 3 : maximum.Value, maximum.Value);
                }
            }
            // Moses's 정밀 기술 (buff 1251201, STRSkill 12512): "the dodge die always comes out above the current stack count";
            // Params [3 max stacks, ATK +2 per stack, dodge floor +1 per stack], the ATK already in the native total. Its
            // weighting is not observed: the faces above the floor count equally, as a die that never shows a lower face.
            if (side == 0 && precision > 0)
            {
                var step = Parameter("Skill", 12512, 2, 3, 1251201);
                if (step is null or < 0 || result.DodgeDice.Mask != 0) return result with { DiceUnsupported = true };
                result = result with { DodgeDice = DiceFaces.Range(Math.Min(6, 1 + precision * step.Value), 6) };
            }
            // The marked side's dice are 0 whatever else limits them; a defender's die serves its dodge too. Only a monster
            // carries the mark (skill 125020), so no hero dice passive meets it.
            if (zeroed)
            {
                faces = DiceFaces.Range(0, 0);
                if (side == 0) result = result with { DodgeDice = faces };
            }
            // The native catalogue proves support, not weighting/clamp/reroll semantics.
            // Keep thresholds usable; do not claim an exact pre-roll probability for restricted ranges.
            result = side == 0 ? result with { DefenseDice = faces, DefenseDistributionUnknown = faces.Mask != 0 && faces.Count > 1 }
                : result with { AttackDice = faces, AttackDistributionUnknown = faces.Mask != 0 && faces.Count > 1 };
        }
        if (!result.DiceValid) result = result with { DiceUnsupported = true };
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("combatDice." + role, $"attack={result.AttackDice}; defense={result.DefenseDice}; dodge={result.DodgeDice}; unsupported={result.DiceUnsupported}; attackUnsupported={result.AttackUnsupported}; attackWeightUnknown={result.AttackDistributionUnknown}; defenseWeightUnknown={result.DefenseDistributionUnknown}");
        return result;
    }

    // Same public lists as the target's (Item1 buffs, Item2 property effects); delayed buffs are not active yet.
    private static bool AddUnknown(RuntimeObject? actor, SortedSet<int> unknown)
    {
        var shown = actor?.Get("buffContainer")?.Call("GetShowBuffs", actor, false);
        if (shown == null) return true;
        for (var list = 0; list < 2; list++)
        {
            var items = shown.Field(list == 0 ? "Item1" : "Item2");
            var count = items?.Get<int>("Count") ?? -1;
            if (count is < 0 or > 256) return false;
            for (var i = 0; i < count; i++)
            {
                var item = items!.Call("get_Item", i)!;
                if (list == 0 && item.Get<int>("DelayRound") > 0) continue;
                var id = list == 0 ? item.Get<int>("BuffId") : item.Field("buffId")!.Value<int>();
                if (!CombatBuffAudit.Counted(id)) unknown.Add(id);
            }
        }
        return true;
    }

    internal static UnknownEffects Unknown(SortedSet<int> ids)
    {
        var shown = ids.Take(4).ToArray();
        int At(int index) => index < shown.Length ? shown[index] : 0;
        return new(At(0), At(1), At(2), At(3), ids.Count);
    }

    // Whether the actor dealing the hit is a monster more than two panels from the one taking it; a player is never one.
    // "Within two panels" is the game's own LandManager.CheckDistance(from, to, 2, 0), the range test cards use; it walks
    // neighbours only, so the same land is checked here. null: a land or the distance cannot be read.
    private static bool? FarMonster(RuntimeObject? target, RuntimeObject? source)
    {
        var type = source?.Get("characterType")?.Value<int>();
        if (type == null) return null;
        if (type != 2) return false;
        static int? Land(RuntimeObject? actor) => actor?.Field("CharacterInst")?.Get("standLand")?.Get<int>("Id");
        if (Land(target) is not { } from || Land(source) is not { } to) return null;
        if (from == to) return false;
        var within = RuntimeObject.StaticField(RuntimeObject.FindClass("Core.Unit", "LandManager"), "_inst")?.Call("CheckDistance", from, to, 2, 0);
        return within == null ? null : !within.Value<bool>();
    }

    private static int? ErosionBonus(RuntimeObject? source, int stacks)
    {
        if (stacks == 0) return 0;
        // STRBuff 1291202: +1 per existing stack, only from Sykes or his tentacles.
        // Character 129 is Sykes; CharacterHandle.Id is independent of cosmetic skins.
        // Tentacles are land-summon damage, not actors in this dice-combat pipeline.
        // The +1 is specified by STRBuff, not Skill12912.Params[4] (stack application).
        var id = source?.Field("player")?.Field("characterConfig")?.Field("Id")?.Value<int>();
        if (id == null || id <= 0) return null;
        return id == 129 ? stacks : 0;
    }

    internal static int? Parameter(string kind, int id, int index, int expectedCount, int buffId, int expectedBuffCount = 1)
    {
        // Active catalog numbers, with a schema/buff association guard. No text parsing,
        // hidden Buff.Params, game-state writes, or persistent copies of catalog values.
        var configClass = RuntimeObject.FindClass("", "StaticConfigure");
        var table = RuntimeObject.StaticCall(configClass, "get_" + kind)?.Get("InfoDict");
        if (table?.Call("ContainsKey", id)?.Value<bool>() != true) return null;
        var config = table.Call("get_Item", id)!;
        var parameters = config.Get("Params")!;
        if (parameters.Get<int>("Count") != expectedCount) return null;
        // Relic uses a scalar BuffId, unlike Skill's repeated BuffId.
        if (kind == "Relic")
        {
            if (config.Get<int>("BuffId") != buffId) return null;
        }
        else
        {
            var ids = config.Get(kind == "Skill" ? "BuffId" : "BuffIds")!;
            if (ids.Get<int>("Count") != expectedBuffCount) return null;
            // expectedBuffCount 0: a config that grants no buff (card 10010's final-ATK percent).
            var associated = expectedBuffCount == 0;
            for (var i = 0; i < expectedBuffCount; i++)
                associated |= ids.Call("get_Item", i)!.Value<int>() == buffId;
            if (!associated) return null;
        }
        var bonus = parameters.Call("get_Item", index)!.Value<int>();
        return bonus is >= -100 and <= 100 ? bonus : null;
    }
}
