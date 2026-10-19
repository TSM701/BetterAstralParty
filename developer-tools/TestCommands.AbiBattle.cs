using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void BattleStatus(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context); var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        foreach (var (type, name, count) in new[] {
            ("Core.Unit.BuffContainer", "GetShowBuffs", 2), ("Core.CardContainer", "get_CardCount", 0), ("UI.UIButton_Buff", "RefreshBuff", 1), ("UI.UIButton_Buff", "RefreshProperty", 2),
            ("party.model.Buff", "get_BuffId", 0), ("party.model.Buff", "get_Progress", 0), ("party.model.Buff", "get_KeepRound", 0), ("UI.UIHelper", "GetLocal", 2),
            ("UI.CommonUIManager", "RefreshHyperlinkDesc", 3), ("FairyGUI.GObject", "LocalToRoot", 2), ("FairyGUI.GObject", "RootToLocal", 2), ("FairyGUI.Stage", "get_inst", 0),
            ("FairyGUI.Stage", "get_touchPosition", 0), ("FairyGUI.DisplayObject", "GlobalToLocal", 1), ("FairyGUI.GRoot", "GetTopWindow", 0), ("FairyGUI.GRoot", "get_modalWaiting", 0),
            ("FairyGUI.GRoot", "get_hasAnyPopup", 0), ("FairyGUI.Window", "get_modal", 0), ("FairyGUI.DisplayObject", "LocalToWorld", 1),
            ("UI.FightWindow", "get_attackerData", 0), ("UI.FightWindow", "get_defenderData", 0), ("GameLogic.BattlePlayerData", "get_Property", 0), ("GameLogic.BattlePlayerData", "get_characterType", 0) })
            Check(types[type].Methods.Count(method => method.Name == name && method.Parameters.Count == count) == 1, "Native battle status method " + type + "." + name);
        foreach (var (type, name) in new[] {
            ("UI.UIFight_PlayerInfo", "attackerData"), ("UI.UIFight_PlayerInfo", "defenderData"), ("GameLogic.FightLogic", "fightStatus"), ("UI.UIButton_Buff", "BuffConfig"), ("FairyGUI.GObject", "data"),
            ("Core.PropertyData`1", "buffId"), ("Core.PropertyData`1", "property"), ("UI.UICom_BuffInfoItem", "loader_Buff"), ("UI.UICom_BuffInfoItem", "txt_Title"), ("UI.UICom_BuffInfoItem", "txt_Desc"),
            ("BattleShowDirector", "PKCamera"), ("Core.Unit.PKCameraManager", "PKMainCamera"), ("UI.UIFight_PlayerInfo", "com_Attack"), ("UI.UIFight_PlayerInfo", "com_Defend"),
            ("UI.UIFight_Com_Attacker", "txt_Life"), ("UI.UIFight_Com_Defenser", "txt_Life"), ("Core.Unit.BattleProperty", "Counter"), ("Core.Unit.BattleProperty", "HP"), ("Core.Unit.BattleProperty", "ATK"), ("Core.Unit.BattleProperty", "DEF"),
            ("UI.UIFightWindow", "com_Show"), ("UI.UIFightWindow", "com_LaunchPK"), ("UI.UIFight_LaunchPK", "btn_PK"), ("UI.UIFight_LaunchPK", "btn_Leave"), ("UI.UIFight_LaunchPK", "txt_HP"),
            ("UI.UIFight_LaunchPK", "txt_ATK"), ("UI.UIFight_LaunchPK", "txt_DEF"), ("UI.UIFight_LaunchPK", "com_Counter"), ("FairyGUI.Transition", "_items"), ("FairyGUI.TransitionItem", "targetId"),
            ("UI.UIFight_Com_Point", "aMovie_Dice"), ("UI.UIFight_Com_Point", "txt_Point"), ("UI.UIFight_Com_Point", "txt_max") })
            Check(types[type].Fields.Any(field => field.Name == name), "Native battle status field " + type + "." + name);
        var filter = Il(types["Core.Unit.BuffContainer"].Methods.Single(method => method.Name == "GetShowBuffs")); Contains(filter, new[] { "get_IsShow(", "get_IsShowIconProgressZero(" }, "Native public buff filter");
        var reads = types["UI.UIButton_Buff"].Methods.Single(method => method.Name == "RefreshBuff").Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().Where(method => method.DeclaringType.FullName == "party.model.Buff");
        foreach (var read in reads) Check(new[] { "get_BuffId", "get_Progress", "get_KeepRound" }.Contains(read.Name, StringComparer.Ordinal), "Buff cache covers every live native renderer dependency " + read.Name);
        var source = ReadSource(context, "src/BattleStatusUi.cs");
        Check(types["UI.UIStringType"].Fields.Single(field => field.Name == "Buff").IsLiteral, "Native buff string category is a named enum literal");
        Check(source.Contains("RuntimeObject.EnumValue(\"UI\", \"UIStringType\", \"Buff\")", StringComparison.Ordinal)
            && !Regex.IsMatch(source, "\"GetLocal\"[^\\r\\n]*,\\s*[0-9]+\\s*\\)"), "Public buff descriptions use the native enum name without ordinal fallback");
        Contains(source, new[] { "root.Call(\"GetTopWindow\")", "topWindow.Pointer != _fight?.Pointer", "root.Get<bool>(\"modalWaiting\")", "root.Get<bool>(\"hasAnyPopup\")", "if (!blocked)",
            "topWindow.Get<bool>(\"modal\") || GameUi.PointerPath().Any(hit => hit.Pointer == topWindow.Pointer)", "!row.Visible || !GameUi.Visible(row.Panel) || !ContainsPoint(row.Panel, point)",
            "item.Get<bool>(\"visible\") && ContainsPoint(item, point)", "item.Get(\"displayObject\")!.Call(\"GlobalToLocal\", point)", ".Get<Vector2>(\"touchPosition\")", "battle.buffHover",
            "\"GetShowBuffs\", player, false", "\"RefreshProperty\", data, false", "\"CardCount\"", "\"PKMainCamera\"", "NativeUi.ProjectBattlePoint(_ui!, _hpDisplay, _hpEdge, _camera)",
            "Field(\"txt_Life\")", "BattleStatusLayout.BelowHp", "_background.Resize(", "foreach (var row in Rows) { row.UpdateInputOrder(step); row.Position(); }", "Panel.Set(\"sortingOrder\", order)",
            "BattleStatusLayout.RowInputOrder(step)", "internal bool NeedsData => !_hasData || _hpDisplay == null;", "!_hasData || _camera == null || !_camera.enabled || _hpDisplay == null || !GameUi.Visible(_hp)",
            "if (anchor == null) { SetVisible(false); return; }", "if (position == null) { SetVisible(false); return; }", "parent.Call(\"SetChildIndex\", Panel, parent.Call(\"GetChildIndex\", parent.Field(\"com_Show\")!)!.Value<int>())",
            "battle status create begin; step={step}", "Field(\"fightStatus\")?.Value<bool>() != true", "!VisibleCombatAdvisor.IsPve(map)", "if (!GameUi.Visible(_fight)) { ClearRows(); return; }", "actorUi.Get<bool>(\"isDisposed\")",
            "NativeUi.CounterIndicator(parent!)", "actorUi.Field(\"com_Defend\")", "Call(\"GetChild\", \"n24\")", "GameUi.Dispose(_counterIcon)", "BattleStatusLayout.CounterReady(visible, Time.unscaledTime, ref _counterVisibleSince)",
            "_counterVisibleSince = -1;", "PositionCounter();", "GameUi.Visible(_counterHeader)", "Get<float>(\"alpha\") > 0", "Call(\"Stop\", false, false)", "if (!_counterShown)", "NativeUi.Play(_counterIcon, \"MaxPoint\")", "BattleStatusLayout.CounterAnchor" }, "Public status, native-coordinate hover and owned counter lifecycle");
        var hover = Derive(source, @"(?s)private static void Hover\(\).*?(?=    private static bool ContainsPoint)", "Public buff hover boundary");
        var hit = Derive(source, @"(?s)private static bool ContainsPoint\(.*?(?=    private static string Local)", "Native display-space bounds boundary");
        Check(!Regex.IsMatch(hover, "Input.mousePosition|Screen.width|Screen.height|ReadyMaskHover|maskHit|step\\s*(==|is|[<>])|GetLocalPlayer|cardItemList") && !Regex.IsMatch(hit, "RootToLocal|item.Call\\(\"GlobalToLocal\""), "Public hover independent of fight role, phase, native hit targets or screen ratios");
        Check(!source.Contains("mask.Set(\"touchable\"", StringComparison.Ordinal) && !source.Contains("mask.Set(\"visible\"", StringComparison.Ordinal)
            && !Regex.IsMatch(source, "HandCards|RequestGetHeroInfo|TryShowHeroInfo|GetBuffDict|_buffDict|ActorUi.Call\\(\"LocalToRoot\"|actorRenderer|SpriteRenderer|AboveHead|Actor.Get\\(\"transform\""), "Status never changes native masks, requests private state or anchors to actor art");
        var projection = Derive(ReadSource(context, "src/NativeUi.cs"), @"(?s)    internal static Vector2\? ProjectBattlePoint\(.*?\r?\n    \}", "Shared HP projection boundary");
        Contains(projection, new[] { "WorldToViewportPoint", "\"RootToLocal\"", "Call(\"LocalToWorld\", local)" }, "Actual HP world-to-viewport projection");
        var refreshInfo = types["UI.FightWindow"].Methods.Single(method => method.Name == "RefreshPlayerInfo" && method.Parameters.Count == 0); Contains(Il(refreshInfo), new[] { "BattleProperty::Counter", "UIFight_LaunchPK::com_Counter" }, "Native public counter display");
        var tick = Derive(source, @"(?s)internal static void Tick\(\).*?(?=    private static void Scan)", "Public status tick boundary");
        var scan = Derive(source, @"(?s)private static void Scan\(.*?(?=    private static void UpdateCounter)", "Public status scan boundary");
        var refresh = Derive(source, @"(?s)private static void RefreshCounter\(.*?(?=    private static void PositionCounter)", "Public counter preparation boundary");
        var clear = Derive(source, @"(?s)private static void ClearRows\(\).*", "Public row cleanup boundary");
        var counterAt = tick.IndexOf("UpdateCounter(director?.Get(\"victim\")?.Field(\"_UI\"));", StringComparison.Ordinal);
        Check(counterAt >= 0 && tick.IndexOf("if (Rows.Count != 2 || Rows[0].NeedsData || Rows[1].NeedsData || Time.unscaledTime >= _scanAt)", StringComparison.Ordinal) > counterAt, "Isolated counter preparation precedes slower scans and choice-window gating");
        Check(!Regex.IsMatch(scan, "GameUi.Visible\\((atk|def)|Counter|\\bClear\\(\\)") && !clear.Contains("ClearCounter", StringComparison.Ordinal) && !Regex.IsMatch(refresh, "GameUi.Visible|GameUi.Find|selectedIndex|_scanAt"), "Public row refresh independent of counter timing and cleanup");
        var init = Il(types["UI.UIFight_PlayerInfo"].Methods.Single(method => method.Name == "InitState")); Check(init.IndexOf("::defenderData", StringComparison.Ordinal) >= 0 && init.IndexOf("::defenderData", StringComparison.Ordinal) < init.IndexOf("::ShowUp", StringComparison.Ordinal), "Defender public data ready before native DEFENSE entrance");
        foreach (var (name, value) in new[] { ("ActiveBattlePlatform", "ldc.i4.1"), ("CloseBattlePlatform", "ldc.i4.0") }) Check(Regex.IsMatch(Il(types["BattleShowDirector"].Methods.Single(method => method.Name == name)), value + @"\s*\r?\n[^\r\n]*stfld System.Boolean GameLogic.FightLogic::fightStatus"), "Native public fight state " + name);
        Contains(ReadSource(context, "src/NativeUi.cs"), new[] { "Create(\"Fight\", \"Fight_Com_Point\")", "Icon(image, \"Com_Icon_Counter\"", "Set(\"playing\", false)", "item.SetField(\"targetId\"", "transition.Field(\"_items\")", "image.Call(\"SetPivot\", 0.5f, 0.5f, true)" }, "Owned MaxPoint animation reuse");
        var effect = ReadSource(context, "src/EncounterCounterUi.cs");
        Contains(effect, new[] { "!Plugin.BattleStatus.Value", "VisibleCombatAdvisor.IsPve", "selectedIndex\") != 1", "!GameUi.Visible(anchor)", "characterType\") != 2", "Field(\"Counter\")?.Get<bool>(\"Value\") != true",
            "_defender == defender.Pointer", "NativeUi.CounterIndicator(anchor!)", "NativeUi.Play(_effect, \"MaxPoint\")", "GameUi.Dispose(_effect)" }, "Encounter counter availability, once-only animation and cleanup");
        Check(!Regex.IsMatch(effect, "Plugin.Enabled|anchor[!]?\\.(Call|Set)\\("), "Encounter counter independent of advice and preserves native objects");
        Check(ReadSource(context, "src/ModUi.cs").Contains("EncounterCounterUi.Tick();", StringComparison.Ordinal), "Encounter counter runs every frame");
        var game = ReadSource(context, "src/GameUi.cs");
        Contains(game, new[] { "if (step == 1)", "selectedIndex\") != 0", "!leave!.Get<bool>(\"touchable\")", "leave.Get<bool>(\"grayed\")", "Visible(launch.Field(\"txt_HP\"))", "if (_encounterInput != input)", "EnsureBadges(attack, leave", "EncounterAdvisor.Calculate(input)" }, "Encounter selection gating and recommendation cache");
        Check(!Regex.IsMatch(game, "Visible\\(launch.Field\\(\"group_Attr\"\\)\\)"), "Non-rendering native GGroup never used as an on-stage gate");
        Check(ReadSource(context, "src/RuntimeObject.cs").Contains("case UnityEngine.Vector3 point:", StringComparison.Ordinal)
            && types["FairyGUI.DisplayObject"].Methods.Single(method => method.Name == "LocalToWorld").Parameters[0].ParameterType.FullName == "UnityEngine.Vector3", "Actual HP world-coordinate Vector3 ABI");
        CounterPreferences(context, stage, pin, types);
        CounterIsolation(context, stage, pin);
    }
}
