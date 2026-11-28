[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InventoryPath,
    [Parameter(Mandatory)] [string] $ArtifactDirectory,
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $OfflineFeedDirectory,
    [string] $Destination,
    [ValidateSet('None', 'Structural', 'DotnetVerify')] [string] $SignatureVerification = 'DotnetVerify',
    [switch] $RequireRepositorySignature,
    [string] $JournalPath,
    [string] $JournalRepository,
    [string] $JournalBranch,
    [string] $JournalGitHubPath,
    [string] $JournalRefBaseSha,
    [string] $AuthorizationId,
    [string] $FlatContainerBaseUrl = 'https://api.nuget.org/v3-flatcontainer',
    [string] $SymbolServerBaseUrl = 'https://symbols.nuget.org/download/symbols',
    [int] $PollAttempts = 5,
    [int] $PollDelaySeconds = 5,
    [string] $DotnetPath = 'dotnet',
    [string] $OnlyOperationFile,
    [switch] $PackagesOnly,
    [switch] $FirstPublication
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PublicationArchive.ps1')
. (Join-Path $PSScriptRoot 'PublicationState.ps1')

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_RECONCILIATION_REFUSED: $Message")
    exit 1
}

function Get-HttpStatusCode($ErrorRecord) {
    $response = $ErrorRecord.Exception.Response
    if ($null -eq $response) { return 0 }
    try {
        if ($response.StatusCode) { return [int] $response.StatusCode }
    }
    catch { }
    try { return [int] $response.StatusCode.value__ } catch { return 0 }
}

function Invoke-BoundedDownload {
    param(
        [Parameter(Mandatory)] [string] $Uri,
        [Parameter(Mandatory)] [string] $OutFile,
        [hashtable] $Headers
    )
    $attempt = 0
    $lastStatus = 0
    $lastError = $null
    while ($attempt -lt [Math]::Max(1, $PollAttempts)) {
        $attempt++
        try {
            $parameters = @{ Uri = $Uri; OutFile = $OutFile; UseBasicParsing = $true; ErrorAction = 'Stop' }
            if ($Headers) { $parameters['Headers'] = $Headers }
            Invoke-WebRequest @parameters | Out-Null
            return [pscustomobject]@{ status = 200; attempts = $attempt; error = $null }
        }
        catch {
            $status = Get-HttpStatusCode $_
            $lastStatus = $status
            $lastError = $_.Exception.Message
            if ($status -eq 404 -or $status -eq 0 -or $status -eq 408 -or $status -eq 429 -or ($status -ge 500 -and $status -le 599)) {
                if ($attempt -lt [Math]::Max(1, $PollAttempts)) {
                    if ($PollDelaySeconds -gt 0) { Start-Sleep -Seconds $PollDelaySeconds }
                    continue
                }
            }
            break
        }
    }
    return [pscustomobject]@{ status = $lastStatus; attempts = $attempt; error = $lastError }
}

function Invoke-BoundedBytesDownload {
    param(
        [Parameter(Mandatory)] [string] $Uri,
        [hashtable] $Headers
    )
    $attempt = 0
    $lastStatus = 0
    $lastError = $null
    while ($attempt -lt [Math]::Max(1, $PollAttempts)) {
        $attempt++
        try {
            $parameters = @{ Uri = $Uri; UseBasicParsing = $true; ErrorAction = 'Stop' }
            if ($Headers) { $parameters['Headers'] = $Headers }
            $response = Invoke-WebRequest @parameters
            return [pscustomobject]@{ status = 200; attempts = $attempt; error = $null; bytes = [byte[]] $response.Content }
        }
        catch {
            $status = Get-HttpStatusCode $_
            $lastStatus = $status
            $lastError = $_.Exception.Message
            if ($status -eq 404 -or $status -eq 0 -or $status -eq 408 -or $status -eq 429 -or ($status -ge 500 -and $status -le 599)) {
                if ($attempt -lt [Math]::Max(1, $PollAttempts)) {
                    if ($PollDelaySeconds -gt 0) { Start-Sleep -Seconds $PollDelaySeconds }
                    continue
                }
            }
            break
        }
    }
    return [pscustomobject]@{ status = $lastStatus; attempts = $attempt; error = $lastError; bytes = $null }
}

function Test-RepositorySignature {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $VerificationMode,
        [bool] $RequireRepository
    )
    if ($VerificationMode -eq 'None') {
        return [pscustomobject]@{ verified = $true; repository = $false; mode = 'none'; output = '' }
    }
    if ($VerificationMode -eq 'Structural') {
        return [pscustomobject]@{ verified = $true; repository = $false; mode = 'structural'; output = '' }
    }
    $previousRevocation = $env:NUGET_CERT_REVOCATION_MODE
    $env:NUGET_CERT_REVOCATION_MODE = 'offline'
    $verifyPath = [System.IO.Path]::GetFullPath($Path)
    Push-Location ([System.IO.Path]::GetTempPath())
    try {
        $output = @(& $DotnetPath nuget verify $verifyPath --all 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        if ($null -eq $previousRevocation) { Remove-Item Env:NUGET_CERT_REVOCATION_MODE -ErrorAction SilentlyContinue }
        else { $env:NUGET_CERT_REVOCATION_MODE = $previousRevocation }
    }
    $text = (@($output) -join "`n").Trim()
    $hasRepository = $text -match 'Signature type:\s*Repository'
    $verified = ($exitCode -eq 0)
    if ($RequireRepository -and !$hasRepository) { $verified = $false }
    return [pscustomobject]@{
        verified = $verified
        repository = $hasRepository
        mode = 'dotnet-nuget-verify-all'
        exitCode = $exitCode
        output = $text
    }
}

$inventory = Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json
if (!$Destination) { $Destination = [string] $inventory.destination }
if ([string] $inventory.destination -cne [string] $Destination) {
    Refuse('Reconciliation destination is not the approved destination.')
}
if (!$OfflineFeedDirectory -and [string] $inventory.destination -cne 'https://api.nuget.org/v3/index.json') {
    Refuse('Online reconciliation only supports the approved NuGet.org destination.')
}
if ($OfflineFeedDirectory -and !(Test-Path -LiteralPath $OfflineFeedDirectory -PathType Container)) {
    Refuse("Offline feed directory is missing: $OfflineFeedDirectory")
}
if ($RequireRepositorySignature -and $SignatureVerification -ne 'DotnetVerify') {
    Refuse('Repository signature verification requires DotnetVerify mode.')
}
$inventoryHash = Get-FileSha256Hex $InventoryPath
$binding = [pscustomobject]@{
    inventoryId = [string] $inventory.inventoryId
    inventorySha256 = $inventoryHash
    authorizationId = [string] $AuthorizationId
    releaseCandidate = [string] $inventory.releaseCandidate.commit
}

$journalStore = $null
$journal = $null
$journalError = $null
if ($JournalPath -or $JournalRepository) {
    $journalStore = New-PublicationJournalStore -Path $JournalPath -Repository $JournalRepository -Branch $JournalBranch -GitHubPath $JournalGitHubPath -RefBaseSha $JournalRefBaseSha
    $journal = Read-PublicationJournal $journalStore
    $bindingCheck = Test-PublicationJournalBinding $journal $binding
    if (!$bindingCheck.valid) {
        $journalError = $bindingCheck.error
        $journal = $null
    }
}
elseif (!$FirstPublication) {
    $journalError = 'no durable publication journal was supplied; 404 visibility cannot be distinguished from an ambiguous prior attempt'
}

function Get-OperationPriorAttempt([string] $File) {
    if ($null -eq $journal) { return $null }
    return Get-PublicationOperationJournalState $journal $File
}

$results = @()
$conflicts = @()
$holds = @()
foreach ($package in @($inventory.packages | Where-Object { $_.kind -eq 'package' -and (!$OnlyOperationFile -or $_.file -ceq $OnlyOperationFile) })) {
    $acceptedPath = Join-Path $ArtifactDirectory $package.file
    if (!(Test-Path -LiteralPath $acceptedPath -PathType Leaf)) {
        Refuse("Accepted package is missing: $($package.file)")
    }
    $acceptedHash = Get-FileSha256Hex $acceptedPath
    if ($acceptedHash -cne [string] $package.sha256) {
        Refuse("Accepted package is not the approved artifact: $($package.file)")
    }
    $identity = Get-NuspecIdentity $acceptedPath ([string] $package.id)
    if ($identity.id -cne [string] $package.id -or $identity.version -cne [string] $package.version) {
        Refuse("Accepted package identity is not the approved identity: $($package.file)")
    }

    $prior = Get-OperationPriorAttempt $package.file
    $publicPath = $null
    $status = 'manual-reconciliation-required'
    $action = 'hold'
    $comparison = $null
    $signature = $null
    $httpStatus = $null
    $attempts = 0
    $detail = $null

    if ($OfflineFeedDirectory) {
        $candidate = Join-Path $OfflineFeedDirectory $package.file
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $publicPath = $candidate
            $httpStatus = 200
        }
        else {
            $httpStatus = 404
        }
    }
    else {
        $id = ([string] $package.id).ToLowerInvariant()
        $version = ([string] $package.version).ToLowerInvariant()
        $url = "$($FlatContainerBaseUrl.TrimEnd('/'))/$id/$version/$id.$version.nupkg"
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("dogwoodsharp-reconcile-" + [Guid]::NewGuid().ToString('N') + '.nupkg')
        $download = Invoke-BoundedDownload -Uri $url -OutFile $tmp
        $httpStatus = $download.status
        $attempts = $download.attempts
        if ($httpStatus -eq 200) { $publicPath = $tmp }
        else { $detail = $download.error }
    }

    if ($publicPath) {
        $publicHash = Get-FileSha256Hex $publicPath
        $allowSignature = ($SignatureVerification -ne 'None') -or $RequireRepositorySignature
        try {
            $comparison = Test-PackagePayloadEquivalence -AcceptedPath $acceptedPath -PublicPath $publicPath -AllowRepositorySignature:$allowSignature
        }
        catch {
            $comparison = [pscustomobject]@{
                payloadMatches = $false
                mismatches = @("archive-rejected: $($_.Exception.Message)")
                missing = @()
                unexpected = @()
                changed = @()
                acceptedPayloadEntries = 0
                publicPayloadEntries = 0
                publicHasSignature = $false
            }
        }
        $publicIdentity = $null
        try { $publicIdentity = Get-NuspecIdentity $publicPath ([string] $package.id) } catch { }
        $identityMatches = ($null -ne $publicIdentity -and $publicIdentity.id -ceq [string] $package.id -and $publicIdentity.version -ceq [string] $package.version)
        if ($comparison.payloadMatches -and $identityMatches) {
            $signature = Test-RepositorySignature -Path $publicPath -VerificationMode $SignatureVerification -RequireRepository ([bool] $RequireRepositorySignature)
            if ($signature.verified) {
                $status = 'published-matching'
                $action = 'skip'
            }
            else {
                $status = 'published-conflicting'
                $action = 'refuse'
                $conflicts += "$($package.id)/$($package.version): repository signature verification failed"
            }
        }
        else {
            $status = 'published-conflicting'
            $action = 'refuse'
            $mismatchList = @($comparison.mismatches)
            if (!$identityMatches) { $mismatchList += 'identity-or-version-mismatch' }
            $conflicts += "$($package.id)/$($package.version): $($mismatchList -join ', ')"
        }
    }
    elseif ($httpStatus -eq 404) {
        if ($null -ne $prior -and ($prior.hasAttempt -or $prior.accepted -or $prior.manual)) {
            $status = if ($prior.accepted) { 'accepted-by-publication-endpoint-but-not-yet-visible' } else { 'publication-outcome-unknown' }
            $action = 'hold'
            $holds += "$($package.file): HTTP 404 after a durable publication attempt remains ambiguous; owner reconciliation is required"
        }
        elseif ($null -eq $journal -and !$FirstPublication) {
            $status = 'manual-reconciliation-required'
            $action = 'hold'
            $holds += "$($package.file): HTTP 404 without a durable journal cannot be treated as absence ($journalError)"
        }
        else {
            $status = 'not-previously-attempted-and-absent'
            $action = 'push'
        }
    }
    else {
        $status = 'registry-unavailable'
        $action = 'hold'
        $holds += "$($package.file): registry query did not produce a conclusive response (HTTP $httpStatus)"
    }

    $results += [ordered]@{
        file = [string] $package.file
        id = [string] $package.id
        version = [string] $package.version
        acceptedUploadSha256 = $acceptedHash
        publicDownloadedSha256 = if ($publicPath) { Get-FileSha256Hex $publicPath } else { $null }
        payloadMatches = if ($comparison) { $comparison.payloadMatches } else { $null }
        payloadMismatches = if ($comparison) { @($comparison.mismatches) } else { @() }
        publicHasRepositorySignature = if ($comparison) { $comparison.publicHasSignature } else { $null }
        repositorySignature = $signature
        httpStatus = $httpStatus
        pollAttempts = $attempts
        status = $status
        action = $action
        priorAttempt = if ($prior) { $prior.attemptPending } else { $false }
        detail = $detail
    }
}

$symbolResults = @()
foreach ($symbol in @($inventory.packages | Where-Object { $_.kind -eq 'symbols' -and !$PackagesOnly -and (!$OnlyOperationFile -or $_.file -ceq $OnlyOperationFile) })) {
    $package = @($inventory.packages | Where-Object { $_.kind -eq 'package' -and $_.id -ceq $symbol.id })[0]
    $packagePath = Join-Path $ArtifactDirectory $package.file
    $symbolPath = Join-Path $ArtifactDirectory $symbol.file
    if (!(Test-Path -LiteralPath $symbolPath -PathType Leaf)) {
        Refuse("Accepted symbol package is missing: $($symbol.file)")
    }
    if ((Get-FileSha256Hex $symbolPath) -cne [string] $symbol.sha256) {
        Refuse("Accepted symbol package is not the approved artifact: $($symbol.file)")
    }
    try {
        $expected = @(Get-ExpectedSymbolIdentities $packagePath $symbolPath)
    }
    catch {
        $symbolResults += [ordered]@{
            file = [string] $symbol.file
            id = [string] $symbol.id
            version = [string] $symbol.version
            acceptedUploadSha256 = [string] $symbol.sha256
            expectedPdbCount = 0
            status = 'symbol-state-requires-manual-reconciliation'
            action = 'hold'
            priorAttempt = $false
            pdbs = @()
            detail = "symbol inventory could not be bound: $($_.Exception.Message)"
        }
        $holds += "$($symbol.file): $($_.Exception.Message)"
        continue
    }
    $prior = Get-OperationPriorAttempt $symbol.file
    $entries = @()
    $present = 0
    $conflicting = 0
    $unavailable = 0
    foreach ($identity in $expected) {
        $url = "$($SymbolServerBaseUrl.TrimEnd('/'))/$($identity.pdbName)/$($identity.symbolKey)/$($identity.pdbName)"
        if ($OfflineFeedDirectory) {
            # An offline symbol feed mirrors the symbol-server layout.
            $candidate = Join-Path $OfflineFeedDirectory ("symbols/$($identity.pdbName)/$($identity.symbolKey)/$($identity.pdbName)")
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $bytes = [System.IO.File]::ReadAllBytes($candidate)
                $download = [pscustomobject]@{ status = 200; attempts = 1; bytes = $bytes; error = $null }
            }
            else {
                $download = [pscustomobject]@{ status = 404; attempts = 1; bytes = $null; error = 'not present in offline symbol feed' }
            }
        }
        else {
            $download = Invoke-BoundedBytesDownload -Uri $url -Headers @{ SymbolChecksum = "SHA256:$($identity.pdbChecksumSha256)" }
        }
        $entryStatus = 'unknown'
        $actualSha = $null
        if ($download.status -eq 200 -and $null -ne $download.bytes) {
            $actualSha = Get-Sha256Hex $download.bytes
            if ($actualSha -ceq [string] $identity.pdbSha256 -and [int64] $download.bytes.Length -eq [int64] $identity.pdbSize) {
                $entryStatus = 'available-matching'
                $present++
            }
            else {
                $entryStatus = 'content-conflict'
                $conflicting++
            }
        }
        elseif ($download.status -eq 404) {
            $entryStatus = 'absent'
        }
        else {
            $entryStatus = 'unavailable'
            $unavailable++
        }
        $entries += [ordered]@{
            tfm = [string] $identity.tfm
            pdbName = [string] $identity.pdbName
            pdbGuid = [string] $identity.pdbGuid
            symbolKey = [string] $identity.symbolKey
            expectedPdbSha256 = [string] $identity.pdbSha256
            expectedPdbChecksumSha256 = [string] $identity.pdbChecksumSha256
            observedPdbSha256 = $actualSha
            httpStatus = $download.status
            pollAttempts = $download.attempts
            status = $entryStatus
        }
    }

    if ($conflicting -gt 0) {
        $status = 'symbols-inconsistent-or-conflicting'
        $action = 'refuse'
        $conflicts += "$($symbol.id)/$($symbol.version): symbol content conflict"
    }
    elseif ($present -eq $entries.Count) {
        $status = 'symbols-available-and-verified'
        $action = 'skip'
    }
    elseif ($unavailable -gt 0) {
        $status = 'symbol-state-requires-manual-reconciliation'
        $action = 'hold'
        $holds += "$($symbol.file): symbol server did not produce a conclusive response"
    }
    elseif ($null -ne $prior -and ($prior.hasAttempt -or $prior.accepted)) {
        if ($prior.accepted) {
            $status = 'symbol-indexing-pending'
            $action = 'hold'
            $holds += "$($symbol.file): symbol upload was accepted but the expected PDBs are not yet indexed; continue bounded reconciliation or owner recovery"
        }
        else {
            $status = 'symbol-upload-outcome-unknown'
            $action = 'hold'
            $holds += "$($symbol.file): symbol upload outcome is unknown after a durable attempt; owner reconciliation is required"
        }
    }
    elseif ($null -eq $journal -and !$FirstPublication) {
        $status = 'symbol-state-requires-manual-reconciliation'
        $action = 'hold'
        $holds += "$($symbol.file): symbols absent without a durable journal ($journalError)"
    }
    else {
        $status = 'symbol-upload-not-attempted'
        $action = 'push'
    }

    $symbolResults += [ordered]@{
        file = [string] $symbol.file
        id = [string] $symbol.id
        version = [string] $symbol.version
        acceptedUploadSha256 = [string] $symbol.sha256
        expectedPdbCount = $entries.Count
        status = $status
        action = $action
        priorAttempt = if ($prior) { $prior.attemptPending } else { $false }
        pdbs = $entries
    }
}

$report = [ordered]@{
    schemaVersion = 2
    destination = [string] $Destination
    offlineFeedDirectory = $OfflineFeedDirectory
    stateModelVersion = 1
    journal = [ordered]@{
        configured = ($null -ne $journalStore)
        valid = ($null -ne $journal)
        error = $journalError
    }
    packages = $results
    symbols = $symbolResults
    conflicts = $conflicts
    holds = $holds
    result = if ($conflicts.Count -gt 0) { 'conflict' } elseif ($holds.Count -gt 0) { 'incomplete' } else { 'no-conflict' }
}
$outDir = Split-Path ([System.IO.Path]::GetFullPath($OutputPath)) -Parent
if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputPath), (($report | ConvertTo-Json -Depth 12) + "`n"), (New-Object System.Text.UTF8Encoding($false)))

foreach ($row in $results) {
    Write-Host "PACKAGE $($row.id) $($row.version) $($row.status) $($row.action)"
}
foreach ($row in $symbolResults) {
    Write-Host "SYMBOLS $($row.id) $($row.version) $($row.status) $($row.action)"
}
if ($conflicts.Count -ne 0) {
    Refuse("Publication conflicts: $($conflicts -join '; ')")
}
if ($holds.Count -ne 0) {
    Refuse("Publication state is inconclusive: $($holds -join '; ')")
}
Write-Host 'PUBLICATION_RECONCILIATION=NO_CONFLICT'
exit 0
