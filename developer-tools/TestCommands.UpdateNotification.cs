using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void UpdateNotificationMetadataCore(DevContext context, string stage, Dictionary<string, string> options, Action<string> pin)
    {
        if (!options.TryGetValue("plugin-path", out var selected)) throw new ArgumentException("Compiled --plugin-path required for notification metadata checks.");
        var path = Path.GetFullPath(selected); pin(path); pin(context.NativeAssemblyPath);
        using var native = Native(context); using var plugin = AssemblyDefinition.ReadAssembly(path);
        var text = native.MainModule.GetType("FairyGUI.GTextField"); var ubbSetter = text.Methods.Where(method => method.Name == "set_UBBEnabled").ToArray();
        Check(ubbSetter.Length == 1 && ubbSetter[0].Parameters[0].ParameterType.FullName == "System.Boolean", "Native literal-text switch Boolean");
        var layout = NativeMethod(native, "FairyGUI.GTextField", "SetTextFieldText").Body.Instructions;
        var ubb = layout.Where(row => row.Operand is FieldReference field && field.Name == "_ubbEnabled").ToArray();
        Check(ubb.Length == 1 && ubb[0].Next.OpCode.Code is Code.Brfalse or Code.Brfalse_S, "Native UBB guard branches to literal text");
        var plainStart = ((Instruction)ubb[0].Next.Operand).Offset; var plain = layout.Where(row => row.Offset >= plainStart && row.Operand is MethodReference).ToArray();
        Check(plain.Length == 1 && ((MethodReference)plain[0].Operand).FullName == "System.Void FairyGUI.TextField::set_text(System.String)", "Disabled UBB reaches only native literal text setter");
        foreach (var (type, name, result, parameters) in new[] {
            ("FairyGUI.GObject", "get_relations", "FairyGUI.Relations", Array.Empty<string>()), ("FairyGUI.Relations", "ClearAll", "System.Void", Array.Empty<string>()),
            ("FairyGUI.GComponent", "get_scrollPane", "FairyGUI.ScrollPane", Array.Empty<string>()), ("FairyGUI.GComponent", "RemoveChildren", "System.Void", new[] { "System.Int32", "System.Int32", "System.Boolean" }),
            ("FairyGUI.GComponent", "EnsureBoundsCorrect", "System.Void", Array.Empty<string>()), ("FairyGUI.GObject", "SetSize", "System.Void", new[] { "System.Single", "System.Single" }),
            ("FairyGUI.GTextField", "get_textHeight", "System.Single", Array.Empty<string>()), ("FairyGUI.GTextField", "set_autoSize", "System.Void", new[] { "FairyGUI.AutoSizeType" }),
            ("FairyGUI.GList", "set_autoResizeItem", "System.Void", new[] { "System.Boolean" }), ("FairyGUI.GList", "set_layout", "System.Void", new[] { "FairyGUI.ListLayoutType" }),
            ("FairyGUI.ScrollPane", "get_viewWidth", "System.Single", Array.Empty<string>()), ("FairyGUI.ScrollPane", "get_viewHeight", "System.Single", Array.Empty<string>()),
            ("FairyGUI.ScrollPane", "get_contentHeight", "System.Single", Array.Empty<string>()), ("FairyGUI.ScrollPane", "get_scrollingPosY", "System.Single", Array.Empty<string>()),
            ("FairyGUI.ScrollPane", "ScrollTop", "System.Void", new[] { "System.Boolean" }), ("FairyGUI.ScrollPane", "set_mouseWheelEnabled", "System.Void", new[] { "System.Boolean" }),
            ("FairyGUI.ScrollPane", "set_bouncebackEffect", "System.Void", new[] { "System.Boolean" }), ("FairyGUI.ScrollPane", "set_scrollStep", "System.Void", new[] { "System.Single" }) }) ExactMethod(native, type, name, result, parameters);
        ExactField(native, "FairyGUI.GList", "selectionMode", "FairyGUI.ListSelectionMode"); ExactField(native, "FairyGUI.GList", "scrollItemToViewOnClick", "System.Boolean");
        foreach (var (type, name, value) in new[] { ("FairyGUI.AutoSizeType", "Height", 2), ("FairyGUI.ListLayoutType", "SingleColumn", 0), ("FairyGUI.ListSelectionMode", "None", 3) }) {
            var declared = native.MainModule.GetType(type); Check(declared.IsEnum && declared.Fields.Count(field => field.Name == "value__" && field.FieldType.FullName == "System.Int32") == 1 && declared.Fields.Count(field => field.Name == name && Equals(field.Constant, value)) == 1, "Exact native scroll enum: " + type);
        }
        var ui = plugin.MainModule.GetType("BetterAstralParty.ModUi");
        var builder = NativeMethod(plugin, ui.FullName, "Create").Body.Instructions; var literal = Strings(builder, "UBBEnabled");
        Check(literal.Length == 1 && literal[0].Next.OpCode.Code == Code.Ldc_I4_0, "Production help disables UBB");
        Check(NativeMethod(plugin, ui.FullName, "SetHelp").Body.Instructions.Any(row => row.Operand is MethodReference method && method.FullName.Contains("ModText::UpdateDetails", StringComparison.Ordinal)), "Update text presenter wired to help");
        foreach (var name in new[] { "Check", "Poll", "Tick" }) Check(!NativeMethod(plugin, "BetterAstralParty.ReleaseUpdates", name).Body.Instructions.Any(row => row.Operand is MethodReference method && method.Name is "Wait" or "SendAsync"), "No direct blocking/network in UI update entry: " + name);
        var tick = NativeMethod(plugin, ui.FullName, "Tick").Body; var backend = IlCalls(tick.Instructions, "BetterAstralParty.ReleaseUpdates", "Tick");
        Check(backend.Length == 1 && tick.ExceptionHandlers.Any(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart.Offset <= backend[0].Offset && handler.TryEnd.Offset > backend[0].Offset), "Final UI-frame automatic-update exception boundary");
        Check(tick.Instructions.Any(row => row.Operand is MethodReference method && method.FullName.Contains("GameUi::RefreshReleaseNotification", StringComparison.Ordinal)), "Availability presenter connected");
        var notice = NativeMethod(plugin, "BetterAstralParty.GameUi", "RefreshReleaseNotification").Body.Instructions;
        Check(notice.Any(row => row.Operand is FieldReference field && field.Name == "_menu") && Strings(notice, "n8").Length != 0, "Home notice targets private menu clone native title");
        Check(!notice.Any(row => row.Operand is MethodReference method && method.Name == "TogglePanel"), "Notice opens no repeated popups");
        var toggle = NativeMethod(plugin, ui.FullName, "TogglePanel").Body.Instructions;
        var toggleCore = NativeMethod(plugin, ui.FullName, "TogglePanelCore").Body.Instructions;
        Check(IlCalls(toggle, ui.FullName, "TogglePanelCore").Length == 1
            && IlCalls(toggleCore, "BetterAstralParty.ReleaseUpdates", "AcknowledgeNotification").Length == 1, "Reading notice acknowledges it through the isolated settings boundary");
        var popup = plugin.MainModule.GetType("BetterAstralParty.UpdateNotificationUi") ?? throw new InvalidDataException("Compiled notice UI absent.");
        var state = plugin.MainModule.GetType("BetterAstralParty.UpdateNotificationState") ?? throw new InvalidDataException("Compiled notice state absent.");
        var popupCall = IlCalls(tick.Instructions, popup.FullName, "Tick"); var gameCall = tick.Instructions.Where(row => row.Operand is MethodReference method && method.FullName.Contains("GameUi::Tick", StringComparison.Ordinal)).ToArray();
        Check(popupCall.Length == 1 && gameCall.Length == 1 && popupCall[0].Offset > gameCall[0].Offset, "Popup polls after current native UI discovery");
        var core = NativeMethod(plugin, popup.FullName, "TickCore").Body.Instructions;
        Check(core.Any(row => row.Operand is MethodReference method && method.Name == "NotificationHome"), "Popup current-home eligibility gate");
        Check(NativeMethod(plugin, popup.FullName, "Tick").Body.ExceptionHandlers.Count(handler => handler.HandlerType == ExceptionHandlerType.Catch) == 1, "Popup failure locally contained");
        var forbidden = new[] { "Download", "Cancel", "Preferences", "Configure", "Context", "Reactivate", "Recheck", "Connect", "SignOut", "FrameFailed", "SetSettingsModal", "RegisterDelegateConvertor" };
        foreach (var type in new[] { popup, state }) foreach (var method in type.Methods.Where(method => method.HasBody)) foreach (var call in method.Body.Instructions.Select(row => row.Operand).OfType<MethodReference>()) {
            var cleanup = call.DeclaringType.FullName == "System.Threading.CancellationTokenSource" && call.Name == "Cancel";
            var rootPath = call.DeclaringType.FullName == "System.IO.Path" && call.Name == "GetDirectoryName";
            Check(cleanup || rootPath || (!forbidden.Contains(call.Name, StringComparer.Ordinal) && !Regex.IsMatch(call.DeclaringType.FullName, @"System\.IO\.|System\.Net\.|System\.Diagnostics\.Process|Harmony|DelegateSupport")), "Notification presentation-only call: " + call.FullName);
        }
        Check(IlCalls(NativeMethod(plugin, ui.FullName, "ApplyCore").Body.Instructions, popup.FullName, "BeginTransition").Length != 0, "Native settings reaches explicit transition review");
        foreach (var (method, type, call) in new[] { ("BeginTransition", "BetterAstralParty.AutomaticUpdates", "ProposeTransition"), ("HandleInput", "BetterAstralParty.Updating.ChannelTransitionIntent", "Approve"), ("HandleInput", "BetterAstralParty.AutomaticUpdates", "ConfirmTransition") }) Check(IlCalls(NativeMethod(plugin, popup.FullName, method).Body.Instructions, type, call).Length == 1, "Exact explicit transition call: " + method + "→" + call);
        foreach (var method in popup.Methods.Where(method => method.HasBody)) foreach (var call in method.Body.Instructions.Select(row => row.Operand).OfType<MethodReference>()) {
            Check(call.Name is not ("Approve" or "ConfirmTransition") || method.Name == "HandleInput", "Only explicit native input approves a transition"); Check(call.DeclaringType.FullName != "BetterAstralParty.Updating.WindowsFileFence", "Transition filesystem review stays on worker");
        }
        var home = NativeMethod(plugin, "BetterAstralParty.GameUi", "NotificationHome").Body.Instructions;
        foreach (var name in new[] { "currentPanel", "TeamId", "roomStateType", "connectRoomId", "GetTopWindow", "hasModalWindow", "modalWaiting", "hasAnyPopup", "Cut_in", "playing" }) Check(Strings(home, name).Length != 0, "Home eligibility blocker: " + name);
        var current = native.MainModule.GetType("UI.UIManager").Methods.Where(method => method.Name == "get_currentPanel").ToArray(); Check(current.Length == 1 && current[0].ReturnType.FullName == "UI.IBasePanel" && current[0].Parameters.Count == 0, "Native current panel ABI");
        Check(native.MainModule.GetType("UI.BasePanel`1").Methods.Count(method => method.Name == "get_ui" && method.Parameters.Count == 0) == 1, "Native home UI getter ABI");
        Check(native.MainModule.GetType("FairyGUI.Transition").Methods.Count(method => method.Name == "get_playing" && method.ReturnType.FullName == "System.Boolean" && method.Parameters.Count == 0) == 1, "Native home transition playing ABI");
        foreach (var name in new[] { "get_hasModalWindow", "get_modalWaiting", "get_hasAnyPopup" }) Check(native.MainModule.GetType("FairyGUI.GRoot").Methods.Count(method => method.Name == name && method.ReturnType.FullName == "System.Boolean" && method.Parameters.Count == 0) == 1, "Native home root blocker: " + name);
        Check(native.MainModule.GetType("GameLogic.RoomStateType").Fields.Count(field => field.Name == "NONE" && Equals(field.Constant, 0)) == 1, "Native idle room enum");
        foreach (var (type, name) in new[] { ("GameLogic.MatchData", "get_TeamId"), ("GameLogic.GameLogicManager", "get_connectRoomId") }) Check(native.MainModule.GetType(type).Methods.Count(method => method.Name == name && method.ReturnType.FullName == "System.Int64" && method.Parameters.Count == 0) == 1, "Native home Int64 getter: " + name);
        var presenter = NativeMethod(plugin, popup.FullName, "Refresh").Body.Instructions;
        Check(presenter.Any(row => row.Operand is MethodReference method && method.FullName.Contains("ModText::UpdateNotificationDetails", StringComparison.Ordinal)), "Popup bilingual status presenter");
        var create = NativeMethod(plugin, popup.FullName, "Create").Body.Instructions;
        foreach (var name in new[] { "Com_BuffInfo", "list_Buff", "relations", "ClearAll", "RemoveChildren", "autoResizeItem", "layout", "selectionMode", "scrollItemToViewOnClick", "scrollPane", "mouseWheelEnabled", "bouncebackEffect", "scrollStep", "viewWidth", "UBBEnabled", "singleLine" }) Check(Strings(create, name).Length != 0, "Native popup scroll setup: " + name);
        var literalSwitch = Strings(create, "UBBEnabled"); var height = Strings(create, "autoSize"); Check(literalSwitch.Length == 1 && literalSwitch[0].Next.OpCode.Code == Code.Ldc_I4_0 && height.Length == 1 && height[0].Next.OpCode.Code == Code.Ldc_I4_2, "Popup literal wrapped auto-height rather than shrink");
        Check(Strings(create, "ScrollTop").Length == 1 && Strings(presenter, "ScrollTop").Length == 0, "Only popup creation resets read position");
        foreach (var name in new[] { "textHeight", "width", "SetSize", "EnsureBoundsCorrect" }) Check(Strings(presenter, name).Length != 0, "Popup dynamic text-height/bounds: " + name);
        var fontPoll = IlCalls(core, "BetterAstralParty.ModFont", "get_Revision"); var fontCapture = presenter.Where(row => row.OpCode.Code == Code.Stsfld && row.Operand is FieldReference field && field.Name == "_fontRevision").ToArray(); var title = IlCalls(presenter, "BetterAstralParty.NativeUi", "StyleTitle");
        Check(fontPoll.Length == 1 && fontCapture.Length == 1 && title.Length == 1 && fontCapture[0].Offset > title[0].Offset && fontCapture[0].Previous.Operand is MethodReference revision && revision.FullName == "System.Int32 BetterAstralParty.ModFont::get_Revision()", "Popup remeasures font fallback and captures revision after writes");
        var marker = NativeMethod(plugin, popup.FullName, "UpdateScrollMarker").Body.Instructions;
        foreach (var name in new[] { "scrollPane", "viewHeight", "contentHeight", "scrollingPosY", "visible", "SetSize", "SetXY" }) Check(Strings(marker, name).Length != 0, "Scroll position marker ABI: " + name);
        Check(IlCalls(core, popup.FullName, "UpdateScrollMarker").Length != 0 && IlCalls(marker, "BetterAstralParty.MenuLayout", "ScrollMarker").Length != 0 && create.Count(row => row.OpCode.Code == Code.Newobj && row.Operand is MethodReference method && method.DeclaringType.FullName == "BetterAstralParty.NativeUi/Surface") == 2, "Popup reuses existing position-only settings scroll marker");
        var hover = NativeMethod(plugin, "BetterAstralParty.ModText", "UpdateDetails").Body.Instructions; Check(!hover.Any(row => row.Operand is MethodReference method && method.Name is "get_Notes" or "PlainText"), "Release notes stay out of hover help");
        var notes = NativeMethod(plugin, "BetterAstralParty.ModText", "UpdateNotificationDetails").Body.Instructions;
        Check(notes.Any(row => row.Operand is MethodReference method && method.Name == "get_Notes") && IlCalls(notes, "BetterAstralParty.ReleaseUpdates", "PlainText").Length != 0 && notes.Any(row => row.OpCode.Code == Code.Ldc_I4 && Equals(row.Operand, 4096)), "Popup contains full bounded literal release notes");
        Check(NativeMethod(plugin, popup.FullName, "HandleInput").Body.Instructions.Any(row => row.Operand is MethodReference method && method.FullName.Contains("UpdateNotificationInput::Step", StringComparison.Ordinal)), "Popup input state linked");
        var runtimeStage = Path.Combine(stage, "popup-runtime"); Directory.CreateDirectory(runtimeStage);
        var runtime = BuildProject(context, "focus-tests/update-notification/PopupFixtures.csproj", runtimeStage, pin);
        Ensure(context.Run(Dotnet(context), new[] { runtime }, cwd: runtimeStage, env: EnvironmentFor(runtimeStage), timeoutMilliseconds: 60000,
            logPath: Path.Combine(stage, "popup-runtime.log")), "Native popup facade runtime fixture");
        context.WriteJsonCreateNew(Path.Combine(stage, "notification-metadata-parity.json"), new { passed = true, pluginSha256 = context.Hash(path), nativeAbiIlAndPresentationContracts = true, actualNativeMarshalingWheelOrRenderingExecuted = false });
    }
    private static Instruction[] Strings(IEnumerable<Instruction> instructions, string value) => instructions.Where(row => row.OpCode.Code == Code.Ldstr && row.Operand is string text && text == value).ToArray();
}
