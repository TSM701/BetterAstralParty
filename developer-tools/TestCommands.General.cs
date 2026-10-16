using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mono.Cecil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static readonly string[] GeneralSupported = {
        "typography", "relic-background", "character-guide", "character-names", "probability-ui", "ko-minimum",
        "encounter-recommendation", "fight-readiness", "runtime-arguments", "runtime-getters", "button-pivots", "surfaces",
        "input-attention", "pvp-safety", "matching-event-audit", "auto-thanks", "shushu-shield", "ping-focus", "ping-popups", "battle-status", "remaining-hp-projection", "startup-notice"
    };

    private static bool TryGeneral(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        switch (name) {
            case "typography": Typography(context, stage, pin); break;
            case "relic-background": RelicBackground(context, stage, pin); break;
            case "character-guide": CharacterGuide(context, stage, pin); break;
            case "character-names": CharacterNames(context, stage, pin); break;
            case "fight-readiness": FightReadiness(context, stage, pin); break;
            case "runtime-arguments": RuntimeArguments(context, stage, pin); break;
            case "runtime-getters": RuntimeGetters(context, stage, pin); break;
            case "probability-ui": ProbabilityUi(context, stage, pin); break;
            case "encounter-recommendation": EncounterRecommendation(context, stage, pin); break;
            case "ko-minimum": KoMinimum(context, stage, pin); break;
            case "button-pivots": ButtonPivots(context); break;
            case "surfaces": Surfaces(context, options, pin); break;
            case "input-attention": InputAttention(context); break;
            case "pvp-safety": PvpSafety(context, stage, pin); break;
            case "matching-event-audit": MatchingEventAudit(context); break;
            case "auto-thanks": AutoThanks(context, stage, pin); break;
            case "shushu-shield": ShushuShield(context, stage, pin); break;
            case "ping-focus": PingFocus(context, stage, pin); break;
            case "ping-popups": PingPopups(context, stage, pin); break;
            case "battle-status": BattleStatus(context, stage, pin); break;
            case "remaining-hp-projection": RemainingHpProjection(context, stage, pin); break;
            case "startup-notice": StartupNotice(context, options, pin); break;
            default: return false;
        }
        return true;
    }

    private static string Derive(string source, string pattern, string reason)
    {
        var match = Regex.Match(source, pattern);
        Check(match.Success && match.Length != 0, reason);
        return match.Value;
    }

    private static string GeneralWithoutNamespace(string source) => source.Replace("namespace BetterAstralParty;", "", StringComparison.Ordinal);
    private static string WithoutImports(string source) => Regex.Replace(GeneralWithoutNamespace(source), @"(?m)^using [^;]+;\r?\n", "");

    private static void RunGeneralFixture(DevContext context, string stage, string code, string invoke, Action<string> pin,
        string? rejection = null, bool allowUnsafe = false)
    {
        Directory.CreateDirectory(stage);
        var source = Path.Combine(stage, "GeneralFixture.cs");
        var project = Path.Combine(stage, "GeneralFixture.csproj");
        File.WriteAllText(source, "#nullable enable\n#pragma warning disable CS8618, CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS0649, CS0169, CS0414, CS9113\n"
            + code + "\ninternal static class GeneralFixtureEntry { private static int Main() { try { " + invoke + "; return 0; } catch (System.Exception error) { System.Console.Error.WriteLine(error); return 1; } } }\n", new UTF8Encoding(false));
        var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net8.0"),
                new XElement("EnableDefaultCompileItems", "false"), new XElement("AssemblyName", "GeneralFixture"),
                new XElement("StartupObject", "GeneralFixtureEntry"), new XElement("AllowUnsafeBlocks", allowUnsafe ? "true" : "false")),
            new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", source))));
        using (var file = new FileStream(project, FileMode.CreateNew, FileAccess.Write, FileShare.None)) xml.Save(file);
        pin(source); pin(project);
        var image = BuildProject(context, project, stage, pin);
        var log = Path.Combine(stage, "runtime.log");
        var exit = context.Run(Dotnet(context), new[] { image }, env: EnvironmentFor(stage), logPath: log);
        if (rejection == null) Ensure(exit, "Production-source focused fixture");
        else Check(exit != 0 && File.ReadAllText(log).Contains(rejection, StringComparison.Ordinal), "Regression control rejected for the expected reason: " + rejection);
        context.WriteJsonCreateNew(Path.Combine(stage, "derived-source.json"), new { derivedSourceSha256 = context.Hash(source), invocation = invoke,
            nativeSubstitutesOnly = true, expectedRejection = rejection, sourceFilesUnmodified = true });
    }

    private static void RuntimeArguments(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/RuntimeObject.cs");
        var check = Derive(source, @"(?s)    private static void CheckArgument\(.*?\r?\n    \}", "Production argument checker boundary");
        var invoke = Derive(source, @"(?s)    private static RuntimeObject\? Invoke\(.*?\r?\n    \}", "Production native invocation boundary");
        string Code(string argument, string invocation) => GeneralRuntimeArguments0.Replace("__NAMESPACE__", "BapArgumentFixture", StringComparison.Ordinal)
            .Replace("__CHECK_ARGUMENT__", argument, StringComparison.Ordinal).Replace("__INVOKE__", invocation, StringComparison.Ordinal);
        RunGeneralFixture(context, Path.Combine(stage, "current"), Code(check, invoke), "BapArgumentFixture.RuntimeObject.Run()", pin, allowUnsafe: true);
        var old = Regex.Replace(check, "case string when.*?: return;", "case string when IL2CPP.il2cpp_type_get_name_(type) == \"System.String\": return;", RegexOptions.Singleline);
        Check(old != check, "Object-string regression anchor exists");
        RunGeneralFixture(context, Path.Combine(stage, "old-object-string"), Code(old, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsObjectString()) throw new System.Exception(\"Old rejection not reproduced\")", pin, allowUnsafe: true);
        var absentCheck = Regex.Replace(check, @"case UnityEngine\.Quaternion:.*?return;", "", RegexOptions.Singleline);
        var absentInvoke = Regex.Replace(invoke, @"case UnityEngine\.Quaternion rotation:.*?break;", "", RegexOptions.Singleline);
        Check(absentCheck != check && absentInvoke != invoke, "Quaternion guard and marshaling regression anchors exist");
        RunGeneralFixture(context, Path.Combine(stage, "absent-quaternion"), Code(absentCheck, invoke),
            "if (BapArgumentFixture.RuntimeObject.AcceptsQuaternion() || BapArgumentFixture.RuntimeObject.InvokesQuaternion()) throw new System.Exception(\"Missing Quaternion guard accepted\")", pin, allowUnsafe: true);
        RunGeneralFixture(context, Path.Combine(stage, "absent-quaternion-marshaling"), Code(check, absentInvoke),
            "if (BapArgumentFixture.RuntimeObject.InvokesQuaternion()) throw new System.Exception(\"Missing Quaternion marshaling accepted\")", pin, allowUnsafe: true);
    }

    private static void RuntimeGetters(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/RuntimeObject.cs");
        var code = GeneralRuntimeGetters0
            .Replace("__ACCESSOR__", Derive(source, @"(?s)    private static string Accessor\(.*?\r?\n    \}", "Production accessor boundary"), StringComparison.Ordinal)
            .Replace("__GETTER__", Derive(source, @"(?s)    internal T Get<T>\(.*?\r?\n    \}", "Production typed getter boundary"), StringComparison.Ordinal)
            .Replace("__FIELD_GETTER__", Derive(source, @"(?s)    internal T Field<T>\(.*?\r?\n    \}", "Production typed field getter boundary"), StringComparison.Ordinal);
        RunGeneralFixture(context, stage, code, "GetterProbe.Run()", pin, allowUnsafe: true);
    }

    private static void FightReadiness(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/GameUi.cs");
        var start = source.IndexOf("        var ui = fight.Get(\"contentPane\");", StringComparison.Ordinal);
        var end = source.IndexOf("        Plugin.Diagnostics.State(\"fightStep\"", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Production fight readiness block");
        RunGeneralFixture(context, stage, GeneralFightReadiness0.Replace("__BLOCK__", source[start..end], StringComparison.Ordinal), "FightReadinessProbe.Run()", pin);
    }

    private static void RelicBackground(DevContext context, string stage, Action<string> pin)
    {
        var source = ReadSource(context, "src/NativeUi.cs");
        var start = source.IndexOf("    internal sealed class Surface", StringComparison.Ordinal);
        var end = source.IndexOf("    internal static void OutlineText", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Production Surface extraction");
        RunGeneralFixture(context, stage, GeneralRelicBackground0 + source[start..end] + GeneralRelicBackground1, "RelicBackgroundTest.RelicBackgroundCheck.Run()", pin);
        Contains(ReadSource(context, "src/CardPreviewUi.cs"), new[] { "fill: new Color(0f, 0f, 0f, 0.88f)", "NativeUi.InheritText(title, dark: true)",
            "NativeUi.InheritText(description, dark: true)", "_relicBackground?.Refresh();" }, "Relic-only styling and live refresh");
    }

    private static void CharacterNames(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context);
        var profile = native.MainModule.GetType("UI.AccountInfoWindow");
        var body = Il(profile.Methods.Single(method => method.Name == "RefreshStatisticalData"));
        Check(Regex.IsMatch(body, "txt_Hero\\r?\\n[^\\n]*ldstr \"data\"[\\s\\S]*?GetCharacterNickName[\\s\\S]*?GTextField::SetVar"), "Native favorite-character template binding");
        foreach (var name in new[] { "get_templateVars", "SetVar", "FlushVars" }) Check(native.MainModule.GetType("FairyGUI.GTextField").Methods.Any(method => method.Name == name), "Native template API " + name);
        var source = ReadSource(context, "src/CharacterNameUi.cs");
        Check(native.MainModule.GetType("UI.UIStringType").Fields.Single(field => field.Name == "Character").IsLiteral, "Native character string category is a named enum literal");
        Contains(source, new[] { "RuntimeObject.EnumValue(\"UI\", \"UIStringType\", \"Character\")", "\"GetLocal\", nameId, stringType", "\"GetLocal\", nickId, stringType" }, "Character name and epithet use the current native category by name");
        Check(!Regex.IsMatch(source, "\"GetLocal\"[^\\r\\n]*,\\s*[0-9]+\\s*\\)"), "Character localization has no ordinal fallback");
        var methods = new[] { "Replace", "ReadLabel", "WriteLabel", "RestoreAll" }.Select(name =>
            Derive(source, @"(?s)    (?:private|internal) static (?:void|string) " + name + @"\(.*?\r?\n    \}", "Production character-name method " + name));
        var code = GeneralCharacterNames0.Replace("__NAMESPACE__", "BapCharacterNames", StringComparison.Ordinal)
            .Replace("__NAMES__", GeneralWithoutNamespace(ReadSource(context, "src/CharacterNames.cs")), StringComparison.Ordinal)
            .Replace("__METHODS__", String.Join("\n", methods), StringComparison.Ordinal);
        RunGeneralFixture(context, stage, code, "BapCharacterNames.Check.Run()", pin);
    }

    private static void ProbabilityUi(DevContext context, string stage, Action<string> pin)
    {
        var paths = new[] { "src/CombatAdvisor.cs", "src/VisibleCombat.cs", "src/CardAdvisor.cs", "src/EncounterAdvisor.cs", "src/AdviceText.cs", "focus-tests/probability/PresentationCheck.cs" };
        var text = ReadSource(context, "src/ModText.cs");
        var start = text.IndexOf("    internal static bool Korean", StringComparison.Ordinal);
        var end = text.IndexOf("    internal static string AutomaticStatus", StringComparison.Ordinal);
        Check(start >= 0 && end > start && text[start..end].Contains("[\"전투불능\"] = \"KO\"", StringComparison.Ordinal), "Exact production localization boundary");
        var code = GeneralProbability0.Replace("__NS__", "BapProbability", StringComparison.Ordinal) + "\n" + String.Join("\n", paths.Select(path => WithoutImports(ReadSource(context, path))))
            + "\ninternal static class ModText {\n" + text[start..end] + "\n}\n}";
        RunGeneralFixture(context, Path.Combine(stage, "current"), code, "BapProbability.PresentationCheck.Run()", pin);
        var mutants = new[] {
            ("attack-minimum", "Probability(afterKoMax)", "Probability(afterKoMin)", "attack card chooses favorable endpoint"),
            ("defense-maximum", "Probability(defKoLow)", "Probability(defKoHigh)", "own defense shows minimum risk"),
            ("dodge-maximum", "Probability(dodgeKoLow)", "Probability(dodgeKoHigh)", "all visible dodge endpoints"),
            ("encounter-self-maximum", "Probability(SelfKoMin)", "Probability(SelfKoMax)", "enemy maximum / own minimum encounter display"),
            ("favorable-recommendation", "new(defHigh, defKoHigh, 0), new(dodgeHigh, dodgeKoHigh, 0)", "new(defLow, defKoLow, 0), new(dodgeLow, dodgeKoLow, 0)", "conservative recommendation remains dodge")
        };
        foreach (var (name, from, to, reason) in mutants) {
            Check(code.Contains(from, StringComparison.Ordinal), "Probability mutation anchor " + name);
            RunGeneralFixture(context, Path.Combine(stage, name), code.Replace(from, to, StringComparison.Ordinal), "BapProbability.PresentationCheck.Run()", pin, reason);
        }
    }

    private static void EncounterRecommendation(DevContext context, string stage, Action<string> pin)
    {
        var ui = ReadSource(context, "src/GameUi.cs");
        var start = ui.IndexOf("    private static void EnsureBadges(", StringComparison.Ordinal); var end = ui.IndexOf("    private static void UpdateHover()", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Exact production encounter UI boundary");
        var paths = new[] { "src/CombatAdvisor.cs", "src/VisibleCombat.cs", "src/CardAdvisor.cs", "src/EncounterAdvisor.cs", "src/AdviceBadge.cs", "src/AdviceText.cs" };
        var code = GeneralEncounter0.Replace("__NS__", "BapEncounter", StringComparison.Ordinal) + "\n" + String.Join("\n", paths.Select(path => WithoutImports(ReadSource(context, path))))
            + "\n" + ReadSource(context, "focus-tests/encounter-recommendation/Checks.cs").Replace("__ENCOUNTER_UI__", ui[start..end], StringComparison.Ordinal) + "\n}";
        RunGeneralFixture(context, Path.Combine(stage, "current"), code, "BapEncounter.EncounterRecommendationCheck.Policy(); BapEncounter.EncounterRecommendationCheck.Badges()", pin);
        var policy = ReadSource(context, "src/EncounterAdvisor.cs");
        var policyStart = policy.IndexOf("        double enemyMin = ", StringComparison.Ordinal); var policyEnd = policy.LastIndexOf("\n    }", StringComparison.Ordinal);
        Check(policyStart >= 0 && policyEnd > policyStart, "Production encounter recommendation policy boundary");
        var mutants = new[] {
            ("old-consensus", policy[policyStart..policyEnd].TrimEnd('\r'), GeneralEncounter1, "Policy", "single favorable paired recommendation"),
            ("attack-on-tie", "bestDamageGain > epsilon;", "bestDamageGain >= -epsilon;", "Policy", "single favorable paired recommendation"),
            ("two-thumbs", "advice.Attack == false, scale", "advice.Attack.HasValue, scale", "Badges", "actual encounter/badge path shows one")
        };
        foreach (var (name, from, to, method, reason) in mutants) {
            Check(code.Contains(from, StringComparison.Ordinal), "Encounter mutation anchor " + name);
            RunGeneralFixture(context, Path.Combine(stage, name), code.Replace(from, to, StringComparison.Ordinal), "BapEncounter.EncounterRecommendationCheck." + method + "()", pin, reason);
        }
    }

    private static void KoMinimum(DevContext context, string stage, Action<string> pin)
    {
        var paths = new[] { "src/CombatAdvisor.cs", "src/PublicCombatEffects.cs", "src/VisibleCombat.cs", "src/CardAdvisor.cs", "src/CardKoMinimum.cs", "src/CardDiceLayout.cs",
            "src/EncounterAdvisor.cs", "src/AdviceText.cs", "self-test/CombatEffectsCheck.cs", "self-test/CombatSimulationCheck.cs", "self-test/CombatLogReplayCheck.cs" };
        var publicSource = ReadSource(context, "self-test/PublicCombatEffectsCheck.cs");
        var fixture = Regex.Match(publicSource, @"(?s)namespace BetterAstralParty\s*\{\s*(internal sealed class RuntimeObject.*?)\r?\n\}\r?\n\r?\ninternal static class PublicCombatEffectsCheck").Groups[1].Value;
        var at = publicSource.IndexOf("internal static class PublicCombatEffectsCheck", StringComparison.Ordinal);
        var schema = Derive(ReadSource(context, "self-test/Program.cs"), @"(?s)var shadow = CardAdvisor\.FromConfig.*?(?=var protectionOnly =)", "Exact card-schema assertion boundary");
        Check(fixture.Length != 0 && at >= 0 && schema.Contains("10009", StringComparison.Ordinal) && schema.Contains("10010", StringComparison.Ordinal), "Focused combat native substitutes");
        var code = GeneralKoMinimum0.Replace("__NS__", "BapKoMinimum", StringComparison.Ordinal) + "\n" + String.Join("\n", paths.Select(path => WithoutImports(ReadSource(context, path))))
            + "\n" + fixture + "\n" + publicSource[at..] + "\n" + GeneralKoMinimum1.Replace("__SCHEMA__", schema, StringComparison.Ordinal);
        RunGeneralFixture(context, Path.Combine(stage, "current"), code, "BapKoMinimum.FocusedCombat.Run()", pin);
        var grant = "id == 10009 && effectType == 1 && parameters[2] == 10010";
        var mutants = new[] {
            ("vacuous-endpoint", "if (row == 1 && pair.Value.DefenseDie == (survival ? defenseDice.Min : defenseDice.Max)) return (0, \"\", \"—\");", "/* removed vacuous endpoint */", "Thresholds", "vacuous defender bound"),
            ("missing-final-damage", "if (damage is { } bounds)", "if (pair == null) return \"계산 정보 부족\"; if (damage is { } bounds)", "Thresholds", "final arithmetic known without pre-roll support"),
            ("missing-final-defense", "if (unconditional) return (0, \"\", \"—\");", "if (pair == null) return (0, \"\", \"?\"); if (unconditional) return (0, \"\", \"—\");", "Thresholds", "known final condition-free defense"),
            ("unknown-grants", grant, "effectType == 1 && parameters[2] == 10010", "Schema", "later-card semantic mapping not generalized"),
            ("unknown-final-atk", grant, "(id == 10009 && effectType == 1 && parameters[2] == 10010 || id == 10010 && effectType == 1)", "Schema", "Known later-hand side effect")
        };
        foreach (var (name, from, to, method, reason) in mutants) {
            Check(code.Contains(from, StringComparison.Ordinal), "KO mutation anchor " + name);
            RunGeneralFixture(context, Path.Combine(stage, name), code.Replace(from, to, StringComparison.Ordinal), "BapKoMinimum.FocusedCombat." + method + "()", pin, reason);
        }
    }

    private static string Il(MethodDefinition method) => method.HasBody ? String.Join("\n", method.Body.Instructions.Select(instruction => instruction.ToString())) : "";
}
