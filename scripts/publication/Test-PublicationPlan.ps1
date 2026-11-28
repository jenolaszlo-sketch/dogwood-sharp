[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InventoryPath,
    [Parameter(Mandatory)] [string] $ArtifactDirectory,
    [Parameter(Mandatory)] [string] $SourceMetadataPath,
    [Parameter(Mandatory)] [string] $AuthorizationPath,
    [Parameter(Mandatory)] [string] $ReleaseCandidate,
    [Parameter(Mandatory)] [string] $SourceRunId,
    [Parameter(Mandatory)] [string] $SourceRunAttempt,
    [Parameter(Mandatory)] [string] $Ref,
    [Parameter(Mandatory)] [string] $Environment,
    [Parameter(Mandatory)] [string] $Destination,
    [Parameter(Mandatory)] [string] $OwnerAuthorizationId,
    [switch] $DryRun,
    [string] $PlanPath,
    [string] $RepositoryRoot,
    [string] $ConcurrencyLockPath,
    [string] $WorkflowLockPath,
    [switch] $SkipGitChecks
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (!$RepositoryRoot) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_REFUSED: $Message")
    exit 1
}

function Read-Json([string] $Path, [string] $Description) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        Refuse("$Description is missing: $Path")
    }
    try {
        return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    }
    catch {
        Refuse("$Description is not valid JSON: $Path")
    }
}

function Get-LowerSha256([string] $Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        Refuse("File is missing: $Path")
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-RepoPath([string] $Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
}

function Get-ZipEntries([string] $Path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $result = @()
        foreach ($entry in @($archive.Entries | Where-Object { !$_.FullName.EndsWith('/') })) {
            $stream = $entry.Open()
            try {
                $memory = New-Object System.IO.MemoryStream
                try {
                    $stream.CopyTo($memory)
                    $result += [pscustomobject]@{
                        FullName = $entry.FullName
                        Length = $entry.Length
                        Bytes = [byte[]] $memory.ToArray()
                    }
                }
                finally {
                    $memory.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }
        }
        return $result
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ZipEntryBytes($Entry) {
    return [byte[]] $Entry.Bytes
}

function Get-BytesSha256([byte[]] $Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-ZipEntryText($Entry) {
    $bytes = Get-ZipEntryBytes $Entry
    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    try { return $utf8.GetString($bytes) }
    catch { Refuse("Package entry is not valid UTF-8: $($Entry.FullName)") }
}

function Get-NuspecMetadata([string] $Text) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    try {
        $xml.LoadXml(($Text -replace '^\uFEFF', ''))
    }
    catch {
        Refuse('Package nuspec is not valid XML.')
    }
    $idNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='id']")
    $versionNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']")
    if ($null -eq $idNode -or $null -eq $versionNode) {
        Refuse('Package nuspec has no exact id or version.')
    }
    $dependencies = @()
    foreach ($group in @($xml.SelectNodes("//*[local-name()='metadata']/*[local-name()='dependencies']/*[local-name()='group']"))) {
        $tfm = $group.GetAttribute('targetFramework')
        foreach ($dependency in @($group.SelectNodes("./*[local-name()='dependency']"))) {
            $dependencies += "$tfm`:$($dependency.GetAttribute('id'))`:$($dependency.GetAttribute('version'))"
        }
    }
    return [pscustomobject]@{
        id = $idNode.InnerText
        version = $versionNode.InnerText
        dependencies = @($dependencies | Sort-Object -CaseSensitive)
    }
}

function Assert-ExactEntrySet([string[]] $Actual, [string[]] $Expected, [string] $Description) {
    $a = @($Actual | Sort-Object -CaseSensitive)
    $e = @($Expected | Sort-Object -CaseSensitive)
    if (($a -join "`n") -cne ($e -join "`n")) {
        $unexpected = @($a | Where-Object { $_ -notin $e })
        $missing = @($e | Where-Object { $_ -notin $a })
        Refuse("$Description. Unexpected: $($unexpected -join ', '). Missing: $($missing -join ', ').")
    }
}

function Assert-ZipPackage(
    [string] $Path,
    [pscustomobject] $Package,
    [pscustomobject] $Accepted,
    [bool] $IsSymbols) {

    $entries = @(Get-ZipEntries $Path)
    $entryNames = @($entries | ForEach-Object FullName)
    $nuspecName = "$($Package.id).nuspec"
    $nuspecEntry = @($entries | Where-Object FullName -CEQ $nuspecName)
    if ($nuspecEntry.Count -ne 1) {
        Refuse("Package identity mismatch: no unique $nuspecName in $($Package.file)")
    }
    $metadata = Get-NuspecMetadata (Get-ZipEntryText $nuspecEntry[0])
    if ([string] $metadata.id -cne $Package.id) {
        if ($IsSymbols) { Refuse("Incorrect symbol package ID: $($Package.file)") }
        Refuse("Incorrect package ID: $($Package.file)")
    }
    if ([string] $metadata.version -cne $Package.version) {
        if ($IsSymbols) { Refuse("Incorrect symbol package version: $($Package.file)") }
        Refuse("Incorrect package version: $($Package.file)")
    }

    $expectedEntries = if ($IsSymbols) { $Accepted.symbols.entries } else { $Accepted.entries }
    Assert-ExactEntrySet $entryNames $expectedEntries "Package entries differ from the accepted inventory: $($Package.file)"

    $actualHash = Get-LowerSha256 $Path
    if ($actualHash -cne $Package.sha256) {
        if ($IsSymbols) { Refuse("Modified symbol package: $($Package.file)") }
        Refuse("Modified package: $($Package.file)")
    }
    $actualSize = (Get-Item -LiteralPath $Path).Length
    if ([int64] $actualSize -ne [int64] $Package.size) {
        Refuse("Package size mismatch: $($Package.file)")
    }

    if ($IsSymbols) {
        foreach ($tfm in @('net8.0', 'net10.0')) {
            $pdb = "lib/$tfm/$($Package.id).pdb"
            if ($entryNames -cnotcontains $pdb) {
                Refuse("Symbol package is missing $pdb")
            }
        }
        if (@($entryNames | Where-Object { $_ -like 'runtimes/*' }).Count -ne 0) {
            Refuse("Symbol package contains native assets: $($Package.file)")
        }
    }
    else {
        if (@($entryNames | Where-Object { $_ -like '*.pdb' }).Count -ne 0) {
            Refuse("Main package contains PDBs: $($Package.file)")
        }
        if ($Package.id -eq 'DogwoodSharp') {
            if (@($metadata.dependencies).Count -ne 0) {
                Refuse('Stable package has unexpected dependencies.')
            }
            foreach ($rid in @('win-x64', 'linux-x64', 'osx-arm64')) {
                $imagePath = "runtimes/$rid/native/$($Accepted.nativeAssets.$rid.file)"
                $manifestPath = "runtimes/$rid/native/dogwoodsharp-native.json"
                $imageEntry = @($entries | Where-Object FullName -CEQ $imagePath)
                $manifestEntry = @($entries | Where-Object FullName -CEQ $manifestPath)
                if ($imageEntry.Count -ne 1 -or $manifestEntry.Count -ne 1) {
                    Refuse("Stable package is missing the $rid native image/manifest pair.")
                }
                $imageHash = Get-BytesSha256 (Get-ZipEntryBytes $imageEntry[0])
                $manifestHash = Get-BytesSha256 (Get-ZipEntryBytes $manifestEntry[0])
                if ($imageHash -cne $Accepted.nativeAssets.$rid.imageSha256 -or
                    $manifestHash -cne $Accepted.nativeAssets.$rid.manifestSha256) {
                    Refuse("Native asset identity differs from the accepted inventory for $rid.")
                }
                $manifest = Get-ZipEntryText $manifestEntry[0] | ConvertFrom-Json
                if ([string] $manifest.sha256 -cne $imageHash) {
                    Refuse("Packaged native manifest is stale for $rid.")
                }
                if ([string] $manifest.rid -cne $rid -or
                    [string] $manifest.file -cne $Accepted.nativeAssets.$rid.file -or
                    [string] $manifest.cargoLockSha256 -cne 'ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa' -or
                    [string] $manifest.dogwoodRevision -cne '929f49e7305534ac61ef06cf8e47269c596dc851' -or
                    [string] $manifest.dogwoodVersion -cne '1.1.2' -or
                    [string] $manifest.cedarVersion -cne '4.11.0' -or
                    [string] $manifest.rustVersion -cne '1.99.1') {
                    Refuse("Packaged native manifest identity differs for $rid.")
                }
            }
        }
        elseif ($Package.id -eq 'DogwoodSharp.CedarSharp') {
            if (@($entryNames | Where-Object { $_ -like 'runtimes/*' }).Count -ne 0) {
                Refuse('Experimental package contains native runtime assets.')
            }
            $expectedDependencies = @($Accepted.dependencies | Sort-Object -CaseSensitive)
            Assert-ExactEntrySet @($metadata.dependencies) $expectedDependencies 'Experimental package dependencies differ from the accepted inventory.'
        }
    }
}

# 1. Authorization and exact binding.
$inventory = Read-Json $InventoryPath 'Publication inventory'
$inventoryIdentities = @($inventory.packages | ForEach-Object { "$($_.id)/$($_.version)/$($_.kind)" })
if (($inventoryIdentities | Sort-Object -Unique).Count -ne $inventoryIdentities.Count) {
    Refuse('Duplicate or conflicting publication identity.')
}
$source = Read-Json $SourceMetadataPath 'Source run metadata'
$auth = Read-Json $AuthorizationPath 'Owner publication authorization'
$report = Read-Json (Resolve-RepoPath $inventory.verificationReport.path) 'Accepted package verification report'
$acceptedInventory = Read-Json (Resolve-RepoPath $inventory.acceptedPackageInventory.path) 'Accepted package inventory'
$cedarHashes = Read-Json (Resolve-RepoPath $inventory.cedarSharpHashManifest.path) 'CedarSharp hash manifest'

$inventoryHash = Get-LowerSha256 $InventoryPath
if ([string] $auth.inventorySha256 -cne $inventoryHash) {
    Refuse('Owner publication authorization is not bound to this inventory.')
}
if ([string] $auth.authorizationId -cne $OwnerAuthorizationId) {
    Refuse('Owner publication authorization ID does not match the invocation.')
}
if ([string] $auth.releaseCandidate -cne [string] $inventory.releaseCandidate.commit) {
    Refuse('Owner publication authorization release candidate mismatch.')
}
if ([int64] $auth.sourceRunId -ne [int64] $inventory.source.runId) {
    Refuse('Owner publication authorization source run mismatch.')
}
if ([string] $auth.environment -cne [string] $inventory.environment) {
    Refuse('Owner publication authorization environment mismatch.')
}
if ([string] $auth.destination -cne [string] $inventory.destination) {
    Refuse('Owner publication authorization destination mismatch.')
}
$authPackages = @($auth.packages | ForEach-Object { "$($_.file):$($_.sha256)" } | Sort-Object -CaseSensitive)
$inventoryPackages = @($inventory.packages | ForEach-Object { "$($_.file):$($_.sha256)" } | Sort-Object -CaseSensitive)
if (($authPackages -join "`n") -cne ($inventoryPackages -join "`n")) {
    Refuse('Owner publication authorization package inventory mismatch.')
}
if ($DryRun) {
    if ([string] $auth.scope -cne 'dry-run' -or $auth.dryRunAuthorized -ne $true -or $auth.publicationAuthorized -ne $false) {
        Refuse('Missing dry-run authorization.')
    }
}
else {
    if ([string] $auth.scope -cne 'publication' -or $auth.publicationAuthorized -ne $true -or $auth.dryRunAuthorized -ne $false) {
        Refuse('Missing owner publication authorization.')
    }
}

if ($Ref -cne 'refs/heads/main') {
    Refuse('Unauthorized ref: publication is restricted to refs/heads/main.')
}
if ($ReleaseCandidate -cne [string] $inventory.releaseCandidate.commit) {
    Refuse('Incorrect release-candidate identity.')
}
if ($SourceRunId -cne [string] $inventory.source.runId) {
    Refuse('Incorrect source workflow run.')
}
if ($SourceRunAttempt -cne [string] $inventory.source.runAttempt) {
    Refuse('Incorrect source workflow run attempt.')
}
if ($Environment -cne [string] $inventory.environment) {
    Refuse('Publication environment mismatch.')
}
if ($Destination -cne [string] $inventory.destination) {
    Refuse('Unapproved publication destination.')
}

# 2. Source run and artifact provenance.
$run = $source.run
if ($null -eq $run) { Refuse('Source run metadata is incomplete.') }
if ([string] $run.repository.full_name -cne [string] $inventory.source.repository) {
    Refuse('Source repository mismatch.')
}
if ([string] $run.path -cne [string] $inventory.source.workflowPath) {
    Refuse('Source workflow path mismatch.')
}
if ([int64] $run.workflow_id -ne [int64] $inventory.source.workflowId) {
    Refuse('Source workflow ID mismatch.')
}
if ([string] $run.head_sha -cne [string] $inventory.releaseCandidate.commit) {
    Refuse('Incorrect release-candidate identity in the source run.')
}
if ([int64] $run.run_attempt -ne [int64] $inventory.source.runAttempt) {
    Refuse('Source workflow run attempt mismatch.')
}
if ([string] $run.event -cne [string] $inventory.source.event) {
    Refuse('Source workflow event mismatch.')
}
if ([string] $run.status -cne 'completed' -or [string] $run.conclusion -cne [string] $inventory.source.conclusion) {
    Refuse('Source workflow did not complete successfully.')
}
$artifacts = @($source.artifacts)
foreach ($required in @($inventory.sourceArtifact, $inventory.verificationArtifact, $inventory.dependencyArtifact)) {
    $matches = @($artifacts | Where-Object { $_.name -ceq $required.name })
    if ($matches.Count -ne 1) {
        Refuse("Source artifact is missing or duplicated: $($required.name)")
    }
    $a = $matches[0]
    if ([int64] $a.id -ne [int64] $required.id -or
        [string] $a.digest -cne [string] $required.digest -or
        [int64] $a.size_in_bytes -ne [int64] $required.sizeInBytes) {
        Refuse("Source artifact identity mismatch: $($required.name)")
    }
    if ($a.expired -eq $true) {
        Refuse("Source artifact expired: $($required.name)")
    }
    if ($null -ne $a.expires_at) {
        try {
            $expires = [DateTimeOffset]::Parse([string] $a.expires_at)
            if ($expires -lt [DateTimeOffset]::UtcNow) {
                Refuse("Source artifact expired: $($required.name)")
            }
        }
        catch {
            Refuse("Source artifact expiry is invalid: $($required.name)")
        }
    }
}

# 3. Exact artifact set and package identities.
if (!(Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    Refuse("Artifact directory is missing: $ArtifactDirectory")
}
$expectedFiles = @($inventory.packages | ForEach-Object { [string] $_.file } | Sort-Object -CaseSensitive)
$actualFiles = @(Get-ChildItem -LiteralPath $ArtifactDirectory -File | ForEach-Object Name | Sort-Object -CaseSensitive)
if (($actualFiles -join "`n") -cne ($expectedFiles -join "`n")) {
    $unexpected = @($actualFiles | Where-Object { $_ -notin $expectedFiles })
    $missing = @($expectedFiles | Where-Object { $_ -notin $actualFiles })
    if (@($unexpected | Where-Object { $_ -match '^cedarsharp\.' }).Count -ne 0) {
        Refuse('CedarSharp dependency was included in the publication set.')
    }
    if ($missing.Count -ne 0) {
        Refuse("Missing artifact: $($missing -join ', ')")
    }
    Refuse("Unexpected additional package: $($unexpected -join ', ')")
}
foreach ($file in $actualFiles) {
    if ($file -match '^cedarsharp\.') {
        Refuse('CedarSharp dependency was included in the publication set.')
    }
}
$stablePackage = @($inventory.packages | Where-Object { $_.id -ceq 'DogwoodSharp' -and $_.kind -ceq 'package' })
$stableSymbols = @($inventory.packages | Where-Object { $_.id -ceq 'DogwoodSharp' -and $_.kind -ceq 'symbols' })
$experimentalPackage = @($inventory.packages | Where-Object { $_.id -ceq 'DogwoodSharp.CedarSharp' -and $_.kind -ceq 'package' })
$experimentalSymbols = @($inventory.packages | Where-Object { $_.id -ceq 'DogwoodSharp.CedarSharp' -and $_.kind -ceq 'symbols' })
if ($stablePackage.Count -ne 1 -or $stableSymbols.Count -ne 1 -or $experimentalPackage.Count -ne 1 -or $experimentalSymbols.Count -ne 1) {
    Refuse('Publication inventory must contain exactly one package and one symbol package for each authorized ID.')
}
foreach ($package in $inventory.packages) {
    $path = Join-Path $ArtifactDirectory $package.file
    if ($package.id -eq 'DogwoodSharp') {
        Assert-ZipPackage $path $package $report.stable ($package.kind -eq 'symbols')
    }
    elseif ($package.id -eq 'DogwoodSharp.CedarSharp') {
        Assert-ZipPackage $path $package $report.experimental ($package.kind -eq 'symbols')
    }
    else {
        Refuse("Incorrect package ID: $($package.id)")
    }
}

# 4. Accepted inventory and dependency reconciliation.
foreach ($package in $inventory.packages) {
    $match = @($acceptedInventory.files | Where-Object { $_.name -ceq $package.file })
    if ($match.Count -ne 1) {
        Refuse("Accepted package inventory is missing $($package.file)")
    }
    if ([string] $match[0].sha256 -cne [string] $package.sha256 -or [int64] $match[0].size -ne [int64] $package.size) {
        Refuse("Accepted package inventory mismatch for $($package.file)")
    }
}
if ([string] $report.stable.sha256 -cne [string] $stablePackage[0].sha256 -or
    [string] $report.stable.symbolsSha256 -cne [string] $stableSymbols[0].sha256 -or
    [string] $report.experimental.sha256 -cne [string] $experimentalPackage[0].sha256 -or
    [string] $report.experimental.symbolsSha256 -cne [string] $experimentalSymbols[0].sha256) {
    Refuse('Package hashes do not match the accepted verification report.')
}
if ([string] $report.cargoLockSha256 -cne 'ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa') {
    Refuse('Accepted verification report lock hash is not the committed shipped lock.')
}
if ([string] $report.cedarSharp.sha256 -cne [string] $inventory.dependency.sha256 -or
    [int64] $report.cedarSharp.entryCount -ne [int64] $inventory.dependency.entryCount -or
    [string] $report.cedarSharp.entriesSha256 -cne [string] $inventory.dependency.entriesSha256) {
    Refuse('CedarSharp dependency identity differs from the accepted verification report.')
}
if ([string] $cedarHashes.packageId -cne 'CedarSharp' -or
    [string] $cedarHashes.packageVersion -cne '1.0.0' -or
    [string] $cedarHashes.packageSha256 -cne [string] $inventory.dependency.sha256 -or
    [string] $cedarHashes.entriesSha256 -cne [string] $inventory.dependency.entriesSha256 -or
    [int64] $cedarHashes.entryCount -ne [int64] $inventory.dependency.entryCount) {
    Refuse('CedarSharp hash manifest differs from the approved dependency identity.')
}

# 5. Workflow revision lock.
if ($WorkflowLockPath -and !$SkipGitChecks) {
    $lock = Read-Json $WorkflowLockPath 'Publication workflow lock'
    foreach ($item in @($lock.files)) {
        $path = Join-Path $RepositoryRoot $item.path
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
            Refuse("Workflow lock file is missing: $($item.path)")
        }
        $blob = (& git -C $RepositoryRoot hash-object -- $item.path).Trim()
        if ($LASTEXITCODE -ne 0) { Refuse('Unable to hash the publication workflow revision.') }
        if ($blob -cne [string] $item.blobSha) {
            Refuse("Publication workflow revision is not the approved revision: $($item.path)")
        }
    }
    if ($lock.approvedAtCommit) {
        & git -C $RepositoryRoot merge-base --is-ancestor ([string] $lock.approvedAtCommit) HEAD
        if ($LASTEXITCODE -ne 0) {
            Refuse('Publication workflow lock is not an ancestor of the invoked revision.')
        }
    }
}

# 6. Local concurrency guard (the workflow concurrency group is the primary guard).
$lockAcquired = $false
$lockFull = $null
if ($ConcurrencyLockPath) {
    $lockFull = Resolve-RepoPath $ConcurrencyLockPath
    $lockDir = Split-Path $lockFull -Parent
    if ($lockDir) { New-Item -ItemType Directory -Force -Path $lockDir | Out-Null }
    try {
        $lockStream = [System.IO.File]::Open($lockFull, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $lockStream.Dispose()
        $lockAcquired = $true
    }
    catch {
        Refuse('Concurrent publication attempt detected.')
    }
}

try {
    $plan = [ordered]@{
        schemaVersion = 1
        status = 'verified'
        dryRun = [bool] $DryRun
        credentialsRequested = $false
        uploadPerformed = $false
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        authorizationId = [string] $auth.authorizationId
        authorizationScope = [string] $auth.scope
        releaseCandidate = [string] $inventory.releaseCandidate.commit
        releaseCandidateTree = [string] $inventory.releaseCandidate.tree
        sourceRepository = [string] $inventory.source.repository
        sourceRunId = [int64] $inventory.source.runId
        sourceRunAttempt = [int64] $inventory.source.runAttempt
        sourceWorkflowPath = [string] $inventory.source.workflowPath
        sourceArtifactName = [string] $inventory.sourceArtifact.name
        sourceArtifactId = [int64] $inventory.sourceArtifact.id
        sourceArtifactDigest = [string] $inventory.sourceArtifact.digest
        destination = [string] $inventory.destination
        environment = [string] $inventory.environment
        verificationReportSha256 = Get-LowerSha256 (Resolve-RepoPath $inventory.verificationReport.path)
        acceptedPackageInventorySha256 = Get-LowerSha256 (Resolve-RepoPath $inventory.acceptedPackageInventory.path)
        packages = @($inventory.packages | Sort-Object order | ForEach-Object {
            [ordered]@{
                order = [int] $_.order
                kind = [string] $_.kind
                id = [string] $_.id
                version = [string] $_.version
                file = [string] $_.file
                size = [int64] $_.size
                sha256 = [string] $_.sha256
            }
        })
        publishOrder = @($inventory.publishOrder)
        dependency = [ordered]@{
            id = [string] $inventory.dependency.id
            version = [string] $inventory.dependency.version
            sha256 = [string] $inventory.dependency.sha256
            publicationArtifact = $false
        }
        rebuilt = $false
        repacked = $false
    }

    if ($PlanPath) {
        $planFull = [System.IO.Path]::GetFullPath($PlanPath)
        $planDir = Split-Path $planFull -Parent
        if ($planDir) { New-Item -ItemType Directory -Force -Path $planDir | Out-Null }
        $json = $plan | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($planFull, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
    }

    Write-Host "PUBLICATION_PLAN=VERIFIED"
    Write-Host "PUBLICATION_DRY_RUN=$([bool] $DryRun)"
    Write-Host "PUBLICATION_PACKAGES=$($inventory.packages.Count)"
    Write-Host "PUBLICATION_ORDER=$($inventory.publishOrder -join ',')"
}
finally {
    if ($lockAcquired -and $lockFull -and (Test-Path -LiteralPath $lockFull)) {
        Remove-Item -LiteralPath $lockFull -Force -ErrorAction SilentlyContinue
    }
}
exit 0
