param([string] $OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/card-scan-performance'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $artifactRoot ([Guid]::NewGuid().ToString('N')) }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (!$outputPath.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test output must stay under artifacts/card-scan-performance' }
if (Test-Path -LiteralPath $outputPath) { throw 'Test output must be fresh; previous evidence is not overwritten' }
$inputs = @('src/CardUi.cs', 'src/CardAdvisor.cs', 'src/CombatAdvisor.cs', 'src/VisibleCombat.cs',
    'focus-tests/card-scan-performance/RuntimeFixture.cs', 'focus-tests/card-scan-performance/CardScanFixture.csproj', 'tools/test-card-scan-performance.ps1')
$hashes = @($inputs | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $projectRoot $_) -Algorithm SHA256).Hash } })
$source = Get-Content -LiteralPath (Join-Path $projectRoot 'src/CardUi.cs') -Raw
$source = $source.Replace("`r`n", "`n")
$replacements = @(
    @('internal static class CardUi', 'internal static class ReferenceCardUi'),
    @(@'
            var values = length is >= 2 and <= 3 ? new int[length] : Array.Empty<int>();
            for (var index = 0; index < values.Length; index++)
                values[index] = parameters.Call("get_Item", index)!.Value<int>();
'@, @'
            var values = length is >= 2 and <= 3 ? Enumerable.Range(0, length)
                .Select(index => parameters.Call("get_Item", index)!.Value<int>()).ToArray() : Array.Empty<int>();
'@),
    @(@'
        var hovered = default((RuntimeObject Card, RuntimeObject Face, CardBonus? Bonus, bool Usable));
        foreach (var candidate in candidates)
            if (candidate.Card.Pointer == hit?.Pointer) { hovered = candidate; break; }
'@, @'
        var hovered = candidates.FirstOrDefault(item => item.Card.Pointer == hit?.Pointer);
'@),
    @(@'
            var cheaper = false;
            if (item.Bonus is { } current)
                foreach (var other in candidates)
                    if (other.Usable && other.Bonus is { } otherBonus && CardAdvisor.Dominates(otherBonus, current))
                    { cheaper = true; break; }
'@, @'
            var cheaper = item.Bonus is { } current && candidates.Any(other => other.Usable
                && other.Bonus is { } otherBonus && CardAdvisor.Dominates(otherBonus, current));
'@)
)
foreach ($pair in $replacements) {
    $from = $pair[0].Replace("`r`n", "`n"); $to = $pair[1].Replace("`r`n", "`n")
    if ([regex]::Matches($source, [regex]::Escape($from)).Count -ne 1) { throw 'Production scan changed; update the exact three-place LINQ comparison reference' }
    $source = $source.Replace($from, $to)
}
$dotnet = Join-Path $projectRoot '.dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { throw 'Use the existing project SDK; no SDK installation is performed' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$reference = Join-Path $outputPath 'ReferenceCardUi.cs'
[IO.File]::WriteAllText($reference, $source, [Text.UTF8Encoding]::new($false))
$project = Join-Path $projectRoot 'focus-tests/card-scan-performance/CardScanFixture.csproj'
$savedTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $build = @(& $dotnet build $project -c Release --property:ReferencePath="$reference" --property:BaseIntermediateOutputPath="$outputPath/obj/" --property:OutputPath="$outputPath/bin/" 2>&1)
    $buildCode = $LASTEXITCODE
    $build | Set-Content -LiteralPath (Join-Path $outputPath 'build.txt') -Encoding UTF8
    if ($buildCode -ne 0) { $build | Write-Host; throw "Card scan fixture compilation failed: $buildCode" }
    $run = @(& $dotnet (Join-Path $outputPath 'bin/CardScanFixture.dll') 2>&1)
    $code = $LASTEXITCODE
    $run | Set-Content -LiteralPath (Join-Path $outputPath 'run.txt') -Encoding UTF8
    if ($code -ne 0) { $run | Write-Host; throw "Card scan fixture failed: $code" }
} finally { $env:DOTNET_CLI_TELEMETRY_OPTOUT = $savedTelemetry }
$json = @($run | Where-Object { $_ -is [string] -and $_.StartsWith('{"Status":') })
if ($json.Count -ne 1) { $run | Write-Host; throw 'Focused fixture result missing' }
$result = $json[0] | ConvertFrom-Json
foreach ($inputHash in $hashes) {
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $inputHash.Path) -Algorithm SHA256).Hash -ne $inputHash.Sha256) { throw "Test input changed: $($inputHash.Path)" }
}
[ordered]@{ Result = $result; Inputs = $hashes; ReferenceSha256 = (Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding UTF8
$json[0] | Write-Host
Write-Host "Evidence: $outputPath"
