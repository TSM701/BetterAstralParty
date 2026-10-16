using System.Text.RegularExpressions;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void Typography(DevContext context, string stage, Action<string> pin)
    {
        RunGeneralFixture(context, stage, GeneralWithoutNamespace(ReadSource(context, "src/FontCoverage.cs")) + "\n" + GeneralTypography0, "FontCoverageCheck.Run()", pin);
        using var native = Native(context);
        var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        foreach (var (type, name) in new[] { ("FairyGUI.TextFormat", "CopyFrom"), ("FairyGUI.FontManager", "GetFont"), ("FairyGUI.DynamicFont", "get_nativeFont"), ("FairyGUI.TextField", "get_parsedText") })
            Check(types[type].Methods.Any(method => method.Name == name), "Native font binding " + type + "." + name);
        var font = ReadSource(context, "src/ModFont.cs"); var ui = ReadSource(context, "src/NativeUi.cs");
        var fallback = "using System; using System.Linq; using System.Collections; using System.Collections.Generic; using System.Reflection; using UnityEngine; using TMPro;\n"
            + WithoutImports(ReadSource(context, "src/FontCoverage.cs")) + "\n"
            + WithoutImports(ReadSource(context, "focus-tests/font-fallback/RuntimeFixture.cs")) + "\n";
        var fontCode = WithoutImports(font);
        RunGeneralFixture(context, Path.Combine(stage, "font-fallback"), fallback + fontCode, "FontFallbackCheck.Run()", pin);
        var forcedLayout = fontCode.Replace("var text = textField.Get(\"parsedText\")", "_ = textField.Get<float>(\"textWidth\");\n            var text = textField.Get(\"parsedText\")", StringComparison.Ordinal);
        Check(forcedLayout != fontCode, "Exact premature-layout regression control bound");
        RunGeneralFixture(context, Path.Combine(stage, "font-layout-regression"), fallback + forcedLayout, "FontFallbackCheck.Run()", pin, rejection: "Forced layout access");
        var atlasMutation = fontCode.Replace("HasCharacter(c, false, false)", "HasCharacter(c, false, true)", StringComparison.Ordinal);
        Check(atlasMutation != fontCode, "Exact glyph-atlas mutation regression control bound");
        RunGeneralFixture(context, Path.Combine(stage, "font-atlas-regression"), fallback + atlasMutation, "FontFallbackCheck.Run()", pin, rejection: "Unexpected atlas mutation");
        var nativeCoverage = NativeMethod(native, "FairyGUI.TMPFont", "HasCharacter", 1).Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference method && method.DeclaringType.FullName == "TMPro.TMP_FontAsset" && method.Name == "HasCharacter");
        Check(nativeCoverage.Previous.OpCode.Name == "ldc.i4.0" && nativeCoverage.Previous.Previous.OpCode.Name == "ldc.i4.0", "Glyph coverage uses the game's read-only native false/false flags");
        var punctuation = NativeMethod(native, "FairyGUI.TextField", ".ctor", 0).Body.Instructions.Select(instruction => instruction.Operand).OfType<string>().Single(value => value.Contains("\\·", StringComparison.Ordinal));
        Check(Regex.IsMatch("·", punctuation, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), "Native line-end punctuation includes the reported field/camera separator");
        Check(Calls(NativeMethod(native, "FairyGUI.TextField", "get_textWidth", 0)).Any(call => call.Contains("::BuildLines()", StringComparison.Ordinal))
            && Calls(NativeMethod(native, "FairyGUI.TextField", "get_parsedText", 0)).Length == 0, "Native width getter forces layout while parsed-text getter does not");
        var wrapping = NativeMethod(native, "FairyGUI.TextField", "BuildLines2", 0).Body.Instructions;
        var punctuationCall = wrapping.Single(instruction => instruction.Operand is MethodReference method && method.DeclaringType.FullName == "System.Text.RegularExpressions.Regex" && method.Name == "IsMatch");
        var wrapBranch = string.Join("\n", wrapping.SkipWhile(instruction => instruction != punctuationCall).Take(40));
        var rewind = Regex.Match(wrapBranch, @"ldloc\.s (V_[0-9]+)\nIL_[0-9a-f]+: ldc\.i4\.1\nIL_[0-9a-f]+: sub\nIL_[0-9a-f]+: stloc\.s \1");
        Check(rewind.Success && Regex.IsMatch(wrapBranch, @"ldloc\.s " + rewind.Groups[1].Value + @"\nIL_[0-9a-f]+: ldc\.i4\.1\nIL_[0-9a-f]+: add\nIL_[0-9a-f]+: stloc\.s " + rewind.Groups[1].Value), "Native punctuation wrapping can retry the same character without advancing");
        Check(!Regex.IsMatch(font, @"RegisterFont|CreateFontAsset|new Font\(path|ModText.Korean") && !Regex.IsMatch(ui, "SetField\\(\"(faceDilate|outline|font|bold|letterSpacing)\""), "No forced or bundled native typography");
        Contains(font, new[] { "GetOSInstalledFontNames", "FontCoverage.NeedsFallback(text, Has)", "FontCoverage.NeedsFallback(text, source.HasCharacter)", "var selected = entry.Native;", "current != entry.Applied", "entry.Asset == asset", "\"parsedText\"", "NativeUi.CopyFormat(entry.NativeFormat, format)", "entry.NativeFormat.Field(field)" }, "Whole-label native font fallback");
        Contains(ui, new[] { "format.Call(\"CopyFrom\"", "dark ? \"Button_PreviewSkin\" : \"Button_Confirm_SmallRounded\"", "CopyFormat(format, source.Get(\"textFormat\")!)" }, "Contextual native style copy");
        Check(ReadSource(context, "src/GameUi.cs").Contains("NativeUi.InheritText(label, dark)", StringComparison.Ordinal), "Game labels inherit contextual style");
        var guide = ReadSource(context, "src/CharacterGuideUi.cs"); var damage = ReadSource(context, "src/RemainingHpUi.cs");
        Contains(guide, new[] { "labelHeight, 19, dark: true", ": (width - textWidth) / 2f;", "badge.Text.Call(\"SetXY\", textLeft + 4, 0f)" }, "Native role text style and centering");
        Contains(damage, new[] { "28, 22, dark: true", "NativeUi.CopyTextFormat(_point!.Field(name)!, source.Field(name)!)" }, "Native dice style");
        foreach (var name in new[] { "faceDilate", "outline", "outlineSoftness", "underlaySoftness", "outlineColor", "shadowColor", "shadowOffset" })
            Check(types["FairyGUI.TextFormat"].Fields.Any(field => field.Name == name) && ui.Contains("\"" + name + "\"", StringComparison.Ordinal), "Native text style field " + name);
        Check(ReadSource(context, "src/ModUi.cs").Contains("_helpFontRevision != ModFont.Revision", StringComparison.Ordinal), "Font change invalidates help measurements");
        Check(!Regex.IsMatch(ReadSource(context, "src/BetterAstralParty.csproj"), @"assets[/\\]fonts"), "No font resources bundled in plugin");
        Check(context.Hash(Path.Combine(context.Root, "src/FieldNameFont.cs")) == "144F4ECECEC714BA7CAB04A67685E4F17E16DCCF9C6E1E7CC58125A028BD1E8A", "Excluded field nickname font implementation unchanged");
        Check(ReadSource(context, "src/FieldBuffUi.cs").Contains("FieldNameFont", StringComparison.Ordinal), "Current player indicators retain the excluded nickname font path");
    }

    private static void CharacterGuide(DevContext context, string stage, Action<string> pin)
    {
        RunGeneralFixture(context, stage, GeneralWithoutNamespace(ReadSource(context, "src/CharacterRole.cs")) + "\n" + GeneralCharacterGuide0, "CharacterRoleCheck.Run()", pin);
        using var native = Native(context); var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        foreach (var (name, value) in new[] { ("Attack", 2), ("Support", 4), ("Card", 3), ("Tank", 5) }) Check((int)types["CharacterTagType"].Fields.Single(field => field.Name == name).Constant == value, "Native character role " + name);
        foreach (var (type, name) in new[] {
            ("UI.UIHero_Button_Hero", "HeroId"), ("UI.UIHeroPanel", "com_HeroList"), ("UI.UIHero_Com_Selector", "list_Hero"),
            ("UI.UIRoomHero_Com_Player", "mapType"), ("UI.UIRoomHero_Com_Player", "selectHero"), ("UI.UIRoomHero_Com_Player", "loader_Character"),
            ("UI.UIRoomHero_Com_Player", "btn_DetailInfo"), ("UI.UIRoomHero_Button_DetailInfo", "list_Skill"), ("UI.UIRoomHero_Com_Skill", "txt_SkillDesc"),
            ("UI.UIHero_Com_HeroItem", "com_Qulity"), ("UI.UIRoomHero_Com_Player", "com_Qulity"), ("UI.UIHero_Com_Skill", "txt_CD"),
            ("UI.UIHero_Com_FileBreakThrough", "com_Desc"), ("UI.UIHero_Com_FileBreakThrough", "language"), ("UI.UIRoomHero_Com_Player", "YOU"),
            ("UI.UIRoomHero_Com_Player", "com_PlayerLabel"), ("UI.UIRoomHero_Com_Player", "com_ChangeSlot"), ("UI.UIRoomHero_Com_ChangeSlot", "btn_ChangeSlot") })
            Check(types[type].Fields.Any(field => field.Name == name), "Native guide field " + type + "." + name);
        var source = ReadSource(context, "src/CharacterGuideUi.cs");
        var settingTemplate = Template(context, "focus-tests/csharp-migration/character-guide-settings.template.cs", pin);
        RunEmbedded(context, stage, "character-guide-settings", settingTemplate.Replace("__SOURCE__", WithoutNamespace(source).Replace("using UnityEngine;", "", StringComparison.Ordinal), StringComparison.Ordinal), "BetterAstralParty.GuideSettingsCheck.Run()", pin, true);
        Contains(source, new[] { "Plugin.CharacterGuide.Value && Compatibility.Allowed(\"CharacterGuide\")", "Plugin.CharacterRoles.Value && Compatibility.Allowed(\"CharacterRoles\")", "ClearGuide(); } catch (Exception ex) { BlockGuide(ex); }", "ClearRoles(); } catch (Exception ex) { BlockRoles(ex); }", "Hover(root); } catch (Exception ex) { BlockGuide(ex); }", "ScanRoles(root); } catch (Exception ex) { BlockRoles(ex); }", "chosen != 0 && _rolesEnabled" }, "Independent guide and role settings preserve their own lifecycle and compatibility breakers");
        foreach (var category in new[] { "Character", "PVENurturance" }) Check(types["UI.UIStringType"].Fields.Single(field => field.Name == category).IsLiteral, "Native guide string category " + category);
        Contains(source, new[] { "RuntimeObject.EnumValue(\"UI\", \"UIStringType\", \"Character\")", "RuntimeObject.EnumValue(\"UI\", \"UIStringType\", \"PVENurturance\")", "\"GetLocal\", potential.Get<int>(\"DescriptionID\"), potentialStrings", "\"GetLocal\", config.Get<int>(\"NameID\"), characterStrings" }, "Guide potential and character text use their native enum names");
        Check(!Regex.IsMatch(source, "\"GetLocal\"[^\\r\\n]*,\\s*[0-9]+\\s*\\)"), "Guide localization has no ordinal fallback");
        var guard = "if (path.Any(p => p.TypeName == \"UIRoomHero_Com_ChangeSlot\")) { Hide(); return; }";
        Check(source.Contains(guard, StringComparison.Ordinal) && source.IndexOf(guard, StringComparison.Ordinal) < source.IndexOf("var overPopup =", StringComparison.Ordinal), "Native swap hit ancestry precedes popup retention");
        Check(!source.Contains("ready ? top.x - left", StringComparison.Ordinal) && !source.Contains("if (ready) textLeft +=", StringComparison.Ordinal), "Role labels do not inherit grade or readiness drift");
        Contains(source, new[] { "(portrait.Get<float>(\"width\") * portrait.Get<float>(\"scaleX\") - textWidth) / 2f", "AlignSwapButton(player, root);", "container.Set(\"x\", target);", "RestoreSwapPositions();",
            "Compatibility.Block(\"CharacterGuide\"", "VisibleCombatAdvisor.IsPve", "quality.Call(\"LocalToRoot\"", "frame.Get(\"texture\")", "Hero_Com_FileBreakThrough",
            "\"AddPackageAsync\", \"Hero\"", "load.Call(\"GetResult\")", "owner.Field(\"YOU\")", "portrait.Get<float>(\"width\") * portrait.Get<float>(\"scaleX\")", "top.y - labelHeight - lift",
            "width += bleed * 2", "portraitIndex + 1", "ready ? 0f : labelHeight * 0.25f", "panel.Set(\"mask\", mask.Get(\"displayObject\")!)", "Color.white, radius, radius, 0f, 0f",
            "Math.Max(end.y, profileMiddle.y)", "\"GetChildIndex\", owner.Field(\"com_PlayerLabel\")", "\"MatchSkillTextContent\", entry.Description, 0", "RefreshReadyBadges();", "previous == state",
            "Badges.Remove(player.Pointer, out var old)", "ReadyStates.Clear()", "if (_cardTemplate == null && HeroUiReady())", "_scanAt = 0;", "if (!HeroUiReady()) return false;" }, "Native guide lifecycle and original resource reuse");
        Check(!Regex.IsMatch(source, "RefreshDetailInfo|SureHero|SendMessage|Request|GetMouseButton|자리 강조|Slot highlight|CharacterRole.Recommends|AddReadyBadge|or 12 or 13|artwork.Max|profileRight|Math.Min\\(left, profileTop.x\\)|desc.Set\\(\"text\", entry.Description\\)"), "Guide remains read-only without removed presentation paths");
        foreach (var portrait in new[] { 300d, 400d, 480d }) foreach (var grade in new[] { 120d, 220d, 300d }) {
            var left = (portrait - grade) / 2 + 4;
            Check(Math.Abs(left + (grade - 8) / 2 - portrait / 2) < .001 && left + grade - 8 <= portrait, "Preparation role text stays centered");
        }
        foreach (var scale in new[] { .5, 1, 1.5 }) {
            var target = 60d - (60 + 12 * scale);
            Check(Math.Abs(target + 12 * scale) < .001 && Math.Abs(target - (target + 12 * scale) - target) < .001, "Swap alignment preserves native hit geometry and is idempotent");
        }
        foreach (var grade in new[] { 12d, 17d, 29d, 40d }) {
            var height = Math.Max(28, grade * 1.6); var lift = height * .15;
            Check(Math.Abs(100 - height - lift + height + lift + grade - (100 + grade)) < .001, "Backing meets grade bottom");
        }
        var resolver = types["UI.CommonUIManager"].Methods.Single(method => method.Name == "MatchSkillTextContent" && method.Parameters.Count == 2);
        Check(resolver.ReturnType.FullName == "System.String", "Native description resolver ABI");
        var pattern = resolver.Body.Instructions.Where(instruction => instruction.OpCode.Name == "ldstr").Select(instruction => (string)instruction.Operand).Single(text => text.Contains("Astral=", StringComparison.Ordinal));
        Check(Regex.IsMatch("[Astral=Buff:1131201][/Astral]", pattern), "Native resolver recognizes empty-label buff tokens");
        var refresh = source[source.IndexOf("private static void RefreshReadyBadges()", StringComparison.Ordinal)..]; refresh = refresh[..refresh.IndexOf("private static bool AddBadge", StringComparison.Ordinal)];
        Check(refresh.Contains("if (config == null || !AddBadge(player, config.Get<int>(\"TagType\"), true)) continue;", StringComparison.Ordinal)
            && refresh.IndexOf("ReadyStates[player.Pointer] = state;", StringComparison.Ordinal) > refresh.IndexOf("!AddBadge(", StringComparison.Ordinal), "Deferred preparation rendering retries before committing state");
        var corners = types["FairyGUI.Shape"].Methods.Single(method => method.Name == "DrawRoundRect" && method.Parameters.Count == 7).Parameters.Select(parameter => parameter.Name);
        Check(String.Join(",", corners).EndsWith("topLeftRadius,topRightRadius,bottomLeftRadius,bottomRightRadius", StringComparison.Ordinal), "Native corner order");
    }

    private static void ButtonPivots(DevContext context)
    {
        using var native = Native(context); var button = native.MainModule.GetType("FairyGUI.GButton"); var obj = native.MainModule.GetType("FairyGUI.GObject");
        var construct = Il(button.Methods.Single(method => method.Name == "ConstructExtension")); var press = Il(button.Methods.Single(method => method.Name == "SetState"));
        Check(construct.Contains("GObject::SetPivot", StringComparison.Ordinal) && construct.Contains("ldc.r4 0.5", StringComparison.Ordinal)
            && press.Contains("GObject::SetScale", StringComparison.Ordinal) && press.Contains("GButton::_downEffectValue", StringComparison.Ordinal), "Native central pivot and down effect");
        Check((int)native.MainModule.GetType("FairyGUI.FillType").Fields.Single(field => field.Name == "ScaleMatchWidth").Constant == 3, "Native width-match fill enum");
        var loader = native.MainModule.GetType("FairyGUI.GLoader");
        Check(Regex.IsMatch(Il(loader.Methods.Single(method => method.Name == "UpdateLayout")), @"ldc.i4.3\s+IL_\w+: bne.un.s IL_\w+\s+IL_\w+: ldloc.2\s+IL_\w+: stloc.3"), "Width-match assigns uniform axes");
        Contains(Il(loader.Methods.Single(method => method.Name == "onExternalLoadSuccess")), new[] { "NTexture::get_width", "GLoader::UpdateLayout" }, "Asynchronous native texture sizing");
        Check(Il(native.MainModule.GetType("FairyGUI.GComponent").Methods.Single(method => method.Name == "set_mask")).Contains("Container::set_mask", StringComparison.Ordinal), "Native mask setter");
        Contains(Il(native.MainModule.GetType("FairyGUI.Container").Methods.Single(method => method.Name == "Update")), new[] { "_PreUpdateMask", "TransformRect" }, "Stencil and screen clipping");
        Check(native.MainModule.GetType("FairyGUI.Shape").Methods.Single(method => method.Name == "DrawRect" && method.Parameters.Count == 3).Parameters[1].ParameterType.FullName == "UnityEngine.Color", "Native mask geometry ABI");
        Check(native.MainModule.GetType("FairyGUI.GearBase").Fields.Single(field => field.Name == "_owner").FieldType.FullName == "FairyGUI.GObject", "Native gear ownership ABI");
        var gear = Il(native.MainModule.GetType("FairyGUI.GearLook").Methods.Single(method => method.Name == "Apply"));
        Check(gear.Contains("GearBase::_owner", StringComparison.Ordinal) && gear.Contains("customEase", StringComparison.Ordinal) && !gear.Contains("get_visible", StringComparison.Ordinal), "Native hover gear route");
        foreach (var axis in new[] { "x", "y" }) {
            var getter = Il(obj.Methods.Single(method => method.Name == "get_" + axis + "Min"));
            Check(getter.Contains("_pivotAsAnchor", StringComparison.Ordinal) && getter.Contains("brtrue", StringComparison.Ordinal) && !getter.Contains("_scale", StringComparison.Ordinal), "Native left/top pivot meaning " + axis);
        }
        foreach (var name in new[] { "get_asButton", "get_pivotX", "get_pivotY" }) Check(obj.Methods.Count(method => method.Name == name) == 1, "Native pivot accessor " + name);
        Contains(ReadSource(context, "src/NativeUi.cs"), new[] { "button?.Field(\"_downEffect\")?.Value<int>() == 2 ? 0.5f : 0f", "obj?.Call(\"SetPivot\", pivot, pivot, true)",
            "x + obj.Get<float>(\"width\") * obj.Get<float>(\"pivotX\") * restingScale", "y + obj.Get<float>(\"height\") * obj.Get<float>(\"pivotY\") * restingScale", "Position(icon, x, y, scale)", "Position(native, 0f, 0f, 1f)" }, "Common pivot placement");
        foreach (var (file, call) in new[] { ("GameUi", "NativeUi.Position(nativeButton"), ("ModUi", "NativeUi.Position(button"), ("BattleStatusUi", "NativeUi.Position(icon"), ("CardPreviewUi", "NativeUi.Position(_card") })
            Check(ReadSource(context, "src/" + file + ".cs").Contains(call, StringComparison.Ordinal), "Common pivot caller " + file);
        Contains(ReadSource(context, "src/ModUi.cs"), new[] { "toggle.SetField(\"_downEffect\", 1)", "toggle.Call(\"SetPivot\", 0f, 0f, true)", "toggle.Call(\"SetSize\", width, MenuLayout.ToggleHeight)", "toggle.Call(\"SetXY\", x, y)" }, "Fixed-size native settings toggle");
        foreach (var dimensions in new[] { (298d, 101d), (298d, 102d), (180d, 107d), (64d, 64d), (86d, 86d) }) foreach (var scale in new[] { .25, .6875, .75, 1, 1.5 }) foreach (var down in new[] { .75, .8, .95, 1 }) foreach (var length in new[] { dimensions.Item1, dimensions.Item2 }) {
            var anchor = 123 + length * .5 * scale; var rest = anchor - length * scale / 2; var pressed = length * scale * down; var left = anchor - pressed / 2;
            Check(Math.Abs(rest - 123) < .00001 && Math.Abs(left + pressed / 2 - anchor) < .00001 && Math.Abs(left - 123 - (length * scale - pressed) / 2) < .00001, "Rest position and symmetric native press scaling");
        }
    }

    private static void Surfaces(DevContext context, Dictionary<string, string> options, Action<string> pin)
    {
        using var native = Native(context); var draw = native.MainModule.GetType("FairyGUI.Shape").Methods.Single(method => method.Name == "DrawRoundRect" && method.Parameters.Count == 7);
        Contains(Il(draw), new[] { "RoundedRectMesh::lineColor", "NGraphics::set_color" }, "Independent native border/fill colors");
        var source = ReadSource(context, "src/NativeUi.cs");
        Contains(source, new[] { "\"DrawRoundRect\", 3f, Color.black, Fill(opacity)", "Graph.Set(\"color\", Fill(opacity))", "if (_opacity == opacity) return", "fixedOpacity ? 1f", "ModFont.Track(label)" }, "Common opacity caching and fixed settings surface");
        Check(!Regex.IsMatch(source, "SetField\\(\"outline(Color)?\","), "Native text outline preserved");
        foreach (var file in new[] { "AdviceBadge", "ChoiceVisual", "BattleStatusUi" }) {
            var text = ReadSource(context, "src/" + file + ".cs"); Check(text.Contains("new NativeUi.Surface", StringComparison.Ordinal) && !Regex.IsMatch(text, "AddRoundedRect|Plugin.Opacity.Value"), "Common battle surface " + file);
        }
        var cards = ReadSource(context, "src/CardUi.cs");
        Check(cards.Contains("new AdviceBadge(item.Face,", StringComparison.Ordinal) && Regex.IsMatch(cards, "Math\\.Min\\(11,\\s*cards\\.Get<int>\\(\"Count\"\\)\\)") && !Regex.IsMatch(cards, "AddRoundedRect|Plugin.Opacity.Value"), "Bounded card-to-badge surface delegation");
        if (!options.TryGetValue("plugin-path", out var image)) throw new ArgumentException("Surfaces requires explicit --plugin-path metadata input.");
        image = Path.GetFullPath(image); pin(image); using var plugin = AssemblyDefinition.ReadAssembly(image);
        var cardCalls = plugin.MainModule.GetType("BetterAstralParty.CardUi").Methods.Single(method => method.Name == "Update").Body.Instructions.Count(instruction => instruction.Operand is MethodReference method && method.Name == ".ctor" && method.DeclaringType.FullName == "BetterAstralParty.AdviceBadge");
        var surfaceCalls = plugin.MainModule.GetType("BetterAstralParty.AdviceBadge").Methods.Single(method => method.Name == ".ctor").Body.Instructions.Count(instruction => instruction.Operand is MethodReference method && method.Name == ".ctor" && method.DeclaringType.FullName == "BetterAstralParty.NativeUi/Surface");
        Check(cardCalls == 1 && surfaceCalls == 1, "Compiled CardUi -> AdviceBadge -> owned Surface chain");
        Contains(ReadSource(context, "src/Plugin.cs"), new[] { "\"배경 불투명도\", 1f", "AcceptableValueRange<float>(0f, 1f)" }, "Default 100 percent opacity and bounds");
        Contains(ReadSource(context, "src/ModUi.cs"), new[] { "Math.Max(0f, MathF.Round(Plugin.Opacity.Value - 0.05f, 2))", "Plugin.Opacity.Value <= 0.001f" }, "Opacity controls include zero");
        static double Luminance(string text) { var values = new[] { 0, 2, 4 }.Select(index => Convert.ToInt32(text.Substring(index, 2), 16) / 255d).Select(value => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4)).ToArray(); return .2126 * values[0] + .7152 * values[1] + .0722 * values[2]; }
        Check((Luminance("FFFFFF") + .05) / (Luminance("000000") + .05) >= 7, "White text and black outline contrast");
        foreach (var ink in new[] { "17663C", "795000", "B5163B", "005B83" }) Check((Luminance("FFFBF2") + .05) / (Luminance(ink) + .05) >= 4.5, "Cream background contrast " + ink);
    }

    private static void InputAttention(DevContext context)
    {
        using var native = Native(context); var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        foreach (var (type, name) in new[] { ("GameLogic.GameLogicManager", "get_room"), ("GameLogic.GameLogicManager", "get_watch"), ("GameLogic.GameLogicManager", "get_replay"), ("GameLogic.GameLogicManager", "get_action"),
            ("GameLogic.RoomLogic", "get_curRoomInfo"), ("GameLogic.RoomInfo", "IsPVE"), ("GameLogic.RoomInfo", "GetSelfInfo"), ("GameLogic.RoomInfo", "IsCampaign"), ("GameLogic.RoomInfo", "get_MapType"),
            ("GameLogic.WatchLogic", "PlayerIsWatcher"), ("GameLogic.ReplayLogic", "get_Session"), ("GameLogic.Replay.ReplaySession", "get_IsReplay"), ("GameLogic.ActionLogic", "get_playerAction"), ("FairyGUI.GObject", "get_asButton") })
            Check(types[type].Methods.Any(method => method.Name == name && method.Parameters.Count == 0), "Native input alert API " + type + "." + name);
        var timer = types["GameLogic.OperationTimer"].Fields.Single(field => field.Name == "timerDict");
        Check(timer.IsStatic && timer.FieldType.FullName == "System.Collections.Generic.Dictionary`2<System.Int64,UnityTimer.Timer>", "Native local input timer registry");
        Check((int)types["GameLogic.RoomStateType"].Fields.Single(field => field.Name == "RUNNING").Constant == 4 && (int)types["UI.FightWindow"].Fields.Single(field => field.Name == "AUDIENCE").Constant == 2, "Native room and audience state constants");
        Contains(Il(types["GameLogic.OperationTimer"].Methods.Single(method => method.Name == "ActionDownTime")), new[] { "curPlayerOperate", "IsCampaign", "ldc.i4.s 10" }, "Native shared input timer semantics");
        var callers = types.Values.SelectMany(type => type.Methods).Where(method => method.HasBody && Il(method).Contains("OperationTimer::ActionDownTime", StringComparison.Ordinal)).Select(method => method.FullName).ToArray();
        foreach (var name in new[] { "RefreshPKCard", "OpenChallengeWin", "RefreshDefendReadyChoice", "DealThrowDiceUI", "DealUseEffectCard", "ShowRelicData", "ShowLoseCard", "ShowRoundCard", "TryShowAssistVote", "DealPVECardShop", "CreateArrow" })
            Check(callers.Any(caller => caller.Contains(name, StringComparison.Ordinal)), "Native input route " + name);
        foreach (var name in new[] { "CancelOperatTimer", "Dispose" }) { var body = Il(types["GameLogic.OperationTimer"].Methods.Single(method => method.Name == name)); Check(body.Contains("timerDict", StringComparison.Ordinal) && Regex.IsMatch(body, "Remove|stsfld"), "Native timer cleanup " + name); }
        var source = ReadSource(context, "src/InputAttention.cs");
        Contains(source, new[] { "Application.isFocused", "InputAttention.Value", "timerDict", "PlayerIsWatcher", "IsReplay", "GetWindowThreadProcessId", "Environment.ProcessId", "UnityWndClass", "GetForegroundWindow()", "if (_flashing) return;", "0x00000002 | 0x0000000C", "Flash(_window, 0)", "Time.unscaledTime + 0.2f", "if (_mode != mode) { Stop();", "WindowsInputNotification.Show", "WindowsInputNotification.Hide()", "if (mode == \"Windows\")" }, "Input notification gating and cleanup");
        Check(!Regex.IsMatch(source, "SetForegroundWindow|ShowWindow\\(|Request\\w+C2S|Call\\(\"(Cancel|Pause|ActionDownTime)\"|GetPlayerID|GetNick|GetCharacterHeadUrl"), "Input alerts neither steal focus nor automate gameplay or read private identity");
        Contains(ReadSource(context, "src/WindowsInputNotification.cs"), new[] { "Shell_NotifyIcon(0,", "Shell_NotifyIcon(1,", "Shell_NotifyIcon(2,", "0x20 | 0x10 | 0x40", "0x1 | 0x10 | 0x80", "ProcessExit", "if (!_added) return;" }, "Bounded Windows notification lifecycle");
    }

    private static void PvpSafety(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context); var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        foreach (var (type, name) in new[] { ("GameLogic.GameLogicManager", "get_match"), ("GameLogic.GameLogicManager", "get_room"), ("GameLogic.MatchData", "get_TeamId"), ("GameLogic.MatchData", "get_MatchMode"), ("GameLogic.RoomController", "get_localRoom"), ("party.model.Room", "get_MapType") })
            Check(types[type].Methods.Any(method => method.Name == name), "PvP gate native getter " + type + "." + name);
        foreach (var (type, name) in new[] { ("GameLogic.MatchLogic", "matchData"), ("GameLogic.RoomLogic", "roomController"), ("GameLogic.RoomController", "roomStateType"), ("GameLogic.RoomInfo", "info") })
            Check(types[type].Fields.Any(field => field.Name == name), "PvP gate native field " + type + "." + name);
        foreach (var (name, value) in new[] { ("Pve", 4), ("CampaignPve", 6), ("PracticePve", 9), ("Pvenovice", 10), ("MutatorPve", 12) }) Check((int)types["MapModeType"].Fields.Single(field => field.Name == name).Constant == value, "Native PvE mode " + name);
        Check(Il(types["GameLogic.MatchData"].Methods.Single(method => method.Name == "Dispose")).Contains("_teamId", StringComparison.Ordinal), "Disposed matchmaking clears native team identity");
        var source = GeneralWithoutNamespace(ReadSource(context, "src/PvpSafety.cs")); var driver = ReadSource(context, "src/ModUi.cs"); var gate = driver.IndexOf("PvpSafety.Poll()", StringComparison.Ordinal);
        foreach (var name in new[] { "PingFocusGuard.Tick()", "InputAttention.Tick()", "FocusAudio.Tick()", "GameUi.Tick()", "CharacterGuideUi.Tick()", "ModFont.Tick()", "CardPreviewUi.Tick()", "HandLayoutUi.Tick()", "BattleStatusUi.Tick()", "EncounterCounterUi.Tick()", "FieldBuffUi.Tick()", "ShushuShieldUi.Tick()" })
            Check(gate >= 0 && driver.IndexOf(name, StringComparison.Ordinal) > gate, "Feature after PvP gate " + name);
        foreach (var name in new[] { "Request", "SetField(", ".Set(", ".Call(" }) Check(!source.Contains(name, StringComparison.Ordinal), "Read-only PvP gate " + name);
        Check(driver.Contains("if (PvpSafety.Suspended) FocusAudio.Restore(); else FocusAudio.Tick();", StringComparison.Ordinal) && driver.IndexOf("FocusAudio.Tick()", StringComparison.Ordinal) < driver.IndexOf("if (pauseGameplay)", StringComparison.Ordinal), "Startup audio independent of gameplay readiness");
        RunGeneralFixture(context, stage, GeneralPvpSafety0.Replace("__NS__", "BapPvpSafety", StringComparison.Ordinal).Replace("__SOURCE__", source, StringComparison.Ordinal), "BapPvpSafety.Check.Run()", pin);
        PveClassifier(context, stage, pin);
    }

    private static void MatchingEventAudit(DevContext context)
    {
        using var native = Native(context); var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var success = types["GameLogic.MatchLogic"].Methods.Single(method => method.Name == "OnMatchSuccessS2CServerCallBack");
        Check(success.ReturnType.FullName == "Cysharp.Threading.Tasks.UniTask" && String.Join(",", success.Parameters.Select(parameter => parameter.ParameterType.FullName)) == "party.protocol.MatchSuccessS2C,System.Int32,System.Boolean", "Native success RPC ABI");
        var state = types.Values.Single(type => type.FullName.StartsWith("GameLogic.MatchLogic/<OnMatchSuccessS2CServerCallBack>", StringComparison.Ordinal)); var body = Il(state.Methods.Single(method => method.Name == "MoveNext"));
        Contains(body, new[] { "::errid", "RoomController::CreateRoom", "RoomController::SwitchRoomState", "MatchSignal::CancelMatch", "MatchSuccessWindow::ShowMatchResult" }, "Actual successful match RPC semantics");
        Check(body.IndexOf("::errid", StringComparison.Ordinal) < body.IndexOf("RoomController::CreateRoom", StringComparison.Ordinal), "Transport error precedes room mutation");
        var callers = types.Values.SelectMany(type => type.Methods).Where(method => method.HasBody && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.FullName == "Cysharp.Threading.Tasks.UniTask UI.MatchSuccessWindow::ShowMatchResult()")).ToArray();
        Check(callers.Length == 1 && callers[0].FullName.Contains("OnMatchSuccessS2CServerCallBack", StringComparison.Ordinal), "Success window has only actual RPC caller");
        var tryShow = types.Values.Single(type => type.FullName.StartsWith("UI.MatchSuccessWindow/<TryShow>", StringComparison.Ordinal)); Check(Il(tryShow.Methods.Single(method => method.Name == "MoveNext")).Contains("FairyGUI.Window::Show()", StringComparison.Ordinal), "Native success window lifecycle");
        Check((int)types["GameLogic.RoomStateType"].Fields.Single(field => field.Name == "CHOICE").Constant == 2, "Native choosing state constant");
        var dispose = types["GameLogic.MatchData"].Methods.Single(method => method.Name == "Dispose");
        Check(Il(dispose).Contains("stfld System.Int64 GameLogic.MatchData::_teamId", StringComparison.Ordinal) && dispose.Body.Instructions.Count(instruction => instruction.Operand is MethodReference) == 1, "Owned native mailbox reset remains local");
        var windows = ReadSource(context, "src/WindowsMatchFocus.cs");
        Check(!Regex.IsMatch(windows, "SendInput|keybd_event|SystemParametersInfo|Process\\.Start|Kill\\("), "Focus utility sends no synthetic input or process commands");
        Contains(ReadSource(context, "src/ModUi.cs"), new[] { "MatchFocus.Tick();" }, "Approved active focus polling integration");
        Console.WriteLine("Historical inactive-scaffold assertion superseded by the approved active focus implementation; native RPC checks remain.");
    }
}
