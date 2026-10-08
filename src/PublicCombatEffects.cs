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
        var applied = new HashSet<int>();
        var evidence = Plugin.Diagnostics.IsRecording ? new List<string>(count) : null;
        var unmodeled = Plugin.Diagnostics.IsRecording ? new List<int>() : null;
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
            if (id != 10006) continue;
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
            // Progress is the public icon count. Only known per-stack rules multiply it.
            var stacks = buff.Get<int>("Progress");
            if (stacks is < 0 or > 10000) return null;
            if (id == 10012 && stacks > 0) mind = true;
            if (!applied.Add(id)) continue;
            int? value = id switch
            {
                2000801 => Parameter("Card", 20008, 3, 4, id),
                1071101 => Parameter("Skill", 10712, 0, 1, id),
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
            // STRBuff 10331101 explicitly reduces received damage to zero. Do not
            // infer immunity from NotSelect, a shield magnitude or a character ID.
            if (id == 10331101) immune = true;
            if (id is not (2000801 or 1071101 or 1140101 or 1140102 or 1261101
                or 10551101 or 10141101 or 10261101 or 10531201 or 1101101
                or 4000301 or 4000401 or 5006001 or 1291202 or 10671302 or 10331101 or 10591101))
                unmodeled?.Add(id);
        }
        if (evidence != null)
        {
            Plugin.Diagnostics.State("combatEffects." + role, string.Join(",", evidence));
            Plugin.Diagnostics.State("combatEffects.resolver." + role,
                $"additive-before-cap; adjustment={adjustment}; cap={damageCap}; immune={immune}; single-hit");
            Plugin.Diagnostics.State("combatEffects.unmodeled." + role, string.Join(",", unmodeled!));
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
            { ShushuShield = applied.Contains(1071101), MinimumHp = minimumHp, OnHitBonus = onHitBonus };
        result = ApplyDice(result, player!, source, applied, mind, role);
        return result.Valid ? result : null;
    }

    private static HitModifiers ApplyDice(HitModifiers result, RuntimeObject target, RuntimeObject? source,
        HashSet<int> targetBuffs, bool mind, string role)
    {
        // Only public profile passives and the same filtered native icon list as BattleStatusUi.
        // Final observed numbers still use HitDamage even if a pre-roll rule is unsupported.
        for (var side = 0; side < 2; side++)
        {
            var actor = side == 0 ? target : source;
            if (actor == null) continue;
            var capped = side == 0 && targetBuffs.Contains(1250101);
            var unsupported = side == 0 && targetBuffs.Contains(1250201);
            if (side == 1 && actor.Get("buffContainer") is { } container)
            {
                var buffs = container.Call("GetShowBuffs", actor, false)?.Field("Item1");
                var count = buffs?.Get<int>("Count") ?? -1;
                if (count is < 0 or > 256) return result with { DiceUnsupported = true };
                for (var i = 0; i < count; i++)
                {
                    var buff = buffs!.Call("get_Item", i)!;
                    if (buff.Get<int>("DelayRound") > 0) continue;
                    capped |= buff.Get<int>("BuffId") == 1250101;
                    unsupported |= buff.Get<int>("BuffId") == 1250201;
                }
            }
            if (unsupported) result = result with { DiceUnsupported = true }; // Zero-roll/conditional precedence not audited.
            var faces = default(DiceFaces);
            if (capped)
            {
                var maximum = Parameter("Skill", 12501, 0, 1, 1250101);
                if (maximum is null) return result with { DiceUnsupported = true };
                faces = DiceFaces.Range(1, maximum.Value);
            }
            var passives = actor.Field("player")?.Call("GetBattlePassiveSkillId");
            var passiveCount = passives?.Get<int>("Count") ?? 0;
            if (passiveCount is < 0 or > 256) return result with { DiceUnsupported = true };
            for (var i = 0; i < passiveCount; i++)
            {
                var id = passives!.Call("get_Item", i)!.Value<int>();
                if (side == 0 && id == 104011)
                {
                    // Skill104011.Params: movement face, dodge face, pass-through damage, candy IDs/weights.
                    var fixedFace = Parameter("Skill", id, 1, 9, 10401101);
                    if (fixedFace is null) return result with { DiceUnsupported = true };
                    result = result with { DodgeDice = DiceFaces.Range(fixedFace.Value, fixedFace.Value) };
                }
                if (side == 1 && id == 106311)
                {
                    // Original STRSkill106311: 3..6; without public Mind, Skill.Params[0] is the fixed face.
                    // No server roll or private buff parameters are read. Overlapping cap precedence is unknown.
                    if (capped) return result with { DiceUnsupported = true };
                    var maximum = Parameter("Skill", id, 0, 4, 10631101);
                    if (maximum is null) return result with { DiceUnsupported = true };
                    faces = DiceFaces.Range(mind ? 3 : maximum.Value, maximum.Value);
                }
            }
            // The native catalogue proves support, not weighting/clamp/reroll semantics.
            // Keep thresholds usable; do not claim an exact pre-roll probability for restricted ranges.
            result = side == 0 ? result with { DefenseDice = faces, DefenseDistributionUnknown = faces.Mask != 0 && faces.Count > 1 }
                : result with { AttackDice = faces, AttackDistributionUnknown = faces.Mask != 0 && faces.Count > 1 };
        }
        if (!result.DiceValid) result = result with { DiceUnsupported = true };
        if (Plugin.Diagnostics.IsRecording)
            Plugin.Diagnostics.State("combatDice." + role, $"attack={result.AttackDice}; defense={result.DefenseDice}; dodge={result.DodgeDice}; unsupported={result.DiceUnsupported}; attackWeightUnknown={result.AttackDistributionUnknown}; defenseWeightUnknown={result.DefenseDistributionUnknown}");
        return result;
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

    private static int? Parameter(string kind, int id, int index, int expectedCount, int buffId, int expectedBuffCount = 1)
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
            var associated = false;
            for (var i = 0; i < expectedBuffCount; i++)
                associated |= ids.Call("get_Item", i)!.Value<int>() == buffId;
            if (!associated) return null;
        }
        var bonus = parameters.Call("get_Item", index)!.Value<int>();
        return bonus is >= -100 and <= 100 ? bonus : null;
    }
}
