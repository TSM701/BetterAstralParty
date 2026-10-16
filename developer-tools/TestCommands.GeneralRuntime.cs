using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterAstralParty.Development;

internal static partial class TestCommands
{
    private static void PingFocus(DevContext context, string stage, Action<string> pin)
    {
        var source = GeneralWithoutNamespace(ReadSource(context, "src/PingFocusGuard.cs"));
        RunGeneralFixture(context, stage, "using System;\n" + source + "\n" + GeneralPingFocus0, "PingFocusCheck.Run()", pin);
        var plugin = ReadSource(context, "src/Plugin.cs"); var ui = ReadSource(context, "src/ModUi.cs");
        Check(!Regex.IsMatch(source, "MonoBehaviour|ClassInjector|OnApplicationFocus") && !Regex.IsMatch(plugin, "AddComponent|ClassInjector|PingApplicationFocusPatch")
            && ui.Contains("PingFocusGuard.Tick();", StringComparison.Ordinal) && !ui.Contains("\"OnApplicationFocus\"", StringComparison.Ordinal), "Ping cancellation uses the existing polling driver, not native type injection");
        using var native = Native(context); var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        Check(types["Core.Scene.BattleSceneController"].Fields.Any(field => field.Name == "MarkManager"), "Native scene mark manager");
        foreach (var name in new[] { "Core.Mark.PCMarkInput", "Core.Mark.MobileMarkInput" }) Check(Il(types[name].Methods.Single(method => method.Name == "ExitMark")).Contains("MarkModeController::ExitMarkMode", StringComparison.Ordinal), "Native mark exit " + name);
        var exit = Il(types["Core.Mark.MarkModeController"].Methods.Single(method => method.Name == "ExitMarkMode"));
        Check(!Regex.IsMatch(exit, "Request|C2S") && exit.Contains("MarkExit", StringComparison.Ordinal) && exit.Contains("UpdateStatus", StringComparison.Ordinal), "Native mark exit is local cleanup");
        var detection = types["Core.Mark.MarkDetectionSystem"];
        Check(types["Core.Mark.MarkManager"].Fields.Any(field => field.Name == "_detectionSystem") && detection.Fields.Any(field => field.Name == "_currentTarget" && field.FieldType.FullName == "Core.Mark.IMarkTarget"), "Pending native mark target binding");
        Contains(Il(detection.Methods.Single(method => method.Name == "OnEnable")), new[] { "MarkSignal::MarkExit", "SelectedCurrentTarget" }, "Native mark-exit subscription");
        Contains(Il(detection.Methods.Single(method => method.Name == "SelectedCurrentTarget")), new[] { "IMarkTarget::OnMarkSelected", "brfalse" }, "Ordinary native mark confirmation");
        var hover = Il(detection.Methods.Single(method => method.Name == "ExitCurrentHover"));
        Contains(hover, new[] { "IMarkTarget::OnMarkHoverExit", "_hoverTimer", "_hoverTriggered" }, "Native hover cleanup");
        Check(!Regex.IsMatch(hover, "OnMarkSelected|Request|C2S"), "Hover cleanup never confirms a ping");
        Check(source.IndexOf("detection.SetField(\"_currentTarget\"", StringComparison.Ordinal) < source.IndexOf("Call(\"ExitMark\")", StringComparison.Ordinal)
            && source.Contains("detection.SetField(\"_currentTarget\", (RuntimeObject?)null)", StringComparison.Ordinal), "Clear pending target before exit dispatch");
    }

    private static void PingPopups(DevContext context, string stage, Action<string> pin)
    {
        using var native = Native(context); var types = native.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        foreach (var (type, name, count) in new[] { ("GameLogic.BattleRelicMessage", "GetRelicMsg", 1), ("StaticConfigure", "get_Relic", 0),
            ("RelicConfigure", "get_InfoDict", 0), ("UI.UIHelper", "GetRelicInfoConfigure", 1), ("FairyGUI.UIPackage", "GetByName", 1), ("FairyGUI.UIPackage", "AddPackageAsync", 2), ("UI.UIRelicWindow", "BindAll", 0) })
            Check(types[type].Methods.Count(method => method.Name == name && method.Parameters.Count == count) == 1, "Native public ping API " + type + "." + name);
        foreach (var name in new[] { "txt_tltle", "txt_Desc", "loader_Icon", "loader_Frame", "status", "RecommendType", "cutIn" }) Check(types["UI.UIRelic_Button_Item"].Fields.Any(field => field.Name == name), "Native relic UI field " + name);
        Contains(Il(types["UI.RelicWindow"].Methods.Single(method => method.Name == "RendererRelicItem")), new[] { "get_NameID", "get_DescID", "get_Icon", "get_RelicQualityType", "UICom_RelicQuality_Large::qualityType", "UIRelic_Button_Item::cutIn" }, "Native relic renderer");
        Check(Il(NativeAsyncMoveNext(native, "UI.BaseWindow", "Load")).Contains("UIPackage::AddPackageAsync(System.String,System.String)", StringComparison.Ordinal), "Native async package loading ABI");
        Contains(Il(types["UI.UIRelicWindow"].Methods.Single(method => method.Name == "BindAll")), new[] { "UI.UIRelic_Button_Item", "UIObjectFactory::SetPackageItemExtension" }, "Native package extension registration");
        var source = ReadSource(context, "src/CardPreviewUi.cs");
        Contains(source, new[] { "_relicLoad.Get<bool>(\"IsCompleted\")", "load.Call(\"GetResult\")", "\"AddPackageAsync\", \"Relic\", \"\"", ".Call(\"GetAwaiter\")", "\"UIRelicWindow\"), \"BindAll\"",
            "if (_relicLoad != null) return false;", "waiting.Id == id && waiting.Relic == relic", "if (!insidePopup && ping == null) BeginClose();", "(!request.Relic || RelicUiReady())",
            "Show(root, request.Id, request.Relic, request.Anchor)", "_anchor = anchor;", "\"BattleRelicMessage\"", "\"GetRelicMsg\"", "\"GetRelicInfoConfigure\"",
            "CardPreviewLayout.Resolve(Pings, RelicPings", "_shownRelic == relic", "_shownRelic != relic", "Local(config, \"NameID\", \"Relic\")", "Local(config, \"DescID\", \"Relic\")",
            "relic ? \"Relic_Button_Item\" : \"Com_Card\"", "relic ? \"cutIn\" : \"ShowDescState\"", "description.Get<float>(\"textHeight\")", "if (!Plugin.CardPopups.Value)", "GameUi.Dispose(_popup)", "Input.GetKeyDown(KeyCode.Escape)" }, "Deferred public ping popup lifecycle");
        foreach (var name in new[] { "BeginClose", "Reset" }) Check(Regex.IsMatch(source, name + @"\(\)\s*\{\s*_pending = null;"), "Pending popup click canceled on " + name);
        var showStart = source.IndexOf("private static void Show(", StringComparison.Ordinal); var showEnd = source.IndexOf("private static void RenderRelic(", StringComparison.Ordinal);
        Check(source.IndexOf("load.Call(\"GetResult\")", StringComparison.Ordinal) < source.IndexOf("Update();", StringComparison.Ordinal) && showStart >= 0 && showEnd > showStart
            && !source[showStart..showEnd].Contains("Input.mousePosition", StringComparison.Ordinal), "Load completes before rendering with the original anchor");
        Contains(ReadSource(context, "src/RuntimeObject.cs"), new[] { "il2cpp_class_is_valuetype(Class) ? IL2CPP.il2cpp_object_unbox(Pointer) : Pointer", "Invoke(Class, instance, method, args)" }, "Boxed native awaiter receivers unboxed");
        Check(types["FairyGUI.GObject"].Fields.Any(field => field.Name == "name") && source.Contains("child.Field(\"name\")!.String()", StringComparison.Ordinal) && !source.Contains("Get(\"name\")", StringComparison.Ordinal), "Native child name read as a field");
        Check(!Regex.IsMatch(source, "Call\\(\"RendererRelicItem\"|Call\\(\"ShowRelicData\"|Call\\(\"ChatRelic\"|Call\\(\"TryShow\"|HandCards|SelectRelicC2S|RequestGetHeroInfo"), "Public popup never selects or sends a relic or reads private hands");
        Check(types["UI.ExpressionListWindow"].Fields.Single(field => field.Name == "_messages").FieldType.FullName == "System.Collections.Generic.List`1<UI.BattleMessage>", "Native displayed public message history");
        foreach (var name in new[] { "shortInfo", "SendMsg" })
            Check(types["UI.BattleMessage"].Fields.Single(field => field.Name == name).FieldType.FullName == "System.String", "Native public message text " + name);
        Check(types["UI.BattleMessage"].Fields.Single(field => field.Name == "MsgType").FieldType.FullName == "GameLogic.MessageType", "Native message discriminator ABI");
        Check(Il(types["UI.UIExpression_Com_ChatItem"].Methods.Single(method => method.Name == "RefreshInfo")).Contains("BattleMessage::shortInfo", StringComparison.Ordinal), "Displayed title is the public message text");
        Check(!source.Contains("GetCardMsg", StringComparison.Ordinal) && source.Contains("IndexCardPings(table);", StringComparison.Ordinal), "Card previews use displayed messages, not a changed formatter signature");
        foreach (var name in new[] { "Card", "Relic", "Character", "PVENurturance", "Buff" })
            Check(types["UI.UIStringType"].Fields.Single(field => field.Name == name).HasConstant, "Native localization enum name " + name);
        var index = Derive(source, @"(?s)    private static void IndexCardPings\(.*?\r?\n    \}", "Production public ping index");
        var local = Derive(source, @"(?s)    private static string Local\(.*?\r?\n    \}", "Production localization enum consumer");
        var enumValue = Derive(ReadSource(context, "src/RuntimeObject.cs"), @"(?m)^    internal static int EnumValue\([^\r\n]+", "Production native enum reader");
        var fixture = ReadSource(context, "focus-tests/ping-popups/Fixture.cs")
            .Replace("__LAYOUT__", WithoutImports(ReadSource(context, "src/CardPreviewLayout.cs")), StringComparison.Ordinal)
            .Replace("__INDEX__", index, StringComparison.Ordinal).Replace("__LOCAL__", local, StringComparison.Ordinal).Replace("__ENUM__", enumValue, StringComparison.Ordinal);
        RunGeneralFixture(context, stage, fixture, "PingPopupCheck.Run()", pin);
    }

    private static FieldDefinition RequiredField(AssemblyDefinition assembly, string type, string name, string valueType)
    {
        var field = assembly.MainModule.GetType(type).Fields.Single(row => row.Name == name);
        Check(!field.IsStatic && field.FieldType.FullName == valueType, type + "." + name + " exact instance field ABI"); return field;
    }
    private static MethodDefinition RequiredMethod(AssemblyDefinition assembly, string type, string name, string result, bool isStatic = false)
    {
        var method = assembly.MainModule.GetType(type).Methods.Single(row => row.Name == name && row.Parameters.Count == 0);
        Check(method.IsPublic && method.IsStatic == isStatic && method.ReturnType.FullName == result, type + "." + name + " exact public ABI"); return method;
    }
    private static string[] GeneralCalls(MethodDefinition method) => method.Body.Instructions.Where(instruction => instruction.Operand is MethodReference).Select(instruction => ((MethodReference)instruction.Operand).FullName).ToArray();

    private static void AutoThanks(DevContext context, string stage, Action<string> pin)
    {
        var nativePath = context.NativeAssemblyPath; var corePath = Path.Combine(context.Root, ".research/extracted/mscorlib.dll.dll"); pin(nativePath); pin(corePath);
        if (!context.NativeAssemblyExplicit) Check(context.Hash(nativePath) == "DDEFD944846C612CC8935BCEF33170BAA8F3EE3974CBF2EC0F8F001678CED341", "Audited historical native recommendation input");
        Check(context.Hash(corePath) == "8238BC094BFF4D34FBA44BA09B306645FAD0E46945973CAE8D80F6787E96E66C", "Audited engine core recommendation metadata input");
        using var native = Native(context); using var core = AssemblyDefinition.ReadAssembly(corePath);
        var scene = native.MainModule.GetType("Core.Scene.BaseSceneController"); var awake = scene.Methods.Single(method => method.Name == "Awake");
        var background = awake.Body.Instructions.Single(instruction => instruction.Operand is MethodReference method && method.FullName == "System.Void UnityEngine.Application::set_runInBackground(System.Boolean)");
        Check(background.Previous.OpCode.Name == "ldc.i4.1", "Native player loop runs in background");
        foreach (var name in new[] { "OnApplicationPause", "OnApplicationFocus" }) { var method = scene.Methods.Single(row => row.Name == name && row.Parameters.Count == 1); Check(method.Body.Instructions.Count == 1 && method.Body.Instructions[0].OpCode.Name == "ret", "Native focus callback does not stop recommendations " + name); }
        foreach (var (type, name, value) in new[] {
            ("UI.ExpressionWindow", "_btn_QuickReply", "UI.UIExpression_Button_QuickReply"), ("UI.ExpressionWindow", "_showQuickReply", "FairyGUI.Transition"), ("UI.ExpressionWindow", "quickReplyCts", "Core.CtsInfo"),
            ("Core.CtsInfo", "id", "System.Int32"), ("Core.CtsInfo", "cts", "System.Threading.CancellationTokenSource"), ("Core.CtsInfo", "_dispose", "System.Boolean"),
            ("FairyGUI.EventListener", "_bridge", "FairyGUI.EventBridge"), ("FairyGUI.EventBridge", "owner", "FairyGUI.EventDispatcher"), ("FairyGUI.EventBridge", "_isLocking", "System.Boolean"),
            ("FairyGUI.EventBridge", "_callback0", "FairyGUI.EventCallback0"), ("FairyGUI.EventBridge", "_callback1", "FairyGUI.EventCallback1"), ("FairyGUI.EventBridge", "_captureCallback", "FairyGUI.EventCallback1"),
            ("GameLogic.RoomLogic", "roomController", "GameLogic.RoomController"), ("GameLogic.RoomController", "roomStateType", "GameLogic.RoomStateType"),
            ("GameLogic.BattleLogic", "clientFinishReady", "System.Boolean"), ("GameLogic.BattlePlayerData", "player", "GameLogic.RoomPlayer") }) RequiredField(native, type, name, value);
        Check(RequiredField(native, "Core.CtsInfo", "id", "System.Int32").IsPublic && RequiredField(native, "Core.CtsInfo", "cts", "System.Threading.CancellationTokenSource").IsPublic, "Native episode identity and cancellation are public");
        foreach (var (type, name, result) in new[] { ("Core.CtsInfo", "get_IsCancellationRequested", "System.Boolean"), ("FairyGUI.GObject", "get_visible", "System.Boolean"), ("FairyGUI.GObject", "get_touchable", "System.Boolean"),
            ("FairyGUI.GObject", "get_grayed", "System.Boolean"), ("FairyGUI.GObject", "get_onClick", "FairyGUI.EventListener"), ("FairyGUI.Window", "get_isShowing", "System.Boolean"),
            ("FairyGUI.GRoot", "get_hasModalWindow", "System.Boolean"), ("FairyGUI.GRoot", "get_modalWaiting", "System.Boolean"), ("FairyGUI.GRoot", "get_hasAnyPopup", "System.Boolean"),
            ("FairyGUI.EventListener", "get_isEmpty", "System.Boolean"), ("FairyGUI.EventListener", "get_isDispatching", "System.Boolean"), ("FairyGUI.EventListener", "Call", "System.Boolean"),
            ("GameLogic.GameLogicManager", "get_room", "GameLogic.RoomLogic"), ("GameLogic.GameLogicManager", "get_watch", "GameLogic.WatchLogic"), ("GameLogic.GameLogicManager", "get_replay", "GameLogic.ReplayLogic"),
            ("GameLogic.GameLogicManager", "get_battle", "GameLogic.BattleLogic"), ("GameLogic.GameLogicManager", "get_account", "GameLogic.AccountLogic"), ("GameLogic.RoomLogic", "get_curRoomInfo", "GameLogic.RoomInfo"),
            ("GameLogic.RoomInfo", "IsPVE", "System.Boolean"), ("GameLogic.RoomInfo", "GetSelfInfo", "GameLogic.RoomPlayer"), ("GameLogic.WatchLogic", "PlayerIsWatcher", "System.Boolean"),
            ("GameLogic.ReplayLogic", "get_Session", "GameLogic.Replay.ReplaySession"), ("GameLogic.Replay.ReplaySession", "get_CanSendC2S", "System.Boolean"),
            ("GameLogic.BattleLogic", "GetSelfPlayerData", "GameLogic.BattlePlayerData"), ("GameLogic.RoomPlayer", "get_Id", "System.Int64"), ("GameLogic.AccountLogic", "GetPlayerID", "System.Int64") }) RequiredMethod(native, type, name, result);
        RequiredMethod(native, "FairyGUI.GObject", "get_draggingObject", "FairyGUI.GObject", true);
        Check(GeneralCalls(RequiredMethod(native, "FairyGUI.EventListener", "get_isEmpty", "System.Boolean")).Contains("System.Boolean FairyGUI.EventDispatcher::hasEventListeners(System.String)", StringComparer.Ordinal), "Native listener availability uses its existing owner");
        Check(native.MainModule.GetType("FairyGUI.EventCallback0").BaseType.FullName == "System.MulticastDelegate", "Native click callback is a multicast delegate");
        RequiredMethod(core, "System.Delegate", "get_Target", "System.Object"); RequiredMethod(core, "System.Delegate", "GetInvocationList", "System.Delegate[]"); RequiredMethod(core, "System.Array", "get_Length", "System.Int32");
        Check((int)native.MainModule.GetType("GameLogic.RoomStateType").Fields.Single(field => field.Name == "RUNNING").Constant == 4, "Native running room state");
        var producer = NativeAsyncMoveNext(native, "UI.ExpressionWindow", "TryShowQuickReply", 1);
        var callbacks = producer.Body.Instructions.Where(instruction => instruction.OpCode.Name is "ldftn" or "ldvirtftn").Select(instruction => instruction.Operand).OfType<MethodReference>()
            .Where(method => method.Parameters.Count == 0 && method.ReturnType.FullName == "System.Void").Select(method => method.Resolve())
            .Where(method => method.DeclaringType.DeclaringType?.FullName == "UI.ExpressionWindow" && method.HasBody && GeneralCalls(method).Any(call => call.Contains("::RequestRoomShortChatC2S(", StringComparison.Ordinal))).Distinct().ToArray();
        Check(callbacks.Length == 1, "Native producer creates exactly one original chat callback"); var callback = callbacks[0]; var closure = callback.DeclaringType;
        Check(closure.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"), "Native chat callback has compiler-generated capture ownership");
        RequiredField(native, closure.FullName, "<>4__this", "UI.ExpressionWindow"); RequiredField(native, closure.FullName, "chatId", "System.Int32");
        Check(GeneralCalls(producer).Contains("System.Void Core.CtsInfo::Cancel()", StringComparer.Ordinal) && producer.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "CreatCts"), "Native producer starts a new episode after canceling the prior one");
        Check(producer.Body.Instructions.Count(instruction => instruction.Operand is MethodReference method && method.FullName == "System.Void FairyGUI.EventListener::Set(FairyGUI.EventCallback0)") == 1, "Native producer installs one original zero-argument callback");
        Check(producer.Body.Instructions.Count(instruction => instruction.OpCode.Name == "stfld" && instruction.Operand is FieldReference field && field.Name == "chatId" && field.FieldType.FullName == "System.Int32" && field.DeclaringType.Resolve() == closure) == 1, "Native callback captures the original chat id");
        Check(NativeMethod(native, "GameLogic.CommunicateLogic", "OnSayPhraseNotifyS2CServerCallBack", 3).FullName == "Cysharp.Threading.Tasks.UniTask GameLogic.CommunicateLogic::OnSayPhraseNotifyS2CServerCallBack(party.protocol.SayPhraseNotifyS2C,System.Int32,System.Boolean)", "Exact native server recommendation signature");
        var recommendation = NativeAsyncMoveNext(native, "GameLogic.CommunicateLogic", "OnSayPhraseNotifyS2CServerCallBack", 3);
        foreach (var id in new[] { 50000, 50001, 50002 }) Check(recommendation.Body.Instructions.Count(instruction => instruction.OpCode.Name == "ldc.i4" && Equals(instruction.Operand, id)) == 1, "Native gratitude recommendation " + id);
        Check(GeneralCalls(recommendation).Contains("Cysharp.Threading.Tasks.UniTask UI.ExpressionWindow::TryShowQuickReply(System.Int32)", StringComparer.Ordinal), "Native server producer forwards its captured chat id");
        var all = native.MainModule.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "TryShowQuickReply")).ToArray();
        Check(all.Length == 1 && all[0].FullName == recommendation.FullName, "All quick replies use the one native server-trigger producer");
        var trigger = native.MainModule.GetType("party.model.TriggerPhrase/Types/Type"); foreach (var (name, value) in new[] { ("TransferGold", 1), ("GiveCard", 2), ("CureFriend", 3), ("KillBoss", 4) }) Check((int)trigger.Fields.Single(field => field.Name == name).Constant == value, "Native quick-reply trigger " + name);
        var dispatch = recommendation.Body.Instructions.Single(instruction => instruction.Offset == 0x00d7); var branches = dispatch.Operand as Instruction[];
        Check(dispatch.OpCode.Name == "switch" && branches is { Length: 4 } && Equals(branches[0].Operand, 50000) && Equals(branches[1].Operand, 50002) && Equals(branches[2].Operand, 50001) && Equals(branches[3].Operand, 50003), "Native transfer/card/heal/boss mapping");
        var skip = recommendation.Body.Instructions.Single(instruction => instruction.Offset == 0x0123); Check(skip.OpCode.Name == "beq.s" && skip.Operand is Instruction target && target.Offset == 0x013a, "Native boss gratitude intentionally has no quick reply");
        Contains(String.Join("\n", GeneralCalls(recommendation)), new[] { "System.Boolean GameLogic.AccountLogic::IsSelf(System.Int64)", "System.Int64 party.model.TriggerPhrase::get_PassivePlayerId()" }, "Native local recipient validation");
        Check(producer.Body.Instructions.Count(instruction => instruction.OpCode.Name == "ldc.i4" && Equals(instruction.Operand, 5000)) == 1 && GeneralCalls(producer).Count(call => call.Contains("UniTask::Delay(System.Int32,", StringComparison.Ordinal)) == 1, "Original five-second native recommendation lifetime");
        Contains(String.Join("\n", GeneralCalls(callback)), new[] { "System.Void FairyGUI.EventListener::Retain()", "System.Void Core.CtsInfo::Cancel()" }, "Native normal confirmation ownership");
        Check(callback.Body.Instructions.Count(instruction => instruction.Operand is MethodReference method && method.Name == "RequestRoomShortChatC2S") == 1, "Original handler owns the one native transport call");
        var completions = callback.Body.Instructions.Where(instruction => instruction.OpCode.Name is "ldftn" or "ldvirtftn").Select(instruction => instruction.Operand).OfType<MethodReference>().Select(method => method.Resolve())
            .Where(method => method.DeclaringType == closure && method.HasBody && GeneralCalls(method).Contains("System.Void FairyGUI.EventListener::Release()", StringComparer.Ordinal)).Distinct().ToArray();
        Check(completions.Length == 1, "Native RPC completion releases its cooldown");
        var call = RequiredMethod(native, "FairyGUI.EventListener", "Call", "System.Boolean"); var eventDispatch = native.MainModule.GetType("FairyGUI.EventDispatcher").Methods.Single(method => method.Name == "InternalDispatchEvent"); var bridgeCall = native.MainModule.GetType("FairyGUI.EventBridge").Methods.Single(method => method.Name == "CallInternal");
        foreach (var method in new[] { recommendation, producer, callback, call, eventDispatch, bridgeCall }) Check(!GeneralCalls(method).Any(value => Regex.IsMatch(value, @"GRoot::get_(hasAnyPopup|hasModalWindow|modalWaiting)|Application::(get_isFocused|[gs]et_runInBackground)|OnApplication(Focus|Pause)")), "No unrelated modal or focus veto in native recommendation route " + method.Name);
        Check(GeneralCalls(call).Contains("System.Boolean FairyGUI.EventDispatcher::InternalDispatchEvent(System.String,FairyGUI.EventBridge,System.Object,System.Object)", StringComparer.Ordinal)
            && GeneralCalls(eventDispatch).Contains("System.Void FairyGUI.EventBridge::CallInternal(FairyGUI.EventContext)", StringComparer.Ordinal)
            && bridgeCall.Body.Instructions.Count(instruction => instruction.Operand is FieldReference field && field.Name == "_isLocking") == 1, "Original native dispatch preserves retained listener lock");
        Check(GeneralCalls(RequiredMethod(native, "FairyGUI.Window", "Show", "System.Void")).Contains("System.Void FairyGUI.GRoot::ShowWindow(FairyGUI.Window)", StringComparer.Ordinal)
            && GeneralCalls(native.MainModule.GetType("FairyGUI.GRoot").Methods.Single(method => method.Name == "ShowWindow")).Contains("FairyGUI.GObject FairyGUI.GComponent::AddChild(FairyGUI.GObject)", StringComparer.Ordinal), "Native expression window attaches to the root");
        var loseFocus = GeneralCalls(native.MainModule.GetType("UI.BaseWindow").Methods.Single(method => method.Name == "LoseFocus")); Check(loseFocus.Length == 5 && loseFocus.Contains("System.Void FairyGUI.EventListener::Remove(FairyGUI.EventCallback1)", StringComparer.Ordinal), "Native window focus removes only its key listener");
        var source = GeneralWithoutNamespace(ReadSource(context, "src/AutoThanksUi.cs")).Replace("using UnityEngine;", "", StringComparison.Ordinal);
        Check(!Regex.IsMatch(source, "\\.Set\\(|\\.Register\\(|\\.AddListener\\(|RequestRoomShortChat|SendChat|Dispatch\\(|IsSelf\"\\s*,"), "AutoThanks adds no native writes or replacement transport");
        Check(source.Contains("click.Call(\"Call\");", StringComparison.Ordinal) && source.IndexOf("_episode = episode;", StringComparison.Ordinal) < source.IndexOf("click.Call(\"Call\");", StringComparison.Ordinal), "Episode latch precedes uncertain native dispatch");
        Contains(source, new[] { "Call(\"GetPlayerID\")", "catch (Exception ex) { Compatibility.Block(Feature, ex); }" }, "Native identity read and independent breaker");
        Check(!source.Contains("Call(\"IsSelf\"", StringComparison.Ordinal), "No unsupported Int64 argument bridge call");
        var fixture = ReadSource(context, "focus-tests/auto-thanks/RuntimeFixture.cs").Replace("__PRODUCTION__", source, StringComparison.Ordinal);
        RunGeneralFixture(context, stage, fixture, "AutoThanksTest.Check.Run()", pin);
    }

    private static void ShushuShield(DevContext context, string stage, Action<string> pin)
    {
        var path = context.NativeAssemblyPath; pin(path); using var native = Native(context);
        foreach (var (type, name) in new[] { ("Core.Unit.BattleActor", "effectParent"), ("Core.Unit.BattleActor", "actorRenderer"), ("Core.Unit.BattleActor", "_UI"), ("UI.UIFightWindow", "btn_Defend"), ("UI.UIFightWindow", "btn_Dodge"), ("UI.UIFightWindow", "playerState"), ("UI.UIFightWindow", "step") })
            Check(native.MainModule.GetType(type).Fields.Any(field => field.Name == name), "Native shield field " + type + "." + name);
        var handler = native.MainModule.GetTypes().Single(type => type.FullName.Contains("LianSkillShieldBuffEffectHandler/<Play>", StringComparison.Ordinal));
        Contains(Il(handler.Methods.Single(method => method.Name == "MoveNext")), new[] { "SkinReplaceEffectID", "GetPlayerDataByHeroId", "standingPainting" }, "Native caster-skin lookup");
        var actor = native.MainModule.GetTypes().Single(type => type.FullName.Contains("BattleActor/<CreateBuffEffect>", StringComparison.Ordinal)).Methods.Single(method => method.Name == "MoveNext");
        Check(Il(actor).Contains("IsShowEffectDuringPK", StringComparison.Ordinal) && actor.Body.Instructions.Count(instruction => instruction.OpCode.Name == "ldc.r4" && Equals(instruction.Operand, .25f)) == 1, "Native combat effect scale and visibility filter");
        var fight = native.MainModule.GetType("UI.FightWindow"); Check(fight.Fields.Any(field => field.Name == "cardItemList"), "Native combat hand field");
        var card = native.MainModule.GetType("UI.UIHandCard_Button_Card"); Check(card.Fields.Any(field => field.Name == "IsRelease") && card.Methods.Any(method => method.Name == "get_CardData"), "Native combat card lifecycle");
        var valid = fight.Methods.Single(method => method.Name == "GetVailCard"); Check(Il(valid).Contains("get_EffectType", StringComparison.Ordinal) && valid.Body.Instructions.Any(instruction => instruction.OpCode.Name == "ldc.i4.2"), "Native defense card filter");
        Check(fight.Methods.Single(method => method.Name == "InitPKCard").Body.Instructions.Any(instruction => instruction.OpCode.Name == "ldc.i4.3"), "Native card selection phase");
        foreach (var name in new[] { "StopDrag", "get_draggingObject" }) Check(native.MainModule.GetType("FairyGUI.GObject").Methods.Any(method => method.Name == name), "Native drag cancellation ABI " + name);
        var choice = fight.Methods.Single(method => method.Name == "RefreshDefendReadyChoice"); Check(Il(choice).Contains("set_touchable", StringComparison.Ordinal) && choice.Body.Instructions.Count(instruction => instruction.OpCode.Name == "ldc.i4.5") == 1, "Native defense choice phase and touchability");
        var source = GeneralWithoutNamespace(ReadSource(context, "src/ShushuShieldUi.cs")).Replace("using UnityEngine;", "", StringComparison.Ordinal);
        Check(!Regex.IsMatch(source, "CounterIndicator|MaxPoint|UpdateIcon|ClearIcon"), "Removed shield emphasis stays removed");
        var counter = Derive(ReadSource(context, "src/BattleStatusUi.cs"), @"(?s)    private static void RefreshCounter\(.*?\r?\n    \}", "Production public counter reader");
        RunGeneralFixture(context, stage, GeneralShushuShield0.Replace("__SOURCE__", source, StringComparison.Ordinal).Replace("__COUNTER__", counter, StringComparison.Ordinal), "ShieldUiTest.Check.Run()", pin);
    }
}
