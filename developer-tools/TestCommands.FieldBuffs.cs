using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void FieldBuffs(DevContext context, string stage, Dictionary<string, string> options, Action<string> pin)
    {
        using var native = Native(context);
        string Il(string type, string method, int argc) => Body(NativeMethod(native, type, method, argc));
        Check((int)native.MainModule.GetType("CharacterType").Fields.Single(field => field.Name == "Hero").Constant == 1, "Native player character type");
        var factory = Il("UI.UICom_AttrInfo", "CreateInstance", 0); Pattern(factory, "\"Common_Internal\"", "Native monster nameplate package"); Pattern(factory, "\"Com_AttrInfo\"", "Native monster nameplate resource");
        var layout = Il("UI.UIBattleInfoPanel", "UpdateAttrInfoPosition", 0); Pattern(Il("UI.UIBattleInfoPanel", "OnUpdate", 0), "::UpdateAttrInfoPosition", "Native per-frame plate position hook");
        Contains(layout, new[] { "::com_AttrInfos", "::GetOwnerPos", "::WorldToScreenPoint", "::UpdateOffset", "::UpdateGuideLine" }, "Native plate tracking/layout");
        Contains(Il("UI.UICom_AttrInfo", "GetOwnerPos", 0), new[] { "BattleProperty::HP", "::CharacterInst", "::IsHide" }, "Death/hidden player protections");
        // Hero clones live in an owned layer: native layout must only walk direct UICom_AttrInfo children,
        // and the mod reuses its exact lifts and per-plate helpers.
        Pattern(layout, @"::get_Item\(System.Int32\)\nIL_[0-9a-f]+: isinst UI.UICom_AttrInfo", "Native plate pass skips non-plate children");
        Pattern(layout, @"ldc.r4 200\n[\s\S]*ldc.r4 120\n", "Native plate and marker lifts");
        foreach (var name in new[] { "UpdateOffset", "UpdateGuideLine" })
            Check(NativeMethod(native, "UI.UICom_AttrInfo", name, 1).Parameters[0].ParameterType.FullName == "UnityEngine.Vector2", "Native plate helper signature: " + name);
        Pattern(layout, @"::get_canStep\(\)[\s\S]*::graph_ShowBuff", "Native step-phase buff hint gate");
        var publicBuffs = Il("Core.Unit.BuffContainer", "GetShowBuffs", 2); Pattern(publicBuffs, @"::get_IsShow\(", "Public effect visibility"); Pattern(publicBuffs, @"::get_IsShowIconProgressZero\(", "Public zero-progress effect filter");
        var description = Il("UI.UICom_AttrInfo", "OpenBuffInfo", 1); Pattern(description, "::GetShowBuffs", "Native description public buffs"); Pattern(description, "::RefreshMonsterInfo", "Native monster info"); NoPattern(description, "Request|RPC", "Native description no RPC");
        var popupMethod = NativeAsyncMoveNext(native, "UI.BattlePlayerInfoWindow", "RefreshMonsterInfo"); var popup = Body(popupMethod); NoPattern(popup, "Request|RPC", "Popup no RPC"); Pattern(popup, "::RefreshMonsterBuffList", "Native monster buff list");
        foreach (var name in new[] { "UpdateHP", "UpdateATK", "UpdateDEF", "UpdateExtraMovePoint", "SwitchInfo", "Dispose" }) NoPattern(Il("UI.UICom_AttrInfo", name, name == "UpdateHP" ? 2 : name == "Dispose" ? 0 : 1), "Request|RPC|AddListener|Delegate::Combine", "No plate update/dispose side effects: " + name);
        // Native road teardown removes upgrade tips from com_UpgradeTips only, then clears its list, so the zoom layer
        // disposes adopted tips the list no longer holds.
        Pattern(Il("UI.BattleInfoPanel", "RemoveUpgradeTips", 1), @"::com_UpgradeTips\nIL_[0-9a-f]+: ldarg.1\nIL_[0-9a-f]+: ldc.i4.1\nIL_[0-9a-f]+: callvirt [^\n]*GComponent::RemoveChild", "Native upgrade tip removal");
        Pattern(Il("Core.RoadLineManager", "DestroyRoad", 0), @"::RemoveUpgradeTips[\s\S]*::UpgradeTips\nIL_[0-9a-f]+: callvirt [^\n]*UIBattleInfo_Com_UpgradeTips>::Clear", "Native road teardown clears its tip list");
        Contains(ReadSource(context, "src/FieldStatePlates.cs"), new[] { "FindClass(\"Core\", \"RoadLineManager\")", "Field(\"UpgradeTips\")", "GameUi.Dispose(tip)" }, "Adopted upgrade tips follow native removal");
        // Player list tabs also hang under the encounter screen's own Com_PlayerInfo copies, whose data RefreshPlayerInfo fills.
        var launchPk = native.MainModule.GetType("UI.UIFight_LaunchPK");
        Check(Enumerable.Range(1, 4).All(slot => launchPk.Fields.Single(field => field.Name == "com_P" + slot).FieldType.FullName == "FairyGUI.GButton")
            && native.MainModule.GetType("UI.UICom_PlayerInfo").Fields.Single(field => field.Name == "_playerData").FieldType.FullName == "GameLogic.BattlePlayerData", "Encounter screen player bars");
        Pattern(Il("UI.FightWindow", "RefreshPlayerInfo", 0), @"UIFight_LaunchPK::com_P1\nIL_[0-9a-f]+: castclass UI.UICom_PlayerInfo", "Encounter screen bars are Com_PlayerInfo");
        Pattern(Il("UI.FightWindow", "RefreshPlayerInfo", 2), @"UICom_PlayerInfo::RefreshData\(GameLogic.BattlePlayerData\)", "Encounter screen fills its player bars");
        // The coin transfer window lists its own Com_PlayerInfo copies.
        Check(native.MainModule.GetType("UI.UIATMWindow").Fields.Single(field => field.Name == "list_Players").FieldType.FullName == "FairyGUI.GList"
            && native.MainModule.GetType("UI.UIATM_Button_RoleHeadshot").Fields.Single(field => field.Name == "com_playerInfo").FieldType.FullName == "FairyGUI.GButton", "Coin transfer player list");
        Pattern(Body(NativeAsyncMoveNext(native, "UI.ATMWindow", "RendererTargetPlayers")), @"UIATM_Button_RoleHeadshot::com_playerInfo\nIL_[0-9a-f]+: castclass UI.UICom_PlayerInfo[\s\S]*UICom_PlayerInfo::RefreshData", "Coin transfer fills its player bars");
        Contains(ReadSource(context, "src/PlayerTabUi.cs"), new[] { "\"ATMWindow\"", "?.Field(\"list_Players\")", "?.Field(\"com_playerInfo\")" }, "Player list tabs follow the coin transfer copies");
        // Issue #17: a two-digit count wrapped in its narrow label and showed only "1".
        Contains(ReadSource(context, "src/PlayerTabUi.cs"), new[] { "foreach (var label in new[] { tab.Hand, tab.Role, tab.Skill }) { label.Set(\"singleLine\", true); label.Set(\"autoSize\", 3); }" }, "Player list tab count, role and turns stay on one line and shrink");
        // Skill cooldown (draft A5, 2026-10-31): the player info window's own values, its turn colour and its stopwatch.
        Contains(Il("UI.BattlePlayerInfoWindow", "GetSkillInfo", 1), new[] { "RoomPlayer::GetBattleActiveSkillId()", "SkillInfoConfigure::get_Round()", "BattleProperty::activeSkillCD" }, "Native skill cooldown values");
        Contains(Il("UI.UIHelper", "GetSkillConfigs", 3), new[] { "SkillConfigure::get_InfoDict()", "SkillInfoConfigure::get_IsShow()" }, "Native window lists shown skills only");
        Pattern(Il("UI.BattlePlayerInfoWindow", "RefreshActiveSkillCD", 2), @"::SkillType\nIL_[0-9a-f]+: ldc.i4.1\n[\s\S]*""\[color=#7FFD37\]""[\s\S]*UIBattlePlayerInfo_Com_Skill::txt_CD", "Native active skill countdown colour");
        Contains(ReadSource(context, "src/PlayerTabUi.cs"), new[] { "\"[color=#7FFD37]{0}[/color]/{1}\"", "Call(\"GetBattleActiveSkillId\")", "Field(\"activeSkillCD\")?.Get<int>(\"Value\")",
            "!config.Get<bool>(\"IsShow\") || config.Get<int>(\"SkillType\") != 1", "NativeUi.Icon(tab.Panel, \"时间\"" }, "Player list tab turns follow the native window");
        // Other players' turns (user-chosen 2026-11-01): every round start copies the receiver's own skill map onto all
        // characters, so the PvE window asks the server and shows the reply's SkillCD; the tab asks the same way.
        Contains(Body(NativeAsyncMoveNext(native, "GameLogic.BattleLogic", "OnRoundStartS2CServerCallBack")), new[] { "BattleLogic::PlayerDatas", "RoundStartS2C::get_SkillCds()", "Character::UpdateActiveSkillCD" }, "Round start copies one skill map onto every character");
        Contains(Il("UI.BattlePlayerInfoWindow", "RefreshHeroInfo_PVE", 1), new[] { "BattlePlayerInfoWindow::IsVailGetInfo()", "BattleLogic::RequestGetHeroInfoC2S(System.Int64)", "RPCAsyncResult::OnFinishedOnly" }, "Native window asks the server for a player's turns");
        Contains(Body(NativeAsyncMoveNext(native, "GameLogic.BattleLogic", "OnGetHeroInfoS2CServerCallBackAsync")), new[] { "GetHeroInfoS2C::get_Cd()", "BattlePlayerData::SkillCD" }, "Native reply stores SkillCD");
        Pattern(Body(NativeAsyncMoveNext(native, "Core.Net.RPCMsgManager", "ProcessNextPushRPCMsg")), @"RPCMsgManager::DealServerCallback[\s\S]*RPCAsyncResult::Finish\(\)", "A reply's callback runs before it completes");
        var rpc = native.MainModule.GetType("Core.Net.RPCAsyncResult"); string FieldType(TypeDefinition type, string name) => type.Fields.Single(field => field.Name == name).FieldType.FullName;
        Check(FieldType(rpc, "isCompleted") == "System.Boolean" && FieldType(rpc, "isTimeOut") == "System.Boolean" && FieldType(rpc, "errId") == "System.Int32"
            && FieldType(native.MainModule.GetType("GameLogic.BattlePlayerData"), "SkillCD") == "System.Int32"
            && NativeMethod(native, "GameLogic.BattleLogic", "GetPlayerDataById", 1).Parameters[0].ParameterType.FullName == "System.Int64"
            && native.MainModule.GetType("GameLogic.RoomInfo").Methods.Any(method => method.Name == "get_Round")
            && native.MainModule.GetType("GameLogic.Replay.ReplaySession").Methods.Count(method => method.Name is "get_IsReplay" or "get_CanSendC2S") == 2, "Native reply state, stored turns, player lookup, round and send gate");
        var tabSource = ReadSource(context, "src/PlayerTabUi.cs");
        Contains(tabSource, new[] { "Call(\"RequestGetHeroInfoC2S\", id)", "Call(\"GetPlayerDataById\", id)?.Field<int>(\"SkillCD\")", "Get<bool>(\"CanSendC2S\")", "room.Get<int>(\"Round\")", "if (room!.Pointer != _asksRoom) { Asks.Clear(); _asksRoom = room.Pointer; }", "Compatibility.Block(SkillFeature, ex, Asks.Clear)" }, "Player list tab asks the server like the window");
        var skillHarness = Template(context, "focus-tests/csharp-migration/player-tab-skill.template.cs", pin)
            .Replace("__ASK__", Fragment(tabSource, @"(?s)    private sealed class Ask .*?    private readonly record struct Server\(RuntimeObject Battle, long Self, int Round\);", "Player tab server state"), StringComparison.Ordinal)
            .Replace("__ASKED__", MethodFragment(tabSource, "private static int Asked"), StringComparison.Ordinal);
        RunEmbedded(context, stage, "player-tab-skill", skillHarness, "PlayerTabSkillTest.Check.Run()", pin);
        var spam = skillHarness.Replace("if (ask.Reply == null && (ask.Round", "if ((ask.Round", StringComparison.Ordinal); Check(spam != skillHarness, "One request at a time anchor exists");
        RunEmbedded(context, stage, "player-tab-skill-spam-mutant", spam.Replace("PlayerTabSkillTest", "PlayerTabSkillSpam", StringComparison.Ordinal), "PlayerTabSkillSpam.Check.Run()", pin, expectedFailure: "no second request while one is pending");
        // Option 1 narrows the role label beside the skill part: the label hands its new size to its shrinking text, which
        // marks itself for a re-fit on its next redraw.
        Pattern(Il("FairyGUI.GTextField", "HandleSizeChanged", 0), @"ldc.i4.2\nIL_[0-9a-f]+: bne.un.s (IL_[0-9a-f]+)\n[\s\S]*\n\1: ldarg.0\nIL_[0-9a-f]+: call FairyGUI.DisplayObject FairyGUI.GObject::get_displayObject\(\)\n(IL_[0-9a-f]+: [^\n]*\n){4}IL_[0-9a-f]+: callvirt System.Void FairyGUI.DisplayObject::SetSize\(System.Single,System.Single\)", "Shrinking label passes a new size to its text");
        Pattern(Il("FairyGUI.TextField", "OnSizeChanged", 0), @"TextField::_autoSize\nIL_[0-9a-f]+: ldc.i4.3\nIL_[0-9a-f]+: beq.s (IL_[0-9a-f]+)\n[\s\S]*\n\1: ldarg.0\nIL_[0-9a-f]+: ldc.i4.1\nIL_[0-9a-f]+: stfld System.Boolean FairyGUI.TextField::_textChanged", "Shrinking text re-fits after a size change");
        Contains(ReadSource(context, "src/PlayerTabUi.cs"), new[] { "tab.Role.Call(\"SetSize\", RolePart(skill) - Inset * 2, Height);" }, "Player list tab role label follows its part's width");
        Contains(ReadSource(context, "src/PlayerTabUi.cs"), new[] { "Color.Lerp(roleFormat.Field<Color>(\"color\"), Color.white, RoleLighten)", "roleFormat.SetField(\"outlineColor\", Color.black);", "roleFormat.SetField(\"bold\", true);", "roleFormat.SetField(\"outline\", RoleStroke);", "RoleLighten = 0.6f, RoleStroke = 1.6f" },
            "Player list tab role: player colour lightened 60 % with a black outline (option C)");
        var common = Path.Combine(context.Root, ".research/ui-assets/Common_fui.bytes"); pin(common);
        Check(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(common)).Contains("时间", StringComparison.Ordinal), "Native stopwatch art (Common/时间)");
        // Picking a player moves the selectable buttons out of com_Container into com_Empty.
        Pattern(Il("UI.UIBattleInfo_Com_Player", "AdjustPlayerParent", 2), @"UIBattleInfo_Com_Player::com_Empty\nIL_[0-9a-f]+: ldarg.1\nIL_[0-9a-f]+: callvirt [^\n]*GComponent::AddChild", "Player selection moves bars into com_Empty");
        Contains(ReadSource(context, "src/PlayerTabUi.cs"), new[] { "if (container != null)", "?.Field(\"com_LaunchPK\")", "launch!.Field(\"com_P\" + slot)", "owner?.Field(\"_playerData\")" }, "Player list tabs follow the encounter screen copies");
        var source = ReadSource(context, "src/FieldBuffUi.cs"); var list = Il("UI.BattlePlayerInfoWindow", "RefreshMonsterBuffList", 3);
        Contains(source, new[] { "Call(\"UpdateOffset\", target)", "Call(\"UpdateGuideLine\", point)", "PlateLift = 200f, MarkerLift = 120f", "FieldZoomUi.IndicatorScale", "Plugin.FieldIndicatorHeight.Value" }, "Owned hero plate layout");
        // Monster plate zoom-follow: native plates move into an owned layer the native pass skips; a hidden owned
        // direct child keeps receiving the native long-press SwitchMonsterInfo state.
        Pattern(Il("UI.UIBattleInfoPanel", "SwitchMonsterInfo", 1), @"::com_AttrInfos\n[\s\S]*::_children[\s\S]*isinst UI.UICom_AttrInfo[\s\S]*::SwitchInfo\(System.Boolean\)", "Native long-press info walks direct plate children");
        Contains(Il("UI.UICom_AttrInfo", "SwitchInfo", 1), new[] { "::ShowInfo", "set_selectedIndex" }, "Native plate info state is the ShowInfo controller");
        Pattern(Il("UI.UICom_AttrInfo", "get_PlayerData", 0), @"::_PlayerId\n[\s\S]*::GetPlayerDataById\(System.Int64\)", "Plate owner resolves from _PlayerId");
        Check(native.MainModule.GetType("UI.UICom_AttrInfo").Fields.Single(field => field.Name == "_PlayerId").FieldType.FullName == "System.Int64", "Native plate owner id is Int64");
        var monsters = ReadSource(context, "src/FieldMonsterPlates.cs");
        Contains(monsters, new[] { "PlateLift = 200f, MarkerLift = 120f", "Call(\"UpdateOffset\", target)", "Call(\"UpdateGuideLine\", point)", "Math.Abs(scale - 1f) > 0.01f", "_parent!.Call(\"AddChild\", ui)", "ui.Call(\"SwitchInfo\", info == 1)" }, "Monster plates follow zoom and return to com_AttrInfos");
        NoPattern(monsters, "FieldIndicatorHeight", "Monster dot height stays native");
        Contains(ReadSource(context, "src/FieldZoomUi.cs"), new[] { "FieldMonsterPlates.Tick(IndicatorScale);", "Attempt(FieldMonsterPlates.Clear);" }, "Zoom drives and clears the monster plate layer");
        // Settings marker slider: exact native GSlider members the mod writes (value/max are properties, changeOnClick a field).
        var slider = native.MainModule.GetType("FairyGUI.GSlider");
        foreach (var name in new[] { "set_max", "set_value" })
            Check(slider.Methods.Single(method => method.Name == name).Parameters.Single().ParameterType.FullName == "System.Double", "Native slider setter ABI: " + name);
        Check(slider.Methods.Single(method => method.Name == "get_value").ReturnType.FullName == "System.Double", "Native slider value getter ABI");
        Check(slider.Fields.Single(field => field.Name == "changeOnClick") is { IsPublic: true, IsStatic: false } click && click.FieldType.FullName == "System.Boolean"
            && !slider.Methods.Any(method => method.Name == "set_changeOnClick"), "Native slider changeOnClick is a public field");
        Check(slider.Fields.Single(field => field.Name == "_barMaxWidth") is { IsStatic: false } barWidth && barWidth.FieldType.FullName == "System.Single", "Native slider bar width field");
        Check(slider.Fields.Single(field => field.Name == "_gripObject") is { IsStatic: false } gripField && gripField.FieldType.FullName == "FairyGUI.GObject", "Native slider grip field");
        // Default markers are translucent copies of the star grip where the grip would sit at the default, under the real grip.
        Check(slider.Fields.Single(field => field.Name == "_barObjectH").FieldType.FullName == "FairyGUI.GObject", "Native slider bar field");
        var markSource = ReadSource(context, "src/ModUi.cs");
        Contains(markSource, new[] { "NativeUi.Create(\"Setting\", \"Setting_Com_Slider_grip\")", "SliderEntry(action).DefaultValue", "mark.Set(\"alpha\", DefaultMarkAlpha);",
            "grip.Get<float>(\"x\") - bar.Get<float>(\"width\") + ratio * slider.Field<float>(\"_barMaxWidth\"), grip.Get<float>(\"y\")",
            "slider.Call(\"AddChildAt\", mark, slider.Call(\"GetChildIndex\", grip)!.Value<int>());" }, "Settings sliders mark their default with a ghost grip");
        Check(markSource.IndexOf("slider.Set(\"value\", (double)(SliderValue(action) - min));", StringComparison.Ordinal)
            < markSource.IndexOf("AddDefaultMark(slider, action, min, max);", StringComparison.Ordinal), "Default marker is placed after the slider shows its value");
        // Drags follow the pointer 1:1 and snap to the default nearby; the native grip drag stays off meanwhile.
        Check(slider.Fields.Single(field => field.Name == "canDrag").FieldType.FullName == "System.Boolean", "Native slider canDrag field");
        Pattern(Il("FairyGUI.GSlider", "__gripTouchBegin", 1), @"ldc.i4.1\nIL_[0-9a-f]+: stfld System.Boolean FairyGUI.GSlider::canDrag", "Native grip press re-enables its drag");
        Pattern(Il("FairyGUI.GSlider", "__gripTouchMove", 1), @"^IL_[0-9a-f]+: ldarg.0\nIL_[0-9a-f]+: ldfld System.Boolean FairyGUI.GSlider::canDrag\nIL_[0-9a-f]+: brtrue", "Native grip drag stops while canDrag is off");
        Contains(markSource, new[] { "slider.SetField(\"canDrag\", false);", "var step = range / pixels;", "var next = _dragStart + (TouchX() - _dragX) * step;", "Math.Abs(next - fallback) <= DefaultSnapPixels * step ? fallback : Math.Round(next)" }, "Slider drags keep the grip under the pointer and snap to the default");
        Contains(markSource, new[] { "NudgeSlider(slider, max - min);", "Input.GetKeyDown(left ? KeyCode.LeftArrow : KeyCode.RightArrow)", "GameUi.PointerPath(hitTest: true).All(item => item.Pointer != slider.Pointer && item.Pointer != area)", "SliderAreas[action] = area;" }, "Arrow keys move the hovered slider one step, anywhere on its row");
        Pattern(Il("FairyGUI.GSlider", "__barTouchBegin", 1), @"::_gripObject[\s\S]*::GlobalToLocal[\s\S]*::_barMaxWidth", "Native bar press measures from the grip over the bar width");
        // A new slider must hold the saved value before anything reads it back, or the setting is overwritten with the minimum.
        var sliderSource = ReadSource(context, "src/ModUi.cs");
        var reset = sliderSource.IndexOf("slider.Set(\"value\", -1d);", StringComparison.Ordinal);
        var load = sliderSource.IndexOf("slider.Set(\"value\", (double)(SliderValue(action) - min));", reset + 1, StringComparison.Ordinal);
        var poll = sliderSource.IndexOf("var value = min + (int)Math.Round(Math.Clamp(slider.Get<double>(\"value\")", StringComparison.Ordinal);
        Check(reset >= 0 && load > reset && load < sliderSource.IndexOf("Sliders[action] = slider;", reset, StringComparison.Ordinal) && poll > load
            && sliderSource.IndexOf("return; // Read user input back only from the next frame", load, StringComparison.Ordinal) is var skip && skip > load && skip < poll,
            "New settings sliders load the saved value before any read-back");
        Contains(ReadSource(context, "src/ModUi.cs"), new[] { "slider.SetField(\"changeOnClick\", true);", "slider.Set(\"max\", (double)(max - min));", "slider.Get<double>(\"value\")" }, "Settings sliders use native member kinds");
        Pattern(list, "::set_numItems", "Native full-data list");
        Pattern(list, @"::buffs[\s\S]*::get_Count\(\)[\s\S]*::propertyBuffs[\s\S]*::get_Count\(\)\nIL_[0-9a-f]+: add[\s\S]*::buffCount[\s\S]*::buffCount[\s\S]*::set_numItems", "Native list retains every public buff and property");
        var nativeResize = native.MainModule.GetType("UI.BattlePlayerInfoWindow").Methods.Where(method => method.Name == "ResizeAndPlaceMonsterInfo").ToArray();
        if (nativeResize.Length == 0) Pattern(list, @"ldc.i4.4[\s\S]*::Clamp[\s\S]*::ResizeToFit", "Historical native four-item viewport");
        else {
            Check(nativeResize.Length == 1 && nativeResize[0].HasBody && nativeResize[0].IsStatic && nativeResize[0].FullName == "System.Void UI.BattlePlayerInfoWindow::ResizeAndPlaceMonsterInfo(UI.UICom_BuffInfo,FairyGUI.GObject,System.Nullable`1<System.Boolean>)", "Exact native adaptive popup geometry signature");
            var calls = Calls(popupMethod); var resize = Array.IndexOf(calls, nativeResize[0].FullName); var fill = Array.FindIndex(calls, call => call.Contains("::RefreshMonsterBuffList(", StringComparison.Ordinal));
            Check(fill >= 0 && resize > fill, "Native popup fills its complete data before adaptive geometry");
            var geometry = Body(nativeResize[0]);
            Check((int)native.MainModule.GetType("FairyGUI.ListLayoutType").Fields.Single(field => field.Name == "FlowVertical").Constant == 3, "Native vertical-flow layout enum");
            Pattern(geometry, @"ldc.i4.3\nIL_[0-9a-f]+: callvirt System.Void FairyGUI.GList::set_layout", "Native adaptive popup uses its vertical-flow layout");
            Pattern(geometry, @"ldc.i4.4\nIL_[0-9a-f]+: callvirt System.Void FairyGUI.GList::set_lineCount", "Native adaptive popup keeps four lines per column");
            Pattern(geometry, @"::get_numChildren\(\)[\s\S]*Mathf::Min[\s\S]*::GetChildAt[\s\S]*::get_width[\s\S]*Mathf::Max[\s\S]*::get_height[\s\S]*::get_numChildren\(\)\nIL_[0-9a-f]+: blt", "Native adaptive popup measures every child rather than truncating its data");
            Contains(geometry, new[] { "::sourceWidth", "::sourceHeight", "::get_lineGap", "::get_columnGap", "::SetSize", "::LocalToRoot", "::RootToLocal", "::Clamp", "::SetXY" }, "Native adaptive popup owns authored sizing and bounded root/local placement");
        }
        NoPattern(MethodFragment(source, "private static void UpdateBuffPopup"), "ResizeToFit|SetSize|SetXY|set_layout|set_lineCount", "Mod popup input repair preserves native geometry in both layout generations");
        Contains(source, new[] { "UpdateBuffPopup();", "RestoreBuffPopup();", "GameUi.Visible(panel)", "list.Get(\"scrollPane\")", "list.Set(\"touchable\", true)", "scroll.Set(\"mouseWheelEnabled\", true)", "_popupList.Set(\"touchable\", _popupTouchable)", "_popupScroll!.Set(\"mouseWheelEnabled\", _popupWheel)" }, "Buff popup scroll and ownership restoration");
        foreach (var name in new[] { "get_mouseWheelEnabled", "set_mouseWheelEnabled" }) Check(native.MainModule.GetType("FairyGUI.ScrollPane").Methods.Any(method => method.Name == name), "Native scroll API: " + name);
        foreach (var name in new[] { "txt_Name", "txt_HP", "graph_Line", "graph_Guide", "graph_Sign" }) Check(native.MainModule.GetType("UI.UICom_AttrInfo").Fields.Any(field => field.Name == name) && source.Contains("\"" + name + "\"", StringComparison.Ordinal), "Ally color field: " + name);
        Contains(source, new[] { "new Color(0.35f, 0.8f, 1f)", "Ui.Field(field)!.Set(\"color\", allyColor)" }, "Owned ally sky-blue style");
        Contains(source, new[] {
            "!Plugin.FieldBuffs.Value",
            "VisibleCombatAdvisor.IsPve",
            "Field(\"fightStatus\")",
            "Get<int>(\"characterType\") != 1",
            "Field(\"CharacterInst\")",
            "Field(\"com_AttrInfos\")",
            "StaticCall(_plateClass, \"CreateInstance\")",
            "SetField(\"_PlayerId\", roomPlayer.Get<long>(\"Id\"))",
            "parent.Call(\"AddChild\", Ui)",
            "\"GetShowBuffs\", player, false",
            "\"RefreshProperty\", data, false",
            "Time.unscaledTime + 0.2f",
            "if (_effects[i] == state) continue",
            "if (_stats != stats)",
            "List.Set(\"numItems\", 0)",
            "Plates.Remove(key)",
            "GameUi.Dispose(plate.Ui)",
            "Plates.Clear()",
            "hit.Call(\"OpenBuffInfo\", (object?)null)",
        }, "Public read/native UI/cache/lifetime guards");
        var nickname = Il("GameLogic.RoomPlayer", "GetNick", 1); Pattern(nickname, "IncoverMode", "Native display-name privacy"); Pattern(nickname, "GetDisplayNick", "Native display-name mapping"); Contains(source, new[] { "Call(\"GetNick\", false)" }, "Native displayed player nickname");
        Check(!source.Contains("Count\") == 0) continue", StringComparison.Ordinal), "Players without public buffs retain name/HP plate");
        NoPattern(source, "GetCharacterName|new Material|renderQueue\\s*=|\"_ZTest\"|StyleWorldGraphics|SortingGroup|_world|ScreenToWorldPoint|DelegateSupport|Il2CppSystem.Action|beforeUpdate|PositionAboveHeads|NativeUi.OutlineTree", "No unsafe custom positioning/native callback/world renderer");
        var baselinePath = Path.Combine(context.Root, "artifacts/backup-before-0.4.30/BetterAstralParty.dll"); var builtPath = options.TryGetValue("plugin-path", out var selected) ? Path.GetFullPath(selected) : Path.Combine(context.Root, "artifacts/BetterAstralParty.dll"); pin(baselinePath); pin(builtPath);
        using (var baseline = AssemblyDefinition.ReadAssembly(baselinePath)) using (var built = AssemblyDefinition.ReadAssembly(builtPath)) {
            var old = baseline.MainModule.GetTypes().Single(type => type.FullName == "BetterAstralParty.FieldBuffUi/Plate").Methods.Single(method => method.Name == "Refresh");
            var current = built.MainModule.GetTypes().Single(type => type.FullName == "BetterAstralParty.FieldBuffUi/Plate").Methods.Single(method => method.Name == "Refresh"); Check(Body(old) == Body(current), "Initial field plate renderer bytecode preserved");
        }
        Check(!source.Contains("Clear(\"unfocused\")", StringComparison.Ordinal) && source.Contains("if (!Application.isFocused) { _pressed = IntPtr.Zero; return; }", StringComparison.Ordinal) && source.IndexOf("Scan();", StringComparison.Ordinal) <= source.IndexOf("if (!Application.isFocused)", StringComparison.Ordinal), "Focus loss preserves plates and cancels click only");
        foreach (var reason in new[] { "setting-off", "settings-open", "fight-active", "parent-hidden", "parent-changed", "not-pve", "players-unavailable", "exception" }) Check(source.Contains("Clear(\"" + reason + "\")", StringComparison.Ordinal), "Plate hide diagnostic: " + reason);
        Contains(source, new[] { "if (!Plugin.Diagnostics.IsRecording || Time.unscaledTime < _diagnoseAt) return;", "_diagnoseAt = Time.unscaledTime + 1f;", "ownerMatches=", "ownerZero=", "animatorHidden=", "attached=", "if (state == _diagnosticState) return;", "Error(\"FieldBuff.inspect\", ex)" }, "Isolated deduplicated plate diagnostics");
        NoPattern(source, "Call\\(\"(ShowAttrInfo|RendererBuff|RefreshBuff)\"\\s*\\)|Call\\(\"ShowAttrInfo\"|HandCards|RequestGetHeroInfo|SetField\\(\"Com_AttrInfo\"|SetField\\(\"UpdateAction\"", "No native subscription/private data/existing object mutation");
        Contains(ReadSource(context, "src/ModUi.cs"), new[] { "FieldBuffUi.Tick();", "Plugin.FieldBuffs.Value = !Plugin.FieldBuffs.Value", "\"FieldBuffs\" => Plugin.FieldBuffs.Value" }, "Independent field indicator setting/tick");
        FieldInput(context, stage, pin);
    }
}
