using BetterAstralParty;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MatchFocusNativeChecks
{
    internal static void Run(string path, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Path.GetFullPath(path));
        var types = assembly.MainModule.GetTypes().ToDictionary(type => type.FullName);
        MethodDefinition Method(string type, string name) => types[type].Methods.Single(method => method.Name == name);
        MethodDefinition Async(string type, string name)
        {
            var source = Method(type, name); var attributes = source.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute").ToArray();
            check(source.HasBody && attributes.Length == 1 && attributes[0].ConstructorArguments.Count == 1 && attributes[0].ConstructorArguments[0].Value is TypeReference, "exact native source async attribute " + type + "." + name);
            var state = ((TypeReference)attributes[0].ConstructorArguments[0].Value).Resolve();
            check(state.Module == assembly.MainModule && state.DeclaringType == source.DeclaringType, "native async state belongs to its exact source owner " + type + "." + name);
            var methods = state.Methods.Where(method => method.Name == "MoveNext" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType.FullName == "System.Void" && method.HasBody).ToArray();
            check(methods.Length == 1, "exact native async body " + type + "." + name); return methods[0];
        }
        void Getter(string type, string name, string result)
        {
            var method = Method(type, "get_" + name);
            check(method.IsPublic && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType.FullName == result,
                "native getter ABI " + type + "." + name);
        }
        void Field(string type, string name, string result) => check(types[type].Fields.Single(field => field.Name == name).FieldType.FullName == result,
            "native field ABI " + type + "." + name);
        int Constant(Instruction instruction) => instruction.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1, Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3, Code.Ldc_I4_4 => 4, Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7, Code.Ldc_I4_8 => 8, Code.Ldc_I4_S => Convert.ToInt32(instruction.Operand),
            Code.Ldc_I4 => (int)instruction.Operand, _ => throw new Exception("Expected integer IL constant")
        };
        int Enum(string type, string name) => Convert.ToInt32(types[type].Fields.Single(field => field.Name == name).Constant);
        Getter("GameLogic.GameLogicManager", "match", "GameLogic.MatchLogic");
        Getter("GameLogic.GameLogicManager", "room", "GameLogic.RoomLogic");
        Getter("GameLogic.GameLogicManager", "connectRoomId", "System.Int64");
        Getter("Core.Net.NetManager", "IsConnected", "System.Boolean");
        Field("Core.Net.NetManager", "_Reconnecting", "System.Boolean");
        Field("GameLogic.MatchLogic", "matchData", "GameLogic.MatchData");
        Field("GameLogic.RoomLogic", "roomController", "GameLogic.RoomController");
        Field("GameLogic.RoomController", "roomStateType", "GameLogic.RoomStateType");
        Getter("GameLogic.RoomController", "localRoom", "GameLogic.RoomInfo");
        Field("GameLogic.RoomInfo", "info", "party.model.Room");
        Getter("GameLogic.MatchData", "TeamId", "System.Int64");
        Getter("GameLogic.MatchData", "MatchStatus", "party.model.MatchTeamInfo/Types/State");
        Getter("party.model.Room", "Id", "System.Int64");
        Getter("party.model.Room", "IsMatchRoom", "System.Boolean");
        Getter("party.model.Room", "MapType", "System.Int32");
        foreach (var type in new[] { "GameLogic.GameLogicManager", "Core.Net.NetManager" })
        {
            var parent = types[types[type].BaseType.GetElementType().FullName];
            check(parent.Fields.Any(field => field.Name == "_inst" && field.IsStatic && field.FieldType is GenericParameter), "inherited singleton ABI " + type);
        }
        check(Enum("GameLogic.RoomStateType", "NONE") == 0 && Enum("GameLogic.RoomStateType", "WAIT") == 1 && Enum("GameLogic.RoomStateType", "CHOICE") == 2
            && Enum("party.model.MatchTeamInfo/Types/State", "Matching") == 2
            && Enum("party.model.MatchTeamInfo/Types/State", "Playing") == 3, "separate native room/queue enum constants");
        foreach (var type in new[] { "GameLogic.RoomStateType", "party.model.MatchTeamInfo/Types/State" })
            check(types[type].Fields.Single(field => field.Name == "value__").FieldType.FullName == "System.Int32", "enum underlying ABI " + type);
        var success = Async("GameLogic.MatchLogic", "OnMatchSuccessS2CServerCallBack").Body.Instructions;
        var create = success.Single(instruction => instruction.Operand is MethodReference method && method.FullName.Contains("RoomController::CreateRoom(", StringComparison.Ordinal));
        var choosing = success.Single(instruction => instruction.Operand is MethodReference method && method.FullName.Contains("RoomController::SwitchRoomState(", StringComparison.Ordinal));
        var popup = success.Single(instruction => instruction.Operand is MethodReference method && method.FullName.Contains("MatchSuccessWindow::ShowMatchResult(", StringComparison.Ordinal));
        check(create.Offset < choosing.Offset && choosing.Offset < popup.Offset && Constant(choosing.Previous) == 2, "native success creates choosing room before success popup");
        check(success.Any(instruction => instruction.Operand is FieldReference field && field.Name == "errid" && instruction.Next.OpCode.FlowControl == FlowControl.Cond_Branch), "native success checks server error first");
        var createRoom = Method("GameLogic.RoomController", "CreateRoom").Body.Instructions;
        var waitingState = createRoom.Single(instruction => instruction.OpCode.Code == Code.Stfld
            && instruction.Operand is FieldReference field && field.Name == "roomStateType");
        check(Constant(waitingState.Previous) == 1, "native manual room creation enters WAIT, not CHOICE");
        var manualStartMethod = Async("GameLogic.RoomLogic", "OnStartGameS2CServerCallBack"); var manualStart = manualStartMethod.Body.Instructions;
        var manualChoice = manualStart.Single(instruction => instruction.Operand is MethodReference method && method.Name == "SwitchRoomState");
        var manualPanel = manualStart.Single(instruction => instruction.Operand is MethodReference method && method.Name == "OpenPanel");
        check(Constant(manualChoice.Previous) == 2 && manualChoice.Offset < manualPanel.Offset && Constant(manualPanel.Previous.Previous) == 17,
            "native host start enters CHOICE before RoomHero selection for all recipients");
        var manualError = manualStart.Single(instruction => instruction.Operand is FieldReference field && field.Name == "errId");
        var roomGuard = manualStart.First(instruction => instruction.Operand is MethodReference method && method.Name == "get_Room");
        check(manualError.Next.OpCode.FlowControl == FlowControl.Cond_Branch && manualError.Offset < roomGuard.Offset
            && roomGuard.Next.OpCode.FlowControl == FlowControl.Cond_Branch && roomGuard.Next.Operand is Instruction missingRoom
            && missingRoom.Offset > manualChoice.Offset, "native manual start rejects server errors/missing room before CHOICE");
        var connect = Method("GameLogic.RoomLogic", "Connect").Body.Instructions;
        var manualHandler = connect.Single(instruction => instruction.OpCode.Code == Code.Ldftn
            && instruction.Operand is MethodReference method && method.Name == "OnStartGameS2CServerCallBack");
        check(connect.Any(instruction => instruction.OpCode.Code == Code.Stfld && instruction.Operand is FieldReference field
                && field.DeclaringType.FullName == "Core.Net.StartGameS2CRPC" && field.Name == "OnStartGameS2CServerCallBackAsync"
                && instruction.Offset > manualHandler.Offset)
            && connect.All(instruction => instruction.OpCode.FlowControl != FlowControl.Cond_Branch),
            "native common Connect registers host-start handler without host/password/player-count gates");
        var receive = Async("Core.Net.RPCMsgManager", "DealServerCallback").Body.Instructions;
        var dispatchStart = receive.Single(instruction => instruction.Operand is MethodReference method && method.Name == "PushStartGameS2CCallBack");
        check(receive.Any(instruction => instruction.OpCode.Code == Code.Ldc_I4 && Constant(instruction) == 5020 && instruction.Offset < dispatchStart.Offset)
            && dispatchStart.Previous.OpCode.Code == Code.Ceq && dispatchStart.Previous.Previous.OpCode.Code == Code.Conv_I8
            && Constant(dispatchStart.Previous.Previous.Previous) == 0 && dispatchStart.Previous.Previous.Previous.Previous.Operand is FieldReference sequence
            && sequence.Name == "upsn", "native start command forwards zero-sequence nonhost dispatch to the same RPC");
        var push = Async("Core.Net.StartGameS2CRPC", "PushStartGameS2CCallBack").Body.Instructions;
        var invokeStart = push.Single(instruction => instruction.Operand is MethodReference method && method.Name == "Invoke");
        check(invokeStart.Previous.Operand is FieldReference dispatch && dispatch.Name == "isDispatch"
            && push.All(instruction => instruction.Operand is not FieldReference field || field.Name != "isDispatch" || instruction == invokeStart.Previous),
            "native start RPC passes response/dispatch flag without rejecting nonhost dispatch");
        var startWrapper = Method("GameLogic.RoomLogic", "OnStartGameS2CServerCallBack");
        check(startWrapper.Parameters.Count == 3 && startWrapper.Parameters[2].ParameterType.FullName == "System.Boolean"
            && startWrapper.Body.Instructions.All(instruction => instruction.OpCode.Code != Code.Ldarg_3)
            && manualStartMethod.DeclaringType.Fields.All(field => field.Name != "isDispatch"),
            "native room start treats local responses and nonhost dispatch identically");
        var join = Async("GameLogic.RoomLogic", "DealJoinRoom").Body.Instructions;
        var joinCreate = join.Single(instruction => instruction.Operand is MethodReference method && method.Name == "CreateRoom");
        var joinPanel = join.Single(instruction => instruction.Operand is MethodReference method && method.Name == "OpenPanel");
        check(joinCreate.Offset < joinPanel.Offset && Constant(joinPanel.Previous.Previous) == 16
            && join.All(instruction => instruction.Operand is not MethodReference method || method.Name != "SwitchRoomState"),
            "native manual join opens RoomWait without synthesizing host start");
        var queue = Async("GameLogic.MatchLogic", "OnRefreshMatchTeamStateNotifyServerCallBackAsync").Body.Instructions;
        check(queue.Any(instruction => instruction.Operand is MethodReference method && method.FullName.Contains("MatchData::set_TeamId(", StringComparison.Ordinal))
            && queue.Any(instruction => instruction.Operand is MethodReference method && method.FullName.Contains("MatchData::set_MatchStatus(", StringComparison.Ordinal)), "public queue notify supplies live identity/status");
        var sync = Async("GameLogic.RoomLogic", "OnSyncRoomS2CServerCallBack").Body.Instructions;
        var syncStart = sync.Single(instruction => instruction.Operand is MethodReference method && method.Name == "SetConnectRoomId");
        var syncEnd = sync.Single(instruction => instruction.Operand is MethodReference method && method.Name == "ResetConnectRoomId");
        check(syncStart.Offset < syncEnd.Offset && sync.Where(instruction => instruction.Operand is MethodReference method && method.Name == "SwitchRoomState")
            .All(instruction => instruction.Offset > syncStart.Offset && instruction.Offset < syncEnd.Offset), "sync room state changes bracketed by connectRoomId");
        var pve = types["GameLogic.BattleConfig"].Methods.Single(method => method.Name == "IsPVE" && method.Parameters[0].ParameterType.FullName == "System.Int32");
        check(pve.IsPublic && pve.IsStatic && pve.Parameters.Count == 1 && pve.ReturnType.FullName == "System.Boolean", "exact native public PvE classifier signature");
        var configured = pve.Body.Instructions.Where(instruction => instruction.Operand is MethodReference method && method.FullName == "GameModeInfoConfigure UI.UIHelper::GetGameModeInfoConfigure(System.Int32)").ToArray();
        if (configured.Length == 0) {
            var nativeModes = pve.Body.Instructions.Where(instruction => instruction.Previous?.OpCode.Code == Code.Ldarg_0
                && instruction.OpCode.Name.StartsWith("ldc.i4", StringComparison.Ordinal)).Select(Constant).ToHashSet();
            check(VisibleCombatAdvisor.PveClassifier == null && nativeModes.SetEquals(new[] { 4, 6, 9, 10, 12 })
                && Enumerable.Range(-1, 16).All(mode => VisibleCombatAdvisor.IsPve(mode) == nativeModes.Contains(mode)), "historical standalone PvE default matches the native literal classifier exactly");
        } else {
            check(configured.Length == 1 && configured[0].Previous.OpCode.Code == Code.Ldarg_0, "current native classifier looks up the requested public configuration row once");
            var group = pve.Body.Instructions.Single(instruction => instruction.Operand is MethodReference method && method.FullName == "System.Int32 GameModeInfoConfigure::get_MapModeGroup()");
            check(Constant(group.Next) == 1 && group.Next.Next.OpCode.Code == Code.Ceq && group.Next.Next.Next.OpCode.Code == Code.Ret, "configured native PvE classification is exactly MapModeGroup == 1");
            var lookup = types["UI.UIHelper"].Methods.Single(method => method.Name == "GetGameModeInfoConfigure" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Int32");
            check(lookup.IsPublic && lookup.IsStatic && lookup.Parameters.Count == 1 && lookup.Parameters[0].ParameterType.FullName == "System.Int32" && lookup.ReturnType.FullName == "GameModeInfoConfigure", "exact public mode-row lookup ABI");
            Getter("GameModeInfoConfigure", "MapModeType", "MapModeType"); Getter("GameModeInfoConfigure", "MapModeGroup", "System.Int32");
            check(types["MapModeType"].Fields.Single(field => field.Name == "value__").FieldType.FullName == "System.Int32", "public configuration mode identity is an Int32 enum");
            var missing = pve.Body.Instructions.Single(instruction => instruction.OpCode.Code == Code.Ret && instruction.Offset < group.Offset);
            check(Constant(missing.Previous) == 1 && pve.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.FullName == "System.Void UnityEngine.Debug::LogError(System.Object)"), "native missing-row permissive fallback is identified, not adopted by the mod");
            var saved = VisibleCombatAdvisor.PveClassifier;
            try {
                VisibleCombatAdvisor.PveClassifier = _ => false; check(!VisibleCombatAdvisor.IsPve(4), "bound classifier rejection never falls back to historical PvE membership");
                VisibleCombatAdvisor.PveClassifier = mode => mode == 12345; check(VisibleCombatAdvisor.IsPve(12345) && !VisibleCombatAdvisor.IsPve(4), "bound classifier supports configured modes without assuming historical whitelist equivalence");
            } finally { VisibleCombatAdvisor.PveClassifier = saved; }
        }
        foreach (var (type, name) in new[] { ("party.model.Room", "get_Id"), ("party.model.Room", "get_MapType"), ("party.model.Room", "get_IsMatchRoom"),
            ("GameLogic.MatchData", "get_TeamId"), ("GameLogic.MatchData", "get_MatchStatus"), ("GameLogic.GameLogicManager", "get_connectRoomId") })
            check(Method(type, name).Body.Instructions.All(instruction => instruction.OpCode.Code is not (Code.Stfld or Code.Stsfld)
                && instruction.OpCode.FlowControl != FlowControl.Call), "native plain read has no calls/writes " + type + "." + name);
        using var product = AssemblyDefinition.ReadAssembly(typeof(MatchFocus).Assembly.Location);
        var adapter = product.MainModule.Types.Single(type => type.FullName == typeof(WindowsMatchFocus).FullName);
        var activation = adapter.Methods.Single(method => method.Name == "Activate").Body.Instructions;
        var foreground = activation.First(instruction => instruction.Operand is MethodReference method && method.Name == "SetForegroundWindow");
        var promotion = activation.Single(instruction => instruction.Operand is MethodReference method && method.Name == "SetWindowPos");
        check(Constant(promotion.Previous) == 0x213 && activation.Any(instruction => instruction.OpCode.Code == Code.Stfld
                && instruction.Operand is FieldReference field && field.Name == "_restoreWindow" && instruction.Offset < promotion.Offset)
            && adapter.Methods.Single(method => method.Name == "Activate").Body.ExceptionHandlers.Any(handler => handler.HandlerType == ExceptionHandlerType.Finally
                && activation.Any(instruction => instruction.Offset >= handler.HandlerStart.Offset && instruction.Offset < handler.HandlerEnd.Offset
                    && instruction.Operand is MethodReference method && method.Name == "RestoreTopmost")),
            "production temporary promotion reserves rollback before synchronous pulse and restores in finally");
        var rollback = adapter.Methods.Single(method => method.Name == "RestoreTopmost").Body.Instructions;
        var demotion = rollback.Single(instruction => instruction.Operand is MethodReference method && method.Name == "SetWindowPos");
        check(Constant(demotion.Previous) == 0x213
            && rollback.Count(instruction => instruction.Operand is MethodReference method && method.Name == "TryTopmost") == 2
            && rollback.All(instruction => instruction.Operand is not MethodReference method || method.Name != "IsWindowVisible"),
            "production rollback is synchronous, rechecks actual topmost style and permits hidden owned window");
        var raising = adapter.Methods.Single(method => method.Name == "Raise").Body.Instructions;
        var position = raising.Single(instruction => instruction.Operand is MethodReference method && method.Name == "SetWindowPos");
        check(Constant(position.Previous) == 0x4213,
            "production raise is async/noactivate without move/resize/owner changes");
        var after = position.Previous.Previous.Previous.Previous.Previous.Previous;
        check(after.Operand is FieldReference field && field.FullName == "System.IntPtr System.IntPtr::Zero"
            && Enumerable.Range(1, 4).All(index => Constant(raising[raising.IndexOf(position) - 1 - index]) == 0),
            "production uses HWND_TOP only, never permanent topmost or unrelated HWND");
        check(activation.First(instruction => instruction.Operand is MethodReference method && method.Name == "IsGameWindow").Offset < foreground.Offset
            && raising.Count(instruction => instruction.Operand is MethodReference method && method.Name == "IsForeground") == 2
            && raising.Count(instruction => instruction.Operand is MethodReference method && method.Name == "IsMinimized") == 2
            && raising.First(instruction => instruction.Operand is MethodReference method && method.Name == "IsForeground").Offset < position.Offset
            && raising.Last(instruction => instruction.Operand is MethodReference method && method.Name == "IsForeground").Offset > position.Offset,
            "production request revalidates own restored foreground Unity window before and after raising");
        var pending = product.MainModule.Types.Single(type => type.FullName == typeof(MatchFocusState).FullName).Methods.Single(method => method.Name == "Complete");
        var raisePending = product.MainModule.Types.Single(type => type.FullName == typeof(MatchFocusState).FullName).Methods.Single(method => method.Name == "CompleteRaise");
        check(pending.Body.Instructions.Concat(raisePending.Body.Instructions).All(instruction => instruction.Operand is not MethodReference method
            || method.Name is not ("Sleep" or "Wait" or "SendMessage" or "SendMessageTimeout" or "AttachThreadInput")),
            "production pending confirmation never blocks the game thread");
        var poller = product.MainModule.Types.Single(type => type.FullName == typeof(MatchFocus).FullName);
        var polling = poller.Methods.Single(method => method.Name == "Tick" && method.Parameters.Count == 1).Body.Instructions;
        check(polling.First(instruction => instruction.Operand is MethodReference method && method.Name == "RestoreTopmost").Offset
            < polling.First(instruction => instruction.Operand is MethodReference method && method.Name == "Allowed").Offset,
            "production cleanup precedes compatibility and game-state guards");
        check(poller.Methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions)
            .All(instruction => instruction.Operand is not MethodReference method || !method.Name.StartsWith("set_", StringComparison.Ordinal)
                && !method.Name.StartsWith("add_", StringComparison.Ordinal) && !method.Name.StartsWith("Request", StringComparison.Ordinal)
                && method.Name is not ("Call" or "StaticCall" or "Set" or "SetField")), "production poller has no event registrations/native writes/network requests");
    }
}
