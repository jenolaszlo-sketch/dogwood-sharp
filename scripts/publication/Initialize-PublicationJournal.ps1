[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InventoryPath,
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
    [string] $JournalRefBaseSha
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PublicationArchive.ps1')
. (Join-Path $PSScriptRoot 'PublicationState.ps1')

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_JOURNAL_REFUSED: $Message")
    exit 1
}

if (!(Test-Path -LiteralPath $InventoryPath -PathType Leaf)) { Refuse('Publication inventory is missing.') }
if (!(Test-Path -LiteralPath $AuthorizationPath -PathType Leaf)) { Refuse('Owner publication authorization is missing.') }
if (!(Test-Path -LiteralPath $PlanPath -PathType Leaf)) { Refuse('Verified publication plan is missing.') }

$inventory = Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json
$authorization = Get-Content -Raw -LiteralPath $AuthorizationPath | ConvertFrom-Json
$plan = Get-Content -Raw -LiteralPath $PlanPath | ConvertFrom-Json
$inventoryHash = Get-FileSha256Hex $InventoryPath

if ([string] $authorization.inventorySha256 -cne $inventoryHash) { Refuse('Owner authorization is not bound to this inventory.') }
if ([string] $authorization.authorizationId -cne $OwnerAuthorizationId) { Refuse('Owner authorization ID does not match the invocation.') }
if ([string] $authorization.scope -cne 'publication' -or $authorization.publicationAuthorized -ne $true) {
    Refuse('Owner authorization does not authorize publication.')
}
if ([string] $authorization.releaseCandidate -cne [string] $inventory.releaseCandidate.commit) {
    Refuse('Owner authorization release candidate mismatch.')
}
if ([string] $authorization.environment -cne [string] $inventory.environment) {
    Refuse('Owner authorization environment mismatch.')
}
if ([string] $authorization.destination -cne [string] $inventory.destination) {
    Refuse('Owner authorization destination mismatch.')
}
if ([string] $ReleaseCandidate -cne [string] $inventory.releaseCandidate.commit) { Refuse('Release-candidate identity mismatch.') }
if ([string] $SourceRunId -cne [string] $inventory.source.runId) { Refuse('Source run identity mismatch.') }
if ([string] $SourceRunAttempt -cne [string] $inventory.source.runAttempt) { Refuse('Source run attempt mismatch.') }
if ([string] $Environment -cne [string] $inventory.environment) { Refuse('Publication environment mismatch.') }
if ([string] $Destination -cne [string] $inventory.destination) { Refuse('Publication destination mismatch.') }
if ($plan.status -cne 'verified' -or [bool] $plan.dryRun -ne $false) { Refuse('Verified live publication plan is not in the expected state.') }
if ([string] $plan.inventorySha256 -cne $inventoryHash) { Refuse('Verified live publication plan is not bound to this inventory.') }
if ([string] $plan.authorizationId -cne $OwnerAuthorizationId) { Refuse('Verified live publication plan is not bound to this authorization.') }
if ([string] $plan.releaseCandidate -cne $ReleaseCandidate) { Refuse('Verified live publication plan release candidate mismatch.') }
if ([bool] $plan.uploadPerformed -ne $false -or [bool] $plan.credentialsRequested -ne $false) {
    Refuse('Verified live publication plan is not in the pre-publication state.')
}

$binding = [pscustomobject]@{
    inventoryId = [string] $inventory.inventoryId
    inventorySha256 = $inventoryHash
    authorizationId = $OwnerAuthorizationId
    releaseCandidate = [string] $inventory.releaseCandidate.commit
}
$store = New-PublicationJournalStore -Path $JournalPath -Repository $JournalRepository -Branch $JournalBranch -GitHubPath $JournalGitHubPath -RefBaseSha $JournalRefBaseSha
$journal = Read-PublicationJournal $store
if ($journal.exists) {
    $check = Test-PublicationJournalBinding $journal $binding
    if (!$check.valid) { Refuse("Existing durable journal is not consistent with this publication: $($check.error)") }
    Write-Host "PUBLICATION_JOURNAL=EXISTING_RECORDS=$($journal.records.Count)"
    exit 0
}

$record = Add-PublicationJournalRecord -Store $store -Binding $binding -Event 'journal-initialized' -State 'binding-verified' -Detail ([ordered]@{
    ownerAuthorizationId = $OwnerAuthorizationId
    workflowRunId = [string] $env:GITHUB_RUN_ID
    workflowRunAttempt = [string] $env:GITHUB_RUN_ATTEMPT
    workflowRef = [string] $env:GITHUB_REF
    workflowSha = [string] $env:GITHUB_SHA
    journalStore = $store.kind
})
Write-Host "PUBLICATION_JOURNAL=INITIALIZED_SEQUENCE=$($record.record.sequence)_HASH=$($record.hash)"
exit 0
