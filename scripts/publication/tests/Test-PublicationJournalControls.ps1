[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $OutputPath,
    [string] $LogPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$RepositoryRoot) { $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')) }
$review = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-prep-01'
$inventoryPath = Join-Path $review 'publication-inventory.json'
if (!$OutputPath) { $OutputPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/journal-controls.json' }
if (!$LogPath) { $LogPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/journal-controls.log' }
$inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
$inventoryHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
$authorizationId = 'DOGWOOD-SHARP-PUB-REMED-01-TEST-AUTHORIZATION-1'
$work = Join-Path $RepositoryRoot ('artifacts/publication-journal-controls/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$script:controls = @()
$script:log = New-Object System.Text.StringBuilder

function Add-Log([string] $Text) {
    [void] $script:log.AppendLine($Text)
    Write-Host $Text
}

function Write-JsonFile([string] $Path, $Object) {
    $dir = Split-Path $Path -Parent
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, (($Object | ConvertTo-Json -Depth 15) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

function Add-Control([string] $Name, [bool] $Passed, [string] $Detail, $Evidence) {
    $evidencePath = $null
    if ($null -ne $Evidence) {
        $evidenceDirectory = Join-Path (Split-Path $OutputPath -Parent) 'journal-evidence'
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        $safeName = $Name -replace '[^A-Za-z0-9._-]', '-'
        $evidencePath = Join-Path $evidenceDirectory "$safeName.json"
        Write-JsonFile $evidencePath $Evidence
    }
    $script:controls += [ordered]@{ name = $Name; passed = $Passed; detail = $Detail; evidenceFile = $evidencePath }
    if ($Passed) { Add-Log "CONTROL PASS $Name" } else { Add-Log "CONTROL FAIL $Name :: $Detail" }
}

function New-TestAuthorization([string] $Path, [bool] $Publication) {
    $authorization = [ordered]@{
        authorizationId = if ($Publication) { $authorizationId } else { 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1' }
        scope = if ($Publication) { 'publication' } else { 'dry-run' }
        dryRunAuthorized = (-not $Publication)
        publicationAuthorized = $Publication
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        releaseCandidate = [string] $inventory.releaseCandidate.commit
        sourceRunId = [int64] $inventory.source.runId
        environment = [string] $inventory.environment
        destination = [string] $inventory.destination
        packages = @($inventory.packages | ForEach-Object { [ordered]@{ file = [string] $_.file; sha256 = [string] $_.sha256 } })
    }
    Write-JsonFile $Path $authorization
}

function New-TestPlan([string] $Path) {
    Write-JsonFile $Path ([ordered]@{
        schemaVersion = 1
        status = 'verified'
        dryRun = $false
        credentialsRequested = $false
        uploadPerformed = $false
        inventorySha256 = $inventoryHash
        authorizationId = $authorizationId
        releaseCandidate = [string] $inventory.releaseCandidate.commit
    })
}

function Invoke-JournalScript([string] $Name, [string] $Script, [string[]] $Arguments) {
    $dir = Join-Path $work $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $stdout = Join-Path $dir 'stdout.txt'
    $stderr = Join-Path $dir 'stderr.txt'
    $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Script) + $Arguments) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $stdoutText = if (Test-Path $stdout) { [string] (Get-Content -Raw $stdout) } else { '' }
    $stderrText = if (Test-Path $stderr) { [string] (Get-Content -Raw $stderr) } else { '' }
    if ($null -eq $stdoutText) { $stdoutText = '' }
    if ($null -eq $stderrText) { $stderrText = '' }
    return [pscustomobject]@{ exitCode = $process.ExitCode; stdout = $stdoutText.Trim(); stderr = $stderrText.Trim() }
}

function Initialize-Journal([string] $JournalPath) {
    . (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
    $store = New-PublicationJournalStore -Path $JournalPath
    $binding = [pscustomobject]@{
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        authorizationId = $authorizationId
        releaseCandidate = [string] $inventory.releaseCandidate.commit
    }
    Add-PublicationJournalRecord -Store $store -Binding $binding -Event 'journal-initialized' -State 'binding-verified' -Detail ([ordered]@{ test = $true }) | Out-Null
}

function Add-Verified([string] $JournalPath, [int] $Order) {
    . (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
    $operation = @($inventory.packages | Where-Object { [int] $_.order -eq $Order })[0]
    $store = New-PublicationJournalStore -Path $JournalPath
    $binding = [pscustomobject]@{
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        authorizationId = $authorizationId
        releaseCandidate = [string] $inventory.releaseCandidate.commit
    }
    foreach ($event in @(
        @{ event = 'attempt-initiated'; state = 'publication-attempt-initiated' },
        @{ event = 'upload-accepted'; state = 'accepted-by-publication-endpoint-but-not-yet-visible' },
        @{ event = if ($operation.kind -eq 'package') { 'published-verified' } else { 'symbols-verified' }; state = if ($operation.kind -eq 'package') { 'published-and-verified-matching' } else { 'symbols-available-and-verified' } }
    )) {
        Add-PublicationJournalRecord -Store $store -Binding $binding -Event $event.event -State $event.state `
            -OperationOrder ([string] $operation.order) -OperationKind ([string] $operation.kind) `
            -OperationId ([string] $operation.id) -OperationVersion ([string] $operation.version) `
            -OperationFile ([string] $operation.file) -OperationSha256 ([string] $operation.sha256) `
            -OperationSize ([string] $operation.size) -AttemptId 'seed' -Detail ([ordered]@{ seed = $true }) | Out-Null
    }
}

$publicationAuth = Join-Path $work 'authorization-publication.json'
$dryRunAuth = Join-Path $work 'authorization-dry-run.json'
$plan = Join-Path $work 'plan.json'
New-TestAuthorization $publicationAuth $true
New-TestAuthorization $dryRunAuth $false
New-TestPlan $plan
$journal = Join-Path $work 'journal.jsonl'
$initScript = Join-Path $RepositoryRoot 'scripts/publication/Initialize-PublicationJournal.ps1'
$commonInit = @(
    '-InventoryPath', $inventoryPath,
    '-AuthorizationPath', $publicationAuth,
    '-PlanPath', $plan,
    '-ReleaseCandidate', [string] $inventory.releaseCandidate.commit,
    '-SourceRunId', [string] $inventory.source.runId,
    '-SourceRunAttempt', [string] $inventory.source.runAttempt,
    '-Environment', [string] $inventory.environment,
    '-Destination', [string] $inventory.destination,
    '-OwnerAuthorizationId', $authorizationId,
    '-JournalPath', $journal
)

$firstInit = Invoke-JournalScript -Name 'init-valid' -Script $initScript -Arguments $commonInit
. (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
$journalState = Read-PublicationJournal (New-PublicationJournalStore -Path $journal)
Add-Control 'journal-initialization-binds-inventory' ($firstInit.exitCode -eq 0 -and $journalState.valid -and $journalState.records.Count -eq 1 -and $journalState.records[0].record.event -eq 'journal-initialized' -and $journalState.records[0].record.inventorySha256 -eq $inventoryHash) "exit=$($firstInit.exitCode) valid=$($journalState.valid) records=$($journalState.records.Count)" ([ordered]@{ result = $firstInit })

$secondInit = Invoke-JournalScript -Name 'init-existing' -Script $initScript -Arguments $commonInit
$journalState = Read-PublicationJournal (New-PublicationJournalStore -Path $journal)
Add-Control 'journal-initialization-idempotent' ($secondInit.exitCode -eq 0 -and $secondInit.stdout -match 'EXISTING_RECORDS=1' -and $journalState.records.Count -eq 1) "exit=$($secondInit.exitCode) stdout=$($secondInit.stdout)" ([ordered]@{ result = $secondInit })

$dryRunJournal = Join-Path $work 'journal-dry-run.json'
$dryRunInitArgs = @($commonInit | ForEach-Object { $_ })
$dryRunInitArgs[$dryRunInitArgs.IndexOf('-AuthorizationPath') + 1] = $dryRunAuth
$dryRunInitArgs[$dryRunInitArgs.IndexOf('-OwnerAuthorizationId') + 1] = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'
$dryRunInitArgs[$dryRunInitArgs.IndexOf('-JournalPath') + 1] = $dryRunJournal
$dryRunInit = Invoke-JournalScript -Name 'init-dry-run' -Script $initScript -Arguments $dryRunInitArgs
Add-Control 'journal-refuses-dry-run-authorization' ($dryRunInit.exitCode -ne 0 -and $dryRunInit.stderr -match 'does not authorize publication' -and !(Test-Path -LiteralPath $dryRunJournal)) "exit=$($dryRunInit.exitCode) stderr=$($dryRunInit.stderr)" ([ordered]@{ result = $dryRunInit })

# Completion: complete sequence succeeds and writes a terminal record.
$completeJournal = Join-Path $work 'journal-complete.jsonl'
Initialize-Journal $completeJournal
foreach ($order in 1..4) { Add-Verified $completeJournal $order }
$completionScript = Join-Path $RepositoryRoot 'scripts/publication/Test-PublicationCompletion.ps1'
$completion = Invoke-JournalScript -Name 'completion-complete' -Script $completionScript -Arguments @(
    '-InventoryPath', $inventoryPath,
    '-AuthorizationPath', $publicationAuth,
    '-PlanPath', $plan,
    '-ReleaseCandidate', [string] $inventory.releaseCandidate.commit,
    '-OwnerAuthorizationId', $authorizationId,
    '-JournalPath', $completeJournal
)
$completedJournal = Read-PublicationJournal (New-PublicationJournalStore -Path $completeJournal)
Add-Control 'complete-four-operation-state' ($completion.exitCode -eq 0 -and $completion.stdout -match 'PUBLICATION_SEQUENCE=COMPLETE' -and $completedJournal.valid -and $completedJournal.records[$completedJournal.records.Count - 1].record.event -eq 'sequence-completed') "exit=$($completion.exitCode) stdout=$($completion.stdout)" ([ordered]@{ result = $completion })

# Completion: a partial sequence is refused.
$partialJournal = Join-Path $work 'journal-partial.jsonl'
Initialize-Journal $partialJournal
Add-Verified $partialJournal 1
Add-Verified $partialJournal 3
$partialCompletion = Invoke-JournalScript -Name 'completion-partial' -Script $completionScript -Arguments @(
    '-InventoryPath', $inventoryPath,
    '-AuthorizationPath', $publicationAuth,
    '-PlanPath', $plan,
    '-ReleaseCandidate', [string] $inventory.releaseCandidate.commit,
    '-OwnerAuthorizationId', $authorizationId,
    '-JournalPath', $partialJournal
)
$partialState = Read-PublicationJournal (New-PublicationJournalStore -Path $partialJournal)
Add-Control 'partial-sequence-refused' ($partialCompletion.exitCode -ne 0 -and $partialCompletion.stderr -match 'not complete' -and $partialState.records[$partialState.records.Count - 1].record.event -ne 'sequence-completed') "exit=$($partialCompletion.exitCode) stderr=$($partialCompletion.stderr)" ([ordered]@{ result = $partialCompletion })

$failed = @($script:controls | Where-Object { !$_.passed })
$resultObject = [ordered]@{
    schemaVersion = 1
    commission = 'DOGWOOD-SHARP-PUB-REMED-01'
    executedAtUtc = [DateTime]::UtcNow.ToString('o')
    total = $script:controls.Count
    passed = @($script:controls | Where-Object { $_.passed }).Count
    failed = $failed.Count
    controls = $script:controls
}
Write-JsonFile $OutputPath $resultObject
$logDir = Split-Path $LogPath -Parent
if ($logDir) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
[System.IO.File]::WriteAllText($LogPath, $script:log.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Add-Log "JOURNAL_CONTROLS=$($resultObject.passed)/$($resultObject.total)"
if ($failed.Count -ne 0) { exit 1 }
exit 0
