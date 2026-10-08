# Per-target snapshots support both failed and interrupted installations.
function Get-InstallJournalPath([string]$GameRoot) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($GameRoot).TrimEnd('\').ToLowerInvariant()))).Replace('-', '') }
    finally { $sha.Dispose() }
    return Join-Path $env:LOCALAPPDATA "BetterAstralParty/Backups/$key/pending.json"
}

function Assert-InstallTarget([string]$GameRoot, [string]$Target) {
    $base = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    $full = [IO.Path]::GetFullPath($Target)
    if (!$full.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Target outside game directory: $Target" }
    $parent = $full
    while ($parent.Length -ge $base.Length) {
        if (Test-Path -LiteralPath $parent) {
            if ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked install target is unsupported: $parent" }
        }
        $parent = Split-Path -Parent $parent
    }
    if (Test-Path -LiteralPath $full -PathType Leaf) {
        if (-not ('BapInstaller.NativeFile' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace BapInstaller {
    public static class NativeFile {
        [StructLayout(LayoutKind.Sequential)]
        public struct Info {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh,
                WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", SetLastError=true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
        public static uint LinkCount(SafeFileHandle handle) {
            Info info;
            if (!GetFileInformationByHandle(handle, out info))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return info.Links;
        }
    }
}
'@
        }
        $handle = [IO.File]::Open($full, 'Open', 'Read', 'Read')
        try {
            if ([BapInstaller.NativeFile]::LinkCount($handle.SafeFileHandle) -ne 1) { throw '하드링크된 설치 대상은 변경하지 않습니다. / Hard-linked installation file is unsupported.' }
        } finally { $handle.Dispose() }
    }
}

function Test-InstallWritable($Targets) {
    $directories = @{}
    foreach ($target in ($Targets | Sort-Object -Unique)) {
        if (Test-Path -LiteralPath $target) {
            if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw "File target is a directory: $target" }
            $handle = [IO.File]::Open($target, 'Open', 'ReadWrite', 'None')
            $handle.Dispose()
        }
        $dir = Split-Path -Parent $target
        while (!(Test-Path -LiteralPath $dir)) { $dir = Split-Path -Parent $dir }
        $directories[$dir] = $true
    }
    foreach ($dir in $directories.Keys) {
        $probe = Join-Path $dir ([guid]::NewGuid().ToString('N') + '.bap-write-test')
        $stream = [IO.File]::Open($probe, 'CreateNew', 'Write', 'None')
        try { $stream.WriteByte(0) } finally { $stream.Dispose(); [IO.File]::Delete($probe) }
    }
}

function Start-InstallTransaction([string]$GameRoot, $Targets, [long]$InstallBytes) {
    $journal = Get-InstallJournalPath $GameRoot
    if (Test-Path -LiteralPath $journal) { throw "Unfinished installation needs recovery: $journal" }
    $targets = @($Targets | Sort-Object -Unique)
    foreach ($target in $targets) { Assert-InstallTarget $GameRoot $target }
    Test-InstallWritable $targets
    $backupDir = Join-Path (Split-Path -Parent $journal) ([guid]::NewGuid().ToString('N'))
    $backupBytes = [long]0
    foreach ($target in $targets) { if (Test-Path -LiteralPath $target) { $backupBytes += (Get-Item -LiteralPath $target).Length } }
    # Include both copies on each volume: conservative when backup/game share a drive.
    foreach ($path in @($GameRoot, $backupDir)) {
        $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($path))
        if ($drive.AvailableFreeSpace -lt ($InstallBytes + $backupBytes + 16MB)) { throw 'Not enough free space for installation and rollback backup.' }
    }
    $null = New-Item -ItemType Directory -Force -Path $backupDir
    $entries = @()
    foreach ($target in $targets) {
        $exists = Test-Path -LiteralPath $target -PathType Leaf
        $backup = Join-Path $backupDir ($entries.Count.ToString() + '.bin')
        $hash = $null
        if ($exists) {
            [IO.File]::Copy($target, $backup)
            $hash = (Get-FileHash -LiteralPath $backup).Hash
            if ($hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "File changed during backup: $target" }
        }
        $entries += @{ Target=$target; Existed=$exists; Backup=$backup; Hash=$hash }
    }
    $state = @{ GameRoot=$GameRoot; Entries=$entries; BackupDir=$backupDir }
    [IO.File]::WriteAllText((Join-Path $backupDir 'pending.json'), ($state | ConvertTo-Json -Depth 5))
    [IO.File]::Move((Join-Path $backupDir 'pending.json'), $journal)
    return $journal
}

function Read-InstallRecoveryJournal([string]$GameRoot, [string[]]$AllowedTargets, [string]$ExpectedJournalHash, [string]$CompletedBackupId) {
    $journal = Get-InstallJournalPath $GameRoot
    $journalRoot = Split-Path -Parent $journal
    if ($CompletedBackupId) {
        if ($CompletedBackupId -cnotmatch '\A[0-9a-f]{32}\z') { throw 'Invalid completed backup identifier.' }
        $journal = Join-Path (Join-Path $journalRoot $CompletedBackupId) 'completed.json'
    }
    Assert-InstallTarget $journalRoot $journal
    $file=[IO.File]::Open($journal,'Open','Read','Read')
    try {
        if($file.Length -lt 2 -or $file.Length -gt 1MB){throw 'Recovery journal size invalid.'}
        $hash=(Get-FileHash -InputStream $file -Algorithm SHA256).Hash
        if($ExpectedJournalHash -and $hash -cne $ExpectedJournalHash){throw 'Recovery journal changed after confirmation; preserved.'}
        $file.Position=0;$reader=[IO.StreamReader]::new($file,[Text.UTF8Encoding]::new($false,$true),$true,4096,$true)
        try {
            $text=$reader.ReadToEnd()
            $state=if($CompletedBackupId){Read-BapInstallJson ([Text.Encoding]::UTF8.GetBytes($text))}else{$text|ConvertFrom-Json}
        } finally {$reader.Dispose()}
    } finally {$file.Dispose()}
    $canonical=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    if($state.GameRoot -isnot [string] -or $state.GameRoot -cne $canonical -or
       @($state.PSObject.Properties).Count -ne 3 -or $state.BackupDir -isnot [string] -or
       $state.Entries -isnot [array] -or $state.Entries.Count -lt 1 -or $state.Entries.Count -gt 300){throw 'Invalid recovery journal.'}
    $backupDir=[IO.Path]::GetFullPath($state.BackupDir)
    if($backupDir -cne $state.BackupDir -or ($CompletedBackupId -and (Split-Path -Leaf $backupDir) -cne $CompletedBackupId) -or (Split-Path -Parent $backupDir) -cne $journalRoot -or
       (Split-Path -Leaf $backupDir) -cnotmatch '\A[0-9a-f]{32}\z'){throw 'Recovery backup scope invalid.'}
    Assert-InstallTarget $journalRoot $backupDir
    if(!(Test-Path -LiteralPath $backupDir -PathType Container)){throw 'Recovery backup directory missing.'}
    $allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($target in $AllowedTargets){$null=$allowed.Add([IO.Path]::GetFullPath($target))}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $index=0
    foreach($entry in $state.Entries){
        if(@($entry.PSObject.Properties).Count -ne 4 -or $entry.Target -isnot [string] -or
           $entry.Backup -isnot [string] -or $entry.Existed -isnot [bool] -or
           [IO.Path]::GetFullPath($entry.Target) -cne $entry.Target -or !$seen.Add($entry.Target) -or
           ($AllowedTargets -and !$allowed.Contains($entry.Target)) -or
           $entry.Backup -cne (Join-Path $backupDir ($index.ToString()+'.bin'))){throw 'Recovery entry scope invalid.'}
        Assert-InstallTarget $GameRoot $entry.Target
        Assert-InstallTarget $journalRoot $entry.Backup
        if($entry.Existed){
            if($entry.Hash -isnot [string] -or $entry.Hash -cnotmatch '\A[A-F0-9]{64}\z' -or
               !(Test-Path -LiteralPath $entry.Backup -PathType Leaf) -or
               (Get-FileHash -LiteralPath $entry.Backup -Algorithm SHA256).Hash -cne $entry.Hash){throw 'Recovery backup missing or damaged.'}
        } elseif($null -ne $entry.Hash -or (Test-Path -LiteralPath $entry.Backup)){throw 'Unexpected recovery backup retained.'}
        $index++
    }
    return @{State=$state;JournalSha256=$hash}
}

function Get-BapInstallRecoveryTargets($Bundle) {
    $relative=@($Bundle.Expected.Keys)+@('BepInEx/config/BepInEx.cfg','doorstop_config.ini','BetterAstralParty.install.json')
    foreach($source in $Bundle.SourceHashes.Keys){
        if($source.StartsWith('.deps/bepinex/',[StringComparison]::Ordinal)){
            $relative+=$source.Substring('.deps/bepinex/'.Length)
        }
    }
    return @($relative|ForEach-Object {Join-Path $Bundle.GameRoot $_}|Sort-Object -Unique)
}

function Invoke-BapOrdinaryRecovery([string]$GameRoot, [string[]]$AllowedTargets, [string]$ExpectedJournalHash, [hashtable]$ExpectedCurrent, [bool]$CompleteOnly, $VerifiedBundle) {
    if (!$VerifiedBundle -or $VerifiedBundle.GameRoot -cne $GameRoot -or !$AllowedTargets -or !$AllowedTargets.Count) {
        throw '승인된 외부 검사기와 명시적인 복구 대상이 필요합니다. 기록을 보존합니다. / Approved incoming inspector and explicit transaction targets required; records preserved.'
    }
    # The native API revalidates the confirmation digest, then retains journal,
    # backup, target and parent handles until all mutations and retirement finish.
    $validated=Read-InstallRecoveryJournal $GameRoot $AllowedTargets $ExpectedJournalHash
    $assembly=Get-BapInstallVerifier $VerifiedBundle
    $api=$assembly.GetType('SteamLauncher').GetMethod('RestoreOrdinaryInstall',[Reflection.BindingFlags]'Public,Static')
    if (!$api) { throw '안전한 핸들 기반 복구 API가 없습니다. journal과 백업을 보존하세요. / Held-handle recovery API missing; retain journal and backups.' }
    $json=if($null -eq $ExpectedCurrent){''}else{$ExpectedCurrent | ConvertTo-Json -Depth 4 -Compress}
    [object[]]$arguments=@([string]$GameRoot,[string](Split-Path -Parent (Get-InstallJournalPath $GameRoot)),[string]$validated.JournalSha256,[string[]]$AllowedTargets,[string]$json,[bool]$CompleteOnly,[string]$VerifiedBundle.RootIdentity)
    try {$null=$api.Invoke($null,$arguments)}
    catch {throw ('복구가 중단됐습니다. journal과 백업을 보존하세요. / Recovery blocked; retain journal and backups. '+$_.Exception.GetBaseException().Message)}
}
function Restore-InstallTransaction([string]$GameRoot, [hashtable]$ExpectedCurrent, [string[]]$AllowedTargets, [string]$ExpectedJournalHash, $VerifiedBundle) {
    Invoke-BapOrdinaryRecovery $GameRoot $AllowedTargets $ExpectedJournalHash $ExpectedCurrent $false $VerifiedBundle
    Write-Host '검증한 핸들로 복원했습니다. 백업과 새 빈 폴더가 남을 수 있습니다. / Verified objects restored through retained handles. Backups retained; new empty directories may remain.'
}
function Complete-InstallTransaction([string]$GameRoot, [string[]]$AllowedTargets, $VerifiedBundle) {
    Invoke-BapOrdinaryRecovery $GameRoot $AllowedTargets '' $null $true $VerifiedBundle
}


function Enter-BapUpdateMaintenance([string]$GameRoot, [switch]$ReadOnly, $VerifiedBundle) {
    # Never execute the installed launcher. A receipt/observed hash alone is not code provenance.
    if (!$VerifiedBundle -or $VerifiedBundle.GameRoot -cne $GameRoot) { throw '검토된 전체 묶음의 안전 검사기가 필요합니다. / Approved complete external maintenance inspector required.' }
    $assembly = Get-BapInstallVerifier $VerifiedBundle
    $methodName = if ($ReadOnly) { 'CheckUpdateState' } else { 'GuardMaintenance' }
    $method = $assembly.GetType('SteamLauncher').GetMethod($methodName, [Reflection.BindingFlags]'Public,Static')
    if (!$method) { throw '외부 검사기의 안전 API를 사용할 수 없습니다. / Approved external maintenance API unavailable.' }
    return $method.Invoke($null, @([string]$GameRoot))
}
