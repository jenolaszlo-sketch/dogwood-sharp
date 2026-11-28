[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $OutputPath,
    [string] $LogPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (!$RepositoryRoot) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
if (!$OutputPath) {
    $OutputPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-prep-01/evidence/publication-controls.json'
}
if (!$LogPath) {
    $LogPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-prep-01/evidence/publication-controls.log'
}

$record = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-prep-01'
$inventory = Join-Path $record 'publication-inventory.json'
$sourceMetadata = Join-Path $record 'evidence/source-metadata.json'
$dryRunAuth = Join-Path $record 'OWNER-AUTHORIZATION-DRY-RUN.json'
$baseArtifacts = Join-Path $record 'artifacts'
$verifier = Join-Path $RepositoryRoot 'scripts/publication/Test-PublicationPlan.ps1'
$reportPath = Join-Path $record 'evidence/publication-plan.dry-run.json'
$workRoot = Join-Path $RepositoryRoot ("artifacts/publication-controls/" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

$psExe = if ($PSVersionTable.PSEdition -eq 'Core') {
    if ($IsWindows) { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'pwsh' }
}
else {
    Join-Path $PSHOME 'powershell.exe'
}

$log = New-Object System.Text.StringBuilder
function Add-Log([string] $Text) {
    [void] $log.AppendLine($Text)
    Write-Host $Text
}

function Write-JsonFile([string] $Path, $Object) {
    $json = $Object | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($Path, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
}

function Copy-BaseArtifacts([string] $Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $baseArtifacts '*') -Destination $Destination -Force
}

function New-RepackedPackage([string] $Source, [string] $Destination, [scriptblock] $Mutate) {
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    $zip = [System.IO.Compression.ZipFile]::Open($Destination, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        & $Mutate $zip
    }
    finally {
        $zip.Dispose()
    }
}

function Set-ZipEntryText($Zip, [string] $Name, [scriptblock] $Transform) {
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing entry $Name" }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $entry.Delete()
    $new = $Zip.CreateEntry($Name)
    $writer = New-Object System.IO.StreamWriter($new.Open())
    try { $writer.Write((& $Transform $text)) } finally { $writer.Dispose() }
}

function Set-ZipEntryBytes($Zip, [string] $Name, [byte[]] $Bytes) {
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing entry $Name" }
    $entry.Delete()
    $new = $Zip.CreateEntry($Name)
    $stream = $new.Open()
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
        $stream.WriteByte(0)
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ZipEntryBytes($Zip, [string] $Name) {
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing entry $Name" }
    $stream = $entry.Open()
    try {
        $memory = New-Object System.IO.MemoryStream
        try {
            $stream.CopyTo($memory)
            return [byte[]] $memory.ToArray()
        }
        finally { $memory.Dispose() }
    }
    finally { $stream.Dispose() }
}

function New-Args([hashtable] $Overrides) {
    $map = @{
        InventoryPath = $inventory
        ArtifactDirectory = $baseArtifacts
        SourceMetadataPath = $sourceMetadata
        AuthorizationPath = $dryRunAuth
        ReleaseCandidate = '58b0a3332391d85cdf19fd7b794d79516d2ecdfe'
        SourceRunId = '46053558029'
        SourceRunAttempt = '1'
        Ref = 'refs/heads/main'
        Environment = 'nuget-publication'
        Destination = 'https://api.nuget.org/v3/index.json'
        OwnerAuthorizationId = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'
        RepositoryRoot = $RepositoryRoot
        ConcurrencyLockPath = (Join-Path $workRoot 'publication.lock')
        DryRun = $true
        SkipGitChecks = $true
    }
    foreach ($key in $Overrides.Keys) {
        $map[$key] = $Overrides[$key]
    }
    $list = @()
    foreach ($key in @($map.Keys | Sort-Object)) {
        $value = $map[$key]
        if ($value -is [bool]) {
            if ($value) { $list += "-$key" }
        }
        else {
            $list += "-$key"
            $list += [string] $value
        }
    }
    return $list
}

function Invoke-Control([int] $Id, [string] $Name, [hashtable] $Overrides, [string] $ExpectedRefusal, [bool] $ExpectSuccess) {
    $planPath = Join-Path $workRoot ("plan-$Id.json")
    $argumentList = New-Args $Overrides
    $argumentList += @('-PlanPath', $planPath)
    Add-Log ""
    Add-Log "=== $Id $Name"
    Add-Log ("ARGS " + ($argumentList -join ' '))
    $stdoutPath = Join-Path $workRoot ("stdout-$Id.txt")
    $stderrPath = Join-Path $workRoot ("stderr-$Id.txt")
    $processArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $verifier) + $argumentList
    $proc = Start-Process -FilePath $psExe -ArgumentList $processArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $exitCode = $proc.ExitCode
    $stdoutText = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -Raw -LiteralPath $stdoutPath } else { '' }
    $stderrText = if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw -LiteralPath $stderrPath } else { '' }
    $text = "$stdoutText`n$stderrText"
    $output = @((($stdoutText -split "`r?`n") + ($stderrText -split "`r?`n")) | Where-Object { $_ -ne '' })
    Add-Log $text.Trim()
    $planExists = Test-Path -LiteralPath $planPath -PathType Leaf
    $message = ''
    foreach ($line in @($output)) {
        if ([string] $line -match 'PUBLICATION_REFUSED:|PUBLICATION_PLAN=VERIFIED') {
            $message = [string] $line
            break
        }
    }
    $passed = $false
    if ($ExpectSuccess) {
        $passed = ($exitCode -eq 0) -and $planExists -and ($text -match 'PUBLICATION_PLAN=VERIFIED')
    }
    else {
        $passed = ($exitCode -ne 0) -and !$planExists -and ($text -match [regex]::Escape($ExpectedRefusal))
    }
    if (!$passed) {
        Add-Log "RESULT FAIL (exit $exitCode, planExists=$planExists, expected '$ExpectedRefusal')"
    }
    else {
        Add-Log "RESULT PASS (exit $exitCode, planExists=$planExists)"
    }
    return [ordered]@{
        id = $Id
        name = $Name
        expected = if ($ExpectSuccess) { 'verified' } else { $ExpectedRefusal }
        executed = $true
        exitCode = $exitCode
        planExists = $planExists
        refused = ($exitCode -ne 0 -and !$planExists)
        message = $message
        passed = $passed
    }
}

$results = @()
$positive = Invoke-Control 0 'positive dry-run using the exact four accepted artifacts' @{} 'PUBLICATION_PLAN=VERIFIED' $true
if ($positive.passed) {
    Copy-Item -LiteralPath (Join-Path $workRoot 'plan-0.json') -Destination $reportPath -Force
}
$results += $positive

# 1. Missing owner publication authorization: live mode with the dry-run authorization.
$results += Invoke-Control 1 'missing owner publication authorization' @{ DryRun = $false } 'Missing owner publication authorization.' $false

# 2. Incorrect release-candidate identity.
$results += Invoke-Control 2 'incorrect release-candidate identity' @{ ReleaseCandidate = '0000000000000000000000000000000000000000' } 'Incorrect release-candidate identity.' $false

# 3. Incorrect source workflow run.
$results += Invoke-Control 3 'incorrect source workflow run' @{ SourceRunId = '46053558028' } 'Incorrect source workflow run.' $false

# 4. Unsuccessful source workflow.
$metaFailure = Join-Path $workRoot 'source-failure.json'
$meta = Get-Content -Raw -LiteralPath $sourceMetadata | ConvertFrom-Json
$meta.run.conclusion = 'failure'
Write-JsonFile $metaFailure $meta
$results += Invoke-Control 4 'unsuccessful source workflow' @{ SourceMetadataPath = $metaFailure } 'Source workflow did not complete successfully.' $false

# 5. Missing artifact.
$missingDir = Join-Path $workRoot 'missing'
Copy-BaseArtifacts $missingDir
Remove-Item (Join-Path $missingDir 'DogwoodSharp.0.1.0-preview.1.snupkg') -Force
$results += Invoke-Control 5 'missing artifact' @{ ArtifactDirectory = $missingDir } 'Missing artifact' $false

# 6. Modified package (same entries, changed bytes).
$modifiedPkgDir = Join-Path $workRoot 'modified-package'
Copy-BaseArtifacts $modifiedPkgDir
New-RepackedPackage (Join-Path $baseArtifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $modifiedPkgDir 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    Set-ZipEntryText $zip 'README.md' { param($t) $t + "`nmodified-package" }
}
$results += Invoke-Control 6 'modified package' @{ ArtifactDirectory = $modifiedPkgDir } 'Modified package:' $false

# 7. Modified symbol package (same entries, changed bytes).
$modifiedSymDir = Join-Path $workRoot 'modified-symbol'
Copy-BaseArtifacts $modifiedSymDir
New-RepackedPackage (Join-Path $baseArtifacts 'DogwoodSharp.0.1.0-preview.1.snupkg') (Join-Path $modifiedSymDir 'DogwoodSharp.0.1.0-preview.1.snupkg') {
    param($zip)
    $bytes = Get-ZipEntryBytes $zip 'lib/net8.0/DogwoodSharp.pdb'
    Set-ZipEntryBytes $zip 'lib/net8.0/DogwoodSharp.pdb' $bytes
}
$results += Invoke-Control 7 'modified symbol package' @{ ArtifactDirectory = $modifiedSymDir } 'Modified symbol package:' $false

# 8. Unexpected additional package.
$extraDir = Join-Path $workRoot 'extra'
Copy-BaseArtifacts $extraDir
Copy-Item -LiteralPath (Join-Path $baseArtifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') -Destination (Join-Path $extraDir 'Unexpected.1.0.0.nupkg') -Force
$results += Invoke-Control 8 'unexpected additional package' @{ ArtifactDirectory = $extraDir } 'Unexpected additional package:' $false

# 9. Incorrect package ID.
$wrongIdDir = Join-Path $workRoot 'wrong-id'
Copy-BaseArtifacts $wrongIdDir
New-RepackedPackage (Join-Path $baseArtifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $wrongIdDir 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    Set-ZipEntryText $zip 'DogwoodSharp.nuspec' { param($t) $t.Replace('<id>DogwoodSharp</id>', '<id>DogwoodSharp.Wrong</id>') }
}
$results += Invoke-Control 9 'incorrect package ID' @{ ArtifactDirectory = $wrongIdDir } 'Incorrect package ID:' $false

# 10. Incorrect package version.
$wrongVersionDir = Join-Path $workRoot 'wrong-version'
Copy-BaseArtifacts $wrongVersionDir
New-RepackedPackage (Join-Path $baseArtifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $wrongVersionDir 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    Set-ZipEntryText $zip 'DogwoodSharp.nuspec' { param($t) $t.Replace('<version>0.1.0-preview.1</version>', '<version>0.1.0-preview.2</version>') }
}
$results += Invoke-Control 10 'incorrect package version' @{ ArtifactDirectory = $wrongVersionDir } 'Incorrect package version:' $false

# 11. CedarSharp dependency accidentally included.
$cedarDir = Join-Path $workRoot 'cedar-dependency'
Copy-BaseArtifacts $cedarDir
$cedarSource = Join-Path $RepositoryRoot 'artifacts/rc-ci-46053558029/dogwoodsharp-d3-dependencies/cedarsharp.1.0.0.nupkg'
if (!(Test-Path -LiteralPath $cedarSource)) { throw 'The CedarSharp dependency file is required for control 11.' }
Copy-Item -LiteralPath $cedarSource -Destination (Join-Path $cedarDir 'cedarsharp.1.0.0.nupkg') -Force
$results += Invoke-Control 11 'CedarSharp dependency accidentally included' @{ ArtifactDirectory = $cedarDir } 'CedarSharp dependency was included' $false

# 12. Invocation from an unauthorized ref.
$results += Invoke-Control 12 'unauthorized ref' @{ Ref = 'refs/heads/feature' } 'Unauthorized ref:' $false

# 13. Missing or mismatched publication environment.
$results += Invoke-Control 13 'publication environment mismatch' @{ Environment = 'nuget-publication-other' } 'Publication environment mismatch.' $false

# 14. Duplicate or conflicting publication identity.
$duplicateInventory = Join-Path $workRoot 'inventory-duplicate.json'
$inv = Get-Content -Raw -LiteralPath $inventory | ConvertFrom-Json
$duplicate = $inv.packages[0] | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$duplicate.order = 5
$inv.packages = @($inv.packages) + @($duplicate)
Write-JsonFile $duplicateInventory $inv
$results += Invoke-Control 14 'duplicate or conflicting publication identity' @{ InventoryPath = $duplicateInventory } 'Duplicate or conflicting publication identity.' $false

# 15. Expired or unavailable source artifacts.
$metaExpired = Join-Path $workRoot 'source-expired.json'
$meta = Get-Content -Raw -LiteralPath $sourceMetadata | ConvertFrom-Json
$candidateArtifact = @($meta.artifacts | Where-Object { $_.name -ceq 'dogwoodsharp-d3-candidates' })[0]
$candidateArtifact.expired = $true
Write-JsonFile $metaExpired $meta
$results += Invoke-Control 15 'expired or unavailable source artifact' @{ SourceMetadataPath = $metaExpired } 'Source artifact expired:' $false

# 16. Unapproved publication destination.
$results += Invoke-Control 16 'unapproved publication destination' @{ Destination = 'https://api.nuget.org/v3/index.json.evil' } 'Unapproved publication destination.' $false

# 17. Attempted rebuild or repack (extra entry, same nuspec identity).
$repackedDir = Join-Path $workRoot 'repacked'
Copy-BaseArtifacts $repackedDir
New-RepackedPackage (Join-Path $baseArtifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $repackedDir 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    $entry = $zip.CreateEntry('extra.txt')
    $writer = New-Object System.IO.StreamWriter($entry.Open())
    try { $writer.Write('unexpected-repack-entry') } finally { $writer.Dispose() }
}
$results += Invoke-Control 17 'attempted rebuild or repack' @{ ArtifactDirectory = $repackedDir } 'Package entries differ from the accepted inventory:' $false

# 18. Concurrent publication attempt: pre-create the lock.
$concurrentLock = Join-Path $workRoot 'concurrent.lock'
[System.IO.File]::WriteAllText($concurrentLock, 'active', (New-Object System.Text.UTF8Encoding($false)))
$results += Invoke-Control 18 'concurrent publication attempt' @{ ConcurrencyLockPath = $concurrentLock } 'Concurrent publication attempt detected.' $false

$failed = @($results | Where-Object { !$_.passed })
$summary = [ordered]@{
    commission = 'DOGWOOD-SHARP-PUB-PREP-01'
    date = '2026-11-28'
    verifier = 'scripts/publication/Test-PublicationPlan.ps1'
    positivePlan = 'docs/reviews/dogwood-sharp-pub-prep-01/evidence/publication-plan.dry-run.json'
    total = $results.Count
    passed = @($results | Where-Object { $_.passed }).Count
    failed = $failed.Count
    results = $results
}
Write-JsonFile $OutputPath $summary
[System.IO.File]::WriteAllText($LogPath, $log.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Add-Log "PUBLICATION_CONTROLS=$($summary.passed)/$($summary.total)"
if ($failed.Count -ne 0) {
    exit 1
}
exit 0
