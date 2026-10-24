using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    internal static bool UpdateChecks(DevContext context, string name, string stage, string scratch, Dictionary<string, string> options, Action<string> pin)
    {
        switch (name) {
            case "shared-loader": SharedLoaderPolicy(context, stage, scratch, pin); break;
            case "update-runtime-isolation": RuntimeIsolation(context, stage, options, pin); break;
            case "update-notification-ui": UpdateNotificationMetadata(context, stage, options, pin); break;
            case "updater-primitives": UpdatePrimitives(context, stage, pin); break;
            case "updater-transaction": UpdateSafetyFixture(context, stage, name, options, pin); break;
            case "updater-apply-boundary": UpdateSafetyFixture(context, stage, name, options, pin); break;
            case "staging": UpdateSafetyFixture(context, stage, name, options, pin); break;
            case "update-generation": UpdateGenerationFixture(context, stage, pin); break;
            default: return false;
        }
        return true;
    }
    private static void SharedLoaderPolicy(DevContext context, string stage, string scratch, Action<string> pin)
    {
        var install = ReadSource(context, "tools/NativeInstall.cs"); NoPattern(install, "set-loader-mode\\.ps1|LoaderChange\\(|SetLoaderMode\\(", "Install never toggles existing shared loader");
        var uninstall = ReadSource(context, "tools/NativeUninstall.cs");
        Pattern(uninstall, "if\\s*\\(!restoreLoader\\.Equals\\(\"Keep\", StringComparison\\.OrdinalIgnoreCase\\) && plan\\.Targets\\.Count != 0\\)\\s*try \\{ loaderUpdated = LoaderChange", "Default native removal bypasses loader mutation");
        var entry = ReadSource(context, "tools/NativeMaintenanceEntry.cs"); Contains(entry, new[] { "Get(values, \"restoreloader\") ?? \"Keep\"" }, "Default loader restoration is Keep");
        var plugin = ReadSource(context, "src/Plugin.cs"); var load = plugin.IndexOf("public override void Load()", StringComparison.Ordinal);
        var gate = plugin.IndexOf("Environment.GetEnvironmentVariable(\"BAP_COMPATIBILITY_CHECKED\")", load, StringComparison.Ordinal); var start = plugin.IndexOf("MinimalDiagnostics = DiagnosticOwner.Start(", load, StringComparison.Ordinal);
        Check(load >= 0 && gate > load && gate < start && plugin[gate..start].Contains("return;", StringComparison.Ordinal), "Ordinary Steam launch returns before BetterAstralParty initialization");
        var consume = plugin.IndexOf("StartupGuard.ConsumeLaunchRequest()", load, StringComparison.Ordinal); Check(consume > load && consume < gate, "Mod launch request is consumed before the activation gate");
        var guard = ReadSource(context, "src/StartupGuard.cs");
        Pattern(guard, @"File\.Delete\(path\);\s*return LaunchRequestFresh\(age\);", "Launch request is deleted before it is honoured");
        Contains(guard, new[] { "TimeSpan.FromMinutes(3)" }, "Launch request is short-lived");
        Contains(guard, new[] { "!text.Contains(\" stage=Stopped \", StringComparison.Ordinal)", "internal static void Stopped() => Stage(\"Stopped\");" }, "A deliberate safety stop is not a failed start");
        Contains(plugin, new[] { "StartupGuard.Stage(\"BootstrapGate\", \"Failed\"); StartupGuard.Stopped(); return;" }, "Update recovery gate marks its stop as deliberate");
        var modEntry = ReadSource(context, "tools/BetterAstralParty-Mod.cmd");
        Contains(modEntry, new[] { "type nul > \"%REQUEST%\\mod-launch.request\"", "start \"\" \"steam://rungameid/2622000\"", "steam://run/2622000//--doorstop-enabled true --bap-compatibility-checked/" }, "Mod entry lets Steam start the owned app");
        NoPattern(modEntry, @"SteamOverlayGameId|SteamGameId|AstralParty_INT\.exe"" --doorstop", "Mod entry never starts the game itself or overrides Steam overlay identity");
        // Issue #15: a PC whose PATH lacks System32 cannot find tasklist/find/findstr; call them by full path.
        var vanillaEntry = ReadSource(context, "tools/AstralParty-Vanilla.cmd");
        foreach (var (name, batchText) in new[] { ("Mod", modEntry), ("Vanilla", vanillaEntry) })
        {
            Contains(batchText, new[] { "\"%SystemRoot%\\System32\\tasklist.exe\"", "\"%SystemRoot%\\System32\\find.exe\"" }, name + " entry calls system tools by full path");
            NoPattern(batchText, @"(?mi)^\s*(tasklist|find|findstr)\b|\|\s*(find|findstr)\b", name + " entry never relies on PATH for system tools");
        }
        Contains(modEntry, new[] { "\"%SystemRoot%\\System32\\findstr.exe\"" }, "Mod entry reads the loader switch by full path");
        // cmd.exe misreads LF-only batch files once chcp changes the code page (issue #15).
        Check(PackagingWhole.CrlfOnly("a\r\nb\r\n"u8.ToArray()) && !PackagingWhole.CrlfOnly("a\nb\r\n"u8.ToArray()) && !PackagingWhole.CrlfOnly("a\r\nb"u8.ToArray()), "CRLF batch detector");
        foreach (var batch in Directory.GetFiles(context.Root, "*.cmd").Concat(Directory.GetFiles(Path.Combine(context.Root, "tools"), "*.cmd")))
        {
            Check(PackagingWhole.CrlfOnly(File.ReadAllBytes(batch)), "CRLF batch file: " + Path.GetFileName(batch));
            NoPattern(File.ReadAllText(batch), @"(?mi)^\s*(tasklist|find|findstr|chcp)\b|\|\s*(find|findstr)\b", Path.GetFileName(batch) + " never relies on PATH for system tools");
        }
        var image = BuildProject(context, "focus-tests/update-runtime-isolation/SharedLoaderFixture.csproj", stage, pin);
        Ensure(context.Run(Dotnet(context), new[] { image, scratch, Path.Combine(context.Root, "artifacts") }, env: EnvironmentFor(stage), timeoutMilliseconds: 60000, logPath: Path.Combine(stage, "shared-loader.log")), "Actual native shared-loader policy");
    }
    private static void RuntimeIsolation(DevContext context, string stage, Dictionary<string, string> options, Action<string> pin)
    {
        var mod = ReadSource(context, "src/ModUi.cs"); var pluginSource = ReadSource(context, "src/Plugin.cs");
        var start = Regex.Match(mod, @"(?m)^    internal static void Tick\(\)\s*\r?\n    \{"); var end = mod.IndexOf("        var measure = ", start.Index, StringComparison.Ordinal);
        Check(start.Success && end >= 0, "Exact production UI-frame prefix boundary"); var prefix = mod.Substring(start.Index, end - start.Index);
        var patch = pluginSource.IndexOf("PatchFeature(\"CoreUi\", typeof(EventSystemUpdatePatch));", StringComparison.Ordinal); var catalog = pluginSource.IndexOf("Catalogs = new CatalogGuard(", StringComparison.Ordinal);
        Check(patch >= 0 && catalog > patch && Regex.Matches(pluginSource, "PatchFeature\\(\"CoreUi\", typeof\\(EventSystemUpdatePatch\\)\\);").Count == 1, "Verified engine driver precedes initial catalog validation exactly once");
        Contains(prefix, new[] { "(!Compatibility.Allowed(\"CoreUi\") || !Compatibility.Allowed(\"HomeMenu\") || GameUi.HomeAvailable) && Plugin.Automatic.Status != AutomaticUpdateStatus.CheckingInstallation" }, "Blocked shared UI or optional menu short-circuits native discovery and checking pauses discovery");
        var uiGate = prefix.IndexOf("if (!Compatibility.Allowed(\"CoreUi\"))", StringComparison.Ordinal);
        Check(uiGate > prefix.IndexOf("MatchFocus.Tick();", StringComparison.Ordinal) && uiGate > prefix.IndexOf("FocusAudio.Tick();", StringComparison.Ordinal)
            && prefix.IndexOf("PvpSafety.Poll();", StringComparison.Ordinal) < prefix.IndexOf("FocusAudio.Restore();", StringComparison.Ordinal),
            "Independent match focus and audio run with fresh PvP state before a failed shared UI gate");
        var observer = MethodFragment(pluginSource, "internal static void PollCatalogs");
        NoPattern(observer, @"Compatibility\.|FailUi|CoreUi", "Catalog observation cannot trip the general UI compatibility breaker");
        var observerCalls = Regex.Matches(mod, @"(?m)^[ \t]*Plugin\.PollCatalogs\(DateTime\.UtcNow\);[ \t]*\r?$");
        var gameTick = mod.IndexOf("GameUi.Tick();", end, StringComparison.Ordinal);
        Check(observerCalls.Count == 1 && observerCalls[0].Index > end && observerCalls[0].Index < gameTick, "The actual catalog observation is called once in the healthy UI body before native gameplay UI");
        var harness = Template(context, "focus-tests/update-runtime-isolation/RuntimeFixture.template.cs", pin)
            .Replace("__NAMESPACE__", "BapRuntimeIsolation", StringComparison.Ordinal).Replace("__PREFIX__", prefix, StringComparison.Ordinal)
            .Replace("__POLL_CATALOGS__", observer, StringComparison.Ordinal).Replace("__CATALOG_TICK__", observerCalls[0].Value, StringComparison.Ordinal);
        RunEmbedded(context, stage, "update-runtime-isolation", harness, "System.Console.WriteLine(\"PREFIX-CASES:\"+BapRuntimeIsolation.Runner.Run()); System.Console.WriteLine(\"CATALOG-CASES:\"+BapRuntimeIsolation.CatalogRunner.Run())", pin, warnAsError: true, noWarn: "8625");
        var game = ReadSource(context, "src/GameUi.cs"); var compatibility = ReadSource(context, "src/Compatibility.cs");
        var failHome = game[game.IndexOf("    private static void FailHomeMenu(", StringComparison.Ordinal)..game.IndexOf("    private static void ClearHomeMenu(", StringComparison.Ordinal)];
        var failSettings = mod[mod.IndexOf("    private static void FailSettings(", StringComparison.Ordinal)..mod.IndexOf("    private static void RefreshSettingsLanguage(", StringComparison.Ordinal)];
        var homeHarness = Template(context, "focus-tests/update-runtime-isolation/HomeSettingsFixture.template.cs", pin)
            .Replace("__COMPATIBILITY__", compatibility.Replace("namespace BetterAstralParty;", "", StringComparison.Ordinal)
                .Replace("internal static int Revision { get; private set; }", "internal static int Revision { get; private set; } internal static void Reset() { Blocked.Clear(); Revision = 0; }", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("__GAME_TICK__", MethodFragment(game, "internal static void Tick"), StringComparison.Ordinal)
            .Replace("__REFRESH_LANGUAGE__", MethodFragment(game, "internal static void RefreshLanguage"), StringComparison.Ordinal)
            .Replace("__FAIL_HOME__", failHome, StringComparison.Ordinal)
            .Replace("__CLEAR_HOME__", MethodFragment(game, "private static void ClearHomeMenu"), StringComparison.Ordinal)
            .Replace("__TOGGLE_PANEL__", MethodFragment(mod, "internal static void TogglePanel"), StringComparison.Ordinal)
            .Replace("__OPEN_UPDATE_SETTINGS__", MethodFragment(mod, "internal static void OpenUpdateSettings"), StringComparison.Ordinal)
            .Replace("__NOTICE_TICK__", MethodFragment(ReadSource(context, "src/UpdateNotificationUi.cs"), "internal static void Tick"), StringComparison.Ordinal)
            .Replace("__IS_OPEN__", Regex.Match(mod, @"(?m)^    internal static bool IsOpen =>[^\r\n]+;").Value, StringComparison.Ordinal)
            .Replace("__FAIL_SETTINGS__", failSettings, StringComparison.Ordinal);
        RunEmbedded(context, stage, "home-settings-isolation", homeHarness, "System.Console.WriteLine(\"HOME-SETTINGS-CASES:\"+BapHomeSettings.Runner.Run())", pin, warnAsError: true, noWarn: "8625");
        Contains(MethodFragment(game, "private static void UpdateHome"), new[] { "HomeAvailable = false;", "if (!Compatibility.Allowed(\"HomeMenu\"))", "_home = home;" }, "Failed menu retains fresh native home observation without recreating its clone");
        Contains(mod, new[] { "internal static bool IsOpen => Compatibility.Allowed(\"SettingsUi\")" }, "Failed settings never dereference stale modal state from another feature");
        var metadataCases = 0;
        if (options.TryGetValue("plugin-path", out var selected)) {
            var path = Path.GetFullPath(selected); pin(path);
            Check(context.Hash(Path.Combine(context.Root, ".deps/bepinex/BepInEx/core/Mono.Cecil.dll")) == "769A59793D4B8885BBBFBC5AEE8F57A0D4E34D275C56C60C03994309B87F67E9", "Approved metadata reader hash");
            using var assembly = AssemblyDefinition.ReadAssembly(path); var startup = NativeMethod(assembly, "BetterAstralParty.Plugin", "LoadCore"); var il = startup.Body.Instructions;
            var driver = il.Where(row => row.OpCode.Code == Code.Ldtoken && row.Operand is TypeReference type && type.FullName == "BetterAstralParty.EventSystemUpdatePatch").ToArray();
            var verify = IlCalls(il, "BetterAstralParty.CatalogGuard", "Verify"); Check(driver.Length == 1 && verify.Length == 1, "Compiled driver/catalog counts");
            var registration = il.FirstOrDefault(row => row.Offset > driver[0].Offset && row.Operand is MethodReference method && method.Name == "PatchFeature");
            var construction = il.FirstOrDefault(row => row.OpCode.Code == Code.Newobj && row.Operand is MethodReference method && method.DeclaringType.FullName == "BetterAstralParty.CatalogGuard");
            Check(registration != null && construction != null && registration.Offset < construction.Offset && registration.Offset < verify[0].Offset, "Compiled engine registration precedes catalog failure");
            var catching = startup.Body.ExceptionHandlers.Where(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= verify[0].Offset && handler.TryEnd.Offset > verify[0].Offset).ToArray();
            Check(catching.Length == 1 && catching[0].TryStart.Offset > registration!.Offset, "Catalog exception does not undo registered driver"); metadataCases++;
            Check(IlCalls(NativeMethod(assembly, "BetterAstralParty.EventSystemUpdatePatch", "Prepare").Body.Instructions, "BetterAstralParty.Plugin", "RequireHook").Length == 1 && IlCalls(NativeMethod(assembly, "BetterAstralParty.EventSystemUpdatePatch", "Postfix").Body.Instructions, "BetterAstralParty.ModUi", "Tick").Length == 1, "Compiled hook signature verification and frame driver"); metadataCases++;
            var tick = NativeMethod(assembly, "BetterAstralParty.ModUi", "Tick").Body;
            var release = IlCalls(tick.Instructions, "BetterAstralParty.ReleaseUpdates", "Tick"); var automatic = IlCalls(tick.Instructions, "BetterAstralParty.AutomaticUpdates", "Tick"); var refresh = IlCalls(tick.Instructions, "BetterAstralParty.GameUi", "RefreshReleaseNotification");
            Check(release.Length == 1 && automatic.Length == 1 && refresh.Length == 1, "Compiled backend/native notice call counts");
            var backendCatch = tick.ExceptionHandlers.Where(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= release[0].Offset && handler.TryEnd.Offset > automatic[0].Offset).ToArray();
            Check(backendCatch.Length == 1 && backendCatch[0].TryEnd.Offset <= refresh[0].Offset && automatic[0].Offset < refresh[0].Offset, "Notice outside and after backend exception boundary"); metadataCases++;
            var refreshCatch = tick.ExceptionHandlers.Single(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= refresh[0].Offset && handler.TryEnd.Offset > refresh[0].Offset);
            var refreshHandler = tick.Instructions.Where(row => row.Offset >= refreshCatch.HandlerStart.Offset && row.Offset < (refreshCatch.HandlerEnd?.Offset ?? int.MaxValue)).ToArray();
            Check(IlCalls(refreshHandler, "BetterAstralParty.Compatibility", "Block").Length == 1 && IlCalls(refreshHandler, "BetterAstralParty.ModUi", "FailUi").Length == 0,
                "Compiled optional notification decoration fault cannot trip the shared UI breaker"); metadataCases++;
            var cleanup = IlCalls(tick.Instructions, "BetterAstralParty.FieldZoomUi", "Clear"); Check(cleanup.Length == 1 && cleanup[0].Offset > automatic[0].Offset && cleanup[0].Offset < refresh[0].Offset, "Blocked camera cleanup between backend and healthy UI");
            var focus = IlCalls(tick.Instructions, "BetterAstralParty.MatchFocus", "Tick"); var audio = IlCalls(tick.Instructions, "BetterAstralParty.FocusAudio", "Tick"); var pvp = IlCalls(tick.Instructions, "BetterAstralParty.PvpSafety", "Poll");
            Check(focus.Length == 1 && audio.Length == 1 && pvp.Length == 1 && focus[0].Offset > automatic[0].Offset && pvp[0].Offset > focus[0].Offset
                && audio[0].Offset > pvp[0].Offset && audio[0].Offset < cleanup[0].Offset, "Compiled independent focus/audio execute before the shared UI early-return cleanup"); metadataCases++;
            var cleanupCatch = tick.ExceptionHandlers.Where(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= cleanup[0].Offset && handler.TryEnd.Offset > cleanup[0].Offset).ToArray();
            Check(cleanupCatch.Length == 1 && cleanupCatch[0].TryStart.Offset >= backendCatch[0].TryEnd.Offset && cleanupCatch[0].TryEnd.Offset <= refresh[0].Offset, "Camera cleanup has separate contained exception boundary"); metadataCases++;
            var catalogTick = IlCalls(tick.Instructions, "BetterAstralParty.Plugin", "PollCatalogs"); var gameplayTick = IlCalls(tick.Instructions, "BetterAstralParty.GameUi", "Tick");
            Check(catalogTick.Length == 1 && gameplayTick.Length == 1 && catalogTick[0].Offset > refresh[0].Offset && catalogTick[0].Offset < gameplayTick[0].Offset, "Compiled catalog observation remains in the continuing healthy UI frame"); metadataCases++;
            var compiledObserver = NativeMethod(assembly, "BetterAstralParty.Plugin", "PollCatalogs", 1); var observerIl = compiledObserver.Body.Instructions;
            var polls = IlCalls(observerIl, "BetterAstralParty.CatalogGuard", "Poll"); var results = IlCalls(observerIl, "BetterAstralParty.CatalogGuard", "get_Changed");
            Check(polls.Length == 1 && results.Length == 1 && polls[0].Offset < results[0].Offset, "Compiled observer polls and reads only its public change result");
            var observerCatch = compiledObserver.Body.ExceptionHandlers.Where(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= polls[0].Offset && handler.TryEnd.Offset > results[0].Offset).ToArray();
            Check(observerCatch.Length == 1 && IlCalls(observerIl, "BetterAstralParty.Compatibility", "Block").Length == 0 && IlCalls(observerIl, "BetterAstralParty.ModUi", "FailUi").Length == 0, "Compiled observation failure is isolated from CoreUi");
            var handlerStart = observerCatch[0].HandlerStart.Offset; var handlerEnd = observerCatch[0].HandlerEnd?.Offset ?? int.MaxValue;
            var retire = IlCalls(observerIl, "BetterAstralParty.Plugin", "set_Catalogs").Where(row => row.Offset >= handlerStart && row.Offset < handlerEnd).ToArray();
            Check(retire.Length == 1 && retire[0].Previous?.OpCode.Code == Code.Ldnull, "Compiled observer catch retires only the observer reference"); metadataCases++;
            var reports = observerIl.Where(row => row.Offset >= handlerStart && row.Offset < handlerEnd && row.Operand is MethodReference method &&
                ((method.DeclaringType.FullName == "BetterAstralParty.Observability.DiagnosticHub" && method.Name == "Failure")
                || (method.DeclaringType.FullName == "BetterAstralParty.DiagnosticLog" && method.Name == "Error")
                || (method.DeclaringType.FullName == "BepInEx.Logging.ManualLogSource" && method.Name == "LogWarning"))).ToArray();
            Check(reports.Length == 3 && reports.All(report => compiledObserver.Body.ExceptionHandlers.Count(handler => handler.HandlerType == ExceptionHandlerType.Catch
                && handler.TryStart.Offset >= handlerStart && handler.TryStart.Offset <= report.Offset && handler.TryEnd.Offset > report.Offset) == 1), "Compiled catalog failure reporters each have independent best-effort boundaries"); metadataCases++;
            var gameBody = NativeMethod(assembly, "BetterAstralParty.GameUi", "Tick").Body;
            foreach (var name in new[] { "UpdateHome", "FollowHomeMenu", "HandleMenuInput" }) {
                var call = IlCalls(gameBody.Instructions, "BetterAstralParty.GameUi", name).Single();
                var boundary = gameBody.ExceptionHandlers.Where(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= call.Offset && handler.TryEnd.Offset > call.Offset)
                    .OrderBy(handler => handler.TryEnd.Offset - handler.TryStart.Offset).First();
                var handlerIl = gameBody.Instructions.Where(row => row.Offset >= boundary.HandlerStart.Offset && row.Offset < (boundary.HandlerEnd?.Offset ?? int.MaxValue));
                Check(IlCalls(handlerIl, "BetterAstralParty.GameUi", "FailHomeMenu").Length == 1 && IlCalls(handlerIl, "BetterAstralParty.ModUi", "FailUi").Length == 0,
                    "Compiled optional home/menu call has its own failure boundary: " + name); metadataCases++;
            }
            foreach (var name in new[] { "TickSettings", "RefreshSettingsLanguage" }) {
                var call = IlCalls(tick.Instructions, "BetterAstralParty.ModUi", name).Single();
                var boundary = tick.ExceptionHandlers.Where(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= call.Offset && handler.TryEnd.Offset > call.Offset)
                    .OrderBy(handler => handler.TryEnd.Offset - handler.TryStart.Offset).First();
                var handlerIl = tick.Instructions.Where(row => row.Offset >= boundary.HandlerStart.Offset && row.Offset < (boundary.HandlerEnd?.Offset ?? int.MaxValue));
                Check(IlCalls(handlerIl, "BetterAstralParty.ModUi", "FailSettings").Length == 1 && IlCalls(handlerIl, "BetterAstralParty.ModUi", "FailUi").Length == 0,
                    "Compiled optional settings call has its own failure boundary: " + name); metadataCases++;
            }
        }
        context.WriteJsonCreateNew(Path.Combine(stage, "runtime-isolation-parity.json"), new { executedPrefixCases = 32, executedCatalogObserverCases = 12, executedHomeSettingsCases = 16, literalProductionCatalogObserver = true, literalProductionHomeTickAndSettingsBoundaries = true, observerAndNativeUiAreInertSubstitutes = true, compiledMetadataCases = metadataCases, compiledPluginChecked = metadataCases != 0, realNativeGameOrNetworkExecuted = false });
    }
    private static Instruction[] IlCalls(IEnumerable<Instruction> il, string type, string name) => il.Where(row => row.Operand is MethodReference method && method.DeclaringType.FullName == type && method.Name == name).ToArray();
}
