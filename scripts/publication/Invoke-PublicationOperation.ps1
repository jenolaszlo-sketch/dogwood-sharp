[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $Order,
    [Parameter(Mandatory)] [string] $InventoryPath,
    [Parameter(Mandatory)] [string] $ArtifactDirectory,
    [Parameter(Mandatory)] [string] $AuthorizationPath,
    [Parameter(Mandatory)] [string] $PlanPath,
    [Parameter(Mandatory)] [string] $ReleaseCandidate,
    [Parameter(Mandatory)] [string] $SourceRunId,
    [Parameter(Mandatory)] [string] $SourceRunAttempt,
    [Parameter(Mandatory)] [string] $Environment,
    [Parameter(Mandatory)] [string] $Destination,
    [Parameter(Mandatory)] [string] $OwnerAuthorizationId,
    [string] $JournalPath,
    [string] $JournalRepository,
    [string] $JournalBranch,
    [string] $JournalGitHubPath,
    [string] $JournalRefBaseSha,
    [string] $FlatContainerBaseUrl = 'https://api.nuget.org/v3-flatcontainer',
    [string] $SymbolServerBaseUrl = 'https://symbols.nuget.org/download/symbols',
    [ValidateSet('None', 'Structural', 'DotnetVerify')] [string] $SignatureVerification = 'DotnetVerify',
    [switch] $RequireRepositorySignature,
    [int] $PollAttempts = 5,
    [int] $PollDelaySeconds = 5,
    [string] $DotnetPath = 'dotnet',
    [string] $ReconcileScript,
    [switch] $SkipPlanChecks,
    [string] $ReportDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PublicationArchive.ps1')
. (Join-Path $PSScriptRoot 'PublicationState.ps1')

if (!$ReconcileScript) { $ReconcileScript = Join-Path $PSScriptRoot 'Reconcile-Publication.ps1' }
if (!$ReportDirectory) { $ReportDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('dogwoodsharp-publication-' + [Guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_OPERATION_REFUSED: $Message")
    exit 1
}

function Write-JsonAtomic([string] $Path, $Object) {
    $json = $Object | ConvertTo-Json -Depth 15
    [System.IO.File]::WriteAllText($Path, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-Reconciliation {
    param(
        [Parameter(Mandatory)] [string] $OperationFile,
        [switch] $PackagesOnly
    )
    $reportPath = Join-Path $ReportDirectory ("reconcile-" + [Guid]::NewGuid().ToString('N') + '.json')
    $arguments = @(
        '-NoProfile',
        '-File', $ReconcileScript,
        '-InventoryPath', $InventoryPath,
        '-ArtifactDirectory', $ArtifactDirectory,
        '-OutputPath', $reportPath,
        '-Destination', $Destination,
        '-JournalPath', $JournalPath,
        '-AuthorizationId', $OwnerAuthorizationId,
        '-FlatContainerBaseUrl', $FlatContainerBaseUrl,
        '-SymbolServerBaseUrl', $SymbolServerBaseUrl,
        '-SignatureVerification', $SignatureVerification,
        '-PollAttempts', ([string] $PollAttempts),
        '-PollDelaySeconds', ([string] $PollDelaySeconds),
        '-DotnetPath', $DotnetPath,
        '-OnlyOperationFile', $OperationFile
    )
    if ($JournalRepository) {
        $arguments += @('-JournalRepository', $JournalRepository, '-JournalBranch', $JournalBranch, '-JournalGitHubPath', $JournalGitHubPath, '-JournalRefBaseSha', $JournalRefBaseSha)
    }
    if ($RequireRepositorySignature) { $arguments += '-RequireRepositorySignature' }
    if ($PackagesOnly) { $arguments += '-PackagesOnly' }
    $parameters = @{
        FilePath = (Get-Process -Id $PID).Path
        ArgumentList = $arguments
        NoNewWindow = $true
        Wait = $true
        PassThru = $true
        RedirectStandardOutput = (Join-Path $ReportDirectory 'reconcile.stdout.txt')
        RedirectStandardError = (Join-Path $ReportDirectory 'reconcile.stderr.txt')
    }
    if ($env:OS -eq 'Windows_NT') {
        $parameters['ArgumentList'] = @('-NoProfile', '-ExecutionPolicy', 'Bypass') + $arguments[1..($arguments.Count - 1)]
    }
    $process = Start-Process @parameters
    $stdout = if (Test-Path $parameters.RedirectStandardOutput) { [string] (Get-Content -Raw $parameters.RedirectStandardOutput) } else { '' }
    $stderr = if (Test-Path $parameters.RedirectStandardError) { [string] (Get-Content -Raw $parameters.RedirectStandardError) } else { '' }
    $report = $null
    if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
        $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    }
    return [pscustomobject]@{
        exitCode = $process.ExitCode
        stdout = $stdout
        stderr = $stderr
        report = $report
        reportPath = $reportPath
    }
}

function Get-ReportRow($Report, [string] $OperationFile) {
    if ($null -eq $Report) { return $null }
    return @(@($Report.packages) + @($Report.symbols) | Where-Object { $_.file -ceq $OperationFile })[0]
}

function Get-RowProperty($Row, [string] $Name) {
    if ($null -eq $Row) { return $null }
    if ($null -eq $Row.PSObject.Properties[$Name]) { return $null }
    return $Row.$Name
}

function Add-Record {
    param([string] $Event, [string] $State, $Detail)
    return Add-PublicationJournalRecord -Store $store -Binding $binding `
        -Event $Event -State $State `
        -OperationOrder ([string] $operation.order) `
        -OperationKind ([string] $operation.kind) `
        -OperationId ([string] $operation.id) `
        -OperationVersion ([string] $operation.version) `
        -OperationFile ([string] $operation.file) `
        -OperationSha256 ([string] $operation.sha256) `
        -OperationSize ([string] $operation.size) `
        -AttemptId ([string] $attemptId) `
        -Detail $Detail
}

if (!(Test-Path -LiteralPath $InventoryPath -PathType Leaf)) { Refuse('Publication inventory is missing.') }
if (!(Test-Path -LiteralPath $AuthorizationPath -PathType Leaf)) { Refuse('Owner publication authorization is missing.') }
if (!(Test-Path -LiteralPath $PlanPath -PathType Leaf)) { Refuse('Publication plan is missing.') }

$inventory = Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json
$authorization = Get-Content -Raw -LiteralPath $AuthorizationPath | ConvertFrom-Json
$plan = Get-Content -Raw -LiteralPath $PlanPath | ConvertFrom-Json
$inventoryHash = Get-FileSha256Hex $InventoryPath

if ([string] $authorization.inventorySha256 -cne $inventoryHash) { Refuse('Owner authorization is not bound to this inventory.') }
if ([string] $authorization.authorizationId -cne $OwnerAuthorizationId) { Refuse('Owner authorization ID does not match the invocation.') }
if ([string] $authorization.scope -cne 'publication' -or $authorization.publicationAuthorized -ne $true) {
    Refuse('Owner authorization does not authorize publication.')
}
if ([string] $ReleaseCandidate -cne [string] $inventory.releaseCandidate.commit) { Refuse('Release-candidate identity mismatch.') }
if ([string] $SourceRunId -cne [string] $inventory.source.runId) { Refuse('Source run identity mismatch.') }
if ([string] $SourceRunAttempt -cne [string] $inventory.source.runAttempt) { Refuse('Source run attempt mismatch.') }
if ([string] $Environment -cne [string] $inventory.environment) { Refuse('Publication environment mismatch.') }
if ([string] $Destination -cne [string] $inventory.destination) { Refuse('Publication destination mismatch.') }
if (!$SkipPlanChecks) {
    if ($plan.status -cne 'verified' -or [bool] $plan.dryRun -ne $false) { Refuse('Publication plan is not a verified live plan.') }
    if ([string] $plan.inventorySha256 -cne $inventoryHash) { Refuse('Publication plan is not bound to this inventory.') }
    if ([string] $plan.authorizationId -cne $OwnerAuthorizationId) { Refuse('Publication plan is not bound to this authorization.') }
    if ([string] $plan.releaseCandidate -cne $ReleaseCandidate) { Refuse('Publication plan release candidate mismatch.') }
    if ([bool] $plan.uploadPerformed -ne $false -or [bool] $plan.credentialsRequested -ne $false) { Refuse('Publication plan is not in the pre-publication state.') }
}

$operation = @($inventory.packages | Where-Object { [int] $_.order -eq $Order })
if ($operation.Count -ne 1) { Refuse("No unique publication operation for order $Order.") }
$operation = $operation[0]
$operationPath = Join-Path $ArtifactDirectory $operation.file
if (!(Test-Path -LiteralPath $operationPath -PathType Leaf)) { Refuse("Operation artifact is missing: $($operation.file)") }
if ((Get-FileSha256Hex $operationPath) -cne [string] $operation.sha256 -or
    [int64] (Get-Item -LiteralPath $operationPath).Length -ne [int64] $operation.size) {
    Refuse("Operation artifact does not match the accepted inventory: $($operation.file)")
}

$binding = [pscustomobject]@{
    inventoryId = [string] $inventory.inventoryId
    inventorySha256 = $inventoryHash
    authorizationId = $OwnerAuthorizationId
    releaseCandidate = [string] $ReleaseCandidate
}
$store = New-PublicationJournalStore -Path $JournalPath -Repository $JournalRepository -Branch $JournalBranch -GitHubPath $JournalGitHubPath -RefBaseSha $JournalRefBaseSha
$journal = Read-PublicationJournal $store
$bindingCheck = Test-PublicationJournalBinding $journal $binding
if (!$bindingCheck.valid) { Refuse("Durable publication journal is not usable: $($bindingCheck.error)") }
$priorState = Get-PublicationOperationJournalState $journal $operation.file
$attemptId = "$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)-$Order-$(Get-Date -Format 'yyyyMMddHHmmss')"

if ($priorState.verified) {
    Write-Host "PUBLICATION_OPERATION=$Order ALREADY_VERIFIED=$($operation.file)"
    exit 0
}
if ($priorState.conflict) {
    Refuse("Operation $Order has a recorded conflict and requires owner reconciliation.")
}
if ($priorState.manual) {
    Refuse("Operation $Order is recorded as requiring owner reconciliation.")
}

# Dependent operations require the public core package to be verified matching.
$dependencyOrder = if ($Order -eq 2 -or $Order -eq 3) { 1 } elseif ($Order -eq 4) { 3 } else { 0 }
if ($dependencyOrder -gt 0) {
    $dependency = @($inventory.packages | Where-Object { [int] $_.order -eq $dependencyOrder })[0]
    $live = Invoke-Reconciliation -OperationFile $dependency.file -PackagesOnly
    $row = Get-ReportRow $live.report $dependency.file
    if ($null -eq $row -or [string] $row.status -cne 'published-matching') {
        Add-Record -Event 'manual-reconciliation-required' -State 'manual-reconciliation-required' -Detail ([ordered]@{
            reason = 'dependent package is not publicly verified matching'
            dependency = $dependency.file
            observedStatus = if ($row) { [string] $row.status } else { 'unavailable' }
        }) | Out-Null
        Refuse("Operation $Order requires $($dependency.file) to be publicly available and matching before publication.")
    }
    Add-Record -Event 'reconciliation-observed' -State 'published-and-verified-matching' -Detail ([ordered]@{
        dependency = $dependency.file
        publicSha256 = [string] (Get-RowProperty $row 'publicDownloadedSha256')
        payloadMatches = [bool] (Get-RowProperty $row 'payloadMatches')
    }) | Out-Null
}

$current = Invoke-Reconciliation -OperationFile $operation.file -PackagesOnly:($operation.kind -eq 'package')
$row = Get-ReportRow $current.report $operation.file
if ($null -eq $row) { Refuse("Reconciliation did not return a state for $($operation.file).") }
$status = [string] $row.status

if ($status -in @('published-matching', 'symbols-available-and-verified')) {
    Add-Record -Event $(if ($operation.kind -eq 'package') { 'published-verified' } else { 'symbols-verified' }) `
        -State $(if ($operation.kind -eq 'package') { 'published-and-verified-matching' } else { 'symbols-available-and-verified' }) `
        -Detail ([ordered]@{ publicSha256 = [string] (Get-RowProperty $row 'publicDownloadedSha256'); payloadMatches = $true; source = 'reconciliation' }) | Out-Null
    Write-Host "PUBLICATION_OPERATION=$Order VERIFIED=$($operation.file)"
    exit 0
}
if ($status -in @('published-conflicting', 'symbols-inconsistent-or-conflicting')) {
    Add-Record -Event 'conflict-detected' -State 'published-and-conflicting' -Detail ([ordered]@{
        status = $status
        mismatches = @(Get-RowProperty $row 'payloadMismatches')
    }) | Out-Null
    Refuse("Operation $Order has a conflicting public state: $($operation.file)")
}
if ($status -notin @('not-previously-attempted-and-absent', 'symbol-upload-not-attempted')) {
    Add-Record -Event 'reconciliation-observed' -State $status -Detail ([ordered]@{
        status = $status
        action = [string] (Get-RowProperty $row 'action')
        httpStatus = Get-RowProperty $row 'httpStatus'
    }) | Out-Null
    Write-Host "PUBLICATION_OPERATION=$Order HOLD=$status"
    exit 2
}

# Durable write-ahead record: the attempt must be persisted before the upload starts.
Assert-NoPendingPublicationAttempt $journal $operation.file
Add-Record -Event 'attempt-initiated' -State 'publication-attempt-initiated' -Detail ([ordered]@{
    destination = $Destination
    transport = if ($operation.kind -eq 'package') { 'dotnet-nuget-push-no-symbols' } else { 'dotnet-nuget-push-symbol-package' }
    plannedSha256 = [string] $operation.sha256
    plannedSize = [int64] $operation.size
    workflowRunId = [string] $env:GITHUB_RUN_ID
    workflowRunAttempt = [string] $env:GITHUB_RUN_ATTEMPT
}) | Out-Null

# Revalidate the complete authorized publication inventory immediately before upload.
foreach ($item in @($inventory.packages)) {
    $path = Join-Path $ArtifactDirectory $item.file
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileSha256Hex $path) -cne [string] $item.sha256 -or
        [int64] (Get-Item -LiteralPath $path).Length -ne [int64] $item.size) {
        Add-Record -Event 'attempt-aborted-before-upload' -State 'publication-attempt-aborted-before-upload' -Detail ([ordered]@{
            reason = 'artifact integrity verification failed immediately before upload'
            file = [string] $item.file
        }) | Out-Null
        Refuse("Artifact integrity verification failed immediately before upload: $($item.file)")
    }
}

$transportArguments = @('nuget', 'push', $operationPath, '--source', $Destination)
if ($operation.kind -eq 'package') { $transportArguments += '--no-symbols' }
Write-Host "PUBLICATION_OPERATION=$Order TRANSPORT=$($operation.kind) FILE=$($operation.file)"
$transportOutput = @(& $DotnetPath @transportArguments 2>&1)
$transportExit = $LASTEXITCODE
$transportText = (@($transportOutput) -join "`n").Trim()
$transportTail = if ($transportText.Length -gt 4000) { $transportText.Substring($transportText.Length - 4000) } else { $transportText }

if ($transportExit -eq 0) {
    Add-Record -Event 'upload-accepted' -State 'accepted-by-publication-endpoint-but-not-yet-visible' -Detail ([ordered]@{
        transportExitCode = $transportExit
        transportOutput = $transportTail
    }) | Out-Null
}
else {
    Add-Record -Event 'upload-outcome-unknown' -State 'publication-outcome-unknown' -Detail ([ordered]@{
        transportExitCode = $transportExit
        transportOutput = $transportTail
        note = 'The upload response was not conclusive. No automatic retry is permitted.'
    }) | Out-Null
}

$verify = Invoke-Reconciliation -OperationFile $operation.file -PackagesOnly:($operation.kind -eq 'package')
$verifyRow = Get-ReportRow $verify.report $operation.file
if ($null -ne $verifyRow -and [string] $verifyRow.status -in @('published-matching', 'symbols-available-and-verified')) {
    Add-Record -Event $(if ($operation.kind -eq 'package') { 'published-verified' } else { 'symbols-verified' }) `
        -State $(if ($operation.kind -eq 'package') { 'published-and-verified-matching' } else { 'symbols-available-and-verified' }) `
        -Detail ([ordered]@{ publicSha256 = [string] (Get-RowProperty $verifyRow 'publicDownloadedSha256'); payloadMatches = $true; transportExitCode = $transportExit }) | Out-Null
    Write-Host "PUBLICATION_OPERATION=$Order VERIFIED=$($operation.file)"
    exit 0
}
if ($null -ne $verifyRow -and [string] $verifyRow.status -in @('published-conflicting', 'symbols-inconsistent-or-conflicting')) {
    Add-Record -Event 'conflict-detected' -State 'published-and-conflicting' -Detail ([ordered]@{
        status = [string] $verifyRow.status
        mismatches = @(Get-RowProperty $verifyRow 'payloadMismatches')
        transportExitCode = $transportExit
    }) | Out-Null
    Refuse("Operation $Order produced a conflicting public state: $($operation.file)")
}

$observedStatus = if ($verifyRow) { [string] $verifyRow.status } else { 'registry-unavailable' }
Add-Record -Event 'reconciliation-observed' -State $observedStatus -Detail ([ordered]@{
    status = $observedStatus
    transportExitCode = $transportExit
    note = 'The publication operation is not conclusively verified. Owner reconciliation is required before any further upload.'
}) | Out-Null
Write-Host "PUBLICATION_OPERATION=$Order INCOMPLETE=$observedStatus"
exit 2
