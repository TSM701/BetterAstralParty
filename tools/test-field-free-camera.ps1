$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$identities = @('src/FieldFreeCamera.cs','src/FieldZoomUi.cs','focus-tests/free-camera/FieldFreeCameraFixture.cs','tools/test-field-free-camera.ps1') | ForEach-Object {
    [pscustomobject]@{ path = $_; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root $_)).Hash }
}
Add-Type -Path "$root/.deps/bepinex/BepInEx/core/Mono.Cecil.dll"
$native = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$root/.research/extracted/AstralParty.Runtime.dll.dll")
$cine = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$root/.research/extracted/Cinemachine.dll.dll")
$interop = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$root/../8vJXnINT/BepInEx/interop/Cinemachine.dll")
$unity = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$root/.research/extracted/UnityEngine.CoreModule.dll.dll")
function Require-Method($assembly, [string]$type, [string]$name, [string]$returns, [string[]]$parameters) {
    $methods = @($assembly.MainModule.GetType($type).Methods | Where-Object { $_.Name -ceq $name -and $_.IsPublic -and !$_.HasGenericParameters -and $_.ReturnType.FullName -ceq $returns -and ($_.Parameters.ParameterType.FullName -join '|') -ceq ($parameters -join '|') })
    if ($methods.Count -ne 1 -or $methods[0].IsStatic) { throw "Native instance API changed: $type.$name" }
    return $methods[0]
}
function Require-Field($assembly, [string]$type, [string]$name, [string]$fieldType, [bool]$public = $true, [bool]$static = $false) {
    $field = @($assembly.MainModule.GetType($type).Fields | Where-Object Name -CEQ $name)
    if ($field.Count -ne 1 -or $field[0].FieldType.FullName -cne $fieldType -or $field[0].IsPublic -ne $public -or $field[0].IsStatic -ne $static) { throw "Native field changed: $type.$name" }
}
try {
    foreach ($assembly in @($cine, $interop)) {
        $set = Require-Method $assembly 'Cinemachine.CinemachineBrain' 'SetCameraOverride' 'System.Int32' @('System.Int32','Cinemachine.ICinemachineCamera','Cinemachine.ICinemachineCamera','System.Single','System.Single')
        $release = Require-Method $assembly 'Cinemachine.CinemachineBrain' 'ReleaseCameraOverride' 'System.Void' @('System.Int32')
        $null = Require-Method $assembly 'Cinemachine.CinemachineVirtualCamera' 'GetComponentOwner' 'UnityEngine.Transform' @()
        $null = Require-Method $assembly 'Cinemachine.CinemachineVirtualCamera' 'InvalidateComponentPipeline' 'System.Void' @()
        $null = Require-Method $assembly 'Cinemachine.CinemachineVirtualCamera' 'ForceCameraPosition' 'System.Void' @('UnityEngine.Vector3','UnityEngine.Quaternion')
        Require-Field $assembly 'Cinemachine.LensSettings' 'Dutch' 'System.Single'
        if ($assembly -eq $interop) {
            foreach ($method in @($set, $release)) {
                if (!($method.Body.Instructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] -and $_.Name -ceq 'il2cpp_runtime_invoke' })) { throw 'Camera interop no longer uses the supported invoke bridge' }
            }
        }
    }
    foreach ($row in @(
        @('Cinemachine.CinemachineBrain','mFrameStack','System.Collections.Generic.List`1<Cinemachine.CinemachineBrain/BrainFrame>',$false),
        @('Cinemachine.CinemachineBrain/BrainFrame','id','System.Int32',$true),
        @('Cinemachine.CinemachineBrain/BrainFrame','blend','Cinemachine.CinemachineBlend',$true),
        @('Cinemachine.CinemachineBlend','CamB','Cinemachine.ICinemachineCamera',$true),
        @('Cinemachine.CinemachineVirtualCamera','m_Lens','Cinemachine.LensSettings',$true),
        @('Cinemachine.CinemachineTransposer','m_FollowOffset','UnityEngine.Vector3',$true))) {
        Require-Field $cine $row[0] $row[1] $row[2] $row[3]
    }
    foreach ($name in @('m_XDamping','m_YDamping','m_ZDamping')) { Require-Field $cine 'Cinemachine.CinemachineTransposer' $name 'System.Single' }
    foreach ($row in @(
        @('Core.Scene.BattleSceneController','freeCamera','Cinemachine.CinemachineVirtualCamera',$true,$false),
        @('Core.Scene.BattleSceneController','freeObject','Core.FreeCameraObject',$true,$false),
        @('Core.FreeCameraObject','status','Core.FreeCameraStatus',$true,$false),
        @('Core.FreeCameraObject','CameraSensitivity','System.Single',$false,$false),
        @('Core.FreeCameraObject','CameraSpeed','System.Single',$false,$false),
        @('Core.GameSettings','MouseControl','System.Boolean',$true,$true),
        @('Core.GameSettings','KeyControl','System.Boolean',$true,$true),
        @('GameLogic.BattleLogic','clientFinishReady','System.Boolean',$true,$false),
        @('GameLogic.FightLogic','fightStatus','System.Boolean',$true,$false),
        @('GameLogic.BattlePlayerData','CharacterInst','Core.Unit.Character',$true,$false),
        @('Core.Unit.Character','vCamera','Core.Camera.CharacterCamera',$true,$false),
        @('Core.Camera.CharacterCamera','vCamera','Cinemachine.CinemachineVirtualCamera',$true,$false))) {
        Require-Field $native $row[0] $row[1] $row[2] $row[3] $row[4]
    }
    $null = Require-Method $native 'GameLogic.BattleLogic' 'GetCurrentPlayer' 'GameLogic.BattlePlayerData' @()
    $null = Require-Method $native 'Core.Camera.CameraManager' 'CancelFreeStatus' 'System.Void' @()
    $null = Require-Method $native 'Core.Camera.CameraManager' 'SwitchCamera' 'Cysharp.Threading.Tasks.UniTask`1<System.Boolean>' @('Core.Camera.CharacterCamera')
    $hostReader = Require-Method $native 'Core.FreeCameraObject' 'GetHostStatus' 'System.Boolean' @()
    $hostReaderIl = ($hostReader.Body.Instructions | ForEach-Object ToString) -join "`n"
    if ($hostReaderIl -notmatch 'ldfld Core.FreeCameraStatus Core.FreeCameraObject::status[\s\S]*ldc.i4.2\nIL_0009: beq.s IL_0014[\s\S]*ldc.i4.3\nIL_000d: ceq\nIL_000f: ldc.i4.0\nIL_0010: ceq\nIL_0012: br.s IL_0015\nIL_0014: ldc.i4.0\nIL_0015: ldc.i4.0\nIL_0016: ceq\nIL_0018: ret') { throw 'Native host reader no longer protects queued Return during System/MapSignal' }
    $null = Require-Method $unity 'UnityEngine.GameObject' 'AddComponent' 'UnityEngine.Component' @('System.Type')
    $cancel = Require-Method $native 'FairyGUI.Stage' 'CancelClick' 'System.Void' @('System.Int32')
    $writes = @($cancel.Body.Instructions | Where-Object { $_.OpCode.Name -ceq 'stfld' } | ForEach-Object { $_.Operand.FullName })
    if ($writes.Count -ne 1 -or $writes[0] -cne 'System.Boolean FairyGUI.TouchInfo::clickCancelled') { throw 'Native CancelClick must only cancel the matching touch click' }
    $null = Require-Method $native 'FairyGUI.GObject' 'get_draggable' 'System.Boolean' @()
    Require-Field $native 'FairyGUI.UIConfig' 'clickDragSensitivity' 'System.Int32' $true $true
    $screenReader = @($native.MainModule.GetType('FairyGUI.Stage').Methods | Where-Object { $_.Name -ceq 'get_touchScreen' -and $_.IsPublic -and $_.IsStatic -and $_.ReturnType.FullName -ceq 'System.Boolean' -and $_.Parameters.Count -eq 0 })
    if ($screenReader.Count -ne 1) { throw 'Native desktop input mode reader changed' }
    $reset = $native.MainModule.GetType('FairyGUI.Stage').Methods | Where-Object Name -CEQ 'ResetInputState'
    $resetIl = ($reset.Body.Instructions | ForEach-Object ToString) -join "`n"
    if ($resetIl -notmatch 'ldc.i4.0\nIL_[0-9a-f]+: ldelem.ref\nIL_[0-9a-f]+: ldc.i4.0\nIL_[0-9a-f]+: stfld System.Int32 FairyGUI.TouchInfo::touchId') { throw 'Native desktop touch ID is no longer 0' }
    $positionReader = Require-Method $native 'FairyGUI.Stage' 'get_touchPosition' 'UnityEngine.Vector2' @()
    if (!($positionReader.Body.Instructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] -and $_.Name -ceq 'UpdateTouchPosition' })) { throw 'Stage position must refresh before indicator gesture origin is sampled' }
    $drag = @($native.MainModule.GetType('FairyGUI.GObject').Methods | Where-Object { $_.Name -ceq 'get_draggingObject' -and $_.IsPublic -and $_.IsStatic -and !$_.HasGenericParameters -and $_.ReturnType.FullName -ceq 'FairyGUI.GObject' -and $_.Parameters.Count -eq 0 })
    if ($drag.Count -ne 1) { throw 'Native drag ownership reader changed' }
    $statuses = @{ None = 0; Player = 1; System = 2; MapSignal = 3 }
    foreach ($name in $statuses.Keys) {
        $field = $native.MainModule.GetType('Core.FreeCameraStatus').Fields | Where-Object Name -CEQ $name
        if ($field.Constant -ne $statuses[$name]) { throw 'Native free camera host-state values changed' }
    }
    # Ordinary FIELD effects host input while their character/show camera blends in frame zero.
    foreach ($row in @(
        @('Core.ActionEffectShow','PlayPlayerShow','LimitCamera'),
        @('Core.ActionEffectShow','LimitCamera','HostFreeCamera'),
        @('Core.Camera.CameraManager','HostFreeCamera','SetHostCamera'))) {
        $method = @($native.MainModule.GetType($row[0]).Methods | Where-Object Name -CEQ $row[1])
        if ($method.Count -ne 1 -or !($method[0].Body.Instructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] -and $_.Name -ceq $row[2] })) { throw 'Native FIELD effect host-control chain changed' }
    }
    $hostMethod = $native.MainModule.GetType('Core.FreeCameraObject').Methods | Where-Object Name -CEQ 'SetHostCamera'
    $hostInstructions = @($hostMethod.Body.Instructions)
    if (!($hostInstructions | Where-Object { $_.OpCode.Name -ceq 'ldc.i4.2' }) -or !($hostInstructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] -and $_.Name -ceq 'UpdateStatus' })) { throw 'Native FIELD effects no longer set System host state' }
    $manual = $cine.MainModule.GetType('Cinemachine.CinemachineBrain').Methods | Where-Object Name -CEQ 'ManualUpdate'
    $manualCalls = @($manual.Body.Instructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] } | ForEach-Object Name)
    if ([array]::IndexOf($manualCalls,'UpdateFrame0') -lt 0 -or [array]::IndexOf($manualCalls,'UpdateFrame0') -ge [array]::IndexOf($manualCalls,'ComputeCurrentBlend') -or [array]::IndexOf($manualCalls,'ComputeCurrentBlend') -ge [array]::IndexOf($manualCalls,'ProcessActiveCamera')) { throw 'Native brain no longer advances its base frame before final output' }
    $wait = $native.MainModule.LookupToken([Mono.Cecil.MetadataToken]::new([uint32]0x0601048B))
    $waitIl = ($wait.Body.Instructions | ForEach-Object ToString) -join "`n"
    if ($wait.FullName -cne 'System.Boolean Core.Camera.CameraManager/<>c__DisplayClass8_0::<SwitchCamera>b__0()' -or
        $waitIl -notmatch 'op_Inequality.*\nIL_0024: brfalse.s IL_0036[\s\S]*get_IsBlending\(\)\nIL_0035: ret\nIL_0036: ldc.i4.0\nIL_0037: ret') { throw 'Native camera wait AND predicate changed; override timing requires a new audit' }
} finally { $native.Dispose(); $cine.Dispose(); $interop.Dispose(); $unity.Dispose() }
$source = (Get-Content -LiteralPath "$root/src/FieldFreeCamera.cs" -Raw).Replace('namespace BetterAstralParty;', '').Replace('using Cinemachine;', '').Replace('using Il2CppInterop.Runtime;', '').Replace('using UnityEngine;', '')
if ($source -match 'Harmony|Detour|Marshal\.|GetFunctionPointer|il2cpp_|\.SetField\(|\.Set\(|\b(Send|Dispatch|Register)\s*\(') { throw 'Free camera must not add detours or mutate native gameplay/settings/status' }
if ($source -match '(?m)^\s*(?!_camera\.Priority\s*=)[A-Za-z_][\w.]*\.Priority\s*=') { throw 'Only the owned virtual camera may set its priority' }
$caller = Get-Content -LiteralPath "$root/src/FieldZoomUi.cs" -Raw
foreach ($contract in @(
    '!Plugin\.FieldZoom\.Value\s*\|\|\s*PvpSafety\.Suspended\s*\|\|\s*root\s*==\s*null',
    '!Alive\(scene\)\s*\|\|\s*!VisibleCombatAdvisor\.IsPve',
    'var blocked\s*=\s*status is not \(0 or 1 or 2 or 3\)\s*\|\|\s*!Alive\(cameraObject\)',
    '!new Camera\(cameraObject!\.Pointer\)\.enabled',
    '!new Camera\(cameraObject\.Pointer\)\.gameObject\.activeInHierarchy',
    'logic\?\.Get\("battle"\)\?\.Field<bool>\("clientFinishReady"\)\s*!=\s*true',
    'frames\?\.Get<int>\("Count"\)\s*!=\s*1',
    'blend\?\.Field\("CamB"\)\?\.Pointer\s*!=\s*active\.Pointer',
    'brain!\.Get<bool>\("IsBlending"\)\s*\|\|\s*!blend\.Get<bool>\("IsComplete"\)',
    'GameUi\.Find\(root,\s*"FightWindow",\s*maxDepth:\s*1\)\s*!=\s*null',
    'FieldFreeCamera\.Update\(scene,\s*brain!,\s*free,\s*player,\s*blocked\)',
    'var inputAllowed\s*=\s*FieldInputAllowed\(root,\s*pan:\s*true\)',
    '!Application\.isFocused\s*\|\|\s*ModUi\.IsOpen\s*\|\|\s*Input\.touchCount\s*!=\s*0\s*\|\|\s*root\.Get<bool>\("modalWaiting"\)',
    'RuntimeObject\.StaticCall\(RuntimeObject\.FindClass\("FairyGUI",\s*"GObject"\),\s*"get_draggingObject"\)\s*!=\s*null',
    'root\.Get\("focus"\)\?\.Get\("asTextInput"\)\?\.Get<bool>\("focused"\)\s*==\s*true',
    'pane\.Get<bool>\("touchEffect"\)\s*&&\s*\(pan\s*\|\|\s*pane\.Get<bool>\("mouseWheelEnabled"\)\)',
    '"get_isTouchOnUI"',
    'FieldFreeCamera\.Pan\(free!,\s*inputAllowed\s*&&\s*!FieldCameraReturnUi\.ConsumedInput,\s*onUi,\s*indicator\s*&&\s*Input\.GetMouseButtonDown\(0\)\)',
    'if\s*\(held\s*&&\s*!FieldFreeCamera\.Holding\)\s*\{\s*_pendingWheel\s*=\s*0;\s*return;',
    'Attempt\(FieldFreeCamera\.Clear\)',
    '!float\.IsFinite\(lo\.x\)', '!float\.IsFinite\(hi\.z\)',
    'lo\.x\s*>\s*hi\.x\s*\|\|\s*lo\.y\s*>\s*hi\.y\s*\|\|\s*lo\.z\s*>\s*hi\.z')) {
    if ($caller -notmatch $contract) { throw "Production free-camera caller guard changed: $contract" }
}
if ($caller -match 'HasInputWindow|var hosted|root\.Get<bool>\("hasModalWindow"\)|root\.Get<bool>\("hasAnyPopup"\)') { throw 'Permitted field UI/native host status must not create a second Pan-only lock' }
$presentation = [regex]::Match($caller, 'var blocked\s*=[\s\S]*?;').Value
if ($presentation -match 'FightWindow|fightStatus|!held\s*&&\s*hosted') { throw 'Preparation windows or host-input locks must not release the visible field camera' }
if ($source -notmatch 'if\s*\(Input\.GetMouseButtonDown\(0\)\)\s*\{\s*_dragging\s*=\s*_indicatorPress\s*=\s*false;\s*if\s*\(!onUi\s*\|\|\s*fieldIndicator' -or
    $source -notmatch 'stage\.Call\("CancelClick",\s*0\)' -or $source -notmatch 'moved\.sqrMagnitude\s*>\s*threshold\s*\*\s*\(float\)threshold') { throw 'Fresh world/indicator gestures and native desktop moved-click cancellation must remain distinct' }
if ($source -notmatch 'free\?\.Call\("GetHostStatus"\)\?\.Value<bool>\(\)\s*!=\s*false') { throw 'Queued Return must wait for native host-input release' }
$harness = Get-Content -LiteralPath "$root/focus-tests/free-camera/FieldFreeCameraFixture.cs" -Raw
Add-Type -TypeDefinition $harness.Replace('__PRODUCTION_SOURCE__', $source) -CompilerOptions '/nowarn:8602,8604'
[FreeCameraTest.Check]::Run()
foreach ($identity in $identities) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root $identity.path)).Hash -cne $identity.sha256) { throw "Test input changed during verification: $($identity.path)" }
}
Write-Host 'PASS: exact native/generated camera APIs including float LensSettings.Dutch, FIELD host-control chain, base-frame output order and camera-wait AND predicate; real production source under strict substitutes; rendered orientation without double Dutch or native lens mutation, other-character/card camera blend retention without override churn, unknown/dead camera and foreign-frame yielding, queued native return, world-origin drag capture across UI with gated starts/cancellation, input/pan bounds, partial native insertion and cleanup failure retry. No game execution; native wait early-completion timing and visual calibration remain pending.'
$identities | ConvertTo-Json -Depth 3 -Compress
