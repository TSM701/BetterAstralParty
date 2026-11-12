using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void RemainingHpProjection(DevContext context, string stage, Action<string> pin)
    {
        var view = ReadSource(context, "src/RemainingHpUi.cs"); var game = ReadSource(context, "src/GameUi.cs"); var dice = ReadSource(context, "src/CardDiceUi.cs");
        Contains(view, new[] { "GameUi.Visible(parent.Field(\"txt_Failure\"))", "fight.Get(\"attackerData\")?.Get(\"characterType\")?.Value<int>() == 2", "if (step == 6 && !_resultLogged && _prediction >= 0" }, "Monster defeat result gates");
        using var native = Native(context);
        var result = Il(NativeAsyncMoveNext(native, "BattleShowDirector", "PlayDefenderDead"));
        Check(Regex.IsMatch(result, "get_characterType\\(\\)") && result.Contains("ldc.i4.2", StringComparison.Ordinal) && Regex.IsMatch(result, @"ldc.i4.1\r?\n[^\n]*Mathf::Max") && result.Contains("UIFight_PlayerInfo::RefreshLife", StringComparison.Ordinal), "Native monster defeat HP display clamp");
        var showDice = Il(NativeAsyncMoveNext(native, "BattleShowDirector", "Dice_Show"));
        var refresh = Il(native.MainModule.GetType("UI.UIFight_PlayerInfo").Methods.Single(method => method.Name == "RefreshPoint_Defender"));
        Check(showDice.Contains("BattleRole::get_Dodge", StringComparison.Ordinal) && Regex.IsMatch(showDice, @"UIFight_Com_Defenser::showResult\r?\n[^\n]*ldc.i4.2\r?\n[^\n]*Controller::set_selectedIndex")
            && Regex.IsMatch(showDice, @"ldc.i4.2\r?\n[^\n]*ldc.i4.0\r?\n[^\n]*ldc.i4.0\r?\n[^\n]*UIFight_PlayerInfo::RefreshPoint_Defender")
            && Regex.IsMatch(refresh, @"UIFight_Com_Defenser::changeState\r?\n[^\n]*ldarg.3\r?\n[^\n]*brtrue[^\n]*\r?\n[^\n]*ldc.i4.0"), "Native failed dodge has zero total and clears its icon");
        // The decided fight (ServerFight): the BattleS2C that ends a fight updates FightLogic.battleFightData and then, on
        // IsEnd, starts PlayBattleResult; the director reads that data, adds the defender's IncHp to its HP for VictimDead,
        // shows IncHp as the hit value and decides a dodge by the two dice.
        var ended = Il(NativeAsyncMoveNext(native, "GameLogic.FightLogic", "OnBattleS2CServerCallBack"));
        var showDirector = native.MainModule.GetType("BattleShowDirector");
        string DirectorIl(string name) => Il(showDirector.Methods.Single(method => method.Name == name));
        var victimHp = DirectorIl("VictimHp"); var dodgeSuccess = DirectorIl("DefendDiceSuccess");
        var hit = Il(native.MainModule.GetType("Core.Unit.HitBehaviour").Methods.Single(method => method.Name == "Play"));
        Check(Regex.IsMatch(ended, @"FightLogic::UpdateFightData[\s\S]*Battle::get_IsEnd\(\)[\s\S]*BattleShowDirector::PlayBattleResult")
            && DirectorIl("get_fightData").Contains("GameLogic.FightLogic::battleFightData", StringComparison.Ordinal)
            && Regex.IsMatch(victimHp, @"BattleProperty::HP[\s\S]*BattleFightData::defenderInfo\r?\n[^\n]*BattleRole::get_IncHp\(\)\r?\n[^\n]*add")
            && Regex.IsMatch(DirectorIl("VictimDead"), @"BattleShowDirector::VictimHp\(\)\r?\n[^\n]*ldc.i4.0\r?\n[^\n]*cgt")
            && Regex.IsMatch(dodgeSuccess, @"attackerInfo\r?\n[^\n]*BattleRole::get_Point\(\)[\s\S]*defenderInfo\r?\n[^\n]*BattleRole::get_Point\(\)")
            && Regex.IsMatch(hit, @"BattleShowDirector::VictimIncHp\(\)\r?\n[^\n]*UIFight_PlayerInfo::ShowHitValue")
            && Regex.IsMatch(DirectorIl("ActiveBattlePlatform"), @"stfld [^\n]*BattleShowDirector::attackerId[\s\S]*stfld [^\n]*BattleShowDirector::defendeId"),
            "Native decided fight: updated before the result show, IncHp is the shown hit and decides the KO, players bound per fight");
        Contains(ReadSource(context, "src/ServerFight.cs"), new[] { "if (Shown(director) is not { } fight || !fight.Data.Field(\"isEnd\")!.Value<bool>()) return null;", "boost.Count == 0 || point == 0 ? atk - point : boost.Before(atk) - point,",
            "attacker.Get<long>(\"PlayerId\") != director.Field(\"attackerId\")!.Value<long>()", "defender.Get<long>(\"PlayerId\") != director.Field(\"defendeId\")!.Value<long>()",
            "_failed = true;" }, "Server fight data is read only after both choices, only for the shown fight, behind its own breaker");
        // The director binds a fight's players in ActiveBattlePlatform before refreshing its actors' panels.
        var director = native.MainModule.GetType("BattleShowDirector");
        Check(new[] { "_attackerData", "_defenderData" }.All(name => director.Fields.Single(field => field.Name == name).FieldType.FullName == "GameLogic.BattlePlayerData")
            && Regex.IsMatch(Il(director.Methods.Single(method => method.Name == "ActiveBattlePlatform")), @"stfld [^\n]*BattleShowDirector::_attackerData[\s\S]*stfld [^\n]*BattleShowDirector::_defenderData[\s\S]*BattleActor::RefreshUI"),
            "Director binds the fight's players before refreshing its panels");
        // Release diagnostics find the released monster by pointer in BattleLogic.PlayerDatas, not by its (shared) hero ID.
        Check(native.MainModule.GetType("GameLogic.BattleLogic").Fields.Single(field => field.Name == "PlayerDatas").FieldType.FullName
            == "System.Collections.Generic.List`1<GameLogic.BattlePlayerData>", "Native battle player list");
        Contains(ReadSource(context, "src/ReleaseWatch.cs"), new[] { "Find(logic?.Get(\"battle\")?.Field(\"PlayerDatas\"))", "player.Pointer == _monster", "hits={string.Join(\",\", Hits)}" },
            "Release diagnostics follow the same monster through every hit");
        Check(!view.Contains(".Get(\"name\")", StringComparison.Ordinal) && view.Contains("child.Field(\"name\")", StringComparison.Ordinal), "Final artwork reads actual native name field");
        Check(!Regex.IsMatch(view, "PKMainCamera|ProjectBattlePoint|LocalToGlobal|GlobalToLocal"), "Final damage artwork is screen-fixed");
        Contains(view, new[] { "Ensure(root)", "AdviceText.DamageEnd(_resultAt < 0 ? -1 : _resultAt - _shownAt)", "AdviceText.DamageAlpha(elapsed, end, _lethal)", "if (step == 6 && _showing && _resultAt < 0) _resultAt = Time.unscaledTime;", "_shownAt = Time.unscaledTime; _resultAt = -1;", "AdviceText.RecommendationScale", "damage.layout", 
            "step == 6 && !_resultLogged && _prediction >= 0", "Plugin.Diagnostics.CombatResult(_hp, _prediction, hp, _modifiers, monsterDefeat, changeHp)", "_resultLogged = false;",
            "PublicCombatEffects.Read(defender, \"remainingHP\", fight.Get(\"attackerData\"))", "RefreshArtwork(a)", "_pointContext == _parent?.Pointer",
            "_point.Get(\"parent\")?.Pointer == parent.Pointer", "image.Set(\"texture\", texture)", "damage.artwork", "if (_layout != layout)", "if (_alpha != alpha)",
            "decided is not { } server) return;", "_prediction = Math.Max(0, -server.HpChange);", "if (!Plugin.FinalDamage.Value) return; // Retain public final evidence",
            "damage.server before=", "incHp={server.HpChange}; model={model}" }, "Final evidence from the server, modifiers, artwork caching and independent lifetime");
        Check(view.IndexOf("?? _modifiers;", StringComparison.Ordinal) < view.IndexOf("LogModel(server);", StringComparison.Ordinal), "Final modifiers refreshed before the model check and the KO view");
        Check(!Regex.IsMatch(view, "&& elapsed >= 0.5f"), "Damage does not wait for the entrance fade");
        NoPattern(view, @"com_value|FinalDamageReady|int\.TryParse|""PointChange""\)!\.Get<bool>|UnknownEffectsRow", "Damage figure reads no dice, totals or arithmetic text, and the server's figure leaves no effect uncounted");
        Contains(game, new[] { "CardKoMinimum.RevealedDie(attackerUi.Field(\"showPoint\")", "PublicCombatEffects.Read(fight.Get(\"defenderData\"), \"defender\", fight.Get(\"attackerData\"))",
            "PublicCombatEffects.Read(fight.Get(\"attackerData\"), \"encounter.self\", enemy)", "PublicCombatEffects.Read(enemy, \"encounter.enemy\", fight.Get(\"attackerData\"))",
            "RemainingHpUi.Update(fight, ui, attackerUi, defenderUi, step, hp, step == 5 ? ServerFight.Decided(director) : null);", "RemainingHpUi.Animate();", "RemainingHpUi.ResetCapture();", "CardDiceUi.PollResolved();", "CardUi.Clear(keepDice: step is 4 or 5 or 6 || spectatorCards)", "CardDiceUi.BindResolved",
            "(role is 0 or 1 || role == 2 && Plugin.KoSpectator.Value)", "director!.Field(\"_attackerData\")?.Pointer != fight.Get(\"attackerData\")?.Pointer" }, "Advice direction, final evidence polling and revealed-roll lifecycle");
        Check(game.IndexOf("CardKoMinimum.RevealedDie(attackerUi.Field(\"showPoint\")", StringComparison.Ordinal) < game.IndexOf("var input = new VisibleCombat(", StringComparison.Ordinal), "Advice rejects final totals before constructing raw-die input");
        Contains(ReadSource(context, "src/NativeUi.cs"), new[] { "LocalToWorld", "WorldToViewportPoint", "BattleStatusLayout.Project", "RootToLocal" }, "Shared world-space buff projection");
        Check(ReadSource(context, "src/BattleStatusUi.cs").Contains("NativeUi.ProjectBattlePoint", StringComparison.Ordinal), "Buff rows retain world-space projection");
        Contains(dice, new[] { "CombatAdvisor.IsDodge(defense.Field(\"changeState\")", "defense.Field(\"showResult\")" }, "Public announced dodge after native icon reset");
        Contains(dice, new[] { "pair == null ? null : CardKoMinimum.OutcomeRange(input, card, thresholdDie)", "var unconditional = status is \"KO 불가\" or \"확정 KO\";",
            "CardDiceLayout.Content(pair, thresholdDie, index, survival, unconditional, input.Modifiers.DefenseDice, fixedDefense, input.Modifiers.AttackDice)", "CardDiceLayout.Title(survival)", "var survival = role == 1 || role == 2 && input.DefenderPlayer;", "var thresholdDie = fixedDie;",
            "ModText.Revision, role, finalDamage, revealed)", "CardKoMinimum.Survival(input, card, thresholdDie, possible: false)", "CardKoMinimum.Calculate(input, card, thresholdDie, possible: false)",
            "CardDiceLayout.Status(pair, damage, input.Hp, survival)", "koMinimum.content", "supported={pair != null}", "status={status}; metadataOnly=True",
            "reason={(unsupported ? \"card-effect\" : pair == null ? \"public-input\" : \"none\")}",
            "if (finalDamage is >= 0) damage = ((long)finalDamage.Value, (long)finalDamage.Value);", "_resolvedUi?.Pointer != ui.Pointer",
            "NativeUi.Create(\"Fight\", \"修改为 90-90\")", "diamond.Call(\"SetXY\", 35f, 35f)", "NativeUi.Label(point, \"\", 38, 30, 36, 100, 36", "format.SetField(\"letterSpacing\", 0)",
            "TintArtwork(disc, Color.cyan)", "index == 0 ? Color.red : Color.cyan", "filter.Call(\"Tint\", color, 1f)", "image.Set(\"filter\", filter)", "CardDiceLayout.Row(row)",
            "NativeUi.Component(_panel, 160, 160)", "row.Empty.Set(\"text\", content.Empty)", "if (!revealed) { _resolvedUi = _attacker = _defender = null; _final = false; }", "defenseDie is >= 1 and <= 6 ? defenseDie : 0, revealed: true);", "Show(_resolvedUi, point, input, null, false, false, die, revealed: true);",
            "GameUi.Visible(number), die, _resolvedInput.Modifiers.AttackDice.ZeroOnly)", "Get<int>(\"selectedIndex\") != 5 || _final)", "row == 0 ? \"骰子底P1\" : \"骰子底P3\"", "if (!Plugin.KoMinimum.Value) { Clear(); return; }",
            "_unknownRow!.Show(finalDamage is >= 0 ? default : input.Modifiers.Unknown);" }, "Minimum-condition policy, native dice presentation and independent setting");
        // Before the server decides, only the attack die is read, at showPoint 1; the decided hit settles the view (ShowFinal).
        NoPattern(dice, @"com_value|""PointChange""\)|RevealedDefenseDie|FightValues|defensePoint", "KO view reads no arithmetic panel, totals or defense die text");
        Check(!Regex.IsMatch(dice, "ThresholdRange|DefenseThresholds|생존 가능 조건|KO 가능 조건|rangeText") && !dice.Contains("공격 눈 공개", StringComparison.Ordinal), "No mixed threshold policy or redundant revealed subtitle");
        var layout = ReadSource(context, "src/CardDiceLayout.cs");
        Check(!layout.Contains("피해량", StringComparison.Ordinal) && !layout.Contains("/ HP", StringComparison.Ordinal) && !dice.Contains("방어 선택 시 피해량", StringComparison.Ordinal), "Minimum conditions never become a damage presentation");
        foreach (var forbidden in new[] { "NativeUi.Arrow(", "\"이상\"", "\"이하\"", "\"확정\"", "\"Fight_Com_Point\"", "Set(\"visible\", possible)", "hpTop", "Local(life,", "NativeUi.Surface", "HigherAlsoPossible", "Plugin.ShowDetails.Value", "Plugin.Enabled.Value" })
            Check(!dice.Contains(forbidden, StringComparison.Ordinal), "Dice preserve numeric semantics without legacy presentation " + forbidden);
        Check(view.Contains("try { if (!server.Dodge) CardDiceUi.ShowFinal(parent, a, _hp, _prediction, _modifiers, server.AttackDie, server.DefenseDie); }", StringComparison.Ordinal)
            && view.IndexOf("CardDiceUi.ShowFinal(", StringComparison.Ordinal) > view.IndexOf("_prediction = Math.Max(0, -server.HpChange);", StringComparison.Ordinal)
            && view.IndexOf("_prediction = Math.Max(0, -server.HpChange);", StringComparison.Ordinal) > view.IndexOf("decided is not { } server) return;", StringComparison.Ordinal)
            && view.IndexOf("if (!Plugin.FinalDamage.Value) return;", StringComparison.Ordinal) > view.IndexOf("CardDiceUi.ShowFinal(", StringComparison.Ordinal)
            && view.IndexOf("if (!Plugin.FinalDamage.Value) return;", StringComparison.Ordinal) < view.IndexOf("            Ensure(root);", StringComparison.Ordinal), "Final certainty follows the server's decided hit and precedes the independent artwork gate");
        var animate = MethodFragment(view, "internal static void Animate"); var displayClear = MethodFragment(view, "internal static void ClearDisplay");
        Contains(animate, new[] { "if (!Plugin.FinalDamage.Value) { ClearDisplay(); return; }", "if (GameUi.HomeAvailable || root == null", "{ Clear(); return; }" }, "Display-only setting cleanup remains distinct from scene cleanup");
        NoPattern(animate, @"Plugin\.(Enabled|KoMinimum)\.Value", "Final damage artwork is independent of advice and KO settings");
        NoPattern(displayClear, @"ResetCapture|_hp|_die|_prediction|_captured|_resultLogged", "Display cleanup retains public hit evidence");
        Check(ReadSource(context, "src/ModUi.cs").Contains("case \"FinalDamage\": Plugin.FinalDamage.Value = !Plugin.FinalDamage.Value; RemainingHpUi.ClearDisplay(); break;", StringComparison.Ordinal), "Final damage toggle uses display-only cleanup rather than resetting the in-flight hit");
        var cards = ReadSource(context, "src/CardUi.cs"); var observerStart = cards.IndexOf("        if (role == 2)", StringComparison.Ordinal); var observerEnd = cards.IndexOf("        if (role is not (0 or 1))", StringComparison.Ordinal);
        Check(observerStart >= 0 && observerEnd > observerStart && observerEnd < cards.IndexOf("var ready =", StringComparison.Ordinal) && observerEnd < cards.IndexOf("var cards =", StringComparison.Ordinal), "Spectator branch precedes ready and private-hand access");
        RunGeneralFixture(context, Path.Combine(stage, "spectator"), GeneralRemainingHp0.Replace("__OBSERVER__", cards[observerStart..observerEnd], StringComparison.Ordinal), "SpectatorKoProbe.Run()", pin);
        Check(!cards.Contains("Plugin.ShowDetails.Value", StringComparison.Ordinal), "KO card previews independent of hover-details setting");
        Contains(cards, new[] { "var hit = Plugin.KoMinimum.Value && Plugin.KoCardPreview.Value", "preview ? hovered.Bonus : null", "parsed.Attack != (role == 0)", "if (!CardAdviceOn) { candidates.Clear(); return; }" }, "Local defender card validation and conditions-only observation");
        var adviceAt = game.IndexOf("        if (!Plugin.Enabled.Value || !Plugin.DefenseAdvice.Value) return;", game.IndexOf("CardDiceUi.BindResolved", StringComparison.Ordinal), StringComparison.Ordinal);
        Check(adviceAt >= 0 && adviceAt < game.IndexOf("var input = new VisibleCombat(", StringComparison.Ordinal), "Advice OFF keeps public dice observation without recommendation art");
        var entryStart = game.IndexOf("        if (!Plugin.Enabled.Value && !Plugin.KoMinimum.Value && !Plugin.FinalDamage.Value)", StringComparison.Ordinal); var entryEnd = game.IndexOf("        if (HomeAvailable) RuntimeObject.RequireClasses(\"Enabled\"", StringComparison.Ordinal);
        Check(entryStart >= 0 && entryEnd > entryStart && entryEnd < game.IndexOf("RemainingHpUi.Update(", StringComparison.Ordinal), "Three independent combat settings guard only the all-OFF entry boundary");
        RunGeneralFixture(context, Path.Combine(stage, "combat-entry"), GeneralRemainingHp1.Replace("__ENTRY__", game[entryStart..entryEnd], StringComparison.Ordinal), "CombatSettingEntryProbe.Run()", pin);
        var update = MethodFragment(view, "internal static void Update");
        var renderStart = update.IndexOf("            Ensure(root);", StringComparison.Ordinal); var updateCatch = update.LastIndexOf("        catch (Exception ex)", StringComparison.Ordinal);
        var animateLayout = animate.IndexOf("            var elapsed =", StringComparison.Ordinal); var animateCatch = animate.LastIndexOf("        catch (Exception ex)", StringComparison.Ordinal);
        var fieldsStart = view.IndexOf("    private static RuntimeObject? _point, _parent;", StringComparison.Ordinal); var fieldsEnd = view.IndexOf("    internal static void Update(", StringComparison.Ordinal);
        Check(renderStart > 0 && updateCatch > renderStart && animateLayout > 0 && animateCatch > animateLayout && fieldsStart >= 0 && fieldsEnd > fieldsStart,
            "Capture fixture replaces optional rendering only after the actual public-evidence and cleanup guards");
        var capture = GeneralRemainingHp2.Replace("__FIELDS__", view[fieldsStart..fieldsEnd], StringComparison.Ordinal)
            .Replace("__UPDATE__", update[..renderStart] + "            Rendered++;\n        }\n" + update[updateCatch..], StringComparison.Ordinal)
            .Replace("__ANIMATE__", animate[..animateLayout] + "            return;\n        }\n" + animate[animateCatch..], StringComparison.Ordinal)
            .Replace("__LOG__", MethodFragment(view, "private static void LogModel"), StringComparison.Ordinal)
            .Replace("__SERVER__", WithoutImports(ReadSource(context, "src/ServerFight.cs")), StringComparison.Ordinal)
            .Replace("__RESET__", MethodFragment(view, "internal static void ResetCapture"), StringComparison.Ordinal)
            .Replace("__DISPLAY_CLEAR__", displayClear, StringComparison.Ordinal)
            .Replace("__CLEAR__", Fragment(view, @"(?m)^    internal static void Clear\(\) \{ ClearDisplay\(\); ResetCapture\(\); \}", "scene clear resets display and capture"), StringComparison.Ordinal)
            .Replace("__COMBAT__", WithoutImports(ReadSource(context, "src/CombatAdvisor.cs")), StringComparison.Ordinal);
        RunGeneralFixture(context, Path.Combine(stage, "capture-lifetime"), capture, "RemainingHpUi.Run()", pin);
        Console.WriteLine("Historical five-argument Content assertion superseded by the approved achievable-defense-face input; old raw damage-log strings superseded by current support/reason/status metadata. Public final-number and spectator privacy gates are unchanged.");
    }
}
