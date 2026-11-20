using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    // Custom Frame Rate (2026-11-19): the game's own frame choice stays the game's, so removing BAP leaves the game starting at
    // that choice. BAP writes nothing the game saves (its index, V-Sync, the list's items), the game writes the frame rate only
    // at start, on a screen change and from its list (all of which Apply puts back the next frame), the native names it reads
    // still exist, and it runs before the PvP gate as the user decided.
    private static void CustomFrameRate(DevContext context)
    {
        using var native = Native(context);
        var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var settings = types["Core.GameSettings"];
        var getFrames = settings.Methods.Single(method => method.Name == "GetFrames" && method.Parameters.Count == 0);
        Check(getFrames.IsStatic && getFrames.ReturnType.FullName == "System.Int32" && Il(getFrames).Contains("GetFramesIndex", StringComparison.Ordinal),
            "Native GetFrames gives the saved choice's frame rate");
        var writers = types.Values.SelectMany(type => type.Methods).Where(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                instruction.Operand is Mono.Cecil.MethodReference reference && reference.Name == "set_targetFrameRate" && reference.DeclaringType.FullName == "UnityEngine.Application"))
            .Select(method => method.DeclaringType.FullName + "::" + method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Check(writers.SequenceEqual(new[] { "Core.GameSettings::InitScreenSetting", "UI.SettingWindow::SelectFrame" }),
            "The game writes the frame rate only at start or a screen change and from its list: " + String.Join(", ", writers));
        Check(types["UI.UISettingWindow"].Fields.Any(field => field.Name == "com_Screen" && field.FieldType.FullName == "UI.UISetting_Com_Screen")
            && types["UI.UISetting_Com_Screen"].Fields.Any(field => field.Name == "com_FrameRate" && field.FieldType.FullName == "UI.UISetting_Com_ComboBox")
            && types["UI.UISetting_Com_ComboBox"].BaseType.FullName == "FairyGUI.GComboBox"
            && types["FairyGUI.GComboBox"].Fields.Any(field => field.Name == "dropdown" && field.FieldType.FullName == "FairyGUI.GComponent")
            && types["FairyGUI.GComboBox"].Fields.Any(field => field.Name == "_list" && field.FieldType.FullName == "FairyGUI.GList")
            && types["FairyGUI.GRoot"].Methods.Any(method => method.Name == "HidePopup" && method.Parameters.Count == 0)
            && types["FairyGUI.GTextInput"].Methods.Any(method => method.Name == "set_restrict")
            && types["FairyGUI.GTextInput"].Methods.Any(method => method.Name == "set_maxLength"),
            "Native frame-rate combo, its dropdown and list, and the text input");
        // Code only: the comments name the game's writers to explain what is left alone.
        var source = Regex.Replace(ReadSource(context, "src/CustomFrameRate.cs"), @"//[^\n]*", "");
        NoPattern(source, @"ES3|PlayerPrefs|UpdateIndex|UpdateVSync|SelectFrame|vSyncCount\s*=(?!=)", "Custom frame rate writes nothing the game saves");
        // Its items, selection and values stay the game's: the combo is only read (Field, Get, LocalToRoot, GetTextField).
        Check(!Regex.IsMatch(source, @"\bcombo\??\.(Set|SetField)\(|\bcombo\??\.Call\(\s*""(set_|Set|Add|Remove|Clear|Dispose)")
            && Regex.Matches(source, @"\bcombo\??\.Call\(\s*""(\w+)""").Select(match => match.Groups[1].Value).All(name => name is "LocalToRoot" or "GetTextField"),
            "Custom frame rate never changes the game's combo itself");
        // The development frame test turns V-Sync and the frame rate limit off while a step measures; Custom Frame Rate stands
        // aside meanwhile, and the test puts the game's values back on a pause, a stop and a failure.
        Check(source.Contains("if (FrameProbe.HoldsFrameRate) return;", StringComparison.Ordinal), "Custom frame rate leaves the frame test's uncapped steps alone");
        var probe = ReadSource(context, "src/FrameProbe.cs");
        Check(Regex.Matches(probe, @"Uncap\(false\)").Count >= 3 && probe.Contains("internal static bool HoldsFrameRate => _cap != null;", StringComparison.Ordinal),
            "Frame test restores the game's V-Sync and frame rate on pause, stop and failure");
        var driver = ReadSource(context, "src/ModUi.cs");
        var apply = driver.IndexOf("CustomFrameRate.Apply();", StringComparison.Ordinal);
        var ui = driver.IndexOf("CustomFrameRate.TickUi();", StringComparison.Ordinal);
        Check(apply > 0 && apply < driver.IndexOf("PvpSafety.Poll()", StringComparison.Ordinal) && ui > 0 && ui < driver.IndexOf("if (pauseGameplay)", StringComparison.Ordinal),
            "Custom frame rate runs before the PvP gate");
    }
}
