[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LockPath,
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

if (!$RepositoryRoot) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_REFUSED: $Message")
    exit 1
}

if (!(Test-Path -LiteralPath $LockPath -PathType Leaf)) {
    Refuse("Publication workflow lock is missing: $LockPath")
}
try {
    $lock = Get-Content -Raw -LiteralPath $LockPath | ConvertFrom-Json
}
catch {
    Refuse('Publication workflow lock is not valid JSON.')
}
if ([int] $lock.schemaVersion -ne 1) {
    Refuse('Publication workflow lock schema version is not 1.')
}
if ([string] $lock.workflowPath -cne '.github/workflows/publish.yml') {
    Refuse('Publication workflow lock does not name the canonical workflow.')
}
if (!$lock.approvedAtCommit -or !$lock.files -or @($lock.files).Count -eq 0) {
    Refuse('Publication workflow lock is incomplete.')
}

foreach ($item in @($lock.files)) {
    $path = [string] $item.path
    if (!$path) { Refuse('Publication workflow lock contains an empty path.') }
    $full = Join-Path $RepositoryRoot $path
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) {
        Refuse("Publication workflow lock file is missing: $path")
    }
    $blob = (& git -C $RepositoryRoot hash-object -- $path).Trim()
    if ($LASTEXITCODE -ne 0) {
        Refuse("Unable to hash the publication workflow revision: $path")
    }
    if ($blob -cne [string] $item.blobSha) {
        Refuse("Publication workflow revision is not the approved revision: $path")
    }
}

& git -C $RepositoryRoot merge-base --is-ancestor ([string] $lock.approvedAtCommit) HEAD
if ($LASTEXITCODE -ne 0) {
    Refuse('Publication workflow lock is not an ancestor of the invoked revision.')
}

Write-Host 'PUBLICATION_WORKFLOW_LOCK=VERIFIED'
exit 0
