param([Parameter(Mandatory)][string]$OutputDirectory,[string]$DotnetRoot)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dotnetRootPath=if($DotnetRoot){[IO.Path]::GetFullPath($DotnetRoot)}else{Join-Path $root '.dotnet'}
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $destination){throw 'Fresh helper output directory required.'}
$null=[IO.Directory]::CreateDirectory($destination)
$compiler=Join-Path $dotnetRootPath 'sdk/8.0.425/Roslyn/bincore/csc.dll'
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compilerArguments=@('/nologo','/noconfig','/nostdlib+','/langversion:latest','/nullable:enable','/warnaserror+','/deterministic+',('/pathmap:'+$root+'=/src/BetterAstralParty'),'/define:NETFRAMEWORK','/target:exe','/platform:x64','/main:BetterAstralParty.Updating.UpdateHelperProgram',('/out:'+(Join-Path $destination 'BetterAstralParty-UpdateHelper.exe')))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','System.Security.dll','System.IO.Compression.dll','System.Net.Http.dll')){$compilerArguments+='/reference:'+(Join-Path $framework $name)}
$compilerArguments+=@(Get-ChildItem -LiteralPath (Join-Path $root 'updater') -File -Filter '*.cs'|Sort-Object Name|ForEach-Object FullName)
$compilerArguments+=@(Get-ChildItem -LiteralPath (Join-Path $root 'src/Observability') -File -Filter '*.cs'|Sort-Object Name|ForEach-Object FullName)
$compilerArguments+=Join-Path $root 'diagnostics/MinimalDiagnosticProjection.cs'
& (Join-Path $dotnetRootPath 'dotnet.exe') $compiler @compilerArguments
if($LASTEXITCODE -ne 0){throw 'Helper build failed.'}

