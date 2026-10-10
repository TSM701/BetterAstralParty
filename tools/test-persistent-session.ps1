param([Parameter(Mandatory)][string]$OutputDirectory,[switch]$Native)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(!$output.StartsWith((Join-Path $root 'artifacts')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'Fresh project artifacts output directory required.'}
$null=[IO.Directory]::CreateDirectory($output)
$sdk=Join-Path $root '.dotnet';$dotnet=Join-Path $sdk 'dotnet.exe';$compiler=Join-Path $sdk 'sdk/8.0.425/Roslyn/bincore/csc.dll'
$savedCli=$env:DOTNET_CLI_HOME;$savedTelemetry=$env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_CLI_HOME=Join-Path $output 'cli-home';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $names=@('ReleaseUpdates','ReleaseHttpClient','ReleaseFeedTransport','PrivateReleaseSession','WindowsReleaseCredentialInput','WindowsReleaseCredentialStore','UpdateDownload','AutomaticUpdates','AutomaticUpdateSnapshot','UpdateHandoff','UpdateSafety','UpdatePreferencesBridge')
    $sources=@((Join-Path $root 'rc2-tests/GlobalUsings.cs'),(Join-Path $root 'rc2-tests/PersistentSessionFixture.cs'))
    $sources+=Join-Path $root 'diagnostics/MinimalDiagnosticProjection.cs'
    $sources+=@($names|ForEach-Object{Join-Path $root ('src/'+$_+'.cs')})
    $sources+=@(Get-ChildItem -LiteralPath (Join-Path $root 'updater') -File -Filter '*.cs'|Where-Object Name -ne 'UpdateHelperProgram.cs'|Sort-Object Name|ForEach-Object FullName)
    $sources+=@(Get-ChildItem -LiteralPath (Join-Path $root 'src/Observability') -File -Filter '*.cs'|Sort-Object Name|ForEach-Object FullName)
    $before=@($sources|ForEach-Object{[pscustomobject]@{path=$_.Substring($root.Length+1);sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
    foreach($target in 'net6.0','net8.0') {
        $version=if($target -eq 'net6.0'){'6.0.36'}else{'8.0.31'}
        $destination=Join-Path $output $target;$null=[IO.Directory]::CreateDirectory($destination)
        $defines=if($target -eq 'net6.0'){'NETCOREAPP,NET6_0,NET5_0_OR_GREATER,NET6_0_OR_GREATER'}else{'NETCOREAPP,NET8_0,NET5_0_OR_GREATER,NET6_0_OR_GREATER,NET7_0_OR_GREATER,NET8_0_OR_GREATER'}
        $arguments=@('/nologo','/noconfig','/nostdlib+','/langversion:latest','/nullable:enable','/warnaserror+',('/define:'+$defines),'/target:exe','/main:PersistentSessionFixture',('/out:'+(Join-Path $destination 'PersistentSessionFixture.dll')))
        $arguments+=@(Get-ChildItem -LiteralPath (Join-Path $sdk ('packs/Microsoft.NETCore.App.Ref/'+$version+'/ref/'+$target)) -File -Filter '*.dll'|ForEach-Object{'/reference:'+$_.FullName})
        foreach($name in 'SemanticVersioning.dll','BepInEx.Core.dll'){$arguments+='/reference:'+(Join-Path $root ('.deps/bepinex/BepInEx/core/'+$name))}
        & $dotnet $compiler @arguments @sources
        if($LASTEXITCODE -ne 0){throw ($target+' persistent-session compile failed')}
        foreach($name in 'SemanticVersioning.dll','BepInEx.Core.dll'){Copy-Item -LiteralPath (Join-Path $root ('.deps/bepinex/BepInEx/core/'+$name)) -Destination (Join-Path $destination $name)}
        $config='{"runtimeOptions":{"tfm":"'+$target+'","framework":{"name":"Microsoft.NETCore.App","version":"'+$version+'"}}}'
        $config|Out-File -LiteralPath (Join-Path $destination 'PersistentSessionFixture.runtimeconfig.json') -Encoding ascii
        $run=@((Join-Path $destination 'PersistentSessionFixture.dll'));if($Native){$run+='--native'}
        & $dotnet @run
        if($LASTEXITCODE -ne 0){throw ($target+' persistent-session fixture failed')}
    }
    foreach($item in $before){if((Get-FileHash -LiteralPath (Join-Path $root $item.path) -Algorithm SHA256).Hash -cne $item.sha256){throw ('Test source changed during compile/run: '+$item.path)}}
    [pscustomobject]@{schema=1;nativeDummyCredential=[bool]$Native;source=$before}|ConvertTo-Json -Depth 5|Out-File -LiteralPath (Join-Path $output 'tested-source.json') -Encoding ascii
    Write-Host 'PASS: current-source persistent session compiled and checked on NET6 and NET8; exact input hashes recorded.'
} finally {$env:DOTNET_CLI_HOME=$savedCli;$env:DOTNET_CLI_TELEMETRY_OPTOUT=$savedTelemetry}
