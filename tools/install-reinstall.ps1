# Shared clean-install generation. The immutable approved package is never patched.
function Test-BapRemovedHistory($Channel,$Water,$State,[string]$PluginVersion,$Repositories,$Historical) {
    # Pure predicate. Historical data must come only from Get-BapRetainedHistoryEvidence.
    if ($Channel.Count -ne 7 -or $Channel[0] -cne 'BetterAstralParty.ChannelState/v1' -or $Channel[6] -cne '' -or
        $Channel[1] -cne ('root='+$State.RootIdentity) -or $Channel[2] -cnotmatch '\Aactive=(Stable|Beta)\z' -or
        $Channel[3] -cnotmatch '\Aoperation=[0-9a-f]{32}\z' -or $State.RootIdentity -cnotmatch '\A[A-F0-9]{8}:[A-F0-9]{16}\z') { throw 'Removal channel/root mismatch.' }
    $active=$Channel[2].Substring(7)
    if ($Water.Count -ne 5 -or $Water[0] -cne 'BetterAstralParty.HighWater/v1' -or $Water[1] -cne $Channel[1] -or
        $Water[2] -cne ('version='+$State.HighestVersion) -or $Water[3] -cnotmatch '\Adescriptor=[A-F0-9]{64}\z' -or $Water[4] -cne '') { throw 'Invalid retained update history.' }
    $highest=Get-BapInstallVersion $State.HighestVersion
    $rows=@{}; $matched=$false
    foreach ($name in @('Stable','Beta')) {
        $index=if($name -ceq 'Stable'){4}else{5}
        if (!$Channel[$index].StartsWith($name+'=',[StringComparison]::Ordinal)) { throw 'Removal channel history missing.' }
        $values=$Channel[$index].Substring($name.Length+1).Split([char]9)
        if ($values.Count -ne 3) { throw 'Invalid removal channel history.' }
        if ($values[1] -ceq '-') {
            if ($values[0] -cne '0' -or $values[2] -cne '-') { throw 'Invalid empty removal channel history.' }
            continue
        }
        if (!$Repositories.ContainsKey($name) -or $Repositories[$name] -le 0 -or $values[0] -cne ([long]$Repositories[$name]).ToString([Globalization.CultureInfo]::InvariantCulture) -or $values[2] -cnotmatch '\A[A-F0-9]{64}\z') { throw 'Removal channel repository identity mismatch.' }
        $rows[$name]=@{Version=(Get-BapInstallVersion $values[1]);Descriptor=$values[2];RepositoryId=[long]$Repositories[$name]}
        if ($values[1] -ceq $State.HighestVersion -and $Water[3] -ceq ('descriptor='+$values[2])) { $matched=$true }
    }
    if (!$rows.ContainsKey($active) -or $rows[$active].Version.Text -cne $PluginVersion) { throw 'Removal backup does not match the active installation version.' }
    if ($matched) { return $true }
    if (!$Historical) { return $false }
    if ($Historical.Count -ne 4 -or !$rows.ContainsKey($Historical.Channel) -or
        $Historical.Version -cne $State.HighestVersion -or $Water[3] -cne ('descriptor='+$Historical.DescriptorSha256) -or
        $Historical.RepositoryId -ne $rows[$Historical.Channel].RepositoryId) { throw 'Historical signed update does not match retained history.' }
    # Whole-package upgrades preserve the legacy global floor, but advance their channel row.
    # Never accept a different descriptor at the same version, or a watermark above its channel.
    return (Compare-BapInstallVersion $rows[$Historical.Channel].Version $highest) -gt 0
}

function Get-BapRetainedHistoryEvidence($Bundle,$State,[string]$DescriptorHash) {
    $assembly=Get-BapInstallVerifier $Bundle
    $flags=[Reflection.BindingFlags]'Static,Public,NonPublic'
    $scan=$assembly.GetType('BetterAstralParty.Updating.UpdateRecovery',$true).GetMethod('Scan',$flags)
    $verify=$assembly.GetType('BetterAstralParty.Updating.WholeInstallBridge',$true).GetMethod('VerifyUpdateProof',$flags)
    if (!$scan -or !$verify) { throw 'Signed historical update validation API required.' }
    $api=Get-BapReinstallFenceApi $Bundle; $fence=$null
    $leases=[Collections.Generic.List[IDisposable]]::new()
    try {
        $fence=$api.Constructor.Invoke([object[]]@([string]$Bundle.GameRoot,$null))
        if ($api.RootText.GetValue($api.Root.GetValue($fence),$null) -cne $State.RootIdentity) { throw 'Retained history root identity changed.' }
        function Read-HistoryProof([string]$Path,[long]$Limit,$Pins) {
            $file=$api.Open.Invoke($fence,[object[]]@($Path,$false,$false)); $leases.Add($file)
            $stream=$api.Stream.GetValue($file,$null)
            if ($stream.Length -lt 1 -or $stream.Length -gt $Limit) { throw 'Historical update proof exceeds its size bound.' }
            $bytes=[byte[]]::new([int]$stream.Length); $offset=0
            while ($offset -lt $bytes.Length) { $count=$stream.Read($bytes,$offset,$bytes.Length-$offset); if ($count -le 0) { throw 'Historical update proof truncated.' }; $offset+=$count }
            $Pins[$Path]=$api.Hash.Invoke($file,@())
            return ,$bytes
        }
        $water=$api.Open.Invoke($fence,[object[]]@('BetterAstralParty.update.version',$false,$false)); $leases.Add($water)
        if ($api.Hash.Invoke($water,@()) -cne $State.WaterHash) { throw 'Retained update history changed.' }
        $waterIdentity=$api.RootText.GetValue($api.Identity.GetValue($water),$null)
        $selected=$null
        foreach ($nativeEntry in $scan.Invoke($null,[object[]]@($fence))) {
            $entry=@{}
            foreach ($name in @('Pending','Committed','Descriptor','Work','Stage')) {
                $field=$nativeEntry.GetType().GetField($name,[Reflection.BindingFlags]'Instance,Public,NonPublic')
                if (!$field) { throw 'Historical transaction discovery contract mismatch.' }
                $entry[$name]=$field.GetValue($nativeEntry)
            }
            if ($entry.Pending -or !$entry.Committed -or $entry.Descriptor -cne $DescriptorHash) { continue }
            if ($entry.Work -cnotmatch '\Abap-txn-[0-9a-f]{32}\z' -or $entry.Stage -cnotmatch '\Abap-stage-[0-9a-f]{32}\z') { throw 'Unsafe historical update proof path.' }
            $pins=@{}; $plan=Read-HistoryProof ($entry.Work+'/plan') 16384 $pins
            $lines=[Text.Encoding]::ASCII.GetString($plan).Split([char]10)
            $row=@($lines | Where-Object { $_.StartsWith("file=BetterAstralParty.update.version`t",[StringComparison]::Ordinal) })
            if ($row.Count -ne 1) { throw 'Historical update must identify one retained history file.' }
            $slot=$row[0].Split([char]9)
            if ($slot.Count -ne 5) { throw 'Invalid historical update history slot.' }
            if ($slot[3] -cne $waterIdentity -or $slot[4] -cne $State.WaterHash) { continue }
            if ($selected) { throw 'Ambiguous historical update evidence.' }
            foreach ($marker in @('committed','commit-intent')) {
                $bytes=Read-HistoryProof ($entry.Work+'/'+$marker) 128 $pins
                if ([Text.Encoding]::ASCII.GetString($bytes) -cne ($pins[$entry.Work+'/plan']+"`n")) { throw 'Historical update commit proof changed.' }
            }
            $descriptor=Read-HistoryProof ($entry.Stage+'/descriptor.bin') 32768 $pins
            $signature=Read-HistoryProof ($entry.Stage+'/signature.bin') 1024 $pins
            $key=Read-HistoryProof ($entry.Stage+'/key-id.bin') 40 $pins
            if ($pins[$entry.Stage+'/descriptor.bin'] -cne $DescriptorHash -or @($key | Where-Object { $_ -gt 127 }).Count) { throw 'Historical update descriptor identity mismatch.' }
            $keyId=[Text.Encoding]::ASCII.GetString($key)
            if ($keyId -cnotmatch '\A[A-Za-z0-9_-]{1,40}\z') { throw 'Invalid historical signing key identity.' }
            $envelope=[Text.Encoding]::ASCII.GetBytes("BetterAstralParty.UpdateSignature/v1`nkey-id=$keyId`nsignature=$([Convert]::ToBase64String($signature))`n")
            $arguments=[object[]]::new(2); $arguments[0]=[byte[]]$descriptor; $arguments[1]=[byte[]]$envelope
            $verified=$verify.Invoke($null,$arguments)
            $historical=@{}
            foreach ($name in @('Version','Channel','DescriptorSha256')) {
                $field=$verified.GetType().GetField($name,[Reflection.BindingFlags]'Instance,Public,NonPublic')
                if (!$field) { throw 'Historical update validation contract mismatch.' }
                $historical[$name]=$field.GetValue($verified)
            }
            $repository=$verified.GetType().GetProperty('RepositoryId',[Reflection.BindingFlags]'Instance,Public,NonPublic')
            if (!$repository -or $repository.PropertyType -ne [long]) { throw 'Historical repository validation contract mismatch.' }
            $historical.RepositoryId=$repository.GetValue($verified,$null)
            $selected=@{Descriptor=$historical;Pins=$pins;WaterIdentity=$waterIdentity}
        }
        if (!$selected) { throw 'Retained update history requires its exact committed signed transaction.' }
        return $selected
    } finally {
        for ($i=$leases.Count-1;$i -ge 0;$i--) { $leases[$i].Dispose() }
        if ($fence) { $fence.Dispose() }
    }
}

function Assert-BapReinstallAbsent($Bundle) {
    foreach ($relative in @($Bundle.Expected.Keys)+@('BetterAstralParty.install.json','BetterAstralParty-UpdateHelper.exe','BetterAstralParty.helper.receipt')) {
        $path=Join-Path $Bundle.GameRoot $relative
        Assert-InstallTarget $Bundle.GameRoot $path
        if (Test-Path -LiteralPath $path) { throw '모드 파일이 남아 있어 재설치 이력을 초기화하지 않습니다. / Reinstall generation requires complete mod removal.' }
    }
    foreach ($relative in @('BetterAstralParty.reinstall.pending','BetterAstralParty.update.operation','BetterAstralParty.update.failed')) {
        if (Test-Path -LiteralPath (Join-Path $Bundle.GameRoot $relative)) { throw '미완료 작업 기록을 보존하고 중단합니다. / Pending operation retained; recovery review required.' }
    }
}

function Get-BapCleanReinstallPlan($Bundle) {
    $pending=Join-Path $Bundle.GameRoot 'BetterAstralParty.reinstall.pending'
    Assert-InstallTarget $Bundle.GameRoot $pending
    if (Test-Path -LiteralPath $pending) { throw '미완료 재설치 기록을 보존하고 중단합니다. / Pending reinstall retained; recovery review required.' }
    $state=Assert-BapInstallRoot $Bundle
    if (!$state.WaterHash -or (Test-Path -LiteralPath (Join-Path $Bundle.GameRoot 'BetterAstralParty.install.json'))) { return $null }
    Assert-BapReinstallAbsent $Bundle
    $null=Enter-BapUpdateMaintenance $Bundle.GameRoot -ReadOnly -VerifiedBundle $Bundle
    Assert-BapHelperBootstrap $Bundle
    $backupRoot=Split-Path -Parent (Get-InstallJournalPath $Bundle.GameRoot)
    $directories=@(Get-ChildItem -LiteralPath $backupRoot -Directory -Force)
    if ($directories.Count -gt 512) { throw 'Too many recovery backups; manual review required.' }
    $completed=@(foreach ($dir in $directories) {
        if ($dir.Name -cmatch '\A[0-9a-f]{32}\z') {
            $path=Join-Path $dir.FullName 'completed.json'
            Assert-InstallTarget $backupRoot $path
            if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Item -LiteralPath $path }
        }
    }) | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (!$completed) { throw '완료된 제거 백업을 찾지 못했습니다. / Complete removal backup required.' }
    $id=Split-Path -Leaf (Split-Path -Parent $completed.FullName)
    $exclusive=@('BepInEx/config/kr.betterastralparty.mod.cfg','BepInEx/BetterAstralParty-Diagnostics/current.log',
        'BepInEx/BetterAstralParty-Diagnostics/previous.log','BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log',
        'BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log','BetterAstralParty-Compatibility.log','BetterAstralParty-LaunchError.log')
    $relative=@($Bundle.Expected.Keys)+@('BetterAstralParty.install.json','BetterAstralParty-UpdateHelper.exe','BetterAstralParty.helper.receipt','doorstop_config.ini')+$exclusive
    $allowed=@($relative | ForEach-Object { Join-Path $Bundle.GameRoot $_ })
    $proof=Read-InstallRecoveryJournal $Bundle.GameRoot -AllowedTargets $allowed -CompletedBackupId $id
    $rows=@{}; $pins=@{$completed.FullName=$proof.JournalSha256}
    foreach ($entry in $proof.State.Entries) {
        if (!$entry.Existed) { throw 'Removal backup must contain the former files.' }
        $rows[$entry.Target.Substring($Bundle.GameRoot.Length+1).Replace('\','/')]=$entry
        $pins[$entry.Backup]=$entry.Hash
    }
    $required=@($Bundle.Expected.Keys)+@('BetterAstralParty.install.json','BetterAstralParty-UpdateHelper.exe','BetterAstralParty.helper.receipt')
    foreach ($path in $required) { if (!$rows.ContainsKey($path)) { throw ('Removal backup is incomplete: '+$path) } }
    $receiptBytes=Read-BapInstallFile $rows['BetterAstralParty.install.json'].Backup
    if ((Get-BapInstallHash $receiptBytes) -cne $rows['BetterAstralParty.install.json'].Hash) { throw 'Removal receipt changed during validation.' }
    $validator=(Get-BapInstallVerifier $Bundle).GetType('SteamLauncher').GetMethod('ValidateInstalledReceipt',[Reflection.BindingFlags]'Public,Static')
    if (!$validator) { throw 'Approved receipt validator required.' }
    $arguments=[object[]]::new(1); $arguments[0]=[byte[]]$receiptBytes; $null=$validator.Invoke($null,$arguments)
    $receipt=Read-BapInstallJson $receiptBytes
    foreach ($file in $receipt.files) { if ($rows[$file.path].Hash -cne $file.sha256) { throw 'Removal backup differs from the former receipt.' } }
    $pluginBytes=Read-BapInstallFile $rows['BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll'].Backup
    if ((Get-BapInstallHash $pluginBytes) -cne $rows['BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll'].Hash) { throw 'Removal plugin changed during validation.' }
    $plugin=Read-BapInstallMetadata $pluginBytes
    if ($receipt.channelState -isnot [string]) { throw 'Removal backup does not identify the installation generation.' }
    $channel=[Text.Encoding]::ASCII.GetString([Convert]::FromBase64String($receipt.channelState)).Split([char]10)
    $waterBytes=Read-BapInstallFile (Join-Path $Bundle.GameRoot 'BetterAstralParty.update.version')
    if ((Get-BapInstallHash $waterBytes) -cne $state.WaterHash) { throw 'Update history changed during validation.' }
    $water=[Text.Encoding]::ASCII.GetString($waterBytes).Split([char]10)
    $feed=(Get-BapInstallVerifier $Bundle).GetType('BetterAstralParty.Updating.ReleaseFeedPolicy',$true)
    $repositories=@{}
    foreach ($name in @('Stable','Beta')) {
        $repository=$feed.GetField(($name+'RepositoryId'),[Reflection.BindingFlags]'Static,Public,NonPublic')
        if (!$repository) { throw 'Removal channel repository identity API missing.' }
        $repositories[$name]=[long]$repository.GetRawConstantValue()
    }
    $gamePins=@{}; $waterIdentity=$null
    if (!(Test-BapRemovedHistory $channel $water $state $plugin.Version $repositories $null)) {
        $historical=Get-BapRetainedHistoryEvidence $Bundle $state $water[3].Substring(11)
        if (!(Test-BapRemovedHistory $channel $water $state $plugin.Version $repositories $historical.Descriptor)) { throw 'Removal backup does not match the retained update history.' }
        $gamePins=$historical.Pins
        $waterIdentity=$historical.WaterIdentity
    }
    $helperReceiptBytes=Read-BapInstallFile $rows['BetterAstralParty.helper.receipt'].Backup
    if ((Get-BapInstallHash $helperReceiptBytes) -cne $rows['BetterAstralParty.helper.receipt'].Hash) { throw 'Removal helper receipt changed during validation.' }
    $helperProof=''
    $validator=(Get-BapInstallVerifier $Bundle).GetType('SteamLauncher').GetMethod('ValidateRemovedHelperEvidence',[Reflection.BindingFlags]'Public,Static',$null,[type[]]@([string],[byte[]],[byte[]]),$null)
    if ($validator) {
        if ($validator.ReturnType -ne [string]) { throw 'Removed helper evidence API mismatch.' }
        $preflight=(Get-BapInstallVerifier $Bundle).GetType('SteamLauncher').GetMethod('ValidateWholeInputs',[Reflection.BindingFlags]'Public,Static',$null,[type[]]@([string],[string],[string]),$null)
        if (!$preflight -or $preflight.ReturnType -ne [void]) { throw 'Read-only signed whole-bundle preflight required.' }
        $null=$preflight.Invoke($null,[object[]]@([string]$Bundle.GameRoot,[string]$Bundle.PackageRoot,[string]$Bundle.Version.Text))
        $helperBytes=Read-BapInstallFile $rows['BetterAstralParty-UpdateHelper.exe'].Backup
        if ((Get-BapInstallHash $helperBytes) -cne $rows['BetterAstralParty-UpdateHelper.exe'].Hash) { throw 'Removal helper image changed during validation.' }
        $arguments=[object[]]::new(3);$arguments[0]=[string]$Bundle.GameRoot;$arguments[1]=[byte[]]$helperBytes;$arguments[2]=[byte[]]$helperReceiptBytes
        $helperProof=$validator.Invoke($null,$arguments)
        $evidence=$helperProof.Split([char]10)
        if ($evidence.Count -ne 6 -or $evidence[0] -cne 'BetterAstralParty.RemovedHelperEvidence/v1' -or $evidence[5] -cne '') { throw 'Invalid removed helper evidence response.' }
        $work=$null;$suffixes=@{}
        foreach ($line in $evidence[1..4]) {
            if ($line -cnotmatch '\A(bap-helper-[0-9a-f]{32})/(plan|descriptor|signature|committed)=([A-F0-9]{64})\z') { throw 'Unsafe removed helper evidence path.' }
            if (($work -and $work -cne $Matches[1]) -or $suffixes.ContainsKey($Matches[2])) { throw 'Ambiguous removed helper evidence.' }
            $work=$Matches[1];$suffixes[$Matches[2]]=$true;$gamePins[$work+'/'+$Matches[2]]=$Matches[3]
        }
    } else {
    # Compatibility only for the deliberately pinned development rc.3 dispatcher.
    if ($Bundle.Version.Text -cne '1.0.0-rc.3' -or $Bundle.ManifestSha256 -cne '80C0CE4BDF0A2C0F3A323B8394DCCD1DD93ECD66B18AEFE2EFD32C929FEFB684') { throw 'Version-neutral reinstall validation API required; use a current complete package.' }
    $helperHash=$Bundle.SourceHashes['helper-bootstrap/BetterAstralParty-UpdateHelper.exe']
    $helperDescriptor=$Bundle.SourceHashes['helper-bootstrap/helper.descriptor']
    $signatureBytes=Read-BapInstallFile (Join-Path $Bundle.PackageRoot 'helper-bootstrap/helper.signature')
    if ((Get-BapInstallHash $signatureBytes) -cne $Bundle.SourceHashes['helper-bootstrap/helper.signature']) { throw 'Pinned helper signature changed.' }
    $keys=[regex]::Matches([Text.Encoding]::ASCII.GetString($signatureBytes),'(?m)^key-id=([A-Za-z0-9._-]{1,128})\r?$')
    if ($keys.Count -ne 1) { throw 'Pinned helper signing identity missing.' }
    $helperReceipt=[Text.Encoding]::ASCII.GetString($helperReceiptBytes)
    $pattern='\ABetterAstralParty.HelperReceipt/v1\nroot='+[regex]::Escape($state.RootIdentity)+'\nimage-id=[A-F0-9]{8}:[A-F0-9]{16}\nversion='+[regex]::Escape($Bundle.Version.Text)+'\nsha256='+$helperHash+'\ndescriptor='+$helperDescriptor+'\nkey-id='+[regex]::Escape($keys[0].Groups[1].Value)+'\n\z'
    if ($rows['BetterAstralParty-UpdateHelper.exe'].Hash -cne $helperHash -or $helperReceipt -cnotmatch $pattern) { throw 'Removed helper does not match the approved signed bootstrap.' }
    }
    return @{RootIdentity=$state.RootIdentity;WaterHash=$state.WaterHash;WaterIdentity=$waterIdentity;Version=$state.HighestVersion;ProofHash=$proof.JournalSha256;Pins=$pins;GamePins=$gamePins;HelperProof=$helperProof;BackupRoot=$backupRoot;BackupId=$id}
}

function Get-BapReinstallFenceApi($Bundle) {
    # Reuse only the pinned inspector's held-handle, no-overwrite rename implementation.
    $assembly=Get-BapInstallVerifier $Bundle
    $fence=$assembly.GetType('BetterAstralParty.Updating.WindowsFileFence',$true)
    $file=$assembly.GetType('BetterAstralParty.Updating.WindowsFileFence+FileLease',$true)
    $flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
    $api=@{
        Constructor=$fence.GetConstructor($flags,$null,[type[]]@([string],[Action[string]]),$null)
        Open=$fence.GetMethod('OpenFile',$flags,$null,[type[]]@([string],[bool],[bool]),$null)
        Directory=$fence.GetMethod('DirectoryIdentity',$flags,$null,[type[]]@([string]),$null)
        Hash=$file.GetMethod('Hash',$flags,$null,[type[]]@(),$null)
        Rename=$file.GetMethod('RenameTo',$flags,$null,[type[]]@($fence,[string]),$null)
        Flush=$file.GetMethod('Flush',$flags,$null,[type[]]@(),$null)
        Stream=$file.GetProperty('Stream',$flags)
        Root=$fence.GetField('RootIdentity',$flags)
        Identity=$file.GetField('Identity',$flags)
    }
    if ($api.Root) { $api.RootText=$api.Root.FieldType.GetProperty('Text',$flags) }
    foreach ($method in $api.Keys) { if ($null -eq $api[$method]) { throw 'Pinned held-handle maintenance API missing.' } }
    if ($api.Identity.FieldType -ne $api.Root.FieldType -or $api.Open.ReturnType -ne $file -or $api.Directory.ReturnType -ne $api.Root.FieldType -or $api.Hash.ReturnType -ne [string] -or $api.Stream.PropertyType -ne [IO.Stream] -or $api.RootText.PropertyType -ne [string] -or $api.Rename.ReturnType -ne [void] -or $api.Flush.ReturnType -ne [void] -or ![IDisposable].IsAssignableFrom($fence) -or ![IDisposable].IsAssignableFrom($file)) { throw 'Pinned held-handle maintenance API mismatch.' }
    return $api
}

function Move-BapReinstallHistory($Bundle,$Plan,[IO.FileStream]$HeldBackupLock) {
    $maintenance=$null; $backupLock=$null; $gameFence=$null; $backupFence=$null
    $leases=[Collections.Generic.List[IDisposable]]::new()
    try {
        $maintenance=Enter-BapUpdateMaintenance $Bundle.GameRoot -VerifiedBundle $Bundle
        $api=Get-BapReinstallFenceApi $Bundle
        $gameFence=$api.Constructor.Invoke([object[]]@([string]$Bundle.GameRoot,$null))
        $backupFence=$api.Constructor.Invoke([object[]]@([string]$Plan.BackupRoot,$null))
        if ($api.RootText.GetValue($api.Root.GetValue($gameFence),$null) -cne $Plan.RootIdentity) { throw 'Installation root identity changed; nothing retired.' }
        # A completed removal already created this lock. Never follow/create a path-based lock here.
        if ($HeldBackupLock) {
            $lockPath=Join-Path $Plan.BackupRoot 'pending.json.lock'
            if (!$HeldBackupLock.CanWrite -or ![string]::Equals($HeldBackupLock.Name,$lockPath,[StringComparison]::OrdinalIgnoreCase)) { throw 'Existing installer backup lock mismatch.' }
            # The ordinary installer holds FileShare.None: inspect its handle, never reopen it.
            if ((Get-Item -LiteralPath $lockPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint -or [BapInstaller.NativeFile]::LinkCount($HeldBackupLock.SafeFileHandle) -ne 1) { throw 'Linked installer backup lock rejected.' }
        } else { $backupLock=$api.Open.Invoke($backupFence,[object[]]@('pending.json.lock',$true,$false)) }
        if (@(Get-BapGameTargets $Bundle.GameRoot).Count) { throw '게임을 종료한 뒤 다시 설치하세요. / Close the game before reinstalling.' }
        foreach ($process in Get-Process -Name BetterAstralParty-UpdateHelper -ErrorAction SilentlyContinue) {
            if (!$process.HasExited -and (!$process.Path -or (Split-Path -Parent $process.Path) -eq $Bundle.GameRoot)) { throw 'Update helper is running; history retained.' }
        }
        # Pin work directories first: revalidation must not reopen an exclusively leased file.
        foreach ($relative in $Plan.GamePins.Keys) {
            $null=$api.Directory.Invoke($gameFence,[object[]]@($relative.Split([char]'/')[0]))
        }
        $current=Get-BapCleanReinstallPlan $Bundle
        if (!$current -or $current.RootIdentity -cne $Plan.RootIdentity -or $current.WaterHash -cne $Plan.WaterHash -or $current.WaterIdentity -cne $Plan.WaterIdentity -or $current.ProofHash -cne $Plan.ProofHash -or $current.HelperProof -cne $Plan.HelperProof) { throw 'Reinstall plan changed; nothing retired.' }
        foreach ($path in $Plan.Pins.Keys) {
            $relative=$path.Substring($Plan.BackupRoot.Length+1).Replace('\','/')
            $file=$api.Open.Invoke($backupFence,[object[]]@($relative,$false,$false)); $leases.Add($file)
            if ($api.Hash.Invoke($file,@()) -cne $Plan.Pins[$path]) { throw 'Removal proof changed; history retained.' }
        }
        foreach ($relative in $Plan.GamePins.Keys) {
            $file=$api.Open.Invoke($gameFence,[object[]]@($relative,$false,$false));$leases.Add($file)
            if ($api.Hash.Invoke($file,@()) -cne $Plan.GamePins[$relative]) { throw 'Signed helper evidence changed; history retained.' }
        }
        $water=$api.Open.Invoke($gameFence,[object[]]@('BetterAstralParty.update.version',$true,$false)); $leases.Add($water)
        if ($Plan.WaterIdentity -and $api.RootText.GetValue($api.Identity.GetValue($water),$null) -cne $Plan.WaterIdentity) { throw 'Retained update history file identity changed; nothing retired.' }
        if ($api.Hash.Invoke($water,@()) -cne $Plan.WaterHash) { throw 'Update history changed; nothing retired.' }
        Assert-BapReinstallAbsent $Bundle
        $nonce=[guid]::NewGuid().ToString('N')
        $archive='BetterAstralParty.update.version.uninstalled-'+$nonce
        $record=@{schema=1;purpose='clean-reinstall-generation';root=$Plan.RootIdentity;fromVersion=$Plan.Version;toVersion=$Bundle.Version.Text;packageManifestSha256=$Bundle.ManifestSha256;backupId=$Plan.BackupId;proofSha256=$Plan.ProofHash;waterSha256=$Plan.WaterHash;archive=$archive}
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($record | ConvertTo-Json -Compress))
        $intent=$api.Open.Invoke($gameFence,[object[]]@('BetterAstralParty.reinstall.pending',$true,$true)); $leases.Add($intent)
        $stream=$api.Stream.GetValue($intent,$null); $stream.Write($bytes,0,$bytes.Length); $null=$api.Flush.Invoke($intent,@())
        # Failure/interruption leaves the intent and all original evidence; never auto-clean or retry.
        $null=$api.Rename.Invoke($water,[object[]]@($gameFence,$archive)); $null=$api.Flush.Invoke($water,@())
        if ($api.Hash.Invoke($water,@()) -cne $Plan.WaterHash) { throw 'Archived history verification failed; retain reinstall intent.' }
        $null=$api.Rename.Invoke($intent,[object[]]@($gameFence,('BetterAstralParty.reinstall.completed-'+$nonce))); $null=$api.Flush.Invoke($intent,@())
        Write-Host "이전 업데이트 이력을 보존하고 새 설치를 준비했습니다. / Previous history archived; new installation generation ready: $archive"
    } finally {
        for ($i=$leases.Count-1;$i -ge 0;$i--) { $leases[$i].Dispose() }
        if ($backupLock) { $backupLock.Dispose() }
        if ($backupFence) { $backupFence.Dispose() }; if ($gameFence) { $gameFence.Dispose() }
        if ($maintenance) { $maintenance.Dispose() }
    }
}
