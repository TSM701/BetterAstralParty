using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidDataException("Check failed: " + name); Console.WriteLine("PASS: " + name); }
    private static string ReadSource(DevContext context, string relative) => File.ReadAllText(Path.Combine(context.Root, relative));
    private static void Contains(string source, IEnumerable<string> rows, string category)
    { foreach (var row in rows) Check(source.Contains(row, StringComparison.Ordinal), category + ": " + row); }
    private static AssemblyDefinition Native(DevContext context)
    { context.ObserveNativeAssembly(); return AssemblyDefinition.ReadAssembly(context.NativeAssemblyPath); }
    private static MethodDefinition NativeAsyncMoveNext(AssemblyDefinition assembly, string type, string name, int? argc = null)
    {
        var source = NativeMethod(assembly, type, name, argc);
        var attributes = source.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute").ToArray();
        Check(attributes.Length == 1 && attributes[0].ConstructorArguments.Count == 1 && attributes[0].ConstructorArguments[0].Value is TypeReference, type + "." + name + " exact async state-machine attribute");
        var state = ((TypeReference)attributes[0].ConstructorArguments[0].Value).Resolve();
        Check(state != null && state.Module == assembly.MainModule && state.DeclaringType == source.DeclaringType, type + "." + name + " state machine belongs to the source method owner");
        var methods = state!.Methods.Where(method => method.Name == "MoveNext" && method.Parameters.Count == 0 && method.ReturnType.FullName == "System.Void" && !method.IsStatic && method.HasBody).ToArray();
        Check(methods.Length == 1, type + "." + name + " exact async MoveNext body"); return methods[0];
    }
    private static void SourceMode(DevContext context, string stage, Action<string> pin)
    {
        var fixture = Path.Combine(stage, "source-inventory");
        foreach (var folder in new[] { "src/nested", "src/bin", "diagnostics", "tools/review-candidates", "updater-tests", "THIRD-PARTY-NOTICES" }) Directory.CreateDirectory(Path.Combine(fixture, folder));
        foreach (var name in new[] { "src/Root.cs", "src/nested/Case.template.cs", "diagnostics/Collector.cs", "diagnostics/AGENTS.md", "src/nested/agents.md", "diagnostics/CLAUDE.md", "src/nested/claude.local.md", "updater-tests/Policy.cs", "README.md", "THIRD-PARTY-NOTICES/SOURCES.md", "THIRD-PARTY-NOTICES/component-matrix.json", "src/bin/Generated.cs", "tools/review-candidates/Historical.cs", "tools/legacy.ps1", "tools/RecoverGaLocal20261008.cs", "tools/SignGaRelease.cs", "tools/SignRc3DescriptorsOnce.cs" }) File.WriteAllText(Path.Combine(fixture, name), "inert source inventory fixture");
        Check(BuildCommands.Sources(fixture).SequenceEqual(new[] { "README.md", "THIRD-PARTY-NOTICES/SOURCES.md", "THIRD-PARTY-NOTICES/component-matrix.json", "diagnostics/Collector.cs", "src/Root.cs", "src/nested/Case.template.cs", "updater-tests/Policy.cs" }, StringComparer.Ordinal), "Source inventory includes normal diagnostics sources, packaged notices, nested templates and updater tests, excluding nested agent instructions and generated/historical interpreter inputs");
        Check(!BuildCommands.IsPublicSourcePath("diagnostics/AGENTS.md") && !BuildCommands.IsPublicSourcePath("src/nested/agents.md")
            && !BuildCommands.IsPublicSourcePath("diagnostics/CLAUDE.md") && !BuildCommands.IsPublicSourcePath("src/nested/claude.local.md")
            && BuildCommands.IsPublicSourcePath("docs/claude-notes.md") && BuildCommands.IsPublicSourcePath("diagnostics/Collector.cs") && BuildCommands.IsPublicSourcePath("docs/usage.md"), "Direct source export excludes nested agent instructions without excluding ordinary source or documentation");
        Check(!BuildCommands.IsPublicSourcePath("tools/REVIEW-CANDIDATES/SignRc3Review.cs"), "Direct source export rejects historical review candidates too");
        BootstrapAllocation(context, stage);
        ScratchCleanup(stage);
        NativeInputSelection(context, stage, pin);
        var lifetime = Path.Combine(stage, "bootstrap-lifetime"); Directory.CreateDirectory(lifetime);
        using (var marker = new FileStream(Path.Combine(lifetime, ".bap-bootstrap-fixture"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
            var bytes = System.Text.Encoding.ASCII.GetBytes("BetterAstralParty.BootstrapLifetimeFixture/v1\n"); marker.Write(bytes);
        }
        Framework(context, lifetime, "NativeBootstrapFixture", new[] { "developer-tools/Bootstrap.cs", "focus-tests/bootstrap-lifetime/NativeFixture.cs" }, new[] { lifetime }, pin);
        foreach (var frontend in new[] { "Developer.cmd", "Create-Diagnostics.cmd", "Open-Logs.cmd", "tools/BetterAstralParty-Mod.cmd", "tools/AstralParty-Vanilla.cmd" })
            Check(Regex.IsMatch(ReadSource(context, frontend), @"\A@echo off\r?\nsetlocal DisableDelayedExpansion\r?\n"), frontend + " preserves literal exclamation marks and caller environment");
        Contains(ReadSource(context, "Force-Uninstall.cmd"), new[] { "Deleted files cannot be restored", "remaining files and logs" }, "Emergency removal reports irreversible partial outcomes");
        foreach (var folder in new[] { "src", "updater", "diagnostics", "focus-tests", "diagnostics-tests", "updater-tests", "updater-safety-tests", "self-test" }) {
            var files = SourceFiles(Path.Combine(context.Root, folder)).Where(file => Path.GetExtension(file) == ".cs").ToArray();
            foreach (var file in files)
                if (Regex.IsMatch(File.ReadAllText(file), @"(?i)powershell\.exe|pwsh\.exe|-EncodedCommand|System\.Management\.Automation")) throw new InvalidDataException("PowerShell route remains in compiled source: " + file);
            Check(files.Length != 0, "Compiled product/fixtures have no PowerShell route: " + folder);
        }
        foreach (var (file, operation) in new[] { ("Install.cmd", "install"), ("Uninstall.cmd", "uninstall"), ("Force-Uninstall.cmd", "force-uninstall") }) {
            var source = ReadSource(context, file);
            Check(Regex.IsMatch(source, @"\A@echo off\r?\nsetlocal DisableDelayedExpansion\r?\n"), file + " delayed expansion disabled");
            Contains(source, new[] { "set \"BAP_ENTRYROOT=%~dp0\"", "set \"BAP_MAINTENANCE_CWD=%CD%\"", "package-manifest.json", "--development --" + operation, "--" + operation + " %*", "exit /b %BAP_EXIT%" }, file);
            Check(!Regex.IsMatch(source, @"(?i)powershell(?:\.exe)?|pwsh(?:\.exe)?|wscript|cscript|mshta|Add-Type|Assembly\]::Load"), file + " invokes no interpreter");
            Check(!Regex.IsMatch(source, @"(?im)^\s*(?:call|start)\s"), file + " retains synchronous exit code");
        }
        var entry = ReadSource(context, "tools/NativeMaintenanceEntry.cs");
        Contains(entry, new[] { "_operation = emergency ? \"EmergencyUninstall\" : values.ContainsKey(\"install\") ? \"Installation\" : \"Uninstall\";" }, "Native failure records distinguish forced removal from ordinary uninstall");
        var start = entry.IndexOf("private static int Development(", StringComparison.Ordinal);
        var end = entry.IndexOf("internal static string CommandLine", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "One native development dispatcher located");
        var dispatcher = entry[start..end];
        var previous = -1;
        foreach (var step in new[] { "fence.OpenFile(\"artifacts/development-maintenance.json\")", "Development package outside approved scope.",
            "fence.OpenFile(relative + \"/package-manifest.json\")", "manifest.Hash() != digest", "MaintenanceJson.Text(inventory, \"version\") != version",
            "MaintenanceJson.Text(row, \"sha256\") != image.Hash()", "Process.Start(" }) {
            var index = dispatcher.IndexOf(step, StringComparison.Ordinal); Check(index > previous, "Pinned source dispatch ordering: " + step); previous = index;
        }
        Contains(dispatcher, new[] { "Package and game directories must be separate.", "pointer.Recheck(); manifest.Recheck(); image.Recheck();", "Arguments = CommandLine(forward)" }, "Dispatcher held identity and arguments");
        Check(!Regex.IsMatch(dispatcher, @"Assembly\.Load|DynamicInvoke|Add-Type|powershell\.exe"), "Development dispatcher never loads or compiles executable bytes");
        var bundle = ReadSource(context, "tools/NativeMaintenanceBundle.cs");
        Contains(bundle, new[] { "PackageRoot.Equals(GameRoot, StringComparison.OrdinalIgnoreCase)", """PackageRoot.StartsWith(GameRoot + "\\", StringComparison.OrdinalIgnoreCase)""",
            """GameRoot.StartsWith(PackageRoot + "\\", StringComparison.OrdinalIgnoreCase)""" }, "Reject overlapping source/game roots");
        var project = XDocument.Load(Path.Combine(context.Root, "src/BetterAstralParty.csproj"));
        foreach (var compile in project.Descendants("Compile").Where(row => row.Attribute("Include") != null)) {
            if (compile.Attribute("Include")!.Value == "$(BapVersionOverrideSource)") {
                var group = compile.Parent;
                Check(compile.Attribute("Link")?.Value == "Plugin.cs" && group != null && group.Attribute("Condition")?.Value == "'$(BapVersionOverrideSource)' != ''"
                    && group.Elements("Compile").Any(row => row.Attribute("Remove")?.Value == "Plugin.cs"), "Version override substitutes only the generated Plugin.cs in its conditional item group");
                continue;
            }
            var path = Path.GetFullPath(Path.Combine(context.Root, "src", compile.Attribute("Include")!.Value));
            Check(path.StartsWith(context.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path), "Linked product input remains project scoped: " + compile.Attribute("Include")!.Value);
        }
    }

    private static void BootstrapAllocation(DevContext context, string stage)
    {
        var source = ReadSource(context, "Developer.cmd");
        const string ready = ":bap_bootstrap_ready";
        var end = source.IndexOf("\n" + ready, StringComparison.Ordinal);
        Check(end >= 0, "Developer bootstrap has a bounded allocator before compilation");
        var directory = Path.Combine(stage, "bootstrap-allocation");
        var artifacts = Path.Combine(directory, "artifacts"); Directory.CreateDirectory(artifacts);
        File.WriteAllText(Path.Combine(directory, "allocate.cmd"), source[..(end + 1)] + ready + "\r\nexit /b 0\r\n", System.Text.Encoding.ASCII);
        var environment = new Dictionary<string, string?> { ["RANDOM"] = "7" };
        var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var caller = Environment.GetEnvironmentVariable("BAP_DEVELOPER_BOOTSTRAP");
        for (var index = 1; index <= 3; index++)
            Check(context.Run(command, new[] { "/d", "/c", "allocate.cmd" }, cwd: directory, env: environment) == 0
                && Directory.Exists(Path.Combine(artifacts, "developer-bootstrap-7-7-" + index)), "Bootstrap allocation survives repeated random-name collisions: " + index);
        for (var index = 4; index <= 32; index++) Directory.CreateDirectory(Path.Combine(artifacts, "developer-bootstrap-7-7-" + index));
        Check(context.Run(command, new[] { "/d", "/c", "allocate.cmd" }, cwd: directory, env: environment) == 1
            && Directory.GetDirectories(artifacts).Length == 32, "Bootstrap allocation fails boundedly without replacing existing directories");
        Check(Environment.GetEnvironmentVariable("BAP_DEVELOPER_BOOTSTRAP") == caller, "Bootstrap allocator preserves caller process environment");
    }

    private static void ScratchCleanup(string stage)
    {
        var fixture = Path.Combine(stage, "scratch-cleanup"); var scratch = Path.Combine(fixture, "ct-scratch"); var outside = Path.Combine(fixture, "outside");
        Directory.CreateDirectory(Path.Combine(scratch, "nested")); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "kept.txt"), "outside");
        var readOnly = Path.Combine(scratch, "nested", "read-only.txt"); File.WriteAllText(readOnly, "scratch"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        CompatibilitySnapshot.FixtureJunction(fixture, Path.Combine(scratch, "linked"), outside);
        DeleteScratch(scratch);
        Check(!Directory.Exists(scratch) && File.ReadAllText(Path.Combine(outside, "kept.txt")) == "outside",
            "Passing-check scratch is removed with its read-only files, and a junction inside it is removed without touching its target");
    }

    private static void SettingsInput(DevContext context)
    {
        using var native = Native(context);
        var coordinate = native.MainModule.GetType("FairyGUI.GObject").Methods.Single(method => method.Name == "LocalToGlobal" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "UnityEngine.Vector2");
        Check(coordinate.Parameters[0].ParameterType.FullName == "UnityEngine.Vector2" && coordinate.ReturnType.FullName == "UnityEngine.Vector2", "Diagnostic coordinate ABI");
        // In-battle menu entry: both native windows, their settings buttons and the clone path the mod uses.
        foreach (var (window, pane) in new[] { ("UI.SettingInBattleWindow", "UI.UISettingInBattleWindow"), ("UI.SinglePlayerSettingInBattleWindow", "UI.UISinglePlayerSettingInBattleWindow") })
            Check(native.MainModule.GetType(window) != null && native.MainModule.GetType(pane)?.Fields.Any(field => field.Name == "btn_OpenSetting" && field.FieldType.FullName == "FairyGUI.GButton") == true, "In-battle menu window and settings button: " + window);
        Check(native.MainModule.GetType("UI.UISettingInBattleWindow").Fields.Any(field => field.Name == "btn_AudienceOpenSetting"), "Spectator settings button");
        // Settings search (2026-11-02): the native input field it types into, the glyph positions its marks follow, the IME
        // text it reads, and the focus that keeps the slider arrow keys off while typing.
        var input = native.MainModule.GetType("FairyGUI.GTextInput");
        var textField = native.MainModule.GetType("FairyGUI.TextField");
        var glyph = textField.NestedTypes.Single(type => type.Name == "CharPosition");
        Check(input.BaseType.FullName == "FairyGUI.GTextField" && new[] { "promptText", "maxLength", "caretPosition" }.All(name => input.Properties.Any(property => property.Name == name))
            && textField.Methods.Any(method => method.Name == "EnableCharPositionSupport" && method.Parameters.Count == 0)
            && textField.Properties.Any(property => property.Name == "charPositions" && property.PropertyType.FullName == "System.Collections.Generic.List`1<FairyGUI.TextField/CharPosition>")
            && glyph.Fields.Any(field => field.Name == "offsetX" && field.FieldType.FullName == "System.Single") && glyph.Fields.Any(field => field.Name == "width" && field.FieldType.FullName == "System.Int16")
            && native.MainModule.GetType("FairyGUI.Stage").Properties.Any(property => property.Name == "focus" && property.PropertyType.FullName == "FairyGUI.DisplayObject")
            && Il(native.MainModule.GetType("FairyGUI.InputTextField").Methods.Single(method => method.Name == "UpdateText")).Contains("UnityEngine.Input::get_compositionString()", StringComparison.Ordinal),
            "Native input field, glyph positions, IME composition and focus the settings search uses");
        // A name mark sits inside the native switch button, whose SetState recolours IColorGear children; a GComponent is not one.
        Check(Il(native.MainModule.GetType("FairyGUI.GButton").Methods.Single(method => method.Name == "SetState")).Contains("FairyGUI.IColorGear::set_color", StringComparison.Ordinal)
            && native.MainModule.GetType("FairyGUI.GComponent").Interfaces.All(type => type.InterfaceType.Name != "IColorGear")
            && native.MainModule.GetType("FairyGUI.GObject").Methods.Any(method => method.Name == "RemoveFromParent" && method.Parameters.Count == 0),
            "Search marks are components the button's state tint leaves alone, re-added behind their label");
        var search = ReadSource(context, "src/ModUi.Search.cs");
        Contains(search, new[] { "text = text.Insert(Math.Clamp(_searchInput.Get<int>(\"caretPosition\"), 0, text.Length), composing);", "if (CharPositions.Add(field.Pointer)) field.Call(\"EnableCharPositionSupport\");",
            "Controls[clear.Pointer] = (clear, \"SearchClear\");", "SettingsSearch.Fit(sentence, _words, room,", "mark = NativeUi.Component(parent, 10, 10);", "mark.Call(\"RemoveFromParent\");", "var blocked = slider && SliderBlocked[action].Get<bool>(\"visible\");", "MenuLayout.Arrange(MenuLayout.Sections.Select((section, index) => (index, section.Actions.Where(shown.Contains).ToArray())), true, grow, notes);" },
            "Settings search reads the field with its IME text, marks native glyph positions and lays out one list");
        Contains(ReadSource(context, "src/ModUi.cs"), new[] { "Compatibility.TogglePreview(previewed.Action);", "if (updateNotice && _general) JumpToUpdates();" },
            "Development builds preview a setting's unavailable state; the first open after the red point scrolls to the update rows");
        Contains(MethodFragment(ReadSource(context, "src/ModUi.cs"), "internal static void OpenUpdateSettings"), new[] { "EndSearch();\n            JumpToUpdates();" },
            "Update Settings from the update notice scrolls to the update rows");
        Contains(ReadSource(context, "src/ModUi.cs"), new[] { "TickSearch();", "|| Typing()) return;", "case \"SearchClear\": SetQuery(\"\"); return;", "EndSearch();", "Place(slider, action);" },
            "Settings search is polled, keeps the slider arrow keys while typing and ends on tabs and opens");
        Check(native.MainModule.GetType("FairyGUI.GObject").Methods.Any(method => method.Name == "get_resourceURL" && method.ReturnType.FullName == "System.String")
            && native.MainModule.GetType("FairyGUI.UIPackage").Methods.Any(method => method.Name == "CreateObjectFromURL" && method.Parameters.Count == 1), "Native button clone by resource URL");
        Contains(ReadSource(context, "src/BattleMenuUi.cs"), new[] { "\"SettingInBattleWindow\", \"SinglePlayerSettingInBattleWindow\"", "\"btn_OpenSetting\", \"btn_AudienceOpenSetting\"", "ModUi.OpenFromBattleMenu()" }, "Mod settings entry uses audited in-battle windows");
        NoPattern(ReadSource(context, "src/BattleMenuUi.cs"), @"onClick|AddListener|item\.(?:Set|Call)\(|template\.(?:Set|Call)\(", "In-battle entry adds no native listeners and changes no native buttons");
        // Old Version Replays: the record refresh grays the play button exactly when the version differs, the button's handler
        // refuses only while it is grayed, and the play request itself never checks the version.
        var account = native.MainModule.GetType("UI.AccountInfoWindow");
        var refresh = Il(account.Methods.Single(method => method.Name == "RefreshRecordOperation"));
        Check(refresh.Contains("BattleShortRecord::IsValidVersion()", StringComparison.Ordinal) && refresh.Contains("btn_ReplayPlay", StringComparison.Ordinal)
            && refresh.Contains("GObject::set_grayed", StringComparison.Ordinal), "Record refresh grays the replay play button by version");
        Check(account.NestedTypes.SelectMany(type => type.Methods).Any(method => method.Name.StartsWith("<RefreshRecordOperation>b__", StringComparison.Ordinal)
            && Il(method) is var click && click.Contains("btn_ReplayPlay", StringComparison.Ordinal) && click.Contains("GObject::get_grayed", StringComparison.Ordinal)
            && click.Contains("AccountInfoWindow::RequestPlayReplay", StringComparison.Ordinal) && !click.Contains("IsValidVersion", StringComparison.Ordinal)), "Replay play handler refuses only while grayed");
        Check(account.NestedTypes.Where(type => type.Name.StartsWith("<RequestPlayReplay>", StringComparison.Ordinal)).SelectMany(type => type.Methods).All(method => !Il(method).Contains("IsValidVersion", StringComparison.Ordinal)),
            "Replay play request has no version check of its own");
        Contains(ReadSource(context, "src/ReplayUi.cs"), new[] { "Plugin.OldReplays.Value", "\"AccountInfoWindow\"", ".Field(\"btn_ReplayPlay\")", "button.Set(\"grayed\", false);", "button.Set(\"grayed\", true);", "Compatibility.Block(Feature, ex, Clear)" },
            "Old Version Replays clears only the version gray and restores it on the same record");
        // Its safety: one throwing tween callback stops TweenManager.Update for the session, and FairyGUI's own static switches
        // catch callbacks; a leftover replay state ends through the session's own Clear, never the global task cancel.
        var tweener = native.MainModule.GetType("FairyGUI.GTweener");
        var tweenUpdate = native.MainModule.GetType("FairyGUI.TweenManager").Methods.Single(method => method.Name == "Update");
        Check(!tweenUpdate.Body.HasExceptionHandlers && Il(tweenUpdate).Contains("GTweener::_Update()", StringComparison.Ordinal)
            && Il(tweener.Methods.Single(method => method.Name == "CallCompleteCallback")).Contains("GTween::catchCallbackExceptions", StringComparison.Ordinal)
            && new[] { "FairyGUI.GTween", "FairyGUI.Timers" }.All(name => native.MainModule.GetType(name).Fields.Any(field => field.Name == "catchCallbackExceptions" && field.IsStatic && field.FieldType.FullName == "System.Boolean")),
            "A throwing tween callback stops TweenManager.Update unless the static catch switches are on");
        var session = native.MainModule.GetType("GameLogic.Replay.ReplaySession");
        var exit = Il(native.MainModule.GetType("GameLogic.ReplayLogic").Methods.Single(method => method.Name == "ExitReplay"));
        var stop = Il(session.Methods.Single(method => method.Name == "StopPlayback"));
        Check(exit.Contains("ReplaySession::StopPlayback()", StringComparison.Ordinal) && exit.Contains("ReplaySession::Clear()", StringComparison.Ordinal)
            && stop.Contains("DelaySignalManager::CancelAllTask()", StringComparison.Ordinal) && stop.Contains("ReplayPlayer::Stop()", StringComparison.Ordinal)
            && Il(session.Methods.Single(method => method.Name == "Clear")).Contains("RoomController::Dispose()", StringComparison.Ordinal)
            && session.Methods.Any(method => method.Name == "get_CurrentPlayer") && session.Methods.Any(method => method.Name == "get_State"),
            "Replay exit is the player's stop, the global task cancel and the session clear, which disposes the room");
        var replaySource = ReadSource(context, "src/ReplayUi.cs");
        Contains(replaySource, new[] { "RuntimeObject.SetStaticField(tween, name, true);", "RuntimeObject.SetStaticField(timers, name, timersValue);",
            "finally { CatchCallbacks(false); }", "!GameUi.NotificationHome().Eligible", "session.Get(\"CurrentPlayer\")?.Call(\"Stop\");", "session.Call(\"Clear\");" },
            "Old Version Replays holds the catch switches only while ON and ends a leftover replay state only at an idle home");
        NoPattern(System.Text.RegularExpressions.Regex.Replace(replaySource, @"//[^\n]*", ""), @"ExitReplay|CancelAllTask|FinishGame|ExitRunningRoom|CloseBattleUI",
            "Old Version Replays never cancels game-wide tasks or leaves a room");
        Contains(ReadSource(context, "src/RuntimeObject.cs"), new[] { "(IL2CPP.il2cpp_field_get_flags(field) & 0x10) == 0", "IL2CPP.il2cpp_runtime_class_init(klass);", "IL2CPP.il2cpp_field_static_set_value(field, &value);" },
            "Static field writes refuse instance fields and run the class initializer first");
        // Settings rows reuse native Common art: the re-roll arrows and stopwatch on the update row's capsule and the
        // large check on the chosen dropdown line.
        var commonPackage = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(context.Root, ".research/ui-assets/Common_fui.bytes")));
        Check(commonPackage.Contains("重新随机", StringComparison.Ordinal) && commonPackage.Contains("时间", StringComparison.Ordinal) && commonPackage.Contains("打勾（大）", StringComparison.Ordinal), "Native Common re-roll arrows and check art");
        var source = ReadSource(context, "src/ModUi.cs");
        Contains(source, new[] { "NativeUi.Icon(parts.Capsule, \"重新随机\"", "NativeUi.Icon(parts.Capsule, \"时间\"", "NativeUi.Icon(_dropdown, \"打勾（大）\"", "ModText.CheckPills(Plugin.Updates.Result, DateTimeOffset.Now)", "new Pills(first, second), CapsuleMode.Check" }, "Update row and dropdown use native art");
        // Channel switch draft 2 (2026-11-01): installed release, arrow and target pills; a switch waiting for a release, or still checked, keeps its row with an inert capsule.
        Contains(source, new[] { "Arrow: true, Pending: target == null", "confirmable ? CapsuleMode.Switch : waiting ? CapsuleMode.Wait : CapsuleMode.Busy", ": Plugin.Automatic.Status == AutomaticUpdateStatus.TransitionRequired ? \"UpdateSwitch\"" }, "Channel switch row shows the route and its state");
        // Home button portrait (2026-11-01): Teru's 매혹적인 가희 card on her Character colour, baked with draft A's antialiased clip; the stencil stays as the fallback.
        Contains(ReadSource(context, "src/HomePortrait.cs"), new[] { "private const string Art = \"UT_Hero_Card_124_02\";", "foreach (var name in new[] { \"ProfileColor\", \"NameBGColor\", \"CardFrontColor\" })",
            "config.HasGetter(name)", "Graphics.Blit(native, target,", "RenderTexture.ReleaseTemporary(target);", "pixels[alpha] = (byte)MathF.Round(pixels[alpha] * coverage);",
            "Plugin.Diagnostics.Error(\"HomePortrait\", ex); // The stencilled portrait stays.", "_portrait!.Set(\"visible\", false);" }, "Home portrait bakes its clip and keeps the stencil fallback");
        Contains(ReadSource(context, "src/GameUi.cs"), new[] { "AddRoundedRect(art, 343, 166, 0, HomePortrait.Plate());", "HomePortrait.Build(art);", "HomePortrait.Tick();" }, "Home menu builds and bakes the portrait");
        // Stall watch (2026-11-01): a background thread names the segment the main thread is stuck in; every ModUi.Tick exit hands it back to the game.
        Contains(ReadSource(context, "src/StallWatch.cs"), new[] { "IsBackground = true", "if (seconds < Limit) continue;", "Plugin.Diagnostics.Flush();", "main.stall ended after" }, "Stall watch records only");
        Contains(source, new[] { "try { TickCore(); }", "finally { StallWatch.Enter(-1); }", "StallWatch.Enter(index < DiagnosticLog.TickFeatures.Length ? index : -1);" }, "Every tick segment feeds the stall watch");
        var unityLog = ReadSource(context, "src/UnityLogTap.cs");
        Contains(unityLog, new[] { "if (!Plugin.Version.Contains(\"-dev.\", StringComparison.Ordinal)) return;", "typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]",
            "if (text == null || !Plugin.Diagnostics.IsRecording) return;", "if (_lines >= MaxLines || text == _last) return;", "catch { }" }, "Unity log tap runs only in development builds while diagnostics record, bounded and failure-silent");
        NoPattern(unityLog, @"logMessageReceived|ClassInjector|DelegateSupport|ILogHandler", "Unity log tap never registers a managed delegate or injected class");
        Contains(ReadSource(context, "src/Plugin.cs"), new[] { "UnityLogTap.Register();" }, "Unity log tap registers with the startup patches");
        Contains(source, new[] { "ReplayUi.Tick();","case \"UpdateChannel\": Plugin.UpdateChannel.Value = value; Plugin.Updates.CheckTransition(DateTimeOffset.UtcNow); break;",
            "(Plugin.AutomaticChecks.Value || Plugin.Updates.TransitionCheckPending)" }, "Explicit channel selection remains manual when periodic discovery is OFF");
        Contains(source, new[] { "if (!Plugin.Diagnostics.IsRecording) return;", "TraceInput(\"down\", current)", "TraceInput(\"up\", current)", "TraceInput(\"applied\", _pressed)",
            "var enabled = ToggleEnabled(action)", "// Detailed input trace reads no extra native controls, captions or bounds.", "Plugin.Diagnostics.Error(\"SettingsInputTrace\", ex)",
            "\"FieldBuffs\" => Plugin.FieldBuffs.Value", "\"FieldZoom\" => Plugin.FieldZoom.Value", "\"MatchFocus\" => Plugin.MatchFocus.Value",
            "MenuLayout.StateHelp(action, ToggleEnabled(action), mode)", "case \"MatchFocus\": Plugin.MatchFocus.Value = !Plugin.MatchFocus.Value; break;",
            "case \"FieldZoom\": Plugin.FieldZoom.Value = !Plugin.FieldZoom.Value; break;" }, "Settings input");
        Contains(source, new[] { "!hit!.Get<bool>(\"grayed\") && !Locked(Controls[current].Action)) Apply(", "slider.Set(\"touchable\", active);",
            "row.Button.Pointer == _hovered && !Locked(action)", "button.Set(\"alpha\", Locked(action) ? 0.5f : 1f);" },
            "Options under an OFF parent or tuned switch dim, ignore clicks and slider drags, and keep their values");
        var trace = Regex.Match(source, @"(?s)private static void TraceInput\(.*?(?=    private static bool ToggleEnabled)");
        Check(trace.Success && trace.Value.Contains("Plugin.Diagnostics.Write(", StringComparison.Ordinal) && !Regex.IsMatch(trace.Value, @"(?:\.Call\(|\.Get<|\.Get\(|\.Field\()"), "Sampled trace reads no extra native objects");
        var features = new[] { "Enabled", "Details", "KoMinimum", "CardPopups", "BattleStatus", "ShushuShield", "FieldBuffs", "Names", "HandLayout", "FieldZoom",
            "ShushuShieldRestrictions", "AutoThanks", "CharacterRoles", "CharacterGuide", "IndicatorFade", "IndicatorHide", "CounterSound", "CounterAnimation", "PingFocus", "FinalDamage", "DiceLock", "EncounterAdvice", "DefenseAdvice", "CardAdvice", "ShootingStarShow", "KoCardPreview", "KoSpectator", "StatusHand", "StatusEffects", "CounterIcons", "ShushuMaster", "PlayerTab", "PlayerTabHand", "PlayerTabRole", "PlayerTabSkill", "OldReplays" };
        for (var row = 0; row < features.Length; row++) Check(source.Contains($"AddToggle(\"{features[row]}\", 440, MenuLayout.ToggleY({row}));", StringComparison.Ordinal), "Feature right column: " + features[row]);
        var general = new[] { "UpdateChannel", "Update", "UpdateDownload", "AutoUpdate", "UpdateAuth", "UpdateSignOut", "DiagnosticsOpen", "DiagnosticsCollect",
            "SuppressNotices", "UpdateNotifications", "SettingsAutoKeep", "SettingsSave", "SettingsLoad", "SettingsFolder", "SettingsReset" };
        for (var row = 0; row < general.Length; row++) Check(source.Contains($"AddToggle(\"{general[row]}\", 440, MenuLayout.ToggleY({row + 7}));", StringComparison.Ordinal), "General setting row: " + general[row]);
        Contains(source, new[] { "AddToggle(\"MatchFocus\", 440, MenuLayout.ToggleY(4));", "foreach (var action in MenuLayout.Sliders)", "AddToggle(\"Diagnostics\", 440, 190);", "AddToggle(\"MuteUnfocused\", 440, 255);",
            "AddToggle(\"InputAttention\", 440, 320);", "MoveIntoSettings(button, MenuLayout.SettingY(action)", "toggle.Call(\"SetSize\", width, height);",
            "if (!MenuLayout.FixedControl(action)) MoveIntoSettings(button, MenuLayout.SettingY(action)", "MoveIntoSettings(Labels[action], MenuLayout.SettingY(action) + MenuLayout.SliderLabelOffsetY(action));", "Labels[\"Saved\"] = NativeUi.Label(_sheet, \"\", 675, MenuLayout.FooterY + 7, 315, 30, 18);",
            "Labels[\"HelpTitle\"] = NativeUi.Label(helpArea, \"\", 18, 14, 324, 40, 27);", "Labels[\"HelpTitle\"].Set(\"singleLine\", true);", "Labels[\"HelpTitle\"].Set(\"autoSize\", 3);",
            "Labels[\"Saved\"].Set(\"singleLine\", true);", "Labels[\"Saved\"].Set(\"autoSize\", 3);",
            "var saved = Time.unscaledTime - _changedAt < 1.6f ? ModText.Text(\"✓ 저장됨\") : $\"BetterAstralParty {Plugin.Version}\";",
            "if (_savedText != saved) { _savedText = saved; Labels[\"Saved\"].Set(\"text\", saved); }" }, "Settings geometry and footer");
        foreach (var label in new[] { "Saved", "HelpTitle" }) Check(Regex.Matches(source, "Labels\\[\"" + label + "\"\\]\\.Set\\(\"(?:singleLine|autoSize)\"").Count == 2, label + " fitting initialized once");
        Contains(ReadSource(context, "src/MenuLayout.cs"), new[] { "ToggleY(int row) => 125 + 65 * row;", "ToggleHeight = 60;", "Math.Min((width - 48f) / Width, (height - 48f) / windowHeight)" }, "Menu screen fit");
        foreach (var (setterName, parameter) in new[] { ("set_autoSize", "FairyGUI.AutoSizeType"), ("set_singleLine", "System.Boolean") }) {
            var method = native.MainModule.GetType("FairyGUI.GTextField").Methods.Single(row => row.Name == setterName);
            Check(method.IsPublic && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == parameter && method.ReturnType.FullName == "System.Void", "Footer setter ABI " + setterName);
        }
        // Every literal Shape drawing call in the plugin must match a native overload by name and argument count.
        var shape = native.MainModule.GetType("FairyGUI.Shape");
        foreach (var file in Directory.GetFiles(Path.Combine(context.Root, "src"), "*.cs"))
            foreach (Match call in Regex.Matches(File.ReadAllText(file), @"Get\(""shape""\)!?\.Call\(""(?<name>\w+)""(?<args>[^;]*)\);"))
            {
                int depth = 0, commas = 0;
                foreach (var c in call.Groups["args"].Value) { if (c == '(') depth++; else if (c == ')') depth--; else if (c == ',' && depth == 0) commas++; }
                var name = call.Groups["name"].Value;
                Check(shape.Methods.Any(method => method.Name == name && method.Parameters.Count == commas), $"Native Shape.{name}/{commas} exists ({Path.GetFileName(file)})");
            }
        var shrink = native.MainModule.GetType("FairyGUI.AutoSizeType").Fields.Single(row => row.Name == "Shrink"); Check(shrink.HasConstant && (int)shrink.Constant == 3, "Native Shrink enum");
        var plugin = ReadSource(context, "src/Plugin.cs"); foreach (var setting in new[] { "MatchFocus", "FieldZoom" }) Check(Regex.IsMatch(plugin, setting + @"\s*=\s*Config\.Bind\([^,]+,[^,]+,\s*true\s*,"), "Fresh default ON: " + setting);
        SettingsPreferenceInventory(context, source, plugin);
        var focus = source.IndexOf("MatchFocus.Tick();", StringComparison.Ordinal); var pvp = source.IndexOf("PvpSafety.Poll()", StringComparison.Ordinal);
        var notify = source.IndexOf("UpdateNotificationUi.Tick();", StringComparison.Ordinal); var zoom = source.IndexOf("FieldZoomUi.Tick();", notify, StringComparison.Ordinal);
        Check(focus >= 0 && focus < pvp && zoom > pvp && zoom > notify && zoom < source.IndexOf("CardTargetButtonsUi.Tick();", StringComparison.Ordinal), "Focus/PvP/zoom/card tick order");
        foreach (var pattern in new[] { @"(?s)Action\[\] PvpCleanup\s*=\s*\{.*?\};", @"(?s)internal static void FailUi\(.*?(?=    private static RuntimeObject Button)" }) Check(Regex.Match(source, pattern).Value.Contains("FieldZoomUi.Clear", StringComparison.Ordinal), "Camera cleanup on PvP/core failure");
        var blocked = Regex.Match(source, @"(?s)if \(!Compatibility\.Allowed\(\""CoreUi\""\)\)\s*\{.*?\n        \}").Value;
        Contains(blocked, new[] { "try { FieldZoomUi.Clear(); }", "catch (Exception ex)", "return;" }, "Blocked core camera cleanup retries");
        Contains(ReadSource(context, "src/FieldZoomUi.cs"), new[] { "!Plugin.FieldZoom.Value || PvpSafety.Suspended", "Compatibility.Block(\"FieldZoom\", ex, Clear)" }, "Independent field zoom breaker");
        Check(Regex.IsMatch(ReadSource(context, "src/Compatibility.cs"), @"feature is not \([^\)]*\""MatchFocus\""[^\)]*\)"), "Focus independent of core UI breaker");
        var project = XDocument.Load(Path.Combine(context.Root, "src/BetterAstralParty.csproj"));
        var excluded = project.Descendants("Compile").Select(row => row.Attribute("Remove")?.Value ?? "").SelectMany(value => value.Split(';')).ToArray();
        foreach (var file in new[] { "MatchFocus.cs", "MatchFocusState.cs", "WindowsMatchFocus.cs", "FieldZoomUi.cs", "FieldZoomGeometry.cs" }) Check(File.Exists(Path.Combine(context.Root, "src", file)) && !excluded.Contains(file, StringComparer.Ordinal), "Feature compiled: " + file);
        Check(project.Descendants("EnableDefaultCompileItems").FirstOrDefault()?.Value != "false", "Default feature inclusion");
    }

    private static void SettingsPreferenceInventory(DevContext context, string source, string plugin)
    {
        var added = new[] { "ShushuShieldRestrictions", "AutoThanks", "CharacterRoles", "CharacterGuide", "IndicatorFade", "IndicatorHide", "DiceLock", "CounterSound", "CounterAnimation", "PingFocus", "FinalDamage", "SuppressNotices", "UpdateNotifications", "PlayerTabSkill", "OldReplays" };
        var layout = ReadSource(context, "src/MenuLayout.cs"); var text = ReadSource(context, "src/ModText.cs");
        var enabled = MethodFragment(source, "private static bool ToggleEnabled"); var apply = MethodFragment(source, "private static void ApplyCore");
        var help = layout[layout.IndexOf("internal static (string Title, string Body) Help(", StringComparison.Ordinal)..];
        var state = layout[layout.IndexOf("internal static string StateHelp(", StringComparison.Ordinal)..layout.IndexOf("internal static (string Title, string Body) Help(", StringComparison.Ordinal)];
        var groups = layout[layout.IndexOf("internal static bool GeneralControl(", StringComparison.Ordinal)..layout.IndexOf("internal static string StateHelp(", StringComparison.Ordinal)];
        const string literal = "\"(?:[^\"\\\\]|\\\\.)*\"";
        var translations = Regex.Matches(text, "\\[(?<key>" + literal + ")\\]\\s*=\\s*(?<value>" + literal + ")")
            .ToLookup(match => match.Groups["key"].Value, match => match.Groups["value"].Value, StringComparer.Ordinal);
        foreach (var setting in added) {
            Check(Regex.IsMatch(plugin, @"ConfigEntry<bool>\s+" + setting + @"\s*\{"), "Boolean preference declared: " + setting);
            var binding = Regex.Match(plugin, @"\b" + setting + @"\s*=\s*Config\.Bind\(" + literal + @",\s*" + literal + @",\s*(?<default>[^,\r\n]+),\s*(?<description>" + literal + @")\)");
            var expectedDefault = setting == "FinalDamage" ? "Enabled.Value" : setting == "ShushuShieldRestrictions" ? "ShushuShield.Value" : setting == "OldReplays" ? "false" : "true";
            Check(binding.Success && binding.Groups["default"].Value.Trim() == expectedDefault, "Fresh/migrated preference default retained: " + setting);
            Check(binding.Groups["description"].Value.Contains(" / ", StringComparison.Ordinal), "Persisted setting has Korean and English guidance: " + setting);
            Check(enabled.Contains("\"" + setting + "\" => Plugin." + setting + ".Value", StringComparison.Ordinal), "Toggle reads its own saved preference: " + setting);
            Check(Regex.IsMatch(apply, "case \\\"" + setting + "\\\":\\s*Plugin\\." + setting + "\\.Value\\s*=\\s*!Plugin\\." + setting + "\\.Value;"), "Toggle writes its own saved preference: " + setting);
            var row = Regex.Match(help, "\"" + setting + "\"\\s*=>\\s*\\((?<title>" + literal + "),\\s*(?<body>" + literal + ")\\)");
            Check(row.Success, "Preference has its own title and help: " + setting);
            foreach (var field in new[] { "title", "body" }) {
                var key = row.Groups[field].Value;
                Check(translations[key].Count() == 1 && translations[key].Single() != key && translations[key].Single().Length > 2, "Korean/English " + field + " resolves exactly once: " + setting);
            }
            Check(state.Contains("\"" + setting + "\"", StringComparison.Ordinal), "Disabled preference has state-specific guidance: " + setting);
            Check(groups.Contains("\"" + setting + "\"", StringComparison.Ordinal) == (setting is "SuppressNotices" or "UpdateNotifications"), "Preference belongs to the intended scrolling tab: " + setting);
        }
        Contains(source, new[] { "ModText.Text(MenuLayout.Help(action).Title)", "MenuLayout.StateHelp(action, ToggleEnabled(action), mode)", "RowText(button, action, ModText.Text(title), state == null ? null : ModText.Text(state));" }, "Preference labels and help retain the localization path");
        foreach (var body in Regex.Matches(state, literal)
            .Where(match => !state[(match.Index + match.Length)..].TrimStart().StartsWith("=>", StringComparison.Ordinal))
            .Select(match => match.Value).Where(value => value.Any(c => c is >= '가' and <= '힣')).Distinct(StringComparer.Ordinal))
            Check(translations[body].Count() == 1 && translations[body].Single() != body, "State-specific guidance has one English translation: " + body);
        foreach (var padding in new[] { "이 기능을 끕니다", "다른 설정", "별도" })
            Check(!state.Contains(padding, StringComparison.Ordinal), "State help omits generic padding: " + padding);
        var sections = Regex.Matches(layout, "\\((?<title>" + literal + "),\\s*(?:true|false),\\s*new\\[\\]");
        Check(sections.Count > 0, "Settings categories have visible title data");
        foreach (Match section in sections) {
            var title = section.Groups["title"].Value;
            Check(translations[title].Count() == 1 && translations[title].Single() != title, "Category heading has one English translation: " + title);
        }
        Contains(layout, new[] { "\"UpdateChannel\" => new[] { \"Stable\", \"PreRelease\", \"Beta\" }", "\"UpdateChannel\" => value switch { \"PreRelease\" => \"프리 릴리즈\", \"Beta\" => \"비공개 베타\", _ => \"릴리즈\" }" }, "Three-channel dropdown preserves public/private order and Korean labels");
        foreach (var (channel, label, english) in new[] { ("Stable", "릴리즈", "Release"), ("PreRelease", "프리 릴리즈", "Pre-release"), ("Beta", "비공개 베타", "Private Beta") }) {
            Check(text.Contains("\"" + channel + "\" => Text(\"" + label + "\")", StringComparison.Ordinal) && translations["\"" + label + "\""].SingleOrDefault() == "\"" + english + "\"", "Channel display is localized without changing its stored identity: " + channel);
        }
        Contains(ReadSource(context, "src/Compatibility.cs"), new[] { "if (feature is \"CounterSound\" or \"CounterAnimation\" or \"CounterIcons\") return Reason(\"CounterIndicators\");" }, "Counter preference errors follow the isolated counter breaker rather than public battle rows");
        Contains(ReadSource(context, "src/Compatibility.cs"), new[] { "\"EncounterAdvice\" or \"DefenseAdvice\" or \"CardAdvice\" or \"KoCardPreview\" or \"KoSpectator\") return Reason(\"Enabled\");",
            "if (feature is \"ShootingStarShow\") return Reason(\"ShootingStar\") ?? Reason(\"Enabled\");", "if (feature is \"StatusHand\" or \"StatusEffects\") return Reason(\"BattleStatus\");",
            "if (feature is \"PlayerTabHand\" or \"PlayerTabRole\" or \"PlayerTabSkill\") return Reason(\"PlayerTab\");" }, "Part switches show unavailable while the breaker of the feature that draws them has tripped");
    }

    private static void SettingsScroll(DevContext context)
    {
        using var native = Native(context);
        var methods = new Dictionary<string, string[]> {
            ["FairyGUI.ScrollPane"] = new[] { "set_mouseWheelEnabled", "set_bouncebackEffect", "set_scrollStep", "ScrollTop", "SetPosY", "get_scrollingPosY", "get_isDragged", "get_viewHeight", "get_contentHeight" },
            ["FairyGUI.GList"] = new[] { "set_autoResizeItem", "set_layout" }, ["FairyGUI.Relations"] = new[] { "ClearAll" }, ["FairyGUI.GComponent"] = new[] { "RemoveChildren", "EnsureBoundsCorrect" }
        };
        foreach (var row in methods) foreach (var name in row.Value) Check(native.MainModule.GetType(row.Key).Methods.Any(method => method.Name == name), "Native scroll API: " + row.Key + "." + name);
        var list = native.MainModule.GetType("FairyGUI.GList"); Check(list.Fields.Any(field => field.Name == "selectionMode"), "List selection field");
        Check(list.Fields.Single(field => field.Name == "scrollItemToViewOnClick").FieldType.FullName == "System.Boolean", "Click auto scroll Boolean");
        var click = list.Methods.Single(method => method.Name == "__clickItem").Body.Instructions;
        var guard = click.Where(instruction => instruction.Operand is FieldReference field && field.Name == "scrollItemToViewOnClick").ToArray();
        var calls = click.Where(instruction => instruction.Operand is MethodReference method && method.Name == "ScrollToView").ToArray();
        Check(guard.Length == 1 && guard[0].Next.OpCode.Code is Code.Brfalse or Code.Brfalse_S, "Native click scroll switch guard");
        Check(calls.Length == 1 && ((Instruction)guard[0].Next.Operand).Offset > calls[0].Offset, "Disabled click scroll bypasses ScrollToView");
        var source = ReadSource(context, "src/ModUi.cs");
        foreach (var phase in new[] { "open begin", "open complete", "close begin", "close complete", "select", "applied" }) Check(source.Contains("settingsDropdown " + phase, StringComparison.Ordinal), "Dropdown breadcrumb: " + phase);
        Contains(source, new[] { "if (_dropdown != null) { HandleDropdown(); return; }", "if (_dropdown != null) CloseDropdown(); else if (_query.Length > 0) SetQuery(\"\"); else ClosePanel();",
            "if (!path.Any(item => item.Pointer == _dropdown!.Pointer)) CloseDropdown();", "if (current == IntPtr.Zero || current != _pressed)", "if (!Compatibility.Allowed(_dropdownAction!))",
            "Plugin.Language.Value = value", "Plugin.GroupHand.Value = value != \"Native\"", "Plugin.InputAttention.Value = value", "MenuLayout.DropdownY", "var chosen = options[i] == selected;",
            "SetField(\"selectionMode\", 3)", "MenuLayout.InSettings(local.x, local.y)", "if (scrolling) { _pressed = IntPtr.Zero; hit = null; }", "MenuLayout.ContentHeight(bottom)",
            "GameUi.Dispose(scrollTemplate)", "MenuLayout.FooterY + 7", "_settingsView.SetField(\"scrollItemToViewOnClick\", false)",
            "var scrollPosition = reopen ? _settingsScroll!.Get<float>(\"scrollingPosY\") : 0;", "_settingsScroll!.Call(\"SetPosY\", scrollPosition, false)" }, "Settings scroll lifecycle");
        Check(!source.Contains("HelpHint", StringComparison.Ordinal) && !source.Contains("Cycle.", StringComparison.Ordinal) && !source.Contains("ModText.NextMode", StringComparison.Ordinal), "No obsolete cyclic settings path");
        Contains(source, new[] { "if (!child.Get<bool>(\"visible\")) continue;", "bottom = Math.Max(bottom, _settingsContent.Call(\"GlobalToLocal\", global)!.Value<Vector2>().y);", "_settingsContent.Set(\"height\", MenuLayout.ContentHeight(bottom));", "_settingsView!.Call(\"EnsureBoundsCorrect\");", "SectionHeaders.Clear();", "CreateSections();",
            "for (var i = 0; i < SectionHeaders.Count; i++) SectionHeaders[i].View.Set(\"visible\", SectionShown(i));" }, "Expanded tabs measure visible controls and category headers with native scroll bounds");
        var create = MethodFragment(source, "private static void Create");
        var stages = new[] { "SettingsModal", "SettingsFrame", "SettingsHelp", "SettingsTabs", "SettingsScrollTemplate", "SettingsScrollDetach", "SettingsScrollDispose", "SettingsScrollConfigure", "SettingsSteppers", "SettingsReparent", "SettingsFooter", "SettingsRefresh" };
        var previous = -1;
        foreach (var phase in stages) {
            var position = create.IndexOf("TraceSettingsOpen(DiagnosticPhase." + phase + ")", StringComparison.Ordinal);
            Check(position > previous, "Settings creation breadcrumb before native phase: " + phase); previous = position;
        }
        var open = MethodFragment(source, "private static void TogglePanelCore");
        Check(open.IndexOf("TraceSettingsOpen(DiagnosticPhase.SettingsNoticeClose)", StringComparison.Ordinal) >= 0 && open.IndexOf("TraceSettingsOpen(DiagnosticPhase.SettingsNoticeClose)", StringComparison.Ordinal) < open.IndexOf("UpdateNotificationUi.CloseForSettings();", StringComparison.Ordinal)
            && open.IndexOf("TraceSettingsOpen(DiagnosticPhase.SettingsOpenState)", StringComparison.Ordinal) > open.IndexOf("UpdateNotificationUi.CloseForSettings();", StringComparison.Ordinal), "Settings breadcrumbs also enclose native update-notice disposal before Create");
        Check(open.IndexOf("DiagnosticOutcome.Begin", StringComparison.Ordinal) >= 0 && open.IndexOf("DiagnosticOutcome.Begin", StringComparison.Ordinal) < open.IndexOf("Create();", StringComparison.Ordinal)
            && open.IndexOf("DiagnosticOutcome.Completed", StringComparison.Ordinal) > open.IndexOf("Layout();", StringComparison.Ordinal), "Always-on open boundaries enclose settings creation and layout");
        var trace = MethodFragment(source, "private static void TraceSettingsOpen");
        Contains(trace, new[] { "DiagnosticHub.Stage(DiagnosticFeature.Settings, stage, DiagnosticOutcome.Observed)", "DiagnosticHub.FlushSoon()", "Plugin.Logger.LogInfo", "catch { }" }, "Native-hang breadcrumbs use the always-on typed writer and survive logger failure");
        var headers = MethodFragment(source, "private static void CreateSections");
        foreach (var part in new[] { ".label", ".fit", ".style", ".separator" })
            Check(headers.Contains("index + \"" + part + "\"", StringComparison.Ordinal), "Category native-call checkpoint: " + part);
        Contains(headers, new[] { "MenuLayout.Sections", "MenuLayout.SectionY(i)", "MenuLayout.SectionHeight", "ModText.Text(section.Title)",
            "header.Set(\"touchable\", false)", "title.Set(\"touchable\", false)", "title.Set(\"singleLine\", true)", "title.Set(\"autoSize\", 3)",
            "var band = SmoothShape.Create(header,", "SectionHeaders.Add((header, title, band, section.General));", "MenuLayout.SectionBandWidth" }, "Category headings and bands are localized noninteractive scrolling content");
        Check(!headers.Contains("Controls[", StringComparison.Ordinal) && !headers.Contains("Button(", StringComparison.Ordinal), "Category headings never become actionable controls");
        Check(headers.IndexOf("var band = SmoothShape.Create(header,", StringComparison.Ordinal) >= 0 && headers.IndexOf("var band = SmoothShape.Create(header,", StringComparison.Ordinal) < headers.IndexOf("NativeUi.Label(header", StringComparison.Ordinal), "Category band is drawn beneath its title");
        Contains(MethodFragment(ReadSource(context, "src/NativeUi.cs"), "internal static RuntimeObject Label"),
            new[] { "label.Call(\"SetSize\", width, height)", "return label" }, "Fresh labels have bounds before category measurement");
        Check(!source.Contains("UpdateSectionLines", StringComparison.Ordinal), "Category bands need no text-width measurement");
        var help = MethodFragment(source, "private static void SetHelp");
        Check(help.Contains("MenuLayout.StateHelp(action, ToggleEnabled(action), mode)", StringComparison.Ordinal)
            && help.Contains("action is \"UpdateGet\" or \"UpdateApply\" or \"UpdateCancel\"", StringComparison.Ordinal)
            && !Regex.IsMatch(help, "action is[^\\r\\n]*(?:\\\"AutoDownload\\\"|\\\"AfterExit\\\")"), "Download preferences show their own current-state help, not transient updater status");
    }
    // The writer emits enum names and two readers accept only their own copies of those names; a missed copy would
    // drop records or switch always-on diagnostics off on the next launch.
    private static void DiagnosticSchema(DevContext context)
    {
        var enums = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var file in new[] { "src/Observability/SafeDiagnosticEvent.cs", "src/Observability/SafeDiagnosticSite.cs" })
            foreach (Match match in Regex.Matches(ReadSource(context, file), @"internal enum (\w+) \{([^}]*)\}"))
                enums[match.Groups[1].Value] = match.Groups[2].Value.Split(',').Select(name => name.Trim()).Where(name => name.Length != 0).ToArray();
        var keys = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["kind"] = "DiagnosticKind", ["feature"] = "DiagnosticFeature", ["phase"] = "DiagnosticPhase", ["outcome"] = "DiagnosticOutcome",
            ["code"] = "DiagnosticCode", ["ui_action"] = "DiagnosticUiAction", ["error_type"] = "DiagnosticErrorType", ["error_site"] = "DiagnosticSite",
            ["update_state"] = "DiagnosticUpdateState", ["settings.language"] = "DiagnosticLanguage", ["settings.channel"] = "DiagnosticChannel", ["settings.hand_mode"] = "DiagnosticHandMode" };
        foreach (var file in new[] { "src/Observability/OwnedDiagnosticRecord.cs", "diagnostics/MinimalDiagnosticProjection.cs" })
        {
            var lists = Regex.Matches(ReadSource(context, file), @"\{ ""([\w.]+)"", ""([^""]+)""\.Split").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Split(','), StringComparer.Ordinal);
            Check(lists.Count == keys.Count, "Diagnostic reader maps every enum field: " + file);
            foreach (var (key, name) in keys)
                Check(lists.TryGetValue(key, out var accepted) && enums.TryGetValue(name, out var written) && accepted.SequenceEqual(written), "Diagnostic reader accepts exactly the written " + name + " names: " + file);
        }
    }
}
