# Read metadata and pin the incoming bytes; never execute the incoming plugin.
function Read-BapInstallJson([byte[]]$Bytes) {
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes).TrimStart([char]0xFEFF)
    $trimmed=$text.Trim()
    if (!$trimmed.StartsWith('{') -or !$trimmed.EndsWith('}')) { throw '설치 JSON 최상위 값은 객체여야 합니다. / Installation JSON root must be an object.' }
    $stack=[Collections.Generic.List[object]]::new()
    $tokens=[regex]::Matches($text,'"(?:[^"\\\x00-\x1F]|\\(?:["\\/bfnrt]|u[0-9A-Fa-f]{4}))*"|[{}\[\]:,]')
    if($tokens.Count -gt 100000){throw '설치 JSON이 너무 큽니다. / Installation JSON exceeds the limit.'}
    foreach($token in $tokens){
        $v=$token.Value
        if($v -eq '{'){$stack.Add(@{Object=$true;WantKey=$true;Keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)})}
        elseif($v -eq '['){$stack.Add(@{Object=$false})}
        elseif($v -eq '}' -or $v -eq ']'){if($stack.Count){$stack.RemoveAt($stack.Count-1)}}
        elseif($stack.Count){
            $top=$stack[$stack.Count-1]
            if($v -eq ',' -and $top.Object){$top.WantKey=$true}
            elseif($v -eq ':'){$top.WantKey=$false}
            elseif($v.StartsWith('"') -and $top.Object -and $top.WantKey){
                $key=$v|ConvertFrom-Json
                if(!$top.Keys.Add($key)){throw '설치 JSON에 중복 속성이 있습니다. / Duplicate installation JSON property.'}
                $top.WantKey=$false
            }
        }
    }
    return ($text|ConvertFrom-Json)
}
function Get-BapInstallHash([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
}
function Read-BapInstallFile([string]$Path) {
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
    try {
        if ($stream.Length -gt 64MB) { throw '설치 파일이 너무 큽니다. / Installation file exceeds the limit.' }
        $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); return ,$memory.ToArray() }
        finally { $memory.Dispose() }
    } finally { $stream.Dispose() }
}
function Get-BapInstallVersion([string]$Text) {
    if (!$Text -or $Text.Length -gt 128 -or $Text -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z') {
        throw '설치 버전을 확인할 수 없습니다. / Invalid installation version.'
    }
    $parts = @()
    foreach ($component in @($Matches[1], $Matches[2], $Matches[3])) {
        $value = 0
        if (![int]::TryParse($component, [ref]$value)) { throw '설치 버전 숫자가 범위를 넘었습니다. / Installation version number exceeds the supported range.' }
        $parts += $value
    }
    $pre = if ($Matches[4]) { @($Matches[4].Split('.')) } else { @() }
    foreach ($p in $pre) { if ($p -match '^[0-9]+$' -and $p.Length -gt 1 -and $p[0] -eq '0') { throw '잘못된 설치 버전입니다. / Invalid prerelease version.' } }
    return @{ Core=$parts; Pre=$pre; Text=$Text }
}
function Compare-BapInstallVersion($Left, $Right) {
    for ($i=0; $i -lt 3; $i++) { if ($Left.Core[$i] -ne $Right.Core[$i]) { return $Left.Core[$i].CompareTo($Right.Core[$i]) } }
    if (!$Left.Pre.Count) { if (!$Right.Pre.Count) { return 0 }; return 1 }
    if (!$Right.Pre.Count) { return -1 }
    for ($i=0; $i -lt [Math]::Min($Left.Pre.Count,$Right.Pre.Count); $i++) {
        $a=$Left.Pre[$i]; $b=$Right.Pre[$i]; $an=$a -match '^[0-9]+$'; $bn=$b -match '^[0-9]+$'
        if ($an -and $bn) { $cmp=$a.Length.CompareTo($b.Length); if (!$cmp) { $cmp=[string]::CompareOrdinal($a,$b) } }
        elseif ($an -ne $bn) { $cmp=if($an){-1}else{1} }
        else { $cmp=[string]::CompareOrdinal($a,$b) }
        if ($cmp) { return [Math]::Sign($cmp) }
    }
    return [Math]::Sign($Left.Pre.Count - $Right.Pre.Count)
}
function Read-BapInstallMetadata([byte[]]$Bytes, [switch]$Launcher) {
    $memory=[IO.MemoryStream]::new($Bytes,$false)
    $assembly=$null
    try {
        $assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($memory)
        $name=if($Launcher){'SteamLauncher'}else{'BetterAstralParty.Plugin'}
        $type=@($assembly.MainModule.Types | Where-Object FullName -ceq $name)
        if ($type.Count -ne 1) { throw '설치 구성품의 메타데이터가 맞지 않습니다. / Unexpected component metadata.' }
        $protocol=@($type[0].Fields | Where-Object { $_.Name -ceq 'InstallBundleProtocol' -and $_.IsLiteral })
        $value=if($protocol.Count -eq 1){$protocol[0].Constant}else{0}
        if ($Launcher) {
            foreach($api in @('GuardModLaunch','CheckUpdateState','ReadInstallerState')) {
                $m=@($type[0].Methods|Where-Object {$_.Name -ceq $api -and $_.IsPublic -and $_.IsStatic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -ceq 'System.String'})
                $returns=if($api -eq 'GuardModLaunch'){'System.IDisposable'}elseif($api -eq 'ReadInstallerState'){'System.String'}else{'System.Void'}
                if($m.Count -ne 1 -or $m[0].ReturnType.FullName -cne $returns){throw '런처 안전 API가 없는 구형 구성품입니다. 최신 설치 묶음을 사용하세요. / Required launcher safety API is missing.'}
            }
            $maintenance=@($type[0].Methods|Where-Object {$_.Name -ceq 'GuardMaintenance' -and $_.IsPublic -and $_.IsStatic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -ceq 'System.String' -and $_.ReturnType.FullName -ceq 'System.IDisposable'})
            $validate=@($type[0].Methods|Where-Object {$_.Name -ceq 'ValidateHelperBootstrap' -and $_.IsPublic -and $_.IsStatic -and $_.Parameters.Count -eq 3 -and $_.ReturnType.FullName -ceq 'System.String' -and @($_.Parameters|Where-Object {$_.ParameterType.FullName -cne 'System.String'}).Count -eq 0})
            $install=@($type[0].Methods|Where-Object {$_.Name -ceq 'InstallHelperBootstrap' -and $_.IsPublic -and $_.IsStatic -and $_.Parameters.Count -eq 4 -and $_.ReturnType.FullName -ceq 'System.Int32' -and @($_.Parameters|Where-Object {$_.ParameterType.FullName -cne 'System.String'}).Count -eq 0})
            $wholeApi=$true
            foreach ($definition in @(@('ValidateWholeBridge',3,'System.String'),@('PrepareWholeReceipt',5,'System.String'),@('RecoverWholeHelper',3,'System.Void'),@('InstallWholeHelper',5,'System.Void'),@('GuardWholeRecovery',3,'System.IDisposable'),@('BeginWholeInstall',5,'System.IDisposable'))) {
                $api=@($type[0].Methods | Where-Object {$_.Name -ceq $definition[0] -and $_.IsPublic -and $_.IsStatic -and $_.Parameters.Count -eq $definition[1] -and $_.ReturnType.FullName -ceq $definition[2] -and @($_.Parameters | Where-Object {$_.ParameterType.FullName -cne 'System.String'}).Count -eq 0})
                if ($api.Count -ne 1) { $wholeApi=$false }
            }
            $ordinary=@($type[0].Methods|Where-Object {$_.Name -ceq 'RestoreOrdinaryInstall' -and $_.IsPublic -and $_.IsStatic -and $_.ReturnType.FullName -ceq 'System.Void' -and $_.Parameters.Count -eq 7 -and $_.Parameters[0].ParameterType.FullName -ceq 'System.String' -and $_.Parameters[1].ParameterType.FullName -ceq 'System.String' -and $_.Parameters[2].ParameterType.FullName -ceq 'System.String' -and $_.Parameters[3].ParameterType.FullName -ceq 'System.String[]' -and $_.Parameters[4].ParameterType.FullName -ceq 'System.String' -and $_.Parameters[5].ParameterType.FullName -ceq 'System.Boolean' -and $_.Parameters[6].ParameterType.FullName -ceq 'System.String'})
            $removed=@($type[0].Methods|Where-Object {$_.Name -ceq 'ValidateRemovedHelperEvidence' -and $_.IsPublic -and $_.IsStatic -and $_.ReturnType.FullName -ceq 'System.String' -and $_.Parameters.Count -eq 3 -and $_.Parameters[0].ParameterType.FullName -ceq 'System.String' -and $_.Parameters[1].ParameterType.FullName -ceq 'System.Byte[]' -and $_.Parameters[2].ParameterType.FullName -ceq 'System.Byte[]'})
            $preflight=@($type[0].Methods|Where-Object {$_.Name -ceq 'ValidateWholeInputs' -and $_.IsPublic -and $_.IsStatic -and $_.ReturnType.FullName -ceq 'System.Void' -and $_.Parameters.Count -eq 3 -and @($_.Parameters | Where-Object {$_.ParameterType.FullName -cne 'System.String'}).Count -eq 0})
            return @{Protocol=$value;MaintenanceApi=($maintenance.Count -eq 1);HelperBootstrapApi=($validate.Count -eq 1 -and $install.Count -eq 1);WholeBridgeApi=$wholeApi;OrdinaryRecoveryApi=($ordinary.Count -eq 1);RemovedHelperEvidenceApi=($removed.Count -eq 1 -and $preflight.Count -eq 1)}
        }
        $version=@($type[0].Fields|Where-Object {$_.Name -ceq 'Version' -and $_.IsLiteral})
        $guid=@($type[0].Fields|Where-Object {$_.Name -ceq 'Guid' -and $_.IsLiteral})
        $pluginName=@($type[0].Fields|Where-Object {$_.Name -ceq 'Name' -and $_.IsLiteral})
        $attr=@($type[0].CustomAttributes|Where-Object {$_.AttributeType.FullName -ceq 'BepInEx.BepInPlugin'})
        if($version.Count -ne 1 -or $guid.Count -ne 1 -or $pluginName.Count -ne 1 -or $guid[0].Constant -cne 'kr.betterastralparty.mod' -or $pluginName[0].Constant -cne 'BetterAstralParty' -or $attr.Count -ne 1 -or $attr[0].ConstructorArguments.Count -ne 3 -or $attr[0].ConstructorArguments[0].Value -cne $guid[0].Constant -or $attr[0].ConstructorArguments[1].Value -cne $pluginName[0].Constant -or $attr[0].ConstructorArguments[2].Value -cne $version[0].Constant){throw 'DLL의 모드 식별자·버전이 일치하지 않습니다. / Plugin identity/version mismatch.'}
        $null=Get-BapInstallVersion $version[0].Constant
        $updateFields=@($type[0].Fields|Where-Object {$_.Name -ceq 'UpdateProtocol' -and $_.IsLiteral})
        $settingsFields=@($type[0].Fields|Where-Object {$_.Name -ceq 'SettingsSchema' -and $_.IsLiteral})
        if($updateFields.Count -gt 1 -or $settingsFields.Count -gt 1){throw 'Ambiguous update metadata.'}
        $updateValue=if($updateFields.Count -eq 1){$updateFields[0].Constant}else{''}
        $settingsValue=if($settingsFields.Count -eq 1){$settingsFields[0].Constant}else{''}
        if($updateValue -cnotin @('','1','2') -or $settingsValue -cnotin @('','1')){throw 'Unsupported update/settings metadata.'}
        return @{Protocol=$value;Version=$version[0].Constant;UpdateProtocol=$updateValue;SettingsSchema=$settingsValue}
    } finally { if($assembly){$assembly.Dispose()}; $memory.Dispose() }
}
function Assert-BapWholeUpdateProtocol($Plugin, $Launcher=$null) {
    if ($Plugin.UpdateProtocol -ceq '2' -and (!$Launcher -or !$Launcher.WholeBridgeApi -or $Plugin.SettingsSchema -cne '1')) {
        throw 'Verified whole channel-migration APIs and settings contract required; existing files preserved.'
    }
}
function Open-BapInstallBundle([string]$PackageRoot,[string]$GameRoot,[string]$RecoveryPlan) {
    $leases=[Collections.Generic.List[IDisposable]]::new()
    try {
        $package=[IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
        $game=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
        if($package -eq $game -or $package.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase) -or $game.StartsWith($package+'\',[StringComparison]::OrdinalIgnoreCase)){throw '설치 입력과 게임 폴더는 분리되어야 합니다. / Package and game directories must be separate.'}
        $manifestPath=Join-Path $package 'package-manifest.json'
        if(!(Test-Path -LiteralPath $manifestPath -PathType Leaf)){throw '검증된 설치 묶음이 없습니다. 소스 폴더나 예전 install.cmd 대신 최신 전체 설치 묶음을 사용하세요. / Verified installation manifest required.'}
        Assert-InstallTarget $package $manifestPath
        $stream=[IO.File]::Open($manifestPath,'Open','Read','Read');$leases.Add($stream)
        $manifestBytes=Read-BapInstallFile $manifestPath
        $manifest=Read-BapInstallJson $manifestBytes
        if(($manifest.schema -isnot [int] -and $manifest.schema -isnot [long]) -or $manifest.schema -ne 1 -or $manifest.version -isnot [string] -or $manifest.kind -isnot [string] -or !$manifest.kind -or $manifest.files -isnot [array] -or @($manifest.files).Count -lt 10){throw '설치 manifest 형식이 잘못되었습니다. / Invalid installation manifest.'}
        $version=Get-BapInstallVersion $manifest.version
        $hashes=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($f in $manifest.files){
            if($f.path -isnot [string] -or !$f.path -or $f.sha256 -isnot [string] -or $f.sha256 -cnotmatch '\A[A-F0-9]{64}\z' -or [IO.Path]::IsPathRooted($f.path) -or $f.path -match '(^|[\\/])\.\.([\\/]|$)|:' -or $hashes.ContainsKey($f.path)){throw '설치 파일 목록이 잘못되었습니다. / Unsafe or duplicate installation inventory.'}
            if($f.path -match '\\|[<>:"|?*\x00-\x1F]' -or ($f.bytes -isnot [int] -and $f.bytes -isnot [long]) -or $f.bytes -lt 0){throw '설치 경로·파일 길이 형식이 잘못되었습니다. / Invalid inventory path/length type.'}
            foreach($part in $f.path.Split('/')){
                if(!$part -or $part -ne $part.Trim() -or $part.EndsWith('.') -or $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)'){throw '설치 경로 별칭은 허용하지 않습니다. / Installation path alias rejected.'}
            }
            $path=Join-Path $package $f.path;Assert-InstallTarget $package $path
            $s=[IO.File]::Open($path,'Open','Read','Read');$leases.Add($s)
            if((Get-FileHash -InputStream $s -Algorithm SHA256).Hash -cne $f.sha256){throw ('설치 묶음 파일이 변경되었습니다: '+$f.path+' / Package hash mismatch.')}
            if($null -ne $f.bytes -and $s.Length -ne $f.bytes){throw '설치 파일 길이가 맞지 않습니다. / Package length mismatch.'}
            $hashes.Add($f.path,$f.sha256)
        }
        foreach($p in @('install.cmd','Uninstall.cmd','tools/install-policy.ps1','tools/approved-install-bundles.json','tools/install.ps1','tools/install-bootstrap.ps1','tools/install-backup.ps1','tools/install-log.ps1','tools/find-game.ps1','tools/steam-session.ps1','tools/set-loader-mode.ps1','tools/uninstall.ps1','.deps/bepinex/BepInEx/core/Mono.Cecil.dll')) {
            if(!$hashes.ContainsKey($p)){throw ('필수 설치 검사가 묶음에 없습니다: '+$p+' / Required installer component missing.')}
        }
        $cecil=Join-Path $package '.deps/bepinex/BepInEx/core/Mono.Cecil.dll'
        if($hashes['.deps/bepinex/BepInEx/core/Mono.Cecil.dll'] -cne '769A59793D4B8885BBBFBC5AEE8F57A0D4E34D275C56C60C03994309B87F67E9'){throw '메타데이터 검사 도구가 일치하지 않습니다. / Metadata reader hash mismatch.'}
        if(-not ('Mono.Cecil.AssemblyDefinition' -as [type])){Add-Type -Path $cecil}
        $owned=@{
            'AstralParty-Vanilla.cmd'='tools/AstralParty-Vanilla.cmd'
            'BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll'='artifacts/BetterAstralParty.dll'
            'BetterAstralParty-Launcher.exe'='artifacts/BetterAstralParty-Launcher.exe'
            'BetterAstralParty-Mod.cmd'='tools/BetterAstralParty-Mod.cmd'
            'BetterAstralParty-Steam.ps1'='tools/BetterAstralParty-Steam.ps1'
            'BetterAstralParty.compatibility.json'='artifacts/BetterAstralParty.compatibility.json'
            'check-compatibility.ps1'='tools/check-compatibility.ps1'
            'steam-shortcut.ps1'='tools/steam-shortcut.ps1'
        }
        $expected=@{}
        foreach($p in $owned.Keys){if(!$hashes.ContainsKey($owned[$p])){throw '설치 소유 파일 8개가 모두 필요합니다. / Missing owned installation file.'};$expected[$p]=$hashes[$owned[$p]]}
        if($expected['BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll'] -ceq '2C6524A88482BFB6E394FBE0A9F6A0E3EE3E42FE15ED62577E33308184BDAD24' -or $expected['BetterAstralParty-Launcher.exe'] -ceq '25CAF5F38626545670BD34A5FB39C8193827499D204276003158630A24039C69'){throw '자동 업데이트와 호환되지 않는 과거 GA 산출물입니다. 버전 표기와 관계없이 설치를 중단합니다. / Obsolete pre-updater GA image blocked.'}
        $plugin=Read-BapInstallMetadata (Read-BapInstallFile (Join-Path $package $owned['BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll']))
        $launcher=Read-BapInstallMetadata (Read-BapInstallFile (Join-Path $package $owned['BetterAstralParty-Launcher.exe'])) -Launcher
        if($plugin.Version -cne $manifest.version){throw 'manifest와 DLL 버전이 다릅니다. / Manifest/plugin version mismatch.'}
        Assert-BapWholeUpdateProtocol $plugin $launcher
        if (!$launcher.OrdinaryRecoveryApi) { throw '안전한 핸들 기반 복구 검사기가 필요합니다. 이전 검사기로 이 번들을 설치할 수 없습니다. / Held-handle ordinary recovery inspector required; old verifier cannot authorize this bundle.' }
        if ($hashes.ContainsKey('tools/install-reinstall.ps1') -and !$launcher.RemovedHelperEvidenceApi) { throw '공통 재설치를 지원하는 검사기가 필요합니다. / Version-neutral reinstall inspector required.' }
        if ($plugin.UpdateProtocol -ceq '2') {
            foreach ($proof in @('update-proof/update.descriptor','update-proof/update.signature')) {
                if (!$hashes.ContainsKey($proof)) { throw 'Signed owned8 channel proof required for v2 whole installation.' }
            }
        }
        $plan=$null
        if($RecoveryPlan){
            $planBytes=Read-BapInstallFile $RecoveryPlan;$plan=Read-BapInstallJson $planBytes
            if(($plan.schema -isnot [int] -and $plan.schema -isnot [long]) -or $plan.schema -ne 1 -or $plan.purpose -isnot [string] -or $plan.packageManifestSha256 -isnot [string] -or $plan.gameRoot -isnot [string] -or $plan.toVersion -isnot [string] -or $plan.fromVersion -isnot [string] -or $plan.receiptSha256 -isnot [string] -or $plan.purpose -cne 'manual-recovery' -or $plan.packageManifestSha256 -cne (Get-BapInstallHash $manifestBytes) -or $plan.gameRoot -cne $game -or $plan.toVersion -cne $manifest.version){throw '복구 계획이 현재 경로·입력 묶음과 다릅니다. / Recovery plan binding mismatch.'}
        }
        $legacyRecovery=$plan -and $plan.allowLegacyProtocol -is [bool] -and $plan.allowLegacyProtocol -and $plugin.Protocol -eq 0 -and $launcher.Protocol -eq 0
        if(!$legacyRecovery -and ($plugin.Protocol -isnot [int] -or $launcher.Protocol -isnot [int] -or $plugin.Protocol -ne 1 -or $launcher.Protocol -ne 1 -or !$launcher.MaintenanceApi)){throw 'DLL·런처가 같은 설치 규약의 묶음이 아닙니다. 최신 전체 설치 묶음을 사용하세요. / Incompatible installation components.'}
        # A review record in the trusted package is not an authentication proof.
        # Never generate/adopt catalog entries at install time.
        [string[]]$names=@($hashes.Keys|Where-Object {$_ -cne 'tools/approved-install-bundles.json'})
        [Array]::Sort($names,[StringComparer]::Ordinal)
        $identity=@('BetterAstralParty.InstallBundle/v1',('version='+$manifest.version),('kind='+$manifest.kind),('protocol='+$plugin.Protocol))
        foreach($n in $names){$identity+=($n+'='+$hashes[$n])}
        $build=Get-BapInstallHash ([Text.Encoding]::UTF8.GetBytes(($identity -join [char]10)+[char]10))
        if($manifest.buildIdentity -isnot [string] -or $manifest.buildIdentity -cne $build){throw '설치 묶음 identity가 전체 파일 목록과 다릅니다. / Bundle identity mismatch.'}
        $catalog=Read-BapInstallJson (Read-BapInstallFile (Join-Path $package 'tools/approved-install-bundles.json'))
        if(($catalog.schema -isnot [int] -and $catalog.schema -isnot [long]) -or $catalog.schema -ne 1 -or $catalog.bundles -isnot [array]){throw '설치 검토 목록이 잘못되었습니다. / Invalid build review catalog.'}
        $approved=@($catalog.bundles|Where-Object {$_.buildIdentity -is [string] -and $_.version -is [string] -and $_.kind -is [string] -and ($_.protocol -is [int] -or $_.protocol -is [long]) -and $_.buildIdentity -ceq $build -and $_.version -ceq $manifest.version -and $_.kind -ceq $manifest.kind -and $_.protocol -eq $plugin.Protocol})
        if($approved.Count -ne 1){throw '검토 등록되지 않은 설치 묶음입니다. 버전이 높아도 설치하지 않습니다. / Unregistered installation bundle.'}
        $bundle=@{Leases=$leases;PackageRoot=$package;GameRoot=$game;Expected=$expected;SourceHashes=$hashes;Version=$version;BuildIdentity=$build;ManifestSha256=(Get-BapInstallHash $manifestBytes);Recovery=$plan;Mode=$null;Observed=$null;Verifier=$null;RootIdentity=$null}
        $state=Get-BapInstallState $bundle
        $bundle.RootIdentity=$state.RootIdentity
        if($plan -and ($plan.gameRootIdentity -isnot [string] -or $plan.gameRootIdentity -cne $bundle.RootIdentity)){throw '복구 계획의 실제 설치 root가 다릅니다. / Recovery plan root identity mismatch.'}
        return $bundle
    } catch { foreach($s in $leases){$s.Dispose()};throw }
}
function Get-BapInstallVerifier($Bundle) {
    # Only an already approved, pinned complete bundle supplies executable inspection code.
    if(!$Bundle -or !$Bundle.BuildIdentity -or !$Bundle.Leases -or !$Bundle.Expected){throw '검토된 전체 검사 묶음이 필요합니다. / Approved complete verifier bundle required.'}
    $path=Join-Path $Bundle.PackageRoot 'artifacts/BetterAstralParty-Launcher.exe'
    $bytes=Read-BapInstallFile $path
    if((Get-BapInstallHash $bytes) -cne $Bundle.Expected['BetterAstralParty-Launcher.exe']){throw '검증한 외부 검사 바이트가 변경되었습니다. / Verified external inspector bytes changed.'}
    if(!$Bundle.Verifier){$Bundle.Verifier=[Reflection.Assembly]::Load($bytes)}
    return $Bundle.Verifier
}
function Get-BapInstallState($Bundle) {
    $assembly=Get-BapInstallVerifier $Bundle
    $method=$assembly.GetType('SteamLauncher').GetMethod('ReadInstallerState',[Reflection.BindingFlags]'Public,Static')
    try{$value=$method.Invoke($null,@([string]$Bundle.GameRoot))}
    catch{throw ('업데이트 버전 기록 또는 root를 확인할 수 없습니다. 기록·백업을 보존하세요. / Invalid update high-water record or root identity; retain records/backups. '+$_.Exception.GetBaseException().Message)}
    if($value -isnot [string] -or $value.Length -gt 320 -or $value -cnotmatch '\ABetterAstralParty.InstallerState/v1\nroot=([A-F0-9]{8}:[A-F0-9]{16})\nversion=([^\r\n]*)\nwater-sha=([A-F0-9]{64})?\n\z'){throw '검사기의 설치 상태 응답이 잘못되었습니다. / Invalid installer state response.'}
    return @{RootIdentity=$Matches[1];HighestVersion=$Matches[2];WaterHash=$(if($Matches[3]){$Matches[3]}else{$null})}
}
function Assert-BapInstallRoot($Bundle) {
    $state=Get-BapInstallState $Bundle
    if($state.RootIdentity -cne $Bundle.RootIdentity){throw '검사 후 설치 root가 교체되었습니다. 변경하지 않고 중단합니다. / Installation root identity changed after preflight.'}
    return $state
}
function Assert-BapInstallTransition($Bundle,[switch]$ConfirmRecovery) {
    $state=Assert-BapInstallRoot $Bundle
    $game=$Bundle.GameRoot;$receiptPath=Join-Path $game 'BetterAstralParty.install.json'
    $observed=@{}
    foreach($p in @($Bundle.Expected.Keys)+@('BetterAstralParty.install.json','BetterAstralParty.update.version')){
        $path=Join-Path $game $p;Assert-InstallTarget $game $path
        $observed[$p]=if(Test-Path -LiteralPath $path -PathType Leaf){Get-BapInstallHash (Read-BapInstallFile $path)}else{$null}
    }
    if($observed['BetterAstralParty.update.version'] -cne $state.WaterHash){throw '검사 중 업데이트 버전 기록이 변경되었습니다. / High-water changed during preflight.'}
    if(!$observed['BetterAstralParty.install.json']){
        if($Bundle.Recovery -or @($Bundle.Expected.Keys|Where-Object {$observed[$_]}).Count -or $observed['BetterAstralParty.update.version']){throw '설치 기록이 없거나 일부 파일만 있습니다. 수동 복구 검토가 필요합니다. / Incomplete installation requires recovery review.'}
        $Bundle.Mode='FirstInstall';$Bundle.Observed=$observed;return
    }
    $receipt=Read-BapInstallJson (Read-BapInstallFile $receiptPath)
    if(($receipt.schema -isnot [int] -and $receipt.schema -isnot [long]) -or $receipt.schema -ne 1 -or $receipt.owner -isnot [string] -or $receipt.owner -cne 'kr.betterastralparty.mod' -or @($receipt.PSObject.Properties).Count -ne $(if($receipt.PSObject.Properties.Name -ccontains 'channelState'){5}else{4}) -or ($null -ne $receipt.loaderBefore -and $receipt.loaderBefore -isnot [bool]) -or @($receipt.files).Count -ne 8){throw '기존 설치 기록을 확인할 수 없습니다. / Invalid existing installation receipt.'}
    $seen=@{}
    foreach($f in $receipt.files){if($f.path -isnot [string] -or !($Bundle.Expected.Keys -ccontains $f.path) -or $seen.ContainsKey($f.path) -or $f.sha256 -isnot [string] -or $f.sha256 -cnotmatch '\A[A-F0-9]{64}\z'){throw '기존 설치 기록의 소유 파일 목록이 잘못되었습니다. / Invalid existing owned inventory.'};$seen[$f.path]=$f.sha256}
    $installed=Read-BapInstallMetadata (Read-BapInstallFile (Join-Path $game 'BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll'))
    $current=Get-BapInstallVersion $installed.Version
    $floor=$current
    if($state.HighestVersion){
        $highest=Get-BapInstallVersion $state.HighestVersion;if((Compare-BapInstallVersion $highest $floor) -gt 0){$floor=$highest}
    }
    $same=@($Bundle.Expected.Keys|Where-Object {$observed[$_] -cne $Bundle.Expected[$_]}).Count -eq 0
    $valid=@($seen.Keys|Where-Object {$observed[$_] -cne $seen[$_]}).Count -eq 0
    if($Bundle.Recovery){
        $plan=$Bundle.Recovery
        if($plan.fromVersion -cne $current.Text -or $plan.receiptSha256 -cne $observed['BetterAstralParty.install.json'] -or !$plan.observedFiles -or @($plan.observedFiles.PSObject.Properties).Count -ne $observed.Count){throw '복구 계획의 현재 설치 기준이 다릅니다. / Recovery plan does not match the current installation.'}
        foreach($p in $observed.Keys){if(!($plan.observedFiles.PSObject.Properties.Name -ccontains $p) -or $plan.observedFiles.$p -cne $observed[$p]){throw '복구 계획 작성 후 설치 파일이 변경되었습니다. / Installation changed since recovery planning.'}}
        if($ConfirmRecovery -and (Read-Host '명시한 복구 묶음으로 교체합니다. 현재 파일은 백업하며 업데이트 버전 기록은 유지합니다. 계속하려면 y / Apply the reviewed recovery plan? [y/N]') -cne 'y'){throw '복구를 취소했습니다. 기존 설치는 보존됩니다. / Recovery cancelled.'}
        $Bundle.Mode='ReviewedRecovery'
    } else {
        if($observed['BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll'] -ceq '2C6524A88482BFB6E394FBE0A9F6A0E3EE3E42FE15ED62577E33308184BDAD24' -or $observed['BetterAstralParty-Launcher.exe'] -ceq '25CAF5F38626545670BD34A5FB39C8193827499D204276003158630A24039C69'){throw '현재 파일에 과거 비호환 산출물이 있습니다. receipt를 정상 기준으로 채택하지 않습니다. 검토된 복구가 필요합니다. / Obsolete current image requires reviewed recovery.'}
        if(!$valid){throw '기존 설치 파일과 receipt가 다릅니다. 덮어쓰지 않고 중단합니다. / Existing installation differs from its receipt.'}
        if((Compare-BapInstallVersion $Bundle.Version $floor) -lt 0){throw ('현재 또는 적용된 업데이트보다 오래된 설치 묶음입니다 ('+$Bundle.Version.Text+' < '+$floor.Text+'). 최신 전체 묶음을 사용하세요. / Downgrade blocked.')}
        if((Compare-BapInstallVersion $Bundle.Version $current) -eq 0 -and !$same){throw '같은 버전이지만 다른 빌드입니다. 기존 설치를 보존합니다. 검토된 복구 계획이 필요합니다. / Different build at the same version requires reviewed recovery.'}
        $Bundle.Mode=if($same){'SameBuild'}else{'Upgrade'}
    }
    $Bundle.Observed=$observed
}
function Assert-BapInstallUnchanged($Bundle) {
    Assert-BapSharedLoaderUnchanged $Bundle
    $null=Assert-BapInstallRoot $Bundle
    foreach($p in $Bundle.Observed.Keys){
        $path=Join-Path $Bundle.GameRoot $p
        $hash=if(Test-Path -LiteralPath $path -PathType Leaf){Get-BapInstallHash (Read-BapInstallFile $path)}else{$null}
        if($hash -cne $Bundle.Observed[$p]){throw '설치 검사 후 대상 파일이 바뀌었습니다. 아무것도 교체하지 않고 중단합니다. / Installation changed after preflight.'}
    }
}

function New-BapSharedLoaderPlan($Bundle, [string]$LoaderRoot) {
    $files = @()
    $observed = @{}
    foreach ($file in Get-ChildItem -LiteralPath $LoaderRoot -Recurse -Force -File) {
        $sourceKey = $file.FullName.Substring($Bundle.PackageRoot.Length + 1).Replace('\', '/')
        if (!$Bundle.SourceHashes.ContainsKey($sourceKey)) { throw 'Unlisted shared loader component rejected.' }
        $expected = $Bundle.SourceHashes[$sourceKey]
        if ((Get-BapInstallHash (Read-BapInstallFile $file.FullName)) -cne $expected) { throw 'Shared loader source changed.' }
        $relative = $file.FullName.Substring($LoaderRoot.Length + 1).Replace('\', '/')
        $target = Join-Path $Bundle.GameRoot $relative
        Assert-InstallTarget $Bundle.GameRoot $target
        if (Test-Path -LiteralPath $target) {
            if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw 'Shared loader destination is not a regular file.' }
            $hash = Get-BapInstallHash (Read-BapInstallFile $target)
            if ($hash -cne $expected) { throw ('Existing shared loader component differs; preserved for review: ' + $relative) }
            $observed[$relative] = $hash
        } else {
            $observed[$relative] = $null
            $files += @{ Source=$file.FullName; Target=$target; SharedLoader=$true }
        }
    }
    # Publish only after the complete plan passes. Differing unowned bytes grant no ownership.
    $Bundle.SharedLoaderObserved = $observed
    return $files
}
function Assert-BapSharedLoaderUnchanged($Bundle) {
    if (!$Bundle.SharedLoaderObserved) { return }
    foreach ($relative in $Bundle.SharedLoaderObserved.Keys) {
        $target = Join-Path $Bundle.GameRoot $relative
        Assert-InstallTarget $Bundle.GameRoot $target
        $hash = if (Test-Path -LiteralPath $target -PathType Leaf) { Get-BapInstallHash (Read-BapInstallFile $target) } else { $null }
        if ($hash -cne $Bundle.SharedLoaderObserved[$relative]) { throw 'Shared loader destination changed after preflight; preserved.' }
    }
}

function Copy-BapSharedLoaderFile([string]$GameRoot, $File) {
    Assert-InstallTarget $GameRoot $File.Target
    $source=[IO.File]::Open($File.Source,'Open','Read','Read')
    try {
        # CreateNew never adopts or overwrites an object that appeared after preflight.
        $target=[IO.File]::Open($File.Target,'CreateNew','Write','None')
        try { $source.CopyTo($target); $target.Flush($true) }
        finally { $target.Dispose() }
    } finally { $source.Dispose() }
}
