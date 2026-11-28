[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InventoryPath,
    [Parameter(Mandatory)] [string] $AuthorizationPath,
    [Parameter(Mandatory)] [string] $PlanPath,
    [Parameter(Mandatory)] [string] $ReleaseCandidate,
    [Parameter(Mandatory)] [string] $OwnerAuthorizationId,
    [string] $JournalPath,
    [string] $JournalRepository,
    [string] $JournalBranch,
    [string] $JournalGitHubPath,
    [string] $JournalRefBaseSha
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PublicationArchive.ps1')
. (Join-Path $PSScriptRoot 'PublicationState.ps1')

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_COMPLETION_REFUSED: $Message")
    exit 1
}

$inventory = Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json
$authorization = Get-Content -Raw -LiteralPath $AuthorizationPath | ConvertFrom-Json
$plan = Get-Content -Raw -LiteralPath $PlanPath | ConvertFrom-Json
$inventoryHash = Get-FileSha256Hex $InventoryPath

if ([string] $authorization.inventorySha256 -cne $inventoryHash) { Refuse('Owner authorization is not bound to this inventory.') }
if ([string] $authorization.authorizationId -cne $OwnerAuthorizationId) { Refuse('Owner authorization ID mismatch.') }
if ([string] $plan.inventorySha256 -cne $inventoryHash -or [string] $plan.authorizationId -cne $OwnerAuthorizationId) {
    Refuse('Publication plan is not bound to this inventory and authorization.')
}
if ([string] $plan.releaseCandidate -cne $ReleaseCandidate) { Refuse('Publication plan release candidate mismatch.') }

$binding = [pscustomobject]@{
    inventoryId = [string] $inventory.inventoryId
    inventorySha256 = $inventoryHash
    authorizationId = $OwnerAuthorizationId
    releaseCandidate = [string] $ReleaseCandidate
}
$store = New-PublicationJournalStore -Path $JournalPath -Repository $JournalRepository -Branch $JournalBranch -GitHubPath $JournalGitHubPath -RefBaseSha $JournalRefBaseSha
$journal = Read-PublicationJournal $store
$check = Test-PublicationJournalBinding $journal $binding
if (!$check.valid) { Refuse("Durable publication journal is not usable: $($check.error)") }

$states = @()
foreach ($operation in @($inventory.packages | Sort-Object order)) {
    $state = Get-PublicationOperationJournalState $journal $operation.file
    $states += [ordered]@{
        order = [int] $operation.order
        file = [string] $operation.file
        kind = [string] $operation.kind
        state = $state.state
        event = $state.event
        verified = $state.verified
        attemptPending = $state.attemptPending
        conflict = $state.conflict
        manual = $state.manual
    }
}

$incomplete = @($states | Where-Object { !$_.verified -or $_.conflict -or $_.manual -or $_.attemptPending })
if ($incomplete.Count -ne 0) {
    $detail = ($incomplete | ForEach-Object { "$($_.file)=$($_.state)" }) -join ', '
    Refuse("Publication sequence is not complete: $detail")
}

$record = Add-PublicationJournalRecord -Store $store -Binding $binding -Event 'sequence-completed' -State 'published-and-verified-matching' -Detail ([ordered]@{
    operations = @($states)
    workflowRunId = [string] $env:GITHUB_RUN_ID
    workflowRunAttempt = [string] $env:GITHUB_RUN_ATTEMPT
})
Write-Host 'PUBLICATION_SEQUENCE=COMPLETE'
Write-Host "PUBLICATION_SEQUENCE_RECORD_HASH=$($record.hash)"
exit 0
